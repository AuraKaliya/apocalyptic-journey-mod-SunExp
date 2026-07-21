using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssEnemyInjectionService
{
    private const string ExtraEnemyAppliedKey = "TerriasEndlessAbyssExtraEnemiesApplied";

    public static int TryInjectAfterFightInit(int floor, EndlessSeaNodeKind nodeKind, string source)
    {
        floor = Math.Max(1, floor);
        if (!EndlessSeaRewardPlan.IsEndless(floor))
        {
            return 0;
        }

        if (EnemyApi.IsClientOnlyDynamicEnemyObserver())
        {
            TerriasLog.Debug("[EndlessAbyssEnemyInjection] client observer skipped injection; floor="
                + floor
                + "; kind="
                + nodeKind
                + "; source="
                + source);
            return 0;
        }

        var manager = FightManager.Instance;
        if (manager == null)
        {
            return 0;
        }

        if (AlreadyInjected(manager, floor))
        {
            return 0;
        }

        var injected = 0;
        foreach (var request in Plan(nodeKind))
        {
            if (EnemyApi.AddDynamicEnemyAuthoritative(request.EnemyId, source + ":" + request.Source))
            {
                injected++;
            }
        }

        if (injected > 0)
        {
            MarkInjected(manager, floor);
            TerriasLog.Info("[EndlessAbyssEnemyInjection] injected "
                + injected
                + " extra enemies; floor="
                + floor
                + "; kind="
                + nodeKind
                + "; source="
                + source);
        }

        return injected;
    }

    private static IEnumerable<EnemyInjectionRequest> Plan(EndlessSeaNodeKind nodeKind)
    {
        if (nodeKind == EndlessSeaNodeKind.EndlessBoss)
        {
            yield return new EnemyInjectionRequest(EndlessSeaEnemyPool.PickSpecialBossEnemy(), "endless-boss-special");
            yield return new EnemyInjectionRequest(EndlessSeaEnemyPool.PickNormalBossEnemy(), "endless-boss-normal");
            yield break;
        }

        if (nodeKind == EndlessSeaNodeKind.Boss)
        {
            yield return new EnemyInjectionRequest(EndlessSeaEnemyPool.PickSpecialBossEnemy(), "endless-floor-boss");
            yield break;
        }

        if (nodeKind == EndlessSeaNodeKind.Elite)
        {
            yield return new EnemyInjectionRequest(EndlessSeaEnemyPool.PickNormalBossEnemy(), "endless-elite");
            yield break;
        }

        yield return new EnemyInjectionRequest(EndlessSeaEnemyPool.PickNormalBossEnemy(), "endless-normal");
    }

    private static bool AlreadyInjected(FightManager manager, int floor)
    {
        return manager.TempVarsMap != null
            && manager.TempVarsMap.TryGetValue(ExtraEnemyAppliedKey, out var appliedFloor)
            && appliedFloor == floor;
    }

    private static void MarkInjected(FightManager manager, int floor)
    {
        manager.TempVarsMap ??= new Dictionary<string, int>();
        manager.TempVarsMap[ExtraEnemyAppliedKey] = floor;
    }

    private readonly struct EnemyInjectionRequest
    {
        public EnemyInjectionRequest(string? enemyId, string source)
        {
            EnemyId = enemyId ?? "";
            Source = source ?? "";
        }

        public string EnemyId { get; }

        public string Source { get; }
    }
}
