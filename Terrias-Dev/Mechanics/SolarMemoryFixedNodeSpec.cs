using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

internal sealed class SolarMemoryFixedNodeSpec
{
    private SolarMemoryFixedNodeSpec(
        int slotIndex,
        int layer,
        int mapSlotIndex,
        bool isEvent,
        string mapId,
        string nodeId)
    {
        SlotIndex = slotIndex;
        Layer = layer;
        MapSlotIndex = mapSlotIndex;
        IsEvent = isEvent;
        MapId = mapId;
        NodeId = nodeId;
    }

    public int SlotIndex { get; }

    public int Layer { get; }

    public int MapSlotIndex { get; }

    public bool IsEvent { get; }

    public string MapId { get; }

    public string NodeId { get; }

    public static SolarMemoryFixedNodeSpec Event(int slotIndex, int layer, int mapSlotIndex)
    {
        var normalizedLayer = SolarMemoryFixedNodeCatalog.ClampLayer(layer);
        var eventIndex = SolarMemoryFixedNodeCatalog.EventIndex(normalizedLayer, mapSlotIndex);
        return new SolarMemoryFixedNodeSpec(
            slotIndex,
            normalizedLayer,
            mapSlotIndex,
            true,
            SunExpIds.SolarMemoryMapIds[eventIndex],
            SunExpIds.SolarMemoryFullEventIds[eventIndex]);
    }

    public static SolarMemoryFixedNodeSpec Boss(int slotIndex, int layer, string mapId, string levelId)
    {
        return new SolarMemoryFixedNodeSpec(
            slotIndex,
            SolarMemoryFixedNodeCatalog.ClampLayer(layer),
            slotIndex,
            false,
            mapId,
            levelId);
    }
}

internal static class SolarMemoryFixedNodeCatalog
{
    public const int OpeningSlotIndex = 0;
    public const int MidLayerSlotIndex = 3;
    public const int PenultimateSlotIndex = 4;
    public const int EndingSlotIndex = 5;

    private static readonly IReadOnlyList<SolarMemoryFixedNodeSpec>[] Layers =
    {
        Array.AsReadOnly(new[]
        {
            SolarMemoryFixedNodeSpec.Event(OpeningSlotIndex, 0, OpeningSlotIndex),
            SolarMemoryFixedNodeSpec.Event(EndingSlotIndex, 0, EndingSlotIndex)
        }),
        Array.AsReadOnly(new[]
        {
            SolarMemoryFixedNodeSpec.Event(OpeningSlotIndex, 1, OpeningSlotIndex),
            SolarMemoryFixedNodeSpec.Event(MidLayerSlotIndex, 1, MidLayerSlotIndex),
            SolarMemoryFixedNodeSpec.Boss(
                EndingSlotIndex,
                1,
                SunExpIds.SolarBossOrbitMirrorMapId,
                SunExpIds.SolarBossOrbitMirrorLevelId)
        }),
        Array.AsReadOnly(new[]
        {
            SolarMemoryFixedNodeSpec.Event(OpeningSlotIndex, 2, OpeningSlotIndex),
            SolarMemoryFixedNodeSpec.Event(MidLayerSlotIndex, 2, MidLayerSlotIndex),
            SolarMemoryFixedNodeSpec.Boss(
                PenultimateSlotIndex,
                2,
                SunExpIds.SolarBossSecondSunMapId,
                SunExpIds.SolarBossSecondSunLevelId),
            SolarMemoryFixedNodeSpec.Boss(
                EndingSlotIndex,
                2,
                SunExpIds.SolarBossSaintWunaMapId,
                SunExpIds.SolarBossSaintWunaLevelId)
        })
    };

    public static IReadOnlyList<SolarMemoryFixedNodeSpec> ForLayer(int layer)
    {
        return Layers[ClampLayer(layer)];
    }

    public static int ClampLayer(int layer)
    {
        return Math.Max(0, Math.Min(SunExpIds.SolarMemoryMaxLayer - 1, layer));
    }

    public static int EventIndex(int layer, int mapSlotIndex)
    {
        var normalizedLayer = ClampLayer(layer);
        var slot = mapSlotIndex >= MidLayerSlotIndex ? 1 : 0;
        var index = normalizedLayer * 2 + slot;
        return Math.Max(0, Math.Min(SunExpIds.SolarMemoryFullEventIds.Length - 1, index));
    }
}
