using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsAudioSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonProperty("audioSystemVersion")]
    public string AudioSystemVersion { get; set; } = "2.0.0";

    [JsonProperty("battleBgm")]
    public AudioFeatureSettings BattleBgm { get; set; } = AudioFeatureSettings.CreateBattleBgmDefault();

    [JsonProperty("cardUse")]
    public AudioFeatureSettings CardUse { get; set; } = AudioFeatureSettings.CreateCardUseDefault();

    public void Normalize()
    {
        SchemaVersion = Math.Max(3, SchemaVersion);
        AudioSystemVersion = string.IsNullOrWhiteSpace(AudioSystemVersion) ? "2.0.0" : AudioSystemVersion.Trim();
        BattleBgm ??= AudioFeatureSettings.CreateBattleBgmDefault();
        CardUse ??= AudioFeatureSettings.CreateCardUseDefault();
        BattleBgm.Normalize("Audio/Global/all/BattleBgm/AuraToolsExp/default-battle-bgm/content.mp3", -1000, false);
        CardUse.Normalize("Audio/Global/all/CardUse/AuraToolsExp/default-card-use/content.mp3", -1000, false);
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
                RelativePath = "Audio/Global/all/BattleBgm/AuraToolsExp/default-battle-bgm/content.mp3",
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
                RelativePath = "Audio/Global/all/CardUse/AuraToolsExp/default-card-use/content.mp3",
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
