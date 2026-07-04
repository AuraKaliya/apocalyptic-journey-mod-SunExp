using System;
using AuraShared.Core;
using Data.Save;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerModeRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TongtianTowerModeEntryRuntime.Initialize(modConfig);
        TongtianTowerSaveCacheRuntime.Initialize(modConfig);
        RegisterBefore(modConfig, "NormalMapManager.MapItemInit", EnsureTowerMapBeforeMapItems);
        RegisterAfter(modConfig, "NormalMapManager.MapItemInit", ApplyTowerSlotsAfterMapItems);
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureTowerMapBeforeSelect);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ReapplyTowerFixedSlotLocks);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ScheduleAbyssMapPanels);
        RegisterAfter(modConfig, "MapSelectUI.DataUpdate", ApplyTowerLayerTitle);
        RegisterAfter(modConfig, "MapSelectUI.DataUpdate", ScheduleAbyssMapPanels);
        RegisterBefore(modConfig, "NormalMapManager.ReadyToChangeMap", AdvanceTowerFloorBeforeMapChange);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", RepairTowerMapAfterNativeGeneration);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", RepairTowerMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcNextMap", EnsureTowerCurrentNodeBeforeNextMap);
        RegisterAfter(modConfig, "MapManager.RpcNextMap", SyncTowerClientLastNodeAfterNextMap);
    }

    public static bool IsTongtianTowerRun()
    {
        return GameSaveManager.GetValue<string>(SunExpIds.TongtianTowerModeKey) == "1";
    }

    public static int CurrentFloor()
    {
        return Math.Max(1, GameSaveManager.GetValue<int>(SunExpIds.TongtianTowerFloorKey));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian tower " + message));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian tower " + message));
    }

    private static void EnsureTowerMapBeforeMapItems(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun() || context.Target is not NormalMapManager manager)
            {
                return;
            }

            TongtianTowerRunStateStore.RepairCurrentRun("NormalMapManager.MapItemInit:before");
            TongtianTowerRunStateStore.MarkPhase(TongtianTowerRunPhase.MapPlanning, "NormalMapManager.MapItemInit:before");
            TongtianTowerOriginService.EnsureOriginCaps("NormalMapManager.MapItemInit:before");
            TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.MapItemInit:before");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower pre-map-item build failed", ex);
        }
    }

    private static void ApplyTowerSlotsAfterMapItems(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun()
                || context.Target is not NormalMapManager manager
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not MapSelectUI mapSelect)
            {
                return;
            }

            TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.MapItemInit:after");
            TongtianTowerMapViewPresenter.ApplySlots(mapSelect, manager, CurrentFloor(), applyAllSlots: true, sync: true, "NormalMapManager.MapItemInit");
            TongtianTowerMapViewPresenter.SetLayerTitle(mapSelect, CurrentFloor());
            ScheduleAbyssMapPanels("NormalMapManager.MapItemInit:after");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower fixed slot apply failed", ex);
        }
    }

    private static void EnsureTowerMapBeforeSelect(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun())
            {
                return;
            }

            TongtianTowerOriginService.EnsureOriginCaps("MapSelectUI.ReadyToSelect");
            if (MapManager.Instance?.ModeMapManager is NormalMapManager manager)
            {
                TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "MapSelectUI.ReadyToSelect");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower pre-select map repair failed", ex);
        }
    }

    private static void ReapplyTowerFixedSlotLocks(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun() || context.Target is not MapSelectUI mapSelect)
            {
                return;
            }

            var manager = MapManager.Instance?.ModeMapManager as NormalMapManager;
            TongtianTowerMapViewPresenter.ApplySlots(mapSelect, manager, CurrentFloor(), applyAllSlots: false, sync: false, "MapSelectUI.ShowMap");
            TongtianTowerMapViewPresenter.SetLayerTitle(mapSelect, CurrentFloor());
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower fixed slot lock repair failed", ex);
        }
    }

    private static void ApplyTowerLayerTitle(ModHookContext context)
    {
        try
        {
            if (IsTongtianTowerRun() && context.Target is MapSelectUI mapSelect)
            {
                TongtianTowerMapViewPresenter.SetLayerTitle(mapSelect, CurrentFloor());
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower layer title failed", ex);
        }
    }

    private static void AdvanceTowerFloorBeforeMapChange(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun()
                || context.Target is not NormalMapManager manager
                || manager.Level < SunExpIds.TongtianTowerLayerNodeCount)
            {
                return;
            }

            if (IsClientOnlyPlayer())
            {
                return;
            }

            var nextFloor = CurrentFloor() + 1;
            TongtianTowerRunStateStore.MarkPhase(TongtianTowerRunPhase.BetweenFloors, "NormalMapManager.ReadyToChangeMap");
            SetSaveValue(SunExpIds.TongtianTowerFloorKey, nextFloor.ToString());
            SetSaveValue(SunExpIds.TongtianTowerGeneratedFloorKey, "0");
            if (TongtianTowerRewardPlan.IsEndless(nextFloor))
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

            TongtianTowerMapBuilder.EnsureFloorMapState(manager, nextFloor, "NormalMapManager.ReadyToChangeMap", forceRebuild: true);
            TongtianTowerRunStateStore.MarkPhase(TongtianTowerRunPhase.MapPlanning, "NormalMapManager.ReadyToChangeMap");
            SunExpLog.Info("[TongtianTowerMode] advanced to floor " + nextFloor + ".");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower floor advance failed", ex);
        }
    }

    public static void TryOpenAbyssMapPanels(string source)
    {
        try
        {
            if (!IsTongtianTowerRun()
                || IsClientOnlyPlayer()
                || GameSaveManager.GetValue<string>(SunExpIds.TongtianTowerStarterDeckAppliedKey) != "1")
            {
                return;
            }

            TongtianTowerRunStateStore.RepairCurrentRun(source);
            EndlessAbyssGazeService.EnsureInitialized(source);
            var floor = CurrentFloor();
            if (TongtianTowerRewardPlan.IsEndless(floor))
            {
                EndlessAbyssGazeService.EnsureAtLeast(
                    EndlessAbyssConfigStore.Current.Gaze.EndlessMinLevel,
                    source + ":endless-entry");
            }
            else
            {
                EndlessAbyssShockService.TryEnqueueStealthFloorShock(floor, source);
            }

            if (EndlessAbyssShockPanel.TryOpenPending(
                    () => EndlessAbyssMilestoneRewardPanel.TryOpenForCurrentFloor(source + ":after-shock"),
                    source))
            {
                return;
            }

            EndlessAbyssMilestoneRewardPanel.TryOpenForCurrentFloor(source);
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
        if (!IsTongtianTowerRun())
        {
            return;
        }

        SunExpFrameDispatcher.RunOnceNextFrame(
            "EndlessAbyss.MapPanels",
            () => TryOpenAbyssMapPanels(source));
    }

    private static void RepairTowerMapAfterNativeGeneration(ModHookContext context)
    {
        try
        {
            if (IsTongtianTowerRun() && context.Target is NormalMapManager manager)
            {
                TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "NormalMapManager.GeneratrMap:after");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower native generation repair failed", ex);
        }
    }

    private static void RepairTowerMapSelection(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun())
            {
                return;
            }

            var manager = MapManager.Instance?.ModeMapManager as NormalMapManager;
            if (manager != null)
            {
                TongtianTowerMapBuilder.EnsureFloorMapState(manager, CurrentFloor(), "MapManager.MapSelectionSync");
            }

            var args = context.Arguments ?? Array.Empty<object>();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is string[] maps && args[i + 1] is string[] mapData)
                {
                    if (TongtianTowerMapBuilder.RepairFixedMapArrays(MapManager.Instance?.MapTree, CurrentFloor(), maps, mapData))
                    {
                        SunExpLog.Info("[TongtianTowerMapSync] fixed slot arrays repaired.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower map selection repair failed", ex);
        }
    }

    private static void EnsureTowerCurrentNodeBeforeNextMap(ModHookContext context)
    {
        try
        {
            if (IsTongtianTowerRun() && IsClientOnlyPlayer() && MapManager.Instance?.MapTree?.currentNode == null)
            {
                MapNodeSafetyService.RestoreCurrentNodeIfMissingOrExclusive(
                    MapManager.Instance?.Level ?? 0,
                    "TongtianTower.MapManager.RpcNextMap",
                    clientOnly: true);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerMapSync] pre-next-map current node repair failed: " + ex.Message);
        }
    }

    private static void SyncTowerClientLastNodeAfterNextMap(ModHookContext context)
    {
        try
        {
            if (!IsTongtianTowerRun() || !IsClientOnlyPlayer())
            {
                return;
            }

            var node = MapManager.Instance?.MapTree?.currentNode;
            if (node != null)
            {
                GameSaveManager.UpdateNode(node);
                SunExpLog.Debug("[TongtianTowerMapSync] synced client save node after RpcNextMap.");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerMapSync] post-next-map save node sync failed: " + ex.Message);
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
