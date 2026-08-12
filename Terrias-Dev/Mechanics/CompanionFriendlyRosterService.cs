using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Canonical battle-faction roster for companion slots, planning, presentation,
/// and execution. RoleStatusMap is an ownership/status routing table and can
/// contain enemies, so it must never be used as a faction roster.
/// </summary>
public static class CompanionFriendlyRosterService
{
    private const int InitialCapacity = 4;

    public static IReadOnlyList<IStatusManager> Snapshot(
        bool includeCompanions = true)
    {
        var result = new List<IStatusManager>(InitialCapacity);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var manager = FightManager.Instance;
        try
        {
            if (manager?.roleQueue != null)
            {
                foreach (var role in manager.roleQueue)
                {
                    var instanceId = role?.InstanceId ?? "";
                    if (!string.IsNullOrWhiteSpace(instanceId)
                        && manager.statuses?.TryGetValue(instanceId, out var status) == true)
                    {
                        Add(result, seen, status);
                    }
                }
            }
        }
        catch
        {
            // The singleton player below is the deterministic local fallback.
        }

        Add(result, seen, FightPlayer.Instance?.Status);
        if (includeCompanions)
        {
            try
            {
                foreach (var status in manager?.statuses?.Values
                             .Where(status => status?.fatherObject is Partner)
                             .OrderBy(status => status.InstanceId, StringComparer.Ordinal)
                         ?? Enumerable.Empty<IStatusManager>())
                {
                    Add(result, seen, status);
                }
            }
            catch
            {
                // Terrias stores below remain the authoritative fallback.
            }

            foreach (var status in HeartChangeControlService.ActiveStatuses()
                         .OrderBy(status => status.InstanceId, StringComparer.Ordinal))
            {
                Add(result, seen, status);
            }

            foreach (var state in ProjectionStateStore.Active()
                         .OrderBy(entry => entry.OwnerPlayerId, StringComparer.Ordinal)
                         .ThenBy(entry => entry.StatusId, StringComparer.Ordinal))
            {
                Add(result, seen, state.Projection?.Status);
            }
            foreach (var state in SpiritStateStore.Active()
                         .OrderBy(entry => entry.OwnerPlayerId, StringComparer.Ordinal)
                         .ThenBy(entry => entry.StatusId, StringComparer.Ordinal))
            {
                Add(result, seen, state.Spirit?.Status);
            }
        }

        return result;
    }

    public static bool Contains(
        IStatusManager? target,
        bool includeCompanions = true)
    {
        if (target == null || string.IsNullOrWhiteSpace(target.InstanceId))
        {
            return false;
        }

        return Snapshot(includeCompanions).Any(candidate =>
            string.Equals(candidate.InstanceId, target.InstanceId, StringComparison.Ordinal));
    }

    private static void Add(
        ICollection<IStatusManager> result,
        ISet<string> seen,
        IStatusManager? status)
    {
        if (status == null
            || string.IsNullOrWhiteSpace(status.InstanceId)
            || status.CurHp <= 0
            || status.state == IStatusManager.State.Dead
            || !seen.Add(status.InstanceId))
        {
            return;
        }

        result.Add(status);
    }
}
