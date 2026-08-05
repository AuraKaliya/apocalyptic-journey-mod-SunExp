using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraFoundationTrainer.Worker;
using AuraToolsExp.Dll.Features.AutoBattle;
using Newtonsoft.Json;

Console.OutputEncoding = Encoding.UTF8;
var jobPath = ResolveArgument(args, "--job");
if (string.IsNullOrWhiteSpace(jobPath) || !File.Exists(jobPath))
{
    Console.Error.WriteLine("Usage: AuraFoundationTrainer.Worker --job <job.json>");
    return 2;
}

CombatFoundationWorkerJob? job = null;
var checkpointWriteFailures = 0;
var checkpointWarning = "";
var trainingMetricWriteFailures = 0;
var trainingMetricWarning = "";
try
{
    job = Deserialize<CombatFoundationWorkerJob>(
        CombatFoundationCheckpointStorage.ReadAllTextShared(jobPath));
    if (!CombatFoundationWorkerProtocol.TryValidateJob(
            job,
            out var jobDiagnostic))
    {
        throw new InvalidOperationException(jobDiagnostic);
    }
    job = job ?? throw new InvalidOperationException("底模训练任务为空");
    AuraToolsAuthoritativeRoleSemantics.Initialize();
    AuraToolsRoleCampaignStrategy.Apply(job.Request.TrainingCampaign);
    AuraToolsRoleCampaignStrategy.Apply(job.Request.ValidationCampaign);
    // Archive residuals are learned data and change after every accepted run.
    // Capture the structural workload identity before those residuals are
    // merged so an execution plan remains reusable across training rounds.
    job.Request.AutoTuneCampaignKey =
        CombatCampaignFoundationTrainer.CampaignFingerprint(
            job.Request.TrainingCampaign);
    // The external worker persists validation aggregates and case artifacts.
    // Full validation battle graphs are process-local diagnostics and must not
    // accumulate across hundreds of validation campaigns.
    job.Request.RetainValidationRunDetails = false;
    Directory.CreateDirectory(job.ResultDirectory);
    if (string.IsNullOrWhiteSpace(job.TrainingMetricsPath))
    {
        job.TrainingMetricsPath = Path.Combine(
            job.ResultDirectory,
            CombatFoundationWorkerProtocol.TrainingMetricsFileName);
    }
    if (string.IsNullOrWhiteSpace(job.TrainingAnalysisPath))
    {
        job.TrainingAnalysisPath = Path.Combine(
            job.ResultDirectory,
            CombatFoundationWorkerProtocol.TrainingAnalysisFileName);
    }
    job.Request.IncludeMetricHistoryInTelemetry = false;
    if (string.IsNullOrWhiteSpace(job.CheckpointPath))
    {
        job.CheckpointPath = Path.Combine(
            job.ResultDirectory,
            CombatFoundationWorkerProtocol.CheckpointFileName);
    }
    if (string.IsNullOrWhiteSpace(job.CheckpointEpisodesPath))
    {
        job.CheckpointEpisodesPath = Path.Combine(
            job.ResultDirectory,
            CombatFoundationWorkerProtocol.CheckpointEpisodesFileName);
    }
    using var trainingLease = AcquireTrainingLease(job);
    var build = CombatSimulationRegistry.BuildRuleset(job.Ruleset);
    if (!build.Success)
    {
        throw new InvalidOperationException(
            "Ruleset build failed: " + string.Join("; ", build.Errors.Take(8)));
    }
    if (!string.IsNullOrWhiteSpace(job.ExpectedRulesetHash)
        && !string.Equals(
            build.Ruleset.RulesetHash,
            job.ExpectedRulesetHash,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Ruleset hash mismatch: expected="
            + job.ExpectedRulesetHash
            + ", actual="
            + build.Ruleset.RulesetHash);
    }

    var package = AuraToolsNativeProgramPackageAudit.Validate(
        job.Request.TrainingCampaign,
        build.Ruleset);
    if (!package.Success)
    {
        throw new InvalidOperationException(
            "Native program package validation failed: "
            + string.Join("; ", package.Errors.Take(8)));
    }
    if (!string.IsNullOrWhiteSpace(job.Request.NativeProgramPackageHash)
        && !string.Equals(
            job.Request.NativeProgramPackageHash,
            package.ProgramSetSha256,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Native program package hash mismatch: expected="
            + job.Request.NativeProgramPackageHash
            + ", actual="
            + package.ProgramSetSha256);
    }
    job.Request.NativeProgramPackageHash = package.ProgramSetSha256;
    var simulationEngine = new CombatSimulationEngine(
        new AuraToolsNativeRewardExtensionFactory());
    var semanticProbe = CombatFoundationSemanticProbe.Validate(
        job.Request.TrainingCampaign,
        build.Ruleset,
        simulationEngine,
        requireNativeProgramCanary: true);
    if (!semanticProbe.Success)
    {
        throw new InvalidOperationException(
            "Training semantic probe failed: "
            + string.Join("; ", semanticProbe.Errors.Take(8)));
    }
    Console.WriteLine(
        "Training semantic probe passed: "
        + semanticProbe.Version
        + ", canary="
        + semanticProbe.CanaryVersion);
    var rolePreparationErrors =
        AuraToolsAuthoritativeRoleSemantics.ValidateFrozenTrainingPreparation();
    if (rolePreparationErrors.Count > 0)
    {
        throw new InvalidOperationException(
            "Frozen role preparation probe failed: "
            + string.Join("; ", rolePreparationErrors.Take(8)));
    }
    Console.WriteLine("Frozen role preparation probe passed.");
    var workerAssemblyPath = Environment.ProcessPath;
    var workerBinarySha256 =
        string.IsNullOrWhiteSpace(workerAssemblyPath)
        || !File.Exists(workerAssemblyPath)
            ? ""
            : Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(workerAssemblyPath)));

    var requestedWorkers = Math.Max(
        1,
        Math.Min(Environment.ProcessorCount, job.Request.MaximumDegreeOfParallelism));
    ThreadPool.GetMinThreads(out var minimumWorkers, out var minimumIo);
    ThreadPool.SetMinThreads(
        Math.Max(
            minimumWorkers,
            Math.Max(
                requestedWorkers + 2,
                job.Request.ThreadPoolMinimumWorkerThreads)),
        minimumIo);
    var requestFingerprint = Fingerprint(job, build.Ruleset.RulesetHash);
    var resume = new CombatCampaignFoundationResumeState();
    CombatFoundationEpisodeSnapshot? checkpointSnapshot = null;
    var resumeDiagnostic = "";
    var resumedFromCheckpoint = job.ResumeFromCheckpoint
                                && TryLoadCheckpoint(
            job,
            requestFingerprint,
            build.Ruleset.RulesetHash,
            out resume,
            out checkpointSnapshot,
            out resumeDiagnostic);
    if (resumedFromCheckpoint)
    {
        job.Request.Resume = resume;
        Console.WriteLine(
            "Foundation worker resumed: stage="
            + resume.Stage
            + ", iteration="
            + resume.NextIteration
            + ", campaigns="
            + resume.CompletedCampaigns
            + ", episodes="
            + resume.Replay.Count);
        if (!string.IsNullOrWhiteSpace(resumeDiagnostic))
        {
            Console.WriteLine(
                "Foundation checkpoint continuation: "
                + resumeDiagnostic);
        }
    }
    else if (job.ResumeFromCheckpoint)
    {
        CombatFoundationCheckpointStorage.DeleteCheckpointArtifacts(
            job.CheckpointPath,
            job.CheckpointEpisodesPath);
        Console.Error.WriteLine(
            "Foundation checkpoint was incompatible and has been discarded: "
            + resumeDiagnostic);
    }
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        job.CheckpointPath,
        job.CheckpointEpisodesPath,
        checkpointSnapshot == null
            ? Array.Empty<string>()
            : new[] { checkpointSnapshot.Path });
    PrepareCaseArchive(job, build.Ruleset.RulesetHash);
    var metricGate = new object();
    using var metricStream = new FileStream(
        job.TrainingMetricsPath,
        FileMode.Append,
        FileAccess.Write,
        FileShare.Read);
    using var metricWriter = new StreamWriter(
        metricStream,
        new UTF8Encoding(false),
        16 * 1024,
        leaveOpen: false)
    {
        AutoFlush = true
    };
    job.Request.ModelMetricRecorded = metrics =>
    {
        lock (metricGate)
        {
            try
            {
                metricWriter.WriteLine(SerializeCompact(
                    new CombatFoundationTrainingMetricRecord
                    {
                        JobId = job.JobId,
                        RecordedUtc = DateTime.UtcNow,
                        RulesetHash = build.Ruleset.RulesetHash,
                        NativeProgramPackageHash =
                            job.Request.NativeProgramPackageHash,
                        ContentSetHash = job.Request.ContentSetHash,
                        OwnerModSetHash = job.Request.OwnerModSetHash,
                        Metrics = metrics
                    }));
                trainingMetricWarning = "";
            }
            catch (Exception ex)
            {
                trainingMetricWriteFailures++;
                trainingMetricWarning =
                    "训练指标暂时无法写入，训练继续："
                    + ex.Message;
                Console.Error.WriteLine(trainingMetricWarning);
            }
        }
    };

    using var cancellation = new CancellationTokenSource();
    using var cancellationTimer = new Timer(
        _ =>
        {
            if (!string.IsNullOrWhiteSpace(job.CancellationPath)
                && File.Exists(job.CancellationPath))
            {
                cancellation.Cancel();
            }
        },
        null,
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(250));
    var progressGate = new object();
    var progressClock = Stopwatch.StartNew();
    var lastProgressMilliseconds = -1000L;
    var lastProgressStage = "";
    CombatCampaignFoundationTelemetry? latestTelemetry = null;
    job.Request.Telemetry = telemetry =>
    {
        lock (progressGate)
        {
            var now = progressClock.ElapsedMilliseconds;
            var stageChanged = !string.Equals(
                lastProgressStage,
                telemetry.Stage,
                StringComparison.Ordinal);
            if (!stageChanged
                && now - lastProgressMilliseconds < 500L
                && telemetry.CompletedCampaigns < telemetry.RequestedCampaigns)
            {
                return;
            }
            lastProgressMilliseconds = now;
            lastProgressStage = telemetry.Stage ?? "";
            latestTelemetry = telemetry;
            TryWriteAuxiliary(
                job.ProgressPath,
                Serialize(new CombatFoundationWorkerProgress
                {
                    JobId = job.JobId,
                    UpdatedUtc = DateTime.UtcNow,
                    Telemetry = telemetry
                }));
        }
    };
    using var progressHeartbeat = new Timer(
        _ =>
        {
            lock (progressGate)
            {
                if (latestTelemetry == null
                    || progressClock.ElapsedMilliseconds
                       - lastProgressMilliseconds < 2000L)
                {
                    return;
                }
                lastProgressMilliseconds = progressClock.ElapsedMilliseconds;
                TryWriteAuxiliary(
                    job.ProgressPath,
                    Serialize(new CombatFoundationWorkerProgress
                    {
                        JobId = job.JobId,
                        UpdatedUtc = DateTime.UtcNow,
                        Telemetry = latestTelemetry
                    }));
            }
        },
        null,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(2));
    var checkpointReplayIdentity =
        checkpointSnapshot?.ReplayIdentity ?? "";
    if (string.IsNullOrWhiteSpace(checkpointReplayIdentity)
        && job.Request.Resume?.Replay != null)
    {
        checkpointReplayIdentity =
            ReplayIdentity(job.Request.Resume.Replay);
    }
    // Snapshot JSON conversion runs concurrently with campaign/model work.
    // Reserve the majority of the worker budget for training itself.
    var checkpointSerializationWorkers = Math.Max(
        1,
        Math.Min(
            2,
            job.Request.CheckpointSerializationParallelism <= 0
                ? requestedWorkers >= 32 ? 2 : 1
                : job.Request.CheckpointSerializationParallelism));
    var checkpointSerializationAutomatic =
        job.Request.CheckpointSerializationParallelism <= 0;
    var checkpointSerializationAutoScaled = false;
    var checkpointSerializationSeconds = 0d;
    using var checkpointPipeline =
        new CombatFoundationLatestWritePipeline<
            CombatCampaignFoundationResumeState>(state =>
        {
            var checkpointStarted = Stopwatch.StartNew();
            try
            {
                var replayIdentity = ReplayIdentity(state.Replay);
                var nextSnapshot = checkpointSnapshot;
                if (nextSnapshot == null
                    || !File.Exists(nextSnapshot.Path)
                    || !string.Equals(
                        checkpointReplayIdentity,
                        replayIdentity,
                        StringComparison.Ordinal))
                {
                    nextSnapshot =
                        CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
                            job.CheckpointEpisodesPath,
                            state.Replay,
                            SerializeCompact,
                            replayIdentity,
                            checkpointSerializationWorkers);
                }
                CombatFoundationCheckpointStorage.WriteAtomicText(
                    job.CheckpointPath,
                    Serialize(new CombatFoundationWorkerCheckpoint
                    {
                        RequestFingerprint = requestFingerprint,
                        RulesetHash = build.Ruleset.RulesetHash,
                        EpisodesPath = nextSnapshot.Path,
                        EpisodeSnapshot = nextSnapshot,
                        UpdatedUtc = DateTime.UtcNow,
                        Resume = WithoutReplay(state)
                    }));
                checkpointSnapshot = nextSnapshot;
                checkpointReplayIdentity = replayIdentity;
                checkpointWarning = "";
                CombatFoundationCheckpointStorage.CleanupArtifacts(
                    job.CheckpointPath,
                    job.CheckpointEpisodesPath,
                    new[] { nextSnapshot.Path });
                if (checkpointSerializationAutomatic
                    && checkpointSerializationWorkers == 1
                    && requestedWorkers >= 12
                    && (state.Replay?.Count ?? 0) >= 512
                    && checkpointStarted.Elapsed.TotalSeconds >= 1.5d)
                {
                    checkpointSerializationWorkers = 2;
                    checkpointSerializationAutoScaled = true;
                }
            }
            catch (Exception ex)
            {
                checkpointWriteFailures++;
                checkpointWarning =
                    "检查点暂时无法写入，训练继续使用上一份有效快照："
                    + ex.Message;
                Console.Error.WriteLine(checkpointWarning);
            }
            finally
            {
                checkpointStarted.Stop();
                checkpointSerializationSeconds +=
                    checkpointStarted.Elapsed.TotalSeconds;
            }
        });
    job.Request.Checkpoint = checkpointPipeline.Enqueue;
    var incrementallyArchivedCases =
        new HashSet<string>(StringComparer.Ordinal);
    var capacityRejectedCaseIds =
        new HashSet<string>(StringComparer.Ordinal);
    var incrementalArchiveErrors = new List<string>();
    var archiveCapacityRejectedObservations = 0;
    var archiveCapacityRejectedCases = 0;

    var autoTuneCachePath = Path.Combine(
        job.SuccessArchiveDirectory,
        CombatFoundationAutoTuneProtocol.CacheFileName);
    job.Request.AutoTuneHardwareKey = AutoTuneHardwareKey();
    if (string.Equals(
            job.Request.ParallelismProfile,
            CombatFoundationExecutionProfileNames.Auto,
            StringComparison.OrdinalIgnoreCase))
    {
        if (job.Request.ReuseAutoTuneCache
            && File.Exists(autoTuneCachePath))
        {
            try
            {
                job.Request.AutoTuneCache =
                    Deserialize<CombatFoundationAutoTuneResult>(
                        CombatFoundationCheckpointStorage.ReadAllTextShared(
                            autoTuneCachePath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "Auto-tune cache ignored: " + ex.Message);
            }
        }
        job.Request.AutoTuneCompleted = result =>
        {
            if (result == null || result.LowConfidence)
            {
                return;
            }
            Directory.CreateDirectory(job.SuccessArchiveDirectory);
            WriteAtomicJson(autoTuneCachePath, result);
        };
    }

    ICombatTransformerTeacher? transformerTeacher = null;
    var transformerOptions = (job.Request.TransformerTeacher
                              ?? new CombatTransformerTeacherOptions())
        .Normalized();
    job.Request.TransformerTeacher = transformerOptions;
    if (!string.Equals(
            transformerOptions.Backend,
            CombatTransformerTeacherBackendNames.Disabled,
            StringComparison.OrdinalIgnoreCase))
    {
        transformerTeacher = new PythonCombatTransformerTeacher(
            job.ResultDirectory,
            ResolveTransformerTeacherScript(),
            Path.Combine(
                job.SuccessArchiveDirectory,
                "transformer-runtime-auto-tune-v1.json"));
    }

    var training = new CombatCampaignFoundationTrainer(
        new CombatCampaignRunner(simulationEngine),
        transformerTeacher).Run(
        job.Request,
        build.Ruleset,
        job.InitialChampion,
        cancellation.Token);
    checkpointPipeline.Drain();
    var roleStrategyMetrics =
        AuraToolsRoleTrainingDiagnostics.Analyze(
            training.Replay,
            training.CampaignObservations);
    var roleStrategyGateFailures = new List<string>();
    var isNanaTraining = string.Equals(
                             job.Request.TrainingCampaign.Player?.RoleId,
                             "career_2",
                             StringComparison.OrdinalIgnoreCase)
                         || string.Equals(
                             job.Request.TrainingCampaign.Player?.RoleId,
                             "career_4",
                             StringComparison.OrdinalIgnoreCase);
    if (isNanaTraining
        && roleStrategyMetrics.GetValueOrDefault(
            "nana.role-strategy-eligible-frames") > 0d
        && roleStrategyMetrics.GetValueOrDefault(
            "nana.role-strategy-frame-coverage") < 0.999999d)
    {
        roleStrategyGateFailures.Add(
            "Nana actionable-frame role-strategy coverage is incomplete.");
    }
    if (isNanaTraining
        && roleStrategyMetrics.GetValueOrDefault(
            "nana.selected-strategically-prohibited-actions") > 0d)
    {
        roleStrategyGateFailures.Add(
            "Nana selected one or more strategically prohibited actions.");
    }
    if (isNanaTraining
        && roleStrategyMetrics.GetValueOrDefault(
            "nana.selected-nonpositive-devours") > 0d)
    {
        roleStrategyGateFailures.Add(
            "Nana selected one or more non-positive Devour lines.");
    }
    if (isNanaTraining
        && roleStrategyMetrics.GetValueOrDefault("nana.devours") >= 20d
        && roleStrategyMetrics.GetValueOrDefault(
            "nana.premature-devour-rate") > 0.05d)
    {
        roleStrategyGateFailures.Add(
            "Nana premature Devour rate exceeded 5%.");
    }
    var roleStrategyGatePassed = roleStrategyGateFailures.Count == 0;
    var roleStrategyGateFailureReason = string.Join(
        " ",
        roleStrategyGateFailures);
    if (!roleStrategyGatePassed)
    {
        training.Success = false;
        training.AcceptancePassed = false;
        training.Message = string.IsNullOrWhiteSpace(training.Message)
            ? roleStrategyGateFailureReason
            : training.Message + " " + roleStrategyGateFailureReason;
    }
    try
    {
        if (job.Request.EnableSuccessCaseArchive
            && roleStrategyGatePassed)
        {
            PersistSuccessCases(
                job,
                training,
                incrementallyArchivedCases,
                capacityRejectedCaseIds,
                incrementalArchiveErrors,
                archiveCapacityRejectedObservations,
                archiveCapacityRejectedCases);
        }
    }
    catch (Exception ex)
    {
        training.CaseAnalysis = CombatFoundationCaseLearning.Analyze(
            training.CampaignObservations);
        training.SuccessArchiveError = ex.ToString();
        Console.Error.WriteLine(
            "Foundation success archive failed without invalidating training: "
            + ex);
    }
    try
    {
        PersistBuildLimitedSeeds(job, training);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            "Foundation build-limited seed index was skipped: "
            + ex.Message);
    }
    var episodesPath = Path.Combine(
        job.ResultDirectory,
        "foundation-training-episodes-v4.jsonl");
    training.GeneratedReplayEpisodes = Math.Max(
        training.GeneratedReplayEpisodes,
        training.Replay.Count);
    training.PersistedReplayEpisodes = training.Replay.Count;
    var trainingAnalysis = BuildTrainingAnalysis(job, training);
    trainingAnalysis.RoleStrategyMetrics = roleStrategyMetrics;
    trainingAnalysis.RoleStrategyGatePassed = roleStrategyGatePassed;
    trainingAnalysis.RoleStrategyGateFailureReason =
        roleStrategyGateFailureReason;
    WriteEpisodes(episodesPath, training.Replay);
    training.Replay.Clear();
    training.CampaignObservations.Clear();
    training.SuccessCases.Clear();
    if (job.Request.PreflightOnly
        || training.AcceptancePassed
        || !roleStrategyGatePassed)
    {
        CombatFoundationCheckpointStorage.DeleteCheckpointArtifacts(
            job.CheckpointPath,
            job.CheckpointEpisodesPath);
    }
    var resumableEpisodesPath = "";
    var resumable = !job.Request.PreflightOnly
                    && !training.AcceptancePassed
                    && TryGetResumableCheckpoint(
                        job,
                        out resumableEpisodesPath);
    var completionKind = job.Request.PreflightOnly
        ? training.Success
            ? "preflight-passed"
            : "preflight-failed"
        : training.AcceptancePassed
            ? "training-accepted"
            : resumable
                ? "training-rejected-resumable"
                : "training-rejected";
    var workerResult = new CombatFoundationWorkerResult
    {
        JobId = job.JobId,
        Success = true,
        WorkerCompleted = true,
        TrainingSucceeded = training.Success,
        ModelAccepted = training.AcceptancePassed,
        EpochsExecuted = training.ModelEpochHistory.Count(item =>
            !item.Calibrated),
        SelectedEpoch = training.ModelBestEpoch,
        PersistedReplayEpisodes = training.PersistedReplayEpisodes,
        CheckpointBytes = resumable
            ? new[] { job.CheckpointPath, resumableEpisodesPath }
                .Where(File.Exists)
                .Sum(path => new FileInfo(path).Length)
            : 0L,
        CompletionKind = completionKind,
        Message = training.Message,
        Runtime = RuntimeDescription(requestedWorkers),
        RulesetHash = build.Ruleset.RulesetHash,
        EpisodesPath = episodesPath,
        CheckpointPath = resumable
            ? job.CheckpointPath
            : "",
        Resumable = resumable,
        CheckpointWriteFailures = checkpointWriteFailures,
        CheckpointWarning = checkpointWarning,
        EffectiveCheckpointSerializationParallelism =
            checkpointSerializationWorkers,
        CheckpointSerializationAutoScaled =
            checkpointSerializationAutoScaled,
        CheckpointSerializationSeconds = checkpointSerializationSeconds,
        CheckpointWritesEnqueued = checkpointPipeline.EnqueuedCount,
        CheckpointWritesExecuted = checkpointPipeline.ExecutedCount,
        CheckpointWritesCoalesced = checkpointPipeline.CoalescedCount,
        TrainingMetricsPath = job.TrainingMetricsPath,
        TrainingAnalysisPath = job.TrainingAnalysisPath,
        TrainingMetricWriteFailures = trainingMetricWriteFailures,
        TrainingMetricWarning = trainingMetricWarning,
        RoleStrategyMetrics = roleStrategyMetrics,
        RoleStrategyGatePassed = roleStrategyGatePassed,
        RoleStrategyGateFailureReason = roleStrategyGateFailureReason,
        ResumeRequested = job.ResumeFromCheckpoint,
        ResumedFromCheckpoint = resumedFromCheckpoint,
        ResumeDiagnostic = resumeDiagnostic,
        Training = training
    };
    if (string.Equals(
            completionKind,
            "training-accepted",
            StringComparison.Ordinal))
    {
        var modelPackage = CombatFoundationModelPackageProtocol.Create(
            job,
            workerResult,
            workerBinarySha256);
        workerResult.ModelPackagePath = Path.Combine(
            job.ResultDirectory,
            CombatFoundationModelPackageProtocol.FileName);
        WriteAtomicJson(workerResult.ModelPackagePath, modelPackage);
    }
    WriteAtomicJson(
        job.TrainingAnalysisPath,
        trainingAnalysis);
    training.ValidationRuns.Clear();
    WriteAtomicJson(job.ResultPath, workerResult);
    Console.WriteLine(
        "Foundation worker completed: campaigns="
        + training.CompletedCampaigns
        + "/"
        + training.RequestedCampaigns
        + ", battles="
        + training.CompletedBattles);
    return 0;
}
catch (OperationCanceledException)
{
    if (job != null)
    {
        var resumable = TryGetResumableCheckpoint(
            job,
            out var resumableEpisodesPath);
        WriteAtomicJson(
            job.ResultPath,
            new CombatFoundationWorkerResult
            {
                JobId = job.JobId,
                WorkerCompleted = true,
                Cancelled = true,
                CompletionKind = resumable
                    ? "cancelled-resumable"
                    : "cancelled",
                Message = "Foundation training cancelled.",
                Runtime = RuntimeDescription(
                    job.Request.MaximumDegreeOfParallelism),
                RulesetHash = job.ExpectedRulesetHash,
                EpisodesPath = resumable ? resumableEpisodesPath : "",
                CheckpointPath = File.Exists(job.CheckpointPath)
                    ? job.CheckpointPath
                    : "",
                Resumable = resumable,
                CheckpointBytes = resumable
                    ? new[] { job.CheckpointPath, resumableEpisodesPath }
                        .Where(File.Exists)
                        .Sum(path => new FileInfo(path).Length)
                    : 0L,
                CheckpointWriteFailures = checkpointWriteFailures,
                CheckpointWarning = checkpointWarning,
                TrainingMetricsPath = job.TrainingMetricsPath,
                TrainingAnalysisPath = job.TrainingAnalysisPath,
                TrainingMetricWriteFailures =
                    trainingMetricWriteFailures,
                TrainingMetricWarning = trainingMetricWarning
            });
    }
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    if (job != null && !string.IsNullOrWhiteSpace(job.ResultPath))
    {
        var resumable = TryGetResumableCheckpoint(
            job,
            out var resumableEpisodesPath);
        WriteAtomicJson(
            job.ResultPath,
            new CombatFoundationWorkerResult
            {
                JobId = job.JobId,
                WorkerCompleted = true,
                CompletionKind = resumable
                    ? "failed-resumable"
                    : "failed",
                Message = ex.ToString(),
                Runtime = RuntimeDescription(
                    job.Request.MaximumDegreeOfParallelism),
                RulesetHash = job.ExpectedRulesetHash,
                EpisodesPath = resumable ? resumableEpisodesPath : "",
                CheckpointPath = File.Exists(job.CheckpointPath)
                    ? job.CheckpointPath
                    : "",
                Resumable = resumable,
                CheckpointBytes = resumable
                    ? new[] { job.CheckpointPath, resumableEpisodesPath }
                        .Where(File.Exists)
                        .Sum(path => new FileInfo(path).Length)
                    : 0L,
                CheckpointWriteFailures = checkpointWriteFailures,
                CheckpointWarning = checkpointWarning,
                TrainingMetricsPath = job.TrainingMetricsPath,
                TrainingAnalysisPath = job.TrainingAnalysisPath,
                TrainingMetricWriteFailures =
                    trainingMetricWriteFailures,
                TrainingMetricWarning = trainingMetricWarning
            });
    }
    return 1;
}

