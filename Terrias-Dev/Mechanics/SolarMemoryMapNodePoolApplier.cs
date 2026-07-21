using System;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Mechanics;

public static class SolarMemoryMapNodePoolApplier
{
    private static int eventRecordCountBeforeMapGeneration = -1;
    private static int eventRecordLayerBeforeMapGeneration = -1;

    public static void CaptureGenerationState(NormalMapManager manager)
    {
        eventRecordCountBeforeMapGeneration = GameSaveManager.GetEventRecord()?.Count ?? 0;
        eventRecordLayerBeforeMapGeneration = SolarMemoryMapNodePoolFactory.LayerFor(manager);
        TerriasLog.Info("[SolarMemoryMapNodePool] captured event record before RandomGenerate: layer="
            + eventRecordLayerBeforeMapGeneration
            + "; count="
            + eventRecordCountBeforeMapGeneration
            + "; level="
            + manager.Level);
    }

    public static void ResetGenerationCapture()
    {
        eventRecordCountBeforeMapGeneration = -1;
        eventRecordLayerBeforeMapGeneration = -1;
    }

    public static bool ApplyToCurrentLayer(NormalMapManager manager, string source, bool trimEventRecord)
    {
        var tree = manager.MapTree;
        if (tree == null)
        {
            TerriasLog.Warn("[SolarMemoryMapNodePool] skipped apply from " + source + ": MapTree is null.");
            return false;
        }

        var pool = SolarMemoryMapNodePoolFactory.GenerateLayer(manager, tree);
        var changed = ApplyDefaultLayer(tree, pool, source);
        changed = ApplySelectLayer(tree, pool, source) || changed;
        if (trimEventRecord)
        {
            TrimSolarMemoryEventRecord(pool.Layer);
        }

        TerriasLog.Info("[SolarMemoryMapNodePool] apply finished from "
            + source
            + "; changed="
            + changed
            + "; level="
            + manager.Level
            + "; layer="
            + pool.Layer
            + "; defaultSegment="
            + pool.DefaultSegmentSize
            + "; selectSegment="
            + pool.SelectSegmentSize);
        return changed;
    }

    private static bool ApplyDefaultLayer(MapTree tree, SolarMemoryMapNodePool pool, string source)
    {
        var defaultStart = pool.Layer * pool.DefaultSegmentSize;
        if (defaultStart < 0 || defaultStart >= tree.DefaultNode.Count)
        {
            TerriasLog.Warn("[SolarMemoryMapNodePool] default segment out of range from "
                + source
                + "; start="
                + defaultStart
                + "; count="
                + tree.DefaultNode.Count);
            return false;
        }

        var changed = false;
        var count = Math.Min(pool.DefaultNodes.Count, tree.DefaultNode.Count - defaultStart);
        for (var i = 0; i < count; i++)
        {
            var targetIndex = defaultStart + i;
            var replacement = pool.DefaultNodes[i];
            MapNodeSafetyService.EnsureNodeDice(tree, replacement, "SolarMemoryMapNodePoolApplier.Default");
            if (!EquivalentNode(tree.DefaultNode[targetIndex], replacement))
            {
                tree.DefaultNode[targetIndex] = replacement;
                changed = true;
            }
        }

        return changed;
    }

    private static bool ApplySelectLayer(MapTree tree, SolarMemoryMapNodePool pool, string source)
    {
        var selectStart = pool.Layer * pool.SelectSegmentSize;
        if (selectStart < 0 || selectStart >= tree.SelectNode.Count)
        {
            TerriasLog.Warn("[SolarMemoryMapNodePool] select segment out of range from "
                + source
                + "; start="
                + selectStart
                + "; count="
                + tree.SelectNode.Count);
            return false;
        }

        var changed = false;
        var count = Math.Min(pool.SelectNodes.Count, tree.SelectNode.Count - selectStart);
        for (var i = 0; i < count; i++)
        {
            var targetIndex = selectStart + i;
            if (i != SolarMemoryMapNodePoolFactory.MidLayerSlotIndex && IsBreakNode(tree.SelectNode[targetIndex]))
            {
                MapNodeSafetyService.EnsureNodeDice(tree, tree.SelectNode[targetIndex], "SolarMemoryMapNodePoolApplier.PreservedBreak");
                TerriasLog.Debug("[SolarMemoryMapNodePool] preserved Break node at select slot " + i + ".");
                continue;
            }

            var replacement = pool.SelectNodes[i];
            MapNodeSafetyService.EnsureNodeDice(tree, replacement, "SolarMemoryMapNodePoolApplier.Select");
            if (!EquivalentNode(tree.SelectNode[targetIndex], replacement))
            {
                tree.SelectNode[targetIndex] = replacement;
                changed = true;
            }
        }

        return changed;
    }

    private static void TrimSolarMemoryEventRecord(int layer)
    {
        var records = GameSaveManager.GetEventRecord();
        if (records == null
            || eventRecordCountBeforeMapGeneration < 0
            || eventRecordLayerBeforeMapGeneration != layer)
        {
            TerriasLog.Debug("[SolarMemoryMapNodePool] event record trim skipped; captureCount="
                + eventRecordCountBeforeMapGeneration
                + "; captureLayer="
                + eventRecordLayerBeforeMapGeneration
                + "; applyLayer="
                + layer);
            ResetGenerationCapture();
            return;
        }

        var beforeTrim = records.Count;
        while (records.Count > eventRecordCountBeforeMapGeneration)
        {
            records.RemoveAt(records.Count - 1);
        }

        if (beforeTrim != records.Count)
        {
            TerriasLog.Info("[SolarMemoryMapNodePool] trimmed event record after RandomGenerate: before="
                + beforeTrim
                + "; after="
                + records.Count
                + "; layer="
                + layer);
        }

        ResetGenerationCapture();
    }

    private static bool EquivalentNode(MapTree.Node? left, MapTree.Node? right)
    {
        return string.Equals(NodeField(left, "Id"), NodeField(right, "Id"), StringComparison.Ordinal)
            && string.Equals(NodeField(left, "NodeId"), NodeField(right, "NodeId"), StringComparison.Ordinal)
            && string.Equals(NodeField(left, "Type"), NodeField(right, "Type"), StringComparison.Ordinal);
    }

    private static string NodeField(MapTree.Node? node, string key)
    {
        return node?.data != null && node.data.TryGetValue(key, out var value) ? value : "";
    }

    private static bool IsBreakNode(MapTree.Node node)
    {
        if (node?.data == null)
        {
            return false;
        }

        return (node.data.TryGetValue("NodeId", out var nodeId) && nodeId.Contains("Breaks"))
            || (node.data.TryGetValue("Id", out var id) && id.Contains("Breaks"));
    }
}
