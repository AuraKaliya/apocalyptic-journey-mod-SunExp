namespace Terrias.Dll.Mechanics;

public static class ProjectionSummonTurnBarrierPolicy
{
    public static bool ShouldAcquire(
        int currentRound,
        int playerTurnCompletedRound,
        int openTransactionCount,
        bool continuationActive,
        bool battleActive)
    {
        return !continuationActive
               && battleActive
               && currentRound > 0
               && playerTurnCompletedRound == currentRound
               && openTransactionCount > 0;
    }
}
