using System;

namespace Terrias.Dll.Mechanics;

public static class SpiritGrowthService
{
    public const int LegacyAptitude = 60;

    public static int MaxLevel => SpiritGrowthRegistry.DefaultMaxLevel;

    public static int MaxLevelFor(SpiritSpeciesGrowthProfile profile)
    {
        return SpiritGrowthRegistry.LevelCurveFor(profile).MaxLevel;
    }

    public static int ExperienceToNextLevel(int level)
    {
        return ExperienceToNextLevel(new SpiritSpeciesGrowthProfile(), level);
    }

    public static int ExperienceToNextLevel(SpiritSpeciesGrowthProfile profile, int level)
    {
        var levelCurve = SpiritGrowthRegistry.LevelCurveFor(profile);
        var experience = SpiritGrowthRegistry.ExperienceCurveFor(profile);
        var normalized = Math.Max(levelCurve.MinLevel, Math.Min(levelCurve.MaxLevel, level));
        if (normalized >= levelCurve.MaxLevel) return 0;
        var offset = normalized - levelCurve.MinLevel;
        return Math.Max(1, experience.Base + experience.Linear * offset + offset * offset / Math.Max(1, experience.QuadraticDivisor));
    }

    public static int TotalExperienceToLevel(int level)
    {
        return TotalExperienceToLevel(new SpiritSpeciesGrowthProfile(), level);
    }

    public static int TotalExperienceToLevel(SpiritSpeciesGrowthProfile profile, int level)
    {
        var curve = SpiritGrowthRegistry.LevelCurveFor(profile);
        var normalized = Math.Max(curve.MinLevel, Math.Min(curve.MaxLevel, level));
        var total = 0;
        for (var current = curve.MinLevel; current < normalized; current++)
        {
            total += ExperienceToNextLevel(profile, current);
        }
        return total;
    }

    public static int RollAptitude(string seed)
    {
        return RollAptitude(new SpiritSpeciesGrowthProfile(), seed);
    }

    public static int RollAptitude(SpiritSpeciesGrowthProfile profile, string seed)
    {
        var roll = SpiritGrowthRegistry.AptitudeRollFor(profile);
        for (var attempt = 0; attempt < roll.MaximumAttempts; attempt++)
        {
            var u1 = UnitInterval(seed + ":aptitude:" + attempt + ":a");
            var u2 = UnitInterval(seed + ":aptitude:" + attempt + ":b");
            var normal = Math.Sqrt(-2d * Math.Log(Math.Max(0.0000001d, u1))) * Math.Cos(2d * Math.PI * u2);
            var value = (int)Math.Round(roll.Mean + roll.StandardDeviation * normal, MidpointRounding.AwayFromZero);
            if (value >= roll.Minimum && value <= roll.Maximum) return value;
        }
        return roll.Fallback;
    }

    public static SpiritOriginVector OriginsAt(SpiritSpeciesGrowthProfile profile, int level, int aptitude)
    {
        profile ??= new SpiritSpeciesGrowthProfile();
        var levelCurve = SpiritGrowthRegistry.LevelCurveFor(profile);
        var aptitudeCurve = SpiritGrowthRegistry.AptitudeCurveFor(profile);
        var normalizedLevel = Math.Max(levelCurve.MinLevel, Math.Min(levelCurve.MaxLevel, level));
        var x = (normalizedLevel - levelCurve.MinLevel) / (double)(levelCurve.MaxLevel - levelCurve.MinLevel);
        var normalizedAptitude = Math.Max(aptitudeCurve.InputMin, Math.Min(aptitudeCurve.InputMax, aptitude));
        var q = (normalizedAptitude - aptitudeCurve.InputMin) / (double)(aptitudeCurve.InputMax - aptitudeCurve.InputMin);
        var smooth = 3d * q * q - 2d * q * q * q;
        var multiplier = aptitudeCurve.OutputMin + (aptitudeCurve.OutputMax - aptitudeCurve.OutputMin) * smooth;
        return new SpiritOriginVector
        {
            Magic = Grow(profile.BaseOrigins.Magic, profile.GrowthOrigins.Magic, x, multiplier),
            Spirit = Grow(profile.BaseOrigins.Spirit, profile.GrowthOrigins.Spirit, x, multiplier),
            Luck = Grow(profile.BaseOrigins.Luck, profile.GrowthOrigins.Luck, x, multiplier),
            Perception = Grow(profile.BaseOrigins.Perception, profile.GrowthOrigins.Perception, x, multiplier)
        };
    }

    public static CompanionStats BattleStats(SpiritOriginVector origins, SpiritIntentProfile? intentProfile = null, int speed = 100)
    {
        return BattleStats(new SpiritSpeciesGrowthProfile(), origins, intentProfile, speed);
    }

    public static CompanionStats BattleStats(
        SpiritSpeciesGrowthProfile growthProfile,
        SpiritOriginVector origins,
        SpiritIntentProfile? intentProfile = null,
        int speed = 100)
    {
        origins ??= new SpiritOriginVector();
        var conversion = SpiritGrowthRegistry.BattleConversionFor(growthProfile);
        var hp = Round(conversion.HpBase + conversion.HpSpirit * origins.Spirit + conversion.HpLuck * origins.Luck);
        var attack = Round(conversion.AttackBase + conversion.AttackMagic * origins.Magic
                           + conversion.AttackPerception * origins.Perception + conversion.AttackLuck * origins.Luck);
        var armor = Round(conversion.ArmorBase + conversion.ArmorPerception * origins.Perception
                          + conversion.ArmorSpirit * origins.Spirit + conversion.ArmorLuck * origins.Luck);
        var intentEnergy = Round(conversion.IntentEnergyBase + conversion.IntentEnergyMagic * origins.Magic
                                 + conversion.IntentEnergyPerception * origins.Perception);
        var profile = intentProfile ?? new SpiritIntentProfile();
        return new CompanionStats(
            Scale(hp, profile.HpMultiplier),
            Scale(intentEnergy, profile.MagicMultiplier),
            Scale(attack, profile.AttackMultiplier),
            Scale(armor, profile.ArmorMultiplier),
            Math.Max(1, speed));
    }

    public static SpiritExperienceResult GrantExperience(SpiritInstance instance, int amount)
    {
        var profile = SpiritGrowthRegistry.Resolve(instance);
        var maxLevel = MaxLevelFor(profile);
        var oldLevel = instance.Level;
        var oldExperience = instance.Experience;
        var remaining = Math.Max(0, amount);
        while (remaining > 0 && instance.Level < maxLevel)
        {
            var needed = Math.Max(1, ExperienceToNextLevel(profile, instance.Level) - instance.Experience);
            var consumed = Math.Min(needed, remaining);
            instance.Experience += consumed;
            remaining -= consumed;
            if (instance.Experience >= ExperienceToNextLevel(profile, instance.Level))
            {
                instance.Level++;
                instance.Experience = 0;
            }
        }
        if (instance.Level >= maxLevel)
        {
            instance.Level = maxLevel;
            instance.Experience = 0;
        }
        var unlocked = SpiritTrainingService.ApplyUnlockedNodes(instance);
        instance.LoadoutHash = SpiritTrainingService.LoadoutHash(instance);
        return new SpiritExperienceResult
        {
            Instance = instance.Clone(),
            OldLevel = oldLevel,
            OldExperience = oldExperience,
            GainedExperience = Math.Max(0, amount) - remaining,
            UnlockedAbilityIds = new System.Collections.Generic.List<string>(unlocked)
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
