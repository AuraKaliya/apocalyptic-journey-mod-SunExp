using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCg.Shared;

internal sealed class AuraCgPreloadSubmission<T>
{
    private AuraCgPreloadSubmission(List<T> items, bool truncated)
    {
        Items = items;
        Truncated = truncated;
    }

    public List<T> Items { get; }

    public bool Truncated { get; }

    public static AuraCgPreloadSubmission<T> Capture(IEnumerable<T>? source, int maximumItems)
    {
        var maximum = Math.Max(1, maximumItems);
        var items = (source ?? Array.Empty<T>())
            .Take(maximum + 1)
            .ToList();
        var truncated = items.Count > maximum;
        if (truncated)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new AuraCgPreloadSubmission<T>(items, truncated);
    }
}
