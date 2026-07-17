using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal sealed class AuraCgPreloadCoordinator
{
    private readonly int maximumAdventureKeys;
    private readonly HashSet<string> pendingKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> adventureKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> adventureOrder = new();

    public AuraCgPreloadCoordinator(int maximumAdventureKeys)
    {
        this.maximumAdventureKeys = Math.Max(1, maximumAdventureKeys);
    }

    public int PendingCount => pendingKeys.Count;

    public int AdventureCount => adventureKeys.Count;

    public bool TryBeginPreload(string key, bool alreadyCached)
    {
        return !alreadyCached
               && !string.IsNullOrWhiteSpace(key)
               && pendingKeys.Add(key);
    }

    public void CompletePreload(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            pendingKeys.Remove(key);
        }
    }

    public bool TryBeginAdventure(string key)
    {
        var normalized = (key ?? "").Trim();
        if (normalized.Length == 0 || !adventureKeys.Add(normalized))
        {
            return false;
        }

        adventureOrder.Enqueue(normalized);
        while (adventureOrder.Count > maximumAdventureKeys)
        {
            adventureKeys.Remove(adventureOrder.Dequeue());
        }

        return true;
    }
}
