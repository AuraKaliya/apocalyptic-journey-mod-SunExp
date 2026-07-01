using System;
using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public sealed class StarScoreCombatState
{
    private readonly List<StarScoreNote> notes = new();
    private readonly List<string> completedCadences = new();

    public IReadOnlyList<StarScoreNote> Notes => notes;

    public IReadOnlyList<string> CompletedCadences => completedCadences;

    public int Version { get; private set; }

    public void Record(StarScoreNote note, int windowSize)
    {
        notes.Add(note);
        while (notes.Count > Math.Max(1, windowSize))
        {
            notes.RemoveAt(0);
        }

        Version++;
    }

    public void Record(string note, int windowSize)
    {
        if (StarScoreNoteCodes.TryFromPatternCode(note, out var parsed))
        {
            Record(parsed, windowSize);
        }
    }

    public void RecordCompletedCadence(string pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            completedCadences.Add(pattern);
        }
    }

    public void RetainLastNoteAsCadenceStart()
    {
        if (notes.Count <= 1)
        {
            return;
        }

        var last = notes[notes.Count - 1];
        notes.Clear();
        notes.Add(last);
        Version++;
    }

    public int ClearNotesOnly()
    {
        var count = notes.Count;
        if (count <= 0)
        {
            return 0;
        }

        notes.Clear();
        Version++;
        return count;
    }

    public bool ReplaceLastNote(StarScoreNote note)
    {
        if (notes.Count <= 0)
        {
            return false;
        }

        notes[notes.Count - 1] = note;
        Version++;
        return true;
    }

    public void Clear()
    {
        notes.Clear();
        completedCadences.Clear();
        Version++;
    }

    public StarScoreDisplaySnapshot Snapshot(string ownerStatusId, bool isCadencePreview = false, string completedCadencePattern = "")
    {
        return new StarScoreDisplaySnapshot(ownerStatusId, notes, Version, isCadencePreview, completedCadencePattern);
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

    public static StarScoreCombatState? Get(IStatusManager? owner)
    {
        var key = owner?.InstanceId ?? "";
        return !string.IsNullOrWhiteSpace(key) && States.TryGetValue(key, out var state)
            ? state
            : null;
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
