using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

/// <summary>
/// Canonical battle-faction roster for companion slots, planning, presentation,
/// and execution. RoleStatusMap is an ownership/status routing table and can
/// contain enemies, so it must never be used as a faction roster.
/// </summary>
public static class CompanionFriendlyRosterService
{
    private const int InitialCapacity = 4;

    public static IReadOnlyList<IStatusManager> Snapshot(bool includeControlled = true)
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
        if (includeControlled)
        {
            foreach (var entry in HeartChangeControlService.ActiveSlotStatuses()
                         .OrderBy(entry => entry.Key)
                         .ThenBy(entry => entry.Value?.InstanceId, StringComparer.Ordinal))
            {
                Add(result, seen, entry.Value);
            }
        }

        return result;
    }

    public static bool Contains(IStatusManager? target, bool includeControlled = true)
    {
        if (target == null || string.IsNullOrWhiteSpace(target.InstanceId))
        {
            return false;
        }

        return Snapshot(includeControlled).Any(candidate =>
            string.Equals(candidate.InstanceId, target.InstanceId, StringComparison.Ordinal));
    }

    private static void Add(
        ICollection<IStatusManager> result,
        ISet<string> seen,
        IStatusManager? status)
    {
        if (status == null || string.IsNullOrWhiteSpace(status.InstanceId) || !seen.Add(status.InstanceId))
        {
            return;
        }

        result.Add(status);
    }
}
