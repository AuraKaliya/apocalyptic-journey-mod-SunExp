using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal sealed class MatchReplayBaselineGate
{
    private bool armed;

    internal bool IsCommitted { get; private set; }

    internal bool MaterializationObserved { get; private set; }

    internal bool CanCaptureTimeline => armed && IsCommitted;

    internal bool AwaitingMaterializedCommit => armed && MaterializationObserved && !IsCommitted;

    internal void Arm()
    {
        armed = true;
        IsCommitted = false;
        MaterializationObserved = false;
    }

    internal void MarkMaterialized() => MaterializationObserved = armed;

    internal bool TryCommit(Func<bool> capture)
    {
        if (!armed || !MaterializationObserved || IsCommitted || capture == null || !capture())
        {
            return false;
        }

        IsCommitted = true;
        return true;
    }

    internal void Reset()
    {
        armed = false;
        IsCommitted = false;
        MaterializationObserved = false;
    }
}
