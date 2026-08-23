using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using Michsky.MUIP;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
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
    private static RoleTable? observedRole;
    private static AdventureArchiveSnapshot? lastSnapshot;
    private static string lastSnapshotSignature = "";
    private static string pendingSnapshotReason = "";
    private static string activeEventId = "";
    private static int eventOccurrence;
    private static string lastMapIdentity = "";
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
        AuraToolsHookRegistry.After(modConfig, "NormalMapManager.InitRoleTable", _ => ScheduleAdventureReady(), Owner);
        AuraToolsHookRegistry.After(modConfig, "SublimationManager.InitRoleTable", _ => ScheduleAdventureReady(), Owner);
        AuraToolsHookRegistry.After(modConfig, "SlotMachineManager.InitRoleTable", _ => ScheduleAdventureReady(), Owner);
        AuraToolsHookRegistry.After(modConfig, "MapSelectUI.ReadyToSelect", _ => ScheduleSnapshot("map-ready"), Owner);
        AuraToolsHookRegistry.Before(modConfig, "MapManager.CmdNextMap", _ => CaptureMapDeparture(), Owner);
        AuraToolsHookRegistry.After(modConfig, "CardChoiceUI.Select", CaptureReward, Owner);
        AuraToolsHookRegistry.After(modConfig, "ShopItem.TryBuy", _ => ScheduleSnapshot("shop"), Owner);
        AuraToolsHookRegistry.After(modConfig, "EventUI.Init", CaptureEventStart, Owner);
        AuraToolsHookRegistry.After(modConfig, "EventUI.Entry", _ => ScheduleSnapshot("event-complete"), Owner);
        AuraToolsHookRegistry.Before(modConfig, "GameApp.GameOver", CompleteFromHook, Owner);
        AuraToolsHookRegistry.Before(modConfig, "PlayerManager.GameOver", CompleteFromHook, Owner);
        AuraToolsHookRegistry.After(modConfig, "GameExitUI.Start", context => Complete("Exited", HookSource(context)), Owner);
        ApplyModuleActivation(Enabled);
        if (!Enabled) return;
        Run("initialize", () =>
        {
            AdventureArchiveStorage.Database.Prune(AuraToolsConfigService.AdventureArchive.MaximumAdventures);
            RefreshCount();
        });
    }

    private static void OnConfigChanged()
    {
        ApplyModuleActivation(Enabled);
        if (!Enabled) return;
        Run("apply configuration", () =>
        {
            AdventureArchiveStorage.Database.Prune(AuraToolsConfigService.AdventureArchive.MaximumAdventures);
            RefreshCount();
        });
    }

    internal static void ApplyModuleActivation(bool enabled)
    {
        if (!initialized || currentConfig == null) return;
        if (!enabled)
        {
            lifecycle?.Dispose();
            lifecycle = null;
            UnbindRoleTable();
            ResetActiveState();
            return;
        }

        lifecycle ??= AuraBattleLifecycleRouter.Register(
            currentConfig,
            AuraToolsIds.ModId,
            Owner,
            new AuraBattleLifecycleSubscription
            {
                BattleInitializing = _ => CaptureBattleStart(),
                BattleSettling = outcome => CaptureBattleEnd(
                    DamageMeterSettlementRuntime.FightResult(outcome.NativeContext)),
                BattleEnded = _ => ScheduleSnapshot("battle-settled")
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn);
    }

    private static void BeginNewAdventure()
    {
        if (!Enabled) return;
        Run("begin adventure", () =>
        {
            UnbindRoleTable();
            ResetCaptureState();
            if (!AuraToolsDamageMeterRuntime.Enabled) DamageMeterNetworkRuntime.BeginAdventure();

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
            var modeId = ResolveModeId();
            AppendEvent(
                "adventure-start",
                "冒险开始",
                AuraToolsPlayerDisplay.ModeName(modeId),
                new JObject { ["modeId"] = modeId },
                "adventure-start");
            AdventureArchiveStorage.Database.Prune(AuraToolsConfigService.AdventureArchive.MaximumAdventures);
            RefreshCount();
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
        });
    }

    private static bool EnsureActive(string stage)
    {
        if (!string.IsNullOrWhiteSpace(activeAdventureId)) return true;
        var candidate = DamageMeterNetworkRuntime.CurrentAdventureId;
        if (string.IsNullOrWhiteSpace(candidate)
            || string.Equals(candidate, lastCompletedAdventureId, StringComparison.Ordinal)) return false;
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
        var roleId = ResolveRoleId();
        var modeId = ResolveModeId();
        AdventureArchiveStorage.Database.Begin(new AdventureArchiveRecord
        {
            AdventureId = activeAdventureId,
            StartedUtc = DateTime.UtcNow.ToString("O"),
            ModeId = modeId,
            ModeName = AuraToolsPlayerDisplay.ModeName(modeId),
            RoleId = roleId,
            RoleName = AuraToolsPlayerDisplay.RoleName(roleId),
            GameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
            ToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
            ModFingerprint = MatchReplayRecorder.CurrentRuntimeFingerprint(),
            LatestStage = stage
        });
    }

    private static void ScheduleAdventureReady()
    {
        if (!Enabled || !EnsureActive("adventure-ready")) return;
        AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "adventure-archive-ready:" + activeAdventureId,
            Source = "AdventureArchive.AdventureReady",
            Action = BindRoleTableAndCaptureInitialState
        });
    }

    private static void BindRoleTableAndCaptureInitialState()
    {
        if (!Enabled || !EnsureActive("adventure-ready")) return;
        UnbindRoleTable();
        observedRole = RoleTable.Instance;
        if (observedRole == null)
        {
            ScheduleSnapshot("adventure-ready");
            return;
        }
        observedRole.cardList.CollectionChanged += OnInventoryChanged;
        observedRole.UnCardList.CollectionChanged += OnInventoryChanged;
        observedRole.relicList.CollectionChanged += OnInventoryChanged;
        observedRole.WithoutArmedRelicList.CollectionChanged += OnInventoryChanged;
        observedRole.blessingConfigs.CollectionChanged += OnInventoryChanged;
        observedRole.PropertyChanged += OnRolePropertyChanged;
        BeginRecord("adventure-ready");
        var snapshot = CaptureSnapshot("adventure-ready", emitChanges: false);
        AppendEvent(
            "adventure-ready",
            "冒险准备完成",
            "卡牌 " + AdventureArchiveProjection.ReadEntries(snapshot?.CardsJson ?? "[]", "牌组").Sum(item => item.Count)
            + " · 遗物 " + AdventureArchiveProjection.ReadEntries(snapshot?.RelicsJson ?? "[]", "遗物").Sum(item => item.Count),
            new JObject { ["roleId"] = ResolveRoleId() },
            "adventure-ready");
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
    }

    private static void OnInventoryChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ScheduleSnapshot("inventory-change");
    }

    private static void OnRolePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.PropertyName)
            || args.PropertyName == "Money"
            || args.PropertyName == "San"
            || args.PropertyName == "relicList")
        {
            ScheduleSnapshot("resource-change");
        }
    }

    private static void ScheduleSnapshot(string reason)
    {
        if (!Enabled || !EnsureActive(reason)) return;
        pendingSnapshotReason = PreferReason(pendingSnapshotReason, reason);
        AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "adventure-archive-snapshot:" + activeAdventureId,
            Source = "AdventureArchive." + reason,
            Action = () =>
            {
                var pending = pendingSnapshotReason;
                pendingSnapshotReason = "";
                CaptureSnapshot(string.IsNullOrWhiteSpace(pending) ? reason : pending, emitChanges: true);
            }
        });
    }

    private static AdventureArchiveSnapshot? CaptureSnapshot(
        string reason,
        bool emitChanges,
        string stageOverride = "")
    {
        if (!Enabled || !EnsureActive(reason)) return null;
        var current = BuildSnapshot(reason, stageOverride);
        var signature = AdventureArchiveProjection.Signature(current);
        if (emitChanges && lastSnapshot != null)
        {
            AppendDiffEvents(AdventureArchiveProjection.Diff(lastSnapshot, current), reason);
        }
        lastSnapshot = current;
        if (string.Equals(signature, lastSnapshotSignature, StringComparison.Ordinal)) return current;
        lastSnapshotSignature = signature;
        AdventureArchiveStorage.Database.AppendSnapshot(activeAdventureId, current);
        BeginRecord(current.Stage);
        return current;
    }

    private static AdventureArchiveSnapshot BuildSnapshot(string reason, string stageOverride)
    {
        var role = RoleTable.Instance;
        var cards = ReadContent(role?.cardList, DataType.Card, "当前卡组")
            .Concat(ReadContent(role?.UnCardList, DataType.Card, "卡牌背包"));
        var relics = ReadContent(role?.relicList, DataType.Relic, "已装备")
            .Concat(ReadContent(role?.WithoutArmedRelicList, DataType.Relic, "遗物背包"));
        var blessings = ReadContent(role?.blessingConfigs, DataType.Bless, "祝福");
        var node = CurrentNode();
        var state = new JObject
        {
            ["modeId"] = ResolveModeId(),
            ["playerId"] = PlayerManager.Instance?.PlayerId ?? "single-player",
            ["money"] = SafeMoney(role),
            ["sanity"] = role?.San ?? 0,
            ["maximumSanity"] = role?.MaxSan ?? 0,
            ["level"] = ResolveLevel(),
            ["nodeId"] = NodeField(node, "NodeId"),
            ["nodeType"] = NodeField(node, "Type"),
            ["nodeName"] = NodeName(node)
        };
        return new AdventureArchiveSnapshot
        {
            OccurredUtc = DateTime.UtcNow.ToString("O"),
            Reason = reason,
            Stage = string.IsNullOrWhiteSpace(stageOverride) ? ResolveStage(reason) : stageOverride,
            RoleId = ResolveRoleId(),
            CardsJson = AdventureArchiveProjection.SerializeEntries(cards),
            RelicsJson = AdventureArchiveProjection.SerializeEntries(relics),
            BlessingsJson = AdventureArchiveProjection.SerializeEntries(blessings),
            StateJson = state.ToString(Formatting.None)
        };
    }

    private static IEnumerable<AdventureArchiveContentEntry> ReadContent(
        IEnumerable<IDataConfig>? values,
        DataType type,
        string zone)
    {
        foreach (var value in values ?? Array.Empty<IDataConfig>())
        {
            var id = ReadDataId(value);
            if (id.Length == 0) continue;
            AuraGameDataSnapshot? definition = null;
            try { definition = AuraGameDataHostApi.Resolve(type, id); } catch { }
            yield return new AdventureArchiveContentEntry
            {
                Id = definition?.Id ?? id,
                OwnerModId = definition?.OwnerModId ?? "",
                DisplayName = DisplayName(type, id),
                Zone = zone,
                Count = 1
            };
        }
    }

    private static void AppendDiffEvents(AdventureArchiveSnapshotDiff diff, string reason)
    {
        if (!diff.HasChanges) return;
        AppendContentDelta("card-change", "卡牌变化", diff.Cards, reason);
        AppendContentDelta("relic-change", "遗物变化", diff.Relics, reason);
        AppendContentDelta("blessing-change", "祝福变化", diff.Blessings, reason);
        if (diff.MoneyDelta != 0)
        {
            AppendEvent("resource-change", "金币变化", Signed(diff.MoneyDelta) + " 金币",
                new JObject { ["moneyDelta"] = diff.MoneyDelta, ["reason"] = reason });
        }
    }

    private static void AppendContentDelta(
        string kind,
        string title,
        IReadOnlyCollection<AdventureArchiveContentDelta> values,
        string reason)
    {
        if (values.Count == 0) return;
        var detail = string.Join("，", values.Select(value =>
            Signed(value.Delta) + " " + Display(value.Entry)));
        AppendEvent(kind, title, detail, new JObject
        {
            ["reason"] = reason,
            ["changes"] = JArray.FromObject(values.Select(value => new
            {
                value.Entry.Id,
                value.Entry.OwnerModId,
                value.Entry.DisplayName,
                value.Entry.Zone,
                value.Delta
            }))
        });
    }

    private static void CaptureMapDeparture()
    {
        if (!Enabled || !EnsureActive("map-node")) return;
        var node = CurrentNode();
        var name = NodeName(node);
        var identity = ResolveLevel() + "|" + NodeField(node, "NodeId") + "|" + NodeField(node, "Id");
        if (identity == lastMapIdentity) return;
        lastMapIdentity = identity;
        AppendEvent("map", "前往 " + (string.IsNullOrWhiteSpace(name) ? "下一地点" : name),
            NodeTypeLabel(NodeField(node, "Type")), NodePayload(node), "map:" + identity);
        CaptureSnapshot("map-node", emitChanges: true, stageOverride: name);
    }

    private static void CaptureEventStart(ModHookContext context)
    {
        if (!Enabled || !EnsureActive("event")) return;
        activeEventId = context.Arguments?.OfType<string>().FirstOrDefault() ?? "";
        eventOccurrence++;
        var name = EventName(activeEventId);
        AppendEvent("event", "触发事件", name,
            new JObject { ["eventId"] = activeEventId, ["eventName"] = name },
            "event-start:" + eventOccurrence);
        AttachEventChoiceObservers(context.Target as UnityEngine.Component);
    }

    private static void AttachEventChoiceObservers(UnityEngine.Component? eventUi)
    {
        if (eventUi == null) return;
        var config = ReflectionUtil.GetMemberValue(eventUi, "dataConfig") as IDataConfig;
        for (var option = 1; option <= 4; option++)
        {
            var index = option.ToString(CultureInfo.InvariantCulture);
            var root = eventUi.transform.Find("Windows/Map0/Content/Selector/option" + option);
            var manager = root?.GetComponent<ButtonManager>();
            if (manager == null) continue;
            manager.onClick.AddListener(() => CaptureEventChoice(config, index));
        }
    }

    private static void CaptureEventChoice(IDataConfig? config, string index)
    {
        if (!Enabled || !EnsureActive("event-choice")) return;
        var eventId = ReadDataId(config);
        if (eventId.Length == 0) eventId = activeEventId;
        var eventName = EventName(eventId);
        var choice = CleanRichText(LocalizedField(config, index + "Describe"));
        AppendEvent("event-choice", "事件选择", eventName + (choice.Length == 0 ? "" : " · " + choice),
            new JObject
            {
                ["eventId"] = eventId,
                ["eventName"] = eventName,
                ["choiceIndex"] = index,
                ["choiceText"] = choice
            },
            "event-choice:" + eventOccurrence + ":" + index);
        ScheduleSnapshot("event-choice");
    }

    private static void CaptureReward(ModHookContext context)
    {
        if (!Enabled || !EnsureActive("reward")) return;
        var reward = context.Arguments?.OfType<IDataConfig>().Select(ReadDataId)
            .FirstOrDefault(id => id.Length > 0) ?? "";
        AppendEvent("reward", "选择卡牌奖励", AuraToolsPlayerDisplay.CardName(reward),
            new JObject { ["cardId"] = reward, ["cardName"] = AuraToolsPlayerDisplay.CardName(reward) });
        ScheduleSnapshot("reward");
    }

    private static void CaptureBattleStart()
    {
        if (!Enabled || !EnsureActive("battle-start")) return;
        var node = CurrentNode();
        AppendEvent("battle", "进入战斗", NodeName(node), NodePayload(node));
        CaptureSnapshot("battle-start", emitChanges: true);
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
    }

    private static void CaptureBattleEnd(string result)
    {
        if (!Enabled || !EnsureActive("battle-end")) return;
        AppendEvent("battle", "战斗结束", AuraToolsPlayerDisplay.BattleResult(result),
            new JObject { ["result"] = result });
        CaptureSnapshot("battle-end", emitChanges: true);
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
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
            CaptureSnapshot("adventure-end", emitChanges: true);
            AppendEvent("adventure-end", "冒险结束", AuraToolsPlayerDisplay.BattleResult(result),
                new JObject { ["result"] = result, ["source"] = source }, "adventure-end");
            AdventureArchiveStorage.Database.Complete(activeAdventureId, result);
            lastCompletedAdventureId = activeAdventureId;
            UnbindRoleTable();
            ResetActiveState();
            RefreshCount();
            AuraToolModuleHost.RefreshState(AuraToolModuleIds.AdventureArchive);
        });
    }

    private static void AppendEvent(
        string kind,
        string title,
        string detail,
        JObject payload,
        string dedupeKey = "")
    {
        payload["schemaVersion"] = AdventureArchiveSchema.CurrentVersion;
        AdventureArchiveStorage.Database.AppendEvent(activeAdventureId, new AdventureArchiveEvent
        {
            OccurredUtc = DateTime.UtcNow.ToString("O"),
            Kind = kind,
            Title = title,
            Detail = detail ?? "",
            PayloadJson = payload.ToString(Formatting.None),
            DedupeKey = dedupeKey
        });
    }

    private static JObject NodePayload(object? node)
    {
        return new JObject
        {
            ["level"] = ResolveLevel(),
            ["mapId"] = NodeField(node, "Id"),
            ["nodeId"] = NodeField(node, "NodeId"),
            ["nodeType"] = NodeField(node, "Type"),
            ["nodeName"] = NodeName(node)
        };
    }

    private static object? CurrentNode()
    {
        try { return MapManager.Instance?.MapTree?.currentNode; }
        catch { return null; }
    }

    private static string NodeField(object? node, string field)
    {
        var data = ReflectionUtil.AsStringDictionary(ReflectionUtil.GetMemberValue(node, "data"));
        return data != null && data.TryGetValue(field, out var value) ? value?.Trim() ?? "" : "";
    }

    private static string NodeName(object? node)
    {
        var data = ReflectionUtil.AsStringDictionary(ReflectionUtil.GetMemberValue(node, "data"));
        if (data == null) return "";
        try
        {
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, "Name", StringComparison.OrdinalIgnoreCase))
                return localized.Trim();
        }
        catch { }
        return data.TryGetValue("Name", out var name) ? name?.Trim() ?? "" : "";
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
        var nodeName = NodeName(CurrentNode());
        if (nodeName.Length > 0) return nodeName;
        var manager = MapManager.Instance?.ModeMapManager;
        var value = ReflectionUtil.ReadString(manager, "CurrentLevel", "CurLevel", "Level", "MapIndex", "Stage");
        return string.IsNullOrWhiteSpace(value) ? fallback : value!;
    }

    private static int ResolveLevel()
    {
        var value = ReflectionUtil.GetMemberValue(MapManager.Instance?.ModeMapManager, "Level");
        try { return Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static int SafeMoney(RoleTable? role)
    {
        try
        {
            var value = ReflectionUtil.GetMemberValue(role, "Money");
            return value == null ? 0 : Convert.ToInt32(value);
        }
        catch { return 0; }
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

    private static string DisplayName(DataType type, string id)
    {
        if (type == DataType.Card) return AuraToolsPlayerDisplay.CardName(id);
        if (type == DataType.Relic) return AuraToolsPlayerDisplay.RelicName(id);
        if (type == DataType.Bless) return AuraToolsPlayerDisplay.BlessingName(id);
        return "内容";
    }

    private static string Display(AdventureArchiveContentEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.DisplayName) ? "已失效内容" : entry.DisplayName;
    }

    private static string EventName(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return "事件";
        try
        {
            var row = AuraGameDataHostApi.CopyRow(DataType.Event, eventId);
            if (row != null)
            {
                var localized = row.Localize("Name");
                if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, "Name", StringComparison.OrdinalIgnoreCase))
                    return localized.Trim();
            }
        }
        catch { }
        return "事件";
    }

    private static string LocalizedField(IDataConfig? config, string field)
    {
        try { return config?.data?.Localize(field) ?? ""; }
        catch { return ""; }
    }

    private static string CleanRichText(string value)
    {
        return Regex.Replace(value ?? "", "<[^>]+>", " ")
            .Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string NodeTypeLabel(string type)
    {
        var normalized = (type ?? "").Trim().ToLowerInvariant();
        if (normalized.Contains("battle") || normalized.Contains("enemy")) return "战斗地点";
        if (normalized.Contains("event")) return "事件地点";
        if (normalized.Contains("shop")) return "商店";
        if (normalized.Contains("rest")) return "休整地点";
        return "冒险地点";
    }

    private static string PreferReason(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(current)) return next;
        if (next == "reward" || next == "shop" || next == "event-choice" || next == "battle-settled") return next;
        return current;
    }

    private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

    private static string HookSource(ModHookContext context)
    {
        try { return context.Target?.GetType().Name ?? "unknown"; }
        catch { return "unknown"; }
    }

    private static void UnbindRoleTable()
    {
        if (observedRole == null) return;
        observedRole.cardList.CollectionChanged -= OnInventoryChanged;
        observedRole.UnCardList.CollectionChanged -= OnInventoryChanged;
        observedRole.relicList.CollectionChanged -= OnInventoryChanged;
        observedRole.WithoutArmedRelicList.CollectionChanged -= OnInventoryChanged;
        observedRole.blessingConfigs.CollectionChanged -= OnInventoryChanged;
        observedRole.PropertyChanged -= OnRolePropertyChanged;
        observedRole = null;
    }

    private static void ResetCaptureState()
    {
        lastSnapshot = null;
        lastSnapshotSignature = "";
        pendingSnapshotReason = "";
        activeEventId = "";
        eventOccurrence = 0;
        lastMapIdentity = "";
    }

    private static void ResetActiveState()
    {
        activeAdventureId = "";
        ResetCaptureState();
    }

    private static void Run(string operation, Action action)
    {
        try { action(); }
        catch (Exception ex) { AuraToolsLog.Warn("[AdventureArchive] " + operation + " failed: " + ex.Message); }
    }
}
