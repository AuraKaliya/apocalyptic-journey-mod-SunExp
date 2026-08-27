using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsSkillCgSettings
{
    public const int CurrentSchemaVersion = 10;

    private AuraToolsEventCgSettings eventCg = new();
    private bool hasExplicitEventCg;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("cardUseCg")]
    public AuraToolsCardUseCgSettings CardUseCg { get; set; } = new();

    [JsonProperty("eventCg", ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public AuraToolsEventCgSettings EventCg
    {
        get => eventCg;
        set
        {
            eventCg = value ?? new AuraToolsEventCgSettings();
            hasExplicitEventCg = true;
        }
    }

    [JsonProperty("lowHealthThreshold")]
    private float? LegacyLowHealthThreshold { get; set; }

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    [JsonProperty("maxQueueLength")]
    public int MaxQueueLength { get; set; } = 8;

    [JsonProperty("maxRequestAgeSeconds")]
    public float MaxRequestAgeSeconds { get; set; } = 6f;

    [JsonProperty("duplicateWindowSeconds")]
    public float DuplicateWindowSeconds { get; set; } = 1.25f;

    [JsonProperty("disableAfterFailures")]
    public bool DisableAfterFailures { get; set; } = true;

    [JsonProperty("maxHookFailures")]
    public int MaxHookFailures { get; set; } = 3;

    [JsonProperty("defaultPresentation")]
    public SkillCgPresentationSettings DefaultPresentation { get; set; } = SkillCgPresentationSettings.CreateDefault();

    [JsonProperty("roleEntries")]
    public Dictionary<string, RoleCgEntryOverrideSettings> RoleEntries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("roleSelections")]
    public Dictionary<string, string> RoleSelections { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("manualRoleEntries")]
    public List<RoleCgManualEntrySettings> ManualRoleEntries { get; set; } = new();

    [JsonProperty("legacyRoleRulesMigrated")]
    public bool LegacyRoleRulesMigrated { get; set; }

    [JsonProperty("roles")]
    public Dictionary<string, SkillCgRoleSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        CardUseCg ??= new AuraToolsCardUseCgSettings();
        CardUseCg.Normalize();
        eventCg ??= new AuraToolsEventCgSettings();
        eventCg.Normalize();
        MaxQueueLength = Math.Max(1, Math.Min(30, MaxQueueLength));
        MaxRequestAgeSeconds = Math.Max(0.5f, Math.Min(30f, MaxRequestAgeSeconds));
        DuplicateWindowSeconds = Math.Max(0.02f, Math.Min(2f, DuplicateWindowSeconds));
        MaxHookFailures = Math.Max(1, Math.Min(20, MaxHookFailures));
        DefaultPresentation = (DefaultPresentation ?? SkillCgPresentationSettings.CreateDefault())
            .Resolve(SkillCgPresentationSettings.CreateDefault());
        RoleEntries ??= new Dictionary<string, RoleCgEntryOverrideSettings>(StringComparer.OrdinalIgnoreCase);
        RoleEntries = RoleEntries
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
            {
                var value = group.Last().Value;
                value.Normalize();
                return value;
            }, StringComparer.OrdinalIgnoreCase);
        RoleSelections ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        RoleSelections = RoleSelections
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key)
                           && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
        ManualRoleEntries ??= new List<RoleCgManualEntrySettings>();
        foreach (var entry in ManualRoleEntries)
        {
            entry?.Normalize();
        }
        ManualRoleEntries = ManualRoleEntries
            .Where(entry => entry != null && entry.IsValid)
            .GroupBy(entry => entry.CgId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        Roles ??= new Dictionary<string, SkillCgRoleSettings>(StringComparer.OrdinalIgnoreCase);
        var normalizedRoles = new Dictionary<string, SkillCgRoleSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Roles)
        {
            var role = pair.Value ?? new SkillCgRoleSettings();
            role.Normalize(pair.Key, DefaultPresentation);
            var normalizedKey = RoleCatalog.NormalizeRoleId(role.RoleId);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                normalizedKey = RoleCatalog.NormalizeRoleId(pair.Key);
            }

            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                continue;
            }

            role.RoleId = normalizedKey;
            if (normalizedRoles.TryGetValue(normalizedKey, out var existing))
            {
                existing.Enabled = existing.Enabled || role.Enabled;
                if (string.IsNullOrWhiteSpace(existing.DisplayName))
                {
                    existing.DisplayName = role.DisplayName;
                }

                existing.Rules.AddRange(role.Rules);
                continue;
            }

            normalizedRoles[normalizedKey] = role;
        }

        Roles = normalizedRoles;
        MigrateLegacyRoleRules();
    }

    public bool ShouldSerializeRoles()
    {
        return !LegacyRoleRulesMigrated && Roles.Count > 0;
    }

    public bool ShouldSerializeLegacyLowHealthThreshold() => false;

    public string GetRoleSelection(string roleId, string channel, string skillId = "")
    {
        var exact = AuraToolsRoleCgContextKeys.Create(roleId, channel, skillId);
        if (RoleSelections.TryGetValue(exact, out var selected))
        {
            return selected;
        }

        if (string.Equals(channel, AuraToolsRoleCgContextKeys.SkillChannel, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skillId, "*", StringComparison.Ordinal))
        {
            var wildcard = AuraToolsRoleCgContextKeys.Create(roleId, channel, "*");
            if (RoleSelections.TryGetValue(wildcard, out selected))
            {
                return selected;
            }
        }

        return "";
    }

    public void SetRoleSelection(string roleId, string channel, string skillId, string qualifiedCgId)
    {
        RoleSelections[AuraToolsRoleCgContextKeys.Create(roleId, channel, skillId)] =
            (qualifiedCgId ?? "").Trim();
    }

    public void ResetRoleSelection(string roleId, string channel, string skillId)
    {
        var exact = AuraToolsRoleCgContextKeys.Create(roleId, channel, skillId);
        if (RoleSelections.Remove(exact))
        {
            return;
        }

        if (string.Equals(channel, AuraToolsRoleCgContextKeys.SkillChannel, StringComparison.OrdinalIgnoreCase))
        {
            RoleSelections.Remove(AuraToolsRoleCgContextKeys.Create(roleId, channel, "*"));
        }
    }

    private void MigrateLegacyRoleRules()
    {
        if (LegacyRoleRulesMigrated)
        {
            Roles.Clear();
            return;
        }

        foreach (var role in Roles.Values.Where(value => value != null))
        {
            var roleId = RoleCatalog.NormalizeRoleId(role.RoleId);
            for (var index = 0; index < role.Rules.Count; index++)
            {
                var rule = role.Rules[index];
                if (rule == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.SourceOwnerModId)
                    && !string.IsNullOrWhiteSpace(rule.SourceCgId))
                {
                    var key = rule.SourceOwnerModId.Trim() + ":" + rule.SourceCgId.Trim();
                    RoleEntries[key] = new RoleCgEntryOverrideSettings
                    {
                        Presentation = PresentationOverride(rule.EffectivePresentation)
                    };
                    SetRoleSelection(
                        roleId,
                        AuraToolsRoleCgContextKeys.SkillChannel,
                        rule.CardId,
                        role.Enabled && rule.Enabled
                            ? key
                            : AuraToolsRoleCgContextKeys.NoneSelectionCgId);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(rule.Image))
                {
                    continue;
                }

                var presentation = rule.EffectivePresentation ?? rule.Presentation.Resolve(DefaultPresentation);
                var manual = new RoleCgManualEntrySettings
                {
                    CgId = "user.legacy." + SafeManualId(roleId) + "." + (index + 1),
                    DisplayName = string.IsNullOrWhiteSpace(rule.DisplayName) ? "导入的技能 CG" : rule.DisplayName,
                    RoleId = roleId,
                    SignalId = "aura.role.skill.committed",
                    SkillId = rule.CardId,
                    Resource = rule.Image,
                    Priority = Math.Max(1000, rule.Priority),
                    Presentation = presentation
                };
                ManualRoleEntries.Add(manual);
                SetRoleSelection(
                    roleId,
                    AuraToolsRoleCgContextKeys.SkillChannel,
                    rule.CardId,
                    role.Enabled && rule.Enabled
                        ? "AuraToolsExp:" + manual.CgId
                        : AuraToolsRoleCgContextKeys.NoneSelectionCgId);
            }
        }

        Roles.Clear();
        LegacyRoleRulesMigrated = true;
        foreach (var entry in ManualRoleEntries)
        {
            entry.Normalize();
        }
    }

    private static string SafeManualId(string value)
    {
        var chars = (value ?? "")
            .Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? char.ToLowerInvariant(character)
                : '-')
            .ToArray();
        var result = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "role" : result;
    }

    private static CardUseCgPresentationOverrideSettings PresentationOverride(
        SkillCgPresentationSettings? presentation)
    {
        presentation ??= SkillCgPresentationSettings.CreateDefault();
        return new CardUseCgPresentationOverrideSettings
        {
            PresentationMode = presentation.Mode,
            FitMode = presentation.Fit,
            FadeIn = presentation.FadeIn,
            Hold = presentation.Hold,
            FadeOut = presentation.FadeOut,
            FocusX = presentation.FocusX,
            FocusY = presentation.FocusY,
            SafeScale = presentation.SafeScale
        };
    }

    internal bool TryImportLegacySettlementCg(LegacyDamageSettlementCgSettings? legacy)
    {
        if (legacy == null || hasExplicitEventCg)
        {
            return false;
        }

        legacy.Normalize();
        eventCg = AuraToolsEventCgSettings.FromLegacy(legacy);
        hasExplicitEventCg = true;
        return true;
    }
}

