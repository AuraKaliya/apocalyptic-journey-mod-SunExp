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
if (string.IsNullOrWhiteSpace(jobPath)
    || !CombatFoundationPathRuntime.FileExists(jobPath))
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
    CombatFoundationPathRuntime.CreateDirectory(job.ResultDirectory);
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
    var checkpointDirectory = Path.GetDirectoryName(
        CombatFoundationPathRuntime.Normalize(job.CheckpointPath))
        ?? job.ResultDirectory;
    if (string.IsNullOrWhiteSpace(job.CheckpointCatalogPath))
    {
        job.CheckpointCatalogPath = Path.Combine(
            checkpointDirectory,
            CombatFoundationCheckpointCatalogProtocol.CatalogFileName);
    }
    if (string.IsNullOrWhiteSpace(job.ModelSelectionAnchorPath))
    {
        job.ModelSelectionAnchorPath = Path.Combine(
            checkpointDirectory,
            CombatFoundationCheckpointCatalogProtocol.SelectionAnchorFileName);
    }
    job.ResumeMode = CombatFoundationCheckpointResumeModes.Normalize(
        job.ResumeMode);
    LoadModelSelectionAnchor(job);
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
    if (!job.ResumeFromCheckpoint && job.ResetCheckpointOnFreshStart)
    {
        ResetActiveCheckpoint(job);
    }
    // Learned archive residuals must be applied before both Worker and Trainer
    // compute identity. CampaignFingerprint normalizes those learned values,
    // so structural compatibility remains stable across training rounds.
    PrepareCaseArchive(job, build.Ruleset.RulesetHash);
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
    if (!resumedFromCheckpoint
        && job.ResumeFromCheckpoint
        && string.IsNullOrWhiteSpace(job.ResumeCheckpointPath))
    {
        var checkpointDiagnostic = resumeDiagnostic;
        resumedFromCheckpoint = TryRecoverPriorWorkingResult(
            job,
            build.Ruleset.RulesetHash,
            out resume,
            out resumeDiagnostic);
        if (resumedFromCheckpoint)
        {
            checkpointSnapshot = null;
            ResetActiveCheckpoint(job);
            resumeDiagnostic = string.IsNullOrWhiteSpace(checkpointDiagnostic)
                ? resumeDiagnostic
                : resumeDiagnostic + " | current checkpoint: "
                  + checkpointDiagnostic;
        }
    }
    if (resumedFromCheckpoint)
    {
        if (string.Equals(
                job.ResumeMode,
                CombatFoundationCheckpointResumeModes.ModelBranch,
                StringComparison.Ordinal))
        {
            resume = CreateModelBranchResume(job, resume);
            resumeDiagnostic = string.IsNullOrWhiteSpace(resumeDiagnostic)
                ? "已从所选检查点创建模型分支；优化器与 epoch 已重置"
                : resumeDiagnostic
                  + " | 已创建模型分支；优化器与 epoch 已重置";
        }
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
        if (job.RequireCompatibleResume)
        {
            throw new InvalidOperationException(
                "Compatible resume was required, but no valid checkpoint or "
                + "historical Working Model could be loaded: "
                + resumeDiagnostic);
        }
        ResetActiveCheckpoint(job);
        Console.Error.WriteLine(
            "Foundation checkpoint was incompatible and has been discarded: "
            + resumeDiagnostic);
    }
    var effectiveStartMode = resumedFromCheckpoint
        ? checkpointSnapshot == null
            ? "historical-working"
            : job.ResumeMode == CombatFoundationCheckpointResumeModes.ModelBranch
                ? "checkpoint-model-branch"
                : "checkpoint-exact"
        : "fresh";
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        job.CheckpointPath,
        job.CheckpointEpisodesPath,
        (ReadCheckpointCatalog(job.CheckpointCatalogPath)?.Entries
             .Select(item => item.EpisodeSnapshotPath)
             .Where(path => !string.IsNullOrWhiteSpace(path))
         ?? Array.Empty<string>())
            .Concat(checkpointSnapshot == null
                ? Array.Empty<string>()
                : new[] { checkpointSnapshot.Path })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
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
    var latestTelemetryUpdatedUtc = DateTime.MinValue;
    long telemetrySequence = 0L;
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
            latestTelemetryUpdatedUtc = DateTime.UtcNow;
            var sequence = Interlocked.Increment(ref telemetrySequence);
            TryWriteAuxiliary(
                job.ProgressPath,
                Serialize(new CombatFoundationWorkerProgress
                {
                    JobId = job.JobId,
                    UpdatedUtc = latestTelemetryUpdatedUtc,
                    TelemetryUpdatedUtc = latestTelemetryUpdatedUtc,
                    TelemetrySequence = sequence,
                    HeartbeatOnly = false,
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
                var heartbeatUtc = DateTime.UtcNow;
                TryWriteAuxiliary(
                    job.ProgressPath,
                    Serialize(new CombatFoundationWorkerProgress
                    {
                        JobId = job.JobId,
                        UpdatedUtc = heartbeatUtc,
                        TelemetryUpdatedUtc = latestTelemetryUpdatedUtc,
                        TelemetrySequence = Volatile.Read(
                            ref telemetrySequence),
                        HeartbeatOnly = true,
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
                    || !CombatFoundationPathRuntime.FileExists(nextSnapshot.Path)
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
                var checkpoint = new CombatFoundationWorkerCheckpoint
                {
                    RequestFingerprint = requestFingerprint,
                    RulesetHash = build.Ruleset.RulesetHash,
                    EpisodesPath = nextSnapshot.Path,
                    EpisodeSnapshot = nextSnapshot,
                    UpdatedUtc = DateTime.UtcNow,
                    Resume = WithoutReplay(state)
                };
                CombatFoundationCheckpointStorage.WriteAtomicText(
                    job.CheckpointPath,
                    Serialize(checkpoint));
                var catalogSnapshots = WriteCheckpointCatalogEntry(
                    job,
                    checkpoint,
                    requestFingerprint,
                    build.Ruleset.RulesetHash);
                checkpointSnapshot = nextSnapshot;
                checkpointReplayIdentity = replayIdentity;
                checkpointWarning = "";
                CombatFoundationCheckpointStorage.CleanupArtifacts(
                    job.CheckpointPath,
                    job.CheckpointEpisodesPath,
                    catalogSnapshots
                        .Append(nextSnapshot.Path)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray());
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
                "transformer-runtime-auto-tune-v2.json"),
            Path.Combine(
                SuccessArchiveRoot(job),
                "transformer-teacher-corpus"));
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
        if (job.Request.EnableSuccessCaseArchive)
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
    var resultEpisodesPath = Path.Combine(
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
    var resumableEpisodesPath = "";
    var resumable = !job.Request.PreflightOnly
                    && !training.AcceptancePassed
                    && roleStrategyGatePassed
                    && TryGetResumableCheckpoint(
                        job,
                        out resumableEpisodesPath);
    var episodesPath = resumable
        ? resumableEpisodesPath
        : resultEpisodesPath;
    if (!resumable)
    {
        WriteEpisodes(episodesPath, training.Replay);
    }
    training.Replay.Clear();
    training.CampaignObservations.Clear();
    training.SuccessCases.Clear();
    if (job.Request.PreflightOnly
        || training.AcceptancePassed
        || !roleStrategyGatePassed)
    {
        ResetActiveCheckpoint(job);
    }
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
                .Where(CombatFoundationPathRuntime.FileExists)
                .Sum(CombatFoundationPathRuntime.FileLength)
            : 0L,
        CompletionKind = completionKind,
        Message = training.Message,
        Runtime = RuntimeDescription(requestedWorkers),
        RulesetHash = build.Ruleset.RulesetHash,
        EpisodesPath = episodesPath,
        CheckpointPath = resumable
            ? job.CheckpointPath
            : "",
        CheckpointCatalogPath = job.CheckpointCatalogPath,
        SelectedCheckpointPath = job.ResumeCheckpointPath,
        ResumeMode = job.ResumeMode,
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
        RequestedStartMode = job.RequestedStartMode,
        EffectiveStartMode = effectiveStartMode,
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
        workerResult.ModelPackageBytes = new FileInfo(
            workerResult.ModelPackagePath).Length;
        if (!CombatFoundationModelPackageProtocol.TryValidateSerializedSize(
                workerResult.ModelPackageBytes,
                out var packageSizeDiagnostic))
        {
            File.Delete(workerResult.ModelPackagePath);
            workerResult.ModelPackagePath = "";
            throw new InvalidOperationException(packageSizeDiagnostic);
        }
        workerResult.ModelPackageSizeWarning = packageSizeDiagnostic;
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
                TrainingMetricWarning = trainingMetricWarning,
                ResumeRequested = job.ResumeFromCheckpoint,
                RequestedStartMode = job.RequestedStartMode,
                EffectiveStartMode = "cancelled"
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
                TrainingMetricWarning = trainingMetricWarning,
                ResumeRequested = job.ResumeFromCheckpoint,
                RequestedStartMode = job.RequestedStartMode,
                EffectiveStartMode = "failed"
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
        request.MinimumArenaDiscordantPairs,
        request.MaximumOfflineHeadRegression,
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
        TrainingCampaign =
            request.TrainingCampaign == null
                ? ""
                : CombatCampaignFoundationTrainer.CampaignFingerprint(
                    request.TrainingCampaign),
        ValidationCampaign =
            request.ValidationCampaign == null
                ? ""
                : CombatCampaignFoundationTrainer.CampaignFingerprint(
                    request.ValidationCampaign),
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
        training.MaximumUnsafeEndTurnFrameShare,
        training.UnsafeEndTurnRiskAuxiliaryShare,
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
    var requestedPath = string.IsNullOrWhiteSpace(job.ResumeCheckpointPath)
        ? job.CheckpointPath
        : job.ResumeCheckpointPath;
    var candidates = string.IsNullOrWhiteSpace(job.ResumeCheckpointPath)
        ? new[]
        {
            requestedPath,
            CombatFoundationCheckpointStorage.BackupPath(requestedPath)
        }
        : new[] { requestedPath };
    foreach (var checkpointPath in candidates
                 .Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(checkpointPath)
            || !CombatFoundationPathRuntime.FileExists(checkpointPath))
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
            if (!CombatCampaignFoundationTrainer.ResumeCompatible(
                    checkpoint.Resume))
            {
                throw new InvalidDataException(
                    "checkpoint model or replay payload is incompatible");
            }
            resume = checkpoint.Resume;
            episodeSnapshot = snapshot;
            diagnostic = !string.IsNullOrWhiteSpace(job.ResumeCheckpointPath)
                ? "已加载所选不可变检查点 " + Path.GetFileName(checkpointPath)
                : checkpointPath.Equals(
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
    if (!string.Equals(
            job.ResumeMode,
            CombatFoundationCheckpointResumeModes.ModelBranch,
            StringComparison.Ordinal))
    {
        return false;
    }
    var current = CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
        job.Request,
        rulesetHash);
    var candidate = checkpoint.Resume?.ModelTraining?.BestModel
                    ?? checkpoint.Resume?.ModelTraining?.Model
                    ?? checkpoint.Resume?.WorkingChampion
                    ?? checkpoint.Resume?.Champion;
    return CombatCampaignFoundationTrainer.ManifestCompatible(
               checkpoint.Resume?.Compatibility,
               current)
           && ModelArchitectureCompatible(candidate, job.Request.Training);
}

static bool ModelArchitectureCompatible(
    CombatPolicyValueNetworkDefinition? model,
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
               StringComparison.Ordinal);
}

static CombatCampaignFoundationResumeState CreateModelBranchResume(
    CombatFoundationWorkerJob job,
    CombatCampaignFoundationResumeState source)
{
    var model = source.ModelTraining?.BestModel
                ?? source.ModelTraining?.Model
                ?? source.WorkingChampion
                ?? source.Champion
                ?? throw new InvalidDataException(
                    "所选检查点不包含可用于模型分支的权重");
    if (!ModelArchitectureCompatible(model, job.Request.Training))
    {
        throw new InvalidDataException(
            "所选检查点的模型结构与当前参数不兼容；参数量不会自动调整");
    }
    source.Stage = "iteration-complete";
    source.ModelTraining = null;
    source.WorkingChampion = model;
    source.Champion = model;
    return source;
}

static bool TryRecoverPriorWorkingResult(
    CombatFoundationWorkerJob job,
    string rulesetHash,
    out CombatCampaignFoundationResumeState resume,
    out string diagnostic)
{
    resume = new CombatCampaignFoundationResumeState();
    diagnostic = "no compatible prior Working Model result was found";
    var parent = Directory.GetParent(
        Path.GetFullPath(job.ResultDirectory))?.FullName;
    if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
    {
        return false;
    }

    var currentDirectory = Path.GetFullPath(job.ResultDirectory)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var resultFileName = Path.GetFileName(job.ResultPath);
    if (string.IsNullOrWhiteSpace(resultFileName))
    {
        resultFileName = "foundation-worker-result.json";
    }
    var currentManifest =
        CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
            job.Request,
            rulesetHash);
    var errors = new List<string>();
    foreach (var directory in Directory.EnumerateDirectories(
                 parent,
                 "foundation-controller-*",
                 SearchOption.TopDirectoryOnly)
             .Select(Path.GetFullPath)
             .Where(path => !string.Equals(
                 path.TrimEnd(
                     Path.DirectorySeparatorChar,
                     Path.AltDirectorySeparatorChar),
                 currentDirectory,
                 StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path)))
    {
        var resultPath = Path.Combine(directory, resultFileName);
        if (!File.Exists(resultPath))
        {
            continue;
        }
        try
        {
            var workerResult = Deserialize<CombatFoundationWorkerResult>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(resultPath));
            var training = workerResult?.Training;
            var working = training?.WorkingChampion ?? training?.Champion;
            if (workerResult?.SchemaVersion
                    != CombatFoundationWorkerProtocol.SchemaVersion
                || training == null
                || working == null
                || !CombatCampaignFoundationTrainer.ManifestCompatible(
                    training.Compatibility,
                    currentManifest))
            {
                continue;
            }
            var episodesPath = workerResult.EpisodesPath;
            if (string.IsNullOrWhiteSpace(episodesPath)
                || !Path.IsPathRooted(episodesPath))
            {
                episodesPath = Path.Combine(directory, episodesPath ?? "");
            }
            if (!File.Exists(episodesPath))
            {
                throw new FileNotFoundException(
                    "prior replay artifact is missing",
                    episodesPath);
            }
            var replayLimit = Math.Max(
                256,
                Math.Min(768, job.Request.Training.ReplayEpisodeLimit));
            var episodes = ReadRecoveryEpisodes(
                episodesPath,
                replayLimit,
                job.Request.MinimumAdvancedReplayShare,
                job.Request.MinimumAdvancedDefeatReplayShare);
            if (episodes.Count == 0)
            {
                throw new InvalidDataException(
                    "prior replay is empty or protocol-incompatible");
            }

            resume = new CombatCampaignFoundationResumeState
            {
                Stage = "iteration-complete",
                NextIteration = training.Iterations.Count,
                CompletedCampaigns = training.CompletedCampaigns,
                GeneratedReplayEpisodes = Math.Max(
                    training.GeneratedReplayEpisodes,
                    episodes.Count),
                RunSeed = training.RunSeed,
                TrainingSeedStart = training.TrainingSeedStart,
                ArenaSeedStart = training.ArenaSeedStart,
                TuningSeedStart = training.TuningSeedStart,
                ValidationSeedStart = training.ValidationSeedStart,
                ModelRandomSeed = training.ModelRandomSeed,
                Champion = training.Champion,
                WorkingChampion = working,
                Replay = episodes,
                Iterations = new List<CombatCampaignFoundationIteration>(
                    training.Iterations),
                HardSeedHistory =
                    new List<CombatFoundationHardSeedHistoryEntry>(
                        training.HardSeedHistory),
                ArenaReplacementCursor = training.ArenaReplacementPairs,
                Compatibility = training.Compatibility
            };
            if (!CombatCampaignFoundationTrainer.ResumeCompatible(resume))
            {
                throw new InvalidDataException(
                    "historical Working Model or replay payload is incompatible");
            }
            diagnostic = "recovered compatible Working Model and bounded "
                         + episodes.Count
                         + " replay episodes from "
                         + Path.GetFileName(directory);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add(Path.GetFileName(directory) + ": " + ex.Message);
        }
    }
    if (errors.Count > 0)
    {
        diagnostic += " | " + string.Join(" | ", errors.Take(4));
    }
    return false;
}

static List<CombatEpisode> ReadRecoveryEpisodes(
    string path,
    int limit,
    double minimumAdvancedShare,
    double minimumAdvancedDefeatShare)
{
    var boundedLimit = Math.Max(1, limit);
    var advancedTarget = Math.Clamp(
        (int)Math.Ceiling(
            boundedLimit * Math.Clamp(minimumAdvancedShare, 0d, 1d)),
        0,
        boundedLimit);
    var advancedDefeatTarget = Math.Clamp(
        (int)Math.Ceiling(
            boundedLimit * Math.Clamp(
                minimumAdvancedDefeatShare,
                0d,
                1d)),
        0,
        advancedTarget);
    var advancedOtherTarget = advancedTarget - advancedDefeatTarget;
    var normalTarget = boundedLimit - advancedTarget;
    var advancedDefeats = new List<CombatEpisode>(advancedDefeatTarget);
    var advancedOther = new List<CombatEpisode>(advancedOtherTarget);
    var normal = new List<CombatEpisode>(normalTarget);
    var advancedDefeatScores = new List<double>(advancedDefeatTarget);
    var advancedOtherScores = new List<double>(advancedOtherTarget);
    var normalScores = new List<double>(normalTarget);
    foreach (var line in File.ReadLines(path))
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }
        var episode = Deserialize<CombatEpisode>(line)
                      ?? throw new InvalidDataException(
                          "prior replay contains a null episode");
        if (episode.ModelProtocol
                != CombatPolicyValueProtocol.EpisodeProtocol
            || episode.FeatureSchemaVersion
                != CombatPolicyValueProtocol.FeatureSchemaVersion)
        {
            throw new InvalidDataException(
                "prior replay contains a protocol-incompatible episode");
        }
        var isAdvanced = string.Equals(
            episode.Campaign?.DifficultyId,
            "advanced",
            StringComparison.OrdinalIgnoreCase);
        var isAdvancedDefeat = isAdvanced
                                && episode.Campaign?.FinalBossVictory != true
                                && !string.Equals(
                                    episode.Campaign?.OutcomeClass,
                                    "victory",
                                    StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(
                                    episode.Campaign?.OutcomeClass,
                                    "encounter-victory",
                                    StringComparison.OrdinalIgnoreCase);
        InsertRecoveryEpisode(
            isAdvancedDefeat
                ? advancedDefeats
                : isAdvanced
                    ? advancedOther
                    : normal,
            isAdvancedDefeat
                ? advancedDefeatScores
                : isAdvanced
                    ? advancedOtherScores
                    : normalScores,
            isAdvancedDefeat
                ? advancedDefeatTarget
                : isAdvanced
                    ? advancedOtherTarget
                    : normalTarget,
            episode);
    }
    return advancedDefeats.Concat(advancedOther).Concat(normal)
        .OrderByDescending(CombatFoundationReplaySampler.RecoveryPriority)
        .ThenBy(episode => episode.EpisodeId, StringComparer.Ordinal)
        .ToList();
}

