using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

internal sealed class SolarMemoryMapSyncRepair
{
    public SolarMemoryMapSyncRepair(SolarMemoryFixedNodeSpec spec)
    {
        SlotIndex = spec.SlotIndex;
        Layer = spec.Layer;
        MapSlotIndex = spec.MapSlotIndex;
        MapId = spec.MapId;
        NodeId = spec.NodeId;
    }

    public int SlotIndex { get; }

    public int Layer { get; }

    public int MapSlotIndex { get; }

    public string MapId { get; }

    public string NodeId { get; }
}

internal static class SolarMemoryMapSyncRepairService
{
    public static int Repair(
        string[]? maps,
        string[]? mapData,
        int layer,
        Action<SolarMemoryMapSyncRepair>? onRepair = null)
    {
        if (maps == null || mapData == null || maps.Length == 0 || mapData.Length == 0)
        {
            return 0;
        }

        var normalizedLayer = SolarMemoryFixedNodeCatalog.ClampLayer(layer);
        var fixedSpecs = SolarMemoryFixedNodeCatalog.ForLayer(normalizedLayer);
        var repairCount = 0;
        for (var i = 0; i < fixedSpecs.Count; i++)
        {
            if (RepairIndex(maps, mapData, fixedSpecs[i], onRepair))
            {
                repairCount++;
            }
        }

        var count = Math.Min(maps.Length, mapData.Length);
        for (var i = 0; i < count; i++)
        {
            if (IsFixedSlot(fixedSpecs, i)
                || !TerriasIds.IsSolarMemoryExclusiveMapId(maps[i])
                    && !TerriasIds.IsSolarMemoryExclusiveEventId(mapData[i]))
            {
                continue;
            }

            if (RepairIndex(
                    maps,
                    mapData,
                    SolarMemoryFixedNodeSpec.Event(i, normalizedLayer, i),
                    onRepair))
            {
                repairCount++;
            }
        }

        return repairCount;
    }

    private static bool IsFixedSlot(IReadOnlyList<SolarMemoryFixedNodeSpec> specs, int slotIndex)
    {
        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].SlotIndex == slotIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RepairIndex(
        string[] maps,
        string[] mapData,
        SolarMemoryFixedNodeSpec spec,
        Action<SolarMemoryMapSyncRepair>? onRepair)
    {
        if (spec.SlotIndex < 0
            || spec.SlotIndex >= maps.Length
            || spec.SlotIndex >= mapData.Length)
        {
            return false;
        }

        var changed = false;
        if (!string.Equals(maps[spec.SlotIndex], spec.MapId, StringComparison.Ordinal))
        {
            maps[spec.SlotIndex] = spec.MapId;
            changed = true;
        }

        if (!string.Equals(mapData[spec.SlotIndex], spec.NodeId, StringComparison.Ordinal))
        {
            mapData[spec.SlotIndex] = spec.NodeId;
            changed = true;
        }

        if (changed)
        {
            onRepair?.Invoke(new SolarMemoryMapSyncRepair(spec));
        }

        return changed;
    }
}
