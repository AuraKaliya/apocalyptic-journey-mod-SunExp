using System;
using Data.Save;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Network;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class EndlessSeaModeRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        EndlessSeaModeEntryRuntime.Initialize(modConfig);
        EndlessSeaSaveCacheRuntime.Initialize(modConfig);
        RegisterBefore(modConfig, "NormalMapManager.MapItemInit", EnsureSeaMapBeforeMapItems);
        RegisterAfter(modConfig, "NormalMapManager.MapItemInit", ApplySeaSlotsAfterMapItems);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSeaMapBeforeSelect);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ReapplySeaFixedSlotLocks);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ScheduleAbyssMapPanels);
        RegisterAfter(modConfig, "MapSelectUI.DataUpdate", ApplySeaLayerTitle);
        RegisterBefore(modConfig, "NormalMapManager.ReadyToChangeMap", AdvanceSeaFloorBeforeMapChange);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RepairSeaMapAfterNativeGeneration);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", RepairSeaMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", RepairSeaMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairSeaMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", RepairSeaMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", RepairSeaMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairSeaMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcNextMap", EnsureSeaCurrentNodeBeforeNextMap);
        RegisterAfter(modConfig, "MapManager.RpcNextMap", SyncSeaClientLastNodeAfterNextMap);
        SunExpBattleLifecycleRouter.Register("EndlessSeaMode", new SunExpBattleLifecycleSubscription
        {
            FightStarted = context => EndlessAbyssMilestonePromptService.Reset("Fight_Start.Init")
        });
    }

    public static bool IsEndlessSeaRun()
    {
        EndlessSeaLegacyMigration.MigrateCurrentSave("EndlessSeaModeRuntime.IsEndlessSeaRun");
        return GameSaveManager.GetValue<string>(SunExpIds.EndlessSeaModeKey) == "1";
    }

    public static int CurrentFloor()
    {
        return Math.Max(1, GameSaveManager.GetValue<int>(SunExpIds.EndlessSeaFloorKey));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "EndlessSeaMode");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "EndlessSeaMode");
    }

    private static void EnsureSeaMapBeforeMapItems(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun() || context.Target is not NormalMapManager manager)
            {
                return;
            }

            EndlessSeaRunStateStore.RepairCurrentRun("NormalMapManager.MapItemInit:before");
            EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.MapPlanning, "NormalMapManager.MapItemInit:before");
            EndlessSeaOriginService.EnsureOriginCaps("NormalMapManager.MapItemInit:before");
            EndlessSeaMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.MapItemInit:before");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea pre-map-item build failed", ex);
        }
    }

    private static void ApplySeaSlotsAfterMapItems(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun()
                || context.Target is not NormalMapManager manager
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapSelectUI mapSelect)
            {
                return;
            }

            EndlessSeaMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.MapItemInit:after");
            EndlessSeaMapViewPresenter.ApplySlots(mapSelect, manager, CurrentFloor(), applyAllSlots: true, sync: true, "NormalMapManager.MapItemInit");
            EndlessSeaMapViewPresenter.SetLayerTitle(mapSelect, CurrentFloor());
            ScheduleAbyssMapPanels("NormalMapManager.MapItemInit:after");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea fixed slot apply failed", ex);
        }
    }

    private static void EnsureSeaMapBeforeSelect(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun())
            {
                return;
            }

            EndlessSeaOriginService.EnsureOriginCaps("MapSelectUI.ReadyToSelect");
            if (MapManager.Instance?.ModeMapManager is NormalMapManager manager)
            {
                EndlessSeaMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "MapSelectUI.ReadyToSelect");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea pre-select map repair failed", ex);
        }
    }

    private static void ReapplySeaFixedSlotLocks(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            var manager = MapManager.Instance?.ModeMapManager as NormalMapManager;
            EndlessSeaMapViewPresenter.ApplySlots(mapSelect, manager, CurrentFloor(), applyAllSlots: false, sync: false, "MapSelectUI.ShowMap");
            EndlessSeaMapViewPresenter.SetLayerTitle(mapSelect, CurrentFloor());
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea fixed slot lock repair failed", ex);
        }
    }

    private static void ApplySeaLayerTitle(ModHookContext context)
    {
        try
        {
            if (IsEndlessSeaRun() && context.Target is MapSelectUI mapSelect)
            {
                EndlessSeaMapViewPresenter.SetLayerTitle(mapSelect, CurrentFloor());
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea layer title failed", ex);
        }
    }

    private static void AdvanceSeaFloorBeforeMapChange(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun()
                || context.Target is not NormalMapManager manager
                || manager.Level < SunExpIds.EndlessSeaLayerNodeCount)
            {
                return;
            }

            if (IsClientOnlyPlayer())
            {
                return;
            }

            var nextFloor = CurrentFloor() + 1;
            EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.BetweenFloors, "NormalMapManager.ReadyToChangeMap");
            SetSaveValue(SunExpIds.EndlessSeaFloorKey, nextFloor.ToString());
            SetSaveValue(SunExpIds.EndlessSeaGeneratedFloorKey, "0");
            if (EndlessSeaRewardPlan.IsEndless(nextFloor))
            {
                EndlessAbyssGazeService.EnsureAtLeast(
                    EndlessAbyssConfigStore.Current.Gaze.EndlessMinLevel,
                    "NormalMapManager.ReadyToChangeMap:endless-entry");
            }
            if (MapManager.Instance != null)
            {
                MapManager.Instance.SetLevel(0);
            }
            else
            {
                manager.Level = 0;
                GameSaveManager.SetLevel(0);
            }

            EndlessSeaMapBuilder.EnsureFloorMapState(manager, nextFloor, "NormalMapManager.ReadyToChangeMap", forceRebuild: true);
            EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.MapPlanning, "NormalMapManager.ReadyToChangeMap");
            SunExpLog.Info("[EndlessSeaMode] advanced to floor " + nextFloor + ".");
            EndlessSeaNetworkSync.BroadcastSnapshot("NormalMapManager.ReadyToChangeMap");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea floor advance failed", ex);
        }
    }

    public static void TryOpenAbyssMapPanels(string source)
    {
        try
        {
            if (!IsEndlessSeaRun()
                || GameSaveManager.GetValue<string>(SunExpIds.EndlessSeaStarterDeckAppliedKey) != "1")
            {
                return;
            }

            if (IsClientOnlyPlayer())
            {
                EndlessSeaNetworkSync.RequestSnapshot(source + ":client-map-panels");
                return;
            }

            EndlessSeaRunStateStore.RepairCurrentRun(source);
            EndlessAbyssGazeService.EnsureInitialized(source);
            var floor = CurrentFloor();
            if (EndlessSeaRewardPlan.IsEndless(floor))
            {
                EndlessAbyssGazeService.EnsureAtLeast(
                    EndlessAbyssConfigStore.Current.Gaze.EndlessMinLevel,
                    source + ":endless-entry");
            }
            else
            {
                EndlessAbyssShockService.TryEnqueueStealthFloorShock(floor, source);
            }

            EndlessSeaNetworkSync.BroadcastSnapshot(source + ":abyss-panels");

            if (EndlessAbyssShockPanel.TryOpenPending(
                    () => EndlessAbyssMilestonePromptService.TryOpen(source + ":after-shock"),
                    source))
            {
                return;
            }

            EndlessAbyssMilestonePromptService.TryOpen(source);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless abyss map panels failed", ex);
        }
    }

    private static void ScheduleAbyssMapPanels(ModHookContext context)
    {
        ScheduleAbyssMapPanels(context.Target?.GetType().Name ?? "MapSelectUI");
    }

    private static void ScheduleAbyssMapPanels(string source)
    {
        if (!IsEndlessSeaRun())
        {
            return;
        }

        SunExpFrameDispatcher.RunOnceNextFrame(
            "EndlessAbyss.MapPanels",
            () => TryOpenAbyssMapPanels(source));
    }

    private static void RepairSeaMapAfterNativeGeneration(ModHookContext context)
    {
        try
        {
            if (IsEndlessSeaRun() && context.Target is NormalMapManager manager)
            {
                EndlessSeaMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.GeneratrMap:after");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea native generation repair failed", ex);
        }
    }

    private static void RepairSeaMapSelection(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun())
            {
                return;
            }

            var manager = MapManager.Instance?.ModeMapManager as NormalMapManager;
            if (manager != null)
            {
                EndlessSeaMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "MapManager.MapSelectionSync");
            }

            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is string[] maps && args[i + 1] is string[] mapData)
                {
                    if (EndlessSeaMapBuilder.RepairFixedMapArrays(MapManager.Instance?.MapTree, CurrentFloor(), maps, mapData))
                    {
                        SunExpLog.Info("[EndlessSeaMapSync] fixed slot arrays repaired.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea map selection repair failed", ex);
        }
    }

    private static void EnsureSeaCurrentNodeBeforeNextMap(ModHookContext context)
    {
        try
        {
            if (IsEndlessSeaRun() && IsClientOnlyPlayer() && MapManager.Instance?.MapTree?.currentNode == null)
            {
                MapNodeSafetyService.RestoreCurrentNodeIfMissingOrExclusive(
                    MapManager.Instance?.Level ?? 0,
                    "EndlessSea.MapManager.RpcNextMap",
                    clientOnly: true);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessSeaMapSync] pre-next-map current node repair failed: " + ex.Message);
        }
    }

    private static void SyncSeaClientLastNodeAfterNextMap(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            var node = MapManager.Instance?.MapTree?.currentNode;
            if (node != null)
            {
                GameSaveManager.UpdateNode(node);
                SunExpLog.Debug("[EndlessSeaMapSync] synced client save node after RpcNextMap.");
            }

            EndlessSeaNetworkSync.RequestSnapshot("EndlessSea.MapManager.RpcNextMap:after");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessSeaMapSync] post-next-map save node sync failed: " + ex.Message);
        }
    }

    private static void SetSaveValue(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value);
        }
        catch
        {
            GameSaveManager.GetNowSave()?.SetValue(key, value);
        }
    }

    private static bool IsClientOnlyPlayer()
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
}
