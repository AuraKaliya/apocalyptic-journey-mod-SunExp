using System;

namespace SunExp.Dll.Mechanics;

public static class SpiritCaptureRollService
{
    public const int BaseChanceBasisPoints = 1000;
    public const int MissingHpChanceBasisPoints = 8000;
    public const int MaximumChanceBasisPoints = 9000;

    public static int ChanceBasisPoints(int currentHp, int maximumHp)
    {
        var maxHp = Math.Max(1, maximumHp);
        var current = Math.Max(0, Math.Min(maxHp, currentHp));
        var missing = maxHp - current;
        var chance = BaseChanceBasisPoints + missing * MissingHpChanceBasisPoints / maxHp;
        return Math.Max(BaseChanceBasisPoints, Math.Min(MaximumChanceBasisPoints, chance));
    }

    public static int RollBasisPoints(string seed)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in seed ?? "")
            {
                hash = (hash ^ character) * 16777619;
            }

            return (int)(hash % 10000);
        }
    }

    public static bool Succeeds(int currentHp, int maximumHp, string seed, out int chance, out int roll)
    {
        chance = ChanceBasisPoints(currentHp, maximumHp);
        roll = RollBasisPoints(seed);
        return roll < chance;
    }
}
