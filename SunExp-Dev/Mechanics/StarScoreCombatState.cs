using System;
using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public sealed class StarScoreCombatState
{
    private readonly List<string> notes = new();
    private readonly List<string> completedCadences = new();

    public IReadOnlyList<string> Notes => notes;

    public IReadOnlyList<string> CompletedCadences => completedCadences;

    public void Record(string note, int windowSize)
    {
        notes.Add(note);
        while (notes.Count > Math.Max(1, windowSize))
        {
            notes.RemoveAt(0);
        }
    }

    public void RecordCompletedCadence(string pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            completedCadences.Add(pattern);
        }
    }

    public void Clear()
    {
        notes.Clear();
        completedCadences.Clear();
    }
}

public static class StarScoreCombatStateStore
{
    private static readonly Dictionary<string, StarScoreCombatState> States = new(StringComparer.Ordinal);

    public static StarScoreCombatState? GetOrCreate(IStatusManager? owner)
    {
        var key = owner?.InstanceId ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!States.TryGetValue(key, out var state))
        {
            state = new StarScoreCombatState();
            States[key] = state;
        }

        return state;
    }

    public static void Remove(IStatusManager? owner)
    {
        var key = owner?.InstanceId ?? "";
        if (!string.IsNullOrWhiteSpace(key))
        {
            States.Remove(key);
        }
    }

    public static void ClearAll()
    {
        States.Clear();
    }
}
