using System;
using GoldExp.Dll.GameApi;
using GoldExp.Dll.Infrastructure;
using GoldExp.Dll.Mechanics;

namespace GoldExp.Dll.Scripting;

public static class CardScripts
{
    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "gold_dream_wager":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    ExecutorApi.AddDescription(self, "1", "Money", GoldDreamService.DynamicWagerLimit());
                    break;
                case "fortune_throw":
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    if (GoldDreamService.TryCanPayGold(self, 1000, out var canPay))
                    {
                        SetUsable(self, canPay);
                    }

                    break;
                default:
                    ExecutorApi.SetBaseScript(self, "CommonCardItem");
                    break;
            }
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Card Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            GoldDreamService.EnsureCombatHooks(self);
            switch (id)
            {
                case "gilded_amulet":
                    CardApi.EnsureHandTags(self, GoldExpIds.GoldDreamTag);
                    break;
                case "gold_dream_wager":
                    UseWager(self);
                    break;
                case "fortune_throw":
                    UseFortuneThrow(self);
                    break;
                case "false_gold_rain":
                    UseDisplayWealth(self);
                    break;
                case "blank_check":
                    GoldDreamService.AddFalseGold(self, 1000);
                    GoldDreamService.AddDebt(self, 2000);
                    if (GoldDreamService.HasGoldenPotential(self))
                    {
                        self.SetStatus("Self");
                        self.DrawCount("3");
                        self.ChangePower("2");
                    }

                    break;
                case "golden_age":
                    GoldDreamService.SettleFalseGoldToRealGold(self, 1, 2);
                    GoldDreamService.SetAllDebtCountdownToOne(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Card Use failed: " + id, ex);
        }
    }

    private static void UseWager(ScriptExecutor self)
    {
        var spend = GoldDreamService.PayRealGoldUpTo(self, GoldDreamService.DynamicWagerLimit());
        GoldDreamService.AddFalseGold(self, spend);
    }

    private static void UseFortuneThrow(ScriptExecutor self)
    {
        if (!GoldDreamService.PayGold(self, 1000))
        {
            return;
        }

        for (var i = 0; i < 6; i++)
        {
            if (UnityEngine.Random.value < 0.5f)
            {
                ExecutorApi.DealDamageRandomEnemy(self, 3, "True");
            }
        }
    }

    private static void UseDisplayWealth(ScriptExecutor self)
    {
        var count = CardApi.DiscardAllHand(self);
        for (var i = 0; i < count; i++)
        {
            CardApi.CreateCardInHand(self, GoldExpIds.WagerCardId, "Burnout");
        }
    }

    private static void SetUsable(ScriptExecutor self, bool usable)
    {
        var value = usable ? "1" : "0";
        DictionaryUtil.Set(self?.Vars, "Usable", value);
        DictionaryUtil.Set(self?.dataConfig?.Vars, "Usable", value);
    }
}
