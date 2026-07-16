using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AuraOnline.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.ModSync;

public static class AuraToolsModSyncRuntime
{
    private const string ButtonName = "AuraToolsModConfigButton";
    private const string OverlayName = "AuraToolsModConfigOverlay";
    private const float ButtonWidth = 132f;
    private const float ButtonHeight = 34f;
    private const float ButtonGap = 6f;
    private const float OverlayWidth = 880f;
    private const float OverlayHeight = 560f;
    private const float ModColumnWidth = 190f;
    private const float PlayerColumnWidth = 132f;
    private const int MaxManifestTransferBytes = 512 * 1024;
    private const int MaxManifestChunks = 64;
    private const int MaxManifestActiveTransfers = 8;
    private static readonly TimeSpan ManifestChunkBufferTtl = TimeSpan.FromSeconds(45);
    private static readonly AuraOnlineHostModSyncSession SyncSession = new(AuraToolsIds.ModId, AuraToolsLog.Info, AuraToolsLog.Warn);
    private static readonly Dictionary<string, ManifestChunkBuffer> ManifestChunkBuffers = new(StringComparer.Ordinal);

    private static bool initialized;
    private static GameEntryUI? currentEntry;
    private static GameObject? buttonRoot;
    private static GameObject? activeOverlay;
    private static Transform? activeOverlayContent;
    private static AuraChatModSyncState? currentState;
    private static string lastDiagnosticsKey = "";
    private static bool manifestRequestPending;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        SyncSession.Changed += RefreshOverlay;
        AuraToolsConfigService.Changed += OnConfigChanged;
        RegisterAfter(modConfig, "GameEntryUI.UpdateLobby", UpdateLobby);
        RegisterAfter(modConfig, "GameEntryUI.Init", _ => RefreshButton());
        RegisterAfter(modConfig, "GameEntryUI.ShowCareer", _ => RefreshButton());
        RegisterBefore(modConfig, "GameEntryUI.StartGame", _ => DestroyUi("GameEntryUI.StartGame"));
        RegisterBefore(modConfig, "UIManager.CloseUI", HideOnUiManagerClose);
        RegisterBefore(modConfig, "UIBase.Close", context => HideOnUiBaseClose(context, "UIBase.Close"));
        RegisterBefore(modConfig, "UIBase.OnDestroy", context => HideOnUiBaseClose(context, "UIBase.OnDestroy"));
    }

    private static void UpdateLobby(ModHookContext context)
    {
        try
        {
            currentEntry = context.Target as GameEntryUI;
            var players = GetPlayersFromContext(context);
            if (!IsEnabled() || players.Count == 0 || PlayerManager.Instance == null)
            {
                DestroyUi("UpdateLobby:not-available");
                return;
            }

            var localPlayerId = ResolveLocalPlayerId(players);
            currentState = AuraChatModSyncSnapshot.BuildState(players, AuraToolsIds.ModId, localPlayerId);
            LogStateDiagnostics(currentState);
            EnsureButton();
            RefreshOverlay();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[ModSync] lobby update failed: " + ex.Message);
        }
    }

    private static void OnConfigChanged()
    {
        if (!IsEnabled())
        {
            DestroyUi("config-disabled");
            return;
        }

        RefreshButton();
    }

    private static bool IsEnabled()
    {
        return AuraToolsConfigService.Root.MatchExperience.Enabled
               && AuraToolsConfigService.MatchExperience.ModSync.Enabled;
    }

    private static void RefreshButton()
    {
        if (!IsEnabled() || currentState == null || currentEntry == null)
        {
            DestroyButton("RefreshButton:not-available");
            return;
        }

        EnsureButton();
    }

    private static void EnsureButton()
    {
        if (!IsEnabled() || currentEntry == null || currentEntry.transform == null)
        {
            DestroyButton("EnsureButton:not-available");
            return;
        }

        var readyButton = currentEntry.transform.Find("ForeBack/Button");
        if (readyButton == null || readyButton.parent == null)
        {
            DestroyButton("EnsureButton:no-ready-button");
            return;
        }

        if (buttonRoot == null || buttonRoot.transform.parent != readyButton.parent)
        {
            DestroyButton("EnsureButton:reparent");
            buttonRoot = AuraToolsUi.CreateRect(
                ButtonName,
                readyButton.parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(ButtonWidth, ButtonHeight));
            AuraToolsUi.AddButtonImage(buttonRoot, new Color(0.16f, 0.13f, 0.22f, 0.98f));
            var button = buttonRoot.AddComponent<Button>();
            AuraUiButtonFeedback.Apply(button, buttonRoot.GetComponent<Image>(), AuraToolsUi.Accent);
            button.onClick.AddListener(ShowOverlay);
            AuraToolsUi.AddFillText(buttonRoot.transform, "MOD配置", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text);
        }

        PositionButton(readyButton);
        buttonRoot.SetActive(true);
        buttonRoot.transform.SetAsLastSibling();
    }

    private static void PositionButton(Transform readyButton)
    {
        if (buttonRoot == null || readyButton is not RectTransform readyRect || buttonRoot.transform is not RectTransform buttonRect)
        {
            return;
        }

        buttonRect.anchorMin = readyRect.anchorMin;
        buttonRect.anchorMax = readyRect.anchorMax;
        buttonRect.pivot = readyRect.pivot;
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        var yOffset = -(Mathf.Max(Mathf.Abs(readyRect.sizeDelta.y), ButtonHeight) + ButtonGap);
        buttonRect.anchoredPosition = readyRect.anchoredPosition + new Vector2(0f, yOffset);
    }

    private static void ShowOverlay()
    {
        if (buttonRoot == null)
        {
            return;
        }

        DestroyOverlay("reopen");
        var parent = ResolveUiParent() ?? buttonRoot.transform.parent;
        if (parent == null)
        {
            return;
        }

        var window = AuraToolsUi.CreateOverlay(
            OverlayName,
            parent,
            "联机MOD配置",
            () =>
            {
                activeOverlay = null;
                activeOverlayContent = null;
            },
            true,
            OverlayWidth);
        activeOverlay = window.transform.parent.gameObject;
        PositionOverlayWindow(window);

        var content = AuraToolsUi.CreateLayout("ModSyncContent", window.transform);
        activeOverlayContent = content.transform;
        var element = AuraToolsUi.EnsureLayoutElement(content);
        element.flexibleWidth = 1f;
        element.flexibleHeight = 1f;

        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        BuildOverlayContent();
    }

    private static void PositionOverlayWindow(GameObject window)
    {
        if (window.transform is not RectTransform windowRect)
        {
            return;
        }

        var parentRect = windowRect.parent as RectTransform;
        var available = parentRect == null || parentRect.rect.width <= 0f || parentRect.rect.height <= 0f
            ? new Vector2(OverlayWidth, OverlayHeight)
            : new Vector2(parentRect.rect.width, parentRect.rect.height);
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(
            Mathf.Min(OverlayWidth, Mathf.Max(520f, available.x - 32f)),
            Mathf.Min(OverlayHeight, Mathf.Max(420f, available.y - 32f)));
        windowRect.offsetMin = new Vector2(
            windowRect.anchoredPosition.x - windowRect.sizeDelta.x * 0.5f,
            windowRect.anchoredPosition.y - windowRect.sizeDelta.y * 0.5f);
        windowRect.offsetMax = new Vector2(
            windowRect.anchoredPosition.x + windowRect.sizeDelta.x * 0.5f,
            windowRect.anchoredPosition.y + windowRect.sizeDelta.y * 0.5f);
    }

    private static void RefreshOverlay()
    {
        if (activeOverlayContent == null)
        {
            return;
        }

        BuildOverlayContent();
    }

    private static void BuildOverlayContent()
    {
        var content = activeOverlayContent;
        if (content == null)
        {
            return;
        }

        AuraToolsUi.ClearChildren(content);
        var state = currentState;
        if (state == null || state.Players.Count == 0)
        {
            AuraToolsUi.AddText(content, "当前无联机玩家信息。", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
            return;
        }

        var host = state.Players.FirstOrDefault(player => string.Equals(player.PlayerId, state.HostPlayerId, StringComparison.Ordinal));
        var local = state.Players.FirstOrDefault(player => string.Equals(player.PlayerId, state.LocalPlayerId, StringComparison.Ordinal));
        AuraToolsUi.AddText(
            content,
            "房主：" + DisplayPlayer(host) + " / 本机：" + DisplayPlayer(local),
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        var scroll = AuraToolsUi.CreateScroll(content, "ModSyncTable");
        BuildTable(scroll, state);
        BuildFooter(content, state);
    }

    private static void BuildTable(Transform parent, AuraChatModSyncState state)
    {
        var headerCells = new List<CellSpec>
        {
            new("MOD", ModColumnWidth, AuraToolsUi.Accent)
        };

        foreach (var player in state.Players)
        {
            headerCells.Add(new CellSpec(PlayerHeader(player, state), PlayerColumnWidth, AuraToolsUi.Accent));
        }

        AddTableRow(parent, "Header", headerCells, AuraToolsUi.Header, AuraToolsUi.ColumnHeaderHeight);

        if (state.Rows.Count == 0)
        {
            AddTableRow(
                parent,
                "Empty",
                new List<CellSpec> { new("当前没有已启用的联机MOD。", ModColumnWidth + PlayerColumnWidth * Math.Max(1, state.Players.Count), AuraToolsUi.MutedText) },
                AuraToolsUi.Row,
                AuraToolsUi.DataRowHeight);
            return;
        }

        var index = 0;
        foreach (var row in state.Rows)
        {
            var hostMod = FindPlayerMod(state.Players.FirstOrDefault(player => string.Equals(player.PlayerId, state.HostPlayerId, StringComparison.Ordinal)), row.ModKey);
            var cells = new List<CellSpec> { new(row.ModName, ModColumnWidth, AuraToolsUi.Text) };
            foreach (var player in state.Players)
            {
                var mod = FindPlayerMod(player, row.ModKey);
                cells.Add(new CellSpec(ModCell(mod), PlayerColumnWidth, CellColor(hostMod, mod)));
            }

            AddTableRow(parent, "ModRow-" + index, cells, index % 2 == 0 ? AuraToolsUi.Row : AuraToolsUi.Panel, AuraToolsUi.DataRowHeight);
            index++;
        }
    }

    private static void BuildFooter(Transform parent, AuraChatModSyncState state)
    {
        var row = AuraToolsUi.CreateLayout("ModSyncFooter", parent);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.FooterHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var pending = SyncSession.CountPendingActions(state);
        var isHost = SyncSession.IsLocalHost(state);
        var text = manifestRequestPending
            ? SyncSession.ActionStatus
            : SyncSession.IsRunning
            ? SyncSession.ActionStatus
            : isHost
                ? "当前玩家是房主，只需查看配置。"
                : pending > 0
                    ? "待同步 " + pending + " 项。同步完成后需要重启游戏生效。"
                    : "当前没有需要同步的房主MOD差异。";
        if (IsClientMisidentifiedAsHost(state) && !SyncSession.IsRunning)
        {
            text = "无法确认本机玩家身份，暂不能同步。";
        }
        else if (!IsKnownLocalPlayer(state) && !SyncSession.IsRunning)
        {
            text = "无法识别本机玩家，暂不能同步。";
        }

        AuraToolsUi.AddText(row.transform, text, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        var syncButton = AuraToolsUi.AddButton(row.transform, SyncSession.IsRunning ? "同步中" : "一键同步", StartSyncFromUi, 128f, AuraToolsUi.ButtonHeight);
        syncButton.interactable = IsKnownLocalPlayer(state) && !isHost && pending > 0 && !SyncSession.IsRunning && !manifestRequestPending;
    }

    private static void AddTableRow(Transform parent, string name, IReadOnlyList<CellSpec> cells, Color color, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        AuraToolsUi.AddImage(row, color);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        foreach (var cell in cells)
        {
            var text = AuraToolsUi.AddText(row.transform, cell.Text, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, cell.Color, AuraToolsUi.TextMinHeight, 0f, cell.Width);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }

    private static string PlayerHeader(AuraChatModPlayerSnapshot player, AuraChatModSyncState state)
    {
        var label = DisplayPlayer(player);
        if (string.Equals(player.PlayerId, state.HostPlayerId, StringComparison.Ordinal))
        {
            label += " 房主";
        }

        if (string.Equals(player.PlayerId, state.LocalPlayerId, StringComparison.Ordinal))
        {
            label += " 本机";
        }

        return label;
    }

    private static AuraChatModSnapshot? FindPlayerMod(AuraChatModPlayerSnapshot? player, string modKey)
    {
        return player?.Mods.FirstOrDefault(mod => string.Equals(mod.MatchKey, modKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string ModCell(AuraChatModSnapshot? mod)
    {
        if (mod == null)
        {
            return "缺失";
        }

        if (!mod.Enabled)
        {
            return "OFF";
        }

        return string.IsNullOrWhiteSpace(mod.ModVersion) ? "ON" : mod.ModVersion;
    }

    private static Color CellColor(AuraChatModSnapshot? hostMod, AuraChatModSnapshot? mod)
    {
        if (mod == null)
        {
            return AuraToolsUi.WarningText;
        }

        if (!mod.Enabled)
        {
            return AuraToolsUi.MutedText;
        }

        if (hostMod != null
            && hostMod.Enabled
            && mod.Enabled
            && !string.IsNullOrWhiteSpace(hostMod.ModVersion)
            && !string.IsNullOrWhiteSpace(mod.ModVersion)
            && !string.Equals(hostMod.ModVersion, mod.ModVersion, StringComparison.OrdinalIgnoreCase))
        {
            return AuraToolsUi.WarningText;
        }

        return AuraToolsUi.SuccessText;
    }

    private static string DisplayPlayer(AuraChatModPlayerSnapshot? player)
    {
        if (player == null)
        {
            return "-";
        }

        return string.IsNullOrWhiteSpace(player.PlayerName) ? player.PlayerId : player.PlayerName;
    }

    private static IReadOnlyList<object> GetPlayersFromContext(ModHookContext context)
    {
        if (context.Arguments == null || context.Arguments.Length == 0)
        {
            return Array.Empty<object>();
        }

        if (context.Arguments[0] is IEnumerable enumerable)
        {
            return enumerable.Cast<object>().ToList();
        }

        return Array.Empty<object>();
    }

    private static string ResolveLocalPlayerId(IReadOnlyList<object> players)
    {
        var manager = PlayerManager.Instance;
        var isClient = manager != null && !manager.isServer;
        var playerId = (manager?.PlayerId ?? "").Trim();
        if (IsUsableLocalPlayerId(players, playerId, isClient))
        {
            return playerId;
        }

        var configPlayerId = (Singleton<GameConfigManager>.Instance?.PlayerId ?? "").Trim();
        if (IsUsableLocalPlayerId(players, configPlayerId, isClient))
        {
            return configPlayerId;
        }

        var managerName = (manager?.playerInfo?.Name ?? "").Trim();
        var playerIdByManagerName = ResolvePlayerIdByName(players, managerName);
        if (IsUsableLocalPlayerId(players, playerIdByManagerName, isClient))
        {
            return playerIdByManagerName;
        }

        var configName = (Singleton<GameConfigManager>.Instance?.PlayerName ?? "").Trim();
        var playerIdByConfigName = ResolvePlayerIdByName(players, configName);
        if (IsUsableLocalPlayerId(players, playerIdByConfigName, isClient))
        {
            return playerIdByConfigName;
        }

        var steamPlayerId = ResolveSteamPlayerId();
        if (IsUsableLocalPlayerId(players, steamPlayerId, isClient))
        {
            return steamPlayerId;
        }

        if (isClient)
        {
            var nonHostId = ResolveOnlyNonHostPlayerId(players);
            if (!string.IsNullOrWhiteSpace(nonHostId))
            {
                return nonHostId;
            }
        }

        return "";
    }

    private static bool IsUsableLocalPlayerId(IReadOnlyList<object> players, string playerId, bool isClient)
    {
        if (!ContainsPlayerId(players, playerId))
        {
            return false;
        }

        return !isClient || !IsLobbyHostPlayerId(players, playerId) || CountDistinctPlayerIds(players) <= 1;
    }

    private static bool ContainsPlayerId(IReadOnlyList<object> players, string playerId)
    {
        return !string.IsNullOrWhiteSpace(playerId)
               && players.Any(player => string.Equals(ReadString(player, "Id"), playerId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLobbyHostPlayerId(IReadOnlyList<object> players, string playerId)
    {
        var hostId = players
            .Select(player => ReadString(player, "Id"))
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? "";
        return !string.IsNullOrWhiteSpace(playerId)
               && string.Equals(hostId, playerId, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountDistinctPlayerIds(IReadOnlyList<object> players)
    {
        return players
            .Select(player => ReadString(player, "Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static string ResolvePlayerIdByName(IReadOnlyList<object> players, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return "";
        }

        var matches = players
            .Where(player => string.Equals(ReadString(player, "Name"), playerName, StringComparison.OrdinalIgnoreCase))
            .Select(player => ReadString(player, "Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches.Count == 1 ? matches[0] : "";
    }

    private static string ResolveOnlyNonHostPlayerId(IReadOnlyList<object> players)
    {
        var playerIds = players
            .Select(player => ReadString(player, "Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return playerIds.Count == 2 ? playerIds[1] : "";
    }

    private static string ResolveSteamPlayerId()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? steamUserType;
            try
            {
                steamUserType = assembly.GetType("Steamworks.SteamUser");
            }
            catch
            {
                continue;
            }

            var method = steamUserType?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                continue;
            }

            try
            {
                var steamId = SteamIdToString(method.Invoke(null, null));
                if (!string.IsNullOrWhiteSpace(steamId))
                {
                    return steamId;
                }
            }
            catch
            {
                return "";
            }
        }

        return "";
    }

    private static string SteamIdToString(object? value)
    {
        if (value == null)
        {
            return "";
        }

        var text = (value.ToString() ?? "").Trim();
        if (text.Length > 0 && text.All(char.IsDigit))
        {
            return text;
        }

        var type = value.GetType();
        foreach (var memberName in new[] { "m_SteamID", "SteamID", "steamID" })
        {
            try
            {
                var member = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
                             ?? type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);
                var memberText = Convert.ToString(member)?.Trim() ?? "";
                if (memberText.Length > 0 && memberText.All(char.IsDigit))
                {
                    return memberText;
                }
            }
            catch
            {
            }
        }

        return "";
    }

    private static string ReadString(object target, string name)
    {
        try
        {
            var type = target.GetType();
            return Convert.ToString(type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                                    ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target))?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsKnownLocalPlayer(AuraChatModSyncState? state)
    {
        return state != null
               && !string.IsNullOrWhiteSpace(state.LocalPlayerId)
               && state.Players.Any(player => string.Equals(player.PlayerId, state.LocalPlayerId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsClientMisidentifiedAsHost(AuraChatModSyncState? state)
    {
        var manager = PlayerManager.Instance;
        return state != null
               && manager != null
               && !manager.isServer
               && state.Players.Count > 1
               && SyncSession.IsLocalHost(state);
    }

    private static void LogStateDiagnostics(AuraChatModSyncState? state)
    {
        if (state == null)
        {
            return;
        }

        var localKnown = IsKnownLocalPlayer(state);
        var isHost = SyncSession.IsLocalHost(state);
        var pending = SyncSession.CountPendingActions(state);
        var playerIds = string.Join(",", state.Players.Select(player => player.PlayerId));
        var key = state.HostPlayerId + "|" + state.LocalPlayerId + "|" + localKnown + "|" + isHost + "|" + pending + "|" + playerIds;
        if (string.Equals(key, lastDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        lastDiagnosticsKey = key;
        AuraToolsLog.Info("[ModSync] lobby state: players="
                          + state.Players.Count
                          + ", host="
                          + state.HostPlayerId
                          + ", local="
                          + state.LocalPlayerId
                          + ", localKnown="
                          + localKnown
                          + ", isHost="
                          + isHost
                          + ", pending="
                          + pending);
    }

    private static void StartSyncFromUi()
    {
        var state = currentState;
        var localKnown = IsKnownLocalPlayer(state);
        var isHost = SyncSession.IsLocalHost(state);
        var pending = SyncSession.CountPendingActions(state);
        AuraToolsLog.Info("[ModSync] one-click requested: localKnown="
                          + localKnown
                          + ", isHost="
                          + isHost
                          + ", pending="
                          + pending
                          + ", host="
                          + (state?.HostPlayerId ?? "")
                          + ", local="
                          + (state?.LocalPlayerId ?? ""));
        if (state == null || !localKnown || isHost || pending <= 0 || SyncSession.IsRunning)
        {
            RefreshOverlay();
            return;
        }

        if (!RequestHostManifest(state))
        {
            SyncSession.StartSync(state);
        }

        RefreshOverlay();
    }

    private static bool RequestHostManifest(AuraChatModSyncState state)
    {
        var manager = PlayerManager.Instance;
        if (manager == null || manager.isServer)
        {
            return false;
        }

        try
        {
            manifestRequestPending = true;
            SyncSession.SetActionStatus("正在向房主请求完整MOD配置...");
            var command = new AuraToolsModSyncManifestCommand
            {
                RequesterPlayerId = state.LocalPlayerId,
                ProtocolVersion = AuraToolsModSyncManifestCommand.CurrentProtocolVersion
            };
            if (!AuraToolsRpcTransport.Send(manager, command, "ModSync.ManifestRequest"))
            {
                manifestRequestPending = false;
                SyncSession.SetActionStatus("Host manifest request failed; falling back to lobby state.");
                return false;
            }

            AuraToolsLog.Info("[ModSync] host manifest requested: requester=" + state.LocalPlayerId + ", host=" + state.HostPlayerId);
            return true;
        }
        catch (Exception ex)
        {
            manifestRequestPending = false;
            AuraToolsLog.Warn("[ModSync] host manifest request failed, fallback to lobby state: " + ex.Message);
            SyncSession.SetActionStatus("请求房主完整配置失败，改用大厅配置尝试同步。");
            return false;
        }
    }

    public static bool TryCreateHostModManifest(AuraToolsRpcSender sender, out AuraChatModPlayerSnapshot? manifest, out string rejection)
    {
        manifest = null;
        rejection = "";
        var manager = PlayerManager.Instance;
        if (manager == null || !manager.isServer)
        {
            rejection = "当前玩家不是房主。";
            return false;
        }

        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "请求者不在当前大厅。";
            return false;
        }

        manifest = AuraOnlineLocalModManifestBuilder.CreateLocalPlayerSnapshot(
            manager.PlayerId,
            manager.playerInfo?.Name ?? "");
        AuraToolsLog.Info("[ModSync] host manifest exported: requester="
                          + sender.PlayerId
                          + ", mods="
                          + manifest.Mods.Count
                          + ", workshop="
                          + manifest.Mods.Count(mod => mod.PublishedFileId != 0UL));
        return true;
    }

    public static bool TrySendHostModManifestChunks(
        AuraToolsRpcSender sender,
        string requesterPlayerId,
        string transferId,
        string payloadJson,
        out string rejection)
    {
        rejection = "";
        var manager = PlayerManager.Instance;
        if (manager == null || !manager.isServer)
        {
            rejection = "host player manager unavailable";
            return false;
        }

        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "requester not in lobby";
            return false;
        }

        if (string.IsNullOrWhiteSpace(requesterPlayerId))
        {
            requesterPlayerId = sender.PlayerId;
        }

        if (string.IsNullOrWhiteSpace(requesterPlayerId) || string.IsNullOrWhiteSpace(transferId))
        {
            rejection = "chunk transfer identity unavailable";
            return false;
        }

        var safePayloadJson = payloadJson ?? "";
        var bytes = Encoding.UTF8.GetByteCount(safePayloadJson);
        if (bytes <= 0 || bytes > MaxManifestTransferBytes)
        {
            rejection = "host manifest payload outside transfer budget";
            return false;
        }

        var chunkCount = Math.Max(1, (bytes + AuraToolsRpcTransport.ChunkRawBytes - 1) / AuraToolsRpcTransport.ChunkRawBytes);
        if (chunkCount > MaxManifestChunks)
        {
            rejection = "host manifest chunk count exceeds budget";
            return false;
        }

        var targetRequester = requesterPlayerId;
        return AuraToolsRpcTransport.SendJsonChunksAsync(
            manager,
            "ModSync.HostManifest",
            transferId,
            safePayloadJson,
            chunk => new AuraToolsModSyncManifestChunkCommand
            {
                RequesterPlayerId = targetRequester,
                TransferId = chunk.TransferId,
                ChunkIndex = chunk.ChunkIndex,
                ChunkCount = chunk.ChunkCount,
                TotalBytes = chunk.TotalBytes,
                Sha256 = chunk.Sha256,
                PayloadBase64 = chunk.PayloadBase64
            },
            excludeOwner: true);
    }

    public static void ReceiveHostModManifest(AuraToolsModSyncManifestCommand command)
    {
        if (!IsLocalRequester(command.RequesterPlayerId))
        {
            return;
        }

        if (command.HostManifestChunked)
        {
            manifestRequestPending = true;
            SyncSession.SetActionStatus("Receiving host mod manifest chunks...");
            AuraToolsLog.Info("[ModSync] host manifest chunk transfer announced: transfer="
                              + command.TransferId
                              + ", requester="
                              + command.RequesterPlayerId);
            RefreshOverlay();
            return;
        }

        manifestRequestPending = false;
        if (!string.IsNullOrWhiteSpace(command.RejectionReason) || command.HostManifest == null)
        {
            var reason = string.IsNullOrWhiteSpace(command.RejectionReason) ? "host manifest unavailable" : command.RejectionReason;
            /*
            var reason = string.IsNullOrWhiteSpace(command.RejectionReason) ? "房主未返回完整配置。" : command.RejectionReason;
            */
            AuraToolsLog.Warn("[ModSync] host manifest unavailable: " + reason);
            var fallbackState = currentState;
            if (fallbackState != null && IsKnownLocalPlayer(fallbackState) && !SyncSession.IsLocalHost(fallbackState))
            {
                AuraToolsLog.Info("[ModSync] falling back to lobby mod summary.");
                SyncSession.StartSync(fallbackState);
            }

            SyncSession.SetActionStatus("房主完整配置不可用：" + reason);
            RefreshOverlay();
            return;
        }

        var state = currentState;
        if (state == null || !IsKnownLocalPlayer(state))
        {
            SyncSession.SetActionStatus("本机玩家信息不可用，无法应用房主配置。");
            RefreshOverlay();
            return;
        }

        var localPlayer = state.Players.FirstOrDefault(player => string.Equals(player.PlayerId, state.LocalPlayerId, StringComparison.Ordinal));
        var localManifest = AuraOnlineLocalModManifestBuilder.CreateLocalPlayerSnapshot(
            state.LocalPlayerId,
            localPlayer?.PlayerName ?? "");
        var hostManifest = command.HostManifest;
        if (string.IsNullOrWhiteSpace(hostManifest.PlayerId))
        {
            hostManifest.PlayerId = state.HostPlayerId;
        }

        currentState = AuraChatModSyncSnapshot.BuildStateFromSnapshots(
            new[] { hostManifest, localManifest },
            AuraToolsIds.ModId,
            localManifest.PlayerId);

        var pending = SyncSession.CountPendingActions(currentState);
        AuraToolsLog.Info("[ModSync] host manifest received: hostMods="
                          + hostManifest.Mods.Count
                          + ", localMods="
                          + localManifest.Mods.Count
                          + ", hostWorkshop="
                          + hostManifest.Mods.Count(mod => mod.PublishedFileId != 0UL)
                          + ", pending="
                          + pending);
        SyncSession.StartSync(currentState);
        RefreshOverlay();
    }

    public static void ReceiveHostModManifestChunk(AuraToolsModSyncManifestChunkCommand command)
    {
        PruneExpiredManifestChunkBuffers();
        if (command == null
            || command.ProtocolVersion != AuraToolsModSyncManifestCommand.CurrentProtocolVersion
            || !IsLocalRequester(command.RequesterPlayerId)
            || string.IsNullOrWhiteSpace(command.TransferId))
        {
            return;
        }

        if (command.ChunkCount <= 0
            || command.ChunkCount > MaxManifestChunks
            || command.ChunkIndex < 0
            || command.ChunkIndex >= command.ChunkCount
            || command.TotalBytes <= 0
            || command.TotalBytes > MaxManifestTransferBytes)
        {
            AuraToolsLog.Warn("[ModSync] rejected manifest chunk: invalid metadata. transfer=" + command.TransferId);
            return;
        }

        byte[] chunkBytes;
        try
        {
            chunkBytes = Convert.FromBase64String(command.PayloadBase64 ?? "");
        }
        catch
        {
            AuraToolsLog.Warn("[ModSync] rejected manifest chunk: invalid base64. transfer=" + command.TransferId);
            return;
        }

        if (!ManifestChunkBuffers.TryGetValue(command.TransferId, out var buffer))
        {
            if (ManifestChunkBuffers.Count >= MaxManifestActiveTransfers)
            {
                AuraToolsLog.Warn("[ModSync] rejected manifest chunk: too many active transfers. transfer=" + command.TransferId);
                return;
            }

            buffer = new ManifestChunkBuffer(
                command.RequesterPlayerId,
                command.TransferId,
                command.ChunkCount,
                command.TotalBytes,
                command.Sha256);
            ManifestChunkBuffers[command.TransferId] = buffer;
        }

        if (!buffer.Accepts(command))
        {
            AuraToolsLog.Warn("[ModSync] rejected manifest chunk: metadata mismatch. transfer=" + command.TransferId);
            ManifestChunkBuffers.Remove(command.TransferId);
            return;
        }

        buffer.Set(command.ChunkIndex, chunkBytes);
        manifestRequestPending = true;
        SyncSession.SetActionStatus("Receiving host mod manifest " + buffer.ReceivedCount + "/" + buffer.ChunkCount + "...");
        if (!buffer.IsComplete)
        {
            RefreshOverlay();
            return;
        }

        ManifestChunkBuffers.Remove(command.TransferId);
        var payload = buffer.Join();
        if (payload.Length != buffer.TotalBytes || !string.Equals(Sha256Hex(payload), buffer.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            manifestRequestPending = false;
            SyncSession.SetActionStatus("Host manifest transfer failed: checksum mismatch.");
            AuraToolsLog.Warn("[ModSync] manifest chunk checksum mismatch. transfer=" + command.TransferId);
            RefreshOverlay();
            return;
        }

        try
        {
            var json = Encoding.UTF8.GetString(payload);
            var manifest = AuraSharedJson.Deserialize<AuraChatModPlayerSnapshot>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException("manifest is empty");
            }

            AuraToolsLog.Info("[ModSync] host manifest chunk transfer completed: transfer="
                              + command.TransferId
                              + ", bytes="
                              + payload.Length
                              + ", chunks="
                              + buffer.ChunkCount);
            ReceiveHostModManifest(new AuraToolsModSyncManifestCommand
            {
                RequesterPlayerId = command.RequesterPlayerId,
                ProtocolVersion = AuraToolsModSyncManifestCommand.CurrentProtocolVersion,
                HostManifest = manifest
            });
        }
        catch (Exception ex)
        {
            manifestRequestPending = false;
            SyncSession.SetActionStatus("Host manifest transfer failed: " + ex.Message);
            AuraToolsLog.Warn("[ModSync] manifest chunk transfer failed: " + ex.Message);
            RefreshOverlay();
        }
    }

    private static void PruneExpiredManifestChunkBuffers()
    {
        if (ManifestChunkBuffers.Count == 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - ManifestChunkBufferTtl;
        foreach (var transferId in ManifestChunkBuffers
                     .Where(pair => pair.Value.CreatedAtUtc < cutoff)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            ManifestChunkBuffers.Remove(transferId);
            AuraToolsLog.Warn("[ModSync] expired stale manifest chunk transfer: " + transferId);
        }
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private sealed class ManifestChunkBuffer
    {
        private readonly byte[][] chunks;
        private readonly bool[] received;

        public ManifestChunkBuffer(
            string requesterPlayerId,
            string transferId,
            int chunkCount,
            int totalBytes,
            string sha256)
        {
            RequesterPlayerId = requesterPlayerId ?? "";
            TransferId = transferId ?? "";
            ChunkCount = chunkCount;
            TotalBytes = totalBytes;
            Sha256 = sha256 ?? "";
            chunks = new byte[Math.Max(0, chunkCount)][];
            received = new bool[Math.Max(0, chunkCount)];
        }

        public string RequesterPlayerId { get; }

        public string TransferId { get; }

        public int ChunkCount { get; }

        public int TotalBytes { get; }

        public string Sha256 { get; }

        public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

        public int ReceivedCount { get; private set; }

        public bool IsComplete => ReceivedCount == ChunkCount;

        public bool Accepts(AuraToolsModSyncManifestChunkCommand command)
        {
            return command != null
                   && string.Equals(RequesterPlayerId, command.RequesterPlayerId ?? "", StringComparison.Ordinal)
                   && string.Equals(TransferId, command.TransferId ?? "", StringComparison.Ordinal)
                   && ChunkCount == command.ChunkCount
                   && TotalBytes == command.TotalBytes
                   && string.Equals(Sha256, command.Sha256 ?? "", StringComparison.OrdinalIgnoreCase)
                   && command.ChunkIndex >= 0
                   && command.ChunkIndex < ChunkCount;
        }

        public void Set(int index, byte[] bytes)
        {
            if (index < 0 || index >= ChunkCount || bytes == null)
            {
                return;
            }

            chunks[index] = bytes;
            if (!received[index])
            {
                received[index] = true;
                ReceivedCount++;
            }
        }

        public byte[] Join()
        {
            var result = new byte[TotalBytes];
            var offset = 0;
            for (var index = 0; index < ChunkCount; index++)
            {
                var chunk = chunks[index] ?? Array.Empty<byte>();
                if (offset + chunk.Length > result.Length)
                {
                    throw new InvalidOperationException("manifest chunk payload exceeds declared size");
                }

                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            if (offset != result.Length)
            {
                throw new InvalidOperationException("manifest chunk payload is incomplete");
            }

            return result;
        }
    }

    private static bool IsLocalRequester(string requesterPlayerId)
    {
        if (string.IsNullOrWhiteSpace(requesterPlayerId))
        {
            return false;
        }

        var managerId = (PlayerManager.Instance?.PlayerId ?? "").Trim();
        return string.Equals(managerId, requesterPlayerId, StringComparison.Ordinal)
               || string.Equals(currentState?.LocalPlayerId ?? "", requesterPlayerId, StringComparison.Ordinal);
    }

    private static void HideOnUiManagerClose(ModHookContext context)
    {
        var uiName = GetArgumentString(context, 0);
        HideIfAdventureUiIsLeaving(uiName, "UIManager.CloseUI");
    }

    private static void HideOnUiBaseClose(ModHookContext context, string source)
    {
        var uiName = GetTargetName(context);
        HideIfAdventureUiIsLeaving(uiName, source);
    }

    private static void HideIfAdventureUiIsLeaving(string uiName, string source)
    {
        if (!string.Equals(uiName, "GameEntryUI", StringComparison.Ordinal)
            && !string.Equals(uiName, "GameExitUI", StringComparison.Ordinal))
        {
            return;
        }

        DestroyUi(source + ":" + uiName);
    }

    private static string GetArgumentString(ModHookContext context, int index)
    {
        try
        {
            if (context.Arguments == null || context.Arguments.Length <= index)
            {
                return "";
            }

            return context.Arguments[index]?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string GetTargetName(ModHookContext context)
    {
        try
        {
            if (context.Target is UnityEngine.Component component)
            {
                return component.gameObject.name;
            }

            return context.Target?.GetType().Name ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static Transform? ResolveUiParent()
    {
        var manager = UIManager.Instance;
        if (manager?.canvasTf != null)
        {
            return manager.canvasTf;
        }

        var canvas = GameObject.Find("Canvas");
        if (canvas != null)
        {
            return canvas.transform;
        }

#pragma warning disable CS0618
        return Object.FindObjectOfType<Canvas>()?.transform;
#pragma warning restore CS0618
    }

    private static void DestroyUi(string source)
    {
        currentEntry = null;
        currentState = null;
        DestroyOverlay(source);
        DestroyButton(source);
    }

    private static void DestroyButton(string source)
    {
        if (buttonRoot == null)
        {
            return;
        }

        UiRaycastSafeDestroyRuntime.DisableAndHide(buttonRoot, "AuraTools ModSync button " + source);
        Object.Destroy(buttonRoot);
        buttonRoot = null;
    }

    private static void DestroyOverlay(string source)
    {
        activeOverlayContent = null;
        if (activeOverlay == null)
        {
            return;
        }

        UiRaycastSafeDestroyRuntime.DisableAndHide(activeOverlay, "AuraTools ModSync overlay " + source);
        Object.Destroy(activeOverlay);
        activeOverlay = null;
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraToolsHookRegistry.Before(config, target, action, "ModSync");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraToolsHookRegistry.After(config, target, action, "ModSync");
    }

    private readonly struct CellSpec
    {
        public CellSpec(string text, float width, Color color)
        {
            Text = text;
            Width = width;
            Color = color;
        }

        public string Text { get; }

        public float Width { get; }

        public Color Color { get; }
    }
}
