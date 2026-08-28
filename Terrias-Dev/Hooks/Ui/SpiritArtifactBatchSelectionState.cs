using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritArtifactBatchSelectionState
{
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);

    public bool IsActive { get; private set; }

    public int Count => selected.Count;

    public IReadOnlyCollection<string> SelectedArtifactUids => selected.ToArray();

    public bool Contains(string? artifactUid)
        => selected.Contains(Normalize(artifactUid));

    public void Enter(string? initialArtifactUid = null)
    {
        IsActive = true;
        selected.Clear();
        var initial = Normalize(initialArtifactUid);
        if (initial.Length > 0) selected.Add(initial);
    }

    public void Exit()
    {
        IsActive = false;
        selected.Clear();
    }

    public bool Toggle(string? artifactUid)
    {
        if (!IsActive) return false;
        var uid = Normalize(artifactUid);
        if (uid.Length == 0) return false;
        if (!selected.Add(uid)) selected.Remove(uid);
        return selected.Contains(uid);
    }

    public void Replace(IEnumerable<string>? artifactUids)
    {
        if (!IsActive) return;
        selected.Clear();
        Add(artifactUids);
    }

    public void Add(IEnumerable<string>? artifactUids)
    {
        if (!IsActive || artifactUids == null) return;
        foreach (var value in artifactUids)
        {
            var uid = Normalize(value);
            if (uid.Length > 0) selected.Add(uid);
        }
    }

    public void Clear()
    {
        if (IsActive) selected.Clear();
    }

    public bool Reconcile(IEnumerable<string>? availableArtifactUids)
    {
        if (!IsActive) return false;
        var available = new HashSet<string>(
            (availableArtifactUids ?? Array.Empty<string>()).Select(Normalize),
            StringComparer.Ordinal);
        return selected.RemoveWhere(uid => !available.Contains(uid)) > 0;
    }

    public bool Remove(IEnumerable<string>? artifactUids)
    {
        if (!IsActive || artifactUids == null) return false;
        var changed = false;
        foreach (var value in artifactUids) changed |= selected.Remove(Normalize(value));
        return changed;
    }

    private static string Normalize(string? value) => (value ?? "").Trim();
}
