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
