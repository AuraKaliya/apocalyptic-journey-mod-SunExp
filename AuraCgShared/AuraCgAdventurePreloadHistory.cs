using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal sealed class AuraCgAdventurePreloadHistory
{
    private readonly int capacity;
    private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> order = new();

    public AuraCgAdventurePreloadHistory(int capacity)
    {
        this.capacity = Math.Max(1, capacity);
    }

    public int Count => keys.Count;

    public bool TryBegin(string key)
    {
        var normalized = (key ?? "").Trim();
        if (normalized.Length == 0 || !keys.Add(normalized))
        {
            return false;
        }

        order.Enqueue(normalized);
        while (order.Count > capacity)
        {
            keys.Remove(order.Dequeue());
        }

        return true;
    }
}
