using System;
using System.Collections.Generic;

namespace Terrias.Dll.Hooks.Ui;

internal readonly struct SpiritArtifactSelectionChange
{
    public SpiritArtifactSelectionChange(string previousUid, string currentUid)
    {
        PreviousUid = previousUid ?? "";
        CurrentUid = currentUid ?? "";
    }

    public string PreviousUid { get; }

    public string CurrentUid { get; }

    public bool Changed => !string.Equals(PreviousUid, CurrentUid, StringComparison.Ordinal);

    public bool HasSelection => CurrentUid.Length > 0;
}

internal sealed class SpiritArtifactSelectionState
{
    public string SelectedArtifactUid { get; private set; } = "";

    public SpiritArtifactSelectionChange Toggle(string? artifactUid)
    {
        var next = Normalize(artifactUid);
        if (next.Length > 0 && string.Equals(next, SelectedArtifactUid, StringComparison.Ordinal))
            next = "";
        return Set(next);
    }

    public SpiritArtifactSelectionChange Select(string? artifactUid)
        => Set(Normalize(artifactUid));

    public SpiritArtifactSelectionChange Clear()
        => Set("");

    public SpiritArtifactSelectionChange Reconcile(IEnumerable<string>? availableArtifactUids)
    {
        if (SelectedArtifactUid.Length == 0) return new SpiritArtifactSelectionChange("", "");
        if (availableArtifactUids != null)
        {
            foreach (var value in availableArtifactUids)
            {
                if (string.Equals(Normalize(value), SelectedArtifactUid, StringComparison.Ordinal))
                    return new SpiritArtifactSelectionChange(SelectedArtifactUid, SelectedArtifactUid);
            }
        }
        return Clear();
    }

    private SpiritArtifactSelectionChange Set(string next)
    {
        var previous = SelectedArtifactUid;
        SelectedArtifactUid = next;
        return new SpiritArtifactSelectionChange(previous, next);
    }

    private static string Normalize(string? value)
        => (value ?? "").Trim();
}
