using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryModeRuntime
{
    private const int LegacySolarFinaleMapLevel = 30;
    private const int SolarMemoryOpeningSlotIndex = 0;
    private const int SolarMemoryMidLayerSlotIndex = 3;
    private static bool handlingSolarMemoryFightAbort;

    public static void Initialize(ModConfig modConfig)
    {
        SolarMemoryModeEntryRuntime.Initialize(modConfig);
        SolarMemoryMapVisualRuntime.Initialize(modConfig);
        RegisterBefore(modConfig, "GameConfigManager.CardPackCheck", FilterSolarMemoryCardPackCheck);
        RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RewriteSolarMemoryMap);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcNextMap", EnsureSolarMemoryCurrentNodeBeforeNextMap);
        RegisterAfter(modConfig, "MapManager.RpcNextMap", SyncSolarMemoryClientLastNodeAfterNextMap);
        RegisterBefore(modConfig, "NormalMapManager.MapItemInit", SettleLegacyTerminalLevelBeforeMapItems);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", SettleSolarMemoryBossAfterWin);
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", PrepareSolarMemoryFightAbort);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", SettleSolarMemoryFightAbort);
        RegisterAfter(modConfig, "Fight_Loss.Init", SettleSolarMemoryFightLoss);
        RegisterBefore(modConfig, "NormalMapManager.ReadyToChangeMap", FinishSolarMemoryAfterFinalLayer);
    }

    public static void OpenOriginWindow()
    {
        try
        {
            SolarMemoryPreparationRuntime.StartOrResume();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory origin window failed", ex);
        }
    }

    public static void OpenBlessingWindow()
    {
        try
        {
            SolarMemoryPreparationRuntime.StartOrResume();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory blessing window failed", ex);
        }
    }

    public static void OpenDeckWindow()
    {
        try
        {
            if (RoleTable.Instance == null)
            {
                return;
            }

            if (!SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryDeckConfiguredKey))
            {
                ClearSolarMemoryReservePool();
            }
            else
            {
                SanitizeSolarMemoryRoleCards(RoleTable.Instance, "OpenDeckWindow");
            }

            var ui = UIManager.Instance.ShowUI<OutDeckUI>("OutDeckUI", true);
            ui.SetRole(new OutDeckUIData(RoleTable.Instance));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory deck window failed", ex);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Solar memory " + message));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Solar memory " + message));
    }

    private static void CaptureSolarMemoryGenerationState(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not NormalMapManager manager)
            {
                SolarMemoryMapNodePoolApplier.ResetGenerationCapture();
                return;
            }

            SolarMemoryMapNodePoolApplier.CaptureGenerationState(manager);
        }
        catch (Exception ex)
        {
            SolarMemoryMapNodePoolApplier.ResetGenerationCapture();
            SunExpLog.Error("Solar memory map generation capture failed", ex);
        }
    }

    private static void RewriteSolarMemoryMap(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not NormalMapManager manager)
            {
                return;
            }

            EnsureSolarMemoryMapState(manager, "NormalMapManager.GeneratrMap", true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map rewrite failed", ex);
        }
    }

    private static void EnsureSolarMemoryMapBeforeSelect(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            var mapManager = MapManager.Instance;
            if (mapManager?.ModeMapManager is NormalMapManager manager)
            {
                EnsureSolarMemoryMapState(manager, "MapSelectUI.ReadyToSelect", false);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory pre-select map repair failed", ex);
        }
    }

    internal static void ApplySolarMemoryLayerTitle(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            var layer = CurrentSolarMemoryLayer();
            var title = SunExpIds.SolarMemoryLayerNames[Math.Max(0, Math.Min(SunExpIds.SolarMemoryLayerNames.Length - 1, layer))];
            SetTmpText(mapSelect.transform.Find("Title/Text/text"), title);

            var text = mapSelect.transform.Find("Title/Text/text")?.GetComponent<Text>();
            if (text != null)
            {
                text.text = title;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory layer title failed", ex);
        }
    }

    private static bool EnsureSolarMemoryMapState(NormalMapManager manager, string source, bool trimEventRecord)
    {
        return SolarMemoryMapNodePoolApplier.ApplyToCurrentLayer(manager, source, trimEventRecord);
    }

    internal static void ApplySolarMemoryFixedSlotsAfterMapItems(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapSelectUI mapSelect)
            {
                return;
            }

            ApplySolarMemoryFixedSlots(mapSelect, manager, true, "NormalMapManager.MapItemInit");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory fixed slot apply failed", ex);
        }
    }

    internal static void ReapplySolarMemoryFixedSlotLocks(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            if (!HasSolarMemoryCurrentNodeReady()
                && !TryRestoreSolarMemoryCurrentNodeFromMapManager("MapSelectUI.ShowMap"))
            {
                SunExpLog.Debug("[SolarMemoryMapLock] skipped fixed slot apply from MapSelectUI.ShowMap: current node is not ready.");
                return;
            }

            ApplySolarMemoryFixedSlots(mapSelect, MapManager.Instance?.ModeMapManager as NormalMapManager, false, "MapSelectUI.ShowMap");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory fixed slot lock repair failed", ex);
        }
    }

    private static void ApplySolarMemoryFixedSlots(MapSelectUI mapSelect, NormalMapManager? manager, bool sync, string source)
    {
        if (manager == null)
        {
            return;
        }

        var nodes = TryGetMapSelectNodes(mapSelect, source);
        if (nodes == null || nodes.Length == 0)
        {
            return;
        }

        var layer = SolarMemoryLayer(manager);
        var changed = false;
        foreach (var spec in FixedNodeSpecs(layer))
        {
            if (spec.SlotIndex < 0 || spec.SlotIndex >= nodes.Length)
            {
                continue;
            }

            var data = CreateFixedNodeData(spec);
            if (data == null)
            {
                continue;
            }

            var node = nodes[spec.SlotIndex];
            if (node == null)
            {
                continue;
            }

            node.data = data;
            node.NodeDice ??= Dice.Default;
            EnsureFixedSlotVisual(mapSelect, spec.SlotIndex, node, data);
            changed = true;
        }

        if (sync && changed)
        {
            mapSelect.SendNode();
            SunExpLog.Info("[SolarMemoryMapLock] fixed slots applied from " + source + "; layer=" + layer + ".");
        }
    }

    private static MapTree.Node[]? TryGetMapSelectNodes(MapSelectUI mapSelect, string source)
    {
        try
        {
            return mapSelect.GetNodes();
        }
        catch (Exception ex)
        {
            var message = "[SolarMemoryMapLock] skipped fixed slot apply from "
                + source
                + ": map nodes unavailable ("
                + ex.GetType().Name
                + ": "
                + ex.Message
                + ").";
            if (IsClientOnlyPlayer())
            {
                SunExpLog.Debug(message);
            }
            else
            {
                SunExpLog.Warn(message);
            }

            return null;
        }
    }

    private static IEnumerable<SolarMemoryFixedNodeSpec> FixedNodeSpecs(int layer)
    {
        var normalizedLayer = ClampSolarMemoryLayer(layer);
        yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryOpeningSlotIndex, normalizedLayer, SolarMemoryOpeningSlotIndex);

        switch (normalizedLayer)
        {
            case 0:
                yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryMapNodePoolFactory.EndingSlotIndex, normalizedLayer, SolarMemoryMapNodePoolFactory.EndingSlotIndex);
                break;
            case 1:
                yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryMidLayerSlotIndex, normalizedLayer, SolarMemoryMidLayerSlotIndex);
                yield return SolarMemoryFixedNodeSpec.Boss(SolarMemoryMapNodePoolFactory.EndingSlotIndex, normalizedLayer, SunExpIds.SolarBossOrbitMirrorMapId, SunExpIds.SolarBossOrbitMirrorLevelId);
                break;
            case 2:
                yield return SolarMemoryFixedNodeSpec.Event(SolarMemoryMidLayerSlotIndex, normalizedLayer, SolarMemoryMidLayerSlotIndex);
                yield return SolarMemoryFixedNodeSpec.Boss(SolarMemoryMapNodePoolFactory.PenultimateSlotIndex, normalizedLayer, SunExpIds.SolarBossSecondSunMapId, SunExpIds.SolarBossSecondSunLevelId);
                yield return SolarMemoryFixedNodeSpec.Boss(SolarMemoryMapNodePoolFactory.EndingSlotIndex, normalizedLayer, SunExpIds.SolarBossSaintWunaMapId, SunExpIds.SolarBossSaintWunaLevelId);
                break;
        }
    }

    private static Dictionary<string, string>? CreateFixedNodeData(SolarMemoryFixedNodeSpec spec)
    {
        Dictionary<string, string>? row;
        if (spec.IsEvent)
        {
            var eventIndex = SolarMemoryEventIndex(spec.Layer, spec.MapSlotIndex);
            var mapId = SunExpIds.SolarMemoryMapIds[eventIndex];
            var shortMapId = SunExpIds.SolarMemoryShortMapIds[eventIndex];
            row = MapRow(mapId) ?? MapRow(shortMapId);
            var data = row == null ? new Dictionary<string, string>() : new Dictionary<string, string>(row);
            data["Id"] = mapId;
            data["Type"] = "Event";
            data["NodeId"] = SunExpIds.SolarMemoryFullEventIds[eventIndex];
            data["Level"] = "-1";
            return data;
        }

        row = MapRow(spec.MapId);
        if (row == null)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] missing map row: " + spec.MapId);
            return null;
        }

        var bossData = new Dictionary<string, string>(row);
        bossData["Id"] = spec.MapId;
        bossData["Type"] = "Fight";
        bossData["NodeId"] = spec.NodeId;
        bossData["Level"] = "-1";
        return bossData;
    }

    private static Dictionary<string, string>? MapRow(string mapId)
    {
        return Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, mapId)
            ?? Singleton<GameConfigManager>.Instance.GetTable(DataType.Map).Getlines()
                .FirstOrDefault(row => string.Equals(Field(row, "Id"), mapId, StringComparison.Ordinal)
                    || string.Equals("SunExp_sunexp_" + Field(row, "Id"), mapId, StringComparison.Ordinal));
    }

    private static void EnsureFixedSlotVisual(MapSelectUI mapSelect, int slotIndex, MapTree.Node node, IDictionary<string, string> data)
    {
        var slot = MapSlotTransform(mapSelect, slotIndex);
        var content = slot?.Find("Content");
        if (slot == null || content == null)
        {
            return;
        }

        foreach (var existing in content.GetComponentsInChildren<MapItem>(true))
        {
            UnityEngine.Object.Destroy(existing.gameObject);
        }

        var nullSlot = content.Find("Null");
        if (nullSlot != null)
        {
            nullSlot.gameObject.SetActive(false);
        }

        var prefabName = Field(data, "Type") + "Prefab";
        var template = mapSelect.transform.Find("MapSelect/" + prefabName);
        if (template == null)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] missing map prefab: " + prefabName);
            return;
        }

        var fixedItem = UnityEngine.Object.Instantiate(template.gameObject, content);
        fixedItem.name = prefabName;
        fixedItem.transform.localScale = Vector3.one;
        fixedItem.SetActive(true);

        var item = fixedItem.GetComponent<MapItem>() ?? fixedItem.AddComponent<MapItem>();
        item.Init(node);
        ApplyMapCardTexture(fixedItem.transform, data);

        if (fixedItem.TryGetComponent<ObjectGroup>(out var objectGroup))
        {
            objectGroup.blocksRaycasts = false;
        }

        var frame = slot.Find("Frame");
        if (frame != null && !HasChain(frame))
        {
            var chain = mapSelect.transform.Find("Chain");
            if (chain != null)
            {
                UnityEngine.Object.Instantiate(chain.gameObject, frame).SetActive(true);
            }
        }
    }

    private static Transform? MapSlotTransform(MapSelectUI mapSelect, int slotIndex)
    {
        var root = mapSelect.transform.Find("Map/NodeContent");
        if (root == null)
        {
            return null;
        }

        if (slotIndex == 0)
        {
            return root.Find("Start");
        }

        if (slotIndex == SolarMemoryMapNodePoolFactory.EndingSlotIndex)
        {
            return root.Find("End");
        }

        return root.Find("Node" + slotIndex);
    }

    private static bool HasChain(Transform frame)
    {
        foreach (Transform child in frame)
        {
            if (child.name.StartsWith("Chain", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyMapCardTexture(Transform item, IDictionary<string, string> data)
    {
        var background = item.Find("Front/background")?.GetComponent<MeshRenderer>();
        if (background == null)
        {
            return;
        }

        var type = Field(data, "Type");
        if (type == "Event")
        {
            var texturePath = VisualRegistry.TexturePath("solar_memory.event_map_card") ?? "";
            Texture? customTexture = null;
            if (!string.IsNullOrWhiteSpace(texturePath))
            {
                customTexture = LoadMapCardTexture(texturePath);
            }

            if (customTexture != null)
            {
                var icon = item.Find("Front/icon");
                if (icon != null)
                {
                    icon.gameObject.SetActive(false);
                }

                background.material.mainTexture = customTexture;
                return;
            }

            background.material.mainTexture = ResourceLoader.Load<Texture>("Icon/CardTemplate/故事牌", true);
        }
        else if (type == "Build")
        {
            background.material.mainTexture = ResourceLoader.Load<Texture>("Icon/CardTemplate/建筑牌", true);
        }
    }

    private static Texture? LoadMapCardTexture(string path)
    {
        try
        {
            return ResourceLoader.Load<Texture>(path, true);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] failed to load map card texture " + path + ": " + ex.Message);
            return null;
        }
    }

    private static bool RewriteSolarMemoryDefaultLayer(MapTree tree, int layer)
    {
        var defaultSegmentSize = DefaultLayerSegmentSize();
        var defaultStart = layer * defaultSegmentSize;
        if (defaultStart < 0 || defaultStart >= tree.DefaultNode.Count)
        {
            return false;
        }

        var changed = false;
        tree.DefaultNode[defaultStart] = CreateSolarMemoryEventNode(tree, layer, SolarMemoryOpeningSlotIndex);
        changed = true;

        var defaultEnd = Math.Min(tree.DefaultNode.Count, defaultStart + defaultSegmentSize);
        for (var i = defaultStart + 1; i < defaultEnd; i++)
        {
            tree.DefaultNode[i] = CreateBossChainNode(tree, i - defaultStart, layer);
            changed = true;
        }

        return changed;
    }

    private static bool RewriteSolarMemorySelectLayer(MapTree tree, int layer)
    {
        var selectSegmentSize = SelectLayerSegmentSize();
        var selectStart = layer * selectSegmentSize;
        if (selectStart < 0 || selectStart >= tree.SelectNode.Count)
        {
            return false;
        }

        var changed = false;
        var selectEnd = Math.Min(tree.SelectNode.Count, selectStart + selectSegmentSize);
        for (var i = selectStart; i < selectEnd; i++)
        {
            var indexInSegment = i - selectStart;
            if (indexInSegment == SolarMemoryMidLayerSlotIndex)
            {
                tree.SelectNode[i] = CreateSolarMemoryEventNode(tree, layer, SolarMemoryMidLayerSlotIndex);
                changed = true;
                continue;
            }

            if (IsBreakNode(tree.SelectNode[i]))
            {
                continue;
            }

            tree.SelectNode[i] = CreateBossChainNode(tree, indexInSegment, layer);
            changed = true;
        }

        return changed;
    }

    private static int SolarMemoryLayer(NormalMapManager manager)
    {
        return ClampSolarMemoryLayer(manager.Level / 6);
    }

    private static int ClampSolarMemoryLayer(int layer)
    {
        return Math.Max(0, Math.Min(SunExpIds.SolarMemoryMaxLayer - 1, layer));
    }

    private static int CurrentSolarMemoryLayer()
    {
        if (MapManager.Instance?.ModeMapManager is not NormalMapManager manager)
        {
            return 0;
        }

        return SolarMemoryLayer(manager);
    }

    private static int SolarMemoryEventIndex(int layer, int mapSlotIndex)
    {
        var normalizedLayer = ClampSolarMemoryLayer(layer);
        var slot = mapSlotIndex >= SolarMemoryMidLayerSlotIndex ? 1 : 0;
        var index = normalizedLayer * 2 + slot;
        return Math.Max(0, Math.Min(SunExpIds.SolarMemoryFullEventIds.Length - 1, index));
    }

    private static int DefaultLayerSegmentSize()
    {
        return Math.Max(1, 2 + GameSaveManager.GetValue<int>(GameVar.ExLockDes));
    }

    private static int SelectLayerSegmentSize()
    {
        return Math.Max(1, 8 - GameSaveManager.GetValue<int>(GameVar.ExDeleteDes));
    }

    private static void RepairSolarMemoryMapSelection(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is string[] maps && args[i + 1] is string[] mapData)
                {
                    if (RepairSolarMemoryMapArrays(maps, mapData))
                    {
                        SunExpLog.Info("[SolarMemoryMapSync] map selection arrays repaired.");
                    }

                    TryRestoreSolarMemoryCurrentNodeFromSyncArrays(maps, mapData, "MapManager.MapSelectionSync");
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map selection repair failed", ex);
        }
    }

    private static void EnsureSolarMemoryCurrentNodeBeforeNextMap(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            if (MapManager.Instance?.MapTree?.currentNode == null)
            {
                TryRestoreSolarMemoryCurrentNodeFromMapManager("MapManager.RpcNextMap");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] pre-next-map current node repair failed: " + ex.Message);
        }
    }

    private static void PrepareSolarMemoryFightAbort(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            handlingSolarMemoryFightAbort = true;
            EnsureSolarMemoryCurrentNodeForTransition("Fight_Escape.ResetStates:before");
            CloseSolarMemoryTransientUi("Fight_Escape.ResetStates:before");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] prepare failed: " + ex.Message);
        }
    }

    private static void SettleSolarMemoryFightAbort(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                handlingSolarMemoryFightAbort = false;
                return;
            }

            EnsureSolarMemoryCurrentNodeForTransition("Fight_Escape.ResetStates:after");
            CloseSolarMemoryTransientUi("Fight_Escape.ResetStates:after");
            SunExpLog.Info("[SolarMemoryFightAbort] escape/loss branch settled.");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] settle failed: " + ex.Message);
        }
        finally
        {
            handlingSolarMemoryFightAbort = false;
        }
    }

    private static void SettleSolarMemoryFightLoss(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            CloseSolarMemoryTransientUi("Fight_Loss.Init");
            if (!handlingSolarMemoryFightAbort)
            {
                EnsureSolarMemoryCurrentNodeForTransition("Fight_Loss.Init");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] loss settle failed: " + ex.Message);
        }
    }

    private static void SyncSolarMemoryClientLastNodeAfterNextMap(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            var node = MapManager.Instance?.MapTree?.currentNode;
            if (node != null)
            {
                GameSaveManager.UpdateNode(node);
                SunExpLog.Debug("[SolarMemoryMapSync] synced client save node after RpcNextMap.");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] post-next-map save node sync failed: " + ex.Message);
        }
    }

    private static bool RepairSolarMemoryMapArrays(string[] maps, string[] mapData)
    {
        if (maps.Length == 0 || mapData.Length == 0)
        {
            return false;
        }

        var layer = CurrentSolarMemoryLayer();
        var changed = false;
        foreach (var spec in FixedNodeSpecs(layer))
        {
            changed = RepairSolarMemorySyncIndex(maps, mapData, spec) || changed;
        }

        var count = Math.Min(maps.Length, mapData.Length);
        for (var i = 0; i < count; i++)
        {
            if (FixedNodeSpecs(layer).Any(spec => spec.SlotIndex == i))
            {
                continue;
            }

            if (IsSolarMemoryMapId(maps[i]) || IsSolarMemoryEventId(mapData[i]))
            {
                var repairSpec = SolarMemoryFixedNodeSpec.Event(i, layer, i);
                changed = RepairSolarMemorySyncIndex(maps, mapData, repairSpec) || changed;
            }
        }

        return changed;
    }

    private static bool RepairSolarMemorySyncIndex(string[] maps, string[] mapData, SolarMemoryFixedNodeSpec spec)
    {
        if (spec.SlotIndex < 0 || spec.SlotIndex >= maps.Length || spec.SlotIndex >= mapData.Length)
        {
            return false;
        }

        var expectedMapId = spec.MapId;
        var expectedNodeId = spec.NodeId;
        if (spec.IsEvent)
        {
            var eventIndex = SolarMemoryEventIndex(spec.Layer, spec.MapSlotIndex);
            expectedMapId = SunExpIds.SolarMemoryMapIds[eventIndex];
            expectedNodeId = SunExpIds.SolarMemoryFullEventIds[eventIndex];
        }

        var changed = false;
        if (maps[spec.SlotIndex] != expectedMapId)
        {
            maps[spec.SlotIndex] = expectedMapId;
            changed = true;
        }

        if (mapData[spec.SlotIndex] != expectedNodeId)
        {
            mapData[spec.SlotIndex] = expectedNodeId;
            changed = true;
        }

        if (changed)
        {
            SunExpLog.Info("[SolarMemoryMapSync] repaired index="
                + spec.SlotIndex
                + "; layer="
                + spec.Layer
                + "; slot="
                + spec.MapSlotIndex
                + "; map="
                + expectedMapId
                + "; node="
                + expectedNodeId);
        }

        return changed;
    }

    private static void EnsureSolarMemoryCurrentNodeForTransition(string source)
    {
        try
        {
            var mapManager = MapManager.Instance;
            var tree = mapManager?.MapTree;
            if (tree == null)
            {
                return;
            }

            if (IsUsableSolarMemoryMapNode(tree.currentNode))
            {
                EnsureSolarMemoryNodeDice(tree.currentNode, tree, source);
                GameSaveManager.UpdateNode(tree.currentNode);
                return;
            }

            var saveNode = GameSaveManager.GetNode();
            if (IsUsableSolarMemoryMapNode(saveNode))
            {
                EnsureSolarMemoryNodeDice(saveNode, tree, source);
                tree.currentNode = saveNode;
                GameSaveManager.UpdateNode(saveNode);
                SunExpLog.Info("[SolarMemoryMapSync] restored current node from save before transition; source=" + source + ".");
                return;
            }

            if (TryRestoreSolarMemoryCurrentNodeFromMapManager(source, false))
            {
                return;
            }

            if (mapManager?.ModeMapManager is NormalMapManager manager
                && EnsureSolarMemoryMapState(manager, source, false)
                && IsUsableSolarMemoryMapNode(tree.currentNode))
            {
                EnsureSolarMemoryNodeDice(tree.currentNode, tree, source);
                GameSaveManager.UpdateNode(tree.currentNode);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] transition current node repair failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    private static bool TryRestoreSolarMemoryCurrentNodeFromMapManager(string source, bool clientOnly = true)
    {
        var mapManager = MapManager.Instance;
        return mapManager != null
            && TryRestoreSolarMemoryCurrentNodeFromSyncArrays(mapManager.mapList, mapManager.mapData, source, clientOnly);
    }

    private static bool TryRestoreSolarMemoryCurrentNodeFromSyncArrays(string[]? maps, string[]? mapData, string source, bool clientOnly = true)
    {
        try
        {
            if ((clientOnly && !IsClientOnlyPlayer())
                || HasSolarMemoryCurrentNodeReady()
                || maps == null
                || mapData == null)
            {
                return false;
            }

            var tree = MapManager.Instance?.MapTree;
            var count = Math.Min(maps.Length, mapData.Length);
            if (tree == null || count <= 0)
            {
                return false;
            }

            var first = BuildSolarMemorySyncedNodeChain(tree, maps, mapData, count);
            if (first == null)
            {
                return false;
            }

            tree.currentNode = first;
            GameSaveManager.UpdateNode(first);
            SunExpLog.Info("[SolarMemoryMapSync] restored client current node from sync arrays; source="
                + source
                + "; count="
                + count
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] failed to restore client current node from "
                + source
                + ": "
                + ex.Message);
            return false;
        }
    }

    private static MapTree.Node? BuildSolarMemorySyncedNodeChain(MapTree tree, string[] maps, string[] mapData, int count)
    {
        MapTree.Node? first = null;
        MapTree.Node? previous = null;
        for (var i = 0; i < count; i++)
        {
            var node = CreateSolarMemorySyncedNode(tree, maps[i], mapData[i], i);
            if (first == null)
            {
                first = node;
            }
            else
            {
                previous?.SetChild(0, node);
            }

            previous = node;
        }

        return first;
    }

    private static MapTree.Node CreateSolarMemorySyncedNode(MapTree tree, string? mapId, string? nodeId, int index)
    {
        var data = CreateSolarMemorySyncedNodeData(mapId, nodeId);
        var type = data == null ? "null" : Field(data, "Note");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = data == null ? "null" : Field(data, "Type");
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            type = "Map";
        }

        return new MapTree.Node(type)
        {
            type = type,
            data = data,
            NodeDice = SyncedNodeDice(tree, index)
        };
    }

    private static Dictionary<string, string>? CreateSolarMemorySyncedNodeData(string? mapId, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return null;
        }

        var normalizedMapId = mapId!;
        var row = MapRow(normalizedMapId);
        var data = row == null ? new Dictionary<string, string>() : new Dictionary<string, string>(row);
        data["Id"] = normalizedMapId;
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            data["NodeId"] = nodeId!;
        }
        else if (!data.ContainsKey("NodeId"))
        {
            data["NodeId"] = normalizedMapId;
        }

        if (!data.ContainsKey("Type") || string.IsNullOrWhiteSpace(data["Type"]))
        {
            data["Type"] = IsSolarMemoryEventId(nodeId) || IsSolarMemoryMapId(normalizedMapId) ? "Event" : "Fight";
        }

        if (!data.ContainsKey("Level") || string.IsNullOrWhiteSpace(data["Level"]))
        {
            data["Level"] = "-1";
        }

        return data;
    }

    private static Dice SyncedNodeDice(MapTree tree, int index)
    {
        return tree.treedice ?? Dice.Default;
    }

    private static bool HasSolarMemoryCurrentNodeReady()
    {
        try
        {
            var currentNode = MapManager.Instance?.MapTree?.currentNode;
            var saveNode = GameSaveManager.GetNode();
            return currentNode != null
                && saveNode != null
                && (IsUsableSolarMemoryMapNode(currentNode) || IsUsableSolarMemoryMapNode(saveNode));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUsableSolarMemoryMapNode(MapTree.Node node)
    {
        return node.data != null || node.childrens != null;
    }

    private static void EnsureSolarMemoryNodeDice(MapTree.Node? node, MapTree tree, string source)
    {
        if (node == null || node.NodeDice != null)
        {
            return;
        }

        node.NodeDice = tree.treedice ?? Dice.Default;
        SunExpLog.Debug("[SolarMemoryMapSync] repaired current node dice from " + source + ".");
    }

    private static void CloseSolarMemoryTransientUi(string source)
    {
        try
        {
            SolarMemorySetupFlowRuntime.ClosePreparationWindows();
            SolarMemoryBlessingPickerRuntime.Close();
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryPackWindow", source, "[SolarMemoryFightAbort]");
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExpSolarMemoryStarterDeck", source, "[SolarMemoryFightAbort]");
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryOriginSetup", source, "[SolarMemoryFightAbort]");
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryBlessingSetup", source, "[SolarMemoryFightAbort]");
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryBlessingPicker", source, "[SolarMemoryFightAbort]");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryFightAbort] transient UI cleanup failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    private static bool IsClientOnlyPlayer()
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            return playerManager != null && !playerManager.isServer;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSolarMemoryMapId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return SunExpIds.SolarMemoryMapIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || SunExpIds.SolarMemoryShortMapIds.Any(value => string.Equals(id, value, StringComparison.Ordinal));
    }

    private static bool IsSolarMemoryEventId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return SunExpIds.SolarMemoryFullEventIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || SunExpIds.SolarMemoryEventIds.Any(value => string.Equals(id, value, StringComparison.Ordinal));
    }

    private static void FinishSolarMemoryAfterFinalLayer(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager)
            {
                return;
            }

            if (manager.Level < SunExpIds.SolarMemoryMaxLayer * 6)
            {
                return;
            }

            CompleteSolarMemoryRun(manager, "NormalMapManager.ReadyToChangeMap", 32);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory settlement failed", ex);
        }
    }

    private static void CompleteSolarMemoryRun(NormalMapManager manager, string source, int levelForNativeFlow)
    {
        manager.Level = levelForNativeFlow;
        SunExpLog.Info("[SolarMemory] third layer complete from "
            + source
            + "; routing directly to settlement at native level "
            + levelForNativeFlow
            + ".");
    }

    private static void SettleLegacyTerminalLevelBeforeMapItems(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager
                || manager.Level < LegacySolarFinaleMapLevel)
            {
                return;
            }

            CompleteSolarMemoryRun(manager, "NormalMapManager.MapItemInit", SunExpIds.SolarMemoryMaxLayer * 6);
            ShowSolarMemorySettlement();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory legacy terminal-level settlement failed", ex);
        }
    }

    public static void ShowSolarMemorySettlement()
    {
        SolarMemorySettlementPresenter.Show();
    }

    private static void SettleSolarMemoryBossAfterWin(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun())
            {
                return;
            }

            var levelId = FightManager.Instance?.level ?? "";
            if (string.Equals(levelId, SunExpIds.SolarBossSecondSunLevelId, StringComparison.Ordinal))
            {
                if (RoleDeckHasCard(SunExpIds.BlazingCrownCollapseCardId))
                {
                    SunExpLog.Info("[SolarMemoryBoss] second sun defeated; blazing crown collapse found, continuing memory.");
                    return;
                }

                CompleteSolarMemoryRunForSettlement("Fight_Win.ResetStates:second_sun_without_key_card");
                return;
            }

            if (string.Equals(levelId, SunExpIds.SolarBossSaintWunaLevelId, StringComparison.Ordinal))
            {
                CompleteSolarMemoryRunForSettlement("Fight_Win.ResetStates:saint_wuna");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory boss win settlement failed", ex);
        }
    }

    private static void CompleteSolarMemoryRunForSettlement(string source)
    {
        if (MapManager.Instance?.ModeMapManager is NormalMapManager manager)
        {
            CompleteSolarMemoryRun(manager, source, 32);
        }

        UIManager.Instance?.CloseUI("FightUI");
        ShowSolarMemorySettlement();
    }

    private static bool RoleDeckHasCard(string cardId)
    {
        var role = RoleTable.Instance;
        if (role == null || string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        return role.cardList.Any(card => IsCardId(card, cardId));
    }

    private static bool IsCardId(DataConfig? card, string expectedFullId)
    {
        var id = CardId(card);
        return string.Equals(id, expectedFullId, StringComparison.Ordinal)
            || string.Equals(id, ShortModId(expectedFullId), StringComparison.Ordinal);
    }

    private static string ShortModId(string id)
    {
        const string prefix = "SunExp_sunexp_";
        return id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : id;
    }

    private static MapTree.Node CreateSolarMemoryEventNode(MapTree tree, int layer, int mapSlotIndex)
    {
        var eventIndex = SolarMemoryEventIndex(layer, mapSlotIndex);
        var mapId = SunExpIds.SolarMemoryMapIds[eventIndex];
        var shortMapId = SunExpIds.SolarMemoryShortMapIds[eventIndex];
        var eventId = SunExpIds.SolarMemoryFullEventIds[eventIndex];
        var data = Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, mapId)
            ?? Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, shortMapId);
        var node = new MapTree.Node("普通事件");
        node.type = "普通事件";
        node.data = data == null ? new Dictionary<string, string>() : new Dictionary<string, string>(data);
        node.data["Id"] = mapId;
        node.data["Type"] = "Event";
        node.data["Note"] = "普通事件";
        node.data["NodeId"] = eventId;
        node.data["Level"] = "-1";
        node.NodeDice = Dice.Default;
        return node;
    }

    private static MapTree.Node CreateBossChainNode(MapTree tree, int indexInSegment, int segment)
    {
        return tree.TypeGenerate("首领");
    }

    private static bool IsBreakNode(MapTree.Node node)
    {
        if (node?.data == null)
        {
            return false;
        }

        return (node.data.TryGetValue("NodeId", out var nodeId) && nodeId.Contains("Breaks"))
            || (node.data.TryGetValue("Id", out var id) && id.Contains("Breaks"));
    }

    public static bool IsSolarMemoryRun()
    {
        return GameSaveManager.GetValue<string>(SunExpIds.SolarMemoryModeKey) == "1";
    }

    private static List<Dictionary<string, string>> VisibleCardPacks()
    {
        return Singleton<GameConfigManager>.Instance.GetTable(DataType.CardPack).Getlines()
            .Where(pack => !Singleton<GameRuntimeData>.Instance.IsLocked(pack["Id"]) && pack["Id"] != "cardpack_13")
            .ToList();
    }

    internal static HashSet<string> InitialPackSelection()
    {
        var visible = VisibleCardPacks().Select(pack => pack["Id"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = Singleton<GameRuntimeData>.Instance.UseCardPack
            .Where(visible.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            selected.UnionWith(visible.Take(6));
        }

        return selected;
    }

    public static List<string> CurrentPackSelection()
    {
        var playerPacks = SolarMemoryPlayerSetupState.SelectedPacks()
            .Where(IsValidPackForCurrentLobby)
            .ToList();
        if (playerPacks.Count > 0)
        {
            return playerPacks;
        }

        if (!PlayerApi.IsMultiplayerSession())
        {
            var saved = IsSolarMemoryRun() ? GameSaveManager.GetValue<string>(SunExpIds.SolarMemorySelectedPacksKey) : "";
            if (!string.IsNullOrWhiteSpace(saved))
            {
                var savedPacks = saved.Split('|')
                    .Where(IsValidPackForCurrentLobby)
                    .ToList();
                if (savedPacks.Count > 0)
                {
                    return savedPacks;
                }
            }
        }

        var selected = Singleton<GameRuntimeData>.Instance.UseCardPack
            .Where(IsValidPackForCurrentLobby)
            .ToList();
        if (selected.Count == 0)
        {
            selected.AddRange(VisibleCardPacks().Take(6).Select(pack => pack["Id"]));
        }

        return selected;
    }

    private static bool IsValidPackForCurrentLobby(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && (!string.Equals(id, "cardpack_13", StringComparison.OrdinalIgnoreCase) || GameCompatibilityApi.ShouldEnableOnlineCardPack());
    }

    public static bool IsSolarMemoryEventCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        if (ContainsEventMarker(cardId))
        {
            return true;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return IsSolarMemoryEventCard(data) || HasLocalizedEventCardType(cardId);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSolarMemoryEventCard(IDictionary<string, string> data)
    {
        var id = Field(data, "Id");
        if (ContainsEventCardIdMarker(id))
        {
            return true;
        }

        return ContainsEventTypeMarker(Field(data, "Type"))
            || ContainsEventTypeMarker(Field(data, "Note"))
            || HasLocalizedEventCardType(id)
            || ContainsSolarMemoryEventScriptMarker(Field(data, "Tag"))
            || ContainsSolarMemoryEventScriptMarker(Field(data, "Action"))
            || ContainsSolarMemoryEventScriptMarker(Field(data, "InitScript"))
            || ContainsSolarMemoryEventScriptMarker(Field(data, "UseScript"));
    }

    private static bool HasLocalizedEventCardType(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return ContainsEventTypeMarker(data.Localize("Type"))
                || ContainsEventTypeMarker(data.Localize("Note"));
        }
        catch
        {
            return false;
        }
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value : "";
    }

    private static bool ContainsEventMarker(string value)
    {
        return ContainsEventCardIdMarker(value) || ContainsEventTypeMarker(value) || ContainsSolarMemoryEventScriptMarker(value);
    }

    private static bool ContainsEventCardIdMarker(string value)
    {
        return value.IndexOf("solar_memory_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("SolarMemoryEvent", StringComparison.OrdinalIgnoreCase) >= 0
            || value.StartsWith("event_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("card_event", StringComparison.OrdinalIgnoreCase)
            || value.IndexOf("_event_", StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("事件");
    }

    private static bool ContainsEventTypeMarker(string value)
    {
        return value.Equals("Event", StringComparison.OrdinalIgnoreCase)
            || value.Equals("事件", StringComparison.Ordinal)
            || value.Equals("事件牌", StringComparison.Ordinal)
            || value.Equals("事件卡", StringComparison.Ordinal)
            || value.IndexOf("EventCard", StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("事件牌")
            || value.Contains("事件卡");
    }

    private static bool ContainsSolarMemoryEventScriptMarker(string value)
    {
        return value.IndexOf("solar_memory_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("SolarMemoryEvent", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void FilterSolarMemoryCardPackCheck(ModHookContext context)
    {
        try
        {
            if (!IsSolarMemoryRun()
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not List<Dictionary<string, string>> cards)
            {
                return;
            }

            var removed = RemoveEventCardData(cards);
            if (removed.Count > 0)
            {
                SunExpLog.Info("[SolarMemoryMode] removed event cards from CardPackCheck: " + string.Join("|", removed));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory CardPackCheck filter failed", ex);
        }
    }

    private static List<string> RemoveEventCardData(List<Dictionary<string, string>> cards)
    {
        var removed = new List<string>();
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var data = cards[i];
            if (data != null && IsSolarMemoryEventCard(data))
            {
                removed.Add(Field(data, "Id"));
                cards.RemoveAt(i);
            }
        }

        removed.Reverse();
        return removed;
    }

    public static int SanitizeSolarMemoryRoleCards(RoleTable? role, string source)
    {
        if (role == null)
        {
            return 0;
        }

        var removed = new List<string>();
        RemoveEventConfigs(role.cardList, removed);
        RemoveEventConfigs(role.UnCardList, removed);
        NormalizeSolarMemoryCardCounts(role);

        if (removed.Count > 0)
        {
            SunExpLog.Info("[SolarMemoryMode] sanitized event cards from " + source + ": " + string.Join("|", removed));
        }

        return removed.Count;
    }

    private static void RemoveEventConfigs(IList<DataConfig> cards, List<string> removed)
    {
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var config = cards[i];
            var id = CardId(config);
            if (IsSolarMemoryEventCard(id))
            {
                removed.Add(id);
                cards.RemoveAt(i);
            }
        }

        removed.Reverse();
    }

    private static string CardId(DataConfig? config)
    {
        if (config == null)
        {
            return "";
        }

        return Field(config.data, "Id");
    }

    private static void NormalizeSolarMemoryCardCounts(RoleTable role)
    {
        role.CardTopCount = Math.Max(role.CardTopCount, role.cardList.Count);
        role.CardBottomCount = Math.Min(role.CardBottomCount, role.cardList.Count);
        role.MaxAlCardCount = role.UnCardList == null ? 0 : Math.Min(role.MaxAlCardCount, role.UnCardList.Count);
    }

    public static void ClearSolarMemoryReservePool()
    {
        ClearSolarMemoryReservePool(RoleTable.Instance);
    }

    public static void ClearSolarMemoryReservePool(RoleTable? role)
    {
        if (role == null)
        {
            return;
        }

        SanitizeSolarMemoryRoleCards(role, "ClearSolarMemoryReservePool");
        role.UnCardList?.Clear();
        NormalizeSolarMemoryCardCounts(role);

        role.SpecialVarMap ??= new Dictionary<string, string>();
        role.SpecialVarMap[SunExpIds.SolarMemoryDeckConfiguredKey] = "1";
        if (ReferenceEquals(role, RoleTable.Instance))
        {
            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryDeckConfiguredKey, true);
        }

        UIManager.Instance?.ShowTip("\u65e5\u8000\u56de\u5fc6\u5907\u9009\u724c\u5df2\u6e05\u7a7a", null);
    }

    private static void SetTmpText(Transform? target, string value)
    {
        if (target == null)
        {
            return;
        }

        var component = target.GetComponent("TMPro.TMP_Text");
        if (component == null)
        {
            return;
        }

        var property = component.GetType().GetProperty("text");
        property?.SetValue(component, value);
    }

    private sealed class SolarMemoryFixedNodeSpec
    {
        private SolarMemoryFixedNodeSpec(int slotIndex, int layer, int mapSlotIndex, bool isEvent, string mapId, string nodeId)
        {
            SlotIndex = slotIndex;
            Layer = layer;
            MapSlotIndex = mapSlotIndex;
            IsEvent = isEvent;
            MapId = mapId;
            NodeId = nodeId;
        }

        public int SlotIndex { get; }

        public int Layer { get; }

        public int MapSlotIndex { get; }

        public bool IsEvent { get; }

        public string MapId { get; }

        public string NodeId { get; }

        public static SolarMemoryFixedNodeSpec Event(int slotIndex, int layer, int mapSlotIndex)
        {
            var eventIndex = SolarMemoryEventIndex(layer, mapSlotIndex);
            return new SolarMemoryFixedNodeSpec(
                slotIndex,
                layer,
                mapSlotIndex,
                true,
                SunExpIds.SolarMemoryMapIds[eventIndex],
                SunExpIds.SolarMemoryFullEventIds[eventIndex]);
        }

        public static SolarMemoryFixedNodeSpec Boss(int slotIndex, int layer, string mapId, string levelId)
        {
            return new SolarMemoryFixedNodeSpec(slotIndex, layer, slotIndex, false, mapId, levelId);
        }
    }

}
