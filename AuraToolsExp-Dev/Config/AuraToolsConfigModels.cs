using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsRootConfig
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("audio")]
    public ModuleFileConfig Audio { get; set; } = new() { ConfigFile = "AudioSettings.json" };

    [JsonProperty("matchExperience")]
    public ModuleFileConfig MatchExperience { get; set; } = new() { ConfigFile = "MatchExperienceSettings.json" };

    [JsonProperty("skillCg")]
    public ModuleFileConfig SkillCg { get; set; } = new() { ConfigFile = "SkillCgSettings.json" };

    [JsonProperty("skin")]
    public ModuleFileConfig Skin { get; set; } = new() { ConfigFile = "SkinSettings.json" };

    [JsonProperty("logging")]
    public ModuleFileConfig Logging { get; set; } = new() { Enabled = true, ConfigFile = "LoggingSettings.json" };

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        Audio ??= new ModuleFileConfig { ConfigFile = "AudioSettings.json" };
        MatchExperience ??= new ModuleFileConfig { ConfigFile = "MatchExperienceSettings.json" };
        SkillCg ??= new ModuleFileConfig { ConfigFile = "SkillCgSettings.json" };
        Skin ??= new ModuleFileConfig { ConfigFile = "SkinSettings.json" };
        Logging ??= new ModuleFileConfig { Enabled = true, ConfigFile = "LoggingSettings.json" };
        Audio.ConfigFile = string.IsNullOrWhiteSpace(Audio.ConfigFile) ? "AudioSettings.json" : Audio.ConfigFile.Trim();
        MatchExperience.ConfigFile = string.IsNullOrWhiteSpace(MatchExperience.ConfigFile) ? "MatchExperienceSettings.json" : MatchExperience.ConfigFile.Trim();
        SkillCg.ConfigFile = string.IsNullOrWhiteSpace(SkillCg.ConfigFile) ? "SkillCgSettings.json" : SkillCg.ConfigFile.Trim();
        Skin.ConfigFile = string.IsNullOrWhiteSpace(Skin.ConfigFile) ? "SkinSettings.json" : Skin.ConfigFile.Trim();
        Logging.ConfigFile = string.IsNullOrWhiteSpace(Logging.ConfigFile) ? "LoggingSettings.json" : Logging.ConfigFile.Trim();
    }
}

public sealed class ModuleFileConfig
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("configFile")]
    public string ConfigFile { get; set; } = "";
}

public sealed class AuraToolsAudioSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonProperty("audioSystemVersion")]
    public string AudioSystemVersion { get; set; } = "2.0.0";

    [JsonProperty("battleBgm")]
    public AudioFeatureSettings BattleBgm { get; set; } = AudioFeatureSettings.CreateBattleBgmDefault();

    [JsonProperty("cardUse")]
    public AudioFeatureSettings CardUse { get; set; } = AudioFeatureSettings.CreateCardUseDefault();

    public void Normalize()
    {
        SchemaVersion = Math.Max(2, SchemaVersion);
        AudioSystemVersion = string.IsNullOrWhiteSpace(AudioSystemVersion) ? "2.0.0" : AudioSystemVersion.Trim();
        BattleBgm ??= AudioFeatureSettings.CreateBattleBgmDefault();
        CardUse ??= AudioFeatureSettings.CreateCardUseDefault();
        BattleBgm.Normalize("Audio/AuraToolsExp/Common/battle_bgm.mp3", -1000, false);
        CardUse.Normalize("Audio/AuraToolsExp/Common/card_use.mp3", -1000, false);
        MigrateBundledPath(BattleBgm.Common, "Audio/Common/battle_bgm.mp3", "Audio/AuraToolsExp/Common/battle_bgm.mp3");
        MigrateBundledPath(CardUse.Common, "Audio/Common/card_use.mp3", "Audio/AuraToolsExp/Common/card_use.mp3");
    }

    private static void MigrateBundledPath(AudioCommonSettings settings, string legacy, string current)
    {
        if (string.Equals(settings.RelativePath, legacy, StringComparison.OrdinalIgnoreCase))
        {
            settings.RelativePath = current;
        }
    }
}

