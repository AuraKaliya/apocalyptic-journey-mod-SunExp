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
try
{
    job = Deserialize<CombatFoundationWorkerJob>(File.ReadAllText(jobPath));
    if (job == null || job.SchemaVersion != 4)
    {
        throw new InvalidOperationException("Unsupported or empty foundation worker job.");
    }
    Directory.CreateDirectory(job.ResultDirectory);
    if (string.IsNullOrWhiteSpace(job.CheckpointPath))
    {
        job.CheckpointPath = Path.Combine(
            job.ResultDirectory,
            "foundation-training-checkpoint-v4.json");
    }
    if (string.IsNullOrWhiteSpace(job.CheckpointEpisodesPath))
    {
        job.CheckpointEpisodesPath = Path.Combine(
            job.ResultDirectory,
            "foundation-training-checkpoint-episodes-v4.jsonl");
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
    var workerAssemblyPath = Environment.ProcessPath;
    job.Request.NativeProgramPackageHash =
        string.IsNullOrWhiteSpace(workerAssemblyPath)
        || !File.Exists(workerAssemblyPath)
            ? "worker:unknown"
            : Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(workerAssemblyPath)));

    var requestedWorkers = Math.Max(
        1,
        Math.Min(Environment.ProcessorCount, job.Request.MaximumDegreeOfParallelism));
    ThreadPool.GetMinThreads(out var minimumWorkers, out var minimumIo);
    ThreadPool.SetMinThreads(
        Math.Max(minimumWorkers, requestedWorkers + 2),
        minimumIo);
    var requestFingerprint = Fingerprint(job, build.Ruleset.RulesetHash);
    var resume = new CombatCampaignFoundationResumeState();
    var resumedFromCheckpoint = job.ResumeFromCheckpoint
                                && TryLoadCheckpoint(
            job,
            requestFingerprint,
            build.Ruleset.RulesetHash,
            out resume);
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
        TryDelete(job.CheckpointPath);
        TryDelete(job.CheckpointEpisodesPath);
    }

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
            WriteAtomic(
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
                WriteAtomic(
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
    var checkpointEpisodeCount = job.Request.Resume?.Replay.Count ?? -1;
    job.Request.Checkpoint = state =>
    {
        lock (checkpointGate)
        {
            if (state.Replay.Count != checkpointEpisodeCount
                || !File.Exists(job.CheckpointEpisodesPath))
            {
                WriteEpisodes(job.CheckpointEpisodesPath, state.Replay);
                checkpointEpisodeCount = state.Replay.Count;
            }
            WriteAtomic(
                job.CheckpointPath,
                Serialize(new CombatFoundationWorkerCheckpoint
                {
                    RequestFingerprint = requestFingerprint,
                    RulesetHash = build.Ruleset.RulesetHash,
                    EpisodesPath = job.CheckpointEpisodesPath,
                    UpdatedUtc = DateTime.UtcNow,
                    Resume = WithoutReplay(state)
                }));
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
        TryDelete(job.CheckpointPath);
        TryDelete(job.CheckpointEpisodesPath);
    }
    var resumable = !job.Request.PreflightOnly
                    && !training.AcceptancePassed
                    && File.Exists(job.CheckpointPath)
                    && File.Exists(job.CheckpointEpisodesPath);
    var completionKind = job.Request.PreflightOnly
        ? training.Success
            ? "preflight-passed"
            : "preflight-failed"
        : training.AcceptancePassed
            ? "training-accepted"
            : resumable
                ? "training-rejected-resumable"
                : "training-rejected";
    WriteAtomic(
        job.ResultPath,
        Serialize(new CombatFoundationWorkerResult
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
            Training = training
        }));
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
        var resumable = File.Exists(job.CheckpointPath)
                        && File.Exists(job.CheckpointEpisodesPath);
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
                EpisodesPath = File.Exists(job.CheckpointEpisodesPath)
                    ? job.CheckpointEpisodesPath
                    : "",
                CheckpointPath = File.Exists(job.CheckpointPath)
                    ? job.CheckpointPath
                    : "",
                Resumable = resumable
            }));
    }
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    if (job != null && !string.IsNullOrWhiteSpace(job.ResultPath))
    {
        var resumable = File.Exists(job.CheckpointPath)
                        && File.Exists(job.CheckpointEpisodesPath);
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
                EpisodesPath = File.Exists(job.CheckpointEpisodesPath)
                    ? job.CheckpointEpisodesPath
                    : "",
                CheckpointPath = File.Exists(job.CheckpointPath)
                    ? job.CheckpointPath
                    : "",
                Resumable = resumable
            }));
    }
    return 1;
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
    out CombatCampaignFoundationResumeState resume)
{
    resume = new CombatCampaignFoundationResumeState();
    try
    {
        if (string.IsNullOrWhiteSpace(job.CheckpointPath)
            || !File.Exists(job.CheckpointPath))
        {
            return false;
        }
        var checkpoint = Deserialize<CombatFoundationWorkerCheckpoint>(
            File.ReadAllText(job.CheckpointPath));
        if (checkpoint == null
            || checkpoint.SchemaVersion != 4
            || !string.Equals(
                checkpoint.RequestFingerprint,
                requestFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                checkpoint.RulesetHash,
                rulesetHash,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(checkpoint.EpisodesPath)
            || !File.Exists(checkpoint.EpisodesPath))
        {
            return false;
        }
        var episodes = new List<CombatEpisode>();
        foreach (var line in File.ReadLines(checkpoint.EpisodesPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var episode = Deserialize<CombatEpisode>(line);
            if (episode != null)
            {
                episodes.Add(episode);
            }
        }
        checkpoint.Resume.Replay = episodes;
        resume = checkpoint.Resume;
        return resume.SchemaVersion == 4;
    }
    catch
    {
        return false;
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
    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(
        Path.GetDirectoryName(fullPath)
        ?? throw new InvalidOperationException(
            "Episode output directory is missing."));
    var temporaryPath = fullPath + ".tmp-" + Environment.ProcessId;
    using (var writer = new StreamWriter(
               temporaryPath,
               append: false,
               new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
    {
        foreach (var episode in episodes)
        {
            writer.WriteLine(SerializeCompact(episode));
        }
    }
    File.Move(temporaryPath, fullPath, overwrite: true);
}

static string SuccessArchiveRoot(CombatFoundationWorkerJob job)
{
    return string.IsNullOrWhiteSpace(job.SuccessArchiveDirectory)
        ? Path.Combine(job.ResultDirectory, "foundation-success-cases")
        : Path.GetFullPath(job.SuccessArchiveDirectory);
}

static void PersistObservation(
    CombatFoundationWorkerJob job,
    CombatFoundationCampaignObservation observation)
{
    var path = Path.Combine(
        SuccessArchiveRoot(job),
        "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
        observation.CompatibilityKey,
        "observations",
        observation.CaseId + ".json");
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
    var compatibilityDirectory = Path.Combine(
        SuccessArchiveRoot(job),
        "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
        observation.CompatibilityKey);
    var casePath = Path.Combine(
        compatibilityDirectory,
        "cases",
        observation.CaseId + ".json");
    var added = !File.Exists(casePath);
    if (added)
    {
        WriteAtomic(casePath, Serialize(successCase));
    }
    if (successCase.Episodes.Count > 0)
    {
        var expertCasePath = Path.Combine(
            compatibilityDirectory,
            "expert-cases",
            observation.CaseId + ".json");
        if (!File.Exists(expertCasePath))
        {
            WriteAtomic(expertCasePath, Serialize(successCase));
        }
    }
    return added;
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
        var observationPath = Path.Combine(
            archiveRoot,
            "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
            observation.CompatibilityKey,
            "observations",
            observation.CaseId + ".json");
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
            archiveRoot,
            "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
            compatibilityKey,
            "observations");
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
        var compatibilityDirectory = Path.Combine(
            archiveRoot,
            "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
            observation.CompatibilityKey);
        var casePath = Path.Combine(
            compatibilityDirectory,
            "cases",
            observation.CaseId + ".json");
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
            var expertCasePath = Path.Combine(
                archiveRoot,
                "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
                observation.CompatibilityKey,
                "expert-cases",
                observation.CaseId + ".json");
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
    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(
        Path.GetDirectoryName(fullPath)
        ?? throw new InvalidOperationException(
            "JSONL output directory is missing."));
    var temporaryPath = fullPath + ".tmp-" + Environment.ProcessId;
    using (var writer = new StreamWriter(
               temporaryPath,
               append: false,
               new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
    {
        foreach (var value in values)
        {
            writer.WriteLine(SerializeCompact(value!));
        }
    }
    File.Move(temporaryPath, fullPath, overwrite: true);
}

static void TryDelete(string path)
{
    try
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
    catch
    {
        // A completed result is valid even when stale resume files remain.
    }
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

static void WriteAtomic(string path, string contents)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return;
    }
    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(
        Path.GetDirectoryName(fullPath)
        ?? throw new InvalidOperationException("Output directory is missing."));
    var temporaryPath = fullPath + ".tmp-" + Environment.ProcessId;
    File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
    File.Move(temporaryPath, fullPath, overwrite: true);
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