static FileStream AcquireTrainingLease(CombatFoundationWorkerJob job)
{
    var archiveRoot = string.IsNullOrWhiteSpace(job.SuccessArchiveDirectory)
        ? job.ResultDirectory
        : job.SuccessArchiveDirectory;
    Directory.CreateDirectory(archiveRoot);
    var leasePath = Path.Combine(archiveRoot, ".foundation-training.lock");
    FileStream stream;
    try
    {
        stream = new FileStream(
            leasePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read);
    }
    catch (IOException ex)
    {
        throw new InvalidOperationException(
            "另一个底模训练进程正在使用同一案例库：" + archiveRoot,
            ex);
    }
    var payload = Encoding.UTF8.GetBytes(
        "jobId="
        + job.JobId
        + Environment.NewLine
        + "pid="
        + Environment.ProcessId
        + Environment.NewLine
        + "startedUtc="
        + DateTime.UtcNow.ToString("O")
        + Environment.NewLine);
    stream.SetLength(0);
    stream.Write(payload, 0, payload.Length);
    stream.Flush(flushToDisk: true);
    return stream;
}

static string Fingerprint(
    CombatFoundationWorkerJob job,
    string rulesetHash)
{
    var request = job.Request;
    var training = request.Training;
    var payload = SerializeCompact(new
    {
        Protocol = "foundation-continuation-v2-stable-budget",
        RulesetHash = rulesetHash,
        request.ContentSetHash,
        request.OwnerModSetHash,
        FeatureSchemaVersion =
            CombatPolicyValueProtocol.FeatureSchemaVersion,
        request.DecisionProfile,
        Profile = HashCompact(request.Profile),
        request.TrainingPolicyVersion,
        CombatPolicyValueProtocol.TrainingSemanticsVersion,
        SemanticCanaryVersion =
            CombatFoundationSemanticProbeResult.CurrentCanaryVersion,
        request.TrainingCampaignsPerIteration,
        request.ArenaCampaignsPerDifficulty,
        request.ArenaConfirmationCampaignsPerDifficulty,
        request.NormalValidationCampaigns,
        request.AdvancedValidationCampaigns,
        request.CapabilityProbeCampaignsPerDifficulty,
        request.RequireCapabilityProbeBaselineGain,
        request.CapabilityProbeMinimumVictoryGain,
        request.CapabilityProbeMinimumDepthGain,
        request.EnableEarlyValidationStop,
        request.ValidationEarlyStopBatchSize,
        request.EnableCurriculum,
        request.EnableStratifiedReplay,
        request.EnablePrioritizedReplay,
        request.EnableHardSeedCurriculum,
        request.EnableCounterfactualHardEncounters,
        request.EnableSuccessCaseArchive,
        request.EnableArenaRecovery,
        request.ArenaInvalidRetryCount,
        request.ArenaInvalidRateLimit,
        request.EnableTuningArena,
        request.TuningNormalCampaigns,
        request.TuningAdvancedCampaigns,
        request.EnableProgressiveTuning,
        request.TuningScreeningNormalCampaigns,
        request.TuningScreeningAdvancedCampaigns,
        request.TuningFinalistCount,
        request.NormalAcceptanceRate,
        request.AdvancedAcceptanceRate,
        request.HardSeedReplayShare,
        HardEncounterWeights = HashCompact(request.HardEncounterWeights),
        request.MinimumAdvancedReplayShare,
        request.MinimumAdvancedDefeatReplayShare,
        request.ExpertReplayEpisodeLimit,
        request.AuthoritativeContentReplayShare,
        request.SelfPlayExplorationProbability,
        request.SelfPlayExplorationTemperature,
        CampaignId = request.TrainingCampaign?.CampaignId ?? "",
        CampaignVersion =
            request.TrainingCampaign?.CampaignVersion ?? "",
        TrainingCampaign = HashCompact(request.TrainingCampaign),
        ValidationCampaign = HashCompact(request.ValidationCampaign),
        training.StateDimensions,
        training.ActionDimensions,
        training.HiddenDimensions,
        training.GradientShardCount,
        training.FeatureEncodingMode,
        training.LearningRate,
        training.L2,
        training.Epochs,
        training.MinimumEpochs,
        training.EarlyStoppingPatience,
        training.EarlyStoppingMinimumDelta,
        training.BatchSize,
        training.EnableFrameStratification,
        training.EnableEndTurnSpecialization,
        training.EndTurnFrameWeight,
        training.PolicyTargetTemperature,
        training.MaximumPolicyTargetProbability,
        training.MaximumFrameStratumWeight,
        training.MaximumFramesPerEpisode,
        training.ReplayEpisodeLimit,
        training.RetainedModelCandidates
    });
    return Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}

