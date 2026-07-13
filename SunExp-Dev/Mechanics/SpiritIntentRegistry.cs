using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Mechanics;

public static class SpiritIntentRegistry
{
    private static readonly object SyncRoot = new();
    private static SpiritIntentRegistryDocument document = BuiltInDocument();

    public static string RegistryHash
    {
        get
        {
            lock (SyncRoot)
            {
                unchecked
                {
                    uint hash = 2166136261;
                    foreach (var character in AuraSharedJson.Serialize(document))
                    {
                        hash = (hash ^ character) * 16777619;
                    }

                    return hash.ToString("x8");
                }
            }
        }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var path = Path.Combine(modConfig.DirectoryName, SunExpIds.SpiritIntentRegistryFile);
            if (!File.Exists(path))
            {
                document = BuiltInDocument();
                SunExpLog.Warn("[SpiritIntentRegistry] missing registry; using projection common pool.");
                return;
            }

            try
            {
                var loaded = AuraSharedJson.Deserialize<SpiritIntentRegistryDocument>(File.ReadAllText(path))
                    ?? new SpiritIntentRegistryDocument();
                if (loaded.SchemaVersion != 1)
                {
                    throw new InvalidDataException("unsupported schemaVersion=" + loaded.SchemaVersion + "; expected 1");
                }

                document = Normalize(loaded);
                SunExpLog.Info("[SpiritIntentRegistry] loaded profiles=" + document.Profiles.Count + " from " + path);
            }
            catch (Exception ex)
            {
                document = BuiltInDocument();
                SunExpLog.Warn("[SpiritIntentRegistry] failed to load registry; using projection common pool: " + ex.Message);
            }
        }
    }

    public static SpiritIntentProfile ProfileFor(string profileKey)
    {
        ParseProfileKey(profileKey, out var enemyId, out var variantId);
        lock (SyncRoot)
        {
            return document.Profiles.FirstOrDefault(profile => Same(profile.EnemyId, enemyId) && Same(profile.VariantId, variantId))
                ?? document.Profiles.FirstOrDefault(profile => Same(profile.EnemyId, enemyId) && profile.VariantId == "*")
                ?? document.Profiles.First(profile => profile.EnemyId == "*" && profile.VariantId == "*");
        }
    }

    public static IReadOnlyList<CompanionIntentDefinition> IntentsFor(string profileKey, CompanionIntentTendency tendency)
    {
        lock (SyncRoot)
        {
            var profile = ProfileFor(profileKey);
            var ids = tendency == CompanionIntentTendency.Attack ? profile.AttackTendency : profile.DefenseTendency;
            var resolved = ids.Select(FindUnlocked).Where(intent => intent != null).Cast<CompanionIntentDefinition>().ToArray();
            return resolved.Length > 0
                ? resolved
                : CompanionIntentRegistry.IntentsForRole("*", tendency);
        }
    }

    public static CompanionIntentDefinition? Find(string intentId)
    {
        lock (SyncRoot)
        {
            return FindUnlocked(intentId);
        }
    }

    public static (int Attack, int Defense) TendencyWeightsFor(string profileKey)
    {
        var profile = ProfileFor(profileKey);
        return (Math.Max(1, profile.AttackWeight), Math.Max(1, profile.DefenseWeight));
    }

    private static CompanionIntentDefinition? FindUnlocked(string intentId)
    {
        return document.Intents.FirstOrDefault(intent => Same(intent.Id, intentId))
            ?? CompanionIntentRegistry.Find(intentId);
    }

    private static SpiritIntentRegistryDocument Normalize(SpiritIntentRegistryDocument loaded)
    {
        var intents = new Dictionary<string, CompanionIntentDefinition>(StringComparer.Ordinal);
        foreach (var intent in loaded.Intents ?? new List<CompanionIntentDefinition>())
        {
            var id = (intent.Id ?? "").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            intent.Id = id;
            intent.EnemyCardId = (intent.EnemyCardId ?? SunExpIds.ProjectionActionWaitCardId).Trim();
            intent.Type = (intent.Type ?? "Attack").Trim();
            intent.HandlerId = (intent.HandlerId ?? "").Trim();
            intent.Target ??= new CompanionIntentTargetSpec();
            intent.Threat ??= new CompanionIntentThreatSpec();
            intent.HitCount = Math.Max(1, intent.HitCount);
            intent.Cost = Math.Max(0, intent.Cost);
            intent.Cooldown = Math.Max(0, intent.Cooldown);
            intent.BasePriority = Math.Max(1, intent.BasePriority);
            var reason = "";
            var valid = Enum.TryParse(intent.Type, true, out CompanionIntentType _)
                && CompanionTargetPolicyRegistry.ValidateSpec(intent.Target, out reason)
                && CompanionIntentHandlerRegistry.Validate(intent, out reason);
            if (valid)
            {
                intents[id] = intent;
            }
            else
            {
                SunExpLog.Warn("[SpiritIntentRegistry] rejected invalid intent " + id + ": " + reason);
            }
        }

        var profiles = new List<SpiritIntentProfile>();
        foreach (var profile in loaded.Profiles ?? new List<SpiritIntentProfile>())
        {
            var enemyId = string.IsNullOrWhiteSpace(profile.EnemyId) ? "*" : profile.EnemyId.Trim();
            var variantId = string.IsNullOrWhiteSpace(profile.VariantId) ? "*" : profile.VariantId.Trim();
            profiles.RemoveAll(existing => Same(existing.EnemyId, enemyId) && Same(existing.VariantId, variantId));
            profiles.Add(new SpiritIntentProfile
            {
                EnemyId = enemyId,
                VariantId = variantId,
                SourceEnemyCardIds = Clean(profile.SourceEnemyCardIds),
                AttackTendency = Known(profile.AttackTendency, intents),
                DefenseTendency = Known(profile.DefenseTendency, intents),
                AttackWeight = Math.Max(1, profile.AttackWeight),
                DefenseWeight = Math.Max(1, profile.DefenseWeight),
                HpMultiplier = ClampMultiplier(profile.HpMultiplier),
                MagicMultiplier = ClampMultiplier(profile.MagicMultiplier),
                AttackMultiplier = ClampMultiplier(profile.AttackMultiplier),
                ArmorMultiplier = ClampMultiplier(profile.ArmorMultiplier)
            });
        }

        if (!profiles.Any(profile => profile.EnemyId == "*" && profile.VariantId == "*"))
        {
            profiles.Add(DefaultProfile());
        }

        return new SpiritIntentRegistryDocument
        {
            SchemaVersion = 1,
            Intents = intents.Values.OrderBy(intent => intent.Id, StringComparer.Ordinal).ToList(),
            Profiles = profiles.OrderBy(profile => profile.EnemyId, StringComparer.Ordinal).ThenBy(profile => profile.VariantId, StringComparer.Ordinal).ToList()
        };
    }

    private static List<string> Known(IEnumerable<string>? ids, IReadOnlyDictionary<string, CompanionIntentDefinition> custom)
    {
        return Clean(ids).Where(id => custom.ContainsKey(id) || CompanionIntentRegistry.Find(id) != null).ToList();
    }

    private static List<string> Clean(IEnumerable<string>? ids)
    {
        return (ids ?? Array.Empty<string>()).Select(id => (id ?? "").Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static float ClampMultiplier(float value) => Math.Max(0.25f, Math.Min(2.5f, value <= 0f ? 1f : value));

    private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);

    private static void ParseProfileKey(string profileKey, out string enemyId, out string variantId)
    {
        var value = (profileKey ?? "").Trim();
        if (value.StartsWith("spirit:", StringComparison.Ordinal))
        {
            value = value.Substring("spirit:".Length);
        }

        var separator = value.IndexOf('#');
        enemyId = separator < 0 ? value : value.Substring(0, separator);
        variantId = separator < 0 ? enemyId : value.Substring(separator + 1);
    }

    private static SpiritIntentRegistryDocument BuiltInDocument()
    {
        return new SpiritIntentRegistryDocument { SchemaVersion = 1, Profiles = new List<SpiritIntentProfile> { DefaultProfile() } };
    }

    private static SpiritIntentProfile DefaultProfile()
    {
        return new SpiritIntentProfile
        {
            EnemyId = "*",
            VariantId = "*",
            AttackTendency = new List<string>(),
            DefenseTendency = new List<string>(),
            AttackWeight = 60,
            DefenseWeight = 40,
            HpMultiplier = 1f,
            MagicMultiplier = 1f,
            AttackMultiplier = 1f,
            ArmorMultiplier = 1f
        };
    }
}

