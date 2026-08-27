using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayLifecycleAndLibraryReturn()
    {
        var failedPreparation = new MatchReplayLifecycleState();
        Assert(failedPreparation.TryBeginPreparation("record-a", true, out _)
               && failedPreparation.Phase == MatchReplayLifecyclePhase.Preparing,
            "interactive replay starts in preparation without committing its origin UI");
        var failedDecision = failedPreparation.BeginExit(
            MatchReplayExitKind.StartFailed,
            "native view failed");
        Assert(!failedDecision.ReturnToLibrary
               && failedDecision.Message == "native view failed",
            "preparation failure stays on the existing match-record page instead of rebuilding it");
        failedPreparation.CompleteExit();
        Assert(failedPreparation.Phase == MatchReplayLifecyclePhase.Idle,
            "failed preparation reaches the idle terminal state after owned cleanup");

        var playback = new MatchReplayLifecycleState();
        Assert(playback.TryBeginPreparation("record-b", true, out _),
            "a fresh interactive replay can begin preparation");
        playback.MarkPrepared();
        playback.CommitOrigin();
        playback.MarkActive();
        var completed = playback.BeginExit(MatchReplayExitKind.Completed);
        Assert(completed.ReturnToLibrary
               && completed.Message == "回放已结束。"
               && playback.IsExiting,
            "a completed committed replay has exactly one match-record return destination");
        playback.CompleteExit();
        Assert(playback.TryBeginPreparation("record-c", false, out _),
            "an unattended export can start after the prior return lifecycle completes");
        playback.MarkPrepared();
        playback.MarkActive();
        var unattended = playback.BeginExit(MatchReplayExitKind.ExportCompleted);
        Assert(!unattended.ReturnToLibrary,
            "startup export recovery does not invent a user-facing return destination");
        playback.CompleteExit();

        var view = new MatchRecordLibraryViewState
        {
            Collection = MatchRecordCollections.Favorite,
            Cursors = new List<long> { 0, 25, 50 },
            PageIndex = 2,
            SearchText = "  星辰  ",
            ResultFilter = "win",
            DateRangeDays = 30,
            CompatibleOnly = true,
            SelectedIds = new HashSet<string>(StringComparer.Ordinal) { "record-b" },
            EditingId = "record-b",
            EditingTags = "收藏",
            EditingNotes = "回放后继续编辑",
            FocusRecordId = "record-b",
            Scroll = new MatchRecordLibraryScrollState
            {
                AnchorId = "match-record.record-b",
                AnchorOffsetY = 18f,
                NormalizedFallback = 0.35f
            }
        };
        var restored = view.CloneNormalized();
        view.Cursors[1] = 999;
        view.SelectedIds.Clear();
        Assert(restored.Collection == MatchRecordCollections.Favorite
               && restored.Cursors.SequenceEqual(new long[] { 0, 25, 50 })
               && restored.PageIndex == 2
               && restored.SearchText == "星辰"
               && restored.ResultFilter == "win"
               && restored.DateRangeDays == 30
               && restored.CompatibleOnly
               && restored.SelectedIds.SetEquals(new[] { "record-b" })
               && restored.EditingId == "record-b"
               && restored.Scroll?.AnchorId == "match-record.record-b"
               && Math.Abs(restored.Scroll.NormalizedFallback - 0.35f) < 0.001f,
            "replay return retains an independent logical snapshot of filters, paging, selection, editing and scroll state");
    }
}
