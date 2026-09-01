using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritTrainingService
{
    public const int TrainingPlanVersion = SpiritSystemContract.TrainingPlanVersion;
    public const int MinimumSpeed = 80;
    public const int MaximumSpeed = 120;
    public const int EquippedIntentCapacity = 3;

    public static IReadOnlyList<string> EmergencyFallbackIntentIds { get; } = new[]
    {
        "staff_tap",
        "shield_blessing"
    };

    public static void InitializeCaptured(SpiritInstance instance)
    {
        Normalize(instance, legacy: false);
    }

    public static void Normalize(SpiritInstance instance, bool legacy)
    {
        if (instance == null) return;
        instance.LearnedIntentIds ??= new List<string>();
        instance.EquippedIntentIds ??= new List<string>();
        instance.LearnedPassiveIds ??= new List<string>();
        instance.UnlockPlan ??= new List<SpiritUnlockNode>();
        instance.NewAbilityIds ??= new List<string>();
        instance.ResolvedInherentIntentIds ??= new List<string>();

        if (instance.Speed < MinimumSpeed || instance.Speed > MaximumSpeed)
        {
            instance.Speed = ExpectedSpeed(instance.SpiritUid, instance.ProfileId);
        }

        var profile = SpiritTrainingRegistry.ProfileFor(instance.SpeciesId, instance.ProfileId);
        ResolveInherentAbilityPlan(instance, profile);
        var native = new List<string>(instance.ResolvedInherentIntentIds);
        instance.LearnedIntentIds = Clean(instance.LearnedIntentIds.Concat(native));
        instance.LearnedPassiveIds = Clean(instance.LearnedPassiveIds)
            .Where(id => SpiritTrainingRegistry.FindPassive(id) != null)
            .ToList();
        var speciesPassive = instance.ResolvedInherentPassiveId;
        if (!string.IsNullOrWhiteSpace(speciesPassive) && !instance.LearnedPassiveIds.Contains(speciesPassive, StringComparer.Ordinal))
        {
            instance.LearnedPassiveIds.Add(speciesPassive);
        }

        var migratedTrainingPlan = instance.TrainingPlanVersion < TrainingPlanVersion;
        if (migratedTrainingPlan || instance.UnlockPlan.Count == 0)
        {
            if (migratedTrainingPlan)
            {
                var retiredGrowthAbilities = new HashSet<string>(
                    instance.UnlockPlan.Select(node => node.AbilityId).Where(id => !string.IsNullOrWhiteSpace(id)),
                    StringComparer.Ordinal);
                instance.LearnedIntentIds.RemoveAll(retiredGrowthAbilities.Contains);
                instance.LearnedPassiveIds.RemoveAll(retiredGrowthAbilities.Contains);
                instance.NewAbilityIds.RemoveAll(retiredGrowthAbilities.Contains);
            }
            instance.UnlockPlan = GeneratePlan(instance);
            instance.TrainingPlanVersion = TrainingPlanVersion;
        }
        ApplyUnlockedNodes(instance, addNewMarker: legacy || migratedTrainingPlan);

        var defaultIntents = Clean(profile.DefaultIntentIds).Where(instance.LearnedIntentIds.Contains).ToList();
        if (defaultIntents.Count == 0) defaultIntents = NativeDefaults(native);
        instance.EquippedIntentIds = Clean(instance.EquippedIntentIds)
            .Where(instance.LearnedIntentIds.Contains)
            .Take(EquippedIntentCapacity)
            .ToList();
        foreach (var intentId in defaultIntents)
        {
            if (instance.EquippedIntentIds.Count >= EquippedIntentCapacity) break;
            if (!instance.EquippedIntentIds.Contains(intentId, StringComparer.Ordinal)) instance.EquippedIntentIds.Add(intentId);
        }

        instance.EquippedPassiveId = instance.LearnedPassiveIds.Contains(instance.EquippedPassiveId ?? "", StringComparer.Ordinal)
            ? instance.EquippedPassiveId ?? ""
            : speciesPassive;
        instance.LearnedPassiveIds = Clean(instance.LearnedPassiveIds)
            .Where(id => SpiritTrainingRegistry.FindPassive(id) != null)
            .ToList();
        instance.NewAbilityIds = Clean(instance.NewAbilityIds)
            .Where(id => instance.LearnedIntentIds.Contains(id, StringComparer.Ordinal)
                         || instance.LearnedPassiveIds.Contains(id, StringComparer.Ordinal))
            .ToList();
        instance.LoadoutRevision = Math.Max(1, instance.LoadoutRevision);
        instance.LoadoutHash = LoadoutHash(instance);
    }

    public static IReadOnlyList<string> ApplyUnlockedNodes(SpiritInstance instance, bool addNewMarker = true)
    {
        var unlocked = new List<string>();
        foreach (var node in instance.UnlockPlan.OrderBy(value => value.Stage))
        {
            if (node.Unlocked || node.RequiredLevel > instance.Level) continue;
            node.Unlocked = true;
            var target = string.Equals(node.AbilityKind, "Passive", StringComparison.Ordinal)
                ? instance.LearnedPassiveIds
                : instance.LearnedIntentIds;
            if (!target.Contains(node.AbilityId, StringComparer.Ordinal)) target.Add(node.AbilityId);
            if (addNewMarker && !instance.NewAbilityIds.Contains(node.AbilityId, StringComparer.Ordinal))
            {
                instance.NewAbilityIds.Add(node.AbilityId);
            }
            unlocked.Add(node.AbilityId);
        }
        return unlocked;
    }

    public static bool EquipIntent(SpiritInstance instance, int slotIndex, string intentId)
    {
        if (instance == null || slotIndex < 0 || slotIndex >= EquippedIntentCapacity
            || !instance.LearnedIntentIds.Contains(intentId ?? "", StringComparer.Ordinal)) return false;
        if (slotIndex > instance.EquippedIntentIds.Count) return false;
        if (slotIndex == instance.EquippedIntentIds.Count)
        {
            if (instance.EquippedIntentIds.Contains(intentId ?? "", StringComparer.Ordinal)) return false;
            instance.EquippedIntentIds.Add(intentId ?? "");
            CommitLoadout(instance, intentId ?? "");
            return true;
        }
        var previous = instance.EquippedIntentIds[slotIndex];
        if (string.Equals(previous, intentId, StringComparison.Ordinal)) return false;
        var duplicate = instance.EquippedIntentIds.FindIndex(value => string.Equals(value, intentId, StringComparison.Ordinal));
        if (duplicate >= 0 && duplicate != slotIndex) instance.EquippedIntentIds[duplicate] = previous;
        instance.EquippedIntentIds[slotIndex] = intentId ?? "";
        instance.EquippedIntentIds = instance.EquippedIntentIds.Take(EquippedIntentCapacity).ToList();
        CommitLoadout(instance, intentId ?? "");
        return true;
    }

    public static bool EquipPassive(SpiritInstance instance, string passiveId)
    {
        if (instance == null || !instance.LearnedPassiveIds.Contains(passiveId ?? "", StringComparer.Ordinal)) return false;
        if (string.Equals(instance.EquippedPassiveId, passiveId, StringComparison.Ordinal)) return false;
        instance.EquippedPassiveId = passiveId ?? "";
        CommitLoadout(instance, passiveId ?? "");
        return true;
    }

    public static SpiritTrainingViewSnapshot BuildView(SpiritInstance instance)
    {
        Normalize(instance, legacy: false);
        var view = new SpiritTrainingViewSnapshot
        {
            Speed = instance.Speed,
            LoadoutRevision = instance.LoadoutRevision,
            LoadoutHash = instance.LoadoutHash
        };
        view.EquippedIntents = instance.EquippedIntentIds.Select(id => Ability(instance, id, "Intent")).ToList();
        view.EquippedPassive = string.IsNullOrWhiteSpace(instance.EquippedPassiveId)
            ? null
            : Ability(instance, instance.EquippedPassiveId, "Passive");
        view.LearnedIntents = instance.LearnedIntentIds.Select(id => Ability(instance, id, "Intent"))
            .OrderBy(value => value.Type, StringComparer.Ordinal).ThenBy(value => value.DisplayName, StringComparer.Ordinal).ToList();
        view.LearnedPassives = instance.LearnedPassiveIds.Select(id => Ability(instance, id, "Passive"))
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal).ToList();
        return view;
    }

    public static int ExpectedSpeed(string spiritUid, string profileId)
    {
        return DeterministicRange((spiritUid ?? "") + ":" + (profileId ?? "") + ":speed", MinimumSpeed, MaximumSpeed);
    }

    public static bool ValidateDeploymentSnapshot(SpiritDeploymentSnapshot? snapshot, out string reason)
    {
        if (snapshot == null
            || !string.Equals(snapshot.TrainingRegistryHash, SpiritTrainingRegistry.RegistryHash, StringComparison.Ordinal)
            || snapshot.LoadoutRevision < 1
            || snapshot.SpiritSpeed != ExpectedSpeed(snapshot.SpiritUid, snapshot.ProfileId))
        {
            reason = "精灵养成快照与当前注册表不兼容。";
            return false;
        }

        var equippedIntents = Clean(snapshot.EquippedIntentIds);
        if (equippedIntents.Count != (snapshot.EquippedIntentIds?.Count ?? 0)
            || equippedIntents.Count > EquippedIntentCapacity)
        {
            reason = "精灵意图配置包含重复、空白或超额槽位。";
            return false;
        }

        var reconstructed = new SpiritInstance
        {
            SpiritUid = snapshot.SpiritUid,
            SpeciesId = snapshot.SpeciesId,
            ProfileId = snapshot.ProfileId,
            Snapshot = SpiritModelCloner.CloneSnapshot(snapshot.Source),
            Level = Math.Max(1, snapshot.SpiritLevel),
            Speed = 0
        };
        Normalize(reconstructed, legacy: false);
        if (equippedIntents.Any(id => !reconstructed.LearnedIntentIds.Contains(id, StringComparer.Ordinal))
            || !reconstructed.LearnedPassiveIds.Contains(snapshot.EquippedPassiveId ?? "", StringComparer.Ordinal))
        {
            reason = "精灵配置包含尚未解锁的能力。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(snapshot.LoadoutHash)
            || !string.Equals(snapshot.LoadoutHash, LoadoutHash(snapshot), StringComparison.Ordinal))
        {
            reason = "精灵配置摘要无效。";
            return false;
        }

        reason = "";
        return true;
    }

    public static string LoadoutHash(SpiritInstance instance)
    {
        return ComputeLoadoutHash(
            instance.Speed,
            instance.EquippedIntentIds,
            instance.EquippedPassiveId,
            instance.LoadoutRevision);
    }

    public static string LoadoutHash(SpiritDeploymentSnapshot snapshot)
    {
        return ComputeLoadoutHash(
            snapshot?.SpiritSpeed ?? 100,
            snapshot?.EquippedIntentIds,
            snapshot?.EquippedPassiveId ?? "",
            snapshot?.LoadoutRevision ?? 0);
    }

    private static string ComputeLoadoutHash(int speed, IEnumerable<string>? intents, string passiveId, int revision)
    {
        unchecked
        {
            uint hash = 2166136261;
            var value = SpiritTrainingRegistry.RegistryHash + "|" + speed + "|"
                        + string.Join(",", intents ?? Array.Empty<string>()) + "|"
                        + (passiveId ?? "") + "|" + revision;
            foreach (var character in value) hash = (hash ^ character) * 16777619;
            return hash.ToString("x8");
        }
    }

    private static SpiritAbilityView Ability(SpiritInstance instance, string id, string kind)
    {
        var isNew = instance.NewAbilityIds.Contains(id, StringComparer.Ordinal);
        if (string.Equals(kind, "Passive", StringComparison.Ordinal))
        {
            var passive = SpiritTrainingRegistry.FindPassive(id);
            return new SpiritAbilityView
            {
                Id = id,
                Kind = kind,
                DisplayName = passive?.DisplayName ?? id,
                Description = passive?.Description ?? "",
                Type = passive?.Pool ?? "种族固有",
                IsNew = isNew
            };
        }
        var intent = SpiritIntentRegistry.Find(id);
        return new SpiritAbilityView
        {
            Id = id,
            Kind = kind,
            DisplayName = SpiritTrainingRegistry.IntentDisplayName(id),
            Description = SpiritTrainingRegistry.IntentDescription(id),
            Type = intent?.Type ?? "",
            Cost = intent?.Cost ?? 0,
            Cooldown = intent?.Cooldown ?? 0,
            IsNew = isNew
        };
    }

    private static List<SpiritUnlockNode> GeneratePlan(SpiritInstance instance)
    {
        var seed = instance.SpiritUid + ":" + instance.ProfileId + ":training-v" + TrainingPlanVersion;
        var learned = new HashSet<string>(instance.LearnedIntentIds.Concat(instance.LearnedPassiveIds), StringComparer.Ordinal);
        var result = new List<SpiritUnlockNode>();
        AddNode(result, learned, 1, DeterministicRange(seed + ":level:1", 6, 10), "Intent",
            Pick(seed + ":ability:1", SpiritTrainingRegistry.CommonIntentIds("Common.Basic"), learned));
        AddNode(result, learned, 2, DeterministicRange(seed + ":level:2", 14, 18), "Intent",
            Pick(seed + ":ability:2", SpiritTrainingRegistry.CommonIntentIds("Common.Tactical"), learned));
        AddNode(result, learned, 3, DeterministicRange(seed + ":level:3", 23, 28), "Passive",
            PickEligiblePassive(seed + ":ability:3", SpiritTrainingRegistry.CommonPassiveIds("Common.Core"), learned));
        var advancedCandidates = SpiritTrainingRegistry.CommonIntentIds("Common.Advanced");
        var plannedCommonTypes = result
            .Where(node => string.Equals(node.AbilityKind, "Intent", StringComparison.Ordinal))
            .Select(node => IntentType(node.AbilityId))
            .Where(type => type.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (plannedCommonTypes.Count == 1)
        {
            var differentType = advancedCandidates
                .Where(id => !string.Equals(IntentType(id), plannedCommonTypes[0], StringComparison.Ordinal));
            var diverse = Pick(seed + ":ability:4:diverse", differentType, learned);
            AddNode(result, learned, 4, DeterministicRange(seed + ":level:4", 32, 38), "Intent",
                diverse.Length > 0 ? diverse : Pick(seed + ":ability:4", advancedCandidates, learned));
        }
        else
        {
            AddNode(result, learned, 4, DeterministicRange(seed + ":level:4", 32, 38), "Intent",
                Pick(seed + ":ability:4", advancedCandidates, learned));
        }

        var passiveLast = DeterministicRange(seed + ":kind:5", 0, 1) == 1;
        var lastKind = passiveLast ? "Passive" : "Intent";
        var lastPool = passiveLast
            ? SpiritTrainingRegistry.CommonPassiveIds("Common.Advanced")
            : SpiritTrainingRegistry.CommonIntentIds("Common.Advanced")
                .Concat(SpiritTrainingRegistry.CommonIntentIds("Common.Tactical"))
                .Concat(SpiritTrainingRegistry.CommonIntentIds("Common.Basic"))
                .ToArray();
        var last = passiveLast
            ? PickEligiblePassive(seed + ":ability:5", lastPool, learned)
            : Pick(seed + ":ability:5", lastPool, learned);
        if (last.Length == 0)
        {
            lastKind = passiveLast ? "Intent" : "Passive";
            last = passiveLast
                ? Pick(seed + ":ability:5:fallback", SpiritTrainingRegistry.CommonIntentIds("Common.Advanced"), learned)
                : PickEligiblePassive(seed + ":ability:5:fallback",
                    SpiritTrainingRegistry.CommonPassiveIds("Common.Advanced"), learned);
        }
        AddNode(result, learned, 5, DeterministicRange(seed + ":level:5", 42, 47), lastKind, last);
        return result;
    }

    private static void AddNode(ICollection<SpiritUnlockNode> result, ISet<string> learned, int stage, int level, string kind, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        learned.Add(id);
        result.Add(new SpiritUnlockNode { Stage = stage, RequiredLevel = level, AbilityKind = kind, AbilityId = id });
    }

    private static string Pick(string seed, IEnumerable<string> values, ISet<string> excluded)
    {
        return (values ?? Array.Empty<string>()).Where(value => !excluded.Contains(value))
            .OrderBy(value => SpiritGrowthService.StableHash(seed + ":" + value))
            .ThenBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault() ?? "";
    }

    private static string PickEligiblePassive(string seed, IEnumerable<string> values, ISet<string> learned)
    {
        var eligible = (values ?? Array.Empty<string>())
            .Where(id => PassiveEligible(id, learned))
            .ToArray();
        return Pick(seed, eligible.Length > 0 ? eligible : values ?? Array.Empty<string>(), learned);
    }

    private static bool PassiveEligible(string passiveId, IEnumerable<string> learned)
    {
        var passive = SpiritTrainingRegistry.FindPassive(passiveId);
        if (passive == null) return false;
        var intents = learned.Select(SpiritIntentRegistry.Find).Where(intent => intent != null).Cast<CompanionIntentDefinition>().ToArray();
        return passive.EffectKind switch
        {
            "swift-calculation" => intents.Any(intent => intent.SpeedScale > 0f),
            "guardian-contract" => intents.Any(intent => intent.Type is "Defense" or "Recovery"),
            "combo-resonance" => intents.Any(intent => intent.Type is "Support" or "Recovery")
                                 && intents.Any(intent => intent.Type is "Attack" or "Defense"),
            "exploit-opening" => intents.Any(intent => intent.Type == "Attack"),
            "alternating-tactics" => intents.Select(intent => intent.Type).Distinct(StringComparer.Ordinal).Count() >= 2,
            "efficient-casting" => intents.Any(intent => intent.Cost >= 2),
            _ => true
        };
    }

    private static string IntentType(string intentId)
    {
        return SpiritIntentRegistry.Find(intentId)?.Type ?? "";
    }

    private static IEnumerable<string> NativeIntentIds(SpiritInstance instance)
    {
        var profile = SpiritIntentRegistry.ProfileForIdentity(instance.ProfileId, instance.Snapshot?.ProfileKey ?? "");
        return profile.PveAttackTendency.Concat(profile.PveDefenseTendency).Distinct(StringComparer.Ordinal);
    }

    private static void ResolveInherentAbilityPlan(
        SpiritInstance instance,
        SpiritSpeciesTrainingProfile profile)
    {
        if (instance.InherentAbilityPlanVersion >= SpiritSystemContract.InherentAbilityPlanVersion
            && instance.ResolvedInherentIntentIds.Count > 0
            && SpiritTrainingRegistry.FindPassive(instance.ResolvedInherentPassiveId) != null)
        {
            return;
        }

        var previousIntents = new HashSet<string>(instance.ResolvedInherentIntentIds, StringComparer.Ordinal);
        var previousPassive = instance.ResolvedInherentPassiveId ?? "";
        var native = NativeIntentIds(instance).Where(id => SpiritIntentRegistry.Find(id) != null).ToList();
        var passive = string.IsNullOrWhiteSpace(profile.InitialPassiveId)
            ? SpiritTrainingRegistry.SpeciesPassiveId(instance.SpeciesId)
            : profile.InitialPassiveId.Trim();
        if (native.Count == 0)
        {
            native = CompatibilityIntentIds(instance);
            passive = SpiritSystemContract.CompatibilityPassiveId;
        }
        if (SpiritTrainingRegistry.FindPassive(passive) == null)
        {
            passive = SpiritSystemContract.CompatibilityPassiveId;
        }

        if (instance.InherentAbilityPlanVersion > 0)
        {
            instance.LearnedIntentIds.RemoveAll(previousIntents.Contains);
            if (previousPassive.Length > 0) instance.LearnedPassiveIds.RemoveAll(id => string.Equals(id, previousPassive, StringComparison.Ordinal));
        }
        instance.ResolvedInherentIntentIds = Clean(native);
        instance.ResolvedInherentPassiveId = passive;
        instance.InherentAbilityPlanVersion = SpiritSystemContract.InherentAbilityPlanVersion;
    }

    private static List<string> CompatibilityIntentIds(SpiritInstance instance)
    {
        var result = new List<string>
        {
            SpiritSystemContract.CompatibilityAttackIntentId,
            SpiritSystemContract.CompatibilityDefenseIntentId
        };
        var growth = SpiritGrowthRegistry.Resolve(instance);
        var totals = new[]
        {
            (Key: "magic", Value: growth.BaseOrigins.Magic + growth.GrowthOrigins.Magic),
            (Key: "perception", Value: growth.BaseOrigins.Perception + growth.GrowthOrigins.Perception),
            (Key: "spirit", Value: growth.BaseOrigins.Spirit + growth.GrowthOrigins.Spirit),
            (Key: "luck", Value: growth.BaseOrigins.Luck + growth.GrowthOrigins.Luck)
        };
        var dominant = totals.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal).First().Key;
        var branch = dominant switch
        {
            "magic" => "spirit.common.basic.focused-chant.intent",
            "perception" => "spirit.common.tactical.guardian-ward.intent",
            "spirit" => "spirit.common.basic.emergency-heal.intent",
            _ => "spirit.common.basic.weakening-mark.intent"
        };
        if (SpiritIntentRegistry.Find(branch) != null) result.Add(branch);
        return Clean(result).Take(EquippedIntentCapacity).ToList();
    }

    private static List<string> NativeDefaults(IReadOnlyList<string> native)
    {
        var result = new List<string>();
        foreach (var group in native.Select(id => SpiritIntentRegistry.Find(id)).Where(value => value != null)
                     .GroupBy(value => value!.Type, StringComparer.Ordinal))
        {
            result.Add(group.First()!.Id);
            if (result.Count >= EquippedIntentCapacity) break;
        }
        foreach (var id in native)
        {
            if (result.Count >= EquippedIntentCapacity) break;
            if (!result.Contains(id, StringComparer.Ordinal)) result.Add(id);
        }
        return result;
    }

    private static int DeterministicRange(string seed, int minimum, int maximum)
    {
        var range = (ulong)(maximum - minimum + 1);
        var domain = 1UL << 32;
        var limit = domain - domain % range;
        for (var attempt = 0; ; attempt++)
        {
            var candidate = (ulong)SpiritGrowthService.StableHash(seed + ":" + attempt);
            if (candidate < limit) return minimum + (int)(candidate % range);
        }
    }

    private static List<string> Clean(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>()).Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static void CommitLoadout(SpiritInstance instance, string viewedAbility)
    {
        instance.NewAbilityIds.RemoveAll(value => string.Equals(value, viewedAbility, StringComparison.Ordinal));
        instance.LoadoutRevision = Math.Max(1, instance.LoadoutRevision + 1);
        instance.LoadoutHash = LoadoutHash(instance);
    }
}