static string HashCompact<T>(T value)
{
    var payload = value == null ? "null" : SerializeCompact(value);
    return Convert.ToHexString(
        SHA256.HashData(
            Encoding.UTF8.GetBytes(payload)));
}

static bool TryLoadCheckpoint(
    CombatFoundationWorkerJob job,
    string requestFingerprint,
    string rulesetHash,
    out CombatCampaignFoundationResumeState resume,
    out CombatFoundationEpisodeSnapshot? episodeSnapshot,
    out string diagnostic)
{
    resume = new CombatCampaignFoundationResumeState();
    episodeSnapshot = null;
    var errors = new List<string>();
    foreach (var checkpointPath in new[]
             {
                 job.CheckpointPath,
                 CombatFoundationCheckpointStorage.BackupPath(
                     job.CheckpointPath)
             }.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(checkpointPath)
            || !File.Exists(checkpointPath))
        {
            continue;
        }
        try
        {
            var checkpoint = Deserialize<CombatFoundationWorkerCheckpoint>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    checkpointPath));
            if (checkpoint == null
                || checkpoint.SchemaVersion
                   != CombatFoundationWorkerProtocol.SchemaVersion)
            {
                throw new InvalidDataException(
                    "checkpoint protocol is incompatible");
            }
            if (!CheckpointIdentityCompatible(
                    job,
                    checkpoint,
                    requestFingerprint,
                    rulesetHash))
            {
                throw new InvalidDataException(
                    "checkpoint identity does not match this job");
            }
            var snapshot = checkpoint.EpisodeSnapshot
                           ?? new CombatFoundationEpisodeSnapshot
                           {
                               StorageVersion = 1,
                               Path = checkpoint.EpisodesPath,
                               EpisodeCount = -1,
                               CreatedUtc = checkpoint.UpdatedUtc
                           };
            var episodes = CombatFoundationCheckpointStorage
                .ReadAndValidateJsonLines(
                    snapshot,
                    line => Deserialize<CombatEpisode>(line));
            if (!string.IsNullOrWhiteSpace(snapshot.ReplayIdentity)
                && !string.Equals(
                    snapshot.ReplayIdentity,
                    ReplayIdentity(episodes),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "checkpoint replay identity mismatch");
            }
            checkpoint.Resume.Replay = episodes;
            if (checkpoint.Resume.SchemaVersion
                != CombatFoundationWorkerProtocol.SchemaVersion)
            {
                throw new InvalidDataException(
                    "checkpoint resume payload is incompatible");
            }
            resume = checkpoint.Resume;
            episodeSnapshot = snapshot;
            diagnostic = checkpointPath.Equals(
                job.CheckpointPath,
                StringComparison.OrdinalIgnoreCase)
                ? ""
                : "已从检查点备份恢复";
            return true;
        }
        catch (Exception ex)
        {
            errors.Add(
                Path.GetFileName(checkpointPath)
                + ": "
                + ex.Message);
        }
    }
    diagnostic = errors.Count == 0
        ? "未找到检查点"
        : string.Join(" | ", errors);
    return false;
}

