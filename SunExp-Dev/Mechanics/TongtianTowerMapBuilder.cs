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
        if (tree?.DefaultNode == null)
        {
            return result;
        }

        for (var slot = 0; slot < SunExpIds.TongtianTowerLayerNodeCount; slot++)
        {
            var index = NativeDefaultIndexForVisualSlot(slot);
            if (index >= 0 && index < tree.DefaultNode.Count)
            {
                result.Add(tree.DefaultNode[index]);
            }
        }

        return result;
    }

    public static bool TryGetVisualDefaultNode(MapTree? tree, int visualSlot, out MapTree.Node node)
    {
        node = null!;
        if (tree?.DefaultNode == null)
        {
            return false;
        }

        var index = NativeDefaultIndexForVisualSlot(visualSlot);
        if (index < 0 || index >= tree.DefaultNode.Count || tree.DefaultNode[index] == null)
        {
            return false;
        }

        node = tree.DefaultNode[index];
        return true;
    }

    public static int BuildingSlotForFloor(int floor)
    {
        return 1 + ((Math.Max(1, floor) - 1) % 4);
    }

    public static bool RepairFixedMapArrays(MapTree? tree, int floor, string[] maps, string[] mapData)
    {
        if (maps.Length == 0 || mapData.Length == 0)
        {
            return false;
        }

        var changed = false;
        changed = RepairMapArraySlot(tree, SunExpIds.TongtianTowerBossSlotIndex, maps, mapData) || changed;
        changed = RepairMapArraySlot(tree, BuildingSlotForFloor(floor), maps, mapData) || changed;
        return changed;
    }

    private static void BuildFloor(MapTree tree, int floor, string source)
    {
        var visualNodes = CreateVisualNodes(tree, floor);
        tree.DefaultNode.Clear();
        foreach (var node in NativeDefaultOrder(visualNodes))
        {
            MapNodeSafetyService.EnsureNodeDice(tree, node, "TongtianTowerMapBuilder.Default");
            tree.DefaultNode.Add(node);
        }

        tree.SelectNode.Clear();
        for (var i = 0; i < SunExpIds.TongtianTowerSelectableNodeCount; i++)
        {
            var selectNode = TongtianTowerNodePoolService.CreateNode(
                tree,
                floor,
                i,
                TongtianTowerNodeKind.Monster);
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
            + "; buildingSlot="
            + BuildingSlotForFloor(floor)
            + "; defaults="
            + string.Join("|", NodeSummaries(visualNodes)));
    }

    private static List<MapTree.Node> CreateVisualNodes(MapTree tree, int floor)
    {
        var buildingSlot = BuildingSlotForFloor(floor);
        var nodes = new List<MapTree.Node>(SunExpIds.TongtianTowerLayerNodeCount);
        for (var slot = 0; slot < SunExpIds.TongtianTowerLayerNodeCount; slot++)
        {
            var kind = slot == SunExpIds.TongtianTowerBossSlotIndex
                ? TongtianTowerNodeKind.Boss
                : slot == buildingSlot
                    ? TongtianTowerNodeKind.Building
                    : TongtianTowerNodeKind.Monster;
            nodes.Add(TongtianTowerNodePoolService.CreateNode(tree, floor, slot, kind));
        }

        return nodes;
    }

    private static IEnumerable<MapTree.Node> NativeDefaultOrder(IReadOnlyList<MapTree.Node> visualNodes)
    {
        yield return visualNodes[0];
        yield return visualNodes[SunExpIds.TongtianTowerBossSlotIndex];
        yield return visualNodes[4];
        yield return visualNodes[3];
        yield return visualNodes[2];
        yield return visualNodes[1];
    }

    private static bool IsCurrentFloorReady(MapTree tree, int floor)
    {
        if (tree.DefaultNode.Count < SunExpIds.TongtianTowerLayerNodeCount
            || tree.SelectNode.Count < SunExpIds.TongtianTowerSelectableNodeCount
            || GameSaveManager.GetValue<int>(SunExpIds.TongtianTowerGeneratedFloorKey) != floor)
        {
            return false;
        }

        return TryGetVisualDefaultNode(tree, SunExpIds.TongtianTowerBossSlotIndex, out var boss)
            && string.Equals(NodeKind(boss), TongtianTowerNodeKind.Boss.ToString(), StringComparison.Ordinal)
            && TryGetVisualDefaultNode(tree, BuildingSlotForFloor(floor), out var building)
            && string.Equals(NodeKind(building), TongtianTowerNodeKind.Building.ToString(), StringComparison.Ordinal);
    }

    private static int NativeDefaultIndexForVisualSlot(int slot)
    {
        return slot switch
        {
            0 => 0,
            5 => 1,
            4 => 2,
            3 => 3,
            2 => 4,
            1 => 5,
            _ => -1
        };
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

    private static void EnsureTowerSaveDefaults()
    {
        SetSaveValue(GameVar.ExLockDes.ToString(), "4");
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

    private static IEnumerable<string> NodeSummaries(IEnumerable<MapTree.Node> nodes)
    {
        foreach (var node in nodes)
        {
            yield return DictionaryUtil.Get(node.data, "Id")
                + "/"
                + DictionaryUtil.Get(node.data, "NodeId")
                + ":"
                + NodeKind(node);
        }
    }

    private static string NodeKind(MapTree.Node node)
    {
        return node.data != null
            ? DictionaryUtil.Get(node.data, SunExpIds.TongtianTowerNodeKindKey)
            : "";
    }
}
