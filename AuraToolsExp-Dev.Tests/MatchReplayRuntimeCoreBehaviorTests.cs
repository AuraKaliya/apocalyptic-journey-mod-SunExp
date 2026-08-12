using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.Settings;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayRuntimeCore()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-ReplayBuffer-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var buffer = new MatchReplayWorkingBuffer(32 * 1024, 64 * 1024, root))
            {
                var random = new Random(8122026);
                for (var index = 1; index <= 600; index++)
                {
                    var payload = new byte[2048];
                    random.NextBytes(payload);
                    buffer.Add(new MatchReplayEvent
                    {
                        Sequence = index,
                        TurnIndex = 1 + index / 20,
                        Kind = "Synthetic",
                        TypeName = "Frame." + index,
                        Payload = payload
                    });
                }

                Assert(buffer.EventCount == 600 && buffer.ChunkCount > 10,
                    "ordinary replay buffers still support bounded incremental chunks");
                Assert(buffer.BufferedBytes < 128 * 1024,
                    "ordinary replay buffers keep compressed chunks within their memory budget");
                Assert(Directory.Exists(root) && Directory.GetFiles(root, "*.work").Length > 0,
                    "ordinary buffers can spill compressed chunks to temporary storage");
                var chunks = buffer.Complete();
                Assert(MatchReplayChunker.Decode(chunks).Count == 600,
                    "spilled replay chunks reconstruct the complete stream");
            }

            Assert(!Directory.Exists(root), "disposing a replay buffer removes its temporary work directory");

            var deferredRoot = root + "-deferred";
            using (var buffer = new MatchReplayWorkingBuffer(
                       32 * 1024,
                       64 * 1024,
                       deferredRoot,
                       deferCompression: true))
            {
                for (var index = 1; index <= 100; index++)
                {
                    buffer.Add(new MatchReplayEvent
                    {
                        Sequence = index,
                        TurnIndex = 1,
                        Kind = MatchReplayEventKinds.ActionFrame,
                        Payload = new byte[2048]
                    });
                }

                Assert(buffer.ChunkCount == 1 && !Directory.Exists(deferredRoot),
                    "live v8 recording defers compression and file IO until detached finalization");
                Assert(buffer.Complete().Count > 1,
                    "deferred finalization still emits bounded persisted chunks");
            }

            var baseline = State(1, hp: 20, power: 3, handCardIds: new[] { "card-a", "card-b" });
            var afterFirst = State(1, hp: 14, power: 2, handCardIds: new[] { "card-b" });
            var secondTurn = State(2, hp: 14, power: 3, handCardIds: new[] { "card-b", "card-c" });
            var afterSecond = State(2, hp: 9, power: 1, handCardIds: new[] { "card-c" });
            var first = new List<MatchReplayEvent>
            {
                TurnFrameEvent(1, baseline),
                CheckpointEvent(2, baseline, 0),
                ActionFrameEvent(3, 1, "action-1", 1, baseline, afterFirst, "card-a", 6),
                TurnFrameEvent(4, secondTurn),
                CheckpointEvent(5, secondTurn, 1),
                ActionFrameEvent(6, 2, "action-2", 2, secondTurn, afterSecond, "card-b", 5)
            };
            var second = first.Select(item => new MatchReplayEvent
            {
                Sequence = item.Sequence,
                TurnIndex = item.TurnIndex,
                ElapsedMilliseconds = item.ElapsedMilliseconds * 1000,
                Kind = item.Kind,
                Semantic = item.Semantic,
                TurnFrame = item.TurnFrame,
                ActionFrame = item.ActionFrame,
                SeekCheckpoint = item.SeekCheckpoint
            }).ToList();
            Assert(MatchReplayPresentationSchedule.Build(first, MatchReplayPresentationModes.Standard)
                    .SequenceEqual(MatchReplayPresentationSchedule.Build(second, MatchReplayPresentationModes.Standard)),
                "v8 presentation scheduling ignores original player and network wait intervals");
            Assert(MatchReplayPresentationSchedule.Build(first, MatchReplayPresentationModes.Compact)[^1]
                   < MatchReplayPresentationSchedule.Build(first, MatchReplayPresentationModes.Showcase)[^1],
                "presentation presets only scale deterministic cue duration and action gaps");
            var standardTimeline = MatchReplayPresentationSchedule.Build(first, MatchReplayPresentationModes.Standard);
            Assert(standardTimeline[2] == standardTimeline[1]
                   && standardTimeline[3] - standardTimeline[2]
                   == MatchReplayPresentationSchedule.Duration(first[2], MatchReplayPresentationModes.Standard)
                      + MatchReplayPresentationSchedule.Gap(MatchReplayPresentationModes.Standard),
                "card use, actor animation, damage, buff, and state changes share one action frame with gaps only between actions");

            var legacy = new MatchRecord { ReplayProtocol = 7 };
            Assert(!MatchReplayCompatibility.Evaluate(legacy, first).CanPlay,
                "pre-release v7 projections are intentionally analysis-only after the v8 visual contract ships");
            var contextual = new MatchRecord
            {
                ReplayProtocol = MatchReplayProtocol.Version,
                InitialState = new MatchReplayInitialState
                {
                    DiceJson = "{\"_cursor\":{\"val\":7}}",
                    BaselineState = baseline
                },
                RequiredCapabilities = new List<string>
                {
                    MatchReplayCapabilities.AuthoritativeFramesV1,
                    MatchReplayCapabilities.StateProjectionV1,
                    MatchReplayCapabilities.PresentationTimelineV1,
                    MatchReplayCapabilities.IndexedSeekV1,
                    MatchReplayCapabilities.CardPresentationReadyV1,
                    MatchReplayCapabilities.IncrementalHandV1,
                    MatchReplayCapabilities.OutcomeCuesV1,
                    MatchReplayCapabilities.PassiveHudV1
                }
            };
            Assert(MatchReplayCompatibility.Evaluate(contextual, first).Level
                   == MatchReplayCompatibilityLevels.Compatible,
                "the current authoritative-frame stream is compatible without a build fingerprint gate");
            var savedTransitions = first[2].ActionFrame!.CardTransitions;
            first[2].ActionFrame!.CardTransitions = new List<MatchReplayCardTransition>();
            Assert(!MatchReplayCompatibility.Evaluate(contextual, first).CanPlay,
                "v8 refuses a card-changing action frame that omitted its identity transitions");
            first[2].ActionFrame!.CardTransitions = savedTransitions;
            var commandReplay = first.Concat(new[]
            {
                new MatchReplayEvent
                {
                    Sequence = 7,
                    TurnIndex = 2,
                    Kind = MatchReplayEventKinds.ActionCommand
                }
            });
            Assert(!MatchReplayCompatibility.Evaluate(contextual, commandReplay).CanPlay,
                "v8 rejects raw combat commands instead of silently re-entering simulation");
            contextual.RequiredCapabilities.Add("future-required-capability");
            Assert(!MatchReplayCompatibility.Evaluate(contextual, first).CanPlay,
                "an unknown required data capability blocks playback while retaining analysis access");

            var actions = MatchReplayActionTimeline.Build(first);
            Assert(actions.Count == 2
                   && actions.Actions[0].BeginEventIndex == 2
                   && actions.Actions[0].EndEventIndex == 2
                   && actions.Actions[0].RestoreEventIndex == 2
                   && actions.EventIndexForCompletedActions(1, first.Count) == 3,
                "one authoritative action frame is one progress and seek transaction");
            Assert(actions.CompletedActionsAtEventIndex(2) == 0
                   && actions.CompletedActionsAtEventIndex(3) == 1,
                "progress commits immediately after the matching action frame");

            var readModel = new MatchReplayReadModel();
            readModel.Reset(baseline);
            readModel.Apply(first[2].ActionFrame!.Delta);
            Assert(MatchReplayProjectionState.Hash(readModel.Current)
                   == MatchReplayProjectionState.Hash(afterFirst),
                "the read model reaches the recorded authoritative state without executing combat logic");
            readModel.Reset(baseline);
            readModel.Apply(first[2].ActionFrame!.Delta);
            var firstSeekHash = MatchReplayProjectionState.Hash(readModel.Current);
            readModel.Reset(baseline);
            readModel.Apply(first[2].ActionFrame!.Delta);
            Assert(firstSeekHash == MatchReplayProjectionState.Hash(readModel.Current),
                "repeated seek reconstruction is idempotent and cannot accumulate state drift");
            Assert(MatchReplayActionBoundaryPolicy.ShouldNest(false)
                   && !MatchReplayActionBoundaryPolicy.ShouldNest(true),
                "unfinished sub-hooks stay in one root action while every later completed card use starts a new action frame");

            var stableConvergence = new MatchReplayActionConvergenceTracker();
            Assert(stableConvergence.Observe("state-a") == MatchReplayActionFinalizationDecision.Observe
                   && stableConvergence.Observe("state-a") == MatchReplayActionFinalizationDecision.Observe
                   && stableConvergence.Observe("state-a") == MatchReplayActionFinalizationDecision.FinalizeStable,
                "an action finalizes after a short minimum window with two matching authoritative projections");
            var delayedConvergence = new MatchReplayActionConvergenceTracker();
            Assert(delayedConvergence.Observe("state-a") == MatchReplayActionFinalizationDecision.Observe
                   && delayedConvergence.Observe("state-a") == MatchReplayActionFinalizationDecision.Observe
                   && delayedConvergence.Observe("state-b") == MatchReplayActionFinalizationDecision.Observe
                   && delayedConvergence.Observe("state-b") == MatchReplayActionFinalizationDecision.FinalizeStable,
                "a next-frame state consequence resets convergence and is captured in the same action frame");
            var deadlineConvergence = new MatchReplayActionConvergenceTracker();
            var deadlineDecision = MatchReplayActionFinalizationDecision.Observe;
            for (var observation = 0;
                 observation < MatchReplayActionConvergenceTracker.MaximumObservations;
                 observation++)
            {
                deadlineDecision = deadlineConvergence.Observe("changing-state-" + observation);
            }
            Assert(deadlineDecision == MatchReplayActionFinalizationDecision.FinalizeDeadline
                   && deadlineConvergence.ObservationCount
                   == MatchReplayActionConvergenceTracker.MaximumObservations,
                "continuously changing projection state reaches a bounded deadline instead of scheduling 120 frame retries");

            var derivedAfter = State(1, hp: 14, power: 2, handCardIds: new[] { "card-b", "card-c" });
            derivedAfter.Statuses[0].Defend = 4;
            derivedAfter.Statuses[0].Buffs.Add(new MatchReplayBuffState
            {
                BuffId = "buff-focus",
                Level = 2,
                UpperBound = 9
            });
            var sourceCard = baseline.Cards.Single(item => item.ReplayCardId == "card-a-instance");
            var derived = MatchReplayActionDerivation.Build(
                "action-derived",
                "CardUse",
                "role",
                "card-a",
                "card-a-instance",
                "Card A",
                sourceCard,
                baseline,
                derivedAfter);
            Assert(derived.DurationMilliseconds == 960
                   && derived.Presentation.Any(item => item.Kind == MatchReplayPresentationCueKinds.CardUse)
                   && derived.Presentation.Any(item => item.Kind == MatchReplayPresentationCueKinds.ActorAction),
                "v8 composes card reveal and actor action inside one deterministic presentation frame");
            Assert(derived.CardTransitions.Any(item => item.ReplayCardId == "card-a-instance"
                                                       && item.FromZone == "Hand"
                                                       && item.ToZone == ""
                                                       && item.Disposition == MatchReplayCardDispositionKinds.Consume)
                   && derived.CardTransitions.Any(item => item.ReplayCardId == "card-c-instance"
                                                          && item.ToZone == "Hand"
                                                          && item.Disposition == MatchReplayCardDispositionKinds.Draw),
                "v8 records explicit hand removal and draw transitions by stable card identity");
            Assert(derived.Semantics.Any(item => item.Category == MatchSemanticCategories.Damage
                                                && item.TargetInstanceId == "role"
                                                && item.Value == 6)
                   && derived.Semantics.Any(item => item.Category == MatchSemanticCategories.Defend
                                                   && item.Action == "DefendGained"
                                                   && item.Value == 4)
                   && derived.Semantics.Any(item => item.Category == MatchSemanticCategories.Buff
                                                   && item.Action == "BuffAdded"
                                                   && item.Label == "buff-focus"
                                                   && item.SecondaryValue == 2)
                   && derived.Semantics.Any(item => item.Category == MatchSemanticCategories.Resource
                                                   && item.Action == "PowerChanged"
                                                   && item.Value == -1),
                "v8 derives exact damage, shield, Buff, and resource outcomes from authoritative state differences");
            var burningSource = MatchReplayProjectionState.Clone(baseline).Cards
                .Single(item => item.ReplayCardId == "card-a-instance");
            burningSource.Vars.Add(new MatchReplayStringValue { Key = "HasBurn", Value = "True" });
            Assert(MatchReplayActionDerivation.BuildCardTransitions(baseline, afterFirst, burningSource)
                    .Any(item => item.ReplayCardId == "card-a-instance"
                                 && item.Disposition == MatchReplayCardDispositionKinds.Burn),
                "v8 preserves an explicit burn disposition for replay-specific card visuals");
            var dynamicCardAfter = MatchReplayProjectionState.Clone(baseline);
            dynamicCardAfter.Cards[0].Vars.Add(new MatchReplayStringValue { Key = "DesVal1", Value = "9" });
            Assert(MatchReplayActionDerivation.BuildCardTransitions(baseline, dynamicCardAfter)
                    .Any(item => item.ReplayCardId == "card-a-instance"
                                 && item.Disposition == MatchReplayCardDispositionKinds.Update),
                "dynamic card text changes are explicit transitions even when hand order is unchanged");
            var shieldBefore = MatchReplayProjectionState.Clone(baseline);
            shieldBefore.Statuses[0].Defend = 5;
            var shieldAfter = MatchReplayProjectionState.Clone(baseline);
            var shieldOutcome = MatchReplayActionDerivation.Build(
                "action-shield",
                "CardUse",
                "role",
                "card-a",
                "card-a-instance",
                "Card A",
                sourceCard,
                shieldBefore,
                shieldAfter);
            Assert(shieldOutcome.Semantics.Any(item => item.Category == MatchSemanticCategories.Damage
                                                       && item.Action == "ShieldDamage"
                                                       && item.Value == 5)
                   && shieldOutcome.Presentation.Any(item => item.Kind == MatchReplayPresentationCueKinds.Damage
                                                            && item.Value == 5),
                "fully defended hits still produce attributed damage and a recorded hit reaction");

            var report = MatchAnalysisBuilder.Build(new MatchRecord { TurnCount = 2 }, first);
            Assert(report.CardUseCount == 2
                   && report.Turns.All(item => item.ActionCount == 1)
                   && report.Cards.Sum(item => item.AttributedDamage) == 11,
                "analysis expands semantics inside each action frame while counting the action once");

            var incomplete = new MatchRecord
            {
                ReplayProtocol = MatchReplayProtocol.Version,
                ReplayState = MatchReplayStates.Incomplete
            };
            Assert(!MatchReplayCompatibility.Evaluate(incomplete).CanPlay,
                "records explicitly marked incomplete remain analysis-only");
            Assert(MatchReplayCaptureQuality.Evaluate(first).CanPlay
                   && !MatchReplayCaptureQuality.Evaluate(first.Where(item => item.Kind != MatchReplayEventKinds.TurnFrame)).CanPlay
                   && !MatchReplayCaptureQuality.Evaluate(first.Where(item => item.Kind != MatchReplayEventKinds.ActionFrame)).CanPlay,
                "capture quality requires both turn baselines and authoritative action frames");
            var eventCounts = first
                .GroupBy(item => item.Kind, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            Assert(MatchReplayCaptureQuality.EvaluateRecording(eventCounts, true, Array.Empty<string>()).CanPlay
                   && !MatchReplayCaptureQuality.EvaluateRecording(eventCounts, false, Array.Empty<string>()).CanPlay
                   && !MatchReplayCaptureQuality.EvaluateRecording(
                       eventCounts,
                       true,
                       new[] { "action projection did not converge" }).CanPlay,
                "recording quality keeps complete captures playable while baseline or convergence failures degrade without deletion");

            var drifted = MatchReplayProjectionState.Clone(afterFirst);
            drifted.PlayerPower--;
            drifted.PlayerMaxPower++;
            drifted.Statuses[0].CurrentHp--;
            drifted.Statuses[0].DynamicVariables[0].Value++;
            var stateDiff = MatchReplayStateComparer.Compare(afterFirst, drifted);
            Assert(!stateDiff.IsMatch
                   && stateDiff.Paths.Contains("player.power")
                   && stateDiff.Paths.Contains("player.maxPower")
                   && stateDiff.Paths.Contains("status[role].hp")
                   && stateDiff.Paths.Contains("status[role].var[CardCost]"),
                "projection diagnostics identify exact logical fields instead of reporting hash-only drift");

            var bootstrap = new MatchReplayRuntimeBootstrap();
            Assert(!bootstrap.Begin(false, true, true, out var activeSessionMessage)
                   && bootstrap.FailureCode == "network-session-active"
                   && activeSessionMessage.Contains("主菜单", StringComparison.Ordinal),
                "the replay view never reuses or mutates an active adventure network session");
            Assert(!bootstrap.Begin(false, false, false, out _)
                   && bootstrap.FailureCode == "lobby-manager-missing",
                "replay preparation reports a missing local view host before changing runtime state");
            Assert(bootstrap.Begin(false, false, true, out _)
                   && bootstrap.Phase == MatchReplayRuntimeBootstrapPhases.WaitingForRuntime,
                "an idle main menu starts asynchronous replay-view preparation");

            var waiting = ReadyReplayRuntime();
            waiting.FightReady = false;
            bootstrap.Advance(1000, waiting);
            Assert(bootstrap.Phase == MatchReplayRuntimeBootstrapPhases.WaitingForRuntime
                   && bootstrap.MissingRuntime.Contains("fight-idle", StringComparison.Ordinal),
                "view bootstrap identifies the exact component still missing");
            waiting.FightReady = true;
            bootstrap.Advance(1, waiting);
            Assert(bootstrap.Phase == MatchReplayRuntimeBootstrapPhases.Ready,
                "the replay view becomes ready only after all native presentation objects exist");

            Assert(bootstrap.Begin(false, false, true, out _),
                "bootstrap can inspect a later map-context preparation attempt");
            var missingContext = ReadyReplayRuntime();
            missingContext.MapContextReady = false;
            missingContext.DiceReady = false;
            bootstrap.Advance(1000, missingContext);
            Assert(missingContext.DescribeMissing().Contains("mode-context", StringComparison.Ordinal)
                   && missingContext.DescribeMissing().Contains("dice-state", StringComparison.Ordinal)
                   && missingContext.DescribeState().Contains("mapNetwork=True", StringComparison.Ordinal),
                "bootstrap keeps map instance, view context, and dice diagnostics separate");

            Assert(bootstrap.Begin(false, false, true, out _),
                "bootstrap can validate native view-only random-pool readiness");
            var missingRandomPool = ReadyReplayRuntime();
            missingRandomPool.RandomPoolReady = false;
            bootstrap.Advance(1000, missingRandomPool);
            Assert(bootstrap.MissingRuntime.Contains("random-pool", StringComparison.Ordinal),
                "bootstrap blocks native view construction before an empty Dice pool can enter FightCardManager.Init");

            Assert(bootstrap.Begin(false, false, true, out _),
                "bootstrap can reset for a later replay view");
            bootstrap.Advance(MatchReplayRuntimeBootstrap.TimeoutMilliseconds, new MatchReplayRuntimeReadiness());
            Assert(bootstrap.Phase == MatchReplayRuntimeBootstrapPhases.Failed
                   && bootstrap.FailureCode == "runtime-timeout"
                   && bootstrap.FailureMessage.Contains("server", StringComparison.Ordinal),
                "bootstrap timeout produces an actionable component-level failure");

            var presentationState = new MatchReplayCardState
            {
                CardId = "card-current",
                ReplayCardId = "card-instance-current",
                DataType = 7,
                Data = new List<MatchReplayStringValue>
                {
                    new() { Key = "Id", Value = "card-old" },
                    new() { Key = "Name", Value = "first-name" },
                    new() { Key = "Name", Value = "latest-name" },
                    new() { Key = "Expend", Value = "9" }
                },
                Vars = new List<MatchReplayStringValue>
                {
                    new() { Key = "InstanceID", Value = "instance-old" },
                    new() { Key = "DesVal1", Value = "5" }
                }
            };
            var composed = MatchReplayCardPresentationData.Compose(presentationState, 2);
            Assert(composed.DataType == 7
                   && composed.Data["Id"] == "card-current"
                   && composed.Data["Name"] == "latest-name"
                   && composed.Data["Expend"] == "2"
                   && composed.Vars["InstanceID"] == "card-instance-current"
                   && composed.Vars["Expend"] == "2",
                "card presentation data is fully composed before the native read-only DataConfig wrapper is constructed");
            Assert(presentationState.Data.Single(item => item.Key == "Expend").Value == "9"
                   && presentationState.Vars.All(item => item.Key != "Expend"),
                "card presentation composition never mutates the recorded replay state");

            var notification = new MatchReplayFailureNotificationState();
            var staleNotification = notification.Schedule();
            var currentNotification = notification.Schedule();
            Assert(!notification.TryPresent(staleNotification)
                   && notification.TryPresent(currentNotification)
                   && notification.IsVisible,
                "a newer replay failure replaces a stale deferred notification");
            notification.Dismiss();
            Assert(!notification.IsVisible && !notification.TryPresent(currentNotification),
                "dismissing a replay failure invalidates callbacks still waiting behind the transition guard");

            var panelBuild = new AuraToolsPanelBuildState();
            var abandonedBuild = panelBuild.Begin();
            panelBuild.CancelBuild();
            var reopenedBuild = panelBuild.Begin();
            panelBuild.Complete(abandonedBuild, true);
            Assert(panelBuild.IsBuilding && !panelBuild.IsBuilt && panelBuild.IsCurrent(reopenedBuild),
                "closing settings invalidates its abandoned build without allowing stale completion to win");
            panelBuild.Complete(reopenedBuild, true);
            Assert(panelBuild.IsBuilt && !panelBuild.IsBuilding,
                "reopening settings can complete a fresh build after the old panel coroutine was cancelled");
            panelBuild.Adopt(true);
            Assert(panelBuild.IsBuilt && panelBuild.Begin() != 0,
                "an adopted native panel retains built content and can still start an explicit later rebuild");

            var bootstrapPoolA = MatchReplayBootstrapRandomPool.Create("record-a", 128);
            var bootstrapPoolB = MatchReplayBootstrapRandomPool.Create("record-a", 128);
            var bootstrapPoolC = MatchReplayBootstrapRandomPool.Create("record-b", 128);
            Assert(bootstrapPoolA.SequenceEqual(bootstrapPoolB)
                   && !bootstrapPoolA.SequenceEqual(bootstrapPoolC)
                   && bootstrapPoolA.All(value => value >= 0f && value < 1f),
                "native replay-view construction receives a bounded deterministic random pool without global Random calls");

            Assert(!MatchReplayViewBootstrapContract.UsesNativeFightInitializer
                   && !MatchReplayViewBootstrapContract.RunsCareerOrRelicScripts
                   && !MatchReplayViewBootstrapContract.RunsEnemyInitScripts
                   && !MatchReplayViewBootstrapContract.StartsTurnRuntime
                   && MatchReplayViewBootstrapContract.Describe().Contains("passive-native-view-v1", StringComparison.Ordinal),
                "replay view bootstrap explicitly excludes native fight, gameplay-script, and turn execution paths");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(root + "-deferred")) Directory.Delete(root + "-deferred", recursive: true);
        }
    }

    private static MatchReplayStateSnapshot State(int turn, int hp, int power, IEnumerable<string> handCardIds)
    {
        return new MatchReplayStateSnapshot
        {
            LevelId = "level",
            TurnIndex = turn,
            EnemyPositive = 1f,
            EnemyHp = 1f,
            PlayerPower = power,
            PlayerMaxPower = 3,
            CardTopCount = 10,
            Statuses = new List<MatchReplayStatusState>
            {
                new()
                {
                    InstanceId = "role",
                    MaxHp = 20,
                    CurrentHp = hp,
                    State = "Alive",
                    DynamicVariables = new List<MatchReplayFloatValue>
                    {
                        new() { Key = "CardCost", Value = power }
                    }
                }
            },
            Cards = handCardIds.Select((id, index) => new MatchReplayCardState
            {
                Zone = "Hand",
                Order = index,
                ReplayCardId = id + "-instance",
                CardId = id,
                Data = new List<MatchReplayStringValue>
                {
                    new() { Key = "Id", Value = id },
                    new() { Key = "Name", Value = id }
                }
            }).ToList()
        };
    }

    private static MatchReplayEvent TurnFrameEvent(long sequence, MatchReplayStateSnapshot state)
    {
        var copy = MatchReplayProjectionState.Clone(state);
        return new MatchReplayEvent
        {
            Sequence = sequence,
            TurnIndex = state.TurnIndex,
            ElapsedMilliseconds = sequence * 1000,
            Kind = MatchReplayEventKinds.TurnFrame,
            TurnFrame = new MatchReplayTurnFrame
            {
                TurnIndex = state.TurnIndex,
                ActiveActorId = "role",
                State = copy,
                StateHash = MatchReplayProjectionState.Hash(copy)
            }
        };
    }

    private static MatchReplayEvent CheckpointEvent(
        long sequence,
        MatchReplayStateSnapshot state,
        int completedActions)
    {
        var copy = MatchReplayProjectionState.Clone(state);
        return new MatchReplayEvent
        {
            Sequence = sequence,
            TurnIndex = state.TurnIndex,
            Kind = MatchReplayEventKinds.SeekCheckpoint,
            SeekCheckpoint = new MatchReplaySeekCheckpoint
            {
                TurnIndex = state.TurnIndex,
                CompletedActionCount = completedActions,
                State = copy,
                StateHash = MatchReplayProjectionState.Hash(copy)
            }
        };
    }

    private static MatchReplayEvent ActionFrameEvent(
        long sequence,
        int turn,
        string actionId,
        int actionIndex,
        MatchReplayStateSnapshot before,
        MatchReplayStateSnapshot after,
        string cardId,
        long damage)
    {
        var damageSemantic = new MatchSemanticEvent
        {
            EventId = actionId + ":damage",
            ActionId = actionId,
            RootActionId = actionId,
            Category = MatchSemanticCategories.Damage,
            ActorId = "role",
            TargetId = "enemy",
            SourceInstanceId = "role",
            TargetInstanceId = "enemy",
            Value = damage,
            AttributionConfidence = MatchAttributionConfidence.Exact
        };
        var primary = new MatchSemanticEvent
        {
            EventId = actionId + ":card",
            ActionId = actionId,
            RootActionId = actionId,
            Category = MatchSemanticCategories.Card,
            ActorId = "role",
            SourceId = cardId,
            Label = cardId,
            AttributionConfidence = MatchAttributionConfidence.Exact
        };
        return new MatchReplayEvent
        {
            Sequence = sequence,
            TurnIndex = turn,
            ElapsedMilliseconds = sequence * 1000,
            Kind = MatchReplayEventKinds.ActionFrame,
            Semantic = primary,
            ActionFrame = new MatchReplayActionFrame
            {
                ActionId = actionId,
                ActionIndex = actionIndex,
                TurnIndex = turn,
                DurationMilliseconds = 960,
                Kind = "CardUse",
                ActorId = "role",
                SourceId = cardId,
                Label = cardId,
                Delta = MatchReplayProjectionState.CreateDelta(before, after),
                CardTransitions = MatchReplayActionDerivation.BuildCardTransitions(before, after),
                Semantics = new List<MatchSemanticEvent> { damageSemantic },
                Presentation = new List<MatchReplayPresentationCue>
                {
                    new()
                    {
                        CueId = actionId + ":actor",
                        Kind = MatchReplayPresentationCueKinds.ActorAction,
                        DurationMilliseconds = 880,
                        ActorId = "role"
                    },
                    new()
                    {
                        CueId = actionId + ":damage",
                        Kind = MatchReplayPresentationCueKinds.Damage,
                        StartOffsetMilliseconds = 90,
                        DurationMilliseconds = 280,
                        TargetIds = new List<string> { "enemy" },
                        Value = damage
                    }
                },
                FinalStateHash = MatchReplayProjectionState.Hash(after)
            }
        };
    }

    private static MatchReplayRuntimeReadiness ReadyReplayRuntime()
    {
        return new MatchReplayRuntimeReadiness
        {
            ServerActive = true,
            ClientActive = true,
            ClientConnected = true,
            ServerConnectionReady = true,
            GameServerReady = true,
            PlayerReady = true,
            MapInstanceReady = true,
            MapNetworkReady = true,
            MapContextReady = true,
            DiceReady = true,
            RandomPoolReady = true,
            FightReady = true,
            RoleTableReady = true,
            UiReady = true,
            GameAppReady = true
        };
    }
}
