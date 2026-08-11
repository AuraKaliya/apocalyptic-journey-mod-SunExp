using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class CompanionBattleStateStore
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, CompanionBattleState> States = new(StringComparer.Ordinal);

    public static CompanionBattleState Create(
        string statusId,
        string roleId,
        string ownerStatusId,
        int slotIndex,
        CompanionStats stats,
        string ownerPlayerId = "",
        string entityKind = "ProjectionAttachment")
    {
        var state = new CompanionBattleState(statusId, roleId, ownerStatusId, slotIndex, stats, ownerPlayerId, entityKind);
        lock (SyncRoot)
        {
            States[state.StatusId] = state;
        }

        CompanionThreatService.Register(state);
        CompanionOwnershipService.Register(state.Identity);
        TerriasPerformanceCounters.Record("Companion.State.Created");
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
        CompanionOwnershipService.Remove(id);
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            States.Clear();
        }

        CompanionThreatService.Clear();
        CompanionOwnershipService.Clear();
    }

    public static IReadOnlyList<CompanionBattleState> Snapshot()
    {
        lock (SyncRoot) return States.Values.ToArray();
    }
}
