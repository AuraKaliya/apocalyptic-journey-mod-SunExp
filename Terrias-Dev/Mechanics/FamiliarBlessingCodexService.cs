using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

public sealed class FamiliarBlessingCodexEntry
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public int Tier { get; set; }

    public string TierLabel { get; set; } = "";
}

public sealed class FamiliarBlessingCodexPool
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public int Order { get; set; }

    public List<FamiliarBlessingCodexEntry> Blessings { get; set; } = new();
}

public static class FamiliarBlessingCodexService
{
    public static IReadOnlyList<FamiliarBlessingCodexPool> Pools()
    {
        return Build(
            FamiliarBlessingRegistry.All(),
            FamiliarGrowthService.Species(),
            FamiliarBlessingRegistry.SpeciesProfiles());
    }

    public static IReadOnlyList<FamiliarBlessingCodexPool> Build(
        IEnumerable<FamiliarBlessingDefinition> blessings,
        IReadOnlyList<FamiliarSpeciesSpec> species)
    {
        return Build(blessings, species, Array.Empty<FamiliarSpeciesGrowthProfile>());
    }

    public static IReadOnlyList<FamiliarBlessingCodexPool> Build(
        IEnumerable<FamiliarBlessingDefinition> blessings,
        IReadOnlyList<FamiliarSpeciesSpec> species,
        IReadOnlyList<FamiliarSpeciesGrowthProfile> profiles)
    {
        return blessings
            .Where(blessing => !string.IsNullOrWhiteSpace(blessing.Pool))
            .GroupBy(blessing => blessing.Pool.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => CreatePool(group.Key, group, species, profiles))
            .OrderBy(pool => pool.Order)
            .ThenBy(pool => pool.Name, StringComparer.Ordinal)
            .ThenBy(pool => pool.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FamiliarBlessingCodexPool CreatePool(
        string poolId,
        IEnumerable<FamiliarBlessingDefinition> blessings,
        IReadOnlyList<FamiliarSpeciesSpec> species,
        IReadOnlyList<FamiliarSpeciesGrowthProfile> profiles)
    {
        var entries = blessings.ToList();
        var profileIndex = ProfilePoolIndex(entries, profiles);
        var speciesIndex = SpeciesPoolIndex(entries, species, profiles, profileIndex);
        return new FamiliarBlessingCodexPool
        {
            Id = poolId,
            Name = PoolName(poolId, entries, species, speciesIndex, profiles, profileIndex),
            Order = PoolOrder(poolId, profileIndex, speciesIndex),
            Blessings = entries
                .OrderBy(blessing => blessing.Tier)
                .ThenBy(blessing => blessing.Id, StringComparer.Ordinal)
                .Select(blessing => new FamiliarBlessingCodexEntry
                {
                    Id = blessing.Id,
                    Name = blessing.Name,
                    Description = blessing.Description,
                    Tier = blessing.Tier,
                    TierLabel = TierLabel(blessing.Tier)
                })
                .ToList()
        };
    }

    private static int SpeciesPoolIndex(
        IReadOnlyList<FamiliarBlessingDefinition> blessings,
        IReadOnlyList<FamiliarSpeciesSpec> species,
        IReadOnlyList<FamiliarSpeciesGrowthProfile> profiles,
        int profileIndex)
    {
        if (profileIndex >= 0 && profileIndex < profiles.Count)
        {
            var profile = profiles[profileIndex];
            for (var index = 0; index < species.Count; index++)
            {
                if (FamiliarId.Matches(profile.FullSpeciesId, species[index])
                    || FamiliarId.Matches(profile.SpeciesId, species[index]))
                {
                    return index;
                }
            }
        }

        var allowed = blessings
            .SelectMany(blessing => blessing.AllowedSpecies)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "*")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < species.Count; index++)
        {
            if (allowed.Any(value => FamiliarId.Matches(value, species[index])))
            {
                return index;
            }
        }

        return -1;
    }

    private static int ProfilePoolIndex(
        IReadOnlyList<FamiliarBlessingDefinition> blessings,
        IReadOnlyList<FamiliarSpeciesGrowthProfile> profiles)
    {
        var blessingIds = new HashSet<string>(blessings.Select(blessing => blessing.Id), StringComparer.Ordinal);
        for (var index = 0; index < profiles.Count; index++)
        {
            if (profiles[index].FinalBlessingIds.Any(blessingIds.Contains))
            {
                return index;
            }
        }

        return -1;
    }

    private static string PoolName(
        string poolId,
        IReadOnlyList<FamiliarBlessingDefinition> blessings,
        IReadOnlyList<FamiliarSpeciesSpec> species,
        int speciesIndex,
        IReadOnlyList<FamiliarSpeciesGrowthProfile> profiles,
        int profileIndex)
    {
        if (string.Equals(poolId, "common", StringComparison.OrdinalIgnoreCase))
        {
            return "通用祝福";
        }

        if (string.Equals(poolId, "final_common", StringComparison.OrdinalIgnoreCase))
        {
            return "通用最终祝福";
        }

        if (string.Equals(poolId, "final_tag", StringComparison.OrdinalIgnoreCase))
        {
            return "羁绊最终祝福";
        }

        if (profileIndex >= 0
            && profileIndex < profiles.Count
            && !string.IsNullOrWhiteSpace(profiles[profileIndex].DisplayName))
        {
            return profiles[profileIndex].DisplayName;
        }

        if (speciesIndex >= 0 && speciesIndex < species.Count)
        {
            return species[speciesIndex].DisplayName;
        }

        var owner = blessings
            .Select(blessing => blessing.OwnerModId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(owner)
            ? poolId
            : owner + " · " + poolId;
    }

    private static int PoolOrder(string poolId, int profileIndex, int speciesIndex)
    {
        if (string.Equals(poolId, "common", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(poolId, "final_common", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (string.Equals(poolId, "final_tag", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if (profileIndex >= 0)
        {
            return 100 + profileIndex;
        }

        return speciesIndex >= 0 ? 500 + speciesIndex : 1000;
    }

    private static string TierLabel(int tier)
    {
        return tier switch
        {
            1 => "Ⅰ阶",
            2 => "Ⅱ阶",
            3 => "Ⅲ阶",
            4 => "Ⅳ阶",
            5 => "Ⅴ阶",
            _ => tier + "阶"
        };
    }
}
