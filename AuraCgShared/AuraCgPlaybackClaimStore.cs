using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal sealed class AuraCgPlaybackClaimStore
{
    private readonly int capacity;
    private readonly HashSet<string> keys = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();

    public AuraCgPlaybackClaimStore(int capacity)
    {
        this.capacity = Math.Max(1, capacity);
    }

    public int Count => keys.Count;

    public bool TryClaim(string issuerPlayerId, string playId, out string key)
    {
        key = AuraCgNetworkPolicy.PlaybackKey(issuerPlayerId, playId);
        if (string.IsNullOrWhiteSpace(key) || !keys.Add(key))
        {
            return false;
        }

        order.Enqueue(key);
        while (order.Count > capacity)
        {
            keys.Remove(order.Dequeue());
        }

        return true;
    }

    public void Clear()
    {
        keys.Clear();
        order.Clear();
    }
}
