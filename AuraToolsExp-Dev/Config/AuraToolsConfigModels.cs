using System;
using System.Collections.Generic;
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

    [JsonProperty("logging")]
    public ModuleFileConfig Logging { get; set; } = new() { Enabled = true, ConfigFile = "LoggingSettings.json" };

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        Audio ??= new ModuleFileConfig { ConfigFile = "AudioSettings.json" };
        MatchExperience ??= new ModuleFileConfig { ConfigFile = "MatchExperienceSettings.json" };
        SkillCg ??= new ModuleFileConfig { ConfigFile = "SkillCgSettings.json" };
        Logging ??= new ModuleFileConfig { Enabled = true, ConfigFile = "LoggingSettings.json" };
        Audio.ConfigFile = string.IsNullOrWhiteSpace(Audio.ConfigFile) ? "AudioSettings.json" : Audio.ConfigFile.Trim();
        MatchExperience.ConfigFile = string.IsNullOrWhiteSpace(MatchExperience.ConfigFile) ? "MatchExperienceSettings.json" : MatchExperience.ConfigFile.Trim();
        SkillCg.ConfigFile = string.IsNullOrWhiteSpace(SkillCg.ConfigFile) ? "SkillCgSettings.json" : SkillCg.ConfigFile.Trim();
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

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        StarterDeck ??= new StarterDeckSettings();
        SafeBox ??= new SafeBoxSettings();
        StarterDeck.Normalize();
    }
}

public sealed class StarterDeckSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("deckSize")]
    public int DeckSize { get; set; } = 11;

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    public void Normalize()
    {
        DeckSize = Math.Max(1, DeckSize);
        CardIds ??= new List<string>();
        CardIds.RemoveAll(string.IsNullOrWhiteSpace);
    }
}

public sealed class SafeBoxSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }
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
