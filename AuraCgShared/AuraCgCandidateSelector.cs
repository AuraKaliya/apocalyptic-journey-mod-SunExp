using System;
using System.Collections.Generic;
using System.Text;

namespace AuraCg.Shared;

public static class AuraCgSelectionModes
{
    public const string Priority = "priority";
    public const string Random = "random";
    public const string Sequential = "sequential";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Random, StringComparison.OrdinalIgnoreCase)) return Random;
        if (string.Equals(value, Sequential, StringComparison.OrdinalIgnoreCase)) return Sequential;
        return Priority;
    }
}

public static class AuraCgCandidateSelector
{
    public static T? Select<T>(IReadOnlyList<T>? candidates, string selectionMode, string selectionKey, long sequence)
        where T : class
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        var mode = AuraCgSelectionModes.Normalize(selectionMode);
        if (string.Equals(mode, AuraCgSelectionModes.Priority, StringComparison.Ordinal))
        {
            return candidates[0];
        }

        var normalizedSequence = Math.Max(1, sequence);
        var index = string.Equals(mode, AuraCgSelectionModes.Sequential, StringComparison.Ordinal)
            ? (int)((normalizedSequence - 1) % candidates.Count)
            : (int)(StableHash((selectionKey ?? "") + "|" + normalizedSequence) % (uint)candidates.Count);
        return candidates[index];
    }

    private static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var item in Encoding.UTF8.GetBytes(value ?? ""))
        {
            hash ^= item;
            hash *= 16777619u;
        }

        return hash;
    }
}