public sealed class AuraToolsEventCgSettings
{
    public const int CurrentSchemaVersion = 3;

    internal const string RetiredDefaultBackgroundResource = "Mods/AuraToolsExp/ModResource/DPSCG/DPS-CG.png";

    private Dictionary<string, AuraToolsEventCgSceneSettings> scenes =
        new(StringComparer.OrdinalIgnoreCase);
    private bool hasExplicitScenes;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    [JsonProperty("playSettlementAfterBattleScene")]
    public bool PlaySettlementAfterBattleScene { get; set; }

    [JsonProperty("scenes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, AuraToolsEventCgSceneSettings> Scenes
    {
        get => scenes;
        set
        {
            scenes = value ?? new Dictionary<string, AuraToolsEventCgSceneSettings>(StringComparer.OrdinalIgnoreCase);
            hasExplicitScenes = true;
        }
    }

    [JsonProperty("backgroundResource")]
    private string LegacyBackgroundResource { get; set; } = "";

    [JsonProperty("baseWidth")]
    private int LegacyBaseWidth { get; set; } = AuraToolsEventCgDefaults.BaseWidth;

    [JsonProperty("baseHeight")]
    private int LegacyBaseHeight { get; set; } = AuraToolsEventCgDefaults.BaseHeight;

    [JsonProperty("fadeIn")]
    private float LegacyFadeIn { get; set; } = AuraToolsEventCgDefaults.FadeIn;

    [JsonProperty("hold")]
    private float LegacyHold { get; set; } = AuraToolsEventCgDefaults.Hold;

    [JsonProperty("fadeOut")]
    private float LegacyFadeOut { get; set; } = AuraToolsEventCgDefaults.FadeOut;

    [JsonProperty("specialBattleIds")]
    private List<string> LegacySpecialBattleIds { get; set; } = new();

    [JsonProperty("specialOpeningEnabled")]
    private bool LegacySpecialOpeningEnabled { get; set; } = true;

    [JsonProperty("specialVictoryEnabled")]
    private bool LegacySpecialVictoryEnabled { get; set; } = true;

    [JsonProperty("battleDefeatEnabled")]
    private bool LegacyBattleDefeatEnabled { get; set; } = true;

    [JsonProperty("adventureSettlementEnabled")]
    private bool LegacyAdventureSettlementEnabled { get; set; } = true;

    public void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        LegacyBackgroundResource = string.IsNullOrWhiteSpace(LegacyBackgroundResource)
                                   || string.Equals(
                                       LegacyBackgroundResource.Trim(),
                                       RetiredDefaultBackgroundResource,
                                       StringComparison.OrdinalIgnoreCase)
            ? ""
            : LegacyBackgroundResource.Trim();
        LegacyBaseWidth = Math.Max(1, Math.Min(8192, LegacyBaseWidth));
        LegacyBaseHeight = Math.Max(1, Math.Min(8192, LegacyBaseHeight));
        LegacyFadeIn = Math.Max(0f, Math.Min(5f, LegacyFadeIn));
        LegacyHold = Math.Max(0.1f, Math.Min(30f, LegacyHold));
        LegacyFadeOut = Math.Max(0f, Math.Min(5f, LegacyFadeOut));
        LegacySpecialBattleIds = (LegacySpecialBattleIds ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToList();

        if (!hasExplicitScenes)
        {
            scenes = CreateMigratedScenes();
            hasExplicitScenes = true;
        }

        scenes ??= new Dictionary<string, AuraToolsEventCgSceneSettings>(StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, AuraToolsEventCgSceneSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var sceneId in AuraToolsEventCgSceneIds.All)
        {
            var scene = scenes.TryGetValue(sceneId, out var configured) && configured != null
                ? configured
                : new AuraToolsEventCgSceneSettings();
            scene.Normalize(sceneId);
            normalized[sceneId] = scene;
        }
        scenes = normalized;
    }

    public AuraToolsEventCgSceneSettings GetScene(string sceneId)
    {
        Normalize();
        var normalized = AuraToolsEventCgSceneIds.Normalize(sceneId);
        return scenes.TryGetValue(normalized, out var scene)
            ? scene
            : scenes[AuraToolsEventCgSceneIds.AdventureSettlement];
    }

    public bool ShouldSerializeLegacyBackgroundResource() => false;
    public bool ShouldSerializeLegacyBaseWidth() => false;
    public bool ShouldSerializeLegacyBaseHeight() => false;
    public bool ShouldSerializeLegacyFadeIn() => false;
    public bool ShouldSerializeLegacyHold() => false;
    public bool ShouldSerializeLegacyFadeOut() => false;
    public bool ShouldSerializeLegacySpecialBattleIds() => false;
    public bool ShouldSerializeLegacySpecialOpeningEnabled() => false;
    public bool ShouldSerializeLegacySpecialVictoryEnabled() => false;
    public bool ShouldSerializeLegacyBattleDefeatEnabled() => false;
    public bool ShouldSerializeLegacyAdventureSettlementEnabled() => false;

    private Dictionary<string, AuraToolsEventCgSceneSettings> CreateMigratedScenes()
    {
        var result = AuraToolsEventCgSceneIds.All.ToDictionary(
            sceneId => sceneId,
            sceneId => new AuraToolsEventCgSceneSettings
            {
                Enabled = LegacyEnabled(sceneId),
                BackgroundResource = LegacyBackgroundResource,
                BaseWidth = LegacyBaseWidth == AuraToolsEventCgDefaults.BaseWidth ? null : LegacyBaseWidth,
                BaseHeight = LegacyBaseHeight == AuraToolsEventCgDefaults.BaseHeight ? null : LegacyBaseHeight,
                FadeIn = Nearly(LegacyFadeIn, AuraToolsEventCgDefaults.FadeIn) ? null : LegacyFadeIn,
                Hold = Nearly(LegacyHold, AuraToolsEventCgDefaults.Hold) ? null : LegacyHold,
                FadeOut = Nearly(LegacyFadeOut, AuraToolsEventCgDefaults.FadeOut) ? null : LegacyFadeOut
            },
            StringComparer.OrdinalIgnoreCase);
        result[AuraToolsEventCgSceneIds.BattleOpening].BattleIds = LegacySpecialBattleIds.ToList();
        return result;
    }

    private bool LegacyEnabled(string sceneId)
    {
        if (AuraToolsEventCgSceneIds.IsVictory(sceneId)) return LegacySpecialVictoryEnabled;
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.BattleOpening, StringComparison.OrdinalIgnoreCase)) return LegacySpecialOpeningEnabled;
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.BattleDefeat, StringComparison.OrdinalIgnoreCase)) return LegacyBattleDefeatEnabled;
        return LegacyAdventureSettlementEnabled;
    }

    private static bool Nearly(float left, float right)
    {
        return Math.Abs(left - right) < 0.0001f;
    }

    internal static AuraToolsEventCgSettings FromLegacy(LegacyDamageSettlementCgSettings legacy)
    {
        var settings = new AuraToolsEventCgSettings
        {
            Enabled = legacy.Enabled,
            SyncRemote = legacy.SyncRemote,
            LegacyBackgroundResource = legacy.BackgroundResource,
            LegacyBaseWidth = legacy.BaseWidth,
            LegacyBaseHeight = legacy.BaseHeight,
            LegacyFadeIn = legacy.FadeIn,
            LegacyHold = legacy.Hold,
            LegacyFadeOut = legacy.FadeOut
        };
        settings.Normalize();
        return settings;
    }
}

