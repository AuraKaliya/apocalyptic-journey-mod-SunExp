using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class PartnerTurnOrderPolicy
{
    public static IReadOnlyList<T> ReorderPartnerSubsequence<T>(
        IReadOnlyList<T>? source,
        Func<T, bool> isPartner,
        Func<T, int> speed,
        Func<T, string> stableId)
    {
        var result = (source ?? Array.Empty<T>()).ToList();
        var slots = result.Select((value, index) => new { value, index })
            .Where(item => isPartner(item.value))
            .ToArray();
        var ordered = slots
            .OrderByDescending(item => speed(item.value))
            .ThenBy(item => item.index)
            .ThenBy(item => stableId(item.value) ?? "", StringComparer.Ordinal)
            .Select(item => item.value)
            .ToArray();
        for (var index = 0; index < slots.Length; index++) result[slots[index].index] = ordered[index];
        return result;
    }
}
