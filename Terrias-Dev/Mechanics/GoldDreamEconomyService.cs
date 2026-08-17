using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class GoldDreamEconomyService
{
    private static readonly HashSet<string> RoundListenerStatusIds = new(StringComparer.Ordinal);
    private static string activeStatusId = "";
    private static int roundListenerEpoch;

    public static event Action<GoldDreamSnapshot>? Changed;

    public static GoldDreamSnapshot CurrentSnapshot()
    {
        return Snapshot(FightPlayer.Instance?.Status);
    }

    public static GoldDreamSnapshot Activate(ScriptExecutor? executor)
    {
        var status = executor?.Self ?? FightPlayer.Instance?.Status;
        if (status == null)
        {
            return GoldDreamSnapshot.Empty;
        }

        activeStatusId = status.InstanceId ?? "";
        EnsureRoundListener(executor, status);
        var snapshot = Snapshot(status);
        SyncPotential(status, snapshot.Tier);
        snapshot = Snapshot(status);
        Changed?.Invoke(snapshot);
        return snapshot;
    }

    public static bool CanPayGold(IStatusManager? status, int amount)
    {
        var requested = Math.Max(0, amount);
        return (long)BuffApi.Level(status, TerriasIds.FalseGold) + PlayerApi.GetMoney() >= requested;
    }

    public static bool PayGold(ScriptExecutor? executor, int amount)
    {
        var status = executor?.Self ?? FightPlayer.Instance?.Status;
        var requested = Math.Max(0, amount);
        if (status == null || !CanPayGold(status, requested))
        {
            return false;
        }

        Activate(executor);
        var snapshot = Snapshot(status);
        var falseSpend = Math.Min(snapshot.FalseGold, requested);
        var realSpend = requested - falseSpend;
        if (!PlayerApi.TrySpendMoney(realSpend))
        {
            return false;
        }

        Commit(status, snapshot.FalseGold - falseSpend, snapshot.DebtDueOne, snapshot.DebtDueTwo, snapshot.DebtDueThree);
        return true;
    }

    public static bool ResolveWager(ScriptExecutor self, out int cost)
    {
        cost = GoldDreamRules.WagerCost(PlayerApi.GetMoney());
        DictionaryUtil.Set(self?.dataConfig?.Vars, TerriasIds.GoldDreamSkipOnce, "1");
        if (self == null || self.Self == null || !PlayerApi.TrySpendMoney(cost))
        {
            return false;
        }

        Activate(self);
        var snapshot = Snapshot(self.Self);
        var baseFalseGold = GoldDreamRules.SaturatingAdd(snapshot.FalseGold, cost);
        var finalFalseGold = GoldDreamRules.SaturatingAdd(
            baseFalseGold,
            GoldDreamRules.TenPercentIncrease(baseFalseGold));
        var debtGrowth = GoldDreamRules.TenPercentIncrease(snapshot.TotalDebt);
        Commit(
            self.Self,
            finalFalseGold,
            snapshot.DebtDueOne,
            snapshot.DebtDueTwo,
            GoldDreamRules.SaturatingAdd(snapshot.DebtDueThree, debtGrowth));
        return true;
    }

    public static GoldDreamSnapshot AddBlankCheckResources(ScriptExecutor self)
    {
        var status = self?.Self ?? FightPlayer.Instance?.Status;
        if (status == null)
        {
            return GoldDreamSnapshot.Empty;
        }

        Activate(self);
        var snapshot = Snapshot(status);
        return Commit(
            status,
            GoldDreamRules.SaturatingAdd(snapshot.FalseGold, 1_000),
            snapshot.DebtDueOne,
            snapshot.DebtDueTwo,
            GoldDreamRules.SaturatingAdd(snapshot.DebtDueThree, 2_000));
    }

    public static GoldDreamSnapshot ApplyGoldDream(ScriptExecutor? executor)
    {
        var status = executor?.Self ?? FightPlayer.Instance?.Status;
        if (status == null)
        {
            return GoldDreamSnapshot.Empty;
        }

        Activate(executor);
        var snapshot = Snapshot(status);
        return Commit(
            status,
            GoldDreamRules.SaturatingAdd(
                snapshot.FalseGold,
                GoldDreamRules.TenPercentIncrease(snapshot.FalseGold)),
            snapshot.DebtDueOne,
            snapshot.DebtDueTwo,
            GoldDreamRules.SaturatingAdd(
                snapshot.DebtDueThree,
                GoldDreamRules.TenPercentIncrease(snapshot.TotalDebt)));
    }

    public static int ConvertFalseGoldAndAccelerateDebt(ScriptExecutor self)
    {
        var status = self?.Self ?? FightPlayer.Instance?.Status;
        if (status == null)
        {
            return 0;
        }

        Activate(self);
        var snapshot = Snapshot(status);
        var converted = GoldDreamRules.ConvertedRealGold(snapshot.FalseGold);
        PlayerApi.AddMoney(converted);
        Commit(status, 0, snapshot.TotalDebt, 0, 0);
        return converted;
    }

    public static void SettleAndAdvance(ScriptExecutor? executor, IStatusManager? status)
    {
        if (status == null || !IsActive(status))
        {
            return;
        }

        var snapshot = Snapshot(status);
        var due = snapshot.DebtDueOne;
        var falseSpend = Math.Min(snapshot.FalseGold, due);
        var remaining = due - falseSpend;
        var realSpend = PlayerApi.SpendMoneyUpTo(remaining);
        var unpaid = remaining - realSpend;

        Commit(status, snapshot.FalseGold - falseSpend, snapshot.DebtDueTwo, snapshot.DebtDueThree, 0);
        if (unpaid <= 0)
        {
            return;
        }

        CardApi.ThrowAllHandCards(executor);
        PlayerPowerApi.TrySetPower(0);
    }

    public static void NotifyMoneyChanged()
    {
        var snapshot = CurrentSnapshot();
        if (snapshot.Active)
        {
            Changed?.Invoke(snapshot);
        }
    }

    public static void ClearCombatState(string source)
    {
        var status = FightPlayer.Instance?.Status;
        if (status != null)
        {
            try
            {
                foreach (var buffId in ResourceAndPotentialBuffIds())
                {
                    if (status.GetBuff(buffId) != null)
                    {
                        status.RemoveBuff(buffId);
                    }
                }
            }
            catch (Exception ex)
            {
                TerriasLog.Error("Gold Dream combat state clear failed from " + source, ex);
            }
        }

        activeStatusId = "";
        RoundListenerStatusIds.Clear();
        roundListenerEpoch = roundListenerEpoch == int.MaxValue ? 1 : roundListenerEpoch + 1;
        Changed?.Invoke(GoldDreamSnapshot.Empty);
    }

    private static GoldDreamSnapshot Commit(
        IStatusManager status,
        int falseGold,
        int debtDueOne,
        int debtDueTwo,
        int debtDueThree)
    {
        var debt = GoldDreamRules.NormalizeDebt(debtDueOne, debtDueTwo, debtDueThree);
        BuffApi.SetExactLevel(status, TerriasIds.FalseGold, Math.Max(0, falseGold));
        BuffApi.SetExactLevel(status, TerriasIds.DebtDueOne, debt.DueOne);
        BuffApi.SetExactLevel(status, TerriasIds.DebtDueTwo, debt.DueTwo);
        BuffApi.SetExactLevel(status, TerriasIds.DebtDueThree, debt.DueThree);
        SyncPotential(status, GoldDreamRules.PotentialTier(falseGold));

        var snapshot = Snapshot(status);
        Changed?.Invoke(snapshot);
        return snapshot;
    }

    private static void SyncPotential(IStatusManager status, GoldenPotentialTier tier)
    {
        var expected = PotentialBuffId(tier);
        foreach (var buffId in PotentialBuffIds())
        {
            if (buffId != expected && status.GetBuff(buffId) != null)
            {
                status.RemoveBuff(buffId);
            }
        }

        if (status.GetBuff(expected) == null)
        {
            status.AddBuff(expected, 1);
        }
    }

    private static GoldDreamSnapshot Snapshot(IStatusManager? status)
    {
        if (status == null)
        {
            return GoldDreamSnapshot.Empty;
        }

        var falseGold = BuffApi.Level(status, TerriasIds.FalseGold);
        return new GoldDreamSnapshot(
            IsActive(status),
            falseGold,
            BuffApi.Level(status, TerriasIds.DebtDueOne),
            BuffApi.Level(status, TerriasIds.DebtDueTwo),
            BuffApi.Level(status, TerriasIds.DebtDueThree),
            GoldDreamRules.PotentialTier(falseGold));
    }

    private static bool IsActive(IStatusManager status)
    {
        if (!string.IsNullOrWhiteSpace(activeStatusId)
            && string.Equals(activeStatusId, status.InstanceId, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var buffId in PotentialBuffIds())
        {
            if (status.GetBuff(buffId) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureRoundListener(ScriptExecutor? executor, IStatusManager status)
    {
        var statusId = status.InstanceId ?? "";
        if (executor == null || string.IsNullOrWhiteSpace(statusId) || !RoundListenerStatusIds.Add(statusId))
        {
            return;
        }

        var epoch = roundListenerEpoch;
        if (!ScriptEventApi.TryAddOwnedEventListener(
                "StartRoundEnd" + statusId,
                () =>
                {
                    if (epoch == roundListenerEpoch
                        && string.Equals(activeStatusId, statusId, StringComparison.Ordinal))
                    {
                        SettleAndAdvance(executor, status);
                    }
                },
                executor,
                context: "gold-dream-debt:" + statusId))
        {
            RoundListenerStatusIds.Remove(statusId);
        }
    }

    private static string PotentialBuffId(GoldenPotentialTier tier)
    {
        return tier switch
        {
            GoldenPotentialTier.B => TerriasIds.GoldenPotentialB,
            GoldenPotentialTier.M => TerriasIds.GoldenPotentialM,
            GoldenPotentialTier.K => TerriasIds.GoldenPotentialK,
            _ => TerriasIds.GoldenPotentialZero
        };
    }

    private static IEnumerable<string> PotentialBuffIds()
    {
        yield return TerriasIds.GoldenPotentialZero;
        yield return TerriasIds.GoldenPotentialK;
        yield return TerriasIds.GoldenPotentialM;
        yield return TerriasIds.GoldenPotentialB;
    }

    private static IEnumerable<string> ResourceAndPotentialBuffIds()
    {
        yield return TerriasIds.FalseGold;
        yield return TerriasIds.DebtDueOne;
        yield return TerriasIds.DebtDueTwo;
        yield return TerriasIds.DebtDueThree;
        foreach (var buffId in PotentialBuffIds())
        {
            yield return buffId;
        }
    }
}