public static class AuraToolsEventCgSceneIds
{
    public const string VictoryStandard = "victory.standard";
    public const string VictoryMidas = "victory.midas";
    public const string VictoryRitual = "victory.ritual";
    public const string VictoryCurse = "victory.curse";
    public const string BattleOpening = "battle-opening";
    public const string BattleDefeat = "battle-defeat";
    public const string AdventureSettlement = "adventure-settlement";

    public static readonly string[] Victory =
    {
        VictoryStandard,
        VictoryMidas,
        VictoryRitual,
        VictoryCurse
    };

    public static readonly string[] All =
    {
        VictoryStandard,
        VictoryMidas,
        VictoryRitual,
        VictoryCurse,
        BattleOpening,
        BattleDefeat,
        AdventureSettlement
    };

    public static string Normalize(string? value)
    {
        var candidate = (value ?? "").Trim().ToLowerInvariant();
        return All.FirstOrDefault(sceneId => string.Equals(sceneId, candidate, StringComparison.OrdinalIgnoreCase))
               ?? AdventureSettlement;
    }

    public static bool IsVictory(string? sceneId)
    {
        return Victory.Contains(Normalize(sceneId), StringComparer.OrdinalIgnoreCase);
    }

    public static string CgId(string sceneId)
    {
        return "event." + Normalize(sceneId);
    }

