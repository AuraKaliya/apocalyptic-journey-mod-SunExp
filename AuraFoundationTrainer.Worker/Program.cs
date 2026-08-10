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
var replaySeedArgument = ResolveOptionValue(args, "--replay-seed");
var roundChild = args.Any(argument => string.Equals(
    argument,
    "--round-child",
    StringComparison.OrdinalIgnoreCase));
if (string.IsNullOrWhiteSpace(jobPath)
    || !CombatFoundationPathRuntime.FileExists(jobPath))
{
    Console.Error.WriteLine(
        "Usage: AuraFoundationTrainer.Worker --job <job.json> "
        + "[--replay-seed <ulong> --difficulty <id> "
        + "--checkpoint <checkpoint.json> --output <result.json> "
        + "--trace none|summary|full --exploration <0..1> "
        + "--exact-branch <0..1>]");
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
    PrepareCheckpointStoragePaths(job);
    if (!CombatFoundationCheckpointCatalogStore.TryGetResetBoundary(
            job,
            out _,
            out _,
            out var checkpointBoundaryDiagnostic))
    {
        throw new InvalidOperationException(
            "Checkpoint storage boundary is invalid: "
            + checkpointBoundaryDiagnostic);
    }
    if (!roundChild
        && string.IsNullOrWhiteSpace(replaySeedArgument)
        && job.Request.EnableIterationProcessIsolation
        && !job.Request.PreflightOnly)
    {
        return RunIterationSupervisor(job);
    }
    AuraToolsAuthoritativeRoleSemantics.Initialize();
    AuraToolsRoleCampaignStrategy.Apply(job.Request.TrainingCampaign);
    AuraToolsRoleCampaignStrategy.Apply(job.Request.ValidationCampaign);
    if (!string.IsNullOrWhiteSpace(replaySeedArgument))
    {
        job.ResultPath = "";
        return ReplayCampaign(
            job,
            jobPath,
            replaySeedArgument,
            args);
    }
    // Archive residuals are learned data and change after every accepted run.
    // Capture the structural workload identity before those residuals are
    // merged so an execution plan remains reusable across training rounds.
    job.Request.AutoTuneCampaignKey =
        CombatCampaignFoundationTrainer.CampaignFingerprint(
            job.Request.TrainingCampaign);
    // The final 50 + 50 sample is the auditable process record for this run.
    job.Request.RetainValidationRunDetails = true;
    CombatFoundationPathRuntime.CreateDirectory(job.ResultDirectory);
    File.Delete(Path.Combine(
        job.ResultDirectory,
        CombatFoundationModelPackageProtocol.FileName));
    File.Delete(Path.Combine(
        job.ResultDirectory,
        CombatFoundationModelPackageProtocol.WeightsFileName));
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
    using var trainingLease = AcquireTrainingLease(job);
    var shouldResetCheckpoint =
        CombatFoundationCheckpointCatalogStore.HasPendingReset(job)
        || !job.ResumeFromCheckpoint && job.ResetCheckpointOnFreshStart;
    if (shouldResetCheckpoint)
    {
        ResetActiveCheckpoint(job);
    }
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
        Math.Min(
            Environment.ProcessorCount,
            job.Request.MaximumDegreeOfParallelism <= 0
                ? Environment.ProcessorCount
                : job.Request.MaximumDegreeOfParallelism));
    ThreadPool.GetMinThreads(out var minimumWorkers, out var minimumIo);
    ThreadPool.SetMinThreads(
        Math.Max(
            minimumWorkers,
            Math.Max(
                requestedWorkers + 2,
                job.Request.ThreadPoolMinimumWorkerThreads)),
        minimumIo);
    // Learned archive residuals must be applied before both Worker and Trainer
    // compute identity. CampaignFingerprint normalizes those learned values,
    // so structural compatibility remains stable across training rounds.
    PrepareCaseArchive(job, build.Ruleset.RulesetHash);
    var requestFingerprint = CombatFoundationRequestIdentity.CreateFingerprint(
        job,
        build.Ruleset.RulesetHash);
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
        else if (!string.IsNullOrWhiteSpace(checkpointDiagnostic))
        {
            resumeDiagnostic = checkpointDiagnostic
                               + " | historical recovery: "
                               + resumeDiagnostic;
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
        job.Request.ReleaseResumeReplayAfterTransfer = true;
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
    CombatFoundationModelSelectionAnchorStore.Load(job);
    var effectiveStartMode = resumedFromCheckpoint
        ? checkpointSnapshot == null
            ? "historical-working"
            : job.ResumeMode == CombatFoundationCheckpointResumeModes.ModelBranch
                ? "checkpoint-model-branch"
                : "checkpoint-exact"
        : "fresh";
    var resumeProvenance = job.ResumeProvenance
                           ?? new CombatFoundationResumeProvenance();
    if (!resumeProvenance.ExternalRequestCaptured)
    {
        resumeProvenance.ExternalRequestCaptured = true;
        resumeProvenance.ExternalResumeRequested = job.ResumeFromCheckpoint;
        resumeProvenance.ExternalRequestedStartMode = job.RequestedStartMode;
    }
    if (!resumeProvenance.ExternalOutcomeCaptured)
    {
        resumeProvenance.ExternalOutcomeCaptured = true;
        resumeProvenance.ExternalResumeApplied = resumedFromCheckpoint;
        resumeProvenance.ExternalResumeDiagnostic = resumeDiagnostic;
        resumeProvenance.ExternalEffectiveStartMode = effectiveStartMode;
    }
    resumeProvenance.InternalResumeRequested =
        resumeProvenance.InternalSegmentNumber > 1
        && job.ResumeFromCheckpoint;
    resumeProvenance.InternalResumeApplied =
        resumeProvenance.InternalSegmentNumber > 1
        && resumedFromCheckpoint;
    resumeProvenance.InternalResumeDiagnostic =
        resumeProvenance.InternalSegmentNumber > 1
            ? resumeDiagnostic
            : "";
    resumeProvenance.InternalEffectiveStartMode =
        resumeProvenance.InternalSegmentNumber > 1
            ? effectiveStartMode
            : "";
    job.ResumeProvenance = resumeProvenance;
    var checkpointCatalogRead = CombatFoundationCheckpointCatalogStore.Read(
        job.CheckpointCatalogPath);
    var checkpointRetention = CombatFoundationCheckpointCatalogStore
        .ReadArtifactRetention(job.CheckpointCatalogPath);
    if (checkpointCatalogRead.RecoveryUncertain)
    {
        Console.Error.WriteLine(
            "Checkpoint catalog recovery is uncertain; artifact cleanup is "
            + "disabled to preserve immutable history: "
            + checkpointCatalogRead.Diagnostic);
    }
    CombatFoundationCheckpointCatalogStore.ExecuteCleanupIfCertain(
        checkpointCatalogRead,
        () =>
        {
        CombatFoundationCheckpointStorage.CleanupArtifacts(
            job.CheckpointPath,
            job.CheckpointEpisodesPath,
            (checkpointCatalogRead.Catalog?.Entries
                 .Select(item => item.EpisodeSnapshotPath)
                 .Where(path => !string.IsNullOrWhiteSpace(path))
             ?? Array.Empty<string>())
                .Concat(checkpointRetention.SnapshotPaths)
                .Concat(checkpointSnapshot == null
                    ? Array.Empty<string>()
                    : new[] { checkpointSnapshot.Path })
                .Concat(CombatFoundationCheckpointCatalogStore
                    .ReadActiveSnapshotRetentionPaths(
                        job.CheckpointPath,
                        job.CheckpointEpisodesPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        });
    var checkpointArtifactDirectory = Path.GetDirectoryName(
        CombatFoundationPathRuntime.Normalize(job.CheckpointPath))
        ?? job.ResultDirectory;
    CombatFoundationReplayWarehouse? replayWarehouse = null;
    if (job.Request.EnableReplayWarehouse)
    {
        replayWarehouse = new CombatFoundationReplayWarehouse(
            Path.Combine(
                checkpointArtifactDirectory,
                "foundation-replay-warehouse-v1"));
    }
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
    var checkpointSourceReplayIdentity =
        checkpointSnapshot?.SourceReplayIdentity ?? "";
    if (string.IsNullOrWhiteSpace(checkpointSourceReplayIdentity))
    {
        checkpointSourceReplayIdentity =
            checkpointSnapshot?.ReplayIdentity ?? "";
    }
    if (string.IsNullOrWhiteSpace(checkpointSourceReplayIdentity)
        && job.Request.Resume?.Replay != null)
    {
        checkpointSourceReplayIdentity =
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
                var catalogReadBeforeArtifacts =
                    CombatFoundationCheckpointCatalogStore.Read(
                        job.CheckpointCatalogPath);
                CombatFoundationCheckpointCatalogStore
                    .EnsureWritableBaseline(catalogReadBeforeArtifacts);
                var sourceReplayIdentity = ReplayIdentity(state.Replay);
                var nextSnapshot = checkpointSnapshot;
                if (nextSnapshot == null
                    || !CombatFoundationPathRuntime.FileExists(nextSnapshot.Path)
                    || !string.Equals(
                        checkpointSourceReplayIdentity,
                        sourceReplayIdentity,
                        StringComparison.Ordinal))
                {
                    IReadOnlyList<CombatEpisode> snapshotReplay = state.Replay;
                    var warehouseReplayKeys = nextSnapshot?.WarehouseReplayKeys
                                                  ?.Where(key =>
                                                      !string.IsNullOrWhiteSpace(
                                                          key))
                                                  .ToHashSet(StringComparer.Ordinal)
                                              ?? new HashSet<string>(
                                                  StringComparer.Ordinal);
                    if (job.Request.EnableIterationProcessIsolation
                        && replayWarehouse != null
                        && state.Replay.Count > 0)
                    {
                        var archiveReport = replayWarehouse.Archive(
                            Math.Max(1, state.NextIteration),
                            state.Replay);
                        var archivedSourceEpisodes =
                            archiveReport.ArchivedEpisodes
                            + archiveReport.DuplicateEpisodes;
                        if (string.IsNullOrWhiteSpace(archiveReport.Error)
                            && archivedSourceEpisodes
                            >= archiveReport.SourceEpisodes)
                        {
                            foreach (var episode in state.Replay.Where(
                                         episode => episode != null))
                            {
                                warehouseReplayKeys.Add(
                                    CombatFoundationReplayWarehouse.StableKey(
                                        episode));
                            }
                            var trainingOptions = job.Request.Training.Normalized();
                            var requiredReplay = (job.Request
                                                      .AuthoritativeContentEpisodes
                                                  ?? new List<CombatEpisode>())
                                .Concat(job.Request.ExpertReplayEpisodes
                                        ?? new List<CombatEpisode>())
                                .ToList();
                            snapshotReplay = CombatFoundationReplaySampler
                                .SelectProcessBoundary(
                                    state.Replay,
                                    requiredReplay,
                                    job.Request.ReplayHotWindowEpisodeLimit,
                                    job.Request.ReplayHotWindowFrameLimit,
                                    job.Request
                                        .ReplayHotWindowEstimatedBytesLimit,
                                    trainingOptions.MinimumEpisodes,
                                    job.Request.EnableStratifiedReplay,
                                    new CombatFoundationReplayBalanceOptions
                                    {
                                        MinimumAdvancedShare = job.Request
                                            .MinimumAdvancedReplayShare,
                                        MinimumAdvancedDefeatShare = job.Request
                                            .MinimumAdvancedDefeatReplayShare,
                                        EnablePrioritySampling = job.Request
                                            .EnablePrioritizedReplay,
                                        AllowCrossDifficultyBackfill = false
                                    })
                                .Episodes;
                        }
                    }
                    var snapshotReplayIdentity = ReplayIdentity(snapshotReplay);
                    nextSnapshot =
                        CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
                            job.CheckpointEpisodesPath,
                            snapshotReplay,
                            SerializeCompact,
                            snapshotReplayIdentity,
                            checkpointSerializationWorkers);
                    nextSnapshot.SourceReplayIdentity = sourceReplayIdentity;
                    nextSnapshot.SourceEpisodeCount = state.Replay.Count;
                    nextSnapshot.ProcessBoundaryCompacted =
                        snapshotReplay.Count < state.Replay.Count;
                    nextSnapshot.WarehouseReplayKeys = warehouseReplayKeys
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToList();
                }
                var checkpoint = new CombatFoundationWorkerCheckpoint
                {
                    RequestFingerprint = requestFingerprint,
                    RequestIdentityFields = CombatFoundationRequestIdentity
                        .CreateFields(job, build.Ruleset.RulesetHash),
                    RulesetHash = build.Ruleset.RulesetHash,
                    EpisodesPath = nextSnapshot.Path,
                    EpisodeSnapshot = nextSnapshot,
                    UpdatedUtc = DateTime.UtcNow,
                    Resume = WithoutReplay(state)
                };
                WriteAtomicJson(
                    job.CheckpointPath,
                    checkpoint,
                    Formatting.None,
                    retainBackup: true,
                    compress: true);
                var catalogSnapshots = WriteCheckpointCatalogEntry(
                    job,
                    checkpoint,
                    requestFingerprint,
                    build.Ruleset.RulesetHash,
                    catalogReadBeforeArtifacts);
                checkpointSnapshot = nextSnapshot;
                checkpointSourceReplayIdentity = sourceReplayIdentity;
                checkpointWarning = "";
                CombatFoundationCheckpointStorage.CleanupArtifacts(
                    job.CheckpointPath,
                    job.CheckpointEpisodesPath,
                    catalogSnapshots
                        .Append(nextSnapshot.Path)
                        .Concat(CombatFoundationCheckpointCatalogStore
                            .ReadActiveSnapshotRetentionPaths(
                                job.CheckpointPath,
                                job.CheckpointEpisodesPath))
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
    job.Request.IterationResourceBarrier = checkpointPipeline.Drain;
    var incrementallyArchivedCases =
        new HashSet<string>(StringComparer.Ordinal);
    var incrementallyDuplicateCases =
        new HashSet<string>(StringComparer.Ordinal);
    var capacityRejectedCaseIds =
        new HashSet<string>(StringComparer.Ordinal);
    var capacityRejectedObservationIds =
        new HashSet<string>(StringComparer.Ordinal);
    var incrementalArchiveErrors = new List<string>();
    var archiveCapacityRejectedObservations = 0;
    var archiveCapacityRejectedCases = 0;
    var incrementalExpertReferenceBytes = 0L;
    var incrementalDeduplicatedExpertBytes = 0L;
    var archiveSinkGate = new object();
    if (job.Request.EnableSuccessCaseArchive)
    {
        var priorObservationRecorded = job.Request.ObservationRecorded;
        job.Request.ObservationRecorded = observation =>
        {
            priorObservationRecorded?.Invoke(observation);
            lock (archiveSinkGate)
            {
                try
                {
                    var path = ResolveObservationPath(job, observation);
                    if (File.Exists(path))
                    {
                        return;
                    }
                    if (!ArchiveWriteBudget.TryReserve(
                            Path.GetDirectoryName(path)!,
                            CombatFoundationCaseArchiveProtocol
                                .MaximumObservationsPerCompatibility))
                    {
                        archiveCapacityRejectedObservations++;
                        capacityRejectedObservationIds.Add(
                            observation.CaseId);
                        return;
                    }
                    WriteAtomicCompressed(path, SerializeCompact(observation));
                }
                catch (Exception ex)
                {
                    incrementalArchiveErrors.Add(
                        "Observation " + observation.CaseId + ": "
                        + ex.Message);
                }
            }
        };
        job.Request.SuccessCaseSink = successCase =>
        {
            lock (archiveSinkGate)
            {
                try
                {
                    var observation = successCase.Observation;
                    if (!RoleStrategyArchiveEligible(job, successCase))
                    {
                        return true;
                    }
                    if (incrementallyArchivedCases.Contains(observation.CaseId)
                        || incrementallyDuplicateCases.Contains(
                            observation.CaseId))
                    {
                        return true;
                    }
                    var expertPath = ResolveExpertReferencePath(job, successCase);
                    if (!File.Exists(expertPath)
                        && !ArchiveWriteBudget.TryReserve(
                            Path.GetDirectoryName(expertPath)!,
                            CombatFoundationCaseArchiveProtocol
                                .MaximumExpertCasesPerCompatibility))
                    {
                        capacityRejectedCaseIds.Add(observation.CaseId);
                        archiveCapacityRejectedCases++;
                        return true;
                    }
                    var casePath = ResolveSuccessCasePath(
                        job,
                        successCase,
                        CombatFoundationCaseArchiveProtocol.CaseDirectoryName);
                    var caseAlreadyExisted = File.Exists(casePath);
                    if (!caseAlreadyExisted)
                    {
                        WriteAtomicCompressed(
                            casePath,
                            SerializeCompact(successCase));
                    }
                    if (!File.Exists(expertPath))
                    {
                        WriteExpertReference(expertPath, successCase, casePath);
                    }
                    incrementalExpertReferenceBytes +=
                        new FileInfo(expertPath).Length;
                    incrementalDeduplicatedExpertBytes += Math.Max(
                        0L,
                        new FileInfo(casePath).Length
                        - new FileInfo(expertPath).Length);
                    if (caseAlreadyExisted)
                    {
                        incrementallyDuplicateCases.Add(observation.CaseId);
                    }
                    else
                    {
                        incrementallyArchivedCases.Add(observation.CaseId);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    incrementalArchiveErrors.Add(
                        "Success case "
                        + successCase.Observation.CaseId
                        + ": "
                        + ex.Message);
                    return false;
                }
            }
        };
    }

    var autoTuneCachePath = Path.Combine(
        job.SuccessArchiveDirectory,
        CombatFoundationAutoTuneProtocol.CacheFileName);
    var autoTuneCachePolicy = new CombatFoundationAutoTuneCachePolicy(
        job.Request.ReuseAutoTuneCache);
    job.Request.AutoTuneHardwareKey = AutoTuneHardwareKey();
    if (string.Equals(
            job.Request.ParallelismProfile,
            CombatFoundationExecutionProfileNames.Auto,
            StringComparison.OrdinalIgnoreCase))
    {
        if (autoTuneCachePolicy.ShouldLoad(autoTuneCachePath))
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
        job.Request.AutoTuneCompleted = autoTuneCachePolicy.ReuseEnabled
            ? result =>
        {
            if (!autoTuneCachePolicy.ShouldPersist(result))
            {
                return;
            }
            Directory.CreateDirectory(job.SuccessArchiveDirectory);
            WriteAtomicJson(autoTuneCachePath, result!);
        }
            : null;
        if (!autoTuneCachePolicy.ReuseEnabled)
        {
            job.Request.AutoTuneCache = null;
        }
    }

    if (replayWarehouse != null)
    {
        job.Request.ReplayArchiveSink = replayWarehouse.Archive;
        job.Request.HistoricalReplaySource = (
            iteration,
            excludedKeys,
            episodeLimit,
            bytesLimit) => replayWarehouse.Load(
            iteration,
            excludedKeys,
            episodeLimit,
            bytesLimit,
            (IReadOnlyCollection<string>?)checkpointSnapshot
                ?.WarehouseReplayKeys
            ?? Array.Empty<string>());
    }

    LoadPinnedSeedHistory(job);

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
                "transformer-runtime-auto-tune-v4.json"),
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
                incrementallyDuplicateCases,
                capacityRejectedObservationIds,
                capacityRejectedCaseIds,
                incrementalArchiveErrors,
                archiveCapacityRejectedObservations,
                archiveCapacityRejectedCases,
                incrementalExpertReferenceBytes,
                incrementalDeduplicatedExpertBytes);
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
    try
    {
        PersistDecisionDifferences(job, training);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            "Foundation decision-difference diagnostics were skipped: "
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
    var resumableEpisodeCount = 0;
    var resumable = !job.Request.PreflightOnly
                    && !training.AcceptancePassed
                    && roleStrategyGatePassed
                    && TryGetResumableCheckpoint(
                        job,
                        out resumableEpisodesPath,
                        out resumableEpisodeCount);
    if (resumable)
    {
        training.PersistedReplayEpisodes = resumableEpisodeCount;
    }
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
        : training.ContinuationRequired && resumable
            ? "iteration-boundary"
        : training.AcceptancePassed
            ? "training-accepted"
            : resumable
                ? "training-rejected-resumable"
                : "training-rejected";
    var finalIteration = training.Iterations.LastOrDefault();
    var bestValidationEpoch = finalIteration?.ModelEpochHistory
        .Where(item => !item.Calibrated)
        .OrderBy(item => item.Validation?.CompositeLoss ?? double.MaxValue)
        .ThenBy(item => item.Epoch)
        .Select(item => item.Epoch)
        .FirstOrDefault() ?? training.ModelBestEpoch;
    var deploymentSelectedEpoch = finalIteration?.TuningSelectedEpoch
                                  ?? training.ModelBestEpoch;
    var workerResult = new CombatFoundationWorkerResult
    {
        JobId = job.JobId,
        Success = true,
        WorkerCompleted = true,
        TrainingSucceeded = training.Success,
        ModelAccepted = training.AcceptancePassed,
        EpochsExecuted = training.ModelEpochHistory.Count(item =>
            !item.Calibrated),
        SelectedEpoch = deploymentSelectedEpoch,
        BestValidationEpoch = bestValidationEpoch,
        DeploymentSelectedEpoch = deploymentSelectedEpoch,
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
        ResumeRequested = resumeProvenance.ExternalResumeRequested,
        ResumedFromCheckpoint = resumeProvenance.ExternalResumeApplied,
        ResumeDiagnostic = resumeProvenance.ExternalResumeDiagnostic,
        RequestedStartMode = resumeProvenance.ExternalRequestedStartMode,
        EffectiveStartMode = resumeProvenance.ExternalEffectiveStartMode,
        ResumeProvenance = resumeProvenance,
        Training = training
    };
    try
    {
        var artifacts = string.Equals(
                completionKind,
                "iteration-boundary",
                StringComparison.Ordinal)
            ? FoundationArtifactBundleWriter.WriteBoundarySnapshot(job, training)
            : FoundationArtifactBundleWriter.WriteTerminalBundle(
                job,
                training,
                trainingAnalysis,
                completionKind);
        workerResult.CandidateArtifactProduced = artifacts.ModelProduced;
        workerResult.ArtifactBundleDirectory = artifacts.BundleDirectory;
        workerResult.ArtifactManifestPath = artifacts.ManifestPath;
        workerResult.CandidateModelPath = artifacts.CandidateModelPath;
        workerResult.CapabilityReportPath = artifacts.CapabilityReportPath;
        workerResult.CapabilityReportHtmlPath =
            artifacts.CapabilityReportHtmlPath;
        workerResult.SimulationDatabasePath =
            artifacts.SimulationDatabasePath;
        workerResult.SeedRegistryPath = artifacts.SeedRegistryPath;
        workerResult.ModelNodeGraphPath = artifacts.ModelNodeGraphPath;
    }
    catch (Exception ex)
    {
        workerResult.ArtifactWarning = ex.ToString();
        Console.Error.WriteLine(
            "Foundation artifact bundle export failed: " + ex);
    }
    if (string.Equals(
            completionKind,
            "training-accepted",
            StringComparison.Ordinal))
    {
        var modelPackage = CombatFoundationModelPackageProtocol.Create(
            job,
            workerResult,
            workerBinarySha256);
        var deploymentModelDirectory = string.IsNullOrWhiteSpace(
            workerResult.ArtifactBundleDirectory)
            ? job.ResultDirectory
            : Path.Combine(workerResult.ArtifactBundleDirectory, "model");
        Directory.CreateDirectory(deploymentModelDirectory);
        workerResult.ModelPackagePath = Path.Combine(
            deploymentModelDirectory,
            CombatFoundationModelPackageProtocol.FileName);
        var weightsPath = Path.Combine(
            deploymentModelDirectory,
            CombatFoundationModelPackageProtocol.WeightsFileName);
        try
        {
            var trainingModel = modelPackage.Model
                                ?? throw new InvalidOperationException(
                                    "待发布底模网络为空");
            modelPackage.ModelArtifact = CombatPolicyValueArtifactProtocol.Write(
                weightsPath,
                trainingModel);
            modelPackage.Model = null;
            WriteAtomicJson(
                workerResult.ModelPackagePath,
                modelPackage,
                Formatting.None);

            var reloaded = JsonConvert.DeserializeObject<
                CombatFoundationModelPackage>(
                File.ReadAllText(workerResult.ModelPackagePath));
            if (!CombatFoundationModelPackageProtocol.TryValidate(
                    reloaded,
                    out var artifactDiagnostic)
                || !CombatPolicyValueArtifactProtocol.TryLoad(
                    deploymentModelDirectory,
                    reloaded?.ModelArtifact,
                    out var runtimeModel,
                    out artifactDiagnostic))
            {
                throw new InvalidDataException(
                    "发布后底模复验失败：" + artifactDiagnostic);
            }
            _ = new ManagedCombatPolicyValueModel(runtimeModel);
            workerResult.ModelPackageBytes = checked(
                new FileInfo(workerResult.ModelPackagePath).Length
                + new FileInfo(weightsPath).Length);
            if (!CombatFoundationModelPackageProtocol.TryValidateSerializedSize(
                    workerResult.ModelPackageBytes,
                    out var packageSizeDiagnostic))
            {
                throw new InvalidOperationException(packageSizeDiagnostic);
            }
            workerResult.ModelPackageSizeWarning = packageSizeDiagnostic;
            if (!string.IsNullOrWhiteSpace(
                    workerResult.ArtifactBundleDirectory))
            {
                FoundationArtifactBundleWriter.AttachDeploymentPackage(
                    workerResult.ArtifactBundleDirectory,
                    workerResult.ModelPackagePath,
                    weightsPath);
            }
        }
        catch
        {
            File.Delete(workerResult.ModelPackagePath);
            File.Delete(weightsPath);
            workerResult.ModelPackagePath = "";
            throw;
        }
    }
    WriteAtomicJson(
        job.TrainingAnalysisPath,
        trainingAnalysis);
    training.ValidationRuns.Clear();
    CombatFoundationWorkerResultProjection.StripRejectedBusinessPayload(
        workerResult);
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
        var terminalResume = TerminalResumeProvenance(job, "cancelled");
        var resumable = TryGetResumableCheckpoint(
            job,
            out var resumableEpisodesPath,
            out var resumableEpisodeCount);
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
                PersistedReplayEpisodes = resumable
                    ? resumableEpisodeCount
                    : 0,
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
                ResumeRequested = terminalResume.ExternalResumeRequested,
                ResumedFromCheckpoint = terminalResume.ExternalResumeApplied,
                ResumeDiagnostic = terminalResume.ExternalResumeDiagnostic,
                RequestedStartMode =
                    terminalResume.ExternalRequestedStartMode,
                EffectiveStartMode =
                    terminalResume.ExternalEffectiveStartMode,
                ResumeProvenance = terminalResume
            });
    }
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    if (job != null && !string.IsNullOrWhiteSpace(job.ResultPath))
    {
        var terminalResume = TerminalResumeProvenance(job, "failed");
        var resumable = TryGetResumableCheckpoint(
            job,
            out var resumableEpisodesPath,
            out var resumableEpisodeCount);
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
                PersistedReplayEpisodes = resumable
                    ? resumableEpisodeCount
                    : 0,
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
                ResumeRequested = terminalResume.ExternalResumeRequested,
                ResumedFromCheckpoint = terminalResume.ExternalResumeApplied,
                ResumeDiagnostic = terminalResume.ExternalResumeDiagnostic,
                RequestedStartMode =
                    terminalResume.ExternalRequestedStartMode,
                EffectiveStartMode =
                    terminalResume.ExternalEffectiveStartMode,
                ResumeProvenance = terminalResume
            });
    }
    return 1;
}

