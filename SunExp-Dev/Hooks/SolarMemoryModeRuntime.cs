using System;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryModeRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        SolarMemoryModeEntryRuntime.Initialize(modConfig);
        SolarMemoryMapVisualRuntime.Initialize(modConfig);
        SolarMemoryDeckIsolationRuntime.Initialize(modConfig);
        SolarMemoryMapLifecycleCoordinator.Initialize(modConfig);
        SolarMemorySettlementCoordinator.Initialize(modConfig);
        SolarMemoryBossTransitionCoordinator.Initialize(modConfig);
        SolarMemoryBattleExitCoordinator.Initialize(modConfig);
    }

    public static void OpenOriginWindow()
    {
        StartOrResumePreparation("origin");
    }

    public static void OpenBlessingWindow()
    {
        StartOrResumePreparation("blessing");
    }

    public static bool IsSolarMemoryRun()
    {
        return GameSaveManager.GetValue<string>(SunExpIds.SolarMemoryModeKey) == "1";
    }

    private static void StartOrResumePreparation(string source)
    {
        try
        {
            SolarMemoryPreparationRuntime.StartOrResume();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory " + source + " window failed", ex);
        }
    }
}
