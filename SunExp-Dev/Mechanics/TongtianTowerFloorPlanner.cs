using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerFloorPlanner
{
    public static TongtianTowerFloorPlan Create(MapTree tree, int floor)
    {
        var normalizedFloor = Math.Max(1, floor);
        var slots = new List<TongtianTowerSlotPlan>(SunExpIds.TongtianTowerNativeDefaultNodeCount);
        var startNode = TongtianTowerNodePoolService.CreateNode(
            tree,
            normalizedFloor,
            SunExpIds.TongtianTowerStartSlotIndex,
            TongtianTowerNodeKind.Monster);
        var startSlot = TongtianTowerSlotPlan.FromNode(
            SunExpIds.TongtianTowerStartSlotIndex,
            TongtianTowerNodeKind.Monster,
            startNode);
        startSlot.Locked = true;
        slots.Add(startSlot);

        var bossKind = TongtianTowerRewardPlan.IsEndless(normalizedFloor)
            ? TongtianTowerNodeKind.EndlessBoss
            : TongtianTowerNodeKind.Boss;
        var bossNode = TongtianTowerNodePoolService.CreateNode(
            tree,
            normalizedFloor,
            SunExpIds.TongtianTowerBossSlotIndex,
            bossKind);
        slots.Add(TongtianTowerSlotPlan.FromNode(
            SunExpIds.TongtianTowerBossSlotIndex,
            bossKind,
            bossNode));

        var plan = new TongtianTowerFloorPlan
        {
            Floor = normalizedFloor,
            BuildingSlot = -1,
            Slots = slots
        };
        plan.Normalize();
        return plan;
    }
}