static bool CheckpointIdentityCompatible(
    CombatFoundationWorkerJob job,
    CombatFoundationWorkerCheckpoint checkpoint,
    string requestFingerprint,
    string rulesetHash)
{
    if (!string.Equals(
            checkpoint.RulesetHash,
            rulesetHash,
            StringComparison.Ordinal))
    {
        return false;
    }
    if (string.Equals(
            checkpoint.RequestFingerprint,
            requestFingerprint,
            StringComparison.Ordinal))
    {
        return !string.Equals(
                   checkpoint.Resume?.Stage,
                   "model-training",
                   StringComparison.Ordinal)
               || string.Equals(
                   checkpoint.Resume?.Compatibility
                       ?.NativeProgramPackageHash,
                   job.Request.NativeProgramPackageHash,
                   StringComparison.Ordinal);
    }
    return false;
}

static CombatCampaignFoundationResumeState WithoutReplay(
    CombatCampaignFoundationResumeState source)
{
    return new CombatCampaignFoundationResumeState
    {
        SchemaVersion = source.SchemaVersion,
        Stage = source.Stage,
        NextIteration = source.NextIteration,
        CompletedCampaigns = source.CompletedCampaigns,
        GeneratedReplayEpisodes = source.GeneratedReplayEpisodes,
        RunSeed = source.RunSeed,
        TrainingSeedStart = source.TrainingSeedStart,
        ArenaSeedStart = source.ArenaSeedStart,
        TuningSeedStart = source.TuningSeedStart,
        ValidationSeedStart = source.ValidationSeedStart,
        ModelRandomSeed = source.ModelRandomSeed,
        Champion = source.Champion,
        WorkingChampion = source.WorkingChampion,
        Iterations = new List<CombatCampaignFoundationIteration>(
            source.Iterations),
        ModelTraining = source.ModelTraining,
        Telemetry = source.Telemetry,
        HardSeedHistory =
            new List<CombatFoundationHardSeedHistoryEntry>(
                source.HardSeedHistory),
        TrainingSchedule = new List<CombatFoundationTrainingSlot>(
            source.TrainingSchedule),
        ArenaReplacementCursor = source.ArenaReplacementCursor,
        Compatibility = source.Compatibility
    };
}

