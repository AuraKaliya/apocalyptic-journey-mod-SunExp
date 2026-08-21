using System;
using Data.Save;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

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
        TerriasBattleLifecycleRouter.Register("EndlessSeaMode", new TerriasBattleLifecycleSubscription
        {
            BattleOpening = context => EndlessAbyssMilestonePromptService.Reset("BattleOpening")
        });
    }

    public static bool IsEndlessSeaRun()
    {
        EndlessSeaLegacyMigration.MigrateCurrentSave("EndlessSeaModeRuntime.IsEndlessSeaRun");
        return GameSaveManager.GetValue<string>(TerriasIds.EndlessSeaModeKey) == "1";
    }

    public static int CurrentFloor()
    {
        return Math.Max(1, GameSaveManager.GetValue<int>(TerriasIds.EndlessSeaFloorKey));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "EndlessSeaMode");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "EndlessSeaMode");
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
            if (!EndlessSeaRunStateStore.IsEvacuating())
            {
                EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.MapPlanning, "NormalMapManager.MapItemInit:before");
            }
            EndlessSeaOriginService.EnsureOriginCaps("NormalMapManager.MapItemInit:before");
            EndlessSeaMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.MapItemInit:before");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea pre-map-item build failed", ex);
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
            TerriasLog.Error("Endless Sea fixed slot apply failed", ex);
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
            TerriasLog.Error("Endless Sea pre-select map repair failed", ex);
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
            EndlessSeaNetworkSync.ApplyPendingProjection(mapSelect, manager, "MapSelectUI.ShowMap");
            EndlessSeaMapViewPresenter.ApplySlots(mapSelect, manager, CurrentFloor(), applyAllSlots: false, sync: false, "MapSelectUI.ShowMap");
            EndlessSeaMapViewPresenter.SetLayerTitle(mapSelect, CurrentFloor());
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea fixed slot lock repair failed", ex);
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
            TerriasLog.Error("Endless Sea layer title failed", ex);
        }
    }

    private static void AdvanceSeaFloorBeforeMapChange(ModHookContext context)
    {
        try
        {
            if (!IsEndlessSeaRun()
                || context.Target is not NormalMapManager manager
                || manager.Level < TerriasIds.EndlessSeaLayerNodeCount)
            {
                return;
            }

            if (IsClientOnlyPlayer())
            {
                return;
            }

            var nextFloor = CurrentFloor() + 1;
            EndlessSeaRunStateStore.MarkPhase(EndlessSeaRunPhase.BetweenFloors, "NormalMapManager.ReadyToChangeMap");
            SetSaveValue(TerriasIds.EndlessSeaFloorKey, nextFloor.ToString());
            SetSaveValue(TerriasIds.EndlessSeaGeneratedFloorKey, "0");
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
            TerriasLog.Info("[EndlessSeaMode] advanced to floor " + nextFloor + ".");
            EndlessSeaNetworkSync.BroadcastSnapshot("NormalMapManager.ReadyToChangeMap");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea floor advance failed", ex);
        }
    }

    public static void TryOpenAbyssMapPanels(string source)
    {
        try
        {
            if (!IsEndlessSeaRun()
                || GameSaveManager.GetValue<string>(TerriasIds.EndlessSeaStarterDeckAppliedKey) != "1")
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
            TerriasLog.Error("Endless abyss map panels failed", ex);
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

        TerriasFrameDispatcher.RunOnceNextFrame(
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
            TerriasLog.Error("Endless Sea native generation repair failed", ex);
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
                        TerriasLog.Info("[EndlessSeaMapSync] fixed slot arrays repaired.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless Sea map selection repair failed", ex);
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
            TerriasLog.Warn("[EndlessSeaMapSync] pre-next-map current node repair failed: " + ex.Message);
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
                TerriasLog.Debug("[EndlessSeaMapSync] synced client save node after RpcNextMap.");
            }

            EndlessSeaNetworkSync.RequestSnapshot("EndlessSea.MapManager.RpcNextMap:after");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaMapSync] post-next-map save node sync failed: " + ex.Message);
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