static CombatFoundationResumeProvenance TerminalResumeProvenance(
    CombatFoundationWorkerJob job,
    string terminalMode)
{
    var provenance = job.ResumeProvenance
                     ?? new CombatFoundationResumeProvenance();
    if (!provenance.ExternalRequestCaptured)
    {
        provenance.ExternalRequestCaptured = true;
        provenance.ExternalResumeRequested = job.ResumeFromCheckpoint;
        provenance.ExternalRequestedStartMode = job.RequestedStartMode;
    }
    if (!provenance.ExternalOutcomeCaptured)
    {
        provenance.ExternalOutcomeCaptured = true;
        provenance.ExternalResumeApplied = false;
        provenance.ExternalEffectiveStartMode = terminalMode;
    }
    return provenance;
}

static int ReplayCampaign(
    CombatFoundationWorkerJob job,
    string jobPath,
    string seedArgument,
    string[] arguments)
{
    if (!ulong.TryParse(seedArgument, out var worldSeed))
    {
        throw new InvalidDataException("Invalid --replay-seed value.");
    }
    var difficulty = ResolveOptionValue(arguments, "--difficulty");
    if (string.IsNullOrWhiteSpace(difficulty))
    {
        difficulty = "advanced";
    }
    var trace = ResolveOptionValue(arguments, "--trace");
    if (!Enum.TryParse<CombatSimulationTraceLevel>(
            string.IsNullOrWhiteSpace(trace) ? "Full" : trace,
            ignoreCase: true,
            out var traceLevel))
    {
        throw new InvalidDataException("Invalid --trace value.");
    }
    var exploration = OptionalProbability(arguments, "--exploration");
    var exactBranch = OptionalProbability(arguments, "--exact-branch");
    var build = CombatSimulationRegistry.BuildRuleset(job.Ruleset);
    if (!build.Success)
    {
        throw new InvalidOperationException(
            "Ruleset build failed: " + string.Join("; ", build.Errors.Take(8)));
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
    job.Request.TrainingCampaign.TraceLevel = traceLevel;
    job.Request.TrainingCampaign.FullTraceFinalEncounterOnly = false;
    var checkpointPath = ResolveReplayCheckpoint(
        job,
        jobPath,
        ResolveArgument(arguments, "--checkpoint"));
    CombatPolicyValueNetworkDefinition? model = job.InitialChampion;
    if (!string.IsNullOrWhiteSpace(checkpointPath))
    {
        var checkpoint = Deserialize<CombatFoundationWorkerCheckpoint>(
            CombatFoundationCheckpointStorage.ReadAllTextShared(checkpointPath));
        model = checkpoint?.Resume?.ModelTraining?.BestModel
                ?? checkpoint?.Resume?.ModelTraining?.Model
                ?? checkpoint?.Resume?.LatestTrainingModel
                ?? checkpoint?.Resume?.WorkingChampion
                ?? checkpoint?.Resume?.Champion
                ?? model;
    }
    var engine = new CombatSimulationEngine(
        new AuraToolsNativeRewardExtensionFactory());
    var result = new CombatCampaignFoundationTrainer(
            new CombatCampaignRunner(engine))
        .ReplayTrainingCampaign(
            job.Request,
            build.Ruleset,
            model,
            difficulty,
            worldSeed,
            exploration,
            exactBranch);
    var outputPath = ResolveArgument(arguments, "--output");
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        outputPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(jobPath)) ?? ".",
            "foundation-replay-"
            + difficulty
            + "-"
            + worldSeed
            + ".json");
    }
    CombatFoundationCheckpointStorage.WriteAtomicText(
        outputPath,
        Serialize(result));
    Console.WriteLine(
        "Replay completed: seed="
        + worldSeed
        + ", difficulty="
        + difficulty
        + ", battles="
        + result.CompletedBattles
        + ", invalid="
        + result.Invalid
        + ", finalBossVictory="
        + result.FinalBossVictory
        + ", model="
        + (model?.ModelId ?? "none"));
    Console.WriteLine("Replay result written to " + Path.GetFullPath(outputPath));
    return 0;
}

