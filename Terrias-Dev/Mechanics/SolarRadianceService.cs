using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SolarRadianceService
{
    public static bool HandleSolarCardUsed(ScriptExecutor? executor, int cost, string source)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
        if (executor?.Self == null)
        {
            TerriasLog.Debug("HandleSolarCardUsed skipped: executor/self missing, source=" + source);
            return false;
        }

        var gain = cost < 0 ? 0 : cost;
        var before = BuffApi.Level(executor.Self, TerriasIds.SolarRadiance);
        var hasCrown = BuffApi.Has(executor.Self, TerriasIds.SolarCrown);
        TerriasLog.Debug("HandleSolarCardUsed enter source=" + source + ", cost=" + cost + ", hasCrown=" + hasCrown + ", radianceBefore=" + before);

        if (hasCrown)
        {
            var triggered = TriggerSolarCrown(executor, source);
            TerriasLog.Debug("HandleSolarCardUsed crown result=" + triggered + ", radianceAfter=" + BuffApi.Level(executor.Self, TerriasIds.SolarRadiance));
            return triggered;
        }

        if (gain <= 0)
        {
            TerriasLog.Debug("HandleSolarCardUsed skipped: gain<=0");
            return false;
        }

        executor.SetStatus("Self");
        executor.AddBuff(TerriasIds.SolarRadiance, gain.ToString());
        TerriasLog.Debug("HandleSolarCardUsed radiance added=" + gain + ", radianceAfter=" + BuffApi.Level(executor.Self, TerriasIds.SolarRadiance));
        return true;
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("SolarRadiance.HandleSolarCardUsed", start);
        }
    }

    private static bool TriggerSolarCrown(ScriptExecutor executor, string source)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
        if (executor.Self == null || !BuffApi.Has(executor.Self, TerriasIds.SolarCrown))
        {
            return false;
        }

        var tier = BuffApi.Level(executor.Self, TerriasIds.SolarCrownTier);
        TerriasLog.Debug("SolarCrown trigger source=" + source + ", tier=" + tier + ", effects=" + SolarCrownEffectSummary(tier));
        var effectCount = 0;

        if (tier >= 1)
        {
            effectCount++;
            var total = BuffApi.RemoveNegativeBuffsAndTotalExcept(
                executor,
                executor.Self,
                TerriasIds.GatheredFlame,
                TerriasIds.Burn,
                TerriasIds.BodyBurn);
            if (total > 0)
            {
                executor.SetStatus("Self");
                executor.AddBuff(TerriasIds.Burn, total.ToString());
            }
        }

        if (tier >= 2)
        {
            effectCount++;
            executor.SetStatus("Self");
            executor.DrawCount("1");
        }

        if (tier >= 3)
        {
            effectCount++;
            executor.SetStatus("Self");
            executor.ChangePower("1");
        }

        if (tier >= 4)
        {
            effectCount++;
            var burn = BuffApi.Level(executor.Self, TerriasIds.Burn);
            if (burn > 0)
            {
                executor.SetStatus("Self");
                executor.RemoveBuff(TerriasIds.Burn);
                executor.AddBuff(TerriasIds.GatheredFlame, burn.ToString());
            }
        }

        if (tier >= 5)
        {
            effectCount++;
            executor.SetStatus("AllTarget");
            executor.AddBuff(TerriasIds.Burn, "5");
            TriggerBurnAllEnemies(executor);
        }

        TerriasLog.Debug("SolarCrown triggered effectCount=" + effectCount + ", tier=" + tier + ", radiance=" + BuffApi.Level(executor.Self, TerriasIds.SolarRadiance));
        return true;
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("SolarRadiance.TriggerSolarCrown", start);
        }
    }

    private static string SolarCrownEffectSummary(int tier)
    {
        if (tier <= 0)
        {
            return "none";
        }

        var effects = "";
        AppendEffect(ref effects, tier >= 1, "T1:self negative buffs -> burn");
        AppendEffect(ref effects, tier >= 2, "T2:draw 1");
        AppendEffect(ref effects, tier >= 3, "T3:gain 1 mana");
        AppendEffect(ref effects, tier >= 4, "T4:self burn -> gathered flame");
        AppendEffect(ref effects, tier >= 5, "T5:all enemies gain 5 burn and trigger burn");
        return effects;
    }

    private static void AppendEffect(ref string effects, bool active, string text)
    {
        if (!active)
        {
            return;
        }

        effects = string.IsNullOrWhiteSpace(effects) ? text : effects + "; " + text;
    }

    private static void TriggerBurnAllEnemies(ScriptExecutor executor)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
        ExecutorApi.TriggerBurnAllEnemies(executor);
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("SolarRadiance.TriggerBurnAllEnemies", start);
        }
    }
}
