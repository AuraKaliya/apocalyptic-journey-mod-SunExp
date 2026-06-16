using System;
using System.Collections.Generic;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class SolarMemoryMapNodePoolFactory
{
    public const int OpeningSlotIndex = 0;
    public const int MidLayerSlotIndex = 3;

    public static SolarMemoryMapNodePool GenerateLayer(NormalMapManager manager, MapTree tree)
    {
        var layer = LayerFor(manager);
        var defaultSegmentSize = DefaultLayerSegmentSize();
        var selectSegmentSize = SelectLayerSegmentSize();
        var defaultNodes = new List<MapTree.Node>(defaultSegmentSize);
        var selectNodes = new List<MapTree.Node>(selectSegmentSize);

        for (var i = 0; i < defaultSegmentSize; i++)
        {
            defaultNodes.Add(i == OpeningSlotIndex
                ? CreateSolarMemoryEventNode(layer, OpeningSlotIndex)
                : CreateBossChainNode(tree, i, layer));
        }

        for (var i = 0; i < selectSegmentSize; i++)
        {
            selectNodes.Add(i == MidLayerSlotIndex
                ? CreateSolarMemoryEventNode(layer, MidLayerSlotIndex)
                : CreateBossChainNode(tree, i, layer));
        }

        SunExpLog.Debug("[SolarMemoryMapNodePool] generated layer="
            + layer
            + "; level="
            + manager.Level
            + "; defaultSegment="
            + defaultSegmentSize
            + "; selectSegment="
            + selectSegmentSize);

        return new SolarMemoryMapNodePool(layer, manager.Level, defaultSegmentSize, selectSegmentSize, defaultNodes, selectNodes);
    }

    public static int LayerFor(NormalMapManager manager)
    {
        return ClampLayer(manager.Level / 6);
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

    public static int DefaultLayerSegmentSize()
    {
        return Math.Max(1, 2 + GameSaveManager.GetValue<int>(GameVar.ExLockDes));
    }

    public static int SelectLayerSegmentSize()
    {
        return Math.Max(1, 8 - GameSaveManager.GetValue<int>(GameVar.ExDeleteDes));
    }

    private static MapTree.Node CreateSolarMemoryEventNode(int layer, int mapSlotIndex)
    {
        var eventIndex = EventIndex(layer, mapSlotIndex);
        var mapId = SunExpIds.SolarMemoryMapIds[eventIndex];
        var shortMapId = SunExpIds.SolarMemoryShortMapIds[eventIndex];
        var eventId = SunExpIds.SolarMemoryFullEventIds[eventIndex];
        var data = Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, mapId)
            ?? Singleton<GameConfigManager>.Instance.GetOne(DataType.Map, shortMapId);
        var node = new MapTree.Node("普通事件");
        node.type = "普通事件";
        node.data = data == null ? new Dictionary<string, string>() : new Dictionary<string, string>(data);
        node.data["Id"] = mapId;
        node.data["Type"] = "Event";
        node.data["Note"] = "普通事件";
        node.data["NodeId"] = eventId;
        node.data["Level"] = "-1";
        return node;
    }

    private static MapTree.Node CreateBossChainNode(MapTree tree, int indexInSegment, int layer)
    {
        try
        {
            return tree.TypeGenerate("首领");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapNodePool] boss node generation failed at layer "
                + layer
                + ", slot "
                + indexInSegment
                + ": "
                + ex.Message);
            return new MapTree.Node("首领")
            {
                type = "首领",
                data = new Dictionary<string, string>
                {
                    ["Id"] = "map_0",
                    ["Type"] = "Fight",
                    ["Note"] = "首领",
                    ["NodeId"] = "map_0",
                    ["Level"] = "-1"
                }
            };
        }
    }
}
