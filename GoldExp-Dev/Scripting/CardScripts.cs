using System;
using GoldExp.Dll.GameApi;
using GoldExp.Dll.Infrastructure;
using GoldExp.Dll.Mechanics;

namespace GoldExp.Dll.Scripting;

public static class CardScripts
{
    private const string FortuneThrowAscensionVar = "GoldExpFortuneThrowAscension";
    private const int FortuneThrowBaseDamage = 3;
    private const int FortuneThrowDamageStep = 3;
    private const int FortuneThrowCheckValue = 50;

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
                    ExecutorApi.AddDescription(self, "1", "TrueDamage", FortuneThrowDamage(self));
                    ExecutorApi.AddDescription(self, "2", "Value", FortuneThrowCheckValue);
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
            var check = self.CheckDice.Roll().Value;
            if (check >= FortuneThrowCheckValue)
            {
                ExecutorApi.DealDamageRandomEnemy(self, FortuneThrowDamage(self), "True");
                if (check > 100)
                {
                    ExecutorApi.DealDamageRandomEnemy(self, FortuneThrowDamage(self), "True");
                }
            }
        }

        AscendFortuneThrow(self);
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

    private static int FortuneThrowDamage(ScriptExecutor self)
    {
        return FortuneThrowBaseDamage + FortuneThrowAscensions(self) * FortuneThrowDamageStep;
    }

    private static int FortuneThrowAscensions(ScriptExecutor self)
    {
        var value = DictionaryUtil.Get(
            self?.Vars,
            FortuneThrowAscensionVar,
            DictionaryUtil.Get(self?.dataConfig?.Vars, FortuneThrowAscensionVar, "0"));
        return Math.Max(0, DictionaryUtil.ParseInt(value));
    }

    private static void AscendFortuneThrow(ScriptExecutor self)
    {
        var ascensions = FortuneThrowAscensions(self) + 1;
        DictionaryUtil.Set(self?.Vars, FortuneThrowAscensionVar, ascensions);
        DictionaryUtil.Set(self?.dataConfig?.Vars, FortuneThrowAscensionVar, ascensions);
    }
}
