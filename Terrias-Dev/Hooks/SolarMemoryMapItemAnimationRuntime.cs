using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryMapItemAnimationRuntime
{
    private static readonly Dictionary<MapTree.Node, SolarMemoryMapPreviewOverride> PendingRestores = new();
    private static int restoreGeneration;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "MapItem.Init", PrepareMapItemAnimation);
        RegisterAfter(modConfig, "MapItem.Init", RestoreMapItemAnimation);
        RegisterBefore(modConfig, "MapSelectUI.Start", _ => RestoreAll("MapSelectUI.Start"));
        RegisterBefore(modConfig, "GameEntryUI.Init", _ => Reset("GameEntryUI.Init"));
        RegisterBefore(modConfig, "GameApp.ReturnToMenu", _ => Reset("GameApp.ReturnToMenu"));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "SolarMemoryMapItemAnimation");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "SolarMemoryMapItemAnimation");
    }

    private static void PrepareMapItemAnimation(ModHookContext context)
    {
        try
        {
            RestoreAll("before-next-map-item");
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node
                || !SolarMemoryMapPreviewApi.TryApplyAnimationOverride(node, out var applied, out var reason)
                || applied == null)
            {
                return;
            }

            PendingRestores[node] = applied;
            var generation = ++restoreGeneration;
            if (TerriasFrameDispatcher.HasNextFrameDispatcher)
            {
                TerriasFrameDispatcher.RunOnceNextFrame(
                    "SolarMemoryMapItemAnimation.Restore." + generation,
                    () => Restore(node, applied, "next-frame"));
            }
            TerriasLog.Info("[SolarMemoryMapItem] applied validated native map animation fallback for "
                            + applied.EnemyId
                            + "; original="
                            + applied.OriginalAnimation
                            + "; fallback="
                            + applied.FallbackAnimation
                            + "; reason="
                            + reason
                            + ".");
        }
        catch (Exception ex)
        {
            RestoreAll("prepare-failure");
            TerriasLog.Error("Solar memory map item animation prepare failed", ex);
        }
    }

    private static void RestoreMapItemAnimation(ModHookContext context)
    {
        try
        {
            if (context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapTree.Node node
                || !PendingRestores.TryGetValue(node, out var restore))
            {
                return;
            }

            Restore(node, restore, "MapItem.Init.after");
        }
        catch (Exception ex)
        {
            RestoreAll("restore-failure");
            TerriasLog.Error("Solar memory map item animation restore failed", ex);
        }
    }

    private static void Restore(MapTree.Node node, SolarMemoryMapPreviewOverride restore, string source)
    {
        if (!PendingRestores.TryGetValue(node, out var current)
            || !ReferenceEquals(current, restore))
        {
            return;
        }

        PendingRestores.Remove(node);
        restore.Restore();
        TerriasLog.Debug("[SolarMemoryMapItem] restored native map animation fallback from " + source + ".");
    }

    private static void RestoreAll(string source)
    {
        if (PendingRestores.Count == 0)
        {
            return;
        }

        foreach (var restore in new List<SolarMemoryMapPreviewOverride>(PendingRestores.Values))
        {
            restore.Restore();
        }

        PendingRestores.Clear();
        TerriasLog.Debug("[SolarMemoryMapItem] restored all pending map animation overrides from " + source + ".");
    }

    private static void Reset(string source)
    {
        RestoreAll(source);
        SolarMemoryMapPreviewApi.ClearProbeCache();
    }
}
