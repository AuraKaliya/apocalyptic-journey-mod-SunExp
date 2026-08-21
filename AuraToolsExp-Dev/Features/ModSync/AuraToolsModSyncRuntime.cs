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
using AuraToolsExp.Dll.Modules;
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
    private const int MaxTargetedManifestBytes = 256 * 1024;
    private static readonly TimeSpan ManifestChunkBufferTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan HostManifestCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TargetedManifestRequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BroadcastManifestRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly AuraOnlineHostModSyncSession SyncSession = new(AuraToolsIds.ModId, AuraToolsLog.Info, AuraToolsLog.Warn);
    private static readonly AuraToolsModSyncRequestTracker ManifestRequest = new();
    private static readonly Dictionary<string, ManifestChunkBuffer> ManifestChunkBuffers = new(StringComparer.Ordinal);

    private static bool initialized;
    private static ModConfig? currentConfig;
    private static GameEntryUI? currentEntry;
    private static GameObject? buttonRoot;
    private static GameObject? activeOverlay;
    private static Transform? activeOverlayContent;
    private static AuraChatModSyncState? currentState;
    private static string lastDiagnosticsKey = "";
    private static uint pendingTargetQueryId;
    private static AuraChatModPlayerSnapshot? cachedHostManifest;
    private static string cachedHostPlayerId = "";
    private static DateTime cachedHostManifestAtUtc;
    private static IDisposable? lobbySubscription;

    internal static bool CanShowOverlay => IsEnabled() && currentState != null && currentEntry != null;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        currentConfig = modConfig;
        SyncSession.Changed += RefreshOverlay;
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.ModSync,
            OnConfigChanged);
        lobbySubscription = AuraLobbySnapshotRuntime.Register(
            modConfig,
            AuraToolsIds.ModId,
            "ModSync",
            UpdateLobby,
            AuraToolsLog.Debug,
            AuraToolsLog.Warn);
        RegisterAfter(modConfig, "GameEntryUI.ShowCareer", _ => RefreshButton());
        RegisterBefore(modConfig, "GameEntryUI.StartGame", _ => DestroyUi("GameEntryUI.StartGame"));
        RegisterBefore(modConfig, "UIManager.CloseUI", HideOnUiManagerClose);
        RegisterBefore(modConfig, "UIBase.Close", context => HideOnUiBaseClose(context, "UIBase.Close"));
        RegisterBefore(modConfig, "UIBase.OnDestroy", context => HideOnUiBaseClose(context, "UIBase.OnDestroy"));
        ApplyModuleActivation(IsEnabled());
    }

    internal static void ApplyModuleActivation(bool enabled)
    {
        if (!initialized || currentConfig == null) return;
        if (!enabled)
        {
            lobbySubscription?.Dispose();
            lobbySubscription = null;
            DestroyUi("module-disabled");
            return;
        }

        lobbySubscription ??= AuraLobbySnapshotRuntime.Register(
            currentConfig,
            AuraToolsIds.ModId,
            "ModSync",
            UpdateLobby,
            AuraToolsLog.Debug,
            AuraToolsLog.Warn);
        UpdateLobby(AuraLobbySnapshotRuntime.Current);
    }

    private static void UpdateLobby(AuraLobbySnapshot snapshot)
    {
        try
        {
            currentEntry = snapshot.Entry;
            if (!IsEnabled()
                || snapshot.Players.Count == 0
                || PlayerManager.Instance == null)
            {
                DestroyUi("UpdateLobby:not-available");
                return;
            }

            currentState = snapshot.ModSyncState;
            LogStateDiagnostics(currentState);
            EnsureButton();
            RefreshOverlay();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[ModSync] lobby update failed: " + ex.Message);
        }
        finally
        {
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.ModSync);
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.PixelEmoji);
            AuraToolModuleHost.RefreshState(
                AuraToolModuleIds.DamageStatistics);
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
        return AuraToolsConfigService.MatchExperience.ModSync.Enabled;
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

    internal static void ShowOverlayFromLobbyStatus()
    {
        RefreshButton();
        ShowOverlay();
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
        var text = ManifestRequest.IsPending
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
        syncButton.interactable = IsKnownLocalPlayer(state) && !isHost && pending > 0 && !SyncSession.IsRunning && !ManifestRequest.IsPending;
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

    private static bool RequestHostManifest(
        AuraChatModSyncState state,
        AuraToolsModSyncRequestMode requestedMode =
            AuraToolsModSyncRequestMode.Targeted)
    {
        var manager = PlayerManager.Instance;
        if (manager == null || manager.isServer)
        {
            return false;
        }

        try
        {
            var requestId = AuraToolsRpcTransport.NewTransferId("modsync-request");
            var mode = requestedMode;
            var forceBroadcast = mode != AuraToolsModSyncRequestMode.Targeted;
            var timeout = forceBroadcast
                ? BroadcastManifestRequestTimeout
                : TargetedManifestRequestTimeout;
            ManifestRequest.Begin(requestId, DateTime.UtcNow, timeout, mode);
            SyncSession.SetActionStatus(mode == AuraToolsModSyncRequestMode.LegacyBroadcastFallback
                ? "正在使用旧版兼容协议请求房主配置..."
                : forceBroadcast
                    ? "定向响应超时，正在使用兼容传输重试..."
                    : "正在向房主请求完整MOD配置...");

            if (TryUseCachedHostManifest(state, requestId))
            {
                return true;
            }

            uint targetQueryId = 0;
            if (!forceBroadcast)
            {
                var query = new AuraToolsModSyncManifestQuery();
                if (!AuraToolsTargetedQueryTransport.TryRegister(
                        manager,
                        query,
                        ReceiveTargetedHostManifest,
                        out targetQueryId,
                        out var targetedRejection))
                {
                    AuraToolsLog.Warn("[ModSync] targeted callback registration unavailable; using broadcast transport: "
                                      + targetedRejection);
                    mode = AuraToolsModSyncRequestMode.BroadcastFallback;
                    timeout = BroadcastManifestRequestTimeout;
                    ManifestRequest.Begin(requestId, DateTime.UtcNow, timeout, mode);
                    SyncSession.SetActionStatus("定向传输不可用，正在使用兼容传输...");
                }
            }

            pendingTargetQueryId = targetQueryId;
            var command = new AuraToolsModSyncManifestCommand
            {
                RequesterPlayerId = state.LocalPlayerId,
                RequestId = requestId,
                TargetQueryId = targetQueryId,
                ForceBroadcastResponse = forceBroadcast,
                RequesterToolVersion = FindToolVersion(
                    AuraOnlineLocalModManifestBuilder.CreateLocalPlayerSnapshot(
                        state.LocalPlayerId,
                        PlayerManager.Instance?.playerInfo?.Name ?? "")),
                ProtocolVersion =
                    AuraToolsModSyncProtocolPolicy.ProtocolVersionFor(mode)
            };
            if (!AuraToolsRpcTransport.Send(manager, command, "ModSync.ManifestRequest"))
            {
                ClearPendingManifestRequest();
                SyncSession.SetActionStatus("请求房主完整配置失败，改用大厅配置尝试同步。");
                return false;
            }

            ScheduleManifestRequestTimeout(requestId);
            AuraToolsLog.Info("[ModSync] host manifest requested: requester="
                              + state.LocalPlayerId
                              + ", host="
                              + state.HostPlayerId
                              + ", mode="
                              + mode
                              + ", queryId="
                              + targetQueryId
                              + ".");
            return true;
        }
        catch (Exception ex)
        {
            ClearPendingManifestRequest();
            AuraToolsLog.Warn("[ModSync] host manifest request failed, fallback to lobby state: " + ex.Message);
            SyncSession.SetActionStatus("请求房主完整配置失败，改用大厅配置尝试同步。");
            return false;
        }
    }

    private static void ScheduleManifestRequestTimeout(string requestId)
    {
        var scheduled = AuraSharedFrameStepRunner.Run(new AuraSharedFrameStepSequence
        {
            OwnerId = AuraToolsIds.ModId,
            Source = "ModSync.ManifestRequestTimeout",
            DeduplicateKey = "ModSync.ManifestRequestTimeout:" + requestId,
            Phase = AuraSharedFramePhase.Background,
            IsCancelled = () => !ManifestRequest.IsPendingRequest(requestId),
            Steps = new[]
            {
                new AuraSharedFrameStep
                {
                    Name = "wait",
                    Work = () => CheckManifestRequestTimeout(requestId)
                }
            }
        });
        if (!scheduled)
        {
            AuraToolsLog.Warn("[ModSync] manifest timeout watcher could not be scheduled: request=" + requestId);
        }
    }

    private static AuraSharedFrameStepResult CheckManifestRequestTimeout(string requestId)
    {
        if (!ManifestRequest.IsPendingRequest(requestId))
        {
            return AuraSharedFrameStepResult.Complete;
        }

        if (!ManifestRequest.IsExpired(DateTime.UtcNow))
        {
            return AuraSharedFrameStepResult.Wait(15);
        }

        var state = currentState;
        var mode = ManifestRequest.Mode;
        ClearPendingManifestRequest();
        if (state != null
            && IsKnownLocalPlayer(state)
            && !SyncSession.IsLocalHost(state)
            && AuraToolsModSyncProtocolPolicy.TryNextFallback(
                mode,
                out var nextMode))
        {
            AuraToolsLog.Warn("[ModSync] host manifest request timed out; retrying mode="
                              + nextMode
                              + ". request="
                              + requestId);
            if (RequestHostManifest(state, nextMode))
            {
                RefreshOverlay();
                return AuraSharedFrameStepResult.Complete;
            }
        }

        FallbackToLobbySummary(
            "房主响应超时，已改用大厅配置尝试同步。",
            "host manifest request timed out; request=" + requestId);
        return AuraSharedFrameStepResult.Complete;
    }

    private static void FallbackToLobbySummary(string status, string reason)
    {
        var state = currentState;
        ClearPendingManifestRequest();
        AuraToolsLog.Warn("[ModSync] falling back to lobby mod summary: " + reason);
        SyncSession.SetActionStatus(status);
        if (state != null && IsKnownLocalPlayer(state) && !SyncSession.IsLocalHost(state))
        {
            SyncSession.StartSync(state);
        }

        RefreshOverlay();
    }

    private static void ClearPendingManifestRequest()
    {
        RemovePendingTargetQuery();
        ManifestRequest.Clear();
    }

    private static void RemovePendingTargetQuery()
    {
        if (pendingTargetQueryId != 0)
        {
            AuraToolsTargetedQueryTransport.RemovePending(PlayerManager.Instance, pendingTargetQueryId);
            pendingTargetQueryId = 0;
        }
    }

    private static void ReceiveTargetedHostManifest(string payload)
    {
        pendingTargetQueryId = 0;
        try
        {
            var result = AuraSharedJson.Deserialize<AuraToolsModSyncManifestQueryResult>(payload ?? "");
            if (result == null)
            {
                throw new InvalidOperationException("targeted host manifest response is empty");
            }

            ReceiveHostModManifest(new AuraToolsModSyncManifestCommand
            {
                ProtocolVersion = result.ProtocolVersion,
                MinimumProtocolVersion = result.MinimumProtocolVersion,
                RequiredCapabilities = result.RequiredCapabilities,
                RequesterPlayerId = result.RequesterPlayerId,
                RequestId = result.RequestId,
                HostToolVersion = result.HostToolVersion,
                HostManifest = result.HostManifest,
                RejectionReason = result.RejectionReason
            });
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[ModSync] targeted host manifest response failed: " + ex.Message);
        }
    }

    public static string FindToolVersion(AuraChatModPlayerSnapshot? snapshot)
    {
        return snapshot?.Mods.FirstOrDefault(mod =>
                   string.Equals(mod.ModId, AuraToolsIds.ModId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mod.ModName, AuraToolsIds.ModId, StringComparison.OrdinalIgnoreCase))
               ?.ModVersion?.Trim() ?? "";
    }

    public static bool TrySendTargetedHostModManifest(
        string requesterPlayerId,
        string requestId,
        uint targetQueryId,
        string hostToolVersion,
        AuraChatModPlayerSnapshot manifest,
        out string rejection)
    {
        rejection = "";
        if (manifest == null || string.IsNullOrWhiteSpace(requestId) || targetQueryId == 0)
        {
            rejection = "targeted manifest response input unavailable";
            return false;
        }

        var result = new AuraToolsModSyncManifestQueryResult
        {
            ProtocolVersion = AuraToolsModSyncManifestCommand.CurrentProtocolVersion,
            MinimumProtocolVersion =
                AuraToolsModSyncManifestCommand.MinimumSupportedProtocolVersion,
            RequesterPlayerId = requesterPlayerId ?? "",
            RequestId = requestId.Trim(),
            HostToolVersion = hostToolVersion ?? "",
            HostManifest = manifest
        };
        var payload = AuraSharedJson.Serialize(result);
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);
        if (payloadBytes <= 0 || payloadBytes > MaxTargetedManifestBytes)
        {
            rejection = "targeted manifest payload outside transfer budget: bytes=" + payloadBytes;
            return false;
        }

        var response = new AuraToolsModSyncManifestQuery
        {
            Result = payload
        };
        var safeRequesterPlayerId = requesterPlayerId ?? "";
        if (!AuraToolsTargetedQueryTransport.TrySend(
                safeRequesterPlayerId,
                targetQueryId,
                response,
                out rejection))
        {
            return false;
        }

        AuraToolsLog.Info("[ModSync] targeted host manifest sent: requester="
                          + safeRequesterPlayerId
                          + ", request="
                          + requestId
                          + ", queryId="
                          + targetQueryId
                          + ", mods="
                          + manifest.Mods.Count
                          + ", bytes="
                          + payloadBytes
                          + ".");
        return true;
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
        string requestId,
        string transferId,
        int protocolVersion,
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
        var targetRequestId = requestId ?? "";
        return AuraToolsRpcTransport.SendJsonChunksAsync(
            manager,
            "ModSync.HostManifest",
            transferId,
            safePayloadJson,
            chunk => new AuraToolsModSyncManifestChunkCommand
            {
                ProtocolVersion = protocolVersion,
                RequesterPlayerId = targetRequester,
                RequestId = targetRequestId,
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
        if (command == null)
        {
            return;
        }

        var isLocalRequester = IsLocalRequester(command.RequesterPlayerId);
        var matchesPendingRequest = isLocalRequester && ManifestRequest.Matches(command.RequestId);
        var compatibility = AuraToolsModSyncManifestCommand.Protocol.Negotiate(
            command.ProtocolVersion,
            command.MinimumProtocolVersion,
            command.RequiredCapabilities);
        if (!compatibility.Compatible)
        {
            if (matchesPendingRequest)
            {
                AuraToolsLog.Warn("[ModSync] ignored incompatible host manifest response: received="
                                  + command.ProtocolVersion
                                  + ", supported="
                                  + AuraToolsModSyncManifestCommand.MinimumSupportedProtocolVersion
                                  + ".."
                                  + AuraToolsModSyncManifestCommand.CurrentProtocolVersion
                                  + ".");
                FallbackToLobbySummary(
                    "房主与本机的MOD同步协议不兼容，已改用大厅配置。",
                    "protocol mismatch");
            }

            return;
        }

        if (command.HostManifest != null)
        {
            CacheHostManifest(command.HostManifest);
        }

        if (!isLocalRequester)
        {
            return;
        }

        if (!matchesPendingRequest)
        {
            AuraToolsLog.Warn("[ModSync] ignored stale or unrelated host manifest response: request="
                              + command.RequestId
                              + ", active="
                              + ManifestRequest.RequestId);
            return;
        }

        if (command.HostManifestChunked)
        {
            var activeMode = ManifestRequest.Mode;
            RemovePendingTargetQuery();
            ManifestRequest.Begin(
                command.RequestId,
                DateTime.UtcNow,
                BroadcastManifestRequestTimeout,
                activeMode);
            SyncSession.SetActionStatus("Receiving host mod manifest chunks...");
            AuraToolsLog.Info("[ModSync] host manifest chunk transfer announced: transfer="
                              + command.TransferId
                              + ", requester="
                              + command.RequesterPlayerId);
            RefreshOverlay();
            return;
        }

        if (!string.IsNullOrWhiteSpace(command.RejectionReason) || command.HostManifest == null)
        {
            var reason = string.IsNullOrWhiteSpace(command.RejectionReason) ? "host manifest unavailable" : command.RejectionReason;
            AuraToolsLog.Warn("[ModSync] host manifest unavailable: " + reason);
            if (reason.IndexOf("协议", StringComparison.OrdinalIgnoreCase) >= 0
                && ManifestRequest.Mode
                != AuraToolsModSyncRequestMode.LegacyBroadcastFallback
                && TryRetryLegacyManifestRequest())
            {
                return;
            }
            FallbackToLobbySummary("房主完整配置不可用：" + reason, reason);
            return;
        }

        ClearPendingManifestRequest();

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
        var localToolVersion = FindToolVersion(localManifest);
        var hostToolVersion = string.IsNullOrWhiteSpace(command.HostToolVersion)
            ? FindToolVersion(hostManifest)
            : command.HostToolVersion.Trim();
        if (!string.IsNullOrWhiteSpace(localToolVersion)
            && !string.IsNullOrWhiteSpace(hostToolVersion)
            && !string.Equals(localToolVersion, hostToolVersion, StringComparison.OrdinalIgnoreCase))
        {
            AuraToolsLog.Warn("[ModSync] AuraTools version differs: host="
                              + hostToolVersion
                              + ", local="
                              + localToolVersion
                              + ". Protocol compatibility was accepted, but AuraTools itself is not auto-replaced.");
        }

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
        var isLocalRequester = command != null && IsLocalRequester(command.RequesterPlayerId);
        var isActiveLocalRequester = isLocalRequester && ManifestRequest.Matches(command?.RequestId ?? "");
        if (command == null
            || !AuraToolsModSyncManifestCommand.Protocol.Negotiate(
                command.ProtocolVersion,
                command.MinimumProtocolVersion).Compatible
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
                command.RequestId,
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
        if (isActiveLocalRequester)
        {
            SyncSession.SetActionStatus("Receiving host mod manifest " + buffer.ReceivedCount + "/" + buffer.ChunkCount + "...");
        }
        if (!buffer.IsComplete)
        {
            if (isActiveLocalRequester)
            {
                RefreshOverlay();
            }
            return;
        }

        ManifestChunkBuffers.Remove(command.TransferId);
        var payload = buffer.Join();
        if (payload.Length != buffer.TotalBytes || !string.Equals(Sha256Hex(payload), buffer.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            if (isActiveLocalRequester)
            {
                FallbackToLobbySummary(
                    "房主配置传输校验失败，已改用大厅配置。",
                    "manifest chunk checksum mismatch");
            }
            AuraToolsLog.Warn("[ModSync] manifest chunk checksum mismatch. transfer=" + command.TransferId);
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
            CacheHostManifest(manifest);
            if (isActiveLocalRequester)
            {
                ReceiveHostModManifest(new AuraToolsModSyncManifestCommand
                {
                    RequesterPlayerId = command.RequesterPlayerId,
                    RequestId = command.RequestId,
                    ProtocolVersion = command.ProtocolVersion,
                    HostManifest = manifest
                });
            }
        }
        catch (Exception ex)
        {
            if (isActiveLocalRequester)
            {
                FallbackToLobbySummary(
                    "房主配置传输失败，已改用大厅配置。",
                    "manifest chunk transfer failed: " + ex.Message);
            }
            AuraToolsLog.Warn("[ModSync] manifest chunk transfer failed: " + ex.Message);
        }
    }

    private static bool TryUseCachedHostManifest(AuraChatModSyncState state, string requestId)
    {
        if (cachedHostManifest == null
            || DateTime.UtcNow - cachedHostManifestAtUtc > HostManifestCacheTtl
            || !string.Equals(cachedHostPlayerId, state.HostPlayerId, StringComparison.Ordinal))
        {
            return false;
        }

        AuraToolsLog.Info("[ModSync] reused cached host manifest: host="
                          + cachedHostPlayerId
                          + ", ageMs="
                          + (long)(DateTime.UtcNow - cachedHostManifestAtUtc).TotalMilliseconds
                          + ".");
        ReceiveHostModManifest(new AuraToolsModSyncManifestCommand
        {
            RequesterPlayerId = state.LocalPlayerId,
            RequestId = requestId,
            ProtocolVersion = AuraToolsModSyncManifestCommand.CurrentProtocolVersion,
            HostManifest = cachedHostManifest
        });
        return true;
    }

    private static bool TryRetryLegacyManifestRequest()
    {
        var state = currentState;
        ClearPendingManifestRequest();
        return state != null
               && IsKnownLocalPlayer(state)
               && !SyncSession.IsLocalHost(state)
               && RequestHostManifest(
                   state,
                   AuraToolsModSyncRequestMode.LegacyBroadcastFallback);
    }

    private static void CacheHostManifest(AuraChatModPlayerSnapshot manifest)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.PlayerId))
        {
            return;
        }

        cachedHostManifest = manifest;
        cachedHostPlayerId = manifest.PlayerId;
        cachedHostManifestAtUtc = DateTime.UtcNow;
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
            string requestId,
            string transferId,
            int chunkCount,
            int totalBytes,
            string sha256)
        {
            RequesterPlayerId = requesterPlayerId ?? "";
            RequestId = requestId ?? "";
            TransferId = transferId ?? "";
            ChunkCount = chunkCount;
            TotalBytes = totalBytes;
            Sha256 = sha256 ?? "";
            chunks = new byte[Math.Max(0, chunkCount)][];
            received = new bool[Math.Max(0, chunkCount)];
        }

        public string RequesterPlayerId { get; }

        public string RequestId { get; }

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
                   && string.Equals(RequestId, command.RequestId ?? "", StringComparison.Ordinal)
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
        ClearPendingManifestRequest();
        ManifestChunkBuffers.Clear();
        cachedHostManifest = null;
        cachedHostPlayerId = "";
        cachedHostManifestAtUtc = default;
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
