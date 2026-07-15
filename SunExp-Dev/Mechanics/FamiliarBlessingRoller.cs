using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

public static class FamiliarBlessingRoller
{
    public const int HighChoiceAptitude = 70;
    public const int LowChoiceSize = 2;
    public const int HighChoiceSize = 3;

    public static int NormalizeAptitude(int aptitude)
    {
        return Math.Max(0, Math.Min(100, aptitude));
    }

    public static int AptitudeFloor(int rebirthCount)
    {
        return Math.Min(70, 30 + Math.Max(0, rebirthCount) * 5);
    }

    public static int RollAptitude(string fullSpeciesId, int rebirthCount)
    {
        var floor = AptitudeFloor(rebirthCount);
        var range = 101 - floor;
        return floor + StableHash(FamiliarId.NormalizeFullSpeciesId(fullSpeciesId) + "|rebirth|" + Math.Max(0, rebirthCount)) % range;
    }

    public static int DefaultAptitude(FamiliarInstance instance)
    {
        return RollAptitude(instance.FullSpeciesId.Length > 0 ? instance.FullSpeciesId : instance.SpeciesId, instance.RebirthCount);
    }

    public static string AptitudeLabel(int aptitude)
    {
        var value = NormalizeAptitude(aptitude);
        if (value >= 90)
        {
            return "完美";
        }

        if (value >= 70)
        {
            return "了不起的天分";
        }

        if (value >= 50)
        {
            return "优秀";
        }

        if (value >= 30)
        {
            return "良好";
        }

        return "普通";
    }

    public static int ChoiceSize(int aptitude)
    {
        return NormalizeAptitude(aptitude) >= HighChoiceAptitude ? HighChoiceSize : LowChoiceSize;
    }

    public static int MaxTierForAptitude(int aptitude)
    {
        var value = NormalizeAptitude(aptitude);
        if (value >= 70)
        {
            return 4;
        }

        if (value >= 50)
        {
            return 3;
        }

        if (value >= 30)
        {
            return 2;
        }

        return 1;
    }

    public static int MaxTierForMilestone(int milestone)
    {
        if (milestone >= 6)
        {
            return 4;
        }

        return milestone >= 4 ? 3 : 2;
    }

    public static FamiliarBlessingChoice? CreateChoice(FamiliarInstance instance, int milestone)
    {
        return milestone >= FamiliarRosterService.FinalBlessingLevel
            ? CreateFinalChoice(instance, milestone)
            : CreateGrowthChoice(instance, milestone);
    }

    private static FamiliarBlessingChoice? CreateGrowthChoice(FamiliarInstance instance, int milestone)
    {
        var owned = new HashSet<string>(instance.AllBlessingIds(), StringComparer.Ordinal);
        var blockedGroups = FamiliarBlessingRegistry.All()
            .Where(blessing => owned.Contains(blessing.Id) && !string.IsNullOrWhiteSpace(blessing.ExclusiveGroup))
            .Select(blessing => blessing.ExclusiveGroup)
            .ToHashSet(StringComparer.Ordinal);
        var maxTier = Math.Min(MaxTierForAptitude(instance.Aptitude), MaxTierForMilestone(milestone));
        var available = FamiliarBlessingRegistry.GrowthEligible(instance, milestone, maxTier)
            .Where(blessing => !owned.Contains(blessing.Id))
            .Where(blessing => string.IsNullOrWhiteSpace(blessing.ExclusiveGroup) || !blockedGroups.Contains(blessing.ExclusiveGroup))
            .Where(blessing => blessing.Weight > 0)
            .ToList();
        if (available.Count == 0)
        {
            return null;
        }

        var random = RandomFor(instance, milestone);
        var selected = new List<FamiliarBlessingDefinition>();
        var newestTier = available.Max(blessing => blessing.Tier);
        var newest = PickWeighted(available.Where(blessing => blessing.Tier == newestTier).ToList(), instance.Aptitude, random);
        if (newest != null)
        {
            selected.Add(newest);
        }

        while (selected.Count < ChoiceSize(instance.Aptitude) && selected.Count < available.Count)
        {
            var next = PickWeighted(available.Where(blessing => selected.All(item => item.Id != blessing.Id)).ToList(), instance.Aptitude, random);
            if (next == null)
            {
                break;
            }

            selected.Add(next);
        }

        return BuildChoice(instance, milestone, FamiliarChoiceKind.Growth, selected);
    }

    private static FamiliarBlessingChoice? CreateFinalChoice(FamiliarInstance instance, int milestone)
    {
        var random = RandomFor(instance, milestone);
        var generic = FamiliarBlessingRegistry.GenericFinals(instance).Where(item => item.Weight > 0).ToList();
        var specific = FamiliarBlessingRegistry.SpecificFinals(instance).Where(item => item.Weight > 0).ToList();
        var selected = new List<FamiliarBlessingDefinition>();
        var specificPick = PickWeighted(specific, instance.Aptitude, random);
        var genericPick = PickWeighted(generic, instance.Aptitude, random);
        if (specificPick != null)
        {
            selected.Add(specificPick);
        }

        if (genericPick != null && selected.All(item => item.Id != genericPick.Id))
        {
            selected.Add(genericPick);
        }

        var union = specific.Concat(generic).GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
        while (selected.Count < ChoiceSize(instance.Aptitude) && selected.Count < union.Count)
        {
            var next = PickWeighted(union.Where(item => selected.All(chosen => chosen.Id != item.Id)).ToList(), instance.Aptitude, random);
            if (next == null)
            {
                break;
            }

            selected.Add(next);
        }

        return BuildChoice(instance, milestone, FamiliarChoiceKind.Final, selected);
    }

    private static FamiliarBlessingChoice? BuildChoice(
        FamiliarInstance instance,
        int milestone,
        string kind,
        IReadOnlyList<FamiliarBlessingDefinition> selected)
    {
        if (selected.Count == 0)
        {
            return null;
        }

        return new FamiliarBlessingChoice
        {
            ChoiceId = "choice-r" + Math.Max(0, instance.RebirthCount).ToString("000")
                       + "-m" + Math.Max(1, milestone).ToString("00")
                       + "-" + Math.Max(0, instance.BlessingRollIndex).ToString("000"),
            Level = milestone,
            Tier = selected.Max(blessing => blessing.Tier),
            Kind = kind,
            BlessingIds = selected.Select(blessing => blessing.Id).ToList()
        };
    }

    private static FamiliarBlessingDefinition? PickWeighted(
        IReadOnlyList<FamiliarBlessingDefinition> candidates,
        int aptitude,
        Random random)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var weights = candidates.Select(candidate => Math.Max(0, candidate.Weight) * TierWeight(aptitude, candidate.Tier)).ToArray();
        var total = weights.Sum();
        if (total <= 0)
        {
            return candidates[random.Next(0, candidates.Count)];
        }

        var roll = random.Next(0, total);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (roll < weights[i])
            {
                return candidates[i];
            }

            roll -= weights[i];
        }

        return candidates[candidates.Count - 1];
    }

    private static int TierWeight(int aptitude, int tier)
    {
        var maxTier = MaxTierForAptitude(aptitude);
        return tier >= maxTier ? 5 : tier == maxTier - 1 ? 3 : 1;
    }

    private static Random RandomFor(FamiliarInstance instance, int milestone)
    {
        return new Random(StableHash(
            instance.FullSpeciesId + "|" + instance.RebirthCount + "|" + milestone + "|" + instance.BlessingRollIndex));
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value ?? "")
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7fffffff);
        }
    }
}
