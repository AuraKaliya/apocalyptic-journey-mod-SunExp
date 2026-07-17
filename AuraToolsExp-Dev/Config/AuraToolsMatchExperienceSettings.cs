using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsMatchExperienceSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 7;

    [JsonProperty("starterDeck")]
    public StarterDeckSettings StarterDeck { get; set; } = new();

    [JsonProperty("safeBox")]
    public SafeBoxSettings SafeBox { get; set; } = new();

    [JsonProperty("modSync")]
    public ModSyncSettings ModSync { get; set; } = new();

    [JsonProperty("feast")]
    public FeastSettings Feast { get; set; } = new();

    [JsonProperty("damageMeter")]
    public DamageMeterSettings DamageMeter { get; set; } = new();

    [JsonProperty("cardRefresh")]
    public CardRefreshSettings CardRefresh { get; set; } = new();

    public void Normalize()
    {
        var loadedSchemaVersion = SchemaVersion;
        SchemaVersion = Math.Max(7, SchemaVersion);
        StarterDeck ??= new StarterDeckSettings();
        SafeBox ??= new SafeBoxSettings();
        ModSync ??= new ModSyncSettings();
        Feast ??= new FeastSettings();
        if (loadedSchemaVersion < 6)
        {
            Feast.Enabled = true;
        }

        DamageMeter ??= new DamageMeterSettings();
        CardRefresh ??= new CardRefreshSettings();
        StarterDeck.Normalize();
        Feast.Normalize();
        DamageMeter.Normalize();
    }
}

public sealed class CardRefreshSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }
}

public sealed class StarterDeckSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("mode")]
    public string Mode { get; set; } = StarterDeckModes.Global;

    [JsonProperty("preferRoleModProfile")]
    public bool PreferRoleModProfile { get; set; } = true;

    [JsonProperty("globalProfile")]
    public StarterDeckLocalProfileSettings GlobalProfile { get; set; } = StarterDeckLocalProfileSettings.CreateGlobal();

    [JsonProperty("roles")]
    public Dictionary<string, StarterDeckLocalProfileSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("selectedProfileByRole")]
    public Dictionary<string, string> SelectedProfileByRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("deckSize")]
    public int DeckSize { get; set; } = 11;

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    public void Normalize()
    {
        Mode = StarterDeckModes.Normalize(Mode);
        PreferRoleModProfile = true;
        DeckSize = Math.Max(1, DeckSize);
        CardIds ??= new List<string>();
        CardIds.RemoveAll(string.IsNullOrWhiteSpace);

        GlobalProfile ??= StarterDeckLocalProfileSettings.CreateGlobal();
        if (GlobalProfile.CardIds.Count == 0 && CardIds.Count > 0)
        {
            GlobalProfile.DeckSize = DeckSize;
            GlobalProfile.CardIds = CardIds.ToList();
        }

        GlobalProfile.Normalize("", "全局自定义卡组");
        Roles ??= new Dictionary<string, StarterDeckLocalProfileSettings>(StringComparer.OrdinalIgnoreCase);
        var normalizedRoles = new Dictionary<string, StarterDeckLocalProfileSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Roles)
        {
            var role = pair.Value ?? new StarterDeckLocalProfileSettings();
            role.Normalize(pair.Key, pair.Key);
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
            normalizedRoles[normalizedKey] = role;
        }

        Roles = normalizedRoles;
        var normalizedSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in SelectedProfileByRole ?? new Dictionary<string, string>())
        {
            var roleId = RoleCatalog.NormalizeRoleId(pair.Key);
            var profileId = pair.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(profileId))
            {
                continue;
            }

            normalizedSelections[roleId] = profileId;
        }

        SelectedProfileByRole = normalizedSelections;

        DeckSize = GlobalProfile.DeckSize;
        CardIds = GlobalProfile.CardIds.ToList();
    }
}

public static class StarterDeckModes
{
    public const string Global = "Global";
    public const string RoleSpecific = "RoleSpecific";

    public static string Normalize(string? value)
    {
        return string.Equals(value, RoleSpecific, StringComparison.OrdinalIgnoreCase) ? RoleSpecific : Global;
    }
}

public sealed class StarterDeckLocalProfileSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("deckSize")]
    public int DeckSize { get; set; } = 11;

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    [JsonProperty("derivedFromProfileId")]
    public string DerivedFromProfileId { get; set; } = "";

    public static StarterDeckLocalProfileSettings CreateGlobal()
    {
        return new StarterDeckLocalProfileSettings
        {
            RoleId = "",
            DisplayName = "全局自定义卡组",
            DeckSize = 11
        };
    }

    public void Normalize(string fallbackRoleId, string fallbackDisplayName = "")
    {
        RoleId = RoleCatalog.NormalizeRoleId(string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId);
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? fallbackDisplayName : DisplayName.Trim();
        DerivedFromProfileId = DerivedFromProfileId?.Trim() ?? "";
        DeckSize = Math.Max(1, DeckSize);
        CardIds ??= new List<string>();
        CardIds.RemoveAll(string.IsNullOrWhiteSpace);
        for (var i = 0; i < CardIds.Count; i++)
        {
            CardIds[i] = CardIds[i].Trim();
        }
    }
}

public sealed class SafeBoxSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }
}

public sealed class ModSyncSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }
}

public sealed class FeastSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("playCg")]
    public bool PlayCg { get; set; } = true;

    [JsonProperty("maxBatchCount")]
    public int MaxBatchCount { get; set; } = 64;

    [JsonProperty("defaultPresentation")]
    public SkillCgPresentationSettings DefaultPresentation { get; set; } = CreateDefaultPresentation();

    [JsonProperty("roles")]
    public Dictionary<string, FeastRoleSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SkillCgPresentationSettings CreateDefaultPresentation()
    {
        return new SkillCgPresentationSettings
        {
            Mode = SkillCgPresentationModeNames.FullscreenFade,
            Fit = SkillCgFitModeNames.Cover,
            FadeIn = 0.35f,
            Hold = 1.5f,
            FadeOut = 0.5f,
            FocusX = 0.5f,
            FocusY = 0.5f,
            SafeScale = 1f
        };
    }

    public void Normalize()
    {
        PlayCg = true;
        MaxBatchCount = Math.Max(1, Math.Min(128, MaxBatchCount));
        DefaultPresentation = (DefaultPresentation ?? CreateDefaultPresentation()).Resolve(CreateDefaultPresentation());
        Roles ??= new Dictionary<string, FeastRoleSettings>(StringComparer.OrdinalIgnoreCase);

        var normalizedRoles = new Dictionary<string, FeastRoleSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Roles)
        {
            var role = pair.Value ?? new FeastRoleSettings();
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
            normalizedRoles[normalizedKey] = role;
        }

        Roles = normalizedRoles;
    }
}

public sealed class FeastRoleSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("selectedCgId")]
    public string SelectedCgId { get; set; } = "";

    [JsonProperty("presentation")]
    public SkillCgPresentationSettings Presentation { get; set; } = SkillCgPresentationSettings.CreateInherited();

    [JsonIgnore]
    public SkillCgPresentationSettings EffectivePresentation { get; private set; } = FeastSettings.CreateDefaultPresentation();

    public void Normalize(string fallbackRoleId, SkillCgPresentationSettings fallbackPresentation)
    {
        RoleId = RoleCatalog.NormalizeRoleId(string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId);
        DisplayName = DisplayName?.Trim() ?? "";
        SelectedCgId = (SelectedCgId ?? "").Trim();
        Presentation = (Presentation ?? SkillCgPresentationSettings.CreateInherited()).Resolve(fallbackPresentation);
        EffectivePresentation = Presentation;
    }
}