static void InsertRecoveryEpisode(
    List<CombatEpisode> target,
    List<double> scores,
    int capacity,
    CombatEpisode candidate)
{
    if (capacity <= 0)
    {
        return;
    }
    if (target.Count < capacity)
    {
        target.Add(candidate);
        scores.Add(CombatFoundationReplaySampler.RecoveryPriority(candidate));
        return;
    }
    var candidatePriority =
        CombatFoundationReplaySampler.RecoveryPriority(candidate);
    var lowestIndex = 0;
    var lowestPriority = scores[0];
    for (var index = 1; index < target.Count; index++)
    {
        var priority = scores[index];
        if (priority < lowestPriority
            || Math.Abs(priority - lowestPriority) < 0.0000001d
            && string.CompareOrdinal(
                target[index].EpisodeId,
                target[lowestIndex].EpisodeId) > 0)
        {
            lowestIndex = index;
            lowestPriority = priority;
        }
    }
    if (candidatePriority > lowestPriority
        || Math.Abs(candidatePriority - lowestPriority) < 0.0000001d
        && string.CompareOrdinal(
            candidate.EpisodeId,
            target[lowestIndex].EpisodeId) < 0)
    {
        target[lowestIndex] = candidate;
        scores[lowestIndex] = candidatePriority;
    }
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

static void LoadModelSelectionAnchor(CombatFoundationWorkerJob job)
{
    job.Request.ModelSelectionAnchorEpisodes = new List<CombatEpisode>();
    if (CombatFoundationPathRuntime.FileExists(job.ModelSelectionAnchorPath))
    {
        foreach (var line in File.ReadLines(
                     CombatFoundationPathRuntime.ForFileSystem(
                         job.ModelSelectionAnchorPath)))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var episode = Deserialize<CombatEpisode>(line);
            if (episode != null)
            {
                job.Request.ModelSelectionAnchorEpisodes.Add(episode);
            }
        }
    }
    job.Request.ModelSelectionAnchorCreated = episodes =>
    {
        if (CombatFoundationPathRuntime.FileExists(job.ModelSelectionAnchorPath))
        {
            return;
        }
        WriteEpisodes(job.ModelSelectionAnchorPath, episodes);
    };
}

