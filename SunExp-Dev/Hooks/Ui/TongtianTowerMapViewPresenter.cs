using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks.Ui;

public static class TongtianTowerMapViewPresenter
{
    private const string BuildCardTemplatePath = "Icon/CardTemplate/\u5efa\u7b51\u724c";

    public static void ApplySlots(
        MapSelectUI mapSelect,
        NormalMapManager? manager,
        int floor,
        bool applyAllSlots,
        bool sync,
        string source)
    {
        if (manager == null)
        {
            return;
        }

        if (!TongtianTowerFloorPlanStore.TryLoad(floor, out var plan))
        {
            TongtianTowerMapBuilder.EnsureFloorMapState(manager, floor, source + ":plan-repair");
            if (!TongtianTowerFloorPlanStore.TryLoad(floor, out plan))
            {
                return;
            }
        }

        var nodes = TryGetMapSelectNodes(mapSelect, source);
        if (nodes == null || nodes.Length == 0)
        {
            return;
        }

        var fixedSlots = new HashSet<int>(plan.FixedSlots());
        var changed = applyAllSlots && ClearEditableSlots(mapSelect, nodes, fixedSlots);
        foreach (var slot in TongtianTowerMapProjectionService.SlotsToApply(plan, applyAllSlots))
        {
            if (slot < 0
                || slot >= nodes.Length
                || !plan.TryGetSlot(slot, out var slotPlan)
                || nodes[slot] == null)
            {
                continue;
            }

            var plannedNode = slotPlan.ToNode(manager.MapTree);
            var node = nodes[slot];
            if (!EquivalentNode(node, plannedNode))
            {
                node.data = plannedNode.data;
                node.NodeDice = plannedNode.NodeDice;
                changed = true;
            }

            MapNodeSafetyService.EnsureNodeDice(manager.MapTree, node, "TongtianTowerMapViewPresenter.ApplySlots");
            EnsureTowerSlotVisual(mapSelect, slot, node, node.data, fixedSlots.Contains(slot));
        }

        if (sync && changed)
        {
            mapSelect.SendNode();
            SunExpLog.Info("[TongtianTowerMap] slots applied from " + source + "; floor=" + floor + ".");
        }
    }

    private static bool ClearEditableSlots(MapSelectUI mapSelect, MapTree.Node[] nodes, HashSet<int> fixedSlots)
    {
        var changed = false;
        var count = Math.Min(nodes.Length, SunExpIds.TongtianTowerLayerNodeCount);
        for (var slot = 0; slot < count; slot++)
        {
            if (fixedSlots.Contains(slot) || nodes[slot] == null)
            {
                continue;
            }

            if (nodes[slot].data != null)
            {
                nodes[slot].data = null;
                changed = true;
            }

            var slotRoot = MapSlotTransform(mapSelect, slot);
            var content = slotRoot?.Find("Content");
            if (slotRoot == null || content == null)
            {
                continue;
            }

            foreach (var existing in content.GetComponentsInChildren<MapItem>(true))
            {
                if (existing == null)
                {
                    continue;
                }

                UnityEngine.Object.Destroy(existing.gameObject);
                changed = true;
            }

            var nullSlot = content.Find("Null");
            if (nullSlot != null && !nullSlot.gameObject.activeSelf)
            {
                nullSlot.gameObject.SetActive(true);
                changed = true;
            }

            var frame = slotRoot.Find("Frame");
            if (frame != null)
            {
                RemoveChains(frame);
            }
        }

        return changed;
    }

    public static void SetLayerTitle(MapSelectUI mapSelect, int floor)
    {
        var title = SunExpIds.TongtianTowerTitle + " \u7b2c" + Math.Max(1, floor) + "\u5c42";
        SetTmpText(mapSelect.transform.Find("Title/Text/text"), title);

        var text = mapSelect.transform.Find("Title/Text/text")?.GetComponent<Text>();
        if (text != null)
        {
            text.text = title;
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

        var prefabName = PrefabNameForType(Field(data, "Type"));
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
        var visualState = slotItem.GetComponent<TongtianTowerSlotViewState>() ?? slotItem.AddComponent<TongtianTowerSlotViewState>();
        var mapId = Field(data, "Id");
        var nodeId = Field(data, "NodeId");
        if (!visualState.Matches(mapId, nodeId, prefabName))
        {
            item.Init(node);
            visualState.Set(mapId, nodeId, prefabName);
        }

        ApplyMapCardTexture(item, data);
        if (slotItem.TryGetComponent<ObjectGroup>(out var objectGroup))
        {
            objectGroup.blocksRaycasts = !locked;
        }

        var frame = slot.Find("Frame");
        if (frame != null && locked && !HasChain(frame))
        {
            var chain = mapSelect.transform.Find("Chain");
            if (chain != null)
            {
                UnityEngine.Object.Instantiate(chain.gameObject, frame).SetActive(true);
            }
        }
        else if (frame != null && !locked)
        {
            RemoveChains(frame);
        }
    }

    private static string PrefabNameForType(string type)
    {
        return string.Equals(type, "Fight", StringComparison.Ordinal) ? "FightPrefab" : "EventPrefab";
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

    private static void RemoveChains(Transform frame)
    {
        var chains = new List<GameObject>();
        foreach (Transform child in frame)
        {
            if (child.name.StartsWith("Chain", StringComparison.Ordinal))
            {
                chains.Add(child.gameObject);
            }
        }

        foreach (var chain in chains)
        {
            UnityEngine.Object.Destroy(chain);
        }
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
}

public sealed class TongtianTowerSlotViewState : MonoBehaviour
{
    private string mapId = "";
    private string nodeId = "";
    private string prefabName = "";

    public bool Matches(string nextMapId, string nextNodeId, string nextPrefabName)
    {
        return string.Equals(mapId, nextMapId, StringComparison.Ordinal)
            && string.Equals(nodeId, nextNodeId, StringComparison.Ordinal)
            && string.Equals(prefabName, nextPrefabName, StringComparison.Ordinal);
    }

    public void Set(string nextMapId, string nextNodeId, string nextPrefabName)
    {
        mapId = nextMapId;
        nodeId = nextNodeId;
        prefabName = nextPrefabName;
    }
}
