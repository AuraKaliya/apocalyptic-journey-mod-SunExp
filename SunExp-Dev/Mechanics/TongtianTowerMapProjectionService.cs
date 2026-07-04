using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerMapProjectionService
{
    public static IReadOnlyList<MapTree.Node> NativeDefaultOrder(MapTree tree, TongtianTowerFloorPlan plan)
    {
        var result = new List<MapTree.Node>(SunExpIds.TongtianTowerNativeDefaultNodeCount);
        var boss = SlotOrFallback(plan, SunExpIds.TongtianTowerBossSlotIndex);
        result.Add(TongtianTowerNodePoolService.CreateNode(
            tree,
            plan.Floor,
            SunExpIds.TongtianTowerStartSlotIndex,
            TongtianTowerNodeKind.Rest));
        result.Add(boss.ToNode(tree));

        return result;
    }

    public static bool IsNativeBootstrapReady(MapTree tree, TongtianTowerFloorPlan plan)
    {
        if (tree?.DefaultNode == null || tree.DefaultNode.Count < SunExpIds.TongtianTowerNativeDefaultNodeCount)
        {
            return false;
        }

        return NodeType(tree.DefaultNode[0]) != "Fight"
            && NodeType(tree.DefaultNode[1]) == "Fight"
            && plan.IsValid;
    }

    public static IEnumerable<int> SlotsToApply(TongtianTowerFloorPlan plan, bool applyAllSlots)
    {
        return applyAllSlots
            ? Enumerable.Range(0, SunExpIds.TongtianTowerLayerNodeCount)
            : plan.FixedSlots();
    }

    private static TongtianTowerSlotPlan SlotOrFallback(TongtianTowerFloorPlan plan, int visualSlot)
    {
        return plan.TryGetSlot(visualSlot, out var slot)
            ? slot
            : plan.Slots.First();
    }

    private static string NodeType(MapTree.Node? node)
    {
        return node?.data != null ? DictionaryUtil.Get(node.data, "Type") : "";
    }
}
