using System;
using System.Collections.Generic;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerMapBuilder
{
    public static bool EnsureFloorMapState(
        NormalMapManager manager,
        int floor,
        string source,
        bool forceRebuild = false)
    {
        var tree = manager?.MapTree;
        if (tree == null)
        {
            SunExpLog.Warn("[TongtianTowerMap] skipped build from " + source + ": MapTree is null.");
            return false;
        }

        var normalizedFloor = Math.Max(1, floor);
        EnsureTowerSaveDefaults();
        EnsureNativeGeneratorSuppressed(tree);
        if (!forceRebuild && IsCurrentFloorReady(tree, normalizedFloor))
        {
            return false;
        }

        BuildFloor(tree, normalizedFloor, source);
        return true;
    }

    public static IReadOnlyList<MapTree.Node> VisualDefaultNodes(MapTree? tree)
    {
        var result = new List<MapTree.Node>(SunExpIds.TongtianTowerLayerNodeCount);
        var plan = TongtianTowerFloorPlanStore.Load();
        if (tree == null || plan == null)
        {
            return result;
        }

        for (var slot = 0; slot < SunExpIds.TongtianTowerLayerNodeCount; slot++)
        {
            if (plan.TryGetSlot(slot, out var slotPlan))
            {
                result.Add(slotPlan.ToNode(tree));
            }
        }

        return result;
    }

    public static bool TryGetVisualDefaultNode(MapTree? tree, int visualSlot, out MapTree.Node node)
    {
        node = null!;
        var plan = TongtianTowerFloorPlanStore.Load();
        if (tree == null || plan == null || !plan.TryGetSlot(visualSlot, out var slot))
        {
            return false;
        }

        node = slot.ToNode(tree);
        return true;
    }

    public static bool RepairFixedMapArrays(MapTree? tree, int floor, string[] maps, string[] mapData)
    {
        if (maps.Length == 0 || mapData.Length == 0)
        {
            return false;
        }

        var changed = false;
        changed = RepairMapArraySlot(tree, SunExpIds.TongtianTowerStartSlotIndex, maps, mapData) || changed;
        changed = RepairMapArraySlot(tree, SunExpIds.TongtianTowerBossSlotIndex, maps, mapData) || changed;

        return changed;
    }

    private static void BuildFloor(MapTree tree, int floor, string source)
    {
        var plan = TongtianTowerFloorPlanner.Create(tree, floor);
        TongtianTowerFloorPlanStore.Save(plan);
        tree.DefaultNode.Clear();
        var nativeDefaults = new List<MapTree.Node>(NativeDefaultOrder(tree, plan));
        foreach (var node in nativeDefaults)
        {
            MapNodeSafetyService.EnsureNodeDice(tree, node, "TongtianTowerMapBuilder.Default");
            tree.DefaultNode.Add(node);
        }

        tree.SelectNode.Clear();
        var selectableKinds = TongtianTowerSelectableNodeDeckPlanner.CreateKinds(
            tree,
            floor,
            SunExpIds.TongtianTowerSelectableNodeCount);
        for (var i = 0; i < selectableKinds.Count; i++)
        {
            var selectNode = TongtianTowerNodePoolService.CreateNode(
                tree,
                floor,
                i,
                selectableKinds[i]);
            tree.SelectNode.Add(selectNode);
        }

        tree.root = new MapTree.Node("Root")
        {
            NodeDice = tree.treedice ?? Dice.Default
        };
        tree.currentNode = tree.root;
        GameSaveManager.UpdateNode(tree.root);
        EnsureNativeGeneratorSuppressed(tree);
        SetSaveValue(SunExpIds.TongtianTowerGeneratedFloorKey, floor.ToString());

        SunExpLog.Info("[TongtianTowerMap] floor built from "
            + source
            + "; floor="
            + floor
            + "; fixedSlots="
            + string.Join("|", plan.FixedSlots())
            + "; select="
            + string.Join("|", selectableKinds)
            + "; visualFixed="
            + string.Join("|", plan.Summaries())
            + "; nativeDefaults="
            + string.Join("|", NativeSummaries(nativeDefaults)));
    }

    private static IEnumerable<MapTree.Node> NativeDefaultOrder(MapTree tree, TongtianTowerFloorPlan plan)
    {
        return TongtianTowerMapProjectionService.NativeDefaultOrder(tree, plan);
    }

    private static bool IsCurrentFloorReady(MapTree tree, int floor)
    {
        if (!TongtianTowerFloorPlanStore.TryLoad(floor, out var plan))
        {
            return false;
        }

        if (tree.DefaultNode.Count < SunExpIds.TongtianTowerNativeDefaultNodeCount
            || tree.SelectNode.Count < SunExpIds.TongtianTowerSelectableNodeCount
            || GameSaveManager.GetValue<int>(SunExpIds.TongtianTowerGeneratedFloorKey) != floor)
        {
            return false;
        }

        return TongtianTowerMapProjectionService.IsNativeBootstrapReady(tree, plan);
    }

    private static bool RepairMapArraySlot(MapTree? tree, int visualSlot, string[] maps, string[] mapData)
    {
        if (visualSlot < 0
            || visualSlot >= maps.Length
            || visualSlot >= mapData.Length
            || !TryGetVisualDefaultNode(tree, visualSlot, out var node)
            || node.data == null)
        {
            return false;
        }

        var expectedMap = DictionaryUtil.Get(node.data, "Id");
        var expectedNode = DictionaryUtil.Get(node.data, "NodeId", expectedMap);
        var changed = false;
        if (!string.Equals(maps[visualSlot], expectedMap, StringComparison.Ordinal))
        {
            maps[visualSlot] = expectedMap;
            changed = true;
        }

        if (!string.Equals(mapData[visualSlot], expectedNode, StringComparison.Ordinal))
        {
            mapData[visualSlot] = expectedNode;
            changed = true;
        }

        return changed;
    }

    private static IEnumerable<string> NativeSummaries(IEnumerable<MapTree.Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (node?.data == null)
            {
                yield return "<empty>";
                continue;
            }

            var id = DictionaryUtil.Get(node.data, "Id");
            var nodeId = DictionaryUtil.Get(node.data, "NodeId", id);
            var type = DictionaryUtil.Get(node.data, "Type");
            var kind = DictionaryUtil.Get(node.data, SunExpIds.TongtianTowerNodeKindKey, type);
            yield return id + "/" + nodeId + ":" + kind;
        }
    }

    private static void EnsureTowerSaveDefaults()
    {
        SetSaveValue(GameVar.ExLockDes.ToString(), "0");
        SetSaveValue(GameVar.ExDeleteDes.ToString(), "0");
    }

    private static void EnsureNativeGeneratorSuppressed(MapTree tree)
    {
        tree.hasUsed ??= new List<int>();
        if (!tree.hasUsed.Contains(0))
        {
            tree.hasUsed.Add(0);
        }
    }

    private static void SetSaveValue(string key, string value)
    {
        try
        {
            GameSaveManager.GetNowSave()?.SetValue(key, value);
        }
        catch
        {
            GameSaveManager.SetValue(key, value);
        }
    }

}
