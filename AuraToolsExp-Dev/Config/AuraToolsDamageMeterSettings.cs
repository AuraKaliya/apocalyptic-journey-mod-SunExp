using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

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
