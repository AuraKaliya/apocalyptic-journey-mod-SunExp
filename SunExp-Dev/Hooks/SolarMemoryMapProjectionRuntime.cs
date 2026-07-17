using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
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

internal static class SolarMemoryMapProjectionRuntime
{
    private const string StoryCardTemplatePath = "Icon/CardTemplate/\u6545\u4e8b\u724c";
    private const string BuildCardTemplatePath = "Icon/CardTemplate/\u5efa\u7b51\u724c";

    internal static void ApplySolarMemoryLayerTitle(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            var layer = CurrentSolarMemoryLayer();
            var title = SunExpIds.SolarMemoryLayerNames[
                Math.Max(0, Math.Min(SunExpIds.SolarMemoryLayerNames.Length - 1, layer))];
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

    internal static void ApplySolarMemoryFixedSlotsAfterMapItems(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
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

    internal static void ApplySolarMemoryFixedSlots(
        MapSelectUI mapSelect,
        NormalMapManager? manager,
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

        var layer = SolarMemoryFixedNodeCatalog.ClampLayer(manager.Level / 6);
        var changed = false;
        foreach (var spec in SolarMemoryFixedNodeCatalog.ForLayer(layer))
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
            if (SolarMemoryMapLifecycleCoordinator.IsClientOnlyPlayer())
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

    private static Dictionary<string, string>? CreateFixedNodeData(SolarMemoryFixedNodeSpec spec)
    {
        Dictionary<string, string>? row;
        if (spec.IsEvent)
        {
            var eventIndex = SolarMemoryFixedNodeCatalog.EventIndex(spec.Layer, spec.MapSlotIndex);
            var mapId = spec.MapId;
            var shortMapId = SunExpIds.SolarMemoryShortMapIds[eventIndex];
            row = MapRow(mapId) ?? MapRow(shortMapId);
            var data = row == null ? new Dictionary<string, string>() : new Dictionary<string, string>(row);
            data["Id"] = mapId;
            data["Type"] = "Event";
            data["NodeId"] = spec.NodeId;
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
        return SunExpConfigIndex.Row(DataType.Map, mapId);
    }

    private static void EnsureFixedSlotVisual(
        MapSelectUI mapSelect,
        int slotIndex,
        MapTree.Node node,
        IDictionary<string, string> data)
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
        var fixedItem = FindReusableFixedSlotItem(content, prefabName, nullSlot);
        foreach (var existing in content.GetComponentsInChildren<MapItem>(true))
        {
            if (existing == null
                || existing.gameObject == fixedItem
                || nullSlot != null && (existing.transform == nullSlot || existing.transform.IsChildOf(nullSlot)))
            {
                continue;
            }

            UnityEngine.Object.Destroy(existing.gameObject);
        }

        if (fixedItem == null)
        {
            var template = mapSelect.transform.Find("MapSelect/" + prefabName);
            if (template == null)
            {
                SunExpLog.Warn("[SolarMemoryMapLock] missing map prefab: " + prefabName);
                return;
            }

            fixedItem = UnityEngine.Object.Instantiate(template.gameObject, content);
            fixedItem.name = prefabName;
        }

        fixedItem.transform.SetParent(content, false);
        fixedItem.transform.localScale = Vector3.one;
        fixedItem.SetActive(true);

        var item = fixedItem.GetComponent<MapItem>() ?? fixedItem.AddComponent<MapItem>();
        var visualState = fixedItem.GetComponent<FixedSlotVisualState>() ?? fixedItem.AddComponent<FixedSlotVisualState>();
        var nodeId = Field(data, "NodeId");
        var mapId = Field(data, "Id");
        if (!visualState.Matches(mapId, nodeId))
        {
            item.Init(node);
            visualState.Set(mapId, nodeId);
        }

        ApplyMapCardTexture(item, data);

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

    private static GameObject? FindReusableFixedSlotItem(Transform content, string prefabName, Transform? nullSlot)
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

        if (slotIndex == SolarMemoryFixedNodeCatalog.OpeningSlotIndex)
        {
            return root.Find("Start");
        }

        if (slotIndex == SolarMemoryFixedNodeCatalog.EndingSlotIndex)
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
        if (item == null)
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
                ApplyMapCardTexture(item, customTexture, hideIcon: true, "event custom");
                return;
            }

            ApplyMapCardTexture(
                item,
                SunExpResourceCache.Load<Texture>(StoryCardTemplatePath, true),
                hideIcon: false,
                "event fallback");
        }
        else if (type == "Build")
        {
            ApplyMapCardTexture(
                item,
                SunExpResourceCache.Load<Texture>(BuildCardTemplatePath, true),
                hideIcon: false,
                "build fallback");
        }
    }

    private static Texture? LoadMapCardTexture(string path)
    {
        try
        {
            return SunExpResourceCache.Load<Texture>(path, true);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] failed to load map card texture " + path + ": " + ex.Message);
            return null;
        }
    }

    private static void ApplyMapCardTexture(MapItem item, Texture? texture, bool hideIcon, string source)
    {
        if (texture == null)
        {
            SunExpLog.Warn("[SolarMemoryMapLock] map card texture missing: " + source);
            return;
        }

        if (!MapItemApi.ApplyCardBackgroundTexture(item, texture, hideIcon, out var appliedTarget))
        {
            SunExpLog.Warn("[SolarMemoryMapLock] map card texture skipped, renderer missing: " + source);
            return;
        }

        SunExpLog.Debug("[SolarMemoryMapLock] map card texture applied: " + source + " -> " + appliedTarget);
    }

    private static int CurrentSolarMemoryLayer()
    {
        if (MapManager.Instance?.ModeMapManager is not NormalMapManager manager)
        {
            return 0;
        }

        return SolarMemoryFixedNodeCatalog.ClampLayer(manager.Level / 6);
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value : "";
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

    private sealed class FixedSlotVisualState : MonoBehaviour
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
