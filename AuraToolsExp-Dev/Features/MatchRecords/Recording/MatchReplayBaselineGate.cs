using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal sealed class MatchReplayBaselineGate
{
    private bool armed;

    internal bool IsCommitted { get; private set; }

    internal bool CanCaptureTimeline => armed && IsCommitted;

    internal void Arm()
    {
        armed = true;
        IsCommitted = false;
    }

    internal bool TryCommit(Func<bool> capture)
    {
        if (!armed || IsCommitted || capture == null || !capture())
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
    }
}
