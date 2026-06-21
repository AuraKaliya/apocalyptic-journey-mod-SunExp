using System;
using GoldExp.Dll.GameApi;
using GoldExp.Dll.Infrastructure;
using GoldExp.Dll.Mechanics;

namespace GoldExp.Dll.Scripting;

public static class PartnerScripts
{
    public static void Fight(ScriptExecutor self, string id)
    {
        try
        {
            if (id == "midas_raven")
            {
                self.SetStatus("Self");
                self.AddBuff(GoldExpIds.MidasRavenTrait, "1");
            }
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Partner Fight failed: " + id, ex);
        }
    }

    public static void RegisterMidasRaven(ScriptExecutor self)
    {
        try
        {
            ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
            {
                ExecutorApi.CombatIntSet("GoldExpMidasRavenRound", 0);
            }), "midas_raven");

            ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
            {
                var round = ExecutorApi.CombatIntGet("GoldExpMidasRavenRound") + 1;
                ExecutorApi.CombatIntSet("GoldExpMidasRavenRound", round);
                if (round % 2 != 0 || GoldDreamService.FalseGold(self) < 4)
                {
                    return;
                }

                GoldDreamService.ConsumeFalseGold(self, 2);
                ExecutorApi.DealDamageRandomEnemy(self, 10);
                self.SetStatus("Self");
                self.ChangeDefence("4");
                if (GoldDreamService.Debt(self) > 0)
                {
                    self.DrawCount("1");
                    GoldDreamService.AddDebt(self, 1);
                }
            }), "midas_raven");
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("RegisterMidasRaven failed", ex);
        }
    }

    public static void ClearMidasRaven(ScriptExecutor self)
    {
        ExecutorApi.CombatIntSet("GoldExpMidasRavenRound", 0);
    }
}
