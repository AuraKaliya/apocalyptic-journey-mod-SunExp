using System;

namespace Terrias.Dll.Mechanics;

public enum EndlessAbyssSettlementBarrierAction
{
    None,
    ForceCommit,
    Close
}

public static class EndlessAbyssSettlementBarrierPolicy
{
    public static EndlessAbyssSettlementBarrierAction Evaluate(
        bool hostReady,
        bool closingSent,
        bool allRemotePlayersCommitted,
        bool forceCommitSent,
        long hostDeadlineUtcTicks,
        long forcedCommitDeadlineUtcTicks,
        long nowUtcTicks)
    {
        if (!hostReady || closingSent)
        {
            return EndlessAbyssSettlementBarrierAction.None;
        }

        if (allRemotePlayersCommitted)
        {
            return EndlessAbyssSettlementBarrierAction.Close;
        }

        if (!forceCommitSent
            && hostDeadlineUtcTicks > 0
            && nowUtcTicks >= hostDeadlineUtcTicks)
        {
            return EndlessAbyssSettlementBarrierAction.ForceCommit;
        }

        if (forceCommitSent
            && forcedCommitDeadlineUtcTicks > 0
            && nowUtcTicks >= forcedCommitDeadlineUtcTicks)
        {
            return EndlessAbyssSettlementBarrierAction.Close;
        }

        return EndlessAbyssSettlementBarrierAction.None;
    }
}
