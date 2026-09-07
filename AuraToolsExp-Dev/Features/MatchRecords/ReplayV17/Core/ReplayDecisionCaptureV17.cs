using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

/// <summary>One input-wait owner; boundaries are immutable as soon as emitted.</summary>
internal sealed class ReplayDecisionCaptureV17
{
    private int sequence;
    private string waitId = "";
    internal string WaitingKind { get; private set; } = "";
    internal bool IsWaiting => waitId.Length != 0;

    internal void Observe(string readyKind, long ticks, Action<string, string, string, long> emit)
    {
        if (readyKind == WaitingKind) return;
        Close(ReplayDecisionTimelineV17.Interrupted, ticks, emit);
        if (string.IsNullOrEmpty(readyKind)) return;
        waitId = "input-wait-" + (++sequence).ToString("D8");
        WaitingKind = readyKind;
        emit(ReplayEventTypesV17.InputWaitStarted, readyKind, waitId, ticks);
    }

    internal void Commit(string kind, string sourceId, long ticks, Action<string, string, string, long> emit)
    {
        Close(ReplayDecisionTimelineV17.Committed, ticks, emit);
        emit(ReplayEventTypesV17.DecisionCommitted, kind, sourceId, ticks);
    }

    internal void Close(string reason, long ticks, Action<string, string, string, long> emit)
    {
        if (!IsWaiting) return;
        var id = waitId;
        waitId = "";
        WaitingKind = "";
        emit(ReplayEventTypesV17.InputWaitEnded, reason, id, ticks);
    }

    internal void Reset() { sequence = 0; waitId = ""; WaitingKind = ""; }
}
