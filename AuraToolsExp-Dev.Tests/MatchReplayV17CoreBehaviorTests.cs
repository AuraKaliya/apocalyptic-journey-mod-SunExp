using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;
using System.Diagnostics;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayV17Core()
    {
        var envelope = BuildReplayV17();
        var validation = ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope);
        Assert(validation.IsValid,
            "v17 finalizer produces a valid perspective-instruction document: " + validation.Message);
        Assert(envelope.Document.Header.DocumentVersion == 17
               && MatchReplayProtocol.Version == ReplayProtocolV17.DocumentVersion
               && envelope.Document.Header.PerspectivePlayerId == "local-player"
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.PerspectiveVisibleState)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.ResolvedInstructionStream)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.IncrementalPersistence)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.CrashResumableFinalization)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.MeasuredNativeLayout)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.NativePrefabPresentation)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.SharedModPresentation)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.ObservedOverlapTracks)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.VisualStateCommit)
               && envelope.Document.Header.RequiredCapabilities.Contains(ReplayCapabilitiesV17.NativeCardView)
               && envelope.Document.Header.TruthRoot.Length == 64
               && envelope.Document.Header.PresentationRoot.Length == 64
               && envelope.DeclaredDocumentRoot.Length == 64,
            "v17 seals one fixed perspective, incremental durability, and separate journal roots");
        Assert(envelope.Document.InitialState.Cards.Any(item => item.Zone == "Hand" && item.IsRevealed)
               && envelope.Document.InitialState.ZoneCounts.Any(item => item.Zone == "Draw")
               && envelope.Document.InitialState.Resources.Any(item => item.ResourceId == "Power")
               && envelope.Document.Presentation.Scene.SceneResourcePath == "TestBattle"
               && envelope.Document.Presentation.Entities.All(item =>
                   item.Animations.All(animation => animation.ResourcePath.Length > 0))
               && envelope.Document.PresentationEvents.Any(item => item.EventType == ReplayEventTypesV17.CardMotionPresented)
               && envelope.Document.PresentationEvents.Any(item => item.EventType == ReplayEventTypesV17.DamageTextPresented),
            "v17 stores visible hand/HUD state and resource-backed resolved instructions instead of a Unity hierarchy snapshot");

        TestVisibleStateReducer();
        TestNativeAnimationFrameAliases();
        TestIntentVisualResolution();
        TestActionSourceClassification();
        TestObservedPresentationTiming(envelope);
        TestLatePresentationEventTimeAndDurability(envelope);
        TestCardVisualLifecycle();
        TestReplayHotPathBudget();
        TestTamperAndContractRejection(envelope);
        TestCheckpointSeek(envelope);
        TestChunkAndPayloadBoundaries(envelope);
        TestCausalLedgerAndStableBarrier();
    }

    private static void TestActionSourceClassification()
    {
        var card = ReplayActionSourceClassifierV17.Classify("Card");
        Assert(card.Supported
               && card.DescriptorKind == ReplayActionSourceDescriptorKindsV17.Card
               && card.TransactionKind == ReplayTransactionKindsV17.ImplicitObserved
               && card.SourceZone.Length == 0,
            "an implicit native Card action remains a card source transaction");
        var routedCardRegistrations = 0;
        var routedIntentRegistrations = 0;
        var routedCard = ReplayActionSourceClassifierV17.RouteDescriptor(
            card,
            () =>
            {
                routedCardRegistrations++;
                return new ReplayActionSourceDescriptorIdentityV17
                {
                    DescriptorId = "card:Witch:strike",
                    Name = "Strike"
                };
            },
            () =>
            {
                routedIntentRegistrations++;
                return new ReplayActionSourceDescriptorIdentityV17 { DescriptorId = "wrong-intent" };
            });
        Assert(routedCardRegistrations == 1
               && routedIntentRegistrations == 0
               && routedCard.DescriptorId == "card:Witch:strike",
            "a native Card action enters only the card descriptor catalog");

        var enemyIntent = ReplayActionSourceClassifierV17.Classify("EnemyCard");
        var partnerIntent = ReplayActionSourceClassifierV17.Classify("PartnerCard");
        Assert(enemyIntent.Supported
               && enemyIntent.DescriptorKind == ReplayActionSourceDescriptorKindsV17.Intent
               && enemyIntent.TransactionKind == ReplayTransactionKindsV17.Intent
               && enemyIntent.SourceZone == "Intent"
               && partnerIntent.Supported
               && partnerIntent.DescriptorKind == ReplayActionSourceDescriptorKindsV17.Intent
               && partnerIntent.TransactionKind == ReplayTransactionKindsV17.Intent
               && partnerIntent.SourceZone == "Intent",
            "EnemyCard and PartnerCard action sources are replay intents, not card artwork");

        var cardRegistrations = 0;
        var intentRegistrations = 0;
        var routed = ReplayActionSourceClassifierV17.RouteDescriptor(
            enemyIntent,
            () =>
            {
                cardRegistrations++;
                return new ReplayActionSourceDescriptorIdentityV17 { DescriptorId = "wrong-card" };
            },
            () =>
            {
                intentRegistrations++;
                return new ReplayActionSourceDescriptorIdentityV17
                {
                    DescriptorId = "intent:Terrias:Terrias_terrias_enemycard_spirit_intent_adapter",
                    Name = "蹈火之域：三阶"
                };
            });
        Assert(cardRegistrations == 0
               && intentRegistrations == 1
               && routed.DescriptorId == "intent:Terrias:Terrias_terrias_enemycard_spirit_intent_adapter",
            "the Terrias spirit intent adapter can only enter the intent descriptor catalog");

        var unsupported = ReplayActionSourceClassifierV17.Classify("Buff");
        var unsupportedRejected = false;
        try
        {
            ReplayActionSourceClassifierV17.RouteDescriptor(
                unsupported,
                () => new ReplayActionSourceDescriptorIdentityV17 { DescriptorId = "card" },
                () => new ReplayActionSourceDescriptorIdentityV17 { DescriptorId = "intent" });
        }
        catch (InvalidOperationException)
        {
            unsupportedRejected = true;
        }
        Assert(!unsupported.Supported
               && unsupported.FailureReason == "unsupported-native-action-data-type:Buff"
               && unsupportedRejected,
            "unknown native action data types fail recording instead of being guessed into a visual catalog");
    }

    private static void TestObservedPresentationTiming(ReplayDocumentEnvelopeV17 envelope)
    {
        var action = envelope.Document.TruthEvents.First(item =>
            item.EventType == ReplayEventTypesV17.TransactionStarted
            && item.Transaction?.Kind == ReplayTransactionKindsV17.Card).TransactionId;
        var events = envelope.Document.PresentationEvents
            .Where(item => item.TransactionId == action)
            .OrderBy(item => item.TimeTicks)
            .ThenBy(item => item.Sequence)
            .ToList();
        var motion = events.Single(item => item.EventType == ReplayEventTypesV17.CardMotionPresented);
        var actor = events.Single(item => item.EventType == ReplayEventTypesV17.ActorAnimationPresented);
        var commit = events.Single(item => item.EventType == ReplayEventTypesV17.VisualStateCommitted);
        var completed = envelope.Document.TruthEvents.Single(item =>
            item.TransactionId == action && item.EventType == ReplayEventTypesV17.TransactionCompleted);
        Assert(events.All(item => (item.Presentation?.DelayTicks ?? 0) == 0)
               && motion.TimeTicks < actor.TimeTicks
               && commit.TimeTicks >= actor.TimeTicks
               && motion.Presentation!.TransformSamples.Count == 2
               && actor.Presentation!.WorldTransformSamples.Count == 2
               && completed.TimeTicks < actor.TimeTicks + actor.Presentation.DurationTicks,
            "v17 preserves observed overlapping card, actor, impact, and state tracks without synthetic phase delays");
    }

    private static void TestLatePresentationEventTimeAndDurability(ReplayDocumentEnvelopeV17 envelope)
    {
        var observedInitial = ReplayCanonicalJsonV17.Clone(envelope.Document.InitialState);
        var observedBuilder = new ReplayJournalBuilderV17(
            new ReplayDocumentHeaderCoreV17
            {
                RecordId = "late-observation",
                BattleSessionId = "late-observation",
                PerspectivePlayerId = observedInitial.PerspectivePlayerId,
                PerspectiveKind = "Player"
            },
            observedInitial);
        var passive = observedBuilder.StartTransaction(
            ReplayTransactionKindsV17.Passive,
            200L,
            observedInitial.RoundSequence,
            observedInitial.ActorTurnSequence,
            "late-spirit");
        var withSpirit = ReplayCanonicalJsonV17.Clone(observedInitial);
        withSpirit.Entities.Add(new ReplayEntityStateV17
        {
            EntityId = "late-spirit",
            DescriptorId = "entity-late-spirit",
            SpawnGeneration = 1,
            Team = ReplayTeamsV17.Friendly,
            SlotIndex = withSpirit.Entities.Count,
            IsPresent = true,
            IsAlive = true,
            MaxHp = 20,
            CurrentHp = 20
        });
        var spawned = observedBuilder.ApplyObservedState(passive, withSpirit, 200L)
            .Single(item => item.EventType == ReplayEventTypesV17.EntitySpawned);
        var lateExtension = observedBuilder.AddPresentation(
            passive,
            ReplayEventTypesV17.ExtensionPresented,
            new ReplayPresentationMessageV17
            {
                Kind = "VisibilityChanged",
                ActorId = "late-spirit",
                ExtensionOwnerModId = "FixtureMod",
                ExtensionTypeId = "SpiritBattlePresentation",
                ExtensionSchemaVersion = 1,
                ExtensionEventId = "late-spirit-spawn",
                ExtensionPayloadJson = "{\"visible\":true}",
                Persistent = true,
                DurationTicks = 1L
            },
            150L,
            "late-spirit");
        observedBuilder.CompleteTransaction(passive, 210L);
        Assert(lateExtension.Sequence > spawned.Sequence
               && lateExtension.TimeTicks < spawned.TimeTicks,
            "an entity-delayed MOD presentation keeps its original observed time after the truth entity materializes");

        var late = ReplayCanonicalJsonV17.Clone(envelope);
        var latePresentation = late.Document.PresentationEvents
            .OrderByDescending(item => item.Sequence)
            .First();
        latePresentation.TimeTicks = 1L;
        var lateValidation = ReplayDocumentFinalizerV17.FinalizeAndValidate(late);
        Assert(lateValidation.IsValid,
            "v17 accepts a late-bound presentation observation while truth time remains monotonic: "
            + lateValidation.Message);

        var regressedTruth = ReplayCanonicalJsonV17.Clone(envelope);
        var orderedTruth = regressedTruth.Document.TruthEvents.OrderBy(item => item.Sequence).ToList();
        orderedTruth[1].TimeTicks = Math.Max(0L, orderedTruth[0].TimeTicks - 1L);
        if (orderedTruth[0].TimeTicks == 0L)
        {
            orderedTruth[0].TimeTicks = 10L;
            orderedTruth[1].TimeTicks = 1L;
        }
        var truthValidation = ReplayDocumentFinalizerV17.FinalizeAndValidate(regressedTruth);
        Assert(truthValidation.Errors.Any(item => item.StartsWith(
                "truth-logical-time-regressed:",
                StringComparison.Ordinal)),
            "truth state time remains strictly ordered even though presentation event time may arrive late");

        var actionId = envelope.Document.TruthEvents.First(item =>
            item.EventType == ReplayEventTypesV17.TransactionStarted
            && item.Transaction?.Kind == ReplayTransactionKindsV17.Card).TransactionId;
        var firstActionSequence = envelope.Document.TruthEvents
            .Concat(envelope.Document.PresentationEvents)
            .Where(item => item.TransactionId == actionId)
            .Min(item => item.Sequence);
        var mutable = envelope.Document.PresentationEvents.First(item => item.TransactionId == actionId).Sequence;
        var openWatermark = ReplayDurableJournalPrefixV17.LastDurableSequence(
            envelope.Document,
            new[] { actionId },
            Array.Empty<long>());
        var mutableWatermark = ReplayDurableJournalPrefixV17.LastDurableSequence(
            envelope.Document,
            Array.Empty<string>(),
            new[] { mutable });
        var fullyDurable = ReplayDurableJournalPrefixV17.LastDurableSequence(
            envelope.Document,
            Array.Empty<string>(),
            Array.Empty<long>());
        Assert(openWatermark == firstActionSequence - 1L
               && mutableWatermark == mutable - 1L
               && fullyDurable == envelope.Document.TruthEvents
                   .Concat(envelope.Document.PresentationEvents)
                   .Max(item => item.Sequence),
            "incremental persistence stops before open transactions and presentation tracks that can still mutate");

        var deferred = new ReplayDeferredObservationQueueV17<ReplayJournalEventV17>(
            2,
            item => item.Sequence);
        var later = new ReplayJournalEventV17 { Sequence = 2L, EventId = "later" };
        var earlier = new ReplayJournalEventV17 { Sequence = 1L, EventId = "earlier" };
        Assert(deferred.TryEnqueue(later)
               && deferred.TryEnqueue(earlier)
               && deferred.Ready(_ => true).Select(item => item.EventId)
                   .SequenceEqual(new[] { "earlier", "later" })
               && deferred.Count == 2,
            "reading ready deferred observations preserves capture order without consuming obligations");
        Assert(deferred.Commit(earlier)
               && deferred.Count == 1
               && deferred.Snapshot.Single().EventId == "later",
            "a deferred observation is removed only after the downstream journal commit succeeds");
    }

    private static void TestCardVisualLifecycle()
    {
        Assert(ReplayCardVisualLifecycleV17.ResetMatches(101, "card-a", 101, "card-a")
               && ReplayCardVisualLifecycleV17.ResetMatches(101, "card-a", 101, "")
               && !ReplayCardVisualLifecycleV17.ResetMatches(101, "card-a", 102, "card-a")
               && !ReplayCardVisualLifecycleV17.ResetMatches(101, "card-a", 101, "card-b"),
            "pooled card Reset closes only the exact visual root and compatible source identity");

        Assert(ReplayCardVisualLifecycleV17.CompletionReason(
                   true, true, true, false, 1) == ReplayCardVisualLifecycleV17.SharedReset
               && ReplayCardVisualLifecycleV17.CompletionReason(
                   false, false, false, false, 1) == ReplayCardVisualLifecycleV17.Destroyed
               && ReplayCardVisualLifecycleV17.CompletionReason(
                   false, true, false, false, 1) == ReplayCardVisualLifecycleV17.Inactive
               && ReplayCardVisualLifecycleV17.CompletionReason(
                   false, true, true, true, 1) == ReplayCardVisualLifecycleV17.Rebound,
            "card motion completes on shared pool reset, native destruction, inactive return, or root reuse");

        Assert(ReplayCardVisualLifecycleV17.CompletionReason(
                   false,
                   true,
                   true,
                   false,
                   ReplayCardVisualLifecycleV17.TimeoutTicks) == ""
               && ReplayCardVisualLifecycleV17.CompletionReason(
                   false,
                   true,
                   true,
                   false,
                   ReplayCardVisualLifecycleV17.TimeoutTicks + 1) == ReplayCardVisualLifecycleV17.Timeout,
            "an active visual never settles by heuristic; the watchdog remains fatal until an authoritative lifecycle boundary occurs");
    }

    private static void TestNativeAnimationFrameAliases()
    {
        var repeatedNames = new[] { "梅花侍从", "梅花侍从" };
        Assert(new[] { "梅花侍从", "魔女教教徒" }.All(name =>
                ReplayFrameSequenceContractV17.ValidateNames(new[] { name, name }, required: true).Length == 0),
            "v17 treats repeated native Sprite names as ordered frame aliases rather than duplicate descriptors");

        var candidates = new List<ReplayFrameCandidate>
        {
            new("asset-subobject", "梅花侍从"),
            new("png-subobject", "梅花侍从")
        };
        Assert(ReplayFrameSequenceContractV17.TryResolveOrdered(
                   candidates,
                   item => item.Name,
                   repeatedNames,
                   out var resolved,
                   out var resolutionError)
               && resolutionError.Length == 0
               && resolved.Select(item => item.Identity).SequenceEqual(
                   new[] { "asset-subobject", "png-subobject" }),
            "v17 preserves both equal-name Hit frame occurrences and their native duration");

        Assert(!ReplayFrameSequenceContractV17.TryResolveOrdered(
                   candidates.Take(1).ToList(),
                   item => item.Name,
                   repeatedNames,
                   out _,
                   out var countError)
               && countError.StartsWith("resource-frame-count-mismatch", StringComparison.Ordinal),
            "v17 rejects a truly incomplete resource frame sequence instead of manufacturing an alias");

        var names = new[] { "Idle 10", "Idle2", "Idle1" };
        Array.Sort(names, ReplayNativeFrameNameComparerV17.Instance);
        Assert(names.SequenceEqual(new[] { "Idle1", "Idle2", "Idle 10" })
               && ReplayFrameSequenceContractV17.ValidateNames(new[] { "", "Hit" }, required: true)
                   .StartsWith("frame-name-empty", StringComparison.Ordinal),
            "replay frame ordering exactly follows the native NaturalStringComparer and still rejects empty identities");
    }

    private static void TestIntentVisualResolution()
    {
        var nativeResources = new HashSet<string>(StringComparer.Ordinal)
        {
            ReplayIntentVisualContractV17.DefaultIconResourcePath,
            ReplayIntentVisualContractV17.DefaultBackIconResourcePath,
            "Icon/ActionIcon/负面底"
        };
        Assert(ReplayIntentVisualContractV17.TryResolve(
                   "Icon/ActionIcon/给予异常",
                   ReplayIntentVisualContractV17.DefaultIconResourcePath,
                   nativeResources.Contains,
                   out var icon,
                   out var iconError)
               && iconError.Length == 0
               && icon.UsedFallback
               && icon.ResolvedPath == ReplayIntentVisualContractV17.DefaultIconResourcePath,
            "v17 captures the native intent-icon fallback for enemycard_Toxin1 instead of persisting its stale request path");
        Assert(ReplayIntentVisualContractV17.TryResolve(
                   "Icon/ActionIcon/负面底",
                   ReplayIntentVisualContractV17.DefaultBackIconResourcePath,
                   nativeResources.Contains,
                   out var background,
                   out _)
               && !background.UsedFallback
               && background.ResolvedPath == "Icon/ActionIcon/负面底",
            "v17 preserves a resolvable native intent background without manufacturing a fallback");
        Assert(!ReplayIntentVisualContractV17.TryResolve(
                   "Icon/ActionIcon/missing",
                   "Icon/ActionIcon/missing-fallback",
                   nativeResources.Contains,
                   out _,
                   out var missingError)
               && missingError.StartsWith("intent-visual-resource-unresolvable", StringComparison.Ordinal),
            "v17 still rejects an intent visual when both the requested and native fallback resources are absent");
    }

    private static void TestReplayHotPathBudget()
    {
        var state = new ReplayVisibleStateV17
        {
            LevelId = "performance-level",
            PerspectivePlayerId = "player",
            BattlePhase = "Active",
            RoundSequence = 1,
            ActorTurnSequence = 1,
            Entities = Enumerable.Range(0, 48).Select(index => new ReplayEntityStateV17
            {
                EntityId = "entity-" + index,
                DescriptorId = "entity:fixture:" + index,
                SpawnGeneration = 1,
                Team = index == 0 ? ReplayTeamsV17.Friendly : ReplayTeamsV17.Enemy,
                SlotIndex = index,
                MaxHp = 100,
                CurrentHp = 100,
                Buffs = Enumerable.Range(0, 4).Select(buff => new ReplayBuffStateV17
                {
                    InstanceId = index + "|buff-" + buff,
                    DescriptorId = "buff:" + buff,
                    Level = buff + 1
                }).ToList()
            }).ToList(),
            Cards = Enumerable.Range(0, 80).Select(index => new ReplayVisibleCardStateV17
            {
                CardInstanceId = "card-" + index,
                DescriptorId = "card:fixture:" + index,
                OwnerPlayerId = "player",
                Zone = "Hand",
                Order = index,
                IsRevealed = true
            }).ToList()
        };
        var builder = new ReplayJournalBuilderV17(new ReplayDocumentHeaderCoreV17
        {
            RecordId = "hot-path",
            BattleSessionId = "hot-path",
            PerspectivePlayerId = "player"
        }, state);
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 300; index++)
        {
            var transaction = builder.StartTransaction(
                ReplayTransactionKindsV17.SystemPhase,
                index * 2L,
                1,
                1,
                "entity-0",
                label: "audio-" + index);
            builder.CompleteTransaction(transaction, index * 2L + 1);
        }
        stopwatch.Stop();
        Assert(builder.Document.TruthEvents.Count == 600
               && stopwatch.ElapsedMilliseconds < 2000,
            "unchanged-state system/audio transactions reuse the visible-state hash and remain within the main-thread construction budget; elapsedMs="
            + stopwatch.ElapsedMilliseconds);
    }

    private static void TestVisibleStateReducer()
    {
        var before = ReplayStateReducerV17.Normalize(new ReplayVisibleStateV17
        {
            LevelId = "level",
            PerspectivePlayerId = "player",
            RoundSequence = 1,
            ActorTurnSequence = 1,
            Entities = new List<ReplayEntityStateV17>
            {
                new()
                {
                    EntityId = "enemy",
                    DescriptorId = "entity:enemy",
                    SpawnGeneration = 1,
                    Team = ReplayTeamsV17.Enemy,
                    MaxHp = 100,
                    CurrentHp = 100
                }
            },
            Cards = new List<ReplayVisibleCardStateV17>
            {
                new()
                {
                    CardInstanceId = "card",
                    DescriptorId = "card:test",
                    OwnerPlayerId = "player",
                    Zone = "Hand",
                    IsRevealed = true,
                    HasMeasuredLayout = true,
                    CanvasPosition = new ReplayVector2Q16V17 { Y = 100 * 65_536 },
                    CanvasSize = new ReplayVector2Q16V17 { X = 300 * 65_536, Y = 460 * 65_536 },
                    LocalScale = new ReplayVector3Q16V17 { X = 32_768, Y = 32_768, Z = 65_536 }
                }
            },
            ZoneCounts = new List<ReplayVisibleZoneCountV17>
            {
                new() { OwnerPlayerId = "player", Zone = "Hand", Count = 1 }
            },
            Resources = new List<ReplayVisibleResourceStateV17>
            {
                new() { OwnerPlayerId = "player", ResourceId = "Power", Value = 3, Maximum = 3, DisplayText = "3/3" }
            },
            Extensions = new List<ReplayVisibleExtensionStateV17>
            {
                new() { OwnerModId = "Terrias", TypeId = "Spirit", InstanceId = "spirit-1", PayloadJson = "{\"rank\":1}" }
            }
        });
        var after = ReplayCanonicalJsonV17.Clone(before);
        after.Entities[0].CurrentHp = 72;
        after.Entities[0].Buffs.Add(new ReplayBuffStateV17
        {
            InstanceId = "enemy|burn",
            DescriptorId = "buff:burn",
            Level = 2
        });
        after.Cards[0].Zone = "Discard";
        after.ZoneCounts[0].Count = 0;
        after.Resources[0].Value = 2;
        after.Resources[0].DisplayText = "2/3";
        after.Extensions[0].PayloadJson = "{\"rank\":2}";
        var diff = ReplayStateReducerV17.CreateDiff(before, after);
        var reduced = ReplayStateReducerV17.Apply(before, diff);
        after.StateVersion = reduced.StateVersion;
        Assert(diff.Delta.Operations.Any(item => item.Kind == ReplayStateOperationKindsV17.SetEntityVitals)
               && diff.Delta.Operations.Any(item => item.Kind == ReplayStateOperationKindsV17.ReplaceVisibleBuffs)
               && diff.Delta.Operations.Any(item => item.Kind == ReplayStateOperationKindsV17.MoveVisibleCard)
               && diff.Delta.Operations.Any(item => item.Kind == ReplayStateOperationKindsV17.SetVisibleZoneCount)
               && diff.Delta.Operations.Any(item => item.Kind == ReplayStateOperationKindsV17.ReplaceVisibleResources)
               && diff.Delta.Operations.Any(item => item.Kind == ReplayStateOperationKindsV17.ReplaceVisibleExtensions)
               && ReplayCanonicalJsonV17.StateHash(reduced) == ReplayCanonicalJsonV17.StateHash(after),
            "visible-state reduction captures HP, BUFF, hand/pile, resource, and owner-qualified extension increments");

        var changedPerspectiveRejected = false;
        var changedPerspective = ReplayCanonicalJsonV17.Clone(after);
        changedPerspective.PerspectivePlayerId = "another-player";
        try { ReplayStateReducerV17.CreateDiff(before, changedPerspective); }
        catch (InvalidOperationException) { changedPerspectiveRejected = true; }
        Assert(changedPerspectiveRejected,
            "a replay cannot silently change its fixed player perspective during one battle");

        var relayout = ReplayCanonicalJsonV17.Clone(before);
        relayout.Cards[0].CanvasPosition.X += 65_536;
        var layoutDiff = ReplayStateReducerV17.CreateDiff(before, relayout);
        Assert(layoutDiff.Delta.Operations.Count == 1
               && layoutDiff.Delta.Operations[0].Kind == ReplayStateOperationKindsV17.MoveVisibleCard
               && layoutDiff.Delta.Operations[0].Card?.CanvasPosition.X == relayout.Cards[0].CanvasPosition.X,
            "measured native hand relayout is persisted as one incremental visible-card operation");
    }

    private static void TestTamperAndContractRejection(ReplayDocumentEnvelopeV17 envelope)
    {
        var tamperedState = ReplayCanonicalJsonV17.Clone(envelope);
        var delta = tamperedState.Document.TruthEvents.First(item => item.Delta?.Operations.Count > 0).Delta!;
        delta.Operations.First(item => item.Kind == ReplayStateOperationKindsV17.SetEntityVitals).CurrentHp--;
        Assert(!ReplayDocumentValidatorV17.Validate(tamperedState).IsValid,
            "v17 rejects a visible-state mutation that was not in the sealed hash chain");

        var wrongPerspective = ReplayCanonicalJsonV17.Clone(envelope);
        wrongPerspective.Document.Header.PerspectivePlayerId = "other-player";
        wrongPerspective.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(wrongPerspective.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(wrongPerspective).Errors.Contains("perspective-identity-invalid"),
            "v17 rejects a header/state perspective mismatch even when the header root is recomputed");

        var pathTraversal = ReplayCanonicalJsonV17.Clone(envelope);
        pathTraversal.Document.Presentation.Scene.SceneResourcePath = "../../outside";
        ReplayDocumentFinalizerV17.Finalize(pathTraversal.Document);
        pathTraversal.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(pathTraversal.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(pathTraversal).Errors.Contains("scene-descriptor-invalid"),
            "resource-backed replay descriptors reject traversal paths");

        var missingEntityLayout = ReplayCanonicalJsonV17.Clone(envelope);
        missingEntityLayout.Document.PresentationEvents
            .First(item => item.Presentation?.EntityBinding != null)
            .Presentation!.EntityBinding!.HasMeasuredLayout = false;
        ReplayDocumentFinalizerV17.Finalize(missingEntityLayout.Document);
        missingEntityLayout.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(missingEntityLayout.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(missingEntityLayout).Errors.Any(item =>
                item.StartsWith("entity-descriptor-missing", StringComparison.Ordinal)
                || item.StartsWith("checkpoint-entity-layout-invalid", StringComparison.Ordinal)),
            "v17 rejects entity projection without measured root/body/head/bottom layout");

        var missingHandLayout = ReplayCanonicalJsonV17.Clone(envelope);
        missingHandLayout.Document.InitialState.Cards[0].HasMeasuredLayout = false;
        ReplayDocumentFinalizerV17.Finalize(missingHandLayout.Document);
        missingHandLayout.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(missingHandLayout.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(missingHandLayout).Errors.Any(item =>
                item.StartsWith("visible-card-layout-missing", StringComparison.Ordinal)),
            "v17 rejects visible hand cards without measured native canvas layout");

        var nonNativeCard = ReplayCanonicalJsonV17.Clone(envelope);
        nonNativeCard.Document.Presentation.Cards[0].NativeVisualTemplateRequired = false;
        ReplayDocumentFinalizerV17.Finalize(nonNativeCard.Document);
        nonNativeCard.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(nonNativeCard.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(nonNativeCard).Errors.Any(item =>
                item.StartsWith("card-descriptor-invalid:", StringComparison.Ordinal)),
            "v17 refuses generic replay cards that bypass the passive native CardItem template");

        var intentRegisteredAsCard = ReplayCanonicalJsonV17.Clone(envelope);
        intentRegisteredAsCard.Document.TruthEvents.First(item =>
                item.EventType == ReplayEventTypesV17.TransactionStarted
                && item.Transaction?.Kind == ReplayTransactionKindsV17.Card)
            .Transaction!.Kind = ReplayTransactionKindsV17.Intent;
        ReplayDocumentFinalizerV17.Finalize(intentRegisteredAsCard.Document);
        intentRegisteredAsCard.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(
            intentRegisteredAsCard.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(intentRegisteredAsCard).Errors.Any(item =>
                item.StartsWith("action-source-descriptor-kind-invalid:", StringComparison.Ordinal)),
            "v17 rejects an intent transaction whose source was incorrectly registered as card artwork");

        var missingVisualCommit = ReplayCanonicalJsonV17.Clone(envelope);
        var cardTransactionId = missingVisualCommit.Document.TruthEvents.First(item =>
            item.EventType == ReplayEventTypesV17.TransactionStarted
            && item.Transaction?.Kind == ReplayTransactionKindsV17.Card).TransactionId;
        missingVisualCommit.Document.PresentationEvents.RemoveAll(item =>
            item.EventType == ReplayEventTypesV17.VisualStateCommitted
            && item.TransactionId == cardTransactionId);
        ReplayDocumentFinalizerV17.Finalize(missingVisualCommit.Document);
        missingVisualCommit.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(
            missingVisualCommit.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(missingVisualCommit).Errors.Any(item =>
                item.StartsWith("visual-state-commit-mismatch:", StringComparison.Ordinal)),
            "v17 rejects an action whose HP, BUFF, hand, or pile delta has no explicit visual commit");

        var outOfOrderPhase = ReplayCanonicalJsonV17.Clone(envelope);
        outOfOrderPhase.Document.PresentationEvents.First(item =>
            item.EventType == ReplayEventTypesV17.CardMotionPresented).Presentation!.PhaseOrdinal = 5;
        ReplayDocumentFinalizerV17.Finalize(outOfOrderPhase.Document);
        outOfOrderPhase.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(outOfOrderPhase.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(outOfOrderPhase).Errors.Any(item =>
                item.StartsWith("presentation-phase-order-invalid:", StringComparison.Ordinal)),
            "v17 rejects card/action/impact/state phases that would visually execute out of order");

        var missingObservedTrack = ReplayCanonicalJsonV17.Clone(envelope);
        missingObservedTrack.Document.PresentationEvents.First(item =>
            item.EventType == ReplayEventTypesV17.CardMotionPresented).Presentation!.TransformSamples.Clear();
        ReplayDocumentFinalizerV17.Finalize(missingObservedTrack.Document);
        missingObservedTrack.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(missingObservedTrack.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(missingObservedTrack).Errors.Any(item =>
                item.StartsWith("card-motion-track-missing:", StringComparison.Ordinal)),
            "v17 rejects a card motion that lacks its observed native transform track");

        var missingCustomOwner = ReplayCanonicalJsonV17.Clone(envelope);
        missingCustomOwner.Document.PresentationEvents.First(item =>
            item.Presentation?.EntityBinding?.CustomPresentation != null)
            .Presentation!.EntityBinding!.CustomPresentation!.OwnerEntityId = "missing-owner";
        ReplayDocumentFinalizerV17.Finalize(missingCustomOwner.Document);
        missingCustomOwner.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(missingCustomOwner.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(missingCustomOwner).Errors.Any(item =>
                item.StartsWith("presentation-owner-entity-missing:", StringComparison.Ordinal)),
            "v17 rejects an owner-attached MOD entity presentation whose owner is not active");

        var nonCanonicalExtension = ReplayCanonicalJsonV17.Clone(envelope);
        nonCanonicalExtension.Document.InitialState.Extensions[0].PayloadJson = "{\"z\":1,\"a\":2}";
        ReplayDocumentFinalizerV17.Finalize(nonCanonicalExtension.Document);
        nonCanonicalExtension.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(nonCanonicalExtension.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(nonCanonicalExtension).Errors.Any(item =>
                item.StartsWith("visible-extension-invalid", StringComparison.Ordinal)),
            "owner-qualified extension payloads must already be canonical JSON");

        var nonCanonicalPresentationExtension = ReplayCanonicalJsonV17.Clone(envelope);
        nonCanonicalPresentationExtension.Document.PresentationEvents.First(item =>
                item.EventType == ReplayEventTypesV17.ExtensionPresented)
            .Presentation!.ExtensionPayloadJson = "{\"z\":1,\"a\":2}";
        ReplayDocumentFinalizerV17.Finalize(nonCanonicalPresentationExtension.Document);
        nonCanonicalPresentationExtension.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(
            nonCanonicalPresentationExtension.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(nonCanonicalPresentationExtension).Errors.Any(item =>
                item.StartsWith("presentation-extension-invalid", StringComparison.Ordinal)),
            "presentation extension payloads remain protected by the v17 canonical document validator");

        var missingFrameSequence = ReplayCanonicalJsonV17.Clone(envelope);
        missingFrameSequence.Document.Presentation.Entities[0].Animations[0].FrameNames.Clear();
        ReplayDocumentFinalizerV17.Finalize(missingFrameSequence.Document);
        missingFrameSequence.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(
            missingFrameSequence.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(missingFrameSequence).Errors.Any(item =>
                item.StartsWith("entity-animation-invalid:", StringComparison.Ordinal)
                && item.EndsWith("frame-sequence-empty", StringComparison.Ordinal)),
            "v17 reports a field-specific animation error when a required resource sequence is actually missing");

        var excessiveOperations = ReplayCanonicalJsonV17.Clone(envelope);
        var excessiveDelta = excessiveOperations.Document.TruthEvents.First(item => item.Delta != null).Delta!;
        while (excessiveDelta.Operations.Count <= ReplayLimitsV17.MaximumOperationsPerTransaction)
            excessiveDelta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.SetActiveActor,
                ActiveActorId = "player-entity"
            });
        Assert(ReplayDocumentValidatorV17.Validate(excessiveOperations).Errors.Contains("state-operation-budget-exceeded"),
            "one malformed transaction cannot force an unbounded reducer workload");

        Assert(ReplayCanonicalJsonV17.TryCanonicalizeJsonPayload("{\"a\":1,\"b\":2}", out var canonical)
               && canonical == "{\"a\":1,\"b\":2}"
               && !ReplayCanonicalJsonV17.TryCanonicalizeJsonPayload("{\"a\":1,\"a\":2}", out _),
            "extension JSON canonicalization rejects duplicate keys");
    }

    private static void TestCheckpointSeek(ReplayDocumentEnvelopeV17 envelope)
    {
        var checkpoint = envelope.Document.TruthCheckpoints.First();
        var truthSequence = envelope.Document.TruthEvents
            .Where(item => item.Sequence <= checkpoint.EventSequence)
            .Select(item => item.Sequence)
            .DefaultIfEmpty(0L)
            .Max();
        var reducer = new ReplayStateReducerV17();
        reducer.Reset(checkpoint.State, truthSequence);
        foreach (var value in envelope.Document.TruthEvents.Where(item => item.Sequence > checkpoint.EventSequence))
            reducer.Apply(value);
        Assert(ReplayCanonicalJsonV17.StateHash(reducer.Current)
               == envelope.Document.Header.FinalVisibleStateSha256,
            "checkpoint seek reaches the same final visible state without executing gameplay");

        var forged = ReplayCanonicalJsonV17.Clone(envelope);
        forged.Document.PresentationCheckpoints[0].EntityBindings.Clear();
        forged.Document.PresentationCheckpoints[0].CheckpointSha256 =
            ReplayCanonicalJsonV17.PresentationCheckpointHash(forged.Document.PresentationCheckpoints[0]);
        forged.Document.Header.PresentationRoot = ReplayCanonicalJsonV17.PresentationRoot(forged.Document);
        forged.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(forged.Document.Header);
        Assert(ReplayDocumentValidatorV17.Validate(forged).Errors.Any(item =>
                item.StartsWith("checkpoint-projection-invalid", StringComparison.Ordinal)),
            "a self-rehashed checkpoint still cannot diverge from deterministic instruction projection");
    }

    private static void TestChunkAndPayloadBoundaries(ReplayDocumentEnvelopeV17 envelope)
    {
        var chunks = ReplayJournalChunkerV17.Build(
            ReplayJournalLanesV17.Truth,
            envelope.Document.TruthEvents,
            ReplayJournalChunkerV17.MinimumTargetBytes);
        var decoded = ReplayJournalChunkerV17.Decode(ReplayJournalLanesV17.Truth, chunks);
        Assert(decoded.Select(item => item.Sequence).SequenceEqual(
                   envelope.Document.TruthEvents.Select(item => item.Sequence)),
            "incremental journal chunks round-trip in strict lane order");
        var corrupt = chunks.Select(ReplayCanonicalJsonV17.Clone).ToList();
        corrupt[0].Payload[0] ^= 0x01;
        var corruptRejected = false;
        try { ReplayJournalChunkerV17.Decode(ReplayJournalLanesV17.Truth, corrupt); }
        catch (InvalidDataException) { corruptRejected = true; }
        Assert(corruptRejected, "incremental journal chunks reject payload tampering");

        var strictUnknownRejected = false;
        try { ReplayCanonicalJsonV17.DeserializeStrict<ReplayVisibleStateV17>("{\"UnknownField\":1}"); }
        catch { strictUnknownRejected = true; }
        Assert(strictUnknownRejected, "v17 strict decoding rejects unknown schema fields");

        var payload = ReplayPayloadV17.Encode(envelope);
        var payloadRoundTrip = ReplayPayloadV17.Decode<ReplayDocumentEnvelopeV17>(payload);
        Assert(payloadRoundTrip.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot,
            "compressed v17 envelopes round-trip without a second compatibility shape");
    }

    private static void TestCausalLedgerAndStableBarrier()
    {
        var ledger = new ReplayTransactionLedgerV17();
        ledger.Begin("parent", ReplayTransactionKindsV17.Card, "player", "card-a");
        ledger.Begin("child", ReplayTransactionKindsV17.Passive, "player", "buff-a", "parent");
        Assert(ledger.TryBindActionPresentation("player", "card-a", out var bound, out _)
               && bound == "parent"
               && !ledger.TryBindActionPresentation("player", "buff-a", out _, out var passiveRejection)
               && passiveRejection == "no-open-transaction",
            "action presentation binding ignores passive/system transactions and requires exact source identity");
        ledger.Begin("parallel", ReplayTransactionKindsV17.Card, "player", "card-b");
        Assert(!ledger.TryBindActionPresentation("player", "", out _, out var ambiguity)
               && ambiguity == "ambiguous-causal-ownership",
            "two real action transactions for one actor remain explicitly ambiguous without a source identity");
        ledger.MarkSourceCompleted("parallel", 4);
        foreach (var readyParallel in ledger.ObserveStableBarrier(4).Where(item => item == "parallel"))
            ledger.Complete(readyParallel);
        ledger.MarkSourceCompleted("child", 4);
        ledger.MarkSourceCompleted("parent", 4);
        Assert(ledger.ObserveStableBarrier(4).SequenceEqual(new[] { "child" }),
            "a stable barrier drains the completed nested transaction first");
        ledger.Complete("child");
        Assert(ledger.ObserveStableBarrier(5).Single() == "parent",
            "the parent drains only after its child and a later stable-state observation");
        ledger.Complete("parent");

        var barriers = new ReplayStableBarrierCoordinatorV17();
        Assert(barriers.Request("card-completed", needsStateCapture: true)
               && !barriers.Request("skill-completed", needsStateCapture: true)
               && barriers.TryTake(out var batch)
               && batch.CaptureState
               && batch.Reasons.SequenceEqual(new[] { "card-completed", "skill-completed" }),
            "stable-state observation coalesces redundant frame requests into one incremental capture");

        var terminalLedger = new ReplayTransactionLedgerV17();
        terminalLedger.Begin("terminal-action", ReplayTransactionKindsV17.Card, "player", "card-final");
        terminalLedger.Begin("terminal-audio-1", ReplayTransactionKindsV17.SystemPhase, "player", "");
        terminalLedger.Begin("terminal-audio-2", ReplayTransactionKindsV17.SystemPhase, "player", "");
        var terminalSealed = terminalLedger.SealSourcesAtTerminal(9);
        var terminalDrainOrder = new List<string>();
        while (true)
        {
            var readyAtTerminal = terminalLedger.ObserveStableBarrier(9);
            if (readyAtTerminal.Count == 0) break;
            foreach (var transactionId in readyAtTerminal)
            {
                terminalLedger.Complete(transactionId);
                terminalDrainOrder.Add(transactionId);
            }
        }
        Assert(terminalSealed.SequenceEqual(new[]
               {
                   "terminal-action", "terminal-audio-1", "terminal-audio-2"
               })
               && terminalDrainOrder.SequenceEqual(terminalSealed)
               && terminalLedger.OpenCount == 0,
            "BattleFinalized seals still-open producers and drains the final action plus unrelated audio transactions without aborting them");

        var terminalBuilder = new ReplayJournalBuilderV17(new ReplayDocumentHeaderCoreV17
        {
            RecordId = "terminal-window",
            BattleSessionId = "terminal-window",
            PerspectivePlayerId = "player"
        }, new ReplayVisibleStateV17
        {
            LevelId = "level",
            PerspectivePlayerId = "player",
            RoundSequence = 1,
            ActorTurnSequence = 1,
            ActiveActorId = "actor"
        });
        var terminalDocumentLedger = new ReplayTransactionLedgerV17();
        var finalAction = terminalBuilder.StartTransaction(
            ReplayTransactionKindsV17.Card, 1, 1, 1, "actor", "final-card", "card:final");
        terminalDocumentLedger.Begin(finalAction, ReplayTransactionKindsV17.Card, "actor", "final-card");
        terminalBuilder.AddTruthMarker(finalAction, ReplayEventTypesV17.ActorTurnStarted, 2, "actor");
        terminalBuilder.AddPresentation(finalAction, ReplayEventTypesV17.SourcePresented,
            new ReplayPresentationMessageV17
            {
                DescriptorId = "card:final", ActorId = "actor", SourceInstanceId = "final-card"
            }, 3, "actor");
        terminalBuilder.AddPresentation(finalAction, ReplayEventTypesV17.ActorAnimationPresented,
            new ReplayPresentationMessageV17
            {
                ActorId = "actor", AnimationState = "Attack", DurationTicks = 1
            }, 4, "actor");
        for (var index = 0; index < 4; index++)
        {
            var audioTransaction = terminalBuilder.StartTransaction(
                ReplayTransactionKindsV17.SystemPhase, 5 + index, 1, 1, "actor", label: "audio-" + index);
            terminalDocumentLedger.Begin(audioTransaction, ReplayTransactionKindsV17.SystemPhase, "actor", "");
        }
        terminalDocumentLedger.SealSourcesAtTerminal(3);
        var terminalTicks = 20L;
        while (true)
        {
            var readyAtTerminal = terminalDocumentLedger.ObserveStableBarrier(3);
            if (readyAtTerminal.Count == 0) break;
            foreach (var transactionId in readyAtTerminal)
            {
                if (transactionId == finalAction)
                    terminalBuilder.AddTruthMarker(
                        transactionId, ReplayEventTypesV17.ActorTurnCompleted, terminalTicks++, "actor");
                terminalBuilder.CompleteTransaction(transactionId, terminalTicks++);
                terminalDocumentLedger.Complete(transactionId);
            }
        }
        Assert(terminalDocumentLedger.OpenCount == 0
               && !terminalBuilder.Document.TruthEvents.Any(item =>
                   item.EventType == ReplayEventTypesV17.TransactionAborted)
               && terminalBuilder.Document.TruthEvents.Count(item =>
                   item.TransactionId == finalAction
                   && item.EventType == ReplayEventTypesV17.ActorTurnStarted) == 1
               && terminalBuilder.Document.TruthEvents.Count(item =>
                   item.TransactionId == finalAction
                   && item.EventType == ReplayEventTypesV17.ActorTurnCompleted) == 1,
            "the reproduced last-card plus four-audio terminal window closes as normal causal history with no aborted transaction");

        var assembled = new ReplayCanonicalChunkBufferV17(
            new string('a', 64), "transfer", 3, 6, new string('b', 64));
        Assert(assembled.TrySet(2, new byte[] { 5, 6 }, 2)
               && assembled.TrySet(0, new byte[] { 1, 2 }, 2)
               && assembled.TrySet(1, new byte[] { 3, 4 }, 2)
               && assembled.IsComplete
               && assembled.Join().SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }),
            "network replication accepts out-of-order canonical chunks and rejoins exact bytes");
    }

    internal static ReplayDocumentEnvelopeV17 BuildReplayV17(string recordId = "record-v17")
    {
        var initial = new ReplayVisibleStateV17
        {
            LevelId = "level-test",
            PerspectivePlayerId = "local-player",
            BattlePhase = "Materialized",
            RoundSequence = 1,
            ActorTurnSequence = 1,
            ActiveActorId = "player-entity",
            Entities = new List<ReplayEntityStateV17>
            {
                new()
                {
                    EntityId = "player-entity",
                    DescriptorId = "entity:Witch:player",
                    SpawnGeneration = 1,
                    Team = ReplayTeamsV17.Friendly,
                    OwnerPlayerId = "local-player",
                    SlotIndex = 0,
                    MaxHp = 100,
                    CurrentHp = 100
                },
                new()
                {
                    EntityId = "enemy-entity",
                    DescriptorId = "entity:Witch:enemy",
                    SpawnGeneration = 1,
                    Team = ReplayTeamsV17.Enemy,
                    SlotIndex = 0,
                    MaxHp = 100,
                    CurrentHp = 100
                }
            },
            Cards = new List<ReplayVisibleCardStateV17>
            {
                new()
                {
                    CardInstanceId = "card-instance-1",
                    DescriptorId = "card:Witch:strike",
                    OwnerPlayerId = "local-player",
                    Zone = "Hand",
                    Order = 0,
                    DisplayedCost = 1,
                    IsRevealed = true,
                    HasMeasuredLayout = true,
                    CanvasPosition = new ReplayVector2Q16V17 { X = 0, Y = 120 * 65_536 },
                    CanvasSize = new ReplayVector2Q16V17 { X = 300 * 65_536, Y = 460 * 65_536 },
                    LocalScale = new ReplayVector3Q16V17 { X = 32_768, Y = 32_768, Z = 65_536 }
                }
            },
            ZoneCounts = new List<ReplayVisibleZoneCountV17>
            {
                new() { OwnerPlayerId = "local-player", Zone = "Draw", Count = 4 },
                new() { OwnerPlayerId = "local-player", Zone = "Discard", Count = 0 },
                new() { OwnerPlayerId = "local-player", Zone = "Hand", Count = 1 }
            },
            Resources = new List<ReplayVisibleResourceStateV17>
            {
                new() { OwnerPlayerId = "local-player", ResourceId = "Power", Value = 3, Maximum = 3, DisplayText = "3/3" }
            },
            Intents = new List<ReplayIntentStateV17>
            {
                new()
                {
                    IntentInstanceId = "enemycard-Toxin1-instance",
                    ActorId = "enemy-entity",
                    DescriptorId = "intent:enemycard:enemycard_Toxin1",
                    SlotIndex = 0,
                    DisplayValue = "3",
                    TargetIds = new List<string> { "player-entity" }
                }
            },
            Extensions = new List<ReplayVisibleExtensionStateV17>
            {
                new()
                {
                    OwnerModId = "Terrias",
                    TypeId = "Spirit",
                    InstanceId = "spirit-system",
                    SchemaVersion = 1,
                    DisplayText = "精灵 Lv.1",
                    PayloadJson = "{\"deployed\":1}"
                }
            }
        };
        var header = new ReplayDocumentHeaderCoreV17
        {
            RecordId = recordId,
            BattleSessionId = recordId,
            PerspectivePlayerId = "local-player",
            PerspectiveKind = "Player",
            LevelId = initial.LevelId,
            BattleTitle = "Replay fixture",
            StartedUtc = "2026-08-29T00:00:00.0000000Z",
            EndedUtc = "2026-08-29T00:01:00.0000000Z",
            Result = "Win",
            GameBuildProvenance = "test+build",
            RecorderBuild = "test-recorder"
        };
        var builder = new ReplayJournalBuilderV17(header, initial);
        builder.Document.Presentation = BuildPresentationCapsule();
        var background = ReplayTestPngBytes();
        var backgroundHash = ReplayCanonicalJsonV17.Sha256(background);
        builder.Document.Presentation.Scene.BackgroundAssetSha256 = backgroundHash;
        builder.Document.Assets.Add(new ReplayAssetV17
        {
            Sha256 = backgroundHash,
            MediaType = "image/png",
            Extension = ".png",
            Usage = "Scene.Background.Fallback",
            ByteLength = background.Length,
            Width = 1,
            Height = 1,
            Payload = background
        });

        var bootstrap = builder.StartTransaction(ReplayTransactionKindsV17.Bootstrap, 0, 1, 1, "player-entity");
        builder.AddTruthMarker(bootstrap, ReplayEventTypesV17.BattleMaterialized, 0, "player-entity");
        AddEntityPresentation(builder, bootstrap, initial.Entities[0], -4f, flipX: true, 0);
        AddEntityPresentation(builder, bootstrap, initial.Entities[1], 3f, flipX: false, 0, "player-entity");
        builder.CompleteTransaction(bootstrap, 1);

        var start = builder.StartTransaction(ReplayTransactionKindsV17.SystemPhase, 10, 1, 1, "player-entity");
        builder.AddTruthMarker(start, ReplayEventTypesV17.FightStartSignaled, 10, "player-entity");
        builder.CompleteTransaction(start, 11);

        var round = builder.StartTransaction(ReplayTransactionKindsV17.SystemPhase, 20, 1, 1, "player-entity");
        builder.AddTruthMarker(round, ReplayEventTypesV17.RoundStarted, 20, "player-entity");
        builder.AddPresentation(round, ReplayEventTypesV17.TurnTransitionPresented,
            new ReplayPresentationMessageV17 { Kind = "RoundStart", DisplayText = "Round 1", DurationTicks = 200_000 },
            20,
            "player-entity");
        builder.CompleteTransaction(round, 21);

        var action = builder.StartTransaction(
            ReplayTransactionKindsV17.Card,
            30,
            1,
            1,
            "player-entity",
            "card-instance-1",
            "card:Witch:strike",
            "Strike",
            "local-player");
        builder.AddTruthMarker(action, ReplayEventTypesV17.ActorTurnStarted, 30, "player-entity");
        builder.AddPresentation(action, ReplayEventTypesV17.SourcePresented,
            new ReplayPresentationMessageV17
            {
                Kind = "Card",
                DescriptorId = "card:Witch:strike",
                ActorId = "player-entity",
                SourceInstanceId = "card-instance-1",
                SourceZone = "Hand",
                SourceSlot = 0,
                Phase = ReplayPresentationPhasesV17.SourceFocus,
                PhaseOrdinal = 0,
                DurationTicks = 1
            }, 31, "player-entity");
        builder.AddPresentation(action, ReplayEventTypesV17.CardMotionPresented,
            new ReplayPresentationMessageV17
            {
                Kind = "CardUse",
                DescriptorId = "card:Witch:strike",
                ActorId = "player-entity",
                SourceInstanceId = "card-instance-1",
                Phase = ReplayPresentationPhasesV17.CardTravel,
                PhaseOrdinal = 1,
                DurationTicks = 300_000,
                TransformSamples = new List<ReplayTransformSampleV17>
                {
                    new()
                    {
                        OffsetTicks = 0,
                        CanvasPosition = new ReplayVector2Q16V17 { X = 0, Y = 120 * 65_536 },
                        CanvasSize = new ReplayVector2Q16V17 { X = 300 * 65_536, Y = 460 * 65_536 },
                        LocalScale = new ReplayVector3Q16V17 { X = 32_768, Y = 32_768, Z = 65_536 }
                    },
                    new()
                    {
                        OffsetTicks = 300_000,
                        CanvasPosition = new ReplayVector2Q16V17 { X = 760 * 65_536, Y = 90 * 65_536 },
                        CanvasSize = new ReplayVector2Q16V17 { X = 300 * 65_536, Y = 460 * 65_536 },
                        LocalScale = new ReplayVector3Q16V17 { X = 8_192, Y = 8_192, Z = 65_536 },
                        AlphaQ16 = 0
                    }
                }
            }, 32, "player-entity");
        builder.AddPresentation(action, ReplayEventTypesV17.ActorAnimationPresented,
            new ReplayPresentationMessageV17
            {
                Kind = "Action",
                ActorId = "player-entity",
                AnimationState = "Attack",
                Phase = ReplayPresentationPhasesV17.ActorFocus,
                PhaseOrdinal = 2,
                DurationTicks = 300_000,
                WorldTransformSamples = new List<ReplayWorldTransformSampleV17>
                {
                    new()
                    {
                        OffsetTicks = 0,
                        WorldPosition = new ReplayVector3Q16V17 { X = -4 * 65_536, Y = -65_536 },
                        RootScale = ReplayVector3Q16V17.One(),
                        BodyLocalScale = ReplayVector3Q16V17.One(),
                        SortingOrder = 0
                    },
                    new()
                    {
                        OffsetTicks = 150_000,
                        WorldPosition = new ReplayVector3Q16V17 { X = -65_536, Y = 0 },
                        RootScale = new ReplayVector3Q16V17 { X = 117_965, Y = 117_965, Z = 65_536 },
                        BodyLocalScale = ReplayVector3Q16V17.One(),
                        SortingOrder = 13
                    }
                }
            }, 33, "player-entity");
        builder.AddPresentation(action, ReplayEventTypesV17.ExtensionPresented,
            new ReplayPresentationMessageV17
            {
                Kind = "OwnerAttachedFocus",
                ActorId = "enemy-entity",
                OwnerEntityId = "player-entity",
                TargetIds = new List<string> { "player-entity" },
                ExtensionOwnerModId = "FixtureMod",
                ExtensionTypeId = "SpiritBattlePresentation",
                ExtensionSchemaVersion = 1,
                ExtensionEventId = "fixture-spirit-focus-1",
                ExtensionPayloadJson = "{\"peakScaleQ16\":73400,\"travelPixels\":70}",
                Phase = ReplayPresentationPhasesV17.Impact,
                PhaseOrdinal = 3,
                DurationTicks = 400_000
            }, 33, "enemy-entity");
        builder.AddPresentation(action, ReplayEventTypesV17.DamageTextPresented,
            new ReplayPresentationMessageV17
            {
                Kind = "Damage",
                ActorId = "enemy-entity",
                TargetIds = new List<string> { "enemy-entity" },
                DisplayText = "30",
                Value = 30,
                Phase = ReplayPresentationPhasesV17.Impact,
                PhaseOrdinal = 3,
                DurationTicks = 400_000
            }, 34, "enemy-entity");
        var afterAction = ReplayCanonicalJsonV17.Clone(initial);
        afterAction.Entities.Single(item => item.EntityId == "enemy-entity").CurrentHp = 70;
        afterAction.Cards[0].Zone = "Discard";
        afterAction.ZoneCounts.Single(item => item.Zone == "Hand").Count = 0;
        afterAction.ZoneCounts.Single(item => item.Zone == "Discard").Count = 1;
        afterAction.Resources[0].Value = 2;
        afterAction.Resources[0].DisplayText = "2/3";
        var actionStateEvents = builder.ApplyObservedState(action, afterAction, 35);
        foreach (var delta in actionStateEvents.Where(item => item.EventType == ReplayEventTypesV17.StateDeltaApplied))
            builder.AddPresentation(action, ReplayEventTypesV17.VisualStateCommitted,
                new ReplayPresentationMessageV17
                {
                    Kind = "VisibleStateCommit",
                    ActorId = "player-entity",
                    Phase = ReplayPresentationPhasesV17.StateCommit,
                    PhaseOrdinal = 4,
                    TruthEventSequence = delta.Sequence,
                    DurationTicks = 1
                }, 35, "player-entity");
        builder.AddTruthMarker(action, ReplayEventTypesV17.ActorTurnCompleted, 36, "player-entity");
        builder.CompleteTransaction(action, 37);

        var outcome = builder.StartTransaction(ReplayTransactionKindsV17.Outcome, 50, 1, 1, "player-entity");
        var final = ReplayCanonicalJsonV17.Clone(afterAction);
        final.BattlePhase = "Finalized";
        final.Outcome = "Win";
        builder.ApplyObservedState(outcome, final, 50);
        builder.AddTruthMarker(outcome, ReplayEventTypesV17.OutcomeEntering, 51, "player-entity");
        builder.AddTruthMarker(outcome, ReplayEventTypesV17.BattleFinalized, 52, "player-entity");
        builder.CompleteTransaction(outcome, 53);
        return new ReplayDocumentEnvelopeV17 { Document = builder.Document };
    }

    private static ReplayPresentationCapsuleV17 BuildPresentationCapsule()
    {
        var provenance = new ReplayContentProvenanceV17
        {
            OwnerModId = "Witch",
            SourceVersion = "test+build"
        };
        return new ReplayPresentationCapsuleV17
        {
            Scene = new ReplaySceneDescriptorV17
            {
                DescriptorId = "scene",
                SceneResourceId = "TestBattle",
                SceneResourcePath = "TestBattle",
                ReferenceWidth = 1920,
                ReferenceHeight = 1080,
                CameraOrthographicSizeQ16 = 5 * 65_536
            },
            Modules = new List<ReplayPresentationModuleRequirementV17>
            {
                new()
                {
                    OwnerModId = "FixtureMod",
                    TypeId = "SpiritBattlePresentation",
                    SchemaVersion = 1,
                    Portability = "Portable",
                    BuildIdentity = "fixture",
                    RendererCapability = "owner-attached-spirit.v1"
                }
            },
            Entities = new List<ReplayEntityDescriptorV17>
            {
                new()
                {
                    DescriptorId = "entity:Witch:player",
                    Archetype = ReplayEntityArchetypesV17.PlayerCombatant,
                    Provenance = ReplayCanonicalJsonV17.Clone(provenance),
                    Name = "Player",
                    NativePrefabResourcePath = "DollAni/Test/Player",
                    IdleResourcePath = "Animation/Test/Player/Idle",
                    Animations = new List<ReplayAnimationDescriptorV17>
                    {
                        new()
                        {
                            State = "Idle", ResourcePath = "Animation/Test/Player/Idle", Loop = true,
                            FrameNames = new List<string> { "PlayerIdle1" }
                        },
                        new()
                        {
                            State = "Attack", ResourcePath = "Animation/Test/Player/Attack", Loop = false,
                            FrameNames = new List<string> { "PlayerAttack" }
                        }
                    }
                },
                new()
                {
                    DescriptorId = "entity:Witch:enemy",
                    Archetype = ReplayEntityArchetypesV17.EnemyCombatant,
                    Provenance = ReplayCanonicalJsonV17.Clone(provenance),
                    Name = "Enemy",
                    NativePrefabResourcePath = "Enemy/Test",
                    IdleResourcePath = "Animation/Test/Enemy/Idle",
                    Animations = new List<ReplayAnimationDescriptorV17>
                    {
                        new()
                        {
                            State = "Idle", ResourcePath = "Animation/Test/Enemy/Idle", Loop = true,
                            FrameNames = new List<string> { "EnemyIdle1" }
                        },
                        new()
                        {
                            State = "Hit", ResourcePath = "Animation/Test/Enemy/Hit", Loop = false,
                            FrameNames = new List<string> { "EnemyHit", "EnemyHit" }
                        }
                    }
                }
            },
            Cards = new List<ReplayCardDescriptorV17>
            {
                new()
                {
                    DescriptorId = "card:Witch:strike",
                    Provenance = new ReplayContentProvenanceV17
                    {
                        OwnerModId = "Witch",
                        ContentKind = "Card",
                        StableContentId = "strike",
                        SourceVersion = "test+build"
                    },
                    Name = "Strike",
                    NativeCardType = "Common",
                    NativeResourcePath = "UI/CardItem",
                    IconResourcePath = "Icon/Card/Strike",
                    FrameResourcePath = "Icon/CardTemplate/NewTemplate/铜卡"
                }
            },
            Intents = new List<ReplayIntentDescriptorV17>
            {
                new()
                {
                    DescriptorId = "intent:enemycard:enemycard_Toxin1",
                    Provenance = new ReplayContentProvenanceV17
                    {
                        OwnerModId = "Witch",
                        ContentKind = "Intent",
                        StableContentId = "enemycard_Toxin1",
                        SourceVersion = "test+build"
                    },
                    Name = "Decay Breath I",
                    IconResourcePath = ReplayIntentVisualContractV17.DefaultIconResourcePath,
                    BackIconResourcePath = "Icon/ActionIcon/负面底"
                }
            }
        };
    }

    private static void AddEntityPresentation(
        ReplayJournalBuilderV17 builder,
        string transactionId,
        ReplayEntityStateV17 entity,
        float worldX,
        bool flipX,
        long ticks,
        string customOwnerEntityId = "")
    {
        builder.AddPresentation(transactionId, ReplayEventTypesV17.EntityPresented,
            new ReplayPresentationMessageV17
            {
                Kind = "Entity",
                ActorId = entity.EntityId,
                EntityBinding = new ReplayEntityPresentationBindingV17
                {
                    EntityId = entity.EntityId,
                    SpawnGeneration = entity.SpawnGeneration,
                    DescriptorId = entity.DescriptorId,
                    HasMeasuredLayout = true,
                    WorldPosition = new ReplayVector3Q16V17
                    {
                        X = (int)(worldX * 65_536),
                        Y = -65_536,
                        Z = 0
                    },
                    RootScale = ReplayVector3Q16V17.One(),
                    BodyLocalScale = new ReplayVector3Q16V17
                    {
                        X = flipX ? -65_536 : 65_536,
                        Y = 65_536,
                        Z = 65_536
                    },
                    HeadLocalPosition = new ReplayVector3Q16V17 { Y = 2 * 65_536 },
                    BottomLocalPosition = new ReplayVector3Q16V17 { Y = -65_536 },
                    CenterLocalPosition = new ReplayVector3Q16V17 { Y = 32_768 },
                    StatusBarSize = new ReplayVector2Q16V17 { X = 280 * 65_536, Y = 78 * 65_536 },
                    HudScaleQ16 = 65_536,
                    SortingLayerName = "Default",
                    FlipX = flipX,
                    CustomPresentation = string.IsNullOrWhiteSpace(customOwnerEntityId)
                        ? null
                        : new ReplayCustomEntityPresentationV17
                        {
                            OwnerModId = "FixtureMod",
                            SchemaVersion = 1,
                            PresentationMode = "OwnerAttachedProxy",
                            OwnerEntityId = customOwnerEntityId,
                            ReferenceHeightPixels = 120,
                            HorizontalOverlapQ16 = 21_845,
                            SortingOrderOffset = -1,
                            HudMode = "DetachedRightVertical",
                            HudScaleQ16 = 47_186,
                            HudRotationQ16 = -90 * 65_536,
                            BadgeIconResourcePath = "Icon/Element/Fire",
                            BadgeText = "Fire",
                            AttackFocusTravelPixels = 70,
                            InterferenceFocusTravelPixels = 45,
                            SupportFocusTravelPixels = 12
                        }
                }
            }, ticks, entity.EntityId);
    }

    internal static byte[] ReplayTestPngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlZSmQAAAAASUVORK5CYII=");

    private sealed class ReplayFrameCandidate
    {
        internal ReplayFrameCandidate(string identity, string name)
        {
            Identity = identity;
            Name = name;
        }

        internal string Identity { get; }
        internal string Name { get; }
    }
}
