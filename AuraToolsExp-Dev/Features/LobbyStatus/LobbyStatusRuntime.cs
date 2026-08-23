using System;
using System.Collections.Generic;
using System.Linq;
using AuraOnline.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.ModHealth;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.LobbyStatus;

internal sealed class LobbyPlayerStatus
{
    internal string PlayerId { get; set; } = "";
    internal string PlayerName { get; set; } = "";
    internal string GameVersion { get; set; } = "";
    internal string RoleId { get; set; } = "";
    internal bool RoleSynced { get; set; }
    internal bool Ready { get; set; }
    internal bool IsHost { get; set; }
    internal bool IsLocal { get; set; }
    internal int ModDifferenceCount { get; set; }
    internal string HealthLevel { get; set; } = "未提供";
}

internal sealed class LobbyStatusSnapshot
{
    internal string LocalPlayerId { get; set; } = "";
    internal string HostPlayerId { get; set; } = "";
    internal List<LobbyPlayerStatus> Players { get; set; } = new();
}

internal static class LobbyStatusRuntime
{
    private static bool initialized;
    private static ModConfig? currentConfig;
    private static GameEntryUI? currentEntry;
    private static IDisposable? lobbySubscription;

    internal static LobbyStatusSnapshot Current { get; private set; } = new();

