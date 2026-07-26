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
    if (job == null || job.SchemaVersion != 2)
    {
        throw new InvalidOperationException("Unsupported or empty foundation worker job.");
    }
    Directory.CreateDirectory(job.ResultDirectory);
    if (string.IsNullOrWhiteSpace(job.CheckpointPath))
    {
        job.CheckpointPath = Path.Combine(
            job.ResultDirectory,
            "foundation-training-checkpoint-v2.json");
    }
    if (string.IsNullOrWhiteSpace(job.CheckpointEpisodesPath))
    {
        job.CheckpointEpisodesPath = Path.Combine(
            job.ResultDirectory,
            "foundation-training-checkpoint-episodes-v2.jsonl");
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

    var training = new CombatCampaignFoundationTrainer(
        new CombatCampaignRunner(
            new CombatSimulationEngine(
                new AuraToolsNativeRewardExtensionFactory()))).Run(
        job.Request,
        build.Ruleset,
        job.InitialChampion,
        cancellation.Token);
    var episodesPath = Path.Combine(
        job.ResultDirectory,
        "foundation-training-episodes-v2.jsonl");
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
    WriteAtomic(
        job.ResultPath,
        Serialize(new CombatFoundationWorkerResult
        {
            JobId = job.JobId,
            Success = true,
            Message = training.Message,
            Runtime = RuntimeDescription(requestedWorkers),
            RulesetHash = build.Ruleset.RulesetHash,
            EpisodesPath = episodesPath,
            Training = training
        }));
    TryDelete(job.CheckpointPath);
    TryDelete(job.CheckpointEpisodesPath);
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
        WriteAtomic(
            job.ResultPath,
            Serialize(new CombatFoundationWorkerResult
            {
                JobId = job.JobId,
                Cancelled = true,
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
                Resumable = File.Exists(job.CheckpointPath)
            }));
    }
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    if (job != null && !string.IsNullOrWhiteSpace(job.ResultPath))
    {
        WriteAtomic(
            job.ResultPath,
            Serialize(new CombatFoundationWorkerResult
            {
                JobId = job.JobId,
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
                Resumable = File.Exists(job.CheckpointPath)
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
            || checkpoint.SchemaVersion != 2
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
        return resume.SchemaVersion == 2;
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
        Champion = source.Champion,
        WorkingChampion = source.WorkingChampion,
        Iterations = new List<CombatCampaignFoundationIteration>(
            source.Iterations),
        ModelTraining = source.ModelTraining,
        Telemetry = source.Telemetry
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
