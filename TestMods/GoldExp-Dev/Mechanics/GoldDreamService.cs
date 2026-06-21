using System;
using GoldExp.Dll.GameApi;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll.Mechanics;

public static class GoldDreamService
{
    private static readonly string[] DebtIds =
    {
        GoldExpIds.DebtDue,
        GoldExpIds.DebtNext,
        GoldExpIds.DebtLater
    };

    public static int FalseGold(ScriptExecutor self)
    {
        return ExecutorApi.SelfBuffLevel(self, GoldExpIds.FalseGold);
    }

    public static int Debt(ScriptExecutor self)
    {
        return DebtDue(self) + DebtNext(self) + DebtLater(self);
    }

    public static int DebtDue(ScriptExecutor self)
    {
        return ExecutorApi.SelfBuffLevel(self, GoldExpIds.DebtDue);
    }

    public static int DebtNext(ScriptExecutor self)
    {
        return ExecutorApi.SelfBuffLevel(self, GoldExpIds.DebtNext);
    }

    public static int DebtLater(ScriptExecutor self)
    {
        return ExecutorApi.SelfBuffLevel(self, GoldExpIds.DebtLater);
    }

    public static int DynamicWagerLimit()
    {
        return Math.Max(0, PlayerApi.GetMoney() / 10 + 100);
    }

    public static bool HasGoldenPotential(ScriptExecutor self)
    {
        return BuffApi.Has(self?.Self, GoldExpIds.GoldenPotential);
    }

    public static void ApplyGoldenPotentialAtFightStart()
    {
        try
        {
            BuffApi.Clear(FightPlayer.Instance?.Status, GoldExpIds.GoldenPotential);
            if (PlayerApi.GetMoney() <= 2000)
            {
                return;
            }

            FightPlayer.Instance?.Status?.AddBuff(GoldExpIds.GoldenPotential, 1);
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Apply Golden Potential failed", ex);
        }
    }

    public static void EnsureCombatHooks(ScriptExecutor self)
    {
        if (ExecutorApi.CombatIntGet(GoldExpIds.CoreHooksRegistered) == 1)
        {
            return;
        }

        var registered = ExecutorApi.TryAddEvent(self, "StartRound", new Action(() => OnStartRound(self)), "goldexp_core");
        ExecutorApi.TryAddEvent(self, "Win", new Action(() => EndCombatCleanup(self)), "goldexp_core");
        ExecutorApi.TryAddEvent(self, "Escape", new Action(() => EndCombatCleanup(self)), "goldexp_core");
        if (registered)
        {
            ExecutorApi.CombatIntSet(GoldExpIds.CoreHooksRegistered, 1);
        }
    }

    public static void AddFalseGold(ScriptExecutor self, int amount, bool countAsRoundGain = true)
    {
        if (amount <= 0)
        {
            return;
        }

        EnsureCombatHooks(self);
        var before = ExecutorApi.CombatIntGet(GoldExpIds.RoundFalseGoldGained);
        self.SetStatus("Self");
        self.AddBuff(GoldExpIds.FalseGold, amount.ToString());
        if (countAsRoundGain)
        {
            ExecutorApi.CombatIntSet(GoldExpIds.RoundFalseGoldGained, before + amount);
        }

        RefreshGoldPaymentCards(self);
    }

    public static void AddDebt(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        EnsureCombatHooks(self);
        AddDebtWithCountdown(self, 3, amount);
        TryTriggerDebtBonus(self);
    }

    public static void AddDebtWithCountdown(ScriptExecutor self, int countdown, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var buffId = countdown <= 1
            ? GoldExpIds.DebtDue
            : countdown == 2
                ? GoldExpIds.DebtNext
                : GoldExpIds.DebtLater;
        self.SetStatus("Self");
        self.AddBuff(buffId, amount.ToString());
    }

    public static int ConsumeFalseGold(ScriptExecutor self, int amount)
    {
        var consumed = ExecutorApi.RemoveSelfBuffStacks(self, GoldExpIds.FalseGold, amount);
        if (consumed > 0)
        {
            TryTriggerFalseGoldSpentBonus(self);
            RefreshGoldPaymentCards(self);
        }

        return consumed;
    }

    public static int ConsumeAllFalseGold(ScriptExecutor self)
    {
        return ConsumeFalseGold(self, FalseGold(self));
    }

    public static bool PayGold(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (!CanPayGold(self, amount))
        {
            return false;
        }

        var falseGold = FalseGold(self);
        var falsePaid = ConsumeFalseGold(self, Math.Min(falseGold, amount));
        var realCost = amount - falsePaid;
        if (realCost > 0)
        {
            PayRealGold(self, realCost);
        }

        return true;
    }

    public static bool CanPayGold(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        return PlayerApi.TryGetMoney(out var money) && FalseGold(self) + money >= amount;
    }

    public static bool TryCanPayGold(ScriptExecutor self, int amount, out bool canPay)
    {
        if (amount <= 0)
        {
            canPay = true;
            return true;
        }

        if (!PlayerApi.TryGetMoney(out var money))
        {
            canPay = false;
            return false;
        }

        canPay = FalseGold(self) + money >= amount;
        return true;
    }

