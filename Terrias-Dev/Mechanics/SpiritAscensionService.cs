using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritGuiyuanPreview
{
    public int DonorCount { get; set; }

    public int OfferedValue { get; set; }

    public int AppliedValue { get; set; }

    public int OverflowValue { get; set; }

    public int CurrentValue { get; set; }

    public int ResultValue { get; set; }

    public int CurrentStarRank { get; set; }

    public int ResultStarRank { get; set; }
}

public sealed class SpiritGuiyuanResult
{
    public bool Success { get; set; }

    public string Reason { get; set; } = "";

    public SpiritGuiyuanPreview Preview { get; set; } = new();

    public SpiritInstance? Target { get; set; }
}

public static class SpiritAscensionService
{
    public const int MaximumStarRank = 5;
    public const int MaximumGuiyuanValue = 16;
    public const int MaximumAllocationPerOrigin = 10;

    private static readonly int[] Thresholds = { 1, 2, 4, 8, 16 };
    private static readonly int[] CumulativePointBudgets = { 0, 2, 6, 12, 20, 30 };

    public static int StarRankFor(int guiyuanValue)
    {
        var normalized = Math.Max(0, Math.Min(MaximumGuiyuanValue, guiyuanValue));
        var rank = 0;
        for (var index = 0; index < Thresholds.Length; index++)
        {
            if (normalized < Thresholds[index]) break;
            rank = index + 1;
        }
        return rank;
    }

    public static int ThresholdForStar(int starRank)
    {
        var normalized = Math.Max(1, Math.Min(MaximumStarRank, starRank));
        return Thresholds[normalized - 1];
    }

    public static int PointBudgetForStar(int starRank)
    {
        return CumulativePointBudgets[Math.Max(0, Math.Min(MaximumStarRank, starRank))];
    }

    public static int PointBudgetFor(SpiritInstance? instance)
    {
        return PointBudgetForStar(StarRankFor(instance?.GuiyuanValue ?? 0));
    }

    public static int ContributionOf(SpiritInstance? donor)
    {
        return 1 + Math.Max(0, Math.Min(MaximumGuiyuanValue, donor?.GuiyuanValue ?? 0));
    }

    public static SpiritOriginVector NormalizeAllocations(SpiritOriginVector? source, int guiyuanValue)
    {
        source ??= new SpiritOriginVector();
        var result = new SpiritOriginVector
        {
            Magic = ClampAllocation(source.Magic),
            Perception = ClampAllocation(source.Perception),
            Spirit = ClampAllocation(source.Spirit),
            Luck = ClampAllocation(source.Luck)
        };
        var excess = Math.Max(0, result.Total - PointBudgetForStar(StarRankFor(guiyuanValue)));
        result.Luck = Trim(result.Luck, ref excess);
        result.Spirit = Trim(result.Spirit, ref excess);
        result.Perception = Trim(result.Perception, ref excess);
        result.Magic = Trim(result.Magic, ref excess);
        return result;
    }

    public static bool IsValidAllocation(SpiritOriginVector? source, int guiyuanValue)
    {
        if (source == null) return false;
        return source.Magic is >= 0 and <= MaximumAllocationPerOrigin
               && source.Perception is >= 0 and <= MaximumAllocationPerOrigin
               && source.Spirit is >= 0 and <= MaximumAllocationPerOrigin
               && source.Luck is >= 0 and <= MaximumAllocationPerOrigin
               && source.Total <= PointBudgetForStar(StarRankFor(guiyuanValue));
    }

    public static SpiritOriginVector EffectiveOrigins(SpiritInstance instance)
    {
        var profile = SpiritGrowthRegistry.Resolve(instance);
        return AddAllocations(
            SpiritGrowthService.OriginsAt(profile, instance.Level, instance.Aptitude),
            NormalizeAllocations(instance.GuiyuanAllocations, instance.GuiyuanValue));
    }

    public static SpiritOriginVector AddAllocations(SpiritOriginVector? origins, SpiritOriginVector? allocations)
    {
        origins ??= new SpiritOriginVector();
        allocations ??= new SpiritOriginVector();
        return new SpiritOriginVector
        {
            Magic = Math.Max(0, origins.Magic) + Math.Max(0, allocations.Magic),
            Perception = Math.Max(0, origins.Perception) + Math.Max(0, allocations.Perception),
            Spirit = Math.Max(0, origins.Spirit) + Math.Max(0, allocations.Spirit),
            Luck = Math.Max(0, origins.Luck) + Math.Max(0, allocations.Luck)
        };
    }

