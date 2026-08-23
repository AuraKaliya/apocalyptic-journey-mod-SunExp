using System.Globalization;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayRuntimeCore()
    {
        var document = ReplayV11Document("core-replay");
        var finalized = ReplayDocumentFinalizerV11.FinalizeAndValidate(document);
        Assert(finalized.IsValid
               && document.Header.DocumentVersion == 11
               && document.Header.MinimumReadableDocumentVersion == 11,
            "Replay Document v11 finalizes as the only readable native protocol");
        Assert(document.Checkpoints.First().EventSequence == 0
               && document.Checkpoints.Last().EventSequence == document.Events.Last().Sequence,
            "v11 finalization always emits initial and final checkpoints");

        var engine = new ReplayProjectionEngine();
        engine.Reset(document.InitialState);
        foreach (var value in document.Events) engine.Apply(value);
        Assert(ReplayProjectionStateV11.Hash(engine.Current) == document.Header.FinalLogicalStateSha256
               && engine.Current.Actors.Single().CurrentHp == 13
               && engine.Current.Cards.All(item => item.InstanceId != "card-a-instance"),
            "pure v11 projection reaches authoritative after-values without combat execution");

        var checkpoint = document.Checkpoints.First(value => value.EventSequence == 0);
        engine.Reset(checkpoint.State);
        foreach (var value in document.Events) engine.Apply(value);
        var finalHash = ReplayProjectionStateV11.Hash(engine.Current);
        engine.Reset(checkpoint.State);
        foreach (var value in document.Events) engine.Apply(value);
        Assert(ReplayProjectionStateV11.Hash(engine.Current) == finalHash,
            "checkpoint seek is idempotent and does not accumulate transient state");

        var chunks = ReplayTimelineChunkerV11.Build(document.Events, ReplayTimelineChunkerV11.MinimumTargetBytes);
        var decoded = ReplayTimelineChunkerV11.Decode(chunks);
        Assert(decoded.Count == document.Events.Count
               && decoded.Last().EventChainHashAfter == document.Header.FinalEventChainSha256,
            "v11 timeline chunks preserve the complete verified event chain");

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var french = ReplayProjectionStateV11.Hash(document.InitialState);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            var chinese = ReplayProjectionStateV11.Hash(document.InitialState);
            Assert(french == chinese, "canonical v11 state hashing is culture invariant");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        var tampered = ReplayV11Document("tampered");
        ReplayDocumentFinalizerV11.FinalizeAndValidate(tampered);
        tampered.Events[1].Delta!.ActorUpserts[0].CurrentHp--;
        Assert(!ReplayDocumentValidatorV11.Validate(tampered).IsValid,
            "tampering with one authoritative event invalidates state, chain, and document hashes");

        var nativeAudio = ReplayV11Document("native-audio");
        var wave = TestWavePayload();
        var waveHash = ReplayCanonicalJsonV11.Sha256(wave);
        nativeAudio.Attachments.Add(new ReplayAttachmentV11
        {
            Sha256 = waveHash,
            MediaType = "audio/wav",
            Extension = ".wav",
            Usage = "test",
            ByteLength = wave.Length,
            SampleRate = 48_000,
            Channels = 2,
            SampleFrames = 1,
            Required = true,
            Payload = wave
        });
        nativeAudio.Events[0].Audio.Add(new ReplayAudioCueV11
        {
            AssetSha256 = waveHash,
            NativeResourceId = "Sounds/card_use",
            ResolutionPolicy = "embedded-required",
            Kind = "Effect",
            Bus = "Effect"
        });
        Assert(ReplayDocumentFinalizerV11.FinalizeAndValidate(nativeAudio).IsValid
               && nativeAudio.Attachments.Count == 1,
            "v11 native replay audio requires a frozen PCM attachment");
        nativeAudio.Events[0].Audio[0].NativeResourceId = "Mods/Custom/replace.ogg";
        Assert(!ReplayDocumentValidatorV11.Validate(nativeAudio).IsValid,
            "replay native audio rejects custom MOD resource paths");

    }

    private static ReplayDocumentV11 ReplayV11Document(string recordId)
    {
        var roleRef = new ReplayContentRefV11
        {
            OwnerModId = "Witch",
            ContentKind = "Role",
            StableContentId = "role-test"
        };
        var cardRef = new ReplayContentRefV11
        {
            OwnerModId = "Witch",
            ContentKind = "Card",
            StableContentId = "card-a"
        };
        var initial = new ReplayLogicalStateV11
        {
            LevelId = "level-test",
            TurnIndex = 1,
            ActiveActorId = "role-instance",
            PlayerPower = 3,
            PlayerMaxPower = 3,
            Actors = new List<ReplayActorStateV11>
            {
                new()
                {
                    InstanceId = "role-instance",
                    Content = roleRef,
                    EntityKind = ReplayEntityKindsV11.Player,
                    Team = ReplayTeamsV11.Friendly,
                    MaxHp = 20,
                    CurrentHp = 20
                }
            },
            Cards = new List<ReplayCardStateV11>
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
        var after = ReplayProjectionStateV11.Clone(initial);
        after.PlayerPower = 2;
        after.Actors[0].CurrentHp = 13;
        after.Cards.Clear();
        var document = new ReplayDocumentV11
        {
            Header = new ReplayDocumentHeaderV11
            {
                RecordId = recordId,
                SessionId = recordId,
                LevelId = initial.LevelId,
                StartedUtc = "2026-08-20T00:00:00Z",
                EndedUtc = "2026-08-20T00:01:00Z",
                Result = "Win",
                GameBuild = "game",
                ToolBuild = "tool",
                RendererBuild = "tool",
                RuntimeFingerprint = new string('f', 64),
                RequiredCapabilities = new List<string>
                {
                    "native-battle-view.v1",
                    "exact-dependency-manifest.v1"
                }
            },
            Content = new ReplayContentManifestV11
            {
                Dependencies = new List<ReplayContentDependencyV11>
                {
                    new() { OwnerModId = "Witch", Version = "test", ManifestSha256 = new string('a', 64) }
                },
                Definitions = new List<ReplayContentDefinitionV11>
                {
                    new() { Content = roleRef, Display = new ReplayDisplaySnapshotV11 { Name = "Test Role" } },
                    new() { Content = cardRef, Display = new ReplayDisplaySnapshotV11 { Name = "Test Card" } }
                }
            },
            InitialState = initial,
            NativeBattle = new ReplayNativeBattleContextV11
            {
                BackgroundScene = "test-background",
                RoleTableJson = "{}",
                RoleQueue = new byte[] { 1 }
            },
            Events = new List<ReplayTimelineEventV11>
            {
                new()
                {
                    Sequence = 1,
                    TimeTicks = 0,
                    TurnIndex = 1,
                    EventId = "event-00000001",
                    ActionId = "action-000001",
                    EventType = ReplayEventTypesV11.ActionStarted,
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
                    EventType = ReplayEventTypesV11.ActionCompleted,
                    ActorId = "role-instance",
                    SourceInstanceId = "card-a-instance",
                    Delta = ReplayProjectionStateV11.CreateDelta(initial, after),
                    Semantics = new List<ReplaySemanticEventV11>
                    {
                        new()
                        {
                            Kind = ReplaySemanticKindsV11.Damage,
                            Action = "HpDamage",
                            ActorId = "role-instance",
                            TargetId = "role-instance",
                            Value = 7,
                            SecondaryValue = 13,
                            Label = "HP"
                        }
                    },
                    Presentation = new List<ReplayPresentationCueV11>
                    {
                        new()
                        {
                            CueId = "event-00000002.hit",
                            Kind = ReplayPresentationKindsV11.Hit,
                            DurationTicks = 480_000,
                            TargetIds = new List<string> { "role-instance" },
                            Value = 7
                        }
                    },
                    NativePresentation = new ReplayNativeActionPresentationV11
                    {
                        ActorAnimationState = "Attack",
                        PresentationDurationMilliseconds = 600
                    }
                },
                new()
                {
                    Sequence = 3,
                    TimeTicks = 1_200_000,
                    TurnIndex = 1,
                    EventId = "event-00000003",
                    EventType = ReplayEventTypesV11.BattleCompleted,
                    ActorId = "role-instance"
                }
            },
            Checkpoints = new List<ReplayCheckpointV11> { new() { EventSequence = 2 } }
        };
        return document;
    }

    private static byte[] TestWavePayload()
    {
        var result = new byte[48];
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, result, 0, 4);
        BitConverter.GetBytes(40).CopyTo(result, 4);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "), 0, result, 8, 8);
        BitConverter.GetBytes(16).CopyTo(result, 16);
        BitConverter.GetBytes((short)1).CopyTo(result, 20);
        BitConverter.GetBytes((short)2).CopyTo(result, 22);
        BitConverter.GetBytes(48_000).CopyTo(result, 24);
        BitConverter.GetBytes(192_000).CopyTo(result, 28);
        BitConverter.GetBytes((short)4).CopyTo(result, 32);
        BitConverter.GetBytes((short)16).CopyTo(result, 34);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, result, 36, 4);
        BitConverter.GetBytes(4).CopyTo(result, 40);
        return result;
    }
}
