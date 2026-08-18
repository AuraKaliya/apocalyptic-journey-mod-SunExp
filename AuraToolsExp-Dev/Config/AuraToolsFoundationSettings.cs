using System;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class PresetLibrarySettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("maximumPresets")]
    public int MaximumPresets { get; set; } = 64;

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        MaximumPresets = Math.Max(1, Math.Min(256, MaximumPresets));
    }
}

public sealed class ModHealthSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("scanOnOpen")]
    public bool ScanOnOpen { get; set; } = true;

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
    }
}

public sealed class LobbyStatusSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("showLocalHealthSummary")]
    public bool ShowLocalHealthSummary { get; set; } = true;

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
    }
}

public sealed class AdventureArchiveSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("maximumAdventures")]
    public int MaximumAdventures { get; set; } = 200;

    [JsonProperty("captureSnapshots")]
    public bool CaptureSnapshots { get; set; } = true;

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        MaximumAdventures = Math.Max(10, Math.Min(2000, MaximumAdventures));
    }
}
