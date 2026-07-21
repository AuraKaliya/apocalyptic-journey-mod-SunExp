using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.Mechanics;

public static class EndlessSeaMapProjectionService
{
    public static IReadOnlyList<MapTree.Node> NativeDefaultOrder(MapTree tree, EndlessSeaFloorPlan plan)
    {
        var result = new List<MapTree.Node>(SunExpIds.EndlessSeaNativeDefaultNodeCount);
        var boss = SlotOrFallback(plan, SunExpIds.EndlessSeaBossSlotIndex);
        result.Add(EndlessSeaNodePoolService.CreateNode(
            tree,
            plan.Floor,
            SunExpIds.EndlessSeaStartSlotIndex,
            EndlessSeaNodeKind.Rest));
        result.Add(boss.ToNode(tree));

        return result;
    }

    public static bool IsNativeBootstrapReady(MapTree tree, EndlessSeaFloorPlan plan)
    {
        if (tree?.DefaultNode == null || tree.DefaultNode.Count < SunExpIds.EndlessSeaNativeDefaultNodeCount)
        {
            return false;
        }

        return NodeType(tree.DefaultNode[0]) != "Fight"
            && NodeType(tree.DefaultNode[1]) == "Fight"
            && plan.IsValid;
    }

    public static IEnumerable<int> SlotsToApply(EndlessSeaFloorPlan plan, bool applyAllSlots)
    {
        return applyAllSlots
            ? Enumerable.Range(0, SunExpIds.EndlessSeaLayerNodeCount)
            : plan.FixedSlots();
    }

    private static EndlessSeaSlotPlan SlotOrFallback(EndlessSeaFloorPlan plan, int visualSlot)
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