static void WriteEpisodes(
    string path,
    IReadOnlyList<CombatEpisode> episodes)
{
    CombatFoundationCheckpointStorage.WriteAtomicJsonLines(
        path,
        episodes.Select(SerializeCompact));
}

static string SuccessArchiveRoot(CombatFoundationWorkerJob job)
{
    return string.IsNullOrWhiteSpace(job.SuccessArchiveDirectory)
        ? Path.Combine(job.ResultDirectory, "foundation-success-cases")
        : Path.GetFullPath(job.SuccessArchiveDirectory);
}

static void PrepareCaseArchive(
    CombatFoundationWorkerJob job,
    string rulesetHash)
{
    var diagnostics = new CombatFoundationCaseArchiveLoadDiagnostics
    {
        ProtocolVersion = CombatFoundationCaseArchiveProtocol.Version,
        OwnerRuntime = ".NET 8 worker",
        StorageVersion = CombatFoundationCaseArchiveProtocol.StorageVersion
    };
    job.Request.CaseArchiveLoad = diagnostics;
    job.Request.ExpertReplayEpisodes = new List<CombatEpisode>();
    job.Request.ExpertReplaySelection =
        new CombatFoundationExpertReplaySelection();
    job.Request.RewardResidualTraining =
        new CombatFoundationRewardResidualTrainingResult();
    if (!job.Request.EnableSuccessCaseArchive)
    {
        diagnostics.Message = "archive disabled";
        return;
    }
    try
    {
        var archiveRoot = SuccessArchiveRoot(job);
        diagnostics.ArchiveExists = Directory.Exists(archiveRoot);
        diagnostics.CompatibilityKey =
            CombatFoundationCaseLearning.CompatibilityKey(
                job.Request.TrainingCampaign.CampaignId,
                job.Request.TrainingCampaign.CampaignVersion,
                CombatCampaignFoundationTrainer.CampaignFingerprint(
                    job.Request.TrainingCampaign),
                rulesetHash,
                job.Request.NativeProgramPackageHash,
                job.Request.TrainingPolicyVersion);
        job.Request.CaseArchiveCompatibilityKey =
            diagnostics.CompatibilityKey;
        if (!diagnostics.ArchiveExists)
        {
            diagnostics.Message = "archive root is absent";
            return;
        }

        var compatibilityDirectory =
            CombatFoundationCaseArchiveProtocol.CompatibilityDirectory(
                archiveRoot,
                diagnostics.CompatibilityKey);
        diagnostics.CompatibilityDirectoryExists =
            Directory.Exists(compatibilityDirectory);
        var expertDirectory = Path.Combine(
            compatibilityDirectory,
            CombatFoundationCaseArchiveProtocol.ExpertDirectoryName);
        var observationDirectory = Path.Combine(
            compatibilityDirectory,
            CombatFoundationCaseArchiveProtocol.ObservationDirectoryName);
        diagnostics.ExpertCasesDirectoryExists =
            Directory.Exists(expertDirectory);
        diagnostics.ObservationsDirectoryExists =
            Directory.Exists(observationDirectory);

        var casePaths = EnumerateArchiveFiles(
            expertDirectory,
            2048);
        diagnostics.ExpertCaseFiles = casePaths.Count;
        var cases = new Dictionary<
            string,
            CombatFoundationSuccessCase>(StringComparer.Ordinal);
        LoadSuccessCasePaths(
            casePaths,
            diagnostics.CompatibilityKey,
            cases,
            diagnostics);
        diagnostics.DistinctLoadedCases = cases.Count;

        var observationPaths = EnumerateArchiveFiles(
            observationDirectory,
            8192);
        diagnostics.ObservationFiles = observationPaths.Count;
        var observations = new Dictionary<
            string,
            CombatFoundationCampaignObservation>(StringComparer.Ordinal);
        LoadObservationPaths(
            observationPaths,
            diagnostics.CompatibilityKey,
            observations,
            diagnostics);
        diagnostics.DistinctLoadedObservations = observations.Count;

        var selection = CombatFoundationCaseLearning.SelectExpertReplay(
            cases.Values,
            job.Request.TrainingCampaign.CampaignId,
            job.Request.TrainingCampaign.CampaignVersion,
            CombatCampaignFoundationTrainer.CampaignFingerprint(
                job.Request.TrainingCampaign),
            rulesetHash,
            job.Request.NativeProgramPackageHash,
            job.Request.TrainingPolicyVersion,
            Math.Max(0, job.Request.ExpertReplayEpisodeLimit),
            targetAdvancedShare: Math.Max(
                job.Request.HardSeedReplayShare,
                job.Request.MinimumAdvancedReplayShare),
            maximumEpisodesPerRun: 16);
        job.Request.ExpertReplayEpisodes =
            new List<CombatEpisode>(selection.Episodes);
        selection.Episodes.Clear();
        job.Request.ExpertReplaySelection = selection;
        var residuals =
            CombatFoundationCaseLearning.TrainRewardResiduals(
                observations.Values);
        var requestedAdvancedEpisodes = (int)Math.Round(
            Math.Max(0, job.Request.ExpertReplayEpisodeLimit)
            * selection.TargetAdvancedShare,
            MidpointRounding.AwayFromZero);
        var advancedReplayShortfall = Math.Max(
            0,
            requestedAdvancedEpisodes - selection.SelectedAdvancedEpisodes);
        if (advancedReplayShortfall > 0)
        {
            residuals.Residuals.Clear();
            residuals.CardResiduals = 0;
            residuals.RelicResiduals = 0;
            residuals.BlessingResiduals = 0;
            residuals.Suppressed = true;
            residuals.SuppressionReason =
                "Reward residuals were suppressed because the expert "
                + "replay window is missing "
                + advancedReplayShortfall
                + " advanced episodes.";
        }
        job.Request.RewardResidualTraining = residuals;
        if (!residuals.Suppressed)
        {
            ApplyRewardResiduals(job.Request.TrainingCampaign, residuals);
            ApplyRewardResiduals(job.Request.ValidationCampaign, residuals);
        }
        var rejected = diagnostics.RejectedCaseFiles
                       + diagnostics.RejectedObservationFiles;
        var loaded = diagnostics.LoadedCases
                     + diagnostics.LoadedObservations;
        diagnostics.Message = loaded > 0
            ? rejected == 0
                ? "compatible archive loaded by worker"
                : "compatible archive loaded with rejections"
            : diagnostics.ExpertCaseFiles + diagnostics.ObservationFiles > 0
                ? "archive files found but none were compatible"
                : diagnostics.CompatibilityDirectoryExists
                    ? "compatible archive is empty"
                    : "compatibility directory is absent";
        Console.WriteLine(
            "Foundation archive prepared: protocol="
            + diagnostics.ProtocolVersion
            + ", cases="
            + diagnostics.LoadedCases
            + "/"
            + diagnostics.ExpertCaseFiles
            + ", observations="
            + diagnostics.LoadedObservations
            + "/"
            + diagnostics.ObservationFiles
            + ", rejected="
            + rejected
            + ", maxPath="
            + diagnostics.MaximumObservedPathLength);
    }
    catch (Exception ex)
    {
        diagnostics.Message =
            "worker archive load failed: "
            + ex.GetType().Name
            + ": "
            + ex.Message;
        RegisterArchiveRejection(diagnostics, "archive-load", ex, "");
        Console.Error.WriteLine(diagnostics.Message);
    }
}

