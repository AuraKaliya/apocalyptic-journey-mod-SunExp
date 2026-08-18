using System;

namespace Terrias.Dll.Mechanics;

public enum GoldenPotentialTier
{
    Zero,
    K,
    M,
    B
}

public sealed class GoldDreamSnapshot
{
    public static GoldDreamSnapshot Empty { get; } = new(false, 0, 0, 0, 0, GoldenPotentialTier.Zero);

    public GoldDreamSnapshot(
        bool active,
        int falseGold,
        int debtDueOne,
        int debtDueTwo,
        int debtDueThree,
        GoldenPotentialTier tier)
    {
        Active = active;
        FalseGold = Math.Max(0, falseGold);
        DebtDueOne = Math.Max(0, debtDueOne);
        DebtDueTwo = Math.Max(0, debtDueTwo);
        DebtDueThree = Math.Max(0, debtDueThree);
        Tier = tier;
    }

    public bool Active { get; }

    public int FalseGold { get; }

    public int DebtDueOne { get; }

    public int DebtDueTwo { get; }

    public int DebtDueThree { get; }

    public GoldenPotentialTier Tier { get; }

    public int TotalDebt => GoldDreamRules.TotalDebt(DebtDueOne, DebtDueTwo, DebtDueThree);
}

public static class GoldDreamRules
{
    public const long KThreshold = 1_000L;
    public const long MThreshold = 1_000_000L;
    public const long BThreshold = 1_000_000_000L;

    public static long TotalAssets(int falseGold, int realGold)
    {
        return (long)Math.Max(0, falseGold) + Math.Max(0, realGold);
    }

    public static GoldenPotentialTier PotentialTier(int falseGold, int realGold)
    {
        return PotentialTier(TotalAssets(falseGold, realGold));
    }

    public static GoldenPotentialTier PotentialTier(long assets)
    {
        var value = Math.Max(0L, assets);
        if (value >= BThreshold)
        {
            return GoldenPotentialTier.B;
        }

        if (value >= MThreshold)
        {
            return GoldenPotentialTier.M;
        }

        return value >= KThreshold ? GoldenPotentialTier.K : GoldenPotentialTier.Zero;
    }

    public static int WagerCost(int realGold)
    {
        return SaturatingAdd(50, Math.Max(0, realGold) / 10);
    }

    public static int TenPercentIncrease(int current)
    {
        var value = Math.Max(0, current);
        return value == 0 ? 0 : (int)Math.Min(int.MaxValue, ((long)value + 9L) / 10L);
    }

    public static int ConvertedRealGold(int falseGold)
    {
        return Math.Max(0, falseGold) / 2;
    }

    public static int FortuneThrowDamage(int roll, int ascensionCount)
    {
        var baseDamage = Math.Max(0, roll) / 10;
        var multiplier = Math.Max(1L, (long)Math.Max(0, ascensionCount) + 1L);
        return (int)Math.Min(int.MaxValue, baseDamage * multiplier);
    }

    public static int SaturatingAdd(int current, int amount)
    {
        return (int)Math.Max(0L, Math.Min(int.MaxValue, (long)Math.Max(0, current) + Math.Max(0, amount)));
    }

    public static int TotalDebt(int dueOne, int dueTwo, int dueThree)
    {
        return (int)Math.Min(
            int.MaxValue,
            (long)Math.Max(0, dueOne) + Math.Max(0, dueTwo) + Math.Max(0, dueThree));
    }

    public static (int DueOne, int DueTwo, int DueThree) NormalizeDebt(int dueOne, int dueTwo, int dueThree)
    {
        var first = Math.Max(0, dueOne);
        var remaining = int.MaxValue - first;
        var second = Math.Min(Math.Max(0, dueTwo), remaining);
        remaining -= second;
        var third = Math.Min(Math.Max(0, dueThree), remaining);
        return (first, second, third);
    }
}
