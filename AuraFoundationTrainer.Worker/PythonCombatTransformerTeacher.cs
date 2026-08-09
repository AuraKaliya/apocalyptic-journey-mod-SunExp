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
    private const string CorpusFileName =
        "world-model-corpus-v4.sparse.jsonl";
    private const string CorpusBacklogFileName =
        "world-model-backlog-v1.sparse.jsonl";
    private const string CorpusIdentityIndexFileName =
        "corpus-identities-v1.txt";
    private const string CorpusManifestFileName = "corpus-manifest-v1.json";
    private const string CorpusGenerationDirectoryName = "generations-v1";
    private const string CorpusActiveGenerationFileName =
        "active-generation-v1.json";
    private const string CorpusGenerationPointerProtocol =
        "aura.transformer-teacher-corpus-generation-pointer.v1";
    private const int RetainedCorpusGenerations = 2;
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
                    "runtime-auto-tune-v4.json")
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
            CombatTransformerTeacherFailureProtocol.Mark(
                report,
                CombatTransformerTeacherFailureKinds.Configuration,
                retryable: false,
                formalModelBlocked: true);
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
            CombatTransformerTeacherFailureProtocol.Mark(
                report,
                CombatTransformerTeacherFailureKinds.Configuration,
                retryable: false,
                formalModelBlocked: true);
            return report;
        }

        var iterationDirectory = Path.Combine(
            resultDirectory,
            "transformer-teacher",
            "iteration-" + Math.Max(1, context.Iteration).ToString("D2"));
        Directory.CreateDirectory(iterationDirectory);
        var currentDatasetPath = Path.Combine(
            iterationDirectory,
            "world-model-current-v4.sparse.jsonl");
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
        var persistentTeacherDirectory = Path.Combine(
            corpusRoot,
            "teachers",
            teacherCompatibilityKey);
        Directory.CreateDirectory(persistentTeacherDirectory);
        var persistentModelPath = Path.Combine(
            persistentTeacherDirectory,
            "policy-teacher-v1.pt");
        var persistentReportPath = Path.Combine(
            persistentTeacherDirectory,
            "policy-teacher-report-v1.json");
        var persistentWorldModelPath = Path.Combine(
            persistentTeacherDirectory,
            "world-teacher-v1.pt");
        var persistentWorldReportPath = Path.Combine(
            persistentTeacherDirectory,
            "world-teacher-report-v1.json");
        var anchorPath = Path.Combine(
            persistentTeacherDirectory,
            "fixed-anchor-validation-v1.jsonl");
        var trainedIdentityPath = Path.Combine(
            persistentTeacherDirectory,
            "trained-frame-identities-v1.txt");
        var attemptedIdentityPath = Path.Combine(
            persistentTeacherDirectory,
            "attempted-frame-identities-v1.txt");
        var trainedIdentities = ReadIdentitySet(trainedIdentityPath);
        var attemptedIdentities = ReadIdentitySet(attemptedIdentityPath);
        var anchorRunKeys = ReadAnchorRunKeys(anchorPath, cancellationToken);
        var legacyDatasetPath = Path.Combine(
            corpusDirectory,
            CorpusFileName);
        var legacyCorpusManifestPath = Path.Combine(
            corpusDirectory,
            CorpusManifestFileName);
        var activeCorpusGeneration = ResolveOrMigrateActiveCorpusGeneration(
            corpusDirectory,
            corpusCompatibilityKey,
            cancellationToken);
        var datasetPath = activeCorpusGeneration?.CorpusPath
                          ?? legacyDatasetPath;
        var backlogPath = activeCorpusGeneration?.BacklogPath
                          ?? Path.Combine(corpusDirectory, CorpusBacklogFileName);
        var corpusManifestPath = activeCorpusGeneration?.ManifestPath
                                 ?? legacyCorpusManifestPath;
        var corpusIdentityIndexPath = activeCorpusGeneration?.IdentityIndexPath
                                      ?? Path.Combine(
                                          corpusDirectory,
                                          CorpusIdentityIndexFileName);
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
        var candidateManifest = activeCorpusGeneration?.Manifest
                                ?? ReadCorpusManifest(
                                    corpusManifestPath,
                                    corpusCompatibilityKey);
        var validatedCorpus = activeCorpusGeneration?.Snapshot
                              ?? (candidateManifest == null
                                  ? null
                                  : ReadValidatedCorpusSnapshot(
                                      datasetPath,
                                      candidateManifest,
                                      cancellationToken));
        var existingManifest = validatedCorpus == null
            ? null
            : candidateManifest;
        var identityIndexValid = validatedCorpus != null
                                 && CorpusIdentityIndexMatches(
                                     corpusIdentityIndexPath,
                                     validatedCorpus);
        var sourceFrameUpperBound = (context.Episodes
                                     ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode?.Authoritative == true)
            .Sum(episode => episode.Frames?.Count ?? 0);
        var incrementalUpdate = existingManifest != null
                                && identityIndexValid
                                && CombatTransformerTeacherCorpusProtocol
                                    .ShouldUseIncrementalExport(
                                        existingManifest.FrameCount,
                                        sourceFrameUpperBound);
        var existingIdentities = incrementalUpdate
            ? new HashSet<string>(
                validatedCorpus!.Identities.Concat(
                    validatedCorpus.BacklogIdentities),
                StringComparer.Ordinal)
            : null;
        report.IncrementalCorpusUpdate = incrementalUpdate;
        var bindings = ExportDataset(
            context.Episodes ?? Array.Empty<CombatEpisode>(),
            options,
            currentDatasetPath,
            context,
            cancellationToken,
            existingIdentities,
            out var skippedExistingFrames);
        report.SkippedExistingCorpusFrames = skippedExistingFrames;
        ReportProgress(context, new CombatTransformerTeacherProgress
        {
            Stage = "merging",
            CompletedFrames = 0,
            TotalFrames = Math.Max(1, bindings.Count),
            Message = "正在去重并合并累计 Transformer 语料"
        });
        var exportedFrames = Math.Max(
            0,
            bindings.Count - skippedExistingFrames);
        var corpus = incrementalUpdate
                     && exportedFrames == 0
                     && validatedCorpus != null
                     && validatedCorpus.BacklogFrameCount == 0
            ? CorpusFromValidatedSnapshot(validatedCorpus, datasetPath)
            : MergeCorpus(
                datasetPath,
                backlogPath,
                currentDatasetPath,
                corpusDirectory,
                corpusCompatibilityKey,
                bindings,
                options.MaximumFrames,
                trainedIdentities,
                anchorRunKeys,
                includeExistingCorpus: validatedCorpus != null,
                cancellationToken: cancellationToken);
        datasetPath = corpus.CorpusPath;
        report.DatasetPath = datasetPath;
        var corpusRows = BindCorpusRows(
            datasetPath,
            bindings,
            cancellationToken);
        ReportProgress(context, new CombatTransformerTeacherProgress
        {
            Stage = "merged",
            CompletedFrames = corpus.FrameCount,
            TotalFrames = corpus.FrameCount,
            Message = "累计 Transformer 语料已就绪"
        });
        report.FrameCount = corpus.FrameCount;
        report.CorpusMaturity = CombatTransformerTeacherCorpusProtocol
            .CorpusMaturity(corpus.FrameCount, options.MinimumFrames);
        report.CorpusDistillationWeightCap =
            CombatTransformerTeacherCorpusProtocol.DistillationWeightCap(
                corpus.FrameCount,
                options.MinimumFrames);
        report.CorpusGrowthFrames = Math.Max(
            0,
            corpus.FrameCount - (existingManifest?.FrameCount ?? 0));
        report.CorpusGrowthRatio = report.CorpusGrowthFrames
                                   / (double)Math.Max(
                                       1,
                                       existingManifest?.FrameCount ?? 0);
        report.CurrentFrameCount = corpus.CurrentFrames;
        report.ReusedCorpusFrames = corpus.ReusedFrames;
        report.DeduplicatedCorpusFrames = corpus.DeduplicatedFrames;
        report.DroppedCorpusFrames = corpus.DroppedFrames;
        report.CorpusBacklogFrames = corpus.BacklogFrames;
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
        var iterationPreviousReport = ReadPreviousReport(
            iterationPreviousReportPath);
        var iterationPreviousApplied = HasAcceptedTeacherArtifactForWarmStart(
            iterationPreviousModelPath,
            iterationPreviousReport,
            teacherCompatibilityKey);
        var persistentPreviousReport = ReadPreviousReport(
            persistentReportPath);
        var persistentWorldPreviousReport = ReadPreviousReport(
            persistentWorldReportPath);
        var persistentPreviousApplied = HasAcceptedTeacherArtifactForWarmStart(
            persistentModelPath,
            persistentPreviousReport,
            teacherCompatibilityKey);
        var previousModelPath = iterationPreviousApplied
            ? iterationPreviousModelPath
            : persistentPreviousApplied
                ? persistentModelPath
                : "";
        var previousReport = iterationPreviousApplied
            ? iterationPreviousReport
            : persistentPreviousApplied
                ? persistentPreviousReport
                : null;
        var rejectedUpdateStreak = ConsecutiveRejectedTeacherUpdates(
            resultDirectory,
            context.Iteration,
            teacherCompatibilityKey);
        var lastAttemptIteration = LastTeacherAttemptIteration(
            resultDirectory,
            context.Iteration,
            teacherCompatibilityKey);
        report.DatasetDriftScore = DatasetDrift(report, previousReport);
        var warmStarted = options.EnableWarmStart
                          && File.Exists(previousModelPath);
        var pendingFrames = corpusRows.Count(row =>
            !anchorRunKeys.Contains(EffectiveRunKey(row))
            && !trainedIdentities.Contains(row.Identity));
        var freshPendingFrames = corpusRows.Count(row =>
            !anchorRunKeys.Contains(EffectiveRunKey(row))
            && !trainedIdentities.Contains(row.Identity)
            && !attemptedIdentities.Contains(row.Identity));
        var finalRefresh =
            CombatTransformerTeacherRefreshProtocol.IsFinalRefresh(context);
        var cpuBackend = string.Equals(
            runtime.EffectiveBackend,
            CombatTransformerTeacherBackendNames.Cpu,
            StringComparison.OrdinalIgnoreCase);
        var driftRefresh = options.EnableAdaptiveRefresh
                           && report.DatasetDriftScore
                              >= options.AdaptiveRefreshDriftThreshold;
        var corpusGrowthRefresh = report.CorpusGrowthFrames >= 256
                                  || report.CorpusGrowthRatio >= 0.20d;
        report.RefreshTriggeredByCorpusGrowth = corpusGrowthRefresh;
        var refreshInterval = cpuBackend
            ? options.CpuRefreshInterval
            : options.AcceleratorRefreshInterval;
        var trainingEnabled = CombatTransformerTeacherRefreshProtocol
            .ShouldRefresh(
                warmStarted,
                finalRefresh,
                cpuBackend,
                context.Iteration,
                previousReport?.Iteration ?? 0,
                lastAttemptIteration,
                rejectedUpdateStreak,
                pendingFrames,
                freshPendingFrames,
                driftRefresh,
                options,
                out var refreshReason);
        report.RefreshReason = refreshReason;
        report.RefreshInterval = refreshInterval;
        report.RefreshRejectedUpdateStreak = rejectedUpdateStreak;
        report.RefreshLastAttemptIteration = lastAttemptIteration;
        report.RefreshMinimumFreshFrames =
            options.MinimumFreshFramesForRefresh;
        report.RefreshFreshPendingFrames = freshPendingFrames;
        var effectiveEpochs = cpuBackend
            ? finalRefresh
                ? options.CpuFinalEpochs
                : warmStarted
                    ? options.CpuIncrementalEpochs
                    : options.CpuEpochs
            : finalRefresh
                ? options.FinalEpochs
                : warmStarted
                    ? options.IncrementalEpochs
                    : options.Epochs;
        report.RequestedEpochs = trainingEnabled ? effectiveEpochs : 0;
        report.WarmStarted = warmStarted;
        report.TrainingRefreshed = trainingEnabled;
        report.ResumeModelPath = warmStarted ? previousModelPath : "";
        report.IncrementalPendingFrames = pendingFrames;
        if (trainingEnabled
            && !warmStarted
            && !File.Exists(anchorPath)
            && corpusRows.Select(EffectiveRunKey)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count() < 2)
        {
            report.TrainingRefreshed = false;
            report.RequestedEpochs = 0;
            report.Message =
                "Transformer teacher is collecting data: at least two "
                + "independent Journey runs are required for run-isolated "
                + "training and validation.";
            return report;
        }
        var memory = CombatFoundationResourceSnapshot.Capture();
        var previousPeak = Math.Max(
            0L,
            previousReport?.PeakWorkingSetBytes ?? 0L);
        var predictedPeak = previousPeak > 0L
            ? Math.Max(
                1024L * 1024L * 1024L,
                (long)Math.Ceiling(previousPeak * 1.15d))
            : options.EnableShardedDataset
                ? 3L * 1024L * 1024L * 1024L
                : 5L * 1024L * 1024L * 1024L;
        if (options.EnableShardedDataset)
        {
            var automaticWorkers = cpuBackend
                ? 0
                : Math.Min(2, Math.Max(1, Environment.ProcessorCount / 4));
            var loaderWorkers = options.DataLoaderWorkers > 0
                ? options.DataLoaderWorkers
                : automaticWorkers;
            var datasetCopies = 1L + loaderWorkers * 2L;
            var cacheEstimate = datasetCopies
                                * options.DatasetShardFrames
                                * 192L * 1024L;
            predictedPeak = predictedPeak > long.MaxValue - cacheEstimate
                ? long.MaxValue
                : predictedPeak + cacheEstimate;
        }
        report.AvailablePhysicalMemoryBytes =
            memory.AvailablePhysicalMemoryBytes;
        report.MemoryReserveBytes = options.MemoryReserveBytes;
        report.PredictedPeakWorkingSetBytes = predictedPeak;
        report.DatasetStorageMode = options.EnableShardedDataset
            ? "auto-resident-sharded-v3-locality"
            : "resident";
        report.DatasetShardFrames = options.DatasetShardFrames;
        report.DatasetEncoding = CombatTransformerWorldModelProtocol
            .SparseDataset;
        report.MemoryAdmissionPassed =
            memory.AvailablePhysicalMemoryBytes
            >= options.MemoryReserveBytes + predictedPeak;
        if (!report.MemoryAdmissionPassed)
        {
            report.Message = "Transformer teacher deferred by memory gate: "
                             + "available="
                             + memory.AvailablePhysicalMemoryBytes
                             + ", predictedPeak="
                             + predictedPeak
                             + ", reserve="
                             + options.MemoryReserveBytes;
            return report;
        }

        var annotationSelectionPath = Path.Combine(
            iterationDirectory,
            "annotation-row-selection-v1.txt");
        var annotationRows = bindings
            .Where(binding => binding.RowIndex >= 0)
            .Select(binding => binding.RowIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        WriteRowSelection(annotationSelectionPath, annotationRows);
        report.AnnotationSelectionFrames = annotationRows.Length;
        if (annotationRows.Length == 0)
        {
            report.Message =
                "Transformer teacher skipped: the current iteration has no "
                + "bindable authoritative frames to annotate.";
            return report;
        }

        var trainingSelectionPath = "";
        IncrementalTrainingSelection? incrementalSelection = null;
        if (trainingEnabled && warmStarted && !finalRefresh)
        {
            incrementalSelection = SelectIncrementalTrainingRows(
                corpusRows,
                trainedIdentities,
                attemptedIdentities,
                anchorRunKeys,
                options,
                teacherCompatibilityKey,
                context.Iteration,
                rejectedUpdateStreak);
            trainingSelectionPath = Path.Combine(
                iterationDirectory,
                "incremental-training-row-selection-v1.txt");
            WriteRowSelection(
                trainingSelectionPath,
                incrementalSelection.RowIndices);
            report.IncrementalTrainingSelection = true;
            report.IncrementalTrainingFrames =
                incrementalSelection.RowIndices.Count;
            report.IncrementalNewFrames = incrementalSelection.NewFrames;
            report.IncrementalFreshFrames = incrementalSelection.FreshFrames;
            report.IncrementalRetryFrames = incrementalSelection.RetryFrames;
            report.IncrementalReplayFrames = incrementalSelection.ReplayFrames;
            report.IncrementalPendingFrames = incrementalSelection.PendingFrames;
            report.IncrementalDeferredFrames =
                incrementalSelection.DeferredFrames;
            report.IncrementalReplayEscalationLevel =
                incrementalSelection.ReplayEscalationLevel;
            if (incrementalSelection.RowIndices.Count == 0)
            {
                trainingEnabled = false;
                trainingSelectionPath = "";
                report.TrainingRefreshed = false;
                report.RequestedEpochs = 0;
                report.RefreshReason = "bounded-selection-unavailable";
            }
        }
        report.RequestedEpochs = trainingEnabled ? effectiveEpochs : 0;
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
            anchorPath,
            trainingSelectionPath,
            annotationSelectionPath,
            corpus.FrameCount);
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
                var processFailure =
                    CombatTransformerTeacherProcessFailureClassifier.Classify(
                        stderr.Result);
                CombatTransformerTeacherFailureProtocol.Mark(
                    report,
                    processFailure.FailureKind,
                    processFailure.Retryable,
                    processFailure.FormalModelBlocked,
                    process.ExitCode);
                report.Message = "Transformer teacher process failed ("
                                 + process.ExitCode
                                 + "): "
                                 + Tail(stderr.Result, 4000);
                return report;
            }
            if (!File.Exists(reportPath) || !File.Exists(annotationsPath))
            {
                report.Message = "Transformer teacher did not produce its report and annotations.";
                CombatTransformerTeacherFailureProtocol.Mark(
                    report,
                    CombatTransformerTeacherFailureKinds.Protocol,
                    retryable: false,
                    formalModelBlocked: true);
                return report;
            }
            var external = JsonConvert.DeserializeObject<
                CombatTransformerTeacherReport>(
                File.ReadAllText(reportPath, Encoding.UTF8));
            if (external == null
                || !string.Equals(
                    external.Protocol,
                    CombatTransformerWorldModelProtocol.Report,
                    StringComparison.Ordinal))
            {
                report.Message =
                    "Transformer teacher report protocol is missing or incompatible.";
                CombatTransformerTeacherFailureProtocol.Mark(
                    report,
                    CombatTransformerTeacherFailureKinds.Protocol,
                    retryable: false,
                    formalModelBlocked: true);
                return report;
            }
            MergeExternalReport(report, external);
            ApplyAnnotations(annotationsPath, bindings, report);
            if (report.TrainingRefreshed && incrementalSelection != null)
            {
                try
                {
                    // Attempt history follows the compatibility-scoped corpus,
                    // including rows currently parked in the durable backlog.
                    var attemptedRows = incrementalSelection.RowIndices
                        .ToHashSet();
                    foreach (var row in corpusRows.Where(row =>
                                 attemptedRows.Contains(row.SourceLine)))
                    {
                        attemptedIdentities.Add(row.Identity);
                    }
                    WriteIdentitySet(
                        attemptedIdentityPath,
                        attemptedIdentities);
                }
                catch (Exception ex)
                {
                    report.DataQualityWarnings.Add(
                        "Transformer attempted-frame watermark was not "
                        + "persisted: "
                        + ex.Message);
                }
            }
            report.Success = report.Success && report.AnnotatedFrames > 0;
            report.PolicyQualityGatePassed =
                report.ValidationUniformPolicyCrossEntropy > 0d
                && !double.IsNaN(report.ValidationPolicyCrossEntropy)
                && !double.IsInfinity(report.ValidationPolicyCrossEntropy)
                && report.ValidationPolicyCrossEntropy
                   <= report.ValidationUniformPolicyCrossEntropy + 0.000001d;
            report.WorldModelQualityGatePassed =
                report.DynamicsTrainingFrames > 0
                && report.DynamicsValidationFrames > 0
                && report.InvalidTransitionFrames == 0
                && report.TerminalKnownFrames
                   >= Math.Ceiling(report.LoadedDatasetFrames * 0.95d)
                && report.ObjectTokenAuditPassed
                && Finite(report.ValidationDynamicsMse)
                && report.ValidationDynamicsMse <= 0.5d
                && Finite(report.ValidationOutcomeMae)
                && report.ValidationOutcomeMae <= 0.5d;
            const int MinimumAnchorValidationFrames = 64;
            const int MaximumAnchorValidationFrames = 192;
            var initialRequiredAnchorFrames = Math.Min(
                MaximumAnchorValidationFrames,
                Math.Max(
                    MinimumAnchorValidationFrames,
                    report.FrameCount / 5));
            var establishedAnchorRequirement = !report.AnchorCreated
                                               && previousReport?.Applied == true
                ? Math.Max(
                    0,
                    previousReport.RequiredAnchorValidationFrames > 0
                        ? previousReport.RequiredAnchorValidationFrames
                        : previousReport.AnchorValidationFrames)
                : 0;
            var existingAnchorFallbackRequirement = !report.AnchorCreated
                                                    && report.AnchorValidationFrames > 0
                ? Math.Min(
                    initialRequiredAnchorFrames,
                    Math.Max(
                        MinimumAnchorValidationFrames,
                        report.AnchorValidationFrames))
                : 0;
            var requiredAnchorFrames = establishedAnchorRequirement > 0
                ? establishedAnchorRequirement
                : existingAnchorFallbackRequirement > 0
                    ? existingAnchorFallbackRequirement
                    : initialRequiredAnchorFrames;
            report.RequiredAnchorValidationFrames = requiredAnchorFrames;
            report.AnchorCoverageGatePassed =
                !options.EnableFixedAnchorValidation
                || report.AnchorValidationFrames >= requiredAnchorFrames;
            report.QualityGatePassed = report.PolicyQualityGatePassed
                                       && report.WorldModelQualityGatePassed
                                       && report.AnchorCoverageGatePassed;
            report.TeacherSourceGatePassed =
                CombatTransformerTeacherApplicationProtocol
                    .HasUsableTeacherSource(report);
            report.PolicyTeacherApplied = report.Success
                                          && report.PolicyQualityGatePassed
                                          && report.AnchorCoverageGatePassed
                                          && report.TeacherSourceGatePassed
                                          && report.FrameCount
                                          >= options.MinimumFrames
                                          && report.AnnotatedFrames > 0;
            report.WorldTeacherApplied = report.PolicyTeacherApplied
                                         && report.WorldModelQualityGatePassed;
            // Applied is the student-distillation contract. A world-head
            // failure must not disable a qualified policy teacher.
            report.Applied = report.PolicyTeacherApplied;
            report.StablePolicyTeacherGeneration = report.PolicyTeacherApplied
                ? report.TeacherGeneration
                : Math.Max(
                    0,
                    persistentPreviousReport?.StablePolicyTeacherGeneration
                    ?? persistentPreviousReport?.TeacherGeneration
                    ?? 0);
            report.StableWorldTeacherGeneration = report.WorldTeacherApplied
                ? report.TeacherGeneration
                : Math.Max(
                    0,
                    persistentWorldPreviousReport?.StableWorldTeacherGeneration
                    ?? persistentWorldPreviousReport?.TeacherGeneration
                    ?? 0);
            report.AnnotationTeacherGeneration = report.PolicyTeacherApplied
                ? report.TeacherGeneration
                : 0;
            HashSet<string>? nextTrainedIdentities = null;
            HashSet<string>? currentAnchorRuns = null;
            if (report.WorldTeacherApplied
                && report.TrainingRefreshed
                && report.UpdateAccepted)
            {
                currentAnchorRuns = ReadAnchorRunKeys(
                    anchorPath,
                    cancellationToken);
                var selectedRows = incrementalSelection == null
                    ? null
                    : incrementalSelection.RowIndices.ToHashSet();
                nextTrainedIdentities = new HashSet<string>(
                    trainedIdentities,
                    StringComparer.Ordinal);
                // Do not prune identities merely because their rows rotated
                // from the active training window into the durable backlog.
                foreach (var row in corpusRows.Where(row =>
                             !currentAnchorRuns.Contains(EffectiveRunKey(row))
                             && (selectedRows == null
                                 || selectedRows.Contains(row.SourceLine))))
                {
                    nextTrainedIdentities.Add(row.Identity);
                }
                report.IncrementalDeferredFrames = corpusRows.Count(row =>
                    !currentAnchorRuns.Contains(EffectiveRunKey(row))
                    && !trainedIdentities.Contains(row.Identity));
            }
            var modelArtifactMissing = report.Applied && !File.Exists(modelPath);
            if (modelArtifactMissing)
            {
                report.Applied = false;
                report.PolicyTeacherApplied = false;
                report.WorldTeacherApplied = false;
                report.AnnotationTeacherGeneration = 0;
                nextTrainedIdentities = null;
                currentAnchorRuns = null;
            }
            if (modelArtifactMissing)
            {
                report.Message =
                    "Transformer teacher withheld: the accepted model artifact is missing.";
            }
            else if (report.PolicyTeacherApplied
                     && !report.WorldTeacherApplied)
            {
                report.Message =
                    "Transformer policy teacher annotations applied; world model update withheld by its independent quality gate.";
            }
            else if (report.Applied)
            {
                report.Message = report.TrainingRefreshed
                                 && !report.UpdateAccepted
                    ? "Transformer update rejected by the fixed-anchor gate; stable teacher annotations applied."
                    : "Transformer teacher annotations applied.";
            }
            else if (report.Success && !report.TeacherSourceGatePassed)
            {
                report.Message =
                    "Transformer teacher withheld: a cold start did not accept a trained update.";
            }
            else if (report.Success && !report.PolicyQualityGatePassed)
            {
                report.Message =
                    "Transformer teacher withheld: validation policy loss did not beat the uniform baseline.";
            }
            else if (report.Success && !report.AnchorCoverageGatePassed)
            {
                report.Message =
                    "Transformer teacher withheld: fixed-anchor validation coverage is too small.";
            }
            else if (string.IsNullOrWhiteSpace(report.Message))
            {
                report.Message = "Transformer teacher completed without enough valid annotations.";
            }
            var baseMessage = report.Message;
            ApplyDataQualityAuditMessage(report, baseMessage);
            WriteTextAtomic(
                reportPath,
                JsonConvert.SerializeObject(report, Formatting.Indented));
            if (report.Applied)
            {
                var watermarkAdvanced =
                    CommitTeacherArtifactsAndTrainingWatermark(
                        modelPath,
                        reportPath,
                        persistentModelPath,
                        persistentReportPath,
                        trainedIdentityPath,
                        nextTrainedIdentities,
                        out var watermarkError);
                if (report.WorldTeacherApplied)
                {
                    CopyAtomic(modelPath, persistentWorldModelPath);
                    CopyAtomic(reportPath, persistentWorldReportPath);
                }
                if (nextTrainedIdentities != null)
                {
                    if (watermarkAdvanced)
                    {
                        trainedIdentities.Clear();
                        trainedIdentities.UnionWith(nextTrainedIdentities);
                    }
                    else
                    {
                        report.DataQualityWarnings.Add(
                            "Transformer training watermark was not advanced; "
                            + "pending runs will be retried: "
                            + watermarkError);
                    }
                    report.IncrementalDeferredFrames = corpusRows.Count(row =>
                        !currentAnchorRuns!.Contains(EffectiveRunKey(row))
                        && !trainedIdentities.Contains(row.Identity));
                }
                ApplyDataQualityAuditMessage(report, baseMessage);
                WriteTextAtomic(
                    reportPath,
                    JsonConvert.SerializeObject(report, Formatting.Indented));
                CopyAtomic(reportPath, persistentReportPath);
                if (report.WorldTeacherApplied)
                {
                    CopyAtomic(reportPath, persistentWorldReportPath);
                }
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
        CancellationToken cancellationToken,
        HashSet<string>? excludedIdentities,
        out int skippedExistingFrames)
    {
        var bindings = new List<FrameBinding>();
        skippedExistingFrames = 0;
        var totalFrames = episodes.Sum(episode =>
            episode?.Frames?.Count ?? 0);
        var completedFrames = 0;
        var exportedRowIndex = 0;
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
            // Episode frame order is the authoritative decision clock. Turn
            // and simulation action ids are observability fields and may stay
            // unchanged across EndTurn or no-effect decisions.
            var frames = (episode.Frames ?? new List<CombatEpisodeFrame>())
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
                var runKey = StableRunKey(episode);
                var identity = runKey
                               + "|"
                               + episode.JourneyBattleIndex
                               + "|"
                               + frame.Turn
                               + "|"
                               + frame.ActionSequence
                               + "|"
                               + frame.DecisionSequence
                               + "|"
                               + frame.StateFingerprint;
                var policy = CombatPolicyValueBatchTrainer.PolicyTargets(
                    candidates,
                    frame.ExecutedCandidateId,
                    1.25d,
                    0.95d);
                var strategyLabels = NormalizeStrategyLabels(
                    frame.StrategyLabelsKnown
                        ? frame.StrategyLabels
                        : Array.Empty<string>());
                var strategyApplicable = NormalizeStrategyLabels(
                    frame.StrategyApplicabilityKnown
                        ? frame.StrategyApplicableLabels
                        : Array.Empty<string>());
                var strategy = strategyApplicable.Count > 0
                    ? PrimaryStrategy(strategyLabels)
                    : "strategy-not-applicable";
                var binding = new FrameBinding
                {
                    Frame = frame,
                    Candidates = candidates,
                    Strategy = strategy,
                    Identity = identity
                };
                bindings.Add(binding);
                if (excludedIdentities?.Contains(identity) == true)
                {
                    skippedExistingFrames++;
                    continue;
                }
                binding.ExportedToCurrentDataset = true;
                var compactNextState =
                    frame.CompactTransitionNextStateFeatures;
                var hasNextState = frame.TransitionKnown
                                   && frame.TransitionValid
                                   && (compactNextState?.Count > 0
                                       || frame.TransitionNextStateFeatures.Count
                                       > 0);
                var nextState = new double[options.StateDimensions];
                if (hasNextState && compactNextState != null)
                {
                    CombatPolicyValueEncoding.EncodeStateInto(
                        compactNextState,
                        nextState,
                        options.StateDimensions,
                        "partitioned-v4");
                }
                else if (hasNextState)
                {
                    CombatPolicyValueEncoding.EncodeStateInto(
                        frame.TransitionNextStateFeatures,
                        nextState,
                        options.StateDimensions,
                        "partitioned-v4");
                }
                var encodedState = new double[options.StateDimensions];
                if (frame.CompactStateFeatures != null)
                {
                    CombatPolicyValueEncoding.EncodeStateInto(
                        frame.CompactStateFeatures,
                        encodedState,
                        options.StateDimensions,
                        "partitioned-v4");
                }
                else
                {
                    CombatPolicyValueEncoding.EncodeStateInto(
                        frame.StateFeatures,
                        encodedState,
                        options.StateDimensions,
                        "partitioned-v4");
                }
                var objectTokens = frame.HasObservation
                    ? CombatWorldModelTokenEncoding.Encode(
                        frame.Observation,
                        options.StateDimensions,
                        options.MaximumObjectTokens,
                        includeActionCandidates: false)
                    : Array.Empty<double[]>();
                var row = new TeacherDatasetRow
                {
                    I = exportedRowIndex++,
                    E = episodeIndex,
                    Y = runKey,
                    F = episodeFrameIndex,
                    T = frame.Turn,
                    Q = frame.ActionSequence,
                    QD = frame.DecisionSequence,
                    D = identity,
                    L = strategy,
                    C = NormalizeDifficulty(episode.Campaign?.DifficultyId),
                    B = episode.JourneyBattleIndex,
                    J = episode.Campaign?.FinalBossVictory == true ? 1 : 0,
                    OK = objectTokens.Length > 0 ? 1 : 0,
                    DK = hasNextState
                         || frame.TerminalKnown && frame.Terminal
                        ? 1
                        : 0,
                    SK = frame.StrategyApplicabilityKnown
                         && strategyApplicable.Count > 0
                        ? 1
                        : 0,
                    TK = frame.TerminalKnown ? 1 : 0,
                    S = SparseFeatureVector.Encode(encodedState),
                    O = objectTokens
                        .Select(SparseFeatureVector.Encode)
                        .ToArray(),
                    A = candidates.Select(candidate =>
                            SparseFeatureVector.Encode(
                                CombatPolicyValueEncoding.EncodeCandidate(
                                    new CombatPolicyValueCandidate
                                    {
                                        CandidateId = candidate.CandidateId,
                                        SourceId = candidate.SourceId,
                                        Features = candidate.Features
                                    },
                                    options.ActionDimensions,
                                    "partitioned-v4")))
                        .ToArray(),
                    P = policy,
                    X = executedIndex,
                    V = Math.Max(-1d, Math.Min(1d, frame.LongTermReturn)),
                    G = frame.StrategyPhase >= 0
                        ? Math.Max(0, Math.Min(4, frame.StrategyPhase))
                        : StrategyStage(frame.StateFeatures),
                    SL = StrategyVector(strategyLabels),
                    SA = StrategyVector(strategyApplicable),
                    K = HasDeclaredTrainingQuota(frame.StateFeatures)
                        || string.Equals(
                            strategy,
                            "strategy-baseline",
                            StringComparison.Ordinal)
                        ? 1d
                        : 2d,
                    N = SparseFeatureVector.Encode(nextState),
                    M = hasNextState ? 1 : 0,
                    TS = hasNextState ? Math.Max(1, frame.TransitionSpan) : 0,
                    TB = frame.TransitionCrossedTurnBoundary ? 1 : 0,
                    AD = frame.TransitionActionSequenceDelta,
                    TR = frame.TransitionInvalidReason ?? "",
                    W = Math.Max(0d, Math.Min(1d, frame.WinTarget)),
                    R = Math.Max(0d, Math.Min(1d, frame.DeathTarget)),
                    H = Math.Max(0d, Math.Min(1d, frame.RemainingHpRatioTarget)),
                    U = Math.Max(0d, frame.RemainingTurnsTarget),
                    Z = frame.TerminalKnown && frame.Terminal ? 1 : 0
                };
                writer.WriteLine(JsonConvert.SerializeObject(row, Formatting.None));
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

    private static TeacherCorpusManifest? ReadCorpusManifest(
        string path,
        string compatibilityKey)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var manifest = JsonConvert.DeserializeObject<TeacherCorpusManifest>(
                File.ReadAllText(path, Encoding.UTF8));
            return manifest != null
                   && string.Equals(
                       manifest.Protocol,
                       CombatTransformerTeacherCorpusProtocol.Version,
                       StringComparison.Ordinal)
                   && string.Equals(
                       manifest.CompatibilityKey,
                       compatibilityKey,
                       StringComparison.Ordinal)
                ? manifest
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static ValidatedCorpusSnapshot? ReadValidatedCorpusSnapshot(
        string path,
        TeacherCorpusManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)
            || manifest.FrameCount < 0
            || manifest.ContentLengthBytes < 0
            || string.IsNullOrWhiteSpace(manifest.Fingerprint)
            || manifest.ContentSha256?.Length != 64)
        {
            return null;
        }
        try
        {
            var info = new FileInfo(path);
            if (info.Length != manifest.ContentLengthBytes
                || !string.Equals(
                    FileSha256(path),
                    manifest.ContentSha256,
                    StringComparison.Ordinal))
            {
                return null;
            }

            var descriptors = new Dictionary<string, CorpusFrameDescriptor>(
                StringComparer.Ordinal);
            var observedRows = ReadCorpusDescriptors(
                path,
                current: false,
                descriptors,
                cancellationToken);
            var identities = descriptors.Keys.ToHashSet(StringComparer.Ordinal);
            var backlogPath = Path.Combine(
                Path.GetDirectoryName(path)!,
                CorpusBacklogFileName);
            var backlogDescriptors = new Dictionary<string, CorpusFrameDescriptor>(
                StringComparer.Ordinal);
            var backlogRows = File.Exists(backlogPath)
                ? ReadCorpusDescriptors(
                    backlogPath,
                    current: false,
                    backlogDescriptors,
                    cancellationToken)
                : 0;
            var backlogValid = manifest.BacklogFrameCount == backlogRows
                               && backlogRows == backlogDescriptors.Count
                               && (backlogRows == 0
                                   ? !File.Exists(backlogPath)
                                     || new FileInfo(backlogPath).Length == 0
                                   : manifest.BacklogContentLengthBytes
                                     == new FileInfo(backlogPath).Length
                                     && string.Equals(
                                         manifest.BacklogContentSha256,
                                         FileSha256(backlogPath),
                                         StringComparison.Ordinal))
                               && !identities.Overlaps(backlogDescriptors.Keys);
            var strategies = descriptors.Values
                .GroupBy(item => item.Strategy, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal);
            if (observedRows != manifest.FrameCount
                || descriptors.Count != manifest.FrameCount
                || !string.Equals(
                    DatasetFingerprint(identities),
                    manifest.Fingerprint,
                    StringComparison.Ordinal)
                || !SameStrategyFrames(strategies, manifest.StrategyFrames)
                || !backlogValid)
            {
                return null;
            }

            return new ValidatedCorpusSnapshot
            {
                FrameCount = manifest.FrameCount,
                Fingerprint = manifest.Fingerprint,
                StrategyFrames = strategies,
                Identities = identities,
                BacklogFrameCount = backlogRows,
                BacklogIdentities = backlogDescriptors.Keys.ToHashSet(
                    StringComparer.Ordinal)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Any missing, truncated, corrupt, or interrupted snapshot must
            // force a full export. It must never make current frames look as
            // though they already exist in the corpus.
            return null;
        }
    }

    private static bool CorpusIdentityIndexMatches(
        string path,
        ValidatedCorpusSnapshot snapshot)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        try
        {
            var indexed = File.ReadLines(path, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            return indexed.Count == snapshot.FrameCount
                   && indexed.Distinct(StringComparer.Ordinal).Count()
                      == snapshot.FrameCount
                   && snapshot.Identities.SetEquals(indexed)
                   && string.Equals(
                       DatasetFingerprint(indexed),
                       snapshot.Fingerprint,
                       StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsPersistedCorpusSnapshotValidForTests(
        string corpusPath,
        string identityIndexPath,
        string manifestPath,
        string compatibilityKey)
    {
        var manifest = ReadCorpusManifest(manifestPath, compatibilityKey);
        var snapshot = manifest == null
            ? null
            : ReadValidatedCorpusSnapshot(
                corpusPath,
                manifest,
                CancellationToken.None);
        return snapshot != null
               && CorpusIdentityIndexMatches(identityIndexPath, snapshot);
    }

    internal static string PublishPersistedCorpusSnapshotForTests(
        string corpusDirectory,
        string corpusPath,
        string identityIndexPath,
        string manifestPath,
        string compatibilityKey)
    {
        return PublishPersistedCorpusSnapshot(
            corpusDirectory,
            corpusPath,
            identityIndexPath,
            manifestPath,
            compatibilityKey,
            CancellationToken.None).CorpusPath;
    }

    internal static string? ResolveOrMigratePersistedCorpusSnapshotForTests(
        string corpusDirectory,
        string compatibilityKey)
    {
        return ResolveOrMigrateActiveCorpusGeneration(
            corpusDirectory,
            compatibilityKey,
            CancellationToken.None)?.CorpusPath;
    }

    internal static (
        string CorpusPath,
        string BacklogPath,
        int ActiveFrames,
        int BacklogFrames,
        int DroppedFrames) MergeCorpusRowsForTests(
        string corpusDirectory,
        IReadOnlyList<string> currentRows,
        int maximumFrames,
        IReadOnlySet<string>? trainedIdentities = null,
        string? existingCorpusPath = null,
        string? existingBacklogPath = null)
    {
        Directory.CreateDirectory(corpusDirectory);
        var currentPath = Path.Combine(
            corpusDirectory,
            "current-for-tests-" + Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllLines(
            currentPath,
            currentRows ?? Array.Empty<string>(),
            new UTF8Encoding(false));
        try
        {
            var result = MergeCorpus(
                existingCorpusPath ?? Path.Combine(
                    corpusDirectory,
                    CorpusFileName),
                existingBacklogPath ?? Path.Combine(
                    corpusDirectory,
                    CorpusBacklogFileName),
                currentPath,
                corpusDirectory,
                "TEST-COMPATIBILITY",
                Array.Empty<FrameBinding>(),
                maximumFrames,
                trainedIdentities
                ?? new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                includeExistingCorpus:
                    File.Exists(existingCorpusPath ?? "")
                    || File.Exists(existingBacklogPath ?? ""),
                cancellationToken: CancellationToken.None);
            return (
                result.CorpusPath,
                Path.Combine(
                    Path.GetDirectoryName(result.CorpusPath)!,
                    CorpusBacklogFileName),
                result.FrameCount,
                result.BacklogFrames,
                result.DroppedFrames);
        }
        finally
        {
            TryDelete(currentPath);
        }
    }

    private static CorpusGenerationSnapshot PublishPersistedCorpusSnapshot(
        string corpusDirectory,
        string corpusPath,
        string identityIndexPath,
        string manifestPath,
        string compatibilityKey,
        CancellationToken cancellationToken)
    {
        var manifest = ReadCorpusManifest(manifestPath, compatibilityKey);
        var snapshot = manifest == null
            ? null
            : ReadValidatedCorpusSnapshot(
                corpusPath,
                manifest,
                cancellationToken);
        if (snapshot == null
            || !CorpusIdentityIndexMatches(identityIndexPath, snapshot))
        {
            throw new InvalidDataException(
                "Cannot publish an invalid Transformer corpus snapshot.");
        }

        var generation = NewCorpusGenerationName();
        var generationRoot = Path.Combine(
            corpusDirectory,
            CorpusGenerationDirectoryName);
        Directory.CreateDirectory(generationRoot);
        var stagingDirectory = Path.Combine(
            generationRoot,
            ".staging-" + generation);
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            File.Copy(
                corpusPath,
                Path.Combine(stagingDirectory, CorpusFileName));
            File.Copy(
                identityIndexPath,
                Path.Combine(stagingDirectory, CorpusIdentityIndexFileName));
            File.Copy(
                manifestPath,
                Path.Combine(stagingDirectory, CorpusManifestFileName));
            var backlogPath = Path.Combine(
                Path.GetDirectoryName(corpusPath)!,
                CorpusBacklogFileName);
            if (File.Exists(backlogPath))
            {
                File.Copy(
                    backlogPath,
                    Path.Combine(stagingDirectory, CorpusBacklogFileName));
            }
            return CommitCorpusGeneration(
                corpusDirectory,
                generation,
                stagingDirectory,
                compatibilityKey,
                cancellationToken);
        }
        finally
        {
            TryDeleteCorpusGenerationDirectory(
                stagingDirectory,
                generationRoot);
        }
    }

    private static CorpusGenerationSnapshot? ResolveOrMigrateActiveCorpusGeneration(
        string corpusDirectory,
        string compatibilityKey,
        CancellationToken cancellationToken)
    {
        var active = ResolveActiveCorpusGeneration(
            corpusDirectory,
            compatibilityKey,
            cancellationToken);
        if (active != null)
        {
            return active;
        }

        var legacyCorpusPath = Path.Combine(corpusDirectory, CorpusFileName);
        var legacyIdentityIndexPath = Path.Combine(
            corpusDirectory,
            CorpusIdentityIndexFileName);
        var legacyManifestPath = Path.Combine(
            corpusDirectory,
            CorpusManifestFileName);
        var legacyManifest = ReadCorpusManifest(
            legacyManifestPath,
            compatibilityKey);
        var legacySnapshot = legacyManifest == null
            ? null
            : ReadValidatedCorpusSnapshot(
                legacyCorpusPath,
                legacyManifest,
                cancellationToken);
        if (legacySnapshot == null
            || !CorpusIdentityIndexMatches(
                legacyIdentityIndexPath,
                legacySnapshot))
        {
            return null;
        }

        return PublishPersistedCorpusSnapshot(
            corpusDirectory,
            legacyCorpusPath,
            legacyIdentityIndexPath,
            legacyManifestPath,
            compatibilityKey,
            cancellationToken);
    }

    internal static string? ResolvePersistedCorpusSnapshotForTests(
        string corpusDirectory,
        string compatibilityKey)
    {
        return ResolveActiveCorpusGeneration(
            corpusDirectory,
            compatibilityKey,
            CancellationToken.None)?.CorpusPath;
    }

    private static CorpusGenerationSnapshot? ResolveActiveCorpusGeneration(
        string corpusDirectory,
        string compatibilityKey,
        CancellationToken cancellationToken)
    {
        var generationRoot = Path.Combine(
            corpusDirectory,
            CorpusGenerationDirectoryName);
        CleanupAbandonedCorpusStagingDirectories(generationRoot);
        var pointerPath = Path.Combine(
            corpusDirectory,
            CorpusActiveGenerationFileName);
        var pointer = ReadCorpusGenerationPointer(pointerPath);
        if (pointer != null
            && string.Equals(
                pointer.CompatibilityKey,
                compatibilityKey,
                StringComparison.Ordinal))
        {
            var active = ReadCorpusGeneration(
                generationRoot,
                pointer.Generation,
                compatibilityKey,
                cancellationToken);
            if (active != null)
            {
                return active;
            }
        }

        if (!Directory.Exists(generationRoot))
        {
            return null;
        }

        var recovered = Directory.EnumerateDirectories(generationRoot)
            .Select(path => new
            {
                Path = path,
                Generation = Path.GetFileName(path)
            })
            .Where(item => IsStrictCorpusGenerationName(item.Generation))
            .Select(item => ReadCorpusGenerationAtDirectory(
                item.Path,
                item.Generation,
                compatibilityKey,
                cancellationToken))
            .Where(item => item != null)
            .OrderByDescending(item => item!.Manifest.UpdatedUtc)
            .ThenByDescending(item => item!.Generation, StringComparer.Ordinal)
            .FirstOrDefault();
        if (recovered != null)
        {
            WriteCorpusGenerationPointer(
                pointerPath,
                compatibilityKey,
                recovered.Generation);
        }
        return recovered;
    }

    private static TeacherCorpusGenerationPointer? ReadCorpusGenerationPointer(
        string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var pointer = JsonConvert.DeserializeObject<
                TeacherCorpusGenerationPointer>(
                File.ReadAllText(path, Encoding.UTF8));
            return pointer != null
                   && string.Equals(
                       pointer.Protocol,
                       CorpusGenerationPointerProtocol,
                       StringComparison.Ordinal)
                   && IsStrictCorpusGenerationName(pointer.Generation)
                ? pointer
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static CorpusGenerationSnapshot? ReadCorpusGeneration(
        string generationRoot,
        string generation,
        string compatibilityKey,
        CancellationToken cancellationToken)
    {
        if (!IsStrictCorpusGenerationName(generation))
        {
            return null;
        }
        return ReadCorpusGenerationAtDirectory(
            Path.Combine(generationRoot, generation),
            generation,
            compatibilityKey,
            cancellationToken);
    }

    private static CorpusGenerationSnapshot? ReadCorpusGenerationAtDirectory(
        string directory,
        string generation,
        string compatibilityKey,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)
            || !IsStrictCorpusGenerationName(generation))
        {
            return null;
        }
        var corpusPath = Path.Combine(directory, CorpusFileName);
        var identityIndexPath = Path.Combine(
            directory,
            CorpusIdentityIndexFileName);
        var manifestPath = Path.Combine(directory, CorpusManifestFileName);
        var backlogPath = Path.Combine(directory, CorpusBacklogFileName);
        var manifest = ReadCorpusManifest(manifestPath, compatibilityKey);
        var snapshot = manifest == null
            ? null
            : ReadValidatedCorpusSnapshot(
                corpusPath,
                manifest,
                cancellationToken);
        return snapshot != null
               && CorpusIdentityIndexMatches(identityIndexPath, snapshot)
            ? new CorpusGenerationSnapshot
            {
                Generation = generation,
                DirectoryPath = directory,
                CorpusPath = corpusPath,
                BacklogPath = backlogPath,
                IdentityIndexPath = identityIndexPath,
                ManifestPath = manifestPath,
                Manifest = manifest!,
                Snapshot = snapshot
            }
            : null;
    }

    private static CorpusGenerationSnapshot CommitCorpusGeneration(
        string corpusDirectory,
        string generation,
        string stagingDirectory,
        string compatibilityKey,
        CancellationToken cancellationToken)
    {
        var staged = ReadCorpusGenerationAtDirectory(
            stagingDirectory,
            generation,
            compatibilityKey,
            cancellationToken);
        if (staged == null)
        {
            throw new InvalidDataException(
                "Transformer corpus generation failed validation before commit.");
        }

        var generationRoot = Path.Combine(
            corpusDirectory,
            CorpusGenerationDirectoryName);
        var finalDirectory = Path.Combine(generationRoot, generation);
        Directory.Move(stagingDirectory, finalDirectory);
        var committed = ReadCorpusGenerationAtDirectory(
            finalDirectory,
            generation,
            compatibilityKey,
            cancellationToken)
            ?? throw new InvalidDataException(
                "Transformer corpus generation failed validation after commit.");
        WriteCorpusGenerationPointer(
            Path.Combine(corpusDirectory, CorpusActiveGenerationFileName),
            compatibilityKey,
            generation);
        PruneCorpusGenerations(
            generationRoot,
            generation,
            compatibilityKey,
            cancellationToken);
        return committed;
    }

    private static void WriteCorpusGenerationPointer(
        string path,
        string compatibilityKey,
        string generation)
    {
        WriteTextAtomic(
            path,
            JsonConvert.SerializeObject(
                new TeacherCorpusGenerationPointer
                {
                    CompatibilityKey = compatibilityKey,
                    Generation = generation
                },
                Formatting.Indented));
    }

    private static void PruneCorpusGenerations(
        string generationRoot,
        string activeGeneration,
        string compatibilityKey,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(generationRoot))
        {
            return;
        }
        var directories = Directory.EnumerateDirectories(generationRoot)
            .Select(path => new
            {
                Path = path,
                Generation = Path.GetFileName(path)
            })
            .Where(item => IsStrictCorpusGenerationName(item.Generation))
            .OrderByDescending(item => item.Generation, StringComparer.Ordinal)
            .ToList();
        var retained = new HashSet<string>(StringComparer.Ordinal)
        {
            activeGeneration
        };
        foreach (var item in directories)
        {
            if (retained.Count >= RetainedCorpusGenerations)
            {
                break;
            }
            if (string.Equals(
                    item.Generation,
                    activeGeneration,
                    StringComparison.Ordinal)
                || ReadCorpusGenerationAtDirectory(
                    item.Path,
                    item.Generation,
                    compatibilityKey,
                    cancellationToken) == null)
            {
                continue;
            }
            retained.Add(item.Generation);
        }
        foreach (var item in directories.Where(item =>
                     !retained.Contains(item.Generation)))
        {
            TryDeleteCorpusGenerationDirectory(item.Path, generationRoot);
        }
    }

    private static string NewCorpusGenerationName()
    {
        return DateTime.UtcNow.ToString(
                   "yyyyMMddTHHmmssfffffff",
                   CultureInfo.InvariantCulture)
               + "-"
               + Guid.NewGuid().ToString("N");
    }

    private static void CleanupAbandonedCorpusStagingDirectories(
        string generationRoot)
    {
        if (!Directory.Exists(generationRoot))
        {
            return;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(
                generationRoot,
                "*",
                SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        const string stagingPrefix = ".staging-";
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!name.StartsWith(stagingPrefix, StringComparison.Ordinal)
                || !IsStrictCorpusGenerationName(
                    name.Substring(stagingPrefix.Length)))
            {
                continue;
            }
            TryDeleteCorpusGenerationDirectory(directory, generationRoot);
        }
    }

    private static bool IsStrictCorpusGenerationName(string? generation)
    {
        return generation is { Length: 55 }
               && generation[8] == 'T'
               && generation[22] == '-'
               && DateTime.TryParseExact(
                   generation.Substring(0, 22),
                   "yyyyMMdd'T'HHmmssfffffff",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal
                   | DateTimeStyles.AdjustToUniversal,
                   out _)
               && Guid.TryParseExact(
                   generation.Substring(23),
                   "N",
                   out _);
    }

    private static bool SameStrategyFrames(
        IReadOnlyDictionary<string, int> actual,
        IReadOnlyDictionary<string, int>? expected)
    {
        if (expected == null || actual.Count != expected.Count)
        {
            return false;
        }
        return actual.All(pair => expected.TryGetValue(pair.Key, out var count)
                                  && count == pair.Value);
    }

    private static CorpusMergeResult CorpusFromValidatedSnapshot(
        ValidatedCorpusSnapshot snapshot,
        string corpusPath)
    {
        return new CorpusMergeResult
        {
            FrameCount = Math.Max(0, snapshot.FrameCount),
            CorpusPath = corpusPath,
            CurrentFrames = 0,
            ReusedFrames = Math.Max(0, snapshot.FrameCount),
            BacklogFrames = Math.Max(0, snapshot.BacklogFrameCount),
            Fingerprint = snapshot.Fingerprint,
            StrategyFrames = new Dictionary<string, int>(
                snapshot.StrategyFrames,
                StringComparer.Ordinal)
        };
    }

    private static CorpusMergeResult MergeCorpus(
        string corpusPath,
        string backlogPath,
        string currentPath,
        string corpusDirectory,
        string compatibilityKey,
        IReadOnlyList<FrameBinding> bindings,
        int maximumFrames,
        IReadOnlySet<string> trainedIdentities,
        IReadOnlySet<string> anchorRunKeys,
        bool includeExistingCorpus,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, CorpusFrameDescriptor>(
            StringComparer.Ordinal);
        var observedRows = 0;
        if (includeExistingCorpus && File.Exists(corpusPath))
        {
            observedRows += ReadCorpusDescriptors(
                corpusPath,
                current: false,
                candidates,
                cancellationToken);
        }
        if (includeExistingCorpus && File.Exists(backlogPath))
        {
            observedRows += ReadCorpusDescriptors(
                backlogPath,
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
        var selected = SelectCorpusFrames(
            candidates.Values,
            capacity,
            trainedIdentities,
            anchorRunKeys);
        var selectedIdentities = selected
            .Select(item => item.Identity)
            .ToHashSet(StringComparer.Ordinal);
        var backlogIdentities = candidates.Keys
            .Where(identity => !selectedIdentities.Contains(identity))
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

        var generation = NewCorpusGenerationName();
        var generationRoot = Path.Combine(
            corpusDirectory,
            CorpusGenerationDirectoryName);
        Directory.CreateDirectory(generationRoot);
        var stagingDirectory = Path.Combine(
            generationRoot,
            ".staging-" + generation);
        Directory.CreateDirectory(stagingDirectory);
        var generationCorpusPath = Path.Combine(
            stagingDirectory,
            CorpusFileName);
        var generationBacklogPath = Path.Combine(
            stagingDirectory,
            CorpusBacklogFileName);
        var rowIndex = 0;
        try
        {
            using (var writer = new StreamWriter(
                       generationCorpusPath,
                       append: false,
                       new UTF8Encoding(false),
                       1024 * 1024))
            {
                if (includeExistingCorpus && File.Exists(corpusPath))
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
                if (includeExistingCorpus && File.Exists(backlogPath))
                {
                    WriteSelectedCorpusRows(
                        backlogPath,
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
            var backlogRowIndex = 0;
            using (var writer = new StreamWriter(
                       generationBacklogPath,
                       append: false,
                       new UTF8Encoding(false),
                       1024 * 1024))
            {
                if (includeExistingCorpus && File.Exists(corpusPath))
                {
                    WriteSelectedCorpusRows(
                        corpusPath,
                        current: false,
                        backlogIdentities,
                        candidates,
                        currentBindings,
                        writer,
                        ref backlogRowIndex,
                        cancellationToken);
                }
                if (includeExistingCorpus && File.Exists(backlogPath))
                {
                    WriteSelectedCorpusRows(
                        backlogPath,
                        current: false,
                        backlogIdentities,
                        candidates,
                        currentBindings,
                        writer,
                        ref backlogRowIndex,
                        cancellationToken);
                }
                WriteSelectedCorpusRows(
                    currentPath,
                    current: true,
                    backlogIdentities,
                    candidates,
                    currentBindings,
                    writer,
                    ref backlogRowIndex,
                    cancellationToken);
            }
            var result = new CorpusMergeResult
            {
                FrameCount = selected.Count,
                CurrentFrames = selected.Count(item => item.Current),
                ReusedFrames = selected.Count(item => !item.Current),
                DeduplicatedFrames = Math.Max(0, observedRows - candidates.Count),
                DroppedFrames = 0,
                BacklogFrames = backlogIdentities.Count,
                Fingerprint = DatasetFingerprint(
                    selected.Select(item => item.Identity)),
                StrategyFrames = selected
                    .GroupBy(item => item.Strategy, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Count(),
                        StringComparer.Ordinal)
            };
            WriteCorpusIdentityIndex(
                Path.Combine(
                    stagingDirectory,
                    CorpusIdentityIndexFileName),
                selected.Select(item => item.Identity));
            var manifest = new TeacherCorpusManifest
            {
                CompatibilityKey = compatibilityKey,
                FrameCount = result.FrameCount,
                Fingerprint = result.Fingerprint,
                StrategyFrames = result.StrategyFrames,
                ContentLengthBytes = new FileInfo(generationCorpusPath).Length,
                ContentSha256 = FileSha256(generationCorpusPath),
                BacklogFrameCount = result.BacklogFrames,
                BacklogContentLengthBytes =
                    new FileInfo(generationBacklogPath).Length,
                BacklogContentSha256 = result.BacklogFrames == 0
                    ? ""
                    : FileSha256(generationBacklogPath),
                UpdatedUtc = DateTime.UtcNow
            };
            WriteTextAtomic(
                Path.Combine(stagingDirectory, CorpusManifestFileName),
                JsonConvert.SerializeObject(manifest, Formatting.Indented));
            var committed = CommitCorpusGeneration(
                corpusDirectory,
                generation,
                stagingDirectory,
                compatibilityKey,
                cancellationToken);
            result.CorpusPath = committed.CorpusPath;
            return result;
        }
        finally
        {
            TryDeleteCorpusGenerationDirectory(
                stagingDirectory,
                generationRoot);
        }
    }

    private static void WriteCorpusIdentityIndex(
        string path,
        IEnumerable<string> identities)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllLines(
                temporaryPath,
                identities.OrderBy(item => item, StringComparer.Ordinal),
                new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
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
                case "Y":
                    descriptor.RunKey = Convert.ToString(
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
                case "OK":
                    descriptor.HasObjectTokens = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture) != 0;
                    break;
                case "DK":
                    descriptor.TransitionContractKnown = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture) != 0;
                    break;
                case "SK":
                    descriptor.StrategyKnown = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture) != 0;
                    break;
                case "M":
                    descriptor.HasTransition = Convert.ToInt32(
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
        int capacity,
        IReadOnlySet<string> trainedIdentities,
        IReadOnlySet<string> anchorRunKeys)
    {
        var all = source.ToList();
        if (all.Count <= capacity)
        {
            return all;
        }
        trainedIdentities ??= new HashSet<string>(StringComparer.Ordinal);
        anchorRunKeys ??= new HashSet<string>(StringComparer.Ordinal);
        var runs = all
            .GroupBy(EffectiveRunKey, StringComparer.Ordinal)
            .Select(group => new CorpusRunDescriptor
            {
                RunKey = group.Key,
                Frames = group
                    .OrderBy(item => item.SourceLine)
                    .ThenBy(item => item.Identity, StringComparer.Ordinal)
                    .ToList(),
                Protected = group.Any(item =>
                    !anchorRunKeys.Contains(EffectiveRunKey(item))
                    && !trainedIdentities.Contains(item.Identity)),
                Advanced = group.Any(IsAdvanced),
                Priority = group.Min(CorpusPriority)
            })
            .OrderBy(run => run.Priority)
            .ThenBy(run => StableRunRank(run.RunKey))
            .ThenBy(run => run.RunKey, StringComparer.Ordinal)
            .ToList();
        var selectedRuns = new HashSet<string>(StringComparer.Ordinal);
        var selectedFrames = 0;
        void AddRun(CorpusRunDescriptor run)
        {
            if (selectedRuns.Contains(run.RunKey)
                || run.Frames.Count > capacity - selectedFrames)
            {
                return;
            }
            selectedRuns.Add(run.RunKey);
            selectedFrames += run.Frames.Count;
        }
        var protectedTarget = runs.Any(run => !run.Protected)
            ? (int)Math.Ceiling(capacity * 0.60d)
            : capacity;
        foreach (var run in runs.Where(run => run.Protected))
        {
            if (selectedFrames >= protectedTarget) break;
            AddRun(run);
        }
        var advancedTarget = Math.Min(
            runs.Where(run => run.Advanced).Sum(run => run.Frames.Count),
            (int)Math.Ceiling(capacity * 0.40d));
        foreach (var run in runs.Where(run => !run.Protected && run.Advanced))
        {
            if (selectedFrames >= advancedTarget) break;
            AddRun(run);
        }
        foreach (var run in runs.Where(run => !run.Protected))
        {
            AddRun(run);
        }
        foreach (var run in runs.Where(run => run.Protected))
        {
            AddRun(run);
        }
        return runs
            .Where(run => selectedRuns.Contains(run.RunKey))
            .SelectMany(run => run.Frames)
            .ToList();
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

    private static List<CorpusFrameDescriptor> BindCorpusRows(
        string corpusPath,
        IReadOnlyList<FrameBinding> bindings,
        CancellationToken cancellationToken)
    {
        foreach (var binding in bindings)
        {
            binding.RowIndex = -1;
        }
        if (!File.Exists(corpusPath))
        {
            return new List<CorpusFrameDescriptor>();
        }
        var descriptors = new Dictionary<string, CorpusFrameDescriptor>(
            StringComparer.Ordinal);
        ReadCorpusDescriptors(
            corpusPath,
            current: false,
            descriptors,
            cancellationToken);
        var rowByIdentity = descriptors.Values.ToDictionary(
            item => item.Identity,
            item => item.SourceLine,
            StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (rowByIdentity.TryGetValue(binding.Identity, out var rowIndex))
            {
                binding.RowIndex = rowIndex;
            }
        }
        return descriptors.Values
            .OrderBy(item => item.SourceLine)
            .ToList();
    }

    private static IncrementalTrainingSelection SelectIncrementalTrainingRows(
        IReadOnlyList<CorpusFrameDescriptor> corpusRows,
        IReadOnlySet<string> trainedIdentities,
        IReadOnlySet<string> attemptedIdentities,
        IReadOnlySet<string> anchorRunKeys,
        CombatTransformerTeacherOptions options,
        string teacherCompatibilityKey,
        int iteration,
        int rejectedUpdateStreak)
    {
        var configuredMaximum = Math.Max(
            options.MinimumFrames,
            options.MaximumIncrementalTrainingFrames);
        var eligibleRows = corpusRows
            .Where(row => !anchorRunKeys.Contains(EffectiveRunKey(row)))
            .ToList();
        var pendingIdentities = eligibleRows
            .Where(row => !trainedIdentities.Contains(row.Identity))
            .Select(row => row.Identity)
            .ToHashSet(StringComparer.Ordinal);
        var pendingRunKeys = eligibleRows
            .Where(row => pendingIdentities.Contains(row.Identity))
            .Select(row => EffectiveRunKey(row))
            .ToHashSet(StringComparer.Ordinal);
        var freshRunKeys = eligibleRows
            .Where(row => pendingIdentities.Contains(row.Identity)
                          && !attemptedIdentities.Contains(row.Identity))
            .Select(EffectiveRunKey)
            .ToHashSet(StringComparer.Ordinal);
        var retryRunKeys = pendingRunKeys
            .Where(runKey => !freshRunKeys.Contains(runKey))
            .ToHashSet(StringComparer.Ordinal);
        var largestPendingRun = eligibleRows
            .Where(row => pendingRunKeys.Contains(EffectiveRunKey(row)))
            .GroupBy(EffectiveRunKey, StringComparer.Ordinal)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();
        var maximum = Math.Min(
            options.MaximumFrames,
            Math.Max(configuredMaximum, largestPendingRun));
        var selected = new Dictionary<int, CorpusFrameDescriptor>();
        var escalationLevel = rejectedUpdateStreak >= 2
            ? 2
            : rejectedUpdateStreak == 1
                ? 1
                : 0;
        var replayShare = CombatTransformerTeacherCorpusProtocol
            .IncrementalReplayShare(rejectedUpdateStreak);
        var replayTarget = Math.Min(
            maximum,
            Math.Max(
                options.IncrementalReplayFrames,
                (int)Math.Ceiling(maximum * replayShare)));
        AddWholeRuns(
            selected,
            eligibleRows.Where(row =>
                !pendingRunKeys.Contains(EffectiveRunKey(row))),
            replayTarget,
            teacherCompatibilityKey + "|replay|" + iteration);

        var pendingCapacity = Math.Max(0, maximum - selected.Count);
        var firstPendingTarget = selected.Count + pendingCapacity / 2;
        if (freshRunKeys.Count > 0 && retryRunKeys.Count > 0)
        {
            AddWholeRuns(
                selected,
                eligibleRows.Where(row =>
                    freshRunKeys.Contains(EffectiveRunKey(row))),
                firstPendingTarget,
                teacherCompatibilityKey + "|fresh|" + iteration);
            AddWholeRuns(
                selected,
                eligibleRows.Where(row =>
                    retryRunKeys.Contains(EffectiveRunKey(row))),
                maximum,
                teacherCompatibilityKey + "|retry|" + iteration);
        }
        AddWholeRuns(
            selected,
            eligibleRows.Where(row =>
                pendingRunKeys.Contains(EffectiveRunKey(row))),
            maximum,
            teacherCompatibilityKey + "|pending-fill|" + iteration);
        var newFrames = selected.Values.Count(row =>
            pendingIdentities.Contains(row.Identity));
        if (pendingIdentities.Count > 0 && newFrames == 0)
        {
            return new IncrementalTrainingSelection
            {
                PendingFrames = pendingIdentities.Count,
                DeferredFrames = pendingIdentities.Count,
                ReplayEscalationLevel = escalationLevel
            };
        }
        if (selected.Count == 0)
        {
            AddWholeRuns(
                selected,
                eligibleRows,
                Math.Min(maximum, options.IncrementalReplayFrames),
                teacherCompatibilityKey + "|refresh|" + iteration);
        }
        return new IncrementalTrainingSelection
        {
            RowIndices = selected.Keys.OrderBy(index => index).ToList(),
            NewFrames = selected.Values.Count(row =>
                pendingIdentities.Contains(row.Identity)),
            FreshFrames = selected.Values.Count(row =>
                pendingIdentities.Contains(row.Identity)
                && !attemptedIdentities.Contains(row.Identity)),
            RetryFrames = selected.Values.Count(row =>
                pendingIdentities.Contains(row.Identity)
                && attemptedIdentities.Contains(row.Identity)),
            ReplayFrames = selected.Values.Count(row =>
                !pendingIdentities.Contains(row.Identity)),
            PendingFrames = pendingIdentities.Count,
            DeferredFrames = Math.Max(
                0,
                pendingIdentities.Count - selected.Values.Count(row =>
                    pendingIdentities.Contains(row.Identity))),
            ReplayEscalationLevel = escalationLevel
        };
    }

    private static void AddWholeRuns(
        IDictionary<int, CorpusFrameDescriptor> destination,
        IEnumerable<CorpusFrameDescriptor> source,
        int target,
        string seed)
    {
        if (target <= destination.Count)
        {
            return;
        }
        var candidates = source
            .Where(row => !destination.ContainsKey(row.SourceLine))
            .ToList();
        var selectedIndices = CombatTransformerTeacherCorpusProtocol
            .SelectWholeRunRows(
                candidates.Select(row => new CombatTransformerTrainingRow
                {
                    RowIndex = row.SourceLine,
                    RunKey = EffectiveRunKey(row),
                    Identity = row.Identity,
                    Priority = CorpusPriority(row)
                }),
                target - destination.Count,
                seed)
            .ToHashSet();
        foreach (var row in candidates.Where(row =>
                     selectedIndices.Contains(row.SourceLine)))
        {
            destination[row.SourceLine] = row;
        }
    }

    private static HashSet<string> ReadIdentitySet(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.ReadLines(path, Encoding.UTF8)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch
        {
            // A missing/corrupt training watermark must cause conservative
            // catch-up, never make rows look trained.
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static void WriteIdentitySet(
        string path,
        IEnumerable<string> identities)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(
                temporaryPath,
                identities
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal),
                new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static bool CommitTeacherArtifactsAndTrainingWatermark(
        string modelPath,
        string reportPath,
        string persistentModelPath,
        string persistentReportPath,
        string trainedIdentityPath,
        IEnumerable<string>? nextTrainedIdentities,
        out string watermarkError)
    {
        watermarkError = "";

        // The watermark is deliberately last. If either durable artifact
        // publication fails, the exception escapes and every pending frame
        // remains eligible for a conservative retry.
        CopyAtomic(modelPath, persistentModelPath);
        CopyAtomic(reportPath, persistentReportPath);
        if (nextTrainedIdentities == null)
        {
            return true;
        }
        try
        {
            WriteIdentitySet(trainedIdentityPath, nextTrainedIdentities);
            return true;
        }
        catch (Exception exception)
        {
            watermarkError = exception.Message;
            return false;
        }
    }

    private static void ApplyDataQualityAuditMessage(
        CombatTransformerTeacherReport report,
        string baseMessage)
    {
        report.Message = baseMessage;
        if (report.DataQualityWarnings.Count == 0)
        {
            return;
        }
        report.Message = report.Message.TrimEnd()
                         + " Data-quality audit: "
                         + string.Join("; ", report.DataQualityWarnings)
                         + ".";
    }

    private static HashSet<string> ReadAnchorRunKeys(
        string path,
        CancellationToken cancellationToken)
    {
        var descriptors = new Dictionary<string, CorpusFrameDescriptor>(
            StringComparer.Ordinal);
        if (File.Exists(path))
        {
            ReadCorpusDescriptors(
                path,
                current: false,
                descriptors,
                cancellationToken);
        }
        return descriptors.Values
            .Select(EffectiveRunKey)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string EffectiveRunKey(CorpusFrameDescriptor row)
    {
        return string.IsNullOrWhiteSpace(row.RunKey)
            ? row.Identity
            : row.RunKey;
    }

    private static void WriteRowSelection(
        string path,
        IEnumerable<int> rowIndices)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllLines(
                temporaryPath,
                rowIndices
                    .Where(index => index >= 0)
                    .Distinct()
                    .OrderBy(index => index)
                    .Select(index => index.ToString(CultureInfo.InvariantCulture)),
                new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
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
        if (item.StrategyKnown
            && !string.Equals(
                item.Strategy,
                "strategy-baseline",
                StringComparison.Ordinal)
            || item.HasTransition && item.HasObjectTokens)
        {
            return 0;
        }
        if (IsAdvanced(item) && item.BattleIndex is >= 1 and <= 3)
        {
            return 1;
        }
        if (IsAdvanced(item) && !item.Victory)
        {
            return 2;
        }
        return IsAdvanced(item) ? 3 : 4;
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

    private static ulong StableRunRank(string runKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(runKey ?? ""));
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
        string anchorPath,
        string trainingSelectionPath,
        string annotationSelectionPath,
        int corpusFrameCount)
    {
        var start = new ProcessStartInfo
        {
            FileName = CombatFoundationPathRuntime.ForExternalProcess(
                pythonExecutable),
            WorkingDirectory = CombatFoundationPathRuntime.ForFileSystem(
                Path.GetDirectoryName(scriptPath)!),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!CombatFoundationPathRuntime.FileExists(datasetPath))
        {
            throw new FileNotFoundException(
                "Transformer corpus is unavailable before process launch "
                + "(logicalLength="
                + CombatFoundationPathRuntime.Normalize(datasetPath).Length
                + ", externalPath="
                + CombatFoundationPathRuntime.ForExternalProcess(datasetPath)
                + ").",
                datasetPath);
        }
        Add(start, CombatFoundationPathRuntime.ForExternalProcess(scriptPath));
        Add(
            start,
            "--input",
            CombatFoundationPathRuntime.ForExternalProcess(datasetPath));
        Add(
            start,
            "--annotations",
            CombatFoundationPathRuntime.ForExternalProcess(annotationsPath));
        Add(
            start,
            "--model",
            CombatFoundationPathRuntime.ForExternalProcess(modelPath));
        Add(
            start,
            "--report",
            CombatFoundationPathRuntime.ForExternalProcess(reportPath));
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
        Add(
            start,
            "--dataset-storage",
            options.EnableShardedDataset ? "auto" : "resident");
        Add(start, "--dataset-shard-frames", options.DatasetShardFrames);
        Add(start, "--corpus-frames", Math.Max(0, corpusFrameCount));
        Add(
            start,
            "--resident-dataset-maximum-frames",
            options.ResidentDatasetMaximumFrames);
        Add(start, "--pin-memory", options.EnablePinnedMemory ? 1 : 0);
        Add(start, "--mixed-precision", options.EnableMixedPrecision ? 1 : 0);
        Add(
            start,
            "--deterministic",
            options.EnableDeterministicTraining ? 1 : 0);
        Add(
            start,
            "--runtime-cache",
            CombatFoundationPathRuntime.ForExternalProcess(runtimeCachePath));
        Add(
            start,
            "--anchor",
            CombatFoundationPathRuntime.ForExternalProcess(anchorPath));
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
            Add(
                start,
                "--resume-model",
                CombatFoundationPathRuntime.ForExternalProcess(
                    resumeModelPath));
        }
        if (!string.IsNullOrWhiteSpace(trainingSelectionPath))
        {
            Add(
                start,
                "--training-selection",
                CombatFoundationPathRuntime.ForExternalProcess(
                    trainingSelectionPath));
        }
        if (!string.IsNullOrWhiteSpace(annotationSelectionPath))
        {
            Add(
                start,
                "--annotation-selection",
                CombatFoundationPathRuntime.ForExternalProcess(
                    annotationSelectionPath));
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
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var annotation = JsonConvert.DeserializeObject<TeacherAnnotation>(line);
            if (annotation == null
                || annotation.I < 0
                || !byRowIndex.TryGetValue(annotation.I, out var rowBindings))
            {
                continue;
            }
            if (annotation.P == null
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
            foreach (var binding in rowBindings.Where(binding =>
                         annotation.P.Length == binding.Candidates.Count))
            {
                for (var index = 0; index < annotation.P.Length; index++)
                {
                    binding.Candidates[index].TransformerTeacherProbability =
                        annotation.P[index] / total;
                }
                report.AnnotatedFrames++;
                report.AnnotatedCandidates += annotation.P.Length;
            }
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

    private static IReadOnlyList<string> NormalizeStrategyLabels(
        IEnumerable<string>? labels)
    {
        var supported = new HashSet<string>(
            new[] { "survival", "finale", "bank", "transform", "growth" },
            StringComparer.Ordinal);
        return (labels ?? Array.Empty<string>())
            .Select(label => (label ?? "").Trim().ToLowerInvariant())
            .Where(supported.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToArray();
    }

    private static int[] StrategyVector(IReadOnlyCollection<string> labels)
    {
        return new[]
        {
            labels.Contains("survival", StringComparer.Ordinal) ? 1 : 0,
            labels.Contains("finale", StringComparer.Ordinal) ? 1 : 0,
            labels.Contains("bank", StringComparer.Ordinal) ? 1 : 0,
            labels.Contains("transform", StringComparer.Ordinal) ? 1 : 0,
            labels.Contains("growth", StringComparer.Ordinal) ? 1 : 0
        };
    }

    private static string PrimaryStrategy(IReadOnlyCollection<string> labels)
    {
        foreach (var label in new[]
                 {
                     "survival", "finale", "bank", "transform", "growth"
                 })
        {
            if (labels.Contains(label, StringComparer.Ordinal))
            {
                return "strategy-" + label;
            }
        }
        return "strategy-baseline";
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
        target.CudaFallbackTriggered = source.CudaFallbackTriggered;
        target.CudaFallbackReason = source.CudaFallbackReason;
        target.EffectiveCpuThreads = source.EffectiveCpuThreads;
        target.EffectiveCpuInteropThreads = source.EffectiveCpuInteropThreads;
        target.EffectiveBatchSize = source.EffectiveBatchSize;
        target.EffectiveMicroBatchSize = source.EffectiveMicroBatchSize;
        target.EffectiveDataLoaderWorkers =
            source.EffectiveDataLoaderWorkers;
        target.EffectivePrefetchBatches = source.EffectivePrefetchBatches;
        target.PinnedMemoryEnabled = source.PinnedMemoryEnabled;
        target.NumericPrecision = source.NumericPrecision;
        target.DeterministicTrainingEnabled =
            source.DeterministicTrainingEnabled;
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
        target.ValidationPhaseAccuracy = source.ValidationPhaseAccuracy;
        target.ValidationStrategyAccuracy =
            source.ValidationStrategyAccuracy;
        target.ValidationDynamicsMse = source.ValidationDynamicsMse;
        target.DynamicsTrainingFrames = source.DynamicsTrainingFrames;
        target.DynamicsValidationFrames = source.DynamicsValidationFrames;
        target.InvalidTransitionFrames = source.InvalidTransitionFrames;
        target.TerminalKnownFrames = source.TerminalKnownFrames;
        target.StrategyLabelFrames = source.StrategyLabelFrames;
        target.StrategyLabelCounts = new Dictionary<string, int>(
            source.StrategyLabelCounts ?? new Dictionary<string, int>(),
            StringComparer.Ordinal);
        target.StrategyApplicableFrames = source.StrategyApplicableFrames;
        target.StrategyApplicableCounts = new Dictionary<string, int>(
            source.StrategyApplicableCounts ?? new Dictionary<string, int>(),
            StringComparer.Ordinal);
        target.StrategyNegativeCounts = new Dictionary<string, int>(
            source.StrategyNegativeCounts ?? new Dictionary<string, int>(),
            StringComparer.Ordinal);
        target.StrategyQualityGatePassed = source.StrategyQualityGatePassed;
        target.ValidationOutcomeMae = source.ValidationOutcomeMae;
        target.ValidationDeathBrier = source.ValidationDeathBrier;
        target.ValidationTerminalAccuracy = source.ValidationTerminalAccuracy;
        target.ElapsedSeconds = source.ElapsedSeconds;
        target.ProcessCpuSeconds = source.ProcessCpuSeconds;
        target.PeakWorkingSetBytes = source.PeakWorkingSetBytes;
        target.DatasetStorageMode = source.DatasetStorageMode;
        target.DatasetShardFrames = source.DatasetShardFrames;
        target.DatasetEncoding = source.DatasetEncoding;
        target.LoadedDatasetFrames = source.LoadedDatasetFrames;
        target.IncrementalTrainingSelection =
            source.IncrementalTrainingSelection;
        target.IncrementalTrainingFrames = source.IncrementalTrainingFrames;
        target.AnnotationSelectionFrames = source.AnnotationSelectionFrames;
        target.DenseFeatureSlots = source.DenseFeatureSlots;
        target.NonZeroFeatureValues = source.NonZeroFeatureValues;
        target.SparseFeatureDensity = source.SparseFeatureDensity;
        target.ObjectTokenFrames = source.ObjectTokenFrames;
        target.EmptyObjectTokenFrames = source.EmptyObjectTokenFrames;
        target.ObjectTokenFrameCoverage = source.ObjectTokenFrameCoverage;
        target.ObjectTokenAuditPassed = source.ObjectTokenAuditPassed;
        target.ObjectTokenAuditAdvisoryOnly =
            source.ObjectTokenAuditAdvisoryOnly;
        target.DataQualityWarnings = new List<string>(
            source.DataQualityWarnings ?? new List<string>());
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

    internal static bool HasAcceptedTeacherArtifactForWarmStart(
        string modelPath,
        CombatTransformerTeacherReport? report,
        string expectedTeacherCompatibilityKey)
    {
        return File.Exists(modelPath)
               && report?.Applied == true
               && report.TeacherGeneration > 0
               && string.Equals(
                   report.TeacherCompatibilityKey,
                   expectedTeacherCompatibilityKey,
                   StringComparison.Ordinal);
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

    private static string FileSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
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

    private static int ConsecutiveRejectedTeacherUpdates(
        string resultDirectory,
        int iteration,
        string teacherCompatibilityKey)
    {
        var streak = 0;
        for (var prior = Math.Max(1, iteration - 1); prior >= 1; prior--)
        {
            var path = Path.Combine(
                resultDirectory,
                "transformer-teacher",
                "iteration-" + prior.ToString("D2"),
                "world-model-report-v2.json");
            var report = ReadPreviousReport(path);
            if (report == null)
            {
                continue;
            }
            if (!string.Equals(
                    report.TeacherCompatibilityKey,
                    teacherCompatibilityKey,
                    StringComparison.Ordinal))
            {
                break;
            }
            if (!report.TrainingRefreshed)
            {
                continue;
            }
            if (report.UpdateAccepted)
            {
                break;
            }
            streak++;
        }
        return streak;
    }

    private static int LastTeacherAttemptIteration(
        string resultDirectory,
        int iteration,
        string teacherCompatibilityKey)
    {
        for (var prior = Math.Max(1, iteration - 1); prior >= 1; prior--)
        {
            var path = Path.Combine(
                resultDirectory,
                "transformer-teacher",
                "iteration-" + prior.ToString("D2"),
                "world-model-report-v2.json");
            var report = ReadPreviousReport(path);
            if (report == null)
            {
                continue;
            }
            if (!string.Equals(
                    report.TeacherCompatibilityKey,
                    teacherCompatibilityKey,
                    StringComparison.Ordinal))
            {
                break;
            }
            if (report.TrainingRefreshed)
            {
                return prior;
            }
        }
        return 0;
    }

    private static void TryDeleteCorpusGenerationDirectory(
        string path,
        string generationRoot)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(generationRoot);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(
                    Path.GetDirectoryName(fullPath),
                    fullRoot,
                    comparison)
                || !Directory.Exists(fullPath))
            {
                return;
            }
            var directory = new DirectoryInfo(fullPath);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0
                || directory.EnumerateDirectories().Any())
            {
                return;
            }
            foreach (var file in directory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return;
                }
            }
            foreach (var file in directory.EnumerateFiles())
            {
                file.Delete();
            }
            directory.Delete(recursive: false);
        }
        catch
        {
            // An abandoned staging/retired generation is never selected by
            // the active pointer, so cleanup failure is safe to retry later.
        }
    }

    private static void WriteTextAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(
                temporary,
                contents,
                new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private sealed class CorpusFrameDescriptor
    {
        public string Identity { get; set; } = "";

        public string RunKey { get; set; } = "";

        public string Strategy { get; set; } = "strategy-baseline";

        public string Difficulty { get; set; } = "normal";

        public int BattleIndex { get; set; }

        public bool Victory { get; set; }

        public bool HasObjectTokens { get; set; }

        public bool HasTransition { get; set; }

        public bool TransitionContractKnown { get; set; }

        public bool StrategyKnown { get; set; }

        public string SourcePath { get; set; } = "";

        public int SourceLine { get; set; }

        public bool Current { get; set; }
    }

    private sealed class CorpusMergeResult
    {
        public string CorpusPath { get; set; } = "";

        public int FrameCount { get; set; }

        public int CurrentFrames { get; set; }

        public int ReusedFrames { get; set; }

        public int DeduplicatedFrames { get; set; }

        public int DroppedFrames { get; set; }

        public int BacklogFrames { get; set; }

        public string Fingerprint { get; set; } = "";

        public Dictionary<string, int> StrategyFrames { get; set; } =
            new(StringComparer.Ordinal);
    }

    private sealed class ValidatedCorpusSnapshot
    {
        public int FrameCount { get; set; }

        public string Fingerprint { get; set; } = "";

        public Dictionary<string, int> StrategyFrames { get; set; } =
            new(StringComparer.Ordinal);

        public HashSet<string> Identities { get; set; } =
            new(StringComparer.Ordinal);

        public int BacklogFrameCount { get; set; }

        public HashSet<string> BacklogIdentities { get; set; } =
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

        public long ContentLengthBytes { get; set; }

        public string ContentSha256 { get; set; } = "";

        public int BacklogFrameCount { get; set; }

        public long BacklogContentLengthBytes { get; set; }

        public string BacklogContentSha256 { get; set; } = "";

        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class TeacherCorpusGenerationPointer
    {
        public string Protocol { get; set; } =
            CorpusGenerationPointerProtocol;

        public string CompatibilityKey { get; set; } = "";

        public string Generation { get; set; } = "";
    }

    private sealed class CorpusGenerationSnapshot
    {
        public string Generation { get; set; } = "";

        public string DirectoryPath { get; set; } = "";

        public string CorpusPath { get; set; } = "";

        public string BacklogPath { get; set; } = "";

        public string IdentityIndexPath { get; set; } = "";

        public string ManifestPath { get; set; } = "";

        public TeacherCorpusManifest Manifest { get; set; } = new();

        public ValidatedCorpusSnapshot Snapshot { get; set; } = new();
    }

    private sealed class FrameBinding
    {
        public CombatEpisodeFrame Frame { get; set; } = new();

        public List<CombatEpisodeCandidate> Candidates { get; set; } = new();

        public string Strategy { get; set; } = "";

        public string Identity { get; set; } = "";

        public int RowIndex { get; set; } = -1;

        public bool ExportedToCurrentDataset { get; set; }
    }

    private sealed class IncrementalTrainingSelection
    {
        public List<int> RowIndices { get; set; } = new();

        public int NewFrames { get; set; }

        public int FreshFrames { get; set; }

        public int RetryFrames { get; set; }

        public int ReplayFrames { get; set; }

        public int PendingFrames { get; set; }

        public int DeferredFrames { get; set; }

        public int ReplayEscalationLevel { get; set; }
    }

    private sealed class CorpusRunDescriptor
    {
        public string RunKey { get; set; } = "";

        public List<CorpusFrameDescriptor> Frames { get; set; } = new();

        public bool Protected { get; set; }

        public bool Advanced { get; set; }

        public int Priority { get; set; }
    }

    private sealed class SparseFeatureVector
    {
        public int D { get; set; }

        public int[] I { get; set; } = Array.Empty<int>();

        public float[] V { get; set; } = Array.Empty<float>();

        public static SparseFeatureVector Encode(IReadOnlyList<double> values)
        {
            var indices = new List<int>();
            var nonZeroValues = new List<float>();
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new InvalidDataException(
                        "Transformer sparse feature contains a non-finite "
                        + "value at index "
                        + index.ToString(CultureInfo.InvariantCulture)
                        + ".");
                }
                if (value == 0d)
                {
                    continue;
                }
                var compact = (float)value;
                if (float.IsNaN(compact) || float.IsInfinity(compact))
                {
                    throw new InvalidDataException(
                        "Transformer sparse feature exceeds finite FP32 "
                        + "range at index "
                        + index.ToString(CultureInfo.InvariantCulture)
                        + ".");
                }
                indices.Add(index);
                nonZeroValues.Add(compact);
            }
            return new SparseFeatureVector
            {
                D = values.Count,
                I = indices.ToArray(),
                V = nonZeroValues.ToArray()
            };
        }
    }

    private sealed class TeacherDatasetRow
    {
        public int I { get; set; }
        public int E { get; set; }
        public string Y { get; set; } = "";
        public int F { get; set; }
        public int T { get; set; }
        public long Q { get; set; }
        public long QD { get; set; }
        public string D { get; set; } = "";
        public string L { get; set; } = "";
        public string C { get; set; } = "normal";
        public int B { get; set; }
        public int J { get; set; }
        public int OK { get; set; }
        public int DK { get; set; }
        public int SK { get; set; }
        public int TK { get; set; }
        public int M { get; set; }
        public SparseFeatureVector S { get; set; } = new();
        public SparseFeatureVector[] O { get; set; } =
            Array.Empty<SparseFeatureVector>();
        public SparseFeatureVector[] A { get; set; } =
            Array.Empty<SparseFeatureVector>();
        public double[] P { get; set; } = Array.Empty<double>();
        public int X { get; set; }
        public double V { get; set; }
        public int G { get; set; }
        public int[] SL { get; set; } = new int[5];
        public int[] SA { get; set; } = new int[5];

        public double K { get; set; } = 1d;
        public SparseFeatureVector N { get; set; } = new();
        public int TS { get; set; }
        public int TB { get; set; }
        public long AD { get; set; }
        public string TR { get; set; } = "";
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
