using System;
using System.Collections.Generic;
using System.Reflection;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class SolarMemoryMapNodePoolFactory
{
    public const int OpeningSlotIndex = SolarMemoryFixedNodeCatalog.OpeningSlotIndex;
    public const int MidLayerSlotIndex = SolarMemoryFixedNodeCatalog.MidLayerSlotIndex;
    public const int PenultimateSlotIndex = SolarMemoryFixedNodeCatalog.PenultimateSlotIndex;
    public const int EndingSlotIndex = SolarMemoryFixedNodeCatalog.EndingSlotIndex;
    private static readonly MethodInfo? DiceWithCursorMethod = typeof(Dice).GetMethod(
        "WithCursor",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private const string BossMapNote = "首领";

    public static SolarMemoryMapNodePool GenerateLayer(NormalMapManager manager, MapTree tree)
    {
        var layer = LayerFor(manager);
        var defaultSegmentSize = DefaultLayerSegmentSize();
        var selectSegmentSize = SelectLayerSegmentSize();
        var defaultNodes = new List<MapTree.Node>(defaultSegmentSize);
        var selectNodes = new List<MapTree.Node>(selectSegmentSize);

        for (var i = 0; i < defaultSegmentSize; i++)
        {
            if (i == OpeningSlotIndex)
            {
                defaultNodes.Add(CreateSolarMemoryEventNode(layer, OpeningSlotIndex));
                continue;
            }

            if (i == 1 && TryCreateFixedEndingNode(tree, layer, out var endingNode))
            {
                defaultNodes.Add(endingNode);
                continue;
            }

            if (i == 2 && layer == 2 && TryCreateFixedBossNode(tree, SunExpIds.SolarBossSecondSunMapId, out var secondSunNode))
            {
                defaultNodes.Add(secondSunNode);
                continue;
            }

            defaultNodes.Add(CreateExpandedBossPoolNode(tree, i, layer));
        }

        for (var i = 0; i < selectSegmentSize; i++)
        {
            selectNodes.Add(CreateExpandedBossPoolNode(tree, i, layer));
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
        return SolarMemoryFixedNodeCatalog.ClampLayer(layer);
    }

    public static int EventIndex(int layer, int mapSlotIndex)
    {
        return SolarMemoryFixedNodeCatalog.EventIndex(layer, mapSlotIndex);
    }

    public static int DefaultLayerSegmentSize()
    {
        return Math.Max(1, 2 + GameSaveManager.GetValue<int>(GameVar.ExLockDes));
    }

    public static int SelectLayerSegmentSize()
    {
        return Math.Max(1, 8 - GameSaveManager.GetValue<int>(GameVar.ExDeleteDes));
    }

    public static bool IsSolarMemoryFixedStoryBoss(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return string.Equals(id, SunExpIds.SolarBossOrbitMirrorMapId, StringComparison.Ordinal)
            || string.Equals(id, "solar_memory_boss_orbit_mirror_array", StringComparison.Ordinal)
            || string.Equals(id, SunExpIds.SolarBossSecondSunMapId, StringComparison.Ordinal)
            || string.Equals(id, "solar_memory_boss_second_sun_last_day", StringComparison.Ordinal)
            || string.Equals(id, SunExpIds.SolarBossSaintWunaMapId, StringComparison.Ordinal)
            || string.Equals(id, "solar_memory_boss_saint_wuna", StringComparison.Ordinal);
    }

    public static MapTree.Node CreateFixedBossNode(MapTree tree, string mapId)
    {
        var data = MapRow(mapId);
        if (data == null)
        {
            throw new InvalidOperationException("Missing fixed boss map row: " + mapId);
        }

        return CreateBossNodeFromMapRow(tree, data);
    }

    private static MapTree.Node CreateSolarMemoryEventNode(int layer, int mapSlotIndex)
    {
        var eventIndex = EventIndex(layer, mapSlotIndex);
        var mapId = SunExpIds.SolarMemoryMapIds[eventIndex];
        var shortMapId = SunExpIds.SolarMemoryShortMapIds[eventIndex];
        var eventId = SunExpIds.SolarMemoryFullEventIds[eventIndex];
        var data = SunExpConfigIndex.Row(DataType.Map, mapId)
            ?? SunExpConfigIndex.Row(DataType.Map, shortMapId);
        var node = new MapTree.Node("普通事件");
        node.type = "普通事件";
        node.data = data == null ? new Dictionary<string, string>() : new Dictionary<string, string>(data);
        node.data["Id"] = mapId;
        node.data["Type"] = "Event";
        node.data["Note"] = "普通事件";
        node.data["NodeId"] = eventId;
        node.data["Level"] = "-1";
        node.NodeDice = Dice.Default;
        return node;
    }

    private static bool TryCreateFixedEndingNode(MapTree tree, int layer, out MapTree.Node node)
    {
        node = null!;
        if (layer == 0)
        {
            return false;
        }

        var mapId = layer switch
        {
            1 => SunExpIds.SolarBossOrbitMirrorMapId,
            2 => SunExpIds.SolarBossSaintWunaMapId,
            _ => ""
        };

        return TryCreateFixedBossNode(tree, mapId, out node);
    }

    private static bool TryCreateFixedBossNode(MapTree tree, string mapId, out MapTree.Node node)
    {
        node = null!;
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return false;
        }

        try
        {
            node = CreateFixedBossNode(tree, mapId);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapNodePool] fixed boss node generation failed; map="
                + mapId
                + ": "
                + ex.Message);
            return false;
        }
    }

    private static MapTree.Node CreateExpandedBossPoolNode(MapTree tree, int indexInSegment, int layer)
    {
        try
        {
            var candidates = ExpandedBossCandidates();

            if (candidates.Count == 0)
            {
                SunExpLog.Warn("[SolarMemoryMapNodePool] expanded boss pool empty; falling back to TypeGenerate.");
                var fallbackNode = tree.TypeGenerate(BossMapNote);
                MapNodeSafetyService.EnsureNodeDice(tree, fallbackNode, "SolarMemoryMapNodePoolFactory.TypeGenerateFallback");
                return fallbackNode;
            }

            var data = new RandomPool(candidates, tree.treedice).DrawByCount(1)[0];
            return CreateBossNodeFromMapRow(tree, data);
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
                    ["Note"] = BossMapNote,
                    ["NodeId"] = "map_0",
                    ["Level"] = "-1"
                },
                NodeDice = CreateFightNodeDice(tree)
            };
        }
    }

    private static bool IsExpandedBossCandidate(Dictionary<string, string> row)
    {
        if (row == null)
        {
            return false;
        }

        var id = DictionaryUtil.Get(row, "Id");
        var nodeId = DictionaryUtil.Get(row, "NodeId");
        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(nodeId)
            || id.StartsWith("*", StringComparison.Ordinal)
            || nodeId.StartsWith("*", StringComparison.Ordinal)
            || IsSolarMemoryFixedStoryBoss(id))
        {
            return false;
        }

        if (!string.Equals(DictionaryUtil.Get(row, "Type"), "Fight", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(DictionaryUtil.Get(row, "Note"), BossMapNote, StringComparison.Ordinal))
        {
            return false;
        }

        var level = DictionaryUtil.Get(row, "Level", "-1");
        if (!int.TryParse(level, out _))
        {
            return false;
        }

        return IsBossLevel(nodeId);
    }

    private static List<Dictionary<string, string>> ExpandedBossCandidates()
    {
        return SunExpConfigIndex.FilteredRows(
            DataType.Map,
            "SolarMemory.ExpandedBossCandidates",
            IsExpandedBossCandidate);
    }

    private static bool IsBossLevel(string nodeId)
    {
        try
        {
            var level = SunExpConfigIndex.Row(DataType.Level, nodeId);
            if (level == null)
            {
                return true;
            }

            return DictionaryUtil.Get(level, "Note").IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0
                || DictionaryUtil.Get(level, "Note").Contains(BossMapNote);
        }
        catch
        {
            return true;
        }
    }

    private static Dictionary<string, string>? MapRow(string mapId)
    {
        return SunExpConfigIndex.Row(DataType.Map, mapId);
    }

    private static MapTree.Node CreateBossNodeFromMapRow(MapTree tree, Dictionary<string, string> row)
    {
        var node = new MapTree.Node(BossMapNote);
        node.type = BossMapNote;
        node.data = new Dictionary<string, string>(row);
        node.data["Type"] = "Fight";
        node.data["Note"] = BossMapNote;
        node.NodeDice = CreateFightNodeDice(tree);
        return node;
    }

    private static Dice CreateFightNodeDice(MapTree tree)
    {
        var dice = tree?.treedice;
        if (dice == null)
        {
            return Dice.Default;
        }

        try
        {
            var cursor = dice.Roll().Value;
            return DiceWithCursorMethod?.Invoke(dice, new object[] { cursor }) as Dice ?? dice;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapNodePool] failed to fork fight NodeDice: " + ex.Message);
            return dice;
        }
    }
}
