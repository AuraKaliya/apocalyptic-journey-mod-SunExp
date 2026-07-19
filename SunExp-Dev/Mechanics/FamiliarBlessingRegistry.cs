using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Newtonsoft.Json;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Mechanics;

public static class FamiliarBlessingRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> SupportedEffectKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "BattleWinGold",
        "BattleRewardExtraChoice",
        "BeforeLethalStarClayBody",
        "BurnStackToEmber",
        "BurnTriggeredEmber",
        "CombatStartBuff",
        "CombatStartCard",
        "CombatStartDraw",
        "CombatStartEnemyBuffRandom",
        "CombatStartField",
        "CombatStartHeal",
        "CombatStartModifiedCard",
        "CombatStartRandomModification",
        "CombatStartResource",
        "CombatStartShield",
        "CardUseAdventureOrigin",
        "CompositeDoomProcPlayerBuff",
        "CompositeDoomProcRandomEnemyBundle",
        "CrowEveryNthSettlementHpDamage",
        "CrowExtraSettlement",
        "DamageNormalEchoByBuff",
        "DamageTrueEchoByBuff",
        "DuskAfterheatMultiplierAndCap",
        "EmberOffsetBurnTransfer",
        "EnemyDeathBurnTransfer",
        "EnemyDeathPersistentSoul",
        "FirstActionResource",
        "FirstDamageTargetBuff",
        "FirstDamageTargetBuffPerRound",
        "FirstDamageTrueEchoPerRound",
        "FirstStarScoreExtraBlessing",
        "AfterResurrectionRecovery",
        "NetherChaseRebirthBonus",
        "PocketCardReplacePerRound",
        "RoundStartExtraordinaryPerEnemyDebuffKind",
        "RunDiceBonus",
        "StarScoreCadenceRandomOverture",
        "StarlightCycleBuffs",
        "StarlightCycleStarClayShape",
        "UnusedNetherChaseWinBlessing"
    };

    private static FamiliarBlessingRegistryDocument document = Normalize(BuiltInDocument());

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var merged = new FamiliarBlessingRegistryDocument { SchemaVersion = 3, OwnerModId = SunExpIds.ModId };
            MergeInto(merged, BuiltInDocument(), "built-in");
            var mainPath = Path.Combine(modConfig.DirectoryName, SunExpIds.FamiliarBlessingRegistryFile);
            if (TryRead(mainPath, SunExpIds.ModId, out var main))
            {
                MergeInto(merged, main, mainPath);
            }
            else
            {
                SunExpLog.Warn("[FamiliarGrowth] missing or invalid main blessing registry; using built-in fallback.");
            }

            foreach (var path in ExtensionRegistryPaths(modConfig.DirectoryName))
            {
                if (TryRead(path, Path.GetFileName(Path.GetDirectoryName(path)) ?? "External", out var extension))
                {
                    MergeInto(merged, extension, path);
                }
            }

            document = Normalize(merged);
            SunExpLog.Info("[FamiliarGrowth] loaded blessing registry: blessings=" + document.Blessings.Count
                           + ", speciesProfiles=" + document.SpeciesProfiles.Count + ".");
        }
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> All()
    {
        lock (SyncRoot)
        {
            return document.Blessings.ToArray();
        }
    }

    public static IReadOnlyList<FamiliarSpeciesGrowthProfile> SpeciesProfiles()
    {
        lock (SyncRoot)
        {
            return document.SpeciesProfiles.ToArray();
        }
    }

    public static FamiliarBlessingDefinition? Find(string blessingId)
    {
        var id = (blessingId ?? "").Trim();
        lock (SyncRoot)
        {
            return document.Blessings.FirstOrDefault(blessing => string.Equals(blessing.Id, id, StringComparison.Ordinal));
        }
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> GrowthEligible(FamiliarInstance instance, int milestone, int maxTier)
    {
        lock (SyncRoot)
        {
            return document.Blessings
                .Where(IsGrowth)
                .Where(blessing => string.Equals(blessing.Pool, "common", StringComparison.OrdinalIgnoreCase))
                .Where(blessing => blessing.RequiredLevel <= milestone && blessing.Tier <= maxTier)
                .Where(blessing => Allows(blessing, instance))
                .OrderBy(blessing => blessing.Tier)
                .ThenBy(blessing => blessing.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> GenericFinals(FamiliarInstance instance)
    {
        lock (SyncRoot)
        {
            return document.Blessings
                .Where(blessing => string.Equals(blessing.Category, FamiliarBlessingCategory.FinalGeneric, StringComparison.Ordinal))
                .Where(blessing => string.Equals(blessing.Pool, "final_common", StringComparison.OrdinalIgnoreCase))
                .Where(blessing => Allows(blessing, instance))
                .OrderBy(blessing => blessing.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static IReadOnlyList<FamiliarBlessingDefinition> SpecificFinals(FamiliarInstance instance)
    {
        lock (SyncRoot)
        {
            var profile = FindProfile(instance);
            if (profile != null && profile.FinalBlessingIds.Count > 0)
            {
                var ids = new HashSet<string>(profile.FinalBlessingIds, StringComparer.Ordinal);
                var explicitFinals = document.Blessings
                    .Where(blessing => ids.Contains(blessing.Id)
                        && string.Equals(blessing.Category, FamiliarBlessingCategory.FinalSpecies, StringComparison.Ordinal)
                        && IsSpeciesFinalPool(blessing.Pool))
                    .ToArray();
                if (explicitFinals.Length > 0)
                {
                    return explicitFinals;
                }
            }

            if (profile != null && profile.Tags.Count > 0)
            {
                var tags = new HashSet<string>(profile.Tags, StringComparer.OrdinalIgnoreCase);
                var tagged = document.Blessings
                    .Where(blessing => string.Equals(blessing.Category, FamiliarBlessingCategory.FinalTag, StringComparison.Ordinal))
                    .Where(blessing => string.Equals(blessing.Pool, "final_tag", StringComparison.OrdinalIgnoreCase))
                    .Where(blessing => blessing.RequiredTags.Count > 0 && blessing.RequiredTags.Any(tags.Contains))
                    .ToArray();
                if (tagged.Length > 0)
                {
                    return tagged;
                }
            }

            return document.Blessings
                .Where(blessing => string.Equals(blessing.Category, FamiliarBlessingCategory.FinalSpecies, StringComparison.Ordinal))
                .Where(blessing => IsSpeciesFinalPool(blessing.Pool))
                .Where(blessing => AllowsSpecies(blessing, instance))
                .ToArray();
        }
    }

    public static bool IsGrowth(FamiliarBlessingDefinition blessing)
    {
        return string.Equals(blessing.Category, FamiliarBlessingCategory.Growth, StringComparison.Ordinal);
    }

    public static bool IsFinal(FamiliarBlessingDefinition blessing)
    {
        return string.Equals(blessing.Category, FamiliarBlessingCategory.FinalGeneric, StringComparison.Ordinal)
               || string.Equals(blessing.Category, FamiliarBlessingCategory.FinalSpecies, StringComparison.Ordinal)
               || string.Equals(blessing.Category, FamiliarBlessingCategory.FinalTag, StringComparison.Ordinal);
    }

    public static bool Allows(FamiliarBlessingDefinition blessing, FamiliarInstance instance)
    {
        if (blessing == null || instance == null)
        {
            return false;
        }

        if (blessing.AllowedSpecies.Count > 0 && !AllowsSpecies(blessing, instance))
        {
            return false;
        }

        if (string.Equals(blessing.Category, FamiliarBlessingCategory.FinalTag, StringComparison.Ordinal))
        {
            var profile = FindProfile(instance);
            return profile != null && blessing.RequiredTags.Any(required => profile.Tags.Contains(required, StringComparer.OrdinalIgnoreCase));
        }

        return true;
    }

    public static bool HasTag(FamiliarInstance instance, string tag)
    {
        var wanted = (tag ?? "").Trim();
        var blessings = new HashSet<string>(instance?.AllBlessingIds() ?? Array.Empty<string>(), StringComparer.Ordinal);
        lock (SyncRoot)
        {
            return wanted.Length > 0 && document.Blessings.Any(blessing =>
                blessings.Contains(blessing.Id)
                && blessing.Tags.Any(value => string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public static bool HasEffect(FamiliarInstance instance, string effectKind)
    {
        var wanted = (effectKind ?? "").Trim();
        var blessings = new HashSet<string>(instance?.AllBlessingIds() ?? Array.Empty<string>(), StringComparer.Ordinal);
        lock (SyncRoot)
        {
            return wanted.Length > 0 && document.Blessings.Any(blessing =>
                blessings.Contains(blessing.Id)
                && blessing.Effects.Any(effect => string.Equals(effect.Kind, wanted, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static FamiliarSpeciesGrowthProfile? FindProfile(FamiliarInstance instance)
    {
        return document.SpeciesProfiles.FirstOrDefault(profile =>
            string.Equals(profile.FullSpeciesId, instance.FullSpeciesId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(profile.SpeciesId)
                && string.Equals(FamiliarId.NormalizeSpeciesId(profile.SpeciesId), FamiliarId.NormalizeSpeciesId(instance.SpeciesId), StringComparison.OrdinalIgnoreCase)));
    }

    private static bool AllowsSpecies(FamiliarBlessingDefinition blessing, FamiliarInstance instance)
    {
        return blessing.AllowedSpecies.Count == 0
               || blessing.AllowedSpecies.Contains("*")
               || blessing.AllowedSpecies.Any(value =>
                   string.Equals(value, instance.FullSpeciesId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(FamiliarId.NormalizeSpeciesId(value), FamiliarId.NormalizeSpeciesId(instance.SpeciesId), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSpeciesFinalPool(string pool)
    {
        var value = (pool ?? "").Trim();
        return value.StartsWith("final_", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "final_common", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(value, "final_tag", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRead(string path, string defaultOwner, out FamiliarBlessingRegistryDocument result)
    {
        result = new FamiliarBlessingRegistryDocument();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            result = JsonConvert.DeserializeObject<FamiliarBlessingRegistryDocument>(File.ReadAllText(path))
                     ?? new FamiliarBlessingRegistryDocument();
            if (string.IsNullOrWhiteSpace(result.OwnerModId))
            {
                result.OwnerModId = defaultOwner;
            }

            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[FamiliarGrowth] ignored invalid registry " + path + ": " + ex.Message);
            return false;
        }
    }

    private static IEnumerable<string> ExtensionRegistryPaths(string ownModDirectory)
    {
        var modsDirectory = AuraSharedPaths.ModsDirectory;
        if (string.IsNullOrWhiteSpace(modsDirectory) || !Directory.Exists(modsDirectory))
        {
            yield break;
        }

        string ownFull;
        try
        {
            ownFull = Path.GetFullPath(ownModDirectory);
        }
        catch
        {
            ownFull = ownModDirectory;
        }

        foreach (var directory in Directory.GetDirectories(modsDirectory))
        {
            string full;
            try
            {
                full = Path.GetFullPath(directory);
            }
            catch
            {
                continue;
            }

            if (string.Equals(full, ownFull, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = Path.Combine(full, SunExpIds.FamiliarBlessingRegistryFile);
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static void MergeInto(FamiliarBlessingRegistryDocument target, FamiliarBlessingRegistryDocument source, string sourceName)
    {
        target.SchemaVersion = Math.Max(target.SchemaVersion, source.SchemaVersion);
        var owner = string.IsNullOrWhiteSpace(source.OwnerModId) ? SunExpIds.ModId : source.OwnerModId.Trim();
        foreach (var blessing in source.Blessings ?? new List<FamiliarBlessingDefinition>())
        {
            blessing.OwnerModId = string.IsNullOrWhiteSpace(blessing.OwnerModId) ? owner : blessing.OwnerModId.Trim();
            var unsupported = blessing.Effects?.FirstOrDefault(effect => !SupportedEffectKinds.Contains(effect.Kind ?? ""));
            if (unsupported != null)
            {
                SunExpLog.Warn("[FamiliarGrowth] rejected blessing " + blessing.Id + " from " + sourceName
                               + ": unsupported effect " + unsupported.Kind + ".");
                continue;
            }

            target.Blessings.RemoveAll(existing => string.Equals(existing.Id, blessing.Id, StringComparison.Ordinal));
            target.Blessings.Add(blessing);
        }

        foreach (var profile in source.SpeciesProfiles ?? new List<FamiliarSpeciesGrowthProfile>())
        {
            target.SpeciesProfiles.RemoveAll(existing =>
                string.Equals(existing.FullSpeciesId, profile.FullSpeciesId, StringComparison.OrdinalIgnoreCase));
            target.SpeciesProfiles.Add(profile);
        }
    }

    private static FamiliarBlessingRegistryDocument Normalize(FamiliarBlessingRegistryDocument source)
    {
        var result = new FamiliarBlessingRegistryDocument
        {
            SchemaVersion = Math.Max(3, source.SchemaVersion),
            OwnerModId = string.IsNullOrWhiteSpace(source.OwnerModId) ? SunExpIds.ModId : source.OwnerModId.Trim()
        };
        foreach (var blessing in source.Blessings ?? new List<FamiliarBlessingDefinition>())
        {
            var id = (blessing.Id ?? "").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            blessing.Id = id;
            blessing.OwnerModId = string.IsNullOrWhiteSpace(blessing.OwnerModId) ? result.OwnerModId : blessing.OwnerModId.Trim();
            blessing.Name = string.IsNullOrWhiteSpace(blessing.Name) ? id : blessing.Name.Trim();
            blessing.Description = blessing.Description?.Trim() ?? "";
            blessing.IconPath = blessing.IconPath?.Trim() ?? "";
            blessing.Category = NormalizeCategory(blessing.Category);
            blessing.Tier = Math.Max(1, Math.Min(5, blessing.Tier));
            blessing.Weight = Math.Max(0, blessing.Weight);
            blessing.Pool = string.IsNullOrWhiteSpace(blessing.Pool)
                ? DefaultPool(blessing.Category)
                : blessing.Pool.Trim().ToLowerInvariant();
            blessing.ExclusiveGroup = blessing.ExclusiveGroup?.Trim() ?? "";
            blessing.RequiredLevel = Math.Max(1, Math.Min(FamiliarRosterService.MaxLevel, blessing.RequiredLevel));
            blessing.MaxRank = 1;
            blessing.AllowedSpecies = NormalizeList(blessing.AllowedSpecies, allowWildcard: true);
            blessing.RequiredTags = NormalizeList(blessing.RequiredTags, allowWildcard: false);
            blessing.Tags = NormalizeList(blessing.Tags, allowWildcard: false);
            blessing.Effects ??= new List<FamiliarBlessingEffect>();
            foreach (var effect in blessing.Effects)
            {
                effect.Kind = effect.Kind?.Trim() ?? "";
                effect.Value = effect.Value?.Trim() ?? "";
                effect.Pool = effect.Pool?.Trim() ?? "";
                effect.Amount = Math.Max(0, effect.Amount);
                effect.Parameters = (effect.Parameters ?? new Dictionary<string, string>())
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .ToDictionary(
                        pair => pair.Key.Trim(),
                        pair => pair.Value?.Trim() ?? "",
                        StringComparer.OrdinalIgnoreCase);
            }

            result.Blessings.RemoveAll(existing => string.Equals(existing.Id, blessing.Id, StringComparison.Ordinal));
            result.Blessings.Add(blessing);
        }

        foreach (var profile in source.SpeciesProfiles ?? new List<FamiliarSpeciesGrowthProfile>())
        {
            profile.FullSpeciesId = FamiliarId.NormalizeFullSpeciesId(profile.FullSpeciesId, profile.SpeciesId);
            profile.SpeciesId = FamiliarId.NormalizeSpeciesId(profile.SpeciesId.Length > 0 ? profile.SpeciesId : profile.FullSpeciesId);
            profile.DisplayName = profile.DisplayName?.Trim() ?? "";
            profile.Tags = NormalizeList(profile.Tags, allowWildcard: false);
            profile.FinalBlessingIds = (profile.FinalBlessingIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (profile.FullSpeciesId.Length > 0)
            {
                result.SpeciesProfiles.RemoveAll(existing =>
                    string.Equals(existing.FullSpeciesId, profile.FullSpeciesId, StringComparison.OrdinalIgnoreCase));
                result.SpeciesProfiles.Add(profile);
            }
        }

        result.Blessings = result.Blessings.OrderBy(item => item.Category).ThenBy(item => item.Tier).ThenBy(item => item.Id).ToList();
        return result;
    }

    private static string NormalizeCategory(string category)
    {
        var value = (category ?? "").Trim().ToLowerInvariant();
        return value == FamiliarBlessingCategory.FinalGeneric
               || value == FamiliarBlessingCategory.FinalSpecies
               || value == FamiliarBlessingCategory.FinalTag
            ? value
            : FamiliarBlessingCategory.Growth;
    }

    private static string DefaultPool(string category)
    {
        return category switch
        {
            FamiliarBlessingCategory.FinalGeneric => "final_common",
            FamiliarBlessingCategory.FinalTag => "final_tag",
            FamiliarBlessingCategory.FinalSpecies => "final_species",
            _ => "common"
        };
    }

    private static List<string> NormalizeList(IEnumerable<string>? values, bool allowWildcard)
    {
        var result = new List<string>();
        foreach (var value in values ?? Array.Empty<string>())
        {
            var clean = (value ?? "").Trim();
            if (clean.Length == 0 || (!allowWildcard && clean == "*"))
            {
                continue;
            }

            if (!result.Contains(clean, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(clean);
            }
        }

        return result;
    }

    private static FamiliarBlessingRegistryDocument BuiltInDocument()
    {
        return new FamiliarBlessingRegistryDocument
        {
            SchemaVersion = 3,
            OwnerModId = SunExpIds.ModId,
            Blessings = new List<FamiliarBlessingDefinition>
            {
                new() { Id = "*familiar_guard_paw", Name = "防护", Description = "战斗开始时，获得10点护盾。", Category = FamiliarBlessingCategory.Growth, Tier = 1, RequiredLevel = 2, Effects = new List<FamiliarBlessingEffect> { new() { Kind = "CombatStartShield", Amount = 10 } } },
                new() { Id = "*familiar_first_aid", Name = "治疗", Description = "战斗开始时，恢复5点生命。", Category = FamiliarBlessingCategory.Growth, Tier = 1, RequiredLevel = 2, Effects = new List<FamiliarBlessingEffect> { new() { Kind = "CombatStartHeal", Amount = 5 } } },
                new() { Id = "*familiar_reward_omen", Name = "这是我应得的", Description = "战斗奖励额外出现1个选择。", Category = FamiliarBlessingCategory.FinalGeneric, Tier = 5, RequiredLevel = 8, Effects = new List<FamiliarBlessingEffect> { new() { Kind = "BattleRewardExtraChoice", Amount = 1 } } },
                new() { Id = "*familiar_law_of_luck", Name = "幸运之骰", Description = "本轮冒险中，数值骰和检定骰的结果各增加20。", Category = FamiliarBlessingCategory.FinalGeneric, Tier = 5, RequiredLevel = 8, Effects = new List<FamiliarBlessingEffect> { new() { Kind = "RunDiceBonus", Amount = 20 } } }
            }
        };
    }
}
