using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
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
    Directory.CreateDirectory(job.ResultDirectory);
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
    var workerAssemblyPath = Environment.ProcessPath;
    var workerSha256 =
        string.IsNullOrWhiteSpace(workerAssemblyPath)
        || !File.Exists(workerAssemblyPath)
            ? ""
            : Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(workerAssemblyPath)));
    job.Request.NativeProgramPackageHash =
        string.IsNullOrWhiteSpace(workerSha256)
            ? "worker:unknown"
            : workerSha256;

    var requestedWorkers = Math.Max(
        1,
        Math.Min(Environment.ProcessorCount, job.Request.MaximumDegreeOfParallelism));
    ThreadPool.GetMinThreads(out var minimumWorkers, out var minimumIo);
    ThreadPool.SetMinThreads(
        Math.Max(minimumWorkers, requestedWorkers + 2),
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
    }
    else if (job.ResumeFromCheckpoint)
    {
        Console.Error.WriteLine(
            "Foundation checkpoint was not resumed and has been preserved: "
            + resumeDiagnostic);
    }
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        job.CheckpointPath,
        job.CheckpointEpisodesPath,
        checkpointSnapshot == null
            ? Array.Empty<string>()
            : new[] { checkpointSnapshot.Path });
    PrepareCaseArchive(job, build.Ruleset.RulesetHash);

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
    var checkpointGate = new object();
    var checkpointReplayIdentity =
        checkpointSnapshot?.ReplayIdentity ?? "";
    if (string.IsNullOrWhiteSpace(checkpointReplayIdentity)
        && job.Request.Resume?.Replay != null)
    {
        checkpointReplayIdentity =
            ReplayIdentity(job.Request.Resume.Replay);
    }
    job.Request.Checkpoint = state =>
    {
        lock (checkpointGate)
        {
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
                            state.Replay.Select(SerializeCompact),
                            replayIdentity);
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
            }
            catch (Exception ex)
            {
                checkpointWriteFailures++;
                checkpointWarning =
                    "检查点暂时无法写入，训练继续使用上一份有效快照："
                    + ex.Message;
                Console.Error.WriteLine(checkpointWarning);
            }
        }
    };
    var incrementallyArchivedCases =
        new HashSet<string>(StringComparer.Ordinal);
    var incrementalArchiveErrors = new List<string>();
    if (job.Request.EnableSuccessCaseArchive)
    {
        job.Request.ObservationRecorded = observation =>
        {
            try
            {
                PersistObservation(job, observation);
            }
            catch (Exception ex)
            {
                incrementalArchiveErrors.Add(ex.ToString());
            }
        };
        job.Request.SuccessCaseRecorded = successCase =>
        {
            try
            {
                if (PersistSuccessCase(job, successCase))
                {
                    incrementallyArchivedCases.Add(
                        successCase.Observation.CaseId);
                }
            }
            catch (Exception ex)
            {
                incrementalArchiveErrors.Add(ex.ToString());
            }
        };
    }

    var training = new CombatCampaignFoundationTrainer(
        new CombatCampaignRunner(
            new CombatSimulationEngine(
                new AuraToolsNativeRewardExtensionFactory()))).Run(
        job.Request,
        build.Ruleset,
        job.InitialChampion,
        cancellation.Token);
    try
    {
        if (job.Request.EnableSuccessCaseArchive)
        {
            PersistSuccessCases(
                job,
                training,
                incrementallyArchivedCases,
                incrementalArchiveErrors);
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
    var episodesPath = Path.Combine(
        job.ResultDirectory,
        "foundation-training-episodes-v3.jsonl");
    training.GeneratedReplayEpisodes = Math.Max(
        training.GeneratedReplayEpisodes,
        training.Replay.Count);
    training.PersistedReplayEpisodes = training.Success
        ? training.Replay.Count
        : 0;
    WriteEpisodes(
        episodesPath,
        training.Success
            ? training.Replay
            : Array.Empty<CombatEpisode>());
    training.Replay.Clear();
    training.CampaignObservations.Clear();
    training.SuccessCases.Clear();
    if (job.Request.PreflightOnly || training.AcceptancePassed)
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
            workerSha256);
        workerResult.ModelPackagePath = Path.Combine(
            job.ResultDirectory,
            CombatFoundationModelPackageProtocol.FileName);
        WriteAtomic(workerResult.ModelPackagePath, Serialize(modelPackage));
    }
    WriteAtomic(job.ResultPath, Serialize(workerResult));
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
        WriteAtomic(
            job.ResultPath,
            Serialize(new CombatFoundationWorkerResult
            {
                JobId = job.JobId,
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
                CheckpointWriteFailures = checkpointWriteFailures,
                CheckpointWarning = checkpointWarning
            }));
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
        WriteAtomic(
            job.ResultPath,
            Serialize(new CombatFoundationWorkerResult
            {
                JobId = job.JobId,
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
                CheckpointWriteFailures = checkpointWriteFailures,
                CheckpointWarning = checkpointWarning
            }));
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
    var payload = rulesetHash
                  + "\n"
                  + CombatPolicyValueProtocol.FeatureSchemaVersion
                  + "\n"
                  + SerializeCompact(job.Request)
                  + "\n"
                  + (job.InitialChampion?.ModelId ?? "");
    return Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
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
            if (!string.Equals(
                    checkpoint.RequestFingerprint,
                    requestFingerprint,
                    StringComparison.Ordinal)
                || !string.Equals(
                    checkpoint.RulesetHash,
                    rulesetHash,
                    StringComparison.Ordinal))
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
                rulesetHash);
        if (!diagnostics.ArchiveExists)
        {
            diagnostics.Message = "archive root is absent";
            return;
        }

        var compactDirectory =
            CombatFoundationCaseArchiveProtocol.CompatibilityDirectory(
                archiveRoot,
                diagnostics.CompatibilityKey);
        var legacyDirectory =
            CombatFoundationCaseArchiveProtocol.LegacyCompatibilityDirectory(
                archiveRoot,
                diagnostics.CompatibilityKey);
        diagnostics.CompatibilityDirectoryExists =
            Directory.Exists(compactDirectory);
        diagnostics.LegacyCompatibilityDirectoryExists =
            Directory.Exists(legacyDirectory);
        var compactExpertDirectory = Path.Combine(
            compactDirectory,
            CombatFoundationCaseArchiveProtocol.ExpertDirectoryName);
        var legacyExpertDirectory = Path.Combine(
            legacyDirectory,
            "expert-cases");
        var compactObservationDirectory = Path.Combine(
            compactDirectory,
            CombatFoundationCaseArchiveProtocol.ObservationDirectoryName);
        var legacyObservationDirectory = Path.Combine(
            legacyDirectory,
            "observations");
        diagnostics.ExpertCasesDirectoryExists =
            Directory.Exists(compactExpertDirectory)
            || Directory.Exists(legacyExpertDirectory);
        diagnostics.ObservationsDirectoryExists =
            Directory.Exists(compactObservationDirectory)
            || Directory.Exists(legacyObservationDirectory);

        var compactCasePaths = EnumerateArchiveFiles(
            compactExpertDirectory,
            2048);
        var legacyCasePaths = EnumerateArchiveFiles(
            legacyExpertDirectory,
            2048);
        diagnostics.CompactExpertCaseFiles = compactCasePaths.Count;
        diagnostics.LegacyExpertCaseFiles = legacyCasePaths.Count;
        diagnostics.ExpertCaseFiles =
            compactCasePaths.Count + legacyCasePaths.Count;
        var cases = new Dictionary<
            string,
            CombatFoundationSuccessCase>(StringComparer.Ordinal);
        LoadSuccessCasePaths(
            job,
            compactCasePaths,
            diagnostics.CompatibilityKey,
            migrate: false,
            cases,
            diagnostics);
        LoadSuccessCasePaths(
            job,
            legacyCasePaths,
            diagnostics.CompatibilityKey,
            migrate: true,
            cases,
            diagnostics);
        diagnostics.DistinctLoadedCases = cases.Count;

        var compactObservationPaths = EnumerateArchiveFiles(
            compactObservationDirectory,
            8192);
        var legacyObservationPaths = EnumerateArchiveFiles(
            legacyObservationDirectory,
            8192);
        diagnostics.CompactObservationFiles =
            compactObservationPaths.Count;
        diagnostics.LegacyObservationFiles =
            legacyObservationPaths.Count;
        diagnostics.ObservationFiles =
            compactObservationPaths.Count + legacyObservationPaths.Count;
        var observations = new Dictionary<
            string,
            CombatFoundationCampaignObservation>(StringComparer.Ordinal);
        LoadObservationPaths(
            job,
            compactObservationPaths,
            diagnostics.CompatibilityKey,
            migrate: false,
            observations,
            diagnostics);
        LoadObservationPaths(
            job,
            legacyObservationPaths,
            diagnostics.CompatibilityKey,
            migrate: true,
            observations,
            diagnostics);
        diagnostics.DistinctLoadedObservations = observations.Count;

        var selection = CombatFoundationCaseLearning.SelectExpertReplay(
            cases.Values,
            job.Request.TrainingCampaign.CampaignId,
            job.Request.TrainingCampaign.CampaignVersion,
            rulesetHash,
            Math.Max(0, job.Request.ExpertReplayEpisodeLimit));
        job.Request.ExpertReplayEpisodes =
            new List<CombatEpisode>(selection.Episodes);
        selection.Episodes.Clear();
        job.Request.ExpertReplaySelection = selection;
        var residuals =
            CombatFoundationCaseLearning.TrainRewardResiduals(
                observations.Values);
        job.Request.RewardResidualTraining = residuals;
        ApplyRewardResiduals(job.Request.TrainingCampaign, residuals);
        ApplyRewardResiduals(job.Request.ValidationCampaign, residuals);
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
                  || diagnostics.LegacyCompatibilityDirectoryExists
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
            + ", migrated="
            + diagnostics.MigratedCases
            + "/"
            + diagnostics.MigratedObservations
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
            "*.json",
            SearchOption.TopDirectoryOnly)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .Take(Math.Max(0, limit))
        .ToList();
}

