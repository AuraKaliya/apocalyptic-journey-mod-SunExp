using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public sealed class StarScoreDisplaySnapshot
{
    public StarScoreDisplaySnapshot(
        string ownerStatusId,
        IEnumerable<StarScoreNote> notes,
        int version,
        bool isCadencePreview = false,
        string completedCadencePattern = "")
    {
        OwnerStatusId = ownerStatusId ?? "";
        Notes = new List<StarScoreNote>(notes ?? System.Array.Empty<StarScoreNote>()).AsReadOnly();
        Version = version;
        IsCadencePreview = isCadencePreview;
        CompletedCadencePattern = completedCadencePattern ?? "";
    }

    public string OwnerStatusId { get; }

    public IReadOnlyList<StarScoreNote> Notes { get; }

    public int Version { get; }

    public bool IsCadencePreview { get; }

    public string CompletedCadencePattern { get; }

    public bool HasNotes => Notes.Count > 0;
}
