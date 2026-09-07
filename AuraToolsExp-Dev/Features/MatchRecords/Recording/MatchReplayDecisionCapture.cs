using System;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using Witch.UI;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static partial class MatchReplayRecorder
{
    private static readonly ReplayDecisionCaptureV17 DecisionCapture = new();
    private static bool nativeSelectionOpen;
    private static bool nativeSelectionCommitted;

    // Run after native Update/coroutines and the existing capture work. Readiness
    // comes from native input/queue/lifecycle state, never an elapsed-idle timeout.
    internal static void ObserveDecisionReadiness()
    {
        try
        {
            lock (Gate)
            {
                if (!CanCaptureNoLock() || TerminalGate.SettlementPrepared) return;
                var ui = UIManager.Instance?.GetUI<FightUI>("FightUI");
                var ready = "";
                if (ui != null && FightManager.Instance != null)
                {
                    var settling = ui.NowAnimation || ui.animationQueue.Count != 0 || ui.createCardQueue.Count != 0
                        || PendingActionObservations.Count != 0 || PendingCardMotionObservations.Count != 0;
                    if (nativeSelectionOpen && FightUI.InIEn && !nativeSelectionCommitted
                        && !ReplayDecisionInputApi.SelectionConfirmed(ui) && !settling)
                        ready = ReplayDecisionTimelineV17.Selection;
                    else if (!FightUI.InIEn && FightManager.Instance.fightType == FightType.Player
                        && CardItem.canUse && ui.turnButton != null && ui.turnButton.gameObject.activeInHierarchy
                        && ui.turnButton.isInteractable && !settling && ContextStack.Count == 0
                        && FightUI.WaitCard.Count == 0 && !Transactions.Values.Any(item => !item.SourceCompleted))
                        ready = ReplayDecisionTimelineV17.Player;
                    else if (!FightUI.InIEn && FightManager.Instance.fightType == FightType.OtherTurn
                        && !settling && ContextStack.Count == 0 && !Transactions.Values.Any(item => !item.SourceCompleted))
                        ready = ReplayDecisionTimelineV17.OtherPlayers;
                }
                DecisionCapture.Observe(ready, ElapsedTicks(), EmitDecisionBoundaryNoLock);
            }
        }
        catch (Exception ex)
        {
            MarkCaptureFailure("decision-input-readiness", ex);
            AuraToolsLog.Error("[MatchRecords] decision input observation failed", ex);
        }
    }

    internal static void ObserveSelectionOpened(object? target)
    {
        if (target is not FightUI || !FightUI.InIEn) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            InterruptDecisionWaitNoLock();
            nativeSelectionOpen = true;
            nativeSelectionCommitted = false;
        }
    }

    internal static void ObserveSelectionConfirmed(object? target)
    {
        if (target is not FightUI ui) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock() || !nativeSelectionOpen || nativeSelectionCommitted
                || !FightUI.InIEn || !ReplayDecisionInputApi.SelectionConfirmed(ui)) return;
            nativeSelectionCommitted = true;
            DecisionCapture.Commit(ReplayDecisionTimelineV17.Selection,
                "selection-" + (builder!.LastSequence + 1), ElapsedTicks(), EmitDecisionBoundaryNoLock);
        }
    }

    internal static void ObserveSelectionReset()
    {
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            InterruptDecisionWaitNoLock();
            nativeSelectionOpen = false;
            nativeSelectionCommitted = false;
        }
    }

    internal static void ObserveEndTurn(object? target)
    {
        if (target is not FightUI || FightManager.Instance == null
            || FightManager.Instance.fightType != FightType.Player) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            DecisionCapture.Commit("EndTurn", "end-turn-" + roundSequence,
                ElapsedTicks(), EmitDecisionBoundaryNoLock);
        }
    }

    private static void ObserveAcceptedSourceNoLock(ReplayCapturedActionSourceV17 source)
    {
        if (source.Kind is not ReplayTransactionKindsV17.Card and not ReplayTransactionKindsV17.Skill
            || ContextStack.Count != 0 || FightUI.InIEn || FightManager.Instance == null
            || FightManager.Instance.fightType != FightType.Player) return;
        var ui = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (ui == null || ui.createCardQueue.Count != 0 || ui.turnButton == null
            || !ui.turnButton.isInteractable) return;
        DecisionCapture.Commit(source.Kind, source.SourceInstanceId, ElapsedTicks(), EmitDecisionBoundaryNoLock);
    }

    private static void InterruptDecisionWaitNoLock() => DecisionCapture.Close(
        ReplayDecisionTimelineV17.Interrupted, ElapsedTicks(), EmitDecisionBoundaryNoLock);

    private static void EmitDecisionBoundaryNoLock(string eventType, string kind, string id, long ticks)
    {
        // A tiny completed system transaction participates in the one journal and
        // its durability prefix. No mutable long-lived wait event or second writer.
        var transaction = builder!.StartTransaction(ReplayTransactionKindsV17.SystemPhase, ticks,
            roundSequence, actorTurnSequence, label: "InputBoundary");
        builder.AddPresentation(transaction, eventType, new ReplayPresentationMessageV17
        {
            Kind = kind,
            SourceInstanceId = id,
            DurationTicks = 0
        }, ticks);
        builder.CompleteTransaction(transaction, ticks);
        QueueCaptureBatchNoLock();
    }
}