static List<string> EnumerateArchiveFiles(string directory, int limit)
{
    if (!Directory.Exists(directory))
    {
        return new List<string>();
    }
    return Directory.EnumerateFiles(
            directory,
            "*",
            SearchOption.TopDirectoryOnly)
        .Where(CombatFoundationCaseArchiveProtocol.IsArchiveJsonFile)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .Take(Math.Max(0, limit))
        .ToList();
}

static void LoadSuccessCasePaths(
    IEnumerable<string> paths,
    string compatibilityKey,
    IDictionary<string, CombatFoundationSuccessCase> destination,
    CombatFoundationCaseArchiveLoadDiagnostics diagnostics)
{
    foreach (var path in paths)
    {
        diagnostics.MaximumObservedPathLength = Math.Max(
            diagnostics.MaximumObservedPathLength,
            path.Length);
        try
        {
            var json = ReadArchiveText(path);
            var reference =
                Deserialize<CombatFoundationExpertCaseReference>(json);
            if (reference != null
                && !string.IsNullOrWhiteSpace(
                    reference.CanonicalFileName))
            {
                if (reference.StorageVersion
                        != CombatFoundationCaseArchiveProtocol.StorageVersion
                    || !string.Equals(
                        reference.ProtocolVersion,
                        CombatFoundationCaseArchiveProtocol.Version,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        reference.CompatibilityKey,
                        compatibilityKey,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        Path.GetFileName(reference.CanonicalFileName),
                        reference.CanonicalFileName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Invalid expert-case reference.");
                }
                var compatibilityDirectory = Directory.GetParent(
                    Path.GetDirectoryName(path)!)?.FullName
                    ?? throw new InvalidDataException(
                        "Expert-case reference has no compatibility directory.");
                var canonicalPath = Path.Combine(
                    compatibilityDirectory,
                    CombatFoundationCaseArchiveProtocol.CaseDirectoryName,
                    reference.CanonicalFileName);
                json = ReadArchiveText(canonicalPath);
            }
            var successCase = Deserialize<CombatFoundationSuccessCase>(
                json);
            if (successCase?.Observation == null
                || successCase.SchemaVersion
                != CombatFoundationCaseLearning.ArchiveSchemaVersion
                || successCase.Observation.SchemaVersion
                != CombatFoundationCaseLearning.ArchiveSchemaVersion
                || string.IsNullOrWhiteSpace(
                    successCase.Observation.CaseId)
                || !string.Equals(
                    successCase.Observation.CompatibilityKey,
                    compatibilityKey,
                    StringComparison.Ordinal))
            {
                diagnostics.RejectedCaseFiles++;
                RegisterArchiveRejection(
                    diagnostics,
                    "incompatible-case",
                    null,
                    path);
                continue;
            }
            destination[successCase.Observation.CaseId] = successCase;
            diagnostics.LoadedCases++;
        }
        catch (Exception ex)
        {
            diagnostics.RejectedCaseFiles++;
            RegisterArchiveRejection(
                diagnostics,
                "case-read",
                ex,
                path);
        }
    }
}

static void LoadObservationPaths(
    IEnumerable<string> paths,
    string compatibilityKey,
    IDictionary<string, CombatFoundationCampaignObservation> destination,
    CombatFoundationCaseArchiveLoadDiagnostics diagnostics)
{
    foreach (var path in paths)
    {
        diagnostics.MaximumObservedPathLength = Math.Max(
            diagnostics.MaximumObservedPathLength,
            path.Length);
        try
        {
            var observation =
                Deserialize<CombatFoundationCampaignObservation>(
                    ReadArchiveText(path));
            if (observation == null
                || observation.SchemaVersion
                != CombatFoundationCaseLearning.ArchiveSchemaVersion
                || string.IsNullOrWhiteSpace(observation.CaseId)
                || !string.Equals(
                    observation.CompatibilityKey,
                    compatibilityKey,
                    StringComparison.Ordinal))
            {
                diagnostics.RejectedObservationFiles++;
                RegisterArchiveRejection(
                    diagnostics,
                    "incompatible-observation",
                    null,
                    path);
                continue;
            }
            destination[observation.CaseId] = observation;
            diagnostics.LoadedObservations++;
        }
        catch (Exception ex)
        {
            diagnostics.RejectedObservationFiles++;
            RegisterArchiveRejection(
                diagnostics,
                "observation-read",
                ex,
                path);
        }
    }
}

static void ApplyRewardResiduals(
    CombatCampaignDefinition campaign,
    CombatFoundationRewardResidualTrainingResult residuals)
{
    campaign.RewardScoreResiduals =
        new Dictionary<string, double>(
            residuals.Residuals,
            StringComparer.OrdinalIgnoreCase);
    campaign.RewardScoreResidualMaximumAbsolute =
        residuals.MaximumAbsoluteResidual;
}

static void RegisterArchiveRejection(
    CombatFoundationCaseArchiveLoadDiagnostics diagnostics,
    string reason,
    Exception? exception,
    string path)
{
    var key = reason;
    if (exception is PathTooLongException
        || exception is DirectoryNotFoundException
        || exception is FileNotFoundException
        || exception is IOException)
    {
        key = "path-access";
        diagnostics.PathAccessFailures++;
    }
    diagnostics.RejectionReasons[key] =
        diagnostics.RejectionReasons.TryGetValue(key, out var count)
            ? count + 1
            : 1;
    diagnostics.MaximumObservedPathLength = Math.Max(
        diagnostics.MaximumObservedPathLength,
        (path ?? "").Length);
}

static string ResolveExpertReferencePath(
    CombatFoundationWorkerJob job,
    CombatFoundationSuccessCase successCase)
{
    var observation = successCase.Observation;
    var path = CombatFoundationCaseArchiveProtocol.EntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        CombatFoundationCaseArchiveProtocol.ExpertDirectoryName,
        observation.CaseId);
    if (!File.Exists(path)
        || ExistingExpertReferenceMatches(path, observation.CaseId))
    {
        return path;
    }
    return CombatFoundationCaseArchiveProtocol.EntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        CombatFoundationCaseArchiveProtocol.ExpertDirectoryName,
        observation.CaseId,
        40);
}

static void WriteExpertReference(
    string path,
    CombatFoundationSuccessCase successCase,
    string canonicalPath)
{
    WriteAtomic(path, Serialize(new CombatFoundationExpertCaseReference
    {
        CompatibilityKey = successCase.Observation.CompatibilityKey,
        CaseId = successCase.Observation.CaseId,
        CanonicalFileName = Path.GetFileName(canonicalPath)
    }));
}

static string ResolveObservationPath(
    CombatFoundationWorkerJob job,
    CombatFoundationCampaignObservation observation)
{
    var legacyPath = CombatFoundationCaseArchiveProtocol.EntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        CombatFoundationCaseArchiveProtocol.ObservationDirectoryName,
        observation.CaseId);
    if (File.Exists(legacyPath)
        && ExistingObservationMatches(legacyPath, observation.CaseId))
    {
        return legacyPath;
    }
    var path = CombatFoundationCaseArchiveProtocol.CompressedEntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        CombatFoundationCaseArchiveProtocol.ObservationDirectoryName,
        observation.CaseId);
    if (!File.Exists(path)
        || ExistingObservationMatches(path, observation.CaseId))
    {
        return path;
    }
    return CombatFoundationCaseArchiveProtocol.CompressedEntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        CombatFoundationCaseArchiveProtocol.ObservationDirectoryName,
        observation.CaseId,
        40);
}

static string ResolveSuccessCasePath(
    CombatFoundationWorkerJob job,
    CombatFoundationSuccessCase successCase,
    string directoryName)
{
    var observation = successCase.Observation;
    var legacyPath = CombatFoundationCaseArchiveProtocol.EntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        directoryName,
        observation.CaseId);
    if (File.Exists(legacyPath)
        && ExistingSuccessCaseMatches(legacyPath, observation.CaseId))
    {
        return legacyPath;
    }
    var path = CombatFoundationCaseArchiveProtocol.CompressedEntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        directoryName,
        observation.CaseId);
    if (!File.Exists(path)
        || ExistingSuccessCaseMatches(path, observation.CaseId))
    {
        return path;
    }
    return CombatFoundationCaseArchiveProtocol.CompressedEntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        directoryName,
        observation.CaseId,
        40);
}

