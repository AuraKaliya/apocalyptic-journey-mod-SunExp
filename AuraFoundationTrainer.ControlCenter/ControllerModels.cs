using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class ControllerSettings
{
    public int SchemaVersion { get; set; } = 5;

    [JsonIgnore]
    public string ModRoot { get; set; } = "";

    [JsonIgnore]
    public string DataRoot { get; set; } = "";

    public string LastRunDirectory { get; set; } = "";

    public int ContinueGeneration { get; set; }

    public CombatGameSubjectPreset GameSubject { get; set; } = new();

    public CombatFoundationTrainingParameters Parameters { get; set; } =
        CreateDefaultParameters();

    private static CombatFoundationTrainingParameters CreateDefaultParameters()
    {
        return new CombatFoundationTrainingParameters
        {
            HardEncounterWeights = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["level_10011"] = 0.25d,
                ["level_10040"] = 0.15d,
                ["level_10004"] = 0.15d,
                ["level_10001"] = 0.15d,
                ["level_10009"] = 0.12d,
                ["level_10006"] = 0.10d,
                ["@other"] = 0.05d,
                ["@final-boss"] = 0.03d
            }
        };
    }
}

internal sealed class ControllerSession
{
    public int SchemaVersion { get; set; } = 1;

    public string JobId { get; set; } = "";

    public string JobPath { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public int ProcessId { get; set; }

    public DateTime StartedUtc { get; set; }
}

internal sealed class ControllerWorkerResultSummary
{
    public int SchemaVersion { get; set; }

    public string JobId { get; set; } = "";

    public bool Success { get; set; }

    public bool Cancelled { get; set; }

    public string CompletionKind { get; set; } = "";

    public string Message { get; set; } = "";

    public string Runtime { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string EpisodesPath { get; set; } = "";

    public string CheckpointPath { get; set; } = "";

    public string ModelPackagePath { get; set; } = "";

    public string TrainingMetricsPath { get; set; } = "";

    public string TrainingAnalysisPath { get; set; } = "";

    public int TrainingMetricWriteFailures { get; set; }

    public string TrainingMetricWarning { get; set; } = "";

    public bool Resumable { get; set; }

    public int CheckpointWriteFailures { get; set; }

    public string CheckpointWarning { get; set; } = "";

    public ControllerTrainingResultSummary? Training { get; set; }
}

internal sealed class ControllerTrainingResultSummary
{
    public bool Success { get; set; }

    public bool AcceptancePassed { get; set; }

    public string Message { get; set; } = "";

    public int GeneratedReplayEpisodes { get; set; }

    public int LoadedExpertReplayEpisodes { get; set; }

    public CombatFoundationExpertReplaySelection ExpertReplaySelection {
        get;
        set;
    } = new();

    public CombatFoundationRewardResidualTrainingResult RewardResidualTraining {
        get;
        set;
    } = new();

    public List<CombatCampaignFoundationIteration> Iterations { get; set; } =
        new();

    public CombatCampaignFoundationValidation Validation { get; set; } = new();

    public CombatCampaignFoundationIntegrityReport Preflight { get; set; } =
        new();

    public CombatFoundationCapabilityProbe CapabilityProbe { get; set; } =
        new();

    public int InvalidTrainingCampaigns { get; set; }

    public int TerminalConsistencyViolations { get; set; }

    public int FeatureLeakageViolations { get; set; }

    public Dictionary<string, int> TrainingFailureCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<CombatCampaignFoundationIntegrityFailure> TrainingFailures {
        get;
        set;
    } = new();

    public long AuthoritativeSelectedActionsAudited { get; set; }

    public long AuthoritativeSelectedSemanticMismatches { get; set; }

    public long AuthoritativeTeacherOverrides { get; set; }

    public double RootMaximumVisitShareMean { get; set; }

    public int ModelCompletedEpochs { get; set; }

    public int ModelConfiguredEpochs { get; set; }

    public int ModelBestEpoch { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public double ModelTrainingLoss { get; set; }

    public double ModelValidationLoss { get; set; }

    public double ModelBestValidationLoss { get; set; }

    public List<CombatPolicyValueEpochMetrics> ModelEpochHistory { get; set; } =
        new();
}
