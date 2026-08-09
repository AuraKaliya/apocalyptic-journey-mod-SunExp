using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using AuraFoundationTrainer.ControlCenter;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

internal static class CombatAiFoundationTrainingBehaviorTests
{
    public static void Run(CombatAiTrainingTestContext context)
    {
        var simulationEngine = context.Simulation.Engine;
        var simulationRules = context.Simulation.Rules;
        var bundledRulesV2 = context.Simulation.BundledRules;
        var episodes = context.Episodes;
        var policyValueTraining = context.PolicyValueTraining;
        var policyValueModel = context.PolicyValueModel;
        var reusableState = context.ReusableState;
        var reusableCandidates = context.ReusableCandidates;
        var campaign = context.Campaign;
        var campaignRules = context.CampaignRules;
        if (!campaignRules.Ruleset.TryGetCardCore("strike", out var projectedStrike))
        {
            throw new InvalidOperationException("Foundation fixture starter attack is missing.");
        }
        var projectedTrainingCampaign = BuildStandardCampaign();
        projectedTrainingCampaign.RequireAuthoritativeRules = true;
        var projectedValidationCampaign = BuildStandardCampaign();
        projectedValidationCampaign.RequireAuthoritativeRules = true;
        foreach (var difficulty in projectedTrainingCampaign.Difficulties
                     .Concat(projectedValidationCampaign.Difficulties))
        {
            difficulty.ApplyGameLevelShield = false;
        }
        const ulong signedTransformerRunSeed = 13786276755866915639UL;
        const int expectedSignedTransformerSeed = -1465117897;
        var foundationSeedPlanA = CombatFoundationSeedPlan.Create(123456789UL, 2_000_000UL);
        var foundationSeedPlanARepeat = CombatFoundationSeedPlan.Create(
            123456789UL,
            2_000_000UL);
        var foundationSeedPlanB = CombatFoundationSeedPlan.Create(987654321UL, 2_000_000UL);
        var signedTransformerSeedPlan = CombatFoundationSeedPlan.Create(
            signedTransformerRunSeed,
            2_000_000UL);
        var signedSeedWorkerJob = CombatFoundationWorkerJobFactory.Create(
            new CombatFoundationWorkerJobBuildRequest
            {
                JobId = "signed-seed-contract",
                ResultDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "aura-foundation-signed-seed-contract"),
                Parameters = new CombatFoundationTrainingParameters
                {
                    RunSeed = signedTransformerRunSeed
                },
                Profile = new CombatDecisionProfile(),
                TrainingCampaign = new CombatCampaignDefinition(),
                ValidationCampaign = new CombatCampaignDefinition(),
                Ruleset = new CombatRulesetDocument()
            });
        Assert(foundationSeedPlanA.TrainingSeedStart
               == foundationSeedPlanARepeat.TrainingSeedStart
               && foundationSeedPlanA.ArenaSeedStart
               == foundationSeedPlanARepeat.ArenaSeedStart
               && foundationSeedPlanA.TuningSeedStart
               == foundationSeedPlanARepeat.TuningSeedStart
               && foundationSeedPlanA.ModelRandomSeed
               == foundationSeedPlanARepeat.ModelRandomSeed
               && foundationSeedPlanA.TrainingSeedStart
               != foundationSeedPlanB.TrainingSeedStart
               && foundationSeedPlanA.ArenaSeedStart
               != foundationSeedPlanB.ArenaSeedStart
               && foundationSeedPlanA.TuningSeedStart
               != foundationSeedPlanB.TuningSeedStart
               && foundationSeedPlanA.ModelRandomSeed
               != foundationSeedPlanB.ModelRandomSeed
               && foundationSeedPlanA.ValidationSeedStart == 2_000_000UL
               && foundationSeedPlanB.ValidationSeedStart == 2_000_000UL
               && signedTransformerSeedPlan.ModelRandomSeed >= 0
               && unchecked((int)signedTransformerRunSeed)
                  == expectedSignedTransformerSeed
               && signedSeedWorkerJob.Request.RunSeed
                  == signedTransformerRunSeed
               && signedSeedWorkerJob.Request.TransformerTeacher.RandomSeed
                  == expectedSignedTransformerSeed
               && signedSeedWorkerJob.Request.MaximumIterationsPerProcess == 3,
            "foundation RunSeed deterministically separates self-play, arena, and model randomness while retaining canonical validation seeds, the signed Transformer seed contract and three-round process batching");
        var cleanFeatureVector = CombatPolicyValueEncoding.EncodeState(
            new Dictionary<string, double>
            {
                ["playerHp"] = 20d,
                ["enemyHpTotal"] = 15d
            },
            32);
        var contaminatedFeatureVector = CombatPolicyValueEncoding.EncodeState(
            new Dictionary<string, double>
            {
                ["playerHp"] = 20d,
                ["enemyHpTotal"] = 15d,
                ["finalBossVictory"] = 1d,
                ["journeyProgress"] = 1d,
                ["target:value"] = 1d,
                ["future.outcome"] = 1d
            },
            32);
        var legacyFeatureModel = new CombatPolicyValueNetworkDefinition
        {
            FeatureSchemaVersion = 6
        };
        Assert(cleanFeatureVector.SequenceEqual(contaminatedFeatureVector)
               && !CombatPolicyValueNetworkValidator.TryValidate(
                   legacyFeatureModel,
                   out _),
            "policy-value feature contract rejects legacy schema and makes post-hoc labels unable to change the encoded observation");
        var curriculumOpening = CombatFoundationCurriculum.BuildDifficulties(
            8,
            0,
            4,
            123456789UL,
            enabled: true);
        var curriculumFinal = CombatFoundationCurriculum.BuildDifficulties(
            8,
            3,
            4,
            123456789UL,
            enabled: true,
            priorNormalWinRate: 1d,
            priorNormalTrials: 200,
            priorAdvancedWinRate: 0.8d,
            priorAdvancedTrials: 100);
        var advancedFloorPlan = CombatFoundationCurriculum.Evaluate(
            true,
            iteration: 0,
            normalWins: 0,
            normalTrials: 0,
            advancedWins: 0,
            advancedTrials: 0);
        CombatCampaignFoundationTrainer.ApplyAdvancedTrainingFloor(
            advancedFloorPlan,
            0.35d);
        var advancedRecoveryPlan = CombatFoundationCurriculum.Evaluate(
            true,
            iteration: 3,
            normalWins: 200,
            normalTrials: 200,
            advancedWins: 0,
            advancedTrials: 64);
        var dualDeficitPlan = CombatFoundationCurriculum.Evaluate(
            true,
            iteration: 3,
            normalWins: 8,
            normalTrials: 32,
            advancedWins: 2,
            advancedTrials: 32);
        Assert(curriculumOpening.Count(item => item == "advanced") == 0
               && Math.Abs(advancedFloorPlan.AdvancedShare - 0.35d) < 0.000001d
               && Math.Abs(advancedFloorPlan.MinimumAdvancedShare - 0.35d)
                  < 0.000001d
               && curriculumFinal.Count(item => item == "advanced") == 2
               && CombatFoundationCurriculum.BuildDifficulties(
                       20,
                       3,
                       4,
                       123456789UL,
                       enabled: true,
                       priorNormalWinRate: 0d,
                       priorNormalTrials: 32)
                    .Count(item => item == "advanced") == 9
               && CombatFoundationCurriculum.BuildDifficulties(
                       20,
                       1,
                       4,
                       123456789UL,
                       enabled: true)
                   .Count(item => item == "advanced") == 5
               && advancedRecoveryPlan.Stage == "advanced-recovery"
               && Math.Abs(advancedRecoveryPlan.AdvancedShare - 0.50d)
                  < 0.000001d
               && Math.Abs(CombatFoundationCurriculum.ExplorationProbability(
                               advancedRecoveryPlan,
                               0.12d) - 0.20d) < 0.000001d
               && dualDeficitPlan.Stage == "dual-deficit-recovery"
               && Math.Abs(dualDeficitPlan.AdvancedShare - 0.45d) < 0.000001d
               && Math.Abs(CombatFoundationCurriculum.ExplorationProbability(
                               dualDeficitPlan,
                               0.12d) - 0.20d) < 0.000001d
               && curriculumOpening.SequenceEqual(
                   CombatFoundationCurriculum.BuildDifficulties(
                       8,
                       0,
                       4,
                       123456789UL,
                       enabled: true)),
            "foundation curriculum starts on normal and raises both Advanced coverage and exploration when the Advanced gate regresses");
        CombatCandidateEvaluation BudgetCandidate(
            string id,
            CombatActionSemantics? semantics = null)
        {
            return new CombatCandidateEvaluation
            {
                Legal = true,
                Action = new CombatActionObservation
                {
                    CandidateId = id,
                    Semantics = semantics ?? new CombatActionSemantics()
                }
            };
        }

