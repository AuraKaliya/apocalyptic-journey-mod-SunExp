using System;
using Data.Save;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryBattleExitCoordinator
{
    private const string HookOwner = "SolarMemoryBattleExit";
    private const string LogPrefix = "[SolarMemoryFightAbort]";
    private static bool handlingSolarMemoryFightAbort;

    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.Before(
            modConfig,
            TerriasHookTargets.FightEscapeResetStates,
            PrepareSolarMemoryFightAbort,
            HookOwner);
        TerriasHookRegistry.After(
            modConfig,
            TerriasHookTargets.FightEscapeResetStates,
            SettleSolarMemoryFightAbort,
            HookOwner);
        TerriasHookRegistry.After(
            modConfig,
            TerriasHookTargets.FightLossInit,
            SettleSolarMemoryFightLoss,
            HookOwner);
    }

    internal static void CloseTransientUi(string source)
    {
        try
        {
            SolarMemorySetupFlowRuntime.ClosePreparationWindows();
            SolarMemoryBlessingPickerRuntime.Close();
            TerriasUiSafety.DisableRaycastsAndDestroyByName("Terrias_SolarMemoryPackWindow", source, LogPrefix);
            TerriasUiSafety.DisableRaycastsAndDestroyByName("TerriasSolarMemoryStarterDeck", source, LogPrefix);
            TerriasUiSafety.DisableRaycastsAndDestroyByName("Terrias_SolarMemoryOriginSetup", source, LogPrefix);
            TerriasUiSafety.DisableRaycastsAndDestroyByName("Terrias_SolarMemoryBlessingSetup", source, LogPrefix);
            TerriasUiSafety.DisableRaycastsAndDestroyByName("Terrias_SolarMemoryBlessingPicker", source, LogPrefix);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " transient UI cleanup failed from "
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
            TerriasLog.Warn(LogPrefix + " prepare failed: " + ex.Message);
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
            TerriasLog.Info(LogPrefix + " escape/loss branch settled.");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " settle failed: " + ex.Message);
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
            TerriasLog.Warn(LogPrefix + " loss settle failed: " + ex.Message);
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
                TerriasLog.Info("[SolarMemoryMapSync] restored current node from save before transition; source=" + source + ".");
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
            TerriasLog.Warn("[SolarMemoryMapSync] transition current node repair failed from "
                + source
                + ": "
                + ex.Message);
        }
    }
}
