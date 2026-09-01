using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

/// <summary>
/// Bounded in-memory obligation queue for observations that cannot yet bind to
/// the authoritative replay state. Reading a ready item never removes it; the
/// recorder commits removal only after every downstream owner has succeeded.
/// The recorder's capture lock owns synchronization.
/// </summary>
internal sealed class ReplayDeferredObservationQueueV17<T> where T : class
{
    private readonly int capacity;
    private readonly Func<T, long> orderKey;
    private readonly List<T> values = new();

    internal ReplayDeferredObservationQueueV17(int capacity, Func<T, long> orderKey)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        this.orderKey = orderKey ?? throw new ArgumentNullException(nameof(orderKey));
    }

    internal int Count => values.Count;

    internal IReadOnlyList<T> Snapshot => values
        .OrderBy(orderKey)
        .ToList();

    internal bool TryEnqueue(T value, Func<T, bool>? duplicate = null)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (duplicate != null && values.Any(duplicate)) return true;
        if (values.Count >= capacity) return false;
        values.Add(value);
        return true;
    }

    internal IReadOnlyList<T> Ready(Func<T, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return values.Where(predicate).OrderBy(orderKey).ToList();
    }

    internal bool Commit(T value) => value != null && values.Remove(value);

    internal void Clear() => values.Clear();
}