        var budgetState = new CombatStateObservation
        {
            Player = new CombatUnitObservation { CurrentHp = 80, MaxHp = 100 },
            Enemies =
            {
                new CombatUnitObservation
                {
                    DefinitionId = "ordinary-enemy",
                    CurrentHp = 100,
                    MaxHp = 100
                }
            }
        };
        var budgetProfile = new CombatDecisionProfile
        {
            SearchBudgetMode = "dynamic",
            SearchQuality = "balanced",
            SearchBudgetContext = "deployment",
            SearchTimeBudgetMilliseconds = 125
        };
        var forcedBudget = CombatSearchBudgetPolicy.Resolve(
            budgetState,
            new[] { BudgetCandidate("only") },
            budgetProfile);
        var simpleBudget = CombatSearchBudgetPolicy.Resolve(
            budgetState,
            new[] { BudgetCandidate("a"), BudgetCandidate("b") },
            budgetProfile);
        var normalBudget = CombatSearchBudgetPolicy.Resolve(
            budgetState,
            Enumerable.Range(0, 5)
                .Select(index => BudgetCandidate("normal-" + index))
                .ToList(),
            budgetProfile);
        var bossState = new CombatStateObservation
        {
            Player = new CombatUnitObservation { CurrentHp = 80, MaxHp = 100 },
            Enemies =
            {
                new CombatUnitObservation
                {
                    DefinitionId = "final-boss",
                    CurrentHp = 500,
                    MaxHp = 500
                }
            }
        };
        var difficultBudget = CombatSearchBudgetPolicy.Resolve(
            bossState,
            Enumerable.Range(0, 5)
                .Select(index => BudgetCandidate("boss-" + index))
                .ToList(),
            budgetProfile);
        var fakeLoopBudget = CombatSearchBudgetPolicy.Resolve(
            budgetState,
            new[]
            {
                BudgetCandidate(
                    "fake-loop",
                    new CombatActionSemantics
                    {
                        Draw = 1,
                        EnergyGain = 1,
                        CardGeneration = 1,
                        EndOfCycleSelfHpLoss = 1
                    }),
                BudgetCandidate("escape")
            },
            budgetProfile);
        var resourceCycleBudget = CombatSearchBudgetPolicy.Resolve(
            budgetState,
            new[]
            {
                BudgetCandidate(
                    "resource-cycle",
                    new CombatActionSemantics
                    {
                        Draw = 1,
                        EnergyGain = 1,
                        CardGeneration = 1
                    }),
                BudgetCandidate("ordinary-1"),
                BudgetCandidate("ordinary-2"),
                BudgetCandidate("ordinary-3"),
                BudgetCandidate("ordinary-4")
            },
            budgetProfile);
        Assert(forcedBudget.Tier == "forced"
               && forcedBudget.SimulationBudget == 1
               && simpleBudget.Tier == "simple"
               && simpleBudget.SimulationBudget == 96
               && normalBudget.Tier == "normal"
               && normalBudget.SimulationBudget == 128
               && normalBudget.TimeBudgetMilliseconds == 125
               && simpleBudget.MinimumTimeMilliseconds == 32
               && normalBudget.MinimumTimeMilliseconds == 44
               && difficultBudget.Tier == "difficult"
               && difficultBudget.SimulationBudget == 192
               && difficultBudget.MinimumTimeMilliseconds == 63
               && fakeLoopBudget.Tier == "complex"
               && fakeLoopBudget.SimulationBudget == 256
               && fakeLoopBudget.MinimumTimeMilliseconds == 75
               && fakeLoopBudget.MinimumRootVisits == 4
               && resourceCycleBudget.Tier == "normal"
               && fakeLoopBudget.MaxPly == 16,
            "deployment search caps latency, reserves bounded deep budgets for true loop risk, and does not overclassify ordinary resource generation");
        var partitionedStatus = CombatPolicyValueEncoding.EncodeState(
            new Dictionary<string, double> { ["playerStatus:test"] = 1d },
            2048,
            "partitioned-v4");
        var partitionedDeck = CombatPolicyValueEncoding.EncodeState(
            new Dictionary<string, double> { ["deck:test"] = 1d },
            2048,
            "partitioned-v4");
        Assert(partitionedStatus
                   .Select((value, index) => new { value, index })
                   .Where(item => Math.Abs(item.value) > 0.0000001d)
                   .All(item => item.index >= 960 && item.index < 1216)
               && partitionedDeck
                   .Select((value, index) => new { value, index })
                   .Where(item => Math.Abs(item.value) > 0.0000001d)
                   .All(item => item.index >= 1216 && item.index < 1408),
            "partitioned state encoding keeps status and deck identities in disjoint feature ranges");
        var coreStateEncoding = CombatPolicyValueEncoding.EncodeState(
            new Dictionary<string, double>
            {
                ["playerHp"] = 10d,
                ["playerMaxHp"] = 20d
            },
            2048,
            "partitioned-v4");
        var coreActionEncoding = CombatPolicyValueEncoding.EncodeCandidate(
            new CombatPolicyValueCandidate
            {
                SourceId = "test",
                Features = new Dictionary<string, double>
                {
                    ["cost"] = 1d,
                    ["risk"] = 2d
                }
            },
            96,
            "partitioned-v4");
        Assert(coreStateEncoding[0] != 0d
               && coreStateEncoding[1] != 0d
               && coreActionEncoding[0] != 0d
               && coreActionEncoding[21] != 0d
               && policyValueTraining.Model!.Metrics.ContainsKey(
                   "stateFeatureCollisionRate")
               && policyValueTraining.Model.Metrics.ContainsKey(
                   "actionFeatureCollisionRate"),
            "partitioned-v4 reserves fixed core slots and reports sparse collision telemetry");
        var replayFixture = Enumerable.Range(0, 8)
            .Select(index => new CombatEpisode
            {
                EpisodeId = "replay-" + index,
                JourneyRunId = "fixture:"
                               + (index < 6 ? "normal" : "advanced")
                               + ":"
                               + index,
                JourneyBattleIndex = index == 7 ? 36 : index,
                Seed = (ulong)index,
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    DifficultyId = index < 6 ? "normal" : "advanced",
                    FinalBossVictory = index == 7,
                    CampaignCompletedBattles = index == 7 ? 37 : index + 1,
                    CampaignTotalBattles = 37,
                    OutcomeClass = index == 7 ? "victory" : "defeat"
                },
                Frames =
                {
                    new CombatEpisodeFrame()
                }
            })
            .ToList();
        var replaySelectionFixture = CombatFoundationReplaySampler.Select(
            replayFixture,
            8,
            enabled: true);
        Assert(replaySelectionFixture.Episodes.Count == 7
               && replaySelectionFixture.NormalEpisodes == 5
               && replaySelectionFixture.AdvancedEpisodes == 2
               && replaySelectionFixture.AdvancedDefeatEpisodes == 1
               && Math.Abs(
                   replaySelectionFixture.TargetAdvancedDefeatShare - 0.25d)
                  < 0.0001d
               && replaySelectionFixture.SuccessfulEpisodes > 0
               && replaySelectionFixture.QuotaShortfalls.TryGetValue(
                   "advanced:defeat",
                   out var advancedDefeatShortfall)
               && advancedDefeatShortfall == 1,
            "foundation replay stratification preserves the advanced quota, reports scarcity, and never silently backfills it with normal episodes");
        var resourceBoundReplayA = CombatFoundationReplaySampler.Select(
            replayFixture,
            8,
            enabled: true);
        var resourceBoundReplayB = CombatFoundationReplaySampler.Select(
            replayFixture,
            8,
            enabled: true);
        CombatFoundationReplaySampler.ApplyResourceBudget(
            resourceBoundReplayA,
            replayFixture.Where(item => item.EpisodeId == "replay-7"),
            minimumEpisodes: 2,
            frameLimit: 3,
            estimatedBytesLimit: 64L * 1024L * 1024L);
        CombatFoundationReplaySampler.ApplyResourceBudget(
            resourceBoundReplayB,
            replayFixture.Where(item => item.EpisodeId == "replay-7"),
            minimumEpisodes: 2,
            frameLimit: 3,
            estimatedBytesLimit: 64L * 1024L * 1024L);
        Assert(resourceBoundReplayA.Episodes.Count == 3
               && resourceBoundReplayA.SelectedFrames == 3
               && resourceBoundReplayA.ResourceBudgetDroppedEpisodes == 4
               && resourceBoundReplayA.Episodes.Any(item =>
                   item.EpisodeId == "replay-7")
               && resourceBoundReplayA.Episodes.Select(item => item.EpisodeId)
                   .SequenceEqual(resourceBoundReplayB.Episodes.Select(item =>
                       item.EpisodeId)),
            "replay resource budgets deterministically retain required content while bounding total frames");
        foreach (var episode in replayFixture.Take(4))
        {
            episode.Campaign.TrainingIteration = 2;
        }
        var currentIterationSelection = CombatFoundationReplaySampler.Select(
            replayFixture,
            8,
            enabled: true);
        CombatFoundationReplaySampler.PinCurrentIterationEpisodes(
            currentIterationSelection,
            replayFixture.Where(episode =>
                episode.Campaign.TrainingIteration == 2),
            episodeLimit: 8,
            requestedShare: 0.60d);
        Assert(currentIterationSelection.PinnedCurrentIterationEpisodes == 4
               && currentIterationSelection.Episodes.Count(episode =>
                   episode.Campaign.TrainingIteration == 2) == 4,
            "the bounded replay hot window pins all available current-round episodes before historical backfill");
        var failedAdvancedJourneyStratum =
            CombatPolicyValueBatchTrainer.FrameStratum(
                new CombatEpisode
                {
                    JourneyBattleIndex = 12,
                    Campaign = new CombatCampaignEpisodeMetadata
                    {
                        DifficultyId = "advanced",
                        FinalBossVictory = false,
                        OutcomeClass = "battle-victory"
                    }
                },
                critical: true);
        var successfulHardEncounterStratum =
            CombatPolicyValueBatchTrainer.FrameStratum(
                new CombatEpisode
                {
                    JourneyBattleIndex = 3,
                    Campaign = new CombatCampaignEpisodeMetadata
                    {
                        DifficultyId = "advanced",
                        FinalBossVictory = false,
                        OutcomeClass = "encounter-victory"
                    }
                },
                critical: false);
        Assert(failedAdvancedJourneyStratum
                   == "advanced:middle:defeat:critical"
               && successfulHardEncounterStratum
                  == "advanced:opening:victory:regular",
            "frame stratification labels every stage of a failed journey as defeat while preserving local hard-encounter victories");
        Assert(CombatPolicyValueBatchTrainer.StrategicFrameStratum(
                   new Dictionary<string, double>
                   {
                       ["roleStrategy:nana.safe-growth-window"] = 1d
                   }) == "strategy-growth"
               && CombatPolicyValueBatchTrainer.StrategicFrameStratum(
                   new Dictionary<string, double>
                   {
                       ["roleStrategy:any.survival-override"] = 1d,
                       ["roleStrategy:any.transform-ready"] = 1d
                   }) == "strategy-survival"
               && CombatPolicyValueBatchTrainer.StrategicFrameStratum(
                   new Dictionary<string, double>
                   {
                       ["roleStrategy:nana.growth-target-doom"] = 12d,
                       ["roleStrategy:nana.bank-for-next-turn"] = 1d,
                       [CombatRoleStrategyFeatureNames.MinimumTrainingShare(
                           "growth")] = 0.15d
                   }) == "strategy-bank"
               && CombatPolicyValueBatchTrainer.StrategicFrameStratum(null)
                   == "strategy-baseline",
            "strategic frame strata use explicit role intents without treating numeric targets or quota declarations as active strategy phases");
        var bankOpportunityWithoutIntent = new CombatEpisodeFrame
        {
            ExecutedCandidateId = "play",
            StateFeatures = new Dictionary<string, double>
            {
                ["roleStrategy:nana.bank-for-next-turn"] = 1d
            },
            Candidates = new List<CombatEpisodeCandidate>
            {
                new()
                {
                    CandidateId = "play",
                    Features = new Dictionary<string, double>
                    {
                        [CombatRoleStrategyFeatureNames.Phase] = 2d
                    }
                }
            }
        };
        var selectedBankIntent = new CombatEpisodeFrame
        {
            ExecutedCandidateId = "end-turn",
            StateFeatures = bankOpportunityWithoutIntent.StateFeatures,
            Candidates = new List<CombatEpisodeCandidate>
            {
                new()
                {
                    CandidateId = "end-turn",
                    Features = new Dictionary<string, double>
                    {
                        ["roleStrategy:nana.intent-bank"] = 1d
                    }
                }
            }
        };
        Assert(CombatPolicyValueBatchTrainer.StrategicFrameStratumForFrame(
                   bankOpportunityWithoutIntent) == "strategy-baseline"
               && CombatPolicyValueBatchTrainer.StrategicFrameStratumForFrame(
                   selectedBankIntent) == "strategy-bank",
            "strategy quota counts selected role actions rather than opportunities the teacher ignored");
        var quotaReplay = Enumerable.Range(0, 100)
            .Select(index =>
            {
                var strategyFeature = index < 60
                    ? "roleStrategy:test.transformed"
                    : index < 75
                        ? "roleStrategy:test.safe-growth-window"
                        : index < 90
                            ? "roleStrategy:test.survival-override"
                            : index < 95
                                ? "roleStrategy:test.finale-safe"
                                : "roleStrategy:test.bank-for-next-turn";
                return new CombatEpisode
                {
                    EpisodeId = "quota-" + index,
                    JourneyRunId = "quota:" + index,
                    JourneyBattleIndex = index,
                    Authoritative = true,
                    DecisionProfile = "balanced",
                    Campaign = new CombatCampaignEpisodeMetadata
                    {
                        DifficultyId = index % 2 == 0 ? "normal" : "advanced",
                        OutcomeClass = index % 3 == 0 ? "victory" : "defeat"
                    },
                    Frames =
                    {
                        new CombatEpisodeFrame
                        {
                            Turn = 1,
                            ActionSequence = index,
                            StateFingerprint = "quota-frame-" + index,
                            ExecutedCandidateId = "play",
                            StateFeatures =
                            {
                                [strategyFeature] = 1d,
                                [CombatRoleStrategyFeatureNames.MaximumTrainingShare(
                                    "transform")] = 0.50d,
                                [CombatRoleStrategyFeatureNames.MinimumTrainingShare(
                                    "growth")] = 0.15d,
                                [CombatRoleStrategyFeatureNames.MinimumTrainingShare(
                                    "survival")] = 0.15d,
                                [CombatRoleStrategyFeatureNames.MinimumTrainingShare(
                                    "finale")] = 0.05d,
                                [CombatRoleStrategyFeatureNames.MinimumTrainingShare(
                                    "bank")] = 0.05d
                            },
                            Candidates =
                            {
                                new CombatEpisodeCandidate
                                {
                                    CandidateId = "play",
                                    SourceId = "card:test",
                                    Legal = true,
                                    SearchVisits = 8
                                },
                                new CombatEpisodeCandidate
                                {
                                    CandidateId = "end",
                                    SourceId = "simulation:end-turn",
                                    Legal = true,
                                    SearchVisits = 2
                                }
                            }
                        }
                    }
                };
            })
            .ToList();
        var quotaWindow = CombatTrainingReplayWindowSelector.Select(
            quotaReplay,
            new CombatTrainingReplayWindowOptions
            {
                MaximumFrames = 100,
                MaximumUnsafeEndTurnShare = 0.30d
            });
        Assert(quotaWindow.StrategyQuotaActive
               && quotaWindow.StrategyQuotaPassed
               && quotaWindow.SelectedFrames == 80
               && quotaWindow.StrategyFrames["strategy-transform"] == 40
               && quotaWindow.StrategyFrames["strategy-growth"] == 15
               && quotaWindow.StrategyFrames["strategy-survival"] == 15
               && quotaWindow.StrategyFrames["strategy-finale"] == 5
               && quotaWindow.StrategyFrames["strategy-bank"] == 5,
            "teacher and student share a deterministic bounded replay window that enforces provider-declared strategy quotas");
        var scarceStrategyEpisode = new CombatEpisode
        {
            EpisodeId = "scarce-strategy-cap",
            JourneyRunId = "scarce-strategy-cap",
            Authoritative = true,
            Campaign = new CombatCampaignEpisodeMetadata
            {
                DifficultyId = "advanced"
            },
            Frames = Enumerable.Range(0, 40)
                .Select(index => new CombatEpisodeFrame
                {
                    Turn = index + 1,
                    ActionSequence = index,
                    StateFingerprint = "scarce-" + index,
                    ExecutedCandidateId = "play",
                    StateFeatures = index == 5
                        ? new Dictionary<string, double>
                        {
                            ["roleStrategy:test.bank-for-next-turn"] = 1d
                        }
                        : index == 12
                            ? new Dictionary<string, double>
                            {
                                ["roleStrategy:test.finale-safe"] = 1d
                            }
                            : new Dictionary<string, double>(),
                    Candidates = new List<CombatEpisodeCandidate>
                    {
                        new()
                        {
                            CandidateId = "play",
                            SourceId = "card:test",
                            Legal = true,
                            SearchVisits = 1
                        }
                    }
                })
                .ToList()
        };
        var scarceStrategyWindow = CombatTrainingReplayWindowSelector.Select(
            new[] { scarceStrategyEpisode },
            new CombatTrainingReplayWindowOptions
            {
                MaximumFrames = 64,
                MaximumFramesPerEpisode = 8
            });
        Assert(scarceStrategyWindow.AvailableSourceFrames == 40
               && scarceStrategyWindow.SourceFrames == 8
               && scarceStrategyWindow.AvailableStrategyFrames["strategy-bank"] == 1
               && scarceStrategyWindow.AvailableStrategyFrames["strategy-finale"] == 1
               && scarceStrategyWindow.SourceStrategyFrames["strategy-bank"] == 1
               && scarceStrategyWindow.SourceStrategyFrames["strategy-finale"] == 1
               && scarceStrategyWindow.StrategyFrames["strategy-bank"] == 1
               && scarceStrategyWindow.StrategyFrames["strategy-finale"] == 1,
            "per-episode caps pin scarce bank/finale frames and report available, capped and selected strategy supply separately");
        var agreementFrame = new CombatEpisodeFrame
        {
            ExecutedCandidateId = "best",
            RemainingTurnsTarget = 8d,
            Candidates =
            {
                new CombatEpisodeCandidate
                {
                    CandidateId = "best",
                    Legal = true,
                    SearchVisits = 20,
                    SearchDeathRisk = 0.10d
                },
                new CombatEpisodeCandidate
                {
                    CandidateId = "other",
                    Legal = true,
                    SearchVisits = 1,
                    SearchDeathRisk = 0.12d
                }
            }
        };
        var disagreementFrame = new CombatEpisodeFrame
        {
            ExecutedCandidateId = "other",
            DeathTarget = 1d,
            RemainingTurnsTarget = 1d,
            StateFeatures = { ["uncertainty"] = 0.8d },
            Candidates =
            {
                new CombatEpisodeCandidate
                {
                    CandidateId = "best",
                    Legal = true,
                    SearchVisits = 12,
                    SearchDeathRisk = 0.05d,
                    SearchReturnStandardError = 0.4d
                },
                new CombatEpisodeCandidate
                {
                    CandidateId = "other",
                    Legal = true,
                    SearchVisits = 8,
                    SearchDeathRisk = 0.75d,
                    SearchReturnStandardError = 0.5d
                }
            }
        };
        Assert(CombatTrainingReplayWindowSelector.InformationPriority(
                   disagreementFrame,
                   stateFrequency: 1)
               > CombatTrainingReplayWindowSelector.InformationPriority(
                   agreementFrame,
                   stateFrequency: 4),
            "frame replay prioritizes policy disagreement, risk spread, uncertainty, terminal proximity, and novelty");
        var quotaRepairInitial = CombatTrainingReplayWindowSelector.Select(
            quotaReplay.Take(90),
            new CombatTrainingReplayWindowOptions
            {
                MaximumFrames = 100,
                MaximumUnsafeEndTurnShare = 0.30d
            });
        var quotaRepairWindow =
            CombatTrainingReplayWindowSelector.RepairStrategyQuota(
                quotaRepairInitial,
                quotaReplay,
                new CombatTrainingReplayWindowOptions
                {
                    MaximumFrames = 100,
                    MaximumUnsafeEndTurnShare = 0.30d
                });
        Assert(!quotaRepairInitial.StrategyQuotaPassed
               && quotaRepairWindow.StrategyQuotaRepairAttempted
               && quotaRepairWindow.StrategyQuotaRepairSourceEpisodes == 10
               && quotaRepairWindow.StrategyQuotaRepairAddedEpisodes == 10
               && quotaRepairWindow.StrategyQuotaPassed,
            "strategy quota shortfalls trigger targeted shared-corpus collection before fitting without lowering declared quotas");
        var strategyClassReplay = Enumerable.Range(0, 8)
            .Select(index => new CombatEpisode
            {
                EpisodeId = "growth-class-" + index,
                JourneyRunId = "growth-class-" + index,
                Authoritative = true,
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    DifficultyId = "advanced"
                },
                Frames =
                {
                    new CombatEpisodeFrame
                    {
                        ExecutedCandidateId = "chosen",
                        Candidates =
                        {
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "chosen",
                                Legal = true,
                                SearchVisits = 8,
                                Features = index < 4
                                    ? new Dictionary<string, double>
                                    {
                                        ["scaling"] = 1d
                                    }
                                    : new Dictionary<string, double>()
                            },
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "growth-option",
                                Legal = true,
                                SearchVisits = 4,
                                Features = new Dictionary<string, double>
                                {
                                    ["scaling"] = 1d
                                }
                            }
                        }
                    }
                }
            })
            .ToList();
        var strategyClassWindow = CombatTrainingReplayWindowSelector.Select(
            strategyClassReplay,
            new CombatTrainingReplayWindowOptions
            {
                MaximumFrames = 64,
                RequiredStrategyClassFrames = new Dictionary<string, int>
                {
                    ["strategy-growth"] = 4,
                    ["strategy-growth-negative"] = 4
                }
            });
        Assert(strategyClassWindow.StrategyQuotaActive
               && strategyClassWindow.StrategyQuotaPassed
               && strategyClassWindow.StrategyQuotaShortfalls.Count == 0
               && strategyClassWindow.RequiredStrategyClassFrames.Count == 2,
            "targeted replay selection distinguishes positive and negative growth supervision instead of relying only on a single strategy stratum");
        var targetedRequirements = CombatCampaignFoundationTrainer
            .RequiredStrategyClassFrames(
                new CombatTransformerTeacherReport
                {
                    StrategyApplicableCounts = new Dictionary<string, int>
                    {
                        ["growth"] = 11,
                        ["transform"] = 0
                    },
                    StrategyLabelCounts = new Dictionary<string, int>
                    {
                        ["growth"] = 0
                    },
                    StrategyNegativeCounts = new Dictionary<string, int>
                    {
                        ["growth"] = 11
                    }
                });
        Assert(targetedRequirements.Count == 1
               && targetedRequirements["strategy-growth"] == 4,
            "teacher applicability telemetry requests missing growth positives but does not launch futile transform collection when no transform opportunity exists");
        var forcedDecisionReplay = new[] { quotaReplay[0] };
        forcedDecisionReplay[0].Frames[0].Candidates.RemoveAt(1);
        var forcedDecisionWindow = CombatTrainingReplayWindowSelector.Select(
            forcedDecisionReplay);
        Assert(forcedDecisionWindow.SelectedFrames == 1,
            "the shared replay window retains forced decisions for dynamics, outcome, risk, and history supervision");
        var replayWithDuplicate = replayFixture.Concat(new[] { replayFixture[7] }).ToList();
        var deduplicatedReplay = CombatFoundationReplaySampler.Select(
            replayWithDuplicate,
            8,
            enabled: true);
        Assert(deduplicatedReplay.Episodes.Count == 7
               && deduplicatedReplay.DroppedDuplicateEpisodes == 1
               && deduplicatedReplay.Episodes
                   .Select(item => item.EpisodeId)
                   .Distinct(StringComparer.Ordinal)
                   .Count() == 7,
            "foundation replay persistence never expands weighted priorities into duplicate episode payloads");
        var concentratedPolicyTargets =
            CombatPolicyValueBatchTrainer.PolicyTargets(
                new[]
                {
                    new CombatEpisodeCandidate
                    {
                        CandidateId = "dominant",
                        SearchVisits = 100
                    },
                    new CombatEpisodeCandidate
                    {
                        CandidateId = "alternative",
                        SearchVisits = 0
                    }
                },
                "dominant",
                temperature: 1.25d,
                maximumProbability: 0.80d);
        Assert(Math.Abs(concentratedPolicyTargets.Sum() - 1d) < 0.000001d
               && concentratedPolicyTargets.Max() <= 0.800001d
               && concentratedPolicyTargets.Min() >= 0.199999d,
            "policy target temperature and cap preserve probability mass without one-hot collapse");
        var teacherCandidates = new[]
        {
            new CombatEpisodeCandidate
            {
                CandidateId = "dominant",
                TransformerTeacherProbability = 0.10d
            },
            new CombatEpisodeCandidate
            {
                CandidateId = "alternative",
                TransformerTeacherProbability = 0.90d
            }
        };
        var distilledPolicyTargets = concentratedPolicyTargets.ToArray();
        Assert(CombatPolicyValueBatchTrainer.BlendTransformerTeacherTargets(
                   distilledPolicyTargets,
                   teacherCandidates,
                   weight: 0.50d,
                   maximumProbability: 0.95d)
               && Math.Abs(distilledPolicyTargets.Sum() - 1d) < 0.000001d
               && Math.Abs(distilledPolicyTargets[0] - 0.45d) < 0.000001d
               && Math.Abs(distilledPolicyTargets[1] - 0.55d) < 0.000001d,
            "Transformer teacher probabilities distill into bounded tactical policy targets without replacing search supervision");
        teacherCandidates[1].TransformerTeacherProbability = -1d;
        var rejectedTeacherTargets = concentratedPolicyTargets.ToArray();
        Assert(!CombatPolicyValueBatchTrainer.BlendTransformerTeacherTargets(
                   rejectedTeacherTargets,
                   teacherCandidates,
                   weight: 0.50d,
                   maximumProbability: 0.95d)
               && rejectedTeacherTargets.SequenceEqual(concentratedPolicyTargets),
            "incomplete Transformer annotations are rejected instead of partially corrupting a policy target");
        var normalizedTeacherOptions = new CombatTransformerTeacherOptions
        {
            Backend = "CUDA",
            PythonExecutable = "python",
            HiddenDimensions = 65,
            AttentionHeads = 8,
            CpuInteropThreads = 99,
            MicroBatchSize = 999,
            DataLoaderWorkers = 99,
            CpuRefreshInterval = 99,
            CpuEpochs = 999,
            CpuIncrementalEpochs = 999,
            CpuFinalEpochs = 999,
            MaximumFrames = 1,
            AdaptiveRefreshDriftThreshold = 9d,
            MaximumHeadRegression = 9d,
            IncrementalEpochs = 999,
            FinalEpochs = 999,
            DistillationWeight = 4d
        }.Normalized();
        Assert(normalizedTeacherOptions.Backend
                   == CombatTransformerTeacherBackendNames.Cuda
               && normalizedTeacherOptions.HiddenDimensions
                  % normalizedTeacherOptions.AttentionHeads == 0
               && normalizedTeacherOptions.PythonExecutable
                  == CombatTransformerRuntimeProtocol.AutomaticExecutable
               && normalizedTeacherOptions.CpuInteropThreads == 8
               && normalizedTeacherOptions.MicroBatchSize
                  == normalizedTeacherOptions.BatchSize
               && normalizedTeacherOptions.DataLoaderWorkers == 8
               && normalizedTeacherOptions.CpuRefreshInterval == 8
               && normalizedTeacherOptions.CpuEpochs
                  == normalizedTeacherOptions.Epochs
               && normalizedTeacherOptions.CpuIncrementalEpochs
                  == normalizedTeacherOptions.CpuEpochs
               && normalizedTeacherOptions.CpuFinalEpochs == 100
               && normalizedTeacherOptions.MaximumFrames
                  == normalizedTeacherOptions.MinimumFrames
               && normalizedTeacherOptions.AdaptiveRefreshDriftThreshold == 1d
               && normalizedTeacherOptions.MaximumHeadRegression == 0.50d
               && normalizedTeacherOptions.IncrementalEpochs
                  == normalizedTeacherOptions.Epochs
               && normalizedTeacherOptions.FinalEpochs == 100
               && normalizedTeacherOptions.EnableWarmStart
               && normalizedTeacherOptions.DistillationWeight == 0.75d,
            "Transformer teacher settings normalize portable CPU/CUDA configuration and attention dimensions");
        var continuationRequestA = new CombatCampaignFoundationTrainingRequest
        {
            ContentSetHash = "content",
            OwnerModSetHash = "owners",
            TransformerTeacher = new CombatTransformerTeacherOptions
            {
                Backend = CombatTransformerTeacherBackendNames.Cuda,
                RandomSeed = 101
            }
        };
        var continuationRequestB = new CombatCampaignFoundationTrainingRequest
        {
            ContentSetHash = "content",
            OwnerModSetHash = "owners",
            TransformerTeacher = new CombatTransformerTeacherOptions
            {
                Backend = CombatTransformerTeacherBackendNames.Cpu,
                RandomSeed = 202
            }
        };
        Assert(CombatCampaignFoundationTrainer.ManifestCompatible(
                   CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
                       continuationRequestA,
                       "rules"),
                   CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
                       continuationRequestB,
                       "rules")),
            "student continuation identity remains stable when only Transformer runtime backend or random seed changes");
        var teacherCorpusManifest = new CombatFoundationCompatibilityManifest
        {
            RulesetHash = new string('a', 64),
            ContentSetHash = new string('b', 64),
            OwnerModSetHash = new string('c', 64),
            NativeProgramPackageHash = new string('d', 64),
            TrainingCampaignHash = new string('e', 64),
            FeatureSchemaVersion = CombatPolicyValueProtocol.FeatureSchemaVersion,
            FeatureEncodingMode = "partitioned-v4",
            TrainingSemanticsVersion =
                CombatPolicyValueProtocol.TrainingSemanticsVersion,
            TrainingPolicyVersion = CombatFoundationTrainingProtocol.TrainingPolicyVersion
        };
        var corpusKeyA = CombatTransformerTeacherCorpusProtocol.CorpusCompatibilityKey(
            teacherCorpusManifest,
            "balanced",
            new CombatTransformerTeacherOptions().Normalized());
        var corpusKeyB = CombatTransformerTeacherCorpusProtocol.CorpusCompatibilityKey(
            teacherCorpusManifest,
            "balanced",
            new CombatTransformerTeacherOptions().Normalized());
        var governanceOnlyCorpusKey =
            CombatTransformerTeacherCorpusProtocol.CorpusCompatibilityKey(
                new CombatFoundationCompatibilityManifest
                {
                    RulesetHash = teacherCorpusManifest.RulesetHash,
                    ContentSetHash = teacherCorpusManifest.ContentSetHash,
                    OwnerModSetHash = teacherCorpusManifest.OwnerModSetHash,
                    NativeProgramPackageHash =
                        teacherCorpusManifest.NativeProgramPackageHash,
                    TrainingCampaignHash = "different-schedule",
                    FeatureSchemaVersion = teacherCorpusManifest.FeatureSchemaVersion,
                    FeatureEncodingMode = teacherCorpusManifest.FeatureEncodingMode,
                    TrainingSemanticsVersion =
                        teacherCorpusManifest.TrainingSemanticsVersion,
                    TrainingPolicyVersion = "different-governance"
                },
                "balanced",
                new CombatTransformerTeacherOptions().Normalized());
        var semanticChangeCorpusKey =
            CombatTransformerTeacherCorpusProtocol.CorpusCompatibilityKey(
                new CombatFoundationCompatibilityManifest
                {
                    RulesetHash = teacherCorpusManifest.RulesetHash,
                    ContentSetHash = teacherCorpusManifest.ContentSetHash,
                    OwnerModSetHash = teacherCorpusManifest.OwnerModSetHash,
                    NativeProgramPackageHash =
                        teacherCorpusManifest.NativeProgramPackageHash,
                    FeatureSchemaVersion = teacherCorpusManifest.FeatureSchemaVersion,
                    FeatureEncodingMode = teacherCorpusManifest.FeatureEncodingMode,
                    TrainingSemanticsVersion = "different-training-semantics"
                },
                "balanced",
                new CombatTransformerTeacherOptions().Normalized());
        var teacherKey = CombatTransformerTeacherCorpusProtocol.TeacherCompatibilityKey(
            corpusKeyA,
            new CombatTransformerTeacherOptions().Normalized());
        Assert(corpusKeyA.Length == 64
               && corpusKeyA == corpusKeyB
               && corpusKeyA == governanceOnlyCorpusKey
               && corpusKeyA != semanticChangeCorpusKey
               && teacherKey.Length == 64
               && corpusKeyA != teacherKey
               && CombatTransformerTeacherCorpusProtocol
                   .ShouldUseIncrementalColdStartExport(
                       existingFrames: 512,
                       sourceFrameUpperBound: 1024,
                       minimumTrainingFrames: 4096)
               && !CombatTransformerTeacherCorpusProtocol
                   .ShouldUseIncrementalColdStartExport(
                       existingFrames: 3072,
                       sourceFrameUpperBound: 1024,
                       minimumTrainingFrames: 4096)
               && CombatTransformerTeacherCorpusProtocol.ShouldUseIncrementalExport(
                   existingFrames: 4096,
                   sourceFrameUpperBound: 1024)
               && CombatTransformerTeacherCorpusProtocol.CorpusMaturity(1024, 1024)
                  == CombatTransformerTeacherCorpusProtocol.BootstrapMaturity
               && CombatTransformerTeacherCorpusProtocol.CorpusMaturity(2048, 1024)
                  == CombatTransformerTeacherCorpusProtocol.ProvisionalMaturity
               && CombatTransformerTeacherCorpusProtocol.CorpusMaturity(4096, 1024)
                  == CombatTransformerTeacherCorpusProtocol.MatureMaturity
               && CombatTransformerTeacherCorpusProtocol
                      .IncrementalReplayShare(0) == 0.25d
               && CombatTransformerTeacherCorpusProtocol
                      .IncrementalReplayShare(1) == 0.50d
               && CombatTransformerTeacherCorpusProtocol
                      .IncrementalReplayShare(2) == 0.75d,
            "Transformer teacher identities are deterministic and cold-start export stays incremental until the next source window can reach training scale");
        Assert(!CombatTransformerTeacherApplicationProtocol
                   .HasUsableTeacherSource(new CombatTransformerTeacherReport
                   {
                       WarmStarted = true,
                       TrainingRefreshed = false,
                       UpdateAccepted = false,
                       TeacherGeneration = 0
                   }),
            "a loaded legacy or external Transformer checkpoint without an accepted generation cannot bypass the teacher source gate");
        Assert(CombatTransformerTeacherFailureProtocol.BlocksFormalModel(
                   new CombatTransformerTeacherReport
                   {
                       Requested = true,
                       FailureKind =
                           CombatTransformerTeacherFailureKinds.Configuration,
                       FormalModelBlocked = true
                   })
               && !CombatTransformerTeacherFailureProtocol.BlocksFormalModel(
                   new CombatTransformerTeacherReport
                   {
                       Requested = true,
                       FailureKind =
                           CombatTransformerTeacherFailureKinds.TransientResource,
                       RetryableFailure = true
                   })
               && !CombatTransformerTeacherFailureProtocol.BlocksFormalModel(
                   new CombatTransformerTeacherReport
                   {
                       Requested = true,
                       FailureKind = CombatTransformerTeacherFailureKinds.Process,
                       RetryableFailure = true
                   }),
            "only explicitly classified permanent Transformer failures block formal model training; resource and unknown process failures remain retryable");
        Assert(!CombatTransformerTeacherApplicationProtocol
                   .HasUsableTeacherSource(new CombatTransformerTeacherReport
                   {
                       WarmStarted = false,
                       TrainingRefreshed = true,
                       UpdateAccepted = false,
                       TeacherGeneration = 0
                   })
               && CombatTransformerTeacherApplicationProtocol
                   .HasUsableTeacherSource(new CombatTransformerTeacherReport
                   {
                       WarmStarted = false,
                       TrainingRefreshed = true,
                       UpdateAccepted = true,
                       TeacherGeneration = 1
                   })
               && CombatTransformerTeacherApplicationProtocol
                   .HasUsableTeacherSource(new CombatTransformerTeacherReport
                   {
                       WarmStarted = true,
                       TrainingRefreshed = true,
                       UpdateAccepted = false,
                       TeacherGeneration = 3
                   }),
            "cold Transformer teachers require an accepted trained generation while rejected warm refreshes retain the persisted teacher");
        var groupedTransformerRows = Enumerable.Range(0, 4)
            .Select(index => new CombatTransformerTrainingRow
            {
                RowIndex = index,
                RunKey = "journey:a",
                Identity = "a:" + index
            })
            .Concat(Enumerable.Range(4, 4).Select(index =>
                new CombatTransformerTrainingRow
                {
                    RowIndex = index,
                    RunKey = "journey:b",
                    Identity = "b:" + index
                }))
            .Concat(Enumerable.Range(8, 7).Select(index =>
                new CombatTransformerTrainingRow
                {
                    RowIndex = index,
                    RunKey = "journey:oversized",
                    Identity = "oversized:" + index
                }))
            .ToArray();
        var boundedTransformerRows = CombatTransformerTeacherCorpusProtocol
            .SelectWholeRunRows(groupedTransformerRows, 8, "seed");
        var repeatedBoundedTransformerRows =
            CombatTransformerTeacherCorpusProtocol.SelectWholeRunRows(
                groupedTransformerRows,
                8,
                "seed");
        var priorityTransformerRows = groupedTransformerRows
            .Where(row => row.RowIndex < 8)
            .Select(row => new CombatTransformerTrainingRow
            {
                RowIndex = row.RowIndex,
                RunKey = row.RunKey,
                Identity = row.Identity,
                Priority = row.RunKey == "journey:b" ? 0 : 4
            })
            .ToArray();
        var selectedPriorityRows = CombatTransformerTeacherCorpusProtocol
            .SelectWholeRunRows(priorityTransformerRows, 4, "seed");
        var selectedTransformerRuns = groupedTransformerRows
            .Where(row => boundedTransformerRows.Contains(row.RowIndex))
            .GroupBy(row => row.RunKey)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert(boundedTransformerRows.Count <= 8
               && boundedTransformerRows.SequenceEqual(
                   repeatedBoundedTransformerRows)
               && selectedTransformerRuns.All(pair =>
                   pair.Value == groupedTransformerRows.Count(row =>
                       row.RunKey == pair.Key))
               && !boundedTransformerRows.Any(index => index >= 8)
               && selectedPriorityRows.SequenceEqual(Enumerable.Range(4, 4))
               && CombatTransformerTeacherCorpusProtocol.SelectWholeRunRows(
                       groupedTransformerRows,
                       3,
                       "seed")
                   .Count == 0,
            "incremental Transformer replay is deterministic, budget-bounded, prioritizes complete supervision, and never splits one Journey run across the selected boundary");
        var runtimeDisplay = CombatTransformerRuntimeResolver.DisplayText(
            new CombatTransformerRuntimeProbe
            {
                Success = true,
                ResolutionSource = "managed-cpu",
                EffectiveBackend = "cpu",
                PythonVersion = "3.14.3",
                TorchVersion = "2.13.0+cpu",
                DeviceName = "test-cpu",
                ExecutablePath = "C:/AuraTF/python.exe"
            });
        Assert(runtimeDisplay.Contains("managed-cpu", StringComparison.Ordinal)
               && runtimeDisplay.Contains("Python 3.14.3", StringComparison.Ordinal)
               && runtimeDisplay.Contains("C:/AuraTF/python.exe", StringComparison.Ordinal),
            "Transformer runtime probes expose their resolved executable and capabilities to the controller");
        var illegalExecutedFrame = new CombatEpisodeFrame
        {
            ExecutedCandidateId = "prohibited",
            Candidates =
            {
                new CombatEpisodeCandidate
                {
                    CandidateId = "safe",
                    Legal = true,
                    SearchVisits = 0
                },
                new CombatEpisodeCandidate
                {
                    CandidateId = "prohibited",
                    Legal = false,
                    SearchVisits = 100,
                    Features =
                    {
                        [CombatRoleStrategyFeatureNames.StrategicallyProhibited] = 1d
                    }
                }
            }
        };
        Assert(!CombatPolicyValueBatchTrainer.PolicyIntegrityValidForTraining(
                   illegalExecutedFrame),
            "batch training rejects frames whose executed action is outside the decision-legal policy set");
        var dominatedEndTurnTargets = new[] { 0.25d, 0.75d };
        CombatPolicyValueBatchTrainer.SuppressPolicyTarget(
            dominatedEndTurnTargets,
            1);
        Assert(Math.Abs(dominatedEndTurnTargets[0] - 1d) < 0.000001d
               && dominatedEndTurnTargets[1] == 0d,
            "counterfactual policy targets assign zero mass to a deterministically dominated end turn");
        var endTurnTrainingEpisodes = Enumerable.Range(0, 4)
            .Select(index => new CombatEpisode
            {
                EpisodeId = "end-turn-specialist-" + index,
                JourneyRunId = "end-turn-specialist:" + index,
                JourneyBattleIndex = index,
                Authoritative = true,
                DecisionProfile = "balanced",
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    DifficultyId = index % 2 == 0 ? "normal" : "advanced",
                    OutcomeClass = index % 2 == 0 ? "defeat" : "victory",
                    FinalBossVictory = index % 2 != 0
                },
                Frames =
                {
                    new CombatEpisodeFrame
                    {
                        ExecutedCandidateId = index == 0 ? "end" : "play",
                        StateFeatures =
                        {
                            ["power"] = 2d
                        },
                        Candidates =
                        {
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "play",
                                SourceId = "card:test",
                                Legal = true,
                                SearchVisits = 7
                            },
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "end",
                                SourceId = "simulation:end-turn",
                                Legal = index != 1,
                                SearchVisits = 3,
                                Features = index == 1
                                    ? new Dictionary<string, double>
                                    {
                                        [CombatTurnFeatureNames.EndTurnDominated] = 1d
                                    }
                                    : new Dictionary<string, double>()
                            }
                        }
                    }
                }
            })
            .ToList();
        var endTurnSpecialistTraining = CombatPolicyValueTrainer.Train(
            endTurnTrainingEpisodes,
            "balanced",
            new CombatPolicyValueTrainingOptions
            {
                Epochs = 5,
                MinimumEpisodes = 2,
                StateDimensions = 16,
                ActionDimensions = 16,
                HiddenDimensions = 8,
                EnableEndTurnSpecialization = true,
                EndTurnFrameWeight = 2d,
                MaximumDegreeOfParallelism = 1
            });
        Assert(endTurnSpecialistTraining.Success
               && endTurnSpecialistTraining.EndTurnDecisionFrames == 4
               && endTurnSpecialistTraining.UnsafeEndTurnFrames == 2
               && endTurnSpecialistTraining.FrameStrata.Keys.Any(key =>
                   key.EndsWith(
                       ":unsafe-end-turn",
                       StringComparison.Ordinal)),
            "end-turn specialist identifies discretionary and unsafe end turns as dedicated weighted strata");
        var imbalancedEndTurnEpisodes = Enumerable.Range(0, 100)
            .Select(index =>
            {
                var template = endTurnTrainingEpisodes[index < 90 ? 0 : 2];
                var clone = JsonSerializer.Deserialize<CombatEpisode>(
                    JsonSerializer.Serialize(template))!;
                clone.EpisodeId = "end-turn-cap-" + index;
                clone.JourneyRunId = "end-turn-cap:" + index;
                return clone;
            })
            .ToList();
        var balancedEndTurnTraining = CombatPolicyValueTrainer.Train(
            imbalancedEndTurnEpisodes,
            "balanced",
            new CombatPolicyValueTrainingOptions
            {
                Epochs = 5,
                MinimumEpisodes = 2,
                StateDimensions = 16,
                ActionDimensions = 16,
                HiddenDimensions = 8,
                MaximumUnsafeEndTurnFrameShare = 0.35d,
                MaximumDegreeOfParallelism = 1
            });
        Assert(balancedEndTurnTraining.Success
               && balancedEndTurnTraining.DroppedUnsafeEndTurnFrames > 0
               && balancedEndTurnTraining.UnsafeEndTurnRiskAuxiliaryFrames > 0
               && balancedEndTurnTraining.TrainingFrameCount > 0
               && (double)balancedEndTurnTraining.UnsafeEndTurnPolicyFrames
                  / balancedEndTurnTraining.TrainingFrameCount <= 0.351d,
            "large imbalanced training sets cap policy-facing unsafe end turns while retaining a bounded risk-only auxiliary batch");
        var priorityReplayFixture = Enumerable.Range(0, 10)
            .Select(index => new CombatEpisode
            {
                EpisodeId = "priority-" + index,
                JourneyRunId = "priority:normal:" + index,
                JourneyBattleIndex = index,
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    DifficultyId = "normal",
                    OutcomeClass = "defeat",
                    FailureBattleIndex = index == 9 ? index : 36
                },
                Frames =
                {
                    new CombatEpisodeFrame
                    {
                        LongTermReturn = index == 9 ? -1d : 0d,
                        ExecutedCandidateId = "play",
                        Candidates =
                        {
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "play",
                                SourceId = "card:test",
                                Legal = true,
                                SearchVisits = 8,
                                SearchValue = index == 9 ? 1d : 0d,
                                SearchDeathRisk = index == 9 ? 1d : 0d
                            },
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "end",
                                SourceId = "simulation:end-turn",
                                Legal = true,
                                SearchVisits = 2
                            }
                        }
                    }
                }
            })
            .ToList();
        var priorityReplay = CombatFoundationReplaySampler.Select(
            priorityReplayFixture,
            4,
            enabled: true,
            balance: new CombatFoundationReplayBalanceOptions
            {
                EnablePrioritySampling = true
            });
        Assert(priorityReplay.Episodes.Any(item => item.EpisodeId == "priority-9")
               && priorityReplay.SelectedPriorityMean
                  >= priorityReplay.SourcePriorityMean
               && priorityReplay.SelectedHighPriorityEpisodes > 0,
            "prioritized replay retains high-error, high-risk end-turn decisions before low-information recent episodes");
        Assert(
            CombatCampaignFoundationTrainer.RequiredWilsonVictories(200, 0.80d)
            > 160,
            "final validation derives victory requirements from the Wilson lower bound instead of the point estimate");

        CombatCampaignResult CaseCampaign(
            ulong seed,
            bool victory,
            string archetype,
            params string[] deck)
        {
            return new CombatCampaignResult
            {
                CampaignId = "case-learning",
                CampaignVersion = "1",
                DifficultyId = "normal",
                WorldSeed = seed,
                PlanHash = "plan-" + seed,
                PolicyId = victory ? "winner" : "failure",
                FinalBossVictory = victory,
                CampaignVictory = victory,
                ReachedFinalBoss = victory,
                CompletedBattles = victory ? 37 : 31,
                TotalBattles = 37,
                BattleSemanticCoverage = 1d,
                ProgressionSemanticCoverage = 1d,
                FinalState = new CombatCampaignState
                {
                    CurrentHp = victory ? 70 : 0,
                    MaxHp = 100,
                    Deck = deck.ToList(),
                    BuildPlan = new CombatCampaignBuildPlan
                    {
                        PrimaryArchetype = archetype
                    }
                },
                Battles =
                {
                    new CombatSimulationResult
                    {
                        ScenarioId = victory ? "final-boss" : "late-elite",
                        RulesetHash = "case-rules",
                        Outcome = victory
                            ? CombatSimulationOutcome.Victory
                            : CombatSimulationOutcome.Defeat,
                        TerminalConsistencyValid = true,
                        SemanticCoverage = 1d,
                        Turns = victory ? 4 : 12,
                        FinalPlayerHp = victory ? 70 : 0,
                        Metrics = new CombatSimulationMetrics
                        {
                            CardsPlayed = victory ? 8 : 20,
                            DamageDealt = victory ? 300 : 180,
                            DamageTaken = victory ? 30 : 100
                        }
                    }
                }
            };
        }

        var caseEpisode = new CombatEpisode
        {
            EpisodeId = "case-success-episode",
            RulesetHash = "case-rules",
            Authoritative = true,
            SemanticCoverage = 1d,
            JourneyBattleIndex = 36,
            Campaign = new CombatCampaignEpisodeMetadata
            {
                FinalBossVictory = true,
                IntegrityValid = true,
                DifficultyId = "normal",
                OutcomeClass = "victory"
            }
        };
        var successfulCaseCampaign = CaseCampaign(
            100UL,
            true,
            "cycle",
            "engine",
            "draw");
        var failedCaseCampaign = CaseCampaign(
            100UL,
            false,
            "cycle",
            "plain",
            "plain",
            "filler");
        var successfulObservation = CombatFoundationCaseLearning.Observe(
            successfulCaseCampaign,
            "arena",
            1,
            "candidate",
            "case-rules",
            "case-campaign-fingerprint",
            "case-native-package",
            CombatFoundationTrainingProtocol.TrainingPolicyVersion,
            "balanced",
            "model-success",
            new[] { caseEpisode });
        var failedObservation = CombatFoundationCaseLearning.Observe(
            failedCaseCampaign,
            "arena",
            1,
            "champion",
            "case-rules",
            "case-campaign-fingerprint",
            "case-native-package",
            CombatFoundationTrainingProtocol.TrainingPolicyVersion,
            "balanced",
            "model-failure");
        var policyInvalidCaseEpisode = new CombatEpisode
        {
            EpisodeId = "case-policy-invalid-episode",
            RulesetHash = "case-rules",
            Authoritative = true,
            SemanticCoverage = 1d,
            Campaign = new CombatCampaignEpisodeMetadata
            {
                FinalBossVictory = true,
                IntegrityValid = true,
                DifficultyId = "normal",
                OutcomeClass = "victory"
            },
            Frames = { illegalExecutedFrame }
        };
        var policyInvalidObservation = CombatFoundationCaseLearning.Observe(
            successfulCaseCampaign,
            "arena",
            1,
            "candidate",
            "case-rules",
            "case-campaign-fingerprint",
            "case-native-package",
            CombatFoundationTrainingProtocol.TrainingPolicyVersion,
            "balanced",
            "model-policy-invalid",
            new[] { policyInvalidCaseEpisode });
        var caseAnalysis = CombatFoundationCaseLearning.Analyze(
            new[] { successfulObservation, failedObservation });
        Assert(successfulObservation.ArchiveEligible
               && successfulObservation.PolicyIntegrityValid
               && !policyInvalidObservation.PolicyIntegrityValid
               && !policyInvalidObservation.ArchiveEligible
               && successfulObservation.RobustnessScore > 0d
               && caseAnalysis.SuccessfulCases == 1
               && caseAnalysis.FailedCases == 1
               && caseAnalysis.MatchedPairs == 1
               && caseAnalysis.Pairs[0].SuccessSeed
               == caseAnalysis.Pairs[0].FailureSeed,
            "foundation success learning archives only policy-valid authoritative wins and builds same-seed comparisons");
        var archivedCase = CombatFoundationCaseLearning.CreateSuccessCase(
            successfulCaseCampaign,
            successfulObservation,
            new[] { caseEpisode });
        var compatibleExpertEpisodes =
            CombatFoundationCaseLearning.SelectExpertEpisodes(
                new[] { archivedCase },
                "case-learning",
                "1",
                "case-campaign-fingerprint",
                "case-rules",
                "case-native-package",
                CombatFoundationTrainingProtocol.TrainingPolicyVersion,
                8);
        var incompatibleExpertEpisodes =
            CombatFoundationCaseLearning.SelectExpertEpisodes(
                new[] { archivedCase },
                "case-learning",
                "1",
                "case-campaign-fingerprint",
                "different-rules",
                "case-native-package",
                CombatFoundationTrainingProtocol.TrainingPolicyVersion,
                8);
        Assert(compatibleExpertEpisodes.Count == 1
               && incompatibleExpertEpisodes.Count == 0
               && CombatFoundationCaseLearning.CompatibilityKey(
                   "case-learning",
                   "1",
                   "case-campaign-fingerprint",
                   "case-rules",
                   "case-native-package",
                   CombatFoundationTrainingProtocol.TrainingPolicyVersion)
               == successfulObservation.CompatibilityKey,
            "foundation expert replay is bounded and isolated by campaign, ruleset and feature protocol");
        var stratifiedCompatibilityKey =
            CombatFoundationCaseLearning.CompatibilityKey(
                "case-learning",
                "1",
                "case-campaign-fingerprint",
                "case-rules",
                "case-native-package",
                CombatFoundationTrainingProtocol.TrainingPolicyVersion);
        var stratifiedExpertCases = Enumerable.Range(0, 8)
            .Select(caseIndex =>
            {
                var advanced = caseIndex >= 6;
                return new CombatFoundationSuccessCase
                {
                    Observation = new CombatFoundationCampaignObservation
                    {
                        CaseId = "stratified-case-" + caseIndex,
                        ArchiveEligible = true,
                        PolicyIntegrityValid = true,
                        CampaignId = "case-learning",
                        CampaignVersion = "1",
                        RulesetHash = "case-rules",
                        CompatibilityKey = stratifiedCompatibilityKey,
                        DifficultyId = advanced ? "advanced" : "normal",
                        StrategyFingerprint = "strategy-" + (caseIndex % 3),
                        RobustnessScore = 1d - caseIndex * 0.01d
                    },
                    Episodes = Enumerable.Range(0, 4)
                        .Select(battleIndex => new CombatEpisode
                        {
                            EpisodeId = "stratified-episode-"
                                        + caseIndex
                                        + "-"
                                        + battleIndex,
                            JourneyRunId = "stratified-run-" + caseIndex,
                            JourneyBattleIndex = battleIndex,
                            RulesetHash = "case-rules",
                            Authoritative = true,
                            Campaign = new CombatCampaignEpisodeMetadata
                            {
                                DifficultyId = advanced ? "advanced" : "normal",
                                IntegrityValid = true,
                                FinalBossVictory = true
                            }
                        })
                        .ToList()
                };
            })
            .ToList();
        var stratifiedExpertSelection =
            CombatFoundationCaseLearning.SelectExpertReplay(
                stratifiedExpertCases,
                "case-learning",
                "1",
                "case-campaign-fingerprint",
                "case-rules",
                "case-native-package",
                CombatFoundationTrainingProtocol.TrainingPolicyVersion,
                episodeLimit: 16,
                targetAdvancedShare: 0.35d,
                maximumEpisodesPerRun: 2);
        Assert(stratifiedExpertSelection.Episodes.Count == 16
               && stratifiedExpertSelection.SelectedAdvancedEpisodes == 4
               && stratifiedExpertSelection.SelectedNormalEpisodes == 12
               && stratifiedExpertSelection.DistinctRuns == 8
               && stratifiedExpertSelection.QuotaShortfalls["advanced"] == 2
               && !stratifiedExpertSelection.QuotaShortfalls.ContainsKey("normal"),
            "expert replay preserves normal evidence and fills unused capacity while reporting scarce advanced success cases");
        Assert(CombatCampaignFoundationTrainer.EffectiveAdvancedTrainingFloor(
                   0.35d,
                   stratifiedExpertSelection) > 0.35d,
            "advanced expert replay shortages raise the next training curriculum floor instead of being silently replaced by normal episodes");
        var rewardResidualObservations = Enumerable.Range(0, 60)
            .Select(index => new CombatFoundationCampaignObservation
            {
                CaseId = "reward-residual-" + index,
                IntegrityValid = true,
                FinalBossVictory = index < 30,
                CompletedBattles = index < 30 ? 37 : 34,
                SelectedCards =
                {
                    index < 30 ? "learned-good-card" : "learned-bad-card"
                },
                Relics =
                {
                    index < 30 ? "relic_learned_good" : "relic_learned_bad"
                },
                Blessings =
                {
                    index < 30
                        ? "blessing_learned_good"
                        : "blessing_learned_bad"
                },
                DifficultyId = "advanced",
                TotalBattles = 37,
                PrimaryArchetype = "control",
                RewardChoices =
                {
                    new CombatFoundationRewardChoiceObservation
                    {
                        RewardId = index < 30
                            ? "learned-good-card"
                            : "learned-bad-card",
                        EncounterIndex = 2,
                        DifficultyId = "advanced",
                        PrimaryArchetype = "control"
                    }
                }
            })
            .ToList();
        var rewardResidualTraining =
            CombatFoundationCaseLearning.TrainRewardResiduals(
                rewardResidualObservations);
        Assert(rewardResidualTraining.EligibleObservations == 60
               && rewardResidualTraining.Residuals["learned-good-card"] > 0d
               && rewardResidualTraining.Residuals["learned-bad-card"] < 0d
               && rewardResidualTraining.Residuals["relic_learned_good"] > 0d
               && rewardResidualTraining.Residuals["relic_learned_bad"] < 0d
               && rewardResidualTraining.Residuals["blessing_learned_good"] > 0d
               && rewardResidualTraining.Residuals["blessing_learned_bad"] < 0d
               && rewardResidualTraining.CardResiduals == 2
               && rewardResidualTraining.RelicResiduals == 2
               && rewardResidualTraining.BlessingResiduals == 2
               && rewardResidualTraining.ConditionalResidualCount >= 2
               && CombatRewardConditionalResidualProtocol.Resolve(
                      rewardResidualTraining.ConditionalResiduals,
                      "learned-good-card",
                      "advanced",
                      2,
                      "control") > 0d
               && CombatRewardConditionalResidualProtocol.Resolve(
                      rewardResidualTraining.ConditionalResiduals,
                      "learned-bad-card",
                      "advanced",
                      2,
                      "control") < 0d
               && rewardResidualTraining.Residuals.Values.All(value =>
                   Math.Abs(value) <= 0.20d),
            "reward residual learning uses late comparable outcomes and hard-bounds every learned adjustment");
        var archetypeFixture = new CombatCampaignState
        {
            BuildPlan = new CombatCampaignBuildPlan
            {
                PrimaryArchetype = "reliability"
            },
            Deck =
            {
                "burningcard_1",
                "burningcard_2",
                "elementscard_1"
            }
        };
        Assert(CombatFoundationCaseLearning.ResolveBuildArchetype(archetypeFixture)
               == "burning",
            "archived reward observations derive a concrete deck family instead of collapsing every build into reliability");

        var hardSeedEpisodes = Enumerable.Range(0, 5)
            .Select(index => new CombatEpisode
            {
                EpisodeId = "hard-seed-" + index,
                JourneyRunId = "hard-seed-run-" + index,
                JourneyBattleIndex = 10 + index,
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    WorldSeed = (ulong)(50_000 + index),
                    DifficultyId = index == 4 ? "advanced" : "normal",
                    CampaignCompletedBattles = 10 + index,
                    TerminalScenarioId = index < 4
                        ? "recurring-gatekeeper"
                        : "one-off-failure",
                    OutcomeClass = index == 3 ? "victory" : "defeat",
                    IntegrityValid = true
                }
            })
            .ToList();
        var hardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
            hardSeedEpisodes,
            campaignCount: 8,
            replayShare: 0.5d,
            iteration: 2,
            runSeed: 123456789UL,
            enabled: true);
        var hardSeedRepeat = CombatFoundationHardSeedCurriculum.Select(
            hardSeedEpisodes,
            campaignCount: 8,
            replayShare: 0.5d,
            iteration: 2,
            runSeed: 123456789UL,
            enabled: true);
        Assert(hardSeedPlan.SourceCampaigns == 4
               && hardSeedPlan.Seeds.Count == 4
               && hardSeedPlan.Seeds.All(seed => seed.WorldSeed != 50_003UL)
               && hardSeedPlan.Seeds
                   .Select(seed => seed.WorldSeed)
                   .SequenceEqual(hardSeedRepeat.Seeds.Select(seed => seed.WorldSeed))
               && hardSeedPlan.Clusters["recurring-gatekeeper"] == 3,
            "hard-seed curriculum deterministically replays valid prior defeats and emphasizes recurring terminal clusters");
        var weightedHardSeedHistory = Enumerable.Range(0, 10)
            .Select(index => new CombatFoundationHardSeedHistoryEntry
            {
                WorldSeed = (ulong)(60_000 + index),
                DifficultyId = "normal",
                TerminalScenarioId = index < 5
                    ? "campaign:5:level_10011"
                    : index < 8
                        ? "campaign:36:final-boss-" + index
                        : index == 8
                            ? "campaign:5:level_10040"
                            : "campaign:5:other",
                FailureOccurrences = 1,
                FirstSeenIteration = 1,
                LastSeenIteration = 1
            })
            .ToList();
        var weightedHardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
            weightedHardSeedHistory,
            campaignCount: 20,
            replayShare: 0.5d,
            iteration: 2,
            runSeed: 321UL,
            enabled: true,
            encounterWeights: new Dictionary<string, double>
            {
                ["level_10011"] = 0.50d,
                ["@final-boss"] = 0.30d,
                ["level_10040"] = 0.10d,
                ["@other"] = 0.10d
            });
        Assert(weightedHardSeedPlan.SourceCategories["target:level_10011"] == 5
               && weightedHardSeedPlan.SourceCategories["target:@final-boss"] == 3
               && weightedHardSeedPlan.SourceCategories["target:level_10040"] == 1
               && weightedHardSeedPlan.SourceCategories["target:@other"] == 1,
            "content-owned encounter weights reserve hard-seed curriculum capacity for the two gatekeepers and final bosses");
        var cooledHardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
            new[]
            {
                new CombatFoundationHardSeedHistoryEntry
                {
                    WorldSeed = 66_001UL,
                    DifficultyId = "normal",
                    TerminalScenarioId = "unsolved-gate",
                    FailureOccurrences = 3,
                    TrainingAttempts = 2,
                    RecoverySuccesses = 0,
                    LastTrainedIteration = 4
                }
            },
            campaignCount: 8,
            replayShare: 0.35d,
            iteration: 5,
            runSeed: 123UL,
            enabled: true);
        Assert(cooledHardSeedPlan.SourceCampaigns == 0
               && cooledHardSeedPlan.Seeds.Count == 0,
            "repeated hard seeds with no recovery enter a cooldown instead of consuming every following curriculum round");
        var buildLimitedHardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
            new[]
            {
                new CombatFoundationHardSeedHistoryEntry
                {
                    WorldSeed = 66_002UL,
                    DifficultyId = "advanced",
                    TerminalScenarioId = "build-limited-gate",
                    FailureOccurrences = 4,
                    SolvabilityClass = "build-limited"
                }
            },
            campaignCount: 8,
            replayShare: 0.35d,
            iteration: 6,
            runSeed: 124UL,
            enabled: true);
        Assert(buildLimitedHardSeedPlan.SourceCampaigns == 0
               && buildLimitedHardSeedPlan.Seeds.Count == 0,
            "oracle-rejected build-limited seeds leave combat-policy replay and are routed away from repeated local action training");
        var hardEncounterCheckpoint = new CombatCampaignCheckpoint
        {
            CampaignId = "hard-encounter",
            CampaignVersion = "1",
            DifficultyId = "advanced",
            WorldSeed = 77_001UL,
            PlanHash = "hard-plan",
            PolicyId = "aura-foundation-training:balanced",
            NextEncounterIndex = 5
        };
        var hardEncounterPlan = CombatFoundationHardSeedCurriculum.Select(
            new[]
            {
                new CombatFoundationHardSeedHistoryEntry
                {
                    WorldSeed = 77_001UL,
                    DifficultyId = "advanced",
                    TerminalScenarioId = "hard-encounter:5:gate",
                    CompletedBattles = 6,
                    FirstSeenIteration = 1,
                    LastSeenIteration = 1,
                    FailureOccurrences = 2,
                    FailureEncounterCheckpoint = hardEncounterCheckpoint
                }
            },
            campaignCount: 4,
            replayShare: 0.5d,
            iteration: 2,
            runSeed: 99UL,
            enabled: true);
        var hardEncounterSchedule = CombatFoundationTrainingSchedule.Build(
            4,
            80_000UL,
            99UL,
            2,
            CombatFoundationCurriculum.Evaluate(
                true,
                2,
                10,
                32,
                0,
                32),
            hardEncounterPlan);
        Assert(hardEncounterPlan.Seeds.Single().FailureEncounterCheckpoint
                   ?.NextEncounterIndex == 5
               && hardEncounterSchedule.Single(slot => slot.HardSeed)
                   .FailureEncounterCheckpoint?.NextEncounterIndex == 5,
            "hard-seed planning carries the compact failed-encounter checkpoint into the training schedule");
        var terminalCreditEpisodes = Enumerable.Range(0, 3)
            .Select(index => new CombatEpisode
            {
                JourneyBattleIndex = index,
                Frames =
                {
                    new CombatEpisodeFrame(),
                    new CombatEpisodeFrame()
                }
            })
            .ToList();
        var terminalCreditCampaign = new CombatCampaignResult
        {
            WorldSeed = 9001UL,
            DifficultyId = "normal",
            CompletedBattles = 3,
            TotalBattles = 37,
            FinalState = new CombatCampaignState
            {
                CurrentHp = 77,
                MaxHp = 143,
                SpecialVariables = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["DoomPower"] = "19"
                }
            },
            Battles =
            {
                new CombatSimulationResult
                {
                    ScenarioId = "won-1",
                    Outcome = CombatSimulationOutcome.Victory
                },
                new CombatSimulationResult
                {
                    ScenarioId = "won-2",
                    Outcome = CombatSimulationOutcome.Victory
                },
                new CombatSimulationResult
                {
                    ScenarioId = "lost-3",
                    Outcome = CombatSimulationOutcome.Defeat
                }
            }
        };
        CombatCampaignFoundationTrainer.ApplyCampaignTargets(
            terminalCreditEpisodes,
            terminalCreditCampaign,
            "terminal-credit-test",
            1);
        Assert(terminalCreditEpisodes[0].Frames[0].LongTermReturn > 0d
               && terminalCreditEpisodes[0].Campaign.OutcomeClass
                  == "battle-victory"
               && terminalCreditEpisodes[1].Frames[0].LongTermReturn
                  > terminalCreditEpisodes[2].Frames[0].LongTermReturn
               && terminalCreditEpisodes[2].Frames[^1].LongTermReturn == -1d
               && terminalCreditEpisodes.All(episode =>
                   episode.Campaign.TerminalSnapshotKnown
                   && episode.Campaign.TerminalBattleIndex == 2
                   && episode.Campaign.TerminalPlayerHp == 77
                   && episode.Campaign.TerminalPlayerMaxHp == 143
                   && episode.Campaign.TerminalDoomPower == 19),
            "terminal credit preserves local victories while assigning the strongest negative target to the actual failing encounter");
        Assert(CombatCampaignFoundationTrainer.ShouldRunCounterfactualHardEncounter(
                   new CombatCampaignFoundationTrainingRequest
                   {
                       EnableCounterfactualHardEncounters = true
                   },
                   true,
                   terminalCreditCampaign)
               && !CombatCampaignFoundationTrainer.ShouldRunCounterfactualHardEncounter(
                   new CombatCampaignFoundationTrainingRequest
                   {
                       EnableCounterfactualHardEncounters = false
                   },
                   true,
                   terminalCreditCampaign),
            "hard-encounter counterfactual replay is gated by protocol setting and a real local defeat");
        var counterfactualBaseline = new CombatCampaignResult
        {
            Battles =
            {
                new CombatSimulationResult
                {
                    Outcome = CombatSimulationOutcome.Defeat,
                    Turns = 3,
                    Metrics = new CombatSimulationMetrics
                    {
                        DamageDealt = 40
                    }
                }
            }
        };
        var counterfactualImprovement = new CombatCampaignResult
        {
            Battles =
            {
                new CombatSimulationResult
                {
                    Outcome = CombatSimulationOutcome.Defeat,
                    Turns = 4,
                    Metrics = new CombatSimulationMetrics
                    {
                        DamageDealt = 55
                    }
                }
            }
        };
        var counterfactualVictory = new CombatCampaignResult
        {
            Battles =
            {
                new CombatSimulationResult
                {
                    Outcome = CombatSimulationOutcome.Victory,
                    Turns = 4
                }
            }
        };
        var counterfactualNoGain = new CombatCampaignResult
        {
            Battles =
            {
                new CombatSimulationResult
                {
                    Outcome = CombatSimulationOutcome.Defeat,
                    Turns = 3,
                    Metrics = new CombatSimulationMetrics
                    {
                        DamageDealt = 44
                    }
                }
            }
        };
        Assert(CombatCampaignFoundationTrainer.ClassifyCounterfactual(
                   counterfactualBaseline,
                   counterfactualVictory)
               == CombatFoundationCounterfactualAdmission.Victory
               && CombatCampaignFoundationTrainer.ClassifyCounterfactual(
                   counterfactualBaseline,
                   counterfactualImprovement)
               == CombatFoundationCounterfactualAdmission.Improved
               && CombatCampaignFoundationTrainer.ClassifyCounterfactual(
                   counterfactualBaseline,
                   counterfactualNoGain)
               == CombatFoundationCounterfactualAdmission.Rejected,
            "counterfactual admission retains victories and measurable improvements while rejecting no-gain teacher defeats");
        var advancedCurriculumCheckpoint = new CombatCampaignCheckpoint
        {
            CampaignId = "curriculum",
            CampaignVersion = "1",
            DifficultyId = "advanced",
            NextEncounterIndex = 3,
            State = new CombatCampaignState
            {
                DifficultyId = "advanced",
                MaxHp = 100,
                CurrentHp = 20,
                Deck = { "burningcard_1" }
            }
        };
        var advancedCurriculumCheckpoints =
            CombatCampaignFoundationTrainer.BuildLocalCurriculumCheckpoints(
                advancedCurriculumCheckpoint);
        Assert(advancedCurriculumCheckpoints.Count == 4
               && advancedCurriculumCheckpoints[0].Checkpoint.State.CurrentHp == 20
               && advancedCurriculumCheckpoints[1].Checkpoint.State.CurrentHp == 65
               && advancedCurriculumCheckpoints[2].Checkpoint.State.CurrentHp == 85
               && advancedCurriculumCheckpoints[3].Checkpoint.State.CurrentHp == 100
               && advancedCurriculumCheckpoint.State.CurrentHp == 20,
            "advanced local curriculum keeps the original encounter intact and adds bounded training-only HP recovery variants");
        advancedCurriculumCheckpoint.DifficultyId = "normal";
        Assert(CombatCampaignFoundationTrainer.BuildLocalCurriculumCheckpoints(
                   advancedCurriculumCheckpoint).Count == 1,
            "local solvability repair never changes normal, arena, or formal validation encounters");
        advancedCurriculumCheckpoint.DifficultyId = "advanced";
        advancedCurriculumCheckpoint.NextEncounterIndex = 32;
        advancedCurriculumCheckpoint.State.CurrentLayer = 6;
        advancedCurriculumCheckpoint.State.CurrentHp = 20;
        var lateCurriculum =
            CombatCampaignFoundationTrainer.BuildLocalCurriculumCheckpoints(
                advancedCurriculumCheckpoint);
        advancedCurriculumCheckpoint.NextEncounterIndex = 36;
        advancedCurriculumCheckpoint.State.CurrentLayer = 7;
        var finaleCurriculum =
            CombatCampaignFoundationTrainer.BuildLocalCurriculumCheckpoints(
                advancedCurriculumCheckpoint);
        Assert(lateCurriculum.Count == 4
               && lateCurriculum[1].HpFloorPercent == 75
               && lateCurriculum[1].CurriculumBand == "late"
               && finaleCurriculum.Count == 3
               && finaleCurriculum[1].HpFloorPercent == 85
               && finaleCurriculum[1].CurriculumBand == "finale",
            "Advanced late and finale local curricula use generic campaign-position bands and bounded HP repair only");
        var bootstrapDistillation =
            CombatCampaignFoundationTrainer.EffectiveTransformerDistillationWeight(
                new CombatTransformerTeacherReport
                {
                    Applied = true,
                    TeacherGeneration = 1
                },
                0.35d,
                null,
                Array.Empty<CombatCampaignFoundationIteration>());
        var policyOnlyDistillation =
            CombatCampaignFoundationTrainer.EffectiveTransformerDistillationWeight(
                new CombatTransformerTeacherReport
                {
                    Applied = true,
                    PolicyTeacherApplied = true,
                    WorldTeacherApplied = false,
                    PolicyQualityGatePassed = true,
                    WorldModelQualityGatePassed = false,
                    TeacherGeneration = 2
                },
                0.35d,
                new CombatPolicyValueNetworkDefinition(),
                Array.Empty<CombatCampaignFoundationIteration>());
        var regressedDistillation =
            CombatCampaignFoundationTrainer.EffectiveTransformerDistillationWeight(
                new CombatTransformerTeacherReport
                {
                    Applied = true,
                    TeacherGeneration = 2
                },
                0.35d,
                new CombatPolicyValueNetworkDefinition(),
                new[]
                {
                    new CombatCampaignFoundationIteration
                    {
                        OfflineHeadRegressionGatePassed = false,
                        TransformerTeacher = new CombatTransformerTeacherReport
                        {
                            Applied = true
                        }
                    }
                });
        var bootstrapCorpusDistillation =
            CombatCampaignFoundationTrainer.EffectiveTransformerDistillationWeight(
                new CombatTransformerTeacherReport
                {
                    Applied = true,
                    FrameCount = 1024,
                    TeacherGeneration = 2
                },
                0.35d,
                new CombatPolicyValueNetworkDefinition(),
                Array.Empty<CombatCampaignFoundationIteration>(),
                1024);
        var provisionalCorpusDistillation =
            CombatCampaignFoundationTrainer.EffectiveTransformerDistillationWeight(
                new CombatTransformerTeacherReport
                {
                    Applied = true,
                    FrameCount = 2048,
                    TeacherGeneration = 2
                },
                0.35d,
                new CombatPolicyValueNetworkDefinition(),
                Array.Empty<CombatCampaignFoundationIteration>(),
                1024);
        Assert(Math.Abs(bootstrapDistillation.Weight - 0.35d) < 0.000001d
               && !bootstrapDistillation.Guarded
               && Math.Abs(policyOnlyDistillation.Weight - 0.35d) < 0.000001d
               && !policyOnlyDistillation.Guarded
               && Math.Abs(regressedDistillation.Weight - 0.35d) < 0.000001d
               && !regressedDistillation.Guarded
               && Math.Abs(bootstrapCorpusDistillation.Weight - 0.35d) < 0.000001d
               && !bootstrapCorpusDistillation.Guarded
               && Math.Abs(provisionalCorpusDistillation.Weight - 0.35d) < 0.000001d
               && !provisionalCorpusDistillation.Guarded,
            "student-side distillation uses the trainer-configured fixed weight for every qualified policy teacher generation even when the independent world head is withheld");
        var consecutiveRegressionDistillation =
            CombatCampaignFoundationTrainer.EffectiveTransformerDistillationWeight(
                new CombatTransformerTeacherReport
                {
                    Applied = true,
                    TeacherGeneration = 3
                },
                0.35d,
                new CombatPolicyValueNetworkDefinition(),
                new[]
                {
                    new CombatCampaignFoundationIteration
                    {
                        ModelValidationMetrics = new CombatPolicyValueMetricSnapshot
                            { FrameCount = 100, CompositeLoss = 0.10d },
                        ModelTestMetrics = new CombatPolicyValueMetricSnapshot
                            { FrameCount = 100, CompositeLoss = 0.10d }
                    },
                    new CombatCampaignFoundationIteration
                    {
                        ModelValidationMetrics = new CombatPolicyValueMetricSnapshot
                            { FrameCount = 100, CompositeLoss = 0.12d },
                        ModelTestMetrics = new CombatPolicyValueMetricSnapshot
                            { FrameCount = 100, CompositeLoss = 0.12d }
                    },
                    new CombatCampaignFoundationIteration
                    {
                        ModelValidationMetrics = new CombatPolicyValueMetricSnapshot
                            { FrameCount = 100, CompositeLoss = 0.15d },
                        ModelTestMetrics = new CombatPolicyValueMetricSnapshot
                            { FrameCount = 100, CompositeLoss = 0.15d }
                    }
                });
        Assert(Math.Abs(consecutiveRegressionDistillation.Weight - 0.35d)
               < 0.000001d
               && !consecutiveRegressionDistillation.Guarded,
            "student validation history does not silently override the trainer-configured fixed distillation weight");
        var ineffectiveHardIterations = new List<CombatCampaignFoundationIteration>
        {
            new()
            {
                HardSeedCounterfactualCampaigns = 10
            },
            new()
            {
                HardSeedCounterfactualCampaigns = 10
            }
        };
        var adaptiveHardRequest = new CombatCampaignFoundationTrainingRequest
        {
            HardSeedReplayShare = 0.35d,
            AdvancedAcceptanceRate = 0.30d
        };
        Assert(Math.Abs(
                   CombatCampaignFoundationTrainer.EffectiveHardSeedReplayShare(
                       adaptiveHardRequest,
                       ineffectiveHardIterations)
                   - adaptiveHardRequest.HardSeedReplayShare)
               < 0.000001d,
            "hard-seed replay share remains configured until advanced arena evidence reaches its acceptance target");
        ineffectiveHardIterations[1].ValidAdvancedArenaPairs = 32;
        ineffectiveHardIterations[1].CandidateAdvancedWinRate = 0.30d;
        ineffectiveHardIterations[1].Promoted = true;
        Assert(Math.Abs(
                   CombatCampaignFoundationTrainer.EffectiveHardSeedReplayShare(
                       adaptiveHardRequest,
                       ineffectiveHardIterations)
                   - CombatFoundationStagnationProtocol.ReducedHardSeedReplayShare)
               < 0.000001d,
            "hard-seed replay share may decay only after advanced performance reaches its acceptance target");
        ineffectiveHardIterations[1].HardSeedCounterfactualVictories = 1;
        Assert(Math.Abs(
                   CombatCampaignFoundationTrainer.EffectiveHardSeedReplayShare(
                       adaptiveHardRequest,
                       ineffectiveHardIterations)
                   - adaptiveHardRequest.HardSeedReplayShare)
               < 0.000001d,
            "hard-seed replay share remains configured when the recent solve-rate floor is met");
        var stagnationIterations = new List<CombatCampaignFoundationIteration>
        {
            new() { Promoted = true, WorkingModelAccepted = true },
            new() { Promoted = false },
            new() { Promoted = false },
            new() { Promoted = false }
        };
        Assert(CombatCampaignFoundationTrainer.ShouldStopForStagnation(
                   new CombatCampaignFoundationTrainingRequest
                   {
                       MaximumConsecutiveRejectedIterations = 3
                   },
                   stagnationIterations,
                   hasChampion: true)
               && !CombatCampaignFoundationTrainer.ShouldStopForStagnation(
                   new CombatCampaignFoundationTrainingRequest
                   {
                       MaximumConsecutiveRejectedIterations = 3
                   },
                   stagnationIterations,
                   hasChampion: false),
            "stagnation control stops only after the configured rejected-candidate streak and only when a usable champion exists");
        Assert(!CombatCampaignFoundationTrainer.ShouldStopForStagnation(
                   new CombatCampaignFoundationTrainingRequest
                   {
                       MaximumConsecutiveRejectedIterations = 3
                   },
                   stagnationIterations,
                   hasChampion: true,
                   startIndex: stagnationIterations.Count),
            "a resumed training attempt resets its rejection streak instead of immediately inheriting historical stagnation");
        var sparseArenaIterations = new List<CombatCampaignFoundationIteration>
        {
            new() { ArenaEvaluationRan = true, WorkingModelAccepted = true },
            new() { ArenaEvaluationRan = true },
            new() { TrainingOnlyIteration = true },
            new() { TrainingOnlyIteration = true }
        };
        Assert(!CombatCampaignFoundationTrainer.ShouldStopForStagnation(
                   new CombatCampaignFoundationTrainingRequest
                   {
                       MaximumConsecutiveRejectedIterations = 1
                   },
                   sparseArenaIterations,
                   hasChampion: true),
            "scheduled training-only iterations never consume or trigger the Arena stagnation budget");
        stagnationIterations[2].ProductiveProgress = true;
        stagnationIterations[2].ProductiveProgressReasons = new List<string>
        {
            "strategy-quota-improved"
        };
        Assert(!CombatCampaignFoundationTrainer.ShouldStopForStagnation(
                   new CombatCampaignFoundationTrainingRequest
                   {
                       MaximumConsecutiveRejectedIterations = 3
                   },
                   stagnationIterations,
                   hasChampion: true),
            "a rejected candidate with measurable multi-objective progress resets the unproductive stagnation streak");
        var productiveHistory = new List<CombatCampaignFoundationIteration>
        {
            new()
            {
                ValidNormalArenaPairs = 16,
                ValidAdvancedArenaPairs = 16,
                CandidateNormalWinRate = 0.80d,
                CandidateAdvancedWinRate = 0.10d,
                OfflineHeadRegressionGatePassed = true,
                FeatureCollisionGatePassed = true,
                TeacherStudentPoolQuotaShortfalls = new Dictionary<string, int>
                {
                    ["strategy-growth"] = 100,
                    ["strategy-bank"] = 20
                },
                ModelValidationMetrics = new CombatPolicyValueMetricSnapshot
                {
                    FrameCount = 100,
                    CompositeLoss = 0.30d
                }
            }
        };
        var productiveCandidate = new CombatCampaignFoundationIteration
        {
            ValidNormalArenaPairs = 16,
            ValidAdvancedArenaPairs = 16,
            CandidateNormalWinRate = 0.75d,
            CandidateAdvancedWinRate = 0.25d,
            OfflineHeadRegressionGatePassed = true,
            FeatureCollisionGatePassed = true,
            AbsoluteAdvancedGatePassed = true,
            TeacherStudentPoolQuotaShortfalls = new Dictionary<string, int>
            {
                ["strategy-growth"] = 70,
                ["strategy-bank"] = 10
            },
            ModelValidationMetrics = new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 100,
                CompositeLoss = 0.28d
            }
        };
        productiveCandidate.ParetoProgress =
            CombatCampaignFoundationTrainer.ParetoFrontierProgress(
                productiveCandidate,
                productiveHistory);
        var productiveReasons = CombatCampaignFoundationTrainer
            .ProductiveProgressReasons(productiveCandidate, productiveHistory);
        Assert(productiveCandidate.ParetoProgress
               && productiveReasons.Contains("pareto-frontier")
               && productiveReasons.Contains("strategy-quota-improved")
               && productiveReasons.Contains("validation-loss-improved")
               && productiveReasons.Contains("advanced-absolute-first-pass")
               && CombatCampaignFoundationTrainer.PreferredWorkingModelSlot(
                   "dual-deficit-recovery") == "advanced-best",
            "productive-progress governance recognizes Pareto, quota, validation and first-gate gains without accepting the candidate");
        Assert(!CombatCampaignFoundationTrainer.ParetoFrontierProgress(
                   productiveHistory[0],
                   productiveHistory),
            "an equal candidate does not refresh the Pareto frontier or mask true stagnation");
        var teacherOnlyHistory = new List<CombatCampaignFoundationIteration>
        {
            new()
            {
                TransformerTeacher = new CombatTransformerTeacherReport
                {
                    Applied = true,
                    TeacherGeneration = 1
                }
            }
        };
        var teacherOnlyCandidate = new CombatCampaignFoundationIteration
        {
            TransformerTeacher = new CombatTransformerTeacherReport
            {
                Applied = true,
                TeacherGeneration = 2
            }
        };
        var teacherBehaviorReasons = CombatCampaignFoundationTrainer
            .ProductiveProgressReasons(teacherOnlyCandidate, teacherOnlyHistory);
        var teacherDataReasons = CombatCampaignFoundationTrainer
            .DataPipelineProgressReasons(teacherOnlyCandidate, teacherOnlyHistory);
        Assert(!teacherBehaviorReasons.Contains("teacher-generation-advanced")
               && teacherDataReasons.Contains("teacher-generation-advanced"),
            "teacher generation advances the data pipeline without masquerading as behavioral model progress");
        var dataOnlyIterations = Enumerable.Range(0, 2)
            .Select(_ => new CombatCampaignFoundationIteration
            {
                DataPipelineProgress = true,
                BehavioralProductiveProgress = false,
                ProductiveProgress = false
            })
            .ToList();
        Assert(CombatCampaignFoundationTrainer.ShouldStopForStagnation(
                new CombatCampaignFoundationTrainingRequest
                {
                    MaximumConsecutiveRejectedIterations = 3
                },
                dataOnlyIterations,
                hasChampion: true),
            "data-only progress receives one grace iteration but cannot reset behavioral stagnation indefinitely");
        Assert(!CombatCampaignFoundationTrainer.ShouldStopForStagnation(
                new CombatCampaignFoundationTrainingRequest
                {
                    MaximumConsecutiveRejectedIterations = 0
                },
                dataOnlyIterations,
                hasChampion: true),
            "disabling stagnation control also disables the data-only grace limit");
        Assert(!CombatTransformerTeacherRefreshProtocol.IsFinalRefresh(
                   new CombatTransformerTeacherContext
                   {
                       Iteration = 8,
                       TotalIterations = 8,
                       FinalRefreshRequested = false
                   })
               && CombatTransformerTeacherRefreshProtocol.IsFinalRefresh(
                   new CombatTransformerTeacherContext
                   {
                       Iteration = 8,
                       TotalIterations = 8,
                       FinalRefreshRequested = true
                   }),
            "continuation boundaries do not force a Transformer final refresh unless the run explicitly requests finalization");
        var refreshOptions = new CombatTransformerTeacherOptions
        {
            CpuRefreshInterval = 4,
            AcceleratorRefreshInterval = 3,
            MinimumFreshFramesForRefresh = 2048
        }.Normalized();
        var stableTeacherReuse =
            !CombatTransformerTeacherRefreshProtocol.ShouldRefresh(
                true, false, false, 2, 1, 0, 0, 1200, 1200, false,
                refreshOptions, out var stableTeacherReason);
        var thresholdRefresh = CombatTransformerTeacherRefreshProtocol
            .ShouldRefresh(
                true, false, false, 2, 1, 0, 0, 2200, 2048, false,
                refreshOptions, out var thresholdRefreshReason);
        var intervalRefresh = CombatTransformerTeacherRefreshProtocol
            .ShouldRefresh(
                true, false, false, 4, 1, 0, 0, 64, 64, false,
                refreshOptions, out var intervalRefreshReason);
        var rejectionBackoff = !CombatTransformerTeacherRefreshProtocol
            .ShouldRefresh(
                true, false, false, 5, 1, 4, 1, 4096, 4096, true,
                refreshOptions, out var rejectionBackoffReason);
        Assert(stableTeacherReuse
               && stableTeacherReason == "stable-teacher-reuse"
               && thresholdRefresh
               && thresholdRefreshReason == "fresh-frame-threshold"
               && intervalRefresh
               && intervalRefreshReason == "maximum-staleness"
               && rejectionBackoff
               && rejectionBackoffReason == "rejected-update-backoff:6",
            "accelerator teacher annotation reuses stable weights until fresh-frame or maximum-staleness gates fire and backs off after rejected updates");
        var unhealthyInference = CombatFoundationInferenceHealthProtocol.Evaluate(
            new CombatCampaignFoundationTelemetry
            {
                InferenceExecutionMode =
                    CombatFoundationExecutionProfileNames.ShardedBatchInference,
                InferenceBatchSizePerLane = 2
            },
            new CombatCampaignFoundationTelemetry
            {
                InferenceExecutionMode =
                    CombatFoundationExecutionProfileNames.ShardedBatchInference,
                InferenceBatchSizePerLane = 2,
                InferenceRequests = 100_000,
                InferenceBatchEvaluations = 65_000,
                InferenceBatchedInputs = 80_000,
                InferenceTimeoutFlushes = 40_000,
                InferenceDirectFallbackRequests = 20_000
            });
        Assert(unhealthyInference.RevalidationRequired
               && unhealthyInference.AverageBatchFill < 0.70d
               && unhealthyInference.TimeoutFlushRate > 0.50d
               && unhealthyInference.DirectFallbackRate > 0.15d,
            "live per-iteration inference deltas invalidate a low-fill, timeout-heavy batch plan");
        var mixedDirectInference = CombatFoundationInferenceHealthProtocol.Evaluate(
            new CombatCampaignFoundationTelemetry
            {
                InferenceExecutionMode =
                    CombatFoundationExecutionProfileNames.ShardedBatchInference,
                InferenceBatchSizePerLane = 4
            },
            new CombatCampaignFoundationTelemetry
            {
                InferenceExecutionMode =
                    CombatFoundationExecutionProfileNames.ShardedBatchInference,
                InferenceBatchSizePerLane = 4,
                InferenceRequests = 10_000,
                InferenceBatchEvaluations = 1_000,
                InferenceBatchedInputs = 2_400,
                InferenceTimeoutFlushes = 400
            });
        Assert(mixedDirectInference.RevalidationRequired
               && Math.Abs(mixedDirectInference.AverageBatchFill - 0.60d)
                  < 0.0000001d
               && mixedDirectInference.DirectBypassRate > 0.70d,
            "batch health uses actual batched inputs so direct model bypasses cannot inflate queue fill or masquerade as a healthy selected plan");
        var longArchiveRoot =
            @"D:\Steam\steamapps\common\Witch's Apocalyptic Journey\Witch's Apocalyptic Journey_Data\ModsData\AuraShared\Logs\AuraToolsExp\combat-simulation-results\foundation-success-cases";
        var fullCompatibilityKey = new string('a', 64);
        var fullCaseId = new string('b', 64);
        var compactArchivePath = CombatFoundationCaseArchiveProtocol.EntryPath(
            longArchiveRoot,
            fullCompatibilityKey,
            CombatFoundationCaseArchiveProtocol.ExpertDirectoryName,
            fullCaseId);
        Assert(CombatFoundationCaseArchiveProtocol.Version
                   == "success-case-archive-worker-v4"
               && compactArchivePath.Length < 260
               && compactArchivePath.Contains(
                   Path.DirectorySeparatorChar
                   + "v4"
                   + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal)
               && !compactArchivePath.Contains(
                   fullCompatibilityKey,
                   StringComparison.Ordinal),
            "case archive v4 keeps long install paths bounded while payload ids remain authoritative");
        var workerProtocolJob = new CombatFoundationWorkerJob
        {
            JobId = "worker-protocol-test"
        };
        var workerProtocolProgress = new CombatFoundationWorkerProgress
        {
            JobId = workerProtocolJob.JobId
        };
        var workerProtocolResult = new CombatFoundationWorkerResult
        {
            JobId = workerProtocolJob.JobId
        };
        Assert(workerProtocolJob.SchemaVersion
                   == CombatFoundationWorkerProtocol.SchemaVersion
               && CombatFoundationWorkerProtocol.SchemaVersion == 16
               && CombatFoundationTerminalCreditProtocol.Version
                  == "terminal-credit-v2"
               && CombatFoundationCounterfactualProtocol.Version
                  == "hard-encounter-counterfactual-v2"
               && CombatFoundationStagnationProtocol.Version
                  == "foundation-stagnation-v4-arena-checkpoints-only"
               && CombatPolicyValueFrameStratificationProtocol.Version
                  == "frame-strata-v8-action-aligned-strategy-quota"
               && workerProtocolProgress.SchemaVersion
                   == CombatFoundationWorkerProtocol.SchemaVersion
               && workerProtocolResult.SchemaVersion
                   == CombatFoundationWorkerProtocol.SchemaVersion
               && new CombatFoundationWorkerCheckpoint().SchemaVersion
                   == CombatFoundationWorkerProtocol.SchemaVersion
               && new CombatCampaignFoundationResumeState().SchemaVersion
                   == CombatFoundationWorkerProtocol.SchemaVersion
               && new CombatFoundationCompatibilityManifest().SchemaVersion
                   == CombatFoundationWorkerProtocol.SchemaVersion,
            "foundation worker artifacts share one protocol version constant");
        var checkpointStorageRoot = Path.Combine(
            Path.GetTempPath(),
            "aura-foundation-checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(checkpointStorageRoot);
        try
        {
            var persistedCheckpointVersions = new List<int>();
            using (var checkpointWriteStarted = new ManualResetEventSlim(false))
            using (var releaseCheckpointWrite = new ManualResetEventSlim(false))
            using (var checkpointPipeline =
                   new CombatFoundationLatestWritePipeline<string>(value =>
                   {
                       if (value == "v1")
                       {
                           checkpointWriteStarted.Set();
                           releaseCheckpointWrite.Wait();
                       }
                       lock (persistedCheckpointVersions)
                       {
                           persistedCheckpointVersions.Add(int.Parse(value[1..]));
                       }
                   }))
            {
                checkpointPipeline.Enqueue("v1");
                checkpointWriteStarted.Wait();
                checkpointPipeline.Enqueue("v2");
                checkpointPipeline.Enqueue("v3");
                releaseCheckpointWrite.Set();
                checkpointPipeline.Drain();
                Assert(checkpointPipeline.EnqueuedCount == 3
                       && checkpointPipeline.ExecutedCount == 2
                       && checkpointPipeline.CoalescedCount == 1
                       && persistedCheckpointVersions.SequenceEqual(
                           new[] { 1, 3 }),
                    "foundation checkpoint pipeline overlaps one durable write and coalesces queued states without losing the latest state");
            }
            var checkpointPointerPath = Path.Combine(
                checkpointStorageRoot,
                CombatFoundationWorkerProtocol.CheckpointFileName);
            var checkpointEpisodesBasePath = Path.Combine(
                checkpointStorageRoot,
                CombatFoundationWorkerProtocol.CheckpointEpisodesFileName);
            var snapshotValues = Enumerable.Range(1, 64).ToArray();
            var snapshotSerializerThreads =
                new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
            var firstSnapshot =
                CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
                    checkpointEpisodesBasePath,
                    snapshotValues,
                    value =>
                    {
                        snapshotSerializerThreads.TryAdd(
                            Environment.CurrentManagedThreadId,
                            0);
                        Thread.SpinWait(2000);
                        return "{\"episode\":" + value + "}";
                    },
                    "replay-a",
                    maximumDegreeOfParallelism: 4);
            var loadedSnapshot =
                CombatFoundationCheckpointStorage.ReadAndValidateJsonLines(
                    firstSnapshot,
                    line => line);
            Assert(firstSnapshot.StorageVersion
                       == CombatFoundationCheckpointStorage.SnapshotStorageVersion
                   && firstSnapshot.EpisodeCount == snapshotValues.Length
                   && firstSnapshot.Length > 0
                   && firstSnapshot.ContentSha256.Length == 64
                   && File.Exists(firstSnapshot.Path)
                   && loadedSnapshot.SequenceEqual(snapshotValues.Select(value =>
                       "{\"episode\":" + value + "}"))
                   && snapshotSerializerThreads.Count > 1,
                "foundation checkpoint storage serializes bounded chunks in parallel while publishing immutable ordered snapshots");

            CombatFoundationCheckpointStorage.WriteAtomicText(
                checkpointPointerPath,
                "pointer-v1");
            using (var blockedPointer = new FileStream(
                       checkpointPointerPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                var releasePointer = Task.Run(() =>
                {
                    Thread.Sleep(180);
                    blockedPointer.Dispose();
                });
                CombatFoundationCheckpointStorage.WriteAtomicText(
                    checkpointPointerPath,
                    "pointer-v2");
                releasePointer.Wait();
            }
            Assert(CombatFoundationCheckpointStorage.ReadAllTextShared(
                       checkpointPointerPath)
                       == "pointer-v2"
                   && CombatFoundationCheckpointStorage.ReadAllTextShared(
                       CombatFoundationCheckpointStorage.BackupPath(
                           checkpointPointerPath))
                       == "pointer-v1",
                "foundation checkpoint pointer replacement retries transient Windows delete-sharing locks and retains the previous pointer");

            var streamedArtifactPath = Path.Combine(
                checkpointStorageRoot,
                "streamed-artifact.json");
            CombatFoundationCheckpointStorage.WriteAtomicStream(
                streamedArtifactPath,
                stream =>
                {
                    using var writer = new StreamWriter(
                        stream,
                        new System.Text.UTF8Encoding(false),
                        1024,
                        leaveOpen: true);
                    writer.Write("{\"mode\":\"streamed\",\"ok\":true}");
                    writer.Flush();
                },
                retainBackup: false);
            Assert(CombatFoundationCheckpointStorage.ReadAllTextShared(
                       streamedArtifactPath)
                       == "{\"mode\":\"streamed\",\"ok\":true}",
                "foundation storage atomically publishes streamed artifacts without constructing a full output string");

            var secondSnapshot =
                CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
                    checkpointEpisodesBasePath,
                    snapshotValues.Select(value => value == snapshotValues.Length
                        ? "{\"episode\":999}"
                        : "{\"episode\":" + value + "}"),
                    "replay-b");
            Assert(firstSnapshot.EpisodeCount == secondSnapshot.EpisodeCount
                   && !string.Equals(
                       firstSnapshot.ContentSha256,
                       secondSnapshot.ContentSha256,
                       StringComparison.Ordinal)
                   && !string.Equals(
                       firstSnapshot.ReplayIdentity,
                       secondSnapshot.ReplayIdentity,
                       StringComparison.Ordinal),
                "foundation checkpoint snapshots detect same-count replay replacement instead of relying on episode count alone");

            File.AppendAllText(secondSnapshot.Path, "corrupt");
            var corruptedSnapshotRejected = false;
            try
            {
                CombatFoundationCheckpointStorage.ReadAndValidateJsonLines(
                    secondSnapshot,
                    line => line);
            }
            catch (InvalidDataException)
            {
                corruptedSnapshotRejected = true;
            }
            Assert(corruptedSnapshotRejected,
                "foundation checkpoint resume rejects truncated or modified episode snapshots before deserialization");

            var orphanTemporaryPath =
                checkpointEpisodesBasePath + ".tmp-orphan";
            File.WriteAllText(orphanTemporaryPath, "orphan");
            CombatFoundationCheckpointStorage.CleanupArtifacts(
                checkpointPointerPath,
                checkpointEpisodesBasePath,
                new[] { firstSnapshot.Path },
                retainNewestSnapshots: 1);
            Assert(File.Exists(firstSnapshot.Path)
                   && !File.Exists(orphanTemporaryPath),
                "foundation checkpoint cleanup preserves the referenced snapshot and removes orphan temporary files");
        }
        finally
        {
            if (Directory.Exists(checkpointStorageRoot))
            {
                Directory.Delete(checkpointStorageRoot, recursive: true);
            }
        }
        Assert(CombatFoundationWorkerProtocol.TryValidateJob(
                   workerProtocolJob,
                   out var validJobDiagnostic)
               && string.IsNullOrEmpty(validJobDiagnostic)
               && CombatFoundationWorkerProtocol.TryValidateProgress(
                   workerProtocolProgress,
                   workerProtocolJob.JobId,
                   out var validProgressDiagnostic)
               && string.IsNullOrEmpty(validProgressDiagnostic)
               && CombatFoundationWorkerProtocol.TryValidateResult(
                   workerProtocolResult,
                   workerProtocolJob.JobId,
                   out var validResultDiagnostic)
               && string.IsNullOrEmpty(validResultDiagnostic),
            "foundation worker host accepts matching job, progress and result artifacts");
        workerProtocolProgress.SchemaVersion =
            CombatFoundationWorkerProtocol.SchemaVersion - 1;
        Assert(!CombatFoundationWorkerProtocol.TryValidateProgress(
                   workerProtocolProgress,
                   workerProtocolJob.JobId,
                   out var versionDiagnostic)
               && versionDiagnostic.Contains(
                   "worker=" + (CombatFoundationWorkerProtocol.SchemaVersion - 1),
                   StringComparison.Ordinal)
               && versionDiagnostic.Contains(
                   "host=" + CombatFoundationWorkerProtocol.SchemaVersion,
                   StringComparison.Ordinal),
            "foundation worker host rejects stale progress with an actionable protocol diagnostic");
        workerProtocolProgress.SchemaVersion =
            CombatFoundationWorkerProtocol.SchemaVersion;
        workerProtocolProgress.JobId = "other-worker-job";
        Assert(!CombatFoundationWorkerProtocol.TryValidateProgress(
                   workerProtocolProgress,
                   workerProtocolJob.JobId,
                   out var jobIdDiagnostic)
               && jobIdDiagnostic.Contains("jobId 不匹配", StringComparison.Ordinal),
            "foundation worker host rejects progress from a different job with an actionable diagnostic");
        var capabilityGateRequest = new CombatCampaignFoundationTrainingRequest
        {
            RequireCapabilityProbeBaselineGain = true,
            CapabilityProbeMinimumVictoryGain = 1,
            CapabilityProbeMinimumDepthGain = 0.5d
        };
        var capabilityGateProbe = new CombatFoundationCapabilityProbe
        {
            Arms =
            {
                new CombatFoundationCapabilityProbeArm
                {
                    ArmId = "rule-baseline",
                    NormalCampaigns = 12,
                    NormalVictories = 10,
                    AdvancedCampaigns = 12,
                    AdvancedVictories = 3,
                    AverageCompletedBattles = 20d
                },
                new CombatFoundationCapabilityProbeArm
                {
                    ArmId = "champion-deployment",
                    NormalCampaigns = 12,
                    NormalVictories = 10,
                    AdvancedCampaigns = 12,
                    AdvancedVictories = 3,
                    AverageCompletedBattles = 20.1d
                },
                new CombatFoundationCapabilityProbeArm
                {
                    ArmId = "champion-teacher-hard",
                    NormalCampaigns = 12,
                    NormalVictories = 11,
                    AdvancedCampaigns = 12,
                    AdvancedVictories = 5,
                    AverageCompletedBattles = 22d
                }
            },
            Pairs =
            {
                new CombatFoundationCapabilityProbePair
                {
                    DifficultyId = "normal",
                    WorldSeed = 1,
                    BaselineVictory = true,
                    ChampionVictory = true,
                    BaselineCompletedBattles = 20,
                    ChampionCompletedBattles = 20
                },
                new CombatFoundationCapabilityProbePair
                {
                    DifficultyId = "advanced",
                    WorldSeed = 1,
                    BaselineVictory = false,
                    ChampionVictory = false,
                    BaselineCompletedBattles = 18,
                    ChampionCompletedBattles = 19
                }
            }
        };
        CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
            capabilityGateRequest,
            capabilityGateProbe);
        Assert(!capabilityGateProbe.PassedBaselineGate
               && capabilityGateProbe.BaselineGateVerdict == "inconclusive"
               && capabilityGateProbe.BaselineGateReason.Contains(
                   "deployment=normal 10/12, advanced 3/12",
                   StringComparison.Ordinal)
               && capabilityGateProbe.BaselineGateReason.Contains(
                   "teacher-hard=normal 11/12, advanced 5/12",
                   StringComparison.Ordinal),
            "capability probe keeps tied evidence inconclusive and blocks publication");
        capabilityGateProbe.Arms[1].NormalVictories = 9;
        CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
            capabilityGateRequest,
            capabilityGateProbe);
        Assert(!capabilityGateProbe.PassedBaselineGate
               && capabilityGateProbe.BaselineGateVerdict == "inconclusive"
               && capabilityGateProbe.ChampionVictoryGain == -1,
            "capability probe blocks publication when paired evidence remains statistically inconclusive");
        capabilityGateProbe.Arms[1].NormalVictories = 10;
        capabilityGateProbe.Pairs.Clear();
        for (var pairedIndex = 0; pairedIndex < 24; pairedIndex++)
        {
            capabilityGateProbe.Pairs.Add(
                new CombatFoundationCapabilityProbePair
                {
                    DifficultyId = pairedIndex < 12 ? "normal" : "advanced",
                    WorldSeed = (ulong)pairedIndex,
                    BaselineVictory = pairedIndex >= 20,
                    ChampionVictory = pairedIndex < 22,
                    BaselineCompletedBattles = 18,
                    ChampionCompletedBattles = 20
                });
        }
        capabilityGateProbe.Arms[1].NormalVictories = 12;
        capabilityGateProbe.Arms[1].AdvancedVictories = 10;
        CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
            capabilityGateRequest,
            capabilityGateProbe);
        Assert(capabilityGateProbe.PassedBaselineGate
               && capabilityGateProbe.BaselineGateVerdict == "pass"
               && capabilityGateProbe.ChampionOnlyWins == 20
               && capabilityGateProbe.BaselineOnlyWins == 2
               && capabilityGateProbe.PairedWinWilsonLowerBound > 0.5d,
            "capability probe promotes only a credible paired-seed win advantage");
        foreach (var pair in capabilityGateProbe.Pairs)
        {
            (pair.BaselineVictory, pair.ChampionVictory) =
                (pair.ChampionVictory, pair.BaselineVictory);
        }
        CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
            capabilityGateRequest,
            capabilityGateProbe);
        Assert(!capabilityGateProbe.PassedBaselineGate
               && capabilityGateProbe.BaselineGateVerdict == "fail",
            "capability probe rejects a credible paired-seed regression");
        var depthGainProbe = new CombatFoundationCapabilityProbe
        {
            Arms =
            {
                new CombatFoundationCapabilityProbeArm
                {
                    ArmId = "rule-baseline",
                    NormalCampaigns = 8,
                    AdvancedCampaigns = 8,
                    AverageCompletedBattles = 18d
                },
                new CombatFoundationCapabilityProbeArm
                {
                    ArmId = "champion-deployment",
                    NormalCampaigns = 8,
                    AdvancedCampaigns = 8,
                    AverageCompletedBattles = 19d
                }
            }
        };
        for (var depthPairIndex = 0; depthPairIndex < 8; depthPairIndex++)
        {
            depthGainProbe.Pairs.Add(new CombatFoundationCapabilityProbePair
            {
                DifficultyId = depthPairIndex < 4 ? "normal" : "advanced",
                WorldSeed = (ulong)depthPairIndex,
                BaselineCompletedBattles = 18,
                ChampionCompletedBattles = 19
            });
        }
        CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
            capabilityGateRequest,
            depthGainProbe);
        Assert(depthGainProbe.PassedBaselineGate
               && depthGainProbe.DepthGainEvidencePassed
               && depthGainProbe.PairedLossPairs == 8
               && depthGainProbe.BaselineGateVerdict == "pass",
            "capability probe accepts sufficiently broad paired survival-depth gain without a win-rate regression");
        depthGainProbe.Arms[1].AverageCompletedBattles = 18.1d;
        CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
            capabilityGateRequest,
            depthGainProbe);
        Assert(!depthGainProbe.PassedBaselineGate
               && !depthGainProbe.DepthGainEvidencePassed
               && depthGainProbe.BaselineGateVerdict == "inconclusive",
            "capability depth evidence still requires the configured aggregate gain threshold");
        Assert(CombatCampaignFoundationTrainer.ShouldExpandCapabilityProbe(
                   capabilityGateRequest,
                   depthGainProbe,
                   completedCampaignsPerDifficulty: 32,
                   initialCampaignsPerDifficulty: 32,
                   maximumCampaignsPerDifficulty: 128),
            "an inconclusive initial capability probe expands instead of rejecting immediately");
        depthGainProbe.Arms[1].AverageCompletedBattles = 19d;
        CombatCampaignFoundationTrainer.EvaluateCapabilityBaselineGate(
            capabilityGateRequest,
            depthGainProbe);
        Assert(!CombatCampaignFoundationTrainer.ShouldExpandCapabilityProbe(
                   capabilityGateRequest,
                   depthGainProbe,
                   completedCampaignsPerDifficulty: 32,
                   initialCampaignsPerDifficulty: 32,
                   maximumCampaignsPerDifficulty: 128),
            "conclusive paired depth evidence stops adaptive capability expansion");
        var saturatedBaseline = Enumerable.Range(0, 8)
            .Select(index => index < 4
                ? new CombatCampaignResult
                {
                    DifficultyId = "normal",
                    FinalBossVictory = true
                }
                : null)
            .ToList();
        var saturatedChampion = saturatedBaseline
            .Select(item => item == null
                ? null
                : new CombatCampaignResult
                {
                    DifficultyId = item.DifficultyId,
                    FinalBossVictory = item.FinalBossVictory
                })
            .ToList();
        Assert(CombatCampaignFoundationTrainer
                   .CapabilityDifficultySaturatedAtVictory(
                       saturatedBaseline,
                       saturatedChampion,
                       campaignCapacity: 4,
                       difficultyIndex: 0,
                       completedCampaigns: 4),
            "adaptive capability probing identifies a saturated all-victory normal arm");
        saturatedChampion[0]!.FinalBossVictory = false;
        Assert(!CombatCampaignFoundationTrainer
                   .CapabilityDifficultySaturatedAtVictory(
                       saturatedBaseline,
                       saturatedChampion,
                       campaignCapacity: 4,
                       difficultyIndex: 0,
                       completedCampaigns: 4),
            "normal capability expansion remains active when the paired arm is not saturated");
        Assert(new CombatPolicyValueTrainingOptions
               {
                   GradientShardCount = 24
               }.Normalized().GradientShardCount == 24
               && new CombatPolicyValueTrainingOptions
               {
                   GradientShardCount = 32
               }.Normalized().GradientShardCount == 32,
            "policy-value training preserves 24 and 32 gradient shard presets for high-parallelism hosts");
        var rollingValidationStarted = 0;
        using (var slowValidationGate = new ManualResetEventSlim(false))
        {
            var rollingValidationTask = Task.Run(() =>
                CombatCampaignFoundationTrainer.RunRollingValidation(
                    requestedCampaigns: 8,
                    parallelism: 4,
                    decisionInterval: 8,
                    cancellationToken: CancellationToken.None,
                    run: index =>
                    {
                        Interlocked.Increment(ref rollingValidationStarted);
                        if (index == 0)
                        {
                            slowValidationGate.Wait(TimeSpan.FromSeconds(5));
                        }
                        return new CombatCampaignResult
                        {
                            CampaignId = index.ToString(),
                            FinalBossVictory = true
                        };
                    },
                    shouldStop: (_, _, _) => false,
                    complete: (_, campaign) => campaign));
            var refilledPastSlowHead = SpinWait.SpinUntil(
                () => Volatile.Read(ref rollingValidationStarted) == 8,
                TimeSpan.FromSeconds(2));
            slowValidationGate.Set();
            var rollingValidationRuns = rollingValidationTask.GetAwaiter().GetResult();
            Assert(refilledPastSlowHead
                   && rollingValidationRuns.Select(item => item.CampaignId)
                       .SequenceEqual(Enumerable.Range(0, 8).Select(item =>
                           item.ToString())),
                "rolling validation refills completed worker slots past a slow head while committing results in deterministic order");
        }
        var speculativeSchedulerRun = CombatFoundationWorkScheduler.RunOrdered(
            count: 12,
            parallelism: 4,
            decisionInterval: 4,
            cancellationToken: CancellationToken.None,
            run: index =>
            {
                if (index == 0)
                {
                    Thread.Sleep(75);
                }
                return index;
            },
            commit: (_, value) => value,
            shouldStop: committed => committed >= 4,
            maximumLookAhead: 8);
        Assert(speculativeSchedulerRun.StoppedEarly
               && speculativeSchedulerRun.Items.SequenceEqual(
                   Enumerable.Range(0, 4))
               && speculativeSchedulerRun.Metrics.PeakRunningWork == 4
               && speculativeSchedulerRun.Metrics.RefillCount > 0
               && speculativeSchedulerRun.Metrics.TailIdleCoreSeconds >= 0d,
            "load-balanced scheduler continuously refills workers, commits a deterministic prefix, and exposes tail diagnostics at decision boundaries");
        Assert(CombatCampaignFoundationTrainer.EstimateTuningCampaigns(
                   3,
                   32,
                   64,
                   progressive: true,
                   screeningNormalCampaigns: 8,
                   screeningAdvancedCampaigns: 16,
                   finalistCount: 2) == 216
               && CombatCampaignFoundationTrainer.EstimateTuningCampaigns(
                   3,
                   32,
                   64,
                   progressive: false,
                   screeningNormalCampaigns: 8,
                   screeningAdvancedCampaigns: 16,
                   finalistCount: 2) == 288,
            "progressive tuning screens all checkpoints on paired seeds and reserves the full arena for finalists");
        var foundationRequest = new CombatCampaignFoundationTrainingRequest
        {
            DecisionProfile = "balanced",
            Iterations = 1,
            TrainingCampaignsPerIteration = 2,
            ArenaCampaignsPerDifficulty = 1,
            PreflightCampaignsPerDifficulty = 1,
            PreflightSeedStart = 19_000,
            NormalValidationCampaigns = 5,
            AdvancedValidationCampaigns = 5,
            CapabilityProbeCampaignsPerDifficulty = 1,
            RequireCapabilityProbeBaselineGain = false,
            MaximumDegreeOfParallelism = 4,
            TuningNormalCampaigns = 2,
            TuningAdvancedCampaigns = 2,
            TuningScreeningNormalCampaigns = 1,
            TuningScreeningAdvancedCampaigns = 1,
            TuningFinalistCount = 1,
            CaseArchiveLoad = new CombatFoundationCaseArchiveLoadDiagnostics
            {
                ArchiveExists = true,
                LoadedCases = 3,
                LoadedObservations = 9,
                Message = "fixture"
            },
            CaseArchiveCompatibilityKey = "frozen-archive-key",
            TrainingSeedStart = 10_000,
            ArenaSeedStart = 20_000,
            ValidationSeedStart = 30_000,
            TrainingCampaign = projectedTrainingCampaign,
            ValidationCampaign = projectedValidationCampaign,
            Profile = new CombatDecisionProfile
            {
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 128,
                SearchNodeBudget = 512,
                SearchMaxPly = 4
            },
            Training = new CombatPolicyValueTrainingOptions
            {
                Epochs = 3,
                RetainedModelCandidates = 3,
                MinimumEpisodes = 2,
                HiddenDimensions = 8,
                RandomSeed = 73
            }
        };
        foundationRequest.Resume = new CombatCampaignFoundationResumeState
        {
            SchemaVersion = 2,
            Stage = "model-training",
            NextIteration = 1,
            CompletedCampaigns = 999,
            Champion = policyValueTraining.Model,
            WorkingChampion = policyValueTraining.Model,
            Replay =
            {
                new CombatEpisode
                {
                    FeatureSchemaVersion = 6,
                    ModelProtocol = CombatPolicyValueProtocol.EpisodeProtocol
                }
            }
        };
        var incrementallyObservedFoundationCases = 0;
        var incrementallyArchivedFoundationCases = 0;
        var incrementallyRecordedModelMetrics =
            new List<CombatPolicyValueEpochMetrics>();
        CombatCampaignFoundationTelemetry? latestFoundationTelemetry = null;
        var foundationTelemetrySnapshots =
            new List<CombatCampaignFoundationTelemetry>();
        foundationRequest.ObservationRecorded = _ =>
            incrementallyObservedFoundationCases++;
        foundationRequest.SuccessCaseRecorded = _ =>
            incrementallyArchivedFoundationCases++;
        foundationRequest.ModelMetricRecorded = metrics =>
            incrementallyRecordedModelMetrics.Add(metrics);
        foundationRequest.Telemetry = telemetry =>
        {
            lock (foundationTelemetrySnapshots)
            {
                latestFoundationTelemetry = telemetry;
                foundationTelemetrySnapshots.Add(telemetry);
            }
        };
        var foundationDecisionAllocationStart =
            CombatDecisionAllocationDiagnostics.Capture();
        CombatDecisionAllocationDiagnostics.DetailedEnabled = true;
        var foundationTraining = new CombatCampaignFoundationTrainer().Run(
            foundationRequest,
            campaignRules.Ruleset);
        var foundationDecisionAllocation = CombatDecisionAllocationDiagnostics
            .Capture()
            .DeltaFrom(foundationDecisionAllocationStart);
        CombatDecisionAllocationDiagnostics.DetailedEnabled = false;
        var foundationDepthBucketCampaigns =
            foundationTraining.Depth1To5Campaigns
            + foundationTraining.Depth6To10Campaigns
            + foundationTraining.Depth11To20Campaigns
            + foundationTraining.Depth21To30Campaigns
            + foundationTraining.Depth31To37Campaigns;
        Assert(foundationTraining.Success
               && foundationTraining.AcceptancePassed
               && foundationTraining.Champion != null
               && foundationTraining.WorkingChampion != null
               && foundationTraining.LatestTrainingModel != null
               && foundationTraining.AbsoluteQualifiedBestModel != null
               && foundationTraining.AbsoluteQualifiedBestEvidence
                  ?.AbsoluteQualificationGatePassed == true
               && foundationTraining.AbsoluteQualifiedBestModel.ModelId
                  == foundationTraining.Champion.ModelId
               && foundationTraining.AbsoluteQualifiedBestEvidence.CandidateModelId
                  == foundationTraining.Champion.ModelId
               && foundationTraining.AcceptanceKind
                  == CombatFoundationPromotionProtocol.AbsoluteQualifiedBest
               && foundationTraining.QualifiedCandidateCount == 1
               && foundationTraining.SelectedQualifiedCandidateIteration == 1
               && foundationTraining.Iterations.Single()
                   .QualifiedCandidateSelected
               && foundationTraining.Preflight.Passed
               && foundationTraining.Preflight.CompletedCampaigns
                  == 2 + CombatFoundationIntegritySeedCorpus.KnownFailures.Count
               && foundationTraining.Preflight.RegressionSeedCampaigns
                  == CombatFoundationIntegritySeedCorpus.KnownFailures.Count
               && foundationTraining.Preflight.SemanticGatePassed
               && foundationTraining.Preflight.InvalidCampaigns == 0
               && foundationTraining.Replay.Count is > 0 and <= 16
               && foundationTraining.Replay.All(episode => episode.Authoritative)
               && foundationTraining.ValidationRuns.Count == 10
               && foundationTraining.CompletedCampaigns < 999
               && foundationTraining.CaseArchiveLoad.LoadedCases == 3
               && foundationTraining.CaseArchiveLoad.LoadedObservations == 9
               && foundationTraining.EffectiveParallelism == 4
               && foundationTraining.InferenceLaneCount == 1
               && foundationTraining.InferenceBatchSizePerLane == 4
               && foundationTraining.PeakConcurrentCampaigns >= 1
               && foundationTraining.ObservedWorkerThreads >= 1
               && foundationTraining.CompletedBattles > 0
               && foundationTraining.MaximumCompletedBattleDepth == 37
               && foundationDepthBucketCampaigns == foundationTraining.CompletedCampaigns
               && foundationTraining.ProjectedBattleDepth == 37d
               && foundationTraining.PolicyDecisions > 0
               && foundationTraining.SearchSimulations > 0
               && foundationTraining.SearchNodes > 0
               && foundationTraining.AllocatedBytes > 0
               && foundationTraining.CpuSeconds > 0d
               && foundationTraining.PhaseElapsedSeconds.Count > 0
               && foundationTraining.PhaseElapsedSeconds.ContainsKey("self-play")
               && foundationTraining.PhaseElapsedSeconds.ContainsKey("model-training")
               && foundationTraining.PhaseCpuSeconds.ContainsKey("self-play")
               && foundationTraining.PhaseCpuSeconds.Values.Sum() > 0d
               && foundationTraining.PhaseAllocatedBytes.ContainsKey("self-play")
               && foundationTraining.PhaseAllocatedBytes.Values.Sum() > 0L
               && foundationTraining.PhasePeakConcurrentWork.TryGetValue(
                   "self-play",
                   out var selfPlayPeakWork)
               && selfPlayPeakWork >= 1
               && foundationTraining.PhaseObservedWorkerThreads.TryGetValue(
                   "self-play",
                   out var selfPlayObservedThreads)
               && selfPlayObservedThreads >= 1
               && foundationTraining.ModelTrainingLoss > 0d
               && foundationTraining.ModelValidationLoss > 0d
               && foundationTraining.ModelEpochHistory.Count > 0
               && foundationTraining.Iterations.All(item =>
                    item.ModelEpochHistory.Count > 0
                    && item.ModelTrainingMetrics.FrameCount > 0
                    && item.ModelValidationMetrics.FrameCount > 0
                    && item.TuningCandidateCount >= 2
                    && item.TuningFinalistCount == 1
                    && (item.TuningEvaluationRan
                        ? item.TuningCampaignsExecuted > 0
                          && item.TuningCampaignsSaved > 0
                        : item.TuningOfflineRejectedCandidates > 0
                          && item.TuningCampaignsExecuted == 0))
               && incrementallyRecordedModelMetrics.Count > 0
               && incrementallyRecordedModelMetrics.All(item =>
                   item.Iteration > 0
                   && item.Epoch > 0)
               && foundationTraining.CapabilityProbe.Arms.Count > 0
               && incrementallyObservedFoundationCases
                  == foundationTraining.CampaignObservations.Count
               && foundationTraining.CampaignObservations.All(item =>
                   item.CompatibilityKey == "frozen-archive-key")
               && incrementallyArchivedFoundationCases
                  == foundationTraining.SuccessCases.Count
               && incrementallyArchivedFoundationCases > 0
               && latestFoundationTelemetry != null
               && latestFoundationTelemetry.RunStartIteration == 1
               && latestFoundationTelemetry.RunIteration == 1
               && latestFoundationTelemetry.RunTotalIterations == 1
               && latestFoundationTelemetry.RunInitialCompletedCampaigns == 0
               && latestFoundationTelemetry.RunCompletedCampaigns
                  == latestFoundationTelemetry.CompletedCampaigns
               && latestFoundationTelemetry.RunRequestedCampaigns
                  == latestFoundationTelemetry.RequestedCampaigns
               && latestFoundationTelemetry.ExecutedCampaigns
                  >= latestFoundationTelemetry.CompletedCampaigns
               && latestFoundationTelemetry.RunInitialExecutedCampaigns == 0
               && latestFoundationTelemetry.RunExecutedCampaigns
                  == latestFoundationTelemetry.ExecutedCampaigns
               && latestFoundationTelemetry.RunExecutedCampaigns
                  > latestFoundationTelemetry.RunCompletedCampaigns
               && latestFoundationTelemetry.RunCompletedBattles
                  == latestFoundationTelemetry.CompletedBattles
               && latestFoundationTelemetry.RunSearchSimulations
                  == latestFoundationTelemetry.SearchSimulations
               && foundationTelemetrySnapshots.Any(item =>
                   string.Equals(
                       item.Phase,
                       "preflight",
                       StringComparison.Ordinal)
                   && item.CurrentPhaseRequestedCampaigns > 0
                   && item.CurrentPhaseCompletedCampaigns > 0
                   && item.CurrentPhaseCompletedBattles > 0
                   && item.CurrentPhaseCampaignsPerSecond > 0d
                   && item.CompletedCampaigns == 0)
               && foundationTraining.ElapsedSeconds > 0d,
            "foundation trainer qualifies an absolute-line bootstrap candidate, validates it, and reports telemetry while streaming successful cases"
            + $" (success={foundationTraining.Success}, acceptance={foundationTraining.AcceptancePassed},"
            + $" preflight={foundationTraining.Preflight.Passed}/{foundationTraining.Preflight.CompletedCampaigns}/"
            + $"{foundationTraining.Preflight.InvalidCampaigns}, replay={foundationTraining.Replay.Count},"
            + $" validationRuns={foundationTraining.ValidationRuns.Count}, completed={foundationTraining.CompletedCampaigns},"
            + $" depthBuckets={foundationDepthBucketCampaigns}, probeArms={foundationTraining.CapabilityProbe.Arms.Count},"
            + $" observations={incrementallyObservedFoundationCases}/{foundationTraining.CampaignObservations.Count},"
            + $" cases={incrementallyArchivedFoundationCases}/{foundationTraining.SuccessCases.Count},"
            + $" elapsed={foundationTraining.ElapsedSeconds:F6},"
            + $" epochs={foundationTraining.ModelEpochHistory.Count}/{incrementallyRecordedModelMetrics.Count},"
            + $" iter={string.Join(";", foundationTraining.Iterations.Select(item => $"{item.ModelEpochHistory.Count}:{item.ModelTrainingMetrics.FrameCount}:{item.ModelValidationMetrics.FrameCount}:{item.TuningCandidateCount}:{item.TuningOfflineRejectedCandidates}:{item.TuningEvaluationRan}:{item.TuningFinalistCount}:{item.TuningCampaignsExecuted}:{item.TuningCampaignsSaved}"))},"
            + $" phases={string.Join(",", foundationTraining.PhaseElapsedSeconds.Keys)},"
            + $" message={foundationTraining.Message})");
        var packageJob = new CombatFoundationWorkerJob
        {
            JobId = "foundation-package-test",
            Request = foundationRequest,
            Ruleset = new CombatRulesetDocument
            {
                Version = campaignRules.Ruleset.Version,
                Cards = campaignRules.Ruleset.SnapshotCards().ToList(),
                Enemies = campaignRules.Ruleset.SnapshotEnemies().ToList(),
                Statuses = campaignRules.Ruleset.SnapshotStatuses().ToList()
            }
        };
        var packageOriginalRoleId =
            packageJob.Request.TrainingCampaign.Player.RoleId;
        var packageOriginalPartnerId =
            packageJob.Request.TrainingCampaign.Player.PartnerId;
        var packageOriginalPresetId =
            packageJob.Request.TrainingCampaign.Player.GameParameterPresetId;
        var packageOriginalParameterHash =
            packageJob.Request.TrainingCampaign.Player.GameParameterHash;
        var packageOriginalPacks = new List<string>(
            packageJob.Request.TrainingCampaign.EnabledRewardCardPackIds);
        var packageOriginalDeckMinimum =
            packageJob.Request.TrainingCampaign.TargetDeckSizeMinimum;
        var packageOriginalDeckMaximum =
            packageJob.Request.TrainingCampaign.TargetDeckSizeMaximum;
        packageJob.Request.TrainingCampaign.Player.RoleId = "career_1";
        packageJob.Request.TrainingCampaign.Player.PartnerId = "Partner_10001";
        packageJob.Request.TrainingCampaign.Player.GameParameterPresetId = "standard";
        packageJob.Request.TrainingCampaign.Player.GameParameterHash =
            "foundation-package-game-parameters";
        packageJob.Request.TrainingCampaign.EnabledRewardCardPackIds =
            new List<string> { "cardpack_1", "cardpack_2", "cardpack_3" };
        packageJob.Request.TrainingCampaign.TargetDeckSizeMinimum = 1;
        packageJob.Request.TrainingCampaign.TargetDeckSizeMaximum = 24;
        // Keep the package fixture explicit so this section stays focused on the
        // export contract rather than the trainer's final-state wiring.
        foundationTraining.Success = true;
        foundationTraining.AcceptancePassed = true;
        foundationTraining.Champion = foundationTraining.WorkingChampion;
        foundationTraining.Validation.Passed = true;
        foundationTraining.Validation.BehaviorPassed = true;
        var packageResult = new CombatFoundationWorkerResult
        {
            JobId = packageJob.JobId,
            Success = true,
            CompletionKind = "training-accepted",
            RulesetHash = foundationTraining.Compatibility.RulesetHash,
            Training = foundationTraining
        };
        var foundationPackage = CombatFoundationModelPackageProtocol.Create(
            packageJob,
            packageResult,
            "ABCDEF");
        Assert(CombatFoundationModelPackageProtocol.TryValidate(
                   foundationPackage,
                   out var foundationPackageDiagnostic)
               && string.IsNullOrEmpty(foundationPackageDiagnostic)
               && foundationPackage.Model != null
               && foundationPackage.Model.ModelId
                  == foundationTraining.Champion!.ModelId
               && foundationPackage.PartnerId == "Partner_10001"
               && foundationPackage.EnabledRewardCardPackIds.Contains("cardpack_3")
               && foundationPackage.TrainingSubject?.RoleId == "career_1"
               && foundationPackage.TrainingSubject?.PartnerId == "Partner_10001"
               && foundationPackage.TrainingSubject.EnabledRewardCardPackIds
                   .Contains("cardpack_3")
               && foundationPackage.DeclaredCoverage?.EntityCoverageKnown == true
               && foundationPackage.SchemaVersion
                  == CombatFoundationModelPackageProtocol.SchemaVersion
               && foundationPackage.Acceptance?.FormalIsolationPassed == true
               && foundationPackage.Acceptance.Classification
                  == CombatFoundationPromotionProtocol.AbsoluteQualifiedBest
               && foundationPackage.Acceptance.AbsoluteQualified
               && foundationPackage.Validation.Passed,
            "accepted worker results create a validated v5 foundation package draft");
        var artifactModel = foundationPackage.Model!;
        var artifactDirectory = Path.Combine(
            Path.GetTempPath(),
            "aura-fp32-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);
        try
        {
            var artifactPath = Path.Combine(
                artifactDirectory,
                CombatFoundationModelPackageProtocol.WeightsFileName);
            var artifact = CombatPolicyValueArtifactProtocol.Write(
                artifactPath,
                artifactModel);
            foundationPackage.ModelArtifact = artifact;
            foundationPackage.Model = null;
            Assert(CombatFoundationModelPackageProtocol.TryValidate(
                       foundationPackage,
                       out var compactPackageDiagnostic)
                   && string.IsNullOrEmpty(compactPackageDiagnostic)
                   && CombatPolicyValueArtifactProtocol.TryValidatePayload(
                       artifactDirectory,
                       artifact,
                       out var payloadDiagnostic)
                   && string.IsNullOrEmpty(payloadDiagnostic),
                "v5 foundation packages publish a validated FP32 payload");
            Assert(CombatPolicyValueArtifactProtocol.TryLoad(
                       artifactDirectory,
                       artifact,
                       out var runtimeDefinition,
                       out var runtimeDiagnostic)
                   && string.IsNullOrEmpty(runtimeDiagnostic)
                   && runtimeDefinition.ModelId == artifactModel.ModelId
                   && runtimeDefinition.StateWeightsByInput.Length
                      == artifactModel.StateWeights.Length
                   && new ManagedCombatPolicyValueModel(runtimeDefinition).ModelId
                      == artifactModel.ModelId,
                "v5 foundation packages use validated little-endian FP32 binary weights");
            var maximumFp32Error = 0d;
            for (var output = 0; output < artifactModel.HiddenDimensions; output++)
            {
                for (var input = 0; input < artifactModel.StateDimensions; input++)
                {
                    maximumFp32Error = Math.Max(
                        maximumFp32Error,
                        Math.Abs(
                            artifactModel.StateWeights[
                                output * artifactModel.StateDimensions + input]
                            - runtimeDefinition.StateWeightsByInput[
                                input * artifactModel.HiddenDimensions + output]));
                }
            }
            Assert(maximumFp32Error < 0.000001d,
                "FP32 publication keeps state-weight quantization error below 1e-6");
        }
        finally
        {
            foundationPackage.Model = artifactModel;
            foundationPackage.ModelArtifact = null;
            Directory.Delete(artifactDirectory, recursive: true);
        }
        var packageSchema = foundationPackage.SchemaVersion;
        var packageVersion = foundationPackage.ModelVersion;
        var packageAcceptance = foundationPackage.Acceptance;
        foundationPackage.SchemaVersion =
            CombatFoundationModelPackageProtocol.PreviousSchemaVersion;
        foundationPackage.ModelVersion =
            CombatFoundationModelPackageProtocol.PreviousModelVersion;
        Assert(CombatFoundationModelPackageProtocol.TryValidate(
                   foundationPackage,
                   out var previousV4PackageDiagnostic)
               && string.IsNullOrEmpty(previousV4PackageDiagnostic),
            "v5 readers retain compatibility with accepted v4 model packages");
        foundationPackage.SchemaVersion =
            CombatFoundationModelPackageProtocol.LegacySchemaVersion;
        foundationPackage.ModelVersion =
            CombatFoundationModelPackageProtocol.LegacyModelVersion;
        foundationPackage.Acceptance = null;
        Assert(CombatFoundationModelPackageProtocol.TryValidate(
                   foundationPackage,
                   out var legacyV3PackageDiagnostic)
               && string.IsNullOrEmpty(legacyV3PackageDiagnostic)
               && CombatFoundationModelPackageProtocol.NormalizeAcceptance(
                      foundationPackage).Classification
                  == "legacy-formal-acceptance",
            "v5 readers retain compatibility with formally accepted v3 model packages");
        foundationPackage.SchemaVersion = packageSchema;
        foundationPackage.ModelVersion = packageVersion;
        foundationPackage.Acceptance = packageAcceptance;
        foundationPackage.Acceptance!.Classification =
            CombatFoundationPromotionProtocol.EquivalentNonInferior;
        foundationPackage.Acceptance.EquivalentNonInferior = true;
        foundationPackage.Acceptance.ValidNormalPairs = 8;
        foundationPackage.Acceptance.ValidAdvancedPairs = 8;
        Assert(!CombatFoundationModelPackageProtocol.TryValidate(
                   foundationPackage,
                   out var weakNonInferiorityPackageDiagnostic)
               && weakNonInferiorityPackageDiagnostic.Contains(
                   "验收证明",
                   StringComparison.Ordinal),
            "v4 model packages reject non-inferiority claims without the required paired evidence");
        foundationPackage.Acceptance.Classification = "retained-champion";
        foundationPackage.Acceptance.EquivalentNonInferior = false;
        foundationPackage.Acceptance.ValidNormalPairs = 0;
        foundationPackage.Acceptance.ValidAdvancedPairs = 0;
        var avoidableEndTurnGateRejected = false;
        foundationTraining.Validation.AvoidableEndTurnsWithUnusedEnergy = 1;
        try
        {
            CombatFoundationModelPackageProtocol.Create(
                packageJob,
                packageResult,
                "ABCDEF");
        }
        catch (InvalidOperationException)
        {
            avoidableEndTurnGateRejected = true;
        }
        finally
        {
            foundationTraining.Validation.AvoidableEndTurnsWithUnusedEnergy = 0;
        }
        Assert(avoidableEndTurnGateRejected,
            "foundation export rejects validation with avoidable unused-energy end turns");
        var endTurnCounterfactualGateRejected = false;
        foundationTraining.Validation.DominatedEndTurns = 1;
        foundationTraining.Validation.EndTurnsIntoAvoidableLethal = 1;
        foundationTraining.Validation.EndTurnsWithCertifiedCycle = 1;
        try
        {
            CombatFoundationModelPackageProtocol.Create(
                packageJob,
                packageResult,
                "ABCDEF");
        }
        catch (InvalidOperationException)
        {
            endTurnCounterfactualGateRejected = true;
        }
        finally
        {
            foundationTraining.Validation.DominatedEndTurns = 0;
            foundationTraining.Validation.EndTurnsIntoAvoidableLethal = 0;
            foundationTraining.Validation.EndTurnsWithCertifiedCycle = 0;
        }
        Assert(endTurnCounterfactualGateRejected,
            "foundation export rejects dominated, avoidable-lethal, or certified-cycle end turns");
        foundationPackage.Validation.EndTurnsWithCertifiedCycle = 1;
        Assert(!CombatFoundationModelPackageProtocol.TryValidate(
                foundationPackage,
                out var certifiedCycleGateDiagnostic)
               && certifiedCycleGateDiagnostic.Contains(
                   "验证",
                   StringComparison.Ordinal),
            "foundation import rejects validation that abandoned a certified cycle");
        foundationPackage.Validation.EndTurnsWithCertifiedCycle = 0;
        var noEffectActionGateRejected = false;
        foundationTraining.Validation.NoEffectActionAttempts = 1;
        try
        {
            CombatFoundationModelPackageProtocol.Create(
                packageJob,
                packageResult,
                "ABCDEF");
        }
        catch (InvalidOperationException)
        {
            noEffectActionGateRejected = true;
        }
        finally
        {
            foundationTraining.Validation.NoEffectActionAttempts = 0;
        }
        Assert(noEffectActionGateRejected,
            "foundation export rejects validation containing no-effect action attempts");
        var currentActionContractVersion =
            foundationPackage.Compatibility.ActionContractVersion;
        foundationPackage.Compatibility.ActionContractVersion =
            "action-contract-legacy";
        Assert(!CombatFoundationModelPackageProtocol.TryValidate(
                foundationPackage,
                out var actionContractCompatibilityDiagnostic)
               && actionContractCompatibilityDiagnostic.Contains(
                   "兼容",
                   StringComparison.Ordinal),
            "foundation import rejects models trained under an incompatible action contract");
        foundationPackage.Compatibility.ActionContractVersion =
            currentActionContractVersion;
        var supersetCoverage = CombatFoundationModelCoverageProtocol.Assess(
            foundationPackage.TrainingSubject!,
            foundationPackage.DeclaredCoverage!,
            new CombatModelRuntimeContext
            {
                RoleId = "career_1",
                PartnerId = "Partner_10001",
                EnabledRewardCardPackIds =
                    new List<string> { "cardpack_1", "cardpack_2" },
                PreferredDeckSizeMinimum = 1,
                PreferredDeckSizeMaximum = 20
            });
        Assert(supersetCoverage.Level == "full"
               && supersetCoverage.RuntimeExtraCardPackIds.Count == 0
               && supersetCoverage.TrainingOnlyCardPackIds.SequenceEqual(
                   new[] { "cardpack_3" }),
            "a model trained with more card packs fully covers a runtime with fewer packs");
        var partialCoverage = CombatFoundationModelCoverageProtocol.Assess(
            foundationPackage.TrainingSubject!,
            foundationPackage.DeclaredCoverage!,
            new CombatModelRuntimeContext
            {
                RoleId = "career_other",
                PartnerId = "Partner_10001",
                EnabledRewardCardPackIds =
                    new List<string>
                    {
                        "cardpack_1",
                        "cardpack_2",
                        "cardpack_4"
                    },
                PreferredDeckSizeMinimum = 1,
                PreferredDeckSizeMaximum = 24
            });
        Assert(partialCoverage.Level == "partial"
               && partialCoverage.RoleSkillFallbackRequired
               && partialCoverage.RuntimeExtraCardPackIds.SequenceEqual(
                   new[] { "cardpack_4" }),
            "role changes and runtime-only card packs are assessed as partial coverage instead of incompatibility");
        var recordingCoverageModel = new RecordingPolicyValueModel();
        var coverageAwareModel = new CoverageAwareCombatPolicyValueModel(
            recordingCoverageModel,
            new CombatFoundationTrainingSubject
            {
                RoleId = "career_trained",
                PartnerId = "partner_trained",
                EnabledRewardCardPackIds =
                    new List<string> { "cardpack_1", "cardpack_2" },
                PreferredDeckSizeMinimum = 1,
                PreferredDeckSizeMaximum = 24
            },
            new CombatFoundationDeclaredCoverage
            {
                EntityCoverageKnown = true,
                CardIds = new List<string> { "known-card" },
                StatusIds = new List<string> { "known-status" }
            },
            new CombatModelRuntimeContext
            {
                RoleId = "career_other",
                PartnerId = "partner_trained"
            });
        var coveragePrediction = coverageAwareModel.Evaluate(
            new CombatPolicyValueInput
            {
                StateFeatures = new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["playerHp"] = 20d,
                    ["playerStatus:known-status"] = 1d,
                    ["playerStatus:unknown-status"] = 2d
                },
                Candidates =
                {
                    new CombatPolicyValueCandidate
                    {
                        CandidateId = "known",
                        SourceId = "known-card",
                        ActionKind = CombatActionKind.PlayCard.ToString()
                    },
                    new CombatPolicyValueCandidate
                    {
                        CandidateId = "unknown",
                        SourceId = "unknown-card",
                        ActionKind = CombatActionKind.PlayCard.ToString()
                    },
                    new CombatPolicyValueCandidate
                    {
                        CandidateId = "role-skill",
                        SourceId = "skill-other",
                        ActionKind = CombatActionKind.UseSkill.ToString()
                    }
                }
            });
        Assert(recordingCoverageModel.LastInput != null
               && recordingCoverageModel.LastInput.StateFeatures.ContainsKey(
                   "playerStatus:known-status")
               && !recordingCoverageModel.LastInput.StateFeatures.ContainsKey(
                   "playerStatus:unknown-status")
               && coveragePrediction.PolicyLogits["known"] == 2d
               && coveragePrediction.PolicyLogits["unknown"] == 0d
               && coveragePrediction.PolicyLogits["role-skill"] == 0d,
            "coverage-aware inference keeps learned card decisions while unknown cards and foreign role skills fall back");
        var packageTrainingSubject = foundationPackage.TrainingSubject;
        var packageDeclaredCoverage = foundationPackage.DeclaredCoverage;
        foundationPackage.TrainingSubject = null;
        foundationPackage.DeclaredCoverage = null;
        Assert(CombatFoundationModelPackageProtocol.TryValidate(
                foundationPackage,
                out var legacyPackageDiagnostic)
               && string.IsNullOrEmpty(legacyPackageDiagnostic),
            "legacy v2 foundation packages without coverage extensions remain importable");
        foundationPackage.TrainingSubject = packageTrainingSubject;
        foundationPackage.DeclaredCoverage = packageDeclaredCoverage;
        foundationPackage.TrainingSubject!.RoleId = "tampered-role";
        Assert(!CombatFoundationModelPackageProtocol.TryValidate(
                foundationPackage,
                out var inconsistentSubjectDiagnostic)
               && inconsistentSubjectDiagnostic.Contains(
                   "训练主体元数据",
                   StringComparison.Ordinal),
            "extended foundation packages reject internally inconsistent training subject metadata");
        foundationPackage.TrainingSubject.RoleId = foundationPackage.RoleId;
        foundationPackage.CompletionKind = "training-rejected";
        Assert(!CombatFoundationModelPackageProtocol.TryValidate(
                   foundationPackage,
                   out var rejectedFoundationPackageDiagnostic)
               && rejectedFoundationPackageDiagnostic.Contains(
                   "已验收",
                   StringComparison.Ordinal),
            "external foundation package validation rejects non-accepted training results");
        foundationPackage.CompletionKind = "training-accepted";
        packageJob.Request.TrainingCampaign.Player.RoleId = packageOriginalRoleId;
        packageJob.Request.TrainingCampaign.Player.PartnerId = packageOriginalPartnerId;
        packageJob.Request.TrainingCampaign.Player.GameParameterPresetId =
            packageOriginalPresetId;
        packageJob.Request.TrainingCampaign.Player.GameParameterHash =
            packageOriginalParameterHash;
        packageJob.Request.TrainingCampaign.EnabledRewardCardPackIds =
            packageOriginalPacks;
        packageJob.Request.TrainingCampaign.TargetDeckSizeMinimum =
            packageOriginalDeckMinimum;
        packageJob.Request.TrainingCampaign.TargetDeckSizeMaximum =
            packageOriginalDeckMaximum;
        var sharedParameters = new CombatFoundationTrainingParameters
        {
            Iterations = 0,
            TrainingCampaignsPerIteration = 1,
            MaximumDegreeOfParallelism = int.MaxValue,
            ModelTrainingParallelism = int.MaxValue,
            ModelEpochs = 1
        }.Normalized();
        Assert(sharedParameters.Iterations == 1
               && sharedParameters.AdditionalIterationsOnResume == 3
               && sharedParameters.TrainingCampaignsPerIteration == 2
               && sharedParameters.ModelEpochs == 5
               && sharedParameters.EnablePrioritizedReplay
               && sharedParameters.EnableEndTurnSpecialization
               && sharedParameters.ModelEndTurnFrameWeight == 1d
               && sharedParameters.ModelMaximumUnsafeEndTurnFrameShare == 0.20d
               && sharedParameters.ModelUnsafeEndTurnRiskAuxiliaryShare == 0.10d
               && sharedParameters.MinimumArenaDiscordantPairs == 8
               && sharedParameters.MaximumOfflineHeadRegression == 0.05d
               && sharedParameters.MaximumStateFeatureCollisionRate == 0.05d
               && sharedParameters.MaximumActionFeatureCollisionRate == 0.06d
               && sharedParameters.ModelStateDimensions == 2048
               && sharedParameters.ModelActionDimensions == 1024
               && sharedParameters.ModelHiddenDimensions == 512
               && sharedParameters.TransformerTeacherStateDimensions == 2048
               && sharedParameters.TransformerTeacherActionDimensions == 1024
               && sharedParameters.TransformerTeacherMinimumFrames == 1024
               && sharedParameters.TransformerTeacherMaximumFrames == 10000
               && sharedParameters.TransformerTeacherCpuEpochs == 4
               && sharedParameters.TransformerTeacherCpuIncrementalEpochs == 1
               && sharedParameters.TransformerTeacherCpuFinalEpochs == 4
               && sharedParameters.ModelMinimumValidationRunGroups == 16
               && sharedParameters.ModelMinimumTestRunGroups == 16
               && sharedParameters.ModelPolicyTargetTemperature == 1.25d
               && sharedParameters.ModelMaximumPolicyTargetProbability == 0.90d
               && sharedParameters.ModelGradientShardCount == 12
               && sharedParameters.AutoTuneObjective
                  == CombatFoundationAutoTuneObjectiveNames.MaximumThroughput
               && sharedParameters.ValidationEarlyStopBatchSize == 32
               && sharedParameters.InferenceParallelism == 0
               && sharedParameters.InferenceLaneCount == 0
               && sharedParameters.InferenceBatchSize == 0
               && sharedParameters.ThreadPoolMinimumWorkerThreads == 0
               && sharedParameters.CheckpointSerializationParallelism == 0
               && sharedParameters.MaximumDegreeOfParallelism
                  <= Math.Max(1, Environment.ProcessorCount)
               && sharedParameters.ModelTrainingParallelism == 64
               && sharedParameters.EstimatedCampaigns() > 0,
            "shared foundation job parameters normalize identically for game and control-center adapters");
        var cpu16Execution = CombatFoundationExecutionProfiles.Resolve(
            CombatFoundationExecutionProfileNames.Cpu16,
            1,
            CombatFoundationExecutionProfileNames.DirectInference,
            0,
            0,
            0,
            availableProcessorCount: 32);
        var cpu32Execution = CombatFoundationExecutionProfiles.Resolve(
            CombatFoundationExecutionProfileNames.Cpu32,
            1,
            CombatFoundationExecutionProfileNames.DirectInference,
            0,
            0,
            0,
            availableProcessorCount: 32);
        Assert(cpu16Execution.CampaignParallelism == 16
               && cpu16Execution.InferenceParallelism == 16
               && cpu16Execution.InferenceBatchSize == 1
               && cpu16Execution.ThreadPoolMinimumWorkerThreads == 24
               && cpu16Execution.CheckpointSerializationParallelism == 1
               && cpu32Execution.CampaignParallelism == 32
               && cpu32Execution.InferenceParallelism == 32
               && cpu32Execution.InferenceBatchSize == 1
               && cpu32Execution.ThreadPoolMinimumWorkerThreads == 40
               && cpu32Execution.CheckpointSerializationParallelism == 2,
            "CPU-16 and CPU-32 profiles expose direct per-campaign inference and bounded background work");
        var fixedExecution = CombatFoundationExecutionProfiles.Resolve(
            CombatFoundationExecutionProfileNames.Custom,
            48,
            CombatFoundationExecutionProfileNames.DirectInference,
            0,
            0,
            0,
            availableProcessorCount: 8);
        Assert(fixedExecution.CampaignParallelism == 48
               && fixedExecution.InferenceParallelism == 48
               && fixedExecution.ThreadPoolMinimumWorkerThreads == 56
               && fixedExecution.CheckpointSerializationParallelism == 2,
            "custom execution honors the explicit CPU parallelism without hardware auto-tuning or processor-count clamping");
        var boundedAutoExecution = CombatFoundationExecutionProfiles.Resolve(
            CombatFoundationExecutionProfileNames.Auto,
            20,
            CombatFoundationExecutionProfileNames.DirectInference,
            0,
            0,
            0,
            availableProcessorCount: 32);
        Assert(boundedAutoExecution.CampaignParallelism == 20
               && boundedAutoExecution.InferenceParallelism == 20,
            "auto execution treats the requested CPU parallelism as a calibration ceiling");
        var autoTuneSelection = CombatFoundationAutoTuneSelector.Select(
            new[]
            {
                new CombatFoundationAutoTuneMeasurement
                {
                    Parallelism = 16,
                    EfficiencyScore = 980d
                },
                new CombatFoundationAutoTuneMeasurement
                {
                    Parallelism = 32,
                    EfficiencyScore = 1000d
                }
            },
            0.02d);
        Assert(autoTuneSelection == 16
               && CombatFoundationAutoTuneSelector.Score(
                   1000d,
                   gen2CollectionsPerSecond: 0d,
                   allocationMegabytesPerSecond: 1024d) >
                  CombatFoundationAutoTuneSelector.Score(
                      1000d,
                      gen2CollectionsPerSecond: 8d,
                      allocationMegabytesPerSecond: 8192d),
            "auto-tune selects the lowest near-maximum throughput profile and penalizes GC/allocation pressure");
        var maximumThroughputSelection = CombatFoundationAutoTuneSelector.Select(
            new[]
            {
                new CombatFoundationAutoTuneMeasurement
                {
                    Parallelism = 12,
                    UsefulWorkPerSecond = 1000d,
                    EfficiencyScore = 1000d
                },
                new CombatFoundationAutoTuneMeasurement
                {
                    Parallelism = 20,
                    UsefulWorkPerSecond = 1010d,
                    EfficiencyScore = 1010d
                }
            },
            0.02d,
            CombatFoundationAutoTuneObjectiveNames.MaximumThroughput);
        Assert(maximumThroughputSelection == 20,
            "maximum-throughput auto-tune chooses the fastest wall-clock candidate even inside the efficiency tolerance");
        var underutilizedHighParallelismSelection =
            CombatFoundationAutoTuneSelector.Select(
                new[]
                {
                    new CombatFoundationAutoTuneMeasurement
                    {
                        Parallelism = 8,
                        UsefulWorkPerSecond = 1000d,
                        EfficiencyScore = 1000d,
                        CpuUtilizationPercent = 25d
                    },
                    new CombatFoundationAutoTuneMeasurement
                    {
                        Parallelism = 20,
                        UsefulWorkPerSecond = 960d,
                        EfficiencyScore = 960d,
                        CpuUtilizationPercent = 25d
                    }
                },
                0.02d,
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput);
        var materialHighParallelismRegressionSelection =
            CombatFoundationAutoTuneSelector.Select(
                new[]
                {
                    new CombatFoundationAutoTuneMeasurement
                    {
                        Parallelism = 8,
                        UsefulWorkPerSecond = 1000d,
                        EfficiencyScore = 1000d,
                        CpuUtilizationPercent = 25d
                    },
                    new CombatFoundationAutoTuneMeasurement
                    {
                        Parallelism = 20,
                        UsefulWorkPerSecond = 930d,
                        EfficiencyScore = 930d,
                        CpuUtilizationPercent = 25d
                    }
                },
                0.02d,
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput);
        var confidentCampaignCalibration =
            CombatFoundationAutoTuneSelector.HasCampaignConfidence(
                new[]
                {
                    new CombatFoundationAutoTuneMeasurement
                    {
                        MeasurementKind = "campaign-steady-state",
                        Parallelism = 20,
                        TrialCount = 2,
                        Campaigns = 40,
                        UsefulWorkPerSecond = 1000d
                    }
                },
                20);
        Assert(underutilizedHighParallelismSelection == 20
               && materialHighParallelismRegressionSelection == 8
               && confidentCampaignCalibration,
            "maximum-throughput calibration explores high parallelism under low CPU, but rejects a material steady-state regression and requires two full waves");
        var inferenceSelection = CombatFoundationAutoTuneSelector.SelectInference(
            new[]
            {
                new CombatFoundationAutoTuneMeasurement
                {
                    MeasurementKind = "inference",
                    InferenceMode = CombatFoundationExecutionProfileNames.DirectInference,
                    InferenceLaneCount = 16,
                    InferenceBatchSize = 1,
                    EfficiencyScore = 990d,
                    P95LatencyMicroseconds = 20d
                },
                new CombatFoundationAutoTuneMeasurement
                {
                    MeasurementKind = "inference",
                    InferenceMode = CombatFoundationExecutionProfileNames.ShardedBatchInference,
                    InferenceLaneCount = 4,
                    InferenceBatchSize = 4,
                    EfficiencyScore = 1000d,
                    P95LatencyMicroseconds = 40d
                }
            },
            0.02d);
        Assert(inferenceSelection?.InferenceMode
               == CombatFoundationExecutionProfileNames.DirectInference,
            "inference auto-tune prefers lower latency when throughput is within tolerance");
        Assert(CombatFoundationExecutionProfiles.EffectiveLaneCount(12) == 1
               && CombatFoundationExecutionProfiles.EffectiveLaneCount(20) == 2
               && CombatFoundationExecutionProfiles.EffectiveBatchSize(12) == 4
               && CombatFoundationExecutionProfiles.EffectiveBatchSize(20) == 4,
            "automatic inference plans keep enough campaign callers on each batch queue");
        Assert(CombatCampaignFoundationTrainer.BuildAutoTuneParallelismCandidates(20)
                .SequenceEqual(new[] { 10, 15, 20 })
               && CombatCampaignFoundationTrainer.BuildAutoTuneParallelismCandidates(64)
                   .SequenceEqual(new[] { 32, 48, 64 })
               && CombatCampaignFoundationTrainer.CalibratedParallelismCeiling(
                   20,
                   15) == 15
               && CombatCampaignFoundationTrainer.CalibratedParallelismCeiling(
                   20,
                   0) == 20,
            "auto-tune derives normalized scaling points and keeps its measured ceiling independent from transient memory clamping");
        var directInferenceMeasurement = new CombatFoundationAutoTuneMeasurement
        {
            MeasurementKind = "inference-end-to-end",
            InferenceMode = CombatFoundationExecutionProfileNames.DirectInference,
            UsefulWorkPerSecond = 1000d,
            EfficiencyScore = 1000d,
            AverageBatchFill = 1d,
            InferenceRequests = 100,
            InferenceBatchEvaluations = 100
        };
        var weakBatchMeasurement = new CombatFoundationAutoTuneMeasurement
        {
            MeasurementKind = "inference-end-to-end",
            InferenceMode = CombatFoundationExecutionProfileNames.ShardedBatchInference,
            UsefulWorkPerSecond = 900d,
            EfficiencyScore = 900d,
            AverageBatchFill = 0.70d,
            InferenceRequests = 100,
            InferenceBatchEvaluations = 100,
            InferenceTimeoutFlushes = 5
        };
        var promisingBatchMeasurement = new CombatFoundationAutoTuneMeasurement
        {
            MeasurementKind = "inference-end-to-end",
            InferenceMode = CombatFoundationExecutionProfileNames.ShardedBatchInference,
            UsefulWorkPerSecond = 970d,
            EfficiencyScore = 970d,
            AverageBatchFill = 0.55d,
            InferenceRequests = 100,
            InferenceBatchEvaluations = 100,
            InferenceTimeoutFlushes = 5
        };
        var unhealthyBatchMeasurement = new CombatFoundationAutoTuneMeasurement
        {
            MeasurementKind = "inference-end-to-end",
            InferenceMode = CombatFoundationExecutionProfileNames.ShardedBatchInference,
            InferenceLaneCount = 1,
            InferenceBatchSize = 8,
            UsefulWorkPerSecond = 1200d,
            EfficiencyScore = 1200d,
            AverageBatchFill = 0.80d,
            InferenceRequests = 128,
            InferenceBatchEvaluations = 8,
            InferenceTimeoutFlushes = 8
        };
        Assert(CombatFoundationAutoTuneSelector.SelectInference(
                   new[] { directInferenceMeasurement, unhealthyBatchMeasurement },
                   0.02d,
                   CombatFoundationAutoTuneObjectiveNames.MaximumThroughput)
               == directInferenceMeasurement,
            "inference auto-tune measures timeout flushes per batch and rejects a plan whose batches all timed out even when its request count and raw throughput are high");
        Assert(!CombatCampaignFoundationTrainer.ShouldExpandInferenceCandidate(
                   weakBatchMeasurement,
                   directInferenceMeasurement,
                   CombatFoundationAutoTuneObjectiveNames.MaximumThroughput)
               && !CombatCampaignFoundationTrainer.ShouldExpandInferenceCandidate(
                   unhealthyBatchMeasurement,
                   directInferenceMeasurement,
                   CombatFoundationAutoTuneObjectiveNames.MaximumThroughput)
               && CombatCampaignFoundationTrainer.ShouldExpandInferenceCandidate(
                   promisingBatchMeasurement,
                   directInferenceMeasurement,
                   CombatFoundationAutoTuneObjectiveNames.MaximumThroughput),
            "inference calibration prunes weak batch families before testing larger queues");
        var signedInferenceRequest = new CombatCampaignFoundationTrainingRequest
        {
            AutoTuneHardwareKey = "cpu-signature",
            AutoTuneCampaignKey = "campaign-signature-a",
            AutoTuneSampleCampaigns = 32
        };
        var signedInferenceDefinition = new CombatPolicyValueNetworkDefinition
        {
            StateDimensions = 1024,
            ActionDimensions = 1024,
            HiddenDimensions = 512,
            ActionQuantileCount = 16,
            FeatureEncodingMode = "partitioned-v4"
        };
        var inferenceSignature20 =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                20);
        var inferenceSignature23 =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                23);
        var inferenceSignature16 =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                16);
        signedInferenceRequest.AutoTuneCampaignKey = "campaign-signature-b";
        var differentCampaignSignature =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                20);
        signedInferenceRequest.AutoTuneCampaignKey = "campaign-signature-a";
        signedInferenceRequest.AutoTuneSampleCampaigns = 16;
        var differentSampleBudgetSignature =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                20);
        signedInferenceRequest.AutoTuneSampleCampaigns = 32;
        signedInferenceDefinition.ActionQuantileHeadReady = true;
        var quantileReadySignature =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                20);
        signedInferenceDefinition.ActionQuantileHeadReady = false;
        signedInferenceRequest.AutoTuneObjective =
            CombatFoundationAutoTuneObjectiveNames.BalancedEfficiency;
        var balancedObjectiveSignature =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                20);
        signedInferenceRequest.AutoTuneObjective =
            CombatFoundationAutoTuneObjectiveNames.MaximumThroughput;
        signedInferenceRequest.AutoTuneThroughputTolerance = 0.05d;
        var differentToleranceSignature =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                20);
        signedInferenceRequest.AutoTuneThroughputTolerance = 0.02d;
        signedInferenceDefinition.HiddenDimensions = 256;
        var differentShapeSignature =
            CombatCampaignFoundationTrainer.BuildInferenceAutoTuneCacheKey(
                signedInferenceRequest,
                signedInferenceDefinition,
                20);
        Assert(inferenceSignature20 != inferenceSignature23
               && inferenceSignature20 != inferenceSignature16
               && inferenceSignature20 != differentCampaignSignature
               && inferenceSignature20 != differentSampleBudgetSignature
               && inferenceSignature20 != quantileReadySignature
               && inferenceSignature20 != balancedObjectiveSignature
               && inferenceSignature20 != differentToleranceSignature
               && inferenceSignature20 != differentShapeSignature
               && CombatCampaignFoundationTrainer.InferenceConcurrencyClass(20)
                  == 32
                   && CombatCampaignFoundationTrainer
                      .InferenceMicrobenchmarkSampleCount(32, 20) == 256
               && CombatCampaignFoundationTrainer
                   .InferenceCalibrationWaveParallelism(20)
                   .Distinct().OrderBy(value => value)
                   .SequenceEqual(new[] { 1, 5, 10, 20 }),
            "inference calibration cache signs hardware, calibration corpus and sample budget, exact arrival parallelism, model workload including quantile-head readiness, selection objective, and tolerance while bounding work and replaying mixed arrival concurrency");
        var healthFallback = new CombatFoundationAutoTuneResult
        {
            InferenceCacheKey = inferenceSignature20,
            InferenceCalibrated = true,
            InferenceCalibrationKind = CombatFoundationAutoTuneProtocol
                .InferenceCalibrationKind,
            SelectedInferenceMode = CombatFoundationExecutionProfileNames
                .ShardedBatchInference,
            SelectedInferenceLaneCount = 2,
            SelectedInferenceBatchSize = 4
        };
        var healthFailureTime = new DateTime(
            2026,
            8,
            8,
            0,
            0,
            0,
            DateTimeKind.Utc);
        CombatCampaignFoundationTrainer.RecordInferenceHealthFailure(
            healthFallback,
            new CombatFoundationInferenceHealth
            {
                RevalidationRequired = true,
                Reason = "low-batch-fill"
            },
            20,
            healthFailureTime);
        Assert(healthFallback.InferenceFallbackActive
               && !healthFallback.InferenceCalibrated
               && healthFallback.SelectedInferenceMode
                  == CombatFoundationExecutionProfileNames.DirectInference
               && healthFallback.InferenceHealthFailureCount == 1
               && healthFallback.InferenceRecalibrationNotBeforeUtc
                  == healthFailureTime.AddMinutes(
                      CombatFoundationAutoTuneProtocol
                          .InferenceHealthCooldownMinutes)
               && !CombatCampaignFoundationTrainer.ShouldCalibrateInference(
                   healthFallback,
                   inferenceSignature20,
                   20,
                   healthFailureTime.AddMinutes(1))
               && CombatCampaignFoundationTrainer.ShouldCalibrateInference(
                   healthFallback,
                   inferenceSignature20,
                   20,
                   healthFallback.InferenceRecalibrationNotBeforeUtc),
            "unhealthy inference enters a persisted direct fallback cooldown instead of recalibrating in the next formal iteration");
        var restoredInferenceState = new CombatFoundationAutoTuneResult
        {
            CampaignCacheKey = "different-campaign-budget"
        };
        Assert(CombatCampaignFoundationTrainer.TryRestoreInferenceAutoTuneState(
                   healthFallback,
                   restoredInferenceState,
                   inferenceSignature20,
                   20,
                   healthFailureTime.AddMinutes(1))
               && restoredInferenceState.InferenceFallbackActive
               && restoredInferenceState.InferenceHealthFailureCount == 1,
            "inference cooldown is restored by its own signature even when the campaign auto-tune cache key changed");
        var expiredRestoreTime = healthFallback
            .InferenceRecalibrationNotBeforeUtc.AddMinutes(1);
        var restoredExpiredHealthState = new CombatFoundationAutoTuneResult
        {
            CampaignCacheKey = "another-campaign-budget"
        };
        Assert(CombatCampaignFoundationTrainer.TryRestoreInferenceAutoTuneState(
                   healthFallback,
                   restoredExpiredHealthState,
                   inferenceSignature20,
                   20,
                   expiredRestoreTime)
               && restoredExpiredHealthState.InferenceFallbackActive
               && !restoredExpiredHealthState.InferenceCalibrated
               && restoredExpiredHealthState.InferenceHealthFailureCount == 1
               && CombatCampaignFoundationTrainer.ShouldCalibrateInference(
                   restoredExpiredHealthState,
                   inferenceSignature20,
                   20,
                   expiredRestoreTime),
            "an expired cooldown still restores matching failure metadata while requiring a fresh calibration");
        var agingInferenceState = new CombatFoundationAutoTuneResult
        {
            Version = CombatFoundationAutoTuneProtocol.Version,
            MeasuredUtc = healthFailureTime,
            InferenceMeasuredUtc = healthFailureTime.AddDays(-29d),
            InferenceCacheKey = inferenceSignature20,
            InferenceCalibrated = true,
            InferenceCalibrationKind = CombatFoundationAutoTuneProtocol
                .InferenceCalibrationKind,
            SelectedInferenceMode = CombatFoundationExecutionProfileNames
                .ShardedBatchInference,
            SelectedInferenceLaneCount = 2,
            SelectedInferenceBatchSize = 4
        };
        var refreshedCampaignState = new CombatFoundationAutoTuneResult
        {
            MeasuredUtc = healthFailureTime
        };
        Assert(CombatCampaignFoundationTrainer.TryRestoreInferenceAutoTuneState(
                   agingInferenceState,
                   refreshedCampaignState,
                   inferenceSignature20,
                   20,
                   healthFailureTime)
               && refreshedCampaignState.InferenceMeasuredUtc
                  == agingInferenceState.InferenceMeasuredUtc
               && CombatCampaignFoundationTrainer.ShouldCalibrateInference(
                   refreshedCampaignState,
                   inferenceSignature20,
                   20,
                   healthFailureTime.AddDays(2d)),
            "campaign remeasurement preserves the independent inference timestamp instead of renewing a stale plan without benchmarking it");
        var legacyTimestampState = new CombatFoundationAutoTuneResult
        {
            Version = CombatFoundationAutoTuneProtocol.Version,
            MeasuredUtc = healthFailureTime.AddDays(-1d),
            InferenceMeasuredUtc = default,
            InferenceCacheKey = inferenceSignature20,
            InferenceCalibrated = true,
            InferenceCalibrationKind = CombatFoundationAutoTuneProtocol
                .InferenceCalibrationKind,
            SelectedInferenceMode = CombatFoundationExecutionProfileNames
                .ShardedBatchInference,
            SelectedInferenceLaneCount = 2,
            SelectedInferenceBatchSize = 4
        };
        var migratedTimestampState = new CombatFoundationAutoTuneResult();
        Assert(CombatCampaignFoundationTrainer.TryRestoreInferenceAutoTuneState(
                   legacyTimestampState,
                   migratedTimestampState,
                   inferenceSignature20,
                   20,
                   healthFailureTime)
               && migratedTimestampState.InferenceCalibrated
               && migratedTimestampState.InferenceMeasuredUtc
                  == legacyTimestampState.MeasuredUtc,
            "legacy inference caches migrate MeasuredUtc into the independent inference timestamp without renewing it");
        restoredExpiredHealthState.InferenceCalibrated = true;
        restoredExpiredHealthState.InferenceFallbackActive = false;
        CombatCampaignFoundationTrainer.RecordInferenceHealthFailure(
            restoredExpiredHealthState,
            new CombatFoundationInferenceHealth
            {
                RevalidationRequired = true,
                Reason = "high-timeout-flush-rate"
            },
            20,
            expiredRestoreTime);
        Assert(restoredExpiredHealthState.InferenceHealthFailureCount == 2
               && restoredExpiredHealthState.InferenceRecalibrationNotBeforeUtc
                  == expiredRestoreTime.AddMinutes(
                      CombatFoundationAutoTuneProtocol
                          .InferenceHealthCooldownMinutes * 2),
            "a post-expiry failure continues the restored exponential cooldown history");
        restoredExpiredHealthState.InferenceCalibrated = true;
        restoredExpiredHealthState.InferenceFallbackActive = false;
        Assert(CombatCampaignFoundationTrainer.RecordInferenceHealthSuccess(
                   restoredExpiredHealthState,
                   new CombatFoundationInferenceHealth
                   {
                       Requests = CombatFoundationInferenceHealthProtocol
                           .MinimumRequests
                   })
               && restoredExpiredHealthState.InferenceHealthFailureCount == 0
               && !restoredExpiredHealthState.InferenceFallbackActive,
            "only a complete healthy production window on the recalibrated plan clears the failure history");
        var migratedControllerSettings = new ControllerSettings
        {
            SchemaVersion = ControllerSettings.PreviousSchemaVersion,
            LastRunDirectory = "preserve-this-run",
            Parameters = new CombatFoundationTrainingParameters
            {
                ReuseAutoTuneCache = false,
                Iterations = 37,
                TrainingCampaignsPerIteration = 73,
                TransformerTeacherDatasetShardFrames = 512,
                ModelStateDimensions = 1024,
                TransformerTeacherStateDimensions = 1024,
                MaximumStateFeatureCollisionRate = 0.20d
            }
        };
        var currentControllerSettings = new ControllerSettings
        {
            SchemaVersion = ControllerSettings.CurrentSchemaVersion,
            Parameters = new CombatFoundationTrainingParameters
            {
                ReuseAutoTuneCache = false,
                Iterations = 19
            }
        };
        Assert(migratedControllerSettings.MigrateFromPreviousSchema()
               && migratedControllerSettings.SchemaVersion
                  == ControllerSettings.CurrentSchemaVersion
               && !migratedControllerSettings.Parameters.ReuseAutoTuneCache
               && migratedControllerSettings.Parameters.Iterations == 37
               && migratedControllerSettings.Parameters
                      .TrainingCampaignsPerIteration == 73
               && migratedControllerSettings.Parameters
                      .IterationsPerIsolatedProcess == 3
               && migratedControllerSettings.Parameters
                      .ModelStateDimensions == 2048
               && migratedControllerSettings.Parameters
                      .TransformerTeacherStateDimensions == 2048
               && migratedControllerSettings.Parameters
                      .MaximumStateFeatureCollisionRate == 0.05d
               && migratedControllerSettings.LastRunDirectory
                  == "preserve-this-run"
               && !currentControllerSettings.MigrateFromPreviousSchema()
               && !currentControllerSettings.Parameters.ReuseAutoTuneCache
               && currentControllerSettings.Parameters.Iterations == 19,
            "controller v21 migration preserves unrelated settings and upgrades the shipped state encoding contract");
        var shippedPresetMigration = new ControllerSettings
        {
            SchemaVersion = ControllerSettings.PreviousSchemaVersion,
            Parameters = new CombatFoundationTrainingParameters
            {
                GovernanceProfile =
                    CombatFoundationGovernanceProfileNames.Development,
                Iterations = 12,
                TrainingCampaignsPerIteration = 96,
                ArenaCampaignsPerDifficulty = 8,
                ArenaConfirmationCampaignsPerDifficulty = 48,
                ArenaEvaluationInterval = 6,
                ArenaConfirmationFinalIterationOnly = true
            }
        };
        Assert(shippedPresetMigration.MigrateFromPreviousSchema()
               && shippedPresetMigration.Parameters
                      .ArenaConfirmationCampaignsPerDifficulty == 56,
            "controller v21 preserves the shipped 64-pair Arena evidence migration");
        var stableAutoTuneCampaign = new CombatCampaignDefinition
        {
            CampaignId = "cache-campaign",
            CampaignVersion = "1"
        };
        var stableAutoTuneRequest = new CombatCampaignFoundationTrainingRequest
        {
            TrainingCampaign = stableAutoTuneCampaign,
            AutoTuneHardwareKey = "hardware",
            AutoTuneCampaignKey = "structural-campaign",
            DecisionProfile = "balanced",
            AutoTuneObjective =
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput,
            InferenceExecutionMode =
                CombatFoundationExecutionProfileNames.ShardedBatchInference,
            Profile = new CombatDecisionProfile()
        };
        var stableAutoTuneKey =
            CombatCampaignFoundationTrainer.BuildAutoTuneCacheKey(
                stableAutoTuneRequest,
                CombatRuleset.Empty);
        stableAutoTuneCampaign.RewardScoreResiduals["learned"] = 0.125d;
        Assert(stableAutoTuneKey
               == CombatCampaignFoundationTrainer.BuildAutoTuneCacheKey(
                   stableAutoTuneRequest,
                   CombatRuleset.Empty),
            "auto-tune cache identity excludes evolving learned campaign residuals when the Worker supplies a structural key");
        var residualIdentityCampaign = new CombatCampaignDefinition
        {
            CampaignId = "residual-identity",
            CampaignVersion = "1"
        };
        var residualIdentityBefore =
            CombatCampaignFoundationTrainer.CampaignFingerprint(
                residualIdentityCampaign);
        residualIdentityCampaign.RewardScoreResiduals["card:test"] = 0.15d;
        residualIdentityCampaign.RewardScoreConditionalResiduals[
            "advanced|build:test"] = -0.11d;
        residualIdentityCampaign.RewardScoreResidualMaximumAbsolute = 0.45d;
        Assert(
            residualIdentityBefore
            == CombatCampaignFoundationTrainer.CampaignFingerprint(
                residualIdentityCampaign),
            "learned global and conditional reward residuals do not change structural campaign compatibility identity");
        var developmentGovernance = CombatFoundationGovernanceProfiles.Resolve(
            CombatFoundationGovernanceProfileNames.Development,
            tuningInterval: 1,
            tuningNormalCampaigns: 32,
            tuningAdvancedCampaigns: 64,
            tuningScreeningNormalCampaigns: 8,
            tuningScreeningAdvancedCampaigns: 16,
            tuningFinalistCount: 2,
            capabilityProbeTeacherCampaignsPerDifficulty: 128,
            autoTuneSampleCampaigns: 32);
        Assert(developmentGovernance.TuningInterval == 2
               && developmentGovernance.TuningNormalCampaigns == 16
               && developmentGovernance.TuningAdvancedCampaigns == 32
               && developmentGovernance.TuningScreeningNormalCampaigns == 4
               && developmentGovernance.TuningScreeningAdvancedCampaigns == 8
               && developmentGovernance.TuningFinalistCount == 1
               && developmentGovernance.CapabilityProbeTeacherCampaignsPerDifficulty
                  == 16
               && developmentGovernance.AutoTuneSampleCampaigns == 16
               && developmentGovernance.ScheduledTuningIterations(8) == 1
               && developmentGovernance.ScheduledArenaIterations(8) == 2
               && developmentGovernance.RunsArenaAtIteration(5, 8)
               && developmentGovernance.RunsArenaAtIteration(7, 8)
               && !developmentGovernance.RunsArenaAtIteration(4, 8)
               && !developmentGovernance.RunsFormalConfirmationAtIteration(5, 8)
               && developmentGovernance.RunsFormalConfirmationAtIteration(7, 8),
            "development governance uses sparse Arena checkpoints, final-only confirmation and final-only tuning");
        var efficientCampaignEstimate = new CombatFoundationTrainingParameters
        {
            GovernanceProfile = CombatFoundationGovernanceProfileNames.Development,
            Iterations = 8,
            TrainingCampaignsPerIteration = 64,
            ArenaCampaignsPerDifficulty = 32,
            ArenaConfirmationCampaignsPerDifficulty = 64,
            NormalValidationCampaigns = 200,
            AdvancedValidationCampaigns = 500,
            CapabilityProbeCampaignsPerDifficulty = 128,
            CapabilityProbeTeacherCampaignsPerDifficulty = 128,
            ModelRetainedCandidates = 3,
            EnableTuningArena = true,
            EnableProgressiveTuning = true,
            TuningNormalCampaigns = 32,
            TuningAdvancedCampaigns = 64,
            TuningScreeningNormalCampaigns = 8,
            TuningScreeningAdvancedCampaigns = 16,
            TuningFinalistCount = 2
        }.EstimatedCampaigns();
        Assert(efficientCampaignEstimate == 2340,
            "development campaign estimate counts only scheduled Arena, final confirmation and final tuning work");
        var rebalancedDevelopmentCampaignEstimate = new CombatFoundationTrainingParameters
        {
            GovernanceProfile = CombatFoundationGovernanceProfileNames.Development,
            Iterations = 8,
            TrainingCampaignsPerIteration = 96,
            ArenaCampaignsPerDifficulty = 16,
            ArenaConfirmationCampaignsPerDifficulty = 48,
            NormalValidationCampaigns = 100,
            AdvancedValidationCampaigns = 200,
            CapabilityProbeCampaignsPerDifficulty = 64,
            CapabilityProbeTeacherCampaignsPerDifficulty = 16,
            ModelRetainedCandidates = 3,
            EnableTuningArena = true,
            EnableProgressiveTuning = true,
            TuningInterval = 2,
            TuningNormalCampaigns = 32,
            TuningAdvancedCampaigns = 64,
            TuningScreeningNormalCampaigns = 8,
            TuningScreeningAdvancedCampaigns = 16,
            TuningFinalistCount = 2
        }.EstimatedCampaigns();
        Assert(rebalancedDevelopmentCampaignEstimate == 2004,
            "development estimate reserves the adaptive capability ceiling while retaining sparse formal evaluation");
        var arenaChampionRuns = new List<CombatCampaignResult>
        {
            new() { DifficultyId = "normal", FinalBossVictory = true },
            new() { DifficultyId = "advanced", FinalBossVictory = true }
        };
        var arenaCandidateRuns = new List<CombatCampaignResult>
        {
            new() { DifficultyId = "normal", FinalBossVictory = false },
            new() { DifficultyId = "advanced", FinalBossVictory = false }
        };
        Assert(!CombatCampaignFoundationTrainer.ArenaNoRegressionStillPossible(
                   arenaChampionRuns,
                   arenaCandidateRuns,
                   remainingPairsPerDifficulty: 0,
                   requireAdvancedStrictGain: false)
               && CombatCampaignFoundationTrainer.ArenaNoRegressionStillPossible(
                   arenaChampionRuns,
                   arenaCandidateRuns,
                   remainingPairsPerDifficulty: 1,
                   requireAdvancedStrictGain: false)
               && CombatCampaignFoundationTrainer.ShouldStopArenaScreening(
                   arenaChampionRuns,
                   arenaCandidateRuns,
                   remainingPairsPerDifficulty: 0,
                   normalAcceptanceRate: 0.80d,
                   advancedAcceptanceRate: 0.30d)
               && !CombatCampaignFoundationTrainer.ShouldStopArenaScreening(
                   arenaChampionRuns,
                   arenaCandidateRuns,
                   remainingPairsPerDifficulty: 1,
                   normalAcceptanceRate: 0.80d,
                   advancedAcceptanceRate: 0.30d)
               && CombatCampaignFoundationTrainer.ArenaScreeningPairsSaved(
                   configuredPairsPerDifficulty: 16,
                   actuallyExecutedPairs: 8) == 24,
            "ordered arena screening stops only on mathematically unrecoverable paired regression and reports the avoided pair budget");
        var sequentialChampionRuns = Enumerable.Range(0, 32)
            .Select(index => new CombatCampaignResult
            {
                DifficultyId = index < 16 ? "normal" : "advanced",
                FinalBossVictory = false
            })
            .ToList();
        var sequentialCandidateRuns = Enumerable.Range(0, 32)
            .Select(index => new CombatCampaignResult
            {
                DifficultyId = index < 16 ? "normal" : "advanced",
                FinalBossVictory = true
            })
            .ToList();
        Assert(CombatCampaignFoundationTrainer.ArenaSequentialDecision(
                   sequentialChampionRuns,
                   sequentialCandidateRuns,
                   remainingPairsPerDifficulty: 16,
                   minimumDiscordantPairs: 8,
                   normalAcceptanceRate: 0.80d,
                   advancedAcceptanceRate: 0.50d,
                   requireAdvancedStrictGain: false)
               == FoundationArenaSequentialDecision.Accept
               && !CombatCampaignFoundationTrainer.ShouldStopArenaConfirmation(
                   FoundationArenaSequentialDecision.Accept)
               && CombatCampaignFoundationTrainer.ArenaSequentialDecision(
                   arenaChampionRuns,
                   arenaCandidateRuns,
                   remainingPairsPerDifficulty: 0,
                   minimumDiscordantPairs: 1,
                   normalAcceptanceRate: 0.80d,
                   advancedAcceptanceRate: 0.50d,
                   requireAdvancedStrictGain: false)
               == FoundationArenaSequentialDecision.Reject
               && CombatCampaignFoundationTrainer.ShouldStopArenaConfirmation(
                   FoundationArenaSequentialDecision.Reject),
            "sequential arena may diagnose provisional acceptance, but confirmation stops early only when recovery is mathematically impossible");
        var absolutePathChampion = Enumerable.Range(0, 12)
            .Select(index => new CombatCampaignResult
            {
                DifficultyId = "normal",
                FinalBossVictory = index < 11
            })
            .Concat(Enumerable.Range(0, 12).Select(index =>
                new CombatCampaignResult
                {
                    DifficultyId = "advanced",
                    FinalBossVictory = index < 10
                }))
            .ToList();
        var absolutePathCandidate = Enumerable.Range(0, 12)
            .Select(_ => new CombatCampaignResult
            {
                DifficultyId = "normal",
                FinalBossVictory = true
            })
            .Concat(Enumerable.Range(0, 12).Select(index =>
                new CombatCampaignResult
                {
                    DifficultyId = "advanced",
                    FinalBossVictory = index < 5
                }))
            .ToList();
        Assert(!CombatCampaignFoundationTrainer.ArenaNoRegressionStillPossible(
                   absolutePathChampion,
                   absolutePathCandidate,
                   remainingPairsPerDifficulty: 4,
                   requireAdvancedStrictGain: false)
               && CombatCampaignFoundationTrainer
                   .ArenaAbsoluteQualificationStillPossible(
                       absolutePathCandidate,
                       remainingPairsPerDifficulty: 4,
                       normalAcceptanceRate: 0.80d,
                       advancedAcceptanceRate: 0.30d)
               && !CombatCampaignFoundationTrainer.ShouldStopArenaScreening(
                   absolutePathChampion,
                   absolutePathCandidate,
                   remainingPairsPerDifficulty: 4,
                   normalAcceptanceRate: 0.80d,
                   advancedAcceptanceRate: 0.30d)
               && CombatCampaignFoundationTrainer.ArenaSequentialDecision(
                   absolutePathChampion,
                   absolutePathCandidate,
                   remainingPairsPerDifficulty: 4,
                   minimumDiscordantPairs: 8,
                   normalAcceptanceRate: 0.80d,
                   advancedAcceptanceRate: 0.30d,
                   requireAdvancedStrictGain: false)
                  != FoundationArenaSequentialDecision.Reject,
            "arena prefixes retain an absolute-qualified-best path even after relative no-regression becomes mathematically unreachable");
        var unrecoverableAdvancedCandidate = Enumerable.Range(0, 40)
            .Select(index => new CombatCampaignResult
            {
                DifficultyId = "advanced",
                FinalBossVictory = index < 4
            })
            .ToList();
        Assert(!CombatCampaignFoundationTrainer.ArenaAdvancedAcceptanceStillPossible(
                   unrecoverableAdvancedCandidate,
                   remainingPairs: 8,
                   advancedAcceptanceRate: 0.30d)
               && CombatCampaignFoundationTrainer.ArenaSequentialDecision(
                   Enumerable.Range(0, 40)
                       .Select(_ => new CombatCampaignResult
                       {
                           DifficultyId = "advanced",
                           FinalBossVictory = false
                       })
                       .ToList(),
                   unrecoverableAdvancedCandidate,
                   remainingPairsPerDifficulty: 8,
                   minimumDiscordantPairs: 8,
                   normalAcceptanceRate: 0.80d,
                   advancedAcceptanceRate: 0.30d,
                   requireAdvancedStrictGain: false)
               == FoundationArenaSequentialDecision.Reject,
            "sequential arena rejects as soon as the absolute Advanced gate is mathematically unreachable");
        Assert(CombatCampaignFoundationTrainer
                   .EffectiveArenaScreeningPairsPerDifficulty(
                       configuredPairs: 16,
                       evaluationBatchSize: 4,
                       diagnosticOnly: true) == 4
               && CombatCampaignFoundationTrainer
                   .EffectiveArenaScreeningPairsPerDifficulty(
                       configuredPairs: 16,
                       evaluationBatchSize: 4,
                       diagnosticOnly: false) == 16,
            "hard non-Arena gate failures reduce screening to a diagnostic batch while eligible candidates retain the full evidence budget");
        var lateStrategyShortfalls = new Dictionary<string, int>
        {
            ["strategy-growth"] = 139,
            ["strategy-finale"] = 105,
            ["strategy-bank"] = 23
        };
        var survivalStrategyShortfalls = new Dictionary<string, int>
        {
            ["strategy-survival"] = 80,
            ["strategy-finale"] = 10
        };
        Assert(CombatCampaignFoundationTrainer.StrategyQuotaCollectionCampaignLimit(
                   lateStrategyShortfalls) == 8
               && CombatCampaignFoundationTrainer.StrategyQuotaCollectionDifficulty(
                   lateStrategyShortfalls,
                   0) == "normal"
               && CombatCampaignFoundationTrainer.StrategyQuotaCollectionDifficulty(
                   lateStrategyShortfalls,
                   2) == "advanced"
               && CombatCampaignFoundationTrainer.StrategyQuotaCollectionDifficulty(
                   survivalStrategyShortfalls,
                   0) == "advanced"
               && CombatCampaignFoundationTrainer.StrategyQuotaCollectionDifficulty(
                   survivalStrategyShortfalls,
                   2) == "normal",
            "strategy quota collection scales with the shortfall and routes late-game versus survival deficits to the useful difficulty");
        var strategyYieldProfiles =
            new Dictionary<string, FoundationStrategyQuotaYieldProfile>(
                StringComparer.Ordinal)
            {
                ["normal"] = new()
                {
                    Campaigns = 2,
                    StrategyFrames = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["strategy-bank"] = 40
                    }
                },
                ["advanced"] = new()
                {
                    Campaigns = 2,
                    StrategyFrames = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["strategy-bank"] = 2,
                        ["strategy-survival"] = 50
                    }
                }
            };
        Assert(CombatCampaignFoundationTrainer.StrategyQuotaCollectionDifficulty(
                   new Dictionary<string, int>
                   {
                       ["strategy-bank"] = 20
                   },
                   4,
                   strategyYieldProfiles) == "normal"
               && CombatCampaignFoundationTrainer.StrategyQuotaCollectionDifficulty(
                   new Dictionary<string, int>
                   {
                       ["strategy-survival"] = 20
                   },
                   4,
                   strategyYieldProfiles) == "advanced",
            "quota collection learns per-stratum difficulty yield instead of repeating a fixed routing heuristic");
        Assert(Math.Abs(CombatCampaignFoundationTrainer
                            .OverallEstimatedRemainingSeconds(
                                600d,
                                40d,
                                phaseEstimateActive: true) - 600d) < 0.000001d
               && Math.Abs(CombatCampaignFoundationTrainer
                               .OverallEstimatedRemainingSeconds(
                                   0d,
                                   40d,
                                   phaseEstimateActive: true) - 40d) < 0.000001d,
            "phase ETA remains a sub-estimate and no longer replaces a larger end-to-end training ETA");
        Assert(CombatCampaignFoundationTrainer.ShouldAcceptWorkingModel(
                   workingCheckpoint: true,
                   bootstrapPromotion: false,
                   meaningfulWinGain: true,
                   meaningfulProgressGain: false)
               && !CombatCampaignFoundationTrainer.ShouldAcceptWorkingModel(
                   workingCheckpoint: true,
                   bootstrapPromotion: false,
                   meaningfulWinGain: false,
                   meaningfulProgressGain: false)
               && !CombatCampaignFoundationTrainer.ShouldAcceptWorkingModel(
                   workingCheckpoint: false,
                   bootstrapPromotion: false,
                   meaningfulWinGain: true,
                   meaningfulProgressGain: true),
            "working models advance on current-window paired gains rather than incomparable historical arena scores");
        var collisionGateModel = new CombatPolicyValueNetworkDefinition
        {
            Metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["stateFeatureCollisionRate"] = 0.19d,
                ["actionFeatureCollisionRate"] = 0.05d
            }
        };
        Assert(CombatCampaignFoundationTrainer.FeatureCollisionGatePassed(
                   collisionGateModel,
                   maximumStateRate: 0.20d,
                   maximumActionRate: 0.06d)
               && !CombatCampaignFoundationTrainer.FeatureCollisionGatePassed(
                   new CombatPolicyValueNetworkDefinition
                   {
                       Metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["stateFeatureCollisionRate"] = 0.21d,
                           ["actionFeatureCollisionRate"] = 0.05d
                       }
                   },
                   maximumStateRate: 0.20d,
                   maximumActionRate: 0.06d)
               && CombatCampaignFoundationTrainer.FormalPromotionGatePassed(
                   bootstrap: false,
                   arenaEvidence: true,
                   absoluteAdvanced: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true)
               && !CombatCampaignFoundationTrainer.FormalPromotionGatePassed(
                   bootstrap: true,
                   arenaEvidence: true,
                   absoluteAdvanced: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true),
            "formal promotion requires explicit feature-collision evidence and never publishes a bootstrap candidate");
        Assert(CombatCampaignFoundationTrainer.NonInferiorityGatePassed(
                   workingCheckpoint: true,
                   validNormalPairs: 64,
                   validAdvancedPairs: 64,
                   candidateOnlyWins: 2,
                   championOnlyWins: 0,
                   pairedRegressionWilsonUpperBound: 0.03d,
                   absoluteNormal: true,
                   absoluteAdvanced: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true)
               && !CombatCampaignFoundationTrainer.NonInferiorityGatePassed(
                   workingCheckpoint: true,
                   validNormalPairs: 64,
                   validAdvancedPairs: 64,
                   candidateOnlyWins: 2,
                   championOnlyWins: 0,
                   pairedRegressionWilsonUpperBound: 0.06d,
                   absoluteNormal: true,
                   absoluteAdvanced: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true),
            "equivalent candidates use a dedicated paired non-inferiority gate without lowering the significant-gain discordance threshold");
        Assert(CombatCampaignFoundationTrainer.AbsoluteQualificationGatePassed(
                   validArenaPairs: 32,
                   expectedArenaPairs: 32,
                   absoluteNormal: true,
                   absoluteAdvanced: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true)
               && !CombatCampaignFoundationTrainer.AbsoluteQualificationGatePassed(
                   validArenaPairs: 31,
                   expectedArenaPairs: 32,
                   absoluteNormal: true,
                   absoluteAdvanced: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true),
            "absolute qualification accepts complete candidates that clear both configured win-rate lines and every hard safety gate");
        var absoluteScreeningConfirmation =
            CombatCampaignFoundationTrainer.ShouldRunArenaConfirmation(
                relativeScreeningPassed: false,
                absoluteScreeningPassed: true,
                confirmationPairsPerDifficulty: 48,
                bootstrap: false,
                offlineHeads: true,
                strategyQuota: true,
                featureCollision: true);
        var fullArenaQualificationPairs =
            CombatCampaignFoundationTrainer.ExpectedArenaQualificationPairs(
                screeningPairsPerDifficulty: 16,
                confirmationPairsPerDifficulty: 48,
                confirmationRan: absoluteScreeningConfirmation);
        Assert(absoluteScreeningConfirmation
               && !CombatCampaignFoundationTrainer.ShouldRunArenaConfirmation(
                   relativeScreeningPassed: false,
                   absoluteScreeningPassed: false,
                   confirmationPairsPerDifficulty: 48,
                   bootstrap: false,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true)
               && CombatCampaignFoundationTrainer.ShouldRunArenaConfirmation(
                   relativeScreeningPassed: false,
                   absoluteScreeningPassed: true,
                   confirmationPairsPerDifficulty: 48,
                   bootstrap: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true)
               && !CombatCampaignFoundationTrainer.ShouldRunArenaConfirmation(
                   relativeScreeningPassed: true,
                   absoluteScreeningPassed: false,
                   confirmationPairsPerDifficulty: 48,
                   bootstrap: true,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true)
               && !CombatCampaignFoundationTrainer.ShouldRunArenaConfirmation(
                   relativeScreeningPassed: false,
                   absoluteScreeningPassed: true,
                   confirmationPairsPerDifficulty: 48,
                   bootstrap: false,
                   offlineHeads: false,
                   strategyQuota: true,
                   featureCollision: true)
               && !CombatCampaignFoundationTrainer.ShouldRunArenaConfirmation(
                   relativeScreeningPassed: false,
                   absoluteScreeningPassed: true,
                   confirmationPairsPerDifficulty: 0,
                   bootstrap: false,
                   offlineHeads: true,
                   strategyQuota: true,
                   featureCollision: true)
               && fullArenaQualificationPairs == 128
               && !CombatCampaignFoundationTrainer
                   .AbsoluteQualificationGatePassed(
                       validArenaPairs: 32,
                       expectedArenaPairs: fullArenaQualificationPairs,
                       absoluteNormal: true,
                       absoluteAdvanced: true,
                       offlineHeads: true,
                       strategyQuota: true,
                       featureCollision: true),
            "an absolute-only screening finalist must run all configured confirmation pairs and cannot qualify on screening evidence alone");
        var qualifiedOpening = new CombatCampaignFoundationIteration
        {
            AbsoluteQualificationGatePassed = true,
            CandidateModelId = "opening",
            CandidateNormalWinRate = 1d,
            CandidateAdvancedWinRate = 0.6875d,
            CandidateArenaScore = 10d,
            CandidateAverageCompletedBattles = 20d,
            ModelValidationMetrics = new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 128,
                CompositeLoss = 0.20d
            }
        };
        var qualifiedBalanced = new CombatCampaignFoundationIteration
        {
            AbsoluteQualificationGatePassed = true,
            CandidateModelId = "balanced",
            CandidateNormalWinRate = 0.9375d,
            CandidateAdvancedWinRate = 0.8125d,
            CandidateArenaScore = 5d,
            CandidateAverageCompletedBattles = 18d,
            ModelValidationMetrics = new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 128,
                CompositeLoss = 0.25d
            }
        };
        Assert(CombatCampaignFoundationTrainer.CompareAbsoluteQualifiedCandidates(
                   qualifiedBalanced,
                   qualifiedOpening) > 0
               && CombatCampaignFoundationTrainer.QualifiedCandidateArenaScore(
                   qualifiedBalanced)
                  > CombatCampaignFoundationTrainer.QualifiedCandidateArenaScore(
                      qualifiedOpening),
            "qualified-candidate selection prefers the strongest balanced absolute arena result before score, depth, and validation-loss tie breakers");
        var qualifiedResumeEvidence = new CombatCampaignFoundationIteration
        {
            CandidateModelId = policyValueTraining.Model!.ModelId,
            AbsoluteQualificationGatePassed = true
        };
        var qualifiedResume = new CombatCampaignFoundationResumeState
        {
            Champion = policyValueTraining.Model,
            WorkingChampion = policyValueTraining.Model,
            LatestTrainingModel = policyValueTraining.Model,
            AbsoluteQualifiedBestModel = policyValueTraining.Model,
            AbsoluteQualifiedBestEvidence = qualifiedResumeEvidence
        };
        Assert(CombatCampaignFoundationTrainer.ResumeCompatible(qualifiedResume),
            "resume accepts a separately persisted absolute-qualified model only when its hard-gate evidence names the same model");
        qualifiedResumeEvidence.CandidateModelId = "mismatched-model";
        Assert(!CombatCampaignFoundationTrainer.ResumeCompatible(qualifiedResume),
            "resume rejects detached absolute-qualified evidence before it can influence final model selection");
        qualifiedResumeEvidence.CandidateModelId =
            policyValueTraining.Model.ModelId;
        var pendingOlder = new CombatFoundationPendingArenaCandidate
        {
            SourceIteration = 7,
            Model = policyValueTraining.Model,
            OfflineHeadRegressionGatePassed = true,
            StrategyQuotaGatePassed = true,
            FeatureCollisionGatePassed = true,
            SelectionAnchorMetrics = new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 128,
                CompositeLoss = 0.20d
            },
            ValidationMetrics = new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 128,
                CompositeLoss = 0.22d,
                ValueMae = 0.10d,
                Brier = 0.10d,
                DeathBrier = 0.10d
            }
        };
        var pendingNewestRegression = new CombatFoundationPendingArenaCandidate
        {
            SourceIteration = 12,
            Model = policyValueTraining.Model,
            OfflineHeadRegressionGatePassed = true,
            StrategyQuotaGatePassed = true,
            FeatureCollisionGatePassed = true,
            SelectionAnchorMetrics = new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 128,
                CompositeLoss = 0.30d
            },
            ValidationMetrics = new CombatPolicyValueMetricSnapshot
            {
                FrameCount = 128,
                CompositeLoss = 0.25d,
                ValueMae = 0.12d,
                Brier = 0.12d,
                DeathBrier = 0.12d
            }
        };
        qualifiedResume.BestPendingArenaCandidate = pendingOlder;
        Assert(CombatCampaignFoundationTrainer.BetterPendingArenaCandidate(
                   pendingNewestRegression,
                   pendingOlder) == pendingOlder
               && CombatCampaignFoundationTrainer.ResumeCompatible(
                   qualifiedResume),
            "training-only rounds persist the strongest offline-safe pending Arena candidate without letting a newer regression overwrite it");
        pendingOlder.StrategyQuotaGatePassed = false;
        Assert(!CombatCampaignFoundationTrainer.ResumeCompatible(
                qualifiedResume),
            "resume rejects a pending Arena slot that no longer satisfies its offline, strategy, or collision safety gates");
        qualifiedResume.BestPendingArenaCandidate = null;
        var quantileMetricClone =
            CombatCampaignFoundationTrainer.CloneMetricSnapshot(
                new CombatPolicyValueMetricSnapshot
                {
                    ActionQuantilePinball = 0.125d,
                    ActionQuantileMae = 0.25d,
                    ActionQuantileLabelCount = 4096
                });
        Assert(Math.Abs(quantileMetricClone.ActionQuantilePinball - 0.125d) < 0.0000001d
               && Math.Abs(quantileMetricClone.ActionQuantileMae - 0.25d) < 0.0000001d
               && quantileMetricClone.ActionQuantileLabelCount == 4096,
            "iteration metric cloning preserves action-quantile telemetry");
        const long expectedExpandedModelParameters = 1_585_174L;
        var expandedModelParameters =
            (2048L * 512) + (1024L * 512)
            + (2L * 512)
            + 512 + 1
            + (16L * 512) + 16
            + (5L * (512 + 1));
        var expandedModelSizeFixture = new CombatPolicyValueNetworkDefinition
        {
            StateWeights = Enumerable.Repeat(0.123456789012345d, 2048 * 512).ToArray(),
            StateBias = Enumerable.Repeat(0.123456789012345d, 512).ToArray(),
            ActionWeights = Enumerable.Repeat(0.123456789012345d, 1024 * 512).ToArray(),
            ActionBias = Enumerable.Repeat(0.123456789012345d, 512).ToArray(),
            PolicyWeights = Enumerable.Repeat(0.123456789012345d, 512).ToArray(),
            ActionQuantileWeights = Enumerable.Repeat(
                0.123456789012345d,
                16 * 512).ToArray(),
            ActionQuantileBias = Enumerable.Repeat(0.123456789012345d, 16).ToArray(),
            ValueWeights = Enumerable.Repeat(0.123456789012345d, 512).ToArray(),
            WinWeights = Enumerable.Repeat(0.123456789012345d, 512).ToArray(),
            RiskWeights = Enumerable.Repeat(0.123456789012345d, 512).ToArray(),
            HpWeights = Enumerable.Repeat(0.123456789012345d, 512).ToArray(),
            TurnWeights = Enumerable.Repeat(0.123456789012345d, 512).ToArray()
        };
        var expandedModelSerializedBytes = JsonSerializer.SerializeToUtf8Bytes(
            expandedModelSizeFixture).LongLength;
        Assert(expandedModelParameters == expectedExpandedModelParameters
               && expandedModelSerializedBytes
                  < CombatFoundationModelPackageProtocol.SoftMaximumUncompressedBytes
               && CombatFoundationModelPackageProtocol.TryValidateSerializedSize(
                   49_999_999L,
                   out _)
               && !CombatFoundationModelPackageProtocol.TryValidateSerializedSize(
                   50_000_001L,
                   out _),
            "2048/1024/512 parameter and serialized-size budgets remain below the 50 MB package hard gate");
        Assert(CombatCampaignFoundationTrainer.OfflineHeadRegressionPassed(
                   new CombatPolicyValueMetricSnapshot
                   {
                       CompositeLoss = 0.40d,
                       ValueMae = 0.30d,
                       Brier = 0.10d,
                       DeathBrier = 0.10d
                   },
                   new CombatPolicyValueMetricSnapshot
                   {
                       CompositeLoss = 0.39d,
                       ValueMae = 0.31d,
                       Brier = 0.10d,
                       DeathBrier = 0.104d
                   },
                   0.05d)
               && !CombatCampaignFoundationTrainer.OfflineHeadRegressionPassed(
                   new CombatPolicyValueMetricSnapshot
                   {
                       CompositeLoss = 0.40d,
                       ValueMae = 0.30d,
                       Brier = 0.10d,
                       DeathBrier = 0.10d
                   },
                   new CombatPolicyValueMetricSnapshot
                   {
                       CompositeLoss = 0.39d,
                       ValueMae = 0.40d,
                       Brier = 0.10d,
                       DeathBrier = 0.12d
                   },
                   0.05d),
            "formal promotion rejects candidates whose long-horizon value or death-risk heads regress on the same offline holdout");
        var capabilityBaselineRuns = new CombatCampaignResult?[]
        {
            new() { DifficultyId = "normal", FinalBossVictory = true },
            new() { DifficultyId = "normal", FinalBossVictory = true },
            new() { DifficultyId = "advanced", FinalBossVictory = true },
            new() { DifficultyId = "advanced", FinalBossVictory = true }
        };
        var capabilityChampionRuns = new CombatCampaignResult?[]
        {
            new() { DifficultyId = "normal", FinalBossVictory = false },
            new() { DifficultyId = "normal", FinalBossVictory = false },
            new() { DifficultyId = "advanced", FinalBossVictory = false },
            new() { DifficultyId = "advanced", FinalBossVictory = false }
        };
        Assert(CombatCampaignFoundationTrainer.CapabilityNoRegressionStillPossible(
                   capabilityBaselineRuns,
                   capabilityChampionRuns,
                   campaignsPerDifficulty: 2,
                   completedPerDifficulty: 1)
               && !CombatCampaignFoundationTrainer.CapabilityNoRegressionStillPossible(
                   capabilityBaselineRuns,
                   capabilityChampionRuns,
                   campaignsPerDifficulty: 2,
                   completedPerDifficulty: 2),
            "capability probe stops only after the remaining paired samples cannot recover baseline parity");
        var reusableRiskStatistics = new CombatSearchRiskStatistics();
        reusableRiskStatistics.Record(-2d, 0.8d);
        reusableRiskStatistics.Record(2d, 0.2d);
        var firstRiskEstimate = reusableRiskStatistics.Estimate(0.5d);
        reusableRiskStatistics.Reset();
        reusableRiskStatistics.Record(4d, 0.1d);
        var resetRiskEstimate = reusableRiskStatistics.Estimate(0.5d);
        Assert(firstRiskEstimate.SampleCount == 2
               && resetRiskEstimate.SampleCount == 1
               && Math.Abs(resetRiskEstimate.Mean - 4d) < 0.000000001d,
            "search risk statistics reset reuses storage without retaining prior evidence");
        for (var index = 0; index < 2048; index++)
        {
            reusableRiskStatistics.Record(index, 0.5d);
        }
        _ = reusableRiskStatistics.Estimate(0.1d);
        var riskAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 128; index++)
        {
            reusableRiskStatistics.Record(index, 0.5d);
            _ = reusableRiskStatistics.Estimate(0.1d);
        }
        var riskAllocationBytes =
            GC.GetAllocatedBytesForCurrentThread() - riskAllocationBefore;
        Assert(riskAllocationBytes < 64 * 1024,
            "risk estimation reuses its ordered-sample buffer in the search hot path");
        var batchDiagnostics = CombatPolicyValueBatchDiagnostics.Capture();
        Assert(batchDiagnostics.Requests >= 12
               && batchDiagnostics.BatchEvaluations > 0
               && batchDiagnostics.AverageBatchSize >= 1d
               && batchDiagnostics.AverageWaitMicroseconds >= 0d,
            "batched inference exposes fill, flush, and wait diagnostics");
        var appendRequest = new CombatCampaignFoundationTrainingRequest
        {
            Iterations = 3,
            AdditionalIterationsOnResume = 3,
            Resume = new CombatCampaignFoundationResumeState
            {
                Stage = "validation",
                NextIteration = 2
            }
        };
        Assert(
            CombatCampaignFoundationTrainer.ResolveIterationLimit(appendRequest) == 5,
            "terminal rejected checkpoints append configured iterations instead of rerunning validation at the old limit");
        appendRequest.Resume.Stage = "iteration-complete";
        Assert(
            CombatCampaignFoundationTrainer.ResolveIterationLimit(appendRequest) == 5,
            "iteration-complete checkpoints append configured iterations from their next iteration boundary");
        appendRequest.Resume.Replay.Add(new CombatEpisode
        {
            ModelProtocol = CombatPolicyValueProtocol.EpisodeProtocol,
            FeatureSchemaVersion = -1
        });
        Assert(
            CombatCampaignFoundationTrainer.ResolveIterationLimit(appendRequest) == 3,
            "protocol-incompatible resume payloads cannot inflate a fresh run's iteration limit");
        var continuationManifest = new CombatFoundationCompatibilityManifest
        {
            RulesetHash = "rules",
            NativeProgramPackageHash = "new-worker",
            CampaignId = "campaign",
            CampaignVersion = "1",
            TrainingCampaignHash = "training",
            ValidationCampaignHash = "validation",
            FeatureSchemaVersion = CombatPolicyValueProtocol.FeatureSchemaVersion,
            FeatureEncodingMode = "partitioned-v4",
            TrainingPolicyVersion =
                CombatFoundationTrainingProtocol.TrainingPolicyVersion,
            StateDimensions = 128,
            ActionDimensions = 96,
            HiddenDimensions = 64
        };
        var priorWorkerManifest = new CombatFoundationCompatibilityManifest
        {
            RulesetHash = continuationManifest.RulesetHash,
            NativeProgramPackageHash = "old-worker",
            CampaignId = continuationManifest.CampaignId,
            CampaignVersion = continuationManifest.CampaignVersion,
            TrainingCampaignHash = continuationManifest.TrainingCampaignHash,
            ValidationCampaignHash = continuationManifest.ValidationCampaignHash,
            FeatureSchemaVersion = continuationManifest.FeatureSchemaVersion,
            FeatureEncodingMode = continuationManifest.FeatureEncodingMode,
            TrainingPolicyVersion = continuationManifest.TrainingPolicyVersion,
            StateDimensions = continuationManifest.StateDimensions,
            ActionDimensions = continuationManifest.ActionDimensions,
            HiddenDimensions = continuationManifest.HiddenDimensions
        };
        Assert(
            CombatCampaignFoundationTrainer.ManifestCompatible(
                priorWorkerManifest,
                continuationManifest),
            "iteration-boundary continuation tolerates a rebuilt worker while retaining ruleset, campaign, feature, and model compatibility gates");
        var originalTransformerOptions = foundationRequest.TransformerTeacher;
        CombatCampaignFoundationResumeState? teacherBlockedCheckpoint = null;
        foundationRequest.TransformerTeacher = new CombatTransformerTeacherOptions
        {
            Backend = CombatTransformerTeacherBackendNames.Cuda,
            MinimumFrames = 64
        };
        foundationRequest.Checkpoint = checkpoint =>
            teacherBlockedCheckpoint = checkpoint;
        var deterministicFailureTeacher =
            new DeterministicFailureTransformerTeacher();
        var teacherBlockedTraining = new CombatCampaignFoundationTrainer(
                transformerTeacher: deterministicFailureTeacher)
            .Run(
                foundationRequest,
                campaignRules.Ruleset,
                foundationTraining.Champion);
        foundationRequest.Checkpoint = null;
        foundationRequest.TransformerTeacher = originalTransformerOptions;
        Assert(deterministicFailureTeacher.InvocationCount == 1
               && !teacherBlockedTraining.Success
               && !teacherBlockedTraining.AcceptancePassed
               && teacherBlockedTraining.FormalModelBlocked
               && teacherBlockedTraining.Iterations.Count == 0
               && teacherBlockedTraining.NextIteration == 0
               && teacherBlockedTraining.TransformerTeacherReports.Count == 1
               && teacherBlockedTraining.TransformerTeacherReports[0]
                   .ProcessExitCode == 1
               && teacherBlockedTraining.Message.Contains(
                   "不可作为正式底模",
                   StringComparison.Ordinal)
               && teacherBlockedCheckpoint != null
               && teacherBlockedCheckpoint.Stage == "model-training"
               && teacherBlockedCheckpoint.NextIteration == 0
               && teacherBlockedCheckpoint.ModelTraining == null
               && teacherBlockedCheckpoint.Replay.Count > 0,
            "a permanent Transformer process failure stops at its first invocation, returns complete formal-model diagnostics, and checkpoints the current replay at the resumable model-training boundary");
        var originalTeacherFailureResume = foundationRequest.Resume;
        CombatCampaignFoundationResumeState? throwingTeacherCheckpoint = null;
        foundationRequest.Resume = teacherBlockedCheckpoint;
        foundationRequest.TransformerTeacher = new CombatTransformerTeacherOptions
        {
            Backend = CombatTransformerTeacherBackendNames.Cuda,
            MinimumFrames = 64
        };
        foundationRequest.Checkpoint = checkpoint =>
            throwingTeacherCheckpoint = checkpoint;
        var throwingTeacher = new ThrowingTransformerTeacher();
        var throwingTeacherTraining = new CombatCampaignFoundationTrainer(
                transformerTeacher: throwingTeacher)
            .Run(
                foundationRequest,
                campaignRules.Ruleset,
                foundationTraining.Champion);
        foundationRequest.Checkpoint = null;
        foundationRequest.TransformerTeacher = originalTransformerOptions;
        foundationRequest.Resume = originalTeacherFailureResume;
        var throwingTeacherReport = throwingTeacherTraining
            .TransformerTeacherReports.Single();
        Assert(throwingTeacher.InvocationCount == 1
               && !throwingTeacherTraining.Success
               && !throwingTeacherTraining.AcceptancePassed
               && throwingTeacherTraining.FormalModelBlocked
               && throwingTeacherTraining.Iterations.Count == 0
               && throwingTeacherReport.FailureKind
                  == CombatTransformerTeacherFailureKinds.Process
               && !throwingTeacherReport.RetryableFailure
               && throwingTeacherReport.FormalModelBlocked
               && throwingTeacherTraining.Message.Contains(
                   "执行边界",
                   StringComparison.Ordinal)
               && throwingTeacherTraining.Message.Contains(
                   "不可作为正式底模",
                   StringComparison.Ordinal)
               && throwingTeacherCheckpoint != null
               && throwingTeacherCheckpoint.Stage == "model-training"
               && throwingTeacherCheckpoint.NextIteration == 0
               && throwingTeacherCheckpoint.Replay.Count > 0,
            "an exception escaping the Transformer host boundary is converted to one permanent formal-model block and preserves the current resumable checkpoint instead of repeating in later iterations");
        CombatCampaignFoundationResumeState? capturedFoundationCheckpoint = null;
        var baselineWorkingModel = foundationTraining.WorkingChampion!;
        var interruptedFoundationObserved = false;
        using (var interruptedFoundation = new CancellationTokenSource())
        {
            foundationRequest.Checkpoint = checkpoint =>
            {
                if (checkpoint.Stage == "model-training"
                    && checkpoint.ModelTraining == null)
                {
                    capturedFoundationCheckpoint = checkpoint;
                    interruptedFoundation.Cancel();
                }
            };
            try
            {
                new CombatCampaignFoundationTrainer().Run(
                    foundationRequest,
                    campaignRules.Ruleset,
                    cancellationToken: interruptedFoundation.Token);
            }
            catch (OperationCanceledException)
            {
                interruptedFoundationObserved = true;
            }
        }
        foundationRequest.Checkpoint = null;
        foundationRequest.Resume = capturedFoundationCheckpoint;
        var resumedFoundationTraining = new CombatCampaignFoundationTrainer().Run(
            foundationRequest,
            campaignRules.Ruleset);
        var resumedWorkingModel = resumedFoundationTraining.WorkingChampion;
        Assert(interruptedFoundationObserved
               && capturedFoundationCheckpoint != null
               && capturedFoundationCheckpoint.SchemaVersion
                  == CombatFoundationWorkerProtocol.SchemaVersion
               && capturedFoundationCheckpoint.RunSeed
                  == foundationRequest.RunSeed
               && capturedFoundationCheckpoint.TrainingSeedStart
                  == foundationRequest.TrainingSeedStart
               && capturedFoundationCheckpoint.Compatibility.FeatureSchemaVersion
                  == CombatPolicyValueProtocol.FeatureSchemaVersion
               && capturedFoundationCheckpoint.Compatibility.CampaignId
                  == foundationRequest.TrainingCampaign.CampaignId
               && !string.IsNullOrWhiteSpace(
                   capturedFoundationCheckpoint.Compatibility.TrainingCampaignHash)
               && !string.IsNullOrWhiteSpace(
                   capturedFoundationCheckpoint.Compatibility.ValidationCampaignHash)
               && capturedFoundationCheckpoint.Compatibility.FeatureEncodingMode
                  == "partitioned-v4"
               && capturedFoundationCheckpoint.Compatibility.StateDimensions
                  == foundationRequest.Training.StateDimensions
               && capturedFoundationCheckpoint.Compatibility.HiddenDimensions == 8
               && capturedFoundationCheckpoint.CompletedCampaigns == 2
               && capturedFoundationCheckpoint.Replay.Count > 0
               && capturedFoundationCheckpoint.Replay.Count < 74
               && resumedFoundationTraining.Success
               && resumedFoundationTraining.AcceptancePassed
               && resumedWorkingModel != null
               && resumedWorkingModel.StateWeights.SequenceEqual(
                    baselineWorkingModel.StateWeights)
               && resumedWorkingModel.PolicyWeights.SequenceEqual(
                    baselineWorkingModel.PolicyWeights),
            "foundation checkpoints persist the sampled replay window and resume model training without replaying campaigns"
            + $" (interrupted={interruptedFoundationObserved}, captured={capturedFoundationCheckpoint != null},"
            + $" completed={capturedFoundationCheckpoint?.CompletedCampaigns}, replay={capturedFoundationCheckpoint?.Replay.Count},"
             + $" resumedSuccess={resumedFoundationTraining.Success}, resumedWorking={resumedWorkingModel != null},"
             + $" baselineWorking={baselineWorkingModel != null},"
             + $" stateEqual={resumedWorkingModel?.StateWeights.SequenceEqual(baselineWorkingModel!.StateWeights)},"
             + $" policyEqual={resumedWorkingModel?.PolicyWeights.SequenceEqual(baselineWorkingModel!.PolicyWeights)})");
        var requestedReplacementRunSeed = signedTransformerRunSeed + 1UL;
        var persistedSeedPlan = CombatFoundationSeedPlan.Create(
            signedTransformerRunSeed,
            foundationRequest.ValidationSeedStart);
        capturedFoundationCheckpoint!.RunSeed = persistedSeedPlan.RunSeed;
        capturedFoundationCheckpoint.TrainingSeedStart =
            persistedSeedPlan.TrainingSeedStart;
        capturedFoundationCheckpoint.ArenaSeedStart =
            persistedSeedPlan.ArenaSeedStart;
        capturedFoundationCheckpoint.TuningSeedStart =
            persistedSeedPlan.TuningSeedStart;
        capturedFoundationCheckpoint.ValidationSeedStart =
            persistedSeedPlan.ValidationSeedStart;
        capturedFoundationCheckpoint.ModelRandomSeed =
            persistedSeedPlan.ModelRandomSeed;
        var priorRunSeed = foundationRequest.RunSeed;
        var priorModelRandomSeed = foundationRequest.Training.RandomSeed;
        var priorSeedTransformerOptions = foundationRequest.TransformerTeacher;
        foundationRequest.RunSeed = requestedReplacementRunSeed;
        foundationRequest.TransformerTeacher = new CombatTransformerTeacherOptions
        {
            Backend = CombatTransformerTeacherBackendNames.Cpu,
            MinimumFrames = 64,
            RandomSeed = unchecked((int)requestedReplacementRunSeed)
        };
        var capturingSeedTeacher = new CapturingSeedTransformerTeacher();
        var persistedSeedTraining = new CombatCampaignFoundationTrainer(
                transformerTeacher: capturingSeedTeacher)
            .Run(
                foundationRequest,
                campaignRules.Ruleset,
                foundationTraining.Champion);
        foundationRequest.RunSeed = priorRunSeed;
        foundationRequest.Training.RandomSeed = priorModelRandomSeed;
        foundationRequest.TransformerTeacher = priorSeedTransformerOptions;
        Assert(capturingSeedTeacher.InvocationCount == 1
               && capturingSeedTeacher.RandomSeed
                  == expectedSignedTransformerSeed
               && persistedSeedTraining.RunSeed == signedTransformerRunSeed
               && persistedSeedTraining.ModelRandomSeed
                  == persistedSeedPlan.ModelRandomSeed,
            "a compatible manual resume keeps the checkpoint RunSeed authoritative for both the model seed plan and signed Transformer random stream");
        foundationRequest.Resume = null;
        foundationRequest.MaximumDegreeOfParallelism = 1;
        var serialFoundationTraining = new CombatCampaignFoundationTrainer().Run(
            foundationRequest,
            campaignRules.Ruleset);
        var serialWorkingModel = serialFoundationTraining.WorkingChampion;
        Assert(serialFoundationTraining.Success
               && serialFoundationTraining.AcceptancePassed
               && serialWorkingModel != null
               && serialFoundationTraining.EffectiveParallelism == 1
               && serialFoundationTraining.PeakConcurrentCampaigns == 1
               && serialWorkingModel.StateWeights.SequenceEqual(
                    baselineWorkingModel!.StateWeights)
               && serialWorkingModel.PolicyWeights.SequenceEqual(
                    baselineWorkingModel.PolicyWeights)
               && serialFoundationTraining.ValidationRuns.Select(item =>
                       item.DifficultyId + ":" + item.WorldSeed + ":" + item.PlanHash)
                   .SequenceEqual(foundationTraining.ValidationRuns.Select(item =>
                        item.DifficultyId + ":" + item.WorldSeed + ":" + item.PlanHash)),
            "foundation CPU parallelism preserves deterministic seed-order replay, model weights, and validation plans");
        Assert(foundationTraining.EpisodeCompactStateVectors > 0
               && foundationTraining.EpisodeCompactCandidateVectors
                  >= foundationTraining.EpisodeCompactStateVectors
               && foundationTraining.WorldModelObservationsBuilt == 0
               && foundationTraining.WorldModelObservationsSkipped
                  == foundationTraining.EpisodeCompactStateVectors
               && foundationTraining.EpisodeStateDictionaryMaterializations
                  < foundationTraining.EpisodeCompactStateVectors,
            "foundation telemetry confirms compact episode recording, lazy dictionaries, and disabled world-model payloads");
        Console.WriteLine(
            "Foundation telemetry fixture: parallel peak="
            + foundationTraining.PeakConcurrentCampaigns
            + "/"
            + foundationTraining.EffectiveParallelism
            + ", observedThreads="
            + foundationTraining.ObservedWorkerThreads
            + ", battles="
            + foundationTraining.CompletedBattles
            + ", allocatedMB="
            + (foundationTraining.AllocatedBytes / 1048576d).ToString("F1")
            + ", elapsed="
            + foundationTraining.ElapsedSeconds.ToString("F3")
            + "s, phaseAlloc="
            + string.Join(
                ",",
                foundationTraining.PhaseAllocatedBytes
                    .OrderByDescending(pair => pair.Value)
                    .Select(pair => pair.Key + ":" + (pair.Value / 1048576d).ToString("F0")))
            + ", compact="
            + foundationTraining.EpisodeCompactStateVectors
            + "/"
            + foundationTraining.EpisodeCompactCandidateVectors
            + ", materialized="
            + foundationTraining.EpisodeStateDictionaryMaterializations
            + "/"
            + foundationTraining.EpisodeCandidateDictionaryMaterializations
            + ", decisionAllocMB="
            + (foundationTraining.ObservationProjectionAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationTraining.DecisionEngineAllocatedBytes / 1048576d)
                .ToString("F0")
            + ", prep/searchMB="
            + (foundationDecisionAllocation.PreparationAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.SearchAllocatedBytes / 1048576d)
                .ToString("F0")
            + "["
            + (foundationDecisionAllocation.SearchSetupAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.SearchSimulationAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.SearchResultAllocatedBytes / 1048576d)
                .ToString("F0")
            + "]"
            + " sim[apply/leaf/score/expand/transposition/backprop/select/determinize/cycle/other]="
            + (foundationDecisionAllocation.ForwardApplyAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.LeafEvaluationAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.ScoreEvaluationAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.SearchExpansionAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.SearchTranspositionAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.SearchBackpropagationAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.SearchSelectionAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.RootDeterminizationAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (foundationDecisionAllocation.CycleAnalysisAllocatedBytes / 1048576d)
                .ToString("F0")
            + "/"
            + (Math.Max(
                    0L,
                    foundationDecisionAllocation.SimulationTrackedAllocatedBytes
                    - foundationDecisionAllocation.ForwardApplyAllocatedBytes
                    - foundationDecisionAllocation.LeafEvaluationAllocatedBytes
                    - foundationDecisionAllocation.ScoreEvaluationAllocatedBytes
                    - foundationDecisionAllocation.SearchExpansionAllocatedBytes
                    - foundationDecisionAllocation.SearchTranspositionAllocatedBytes
                    - foundationDecisionAllocation.SearchBackpropagationAllocatedBytes
                    - foundationDecisionAllocation.SearchSelectionAllocatedBytes
                    - foundationDecisionAllocation.RootDeterminizationAllocatedBytes
                    - foundationDecisionAllocation.CycleAnalysisAllocatedBytes)
               / 1048576d).ToString("F0")
            + "s; serial elapsed="
            + serialFoundationTraining.ElapsedSeconds.ToString("F3")
            + "s");
        var failingValidationCampaign = BuildStandardCampaign();
        failingValidationCampaign.RequireAuthoritativeRules = true;
        failingValidationCampaign.Player.MaxHp = 1;
        failingValidationCampaign.Player.CurrentHp = 0;
        foreach (var difficulty in failingValidationCampaign.Difficulties)
        {
            difficulty.ApplyGameLevelShield = false;
        }
        foundationRequest.MaximumDegreeOfParallelism = 4;
        foundationRequest.ValidationCampaign = failingValidationCampaign;
        foundationRequest.RetainValidationRunDetails = false;
        var earlyStoppedFoundationTraining = new CombatCampaignFoundationTrainer().Run(
            foundationRequest,
            campaignRules.Ruleset,
            foundationTraining.Champion);
        Assert(earlyStoppedFoundationTraining.Success
               && !earlyStoppedFoundationTraining.AcceptancePassed
               && earlyStoppedFoundationTraining.Validation.EarlyStopped
               && earlyStoppedFoundationTraining.Validation.NormalCampaigns == 5
               && earlyStoppedFoundationTraining.Validation.AdvancedCampaigns == 0
               && earlyStoppedFoundationTraining.CompletedCampaigns
                  < earlyStoppedFoundationTraining.RequestedCampaigns
               && earlyStoppedFoundationTraining.ValidationRuns.Count == 5
               && earlyStoppedFoundationTraining.ValidationRuns.All(item =>
                   item.Battles.Count == 0
                   && item.Rewards.Count == 0
                   && !item.FinalBossVictory),
            "foundation validation analyzes one deterministic configured batch and releases full battle graphs when the external worker retention policy is active"
            + $" (success={earlyStoppedFoundationTraining.Success}, accepted={earlyStoppedFoundationTraining.AcceptancePassed}, early={earlyStoppedFoundationTraining.Validation.EarlyStopped}, normal={earlyStoppedFoundationTraining.Validation.NormalCampaigns}, advanced={earlyStoppedFoundationTraining.Validation.AdvancedCampaigns}, completed={earlyStoppedFoundationTraining.CompletedCampaigns}/{earlyStoppedFoundationTraining.RequestedCampaigns}, retained={earlyStoppedFoundationTraining.ValidationRuns.Count}, compact={earlyStoppedFoundationTraining.ValidationRuns.Count(item => item.Battles.Count == 0 && item.Rewards.Count == 0 && !item.FinalBossVictory)})");
        foundationRequest.RetainValidationRunDetails = true;
        projectedStrike.Fidelity = CombatRuleFidelity.Approximate;
        var invalidPreflightTraining = new CombatCampaignFoundationTrainer().Run(
            foundationRequest,
            campaignRules.Ruleset,
            foundationTraining.Champion);
        projectedStrike.Fidelity = CombatRuleFidelity.Authoritative;
        Assert(!invalidPreflightTraining.Success
               && !invalidPreflightTraining.Preflight.Passed
               && invalidPreflightTraining.Preflight.InvalidCampaigns
                  == 2 + CombatFoundationIntegritySeedCorpus.KnownFailures.Count
               && invalidPreflightTraining.Preflight.Failures.Any(item =>
                   item.DifficultyId == "normal" && item.WorldSeed == 19000UL)
               && invalidPreflightTraining.Preflight.Failures.Any(item =>
                   item.DifficultyId == "advanced" && item.WorldSeed == 19001UL)
               && invalidPreflightTraining.CompletedCampaigns == 0
               && invalidPreflightTraining.Replay.Count == 0
               && invalidPreflightTraining.Message.Contains(
                   "训练前权威快检失败",
                   StringComparison.Ordinal),
            "foundation preflight fails before self-play and produces no replay when authoritative execution is invalid");
        foundationRequest.PreflightCampaignsPerDifficulty = 0;
        projectedStrike.Fidelity = CombatRuleFidelity.Approximate;
        var invalidSelfPlayTraining = new CombatCampaignFoundationTrainer().Run(
            foundationRequest,
            campaignRules.Ruleset,
            foundationTraining.Champion);
        projectedStrike.Fidelity = CombatRuleFidelity.Authoritative;
        foundationRequest.PreflightCampaignsPerDifficulty = 1;
        Assert(!invalidSelfPlayTraining.Success
               && invalidSelfPlayTraining.CompletedCampaigns == 2
               && invalidSelfPlayTraining.InvalidTrainingCampaigns == 2
               && invalidSelfPlayTraining.TrainingFailures.Count == 2
               && invalidSelfPlayTraining.TrainingFailures.Select(item =>
                       item.WorldSeed)
                   .SequenceEqual(new ulong[] { 10_000, 10_001 })
               && invalidSelfPlayTraining.TrainingFailures.All(item =>
                   item.Reasons.Count > 0)
               && invalidSelfPlayTraining.TrainingFailureCounts.Count > 0
               && invalidSelfPlayTraining.Message.Contains(
                   "normal/10000",
                   StringComparison.Ordinal),
            "foundation self-play failures retain deterministic seed, depth, and machine-readable reasons");

        var dynamicEnemyRules = new CombatRulesetBuilder("dynamic-enemy-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "observe",
                Cost = 0,
                Fidelity = CombatRuleFidelity.Authoritative
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "opening-mark",
                MaximumStacks = 99,
                Fidelity = CombatRuleFidelity.Authoritative
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "dynamic-enemy",
                MaxHp = 20,
                Fidelity = CombatRuleFidelity.Authoritative,
                InitialStatuses =
                {
                    new CombatInitialStatus
                    {
                        StatusId = "opening-mark",
                        Stacks = 2,
                        ConditionExpression = new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.GreaterThan,
                            Arguments =
                            {
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceVariable,
                                    Key = "TagDiff"
                                },
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.Constant,
                                    Constant = 20
                                }
                            }
                        }
                    }
                },
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "ordinary",
                        Priority = 1,
                        Weight = 1
                    },
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "advanced",
                        Priority = 0,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.CopyStatuses,
                                Target = CombatSimulationTarget.Player
                            },
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.ModifyVariablePercent,
                                Target = CombatSimulationTarget.Player,
                                DefinitionId = "HealMultiplier",
                                Amount = -20
                            }
                        },
                        PriorityExpression = new CombatSimulationValueExpression
                        {
                            Operation = CombatSimulationValueOperation.Conditional,
                            Arguments =
                            {
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.GreaterThan,
                                    Arguments =
                                    {
                                        new CombatSimulationValueExpression
                                        {
                                            Operation = CombatSimulationValueOperation.SourceVariable,
                                            Key = "TagDiff"
                                        },
                                        new CombatSimulationValueExpression
                                        {
                                            Operation = CombatSimulationValueOperation.Constant,
                                            Constant = 20
                                        }
                                    }
                                },
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.Constant,
                                    Constant = 5
                                },
                                new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.Constant,
                                    Constant = 0
                                }
                            }
                        },
                        Weight = 1
                    }
                }
            })
            .Freeze();
        CombatSimulationResult RunDynamicEnemy(double tagDiff)
        {
            return new CombatSimulationEngine().Run(
                new CombatScenarioDefinition
                {
                    ScenarioId = "dynamic-enemy-" + tagDiff,
                    RulesetVersion = "dynamic-enemy-v1",
                    Seed = 9,
                    TraceLevel = CombatSimulationTraceLevel.Full,
                    Player = new CombatPlayerSetup
                    {
                        RoleId = "tests",
                        MaxHp = 20,
                        CurrentHp = 20,
                        Deck = { "observe" },
                        InitialStatuses =
                        {
                            new CombatInitialStatus
                            {
                                StatusId = "opening-mark",
                                Stacks = 3
                            }
                        }
                    },
                    Enemies =
                    {
                        new CombatEnemySetup
                        {
                            EnemyId = "dynamic-enemy",
                            Variables = { ["TagDiff"] = tagDiff }
                        }
                    },
                    Limits = new CombatSimulationLimits
                    {
                        MaximumTurns = 1,
                        MaximumActions = 20,
                        MaximumCommands = 100
                    }
                },
                dynamicEnemyRules.Ruleset,
                new GreedyCombatSimulationPolicy());
        }
        var normalDynamicEnemy = RunDynamicEnemy(0);
        var advancedDynamicEnemy = RunDynamicEnemy(40);
        Assert(normalDynamicEnemy.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.IntentSelected
                && item.DefinitionId == "ordinary"),
            "normal enemy variables select the ordinary intent");
        Assert(normalDynamicEnemy.FinalState.LivingEnemies.Single().Statuses.All(status =>
                status.StatusId != "opening-mark"),
            "normal enemy variables skip the advanced opening status");
        Assert(advancedDynamicEnemy.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.IntentSelected
                && item.DefinitionId == "advanced"),
            "advanced enemy variables select the dynamically prioritized intent");
        Assert(advancedDynamicEnemy.FinalState.LivingEnemies.Single().Statuses.Single(status =>
                status.StatusId == "opening-mark").Stacks == 5
               && Math.Abs(
                   advancedDynamicEnemy.FinalState.Player!.Variables["HealMultiplier"] - 0.8d)
               < 0.000001d,
            "enemy definitions apply opening statuses, status copying, and percent variables");

        var dynamicVariableMutationRules = new CombatRulesetBuilder(
                "dynamic-variable-mutation-v1")
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "dynamic-variable-offset",
                MaximumStacks = 99,
                Fidelity = CombatRuleFidelity.Authoritative,
                DynamicModifiersPerStack =
                {
                    ["AttackedPercentDamage"] = -0.2d,
                    ["TestVariable"] = -3d
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "mutate-base-variables",
                Cost = 0,
                RequiresEnemyTarget = true,
                Fidelity = CombatRuleFidelity.Authoritative,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.ModifyVariable,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        DefinitionId = "TestVariable",
                        Amount = 5
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.ModifyVariablePercent,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        DefinitionId = "AttackedPercentDamage",
                        Amount = 10
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.DeferVariableUntilVictory,
                        Target = CombatSimulationTarget.Player,
                        DefinitionId = "TestVariable",
                        Amount = 5
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 100
                    }
                }
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "dynamic-variable-target",
                MaxHp = 80,
                Fidelity = CombatRuleFidelity.Authoritative,
                InitialStatuses =
                {
                    new CombatInitialStatus
                    {
                        StatusId = "dynamic-variable-offset",
                        Stacks = 1
                    }
                },
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Priority = 1,
                        Weight = 1
                    }
                }
            })
            .Freeze();
        var dynamicVariableMutation = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "dynamic-variable-mutation",
                RulesetVersion = "dynamic-variable-mutation-v1",
                Seed = 91,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tests",
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck = { "mutate-base-variables" },
                    InitialStatuses =
                    {
                        new CombatInitialStatus
                        {
                            StatusId = "dynamic-variable-offset",
                            Stacks = 1
                        }
                    }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "dynamic-variable-target" }
                },
                Limits = new CombatSimulationLimits
                {
                    MaximumTurns = 1,
                    MaximumActions = 10,
                    MaximumCommands = 100
                }
            },
            dynamicVariableMutationRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        var dynamicVariableTarget = dynamicVariableMutation.FinalState.Actors.Single(
            actor => actor.DefinitionId == "dynamic-variable-target");
        Assert(dynamicVariableMutation.Outcome == CombatSimulationOutcome.Victory
               && Math.Abs(dynamicVariableTarget.Variables["TestVariable"] - 5d)
                  < 0.000001d
               && Math.Abs(
                   dynamicVariableTarget.Variables["AttackedPercentDamage"] - 1.1d)
                  < 0.000001d
               && Math.Abs(
                   dynamicVariableMutation.FinalState.Player!.Variables["TestVariable"]
                   - 5d) < 0.000001d,
            "variable mutations update stored base values without baking status-derived dynamic modifiers into the base");

        context.FoundationTraining = foundationTraining;
        context.FoundationPackage = foundationPackage;
    }
}

