using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class CompanionBattleStateStore
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, CompanionBattleState> States = new(StringComparer.Ordinal);

    public static CompanionBattleState Create(
        string statusId,
        string roleId,
        string ownerStatusId,
        int slotIndex,
        CompanionStats stats)
    {
        var state = new CompanionBattleState(statusId, roleId, ownerStatusId, slotIndex, stats);
        lock (SyncRoot)
        {
            States[state.StatusId] = state;
        }

        CompanionThreatService.Register(state);
        SunExpPerformanceCounters.Record("Companion.State.Created");
        return state;
    }

    public static CompanionBattleState? Find(string? statusId)
    {
        var id = statusId ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (SyncRoot)
        {
            return States.TryGetValue(id, out var state)
                ? state
                : null;
        }
    }

    public static void Remove(string? statusId)
    {
        var id = statusId ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (SyncRoot)
        {
            States.Remove(id);
        }

        CompanionThreatService.Remove(id);
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            States.Clear();
        }

        CompanionThreatService.Clear();
    }
}
