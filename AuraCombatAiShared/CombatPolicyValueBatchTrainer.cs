using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AuraCombatAi.Shared;

internal static class CombatPolicyValueBatchTrainer
{
    public static CombatPolicyValueTrainingResult Train(
        IEnumerable<CombatEpisode> source,
        string decisionProfile,
        CombatPolicyValueTrainingOptions? trainingOptions,
        CancellationToken cancellationToken,
        CombatPolicyValueTrainingSession? session)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        var options = (trainingOptions ?? new CombatPolicyValueTrainingOptions())
            .Normalized();
        var profile = NormalizeProfile(decisionProfile);
        var episodes = (source ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode != null
                              && episode.ModelProtocol
                              == CombatPolicyValueProtocol.EpisodeProtocol
                              && episode.FeatureSchemaVersion
                              == CombatPolicyValueProtocol.FeatureSchemaVersion
                              && (episode.Campaign?.IntegrityValid ?? true)
                              && (!options.RequireAuthoritativeEpisodes
                                  || episode.Authoritative)
                              && string.Equals(
                                  NormalizeProfile(episode.DecisionProfile),
                                  profile,
                                  StringComparison.Ordinal))
            .OrderBy(StableRunKey, StringComparer.Ordinal)
            .ThenBy(episode => episode.JourneyBattleIndex)
            .ThenBy(episode => episode.Seed)
            .ThenBy(episode => episode.ScenarioId, StringComparer.Ordinal)
            .ThenBy(episode => episode.EpisodeId, StringComparer.Ordinal)
            .ToList();
        if (episodes.Count > options.ReplayEpisodeLimit)
        {
            episodes = episodes
                .Skip(episodes.Count - options.ReplayEpisodeLimit)
                .ToList();
        }
        var result = new CombatPolicyValueTrainingResult
        {
            EpisodeCount = episodes.Count,
            FrameCount = episodes.Sum(episode => Math.Min(
                episode.Frames?.Count ?? 0,
                options.MaximumFramesPerEpisode)),
            DroppedFramesByEpisodeCap = episodes.Sum(episode => Math.Max(
                0,
                (episode.Frames?.Count ?? 0)
                - options.MaximumFramesPerEpisode))
        };
        if (episodes.Count < options.MinimumEpisodes)
        {
            result.Message = (options.RequireAuthoritativeEpisodes
                                 ? "完整权威战斗轨迹不足：当前 "
                                 : "完整投影战斗轨迹不足：当前 ")
                             + episodes.Count
                             + "，最低要求 "
                             + options.MinimumEpisodes;
            return result;
        }

        session?.Progress?.Invoke(new CombatPolicyValueTrainingProgress
        {
            Stage = "encoding",
            TotalEpochs = options.Epochs,
            TotalFrames = result.FrameCount
        });
        var runKeys = episodes
            .Select(StableRunKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var validationRunKeys = new HashSet<string>(StringComparer.Ordinal);
        var testRunKeys = new HashSet<string>(StringComparer.Ordinal);
        if (episodes.Count >= 10 && runKeys.Count >= 3)
        {
            var splitKeys = runKeys
                .OrderBy(StableSplitHash)
                .ThenBy(key => key, StringComparer.Ordinal)
                .ToList();
            var testCount = Math.Max(
                splitKeys.Count >= 64
                    ? options.MinimumTestRunGroups
                    : 1,
                splitKeys.Count / 10);
            var validationCount = Math.Max(
                splitKeys.Count >= 64
                    ? options.MinimumValidationRunGroups
                    : 1,
                splitKeys.Count / 10);
            testCount = Math.Min(testCount, splitKeys.Count - 2);
            validationCount = Math.Min(
                validationCount,
                splitKeys.Count - testCount - 1);
            testRunKeys.UnionWith(splitKeys.Take(testCount));
            validationRunKeys.UnionWith(
                splitKeys.Skip(testCount).Take(validationCount));
        }
        var trainingEpisodes = episodes
            .Where(episode =>
                !validationRunKeys.Contains(StableRunKey(episode))
                && !testRunKeys.Contains(StableRunKey(episode)))
            .ToList();
        var validationEpisodes = episodes
            .Where(episode =>
                validationRunKeys.Contains(StableRunKey(episode)))
            .ToList();
        var testEpisodes = episodes
            .Where(episode =>
                testRunKeys.Contains(StableRunKey(episode)))
            .ToList();
        if (trainingEpisodes.Count == 0)
        {
            trainingEpisodes = episodes;
            validationEpisodes.Clear();
            testEpisodes.Clear();
        }
        var trainingSplitHash = SplitIdentity(
            trainingEpisodes.Select(StableRunKey));
        var validationSplitHash = SplitIdentity(
            (validationEpisodes.Count == 0
                    ? trainingEpisodes
                    : validationEpisodes)
                .Select(StableRunKey));
        result.DroppedPolicyIntegrityFrames = trainingEpisodes.Sum(episode =>
            SelectEpisodeFrames(episode, options.MaximumFramesPerEpisode)
                .Count(frame => !PolicyIntegrityValidForTraining(frame)));
        var trainingFrames = Encode(
            trainingEpisodes,
            options,
            cancellationToken);
        trainingFrames = CapUnsafeEndTurnFrames(
            trainingFrames,
            options.MaximumUnsafeEndTurnFrameShare,
            out var droppedUnsafeEndTurnFrames);
        var validationFrames = Encode(
            validationEpisodes.Count == 0
                ? trainingEpisodes
                : validationEpisodes,
            options,
            cancellationToken);
        var testFrames = Encode(
            testEpisodes,
            options,
            cancellationToken);
        if (trainingFrames.Length == 0)
        {
            result.Message = "完整战斗轨迹没有可训练的合法决策帧";
            return result;
        }
        result.TrainingFrameCount = trainingFrames.Length;
        result.DroppedUnsafeEndTurnFrames =
            droppedUnsafeEndTurnFrames;
        result.EndTurnDecisionFrames =
            trainingFrames.Count(frame => frame.EndTurnDecision);
        result.UnsafeEndTurnFrames =
            trainingFrames.Count(frame => frame.UnsafeEndTurn);
        result.MeanPolicyTargetMaximum = trainingFrames.Average(frame =>
            frame.PolicyTargets.Length == 0
                ? 0d
                : frame.PolicyTargets.Max());
        if (options.EnableFrameStratification)
        {
            ApplyFrameStratumWeights(
                trainingFrames,
                options.MaximumFrameStratumWeight);
            result.FrameStratificationProtocol =
                CombatPolicyValueFrameStratificationProtocol.Version;
            result.FrameStrata = trainingFrames
                .GroupBy(frame => frame.Stratum, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal);
            result.MinimumFrameWeight =
                trainingFrames.Min(frame => frame.SampleWeight);
            result.MaximumFrameWeight =
                trainingFrames.Max(frame => frame.SampleWeight);
        }

        var resume = session?.Resume;
        var model = Compatible(resume?.Model, profile, options)
            ? Clone(resume!.Model!)
            : Initialize(profile, options);
        var optimizer = CompatibleOptimizer(resume?.Optimizer, model)
            ? CloneOptimizer(resume!.Optimizer!)
            : NewOptimizer(model);
        var bestModel = Compatible(resume?.BestModel, profile, options)
            ? Clone(resume!.BestModel!)
            : Clone(model);
        var startEpoch = Math.Max(
            0,
            Math.Min(options.Epochs, resume?.CompletedEpochs ?? 0));
        if (startEpoch == 0 && resume?.Model != null)
        {
            model.ModelId =
                "aura-combat-policy-value-" + DateTime.UtcNow.Ticks;
            model.CreatedUtc = DateTime.UtcNow;
        }
        var resumedBestLoss = resume?.BestValidationLoss ?? double.MaxValue;
        var bestLoss = Finite(resumedBestLoss)
            ? resumedBestLoss
            : double.MaxValue;
        var bestEpoch = Math.Max(0, resume?.BestEpoch ?? 0);
        var staleEpochs = Math.Max(0, resume?.StaleEpochs ?? 0);
        var topModels = (resume?.TopModels
                         ?? new List<CombatPolicyValueModelCandidate>())
            .Where(item => item?.Model != null
                           && Compatible(item.Model, profile, options))
            .Select(item => new CombatPolicyValueModelCandidate
            {
                Epoch = item.Epoch,
                ValidationLoss = item.ValidationLoss,
                TrainingMetrics = CloneMetricSnapshot(item.TrainingMetrics),
                ValidationMetrics =
                    CloneMetricSnapshot(item.ValidationMetrics),
                TestMetrics = CloneMetricSnapshot(item.TestMetrics),
                Model = Clone(item.Model)
            })
            .OrderBy(item => item.ValidationLoss)
            .ThenBy(item => item.Epoch)
            .Take(options.RetainedModelCandidates)
            .ToList();
        var epochHistory = (resume?.EpochHistory
                            ?? new List<CombatPolicyValueEpochMetrics>())
            .Select(CloneEpochMetrics)
            .Where(item => item.Epoch <= startEpoch)
            .OrderBy(item => item.Epoch)
            .ThenBy(item => item.Calibrated)
            .ToList();
        var order = Enumerable.Range(0, trainingFrames.Length).ToArray();
        var batchCapacity = Math.Min(options.BatchSize, trainingFrames.Length);
        var gradientWorkerCapacity = Math.Min(
            batchCapacity,
            options.GradientShardCount);
        var gradients = Enumerable.Range(0, gradientWorkerCapacity)
            .Select(_ => new ModelGradient(model))
            .ToArray();
        var scratchGradients = Enumerable.Range(0, gradientWorkerCapacity)
            .Select(_ => new ModelGradient(model))
            .ToArray();
        var gradientWorkspaces = Enumerable
            .Range(0, gradientWorkerCapacity)
            .Select(_ => new ModelWorkspace(model.HiddenDimensions))
            .ToArray();
        var aggregateGradient = new double[ParameterCount(model)];
        var gradientParallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism =
                options.MaximumDegreeOfParallelism
        };
        var stoppedEarly = false;
        var completedEpochsActual = startEpoch;
        var gradientClipCount = 0;
        var maximumGradientNorm = 0d;
        var lastBatchProgressMilliseconds = -1000L;
        for (var epoch = startEpoch; epoch < options.Epochs; epoch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var onlineTrainingFrameMetrics =
                new FrameMetrics[trainingFrames.Length];
            var epochGradientNorm = 0d;
            var epochGradientClipCount = 0;
            if (options.EnableFrameStratification)
            {
                order = BuildStratifiedOrder(
                    trainingFrames,
                    options.RandomSeed,
                    epoch);
            }
            else
            {
                ResetOrder(order);
                Shuffle(order, options.RandomSeed, epoch);
            }
            var rate = options.LearningRate / Math.Sqrt(1d + epoch * 0.05d);
            for (var batchStart = 0;
                 batchStart < order.Length;
                 batchStart += options.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(options.BatchSize, order.Length - batchStart);
                const int minimumFramesPerGradientWorker = 2;
                var usefulWorkerCount = Math.Max(
                    1,
                    (count + minimumFramesPerGradientWorker - 1)
                    / minimumFramesPerGradientWorker);
                var workerCount = Math.Min(
                    gradientWorkerCapacity,
                    usefulWorkerCount);
                Parallel.For(
                    0,
                    workerCount,
                    gradientParallelOptions,
                    worker =>
                    {
                        var aggregate = gradients[worker];
                        var scratch = scratchGradients[worker];
                        aggregate.Clear();
                        for (var offset = worker;
                             offset < count;
                             offset += workerCount)
                        {
                            var frameIndex = order[batchStart + offset];
                            var frame = trainingFrames[frameIndex];
                            scratch.Clear();
                            onlineTrainingFrameMetrics[frameIndex] =
                                AccumulateGradient(
                                    model,
                                    frame,
                                    scratch,
                                    gradientWorkspaces[worker]);
                            aggregate.AddScaled(
                                scratch,
                                frame.SampleWeight);
                        }
                    });
                var update = ApplyBatch(
                    model,
                    optimizer,
                    gradients,
                    workerCount,
                    count,
                    rate,
                    options.L2,
                    aggregateGradient);
                maximumGradientNorm = Math.Max(
                    maximumGradientNorm,
                    update.GradientNorm);
                gradientClipCount += update.Clipped ? 1 : 0;
                epochGradientNorm = Math.Max(
                    epochGradientNorm,
                    update.GradientNorm);
                epochGradientClipCount += update.Clipped ? 1 : 0;
                var nowMilliseconds = clock.ElapsedMilliseconds;
                if (nowMilliseconds - lastBatchProgressMilliseconds >= 500L)
                {
                    lastBatchProgressMilliseconds = nowMilliseconds;
                    var completedFrames = epoch * trainingFrames.Length
                                          + Math.Min(
                                              trainingFrames.Length,
                                              batchStart + count);
                    var frameRate = completedFrames <= 0
                        ? 0d
                        : completedFrames
                          / Math.Max(0.001d, clock.Elapsed.TotalSeconds);
                    session?.Progress?.Invoke(
                        new CombatPolicyValueTrainingProgress
                        {
                            Stage = "training",
                            Epoch = epoch + 1,
                            TotalEpochs = options.Epochs,
                            CompletedFrames = completedFrames,
                            TotalFrames =
                                options.Epochs * trainingFrames.Length,
                            EstimatedRemainingSeconds = frameRate <= 0d
                                ? 0d
                                : Math.Max(
                                      0,
                                      options.Epochs
                                      * trainingFrames.Length
                                      - completedFrames)
                                  / frameRate,
                            BestValidationLoss = bestLoss,
                            BestEpoch = bestEpoch,
                            StaleEpochs = staleEpochs
                        });
                }
            }

            var validation = Evaluate(
                model,
                validationFrames,
                options.MaximumDegreeOfParallelism,
                cancellationToken);
            var training = AggregateMetrics(
                trainingFrames,
                onlineTrainingFrameMetrics);
            var validationLoss = CompositeValidationLoss(validation);
            var completedEpochs = epoch + 1;
            completedEpochsActual = completedEpochs;
            var improved = bestLoss - validationLoss
                           > options.EarlyStoppingMinimumDelta;
            if (improved)
            {
                bestLoss = validationLoss;
                bestEpoch = completedEpochs;
                staleEpochs = 0;
                bestModel = Clone(model);
            }
            else
            {
                staleEpochs++;
            }
            topModels.Add(new CombatPolicyValueModelCandidate
            {
                Epoch = completedEpochs,
                ValidationLoss = validationLoss,
                TrainingMetrics = Snapshot(training, trainingFrames.Length),
                ValidationMetrics =
                    Snapshot(validation, validationFrames.Length),
                Model = Clone(model)
            });
            topModels = topModels
                .OrderBy(item => item.ValidationLoss)
                .ThenBy(item => item.Epoch)
                .Take(options.RetainedModelCandidates)
                .ToList();
            var epochRate = completedEpochs <= startEpoch
                ? 0d
                : (completedEpochs - startEpoch)
                  / Math.Max(0.001d, clock.Elapsed.TotalSeconds);
            var epochMetrics = EpochMetrics(
                completedEpochs,
                calibrated: false,
                trainingFrames.Length,
                training,
                validationFrames.Length,
                validation);
            epochMetrics.EventKind = "epoch";
            epochMetrics.TrainingMeasurement = "online-minibatch";
            epochMetrics.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            epochMetrics.LearningRate = rate;
            epochMetrics.GradientNorm = epochGradientNorm;
            epochMetrics.GradientClipCount = epochGradientClipCount;
            epochMetrics.Improved = improved;
            epochMetrics.BestEpoch = bestEpoch;
            epochMetrics.BestValidationLoss = bestLoss;
            epochMetrics.StaleEpochs = staleEpochs;
            epochMetrics.TrainingSplitHash = trainingSplitHash;
            epochMetrics.ValidationSplitHash = validationSplitHash;
            var shouldStop = completedEpochs >= options.MinimumEpochs
                             && staleEpochs
                             >= options.EarlyStoppingPatience;
            epochMetrics.EarlyStopped = shouldStop;
            var progress = new CombatPolicyValueTrainingProgress
            {
                Stage = "training",
                Epoch = completedEpochs,
                TotalEpochs = options.Epochs,
                CompletedFrames = completedEpochs * trainingFrames.Length,
                TotalFrames = options.Epochs * trainingFrames.Length,
                EpochsPerSecond = epochRate,
                EstimatedRemainingSeconds = epochRate <= 0d
                    ? 0d
                    : (options.Epochs - completedEpochs) / epochRate,
                ValidationLoss = validationLoss,
                BestValidationLoss = bestLoss,
                BestEpoch = bestEpoch,
                StaleEpochs = staleEpochs,
                Metrics = epochMetrics
            };
            epochHistory.RemoveAll(item =>
                item.Epoch == completedEpochs && !item.Calibrated);
            epochHistory.Add(CloneEpochMetrics(progress.Metrics));
            session?.Progress?.Invoke(progress);
            session?.EpochCompleted?.Invoke(CloneEpochMetrics(epochMetrics));
            var shouldCheckpoint = shouldStop
                                   || completedEpochs == options.Epochs
                                   || completedEpochs % 4 == 0;
            if (shouldCheckpoint)
            {
                session?.Checkpoint?.Invoke(
                    new CombatPolicyValueTrainingResumeState
                    {
                        CompletedEpochs = completedEpochs,
                        Model = Clone(model),
                        BestModel = Clone(bestModel),
                        BestValidationLoss = bestLoss,
                        BestEpoch = bestEpoch,
                        StaleEpochs = staleEpochs,
                        Optimizer = CloneOptimizer(optimizer),
                        TopModels = CloneCandidates(topModels),
                        EpochHistory = epochHistory
                            .Select(CloneEpochMetrics)
                            .ToList()
                    });
            }
            if (shouldStop)
            {
                stoppedEarly = true;
                progress.EarlyStopped = true;
                progress.Stage = "early-stopped";
                session?.Progress?.Invoke(progress);
                break;
            }
        }

