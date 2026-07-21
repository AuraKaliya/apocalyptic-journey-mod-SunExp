using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

internal static class SolarMemoryMapLifecycleCoordinator
{
    internal static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RewriteSolarMemoryMap);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairSolarMemoryMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcNextMap", EnsureSolarMemoryCurrentNodeBeforeNextMap);
        RegisterAfter(modConfig, "MapManager.RpcNextMap", SyncSolarMemoryClientLastNodeAfterNextMap);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "SolarMemoryMapLifecycle");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "SolarMemoryMapLifecycle");
    }

    private static void CaptureSolarMemoryGenerationState(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || context.Target is not NormalMapManager manager)
            {
                SolarMemoryMapNodePoolApplier.ResetGenerationCapture();
                return;
            }

            SolarMemoryMapNodePoolApplier.CaptureGenerationState(manager);
        }
        catch (Exception ex)
        {
            SolarMemoryMapNodePoolApplier.ResetGenerationCapture();
            TerriasLog.Error("Solar memory map generation capture failed", ex);
        }
    }

    private static void RewriteSolarMemoryMap(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || context.Target is not NormalMapManager manager)
            {
                return;
            }

            EnsureSolarMemoryMapState(manager, "NormalMapManager.GeneratrMap", true);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory map rewrite failed", ex);
        }
    }

    private static void EnsureSolarMemoryMapBeforeSelect(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            var mapManager = MapManager.Instance;
            if (mapManager?.ModeMapManager is NormalMapManager manager)
            {
                EnsureSolarMemoryMapState(manager, "MapSelectUI.ReadyToSelect", false);
                SolarMemoryBossTransitionCoordinator.TryContinuePendingSaintWunaBoss("MapSelectUI.ReadyToSelect");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory pre-select map repair failed", ex);
        }
    }

    internal static bool EnsureSolarMemoryMapState(NormalMapManager manager, string source, bool trimEventRecord)
    {
        return SolarMemoryMapNodePoolApplier.ApplyToCurrentLayer(manager, source, trimEventRecord);
    }

    internal static void ReapplySolarMemoryFixedSlotLocks(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            if (!HasSolarMemoryCurrentNodeReady()
                && !TryRestoreSolarMemoryCurrentNodeFromMapManager("MapSelectUI.ShowMap"))
            {
                TerriasLog.Debug("[SolarMemoryMapLock] skipped fixed slot apply from MapSelectUI.ShowMap: current node is not ready.");
                return;
            }

            SolarMemoryMapProjectionRuntime.ApplySolarMemoryFixedSlots(
                mapSelect,
                MapManager.Instance?.ModeMapManager as NormalMapManager,
                false,
                "MapSelectUI.ShowMap");
            SolarMemoryBossTransitionCoordinator.TryContinuePendingSaintWunaBoss("MapSelectUI.ShowMap");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory fixed slot lock repair failed", ex);
        }
    }

    private static Dictionary<string, string>? MapRow(string mapId)
    {
        return TerriasConfigIndex.Row(DataType.Map, mapId);
    }

    private static int CurrentSolarMemoryLayer()
    {
        if (MapManager.Instance?.ModeMapManager is not NormalMapManager manager)
        {
            return 0;
        }

        return SolarMemoryFixedNodeCatalog.ClampLayer(manager.Level / 6);
    }

    private static void RepairSolarMemoryMapSelection(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is string[] maps && args[i + 1] is string[] mapData)
                {
                    if (RepairSolarMemoryMapArrays(maps, mapData))
                    {
                        TerriasLog.Info("[SolarMemoryMapSync] map selection arrays repaired.");
                    }

                    TryRestoreSolarMemoryCurrentNodeFromSyncArrays(maps, mapData, "MapManager.MapSelectionSync");
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory map selection repair failed", ex);
        }
    }

    private static void EnsureSolarMemoryCurrentNodeBeforeNextMap(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            if (MapManager.Instance?.MapTree?.currentNode == null)
            {
                TryRestoreSolarMemoryCurrentNodeFromMapManager("MapManager.RpcNextMap");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SolarMemoryMapSync] pre-next-map current node repair failed: " + ex.Message);
        }
    }

    private static void SyncSolarMemoryClientLastNodeAfterNextMap(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            var node = MapManager.Instance?.MapTree?.currentNode;
            if (node != null)
            {
                GameSaveManager.UpdateNode(node);
                TerriasLog.Debug("[SolarMemoryMapSync] synced client save node after RpcNextMap.");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SolarMemoryMapSync] post-next-map save node sync failed: " + ex.Message);
        }
    }

    internal static bool RepairSolarMemoryMapArrays(string[] maps, string[] mapData)
    {
        var layer = CurrentSolarMemoryLayer();
        var repairCount = SolarMemoryMapSyncRepairService.Repair(maps, mapData, layer, repair =>
        {
            TerriasLog.Info("[SolarMemoryMapSync] repaired index="
                + repair.SlotIndex
                + "; layer="
                + repair.Layer
                + "; slot="
                + repair.MapSlotIndex
                + "; map="
                + repair.MapId
                + "; node="
                + repair.NodeId);
        });

        return repairCount > 0;
    }

    internal static bool TryRestoreSolarMemoryCurrentNodeFromMapManager(string source, bool clientOnly = true)
    {
        var mapManager = MapManager.Instance;
        return mapManager != null
            && TryRestoreSolarMemoryCurrentNodeFromSyncArrays(mapManager.mapList, mapManager.mapData, source, clientOnly);
    }

    private static bool TryRestoreSolarMemoryCurrentNodeFromSyncArrays(string[]? maps, string[]? mapData, string source, bool clientOnly = true)
    {
        try
        {
            if ((clientOnly && !IsClientOnlyPlayer())
                || HasSolarMemoryCurrentNodeReady()
                || maps == null
                || mapData == null)
            {
                return false;
            }

            var tree = MapManager.Instance?.MapTree;
            var count = Math.Min(maps.Length, mapData.Length);
            if (tree == null || count <= 0)
            {
                return false;
            }

            var first = BuildSolarMemorySyncedNodeChain(tree, maps, mapData, count);
            if (first == null)
            {
                return false;
            }

            tree.currentNode = first;
            GameSaveManager.UpdateNode(first);
            TerriasLog.Info("[SolarMemoryMapSync] restored client current node from sync arrays; source="
                + source
                + "; count="
                + count
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SolarMemoryMapSync] failed to restore client current node from "
                + source
                + ": "
                + ex.Message);
            return false;
        }
    }

    private static MapTree.Node? BuildSolarMemorySyncedNodeChain(MapTree tree, string[] maps, string[] mapData, int count)
    {
        MapTree.Node? first = null;
        MapTree.Node? previous = null;
        for (var i = 0; i < count; i++)
        {
            var node = CreateSolarMemorySyncedNode(tree, maps[i], mapData[i], i);
            if (first == null)
            {
                first = node;
            }
            else
            {
                previous?.SetChild(0, node);
            }

            previous = node;
        }

        return first;
    }

    private static MapTree.Node CreateSolarMemorySyncedNode(MapTree tree, string? mapId, string? nodeId, int index)
    {
        var data = CreateSolarMemorySyncedNodeData(mapId, nodeId);
        var type = data == null ? "null" : Field(data, "Note");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = data == null ? "null" : Field(data, "Type");
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            type = "Map";
        }

        return new MapTree.Node(type)
        {
            type = type,
            data = data,
            NodeDice = SyncedNodeDice(tree, index)
        };
    }

    private static Dictionary<string, string>? CreateSolarMemorySyncedNodeData(string? mapId, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return null;
        }

        var normalizedMapId = mapId!;
        var row = MapRow(normalizedMapId);
        var data = row == null ? new Dictionary<string, string>() : new Dictionary<string, string>(row);
        data["Id"] = normalizedMapId;
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            data["NodeId"] = nodeId!;
        }
        else if (!data.ContainsKey("NodeId"))
        {
            data["NodeId"] = normalizedMapId;
        }

        if (!data.ContainsKey("Type") || string.IsNullOrWhiteSpace(data["Type"]))
        {
            data["Type"] = IsSolarMemoryEventId(nodeId) || IsSolarMemoryMapId(normalizedMapId) ? "Event" : "Fight";
        }

        if (!data.ContainsKey("Level") || string.IsNullOrWhiteSpace(data["Level"]))
        {
            data["Level"] = "-1";
        }

        return data;
    }

    private static Dice SyncedNodeDice(MapTree tree, int index)
    {
        return tree.treedice ?? Dice.Default;
    }

    private static bool HasSolarMemoryCurrentNodeReady()
    {
        try
        {
            var currentNode = MapManager.Instance?.MapTree?.currentNode;
            var saveNode = GameSaveManager.GetNode();
            return currentNode != null
                && saveNode != null
                && (IsUsableSolarMemoryMapNode(currentNode) || IsUsableSolarMemoryMapNode(saveNode));
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsUsableSolarMemoryMapNode(MapTree.Node? node)
    {
        return node != null && (node.data != null || node.childrens != null);
    }

    internal static void EnsureSolarMemoryNodeDice(MapTree.Node? node, MapTree tree, string source)
    {
        if (node == null || node.NodeDice != null)
        {
            return;
        }

        node.NodeDice = tree.treedice ?? Dice.Default;
        TerriasLog.Debug("[SolarMemoryMapSync] repaired current node dice from " + source + ".");
    }

    internal static bool IsClientOnlyPlayer()
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            return playerManager != null && !playerManager.isServer;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSolarMemoryMapId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return TerriasIds.SolarMemoryMapIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || TerriasIds.SolarMemoryShortMapIds.Any(value => string.Equals(id, value, StringComparison.Ordinal));
    }

    private static bool IsSolarMemoryEventId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return TerriasIds.SolarMemoryFullEventIds.Any(value => string.Equals(id, value, StringComparison.Ordinal))
            || TerriasIds.SolarMemoryEventIds.Any(value => string.Equals(id, value, StringComparison.Ordinal));
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value : "";
    }
}