    public static int PayRealGoldUpTo(ScriptExecutor self, int maxAmount)
    {
        if (maxAmount <= 0)
        {
            return 0;
        }

        var spend = Math.Min(PlayerApi.GetMoney(), maxAmount);
        if (spend > 0)
        {
            PayRealGold(self, spend);
        }

        return spend;
    }

    public static bool PayRealGold(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (PlayerApi.GetMoney() < amount)
        {
            return false;
        }

        self.ChangeMoney((-amount).ToString(), "false");
        TrackMoney(self);
        RefreshGoldPaymentCards(self);
        return true;
    }

    public static void GainGold(ScriptExecutor self, int amount, bool trackMoney = true)
    {
        if (amount <= 0)
        {
            return;
        }

        self.ChangeMoney(amount.ToString(), "false");
        if (trackMoney)
        {
            TrackMoney(self);
        }
        else
        {
            ExecutorApi.CombatIntSet(GoldExpIds.LastKnownMoney, PlayerApi.GetMoney());
        }

        RefreshGoldPaymentCards(self);
    }

    public static int SettleFalseGoldToRealGold(ScriptExecutor self)
    {
        return SettleFalseGoldToRealGold(self, 1, 1);
    }

    public static int SettleFalseGoldToRealGold(ScriptExecutor self, int numerator, int denominator)
    {
        var amount = ConsumeAllFalseGold(self);
        var realGold = denominator <= 0 ? 0 : Math.Max(0, amount * Math.Max(0, numerator) / denominator);
        if (realGold > 0)
        {
            GainGold(self, realGold, trackMoney: false);
        }

        return realGold;
    }

    public static int IncreaseFalseGoldByPercent(ScriptExecutor self, int percent)
    {
        var current = FalseGold(self);
        if (current <= 0 || percent <= 0)
        {
            return 0;
        }

        var gain = Math.Max(1, (current * percent + 99) / 100);
        AddFalseGold(self, gain);
        return gain;
    }

    public static int IncreaseDebtByPercent(ScriptExecutor self, int percent)
    {
        var current = Debt(self);
        if (current <= 0 || percent <= 0)
        {
            return 0;
        }

        var gain = Math.Max(1, (current * percent + 99) / 100);
        AddDebt(self, gain);
        return gain;
    }

    public static int HandleGoldDreamCardPlayed(ScriptExecutor self, string source)
    {
        var falseGoldGain = IncreaseFalseGoldByPercent(self, 10);
        var debtGain = IncreaseDebtByPercent(self, 10);
        GoldExpLog.Debug("Golden Dream resolved from " + source + ": falseGoldGain=" + falseGoldGain + ", debtGain=" + debtGain);
        return falseGoldGain + debtGain;
    }

    public static void SetAllDebtCountdownToOne(ScriptExecutor self)
    {
        var total = Debt(self);
        ClearAllDebt(self);
        if (total > 0)
        {
            AddDebtWithCountdown(self, 1, total);
            EnsureCombatHooks(self);
        }
    }

    public static void RegisterCareer(ScriptExecutor self)
    {
        PlayerApi.SetGameVar(GoldExpIds.GoldWitchActive, "1");
        PlayerApi.SetSkillTime(GoldExpIds.MidasContractCardId, 0);
        PlayerApi.SetSkillTime(GoldExpIds.FinalAuditCardId, 0);
        EnsureCombatHooks(self);

        ExecutorApi.TryAddEvent(self, "FightStart", new Action(() => OnFightStart(self)), "goldwitch_career");
        ExecutorApi.TryAddEvent(self, "Action", new Action(() => TrackMoney(self)), "goldwitch_career");
    }

    public static void OnFightStart(ScriptExecutor self)
    {
        ExecutorApi.CombatIntSet(GoldExpIds.FirstGoldGainDone, 0);
        ExecutorApi.CombatIntSet(GoldExpIds.FalseGoldSpentThisFight, 0);
        ExecutorApi.CombatIntSet(GoldExpIds.DebtBonusDone, 0);
        ExecutorApi.CombatIntSet(GoldExpIds.RoundFalseGoldGained, 0);
        ExecutorApi.CombatIntSet(GoldExpIds.LastKnownMoney, PlayerApi.GetMoney());
        ClearAllDebt(self);
        ApplyGoldenPotentialAtFightStart();

        var opening = Math.Min(8, Math.Max(0, PlayerApi.GetMoney() / 60));
        AddFalseGold(self, opening, countAsRoundGain: false);
    }

    public static void OnStartRound(ScriptExecutor self)
    {
        TickSkill(GoldExpIds.MidasContractCardId);
        TickSkill(GoldExpIds.FinalAuditCardId);
        ExecutorApi.CombatIntSet(GoldExpIds.RoundFalseGoldGained, 0);
        TickDebt(self);
        TrackMoney(self);
        RefreshGoldPaymentCards(self);
    }

