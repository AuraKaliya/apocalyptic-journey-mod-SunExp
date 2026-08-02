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
using UnityEngine;

namespace Terrias.Dll.Hooks;

public static class DimensionShopRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "NormalMapManager.RandomGenerate", InjectFirstLayerCandidate);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", RepairBeforeSelection);
        RegisterBefore(modConfig, "MapSelectUI.CreateMapItem", PreparePresentationNodes);
        RegisterBefore(modConfig, "MapItem.Init", EnsureNativePresentationTexture);
        RegisterAfter(modConfig, "MapItem.Init", RestorePresentationNodeIdentity);
        RegisterAfter(modConfig, "MapSelectUI.CreateMapItem", RestorePresentationList);
        RegisterBefore(modConfig, "MapSelectUI.SetNodes", NormalizeRenderedNodeIdentity);
        RegisterAfter(modConfig, "Commands.load", OpenDimensionShop);
        TerriasBattleLifecycleRouter.Register("DimensionShop", new TerriasBattleLifecycleSubscription
        {
            AdventureStarting = _ => DimensionShopService.EnsureRunSnapshot("AdventureStarting")
        });
    }

    private static void PreparePresentationNodes(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not List<MapTree.Node> nodes)
            {
                return;
            }

            for (var index = nodes.Count - 1; index >= 0; index--)
            {
                var authoritative = nodes[index];
                if (!IsDimensionShopNode(authoritative))
                {
                    continue;
                }

                if (!NativeMapTextureLeaseApi.TryEnsurePresentationTexture(
                        out var nativeNodeId,
                        out var diagnostic))
                {
                    nodes.RemoveAt(index);
                    TerriasLog.WarnOnce(
                        "DimensionShop.NativeTextureLease.HideCandidate",
                        "[DimensionShop] " + diagnostic + ".");
                    continue;
                }

                nodes[index] = CreatePresentationClone(
                    authoritative,
                    nativeNodeId);
                TerriasLog.DebugOnce(
                    "DimensionShop.PresentationClone",
                    "[DimensionShop] created a presentation-only map node clone; authoritative NodeId remains "
                    + TerriasIds.DimensionShopNodeId
                    + "; native presentation NodeId="
                    + nativeNodeId
                    + ".");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] presentation clone preparation failed", ex);
        }
    }

    private static void EnsureNativePresentationTexture(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node
                || !IsDimensionShopNode(node))
            {
                return;
            }

            if (NativeMapTextureLeaseApi.TryEnsurePresentationTexture(
                    out var nativeNodeId,
                    out var diagnostic))
            {
                node.data ??= new Dictionary<string, string>();
                node.data["Type"] = "Build";
                node.data["NodeId"] = nativeNodeId;
                TerriasLog.DebugOnce(
                    "DimensionShop.NativeTextureLease",
                    "[DimensionShop] " + diagnostic + ".");
            }
            else
            {
                TerriasLog.WarnOnce(
                    "DimensionShop.NativeTextureLease.Failed",
                    "[DimensionShop] " + diagnostic + ".");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] native map texture lease failed", ex);
        }
    }

    private static MapTree.Node CreatePresentationClone(
        MapTree.Node authoritative,
        string nativeNodeId)
    {
        var data = authoritative.data == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(authoritative.data);
        data["NodeId"] = nativeNodeId;
        var type = string.IsNullOrWhiteSpace(authoritative.type)
            ? "\u5efa\u7b51"
            : authoritative.type;
        return new MapTree.Node(type)
        {
            type = type,
            data = data,
            NodeDice = authoritative.NodeDice
        };
    }

    private static void RestorePresentationNodeIdentity(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node
                || !IsDimensionShopNode(node))
            {
                return;
            }

            NormalizeDimensionShopNode(node);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] presentation identity restore failed", ex);
        }
    }

    private static void RestorePresentationList(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not List<MapTree.Node> nodes)
            {
                return;
            }

            foreach (var node in nodes)
            {
                if (IsDimensionShopNode(node))
                {
                    NormalizeDimensionShopNode(node);
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] presentation list restore failed", ex);
        }
    }

    private static void NormalizeRenderedNodeIdentity(ModHookContext context)
    {
        try
        {
            if (context.Target is not UnityEngine.Component component)
            {
                return;
            }

            foreach (var item in component.GetComponentsInChildren<MapItem>())
            {
                if (item?.node != null && IsDimensionShopNode(item.node))
                {
                    NormalizeDimensionShopNode(item.node);
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[DimensionShop] rendered node identity normalization failed", ex);
        }
    }

    private static void NormalizeDimensionShopNode(MapTree.Node node)
    {
        node.data ??= new Dictionary<string, string>();
        node.data["Id"] = TerriasIds.DimensionShopMapId;
        node.data["Type"] = "Build";
        node.data["NodeId"] = TerriasIds.DimensionShopNodeId;
        node.data["Note"] = "\u5efa\u7b51";
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

    private static void OpenDimensionShop(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length < 2
                || !string.Equals(Convert.ToString(context.Arguments[0]), "build", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Convert.ToString(context.Arguments[1]), TerriasIds.DimensionShopNodeId, StringComparison.Ordinal))
            {
                return;
            }

            DimensionShopGameApi.CloseMapUi();
            DimensionShopPanel.Open("Commands.load");
        }
        catch (Exception ex)
        {
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