static bool ExistingObservationMatches(string path, string caseId)
{
    try
    {
        return string.Equals(
            Deserialize<CombatFoundationCampaignObservation>(
                ReadArchiveText(path))?.CaseId,
            caseId,
            StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

static bool ExistingSuccessCaseMatches(string path, string caseId)
{
    try
    {
        return string.Equals(
            Deserialize<CombatFoundationSuccessCase>(
                ReadArchiveText(path))?.Observation?.CaseId,
            caseId,
            StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

static bool ExistingExpertReferenceMatches(string path, string caseId)
{
    try
    {
        return string.Equals(
            Deserialize<CombatFoundationExpertCaseReference>(
                File.ReadAllText(path))?.CaseId,
            caseId,
            StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

static void PersistSuccessCases(
    CombatFoundationWorkerJob job,
    CombatCampaignFoundationTrainingResult training,
    ISet<string> incrementallyArchivedCases,
    ISet<string> capacityRejectedCaseIds,
    IReadOnlyList<string> incrementalArchiveErrors,
    int capacityRejectedObservations,
    int capacityRejectedCases)
{
    var archiveRoot = SuccessArchiveRoot(job);
    Directory.CreateDirectory(archiveRoot);
    var currentObservations = training.CampaignObservations
        .Where(item => item != null)
        .GroupBy(item => item.CaseId, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(item => item.CaseId, StringComparer.Ordinal)
        .ToList();
    foreach (var observation in currentObservations)
    {
        var observationPath = ResolveObservationPath(job, observation);
        if (File.Exists(observationPath))
        {
            continue;
        }
        if (ArchiveWriteBudget.TryReserve(
                Path.GetDirectoryName(observationPath)!,
                CombatFoundationCaseArchiveProtocol
                    .MaximumObservationsPerCompatibility))
        {
            WriteAtomicCompressed(
                observationPath,
                SerializeCompact(observation));
            continue;
        }
        capacityRejectedObservations++;
    }
    var cumulativeObservations =
        new List<CombatFoundationCampaignObservation>();
    foreach (var compatibilityKey in currentObservations
                 .Select(item => item.CompatibilityKey)
                 .Distinct(StringComparer.Ordinal))
    {
        var observationDirectory = Path.Combine(
            CombatFoundationCaseArchiveProtocol.CompatibilityDirectory(
                archiveRoot,
                compatibilityKey),
            CombatFoundationCaseArchiveProtocol.ObservationDirectoryName);
        if (!Directory.Exists(observationDirectory))
        {
            continue;
        }
        foreach (var path in Directory.EnumerateFiles(
                     observationDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly)
                 .Where(CombatFoundationCaseArchiveProtocol.IsArchiveJsonFile)
                 .OrderBy(item => item, StringComparer.Ordinal)
                 .Take(CombatFoundationCaseArchiveProtocol
                     .MaximumObservationsPerCompatibility))
        {
            var observation =
                Deserialize<CombatFoundationCampaignObservation>(
                    ReadArchiveText(path));
            if (observation != null)
            {
                cumulativeObservations.Add(observation);
            }
        }
    }
    training.CaseAnalysis = CombatFoundationCaseLearning.Analyze(
        cumulativeObservations.Count == 0
            ? currentObservations
            : cumulativeObservations);
    WriteJsonLines(
        Path.Combine(
            job.ResultDirectory,
            "foundation-case-observations-v1.jsonl"),
        currentObservations);
    var observations = new List<CombatFoundationCampaignObservation>();
    var archived = 0;
    var duplicates = 0;
    foreach (var successCase in training.SuccessCases
                 .Where(item => item?.Observation?.ArchiveEligible == true
                                && item.Episodes.Count > 0
                                && !capacityRejectedCaseIds.Contains(
                                    item.Observation.CaseId))
                 .GroupBy(
                     item => item.Observation.CaseId,
                     StringComparer.Ordinal)
                 .Select(group => group.First())
                 .OrderBy(
                     item => item.Observation.CompatibilityKey,
                     StringComparer.Ordinal)
                 .ThenBy(
                     item => item.Observation.CaseId,
                     StringComparer.Ordinal))
    {
        var observation = successCase.Observation;
        var expertCasePath = ResolveExpertReferencePath(
            job,
            successCase);
        if (!File.Exists(expertCasePath)
            && !ArchiveWriteBudget.TryReserve(
                Path.GetDirectoryName(expertCasePath)!,
                CombatFoundationCaseArchiveProtocol
                    .MaximumExpertCasesPerCompatibility))
        {
            capacityRejectedCaseIds.Add(observation.CaseId);
            capacityRejectedCases++;
            continue;
        }
        var casePath = ResolveSuccessCasePath(
            job,
            successCase,
            CombatFoundationCaseArchiveProtocol.CaseDirectoryName);
        if (File.Exists(casePath))
        {
            if (incrementallyArchivedCases.Contains(observation.CaseId))
            {
                archived++;
            }
            else
            {
                duplicates++;
            }
        }
        else
        {
            WriteAtomicCompressed(casePath, SerializeCompact(successCase));
            archived++;
        }
        if (successCase.Episodes.Count > 0)
        {
            if (!File.Exists(expertCasePath))
            {
                WriteExpertReference(
                    expertCasePath,
                    successCase,
                    casePath);
            }
            training.ExpertReferenceBytes +=
                new FileInfo(expertCasePath).Length;
            training.DeduplicatedExpertBytes += Math.Max(
                0L,
                new FileInfo(casePath).Length
                - new FileInfo(expertCasePath).Length);
        }
        observations.Add(observation);
    }
    var indexPath = Path.Combine(
        job.ResultDirectory,
        "foundation-success-case-index-v1.jsonl");
    WriteJsonLines(indexPath, observations);
    WriteAtomic(
        Path.Combine(
            job.ResultDirectory,
            "foundation-success-analysis-v1.json"),
        Serialize(training.CaseAnalysis));
    training.ArchivedSuccessCases = archived;
    training.DuplicateSuccessCases = duplicates;
    training.ArchiveCapacityRejectedObservations =
        Math.Max(0, capacityRejectedObservations);
    training.ArchiveCapacityRejectedCases =
        Math.Max(0, capacityRejectedCases);
    training.SuccessArchiveDirectory = archiveRoot;
    training.SuccessCaseIndexPath = indexPath;
    if (incrementalArchiveErrors.Count > 0)
    {
        training.SuccessArchiveError = string.Join(
            Environment.NewLine,
            incrementalArchiveErrors.Take(4));
    }
}

static void WriteJsonLines<T>(
    string path,
    IEnumerable<T> values)
{
    CombatFoundationCheckpointStorage.WriteAtomicJsonLines(
        path,
        values.Select(value => SerializeCompact(value!)));
}

static string ResolveTransformerTeacherScript()
{
    var packaged = Path.Combine(
        AppContext.BaseDirectory,
        "TransformerTeacher",
        "train_teacher.py");
    if (File.Exists(packaged))
    {
        return packaged;
    }
    return Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "tools",
        "transformer-teacher",
        "train_teacher.py"));
}

static string ResolveArgument(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.Ordinal))
        {
            return Path.GetFullPath(arguments[index + 1]);
        }
    }
    return "";
}

static T? Deserialize<T>(string json)
{
    return JsonConvert.DeserializeObject<T>(json);
}

static void PersistBuildLimitedSeeds(
    CombatFoundationWorkerJob job,
    CombatCampaignFoundationTrainingResult training)
{
    var routed = (training.HardSeedHistory
                  ?? new List<CombatFoundationHardSeedHistoryEntry>())
        .Where(item => item != null
                       && !item.Resolved
                       && (string.Equals(
                               item.SolvabilityClass,
                               "build-limited",
                               StringComparison.OrdinalIgnoreCase)
                           || string.Equals(
                               item.SolvabilityClass,
                               "build-limited-provisional",
                               StringComparison.OrdinalIgnoreCase)))
        .OrderBy(item => item.DifficultyId, StringComparer.Ordinal)
        .ThenBy(item => item.WorldSeed)
        .ToList();
    var path = Path.Combine(
        job.ResultDirectory,
        "foundation-build-limited-seeds-v1.jsonl");
    WriteJsonLines(path, routed);
    training.BuildLimitedSeedIndexPath = path;
    training.BuildLimitedSeedCases = routed.Count(item => string.Equals(
        item.SolvabilityClass,
        "build-limited",
        StringComparison.OrdinalIgnoreCase));
    training.ProvisionalBuildLimitedSeedCases = routed.Count(item =>
        string.Equals(
            item.SolvabilityClass,
            "build-limited-provisional",
            StringComparison.OrdinalIgnoreCase));
}

static string ReadArchiveText(string path)
{
    if (!path.EndsWith(
            CombatFoundationCaseArchiveProtocol.CompressedJsonExtension,
            StringComparison.OrdinalIgnoreCase))
    {
        return File.ReadAllText(path);
    }
    using var input = File.OpenRead(path);
    using var gzip = new GZipStream(
        input,
        CompressionMode.Decompress,
        leaveOpen: false);
    using var reader = new StreamReader(
        gzip,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true);
    return reader.ReadToEnd();
}

static string Serialize(object value)
{
    return JsonConvert.SerializeObject(
        value,
        Formatting.Indented,
        new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });
}

static string SerializeCompact(object value)
{
    return JsonConvert.SerializeObject(
        value,
        Formatting.None,
        new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            FloatFormatHandling = FloatFormatHandling.DefaultValue
        });
}

static string ReplayIdentity(IReadOnlyList<CombatEpisode> episodes)
{
    var builder = new StringBuilder();
    builder.Append(episodes.Count).Append('\n');
    foreach (var episode in episodes)
    {
        var frames = episode.Frames ?? new List<CombatEpisodeFrame>();
        builder.Append(episode.EpisodeId).Append('|')
            .Append(episode.JourneyRunId).Append('|')
            .Append(episode.JourneyBattleIndex).Append('|')
            .Append(episode.Seed).Append('|')
            .Append(episode.Outcome).Append('|')
            .Append(episode.Turns).Append('|')
            .Append(frames.Count).Append('|')
            .Append(frames.FirstOrDefault()?.StateFingerprint ?? "")
            .Append('|')
            .Append(frames.LastOrDefault()?.StateFingerprint ?? "")
            .Append('\n');
    }
    return Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
}

static bool TryGetResumableCheckpoint(
    CombatFoundationWorkerJob job,
    out string episodesPath)
{
    episodesPath = "";
    foreach (var checkpointPath in new[]
             {
                 job.CheckpointPath,
                 CombatFoundationCheckpointStorage.BackupPath(
                     job.CheckpointPath)
             }.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        try
        {
            if (!File.Exists(checkpointPath))
            {
                continue;
            }
            var checkpoint = Deserialize<CombatFoundationWorkerCheckpoint>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    checkpointPath));
            var candidate = checkpoint?.EpisodeSnapshot?.Path
                            ?? checkpoint?.EpisodesPath
                            ?? "";
            if (checkpoint?.SchemaVersion
                    == CombatFoundationWorkerProtocol.SchemaVersion
                && !string.IsNullOrWhiteSpace(candidate)
                && File.Exists(candidate))
            {
                episodesPath = candidate;
                return true;
            }
        }
        catch
        {
            // The backup pointer is checked next.
        }
    }
    return false;
}

static void TryWriteAuxiliary(string path, string contents)
{
    try
    {
        WriteAtomic(path, contents);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            "Auxiliary status write was skipped: "
            + path
            + " | "
            + ex.Message);
    }
}