    public static string FromCgId(string? cgId)
    {
        var value = (cgId ?? "").Trim();
        return value.StartsWith("event.", StringComparison.OrdinalIgnoreCase)
            ? Normalize(value.Substring("event.".Length))
            : Normalize(value);
    }
}

public static class AuraToolsEventCgOutcomeReasons
{
    public const string StandardVictory = "standard-victory";
    public const string MidasEscape = "midas-escape";
    public const string RitualVictory = "ritual-victory";
    public const string CurseVictory = "curse-victory";
    public const string Defeat = "defeat";
    public const string Escape = "escape";

    public static string SceneForReason(string? reason)
    {
        var value = (reason ?? "").Trim();
        if (string.Equals(value, MidasEscape, StringComparison.OrdinalIgnoreCase)) return AuraToolsEventCgSceneIds.VictoryMidas;
        if (string.Equals(value, RitualVictory, StringComparison.OrdinalIgnoreCase)) return AuraToolsEventCgSceneIds.VictoryRitual;
        if (string.Equals(value, CurseVictory, StringComparison.OrdinalIgnoreCase)) return AuraToolsEventCgSceneIds.VictoryCurse;
        return AuraToolsEventCgSceneIds.VictoryStandard;
    }
}

public static class AuraToolsEventCgDefaults
{
    public const int BaseWidth = 1600;
    public const int BaseHeight = 900;
    public const float FadeIn = 0.35f;
    public const float Hold = 3f;
    public const float FadeOut = 0.45f;
}

public sealed class AuraToolsEventCgSceneSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("backgroundResource", NullValueHandling = NullValueHandling.Ignore)]
    public string BackgroundResource { get; set; } = "";

    [JsonProperty("baseWidth", NullValueHandling = NullValueHandling.Ignore)]
    public int? BaseWidth { get; set; }

    [JsonProperty("baseHeight", NullValueHandling = NullValueHandling.Ignore)]
    public int? BaseHeight { get; set; }

    [JsonProperty("fadeIn", NullValueHandling = NullValueHandling.Ignore)]
    public float? FadeIn { get; set; }

    [JsonProperty("hold", NullValueHandling = NullValueHandling.Ignore)]
    public float? Hold { get; set; }

    [JsonProperty("fadeOut", NullValueHandling = NullValueHandling.Ignore)]
    public float? FadeOut { get; set; }

    [JsonProperty("battleIds", NullValueHandling = NullValueHandling.Ignore)]
    public List<string> BattleIds { get; set; } = new();

    [JsonIgnore]
    public string SceneId { get; private set; } = AuraToolsEventCgSceneIds.AdventureSettlement;

    [JsonIgnore]
    public string EffectiveBackgroundResource => BackgroundResource;

    [JsonIgnore]
    public int EffectiveBaseWidth => BaseWidth ?? AuraToolsEventCgDefaults.BaseWidth;

    [JsonIgnore]
    public int EffectiveBaseHeight => BaseHeight ?? AuraToolsEventCgDefaults.BaseHeight;

    [JsonIgnore]
    public float EffectiveFadeIn => FadeIn ?? AuraToolsEventCgDefaults.FadeIn;

    [JsonIgnore]
    public float EffectiveHold => Hold ?? AuraToolsEventCgDefaults.Hold;

    [JsonIgnore]
    public float EffectiveFadeOut => FadeOut ?? AuraToolsEventCgDefaults.FadeOut;

    [JsonIgnore]
    public bool UsesDefaultPresentation => string.IsNullOrWhiteSpace(BackgroundResource)
                                           && !BaseWidth.HasValue
                                           && !BaseHeight.HasValue
                                           && !FadeIn.HasValue
                                           && !Hold.HasValue
                                           && !FadeOut.HasValue;

    public void Normalize(string sceneId)
    {
        SceneId = AuraToolsEventCgSceneIds.Normalize(sceneId);
        BackgroundResource = (BackgroundResource ?? "").Trim().Replace('\\', '/');
        if (string.Equals(
                BackgroundResource,
                AuraToolsEventCgSettings.RetiredDefaultBackgroundResource,
                StringComparison.OrdinalIgnoreCase))
        {
            BackgroundResource = "";
        }
        BaseWidth = BaseWidth.HasValue ? Math.Max(1, Math.Min(8192, BaseWidth.Value)) : null;
        BaseHeight = BaseHeight.HasValue ? Math.Max(1, Math.Min(8192, BaseHeight.Value)) : null;
        FadeIn = Clamp(FadeIn, 0f, 5f);
        Hold = Clamp(Hold, 0.1f, 30f);
        FadeOut = Clamp(FadeOut, 0f, 5f);
        BattleIds = (BattleIds ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToList();
    }

    public void ResetPresentation()
    {
        BackgroundResource = "";
        BaseWidth = null;
        BaseHeight = null;
        FadeIn = null;
        Hold = null;
        FadeOut = null;
    }

    private static float? Clamp(float? value, float minimum, float maximum)
    {
        return value.HasValue ? Math.Max(minimum, Math.Min(maximum, value.Value)) : null;
    }
}

public sealed class RoleCgEntryOverrideSettings
{
    [JsonProperty("enabled", NullValueHandling = NullValueHandling.Ignore)]
    private bool? LegacyEnabled { get; set; }

    [JsonProperty("presentation")]
    public CardUseCgPresentationOverrideSettings Presentation { get; set; } = new();

    public void Normalize()
    {
        Presentation ??= new CardUseCgPresentationOverrideSettings();
        Presentation.Normalize();
    }

    public bool ShouldSerializeLegacyEnabled()
    {
        return false;
    }
}

public static class AuraToolsRoleCgContextKeys
{
    public const string SkillChannel = "skill";
    public const string NoneSelectionCgId = "AuraToolsExp:role-cg.none";

    public static string Create(string roleId, string channel, string skillId = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId).ToLowerInvariant();
        var normalizedChannel = (channel ?? "").Trim().ToLowerInvariant();
        var normalizedSkill = string.Equals(normalizedChannel, SkillChannel, StringComparison.Ordinal)
            ? (skillId ?? "").Trim().ToLowerInvariant()
            : "";
        return normalizedChannel + "|" + normalizedRole + "|" + normalizedSkill;
    }
}