    internal static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        currentConfig = modConfig;
        lobbySubscription = AuraLobbySnapshotRuntime.Register(
            modConfig,
            AuraToolsIds.ModId,
            "LobbyStatus",
            UpdateLobby,
            AuraToolsLog.Debug,
            AuraToolsLog.Warn);
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.ShowCareer", _ => RefreshButton(), "LobbyStatus");
        AuraToolsConfigService.SubscribeModule(AuraToolModuleIds.LobbyStatus, OnConfigChanged);
        AuraToolsConfigService.SubscribeModule(AuraToolModuleIds.ModSync, RefreshButton);
        AuraToolsPreparationDock.Register(
            "lobby-status",
            "大厅状态",
            20,
            () => AuraToolsConfigService.LobbyStatus.Enabled
                  && currentEntry != null
                  && Current.Players.Count > 0,
            Show);
        ModHealthRuntime.Changed += RefreshLocalHealth;
        ApplyModuleActivation(AuraToolsConfigService.LobbyStatus.Enabled);
    }

    internal static void ApplyModuleActivation(bool enabled)
    {
        if (!initialized || currentConfig == null) return;
        if (!enabled)
        {
            lobbySubscription?.Dispose();
            lobbySubscription = null;
            Current = new LobbyStatusSnapshot();
            currentEntry = null;
            DestroyButton();
            return;
        }

        lobbySubscription ??= AuraLobbySnapshotRuntime.Register(
            currentConfig,
            AuraToolsIds.ModId,
            "LobbyStatus",
            UpdateLobby,
            AuraToolsLog.Debug,
            AuraToolsLog.Warn);
        UpdateLobby(AuraLobbySnapshotRuntime.Current);
    }

    private static void UpdateLobby(AuraLobbySnapshot snapshot)
    {
        currentEntry = snapshot.Entry;
        AuraToolsPreparationDock.Attach(currentEntry);
        if (snapshot.Players.Count == 0)
        {
            Current = new LobbyStatusSnapshot();
            DestroyButton();
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.LobbyStatus);
            RefreshButton();
            return;
        }
        var host = snapshot.Players.FirstOrDefault(player => player.IsHost);
        Current = new LobbyStatusSnapshot
        {
            LocalPlayerId = snapshot.LocalPlayerId,
            HostPlayerId = snapshot.HostPlayerId,
            Players = snapshot.Players.Select(player =>
            {
                return new LobbyPlayerStatus
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.PlayerName,
                    GameVersion = player.GameVersion,
                    RoleId = player.RoleId,
                    RoleSynced = player.RoleSynced,
                    Ready = player.Ready,
                    IsHost = player.IsHost,
                    IsLocal = player.IsLocal,
                    ModDifferenceCount = DifferenceCount(
                        host,
                        player),
                    HealthLevel = player.IsLocal
                                  && AuraToolsConfigService.LobbyStatus.ShowLocalHealthSummary
                        ? (ModHealthRuntime.Current.ScannedUtc.Length == 0 ? "尚未扫描" : ModHealthRuntime.Current.Level)
                        : "未提供"
                };
            }).ToList()
        };
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.LobbyStatus);
        RefreshButton();
    }

    private static void RefreshLocalHealth()
    {
        foreach (var player in Current.Players.Where(player => player.IsLocal))
        {
            player.HealthLevel = AuraToolsConfigService.LobbyStatus.ShowLocalHealthSummary
                ? (ModHealthRuntime.Current.ScannedUtc.Length == 0 ? "尚未扫描" : ModHealthRuntime.Current.Level)
                : "未提供";
        }
    }

    private static void OnConfigChanged()
    {
        RefreshLocalHealth();
        RefreshButton();
    }

    internal static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay("AuraTools.LobbyStatus", parent, "大厅状态面板");
        var toolbar = Row(window.transform, "Tabs", AuraToolsUi.ToolbarHeight);
        var statusTab = AuraToolsUi.AddButton(toolbar.transform, "玩家状态", () => { }, 96f);
        statusTab.interactable = false;
        var syncButton = AuraToolsUi.AddButton(toolbar.transform, "MOD 同步", AuraToolsModSyncRuntime.ShowOverlayFromLobbyStatus, 96f);
        syncButton.interactable = AuraToolsModSyncRuntime.CanShowOverlay;
        AuraToolsUi.AddText(toolbar.transform, "本机健康摘要", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 96f);
        AuraToolsUi.AddToggle(toolbar.transform, AuraToolsConfigService.LobbyStatus.ShowLocalHealthSummary, value =>
        {
            AuraToolsConfigService.LobbyStatus.ShowLocalHealthSummary = value;
            AuraToolsConfigService.SaveLobbyStatus();
        });
        AuraToolsUi.AddText(toolbar.transform,
            Current.Players.Count == 0 ? "当前不在可观察的联机大厅" : "玩家 " + Current.Players.Count,
            AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        var content = AuraToolsUi.CreateScroll(window.transform, "LobbyPlayers");
        foreach (var player in Current.Players)
        {
            var row = Row(content, "Player-" + player.PlayerId, 78f);
            AuraToolsUi.AddText(row.transform,
                (player.IsHost ? "房主 · " : "") + (player.IsLocal ? "本机 · " : "") + player.PlayerName
                + "\n" + (string.IsNullOrWhiteSpace(player.RoleId)
                    ? (player.RoleSynced ? "角色已同步" : "角色未同步")
                    : AuraToolsPlayerDisplay.RoleName(player.RoleId)),
                AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 70f, 1f);
            AuraToolsUi.AddText(row.transform,
                "游戏 " + (string.IsNullOrWhiteSpace(player.GameVersion) ? "未知" : player.GameVersion)
                + "\nMOD 差异 " + player.ModDifferenceCount,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
                player.ModDifferenceCount == 0 ? AuraToolsUi.SuccessText : AuraToolsUi.WarningText, 70f, 0f, 180f);
            AuraToolsUi.AddText(row.transform,
                (player.Ready ? "已准备" : "未准备") + "\n健康 " + player.HealthLevel,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleRight,
                player.Ready ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText, 70f, 0f, 150f);
        }
    }

    private static void RefreshButton()
    {
        AuraToolsPreparationDock.Attach(currentEntry);
        AuraToolsPreparationDock.Refresh();
    }

    private static int DifferenceCount(
        AuraLobbyPlayerState? host,
        AuraLobbyPlayerState player)
    {
        if (host == null) return 0;
        var keys = host.Mods.Select(mod => mod.MatchKey).Concat(player.Mods.Select(mod => mod.MatchKey)).Distinct(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var key in keys)
        {
            var left = host.Mods.FirstOrDefault(mod => string.Equals(mod.MatchKey, key, StringComparison.OrdinalIgnoreCase));
            var right = player.Mods.FirstOrDefault(mod => string.Equals(mod.MatchKey, key, StringComparison.OrdinalIgnoreCase));
            if (left == null || right == null || left.Enabled != right.Enabled
                || !string.Equals(left.ModVersion, right.ModVersion, StringComparison.OrdinalIgnoreCase)) count++;
        }
        return count;
    }

    private static GameObject Row(Transform parent, string name, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        AuraToolsUi.AddListRowImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static void DestroyButton()
    {
        AuraToolsPreparationDock.Refresh();
    }
}
