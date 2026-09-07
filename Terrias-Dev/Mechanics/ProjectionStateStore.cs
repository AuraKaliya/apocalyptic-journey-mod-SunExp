using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class ProjectionStateStore
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, ProjectionState> Projections = new(StringComparer.Ordinal);
    private static int nextProjectionIndex;

    public static event Action<ProjectionState>? Registered;

    public static event Action<ProjectionState>? Retired;

    public static event Action<ProjectionState, CompanionIntentPlan>? IntentPresented;

    public static event Action<ProjectionState>? ActionPresented;

    public static string NextStatusId()
    {
        lock (SyncRoot)
        {
            var manager = FightManager.Instance;
            for (var i = 0; i < 64; i++)
            {
                var id = TerriasIds.ProjectionStatusIdPrefix + nextProjectionIndex++;
                if (manager?.statuses == null || !manager.statuses.ContainsKey(id))
                {
                    return id;
                }
            }
        }

        return TerriasIds.ProjectionStatusIdPrefix + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    public static void Register(ProjectionState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.StatusId))
        {
            return;
        }

        lock (SyncRoot)
        {
            Projections[state.StatusId] = state;
        }

        TerriasPerformanceCounters.Record("Projection.Registered");
        Registered?.Invoke(state);
    }

    public static IReadOnlyList<ProjectionState> Active()
    {
        lock (SyncRoot)
        {
            return Projections.Values.Where(IsAlive).ToArray();
        }
    }

    public static int ActiveCount()
    {
        return Active().Count;
    }

    public static ProjectionState? Find(string statusId)
    {
        lock (SyncRoot)
        {
            return !string.IsNullOrWhiteSpace(statusId) && Projections.TryGetValue(statusId, out var state)
                ? state
                : null;
        }
    }

    public static ProjectionState? FindByOwner(string ownerPlayerId, string ownerStatusId = "")
    {
        lock (SyncRoot)
        {
            return Projections.Values.FirstOrDefault(state =>
                (!string.IsNullOrWhiteSpace(ownerPlayerId)
                    && string.Equals(state.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(ownerStatusId)
                    && string.Equals(state.OwnerStatusId, ownerStatusId, StringComparison.Ordinal)));
        }
    }

    public static bool HasForOwner(string ownerPlayerId, string ownerStatusId = "")
    {
        return FindByOwner(ownerPlayerId, ownerStatusId) != null;
    }

    public static void NotifyIntentPresented(string statusId, CompanionIntentPlan? plan)
    {
        var state = Find(statusId);
        if (state != null && plan != null)
        {
            IntentPresented?.Invoke(state, plan);
        }
    }

    public static void NotifyActionPresented(string statusId)
    {
        var state = Find(statusId);
        if (state != null)
        {
            ActionPresented?.Invoke(state);
        }
    }

    public static bool IsProjection(IStatusManager? status)
    {
        return status != null && Find(status.InstanceId) != null;
    }

    public static bool RetireIfDead(IStatusManager? status, string source)
    {
        if (!ShouldRetire(status))
        {
            return false;
        }

        Retire(status, source);
        return true;
    }

    public static void Retire(IStatusManager? status, string source)
    {
        if (status == null)
        {
            return;
        }

        ProjectionState? state = null;
        lock (SyncRoot)
        {
            Projections.TryGetValue(status.InstanceId, out state);
        }

        if (state == null)
        {
            return;
        }

        ProjectionLifecycle.Current.Retire(state, source);
        lock (SyncRoot)
        {
            Projections.Remove(status.InstanceId);
        }

        try
        {
            var statusId = status.InstanceId;
            CompanionBattleStateStore.Remove(statusId);
            status.state = IStatusManager.State.Dead;
            RemoveFromFightState(statusId, removeStatusRecords: false);
            ScheduleStatusRecordCleanup(statusId, source);
            state.Projection.DeadEffect();
            TerriasLog.Info("[Projection] retired from " + source + ": status=" + statusId + ", role=" + state.RoleId);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[Projection] retire failed from " + source + ": " + ex.Message);
            try
            {
                UnityEngine.Object.Destroy(state.Projection.gameObject);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        Retired?.Invoke(state);
    }

    public static void ClearAll(string source)
    {
        ProjectionState[] states;
        lock (SyncRoot)
        {
            if (Projections.Count == 0)
            {
                return;
            }

            states = Projections.Values.ToArray();
            Projections.Clear();
        }

        foreach (var state in states)
        {
            try
            {
                CompanionBattleStateStore.Remove(state.StatusId);
                RemoveFromFightState(state.StatusId);
                if (state.Projection != null)
                {
                    UnityEngine.Object.Destroy(state.Projection.gameObject);
                }
                Retired?.Invoke(state);
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[Projection] cleanup failed from " + source + ": " + ex.Message);
            }
        }

        TerriasPerformanceCounters.Record("Projection.Cleared");
    }

    private static bool IsAlive(ProjectionState state)
    {
        if (state?.Projection == null)
        {
            return false;
        }

        var status = state.Projection.Status;
        return status != null
            && status.CurHp > 0
            && status.state != IStatusManager.State.Dead
            && state.Projection.gameObject != null;
    }

    private static bool ShouldRetire(IStatusManager? status)
    {
        return IsProjection(status)
            && (status!.CurHp <= 0
                || status.MaxHp <= 0
                || status.state == IStatusManager.State.Dead);
    }

    private static void ScheduleStatusRecordCleanup(string statusId, string source)
    {
        TerriasFrameDispatcher.RunOnceNextFrame("Projection.RemoveStatusRecords." + statusId, () =>
        {
            RemoveStatusRecords(statusId);
            TerriasLog.Debug("[Projection] status records removed after retire from " + source + ": " + statusId);
        });
    }

    private static void RemoveStatusRecords(string statusId)
    {
        var manager = FightManager.Instance;
        if (manager == null || string.IsNullOrWhiteSpace(statusId))
        {
            return;
        }

        manager.statuses?.Remove(statusId);
        manager.statusData?.Remove(statusId);
    }

    private static void RemoveFromFightState(string statusId, bool removeStatusRecords = true)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return;
        }

        var manager = FightManager.Instance;
        if (manager != null)
        {
            if (removeStatusRecords)
            {
                RemoveStatusRecords(statusId);
            }

            if (manager.ActionQueue != null)
            {
                manager.ActionQueue.RemoveAll(obj => obj == null || obj.InstanceId == statusId);
            }
        }

        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        CompanionNativeStatusRouting.Remove(map, statusId);

        try
        {
            var ui = UIManager.Instance?.GetUI<FightUI>("FightUI");
            ui?.StatusList?.RemoveAll(status => status == null || status.InstanceId == statusId);
        }
        catch
        {
            // UI may already be gone while a fight is closing.
        }
    }
}
