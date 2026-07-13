using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class SpiritStateStore
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, SpiritState> Spirits = new(StringComparer.Ordinal);
    private static int nextIndex;

    public static event Action<SpiritState>? Registered;
    public static event Action<SpiritState>? Retired;
    public static event Action<SpiritState, CompanionIntentPlan>? IntentPresented;
    public static event Action<SpiritState>? ActionPresented;

    public static string NextStatusId()
    {
        lock (SyncRoot)
        {
            for (var i = 0; i < 64; i++)
            {
                var id = SunExpIds.SpiritStatusIdPrefix + nextIndex++;
                if (FightManager.Instance?.statuses?.ContainsKey(id) != true)
                {
                    return id;
                }
            }
        }

        return SunExpIds.SpiritStatusIdPrefix + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    public static void Register(SpiritState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.StatusId))
        {
            return;
        }

        lock (SyncRoot)
        {
            Spirits[state.StatusId] = state;
        }

        Registered?.Invoke(state);
        SunExpPerformanceCounters.Record("Spirit.Registered");
    }

    public static IReadOnlyList<SpiritState> Active()
    {
        lock (SyncRoot)
        {
            return Spirits.Values.Where(IsAlive).ToArray();
        }
    }

    public static SpiritState? Find(string statusId)
    {
        lock (SyncRoot)
        {
            return !string.IsNullOrWhiteSpace(statusId) && Spirits.TryGetValue(statusId, out var state) ? state : null;
        }
    }

    public static SpiritState? FindByOwner(string ownerPlayerId, string ownerStatusId = "")
    {
        lock (SyncRoot)
        {
            return Spirits.Values.FirstOrDefault(state =>
                (!string.IsNullOrWhiteSpace(ownerPlayerId) && string.Equals(state.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(ownerStatusId) && string.Equals(state.OwnerStatusId, ownerStatusId, StringComparison.Ordinal)));
        }
    }

    public static bool HasForOwner(string ownerPlayerId, string ownerStatusId = "") => FindByOwner(ownerPlayerId, ownerStatusId) != null;

    public static bool IsSpirit(IStatusManager? status) => status != null && Find(status.InstanceId) != null;

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

    public static bool RetireIfDead(IStatusManager? status, string source)
    {
        if (!IsSpirit(status) || status!.CurHp > 0 && status.MaxHp > 0 && status.state != IStatusManager.State.Dead)
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

        SpiritState? state;
        lock (SyncRoot)
        {
            Spirits.TryGetValue(status.InstanceId, out state);
            Spirits.Remove(status.InstanceId);
        }

        if (state == null)
        {
            return;
        }

        RemoveFightRecords(state.StatusId);
        CompanionBattleStateStore.Remove(state.StatusId);
        status.state = IStatusManager.State.Dead;
        state.Spirit.DeadEffect();
        Retired?.Invoke(state);
        SunExpLog.Info("[Spirit] retired from " + source + ": status=" + state.StatusId + ", enemy=" + state.Snapshot.EnemyId);
    }

    public static void ClearAll(string source)
    {
        SpiritState[] states;
        lock (SyncRoot)
        {
            states = Spirits.Values.ToArray();
            Spirits.Clear();
        }

        foreach (var state in states)
        {
            RemoveFightRecords(state.StatusId);
            CompanionBattleStateStore.Remove(state.StatusId);
            if (state.Spirit?.gameObject != null)
            {
                UnityEngine.Object.Destroy(state.Spirit.gameObject);
            }
            Retired?.Invoke(state);
        }

        SunExpLog.Debug("[Spirit] cleared from " + source + ": count=" + states.Length);
    }

    private static bool IsAlive(SpiritState state)
    {
        var spirit = state?.Spirit;
        var status = spirit?.Status;
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead && spirit!.gameObject != null;
    }

    private static void RemoveFightRecords(string statusId)
    {
        var manager = FightManager.Instance;
        manager?.statuses?.Remove(statusId);
        manager?.statusData?.Remove(statusId);
        manager?.ActionQueue?.RemoveAll(item => item == null || item.InstanceId == statusId);
        try
        {
            UIManager.Instance?.GetUI<FightUI>("FightUI")?.StatusList?.RemoveAll(status => status == null || status.InstanceId == statusId);
        }
        catch
        {
            // Fight UI can already be closing.
        }
    }
}