static void WriteAtomic(string path, string contents)
{
    CombatFoundationCheckpointStorage.WriteAtomicText(
        path,
        contents,
        retainBackup: false);
}

static void WriteAtomicCompressed(string path, string contents)
{
    CombatFoundationCheckpointStorage.WriteAtomicStream(
        path,
        stream =>
        {
            using var gzip = new GZipStream(
                stream,
                CompressionLevel.Fastest,
                leaveOpen: true);
            using var writer = new StreamWriter(
                gzip,
                new UTF8Encoding(false),
                64 * 1024,
                leaveOpen: false);
            writer.Write(contents);
        },
        retainBackup: false);
}

static void WriteAtomicJson(string path, object value)
{
    CombatFoundationCheckpointStorage.WriteAtomicStream(
        path,
        stream =>
        {
            using var textWriter = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                64 * 1024,
                leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(textWriter)
            {
                CloseOutput = false,
                Formatting = Formatting.Indented
            };
            var serializer = JsonSerializer.Create(
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
            serializer.Serialize(jsonWriter, value);
            jsonWriter.Flush();
            textWriter.Flush();
        },
        retainBackup: false);
}

static CombatFoundationTrainingAnalysis BuildTrainingAnalysis(
    CombatFoundationWorkerJob job,
    CombatCampaignFoundationTrainingResult training)
{
    const double alpha = 0.30d;
    var iterationMetrics = (training.Iterations
                            ?? new List<CombatCampaignFoundationIteration>())
        .SelectMany(iteration =>
            iteration.ModelEpochHistory
            ?? new List<CombatPolicyValueEpochMetrics>())
        .ToList();
    var source = iterationMetrics.Count > 0
        ? iterationMetrics
        : training.ModelEpochHistory
          ?? new List<CombatPolicyValueEpochMetrics>();
    var epochs = source
        .Where(item => item != null
                       && !item.Calibrated
                       && item.Epoch > 0)
        .GroupBy(
            item => (
                Iteration: Math.Max(1, item.Iteration),
                item.Epoch))
        .Select(group => group
            .OrderByDescending(item => item.ElapsedSeconds)
            .First())
        .OrderBy(item => item.Iteration)
        .ThenBy(item => item.Epoch)
        .ToList();
    var analysis = new CombatFoundationTrainingAnalysis
    {
        JobId = job.JobId,
        GeneratedUtc = DateTime.UtcNow,
        SourceMetricsPath = job.TrainingMetricsPath,
        EmaAlpha = alpha,
        EpochCount = epochs.Count,
        IterationCount = epochs
            .Select(item => Math.Max(1, item.Iteration))
            .Distinct()
            .Count()
    };
    foreach (var iteration in epochs.GroupBy(item =>
                 Math.Max(1, item.Iteration)))
    {
        double? trainingEma = null;
        double? validationEma = null;
        foreach (var metric in iteration.OrderBy(item => item.Epoch))
        {
            trainingEma = trainingEma.HasValue
                ? alpha * metric.Training.CompositeLoss
                  + (1d - alpha) * trainingEma.Value
                : metric.Training.CompositeLoss;
            validationEma = validationEma.HasValue
                ? alpha * metric.Validation.CompositeLoss
                  + (1d - alpha) * validationEma.Value
                : metric.Validation.CompositeLoss;
            analysis.Points.Add(
                new CombatFoundationTrainingAnalysisPoint
                {
                    Iteration = iteration.Key,
                    Epoch = metric.Epoch,
                    TrainingLoss = metric.Training.CompositeLoss,
                    ValidationLoss = metric.Validation.CompositeLoss,
                    TrainingLossEma = trainingEma.Value,
                    ValidationLossEma = validationEma.Value,
                    ValidationCiLower =
                        metric.Validation.CompositeLossCiLower,
                    ValidationCiUpper =
                        metric.Validation.CompositeLossCiUpper,
                    GeneralizationGap =
                        metric.Validation.CompositeLoss
                        - metric.Training.CompositeLoss,
                    Improved = metric.Improved,
                    EarlyStopped = metric.EarlyStopped
                });
        }
    }
    var best = epochs
        .OrderBy(item => item.Validation.CompositeLoss)
        .ThenBy(item => item.Iteration)
        .ThenBy(item => item.Epoch)
        .FirstOrDefault();
    if (best != null)
    {
        analysis.BestValidationLoss = best.Validation.CompositeLoss;
        analysis.BestIteration = Math.Max(1, best.Iteration);
        analysis.BestEpoch = best.Epoch;
    }
    return analysis;
}

static string RuntimeDescription(int workers)
{
    return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
           + "; "
           + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
           + "; serverGC="
           + System.Runtime.GCSettings.IsServerGC
           + "; workers="
           + workers;
}

static string AutoTuneHardwareKey()
{
    var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    var availableMemoryGiB = Math.Max(
        1L,
        (availableMemory + (1L << 30) - 1L) >> 30);
    var memoryTierGiB = 1L;
    while (memoryTierGiB < availableMemoryGiB && memoryTierGiB < 1024L)
    {
        memoryTierGiB <<= 1;
    }
    return string.Join(
        "|",
        Environment.MachineName,
        Environment.ProcessorCount,
        Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "",
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        System.Runtime.GCSettings.IsServerGC
            ? "server-gc"
            : "workstation-gc",
        // GC's available-memory estimate can move by a GiB under pressure.
        // A capability tier keeps cache identity stable without sharing plans
        // between materially different memory classes.
        "memory-tier-" + memoryTierGiB.ToString() + "gib",
        Environment.Version);
}

internal static class ArchiveWriteBudget
{
    private static readonly object Gate = new();

    private static readonly Dictionary<string, int> Counts =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool TryReserve(string directory, int maximumEntries)
    {
        if (maximumEntries <= 0)
        {
            return false;
        }
        var fullPath = Path.GetFullPath(directory);
        lock (Gate)
        {
            if (!Counts.TryGetValue(fullPath, out var count))
            {
                count = Directory.Exists(fullPath)
                    ? Directory.EnumerateFiles(
                            fullPath,
                            "*",
                            SearchOption.TopDirectoryOnly)
                        .Count(CombatFoundationCaseArchiveProtocol
                            .IsArchiveJsonFile)
                    : 0;
            }
            if (count >= maximumEntries)
            {
                Counts[fullPath] = count;
                return false;
            }
            Counts[fullPath] = count + 1;
            return true;
        }
    }
}
