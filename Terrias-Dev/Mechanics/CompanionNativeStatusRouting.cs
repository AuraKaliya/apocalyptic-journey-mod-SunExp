using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Maintains the native status-event routing table for synthetic companions.
/// This table is not a friendly roster and does not add HUD or targeting rows.
/// </summary>
public static class CompanionNativeStatusRouting
{
    public static bool Register(
        IDictionary<string, List<string>>? roleStatusMap,
        string? ownerPlayerId,
        string? statusId)
    {
        var owner = (ownerPlayerId ?? "").Trim();
        var status = (statusId ?? "").Trim();
        if (roleStatusMap == null || owner.Length == 0 || status.Length == 0)
        {
            return false;
        }

        var occurrenceCount = 0;
        var ownerOccurrenceCount = 0;
        foreach (var entry in roleStatusMap)
        {
            if (entry.Value == null)
            {
                continue;
            }

            var count = 0;
            foreach (var candidate in entry.Value)
            {
                if (string.Equals(candidate, status, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            occurrenceCount += count;
            if (string.Equals(entry.Key, owner, StringComparison.Ordinal))
            {
                ownerOccurrenceCount = count;
            }
        }

        if (occurrenceCount == 1 && ownerOccurrenceCount == 1)
        {
            return true;
        }

        Remove(roleStatusMap, status);
        if (!roleStatusMap.TryGetValue(owner, out var statuses) || statuses == null)
        {
            statuses = new List<string>();
            roleStatusMap[owner] = statuses;
        }

        if (!statuses.Contains(status))
        {
            statuses.Add(status);
        }

        return true;
    }

    public static int Remove(
        IDictionary<string, List<string>>? roleStatusMap,
        string? statusId)
    {
        var status = (statusId ?? "").Trim();
        if (roleStatusMap == null || status.Length == 0)
        {
            return 0;
        }

        var removed = 0;
        foreach (var statuses in roleStatusMap.Values)
        {
            if (statuses == null)
            {
                continue;
            }

            while (statuses.Remove(status))
            {
                removed++;
            }
        }

        return removed;
    }

    public static bool Contains(
        IDictionary<string, List<string>>? roleStatusMap,
        string? ownerPlayerId,
        string? statusId)
    {
        return roleStatusMap != null
               && roleStatusMap.TryGetValue((ownerPlayerId ?? "").Trim(), out var statuses)
               && statuses != null
               && statuses.Contains((statusId ?? "").Trim());
    }
}
