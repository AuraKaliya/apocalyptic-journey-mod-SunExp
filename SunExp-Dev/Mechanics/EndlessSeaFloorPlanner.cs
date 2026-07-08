using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.Mechanics;

public static class EndlessSeaFloorPlanner
{
    public static EndlessSeaFloorPlan Create(MapTree tree, int floor)
    {
        var normalizedFloor = Math.Max(1, floor);
        var slots = new List<EndlessSeaSlotPlan>(SunExpIds.EndlessSeaNativeDefaultNodeCount);
        var startNode = EndlessSeaNodePoolService.CreateNode(
            tree,
            normalizedFloor,
            SunExpIds.EndlessSeaStartSlotIndex,
            EndlessSeaNodeKind.Monster);
        var startSlot = EndlessSeaSlotPlan.FromNode(
            SunExpIds.EndlessSeaStartSlotIndex,
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
            SunExpIds.EndlessSeaBossSlotIndex,
            bossKind);
        slots.Add(EndlessSeaSlotPlan.FromNode(
            SunExpIds.EndlessSeaBossSlotIndex,
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
