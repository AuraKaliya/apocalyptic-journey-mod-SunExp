using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

internal static class CombatAiPolicyValueBehaviorTests
{
    public static CombatAiTrainingTestContext Run(
        CombatAiSimulationTestContext simulationContext)
    {
        var simulationEngine = simulationContext.Engine;
        var simulationRules = simulationContext.Rules;
        var bundledRulesV2 = simulationContext.BundledRules;

        var episodeProfile = new CombatDecisionProfile
        {
            Id = "balanced",
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 128,
            SearchNodeBudget = 1024,
            SearchMaxPly = 8
        };
        var episodes = new List<CombatEpisode>();
        for (var episodeIndex = 0; episodeIndex < 10; episodeIndex++)
        {
            var episodePolicy = new CombatEpisodeRecordingPolicy(
                new CombatDecisionSimulationPolicy(episodeProfile),
                episodeProfile.Id);
            var episodeResult = simulationEngine.Run(
                BuildSimulationScenario(
                    seed: (ulong)(100 + episodeIndex),
                    CombatSimulationTraceLevel.Summary),
                simulationRules.Ruleset,
                episodePolicy);
            var recordedEpisode = episodePolicy.Complete(episodeResult);
            recordedEpisode.JourneyRunId = "policy-value-run:" + episodeIndex / 2;
            recordedEpisode.JourneyBattleIndex = (episodeIndex % 4) switch
            {
                0 => 2,
                1 => 12,
                2 => 25,
                _ => 36
            };
            recordedEpisode.Campaign.DifficultyId =
                episodeIndex % 3 == 0 ? "advanced" : "normal";
            recordedEpisode.Campaign.OutcomeClass =
                episodeIndex % 2 == 0 ? "victory" : "defeat";
            recordedEpisode.Campaign.FinalBossVictory = episodeIndex % 2 == 0;
            episodes.Add(recordedEpisode);
        }
        Assert(episodes.All(episode => episode.Frames.Count > 0
                                       && episode.Frames.All(frame =>
                                           frame.Candidates.Count > 0
                                           && frame.RemainingTurnsTarget >= 0d))
               && episodes.All(episode => episode.Authoritative),
            "episode recorder captures search targets and backfills cross-turn terminal returns");
        var recordedEpochMetrics =
            new List<CombatPolicyValueEpochMetrics>();
        var policyValueTraining = CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            new CombatPolicyValueTrainingOptions
            {
                Epochs = 12,
                LearningRate = 0.01d,
                MinimumEpisodes = 4,
                RandomSeed = 17
            },
            CancellationToken.None,
            new CombatPolicyValueTrainingSession
            {
                EpochCompleted = metrics =>
                    recordedEpochMetrics.Add(metrics)
            });
        var policyValueNetworkValid = CombatPolicyValueNetworkValidator.TryValidate(
            policyValueTraining.Model,
            out var policyValueValidationDiagnostic);
        var invalidPolicyEpochs = policyValueTraining.EpochHistory
            .Where(item => !item.Calibrated)
            .Count(item =>
                item.Training.CompositeLoss <= 0d
                || item.Validation.CompositeLoss <= 0d
                || item.TrainingMeasurement != "online-minibatch"
                || string.IsNullOrWhiteSpace(item.TrainingSplitHash)
                || string.IsNullOrWhiteSpace(item.ValidationSplitHash));
        var invalidPolicyCandidates = policyValueTraining.CandidateModels.Count(candidate =>
            candidate.Model.PolicyTemperature is < 0.5d or > 3d
            || !candidate.Model.Metrics.ContainsKey("validationCompositeLoss")
            || candidate.Model.Metrics["policyTemperature"]
               != candidate.Model.PolicyTemperature);
        var policyValueDiagnosticChecks = new Dictionary<string, bool>
        {
            ["metric:testCompositeLoss"] =
                policyValueTraining.Model?.Metrics.ContainsKey("testCompositeLoss")
                == true,
            ["metric:optimizerAdamW"] =
                policyValueTraining.Model?.Metrics.GetValueOrDefault(
                    "optimizerAdamW") == 1d,
            ["metric:temperature"] =
                policyValueTraining.Model?.Metrics.GetValueOrDefault(
                    "policyTemperature")
                == policyValueTraining.Model?.PolicyTemperature,
            ["metric:validationPolicyCrossEntropy"] =
                policyValueTraining.Model?.Metrics.ContainsKey(
                    "validationPolicyCrossEntropy") == true,
            ["metric:validationCriticalPolicyAccuracy"] =
                policyValueTraining.Model?.Metrics.ContainsKey(
                    "validationCriticalPolicyAccuracy") == true,
            ["metric:validationDeathBrier"] =
                policyValueTraining.Model?.Metrics.ContainsKey(
                    "validationDeathBrier") == true,
            ["metric:validationCompositeLoss"] =
                policyValueTraining.Model?.Metrics.ContainsKey(
                    "validationCompositeLoss") == true,
            ["metric:trainingCompositeLoss"] =
                policyValueTraining.Model?.Metrics.ContainsKey(
                    "trainingCompositeLoss") == true,
            ["frame-counts"] =
                policyValueTraining.TrainingMetrics.FrameCount > 0
                && policyValueTraining.ValidationMetrics.FrameCount > 0
                && policyValueTraining.ValidationMetrics.RunCount > 0,
            ["ci-order"] =
                policyValueTraining.ValidationMetrics.CompositeLossCiUpper
                >= policyValueTraining.ValidationMetrics.CompositeLossCiLower,
            ["epochs"] =
                policyValueTraining.EpochHistory.Count
                >= policyValueTraining.CompletedEpochs
                && invalidPolicyEpochs == 0,
            ["events"] =
                recordedEpochMetrics.Count
                == policyValueTraining.CompletedEpochs + 1,
            ["candidates"] =
                policyValueTraining.CandidateModels.Count is > 0 and <= 3
                && invalidPolicyCandidates == 0,
            ["strata"] =
                policyValueTraining.FrameStratificationProtocol
                == CombatPolicyValueFrameStratificationProtocol.Version
                && policyValueTraining.FrameStrata.Count >= 4
                && policyValueTraining.MinimumFrameWeight
                >= CombatPolicyValueFrameStratificationProtocol.MinimumWeight
                && policyValueTraining.MaximumFrameWeight <= 3d,
            ["network"] = policyValueNetworkValid
        };
        var failedPolicyValueChecks = string.Join(
            ",",
            policyValueDiagnosticChecks
                .Where(item => !item.Value)
                .Select(item => item.Key));
        Assert(policyValueTraining.Success
               && policyValueTraining.Model != null
               && policyValueTraining.Model.Metrics["trainingRunCount"] == 3d
               && policyValueTraining.Model.Metrics["validationRunCount"] == 1d
               && policyValueTraining.Model.Metrics["testRunCount"] == 1d
               && policyValueTraining.Model.Metrics.ContainsKey("testCompositeLoss")
               && policyValueTraining.Model.Metrics["optimizerAdamW"] == 1d
               && policyValueTraining.Model.Metrics["optimizerStep"] > 0d
               && policyValueTraining.Model.PolicyTemperature is >= 0.5d and <= 3d
               && policyValueTraining.Model.Metrics["policyTemperature"]
                  == policyValueTraining.Model.PolicyTemperature
               && policyValueTraining.Model.Metrics.ContainsKey(
                   "validationPolicyCrossEntropy")
               && policyValueTraining.Model.Metrics.ContainsKey(
                   "validationCriticalPolicyAccuracy")
               && policyValueTraining.Model.Metrics.ContainsKey(
                   "validationDeathBrier")
               && policyValueTraining.Model.ModelProtocol
                  == "aura.combat-policy-value.mlp.v2"
               && policyValueTraining.Model.ProtocolVersion == 2
               && policyValueTraining.Model.ActionQuantileCount == 16
               && policyValueTraining.Model.ActionQuantileWeights.Length
                  == policyValueTraining.Model.HiddenDimensions * 16
               && policyValueTraining.Model.Metrics.ContainsKey(
                   "validationActionQuantilePinball")
               && policyValueTraining.Model.Metrics.ContainsKey(
                   "validationCompositeLoss")
               && policyValueTraining.Model.Metrics.ContainsKey(
                   "trainingCompositeLoss")
               && policyValueTraining.TrainingMetrics.FrameCount > 0
               && policyValueTraining.ValidationMetrics.FrameCount > 0
               && policyValueTraining.ValidationMetrics.RunCount > 0
               && policyValueTraining.ValidationMetrics
                      .CompositeLossCiUpper
                  >= policyValueTraining.ValidationMetrics
                      .CompositeLossCiLower
               && policyValueTraining.EpochHistory.Count
                  >= policyValueTraining.CompletedEpochs
               && policyValueTraining.EpochHistory.Any(item => item.Calibrated)
               && policyValueTraining.EpochHistory
                   .Where(item => !item.Calibrated)
                   .All(item =>
                       item.Training.CompositeLoss > 0d
                       && item.Validation.CompositeLoss > 0d
                       && item.TrainingMeasurement
                          == "online-minibatch"
                       && !string.IsNullOrWhiteSpace(
                           item.TrainingSplitHash)
                       && !string.IsNullOrWhiteSpace(
                           item.ValidationSplitHash))
               && recordedEpochMetrics.Count
                  == policyValueTraining.CompletedEpochs + 1
               && recordedEpochMetrics.Count(item =>
                   item.EventKind == "epoch")
                  == policyValueTraining.CompletedEpochs
               && recordedEpochMetrics.Count(item =>
                   item.EventKind == "calibrated")
                  == 1
               && policyValueTraining.CandidateModels.Count > 0
               && policyValueTraining.CandidateModels.Count <= 3
               && policyValueTraining.CandidateModels.All(candidate =>
                   candidate.Model.PolicyTemperature is >= 0.5d and <= 3d
                   && candidate.Model.Metrics.ContainsKey(
                       "validationCompositeLoss")
                   && candidate.Model.Metrics["policyTemperature"]
                      == candidate.Model.PolicyTemperature)
               && policyValueTraining.FrameStratificationProtocol
                  == CombatPolicyValueFrameStratificationProtocol.Version
               && policyValueTraining.FrameStrata.Count >= 4
               && policyValueTraining.MinimumFrameWeight
                  >= CombatPolicyValueFrameStratificationProtocol.MinimumWeight
               && policyValueTraining.MaximumFrameWeight <= 3d
               && policyValueNetworkValid,
            "complete episodes train a validated managed policy-value network, retain Top-K checkpoints, and select by multi-objective validation"
            + $" (success={policyValueTraining.Success}, model={policyValueTraining.Model != null},"
            + $" runs={policyValueTraining.Model?.Metrics.GetValueOrDefault("trainingRunCount")}/"
            + $"{policyValueTraining.Model?.Metrics.GetValueOrDefault("validationRunCount")}/"
            + $"{policyValueTraining.Model?.Metrics.GetValueOrDefault("testRunCount")},"
            + $" frames={policyValueTraining.TrainingMetrics.FrameCount}/"
            + $"{policyValueTraining.ValidationMetrics.FrameCount},"
            + $" epochs={policyValueTraining.CompletedEpochs}/{policyValueTraining.EpochHistory.Count}/"
            + $"{recordedEpochMetrics.Count}, candidates={policyValueTraining.CandidateModels.Count},"
            + $" strata={policyValueTraining.FrameStrata.Count},"
            + $" weights={policyValueTraining.MinimumFrameWeight:F3}-"
            + $"{policyValueTraining.MaximumFrameWeight:F3},"
            + $" endFrames={policyValueTraining.EndTurnDecisionFrames},"
            + $" unsafeEndFrames={policyValueTraining.UnsafeEndTurnFrames},"
            + $" temp={policyValueTraining.Model?.PolicyTemperature:F3},"
            + $" calibrated={policyValueTraining.EpochHistory.Count(item => item.Calibrated)},"
            + $" epochEvents={recordedEpochMetrics.Count(item => item.EventKind == "epoch")},"
            + $" calibratedEvents={recordedEpochMetrics.Count(item => item.EventKind == "calibrated")},"
            + $" badEpochs={invalidPolicyEpochs}, badCandidates={invalidPolicyCandidates},"
            + $" ci={policyValueTraining.ValidationMetrics.CompositeLossCiLower:F3}-"
            + $"{policyValueTraining.ValidationMetrics.CompositeLossCiUpper:F3},"
            + $" optimizer={policyValueTraining.Model?.Metrics.GetValueOrDefault("optimizerStep")},"
            + $" failed={failedPolicyValueChecks},"
            + $" protocol={policyValueTraining.FrameStratificationProtocol},"
            + $" valid={policyValueNetworkValid}:{policyValueValidationDiagnostic})");
        var originalEpisodeFrames = episodes
            .Select(episode => episode.Frames.ToList())
            .ToList();
        for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
        {
            while (episodes[episodeIndex].Frames.Count < 20)
            {
                episodes[episodeIndex].Frames.Add(
                    episodes[episodeIndex].Frames[0]);
            }
        }
        var cappedFrameTraining = CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            new CombatPolicyValueTrainingOptions
            {
                Epochs = 5,
                MinimumEpisodes = 4,
                MaximumFramesPerEpisode = 8,
                RandomSeed = 18
            });
        for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
        {
            episodes[episodeIndex].Frames = originalEpisodeFrames[episodeIndex];
        }
        Assert(cappedFrameTraining.Success
               && cappedFrameTraining.FrameCount == episodes.Count * 8
               && cappedFrameTraining.DroppedFramesByEpisodeCap
                  == episodes.Count * 12,
            "frame-balanced training uniformly caps each episode so long opening battles cannot dominate a minibatch");
        var serialParameterTraining = CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            new CombatPolicyValueTrainingOptions
            {
                Epochs = 5,
                MinimumEpochs = 5,
                EarlyStoppingPatience = 5,
                BatchSize = 8,
                GradientShardCount = 4,
                MaximumDegreeOfParallelism = 1,
                StateDimensions = 256,
                ActionDimensions = 256,
                HiddenDimensions = 128,
                MaximumFramesPerEpisode = 8,
                MinimumEpisodes = 4,
                RandomSeed = 119
            });
        var parallelParameterTraining = CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            new CombatPolicyValueTrainingOptions
            {
                Epochs = 5,
                MinimumEpochs = 5,
                EarlyStoppingPatience = 5,
                BatchSize = 8,
                GradientShardCount = 4,
                MaximumDegreeOfParallelism = 4,
                StateDimensions = 256,
                ActionDimensions = 256,
                HiddenDimensions = 128,
                MaximumFramesPerEpisode = 8,
                MinimumEpisodes = 4,
                RandomSeed = 119
            });
        Assert(serialParameterTraining.Success
               && parallelParameterTraining.Success
               && serialParameterTraining.Model!.StateWeights.SequenceEqual(
                   parallelParameterTraining.Model!.StateWeights)
               && serialParameterTraining.Model.ActionWeights.SequenceEqual(
                   parallelParameterTraining.Model.ActionWeights)
               && serialParameterTraining.Model.ActionQuantileWeights.SequenceEqual(
                   parallelParameterTraining.Model.ActionQuantileWeights)
               && parallelParameterTraining.Model.Metrics.GetValueOrDefault(
                   "parallelParameterUpdate") == 1d
               && parallelParameterTraining.Model.Metrics.GetValueOrDefault(
                   "gradientAggregationSeconds") >= 0d
               && parallelParameterTraining.Model.Metrics.GetValueOrDefault(
                   "optimizerUpdateSeconds") >= 0d,
            "parallel gradient aggregation and AdamW preserve serial parameter results and expose hot-path timing");
        var trainingCancellationObserved = false;
        using (var cancelledTraining = new CancellationTokenSource())
        {
            cancelledTraining.Cancel();
            try
            {
                CombatPolicyValueTrainer.Train(
                    episodes,
                    "balanced",
                    new CombatPolicyValueTrainingOptions { MinimumEpisodes = 4 },
                    cancelledTraining.Token);
            }
            catch (OperationCanceledException)
            {
                trainingCancellationObserved = true;
            }
        }
        Assert(trainingCancellationObserved,
            "policy-value training observes cancellation before expensive epoch work");
        var batchTrainingOptions = new CombatPolicyValueTrainingOptions
        {
            Epochs = 10,
            MinimumEpochs = 10,
            EarlyStoppingPatience = 10,
            BatchSize = 8,
            MaximumDegreeOfParallelism = 4,
            LearningRate = 0.01d,
            MinimumEpisodes = 4,
            RandomSeed = 117
        };
        CombatPolicyValueTrainingResumeState? capturedBatchCheckpoint = null;
        var batchProgress = new List<CombatPolicyValueTrainingProgress>();
        using (var interruptedBatchTraining = new CancellationTokenSource())
        {
            var interrupted = false;
            try
            {
                CombatPolicyValueTrainer.Train(
                    episodes,
                    "balanced",
                    batchTrainingOptions,
                    interruptedBatchTraining.Token,
                    new CombatPolicyValueTrainingSession
                    {
                        Progress = progress => batchProgress.Add(progress),
                        Checkpoint = checkpoint =>
                        {
                            capturedBatchCheckpoint = checkpoint;
                            if (checkpoint.CompletedEpochs >= 8)
                            {
                                interruptedBatchTraining.Cancel();
                            }
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                interrupted = true;
            }
            Assert(interrupted
                   && capturedBatchCheckpoint?.CompletedEpochs == 8
                   && CombatPolicyValueBatchTrainer
                          .TrainingCheckpointEpochInterval == 8
                   && capturedBatchCheckpoint.Optimizer?.Step > 0
                   && capturedBatchCheckpoint.Optimizer.FirstMoment.Length
                      == capturedBatchCheckpoint.Optimizer.SecondMoment.Length
                   && batchProgress.Any(progress =>
                       progress.Stage == "encoding")
                   && batchProgress.Any(progress =>
                       progress.Stage == "training"
                       && progress.CompletedFrames > 0),
                "batch policy-value training reports frame progress and keeps periodic resumable checkpoints");
        }
        var resumedBatchTraining = CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            batchTrainingOptions,
            CancellationToken.None,
            new CombatPolicyValueTrainingSession
            {
                Resume = capturedBatchCheckpoint
            });
        var uninterruptedBatchTraining = CombatPolicyValueTrainer.Train(
            episodes,
            "balanced",
            batchTrainingOptions,
            CancellationToken.None);
        Assert(resumedBatchTraining.Success
               && uninterruptedBatchTraining.Success
               && resumedBatchTraining.CompletedEpochs == 10
               && resumedBatchTraining.Model != null
               && uninterruptedBatchTraining.Model != null
               && resumedBatchTraining.Model.StateWeights.SequenceEqual(
                   uninterruptedBatchTraining.Model.StateWeights)
               && resumedBatchTraining.Model.PolicyWeights.SequenceEqual(
                   uninterruptedBatchTraining.Model.PolicyWeights),
            "resumed deterministic minibatch training produces the uninterrupted model weights");
        var policyValueModel = new ManagedCombatPolicyValueModel(policyValueTraining.Model!);
        Assert(policyValueTraining.Model!.Metrics.TryGetValue(
                   "sparseTrainingDensity",
                   out var sparseTrainingDensity)
               && sparseTrainingDensity is > 0d and < 0.25d
               && policyValueTraining.Model.Metrics.TryGetValue(
                   "sparseTrainingPayloadReduction",
                   out var sparseTrainingPayloadReduction)
               && sparseTrainingPayloadReduction > 0.60d,
            "policy-value training stores encoded state and action features as compact sparse columns");
        var firstEpisodeFrame = episodes[0].Frames[0];
        var policyValueInput = new CombatPolicyValueInput
        {
            StateFeatures = firstEpisodeFrame.StateFeatures,
            Candidates = firstEpisodeFrame.Candidates
                .Where(candidate => candidate.Legal)
                .Select(candidate => new CombatPolicyValueCandidate
                {
                    CandidateId = candidate.CandidateId,
                    SourceId = candidate.SourceId,
                    Features = candidate.Features
                })
                .ToList()
        };
        // Exclude first-use JIT, thread-local workspace growth and cache population
        // from the steady-state direct inference diagnostics below.
        policyValueModel.Evaluate(policyValueInput);
        var directInferenceDiagnosticsStart =
            CombatPolicyValueBatchDiagnostics.Capture();
        var policyValuePrediction = policyValueModel.Evaluate(policyValueInput);
        var firstPolicyCandidateId = policyValueInput.Candidates[0].CandidateId;
        Assert(policyValuePrediction.TryGetPolicyLogit(
                   firstPolicyCandidateId,
                   out var densePolicyLogit)
               && !double.IsNaN(densePolicyLogit)
               && policyValuePrediction.TryGetActionQuantiles(
                   firstPolicyCandidateId,
                   out var denseActionQuantiles)
               && denseActionQuantiles.Count == 16,
            "managed inference exposes allocation-light dense policy and quantile views before compatibility dictionaries are materialized");
        Assert(policyValuePrediction.PolicyLogits.Count
               == firstEpisodeFrame.Candidates.Count(candidate => candidate.Legal)
               && policyValuePrediction.ActionReturnQuantiles.Count
                  == policyValuePrediction.PolicyLogits.Count
               && policyValuePrediction.ActionReturnQuantiles.Values.All(values =>
                   values.Count == 16
                   && values.All(value => value is >= -1d and <= 1d))
               && policyValuePrediction.WinProbability is >= 0d and <= 1d
               && policyValuePrediction.DeathProbability is >= 0d and <= 1d,
            "managed policy-value inference returns masked action logits and calibrated probability ranges");
        var trainedQuantileHeadReady = policyValueTraining.Model!.ActionQuantileHeadReady;
        policyValueTraining.Model.ActionQuantileHeadReady = false;
        var unreadyQuantilePrediction =
            new ManagedCombatPolicyValueModel(policyValueTraining.Model)
                .Evaluate(policyValueInput);
        policyValueTraining.Model.ActionQuantileHeadReady = trainedQuantileHeadReady;
        Assert(trainedQuantileHeadReady
               && unreadyQuantilePrediction.ActionReturnQuantiles.Count == 0,
            "managed inference withholds randomly initialized action quantiles until supervised labels have trained and validated the head");
        var batchPolicyPredictions = policyValueModel.EvaluateBatch(
            new[] { policyValueInput, policyValueInput });
        var directInferenceDiagnostics = CombatPolicyValueBatchDiagnostics
            .Capture()
            .DeltaFrom(directInferenceDiagnosticsStart);
        Assert(batchPolicyPredictions.Count == 2
               && batchPolicyPredictions.All(prediction =>
                   Math.Abs(
                       prediction.ExpectedReturn
                       - policyValuePrediction.ExpectedReturn) < 0.000000001d
                   && Math.Abs(
                       prediction.WinProbability
                       - policyValuePrediction.WinProbability) < 0.000000001d
                   && prediction.PolicyLogits.Count
                      == policyValuePrediction.PolicyLogits.Count
                   && prediction.ActionReturnQuantiles.All(pair =>
                       pair.Value.Zip(
                               policyValuePrediction.ActionReturnQuantiles[pair.Key],
                               (left, right) => Math.Abs(left - right))
                           .All(delta => delta < 0.000000001d))
                   && prediction.PolicyLogits.All(pair =>
                       Math.Abs(
                           pair.Value
                           - policyValuePrediction.PolicyLogits[pair.Key])
                       < 0.000000001d)),
            "managed policy-value batch inference evaluates a shared state/action matrix with scalar-equivalent outputs");
        Assert(directInferenceDiagnostics.Requests == 4
               && directInferenceDiagnostics.DirectEvaluations == 3
               && directInferenceDiagnostics.DirectInputs == 4
               && directInferenceDiagnostics.AverageDirectEvaluationMicroseconds >= 0d
               && directInferenceDiagnostics.AverageDirectAllocatedBytes >= 0d,
            "direct managed inference contributes request counts and latency diagnostics without requiring the batching wrapper");
        Assert(directInferenceDiagnostics.SparseInputs >= 4
               && directInferenceDiagnostics.AverageSparseFeatureCount > 0d
               && directInferenceDiagnostics.SparseFeatureDensity < 0.25d
               && directInferenceDiagnostics.WeightMultiplicationReduction > 0.75d,
            "managed inference traverses only populated feature columns and reports the avoided dense weight work");
        Console.WriteLine(
            $"Sparse inference mixed-path: {directInferenceDiagnostics.AverageDirectEvaluationMicroseconds:0.0} us/evaluation, "
            + $"{directInferenceDiagnostics.AverageDirectAllocatedBytes:0} B/input, "
            + $"density={directInferenceDiagnostics.SparseFeatureDensity:P1}, "
            + $"multiplications saved={directInferenceDiagnostics.WeightMultiplicationReduction:P1}");
        var steadyInferenceStart = CombatPolicyValueBatchDiagnostics.Capture();
        for (var index = 0; index < 64; index++)
        {
            policyValueModel.Evaluate(policyValueInput);
        }
        var steadyInferenceDiagnostics = CombatPolicyValueBatchDiagnostics
            .Capture()
            .DeltaFrom(steadyInferenceStart);
        Console.WriteLine(
            $"Sparse inference steady-state: {steadyInferenceDiagnostics.AverageDirectEvaluationMicroseconds:0.0} us/evaluation, "
            + $"{steadyInferenceDiagnostics.AverageDirectAllocatedBytes:0} B/input");
        Assert(steadyInferenceDiagnostics.DirectInputs == 64
               && steadyInferenceDiagnostics.SparseFeatureDensity < 0.25d
               && steadyInferenceDiagnostics.WeightMultiplicationReduction > 0.75d,
            "steady-state sparse inference keeps the warmed model on the compact execution path");
        Assert(policyValueModel.ActionTowerCacheHits
               >= policyValueInput.Candidates.Count * 2,
            "managed inference reuses immutable action-tower embeddings across scalar and batch evaluations");
        var encodingBuffer = new double[Math.Max(
            policyValueTraining.Model!.StateDimensions,
            policyValueTraining.Model.ActionDimensions)];
        CombatPolicyValueEncoding.EncodeStateInto(
            policyValueInput.StateFeatures,
            encodingBuffer,
            policyValueTraining.Model.StateDimensions,
            policyValueTraining.Model.FeatureEncodingMode);
        CombatPolicyValueEncoding.EncodeCandidateInto(
            policyValueInput.Candidates[0],
            encodingBuffer,
            policyValueTraining.Model.ActionDimensions,
            policyValueTraining.Model.FeatureEncodingMode);
        var encodingAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 512; index++)
        {
            CombatPolicyValueEncoding.EncodeStateInto(
                policyValueInput.StateFeatures,
                encodingBuffer,
                policyValueTraining.Model.StateDimensions,
                policyValueTraining.Model.FeatureEncodingMode);
            CombatPolicyValueEncoding.EncodeCandidateInto(
                policyValueInput.Candidates[index % policyValueInput.Candidates.Count],
                encodingBuffer,
                policyValueTraining.Model.ActionDimensions,
                policyValueTraining.Model.FeatureEncodingMode);
        }
        var encodingAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - encodingAllocationBefore;
        Console.WriteLine(
            $"Encoding hot-path allocation: {encodingAllocatedBytes:N0} bytes / 512 state+action pairs");
        Assert(encodingAllocatedBytes < 64 * 1024,
            "policy-value encoding keeps steady-state hot-path allocation bounded");
        var reusablePolicyInput = new CombatPolicyValueInput();
        var reusableState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                DefinitionId = "test-player",
                CurrentHp = 41,
                MaxHp = 60
            },
            Enemies = new List<CombatUnitObservation>
            {
                new()
                {
                    DefinitionId = "test-enemy",
                    CurrentHp = 27,
                    MaxHp = 40
                }
            },
            CurrentPower = 2,
            MaxPower = 3,
            HandCount = 4,
            ExpectedIncomingDamage = 8,
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["turn"] = 3,
                ["uncertainty"] = 0.25d
            }
        };
        var reusableCandidates = new List<CombatCandidateEvaluation>
        {
            new()
            {
                Legal = true,
                RuleScore = 1.5d,
                BaseRuleScore = 1.25d,
                PlanScore = 0.75d,
                Action = new CombatActionObservation
                {
                    CandidateId = "reusable-card",
                    SourceId = "test-card",
                    Kind = CombatActionKind.PlayCard,
                    Cost = 1,
                    Features = new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["cardDamage"] = 9d
                    },
                    Semantics = new CombatActionSemantics
                    {
                        Damage = 9d
                    }
                }
            }
        };
        var allocatingPolicyInput = CombatPolicyValueEncoding.BuildInput(
            reusableState,
            reusableCandidates);
        CombatPolicyValueEncoding.BuildInputInto(
            reusablePolicyInput,
            reusableState,
            reusableCandidates);
        Assert(allocatingPolicyInput.StateFeatures.OrderBy(pair => pair.Key)
                   .SequenceEqual(reusablePolicyInput.StateFeatures.OrderBy(pair => pair.Key))
               && allocatingPolicyInput.Candidates.Count
                  == reusablePolicyInput.Candidates.Count
               && allocatingPolicyInput.Candidates[0].CandidateId
                  == reusablePolicyInput.Candidates[0].CandidateId
               && allocatingPolicyInput.Candidates[0].ActionKind == "PlayCard"
               && allocatingPolicyInput.Candidates[0].Features.OrderBy(pair => pair.Key)
                   .SequenceEqual(
                       reusablePolicyInput.Candidates[0].Features.OrderBy(pair => pair.Key)),
            "reusable policy-value input construction preserves allocating encoder semantics");
        var reusableStateFeatures = reusablePolicyInput.StateFeatures;
        var reusableCandidateFeatures = reusablePolicyInput.Candidates[0].Features;
        CombatPolicyValueEncoding.BuildInputInto(
            reusablePolicyInput,
            reusableState,
            reusableCandidates);
        Assert(ReferenceEquals(reusableStateFeatures, reusablePolicyInput.StateFeatures)
               && ReferenceEquals(
                   reusableCandidateFeatures,
                   reusablePolicyInput.Candidates[0].Features),
            "reusable policy-value input construction keeps dictionaries across decisions");
        Assert(typeof(CombatLeafEvaluation).IsValueType,
            "leaf inference encoding avoids per-call dictionaries and leaf result objects");
        var concurrentBatchModel = new ConcurrentBatchedCombatPolicyValueModel(
            policyValueModel,
            4,
            TimeSpan.FromMilliseconds(50));
        var concurrentDiagnosticsBefore =
            CombatPolicyValueBatchDiagnostics.Capture();
        var concurrentPredictions = new CombatPolicyValuePrediction?[4];
        var concurrentErrors = new Exception?[4];
        using (var concurrentBarrier = new Barrier(4))
        {
            var inferenceThreads = Enumerable.Range(0, 4)
                .Select(index => new Thread(() =>
                {
                    try
                    {
                        concurrentBarrier.SignalAndWait();
                        concurrentPredictions[index] =
                            concurrentBatchModel.Evaluate(policyValueInput);
                    }
                    catch (Exception exception)
                    {
                        concurrentErrors[index] = exception;
                    }
                }))
                .ToArray();
            foreach (var thread in inferenceThreads)
            {
                thread.Start();
            }
            foreach (var thread in inferenceThreads)
            {
                thread.Join();
            }
        }
        var concurrentDiagnostics = CombatPolicyValueBatchDiagnostics.Capture()
            .DeltaFrom(concurrentDiagnosticsBefore);
        Assert(concurrentErrors.All(error => error == null)
               && concurrentPredictions.All(prediction =>
                   prediction != null
                   && Math.Abs(
                       prediction.ExpectedReturn
                       - policyValuePrediction.ExpectedReturn) < 0.000000001d)
                && concurrentBatchModel.BatchedInputCount == 4
                && concurrentBatchModel.BatchEvaluationCount == 1
                && concurrentDiagnostics.Requests == 4
                && concurrentDiagnostics.BatchedInputs == 4,
            "parallel campaign inference coalesces synchronous calls into one true model batch");
        var shardedBatchModel = new ShardedBatchedCombatPolicyValueModel(
            policyValueModel,
            laneCount: 2,
            maximumBatchSizePerLane: 2,
            coalescingWindow: TimeSpan.FromMilliseconds(20));
        var shardedPredictions = new CombatPolicyValuePrediction?[8];
        var shardedErrors = new Exception?[8];
        using (var shardedBarrier = new Barrier(8))
        {
            var shardedThreads = Enumerable.Range(0, 8)
                .Select(index => new Thread(() =>
                {
                    try
                    {
                        shardedBarrier.SignalAndWait();
                        shardedPredictions[index] =
                            shardedBatchModel.Evaluate(policyValueInput);
                    }
                    catch (Exception exception)
                    {
                        shardedErrors[index] = exception;
                    }
                }))
                .ToArray();
            foreach (var thread in shardedThreads)
            {
                thread.Start();
            }
            foreach (var thread in shardedThreads)
            {
                thread.Join();
            }
        }
        Assert(shardedBatchModel.LaneCount == 2
               && shardedErrors.All(error => error == null)
               && shardedPredictions.All(prediction =>
                   prediction != null
                   && Math.Abs(
                       prediction.ExpectedReturn
                       - policyValuePrediction.ExpectedReturn) < 0.000000001d)
                && shardedBatchModel.BatchedInputCount == 8
                && shardedBatchModel.BatchEvaluationCount is >= 2 and <= 8
                && shardedBatchModel.CaptureLaneBatchEvaluationCounts()
                    .All(count => count > 0),
            "high campaign parallelism balances stable worker lanes without changing predictions");
        var adaptiveBatchModel = new ConcurrentBatchedCombatPolicyValueModel(
            NullCombatPolicyValueModel.Instance,
            maximumBatchSize: 4,
            coalescingWindow: TimeSpan.Zero);
        var adaptiveDiagnosticsBefore = CombatPolicyValueBatchDiagnostics.Capture();
        for (var index = 0; index < 2050; index++)
        {
            _ = adaptiveBatchModel.Evaluate(policyValueInput);
        }
        var adaptiveDiagnostics = CombatPolicyValueBatchDiagnostics.Capture()
            .DeltaFrom(adaptiveDiagnosticsBefore);
        Assert(adaptiveBatchModel.AdaptiveFallbackActive
               && adaptiveDiagnostics.AdaptiveFallbackActivations == 1
               && adaptiveDiagnostics.DirectFallbackRequests > 0,
            "persistently empty inference batches switch to direct execution automatically");
        var evolution = new CombatPolicyEvolutionRunner().Run(
            new CombatPolicyEvolutionRequest
            {
                DecisionProfile = "balanced",
                Iterations = 1,
                TrainingEpisodesPerIteration = 8,
                ArenaEpisodesPerIteration = 2,
                SeedStart = 500,
                Profile = episodeProfile,
                Training = new CombatPolicyValueTrainingOptions
                {
                    Epochs = 5,
                    MinimumEpisodes = 4,
                    HiddenDimensions = 16,
                    RandomSeed = 31
                },
                Scenarios =
                {
                    BuildSimulationScenario(seed: 500, CombatSimulationTraceLevel.Summary)
                }
            },
            simulationRules.Ruleset);
        Assert(evolution.Iterations.Count == 1
               && evolution.Replay.Count == 8
               && evolution.Iterations[0].InvalidCandidateBattles == 0,
            "automatic policy evolution generates episodes, trains a challenger, and runs a paired arena");

        return new CombatAiTrainingTestContext(
            simulationContext,
            episodes,
            policyValueTraining,
            policyValueModel,
            reusableState,
            reusableCandidates);
    }
}
