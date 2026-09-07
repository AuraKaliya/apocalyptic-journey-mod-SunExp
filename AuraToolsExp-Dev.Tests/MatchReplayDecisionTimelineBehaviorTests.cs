using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static partial class AuraToolsTestSuite
{
    private static void TestDecisionTimeline()
    {
        var shortRecord = BuildDecisionReplay(5_000_000);
        var longRecord = BuildDecisionReplay(35_000_000);
        var first = ReplayDocumentFinalizerV17.FinalizeAndValidate(shortRecord);
        var second = ReplayDocumentFinalizerV17.FinalizeAndValidate(longRecord);
        Assert(first.IsValid && second.IsValid, "decision witness documents seal: " + first.Message + "; " + second.Message);
        var sealedBytes = ReplayCanonicalJsonV17.SerializeUtf8(longRecord);
        var shortPlan = new ReplayDecisionTimelineV17(shortRecord.Document);
        var longPlan = new ReplayDecisionTimelineV17(longRecord.Document);
        Assert(shortPlan.HasDecisionTiming && shortPlan.DurationTicks == longPlan.DurationTicks,
            "different player and nested selection wait lengths produce identical viewing duration");
        Assert(shortPlan.Document.PresentationEvents.Select(item => (item.EventType, item.TimeTicks, item.Presentation!.DurationTicks))
            .SequenceEqual(longPlan.Document.PresentationEvents.Select(item => (item.EventType, item.TimeTicks, item.Presentation!.DurationTicks))),
            "state, action, selection and audio cues share one deterministic viewing schedule");
        Assert(ReplayCanonicalJsonV17.SerializeUtf8(longRecord).SequenceEqual(sealedBytes)
            && ReplayDocumentValidatorV17.Validate(longRecord).IsValid,
            "planning neither rewrites sealed source bytes nor invalidates document roots");
        Assert(longRecord.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.DecisionTimeline)
            && ReplayCapabilitiesV17.RecordingRequired.Contains(ReplayCapabilitiesV17.DecisionTimeline)
            && !ReplayCapabilitiesV17.Required.Contains(ReplayCapabilitiesV17.DecisionTimeline),
            "new recording negotiation requires decision support while legacy document reading remains supported");
        var sourceReducer = new ReplayStateReducerV17();
        var viewReducer = new ReplayStateReducerV17();
        sourceReducer.Reset(longRecord.Document.InitialState);
        viewReducer.Reset(longPlan.Document.InitialState);
        foreach (var truth in longRecord.Document.TruthEvents) sourceReducer.Apply(truth);
        foreach (var truth in longPlan.Document.TruthEvents) viewReducer.Apply(truth);
        Assert(sourceReducer.CurrentStateHash == viewReducer.CurrentStateHash,
            "retiming preserves every state delta, causal order and original state hash");
        var commit = longPlan.Document.PresentationEvents.First(item => item.EventType == ReplayEventTypesV17.DecisionCommitted);
        Assert(commit.TimeTicks == 1_500_000 && longPlan.DecisionSequences.Count == 3,
            "first decision gets the uniform half-second pause; selection and end-turn count as decisions");
        var sound = longPlan.Document.PresentationEvents.Single(item => item.Presentation?.Audio?.Bus == "Effect");
        Assert(sound.Presentation!.Audio!.StartSample == sound.TimeTicks * 48_000 / ReplayProtocolV17.TimebaseTicksPerSecond,
            "offline PCM start aligns with the same projected event used by interactive audio");
        var music = longPlan.Document.PresentationEvents.Single(item => item.Presentation?.Audio?.Bus == "Bgm");
        Assert(music.Presentation!.Audio!.LoopEndSample == 48_000
            && music.Presentation.Audio.DurationSamples == (longPlan.DurationTicks - ReplayDecisionTimelineV17.DecisionPauseTicks) * 48_000 / ReplayProtocolV17.TimebaseTicksPerSecond,
            "continuous music is shortened on the viewing clock while source loop coordinates are unchanged");
        foreach (var checkpoint in longPlan.Document.TruthCheckpoints)
        {
            var seek = new ReplayStateReducerV17();
            seek.Reset(checkpoint.State, checkpoint.EventSequence);
            foreach (var truth in longPlan.Document.TruthEvents.Where(item => item.Sequence > checkpoint.EventSequence)) seek.Apply(truth);
            Assert(seek.CurrentStateHash == viewReducer.CurrentStateHash,
                "projected checkpoint seek reaches the same final state");
        }

        var old = BuildReplayV17();
        ReplayDocumentFinalizerV17.FinalizeAndValidate(old);
        var oldBytes = ReplayCanonicalJsonV17.SerializeUtf8(old);
        var oldPlan = new ReplayDecisionTimelineV17(old.Document);
        Assert(!oldPlan.HasDecisionTiming && ReferenceEquals(oldPlan.Document, old.Document)
            && ReplayCanonicalJsonV17.SerializeUtf8(old).SequenceEqual(oldBytes),
            "legacy records without input witnesses retain their original cadence and bytes");
        var broken = ReplayCanonicalJsonV17.Clone(longRecord.Document);
        broken.PresentationEvents.RemoveAll(item => item.EventType == ReplayEventTypesV17.InputWaitEnded);
        var errors = new List<string>();
        ReplayDecisionTimelineV17.Validate(broken, errors);
        Assert(errors.Count > 0, "unclosed or overlapping waits reject instead of guessing a timeout");
        broken = ReplayCanonicalJsonV17.Clone(longRecord.Document);
        broken.Presentation.Ui.DecisionTimingContract = null;
        errors.Clear(); ReplayDecisionTimelineV17.Validate(broken, errors);
        Assert(errors.Contains("decision-timing-contract-unsupported"), "unadvertised decision boundaries reject");
        TestDecisionCaptureOwnership();
        TestNativeDecisionAdapter();
        TestProtectedDecisionExecution();
        TestDegenerateDecisionIntervals();
    }

    private static void TestDegenerateDecisionIntervals()
    {
        var source = new ReplayDocumentV17();
        source.Presentation.Ui.DecisionTimingContract = ReplayDecisionTimelineV17.Contract;
        void Boundary(string type, string kind, string id, long ticks) => source.PresentationEvents.Add(new ReplayJournalEventV17
        {
            Lane = ReplayJournalLanesV17.Presentation, Sequence = source.PresentationEvents.Count + 1,
            EventType = type, TimeTicks = ticks, Presentation = new ReplayPresentationMessageV17 { Kind = kind, SourceInstanceId = id }
        });
        Boundary(ReplayEventTypesV17.InputWaitStarted, ReplayDecisionTimelineV17.Player, "instant", 10_000_000);
        Boundary(ReplayEventTypesV17.InputWaitEnded, ReplayDecisionTimelineV17.Committed, "instant", 10_000_000);
        Boundary(ReplayEventTypesV17.DecisionCommitted, "Card", "card", 10_000_000);
        source.PresentationEvents.Add(new ReplayJournalEventV17
        {
            Lane = ReplayJournalLanesV17.Presentation, Sequence = 4, EventType = ReplayEventTypesV17.ActorAnimationPresented,
            TimeTicks = 0, Presentation = new ReplayPresentationMessageV17 { DurationTicks = 20_000_000 }
        });
        var plan = new ReplayDecisionTimelineV17(source);
        Assert(plan.ToPlayback(25_000_000) == 25_000_000
            && plan.Document.PresentationEvents.Last().Presentation!.DurationTicks == 20_000_000,
            "an instantaneous wait inside active presentation never creates an inverted cut or stretches the action");
        source.PresentationEvents.Clear();
        Boundary(ReplayEventTypesV17.InputWaitStarted, ReplayDecisionTimelineV17.Player, "cancel", 1_000_000);
        Boundary(ReplayEventTypesV17.InputWaitEnded, ReplayDecisionTimelineV17.Interrupted, "cancel", 10_000_000);
        source.PresentationEvents.Add(new ReplayJournalEventV17
        {
            Lane = ReplayJournalLanesV17.Presentation, Sequence = 3, EventType = ReplayEventTypesV17.AudioPresented,
            TimeTicks = 2_000_000, Presentation = new ReplayPresentationMessageV17
            {
                Audio = new ReplayAudioCueV17 { Bus = "Bgm", StartSample = 96_000, DurationSamples = 48_000, ResourcePath = "Audio/TransientMusic" }
            }
        });
        plan = new ReplayDecisionTimelineV17(source);
        Assert(plan.Document.PresentationEvents.All(item => item.EventType != ReplayEventTypesV17.AudioPresented)
            && source.PresentationEvents.Last().Presentation!.Audio!.DurationSamples == 48_000,
            "a completely cut music cue is absent for continuous playback, seek and offline mixing without changing its source");
    }

    private static void TestNativeDecisionAdapter()
    {
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureReset();
        var ui = Witch.UI.UIManager.Instance!.Ui!;
        void Poll(long ticks)
        {
            AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureClock(ticks);
            AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.ObserveDecisionReadiness();
        }
        int Count(string type) => AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureDocument
            .PresentationEvents.Count(item => item.EventType == type);
        ui.animationQueue.Enqueue(new object()); Poll(100);
        Assert(Count(ReplayEventTypesV17.InputWaitStarted) == 0, "queued native animation prevents a false player wait");
        ui.animationQueue.Clear();
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureSetMotion(true); Poll(200);
        Assert(Count(ReplayEventTypesV17.InputWaitStarted) == 0, "automatic hand motion must finish before ordinary input wait");
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureSetMotion(false); Poll(300);
        Assert(Count(ReplayEventTypesV17.InputWaitStarted) == 1, "native ready input opens the wait once queues and motion drain");
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureAcceptedCard(false);
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureAcceptedCard(true);
        Assert(Count(ReplayEventTypesV17.DecisionCommitted) == 1, "nested auto-use is a consequence, not another player decision");
        Witch.UI.Window.FightUI.InIEn = true; CardItem.canUse = false;
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureSetSource(false);
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.ObserveSelectionOpened(ui);
        Poll(500);
        Assert(Count(ReplayEventTypesV17.InputWaitStarted) == 2, "selection wait works inside an unfinished source transaction");
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.ObserveSelectionConfirmed(ui);
        Assert(Count(ReplayEventTypesV17.DecisionCommitted) == 1, "rejected native confirmation is not recorded as a decision");
        ui.FixtureConfirm(true);
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.ObserveSelectionConfirmed(ui);
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.ObserveSelectionConfirmed(ui);
        Assert(Count(ReplayEventTypesV17.DecisionCommitted) == 2, "accepted native confirmation closes the nested wait exactly once");
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.ObserveSelectionReset();
        Witch.UI.Window.FightUI.InIEn = false; CardItem.canUse = true;
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureSetSource(true); Poll(1000);
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.ObserveEndTurn(ui);
        Assert(Count(ReplayEventTypesV17.DecisionCommitted) == 3, "accepted end-turn is a decision boundary");
        AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureTerminal(); Poll(2000);
        Assert(Count(ReplayEventTypesV17.InputWaitStarted) == 3
            && AuraToolsExp.Dll.Features.MatchRecords.Recording.MatchReplayRecorder.FixtureFailures.Count == 0,
            "settlement never opens another input wait and adapter emits no capture failures");
    }

    private static void TestProtectedDecisionExecution()
    {
        var source = BuildDecisionReplay(20_000_000).Document;
        // Presentation can arrive late with its original observation time. Put
        // a real moving actor and overlapping hit cue inside a witnessed wait.
        var actor = new ReplayJournalEventV17
        {
            Lane = ReplayJournalLanesV17.Presentation, Sequence = 100_001,
            EventType = ReplayEventTypesV17.ActorAnimationPresented, TimeTicks = 5_000_000,
            Presentation = new ReplayPresentationMessageV17
            {
                DurationTicks = 2_000_000,
                WorldTransformSamples = new List<ReplayWorldTransformSampleV17>
                {
                    new() { OffsetTicks = 0, WorldPosition = new() { X = 0 } },
                    new() { OffsetTicks = 1_000_000, WorldPosition = new() { X = 65536 } },
                    new() { OffsetTicks = 2_000_000, WorldPosition = new() { X = 0 } }
                }
            }
        };
        source.PresentationEvents.Add(actor);
        source.PresentationEvents.Add(new ReplayJournalEventV17
        {
            Lane = ReplayJournalLanesV17.Presentation, Sequence = 100_002,
            EventType = ReplayEventTypesV17.CardMotionPresented, TimeTicks = 6_000_000,
            Presentation = new ReplayPresentationMessageV17
            {
                Kind = "NativeCardDiscard", DurationTicks = 2_000_000,
                TransformSamples = new List<ReplayTransformSampleV17>
                {
                    new() { OffsetTicks = 0 }, new() { OffsetTicks = 2_000_000 }
                }
            }
        });
        var plan = new ReplayDecisionTimelineV17(source);
        var projectedActor = plan.Document.PresentationEvents.Single(item => item.Sequence == actor.Sequence);
        var projectedCard = plan.Document.PresentationEvents.Single(item => item.Sequence == 100_002);
        Assert(projectedActor.Presentation!.DurationTicks == 2_000_000
            && projectedCard.Presentation!.DurationTicks == 2_000_000
            && projectedCard.TimeTicks - projectedActor.TimeTicks == 1_000_000
            && projectedActor.Presentation.WorldTransformSamples.Select(sample => sample.OffsetTicks).SequenceEqual(new long[] { 0, 1_000_000, 2_000_000 }),
            "late observed execution and overlapping tracks retain all internal durations and offsets");
        var firstCommit = plan.Document.PresentationEvents.First(item => item.EventType == ReplayEventTypesV17.DecisionCommitted);
        Assert(firstCommit.TimeTicks - (projectedCard.TimeTicks + projectedCard.Presentation!.DurationTicks)
            == ReplayDecisionTimelineV17.DecisionPauseTicks,
            "one readability pause follows the required execution instead of one pause per low-level effect");
        var last = -1L;
        var monotonic = true;
        for (var ticks = 0L; ticks <= 44_000_000; ticks += 12_345)
        {
            var mapped = plan.ToPlayback(ticks);
            monotonic &= mapped >= last;
            last = mapped;
        }
        Assert(monotonic, "viewing map stays monotonic through interrupted and protected intervals");
        TestProjectedAudioMix();
    }

    private static void TestProjectedAudioMix()
    {
        var source = BuildDecisionReplay(20_000_000).Document;
        source.PresentationEvents.Single(item => item.Presentation?.Audio?.Bus == "Effect").Presentation!.Audio!.AssetSha256 = new string('a', 64);
        var plan = new ReplayDecisionTimelineV17(source);
        var root = Path.Combine(Path.GetTempPath(), "AuraReplayDecisionAudio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clip = Path.Combine(root, "clip.wav");
            var mixed = Path.Combine(root, "mixed.wav");
            WritePcmWave(clip, 48_000, 1, 24_000, 4096);
            var frames = (long)Math.Ceiling(plan.DurationTicks * 30d / ReplayProtocolV17.TimebaseTicksPerSecond) + 1;
            AuraToolsExp.Dll.Features.MatchRecords.Media.ReplayOfflineAudioMixer.MixToWave(plan.Document, frames, 30, _ => clip, mixed);
            var bytes = File.ReadAllBytes(mixed);
            var firstSound = plan.Document.PresentationEvents.Single(item => item.Presentation?.Audio?.Bus == "Effect").Presentation!.Audio!.StartSample;
            Assert(BitConverter.ToInt16(bytes, (int)(44 + (firstSound - 1) * 4)) == 0
                && BitConverter.ToInt16(bytes, (int)(44 + firstSound * 4)) > 0,
                "exported PCM becomes audible on the same projected frame as the interactive action");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestDecisionCaptureOwnership()
    {
        var capture = new ReplayDecisionCaptureV17();
        var events = new List<(string Type, string Kind, string Id, long Time)>();
        void Emit(string type, string kind, string id, long ticks) => events.Add((type, kind, id, ticks));
        capture.Observe(ReplayDecisionTimelineV17.Player, 100, Emit);
        // Hover, drag, cancel and invalid confirmations do not call Commit.
        for (var frame = 0; frame < 100; frame++) capture.Observe(ReplayDecisionTimelineV17.Player, 200 + frame, Emit);
        Assert(events.Count == 1 && capture.IsWaiting, "uncommitted input leaves one wait open without recording fake actions");
        capture.Commit("Card", "immune-hit", 1000, Emit);
        Assert(events.Count == 3 && events[1].Kind == ReplayDecisionTimelineV17.Committed
            && events[2].Type == ReplayEventTypesV17.DecisionCommitted && !capture.IsWaiting,
            "accepted cards count regardless of damage result, and close the exact wait before their effects");
        capture.Observe(ReplayDecisionTimelineV17.Selection, 2000, Emit);
        capture.Close(ReplayDecisionTimelineV17.Interrupted, 2500, Emit);
        capture.Observe(ReplayDecisionTimelineV17.Selection, 3000, Emit);
        capture.Commit(ReplayDecisionTimelineV17.Selection, "selection-1", 4000, Emit);
        capture.Observe(ReplayDecisionTimelineV17.Player, 5000, Emit);
        capture.Close(ReplayDecisionTimelineV17.Terminal, 6000, Emit);
        var count = events.Count;
        capture.Close(ReplayDecisionTimelineV17.Terminal, 7000, Emit);
        Assert(events.Count == count && !capture.IsWaiting, "terminal and duplicate cleanup leave no pending decision wait");
        capture.Reset();
        capture.Observe(ReplayDecisionTimelineV17.Player, 0, Emit);
        Assert(capture.IsWaiting && events.Last().Id == "input-wait-00000001", "next battle gets fresh wait identities");
    }

    private static ReplayDocumentEnvelopeV17 BuildDecisionReplay(long wait)
    {
        var seed = BuildReplayV17().Document;
        var writer = new ReplayJournalBuilderV17(seed.Header, seed.InitialState);
        writer.Document.Presentation = seed.Presentation;
        writer.Document.Assets = seed.Assets;
        writer.Document.Presentation.Ui.DecisionTimingContract = ReplayDecisionTimelineV17.Contract;
        var bootstrap = writer.StartTransaction(ReplayTransactionKindsV17.Bootstrap, 0, 1, 1, "player-entity");
        writer.AddTruthMarker(bootstrap, ReplayEventTypesV17.BattleMaterialized, 0, "player-entity");
        AddEntityPresentation(writer, bootstrap, seed.InitialState.Entities[0], -4f, true, 0);
        AddEntityPresentation(writer, bootstrap, seed.InitialState.Entities[1], 3f, false, 0, "player-entity");
        writer.AddTruthMarker(bootstrap, ReplayEventTypesV17.FightStartSignaled, 0, "player-entity");
        writer.AddTruthMarker(bootstrap, ReplayEventTypesV17.RoundStarted, 0, "player-entity");
        var end = 4_000_000 + wait * 2;
        writer.AddPresentation(bootstrap, ReplayEventTypesV17.AudioPresented, new ReplayPresentationMessageV17
        {
            Audio = new ReplayAudioCueV17 { Bus = "Bgm", ResourcePath = "Audio/Battle", StartSample = 0,
                DurationSamples = end * 48_000 / ReplayProtocolV17.TimebaseTicksPerSecond, LoopEndSample = 48_000 }
        }, 0);
        writer.CompleteTransaction(bootstrap, 0);
        var capture = new ReplayDecisionCaptureV17();
        void Emit(string type, string kind, string id, long ticks)
        {
            var tx = writer.StartTransaction(ReplayTransactionKindsV17.SystemPhase, ticks, 1, 1);
            writer.AddPresentation(tx, type, new ReplayPresentationMessageV17 { Kind = kind, SourceInstanceId = id }, ticks);
            writer.CompleteTransaction(tx, ticks);
        }
        capture.Observe(ReplayDecisionTimelineV17.Player, 1_000_000, Emit);
        capture.Commit("Card", "card-instance-1", 1_000_000 + wait, Emit);
        var action = writer.StartTransaction(ReplayTransactionKindsV17.Passive, 1_000_000 + wait, 1, 1, "player-entity");
        writer.AddPresentation(action, ReplayEventTypesV17.AudioPresented, new ReplayPresentationMessageV17
        {
            Audio = new ReplayAudioCueV17 { Bus = "Effect", ResourcePath = "Audio/Hit",
                StartSample = (1_000_000 + wait) * 48_000 / ReplayProtocolV17.TimebaseTicksPerSecond, DurationSamples = 24_000 }
        }, 1_000_000 + wait);
        capture.Observe(ReplayDecisionTimelineV17.Selection, 2_000_000 + wait, Emit);
        capture.Commit(ReplayDecisionTimelineV17.Selection, "choose-burn", 2_000_000 + wait * 2, Emit);
        var changed = writer.CurrentState;
        changed.Entities.Single(item => item.EntityId == "enemy-entity").CurrentHp -= 10;
        writer.ApplyObservedState(action, changed, 2_000_000 + wait * 2);
        writer.CompleteTransaction(action, 3_000_000 + wait * 2);
        Emit(ReplayEventTypesV17.DecisionCommitted, "EndTurn", "end-turn-1", 3_000_000 + wait * 2);
        var outcome = writer.StartTransaction(ReplayTransactionKindsV17.Outcome, end, 1, 1);
        var final = writer.CurrentState;
        final.BattlePhase = "Finalized";
        final.Outcome = "Win";
        writer.ApplyObservedState(outcome, final, end);
        writer.AddTruthMarker(outcome, ReplayEventTypesV17.OutcomeEntering, end);
        writer.AddTruthMarker(outcome, ReplayEventTypesV17.BattleFinalized, end);
        writer.CompleteTransaction(outcome, end);
        return new ReplayDocumentEnvelopeV17 { Document = writer.Document };
    }
}
