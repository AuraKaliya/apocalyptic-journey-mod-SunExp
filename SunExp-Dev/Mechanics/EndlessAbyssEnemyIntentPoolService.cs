using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssEnemyIntentPoolService
{
    private const string IntentAppliedKey = "SunExpEndlessAbyssIntentApplied";

    private static readonly string[] IntentPool =
    {
        SunExpIds.AbyssLifeTheftEnemyCardId,
        SunExpIds.AbyssDeficitEnemyCardId
    };

    public static bool TryAddIntent(Enemy enemy, int floor, TongtianTowerNodeKind nodeKind, string source)
    {
        try
        {
            if (!ShouldApply(floor, nodeKind)
                || enemy?.Status == null
                || AlreadyApplied(enemy.Status))
            {
                return false;
            }

            var executor = enemy.Status.MirrorSc as ScriptExecutor;
            if (executor == null)
            {
                return false;
            }

            var cardId = IntentPool[PickIndex(IntentPool.Length, source + ":" + enemy.InstanceId)];
            if (!ExecutorApi.AddEnemyAction(executor, cardId))
            {
                return false;
            }

            MarkApplied(enemy.Status);
            SunExpLog.Info("[EndlessAbyssIntent] added " + cardId + " to enemy " + enemy.InstanceId + " from " + source + ".");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssIntent] failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    private static bool ShouldApply(int floor, TongtianTowerNodeKind nodeKind)
    {
        if (TongtianTowerRewardPlan.IsEndless(floor))
        {
            return true;
        }

        return nodeKind == TongtianTowerNodeKind.Boss;
    }

    private static bool AlreadyApplied(IStatusManager status)
    {
        return status is StatusManager concrete
            && concrete.dynamicVariables != null
            && concrete.dynamicVariables.TryGetValue(IntentAppliedKey, out var value)
            && value > 0;
    }

    private static void MarkApplied(IStatusManager status)
    {
        if (status is not StatusManager concrete)
        {
            return;
        }

        concrete.dynamicVariables ??= new Dictionary<string, float>();
        concrete.dynamicVariables[IntentAppliedKey] = 1;
    }

    private static int PickIndex(int count, string seed)
    {
        if (count <= 1)
        {
            return 0;
        }

        unchecked
        {
            var hash = 23;
            foreach (var ch in seed ?? "")
            {
                hash = hash * 31 + ch;
            }

            return Math.Abs(hash == int.MinValue ? int.MaxValue : hash) % count;
        }
    }
}