public sealed class AudioFeatureSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("mode")]
    public string Mode { get; set; } = AudioModes.Common;

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; }

    [JsonProperty("common")]
    public AudioCommonSettings Common { get; set; } = new();

    [JsonProperty("roles")]
    public Dictionary<string, AudioRoleSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static AudioFeatureSettings CreateBattleBgmDefault()
    {
        return new AudioFeatureSettings
        {
            Enabled = true,
            Mode = AudioModes.Common,
            SyncRemote = false,
            Common = new AudioCommonSettings
            {
                RelativePath = "Audio/AuraToolsExp/Common/battle_bgm.mp3",
                Priority = -1000,
                HardClaim = false,
                SilenceWhenLoading = false,
                FallbackToOriginalWhenFailed = true
            }
        };
    }

    public static AudioFeatureSettings CreateCardUseDefault()
    {
        return new AudioFeatureSettings
        {
            Enabled = true,
            Mode = AudioModes.Common,
            SyncRemote = false,
            Common = new AudioCommonSettings
            {
                RelativePath = "Audio/AuraToolsExp/Common/card_use.mp3",
                Priority = -1000,
                HardClaim = false,
                GainDb = 6f
            }
        };
    }

    public void Normalize(string defaultRelativePath, int defaultPriority, bool defaultHardClaim)
    {
        Mode = AudioModes.Normalize(Mode);
        Common ??= new AudioCommonSettings();
        Common.Normalize(defaultRelativePath, defaultPriority, defaultHardClaim);
        Roles ??= new Dictionary<string, AudioRoleSettings>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in Roles)
        {
            pair.Value?.Normalize(pair.Key, defaultPriority + 1100);
        }
    }
}

public static class AudioModes
{
    public const string Common = "Common";
    public const string Advanced = "Advanced";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Advanced, StringComparison.OrdinalIgnoreCase) ? Advanced : Common;
    }
}

public sealed class AudioCommonSettings
{
    [JsonProperty("relativePath")]
    public string RelativePath { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; } = -1000;

    [JsonProperty("hardClaim")]
    public bool HardClaim { get; set; }

    [JsonProperty("silenceWhenLoading")]
    public bool SilenceWhenLoading { get; set; }

    [JsonProperty("fallbackToOriginalWhenFailed")]
    public bool FallbackToOriginalWhenFailed { get; set; } = true;

    [JsonProperty("gainDb")]
    public float GainDb { get; set; }

    public void Normalize(string defaultRelativePath, int defaultPriority, bool defaultHardClaim)
    {
        RelativePath = string.IsNullOrWhiteSpace(RelativePath) ? defaultRelativePath : RelativePath.Trim();
        Priority = Priority == 0 && defaultPriority != 0 ? defaultPriority : Priority;
        HardClaim = HardClaim || defaultHardClaim;
        FallbackToOriginalWhenFailed = true;
    }
}

public sealed class AudioRoleSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("relativePath")]
    public string RelativePath { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; } = 100;

    [JsonProperty("hardClaim")]
    public bool HardClaim { get; set; }

    [JsonProperty("gainDb")]
    public float GainDb { get; set; } = 6f;

    public void Normalize(string fallbackRoleId, int defaultPriority)
    {
        RoleId = string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId.Trim();
        DisplayName = DisplayName?.Trim() ?? "";
        RelativePath = RelativePath?.Trim() ?? "";
        if (Priority == 0)
        {
            Priority = defaultPriority;
        }
    }
}

public sealed class AuraToolsMatchExperienceSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 6;

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

    public void Normalize()
    {
        var loadedSchemaVersion = SchemaVersion;
        SchemaVersion = Math.Max(6, SchemaVersion);
        StarterDeck ??= new StarterDeckSettings();
        SafeBox ??= new SafeBoxSettings();
        ModSync ??= new ModSyncSettings();
        Feast ??= new FeastSettings();
        if (loadedSchemaVersion < 6)
        {
            Feast.Enabled = true;
        }

        DamageMeter ??= new DamageMeterSettings();
        StarterDeck.Normalize();
        Feast.Normalize();
        DamageMeter.Normalize();
    }
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

