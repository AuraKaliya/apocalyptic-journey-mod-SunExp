using System.Globalization;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
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
               && engine.Current.Actors.Single(value => value.EntityKind == ReplayEntityKindsV11.Player).CurrentHp == 13
               && engine.Current.Cards.All(item => item.InstanceId != "card-a-instance"),
            "pure v11 projection reaches authoritative after-values without combat execution");

        var missingEnemy = ReplayV11Document("missing-enemy");
        missingEnemy.InitialState.Actors.RemoveAll(value => value.EntityKind == ReplayEntityKindsV11.Enemy);
        Assert(!ReplayDocumentFinalizerV11.FinalizeAndValidate(missingEnemy).IsValid,
            "a hash-consistent document without a materialized enemy baseline never enters Ready");

        var missingTag = ReplayV11Document("missing-card-tag");
        missingTag.InitialState.Cards[0].Values.RemoveAll(value => value.Key == ReplayCardPresentationContractV11.TagKey);
        Assert(!ReplayDocumentFinalizerV11.FinalizeAndValidate(missingTag).IsValid,
            "a replay card without an explicit empty Tag cannot enter Ready");
        Assert(ReplayCardPresentationContractV11.NormalizeDocument(missingTag) == 1
               && ReplayDocumentFinalizerV11.FinalizeAndValidate(missingTag).IsValid,
            "the bounded v11 card migration restores an explicit empty Tag and a valid document hash chain");

        var presentationPayload = MatchReplayCardPresentationData.Compose(new MatchReplayCardState
        {
            CardId = "card-empty-tag",
            ReplayCardId = "card-empty-tag-instance",
            DataType = 1,
            Data = new List<MatchReplayStringValue>
            {
                new() { Key = "Id", Value = "card-empty-tag" },
                new() { Key = "Rarity", Value = "1" },
                new() { Key = "Icon", Value = "Icon/test" }
            }
        });
        Assert(presentationPayload.Data.TryGetValue("Tag", out var emptyTag)
               && emptyTag == ""
               && presentationPayload.Vars.TryGetValue("Tag", out var runtimeTag)
               && runtimeTag == ""
               && presentationPayload.Vars.TryGetValue("SpecialTag", out var specialTag)
               && specialTag == "",
            "native replay card composition preserves empty Tag semantics without building gameplay tag indexes");

        var preMaterialized = ReplayV11PreMaterializedDocument("pre-materialized");
        var migrated = ReplayMaterializedBaselineMigrationV11.Rebase(preMaterialized);
        Assert(migrated.Success
               && migrated.Changed
               && migrated.AnchorSequence == 2
               && migrated.RemovedPreludeEvents == 2
               && migrated.RemovedAttachments == 1
               && migrated.Document != null
               && ReplayPlayableBootstrapContractV11.ValidateState(migrated.Document.InitialState).Count == 0
               && migrated.Document.Events.First().Audio.Single().Kind == "BattleBgm"
               && migrated.Document.Events.Skip(1).First().EventType == ReplayEventTypesV11.ActionStarted
               && ReplayDocumentValidatorV11.Validate(migrated.Document).IsValid,
            "empty-baseline v11 records rebase to the first complete round, retain BGM and remove unpaired prelude audio");

        var unsafePrelude = ReplayV11PreMaterializedDocument("unsafe-prelude");
        unsafePrelude.Events[0].ActionId = "opening-action";
        Assert(!ReplayMaterializedBaselineMigrationV11.Rebase(unsafePrelude).Success,
            "an old replay with semantic work before materialization is rejected instead of partially rewritten");

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
        var canonicalWave = ReplayPcm16WaveContractV11.BuildPayload(
            new[] { new byte[4] },
            sampleFrames: 1,
            channels: 2,
            sampleRate: 48_000);
        Assert(canonicalWave[34] == 16
               && canonicalWave[35] == 0
               && ReplayPcm16WaveContractV11.TryRead(canonicalWave, out var canonicalInfo, out _)
               && canonicalInfo.SampleRate == 48_000
               && canonicalInfo.Channels == 2
               && canonicalInfo.SampleFrames == 1,
            "the single replay PCM writer emits a canonical 16-bit WAV header and matching sample metadata");
        var legacyMissingBits = (byte[])canonicalWave.Clone();
        legacyMissingBits[34] = 0;
        legacyMissingBits[35] = 0;
        Assert(!ReplayPcm16WaveContractV11.TryRead(legacyMissingBits, out _, out var legacyError)
               && legacyError == "bits-per-sample-invalid"
               && ReplayPcm16WaveContractV11.TryRepairLegacyMissingBits(
                   legacyMissingBits,
                   out var repairedWave,
                   out var repairedInfo,
                   out _)
               && repairedInfo.BitsPerSample == 16
               && repairedWave.Skip(ReplayPcm16WaveContractV11.HeaderBytes)
                   .SequenceEqual(legacyMissingBits.Skip(ReplayPcm16WaveContractV11.HeaderBytes)),
            "the bounded v7 migration repairs only the missing bits field and preserves every PCM sample byte");
        var invalidAudio = ReplayV11Document("invalid-pcm-wave");
        var invalidWaveHash = ReplayCanonicalJsonV11.Sha256(legacyMissingBits);
        invalidAudio.Attachments.Add(new ReplayAttachmentV11
        {
            Sha256 = invalidWaveHash,
            MediaType = "audio/wav",
            Extension = ".wav",
            Usage = "test",
            ByteLength = legacyMissingBits.Length,
            SampleRate = 48_000,
            Channels = 2,
            SampleFrames = 1,
            Required = true,
            Payload = legacyMissingBits
        });
        invalidAudio.Events[0].Audio.Add(new ReplayAudioCueV11
        {
            AssetSha256 = invalidWaveHash,
            NativeResourceId = "Sounds/card_use",
            ResolutionPolicy = "embedded-required",
            Kind = "Effect",
            Bus = "Effect"
        });
        Assert(!ReplayDocumentFinalizerV11.FinalizeAndValidate(invalidAudio).IsValid,
            "a hash-consistent attachment with a malformed PCM header cannot enter Ready");
        Assert(ReplayPcm16WaveContractV11.TryNormalizeLegacyAttachments(
                   invalidAudio,
                   out var normalizedPcmAttachments,
                   out _)
               && normalizedPcmAttachments == 1
               && invalidAudio.Events[0].Audio[0].AssetSha256
                  == invalidAudio.Attachments[0].Sha256
               && invalidAudio.Attachments[0].Sha256 != invalidWaveHash
               && ReplayDocumentFinalizerV11.FinalizeAndValidate(invalidAudio).IsValid,
            "a verified old package receives the same bounded PCM hash rewrite before entering current storage");
        nativeAudio.Events[0].Audio[0].NativeResourceId = "Mods/Custom/replace.ogg";
        Assert(!ReplayDocumentValidatorV11.Validate(nativeAudio).IsValid,
            "replay native audio rejects custom MOD resource paths");

    }

    private static ReplayDocumentV11 ReplayV11PreMaterializedDocument(string recordId)
    {
        var preMaterialized = ReplayV11Document(recordId);
        var materializedState = ReplayProjectionStateV11.Clone(preMaterialized.InitialState);
        var emptyState = new ReplayLogicalStateV11
        {
            LevelId = materializedState.LevelId,
            TurnIndex = materializedState.TurnIndex
        };
        foreach (var value in preMaterialized.Events)
        {
            value.Sequence += 2;
            value.TimeTicks += 1_800_000;
            value.EventId = "event-" + value.Sequence.ToString("D8");
        }
        preMaterialized.Events[1].CauseEventId = preMaterialized.Events[0].EventId;
        preMaterialized.Events.Insert(0, new ReplayTimelineEventV11
        {
            Sequence = 1,
            TimeTicks = 0,
            TurnIndex = 1,
            EventId = "event-00000001",
            EventType = ReplayEventTypesV11.StateChanged
        });
        preMaterialized.Events.Insert(1, new ReplayTimelineEventV11
        {
            Sequence = 2,
            TimeTicks = 1_800_000,
            TurnIndex = 1,
            EventId = "event-00000002",
            EventType = ReplayEventTypesV11.TurnChanged,
            ActorId = materializedState.ActiveActorId,
            Delta = ReplayProjectionStateV11.CreateDelta(emptyState, materializedState)
        });
        var bgmWave = TestWavePayload();
        var effectWave = TestWavePayload();
        effectWave[44] = 1;
        var bgmHash = ReplayCanonicalJsonV11.Sha256(bgmWave);
        var effectHash = ReplayCanonicalJsonV11.Sha256(effectWave);
        preMaterialized.Attachments.Add(new ReplayAttachmentV11
        {
            Sha256 = bgmHash,
            MediaType = "audio/wav",
            Extension = ".wav",
            Usage = "BattleBgm",
            ByteLength = bgmWave.Length,
            SampleRate = 48_000,
            Channels = 2,
            SampleFrames = 1,
            Required = true,
            Payload = bgmWave
        });
        preMaterialized.Attachments.Add(new ReplayAttachmentV11
        {
            Sha256 = effectHash,
            MediaType = "audio/wav",
            Extension = ".wav",
            Usage = "SetupEffect",
            ByteLength = effectWave.Length,
            SampleRate = 48_000,
            Channels = 2,
            SampleFrames = 1,
            Required = true,
            Payload = effectWave
        });
        preMaterialized.Events[0].Audio.Add(new ReplayAudioCueV11
        {
            AssetSha256 = bgmHash,
            NativeResourceId = "RoadBGM",
            ResolutionPolicy = "embedded-required",
            OwnerModId = "Witch",
            ProviderId = "native:RoadBGM",
            Kind = "BattleBgm",
            DurationSamples = 1,
            Bus = "Bgm"
        });
        preMaterialized.Events[0].Audio.Add(new ReplayAudioCueV11
        {
            AssetSha256 = effectHash,
            NativeResourceId = "Sounds/setup",
            ResolutionPolicy = "embedded-required",
            OwnerModId = "Witch",
            ProviderId = "native:Sounds/setup",
            Kind = "Effect",
            DurationSamples = 1,
            Bus = "Effect"
        });
        preMaterialized.InitialState = emptyState;
        ReplayDocumentFinalizerV11.FinalizeAndValidate(preMaterialized);
        return preMaterialized;
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
        var enemyRef = new ReplayContentRefV11
        {
            OwnerModId = "Witch",
            ContentKind = "Enemy",
            StableContentId = "enemy-test"
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
                },
                new()
                {
                    InstanceId = "enemy-instance",
                    Content = enemyRef,
                    EntityKind = ReplayEntityKindsV11.Enemy,
                    Team = ReplayTeamsV11.Enemy,
                    SlotIndex = 0,
                    MaxHp = 30,
                    CurrentHp = 30
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
                    DisplayedCost = 1,
                    Values = new List<ReplayStringValueV11>
                    {
                        new() { Key = ReplayCardPresentationContractV11.TagKey, Value = "" },
                        new() { Key = ReplayCardPresentationContractV11.RarityKey, Value = "1" },
                        new() { Key = ReplayCardPresentationContractV11.IconKey, Value = "Icon/test" }
                    }
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
                    new() { Content = cardRef, Display = new ReplayDisplaySnapshotV11 { Name = "Test Card" } },
                    new() { Content = enemyRef, Display = new ReplayDisplaySnapshotV11 { Name = "Test Enemy" } }
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
