using System.Diagnostics;
using System.Globalization;
using System.Text;
using AuraCombatAi.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.Worker;

internal sealed class PythonCombatTransformerTeacher :
    ICombatTransformerTeacher
{
    private const string ProgressPrefix = "AURA_TEACHER_PROGRESS ";
    private readonly string resultDirectory;
    private readonly string scriptPath;
    private readonly string runtimeCachePath;
    private readonly object runtimeProbeGate = new();
    private CombatTransformerRuntimeProbe? cachedRuntimeProbe;
    private string cachedRuntimeKey = "";

    public PythonCombatTransformerTeacher(
        string resultDirectory,
        string scriptPath,
        string? runtimeCachePath = null)
    {
        this.resultDirectory = Path.GetFullPath(resultDirectory);
        this.scriptPath = Path.GetFullPath(scriptPath);
        this.runtimeCachePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(runtimeCachePath)
                ? Path.Combine(
                    this.resultDirectory,
                    "transformer-teacher",
                    "runtime-auto-tune-v1.json")
                : runtimeCachePath);
    }

    public CombatTransformerTeacherReport TrainAndAnnotate(
        CombatTransformerTeacherContext context,
        CancellationToken cancellationToken)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var options = (context.Options ?? new CombatTransformerTeacherOptions())
            .Normalized();
        var report = new CombatTransformerTeacherReport
        {
            Iteration = context.Iteration,
            Requested = true,
            RequestedBackend = options.Backend,
            EpisodeCount = context.Episodes?.Count ?? 0
        };
        if (!File.Exists(scriptPath))
        {
            report.Message = "Transformer teacher script is missing: " + scriptPath;
            return report;
        }
        var runtime = ResolveRuntime(options);
        report.ResolvedPythonExecutable = runtime.ExecutablePath;
        report.RuntimeResolutionSource = runtime.ResolutionSource;
        report.PythonVersion = runtime.PythonVersion;
        report.TorchVersion = runtime.TorchVersion;
        report.NumpyVersion = runtime.NumpyVersion;
        report.EffectiveBackend = runtime.EffectiveBackend;
        report.DeviceName = runtime.DeviceName;
        if (!runtime.Success)
        {
            report.Message = "Transformer runtime unavailable: " + runtime.Message;
            return report;
        }

        var iterationDirectory = Path.Combine(
            resultDirectory,
            "transformer-teacher",
            "iteration-" + Math.Max(1, context.Iteration).ToString("D2"));
        Directory.CreateDirectory(iterationDirectory);
        var datasetPath = Path.Combine(iterationDirectory, "world-model-dataset-v2.jsonl");
        var annotationsPath = Path.Combine(
            iterationDirectory,
            "world-model-annotations-v2.jsonl");
        var modelPath = Path.Combine(iterationDirectory, "world-model-v2.pt");
        var reportPath = Path.Combine(iterationDirectory, "world-model-report-v2.json");
        report.DatasetPath = datasetPath;
        report.ModelPath = modelPath;
        report.ReportPath = reportPath;

        ReportProgress(context, new CombatTransformerTeacherProgress
        {
            Stage = "exporting",
            Message = "正在导出冻结 Replay 数据集"
        });

        var bindings = ExportDataset(
            context.Episodes ?? Array.Empty<CombatEpisode>(),
            options,
            datasetPath,
            cancellationToken);
        report.FrameCount = bindings.Count;
        if (bindings.Count < options.MinimumFrames)
        {
            report.Message = "Transformer teacher skipped: frames="
                             + bindings.Count
                             + ", required="
                             + options.MinimumFrames;
            return report;
        }

        var previousModelPath = Path.Combine(
            resultDirectory,
            "transformer-teacher",
            "iteration-"
            + Math.Max(1, context.Iteration - 1).ToString("D2"),
            "world-model-v2.pt");
        var warmStarted = options.EnableWarmStart
                          && context.Iteration > 1
                          && File.Exists(previousModelPath);
        var finalIteration = context.Iteration >= Math.Max(
            1,
            context.TotalIterations);
        var cpuBackend = string.Equals(
            runtime.EffectiveBackend,
            CombatTransformerTeacherBackendNames.Cpu,
            StringComparison.OrdinalIgnoreCase);
        var trainingEnabled = !warmStarted
                              || !cpuBackend
                              || finalIteration
                              || (context.Iteration - 1)
                              % options.CpuRefreshInterval == 0;
        var effectiveEpochs = finalIteration
            ? options.FinalEpochs
            : warmStarted
                ? options.IncrementalEpochs
                : options.Epochs;
        report.RequestedEpochs = trainingEnabled ? effectiveEpochs : 0;
        report.WarmStarted = warmStarted;
        report.TrainingRefreshed = trainingEnabled;
        report.ResumeModelPath = warmStarted ? previousModelPath : "";
        ReportProgress(context, new CombatTransformerTeacherProgress
        {
            Stage = "launching",
            TotalFrames = bindings.Count,
            TotalEpochs = report.RequestedEpochs,
            WarmStarted = warmStarted,
            TrainingEnabled = trainingEnabled,
            Message = trainingEnabled
                ? warmStarted
                    ? "正在增量刷新 Transformer 教师"
                    : "正在初始化 Transformer 教师"
                : "本轮复用教师权重并重新生成蒸馏标注"
        });

        var process = StartTeacher(
            options,
            runtime.ExecutablePath,
            datasetPath,
            annotationsPath,
            modelPath,
            reportPath,
            warmStarted ? previousModelPath : "",
            trainingEnabled,
            effectiveEpochs);
        try
        {
            var stdout = ReadTeacherOutputAsync(
                process.StandardOutput,
                context,
                bindings.Count,
                report.RequestedEpochs,
                warmStarted,
                trainingEnabled);
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                report.Message = "Transformer teacher process failed ("
                                 + process.ExitCode
                                 + "): "
                                 + Tail(stderr.Result, 4000);
                return report;
            }
            if (!File.Exists(reportPath) || !File.Exists(annotationsPath))
            {
                report.Message = "Transformer teacher did not produce its report and annotations.";
                return report;
            }
            var external = JsonConvert.DeserializeObject<
                CombatTransformerTeacherReport>(
                File.ReadAllText(reportPath, Encoding.UTF8));
            if (external != null)
            {
                MergeExternalReport(report, external);
            }
            ApplyAnnotations(annotationsPath, bindings, report);
            report.Success = report.Success && report.AnnotatedFrames > 0;
            report.PolicyQualityGatePassed =
                report.ValidationUniformPolicyCrossEntropy > 0d
                && !double.IsNaN(report.ValidationPolicyCrossEntropy)
                && !double.IsInfinity(report.ValidationPolicyCrossEntropy)
                && report.ValidationPolicyCrossEntropy
                   <= report.ValidationUniformPolicyCrossEntropy + 0.000001d;
            report.WorldModelQualityGatePassed =
                report.DynamicsTrainingFrames > 0
                && Finite(report.ValidationDynamicsMse)
                && report.ValidationDynamicsMse <= 0.5d
                && Finite(report.ValidationOutcomeMae)
                && report.ValidationOutcomeMae <= 0.5d;
            report.QualityGatePassed = report.PolicyQualityGatePassed
                                       && report.WorldModelQualityGatePassed;
            report.Applied = report.Success
                             && report.QualityGatePassed
                             && report.AnnotatedFrames >= options.MinimumFrames;
            if (report.Applied)
            {
                report.Message = "Transformer teacher annotations applied.";
            }
            else if (report.Success && !report.PolicyQualityGatePassed)
            {
                report.Message =
                    "Transformer teacher withheld: validation policy loss did not beat the uniform baseline.";
            }
            else if (report.Success && !report.WorldModelQualityGatePassed)
            {
                report.Message =
                    "Transformer world model withheld: dynamics or outcome validation gate failed.";
            }
            else if (string.IsNullOrWhiteSpace(report.Message))
            {
                report.Message = "Transformer teacher completed without enough valid annotations.";
            }
            File.WriteAllText(
                reportPath,
                JsonConvert.SerializeObject(report, Formatting.Indented),
                new UTF8Encoding(false));
            return report;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            process.Dispose();
        }
    }

    private CombatTransformerRuntimeProbe ResolveRuntime(
        CombatTransformerTeacherOptions options)
    {
        var key = options.PythonExecutable + "\n" + options.Backend;
        lock (runtimeProbeGate)
        {
            if (cachedRuntimeProbe != null
                && string.Equals(cachedRuntimeKey, key, StringComparison.Ordinal))
            {
                return cachedRuntimeProbe;
            }

            cachedRuntimeProbe = CombatTransformerRuntimeResolver.Resolve(
                options.PythonExecutable,
                options.Backend,
                new[]
                {
                    Path.GetDirectoryName(scriptPath) ?? "",
                    AppContext.BaseDirectory
                });
            cachedRuntimeKey = key;
            return cachedRuntimeProbe;
        }
    }

    private static List<FrameBinding> ExportDataset(
        IReadOnlyList<CombatEpisode> episodes,
        CombatTransformerTeacherOptions options,
        string path,
        CancellationToken cancellationToken)
    {
        var bindings = new List<FrameBinding>();
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(false),
            1024 * 1024);
        for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var episode = episodes[episodeIndex];
            if (episode == null || !episode.Authoritative)
            {
                continue;
            }
            var frames = (episode.Frames ?? new List<CombatEpisodeFrame>())
                .OrderBy(frame => frame.Turn)
                .ThenBy(frame => frame.ActionSequence)
                .ToList();
            for (var episodeFrameIndex = 0;
                 episodeFrameIndex < frames.Count;
                 episodeFrameIndex++)
            {
                var frame = frames[episodeFrameIndex];
                foreach (var candidate in frame.Candidates
                             ?? new List<CombatEpisodeCandidate>())
                {
                    candidate.TransformerTeacherProbability = -1d;
                }
                var candidates = (frame.Candidates
                                  ?? new List<CombatEpisodeCandidate>())
                    .Where(candidate => candidate != null && candidate.Legal)
                    .ToList();
                if (candidates.Count < 2)
                {
                    continue;
                }
                var executedIndex = candidates.FindIndex(candidate =>
                    string.Equals(
                        candidate.CandidateId,
                        frame.ExecutedCandidateId,
                        StringComparison.Ordinal));
                if (executedIndex < 0)
                {
                    continue;
                }
                var policy = CombatPolicyValueBatchTrainer.PolicyTargets(
                    candidates,
                    frame.ExecutedCandidateId,
                    1.25d,
                    0.95d);
                var rowIndex = bindings.Count;
                var hasNextState = episodeFrameIndex + 1 < frames.Count;
                var nextState = hasNextState
                    ? CombatPolicyValueEncoding.EncodeState(
                        frames[episodeFrameIndex + 1].StateFeatures,
                        options.StateDimensions,
                        "partitioned-v3")
                    : new double[options.StateDimensions];
                var row = new TeacherDatasetRow
                {
                    I = rowIndex,
                    E = episodeIndex,
                    F = episodeFrameIndex,
                    T = frame.Turn,
                    Q = frame.ActionSequence,
                    S = CombatPolicyValueEncoding.EncodeState(
                        frame.StateFeatures,
                        options.StateDimensions,
                        "partitioned-v3"),
                    O = CombatWorldModelTokenEncoding.Encode(
                        frame.Observation,
                        options.StateDimensions),
                    A = candidates.Select(candidate =>
                            CombatPolicyValueEncoding.EncodeCandidate(
                                new CombatPolicyValueCandidate
                                {
                                    CandidateId = candidate.CandidateId,
                                    SourceId = candidate.SourceId,
                                    Features = candidate.Features
                                },
                                options.ActionDimensions,
                                "partitioned-v3"))
                        .ToArray(),
                    P = policy,
                    X = executedIndex,
                    V = Math.Max(-1d, Math.Min(1d, frame.LongTermReturn)),
                    G = StrategyStage(frame.StateFeatures),
                    K = string.Equals(
                        CombatPolicyValueBatchTrainer.StrategicFrameStratum(
                            frame.StateFeatures),
                        "strategy-baseline",
                        StringComparison.Ordinal)
                        ? 1d
                        : 2d,
                    N = nextState,
                    M = hasNextState ? 1 : 0,
                    W = Math.Max(0d, Math.Min(1d, frame.WinTarget)),
                    R = Math.Max(0d, Math.Min(1d, frame.DeathTarget)),
                    H = Math.Max(0d, Math.Min(1d, frame.RemainingHpRatioTarget)),
                    U = Math.Max(0d, frame.RemainingTurnsTarget),
                    Z = hasNextState ? 0 : 1
                };
                writer.WriteLine(JsonConvert.SerializeObject(row, Formatting.None));
                bindings.Add(new FrameBinding(frame, candidates));
            }
        }
        return bindings;
    }

    private Process StartTeacher(
        CombatTransformerTeacherOptions options,
        string pythonExecutable,
        string datasetPath,
        string annotationsPath,
        string modelPath,
        string reportPath,
        string resumeModelPath,
        bool trainingEnabled,
        int effectiveEpochs)
    {
        var start = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        Add(start, scriptPath);
        Add(start, "--input", datasetPath);
        Add(start, "--annotations", annotationsPath);
        Add(start, "--model", modelPath);
        Add(start, "--report", reportPath);
        Add(start, "--backend", options.Backend);
        Add(start, "--epochs", trainingEnabled ? effectiveEpochs : 0);
        Add(start, "--batch-size", options.BatchSize);
        Add(start, "--hidden", options.HiddenDimensions);
        Add(start, "--layers", options.Layers);
        Add(start, "--heads", options.AttentionHeads);
        Add(start, "--ffn", options.FeedForwardDimensions);
        Add(start, "--history", options.HistoryLength);
        Add(start, "--cpu-threads", options.CpuThreads);
        Add(start, "--cpu-interop-threads", options.CpuInteropThreads);
        Add(start, "--micro-batch-size", options.MicroBatchSize);
        Add(start, "--loader-workers", options.DataLoaderWorkers);
        Add(start, "--prefetch-batches", options.PrefetchBatches);
        Add(start, "--pin-memory", options.EnablePinnedMemory ? 1 : 0);
        Add(start, "--mixed-precision", options.EnableMixedPrecision ? 1 : 0);
        Add(start, "--runtime-cache", runtimeCachePath);
        if (!string.IsNullOrWhiteSpace(resumeModelPath))
        {
            Add(start, "--resume-model", resumeModelPath);
        }
        Add(start, "--training-enabled", trainingEnabled ? 1 : 0);
        Add(start, "--seed", options.RandomSeed);
        return Process.Start(start)
               ?? throw new InvalidOperationException(
                   "Could not start Transformer teacher process.");
    }

    private static void ApplyAnnotations(
        string path,
        IReadOnlyList<FrameBinding> bindings,
        CombatTransformerTeacherReport report)
    {
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var annotation = JsonConvert.DeserializeObject<TeacherAnnotation>(line);
            if (annotation == null
                || annotation.I < 0
                || annotation.I >= bindings.Count)
            {
                continue;
            }
            var binding = bindings[annotation.I];
            if (annotation.P == null
                || annotation.P.Length != binding.Candidates.Count
                || annotation.P.Any(value =>
                    double.IsNaN(value)
                    || double.IsInfinity(value)
                    || value < 0d))
            {
                continue;
            }
            var total = annotation.P.Sum();
            if (total <= 0d)
            {
                continue;
            }
            for (var index = 0; index < annotation.P.Length; index++)
            {
                binding.Candidates[index].TransformerTeacherProbability =
                    annotation.P[index] / total;
            }
            report.AnnotatedFrames++;
            report.AnnotatedCandidates += annotation.P.Length;
        }
    }

    private static int StrategyStage(
        IReadOnlyDictionary<string, double>? features)
    {
        if (features != null
            && features.TryGetValue(
                CombatRoleStrategyFeatureNames.Phase,
                out var phase)
            && !double.IsNaN(phase)
            && !double.IsInfinity(phase))
        {
            return Math.Max(0, Math.Min(4, (int)Math.Round(phase)));
        }
        return -1;
    }

    private static void MergeExternalReport(
        CombatTransformerTeacherReport target,
        CombatTransformerTeacherReport source)
    {
        target.Success = source.Success;
        target.EffectiveBackend = source.EffectiveBackend;
        target.DeviceName = source.DeviceName;
        target.PythonVersion = source.PythonVersion;
        target.TorchVersion = source.TorchVersion;
        target.NumpyVersion = source.NumpyVersion;
        target.RuntimeAutoTuned = source.RuntimeAutoTuned;
        target.RuntimeAutoTuneCacheHit = source.RuntimeAutoTuneCacheHit;
        target.EffectiveCpuThreads = source.EffectiveCpuThreads;
        target.EffectiveCpuInteropThreads = source.EffectiveCpuInteropThreads;
        target.EffectiveBatchSize = source.EffectiveBatchSize;
        target.EffectiveMicroBatchSize = source.EffectiveMicroBatchSize;
        target.EffectiveDataLoaderWorkers =
            source.EffectiveDataLoaderWorkers;
        target.EffectivePrefetchBatches = source.EffectivePrefetchBatches;
        target.PinnedMemoryEnabled = source.PinnedMemoryEnabled;
        target.NumericPrecision = source.NumericPrecision;
        target.ParameterCount = source.ParameterCount;
        target.HiddenDimensions = source.HiddenDimensions;
        target.Layers = source.Layers;
        target.AttentionHeads = source.AttentionHeads;
        target.FeedForwardDimensions = source.FeedForwardDimensions;
        target.TrainingFrames = source.TrainingFrames;
        target.ValidationFrames = source.ValidationFrames;
        target.EpochsExecuted = source.EpochsExecuted;
        target.RequestedEpochs = source.RequestedEpochs;
        target.WarmStarted = source.WarmStarted;
        target.TrainingRefreshed = source.TrainingRefreshed;
        target.ResumeModelPath = source.ResumeModelPath;
        target.ValidationPolicyCrossEntropy =
            source.ValidationPolicyCrossEntropy;
        target.ValidationUniformPolicyCrossEntropy =
            source.ValidationUniformPolicyCrossEntropy;
        target.ValidationPolicyTop1Accuracy =
            source.ValidationPolicyTop1Accuracy;
        target.ValidationValueMae = source.ValidationValueMae;
        target.ValidationStrategyAccuracy =
            source.ValidationStrategyAccuracy;
        target.ValidationDynamicsMse = source.ValidationDynamicsMse;
        target.DynamicsTrainingFrames = source.DynamicsTrainingFrames;
        target.ValidationOutcomeMae = source.ValidationOutcomeMae;
        target.ValidationDeathBrier = source.ValidationDeathBrier;
        target.ValidationTerminalAccuracy = source.ValidationTerminalAccuracy;
        target.ElapsedSeconds = source.ElapsedSeconds;
        target.DataPreparationSeconds = source.DataPreparationSeconds;
        target.RuntimeCalibrationSeconds = source.RuntimeCalibrationSeconds;
        target.TrainingFramesPerSecond = source.TrainingFramesPerSecond;
        target.PeakDeviceMemoryBytes = source.PeakDeviceMemoryBytes;
        target.Message = source.Message;
    }

    private static async Task<string> ReadTeacherOutputAsync(
        StreamReader reader,
        CombatTransformerTeacherContext context,
        int totalFrames,
        int totalEpochs,
        bool warmStarted,
        bool trainingEnabled)
    {
        var tail = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            {
                try
                {
                    var progress = JsonConvert.DeserializeObject<
                        CombatTransformerTeacherProgress>(
                        line.Substring(ProgressPrefix.Length));
                    if (progress != null)
                    {
                        if (progress.TotalFrames <= 0)
                        {
                            progress.TotalFrames = totalFrames;
                        }
                        if (progress.TotalEpochs <= 0
                            && trainingEnabled
                            && !string.Equals(
                                progress.Stage,
                                "annotating",
                                StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(
                                progress.Stage,
                                "completed",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            progress.TotalEpochs = totalEpochs;
                        }
                        progress.WarmStarted = warmStarted;
                        progress.TrainingEnabled = trainingEnabled;
                        ReportProgress(context, progress);
                    }
                }
                catch
                {
                    // A malformed diagnostic line must not abort training.
                }
                continue;
            }
            tail.AppendLine(line);
            if (tail.Length > 8000)
            {
                tail.Remove(0, tail.Length - 8000);
            }
        }
        return tail.ToString();
    }

    private static void ReportProgress(
        CombatTransformerTeacherContext context,
        CombatTransformerTeacherProgress progress)
    {
        progress.Iteration = Math.Max(1, context.Iteration);
        progress.TotalIterations = Math.Max(
            progress.Iteration,
            context.TotalIterations);
        try
        {
            context.Progress?.Invoke(progress);
        }
        catch
        {
            // Independent progress reporting must not abort the teacher.
        }
    }

    private static void Add(ProcessStartInfo start, params object[] values)
    {
        foreach (var value in values)
        {
            start.ArgumentList.Add(Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ?? "");
        }
    }

    private static string Tail(string value, int maximum)
    {
        var safe = value ?? "";
        return safe.Length <= maximum ? safe : safe[^maximum..];
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cancellation cleanup must not hide the cancellation itself.
        }
    }

    private sealed record FrameBinding(
        CombatEpisodeFrame Frame,
        List<CombatEpisodeCandidate> Candidates);

    private sealed class TeacherDatasetRow
    {
        public int I { get; set; }
        public int E { get; set; }
        public int F { get; set; }
        public int T { get; set; }
        public long Q { get; set; }
        public double[] S { get; set; } = Array.Empty<double>();
        public double[][] O { get; set; } = Array.Empty<double[]>();
        public double[][] A { get; set; } = Array.Empty<double[]>();
        public double[] P { get; set; } = Array.Empty<double>();
        public int X { get; set; }
        public double V { get; set; }
        public int G { get; set; }

        public double K { get; set; } = 1d;
        public double[] N { get; set; } = Array.Empty<double>();
        public int M { get; set; }
        public double W { get; set; }
        public double R { get; set; }
        public double H { get; set; }
        public double U { get; set; }
        public int Z { get; set; }
    }

    private sealed class TeacherAnnotation
    {
        public int I { get; set; }
        public double[] P { get; set; } = Array.Empty<double>();
    }
}