static double? OptionalProbability(
    string[] arguments,
    string option)
{
    var raw = ResolveOptionValue(arguments, option);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }
    if (!double.TryParse(
            raw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
        || value < 0d
        || value > 1d)
    {
        throw new InvalidDataException("Invalid " + option + " value.");
    }
    return value;
}

static string ResolveReplayCheckpoint(
    CombatFoundationWorkerJob job,
    string jobPath,
    string requestedPath)
{
    if (!string.IsNullOrWhiteSpace(requestedPath))
    {
        if (!CombatFoundationCheckpointCatalogStore.TrySelectResumeCandidate(
                requestedPath,
                explicitlySelected: true,
                out var selectedPath,
                out _,
                out _,
                out var diagnostic))
        {
            throw new InvalidDataException(
                "Replay checkpoint is invalid: " + diagnostic);
        }
        return selectedPath;
    }
    if (!string.IsNullOrWhiteSpace(job.CheckpointPath))
    {
        if (CombatFoundationCheckpointCatalogStore.TrySelectResumeCandidate(
                job.CheckpointPath,
                explicitlySelected: false,
                out var activeCandidate,
                out _,
                out _,
                out _))
        {
            return activeCandidate;
        }
    }
    var jobDirectory = Path.GetDirectoryName(Path.GetFullPath(jobPath)) ?? ".";
    var resultsDirectory = Directory.GetParent(jobDirectory)?.FullName
                           ?? jobDirectory;
    foreach (var candidate in new[]
             {
                 CombatFoundationWorkerProtocol.CheckpointFileName,
                 CombatFoundationWorkerProtocol.LegacyCheckpointFileName
             }
        .SelectMany(pattern => Directory.EnumerateFiles(
            resultsDirectory,
            pattern,
            SearchOption.AllDirectories))
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (CombatFoundationCheckpointCatalogStore.TryReadResumeCandidate(
                candidate,
                out _,
                out _,
                out _))
        {
            return candidate;
        }
    }
    return "";
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
    var legacyPath = CombatFoundationCheckpointCatalogStore
        .LegacyCheckpointPath(requestedPath);
    var candidates = CombatFoundationCheckpointCatalogStore.ResumeCandidates(
        requestedPath,
        explicitlySelected: !string.IsNullOrWhiteSpace(
            job.ResumeCheckpointPath));
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
            if (checkpoint == null)
            {
                throw new InvalidDataException(
                    "checkpoint protocol is incompatible");
            }
            var previousMigration = checkpoint.SchemaVersion
                                    == CombatFoundationWorkerProtocol
                                        .PreviousSchemaVersion;
            var repairMigration = checkpoint.SchemaVersion
                                  == CombatFoundationWorkerProtocol
                                      .RepairMigratableSchemaVersion;
            if (!previousMigration
                && !repairMigration
                && checkpoint.SchemaVersion
                != CombatFoundationWorkerProtocol.SchemaVersion)
            {
                throw new InvalidDataException(
                    "checkpoint protocol is incompatible");
            }
            if (!CombatFoundationCheckpointCatalogStore
                    .TryValidateSelectedImmutableCheckpoint(
                        job.CheckpointCatalogPath,
                        checkpointPath,
                        checkpoint,
                        out var artifactDiagnostic))
            {
                throw new InvalidDataException(artifactDiagnostic);
            }
            if (!(repairMigration
                    ? RepairMigrationIdentityCompatible(
                        job,
                        checkpoint,
                        rulesetHash)
                    : previousMigration
                        ? PreviousMigrationIdentityCompatible(
                            job,
                            checkpoint,
                            rulesetHash)
                    : CheckpointIdentityCompatible(
                        job,
                        checkpoint,
                        requestFingerprint,
                        rulesetHash)))
            {
                CombatFoundationRequestIdentity.Matches(
                    job,
                    checkpoint,
                    rulesetHash,
                    out var identityDiagnostic);
                throw new InvalidDataException(
                    "checkpoint identity does not match this job"
                    + "; checkpointFingerprint="
                    + checkpoint.RequestFingerprint
                    + ", requestFingerprint="
                    + requestFingerprint
                    + ", checkpointRuleset="
                    + checkpoint.RulesetHash
                    + ", requestRuleset="
                    + rulesetHash
                    + ", differences="
                    + identityDiagnostic
                    + ", supervisorContinuation="
                    + SupervisorContinuationDiagnostic(
                        job,
                        checkpoint,
                        rulesetHash));
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
            var migratedEpisodes = 0;
            if (previousMigration || repairMigration)
            {
                if (repairMigration)
                {
                    foreach (var episode in episodes)
                    {
                        if (CombatPolicyValueEpisodeMigration.UpgradeInPlace(episode))
                        {
                            migratedEpisodes++;
                        }
                        else if (episode.ModelProtocol
                                 != CombatPolicyValueProtocol.EpisodeProtocol
                                 || episode.FeatureSchemaVersion
                                 != CombatPolicyValueProtocol.FeatureSchemaVersion)
                        {
                            throw new InvalidDataException(
                                "repair migration encountered an unsupported replay episode");
                        }
                    }
                }
                checkpoint.SchemaVersion =
                    CombatFoundationWorkerProtocol.SchemaVersion;
                checkpoint.RequestFingerprint = requestFingerprint;
                checkpoint.Resume.SchemaVersion =
                    CombatFoundationWorkerProtocol.SchemaVersion;
                checkpoint.Resume.Compatibility =
                    CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
                        job.Request,
                        rulesetHash);
                if (repairMigration)
                {
                    checkpoint.Resume.BestPendingArenaCandidate = null;
                    checkpoint.Resume.AbsoluteQualifiedBestModel = null;
                    checkpoint.Resume.AbsoluteQualifiedBestEvidence = null;
                    foreach (var iteration in checkpoint.Resume.Iterations
                                 ?? new List<CombatCampaignFoundationIteration>())
                    {
                        iteration.AbsoluteQualificationGatePassed = false;
                        iteration.QualifiedCandidateSelected = false;
                    }
                    // ReplayIdentity intentionally describes the stable episode
                    // population rather than its storage protocol, so v6 and v7
                    // payloads can share it. Force the next checkpoint commit to
                    // materialize the migrated frames instead of reusing the
                    // immutable v6 snapshot under a v15 outer checkpoint.
                    snapshot.SourceReplayIdentity =
                        "repair-migration-v23-rewrite-required:"
                        + (string.IsNullOrWhiteSpace(snapshot.SourceReplayIdentity)
                            ? snapshot.ReplayIdentity
                            : snapshot.SourceReplayIdentity);
                }
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
            diagnostic = repairMigration
                ? "已将 v22/v6 检查点修复迁移到 v23/v7；重建 "
                  + migratedEpisodes
                  + " 个 Episode 的决策转移与策略适用标签，旧合格证据已隔离"
                : previousMigration
                    ? "已将 v14 检查点无损迁移到 v15，并启用历史待验证候选槽"
                : !string.IsNullOrWhiteSpace(job.ResumeCheckpointPath)
                ? "已加载所选不可变检查点 " + Path.GetFileName(checkpointPath)
                : checkpointPath.Equals(
                    legacyPath,
                    StringComparison.OrdinalIgnoreCase)
                  || checkpointPath.Equals(
                      CombatFoundationCheckpointStorage.BackupPath(legacyPath),
                      StringComparison.OrdinalIgnoreCase)
                    ? "已从 v11 活动检查点兼容恢复；下一次提交将升级为 v12"
                : checkpointPath.Equals(
                    job.CheckpointPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : "已从检查点备份恢复";
            if (!string.Equals(
                    checkpoint.RequestFingerprint,
                    requestFingerprint,
                    StringComparison.Ordinal)
                && SupervisorContinuationIdentityCompatible(
                    job,
                    checkpoint,
                    rulesetHash))
            {
                diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                    ? "已按轮次监督器交接指纹恢复检查点"
                    : diagnostic + "；已验证轮次监督器交接指纹";
            }
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
    var exactRequestIdentity = CombatFoundationRequestIdentity.Matches(
        job,
        checkpoint,
        rulesetHash,
        out _);
    if (exactRequestIdentity)
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
    if (SupervisorContinuationIdentityCompatible(
            job,
            checkpoint,
            rulesetHash))
    {
        return true;
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
                    ?? checkpoint.Resume?.LatestTrainingModel
                    ?? checkpoint.Resume?.WorkingChampion
                    ?? checkpoint.Resume?.Champion;
    return CombatCampaignFoundationTrainer.ManifestCompatible(
               checkpoint.Resume?.Compatibility,
               current)
           && ModelArchitectureCompatible(candidate, job.Request.Training);
}

static bool RepairMigrationIdentityCompatible(
    CombatFoundationWorkerJob job,
    CombatFoundationWorkerCheckpoint checkpoint,
    string rulesetHash)
{
    var resume = checkpoint.Resume;
    var legacy = resume?.Compatibility;
    if (resume == null
        || legacy == null
        || resume.SchemaVersion
        != CombatFoundationWorkerProtocol.RepairMigratableSchemaVersion
        || legacy.SchemaVersion
        != CombatFoundationWorkerProtocol.RepairMigratableSchemaVersion
        || legacy.FeatureSchemaVersion
        != CombatPolicyValueProtocol.FeatureSchemaVersion
        || !string.Equals(
            legacy.TrainingSemanticsVersion,
            "content-set-object-transition-multistrategy-replay-qualified-best-v22",
            StringComparison.Ordinal))
    {
        return false;
    }
    var current = CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
        job.Request,
        rulesetHash);
    return string.Equals(legacy.RulesetHash, current.RulesetHash, StringComparison.Ordinal)
           && string.Equals(
               legacy.ContentSetHash,
               current.ContentSetHash,
               StringComparison.Ordinal)
           && string.Equals(
               legacy.OwnerModSetHash,
               current.OwnerModSetHash,
               StringComparison.Ordinal)
           && string.Equals(
               legacy.NativeProgramPackageHash,
               current.NativeProgramPackageHash,
               StringComparison.Ordinal)
           && string.Equals(
               legacy.CampaignId,
               current.CampaignId,
               StringComparison.Ordinal)
           && string.Equals(
               legacy.CampaignVersion,
               current.CampaignVersion,
               StringComparison.Ordinal)
           && string.Equals(
               legacy.TrainingCampaignHash,
               current.TrainingCampaignHash,
               StringComparison.Ordinal)
           && string.Equals(
               legacy.ValidationCampaignHash,
               current.ValidationCampaignHash,
               StringComparison.Ordinal)
           && string.Equals(
               legacy.FeatureEncodingMode,
               current.FeatureEncodingMode,
               StringComparison.Ordinal)
           && legacy.StateDimensions == current.StateDimensions
           && legacy.ActionDimensions == current.ActionDimensions
           && legacy.HiddenDimensions == current.HiddenDimensions;
}

static bool PreviousMigrationIdentityCompatible(
    CombatFoundationWorkerJob job,
    CombatFoundationWorkerCheckpoint checkpoint,
    string rulesetHash)
{
    var resume = checkpoint.Resume;
    var previous = resume?.Compatibility;
    if (resume == null
        || previous == null
        || resume.SchemaVersion
        != CombatFoundationWorkerProtocol.PreviousSchemaVersion
        || previous.SchemaVersion
        != CombatFoundationWorkerProtocol.PreviousSchemaVersion)
    {
        return false;
    }
    var current = CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
        job.Request,
        rulesetHash);
    var previousSchema = previous.SchemaVersion;
    try
    {
        previous.SchemaVersion = current.SchemaVersion;
        var candidate = resume.ModelTraining?.BestModel
                        ?? resume.ModelTraining?.Model
                        ?? resume.LatestTrainingModel
                        ?? resume.WorkingChampion
                        ?? resume.Champion;
        return CombatCampaignFoundationTrainer.ManifestCompatible(
                   previous,
                   current)
               && ModelArchitectureCompatible(
                   candidate,
                   job.Request.Training);
    }
    finally
    {
        previous.SchemaVersion = previousSchema;
    }
}

static bool SupervisorManifestCompatible(
    CombatFoundationCompatibilityManifest? checkpoint,
    CombatFoundationCompatibilityManifest current)
{
    return checkpoint != null
           && checkpoint.SchemaVersion == current.SchemaVersion
           && checkpoint.FeatureSchemaVersion == current.FeatureSchemaVersion
           && checkpoint.StateDimensions == current.StateDimensions
           && checkpoint.ActionDimensions == current.ActionDimensions
           && checkpoint.HiddenDimensions == current.HiddenDimensions
           && string.Equals(
               checkpoint.RulesetHash,
               current.RulesetHash,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.ContentSetHash,
               current.ContentSetHash,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.OwnerModSetHash,
               current.OwnerModSetHash,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.ActionContractVersion,
               current.ActionContractVersion,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.SemanticGateVersion,
               current.SemanticGateVersion,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.IntegritySeedCorpusVersion,
               current.IntegritySeedCorpusVersion,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.NativeProgramPackageHash,
               current.NativeProgramPackageHash,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.CampaignId,
               current.CampaignId,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.CampaignVersion,
               current.CampaignVersion,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.FeatureEncodingMode,
               current.FeatureEncodingMode,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.SearchPolicyVersion,
               current.SearchPolicyVersion,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.CurriculumVersion,
               current.CurriculumVersion,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.TrainingPolicyVersion,
               current.TrainingPolicyVersion,
               StringComparison.Ordinal)
           && string.Equals(
               checkpoint.TrainingSemanticsVersion,
               current.TrainingSemanticsVersion,
               StringComparison.Ordinal);
}

static bool SupervisorContinuationIdentityCompatible(
    CombatFoundationWorkerJob job,
    CombatFoundationWorkerCheckpoint checkpoint,
    string rulesetHash)
{
    if (!job.ResumeFromCheckpoint
        || !job.RequireCompatibleResume
        || !string.Equals(
            job.RequestedStartMode,
            "iteration-boundary-resume",
            StringComparison.Ordinal)
        || !string.Equals(
            job.ResumeMode,
            CombatFoundationCheckpointResumeModes.Exact,
            StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(job.RequiredCheckpointFingerprint)
        || !string.Equals(
            checkpoint.RequestFingerprint,
            job.RequiredCheckpointFingerprint,
            StringComparison.Ordinal)
        || !string.Equals(
            checkpoint.Resume?.Stage,
            "iteration-complete",
            StringComparison.Ordinal))
    {
        return false;
    }

    var current = CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
        job.Request,
        rulesetHash);
    var candidate = checkpoint.Resume?.LatestTrainingModel
                    ?? checkpoint.Resume?.WorkingChampion
                    ?? checkpoint.Resume?.Champion;
    return SupervisorManifestCompatible(
               checkpoint.Resume?.Compatibility,
               current)
           && ModelArchitectureCompatible(candidate, job.Request.Training);
}

static string SupervisorContinuationDiagnostic(
    CombatFoundationWorkerJob job,
    CombatFoundationWorkerCheckpoint checkpoint,
    string rulesetHash)
{
    if (!string.Equals(
            job.RequestedStartMode,
            "iteration-boundary-resume",
            StringComparison.Ordinal))
    {
        return "not-requested";
    }
    var current = CombatCampaignFoundationTrainer.BuildCompatibilityManifest(
        job.Request,
        rulesetHash);
    var stored = checkpoint.Resume?.Compatibility;
    var candidate = checkpoint.Resume?.LatestTrainingModel
                    ?? checkpoint.Resume?.WorkingChampion
                    ?? checkpoint.Resume?.Champion;
    return "requiredFingerprint="
           + job.RequiredCheckpointFingerprint
           + ", stage="
           + checkpoint.Resume?.Stage
           + ", manifestCompatible="
           + CombatCampaignFoundationTrainer.ManifestCompatible(stored, current)
           + ", supervisorManifestCompatible="
           + SupervisorManifestCompatible(stored, current)
           + ", architectureCompatible="
           + ModelArchitectureCompatible(candidate, job.Request.Training)
           + ", trainingCampaign="
           + stored?.TrainingCampaignHash
           + "/"
           + current.TrainingCampaignHash
           + ", validationCampaign="
           + stored?.ValidationCampaignHash
           + "/"
           + current.ValidationCampaignHash
           + ", nativePackage="
           + stored?.NativeProgramPackageHash
           + "/"
           + current.NativeProgramPackageHash;
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
                ?? source.LatestTrainingModel
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
    source.LatestTrainingModel = model;
    source.WorkingChampion = model;
    source.Champion = model;
    source.BestPendingArenaCandidate = null;
    if (!string.Equals(
            source.AbsoluteQualifiedBestModel?.ModelId,
            model.ModelId,
            StringComparison.Ordinal))
    {
        source.AbsoluteQualifiedBestModel = null;
        source.AbsoluteQualifiedBestEvidence = null;
    }
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
            var latest = training?.LatestTrainingModel ?? working;
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
                LatestTrainingModel = latest,
                BestPendingArenaCandidate =
                    training.BestPendingArenaCandidate,
                AbsoluteQualifiedBestModel =
                    training.AbsoluteQualifiedBestModel,
                AbsoluteQualifiedBestEvidence =
                    training.AbsoluteQualifiedBestEvidence,
                Replay = episodes,
                Iterations = new List<CombatCampaignFoundationIteration>(
                    training.Iterations),
                Preflight = training.Preflight,
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
        CombatPolicyValueEpisodeMigration.UpgradeInPlace(episode);
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
        LatestTrainingModel = source.LatestTrainingModel,
        BestPendingArenaCandidate = source.BestPendingArenaCandidate,
        AbsoluteQualifiedBestModel = source.AbsoluteQualifiedBestModel,
        AbsoluteQualifiedBestEvidence = source.AbsoluteQualifiedBestEvidence,
        Iterations = new List<CombatCampaignFoundationIteration>(
            source.Iterations),
        Preflight = source.Preflight,
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

static IReadOnlyList<string> WriteCheckpointCatalogEntry(
    CombatFoundationWorkerJob job,
    CombatFoundationWorkerCheckpoint checkpoint,
    string requestFingerprint,
    string rulesetHash,
    CombatFoundationCheckpointCatalogReadResult catalogRead)
{
    var state = checkpoint.Resume;
    var modelTraining = state.ModelTraining;
    var iteration = string.Equals(
            state.Stage,
            "iteration-complete",
            StringComparison.Ordinal)
        ? state.Iterations?.LastOrDefault(item =>
              item.Iteration == state.NextIteration)
          ?? state.Iterations?.LastOrDefault()
        : null;
    var completedModelEpochs = modelTraining?.CompletedEpochs ?? 0;
    var bestModelEpoch = (modelTraining?.BestValidationEpoch ?? 0) > 0
        ? modelTraining!.BestValidationEpoch
        : modelTraining?.BestEpoch ?? 0;
    CombatFoundationCheckpointCatalogStore.EnsureWritableBaseline(catalogRead);
    var catalog = catalogRead.Catalog
                  ?? new CombatFoundationCheckpointCatalog();
    var bestModelAlreadyCataloged = catalog.Entries.Any(item =>
        string.Equals(
            item.Stage,
            "model-training",
            StringComparison.Ordinal)
        && item.NextIteration == state.NextIteration
        && (item.BestValidationEpoch > 0
                ? item.BestValidationEpoch
                : item.BestEpoch)
           == bestModelEpoch);
    var shouldCatalog = string.Equals(
                            state.Stage,
                            "iteration-complete",
                            StringComparison.Ordinal)
                        || string.Equals(
                            state.Stage,
                            "model-training",
                            StringComparison.Ordinal)
                        && completedModelEpochs > 0
                        && bestModelEpoch > 0
                        && !bestModelAlreadyCataloged;
    if (!shouldCatalog)
    {
        if (catalogRead.Catalog == null
            || catalogRead.RecoveryUncertain && catalogRead.CanRewriteSafely)
        {
            CombatFoundationCheckpointCatalogStore.PrepareForWrite(
                catalog,
                job.CheckpointCatalogPath);
            CombatFoundationCheckpointCatalogStore.WriteCatalogAtomic(
                job.CheckpointCatalogPath,
                Serialize(catalog),
                catalogRead);
        }
        var existingRetention = CombatFoundationCheckpointCatalogStore
            .ReadArtifactRetention(job.CheckpointCatalogPath);
        if (existingRetention.ValidGenerationCount > 0)
        {
            return existingRetention.SnapshotPaths;
        }
        if (catalog.Entries.Count > 0)
        {
            throw new InvalidDataException(
                "Checkpoint catalog retention generations are unavailable; cleanup was refused.");
        }
        return Array.Empty<string>();
    }
    var bestValidationEpoch = iteration == null
        ? modelTraining?.BestValidationEpoch > 0
            ? modelTraining.BestValidationEpoch
            : modelTraining?.BestEpoch ?? 0
        : iteration.ModelEpochHistory
            .Where(item => !item.Calibrated)
            .OrderBy(item => item.Validation?.CompositeLoss
                             ?? double.MaxValue)
            .ThenBy(item => item.Epoch)
            .Select(item => item.Epoch)
            .FirstOrDefault();
    var deploymentSelectedEpoch = iteration?.TuningSelectedEpoch
                                  ?? modelTraining?.DeploymentSelectedEpoch
                                  ?? 0;
    var epochMetrics = modelTraining?.EpochHistory?
        .FirstOrDefault(item => item.Epoch == bestValidationEpoch
                                && !item.Calibrated)
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
                ?? state.LatestTrainingModel
                ?? state.WorkingChampion
                ?? state.Champion;
    var identity = HashCompact(new
    {
        requestFingerprint,
        state.Stage,
        state.NextIteration,
        Epoch = iteration?.TuningSelectedEpoch
                ?? modelTraining?.CompletedEpochs
                ?? 0,
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
        "foundation-checkpoint-"
        + id
        + ".json.gz");
    if (!CombatFoundationPathRuntime.FileExists(immutablePath))
    {
        CombatFoundationCheckpointStorage.CopyAtomicFile(
            job.CheckpointPath,
            immutablePath,
            retainBackup: false);
    }
    var risk = CombatFoundationCheckpointCatalogProtocol.Risk(
        trainingMetrics,
        validationMetrics,
        testMetrics,
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
        CompletedEpochs = iteration?.ModelEpochHistory.Count(item =>
                              !item.Calibrated)
                          ?? modelTraining?.CompletedEpochs
                          ?? 0,
        BestEpoch = deploymentSelectedEpoch > 0
            ? deploymentSelectedEpoch
            : bestValidationEpoch,
        BestValidationEpoch = bestValidationEpoch,
        DeploymentSelectedEpoch = deploymentSelectedEpoch,
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
    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        catalog,
        job.CheckpointCatalogPath,
        entry.Id);
    CombatFoundationCheckpointCatalogStore.WriteCatalogAtomic(
        job.CheckpointCatalogPath,
        Serialize(catalog),
        catalogRead);
    var retention = CombatFoundationCheckpointCatalogStore
        .ReadArtifactRetention(job.CheckpointCatalogPath);
    if (retention.ValidGenerationCount == 0)
    {
        throw new InvalidDataException(
            "Committed checkpoint catalog could not be reread; cleanup was refused.");
    }
    CombatFoundationCheckpointCatalogStore.ExecuteCleanupIfCertain(
        catalogRead,
        () => CombatFoundationCheckpointStorage.CleanupImmutableFiles(
            immutableDirectory,
            "foundation-checkpoint-*",
            retention.CheckpointPaths));
    return retention.SnapshotPaths;
}

static void PrepareCheckpointStoragePaths(CombatFoundationWorkerJob job)
{
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
}

static void ResetActiveCheckpoint(CombatFoundationWorkerJob job)
{
    CombatFoundationCheckpointCatalogStore.ResetCheckpointArtifacts(job);
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
    ISet<string> incrementallyDuplicateCases,
    ISet<string> capacityRejectedObservationIds,
    ISet<string> capacityRejectedCaseIds,
    IReadOnlyList<string> incrementalArchiveErrors,
    int capacityRejectedObservations,
    int capacityRejectedCases,
    long incrementalExpertReferenceBytes,
    long incrementalDeduplicatedExpertBytes)
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
        if (capacityRejectedObservationIds.Contains(observation.CaseId))
        {
            continue;
        }
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
    var observations = currentObservations
        .Where(item => incrementallyArchivedCases.Contains(item.CaseId)
                       || incrementallyDuplicateCases.Contains(item.CaseId))
        .ToList();
    var archived = incrementallyArchivedCases.Count;
    var duplicates = incrementallyDuplicateCases.Count;
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
    training.ExpertReferenceBytes += incrementalExpertReferenceBytes;
    training.DeduplicatedExpertBytes += incrementalDeduplicatedExpertBytes;
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

static int RunIterationSupervisor(
    CombatFoundationWorkerJob sourceJob)
{
    CombatFoundationPathRuntime.CreateDirectory(sourceJob.ResultDirectory);
    var finalResultPath = sourceJob.ResultPath;
    if (string.IsNullOrWhiteSpace(finalResultPath))
    {
        throw new InvalidOperationException(
            "轮次进程隔离需要有效的最终结果路径。");
    }

    var supervisorDirectory = Path.Combine(
        sourceJob.ResultDirectory,
        ".iteration-processes");
    CombatFoundationPathRuntime.CreateDirectory(supervisorDirectory);
    var segmentJobPath = Path.Combine(
        supervisorDirectory,
        sourceJob.JobId + ".job.json");
    var segmentResultPath = Path.Combine(
        supervisorDirectory,
        sourceJob.JobId + ".result.json");
    var segment = 0;
    var resolvedIterationLimit = 0;
    CombatFoundationSegmentResourceObservation? previousResources = null;
    sourceJob.ResumeProvenance ??= new CombatFoundationResumeProvenance();
    if (!sourceJob.ResumeProvenance.ExternalRequestCaptured)
    {
        sourceJob.ResumeProvenance.ExternalRequestCaptured = true;
        sourceJob.ResumeProvenance.ExternalResumeRequested =
            sourceJob.ResumeFromCheckpoint;
        sourceJob.ResumeProvenance.ExternalRequestedStartMode =
            sourceJob.RequestedStartMode;
    }
    var requiredCheckpointFingerprint =
        sourceJob.ResumeFromCheckpoint
        && sourceJob.RequireCompatibleResume
        && string.IsNullOrWhiteSpace(sourceJob.ResumeCheckpointPath)
        && string.Equals(
            CombatFoundationCheckpointResumeModes.Normalize(
                sourceJob.ResumeMode),
            CombatFoundationCheckpointResumeModes.Exact,
            StringComparison.Ordinal)
        && string.Equals(
            sourceJob.RequestedStartMode,
            "resume-required",
            StringComparison.Ordinal)
        && !CombatFoundationCheckpointCatalogStore.HasPendingReset(sourceJob)
        && CombatFoundationPathRuntime.FileExists(sourceJob.CheckpointPath)
            ? ReadCheckpointRequestFingerprint(sourceJob.CheckpointPath)
            : "";
    try
    {
        while (true)
        {
            segment++;
            if (segment > 10_000)
            {
                throw new InvalidOperationException(
                    "轮次进程隔离超过安全的进程重启次数。");
            }

            var childJob = Deserialize<CombatFoundationWorkerJob>(
                               Serialize(sourceJob))
                           ?? throw new InvalidOperationException(
                               "无法创建轮次子进程任务。");
            childJob.ResumeProvenance.InternalSegmentNumber = segment;
            childJob.ResumeProvenance.InternalResumeRequested = segment > 1;
            childJob.ResumeProvenance.InternalResumeApplied = false;
            childJob.ResumeProvenance.InternalResumeDiagnostic = "";
            childJob.ResumeProvenance.InternalEffectiveStartMode = "";
            childJob.ResultPath = segmentResultPath;
            childJob.RequiredCheckpointFingerprint = "";
            childJob.Request.EnableIterationProcessIsolation = true;
            var segmentResources =
                CombatFoundationResourceSnapshot.Capture();
            var configuredIterationsPerProcess =
                sourceJob.Request.MaximumIterationsPerProcess > 0
                    ? sourceJob.Request.MaximumIterationsPerProcess
                    : 3;
            var memoryDecision = CombatFoundationMemoryExecutionPolicy
                .SelectAdaptive(
                    configuredIterationsPerProcess,
                    sourceJob.Request.ModelTrainingParallelism,
                    segmentResources,
                    previousResources);
            childJob.Request.MaximumIterationsPerProcess =
                memoryDecision.IterationsPerProcess;
            childJob.Request.ModelTrainingParallelism =
                memoryDecision.ModelTrainingParallelism;
            Console.WriteLine(
                "自适应隔离计划：模式="
                + memoryDecision.Mode
                + "，每进程迭代="
                + childJob.Request.MaximumIterationsPerProcess
                + "，模型训练并行="
                + childJob.Request.ModelTrainingParallelism
                + "，物理内存="
                + segmentResources.TotalPhysicalMemoryBytes
                + "，当前可用="
                + segmentResources.AvailablePhysicalMemoryBytes
                + "，观测进程树峰值="
                + memoryDecision.ObservedProcessTreePeakBytes
                + "，原因="
                + memoryDecision.Reason);
            if (segment > 1)
            {
                childJob.ResumeFromCheckpoint = true;
                childJob.RequireCompatibleResume = true;
                childJob.ResetCheckpointOnFreshStart = false;
                childJob.ResumeCheckpointPath = "";
                childJob.ResumeMode =
                    CombatFoundationCheckpointResumeModes.Exact;
                childJob.Request.AdditionalIterationsOnResume = 0;
                // Preserve the explicit request across process boundaries.
                // When reuse is enabled, the first segment's signed cache is
                // shared; when disabled, no segment may silently opt back in.
                childJob.Request.ReuseAutoTuneCache =
                    sourceJob.Request.ReuseAutoTuneCache;
                childJob.Request.PreflightCampaignsPerDifficulty = 0;
                childJob.Request.Iterations = resolvedIterationLimit;
                childJob.InitialChampion = null;
                childJob.RequestedStartMode = "iteration-boundary-resume";
                childJob.RequiredCheckpointFingerprint =
                    requiredCheckpointFingerprint;
            }
            else if (!string.IsNullOrWhiteSpace(
                         requiredCheckpointFingerprint))
            {
                childJob.RequestedStartMode = "iteration-boundary-resume";
                childJob.RequiredCheckpointFingerprint =
                    requiredCheckpointFingerprint;
            }

            CombatFoundationPathRuntime.DeleteFile(segmentResultPath);
            CombatFoundationCheckpointStorage.WriteAtomicText(
                segmentJobPath,
                Serialize(childJob),
                retainBackup: false);
            var startInfo = CreateRoundChildStartInfo(segmentJobPath);
            using var child = Process.Start(startInfo)
                              ?? throw new InvalidOperationException(
                                  "无法启动轮次训练子进程。");
            var childPeakWorkingSetBytes = 0L;
            while (!child.WaitForExit(500))
            {
                try
                {
                    child.Refresh();
                    childPeakWorkingSetBytes = Math.Max(
                        childPeakWorkingSetBytes,
                        child.WorkingSet64);
                }
                catch (InvalidOperationException)
                {
                    // The child can exit between WaitForExit and Refresh.
                }
            }
            try
            {
                child.Refresh();
                childPeakWorkingSetBytes = Math.Max(
                    childPeakWorkingSetBytes,
                    child.PeakWorkingSet64);
            }
            catch (InvalidOperationException)
            {
                // Exit status and the segment result remain authoritative.
            }
            if (!CombatFoundationPathRuntime.FileExists(segmentResultPath))
            {
                throw new InvalidOperationException(
                    "轮次训练子进程未生成结果，退出码="
                    + child.ExitCode);
            }

            var status = Deserialize<IterationSegmentStatus>(
                             CombatFoundationCheckpointStorage
                                 .ReadAllTextShared(segmentResultPath))
                         ?? throw new InvalidOperationException(
                             "无法读取轮次训练子进程结果。");
            if (status.ResumeProvenance != null)
            {
                sourceJob.ResumeProvenance = status.ResumeProvenance;
            }
            var continueAtBoundary = child.ExitCode == 0
                                     && string.Equals(
                                         status.CompletionKind,
                                         "iteration-boundary",
                                         StringComparison.Ordinal)
                                     && status.Training
                                         ?.ContinuationRequired == true;
            if (!continueAtBoundary)
            {
                CopyAtomic(segmentResultPath, finalResultPath);
                return child.ExitCode;
            }

            previousResources = new CombatFoundationSegmentResourceObservation
            {
                IterationsPerProcess =
                    childJob.Request.MaximumIterationsPerProcess,
                ModelTrainingParallelism =
                    childJob.Request.ModelTrainingParallelism,
                WorkerPeakWorkingSetBytes = childPeakWorkingSetBytes,
                EndPrivateMemoryBytes = Math.Max(
                    0L,
                    status.Training?.PrivateMemoryBytes ?? 0L),
                TransformerPeakWorkingSetBytes = Math.Max(
                    0L,
                    status.Training?.TransformerTeacherPeakWorkingSetBytes
                    ?? 0L),
                GcHeapSizeBytes = Math.Max(
                    0L,
                    status.Training?.GcHeapSizeBytes ?? 0L),
                GcFragmentedBytes = Math.Max(
                    0L,
                    status.Training?.GcFragmentedBytes ?? 0L),
                ResourceFailure = child.ExitCode != 0
            };

            var nextIteration = status.Training?.NextIteration ?? 0;
            resolvedIterationLimit = status.Training
                                         ?.ResolvedIterationLimit
                                     ?? 0;
            if (nextIteration <= 0
                || resolvedIterationLimit <= nextIteration)
            {
                throw new InvalidOperationException(
                    "轮次子进程请求继续，但没有提供有效的下一轮边界。"
                    + " next=" + nextIteration
                    + ", limit=" + resolvedIterationLimit);
            }
            if (CombatFoundationCheckpointCatalogStore.HasPendingReset(
                    sourceJob))
            {
                throw new InvalidOperationException(
                    "Checkpoint reset remained pending after an isolated iteration.");
            }
            requiredCheckpointFingerprint =
                ReadCheckpointRequestFingerprint(sourceJob.CheckpointPath);
        }
    }
    finally
    {
        CombatFoundationPathRuntime.DeleteFile(segmentJobPath);
        CombatFoundationPathRuntime.DeleteFile(segmentResultPath);
        CombatFoundationPathRuntime.DeleteFile(
            CombatFoundationCheckpointStorage.BackupPath(segmentJobPath));
        CombatFoundationPathRuntime.DeleteFile(
            CombatFoundationCheckpointStorage.BackupPath(segmentResultPath));
    }
}

static string ReadCheckpointRequestFingerprint(string checkpointPath)
{
    using var stream = new FileStream(
        CombatFoundationPathRuntime.ForFileSystem(checkpointPath),
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        64 * 1024,
        FileOptions.SequentialScan);
    var first = stream.ReadByte();
    var second = stream.ReadByte();
    stream.Position = 0L;
    using var gzip = first == 0x1f && second == 0x8b
        ? new GZipStream(
            stream,
            CompressionMode.Decompress,
            leaveOpen: true)
        : null;
    Stream payload = gzip == null ? stream : gzip;
    using var textReader = new StreamReader(
        payload,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true,
        64 * 1024,
        leaveOpen: true);
    using var jsonReader = new JsonTextReader(textReader);
    while (jsonReader.Read())
    {
        if (jsonReader.TokenType != JsonToken.PropertyName
            || !string.Equals(
                Convert.ToString(jsonReader.Value),
                nameof(CombatFoundationWorkerCheckpoint.RequestFingerprint),
                StringComparison.Ordinal)
            || !jsonReader.Read())
        {
            continue;
        }
        var fingerprint = Convert.ToString(jsonReader.Value) ?? "";
        if (!string.IsNullOrWhiteSpace(fingerprint))
        {
            return fingerprint;
        }
        break;
    }
    throw new InvalidDataException(
        "轮次断点缺少请求指纹：" + checkpointPath);
}

static ProcessStartInfo CreateRoundChildStartInfo(string childJobPath)
{
    var executable = Environment.ProcessPath
                     ?? throw new InvalidOperationException(
                         "无法确定训练 Worker 可执行文件。");
    var startInfo = new ProcessStartInfo
    {
        FileName = executable,
        WorkingDirectory = AppContext.BaseDirectory,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    if (string.Equals(
            Path.GetFileNameWithoutExtension(executable),
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
    {
        var entryAssembly = Environment.GetCommandLineArgs()
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(entryAssembly))
        {
            throw new InvalidOperationException(
                "dotnet 托管启动模式缺少 Worker 程序集路径。");
        }
        startInfo.ArgumentList.Add(entryAssembly);
    }
    startInfo.ArgumentList.Add("--job");
    startInfo.ArgumentList.Add(childJobPath);
    startInfo.ArgumentList.Add("--round-child");
    return startInfo;
}

static void CopyAtomic(string sourcePath, string destinationPath)
{
    CombatFoundationCheckpointStorage.WriteAtomicStream(
        destinationPath,
        output =>
        {
            using var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                128 * 1024,
                FileOptions.SequentialScan);
            input.CopyTo(output, 128 * 1024);
        },
        retainBackup: false);
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

static string ResolveOptionValue(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.Ordinal))
        {
            return arguments[index + 1];
        }
    }
    return "";
}

static T? Deserialize<T>(string json)
{
    return JsonConvert.DeserializeObject<T>(
        json,
        new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
    });
}

static void LoadPinnedSeedHistory(CombatFoundationWorkerJob job)
{
    var path = Path.Combine(
        string.IsNullOrWhiteSpace(job.SuccessArchiveDirectory)
            ? job.ResultDirectory
            : job.SuccessArchiveDirectory,
        "foundation-seed-registry-v1.jsonl");
    if (!File.Exists(path))
    {
        return;
    }
    var pinned = new List<FoundationSeedTag>();
    foreach (var line in File.ReadLines(path))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        try
        {
            var tag = Deserialize<FoundationSeedTag>(line);
            if (tag != null
                && tag.WorldSeed > 0UL
                && tag.Tag.StartsWith(
                    "problem-",
                    StringComparison.OrdinalIgnoreCase))
            {
                pinned.Add(tag);
            }
        }
        catch (JsonException)
        {
            // Keep valid historical lines useful when one line is damaged.
        }
    }
    job.Request.PinnedSeedHistory = pinned
        .GroupBy(item => new
        {
            item.WorldSeed,
            Difficulty = string.Equals(
                item.DifficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase)
                ? "advanced"
                : "normal"
        })
        .Select(group => group
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Tag, StringComparer.Ordinal)
            .First())
        .OrderByDescending(item => item.Priority)
        .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
        .ThenBy(item => item.WorldSeed)
        .Take(256)
        .Select(item => new CombatFoundationHardSeedHistoryEntry
        {
            WorldSeed = item.WorldSeed,
            DifficultyId = string.Equals(
                item.DifficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase)
                ? "advanced"
                : "normal",
            FirstSeenIteration = 1,
            FailureOccurrences = 1,
            TerminalScenarioId = "pinned:" + item.Tag,
            SolvabilityClass = "unknown",
            Resolved = false
        })
        .ToList();
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

