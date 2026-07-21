using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaFloorPlanner
{
    public static EndlessSeaFloorPlan Create(MapTree tree, int floor)
    {
        var normalizedFloor = Math.Max(1, floor);
        var slots = new List<EndlessSeaSlotPlan>(TerriasIds.EndlessSeaNativeDefaultNodeCount);
        var startNode = EndlessSeaNodePoolService.CreateNode(
            tree,
            normalizedFloor,
            TerriasIds.EndlessSeaStartSlotIndex,
            EndlessSeaNodeKind.Monster);
        var startSlot = EndlessSeaSlotPlan.FromNode(
            TerriasIds.EndlessSeaStartSlotIndex,
            EndlessSeaNodeKind.Monster,
            startNode);
        startSlot.Locked = true;
        slots.Add(startSlot);

        var bossKind = EndlessSeaRewardPlan.IsEndless(normalizedFloor)
            ? EndlessSeaNodeKind.EndlessBoss
            : EndlessSeaNodeKind.Boss;
        var bossNode = EndlessSeaNodePoolService.CreateNode(
            tree,
            normalizedFloor,
            TerriasIds.EndlessSeaBossSlotIndex,
            bossKind);
        slots.Add(EndlessSeaSlotPlan.FromNode(
            TerriasIds.EndlessSeaBossSlotIndex,
            bossKind,
            bossNode));

        var plan = new EndlessSeaFloorPlan
        {
            Floor = normalizedFloor,
            BuildingSlot = -1,
            Slots = slots
        };
        plan.Normalize();
        return plan;
    }
}
