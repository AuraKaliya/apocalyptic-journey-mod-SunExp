using AuraReplay.Presentation.Shared;

internal static partial class CoreTestSuite
{
    internal static void TestReplayPresentationRuntime()
    {
        TestReplayPresentationCompatibility();
        AuraReplayPresentationRuntime.ClearOwner("OwnerReplay");
        using var module = AuraReplayPresentationRuntime.Register(new ReplayModule());
        var descriptors = AuraReplayPresentationRuntime.SnapshotModules();
        Assert(descriptors.Count == 1
               && descriptors[0].OwnerModId == "OwnerReplay"
               && descriptors[0].TypeId == "SpiritPresentation"
               && descriptors[0].Portability == AuraReplayPresentationPortability.Portable,
            "replay presentation modules are owner-qualified and expose an explicit portability contract");

        var captured = new List<AuraReplayCapturedPresentationEvent>();
        using (var capture = AuraReplayPresentationRuntime.BeginCapture("battle-1", captured.Add))
        {
            var published = AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
            {
                EventId = "event-1",
                DuplicateKey = "spirit-1|focus|1",
                OwnerModId = "OwnerReplay",
                TypeId = "SpiritPresentation",
                SchemaVersion = 1,
                Kind = AuraReplayPresentationKinds.OwnerAttachedFocus,
                ActorEntityId = "spirit-1",
                OwnerEntityId = "player-1",
                TargetEntityIds = new List<string> { "enemy-1", "enemy-1" },
                PayloadJson = "{\"z\":{\"b\":2,\"a\":1},\"list\":[{\"y\":2,\"x\":1},3],\"a\":0}",
                DurationMicroseconds = 400_000
            });
            Assert(published == AuraReplayPresentationPublishResult.Published
                   && captured.Count == 1
                   && captured[0].BattleSessionId == "battle-1"
                   && captured[0].CaptureSequence == 1
                   && captured[0].Event.TargetEntityIds.SequenceEqual(new[] { "enemy-1" })
                   && captured[0].Event.PayloadJson
                       == "{\"a\":0,\"list\":[{\"x\":1,\"y\":2},3],\"z\":{\"a\":1,\"b\":2}}",
                "the active capture lease timestamps and recursively canonicalizes one data-only presentation event while preserving array order");
            Assert(AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
                   {
                       EventId = "invalid-duplicate-json",
                       OwnerModId = "OwnerReplay",
                       TypeId = "SpiritPresentation",
                       Kind = AuraReplayPresentationKinds.Overlay,
                       PayloadJson = "{\"a\":1,\"a\":2}"
                   }) == AuraReplayPresentationPublishResult.Invalid
                   && AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
                   {
                       EventId = "invalid-trailing-json",
                       OwnerModId = "OwnerReplay",
                       TypeId = "SpiritPresentation",
                       Kind = AuraReplayPresentationKinds.Overlay,
                       PayloadJson = "{}{}"
                   }) == AuraReplayPresentationPublishResult.Invalid
                   && AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
                   {
                       EventId = "invalid-deep-json",
                       OwnerModId = "OwnerReplay",
                       TypeId = "SpiritPresentation",
                       Kind = AuraReplayPresentationKinds.Overlay,
                       PayloadJson = new string('[', 65) + "0" + new string(']', 65)
                   }) == AuraReplayPresentationPublishResult.Invalid
                   && AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
                   {
                       EventId = "invalid-large-json",
                       OwnerModId = "OwnerReplay",
                       TypeId = "SpiritPresentation",
                       Kind = AuraReplayPresentationKinds.Overlay,
                       PayloadJson = "{\"value\":\"" + new string('x', AuraReplayPresentationProtocol.MaximumPayloadCharacters) + "\"}"
                   }) == AuraReplayPresentationPublishResult.Invalid
                   && captured.Count == 1,
                "the shared replay boundary rejects duplicate properties, trailing content, excessive depth, and oversized payloads before capture");
            Assert(AuraReplayPresentationRuntime.Publish(captured[0].Event)
                   == AuraReplayPresentationPublishResult.Duplicate,
                "presentation event identity is duplicate-safe within one battle capture");

            var secondCaptureRejected = false;
            try { AuraReplayPresentationRuntime.BeginCapture("battle-2", _ => { }); }
            catch (InvalidOperationException) { secondCaptureRejected = true; }
            Assert(secondCaptureRejected,
                "one shared presentation capture owner exists per process");
        }
        Assert(!AuraReplayPresentationRuntime.HasActiveCapture
               && AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
               {
                   EventId = "event-2",
                   OwnerModId = "OwnerReplay",
                   TypeId = "SpiritPresentation",
                   Kind = AuraReplayPresentationKinds.VisibilityChanged
               }) == AuraReplayPresentationPublishResult.NoCaptureSession,
            "capture lease disposal closes the transient presentation producer boundary");

        module.Dispose();
        Assert(AuraReplayPresentationRuntime.SnapshotModules().Count == 0,
            "presentation module leases cleanly unregister their exact generation");

        var rendererModule = new ReplayRendererModule();
        using var rendererLease = AuraReplayPresentationRuntime.Register(rendererModule);
        using var renderer = AuraReplayPresentationRuntime.CreateRenderer(
            "OwnerReplay",
            "RendererPresentation",
            2,
            new AuraReplayPresentationRenderContext())
            ?? throw new InvalidOperationException("renderer module was not resolved");
        renderer.Apply(new AuraReplayPresentationEvent { Kind = AuraReplayPresentationKinds.Overlay }, 10);
        renderer.Tick(20);
        renderer.Reset();
        Assert(rendererModule.Renderer.Applied == 1
               && rendererModule.Renderer.LastTick == 20
               && rendererModule.Renderer.ResetCount == 1,
            "provider-qualified replay renderers are resolved through the shared interface and receive the manual replay clock");
    }

    private static void TestReplayPresentationCompatibility()
    {
        var original = new ReplayRendererModule().Descriptor;
        var rebuilt = new ReplayRendererModule().Descriptor;
        original.BuildIdentity = "original-build";
        rebuilt.BuildIdentity = "unrelated-code-rebuilt";
        Assert(rebuilt.MatchesContract(original),
            "module compatibility uses its schema and capability, not the enclosing assembly build");
        Assert(!rebuilt.MatchesContract(null),
            "a missing required contract is never compatible");
        rebuilt.SchemaVersion++;
        Assert(!rebuilt.MatchesContract(original),
            "a changed event schema remains incompatible after build provenance is separated");
        rebuilt.SchemaVersion = original.SchemaVersion;
        rebuilt.RendererCapability = "tests.renderer.v2";
        Assert(!rebuilt.MatchesContract(original),
            "a changed renderer contract remains incompatible");
        rebuilt.RendererCapability = "";
        original.RendererCapability = "";
        Assert(!rebuilt.MatchesContract(original),
            "provider-required modules cannot use two empty capabilities to claim compatibility");
    }

    private sealed class ReplayModule : IAuraReplayPresentationModule
    {
        public AuraReplayPresentationModuleDescriptor Descriptor { get; } = new()
        {
            OwnerModId = "OwnerReplay",
            TypeId = "SpiritPresentation",
            SchemaVersion = 1,
            Portability = AuraReplayPresentationPortability.Portable,
            BuildIdentity = "tests"
        };
    }

    private sealed class ReplayRendererModule : IAuraReplayPresentationRendererModule
    {
        internal FakeRenderer Renderer { get; } = new();

        public AuraReplayPresentationModuleDescriptor Descriptor { get; } = new()
        {
            OwnerModId = "OwnerReplay",
            TypeId = "RendererPresentation",
            SchemaVersion = 2,
            Portability = AuraReplayPresentationPortability.ProviderRequired,
            BuildIdentity = "tests",
            RendererCapability = "tests.renderer.v1"
        };

        public IAuraReplayPresentationRenderer CreateRenderer(AuraReplayPresentationRenderContext context) => Renderer;
    }

    private sealed class FakeRenderer : IAuraReplayPresentationRenderer
    {
        internal int Applied { get; private set; }
        internal long LastTick { get; private set; }
        internal int ResetCount { get; private set; }

        public void Apply(AuraReplayPresentationEvent value, long logicalMicroseconds) => Applied++;
        public void Tick(long logicalMicroseconds) => LastTick = logicalMicroseconds;
        public void Reset() => ResetCount++;
        public void Dispose() { }
    }
}
