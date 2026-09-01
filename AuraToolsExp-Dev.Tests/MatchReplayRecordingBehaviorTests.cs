using AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayNativeAudioCallTracking()
    {
        var tracker = new ReplayNativeAudioCallTracker();
        tracker.BeginSymbolic("Effect", new[] { "NewSounds/Card/Draw" });
        Assert(tracker.PendingCount == 1,
            "native symbolic effect calls wait for the actual playback clip instead of creating a cue");

        var resolved = tracker.ObserveClip("Effect", "draw", 17);
        Assert(resolved.InheritedSymbolicCall
               && resolved.ResourceId == "NewSounds/Card/Draw",
            "the actual effect clip inherits the exact symbolic resource identity");
        tracker.EndSymbolic("Effect");
        Assert(tracker.PendingCount == 0,
            "the outer effect call always releases its correlation frame");

        tracker.BeginSymbolic("Effect", new[] { "missing-effect" });
        tracker.EndSymbolic("Effect");
        var unrelated = tracker.ObserveClip("Effect", "later/clip:name", 23);
        Assert(!unrelated.InheritedSymbolicCall
               && unrelated.ResourceId == "Clip/later_clip_name",
            "a null native effect cannot leak its symbolic identity into the next real clip");

        tracker.BeginSymbolic("Vocal", new[] { "role-id", "Voice/actual-line" });
        var vocal = tracker.ObserveClip("Vocal", "voice", 31);
        Assert(vocal.ResourceId == "Voice/actual-line",
            "vocal correlation uses the clip path rather than mistaking the role id for an audio resource");
        tracker.EndSymbolic("Vocal");

        var anonymous = tracker.ObserveClip("Effect", "", 41);
        Assert(anonymous.ResourceId == "Clip/instance-41",
            "an actual anonymous clip still receives a deterministic safe diagnostic identity");

        var terminal = new MatchReplayTerminalGate();
        terminal.Prepare("Win");
        Assert(!terminal.CanFinalize,
            "settlement preparation alone cannot detach a replay before terminal UI cleanup");
        terminal.SealTerminalFrame("Ended");
        Assert(terminal.Result == "Win" && terminal.CanFinalize,
            "the post-cleanup terminal frame preserves the real outcome and opens replay finalization");

        var baseline = new MatchReplayBaselineGate();
        baseline.Arm();
        Assert(!baseline.CanCaptureTimeline,
            "capturing FightManager.Init context does not open the replay timeline before materialization");
        Assert(!baseline.TryCommit(() => true),
            "a populated FightManager before BattleMaterialized cannot open the canonical timeline early");
        baseline.MarkMaterialized();
        Assert(!baseline.TryCommit(() => false) && !baseline.CanCaptureTimeline,
            "an incomplete materialized snapshot cannot become the authoritative replay baseline");
        Assert(baseline.TryCommit(() => true) && baseline.CanCaptureTimeline,
            "the first complete BattleMaterialized snapshot opens the replay timeline exactly once");
        Assert(!baseline.TryCommit(() => true),
            "later lifecycle callbacks cannot replace the committed materialized baseline");
        baseline.Reset();
        Assert(!baseline.CanCaptureTimeline,
            "battle cleanup closes the materialized replay timeline gate");
    }
}
