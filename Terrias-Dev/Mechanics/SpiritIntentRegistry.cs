using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class SpiritIntentRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly string[] ProfileListFields =
    {
        "sourceEnemyCardIds",
        "pveAttackTendency",
        "pveDefenseTendency",
        "pvpAttackTendency",
        "pvpDefenseTendency",
        "fallbackAttackTendency",
        "fallbackDefenseTendency",
        "pvpSourceEnemyCardIds",
        "fallbackSourceEnemyCardIds"
    };
    private static SpiritIntentRegistryDocument document = BuiltInDocument();
    private static Dictionary<string, CompanionIntentDefinition> intentById = new(StringComparer.Ordinal);
    private static Dictionary<string, SpiritIntentProfile> profileById = new(StringComparer.Ordinal);
    private static string registryHash = "00000000";

    static SpiritIntentRegistry()
    {
        SetDocument(document);
    }

    public static string RegistryHash
    {
        get
        {
            lock (SyncRoot)
            {
                return registryHash;
            }
        }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.SpiritIntentRegistryFile);
            if (!File.Exists(path))
            {
                SetDocument(BuiltInDocument());
                TerriasLog.Warn("[SpiritIntentRegistry] missing registry; using projection common pool.");
                return;
            }

            try
            {
                var readResult = ReadDocument(File.ReadAllText(path));
                var loaded = readResult.Document;
                foreach (var diagnostic in readResult.Diagnostics)
                {
                    TerriasLog.Warn(diagnostic);
                }

                if (loaded.SchemaVersion != SpiritSystemContract.IntentRegistrySchemaVersion)
                {
                    throw new InvalidDataException("unsupported schemaVersion=" + loaded.SchemaVersion
                                                   + "; expected " + SpiritSystemContract.IntentRegistrySchemaVersion);
                }

                SetDocument(Normalize(loaded));
                TerriasLog.Info(
                    "[SpiritIntentRegistry] registryState=ready profiles=" + document.Profiles.Count
                    + ", intents=" + document.Intents.Count
                    + ", normalizedListFields=" + readResult.NormalizedListFields
                    + ", rejectedProfiles=" + readResult.RejectedProfiles
                    + ", rejectedIntents=" + readResult.RejectedIntents
                    + ", path=" + path);
            }
            catch (Exception ex)
            {
                SetDocument(BuiltInDocument());
                TerriasLog.Warn(
                    "[SpiritIntentRegistry] registryState=fallback-only; failed to load registry; "
                    + "using projection common pool: " + ex.Message);
            }
        }
    }

    public static SpiritIntentProfile ProfileFor(string profileKey)
    {
        return ResolveProfile(profileKey).Profile;
    }

    public static SpiritIntentProfile ProfileForIdentity(string profileId, string fallbackProfileKey)
    {
        return ResolveProfileIdentity(profileId, fallbackProfileKey).Profile;
    }

    public static SpiritProfileResolution<SpiritIntentProfile> ResolveProfileIdentity(string profileId, string fallbackProfileKey)
    {
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(profileId) && profileById.TryGetValue(profileId.Trim(), out var fixedProfile))
            {
                return DirectProfileIdResolution(fixedProfile, profileId);
            }
        }
        return ResolveProfile(fallbackProfileKey);
    }

    public static SpiritProfileResolution<SpiritIntentProfile> ResolveProfile(string profileKey)
    {
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(profileKey) && profileById.TryGetValue(profileKey.Trim(), out var fixedProfile))
            {
                return DirectProfileIdResolution(fixedProfile, profileKey);
            }
        }
        SpiritProfileIdentityResolver.ParseProfileKey(profileKey, out var enemyId, out var variantId);
        lock (SyncRoot)
        {
            return SpiritProfileIdentityResolver.Resolve(
                document.Profiles,
                profile => profile.EnemyId,
                profile => profile.VariantId,
                enemyId,
                variantId);
        }
    }

    public static IReadOnlyList<CompanionIntentDefinition> IntentsFor(string profileKey, CompanionIntentTendency tendency)
    {
        return IntentsFor(profileKey, tendency, SpiritIntentPool.Pve);
    }

    public static IReadOnlyList<CompanionIntentDefinition> IntentsFor(
        string profileKey,
        CompanionIntentTendency tendency,
        SpiritIntentPool pool)
    {
        lock (SyncRoot)
        {
            var resolution = ResolveProfile(profileKey);
            var profile = resolution.Profile;
            if (resolution.UsedGlobalFallback)
            {
                TerriasLog.WarnOnce(
                    "spirit-intent-global:" + resolution.RawEnemyId + "#" + resolution.RawVariantId,
                    "[SpiritProfile] intent registry used global fallback: raw="
                    + SpiritProfileIdentityResolver.CreateProfileKey(resolution.RawEnemyId, resolution.RawVariantId)
                    + ", matched=" + resolution.MatchedProfileKey
                    + ", kind=" + resolution.MatchKind
                    + ", registry=" + RegistryHash);
            }
            var ids = SelectIds(profile, tendency, pool);
            var resolved = ids.Select(FindUnlocked).Where(intent => intent != null).Cast<CompanionIntentDefinition>().ToArray();
            if (resolved.Length == 0 && pool == SpiritIntentPool.Pve)
            {
                ids = SelectIds(profile, tendency, SpiritIntentPool.Fallback);
                resolved = ids.Select(FindUnlocked).Where(intent => intent != null).Cast<CompanionIntentDefinition>().ToArray();
            }

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
        return intentById.TryGetValue(intentId ?? "", out var intent)
            ? intent
            : CompanionIntentRegistry.Find(intentId ?? "");
    }

    private static RegistryReadResult ReadDocument(string json)
    {
        var root = JObject.Parse(json ?? "");
        var result = new RegistryReadResult
        {
            Document = new SpiritIntentRegistryDocument
            {
                SchemaVersion = root.Value<int?>("schemaVersion") ?? 0
            }
        };

        var intentTokens = ReadArray(root, "intents");
        for (var index = 0; index < intentTokens.Count; index++)
        {
            var token = intentTokens[index];
            if (token is not JObject)
            {
                result.RejectedIntents++;
                result.Diagnostics.Add("[SpiritIntentRegistry] rejected intent index=" + index + ": expected object.");
                continue;
            }

            try
            {
                var intent = AuraSharedJson.Deserialize<CompanionIntentDefinition>(token.ToString(Formatting.None));
                if (intent == null)
                {
                    throw new InvalidDataException("deserialized value is null");
                }

                result.Document.Intents.Add(intent);
            }
            catch (Exception ex)
            {
                result.RejectedIntents++;
                result.Diagnostics.Add("[SpiritIntentRegistry] rejected intent index=" + index + ": " + ex.Message);
            }
        }

        var profileTokens = ReadArray(root, "profiles");
        for (var index = 0; index < profileTokens.Count; index++)
        {
            if (profileTokens[index] is not JObject sourceProfile)
            {
                result.RejectedProfiles++;
                result.Diagnostics.Add("[SpiritIntentRegistry] rejected profile index=" + index + ": expected object.");
                continue;
            }

            var profile = (JObject)sourceProfile.DeepClone();
            var profileKey = ProfileKeyForLog(profile, index);
            if (!NormalizeProfileListFields(profile, profileKey, result, out var reason))
            {
                result.RejectedProfiles++;
                result.Diagnostics.Add("[SpiritIntentRegistry] rejected profile=" + profileKey + ": " + reason);
                continue;
            }

            try
            {
                var loadedProfile = AuraSharedJson.Deserialize<SpiritIntentProfile>(profile.ToString(Formatting.None));
                if (loadedProfile == null)
                {
                    throw new InvalidDataException("deserialized value is null");
                }

                result.Document.Profiles.Add(loadedProfile);
            }
            catch (Exception ex)
            {
                result.RejectedProfiles++;
                result.Diagnostics.Add("[SpiritIntentRegistry] rejected profile=" + profileKey + ": " + ex.Message);
            }
        }

        return result;
    }

    private static JArray ReadArray(JObject root, string field)
    {
        var token = root[field];
        if (token == null || token.Type == JTokenType.Null)
        {
            return new JArray();
        }

        return token as JArray
            ?? throw new InvalidDataException("top-level field '" + field + "' must be an array");
    }

    private static bool NormalizeProfileListFields(
        JObject profile,
        string profileKey,
        RegistryReadResult result,
        out string reason)
    {
        foreach (var field in ProfileListFields)
        {
            var token = profile[field];
            if (token == null)
            {
                continue;
            }

            if (token.Type == JTokenType.Null)
            {
                profile[field] = new JArray();
                RecordLegacyListNormalization(profileKey, field, "null", result);
                continue;
            }

            if (token.Type == JTokenType.String)
            {
                profile[field] = new JArray(token.Value<string>() ?? "");
                RecordLegacyListNormalization(profileKey, field, "string", result);
                continue;
            }

            if (token is not JArray values)
            {
                reason = "field '" + field + "' must be an array or legacy string; actual=" + token.Type;
                return false;
            }

            if (values.Any(value => value.Type != JTokenType.String && value.Type != JTokenType.Null))
            {
                reason = "field '" + field + "' contains a non-string value";
                return false;
            }
        }

        reason = "";
        return true;
    }

    private static void RecordLegacyListNormalization(
        string profileKey,
        string field,
        string sourceType,
        RegistryReadResult result)
    {
        result.NormalizedListFields++;
        result.Diagnostics.Add(
            "[SpiritIntentRegistry] normalized legacy list field profile=" + profileKey
            + ", field=" + field
            + ", sourceType=" + sourceType);
    }

    private static string ProfileKeyForLog(JObject profile, int index)
    {
        var enemyId = (profile.Value<string>("enemyId") ?? "?").Trim();
        var variantId = (profile.Value<string>("variantId") ?? "*").Trim();
        return enemyId + "#" + variantId + "@" + index;
    }

    private static SpiritIntentRegistryDocument Normalize(SpiritIntentRegistryDocument loaded)
    {
        var intents = new Dictionary<string, CompanionIntentDefinition>(StringComparer.Ordinal);
        foreach (var intent in (loaded.Intents ?? new List<CompanionIntentDefinition>())
                     .Concat(SpiritTrainingRegistry.CommonIntents()))
        {
            var id = (intent.Id ?? "").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            intent.Id = id;
            intent.EnemyCardId = (intent.EnemyCardId ?? TerriasIds.ProjectionActionWaitCardId).Trim();
            intent.Pool = string.IsNullOrWhiteSpace(intent.Pool) ? "Pve" : intent.Pool.Trim();
            intent.AdaptationNote = (intent.AdaptationNote ?? "").Trim();
            intent.DisplayName = (intent.DisplayName ?? "").Trim();
            intent.Description = (intent.Description ?? "").Trim();
            intent.Type = (intent.Type ?? "Attack").Trim();
            intent.HandlerId = (intent.HandlerId ?? "").Trim();
            intent.EligibilityPolicy = (intent.EligibilityPolicy ?? "").Trim();
            intent.Target ??= new CompanionIntentTargetSpec();
            intent.Threat ??= new CompanionIntentThreatSpec();
            intent.HitCount = Math.Max(1, intent.HitCount);
            intent.Cost = Math.Max(0, intent.Cost);
            intent.Cooldown = Math.Max(0, intent.Cooldown);
            intent.BasePriority = Math.Max(1, intent.BasePriority);
            intent.Effects = NormalizeEffects(intent);
            if (intent.Effects.Count > 0)
            {
                ApplyPrimaryEffect(intent, intent.Effects[0]);
            }

            var reason = "";
            var valid = Enum.TryParse(intent.Type, true, out CompanionIntentType _)
                && CompanionTargetPolicyRegistry.ValidateSpec(intent.Target, out reason)
                && CompanionIntentHandlerRegistry.Validate(intent, out reason)
                && intent.Effects.All(effect => ValidateEffect(intent, effect, out reason));
            if (valid)
            {
                intents[id] = intent;
            }
            else
            {
                TerriasLog.Warn("[SpiritIntentRegistry] rejected invalid intent " + id + ": " + reason);
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
                ProfileId = (profile.ProfileId ?? "").Trim(),
                EnemyId = enemyId,
                VariantId = variantId,
                SourceEnemyCardIds = Clean(profile.SourceEnemyCardIds),
                PveAttackTendency = Known(profile.PveAttackTendency, intents),
                PveDefenseTendency = Known(profile.PveDefenseTendency, intents),
                PvpAttackTendency = Known(profile.PvpAttackTendency, intents),
                PvpDefenseTendency = Known(profile.PvpDefenseTendency, intents),
                FallbackAttackTendency = Known(profile.FallbackAttackTendency, intents),
                FallbackDefenseTendency = Known(profile.FallbackDefenseTendency, intents),
                PvpSourceEnemyCardIds = Clean(profile.PvpSourceEnemyCardIds),
                FallbackSourceEnemyCardIds = Clean(profile.FallbackSourceEnemyCardIds),
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
            SchemaVersion = SpiritSystemContract.IntentRegistrySchemaVersion,
            Intents = intents.Values.OrderBy(intent => intent.Id, StringComparer.Ordinal).ToList(),
            Profiles = profiles.OrderBy(profile => profile.EnemyId, StringComparer.Ordinal).ThenBy(profile => profile.VariantId, StringComparer.Ordinal).ToList()
        };
    }

    private static List<string> Known(IEnumerable<string>? ids, IReadOnlyDictionary<string, CompanionIntentDefinition> custom)
    {
        return Clean(ids).Where(id => custom.ContainsKey(id) || CompanionIntentRegistry.Find(id) != null).ToList();
    }

    private static IReadOnlyList<string> SelectIds(
        SpiritIntentProfile profile,
        CompanionIntentTendency tendency,
        SpiritIntentPool pool)
    {
        return pool switch
        {
            SpiritIntentPool.PvpReserved => tendency == CompanionIntentTendency.Attack
                ? profile.PvpAttackTendency
                : profile.PvpDefenseTendency,
            SpiritIntentPool.Fallback => tendency == CompanionIntentTendency.Attack
                ? profile.FallbackAttackTendency
                : profile.FallbackDefenseTendency,
            _ => tendency == CompanionIntentTendency.Attack
                ? profile.PveAttackTendency
                : profile.PveDefenseTendency
        };
    }

    private static List<string> Clean(IEnumerable<string>? ids)
    {
        return (ids ?? Array.Empty<string>()).Select(id => (id ?? "").Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static float ClampMultiplier(float value) => Math.Max(0.25f, Math.Min(2.5f, value <= 0f ? 1f : value));

    private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);

    private static List<CompanionIntentEffectSpec> NormalizeEffects(CompanionIntentDefinition intent)
    {
        var source = intent.Effects != null && intent.Effects.Count > 0
            ? intent.Effects
            : new List<CompanionIntentEffectSpec> { CompanionIntentEffects.FromLegacy(intent) };
        var effects = new List<CompanionIntentEffectSpec>();
        for (var index = 0; index < source.Count; index++)
        {
            var effect = source[index] ?? new CompanionIntentEffectSpec();
            effect.HandlerId = (effect.HandlerId ?? "").Trim();
            effect.Target ??= new CompanionIntentTargetSpec();
            effect.Target.Scope = (effect.Target.Scope ?? "").Trim();
            effect.Target.Mode = string.IsNullOrWhiteSpace(effect.Target.Mode) ? "Single" : effect.Target.Mode.Trim();
            effect.Target.Policy = (effect.Target.Policy ?? "").Trim();
            effect.HitCount = Math.Max(1, effect.HitCount);
            effect.BuffId = (effect.BuffId ?? "").Trim();
            effect.BuffStacks = Math.Max(0, effect.BuffStacks);
            effect.FlatValue = Math.Max(0, effect.FlatValue);
            effect.DisplayIndex = effect.DisplayIndex <= 0 ? index + 1 : effect.DisplayIndex;
            effects.Add(effect);
        }

        return effects;
    }

    private static bool ValidateEffect(
        CompanionIntentDefinition parent,
        CompanionIntentEffectSpec effect,
        out string reason)
    {
        var definition = CompanionIntentEffects.AsDefinition(parent, effect);
        return CompanionTargetPolicyRegistry.ValidateSpec(effect.Target, out reason)
            && CompanionIntentHandlerRegistry.Validate(definition, out reason);
    }

    private static void ApplyPrimaryEffect(CompanionIntentDefinition intent, CompanionIntentEffectSpec effect)
    {
        intent.HandlerId = effect.HandlerId;
        intent.Target = effect.Target;
        intent.HitCount = effect.HitCount;
        intent.BuffId = effect.BuffId;
        intent.BuffStacks = effect.BuffStacks;
        intent.FlatValue = effect.FlatValue;
        intent.AttackScale = effect.AttackScale;
        intent.ArmorScale = effect.ArmorScale;
        intent.MagicScale = effect.MagicScale;
        intent.SpeedScale = effect.SpeedScale;
    }

    private static void SetDocument(SpiritIntentRegistryDocument next)
    {
        document = next ?? BuiltInDocument();
        intentById = (document.Intents ?? new List<CompanionIntentDefinition>())
            .Where(intent => !string.IsNullOrWhiteSpace(intent.Id))
            .ToDictionary(intent => intent.Id, intent => intent, StringComparer.Ordinal);
        profileById = (document.Profiles ?? new List<SpiritIntentProfile>())
            .Where(profile => !string.IsNullOrWhiteSpace(profile.ProfileId))
            .GroupBy(profile => profile.ProfileId.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in AuraSharedJson.Serialize(document))
            {
                hash = (hash ^ character) * 16777619;
            }

            registryHash = hash.ToString("x8");
        }
    }

    private static SpiritIntentRegistryDocument BuiltInDocument()
    {
        return new SpiritIntentRegistryDocument
        {
            SchemaVersion = SpiritSystemContract.IntentRegistrySchemaVersion,
            Intents = SpiritTrainingRegistry.CommonIntents().ToList(),
            Profiles = new List<SpiritIntentProfile> { DefaultProfile() }
        };
    }

    private static SpiritIntentProfile DefaultProfile()
    {
        return new SpiritIntentProfile
        {
            ProfileId = "",
            EnemyId = "*",
            VariantId = "*",
            PveAttackTendency = new List<string>(),
            PveDefenseTendency = new List<string>(),
            FallbackAttackTendency = new List<string> { "staff_tap" },
            FallbackDefenseTendency = new List<string> { "shield_blessing" },
            AttackWeight = 60,
            DefenseWeight = 40,
            HpMultiplier = 1f,
            MagicMultiplier = 1f,
            AttackMultiplier = 1f,
            ArmorMultiplier = 1f
        };
    }

    private static SpiritProfileResolution<SpiritIntentProfile> DirectProfileIdResolution(SpiritIntentProfile profile, string profileId)
    {
        return new SpiritProfileResolution<SpiritIntentProfile>(
            profile,
            profileId ?? "",
            "",
            profile.EnemyId,
            profile.VariantId,
            "profile-id",
            false,
            false,
            false);
    }

    private sealed class RegistryReadResult
    {
        public SpiritIntentRegistryDocument Document { get; set; } = new();

        public int NormalizedListFields { get; set; }

        public int RejectedProfiles { get; set; }

        public int RejectedIntents { get; set; }

        public List<string> Diagnostics { get; } = new();
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
        if (!IsSpirit(state)) return CompanionIntentRegistry.IntentsForRole(state.RoleId, tendency);
        var equipped = state.EquippedIntentIds
            .Select(SpiritIntentRegistry.Find)
            .Where(intent => intent != null && MatchesTendency(intent, tendency))
            .Cast<CompanionIntentDefinition>()
            .ToArray();
        if (equipped.Length > 0) return equipped;

        try
        {
            TerriasLog.WarnOnce(
                "spirit-emergency-loadout:" + state.StatusId + ":" + tendency,
                "[SpiritTraining] effective loadout was empty; using the bounded compatibility fallback for status="
                + state.StatusId + ", tendency=" + tendency + ".");
        }
        catch
        {
            // Pure behavior hosts do not provide Unity's native logging ECall.
        }
        return SpiritTrainingService.EmergencyFallbackIntentIds
            .Select(SpiritIntentRegistry.Find)
            .Where(intent => intent != null && MatchesTendency(intent, tendency))
            .Cast<CompanionIntentDefinition>()
            .ToArray();
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

    private static bool MatchesTendency(CompanionIntentDefinition intent, CompanionIntentTendency tendency)
    {
        var type = CompanionIntentRegistry.IntentType(intent);
        return tendency == CompanionIntentTendency.Attack
            ? type is CompanionIntentType.Attack or CompanionIntentType.Interference
            : type is CompanionIntentType.Defense or CompanionIntentType.Recovery or CompanionIntentType.Support;
    }
}
