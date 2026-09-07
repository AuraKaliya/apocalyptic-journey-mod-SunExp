using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;

internal static partial class AuraToolsTestSuite
{
    internal static void TestReplayCaptureIteration()
    {
        var fixture = BuildReplayV17();
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(fixture).IsValid, "the deferred-sealing reference fixture is sealed");
        var original = ReplayPayloadV17.Encode(fixture);
        foreach (var value in fixture.Document.TruthEvents.Concat(fixture.Document.PresentationEvents))
        {
            value.EventHash = value.PreviousLaneEventHash = value.StateHashBefore = value.StateHashAfter = "";
        }
        var resealed = ReplayDocumentFinalizerV17.FinalizeAndValidate(fixture);
        Assert(resealed.IsValid, "deferred sealing validates: " + resealed.Message);
        Assert(original.SequenceEqual(ReplayPayloadV17.Encode(fixture)),
            "deferred hash sealing reconstructs the identical complete document and roots");
        var binding = fixture.Document.PresentationEvents.First(item => item.Presentation?.EntityBinding != null).Presentation!.EntityBinding!;
        Assert(!System.Text.Encoding.UTF8.GetString(ReplayCanonicalJsonV17.SerializeUtf8(binding)).Contains("AttachmentBounds"),
            "absent new geometry does not change a historical binding's canonical bytes");
        binding.AttachmentBounds = new ReplayBoundsQ16V17
        {
            Center = new ReplayVector3Q16V17 { Y = 65_536 },
            Size = new ReplayVector3Q16V17 { X = 65_536, Y = 131_072, Z = 65_536 }
        };
        var copied = ReplayFastCloneV17.Binding(binding);
        binding.AttachmentBounds.Center.Y++;
        Assert(copied.AttachmentBounds!.Center.Y == 65_536,
            "durability and checkpoint clones own independent measured attachment geometry");
        fixture.Document.PresentationEvents.First(item => item.EventType == ReplayEventTypesV17.CardMotionPresented)
            .Presentation!.VisualInstanceId = "native-view-1";
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(fixture).IsValid
               && fixture.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.MeasuredAttachmentBounds)
               && fixture.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.CardViewIdentity),
            "new measured geometry and physical card views declare their exact reader capabilities");
        fixture.Document.Header.RequiredCapabilities.Remove(ReplayCapabilitiesV17.CardViewIdentity);
        Assert(ReplayDocumentValidatorV17.Validate(fixture).Errors.Contains("required-capability-invalid"),
            "new visual identity data cannot masquerade as a legacy-reader document");

        var journal = new ReplayJournalBuilderV17(fixture.Document.Header, fixture.Document.InitialState);
        var tx = journal.StartTransaction(ReplayTransactionKindsV17.Passive, 1, 1, 1);
        var observed = journal.CurrentState;
        observed.RoundSequence++;
        var prepared = journal.CreateObservedDiff(observed);
        journal.ApplyObservedState(tx, observed, 2, prepared);
        var staleRejected = false;
        try { journal.ApplyObservedState(tx, observed, 3, prepared); }
        catch (InvalidOperationException) { staleRejected = true; }
        Assert(staleRejected, "a prepared diff cannot overwrite a more recent state");
        Assert(journal.Document.TruthEvents.All(item => item.EventHash.Length == 0),
            "the live journal avoids provisional JSON hash work before durable batch sealing");
        Assert(journal.LastDurableSequence(new[] { tx }, Array.Empty<long>()) == 0,
            "the indexed durability watermark still holds every event behind its open transaction");
        journal.CompleteTransaction(tx, 4);
        Assert(journal.LastDurableSequence(Array.Empty<string>(), new[] { 2L }) == 1,
            "an unfinished presentation still blocks later durable events after transaction completion");

        var cards = new List<ReplayTransformSampleV17>();
        for (var i = 0; i <= 1000; i++)
            AssertTrack(ReplayTransformTrackV17.Append(cards, CardPose(i * 1000L, 0), 10));
        ReplayTransformTrackV17.Append(cards, CardPose(1_100_000, 100), 10);
        Assert(cards.Count == 3 && cards[1].OffsetTicks == 1_000_000 && cards[1].CanvasPosition.X == 0,
            "stationary card sampling is bounded while retaining the real start of the next movement");
        var actors = new List<ReplayWorldTransformSampleV17>();
        for (var i = 0; i < 100; i++)
            ReplayTransformTrackV17.Append(actors, new ReplayWorldTransformSampleV17 { OffsetTicks = i }, 10);
        ReplayTransformTrackV17.Append(actors, new ReplayWorldTransformSampleV17
            { OffsetTicks = 100, AttachmentBounds = copied.AttachmentBounds }, 10);
        Assert(actors.Count == 3 && actors[1].OffsetTicks == 99 && actors[2].AttachmentBounds != null,
            "actor hold endpoints and geometry changes survive track compression");
    }

    private static void AssertTrack(bool accepted)
    {
        if (!accepted) throw new InvalidOperationException("A stationary trajectory exceeded its bounded storage.");
    }
    private static ReplayTransformSampleV17 CardPose(long time, int x) => new()
        { OffsetTicks = time, CanvasPosition = new ReplayVector2Q16V17 { X = x } };
}
