using System.Globalization;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Runtime;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayRuntimeCore()
    {
        var document = ReplayV10Document("core-replay");
        var finalized = ReplayDocumentFinalizerV10.FinalizeAndValidate(document);
        Assert(finalized.IsValid
               && document.Header.DocumentVersion == 10
               && document.Header.MinimumReadableDocumentVersion == 10,
            "Replay Document v10 finalizes as the only readable protocol");
        Assert(document.Checkpoints.First().EventSequence == 0
               && document.Checkpoints.Last().EventSequence == document.Events.Last().Sequence,
            "v10 finalization always emits initial and final checkpoints");

        var engine = new ReplayProjectionEngine();
        engine.Reset(document.InitialState);
        foreach (var value in document.Events) engine.Apply(value);
        Assert(ReplayProjectionStateV10.Hash(engine.Current) == document.Header.FinalLogicalStateSha256
               && engine.Current.Actors.Single().CurrentHp == 13
               && engine.Current.Cards.All(item => item.InstanceId != "card-a-instance"),
            "pure v10 projection reaches authoritative after-values without combat execution");

        var controller = new ReplayTimelineController(document);
        controller.SeekSequence(document.Events.Last().Sequence);
        var finalHash = ReplayProjectionStateV10.Hash(controller.State);
        controller.SeekTime(0);
        controller.SeekSequence(document.Events.Last().Sequence);
        Assert(ReplayProjectionStateV10.Hash(controller.State) == finalHash,
            "checkpoint seek is idempotent and does not accumulate transient state");

        var chunks = ReplayTimelineChunkerV10.Build(document.Events, ReplayTimelineChunkerV10.MinimumTargetBytes);
        var decoded = ReplayTimelineChunkerV10.Decode(chunks);
        Assert(decoded.Count == document.Events.Count
               && decoded.Last().EventChainHashAfter == document.Header.FinalEventChainSha256,
            "v10 timeline chunks preserve the complete verified event chain");

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var french = ReplayProjectionStateV10.Hash(document.InitialState);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            var chinese = ReplayProjectionStateV10.Hash(document.InitialState);
            Assert(french == chinese, "canonical v10 state hashing is culture invariant");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        var tampered = ReplayV10Document("tampered");
        ReplayDocumentFinalizerV10.FinalizeAndValidate(tampered);
        tampered.Events[1].Delta!.ActorUpserts[0].CurrentHp--;
        Assert(!ReplayDocumentValidatorV10.Validate(tampered).IsValid,
            "tampering with one authoritative event invalidates state, chain, and document hashes");

        var stable = new MatchReplayActionConvergenceTracker();
        Assert(stable.Observe("same") == MatchReplayActionFinalizationDecision.Observe
               && stable.Observe("same") == MatchReplayActionFinalizationDecision.Observe
               && stable.Observe("same") == MatchReplayActionFinalizationDecision.FinalizeStable,
            "capture finalization waits for repeated authoritative state convergence");
    }

    private static ReplayDocumentV10 ReplayV10Document(string recordId)
    {
        var roleRef = new ReplayContentRefV10
        {
            OwnerModId = "Witch",
            ContentKind = "Role",
            StableContentId = "role-test"
        };
        var cardRef = new ReplayContentRefV10
        {
            OwnerModId = "Witch",
            ContentKind = "Card",
            StableContentId = "card-a"
        };
        var initial = new ReplayLogicalStateV10
        {
            LevelId = "level-test",
            TurnIndex = 1,
            ActiveActorId = "role-instance",
            PlayerPower = 3,
            PlayerMaxPower = 3,
            Actors = new List<ReplayActorStateV10>
            {
                new()
                {
                    InstanceId = "role-instance",
                    Content = roleRef,
                    EntityKind = ReplayEntityKindsV10.Player,
                    Team = ReplayTeamsV10.Friendly,
                    MaxHp = 20,
                    CurrentHp = 20
                }
            },
            Cards = new List<ReplayCardStateV10>
            {
                new()
                {
                    InstanceId = "card-a-instance",
                    Content = cardRef,
                    Zone = "Hand",
                    Order = 0,
                    DisplayedCost = 1
                }
            }
        };
        var after = ReplayProjectionStateV10.Clone(initial);
        after.PlayerPower = 2;
        after.Actors[0].CurrentHp = 13;
        after.Cards.Clear();
        var document = new ReplayDocumentV10
        {
            Header = new ReplayDocumentHeaderV10
            {
                RecordId = recordId,
                SessionId = recordId,
                LevelId = initial.LevelId,
                StartedUtc = "2026-08-20T00:00:00Z",
                EndedUtc = "2026-08-20T00:01:00Z",
                Result = "Win",
                GameBuild = "game",
                ToolBuild = "tool",
                RendererBuild = "tool"
            },
            Content = new ReplayContentManifestV10
            {
                Dependencies = new List<ReplayContentDependencyV10>
                {
                    new() { OwnerModId = "Witch", Version = "test", ManifestSha256 = new string('a', 64) }
                },
                Definitions = new List<ReplayContentDefinitionV10>
                {
                    new() { Content = roleRef, Display = new ReplayDisplaySnapshotV10 { Name = "Test Role" } },
                    new() { Content = cardRef, Display = new ReplayDisplaySnapshotV10 { Name = "Test Card" } }
                }
            },
            InitialState = initial,
            Events = new List<ReplayTimelineEventV10>
            {
                new()
                {
                    Sequence = 1,
                    TimeTicks = 0,
                    TurnIndex = 1,
                    EventId = "event-00000001",
                    ActionId = "action-000001",
                    EventType = ReplayEventTypesV10.ActionStarted,
                    ActorId = "role-instance",
                    SourceInstanceId = "card-a-instance"
                },
                new()
                {
                    Sequence = 2,
                    TimeTicks = 600_000,
                    TurnIndex = 1,
                    EventId = "event-00000002",
                    ActionId = "action-000001",
                    CauseEventId = "event-00000001",
                    EventType = ReplayEventTypesV10.ActionCompleted,
                    ActorId = "role-instance",
                    SourceInstanceId = "card-a-instance",
                    Delta = ReplayProjectionStateV10.CreateDelta(initial, after),
                    Semantics = new List<ReplaySemanticEventV10>
                    {
                        new()
                        {
                            Kind = ReplaySemanticKindsV10.Damage,
                            Action = "HpDamage",
                            ActorId = "role-instance",
                            TargetId = "role-instance",
                            Value = 7,
                            SecondaryValue = 13,
                            Label = "HP"
                        }
                    },
                    Presentation = new List<ReplayPresentationCueV10>
                    {
                        new()
                        {
                            CueId = "event-00000002.hit",
                            Kind = ReplayPresentationKindsV10.Hit,
                            DurationTicks = 480_000,
                            TargetIds = new List<string> { "role-instance" },
                            Value = 7
                        }
                    }
                },
                new()
                {
                    Sequence = 3,
                    TimeTicks = 1_200_000,
                    TurnIndex = 1,
                    EventId = "event-00000003",
                    EventType = ReplayEventTypesV10.BattleCompleted,
                    ActorId = "role-instance"
                }
            },
            Checkpoints = new List<ReplayCheckpointV10> { new() { EventSequence = 2 } }
        };
        return document;
    }
}
