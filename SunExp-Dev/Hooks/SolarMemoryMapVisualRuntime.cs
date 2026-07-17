using System;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryMapVisualRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "MapSelectUI.DataUpdate", SolarMemoryMapProjectionRuntime.ApplySolarMemoryLayerTitle);
        RegisterAfter(modConfig, "NormalMapManager.MapItemInit", SolarMemoryMapProjectionRuntime.ApplySolarMemoryFixedSlotsAfterMapItems);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", SolarMemoryMapLifecycleCoordinator.ReapplySolarMemoryFixedSlotLocks);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "SolarMemoryMapVisual");
    }
}
