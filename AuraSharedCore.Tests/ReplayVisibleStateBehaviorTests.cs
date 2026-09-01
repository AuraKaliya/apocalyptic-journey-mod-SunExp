using AuraReplay.VisibleState.Shared;

internal static partial class CoreTestSuite
{
    internal static void TestReplayVisibleStateRuntime()
    {
        AuraReplayVisibleStateRuntime.ClearOwner("OwnerA");
        var provider = new VisibleProvider("OwnerA", "Spirit", 2);
        using var lease = AuraReplayVisibleStateRuntime.Register(provider);
        Assert(AuraReplayVisibleStateRuntime.Snapshot().SequenceEqual(new[] { provider }),
            "visible replay providers are owner-qualified and deterministically ordered");
        var duplicateRejected = false;
        try { AuraReplayVisibleStateRuntime.Register(new VisibleProvider("OwnerA", "Spirit", 2)); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        Assert(duplicateRejected,
            "one owner/type pair has exactly one visible replay provider");
        var items = provider.Capture(new AuraReplayVisibleCaptureContext
        {
            RecordId = "record",
            PerspectivePlayerId = "player"
        });
        Assert(items.Single().InstanceId == "spirit-1"
               && items.Single().PayloadJson == "{\"level\":3}",
            "the shared contract carries only owner-produced visible data and no Unity/native restore objects");
        lease.Dispose();
        Assert(AuraReplayVisibleStateRuntime.Snapshot().Count == 0,
            "disposing an owner lease removes its provider without global side effects");

        AuraReplayEntityPresentationRuntime.ClearOwner("OwnerA");
        var entityProvider = new EntityPresentationProvider();
        using var entityLease = AuraReplayEntityPresentationRuntime.Register(entityProvider);
        var presentation = AuraReplayEntityPresentationRuntime.Snapshot().Single()
            .Capture(new AuraReplayVisibleCaptureContext()).Single();
        Assert(presentation.EntityId == "spirit-status"
               && presentation.OwnerEntityId == "player-status"
               && presentation.PresentationMode == AuraReplayEntityPresentationModes.OwnerAttachedProxy
               && presentation.HudMode == AuraReplayEntityHudModes.DetachedRightVertical,
            "cross-mod replay entity presentation is a typed pure-data contract with explicit owner anchoring");
        entityLease.Dispose();
        Assert(AuraReplayEntityPresentationRuntime.Snapshot().Count == 0,
            "disposing an entity-presentation lease removes the owner provider deterministically");
    }

    private sealed class VisibleProvider : IAuraReplayVisibleStateProvider
    {
        internal VisibleProvider(string owner, string type, int schema)
        {
            OwnerModId = owner;
            TypeId = type;
            SchemaVersion = schema;
        }

        public string OwnerModId { get; }
        public string TypeId { get; }
        public int SchemaVersion { get; }

        public IReadOnlyList<AuraReplayVisibleStateItem> Capture(AuraReplayVisibleCaptureContext context) =>
            new[]
            {
                new AuraReplayVisibleStateItem
                {
                    InstanceId = "spirit-1",
                    DisplayText = "Spirit Lv.3",
                    PayloadJson = "{\"level\":3}"
                }
            };
    }

    private sealed class EntityPresentationProvider : IAuraReplayEntityPresentationProvider
    {
        public string OwnerModId => "OwnerA";
        public int SchemaVersion => 1;

        public IReadOnlyList<AuraReplayEntityPresentationItem> Capture(AuraReplayVisibleCaptureContext context) =>
            new[]
            {
                new AuraReplayEntityPresentationItem
                {
                    EntityId = "spirit-status",
                    OwnerEntityId = "player-status",
                    PresentationMode = AuraReplayEntityPresentationModes.OwnerAttachedProxy,
                    HudMode = AuraReplayEntityHudModes.DetachedRightVertical,
                    ReferenceHeightPixels = 120,
                    HorizontalOverlapQ16 = 21_845
                }
            };
    }
}
