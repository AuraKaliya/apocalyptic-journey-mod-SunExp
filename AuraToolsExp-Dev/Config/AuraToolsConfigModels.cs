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
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("audioSystemVersion")]
    public string AudioSystemVersion { get; set; } = "2.0.0";

    [JsonProperty("battleBgm")]
    public AudioFeatureSettings BattleBgm { get; set; } = AudioFeatureSettings.CreateBattleBgmDefault();

    [JsonProperty("cardUse")]
    public AudioFeatureSettings CardUse { get; set; } = AudioFeatureSettings.CreateCardUseDefault();

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        AudioSystemVersion = string.IsNullOrWhiteSpace(AudioSystemVersion) ? "2.0.0" : AudioSystemVersion.Trim();
        BattleBgm ??= AudioFeatureSettings.CreateBattleBgmDefault();
        CardUse ??= AudioFeatureSettings.CreateCardUseDefault();
        BattleBgm.Normalize("Audio/Common/battle_bgm.mp3", -1000, false);
        CardUse.Normalize("Audio/Common/card_use.mp3", -1000, false);
    }
}

public sealed class AudioFeatureSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("mode")]
    public string Mode { get; set; } = AudioModes.Common;

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
            Common = new AudioCommonSettings
            {
                RelativePath = "Audio/Common/battle_bgm.mp3",
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
            Common = new AudioCommonSettings
            {
                RelativePath = "Audio/Common/card_use.mp3",
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
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("starterDeck")]
    public StarterDeckSettings StarterDeck { get; set; } = new();

    [JsonProperty("safeBox")]
    public SafeBoxSettings SafeBox { get; set; } = new();

    [JsonProperty("damageMeter")]
    public DamageMeterSettings DamageMeter { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        StarterDeck ??= new StarterDeckSettings();
        SafeBox ??= new SafeBoxSettings();
        DamageMeter ??= new DamageMeterSettings();
        StarterDeck.Normalize();
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

public sealed class DamageMeterSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("hotkey")]
    public string Hotkey { get; set; } = "F8";

    [JsonProperty("showPanelByDefault")]
    public bool ShowPanelByDefault { get; set; } = true;

    [JsonProperty("friendlyOnly")]
    public bool FriendlyOnly { get; set; }

    [JsonProperty("countShieldLoss")]
    public bool CountShieldLoss { get; set; } = true;

    [JsonProperty("maxRows")]
    public int MaxRows { get; set; } = 6;

    public void Normalize()
    {
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "F8" : Hotkey.Trim();
        MaxRows = Math.Max(1, Math.Min(12, MaxRows));
    }
}

public sealed class AuraToolsSkillCgSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    [JsonProperty("maxQueueLength")]
    public int MaxQueueLength { get; set; } = 8;

    [JsonProperty("maxRequestAgeSeconds")]
    public float MaxRequestAgeSeconds { get; set; } = 6f;

    [JsonProperty("duplicateWindowSeconds")]
    public float DuplicateWindowSeconds { get; set; } = 0.2f;

    [JsonProperty("roles")]
    public Dictionary<string, SkillCgRoleSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        MaxQueueLength = Math.Max(1, Math.Min(30, MaxQueueLength));
        MaxRequestAgeSeconds = Math.Max(0.5f, Math.Min(30f, MaxRequestAgeSeconds));
        DuplicateWindowSeconds = Math.Max(0.02f, Math.Min(2f, DuplicateWindowSeconds));
        Roles ??= new Dictionary<string, SkillCgRoleSettings>(StringComparer.OrdinalIgnoreCase);
        var normalizedRoles = new Dictionary<string, SkillCgRoleSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Roles)
        {
            var role = pair.Value ?? new SkillCgRoleSettings();
            role.Normalize(pair.Key);
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

public sealed class SkillCgRoleSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("rules")]
    public List<SkillCgRuleSettings> Rules { get; set; } = new();

    public void Normalize(string fallbackRoleId)
    {
        RoleId = RoleCatalog.NormalizeRoleId(string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId);
        DisplayName = DisplayName?.Trim() ?? "";
        Rules ??= new List<SkillCgRuleSettings>();
        for (var i = Rules.Count - 1; i >= 0; i--)
        {
            var rule = Rules[i];
            if (rule == null || !rule.IsActiveSkillRule())
            {
                Rules.RemoveAt(i);
                continue;
            }

            rule.Normalize();
        }
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
    public float FadeIn { get; set; } = 0.35f;

    [JsonProperty("hold")]
    public float Hold { get; set; } = 1.0f;

    [JsonProperty("fadeOut")]
    public float FadeOut { get; set; } = 0.45f;

    public bool IsActiveSkillRule()
    {
        return !string.Equals(LegacyTriggerType, LegacyTriggerPassiveSkill, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(LegacyTriggerType, LegacyTriggerPassiveEvent, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldSerializeLegacyTriggerType()
    {
        return false;
    }

    public void Normalize()
    {
        LegacyTriggerType = TriggerActiveSkill;
        CardId = string.IsNullOrWhiteSpace(CardId) ? "*" : CardId.Trim();
        Action = string.IsNullOrWhiteSpace(Action) ? "*" : Action.Trim();
        Image = Image?.Trim() ?? "";
        ProviderId = ProviderId?.Trim() ?? "";
        FadeIn = Math.Max(0f, FadeIn);
        Hold = Math.Max(0f, Hold);
        FadeOut = Math.Max(0f, FadeOut);
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
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("fileNamePattern")]
    public string FileNamePattern { get; set; } = "AuraTools-{date}.log";

    [JsonProperty("minimumLevel")]
    public string MinimumLevel { get; set; } = "Info";

    [JsonProperty("mirrorUnityLog")]
    public bool MirrorUnityLog { get; set; } = true;

    [JsonProperty("mirrorCommandsLog")]
    public bool MirrorCommandsLog { get; set; } = true;

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        FileNamePattern = string.IsNullOrWhiteSpace(FileNamePattern) ? "AuraTools-{date}.log" : FileNamePattern.Trim();
        MinimumLevel = string.IsNullOrWhiteSpace(MinimumLevel) ? "Info" : MinimumLevel.Trim();
    }
}
