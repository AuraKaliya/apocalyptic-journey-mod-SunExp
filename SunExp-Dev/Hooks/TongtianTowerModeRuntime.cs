using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerModeRuntime
{
    private const string BuildCardTemplatePath = "Icon/CardTemplate/\u5efa\u7b51\u724c";

    public static void Initialize(ModConfig modConfig)
    {
        TongtianTowerModeEntryRuntime.Initialize(modConfig);
        RegisterBefore(modConfig, "NormalMapManager.MapItemInit", EnsureTowerMapBeforeMapItems);
        RegisterAfter(modConfig, "NormalMapManager.MapItemInit", ApplyTowerSlotsAfterMapItems);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureTowerMapBeforeSelect);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ReapplyTowerFixedSlotLocks);
        RegisterAfter(modConfig, "MapSelectUI.DataUpdate", ApplyTowerLayerTitle);
        RegisterBefore(modConfig, "NormalMapManager.ReadyToChangeMap", AdvanceTowerFloorBeforeMapChange);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RepairTowerMapAfterNativeGeneration);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcNextMap", EnsureTowerCurrentNodeBeforeNextMap);
        RegisterAfter(modConfig, "MapManager.RpcNextMap", SyncTowerClientLastNodeAfterNextMap);
    }

    public static bool IsTongtianTowerRun()
    {
        return GameSaveManager.GetValue<string>(SunExpIds.TongtianTowerModeKey) == "1";
    }

    public static int CurrentFloor()
    {
        return Math.Max(1, GameSaveManager.GetValue<int>(SunExpIds.TongtianTowerFloorKey));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian tower " + message));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian tower " + message));
    }

    private static void EnsureTowerMapBeforeMapItems(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun() || context.Target is not NormalMapManager manager)
            {
                return;
            }

            TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.MapItemInit:before");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower pre-map-item build failed", ex);
        }
    }

    private static void ApplyTowerSlotsAfterMapItems(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun()
                || context.Target is not NormalMapManager manager
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapSelectUI mapSelect)
            {
                return;
            }

            TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.MapItemInit:after");
            ApplyTowerSlots(mapSelect, manager, applyAllSlots: true, sync: true, "NormalMapManager.MapItemInit");
            SetTowerLayerTitle(mapSelect);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower fixed slot apply failed", ex);
        }
    }

    private static void EnsureTowerMapBeforeSelect(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun())
            {
                return;
            }

            if (MapManager.Instance?.ModeMapManager is NormalMapManager manager)
            {
                TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "MapSelectUI.ReadyToSelect");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower pre-select map repair failed", ex);
        }
    }

    private static void ReapplyTowerFixedSlotLocks(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            var manager = MapManager.Instance?.ModeMapManager as NormalMapManager;
            ApplyTowerSlots(mapSelect, manager, applyAllSlots: false, sync: false, "MapSelectUI.ShowMap");
            SetTowerLayerTitle(mapSelect);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower fixed slot lock repair failed", ex);
        }
    }

    private static void ApplyTowerLayerTitle(ModHookContext context)
    {
        try
        {
            if (IsTongtianTowerRun() && context.Target is MapSelectUI mapSelect)
            {
                SetTowerLayerTitle(mapSelect);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower layer title failed", ex);
        }
    }

    private static void AdvanceTowerFloorBeforeMapChange(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun()
                || context.Target is not NormalMapManager manager
                || manager.Level < SunExpIds.TongtianTowerLayerNodeCount)
            {
                return;
            }

            if (IsClientOnlyPlayer())
            {
                return;
            }

            var nextFloor = CurrentFloor() + 1;
            SetSaveValue(SunExpIds.TongtianTowerFloorKey, nextFloor.ToString());
            SetSaveValue(SunExpIds.TongtianTowerGeneratedFloorKey, "0");
            if (MapManager.Instance != null)
            {
                MapManager.Instance.SetLevel(0);
            }
            else
            {
                manager.Level = 0;
                GameSaveManager.SetLevel(0);
            }

            TongtianTowerMapBuilder.EnsureFloorMapState(manager, nextFloor, "NormalMapManager.ReadyToChangeMap", forceRebuild: true);
            SunExpLog.Info("[TongtianTowerMode] advanced to floor " + nextFloor + ".");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower floor advance failed", ex);
        }
    }

    private static void RepairTowerMapAfterNativeGeneration(ModHookContext context)
    {
        try
        {
            if (IsTongtianTowerRun() && context.Target is NormalMapManager manager)
            {
                TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.GeneratrMap:after");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower native generation repair failed", ex);
        }
    }

    private static void RepairTowerMapSelection(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun())
            {
                return;
            }

            var manager = MapManager.Instance?.ModeMapManager as NormalMapManager;
            if (manager != null)
            {
                TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "MapManager.MapSelectionSync");
            }

            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is string[] maps && args[i + 1] is string[] mapData)
                {
                    if (TongtianTowerMapBuilder.RepairFixedMapArrays(MapManager.Instance?.MapTree, CurrentFloor(), maps, mapData))
                    {
                        SunExpLog.Info("[TongtianTowerMapSync] fixed slot arrays repaired.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower map selection repair failed", ex);
        }
    }

    private static void EnsureTowerCurrentNodeBeforeNextMap(ModHookContext context)
    {
        try
        {
            if (IsTongtianTowerRun() && IsClientOnlyPlayer() && MapManager.Instance?.MapTree?.currentNode == null)
            {
                MapNodeSafetyService.RestoreCurrentNodeIfMissingOrExclusive(
                    MapManager.Instance?.Level ?? 0,
                    "TongtianTower.MapManager.RpcNextMap",
                    clientOnly: true);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerMapSync] pre-next-map current node repair failed: " + ex.Message);
        }
    }

    private static void SyncTowerClientLastNodeAfterNextMap(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            var node = MapManager.Instance?.MapTree?.currentNode;
            if (node != null)
            {
                GameSaveManager.UpdateNode(node);
                SunExpLog.Debug("[TongtianTowerMapSync] synced client save node after RpcNextMap.");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerMapSync] post-next-map save node sync failed: " + ex.Message);
        }
    }

    private static void ApplyTowerSlots(
        MapSelectUI mapSelect,
        NormalMapManager? manager,
        bool applyAllSlots,
        bool sync,
        string source)
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

        var floor = CurrentFloor();
        var fixedSlots = FixedSlots(floor);
        var slots = applyAllSlots
            ? Enumerable.Range(0, Math.Min(SunExpIds.TongtianTowerLayerNodeCount, nodes.Length)).ToArray()
            : fixedSlots;
        var changed = false;
        foreach (var slot in slots)
        {
            if (slot < 0
                || slot >= nodes.Length
                || !TongtianTowerMapBuilder.TryGetVisualDefaultNode(manager.MapTree, slot, out var defaultNode)
                || defaultNode.data == null
                || nodes[slot] == null)
            {
                continue;
            }

            var node = nodes[slot];
            if (!EquivalentNode(node, defaultNode))
            {
                node.data = defaultNode.data;
                node.NodeDice = defaultNode.NodeDice;
                changed = true;
            }

            MapNodeSafetyService.EnsureNodeDice(manager.MapTree, node, "TongtianTowerModeRuntime.ApplyTowerSlots");
            EnsureTowerSlotVisual(mapSelect, slot, node, node.data, fixedSlots.Contains(slot));
        }

        if (sync && changed)
        {
            mapSelect.SendNode();
            SunExpLog.Info("[TongtianTowerMap] slots applied from " + source + "; floor=" + floor + ".");
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
            SunExpLog.Warn("[TongtianTowerMap] skipped slot apply from "
                + source
                + ": map nodes unavailable ("
                + ex.GetType().Name
                + ": "
                + ex.Message
                + ").");
            return null;
        }
    }

    private static int[] FixedSlots(int floor)
    {
        return new[]
        {
            TongtianTowerMapBuilder.BuildingSlotForFloor(floor),
            SunExpIds.TongtianTowerBossSlotIndex
        };
    }

    private static void EnsureTowerSlotVisual(
        MapSelectUI mapSelect,
        int slotIndex,
        MapTree.Node node,
        IDictionary<string, string> data,
        bool locked)
    {
        var slot = MapSlotTransform(mapSelect, slotIndex);
        var content = slot?.Find("Content");
        if (slot == null || content == null)
        {
            return;
        }

        var nullSlot = content.Find("Null");
        if (nullSlot != null)
        {
            nullSlot.gameObject.SetActive(false);
        }

        var prefabName = Field(data, "Type") + "Prefab";
        var slotItem = FindReusableTowerSlotItem(content, prefabName, nullSlot);
        foreach (var existing in content.GetComponentsInChildren<MapItem>(true))
        {
            if (existing == null
                || existing.gameObject == slotItem
                || nullSlot != null && (existing.transform == nullSlot || existing.transform.IsChildOf(nullSlot)))
            {
                continue;
            }

            UnityEngine.Object.Destroy(existing.gameObject);
        }

        if (slotItem == null)
        {
            var template = mapSelect.transform.Find("MapSelect/" + prefabName);
            if (template == null)
            {
                SunExpLog.Warn("[TongtianTowerMap] missing map prefab: " + prefabName);
                return;
            }

            slotItem = UnityEngine.Object.Instantiate(template.gameObject, content);
            slotItem.name = prefabName;
        }

        slotItem.transform.SetParent(content, false);
        slotItem.transform.localScale = Vector3.one;
        slotItem.SetActive(true);

        var item = slotItem.GetComponent<MapItem>() ?? slotItem.AddComponent<MapItem>();
        var visualState = slotItem.GetComponent<TowerSlotVisualState>() ?? slotItem.AddComponent<TowerSlotVisualState>();
        var mapId = Field(data, "Id");
        var nodeId = Field(data, "NodeId");
        if (!visualState.Matches(mapId, nodeId))
        {
            item.Init(node);
            visualState.Set(mapId, nodeId);
        }

        ApplyMapCardTexture(item, data);
        if (slotItem.TryGetComponent<ObjectGroup>(out var objectGroup))
        {
            objectGroup.blocksRaycasts = !locked;
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

    private static GameObject? FindReusableTowerSlotItem(Transform content, string prefabName, Transform? nullSlot)
    {
        foreach (var existing in content.GetComponentsInChildren<MapItem>(true))
        {
            if (existing == null
                || nullSlot != null && (existing.transform == nullSlot || existing.transform.IsChildOf(nullSlot)))
            {
                continue;
            }

            if (existing.gameObject.name.StartsWith(prefabName, StringComparison.Ordinal))
            {
                return existing.gameObject;
            }
        }

        return null;
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

        if (slotIndex == SunExpIds.TongtianTowerBossSlotIndex)
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

    private static void ApplyMapCardTexture(MapItem item, IDictionary<string, string> data)
    {
        if (item == null || Field(data, "Type") != "Build")
        {
            return;
        }

        var texture = SunExpResourceCache.Load<Texture>(BuildCardTemplatePath, true);
        if (texture != null && !MapItemApi.ApplyCardBackgroundTexture(item, texture, hideIcon: false, out _))
        {
            SunExpLog.Warn("[TongtianTowerMap] build card texture skipped, renderer missing.");
        }
    }

    private static bool EquivalentNode(MapTree.Node? left, MapTree.Node? right)
    {
        return string.Equals(NodeField(left, "Id"), NodeField(right, "Id"), StringComparison.Ordinal)
            && string.Equals(NodeField(left, "NodeId"), NodeField(right, "NodeId"), StringComparison.Ordinal)
            && string.Equals(NodeField(left, "Type"), NodeField(right, "Type"), StringComparison.Ordinal);
    }

    private static void SetTowerLayerTitle(MapSelectUI mapSelect)
    {
        var title = SunExpIds.TongtianTowerTitle + " 第" + CurrentFloor() + "层";
        SetTmpText(mapSelect.transform.Find("Title/Text/text"), title);

        var text = mapSelect.transform.Find("Title/Text/text")?.GetComponent<Text>();
        if (text != null)
        {
            text.text = title;
        }
    }

    private static void SetSaveValue(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value);
        }
        catch
        {
            GameSaveManager.GetNowSave()?.SetValue(key, value);
        }
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

    private static string NodeField(MapTree.Node? node, string key)
    {
        return node?.data != null && node.data.TryGetValue(key, out var value) ? value : "";
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value : "";
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

    private sealed class TowerSlotVisualState : MonoBehaviour
    {
        private string mapId = "";
        private string nodeId = "";

        public bool Matches(string nextMapId, string nextNodeId)
        {
            return string.Equals(mapId, nextMapId, StringComparison.Ordinal)
                && string.Equals(nodeId, nextNodeId, StringComparison.Ordinal);
        }

        public void Set(string nextMapId, string nextNodeId)
        {
            mapId = nextMapId;
            nodeId = nextNodeId;
        }
    }
}
