using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording;
using UnityEngine;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static partial class MatchReplayRecorder
{
    private static readonly Dictionary<int, string> ObservedHandViews = new();
    private static int handInputFeedbackDepth;

    internal static void PrepareHandHover(object? target)
    {
        handInputFeedbackDepth++;
        if (target is CardItem card && !card.draging && card.enabled && CardItem.canUse && !card.hasUse)
            PrepareHandInput(card);
    }

    internal static void FinishHandHover() => handInputFeedbackDepth = Math.Max(0, handInputFeedbackDepth - 1);

    // Native CardItem.OnBeginDrag stops its arrival/layout tween before input
    // starts moving the view. Finish that exact automatic lifetime at this
    // boundary; a long hold must not remain hidden inside an Arrival track.
    internal static void PrepareHandInput(object? target)
    {
        if (MatchReplaySessionState.IsPlayback || target is not CardItem card
            || FightUI.SelectedCard.Contains(card)) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            var existing = MotionFor(card);
            if (existing.Value == null || !existing.Value.IsHandMotion || existing.Value.NativeExitStarted) return;
            CompleteCardMotionObservationNoLock(existing.Key, existing.Value, ElapsedTicks(), "NativeInputTakeover");
        }
    }

    internal static void ObserveHandRequest(object? target)
    {
        if (MatchReplaySessionState.IsPlayback || target is not FightUI) return;
        lock (Gate)
        {
            // Queueing, an empty deck and a full hand do not create a visible
            // card. Reconcile counts at the existing coalesced state barrier.
            if (CanCaptureNoLock()) RequestStableBarrierNoLock("native-hand-request", needsStateCapture: true);
        }
    }

    // Native DrawEffect is after binding and before DrawScript can auto-use a
    // new card. It precedes insertion into FightUI.cardItemList.
    internal static void ObserveCardDraw(object? target)
    {
        if (MatchReplaySessionState.IsPlayback || target is not CardItem card) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock() || !IsNativeHandView(card)) return;
            ObserveHandViewNoLock(card);
            RequestStableBarrierNoLock("native-hand-arrival", needsStateCapture: true);
        }
    }

    internal static void ObserveHandCreated(object? target, object[]? arguments)
    {
        if (MatchReplaySessionState.IsPlayback || target is not FightUI ui) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock() || ui.cardContainer == null) return;
            foreach (var card in NativeHandViews(ui)) ObserveHandViewNoLock(card);
            var createdConfig = arguments?.FirstOrDefault();
            foreach (var card in ui.cardContainer.GetComponentsInChildren<CardItem>(true).Where(card => card != null
                         && card.dataConfig != null && ReferenceEquals(card.dataConfig, createdConfig)))
            {
                var motion = MotionFor(card);
                if (motion.Value?.AwaitingInitialCardBinding == true && PendingCardMotions.TryGetValue(motion.Key, out var pending))
                {
                    pending.Event.Presentation!.CardView = ReplayFactCaptureV17.CaptureCardView(card, catalog!);
                    pending.Event.Presentation.Value = pending.Event.Presentation.CardView.DisplayedCost;
                    motion.Value.AwaitingInitialCardBinding = false;
                }
            }
            RequestStableBarrierNoLock("native-hand-created", needsStateCapture: true);
        }
    }

    internal static void BeginHandLayout(object? target)
    {
        if (MatchReplaySessionState.IsPlayback || target is not FightUI ui) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock() || DecisionCapture.IsWaiting || nativeSelectionOpen && !nativeSelectionCommitted) return;
            foreach (var card in NativeHandViews(ui))
                if (!card.ignore && !card.draging) ObserveHandViewNoLock(card);
        }
    }

    internal static void EndHandLayout(object? target)
    {
        if (MatchReplaySessionState.IsPlayback || target is not FightUI) return;
        // Preserve each view's observed trajectory, but reconcile game state
        // once at the existing frame/action barrier. Nested native layouts do
        // not each need a full combat snapshot and a storage batch.
        lock (Gate)
            if (CanCaptureNoLock() && !DecisionCapture.IsWaiting && (!nativeSelectionOpen || nativeSelectionCommitted))
                RequestStableBarrierNoLock("native-hand-layout", needsStateCapture: true);
    }

    private static IEnumerable<CardItem> NativeHandViews(FightUI ui) =>
        (FightUI.cardItemList ?? new List<CardItem>()).Where(card => card != null
            && card.dataConfig != null && card.transform.parent == ui.cardContainer?.transform
            && !card.hasDone && card.gameObject.activeInHierarchy);

    private static bool IsNativeHandView(CardItem card)
    {
        var ui = card.GetComponentInParent<FightUI>();
        return card.dataConfig != null && ui?.cardContainer != null
            && card.transform.parent == ui.cardContainer.transform && card.gameObject.activeInHierarchy;
    }

    private static void ObserveHandViewNoLock(CardItem card)
    {
        if (card.dataConfig == null) return;
        var sourceId = card.dataConfig.InstanceID ?? "";
        var rootId = card.GetInstanceID();
        var arrival = !ObservedHandViews.TryGetValue(rootId, out var previous) || previous != sourceId;
        if (!arrival && (DecisionCapture.IsWaiting || nativeSelectionOpen && !nativeSelectionCommitted || card.draging)) return;
        ObservedHandViews[rootId] = sourceId;
        var existing = MotionFor(card);
        if (existing.Value != null)
        {
            CaptureCardMotionSampleNoLock(existing.Key, existing.Value, ElapsedTicks());
            return;
        }
        var source = ReplayFactCaptureV17.CaptureActionSource(card, catalog!);
        source.Kind = ReplayTransactionKindsV17.Passive;
        source.Label = arrival ? "CardArrival" : "HandLayout";
        var transaction = BeginSourceTransactionNoLock(source, pushContext: false);
        var key = StartMotionNoLock(sourceId, transaction, card, card.GetComponentInParent<FightUI>(),
            arrival ? ReplayHandLifecycleContractV17.Arrival : ReplayHandLifecycleContractV17.Layout, true);
        var observation = PendingCardMotionObservations[key];
        observation.IsHandMotion = true;
        observation.PointerReleased = !card.draging;
        observation.AwaitNativeHandSettled = true;
        observation.CaptureStateOnComplete = false;
        observation.AwaitingInitialCardBinding = arrival;
    }

    private static void SeedInitialHandViewsNoLock()
    {
        ObservedHandViews.Clear();
        foreach (var card in FightUI.cardItemList ?? new List<CardItem>())
            if (card != null && card.dataConfig != null && !card.hasDone)
                ObservedHandViews[card.GetInstanceID()] = card.dataConfig.InstanceID ?? "";
    }
}
