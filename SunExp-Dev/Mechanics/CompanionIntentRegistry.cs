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

    public static string RegistryHash
    {
        get
        {
            lock (SyncRoot)
            {
                unchecked
                {
                    uint hash = 2166136261;
                    var canonical = JsonConvert.SerializeObject(document, Formatting.None);
                    foreach (var character in canonical)
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
                if (loaded.SchemaVersion != 3)
                {
                    throw new InvalidDataException("unsupported schemaVersion=" + loaded.SchemaVersion + "; expected 3");
                }
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

    public static CompanionIntentType IntentType(CompanionIntentDefinition? intent)
    {
        return Enum.TryParse(intent?.Type ?? "", ignoreCase: true, out CompanionIntentType type)
            ? type
            : CompanionIntentType.Attack;
    }

    public static (int Attack, int Defense) TendencyWeightsForRole(string roleId)
    {
        lock (SyncRoot)
        {
            var profile = ProfileFor(roleId);
            return (Math.Max(1, profile.AttackWeight), Math.Max(1, profile.DefenseWeight));
        }
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
        var result = new CompanionIntentRegistryDocument { SchemaVersion = 3 };
        var intents = new Dictionary<string, CompanionIntentDefinition>(StringComparer.Ordinal);
        foreach (var intent in fallback.Intents.Concat(loaded.Intents ?? new List<CompanionIntentDefinition>()))
        {
            var id = (intent.Id ?? "").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            intent.Id = id;
            intent.EnemyCardId = (intent.EnemyCardId ?? id).Trim();
            intent.Type = (intent.Type ?? "Attack").Trim();
            intent.HandlerId = (intent.HandlerId ?? "").Trim();
            intent.Target ??= new CompanionIntentTargetSpec();
            intent.Target.Scope = (intent.Target.Scope ?? "").Trim();
            intent.Target.Mode = string.IsNullOrWhiteSpace(intent.Target.Mode) ? "Single" : intent.Target.Mode.Trim();
            intent.Target.Policy = (intent.Target.Policy ?? "").Trim();
            intent.HitCount = Math.Max(1, intent.HitCount);
            intent.BuffId = (intent.BuffId ?? "").Trim();
            intent.BuffStacks = Math.Max(0, intent.BuffStacks);
            intent.Cost = Math.Max(0, intent.Cost);
            intent.Cooldown = Math.Max(0, intent.Cooldown);
            intent.BasePriority = Math.Max(1, intent.BasePriority);
            var threat = intent.Threat ?? new CompanionIntentThreatSpec();
            intent.Threat = threat;
            threat.Preview = Math.Max(0, threat.Preview);
            threat.OnUse = Math.Max(0, threat.OnUse);
            threat.Decay = Math.Max(1, threat.Decay);
            var validHandler = CompanionIntentHandlerRegistry.Validate(intent, out var reason);
            if (!Enum.TryParse(intent.Type, true, out CompanionIntentType _)
                || string.IsNullOrWhiteSpace(intent.EnemyCardId)
                || !CompanionTargetPolicyRegistry.ValidateSpec(intent.Target, out _)
                || !validHandler)
            {
                SunExpLog.Warn("[CompanionIntentRegistry] rejected invalid intent " + id + ": " + reason);
                continue;
            }
            intents[id] = intent;
        }

        result.Intents = intents.Values.OrderBy(intent => intent.Id, StringComparer.Ordinal).ToList();
        var profiles = new List<CompanionIntentProfile>();
        foreach (var profile in fallback.Profiles.Concat(loaded.Profiles ?? new List<CompanionIntentProfile>()))
        {
            var roleId = string.IsNullOrWhiteSpace(profile.RoleId) ? "*" : profile.RoleId.Trim();
            profiles.RemoveAll(existing => SameId(existing.RoleId, roleId));
            profiles.Add(new CompanionIntentProfile
            {
                RoleId = roleId,
                AttackTendency = FilterKnown(profile.AttackTendency, intents),
                DefenseTendency = FilterKnown(profile.DefenseTendency, intents),
                AttackWeight = Math.Max(1, profile.AttackWeight),
                DefenseWeight = Math.Max(1, profile.DefenseWeight)
            });
        }

        result.Profiles = (profiles.Count == 0 ? fallback.Profiles : profiles)
            .OrderBy(profile => profile.RoleId, StringComparer.Ordinal)
            .ToList();
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
            SchemaVersion = 3,
            Intents = new List<CompanionIntentDefinition>
            {
                new()
                {
                    Id = SunExpIds.ProjectionActionStaffTap,
                    EnemyCardId = SunExpIds.ProjectionActionStaffTapCardId,
                    Type = nameof(CompanionIntentType.Attack),
                    HandlerId = CompanionIntentHandlerRegistry.DamageSingle,
                    Target = Target("Enemy", "Single", CompanionTargetPolicyRegistry.EnemyLowestHp),
                    HitCount = 1,
                    Cost = 1,
                    Cooldown = 0,
                    BasePriority = 28,
                    FlatValue = 2,
                    AttackScale = 1.0f,
                    MagicScale = 0.3f,
                    PriorityBonus = "execute_low_hp",
                    Threat = new CompanionIntentThreatSpec
                    {
                        Preview = 8,
                        OnUse = 12,
                        Decay = 4
                    }
                },
                new()
                {
                    Id = SunExpIds.ProjectionActionShieldBlessing,
                    EnemyCardId = SunExpIds.ProjectionActionShieldBlessingCardId,
                    Type = nameof(CompanionIntentType.Defense),
                    HandlerId = CompanionIntentHandlerRegistry.BlockSingle,
                    Target = Target("Friendly", "Single", CompanionTargetPolicyRegistry.FriendlyOwnerOrSelfDefense),
                    Cost = 1,
                    Cooldown = 0,
                    BasePriority = 16,
                    FlatValue = 2,
                    ArmorScale = 1.0f,
                    MagicScale = 0.35f,
                    PriorityBonus = "low_hp_or_no_block",
                    Threat = new CompanionIntentThreatSpec
                    {
                        Preview = 10,
                        OnUse = 16,
                        Decay = 4
                    }
                },
                new()
                {
                    Id = SunExpIds.ProjectionActionStaffCombo,
                    EnemyCardId = SunExpIds.ProjectionActionStaffComboCardId,
                    Type = nameof(CompanionIntentType.Attack),
                    HandlerId = CompanionIntentHandlerRegistry.DamageMulti,
                    Target = Target("Enemy", "Single", CompanionTargetPolicyRegistry.EnemyLowestHp),
                    HitCount = 3,
                    Cost = 2,
                    Cooldown = 1,
                    BasePriority = 26,
                    FlatValue = 1,
                    AttackScale = 0.38f,
                    MagicScale = 0.10f,
                    PriorityBonus = "execute_low_hp",
                    Threat = new CompanionIntentThreatSpec { Preview = 14, OnUse = 24, Decay = 5 }
                },
                new()
                {
                    Id = SunExpIds.ProjectionActionMagicInterference,
                    EnemyCardId = SunExpIds.ProjectionActionMagicInterferenceCardId,
                    Type = nameof(CompanionIntentType.Interference),
                    HandlerId = CompanionIntentHandlerRegistry.ApplyBuff,
                    Target = Target("Enemy", "Single", CompanionTargetPolicyRegistry.EnemyLowestBuffThenHp),
                    BuffId = "buff_vulnerability",
                    BuffStacks = 2,
                    Cost = 1,
                    Cooldown = 1,
                    BasePriority = 18,
                    Threat = new CompanionIntentThreatSpec { Preview = 8, OnUse = 14, Decay = 4 }
                },
                new()
                {
                    Id = SunExpIds.ProjectionActionYouAreEnhanced,
                    EnemyCardId = SunExpIds.ProjectionActionYouAreEnhancedCardId,
                    Type = nameof(CompanionIntentType.Support),
                    HandlerId = CompanionIntentHandlerRegistry.ApplyBuff,
                    Target = Target("Friendly", "All", CompanionTargetPolicyRegistry.FriendlyAll),
                    BuffId = SunExpIds.Extraordinary,
                    BuffStacks = 50,
                    Cost = 2,
                    Cooldown = 2,
                    BasePriority = 14,
                    Threat = new CompanionIntentThreatSpec { Preview = 12, OnUse = 20, Decay = 5 }
                },
                new()
                {
                    Id = SunExpIds.ProjectionActionCharge,
                    EnemyCardId = SunExpIds.ProjectionActionChargeCardId,
                    Type = nameof(CompanionIntentType.Support),
                    HandlerId = CompanionIntentHandlerRegistry.ApplyBuff,
                    Target = Target("Self", "Single", CompanionTargetPolicyRegistry.Self),
                    BuffId = SunExpIds.Extraordinary,
                    BuffStacks = 50,
                    Cost = 1,
                    Cooldown = 1,
                    BasePriority = 12,
                    Threat = new CompanionIntentThreatSpec { Preview = 6, OnUse = 10, Decay = 4 }
                },
                new()
                {
                    Id = SunExpIds.ProjectionActionHolyHeal,
                    EnemyCardId = SunExpIds.ProjectionActionHolyHealCardId,
                    Type = nameof(CompanionIntentType.Recovery),
                    HandlerId = CompanionIntentHandlerRegistry.HealSingle,
                    Target = Target("Friendly", "Single", CompanionTargetPolicyRegistry.FriendlyMostWounded),
                    Cost = 2,
                    Cooldown = 1,
                    BasePriority = 10,
                    FlatValue = 4,
                    AttackScale = 0.45f,
                    MagicScale = 0.35f,
                    Threat = new CompanionIntentThreatSpec { Preview = 12, OnUse = 20, Decay = 5 }
                    }
            },
            Profiles = new List<CompanionIntentProfile>
            {
                new()
                {
                    RoleId = "*",
                    AttackTendency = new List<string>
                    {
                        SunExpIds.ProjectionActionStaffTap,
                        SunExpIds.ProjectionActionStaffCombo,
                        SunExpIds.ProjectionActionMagicInterference
                    },
                    DefenseTendency = new List<string>
                    {
                        SunExpIds.ProjectionActionShieldBlessing,
                        SunExpIds.ProjectionActionYouAreEnhanced,
                        SunExpIds.ProjectionActionCharge,
                        SunExpIds.ProjectionActionHolyHeal
                    },
                    AttackWeight = 60,
                    DefenseWeight = 40
                }
            }
        };
    }

    private static CompanionIntentTargetSpec Target(string scope, string mode, string policy)
    {
        return new CompanionIntentTargetSpec
        {
            Scope = scope,
            Mode = mode,
            Policy = policy
        };
    }

    private static bool SameId(string? left, string? right)
    {
        return string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    }

}
