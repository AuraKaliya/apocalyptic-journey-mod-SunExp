using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.Worker;

public sealed class FoundationArtifactBundleResult
{
    public bool ModelProduced { get; set; }

    public string BundleDirectory { get; set; } = "";

    public string ManifestPath { get; set; } = "";

    public string CandidateModelPath { get; set; } = "";

    public string CapabilityReportPath { get; set; } = "";

    public string CapabilityReportHtmlPath { get; set; } = "";

    public string SimulationDatabasePath { get; set; } = "";

    public string SeedRegistryPath { get; set; } = "";

    public string ModelNodeGraphPath { get; set; } = "";
}

public sealed class FoundationArtifactBundleManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ArtifactKind { get; set; } =
        "aura.foundation-training-artifact-bundle.v1";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string JobId { get; set; } = "";

    public string CompletionKind { get; set; } = "";

    public bool TrainingSucceeded { get; set; }

    public bool DeploymentEligible { get; set; }

    public string EvaluatedModelId { get; set; } = "";

    public int EvaluatedModelIteration { get; set; }

    public string CandidateModelManifest { get; set; } = "";

    public string CandidateModelWeights { get; set; } = "";

    public string CapabilityReport { get; set; } = "";

    public string CapabilityReportHtml { get; set; } = "";

    public string SimulationDatabase { get; set; } = "";

    public string SeedRegistry { get; set; } = "";

    public string ModelNodeGraph { get; set; } = "";

    public string DeploymentModelPackage { get; set; } = "";

    public string DeploymentModelWeights { get; set; } = "";

    public Dictionary<string, string> Sha256 { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class FoundationBoundaryArtifactManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ArtifactKind { get; set; } =
        "aura.foundation-training-boundary-snapshot.v1";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string JobId { get; set; } = "";

    public string CompletionKind { get; set; } = "iteration-boundary";

    public int CompletedIterations { get; set; }

    public int NextIteration { get; set; }

    public string CandidateModelManifest { get; set; } = "";

    public string CandidateModelWeights { get; set; } = "";

    public string ModelNodeGraph { get; set; } = "";

    public Dictionary<string, string> Sha256 { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class FoundationCandidateModelManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ArtifactKind { get; set; } =
        "aura.foundation-training-candidate-model.v1";

    public string ModelId { get; set; } = "";

    public int SourceIteration { get; set; }

    public bool DeploymentEligible { get; set; }

    public string AcceptanceKind { get; set; } = "";

    public string SelectionReason { get; set; } = "";

    public CombatPolicyValueArtifactManifest? Artifact { get; set; }
}

public sealed class FoundationSeedTag
{
    public int SchemaVersion { get; set; } = 1;

    public string Protocol { get; set; } = "aura.foundation-seed-tag.v1";

    public string Source { get; set; } = "";

    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public string Tag { get; set; } = "";

    public double Priority { get; set; }

    public string Reason { get; set; } = "";

    public string Key => Source + "|" + DifficultyId + "|" + WorldSeed + "|" + Tag;
}

public static class FoundationArtifactBundleWriter
{
    public const string DirectoryName = "training-artifacts-v1";
    public const string ManifestFileName = "artifact-manifest-v1.json";
    public const string ReportFileName = "model-capability-report-v1.json";
    public const string ReportHtmlFileName = "model-capability-report-v1.html";
    public const string DatabaseFileName = "simulation-process-v1.sqlite";
    public const string SeedRegistryFileName = "seed-registry-v1.jsonl";
    public const string ModelNodeGraphFileName = "model-node-graph-v1.json";
    public const string LiveDirectoryName = ".live";
    public const string BoundaryManifestFileName =
        "boundary-snapshot-manifest-v1.json";

    public static FoundationArtifactBundleResult Write(
        CombatFoundationWorkerJob job,
        CombatCampaignFoundationTrainingResult training,
        CombatFoundationTrainingAnalysis trainingAnalysis,
        string completionKind)
    {
        return WriteTerminalBundle(
            job,
            training,
            trainingAnalysis,
            completionKind);
    }

    public static FoundationArtifactBundleResult WriteBoundarySnapshot(
        CombatFoundationWorkerJob job,
        CombatCampaignFoundationTrainingResult training)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(training);

        var bundleDirectory = Path.Combine(job.ResultDirectory, DirectoryName);
        var liveDirectory = Path.Combine(bundleDirectory, LiveDirectoryName);
        var modelDirectory = Path.Combine(liveDirectory, "model");
        Directory.CreateDirectory(modelDirectory);

        var selected = SelectModel(training);
        var modelManifestPath = "";
        var modelWeightsPath = "";
        if (selected.Model != null)
        {
            modelWeightsPath = Path.Combine(
                modelDirectory,
                "candidate-model-weights-v1.bin");
            var artifact = CombatPolicyValueArtifactProtocol.Write(
                modelWeightsPath,
                selected.Model);
            modelManifestPath = Path.Combine(
                modelDirectory,
                "candidate-model-v1.json");
            WriteJson(
                modelManifestPath,
                new FoundationCandidateModelManifest
                {
                    ModelId = selected.Model.ModelId,
                    SourceIteration = selected.Iteration,
                    DeploymentEligible = false,
                    AcceptanceKind = training.AcceptanceKind,
                    SelectionReason = selected.Reason,
                    Artifact = artifact
                });
        }

        var modelNodeGraphPath = Path.Combine(
            liveDirectory,
            ModelNodeGraphFileName);
        WriteJson(modelNodeGraphPath, BuildModelNodeGraph(training, selected));

        var manifestPath = Path.Combine(
            liveDirectory,
            BoundaryManifestFileName);
        var manifest = new FoundationBoundaryArtifactManifest
        {
            JobId = job.JobId,
            CompletedIterations = training.Iterations.Count,
            NextIteration = training.NextIteration,
            CandidateModelManifest = Relative(
                liveDirectory,
                modelManifestPath),
            CandidateModelWeights = Relative(
                liveDirectory,
                modelWeightsPath),
            ModelNodeGraph = Relative(liveDirectory, modelNodeGraphPath)
        };
        foreach (var path in new[]
                 {
                     modelManifestPath,
                     modelWeightsPath,
                     modelNodeGraphPath
                 }.Where(File.Exists))
        {
            manifest.Sha256[Relative(liveDirectory, path)] = HashFile(path);
        }
        WriteJson(manifestPath, manifest);

        return new FoundationArtifactBundleResult
        {
            ModelProduced = selected.Model != null,
            BundleDirectory = bundleDirectory,
            ManifestPath = manifestPath,
            CandidateModelPath = modelManifestPath,
            ModelNodeGraphPath = modelNodeGraphPath
        };
    }

    public static FoundationArtifactBundleResult WriteTerminalBundle(
        CombatFoundationWorkerJob job,
        CombatCampaignFoundationTrainingResult training,
        CombatFoundationTrainingAnalysis trainingAnalysis,
        string completionKind)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(training);
        ArgumentNullException.ThrowIfNull(trainingAnalysis);

        var bundleDirectory = Path.Combine(job.ResultDirectory, DirectoryName);
        var modelDirectory = Path.Combine(bundleDirectory, "model");
        Directory.CreateDirectory(modelDirectory);
        foreach (var staleModelArtifact in new[]
                 {
                     Path.Combine(modelDirectory, "candidate-model-v1.json"),
                     Path.Combine(modelDirectory, "candidate-model-weights-v1.bin"),
                     Path.Combine(
                         modelDirectory,
                         CombatFoundationModelPackageProtocol.FileName),
                     Path.Combine(
                         modelDirectory,
                         CombatFoundationModelPackageProtocol.WeightsFileName)
                 })
        {
            File.Delete(staleModelArtifact);
        }

        var selected = SelectModel(training);
        var modelManifestPath = "";
        var modelWeightsPath = "";
        if (selected.Model != null)
        {
            modelWeightsPath = Path.Combine(
                modelDirectory,
                "candidate-model-weights-v1.bin");
            var artifact = CombatPolicyValueArtifactProtocol.Write(
                modelWeightsPath,
                selected.Model);
            modelManifestPath = Path.Combine(
                modelDirectory,
                "candidate-model-v1.json");
            WriteJson(
                modelManifestPath,
                new FoundationCandidateModelManifest
                {
                    ModelId = selected.Model.ModelId,
                    SourceIteration = selected.Iteration,
                    DeploymentEligible = training.AcceptancePassed,
                    AcceptanceKind = training.AcceptanceKind,
                    SelectionReason = selected.Reason,
                    Artifact = artifact
                });
        }

        var modelNodeGraphPath = Path.Combine(
            bundleDirectory,
            ModelNodeGraphFileName);
        WriteJson(modelNodeGraphPath, BuildModelNodeGraph(training, selected));

        var seeds = BuildSeedTags(training);
        seeds = MergeCumulativeSeedRegistry(job, seeds);
        var seedRegistryPath = Path.Combine(
            bundleDirectory,
            SeedRegistryFileName);
        WriteJsonLines(seedRegistryPath, seeds);

        var reportPath = Path.Combine(bundleDirectory, ReportFileName);
        var report = BuildReport(
            job,
            training,
            trainingAnalysis,
            completionKind,
            selected,
            seeds);
        WriteJson(reportPath, report);
        var reportHtmlPath = Path.Combine(bundleDirectory, ReportHtmlFileName);
        WriteHtmlReport(reportHtmlPath, training, trainingAnalysis, selected, seeds);

        var databasePath = Path.Combine(bundleDirectory, DatabaseFileName);
        WriteSimulationDatabase(
            databasePath,
            job,
            training,
            completionKind,
            selected,
            seeds);

        var manifestPath = Path.Combine(bundleDirectory, ManifestFileName);
        var manifest = new FoundationArtifactBundleManifest
        {
            JobId = job.JobId,
            CompletionKind = completionKind,
            TrainingSucceeded = training.Success,
            DeploymentEligible = training.AcceptancePassed,
            EvaluatedModelId = selected.Model?.ModelId ?? "",
            EvaluatedModelIteration = selected.Iteration,
            CandidateModelManifest = Relative(bundleDirectory, modelManifestPath),
            CandidateModelWeights = Relative(bundleDirectory, modelWeightsPath),
            CapabilityReport = Relative(bundleDirectory, reportPath),
            CapabilityReportHtml = Relative(bundleDirectory, reportHtmlPath),
            SimulationDatabase = Relative(bundleDirectory, databasePath),
            SeedRegistry = Relative(bundleDirectory, seedRegistryPath),
            ModelNodeGraph = Relative(bundleDirectory, modelNodeGraphPath)
        };
        foreach (var path in new[]
                 {
                     modelManifestPath,
                     modelWeightsPath,
                     reportPath,
                     reportHtmlPath,
                     databasePath,
                     seedRegistryPath,
                     modelNodeGraphPath
                 }.Where(File.Exists))
        {
            manifest.Sha256[Relative(bundleDirectory, path)] = HashFile(path);
        }
        WriteJson(manifestPath, manifest);

        var liveDirectory = Path.Combine(bundleDirectory, LiveDirectoryName);
        if (Directory.Exists(liveDirectory))
        {
            try
            {
                Directory.Delete(liveDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The terminal bundle is authoritative even if a viewer still
                // has the obsolete live snapshot open.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best-effort and must not hide a complete bundle.
            }
        }

        return new FoundationArtifactBundleResult
        {
            ModelProduced = selected.Model != null,
            BundleDirectory = bundleDirectory,
            ManifestPath = manifestPath,
            CandidateModelPath = modelManifestPath,
            CapabilityReportPath = reportPath,
            CapabilityReportHtmlPath = reportHtmlPath,
            SimulationDatabasePath = databasePath,
            SeedRegistryPath = seedRegistryPath,
            ModelNodeGraphPath = modelNodeGraphPath
        };
    }

    public static void AttachDeploymentPackage(
        string bundleDirectory,
        string packagePath,
        string weightsPath)
    {
        var manifestPath = Path.Combine(bundleDirectory, ManifestFileName);
        var manifest = JsonConvert.DeserializeObject<
                           FoundationArtifactBundleManifest>(
                           File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException(
                           "Training artifact manifest is invalid.");
        manifest.DeploymentModelPackage = Relative(
            bundleDirectory,
            packagePath);
        manifest.DeploymentModelWeights = Relative(
            bundleDirectory,
            weightsPath);
        manifest.Sha256[manifest.DeploymentModelPackage] = HashFile(packagePath);
        manifest.Sha256[manifest.DeploymentModelWeights] = HashFile(weightsPath);
        WriteJson(manifestPath, manifest);
    }

    private static (CombatPolicyValueNetworkDefinition? Model, int Iteration,
        string Reason) SelectModel(
        CombatCampaignFoundationTrainingResult training)
    {
        var model = training.AcceptancePassed
            ? training.Champion
            : training.AbsoluteQualifiedBestModel
              ?? training.BestPendingArenaCandidate?.Model
              ?? training.LatestTrainingModel
              ?? training.WorkingChampion
              ?? training.Champion;
        var iteration = training.Iterations
            .Where(item => string.Equals(
                item.CandidateModelId,
                model?.ModelId,
                StringComparison.Ordinal))
            .Select(item => item.Iteration)
            .LastOrDefault();
        if (iteration == 0)
        {
            iteration = training.EvaluatedModelIteration;
        }
        var reason = training.AcceptancePassed
            ? "accepted-champion"
            : ReferenceEquals(model, training.AbsoluteQualifiedBestModel)
                ? "absolute-qualified-diagnostic"
                : ReferenceEquals(model, training.BestPendingArenaCandidate?.Model)
                    ? "best-pending-arena-candidate"
                    : ReferenceEquals(model, training.LatestTrainingModel)
                        ? "latest-training-model"
                        : "retained-working-model";
        return (model, iteration, reason);
    }

    private static object BuildModelNodeGraph(
        CombatCampaignFoundationTrainingResult training,
        (CombatPolicyValueNetworkDefinition? Model, int Iteration,
            string Reason) selected)
    {
        var ordered = training.Iterations
            .OrderBy(item => item.Iteration)
            .ToList();
        return new
        {
            schemaVersion = 1,
            protocol = "aura.foundation-model-node-graph.v1",
            selectedModelId = selected.Model?.ModelId ?? "",
            selectedIteration = selected.Iteration,
            selectedReason = selected.Reason,
            nodes = ordered.Select((item, index) => new
            {
                iteration = item.Iteration,
                modelId = item.CandidateModelId,
                parentModelId = index == 0
                    ? ""
                    : ordered[index - 1].CandidateModelId,
                item.CandidateQualificationState,
                item.WorkingModelAccepted,
                item.NonInferiorityGatePassed,
                item.AbsoluteQualificationGatePassed,
                item.QualifiedCandidateSelected,
                item.CandidateNormalWinRate,
                item.CandidateAdvancedWinRate,
                item.CandidateScoreGain,
                item.CandidateDepthGain,
                item.PromotionKind,
                item.PromotionReason,
                selected = string.Equals(
                    item.CandidateModelId,
                    selected.Model?.ModelId,
                    StringComparison.Ordinal)
            }).ToList()
        };
    }

    private static object BuildReport(
        CombatFoundationWorkerJob job,
        CombatCampaignFoundationTrainingResult training,
        CombatFoundationTrainingAnalysis trainingAnalysis,
        string completionKind,
        (CombatPolicyValueNetworkDefinition? Model, int Iteration,
            string Reason) selected,
        IReadOnlyList<FoundationSeedTag> seeds)
    {
        return new
        {
            schemaVersion = 1,
            protocol = "aura.foundation-model-capability-report.v1",
            generatedUtc = DateTime.UtcNow,
            jobId = job.JobId,
            completionKind,
            model = new
            {
                modelId = selected.Model?.ModelId ?? "",
                sourceIteration = selected.Iteration,
                selectionReason = selected.Reason,
                deploymentEligible = training.AcceptancePassed,
                training.AcceptanceKind,
                training.Message
            },
            capabilityProbe = training.CapabilityProbe,
            arena = new
            {
                iterations = training.Iterations,
                training.ArenaFailureCounts,
                training.ArenaFailures,
                training.ArenaRetryAttempts,
                training.ArenaRecoveredCampaigns
            },
            simulatedAdventures = new
            {
                validation = training.Validation,
                runs = training.ValidationRuns.Select(CampaignSummary).ToList()
            },
            decisionDifferences = training.CapabilityProbe.DecisionDifferences,
            seedTags = seeds,
            performance = trainingAnalysis
        };
    }

    private static object CampaignSummary(CombatCampaignResult item)
    {
        return new
        {
            item.DifficultyId,
            item.WorldSeed,
            item.PolicyId,
            item.FinalBossVictory,
            item.Invalid,
            item.CompletedBattles,
            item.TotalBattles,
            finalHp = item.FinalState?.CurrentHp ?? 0,
            maxHp = item.FinalState?.MaxHp ?? 0,
            rewardDecisions = item.Rewards?.Count ?? 0,
            battleTurns = item.Battles?.Sum(battle => battle.Turns) ?? 0
        };
    }

    private static List<FoundationSeedTag> BuildSeedTags(
        CombatCampaignFoundationTrainingResult training)
    {
        var records = new Dictionary<string, FoundationSeedTag>(
            StringComparer.Ordinal);
        void Add(
            string source,
            string difficulty,
            ulong seed,
            string tag,
            double priority,
            string reason)
        {
            var key = source + "|" + difficulty + "|" + seed + "|" + tag;
            records[key] = new FoundationSeedTag
            {
                Source = source,
                DifficultyId = difficulty,
                WorldSeed = seed,
                Tag = tag,
                Priority = priority,
                Reason = reason
            };
        }

        foreach (var run in training.ValidationRuns)
        {
            if (run.Invalid || !run.FinalBossVictory)
            {
                Add(
                    "validation",
                    run.DifficultyId,
                    run.WorldSeed,
                    run.Invalid ? "problem-invalid" : "problem-failure",
                    run.Invalid ? 1d : 0.85d,
                    run.Invalid
                        ? "validation campaign was invalid"
                        : "candidate did not defeat the final boss");
            }
            else if (string.Equals(
                         run.DifficultyId,
                         "advanced",
                         StringComparison.Ordinal))
            {
                Add(
                    "validation",
                    run.DifficultyId,
                    run.WorldSeed,
                    "high-value-advanced-win",
                    0.65d,
                    "advanced validation victory");
            }
        }
        foreach (var pair in training.CapabilityProbe.Pairs)
        {
            if (pair.BaselineVictory && !pair.ChampionVictory)
            {
                Add(
                    "capability-probe",
                    pair.DifficultyId,
                    pair.WorldSeed,
                    "problem-baseline-regression",
                    1d,
                    "baseline won while candidate lost");
            }
            else if (!pair.BaselineVictory && pair.ChampionVictory)
            {
                Add(
                    "capability-probe",
                    pair.DifficultyId,
                    pair.WorldSeed,
                    "high-value-model-improvement",
                    0.9d,
                    "candidate won while baseline lost");
            }
        }
        foreach (var failure in training.ArenaFailures)
        {
            Add(
                "arena",
                failure.DifficultyId,
                failure.WorldSeed,
                "problem-arena-integrity",
                1d,
                string.Join("; ", failure.Reasons));
        }
        return records.Values.ToList();
    }

    private static List<FoundationSeedTag> MergeCumulativeSeedRegistry(
        CombatFoundationWorkerJob job,
        IReadOnlyList<FoundationSeedTag> current)
    {
        var root = string.IsNullOrWhiteSpace(job.SuccessArchiveDirectory)
            ? job.ResultDirectory
            : job.SuccessArchiveDirectory;
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "foundation-seed-registry-v1.jsonl");
        var merged = new Dictionary<string, FoundationSeedTag>(
            StringComparer.Ordinal);
        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var existing = JsonConvert.DeserializeObject<FoundationSeedTag>(line);
                    if (existing != null && !string.IsNullOrWhiteSpace(existing.Tag))
                    {
                        merged[existing.Key] = existing;
                    }
                }
                catch (JsonException)
                {
                    // A damaged historical line does not block current artifacts.
                }
            }
        }
        foreach (var seed in current)
        {
            merged[seed.Key] = seed;
        }
        var values = merged.Values
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
            .ThenBy(item => item.WorldSeed)
            .ThenBy(item => item.Tag, StringComparer.Ordinal)
            .ToList();
        WriteJsonLines(path, values);
        return values;
    }

    private static void WriteSimulationDatabase(
        string path,
        CombatFoundationWorkerJob job,
        CombatCampaignFoundationTrainingResult training,
        string completionKind,
        (CombatPolicyValueNetworkDefinition? Model, int Iteration,
            string Reason) selected,
        IReadOnlyList<FoundationSeedTag> seeds)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var connection = new SqliteConnection(
                       new SqliteConnectionStringBuilder
                       {
                           DataSource = temporaryPath,
                           Mode = SqliteOpenMode.ReadWriteCreate,
                           Cache = SqliteCacheMode.Private,
                           Pooling = false
                       }.ToString()))
            {
                connection.Open();
                using (var pragma = connection.CreateCommand())
                {
                    pragma.CommandText =
                        "PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON;";
                    pragma.ExecuteNonQuery();
                }
                CreateSchema(connection);
                using var transaction = connection.BeginTransaction();
                InsertMetadata(connection, transaction, new Dictionary<string, string>
                {
                    ["schema_version"] = "1",
                    ["protocol"] = "aura.foundation-simulation-process.v1",
                    ["job_id"] = job.JobId,
                    ["completion_kind"] = completionKind,
                    ["model_id"] = selected.Model?.ModelId ?? "",
                    ["model_iteration"] = selected.Iteration.ToString(
                        CultureInfo.InvariantCulture),
                    ["deployment_eligible"] = training.AcceptancePassed.ToString(),
                    ["validation_sample_plan_hash"] = training.Validation.SamplePlanHash,
                    ["generated_utc"] = DateTime.UtcNow.ToString("O")
                });
                InsertModelNodes(
                    connection,
                    transaction,
                    training,
                    selected.Model?.ModelId);
                InsertValidationRuns(
                    connection,
                    transaction,
                    training.ValidationRuns);
                InsertCapability(connection, transaction, training.CapabilityProbe);
                InsertArena(connection, transaction, training);
                InsertSeedTags(connection, transaction, seeds);
                transaction.Commit();
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
            File.Delete(temporaryPath + "-journal");
            File.Delete(temporaryPath + "-wal");
            File.Delete(temporaryPath + "-shm");
        }
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE model_nodes(
              iteration INTEGER PRIMARY KEY, model_id TEXT, parent_model_id TEXT,
              qualification_state TEXT, working_accepted INTEGER,
              noninferiority_passed INTEGER, absolute_passed INTEGER,
              selected INTEGER, normal_win_rate REAL, advanced_win_rate REAL,
              score_gain REAL, depth_gain REAL, promotion_kind TEXT,
              promotion_reason TEXT, details_json TEXT NOT NULL);
            CREATE TABLE campaigns(
              id INTEGER PRIMARY KEY, stage TEXT NOT NULL, difficulty TEXT,
              world_seed TEXT, policy_id TEXT, victory INTEGER, invalid INTEGER,
              completed_battles INTEGER, total_battles INTEGER,
              final_hp INTEGER, max_hp INTEGER, plan_hash TEXT,
              details_json TEXT NOT NULL);
            CREATE TABLE battles(
              id INTEGER PRIMARY KEY, campaign_id INTEGER NOT NULL,
              battle_index INTEGER, scenario_id TEXT, outcome TEXT,
              termination_reason TEXT, turns INTEGER, final_hp INTEGER,
              search_simulations INTEGER, search_nodes INTEGER,
              damage_dealt INTEGER, damage_taken INTEGER,
              details_json TEXT NOT NULL,
              FOREIGN KEY(campaign_id) REFERENCES campaigns(id));
            CREATE TABLE turns(
              id INTEGER PRIMARY KEY, battle_id INTEGER NOT NULL,
              turn_index INTEGER, player_hp_start INTEGER, player_hp_end INTEGER,
              enemy_hp_start INTEGER, enemy_hp_end INTEGER, actions INTEGER,
              details_json TEXT NOT NULL,
              FOREIGN KEY(battle_id) REFERENCES battles(id));
            CREATE TABLE reward_decisions(
              id INTEGER PRIMARY KEY, campaign_id INTEGER NOT NULL,
              encounter_index INTEGER, encounter_id TEXT, kind TEXT,
              round_number INTEGER, selected_id TEXT, skipped INTEGER,
              skip_score REAL, reason TEXT, details_json TEXT NOT NULL,
              FOREIGN KEY(campaign_id) REFERENCES campaigns(id));
            CREATE TABLE reward_candidates(
              id INTEGER PRIMARY KEY, reward_decision_id INTEGER NOT NULL,
              reward_id TEXT, total_score REAL, base_value REAL,
              learned_residual REAL, conditional_residual REAL,
              strategy_fit REAL, details_json TEXT NOT NULL,
              FOREIGN KEY(reward_decision_id) REFERENCES reward_decisions(id));
            CREATE TABLE capability_pairs(
              id INTEGER PRIMARY KEY, difficulty TEXT, world_seed TEXT,
              baseline_victory INTEGER, champion_victory INTEGER,
              baseline_depth INTEGER, champion_depth INTEGER,
              baseline_invalid INTEGER, champion_invalid INTEGER,
              details_json TEXT NOT NULL);
            CREATE TABLE decision_differences(
              id INTEGER PRIMARY KEY, difficulty TEXT, world_seed TEXT,
              battle_index INTEGER, decision_sequence INTEGER,
              failure_category TEXT, confidence REAL,
              preferred_candidate_id TEXT, details_json TEXT NOT NULL);
            CREATE TABLE arena_iterations(
              iteration INTEGER PRIMARY KEY, candidate_model_id TEXT,
              normal_win_rate REAL, advanced_win_rate REAL,
              champion_normal_win_rate REAL, champion_advanced_win_rate REAL,
              candidate_only_wins INTEGER, champion_only_wins INTEGER,
              noninferiority_passed INTEGER, absolute_passed INTEGER,
              promotion_kind TEXT, promotion_reason TEXT,
              elapsed_seconds REAL, details_json TEXT NOT NULL);
            CREATE TABLE seed_tags(
              id INTEGER PRIMARY KEY, source TEXT, difficulty TEXT,
              world_seed TEXT, tag TEXT, priority REAL, reason TEXT,
              details_json TEXT NOT NULL);
            CREATE INDEX ix_campaigns_seed ON campaigns(difficulty, world_seed);
            CREATE INDEX ix_battles_campaign ON battles(campaign_id, battle_index);
            CREATE INDEX ix_rewards_campaign ON reward_decisions(campaign_id, encounter_index);
            CREATE INDEX ix_seed_tags_seed ON seed_tags(difficulty, world_seed);
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            Execute(
                connection,
                transaction,
                "INSERT INTO metadata(key,value) VALUES($key,$value)",
                ("$key", pair.Key),
                ("$value", pair.Value));
        }
    }

    private static void InsertModelNodes(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CombatCampaignFoundationTrainingResult training,
        string? selectedModelId)
    {
        var parentModelId = "";
        foreach (var item in training.Iterations.OrderBy(value => value.Iteration))
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO model_nodes VALUES(
                  $iteration,$model,$parent,$state,$working,$noninferior,$absolute,
                  $selected,$normal,$advanced,$score,$depth,$kind,$reason,$json)
                """,
                ("$iteration", item.Iteration),
                ("$model", item.CandidateModelId),
                ("$parent", parentModelId),
                ("$state", item.CandidateQualificationState),
                ("$working", Bool(item.WorkingModelAccepted)),
                ("$noninferior", Bool(item.NonInferiorityGatePassed)),
                ("$absolute", Bool(item.AbsoluteQualificationGatePassed)),
                ("$selected", Bool(string.Equals(
                    item.CandidateModelId,
                    selectedModelId,
                    StringComparison.Ordinal))),
                ("$normal", item.CandidateNormalWinRate),
                ("$advanced", item.CandidateAdvancedWinRate),
                ("$score", item.CandidateScoreGain),
                ("$depth", item.CandidateDepthGain),
                ("$kind", item.PromotionKind),
                ("$reason", item.PromotionReason),
                ("$json", Json(item)));
            parentModelId = item.CandidateModelId;
        }
    }

    private static void InsertValidationRuns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CombatCampaignResult> runs)
    {
        foreach (var campaign in runs)
        {
            var campaignId = InsertReturningId(
                connection,
                transaction,
                """
                INSERT INTO campaigns(
                  stage,difficulty,world_seed,policy_id,victory,invalid,
                  completed_battles,total_battles,final_hp,max_hp,plan_hash,details_json)
                VALUES(
                  'validation',$difficulty,$seed,$policy,$victory,$invalid,
                  $completed,$total,$hp,$maxHp,$plan,$json);
                SELECT last_insert_rowid();
                """,
                ("$difficulty", campaign.DifficultyId),
                ("$seed", campaign.WorldSeed.ToString(CultureInfo.InvariantCulture)),
                ("$policy", campaign.PolicyId),
                ("$victory", Bool(campaign.FinalBossVictory)),
                ("$invalid", Bool(campaign.Invalid)),
                ("$completed", campaign.CompletedBattles),
                ("$total", campaign.TotalBattles),
                ("$hp", campaign.FinalState?.CurrentHp ?? 0),
                ("$maxHp", campaign.FinalState?.MaxHp ?? 0),
                ("$plan", campaign.PlanHash),
                ("$json", Json(CampaignSummary(campaign))));
            InsertBattles(connection, transaction, campaignId, campaign.Battles);
            InsertRewards(connection, transaction, campaignId, campaign.Rewards);
        }
    }

    private static void InsertBattles(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long campaignId,
        IReadOnlyList<CombatSimulationResult>? battles)
    {
        for (var index = 0; index < (battles?.Count ?? 0); index++)
        {
            var battle = battles![index];
            var battleId = InsertReturningId(
                connection,
                transaction,
                """
                INSERT INTO battles(
                  campaign_id,battle_index,scenario_id,outcome,termination_reason,
                  turns,final_hp,search_simulations,search_nodes,damage_dealt,
                  damage_taken,details_json)
                VALUES(
                  $campaign,$index,$scenario,$outcome,$termination,$turns,$hp,
                  $simulations,$nodes,$dealt,$taken,$json);
                SELECT last_insert_rowid();
                """,
                ("$campaign", campaignId),
                ("$index", index),
                ("$scenario", battle.ScenarioId),
                ("$outcome", battle.Outcome.ToString()),
                ("$termination", battle.TerminationReason.ToString()),
                ("$turns", battle.Turns),
                ("$hp", battle.FinalPlayerHp),
                ("$simulations", battle.Metrics?.SearchSimulations ?? 0L),
                ("$nodes", battle.Metrics?.SearchNodes ?? 0L),
                ("$dealt", battle.Metrics?.DamageDealt ?? 0),
                ("$taken", battle.Metrics?.DamageTaken ?? 0),
                ("$json", Json(battle.Metrics)));
            foreach (var turn in battle.TurnsSummary ?? new List<CombatTurnSummary>())
            {
                Execute(
                    connection,
                    transaction,
                    """
                    INSERT INTO turns(
                      battle_id,turn_index,player_hp_start,player_hp_end,
                      enemy_hp_start,enemy_hp_end,actions,details_json)
                    VALUES($battle,$turn,$phs,$phe,$ehs,$ehe,$actions,$json)
                    """,
                    ("$battle", battleId),
                    ("$turn", turn.Turn),
                    ("$phs", turn.PlayerHpAtStart),
                    ("$phe", turn.PlayerHpAtEnd),
                    ("$ehs", turn.EnemyHpAtStart),
                    ("$ehe", turn.EnemyHpAtEnd),
                    ("$actions", turn.Actions),
                    ("$json", Json(turn)));
            }
        }
    }

    private static void InsertRewards(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long campaignId,
        IReadOnlyList<CombatCampaignRewardDecision>? rewards)
    {
        foreach (var reward in rewards ?? Array.Empty<CombatCampaignRewardDecision>())
        {
            foreach (var card in reward.Cards ?? new List<CombatCampaignCardDecision>())
            {
                var rewardDecisionId = InsertRewardDecision(
                    connection,
                    transaction,
                    campaignId,
                    reward,
                    "card",
                    card.Round,
                    card.SelectedId,
                    card.Skipped,
                    card.SkipScore,
                    card.SkipReason,
                    card);
                InsertRewardCandidates(
                    connection,
                    transaction,
                    rewardDecisionId,
                    card.Scores);
            }
            if (!string.IsNullOrWhiteSpace(reward.Relic?.OfferedId))
            {
                var relic = reward.Relic;
                var rewardDecisionId = InsertRewardDecision(
                    connection,
                    transaction,
                    campaignId,
                    reward,
                    "relic",
                    0,
                    relic.ResolvedId,
                    string.Equals(relic.Decision, "skip", StringComparison.OrdinalIgnoreCase),
                    0d,
                    relic.Decision,
                    relic);
                InsertRewardCandidates(
                    connection,
                    transaction,
                    rewardDecisionId,
                    relic.Scores);
            }
            if (!string.IsNullOrWhiteSpace(reward.Blessing?.OfferedId))
            {
                var blessing = reward.Blessing;
                InsertRewardDecision(
                    connection,
                    transaction,
                    campaignId,
                    reward,
                    "blessing",
                    0,
                    blessing.Acquired ? blessing.OfferedId : "",
                    !blessing.Acquired,
                    0d,
                    blessing.Acquired ? "acquired" : "declined",
                    blessing);
            }
        }
    }

    private static long InsertRewardDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long campaignId,
        CombatCampaignRewardDecision reward,
        string kind,
        int round,
        string selected,
        bool skipped,
        double skipScore,
        string reason,
        object details)
    {
        return InsertReturningId(
            connection,
            transaction,
            """
            INSERT INTO reward_decisions(
              campaign_id,encounter_index,encounter_id,kind,round_number,
              selected_id,skipped,skip_score,reason,details_json)
            VALUES($campaign,$encounter,$encounterId,$kind,$round,$selected,
              $skipped,$score,$reason,$json);
            SELECT last_insert_rowid();
            """,
            ("$campaign", campaignId),
            ("$encounter", reward.EncounterIndex),
            ("$encounterId", reward.EncounterId),
            ("$kind", kind),
            ("$round", round),
            ("$selected", selected),
            ("$skipped", Bool(skipped)),
            ("$score", skipScore),
            ("$reason", reason),
            ("$json", Json(details)));
    }

    private static void InsertRewardCandidates(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long rewardDecisionId,
        IReadOnlyList<CombatCampaignRewardScore>? scores)
    {
        foreach (var score in scores ?? Array.Empty<CombatCampaignRewardScore>())
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO reward_candidates(
                  reward_decision_id,reward_id,total_score,base_value,
                  learned_residual,conditional_residual,strategy_fit,details_json)
                VALUES($decision,$reward,$total,$base,$learned,$conditional,$strategy,$json)
                """,
                ("$decision", rewardDecisionId),
                ("$reward", score.RewardId),
                ("$total", score.Total),
                ("$base", score.BaseValue),
                ("$learned", score.LearnedResidual),
                ("$conditional", score.ConditionalResidual),
                ("$strategy", score.StrategyFit),
                ("$json", Json(score)));
        }
    }

    private static void InsertCapability(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CombatFoundationCapabilityProbe probe)
    {
        foreach (var pair in probe.Pairs)
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO capability_pairs(
                  difficulty,world_seed,baseline_victory,champion_victory,
                  baseline_depth,champion_depth,baseline_invalid,champion_invalid,
                  details_json)
                VALUES($difficulty,$seed,$baselineWin,$championWin,$baselineDepth,
                  $championDepth,$baselineInvalid,$championInvalid,$json)
                """,
                ("$difficulty", pair.DifficultyId),
                ("$seed", pair.WorldSeed.ToString(CultureInfo.InvariantCulture)),
                ("$baselineWin", Bool(pair.BaselineVictory)),
                ("$championWin", Bool(pair.ChampionVictory)),
                ("$baselineDepth", pair.BaselineCompletedBattles),
                ("$championDepth", pair.ChampionCompletedBattles),
                ("$baselineInvalid", Bool(pair.BaselineInvalid)),
                ("$championInvalid", Bool(pair.ChampionInvalid)),
                ("$json", Json(pair)));
        }
        foreach (var difference in probe.DecisionDifferences)
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO decision_differences(
                  difficulty,world_seed,battle_index,decision_sequence,
                  failure_category,confidence,preferred_candidate_id,details_json)
                VALUES($difficulty,$seed,$battle,$sequence,$category,$confidence,
                  $preferred,$json)
                """,
                ("$difficulty", difference.DifficultyId),
                ("$seed", difference.WorldSeed.ToString(CultureInfo.InvariantCulture)),
                ("$battle", difference.JourneyBattleIndex),
                ("$sequence", difference.DecisionSequence),
                ("$category", difference.FailureCategory),
                ("$confidence", difference.Confidence),
                ("$preferred", difference.PreferredCandidateId),
                ("$json", Json(difference)));
        }
    }

    private static void InsertArena(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CombatCampaignFoundationTrainingResult training)
    {
        foreach (var item in training.Iterations)
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO arena_iterations(
                  iteration,candidate_model_id,normal_win_rate,advanced_win_rate,
                  champion_normal_win_rate,champion_advanced_win_rate,
                  candidate_only_wins,champion_only_wins,noninferiority_passed,
                  absolute_passed,promotion_kind,promotion_reason,elapsed_seconds,
                  details_json)
                VALUES($iteration,$model,$normal,$advanced,$championNormal,
                  $championAdvanced,$candidateOnly,$championOnly,$noninferior,
                  $absolute,$kind,$reason,$elapsed,$json)
                """,
                ("$iteration", item.Iteration),
                ("$model", item.CandidateModelId),
                ("$normal", item.CandidateNormalWinRate),
                ("$advanced", item.CandidateAdvancedWinRate),
                ("$championNormal", item.ChampionNormalWinRate),
                ("$championAdvanced", item.ChampionAdvancedWinRate),
                ("$candidateOnly", item.CandidateOnlyWins),
                ("$championOnly", item.ChampionOnlyWins),
                ("$noninferior", Bool(item.NonInferiorityGatePassed)),
                ("$absolute", Bool(item.AbsoluteQualificationGatePassed)),
                ("$kind", item.PromotionKind),
                ("$reason", item.PromotionReason),
                ("$elapsed", item.ResourceElapsedSeconds),
                ("$json", Json(item)));
        }
    }

    private static void InsertSeedTags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<FoundationSeedTag> seeds)
    {
        foreach (var seed in seeds)
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO seed_tags(
                  source,difficulty,world_seed,tag,priority,reason,details_json)
                VALUES($source,$difficulty,$seed,$tag,$priority,$reason,$json)
                """,
                ("$source", seed.Source),
                ("$difficulty", seed.DifficultyId),
                ("$seed", seed.WorldSeed.ToString(CultureInfo.InvariantCulture)),
                ("$tag", seed.Tag),
                ("$priority", seed.Priority),
                ("$reason", seed.Reason),
                ("$json", Json(seed)));
        }
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }

    private static long InsertReturningId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    private static int Bool(bool value) => value ? 1 : 0;

    private static string Json(object? value) =>
        JsonConvert.SerializeObject(value, Formatting.None);

    private static void WriteJson(string path, object value)
    {
        WriteText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
    }

    private static void WriteJsonLines(
        string path,
        IEnumerable<object> values)
    {
        WriteText(path, string.Join(
            Environment.NewLine,
            values.Select(Json)) + Environment.NewLine);
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static string Relative(string root, string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? ""
            : Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteHtmlReport(
        string path,
        CombatCampaignFoundationTrainingResult training,
        CombatFoundationTrainingAnalysis analysis,
        (CombatPolicyValueNetworkDefinition? Model, int Iteration,
            string Reason) selected,
        IReadOnlyList<FoundationSeedTag> seeds)
    {
        static string E(object? value) => WebUtility.HtmlEncode(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        var normal = training.Validation.NormalCampaigns;
        var advanced = training.Validation.AdvancedCampaigns;
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">")
            .Append("<title>模型能力报告</title><style>")
            .Append("body{font-family:Segoe UI,Microsoft YaHei,sans-serif;margin:32px;color:#17202a;background:#f5f7f8}")
            .Append("main{max-width:1180px;margin:auto}h1{font-size:28px}h2{margin-top:30px;font-size:20px}")
            .Append(".summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px}")
            .Append(".metric{background:white;border:1px solid #d8dee3;border-radius:6px;padding:14px}")
            .Append(".metric b{display:block;font-size:22px;margin-top:5px}table{width:100%;border-collapse:collapse;background:white}")
            .Append("th,td{padding:8px 10px;border:1px solid #d8dee3;text-align:left;font-size:13px}th{background:#eef2f4}")
            .Append(".pass{color:#137333}.fail{color:#b3261e}</style></head><body><main>")
            .Append("<h1>模型能力报告</h1><div class=\"summary\">")
            .Append("<div class=\"metric\">模型<b>").Append(E(selected.Model?.ModelId)).Append("</b>节点 ").Append(selected.Iteration).Append("</div>")
            .Append("<div class=\"metric\">部署资格<b class=\"")
            .Append(training.AcceptancePassed ? "pass\">通过" : "fail\">未通过")
            .Append("</b></div>")
            .Append("<div class=\"metric\">普通模拟<b>").Append(training.Validation.NormalVictories).Append('/').Append(normal).Append("</b></div>")
            .Append("<div class=\"metric\">高级模拟<b>").Append(training.Validation.AdvancedVictories).Append('/').Append(advanced).Append("</b></div>")
            .Append("<div class=\"metric\">能力探针对局<b>").Append(training.CapabilityProbe.Pairs.Count).Append("</b></div>")
            .Append("<div class=\"metric\">固定种子标签<b>").Append(seeds.Count).Append("</b></div></div>")
            .Append("<h2>能力探针</h2><table><tr><th>结论</th><th>胜场增益</th><th>深度增益</th><th>仅模型胜</th><th>仅基线胜</th><th>原因</th></tr><tr><td>")
            .Append(E(training.CapabilityProbe.BaselineGateVerdict)).Append("</td><td>")
            .Append(training.CapabilityProbe.ChampionVictoryGain).Append("</td><td>")
            .Append(training.CapabilityProbe.ChampionDepthGain.ToString("0.###", CultureInfo.InvariantCulture)).Append("</td><td>")
            .Append(training.CapabilityProbe.ChampionOnlyWins).Append("</td><td>")
            .Append(training.CapabilityProbe.BaselineOnlyWins).Append("</td><td>")
            .Append(E(training.CapabilityProbe.BaselineGateReason)).Append("</td></tr></table>")
            .Append("<h2>竞技场节点</h2><table><tr><th>轮次</th><th>普通</th><th>高级</th><th>不劣</th><th>绝对线</th><th>结果</th><th>原因</th></tr>");
        foreach (var item in training.Iterations)
        {
            html.Append("<tr><td>").Append(item.Iteration).Append("</td><td>")
                .Append(item.CandidateNormalWinRate.ToString("P1", CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(item.CandidateAdvancedWinRate.ToString("P1", CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(item.NonInferiorityGatePassed ? "通过" : "未通过").Append("</td><td>")
                .Append(item.AbsoluteQualificationGatePassed ? "通过" : "未通过").Append("</td><td>")
                .Append(E(item.PromotionKind)).Append("</td><td>").Append(E(item.PromotionReason)).Append("</td></tr>");
        }
        html.Append("</table><h2>模拟冒险</h2><table><tr><th>难度</th><th>Seed</th><th>胜利</th><th>进度</th><th>最终生命</th><th>奖励决策</th></tr>");
        foreach (var run in training.ValidationRuns)
        {
            html.Append("<tr><td>").Append(E(run.DifficultyId)).Append("</td><td>").Append(run.WorldSeed).Append("</td><td>")
                .Append(run.FinalBossVictory ? "是" : "否").Append("</td><td>").Append(run.CompletedBattles).Append('/').Append(run.TotalBattles)
                .Append("</td><td>").Append(run.FinalState?.CurrentHp ?? 0).Append('/').Append(run.FinalState?.MaxHp ?? 0)
                .Append("</td><td>").Append(run.Rewards?.Count ?? 0).Append("</td></tr>");
        }
        html.Append("</table><h2>性能</h2><p>总耗时 ")
            .Append(analysis.TotalElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" 秒。详细热点与原始数据请查看 JSON 报告和 SQLite 数据库。</p></main></body></html>");
        WriteText(path, html.ToString());
    }
}