    public static void TrackMoney(ScriptExecutor self)
    {
        var current = PlayerApi.GetMoney();
        var previous = ExecutorApi.CombatIntGet(GoldExpIds.LastKnownMoney, current);
        if (current > previous && ExecutorApi.CombatIntGet(GoldExpIds.FirstGoldGainDone) == 0)
        {
            var gain = Math.Max(1, (current - previous) / 20);
            AddFalseGold(self, gain);
            ExecutorApi.CombatIntSet(GoldExpIds.FirstGoldGainDone, 1);
        }

        ExecutorApi.CombatIntSet(GoldExpIds.LastKnownMoney, current);
    }

    public static int SettleDebt(ScriptExecutor self, int stacks, bool removeSettledStacks)
    {
        var targetStacks = Math.Min(Debt(self), Math.Max(0, stacks));
        if (targetStacks <= 0)
        {
            return 0;
        }

        SettleDebtAmount(self, targetStacks);
        if (removeSettledStacks)
        {
            RemoveDebtStacks(self, targetStacks);
        }

        return targetStacks;
    }

    public static void EndCombatCleanup(ScriptExecutor self)
    {
        ConsumeAllFalseGold(self);
        ClearAllDebt(self);
    }

    public static void EnableBankruptcyContract()
    {
        ExecutorApi.CombatIntSet("GoldExpBankruptcyContractActive", 1);
    }

    public static void TryTriggerFalseGoldSpentBonus(ScriptExecutor self)
    {
        if (ExecutorApi.CombatIntGet(GoldExpIds.FalseGoldSpentThisFight) != 0)
        {
            return;
        }

        ExecutorApi.CombatIntSet(GoldExpIds.FalseGoldSpentThisFight, 1);
        if (ExecutorApi.CombatIntGet("GoldExpOldKingCoinActive") == 1)
        {
            self.SetStatus("Self");
            self.ChangePower("1");
        }
    }

    public static void TryTriggerDebtBonus(ScriptExecutor self)
    {
        if (ExecutorApi.CombatIntGet(GoldExpIds.DebtBonusDone) != 0)
        {
            return;
        }

        if (ExecutorApi.CombatIntGet("GoldExpBankruptcyContractActive") != 1)
        {
            return;
        }

        ExecutorApi.CombatIntSet(GoldExpIds.DebtBonusDone, 1);
        self.SetStatus("Self");
        self.DrawCount("2");
        self.ChangePower("1");
    }

    private static void TickDebt(ScriptExecutor self)
    {
        var due = DebtDue(self);
        if (due > 0)
        {
            SettleDebtAmount(self, due);
            ClearDebtBuff(self, GoldExpIds.DebtDue);
        }

        var next = DebtNext(self);
        if (next > 0)
        {
            ClearDebtBuff(self, GoldExpIds.DebtNext);
            AddDebtWithCountdown(self, 1, next);
        }

        var later = DebtLater(self);
        if (later > 0)
        {
            ClearDebtBuff(self, GoldExpIds.DebtLater);
            AddDebtWithCountdown(self, 2, later);
        }
    }

    private static void SettleDebtAmount(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var falseGold = FalseGold(self);
        var money = PlayerApi.GetMoney();
        var canFullyPay = falseGold + money >= amount;
        var falseSpend = ConsumeFalseGold(self, Math.Min(falseGold, amount));
        var remaining = amount - falseSpend;
        var realSpend = Math.Min(money, remaining);
        if (realSpend > 0)
        {
            self.ChangeMoney((-realSpend).ToString(), "false");
        }

        if (!canFullyPay)
        {
            ClearHandAndPower(self);
        }

        ExecutorApi.CombatIntSet(GoldExpIds.LastKnownMoney, PlayerApi.GetMoney());
        RefreshGoldPaymentCards(self);
    }

    private static void ClearHandAndPower(ScriptExecutor self)
    {
        CardApi.DiscardAllHand(self);
        self.SetPower("0");
    }

    private static void RefreshGoldPaymentCards(ScriptExecutor self)
    {
        CardApi.RefreshUsableByLocalId(self, "fortune_throw", CanPayGold(self, 1000));
    }

    private static void RemoveDebtStacks(ScriptExecutor self, int amount)
    {
        var remaining = amount;
        foreach (var debtId in DebtIds)
        {
            if (remaining <= 0)
            {
                return;
            }

            remaining -= ExecutorApi.RemoveSelfBuffStacks(self, debtId, remaining);
        }
    }

    private static void ClearAllDebt(ScriptExecutor self)
    {
        foreach (var debtId in DebtIds)
        {
            ClearDebtBuff(self, debtId);
        }
    }

    private static void ClearDebtBuff(ScriptExecutor self, string buffId)
    {
        ExecutorApi.RemoveSelfBuffStacks(self, buffId, int.MaxValue);
    }

    private static void TickSkill(string key)
    {
        var current = PlayerApi.GetSkillTime(key);
        if (current > 0)
        {
            PlayerApi.SetSkillTime(key, current - 1);
        }
    }
}
