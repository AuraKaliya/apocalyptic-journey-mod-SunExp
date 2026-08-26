using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsSkillCgSettings
{
    public const int CurrentSchemaVersion = 7;

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
    public float LowHealthThreshold { get; set; } = 0.3f;

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

    [JsonProperty("roles")]
    public Dictionary<string, SkillCgRoleSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        CardUseCg ??= new AuraToolsCardUseCgSettings();
        CardUseCg.Normalize();
        eventCg ??= new AuraToolsEventCgSettings();
        eventCg.Normalize();
        LowHealthThreshold = Math.Max(0.05f, Math.Min(0.95f, LowHealthThreshold));
        MaxQueueLength = Math.Max(1, Math.Min(30, MaxQueueLength));
        MaxRequestAgeSeconds = Math.Max(0.5f, Math.Min(30f, MaxRequestAgeSeconds));
        DuplicateWindowSeconds = Math.Max(0.02f, Math.Min(2f, DuplicateWindowSeconds));
        MaxHookFailures = Math.Max(1, Math.Min(20, MaxHookFailures));
        DefaultPresentation = (DefaultPresentation ?? SkillCgPresentationSettings.CreateDefault())
            .Resolve(SkillCgPresentationSettings.CreateDefault());
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
    public const int CurrentSchemaVersion = 1;

    public const string DefaultBackgroundResource = "Mods/AuraToolsExp/ModResource/DPSCG/DPS-CG.png";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    [JsonProperty("backgroundResource")]
    public string BackgroundResource { get; set; } = DefaultBackgroundResource;

    [JsonProperty("baseWidth")]
    public int BaseWidth { get; set; } = 1600;

    [JsonProperty("baseHeight")]
    public int BaseHeight { get; set; } = 900;

    [JsonProperty("fadeIn")]
    public float FadeIn { get; set; } = 0.35f;

    [JsonProperty("hold")]
    public float Hold { get; set; } = 3f;

    [JsonProperty("fadeOut")]
    public float FadeOut { get; set; } = 0.45f;

    [JsonProperty("specialBattleIds")]
    public List<string> SpecialBattleIds { get; set; } = new();

    [JsonProperty("specialOpeningEnabled")]
    public bool SpecialOpeningEnabled { get; set; } = true;

    [JsonProperty("specialVictoryEnabled")]
    public bool SpecialVictoryEnabled { get; set; } = true;

    [JsonProperty("battleDefeatEnabled")]
    public bool BattleDefeatEnabled { get; set; } = true;

    [JsonProperty("adventureSettlementEnabled")]
    public bool AdventureSettlementEnabled { get; set; } = true;

    public void Normalize()
    {
        BackgroundResource = string.IsNullOrWhiteSpace(BackgroundResource)
            ? DefaultBackgroundResource
            : BackgroundResource.Trim();
        BaseWidth = Math.Max(1, Math.Min(8192, BaseWidth));
        BaseHeight = Math.Max(1, Math.Min(8192, BaseHeight));
        FadeIn = Math.Max(0f, Math.Min(5f, FadeIn));
        Hold = Math.Max(0.1f, Math.Min(30f, Hold));
        FadeOut = Math.Max(0f, Math.Min(5f, FadeOut));
        SpecialBattleIds = (SpecialBattleIds ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToList();
    }

    internal static AuraToolsEventCgSettings FromLegacy(LegacyDamageSettlementCgSettings legacy)
    {
        return new AuraToolsEventCgSettings
        {
            Enabled = legacy.Enabled,
            SyncRemote = legacy.SyncRemote,
            BackgroundResource = legacy.BackgroundResource,
            BaseWidth = legacy.BaseWidth,
            BaseHeight = legacy.BaseHeight,
            FadeIn = legacy.FadeIn,
            Hold = legacy.Hold,
            FadeOut = legacy.FadeOut
        };
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
