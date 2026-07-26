using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            FrameCount = episodes.Sum(episode => episode.Frames?.Count ?? 0)
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
        var validationRunKeys = episodes.Count >= 10 && runKeys.Count >= 2
            ? runKeys
                .Where((_, index) => index % 5 == 0)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var trainingEpisodes = episodes
            .Where(episode =>
                !validationRunKeys.Contains(StableRunKey(episode)))
            .ToList();
        var validationEpisodes = episodes
            .Where(episode =>
                validationRunKeys.Contains(StableRunKey(episode)))
            .ToList();
        if (trainingEpisodes.Count == 0)
        {
            trainingEpisodes = episodes;
            validationEpisodes.Clear();
        }
        var trainingFrames = Encode(
            trainingEpisodes,
            options,
            cancellationToken);
        var validationFrames = Encode(
            validationEpisodes.Count == 0
                ? trainingEpisodes
                : validationEpisodes,
            options,
            cancellationToken);
        if (trainingFrames.Length == 0)
        {
            result.Message = "完整战斗轨迹没有可训练的合法决策帧";
            return result;
        }

        var resume = session?.Resume;
        var model = Compatible(resume?.Model, profile, options)
            ? Clone(resume!.Model!)
            : Initialize(profile, options);
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
        var order = Enumerable.Range(0, trainingFrames.Length).ToArray();
        var batchCapacity = Math.Min(options.BatchSize, trainingFrames.Length);
        var gradients = Enumerable.Range(0, batchCapacity)
            .Select(_ => new ModelGradient(model))
            .ToArray();
        var stoppedEarly = false;
        var completedEpochsActual = startEpoch;
        var lastBatchProgressMilliseconds = -1000L;
        for (var epoch = startEpoch; epoch < options.Epochs; epoch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetOrder(order);
            Shuffle(order, options.RandomSeed, epoch);
            var rate = options.LearningRate / Math.Sqrt(1d + epoch * 0.05d);
            for (var batchStart = 0;
                 batchStart < order.Length;
                 batchStart += options.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(options.BatchSize, order.Length - batchStart);
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism =
                        options.MaximumDegreeOfParallelism
                };
                Parallel.For(
                    0,
                    count,
                    parallelOptions,
                    offset =>
                    {
                        var gradient = gradients[offset];
                        gradient.Clear();
                        AccumulateGradient(
                            model,
                            trainingFrames[order[batchStart + offset]],
                            gradient);
                    });
                ApplyBatch(
                    model,
                    gradients,
                    count,
                    rate,
                    options.L2);
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
            var validationLoss = validation.ValueMae
                                 + validation.Brier
                                 + (1d - validation.PolicyAccuracy) * 0.25d;
            var completedEpochs = epoch + 1;
            completedEpochsActual = completedEpochs;
            if (bestLoss - validationLoss
                > options.EarlyStoppingMinimumDelta)
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
            var epochRate = completedEpochs <= startEpoch
                ? 0d
                : (completedEpochs - startEpoch)
                  / Math.Max(0.001d, clock.Elapsed.TotalSeconds);
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
                StaleEpochs = staleEpochs
            };
            session?.Progress?.Invoke(progress);
            session?.Checkpoint?.Invoke(
                new CombatPolicyValueTrainingResumeState
                {
                    CompletedEpochs = completedEpochs,
                    Model = Clone(model),
                    BestModel = Clone(bestModel),
                    BestValidationLoss = bestLoss,
                    BestEpoch = bestEpoch,
                    StaleEpochs = staleEpochs
                });
            if (completedEpochs >= options.MinimumEpochs
                && staleEpochs >= options.EarlyStoppingPatience)
            {
                stoppedEarly = true;
                progress.EarlyStopped = true;
                progress.Stage = "early-stopped";
                session?.Progress?.Invoke(progress);
                break;
            }
        }

        model = Clone(bestModel);
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
            ["trainingPolicyAccuracy"] = trainingMetrics.PolicyAccuracy,
            ["validationPolicyAccuracy"] = validationMetrics.PolicyAccuracy,
            ["trainingValueMae"] = trainingMetrics.ValueMae,
            ["validationValueMae"] = validationMetrics.ValueMae,
            ["validationBrier"] = validationMetrics.Brier,
            ["validationEpisodeCount"] = validationEpisodes.Count,
            ["completedEpochs"] = Math.Max(startEpoch, bestEpoch),
            ["bestEpoch"] = bestEpoch,
            ["earlyStopped"] = stoppedEarly ? 1d : 0d,
            ["batchSize"] = options.BatchSize,
            ["trainingParallelism"] = options.MaximumDegreeOfParallelism
        };
        result.Success = true;
        result.Model = model;
        result.CompletedEpochs = Math.Max(
            startEpoch,
            completedEpochsActual);
        result.BestEpoch = bestEpoch;
        result.EarlyStopped = stoppedEarly;
        result.ElapsedSeconds = clock.Elapsed.TotalSeconds;
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
            ValidationLoss = validationMetrics.ValueMae
                             + validationMetrics.Brier
                             + (1d - validationMetrics.PolicyAccuracy) * 0.25d,
            BestValidationLoss = bestLoss,
            BestEpoch = bestEpoch,
            EarlyStopped = stoppedEarly
        });
        return result;
    }

    private static string StableRunKey(CombatEpisode episode)
    {
        return string.IsNullOrWhiteSpace(episode.JourneyRunId)
            ? "episode:" + (episode.EpisodeId ?? "")
            : "journey:" + episode.JourneyRunId;
    }

    private static EncodedFrame[] Encode(
        IReadOnlyList<CombatEpisode> episodes,
        CombatPolicyValueTrainingOptions options,
        CancellationToken cancellationToken)
    {
        var frames = episodes
            .SelectMany(episode => episode.Frames
                                  ?? new List<CombatEpisodeFrame>())
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
            index => encoded[index] = EncodeFrame(frames[index], options));
        return encoded.Where(item => item != null).Select(item => item!).ToArray();
    }

    private static EncodedFrame? EncodeFrame(
        CombatEpisodeFrame frame,
        CombatPolicyValueTrainingOptions options)
    {
        var legal = (frame.Candidates
                     ?? new List<CombatEpisodeCandidate>())
            .Where(candidate => candidate.Legal)
            .ToList();
        if (legal.Count == 0)
        {
            return null;
        }
        var targets = PolicyTargets(legal, frame.ExecutedCandidateId);
        return new EncodedFrame
        {
            State = CombatPolicyValueEncoding.EncodeState(
                frame.StateFeatures,
                options.StateDimensions),
            Actions = legal.Select(candidate =>
                    CombatPolicyValueEncoding.EncodeCandidate(
                        new CombatPolicyValueCandidate
                        {
                            CandidateId = candidate.CandidateId,
                            SourceId = candidate.SourceId,
                            Features = candidate.Features
                        },
                        options.ActionDimensions))
                .ToArray(),
            PolicyTargets = targets,
            PolicyTargetIndex = MaximumIndex(targets),
            LongTermReturn = Clamp(frame.LongTermReturn, -1d, 1d),
            WinTarget = Clamp(frame.WinTarget, 0d, 1d),
            DeathTarget = Clamp(frame.DeathTarget, 0d, 1d),
            HpTarget = Clamp(frame.RemainingHpRatioTarget, 0d, 1d),
            TurnsTarget = Math.Max(0d, frame.RemainingTurnsTarget)
        };
    }

    private static void AccumulateGradient(
        CombatPolicyValueNetworkDefinition model,
        EncodedFrame frame,
        ModelGradient gradient)
    {
        var hiddenCount = model.HiddenDimensions;
        var stateHidden = DenseTanh(
            frame.State,
            model.StateWeights,
            model.StateBias,
            hiddenCount);
        var actionHidden = new double[frame.Actions.Length][];
        var logits = new double[frame.Actions.Length];
        for (var actionIndex = 0;
             actionIndex < frame.Actions.Length;
             actionIndex++)
        {
            actionHidden[actionIndex] = DenseTanh(
                frame.Actions[actionIndex],
                model.ActionWeights,
                model.ActionBias,
                hiddenCount);
            logits[actionIndex] = Interaction(
                stateHidden,
                actionHidden[actionIndex],
                model.PolicyWeights)
                                  + model.PolicyBias;
        }
        var probabilities = Softmax(logits);
        var stateGradient = new double[hiddenCount];
        for (var actionIndex = 0;
             actionIndex < frame.Actions.Length;
             actionIndex++)
        {
            var outputGradient = probabilities[actionIndex]
                                 - frame.PolicyTargets[actionIndex];
            gradient.PolicyBias += outputGradient;
            var actionGradient = new double[hiddenCount];
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

    private static void ApplyBatch(
        CombatPolicyValueNetworkDefinition model,
        IReadOnlyList<ModelGradient> gradients,
        int count,
        double learningRate,
        double l2)
    {
        var scale = learningRate / Math.Max(1, count);
        ApplyArray(model.StateWeights, gradients, count,
            gradient => gradient.StateWeights, scale, l2);
        ApplyArray(model.StateBias, gradients, count,
            gradient => gradient.StateBias, scale, 0d);
        ApplyArray(model.ActionWeights, gradients, count,
            gradient => gradient.ActionWeights, scale, l2);
        ApplyArray(model.ActionBias, gradients, count,
            gradient => gradient.ActionBias, scale, 0d);
        ApplyArray(model.PolicyWeights, gradients, count,
            gradient => gradient.PolicyWeights, scale, l2);
        ApplyArray(model.ValueWeights, gradients, count,
            gradient => gradient.ValueWeights, scale, l2);
        ApplyArray(model.WinWeights, gradients, count,
            gradient => gradient.WinWeights, scale, l2);
        ApplyArray(model.RiskWeights, gradients, count,
            gradient => gradient.RiskWeights, scale, l2);
        ApplyArray(model.HpWeights, gradients, count,
            gradient => gradient.HpWeights, scale, l2);
        ApplyArray(model.TurnWeights, gradients, count,
            gradient => gradient.TurnWeights, scale, l2);
        model.PolicyBias -= scale * Sum(
            gradients, count, gradient => gradient.PolicyBias);
        model.ValueBias -= scale * Sum(
            gradients, count, gradient => gradient.ValueBias);
        model.WinBias -= scale * Sum(
            gradients, count, gradient => gradient.WinBias);
        model.RiskBias -= scale * Sum(
            gradients, count, gradient => gradient.RiskBias);
        model.HpBias -= scale * Sum(
            gradients, count, gradient => gradient.HpBias);
        model.TurnBias -= scale * Sum(
            gradients, count, gradient => gradient.TurnBias);
    }

    private static void ApplyArray(
        double[] target,
        IReadOnlyList<ModelGradient> gradients,
        int count,
        Func<ModelGradient, double[]> select,
        double scale,
        double l2)
    {
        for (var index = 0; index < target.Length; index++)
        {
            var sum = 0d;
            for (var frame = 0; frame < count; frame++)
            {
                sum += select(gradients[frame])[index];
            }
            target[index] -= scale * sum + scale * count * l2 * target[index];
        }
    }

    private static double Sum(
        IReadOnlyList<ModelGradient> gradients,
        int count,
        Func<ModelGradient, double> select)
    {
        var sum = 0d;
        for (var index = 0; index < count; index++)
        {
            sum += select(gradients[index]);
        }
        return sum;
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
        var correct = 0;
        var valueError = 0d;
        var brier = 0d;
        for (var index = 0; index < results.Length; index++)
        {
            correct += results[index].Correct;
            valueError += results[index].ValueError;
            brier += results[index].Brier;
        }
        return new Metrics
        {
            PolicyAccuracy = (double)correct / results.Length,
            ValueMae = valueError / results.Length,
            Brier = brier / results.Length
        };
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
        for (var index = 0; index < frame.Actions.Length; index++)
        {
            var actionHidden = DenseTanh(
                frame.Actions[index],
                model.ActionWeights,
                model.ActionBias,
                model.HiddenDimensions);
            var logit = Interaction(
                hidden,
                actionHidden,
                model.PolicyWeights)
                        + model.PolicyBias;
            if (logit > bestLogit)
            {
                bestLogit = logit;
                bestIndex = index;
            }
        }
        var value = Clamp(
            Dot(hidden, model.ValueWeights) + model.ValueBias,
            -1d,
            1d);
        var win = Sigmoid(Dot(hidden, model.WinWeights) + model.WinBias);
        var winDifference = win - frame.WinTarget;
        return new FrameMetrics
        {
            Correct = bestIndex == frame.PolicyTargetIndex ? 1 : 0,
            ValueError = Math.Abs(value - frame.LongTermReturn),
            Brier = winDifference * winDifference
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

    private static bool Compatible(
        CombatPolicyValueNetworkDefinition? model,
        string profile,
        CombatPolicyValueTrainingOptions options)
    {
        return model != null
               && model.StateDimensions == options.StateDimensions
               && model.ActionDimensions == options.ActionDimensions
               && model.HiddenDimensions == options.HiddenDimensions
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
            StateWeights = (double[])source.StateWeights.Clone(),
            StateBias = (double[])source.StateBias.Clone(),
            ActionWeights = (double[])source.ActionWeights.Clone(),
            ActionBias = (double[])source.ActionBias.Clone(),
            PolicyWeights = (double[])source.PolicyWeights.Clone(),
            PolicyBias = source.PolicyBias,
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

    private static double[] PolicyTargets(
        IReadOnlyList<CombatEpisodeCandidate> candidates,
        string executedCandidateId)
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
        var visits = candidates.Sum(candidate =>
            Math.Max(0, candidate.SearchVisits));
        var result = new double[candidates.Count];
        if (visits > 0)
        {
            if (executed >= 0 && candidates[executed].SearchVisits <= 0)
            {
                result[executed] = 1d;
                return result;
            }
            for (var index = 0; index < candidates.Count; index++)
            {
                result[index] =
                    (double)Math.Max(0, candidates[index].SearchVisits)
                    / visits;
            }
            return result;
        }
        if (executed >= 0)
        {
            result[executed] = 1d;
            return result;
        }
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = 1d / result.Length;
        }
        return result;
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
        IReadOnlyList<double> input,
        IReadOnlyList<double> weights,
        IReadOnlyList<double> bias,
        int outputs)
    {
        var result = new double[outputs];
        for (var output = 0; output < outputs; output++)
        {
            var value = bias[output];
            var offset = output * input.Count;
            for (var inputIndex = 0;
                 inputIndex < input.Count;
                 inputIndex++)
            {
                value += input[inputIndex] * weights[offset + inputIndex];
            }
            result[output] = Math.Tanh(value);
        }
        return result;
    }

    private static double Interaction(
        IReadOnlyList<double> state,
        IReadOnlyList<double> action,
        IReadOnlyList<double> weights)
    {
        var result = 0d;
        for (var index = 0; index < state.Count; index++)
        {
            result += state[index] * action[index] * weights[index];
        }
        return result;
    }

    private static double[] Softmax(IReadOnlyList<double> logits)
    {
        var maximum = logits.Max();
        var result = new double[logits.Count];
        var total = 0d;
        for (var index = 0; index < logits.Count; index++)
        {
            result[index] = Math.Exp(Clamp(
                logits[index] - maximum,
                -30d,
                30d));
            total += result[index];
        }
        total = Math.Max(0.0000001d, total);
        for (var index = 0; index < result.Length; index++)
        {
            result[index] /= total;
        }
        return result;
    }

    private static double Dot(
        IReadOnlyList<double> left,
        IReadOnlyList<double> right)
    {
        var result = 0d;
        for (var index = 0;
             index < Math.Min(left.Count, right.Count);
             index++)
        {
            result += left[index] * right[index];
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
        public double[] State { get; set; } = Array.Empty<double>();

        public double[][] Actions { get; set; } = Array.Empty<double[]>();

        public double[] PolicyTargets { get; set; } = Array.Empty<double>();

        public int PolicyTargetIndex { get; set; }

        public double LongTermReturn { get; set; }

        public double WinTarget { get; set; }

        public double DeathTarget { get; set; }

        public double HpTarget { get; set; }

        public double TurnsTarget { get; set; }
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
    }

    private sealed class Metrics
    {
        public double PolicyAccuracy { get; set; }

        public double ValueMae { get; set; }

        public double Brier { get; set; }
    }

    private struct FrameMetrics
    {
        public int Correct { get; set; }

        public double ValueError { get; set; }

        public double Brier { get; set; }
    }
}
