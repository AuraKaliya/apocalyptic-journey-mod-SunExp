using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssBlessingService
{
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
        var token = ExecutorApi.RegisterHook(self, "SunExpAbyssBlessingHook", "SunExpAbyssBlessingToken");
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpAbyssBlessingToken", token, new Action(() =>
        {
            ResolveStartRound(self);
        }), "abyss_blessing");
    }

    public static void Clear(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "SunExpAbyssBlessingHook", "SunExpAbyssBlessingToken");
    }

    public static void ApplyOpeningStacks(Enemy enemy, string source)
    {
        var stacks = Math.Max(0, EndlessAbyssGazeService.CurrentLevel());
        if (stacks <= 0 || enemy?.Status == null)
        {
            return;
        }

        enemy.Status.AddBuff(SunExpIds.AbyssBlessingBuff, stacks);
        SunExpLog.Debug("[EndlessAbyssBlessing] applied "
            + stacks
            + " to enemy "
            + enemy.InstanceId
            + " from "
            + source
            + ".");
    }

    private static void ResolveStartRound(ScriptExecutor self)
    {
        var stacks = ExecutorApi.SelfBuffLevel(self, SunExpIds.AbyssBlessingBuff);
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
