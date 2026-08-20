using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssBlessingService
{
    private const string OpeningStacksAppliedKey = "TerriasEndlessAbyssBlessingOpeningStacksApplied";

    private static readonly (string BuffId, int Amount)[] BlessingPool =
    {
        ("buff_resilient", 1),
        ("buff_keenedge", 1),
        ("buff_vitality", 1),
        ("buff_rebirth", 5),
        ("buff_extraordinary", 10)
    };

    public static void Apply(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Buff.AbyssBlessing");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            ResolveStartRound(self);
        }), "abyss_blessing");
        scope.Commit();
    }

    public static void Clear(ScriptExecutor self)
    {
        ScriptEventApi.InvalidateFightScope(self, "Buff.AbyssBlessing");
    }

    public static void ApplyOpeningStacks(Enemy enemy, string source)
    {
        var stacks = Math.Max(0, EndlessAbyssGazeService.CurrentLevel());
        if (stacks <= 0 || enemy?.Status == null || AlreadyApplied(enemy.Status, stacks))
        {
            return;
        }

        enemy.Status.AddBuff(TerriasIds.AbyssBlessingBuff, stacks);
        MarkApplied(enemy.Status, stacks);
        TerriasLog.Debug("[EndlessAbyssBlessing] applied "
            + stacks
            + " to enemy "
            + enemy.InstanceId
            + " from "
            + source
            + ".");
    }

    private static bool AlreadyApplied(IStatusManager status, int stacks)
    {
        return status is StatusManager concrete
            && concrete.dynamicVariables != null
            && concrete.dynamicVariables.TryGetValue(OpeningStacksAppliedKey, out var value)
            && value >= stacks;
    }

    private static void MarkApplied(IStatusManager status, int stacks)
    {
        if (status is not StatusManager concrete)
        {
            return;
        }

        concrete.dynamicVariables ??= new System.Collections.Generic.Dictionary<string, float>();
        concrete.dynamicVariables[OpeningStacksAppliedKey] = stacks;
    }

    private static void ResolveStartRound(ScriptExecutor self)
    {
        var stacks = ExecutorApi.SelfBuffLevel(self, TerriasIds.AbyssBlessingBuff);
        if (stacks <= 0)
        {
            return;
        }

        for (var i = 0; i < stacks; i++)
        {
            var entry = BlessingPool[UnityEngine.Random.Range(0, BlessingPool.Length)];
            self.SetStatus("Self");
            self.AddBuff(entry.BuffId, entry.Amount.ToString());
        }
    }
}