        model = Clone(bestModel);
        model.PolicyTemperature = CalibratePolicyTemperature(
            model,
            validationFrames,
            options.MaximumDegreeOfParallelism,
            cancellationToken);
        var trainingMetrics = Evaluate(
            model,
            trainingFrames,
            options.MaximumDegreeOfParallelism,
            cancellationToken);
        var validationMetrics = Evaluate(
            model,
            validationFrames,
            options.MaximumDegreeOfParallelism,
            cancellationToken);
        var testMetrics = testFrames.Length == 0
            ? new Metrics()
            : Evaluate(
                model,
                testFrames,
                options.MaximumDegreeOfParallelism,
                cancellationToken);
        var calibratedTrainingLoss = CompositeValidationLoss(trainingMetrics);
        var calibratedValidationLoss =
            CompositeValidationLoss(validationMetrics);
        bestLoss = Math.Min(bestLoss, calibratedValidationLoss);
        var calibratedMetrics = EpochMetrics(
            bestEpoch,
            calibrated: true,
            trainingFrames.Length,
            trainingMetrics,
            validationFrames.Length,
            validationMetrics);
        calibratedMetrics.EventKind = "calibrated";
        calibratedMetrics.TrainingMeasurement = "full-evaluation";
        calibratedMetrics.ElapsedSeconds = clock.Elapsed.TotalSeconds;
        calibratedMetrics.BestEpoch = bestEpoch;
        calibratedMetrics.BestValidationLoss = bestLoss;
        calibratedMetrics.StaleEpochs = staleEpochs;
        calibratedMetrics.EarlyStopped = stoppedEarly;
        calibratedMetrics.TrainingSplitHash = trainingSplitHash;
        calibratedMetrics.ValidationSplitHash = validationSplitHash;
        epochHistory.RemoveAll(item =>
            item.Epoch == bestEpoch && item.Calibrated);
        epochHistory.Add(calibratedMetrics);
        session?.EpochCompleted?.Invoke(CloneEpochMetrics(calibratedMetrics));
        var stateCollision = CollisionRate(episodes
            .SelectMany(episode => episode.Frames
                                  ?? new List<CombatEpisodeFrame>())
            .Select(frame => CombatPolicyValueEncoding
                .MeasureStateCollisions(
                    frame.StateFeatures,
                    options.StateDimensions)));
        var actionCollision = CollisionRate(episodes
            .SelectMany(episode => episode.Frames
                                  ?? new List<CombatEpisodeFrame>())
            .SelectMany(frame => frame.Candidates
                                ?? new List<CombatEpisodeCandidate>())
            .Select(candidate => CombatPolicyValueEncoding
                .MeasureCandidateCollisions(
                    new CombatPolicyValueCandidate
                    {
                        CandidateId = candidate.CandidateId,
                        SourceId = candidate.SourceId,
                        Features = candidate.Features
                    },
                    options.ActionDimensions)));
        model.Metrics = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["episodeCount"] = episodes.Count,
            ["frameCount"] = result.FrameCount,
            ["trainingRunCount"] = trainingEpisodes
                .Select(StableRunKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            ["validationRunCount"] = validationEpisodes
                .Select(StableRunKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            ["testRunCount"] = testEpisodes
                .Select(StableRunKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            ["trainingPolicyAccuracy"] = trainingMetrics.PolicyAccuracy,
            ["trainingPolicyCrossEntropy"] =
                trainingMetrics.PolicyCrossEntropy,
            ["trainingCriticalPolicyAccuracy"] =
                trainingMetrics.CriticalPolicyAccuracy,
            ["validationPolicyAccuracy"] = validationMetrics.PolicyAccuracy,
            ["validationPolicyCrossEntropy"] =
                validationMetrics.PolicyCrossEntropy,
            ["validationCriticalPolicyAccuracy"] =
                validationMetrics.CriticalPolicyAccuracy,
            ["trainingValueMae"] = trainingMetrics.ValueMae,
            ["trainingBrier"] = trainingMetrics.Brier,
            ["trainingDeathBrier"] = trainingMetrics.DeathBrier,
            ["trainingHpMae"] = trainingMetrics.HpMae,
            ["trainingTurnHuber"] = trainingMetrics.TurnHuber,
            ["trainingActionQuantilePinball"] =
                trainingMetrics.ActionQuantilePinball,
            ["trainingActionQuantileMae"] =
                trainingMetrics.ActionQuantileMae,
            ["trainingCompositeLoss"] = calibratedTrainingLoss,
            ["validationValueMae"] = validationMetrics.ValueMae,
            ["validationBrier"] = validationMetrics.Brier,
            ["validationDeathBrier"] = validationMetrics.DeathBrier,
            ["validationHpMae"] = validationMetrics.HpMae,
            ["validationTurnHuber"] = validationMetrics.TurnHuber,
            ["validationActionQuantilePinball"] =
                validationMetrics.ActionQuantilePinball,
            ["validationActionQuantileMae"] =
                validationMetrics.ActionQuantileMae,
            ["trainingActionQuantileLabelCount"] =
                trainingMetrics.ActionQuantileLabelCount,
            ["validationActionQuantileLabelCount"] =
                validationMetrics.ActionQuantileLabelCount,
            ["validationCompositeLoss"] =
                CompositeValidationLoss(validationMetrics),
            ["testCompositeLoss"] = testFrames.Length == 0
                ? 0d
                : CompositeValidationLoss(testMetrics),
            ["validationEpisodeCount"] = validationEpisodes.Count,
            ["testEpisodeCount"] = testEpisodes.Count,
            ["completedEpochs"] = Math.Max(
                startEpoch,
                completedEpochsActual),
            ["bestEpoch"] = bestEpoch,
            ["candidateEpoch"] = bestEpoch,
            ["earlyStopped"] = stoppedEarly ? 1d : 0d,
            ["batchSize"] = options.BatchSize,
            ["gradientShardCount"] = options.GradientShardCount,
            ["trainingParallelism"] = options.MaximumDegreeOfParallelism,
            ["gradientBufferCount"] = gradientWorkerCapacity * 2d,
            ["optimizerAdamW"] = 1d,
            ["optimizerStep"] = optimizer.Step,
            ["gradientClipCount"] = gradientClipCount,
            ["maximumGradientNorm"] = maximumGradientNorm,
            ["stateFeatureCollisionRate"] = stateCollision,
            ["actionFeatureCollisionRate"] = actionCollision,
            ["policyTemperature"] = model.PolicyTemperature,
            ["frameStratumCount"] = result.FrameStrata.Count,
            ["minimumFrameWeight"] = result.MinimumFrameWeight,
            ["maximumFrameWeight"] = result.MaximumFrameWeight,
            ["endTurnDecisionFrames"] = result.EndTurnDecisionFrames,
            ["unsafeEndTurnFrames"] = result.UnsafeEndTurnFrames,
            ["meanPolicyTargetMaximum"] =
                result.MeanPolicyTargetMaximum
        };
        var calibratedCandidates = CalibrateCandidates(
            topModels,
            model,
            bestEpoch,
            trainingFrames,
            validationFrames,
            testFrames,
            options.MaximumDegreeOfParallelism,
            options.RetainedModelCandidates,
            cancellationToken);
        result.Success = true;
        result.Model = model;
        result.CandidateModels = calibratedCandidates;
        result.CompletedEpochs = Math.Max(
            startEpoch,
            completedEpochsActual);
        result.BestEpoch = bestEpoch;
        result.EarlyStopped = stoppedEarly;
        result.ElapsedSeconds = clock.Elapsed.TotalSeconds;
        result.TestLoss = testFrames.Length == 0
            ? 0d
            : CompositeValidationLoss(testMetrics);
        result.TrainingMetrics = Snapshot(
            trainingMetrics,
            trainingFrames.Length);
        result.ValidationMetrics = Snapshot(
            validationMetrics,
            validationFrames.Length);
        result.TestMetrics = Snapshot(testMetrics, testFrames.Length);
        result.EpochHistory = epochHistory
            .OrderBy(item => item.Epoch)
            .ThenBy(item => item.Calibrated)
            .Select(CloneEpochMetrics)
            .ToList();
        result.Message = "已从 "
                         + episodes.Count
                         + " 场完整战斗、"
                         + result.FrameCount
                         + " 个决策训练策略价值网络"
                         + (stoppedEarly
                             ? "；验证集提前停止于第 "
                               + result.CompletedEpochs
                               + " 轮"
                             : "");
        session?.Progress?.Invoke(new CombatPolicyValueTrainingProgress
        {
            Stage = "completed",
            Epoch = result.CompletedEpochs,
            TotalEpochs = options.Epochs,
            CompletedFrames = result.CompletedEpochs * trainingFrames.Length,
            TotalFrames = options.Epochs * trainingFrames.Length,
            ValidationLoss = CompositeValidationLoss(validationMetrics),
            BestValidationLoss = bestLoss,
            BestEpoch = bestEpoch,
            EarlyStopped = stoppedEarly,
            Metrics = CloneEpochMetrics(calibratedMetrics)
        });
        return result;
    }

    private static List<CombatPolicyValueModelCandidate> CalibrateCandidates(
        IEnumerable<CombatPolicyValueModelCandidate> source,
        CombatPolicyValueNetworkDefinition selectedModel,
        int selectedEpoch,
        EncodedFrame[] trainingFrames,
        EncodedFrame[] validationFrames,
        EncodedFrame[] testFrames,
        int parallelism,
        int maximumCandidates,
        CancellationToken cancellationToken)
    {
        var result = new List<CombatPolicyValueModelCandidate>();
        foreach (var candidate in source
                     ?? Array.Empty<CombatPolicyValueModelCandidate>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var calibrated = candidate.Epoch == selectedEpoch
                ? Clone(selectedModel)
                : Clone(candidate.Model);
            if (candidate.Epoch != selectedEpoch)
            {
                calibrated.PolicyTemperature =
                    CalibratePolicyTemperature(
                        calibrated,
                        validationFrames,
                        parallelism,
                        cancellationToken);
            }
            var validation = Evaluate(
                calibrated,
                validationFrames,
                parallelism,
                cancellationToken);
            var training = Evaluate(
                calibrated,
                trainingFrames,
                parallelism,
                cancellationToken);
            var test = testFrames.Length == 0
                ? new Metrics()
                : Evaluate(
                    calibrated,
                    testFrames,
                    parallelism,
                    cancellationToken);
            // Epoch snapshots are captured before the final full-evaluation
            // metric set is assembled. Start from the selected model's full
            // manifest and then overlay epoch-specific values so a tuning
            // winner never loses provenance/training diagnostics merely
            // because it came from a retained non-best epoch.
            var inherited = new Dictionary<string, double>(
                selectedModel.Metrics
                ?? new Dictionary<string, double>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var metric in calibrated.Metrics
                         ?? new Dictionary<string, double>())
            {
                inherited[metric.Key] = metric.Value;
            }
            inherited["validationPolicyAccuracy"] =
                validation.PolicyAccuracy;
            inherited["validationPolicyCrossEntropy"] =
                validation.PolicyCrossEntropy;
            inherited["validationCriticalPolicyAccuracy"] =
                validation.CriticalPolicyAccuracy;
            inherited["validationValueMae"] = validation.ValueMae;
            inherited["validationBrier"] = validation.Brier;
            inherited["validationDeathBrier"] = validation.DeathBrier;
            inherited["validationHpMae"] = validation.HpMae;
            inherited["validationTurnHuber"] = validation.TurnHuber;
            inherited["validationCompositeLoss"] =
                CompositeValidationLoss(validation);
            inherited["testCompositeLoss"] = testFrames.Length == 0
                ? 0d
                : CompositeValidationLoss(test);
            inherited["policyTemperature"] = calibrated.PolicyTemperature;
            inherited["candidateEpoch"] = candidate.Epoch;
            calibrated.Metrics = inherited;
            result.Add(new CombatPolicyValueModelCandidate
            {
                Epoch = candidate.Epoch,
                ValidationLoss = CompositeValidationLoss(validation),
                TrainingMetrics = Snapshot(training, trainingFrames.Length),
                ValidationMetrics =
                    Snapshot(validation, validationFrames.Length),
                TestMetrics = Snapshot(test, testFrames.Length),
                Model = calibrated
            });
        }
        if (result.All(item => item.Epoch != selectedEpoch))
        {
            result.Add(new CombatPolicyValueModelCandidate
            {
                Epoch = selectedEpoch,
                ValidationLoss = selectedModel.Metrics != null
                                 && selectedModel.Metrics.TryGetValue(
                                     "validationCompositeLoss",
                                     out var loss)
                    ? loss
                    : double.MaxValue,
                TrainingMetrics = Snapshot(
                    Evaluate(
                        selectedModel,
                        trainingFrames,
                        parallelism,
                        cancellationToken),
                    trainingFrames.Length),
                ValidationMetrics = Snapshot(
                    Evaluate(
                        selectedModel,
                        validationFrames,
                        parallelism,
                        cancellationToken),
                    validationFrames.Length),
                TestMetrics = Snapshot(
                    testFrames.Length == 0
                        ? new Metrics()
                        : Evaluate(
                            selectedModel,
                            testFrames,
                            parallelism,
                            cancellationToken),
                    testFrames.Length),
                Model = Clone(selectedModel)
            });
        }
        return result
            .OrderBy(item => item.ValidationLoss)
            .ThenBy(item => item.Epoch)
            .Take(Math.Max(1, maximumCandidates))
            .ToList();
    }

    private static string StableRunKey(CombatEpisode episode)
    {
        return string.IsNullOrWhiteSpace(episode.JourneyRunId)
            ? "episode:" + (episode.EpisodeId ?? "")
            : "journey:" + episode.JourneyRunId;
    }

    private static uint StableSplitHash(string key)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in key ?? "")
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private static EncodedFrame[] CapUnsafeEndTurnFrames(
        EncodedFrame[] source,
        double maximumShare,
        out int droppedFrames)
    {
        droppedFrames = 0;
        if (source.Length < 64)
        {
            return source;
        }
        var nonUnsafeCount = source.Count(frame => !frame.UnsafeEndTurn);
        var unsafeCount = source.Length - nonUnsafeCount;
        if (nonUnsafeCount == 0 || unsafeCount == 0)
        {
            return source;
        }
        var maximumUnsafeCount = Math.Max(
            1,
            (int)Math.Floor(
                nonUnsafeCount
                * maximumShare
                / Math.Max(0.000001d, 1d - maximumShare)));
        if (unsafeCount <= maximumUnsafeCount)
        {
            return source;
        }
        var retainedUnsafeIndices = source
            .Select((frame, index) => new { Frame = frame, Index = index })
            .Where(item => item.Frame.UnsafeEndTurn)
            .OrderBy(item => StableSplitHash(
                item.Frame.RunKey
                + "|"
                + item.Frame.Stratum
                + "|"
                + item.Index))
            .ThenBy(item => item.Index)
            .Take(maximumUnsafeCount)
            .Select(item => item.Index)
            .ToHashSet();
        var retained = source
            .Where((frame, index) =>
                !frame.UnsafeEndTurn
                || retainedUnsafeIndices.Contains(index))
            .ToArray();
        droppedFrames = source.Length - retained.Length;
        return retained;
    }

