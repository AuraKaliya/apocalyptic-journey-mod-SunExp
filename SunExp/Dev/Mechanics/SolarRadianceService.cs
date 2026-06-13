using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class SolarRadianceService
{
    public static bool HandleSolarCardUsed(ScriptExecutor? executor, int cost, string source)
    {
        if (executor?.Self == null)
        {
            SunExpLog.Debug("HandleSolarCardUsed skipped: executor/self missing, source=" + source);
            return false;
        }

        var gain = cost < 0 ? 0 : cost;
        var before = BuffApi.Level(executor.Self, SunExpIds.SolarRadiance);
        var hasCrown = BuffApi.Has(executor.Self, SunExpIds.SolarCrown);
        SunExpLog.Debug("HandleSolarCardUsed enter source=" + source + ", cost=" + cost + ", hasCrown=" + hasCrown + ", radianceBefore=" + before);

        if (hasCrown)
        {
            var triggered = TriggerSolarCrown(executor);
            SunExpLog.Debug("HandleSolarCardUsed crown result=" + triggered + ", radianceAfter=" + BuffApi.Level(executor.Self, SunExpIds.SolarRadiance));
            return triggered;
        }

        if (gain <= 0)
        {
            SunExpLog.Debug("HandleSolarCardUsed skipped: gain<=0");
            return false;
        }

        executor.SetStatus("Self");
        executor.AddBuff(SunExpIds.SolarRadiance, gain.ToString());
        SunExpLog.Debug("HandleSolarCardUsed radiance added=" + gain + ", radianceAfter=" + BuffApi.Level(executor.Self, SunExpIds.SolarRadiance));
        return true;
    }

    private static bool TriggerSolarCrown(ScriptExecutor executor)
    {
        if (executor.Self == null || !BuffApi.Has(executor.Self, SunExpIds.SolarCrown))
        {
            return false;
        }

        var tier = BuffApi.Level(executor.Self, SunExpIds.SolarCrownTier);
        var effectCount = 0;

        if (tier >= 1)
        {
            effectCount++;
            var total = BuffApi.NegativeTotal(executor.Self);
            if (total > 0)
            {
                BuffApi.RemoveNegativeBuffs(executor, executor.Self);
                executor.SetStatus("Self");
                executor.AddBuff(SunExpIds.Burn, total.ToString());
            }
        }

        if (tier >= 2)
        {
            effectCount++;
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
            var burn = BuffApi.Level(executor.Self, SunExpIds.Burn);
            if (burn > 0)
            {
                executor.SetStatus("Self");
                executor.RemoveBuff(SunExpIds.Burn);
                executor.AddBuff(SunExpIds.GatheredFlame, burn.ToString());
            }
        }

        if (tier >= 5)
        {
            effectCount++;
            executor.SetStatus("AllTarget");
            executor.AddBuff(SunExpIds.Burn, "5");
            TriggerBurnAllEnemies(executor);
        }

        SunExpLog.Debug("SolarCrown triggered effectCount=" + effectCount + ", tier=" + tier + ", radiance=" + BuffApi.Level(executor.Self, SunExpIds.SolarRadiance));
        return true;
    }

    private static void TriggerBurnAllEnemies(ScriptExecutor executor)
    {
        var targets = executor.SetStatus("AllTarget");
        if (targets != null)
        {
            foreach (var target in targets)
            {
                BuffApi.ConsumeEmberBeforeBurn(executor, target);
            }
        }

        executor.SetStatus("AllTarget");
        executor.RunImmediately(SunExpIds.Burn, "StartRound");
    }
}
