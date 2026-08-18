using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class MorningStarCurseFormula
{
    public const int AllBeingsAspectFallbackVowPower = 5;
    public const int ImpregnableUpperBound = 8;

    public static int NormalizeTier(int rarity)
    {
        return Math.Max(1, Math.Min(4, rarity));
    }

    public static int ElegyHealthLoss(int currentHp)
    {
        return Math.Max(0, currentHp) / 2;
    }

    public static int ElegyTriggerCount(int actualLoss, int maxHp)
    {
        var loss = Math.Max(0, actualLoss);
        var maximum = Math.Max(0, maxHp);
        if (loss <= 0 || maximum <= 0)
        {
            return 0;
        }

        var denominator = (long)maximum * 7L;
        var count = denominator <= 0L ? 0L : (long)loss * 100L / denominator;
        return (int)Math.Max(0L, Math.Min(7L, count));
    }

    public static long BlackSunCrossTheoreticalRecovery(int maxHp, int vowPower)
    {
        var maximum = Math.Max(0, maxHp);
        var vow = Math.Max(0, vowPower);
        return (long)maximum * vow / 100L;
    }

    public static int BlackSunCrossRecovery(int maxHp, int currentHp, int vowPower)
    {
        var maximum = Math.Max(0, maxHp);
        var current = Math.Max(0, currentHp);
        var missing = Math.Max(0, maximum - current);
        if (missing <= 0 || vowPower <= 0)
        {
            return 0;
        }

        var theoretical = BlackSunCrossTheoreticalRecovery(maximum, vowPower);
        if (theoretical <= 0L)
        {
            theoretical = 1L;
        }

        return (int)Math.Min(missing, Math.Min(int.MaxValue, theoretical));
    }

    public static int DistinctBlessingCount(
        IEnumerable<string>? ownedBlessingIds,
        IEnumerable<string>? allBeingsBlessingIds)
    {
        var pool = new HashSet<string>(allBeingsBlessingIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (pool.Count == 0)
        {
            return 0;
        }

        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ownedBlessingIds ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(id) && pool.Contains(id.Trim()))
            {
                owned.Add(id.Trim());
            }
        }

        return Math.Min(pool.Count, owned.Count);
    }

    public static int ImpregnableGain(int currentLevel, int requestedGain)
    {
        var current = Math.Max(0, currentLevel);
        var requested = Math.Max(0, requestedGain);
        return Math.Max(0, Math.Min(requested, ImpregnableUpperBound - current));
    }

    public static int SaturatingMultiply(int left, int right)
    {
        var value = (long)Math.Max(0, left) * Math.Max(0, right);
        return (int)Math.Min(int.MaxValue, value);
    }

    public static int SaturatingAdd(int left, int right)
    {
        var value = (long)Math.Max(0, left) + Math.Max(0, right);
        return (int)Math.Min(int.MaxValue, value);
    }
}
