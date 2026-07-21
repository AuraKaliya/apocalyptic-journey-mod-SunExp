using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class SolarCombatApi
{
    public static int SolarMultiplier(ScriptExecutor? executor)
    {
        return BuffApi.Has(executor?.Self, TerriasIds.SolarCrown) ? 2 : 1;
    }

    public static int SolarCoefficient(ScriptExecutor? executor, IStatusManager? target)
    {
        var radiance = BuffApi.Level(executor?.Self, TerriasIds.SolarRadiance);
        var flame = BuffApi.Level(executor?.Self, TerriasIds.GatheredFlame);
        var burn = BuffApi.Level(target, TerriasIds.Burn);
        return SolarMultiplier(executor) * (radiance * 2 + flame / 3 + burn / 2);
    }

    public static int SolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, int coefficientScale = 1)
    {
        return baseDamage + SolarCoefficient(executor, target) * coefficientScale;
    }

    public static int SolarKeywordBlock(ScriptExecutor? executor, int baseBlock)
    {
        return baseBlock + SolarCoefficient(executor, null);
    }

    public static bool DealSolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, string fallbackStatus = "Target", int coefficientScale = 1)
    {
        if (executor == null)
        {
            return false;
        }

        TargetApi.SetStatusForTarget(executor, target, fallbackStatus);
        return DamageApi.DealDamage(executor, SolarKeywordDamage(executor, baseDamage, target, coefficientScale));
    }

    public static int DealSolarKeywordDamageAllEnemies(ScriptExecutor? executor, int baseDamage, int coefficientScale)
    {
        var max = 0;
        foreach (var target in TargetApi.EnemyTargets(executor))
        {
            var damage = SolarKeywordDamage(executor, baseDamage, target, coefficientScale);
            max = Math.Max(max, damage);
            TargetApi.SetStatusForTarget(executor, target, "Target");
            DamageApi.DealDamage(executor, damage);
        }

        return max;
    }

    public static int ApplySolarKeywordSkill(ScriptExecutor? executor, int baseBlock)
    {
        if (executor == null)
        {
            return 0;
        }

        var block = SolarKeywordBlock(executor, baseBlock);
        if (block > 0)
        {
            executor.SetStatus("Self");
            executor.ChangeDefence(block.ToString());
        }

        return block;
    }
}
