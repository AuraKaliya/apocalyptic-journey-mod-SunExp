using System;
using System.Collections.Generic;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class DimensionShopRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "NormalMapManager.RandomGenerate", InjectFirstLayerCandidate);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", RepairBeforeSelection);
        RegisterAfter(modConfig, "Commands.load", OpenDimensionShop);
        SunExpBattleLifecycleRouter.Register("DimensionShop", new SunExpBattleLifecycleSubscription
        {
            AdventureStarting = _ => DimensionShopService.EnsureRunSnapshot("AdventureStarting")
        });
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "DimensionShop");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "DimensionShop");
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
            SunExpLog.Error("[DimensionShop] first-layer injection failed", ex);
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
            SunExpLog.Error("[DimensionShop] pre-selection repair failed", ex);
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
            SunExpLog.Warn("[DimensionShop] no replaceable first-layer candidate from " + source + ".");
            return;
        }

        var row = SunExpConfigIndex.Row(DataType.Map, SunExpIds.DimensionShopMapId)
                  ?? SunExpConfigIndex.Row(DataType.Map, SunExpIds.DimensionShopMapShortId);
        if (row == null)
        {
            SunExpLog.Warn("[DimensionShop] map row is unavailable from " + source + ".");
            return;
        }

        var data = new Dictionary<string, string>(row)
        {
            ["Type"] = "Build",
            ["NodeId"] = SunExpIds.DimensionShopNodeId,
            ["Note"] = "\u5efa\u7b51"
        };
        var previous = nodes[replacement];
        nodes[replacement] = new MapTree.Node("\u5efa\u7b51")
        {
            data = data,
            NodeDice = previous.NodeDice
        };
        SunExpLog.Info("[DimensionShop] injected first-layer candidate at index="
                       + replacement
                       + " from "
                       + source
                       + ".");
    }

    private static void OpenDimensionShop(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length < 2
                || !string.Equals(Convert.ToString(context.Arguments[0]), "build", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Convert.ToString(context.Arguments[1]), SunExpIds.DimensionShopNodeId, StringComparison.Ordinal))
            {
                return;
            }

            DimensionShopGameApi.CloseMapUi();
            DimensionShopPanel.Open("Commands.load");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DimensionShop] open route failed", ex);
            DimensionShopGameApi.AdvanceMap();
        }
    }

    private static bool IsDimensionShopNode(MapTree.Node? node)
    {
        var id = DictionaryUtil.Get(node?.data, "Id");
        var nodeId = DictionaryUtil.Get(node?.data, "NodeId");
        return string.Equals(id, SunExpIds.DimensionShopMapId, StringComparison.Ordinal)
               || string.Equals(id, SunExpIds.DimensionShopMapShortId, StringComparison.Ordinal)
               || string.Equals(nodeId, SunExpIds.DimensionShopNodeId, StringComparison.Ordinal);
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
