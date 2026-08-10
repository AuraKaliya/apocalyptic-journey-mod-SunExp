using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class SpiritGrowthService
{
    public const int MaxLevel = 50;
    public const int LegacyAptitude = 60;

    public static int ExperienceToNextLevel(int level)
    {
        var normalized = Math.Max(1, Math.Min(MaxLevel, level));
        return normalized >= MaxLevel
            ? 0
            : 20 + 2 * (normalized - 1) + (normalized - 1) * (normalized - 1) / 24;
    }

    public static int TotalExperienceToLevel(int level)
    {
        var normalized = Math.Max(1, Math.Min(MaxLevel, level));
        var total = 0;
        for (var current = 1; current < normalized; current++)
        {
            total += ExperienceToNextLevel(current);
        }

        return total;
    }

    public static int RollAptitude(string seed)
    {
        // Rejection sampling produces a real truncated N(60, 15) rather than
        // piling out-of-range samples onto 0 and 100.
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var u1 = UnitInterval(seed + ":aptitude:" + attempt + ":a");
            var u2 = UnitInterval(seed + ":aptitude:" + attempt + ":b");
            var normal = Math.Sqrt(-2d * Math.Log(Math.Max(0.0000001d, u1))) * Math.Cos(2d * Math.PI * u2);
            var value = (int)Math.Round(60d + 15d * normal, MidpointRounding.AwayFromZero);
            if (value >= 0 && value <= 100)
            {
                return value;
            }
        }

        return LegacyAptitude;
    }

    public static SpiritOriginVector OriginsAt(
        SpiritSpeciesGrowthProfile profile,
        int level,
        int aptitude)
    {
        profile ??= new SpiritSpeciesGrowthProfile();
        var x = (Math.Max(1, Math.Min(MaxLevel, level)) - 1d) / (MaxLevel - 1d);
        var q = Math.Max(0d, Math.Min(1d, aptitude / 100d));
        var smooth = 3d * q * q - 2d * q * q * q;
        var multiplier = 0.8d + 0.4d * smooth;
        return new SpiritOriginVector
        {
            Magic = Grow(profile.BaseOrigins.Magic, profile.GrowthOrigins.Magic, x, multiplier),
            Spirit = Grow(profile.BaseOrigins.Spirit, profile.GrowthOrigins.Spirit, x, multiplier),
            Luck = Grow(profile.BaseOrigins.Luck, profile.GrowthOrigins.Luck, x, multiplier),
            Perception = Grow(profile.BaseOrigins.Perception, profile.GrowthOrigins.Perception, x, multiplier)
        };
    }

    public static CompanionStats BattleStats(SpiritOriginVector origins, SpiritIntentProfile? intentProfile = null)
    {
        origins ??= new SpiritOriginVector();
        var hp = Round(20d + 2.40d * origins.Spirit + 0.80d * origins.Luck);
        var attack = Round(3d + 0.80d * origins.Magic + 0.25d * origins.Perception + 0.15d * origins.Luck);
        var armor = Round(1d + 0.55d * origins.Perception + 0.20d * origins.Spirit + 0.10d * origins.Luck);
        var intentEnergy = Round(3d + 0.15d * origins.Magic + 0.10d * origins.Perception);
        var profile = intentProfile ?? new SpiritIntentProfile();
        return new CompanionStats(
            Scale(hp, profile.HpMultiplier),
            Scale(intentEnergy, profile.MagicMultiplier),
            Scale(attack, profile.AttackMultiplier),
            Scale(armor, profile.ArmorMultiplier));
    }

    public static SpiritExperienceResult GrantExperience(SpiritInstance instance, int amount)
    {
        var oldLevel = instance.Level;
        var oldExperience = instance.Experience;
        var remaining = Math.Max(0, amount);
        while (remaining > 0 && instance.Level < MaxLevel)
        {
            var needed = Math.Max(1, ExperienceToNextLevel(instance.Level) - instance.Experience);
            var consumed = Math.Min(needed, remaining);
            instance.Experience += consumed;
            remaining -= consumed;
            if (instance.Experience >= ExperienceToNextLevel(instance.Level))
            {
                instance.Level++;
                instance.Experience = 0;
            }
        }

        if (instance.Level >= MaxLevel)
        {
            instance.Level = MaxLevel;
            instance.Experience = 0;
        }

        return new SpiritExperienceResult
        {
            Instance = instance.Clone(),
            OldLevel = oldLevel,
            OldExperience = oldExperience,
            GainedExperience = Math.Max(0, amount) - remaining
        };
    }

    private static int Grow(int basis, int growth, double levelFactor, double aptitudeMultiplier)
    {
        return Math.Max(0, basis + (int)Math.Round(growth * levelFactor * aptitudeMultiplier, MidpointRounding.AwayFromZero));
    }

    private static int Round(double value)
    {
        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static int Scale(int value, float multiplier)
    {
        var normalized = Math.Max(0.25f, Math.Min(2.5f, multiplier <= 0f ? 1f : multiplier));
        return Math.Max(1, (int)Math.Round(value * normalized, MidpointRounding.AwayFromZero));
    }

    private static double UnitInterval(string value)
    {
        var hash = StableHash(value);
        return (hash + 1d) / (uint.MaxValue + 2d);
    }

    internal static uint StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }
    }
}
