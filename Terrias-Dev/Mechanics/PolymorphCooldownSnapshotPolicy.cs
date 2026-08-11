using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class PolymorphCooldownSnapshotPolicy
{
    public static Dictionary<string, int> Normalize(IReadOnlyDictionary<string, int>? source)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (source == null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            var id = (pair.Key ?? "").Trim().TrimStart('*');
            if (id.Length > 0)
            {
                result[id] = Math.Max(0, pair.Value);
            }
        }

        return result;
    }

    public static Dictionary<string, int> ResolveEntry(
        IEnumerable<string>? skillIds,
        IReadOnlyDictionary<string, int>? initialized,
        IReadOnlyDictionary<string, int>? saved)
    {
        var initial = Normalize(initialized);
        var previous = saved == null ? null : Normalize(saved);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rawId in skillIds ?? Array.Empty<string>())
        {
            var id = (rawId ?? "").Trim().TrimStart('*');
            if (id.Length == 0 || result.ContainsKey(id))
            {
                continue;
            }

            result[id] = previous != null && previous.TryGetValue(id, out var restored)
                ? restored
                : initial.TryGetValue(id, out var firstEntry)
                    ? firstEntry
                    : 0;
        }

        return result;
    }
}