    private static string SplitIdentity(IEnumerable<string> source)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var key in (source ?? Array.Empty<string>())
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                foreach (var character in key)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }
                hash ^= 0xff;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }

    private static double CollisionRate(
        IEnumerable<CombatFeatureCollisionTelemetry> source)
    {
        var featureCount = 0L;
        var collisionCount = 0L;
        foreach (var item in source)
        {
            featureCount += item.FeatureCount;
            collisionCount += item.CollisionCount;
        }
        return featureCount == 0L
            ? 0d
            : (double)collisionCount / featureCount;
    }

    private static EncodedFrame[] Encode(
        IReadOnlyList<CombatEpisode> episodes,
        CombatPolicyValueTrainingOptions options,
        CancellationToken cancellationToken)
    {
        var frames = episodes
            .SelectMany(episode =>
                SelectEpisodeFrames(episode, options.MaximumFramesPerEpisode)
                .Select(frame => new FrameSource
                {
                    Episode = episode,
                    Frame = frame
                }))
            .ToArray();
        var encoded = new EncodedFrame?[frames.Length];
        Parallel.For(
            0,
            frames.Length,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism =
                    options.MaximumDegreeOfParallelism
            },
            index => encoded[index] = EncodeFrame(
                frames[index].Episode,
                frames[index].Frame,
                options));
        return encoded.Where(item => item != null).Select(item => item!).ToArray();
    }

    private static IReadOnlyList<CombatEpisodeFrame> SelectEpisodeFrames(
        CombatEpisode episode,
        int maximumFrames)
    {
        var frames = episode.Frames ?? new List<CombatEpisodeFrame>();
        if (frames.Count <= maximumFrames)
        {
            return frames;
        }
        var selected = new List<CombatEpisodeFrame>(maximumFrames);
        for (var index = 0; index < maximumFrames; index++)
        {
            var sourceIndex = (int)Math.Round(
                index * (frames.Count - 1d) / (maximumFrames - 1d),
                MidpointRounding.AwayFromZero);
            selected.Add(frames[sourceIndex]);
        }
        return selected;
    }

    private static EncodedFrame? EncodeFrame(
        CombatEpisode episode,
        CombatEpisodeFrame frame,
        CombatPolicyValueTrainingOptions options)
    {
        var allCandidates = frame.Candidates
                            ?? new List<CombatEpisodeCandidate>();
        var executedCandidate = allCandidates.FirstOrDefault(candidate =>
            string.Equals(
                candidate.CandidateId,
                frame.ExecutedCandidateId,
                StringComparison.Ordinal));
        if (executedCandidate == null)
        {
            return null;
        }
        var legal = allCandidates
            .Where(candidate => candidate.Legal)
            .ToList();
        if (legal.Count == 0)
        {
            return null;
        }
        var endTurnCandidate = allCandidates.FirstOrDefault(
            IsEndTurnCandidate);
        var dominatedEndTurn = endTurnCandidate != null
                               && Feature(
                                   endTurnCandidate.Features,
                                   CombatTurnFeatureNames.EndTurnDominated) > 0.5d;
        if (!executedCandidate.Legal
            && !(ReferenceEquals(executedCandidate, endTurnCandidate)
                 && dominatedEndTurn))
        {
            return null;
        }
        var policyCandidates = new List<CombatEpisodeCandidate>(legal);
        if (dominatedEndTurn
            && endTurnCandidate != null
            && !policyCandidates.Contains(endTurnCandidate))
        {
            policyCandidates.Add(endTurnCandidate);
        }
        var targets = PolicyTargets(
            policyCandidates,
            frame.ExecutedCandidateId,
            options.PolicyTargetTemperature,
            options.MaximumPolicyTargetProbability);
        if (dominatedEndTurn && endTurnCandidate != null)
        {
            var dominatedIndex = policyCandidates.IndexOf(endTurnCandidate);
            SuppressPolicyTarget(targets, dominatedIndex);
        }
        var orderedVisits = legal
            .Select(candidate => Math.Max(0, candidate.SearchVisits))
            .OrderByDescending(value => value)
            .Take(2)
            .ToArray();
        var riskValues = legal
            .Select(candidate => candidate.SearchDeathRisk)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        var critical = frame.DeathTarget >= 0.5d
                       || riskValues.Length > 1
                          && riskValues.Max() - riskValues.Min() >= 0.20d
                       || orderedVisits.Length > 1
                          && orderedVisits[0] >= Math.Max(
                              4,
                              orderedVisits[1] * 2);
        var executedEndTurn = executedCandidate != null
                              && IsEndTurnCandidate(executedCandidate);
        var hasPlayableAlternative = endTurnCandidate != null
                                     && legal.Any(candidate =>
                                         !IsEndTurnCandidate(candidate));
        var endTurnDecision = hasPlayableAlternative;
        var unusedEnergy = frame.StateFeatures != null
                           && frame.StateFeatures.TryGetValue(
                               "power",
                               out var power)
            && power > 0.5d;
        var unsafeEndTurn = dominatedEndTurn
                            || (executedEndTurn
                                && hasPlayableAlternative
                                && unusedEnergy);
        var endTurnWeight = options.EnableEndTurnSpecialization
                            && endTurnDecision
            ? options.EndTurnFrameWeight * (unsafeEndTurn ? 0.75d : 1d)
            : 1d;
        var baseSampleWeight = Clamp(
            (episode.Campaign?.TrainingWeight ?? 1d)
            * Math.Max(0.1d, frame.TrainingWeight)
            * endTurnWeight,
            0.10d,
            5d);
        return new EncodedFrame
        {
            RunKey = StableRunKey(episode),
            State = CombatPolicyValueEncoding.EncodeState(
                frame.StateFeatures,
                options.StateDimensions,
                options.FeatureEncodingMode),
            Actions = policyCandidates.Select(candidate =>
                    CombatPolicyValueEncoding.EncodeCandidate(
                        new CombatPolicyValueCandidate
                        {
                            CandidateId = candidate.CandidateId,
                            SourceId = candidate.SourceId,
                            Features = candidate.Features
                        },
                        options.ActionDimensions,
                        options.FeatureEncodingMode))
                .ToArray(),
            PolicyTargets = targets,
            ActionQuantileTargets = policyCandidates.Select(candidate =>
                    candidate.SearchVisits
                    >= options.MinimumSearchVisitsForActionQuantiles
                    && candidate.SearchReturnQuantiles != null
                    && candidate.SearchReturnQuantiles.Count >= 4
                        ? ResampleQuantiles(
                            candidate.SearchReturnQuantiles,
                            options.ActionQuantileCount)
                        : Array.Empty<double>())
                .ToArray(),
            ActionQuantileLossWeight = options.ActionQuantileLossWeight,
            PolicyTargetIndex = MaximumIndex(targets),
            LongTermReturn = Clamp(frame.LongTermReturn, -1d, 1d),
            WinTarget = Clamp(frame.WinTarget, 0d, 1d),
            DeathTarget = Clamp(frame.DeathTarget, 0d, 1d),
            HpTarget = Clamp(frame.RemainingHpRatioTarget, 0d, 1d),
            TurnsTarget = Math.Max(0d, frame.RemainingTurnsTarget),
            Critical = critical,
            EndTurnDecision = endTurnDecision,
            UnsafeEndTurn = unsafeEndTurn,
            Stratum = FrameStratum(episode, critical)
                      + ":"
                      + (unsafeEndTurn
                          ? "unsafe-end-turn"
                          : endTurnDecision
                              ? "end-turn-decision"
                              : "other-action"),
            BaseSampleWeight = baseSampleWeight,
            SampleWeight = baseSampleWeight
        };
    }

    private static double[] ResampleQuantiles(
        IReadOnlyList<double> source,
        int count)
    {
        var ordered = source
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .Select(value => Clamp(value, -1d, 1d))
            .OrderBy(value => value)
            .ToArray();
        if (ordered.Length == 0)
        {
            return Array.Empty<double>();
        }
        var result = new double[count];
        for (var index = 0; index < count; index++)
        {
            var position = (index + 0.5d) / count * ordered.Length - 0.5d;
            var lower = Math.Max(0, Math.Min(ordered.Length - 1, (int)Math.Floor(position)));
            var upper = Math.Max(0, Math.Min(ordered.Length - 1, lower + 1));
            var fraction = Math.Max(0d, Math.Min(1d, position - Math.Floor(position)));
            result[index] = ordered[lower]
                            + (ordered[upper] - ordered[lower]) * fraction;
        }
        return result;
    }

    private static bool IsEndTurnCandidate(CombatEpisodeCandidate candidate)
    {
        return string.Equals(
                   candidate.SourceId,
                   "simulation:end-turn",
                   StringComparison.OrdinalIgnoreCase)
               || candidate.Features != null
               && candidate.Features.TryGetValue(
                   "actionKindEndTurn",
                   out var value)
               && value > 0.5d;
    }

    internal static bool PolicyIntegrityValidForTraining(
        CombatEpisodeFrame frame)
    {
        var candidates = frame?.Candidates
                         ?? new List<CombatEpisodeCandidate>();
        var executed = candidates.FirstOrDefault(candidate => string.Equals(
            candidate.CandidateId,
            frame?.ExecutedCandidateId,
            StringComparison.Ordinal));
        if (executed == null)
        {
            return false;
        }
        if (executed.Legal)
        {
            return true;
        }
        return IsEndTurnCandidate(executed)
               && Feature(
                   executed.Features,
                   CombatTurnFeatureNames.EndTurnDominated) > 0.5d;
    }

    private static double Feature(
        IReadOnlyDictionary<string, double>? features,
        string key)
    {
        return features != null
               && features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    internal static void SuppressPolicyTarget(
        double[] targets,
        int suppressedIndex)
    {
        if (targets == null
            || suppressedIndex < 0
            || suppressedIndex >= targets.Length)
        {
            return;
        }
        targets[suppressedIndex] = 0d;
        var remaining = targets.Sum();
        if (remaining > 0.000000001d)
        {
            for (var index = 0; index < targets.Length; index++)
            {
                targets[index] = Math.Max(0d, targets[index]) / remaining;
            }
            return;
        }
        var alternatives = Math.Max(0, targets.Length - 1);
        if (alternatives == 0)
        {
            targets[suppressedIndex] = 1d;
            return;
        }
        var uniform = 1d / alternatives;
        for (var index = 0; index < targets.Length; index++)
        {
            targets[index] = index == suppressedIndex ? 0d : uniform;
        }
    }

    internal static string FrameStratum(
        CombatEpisode episode,
        bool critical)
    {
        var difficulty = string.Equals(
            episode.Campaign?.DifficultyId,
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
        var battleIndex = Math.Max(0, episode.JourneyBattleIndex);
        var phase = battleIndex <= 5
            ? "opening"
            : battleIndex <= 20
                ? "middle"
                : battleIndex <= 34
                    ? "late"
                    : "final";
        var outcomeClass =
            episode.Campaign?.OutcomeClass ?? episode.Outcome ?? "";
        var encounterVictory = outcomeClass.IndexOf(
            "encounter-victory",
            StringComparison.OrdinalIgnoreCase) >= 0;
        var outcome = episode.Campaign != null
            ? (episode.Campaign.FinalBossVictory || encounterVictory
                ? "victory"
                : "defeat")
            : (outcomeClass.IndexOf(
                "victory",
                StringComparison.OrdinalIgnoreCase) >= 0
                ? "victory"
                : "defeat");
        return difficulty
               + ":"
               + phase
               + ":"
               + outcome
               + ":"
               + (critical ? "critical" : "regular");
    }

    private static void ApplyFrameStratumWeights(
        IReadOnlyList<EncodedFrame> frames,
        double maximumWeight)
    {
        if (frames.Count == 0)
        {
            return;
        }
        var groups = frames
            .GroupBy(frame => frame.Stratum, StringComparer.Ordinal)
            .ToList();
        var stratumCount = Math.Max(1, groups.Count);
        foreach (var group in groups)
        {
            var raw = Math.Sqrt(
                frames.Count
                / (double)(stratumCount * Math.Max(1, group.Count())));
            var weight = Clamp(
                raw,
                CombatPolicyValueFrameStratificationProtocol.MinimumWeight,
                maximumWeight);
            foreach (var frame in group)
            {
                frame.SampleWeight = weight * frame.BaseSampleWeight;
            }
        }
        var mean = frames.Average(frame => frame.SampleWeight);
        if (mean <= 0d)
        {
            return;
        }
        foreach (var frame in frames)
        {
            frame.SampleWeight = Clamp(
                frame.SampleWeight / mean,
                CombatPolicyValueFrameStratificationProtocol.MinimumWeight,
                maximumWeight);
        }
    }

    private static int[] BuildStratifiedOrder(
        IReadOnlyList<EncodedFrame> frames,
        int seed,
        int epoch)
    {
        var groups = frames
            .Select((frame, index) => new
            {
                frame.Stratum,
                Index = index
            })
            .GroupBy(item => item.Stratum, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Select(item => item.Index).ToArray())
            .ToList();
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            Shuffle(
                groups[groupIndex],
                unchecked(seed ^ groupIndex * 104729),
                epoch);
        }
        var order = new List<int>(frames.Count);
        for (var offset = 0; order.Count < frames.Count; offset++)
        {
            foreach (var group in groups)
            {
                if (offset < group.Length)
                {
                    order.Add(group[offset]);
                }
            }
        }
        return order.ToArray();
    }

    private static FrameMetrics AccumulateGradient(
        CombatPolicyValueNetworkDefinition model,
        EncodedFrame frame,
        ModelGradient gradient,
        ModelWorkspace workspace)
    {
        var hiddenCount = model.HiddenDimensions;
        workspace.Prepare(frame.Actions.Length);
        var stateHidden = workspace.StateHidden;
        DenseTanhInto(
            frame.State,
            model.StateWeights,
            model.StateBias,
            stateHidden,
            hiddenCount);
        var actionHidden = workspace.ActionHidden;
        var logits = workspace.Logits;
        for (var actionIndex = 0;
             actionIndex < frame.Actions.Length;
             actionIndex++)
        {
            DenseTanhInto(
                frame.Actions[actionIndex],
                model.ActionWeights,
                model.ActionBias,
                actionHidden[actionIndex],
                hiddenCount);
            logits[actionIndex] = Interaction(
                stateHidden,
                actionHidden[actionIndex],
                model.PolicyWeights)
                                  + model.PolicyBias;
        }
        var probabilities = workspace.Probabilities;
        SoftmaxInto(logits, frame.Actions.Length, probabilities);
        var stateGradient = workspace.StateGradient;
        Array.Clear(stateGradient, 0, hiddenCount);
        var actionQuantilePinball = 0d;
        var actionQuantileMae = 0d;
        var actionQuantileLabels = 0;
        for (var actionIndex = 0;
             actionIndex < frame.Actions.Length;
             actionIndex++)
        {
            var outputGradient = probabilities[actionIndex]
                                 - frame.PolicyTargets[actionIndex];
            gradient.PolicyBias += outputGradient;
            var actionGradient = workspace.ActionGradient;
            Array.Clear(actionGradient, 0, hiddenCount);
            for (var hidden = 0; hidden < hiddenCount; hidden++)
            {
                var weight = model.PolicyWeights[hidden];
                gradient.PolicyWeights[hidden] += outputGradient
                                                  * stateHidden[hidden]
                                                  * actionHidden[actionIndex][hidden];
                stateGradient[hidden] += outputGradient
                                         * weight
                                         * actionHidden[actionIndex][hidden];
                actionGradient[hidden] += outputGradient
                                          * weight
                                          * stateHidden[hidden];
            }
            var quantileTargets = actionIndex < frame.ActionQuantileTargets.Length
                ? frame.ActionQuantileTargets[actionIndex]
                : Array.Empty<double>();
            for (var quantile = 0;
                 quantile < quantileTargets.Length
                 && quantile < model.ActionQuantileCount;
                 quantile++)
            {
                var weightOffset = quantile * hiddenCount;
                var prediction = Interaction(
                    stateHidden,
                    actionHidden[actionIndex],
                    model.ActionQuantileWeights,
                    weightOffset)
                                 + model.ActionQuantileBias[quantile];
                var error = prediction - quantileTargets[quantile];
                var tau = (quantile + 0.5d) / model.ActionQuantileCount;
                var asymmetry = error >= 0d ? 1d - tau : tau;
                var huberGradient = Clamp(error, -1d, 1d);
                var quantileGradient = frame.ActionQuantileLossWeight
                                       * asymmetry
                                       * huberGradient
                                       / Math.Max(1, quantileTargets.Length);
                gradient.ActionQuantileBias[quantile] += quantileGradient;
                for (var hidden = 0; hidden < hiddenCount; hidden++)
                {
                    var weight = model.ActionQuantileWeights[weightOffset + hidden];
                    gradient.ActionQuantileWeights[weightOffset + hidden] +=
                        quantileGradient
                        * stateHidden[hidden]
                        * actionHidden[actionIndex][hidden];
                    stateGradient[hidden] += quantileGradient
                                             * weight
                                             * actionHidden[actionIndex][hidden];
                    actionGradient[hidden] += quantileGradient
                                              * weight
                                              * stateHidden[hidden];
                }
                var absolute = Math.Abs(error);
                var huber = absolute <= 1d
                    ? 0.5d * absolute * absolute
                    : absolute - 0.5d;
                actionQuantilePinball += asymmetry * huber;
                actionQuantileMae += absolute;
                actionQuantileLabels++;
            }
            BackpropDense(
                frame.Actions[actionIndex],
                actionHidden[actionIndex],
                actionGradient,
                model.ActionWeights,
                gradient.ActionWeights,
                gradient.ActionBias);
        }

        var value = Dot(stateHidden, model.ValueWeights) + model.ValueBias;
        gradient.ValueBias += AccumulateHead(
            stateHidden,
            model.ValueWeights,
            value - frame.LongTermReturn,
            gradient.ValueWeights,
            stateGradient);
        var win = Sigmoid(Dot(stateHidden, model.WinWeights) + model.WinBias);
        gradient.WinBias += AccumulateHead(
            stateHidden,
            model.WinWeights,
            win - frame.WinTarget,
            gradient.WinWeights,
            stateGradient);
        var risk = Sigmoid(Dot(stateHidden, model.RiskWeights) + model.RiskBias);
        gradient.RiskBias += AccumulateHead(
            stateHidden,
            model.RiskWeights,
            risk - frame.DeathTarget,
            gradient.RiskWeights,
            stateGradient);
        var hp = Sigmoid(Dot(stateHidden, model.HpWeights) + model.HpBias);
        gradient.HpBias += AccumulateHead(
            stateHidden,
            model.HpWeights,
            (hp - frame.HpTarget) * hp * (1d - hp),
            gradient.HpWeights,
            stateGradient);
        var turnsRaw = Dot(stateHidden, model.TurnWeights) + model.TurnBias;
        var turns = SoftPlus(turnsRaw);
        gradient.TurnBias += AccumulateHead(
            stateHidden,
            model.TurnWeights,
            (turns - frame.TurnsTarget) * Sigmoid(turnsRaw) * 0.1d,
            gradient.TurnWeights,
            stateGradient);
        BackpropDense(
            frame.State,
            stateHidden,
            stateGradient,
            model.StateWeights,
            gradient.StateWeights,
            gradient.StateBias);
        var bestIndex = 0;
        var bestProbability = double.NegativeInfinity;
        var crossEntropy = 0d;
        for (var index = 0; index < frame.Actions.Length; index++)
        {
            if (probabilities[index] > bestProbability)
            {
                bestProbability = probabilities[index];
                bestIndex = index;
            }
            crossEntropy -= frame.PolicyTargets[index]
                            * Math.Log(Math.Max(
                                0.000000001d,
                                probabilities[index]));
        }
        var clampedValue = Clamp(value, -1d, 1d);
        var turnError = Math.Abs(turns - frame.TurnsTarget);
        return new FrameMetrics
        {
            Correct = bestIndex == frame.PolicyTargetIndex ? 1 : 0,
            ValueError = Math.Abs(
                clampedValue - frame.LongTermReturn),
            Brier = (win - frame.WinTarget) * (win - frame.WinTarget),
            DeathBrier = (risk - frame.DeathTarget)
                         * (risk - frame.DeathTarget),
            HpError = Math.Abs(hp - frame.HpTarget),
            TurnHuber = turnError <= 1d
                ? 0.5d * turnError * turnError
                : turnError - 0.5d,
            PolicyCrossEntropy = crossEntropy,
            ActionQuantilePinball = actionQuantileLabels == 0
                ? 0d
                : actionQuantilePinball / actionQuantileLabels,
            ActionQuantileMae = actionQuantileLabels == 0
                ? 0d
                : actionQuantileMae / actionQuantileLabels,
            ActionQuantileLabelCount = actionQuantileLabels,
            Critical = frame.Critical
        };
    }

    private static double AccumulateHead(
        IReadOnlyList<double> hidden,
        IReadOnlyList<double> weights,
        double outputGradient,
        double[] weightGradient,
        double[] hiddenGradient)
    {
        for (var index = 0; index < hidden.Count; index++)
        {
            weightGradient[index] += outputGradient * hidden[index];
            hiddenGradient[index] += outputGradient * weights[index];
        }
        return outputGradient;
    }

    private static void BackpropDense(
        IReadOnlyList<double> input,
        IReadOnlyList<double> hidden,
        IReadOnlyList<double> hiddenGradient,
        IReadOnlyList<double> weights,
        double[] weightGradient,
        double[] biasGradient)
    {
        for (var output = 0; output < hidden.Count; output++)
        {
            var outputGradient = hiddenGradient[output]
                                 * (1d - hidden[output] * hidden[output]);
            biasGradient[output] += outputGradient;
            var offset = output * input.Count;
            for (var inputIndex = 0;
                 inputIndex < input.Count;
                 inputIndex++)
            {
                weightGradient[offset + inputIndex] +=
                    outputGradient * input[inputIndex];
            }
        }
    }

    private static BatchUpdate ApplyBatch(
        CombatPolicyValueNetworkDefinition model,
        CombatPolicyValueOptimizerState optimizer,
        IReadOnlyList<ModelGradient> gradients,
        int gradientCount,
        int sampleCount,
        double learningRate,
        double l2,
        double[] aggregate)
    {
        var cursor = 0;
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.StateWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.StateBias);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.ActionWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.ActionBias);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.PolicyWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.ActionQuantileWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.ActionQuantileBias);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.ValueWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.WinWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.RiskWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.HpWeights);
        Aggregate(aggregate, ref cursor, gradients, gradientCount, sampleCount,
            gradient => gradient.TurnWeights);
        aggregate[cursor++] = Average(
            gradients, gradientCount, sampleCount,
            gradient => gradient.PolicyBias);
        aggregate[cursor++] = Average(
            gradients, gradientCount, sampleCount,
            gradient => gradient.ValueBias);
        aggregate[cursor++] = Average(
            gradients, gradientCount, sampleCount,
            gradient => gradient.WinBias);
        aggregate[cursor++] = Average(
            gradients, gradientCount, sampleCount,
            gradient => gradient.RiskBias);
        aggregate[cursor++] = Average(
            gradients, gradientCount, sampleCount,
            gradient => gradient.HpBias);
        aggregate[cursor] = Average(
            gradients, gradientCount, sampleCount,
            gradient => gradient.TurnBias);

        var squaredNorm = 0d;
        foreach (var value in aggregate)
        {
            if (!Finite(value))
            {
                throw new InvalidOperationException(
                    "策略价值网络梯度包含非有限值");
            }
            squaredNorm += value * value;
        }
        var norm = Math.Sqrt(squaredNorm);
        var clipScale = norm > 1d ? 1d / norm : 1d;
        if (clipScale < 1d)
        {
            for (var index = 0; index < aggregate.Length; index++)
            {
                aggregate[index] *= clipScale;
            }
        }

        optimizer.Step++;
        cursor = 0;
        ApplyAdamW(model.StateWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        ApplyAdamW(model.StateBias, aggregate, optimizer, ref cursor,
            learningRate, 0d);
        ApplyAdamW(model.ActionWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        ApplyAdamW(model.ActionBias, aggregate, optimizer, ref cursor,
            learningRate, 0d);
        ApplyAdamW(model.PolicyWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        ApplyAdamW(
            model.ActionQuantileWeights,
            aggregate,
            optimizer,
            ref cursor,
            learningRate,
            l2);
        ApplyAdamW(
            model.ActionQuantileBias,
            aggregate,
            optimizer,
            ref cursor,
            learningRate,
            0d);
        ApplyAdamW(model.ValueWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        ApplyAdamW(model.WinWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        ApplyAdamW(model.RiskWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        ApplyAdamW(model.HpWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        ApplyAdamW(model.TurnWeights, aggregate, optimizer, ref cursor,
            learningRate, l2);
        model.PolicyBias = ApplyAdam(
            model.PolicyBias, aggregate[cursor], optimizer, cursor++, learningRate);
        model.ValueBias = ApplyAdam(
            model.ValueBias, aggregate[cursor], optimizer, cursor++, learningRate);
        model.WinBias = ApplyAdam(
            model.WinBias, aggregate[cursor], optimizer, cursor++, learningRate);
        model.RiskBias = ApplyAdam(
            model.RiskBias, aggregate[cursor], optimizer, cursor++, learningRate);
        model.HpBias = ApplyAdam(
            model.HpBias, aggregate[cursor], optimizer, cursor++, learningRate);
        model.TurnBias = ApplyAdam(
            model.TurnBias, aggregate[cursor], optimizer, cursor, learningRate);
        return new BatchUpdate(norm, clipScale < 1d);
    }

    private static void Aggregate(
        double[] target,
        ref int cursor,
        IReadOnlyList<ModelGradient> gradients,
        int gradientCount,
        int sampleCount,
        Func<ModelGradient, double[]> select)
    {
        var sourceLength = select(gradients[0]).Length;
        for (var index = 0; index < sourceLength; index++)
        {
            var sum = 0d;
            for (var frame = 0; frame < gradientCount; frame++)
            {
                sum += select(gradients[frame])[index];
            }
            target[cursor++] = sum / Math.Max(1, sampleCount);
        }
    }

    private static double Average(
        IReadOnlyList<ModelGradient> gradients,
        int gradientCount,
        int sampleCount,
        Func<ModelGradient, double> select)
    {
        var sum = 0d;
        for (var index = 0; index < gradientCount; index++)
        {
            sum += select(gradients[index]);
        }
        return sum / Math.Max(1, sampleCount);
    }

    private static void ApplyAdamW(
        double[] target,
        IReadOnlyList<double> gradient,
        CombatPolicyValueOptimizerState optimizer,
        ref int cursor,
        double learningRate,
        double weightDecay)
    {
        for (var index = 0; index < target.Length; index++)
        {
            if (weightDecay > 0d)
            {
                target[index] *= Math.Max(
                    0d,
                    1d - learningRate * weightDecay);
            }
            target[index] = ApplyAdam(
                target[index],
                gradient[cursor],
                optimizer,
                cursor,
                learningRate);
            cursor++;
        }
    }

    private static double ApplyAdam(
        double target,
        double gradient,
        CombatPolicyValueOptimizerState optimizer,
        int index,
        double learningRate)
    {
        const double beta1 = 0.9d;
        const double beta2 = 0.999d;
        const double epsilon = 0.00000001d;
        var first = beta1 * optimizer.FirstMoment[index]
                    + (1d - beta1) * gradient;
        var second = beta2 * optimizer.SecondMoment[index]
                     + (1d - beta2) * gradient * gradient;
        optimizer.FirstMoment[index] = first;
        optimizer.SecondMoment[index] = second;
        var firstCorrection = 1d - Math.Pow(beta1, optimizer.Step);
        var secondCorrection = 1d - Math.Pow(beta2, optimizer.Step);
        var firstHat = first / Math.Max(epsilon, firstCorrection);
        var secondHat = second / Math.Max(epsilon, secondCorrection);
        var updated = target
                      - learningRate
                      * firstHat
                      / (Math.Sqrt(secondHat) + epsilon);
        if (!Finite(updated))
        {
            throw new InvalidOperationException(
                "策略价值网络优化器产生非有限权重");
        }
        return updated;
    }

    private static int ParameterCount(CombatPolicyValueNetworkDefinition model)
    {
        return model.StateWeights.Length
               + model.StateBias.Length
               + model.ActionWeights.Length
               + model.ActionBias.Length
               + model.PolicyWeights.Length
               + model.ActionQuantileWeights.Length
               + model.ActionQuantileBias.Length
               + model.ValueWeights.Length
               + model.WinWeights.Length
               + model.RiskWeights.Length
               + model.HpWeights.Length
               + model.TurnWeights.Length
               + 6;
    }

    private static CombatPolicyValueOptimizerState NewOptimizer(
        CombatPolicyValueNetworkDefinition model)
    {
        var count = ParameterCount(model);
        return new CombatPolicyValueOptimizerState
        {
            FirstMoment = new double[count],
            SecondMoment = new double[count]
        };
    }

    private static bool CompatibleOptimizer(
        CombatPolicyValueOptimizerState? optimizer,
        CombatPolicyValueNetworkDefinition model)
    {
        var count = ParameterCount(model);
        return optimizer != null
               && optimizer.Step >= 0
               && optimizer.FirstMoment?.Length == count
               && optimizer.SecondMoment?.Length == count
               && optimizer.FirstMoment.All(Finite)
               && optimizer.SecondMoment.All(Finite);
    }

    private static CombatPolicyValueOptimizerState CloneOptimizer(
        CombatPolicyValueOptimizerState source)
    {
        return new CombatPolicyValueOptimizerState
        {
            Step = source.Step,
            FirstMoment = (double[])source.FirstMoment.Clone(),
            SecondMoment = (double[])source.SecondMoment.Clone()
        };
    }

    private static Metrics Evaluate(
        CombatPolicyValueNetworkDefinition model,
        IReadOnlyList<EncodedFrame> frames,
        int parallelism,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            return new Metrics();
        }
        var results = new FrameMetrics[frames.Count];
        Parallel.For(
            0,
            frames.Count,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelism
            },
            index => results[index] = EvaluateFrame(model, frames[index]));
        return AggregateMetrics(frames, results);
    }

    private static Metrics AggregateMetrics(
        IReadOnlyList<EncodedFrame> frames,
        IReadOnlyList<FrameMetrics> results)
    {
        if (frames.Count == 0 || results.Count == 0)
        {
            return new Metrics();
        }
        var allIndexes = Enumerable.Range(
                0,
                Math.Min(frames.Count, results.Count))
            .ToArray();
        var metrics = Summarize(results, allIndexes);
        var runLosses = allIndexes
            .GroupBy(index => frames[index].RunKey, StringComparer.Ordinal)
            .Select(group => CompositeValidationLoss(
                Summarize(results, group.ToArray())))
            .Where(Finite)
            .ToArray();
        metrics.RunCount = runLosses.Length;
        if (runLosses.Length == 0)
        {
            return metrics;
        }
        var mean = runLosses.Average();
        var variance = runLosses.Length <= 1
            ? 0d
            : runLosses.Sum(value =>
                (value - mean) * (value - mean))
              / (runLosses.Length - 1d);
        metrics.CompositeLossStandardError =
            Math.Sqrt(Math.Max(0d, variance) / runLosses.Length);
        var margin = 1.96d * metrics.CompositeLossStandardError;
        var composite = CompositeValidationLoss(metrics);
        metrics.CompositeLossCiLower = Math.Max(0d, composite - margin);
        metrics.CompositeLossCiUpper = composite + margin;
        return metrics;
    }

    private static Metrics Summarize(
        IReadOnlyList<FrameMetrics> results,
        IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 0)
        {
            return new Metrics();
        }
        var correct = 0;
        var criticalCorrect = 0;
        var criticalCount = 0;
        var valueError = 0d;
        var brier = 0d;
        var deathBrier = 0d;
        var hpError = 0d;
        var turnHuber = 0d;
        var policyCrossEntropy = 0d;
        var actionQuantilePinball = 0d;
        var actionQuantileMae = 0d;
        var actionQuantileLabels = 0;
        for (var offset = 0; offset < indexes.Count; offset++)
        {
            var index = indexes[offset];
            correct += results[index].Correct;
            valueError += results[index].ValueError;
            brier += results[index].Brier;
            deathBrier += results[index].DeathBrier;
            hpError += results[index].HpError;
            turnHuber += results[index].TurnHuber;
            policyCrossEntropy += results[index].PolicyCrossEntropy;
            actionQuantilePinball += results[index].ActionQuantilePinball
                                     * results[index].ActionQuantileLabelCount;
            actionQuantileMae += results[index].ActionQuantileMae
                                 * results[index].ActionQuantileLabelCount;
            actionQuantileLabels += results[index].ActionQuantileLabelCount;
            if (results[index].Critical)
            {
                criticalCount++;
                criticalCorrect += results[index].Correct;
            }
        }
        return new Metrics
        {
            PolicyAccuracy = (double)correct / indexes.Count,
            ValueMae = valueError / indexes.Count,
            Brier = brier / indexes.Count,
            DeathBrier = deathBrier / indexes.Count,
            HpMae = hpError / indexes.Count,
            TurnHuber = turnHuber / indexes.Count,
            PolicyCrossEntropy = policyCrossEntropy / indexes.Count,
            ActionQuantilePinball = actionQuantileLabels == 0
                ? 0d
                : actionQuantilePinball / actionQuantileLabels,
            ActionQuantileMae = actionQuantileLabels == 0
                ? 0d
                : actionQuantileMae / actionQuantileLabels,
            ActionQuantileLabelCount = actionQuantileLabels,
            CriticalPolicyAccuracy = criticalCount == 0
                ? (double)correct / indexes.Count
                : (double)criticalCorrect / criticalCount
        };
    }

    private static double CalibratePolicyTemperature(
        CombatPolicyValueNetworkDefinition model,
        IReadOnlyList<EncodedFrame> validationFrames,
        int parallelism,
        CancellationToken cancellationToken)
    {
        var candidates = new[] { 0.5d, 0.75d, 1d, 1.25d, 1.5d, 2d, 3d };
        if (validationFrames.Count == 0)
        {
            model.PolicyTemperature = 1d;
            return 1d;
        }
        var frameLosses = new double[
            validationFrames.Count * candidates.Length];
        Parallel.For(
            0,
            validationFrames.Count,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelism
            },
            index => WritePolicyTemperatureLosses(
                model,
                validationFrames[index],
                candidates,
                frameLosses,
                index * candidates.Length));
        var bestTemperature = 1d;
        var bestCrossEntropy = double.MaxValue;
        for (var candidateIndex = 0;
             candidateIndex < candidates.Length;
             candidateIndex++)
        {
            var crossEntropy = 0d;
            for (var frameIndex = 0;
                 frameIndex < validationFrames.Count;
                 frameIndex++)
            {
                crossEntropy += frameLosses[
                    frameIndex * candidates.Length + candidateIndex];
            }
            crossEntropy /= validationFrames.Count;
            if (crossEntropy < bestCrossEntropy - 0.000000001d)
            {
                bestCrossEntropy = crossEntropy;
                bestTemperature = candidates[candidateIndex];
            }
        }
        model.PolicyTemperature = bestTemperature;
        return bestTemperature;
    }

    private static void WritePolicyTemperatureLosses(
        CombatPolicyValueNetworkDefinition model,
        EncodedFrame frame,
        IReadOnlyList<double> temperatures,
        double[] losses,
        int lossOffset)
    {
        var hidden = DenseTanh(
            frame.State,
            model.StateWeights,
            model.StateBias,
            model.HiddenDimensions);
        var logits = new double[frame.Actions.Length];
        for (var index = 0; index < frame.Actions.Length; index++)
        {
            var actionHidden = DenseTanh(
                frame.Actions[index],
                model.ActionWeights,
                model.ActionBias,
                model.HiddenDimensions);
            logits[index] =
                Interaction(hidden, actionHidden, model.PolicyWeights)
                + model.PolicyBias;
        }
        for (var temperatureIndex = 0;
             temperatureIndex < temperatures.Count;
             temperatureIndex++)
        {
            var inverseTemperature =
                1d / Math.Max(0.000001d, temperatures[temperatureIndex]);
            var maximum = double.NegativeInfinity;
            for (var index = 0; index < logits.Length; index++)
            {
                maximum = Math.Max(
                    maximum,
                    logits[index] * inverseTemperature);
            }
            var exponentialSum = 0d;
            var targetLogit = 0d;
            for (var index = 0; index < logits.Length; index++)
            {
                var scaled = logits[index] * inverseTemperature;
                exponentialSum += Math.Exp(scaled - maximum);
                targetLogit += frame.PolicyTargets[index] * scaled;
            }
            losses[lossOffset + temperatureIndex] =
                maximum
                + Math.Log(Math.Max(0.000000001d, exponentialSum))
                - targetLogit;
        }
    }

    private static FrameMetrics EvaluateFrame(
        CombatPolicyValueNetworkDefinition model,
        EncodedFrame frame)
    {
        var hidden = DenseTanh(
            frame.State,
            model.StateWeights,
            model.StateBias,
            model.HiddenDimensions);
        var bestIndex = 0;
        var bestLogit = double.NegativeInfinity;
        var logits = new double[frame.Actions.Length];
        var actionQuantilePinball = 0d;
        var actionQuantileMae = 0d;
        var actionQuantileLabels = 0;
        for (var index = 0; index < frame.Actions.Length; index++)
        {
            var actionHidden = DenseTanh(
                frame.Actions[index],
                model.ActionWeights,
                model.ActionBias,
                model.HiddenDimensions);
            var logit = (
                Interaction(
                    hidden,
                    actionHidden,
                    model.PolicyWeights)
                + model.PolicyBias)
                / model.PolicyTemperature;
            logits[index] = logit;
            if (logit > bestLogit)
            {
                bestLogit = logit;
                bestIndex = index;
            }
            var quantileTargets = index < frame.ActionQuantileTargets.Length
                ? frame.ActionQuantileTargets[index]
                : Array.Empty<double>();
            for (var quantile = 0;
                 quantile < quantileTargets.Length
                 && quantile < model.ActionQuantileCount;
                 quantile++)
            {
                var prediction = Interaction(
                    hidden,
                    actionHidden,
                    model.ActionQuantileWeights,
                    quantile * model.HiddenDimensions)
                                 + model.ActionQuantileBias[quantile];
                var error = prediction - quantileTargets[quantile];
                var absolute = Math.Abs(error);
                var tau = (quantile + 0.5d) / model.ActionQuantileCount;
                var asymmetry = error >= 0d ? 1d - tau : tau;
                actionQuantilePinball += asymmetry * (absolute <= 1d
                    ? 0.5d * absolute * absolute
                    : absolute - 0.5d);
                actionQuantileMae += absolute;
                actionQuantileLabels++;
            }
        }
        var value = Clamp(
            Dot(hidden, model.ValueWeights) + model.ValueBias,
            -1d,
            1d);
        var win = Sigmoid(Dot(hidden, model.WinWeights) + model.WinBias);
        var winDifference = win - frame.WinTarget;
        var risk = Sigmoid(Dot(hidden, model.RiskWeights) + model.RiskBias);
        var hp = Sigmoid(Dot(hidden, model.HpWeights) + model.HpBias);
        var turns = SoftPlus(Dot(hidden, model.TurnWeights) + model.TurnBias);
        var probabilities = Softmax(logits);
        var crossEntropy = 0d;
        for (var index = 0; index < probabilities.Length; index++)
        {
            crossEntropy -= frame.PolicyTargets[index]
                            * Math.Log(Math.Max(0.000000001d,
                                probabilities[index]));
        }
        var turnError = Math.Abs(turns - frame.TurnsTarget);
        return new FrameMetrics
        {
            Correct = bestIndex == frame.PolicyTargetIndex ? 1 : 0,
            ValueError = Math.Abs(value - frame.LongTermReturn),
            Brier = winDifference * winDifference,
            DeathBrier = (risk - frame.DeathTarget)
                         * (risk - frame.DeathTarget),
            HpError = Math.Abs(hp - frame.HpTarget),
            TurnHuber = turnError <= 1d
                ? 0.5d * turnError * turnError
                : turnError - 0.5d,
            PolicyCrossEntropy = crossEntropy,
            ActionQuantilePinball = actionQuantileLabels == 0
                ? 0d
                : actionQuantilePinball / actionQuantileLabels,
            ActionQuantileMae = actionQuantileLabels == 0
                ? 0d
                : actionQuantileMae / actionQuantileLabels,
            ActionQuantileLabelCount = actionQuantileLabels,
            Critical = frame.Critical
        };
    }

    private static CombatPolicyValueNetworkDefinition Initialize(
        string profile,
        CombatPolicyValueTrainingOptions options)
    {
        var random = new Random(options.RandomSeed);
        return new CombatPolicyValueNetworkDefinition
        {
            ModelId = "aura-combat-policy-value-" + DateTime.UtcNow.Ticks,
            DecisionProfile = profile,
            StateDimensions = options.StateDimensions,
            ActionDimensions = options.ActionDimensions,
            HiddenDimensions = options.HiddenDimensions,
            FeatureEncodingMode = options.FeatureEncodingMode,
            StateWeights = RandomWeights(
                random,
                options.StateDimensions * options.HiddenDimensions,
                options.StateDimensions),
            StateBias = new double[options.HiddenDimensions],
            ActionWeights = RandomWeights(
                random,
                options.ActionDimensions * options.HiddenDimensions,
                options.ActionDimensions),
            ActionBias = new double[options.HiddenDimensions],
            PolicyWeights = RandomWeights(
                random, options.HiddenDimensions, options.HiddenDimensions),
            ActionQuantileCount = options.ActionQuantileCount,
            ActionQuantileWeights = RandomWeights(
                random,
                options.HiddenDimensions * options.ActionQuantileCount,
                options.HiddenDimensions),
            ActionQuantileBias = new double[options.ActionQuantileCount],
            ValueWeights = RandomWeights(
                random, options.HiddenDimensions, options.HiddenDimensions),
            WinWeights = RandomWeights(
                random, options.HiddenDimensions, options.HiddenDimensions),
            RiskWeights = RandomWeights(
                random, options.HiddenDimensions, options.HiddenDimensions),
            HpWeights = RandomWeights(
                random, options.HiddenDimensions, options.HiddenDimensions),
            TurnWeights = RandomWeights(
                random, options.HiddenDimensions, options.HiddenDimensions)
        };
    }

    private static double CompositeValidationLoss(Metrics metrics)
    {
        return metrics.PolicyCrossEntropy * 0.25d
               + (1d - metrics.CriticalPolicyAccuracy) * 0.15d
               + metrics.ValueMae * 0.10d
               + metrics.Brier * 0.15d
               + metrics.DeathBrier * 0.15d
               + metrics.HpMae * 0.05d
               + Math.Min(1d, metrics.TurnHuber / 10d) * 0.05d
               + metrics.ActionQuantilePinball * 0.10d;
    }

    private static CombatPolicyValueEpochMetrics EpochMetrics(
        int epoch,
        bool calibrated,
        int trainingFrameCount,
        Metrics training,
        int validationFrameCount,
        Metrics validation)
    {
        return new CombatPolicyValueEpochMetrics
        {
            Epoch = Math.Max(0, epoch),
            Calibrated = calibrated,
            Training = Snapshot(training, trainingFrameCount),
            Validation = Snapshot(validation, validationFrameCount)
        };
    }

    private static CombatPolicyValueMetricSnapshot Snapshot(
        Metrics metrics,
        int frameCount)
    {
        return new CombatPolicyValueMetricSnapshot
        {
            FrameCount = Math.Max(0, frameCount),
            RunCount = Math.Max(0, metrics.RunCount),
            CompositeLoss = CompositeValidationLoss(metrics),
            CompositeLossStandardError =
                Math.Max(0d, metrics.CompositeLossStandardError),
            CompositeLossCiLower =
                Math.Max(0d, metrics.CompositeLossCiLower),
            CompositeLossCiUpper =
                Math.Max(0d, metrics.CompositeLossCiUpper),
            PolicyAccuracy = metrics.PolicyAccuracy,
            CriticalPolicyAccuracy = metrics.CriticalPolicyAccuracy,
            PolicyCrossEntropy = metrics.PolicyCrossEntropy,
            ValueMae = metrics.ValueMae,
            Brier = metrics.Brier,
            DeathBrier = metrics.DeathBrier,
            HpMae = metrics.HpMae,
            TurnHuber = metrics.TurnHuber,
            ActionQuantilePinball = metrics.ActionQuantilePinball,
            ActionQuantileMae = metrics.ActionQuantileMae,
            ActionQuantileLabelCount = metrics.ActionQuantileLabelCount
        };
    }

    private static CombatPolicyValueEpochMetrics CloneEpochMetrics(
        CombatPolicyValueEpochMetrics? source)
    {
        source ??= new CombatPolicyValueEpochMetrics();
        return new CombatPolicyValueEpochMetrics
        {
            Iteration = source.Iteration,
            Epoch = source.Epoch,
            Calibrated = source.Calibrated,
            EventKind = source.EventKind,
            TrainingMeasurement = source.TrainingMeasurement,
            ElapsedSeconds = source.ElapsedSeconds,
            LearningRate = source.LearningRate,
            GradientNorm = source.GradientNorm,
            GradientClipCount = source.GradientClipCount,
            Improved = source.Improved,
            BestEpoch = source.BestEpoch,
            BestValidationLoss = source.BestValidationLoss,
            StaleEpochs = source.StaleEpochs,
            EarlyStopped = source.EarlyStopped,
            TrainingSplitHash = source.TrainingSplitHash,
            ValidationSplitHash = source.ValidationSplitHash,
            Training = CloneMetricSnapshot(source.Training),
            Validation = CloneMetricSnapshot(source.Validation)
        };
    }

    private static CombatPolicyValueMetricSnapshot CloneMetricSnapshot(
        CombatPolicyValueMetricSnapshot? source)
    {
        source ??= new CombatPolicyValueMetricSnapshot();
        return new CombatPolicyValueMetricSnapshot
        {
            FrameCount = source.FrameCount,
            RunCount = source.RunCount,
            CompositeLoss = source.CompositeLoss,
            CompositeLossStandardError =
                source.CompositeLossStandardError,
            CompositeLossCiLower = source.CompositeLossCiLower,
            CompositeLossCiUpper = source.CompositeLossCiUpper,
            PolicyAccuracy = source.PolicyAccuracy,
            CriticalPolicyAccuracy = source.CriticalPolicyAccuracy,
            PolicyCrossEntropy = source.PolicyCrossEntropy,
            ValueMae = source.ValueMae,
            Brier = source.Brier,
            DeathBrier = source.DeathBrier,
            HpMae = source.HpMae,
            TurnHuber = source.TurnHuber,
            ActionQuantilePinball = source.ActionQuantilePinball,
            ActionQuantileMae = source.ActionQuantileMae,
            ActionQuantileLabelCount = source.ActionQuantileLabelCount
        };
    }

    private static List<CombatPolicyValueModelCandidate> CloneCandidates(
        IEnumerable<CombatPolicyValueModelCandidate> source)
    {
        return (source ?? Array.Empty<CombatPolicyValueModelCandidate>())
            .Select(item => new CombatPolicyValueModelCandidate
            {
                Epoch = item.Epoch,
                ValidationLoss = item.ValidationLoss,
                TrainingMetrics = CloneMetricSnapshot(item.TrainingMetrics),
                ValidationMetrics =
                    CloneMetricSnapshot(item.ValidationMetrics),
                TestMetrics = CloneMetricSnapshot(item.TestMetrics),
                Model = Clone(item.Model)
            })
            .ToList();
    }

    private static bool Compatible(
        CombatPolicyValueNetworkDefinition? model,
        string profile,
        CombatPolicyValueTrainingOptions options)
    {
        return model != null
               && model.StateDimensions == options.StateDimensions
               && model.ActionDimensions == options.ActionDimensions
               && model.HiddenDimensions == options.HiddenDimensions
               && model.ActionQuantileCount == options.ActionQuantileCount
               && string.Equals(
                   model.FeatureEncodingMode,
                   options.FeatureEncodingMode,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   NormalizeProfile(model.DecisionProfile),
                   profile,
                   StringComparison.Ordinal)
               && CombatPolicyValueNetworkValidator.TryValidate(
                   model,
                   out _);
    }

    internal static CombatPolicyValueNetworkDefinition Clone(
        CombatPolicyValueNetworkDefinition source)
    {
        return new CombatPolicyValueNetworkDefinition
        {
            ModelProtocol = source.ModelProtocol,
            ProtocolVersion = source.ProtocolVersion,
            FeatureSchemaVersion = source.FeatureSchemaVersion,
            ModelId = source.ModelId,
            DecisionProfile = source.DecisionProfile,
            StateDimensions = source.StateDimensions,
            ActionDimensions = source.ActionDimensions,
            HiddenDimensions = source.HiddenDimensions,
            FeatureEncodingMode = source.FeatureEncodingMode,
            PolicyTemperature = source.PolicyTemperature,
            StateWeights = (double[])source.StateWeights.Clone(),
            StateBias = (double[])source.StateBias.Clone(),
            ActionWeights = (double[])source.ActionWeights.Clone(),
            ActionBias = (double[])source.ActionBias.Clone(),
            PolicyWeights = (double[])source.PolicyWeights.Clone(),
            PolicyBias = source.PolicyBias,
            ActionQuantileCount = source.ActionQuantileCount,
            ActionQuantileWeights =
                (double[])source.ActionQuantileWeights.Clone(),
            ActionQuantileBias = (double[])source.ActionQuantileBias.Clone(),
            ValueWeights = (double[])source.ValueWeights.Clone(),
            ValueBias = source.ValueBias,
            WinWeights = (double[])source.WinWeights.Clone(),
            WinBias = source.WinBias,
            RiskWeights = (double[])source.RiskWeights.Clone(),
            RiskBias = source.RiskBias,
            HpWeights = (double[])source.HpWeights.Clone(),
            HpBias = source.HpBias,
            TurnWeights = (double[])source.TurnWeights.Clone(),
            TurnBias = source.TurnBias,
            Metrics = new Dictionary<string, double>(
                source.Metrics ?? new Dictionary<string, double>(),
                StringComparer.OrdinalIgnoreCase),
            CreatedUtc = source.CreatedUtc
        };
    }

    private static double[] RandomWeights(
        Random random,
        int count,
        int fanIn)
    {
        var scale = Math.Sqrt(2d / Math.Max(1, fanIn)) * 0.25d;
        var result = new double[count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = (random.NextDouble() * 2d - 1d) * scale;
        }
        return result;
    }

    private static void ResetOrder(int[] order)
    {
        for (var index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }
    }

    private static void Shuffle(int[] order, int seed, int epoch)
    {
        var random = new Random(unchecked(seed * 397 ^ epoch * 7919));
        for (var index = order.Length - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (order[index], order[other]) = (order[other], order[index]);
        }
    }

    internal static double[] PolicyTargets(
        IReadOnlyList<CombatEpisodeCandidate> candidates,
        string executedCandidateId,
        double temperature,
        double maximumProbability)
    {
        var executed = -1;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (string.Equals(
                    candidates[index].CandidateId,
                    executedCandidateId,
                    StringComparison.Ordinal))
            {
                executed = index;
                break;
            }
        }
        var inverseTemperature = 1d / Math.Max(1d, temperature);
        var visitWeights = candidates
            .Select(candidate => Math.Pow(
                Math.Max(0, candidate.SearchVisits),
                inverseTemperature))
            .ToArray();
        var visits = visitWeights.Sum();
        var result = new double[candidates.Count];
        if (visits > 0)
        {
            if (executed >= 0 && candidates[executed].SearchVisits <= 0)
            {
                result[executed] = 1d;
            }
            else
            {
                for (var index = 0; index < candidates.Count; index++)
                {
                    result[index] = visitWeights[index] / visits;
                }
            }
        }
        else if (executed >= 0)
        {
            result[executed] = 1d;
        }
        else
        {
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = 1d / result.Length;
            }
        }
        CapPolicyTarget(result, maximumProbability);
        return result;
    }

    private static void CapPolicyTarget(
        double[] probabilities,
        double maximumProbability)
    {
        if (probabilities.Length <= 1)
        {
            return;
        }
        var maximumIndex = MaximumIndex(probabilities);
        var cap = Clamp(
            maximumProbability,
            1d / probabilities.Length,
            1d);
        var excess = probabilities[maximumIndex] - cap;
        if (excess <= 0d)
        {
            return;
        }
        probabilities[maximumIndex] = cap;
        var otherMass = probabilities.Sum() - cap;
        if (otherMass <= 0d)
        {
            var share = excess / (probabilities.Length - 1);
            for (var index = 0; index < probabilities.Length; index++)
            {
                if (index != maximumIndex)
                {
                    probabilities[index] = share;
                }
            }
            return;
        }
        for (var index = 0; index < probabilities.Length; index++)
        {
            if (index != maximumIndex)
            {
                probabilities[index] +=
                    excess * probabilities[index] / otherMass;
            }
        }
    }

    private static int MaximumIndex(IReadOnlyList<double> values)
    {
        var result = 0;
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] > values[result])
            {
                result = index;
            }
        }
        return result;
    }

    private static double[] DenseTanh(
        double[] input,
        double[] weights,
        double[] bias,
        int outputs)
    {
        var result = new double[outputs];
        DenseTanhInto(input, weights, bias, result, outputs);
        return result;
    }

    private static void DenseTanhInto(
        double[] input,
        double[] weights,
        double[] bias,
        double[] result,
        int outputs)
    {
        for (var output = 0; output < outputs; output++)
        {
            var value = bias[output];
            var offset = output * input.Length;
            value += Dot(
                input,
                0,
                weights,
                offset,
                input.Length);
            result[output] = Math.Tanh(value);
        }
    }

    private static double Interaction(
        double[] state,
        double[] action,
        double[] weights)
    {
        return Interaction(state, action, weights, 0);
    }

    private static double Interaction(
        double[] state,
        double[] action,
        double[] weights,
        int weightOffset)
    {
        var result = 0d;
        var index = 0;
#if NET8_0_OR_GREATER
        if (Vector.IsHardwareAccelerated
            && state.Length >= Vector<double>.Count)
        {
            var vectorSum = Vector<double>.Zero;
            var vectorEnd =
                state.Length - state.Length % Vector<double>.Count;
            for (; index < vectorEnd; index += Vector<double>.Count)
            {
                vectorSum += new Vector<double>(state, index)
                             * new Vector<double>(action, index)
                             * new Vector<double>(weights, weightOffset + index);
            }
            for (var lane = 0; lane < Vector<double>.Count; lane++)
            {
                result += vectorSum[lane];
            }
        }
#endif
        for (; index < state.Length; index++)
        {
            result += state[index]
                      * action[index]
                      * weights[weightOffset + index];
        }
        return result;
    }

    private static double[] Softmax(IReadOnlyList<double> logits)
    {
        var result = new double[logits.Count];
        SoftmaxInto(logits, logits.Count, result);
        return result;
    }

    private static void SoftmaxInto(
        IReadOnlyList<double> logits,
        int count,
        double[] result)
    {
        var maximum = double.NegativeInfinity;
        for (var index = 0; index < count; index++)
        {
            maximum = Math.Max(maximum, logits[index]);
        }
        var total = 0d;
        for (var index = 0; index < count; index++)
        {
            result[index] = Math.Exp(Clamp(
                logits[index] - maximum,
                -30d,
                30d));
            total += result[index];
        }
        total = Math.Max(0.0000001d, total);
        for (var index = 0; index < count; index++)
        {
            result[index] /= total;
        }
    }

    private static double Dot(
        IReadOnlyList<double> left,
        IReadOnlyList<double> right)
    {
        if (left is double[] leftArray
            && right is double[] rightArray)
        {
            return Dot(
                leftArray,
                0,
                rightArray,
                0,
                Math.Min(leftArray.Length, rightArray.Length));
        }
        var result = 0d;
        for (var index = 0;
             index < Math.Min(left.Count, right.Count);
             index++)
        {
            result += left[index] * right[index];
        }
        return result;
    }

    private static double Dot(
        double[] left,
        int leftOffset,
        double[] right,
        int rightOffset,
        int length)
    {
        var result = 0d;
        var index = 0;
#if NET8_0_OR_GREATER
        if (Vector.IsHardwareAccelerated
            && length >= Vector<double>.Count)
        {
            var vectorSum = Vector<double>.Zero;
            var vectorEnd = length - length % Vector<double>.Count;
            for (; index < vectorEnd; index += Vector<double>.Count)
            {
                vectorSum += new Vector<double>(left, leftOffset + index)
                             * new Vector<double>(
                                 right,
                                 rightOffset + index);
            }
            for (var lane = 0; lane < Vector<double>.Count; lane++)
            {
                result += vectorSum[lane];
            }
        }
#endif
        for (; index < length; index++)
        {
            result += left[leftOffset + index]
                      * right[rightOffset + index];
        }
        return result;
    }

    private static double Sigmoid(double value)
    {
        value = Clamp(value, -30d, 30d);
        return 1d / (1d + Math.Exp(-value));
    }

    private static double SoftPlus(double value)
    {
        value = Clamp(value, -30d, 30d);
        return Math.Log(1d + Math.Exp(value));
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Max(minimum, Math.Min(maximum, value));
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string NormalizeProfile(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "aggressive" => "aggressive",
            "defensive" => "defensive",
            _ => "balanced"
        };
    }

    private sealed class EncodedFrame
    {
        public string RunKey { get; set; } = "";

        public double[] State { get; set; } = Array.Empty<double>();

        public double[][] Actions { get; set; } = Array.Empty<double[]>();

        public double[] PolicyTargets { get; set; } = Array.Empty<double>();

        public double[][] ActionQuantileTargets { get; set; } =
            Array.Empty<double[]>();

        public double ActionQuantileLossWeight { get; set; }

        public int PolicyTargetIndex { get; set; }

        public double LongTermReturn { get; set; }

        public double WinTarget { get; set; }

        public double DeathTarget { get; set; }

        public double HpTarget { get; set; }

        public double TurnsTarget { get; set; }

        public bool Critical { get; set; }

        public bool EndTurnDecision { get; set; }

        public bool UnsafeEndTurn { get; set; }

        public string Stratum { get; set; } = "";

        public double SampleWeight { get; set; } = 1d;

        public double BaseSampleWeight { get; set; } = 1d;
    }

    private sealed class FrameSource
    {
        public CombatEpisode Episode { get; set; } = new();

        public CombatEpisodeFrame Frame { get; set; } = new();
    }

    private sealed class ModelWorkspace
    {
        private readonly int hiddenDimensions;

        public ModelWorkspace(int hiddenDimensions)
        {
            this.hiddenDimensions = Math.Max(1, hiddenDimensions);
            StateHidden = new double[this.hiddenDimensions];
            StateGradient = new double[this.hiddenDimensions];
            ActionGradient = new double[this.hiddenDimensions];
        }

        public double[] StateHidden { get; }

        public double[] StateGradient { get; }

        public double[] ActionGradient { get; }

        public double[][] ActionHidden { get; private set; } =
            Array.Empty<double[]>();

        public double[] Logits { get; private set; } = Array.Empty<double>();

        public double[] Probabilities { get; private set; } =
            Array.Empty<double>();

        public void Prepare(int actionCount)
        {
            var count = Math.Max(1, actionCount);
            if (Logits.Length >= count)
            {
                return;
            }
            var capacity = Math.Max(count, Logits.Length * 2);
            Logits = new double[capacity];
            Probabilities = new double[capacity];
            var hidden = new double[capacity][];
            Array.Copy(
                ActionHidden,
                hidden,
                ActionHidden.Length);
            for (var index = ActionHidden.Length;
                 index < capacity;
                 index++)
            {
                hidden[index] = new double[hiddenDimensions];
            }
            ActionHidden = hidden;
        }
    }

    private sealed class ModelGradient
    {
        public ModelGradient(CombatPolicyValueNetworkDefinition model)
        {
            StateWeights = new double[model.StateWeights.Length];
            StateBias = new double[model.StateBias.Length];
            ActionWeights = new double[model.ActionWeights.Length];
            ActionBias = new double[model.ActionBias.Length];
            PolicyWeights = new double[model.PolicyWeights.Length];
            ActionQuantileWeights =
                new double[model.ActionQuantileWeights.Length];
            ActionQuantileBias = new double[model.ActionQuantileBias.Length];
            ValueWeights = new double[model.ValueWeights.Length];
            WinWeights = new double[model.WinWeights.Length];
            RiskWeights = new double[model.RiskWeights.Length];
            HpWeights = new double[model.HpWeights.Length];
            TurnWeights = new double[model.TurnWeights.Length];
        }

        public double[] StateWeights { get; }

        public double[] StateBias { get; }

        public double[] ActionWeights { get; }

        public double[] ActionBias { get; }

        public double[] PolicyWeights { get; }

        public double PolicyBias { get; set; }

        public double[] ActionQuantileWeights { get; }

        public double[] ActionQuantileBias { get; }

        public double[] ValueWeights { get; }

        public double ValueBias { get; set; }

        public double[] WinWeights { get; }

        public double WinBias { get; set; }

        public double[] RiskWeights { get; }

        public double RiskBias { get; set; }

        public double[] HpWeights { get; }

        public double HpBias { get; set; }

        public double[] TurnWeights { get; }

        public double TurnBias { get; set; }

        public void Clear()
        {
            Array.Clear(StateWeights, 0, StateWeights.Length);
            Array.Clear(StateBias, 0, StateBias.Length);
            Array.Clear(ActionWeights, 0, ActionWeights.Length);
            Array.Clear(ActionBias, 0, ActionBias.Length);
            Array.Clear(PolicyWeights, 0, PolicyWeights.Length);
            Array.Clear(
                ActionQuantileWeights,
                0,
                ActionQuantileWeights.Length);
            Array.Clear(ActionQuantileBias, 0, ActionQuantileBias.Length);
            Array.Clear(ValueWeights, 0, ValueWeights.Length);
            Array.Clear(WinWeights, 0, WinWeights.Length);
            Array.Clear(RiskWeights, 0, RiskWeights.Length);
            Array.Clear(HpWeights, 0, HpWeights.Length);
            Array.Clear(TurnWeights, 0, TurnWeights.Length);
            PolicyBias = 0d;
            ValueBias = 0d;
            WinBias = 0d;
            RiskBias = 0d;
            HpBias = 0d;
            TurnBias = 0d;
        }

        public void Scale(double factor)
        {
            Scale(StateWeights, factor);
            Scale(StateBias, factor);
            Scale(ActionWeights, factor);
            Scale(ActionBias, factor);
            Scale(PolicyWeights, factor);
            Scale(ActionQuantileWeights, factor);
            Scale(ActionQuantileBias, factor);
            Scale(ValueWeights, factor);
            Scale(WinWeights, factor);
            Scale(RiskWeights, factor);
            Scale(HpWeights, factor);
            Scale(TurnWeights, factor);
            PolicyBias *= factor;
            ValueBias *= factor;
            WinBias *= factor;
            RiskBias *= factor;
            HpBias *= factor;
            TurnBias *= factor;
        }

        public void AddScaled(ModelGradient source, double factor)
        {
            AddScaled(StateWeights, source.StateWeights, factor);
            AddScaled(StateBias, source.StateBias, factor);
            AddScaled(ActionWeights, source.ActionWeights, factor);
            AddScaled(ActionBias, source.ActionBias, factor);
            AddScaled(PolicyWeights, source.PolicyWeights, factor);
            AddScaled(
                ActionQuantileWeights,
                source.ActionQuantileWeights,
                factor);
            AddScaled(
                ActionQuantileBias,
                source.ActionQuantileBias,
                factor);
            AddScaled(ValueWeights, source.ValueWeights, factor);
            AddScaled(WinWeights, source.WinWeights, factor);
            AddScaled(RiskWeights, source.RiskWeights, factor);
            AddScaled(HpWeights, source.HpWeights, factor);
            AddScaled(TurnWeights, source.TurnWeights, factor);
            PolicyBias += source.PolicyBias * factor;
            ValueBias += source.ValueBias * factor;
            WinBias += source.WinBias * factor;
            RiskBias += source.RiskBias * factor;
            HpBias += source.HpBias * factor;
            TurnBias += source.TurnBias * factor;
        }

        private static void Scale(double[] values, double factor)
        {
            for (var index = 0; index < values.Length; index++)
            {
                values[index] *= factor;
            }
        }

        private static void AddScaled(
            double[] target,
            IReadOnlyList<double> source,
            double factor)
        {
            for (var index = 0; index < target.Length; index++)
            {
                target[index] += source[index] * factor;
            }
        }
    }

    private sealed class Metrics
    {
        public int RunCount { get; set; }

        public double CompositeLossStandardError { get; set; }

        public double CompositeLossCiLower { get; set; }

        public double CompositeLossCiUpper { get; set; }

        public double PolicyAccuracy { get; set; }

        public double ValueMae { get; set; }

        public double Brier { get; set; }

        public double DeathBrier { get; set; }

        public double HpMae { get; set; }

        public double TurnHuber { get; set; }

        public double PolicyCrossEntropy { get; set; }

        public double CriticalPolicyAccuracy { get; set; }

        public double ActionQuantilePinball { get; set; }

        public double ActionQuantileMae { get; set; }

        public int ActionQuantileLabelCount { get; set; }
    }

    private readonly struct BatchUpdate
    {
        public BatchUpdate(double gradientNorm, bool clipped)
        {
            GradientNorm = gradientNorm;
            Clipped = clipped;
        }

        public double GradientNorm { get; }

        public bool Clipped { get; }
    }

    private struct FrameMetrics
    {
        public int Correct { get; set; }

        public double ValueError { get; set; }

        public double Brier { get; set; }

        public double DeathBrier { get; set; }

        public double HpError { get; set; }

        public double TurnHuber { get; set; }

        public double PolicyCrossEntropy { get; set; }

        public double ActionQuantilePinball { get; set; }

        public double ActionQuantileMae { get; set; }

        public int ActionQuantileLabelCount { get; set; }

        public bool Critical { get; set; }
    }
}