public sealed class DamageMeterSettings
{
    private const int FixedMaxRows = 6;
    private const int DefaultMaxHistoryEnvelopeBytes = 1048576;
    private const int DefaultMaxAvatarEncodePixels = 262144;
    private const int DefaultMaxAvatarPngBytes = 262144;
    private const int DefaultUiRefreshIntervalMs = 1000;
    private const int DefaultSubmitBatchIntervalMs = 250;
    private const int DefaultMaxEventsPerBatch = 24;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("hotkey")]
    public string Hotkey { get; set; } = "F8";

    [JsonProperty("showPanelByDefault")]
    public bool ShowPanelByDefault { get; set; }

    [JsonProperty("friendlyOnly")]
    public bool FriendlyOnly { get; set; }

    [JsonProperty("includeUnknownTeam")]
    public bool IncludeUnknownTeam { get; set; } = true;

    [JsonProperty("countShieldLoss")]
    public bool CountShieldLoss { get; set; } = true;

    [JsonProperty("maxRows")]
    public int MaxRows { get; set; } = 6;

    [JsonProperty("showAverageDpt")]
    public bool ShowAverageDpt { get; set; } = true;

    [JsonProperty("showTeamShare")]
    public bool ShowTeamShare { get; set; } = true;

    [JsonProperty("loadHistoryOnStartup")]
    public bool LoadHistoryOnStartup { get; set; }

    [JsonProperty("captureTeamAvatars")]
    public bool CaptureTeamAvatars { get; set; }

    [JsonProperty("maxHistoryEnvelopeBytes")]
    public int MaxHistoryEnvelopeBytes { get; set; } = DefaultMaxHistoryEnvelopeBytes;

    [JsonProperty("maxAvatarEncodePixels")]
    public int MaxAvatarEncodePixels { get; set; } = DefaultMaxAvatarEncodePixels;

    [JsonProperty("maxAvatarPngBytes")]
    public int MaxAvatarPngBytes { get; set; } = DefaultMaxAvatarPngBytes;

    [JsonProperty("uiRefreshIntervalMs")]
    public int UiRefreshIntervalMs { get; set; } = DefaultUiRefreshIntervalMs;

    [JsonProperty("submitBatchIntervalMs")]
    public int SubmitBatchIntervalMs { get; set; } = DefaultSubmitBatchIntervalMs;

    [JsonProperty("maxEventsPerBatch")]
    public int MaxEventsPerBatch { get; set; } = DefaultMaxEventsPerBatch;

    [JsonProperty("settlementCg")]
    public DamageSettlementCgSettings SettlementCg { get; set; } = new();

    public void Normalize()
    {
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "F8" : Hotkey.Trim();
        ShowPanelByDefault = false;
        IncludeUnknownTeam = !FriendlyOnly;
        CountShieldLoss = true;
        MaxRows = FixedMaxRows;
        ShowAverageDpt = true;
        ShowTeamShare = true;
        MaxHistoryEnvelopeBytes = Math.Max(65536, Math.Min(8388608, MaxHistoryEnvelopeBytes <= 0
            ? DefaultMaxHistoryEnvelopeBytes
            : MaxHistoryEnvelopeBytes));
        MaxAvatarEncodePixels = Math.Max(4096, Math.Min(1048576, MaxAvatarEncodePixels <= 0
            ? DefaultMaxAvatarEncodePixels
            : MaxAvatarEncodePixels));
        MaxAvatarPngBytes = Math.Max(16384, Math.Min(1048576, MaxAvatarPngBytes <= 0
            ? DefaultMaxAvatarPngBytes
            : MaxAvatarPngBytes));
        UiRefreshIntervalMs = Math.Max(100, Math.Min(2000, UiRefreshIntervalMs <= 0
            ? DefaultUiRefreshIntervalMs
            : UiRefreshIntervalMs));
        SubmitBatchIntervalMs = Math.Max(50, Math.Min(1000, SubmitBatchIntervalMs <= 0
            ? DefaultSubmitBatchIntervalMs
            : SubmitBatchIntervalMs));
        MaxEventsPerBatch = Math.Max(1, Math.Min(64, MaxEventsPerBatch <= 0
            ? DefaultMaxEventsPerBatch
            : MaxEventsPerBatch));
        SettlementCg ??= new DamageSettlementCgSettings();
        SettlementCg.Normalize();
    }
}

public sealed class DamageSettlementCgSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    [JsonProperty("backgroundResource")]
    public string BackgroundResource { get; set; } = "Mods/AuraToolsExp/ModResource/DPSCG/DPS-CG.png";

    [JsonProperty("baseWidth")]
    public int BaseWidth { get; set; } = 1600;

    [JsonProperty("baseHeight")]
    public int BaseHeight { get; set; } = 900;

    [JsonProperty("slotSize")]
    public int SlotSize { get; set; } = 180;

    [JsonProperty("fadeIn")]
    public float FadeIn { get; set; } = 0.35f;

    [JsonProperty("hold")]
    public float Hold { get; set; } = 3f;

    [JsonProperty("fadeOut")]
    public float FadeOut { get; set; } = 0.45f;

    public void Normalize()
    {
        BackgroundResource = string.IsNullOrWhiteSpace(BackgroundResource)
            ? "Mods/AuraToolsExp/ModResource/DPSCG/DPS-CG.png"
            : BackgroundResource.Trim();
        BaseWidth = Math.Max(1, BaseWidth);
        BaseHeight = Math.Max(1, BaseHeight);
        SlotSize = Math.Max(1, SlotSize);
        FadeIn = Math.Max(0f, Math.Min(5f, FadeIn));
        Hold = Math.Max(0.1f, Math.Min(30f, Hold));
        FadeOut = Math.Max(0f, Math.Min(5f, FadeOut));
    }
}

