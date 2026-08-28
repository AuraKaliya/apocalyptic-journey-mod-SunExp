using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal sealed class ReplayStableBarrierBatchV12
{
    public bool CaptureState { get; set; }

    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();

    public string Label => Reasons.Count == 0
        ? "StableBarrier"
        : "StableBarrier:" + string.Join("+", Reasons);
}

/// <summary>
/// Coalesces native action-completion signals into one next-frame reconciliation.
/// The coordinator owns scheduling state only; the recorder remains the owner of
/// Unity snapshots and the causal transaction ledger.
/// </summary>
internal sealed class ReplayStableBarrierCoordinatorV12
{
    private readonly HashSet<string> reasons = new(StringComparer.Ordinal);
    private bool scheduled;
    private bool captureState;

    internal bool IsPending => scheduled;

    internal bool Request(string reason, bool needsStateCapture)
    {
        var shouldSchedule = !scheduled;
        scheduled = true;
        captureState |= needsStateCapture;
        if (!string.IsNullOrWhiteSpace(reason)) reasons.Add(reason.Trim());
        return shouldSchedule;
    }

    internal bool TryTake(out ReplayStableBarrierBatchV12 batch)
    {
        if (!scheduled)
        {
            batch = new ReplayStableBarrierBatchV12();
            return false;
        }

        batch = new ReplayStableBarrierBatchV12
        {
            CaptureState = captureState,
            Reasons = reasons.OrderBy(value => value, StringComparer.Ordinal).ToArray()
        };
        Reset();
        return true;
    }

    internal void Reset()
    {
        scheduled = false;
        captureState = false;
        reasons.Clear();
    }
}
