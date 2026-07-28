using AuraCombatAi.Shared;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class ControllerSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string ModRoot { get; set; } = "";

    public string DataRoot { get; set; } = "";

    public string LastRunDirectory { get; set; } = "";

    public int ContinueGeneration { get; set; }

    public CombatFoundationTrainingParameters Parameters { get; set; } = new();
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
