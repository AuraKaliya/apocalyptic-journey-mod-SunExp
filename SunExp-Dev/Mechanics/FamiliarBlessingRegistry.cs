using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Mechanics;

public static class FamiliarBlessingRegistry
{
    private static readonly object SyncRoot = new();
    private static FamiliarBlessingRegistryDocument document = BuiltInDocument();

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var fallback = BuiltInDocument();
            var path = Path.Combine(modConfig.DirectoryName, SunExpIds.FamiliarBlessingRegistryFile);
            if (!File.Exists(path))
            {
                document = Normalize(fallback, fallback);
                SunExpLog.Warn("[FamiliarGrowth] missing blessing registry; using built-in familiar blessings.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<FamiliarBlessingRegistryDocument>(File.ReadAllText(path))
                             ?? new FamiliarBlessingRegistryDocument();
                document = Normalize(loaded, new FamiliarBlessingRegistryDocument());
                SunExpLog.Info("[FamiliarGrowth] loaded familiar blessing registry from " + path);
            }
            catch (Exception ex)
            {
                document = Normalize(fallback, fallback);
                SunExpLog.Warn("[FamiliarGrowth] failed to load blessing registry; using built-in blessings: " + ex.Message);
            }
        }
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> All()
    {
        lock (SyncRoot)
        {
            return document.Blessings.ToArray();
        }
    }