static void LoadSuccessCasePaths(
    CombatFoundationWorkerJob job,
    IEnumerable<string> paths,
    string compatibilityKey,
    bool migrate,
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
            var successCase = Deserialize<CombatFoundationSuccessCase>(
                File.ReadAllText(path));
            if (successCase?.Observation == null
                || successCase.SchemaVersion
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
            if (migrate)
            {
                var compactPath = ResolveSuccessCasePath(
                    job,
                    successCase,
                    CombatFoundationCaseArchiveProtocol.ExpertDirectoryName);
                var existed = File.Exists(compactPath);
                if (!existed)
                {
                    WriteAtomic(compactPath, Serialize(successCase));
                    diagnostics.MigratedCases++;
                }
            }
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
    CombatFoundationWorkerJob job,
    IEnumerable<string> paths,
    string compatibilityKey,
    bool migrate,
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
                    File.ReadAllText(path));
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
            if (migrate)
            {
                var compactPath = ResolveObservationPath(job, observation);
                if (!File.Exists(compactPath))
                {
                    WriteAtomic(compactPath, Serialize(observation));
                    diagnostics.MigratedObservations++;
                }
            }
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

static void PersistObservation(
    CombatFoundationWorkerJob job,
    CombatFoundationCampaignObservation observation)
{
    var path = ResolveObservationPath(job, observation);
    if (!File.Exists(path))
    {
        WriteAtomic(path, Serialize(observation));
    }
}

static bool PersistSuccessCase(
    CombatFoundationWorkerJob job,
    CombatFoundationSuccessCase successCase)
{
    var observation = successCase.Observation;
    var casePath = ResolveSuccessCasePath(
        job,
        successCase,
        CombatFoundationCaseArchiveProtocol.CaseDirectoryName);
    var added = !File.Exists(casePath);
    if (added)
    {
        WriteAtomic(casePath, Serialize(successCase));
    }
    if (successCase.Episodes.Count > 0)
    {
        var expertCasePath = ResolveSuccessCasePath(
            job,
            successCase,
            CombatFoundationCaseArchiveProtocol.ExpertDirectoryName);
        if (!File.Exists(expertCasePath))
        {
            WriteAtomic(expertCasePath, Serialize(successCase));
        }
    }
    return added;
}

static string ResolveObservationPath(
    CombatFoundationWorkerJob job,
    CombatFoundationCampaignObservation observation)
{
    var path = CombatFoundationCaseArchiveProtocol.EntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        CombatFoundationCaseArchiveProtocol.ObservationDirectoryName,
        observation.CaseId);
    if (!File.Exists(path)
        || ExistingObservationMatches(path, observation.CaseId))
    {
        return path;
    }
    return CombatFoundationCaseArchiveProtocol.EntryPath(
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
    var path = CombatFoundationCaseArchiveProtocol.EntryPath(
        SuccessArchiveRoot(job),
        observation.CompatibilityKey,
        directoryName,
        observation.CaseId);
    if (!File.Exists(path)
        || ExistingSuccessCaseMatches(path, observation.CaseId))
    {
        return path;
    }
    return CombatFoundationCaseArchiveProtocol.EntryPath(
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
                File.ReadAllText(path))?.CaseId,
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
                File.ReadAllText(path))?.Observation?.CaseId,
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
    IReadOnlyList<string> incrementalArchiveErrors)
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
        if (!File.Exists(observationPath))
        {
            WriteAtomic(observationPath, Serialize(observation));
        }
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
                     "*.json",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(item => item, StringComparer.Ordinal)
                 .Take(20_000))
        {
            var observation =
                Deserialize<CombatFoundationCampaignObservation>(
                    File.ReadAllText(path));
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
                 .Where(item => item?.Observation?.ArchiveEligible == true)
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
            WriteAtomic(casePath, Serialize(successCase));
            archived++;
        }
        if (successCase.Episodes.Count > 0)
        {
            var expertCasePath = ResolveSuccessCasePath(
                job,
                successCase,
                CombatFoundationCaseArchiveProtocol.ExpertDirectoryName);
            if (!File.Exists(expertCasePath))
            {
                WriteAtomic(expertCasePath, Serialize(successCase));
            }
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
