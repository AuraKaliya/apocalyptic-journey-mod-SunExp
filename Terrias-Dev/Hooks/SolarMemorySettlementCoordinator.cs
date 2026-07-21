using System;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace Terrias.Dll.Hooks;

public static class SolarMemorySettlementCoordinator
{
    private const int LegacySolarFinaleMapLevel = 30;
    private const string HookOwner = "SolarMemorySettlement";

    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.Before(
            modConfig,
            "NormalMapManager.MapItemInit",
            SettleLegacyTerminalLevelBeforeMapItems,
            HookOwner);
        TerriasHookRegistry.Before(
            modConfig,
            "NormalMapManager.ReadyToChangeMap",
            FinishSolarMemoryAfterFinalLayer,
            HookOwner);
    }

    public static void ShowSolarMemorySettlement()
    {
        SolarMemorySettlementPresenter.Show();
    }

    internal static void CompleteSolarMemoryRunForSettlement(string source)
    {
        if (MapManager.Instance?.ModeMapManager is NormalMapManager manager)
        {
            RouteToNativeSettlement(manager, source, 32);
        }

        UIManager.Instance?.CloseUI("FightUI");
        ShowSolarMemorySettlement();
    }

    private static void FinishSolarMemoryAfterFinalLayer(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager)
            {
                return;
            }

            if (SolarMemoryBossTransitionCoordinator.IsSettlementPending)
            {
                TerriasLog.Info("[SolarMemoryStory] deferred final-layer settlement while story dialogue is pending.");
                return;
            }

            if (manager.Level < TerriasIds.SolarMemoryMaxLayer * 6)
            {
                return;
            }

            RouteToNativeSettlement(manager, "NormalMapManager.ReadyToChangeMap", 32);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory settlement failed", ex);
        }
    }

    private static void SettleLegacyTerminalLevelBeforeMapItems(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
                || context.Target is not NormalMapManager manager
                || manager.Level < LegacySolarFinaleMapLevel)
            {
                return;
            }

            if (SolarMemoryBossTransitionCoordinator.IsSettlementPending)
            {
                TerriasLog.Info("[SolarMemoryStory] deferred legacy terminal settlement while story dialogue is pending.");
                return;
            }

            RouteToNativeSettlement(
                manager,
                "NormalMapManager.MapItemInit",
                TerriasIds.SolarMemoryMaxLayer * 6);
            ShowSolarMemorySettlement();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory legacy terminal-level settlement failed", ex);
        }
    }

    private static void RouteToNativeSettlement(NormalMapManager manager, string source, int levelForNativeFlow)
    {
        manager.Level = levelForNativeFlow;
        TerriasLog.Info("[SolarMemory] third layer complete from "
            + source
            + "; routing directly to settlement at native level "
            + levelForNativeFlow
            + ".");
    }
}
