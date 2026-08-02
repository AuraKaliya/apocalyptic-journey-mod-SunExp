using System;
using System.Collections.Generic;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public static class CombatFoundationWorkerProtocol
{
    public const int SchemaVersion = 8;
    public const int TrainingMetricsSchemaVersion = 1;
    public const string TrainingMetricsFileName =
        "foundation-training-metrics-v1.jsonl";
    public const string TrainingAnalysisFileName =
        "foundation-training-analysis-v1.json";
    public const string CheckpointFileName =
        "foundation-training-checkpoint-v8.json";
    public const string CheckpointEpisodesFileName =
        "foundation-training-checkpoint-episodes-v8.jsonl";

    public static bool TryValidateJob(
        CombatFoundationWorkerJob? job,
        out string diagnostic)
    {
        if (job == null)
        {
            diagnostic = "底模训练任务为空";
            return false;
        }
        if (job.SchemaVersion != SchemaVersion)
        {
            diagnostic = "底模训练任务协议不兼容：job="
                         + job.SchemaVersion
                         + "，worker="
                         + SchemaVersion;
            return false;
        }
        diagnostic = "";
        return true;
    }

    public static bool TryValidateProgress(
        CombatFoundationWorkerProgress? progress,
        string expectedJobId,
        out string diagnostic)
    {
        if (progress == null)
        {
            diagnostic = "底模训练进度为空";
            return false;
        }
        if (progress.SchemaVersion != SchemaVersion)
        {
            diagnostic = "底模训练进度协议不兼容：worker="
                         + progress.SchemaVersion
                         + "，host="
                         + SchemaVersion;
            return false;
        }
        if (!string.Equals(
                progress.JobId,
                expectedJobId,
                StringComparison.Ordinal))
        {
            diagnostic = "底模训练进度 jobId 不匹配：worker="
                         + (progress.JobId ?? "")
                         + "，host="
                         + (expectedJobId ?? "");
            return false;
        }
        if (progress.Telemetry == null)
        {
            diagnostic = "底模训练进度缺少 Telemetry";
            return false;
        }
        diagnostic = "";
        return true;
    }

    public static bool TryValidateResult(
        CombatFoundationWorkerResult? result,
        string expectedJobId,
        out string diagnostic)
    {
        if (result == null)
        {
            diagnostic = "底模训练结果为空";
            return false;
        }
        if (result.SchemaVersion != SchemaVersion)
        {
            diagnostic = "底模训练结果协议不兼容：worker="
                         + result.SchemaVersion
                         + "，host="
                         + SchemaVersion;
            return false;
        }
        if (!string.Equals(
                result.JobId,
                expectedJobId,
                StringComparison.Ordinal))
        {
            diagnostic = "底模训练结果 jobId 不匹配：worker="
                         + (result.JobId ?? "")
                         + "，host="
                         + (expectedJobId ?? "");
            return false;
        }
        diagnostic = "";
        return true;
    }
}

public sealed class CombatFoundationWorkerJob
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string JobId { get; set; } = "";

    public string ExpectedRulesetHash { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public string ProgressPath { get; set; } = "";

    public string ResultPath { get; set; } = "";

    public string CancellationPath { get; set; } = "";

    public string CheckpointPath { get; set; } = "";

    public string CheckpointEpisodesPath { get; set; } = "";

    public string SuccessArchiveDirectory { get; set; } = "";

    public string TrainingMetricsPath { get; set; } = "";

    public string TrainingAnalysisPath { get; set; } = "";

    public bool ResumeFromCheckpoint { get; set; } = true;

    public CombatCampaignFoundationTrainingRequest Request { get; set; } = new();

    public CombatRulesetDocument Ruleset { get; set; } = new();

    public CombatPolicyValueNetworkDefinition? InitialChampion { get; set; }
}

public sealed class CombatFoundationWorkerProgress
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string JobId { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public CombatCampaignFoundationTelemetry Telemetry { get; set; } = new();
}

public sealed class CombatFoundationWorkerResult
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string JobId { get; set; } = "";

    public bool Success { get; set; }

    public bool WorkerCompleted { get; set; }

    public bool TrainingSucceeded { get; set; }

    public bool ModelAccepted { get; set; }

    public int EpochsExecuted { get; set; }

    public int SelectedEpoch { get; set; }

    public int PersistedReplayEpisodes { get; set; }

    public long CheckpointBytes { get; set; }

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

    public Dictionary<string, double> RoleStrategyMetrics { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool RoleStrategyGatePassed { get; set; } = true;

    public string RoleStrategyGateFailureReason { get; set; } = "";

    public bool ResumeRequested { get; set; }

    public bool ResumedFromCheckpoint { get; set; }

    public string ResumeDiagnostic { get; set; } = "";

    public bool Resumable { get; set; }

    public int CheckpointWriteFailures { get; set; }

    public string CheckpointWarning { get; set; } = "";

    public CombatCampaignFoundationTrainingResult? Training { get; set; }
}

public sealed class CombatFoundationTrainingMetricRecord
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.TrainingMetricsSchemaVersion;

    public string JobId { get; set; } = "";

    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;

    public string RulesetHash { get; set; } = "";

    public string NativeProgramPackageHash { get; set; } = "";

    public CombatPolicyValueEpochMetrics Metrics { get; set; } = new();
}

public sealed class CombatFoundationTrainingAnalysis
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.TrainingMetricsSchemaVersion;

    public string JobId { get; set; } = "";

    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    public string SourceMetricsPath { get; set; } = "";

    public double EmaAlpha { get; set; } = 0.30d;

    public int EpochCount { get; set; }

    public int IterationCount { get; set; }

    public double BestValidationLoss { get; set; }

    public int BestIteration { get; set; }

    public int BestEpoch { get; set; }

    public Dictionary<string, double> RoleStrategyMetrics { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool RoleStrategyGatePassed { get; set; } = true;

    public string RoleStrategyGateFailureReason { get; set; } = "";

    public List<CombatFoundationTrainingAnalysisPoint> Points { get; set; } =
        new();
}

public sealed class CombatFoundationTrainingAnalysisPoint
{
    public int Iteration { get; set; }

    public int Epoch { get; set; }

    public double TrainingLoss { get; set; }

    public double ValidationLoss { get; set; }

    public double TrainingLossEma { get; set; }

    public double ValidationLossEma { get; set; }

    public double ValidationCiLower { get; set; }

    public double ValidationCiUpper { get; set; }

    public double GeneralizationGap { get; set; }

    public bool Improved { get; set; }

    public bool EarlyStopped { get; set; }
}

public sealed class CombatFoundationEpisodeSnapshot
{
    public int StorageVersion { get; set; } =
        CombatFoundationCheckpointStorage.SnapshotStorageVersion;

    public string Path { get; set; } = "";

    public string ContentSha256 { get; set; } = "";

    public string ReplayIdentity { get; set; } = "";

    public int EpisodeCount { get; set; } = -1;

    public long Length { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CombatFoundationWorkerCheckpoint
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string RequestFingerprint { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string EpisodesPath { get; set; } = "";

    public CombatFoundationEpisodeSnapshot? EpisodeSnapshot { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public CombatCampaignFoundationResumeState Resume { get; set; } = new();
}
