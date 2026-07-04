using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

public static class FamiliarBlessingRoller
{
    public const int BodyDefaultAptitude = 70;
    public const int ChoiceSize = 3;

    public static int NormalizeAptitude(int aptitude)
    {
        return Math.Max(0, Math.Min(100, aptitude));
    }

    public static int DefaultAptitude(FamiliarInstance instance)
    {
        if (instance.IsBody || string.Equals(instance.InstanceId, FamiliarId.BodyInstanceId(instance.SpeciesId), StringComparison.Ordinal))
        {
            return BodyDefaultAptitude;
        }

        return StableHash(instance.InstanceId + "|aptitude") % 101;
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

    public static int MaxTierForAptitude(int aptitude)
    {
        var value = NormalizeAptitude(aptitude);
        if (value >= 90)
        {
            return 5;
        }

        if (value >= 70)
        {
            return 4;
        }

        if (value >= 50)
        {
            return 3;
        }

        return value >= 30 ? 2 : 1;
    }

    public static FamiliarBlessingChoice? CreateChoice(FamiliarInstance instance, int level)
    {
        var owned = new HashSet<string>(instance.Blessings ?? new List<string>(), StringComparer.Ordinal);
        foreach (var pendingId in (instance.PendingBlessingChoices ?? new List<FamiliarBlessingChoice>())
                     .SelectMany(choice => choice.BlessingIds ?? new List<string>()))
        {
            if (!string.IsNullOrWhiteSpace(pendingId))
            {
                owned.Add(pendingId.Trim());
            }
        }

        var blockedGroups = FamiliarBlessingRegistry.All()
            .Where(blessing => owned.Contains(blessing.Id) && !string.IsNullOrWhiteSpace(blessing.ExclusiveGroup))
            .Select(blessing => blessing.ExclusiveGroup)
            .ToHashSet(StringComparer.Ordinal);

        var speciesId = FamiliarId.NormalizeSpeciesId(instance.SpeciesId);
        var maxTier = MaxTierForAptitude(instance.Aptitude);
        var available = FamiliarBlessingRegistry.All()
            .Where(blessing => blessing.RequiredLevel <= Math.Max(1, level))
            .Where(blessing => blessing.Tier <= maxTier)
            .Where(blessing => FamiliarBlessingRegistry.Allows(blessing, speciesId))
            .Where(blessing => !owned.Contains(blessing.Id))
            .Where(blessing => string.IsNullOrWhiteSpace(blessing.ExclusiveGroup) || !blockedGroups.Contains(blessing.ExclusiveGroup))
            .Where(blessing => blessing.Weight > 0)
            .ToList();
        if (available.Count == 0)
        {
            return null;
        }

        var random = new Random(StableHash(instance.InstanceId + "|" + level + "|" + instance.BlessingRollIndex));
        var selected = new List<FamiliarBlessingDefinition>();
        while (selected.Count < ChoiceSize && selected.Count < available.Count)
        {
            var tier = RollAvailableTier(instance.Aptitude, available, selected, random);
            var candidate = PickWeighted(
                available.Where(blessing => blessing.Tier == tier && selected.All(item => item.Id != blessing.Id)).ToList(),
                random);
            if (candidate == null)
            {
                candidate = PickWeighted(available.Where(blessing => selected.All(item => item.Id != blessing.Id)).ToList(), random);
            }

            if (candidate == null)
            {
                break;
            }

            selected.Add(candidate);
        }

        if (selected.Count == 0)
        {
            return null;
        }

        return new FamiliarBlessingChoice
        {
            ChoiceId = "choice-" + Math.Max(0, instance.BlessingRollIndex).ToString("000"),
            Level = Math.Max(1, level),
            Tier = selected.Max(blessing => blessing.Tier),
            BlessingIds = selected.Select(blessing => blessing.Id).ToList()
        };
    }

    private static int RollAvailableTier(
        int aptitude,
        IReadOnlyList<FamiliarBlessingDefinition> available,
        IReadOnlyList<FamiliarBlessingDefinition> selected,
        Random random)
    {
        var selectedIds = new HashSet<string>(selected.Select(item => item.Id), StringComparer.Ordinal);
        var availableTiers = available
            .Where(blessing => !selectedIds.Contains(blessing.Id))
            .Select(blessing => blessing.Tier)
            .Distinct()
            .ToHashSet();
        var weights = TierWeights(aptitude)
            .Where(entry => availableTiers.Contains(entry.Tier) && entry.Weight > 0)
            .ToList();
        if (weights.Count == 0)
        {
            return availableTiers.OrderBy(tier => tier).FirstOrDefault();
        }

        var total = weights.Sum(entry => entry.Weight);
        var roll = random.Next(0, Math.Max(1, total));
        foreach (var entry in weights)
        {
            if (roll < entry.Weight)
            {
                return entry.Tier;
            }

            roll -= entry.Weight;
        }

        return weights[weights.Count - 1].Tier;
    }

    private static FamiliarBlessingDefinition? PickWeighted(IReadOnlyList<FamiliarBlessingDefinition> candidates, Random random)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var total = candidates.Sum(item => Math.Max(0, item.Weight));
        if (total <= 0)
        {
            return candidates[random.Next(0, candidates.Count)];
        }

        var roll = random.Next(0, total);
        foreach (var candidate in candidates)
        {
            var weight = Math.Max(0, candidate.Weight);
            if (roll < weight)
            {
                return candidate;
            }

            roll -= weight;
        }

        return candidates[candidates.Count - 1];
    }

    private static IReadOnlyList<(int Tier, int Weight)> TierWeights(int aptitude)
    {
        var value = NormalizeAptitude(aptitude);
        if (value >= 90)
        {
            return new[] { (1, 10), (2, 20), (3, 20), (4, 30), (5, 20) };
        }

        if (value >= 70)
        {
            return new[] { (1, 20), (2, 20), (3, 30), (4, 30) };
        }

        if (value >= 50)
        {
            return new[] { (1, 40), (2, 30), (3, 30) };
        }

        if (value >= 30)
        {
            return new[] { (1, 75), (2, 25) };
        }

        return new[] { (1, 100) };
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
