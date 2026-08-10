using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class SpiritGrowthQueryService
{
    public static SpiritGrowthViewSnapshot Build(SpiritInstance instance)
    {
        instance ??= new SpiritInstance();
        var sourceSnapshot = instance.Snapshot ?? new CapturedEnemySnapshot();
        var profile = SpiritGrowthRegistry.Resolve(instance);
        var maxLevel = SpiritGrowthService.MaxLevelFor(profile);
        var current = SpiritGrowthService.OriginsAt(profile, instance.Level, instance.Aptitude);
        var potential = SpiritGrowthService.OriginsAt(profile, maxLevel, instance.Aptitude);
        var standard = SpiritGrowthService.OriginsAt(profile, maxLevel, SpiritGrowthService.LegacyAptitude);
        var intent = SpiritIntentRegistry.ProfileForIdentity(instance.ProfileId, sourceSnapshot.ProfileKey);
        var radar = SpiritGrowthRegistry.RadarScaleFor(profile);
        return new SpiritGrowthViewSnapshot
        {
            SpiritUid = instance.SpiritUid,
            SpeciesId = string.IsNullOrWhiteSpace(instance.SpeciesId) ? profile.SpeciesId : instance.SpeciesId,
            ProfileId = string.IsNullOrWhiteSpace(instance.ProfileId) ? profile.ProfileId : instance.ProfileId,
            FormKey = profile.FormKey,
            FormLabel = SpiritGrowthRegistry.FormLabel(profile),
            Tier = ParseTier(profile.Tier),
            Level = Math.Max(1, Math.Min(maxLevel, instance.Level)),
            MaxLevel = maxLevel,
            Experience = instance.Experience,
            ExperienceToNextLevel = SpiritGrowthService.ExperienceToNextLevel(profile, instance.Level),
            Aptitude = instance.Aptitude,
            BaseOrigins = profile.BaseOrigins.Clone(),
            GrowthOrigins = profile.GrowthOrigins.Clone(),
            CurrentOrigins = current,
            MaxLevelOriginsAtCurrentAptitude = potential,
            StandardOriginsAtLevel50Aptitude60 = standard,
            BattleStats = SpiritGrowthService.BattleStats(profile, current, intent),
            RadarScaleId = radar.Id,
            RadarAxes = BuildRadarAxes(profile, radar, current, potential),
            CurrentAptitudeCurve = BuildCurve(profile, instance.Aptitude),
            StandardAptitudeCurve = BuildCurve(profile, SpiritGrowthService.LegacyAptitude),
            TheoreticalAptitudeCurve = BuildCurve(profile, 100)
        };
    }

    public static List<SpiritGrowthCurvePoint> BuildCurve(SpiritSpeciesGrowthProfile profile, int aptitude)
    {
        var result = new List<SpiritGrowthCurvePoint>();
        var levelCurve = SpiritGrowthRegistry.LevelCurveFor(profile);
        for (var level = levelCurve.MinLevel; level <= levelCurve.MaxLevel; level++)
        {
            result.Add(new SpiritGrowthCurvePoint
            {
                Level = level,
                TotalExperience = SpiritGrowthService.TotalExperienceToLevel(profile, level),
                Origins = SpiritGrowthService.OriginsAt(profile, level, aptitude)
            });
        }
        return result;
    }

    private static List<SpiritRadarAxisSnapshot> BuildRadarAxes(
        SpiritSpeciesGrowthProfile profile,
        SpiritRadarScaleSet radar,
        SpiritOriginVector current,
        SpiritOriginVector potential)
    {
        var result = new List<SpiritRadarAxisSnapshot>();
        foreach (var axis in radar.Axes)
        {
            var basis = Value(profile.BaseOrigins, axis.Key);
            var growth = Value(profile.GrowthOrigins, axis.Key);
            var rawCurrent = Value(current, axis.Key);
            var rawPotential = Value(potential, axis.Key);
            result.Add(new SpiritRadarAxisSnapshot
            {
                Key = axis.Key,
                Label = Label(axis.Key),
                BaseValue = basis,
                GrowthBudget = growth,
                RawCurrent = rawCurrent,
                RawPotential = rawPotential,
                Cap = axis.Cap,
                NormalizedCurrent = Clamp01(rawCurrent / (float)Math.Max(1, axis.Cap)),
                NormalizedPotential = Clamp01(rawPotential / (float)Math.Max(1, axis.Cap))
            });
        }
        return result;
    }

    public static int Value(SpiritOriginVector origins, string key)
    {
        return key switch
        {
            "magic" => origins.Magic,
            "perception" => origins.Perception,
            "spirit" => origins.Spirit,
            "luck" => origins.Luck,
            "total" => origins.Total,
            _ => 0
        };
    }

    public static string Label(string key)
    {
        return key switch
        {
            "magic" => "魔力",
            "perception" => "感知",
            "spirit" => "精神",
            "luck" => "幸运",
            "total" => "总值",
            _ => key ?? ""
        };
    }

    private static SpiritSpeciesTier ParseTier(string value)
    {
        return Enum.TryParse(value, true, out SpiritSpeciesTier tier) ? tier : SpiritSpeciesTier.Normal;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}
