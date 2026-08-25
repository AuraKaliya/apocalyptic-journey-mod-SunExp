using System;
using Terrias.Dll.Application;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class EndlessSeaStateProjectionRuntime
{
    private static bool initialized;
    private static EndlessSeaStateSnapshot? pending;
    private static EndlessSeaFloorPlan? cachedPlan;
    private static string cachedPlanHash = "";

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        EndlessSeaApplicationService.StateCommitted += OnStateCommitted;
        EndlessSeaApplicationService.ShockResolutionCommitted += source =>
            EndlessAbyssMilestonePromptService.Schedule(source);
    }

    public static bool TryGetCachedPlan(int floor, out EndlessSeaFloorPlan plan)
    {
        plan = cachedPlan!;
        return cachedPlan != null && cachedPlan.Floor == Math.Max(1, floor) && cachedPlan.IsValid;
    }

    public static void ApplyPending(MapSelectUI? mapSelect, NormalMapManager? manager, string source)
    {
        var snapshot = pending;
        if (snapshot == null || mapSelect == null || manager == null || snapshot.Floor != EndlessSeaSaveApi.CurrentFloor())
        {
            return;
        }
        EndlessSeaMapViewPresenter.SetLayerTitle(mapSelect, snapshot.Floor);
        EndlessSeaMapViewPresenter.ApplySlots(mapSelect, manager, snapshot.Floor, applyAllSlots: false, sync: false, source);
        pending = null;
    }

    private static void OnStateCommitted(EndlessSeaStateCommitted committed)
    {
        var snapshot = committed.Snapshot;
        if (committed.FloorPlan != null)
        {
            cachedPlan = committed.FloorPlan;
            cachedPlanHash = snapshot.FloorPlanHash;
        }
        else if (!string.Equals(cachedPlanHash, snapshot.FloorPlanHash, StringComparison.Ordinal))
        {
            cachedPlan = null;
            cachedPlanHash = "";
        }
        pending = snapshot;
        if (string.Equals(snapshot.RunPhase, EndlessSeaRunPhase.Evacuating, StringComparison.Ordinal))
        {
            EndlessAbyssEvacuationRuntime.ReceiveAuthoritative(
                new EndlessAbyssEvacuationResolution
                {
                    RunId = snapshot.RunId,
                    Token = snapshot.EvacuationToken,
                    Reason = snapshot.EvacuationReason,
                    Floor = snapshot.EvacuationFloor,
                    SettlementDepth = snapshot.EvacuationDepth,
                    EvacuatedAt = snapshot.EvacuationAt
                },
                committed.Source + ":snapshot");
        }
    }
}
