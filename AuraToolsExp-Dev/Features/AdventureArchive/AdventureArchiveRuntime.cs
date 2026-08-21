using System;
using System.Collections.Generic;
using System.Linq;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.AdventureArchive;

internal static class AdventureArchiveRuntime
{
    private const string Owner = "AdventureArchive";
    private static bool initialized;
    private static ModConfig? currentConfig;
    private static string activeAdventureId = "";
    private static string lastCompletedAdventureId = "";
    private static IDisposable? lifecycle;
    private static int cachedCount;

    internal static bool Enabled => AuraToolsConfigService.AdventureArchive.Enabled;

    internal static string ActiveAdventureId => activeAdventureId;

    internal static int Count => cachedCount;

    internal static void RefreshCount()
    {
        try { cachedCount = AdventureArchiveStorage.Database.List(AuraToolsConfigService.AdventureArchive.MaximumAdventures).Count; }
        catch { cachedCount = 0; }
    }

    internal static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        currentConfig = modConfig;
        AuraToolsConfigService.SubscribeModule(AuraToolModuleIds.AdventureArchive, OnConfigChanged);
        AuraToolsHookRegistry.After(modConfig, "GameEntryUI.StartGame", _ => BeginNewAdventure(), Owner);
        AuraToolsHookRegistry.After(modConfig, "NormalMapManager.InitRoleTable", _ => CaptureCheckpoint("adventure-ready", "冒险已开始"), Owner);
        AuraToolsHookRegistry.After(modConfig, "SublimationManager.InitRoleTable", _ => CaptureCheckpoint("adventure-ready", "冒险已开始"), Owner);
        AuraToolsHookRegistry.After(modConfig, "SlotMachineManager.InitRoleTable", _ => CaptureCheckpoint("adventure-ready", "冒险已开始"), Owner);
        AuraToolsHookRegistry.After(modConfig, "MapSelectUI.ReadyToSelect", _ => CaptureSnapshot("map-ready"), Owner);
        AuraToolsHookRegistry.After(modConfig, "CardChoiceUI.Select", CaptureReward, Owner);
        AuraToolsHookRegistry.Before(modConfig, "GameApp.GameOver", CompleteFromHook, Owner);
        AuraToolsHookRegistry.Before(modConfig, "PlayerManager.GameOver", CompleteFromHook, Owner);
        AuraToolsHookRegistry.After(modConfig, "GameExitUI.Start", context => Complete("Exited", HookSource(context)), Owner);
        ApplyModuleActivation(Enabled);
        if (Enabled)
        {
            try
            {
                AdventureArchiveStorage.Database.Prune(AuraToolsConfigService.AdventureArchive.MaximumAdventures);
                RefreshCount();
            }
            catch (Exception ex) { AuraToolsLog.Warn("[AdventureArchive] initialization failed: " + ex.Message); }
        }
    }

    private static void OnConfigChanged()
    {
        ApplyModuleActivation(Enabled);
        if (!Enabled) return;
        try
        {
            AdventureArchiveStorage.Database.Prune(AuraToolsConfigService.AdventureArchive.MaximumAdventures);
            RefreshCount();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[AdventureArchive] configuration apply failed: " + ex.Message);
        }
    }

    internal static void ApplyModuleActivation(bool enabled)
    {
        if (!initialized || currentConfig == null) return;
        if (!enabled)
        {
            lifecycle?.Dispose();
            lifecycle = null;
            activeAdventureId = "";
            return;
        }

        lifecycle ??= AuraBattleLifecycleRouter.Register(
            currentConfig,
            AuraToolsIds.ModId,
            Owner,
            new AuraBattleLifecycleSubscription
            {
                BattleInitializing = _ =>
                    CaptureCheckpoint("battle-start", "进入战斗"),
                BattleSettling = outcome => CaptureCheckpoint(
                    "battle-end",
                    "战斗结束",
                    DamageMeterSettlementRuntime.FightResult(
                        outcome.NativeContext)),
                BattleEnded = _ => CaptureSnapshot("battle-settled")
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn);
    }

    private static void BeginNewAdventure()
    {
        if (!Enabled) return;
        Run("begin adventure", () =>
        {
            if (!AuraToolsDamageMeterRuntime.Enabled)
            {
                DamageMeterNetworkRuntime.BeginAdventure();
            }

            var candidate = DamageMeterNetworkRuntime.CurrentAdventureId;
            var existing = AdventureArchiveStorage.Database.Load(candidate);
            if (existing?.Record.Status == "complete")
            {
                DamageMeterNetworkRuntime.BeginAdventure();
                candidate = DamageMeterNetworkRuntime.CurrentAdventureId;
            }

            activeAdventureId = candidate;
            lastCompletedAdventureId = "";
            BeginRecord("start-game");
            AppendEvent("adventure-start", "冒险开始", ResolveModeId());
            CaptureSnapshot("start-game");
            AdventureArchiveStorage.Database.Prune(AuraToolsConfigService.AdventureArchive.MaximumAdventures);
            RefreshCount();
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
        });
    }

    private static bool EnsureActive(string stage)
    {
        if (!string.IsNullOrWhiteSpace(activeAdventureId)) return true;
        var candidate = DamageMeterNetworkRuntime.CurrentAdventureId;
        if (string.Equals(candidate, lastCompletedAdventureId, StringComparison.Ordinal)) return false;
        var existing = AdventureArchiveStorage.Database.Load(candidate);
        if (existing?.Record.Status == "complete")
        {
            DamageMeterNetworkRuntime.BeginAdventure();
            candidate = DamageMeterNetworkRuntime.CurrentAdventureId;
        }
        activeAdventureId = candidate;
        BeginRecord(stage);
        return true;
    }

    private static void BeginRecord(string stage)
    {
        var role = ResolveRoleId();
        AdventureArchiveStorage.Database.Begin(new AdventureArchiveRecord
        {
            AdventureId = activeAdventureId,
            StartedUtc = DateTime.UtcNow.ToString("O"),
            ModeId = ResolveModeId(),
            RoleId = role,
            GameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
            ToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
            ModFingerprint = MatchReplayRecorder.CurrentRuntimeFingerprint(),
            LatestStage = stage
        });
    }

    private static void CaptureCheckpoint(string kind, string title, string detail = "")
    {
        if (!Enabled) return;
        Run(kind, () =>
        {
            if (!EnsureActive(kind)) return;
            AppendEvent(kind, title, detail);
            CaptureSnapshot(kind);
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
        });
    }

    private static void CaptureReward(ModHookContext context)
    {
        if (!Enabled) return;
        var reward = context.Arguments?.OfType<IDataConfig>().Select(ReadDataId).FirstOrDefault(id => id.Length > 0) ?? "";
        CaptureCheckpoint("reward", "获得奖励", reward);
    }

    private static void CaptureSnapshot(string reason)
    {
        if (!AuraToolsConfigService.AdventureArchive.CaptureSnapshots) return;
        if (!EnsureActive(reason)) return;
        var role = RoleTable.Instance;
        var cards = role?.cardList?.Select(ReadDataId).Where(id => id.Length > 0).ToList() ?? new List<string>();
        var relics = role?.relicList?.Select(ReadDataId).Where(id => id.Length > 0).ToList() ?? new List<string>();
        var stage = ResolveStage(reason);
        var state = new JObject
        {
            ["modeId"] = ResolveModeId(),
            ["playerId"] = PlayerManager.Instance?.PlayerId ?? "single-player",
            ["cardCount"] = cards.Count,
            ["relicCount"] = relics.Count,
            ["mapManager"] = MapManager.Instance?.ModeMapManager?.GetType().Name ?? ""
        };
        AdventureArchiveStorage.Database.AppendSnapshot(activeAdventureId, new AdventureArchiveSnapshot
        {
            OccurredUtc = DateTime.UtcNow.ToString("O"),
            Reason = reason,
            Stage = stage,
            RoleId = ResolveRoleId(),
            CardsJson = JsonConvert.SerializeObject(cards, Formatting.None),
            RelicsJson = JsonConvert.SerializeObject(relics, Formatting.None),
            StateJson = state.ToString(Formatting.None)
        });
    }

    private static void AppendEvent(string kind, string title, string detail = "")
    {
        AdventureArchiveStorage.Database.AppendEvent(activeAdventureId, new AdventureArchiveEvent
        {
            OccurredUtc = DateTime.UtcNow.ToString("O"),
            Kind = kind,
            Title = title,
            Detail = detail ?? "",
            PayloadJson = "{}"
        });
    }

    private static void CompleteFromHook(ModHookContext context)
    {
        Complete(DamageMeterSettlementRuntime.FightResult(context), HookSource(context));
    }

    private static void Complete(string result, string source)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(activeAdventureId)) return;
        Run("complete adventure", () =>
        {
            AppendEvent("adventure-end", "冒险结束", string.IsNullOrWhiteSpace(result) ? source : result);
            CaptureSnapshot("adventure-end");
            AdventureArchiveStorage.Database.Complete(activeAdventureId, result);
            lastCompletedAdventureId = activeAdventureId;
            activeAdventureId = "";
            RefreshCount();
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
        });
    }

    private static string ResolveModeId()
    {
        try
        {
            var mode = AuraModeRuntime.Current(AuraToolsIds.ModId, refresh: true)?.ModeId;
            if (!string.IsNullOrWhiteSpace(mode)) return mode!;
        }
        catch { }
        try { return LobbyManager.Instance?.CurrentLobbyModeType ?? "Normal"; }
        catch { return "Normal"; }
    }

    private static string ResolveRoleId()
    {
        try { return ReadDataId(RoleTable.Instance?.Career); }
        catch { return ""; }
    }

    private static string ResolveStage(string fallback)
    {
        var manager = MapManager.Instance?.ModeMapManager;
        var value = ReflectionUtil.ReadString(manager, "CurrentLevel", "CurLevel", "Level", "MapIndex", "Stage");
        return string.IsNullOrWhiteSpace(value) ? fallback : value!;
    }

    private static string ReadDataId(IDataConfig? value)
    {
        try
        {
            return value?.data != null && value.data.TryGetValue("Id", out var id)
                ? id ?? ""
                : value?.InstanceID ?? "";
        }
        catch { return ""; }
    }

    private static string HookSource(ModHookContext context)
    {
        try { return context.Target?.GetType().Name ?? "unknown"; }
        catch { return "unknown"; }
    }

    private static void Run(string operation, Action action)
    {
        try { action(); }
        catch (Exception ex) { AuraToolsLog.Warn("[AdventureArchive] " + operation + " failed: " + ex.Message); }
    }
}
