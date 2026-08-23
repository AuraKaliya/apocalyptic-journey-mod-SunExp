using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords;

internal sealed class MatchRecordLibraryScrollState
{
    internal string FocusedId { get; set; } = "";

    internal string AnchorId { get; set; } = "";

    internal float AnchorOffsetY { get; set; }

    internal float NormalizedFallback { get; set; } = 1f;

    internal MatchRecordLibraryScrollState Clone()
    {
        return new MatchRecordLibraryScrollState
        {
            FocusedId = FocusedId,
            AnchorId = AnchorId,
            AnchorOffsetY = AnchorOffsetY,
            NormalizedFallback = Math.Max(0f, Math.Min(1f, NormalizedFallback))
        };
    }
}

/// <summary>
/// Logical return address for the match-record library. It deliberately contains
/// no Unity objects so a destroyed SettingUI can be reconstructed without retaining
/// stale transforms, canvases, or event targets.
/// </summary>
internal sealed class MatchRecordLibraryViewState
{
    internal string Collection { get; set; } = Model.MatchRecordCollections.Auto;

    internal List<long> Cursors { get; set; } = new() { 0 };

    internal int PageIndex { get; set; }

    internal string SearchText { get; set; } = "";

    internal string ResultFilter { get; set; } = "";

    internal int DateRangeDays { get; set; }

    internal bool CompatibleOnly { get; set; }

    internal HashSet<string> SelectedIds { get; set; } = new(StringComparer.Ordinal);

    internal string EditingId { get; set; } = "";

    internal string EditingTags { get; set; } = "";

    internal string EditingNotes { get; set; } = "";

    internal string FocusRecordId { get; set; } = "";

    internal MatchRecordLibraryScrollState? Scroll { get; set; }

    internal MatchRecordLibraryViewState CloneNormalized()
    {
        var cursors = (Cursors ?? new List<long>())
            .Where(value => value >= 0)
            .ToList();
        if (cursors.Count == 0 || cursors[0] != 0)
        {
            cursors.Insert(0, 0);
        }

        var page = Math.Max(0, Math.Min(PageIndex, cursors.Count - 1));
        return new MatchRecordLibraryViewState
        {
            Collection = string.IsNullOrWhiteSpace(Collection)
                ? Model.MatchRecordCollections.Auto
                : Collection,
            Cursors = cursors,
            PageIndex = page,
            SearchText = (SearchText ?? "").Trim(),
            ResultFilter = (ResultFilter ?? "").Trim(),
            DateRangeDays = DateRangeDays is 7 or 30 ? DateRangeDays : 0,
            CompatibleOnly = CompatibleOnly,
            SelectedIds = new HashSet<string>(
                (SelectedIds ?? new HashSet<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal),
            EditingId = EditingId ?? "",
            EditingTags = EditingTags ?? "",
            EditingNotes = EditingNotes ?? "",
            FocusRecordId = FocusRecordId ?? "",
            Scroll = Scroll?.Clone()
        };
    }
}
