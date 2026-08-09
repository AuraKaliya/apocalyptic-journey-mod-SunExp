using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraFoundationTrainer.Worker;
using Newtonsoft.Json;

var assertions = 0;

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + message);
    }
    assertions++;
}

CombatEpisode Episode(int id, bool successful = true)
{
    return new CombatEpisode
    {
        EpisodeId = "episode-" + id.ToString("D4"),
        JourneyRunId = "run-" + (id / 4).ToString("D4"),
        JourneyBattleIndex = id % 4,
        Seed = (ulong)(100_000 + id),
        ScenarioId = "scenario-" + (id % 3),
        Outcome = successful ? "victory" : "defeat",
        FinalPlayerHp = successful ? 18 : 1,
        FinalPlayerMaxHp = 20,
        Campaign = new CombatCampaignEpisodeMetadata
        {
            DifficultyId = id % 2 == 0 ? "normal" : "advanced",
            FinalBossVictory = successful,
            OutcomeClass = successful ? "victory" : "defeat",
            TrainingIteration = 1,
            CurriculumStage = "storage-test"
        },
        Frames = new List<CombatEpisodeFrame>
        {
            new()
            {
                Turn = id,
                StateFingerprint = "state-" + id,
                StateFeatures = new Dictionary<string, double>
                {
                    ["playerHp"] = 18,
                    ["enemyHpTotal"] = id + 3
                }
            }
        }
    };
}

void WriteGzipText(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var output = File.Create(path);
    using var gzip = new GZipStream(
        output,
        CompressionLevel.Fastest,
        leaveOpen: false);
    using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
    writer.Write(text);
}

void WriteLegacyReplayEntry(
    string warehouseRoot,
    CombatEpisode episode,
    string relativePath,
    IReadOnlyDictionary<int, string>? tokenCatalog = null)
{
    var episodePath = Path.Combine(
        warehouseRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));
    WriteGzipText(episodePath, JsonConvert.SerializeObject(episode));
    File.WriteAllText(
        Path.Combine(warehouseRoot, "replay-index-v1.jsonl"),
        JsonConvert.SerializeObject(new
        {
            Key = CombatFoundationReplayWarehouse.StableKey(episode),
            RelativePath = relativePath,
            DifficultyId = episode.Campaign?.DifficultyId ?? "",
            ScenarioId = episode.ScenarioId,
            Successful = episode.Campaign?.FinalBossVictory == true,
            Hard = episode.Campaign?.FinalBossVictory != true,
            TrainingIteration = episode.Campaign?.TrainingIteration ?? 0,
            Frames = episode.Frames?.Count ?? 0,
            EstimatedResidentBytes = 1024,
            StoredBytes = new FileInfo(episodePath).Length,
            CurriculumStage = "legacy",
            CreatedUtc = DateTime.UtcNow,
            EmbeddedFeatureTokenCatalogPresent = tokenCatalog != null,
            EmbeddedFeatureTokenCatalog = tokenCatalog
        }) + Environment.NewLine,
        new UTF8Encoding(false));
}