internal sealed class DeterministicFailureTransformerTeacher :
    ICombatTransformerTeacher
{
    public int InvocationCount { get; private set; }

    public CombatTransformerTeacherReport TrainAndAnnotate(
        CombatTransformerTeacherContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InvocationCount++;
        return new CombatTransformerTeacherReport
        {
            Iteration = context.Iteration,
            Requested = true,
            RequestedBackend = context.Options.Backend,
            FailureKind = CombatTransformerTeacherFailureKinds.Configuration,
            ProcessExitCode = 1,
            RetryableFailure = false,
            FormalModelBlocked = true,
            Message = "Seed must be between 0 and 2**32 - 1"
        };
    }
}

internal sealed class ThrowingTransformerTeacher : ICombatTransformerTeacher
{
    public int InvocationCount { get; private set; }

    public CombatTransformerTeacherReport TrainAndAnnotate(
        CombatTransformerTeacherContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InvocationCount++;
        throw new InvalidOperationException(
            "Transformer host boundary contract mismatch.");
    }
}

internal sealed class CapturingSeedTransformerTeacher :
    ICombatTransformerTeacher
{
    public int InvocationCount { get; private set; }

    public int RandomSeed { get; private set; }

    public CombatTransformerTeacherReport TrainAndAnnotate(
        CombatTransformerTeacherContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InvocationCount++;
        RandomSeed = context.Options.RandomSeed;
        return new CombatTransformerTeacherReport
        {
            Iteration = context.Iteration,
            Requested = true,
            RequestedBackend = context.Options.Backend,
            Success = true,
            Message = "captured signed resume seed"
        };
    }
}
