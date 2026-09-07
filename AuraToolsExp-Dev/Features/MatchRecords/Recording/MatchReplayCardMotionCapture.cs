using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording;
using UnityEngine;
using DG.Tweening;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static partial class MatchReplayRecorder
{
    private static readonly Dictionary<string, Stack<string>> NativeCardCreates = new(StringComparer.Ordinal);

    internal static void BeginCardDrag(object? target)
    {
        if (MatchReplaySessionState.IsPlayback || target is not CardItem card || !card.draging) return;
        lock (Gate) { if (CanCaptureNoLock()) EnsureHandMotionNoLock(card, released: false); }
    }

    internal static void EndCardDrag(object? target)
    {
        if (target is not CardItem card) return;
        lock (Gate)
        {
            var pair = MotionFor(card);
            if (pair.Value == null) return;
            pair.Value.PointerReleased = true;
            CaptureCardMotionSampleNoLock(pair.Key, pair.Value, ElapsedTicks());
        }
    }

    private static KeyValuePair<string, CardMotionObservation> MotionFor(CardItem card) =>
        PendingCardMotionObservations.FirstOrDefault(pair => ReferenceEquals(pair.Value.Visual, card));

    private static void EnsureHandMotionNoLock(CardItem card, bool released)
    {
        if (card.dataConfig == null || builder == null || !card.gameObject.activeInHierarchy) return;
        var existing = MotionFor(card);
        if (existing.Value != null)
        {
            existing.Value.PointerReleased = released;
            existing.Value.CaptureStateOnComplete = true;
            return;
        }
        var source = ReplayFactCaptureV17.CaptureActionSource(card, catalog!);
        source.Kind = ReplayTransactionKindsV17.Passive;
        source.Label = "HandCardMotion";
        var transactionId = BeginSourceTransactionNoLock(source, pushContext: false);
        var key = StartMotionNoLock(source.SourceInstanceId, transactionId, card, card.GetComponentInParent<FightUI>(), "Hand", true);
        var observation = PendingCardMotionObservations[key];
        observation.IsHandMotion = true;
        observation.PointerReleased = released;
    }

    private static string OpenCardTransaction(string sourceId) => ContextStack.LastOrDefault(id =>
        Transactions.TryGetValue(id, out var entry) && entry.Source.SourceInstanceId == sourceId && builder!.IsOpen(id))
        ?? Transactions.Values.Where(item => !item.SourceCompleted && item.Source.SourceInstanceId == sourceId && builder!.IsOpen(item.TransactionId))
            .OrderByDescending(item => item.TransactionId, StringComparer.Ordinal).Select(item => item.TransactionId).FirstOrDefault() ?? "";

    private static string StartMotionNoLock(string sourceId, string transactionId, CardItem? card, FightUI? fightUi, string kind, bool owns)
    {
        var ticks = ElapsedTicks();
        var motion = RequireCardMotionPresentationNoLock(transactionId, sourceId, ticks);
        var key = "card-visual:" + motion.Sequence;
        motion.Presentation!.VisualInstanceId = key;
        motion.Presentation.Kind = kind;
        if (card?.dataConfig != null)
        {
            motion.Presentation.Value = NativeDisplayedCost(card.dataConfig);
            motion.Presentation.CardView = ReplayFactCaptureV17.CaptureCardView(card, catalog!);
        }
        PendingCardMotions[key] = new PendingPresentationTiming(ticks, motion);
        var observation = new CardMotionObservation(ticks, fightUi,
            card == null && fightUi != null
                ? fightUi.GetComponentsInChildren<CardItem>(true).Where(item => item != null).Select(item => item.GetInstanceID()).ToHashSet()
                : new HashSet<int>())
        {
            Visual = card, SourceInstanceId = sourceId, OwnsTransaction = owns, TransactionId = transactionId
        };
        PendingCardMotionObservations[key] = observation;
        if (card != null)
        {
            CaptureCardMotionSampleNoLock(key, observation, ticks);
            ScheduleCardMotionObservationNoLock(key, observation);
        }
        return key;
    }

    internal static void BeginNativeCardMotion(object? target, object[]? arguments)
    {
        if (MatchReplaySessionState.IsPlayback || arguments == null || arguments.Length == 0) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            var config = NativeCardData(arguments[0]);
            if (config == null) return;
            var id = config.InstanceID ?? "";
            var transaction = OpenCardTransaction(id);
            if (transaction.Length == 0) return;
            var key = StartMotionNoLock(id, transaction, null, target as FightUI, NativeCardMotionKind(arguments), false);
            PendingCardMotions[key].Event.Presentation!.Value = NativeDisplayedCost(config);
            if (!NativeCardCreates.TryGetValue(id, out var pending)) NativeCardCreates[id] = pending = new Stack<string>();
            pending.Push(key);
        }
    }

    internal static void EndNativeCardMotion(object? target, object[]? arguments)
    {
        if (arguments == null || arguments.Length == 0) return;
        lock (Gate)
        {
            var config = NativeCardData(arguments[0]);
            var id = config?.InstanceID ?? "";
            if (!NativeCardCreates.TryGetValue(id, out var pending) || pending.Count == 0) return;
            var key = pending.Pop();
            if (pending.Count == 0) NativeCardCreates.Remove(id);
            if (!PendingCardMotionObservations.TryGetValue(key, out var observation)) return;
            var centre = (target as FightUI)?.transform.Find("CenterCardContainer");
            // The native method assigns dataConfig only when needInit=true.
            // Otherwise its one new direct centre child is still the exact
            // visual owned by this call; a null config is not a missing view.
            var candidates = (target as FightUI)?.GetComponentsInChildren<CardItem>(true)
                .Where(item => item != null && item.transform.parent == centre
                    && !observation.ExistingInstanceIds.Contains(item.GetInstanceID())
                    && (item.dataConfig == null || string.Equals(item.dataConfig.InstanceID, id, StringComparison.Ordinal))
                    && !PendingCardMotionObservations.Values.Any(other => ReferenceEquals(other.Visual, item)))
                .ToArray() ?? Array.Empty<CardItem>();
            observation.Visual = candidates.Length == 1 ? candidates[0] : null;
            if (observation.Visual == null)
            {
                AddDiagnosticNoLock("native-card-motion-visual-missing:" + id);
                CompleteCardMotionObservationNoLock(key, observation, ElapsedTicks(), "MissingVisual");
                return;
            }
            CaptureCardMotionSampleNoLock(key, observation, ElapsedTicks());
            ScheduleCardMotionObservationNoLock(key, observation);
        }
    }

    internal static void ObserveNativeCardExitMotion(object? target, string kind)
    {
        if (MatchReplaySessionState.IsPlayback || target is not CardItem card) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            // Synchronous centre-card exit setup belongs to the enclosing
            // DoCardUseAnimation observation. Its after hook will bind the new
            // view; creating an exit observation here would claim it twice.
            if (NativeCardCreates.Values.Where(stack => stack.Count > 0).Select(stack => stack.Peek())
                .Any(key => PendingCardMotionObservations.TryGetValue(key, out var creating)
                    && card.transform.parent == creating.FightUi?.transform.Find("CenterCardContainer")
                    && !creating.ExistingInstanceIds.Contains(card.GetInstanceID()))) return;
            var existing = MotionFor(card);
            if (existing.Value != null)
            {
                if (!existing.Value.NativeExitStarted) existing.Value.NativeExitStartedTicks = ElapsedTicks();
                existing.Value.NativeExitStarted = true;
                existing.Value.CaptureStateOnComplete = true;
                existing.Value.PointerReleased = true;
                CaptureCardMotionSampleNoLock(existing.Key, existing.Value, ElapsedTicks());
                return;
            }
            if (card.dataConfig == null) return;
            var sourceId = card.dataConfig.InstanceID ?? "";
            var transaction = OpenCardTransaction(sourceId);
            var owns = transaction.Length == 0;
            if (owns)
            {
                var source = ReplayFactCaptureV17.CaptureActionSource(card, catalog!);
                source.Kind = ReplayTransactionKindsV17.Passive;
                source.Label = "CardExit:" + kind;
                transaction = BeginSourceTransactionNoLock(source, pushContext: false);
            }
            var key = StartMotionNoLock(sourceId, transaction, card, card.GetComponentInParent<FightUI>(), kind, owns);
            PendingCardMotionObservations[key].NativeExitStarted = true;
            PendingCardMotionObservations[key].NativeExitStartedTicks = ElapsedTicks();
        }
    }

    private static bool IsAtHandRest(CardItem card)
    {
        var rect = card.GetComponent<RectTransform>();
        return rect != null && (rect.anchoredPosition - (Vector2)card.initPosition).sqrMagnitude < 0.0001f
            && Mathf.Abs(rect.localScale.x - card.initScale) < 0.00001f
            && Mathf.Abs(Mathf.DeltaAngle(rect.localEulerAngles.z, card.initAngle.z)) < 0.001f;
    }

    private static bool NativeHandAnimationsSettled(CardItem card)
    {
        if (!card.enabled || card.draging) return false;
        if (card.cardcontainer?.cardTweenDict.TryGetValue(card, out var layout) == true
            && layout != null && layout.IsActive() && !layout.IsComplete()) return false;
        var move = card.animationController?.moveTween;
        var scale = card.animationController?.scaleTween;
        return !(move != null && move.IsActive() && !move.IsComplete())
            && !(scale != null && scale.IsActive() && !scale.IsComplete());
    }
}