public sealed class RoleCgManualEntrySettings
{
    [JsonProperty("cgId")]
    public string CgId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("signalId")]
    public string SignalId { get; set; } = "";

    [JsonProperty("skillId")]
    public string SkillId { get; set; } = "";

    [JsonProperty("resource")]
    public string Resource { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; } = 1000;

    [JsonProperty("presentation")]
    public SkillCgPresentationSettings Presentation { get; set; } = SkillCgPresentationSettings.CreateDefault();

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(CgId)
                           && !string.IsNullOrWhiteSpace(RoleId)
                           && !string.IsNullOrWhiteSpace(SignalId)
                           && !string.IsNullOrWhiteSpace(Resource);

    public void Normalize()
    {
        CgId = (CgId ?? "").Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "玩家自定义 CG" : DisplayName.Trim();
        RoleId = RoleCatalog.NormalizeRoleId(RoleId);
        SignalId = (SignalId ?? "").Trim().ToLowerInvariant();
        SkillId = (SkillId ?? "").Trim();
        Resource = (Resource ?? "").Trim().Replace('\\', '/').TrimStart('/');
        Priority = Math.Max(1000, Math.Min(10000, Priority));
        Presentation = (Presentation ?? SkillCgPresentationSettings.CreateDefault())
            .Resolve(SkillCgPresentationSettings.CreateDefault());
    }
}

public sealed class AuraToolsCardUseCgSettings
{
    public const int CurrentSchemaVersion = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("registeredEntries")]
    public Dictionary<string, bool> RegisteredEntries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("presentationOverrides")]
    public Dictionary<string, CardUseCgPresentationOverrideSettings> PresentationOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        RegisteredEntries ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        RegisteredEntries = RegisteredEntries
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => MigrateQualifiedCgId(pair.Key.Trim()), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        PresentationOverrides = (PresentationOverrides ?? new Dictionary<string, CardUseCgPresentationOverrideSettings>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            .GroupBy(pair => MigrateQualifiedCgId(pair.Key.Trim()), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
            {
                var value = group.Last().Value;
                value.Normalize();
                return value;
            }, StringComparer.OrdinalIgnoreCase);
    }

    private static string MigrateQualifiedCgId(string value)
    {
        return string.Equals(value, "AuraToolsExp:terrias.blazing-crown-collapse", StringComparison.OrdinalIgnoreCase)
            ? "Terrias:terrias.blazing-crown-collapse"
            : value;
    }
}

public sealed class CardUseCgPresentationOverrideSettings
{
    [JsonProperty("presentationMode")] public string PresentationMode { get; set; } = "";
    [JsonProperty("fitMode")] public string FitMode { get; set; } = "";
    [JsonProperty("fadeIn")] public float? FadeIn { get; set; }
    [JsonProperty("hold")] public float? Hold { get; set; }
    [JsonProperty("fadeOut")] public float? FadeOut { get; set; }
    [JsonProperty("focusX")] public float? FocusX { get; set; }
    [JsonProperty("focusY")] public float? FocusY { get; set; }
    [JsonProperty("safeScale")] public float? SafeScale { get; set; }
    [JsonProperty("frameSeconds")] public float? FrameSeconds { get; set; }
    [JsonProperty("alphaMode")] public string AlphaMode { get; set; } = "";
    [JsonProperty("keyThreshold")] public float? KeyThreshold { get; set; }
    [JsonProperty("keySoftness")] public float? KeySoftness { get; set; }
    [JsonProperty("flashMode")] public string FlashMode { get; set; } = "";
    [JsonProperty("flashAtSeconds")] public float? FlashAtSeconds { get; set; }
    [JsonProperty("flashDuration")] public float? FlashDuration { get; set; }
    [JsonProperty("flashStartFrame")] public int? FlashStartFrame { get; set; }
    [JsonProperty("flashEndFrame")] public int? FlashEndFrame { get; set; }
    [JsonProperty("flashPulseEveryFrames")] public int? FlashPulseEveryFrames { get; set; }
    [JsonProperty("flashStrength")] public float? FlashStrength { get; set; }

    public void Normalize()
    {
        PresentationMode = PresentationMode?.Trim() ?? "";
        FitMode = FitMode?.Trim() ?? "";
        AlphaMode = AlphaMode?.Trim() ?? "";
        FlashMode = FlashMode?.Trim() ?? "";
        FadeIn = ClampNullable(FadeIn, 0f, 10f);
        Hold = ClampNullable(Hold, 0f, 30f);
        FadeOut = ClampNullable(FadeOut, 0f, 10f);
        FocusX = ClampNullable(FocusX, 0f, 1f);
        FocusY = ClampNullable(FocusY, 0f, 1f);
        SafeScale = ClampNullable(SafeScale, 1f, 3f);
        FrameSeconds = ClampNullable(FrameSeconds, 0.01f, 1f);
        KeyThreshold = ClampNullable(KeyThreshold, 0f, 1f);
        KeySoftness = ClampNullable(KeySoftness, 0.001f, 1f);
        FlashAtSeconds = FlashAtSeconds.HasValue && FlashAtSeconds.Value < 0f ? null : FlashAtSeconds;
        FlashDuration = ClampNullable(FlashDuration, 0.03f, 1f);
        FlashStartFrame = FlashStartFrame.HasValue ? Math.Max(0, FlashStartFrame.Value) : null;
        FlashEndFrame = FlashEndFrame.HasValue ? Math.Max(0, FlashEndFrame.Value) : null;
        FlashPulseEveryFrames = FlashPulseEveryFrames.HasValue ? Math.Max(1, FlashPulseEveryFrames.Value) : null;
        FlashStrength = ClampNullable(FlashStrength, 0f, 1f);
    }