public static class CompanionIntentResolver
{
    public static CompanionIntentDefinition? Find(CompanionBattleState? state, string intentId)
    {
        return IsSpirit(state)
            ? SpiritIntentRegistry.Find(intentId)
            : CompanionIntentRegistry.Find(intentId);
    }

    public static IReadOnlyList<CompanionIntentDefinition> IntentsFor(CompanionBattleState state, CompanionIntentTendency tendency)
    {
        return IsSpirit(state)
            ? SpiritIntentRegistry.IntentsFor(state.RoleId, tendency)
            : CompanionIntentRegistry.IntentsForRole(state.RoleId, tendency);
    }

    public static (int Attack, int Defense) TendencyWeightsFor(CompanionBattleState state)
    {
        return IsSpirit(state)
            ? SpiritIntentRegistry.TendencyWeightsFor(state.RoleId)
            : CompanionIntentRegistry.TendencyWeightsForRole(state.RoleId);
    }

    public static CompanionIntentType IntentType(CompanionBattleState? state, CompanionIntentDefinition? intent)
    {
        return CompanionIntentRegistry.IntentType(intent);
    }

    private static bool IsSpirit(CompanionBattleState? state)
    {
        return string.Equals(state?.EntityKind, "SpiritAttachment", StringComparison.Ordinal);
    }
}
