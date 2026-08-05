using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
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
    private readonly string corpusRoot;
    private readonly object runtimeProbeGate = new();
    private CombatTransformerRuntimeProbe? cachedRuntimeProbe;
    private string cachedRuntimeKey = "";

    public PythonCombatTransformerTeacher(
        string resultDirectory,
        string scriptPath,
        string? runtimeCachePath = null,
        string? corpusRoot = null)
    {
        this.resultDirectory = Path.GetFullPath(resultDirectory);
        this.scriptPath = Path.GetFullPath(scriptPath);
        this.runtimeCachePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(runtimeCachePath)
                ? Path.Combine(
                    this.resultDirectory,
                    "transformer-teacher",
                    "runtime-auto-tune-v2.json")
                : runtimeCachePath);
        this.corpusRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(corpusRoot)
                ? Path.Combine(this.resultDirectory, "transformer-teacher-corpus")
                : corpusRoot);
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
        var currentDatasetPath = Path.Combine(
            iterationDirectory,
            "world-model-current-v3.jsonl");
        var corpusCompatibilityKey = SafeCompatibilityKey(
            context.CorpusCompatibilityKey,
            "corpus");
        var teacherCompatibilityKey = SafeCompatibilityKey(
            context.TeacherCompatibilityKey,
            "teacher");
        var corpusDirectory = Path.Combine(
            corpusRoot,
            "corpora",
            corpusCompatibilityKey);
        Directory.CreateDirectory(corpusDirectory);
        var datasetPath = Path.Combine(
            corpusDirectory,
            "world-model-corpus-v3.jsonl");
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

        report.CorpusCompatibilityKey = corpusCompatibilityKey;
        report.TeacherCompatibilityKey = teacherCompatibilityKey;
        var bindings = ExportDataset(
            context.Episodes ?? Array.Empty<CombatEpisode>(),
            options,
            currentDatasetPath,
            context,
            cancellationToken);
        ReportProgress(context, new CombatTransformerTeacherProgress
        {
            Stage = "merging",
            CompletedFrames = 0,
            TotalFrames = Math.Max(1, bindings.Count),
            Message = "正在去重并合并累计 Transformer 语料"
        });
        var corpus = MergeCorpus(
            datasetPath,
            currentDatasetPath,
            corpusDirectory,
            corpusCompatibilityKey,
            bindings,
            options.MaximumFrames,
            cancellationToken);
        ReportProgress(context, new CombatTransformerTeacherProgress
        {
            Stage = "merged",
            CompletedFrames = corpus.FrameCount,
            TotalFrames = corpus.FrameCount,
            Message = "累计 Transformer 语料已就绪"
        });
        report.FrameCount = corpus.FrameCount;
        report.CurrentFrameCount = corpus.CurrentFrames;
        report.ReusedCorpusFrames = corpus.ReusedFrames;
        report.DeduplicatedCorpusFrames = corpus.DeduplicatedFrames;
        report.DroppedCorpusFrames = corpus.DroppedFrames;
        report.DatasetStrategyFrames = corpus.StrategyFrames;
        report.DatasetFingerprint = corpus.Fingerprint;
        TryDelete(currentDatasetPath);
        if (corpus.FrameCount < options.MinimumFrames)
        {
            report.Message = "Transformer teacher skipped: frames="
                             + corpus.FrameCount
                             + ", required="
                             + options.MinimumFrames;
            return report;
        }

        var iterationPreviousModelPath = Path.Combine(
            resultDirectory,
            "transformer-teacher",
            "iteration-"
            + Math.Max(1, context.Iteration - 1).ToString("D2"),
            "world-model-v2.pt");
        var iterationPreviousReportPath = Path.Combine(
            resultDirectory,
            "transformer-teacher",
            "iteration-"
            + Math.Max(1, context.Iteration - 1).ToString("D2"),
            "world-model-report-v2.json");
        var persistentTeacherDirectory = Path.Combine(
            corpusRoot,
            "teachers",
            teacherCompatibilityKey);
        Directory.CreateDirectory(persistentTeacherDirectory);
        var persistentModelPath = Path.Combine(
            persistentTeacherDirectory,
            "world-model-v2.pt");
        var persistentReportPath = Path.Combine(
            persistentTeacherDirectory,
            "world-model-report-v2.json");
        var previousModelPath = File.Exists(iterationPreviousModelPath)
            ? iterationPreviousModelPath
            : persistentModelPath;
        var previousReportPath = File.Exists(iterationPreviousReportPath)
            ? iterationPreviousReportPath
            : persistentReportPath;
        var previousReport = ReadPreviousReport(previousReportPath);
        report.DatasetDriftScore = DatasetDrift(report, previousReport);
        var warmStarted = options.EnableWarmStart
                          && File.Exists(previousModelPath);
        var finalIteration = context.Iteration >= Math.Max(
            1,
            context.TotalIterations);
        var cpuBackend = string.Equals(
            runtime.EffectiveBackend,
            CombatTransformerTeacherBackendNames.Cpu,
            StringComparison.OrdinalIgnoreCase);
        var intervalRefresh = (context.Iteration - 1)
                              % options.CpuRefreshInterval == 0;
        var driftRefresh = options.EnableAdaptiveRefresh
                           && report.DatasetDriftScore
                              >= options.AdaptiveRefreshDriftThreshold;
        var trainingEnabled = !warmStarted
                              || !cpuBackend
                              || finalIteration
                              || intervalRefresh
                              || driftRefresh;
        report.RefreshReason = !warmStarted
            ? "cold-start"
            : !cpuBackend
                ? "accelerator-refresh"
                : finalIteration
                    ? "final-refresh"
                    : driftRefresh
                        ? "dataset-drift"
                        : intervalRefresh
                            ? "maximum-staleness"
                            : "stable-teacher-reuse";
        var effectiveEpochs = cpuBackend
            ? finalIteration
                ? options.CpuFinalEpochs
                : warmStarted
                    ? options.CpuIncrementalEpochs
                    : options.CpuEpochs
            : finalIteration
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
            TotalFrames = corpus.FrameCount,
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
            effectiveEpochs,
            Path.Combine(
                persistentTeacherDirectory,
                "fixed-anchor-validation-v1.jsonl"));
        try
        {
            var stdout = ReadTeacherOutputAsync(
                process.StandardOutput,
                context,
                corpus.FrameCount,
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
                             && report.FrameCount >= options.MinimumFrames
                             && report.AnnotatedFrames > 0;
            if (report.Applied)
            {
                report.Message = report.TrainingRefreshed
                                 && !report.UpdateAccepted
                    ? "Transformer update rejected by the fixed-anchor gate; stable teacher annotations applied."
                    : "Transformer teacher annotations applied.";
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
            if (report.Success && File.Exists(modelPath))
            {
                CopyAtomic(modelPath, persistentModelPath);
                CopyAtomic(reportPath, persistentReportPath);
            }
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
        CombatTransformerTeacherContext context,
        CancellationToken cancellationToken)
    {
        var bindings = new List<FrameBinding>();
        var totalFrames = episodes.Sum(episode =>
            episode?.Frames?.Count ?? 0);
        var completedFrames = 0;
        var started = Stopwatch.StartNew();
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
                completedFrames++;
                if (completedFrames % 64 == 0)
                {
                    var rate = completedFrames
                               / Math.Max(
                                   0.001d,
                                   started.Elapsed.TotalSeconds);
                    ReportProgress(
                        context,
                        new CombatTransformerTeacherProgress
                        {
                            Stage = "exporting",
                            CompletedFrames = completedFrames,
                            TotalFrames = totalFrames,
                            FramesPerSecond = rate,
                            EstimatedRemainingSeconds = Math.Max(
                                0,
                                totalFrames - completedFrames) / rate,
                            Message = "正在导出冻结 Replay 数据集"
                        });
                }
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
                if (candidates.Count == 0)
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
                var runKey = StableRunKey(episode);
                var identity = runKey
                               + "|"
                               + episode.JourneyBattleIndex
                               + "|"
                               + frame.Turn
                               + "|"
                               + frame.ActionSequence
                               + "|"
                               + frame.StateFingerprint;
                var strategy = CombatPolicyValueBatchTrainer
                    .StrategicFrameStratum(frame.StateFeatures);
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
                    Y = runKey,
                    F = episodeFrameIndex,
                    T = frame.Turn,
                    Q = frame.ActionSequence,
                    D = identity,
                    L = strategy,
                    C = NormalizeDifficulty(episode.Campaign?.DifficultyId),
                    B = episode.JourneyBattleIndex,
                    J = episode.Campaign?.FinalBossVictory == true ? 1 : 0,
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
                    K = HasDeclaredTrainingQuota(frame.StateFeatures)
                        || string.Equals(
                            strategy,
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
                bindings.Add(new FrameBinding
                {
                    Frame = frame,
                    Candidates = candidates,
                    Strategy = strategy,
                    Identity = identity
                });
            }
        }
        ReportProgress(context, new CombatTransformerTeacherProgress
        {
            Stage = "exporting",
            CompletedFrames = totalFrames,
            TotalFrames = totalFrames,
            Message = "冻结 Replay 数据集已导出"
        });
        return bindings;
    }

    private static CorpusMergeResult MergeCorpus(
        string corpusPath,
        string currentPath,
        string corpusDirectory,
        string compatibilityKey,
        IReadOnlyList<FrameBinding> bindings,
        int maximumFrames,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, CorpusFrameDescriptor>(
            StringComparer.Ordinal);
        var observedRows = 0;
        if (File.Exists(corpusPath))
        {
            observedRows += ReadCorpusDescriptors(
                corpusPath,
                current: false,
                candidates,
                cancellationToken);
        }
        observedRows += ReadCorpusDescriptors(
            currentPath,
            current: true,
            candidates,
            cancellationToken);
        var capacity = Math.Min(
            Math.Max(64, maximumFrames),
            candidates.Count);
        var selected = SelectCorpusFrames(candidates.Values, capacity);
        var selectedIdentities = selected
            .Select(item => item.Identity)
            .ToHashSet(StringComparer.Ordinal);
        var currentBindings = bindings
            .GroupBy(binding => binding.Identity, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            binding.RowIndex = -1;
        }

        var temporaryPath = corpusPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var rowIndex = 0;
        try
        {
            using (var writer = new StreamWriter(
                       temporaryPath,
                       append: false,
                       new UTF8Encoding(false),
                       1024 * 1024))
            {
                if (File.Exists(corpusPath))
                {
                    WriteSelectedCorpusRows(
                        corpusPath,
                        current: false,
                        selectedIdentities,
                        candidates,
                        currentBindings,
                        writer,
                        ref rowIndex,
                        cancellationToken);
                }
                WriteSelectedCorpusRows(
                    currentPath,
                    current: true,
                    selectedIdentities,
                    candidates,
                    currentBindings,
                    writer,
                    ref rowIndex,
                    cancellationToken);
            }
            File.Move(temporaryPath, corpusPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }

        var result = new CorpusMergeResult
        {
            FrameCount = selected.Count,
            CurrentFrames = selected.Count(item => item.Current),
            ReusedFrames = selected.Count(item => !item.Current),
            DeduplicatedFrames = Math.Max(0, observedRows - candidates.Count),
            DroppedFrames = Math.Max(0, candidates.Count - selected.Count),
            Fingerprint = DatasetFingerprint(selected.Select(item => item.Identity)),
            StrategyFrames = selected
                .GroupBy(item => item.Strategy, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal)
        };
        var manifest = new TeacherCorpusManifest
        {
            CompatibilityKey = compatibilityKey,
            FrameCount = result.FrameCount,
            Fingerprint = result.Fingerprint,
            StrategyFrames = result.StrategyFrames,
            UpdatedUtc = DateTime.UtcNow
        };
        File.WriteAllText(
            Path.Combine(corpusDirectory, "corpus-manifest-v1.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented),
            new UTF8Encoding(false));
        return result;
    }

    private static int ReadCorpusDescriptors(
        string path,
        bool current,
        IDictionary<string, CorpusFrameDescriptor> destination,
        CancellationToken cancellationToken)
    {
        var lineIndex = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(line))
            {
                var descriptor = ReadCorpusDescriptor(line, path, lineIndex, current);
                if (descriptor != null)
                {
                    // The current task owns the newest semantic copy and must
                    // replace an older row with the same frame fingerprint.
                    if (current || !destination.ContainsKey(descriptor.Identity))
                    {
                        destination[descriptor.Identity] = descriptor;
                    }
                }
            }
            lineIndex++;
        }
        return lineIndex;
    }

    private static CorpusFrameDescriptor? ReadCorpusDescriptor(
        string line,
        string path,
        int lineIndex,
        bool current)
    {
        var descriptor = new CorpusFrameDescriptor
        {
            SourcePath = path,
            SourceLine = lineIndex,
            Current = current
        };
        using var text = new StringReader(line);
        using var reader = new JsonTextReader(text);
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName)
            {
                continue;
            }
            var property = Convert.ToString(reader.Value, CultureInfo.InvariantCulture)
                           ?? "";
            if (!reader.Read())
            {
                break;
            }
            switch (property)
            {
                case "D":
                    descriptor.Identity = Convert.ToString(
                        reader.Value,
                        CultureInfo.InvariantCulture) ?? "";
                    break;
                case "L":
                    descriptor.Strategy = Convert.ToString(
                        reader.Value,
                        CultureInfo.InvariantCulture) ?? "strategy-baseline";
                    break;
                case "C":
                    descriptor.Difficulty = NormalizeDifficulty(
                        Convert.ToString(reader.Value, CultureInfo.InvariantCulture));
                    break;
                case "B":
                    descriptor.BattleIndex = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case "J":
                    descriptor.Victory = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture) != 0;
                    break;
                case "S":
                    return string.IsNullOrWhiteSpace(descriptor.Identity)
                        ? null
                        : descriptor;
            }
        }
        return string.IsNullOrWhiteSpace(descriptor.Identity) ? null : descriptor;
    }

    private static List<CorpusFrameDescriptor> SelectCorpusFrames(
        IEnumerable<CorpusFrameDescriptor> source,
        int capacity)
    {
        var all = source.ToList();
        if (all.Count <= capacity)
        {
            return all;
        }
        var selected = new Dictionary<string, CorpusFrameDescriptor>(
            StringComparer.Ordinal);
        var advancedTarget = Math.Min(
            all.Count(IsAdvanced),
            (int)Math.Ceiling(capacity * 0.40d));
        AddStratified(
            selected,
            all.Where(IsAdvanced),
            advancedTarget);
        AddStratified(selected, all, capacity);
        return selected.Values.ToList();
    }

    private static void AddStratified(
        IDictionary<string, CorpusFrameDescriptor> selected,
        IEnumerable<CorpusFrameDescriptor> source,
        int target)
    {
        var groups = source
            .Where(item => !selected.ContainsKey(item.Identity))
            .GroupBy(CorpusStratum, StringComparer.Ordinal)
            .OrderBy(group => CorpusPriority(group.First()))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Queue<CorpusFrameDescriptor>(group
                .OrderBy(StableCorpusRank)
                .ThenBy(item => item.Identity, StringComparer.Ordinal)))
            .ToList();
        while (selected.Count < target && groups.Count > 0)
        {
            var added = false;
            foreach (var group in groups)
            {
                while (group.Count > 0)
                {
                    var item = group.Dequeue();
                    if (selected.ContainsKey(item.Identity))
                    {
                        continue;
                    }
                    selected[item.Identity] = item;
                    added = true;
                    break;
                }
                if (selected.Count >= target)
                {
                    break;
                }
            }
            groups.RemoveAll(group => group.Count == 0);
            if (!added)
            {
                break;
            }
        }
    }

    private static void WriteSelectedCorpusRows(
        string path,
        bool current,
        IReadOnlySet<string> selectedIdentities,
        IReadOnlyDictionary<string, CorpusFrameDescriptor> descriptors,
        IReadOnlyDictionary<string, FrameBinding> currentBindings,
        TextWriter writer,
        ref int outputRowIndex,
        CancellationToken cancellationToken)
    {
        var descriptorsByLine = descriptors.Values
            .Where(item => item.Current == current
                           && string.Equals(
                               item.SourcePath,
                               path,
                               StringComparison.Ordinal))
            .ToDictionary(item => item.SourceLine);
        var lineIndex = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (descriptorsByLine.TryGetValue(lineIndex, out var descriptor)
                && selectedIdentities.Contains(descriptor.Identity))
            {
                writer.WriteLine(RewriteRowIndex(line, outputRowIndex));
                if (current
                    && currentBindings.TryGetValue(
                        descriptor.Identity,
                        out var binding))
                {
                    binding.RowIndex = outputRowIndex;
                }
                outputRowIndex++;
            }
            lineIndex++;
        }
    }

    private static string RewriteRowIndex(string line, int rowIndex)
    {
        const string prefix = "{\"I\":";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return line;
        }
        var comma = line.IndexOf(',', prefix.Length);
        return comma < 0
            ? line
            : prefix
              + rowIndex.ToString(CultureInfo.InvariantCulture)
              + line.Substring(comma);
    }

    private static string CorpusStratum(CorpusFrameDescriptor item)
    {
        return item.Difficulty
               + "|"
               + (item.BattleIndex is >= 1 and <= 3 ? "local-1-3" : "other")
               + "|"
               + (item.Victory ? "victory" : "defeat")
               + "|"
               + item.Strategy;
    }

    private static int CorpusPriority(CorpusFrameDescriptor item)
    {
        if (IsAdvanced(item) && item.BattleIndex is >= 1 and <= 3)
        {
            return 0;
        }
        if (IsAdvanced(item) && !item.Victory)
        {
            return 1;
        }
        return IsAdvanced(item) ? 2 : 3;
    }

    private static bool IsAdvanced(CorpusFrameDescriptor item)
    {
        return string.Equals(
            item.Difficulty,
            "advanced",
            StringComparison.Ordinal);
    }

    private static ulong StableCorpusRank(CorpusFrameDescriptor item)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(item.Identity));
        return BitConverter.ToUInt64(hash, 0);
    }

    private static string StableRunKey(CombatEpisode episode)
    {
        return string.IsNullOrWhiteSpace(episode.JourneyRunId)
            ? "episode:" + (episode.EpisodeId ?? "")
            : "journey:" + episode.JourneyRunId;
    }

    private static bool HasDeclaredTrainingQuota(
        IReadOnlyDictionary<string, double>? features)
    {
        return features != null && features.Any(pair =>
            pair.Value > 0d
            && pair.Key.StartsWith(
                CombatRoleStrategyFeatureNames.TrainingQuotaPrefix,
                StringComparison.OrdinalIgnoreCase));
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
        int effectiveEpochs,
        string anchorPath)
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
        Add(
            start,
            "--anchor",
            anchorPath);
        Add(
            start,
            "--fixed-anchor",
            options.EnableFixedAnchorValidation ? 1 : 0);
        Add(
            start,
            "--maximum-head-regression",
            options.MaximumHeadRegression);
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
        var byRowIndex = bindings
            .Where(binding => binding.RowIndex >= 0)
            .GroupBy(binding => binding.RowIndex)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var annotation = JsonConvert.DeserializeObject<TeacherAnnotation>(line);
            if (annotation == null
                || annotation.I < 0
                || !byRowIndex.TryGetValue(annotation.I, out var binding))
            {
                continue;
            }
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
        target.UpdateAccepted = source.UpdateAccepted;
        target.TeacherGeneration = source.TeacherGeneration;
        target.AnchorValidationFrames = source.AnchorValidationFrames;
        target.AnchorCreated = source.AnchorCreated;
        target.AnchorPath = source.AnchorPath;
        target.BaselinePolicyCrossEntropy =
            source.BaselinePolicyCrossEntropy;
        target.BaselineValueMae = source.BaselineValueMae;
        target.BaselineOutcomeMae = source.BaselineOutcomeMae;
        target.BaselineDeathBrier = source.BaselineDeathBrier;
        target.ValidationCompositeScore = source.ValidationCompositeScore;
        target.BaselineCompositeScore = source.BaselineCompositeScore;
        target.CompositeImprovement = source.CompositeImprovement;
        target.HeadRegressionGatePassed =
            source.HeadRegressionGatePassed;
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
        target.ProcessCpuSeconds = source.ProcessCpuSeconds;
        target.PeakWorkingSetBytes = source.PeakWorkingSetBytes;
        target.DataLoadingSeconds = source.DataLoadingSeconds;
        target.DataPreparationSeconds = source.DataPreparationSeconds;
        target.RuntimeCalibrationSeconds = source.RuntimeCalibrationSeconds;
        target.TrainingSeconds = source.TrainingSeconds;
        target.EvaluationSeconds = source.EvaluationSeconds;
        target.AnnotationSeconds = source.AnnotationSeconds;
        target.SavingSeconds = source.SavingSeconds;
        target.StageSeconds = new Dictionary<string, double>(
            source.StageSeconds
            ?? new Dictionary<string, double>(),
            StringComparer.OrdinalIgnoreCase);
        target.TrainingFramesPerSecond = source.TrainingFramesPerSecond;
        target.AnnotationFramesPerSecond =
            source.AnnotationFramesPerSecond;
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

    private static CombatTransformerTeacherReport? ReadPreviousReport(
        string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<CombatTransformerTeacherReport>(
                    File.ReadAllText(path, Encoding.UTF8))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static double DatasetDrift(
        CombatTransformerTeacherReport current,
        CombatTransformerTeacherReport? previous)
    {
        if (previous == null || previous.FrameCount <= 0)
        {
            return 1d;
        }
        var currentStrategies = current.DatasetStrategyFrames
                                ?? new Dictionary<string, int>();
        var previousStrategies = previous.DatasetStrategyFrames
                                 ?? new Dictionary<string, int>();
        var keys = currentStrategies.Keys
            .Concat(previousStrategies.Keys)
            .Distinct(StringComparer.Ordinal);
        var totalVariation = 0d;
        foreach (var key in keys)
        {
            var currentShare = currentStrategies
                                   .GetValueOrDefault(key)
                               / (double)Math.Max(1, current.FrameCount);
            var previousShare = previousStrategies
                                    .GetValueOrDefault(key)
                                / (double)Math.Max(1, previous.FrameCount);
            totalVariation += Math.Abs(currentShare - previousShare);
        }
        var frameDrift = Math.Abs(current.FrameCount - previous.FrameCount)
                         / (double)Math.Max(
                             current.FrameCount,
                             previous.FrameCount);
        return Math.Max(frameDrift, totalVariation * 0.5d);
    }

    private static string DatasetFingerprint(
        IEnumerable<string> identities)
    {
        var payload = string.Join(
            "\n",
            identities.OrderBy(identity => identity, StringComparer.Ordinal));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string NormalizeDifficulty(string? value)
    {
        return string.Equals(
            (value ?? "").Trim(),
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
    }

    private static string SafeCompatibilityKey(string? value, string fallback)
    {
        var normalized = (value ?? "").Trim().ToUpperInvariant();
        if (normalized.Length == 64
            && normalized.All(character =>
                character is >= '0' and <= '9'
                || character is >= 'A' and <= 'F'))
        {
            return normalized;
        }
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(fallback + "|" + normalized)));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary diagnostics must not abort an otherwise valid run.
        }
    }

    private static void CopyAtomic(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private sealed class CorpusFrameDescriptor
    {
        public string Identity { get; set; } = "";

        public string Strategy { get; set; } = "strategy-baseline";

        public string Difficulty { get; set; } = "normal";

        public int BattleIndex { get; set; }

        public bool Victory { get; set; }

        public string SourcePath { get; set; } = "";

        public int SourceLine { get; set; }

        public bool Current { get; set; }
    }

    private sealed class CorpusMergeResult
    {
        public int FrameCount { get; set; }

        public int CurrentFrames { get; set; }

        public int ReusedFrames { get; set; }

        public int DeduplicatedFrames { get; set; }

        public int DroppedFrames { get; set; }

        public string Fingerprint { get; set; } = "";

        public Dictionary<string, int> StrategyFrames { get; set; } =
            new(StringComparer.Ordinal);
    }

    private sealed class TeacherCorpusManifest
    {
        public string Protocol { get; set; } =
            CombatTransformerTeacherCorpusProtocol.Version;

        public string CompatibilityKey { get; set; } = "";

        public int FrameCount { get; set; }

        public string Fingerprint { get; set; } = "";

        public Dictionary<string, int> StrategyFrames { get; set; } =
            new(StringComparer.Ordinal);

        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class FrameBinding
    {
        public CombatEpisodeFrame Frame { get; set; } = new();

        public List<CombatEpisodeCandidate> Candidates { get; set; } = new();

        public string Strategy { get; set; } = "";

        public string Identity { get; set; } = "";

        public int RowIndex { get; set; } = -1;
    }

    private sealed class TeacherDatasetRow
    {
        public int I { get; set; }
        public int E { get; set; }
        public string Y { get; set; } = "";
        public int F { get; set; }
        public int T { get; set; }
        public long Q { get; set; }
        public string D { get; set; } = "";
        public string L { get; set; } = "";
        public string C { get; set; } = "normal";
        public int B { get; set; }
        public int J { get; set; }
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