    private static float? ClampNullable(float? value, float minimum, float maximum)
    {
        return value.HasValue ? Math.Max(minimum, Math.Min(maximum, value.Value)) : null;
    }
}

public sealed class SkillCgRoleSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("defaultPresentation")]
    public SkillCgPresentationSettings DefaultPresentation { get; set; } = SkillCgPresentationSettings.CreateInherited();

    [JsonProperty("rules")]
    public List<SkillCgRuleSettings> Rules { get; set; } = new();

    public void Normalize(string fallbackRoleId, SkillCgPresentationSettings fallbackPresentation)
    {
        RoleId = RoleCatalog.NormalizeRoleId(string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId);
        DisplayName = DisplayName?.Trim() ?? "";
        DefaultPresentation = (DefaultPresentation ?? SkillCgPresentationSettings.CreateInherited()).Resolve(fallbackPresentation);
        Rules ??= new List<SkillCgRuleSettings>();
        for (var i = Rules.Count - 1; i >= 0; i--)
        {
            var rule = Rules[i];
            if (rule == null || !rule.IsActiveSkillRule())
            {
                Rules.RemoveAt(i);
                continue;
            }

            rule.Normalize(DefaultPresentation);
        }
    }
}

public sealed class SkillCgPresentationSettings
{
    [JsonProperty("mode")]
    public string Mode { get; set; } = "";

    [JsonProperty("fit")]
    public string Fit { get; set; } = "";

    [JsonProperty("fadeIn")]
    public float FadeIn { get; set; } = -1f;

    [JsonProperty("hold")]
    public float Hold { get; set; } = -1f;

    [JsonProperty("fadeOut")]
    public float FadeOut { get; set; } = -1f;

    [JsonProperty("focusX")]
    public float FocusX { get; set; } = -1f;

    [JsonProperty("focusY")]
    public float FocusY { get; set; } = -1f;

    [JsonProperty("safeScale")]
    public float SafeScale { get; set; } = -1f;

    public static SkillCgPresentationSettings CreateDefault()
    {
        return new SkillCgPresentationSettings
        {
            Mode = SkillCgPresentationModeNames.Slide,
            Fit = SkillCgFitModeNames.Contain,
            FadeIn = 0.35f,
            Hold = 1f,
            FadeOut = 0.45f,
            FocusX = 0.5f,
            FocusY = 0.5f,
            SafeScale = 1f
        };
    }

    public static SkillCgPresentationSettings CreateInherited()
    {
        return new SkillCgPresentationSettings();
    }

    public SkillCgPresentationSettings Resolve(SkillCgPresentationSettings fallback)
    {
        fallback ??= CreateDefault();
        return new SkillCgPresentationSettings
        {
            Mode = string.IsNullOrWhiteSpace(Mode)
                ? SkillCgPresentationModeNames.Normalize(fallback.Mode)
                : SkillCgPresentationModeNames.Normalize(Mode),
            Fit = string.IsNullOrWhiteSpace(Fit)
                ? SkillCgFitModeNames.Normalize(fallback.Fit)
                : SkillCgFitModeNames.Normalize(Fit),
            FadeIn = FadeIn >= 0f ? FadeIn : Math.Max(0f, fallback.FadeIn),
            Hold = Hold >= 0f ? Hold : Math.Max(0f, fallback.Hold),
            FadeOut = FadeOut >= 0f ? FadeOut : Math.Max(0f, fallback.FadeOut),
            FocusX = FocusX >= 0f ? Clamp01(FocusX) : Clamp01(fallback.FocusX),
            FocusY = FocusY >= 0f ? Clamp01(FocusY) : Clamp01(fallback.FocusY),
            SafeScale = SafeScale >= 0f ? ClampSafeScale(SafeScale) : ClampSafeScale(fallback.SafeScale)
        };
    }