public sealed class AuraToolsSkillCgSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("cardUseCg")]
    public AuraToolsCardUseCgSettings CardUseCg { get; set; } = new();

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
        SchemaVersion = Math.Max(3, SchemaVersion);
        CardUseCg ??= new AuraToolsCardUseCgSettings();
        CardUseCg.Normalize();
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
}

public sealed class AuraToolsCardUseCgSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    public void Normalize()
    {
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
        Image = Image?.Trim() ?? "";
        if (Image.StartsWith("CG/Roles/", StringComparison.OrdinalIgnoreCase))
        {
            Image = "CG/AuraToolsExp/Roles/" + Image.Substring("CG/Roles/".Length);
        }
        ProviderId = ProviderId?.Trim() ?? "";
        Presentation ??= SkillCgPresentationSettings.CreateInherited();
        EffectivePresentation = Presentation.ResolveRule(fallbackPresentation, FadeIn, Hold, FadeOut);
        FadeIn = EffectivePresentation.FadeIn;
        Hold = EffectivePresentation.Hold;
        FadeOut = EffectivePresentation.FadeOut;
    }
}

public sealed class AuraToolsSkinSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("autoInstallBundledSkins")]
    public bool AutoInstallBundledSkins { get; set; } = true;

    [JsonProperty("showEntrySkinButton")]
    public bool ShowEntrySkinButton { get; set; } = true;

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        AutoInstallBundledSkins = true;
    }
}

