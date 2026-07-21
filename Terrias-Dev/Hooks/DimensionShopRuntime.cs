using System;
using System.Collections.Generic;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class DimensionShopRuntime
{
    private const string NativeMapItemNodeId = "Breaks";
    private static readonly Dictionary<MapTree.Node, string> PendingNativeNodeIds = new();
    private static bool pendingResidualRoute;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "NormalMapManager.RandomGenerate", InjectFirstLayerCandidate);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", RepairBeforeSelection);
        RegisterBefore(modConfig, "MapSelectUI.SetNodes", RestoreBeforeMapSelectionBoundary);
        RegisterBefore(modConfig, "MapSelectUI.SelectMap", RestoreBeforeMapSelectionBoundary);
        RegisterBefore(modConfig, "MapItem.Init", PrepareDimensionShopMapItem);
        RegisterAfter(modConfig, "MapItem.Init", RestoreDimensionShopMapItem);
        RegisterBefore(modConfig, "MapItem.OnPointerDown", RestoreBeforeMapItemBoundary);
        RegisterBefore(modConfig, "Commands.load", PrepareDimensionShopRoute);
        RegisterAfter(modConfig, "Commands.load", OpenDimensionShop);
        TerriasBattleLifecycleRouter.Register("DimensionShop", new TerriasBattleLifecycleSubscription
        {
            AdventureStarting = _ =>
            {
                RestorePendingDimensionShopNodes("AdventureStarting");
                DimensionShopService.EnsureRunSnapshot("AdventureStarting");
            }
        });
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "DimensionShop");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "DimensionShop");
    }

    private static void InjectFirstLayerCandidate(ModHookContext context)
    {
        try
        {
            if (context.Target is NormalMapManager manager)
            {
                EnsureFirstLayerCandidate(manager, "NormalMapManager.RandomGenerate:after");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] first-layer injection failed", ex);
        }
    }

    private static void RepairBeforeSelection(ModHookContext context)
    {
        try
        {
            RestorePendingDimensionShopNodes("MapSelectUI.ReadyToSelect:before");
            if (MapManager.Instance?.ModeMapManager is NormalMapManager manager)
            {
                EnsureFirstLayerCandidate(manager, "MapSelectUI.ReadyToSelect:before");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] pre-selection repair failed", ex);
        }
    }

    private static void EnsureFirstLayerCandidate(NormalMapManager manager, string source)
    {
        if (!DimensionShopService.IsWorldSimulationRun()
            || !IsMapAuthority()
            || manager.Level / 6 != 0
            || manager.MapTree?.SelectNode == null)
        {
            return;
        }

        DimensionShopService.EnsureRunSnapshot(source);
        var count = Math.Max(1, 8 - GameSaveManager.GetValue<int>(GameVar.ExDeleteDes));
        var nodes = manager.MapTree.SelectNode;
        var end = Math.Min(nodes.Count, count);
        for (var i = 0; i < end; i++)
        {
            if (IsDimensionShopNode(nodes[i]))
            {
                return;
            }
        }

        var replacement = -1;
        for (var i = end - 1; i >= 0; i--)
        {
            var nodeId = DictionaryUtil.Get(nodes[i]?.data, "NodeId");
            if (nodeId.IndexOf("Breaks", StringComparison.OrdinalIgnoreCase) < 0)
            {
                replacement = i;
                break;
            }
        }

        if (replacement < 0)
        {
            TerriasLog.Warn("[DimensionShop] no replaceable first-layer candidate from " + source + ".");
            return;
        }

        var row = TerriasConfigIndex.Row(DataType.Map, TerriasIds.DimensionShopMapId)
                  ?? TerriasConfigIndex.Row(DataType.Map, TerriasIds.DimensionShopMapShortId);
        if (row == null)
        {
            TerriasLog.Warn("[DimensionShop] map row is unavailable from " + source + ".");
            return;
        }

        var data = new Dictionary<string, string>(row)
        {
            ["Type"] = "Build",
            ["NodeId"] = TerriasIds.DimensionShopNodeId,
            ["Note"] = "\u5efa\u7b51"
        };
        var previous = nodes[replacement];
        var replacementNode = new MapTree.Node("\u5efa\u7b51")
        {
            data = data,
            NodeDice = previous?.NodeDice
        };
        MapNodeSafetyService.EnsureNodeDice(
            manager.MapTree,
            replacementNode,
            "DimensionShopRuntime.EnsureFirstLayerCandidate");
        nodes[replacement] = replacementNode;
        TerriasLog.Info("[DimensionShop] injected first-layer candidate at index="
                       + replacement
                       + " from "
                       + source
                       + "; nodeDice="
                       + (replacementNode.NodeDice != null ? "present" : "missing")
                       + ".");
    }

    private static void PrepareDimensionShopMapItem(ModHookContext context)
    {
        try
        {
            if (!TryGetMapNode(context, out var node)
                || node.data == null
                || PendingNativeNodeIds.ContainsKey(node))
            {
                return;
            }

            var nodeId = DictionaryUtil.Get(node.data, "NodeId");
            if (!IsDimensionShopNode(node)
                || !string.Equals(nodeId, TerriasIds.DimensionShopNodeId, StringComparison.Ordinal))
            {
                return;
            }

            PendingNativeNodeIds[node] = nodeId;
            node.data["NodeId"] = NativeMapItemNodeId;
            TerriasLog.InfoAlways("[DimensionShop] native MapItem compatibility applied; id="
                                 + DictionaryUtil.Get(node.data, "Id")
                                 + "; originalNodeId="
                                 + nodeId
                                 + "; nativeNodeId="
                                 + NativeMapItemNodeId
                                 + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] native MapItem compatibility prepare failed", ex);
        }
    }

    private static void RestoreDimensionShopMapItem(ModHookContext context)
    {
        if (!TryGetMapNode(context, out var node))
        {
            return;
        }

        RestorePendingDimensionShopNode(node, "MapItem.Init:after", expected: true);
    }

    private static void RestoreBeforeMapItemBoundary(ModHookContext context)
    {
        try
        {
            if (context.Target is MapItem item && item.node != null)
            {
                RestorePendingDimensionShopNode(
                    item.node,
                    "MapItem interaction:before",
                    expected: false);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] MapItem boundary restore failed", ex);
        }
    }

    private static void RestoreBeforeMapSelectionBoundary(ModHookContext context)
    {
        RestorePendingDimensionShopNodes("Map selection boundary:before");
    }

    private static void RestorePendingDimensionShopNodes(string source)
    {
        if (PendingNativeNodeIds.Count == 0)
        {
            return;
        }

        var nodes = new List<MapTree.Node>(PendingNativeNodeIds.Keys);
        foreach (var node in nodes)
        {
            RestorePendingDimensionShopNode(node, source, expected: false);
        }
    }

    private static bool RestorePendingDimensionShopNode(MapTree.Node node, string source, bool expected)
    {
        if (!PendingNativeNodeIds.TryGetValue(node, out var originalNodeId))
        {
            return false;
        }

        try
        {
            if (node.data == null)
            {
                TerriasLog.Warn("[DimensionShop] native MapItem compatibility restore deferred because node data is unavailable; source="
                               + source
                               + ".");
                return false;
            }

            node.data["NodeId"] = originalNodeId;
            PendingNativeNodeIds.Remove(node);
            var message = "[DimensionShop] native MapItem compatibility restored; id="
                          + DictionaryUtil.Get(node.data, "Id")
                          + "; nodeId="
                          + originalNodeId
                          + "; source="
                          + source
                          + ".";
            if (expected)
            {
                TerriasLog.InfoAlways(message);
            }
            else
            {
                TerriasLog.Warn(message);
            }

            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] native MapItem compatibility restore failed from " + source, ex);
            return false;
        }
    }

    private static bool TryGetMapNode(ModHookContext context, out MapTree.Node node)
    {
        node = null!;
        if (context.Arguments == null
            || context.Arguments.Length == 0
            || context.Arguments[0] is not MapTree.Node candidate)
        {
            return false;
        }

        node = candidate;
        return true;
    }

    private static void PrepareDimensionShopRoute(ModHookContext context)
    {
        pendingResidualRoute = false;
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length < 2
                || !string.Equals(Convert.ToString(context.Arguments[0]), "build", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Convert.ToString(context.Arguments[1]), NativeMapItemNodeId, StringComparison.Ordinal))
            {
                return;
            }

            var currentNode = (MapManager.Instance?.ModeMapManager as NormalMapManager)?.MapTree?.currentNode;
            if (currentNode == null || !IsDimensionShopNode(currentNode))
            {
                return;
            }

            pendingResidualRoute = true;
            RestorePendingDimensionShopNode(currentNode, "Commands.load:before", expected: false);
            RestorePendingDimensionShopNodes("Commands.load:before");
            TerriasLog.Warn("[DimensionShop] recovered a residual native NodeId at the command route boundary.");
        }
        catch (Exception ex)
        {
            pendingResidualRoute = false;
            TerriasLog.Error("[DimensionShop] route compatibility prepare failed", ex);
        }
    }

    private static void OpenDimensionShop(ModHookContext context)
    {
        try
        {
            var residualRoute = pendingResidualRoute;
            pendingResidualRoute = false;
            if (context.Arguments == null
                || context.Arguments.Length < 2
                || !string.Equals(Convert.ToString(context.Arguments[0]), "build", StringComparison.OrdinalIgnoreCase)
                || (!string.Equals(Convert.ToString(context.Arguments[1]), TerriasIds.DimensionShopNodeId, StringComparison.Ordinal)
                    && !residualRoute))
            {
                return;
            }

            if (residualRoute)
            {
                DimensionShopGameApi.CloseNativeBreakFallback();
            }

            DimensionShopGameApi.CloseMapUi();
            DimensionShopPanel.Open("Commands.load");
        }
        catch (Exception ex)
        {
            pendingResidualRoute = false;
            TerriasLog.Error("[DimensionShop] open route failed", ex);
            DimensionShopGameApi.AdvanceMap();
        }
    }

    private static bool IsDimensionShopNode(MapTree.Node? node)
    {
        var id = DictionaryUtil.Get(node?.data, "Id");
        var nodeId = DictionaryUtil.Get(node?.data, "NodeId");
        return string.Equals(id, TerriasIds.DimensionShopMapId, StringComparison.Ordinal)
               || string.Equals(id, TerriasIds.DimensionShopMapShortId, StringComparison.Ordinal)
               || string.Equals(nodeId, TerriasIds.DimensionShopNodeId, StringComparison.Ordinal);
    }

    private static bool IsMapAuthority()
    {
        try
        {
            return PlayerManager.Instance == null || PlayerManager.Instance.isServer;
        }
        catch
        {
            return true;
        }
    }
}
