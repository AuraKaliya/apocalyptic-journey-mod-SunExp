using System;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class MatchRecordSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("statistics")]
    public DamageMeterSettings Statistics { get; set; } = CreateDefaultStatistics();

    [JsonProperty("replay")]
    public MatchReplaySettings Replay { get; set; } = new();

    public void Normalize()
    {
        Statistics ??= CreateDefaultStatistics();
        Replay ??= new MatchReplaySettings();
        Statistics.Normalize();
        Replay.Normalize();
    }

    internal static MatchRecordSettings FromLegacy(DamageMeterSettings? legacy)
    {
        legacy ??= new DamageMeterSettings();
        legacy.Normalize();
        var enabled = legacy.Enabled;
        legacy.Enabled = true;
        return new MatchRecordSettings
        {
            Enabled = enabled,
            Statistics = legacy,
            Replay = new MatchReplaySettings()
        };
    }

    private static DamageMeterSettings CreateDefaultStatistics()
    {
        return new DamageMeterSettings { Enabled = true };
    }
}

public sealed class MatchReplaySettings
{
    public const int DefaultAutoRecordLimit = 20;
    public const int MaximumAutoRecordLimit = 500;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("autoRecordLimit")]
    public int AutoRecordLimit { get; set; } = DefaultAutoRecordLimit;

    [JsonProperty("chunkTargetBytes")]
    public int ChunkTargetBytes { get; set; } = 256 * 1024;

    public void Normalize()
    {
        AutoRecordLimit = Math.Max(1, Math.Min(
            MaximumAutoRecordLimit,
            AutoRecordLimit <= 0 ? DefaultAutoRecordLimit : AutoRecordLimit));
        ChunkTargetBytes = Math.Max(32 * 1024, Math.Min(
            1024 * 1024,
            ChunkTargetBytes <= 0 ? 256 * 1024 : ChunkTargetBytes));
    }
}