    public static FamiliarBlessingDefinition? Find(string blessingId)
    {
        var id = (blessingId ?? "").Trim();
        if (id.Length == 0)
        {
            return null;
        }

        lock (SyncRoot)
        {
            return document.Blessings.FirstOrDefault(blessing => SameId(blessing.Id, id));
        }
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> UnlocksFor(FamiliarInstance instance)
    {
        return EligibleFor(instance);
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> EligibleFor(FamiliarInstance instance)
    {
        if (instance == null)
        {
            return Array.Empty<FamiliarBlessingDefinition>();
        }

        var speciesId = FamiliarId.NormalizeSpeciesId(instance.SpeciesId);
        lock (SyncRoot)
        {
            return document.Blessings
                .Where(blessing => blessing.RequiredLevel <= Math.Max(1, instance.Level)
                                   && AllowsSpecies(blessing, speciesId))
                .OrderBy(blessing => blessing.Tier)
                .ThenBy(blessing => blessing.RequiredLevel)
                .ThenBy(blessing => blessing.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static bool Allows(FamiliarBlessingDefinition blessing, string speciesId)
    {
        return AllowsSpecies(blessing, FamiliarId.NormalizeSpeciesId(speciesId));
    }

    public static bool HasTag(FamiliarInstance instance, string tag)
    {
        var wanted = (tag ?? "").Trim();
        if (instance == null || wanted.Length == 0)
        {
            return false;
        }

        var blessings = new HashSet<string>(instance.Blessings ?? new List<string>(), StringComparer.Ordinal);
        lock (SyncRoot)
        {
            return document.Blessings.Any(blessing =>
                blessings.Contains(blessing.Id)
                && blessing.Tags.Any(value => string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public static bool HasEffect(FamiliarInstance instance, string effectKind)
    {
        var wanted = (effectKind ?? "").Trim();
        if (instance == null || wanted.Length == 0)
        {
            return false;
        }

        var blessings = new HashSet<string>(instance.Blessings ?? new List<string>(), StringComparer.Ordinal);
        lock (SyncRoot)
        {
            return document.Blessings.Any(blessing =>
                blessings.Contains(blessing.Id)
                && blessing.Effects.Any(effect => string.Equals(effect.Kind, wanted, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static FamiliarBlessingRegistryDocument Normalize(
        FamiliarBlessingRegistryDocument loaded,
        FamiliarBlessingRegistryDocument fallback)
    {
        var result = new FamiliarBlessingRegistryDocument();
        var map = new Dictionary<string, FamiliarBlessingDefinition>(StringComparer.Ordinal);
        foreach (var blessing in fallback.Blessings.Concat(loaded.Blessings ?? new List<FamiliarBlessingDefinition>()))
        {
            var id = (blessing.Id ?? "").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            blessing.Id = id;
            blessing.Name = string.IsNullOrWhiteSpace(blessing.Name) ? id : blessing.Name.Trim();
            blessing.Description = blessing.Description?.Trim() ?? "";
            blessing.IconPath = blessing.IconPath?.Trim() ?? "";
            blessing.Tier = Math.Max(1, Math.Min(5, blessing.Tier));
            blessing.Weight = Math.Max(0, blessing.Weight);
            blessing.Pool = string.IsNullOrWhiteSpace(blessing.Pool) ? "common" : FamiliarId.Sanitize(blessing.Pool).ToLowerInvariant();
            blessing.ExclusiveGroup = blessing.ExclusiveGroup?.Trim() ?? "";
            blessing.RequiredLevel = Math.Max(1, blessing.RequiredLevel);
            blessing.MaxRank = Math.Max(1, blessing.MaxRank);
            blessing.AllowedSpecies = NormalizeList(blessing.AllowedSpecies, allowWildcard: true);
            blessing.Tags = NormalizeList(blessing.Tags, allowWildcard: false);
            blessing.Effects ??= new List<FamiliarBlessingEffect>();
            foreach (var effect in blessing.Effects)
            {
                effect.Kind = effect.Kind?.Trim() ?? "";
                effect.Value = effect.Value?.Trim() ?? "";
                effect.Pool = effect.Pool?.Trim() ?? "";
                effect.Amount = Math.Max(0, effect.Amount);
            }

            if (blessing.AllowedSpecies.Count == 0)
            {
                blessing.AllowedSpecies.Add("*");
            }

            map[id] = blessing;
        }

        result.Blessings = map.Values
            .OrderBy(blessing => blessing.Tier)
            .ThenBy(blessing => blessing.RequiredLevel)
            .ThenBy(blessing => blessing.Id, StringComparer.Ordinal)
            .ToList();
        return result;
    }

    private static List<string> NormalizeList(IEnumerable<string>? values, bool allowWildcard)
    {
        var result = new List<string>();
        foreach (var value in values ?? Array.Empty<string>())
        {
            var text = (value ?? "").Trim();
            var clean = allowWildcard && text == "*" ? "*" : FamiliarId.NormalizeSpeciesId(text);
            if (clean.Length > 0 && !result.Contains(clean))
            {
                result.Add(clean);
            }
        }

        return result;
    }

    private static bool AllowsSpecies(FamiliarBlessingDefinition blessing, string speciesId)
    {
        return blessing.AllowedSpecies.Contains("*")
               || blessing.AllowedSpecies.Any(value => string.Equals(value, speciesId, StringComparison.Ordinal));
    }

    private static bool SameId(string? left, string? right)
    {
        return string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    }

    private static FamiliarBlessingRegistryDocument BuiltInDocument()
    {
        return new FamiliarBlessingRegistryDocument
        {
            Blessings = new List<FamiliarBlessingDefinition>
            {
                new()
                {
                    Id = "sunexp.familiar.bond_spark",
                    Name = "\u7f81\u7eca\u5fae\u5149",
                    Description = "\u8fd9\u53ea\u4f7f\u9b54\u5df2\u7ecf\u80fd\u7a33\u5b9a\u56de\u5e94\u547c\u5524\u3002",
                    RequiredLevel = 2,
                    AllowedSpecies = new List<string> { "*" },
                    Tags = new List<string> { "growth", "bond" }
                },
                new()
                {
                    Id = "sunexp.familiar.dusk_afterheat_focus",
                    Name = "\u4f59\u70ed\u4eb2\u548c",
                    Description = "\u9ec4\u660f\u66f4\u5bb9\u6613\u4fdd\u7559\u707c\u70e7\u7184\u706d\u524d\u7684\u4f59\u6e29\u3002",
                    RequiredLevel = 3,
                    AllowedSpecies = new List<string> { "dusk" },
                    Tags = new List<string> { "dusk", "afterheat" }
                },
                new()
                {
                    Id = "sunexp.familiar.star_clay_memory",
                    Name = "\u661f\u6ce5\u8bb0\u5fc6",
                    Description = "\u661f\u6ce5\u4eba\u5080\u5b66\u4f1a\u628a\u4e00\u70b9\u5149\u7559\u5230\u4e0b\u4e00\u6b21\u884c\u52a8\u91cc\u3002",
                    RequiredLevel = 3,
                    AllowedSpecies = new List<string> { "star_clay_doll" },
                    Tags = new List<string> { "star_clay", "starlight" }
                },
                new()
                {
                    Id = "sunexp.familiar.manifest",
                    Name = "\u73b0\u5f62",
                    Description = "\u89e3\u9501\u4f5c\u4e3a\u53cb\u65b9\u5355\u4f4d\u73b0\u5f62\u7684\u7cfb\u7edf\u6743\u9650\u3002",
                    RequiredLevel = 5,
                    AllowedSpecies = new List<string> { "*" },
                    Tags = new List<string> { "manifest", "combat" },
                    Effects = new List<FamiliarBlessingEffect>
                    {
                        new() { Kind = "ManifestEnable" },
                        new() { Kind = "CompanionIntentPoolPatch", Pool = "projection.default" }
                    }
                }
            }
        };
    }
}