var root = Path.Combine(
    Path.GetTempPath(),
    "aura-foundation-storage-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    if (args.Contains("--transformer-only", StringComparer.Ordinal))
    {
        RunTransformerSafetyTests();
        Console.WriteLine(
            "AuraFoundationTrainer.Worker Transformer safety tests passed: "
            + assertions
            + " assertions.");
        return;
    }

    var identityFilesIndex = Array.FindIndex(args, argument => string.Equals(
        argument,
        "--identity-files",
        StringComparison.Ordinal));
    if (identityFilesIndex >= 0)
    {
        if (identityFilesIndex + 2 >= args.Length)
        {
            throw new InvalidOperationException(
                "--identity-files requires <job.json> <checkpoint.json.gz>");
        }
        var identityFileJob = JsonConvert.DeserializeObject<
                                  CombatFoundationWorkerJob>(
                                  CombatFoundationCheckpointStorage
                                      .ReadAllTextShared(
                                          args[identityFilesIndex + 1]),
                                  new JsonSerializerSettings
                                  {
                                      ObjectCreationHandling =
                                          ObjectCreationHandling.Replace
                                  })
                              ?? throw new InvalidDataException(
                                  "identity probe job is invalid");
        var identityFileCheckpoint = JsonConvert.DeserializeObject<
                                         CombatFoundationWorkerCheckpoint>(
                                         CombatFoundationCheckpointStorage
                                             .ReadAllTextShared(
                                                 args[identityFilesIndex + 2]))
                                     ?? throw new InvalidDataException(
                                         "identity probe checkpoint is invalid");
        var workerProgram = typeof(CombatFoundationRequestIdentity).Assembly
            .GetType("Program");
        var prepareCaseArchive = workerProgram?.GetMethod(
            "PrepareCaseArchive",
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic);
        prepareCaseArchive?.Invoke(
            null,
            new object[]
            {
                identityFileJob,
                identityFileCheckpoint.RulesetHash
            });
        var compatible = CombatFoundationRequestIdentity.Matches(
            identityFileJob,
            identityFileCheckpoint,
            identityFileCheckpoint.RulesetHash,
            out var fileIdentityDiagnostic);
        Console.WriteLine(
            "Checkpoint identity probe: compatible="
            + compatible
            + ", checkpoint="
            + identityFileCheckpoint.RequestFingerprint
            + ", current="
            + CombatFoundationRequestIdentity.CreateFingerprint(
                identityFileJob,
                identityFileCheckpoint.RulesetHash)
            + ", legacy="
            + CombatFoundationRequestIdentity.CreateLegacyFingerprint(
                identityFileJob,
                identityFileCheckpoint.RulesetHash,
                unchecked((int)(identityFileCheckpoint.Resume?.RunSeed ?? 0UL)))
            + ", diagnostic="
            + fileIdentityDiagnostic);
        if (!compatible)
        {
            Environment.ExitCode = 2;
        }
        return;
    }

    var autoTuneCachePath = Path.Combine(root, "foundation-auto-tune.json");
    File.WriteAllText(autoTuneCachePath, "{}");
    var disabledAutoTuneCache = new CombatFoundationAutoTuneCachePolicy(false);
    var enabledAutoTuneCache = new CombatFoundationAutoTuneCachePolicy(true);
    var confidentAutoTune = new CombatFoundationAutoTuneResult();
    Assert(!disabledAutoTuneCache.ShouldLoad(autoTuneCachePath)
           && !disabledAutoTuneCache.ShouldPersist(confidentAutoTune),
        "an explicit auto-tune cache opt-out disables both reads and writes");
    Assert(enabledAutoTuneCache.ShouldLoad(autoTuneCachePath)
           && enabledAutoTuneCache.ShouldPersist(confidentAutoTune)
           && !enabledAutoTuneCache.ShouldPersist(
               new CombatFoundationAutoTuneResult { LowConfidence = true }),
        "enabled cache reuse reads existing state and persists only confident measurements");

    const string identityRuleset =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    var identityJob = new CombatFoundationWorkerJob
    {
        Request = new CombatCampaignFoundationTrainingRequest
        {
            TransformerTeacher = new CombatTransformerTeacherOptions
            {
                Backend = CombatTransformerTeacherBackendNames.Cpu,
                RandomSeed = 12345
            }.Normalized()
        }
    };
    var stableFingerprint = CombatFoundationRequestIdentity.CreateFingerprint(
        identityJob,
        identityRuleset);
    var persistedSeedFingerprint = CombatFoundationRequestIdentity
        .CreatePersistedSeedFingerprint(identityJob, identityRuleset);
    var identityJsonRoundTrip = JsonConvert.DeserializeObject<
                                    CombatFoundationWorkerJob>(
                                    JsonConvert.SerializeObject(identityJob),
                                    new JsonSerializerSettings
                                    {
                                        ObjectCreationHandling =
                                            ObjectCreationHandling.Replace
                                    })
                                ?? throw new InvalidOperationException(
                                    "identity JSON round-trip failed");
    var identityBoundaryDifference = CombatFoundationRequestIdentity
        .DescribeDifferences(
            CombatFoundationRequestIdentity.CreateFields(
                identityJob,
                identityRuleset),
            CombatFoundationRequestIdentity.CreateFields(
                identityJsonRoundTrip,
                identityRuleset));
    Assert(string.Equals(
               stableFingerprint,
               CombatFoundationRequestIdentity.CreateFingerprint(
                   identityJsonRoundTrip,
                   identityRuleset),
               StringComparison.Ordinal),
        "current identity is stable across the Control Center to Worker JSON boundary: "
        + identityBoundaryDifference);
    var appendedDefaultIdentityJob = JsonConvert.DeserializeObject<
                                         CombatFoundationWorkerJob>(
                                         JsonConvert.SerializeObject(
                                             identityJob),
                                         new JsonSerializerSettings
                                         {
                                             ObjectCreationHandling =
                                                 ObjectCreationHandling.Replace
                                         })
                                     ?? throw new InvalidOperationException(
                                         "appended-default identity clone failed");
    foreach (var campaign in new[]
             {
                 appendedDefaultIdentityJob.Request.TrainingCampaign,
                 appendedDefaultIdentityJob.Request.ValidationCampaign
             })
    {
        campaign.AttributeIds.AddRange(campaign.AttributeIds.ToList());
        campaign.EnabledRewardCardPackIds.AddRange(
            campaign.EnabledRewardCardPackIds.ToList());
        campaign.CardRewardEncounterKinds.AddRange(
            campaign.CardRewardEncounterKinds.ToList());
    }
    Assert(string.Equals(
               stableFingerprint,
               CombatFoundationRequestIdentity.CreateFingerprint(
                   appendedDefaultIdentityJob,
                   identityRuleset),
               StringComparison.Ordinal),
        "legacy appended defaults in set-like Campaign fields do not change exact-resume identity");
    appendedDefaultIdentityJob.Request.TrainingCampaign.Player.Deck.Add(
        appendedDefaultIdentityJob.Request.TrainingCampaign.Player.Deck
            .FirstOrDefault() ?? "card_1001");
    Assert(!string.Equals(
               stableFingerprint,
               CombatFoundationRequestIdentity.CreateFingerprint(
                   appendedDefaultIdentityJob,
                   identityRuleset),
               StringComparison.Ordinal),
        "ordered Campaign multisets such as the player deck remain identity-sensitive");
    var persistedSeedCheckpoint = new CombatFoundationWorkerCheckpoint
    {
        RequestFingerprint = persistedSeedFingerprint,
        RulesetHash = identityRuleset
    };
    Assert(CombatFoundationRequestIdentity.Matches(
               identityJsonRoundTrip,
               persistedSeedCheckpoint,
               identityRuleset,
               out var persistedSeedDiagnostic)
           && persistedSeedDiagnostic.Contains(
               "legacy v3 identity matched",
               StringComparison.Ordinal),
        "v3 persisted-seed checkpoints migrate to canonical Campaign identity");
    var legacyFingerprint = CombatFoundationRequestIdentity
        .CreateLegacyFingerprint(identityJob, identityRuleset, 12345);
    identityJob.Request.TransformerTeacher.RandomSeed = 54321;
    Assert(string.Equals(
               stableFingerprint,
               CombatFoundationRequestIdentity.CreateFingerprint(
                   identityJob,
                   identityRuleset),
               StringComparison.Ordinal),
        "exact-resume identity excludes the runtime-generated Transformer seed");
    var legacyCheckpoint = new CombatFoundationWorkerCheckpoint
    {
        RequestFingerprint = legacyFingerprint,
        RulesetHash = identityRuleset,
        Resume = new CombatCampaignFoundationResumeState
        {
            RunSeed = 12345UL
        }
    };
    Assert(CombatFoundationRequestIdentity.Matches(
               identityJob,
               legacyCheckpoint,
               identityRuleset,
               out var legacyIdentityDiagnostic)
           && legacyIdentityDiagnostic.Contains(
               "persisted seed plan",
               StringComparison.Ordinal),
        "schema-v16 checkpoints migrate from the legacy seed-sensitive identity");
    var structuredCheckpoint = new CombatFoundationWorkerCheckpoint
    {
        RequestFingerprint = stableFingerprint,
        RequestIdentityFields = CombatFoundationRequestIdentity.CreateFields(
            identityJob,
            identityRuleset),
        RulesetHash = identityRuleset,
        Resume = new CombatCampaignFoundationResumeState { RunSeed = 12345UL }
    };
    identityJob.Request.Training.BatchSize++;
    Assert(!CombatFoundationRequestIdentity.Matches(
               identityJob,
               structuredCheckpoint,
               identityRuleset,
               out var identityDifference)
           && identityDifference.Contains(
               nameof(CombatPolicyValueTrainingOptions.BatchSize),
               StringComparison.Ordinal),
        "structured checkpoint identity reports the incompatible field instead of only opaque hashes");
    identityJob.Request.Training.BatchSize--;
    var identityResultsRoot = Path.Combine(root, "identity-results");
    var identityCatalogDirectory = Path.Combine(
        identityResultsRoot,
        "foundation-controller-checkpoint",
        "subject-hash");
    var identityCheckpointPath = Path.Combine(
        identityCatalogDirectory,
        "checkpoints",
        "opaque-legacy.json.gz");
    var identityCatalogPath = Path.Combine(
        identityCatalogDirectory,
        CombatFoundationCheckpointCatalogProtocol.CatalogFileName);
    var identitySourceJobId = "identity-source-job";
    var identitySourceJobDirectory = Path.Combine(
        identityResultsRoot,
        identitySourceJobId);
    Directory.CreateDirectory(identityCatalogDirectory);
    Directory.CreateDirectory(identitySourceJobDirectory);
    var identitySourceJob = JsonConvert.DeserializeObject<
                                CombatFoundationWorkerJob>(
                                JsonConvert.SerializeObject(identityJob),
                                new JsonSerializerSettings
                                {
                                    ObjectCreationHandling =
                                        ObjectCreationHandling.Replace
                                })
                            ?? throw new InvalidOperationException(
                                "identity source clone failed");
    foreach (var campaign in new[]
             {
                 identitySourceJob.Request.TrainingCampaign,
                 identitySourceJob.Request.ValidationCampaign
             })
    {
        campaign.AttributeIds.AddRange(campaign.AttributeIds.ToList());
        campaign.CardRewardEncounterKinds.AddRange(
            campaign.CardRewardEncounterKinds.ToList());
    }
    identitySourceJob.Request.RunSeed = 777UL;
    identitySourceJob.Request.TransformerTeacher.RandomSeed = 777;
    File.WriteAllText(
        Path.Combine(identitySourceJobDirectory, "foundation-worker-job.json"),
        JsonConvert.SerializeObject(identitySourceJob));
    const string opaqueLegacyFingerprint =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    File.WriteAllText(
        identityCatalogPath,
        JsonConvert.SerializeObject(new CombatFoundationCheckpointCatalog
        {
            Entries =
            {
                new CombatFoundationCheckpointCatalogEntry
                {
                    SourceJobId = identitySourceJobId,
                    RequestFingerprint = opaqueLegacyFingerprint,
                    CheckpointPath = identityCheckpointPath,
                    SupportsExact = true
                }
            }
        }));
    // Keep the current request in its original in-memory form. This is the
    // Control Center preflight boundary; the source job above represents the
    // same request after Worker JSON deserialization.
    var identityCurrentJob = identityJob;
    identityCurrentJob.CheckpointCatalogPath = identityCatalogPath;
    identityCurrentJob.ResumeCheckpointPath = identityCheckpointPath;
    identityCurrentJob.Request.RunSeed = 888UL;
    identityCurrentJob.Request.TransformerTeacher.RandomSeed = 888;
    var opaqueLegacyCheckpoint = new CombatFoundationWorkerCheckpoint
    {
        RequestFingerprint = opaqueLegacyFingerprint,
        RulesetHash = identityRuleset,
        Resume = new CombatCampaignFoundationResumeState { RunSeed = 777UL }
    };
    Assert(CombatFoundationRequestIdentity.Matches(
               identityCurrentJob,
               opaqueLegacyCheckpoint,
               identityRuleset,
               out var sourceJobIdentityDiagnostic)
           && sourceJobIdentityDiagnostic.Contains(
               "source-job identity matched",
               StringComparison.Ordinal),
        "opaque legacy checkpoints bind exact continuation to their catalog source job fields: "
        + sourceJobIdentityDiagnostic);
    identityCurrentJob.Request.Training.LearningRate *= 0.5d;
    Assert(!CombatFoundationRequestIdentity.Matches(
               identityCurrentJob,
               opaqueLegacyCheckpoint,
               identityRuleset,
               out var sourceJobDifference)
           && sourceJobDifference.Contains(
               nameof(CombatPolicyValueTrainingOptions.LearningRate),
               StringComparison.Ordinal),
        "source-job exact migration still rejects a real optimizer configuration change");

    var replayRoot = Path.Combine(root, "replay");
    var replay = new CombatFoundationReplayWarehouse(replayRoot);
    var source = Enumerable.Range(0, 600)
        .Select(index => Episode(index, index % 5 != 0))
        .ToList();
    var clock = Stopwatch.StartNew();
    var archive = replay.Archive(1, source);
    clock.Stop();
    var shardPaths = Directory.GetFiles(
        replayRoot,
        "*.arsh",
        SearchOption.AllDirectories);
    var tinyEpisodeFiles = Directory.GetFiles(
        replayRoot,
        "*.json.gz",
        SearchOption.AllDirectories);
    var indexPath = Path.Combine(replayRoot, "replay-index-v2.jsonl");
    Assert(string.IsNullOrWhiteSpace(archive.Error)
           && archive.ArchivedEpisodes == source.Count
           && archive.ArchivedBytes > 0,
        "replay archive publishes every episode in one committed batch");
    Assert(shardPaths.Length == 3
           && tinyEpisodeFiles.Length == 0
           && File.ReadLines(indexPath).Count() == 1,
        "replay archive bounds 600 episodes to three binary shards and one index transaction");
    var loaded = replay.Load(
        2,
        Array.Empty<string>(),
        source.Count,
        long.MaxValue);
    Assert(loaded.Count == source.Count
           && loaded.Select(item => item.EpisodeId).Distinct().Count()
              == source.Count,
        "binary replay shards round-trip with stable episode identity");
    var duplicate = replay.Archive(2, source);
    Assert(duplicate.ArchivedEpisodes == 0
           && duplicate.DuplicateEpisodes == source.Count
           && Directory.GetFiles(
               replayRoot,
               "*.arsh",
               SearchOption.AllDirectories).Length == 3
           && File.ReadLines(indexPath).Count() == 1,
        "replay retry is idempotent without new shards or index commits");
    Assert(replay.Load(
               2,
               Array.Empty<string>(),
               10,
               1L).Count == 0,
        "replay loader enforces the resident-byte budget before materialization");

    File.AppendAllText(indexPath, "{truncated", new UTF8Encoding(false));
    var reopenedAfterIndexTail = new CombatFoundationReplayWarehouse(replayRoot);
    Assert(reopenedAfterIndexTail.Load(
               3,
               Array.Empty<string>(),
               source.Count,
               long.MaxValue).Count == source.Count,
        "truncated final index transaction is ignored without losing committed shards");
    var afterTailSource = Enumerable.Range(600, 8)
        .Select(index => Episode(index, index % 2 == 0))
        .ToList();
    var afterTailArchive = reopenedAfterIndexTail.Archive(3, afterTailSource);
    var reopenedAfterTailCommit = new CombatFoundationReplayWarehouse(replayRoot);
    var afterTailLoaded = reopenedAfterTailCommit.Load(
        4,
        Array.Empty<string>(),
        source.Count + afterTailSource.Count,
        long.MaxValue);
    Assert(string.IsNullOrWhiteSpace(afterTailArchive.Error)
           && afterTailArchive.ArchivedEpisodes == afterTailSource.Count
           && afterTailLoaded.Count == source.Count + afterTailSource.Count
           && afterTailSource.All(expected => afterTailLoaded.Any(actual =>
               actual.EpisodeId == expected.EpisodeId))
           && File.ReadLines(indexPath).Count() == 2
           && !File.ReadAllText(indexPath).Contains(
               "{truncated",
               StringComparison.Ordinal),
        "index recovery truncates the invalid transaction boundary before a new commit, which remains visible after restart");

    var legacyEpisode = Episode(10_000, successful: false);
    var legacyKey = CombatFoundationReplayWarehouse.StableKey(legacyEpisode);
    var legacyRelativePath = "episodes/legacy/legacy.json.gz";
    var legacyPath = Path.Combine(
        replayRoot,
        legacyRelativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
    using (var output = File.Create(legacyPath))
    using (var gzip = new GZipStream(
               output,
               CompressionLevel.Fastest,
               leaveOpen: false))
    using (var writer = new StreamWriter(
               gzip,
               new UTF8Encoding(false)))
    {
        writer.Write(JsonConvert.SerializeObject(legacyEpisode));
    }
    var legacyIndexPath = Path.Combine(replayRoot, "replay-index-v1.jsonl");
    var legacyIndexRow = JsonConvert.SerializeObject(new
    {
        Key = legacyKey,
        RelativePath = legacyRelativePath,
        DifficultyId = "advanced",
        ScenarioId = legacyEpisode.ScenarioId,
        Successful = false,
        Hard = true,
        TrainingIteration = 1,
        Frames = 1,
        EstimatedResidentBytes = 1024,
        StoredBytes = new FileInfo(legacyPath).Length,
        CurriculumStage = "legacy",
        CreatedUtc = DateTime.UtcNow
    });
    File.WriteAllText(
        legacyIndexPath,
        legacyIndexRow + Environment.NewLine,
        new UTF8Encoding(false));
    var mixed = new CombatFoundationReplayWarehouse(replayRoot).Load(
        4,
        Array.Empty<string>(),
        source.Count + afterTailSource.Count + 1,
        long.MaxValue);
    Assert(mixed.Any(item => item.EpisodeId == legacyEpisode.EpisodeId)
           && mixed.Any(item => item.EpisodeId == source[0].EpisodeId)
           && !File.Exists(legacyPath)
           && !File.ReadAllText(legacyIndexPath).Contains(
               legacyKey,
               StringComparison.Ordinal),
        "warehouse recovery commits and verifies a v1-to-v2 batch migration before deleting the tiny file and atomically removing its stale v1 index row");
    File.WriteAllText(
        legacyIndexPath,
        legacyIndexRow + Environment.NewLine,
        new UTF8Encoding(false));
    _ = new CombatFoundationReplayWarehouse(replayRoot);
    Assert(!File.ReadAllText(legacyIndexPath).Contains(
            legacyKey,
            StringComparison.Ordinal),
        "startup compacts a stale migrated v1 index row left by a crash after the legacy tiny file was already deleted");

    var remapReplayRoot = Path.Combine(root, "cross-process-token-replay");
    Directory.CreateDirectory(remapReplayRoot);
    const string remappedFeatureName = "storage.cross-process.playerHp";
    const int foreignProcessToken = 900_001;
    var localProcessToken = CombatFeatureTokenRegistry.GetToken(
        remappedFeatureName);
    Assert(localProcessToken != foreignProcessToken,
        "cross-process token fixture uses a deliberately different local id");
    var foreignCompactEpisode = Episode(10_100);
    var foreignFrame = foreignCompactEpisode.Frames.Single();
    foreignFrame.CompactStateFeatureTokenIds = new[] { foreignProcessToken };
    foreignFrame.CompactStateFeatureValues = new[] { 18f };
    foreignFrame.Candidates = new List<CombatEpisodeCandidate>
    {
        new()
        {
            CandidateId = "foreign-action",
            Legal = true,
            CompactFeatureTokenIds = new[] { foreignProcessToken },
            CompactFeatureValues = new[] { 1f }
        }
    };
    const string foreignRelativePath =
        "episodes/legacy/foreign-compact.json.gz";
    WriteLegacyReplayEntry(
        remapReplayRoot,
        foreignCompactEpisode,
        foreignRelativePath,
        new Dictionary<int, string>
        {
            [foreignProcessToken] = remappedFeatureName
        });
    var remappedReplay = new CombatFoundationReplayWarehouse(remapReplayRoot);
    var remappedEpisode = remappedReplay.Load(
            2,
            Array.Empty<string>(),
            2,
            long.MaxValue)
        .Single();
    Assert(remappedEpisode.Frames.Single().CompactStateFeatureTokenIds?
               .Single() == localProcessToken
           && remappedEpisode.Frames.Single().Candidates.Single()
               .CompactFeatureTokenIds?.Single() == localProcessToken
           && !File.Exists(Path.Combine(
               remapReplayRoot,
               foreignRelativePath.Replace(
                   '/',
                   Path.DirectorySeparatorChar)))
           && !File.ReadAllText(Path.Combine(
               remapReplayRoot,
               "replay-index-v1.jsonl")).Contains(
               CombatFoundationReplayWarehouse.StableKey(
                   foreignCompactEpisode),
               StringComparison.Ordinal),
        "legacy compact replay remaps state and action ids by persisted name before committing the self-contained v2 shard");

    var noCatalogReplayRoot = Path.Combine(root, "no-token-catalog-replay");
    Directory.CreateDirectory(noCatalogReplayRoot);
    var noCatalogEpisode = Episode(10_200);
    noCatalogEpisode.Frames.Single().CompactStateFeatureTokenIds =
        new[] { 900_002 };
    noCatalogEpisode.Frames.Single().CompactStateFeatureValues = new[] { 7f };
    const string noCatalogRelativePath =
        "episodes/legacy/no-catalog-compact.json.gz";
    WriteLegacyReplayEntry(
        noCatalogReplayRoot,
        noCatalogEpisode,
        noCatalogRelativePath);
    var noCatalogReplay = new CombatFoundationReplayWarehouse(
        noCatalogReplayRoot);
    Assert(noCatalogReplay.Load(
               2,
               Array.Empty<string>(),
               2,
               long.MaxValue).Count == 0
           && File.Exists(Path.Combine(
               noCatalogReplayRoot,
               noCatalogRelativePath.Replace(
                   '/',
                   Path.DirectorySeparatorChar)))
           && !File.Exists(Path.Combine(
               noCatalogReplayRoot,
               "replay-index-v2.jsonl"))
           && File.ReadAllText(Path.Combine(
               noCatalogReplayRoot,
               "replay-index-v1.jsonl")).Contains(
               CombatFoundationReplayWarehouse.StableKey(noCatalogEpisode),
               StringComparison.Ordinal),
        "compact-only legacy replay without a token catalog fails closed and is not destructively migrated");

    var uncertainReplayRoot = Path.Combine(root, "uncertain-index-replay");
    var uncertainReplay = new CombatFoundationReplayWarehouse(
        uncertainReplayRoot);
    _ = uncertainReplay.Archive(1, new[] { Episode(10_300) });
    _ = uncertainReplay.Archive(2, new[] { Episode(10_301) });
    var uncertainIndexPath = Path.Combine(
        uncertainReplayRoot,
        "replay-index-v2.jsonl");
    var committedIndexLines = File.ReadAllLines(uncertainIndexPath);
    var parseableButCorruptBatch = JsonConvert.DeserializeObject<
        Newtonsoft.Json.Linq.JObject>(committedIndexLines[0])!;
    parseableButCorruptBatch["TransactionId"] = "checksum-bit-flip";
    File.WriteAllLines(
        uncertainIndexPath,
        new[]
        {
            parseableButCorruptBatch.ToString(Formatting.None),
            committedIndexLines[1]
        },
        new UTF8Encoding(false));
    var shardsBeforeUncertainRecovery = Directory.GetFiles(
        uncertainReplayRoot,
        "*.arsh",
        SearchOption.AllDirectories);
    var reopenedUncertainReplay = new CombatFoundationReplayWarehouse(
        uncertainReplayRoot);
    var retainedAfterMiddleDamage = reopenedUncertainReplay.Load(
        3,
        Array.Empty<string>(),
        4,
        long.MaxValue);
    Assert(reopenedUncertainReplay.RecoveryUncertain
           && retainedAfterMiddleDamage.Any(item =>
               item.EpisodeId == "episode-10301")
           && Directory.GetFiles(
               uncertainReplayRoot,
               "*.arsh",
               SearchOption.AllDirectories).Length
              == shardsBeforeUncertainRecovery.Length,
        "a checksummed middle transaction bit flip preserves later parseable history and disables orphan GC");

    var legacyV2ReplayRoot = Path.Combine(root, "legacy-v2-index-replay");
    var legacyV2Replay = new CombatFoundationReplayWarehouse(
        legacyV2ReplayRoot);
    _ = legacyV2Replay.Archive(1, new[] { Episode(10_400) });
    var legacyV2IndexPath = Path.Combine(
        legacyV2ReplayRoot,
        "replay-index-v2.jsonl");
    var legacyV2Batch = JsonConvert.DeserializeObject<
        Newtonsoft.Json.Linq.JObject>(File.ReadAllText(legacyV2IndexPath))!;
    legacyV2Batch.Remove("ChecksumVersion");
    legacyV2Batch.Remove("ContentChecksumSha256");
    File.WriteAllText(
        legacyV2IndexPath,
        legacyV2Batch.ToString(Formatting.None) + Environment.NewLine,
        new UTF8Encoding(false));
    var legacyV2Shard = Directory.GetFiles(
        legacyV2ReplayRoot,
        "*.arsh",
        SearchOption.AllDirectories).Single();
    var legacyV2Orphan = Path.Combine(
        Path.GetDirectoryName(legacyV2Shard)!,
        "legacy-unchecksummed-orphan.arsh");
    File.Copy(legacyV2Shard, legacyV2Orphan);
    var reopenedLegacyV2 = new CombatFoundationReplayWarehouse(
        legacyV2ReplayRoot);
    Assert(reopenedLegacyV2.RecoveryUncertain
           && reopenedLegacyV2.Load(
               2,
               Array.Empty<string>(),
               2,
               long.MaxValue).Single().EpisodeId == "episode-10400"
           && File.Exists(legacyV2Orphan),
        "legacy unchecksummed v2 index remains readable but enters recovery-uncertain mode and cannot garbage-collect orphans");

    var corruptReplayRoot = Path.Combine(root, "corrupt-replay");
    var corruptReplay = new CombatFoundationReplayWarehouse(corruptReplayRoot);
    var corruptArchive = corruptReplay.Archive(1, source.Take(8).ToList());
    Assert(string.IsNullOrWhiteSpace(corruptArchive.Error),
        "corruption fixture archive succeeds");
    var corruptShard = Directory.GetFiles(
        corruptReplayRoot,
        "*.arsh",
        SearchOption.AllDirectories).Single();
    using (var stream = new FileStream(
               corruptShard,
               FileMode.Open,
               FileAccess.Write,
               FileShare.Read))
    {
        stream.SetLength(stream.Length - 7);
    }
    var reopenedCorruptReplay = new CombatFoundationReplayWarehouse(
        corruptReplayRoot);
    var corruptLoaded = reopenedCorruptReplay.Load(
        2,
        Array.Empty<string>(),
        8,
        long.MaxValue);
    Assert(corruptLoaded.Count == 0,
        "truncated replay shard is detected and ignored instead of returning partial episodes");
    var repairedCorruptArchive = reopenedCorruptReplay.Archive(
        2,
        source.Take(8).ToList());
    var repairedCorruptLoaded = new CombatFoundationReplayWarehouse(
            corruptReplayRoot)
        .Load(3, Array.Empty<string>(), 8, long.MaxValue);
    Assert(string.IsNullOrWhiteSpace(repairedCorruptArchive.Error)
           && repairedCorruptArchive.ArchivedEpisodes == 8
           && repairedCorruptLoaded.Count == 8,
        "a corrupt shard releases its episode identities so the same episodes can be re-archived and recovered after restart");

    var traversalRoot = Path.Combine(root, "traversal-replay");
    Directory.CreateDirectory(traversalRoot);
    var outsideEpisode = Episode(20_000);
    var outsidePath = Path.Combine(root, "outside-replay.json.gz");
    using (var output = File.Create(outsidePath))
    using (var gzip = new GZipStream(
               output,
               CompressionLevel.Fastest,
               leaveOpen: false))
    using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
    {
        writer.Write(JsonConvert.SerializeObject(outsideEpisode));
    }
    File.WriteAllText(
        Path.Combine(traversalRoot, "replay-index-v1.jsonl"),
        JsonConvert.SerializeObject(new
        {
            Key = CombatFoundationReplayWarehouse.StableKey(outsideEpisode),
            RelativePath = "../outside-replay.json.gz",
            Frames = 1,
            EstimatedResidentBytes = 1024,
            StoredBytes = new FileInfo(outsidePath).Length
        }) + Environment.NewLine,
        new UTF8Encoding(false));
    var traversalLoaded = new CombatFoundationReplayWarehouse(traversalRoot)
        .Load(1, Array.Empty<string>(), 1, long.MaxValue);
    Assert(traversalLoaded.Count == 0 && File.Exists(outsidePath),
        "warehouse index paths cannot escape the configured root and cleanup never touches the outside target");

    var snapshotBase = Path.Combine(root, "checkpoint-episodes.afes");
    var snapshotValues = Enumerable.Range(1, 128)
        .Select(value => "{\"episode\":" + value + ",\"padding\":\""
                         + new string('x', 256)
                         + "\"}")
        .ToArray();
    var snapshot = CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
        snapshotBase,
        snapshotValues,
        "replay-v5");
    var snapshotRoundTrip = CombatFoundationCheckpointStorage
        .ReadAndValidateJsonLines(snapshot, value => value);
    Assert(snapshot.StorageVersion == 5
           && snapshot.Path.EndsWith(".afes", StringComparison.OrdinalIgnoreCase)
           && snapshotRoundTrip.SequenceEqual(snapshotValues),
        "v5 checkpoint snapshot binary protocol round-trips ordered records");
    Assert(snapshot.Length
           < snapshotValues.Sum(value => Encoding.UTF8.GetByteCount(value)),
        "checkpoint snapshot compresses records at shard scope");

    var legacySnapshotPath = Path.Combine(root, "legacy-snapshot.jsonl");
    CombatFoundationCheckpointStorage.WriteAtomicJsonLines(
        legacySnapshotPath,
        snapshotValues.Take(3));
    var legacySnapshot = new CombatFoundationEpisodeSnapshot
    {
        StorageVersion = 4,
        Path = legacySnapshotPath,
        EpisodeCount = 3,
        Length = new FileInfo(legacySnapshotPath).Length
    };
    Assert(CombatFoundationCheckpointStorage.ReadAndValidateJsonLines(
               legacySnapshot,
               value => value).SequenceEqual(snapshotValues.Take(3)),
        "checkpoint loader remains compatible with v4 JSONL snapshots");

    var truncatedSnapshotPath = Path.Combine(root, "truncated.afes");
    File.Copy(snapshot.Path, truncatedSnapshotPath);
    using (var stream = new FileStream(
               truncatedSnapshotPath,
               FileMode.Open,
               FileAccess.Write,
               FileShare.Read))
    {
        stream.SetLength(stream.Length - 9);
    }
    var truncatedSnapshot = new CombatFoundationEpisodeSnapshot
    {
        StorageVersion = 5,
        Path = truncatedSnapshotPath,
        EpisodeCount = snapshot.EpisodeCount,
        Length = 0,
        ContentSha256 = ""
    };
    var truncatedRejected = false;
    try
    {
        _ = CombatFoundationCheckpointStorage.ReadAndValidateJsonLines(
            truncatedSnapshot,
            value => value);
    }
    catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
    {
        truncatedRejected = true;
    }
    Assert(truncatedRejected,
        "checkpoint snapshot detects a truncated binary tail without trusting external metadata");

    var excessiveCountSnapshotPath = Path.Combine(
        root,
        "excessive-record-count.afes");
    File.Copy(snapshot.Path, excessiveCountSnapshotPath);
    using (var stream = new FileStream(
               excessiveCountSnapshotPath,
               FileMode.Open,
               FileAccess.Write,
               FileShare.Read))
    using (var writer = new BinaryWriter(
               stream,
               Encoding.UTF8,
               leaveOpen: true))
    {
        stream.Position = 16L;
        writer.Write(1_000_001);
    }
    var excessiveCountRejected = false;
    try
    {
        _ = CombatFoundationCheckpointStorage.ReadAndValidateJsonLines(
            new CombatFoundationEpisodeSnapshot
            {
                StorageVersion = 5,
                Path = excessiveCountSnapshotPath,
                EpisodeCount = -1,
                Length = 0,
                ContentSha256 = ""
            },
            value => value);
    }
    catch (InvalidDataException)
    {
        excessiveCountRejected = true;
    }
    Assert(excessiveCountRejected,
        "AURAFES5 rejects an attacker-controlled recordCount above the bounded allocation limit");

    var checkpointPath = Path.Combine(root, "checkpoint.json.gz");
    var checkpointText = "{\"weights\":\"" + new string('a', 512 * 1024) + "\"}";
    CombatFoundationCheckpointStorage.WriteAtomicText(
        checkpointPath,
        checkpointText);
    var checkpointMagic = File.ReadAllBytes(checkpointPath).Take(2).ToArray();
    Assert(CombatFoundationCheckpointStorage.ReadAllTextShared(checkpointPath)
               == checkpointText
           && new FileInfo(checkpointPath).Length < checkpointText.Length / 10
           && checkpointMagic.SequenceEqual(new byte[] { 0x1F, 0x8B }),
        "checkpoint text uses gzip storage and transparent readback");
    CombatFoundationCheckpointStorage.WriteAtomicText(
        checkpointPath,
        "{\"version\":2}");
    Assert(CombatFoundationCheckpointStorage.ReadAllTextShared(
               CombatFoundationCheckpointStorage.BackupPath(checkpointPath))
               == checkpointText
           && File.ReadAllBytes(
                   CombatFoundationCheckpointStorage.BackupPath(checkpointPath))
               .Take(2)
               .SequenceEqual(new byte[] { 0x1F, 0x8B }),
        "compressed latest checkpoint replacement retains a readable previous generation");

    var catalogRoot = Path.Combine(root, "catalog-recovery");
    var catalogImmutableRoot = Path.Combine(
        catalogRoot,
        CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
    Directory.CreateDirectory(catalogImmutableRoot);
    var catalogSnapshot = CombatFoundationCheckpointStorage
        .WriteEpisodeSnapshot(
            Path.Combine(
                catalogRoot,
                CombatFoundationWorkerProtocol.CheckpointEpisodesFileName),
            new[] { JsonConvert.SerializeObject(Episode(30_000)) },
            "catalog-replay-identity");
    var immutableHistory = Path.Combine(
        catalogImmutableRoot,
        "foundation-checkpoint-history-1.json.gz");
    var catalogCheckpoint = new CombatFoundationWorkerCheckpoint
    {
        RequestFingerprint = "catalog-request",
        RulesetHash = "catalog-ruleset",
        EpisodesPath = catalogSnapshot.Path,
        EpisodeSnapshot = catalogSnapshot,
        UpdatedUtc = DateTime.UtcNow,
        Resume = new CombatCampaignFoundationResumeState
        {
            Stage = "iteration-complete",
            NextIteration = 3,
            CompletedCampaigns = 9
        }
    };
    var serializedCatalogCheckpoint = JsonConvert.SerializeObject(
        catalogCheckpoint);
    CombatFoundationCheckpointStorage.WriteAtomicText(
        immutableHistory,
        serializedCatalogCheckpoint,
        retainBackup: false);
    var originalCheckpointBytes = File.ReadAllBytes(immutableHistory);
    var catalogPath = Path.Combine(
        catalogRoot,
        CombatFoundationCheckpointCatalogProtocol.CatalogFileName);
    var catalog = new CombatFoundationCheckpointCatalog
    {
        RequestFingerprint = catalogCheckpoint.RequestFingerprint,
        RulesetHash = catalogCheckpoint.RulesetHash,
        RecommendedCheckpointId = "history-1",
        Entries = new List<CombatFoundationCheckpointCatalogEntry>
        {
            new()
            {
                Id = "history-1",
                RequestFingerprint = catalogCheckpoint.RequestFingerprint,
                RulesetHash = catalogCheckpoint.RulesetHash,
                CreatedUtc = catalogCheckpoint.UpdatedUtc,
                Stage = catalogCheckpoint.Resume.Stage,
                NextIteration = catalogCheckpoint.Resume.NextIteration,
                CompletedCampaigns =
                    catalogCheckpoint.Resume.CompletedCampaigns,
                CheckpointPath = immutableHistory,
                EpisodeSnapshotPath = catalogSnapshot.Path,
                ReplayIdentity = catalogSnapshot.ReplayIdentity,
                EpisodeCount = catalogSnapshot.EpisodeCount,
                Recommended = true
            }
        }
    };
    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        catalog,
        catalogPath);
    CombatFoundationCheckpointStorage.WriteAtomicText(
        catalogPath,
        JsonConvert.SerializeObject(catalog));
    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        catalog,
        catalogPath);
    CombatFoundationCheckpointStorage.WriteAtomicText(
        catalogPath,
        JsonConvert.SerializeObject(catalog));
    var catalogBackup = CombatFoundationCheckpointStorage.BackupPath(
        catalogPath);
    File.WriteAllText(catalogPath, "{corrupt-primary", new UTF8Encoding(false));
    var recoveredCatalog = CombatFoundationCheckpointCatalogStore.Read(
        catalogPath);
    Assert(!recoveredCatalog.RecoveryUncertain
           && recoveredCatalog.RecoveredFromBackup
           && recoveredCatalog.Catalog?.Generation > 0
           && recoveredCatalog.Catalog?.Entries.Single().Id == "history-1",
        "checkpoint catalog recovery validates the backup generation, checksum and bounded retention paths without eagerly rehashing large artifacts");

    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        recoveredCatalog.Catalog!,
        catalogPath);
    CombatFoundationCheckpointCatalogStore.WriteCatalogAtomic(
        catalogPath,
        JsonConvert.SerializeObject(recoveredCatalog.Catalog),
        recoveredCatalog);
    File.WriteAllText(
        catalogPath,
        "{corrupt-recovered-primary",
        new UTF8Encoding(false));
    var secondCatalogRecovery = CombatFoundationCheckpointCatalogStore.Read(
        catalogPath);
    Assert(secondCatalogRecovery.RecoveredFromBackup
           && !secondCatalogRecovery.RecoveryUncertain
           && secondCatalogRecovery.Catalog?.Entries.Single().Id
              == "history-1",
        "committing after backup recovery isolates the corrupt primary instead of rotating it over the only valid fallback generation");

    var customCatalogRoot = Path.Combine(root, "custom-catalog-family");
    Directory.CreateDirectory(customCatalogRoot);
    var customCatalogPath = Path.Combine(
        customCatalogRoot,
        CombatFoundationCheckpointCatalogProtocol.CatalogFileName);
    var customCatalogPriorRead = CombatFoundationCheckpointCatalogStore.Read(
        customCatalogPath);
    CombatFoundationCheckpointCatalogStore.EnsureWritableBaseline(
        customCatalogPriorRead);
    var customImmutableRoot = Path.Combine(
        customCatalogRoot,
        CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
    Directory.CreateDirectory(customImmutableRoot);
    var customSnapshot = CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
        Path.Combine(customCatalogRoot, "checkpoint-episodes.afes"),
        new[] { JsonConvert.SerializeObject(Episode(31_000)) },
        "custom-catalog-replay");
    const string customEntryId = "custom-family";
    var customImmutableCheckpoint = Path.Combine(
        customImmutableRoot,
        "foundation-checkpoint-" + customEntryId + ".json.gz");
    var customCheckpoint = new CombatFoundationWorkerCheckpoint
    {
        RequestFingerprint = "custom-catalog-request",
        RulesetHash = "custom-catalog-ruleset",
        EpisodesPath = customSnapshot.Path,
        EpisodeSnapshot = customSnapshot,
        UpdatedUtc = DateTime.UtcNow,
        Resume = new CombatCampaignFoundationResumeState
        {
            Stage = "iteration-complete",
            NextIteration = 4,
            CompletedCampaigns = 12
        }
    };
    CombatFoundationCheckpointStorage.WriteAtomicText(
        customImmutableCheckpoint,
        JsonConvert.SerializeObject(customCheckpoint),
        retainBackup: false);
    var customCatalog = new CombatFoundationCheckpointCatalog
    {
        RequestFingerprint = customCheckpoint.RequestFingerprint,
        RulesetHash = customCheckpoint.RulesetHash,
        RecommendedCheckpointId = customEntryId,
        Entries = new List<CombatFoundationCheckpointCatalogEntry>
        {
            new()
            {
                Id = customEntryId,
                RequestFingerprint = customCheckpoint.RequestFingerprint,
                RulesetHash = customCheckpoint.RulesetHash,
                CreatedUtc = customCheckpoint.UpdatedUtc,
                Stage = customCheckpoint.Resume.Stage,
                NextIteration = customCheckpoint.Resume.NextIteration,
                CompletedCampaigns =
                    customCheckpoint.Resume.CompletedCampaigns,
                CheckpointPath = customImmutableCheckpoint,
                EpisodeSnapshotPath = customSnapshot.Path,
                ReplayIdentity = customSnapshot.ReplayIdentity,
                EpisodeCount = customSnapshot.EpisodeCount,
                Recommended = true
            }
        }
    };
    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        customCatalog,
        customCatalogPath,
        customEntryId);
    CombatFoundationCheckpointCatalogStore.WriteCatalogAtomic(
        customCatalogPath,
        JsonConvert.SerializeObject(customCatalog),
        customCatalogPriorRead);
    var customCatalogRead = CombatFoundationCheckpointCatalogStore.Read(
        customCatalogPath);
    Assert(!customCatalogPriorRead.RecoveryUncertain
           && customCatalogPriorRead.Catalog == null
           && !customCatalogRead.RecoveryUncertain
           && customCatalogRead.Catalog?.Entries.Single()
               .EpisodeSnapshotPath == customSnapshot.Path,
        "a clean pre-artifact catalog baseline commits the first real custom snapshot and immutable checkpoint without misclassifying its own transaction as lost history");

    bool RejectCustomSnapshotPath(string candidatePath)
    {
        File.Copy(customSnapshot.Path, candidatePath, overwrite: true);
        var candidateCatalog = JsonConvert.DeserializeObject<
            CombatFoundationCheckpointCatalog>(
            JsonConvert.SerializeObject(customCatalog))!;
        candidateCatalog.Entries.Single().EpisodeSnapshotPath = candidatePath;
        try
        {
            CombatFoundationCheckpointCatalogStore.PrepareForWrite(
                candidateCatalog,
                customCatalogPath);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    var malformedCustomSnapshot = Path.Combine(
        customCatalogRoot,
        "checkpoint-episodes.snapshot-20261301010101000-nothexvalue1.afes");
    var nestedCustomSnapshotDirectory = Path.Combine(
        customCatalogRoot,
        "nested");
    Directory.CreateDirectory(nestedCustomSnapshotDirectory);
    var nestedCustomSnapshot = Path.Combine(
        nestedCustomSnapshotDirectory,
        Path.GetFileName(customSnapshot.Path));
    Assert(RejectCustomSnapshotPath(malformedCustomSnapshot)
           && RejectCustomSnapshotPath(nestedCustomSnapshot),
        "catalog validation rejects malformed custom snapshot generations and valid-looking snapshots below nested directories");

    File.Copy(catalogBackup, catalogPath, overwrite: true);
    var legacyCatalogPath = Path.Combine(
        catalogRoot,
        "legacy-unchecksummed-catalog.json");
    var legacyCatalog = JsonConvert.DeserializeObject<
        CombatFoundationCheckpointCatalog>(
        CombatFoundationCheckpointStorage.ReadAllTextShared(catalogPath))!;
    legacyCatalog.Generation = 0;
    legacyCatalog.ChecksumVersion = 0;
    legacyCatalog.ContentChecksumSha256 = "";
    foreach (var item in legacyCatalog.Entries)
    {
        item.CheckpointContentSha256 = "";
        item.EpisodeSnapshotContentSha256 = "";
    }
    CombatFoundationCheckpointStorage.WriteAtomicText(
        legacyCatalogPath,
        JsonConvert.SerializeObject(legacyCatalog));
    var legacyCatalogRead = CombatFoundationCheckpointCatalogStore.Read(
        legacyCatalogPath);
    Assert(legacyCatalogRead.RecoveryUncertain
           && legacyCatalogRead.CanRewriteSafely
           && legacyCatalogRead.Catalog != null,
        "legacy catalog without a checksum is recoverable for upgrade but remains recovery-uncertain and cannot authorize GC");
    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        legacyCatalogRead.Catalog!,
        legacyCatalogPath);
    CombatFoundationCheckpointStorage.WriteAtomicText(
        legacyCatalogPath,
        JsonConvert.SerializeObject(legacyCatalogRead.Catalog));
    Assert(!CombatFoundationCheckpointCatalogStore.Read(legacyCatalogPath)
               .RecoveryUncertain,
        "a deeply validated legacy catalog becomes certain only after a checksummed generation is committed and reread");

    catalogCheckpoint.Resume.CompletedCampaigns++;
    CombatFoundationCheckpointStorage.WriteAtomicText(
        immutableHistory,
        JsonConvert.SerializeObject(catalogCheckpoint),
        retainBackup: false);
    var artifactBitFlipRead = CombatFoundationCheckpointCatalogStore.Read(
        catalogPath);
    var driftedCheckpoint = JsonConvert.DeserializeObject<
        CombatFoundationWorkerCheckpoint>(
        CombatFoundationCheckpointStorage.ReadAllTextShared(
            immutableHistory))!;
    Assert(!artifactBitFlipRead.RecoveryUncertain
           && artifactBitFlipRead.Catalog != null
           && !CombatFoundationCheckpointCatalogStore
               .TryValidateSelectedImmutableCheckpoint(
                   catalogPath,
                   immutableHistory,
                   driftedCheckpoint,
                   out _),
        "a valid checksummed catalog can still authorize retention GC, while selected parseable checkpoint drift is rejected lazily by its artifact hash");
    File.WriteAllBytes(immutableHistory, originalCheckpointBytes);

    File.WriteAllText(catalogPath, "{corrupt-primary", new UTF8Encoding(false));
    File.WriteAllText(catalogBackup, "{corrupt-backup", new UTF8Encoding(false));
    var orphanHistory = Path.Combine(
        catalogImmutableRoot,
        "foundation-checkpoint-orphan.json.gz");
    File.WriteAllText(orphanHistory, "must-survive-uncertain-recovery");
    var uncertainCatalog = CombatFoundationCheckpointCatalogStore.Read(
        catalogPath);
    var cleanupInvoked = false;
    var cleanupExecuted = CombatFoundationCheckpointCatalogStore
        .ExecuteCleanupIfCertain(
            uncertainCatalog,
            () =>
            {
                cleanupInvoked = true;
                CombatFoundationCheckpointStorage.CleanupImmutableFiles(
                    catalogImmutableRoot,
                    "foundation-checkpoint-*",
                    Array.Empty<string>());
            });
    Assert(uncertainCatalog.RecoveryUncertain
           && uncertainCatalog.Catalog == null
           && !cleanupExecuted
           && !cleanupInvoked
           && File.Exists(immutableHistory)
           && File.Exists(orphanHistory),
        "actual immutable-file GC invocation is blocked when both catalog generations are corrupt");

    var missingCatalogRoot = Path.Combine(root, "missing-catalog-history");
    var missingCatalogImmutableRoot = Path.Combine(
        missingCatalogRoot,
        CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
    Directory.CreateDirectory(missingCatalogImmutableRoot);
    var missingHistoryPaths = new[]
    {
        Path.Combine(
            missingCatalogImmutableRoot,
            "foundation-checkpoint-unindexed.json.gz"),
        Path.Combine(
            missingCatalogRoot,
            CombatFoundationWorkerProtocol.CheckpointFileName),
        Path.Combine(
            missingCatalogRoot,
            CombatFoundationWorkerProtocol.CheckpointFileName + ".bak"),
        Path.Combine(
            missingCatalogRoot,
            "foundation-training-checkpoint-episodes-v12.snapshot-"
            + "20260101010101000-0123456789ab.afes")
    };
    for (var index = 0; index < missingHistoryPaths.Length; index++)
    {
        File.WriteAllBytes(
            missingHistoryPaths[index],
            Encoding.UTF8.GetBytes("historical-artifact-" + index));
        File.SetLastWriteTimeUtc(
            missingHistoryPaths[index],
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
                .AddMinutes(index));
    }
    var missingHistoryBytes = missingHistoryPaths.ToDictionary(
        path => path,
        File.ReadAllBytes,
        StringComparer.OrdinalIgnoreCase);
    var missingHistoryWriteTimes = missingHistoryPaths.ToDictionary(
        path => path,
        File.GetLastWriteTimeUtc,
        StringComparer.OrdinalIgnoreCase);
    var missingHistoryCatalogRead = CombatFoundationCheckpointCatalogStore.Read(
        Path.Combine(
            missingCatalogRoot,
            CombatFoundationCheckpointCatalogProtocol.CatalogFileName));
    var missingHistoryCleanupInvoked = false;
    var missingHistoryCleanupExecuted =
        CombatFoundationCheckpointCatalogStore.ExecuteCleanupIfCertain(
            missingHistoryCatalogRead,
            () => missingHistoryCleanupInvoked = true);
    var missingHistoryWriteRejected = false;
    try
    {
        CombatFoundationCheckpointCatalogStore.EnsureWritableBaseline(
            missingHistoryCatalogRead);
    }
    catch (InvalidDataException)
    {
        missingHistoryWriteRejected = true;
    }
    Assert(missingHistoryCatalogRead.RecoveryUncertain
           && !missingHistoryCleanupExecuted
           && !missingHistoryCleanupInvoked
           && missingHistoryWriteRejected
           && missingHistoryPaths.All(path =>
               File.ReadAllBytes(path).SequenceEqual(
                   missingHistoryBytes[path])
               && File.GetLastWriteTimeUtc(path)
                  == missingHistoryWriteTimes[path]),
        "the production catalog baseline guard rejects missing catalog plus active primary, backup, snapshot and immutable history without changing any bytes or timestamps");
    var activeV12Path = Path.Combine(
        root,
        CombatFoundationWorkerProtocol.CheckpointFileName);
    var resumeCandidates = CombatFoundationCheckpointCatalogStore
        .ResumeCandidates(activeV12Path, explicitlySelected: false);
    var legacyV11Path = Path.Combine(
        root,
        CombatFoundationWorkerProtocol.LegacyCheckpointFileName);
    Assert(resumeCandidates.SequenceEqual(
               new[]
               {
                   activeV12Path,
                   CombatFoundationCheckpointStorage.BackupPath(activeV12Path),
                   legacyV11Path,
                   CombatFoundationCheckpointStorage.BackupPath(legacyV11Path)
               },
               StringComparer.OrdinalIgnoreCase)
           && CombatFoundationCheckpointCatalogStore.ResumeCandidates(
                   activeV12Path,
                   explicitlySelected: true).Count == 1,
        "automatic resume probes v12/v11 primary and backup checkpoints in order while an explicit checkpoint selection remains exact");

    var fallbackRoot = Path.Combine(root, "resume-candidate-fallback");
    Directory.CreateDirectory(fallbackRoot);
    var fallbackV12Path = Path.Combine(
        fallbackRoot,
        CombatFoundationWorkerProtocol.CheckpointFileName);
    var fallbackV12Backup = CombatFoundationCheckpointStorage.BackupPath(
        fallbackV12Path);
    CombatFoundationWorkerCheckpoint FallbackCheckpoint(
        CombatFoundationEpisodeSnapshot episodeSnapshot,
        int completedCampaigns)
    {
        return new CombatFoundationWorkerCheckpoint
        {
            RequestFingerprint = "resume-fallback-request",
            RulesetHash = "resume-fallback-ruleset",
            EpisodesPath = episodeSnapshot.Path,
            EpisodeSnapshot = episodeSnapshot,
            UpdatedUtc = DateTime.UtcNow,
            Resume = new CombatCampaignFoundationResumeState
            {
                Stage = "iteration-complete",
                NextIteration = 2,
                CompletedCampaigns = completedCampaigns
            }
        };
    }
    void WriteFallbackCheckpoint(
        string path,
        CombatFoundationEpisodeSnapshot episodeSnapshot,
        int completedCampaigns)
    {
        CombatFoundationCheckpointStorage.WriteAtomicText(
            path,
            JsonConvert.SerializeObject(FallbackCheckpoint(
                episodeSnapshot,
                completedCampaigns)),
            retainBackup: false);
    }
    var corruptMainSnapshot = CombatFoundationCheckpointStorage
        .WriteEpisodeSnapshot(
            Path.Combine(fallbackRoot, "main-episodes.afes"),
            new[] { JsonConvert.SerializeObject(Episode(40_000)) },
            "resume-main");
    WriteFallbackCheckpoint(fallbackV12Path, corruptMainSnapshot, 10);
    using (var stream = new FileStream(
               corruptMainSnapshot.Path,
               FileMode.Open,
               FileAccess.Write,
               FileShare.Read))
    {
        stream.SetLength(stream.Length - 9L);
    }
    var validBackupSnapshot = CombatFoundationCheckpointStorage
        .WriteEpisodeSnapshot(
            Path.Combine(fallbackRoot, "backup-episodes.afes"),
            new[] { JsonConvert.SerializeObject(Episode(40_001)) },
            "resume-backup");
    WriteFallbackCheckpoint(fallbackV12Backup, validBackupSnapshot, 11);
    Assert(CombatFoundationCheckpointCatalogStore.TrySelectResumeCandidate(
               fallbackV12Path,
               explicitlySelected: false,
               out var selectedBackupPath,
               out var selectedBackupCheckpoint,
               out var selectedBackupSnapshot,
               out _)
           && string.Equals(
               selectedBackupPath,
               fallbackV12Backup,
               StringComparison.OrdinalIgnoreCase)
           && selectedBackupCheckpoint?.Resume.CompletedCampaigns == 11
           && string.Equals(
               selectedBackupSnapshot?.Path,
               validBackupSnapshot.Path,
               StringComparison.OrdinalIgnoreCase),
        "a truncated v12 primary snapshot is rejected and automatic resume selects the fully validated v12 backup once");

    using (var stream = new FileStream(
               validBackupSnapshot.Path,
               FileMode.Open,
               FileAccess.Write,
               FileShare.Read))
    {
        stream.SetLength(stream.Length - 9L);
    }
    var fallbackV11Path = Path.Combine(
        fallbackRoot,
        CombatFoundationWorkerProtocol.LegacyCheckpointFileName);
    var fallbackV11SnapshotPath = Path.Combine(
        fallbackRoot,
        "foundation-training-checkpoint-episodes-v11.snapshot-test.jsonl");
    var fallbackV11Rows = new[]
    {
        JsonConvert.SerializeObject(Episode(40_002))
    };
    CombatFoundationCheckpointStorage.WriteAtomicJsonLines(
        fallbackV11SnapshotPath,
        fallbackV11Rows);
    var fallbackV11Snapshot = new CombatFoundationEpisodeSnapshot
    {
        StorageVersion = 4,
        Path = fallbackV11SnapshotPath,
        ContentSha256 = CombatFoundationCheckpointStorage.ComputeFileSha256(
            fallbackV11SnapshotPath),
        ReplayIdentity = "resume-v11",
        EpisodeCount = fallbackV11Rows.Length,
        Length = new FileInfo(fallbackV11SnapshotPath).Length,
        CreatedUtc = DateTime.UtcNow
    };
    WriteFallbackCheckpoint(fallbackV11Path, fallbackV11Snapshot, 12);
    Assert(CombatFoundationCheckpointCatalogStore.TrySelectResumeCandidate(
               fallbackV12Path,
               explicitlySelected: false,
               out var selectedV11Path,
               out var selectedV11Checkpoint,
               out var selectedV11Snapshot,
               out _)
           && string.Equals(
               selectedV11Path,
               fallbackV11Path,
               StringComparison.OrdinalIgnoreCase)
           && selectedV11Checkpoint?.Resume.CompletedCampaigns == 12
           && selectedV11Snapshot?.StorageVersion == 4
           && !CombatFoundationCheckpointCatalogStore.TrySelectResumeCandidate(
               fallbackV12Path,
               explicitlySelected: true,
               out _,
               out _,
               out _,
               out _),
        "when both v12 generations are corrupt, automatic resume and replay discovery fall back to a validated v11 snapshot while an explicit corrupt target fails exactly");

    var rolloverRoot = Path.Combine(root, "catalog-retention-rollover");
    var rolloverImmutableRoot = Path.Combine(
        rolloverRoot,
        CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
    Directory.CreateDirectory(rolloverImmutableRoot);
    var rolloverCatalogPath = Path.Combine(
        rolloverRoot,
        CombatFoundationCheckpointCatalogProtocol.CatalogFileName);
    var rolloverSnapshotBase = Path.Combine(
        rolloverRoot,
        CombatFoundationWorkerProtocol.CheckpointEpisodesFileName);
    var rolloverCatalog = new CombatFoundationCheckpointCatalog
    {
        RequestFingerprint = "rollover-request",
        RulesetHash = "rollover-ruleset"
    };
    for (var index = 0; index < 9; index++)
    {
        var id = "rollover-" + index;
        var itemSnapshot = CombatFoundationCheckpointStorage
            .WriteEpisodeSnapshot(
                rolloverSnapshotBase,
                new[] { JsonConvert.SerializeObject(Episode(50_000 + index)) },
                "rollover-replay-" + index);
        var itemCheckpoint = new CombatFoundationWorkerCheckpoint
        {
            RequestFingerprint = rolloverCatalog.RequestFingerprint,
            RulesetHash = rolloverCatalog.RulesetHash,
            EpisodesPath = itemSnapshot.Path,
            EpisodeSnapshot = itemSnapshot,
            UpdatedUtc = DateTime.UtcNow.AddMinutes(index),
            Resume = new CombatCampaignFoundationResumeState
            {
                Stage = "iteration-complete",
                NextIteration = index + 1,
                CompletedCampaigns = index + 1
            }
        };
        var itemCheckpointPath = Path.Combine(
            rolloverImmutableRoot,
            "foundation-checkpoint-" + id + ".json.gz");
        CombatFoundationCheckpointStorage.WriteAtomicText(
            itemCheckpointPath,
            JsonConvert.SerializeObject(itemCheckpoint),
            retainBackup: false);
        rolloverCatalog.Entries.Add(new CombatFoundationCheckpointCatalogEntry
        {
            Id = id,
            RequestFingerprint = itemCheckpoint.RequestFingerprint,
            RulesetHash = itemCheckpoint.RulesetHash,
            CreatedUtc = itemCheckpoint.UpdatedUtc,
            Stage = itemCheckpoint.Resume.Stage,
            NextIteration = itemCheckpoint.Resume.NextIteration,
            CompletedCampaigns = itemCheckpoint.Resume.CompletedCampaigns,
            CheckpointPath = itemCheckpointPath,
            EpisodeSnapshotPath = itemSnapshot.Path,
            ReplayIdentity = itemSnapshot.ReplayIdentity,
            EpisodeCount = itemSnapshot.EpisodeCount
        });
        if (index == 7)
        {
            CombatFoundationCheckpointCatalogStore.PrepareForWrite(
                rolloverCatalog,
                rolloverCatalogPath);
            CombatFoundationCheckpointStorage.WriteAtomicText(
                rolloverCatalogPath,
                JsonConvert.SerializeObject(rolloverCatalog));
        }
    }
    rolloverCatalog.Entries = rolloverCatalog.Entries
        .OrderByDescending(item => item.CreatedUtc)
        .Take(CombatFoundationCheckpointCatalogProtocol.MaximumEntries)
        .ToList();
    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        rolloverCatalog,
        rolloverCatalogPath,
        "rollover-8");
    CombatFoundationCheckpointStorage.WriteAtomicText(
        rolloverCatalogPath,
        JsonConvert.SerializeObject(rolloverCatalog));
    var rolloverRetention = CombatFoundationCheckpointCatalogStore
        .ReadArtifactRetention(rolloverCatalogPath);
    CombatFoundationCheckpointStorage.CleanupImmutableFiles(
        rolloverImmutableRoot,
        "foundation-checkpoint-*",
        rolloverRetention.CheckpointPaths);
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        Path.Combine(
            rolloverRoot,
            CombatFoundationWorkerProtocol.CheckpointFileName),
        rolloverSnapshotBase,
        rolloverRetention.SnapshotPaths,
        retainNewestSnapshots: 0);
    File.WriteAllText(
        rolloverCatalogPath,
        "{corrupt-rollover-primary",
        new UTF8Encoding(false));
    var rolloverRecovered = CombatFoundationCheckpointCatalogStore.Read(
        rolloverCatalogPath);
    Assert(rolloverRetention.ValidGenerationCount == 2
           && rolloverRetention.CheckpointPaths.Count == 9
           && rolloverRetention.SnapshotPaths.Count == 9
           && rolloverRecovered.RecoveredFromBackup
           && !rolloverRecovered.RecoveryUncertain
           && rolloverRecovered.Catalog?.Entries.Count == 8,
        "catalog rollover GC retains the independently valid primary and backup generations so primary corruption remains recoverable");

    var activeRetentionRoot = Path.Combine(root, "active-backup-retention");
    Directory.CreateDirectory(activeRetentionRoot);
    var activeRetentionCheckpointPath = Path.Combine(
        activeRetentionRoot,
        CombatFoundationWorkerProtocol.CheckpointFileName);
    var activeRetentionSnapshotBase = Path.Combine(
        activeRetentionRoot,
        CombatFoundationWorkerProtocol.CheckpointEpisodesFileName);
    CombatFoundationWorkerCheckpoint ActiveRetentionCheckpoint(
        CombatFoundationEpisodeSnapshot itemSnapshot,
        int completedCampaigns)
    {
        return new CombatFoundationWorkerCheckpoint
        {
            RequestFingerprint = "active-retention-request",
            RulesetHash = "active-retention-ruleset",
            EpisodesPath = itemSnapshot.Path,
            EpisodeSnapshot = itemSnapshot,
            UpdatedUtc = DateTime.UtcNow,
            Resume = new CombatCampaignFoundationResumeState
            {
                Stage = "iteration-complete",
                NextIteration = 2,
                CompletedCampaigns = completedCampaigns
            }
        };
    }
    var backupRetainedSnapshot = CombatFoundationCheckpointStorage
        .WriteEpisodeSnapshot(
            activeRetentionSnapshotBase,
            new[] { JsonConvert.SerializeObject(Episode(60_000)) },
            "active-retention-backup");
    CombatFoundationCheckpointStorage.WriteAtomicText(
        activeRetentionCheckpointPath,
        JsonConvert.SerializeObject(ActiveRetentionCheckpoint(
            backupRetainedSnapshot,
            1)));
    _ = CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
        activeRetentionSnapshotBase,
        new[] { JsonConvert.SerializeObject(Episode(60_001)) },
        "active-retention-orphan-1");
    _ = CombatFoundationCheckpointStorage.WriteEpisodeSnapshot(
        activeRetentionSnapshotBase,
        new[] { JsonConvert.SerializeObject(Episode(60_002)) },
        "active-retention-orphan-2");
    var activePrimarySnapshot = CombatFoundationCheckpointStorage
        .WriteEpisodeSnapshot(
            activeRetentionSnapshotBase,
            new[] { JsonConvert.SerializeObject(Episode(60_003)) },
            "active-retention-primary");
    CombatFoundationCheckpointStorage.WriteAtomicText(
        activeRetentionCheckpointPath,
        JsonConvert.SerializeObject(ActiveRetentionCheckpoint(
            activePrimarySnapshot,
            2)));
    var activeSnapshotRetention = CombatFoundationCheckpointCatalogStore
        .ReadActiveSnapshotRetentionPaths(
            activeRetentionCheckpointPath,
            activeRetentionSnapshotBase);
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        activeRetentionCheckpointPath,
        activeRetentionSnapshotBase,
        activeSnapshotRetention,
        retainNewestSnapshots: 2);
    using (var stream = new FileStream(
               activePrimarySnapshot.Path,
               FileMode.Open,
               FileAccess.Write,
               FileShare.Read))
    {
        stream.SetLength(stream.Length - 9L);
    }
    Assert(File.Exists(backupRetainedSnapshot.Path)
           && activeSnapshotRetention.Contains(
               backupRetainedSnapshot.Path,
               StringComparer.OrdinalIgnoreCase)
           && CombatFoundationCheckpointCatalogStore.TrySelectResumeCandidate(
               activeRetentionCheckpointPath,
               explicitlySelected: false,
               out var activeBackupSelectedPath,
               out var activeBackupSelectedCheckpoint,
               out _,
               out _)
           && string.Equals(
               activeBackupSelectedPath,
               CombatFoundationCheckpointStorage.BackupPath(
                   activeRetentionCheckpointPath),
               StringComparison.OrdinalIgnoreCase)
           && activeBackupSelectedCheckpoint?.Resume.CompletedCampaigns == 1,
        "snapshot GC retains the descriptor-bound active checkpoint backup despite newer orphan snapshots");

    var resetRoot = Path.Combine(root, "reset-boundary");
    Directory.CreateDirectory(resetRoot);
    var resetJob = new CombatFoundationWorkerJob
    {
        CheckpointPath = Path.Combine(
            resetRoot,
            "checkpoint.json"),
        CheckpointEpisodesPath = Path.Combine(
            resetRoot,
            "checkpoint-episodes.afes"),
        CheckpointCatalogPath = Path.Combine(
            resetRoot,
            CombatFoundationCheckpointCatalogProtocol.CatalogFileName),
        ModelSelectionAnchorPath = Path.Combine(
            resetRoot,
            CombatFoundationCheckpointCatalogProtocol.SelectionAnchorFileName)
    };
    Assert(CombatFoundationCheckpointCatalogStore.TryGetResetBoundary(
               resetJob,
               out var validatedResetRoot,
               out var validatedImmutableDirectory,
               out _)
           && string.Equals(
               validatedResetRoot,
               Path.GetFullPath(resetRoot),
               StringComparison.OrdinalIgnoreCase)
           && string.Equals(
               validatedImmutableDirectory,
               Path.Combine(
                   Path.GetFullPath(resetRoot),
                   CombatFoundationCheckpointCatalogProtocol
                       .ImmutableDirectoryName),
               StringComparison.OrdinalIgnoreCase),
        "checkpoint reset accepts explicitly configured safe active leaf names while keeping fixed metadata in one non-root directory");
    Directory.CreateDirectory(validatedImmutableDirectory);
    File.WriteAllText(
        Path.Combine(
            validatedImmutableDirectory,
            "foundation-checkpoint-reset-test.json.gz"),
        "reset-me");
    var resetOutsideSentinel = Path.Combine(root, "reset-outside-sentinel.txt");
    File.WriteAllText(resetOutsideSentinel, "keep-me");
    var resetCustomSnapshot = Path.Combine(
        resetRoot,
        "checkpoint-episodes.snapshot-unreferenced.afes");
    File.WriteAllText(resetCustomSnapshot, "reset-custom-snapshot");
    var resetUnrelatedSnapshotSentinel = Path.Combine(
        resetRoot,
        "unrelated-episodes.snapshot-keep.afes");
    File.WriteAllText(resetUnrelatedSnapshotSentinel, "keep-unrelated");
    var resetNestedDirectory = Path.Combine(resetRoot, "nested");
    Directory.CreateDirectory(resetNestedDirectory);
    var resetNestedSnapshotSentinel = Path.Combine(
        resetNestedDirectory,
        "checkpoint-episodes.snapshot-nested.afes");
    File.WriteAllText(resetNestedSnapshotSentinel, "keep-nested");
    foreach (var path in new[]
             {
                 resetJob.CheckpointPath,
                 CombatFoundationCheckpointStorage.BackupPath(
                     resetJob.CheckpointPath),
                 resetJob.CheckpointEpisodesPath,
                 CombatFoundationCheckpointStorage.BackupPath(
                     resetJob.CheckpointEpisodesPath)
             })
    {
        File.WriteAllText(path, "custom-active-reset-artifact");
    }
    var resetLegacyCheckpoint = Path.Combine(
        resetRoot,
        CombatFoundationWorkerProtocol.LegacyCheckpointFileName);
    var resetLegacyEpisodes = Path.Combine(
        resetRoot,
        CombatFoundationWorkerProtocol.LegacyCheckpointEpisodesFileName);
    var resetLegacySnapshot = Path.Combine(
        resetRoot,
        Path.GetFileNameWithoutExtension(
            CombatFoundationWorkerProtocol.LegacyCheckpointEpisodesFileName)
        + ".snapshot-reset.jsonl");
    foreach (var path in new[]
             {
                 resetLegacyCheckpoint,
                 CombatFoundationCheckpointStorage.BackupPath(
                     resetLegacyCheckpoint),
                 resetLegacyEpisodes,
                 CombatFoundationCheckpointStorage.BackupPath(
                     resetLegacyEpisodes),
                 resetLegacySnapshot
             })
    {
        File.WriteAllText(path, "legacy-reset-artifact");
    }
    var resetInterrupted = false;
    try
    {
        CombatFoundationCheckpointCatalogStore.ResetCheckpointArtifacts(
            resetJob,
            () => throw new IOException("injected reset interruption"));
    }
    catch (IOException)
    {
        resetInterrupted = true;
    }
    var resetMarkerPath = CombatFoundationCheckpointCatalogStore
        .ResetMarkerPath(validatedResetRoot);
    Assert(resetInterrupted
           && File.Exists(resetMarkerPath)
           && CombatFoundationCheckpointCatalogStore.HasPendingReset(resetJob)
           && CombatFoundationCheckpointCatalogStore.ResumeCandidates(
                   resetJob.CheckpointPath,
                   explicitlySelected: false).Count == 0
           && File.Exists(resetJob.CheckpointPath)
           && Directory.Exists(validatedImmutableDirectory),
        "an interrupted reset leaves a durable matching marker that fails closed before any resume pointer or history is consumed");
    CombatFoundationCheckpointCatalogStore.ResetCheckpointArtifacts(resetJob);
    Assert(!Directory.Exists(validatedImmutableDirectory)
           && !File.Exists(resetMarkerPath)
           && !CombatFoundationCheckpointCatalogStore.HasPendingReset(resetJob)
           && File.Exists(resetOutsideSentinel),
        "checkpoint reset deletes only validated top-level immutable artifacts without recursive traversal outside the boundary");
    Assert(!File.Exists(resetJob.CheckpointPath)
           && !File.Exists(CombatFoundationCheckpointStorage.BackupPath(
               resetJob.CheckpointPath))
           && !File.Exists(resetJob.CheckpointEpisodesPath)
           && !File.Exists(CombatFoundationCheckpointStorage.BackupPath(
               resetJob.CheckpointEpisodesPath))
           && !File.Exists(resetCustomSnapshot)
           && !Directory.EnumerateFiles(
                   resetRoot,
                   "checkpoint-episodes.snapshot-*.*",
                   SearchOption.TopDirectoryOnly)
               .Any()
           && File.Exists(resetUnrelatedSnapshotSentinel)
           && File.Exists(resetNestedSnapshotSentinel),
        "checkpoint reset deletes custom active main, backup and derived top-level snapshot leaves exactly without touching unrelated families or nested files");
    Assert(!File.Exists(resetLegacyCheckpoint)
           && !File.Exists(CombatFoundationCheckpointStorage.BackupPath(
               resetLegacyCheckpoint))
           && !File.Exists(resetLegacyEpisodes)
           && !File.Exists(CombatFoundationCheckpointStorage.BackupPath(
               resetLegacyEpisodes))
           && !File.Exists(resetLegacySnapshot)
           && CombatFoundationCheckpointCatalogStore.ResumeCandidates(
                   resetJob.CheckpointPath,
                   explicitlySelected: false)
               .All(path => !File.Exists(path)),
        "checkpoint reset removes real v11 primary, backup and snapshot artifacts so automatic resume cannot resurrect stale training state");
    resetJob.CheckpointEpisodesPath = Path.Combine(
        root,
        "crafted-outside",
        CombatFoundationWorkerProtocol.CheckpointEpisodesFileName);
    Assert(!CombatFoundationCheckpointCatalogStore.TryGetResetBoundary(
               resetJob,
               out _,
               out _,
               out _),
        "checkpoint reset fails closed when a crafted job redirects any artifact to a different parent");
    resetJob.CheckpointEpisodesPath = Path.Combine(
        resetRoot,
        "checkpoint-episodes*.afes");
    Assert(!CombatFoundationCheckpointCatalogStore.TryGetResetBoundary(
               resetJob,
               out _,
               out _,
               out _)
           && File.Exists(resetUnrelatedSnapshotSentinel),
        "checkpoint reset rejects wildcard custom leaves before snapshot enumeration can widen the deletion family");
    var wildcardCleanupSentinel = Path.Combine(
        resetRoot,
        "checkpoint-episodes-victim.snapshot-keep.afes");
    File.WriteAllText(wildcardCleanupSentinel, "keep-wildcard-sibling");
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        Path.Combine(resetRoot, "checkpoint*.json"),
        resetJob.CheckpointEpisodesPath,
        Array.Empty<string>(),
        retainNewestSnapshots: 0);
    Assert(File.Exists(wildcardCleanupSentinel),
        "ordinary snapshot cleanup treats a crafted wildcard basename literally instead of widening its filesystem enumeration");
    var staleSnapshotTemporary = Path.Combine(
        resetRoot,
        "checkpoint-episodes.snapshot-stale.afes.tmp-123");
    var staleBaseEpisodesTemporary = Path.Combine(
        resetRoot,
        "checkpoint-episodes.afes.tmp-legacy");
    var checkpointTemporary = Path.Combine(
        resetRoot,
        "checkpoint.json.tmp-write");
    var checkpointNotesTemporarySentinel = Path.Combine(
        resetRoot,
        "checkpoint.json.notes.tmp-keep");
    var checkpointInvalidTokenSentinel = Path.Combine(
        resetRoot,
        "checkpoint.json.tmp-invalid.token");
    var unrelatedTemporarySentinel = Path.Combine(
        resetRoot,
        "checkpoint-episodes-notes.tmp-123");
    File.WriteAllText(staleSnapshotTemporary, "delete-stale-temp");
    File.WriteAllText(staleBaseEpisodesTemporary, "delete-legacy-temp");
    File.WriteAllText(checkpointTemporary, "delete-checkpoint-temp");
    File.WriteAllText(
        checkpointNotesTemporarySentinel,
        "keep-checkpoint-notes");
    File.WriteAllText(
        checkpointInvalidTokenSentinel,
        "keep-invalid-temp-token");
    File.WriteAllText(unrelatedTemporarySentinel, "keep-unrelated-temp");
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        Path.Combine(resetRoot, "checkpoint.json"),
        Path.Combine(resetRoot, "checkpoint-episodes.afes"),
        Array.Empty<string>(),
        retainNewestSnapshots: 0);
    Assert(!File.Exists(staleSnapshotTemporary)
           && !File.Exists(staleBaseEpisodesTemporary)
           && !File.Exists(checkpointTemporary)
           && File.Exists(checkpointNotesTemporarySentinel)
           && File.Exists(checkpointInvalidTokenSentinel)
           && File.Exists(unrelatedTemporarySentinel),
        "checkpoint cleanup removes snapshot-family and exact base-episode temporaries without deleting neighboring temporary files");
    var deleteBaseEpisodesTemporary = Path.Combine(
        resetRoot,
        "checkpoint-episodes.afes.tmp-delete");
    var deleteSnapshotTemporary = Path.Combine(
        resetRoot,
        "checkpoint-episodes.snapshot-delete.afes.tmp-delete");
    var deleteCheckpointTemporary = Path.Combine(
        resetRoot,
        "checkpoint.json.tmp-delete");
    var deleteCheckpointNotesSentinel = Path.Combine(
        resetRoot,
        "checkpoint.json.notes.tmp-delete");
    File.WriteAllText(deleteBaseEpisodesTemporary, "delete-base-temp");
    File.WriteAllText(deleteSnapshotTemporary, "delete-snapshot-temp");
    File.WriteAllText(deleteCheckpointTemporary, "delete-checkpoint-temp");
    File.WriteAllText(
        deleteCheckpointNotesSentinel,
        "keep-checkpoint-notes");
    CombatFoundationCheckpointStorage.DeleteCheckpointArtifacts(
        Path.Combine(resetRoot, "checkpoint.json"),
        Path.Combine(resetRoot, "checkpoint-episodes.afes"));
    Assert(!File.Exists(deleteBaseEpisodesTemporary)
           && !File.Exists(deleteSnapshotTemporary)
           && !File.Exists(deleteCheckpointTemporary)
           && File.Exists(deleteCheckpointNotesSentinel)
           && File.Exists(unrelatedTemporarySentinel),
        "checkpoint deletion symmetrically removes exact base-episode and snapshot temporaries while retaining neighboring sentinels");
    var shortLeafTemporary = Path.Combine(resetRoot, "a.tmp-delete");
    var shortLeafNotesSentinel = Path.Combine(
        resetRoot,
        "a.notes.tmp-keep");
    var shortLeafPrefixSentinel = Path.Combine(
        resetRoot,
        "archive.tmp-keep");
    File.WriteAllText(shortLeafTemporary, "delete-short-leaf-temp");
    File.WriteAllText(shortLeafNotesSentinel, "keep-short-leaf-notes");
    File.WriteAllText(shortLeafPrefixSentinel, "keep-short-leaf-prefix");
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        Path.Combine(resetRoot, "a"),
        Path.Combine(resetRoot, "e"),
        Array.Empty<string>(),
        retainNewestSnapshots: 0);
    Assert(!File.Exists(shortLeafTemporary)
           && File.Exists(shortLeafNotesSentinel)
           && File.Exists(shortLeafPrefixSentinel),
        "exact temporary cleanup for a short custom leaf does not widen into leaf-notes or longer sibling prefixes");
    var tempNamedSnapshotBase = Path.Combine(
        resetRoot,
        "episodes.afes.tmp-safe.afes");
    var tempNamedSnapshot = CombatFoundationCheckpointStorage
        .WriteEpisodeSnapshot(
            tempNamedSnapshotBase,
            new[] { JsonConvert.SerializeObject(Episode(14_900)) },
            "temp-named-snapshot");
    var tempNamedSnapshotTemporary = tempNamedSnapshot.Path + ".tmp-orphan";
    File.WriteAllText(
        tempNamedSnapshotTemporary,
        "delete-real-snapshot-temp");
    CombatFoundationCheckpointStorage.CleanupArtifacts(
        Path.Combine(resetRoot, "checkpoint.json"),
        tempNamedSnapshotBase,
        new[] { tempNamedSnapshot.Path },
        retainNewestSnapshots: 0);
    Assert(File.Exists(tempNamedSnapshot.Path)
           && !File.Exists(tempNamedSnapshotTemporary),
        "snapshot family cleanup retains an artifact whose safe base contains tmp while deleting its real afes temporary");

    var freshResetRoot = Path.Combine(root, "fresh-reset-anchor");
    Directory.CreateDirectory(freshResetRoot);
    var freshResetJob = new CombatFoundationWorkerJob
    {
        CheckpointPath = Path.Combine(
            freshResetRoot,
            CombatFoundationWorkerProtocol.CheckpointFileName),
        CheckpointEpisodesPath = Path.Combine(
            freshResetRoot,
            CombatFoundationWorkerProtocol.CheckpointEpisodesFileName),
        CheckpointCatalogPath = Path.Combine(
            freshResetRoot,
            CombatFoundationCheckpointCatalogProtocol.CatalogFileName),
        ModelSelectionAnchorPath = Path.Combine(
            freshResetRoot,
            CombatFoundationCheckpointCatalogProtocol.SelectionAnchorFileName),
        ResumeFromCheckpoint = false,
        ResetCheckpointOnFreshStart = true
    };
    var staleAnchorEpisode = Episode(15_000);
    freshResetJob.Request.ModelSelectionAnchorEpisodes =
        new List<CombatEpisode> { staleAnchorEpisode };
    File.WriteAllText(
        freshResetJob.ModelSelectionAnchorPath,
        JsonConvert.SerializeObject(staleAnchorEpisode) + Environment.NewLine,
        new UTF8Encoding(false));
    File.WriteAllText(freshResetJob.CheckpointCatalogPath, "stale-catalog");
    var startupResetRequired =
        CombatFoundationCheckpointCatalogStore.HasPendingReset(freshResetJob)
        || !freshResetJob.ResumeFromCheckpoint
        && freshResetJob.ResetCheckpointOnFreshStart;
    if (startupResetRequired)
    {
        CombatFoundationCheckpointCatalogStore.ResetCheckpointArtifacts(
            freshResetJob);
    }
    CombatFoundationModelSelectionAnchorStore.Load(freshResetJob);
    Assert(startupResetRequired
           && !File.Exists(freshResetJob.ModelSelectionAnchorPath)
           && freshResetJob.Request.ModelSelectionAnchorEpisodes.Count == 0
           && freshResetJob.Request.ModelSelectionAnchorCreated != null,
        "fresh startup reset invalidates the persisted anchor before the single anchor load so stale episodes cannot remain resident");
    var rebuiltAnchorEpisode = Episode(15_001);
    freshResetJob.Request.ModelSelectionAnchorEpisodes =
        new List<CombatEpisode> { rebuiltAnchorEpisode };
    freshResetJob.Request.ModelSelectionAnchorCreated!(
        freshResetJob.Request.ModelSelectionAnchorEpisodes);
    var rebuiltAnchor = File.ReadLines(freshResetJob.ModelSelectionAnchorPath)
        .Select(JsonConvert.DeserializeObject<CombatEpisode>)
        .Single();
    var rebuiltCatalog = new CombatFoundationCheckpointCatalog
    {
        RequestFingerprint = new string('a', 64),
        RulesetHash = new string('b', 64),
        SelectionAnchorPath = freshResetJob.ModelSelectionAnchorPath,
        SelectionAnchorEpisodes = 1,
        SelectionAnchorIdentity = "rebuilt-anchor"
    };
    var emptyCatalogRead = CombatFoundationCheckpointCatalogStore.Read(
        freshResetJob.CheckpointCatalogPath);
    CombatFoundationCheckpointCatalogStore.PrepareForWrite(
        rebuiltCatalog,
        freshResetJob.CheckpointCatalogPath);
    CombatFoundationCheckpointCatalogStore.WriteCatalogAtomic(
        freshResetJob.CheckpointCatalogPath,
        JsonConvert.SerializeObject(rebuiltCatalog),
        emptyCatalogRead);
    var rebuiltCatalogRead = CombatFoundationCheckpointCatalogStore.Read(
        freshResetJob.CheckpointCatalogPath);
    Assert(rebuiltAnchor?.EpisodeId == rebuiltAnchorEpisode.EpisodeId
           && rebuiltAnchor.EpisodeId != staleAnchorEpisode.EpisodeId
           && rebuiltCatalogRead.Catalog?.SelectionAnchorEpisodes == 1
           && string.Equals(
               rebuiltCatalogRead.Catalog?.SelectionAnchorIdentity,
               "rebuilt-anchor",
               StringComparison.Ordinal),
        "fresh startup can rebuild the anchor and commit a new catalog after reset without reviving stale anchor state");

    var immutableRoot = Path.Combine(root, "immutable");
    Directory.CreateDirectory(immutableRoot);
    var keep = Path.Combine(immutableRoot, "foundation-checkpoint-keep.json.gz");
    var keepWithTempId = Path.Combine(
        immutableRoot,
        "foundation-checkpoint-id.tmp-x.json.gz");
    var realImmutableTemporary = keepWithTempId + ".tmp-orphan";
    var emptyImmutableTemporarySuffix = keepWithTempId + ".tmp-";
    var keepWithEmbeddedMarker = Path.Combine(
        immutableRoot,
        "foundation-checkpoint-foo.json.gz.tmp-x.json.gz");
    var embeddedMarkerTemporary = keepWithEmbeddedMarker + ".tmp-orphan";
    var removeA = Path.Combine(immutableRoot, "foundation-checkpoint-a.json.gz");
    var removeB = Path.Combine(immutableRoot, "foundation-checkpoint-b.json.gz");
    File.WriteAllText(keep, "keep");
    File.WriteAllText(keepWithTempId, "keep-safe-tmp-id");
    File.WriteAllText(realImmutableTemporary, "delete-real-temp");
    File.WriteAllText(emptyImmutableTemporarySuffix, "keep-empty-temp-token");
    File.WriteAllText(keepWithEmbeddedMarker, "keep-embedded-marker-id");
    File.WriteAllText(
        embeddedMarkerTemporary,
        "delete-embedded-marker-temp");
    File.WriteAllText(removeA, "remove");
    File.WriteAllText(removeB, "remove");
    CombatFoundationCheckpointStorage.CleanupImmutableFiles(
        immutableRoot,
        "foundation-checkpoint-*",
        new[]
        {
            keep,
            keepWithTempId,
            realImmutableTemporary,
            emptyImmutableTemporarySuffix,
            keepWithEmbeddedMarker,
            embeddedMarkerTemporary
        });
    Assert(File.Exists(keep)
           && File.Exists(keepWithTempId)
           && !File.Exists(realImmutableTemporary)
           && File.Exists(emptyImmutableTemporarySuffix)
           && File.Exists(keepWithEmbeddedMarker)
           && !File.Exists(embeddedMarkerTemporary)
           && !File.Exists(removeA)
           && !File.Exists(removeB),
        "immutable cleanup retains safe ids containing tmp or an embedded json-gz tmp marker, removes only real json-gz temporaries with valid tokens, and enforces catalog retention");

    void RunTransformerSafetyTests()
    {
    var seedFailure = CombatTransformerTeacherProcessFailureClassifier.Classify(
        "ValueError: Seed must be between 0 and 2**32 - 1");
    var protocolFailure =
        CombatTransformerTeacherProcessFailureClassifier.Classify(
            "train_teacher.py: error: unrecognized arguments: --obsolete");
    var cudaOutOfMemory =
        CombatTransformerTeacherProcessFailureClassifier.Classify(
            "torch.OutOfMemoryError: CUDA out of memory");
    var dataDependentFailure =
        CombatTransformerTeacherProcessFailureClassifier.Classify(
            "json.decoder.JSONDecodeError: malformed row in dataset");
    Assert(seedFailure.FailureKind
               == CombatTransformerTeacherFailureKinds.Configuration
           && seedFailure.FormalModelBlocked
           && !seedFailure.Retryable
           && protocolFailure.FailureKind
              == CombatTransformerTeacherFailureKinds.Protocol
           && protocolFailure.FormalModelBlocked
           && !protocolFailure.Retryable
           && cudaOutOfMemory.FailureKind
              == CombatTransformerTeacherFailureKinds.TransientResource
           && !cudaOutOfMemory.FormalModelBlocked
           && cudaOutOfMemory.Retryable
           && dataDependentFailure.FailureKind
              == CombatTransformerTeacherFailureKinds.Process
           && !dataDependentFailure.FormalModelBlocked
           && dataDependentFailure.Retryable,
        "Transformer process failure classification blocks repeatable seed and CLI faults while leaving CUDA OOM and data-dependent failures retryable");
    var transformerCorpusRoot = Path.Combine(root, "transformer-corpus");
    Directory.CreateDirectory(transformerCorpusRoot);
    var transformerCorpusPath = Path.Combine(
        transformerCorpusRoot,
        "world-model-corpus-v4.sparse.jsonl");
    var transformerIndexPath = Path.Combine(
        transformerCorpusRoot,
        "corpus-identities-v1.txt");
    var transformerManifestPath = Path.Combine(
        transformerCorpusRoot,
        "corpus-manifest-v1.json");
    const string transformerCompatibilityKey = "TEST-COMPATIBILITY";
    var transformerIdentities = new[] { "journey:a|0|1|1|state-a" };
    var transformerRows = new[]
    {
        JsonConvert.SerializeObject(new
        {
            I = 0,
            E = 0,
            Y = "journey:a",
            F = 0,
            T = 1,
            Q = 1,
            D = transformerIdentities[0],
            L = "strategy-baseline",
            C = "normal",
            B = 0,
            J = 1,
            S = new { D = 4, I = new[] { 0 }, V = new[] { 1f } },
            O = Array.Empty<object>(),
            A = new[]
            {
                new { D = 4, I = new[] { 1 }, V = new[] { 1f } }
            },
            P = new[] { 1d },
            X = 0,
            V = 1d,
            G = 0,
            K = 1d,
            N = new { D = 4, I = Array.Empty<int>(), V = Array.Empty<float>() },
            M = 0,
            W = 1d,
            R = 0d,
            H = 1d,
            U = 0d,
            Z = 1
        })
    };
    File.WriteAllLines(
        transformerCorpusPath,
        transformerRows,
        new UTF8Encoding(false));
    File.WriteAllLines(
        transformerIndexPath,
        transformerIdentities,
        new UTF8Encoding(false));
    string Sha256Text(string value)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
    string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    void WriteTransformerManifest(
        IReadOnlyList<string>? identities = null,
        IReadOnlyList<string>? rows = null)
    {
        identities ??= transformerIdentities;
        rows ??= transformerRows;
        File.WriteAllText(
            transformerManifestPath,
            JsonConvert.SerializeObject(new
            {
                Protocol = CombatTransformerTeacherCorpusProtocol.Version,
                CompatibilityKey = transformerCompatibilityKey,
                FrameCount = rows.Count,
                Fingerprint = Sha256Text(string.Join("\n", identities)),
                StrategyFrames = new Dictionary<string, int>
                {
                    ["strategy-baseline"] = rows.Count
                },
                ContentLengthBytes = new FileInfo(transformerCorpusPath).Length,
                ContentSha256 = Sha256File(transformerCorpusPath),
                UpdatedUtc = DateTime.UtcNow
            }, Formatting.Indented),
            new UTF8Encoding(false));
    }
    WriteTransformerManifest();
    Assert(PythonCombatTransformerTeacher.IsPersistedCorpusSnapshotValidForTests(
               transformerCorpusPath,
               transformerIndexPath,
               transformerManifestPath,
               transformerCompatibilityKey),
        "Transformer corpus incremental exclusion requires sidecars that match the actual corpus bytes and identities");
    var transformerCorpusBackup = transformerCorpusPath + ".backup";
    File.Move(transformerCorpusPath, transformerCorpusBackup);
    Assert(!PythonCombatTransformerTeacher.IsPersistedCorpusSnapshotValidForTests(
               transformerCorpusPath,
               transformerIndexPath,
               transformerManifestPath,
               transformerCompatibilityKey),
        "a missing Transformer corpus cannot be hidden by an intact manifest and identity index");
    File.Move(transformerCorpusBackup, transformerCorpusPath);
    File.AppendAllText(
        transformerCorpusPath,
        "{interrupted",
        new UTF8Encoding(false));
    Assert(!PythonCombatTransformerTeacher.IsPersistedCorpusSnapshotValidForTests(
               transformerCorpusPath,
               transformerIndexPath,
               transformerManifestPath,
               transformerCompatibilityKey),
        "a truncated Transformer corpus invalidates otherwise matching sidecars");
    File.WriteAllLines(
        transformerCorpusPath,
        transformerRows,
        new UTF8Encoding(false));
    File.WriteAllText(
        transformerIndexPath,
        "stale-identity" + Environment.NewLine,
        new UTF8Encoding(false));
    Assert(!PythonCombatTransformerTeacher.IsPersistedCorpusSnapshotValidForTests(
               transformerCorpusPath,
               transformerIndexPath,
               transformerManifestPath,
               transformerCompatibilityKey),
        "a stale Transformer identity index cannot exclude frames from a valid corpus");

    File.WriteAllLines(
        transformerIndexPath,
        transformerIdentities,
        new UTF8Encoding(false));
    WriteTransformerManifest();
    var firstGenerationCorpus = PythonCombatTransformerTeacher
        .ResolveOrMigratePersistedCorpusSnapshotForTests(
            transformerCorpusRoot,
            transformerCompatibilityKey);
    Assert(firstGenerationCorpus != null
           && !string.Equals(
               Path.GetFullPath(firstGenerationCorpus),
               Path.GetFullPath(transformerCorpusPath),
               StringComparison.OrdinalIgnoreCase)
           && PythonCombatTransformerTeacher
               .IsPersistedCorpusSnapshotValidForTests(
                   firstGenerationCorpus,
                   Path.Combine(
                       Path.GetDirectoryName(firstGenerationCorpus)!,
                       "corpus-identities-v1.txt"),
                   Path.Combine(
                       Path.GetDirectoryName(firstGenerationCorpus)!,
                       "corpus-manifest-v1.json"),
                   transformerCompatibilityKey),
        "a valid legacy Transformer triplet is automatically committed as the first production generation even before any new frames are exported");
    var firstGenerationDirectory = Path.GetDirectoryName(
        firstGenerationCorpus!)!;
    var transformerGenerationRoot = Path.GetDirectoryName(
        firstGenerationDirectory)!;
    var firstGenerationName = Path.GetFileName(firstGenerationDirectory);
    var foreignGenerationDirectory = Path.Combine(
        transformerGenerationRoot,
        "user-notes");
    Directory.CreateDirectory(foreignGenerationDirectory);
    foreach (var generationFileName in new[]
             {
                 "world-model-corpus-v4.sparse.jsonl",
                 "corpus-identities-v1.txt",
                 "corpus-manifest-v1.json"
             })
    {
        File.Copy(
            Path.Combine(firstGenerationDirectory, generationFileName),
            Path.Combine(foreignGenerationDirectory, generationFileName));
    }
    var foreignGenerationSentinel = Path.Combine(
        foreignGenerationDirectory,
        "important.txt");
    File.WriteAllText(
        foreignGenerationSentinel,
        "preserve",
        new UTF8Encoding(false));
    File.WriteAllText(
        Path.Combine(transformerCorpusRoot, "active-generation-v1.json"),
        JsonConvert.SerializeObject(
            new
            {
                Protocol =
                    "aura.transformer-teacher-corpus-generation-pointer.v1",
                CompatibilityKey = transformerCompatibilityKey,
                Generation = "user-notes"
            },
            Formatting.Indented),
        new UTF8Encoding(false));
    var resolvedAfterForeignPointer = PythonCombatTransformerTeacher
        .ResolveOrMigratePersistedCorpusSnapshotForTests(
            transformerCorpusRoot,
            transformerCompatibilityKey);
    var abandonedFlatStaging = Path.Combine(
        transformerGenerationRoot,
        ".staging-" + firstGenerationName);
    Directory.CreateDirectory(abandonedFlatStaging);
    File.WriteAllText(
        Path.Combine(abandonedFlatStaging, "partial-corpus.tmp"),
        "interrupted",
        new UTF8Encoding(false));
    var malformedStaging = Path.Combine(
        transformerGenerationRoot,
        ".staging-not-a-generation");
    Directory.CreateDirectory(malformedStaging);
    File.WriteAllText(
        Path.Combine(malformedStaging, "sentinel.txt"),
        "keep",
        new UTF8Encoding(false));
    var nestedStagingGeneration = DateTime.UtcNow.AddSeconds(1).ToString(
                                      "yyyyMMddTHHmmssfffffff",
                                      System.Globalization.CultureInfo
                                          .InvariantCulture)
                                  + "-"
                                  + Guid.NewGuid().ToString("N");
    var nestedStaging = Path.Combine(
        transformerGenerationRoot,
        ".staging-" + nestedStagingGeneration);
    Directory.CreateDirectory(Path.Combine(nestedStaging, "nested"));
    var resolvedAfterStagingCleanup = PythonCombatTransformerTeacher
        .ResolveOrMigratePersistedCorpusSnapshotForTests(
            transformerCorpusRoot,
            transformerCompatibilityKey);
    Assert(!Directory.Exists(abandonedFlatStaging)
           && Directory.Exists(malformedStaging)
           && Directory.Exists(nestedStaging)
           && string.Equals(
               resolvedAfterStagingCleanup,
               firstGenerationCorpus,
               StringComparison.OrdinalIgnoreCase),
        "production recovery deletes only strict flat crash-staging directories and refuses malformed or nested directories");
    const string replacementIdentity = "journey:b|0|1|1|state-b";
    var replacementRows = transformerRows
        .Select(row => row.Replace(
            transformerIdentities[0],
            replacementIdentity,
            StringComparison.Ordinal))
        .ToArray();
    File.WriteAllLines(
        transformerCorpusPath,
        replacementRows,
        new UTF8Encoding(false));
    File.WriteAllLines(
        transformerIndexPath,
        new[] { replacementIdentity },
        new UTF8Encoding(false));
    WriteTransformerManifest(new[] { replacementIdentity }, replacementRows);
    var interruptedGenerationCorpus = PythonCombatTransformerTeacher
        .PublishPersistedCorpusSnapshotForTests(
            transformerCorpusRoot,
            transformerCorpusPath,
            transformerIndexPath,
            transformerManifestPath,
            transformerCompatibilityKey);
    File.WriteAllText(
        Path.Combine(
            Path.GetDirectoryName(interruptedGenerationCorpus)!,
            "corpus-identities-v1.txt"),
        "interrupted-sidecar",
        new UTF8Encoding(false));
    const string latestIdentity = "journey:c|0|1|1|state-c";
    var latestRows = transformerRows
        .Select(row => row.Replace(
            transformerIdentities[0],
            latestIdentity,
            StringComparison.Ordinal))
        .ToArray();
    File.WriteAllLines(
        transformerCorpusPath,
        latestRows,
        new UTF8Encoding(false));
    File.WriteAllLines(
        transformerIndexPath,
        new[] { latestIdentity },
        new UTF8Encoding(false));
    WriteTransformerManifest(new[] { latestIdentity }, latestRows);
    var latestGenerationCorpus = PythonCombatTransformerTeacher
        .PublishPersistedCorpusSnapshotForTests(
            transformerCorpusRoot,
            transformerCorpusPath,
            transformerIndexPath,
            transformerManifestPath,
            transformerCompatibilityKey);
    File.WriteAllText(
        Path.Combine(
            Path.GetDirectoryName(latestGenerationCorpus)!,
            "corpus-identities-v1.txt"),
        "latest-interrupted-sidecar",
        new UTF8Encoding(false));
    var recoveredGenerationCorpus = PythonCombatTransformerTeacher
        .ResolveOrMigratePersistedCorpusSnapshotForTests(
            transformerCorpusRoot,
            transformerCompatibilityKey);
    Assert(string.Equals(
               recoveredGenerationCorpus,
               firstGenerationCorpus,
               StringComparison.OrdinalIgnoreCase)
           && File.ReadAllText(recoveredGenerationCorpus!, Encoding.UTF8)
               .Contains(transformerIdentities[0], StringComparison.Ordinal)
           && !File.ReadAllText(recoveredGenerationCorpus!, Encoding.UTF8)
               .Contains(replacementIdentity, StringComparison.Ordinal)
           && !File.ReadAllText(recoveredGenerationCorpus!, Encoding.UTF8)
               .Contains(latestIdentity, StringComparison.Ordinal)
           && PythonCombatTransformerTeacher
               .IsPersistedCorpusSnapshotValidForTests(
                   recoveredGenerationCorpus!,
                   Path.Combine(
                       Path.GetDirectoryName(recoveredGenerationCorpus!)!,
                       "corpus-identities-v1.txt"),
                   Path.Combine(
                       Path.GetDirectoryName(recoveredGenerationCorpus!)!,
                       "corpus-manifest-v1.json"),
                   transformerCompatibilityKey),
        "invalid ordinary generations do not consume the rollback retention slot, and a torn active Transformer generation recovers the previous complete accumulated corpus");
    Assert(string.Equals(
               resolvedAfterForeignPointer,
               firstGenerationCorpus,
               StringComparison.OrdinalIgnoreCase)
           && File.Exists(foreignGenerationSentinel)
           && string.Equals(
               File.ReadAllText(foreignGenerationSentinel, Encoding.UTF8),
               "preserve",
               StringComparison.Ordinal),
        "Transformer corpus recovery and pruning ignore valid-looking directories that do not use the strict generation protocol name");

    var backlogCorpusRoot = Path.Combine(root, "transformer-backlog");
    var backlogSourceRows = Enumerable.Range(0, 80)
        .Select(index => JsonConvert.SerializeObject(new
        {
            I = index,
            D = "backlog-identity-" + index.ToString("D3"),
            Y = "backlog-run-" + (index / 4).ToString("D2"),
            L = "strategy-baseline",
            C = index % 8 == 0 ? "advanced" : "normal",
            B = index / 4,
            J = index % 2,
            OK = 1,
            DK = 1,
            SK = 0,
            M = 1,
            S = new { D = 4, I = new[] { 0 }, V = new[] { 1f } }
        }))
        .ToArray();
    Dictionary<string, string> ReadBacklogPartition(string path)
    {
        return File.ReadLines(path, Encoding.UTF8)
            .Select(line => JsonConvert.DeserializeObject<
                Dictionary<string, object>>(line)!)
            .ToDictionary(
                row => Convert.ToString(row["D"],
                           System.Globalization.CultureInfo.InvariantCulture)!,
                row => Convert.ToString(row["Y"],
                           System.Globalization.CultureInfo.InvariantCulture)!,
                StringComparer.Ordinal);
    }
    var firstBacklogMerge = PythonCombatTransformerTeacher
        .MergeCorpusRowsForTests(
            backlogCorpusRoot,
            backlogSourceRows,
            maximumFrames: 64);
    var firstActiveRows = ReadBacklogPartition(firstBacklogMerge.CorpusPath);
    var firstBacklogRows = ReadBacklogPartition(firstBacklogMerge.BacklogPath);
    var firstAllRows = firstActiveRows.Concat(firstBacklogRows)
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    var firstRunsAreWhole = firstAllRows
        .GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .All(group => group.Count() == 4
                      && (group.All(pair => firstActiveRows.ContainsKey(pair.Key))
                          || group.All(pair => firstBacklogRows.ContainsKey(pair.Key))));
    var secondBacklogMerge = PythonCombatTransformerTeacher
        .MergeCorpusRowsForTests(
            backlogCorpusRoot,
            Array.Empty<string>(),
            maximumFrames: 64,
            trainedIdentities: firstActiveRows.Keys.ToHashSet(
                StringComparer.Ordinal),
            existingCorpusPath: firstBacklogMerge.CorpusPath,
            existingBacklogPath: firstBacklogMerge.BacklogPath);
    var secondActiveRows = ReadBacklogPartition(secondBacklogMerge.CorpusPath);
    var secondBacklogRows = ReadBacklogPartition(secondBacklogMerge.BacklogPath);
    Assert(firstBacklogMerge.ActiveFrames == 64
           && firstBacklogMerge.BacklogFrames == 16
           && firstBacklogMerge.DroppedFrames == 0
           && firstActiveRows.Keys.Intersect(
                   firstBacklogRows.Keys,
                   StringComparer.Ordinal).Count() == 0
           && firstAllRows.Count == backlogSourceRows.Length
           && firstRunsAreWhole
           && secondBacklogMerge.ActiveFrames == 64
           && secondBacklogMerge.BacklogFrames == 16
           && secondBacklogMerge.DroppedFrames == 0
           && secondActiveRows.Keys.Intersect(
                   firstBacklogRows.Keys,
                   StringComparer.Ordinal).Any()
           && secondActiveRows.Concat(secondBacklogRows)
               .Select(pair => pair.Key)
               .ToHashSet(StringComparer.Ordinal)
               .SetEquals(firstAllRows.Keys),
        "Transformer capacity overflow persists complete Journey runs in a lossless backlog and rotates pending backlog rows into the active window after trained watermarks advance");

    var transformerCommitRoot = Path.Combine(root, "transformer-commit");
    Directory.CreateDirectory(transformerCommitRoot);
    var sourceModel = Path.Combine(transformerCommitRoot, "source-model.pt");
    var sourceReport = Path.Combine(transformerCommitRoot, "source-report.json");
    var acceptedSourceReport = JsonConvert.SerializeObject(
        new CombatTransformerTeacherReport
        {
            Applied = true,
            TeacherGeneration = 1
        });
    File.WriteAllText(sourceModel, "accepted-model", new UTF8Encoding(false));
    File.WriteAllText(
        sourceReport,
        acceptedSourceReport,
        new UTF8Encoding(false));
    var expectedTeacherCompatibilityKey = new string('A', 64);
    var incompatibleTeacherCompatibilityKey = new string('B', 64);
    Assert(!PythonCombatTransformerTeacher
               .HasAcceptedTeacherArtifactForWarmStart(
                   sourceModel,
                   new CombatTransformerTeacherReport
                   {
                       Applied = true,
                       TeacherGeneration = 0,
                       TeacherCompatibilityKey =
                           expectedTeacherCompatibilityKey
                   },
                   expectedTeacherCompatibilityKey)
           && !PythonCombatTransformerTeacher
               .HasAcceptedTeacherArtifactForWarmStart(
                   sourceModel,
                   new CombatTransformerTeacherReport
                   {
                       Applied = true,
                       TeacherGeneration = 1,
                       TeacherCompatibilityKey =
                           incompatibleTeacherCompatibilityKey
                   },
                   expectedTeacherCompatibilityKey)
           && PythonCombatTransformerTeacher
               .HasAcceptedTeacherArtifactForWarmStart(
                   sourceModel,
                   new CombatTransformerTeacherReport
                   {
                       Applied = true,
                       TeacherGeneration = 1,
                       TeacherCompatibilityKey =
                           expectedTeacherCompatibilityKey
                   },
                   expectedTeacherCompatibilityKey),
        "Worker warm-start selection requires an applied teacher artifact with a positive accepted generation bound to the current teacher compatibility key");
    var failedModelDestination = Path.Combine(
        transformerCommitRoot,
        "failed-persistent-model.pt");
    var failedReportDestination = Path.Combine(
        transformerCommitRoot,
        "report-destination-is-directory");
    Directory.CreateDirectory(failedReportDestination);
    var failedWatermark = Path.Combine(
        transformerCommitRoot,
        "failed-trained-identities.txt");
    var artifactCommitFailed = false;
    try
    {
        _ = PythonCombatTransformerTeacher
            .CommitTeacherArtifactsAndTrainingWatermark(
                sourceModel,
                sourceReport,
                failedModelDestination,
                failedReportDestination,
                failedWatermark,
                transformerIdentities,
                out _);
    }
    catch (Exception exception) when (
        exception is IOException or UnauthorizedAccessException)
    {
        artifactCommitFailed = true;
    }
    Assert(artifactCommitFailed
           && File.Exists(failedModelDestination)
           && !File.Exists(failedWatermark),
        "Transformer artifact publication failure leaves the training watermark untouched for conservative retry");

    var durableModel = Path.Combine(transformerCommitRoot, "persistent-model.pt");
    var durableReport = Path.Combine(transformerCommitRoot, "persistent-report.json");
    var blockedWatermarkParent = Path.Combine(
        transformerCommitRoot,
        "watermark-parent-is-file");
    File.WriteAllText(
        blockedWatermarkParent,
        "blocked",
        new UTF8Encoding(false));
    var watermarkAdvanced = PythonCombatTransformerTeacher
        .CommitTeacherArtifactsAndTrainingWatermark(
            sourceModel,
            sourceReport,
            durableModel,
            durableReport,
            Path.Combine(blockedWatermarkParent, "trained-identities.txt"),
            transformerIdentities,
            out var watermarkError);
    Assert(!watermarkAdvanced
           && !string.IsNullOrWhiteSpace(watermarkError)
           && File.ReadAllText(durableModel) == "accepted-model"
           && File.ReadAllText(durableReport) == acceptedSourceReport,
        "Transformer watermark failure is non-destructive after both durable teacher artifacts commit");
    var successfulWatermark = Path.Combine(
        transformerCommitRoot,
        "trained-identities.txt");
    Assert(PythonCombatTransformerTeacher
               .CommitTeacherArtifactsAndTrainingWatermark(
                   sourceModel,
                   sourceReport,
                   durableModel,
                   durableReport,
                   successfulWatermark,
                   transformerIdentities,
                   out var successfulWatermarkError)
           && string.IsNullOrEmpty(successfulWatermarkError)
           && File.ReadAllLines(successfulWatermark)
               .SequenceEqual(transformerIdentities),
        "Transformer training watermark advances only after both durable teacher artifacts commit");
    }
    RunTransformerSafetyTests();

    Console.WriteLine(
        "Replay shard microbenchmark: episodes="
        + source.Count
        + ", shards="
        + shardPaths.Length
        + ", elapsedMs="
        + clock.Elapsed.TotalMilliseconds.ToString("F1")
        + ", episodesPerSecond="
        + (source.Count / Math.Max(0.001, clock.Elapsed.TotalSeconds))
            .ToString("F1"));
    Console.WriteLine(
        "AuraFoundationTrainer.Worker.Tests passed: "
        + assertions
        + " assertions.");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}
