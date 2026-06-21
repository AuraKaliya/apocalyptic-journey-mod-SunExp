using System;
using StarExp.Dll.GameApi;
using StarExp.Dll.Infrastructure;
using StarExp.Dll.Mechanics;

namespace StarExp.Dll.Scripting;

public static class CardScripts
{
    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "morning_star_blade":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    ExecutorApi.AddDescription(self, "1", "Damage", MorningStarBladeDamage());
                    break;
                case "stargaze":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    ExecutorApi.AddDescription(self, "1", "Defence", 5);
                    break;
                case "ninth_attempt":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    ExecutorApi.AddDescription(self, "1", "Defence", 8 + StarMiracleService.BlackStonesThisRound() * 3);
                    break;
                case "restless_practice":
                    ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
                    ExecutorApi.AddDescription(self, "1", "Damage", 5 + StarMiracleService.BlackStonesThisCombat());
                    break;
                case "borrowed_miracle":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    break;
                default:
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    break;
            }
        }
        catch (Exception ex)
        {
            StarExpLog.Error("Card Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            StarMiracleService.EnsureCombatHooks(self);
            switch (id)
            {
                case "morning_star_blade":
                    UseMorningStarBlade(self);
                    break;
                case "stargaze":
                    self.SetStatus("Self");
                    self.ChangeDefence("5");
                    StarMiracleService.RemoveBlackStones(self, 1);
                    break;
                case "morning_star_guidance":
                    UseMorningStarGuidance(self);
                    break;
                case "clock_hand":
                    StarMiracleService.ReduceClock(self, 2, canWaiveDebt: true);
                    break;
                case "white_stone":
                    self.SetStatus("Self");
                    self.AddBuff(StarExpIds.WhiteStonePower, "1");
                    break;
                case "ninth_attempt":
                    self.SetStatus("Self");
                    self.ChangeDefence((8 + StarMiracleService.BlackStonesThisRound() * 3).ToString());
                    break;
                case "restless_practice":
                    UseRestlessPractice(self);
                    break;
                case "borrowed_miracle":
                    StarMiracleService.TriggerBorrowedMiracle(self);
                    break;
                case "repayment_night":
                    UseRepaymentNight(self);
                    break;
                case "star_thread_pull":
                    StarMiracleService.RemoveBlackStones(self, 2);
                    break;
                case "starlight_fragment":
                    StarMiracleService.AddStarlight(self, 1);
                    if (StarMiracleService.ClockDebt(self) <= 0)
                    {
                        self.SetStatus("Self");
                        self.DrawCount("1");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            StarExpLog.Error("Card Use failed: " + id, ex);
        }
    }

    private static int MorningStarBladeDamage()
    {
        return 6 + (StarMiracleService.BlackStonesThisRound() > 0 ? 2 : 0);
    }

    private static void UseMorningStarBlade(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        ExecutorApi.SetStatusForTarget(self, target, "Target");
        ExecutorApi.DealDamage(self, MorningStarBladeDamage());
    }

    private static void UseMorningStarGuidance(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.DrawCount("1");
        StarMiracleService.AddStarlight(self, 1);
        PlayerApi.ShowCaption("Guided card confirmed: White Stone.");
    }

    private static void UseRestlessPractice(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        ExecutorApi.SetStatusForTarget(self, target, "Target");
        ExecutorApi.DealDamage(self, 5 + StarMiracleService.BlackStonesThisCombat());
    }

    private static void UseRepaymentNight(ScriptExecutor self)
    {
        var debt = StarMiracleService.ClockDebt(self);
        if (debt <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        ExecutorApi.DealDamage(self, debt, "True");
        StarMiracleService.ClearDebt(self);
        self.DrawCount(debt.ToString());
    }
}