public sealed class AuraToolsLoggingSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 4;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("performanceDiagnostics")]
    public bool PerformanceDiagnostics { get; set; }

    [JsonProperty("fileNamePattern")]
    public string FileNamePattern { get; set; } = "AuraTools-{date}.log";

    [JsonProperty("minimumLevel")]
    public string MinimumLevel { get; set; } = "Info";

    [JsonProperty("mirrorUnityLog")]
    public bool MirrorUnityLog { get; set; }

    [JsonProperty("mirrorCommandsLog")]
    public bool MirrorCommandsLog { get; set; }

    [JsonProperty("enabledSources")]
    public List<string> EnabledSources { get; set; } = new() { "AuraTools" };

    [JsonProperty("unityLogTypes")]
    public List<string> UnityLogTypes { get; set; } = new() { "Warning", "Error", "Exception", "Assert" };

    [JsonProperty("includedCommandTags")]
    public List<string> IncludedCommandTags { get; set; } = new();

    [JsonProperty("excludedCommandTags")]
    public List<string> ExcludedCommandTags { get; set; } = new();

    [JsonProperty("stackTraceMode")]
    public string StackTraceMode { get; set; } = "ErrorsOnly";

    [JsonProperty("maxQueueLength")]
    public int MaxQueueLength { get; set; } = 1024;

    [JsonProperty("flushIntervalMs")]
    public int FlushIntervalMs { get; set; } = 1000;

    [JsonProperty("maxRetainedLogFiles")]
    public int MaxRetainedLogFiles { get; set; } = 10;

    public void Normalize()
    {
        var loadedSchemaVersion = SchemaVersion;
        var shouldMigrateHighVolumeDefaults = loadedSchemaVersion < 2 && LooksLikeLegacyHighVolumeDefaults();
        var shouldMigrateWarningOnlyDefaults = loadedSchemaVersion < 3 && LooksLikeWarningOnlyDefaults();
        SchemaVersion = Math.Max(4, SchemaVersion);
        if (shouldMigrateHighVolumeDefaults || shouldMigrateWarningOnlyDefaults)
        {
            MinimumLevel = LoggingLevelNames.Info;
            MirrorUnityLog = false;
            MirrorCommandsLog = false;
            EnabledSources = new List<string> { "AuraTools" };
            UnityLogTypes = new List<string> { "Warning", "Error", "Exception", "Assert" };
            StackTraceMode = LoggingStackTraceModes.ErrorsOnly;
            MaxQueueLength = Math.Min(MaxQueueLength <= 0 ? 1024 : MaxQueueLength, 1024);
        }

        FileNamePattern = string.IsNullOrWhiteSpace(FileNamePattern) ? "AuraTools-{date}.log" : FileNamePattern.Trim();
        MinimumLevel = LoggingLevelNames.Normalize(MinimumLevel);
        EnabledSources = NormalizeList(EnabledSources, new[] { "AuraTools" });
        UnityLogTypes = NormalizeList(UnityLogTypes, new[] { "Warning", "Error", "Exception", "Assert" });
        IncludedCommandTags = NormalizeList(IncludedCommandTags, Array.Empty<string>());
        ExcludedCommandTags = NormalizeList(ExcludedCommandTags, Array.Empty<string>());
        StackTraceMode = LoggingStackTraceModes.Normalize(StackTraceMode);
        MaxQueueLength = Math.Max(128, Math.Min(65536, MaxQueueLength));
        FlushIntervalMs = Math.Max(100, Math.Min(10000, FlushIntervalMs));
        MaxRetainedLogFiles = Math.Max(1, Math.Min(50, MaxRetainedLogFiles));
    }

    private bool LooksLikeLegacyHighVolumeDefaults()
    {
        return MirrorUnityLog
               || MirrorCommandsLog
               || ContainsValue(EnabledSources, "Unity")
               || ContainsValue(EnabledSources, "Command")
               || ContainsValue(UnityLogTypes, "Log")
               || string.Equals(StackTraceMode, LoggingStackTraceModes.All, StringComparison.OrdinalIgnoreCase)
               || MaxQueueLength >= 4096;
    }

    private bool LooksLikeWarningOnlyDefaults()
    {
        return string.Equals(MinimumLevel, LoggingLevelNames.Warning, StringComparison.OrdinalIgnoreCase)
               && !MirrorUnityLog
               && !MirrorCommandsLog
               && ContainsOnlyValue(EnabledSources, "AuraTools")
               && !ContainsValue(UnityLogTypes, "Log")
               && string.Equals(StackTraceMode, LoggingStackTraceModes.ErrorsOnly, StringComparison.OrdinalIgnoreCase)
               && MaxQueueLength <= 1024;
    }

    private static bool ContainsValue(IEnumerable<string>? values, string expected)
    {
        return values != null && values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsOnlyValue(IEnumerable<string>? values, string expected)
    {
        if (values == null)
        {
            return false;
        }

        var normalized = values
            .Select(value => value?.Trim() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count == 1 && string.Equals(normalized[0], expected, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeList(IEnumerable<string>? values, IEnumerable<string> fallback)
    {
        var list = new List<string>();
        foreach (var value in values ?? fallback)
        {
            var text = value?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(text)
                && !list.Any(existing => string.Equals(existing, text, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(text);
            }
        }

        if (list.Count == 0)
        {
            foreach (var value in fallback)
            {
                var text = value?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add(text);
                }
            }
        }

        return list;
    }
}

public static class LoggingLevelNames
{
    public const string Debug = "Debug";
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";

    public static string Normalize(string? value)
    {
        var text = value?.Trim() ?? "";
        if (string.Equals(text, Debug, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Log", StringComparison.OrdinalIgnoreCase))
        {
            return Debug;
        }

        if (string.Equals(text, Warning, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Warn", StringComparison.OrdinalIgnoreCase))
        {
            return Warning;
        }

        if (string.Equals(text, Error, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Exception", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Assert", StringComparison.OrdinalIgnoreCase))
        {
            return Error;
        }

        return Info;
    }
}

public static class LoggingStackTraceModes
{
    public const string Off = "Off";
    public const string ErrorsOnly = "ErrorsOnly";
    public const string All = "All";

    public static string Normalize(string? value)
    {
        var text = value?.Trim() ?? "";
        if (string.Equals(text, Off, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
        {
            return Off;
        }

        if (string.Equals(text, All, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Always", StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        return ErrorsOnly;
    }
}