    public SkillCgPresentationSettings ResolveRule(SkillCgPresentationSettings fallback, float legacyFadeIn, float legacyHold, float legacyFadeOut)
    {
        fallback ??= CreateDefault();
        return new SkillCgPresentationSettings
        {
            Mode = string.IsNullOrWhiteSpace(Mode)
                ? SkillCgPresentationModeNames.Normalize(fallback.Mode)
                : SkillCgPresentationModeNames.Normalize(Mode),
            Fit = string.IsNullOrWhiteSpace(Fit)
                ? SkillCgFitModeNames.Normalize(fallback.Fit)
                : SkillCgFitModeNames.Normalize(Fit),
            FadeIn = FadeIn >= 0f ? FadeIn : (legacyFadeIn >= 0f ? legacyFadeIn : Math.Max(0f, fallback.FadeIn)),
            Hold = Hold >= 0f ? Hold : (legacyHold >= 0f ? legacyHold : Math.Max(0f, fallback.Hold)),
            FadeOut = FadeOut >= 0f ? FadeOut : (legacyFadeOut >= 0f ? legacyFadeOut : Math.Max(0f, fallback.FadeOut)),
            FocusX = FocusX >= 0f ? Clamp01(FocusX) : Clamp01(fallback.FocusX),
            FocusY = FocusY >= 0f ? Clamp01(FocusY) : Clamp01(fallback.FocusY),
            SafeScale = SafeScale >= 0f ? ClampSafeScale(SafeScale) : ClampSafeScale(fallback.SafeScale)
        };
    }

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
    }

    private static float ClampSafeScale(float value)
    {
        return Math.Max(1f, Math.Min(3f, value <= 0f ? 1f : value));
    }
}

internal static class SkillCgPresentationModeNames
{
    public const string Slide = "slide";
    public const string FullscreenFade = "fullscreenFade";
    public const string CenterFade = "centerFade";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, FullscreenFade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullScreenFade", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullscreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullScreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fade", StringComparison.OrdinalIgnoreCase))
        {
            return FullscreenFade;
        }

        if (string.Equals(mode, CenterFade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "center", StringComparison.OrdinalIgnoreCase))
        {
            return CenterFade;
        }

        return Slide;
    }
}

internal static class SkillCgFitModeNames
{
    public const string Contain = "contain";
    public const string Cover = "cover";
    public const string Stretch = "stretch";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, Cover, StringComparison.OrdinalIgnoreCase))
        {
            return Cover;
        }

        if (string.Equals(mode, Stretch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fill", StringComparison.OrdinalIgnoreCase))
        {
            return Stretch;
        }

        return Contain;
    }
}

public sealed class SkillCgRuleSettings
{
    public const string TriggerActiveSkill = "ActiveSkill";
    private const string LegacyTriggerPassiveSkill = "PassiveSkill";
    private const string LegacyTriggerPassiveEvent = "PassiveEvent";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("providerId")]
    public string ProviderId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("sourceOwnerModId")]
    public string SourceOwnerModId { get; set; } = "";

    [JsonProperty("sourceCgId")]
    public string SourceCgId { get; set; } = "";

    [JsonProperty("triggerType")]
    public string LegacyTriggerType { get; set; } = TriggerActiveSkill;

    [JsonProperty("cardId")]
    public string CardId { get; set; } = "*";

    [JsonProperty("action")]
    public string Action { get; set; } = "*";

    [JsonProperty("image")]
    public string Image { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; } = 10;

    [JsonProperty("fadeIn")]
    public float FadeIn { get; set; } = -1f;

    [JsonProperty("hold")]
    public float Hold { get; set; } = -1f;

    [JsonProperty("fadeOut")]
    public float FadeOut { get; set; } = -1f;

    [JsonProperty("presentation")]
    public SkillCgPresentationSettings Presentation { get; set; } = SkillCgPresentationSettings.CreateInherited();

    [JsonIgnore]
    public SkillCgPresentationSettings EffectivePresentation { get; private set; } = SkillCgPresentationSettings.CreateDefault();

    public bool IsActiveSkillRule()
    {
        return !string.Equals(LegacyTriggerType, LegacyTriggerPassiveSkill, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(LegacyTriggerType, LegacyTriggerPassiveEvent, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldSerializeLegacyTriggerType()
    {
        return false;
    }

    public void Normalize(SkillCgPresentationSettings fallbackPresentation)
    {
        LegacyTriggerType = TriggerActiveSkill;
        CardId = string.IsNullOrWhiteSpace(CardId) ? "*" : CardId.Trim();
        Action = string.IsNullOrWhiteSpace(Action) ? "*" : Action.Trim();
        DisplayName = DisplayName?.Trim() ?? "";
        SourceOwnerModId = SourceOwnerModId?.Trim() ?? "";
        SourceCgId = SourceCgId?.Trim() ?? "";
        if (string.Equals(SourceOwnerModId, "AuraToolsExp", StringComparison.OrdinalIgnoreCase)
            && IsMigratedTerriasCg(SourceCgId))
        {
            SourceOwnerModId = "Terrias";
        }
        Image = Image?.Trim() ?? "";
        ProviderId = ProviderId?.Trim() ?? "";
        Presentation ??= SkillCgPresentationSettings.CreateInherited();
        EffectivePresentation = Presentation.ResolveRule(fallbackPresentation, FadeIn, Hold, FadeOut);
        FadeIn = EffectivePresentation.FadeIn;
        Hold = EffectivePresentation.Hold;
        FadeOut = EffectivePresentation.FadeOut;
    }

    private static bool IsMigratedTerriasCg(string cgId)
    {
        return string.Equals(cgId, "loneer.morning-star-prayer", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cgId, "wuna.white-sun-prayer", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cgId, "columbina.homesickness", StringComparison.OrdinalIgnoreCase);
    }
}
