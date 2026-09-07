using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static partial class AuraToolsTestSuite
{
    internal static void TestReplayHandLifecycle()
    {
        var document = new ReplayDocumentV17();
        document.Presentation.Ui.HandPresentationContract = ReplayHandLifecycleContractV17.Contract;
        document.TruthEvents.Add(HandChange(2, 100, "A", true));
        var errors = new List<string>();
        ReplayHandLifecycleContractV17.Validate(document, errors);
        Assert(errors.Any(error => error.StartsWith("hand-arrival-presentation-missing:")), "a new hand card cannot silently appear in a later snapshot");
        document.PresentationEvents.Add(Motion(1, 90, "A", "Draw"));
        errors.Clear(); ReplayHandLifecycleContractV17.Validate(document, errors);
        Assert(errors.Count == 0, "an observed native birth precedes hand registration");
        document.TruthEvents.Add(HandChange(3, 200, "A", false));
        document.TruthEvents.Add(HandChange(5, 300, "A", true));
        errors.Clear(); ReplayHandLifecycleContractV17.Validate(document, errors);
        Assert(errors.Any(error => error.StartsWith("hand-arrival-presentation-missing:")), "redrawing the same logical card requires a new arrival witness");
        document.PresentationEvents.Add(Motion(4, 290, "A", "Draw"));
        errors.Clear(); ReplayHandLifecycleContractV17.Validate(document, errors);
        Assert(errors.Count == 0, "a second physical view supports a later hand entry");
        document.PresentationEvents.Add(Motion(6, 400, "instant", "Draw"));
        document.PresentationEvents.Add(Motion(7, 401, "instant", "Hand"));
        errors.Clear(); ReplayHandLifecycleContractV17.Validate(document, errors);
        Assert(errors.Count == 0, "a card consumed during DrawScript can have an observed birth without a stable hand slot");
        document.PresentationEvents.Add(Motion(8, 500, "missing", "Hand"));
        errors.Clear(); ReplayHandLifecycleContractV17.Validate(document, errors);
        Assert(errors.Any(error => error.StartsWith("hand-interaction-before-appearance:")), "using a card that never appeared is not accepted as a complete hand recording");
        document.Presentation.Ui.HandPresentationContract = null;
        errors.Clear(); ReplayHandLifecycleContractV17.Validate(document, errors);
        Assert(errors.Count == 0, "legacy records remain readable without pretending that their missing arrivals were recorded");
        var fixture = BuildReplayV17();
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(fixture).IsValid, "legacy hand fixture seals normally");
        var originalRoot = fixture.DeclaredDocumentRoot;
        fixture.Document.Presentation.Ui.HandPresentationContract = ReplayHandLifecycleContractV17.Contract;
        var upgraded = ReplayDocumentFinalizerV17.FinalizeAndValidate(fixture);
        Assert(fixture.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.HandLifecycle),
            "a new hand recording declares coverage even when no arrival was observed");
        fixture.Document.Presentation.Ui.HandPresentationContract = null;
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(fixture).IsValid && fixture.DeclaredDocumentRoot == originalRoot,
            "an absent hand contract preserves existing document roots");
    }

    private static ReplayJournalEventV17 HandChange(long sequence, long time, string id, bool add) => new()
    {
        Sequence = sequence, TimeTicks = time, EventType = ReplayEventTypesV17.StateDeltaApplied,
        Delta = new ReplayStateDeltaV17 { Operations = new List<ReplayStateOperationV17> { new()
        {
            Kind = add ? ReplayStateOperationKindsV17.AddVisibleCard : ReplayStateOperationKindsV17.RemoveVisibleCard,
            CardInstanceId = id, Card = add ? new ReplayVisibleCardStateV17 { CardInstanceId = id, Zone = "Hand" } : null
        } } }
    };
    private static ReplayJournalEventV17 Motion(long sequence, long time, string id, string kind) => new()
    {
        Sequence = sequence, TimeTicks = time, EventType = ReplayEventTypesV17.CardMotionPresented,
        Presentation = new ReplayPresentationMessageV17 { Kind = kind, SourceInstanceId = id, VisualInstanceId = "view:" + sequence }
    };
}