    public static CompanionStats ApplyStarBonus(CompanionStats stats, int starRank)
    {
        stats ??= new CompanionStats(1, 1, 0, 0);
        var multiplier = 1d + 0.2d * Math.Max(0, Math.Min(MaximumStarRank, starRank));
        var result = new CompanionStats(
            Scale(stats.MaxHp, multiplier),
            stats.MaxMagic,
            Scale(stats.Attack, multiplier),
            Scale(stats.Armor, multiplier),
            stats.Speed);
        result.SetCurrentMagic(stats.CurrentMagic);
        return result;
    }

    public static SpiritGuiyuanPreview Preview(SpiritInstance target, IReadOnlyList<SpiritInstance> donors)
    {
        var current = Math.Max(0, Math.Min(MaximumGuiyuanValue, target?.GuiyuanValue ?? 0));
        var offered = 0;
        foreach (var donor in donors ?? Array.Empty<SpiritInstance>()) offered += ContributionOf(donor);
        var applied = Math.Min(Math.Max(0, MaximumGuiyuanValue - current), Math.Max(0, offered));
        return new SpiritGuiyuanPreview
        {
            DonorCount = donors?.Count ?? 0,
            OfferedValue = offered,
            AppliedValue = applied,
            OverflowValue = Math.Max(0, offered - applied),
            CurrentValue = current,
            ResultValue = current + applied,
            CurrentStarRank = StarRankFor(current),
            ResultStarRank = StarRankFor(current + applied)
        };
    }

    public static bool ValidateDeploymentSnapshot(CapturedEnemySnapshot? snapshot, out string reason)
    {
        if (snapshot == null
            || snapshot.SpiritGuiyuanValue < 0
            || snapshot.SpiritGuiyuanValue > MaximumGuiyuanValue
            || snapshot.SpiritStarRank != StarRankFor(snapshot.SpiritGuiyuanValue))
        {
            reason = "精灵归元星级快照无效。";
            return false;
        }

        var allocations = new SpiritOriginVector
        {
            Magic = snapshot.GuiyuanAllocationMagic,
            Spirit = snapshot.GuiyuanAllocationSpirit,
            Luck = snapshot.GuiyuanAllocationLuck,
            Perception = snapshot.GuiyuanAllocationPerception
        };
        if (!IsValidAllocation(allocations, snapshot.SpiritGuiyuanValue))
        {
            reason = "精灵归元本源分配快照无效。";
            return false;
        }

        var profile = !string.IsNullOrWhiteSpace(snapshot.ProfileId)
                      && SpiritGrowthRegistry.TryFind(snapshot.ProfileId, out var fixedProfile)
            ? fixedProfile
            : SpiritGrowthRegistry.Resolve(snapshot);
        var roll = SpiritGrowthRegistry.AptitudeRollFor(profile);
        var maxLevel = SpiritGrowthService.MaxLevelFor(profile);
        if (snapshot.SpiritLevel < 1 || snapshot.SpiritLevel > maxLevel
            || snapshot.SpiritAptitude < roll.Minimum || snapshot.SpiritAptitude > roll.Maximum)
        {
            reason = "精灵归元成长参数快照无效。";
            return false;
        }
        var expected = AddAllocations(
            SpiritGrowthService.OriginsAt(profile, snapshot.SpiritLevel, snapshot.SpiritAptitude),
            allocations);
        if (snapshot.OriginMagic != expected.Magic
            || snapshot.OriginSpirit != expected.Spirit
            || snapshot.OriginLuck != expected.Luck
            || snapshot.OriginPerception != expected.Perception)
        {
            reason = "精灵归元本源数值与成长快照不一致。";
            return false;
        }

        reason = "";
        return true;
    }

    private static int ClampAllocation(int value) => Math.Max(0, Math.Min(MaximumAllocationPerOrigin, value));

    private static int Trim(int value, ref int excess)
    {
        if (excess <= 0 || value <= 0) return value;
        var removed = Math.Min(value, excess);
        value -= removed;
        excess -= removed;
        return value;
    }

    private static int Scale(int value, double multiplier)
    {
        return Math.Max(0, (int)Math.Round(Math.Max(0, value) * multiplier, MidpointRounding.AwayFromZero));
    }
}