static void PersistDecisionDifferences(
    CombatFoundationWorkerJob job,
    CombatCampaignFoundationTrainingResult training)
{
    var cases = training.CapabilityProbe?.DecisionDifferences
                ?? new List<CombatFoundationDecisionDifferenceCase>();
    var path = Path.Combine(
        job.ResultDirectory,
        "foundation-decision-differences-v1.jsonl");
    WriteJsonLines(path, cases);
    training.DecisionDifferencePath = path;
    training.DecisionDifferenceCases = cases.Count;
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
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = WorkerCompactEpisodeContractResolver.Instance
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
            FloatFormatHandling = FloatFormatHandling.DefaultValue,
            ContractResolver = WorkerCompactEpisodeContractResolver.Instance
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
    out string episodesPath,
    out int episodeCount)
{
    episodesPath = "";
    episodeCount = 0;
    if (!CombatFoundationCheckpointCatalogStore.TrySelectResumeCandidate(
            job.CheckpointPath,
            explicitlySelected: false,
            out _,
            out var checkpoint,
            out var snapshot,
            out _)
        || checkpoint == null
        || snapshot == null)
    {
        return false;
    }
    episodesPath = snapshot.Path;
    episodeCount = Math.Max(
        0,
        snapshot.EpisodeCount >= 0
            ? snapshot.EpisodeCount
            : checkpoint.Resume?.Replay?.Count ?? 0);
    return true;
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

static void WriteAtomicJson(
    string path,
    object value,
    Formatting formatting = Formatting.Indented,
    bool retainBackup = false,
    bool compress = false)
{
    CombatFoundationCheckpointStorage.WriteAtomicStream(
        path,
        stream =>
        {
            GZipStream? gzip = null;
            try
            {
                Stream payload = stream;
                if (compress || path.EndsWith(
                        ".gz",
                        StringComparison.OrdinalIgnoreCase))
                {
                    gzip = new GZipStream(
                        stream,
                        CompressionLevel.Fastest,
                        leaveOpen: true);
                    payload = gzip;
                }
                using var textWriter = new StreamWriter(
                    payload,
                    new UTF8Encoding(false),
                    64 * 1024,
                    leaveOpen: true);
                using var jsonWriter = new JsonTextWriter(textWriter)
                {
                    CloseOutput = false,
                    Formatting = formatting
                };
                var serializer = JsonSerializer.Create(
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    });
                serializer.Serialize(jsonWriter, value);
                jsonWriter.Flush();
                textWriter.Flush();
            }
            finally
            {
                gzip?.Dispose();
            }
        },
        retainBackup);
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

internal sealed class IterationSegmentStatus
{
    public string CompletionKind { get; set; } = "";

    public CombatFoundationResumeProvenance? ResumeProvenance { get; set; }

    public IterationSegmentTrainingStatus? Training { get; set; }
}

internal sealed class IterationSegmentTrainingStatus
{
    public bool ContinuationRequired { get; set; }

    public int NextIteration { get; set; }

    public int ResolvedIterationLimit { get; set; }

    public long PrivateMemoryBytes { get; set; }

    public long GcHeapSizeBytes { get; set; }

    public long GcFragmentedBytes { get; set; }

    public long TransformerTeacherPeakWorkingSetBytes { get; set; }
}
