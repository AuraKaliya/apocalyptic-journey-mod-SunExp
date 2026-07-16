using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryContentIsolationRuntime
{
    private const string NormalEventNote = "普通事件";
    private const string BossNote = "首领";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", SanitizeGeneratedMap);
        RegisterAfter(modConfig, "SublimationManager.GeneratrMap", SanitizeGeneratedMap);
        RegisterAfter(modConfig, "TeachMapManager.GeneratrMap", SanitizeGeneratedMap);
        RegisterAfter(modConfig, "SlotMachineManager.GeneratrMap", SanitizeGeneratedMap);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", SanitizeMapBeforeSelect);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", SanitizeMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", SanitizeMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", SanitizeMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", SanitizeMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", SanitizeMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", SanitizeMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcNextMap", RepairCurrentNodeBeforeNextMap);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "SolarMemoryContentIsolation");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "SolarMemoryContentIsolation");
    }

    private static void SanitizeGeneratedMap(ModHookContext context)
    {
        if (SolarMemoryModeRuntime.IsSolarMemoryRun())
        {
            return;
        }

        try
        {
            var tree = (context.Target as IModeManager)?.MapTree ?? MapManager.Instance?.MapTree;
            var level = (context.Target as IModeManager)?.Level ?? MapManager.Instance?.ModeMapManager?.Level ?? 0;
            SanitizeTree(tree, level, "generated map");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory generated-map isolation failed", ex);
        }
    }

    private static void SanitizeMapBeforeSelect(ModHookContext context)
    {
        if (SolarMemoryModeRuntime.IsSolarMemoryRun())
        {
            return;
        }

        try
        {
            var manager = MapManager.Instance?.ModeMapManager;
            var level = manager?.Level ?? 0;
            SanitizeTree(manager?.MapTree, level, "map selection");
            MapNodeSafetyService.RestoreCurrentNodeIfMissingOrExclusive(level, "MapSelectUI.ReadyToSelect", clientOnly: true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory pre-select isolation failed", ex);
        }
    }

    private static void SanitizeMapSelection(ModHookContext context)
    {
        if (SolarMemoryModeRuntime.IsSolarMemoryRun())
        {
            return;
        }

        try
        {
            var level = MapManager.Instance?.ModeMapManager?.Level ?? 0;
            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is string[] maps
                    && args[i + 1] is string[] mapData
                    && SanitizeSelectionArrays(maps, mapData, level))
                {
                    SunExpLog.Warn("[SolarMemoryIsolation] removed exclusive content from synchronized map choices.");
                }
            }

            MapNodeSafetyService.RestoreCurrentNodeIfMissingOrExclusive(level, "MapManager.MapSelectionSync", clientOnly: true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory map-sync isolation failed", ex);
        }
    }

    private static void RepairCurrentNodeBeforeNextMap(ModHookContext context)
    {
        if (SolarMemoryModeRuntime.IsSolarMemoryRun())
        {
            return;
        }

        try
        {
            var level = MapManager.Instance?.ModeMapManager?.Level ?? 0;
            MapNodeSafetyService.RestoreCurrentNodeIfMissingOrExclusive(level, "MapManager.RpcNextMap", clientOnly: true);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[MapNodeSafety] pre-next-map repair failed: " + ex.Message);
        }
    }

    private static bool SanitizeTree(MapTree? tree, int level, string source)
    {
        if (tree == null)
        {
            return false;
        }

        var changed = SanitizeNodes(tree, tree.DefaultNode, level);
        changed = SanitizeNodes(tree, tree.SelectNode, level) || changed;
        changed = SanitizeCurrentNode(tree, level) || changed;
        if (changed)
        {
            SunExpLog.Warn("[SolarMemoryIsolation] removed exclusive nodes from " + source + ".");
        }

        return changed;
    }

    private static bool SanitizeNodes(MapTree tree, IList<MapTree.Node>? nodes, int level)
    {
        if (nodes == null)
        {
            return false;
        }

        var changed = false;
        foreach (var node in nodes)
        {
            if (node?.data == null || !MapNodeSafetyService.IsExclusiveNode(node))
            {
                MapNodeSafetyService.EnsureNodeDice(tree, node, "SolarMemoryIsolation.SanitizeNodes");
                continue;
            }

            var oldNodeId = DictionaryUtil.Get(node.data, "NodeId");
            var fallback = FindFallbackMap(node.data, level);
            if (fallback == null)
            {
                SunExpLog.Warn("[SolarMemoryIsolation] no base-map fallback found for " + DictionaryUtil.Get(node.data, "Id") + ".");
                continue;
            }

            ApplyFallbackToNode(node, fallback, oldNodeId);
            MapNodeSafetyService.EnsureNodeDice(tree, node, "SolarMemoryIsolation.SanitizeNodes");

            changed = true;
        }

        return changed;
    }

    private static bool SanitizeCurrentNode(MapTree tree, int level)
    {
        var node = tree.currentNode;
        if (node?.data == null || !MapNodeSafetyService.IsExclusiveNode(node))
        {
            MapNodeSafetyService.EnsureNodeDice(tree, node, "SolarMemoryIsolation.CurrentNode");
            return false;
        }

        var oldNodeId = DictionaryUtil.Get(node.data, "NodeId");
        var fallback = FindFallbackMap(node.data, level);
        if (fallback == null)
        {
            SunExpLog.Warn("[SolarMemoryIsolation] no current-node fallback found for "
                + DictionaryUtil.Get(node.data, "Id")
                + ".");
            return false;
        }

        ApplyFallbackToNode(node, fallback, oldNodeId);
        MapNodeSafetyService.EnsureNodeDice(tree, node, "SolarMemoryIsolation.CurrentNode");
        return true;
    }

    private static void ApplyFallbackToNode(MapTree.Node node, IDictionary<string, string> fallback, string oldNodeId)
    {
        node.data = new Dictionary<string, string>(fallback);
        node.type = DictionaryUtil.Get(fallback, "Note", node.type);
        if (string.Equals(DictionaryUtil.Get(fallback, "Type"), "Event", StringComparison.Ordinal))
        {
            node.data["NodeId"] = ResolveEventId(oldNodeId, fallback);
        }
    }

    private static bool SanitizeSelectionArrays(string[] maps, string[] mapData, int level)
    {
        var changed = false;
        var count = Math.Min(maps.Length, mapData.Length);
        for (var i = 0; i < count; i++)
        {
            if (!SunExpIds.IsSolarMemoryExclusiveMapId(maps[i])
                && !SunExpIds.IsSolarMemoryExclusiveEventId(mapData[i]))
            {
                continue;
            }

            var exclusiveRow = FindMapById(maps[i]);
            var fallback = FindFallbackMap(exclusiveRow, level, maps[i]);
            if (fallback == null)
            {
                SunExpLog.Warn("[SolarMemoryIsolation] no synchronized fallback found for " + maps[i] + ".");
                continue;
            }

            maps[i] = DictionaryUtil.Get(fallback, "Id");
            mapData[i] = string.Equals(DictionaryUtil.Get(fallback, "Type"), "Event", StringComparison.Ordinal)
                ? ResolveEventId(mapData[i], fallback)
                : DictionaryUtil.Get(fallback, "NodeId");
            changed = true;
        }

        return changed;
    }

    private static Dictionary<string, string>? FindFallbackMap(
        IDictionary<string, string>? exclusiveRow,
        int level,
        string exclusiveMapId = "")
    {
        var type = DictionaryUtil.Get(exclusiveRow, "Type");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = exclusiveMapId.IndexOf("_boss_", StringComparison.Ordinal) >= 0 ? "Fight" : "Event";
        }

        var note = DictionaryUtil.Get(exclusiveRow, "Note");
        if (string.IsNullOrWhiteSpace(note))
        {
            note = string.Equals(type, "Fight", StringComparison.Ordinal) ? BossNote : NormalEventNote;
        }

        var layer = Math.Max(0, level / 12);
        return SunExpConfigIndex.Rows(DataType.Map)
            .Where(row => !SunExpIds.IsSolarMemoryExclusiveMapId(DictionaryUtil.Get(row, "Id")))
            .Where(row => !string.Equals(DictionaryUtil.Get(row, "Rarity", "0"), "7", StringComparison.Ordinal))
            .Where(row => string.Equals(DictionaryUtil.Get(row, "Type"), type, StringComparison.Ordinal))
            .Where(row => string.Equals(DictionaryUtil.Get(row, "Note"), note, StringComparison.Ordinal))
            .Where(row => !DictionaryUtil.Get(row, "NodeId").Contains("Breaks"))
            .Where(row => !Singleton<GameRuntimeData>.Instance.IsLocked(DictionaryUtil.Get(row, "Id")))
            .Where(row => !string.Equals(note, BossNote, StringComparison.Ordinal)
                || DictionaryUtil.Get(row, "Level") == "-1"
                || DictionaryUtil.ParseInt(DictionaryUtil.Get(row, "Level"), -999) == layer)
            .OrderBy(row => DictionaryUtil.Get(row, "Level") == layer.ToString() ? 0 : 1)
            .ThenBy(row => DictionaryUtil.Get(row, "Id"), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static Dictionary<string, string>? FindMapById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return SunExpConfigIndex.Row(DataType.Map, id);
    }

    private static string ResolveEventId(string oldNodeId, IDictionary<string, string> fallback)
    {
        if (!string.IsNullOrWhiteSpace(oldNodeId) && !SunExpIds.IsSolarMemoryExclusiveEventId(oldNodeId))
        {
            return oldNodeId;
        }

        var fallbackNodeId = DictionaryUtil.Get(fallback, "NodeId");
        if (!string.IsNullOrWhiteSpace(fallbackNodeId)
            && !fallbackNodeId.Contains("Breaks")
            && !SunExpIds.IsSolarMemoryExclusiveEventId(fallbackNodeId))
        {
            return fallbackNodeId;
        }

        return SunExpConfigIndex.Rows(DataType.Event)
            .Where(row => !DictionaryUtil.Get(row, "Id").Contains("Sub"))
            .Where(row => !Singleton<GameRuntimeData>.Instance.IsLocked(DictionaryUtil.Get(row, "Id")))
            .OrderBy(row => DictionaryUtil.Get(row, "Id"), StringComparer.Ordinal)
            .Select(row => DictionaryUtil.Get(row, "Id"))
            .FirstOrDefault() ?? "";
    }
}
