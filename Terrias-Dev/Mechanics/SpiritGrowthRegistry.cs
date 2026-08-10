using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class SpiritGrowthRegistry
{
    private static readonly object SyncRoot = new();
    private static SpiritGrowthRegistryDocument document = new();

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.SpiritGrowthRegistryFile);
            try
            {
                if (!File.Exists(path))
                {
                    document = new SpiritGrowthRegistryDocument();
                    TerriasLog.Warn("[SpiritGrowthRegistry] missing registry; using deterministic species fallbacks.");
                    return;
                }

                var loaded = AuraSharedJson.Deserialize<SpiritGrowthRegistryDocument>(File.ReadAllText(path))
                             ?? new SpiritGrowthRegistryDocument();
                if (loaded.SchemaVersion != 1)
                {
                    throw new InvalidDataException("unsupported schemaVersion=" + loaded.SchemaVersion + "; expected 1");
                }

                document = Normalize(loaded);
                TerriasLog.Info("[SpiritGrowthRegistry] loaded profiles=" + document.Profiles.Count + " from " + path);
            }
            catch (Exception ex)
            {
                document = new SpiritGrowthRegistryDocument();
                TerriasLog.Warn("[SpiritGrowthRegistry] invalid registry; using deterministic species fallbacks: " + ex.Message);
            }
        }
    }

    public static SpiritSpeciesGrowthProfile Resolve(CapturedEnemySnapshot snapshot)
    {
        snapshot ??= new CapturedEnemySnapshot();
        lock (SyncRoot)
        {
            var enemyCandidates = IdentityCandidates(snapshot.EnemyId);
            var variantCandidates = IdentityCandidates(string.IsNullOrWhiteSpace(snapshot.VariantId)
                ? snapshot.EnemyId
                : snapshot.VariantId);
            var exact = document.Profiles.FirstOrDefault(profile => enemyCandidates.Any(id => Same(profile.EnemyId, id))
                                                                    && variantCandidates.Any(id => Same(profile.VariantId, id)));
            var species = exact ?? document.Profiles.FirstOrDefault(profile => enemyCandidates.Any(id => Same(profile.EnemyId, id))
                                                                     && profile.VariantId == "*");
            return Clone(species ?? CreateFallback(snapshot));
        }
    }

    public static SpiritSpeciesTier TierFor(CapturedEnemySnapshot snapshot)
    {
        return ParseTier(Resolve(snapshot).Tier, TierFromRarity(snapshot?.Rarity ?? 1));
    }

    public static SpiritSpeciesTier TierFromRarity(int rarity)
    {
        return rarity >= 3 ? SpiritSpeciesTier.Boss : rarity == 2 ? SpiritSpeciesTier.Elite : SpiritSpeciesTier.Normal;
    }

    private static SpiritGrowthRegistryDocument Normalize(SpiritGrowthRegistryDocument source)
    {
        var profiles = new List<SpiritSpeciesGrowthProfile>();
        foreach (var item in source.Profiles ?? new List<SpiritSpeciesGrowthProfile>())
        {
            var enemyId = (item.EnemyId ?? "").Trim().Replace("*", "");
            if (enemyId.Length == 0)
            {
                continue;
            }

            var tier = ParseTier(item.Tier, SpiritSpeciesTier.Normal);
            var profile = new SpiritSpeciesGrowthProfile
            {
                EnemyId = enemyId,
                VariantId = string.IsNullOrWhiteSpace(item.VariantId) || item.VariantId.Trim() == "*"
                    ? "*"
                    : item.VariantId.Trim().Replace("*", ""),
                Tier = tier.ToString(),
                BaseOrigins = NormalizeVector(item.BaseOrigins, DefaultBaseTotal(tier), enemyId + ":base"),
                GrowthOrigins = NormalizeVector(item.GrowthOrigins, DefaultGrowthTotal(tier), enemyId + ":growth")
            };
            profiles.RemoveAll(existing => Same(existing.EnemyId, profile.EnemyId) && Same(existing.VariantId, profile.VariantId));
            profiles.Add(profile);
        }

        return new SpiritGrowthRegistryDocument { SchemaVersion = 1, Profiles = profiles };
    }

    private static SpiritSpeciesGrowthProfile CreateFallback(CapturedEnemySnapshot snapshot)
    {
        var tier = TierFromRarity(snapshot.Rarity);
        var weights = new[]
        {
            1d + Math.Max(0, snapshot.BaseAttack),
            1d + Math.Max(0, snapshot.BaseHp) / 4d,
            1d + (SpiritGrowthService.StableHash(snapshot.ProfileKey + ":luck") % 1000) / 160d,
            1d + Math.Max(0, snapshot.BaseArmor) * 1.5d
        };
        return new SpiritSpeciesGrowthProfile
        {
            EnemyId = snapshot.EnemyId,
            VariantId = string.IsNullOrWhiteSpace(snapshot.VariantId) ? snapshot.EnemyId : snapshot.VariantId,
            Tier = tier.ToString(),
            BaseOrigins = Allocate(DefaultBaseTotal(tier), weights, snapshot.ProfileKey + ":base"),
            GrowthOrigins = Allocate(DefaultGrowthTotal(tier), weights, snapshot.ProfileKey + ":growth")
        };
    }

    private static SpiritOriginVector NormalizeVector(SpiritOriginVector? vector, int fallbackTotal, string seed)
    {
        if (vector == null || vector.Total <= 0)
        {
            return Allocate(fallbackTotal, new[] { 1d, 1d, 1d, 1d }, seed);
        }

        return Allocate(vector.Total, new[]
        {
            (double)Math.Max(0, vector.Magic),
            (double)Math.Max(0, vector.Spirit),
            (double)Math.Max(0, vector.Luck),
            (double)Math.Max(0, vector.Perception)
        }, seed);
    }

    private static SpiritOriginVector Allocate(int total, IReadOnlyList<double> rawWeights, string seed)
    {
        total = Math.Max(4, total);
        var weights = rawWeights.Select(value => Math.Max(0.01d, value)).ToArray();
        var minimum = Math.Max(1, (int)Math.Ceiling(total * 0.10d));
        var maximum = Math.Max(minimum, (int)Math.Floor(total * 0.45d));
        var desired = weights.Select(value => value / weights.Sum() * total).ToArray();
        var values = Enumerable.Repeat(minimum, 4).ToArray();
        while (values.Sum() < total)
        {
            var index = Enumerable.Range(0, 4)
                .Where(candidate => values[candidate] < maximum)
                .OrderByDescending(candidate => desired[candidate] - values[candidate])
                .ThenBy(candidate => SpiritGrowthService.StableHash(seed + ":" + candidate))
                .First();
            values[index]++;
        }

        return new SpiritOriginVector { Magic = values[0], Spirit = values[1], Luck = values[2], Perception = values[3] };
    }

    private static int DefaultBaseTotal(SpiritSpeciesTier tier)
    {
        return tier switch
        {
            SpiritSpeciesTier.Elite => 36,
            SpiritSpeciesTier.Boss => 44,
            SpiritSpeciesTier.FinalBoss => 54,
            _ => 28
        };
    }

    private static int DefaultGrowthTotal(SpiritSpeciesTier tier)
    {
        return tier switch
        {
            SpiritSpeciesTier.Elite => 80,
            SpiritSpeciesTier.Boss => 100,
            SpiritSpeciesTier.FinalBoss => 120,
            _ => 64
        };
    }

    private static SpiritSpeciesTier ParseTier(string value, SpiritSpeciesTier fallback)
    {
        return Enum.TryParse(value, true, out SpiritSpeciesTier tier) ? tier : fallback;
    }

    private static SpiritSpeciesGrowthProfile Clone(SpiritSpeciesGrowthProfile source)
    {
        return new SpiritSpeciesGrowthProfile
        {
            EnemyId = source.EnemyId,
            VariantId = source.VariantId,
            Tier = source.Tier,
            BaseOrigins = source.BaseOrigins.Clone(),
            GrowthOrigins = source.GrowthOrigins.Clone()
        };
    }

    private static IReadOnlyList<string> IdentityCandidates(string rawId)
    {
        var result = new List<string>();
        void Add(string value)
        {
            var normalized = (value ?? "").Trim();
            if (normalized.Length > 0 && !result.Contains(normalized, StringComparer.Ordinal)) result.Add(normalized);
        }
        var normalizedRawId = rawId ?? "";
        Add(normalizedRawId);
        if (normalizedRawId.StartsWith("enemy_", StringComparison.Ordinal)) Add(normalizedRawId.Substring("enemy_".Length));
        foreach (var candidate in TerriasContentIdCompatibility.LookupCandidates(normalizedRawId, "terrias")) Add(candidate);
        return result;
    }

    private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
}
