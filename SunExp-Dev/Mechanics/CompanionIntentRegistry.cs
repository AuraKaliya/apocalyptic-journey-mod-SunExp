using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Mechanics;

public static class CompanionIntentRegistry
{
    private const string RegistryFileName = "companion.intent.registry.json";

    private static readonly object SyncRoot = new();
    private static CompanionIntentRegistryDocument document = BuiltInDocument();

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var fallback = BuiltInDocument();
            var path = Path.Combine(modConfig.DirectoryName, RegistryFileName);
            if (!File.Exists(path))
            {
                document = Normalize(fallback, fallback);
                SunExpLog.Warn("[CompanionIntentRegistry] missing registry; using built-in intents.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<CompanionIntentRegistryDocument>(File.ReadAllText(path))
                    ?? new CompanionIntentRegistryDocument();
                document = Normalize(loaded, fallback);
                SunExpLog.Info("[CompanionIntentRegistry] loaded companion intents from " + path);
            }
            catch (Exception ex)
            {
                document = Normalize(fallback, fallback);
                SunExpLog.Warn("[CompanionIntentRegistry] failed to load registry; using built-in intents: " + ex.Message);
            }
        }
    }

    public static CompanionIntentDefinition? Find(string intentId)
    {
        lock (SyncRoot)
        {
            return document.Intents.FirstOrDefault(intent => SameId(intent.Id, intentId));
        }
    }

    public static IReadOnlyList<CompanionIntentDefinition> IntentsForRole(string roleId, CompanionIntentTendency tendency)
    {
        lock (SyncRoot)
        {
            var profile = ProfileFor(roleId);
            var ids = tendency == CompanionIntentTendency.Attack
                ? profile.AttackTendency
                : profile.DefenseTendency;
            return ids
                .Select(FindUnlocked)
                .Where(intent => intent != null)
                .Cast<CompanionIntentDefinition>()
                .ToArray();
        }
    }

    public static CompanionIntentType IntentType(CompanionIntentDefinition intent)
    {
        return Enum.TryParse(intent?.Type ?? "", ignoreCase: true, out CompanionIntentType type)
            ? type
            : CompanionIntentType.Attack;
    }

    private static CompanionIntentProfile ProfileFor(string roleId)
    {
        var id = roleId ?? "";
        return document.Profiles.FirstOrDefault(profile => SameId(profile.RoleId, id))
            ?? document.Profiles.FirstOrDefault(profile => profile.RoleId == "*")
            ?? BuiltInDocument().Profiles[0];
    }

    private static CompanionIntentDefinition? FindUnlocked(string intentId)
    {
        return document.Intents.FirstOrDefault(intent => SameId(intent.Id, intentId));
    }

    private static CompanionIntentRegistryDocument Normalize(
        CompanionIntentRegistryDocument loaded,
        CompanionIntentRegistryDocument fallback)
    {
        var result = new CompanionIntentRegistryDocument();
        var intents = new Dictionary<string, CompanionIntentDefinition>(StringComparer.Ordinal);
        foreach (var intent in fallback.Intents.Concat(loaded.Intents ?? new List<CompanionIntentDefinition>()))
        {
            var id = (intent.Id ?? "").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            intent.Id = id;
            intent.EnemyCardId = string.IsNullOrWhiteSpace(intent.EnemyCardId) ? id : intent.EnemyCardId.Trim();
            intent.Type = string.IsNullOrWhiteSpace(intent.Type) ? "Attack" : intent.Type.Trim();
            intent.Cost = Math.Max(0, intent.Cost);
            intent.Cooldown = Math.Max(0, intent.Cooldown);
            intent.BasePriority = Math.Max(1, intent.BasePriority);
            intent.Threat ??= new CompanionIntentThreatSpec();
            intent.Threat.Preview = Math.Max(0, intent.Threat.Preview);
            intent.Threat.OnUse = Math.Max(0, intent.Threat.OnUse);
            intent.Threat.Decay = Math.Max(1, intent.Threat.Decay);
            intents[id] = intent;
        }

        result.Intents = intents.Values.ToList();
        var profiles = new List<CompanionIntentProfile>();
        foreach (var profile in fallback.Profiles.Concat(loaded.Profiles ?? new List<CompanionIntentProfile>()))
        {
            var roleId = string.IsNullOrWhiteSpace(profile.RoleId) ? "*" : profile.RoleId.Trim();
            profiles.RemoveAll(existing => SameId(existing.RoleId, roleId));
            profiles.Add(new CompanionIntentProfile
            {
                RoleId = roleId,
                AttackTendency = FilterKnown(profile.AttackTendency, intents),
                DefenseTendency = FilterKnown(profile.DefenseTendency, intents)
            });
        }

        result.Profiles = profiles.Count == 0 ? fallback.Profiles : profiles;
        return result;
    }

    private static List<string> FilterKnown(IEnumerable<string>? ids, Dictionary<string, CompanionIntentDefinition> intents)
    {
        var result = new List<string>();
        foreach (var id in ids ?? Array.Empty<string>())
        {
            var clean = (id ?? "").Trim();
            if (clean.Length > 0 && intents.ContainsKey(clean) && !result.Contains(clean))
            {
                result.Add(clean);
            }
        }

        return result;
    }

    private static CompanionIntentRegistryDocument BuiltInDocument()
    {
        return new CompanionIntentRegistryDocument
        {
            Intents = new List<CompanionIntentDefinition>
            {
                new()
                {
                    Id = SunExpIds.ProjectionActionStaffTap,
                    EnemyCardId = SunExpIds.ProjectionActionStaffTapCardId,
                    Type = nameof(CompanionIntentType.Attack),
                    Cost = 1,
                    Cooldown = 0,
                    BasePriority = 20,
                    Effect = "Damage",
                    FlatValue = 2,
                    AttackScale = 1.0f,
                    MagicScale = 0.3f,
                    PriorityBonus = "execute_low_hp",
                    Threat = new CompanionIntentThreatSpec
                    {
                        Preview = 8,
                        OnUse = 12,
                        Decay = 4,
                        TargetBias = "self"
                    }
                },
                new()
                {
                    Id = SunExpIds.ProjectionActionShieldBlessing,
                    EnemyCardId = SunExpIds.ProjectionActionShieldBlessingCardId,
                    Type = nameof(CompanionIntentType.Defense),
                    Cost = 1,
                    Cooldown = 0,
                    BasePriority = 16,
                    Effect = "Block",
                    FlatValue = 2,
                    ArmorScale = 1.0f,
                    MagicScale = 0.35f,
                    PriorityBonus = "low_hp_or_no_block",
                    Threat = new CompanionIntentThreatSpec
                    {
                        Preview = 10,
                        OnUse = 16,
                        Decay = 4,
                        TargetBias = "self"
                    }
                }
            },
            Profiles = new List<CompanionIntentProfile>
            {
                new()
                {
                    RoleId = "*",
                    AttackTendency = new List<string> { SunExpIds.ProjectionActionStaffTap },
                    DefenseTendency = new List<string> { SunExpIds.ProjectionActionShieldBlessing }
                }
            }
        };
    }

    private static bool SameId(string? left, string? right)
    {
        return string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    }
}
