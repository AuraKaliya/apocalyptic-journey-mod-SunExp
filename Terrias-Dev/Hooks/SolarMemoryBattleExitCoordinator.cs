using System;
using Data.Save;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryBattleExitCoordinator
{
    private const string HookOwner = "SolarMemoryBattleExit";
    private const string LogPrefix = "[SolarMemoryFightAbort]";
    private static bool handlingSolarMemoryFightAbort;

    public static void Initialize(ModConfig modConfig)
    {
        SunExpHookRegistry.Before(
            modConfig,
            SunExpHookTargets.FightEscapeResetStates,
            PrepareSolarMemoryFightAbort,
            HookOwner);
        SunExpHookRegistry.After(
            modConfig,
            SunExpHookTargets.FightEscapeResetStates,
            SettleSolarMemoryFightAbort,
            HookOwner);
        SunExpHookRegistry.After(
            modConfig,
            SunExpHookTargets.FightLossInit,
            SettleSolarMemoryFightLoss,
            HookOwner);
    }

    internal static void CloseTransientUi(string source)
    {
        try
        {
            SolarMemorySetupFlowRuntime.ClosePreparationWindows();
            SolarMemoryBlessingPickerRuntime.Close();
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryPackWindow", source, LogPrefix);
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExpSolarMemoryStarterDeck", source, LogPrefix);
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryOriginSetup", source, LogPrefix);
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryBlessingSetup", source, LogPrefix);
            SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExp_SolarMemoryBlessingPicker", source, LogPrefix);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " transient UI cleanup failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    private static void PrepareSolarMemoryFightAbort(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            handlingSolarMemoryFightAbort = true;
            SolarMemoryBossTransitionCoordinator.ClearPendingSaintWunaBossFlow();
            EnsureCurrentNodeForTransition("Fight_Escape.ResetStates:before");
            CloseTransientUi("Fight_Escape.ResetStates:before");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " prepare failed: " + ex.Message);
        }
    }

    private static void SettleSolarMemoryFightAbort(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                handlingSolarMemoryFightAbort = false;
                return;
            }

            EnsureCurrentNodeForTransition("Fight_Escape.ResetStates:after");
            CloseTransientUi("Fight_Escape.ResetStates:after");
            SunExpLog.Info(LogPrefix + " escape/loss branch settled.");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " settle failed: " + ex.Message);
        }
        finally
        {
            handlingSolarMemoryFightAbort = false;
        }
    }

    private static void SettleSolarMemoryFightLoss(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            CloseTransientUi("Fight_Loss.Init");
            SolarMemoryBossTransitionCoordinator.ClearPendingSaintWunaBossFlow();
            if (!handlingSolarMemoryFightAbort)
            {
                EnsureCurrentNodeForTransition("Fight_Loss.Init");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(LogPrefix + " loss settle failed: " + ex.Message);
        }
    }

    private static void EnsureCurrentNodeForTransition(string source)
    {
        try
        {
            var mapManager = MapManager.Instance;
            var tree = mapManager?.MapTree;
            if (tree == null)
            {
                return;
            }

            if (SolarMemoryMapLifecycleCoordinator.IsUsableSolarMemoryMapNode(tree.currentNode))
            {
                SolarMemoryMapLifecycleCoordinator.EnsureSolarMemoryNodeDice(tree.currentNode, tree, source);
                GameSaveManager.UpdateNode(tree.currentNode);
                return;
            }

            var saveNode = GameSaveManager.GetNode();
            if (SolarMemoryMapLifecycleCoordinator.IsUsableSolarMemoryMapNode(saveNode))
            {
                SolarMemoryMapLifecycleCoordinator.EnsureSolarMemoryNodeDice(saveNode, tree, source);
                tree.currentNode = saveNode;
                GameSaveManager.UpdateNode(saveNode);
                SunExpLog.Info("[SolarMemoryMapSync] restored current node from save before transition; source=" + source + ".");
                return;
            }

            if (SolarMemoryMapLifecycleCoordinator.TryRestoreSolarMemoryCurrentNodeFromMapManager(source, false))
            {
                return;
            }

            if (mapManager?.ModeMapManager is NormalMapManager manager
                && SolarMemoryMapLifecycleCoordinator.EnsureSolarMemoryMapState(manager, source, false)
                && SolarMemoryMapLifecycleCoordinator.IsUsableSolarMemoryMapNode(tree.currentNode))
            {
                SolarMemoryMapLifecycleCoordinator.EnsureSolarMemoryNodeDice(tree.currentNode, tree, source);
                GameSaveManager.UpdateNode(tree.currentNode);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryMapSync] transition current node repair failed from "
                + source
                + ": "
                + ex.Message);
        }
    }
}
