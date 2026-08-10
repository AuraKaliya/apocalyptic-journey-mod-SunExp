using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class ProjectionTurnCoordinator
{
    private static int roundSequence;

    public static void BeginBattle(string source)
    {
        roundSequence = 0;
        TerriasLog.Debug("[PartnerTurn] companion queue reset from " + source);
    }

    public static void BeginPlayerRound(string source)
    {
        roundSequence = Math.Max(1, roundSequence + 1);
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.RoundStarted");
        TerriasLog.Debug("[PartnerTurn] native partner round prepared: round="
            + roundSequence
            + ", source="
            + source);
    }

    public static void RegisterProjection(
        ProjectionOtherObj projection,
        string source)
    {
        RegisterCompanion(projection, source);
    }

    public static void RegisterCompanion(OtherObj companion, string source)
    {
        if (companion == null || FightManager.Instance?.ActionQueue == null)
        {
            return;
        }

        var queue = FightManager.Instance.ActionQueue;
        queue.RemoveAll(item => item == null || ReferenceEquals(item, companion));
        var enemyIndex = queue.FindIndex(item => item is Enemy);
        if (enemyIndex < 0)
        {
            queue.Add(companion);
        }
        else
        {
            queue.Insert(enemyIndex, companion);
        }
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.NativePartnerQueued");
        TerriasLog.Info("[PartnerTurn] companion queued in native phase: status="
            + companion.InstanceId
            + ", source="
            + source);
    }

    public static void ClearBattle(string source)
    {
        roundSequence = 0;
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.Cleared");
        TerriasLog.Debug("[PartnerTurn] coordinator cleared from " + source);
    }
}
