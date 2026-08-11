using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

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
                var id = TerriasIds.SpiritStatusIdPrefix + nextIndex++;
                if (FightManager.Instance?.statuses?.ContainsKey(id) != true)
                {
                    return id;
                }
            }
        }

        return TerriasIds.SpiritStatusIdPrefix + Guid.NewGuid().ToString("N").Substring(0, 6);
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
        TerriasPerformanceCounters.Record("Spirit.Registered");
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
            var hasPlayerId = !string.IsNullOrWhiteSpace(ownerPlayerId);
            var hasStatusId = !string.IsNullOrWhiteSpace(ownerStatusId);
            return Spirits.Values.FirstOrDefault(state =>
                (hasPlayerId || hasStatusId)
                && (!hasPlayerId || string.Equals(state.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal))
                && (!hasStatusId || string.Equals(state.OwnerStatusId, ownerStatusId, StringComparison.Ordinal)));
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

        Remove(status.InstanceId, source, playDeathEffect: true, broadcast: true);
    }

    public static bool Withdraw(string statusId, string source)
    {
        return Remove(statusId, source, playDeathEffect: false, broadcast: true);
    }

    public static bool RemoveAuthoritative(string statusId, string source, bool playDeathEffect)
    {
        return Remove(statusId, source, playDeathEffect, broadcast: false);
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
            var spirit = state.Spirit;
            if (spirit != null)
            {
                UnityEngine.Object.Destroy(spirit.gameObject);
            }
            Retired?.Invoke(state);
        }

        TerriasLog.Debug("[Spirit] cleared from " + source + ": count=" + states.Length);
    }

    private static bool IsAlive(SpiritState state)
    {
        var spirit = state?.Spirit;
        if (spirit == null)
        {
            return false;
        }

        var status = spirit.Status;
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead && spirit.gameObject != null;
    }

    private static bool Remove(string statusId, string source, bool playDeathEffect, bool broadcast)
    {
        SpiritState? state;
        lock (SyncRoot)
        {
            Spirits.TryGetValue(statusId ?? "", out state);
            if (state != null)
            {
                Spirits.Remove(state.StatusId);
            }
        }

        if (state == null)
        {
            return false;
        }

        if (broadcast)
        {
            SpiritSummonService.BroadcastRemoval(state, source, playDeathEffect);
        }

        var spirit = state.Spirit;
        if (spirit != null)
        {
            if (playDeathEffect)
            {
                if (spirit.Status != null && spirit.Status.state != IStatusManager.State.Dead)
                {
                    spirit.Status.state = IStatusManager.State.Dead;
                }
                spirit.DeadEffect();
            }
            else
            {
                UnityEngine.Object.Destroy(spirit.gameObject);
            }
        }

        RemoveFightRecords(state.StatusId);
        CompanionBattleStateStore.Remove(state.StatusId);

        Retired?.Invoke(state);
        TerriasLog.Info("[Spirit] " + (playDeathEffect ? "retired" : "withdrawn") + " from " + source
            + ": status=" + state.StatusId + ", enemy=" + state.Snapshot.EnemyId);
        TerriasPerformanceCounters.Record(playDeathEffect ? "Spirit.Retired" : "Spirit.Withdrawn");
        return true;
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
