using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Data.Save;

namespace SunExp.Dll.Mechanics;

public static class CompanionStatsService
{
    public static CompanionStats ProjectionStats(PolymorphRoleSpec role)
    {
        return BaseStats();
    }

    public static CompanionStats SpiritStats(SpiritIntentProfile? profile)
    {
        var source = BaseStats();
        var active = profile ?? new SpiritIntentProfile();
        return new CompanionStats(
            Scale(source.MaxHp, active.HpMultiplier),
            Scale(source.MaxMagic, active.MagicMultiplier),
            Scale(source.Attack, active.AttackMultiplier),
            Scale(source.Armor, active.ArmorMultiplier));
    }

    private static CompanionStats BaseStats()
    {
        var origins = CurrentOrigins();
        var multiplier = AbyssMultiplier();
        var maxHp = Round((28 + origins.Spirit * 3.0f + origins.Luck * 2.0f) * multiplier);
        var maxMagic = Round((3 + origins.Magic * 0.2f + origins.Perception * 0.16f) * multiplier);
        var attack = Round((5 + origins.Magic * 1.2f) * multiplier);
        var armor = Round((4 + origins.Spirit * 0.7f + origins.Perception * 0.8f) * multiplier);
        return new CompanionStats(maxHp, maxMagic, attack, armor);
    }

    private static int Scale(int value, float multiplier)
    {
        var safe = Math.Max(0.25f, Math.Min(2.5f, multiplier <= 0f ? 1f : multiplier));
        return Math.Max(1, (int)Math.Round(value * safe, MidpointRounding.AwayFromZero));
    }

    private static CompanionOriginStats CurrentOrigins()
    {
        var vars = FightManager.Instance?.TempVarsMap;
        if (vars == null || vars.Count == 0)
        {
            vars = RoleTable.Instance?.VarsMap;
        }

        return new CompanionOriginStats(
            ReadInt(vars, "Strength"),
            ReadInt(vars, "Lucky"),
            ReadInt(vars, "Wisdom"),
            ReadInt(vars, "Perceive"));
    }

    private static float AbyssMultiplier()
    {
        try
        {
            var exHard = Math.Max(0, GameSaveManager.GetEXHard());
            var hardTags = GameSaveManager.GetHardTags()?.Count ?? 0;
            return 1f + Math.Min(1.2f, exHard * 0.04f + hardTags * 0.02f);
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[CompanionStats] abyss multiplier fallback used: " + ex.Message);
            return 1f;
        }
    }

    private static int ReadInt(Dictionary<string, int>? vars, string key)
    {
        if (vars == null || string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        return vars.TryGetValue(key, out var value) ? Math.Max(0, value) : 0;
    }

    private static int Round(float value)
    {
        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private readonly struct CompanionOriginStats
    {
        public CompanionOriginStats(int magic, int spirit, int luck, int perception)
        {
            Magic = magic;
            Spirit = spirit;
            Luck = luck;
            Perception = perception;
        }

        public int Magic { get; }

        public int Spirit { get; }

        public int Luck { get; }

        public int Perception { get; }
    }
}
