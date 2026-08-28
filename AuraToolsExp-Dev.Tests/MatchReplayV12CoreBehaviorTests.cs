using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayV12Core()
    {
        var envelope = BuildReplayV12();
        var validation = ReplayDocumentFinalizerV12.FinalizeAndValidate(envelope);
        Assert(validation.IsValid,
            "v12 finalizer produces a valid canonical document: " + validation.Message);
        Assert(envelope.Document.Header.DocumentVersion == 12
               && MatchReplayProtocol.Version == ReplayProtocolV12.DocumentVersion
               && envelope.Document.Header.TruthRoot.Length == 64
               && envelope.Document.Header.PresentationRoot.Length == 64
               && envelope.DeclaredDocumentRoot.Length == 64,
            "v12 seals separate truth, presentation, and document roots");
        var opaqueFieldRejected = false;
        try
        {
            ReplayCanonicalJsonV12.DeserializeStrict<ReplayDocumentHeaderCoreV12>(
                "{\"RecordId\":\"record\",\"BattleSessionId\":\"battle\",\"OpaqueScript\":\"run\"}");
        }
        catch (Newtonsoft.Json.JsonException) { opaqueFieldRejected = true; }
        Assert(opaqueFieldRejected,
            "strict v12 JSON readers reject undeclared opaque fields instead of silently dropping them");
        var duplicateFieldRejected = false;
        try
        {
            ReplayCanonicalJsonV12.DeserializeStrict<ReplayDocumentHeaderCoreV12>(
                "{\"RecordId\":\"first\",\"RecordId\":\"second\"}");
        }
        catch (Newtonsoft.Json.JsonException) { duplicateFieldRejected = true; }
        Assert(duplicateFieldRejected,
            "strict v12 JSON readers reject duplicate property ambiguity");
        var decompressionBudgetRejected = false;
        try
        {
            ReplayPayloadV12.Decode<string>(ReplayPayloadV12.Encode(new string('x', 4096)), 128);
        }
        catch (InvalidDataException) { decompressionBudgetRejected = true; }
        Assert(decompressionBudgetRejected,
            "v12 compressed payload readers enforce decoded-byte budgets before JSON materialization");
        Assert(envelope.Document.TruthEvents.Count(value =>
                   value.EventType == ReplayEventTypesV12.StateDeltaApplied) == 3,
            "one causal transaction can preserve multiple ordered authoritative state deltas");
        Assert(envelope.Document.TruthEvents.All(value => !string.IsNullOrWhiteSpace(value.TransactionId))
               && envelope.Document.PresentationEvents.All(value => !string.IsNullOrWhiteSpace(value.TransactionId)),
            "all truth and presentation events belong to a causal transaction");
        Assert(envelope.Document.TruthEvents.Any(value =>
                   value.EventType == ReplayEventTypesV12.EntitySpawned
                   && value.Entity?.EntityId == "projection-unit")
               && envelope.Document.PresentationEvents.Any(value =>
                   value.EventType == ReplayEventTypesV12.SourcePresented
                   && value.Presentation?.ActorId == "projection-unit"),
            "a dynamically created friendly unit and its own card action use the same generic spawn, entity, source, and action contracts");
        Assert(envelope.Document.TruthCheckpoints.Count >= 2
               && envelope.Document.TruthCheckpoints.Count == envelope.Document.PresentationCheckpoints.Count,
            "v12 creates paired reducer-derived full checkpoints");
        Assert(envelope.Document.PresentationCheckpoints.Last().EntityViews
                   .All(item => item.AnimationState == "Idle" && item.AnimationEndsTicks == 0),
            "stable presentation checkpoints expire transient hit animations instead of freezing them forever");
        Assert(envelope.Document.Assets.Count == 1
               && envelope.Document.Assets[0].Usage == "Background",
            "v12 reachability pruning retains only referenced presentation assets");
        var payloadClone = ReplayCanonicalJsonV12.CloneAssetWithPayload(envelope.Document.Assets[0]);
        Assert(payloadClone.Payload.SequenceEqual(envelope.Document.Assets[0].Payload)
               && !ReferenceEquals(payloadClone.Payload, envelope.Document.Assets[0].Payload),
            "in-memory replay asset copies retain independent payload bytes even though canonical JSON excludes payloads");
        var transferredEnvelope = ReplayCanonicalJsonV12.Clone(envelope);
        var transferredPayloads = ReplayCanonicalJsonV12.Clone(
            ReplayAssetPayloadTransferV12.Capture(envelope.Document));
        Assert(transferredEnvelope.Document.Assets.All(item => item.Payload.Length == 0)
               && transferredPayloads.Items.Single().Payload.SequenceEqual(envelope.Document.Assets.Single().Payload),
            "network serialization carries asset bytes in a separate bounded payload set instead of the canonical manifest");
        ReplayAssetPayloadTransferV12.AttachAndValidate(transferredEnvelope.Document, transferredPayloads);
        Assert(transferredEnvelope.Document.Assets.Single().Payload.SequenceEqual(envelope.Document.Assets.Single().Payload),
            "a canonical replica becomes committable only after the exact content-addressed payload set is attached and validated");
        var truthChunks = ReplayJournalChunkerV12.Build(
            ReplayJournalLanesV12.Truth,
            envelope.Document.TruthEvents,
            ReplayJournalChunkerV12.MinimumTargetBytes);
        var presentationChunks = ReplayJournalChunkerV12.Build(
            ReplayJournalLanesV12.Presentation,
            envelope.Document.PresentationEvents,
            ReplayJournalChunkerV12.MinimumTargetBytes);
        Assert(ReplayJournalChunkerV12.Decode(ReplayJournalLanesV12.Truth, truthChunks).Count
                   == envelope.Document.TruthEvents.Count
               && ReplayJournalChunkerV12.Decode(ReplayJournalLanesV12.Presentation, presentationChunks).Count
                   == envelope.Document.PresentationEvents.Count,
            "v12 persists and restores independent hash-chained truth and presentation chunks");
        var alteredChunkMetadata = ReplayCanonicalJsonV12.Clone(truthChunks[0]);
        alteredChunkMetadata.FirstTimeTicks++;
        var alteredChunkRejected = false;
        try
        {
            ReplayJournalChunkerV12.Decode(
                ReplayJournalLanesV12.Truth,
                new[] { alteredChunkMetadata }.Concat(truthChunks.Skip(1)));
        }
        catch (InvalidDataException) { alteredChunkRejected = true; }
        Assert(alteredChunkRejected,
            "journal chunk self-hashes bind lane, ranges, time metadata, predecessor, and compressed payload");

        var presentationMutation = ReplayCanonicalJsonV12.Clone(envelope.Document);
        presentationMutation.Presentation.Cards[0].Name = "Changed visual name";
        ReplayDocumentFinalizerV12.Finalize(presentationMutation);
        Assert(presentationMutation.Header.TruthRoot == envelope.Document.Header.TruthRoot
               && presentationMutation.Header.PresentationRoot != envelope.Document.Header.PresentationRoot,
            "presentation changes cannot alter the authoritative truth root");

        var corrupted = ReplayCanonicalJsonV12.Clone(envelope);
        corrupted.Document.TruthEvents.First(value =>
            value.EventType == ReplayEventTypesV12.StateDeltaApplied).StateHashAfter = new string('0', 64);
        Assert(!ReplayDocumentValidatorV12.Validate(corrupted).IsValid,
            "v12 validation rejects a state event whose authoritative hash was modified");

        var forgedCheckpoint = ReplayCanonicalJsonV12.Clone(envelope);
        forgedCheckpoint.Document.PresentationCheckpoints[0].EntityBindings.Clear();
        forgedCheckpoint.Document.PresentationCheckpoints[0].CheckpointSha256 =
            ReplayCanonicalJsonV12.PresentationCheckpointHash(forgedCheckpoint.Document.PresentationCheckpoints[0]);
        forgedCheckpoint.Document.Header.PresentationRoot = ReplayCanonicalJsonV12.PresentationRoot(forgedCheckpoint.Document);
        forgedCheckpoint.DeclaredDocumentRoot = ReplayCanonicalJsonV12.DocumentRoot(forgedCheckpoint.Document.Header);
        Assert(ReplayDocumentValidatorV12.Validate(forgedCheckpoint).Errors
                .Any(item => item.StartsWith("checkpoint-projection-invalid", StringComparison.Ordinal)),
            "v12 rejects a self-consistently rehashed checkpoint that differs from deterministic journal projection");

        var unsupportedRequirement = ReplayCanonicalJsonV12.Clone(envelope);
        unsupportedRequirement.Document.Header.RequiredCapabilities.Add("unknown-required-capability.v1");
        unsupportedRequirement.DeclaredDocumentRoot = ReplayCanonicalJsonV12.DocumentRoot(unsupportedRequirement.Document.Header);
        Assert(ReplayDocumentValidatorV12.Validate(unsupportedRequirement).Errors.Contains("required-capability-invalid"),
            "v12 rejects unknown mandatory capabilities instead of silently playing a partial interpretation");

        var invalidTimebase = ReplayCanonicalJsonV12.Clone(envelope);
        invalidTimebase.Document.Header.TimebaseTicksPerSecond = 1_000;
        invalidTimebase.DeclaredDocumentRoot = ReplayCanonicalJsonV12.DocumentRoot(invalidTimebase.Document.Header);
        Assert(ReplayDocumentValidatorV12.Validate(invalidTimebase).Errors.Contains("version-invalid"),
            "v12 rejects a document that changes the canonical logical timebase");

        var missingAudio = ReplayCanonicalJsonV12.Clone(envelope);
        var hitCue = missingAudio.Document.PresentationEvents.First(item =>
            item.EventType == ReplayEventTypesV12.HitReactionPresented);
        hitCue.EventType = ReplayEventTypesV12.AudioPresented;
        hitCue.Presentation = new ReplayPresentationMessageV12 { Audio = new ReplayAudioCueV12() };
        ReplayDocumentFinalizerV12.Finalize(missingAudio.Document);
        missingAudio.DeclaredDocumentRoot = ReplayCanonicalJsonV12.DocumentRoot(missingAudio.Document.Header);
        Assert(ReplayDocumentValidatorV12.Validate(missingAudio).Errors
                .Any(item => item.StartsWith("audio-asset-missing", StringComparison.Ordinal)),
            "an audio presentation cannot enter Ready without its content-addressed PCM asset");

        var ledger = new ReplayTransactionLedgerV12();
        ledger.Begin("parent", ReplayTransactionKindsV12.Card, "player", "card-a");
        ledger.Begin("child", ReplayTransactionKindsV12.Passive, "player", "buff-a", "parent");
        Assert(!ledger.TryBindPresentation("player", "", out _, out var ambiguity)
               && ambiguity == "ambiguous-causal-ownership",
            "the ledger rejects ambiguous causal presentation ownership instead of guessing by time");
        Assert(ledger.TryBindPresentation("player", "buff-a", out var bound, out _)
               && bound == "child",
            "the ledger binds a presentation cue to its unique nested transaction");
        ledger.RequireAsset("child", "asset-a");
        ledger.MarkSourceCompleted("child", 4);
        ledger.MarkSourceCompleted("parent", 4);
        Assert(ledger.ObserveStableBarrier(4).Count == 0,
            "a stable state barrier cannot complete a pending-asset child or its parent");
        ledger.ResolveAsset("asset-a");
        var ready = ledger.ObserveStableBarrier(4);
        Assert(ready.SequenceEqual(new[] { "child" }),
            "the durable ledger drains after source, state watermark, barrier, and asset obligations finish");
        ledger.Complete("child");
        Assert(ledger.ObserveStableBarrier(5).Single() == "parent",
            "the parent transaction drains only after its own stable barrier");
        ledger.Complete("parent");
        Assert(ledger.OpenCount == 0,
            "completed causal transactions leave no active ledger entries");
        ledger.Begin("aborted", ReplayTransactionKindsV12.Skill, "player", "skill-a");
        ledger.Abort("aborted");
        Assert(ledger.OpenCount == 0,
            "an explicitly aborted native skill cannot leak an open replay ledger entry");

        ledger.Begin("sibling-a", ReplayTransactionKindsV12.Card, "player", "card-a");
        ledger.Begin("sibling-b", ReplayTransactionKindsV12.Skill, "player", "skill-b");
        ledger.MarkSourceCompleted("sibling-a", 6);
        ledger.MarkSourceCompleted("sibling-b", 6);
        var siblingReady = ledger.ObserveStableBarrier(6);
        Assert(siblingReady.SequenceEqual(new[] { "sibling-a", "sibling-b" }),
            "one explicit stable barrier drains completed sibling actions without guessing a unique state owner");
        ledger.Complete("sibling-a");
        ledger.Complete("sibling-b");

        var barrierCoordinator = new ReplayStableBarrierCoordinatorV12();
        Assert(barrierCoordinator.Request("card-completed", needsStateCapture: true)
               && !barrierCoordinator.Request("skill-completed", needsStateCapture: true)
               && barrierCoordinator.TryTake(out var barrierBatch)
               && barrierBatch.CaptureState
               && barrierBatch.Reasons.SequenceEqual(new[] { "card-completed", "skill-completed" })
               && barrierCoordinator.Request("next-action", needsStateCapture: false),
            "stable-barrier requests coalesce within one frame and reopen only after the scheduled batch drains");

        var checkpoint = envelope.Document.TruthCheckpoints.First();
        var seekReducer = new ReplayStateReducerV12();
        var checkpointTruthSequence = envelope.Document.TruthEvents
            .Where(item => item.Sequence <= checkpoint.EventSequence)
            .Select(item => item.Sequence)
            .DefaultIfEmpty(0L)
            .Max();
        seekReducer.Reset(checkpoint.State, checkpointTruthSequence);
        foreach (var value in envelope.Document.TruthEvents.Where(item => item.Sequence > checkpoint.EventSequence))
            seekReducer.Apply(value);
        Assert(ReplayCanonicalJsonV12.StateHash(seekReducer.Current)
               == envelope.Document.Header.FinalPublicStateSha256,
            "checkpoint seek reaches the same authoritative final state without replaying gameplay scripts");

        var assembled = new ReplayCanonicalChunkBufferV12(
            envelope.DeclaredDocumentRoot,
            "transfer-1",
            3,
            6,
            new string('a', 64));
        Assert(assembled.TrySet(2, new byte[] { 5, 6 }, 2)
               && assembled.TrySet(0, new byte[] { 1, 2 }, 2)
               && assembled.TrySet(0, new byte[] { 1, 2 }, 2)
               && assembled.TrySet(1, new byte[] { 3, 4 }, 2)
               && assembled.IsComplete
               && assembled.Join().SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }),
            "canonical replication accepts out-of-order and idempotent duplicate chunks then joins exact bytes");
        var conflicting = new ReplayCanonicalChunkBufferV12(
            envelope.DeclaredDocumentRoot,
            "transfer-2",
            1,
            2,
            new string('b', 64));
        Assert(conflicting.TrySet(0, new byte[] { 1, 2 }, 2)
               && !conflicting.TrySet(0, new byte[] { 2, 1 }, 2)
               && !conflicting.Accepts(envelope.DeclaredDocumentRoot, "other", 1, 2, new string('b', 64)),
            "canonical replication rejects conflicting duplicates and transfer-identity mixing");

        var timeBuilder = new ReplayJournalBuilderV12(
            new ReplayDocumentHeaderCoreV12 { RecordId = "time-order", BattleSessionId = "time-order" },
            new ReplayPublicStateV12());
        var timeTransaction = timeBuilder.StartTransaction(ReplayTransactionKindsV12.SystemPhase, 10, 1, 1);
        var regressedTimeRejected = false;
        try { timeBuilder.AddTruthMarker(timeTransaction, ReplayEventTypesV12.RoundStarted, 9); }
        catch (InvalidOperationException) { regressedTimeRejected = true; }
        Assert(regressedTimeRejected,
            "journal construction rejects a later step scheduled before its causal predecessor");

        var sidecar = new ReplayPovSidecarV12
        {
            ParentDocumentRoot = envelope.DeclaredDocumentRoot,
            PlayerId = "private-player",
            Events = new List<ReplayPovEventV12>
            {
                new()
                {
                    CanonicalSequence = 1,
                    TransactionId = "transaction-00000001",
                    Kind = ReplayPovEventKindsV12.RemovePrivateCard,
                    CardInstanceId = "not-present"
                }
            }
        };
        ReplayPovContractV12.Finalize(sidecar);
        Assert(ReplayPovContractV12.Validate(sidecar, requirePayloads: false) == ""
               && ReplayPovContractV12.ValidateAlignment(sidecar, envelope) == ""
               && !System.Text.Encoding.UTF8.GetString(ReplayCanonicalJsonV12.SerializeUtf8(envelope))
                   .Contains("private-player", StringComparison.Ordinal),
            "POV sidecar is valid independently and cannot enter the canonical envelope or its roots");
        var povReducer = new ReplayPovReducerV12();
        var privateCard = new ReplayPublicCardStateV12
        {
            CardInstanceId = "private-1",
            DescriptorId = "private-descriptor",
            Zone = "Hand"
        };
        povReducer.Apply(new ReplayPovEventV12 { Sequence = 1, Kind = ReplayPovEventKindsV12.UpsertPrivateCard, Card = privateCard });
        povReducer.Apply(new ReplayPovEventV12 { Sequence = 2, Kind = ReplayPovEventKindsV12.RemovePrivateCard, CardInstanceId = "private-1" });
        Assert(povReducer.Cards.Count == 0,
            "POV reducer can overlay and remove private cards without touching public state");

        var pcm = ReplayPcm16WaveContractV12.BuildPayload(
            new[] { new byte[] { 0, 0, 255, 127 } },
            sampleFrames: 2,
            channels: 1,
            sampleRate: 48_000);
        Assert(ReplayPcm16WaveContractV12.TryRead(pcm, out var wave, out _)
               && wave.SampleFrames == 2
               && wave.Channels == 1
               && ReplayPcm16WaveContractV12.DecodeSamples(pcm, wave, 8).Length == 2,
            "v12 PCM assets use one canonical bounded RIFF contract for playback and export");
    }

    internal static ReplayDocumentEnvelopeV12 BuildReplayV12(string recordId = "record-v12")
    {
        var initial = new ReplayPublicStateV12
        {
            LevelId = "level-test",
            BattlePhase = "Materialized",
            RoundSequence = 1,
            ActorTurnSequence = 1,
            ActiveActorId = "player",
            Entities = new List<ReplayEntityStateV12>
            {
                new()
                {
                    EntityId = "player",
                    SpawnGeneration = 1,
                    Team = ReplayTeamsV12.Friendly,
                    OwnerPlayerId = "p1",
                    MaxHp = 20,
                    CurrentHp = 20,
                    IsAlive = true,
                    IsPresent = true
                }
            }
        };
        var builder = new ReplayJournalBuilderV12(new ReplayDocumentHeaderCoreV12
        {
            RecordId = recordId,
            BattleSessionId = recordId + "-session",
            LevelId = "level-test",
            StartedUtc = "2026-08-27T00:00:00Z",
            EndedUtc = "2026-08-27T00:01:00Z",
            Result = "Win",
            RecorderBuild = "test"
        }, initial);
        var background = ReplayTestPngBytes();
        var backgroundHash = ReplayCanonicalJsonV12.Sha256(background);
        builder.Document.Assets.Add(new ReplayAssetV12
        {
            Sha256 = backgroundHash,
            MediaType = "image/png",
            Extension = ".png",
            Usage = "Background",
            ByteLength = background.Length,
            Width = 1,
            Height = 1,
            Payload = background
        });
        builder.Document.Assets.Add(new ReplayAssetV12
        {
            Sha256 = ReplayCanonicalJsonV12.Sha256(new byte[] { 9 }),
            MediaType = "image/png",
            Extension = ".png",
            Usage = "Unused",
            ByteLength = 1,
            Payload = new byte[] { 9 }
        });
        builder.Document.Presentation.Scene.BackgroundAssetSha256 = backgroundHash;
        builder.Document.Presentation.Scene.Anchors.Add(new ReplayLayoutAnchorV12
        {
            AnchorId = "friendly-0"
        });
        builder.Document.Presentation.Scene.Anchors.Add(new ReplayLayoutAnchorV12
        {
            AnchorId = "friendly-1"
        });
        builder.Document.Presentation.Entities.Add(new ReplayEntityDescriptorV12
        {
            DescriptorId = "entity-player",
            Archetype = ReplayEntityArchetypesV12.PlayerCombatant,
            Name = "Player",
            Animations = new List<ReplayAnimationDescriptorV12>
            {
                new()
                {
                    State = "Idle",
                    Frames = new List<ReplaySpriteFrameV12>
                    {
                        new()
                        {
                            AssetSha256 = backgroundHash,
                            RectWidth = 1,
                            RectHeight = 1
                        }
                    }
                }
            }
        });
        builder.Document.Presentation.Entities.Add(new ReplayEntityDescriptorV12
        {
            DescriptorId = "entity-projection",
            Archetype = ReplayEntityArchetypesV12.AlliedCombatant,
            Name = "Projection",
            Provenance = new ReplayContentProvenanceV12
            {
                OwnerModId = "Fixture",
                ContentKind = "Partner",
                StableContentId = "projection"
            },
            Animations = new List<ReplayAnimationDescriptorV12>
            {
                new()
                {
                    State = "Idle",
                    Frames = new List<ReplaySpriteFrameV12>
                    {
                        new() { AssetSha256 = backgroundHash, RectWidth = 1, RectHeight = 1 }
                    }
                },
                new()
                {
                    State = "Action",
                    Loop = false,
                    Frames = new List<ReplaySpriteFrameV12>
                    {
                        new() { AssetSha256 = backgroundHash, RectWidth = 1, RectHeight = 1 }
                    }
                }
            }
        });
        builder.Document.Presentation.Cards.Add(new ReplayCardDescriptorV12
        {
            DescriptorId = "card-a",
            Name = "Card A",
            ArtworkAssetSha256 = backgroundHash,
            Provenance = new ReplayContentProvenanceV12
            {
                OwnerModId = "Fixture",
                ContentKind = "Card",
                StableContentId = "card-a"
            }
        });

        var bootstrap = builder.StartTransaction(
            ReplayTransactionKindsV12.Bootstrap,
            0,
            1,
            1,
            actorId: "player");
        builder.AddTruthMarker(bootstrap, ReplayEventTypesV12.BattleMaterialized, 0, "player");
        builder.AddPresentation(
            bootstrap,
            ReplayEventTypesV12.EntityPresented,
            new ReplayPresentationMessageV12
            {
                ActorId = "player",
                EntityBinding = new ReplayEntityPresentationBindingV12
                {
                    EntityId = "player",
                    SpawnGeneration = 1,
                    DescriptorId = "entity-player",
                    LayoutAnchor = "friendly-0"
                }
            },
            0,
            "player");
        builder.CompleteTransaction(bootstrap, 1);

        var fightStart = builder.StartTransaction(
            ReplayTransactionKindsV12.SystemPhase,
            10_000,
            1,
            1,
            actorId: "player",
            label: "FightStart");
        builder.AddTruthMarker(fightStart, ReplayEventTypesV12.FightStartSignaled, 10_000, "player");
        builder.CompleteTransaction(fightStart, 11_000);

        var roundStart = builder.StartTransaction(
            ReplayTransactionKindsV12.SystemPhase,
            20_000,
            1,
            1,
            actorId: "player",
            label: "RoundStart");
        builder.AddTruthMarker(roundStart, ReplayEventTypesV12.RoundStarted, 20_000, "player");
        builder.CompleteTransaction(roundStart, 21_000);

        var card = builder.StartTransaction(
            ReplayTransactionKindsV12.Card,
            100_000,
            1,
            1,
            actorId: "player",
            sourceInstanceId: "card-a-instance",
            sourceDescriptorId: "card-a",
            label: "Card A");
        builder.AddTruthMarker(card, ReplayEventTypesV12.ActorTurnStarted, 100_000, "player");
        builder.AddPresentation(card, ReplayEventTypesV12.SourcePresented, new ReplayPresentationMessageV12
        {
            Kind = "Card",
            DescriptorId = "card-a",
            ActorId = "player",
            SourceInstanceId = "card-a-instance",
            SourceZone = "Hand",
            SourceSlot = 0,
            DurationTicks = 100_000
        }, 100_000, "player");
        builder.AddPresentation(card, ReplayEventTypesV12.ActorAnimationPresented, new ReplayPresentationMessageV12
        {
            Kind = "Action",
            ActorId = "player",
            AnimationState = "Idle",
            DurationTicks = 100_000
        }, 110_000, "player");
        var firstHit = ReplayStateReducerV12.Normalize(builder.CurrentState);
        firstHit.Entities[0].CurrentHp = 16;
        builder.ApplyObservedState(card, firstHit, 200_000);
        builder.AddPresentation(card, ReplayEventTypesV12.HitReactionPresented, new ReplayPresentationMessageV12
        {
            Kind = "Hit",
            ActorId = "player",
            AnimationState = "Hit",
            Value = 4,
            DurationTicks = 80_000
        }, 210_000, "player");
        var projectionSpawn = ReplayStateReducerV12.Normalize(builder.CurrentState);
        projectionSpawn.Entities.Add(new ReplayEntityStateV12
        {
            EntityId = "projection-unit",
            SpawnGeneration = 1,
            Team = ReplayTeamsV12.Friendly,
            OwnerPlayerId = "p1",
            SlotIndex = 1,
            MaxHp = 8,
            CurrentHp = 8,
            IsAlive = true,
            IsPresent = true
        });
        builder.ApplyObservedState(card, projectionSpawn, 220_000);
        builder.AddPresentation(card, ReplayEventTypesV12.EntityPresented, new ReplayPresentationMessageV12
        {
            Kind = "Entity",
            ActorId = "projection-unit",
            EntityBinding = new ReplayEntityPresentationBindingV12
            {
                EntityId = "projection-unit",
                SpawnGeneration = 1,
                DescriptorId = "entity-projection",
                LayoutAnchor = "friendly-1"
            }
        }, 220_000, "projection-unit");
        var projectionAction = builder.StartTransaction(
            ReplayTransactionKindsV12.Card,
            230_000,
            1,
            1,
            actorId: "projection-unit",
            sourceInstanceId: "projection-card-instance",
            sourceDescriptorId: "card-a",
            label: "Projection action",
            parentTransactionId: card);
        builder.AddPresentation(projectionAction, ReplayEventTypesV12.SourcePresented, new ReplayPresentationMessageV12
        {
            Kind = "Card",
            DescriptorId = "card-a",
            ActorId = "projection-unit",
            SourceInstanceId = "projection-card-instance",
            SourceZone = "Generated",
            DurationTicks = 80_000
        }, 230_000, "projection-unit");
        builder.AddPresentation(projectionAction, ReplayEventTypesV12.ActorAnimationPresented, new ReplayPresentationMessageV12
        {
            Kind = "Action",
            ActorId = "projection-unit",
            AnimationState = "Action",
            DurationTicks = 50_000
        }, 240_000, "projection-unit");
        builder.CompleteTransaction(projectionAction, 295_000);
        var secondHit = ReplayStateReducerV12.Normalize(builder.CurrentState);
        secondHit.Entities[0].CurrentHp = 12;
        builder.ApplyObservedState(card, secondHit, 300_000);
        builder.AddTruthMarker(card, ReplayEventTypesV12.ActorTurnCompleted, 340_000, "player");
        builder.CompleteTransaction(card, 350_000);

        var outcome = builder.StartTransaction(
            ReplayTransactionKindsV12.Outcome,
            500_000,
            1,
            1,
            actorId: "player");
        var final = ReplayStateReducerV12.Normalize(builder.CurrentState);
        final.BattlePhase = "Finalized";
        final.Outcome = "Win";
        builder.ApplyObservedState(outcome, final, 500_000);
        builder.AddTruthMarker(outcome, ReplayEventTypesV12.OutcomeEntering, 505_000, "player");
        builder.AddTruthMarker(outcome, ReplayEventTypesV12.BattleFinalized, 510_000, "player");
        builder.CompleteTransaction(outcome, 520_000);
        return new ReplayDocumentEnvelopeV12 { Document = builder.Document };
    }

    internal static byte[] ReplayTestPngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
