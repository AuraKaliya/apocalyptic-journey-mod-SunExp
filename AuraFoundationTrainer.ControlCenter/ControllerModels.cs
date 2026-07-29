using AuraCombatAi.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class ControllerSettings
{
    public int SchemaVersion { get; set; } = 4;

    [JsonIgnore]
    public string ModRoot { get; set; } = "";

    [JsonIgnore]
    public string DataRoot { get; set; } = "";

    public string LastRunDirectory { get; set; } = "";

    public int ContinueGeneration { get; set; }

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

    public bool Resumable { get; set; }

    public int CheckpointWriteFailures { get; set; }

    public string CheckpointWarning { get; set; } = "";

    public ControllerTrainingResultSummary? Training { get; set; }
}

internal sealed class ControllerTrainingResultSummary
{
    public CombatCampaignFoundationValidation Validation { get; set; } = new();
}