static IReadOnlyList<string> WriteCheckpointCatalogEntry(
    CombatFoundationWorkerJob job,
    CombatFoundationWorkerCheckpoint checkpoint,
    string requestFingerprint,
    string rulesetHash)
{
    var state = checkpoint.Resume;
    var modelTraining = state.ModelTraining;
    var iteration = state.Iterations?.LastOrDefault();
    var shouldCatalog = string.Equals(
                            state.Stage,
                            "iteration-complete",
                            StringComparison.Ordinal)
                        || string.Equals(
                            state.Stage,
                            "model-training",
                            StringComparison.Ordinal)
                        && (modelTraining?.CompletedEpochs ?? 0) > 0;
    var catalog = ReadCheckpointCatalog(job.CheckpointCatalogPath)
                  ?? new CombatFoundationCheckpointCatalog();
    if (!shouldCatalog)
    {
        return catalog.Entries
            .Select(item => item.EpisodeSnapshotPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }
    var epochMetrics = modelTraining?.EpochHistory?
        .FirstOrDefault(item => item.Epoch == modelTraining.BestEpoch)
        ?? modelTraining?.EpochHistory?.LastOrDefault();
    var trainingMetrics = iteration?.ModelTrainingMetrics
                          ?? epochMetrics?.Training
                          ?? new CombatPolicyValueMetricSnapshot();
    var validationMetrics = iteration?.ModelValidationMetrics
                            ?? epochMetrics?.Validation
                            ?? new CombatPolicyValueMetricSnapshot();
    var testMetrics = iteration?.ModelTestMetrics
                      ?? new CombatPolicyValueMetricSnapshot();
    var anchorMetrics = iteration?.ModelSelectionAnchorMetrics
                        ?? new CombatPolicyValueMetricSnapshot();
    var model = modelTraining?.BestModel
                ?? modelTraining?.Model
                ?? state.WorkingChampion
                ?? state.Champion;
    var identity = HashCompact(new
    {
        requestFingerprint,
        state.Stage,
        state.NextIteration,
        Epoch = modelTraining?.CompletedEpochs ?? iteration?.TuningSelectedEpoch ?? 0,
        ModelId = model?.ModelId ?? "",
        Replay = checkpoint.EpisodeSnapshot?.ReplayIdentity ?? ""
    });
    var id = identity.Substring(0, 20).ToLowerInvariant();
    var immutableDirectory = Path.Combine(
        Path.GetDirectoryName(
            CombatFoundationPathRuntime.Normalize(job.CheckpointCatalogPath))
        ?? job.ResultDirectory,
        CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
    CombatFoundationPathRuntime.CreateDirectory(immutableDirectory);
    var immutablePath = Path.Combine(
        immutableDirectory,
        "foundation-checkpoint-" + id + ".json");
    if (!CombatFoundationPathRuntime.FileExists(immutablePath))
    {
        CombatFoundationCheckpointStorage.WriteAtomicText(
            immutablePath,
            Serialize(checkpoint),
            retainBackup: false);
    }
    var risk = CombatFoundationCheckpointCatalogProtocol.Risk(
        trainingMetrics.CompositeLoss,
        validationMetrics.CompositeLoss,
        iteration?.ModelEpochHistory ?? modelTraining?.EpochHistory,
        out var riskReason);
    var entry = new CombatFoundationCheckpointCatalogEntry
    {
        Id = id,
        SourceJobId = job.JobId,
        RequestFingerprint = requestFingerprint,
        RulesetHash = rulesetHash,
        CreatedUtc = checkpoint.UpdatedUtc,
        Stage = state.Stage,
        NextIteration = state.NextIteration,
        CompletedCampaigns = state.CompletedCampaigns,
        CompletedEpochs = modelTraining?.CompletedEpochs
                          ?? iteration?.TuningSelectedEpoch
                          ?? 0,
        BestEpoch = modelTraining?.BestEpoch
                    ?? iteration?.TuningSelectedEpoch
                    ?? 0,
        ModelId = model?.ModelId ?? "",
        CheckpointPath = immutablePath,
        EpisodeSnapshotPath = checkpoint.EpisodeSnapshot?.Path
                              ?? checkpoint.EpisodesPath,
        ReplayIdentity = checkpoint.EpisodeSnapshot?.ReplayIdentity ?? "",
        EpisodeCount = checkpoint.EpisodeSnapshot?.EpisodeCount ?? 0,
        TrainingLoss = trainingMetrics.CompositeLoss,
        ValidationLoss = validationMetrics.CompositeLoss,
        TestLoss = testMetrics.CompositeLoss,
        GeneralizationGap = validationMetrics.CompositeLoss
                            - trainingMetrics.CompositeLoss,
        SelectionAnchorMetrics = anchorMetrics,
        Risk = risk,
        RiskReason = riskReason,
        EarlyStopped = (iteration?.ModelEpochHistory
                        ?? modelTraining?.EpochHistory
                        ?? new List<CombatPolicyValueEpochMetrics>())
            .Any(item => item.EarlyStopped),
        QualityGatesPassed = iteration != null
                             && iteration.OfflineHeadRegressionGatePassed
                             && iteration.FeatureCollisionGatePassed
                             && iteration.StrategyQuotaGatePassed,
        SupportsExact = true,
        SupportsModelBranch = model != null
    };
    catalog.Protocol = CombatFoundationCheckpointCatalogProtocol.Version;
    catalog.RequestFingerprint = requestFingerprint;
    catalog.RulesetHash = rulesetHash;
    catalog.UpdatedUtc = DateTime.UtcNow;
    catalog.SelectionAnchorPath = job.ModelSelectionAnchorPath;
    catalog.SelectionAnchorEpisodes =
        job.Request.ModelSelectionAnchorEpisodes?.Count ?? 0;
    catalog.SelectionAnchorIdentity = catalog.SelectionAnchorEpisodes == 0
        ? ""
        : ReplayIdentity(
            job.Request.ModelSelectionAnchorEpisodes
            ?? new List<CombatEpisode>());
    catalog.Entries.RemoveAll(item => string.Equals(
        item.Id,
        entry.Id,
        StringComparison.Ordinal));
    catalog.Entries.Add(entry);
    catalog.Entries = catalog.Entries
        .OrderByDescending(item => item.CreatedUtc)
        .Take(CombatFoundationCheckpointCatalogProtocol.MaximumEntries)
        .ToList();
    var recommended = CombatFoundationCheckpointCatalogProtocol.Recommend(
        catalog.Entries);
    catalog.RecommendedCheckpointId = recommended?.Id ?? "";
    foreach (var item in catalog.Entries)
    {
        item.Recommended = string.Equals(
            item.Id,
            catalog.RecommendedCheckpointId,
            StringComparison.Ordinal);
    }
    CombatFoundationCheckpointStorage.WriteAtomicText(
        job.CheckpointCatalogPath,
        Serialize(catalog));
    return catalog.Entries
        .Select(item => item.EpisodeSnapshotPath)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static CombatFoundationCheckpointCatalog? ReadCheckpointCatalog(string path)
{
    if (!CombatFoundationPathRuntime.FileExists(path))
    {
        return null;
    }
    try
    {
        var catalog = Deserialize<CombatFoundationCheckpointCatalog>(
            CombatFoundationCheckpointStorage.ReadAllTextShared(path));
        return string.Equals(
            catalog?.Protocol,
            CombatFoundationCheckpointCatalogProtocol.Version,
            StringComparison.Ordinal)
            ? catalog
            : null;
    }
    catch
    {
        return null;
    }
}

static void ResetActiveCheckpoint(CombatFoundationWorkerJob job)
{
    CombatFoundationPathRuntime.DeleteFile(job.CheckpointPath);
    CombatFoundationPathRuntime.DeleteFile(
        CombatFoundationCheckpointStorage.BackupPath(job.CheckpointPath));
    var retained = ReadCheckpointCatalog(job.CheckpointCatalogPath)?.Entries
        .Select(item => item.EpisodeSnapshotPath)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .ToArray()
        ?? Array.Empty<string>();
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        job.CheckpointPath,
        job.CheckpointEpisodesPath,
        retained,
        retainNewestSnapshots: 0);
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
            residuals.ConditionalCurriculumOnly =
                residuals.ConditionalResiduals.Count > 0;
            residuals.ConditionalCurriculumReason =
                residuals.ConditionalCurriculumOnly
                    ? "advanced expert quota is incomplete; conditional "
                      + "residuals are restricted to self-play curriculum"
                    : "";
            residuals.SuppressionReason =
                "Global reward residuals were suppressed because the expert "
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
        else if (residuals.ConditionalCurriculumOnly)
        {
            ApplyConditionalCurriculumResiduals(
                job.Request.TrainingCampaign,
                residuals);
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
    campaign.RewardScoreConditionalResiduals =
        new Dictionary<string, double>(
            residuals.ConditionalResiduals,
            StringComparer.OrdinalIgnoreCase);
    campaign.RewardScoreResidualMaximumAbsolute =
        residuals.MaximumAbsoluteResidual;
}

static void ApplyConditionalCurriculumResiduals(
    CombatCampaignDefinition campaign,
    CombatFoundationRewardResidualTrainingResult residuals)
{
    campaign.RewardScoreResiduals.Clear();
    campaign.RewardScoreConditionalResiduals =
        new Dictionary<string, double>(
            residuals.ConditionalResiduals,
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
                                && RoleStrategyArchiveEligible(job, item)
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

static bool RoleStrategyArchiveEligible(
    CombatFoundationWorkerJob job,
    CombatFoundationSuccessCase successCase)
{
    var roleId = job.Request.TrainingCampaign.Player?.RoleId ?? "";
    if (!string.Equals(roleId, "career_2", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(
            roleId,
            "career_4",
            StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }
    var metrics = AuraToolsRoleTrainingDiagnostics.Analyze(
        successCase.Episodes,
        new[] { successCase.Observation });
    var eligibleFrames = metrics.GetValueOrDefault(
        "nana.role-strategy-eligible-frames");
    if (eligibleFrames > 0d
        && metrics.GetValueOrDefault(
            "nana.role-strategy-frame-coverage") < 0.999999d)
    {
        return false;
    }
    if (metrics.GetValueOrDefault(
            "nana.selected-strategically-prohibited-actions") > 0d
        || metrics.GetValueOrDefault(
            "nana.selected-nonpositive-devours") > 0d)
    {
        return false;
    }
    var devours = metrics.GetValueOrDefault("nana.devours");
    return devours < 20d
           || metrics.GetValueOrDefault("nana.premature-devour-rate")
              <= 0.05d;
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
            .Count(),
        LogicalProcessors = Math.Max(1, Environment.ProcessorCount),
        TotalElapsedSeconds = Math.Max(0d, training.ElapsedSeconds),
        WorkerCpuSeconds = Math.Max(0d, training.CpuSeconds),
        ExternalCpuSeconds = (training.PhaseExternalCpuSeconds
                              ?? new Dictionary<string, double>())
            .Values
            .Where(value => value > 0d)
            .Sum(),
        EnabledPerformanceProbes = new List<string>
        {
            "phase-wall-time",
            "worker-cpu-time",
            "external-process-cpu-time",
            "managed-allocation",
            "phase-peak-concurrency",
            "phase-worker-threads",
            "transformer-stage-time",
            "transformer-peak-working-set"
        }
    };
    analysis.EffectiveCpuUtilizationPercent =
        analysis.TotalElapsedSeconds <= 0d
            ? 0d
            : (analysis.WorkerCpuSeconds + analysis.ExternalCpuSeconds)
              / analysis.TotalElapsedSeconds
              / analysis.LogicalProcessors
              * 100d;

    var phaseElapsed = training.PhaseElapsedSeconds
                       ?? new Dictionary<string, double>();
    var phaseCpu = training.PhaseCpuSeconds
                   ?? new Dictionary<string, double>();
    var phaseExternalCpu = training.PhaseExternalCpuSeconds
                           ?? new Dictionary<string, double>();
    var phaseAllocated = training.PhaseAllocatedBytes
                         ?? new Dictionary<string, long>();
    var phasePeakWork = training.PhasePeakConcurrentWork
                        ?? new Dictionary<string, int>();
    var phaseThreads = training.PhaseObservedWorkerThreads
                       ?? new Dictionary<string, int>();
    var phaseHotspots = phaseElapsed.Keys
        .Concat(phaseCpu.Keys)
        .Concat(phaseExternalCpu.Keys)
        .Concat(phaseAllocated.Keys)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(name =>
        {
            var elapsed = phaseElapsed.TryGetValue(name, out var wall)
                ? Math.Max(0d, wall)
                : 0d;
            var workerCpu = phaseCpu.TryGetValue(name, out var worker)
                ? Math.Max(0d, worker)
                : 0d;
            var externalCpu = phaseExternalCpu.TryGetValue(
                name,
                out var external)
                ? Math.Max(0d, external)
                : 0d;
            var allocated = phaseAllocated.TryGetValue(
                name,
                out var bytes)
                ? Math.Max(0L, bytes)
                : 0L;
            var utilization = elapsed <= 0d
                ? 0d
                : (workerCpu + externalCpu)
                  / elapsed
                  / analysis.LogicalProcessors
                  * 100d;
            return new CombatFoundationPerformanceHotspot
            {
                Scope = "phase",
                Name = name,
                ElapsedSeconds = elapsed,
                WallTimeSharePercent = analysis.TotalElapsedSeconds <= 0d
                    ? 0d
                    : elapsed / analysis.TotalElapsedSeconds * 100d,
                WorkerCpuSeconds = workerCpu,
                ExternalCpuSeconds = externalCpu,
                EffectiveCpuUtilizationPercent = utilization,
                AllocatedBytes = allocated,
                AllocationMegabytesPerSecond = elapsed <= 0d
                    ? 0d
                    : allocated / elapsed / (1024d * 1024d),
                PeakConcurrentWork = phasePeakWork.TryGetValue(
                    name,
                    out var peak)
                    ? Math.Max(0, peak)
                    : 0,
                ObservedWorkerThreads = phaseThreads.TryGetValue(
                    name,
                    out var threads)
                    ? Math.Max(0, threads)
                    : 0,
                UtilizationBand = PerformanceUtilizationBand(utilization)
            };
        })
        .OrderByDescending(item => item.ElapsedSeconds)
        .ThenBy(item => item.Name, StringComparer.Ordinal)
        .ToList();
    for (var index = 0; index < phaseHotspots.Count; index++)
    {
        phaseHotspots[index].Rank = index + 1;
    }
    analysis.PerformanceHotspots.AddRange(phaseHotspots);

    var transformerStages = (training.TransformerTeacherReports
                             ?? new List<CombatTransformerTeacherReport>())
        .Where(report => report != null)
        .SelectMany(report => report.StageSeconds
            ?? new Dictionary<string, double>())
        .Where(pair => pair.Value > 0.001d)
        .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => new CombatFoundationPerformanceHotspot
        {
            Scope = "transformer-stage",
            Name = group.Key,
            ElapsedSeconds = group.Sum(pair => Math.Max(0d, pair.Value)),
            UtilizationBand = "stage-timing"
        })
        .OrderByDescending(item => item.ElapsedSeconds)
        .ThenBy(item => item.Name, StringComparer.Ordinal)
        .ToList();
    for (var index = 0; index < transformerStages.Count; index++)
    {
        transformerStages[index].Rank = index + 1;
        transformerStages[index].WallTimeSharePercent =
            analysis.TotalElapsedSeconds <= 0d
                ? 0d
                : transformerStages[index].ElapsedSeconds
                  / analysis.TotalElapsedSeconds
                  * 100d;
    }
    analysis.PerformanceHotspots.AddRange(transformerStages);
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

static string PerformanceUtilizationBand(double percent)
{
    if (percent >= 70d) return "high";
    if (percent >= 40d) return "moderate";
    if (percent > 0d) return "low";
    return "not-observed";
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
