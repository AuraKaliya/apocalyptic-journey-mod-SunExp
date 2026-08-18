using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    private const string ButtonName = "AuraToolsLobbyStatusButton";
    private static bool initialized;
    private static GameEntryUI? currentEntry;
    private static GameObject? buttonRoot;
    private static readonly Dictionary<string, string> RolesByPlayer = new(StringComparer.OrdinalIgnoreCase);

    internal static LobbyStatusSnapshot Current { get; private set; } = new();

    internal static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.UpdateLobby", UpdateLobby, "LobbyStatus");
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.ChangeRole", CaptureRole, "LobbyStatus");
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.SetReady", _ => RefreshReady(), "LobbyStatus");
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.Init", ResetLobby, "LobbyStatus");
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.Outlobby", _ => ClearLobby(), "LobbyStatus");
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.ReturnHouse", _ => ClearLobby(), "LobbyStatus");
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.ShowCareer", _ => RefreshButton(), "LobbyStatus");
        AuraToolsHookRegistry.Before(modConfig, "GameEntryUI.StartGame", _ => ClearLobby(), "LobbyStatus");
        AuraToolsConfigService.SubscribeModule(AuraToolModuleIds.LobbyStatus, OnConfigChanged);
        AuraToolsConfigService.SubscribeModule(AuraToolModuleIds.ModSync, RefreshButton);
        ModHealthRuntime.Changed += RefreshLocalHealth;
    }

    private static void UpdateLobby(ModHookContext context)
    {
        currentEntry = context.Target as GameEntryUI ?? currentEntry;
        var players = ExtractPlayers(context.Arguments);
        if (players.Count == 0)
        {
            Current = new LobbyStatusSnapshot();
            RefreshButton();
            return;
        }
        var localId = PlayerManager.Instance?.PlayerId ?? "";
        var modState = AuraChatModSyncSnapshot.BuildState(players, AuraToolsIds.ModId, localId);
        var ready = ReadReadyMap(currentEntry);
        var host = modState.Players.FirstOrDefault();
        Current = new LobbyStatusSnapshot
        {
            LocalPlayerId = localId,
            HostPlayerId = host?.PlayerId ?? "",
            Players = modState.Players.Select((player, index) =>
            {
                var raw = players.FirstOrDefault(value => string.Equals(Read(value, "Id"), player.PlayerId, StringComparison.OrdinalIgnoreCase));
                return new LobbyPlayerStatus
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.PlayerName,
                    GameVersion = Read(raw, "Version"),
                    RoleId = RolesByPlayer.TryGetValue(player.PlayerId, out var role) ? role : "",
                    RoleSynced = ReadBool(raw, "IsSyncedRole"),
                    Ready = ready.TryGetValue(player.PlayerId, out var isReady) && isReady,
                    IsHost = index == 0,
                    IsLocal = string.Equals(player.PlayerId, localId, StringComparison.OrdinalIgnoreCase),
                    ModDifferenceCount = DifferenceCount(host, player),
                    HealthLevel = string.Equals(player.PlayerId, localId, StringComparison.OrdinalIgnoreCase)
                                  && AuraToolsConfigService.LobbyStatus.ShowLocalHealthSummary
                        ? (ModHealthRuntime.Current.ScannedUtc.Length == 0 ? "尚未扫描" : ModHealthRuntime.Current.Level)
                        : "未提供"
                };
            }).ToList()
        };
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.LobbyStatus);
        RefreshButton();
    }

    private static void ResetLobby(ModHookContext context)
    {
        ClearLobby();
        currentEntry = context.Target as GameEntryUI;
        RefreshButton();
    }

    private static void ClearLobby()
    {
        RolesByPlayer.Clear();
        Current = new LobbyStatusSnapshot();
        currentEntry = null;
        DestroyButton();
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.LobbyStatus);
    }

    private static void CaptureRole(ModHookContext context)
    {
        var data = context.Arguments?.OfType<DataConfig>().FirstOrDefault();
        var playerId = context.Arguments?.OfType<string>().FirstOrDefault() ?? "";
        if (data?.data != null && data.data.TryGetValue("Id", out var roleId) && !string.IsNullOrWhiteSpace(playerId))
        {
            RolesByPlayer[playerId] = roleId ?? "";
            var player = Current.Players.FirstOrDefault(value =>
                string.Equals(value.PlayerId, playerId, StringComparison.OrdinalIgnoreCase));
            if (player != null) player.RoleId = roleId ?? "";
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.LobbyStatus);
        }
    }

    private static void RefreshReady()
    {
        var ready = ReadReadyMap(currentEntry);
        foreach (var player in Current.Players)
        {
            player.Ready = ready.TryGetValue(player.PlayerId, out var value) && value;
        }
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.LobbyStatus);
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
                    : RoleCatalog.GetDisplayName(player.RoleId)),
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
        if (!AuraToolsConfigService.LobbyStatus.Enabled || currentEntry == null || Current.Players.Count == 0)
        {
            DestroyButton();
            return;
        }
        var ready = currentEntry.transform.Find("ForeBack/Button");
        if (ready == null || ready.parent == null) return;
        if (buttonRoot == null || buttonRoot.transform.parent != ready.parent)
        {
            DestroyButton();
            buttonRoot = AuraToolsUi.CreateRect(ButtonName, ready.parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(132f, 34f));
            AuraToolsUi.AddButtonImage(buttonRoot, new Color(0.16f, 0.13f, 0.22f, 0.98f));
            var button = buttonRoot.AddComponent<Button>();
            AuraUiButtonFeedback.Apply(button, buttonRoot.GetComponent<Image>(), AuraToolsUi.Accent);
            button.onClick.AddListener(() => Show(ready.parent));
            AuraToolsUi.AddFillText(buttonRoot.transform, "大厅状态", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text);
        }
        if (ready is RectTransform readyRect && buttonRoot.transform is RectTransform rect)
        {
            rect.anchorMin = readyRect.anchorMin;
            rect.anchorMax = readyRect.anchorMax;
            rect.pivot = readyRect.pivot;
            var offset = Mathf.Max(Mathf.Abs(readyRect.sizeDelta.y), 34f) + 6f;
            var row = AuraToolsConfigService.MatchExperience.ModSync.Enabled ? 2f : 1f;
            rect.anchoredPosition = readyRect.anchoredPosition + new Vector2(0f, -offset * row);
        }
        buttonRoot.SetActive(true);
    }

    private static List<object> ExtractPlayers(IReadOnlyList<object>? arguments)
    {
        foreach (var argument in arguments ?? Array.Empty<object>())
        {
            if (argument is string || argument is not IEnumerable enumerable) continue;
            var values = enumerable.Cast<object>().Where(value => value != null).ToList();
            if (values.Any(value => !string.IsNullOrWhiteSpace(Read(value, "Id")))) return values;
        }
        return new List<object>();
    }

    private static Dictionary<string, bool> ReadReadyMap(GameEntryUI? entry)
    {
        try
        {
            return typeof(GameEntryUI).GetField("Ready", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(entry)
                       as Dictionary<string, bool>
                   ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); }
    }

    private static int DifferenceCount(AuraChatModPlayerSnapshot? host, AuraChatModPlayerSnapshot player)
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

    private static string Read(object? target, string name)
    {
        if (target == null) return "";
        try
        {
            var type = target.GetType();
            return Convert.ToString(type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target)
                                    ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target))?.Trim() ?? "";
        }
        catch { return ""; }
    }

    private static bool ReadBool(object? target, string name)
    {
        return bool.TryParse(Read(target, name), out var value) && value;
    }

    private static GameObject Row(Transform parent, string name, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        AuraToolsUi.AddPanelImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }

    private static void DestroyButton()
    {
        if (buttonRoot == null) return;
        UiRaycastSafetyShared.UiRaycastSafeDestroyRuntime.DisableAndHide(buttonRoot, "LobbyStatus");
        UnityEngine.Object.Destroy(buttonRoot);
        buttonRoot = null;
    }
}
