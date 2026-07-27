using System;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationWorkerJob
{
    public int SchemaVersion { get; set; } = 4;

    public string JobId { get; set; } = "";

    public string ExpectedRulesetHash { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public string ProgressPath { get; set; } = "";

    public string ResultPath { get; set; } = "";

    public string CancellationPath { get; set; } = "";

    public string CheckpointPath { get; set; } = "";

    public string CheckpointEpisodesPath { get; set; } = "";

    public string SuccessArchiveDirectory { get; set; } = "";

    public bool ResumeFromCheckpoint { get; set; } = true;

    public CombatCampaignFoundationTrainingRequest Request { get; set; } = new();

    public CombatRulesetDocument Ruleset { get; set; } = new();

    public CombatPolicyValueNetworkDefinition? InitialChampion { get; set; }
}

public sealed class CombatFoundationWorkerProgress
{
    public int SchemaVersion { get; set; } = 4;

    public string JobId { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public CombatCampaignFoundationTelemetry Telemetry { get; set; } = new();
}

public sealed class CombatFoundationWorkerResult
{
    public int SchemaVersion { get; set; } = 4;

    public string JobId { get; set; } = "";

    public bool Success { get; set; }

    public bool Cancelled { get; set; }

    public string CompletionKind { get; set; } = "";

    public string Message { get; set; } = "";

    public string Runtime { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string EpisodesPath { get; set; } = "";

    public string CheckpointPath { get; set; } = "";

    public bool Resumable { get; set; }

    public CombatCampaignFoundationTrainingResult? Training { get; set; }
}

public sealed class CombatFoundationWorkerCheckpoint
{
    public int SchemaVersion { get; set; } = 4;

    public string RequestFingerprint { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string EpisodesPath { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public CombatCampaignFoundationResumeState Resume { get; set; } = new();
}
