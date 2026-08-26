using System;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public static class DamageMeterDisplayModes
{
    public const string Table = "Table";
    public const string Bars = "Bars";
}

public static class DamageMeterDisplayScopes
{
    public const string Fight = "Fight";
    public const string Adventure = "Adventure";
}

public static class DamageMeterTeamFilters
{
    public const string All = "All";
    public const string Friendly = "Friendly";
    public const string Enemy = "Enemy";
}

public sealed class DamageMeterSettings
{
    private const int DefaultMaxAvatarEncodePixels = 262144;
    private const int DefaultMaxAvatarPngBytes = 262144;
    private const int DefaultUiRefreshIntervalMs = 1000;
    private const int DefaultSubmitBatchIntervalMs = 250;
    private const int DefaultMaxEventsPerBatch = 24;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("displayMode")]
    public string DisplayMode { get; set; } = DamageMeterDisplayModes.Table;

    [JsonProperty("displayScope")]
    public string DisplayScope { get; set; } = DamageMeterDisplayScopes.Fight;

    [JsonProperty("teamFilter")]
    public string TeamFilter { get; set; } = DamageMeterTeamFilters.All;

    [JsonProperty("friendlyOnly")]
    private bool LegacyFriendlyOnly
    {
        set
        {
            if (value)
            {
                TeamFilter = DamageMeterTeamFilters.Friendly;
            }
        }
    }

    [JsonProperty("captureTeamAvatars")]
    public bool CaptureTeamAvatars { get; set; }

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

    private LegacyDamageSettlementCgSettings? legacySettlementCg;

    [JsonProperty("settlementCg")]
    private LegacyDamageSettlementCgSettings? LegacySettlementCg
    {
        set => legacySettlementCg = value;
    }

    public void Normalize()
    {
        DisplayMode = NormalizeChoice(DisplayMode, DamageMeterDisplayModes.Table, DamageMeterDisplayModes.Bars);
        DisplayScope = NormalizeChoice(DisplayScope, DamageMeterDisplayScopes.Fight, DamageMeterDisplayScopes.Adventure);
        TeamFilter = NormalizeChoice(
            TeamFilter,
            DamageMeterTeamFilters.All,
            DamageMeterTeamFilters.Friendly,
            DamageMeterTeamFilters.Enemy);
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
    }

    internal LegacyDamageSettlementCgSettings? TakeLegacySettlementCg()
    {
        var value = legacySettlementCg;
        legacySettlementCg = null;
        return value;
    }

    private static string NormalizeChoice(string? value, string fallback, params string[] choices)
    {
        foreach (var choice in choices)
        {
            if (string.Equals(value, choice, StringComparison.OrdinalIgnoreCase))
            {
                return choice;
            }
        }

        return fallback;
    }
}

internal sealed class LegacyDamageSettlementCgSettings
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
