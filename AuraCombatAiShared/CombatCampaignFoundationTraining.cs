using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public static class CombatFoundationTrainingProtocol
{
    public const string TrainingPolicyVersion =
        "foundation-governance-v29-source-audit-partitioned-v4";

    public const string SearchPolicyVersion =
        "dynamic-search-v15-minimum-duration-enforced";

    public const string CurriculumVersion =
        "curriculum-v13-dual-deficit-recovery";

    public const int MaximumAdaptiveCapabilityProbeCampaignsPerDifficulty =
        64;

    public const int MinimumCapabilityDepthEvidencePairs = 8;
}

public static class CombatFoundationTerminalCreditProtocol
{
    public const string Version = "terminal-credit-v2";

    public const double WonBattleCredit = 0.30d;

    public const double FailureBackpropagationDecay = 0.50d;
}

public static class CombatFoundationCounterfactualProtocol
{
    public const string Version = "hard-encounter-counterfactual-v2";

    public const double ImprovedEpisodeWeight = 0.35d;

    public const double MinimumDamageImprovementRatio = 0.15d;

    public const int MinimumDamageImprovement = 10;
}

public static class CombatFoundationStagnationProtocol
{
    public const string Version =
        "foundation-stagnation-v4-arena-checkpoints-only";

    public const int DefaultMaximumConsecutiveRejectedIterations = 3;

    public const double MinimumStrategyQuotaImprovementRatio = 0.15d;

    public const double MinimumValidationLossImprovementRatio = 0.01d;

    public const int HardSeedSolveRateWindow = 2;

    public const int MaximumConsecutiveDataOnlyIterations = 2;

    public const double MinimumHardSeedSolveRate = 0.05d;

    public const double ReducedHardSeedReplayShare = 0.12d;
}

public static class CombatFoundationInferenceHealthProtocol
{
    public const long MinimumRequests = 10_000L;

    public const double MinimumAverageBatchFill = 0.70d;

    public const double MaximumTimeoutFlushRate = 0.50d;

    public const double MaximumDirectFallbackRate = 0.15d;

    public static CombatFoundationInferenceHealth Evaluate(
        CombatCampaignFoundationTelemetry start,
        CombatCampaignFoundationTelemetry end)
    {
        var requests = Math.Max(
            0L,
            (end?.InferenceRequests ?? 0L)
            - (start?.InferenceRequests ?? 0L));
        var batches = Math.Max(
            0L,
            (end?.InferenceBatchEvaluations ?? 0L)
            - (start?.InferenceBatchEvaluations ?? 0L));
        var batchedInputs = Math.Max(
            0L,
            (end?.InferenceBatchedInputs ?? 0L)
            - (start?.InferenceBatchedInputs ?? 0L));
        var timeouts = Math.Max(
            0L,
            (end?.InferenceTimeoutFlushes ?? 0L)
            - (start?.InferenceTimeoutFlushes ?? 0L));
        var fallbacks = Math.Max(
            0L,
            (end?.InferenceDirectFallbackRequests ?? 0L)
            - (start?.InferenceDirectFallbackRequests ?? 0L));
        var directBypasses = Math.Max(
            fallbacks,
            Math.Max(0L, requests - batchedInputs));
        var batchSize = Math.Max(1, end?.InferenceBatchSizePerLane ?? 1);
        var averageBatchSize = batches <= 0
            ? 0d
            : batchedInputs / (double)batches;
        var averageBatchFill = batchSize <= 1
            ? 1d
            : averageBatchSize / batchSize;
        var timeoutRate = batches <= 0 ? 0d : timeouts / (double)batches;
        var fallbackRate = requests <= 0 ? 0d : fallbacks / (double)requests;
        var directBypassRate = requests <= 0
            ? 0d
            : directBypasses / (double)requests;
        var batchMode = string.Equals(
            end?.InferenceExecutionMode,
            CombatFoundationExecutionProfileNames.ShardedBatchInference,
            StringComparison.Ordinal);
        var reasons = new List<string>();
        if (requests >= MinimumRequests && batchMode)
        {
            if (averageBatchFill < MinimumAverageBatchFill)
            {
                reasons.Add("low-batch-fill");
            }
            if (timeoutRate > MaximumTimeoutFlushRate)
            {
                reasons.Add("high-timeout-flush-rate");
            }
            if (directBypassRate > MaximumDirectFallbackRate)
            {
                reasons.Add("high-direct-bypass-rate");
            }
        }
        return new CombatFoundationInferenceHealth
        {
            Requests = requests,
            BatchEvaluations = batches,
            BatchedInputs = batchedInputs,
            TimeoutFlushes = timeouts,
            DirectFallbackRequests = fallbacks,
            DirectBypassRequests = directBypasses,
            AverageBatchSize = averageBatchSize,
            AverageBatchFill = averageBatchFill,
            TimeoutFlushRate = timeoutRate,
            DirectFallbackRate = fallbackRate,
            DirectBypassRate = directBypassRate,
            RevalidationRequired = reasons.Count > 0,
            Reason = string.Join(",", reasons)
        };
    }
}

public sealed class CombatFoundationInferenceHealth
{
    public long Requests { get; set; }

    public long BatchEvaluations { get; set; }

    public long BatchedInputs { get; set; }

    public long TimeoutFlushes { get; set; }

    public long DirectFallbackRequests { get; set; }

    public long DirectBypassRequests { get; set; }

    public double AverageBatchSize { get; set; }

    public double AverageBatchFill { get; set; }

    public double TimeoutFlushRate { get; set; }

    public double DirectFallbackRate { get; set; }

    public double DirectBypassRate { get; set; }

    public bool RevalidationRequired { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatFoundationPromotionProtocol
{
    public const string Version =
        "paired-evidence-v7-conclusive-baseline";

    public const string SignificantImprovement = "significant-improvement";

    public const string EquivalentNonInferior = "equivalent-noninferior";

    public const string AbsoluteQualifiedBest = "absolute-qualified-best";

    public const string OfflineRejected = "offline-rejected";

    public const string OfflineSafe = "offline-safe";

    public const string ScreeningPassed = "screening-passed";

    public const string ConfirmedQualified = "confirmed-qualified";

    public const string Accepted = "accepted";

    public const string Regressed = "regressed";

    public const string InsufficientEvidence = "insufficient-evidence";

    public const double MinimumPairedWinWilsonLowerBound = 0.20d;

    public const double MinimumScoreGain = 0.01d;

    public const double MinimumDepthGain = 0.25d;

    public const int DefaultMinimumDiscordantPairs = 8;

    public const int MinimumNonInferiorityPairsPerDifficulty = 64;

    public const double MaximumPairedRegressionWilsonUpperBound = 0.05d;

    public const double DefaultMaximumOfflineHeadRegression = 0.05d;

    public const double DefaultMaximumStateFeatureCollisionRate = 0.05d;

    public const double TargetStateFeatureCollisionRate = 0.03d;

    public const double DefaultMaximumActionFeatureCollisionRate = 0.06d;
}

public static class CombatFoundationSemanticGateProtocol
{
    public const string Version =
        "semantic-admission-v5-actual-selected-transition";

    public const double MaximumSourceProjectionInvalidRate = 0.01d;

    public const double MaximumSourceProjectionMismatchRate = 0.05d;
}

public static class CombatFoundationDecisionDifferenceProtocol
{
    public const string Version = "foundation-decision-difference-v1";

    public const string AcceptanceDiagnosticPartition =
        "acceptance-diagnostic";

    public const string DevelopmentPartition = "development";

    public const string MiningPartition = "mining";
}

public sealed class CombatFoundationIntegritySeed
{
    public string DifficultyId { get; set; } = "advanced";

    public ulong WorldSeed { get; set; }
}

public static class CombatFoundationIntegritySeedCorpus
{
    public const string Version = "integrity-seeds-v2-dynamic-variable-base";

    public static IReadOnlyList<CombatFoundationIntegritySeed> KnownFailures {
        get;
    } = new[]
    {
        new CombatFoundationIntegritySeed
        {
            DifficultyId = "advanced",
            WorldSeed = 1904247873788260473UL
        },
        new CombatFoundationIntegritySeed
        {
            DifficultyId = "advanced",
            WorldSeed = 1630047245334700981UL
        },
        new CombatFoundationIntegritySeed
        {
            DifficultyId = "advanced",
            WorldSeed = 1465699506046447325UL
        },
        new CombatFoundationIntegritySeed
        {
            DifficultyId = "advanced",
            WorldSeed = 2049918757947132046UL
        }
    };
}

public enum CombatFoundationCounterfactualAdmission
{
    Rejected = 0,
    Improved = 1,
    Victory = 2
}

public sealed class CombatFoundationReplayArchiveReport
{
    public int Iteration { get; set; }

    public int SourceEpisodes { get; set; }

    public int ArchivedEpisodes { get; set; }

    public int DuplicateEpisodes { get; set; }

    public long ArchivedBytes { get; set; }

    public int LoadedHistoricalEpisodes { get; set; }

    public long LoadedHistoricalBytes { get; set; }

    public string WarehousePath { get; set; } = "";

    public string Error { get; set; } = "";
}

public sealed class CombatCampaignFoundationTrainingRequest
{
    public string GovernanceProfile { get; set; } =
        CombatFoundationGovernanceProfileNames.Release;

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public ulong RunSeed { get; set; }

    public string DecisionProfile { get; set; } = "balanced";

    public int Iterations { get; set; } = 12;

    public bool EnableIterationProcessIsolation { get; set; } = true;

    public int MaximumIterationsPerProcess { get; set; }

    public int AdditionalIterationsOnResume { get; set; } = 3;

    public int TrainingCampaignsPerIteration { get; set; } = 96;

    public int ArenaCampaignsPerDifficulty { get; set; } = 8;

    public int ArenaConfirmationCampaignsPerDifficulty { get; set; } = 56;

    public int ArenaEvaluationInterval { get; set; } = 6;

    public bool ArenaConfirmationFinalIterationOnly { get; set; } = true;

    public int NormalValidationCampaigns { get; set; } = 50;

    public int AdvancedValidationCampaigns { get; set; } = 50;

    public int CapabilityProbeCampaignsPerDifficulty { get; set; } = 16;

    public int CapabilityProbeTeacherCampaignsPerDifficulty { get; set; } = 4;

    public int CapabilityProbeBatchSize { get; set; } = 8;

    public bool RequireCapabilityProbeBaselineGain { get; set; } = true;

    public bool EnableCapabilityDecisionDifferenceDiagnostics { get; set; } =
        true;

    public int MaximumCapabilityDecisionDifferenceCases { get; set; } = 16;

    public int CapabilityProbeMinimumVictoryGain { get; set; } = 1;

    public double CapabilityProbeMinimumDepthGain { get; set; } = 0.5d;

    public int PreflightCampaignsPerDifficulty { get; set; }

    public ulong PreflightSeedStart { get; set; } = 1_000_000UL;

    public bool PreflightOnly { get; set; }

    public int MaximumDegreeOfParallelism { get; set; } = 1;

    public int ModelTrainingParallelism { get; set; } =
        Math.Max(
            1,
            Math.Min(
                CombatFoundationParallelismProtocol.MaximumSupportedParallelism,
                Environment.ProcessorCount));

    public string ParallelismProfile { get; set; } =
        CombatFoundationExecutionProfileNames.Custom;

    public string InferenceExecutionMode { get; set; } =
        CombatFoundationExecutionProfileNames.ShardedBatchInference;

    public int InferenceParallelism { get; set; }

    public int InferenceLaneCount { get; set; }

    public int InferenceBatchSize { get; set; }

    public int ThreadPoolMinimumWorkerThreads { get; set; }

    public int CheckpointSerializationParallelism { get; set; }

    public bool EnableMemoryCapacityParallelism { get; set; } = true;

    public long ParallelismPerLaneBytes { get; set; }

    public long ParallelismMemoryReserveBytes { get; set; }

    public bool ReuseAutoTuneCache { get; set; } = true;

    public int AutoTuneSampleCampaigns { get; set; } = 32;

    public double AutoTuneThroughputTolerance { get; set; } = 0.02d;

    public string AutoTuneObjective { get; set; } =
        CombatFoundationAutoTuneObjectiveNames.MaximumThroughput;

    public string AutoTuneHardwareKey { get; set; } = "";

    public string AutoTuneCampaignKey { get; set; } = "";

    public CombatFoundationAutoTuneResult? AutoTuneCache { get; set; }

    public Action<CombatFoundationAutoTuneResult>? AutoTuneCompleted { get; set; }

    public bool RetainValidationRunDetails { get; set; } = true;

    public bool EnableEarlyValidationStop { get; set; }

    public int ValidationEarlyStopBatchSize { get; set; } = 32;

    public bool EnableCurriculum { get; set; } = true;

    public bool EnableStratifiedReplay { get; set; } = true;

    public bool EnablePrioritizedReplay { get; set; } = true;

    public bool EnableReplayWarehouse { get; set; } = true;

    public int ReplayHotWindowEpisodeLimit { get; set; } = 2048;

    public int ReplayHotWindowFrameLimit { get; set; } = 96_000;

    public long ReplayHotWindowEstimatedBytesLimit { get; set; } =
        768L * 1024L * 1024L;

    public double ReplayCurrentIterationShare { get; set; } = 0.60d;

    public double ReplayHistoricalShare { get; set; } = 0.40d;

    public bool EnableHardSeedCurriculum { get; set; } = true;

    public bool EnableCounterfactualHardEncounters { get; set; } = true;

    public List<CombatFoundationHardSeedHistoryEntry> PinnedSeedHistory {
        get;
        set;
    } = new();

    public bool EnableSuccessCaseArchive { get; set; } = true;

    public bool EnableArenaRecovery { get; set; } = true;

    public int ArenaInvalidRetryCount { get; set; } = 1;

    public double ArenaInvalidRateLimit { get; set; } = 0.02d;

    public bool EnableTuningArena { get; set; } = true;

    public int TuningNormalCampaigns { get; set; } = 8;

    public int TuningAdvancedCampaigns { get; set; } = 16;

    public bool EnableProgressiveTuning { get; set; } = true;

    public int TuningInterval { get; set; } = 6;

    public bool EnableOfflineTuningGate { get; set; } = true;

    public int TuningScreeningNormalCampaigns { get; set; } = 4;

    public int TuningScreeningAdvancedCampaigns { get; set; } = 8;

    public int TuningFinalistCount { get; set; } = 1;

    public bool EnableSequentialArenaStop { get; set; } = true;

    public int ArenaEvaluationBatchSize { get; set; } = 16;

    public ulong TuningSeedStart { get; set; } = 1_500_000UL;

    public double NormalAcceptanceRate { get; set; } = 0.80d;

    public double AdvancedAcceptanceRate { get; set; } = 0.30d;

    public int MinimumArenaDiscordantPairs { get; set; } =
        CombatFoundationPromotionProtocol.DefaultMinimumDiscordantPairs;

    public double MaximumOfflineHeadRegression { get; set; } =
        CombatFoundationPromotionProtocol.DefaultMaximumOfflineHeadRegression;

    public double MaximumStateFeatureCollisionRate { get; set; } =
        CombatFoundationPromotionProtocol.DefaultMaximumStateFeatureCollisionRate;

    public double MaximumActionFeatureCollisionRate { get; set; } =
        CombatFoundationPromotionProtocol.DefaultMaximumActionFeatureCollisionRate;

    public string NativeProgramPackageHash { get; set; } = "";

    public string TrainingPolicyVersion { get; set; } =
        CombatFoundationTrainingProtocol.TrainingPolicyVersion;

    public double HardSeedReplayShare { get; set; } = 0.35d;

    public Dictionary<string, double> HardEncounterWeights { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public double MinimumAdvancedReplayShare { get; set; } = 0.40d;

    public double MinimumAdvancedDefeatReplayShare { get; set; } = 0.25d;

    public int ExpertReplayEpisodeLimit { get; set; }

    public int MaximumConsecutiveRejectedIterations { get; set; } =
        CombatFoundationStagnationProtocol
            .DefaultMaximumConsecutiveRejectedIterations;

    public List<CombatEpisode> ExpertReplayEpisodes { get; set; } = new();

    public List<CombatEpisode> AuthoritativeContentEpisodes { get; set; } = new();

    public double AuthoritativeContentReplayShare { get; set; } = 0.20d;

    public CombatFoundationExpertReplaySelection ExpertReplaySelection {
        get;
        set;
    } = new();

    public CombatFoundationRewardResidualTrainingResult RewardResidualTraining {
        get;
        set;
    } = new();

    public CombatFoundationCaseArchiveLoadDiagnostics CaseArchiveLoad {
        get;
        set;
    } = new();

    public string CaseArchiveCompatibilityKey { get; set; } = "";

    public double SelfPlayExplorationProbability { get; set; } = 0.15d;

    public double SelfPlayExplorationTemperature { get; set; } = 1d;

    public double TeacherExactBranchProbability { get; set; } = 0.15d;

    public double HardTeacherExactBranchProbability { get; set; } = 1d;

    public ulong TrainingSeedStart { get; set; } = 10_000UL;

    public ulong ArenaSeedStart { get; set; } = 1_000_000UL;

    public ulong ValidationSeedStart { get; set; } = 2_000_000UL;

    public CombatDecisionProfile Profile { get; set; } = new();

    public CombatPolicyValueTrainingOptions Training { get; set; } = new();

    public CombatTransformerTeacherOptions TransformerTeacher { get; set; } =
        new();

    public bool FinalizeTransformerTeacher { get; set; }

    public CombatCampaignDefinition TrainingCampaign { get; set; } = new();

    public CombatCampaignDefinition ValidationCampaign { get; set; } = new();

    public Action<int, int, string>? Progress { get; set; }

    public Action<CombatCampaignFoundationTelemetry>? Telemetry { get; set; }

    public bool IncludeMetricHistoryInTelemetry { get; set; } = true;

    public Action<CombatPolicyValueEpochMetrics>? ModelMetricRecorded {
        get;
        set;
    }

    public Action<CombatFoundationCampaignObservation>? ObservationRecorded {
        get;
        set;
    }

    public Action<CombatFoundationSuccessCase>? SuccessCaseRecorded {
        get;
        set;
    }

    /// <summary>
    /// Receives a complete success case before the trainer retains it. Return
    /// true after durably consuming the case so its heavyweight campaign and
    /// episode graph can be released immediately.
    /// </summary>
    public Func<CombatFoundationSuccessCase, bool>? SuccessCaseSink {
        get;
        set;
    }

    public CombatCampaignFoundationResumeState? Resume { get; set; }

    /// <summary>
    /// Allows the isolated Worker to transfer ownership of resume replay into
    /// the result instead of retaining a second cross-round reference graph.
    /// Library callers keep the non-mutating default.
    /// </summary>
    public bool ReleaseResumeReplayAfterTransfer { get; set; }

    public Action<CombatCampaignFoundationResumeState>? Checkpoint { get; set; }

    /// <summary>
    /// Waits for asynchronous checkpoint serialization before an iteration
    /// boundary measures memory and releases retained search arenas.
    /// </summary>
    public Action? IterationResourceBarrier { get; set; }

    public Func<
        int,
        IReadOnlyCollection<string>,
        int,
        long,
        IReadOnlyList<CombatEpisode>>? HistoricalReplaySource { get; set; }

    public Func<
        int,
        IReadOnlyList<CombatEpisode>,
        CombatFoundationReplayArchiveReport>? ReplayArchiveSink { get; set; }

    public List<CombatEpisode> ModelSelectionAnchorEpisodes { get; set; } = new();

    public Action<IReadOnlyList<CombatEpisode>>? ModelSelectionAnchorCreated {
        get;
        set;
    }
}

public sealed class CombatCampaignFoundationResumeState
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string Stage { get; set; } = "";

    public int NextIteration { get; set; }

    public int CompletedCampaigns { get; set; }

    public int GeneratedReplayEpisodes { get; set; }

    public ulong RunSeed { get; set; }

    public ulong TrainingSeedStart { get; set; }

    public ulong ArenaSeedStart { get; set; }

    public ulong TuningSeedStart { get; set; }

    public ulong ValidationSeedStart { get; set; }

    public int ModelRandomSeed { get; set; }

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public CombatPolicyValueNetworkDefinition? WorkingChampion { get; set; }

    public CombatPolicyValueNetworkDefinition? LatestTrainingModel { get; set; }

    public CombatFoundationPendingArenaCandidate? BestPendingArenaCandidate {
        get;
        set;
    }

    public CombatPolicyValueNetworkDefinition? AbsoluteQualifiedBestModel {
        get;
        set;
    }

    public CombatCampaignFoundationIteration? AbsoluteQualifiedBestEvidence {
        get;
        set;
    }

    public List<CombatEpisode> Replay { get; set; } = new();

    public List<CombatCampaignFoundationIteration> Iterations { get; set; } =
        new();

    public CombatCampaignFoundationIntegrityReport Preflight { get; set; } =
        new();

    public CombatPolicyValueTrainingResumeState? ModelTraining { get; set; }

    public CombatCampaignFoundationTelemetry Telemetry { get; set; } = new();

    public List<CombatFoundationHardSeedHistoryEntry> HardSeedHistory {
        get;
        set;
    } = new();

    public List<CombatFoundationTrainingSlot> TrainingSchedule { get; set; } =
        new();

    public int ArenaReplacementCursor { get; set; }

    public CombatFoundationCompatibilityManifest Compatibility { get; set; } =
        new();
}

/// <summary>
/// Preserves the best offline-safe model produced between Arena checkpoints.
/// This slot is intentionally independent from both the most recently trained
/// model and the formally qualified model.
/// </summary>
public sealed class CombatFoundationPendingArenaCandidate
{
    public int SourceIteration { get; set; }

    public CombatPolicyValueNetworkDefinition? Model { get; set; }

    public CombatPolicyValueMetricSnapshot BaselineValidationMetrics {
        get;
        set;
    } = new();

    public CombatPolicyValueMetricSnapshot TrainingMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot ValidationMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot SelectionAnchorMetrics {
        get;
        set;
    } = new();

    public CombatPolicyValueMetricSnapshot TestMetrics { get; set; } = new();

    public List<CombatPolicyValueEpochMetrics> EpochHistory { get; set; } =
        new();

    public int SelectedEpoch { get; set; }

    public double SelectedScore { get; set; }

    public bool OfflineHeadRegressionGatePassed { get; set; }

    public bool StrategyQuotaGatePassed { get; set; }

    public bool FeatureCollisionGatePassed { get; set; }

    public double StateFeatureCollisionRate { get; set; }

    public double ActionFeatureCollisionRate { get; set; }
}

public sealed class CombatFoundationCompatibilityManifest
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string RulesetHash { get; set; } = "";

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public string ActionContractVersion { get; set; } =
        CombatActionContractProtocol.Version;

    public string SemanticGateVersion { get; set; } =
        CombatFoundationSemanticGateProtocol.Version;

    public string IntegritySeedCorpusVersion { get; set; } =
        CombatFoundationIntegritySeedCorpus.Version;

    public string NativeProgramPackageHash { get; set; } = "";

    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public string TrainingCampaignHash { get; set; } = "";

    public string ValidationCampaignHash { get; set; } = "";

    public int FeatureSchemaVersion { get; set; }

    public string FeatureEncodingMode { get; set; } = "";

    public string SearchPolicyVersion { get; set; } =
        CombatFoundationTrainingProtocol.SearchPolicyVersion;

    public string CurriculumVersion { get; set; } =
        CombatFoundationTrainingProtocol.CurriculumVersion;

    public string TrainingPolicyVersion { get; set; } = "";

    public string TrainingSemanticsVersion { get; set; } =
        CombatPolicyValueProtocol.TrainingSemanticsVersion;

    public int StateDimensions { get; set; }

    public int ActionDimensions { get; set; }

    public int HiddenDimensions { get; set; }
}

public sealed class CombatCampaignFoundationTelemetry
{
    public string Stage { get; set; } = "";

    public string Phase { get; set; } = "";

    public int Iteration { get; set; }

    public int TotalIterations { get; set; }

    public int RunStartIteration { get; set; }

    public int RunIteration { get; set; }

    public int RunTotalIterations { get; set; }

    public int EffectiveParallelism { get; set; }

    public CombatFoundationParallelismDecision ParallelismDecision {
        get;
        set;
    } = new();

    public int ModelTrainingParallelism { get; set; }

    public string GovernanceProfile { get; set; } = "";

    public string ParallelismProfile { get; set; } = "";

    public string InferenceExecutionMode { get; set; } = "";

    public int InferenceParallelism { get; set; }

    public CombatFoundationAutoTuneResult AutoTune { get; set; } = new();

    public int InferenceLaneCount { get; set; }

    public int InferenceBatchSizePerLane { get; set; }

    public long InferenceRequests { get; set; }

    public long InferenceBatchEvaluations { get; set; }

    public long InferenceBatchedInputs { get; set; }

    public double InferenceAverageBatchSize { get; set; }

    public long InferenceFullBatchEvaluations { get; set; }

    public long InferenceTimeoutFlushes { get; set; }

    public long InferenceDirectFallbackRequests { get; set; }

    public long InferenceAdaptiveFallbackActivations { get; set; }

    public double InferenceAverageWaitMicroseconds { get; set; }

    public long InferenceDirectEvaluations { get; set; }

    public double InferenceAverageDirectEvaluationMicroseconds { get; set; }

    public double InferenceAverageDirectAllocatedBytes { get; set; }

    public double InferenceAverageSparseFeatureCount { get; set; }

    public double InferenceSparseFeatureDensity { get; set; }

    public double InferenceWeightMultiplicationReduction { get; set; }

    public int ActiveCampaigns { get; set; }

    public int PeakConcurrentCampaigns { get; set; }

    public int SchedulerQueuedWork { get; set; }

    public int SchedulerRunningWork { get; set; }

    public int SchedulerCompletedWork { get; set; }

    public int SchedulerCommittedWork { get; set; }

    public int SchedulerPeakRunningWork { get; set; }

    public long SchedulerRefillCount { get; set; }

    public int SchedulerSpeculativeDiscardedWork { get; set; }

    public double SchedulerTailIdleCoreSeconds { get; set; }

    public int ObservedWorkerThreads { get; set; }

    public int CompletedCampaigns { get; set; }

    public int RequestedCampaigns { get; set; }

    public int RunInitialCompletedCampaigns { get; set; }

    public int RunCompletedCampaigns { get; set; }

    public int RunRequestedCampaigns { get; set; }

    public int ExecutedCampaigns { get; set; }

    public int RunInitialExecutedCampaigns { get; set; }

    public int RunExecutedCampaigns { get; set; }

    public int CurrentPhaseCompletedCampaigns { get; set; }

    public int CurrentPhaseRequestedCampaigns { get; set; }

    public int CompletedBattles { get; set; }

    public int RunCompletedBattles { get; set; }

    public int CurrentPhaseCompletedBattles { get; set; }

    public int MaximumCompletedBattleDepth { get; set; }

    public int MaximumActiveBattleDepth { get; set; }

    public int Depth1To5Campaigns { get; set; }

    public int Depth6To10Campaigns { get; set; }

    public int Depth11To20Campaigns { get; set; }

    public int Depth21To30Campaigns { get; set; }

    public int Depth31To37Campaigns { get; set; }

    public double ProjectedBattleDepth { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public double EstimatedRemainingLowerSeconds { get; set; }

    public double EstimatedRemainingUpperSeconds { get; set; }

    public string EtaEstimatorVersion { get; set; } = "";

    public Dictionary<string, double> EtaStageSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int ModelEpoch { get; set; }

    public int ModelTotalEpochs { get; set; }

    public int ModelCompletedFrames { get; set; }

    public int ModelTotalFrames { get; set; }

    public double ModelEpochsPerSecond { get; set; }

    public double ModelValidationLoss { get; set; }

    public double ModelTrainingLoss { get; set; }

    public double ModelBestValidationLoss { get; set; }

    public int ModelBestEpoch { get; set; }

    public int ModelStaleEpochs { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public List<CombatPolicyValueEpochMetrics> ModelEpochHistory { get; set; } =
        new();

    public double PhaseEstimatedRemainingSeconds { get; set; }

    public string TransformerTeacherStage { get; set; } = "";

    public int TransformerTeacherEpoch { get; set; }

    public int TransformerTeacherTotalEpochs { get; set; }

    public int TransformerTeacherCompletedFrames { get; set; }

    public int TransformerTeacherTotalFrames { get; set; }

    public double TransformerTeacherFramesPerSecond { get; set; }

    public double TransformerTeacherElapsedSeconds { get; set; }

    public double TransformerTeacherCpuPercent { get; set; }

    public double TransformerTeacherProcessCpuSeconds { get; set; }

    public long TransformerTeacherWorkingSetBytes { get; set; }

    public long TransformerTeacherPeakWorkingSetBytes { get; set; }

    public double TransformerTeacherStageElapsedSeconds { get; set; }

    public bool TransformerTeacherWarmStarted { get; set; }

    public bool TransformerTeacherTrainingEnabled { get; set; }

    public string TransformerTeacherMessage { get; set; } = "";

    public long PolicyDecisions { get; set; }

    public long SearchSimulations { get; set; }

    public long RunSearchSimulations { get; set; }

    public long SearchNodes { get; set; }

    public double SearchMillisecondsTotal { get; set; }

    public long ObservationProjectionAllocatedBytes { get; set; }

    public long DecisionEngineAllocatedBytes { get; set; }

    public long SearchModelEvaluations { get; set; }

    public long SearchModelCacheHits { get; set; }

    public long SearchOriginalCandidates { get; set; }

    public long SearchRetainedCandidates { get; set; }

    public int SearchTimeBudgetStops { get; set; }

    public int SearchModelBudgetStops { get; set; }

    public int SearchEarlyStops { get; set; }

    public Dictionary<string, int> SearchBudgetTierCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int RuleTerminalOverrides { get; set; }

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }

    public long ExplorationDecisions { get; set; }

    public long ExplorationActionOverrides { get; set; }

    public double RootMaximumVisitShareMean { get; set; }

    public int RootMaximumVisitShareSamples { get; set; }

    public long AuthoritativeActionsAudited { get; set; }

    public long AuthoritativeSemanticMismatches { get; set; }

    public long AuthoritativeSelectedActionsAudited { get; set; }

    public long AuthoritativeSelectedSemanticMismatches { get; set; }

    public long AuthoritativeTeacherOverrides { get; set; }

    public Dictionary<string, int> AuthoritativeSemanticMismatchKinds {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> AuthoritativeSemanticMismatchSources {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> AuthoritativeSemanticMismatchScenarios {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    public CombatSemanticAuditMetrics SemanticAudit { get; set; } = new();

    public double SearchSimulationsPerSecond { get; set; }

    public double ElapsedSeconds { get; set; }

    public double CampaignsPerSecond { get; set; }

    public double CurrentPhaseCampaignsPerSecond { get; set; }

    public double CurrentPhaseSearchSimulationsPerSecond { get; set; }

    public double CurrentPhaseCpuUtilizationPercent { get; set; }

    public double CurrentPhaseAllocationMegabytesPerSecond { get; set; }

    public double BattlesPerSecond { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }

    public long AllocatedBytes { get; set; }

    public long EpisodeCompactStateVectors { get; set; }

    public long EpisodeCompactCandidateVectors { get; set; }

    public long EpisodeStateDictionaryMaterializations { get; set; }

    public long EpisodeCandidateDictionaryMaterializations { get; set; }

    public long WorldModelObservationsBuilt { get; set; }

    public long WorldModelObservationsSkipped { get; set; }

    public long WorkingSetBytes { get; set; }

    public long PrivateMemoryBytes { get; set; }

    public long GcHeapSizeBytes { get; set; }

    public long GcFragmentedBytes { get; set; }

    public long MemoryLoadBytes { get; set; }

    public long TotalAvailableMemoryBytes { get; set; }

    public double CpuSeconds { get; set; }

    public double CpuUtilizationPercent { get; set; }

    public double AllocationMegabytesPerSecond { get; set; }

    public Dictionary<string, double> PhaseElapsedSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> PhaseCpuSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, long> PhaseAllocatedBytes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> PhaseExternalCpuSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PhasePeakConcurrentWork { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PhaseObservedWorkerThreads { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatCampaignFoundationIteration
{
    public bool HadIncumbentModel { get; set; }

    public int Iteration { get; set; }

    public bool ArenaEvaluationRan { get; set; }

    public bool FormalArenaConfirmationScheduled { get; set; }

    public bool TrainingOnlyIteration { get; set; }

    public int ArenaCandidateSourceIteration { get; set; }

    public bool ArenaCandidateSelectedFromPendingBank { get; set; }

    public bool PendingArenaCandidateRetained { get; set; }

    public string CandidateQualificationState { get; set; } =
        CombatFoundationPromotionProtocol.OfflineRejected;

    public bool ScreeningQualificationGatePassed { get; set; }

    public bool FormalConfirmationCompleted { get; set; }

    public int WorkerProcessId { get; set; }

    public CombatFoundationParallelismDecision ParallelismDecision {
        get;
        set;
    } = new();

    public int ReplayEpisodes { get; set; }

    public int TrainingReplayEpisodes { get; set; }

    public int TrainingReplayNormalEpisodes { get; set; }

    public int TrainingReplayAdvancedEpisodes { get; set; }

    public int TrainingReplayAdvancedDefeatEpisodes { get; set; }

    public int TrainingReplaySuccessfulEpisodes { get; set; }

    public int TrainingReplayDroppedDuplicates { get; set; }

    public double TrainingReplayTargetNormalShare { get; set; }

    public double TrainingReplayTargetAdvancedDefeatShare { get; set; }

    public int TrainingReplaySourceCampaigns { get; set; }

    public int TrainingReplaySelectedCampaigns { get; set; }

    public int TrainingReplaySuccessfulCampaigns { get; set; }

    public double TrainingReplaySourcePriorityMean { get; set; }

    public double TrainingReplaySelectedPriorityMean { get; set; }

    public int TrainingReplayHighPriorityEpisodes { get; set; }

    public int TrainingReplayPinnedContentEpisodes { get; set; }

    public int TrainingReplayFrames { get; set; }

    public long TrainingReplayEstimatedResidentBytes { get; set; }

    public int ReplayArchivedEpisodes { get; set; }

    public int ReplayArchiveDuplicates { get; set; }

    public long ReplayArchivedBytes { get; set; }

    public int ReplayLoadedHistoricalEpisodes { get; set; }

    public long ReplayLoadedHistoricalBytes { get; set; }

    public int ReplayPinnedCurrentIterationEpisodes { get; set; }

    public int TrainingReplayResourceBudgetDroppedEpisodes { get; set; }

    public double ResourceElapsedSeconds { get; set; }

    public double ResourceCpuSeconds { get; set; }

    public double ResourceCpuUtilizationPercent { get; set; }

    public long ResourceAllocatedBytes { get; set; }

    public long ResourceWorkingSetBytes { get; set; }

    public long ResourcePrivateMemoryBytes { get; set; }

    public long ResourceGcHeapSizeBytes { get; set; }

    public long ResourceGcFragmentedBytes { get; set; }

    public long ResourceMemoryLoadBytes { get; set; }

    public long ResourceTotalAvailableMemoryBytes { get; set; }

    public Dictionary<string, int> TrainingReplayQuotaShortfalls { get; set; } =
        new(StringComparer.Ordinal);

    public int HardSeedSourceCampaigns { get; set; }

    public int HardSeedRoutedBuildLimitedCampaigns { get; set; }

    public int HardSeedRoutedProvisionalBuildLimitedCampaigns { get; set; }

    public int HardSeedTrainingCampaigns { get; set; }

    public int HardSeedTrainingVictories { get; set; }

    public int HardSeedEncounterCampaigns { get; set; }

    public int HardSeedCounterfactualCampaigns { get; set; }

    public int HardSeedCounterfactualVictories { get; set; }

    public int HardSeedCounterfactualImprovements { get; set; }

    public int HardSeedCounterfactualRejected { get; set; }

    public int AdvancedLocalCurriculumAttempts { get; set; }

    public int AdvancedLocalCurriculumSuccesses { get; set; }

    public double EffectiveHardSeedReplayShare { get; set; }

    public Dictionary<string, int> HardSeedClusters { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> HardSeedSourceCategories { get; set; } =
        new(StringComparer.Ordinal);

    public int AdvancedTrainingCampaigns { get; set; }

    public double EffectiveMinimumAdvancedReplayShare { get; set; }

    public string CurriculumStage { get; set; } = "";

    public double NormalWilsonLowerBound { get; set; }

    public double AdvancedWilsonLowerBound { get; set; }

    public double SelfPlayExplorationProbability { get; set; }

    public int TeacherStudentPoolSourceFrames { get; set; }

    public int TeacherStudentPoolAvailableSourceFrames { get; set; }

    public int TeacherStudentPoolSelectedFrames { get; set; }

    public int TeacherStudentPoolDroppedFrames { get; set; }

    public int TeacherStudentPoolUnsafeEndTurnFrames { get; set; }

    public double TeacherStudentPoolSourcePriorityMean { get; set; }

    public double TeacherStudentPoolSelectedPriorityMean { get; set; }

    public int TeacherStudentPoolHighPriorityFrames { get; set; }

    public bool TeacherStudentPoolStrategyQuotaActive { get; set; }

    public bool TeacherStudentPoolStrategyQuotaPassed { get; set; } = true;

    public Dictionary<string, int> TeacherStudentPoolStrategyFrames {
        get;
        set;
    } = new(StringComparer.Ordinal);

    public Dictionary<string, int> TeacherStudentPoolAvailableStrategyFrames {
        get;
        set;
    } = new(StringComparer.Ordinal);

    public Dictionary<string, int> TeacherStudentPoolSourceStrategyFrames {
        get;
        set;
    } = new(StringComparer.Ordinal);

    public Dictionary<string, int> TeacherStudentPoolQuotaShortfalls {
        get;
        set;
    } = new(StringComparer.Ordinal);

    public bool StrategyQuotaRepairAttempted { get; set; }

    public int StrategyQuotaRepairSourceEpisodes { get; set; }

    public int StrategyQuotaRepairAddedEpisodes { get; set; }

    public int StrategyQuotaCollectionCampaigns { get; set; }

    public int StrategyQuotaCollectionEpisodes { get; set; }

    public Dictionary<string, int> StrategyQuotaCollectionDifficultyCampaigns {
        get;
        set;
    } = new(StringComparer.Ordinal);

    public Dictionary<string, int> StrategyQuotaCollectionYieldFrames {
        get;
        set;
    } = new(StringComparer.Ordinal);

    public Dictionary<string, int> ModelFrameStrata { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> ModelEncodedStrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public CombatFoundationInferenceHealth InferenceHealth { get; set; } =
        new();

    public double ModelMinimumFrameWeight { get; set; } = 1d;

    public double ModelMaximumFrameWeight { get; set; } = 1d;

    public int ModelDroppedFramesByEpisodeCap { get; set; }

    public int ModelTrainingFrameCount { get; set; }

    public int ModelDroppedUnsafeEndTurnFrames { get; set; }

    public int ModelDroppedPolicyIntegrityFrames { get; set; }

    public int ModelEndTurnDecisionFrames { get; set; }

    public int ModelUnsafeEndTurnFrames { get; set; }

    public int ModelUnsafeEndTurnPolicyFrames { get; set; }

    public int ModelUnsafeEndTurnRiskAuxiliaryFrames { get; set; }

    public CombatPolicyValueMetricSnapshot ModelBaselineValidationMetrics {
        get;
        set;
    } = new();

    public double ModelMeanPolicyTargetMaximum { get; set; }

    public CombatPolicyValueMetricSnapshot ModelTrainingMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot ModelValidationMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot ModelSelectionAnchorMetrics {
        get;
        set;
    } = new();

    public CombatTransformerTeacherReport TransformerTeacher { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot ModelTestMetrics { get; set; } =
        new();

    public List<CombatPolicyValueEpochMetrics> ModelEpochHistory { get; set; } =
        new();

    public string CandidateModelId { get; set; } = "";

    public int TuningSelectedEpoch { get; set; }

    public double TuningSelectedScore { get; set; }

    public int TuningCandidateCount { get; set; }

    public int TuningOfflineRejectedCandidates { get; set; }

    public bool TuningAllCandidatesRejectedOffline { get; set; }

    public bool TuningEvaluationRan { get; set; }

    public int TuningInvalidCampaigns { get; set; }

    public int TuningFinalistCount { get; set; }

    public int TuningCampaignsExecuted { get; set; }

    public int TuningCampaignsSaved { get; set; }

    public double ChampionArenaScore { get; set; }

    public double CandidateArenaScore { get; set; }

    public double ChampionNormalWinRate { get; set; }

    public double CandidateNormalWinRate { get; set; }

    public double ChampionAdvancedWinRate { get; set; }

    public double CandidateAdvancedWinRate { get; set; }

    public int InvalidCandidateCampaigns { get; set; }

    public int InvalidChampionCampaigns { get; set; }

    public int ArenaRetryAttempts { get; set; }

    public int ArenaRecoveredCampaigns { get; set; }

    public int ArenaIsolatedPairs { get; set; }

    public int ArenaReplacementPairs { get; set; }

    public Dictionary<string, int> ArenaInvalidSignatures { get; set; } =
        new(StringComparer.Ordinal);

    public int ValidArenaPairs { get; set; }

    public int ArenaScreeningPairs { get; set; }

    public int ArenaScreeningPairsSaved { get; set; }

    public bool ArenaScreeningDiagnosticOnly { get; set; }

    public bool ArenaScreeningStoppedEarly { get; set; }

    public int ArenaConfirmationPairs { get; set; }

    public bool ArenaConfirmationStoppedEarly { get; set; }

    public bool ArenaConfirmationAcceptedEarly { get; set; }

    public int ArenaConfirmationPairsSaved { get; set; }

    public int ArenaHardSeedsMined { get; set; }

    public int ValidNormalArenaPairs { get; set; }

    public int ValidAdvancedArenaPairs { get; set; }

    public int CandidateOnlyWins { get; set; }

    public int ChampionOnlyWins { get; set; }

    public double PairedWinWilsonLowerBound { get; set; }

    public int ArenaDiscordantPairs { get; set; }

    public bool ArenaEvidenceGatePassed { get; set; }

    public string PairedEvidenceKind { get; set; } =
        CombatFoundationPromotionProtocol.InsufficientEvidence;

    public double PairedRegressionWilsonUpperBound { get; set; }

    public bool NonInferiorityGatePassed { get; set; }

    public bool AbsoluteNormalGatePassed { get; set; }

    public bool AbsoluteAdvancedGatePassed { get; set; }

    public bool AbsoluteQualificationGatePassed { get; set; }

    public bool QualifiedCandidateSelected { get; set; }

    public bool OfflineHeadRegressionGatePassed { get; set; }

    public bool StrategyQuotaGatePassed { get; set; }

    public double StateFeatureCollisionRate { get; set; }

    public double ActionFeatureCollisionRate { get; set; }

    public bool FeatureCollisionGatePassed { get; set; }

    public double CandidateScoreGain { get; set; }

    public double CandidateDepthGain { get; set; }

    public string IterativeGainKind { get; set; } = "";

    public string PromotionProtocolVersion { get; set; } = "";

    public double ChampionAverageCompletedBattles { get; set; }

    public double CandidateAverageCompletedBattles { get; set; }

    public bool Promoted { get; set; }

    public bool ProvisionalChampionSelected { get; set; }

    public bool CurriculumCheckpointAccepted { get; set; }

    public bool WorkingCheckpointAccepted { get; set; }

    public bool WorkingModelAccepted { get; set; }

    public string PromotionKind { get; set; } = "rejected";

    public string PromotionReason { get; set; } = "";

    public int ConsecutiveRejectedIterations { get; set; }

    public bool ParetoProgress { get; set; }

    public string WorkingModelBankSlot { get; set; } = "";

    public bool ProductiveProgress { get; set; }

    public List<string> ProductiveProgressReasons { get; set; } = new();

    public bool BehavioralProductiveProgress { get; set; }

    public List<string> BehavioralProductiveProgressReasons { get; set; } =
        new();

    public bool DataPipelineProgress { get; set; }

    public List<string> DataPipelineProgressReasons { get; set; } = new();

    public int ConsecutiveUnproductiveIterations { get; set; }

    public int ConsecutiveDataOnlyIterations { get; set; }

    public bool StagnationStopTriggered { get; set; }
}

public sealed class CombatFoundationValidationSeedPlan
{
    public List<ulong> NormalWorldSeeds { get; set; } = new();

    public List<ulong> AdvancedWorldSeeds { get; set; } = new();

    public string PlanHash { get; set; } = "";
}

public static class CombatFoundationValidationSeedSampler
{
    public const string Version = "foundation-validation-random-seeds-v1";

    public static CombatFoundationValidationSeedPlan Create(
        ulong runSeed,
        ulong validationSeedStart,
        int normalCount,
        int advancedCount)
    {
        var seen = new HashSet<ulong>();
        var normal = Sample(
            runSeed,
            validationSeedStart,
            0x6e6f726d616cUL,
            Math.Max(0, normalCount),
            seen);
        var advanced = Sample(
            runSeed,
            validationSeedStart,
            0x616476616e636564UL,
            Math.Max(0, advancedCount),
            seen);
        var canonical = Version
                        + "|"
                        + string.Join(",", normal)
                        + "|"
                        + string.Join(",", advanced);
        using var sha256 = SHA256.Create();
        var hash = BitConverter.ToString(
                sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .Replace("-", "")
            .ToLowerInvariant();
        return new CombatFoundationValidationSeedPlan
        {
            NormalWorldSeeds = normal,
            AdvancedWorldSeeds = advanced,
            PlanHash = hash
        };
    }

    private static List<ulong> Sample(
        ulong runSeed,
        ulong seedStart,
        ulong domain,
        int count,
        ISet<ulong> seen)
    {
        var result = new List<ulong>(count);
        for (var index = 0; result.Count < count; index++)
        {
            var candidate = Mix(
                runSeed
                ^ seedStart
                ^ domain
                ^ unchecked((ulong)index * 0x9e3779b97f4a7c15UL));
            if (seen.Add(candidate))
            {
                result.Add(candidate);
            }
        }
        return result;
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9e3779b97f4a7c15UL;
        value = (value ^ value >> 30) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ value >> 27) * 0x94d049bb133111ebUL;
        return value ^ value >> 31;
    }
}

public sealed class CombatCampaignFoundationValidation
{
    public string SampleProtocol { get; set; } =
        CombatFoundationValidationSeedSampler.Version;

    public bool RandomSampling { get; set; } = true;

    public string SamplePlanHash { get; set; } = "";

    public List<ulong> NormalWorldSeeds { get; set; } = new();

    public List<ulong> AdvancedWorldSeeds { get; set; } = new();

    public int CampaignsPerDifficulty { get; set; }

    public int NormalPlannedCampaigns { get; set; }

    public int AdvancedPlannedCampaigns { get; set; }

    public int NormalCampaigns { get; set; }

    public int AdvancedCampaigns { get; set; }

    public string NormalStatus { get; set; } = "not-run";

    public string AdvancedStatus { get; set; } = "not-run";

    public int NormalVictories { get; set; }

    public int AdvancedVictories { get; set; }

    public int RequiredNormalVictories { get; set; }

    public int RequiredAdvancedVictories { get; set; }

    public double RequiredNormalWinRate { get; set; }

    public double RequiredAdvancedWinRate { get; set; }

    public int InvalidCampaigns { get; set; }

    public double NormalWinRate { get; set; }

    public double AdvancedWinRate { get; set; }

    public double NormalWilsonLowerBound { get; set; }

    public double AdvancedWilsonLowerBound { get; set; }

    public int VoluntaryEndTurns { get; set; }

    public int EmptyEndTurns { get; set; }

    public int EndTurnsWithUnusedEnergy { get; set; }

    public int UnusedEnergyAtEndTurns { get; set; }

    public int AvoidableEndTurnsWithUnusedEnergy { get; set; }

    public int AvoidableUnusedEnergyAtEndTurns { get; set; }

    public int SaturatedEndTurnsWithUnusedEnergy { get; set; }

    public int SevereEndTurnMistakes { get; set; }

    public int DominatedEndTurns { get; set; }

    public int EndTurnsIntoAvoidableLethal { get; set; }

    public int EndTurnsWithCertifiedCycle { get; set; }

    public int EndTurnsWithUnknownLifecycle { get; set; }

    public int EndTurnsWithBankedSurplus { get; set; }

    public int BankedSurplusAtEndTurns { get; set; }

    public int MaximumConsecutiveNoProgressTurns { get; set; }

    public int NoEffectActionAttempts { get; set; }

    public int RepeatedNoEffectActionAttempts { get; set; }

    public int GuaranteedNoEffectActionAttempts { get; set; }

    public int InteractiveActionContractFailures { get; set; }

    public bool BehaviorPassed { get; set; }

    public bool Passed { get; set; }

    public bool EarlyStopped { get; set; }

    public string EarlyStopReason { get; set; } = "";
}

public sealed class CombatCampaignFoundationIntegrityFailure
{
    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public int CompletedBattles { get; set; }

    public List<string> Reasons { get; set; } = new();
}

public sealed class CombatCampaignFoundationIntegrityReport
{
    public string SemanticGateVersion { get; set; } =
        CombatFoundationSemanticGateProtocol.Version;

    public string IntegritySeedCorpusVersion { get; set; } =
        CombatFoundationIntegritySeedCorpus.Version;

    public int CampaignsPerDifficulty { get; set; }

    public int RegressionSeedCampaigns { get; set; }

    public int CompletedCampaigns { get; set; }

    public int InvalidCampaigns { get; set; }

    public int TerminalConsistencyViolations { get; set; }

    public int SelectedInvalidActions { get; set; }

    public int SelectedUnexplainedMismatchActions { get; set; }

    public int SelectedSourceProjectionInvalidActions { get; set; }

    public int SelectedSourceProjectionUnexplainedMismatchActions {
        get;
        set;
    }

    public double SourceProjectionInvalidRate { get; set; }

    public double SourceProjectionMismatchRate { get; set; }

    public bool SemanticGatePassed { get; set; }

    public bool Passed { get; set; }

    public Dictionary<string, int> FailureCounts { get; set; } =
        new(StringComparer.Ordinal);

    public List<CombatCampaignFoundationIntegrityFailure> Failures { get; set; } =
        new();
}

public sealed class CombatFoundationCapabilityProbeArm
{
    public string ArmId { get; set; } = "";

    public int NormalCampaigns { get; set; }

    public int NormalVictories { get; set; }

    public int AdvancedCampaigns { get; set; }

    public int AdvancedVictories { get; set; }

    public int InvalidCampaigns { get; set; }

    public double AverageCompletedBattles { get; set; }

    public double NormalWilsonLowerBound { get; set; }

    public double AdvancedWilsonLowerBound { get; set; }
}

public sealed class CombatFoundationCapabilityProbePair
{
    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public bool BaselineVictory { get; set; }

    public bool ChampionVictory { get; set; }

    public int BaselineCompletedBattles { get; set; }

    public int ChampionCompletedBattles { get; set; }

    public bool BaselineInvalid { get; set; }

    public bool ChampionInvalid { get; set; }
}

public sealed class CombatFoundationDecisionCandidateTrace
{
    public string CandidateId { get; set; } = "";

    public int SearchVisits { get; set; }

    public double SearchPrior { get; set; }

    public double SearchValue { get; set; }

    public double SearchDeathRisk { get; set; }

    public double SearchMeanReturn { get; set; }

    public double SearchReturnStandardError { get; set; }

    public double BaseRuleScore { get; set; }

    public double RawResidualScore { get; set; }

    public double ResidualApplicability { get; set; }

    public double AppliedResidualScore { get; set; }

    public double RuleScore { get; set; }

    public double SearchLowerTailMean { get; set; }
}

public sealed class CombatFoundationDecisionDifferenceCase
{
    public string Protocol { get; set; } =
        CombatFoundationDecisionDifferenceProtocol.Version;

    public string DataPartition { get; set; } =
        CombatFoundationDecisionDifferenceProtocol.AcceptanceDiagnosticPartition;

    public bool TrainingEligible { get; set; }

    public bool AcceptanceSeedRetired { get; set; }

    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public int JourneyBattleIndex { get; set; }

    public long DecisionSequence { get; set; }

    public string StateFingerprint { get; set; } = "";

    public bool BaselineVictory { get; set; }

    public bool ChampionVictory { get; set; }

    public int BaselineCompletedBattles { get; set; }

    public int ChampionCompletedBattles { get; set; }

    public string PreferredCandidateId { get; set; } = "";

    public string FailureCategory { get; set; } = "";

    public double Confidence { get; set; }

    public CombatFoundationDecisionCandidateTrace BaselineDecision {
        get;
        set;
    } = new();

    public CombatFoundationDecisionCandidateTrace ChampionDecision {
        get;
        set;
    } = new();

    public CombatFoundationDecisionCandidateTrace ChampionViewOfBaselineAction {
        get;
        set;
    } = new();
}

public sealed class CombatFoundationCapabilityProbe
{
    public int CampaignsPerDifficulty { get; set; }

    public int MaximumCampaignsPerDifficulty { get; set; }

    public int NormalCampaignsExecuted { get; set; }

    public int AdvancedCampaignsExecuted { get; set; }

    public bool AdaptiveExpansionUsed { get; set; }

    public bool SaturatedNormalExpansionSkipped { get; set; }

    public ulong SeedStart { get; set; }

    public List<CombatFoundationCapabilityProbeArm> Arms { get; set; } =
        new();

    public List<CombatFoundationCapabilityProbePair> Pairs { get; set; } =
        new();

    public List<CombatFoundationDecisionDifferenceCase> DecisionDifferences {
        get;
        set;
    } = new();

    public string DecisionDifferencePartition { get; set; } =
        CombatFoundationDecisionDifferenceProtocol.AcceptanceDiagnosticPartition;

    public List<int> CompletedStages { get; set; } = new();

    public bool StoppedEarly { get; set; }

    public int CampaignsSaved { get; set; }

    public bool BaselineGateRequired { get; set; }

    public bool PassedBaselineGate { get; set; }

    public int ChampionVictoryGain { get; set; }

    public double ChampionDepthGain { get; set; }

    public int ChampionOnlyWins { get; set; }

    public int BaselineOnlyWins { get; set; }

    public int NormalChampionOnlyWins { get; set; }

    public int NormalBaselineOnlyWins { get; set; }

    public int AdvancedChampionOnlyWins { get; set; }

    public int AdvancedBaselineOnlyWins { get; set; }

    public double PairedWinWilsonLowerBound { get; set; }

    public double PairedWinWilsonUpperBound { get; set; }

    public double PairedLossMedianDepthGain { get; set; }

    public int PairedLossPairs { get; set; }

    public bool DepthGainEvidencePassed { get; set; }

    public string BaselineGateVerdict { get; set; } = "inconclusive";

    public string BaselineGateReason { get; set; } = "";
}

public sealed class CombatCampaignFoundationArenaFailure
{
    public int Iteration { get; set; }

    public string Competitor { get; set; } = "";

    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public int CompletedBattles { get; set; }

    public List<string> Reasons { get; set; } = new();
}

public sealed class CombatCampaignFoundationTrainingResult
{
    public ulong RunSeed { get; set; }

    public ulong TrainingSeedStart { get; set; }

    public ulong ArenaSeedStart { get; set; }

    public ulong ValidationSeedStart { get; set; }

    public int ModelRandomSeed { get; set; }

    public ulong TuningSeedStart { get; set; }

    public bool Success { get; set; }

    public bool AcceptancePassed { get; set; }

    public bool FormalModelBlocked { get; set; }

    public string FormalModelBlockReason { get; set; } = "";

    public bool ContinuationRequired { get; set; }

    public bool IterationProcessIsolationEnabled { get; set; }

    public int NextIteration { get; set; }

    public int ResolvedIterationLimit { get; set; }

    public string AcceptanceKind { get; set; } = "";

    public string Message { get; set; } = "";

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public CombatPolicyValueNetworkDefinition? WorkingChampion { get; set; }

    public CombatPolicyValueNetworkDefinition? LatestTrainingModel { get; set; }

    public CombatFoundationPendingArenaCandidate? BestPendingArenaCandidate {
        get;
        set;
    }

    public CombatPolicyValueNetworkDefinition? AbsoluteQualifiedBestModel {
        get;
        set;
    }

    public CombatCampaignFoundationIteration? AbsoluteQualifiedBestEvidence {
        get;
        set;
    }

    public int QualifiedCandidateCount { get; set; }

    public int SelectedQualifiedCandidateIteration { get; set; }

    public string SelectedQualifiedCandidateModelId { get; set; } = "";

    public string EvaluatedModelId { get; set; } = "";

    public int EvaluatedModelIteration { get; set; }

    public bool EvaluatedModelDeploymentQualified { get; set; }

    public List<CombatEpisode> Replay { get; set; } = new();

    public int GeneratedReplayEpisodes { get; set; }

    public int PersistedReplayEpisodes { get; set; }

    public int ReplayArchivedEpisodes { get; set; }

    public int ReplayArchiveDuplicates { get; set; }

    public long ReplayArchivedBytes { get; set; }

    public int LoadedHistoricalReplayEpisodes { get; set; }

    public long LoadedHistoricalReplayBytes { get; set; }

    public string ReplayWarehousePath { get; set; } = "";

    public string ReplayWarehouseError { get; set; } = "";

    public int DiscardedCounterfactualEpisodes { get; set; }

    public int LoadedExpertReplayEpisodes { get; set; }

    public int LoadedAuthoritativeContentEpisodes { get; set; }

    public CombatFoundationExpertReplaySelection ExpertReplaySelection {
        get;
        set;
    } = new();

    public CombatFoundationRewardResidualTrainingResult RewardResidualTraining {
        get;
        set;
    } = new();

    public CombatFoundationCaseArchiveLoadDiagnostics CaseArchiveLoad {
        get;
        set;
    } = new();

    public int ArchivedSuccessCases { get; set; }

    public int DuplicateSuccessCases { get; set; }

    public int ArchiveCapacityRejectedObservations { get; set; }

    public int ArchiveCapacityRejectedCases { get; set; }

    public long ExpertReferenceBytes { get; set; }

    public long DeduplicatedExpertBytes { get; set; }

    public string SuccessArchiveDirectory { get; set; } = "";

    public string SuccessCaseIndexPath { get; set; } = "";

    public string BuildLimitedSeedIndexPath { get; set; } = "";

    public int BuildLimitedSeedCases { get; set; }

    public int ProvisionalBuildLimitedSeedCases { get; set; }

    public string DecisionDifferencePath { get; set; } = "";

    public int DecisionDifferenceCases { get; set; }

    public string SuccessArchiveError { get; set; } = "";

    public List<CombatFoundationCampaignObservation> CampaignObservations {
        get;
        set;
    } = new();

    public List<CombatFoundationSuccessCase> SuccessCases { get; set; } = new();

    public CombatFoundationCaseAnalysis CaseAnalysis { get; set; } = new();

    public CombatFoundationCompatibilityManifest Compatibility { get; set; } =
        new();

    public List<CombatFoundationHardSeedHistoryEntry> HardSeedHistory {
        get;
        set;
    } = new();

    public int ArenaRetryAttempts { get; set; }

    public int ArenaRecoveredCampaigns { get; set; }

    public int ArenaIsolatedPairs { get; set; }

    public int ArenaReplacementPairs { get; set; }

    public Dictionary<string, int> ArenaInvalidSignatures { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> SearchBudgetTierCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<CombatCampaignFoundationIteration> Iterations { get; set; } = new();

    public List<CombatTransformerTeacherReport> TransformerTeacherReports {
        get;
        set;
    } = new();

    public bool StoppedForStagnation { get; set; }

    public int ConsecutiveRejectedIterations { get; set; }

    public int ConsecutiveUnproductiveIterations { get; set; }

    public int ConsecutiveDataOnlyIterations { get; set; }

    public string IterationStopReason { get; set; } = "";

    public CombatCampaignFoundationValidation Validation { get; set; } = new();

    public CombatCampaignFoundationIntegrityReport Preflight { get; set; } = new();

    public CombatFoundationCapabilityProbe CapabilityProbe { get; set; } =
        new();

    public List<CombatCampaignResult> ValidationRuns { get; set; } = new();

    public int RequestedCampaigns { get; set; }

    public int CompletedCampaigns { get; set; }

    public int ExecutedCampaigns { get; set; }

    public int InvalidTrainingCampaigns { get; set; }

    public int DiscardedInvalidEpisodes { get; set; }

    public bool SemanticGatePassed { get; set; } = true;

    public int SemanticRejectedCampaigns { get; set; }

    public int DiscardedSemanticEpisodes { get; set; }

    public string SemanticGateFailureReason { get; set; } = "";

    public int TerminalConsistencyViolations { get; set; }

    public int FeatureLeakageViolations { get; set; }

    public Dictionary<string, int> TrainingFailureCounts { get; set; } =
        new(StringComparer.Ordinal);

    public List<CombatCampaignFoundationIntegrityFailure> TrainingFailures {
        get;
        set;
    } = new();

    public Dictionary<string, int> ArenaFailureCounts { get; set; } =
        new(StringComparer.Ordinal);

    public List<CombatCampaignFoundationArenaFailure> ArenaFailures { get; set; } =
        new();

    public string EarlyStopReason { get; set; } = "";

    public int EffectiveParallelism { get; set; }

    public CombatFoundationParallelismDecision ParallelismDecision {
        get;
        set;
    } = new();

    public int ModelTrainingParallelism { get; set; }

    public string GovernanceProfile { get; set; } = "";

    public string ParallelismProfile { get; set; } = "";

    public string InferenceExecutionMode { get; set; } = "";

    public int InferenceParallelism { get; set; }

    public CombatFoundationAutoTuneResult AutoTune { get; set; } = new();

    public int InferenceLaneCount { get; set; }

    public int InferenceBatchSizePerLane { get; set; }

    public long InferenceRequests { get; set; }

    public long InferenceBatchEvaluations { get; set; }

    public long InferenceBatchedInputs { get; set; }

    public double InferenceAverageBatchSize { get; set; }

    public long InferenceFullBatchEvaluations { get; set; }

    public long InferenceTimeoutFlushes { get; set; }

    public long InferenceDirectFallbackRequests { get; set; }

    public long InferenceAdaptiveFallbackActivations { get; set; }

    public double InferenceAverageWaitMicroseconds { get; set; }

    public long InferenceDirectEvaluations { get; set; }

    public double InferenceAverageDirectEvaluationMicroseconds { get; set; }

    public double InferenceAverageDirectAllocatedBytes { get; set; }

    public double InferenceAverageSparseFeatureCount { get; set; }

    public double InferenceSparseFeatureDensity { get; set; }

    public double InferenceWeightMultiplicationReduction { get; set; }

    public int PeakConcurrentCampaigns { get; set; }

    public int SchedulerPeakRunningWork { get; set; }

    public long SchedulerRefillCount { get; set; }

    public int SchedulerSpeculativeDiscardedWork { get; set; }

    public double SchedulerTailIdleCoreSeconds { get; set; }

    public int ObservedWorkerThreads { get; set; }

    public int CompletedBattles { get; set; }

    public int MaximumCompletedBattleDepth { get; set; }

    public int Depth1To5Campaigns { get; set; }

    public int Depth6To10Campaigns { get; set; }

    public int Depth11To20Campaigns { get; set; }

    public int Depth21To30Campaigns { get; set; }

    public int Depth31To37Campaigns { get; set; }

    public double ProjectedBattleDepth { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public double EstimatedRemainingLowerSeconds { get; set; }

    public double EstimatedRemainingUpperSeconds { get; set; }

    public string EtaEstimatorVersion { get; set; } = "";

    public Dictionary<string, double> EtaStageSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public long TransformerTeacherPeakWorkingSetBytes { get; set; }

    public long PolicyDecisions { get; set; }

    public long SearchSimulations { get; set; }

    public long SearchNodes { get; set; }

    public double SearchMillisecondsTotal { get; set; }

    public long ObservationProjectionAllocatedBytes { get; set; }

    public long DecisionEngineAllocatedBytes { get; set; }

    public long SearchModelEvaluations { get; set; }

    public long SearchModelCacheHits { get; set; }

    public long SearchOriginalCandidates { get; set; }

    public long SearchRetainedCandidates { get; set; }

    public int SearchTimeBudgetStops { get; set; }

    public int SearchModelBudgetStops { get; set; }

    public int SearchEarlyStops { get; set; }

    public int RuleTerminalOverrides { get; set; }

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }

    public long ExplorationDecisions { get; set; }

    public long ExplorationActionOverrides { get; set; }

    public double RootMaximumVisitShareMean { get; set; }

    public int RootMaximumVisitShareSamples { get; set; }

    public long AuthoritativeActionsAudited { get; set; }

    public long AuthoritativeSemanticMismatches { get; set; }

    public long AuthoritativeSelectedActionsAudited { get; set; }

    public long AuthoritativeSelectedSemanticMismatches { get; set; }

    public long AuthoritativeTeacherOverrides { get; set; }

    public Dictionary<string, int> AuthoritativeSemanticMismatchKinds {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> AuthoritativeSemanticMismatchSources {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> AuthoritativeSemanticMismatchScenarios {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    public CombatSemanticAuditMetrics SemanticAudit { get; set; } = new();

    public int ModelCompletedEpochs { get; set; }

    public int ModelConfiguredEpochs { get; set; }

    public int ModelBestEpoch { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public double ModelBestValidationLoss { get; set; }

    public double ModelTrainingLoss { get; set; }

    public double ModelValidationLoss { get; set; }

    public List<CombatPolicyValueEpochMetrics> ModelEpochHistory { get; set; } =
        new();

    public double ElapsedSeconds { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }

    public long AllocatedBytes { get; set; }

    public long EpisodeCompactStateVectors { get; set; }

    public long EpisodeCompactCandidateVectors { get; set; }

    public long EpisodeStateDictionaryMaterializations { get; set; }

    public long EpisodeCandidateDictionaryMaterializations { get; set; }

    public long WorldModelObservationsBuilt { get; set; }

    public long WorldModelObservationsSkipped { get; set; }

    public long WorkingSetBytes { get; set; }

    public long PrivateMemoryBytes { get; set; }

    public long GcHeapSizeBytes { get; set; }

    public long GcFragmentedBytes { get; set; }

    public long MemoryLoadBytes { get; set; }

    public long TotalAvailableMemoryBytes { get; set; }

    public double CpuSeconds { get; set; }

    public double CpuUtilizationPercent { get; set; }

    public double AllocationMegabytesPerSecond { get; set; }

    public Dictionary<string, double> PhaseElapsedSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> PhaseCpuSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, long> PhaseAllocatedBytes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> PhaseExternalCpuSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PhasePeakConcurrentWork { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PhaseObservedWorkerThreads { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal enum FoundationArenaSequentialDecision
{
    Continue,
    Accept,
    Reject
}

internal sealed class FoundationStrategyQuotaYieldProfile
{
    public int Campaigns { get; set; }

    public Dictionary<string, int> StrategyFrames { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class CombatFoundationEtaEstimate
{
    public const string CurrentVersion = "stage-aware-v1";

    public double ExpectedSeconds { get; set; }

    public double LowerSeconds { get; set; }

    public double UpperSeconds { get; set; }

    public Dictionary<string, double> StageSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatCampaignFoundationTrainer
{
    private readonly CombatCampaignRunner campaignRunner;
    private readonly ICombatTransformerTeacher? transformerTeacher;

    public CombatCampaignFoundationTrainer(
        CombatCampaignRunner? campaignRunner = null,
        ICombatTransformerTeacher? transformerTeacher = null)
    {
        this.campaignRunner = campaignRunner ?? new CombatCampaignRunner();
        this.transformerTeacher = transformerTeacher;
    }

    public CombatCampaignResult ReplayTrainingCampaign(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        CombatPolicyValueNetworkDefinition? model,
        string difficultyId,
        ulong worldSeed,
        double? explorationProbability = null,
        double? exactBranchProbability = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        ICombatPolicyValueModel policyValue = model == null
            ? NullCombatPolicyValueModel.Instance
            : new ManagedCombatPolicyValueModel(model);
        var factory = new RecordingCampaignPolicyFactory(
            CombatSearchBudgetPolicy.WithContext(request.Profile, "teacher"),
            policyValue,
            request.DecisionProfile,
            Math.Max(
                0d,
                Math.Min(
                    1d,
                    explorationProbability
                    ?? request.SelfPlayExplorationProbability)),
            request.SelfPlayExplorationTemperature,
            worldSeed,
            Math.Max(
                0d,
                Math.Min(
                    1d,
                    exactBranchProbability
                    ?? request.TeacherExactBranchProbability)),
            campaignRunner.SimulationEngine,
            request.ContentSetHash,
            request.OwnerModSetHash,
            recordWorldModelObservations: false);
        var plan = CombatCampaignWorldPlanner.Build(
            request.TrainingCampaign,
            string.IsNullOrWhiteSpace(difficultyId)
                ? "advanced"
                : difficultyId,
            worldSeed);
        return campaignRunner.Run(
            request.TrainingCampaign,
            plan,
            ruleset,
            factory,
            cancellationToken: cancellationToken);
    }

    public CombatCampaignFoundationTrainingResult Run(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        CombatPolicyValueNetworkDefinition? initialChampion = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        CombatCampaignWorldPlanner.Validate(request.TrainingCampaign);
        CombatCampaignWorldPlanner.Validate(request.ValidationCampaign);
        if (!request.TrainingCampaign.RequireAuthoritativeRules
            || !request.ValidationCampaign.RequireAuthoritativeRules)
        {
            throw new ArgumentException(
                "Formal foundation training requires authoritative training and validation campaigns.");
        }
        if (!string.Equals(
                request.TrainingCampaign.CampaignId,
                request.ValidationCampaign.CampaignId,
                StringComparison.Ordinal)
            || !string.Equals(
                request.TrainingCampaign.CampaignVersion,
                request.ValidationCampaign.CampaignVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Training and validation campaigns must share identity.");
        }

        var iterations = ResolveIterationLimit(request);
        var trainingCampaigns = Math.Max(
            2,
            Math.Min(1000, request.TrainingCampaignsPerIteration));
        var arenaPerDifficulty = Math.Max(
            1,
            Math.Min(100, request.ArenaCampaignsPerDifficulty));
        var arenaConfirmationPerDifficulty = Math.Max(
            0,
            Math.Min(
                200,
                request.ArenaConfirmationCampaignsPerDifficulty));
        var normalValidationCampaigns = Math.Max(
            5,
            Math.Min(1000, request.NormalValidationCampaigns));
        var advancedValidationCampaigns = Math.Max(
            5,
            Math.Min(1000, request.AdvancedValidationCampaigns));
        var capabilityProbeCampaigns = Math.Max(
            0,
            Math.Min(
                CombatFoundationTrainingProtocol
                    .MaximumAdaptiveCapabilityProbeCampaignsPerDifficulty,
                request.CapabilityProbeCampaignsPerDifficulty));
        var maximumCapabilityProbeCampaigns = capabilityProbeCampaigns <= 0
            ? 0
            : Math.Max(
                capabilityProbeCampaigns,
                CombatFoundationTrainingProtocol
                    .MaximumAdaptiveCapabilityProbeCampaignsPerDifficulty);
        var governance = CombatFoundationGovernanceProfiles.Resolve(
            request.GovernanceProfile,
            request.TuningInterval,
            request.TuningNormalCampaigns,
            request.TuningAdvancedCampaigns,
            request.TuningScreeningNormalCampaigns,
            request.TuningScreeningAdvancedCampaigns,
            request.TuningFinalistCount,
            request.CapabilityProbeTeacherCampaignsPerDifficulty,
            request.AutoTuneSampleCampaigns,
            request.ArenaEvaluationInterval,
            request.ArenaConfirmationFinalIterationOnly);
        request.GovernanceProfile = governance.Profile;
        request.AutoTuneObjective =
            CombatFoundationAutoTuneObjectiveNames.Normalize(
                request.AutoTuneObjective);
        request.TuningInterval = governance.TuningInterval;
        request.TuningNormalCampaigns = governance.TuningNormalCampaigns;
        request.TuningAdvancedCampaigns = governance.TuningAdvancedCampaigns;
        request.TuningScreeningNormalCampaigns =
            governance.TuningScreeningNormalCampaigns;
        request.TuningScreeningAdvancedCampaigns =
            governance.TuningScreeningAdvancedCampaigns;
        request.TuningFinalistCount = governance.TuningFinalistCount;
        request.AutoTuneSampleCampaigns = governance.AutoTuneSampleCampaigns;
        request.ArenaEvaluationInterval = governance.ArenaEvaluationInterval;
        request.ArenaConfirmationFinalIterationOnly =
            governance.ArenaConfirmationFinalIterationOnly;
        var tuningNormalCampaigns = request.EnableTuningArena
            ? governance.TuningNormalCampaigns
            : 0;
        var tuningAdvancedCampaigns = request.EnableTuningArena
            ? governance.TuningAdvancedCampaigns
            : 0;
        var tuningScreeningNormalCampaigns =
            request.EnableProgressiveTuning
                ? tuningNormalCampaigns == 0
                    ? 0
                    : Math.Max(
                        1,
                        Math.Min(
                            tuningNormalCampaigns,
                            governance.TuningScreeningNormalCampaigns))
                : tuningNormalCampaigns;
        var tuningScreeningAdvancedCampaigns =
            request.EnableProgressiveTuning
                ? tuningAdvancedCampaigns == 0
                    ? 0
                    : Math.Max(
                        1,
                        Math.Min(
                            tuningAdvancedCampaigns,
                            governance.TuningScreeningAdvancedCampaigns))
                : tuningAdvancedCampaigns;
        var normalAcceptanceRate =
            double.IsNaN(request.NormalAcceptanceRate)
            || double.IsInfinity(request.NormalAcceptanceRate)
                ? 0.80d
                : Math.Max(0d, Math.Min(1d, request.NormalAcceptanceRate));
        var advancedAcceptanceRate =
            double.IsNaN(request.AdvancedAcceptanceRate)
            || double.IsInfinity(request.AdvancedAcceptanceRate)
                ? 0.30d
                : Math.Max(
                    0d,
                    Math.Min(1d, request.AdvancedAcceptanceRate));
        var minimumArenaDiscordantPairs = Math.Max(
            1,
            Math.Min(128, request.MinimumArenaDiscordantPairs));
        var requestedOfflineHeadRegression =
            request.MaximumOfflineHeadRegression;
        var maximumOfflineHeadRegression =
            double.IsNaN(requestedOfflineHeadRegression)
            || double.IsInfinity(requestedOfflineHeadRegression)
                ? CombatFoundationPromotionProtocol
                    .DefaultMaximumOfflineHeadRegression
                : Math.Max(0d, Math.Min(0.50d,
                    requestedOfflineHeadRegression));
        var maximumStateFeatureCollisionRate = FiniteOrDefault(
            request.MaximumStateFeatureCollisionRate,
            CombatFoundationPromotionProtocol
                .DefaultMaximumStateFeatureCollisionRate);
        maximumStateFeatureCollisionRate = Math.Max(
            0d,
            Math.Min(1d, maximumStateFeatureCollisionRate));
        var maximumActionFeatureCollisionRate = FiniteOrDefault(
            request.MaximumActionFeatureCollisionRate,
            CombatFoundationPromotionProtocol
                .DefaultMaximumActionFeatureCollisionRate);
        maximumActionFeatureCollisionRate = Math.Max(
            0d,
            Math.Min(1d, maximumActionFeatureCollisionRate));
        var requiredNormalVictories = RequiredWilsonVictories(
            normalValidationCampaigns,
            normalAcceptanceRate);
        var requiredAdvancedVictories = RequiredWilsonVictories(
            advancedValidationCampaigns,
            advancedAcceptanceRate);
        var configuredInferenceMode = request.InferenceExecutionMode;
        var configuredInferenceParallelism = request.InferenceParallelism;
        var configuredInferenceLaneCount = request.InferenceLaneCount;
        var configuredInferenceBatchSize = request.InferenceBatchSize;
        var configuredThreadPoolMinimumWorkerThreads =
            request.ThreadPoolMinimumWorkerThreads;
        var configuredCheckpointSerializationParallelism =
            request.CheckpointSerializationParallelism;
        var executionPlan = CombatFoundationExecutionProfiles.Resolve(
            request.ParallelismProfile,
            request.MaximumDegreeOfParallelism,
            request.InferenceExecutionMode,
            request.InferenceParallelism,
            request.ThreadPoolMinimumWorkerThreads,
            request.CheckpointSerializationParallelism,
            null,
            request.InferenceLaneCount,
            request.InferenceBatchSize);
        var maximumCampaignParallelism = executionPlan.CampaignParallelism;
        var parallelism = maximumCampaignParallelism;
        var autoTuneCacheKey = BuildAutoTuneCacheKey(request, ruleset);
        var inferenceAutoTuneCacheKey = BuildInferenceAutoTuneCacheKey(
            request,
            request.Training,
            parallelism);
        var autoTune = new CombatFoundationAutoTuneResult
        {
            CacheKey = autoTuneCacheKey,
            CampaignCacheKey = autoTuneCacheKey,
            InferenceCacheKey = inferenceAutoTuneCacheKey,
            HardwareKey = request.AutoTuneHardwareKey ?? "",
            SelectedParallelism = parallelism,
            SelectedInferenceMode = executionPlan.InferenceMode,
            SelectedInferenceLaneCount = executionPlan.InferenceLaneCount,
            SelectedInferenceBatchSize = executionPlan.InferenceBatchSize,
            ThroughputTolerance = request.AutoTuneThroughputTolerance,
            Objective = request.AutoTuneObjective
        };
        if (string.Equals(
                executionPlan.Profile,
                CombatFoundationExecutionProfileNames.Auto,
                StringComparison.Ordinal)
            && request.ReuseAutoTuneCache)
        {
            var campaignCacheCompatible = AutoTuneCacheCompatible(
                request.AutoTuneCache,
                autoTuneCacheKey,
                executionPlan.CampaignParallelism);
            autoTune.CacheMissReason = campaignCacheCompatible
                ? ""
                : AutoTuneCacheMissReason(
                    request.AutoTuneCache,
                    autoTuneCacheKey,
                    executionPlan.CampaignParallelism,
                    request.AutoTuneHardwareKey);
            if (campaignCacheCompatible)
            {
                autoTune = CloneAutoTuneResult(request.AutoTuneCache!);
                autoTune.CacheKey = autoTuneCacheKey;
                autoTune.CampaignCacheKey = autoTuneCacheKey;
                autoTune.CacheHit = true;
                autoTune.CacheMissReason = "";
            }
            else
            {
                TryRestoreInferenceAutoTuneState(
                    request.AutoTuneCache,
                    autoTune,
                    inferenceAutoTuneCacheKey,
                    parallelism,
                    DateTime.UtcNow);
            }
            var inferenceCacheCompatible = InferenceAutoTuneCacheCompatible(
                autoTune,
                inferenceAutoTuneCacheKey,
                parallelism,
                DateTime.UtcNow);
            autoTune.InferenceCalibrated &= inferenceCacheCompatible;
            if (autoTune.InferenceCalibrated)
            {
                configuredInferenceMode = autoTune.SelectedInferenceMode;
                configuredInferenceLaneCount = string.Equals(
                    configuredInferenceMode,
                    CombatFoundationExecutionProfileNames.DirectInference,
                    StringComparison.Ordinal)
                    ? parallelism
                    : autoTune.SelectedInferenceLaneCount;
                configuredInferenceBatchSize =
                    autoTune.SelectedInferenceBatchSize;
            }
            else if (InferenceCalibrationCooldownActive(
                         autoTune,
                         inferenceAutoTuneCacheKey,
                         parallelism,
                         DateTime.UtcNow))
            {
                // A live health failure is stronger evidence than another
                // immediate benchmark. Use the deterministic direct fallback
                // until the persisted cooldown expires.
                configuredInferenceMode =
                    CombatFoundationExecutionProfileNames.DirectInference;
                configuredInferenceLaneCount = parallelism;
                configuredInferenceBatchSize = 1;
            }
            ApplyEffectiveExecutionPlan(
                executionPlan,
                request,
                maximumCampaignParallelism,
                configuredInferenceMode,
                configuredInferenceParallelism,
                configuredThreadPoolMinimumWorkerThreads,
                configuredCheckpointSerializationParallelism,
                configuredInferenceLaneCount,
                configuredInferenceBatchSize);
        }
        request.ParallelismProfile = executionPlan.Profile;
        request.MaximumDegreeOfParallelism = parallelism;
        request.InferenceExecutionMode = executionPlan.InferenceMode;
        request.InferenceParallelism = executionPlan.InferenceParallelism;
        request.InferenceLaneCount = executionPlan.InferenceLaneCount;
        request.InferenceBatchSize = executionPlan.InferenceBatchSize;
        request.ThreadPoolMinimumWorkerThreads =
            executionPlan.ThreadPoolMinimumWorkerThreads;
        EnsureThreadPoolCapacity(request.ThreadPoolMinimumWorkerThreads);
        request.CheckpointSerializationParallelism =
            executionPlan.CheckpointSerializationParallelism;
        var validationEarlyStopBatchSize = Math.Max(
            1,
            Math.Min(128, request.ValidationEarlyStopBatchSize));
        var preflightPerDifficulty = Math.Max(
            0,
            Math.Min(100, request.PreflightCampaignsPerDifficulty));
        var requestedSeedPlan = request.RunSeed == 0UL
            ? new CombatFoundationSeedPlan
            {
                RunSeed = 0UL,
                TrainingSeedStart = request.TrainingSeedStart,
                ArenaSeedStart = request.ArenaSeedStart,
                TuningSeedStart = request.TuningSeedStart,
                ValidationSeedStart = request.ValidationSeedStart,
                ModelRandomSeed = request.Training.RandomSeed
            }
            : CombatFoundationSeedPlan.Create(
                request.RunSeed,
                request.ValidationSeedStart);
        var resumeSeedSource = request.Resume?.SchemaVersion
                                   == CombatFoundationWorkerProtocol.SchemaVersion
                               && ResumeCompatible(request.Resume)
            ? request.Resume
            : null;
        var seedPlan = resumeSeedSource == null
            ? requestedSeedPlan
            : new CombatFoundationSeedPlan
            {
                RunSeed = resumeSeedSource.RunSeed,
                TrainingSeedStart = resumeSeedSource.TrainingSeedStart,
                ArenaSeedStart = resumeSeedSource.ArenaSeedStart,
                TuningSeedStart = resumeSeedSource.TuningSeedStart,
                ValidationSeedStart = resumeSeedSource.ValidationSeedStart,
                ModelRandomSeed = resumeSeedSource.ModelRandomSeed
            };
        var foundationTrainingOptions = request.Training.Normalized();
        var replayHotEpisodeLimit = Math.Max(
            foundationTrainingOptions.MinimumEpisodes,
            Math.Min(
                foundationTrainingOptions.ReplayEpisodeLimit,
                Math.Max(64, request.ReplayHotWindowEpisodeLimit)));
        var replayHotFrameLimit = Math.Max(
            foundationTrainingOptions.MinimumEpisodes,
            Math.Min(
                foundationTrainingOptions.ReplayFrameLimit,
                Math.Max(4096, request.ReplayHotWindowFrameLimit)));
        var replayHotBytesLimit = Math.Max(
            128L * 1024L * 1024L,
            Math.Min(
                foundationTrainingOptions.ReplayEstimatedBytesLimit,
                request.ReplayHotWindowEstimatedBytesLimit > 0L
                    ? request.ReplayHotWindowEstimatedBytesLimit
                    : 768L * 1024L * 1024L));
        var replayCurrentIterationShare = Clamp01(
            double.IsNaN(request.ReplayCurrentIterationShare)
            || double.IsInfinity(request.ReplayCurrentIterationShare)
                ? 0.60d
                : request.ReplayCurrentIterationShare);
        var replayHistoricalShare = Clamp01(
            double.IsNaN(request.ReplayHistoricalShare)
            || double.IsInfinity(request.ReplayHistoricalShare)
                ? 0.40d
                : request.ReplayHistoricalShare);
        var transformerTeacherOptions =
            (request.TransformerTeacher ?? new CombatTransformerTeacherOptions())
            .Normalized();
        request.TransformerTeacher = transformerTeacherOptions;
        var recordWorldModelObservations = !string.Equals(
            transformerTeacherOptions.Backend,
            CombatTransformerTeacherBackendNames.Disabled,
            StringComparison.OrdinalIgnoreCase);
        foundationTrainingOptions.RequireAuthoritativeEpisodes = true;
        foundationTrainingOptions.MaximumDegreeOfParallelism = Math.Max(
            1,
            Math.Min(64, request.ModelTrainingParallelism));
        foundationTrainingOptions.RandomSeed = seedPlan.ModelRandomSeed;
        var compatibility = BuildCompatibilityManifest(
            request,
            ruleset.RulesetHash,
            foundationTrainingOptions);
        var resume = request.Resume?.SchemaVersion
                         == CombatFoundationWorkerProtocol.SchemaVersion
                     && ResumeCompatible(request.Resume)
                     && ManifestCompatible(
                         request.Resume.Compatibility,
                         compatibility)
            ? request.Resume
            : null;
        if (resume == null && resumeSeedSource != null)
        {
            seedPlan = requestedSeedPlan;
            foundationTrainingOptions.RandomSeed = seedPlan.ModelRandomSeed;
        }
        if (seedPlan.RunSeed != 0UL)
        {
            // The persisted resume seed plan is authoritative. Keep the
            // signed Int32 contract used by Python/Torch while ensuring a
            // manually resumed job cannot switch the Transformer stream by
            // carrying a newly generated request RunSeed.
            transformerTeacherOptions.RandomSeed = unchecked(
                (int)seedPlan.RunSeed);
        }
        ValidateSeedPartitions(
            seedPlan.TrainingSeedStart,
            seedPlan.ArenaSeedStart,
            seedPlan.TuningSeedStart,
            seedPlan.ValidationSeedStart,
            iterations,
            trainingCampaigns,
            arenaPerDifficulty + arenaConfirmationPerDifficulty,
            tuningNormalCampaigns + tuningAdvancedCampaigns,
            normalValidationCampaigns,
            advancedValidationCampaigns);
        var compatibleInitialChampion =
            CombatPolicyValueNetworkValidator.TryValidate(
                initialChampion,
                out _)
                ? initialChampion
                : null;
        var result = new CombatCampaignFoundationTrainingResult
        {
            IterationProcessIsolationEnabled =
                request.EnableIterationProcessIsolation,
            Champion = resume?.Champion ?? compatibleInitialChampion,
            WorkingChampion = resume?.WorkingChampion
                              ?? resume?.Champion
                              ?? compatibleInitialChampion,
            LatestTrainingModel = resume?.LatestTrainingModel
                                  ?? resume?.WorkingChampion
                                  ?? resume?.Champion
                                  ?? compatibleInitialChampion,
            BestPendingArenaCandidate =
                resume?.BestPendingArenaCandidate,
            AbsoluteQualifiedBestModel =
                resume?.AbsoluteQualifiedBestModel,
            AbsoluteQualifiedBestEvidence =
                resume?.AbsoluteQualifiedBestEvidence,
            RunSeed = seedPlan.RunSeed,
            TrainingSeedStart = seedPlan.TrainingSeedStart,
            ArenaSeedStart = seedPlan.ArenaSeedStart,
            TuningSeedStart = seedPlan.TuningSeedStart,
            ValidationSeedStart = seedPlan.ValidationSeedStart,
            ModelRandomSeed = seedPlan.ModelRandomSeed,
            EffectiveParallelism = parallelism,
            ModelTrainingParallelism =
                foundationTrainingOptions.MaximumDegreeOfParallelism,
            GovernanceProfile = governance.Profile,
            ParallelismProfile = executionPlan.Profile,
            InferenceExecutionMode = executionPlan.InferenceMode,
            InferenceParallelism = executionPlan.InferenceParallelism,
            InferenceLaneCount = executionPlan.InferenceLaneCount,
            InferenceBatchSizePerLane = executionPlan.InferenceBatchSize,
            AutoTune = autoTune,
            Compatibility = compatibility
        };
        result.ResolvedIterationLimit = iterations;
        if (resume != null)
        {
            var resumedReplay = resume.Replay ?? new List<CombatEpisode>();
            result.GeneratedReplayEpisodes = Math.Max(
                resume.GeneratedReplayEpisodes,
                resumedReplay.Count);
            result.Replay.AddRange(resumedReplay);
            // Keep resumed observations alive until the Transformer export
            // boundary. A model-training checkpoint may be replayed before
            // the corpus transaction was committed; releasing here would
            // silently turn that recovery attempt into state-only data.
            // Result now owns the live hot set. Do not let the request-level
            // resume payload pin episodes that are dropped later this round.
            if (request.ReleaseResumeReplayAfterTransfer)
            {
                resumedReplay.Clear();
            }
            result.Iterations.AddRange(
                resume.Iterations
                ?? new List<CombatCampaignFoundationIteration>());
            result.HardSeedHistory.AddRange(
                resume.HardSeedHistory
                ?? new List<CombatFoundationHardSeedHistoryEntry>());
            result.Preflight = resume.Preflight
                               ?? new CombatCampaignFoundationIntegrityReport();
            result.ReplayArchivedEpisodes = result.Iterations.Sum(item =>
                item.ReplayArchivedEpisodes);
            result.ReplayArchiveDuplicates = result.Iterations.Sum(item =>
                item.ReplayArchiveDuplicates);
            result.ReplayArchivedBytes = result.Iterations.Sum(item =>
                item.ReplayArchivedBytes);
            result.LoadedHistoricalReplayEpisodes = result.Iterations.Sum(
                item => item.ReplayLoadedHistoricalEpisodes);
            result.LoadedHistoricalReplayBytes = result.Iterations.Sum(item =>
                item.ReplayLoadedHistoricalBytes);
        }
        foreach (var pinned in request.PinnedSeedHistory
                     ?? new List<CombatFoundationHardSeedHistoryEntry>())
        {
            if (pinned == null
                || pinned.WorldSeed == 0UL
                || result.HardSeedHistory.Any(item =>
                    item.WorldSeed == pinned.WorldSeed
                    && string.Equals(
                        item.DifficultyId,
                        pinned.DifficultyId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            result.HardSeedHistory.Add(pinned);
        }
        var expertReplay = (request.ExpertReplayEpisodes
                            ?? new List<CombatEpisode>())
            .Where(episode =>
                episode != null
                && episode.Authoritative
                && episode.Campaign?.IntegrityValid == true
                && episode.Campaign.FinalBossVictory
                && episode.ModelProtocol
                == CombatPolicyValueProtocol.EpisodeProtocol
                && episode.FeatureSchemaVersion
                == CombatPolicyValueProtocol.FeatureSchemaVersion
                && string.Equals(
                    episode.RulesetHash,
                    ruleset.RulesetHash,
                    StringComparison.Ordinal))
            .ToList();
        result.LoadedExpertReplayEpisodes = expertReplay.Count;
        var contentReplay = (request.AuthoritativeContentEpisodes
                             ?? new List<CombatEpisode>())
            .Where(episode => CombatContentTrainingEpisodeProtocol.TryValidate(
                episode,
                request.ContentSetHash,
                request.OwnerModSetHash,
                ruleset.RulesetHash,
                out _)
                && string.Equals(
                    episode.DecisionProfile,
                    request.DecisionProfile,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        result.LoadedAuthoritativeContentEpisodes = contentReplay.Count;
        var expertDiagnostics = request.ExpertReplaySelection
                                ?? new CombatFoundationExpertReplaySelection();
        result.ExpertReplaySelection =
            new CombatFoundationExpertReplaySelection
            {
                CompatibleCases = expertDiagnostics.CompatibleCases,
                SelectedCases = expertDiagnostics.SelectedCases,
                SelectedNormalEpisodes =
                    expertDiagnostics.SelectedNormalEpisodes,
                SelectedAdvancedEpisodes =
                    expertDiagnostics.SelectedAdvancedEpisodes,
                DistinctRuns = expertDiagnostics.DistinctRuns,
                TargetAdvancedShare =
                    expertDiagnostics.TargetAdvancedShare,
                QuotaShortfalls = new Dictionary<string, int>(
                    expertDiagnostics.QuotaShortfalls,
                    StringComparer.Ordinal)
            };
        if (result.ExpertReplaySelection.SelectedNormalEpisodes
                + result.ExpertReplaySelection.SelectedAdvancedEpisodes
            == 0
            && expertReplay.Count > 0)
        {
            result.ExpertReplaySelection.SelectedAdvancedEpisodes =
                expertReplay.Count(episode => string.Equals(
                    episode.Campaign?.DifficultyId,
                    "advanced",
                    StringComparison.OrdinalIgnoreCase));
            result.ExpertReplaySelection.SelectedNormalEpisodes =
                expertReplay.Count
                - result.ExpertReplaySelection.SelectedAdvancedEpisodes;
            result.ExpertReplaySelection.DistinctRuns = expertReplay
                .Select(episode => episode.JourneyRunId ?? "")
                .Distinct(StringComparer.Ordinal)
                .Count();
        }
        result.RewardResidualTraining =
            request.RewardResidualTraining
            ?? new CombatFoundationRewardResidualTrainingResult();
        result.CaseArchiveLoad =
            request.CaseArchiveLoad
            ?? new CombatFoundationCaseArchiveLoadDiagnostics();
        result.Replay.AddRange(contentReplay);
        result.Replay.AddRange(expertReplay);
        var loadedHistoricalThisInvocationEpisodes = 0;
        var loadedHistoricalThisInvocationBytes = 0L;
        if (request.EnableReplayWarehouse
            && request.HistoricalReplaySource != null)
        {
            try
            {
                var existingKeys = result.Replay
                    .Select(ReplayEpisodeKey)
                    .Where(key => key.Length > 0)
                    .ToHashSet(StringComparer.Ordinal);
                var historicalLimit = Math.Max(
                    1,
                    (int)Math.Round(
                        replayHotEpisodeLimit * replayHistoricalShare,
                        MidpointRounding.AwayFromZero));
                var historicalBytesLimit = Math.Max(
                    64L * 1024L * 1024L,
                    (long)Math.Round(
                        replayHotBytesLimit * replayHistoricalShare,
                        MidpointRounding.AwayFromZero));
                var historical = request.HistoricalReplaySource(
                    Math.Max(1, (resume?.NextIteration ?? 0) + 1),
                    existingKeys,
                    historicalLimit,
                    historicalBytesLimit)
                    ?? Array.Empty<CombatEpisode>();
                result.Replay.AddRange(historical.Where(episode =>
                    episode != null));
                loadedHistoricalThisInvocationEpisodes = historical.Count;
                loadedHistoricalThisInvocationBytes = historical.Sum(
                    CombatFoundationReplaySampler.EstimateResidentBytes);
                result.LoadedHistoricalReplayEpisodes +=
                    loadedHistoricalThisInvocationEpisodes;
                result.LoadedHistoricalReplayBytes +=
                    loadedHistoricalThisInvocationBytes;
            }
            catch (Exception ex)
            {
                result.ReplayWarehouseError = ex.Message;
            }
        }
        var calibratedParallelismCeiling = CalibratedParallelismCeiling(
            maximumCampaignParallelism,
            autoTune.SelectedParallelism);
        var parallelismDecision = request.EnableMemoryCapacityParallelism
            ? PrepareParallelismDecision(
                request,
                Math.Max(1, resume?.NextIteration + 1 ?? 1),
                calibratedParallelismCeiling)
            : CombatFoundationParallelismPlanner.Select(
                Math.Max(1, resume?.NextIteration + 1 ?? 1),
                calibratedParallelismCeiling,
                new CombatFoundationResourceSnapshot
                {
                    AvailablePhysicalMemoryBytes = long.MaxValue,
                    TotalPhysicalMemoryBytes = long.MaxValue
                });
        parallelism = parallelismDecision.SelectedParallelism;
        ApplyEffectiveExecutionPlan(
            executionPlan,
            request,
            parallelism,
            configuredInferenceMode,
            configuredInferenceParallelism,
            configuredThreadPoolMinimumWorkerThreads,
            configuredCheckpointSerializationParallelism,
            configuredInferenceLaneCount,
            configuredInferenceBatchSize);
        EnsureThreadPoolCapacity(request.ThreadPoolMinimumWorkerThreads);
        result.EffectiveParallelism = parallelism;
        result.ParallelismDecision = parallelismDecision;
        var workingChampion = resume?.WorkingChampion ?? result.Champion;
        var latestTrainingModel = resume?.LatestTrainingModel
                                  ?? workingChampion;
        foreach (var priorIteration in result.Iterations)
        {
            var legacyScreeningQualification =
                priorIteration.AbsoluteQualificationGatePassed;
            priorIteration.FormalConfirmationCompleted =
                ConfirmedQualificationEvidence(
                    priorIteration,
                    arenaConfirmationPerDifficulty);
            priorIteration.AbsoluteQualificationGatePassed =
                priorIteration.FormalConfirmationCompleted
                && AbsoluteQualificationGatePassed(
                    priorIteration.ValidArenaPairs,
                    priorIteration.ArenaScreeningPairs
                    + priorIteration.ArenaConfirmationPairs,
                    priorIteration.AbsoluteNormalGatePassed,
                    priorIteration.AbsoluteAdvancedGatePassed,
                    priorIteration.OfflineHeadRegressionGatePassed,
                    priorIteration.StrategyQuotaGatePassed,
                    priorIteration.FeatureCollisionGatePassed);
            priorIteration.ScreeningQualificationGatePassed |=
                legacyScreeningQualification
                || priorIteration.AbsoluteQualificationGatePassed;
            priorIteration.CandidateQualificationState =
                priorIteration.AbsoluteQualificationGatePassed
                    ? CombatFoundationPromotionProtocol.ConfirmedQualified
                    : priorIteration.ScreeningQualificationGatePassed
                        ? CombatFoundationPromotionProtocol.ScreeningPassed
                        : priorIteration.OfflineHeadRegressionGatePassed
                          && priorIteration.StrategyQuotaGatePassed
                          && priorIteration.FeatureCollisionGatePassed
                            ? CombatFoundationPromotionProtocol.OfflineSafe
                            : CombatFoundationPromotionProtocol
                                .OfflineRejected;
        }
        if (result.AbsoluteQualifiedBestModel != null)
        {
            var canonicalQualifiedEvidence = result.Iterations
                .FirstOrDefault(item =>
                    item.Iteration
                    == result.AbsoluteQualifiedBestEvidence?.Iteration
                    && string.Equals(
                        item.CandidateModelId,
                        result.AbsoluteQualifiedBestModel.ModelId,
                        StringComparison.Ordinal));
            if (canonicalQualifiedEvidence == null
                || !canonicalQualifiedEvidence
                    .AbsoluteQualificationGatePassed)
            {
                result.AbsoluteQualifiedBestModel = null;
                result.AbsoluteQualifiedBestEvidence = null;
            }
            else
            {
                result.AbsoluteQualifiedBestEvidence =
                    canonicalQualifiedEvidence;
            }
        }
        var workingChampionEvidence = result.Iterations
            .LastOrDefault(item => workingChampion != null
                                   && string.Equals(
                                       item.CandidateModelId,
                                       workingChampion.ModelId,
                                       StringComparison.Ordinal));
        var workingModelBank = new FoundationWorkingModelBank(
            workingChampion,
            workingChampionEvidence,
            result.AbsoluteQualifiedBestModel,
            result.AbsoluteQualifiedBestEvidence);
        ICombatPolicyValueModel championModel = result.Champion == null
            ? NullCombatPolicyValueModel.Instance
            : CreateParallelPolicyValueModel(
                result.Champion,
                request,
                parallelism);
        var deploymentProfile = CombatSearchBudgetPolicy.WithContext(
            request.Profile,
            "deployment");
        var teacherProfile = CombatSearchBudgetPolicy.WithContext(
            request.Profile,
            "teacher");
        var hardTeacherProfile = CombatSearchBudgetPolicy.WithContext(
            request.Profile,
            "teacher-hard");
        var startIteration = Math.Max(
            0,
            Math.Min(iterations, resume?.NextIteration ?? 0));
        // A user-requested continuation is a new optimization attempt. Keep
        // the champion and replay, but do not let the terminal rejection
        // streak from the previous attempt suppress every appended iteration.
        var stagnationAttemptStartIndex = resume != null
                                          && request.AdditionalIterationsOnResume > 0
            ? result.Iterations.Count
            : 0;
        var trainingSeed = seedPlan.TrainingSeedStart
                           + (ulong)(startIteration * trainingCampaigns);
        var arenaSeed = seedPlan.ArenaSeedStart
                        + (ulong)(startIteration
                                  * (arenaPerDifficulty
                                     + arenaConfirmationPerDifficulty)
                                  * 2);
        var completedCampaigns = Math.Max(
            0,
            resume?.CompletedCampaigns ?? 0);
        var tuningCampaignsPerEvaluation = EstimateTuningCampaigns(
                foundationTrainingOptions.RetainedModelCandidates,
                tuningNormalCampaigns,
                tuningAdvancedCampaigns,
                request.EnableProgressiveTuning,
                tuningScreeningNormalCampaigns,
                tuningScreeningAdvancedCampaigns,
                governance.TuningFinalistCount);
        var finalCampaigns =
            normalValidationCampaigns
            + advancedValidationCampaigns
            + maximumCapabilityProbeCampaigns * 2 * 2
            + governance.CapabilityProbeTeacherCampaignsPerDifficulty * 2
            + (request.EnableCapabilityDecisionDifferenceDiagnostics
                ? Math.Max(
                    0,
                    request.MaximumCapabilityDecisionDifferenceCases) * 2
                : 0);
        var remainingIterationCount = Math.Max(0, iterations - startIteration);
        var remainingTuningIterations = Enumerable
            .Range(startIteration, remainingIterationCount)
            .Count(iteration => governance.RunsTuningAtIteration(
                iteration,
                iterations));
        var remainingCampaigns = Enumerable
                                 .Range(startIteration, remainingIterationCount)
                                 .Sum(iteration =>
                                     trainingCampaigns
                                     + (governance.RunsArenaAtIteration(
                                             iteration,
                                             iterations)
                                         ? arenaPerDifficulty * 4
                                           + (governance
                                                  .RunsFormalConfirmationAtIteration(
                                                      iteration,
                                                      iterations)
                                              ? arenaConfirmationPerDifficulty
                                                * 4
                                              : 0)
                                         : 0))
                                 + remainingTuningIterations
                                 * tuningCampaignsPerEvaluation;
        var totalCampaigns = resume == null
            ? remainingCampaigns + finalCampaigns
            : completedCampaigns + remainingCampaigns + finalCampaigns;
        result.RequestedCampaigns = totalCampaigns;
        var telemetry = new FoundationTelemetryTracker(
            request,
            parallelism,
            totalCampaigns,
            iterations,
            resume?.Telemetry,
            completedCampaigns,
            startIteration + 1,
            remainingIterationCount);
        telemetry.SetParallelismDecision(parallelismDecision);
        telemetry.BeginPhase("setup");
        telemetry.ReportStage(resume == null ? "starting" : "resumed");

        if (preflightPerDifficulty > 0 && startIteration == 0)
        {
            result.Preflight = RunIntegrityPreflight(
                request,
                ruleset,
                championModel,
                telemetry,
                preflightPerDifficulty,
                request.RunSeed == 0UL
                    ? request.PreflightSeedStart
                    : seedPlan.TrainingSeedStart,
                parallelism,
                autoTuneCacheKey,
                string.Equals(
                    executionPlan.Profile,
                    CombatFoundationExecutionProfileNames.Auto,
                    StringComparison.Ordinal)
                && !autoTune.CacheHit,
                out var measuredAutoTune,
                cancellationToken);
            if (measuredAutoTune != null)
            {
                var priorAutoTune = autoTune;
                measuredAutoTune.CacheMissReason =
                    priorAutoTune.CacheMissReason;
                var measuredInferenceKey = BuildInferenceAutoTuneCacheKey(
                    request,
                    request.Training,
                    measuredAutoTune.SelectedParallelism);
                TryRestoreInferenceAutoTuneState(
                    priorAutoTune,
                    measuredAutoTune,
                    measuredInferenceKey,
                    measuredAutoTune.SelectedParallelism,
                    DateTime.UtcNow);
                autoTune = measuredAutoTune;
                result.AutoTune = autoTune;
                request.AutoTuneCache = autoTune;
                parallelism = Math.Max(
                    1,
                    Math.Min(
                        executionPlan.CampaignParallelism,
                        autoTune.SelectedParallelism));
                parallelismDecision.SelectedParallelism = parallelism;
                parallelismDecision.PredictedPeakPrivateBytes =
                    PredictedPeakPrivateBytes(
                        parallelismDecision.FixedProcessBytes,
                        parallelismDecision.PredictedPerLaneBytes,
                        parallelism);
                parallelismDecision.Reason +=
                    "; throughput-auto-tune selected=" + parallelism;
                result.ParallelismDecision = parallelismDecision;
                telemetry.SetParallelismDecision(parallelismDecision);
                if (InferenceAutoTuneCacheCompatible(
                        autoTune,
                        measuredInferenceKey,
                        parallelism,
                        DateTime.UtcNow))
                {
                    configuredInferenceMode = autoTune.SelectedInferenceMode;
                    configuredInferenceLaneCount = string.Equals(
                        configuredInferenceMode,
                        CombatFoundationExecutionProfileNames.DirectInference,
                        StringComparison.Ordinal)
                        ? parallelism
                        : autoTune.SelectedInferenceLaneCount;
                    configuredInferenceBatchSize =
                        autoTune.SelectedInferenceBatchSize;
                }
                else
                {
                    configuredInferenceMode =
                        CombatFoundationExecutionProfileNames.DirectInference;
                    configuredInferenceLaneCount = parallelism;
                    configuredInferenceBatchSize = 1;
                }
                ApplyEffectiveExecutionPlan(
                    executionPlan,
                    request,
                    parallelism,
                    configuredInferenceMode,
                    configuredInferenceParallelism,
                    configuredThreadPoolMinimumWorkerThreads,
                    configuredCheckpointSerializationParallelism,
                    configuredInferenceLaneCount,
                    configuredInferenceBatchSize);
                EnsureThreadPoolCapacity(
                    request.ThreadPoolMinimumWorkerThreads);
                result.EffectiveParallelism = parallelism;
                result.InferenceExecutionMode = executionPlan.InferenceMode;
                result.InferenceParallelism = executionPlan.InferenceParallelism;
                result.InferenceLaneCount = executionPlan.InferenceLaneCount;
                result.InferenceBatchSizePerLane =
                    executionPlan.InferenceBatchSize;
                telemetry.SetEffectiveParallelism(parallelism);
                championModel = result.Champion == null
                    ? NullCombatPolicyValueModel.Instance
                    : CreateParallelPolicyValueModel(
                        result.Champion,
                        request,
                        parallelism);
                request.AutoTuneCompleted?.Invoke(autoTune);
            }
            result.TerminalConsistencyViolations +=
                result.Preflight.TerminalConsistencyViolations;
            if (!result.Preflight.Passed)
            {
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
                result.SemanticGatePassed =
                    result.Preflight.SemanticGatePassed;
                result.SemanticGateFailureReason =
                    result.Preflight.SemanticGatePassed
                        ? ""
                        : "selected realized invalid="
                          + result.Preflight.SelectedInvalidActions
                          + ", selected realized mismatch="
                          + result.Preflight.SelectedUnexplainedMismatchActions
                          + ", selected decision-input invalid="
                          + result.Preflight
                              .SelectedSourceProjectionInvalidActions
                          + ", selected decision-input mismatch="
                          + result.Preflight
                              .SelectedSourceProjectionUnexplainedMismatchActions
                          + ", decision-input invalid rate="
                          + result.Preflight.SourceProjectionInvalidRate
                              .ToString("P2", CultureInfo.InvariantCulture)
                          + ", decision-input mismatch rate="
                          + result.Preflight.SourceProjectionMismatchRate
                              .ToString("P2", CultureInfo.InvariantCulture);
                result.Message =
                    "底模训练前权威快检失败："
                    + result.Preflight.InvalidCampaigns
                    + "/"
                    + result.Preflight.CompletedCampaigns
                    + " 个战役无效；未写入训练轨迹，也未开始模型训练。";
                telemetry.ApplyTo(result);
                FinalizeCaseAnalysis(result);
                return result;
            }
            if (request.PreflightOnly)
            {
                result.Success = true;
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
                result.Message =
                    "底模训练前权威快检通过："
                    + result.Preflight.CompletedCampaigns
                    + " 个战役全部有效；未执行自博弈与模型训练。";
                telemetry.ApplyTo(result);
                FinalizeCaseAnalysis(result);
                return result;
            }
        }

        result.ConsecutiveRejectedIterations =
            ConsecutiveRejectedIterations(
                result.Iterations,
                stagnationAttemptStartIndex);
        result.ConsecutiveUnproductiveIterations =
            ConsecutiveUnproductiveIterations(
                result.Iterations,
                stagnationAttemptStartIndex);
        result.ConsecutiveDataOnlyIterations =
            ConsecutiveDataOnlyIterations(
                result.Iterations,
                stagnationAttemptStartIndex);
        if (ShouldStopForStagnation(
                request,
                result.Iterations,
                workingChampion != null,
                stagnationAttemptStartIndex))
        {
            result.StoppedForStagnation = true;
            result.IterationStopReason =
                CombatFoundationStagnationProtocol.Version
                + ": resumed after consecutive unproductive candidates="
                + result.ConsecutiveUnproductiveIterations
                + ", rejected candidates="
                + result.ConsecutiveRejectedIterations;
        }

        var strategyQuotaYieldProfiles =
            new Dictionary<string, FoundationStrategyQuotaYieldProfile>(
                StringComparer.Ordinal);
        var iterationInvocationLimit = request.MaximumIterationsPerProcess > 0
            ? Math.Min(
                iterations,
                startIteration + Math.Max(1, request.MaximumIterationsPerProcess))
            : iterations;
        for (var iteration = startIteration;
             iteration < iterationInvocationLimit;
             iteration++)
        {
            if (result.StoppedForStagnation)
            {
                break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var iterationNumber = iteration + 1;
            var arenaEvaluationRan = governance.RunsArenaAtIteration(
                iteration,
                iterations);
            var formalArenaConfirmationScheduled =
                governance.RunsFormalConfirmationAtIteration(
                    iteration,
                    iterations);
            var scheduledArenaConfirmationPerDifficulty =
                formalArenaConfirmationScheduled
                    ? arenaConfirmationPerDifficulty
                    : 0;
            // Each iteration owns a stable seed partition even when its Arena
            // work is intentionally skipped. Resume and uninterrupted runs
            // therefore remain bit-for-bit comparable.
            arenaSeed = seedPlan.ArenaSeedStart
                        + (ulong)(iteration
                                  * (arenaPerDifficulty
                                     + arenaConfirmationPerDifficulty)
                                  * 2);
            var replayArchiveReport = new CombatFoundationReplayArchiveReport
            {
                Iteration = iterationNumber,
                LoadedHistoricalEpisodes =
                    loadedHistoricalThisInvocationEpisodes,
                LoadedHistoricalBytes = loadedHistoricalThisInvocationBytes
            };
            var adaptiveParallelismCeiling = CalibratedParallelismCeiling(
                maximumCampaignParallelism,
                autoTune.SelectedParallelism);
            parallelismDecision = request.EnableMemoryCapacityParallelism
                ? PrepareParallelismDecision(
                    request,
                    iterationNumber,
                    adaptiveParallelismCeiling)
                : CombatFoundationParallelismPlanner.Select(
                    iterationNumber,
                    adaptiveParallelismCeiling,
                    new CombatFoundationResourceSnapshot
                    {
                        AvailablePhysicalMemoryBytes = long.MaxValue,
                        TotalPhysicalMemoryBytes = long.MaxValue
                    });
            parallelism = parallelismDecision.SelectedParallelism;
            ApplyEffectiveExecutionPlan(
                executionPlan,
                request,
                parallelism,
                configuredInferenceMode,
                configuredInferenceParallelism,
                configuredThreadPoolMinimumWorkerThreads,
                configuredCheckpointSerializationParallelism,
                configuredInferenceLaneCount,
                configuredInferenceBatchSize);
            EnsureThreadPoolCapacity(request.ThreadPoolMinimumWorkerThreads);
            result.EffectiveParallelism = parallelism;
            result.ParallelismDecision = parallelismDecision;
            telemetry.SetParallelismDecision(parallelismDecision);
            championModel = result.Champion == null
                ? NullCombatPolicyValueModel.Instance
                : CreateParallelPolicyValueModel(
                    result.Champion,
                    request,
                    parallelism);
            telemetry.BeginIteration(iterationNumber);
            var inferenceIterationStart = telemetry.Current(
                "iteration:inference-baseline");
            var curriculumEvidence = result.Iterations
                .Skip(Math.Max(
                    0,
                    result.Iterations.Count
                    - CombatFoundationCurriculum.RecentEvidenceWindow))
                .ToList();
            var priorNormalTrials = curriculumEvidence.Sum(item =>
                item.ValidNormalArenaPairs);
            var priorAdvancedTrials = curriculumEvidence.Sum(item =>
                item.ValidAdvancedArenaPairs);
            var priorNormalWins = curriculumEvidence.Sum(item =>
                (int)Math.Round(
                    item.CandidateNormalWinRate
                    * item.ValidNormalArenaPairs));
            var priorAdvancedWins = curriculumEvidence.Sum(item =>
                (int)Math.Round(
                    item.CandidateAdvancedWinRate
                    * item.ValidAdvancedArenaPairs));
            var priorNormalWinRate = priorNormalTrials <= 0
                ? double.NaN
                : priorNormalWins / (double)priorNormalTrials;
            var priorAdvancedWinRate = priorAdvancedTrials <= 0
                ? double.NaN
                : priorAdvancedWins / (double)priorAdvancedTrials;
            var curriculumPlan = CombatFoundationCurriculum.Evaluate(
                request.EnableCurriculum,
                iteration,
                priorNormalWins,
                priorNormalTrials,
                priorAdvancedWins,
                priorAdvancedTrials);
            var effectiveMinimumAdvancedReplayShare =
                EffectiveAdvancedTrainingFloor(
                    request.MinimumAdvancedReplayShare,
                    result.ExpertReplaySelection);
            ApplyAdvancedTrainingFloor(
                curriculumPlan,
                effectiveMinimumAdvancedReplayShare);
            workingChampion = workingModelBank.Select(
                PreferredWorkingModelSlot(curriculumPlan.Stage),
                workingChampion);
            result.WorkingChampion = workingChampion;
            var effectiveExplorationProbability =
                CombatFoundationCurriculum.ExplorationProbability(
                    curriculumPlan,
                    request.SelfPlayExplorationProbability);
            var effectiveHardSeedReplayShare =
                EffectiveHardSeedReplayShare(request, result.Iterations);
            var hardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
                result.HardSeedHistory,
                trainingCampaigns,
                effectiveHardSeedReplayShare,
                iteration,
                seedPlan.RunSeed,
                request.EnableHardSeedCurriculum,
                request.HardEncounterWeights);
            var hardSeedTrainingVictories = 0;
            var hardSeedCounterfactualCampaigns = 0;
            var hardSeedCounterfactualVictories = 0;
            var hardSeedCounterfactualImprovements = 0;
            var hardSeedCounterfactualRejected = 0;
            var advancedLocalCurriculumAttempts = 0;
            var advancedLocalCurriculumSuccesses = 0;
            IReadOnlyList<CombatFoundationTrainingSlot> trainingSchedule =
                resume != null
                && resume.NextIteration == iteration
                && resume.TrainingSchedule.Count == trainingCampaigns
                    ? resume.TrainingSchedule
                    : Array.Empty<CombatFoundationTrainingSlot>();
            var resumeModelTraining = resume != null
                                      && resume.NextIteration == iteration
                                      && string.Equals(
                                          resume.Stage,
                                          "model-training",
                                          StringComparison.Ordinal);
            if (resumeModelTraining)
            {
                trainingSeed += (ulong)trainingCampaigns;
            }
            if (!resumeModelTraining)
            {
                telemetry.BeginPhase("self-play");
                var invalidTrainingCampaignsBefore =
                    result.InvalidTrainingCampaigns;
                var semanticRejectedCampaignsBefore =
                    result.SemanticRejectedCampaigns;
                var trainingSeedBase = trainingSeed;
                trainingSeed += (ulong)trainingCampaigns;
                trainingSchedule = CombatFoundationTrainingSchedule.Build(
                    trainingCampaigns,
                    trainingSeedBase,
                    seedPlan.RunSeed,
                    iteration,
                    curriculumPlan,
                    hardSeedPlan);
                var trainingRuns =
                    new FoundationTrainingCampaignRun?[trainingCampaigns];
                CombatFoundationWorkScheduler.For(
                    trainingCampaigns,
                    parallelism,
                    cancellationToken,
                    campaignIndex =>
                    {
                        var slot = trainingSchedule[campaignIndex];
                        var difficulty = slot.DifficultyId;
                        var campaignSeed = slot.WorldSeed;
                        var factory = new RecordingCampaignPolicyFactory(
                            slot.HardSeed
                                ? hardTeacherProfile
                                : teacherProfile,
                            championModel,
                            request.DecisionProfile,
                            effectiveExplorationProbability,
                            request.SelfPlayExplorationTemperature,
                            campaignSeed,
                            slot.HardSeed
                                ? request.HardTeacherExactBranchProbability
                                : request.TeacherExactBranchProbability,
                            campaignRunner.SimulationEngine,
                            request.ContentSetHash,
                            request.OwnerModSetHash,
                            recordWorldModelObservations);
                        var localEncounter =
                            slot.HardSeed
                            && slot.FailureEncounterCheckpoint != null;
                        CombatCampaignCheckpoint? failureEncounterCheckpoint =
                            null;
                        var campaign = localEncounter
                            ? RunCampaignSegment(
                                request.TrainingCampaign,
                                difficulty,
                                campaignSeed,
                                ruleset,
                                factory,
                                slot.FailureEncounterCheckpoint!,
                                telemetry,
                                "training-hard-encounter:"
                                + iterationNumber,
                                cancellationToken)
                            : RunCampaign(
                                request.TrainingCampaign,
                                difficulty,
                                campaignSeed,
                                ruleset,
                                factory,
                                telemetry,
                                "training:" + iterationNumber,
                                cancellationToken,
                                encounterStart =>
                                    failureEncounterCheckpoint =
                                        CompactEncounterCheckpoint(
                                            encounterStart));
                        var encounterStartIndex = localEncounter
                            ? slot.FailureEncounterCheckpoint!
                                .NextEncounterIndex
                            : 0;
                        var episodes = factory.Complete(
                            campaign,
                            encounterStartIndex,
                            localEncounter
                                ? ":hard-encounter:"
                                  + iterationNumber
                                  + ":"
                                  + campaignIndex
                                : "");
                        CombatCampaignResult? counterfactualCampaign = null;
                        var counterfactualEpisodes = new List<CombatEpisode>();
                        if (ShouldRunCounterfactualHardEncounter(
                                request,
                                localEncounter,
                                campaign))
                        {
                            var curriculumCheckpoints =
                                BuildLocalCurriculumCheckpoints(
                                    slot.FailureEncounterCheckpoint!);
                            var bestScore = double.NegativeInfinity;
                            foreach (var curriculumCheckpoint
                                     in curriculumCheckpoints)
                            {
                                var counterfactualFactory =
                                    new RecordingCampaignPolicyFactory(
                                        hardTeacherProfile,
                                        NullCombatPolicyValueModel.Instance,
                                        request.DecisionProfile,
                                        0d,
                                        1d,
                                        campaignSeed,
                                        request.HardTeacherExactBranchProbability,
                                        campaignRunner.SimulationEngine,
                                        request.ContentSetHash,
                                        request.OwnerModSetHash,
                                        recordWorldModelObservations);
                                var candidateCounterfactual =
                                    RunCampaignSegment(
                                        request.TrainingCampaign,
                                        difficulty,
                                        campaignSeed,
                                        ruleset,
                                        counterfactualFactory,
                                        curriculumCheckpoint.Checkpoint,
                                        telemetry,
                                        curriculumCheckpoint.Repaired
                                            ? "training-advanced-local-curriculum:"
                                              + curriculumCheckpoint.CurriculumBand
                                              + ":"
                                              + iterationNumber
                                            : "training-hard-counterfactual:"
                                              + iterationNumber,
                                        cancellationToken);
                                var candidateEpisodes =
                                    counterfactualFactory.Complete(
                                        candidateCounterfactual,
                                        encounterStartIndex,
                                        curriculumCheckpoint.Repaired
                                            ? ":advanced-local-curriculum:"
                                              + curriculumCheckpoint.CurriculumBand
                                              + ":"
                                              + iterationNumber
                                              + ":"
                                              + campaignIndex
                                              + ":hp"
                                              + curriculumCheckpoint.HpFloorPercent
                                            : ":hard-counterfactual:"
                                              + iterationNumber
                                              + ":"
                                              + campaignIndex);
                                var score = CounterfactualCurriculumScore(
                                    candidateCounterfactual);
                                trainingRuns[campaignIndex] ??= new();
                                trainingRuns[campaignIndex]!
                                    .CounterfactualAttemptsExecuted++;
                                if (curriculumCheckpoint.Repaired)
                                {
                                    trainingRuns[campaignIndex]!
                                        .AdvancedLocalCurriculumAttempts++;
                                }
                                if (counterfactualCampaign == null
                                    || score > bestScore)
                                {
                                    bestScore = score;
                                    counterfactualCampaign =
                                        candidateCounterfactual;
                                    counterfactualEpisodes = candidateEpisodes;
                                    trainingRuns[campaignIndex]!
                                        .AdvancedLocalCurriculumRepaired =
                                        curriculumCheckpoint.Repaired;
                                    trainingRuns[campaignIndex]!
                                        .AdvancedLocalCurriculumHpFloorPercent =
                                        curriculumCheckpoint.HpFloorPercent;
                                }
                                if (candidateCounterfactual.Battles
                                        .LastOrDefault()?.Outcome
                                    == CombatSimulationOutcome.Victory)
                                {
                                    break;
                                }
                            }
                        }
                        var trainingRun = trainingRuns[campaignIndex]
                                          ?? new FoundationTrainingCampaignRun();
                        trainingRun.Campaign = campaign;
                        trainingRun.Episodes = episodes;
                        trainingRun.CounterfactualCampaign =
                            counterfactualCampaign;
                        trainingRun.CounterfactualEpisodes =
                            counterfactualEpisodes;
                        trainingRun.HardSeed = slot.HardSeed;
                        trainingRun.Schedule = slot;
                        trainingRun.LocalEncounter = localEncounter;
                        trainingRun.FailureEncounterCheckpoint =
                            failureEncounterCheckpoint;
                        trainingRuns[campaignIndex] = trainingRun;
                        ReportProgress(
                            request,
                            telemetry,
                            campaign,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 "
                            + iterationNumber
                            + " 轮：七层训练推演");
                    },
                    telemetry.SchedulerProgress);
                CombatFoundationWorkScheduler.For(
                    trainingRuns.Length,
                    parallelism,
                    cancellationToken,
                    campaignIndex =>
                    {
                        var trainingRun = trainingRuns[campaignIndex]!;
                        trainingRun.SemanticAudit = AggregateSemanticAudit(
                            new[]
                            {
                                trainingRun.Campaign,
                                trainingRun.CounterfactualCampaign
                            }.Where(item => item != null)
                                .Select(item => item!));
                        if (trainingRun.Campaign.Invalid
                            || !SemanticGateSatisfied(
                                trainingRun.SemanticAudit))
                        {
                            return;
                        }
                        if (trainingRun.LocalEncounter)
                        {
                            ApplyHardEncounterTargets(
                                trainingRun.Episodes,
                                trainingRun.Campaign,
                                curriculumPlan.Stage,
                                iterationNumber);
                        }
                        else
                        {
                            ApplyCampaignTargets(
                                trainingRun.Episodes,
                                trainingRun.Campaign,
                                curriculumPlan.Stage,
                                iterationNumber);
                        }
                        trainingRun.FeatureLeakageViolations +=
                            SanitizeEpisodeFeatures(trainingRun.Episodes);
                        if (trainingRun.CounterfactualCampaign == null)
                        {
                            return;
                        }
                        trainingRun.CounterfactualAdmission =
                            ClassifyCounterfactual(
                                trainingRun.Campaign,
                                trainingRun.CounterfactualCampaign);
                        if (trainingRun.CounterfactualAdmission
                            == CombatFoundationCounterfactualAdmission.Rejected)
                        {
                            return;
                        }
                        ApplyHardEncounterTargets(
                            trainingRun.CounterfactualEpisodes,
                            trainingRun.CounterfactualCampaign,
                            curriculumPlan.Stage + ":counterfactual",
                            iterationNumber);
                        if (trainingRun.CounterfactualAdmission
                            == CombatFoundationCounterfactualAdmission.Improved)
                        {
                            ApplyImprovedCounterfactualTargets(
                                trainingRun.CounterfactualEpisodes);
                        }
                        trainingRun.FeatureLeakageViolations +=
                            SanitizeEpisodeFeatures(
                                trainingRun.CounterfactualEpisodes);
                    },
                    telemetry.SchedulerProgress);
                for (var campaignIndex = 0;
                     campaignIndex < trainingRuns.Length;
                     campaignIndex++)
                {
                    var trainingRun = trainingRuns[campaignIndex]!;
                    var semanticAudit = trainingRun.SemanticAudit;
                    if (trainingRun.Campaign.Invalid)
                    {
                        result.InvalidTrainingCampaigns++;
                        result.DiscardedInvalidEpisodes +=
                            trainingRun.Episodes.Count;
                        AddIntegrityFailure(
                            result.TrainingFailures,
                            result.TrainingFailureCounts,
                            trainingRun.Campaign);
                        trainingRun.Episodes.Clear();
                    }
                    else if (!SemanticGateSatisfied(semanticAudit))
                    {
                        result.SemanticGatePassed = false;
                        result.SemanticRejectedCampaigns++;
                        result.GeneratedReplayEpisodes +=
                            trainingRun.Episodes.Count
                            + trainingRun.CounterfactualEpisodes.Count;
                        result.DiscardedSemanticEpisodes +=
                            trainingRun.Episodes.Count
                            + trainingRun.CounterfactualEpisodes.Count;
                        result.SemanticGateFailureReason =
                            DescribeSemanticGateFailure(semanticAudit);
                        trainingRun.Episodes.Clear();
                        trainingRun.CounterfactualEpisodes.Clear();
                    }
                    else
                    {
                        result.FeatureLeakageViolations +=
                            trainingRun.FeatureLeakageViolations;
                        result.GeneratedReplayEpisodes +=
                            trainingRun.Episodes.Count;
                        if (trainingRun.CounterfactualCampaign != null)
                        {
                            advancedLocalCurriculumAttempts +=
                                trainingRun.AdvancedLocalCurriculumAttempts;
                            hardSeedCounterfactualCampaigns++;
                            var admission =
                                trainingRun.CounterfactualAdmission;
                            if (admission
                                != CombatFoundationCounterfactualAdmission
                                    .Rejected)
                            {
                                if (admission
                                    == CombatFoundationCounterfactualAdmission
                                        .Improved)
                                {
                                    hardSeedCounterfactualImprovements++;
                                }
                                result.GeneratedReplayEpisodes +=
                                    trainingRun.CounterfactualEpisodes.Count;
                                result.Replay.AddRange(
                                    trainingRun.CounterfactualEpisodes);
                                if (trainingRun.CounterfactualCampaign
                                        .Battles.LastOrDefault()?.Outcome
                                    == CombatSimulationOutcome.Victory)
                                {
                                    hardSeedCounterfactualVictories++;
                                    if (trainingRun
                                        .AdvancedLocalCurriculumRepaired)
                                    {
                                        advancedLocalCurriculumSuccesses++;
                                    }
                                }
                            }
                            else
                            {
                                hardSeedCounterfactualRejected++;
                                result.DiscardedCounterfactualEpisodes +=
                                    trainingRun.CounterfactualEpisodes.Count;
                            }
                        }
                        if (trainingRun.HardSeed
                            && TrainingObjectiveVictory(trainingRun))
                        {
                            hardSeedTrainingVictories++;
                        }
                        if (!trainingRun.LocalEncounter)
                        {
                            RecordCase(
                                result,
                                trainingRun.Campaign,
                                "training",
                                iterationNumber,
                                trainingRun.HardSeed
                                    ? "hard-seed-self-play"
                                    : "self-play",
                                ruleset.RulesetHash,
                                request.DecisionProfile,
                                workingChampion?.ModelId ?? "",
                                trainingRun.Episodes,
                                request);
                        }
                    }
                    UpdateHardSeedHistory(
                        result.HardSeedHistory,
                        trainingRun.Campaign,
                        trainingRun.Schedule,
                        iterationNumber,
                        trainingRun.FailureEncounterCheckpoint,
                        trainingRun.LocalEncounter);
                    if (trainingRun.CounterfactualCampaign != null)
                    {
                        UpdateHardSeedSolvability(
                            result.HardSeedHistory,
                            trainingRun.Campaign,
                            ClassifyCounterfactual(
                                trainingRun.Campaign,
                                trainingRun.CounterfactualCampaign));
                    }
                    if (trainingRun.CounterfactualCampaign?.Battles
                            .LastOrDefault()?.Outcome
                        == CombatSimulationOutcome.Victory)
                    {
                        UpdateHardSeedHistory(
                            result.HardSeedHistory,
                            trainingRun.CounterfactualCampaign,
                            trainingRun.Schedule,
                            iterationNumber,
                            trainingRun.FailureEncounterCheckpoint,
                            localEncounter: true);
                    }
                    result.TerminalConsistencyViolations +=
                        CountTerminalConsistencyViolations(
                            trainingRun.Campaign);
                    result.Replay.AddRange(trainingRun.Episodes);
                }
                result.TrainingFailures = result.TrainingFailures
                    .OrderBy(item => item.WorldSeed)
                    .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
                    .ToList();
                if (result.InvalidTrainingCampaigns
                    > invalidTrainingCampaignsBefore)
                {
                    result.CompletedCampaigns =
                        Volatile.Read(ref completedCampaigns);
                    result.Message =
                        "自博弈阶段出现无效战役；相关战役的全部轨迹已隔离，"
                        + "训练已停止，避免污染底模。失败摘要："
                        + FormatIntegrityFailureSummary(
                            result.TrainingFailures,
                            4);
                    telemetry.ApplyTo(result);
                    FinalizeCaseAnalysis(result);
                    return result;
                }
                if (result.SemanticRejectedCampaigns
                    > semanticRejectedCampaignsBefore)
                {
                    result.CompletedCampaigns =
                        Volatile.Read(ref completedCampaigns);
                    result.Message =
                        "自博弈阶段检测到语义准入失败；相关轨迹已隔离，"
                        + "训练已停止以避免污染底模。"
                        + result.SemanticGateFailureReason;
                    telemetry.ApplyTo(result);
                    FinalizeCaseAnalysis(result);
                    return result;
                }
            }

            telemetry.BeginPhase("replay-selection");
            if (request.EnableReplayWarehouse
                && request.ReplayArchiveSink != null)
            {
                try
                {
                    replayArchiveReport = request.ReplayArchiveSink(
                        iterationNumber,
                        result.Replay)
                        ?? replayArchiveReport;
                    result.ReplayArchivedEpisodes +=
                        replayArchiveReport.ArchivedEpisodes;
                    result.ReplayArchiveDuplicates +=
                        replayArchiveReport.DuplicateEpisodes;
                    result.ReplayArchivedBytes +=
                        replayArchiveReport.ArchivedBytes;
                    if (!string.IsNullOrWhiteSpace(
                            replayArchiveReport.WarehousePath))
                    {
                        result.ReplayWarehousePath =
                            replayArchiveReport.WarehousePath;
                    }
                    if (!string.IsNullOrWhiteSpace(replayArchiveReport.Error))
                    {
                        result.ReplayWarehouseError = replayArchiveReport.Error;
                    }
                }
                catch (Exception ex)
                {
                    replayArchiveReport.Error = ex.Message;
                    result.ReplayWarehouseError = ex.Message;
                }
            }
            var allCollectedReplay = result.Replay.ToList();
            var currentIterationReplay = result.Replay
                .Where(episode => episode?.Campaign?.TrainingIteration
                                  == iterationNumber)
                .ToList();
            var replaySelection = CombatFoundationReplaySampler.Select(
                result.Replay,
                replayHotEpisodeLimit,
                request.EnableStratifiedReplay,
                new CombatFoundationReplayBalanceOptions
                {
                    MinimumAdvancedShare =
                        request.MinimumAdvancedReplayShare,
                    MinimumAdvancedDefeatShare =
                        request.MinimumAdvancedDefeatReplayShare,
                    EnablePrioritySampling =
                        request.EnablePrioritizedReplay,
                    TargetAdvancedShare =
                        curriculumPlan.AdvancedShare,
                    AllowCrossDifficultyBackfill = false
                });
            CombatFoundationReplaySampler.PinEpisodes(
                replaySelection,
                contentReplay,
                replayHotEpisodeLimit,
                request.AuthoritativeContentReplayShare);
            CombatFoundationReplaySampler.PinCurrentIterationEpisodes(
                replaySelection,
                currentIterationReplay,
                replayHotEpisodeLimit,
                replayCurrentIterationShare);
            CombatFoundationReplaySampler.ApplyResourceBudget(
                replaySelection,
                contentReplay.Concat(currentIterationReplay),
                foundationTrainingOptions.MinimumEpisodes,
                replayHotFrameLimit,
                replayHotBytesLimit);
            var replayWindow = replaySelection.Episodes;
            var replayWindowOptions =
                new CombatTrainingReplayWindowOptions
                {
                    MaximumFrames = transformerTeacherOptions.MaximumFrames,
                    MaximumFramesPerEpisode = foundationTrainingOptions
                        .MaximumFramesPerEpisode,
                    // Forced decisions still carry useful dynamics, outcome,
                    // risk and history supervision for the world model.
                    RequireMultipleCandidates = false,
                    RequiredStrategyClassFrames =
                        RequiredStrategyClassFrames(
                            result.Iterations.LastOrDefault()
                                ?.TransformerTeacher),
                    MaximumUnsafeEndTurnShare = Math.Min(
                        0.80d,
                        foundationTrainingOptions
                            .MaximumUnsafeEndTurnFrameShare
                        + foundationTrainingOptions
                            .UnsafeEndTurnRiskAuxiliaryShare)
                };
            var teacherStudentPool =
                CombatTrainingReplayWindowSelector.Select(
                    replayWindow,
                    replayWindowOptions);
            if (teacherStudentPool.StrategyQuotaActive
                && !teacherStudentPool.StrategyQuotaPassed)
            {
                telemetry.BeginPhase("strategy-quota-repair");
                teacherStudentPool =
                    CombatTrainingReplayWindowSelector.RepairStrategyQuota(
                        teacherStudentPool,
                        allCollectedReplay,
                        replayWindowOptions);
            }
            var strategyQuotaCollectionCampaigns = 0;
            var strategyQuotaCollectionEpisodes = 0;
            if (teacherStudentPool.StrategyQuotaActive
                && !teacherStudentPool.StrategyQuotaPassed)
            {
                telemetry.BeginPhase("strategy-quota-collection");
                var targetedCollection = new List<CombatEpisode>();
                var maximumCollectionCampaigns =
                    StrategyQuotaCollectionCampaignLimit(
                        teacherStudentPool.StrategyQuotaShortfalls);
                const int collectionBatchSize = 2;
                var noProgressBatches = 0;
                for (var batchStart = 0;
                     batchStart < maximumCollectionCampaigns
                     && !teacherStudentPool.StrategyQuotaPassed
                     && noProgressBatches < 2;
                     batchStart += collectionBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var shortfallBefore = StrategyQuotaShortfallTotal(
                        teacherStudentPool.StrategyQuotaShortfalls);
                    var batchCount = Math.Min(
                        collectionBatchSize,
                        maximumCollectionCampaigns - batchStart);
                    var collectionDifficulties = Enumerable.Range(
                            batchStart,
                            batchCount)
                        .Select(index => StrategyQuotaCollectionDifficulty(
                            teacherStudentPool.StrategyQuotaShortfalls,
                            index,
                            strategyQuotaYieldProfiles))
                        .ToArray();
                    var seedBase = trainingSeed;
                    trainingSeed += (ulong)batchCount;
                    var collectionCampaigns =
                        new CombatCampaignResult?[batchCount];
                    var collectionEpisodes =
                        new List<CombatEpisode>?[batchCount];
                    CombatFoundationWorkScheduler.For(
                        batchCount,
                        Math.Min(parallelism, batchCount),
                        cancellationToken,
                        batchOffset =>
                        {
                            var collectionIndex = batchStart + batchOffset;
                            var difficulty =
                                collectionDifficulties[batchOffset];
                            var campaignSeed = seedBase + (ulong)batchOffset;
                            var collectionFactory =
                                new RecordingCampaignPolicyFactory(
                                    teacherProfile,
                                    championModel,
                                    request.DecisionProfile,
                                    Math.Max(
                                        0.35d,
                                        effectiveExplorationProbability),
                                    request.SelfPlayExplorationTemperature,
                                    campaignSeed,
                                    Math.Max(
                                        0.75d,
                                        request
                                            .HardTeacherExactBranchProbability),
                                    campaignRunner.SimulationEngine,
                                    request.ContentSetHash,
                                    request.OwnerModSetHash,
                                    recordWorldModelObservations);
                            var collectionCampaign = RunCampaign(
                                request.TrainingCampaign,
                                difficulty,
                                campaignSeed,
                                ruleset,
                                collectionFactory,
                                telemetry,
                                "training-strategy-quota-repair:"
                                + iterationNumber,
                                cancellationToken);
                            collectionCampaigns[batchOffset] =
                                collectionCampaign;
                            collectionEpisodes[batchOffset] =
                                collectionFactory.Complete(
                                    collectionCampaign,
                                    0,
                                    ":strategy-quota-repair:"
                                    + iterationNumber
                                    + ":"
                                    + collectionIndex);
                        },
                        telemetry.SchedulerProgress);
                    for (var batchOffset = 0;
                         batchOffset < batchCount;
                         batchOffset++)
                    {
                        var collectionCampaign =
                            collectionCampaigns[batchOffset]!;
                        var episodes = collectionEpisodes[batchOffset]
                                       ?? new List<CombatEpisode>();
                        strategyQuotaCollectionCampaigns++;
                        var semanticAudit = AggregateSemanticAudit(
                            new[] { collectionCampaign });
                        if (collectionCampaign.Invalid
                            || !SemanticGateSatisfied(semanticAudit))
                        {
                            result.DiscardedInvalidEpisodes += episodes.Count;
                            continue;
                        }
                        ApplyCampaignTargets(
                            episodes,
                            collectionCampaign,
                            curriculumPlan.Stage + ":strategy-quota-repair",
                            iterationNumber);
                        result.FeatureLeakageViolations +=
                            SanitizeEpisodeFeatures(episodes);
                        result.GeneratedReplayEpisodes += episodes.Count;
                        strategyQuotaCollectionEpisodes += episodes.Count;
                        RecordStrategyQuotaYield(
                            strategyQuotaYieldProfiles,
                            collectionDifficulties[batchOffset],
                            episodes);
                        targetedCollection.AddRange(episodes);
                        allCollectedReplay.AddRange(episodes);
                    }
                    teacherStudentPool =
                        CombatTrainingReplayWindowSelector
                            .RepairStrategyQuota(
                                teacherStudentPool,
                                targetedCollection,
                                replayWindowOptions);
                    var shortfallAfter = StrategyQuotaShortfallTotal(
                        teacherStudentPool.StrategyQuotaShortfalls);
                    noProgressBatches = shortfallAfter < shortfallBefore
                        ? 0
                        : noProgressBatches + 1;
                }
            }
            var trainingReplayWindow = teacherStudentPool.Episodes;
            var selectionAnchor = EnsureModelSelectionAnchor(
                request,
                trainingReplayWindow);
            if (selectionAnchor.Count > 0)
            {
                var anchorRuns = selectionAnchor
                    .Select(ModelSelectionRunKey)
                    .ToHashSet(StringComparer.Ordinal);
                trainingReplayWindow = trainingReplayWindow
                    .Where(episode =>
                        !anchorRuns.Contains(ModelSelectionRunKey(episode)))
                    .ToList();
            }
            if (teacherStudentPool.StrategyQuotaRepairAddedEpisodes > 0)
            {
                var repairPinLimit = Math.Max(
                    1,
                    replayHotEpisodeLimit / 4);
                var repairPins = trainingReplayWindow
                    .Where(episode => (episode.Frames?.Count ?? 0) > 0)
                    .OrderByDescending(
                        CombatFoundationReplaySampler.RecoveryPriority)
                    .ThenBy(episode => episode.EpisodeId, StringComparer.Ordinal)
                    .Take(repairPinLimit)
                    .ToList();
                var pinnedIds = repairPins.Select(episode => episode.EpisodeId)
                    .ToHashSet(StringComparer.Ordinal);
                replayWindow = repairPins.Concat(replayWindow.Where(episode =>
                        !pinnedIds.Contains(episode.EpisodeId)))
                    .Take(replayHotEpisodeLimit)
                    .OrderBy(episode => episode.EpisodeId, StringComparer.Ordinal)
                    .ToList();
            }
            replaySelection.Episodes = replayWindow;
            CombatFoundationReplaySampler.ApplyResourceBudget(
                replaySelection,
                contentReplay.Concat(currentIterationReplay),
                foundationTrainingOptions.MinimumEpisodes,
                replayHotFrameLimit,
                replayHotBytesLimit);
            replayWindow = replaySelection.Episodes;
            result.Replay = replayWindow;
            // Replay selection clones episode envelopes while intentionally
            // sharing frame objects. Protect the selected frame identities;
            // comparing cloned episode references would release the shared
            // observations immediately before Transformer export.
            var teacherReplayFrames = trainingReplayWindow
                .SelectMany(episode => episode.Frames
                                       ?? new List<CombatEpisodeFrame>())
                .ToHashSet();
            ReleaseTransientEpisodeStorage(
                allCollectedReplay,
                teacherReplayFrames);
            CombatRiskAwareRootSamplingPuctPlanner.TrimRetainedSearchMemory();
            CompactManagedHeap();
            var teacherDatasetReleased = false;
            var transformerTeacherReport =
                new CombatTransformerTeacherReport
                {
                    Iteration = iterationNumber,
                    RequestedBackend = transformerTeacherOptions.Backend,
                    Requested = !string.Equals(
                        transformerTeacherOptions.Backend,
                        CombatTransformerTeacherBackendNames.Disabled,
                        StringComparison.Ordinal)
                };
            if (transformerTeacherReport.Requested)
            {
                telemetry.BeginPhase("transformer-teacher");
                if (transformerTeacher == null)
                {
                    transformerTeacherReport.Message =
                        "Transformer teacher backend is not installed in this worker.";
                    CombatTransformerTeacherFailureProtocol.Mark(
                        transformerTeacherReport,
                        CombatTransformerTeacherFailureKinds.Configuration,
                        retryable: false,
                        formalModelBlocked: true);
                }
                else
                {
                    try
                    {
                        transformerTeacherReport =
                            transformerTeacher.TrainAndAnnotate(
                                new CombatTransformerTeacherContext
                                {
                                    Iteration = iterationNumber,
                                    TotalIterations = iterations,
                                    FinalRefreshRequested =
                                        request.Resume == null
                                        || request.FinalizeTransformerTeacher,
                                    DecisionProfile = request.DecisionProfile,
                                    Episodes = trainingReplayWindow,
                                    Options = transformerTeacherOptions,
                                    CorpusCompatibilityKey =
                                        CombatTransformerTeacherCorpusProtocol
                                            .CorpusCompatibilityKey(
                                                compatibility,
                                                request.DecisionProfile,
                                                transformerTeacherOptions),
                                    TeacherCompatibilityKey =
                                        CombatTransformerTeacherCorpusProtocol
                                            .TeacherCompatibilityKey(
                                                CombatTransformerTeacherCorpusProtocol
                                                    .CorpusCompatibilityKey(
                                                        compatibility,
                                                        request.DecisionProfile,
                                                        transformerTeacherOptions),
                                                transformerTeacherOptions),
                                    ReleaseExportedDataset = () =>
                                    {
                                        if (teacherDatasetReleased)
                                        {
                                            return new CombatTransformerTeacherHostReleaseReport
                                            {
                                                Attempted = true,
                                                Diagnostic =
                                                    "teacher dataset storage was already released"
                                            };
                                        }
                                        var process = System.Diagnostics.Process
                                            .GetCurrentProcess();
                                        var beforeWorkingSet = process.WorkingSet64;
                                        var beforeHeap =
                                            GC.GetTotalMemory(
                                                forceFullCollection: false);
                                        var releasedEpisodes =
                                            trainingReplayWindow.Count;
                                        var releasedFrames =
                                            trainingReplayWindow.Sum(episode =>
                                                episode?.Frames?.Count ?? 0);
                                        ReleaseTransientEpisodeStorage(
                                            trainingReplayWindow);
                                        ReleaseTransientEpisodeStorage(
                                            replayWindow);
                                        CombatRiskAwareRootSamplingPuctPlanner
                                            .TrimRetainedSearchMemory();
                                        CompactManagedHeap();
                                        process.Refresh();
                                        teacherDatasetReleased = true;
                                        return new CombatTransformerTeacherHostReleaseReport
                                        {
                                            Attempted = true,
                                            ReleasedEpisodes = releasedEpisodes,
                                            ReleasedFrames = releasedFrames,
                                            WorkingSetBeforeBytes =
                                                beforeWorkingSet,
                                            WorkingSetAfterBytes =
                                                process.WorkingSet64,
                                            GcHeapBeforeBytes = beforeHeap,
                                            GcHeapAfterBytes =
                                                GC.GetTotalMemory(
                                                    forceFullCollection: false)
                                        };
                                    },
                                    Progress = progress =>
                                        telemetry.TransformerTeacherProgress(
                                            progress)
                                },
                                cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        transformerTeacherReport.Message =
                            "Transformer teacher execution boundary failed: "
                            + ex.Message;
                        CombatTransformerTeacherFailureProtocol.Mark(
                            transformerTeacherReport,
                            CombatTransformerTeacherFailureKinds.Process,
                            retryable: false,
                            formalModelBlocked: true);
                    }
                }
                result.TransformerTeacherReports.Add(transformerTeacherReport);
            }
            // Observation envelopes are needed only through teacher export.
            // Compact state/action columns and teacher annotations remain for
            // the MLP trainer and the cross-process checkpoint.
            ReleaseTransientEpisodeStorage(trainingReplayWindow);
            ReleaseTransientEpisodeStorage(replayWindow);
            CompactManagedHeap();
            transformerTeacherReport.PolicyTeacherFreshnessAgeIterations =
                PolicyTeacherFreshnessAge(
                    iterationNumber,
                    transformerTeacherReport,
                    result.Iterations);
            transformerTeacherReport.PolicyTeacherFreshnessGatePassed =
                !transformerTeacherReport.Requested
                || transformerTeacherReport.PolicyTeacherApplied
                || transformerTeacherReport.PolicyTeacherFreshnessAgeIterations
                   <= transformerTeacherOptions
                       .MaximumPolicyTeacherStalenessIterations;
            if (transformerTeacherOptions.BlockTrainingWhenPolicyTeacherStale
                && !transformerTeacherReport
                    .PolicyTeacherFreshnessGatePassed)
            {
                var staleMessage = "Transformer policy teacher has been stale for "
                                   + transformerTeacherReport
                                       .PolicyTeacherFreshnessAgeIterations
                                   + " iterations; configured maximum is "
                                   + transformerTeacherOptions
                                       .MaximumPolicyTeacherStalenessIterations
                                   + ".";
                transformerTeacherReport.Message = string.IsNullOrWhiteSpace(
                    transformerTeacherReport.Message)
                    ? staleMessage
                    : transformerTeacherReport.Message + " " + staleMessage;
                CombatTransformerTeacherFailureProtocol.Mark(
                    transformerTeacherReport,
                    string.IsNullOrWhiteSpace(
                        transformerTeacherReport.FailureKind)
                        ? CombatTransformerTeacherFailureKinds.TransientResource
                        : transformerTeacherReport.FailureKind,
                    retryable: true,
                    formalModelBlocked: true,
                    processExitCode:
                    transformerTeacherReport.ProcessExitCode);
            }
            if (CombatTransformerTeacherFailureProtocol.BlocksFormalModel(
                    transformerTeacherReport))
            {
                result.Success = false;
                result.AcceptancePassed = false;
                result.FormalModelBlocked = true;
                result.FormalModelBlockReason = transformerTeacherReport.Message;
                result.NextIteration = iteration;
                result.IterationStopReason =
                    "transformer-teacher-formal-model-blocked";
                result.Message = "第 "
                                 + iterationNumber
                                 + (transformerTeacherReport.RetryableFailure
                                     ? " 轮 Transformer Teacher 连续未能提供新鲜蒸馏标注，已在可恢复边界停止训练。"
                                     : " 轮 Transformer Teacher 出现不可重试的配置、协议或执行边界故障，已停止后续训练。")
                                 + "当前数据与检查点可在修复后恢复；"
                                 + "本次训练结果不可作为正式底模。原因："
                                 + transformerTeacherReport.Message;
                telemetry.BeginPhase("transformer-teacher-blocked");
                PublishCheckpoint(
                    request,
                    CreateResumeState(
                        "model-training",
                        iteration,
                        completedCampaigns,
                        result,
                        telemetry,
                        workingChampion,
                        resumeModelTraining ? resume!.ModelTraining : null,
                        trainingSchedule));
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
                telemetry.ApplyTo(result);
                FinalizeCaseAnalysis(result);
                return result;
            }
            var effectiveDistillation =
                EffectiveTransformerDistillationWeight(
                    transformerTeacherReport,
                    transformerTeacherOptions.DistillationWeight,
                    workingChampion,
                    result.Iterations,
                    transformerTeacherOptions.MinimumFrames);
            foundationTrainingOptions.TransformerDistillationWeight =
                effectiveDistillation.Weight;
            transformerTeacherReport.EffectiveDistillationWeight =
                effectiveDistillation.Weight;
            transformerTeacherReport.DistillationStudentGuardApplied =
                effectiveDistillation.Guarded;
            transformerTeacherReport.DistillationStudentGuardReason =
                effectiveDistillation.Reason;
            telemetry.BeginPhase("model-training");
            PublishCheckpoint(
                request,
                CreateResumeState(
                    "model-training",
                    iteration,
                    completedCampaigns,
                    result,
                    telemetry,
                    workingChampion,
                    resumeModelTraining ? resume!.ModelTraining : null,
                    trainingSchedule));
            var trainingSession = new CombatPolicyValueTrainingSession
            {
                Resume = resumeModelTraining
                    ? resume!.ModelTraining
                    : latestTrainingModel == null
                        ? null
                        : new CombatPolicyValueTrainingResumeState
                        {
                            Model = latestTrainingModel,
                            BestModel = latestTrainingModel,
                            BestValidationLoss = double.MaxValue
                        },
                Progress = progress => telemetry.ModelTrainingProgress(
                    iterationNumber,
                    iterations,
                    progress),
                EpochCompleted = metrics =>
                {
                    var recorded = CloneEpochMetrics(
                        metrics,
                        iterationNumber);
                    try
                    {
                        request.ModelMetricRecorded?.Invoke(recorded);
                    }
                    catch
                    {
                        // Independent diagnostics must not abort training.
                    }
                },
                Checkpoint = modelTraining => PublishCheckpoint(
                    request,
                    CreateResumeState(
                        "model-training",
                        iteration,
                        completedCampaigns,
                        result,
                        telemetry,
                        workingChampion,
                        modelTraining,
                        trainingSchedule))
            };
            var trained = CombatPolicyValueTrainer.Train(
                trainingReplayWindow,
                request.DecisionProfile,
                foundationTrainingOptions,
                cancellationToken,
                trainingSession);
            if (!trained.Success || trained.Model == null)
            {
                result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
                result.Message = "第 "
                                 + (iteration + 1)
                                 + " 轮底模训练失败："
                                 + trained.Message;
                telemetry.ApplyTo(result);
                FinalizeCaseAnalysis(result);
                return result;
            }
            transformerTeacherReport.DistillationTrainingFrames =
                trained.TransformerDistillationTrainingFrames;
            transformerTeacherReport.DistillationValidationFrames =
                trained.TransformerDistillationValidationFrames;
            transformerTeacherReport.DistillationUtilization =
                transformerTeacherReport.AnnotatedFrames <= 0
                    ? 0d
                    : (trained.TransformerDistillationTrainingFrames
                       + trained.TransformerDistillationValidationFrames)
                      / (double)transformerTeacherReport.AnnotatedFrames;
            if (parallelism > 1
                && string.Equals(
                    executionPlan.Profile,
                    CombatFoundationExecutionProfileNames.Auto,
                    StringComparison.Ordinal)
                && ShouldCalibrateInference(
                    autoTune,
                    BuildInferenceAutoTuneCacheKey(
                        request,
                        trained.Model,
                        parallelism),
                    parallelism,
                    DateTime.UtcNow))
            {
                autoTune = CalibrateInferenceExecution(
                    request,
                    trained.Model,
                    replayWindow,
                    autoTune,
                    parallelism,
                    telemetry,
                    cancellationToken);
                if (autoTune.InferenceCalibrated)
                {
                    configuredInferenceMode = autoTune.SelectedInferenceMode;
                    configuredInferenceParallelism = parallelism;
                    configuredInferenceLaneCount =
                        autoTune.SelectedInferenceLaneCount;
                    configuredInferenceBatchSize =
                        autoTune.SelectedInferenceBatchSize;
                    ApplyEffectiveExecutionPlan(
                        executionPlan,
                        request,
                        parallelism,
                        configuredInferenceMode,
                        configuredInferenceParallelism,
                        configuredThreadPoolMinimumWorkerThreads,
                        configuredCheckpointSerializationParallelism,
                        configuredInferenceLaneCount,
                        configuredInferenceBatchSize);
                    // Health must describe only the newly selected plan. The
                    // self-play/model-training prefix above used the prior
                    // direct plan and would otherwise inflate batch fill.
                    inferenceIterationStart = telemetry.Current(
                        "iteration:inference-reconfigured");
                }
                result.AutoTune = autoTune;
                request.AutoTuneCache = autoTune;
                result.InferenceExecutionMode = request.InferenceExecutionMode;
                result.InferenceParallelism = request.InferenceParallelism;
                result.InferenceLaneCount = request.InferenceLaneCount;
                result.InferenceBatchSizePerLane = request.InferenceBatchSize;
                request.AutoTuneCompleted?.Invoke(autoTune);
            }
            var runTuning = governance.RunsTuningAtIteration(
                iteration,
                iterations);
            var tuning = SelectTunedCandidate(
                trained,
                result,
                request,
                ruleset,
                deploymentProfile,
                seedPlan.TuningSeedStart,
                iteration,
                runTuning ? tuningNormalCampaigns : 0,
                runTuning ? tuningAdvancedCampaigns : 0,
                parallelism,
                telemetry,
                ref completedCampaigns,
                totalCampaigns,
                cancellationToken);
            trained.Model = tuning.Model;
            trained.TrainingMetrics =
                CloneMetricSnapshot(tuning.TrainingMetrics);
            trained.ValidationMetrics =
                CloneMetricSnapshot(tuning.ValidationMetrics);
            trained.TestMetrics = CloneMetricSnapshot(tuning.TestMetrics);
            var selectionAnchorMetrics = selectionAnchor.Count == 0
                ? new CombatPolicyValueMetricSnapshot()
                : CombatPolicyValueTrainer.EvaluateFrozenAnchor(
                    selectionAnchor,
                    trained.Model,
                    foundationTrainingOptions,
                    cancellationToken);
            trained.EpochHistory.RemoveAll(item =>
                item.Epoch == tuning.Epoch && item.Calibrated);
            trained.EpochHistory.Add(new CombatPolicyValueEpochMetrics
            {
                Iteration = iteration + 1,
                Epoch = tuning.Epoch,
                Calibrated = true,
                EventKind = "selected",
                TrainingMeasurement = "full-evaluation",
                Training = CloneMetricSnapshot(tuning.TrainingMetrics),
                Validation = CloneMetricSnapshot(tuning.ValidationMetrics)
            });
            telemetry.ModelSelection(
                iteration + 1,
                tuning.Epoch,
                tuning.TrainingMetrics,
                tuning.ValidationMetrics);
            var freshlyTrainedModel = trained.Model;
            var arenaReferenceDefinition = result.Champion ?? workingChampion;
            var arenaChampionModel = arenaReferenceDefinition == null
                ? NullCombatPolicyValueModel.Instance
                : CreateParallelPolicyValueModel(
                    arenaReferenceDefinition,
                    request,
                    parallelism,
                    competingModelCount: 2);
            var bootstrapCandidate = result.Champion == null
                                     && workingChampion == null;
            var preArenaOfflineHeadRegressionGatePassed =
                !tuning.AllCandidatesRejectedOffline
                && OfflineHeadRegressionPassed(
                    trained.BaselineValidationMetrics,
                    trained.ValidationMetrics,
                    maximumOfflineHeadRegression);
            var stateFeatureCollisionRate = ModelMetric(
                trained.Model,
                "stateFeatureCollisionRate",
                double.PositiveInfinity);
            var actionFeatureCollisionRate = ModelMetric(
                trained.Model,
                "actionFeatureCollisionRate",
                double.PositiveInfinity);
            var preArenaFeatureCollisionGatePassed = FeatureCollisionGatePassed(
                trained.Model,
                maximumStateFeatureCollisionRate,
                maximumActionFeatureCollisionRate);
            var preArenaStrategyQuotaGatePassed =
                !teacherStudentPool.StrategyQuotaActive
                || teacherStudentPool.StrategyQuotaPassed;
            var currentPendingArenaCandidate =
                CreatePendingArenaCandidate(
                    iteration + 1,
                    trained,
                    selectionAnchorMetrics,
                    tuning.Epoch,
                    tuning.Score,
                    preArenaOfflineHeadRegressionGatePassed,
                    preArenaStrategyQuotaGatePassed,
                    preArenaFeatureCollisionGatePassed,
                    stateFeatureCollisionRate,
                    actionFeatureCollisionRate);
            if (PendingArenaCandidateEligible(currentPendingArenaCandidate))
            {
                result.BestPendingArenaCandidate = BetterPendingArenaCandidate(
                    currentPendingArenaCandidate,
                    result.BestPendingArenaCandidate);
            }
            var arenaCandidateSourceIteration = iteration + 1;
            var arenaCandidateSelectedFromPendingBank = false;
            var selectedTuningEpoch = tuning.Epoch;
            var selectedTuningScore = tuning.Score;
            if (arenaEvaluationRan
                && PendingArenaCandidateEligible(
                    result.BestPendingArenaCandidate))
            {
                var pending = result.BestPendingArenaCandidate!;
                arenaCandidateSourceIteration = pending.SourceIteration;
                arenaCandidateSelectedFromPendingBank =
                    pending.SourceIteration != iteration + 1
                    || !string.Equals(
                        pending.Model?.ModelId,
                        freshlyTrainedModel.ModelId,
                        StringComparison.Ordinal);
                ApplyPendingArenaCandidate(trained, pending);
                selectionAnchorMetrics = CloneMetricSnapshot(
                    pending.SelectionAnchorMetrics);
                selectedTuningEpoch = pending.SelectedEpoch;
                selectedTuningScore = pending.SelectedScore;
                preArenaOfflineHeadRegressionGatePassed =
                    pending.OfflineHeadRegressionGatePassed;
                preArenaStrategyQuotaGatePassed =
                    pending.StrategyQuotaGatePassed;
                preArenaFeatureCollisionGatePassed =
                    pending.FeatureCollisionGatePassed;
                stateFeatureCollisionRate = pending.StateFeatureCollisionRate;
                actionFeatureCollisionRate = pending.ActionFeatureCollisionRate;
            }
            var candidateModel = CreateParallelPolicyValueModel(
                trained.Model,
                request,
                parallelism,
                competingModelCount: 2);
            var arenaScreeningDiagnosticOnly =
                !preArenaOfflineHeadRegressionGatePassed
                || !preArenaFeatureCollisionGatePassed
                || !preArenaStrategyQuotaGatePassed;
            var arenaScreeningPerDifficulty =
                arenaEvaluationRan
                && !arenaScreeningDiagnosticOnly
                    ? EffectiveArenaScreeningPairsPerDifficulty(
                        arenaPerDifficulty,
                        request.ArenaEvaluationBatchSize,
                        diagnosticOnly: false)
                    : 0;
            var arenaRetryAttemptsBefore = result.ArenaRetryAttempts;
            var arenaRecoveredCampaignsBefore =
                result.ArenaRecoveredCampaigns;
            var arenaIsolatedPairsBefore = result.ArenaIsolatedPairs;
            var arenaReplacementPairsBefore =
                result.ArenaReplacementPairs;
            var arenaInvalidSignaturesBefore =
                new Dictionary<string, int>(
                    result.ArenaInvalidSignatures,
                    StringComparer.Ordinal);
            var championArena = new List<CombatCampaignResult>();
            var candidateArena = new List<CombatCampaignResult>();
            var replacementSeedStart =
                0x3800000000000000UL
                | (CombatFoundationSeedPlan.Mix(
                       seedPlan.RunSeed
                       ^ (ulong)iteration
                       ^ 0x5245504C414345UL)
                   & 0x07FFFFFFFFFFFFFFUL);
            var replacementCursor = resume?.ArenaReplacementCursor
                                    ?? result.ArenaReplacementPairs;
            var arenaInvalidSides = 0;
            var systemicArenaFailure = false;
            var invalidSignatureSeeds =
                new Dictionary<string, HashSet<ulong>>(StringComparer.Ordinal);
            var plannedArenaSides = Math.Max(
                1,
                (arenaScreeningPerDifficulty
                 + scheduledArenaConfirmationPerDifficulty) * 4);
            var arenaDifficulties = new[] { "normal", "advanced" };
            var arenaChampionPolicyFactory =
                new CombatDecisionSimulationPolicyFactory(
                    deploymentProfile,
                    policyValueModel: arenaChampionModel);
            var arenaCandidatePolicyFactory =
                new CombatDecisionSimulationPolicyFactory(
                    deploymentProfile,
                    policyValueModel: candidateModel);
            telemetry.BeginPhase("arena-screening");
            var screeningSeedStart = arenaSeed;
            arenaSeed += (ulong)(
                arenaScreeningPerDifficulty * arenaDifficulties.Length);
            var screeningPairs = arenaDifficulties
                .Select(_ => Enumerable.Range(0, arenaScreeningPerDifficulty)
                    .Select(_ => new FoundationArenaPair())
                    .ToArray())
                .ToArray();
            var screeningDecisionPairs = Math.Min(
                arenaScreeningPerDifficulty,
                Math.Max(2, Math.Min(4, request.ArenaEvaluationBatchSize)));
            var screeningDecisionInterval = screeningDecisionPairs
                                            * arenaDifficulties.Length
                                            * 2;
            var screeningWorkCount = arenaScreeningPerDifficulty
                                     * arenaDifficulties.Length
                                     * 2;
            var screeningRun = CombatFoundationWorkScheduler.RunOrdered(
                screeningWorkCount,
                parallelism,
                screeningDecisionInterval,
                cancellationToken,
                workIndex =>
                {
                    var pairWorkIndex = workIndex / 2;
                    var championSide = workIndex % 2 == 0;
                    var arenaIndex = pairWorkIndex / arenaDifficulties.Length;
                    var difficultyIndex =
                        pairWorkIndex % arenaDifficulties.Length;
                    var difficulty = arenaDifficulties[difficultyIndex];
                    var seed = screeningSeedStart
                               + (ulong)(
                                    difficultyIndex
                                    * arenaScreeningPerDifficulty
                                    + arenaIndex);
                    var campaign = RunCampaign(
                        request.TrainingCampaign,
                        difficulty,
                        seed,
                        ruleset,
                        championSide
                            ? arenaChampionPolicyFactory
                            : arenaCandidatePolicyFactory,
                        telemetry,
                        "arena:"
                        + difficulty
                        + (championSide ? ":champion" : ":candidate"),
                        cancellationToken);
                    ReportProgress(
                        request,
                        telemetry,
                        campaign,
                        ref completedCampaigns,
                        totalCampaigns,
                        "第 " + iterationNumber + " 轮：隔离种子竞技场");
                    return new FoundationArenaSide
                    {
                        DifficultyIndex = difficultyIndex,
                        ArenaIndex = arenaIndex,
                        ChampionSide = championSide,
                        Campaign = campaign
                    };
                },
                (workIndex, side) =>
                {
                    var pair = screeningPairs[side.DifficultyIndex][
                        side.ArenaIndex];
                    if (side.ChampionSide)
                    {
                        pair.Champion = side.Campaign;
                        return side;
                    }
                    pair.Candidate = side.Campaign;
                    var committed = new[] { pair };
                    systemicArenaFailure |= RecoverArenaPairs(
                        committed,
                        request,
                        result,
                        ruleset,
                        deploymentProfile,
                        arenaChampionModel,
                        candidateModel,
                        telemetry,
                        iterationNumber,
                        "screening:"
                        + arenaDifficulties[side.DifficultyIndex],
                        replacementSeedStart,
                        ref replacementCursor,
                        ref arenaInvalidSides,
                        plannedArenaSides,
                        invalidSignatureSeeds,
                        ref completedCampaigns,
                        totalCampaigns,
                        cancellationToken);
                    screeningPairs[side.DifficultyIndex][side.ArenaIndex] =
                        committed[0];
                    return side;
                },
                committedWork =>
                {
                    if (!request.EnableSequentialArenaStop)
                    {
                        return false;
                    }
                    var executedPerDifficulty = committedWork
                                                / (arenaDifficulties.Length * 2);
                    var remainingPerDifficulty = arenaScreeningPerDifficulty
                                                 - executedPerDifficulty;
                    if (remainingPerDifficulty <= 0)
                    {
                        return false;
                    }
                    var committedChampion = new List<CombatCampaignResult>();
                    var committedCandidate = new List<CombatCampaignResult>();
                    for (var difficultyIndex = 0;
                         difficultyIndex < arenaDifficulties.Length;
                         difficultyIndex++)
                    {
                        for (var arenaIndex = 0;
                             arenaIndex < executedPerDifficulty;
                             arenaIndex++)
                        {
                            committedChampion.Add(
                                screeningPairs[difficultyIndex][arenaIndex]
                                    .Champion);
                            committedCandidate.Add(
                                screeningPairs[difficultyIndex][arenaIndex]
                                    .Candidate);
                        }
                    }
                    return ShouldStopArenaScreening(
                        committedChampion,
                        committedCandidate,
                        remainingPerDifficulty,
                        normalAcceptanceRate,
                        advancedAcceptanceRate);
                },
                maximumLookAhead: Math.Max(
                    parallelism,
                    screeningDecisionInterval),
                progress: telemetry.SchedulerProgress);
            var screeningPairsExecutedPerDifficulty = screeningRun.Items.Count
                                                       / (arenaDifficulties.Length
                                                          * 2);
            var screeningStoppedEarly = screeningRun.StoppedEarly;
            var screeningPairsActuallyExecuted = Math.Min(
                arenaScreeningPerDifficulty * arenaDifficulties.Length,
                (screeningRun.Metrics.CompletedWork + 1) / 2);
            for (var difficultyIndex = 0;
                 difficultyIndex < arenaDifficulties.Length;
                 difficultyIndex++)
            {
                var arenaPairs = screeningPairs[difficultyIndex];
                for (var arenaIndex = 0;
                     arenaIndex < screeningPairsExecutedPerDifficulty;
                     arenaIndex++)
                {
                    championArena.Add(arenaPairs[arenaIndex]!.Champion);
                    candidateArena.Add(arenaPairs[arenaIndex]!.Candidate);
                }
            }

            var screeningPairIndexes = Enumerable
                .Range(0, Math.Min(championArena.Count, candidateArena.Count))
                .Where(index => !championArena[index].Invalid
                                && !candidateArena[index].Invalid)
                .ToList();
            var screeningChampionNormal = WinRate(
                screeningPairIndexes.Select(index => championArena[index])
                    .ToList(),
                "normal");
            var screeningCandidateNormal = WinRate(
                screeningPairIndexes.Select(index => candidateArena[index])
                    .ToList(),
                "normal");
            var screeningChampionAdvanced = WinRate(
                screeningPairIndexes.Select(index => championArena[index])
                    .ToList(),
                "advanced");
            var screeningCandidateAdvanced = WinRate(
                screeningPairIndexes.Select(index => candidateArena[index])
                    .ToList(),
                "advanced");
            var screeningCandidateOnlyWins = screeningPairIndexes.Count(index =>
                candidateArena[index].FinalBossVictory
                && !championArena[index].FinalBossVictory);
            var screeningChampionOnlyWins = screeningPairIndexes.Count(index =>
                championArena[index].FinalBossVictory
                && !candidateArena[index].FinalBossVictory);
            var screeningChampionScore = screeningPairIndexes.Count == 0
                ? 0d
                : screeningPairIndexes.Average(index =>
                    Score(championArena[index]));
            var screeningCandidateScore = screeningPairIndexes.Count == 0
                ? 0d
                : screeningPairIndexes.Average(index =>
                    Score(candidateArena[index]));
            var screeningChampionDepth = screeningPairIndexes.Count == 0
                ? 0d
                : screeningPairIndexes.Average(index =>
                    championArena[index].CompletedBattles);
            var screeningCandidateDepth = screeningPairIndexes.Count == 0
                ? 0d
                : screeningPairIndexes.Average(index =>
                    candidateArena[index].CompletedBattles);
            var screeningProgressGain =
                screeningCandidateScore
                >= screeningChampionScore
                   + CombatFoundationPromotionProtocol.MinimumScoreGain
                && screeningCandidateDepth
                   >= screeningChampionDepth
                      + CombatFoundationPromotionProtocol.MinimumDepthGain;
            var screeningAdvancedRecoveryRequired =
                result.Champion != null
                && screeningChampionAdvanced + 0.0000001d
                   < advancedAcceptanceRate;
            var screeningPassed =
                arenaEvaluationRan
                && screeningPairIndexes.Count == arenaScreeningPerDifficulty * 2
                && screeningCandidateNormal + 0.0000001d
                   >= screeningChampionNormal
                && screeningCandidateAdvanced + 0.0000001d
                   >= screeningChampionAdvanced
                && (!screeningAdvancedRecoveryRequired
                    || screeningCandidateAdvanced
                       > screeningChampionAdvanced + 0.0000001d)
                && (workingChampion == null
                    || screeningCandidateOnlyWins
                       > screeningChampionOnlyWins
                    || screeningProgressGain);
            var screeningAbsoluteQualificationPassed =
                arenaEvaluationRan
                && AbsoluteQualificationGatePassed(
                    screeningPairIndexes.Count,
                    arenaScreeningPerDifficulty * 2,
                    screeningCandidateNormal + 0.0000001d
                    >= normalAcceptanceRate,
                    screeningCandidateAdvanced + 0.0000001d
                    >= advancedAcceptanceRate,
                    preArenaOfflineHeadRegressionGatePassed,
                    preArenaStrategyQuotaGatePassed,
                    preArenaFeatureCollisionGatePassed);
            var confirmationRan = ShouldRunArenaConfirmation(
                screeningPassed,
                screeningAbsoluteQualificationPassed,
                scheduledArenaConfirmationPerDifficulty,
                bootstrapCandidate,
                preArenaOfflineHeadRegressionGatePassed,
                preArenaStrategyQuotaGatePassed,
                preArenaFeatureCollisionGatePassed);
            var confirmationPairsExecutedPerDifficulty = 0;
            var confirmationStoppedEarly = false;
            var confirmationAcceptedEarly = false;
            if (confirmationRan)
            {
                telemetry.BeginPhase("arena-confirmation");
                var confirmationSeedStart = arenaSeed;
                arenaSeed += (ulong)(
                    scheduledArenaConfirmationPerDifficulty
                    * arenaDifficulties.Length);
                var confirmationPairs = arenaDifficulties
                    .Select(_ => Enumerable
                        .Range(0, scheduledArenaConfirmationPerDifficulty)
                        .Select(_ => new FoundationArenaPair())
                        .ToArray())
                    .ToArray();
                var evaluationBatchSize = Math.Max(
                    1,
                    Math.Min(
                        scheduledArenaConfirmationPerDifficulty,
                        request.ArenaEvaluationBatchSize));
                var confirmationWorkCount =
                    scheduledArenaConfirmationPerDifficulty
                    * arenaDifficulties.Length;
                var confirmationRun = CombatFoundationWorkScheduler.RunOrdered(
                    confirmationWorkCount,
                    parallelism,
                    evaluationBatchSize * arenaDifficulties.Length,
                    cancellationToken,
                    workIndex =>
                    {
                        var arenaIndex = workIndex / arenaDifficulties.Length;
                        var difficultyIndex =
                            workIndex % arenaDifficulties.Length;
                        var difficulty = arenaDifficulties[difficultyIndex];
                        var seed = confirmationSeedStart
                                   + (ulong)(
                                       difficultyIndex
                                       * scheduledArenaConfirmationPerDifficulty
                                       + arenaIndex);
                        var pair = new FoundationArenaPair
                        {
                            Champion = RunCampaign(
                                request.TrainingCampaign,
                                difficulty,
                                seed,
                                ruleset,
                                arenaChampionPolicyFactory,
                                telemetry,
                                "arena-confirmation:"
                                + difficulty
                                + ":champion",
                                cancellationToken)
                        };
                        ReportProgress(
                            request,
                            telemetry,
                            pair.Champion,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 "
                            + iterationNumber
                            + " 轮：晋级确认竞技场");
                        pair.Candidate = RunCampaign(
                            request.TrainingCampaign,
                            difficulty,
                            seed,
                            ruleset,
                            arenaCandidatePolicyFactory,
                            telemetry,
                            "arena-confirmation:"
                            + difficulty
                            + ":candidate",
                            cancellationToken);
                        ReportProgress(
                            request,
                            telemetry,
                            pair.Candidate,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 "
                            + iterationNumber
                            + " 轮：晋级确认竞技场");
                        return pair;
                    },
                    (workIndex, pair) =>
                    {
                        var arenaIndex = workIndex / arenaDifficulties.Length;
                        var difficultyIndex =
                            workIndex % arenaDifficulties.Length;
                        var difficulty = arenaDifficulties[difficultyIndex];
                        var committed = new[] { pair };
                        systemicArenaFailure |= RecoverArenaPairs(
                            committed,
                            request,
                            result,
                            ruleset,
                            deploymentProfile,
                            arenaChampionModel,
                            candidateModel,
                            telemetry,
                            iterationNumber,
                            "confirmation:" + difficulty,
                            replacementSeedStart,
                            ref replacementCursor,
                            ref arenaInvalidSides,
                            plannedArenaSides,
                            invalidSignatureSeeds,
                            ref completedCampaigns,
                            totalCampaigns,
                            cancellationToken);
                        confirmationPairs[difficultyIndex][arenaIndex] =
                            committed[0];
                        championArena.Add(committed[0].Champion);
                        candidateArena.Add(committed[0].Candidate);
                        return committed[0];
                    },
                    committedWork =>
                    {
                        confirmationPairsExecutedPerDifficulty =
                            committedWork / arenaDifficulties.Length;
                        var remainingPerDifficulty =
                            scheduledArenaConfirmationPerDifficulty
                            - confirmationPairsExecutedPerDifficulty;
                        if (!request.EnableSequentialArenaStop
                            || remainingPerDifficulty <= 0)
                        {
                            return false;
                        }
                        var sequentialDecision = ArenaSequentialDecision(
                            championArena,
                            candidateArena,
                            remainingPerDifficulty,
                            minimumArenaDiscordantPairs,
                            normalAcceptanceRate,
                            advancedAcceptanceRate,
                            requireAdvancedStrictGain: false);
                        if (!ShouldStopArenaConfirmation(sequentialDecision))
                        {
                            return false;
                        }
                        confirmationStoppedEarly = true;
                        confirmationAcceptedEarly = false;
                        return true;
                    },
                    maximumLookAhead: Math.Max(
                        parallelism,
                        evaluationBatchSize * arenaDifficulties.Length
                        + parallelism),
                    progress: telemetry.SchedulerProgress);
                confirmationPairsExecutedPerDifficulty =
                    confirmationRun.Items.Count / arenaDifficulties.Length;
                confirmationStoppedEarly |= confirmationRun.StoppedEarly;
            }
            else
            {
                // Reserve the confirmation seed partition even when screening
                // rejects the candidate so resumed and uninterrupted runs use
                // identical later-iteration seeds.
                arenaSeed += (ulong)(scheduledArenaConfirmationPerDifficulty * 2);
            }

            var invalidCandidate =
                candidateArena.Count(item => item.Invalid);
            var invalidChampion =
                championArena.Count(item => item.Invalid);
            result.TerminalConsistencyViolations += championArena.Sum(
                CountTerminalConsistencyViolations);
            result.TerminalConsistencyViolations += candidateArena.Sum(
                CountTerminalConsistencyViolations);
            for (var arenaIndex = 0;
                 arenaIndex < Math.Min(
                     championArena.Count,
                     candidateArena.Count);
                 arenaIndex++)
            {
                if (championArena[arenaIndex].Invalid)
                {
                    AddArenaFailure(
                        result,
                        iterationNumber,
                        "champion",
                        championArena[arenaIndex]);
                }
                if (candidateArena[arenaIndex].Invalid)
                {
                    AddArenaFailure(
                        result,
                        iterationNumber,
                        "candidate",
                        candidateArena[arenaIndex]);
                }
            }
            result.ArenaFailures = result.ArenaFailures
                .OrderBy(item => item.Iteration)
                .ThenBy(item => item.WorldSeed)
                .ThenBy(item => item.Competitor, StringComparer.Ordinal)
                .ToList();
            foreach (var campaign in championArena)
            {
                RecordCase(
                    result,
                    campaign,
                    "arena",
                    iterationNumber,
                    "champion",
                    ruleset.RulesetHash,
                    request.DecisionProfile,
                    workingChampion?.ModelId ?? "",
                    episodes: null,
                    request: request);
            }
            foreach (var campaign in candidateArena)
            {
                RecordCase(
                    result,
                    campaign,
                    "arena",
                    iterationNumber,
                    "candidate",
                    ruleset.RulesetHash,
                    request.DecisionProfile,
                    trained.Model.ModelId,
                    episodes: null,
                    request: request);
            }
            var validPairIndexes = Enumerable
                .Range(0, Math.Min(championArena.Count, candidateArena.Count))
                .Where(index => !championArena[index].Invalid
                                && !candidateArena[index].Invalid)
                .ToList();
            var validChampionArena = validPairIndexes
                .Select(index => championArena[index])
                .ToList();
            var validCandidateArena = validPairIndexes
                .Select(index => candidateArena[index])
                .ToList();
            var validNormalPairs = validPairIndexes.Count(index =>
                string.Equals(
                    candidateArena[index].DifficultyId,
                    "normal",
                    StringComparison.Ordinal));
            var validAdvancedPairs = validPairIndexes.Count(index =>
                string.Equals(
                    candidateArena[index].DifficultyId,
                    "advanced",
                    StringComparison.Ordinal));
            var candidateOnlyWins = validPairIndexes.Count(index =>
                candidateArena[index].FinalBossVictory
                && !championArena[index].FinalBossVictory);
            var championOnlyWins = validPairIndexes.Count(index =>
                championArena[index].FinalBossVictory
                && !candidateArena[index].FinalBossVictory);
            var arenaHardSeedsMined = 0;
            // Arena seeds retire after this iteration; only the next curriculum
            // may train them, while validation seeds remain permanently isolated.
            foreach (var pairIndex in validPairIndexes)
            {
                if (!championArena[pairIndex].FinalBossVictory
                    || candidateArena[pairIndex].FinalBossVictory)
                {
                    continue;
                }
                UpdateHardSeedHistory(
                    result.HardSeedHistory,
                    candidateArena[pairIndex],
                    slot: null,
                    iterationNumber);
                arenaHardSeedsMined++;
            }
            var championScore = validChampionArena.Count == 0
                ? 0d
                : validChampionArena.Average(Score);
            var candidateScore = validCandidateArena.Count == 0
                ? 0d
                : validCandidateArena.Average(Score);
            var championNormal = WinRate(validChampionArena, "normal");
            var candidateNormal = WinRate(validCandidateArena, "normal");
            var championAdvanced = WinRate(validChampionArena, "advanced");
            var candidateAdvanced = WinRate(validCandidateArena, "advanced");
            var championAverageDepth = validChampionArena.Count == 0
                ? 0d
                : validChampionArena.Average(item => item.CompletedBattles);
            var candidateAverageDepth = validCandidateArena.Count == 0
                ? 0d
                : validCandidateArena.Average(item => item.CompletedBattles);
            var expectedArenaPairs = (
                arenaScreeningPerDifficulty
                + (confirmationRan
                    ? scheduledArenaConfirmationPerDifficulty
                    : 0)) * 2;
            var expectedQualificationPairs =
                ExpectedArenaQualificationPairs(
                    arenaScreeningPerDifficulty,
                    arenaConfirmationPerDifficulty,
                    confirmationRan);
            var advancedRecoveryRequired =
                arenaReferenceDefinition != null
                && championAdvanced + 0.0000001d
                   < advancedAcceptanceRate;
            var curriculumCheckpoint =
                arenaEvaluationRan
                && validPairIndexes.Count == expectedArenaPairs
                                       && candidateNormal + 0.0000001d >= championNormal
                                       && candidateAdvanced + 0.0000001d >= championAdvanced
                                       && (!advancedRecoveryRequired
                                           || candidateAdvanced
                                              > championAdvanced
                                                + 0.0000001d);
            var workingCheckpoint =
                arenaEvaluationRan
                && validPairIndexes.Count == expectedArenaPairs
                && candidateNormal + 0.0000001d >= championNormal
                && candidateAdvanced + 0.0000001d >= championAdvanced;
            var discordantPairs =
                candidateOnlyWins + championOnlyWins;
            var pairedWinWilsonLowerBound =
                CombatFoundationCurriculum.WilsonLowerBound(
                    candidateOnlyWins,
                    discordantPairs);
            var pairedRegressionWilsonUpperBound =
                validPairIndexes.Count == 0
                    ? 1d
                    : 1d - CombatFoundationCurriculum.WilsonLowerBound(
                        validPairIndexes.Count - championOnlyWins,
                        validPairIndexes.Count);
            var meaningfulWinGain =
                discordantPairs >= minimumArenaDiscordantPairs
                && candidateOnlyWins > championOnlyWins
                && pairedWinWilsonLowerBound
                   >= CombatFoundationPromotionProtocol
                       .MinimumPairedWinWilsonLowerBound;
            var meaningfulProgressGain =
                candidateScore
                >= championScore
                   + CombatFoundationPromotionProtocol.MinimumScoreGain
                && candidateAverageDepth
                   >= championAverageDepth
                      + CombatFoundationPromotionProtocol.MinimumDepthGain;
            var iterativeGain =
                meaningfulWinGain || meaningfulProgressGain;
            var bootstrapPromotion = arenaEvaluationRan && bootstrapCandidate;
            var arenaEvidenceGatePassed =
                discordantPairs >= minimumArenaDiscordantPairs;
            var absoluteNormalGatePassed =
                arenaEvaluationRan
                && candidateNormal + 0.0000001d >= normalAcceptanceRate;
            var absoluteAdvancedGatePassed =
                arenaEvaluationRan
                && candidateAdvanced + 0.0000001d >= advancedAcceptanceRate;
            var offlineHeadRegressionGatePassed =
                preArenaOfflineHeadRegressionGatePassed;
            var strategyQuotaGatePassed =
                preArenaStrategyQuotaGatePassed;
            var featureCollisionGatePassed =
                preArenaFeatureCollisionGatePassed;
            var absoluteQualificationGatePassed =
                AbsoluteQualificationGatePassed(
                    validPairIndexes.Count,
                    expectedQualificationPairs,
                    absoluteNormalGatePassed,
                    absoluteAdvancedGatePassed,
                    offlineHeadRegressionGatePassed,
                    strategyQuotaGatePassed,
                    featureCollisionGatePassed);
            var nonInferiorityGatePassed = NonInferiorityGatePassed(
                workingCheckpoint,
                validNormalPairs,
                validAdvancedPairs,
                candidateOnlyWins,
                championOnlyWins,
                pairedRegressionWilsonUpperBound,
                absoluteNormalGatePassed,
                absoluteAdvancedGatePassed,
                offlineHeadRegressionGatePassed,
                strategyQuotaGatePassed,
                featureCollisionGatePassed);
            var formalPromotionGatePassed = FormalPromotionGatePassed(
                bootstrapPromotion,
                arenaEvidenceGatePassed,
                absoluteAdvancedGatePassed,
                offlineHeadRegressionGatePassed,
                strategyQuotaGatePassed,
                featureCollisionGatePassed);
            var promoted = curriculumCheckpoint
                           && !bootstrapPromotion
                           && iterativeGain
                           && formalPromotionGatePassed;
            var provisionalChampionSelected =
                result.Champion == null
                && nonInferiorityGatePassed;
            var pairedEvidenceKind = promoted
                ? CombatFoundationPromotionProtocol.SignificantImprovement
                : nonInferiorityGatePassed
                    ? CombatFoundationPromotionProtocol.EquivalentNonInferior
                    : !workingCheckpoint
                      || candidateOnlyWins < championOnlyWins
                      || !absoluteNormalGatePassed
                      || !absoluteAdvancedGatePassed
                        ? CombatFoundationPromotionProtocol.Regressed
                        : CombatFoundationPromotionProtocol
                            .InsufficientEvidence;
            var workingWindowAccepted = ShouldAcceptWorkingModel(
                workingCheckpoint,
                bootstrapPromotion,
                meaningfulWinGain,
                meaningfulProgressGain)
                                       || nonInferiorityGatePassed;
            workingWindowAccepted = workingWindowAccepted
                                       && (bootstrapPromotion
                                           || offlineHeadRegressionGatePassed)
                                       && featureCollisionGatePassed;
            var promotionReason = !arenaEvaluationRan
                ? "scheduled-training-continuation"
                : !preArenaOfflineHeadRegressionGatePassed
                    ? "offline-head-regression"
                : !preArenaStrategyQuotaGatePassed
                    ? "strategy-quota-shortfall"
                : !preArenaFeatureCollisionGatePassed
                    ? "feature-collision-gate"
                : !curriculumCheckpoint
                ? advancedRecoveryRequired
                  && candidateAdvanced
                     <= championAdvanced + 0.0000001d
                    ? "advanced-target-not-improved"
                    : "regression-or-incomplete-arena"
                : bootstrapPromotion
                    ? provisionalChampionSelected
                        ? "bootstrap-noninferior"
                        : featureCollisionGatePassed
                            ? "bootstrap-working-window"
                        : "feature-collision-gate"
                : nonInferiorityGatePassed
                    ? "equivalent-noninferior"
                : !iterativeGain
                    ? "no-iterative-gain"
                    : !absoluteAdvancedGatePassed
                        ? "absolute-advanced-gate"
                    : !arenaEvidenceGatePassed
                        ? "insufficient-discordant-pairs"
                    : promoted
                        ? meaningfulWinGain
                            ? "paired-win-gain"
                            : "score-depth-gain"
                        : "no-meaningful-gain";
            var inferenceIterationEnd = telemetry.Current(
                "iteration:inference-completed");
            var inferenceHealth = CombatFoundationInferenceHealthProtocol
                .Evaluate(inferenceIterationStart, inferenceIterationEnd);
            var completedIteration = new CombatCampaignFoundationIteration
            {
                Iteration = iteration + 1,
                HadIncumbentModel = arenaReferenceDefinition != null,
                ArenaEvaluationRan = arenaEvaluationRan,
                FormalArenaConfirmationScheduled =
                    formalArenaConfirmationScheduled,
                TrainingOnlyIteration = !arenaEvaluationRan,
                ArenaCandidateSourceIteration =
                    arenaEvaluationRan ? arenaCandidateSourceIteration : 0,
                ArenaCandidateSelectedFromPendingBank =
                    arenaEvaluationRan
                    && arenaCandidateSelectedFromPendingBank,
                PendingArenaCandidateRetained =
                    !arenaEvaluationRan
                    && PendingArenaCandidateEligible(
                        result.BestPendingArenaCandidate),
                CandidateQualificationState =
                    absoluteQualificationGatePassed
                        ? CombatFoundationPromotionProtocol.ConfirmedQualified
                        : screeningPassed
                          || screeningAbsoluteQualificationPassed
                            ? CombatFoundationPromotionProtocol.ScreeningPassed
                            : preArenaOfflineHeadRegressionGatePassed
                              && preArenaStrategyQuotaGatePassed
                              && preArenaFeatureCollisionGatePassed
                                ? CombatFoundationPromotionProtocol.OfflineSafe
                                : CombatFoundationPromotionProtocol
                                    .OfflineRejected,
                ScreeningQualificationGatePassed =
                    screeningPassed || screeningAbsoluteQualificationPassed,
                FormalConfirmationCompleted = confirmationRan
                                              && confirmationPairsExecutedPerDifficulty
                                              == arenaConfirmationPerDifficulty,
                WorkerProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                ParallelismDecision = parallelismDecision,
                ReplayEpisodes = result.Replay.Count,
                TrainingReplayEpisodes = replaySelection.Episodes.Count,
                TrainingReplayNormalEpisodes =
                    replaySelection.NormalEpisodes,
                TrainingReplayAdvancedEpisodes =
                    replaySelection.AdvancedEpisodes,
                TrainingReplayAdvancedDefeatEpisodes =
                    replaySelection.AdvancedDefeatEpisodes,
                TrainingReplaySuccessfulEpisodes =
                    replaySelection.SuccessfulEpisodes,
                TrainingReplayDroppedDuplicates =
                    replaySelection.DroppedDuplicateEpisodes,
                TrainingReplayTargetNormalShare =
                    replaySelection.TargetNormalShare,
                TrainingReplayTargetAdvancedDefeatShare =
                    replaySelection.TargetAdvancedDefeatShare,
                TrainingReplaySourceCampaigns =
                    replaySelection.SourceCampaigns,
                TrainingReplaySelectedCampaigns =
                    replaySelection.SelectedCampaigns,
                TrainingReplaySuccessfulCampaigns =
                    replaySelection.SuccessfulCampaigns,
                TrainingReplaySourcePriorityMean =
                    replaySelection.SourcePriorityMean,
                TrainingReplaySelectedPriorityMean =
                    replaySelection.SelectedPriorityMean,
                TrainingReplayHighPriorityEpisodes =
                    replaySelection.SelectedHighPriorityEpisodes,
                TrainingReplayPinnedContentEpisodes =
                    replaySelection.PinnedContentEpisodes,
                TrainingReplayFrames = replaySelection.SelectedFrames,
                TrainingReplayEstimatedResidentBytes =
                    replaySelection.EstimatedResidentBytes,
                ReplayArchivedEpisodes =
                    replayArchiveReport.ArchivedEpisodes,
                ReplayArchiveDuplicates =
                    replayArchiveReport.DuplicateEpisodes,
                ReplayArchivedBytes = replayArchiveReport.ArchivedBytes,
                ReplayLoadedHistoricalEpisodes =
                    iteration == startIteration
                        ? loadedHistoricalThisInvocationEpisodes
                        : 0,
                ReplayLoadedHistoricalBytes =
                    iteration == startIteration
                        ? loadedHistoricalThisInvocationBytes
                        : 0L,
                ReplayPinnedCurrentIterationEpisodes =
                    replaySelection.PinnedCurrentIterationEpisodes,
                TrainingReplayResourceBudgetDroppedEpisodes =
                    replaySelection.ResourceBudgetDroppedEpisodes,
                ResourceElapsedSeconds = Math.Max(
                    0d,
                    inferenceIterationEnd.ElapsedSeconds
                    - inferenceIterationStart.ElapsedSeconds),
                ResourceCpuSeconds = Math.Max(
                    0d,
                    inferenceIterationEnd.CpuSeconds
                    - inferenceIterationStart.CpuSeconds),
                ResourceCpuUtilizationPercent = Math.Max(
                    0d,
                    (inferenceIterationEnd.CpuSeconds
                     - inferenceIterationStart.CpuSeconds)
                    / Math.Max(
                        0.001d,
                        inferenceIterationEnd.ElapsedSeconds
                        - inferenceIterationStart.ElapsedSeconds)
                    / Math.Max(1, Environment.ProcessorCount)
                    * 100d),
                ResourceAllocatedBytes = Math.Max(
                    0L,
                    inferenceIterationEnd.AllocatedBytes
                    - inferenceIterationStart.AllocatedBytes),
                ResourceWorkingSetBytes =
                    inferenceIterationEnd.WorkingSetBytes,
                ResourcePrivateMemoryBytes =
                    inferenceIterationEnd.PrivateMemoryBytes,
                ResourceGcHeapSizeBytes =
                    inferenceIterationEnd.GcHeapSizeBytes,
                ResourceGcFragmentedBytes =
                    inferenceIterationEnd.GcFragmentedBytes,
                ResourceMemoryLoadBytes =
                    inferenceIterationEnd.MemoryLoadBytes,
                ResourceTotalAvailableMemoryBytes =
                    inferenceIterationEnd.TotalAvailableMemoryBytes,
                TrainingReplayQuotaShortfalls =
                    new Dictionary<string, int>(
                        replaySelection.QuotaShortfalls,
                        StringComparer.Ordinal),
                HardSeedSourceCampaigns =
                    hardSeedPlan.SourceCampaigns,
                HardSeedRoutedBuildLimitedCampaigns =
                    hardSeedPlan.RoutedBuildLimitedCampaigns,
                HardSeedRoutedProvisionalBuildLimitedCampaigns =
                    hardSeedPlan.RoutedProvisionalBuildLimitedCampaigns,
                HardSeedTrainingCampaigns =
                    hardSeedPlan.Seeds.Count,
                HardSeedTrainingVictories =
                    hardSeedTrainingVictories,
                HardSeedEncounterCampaigns =
                    trainingSchedule.Count(slot =>
                        slot.HardSeed
                        && slot.FailureEncounterCheckpoint != null),
                HardSeedCounterfactualCampaigns =
                    hardSeedCounterfactualCampaigns,
                HardSeedCounterfactualVictories =
                    hardSeedCounterfactualVictories,
                HardSeedCounterfactualImprovements =
                    hardSeedCounterfactualImprovements,
                HardSeedCounterfactualRejected =
                    hardSeedCounterfactualRejected,
                AdvancedLocalCurriculumAttempts =
                    advancedLocalCurriculumAttempts,
                AdvancedLocalCurriculumSuccesses =
                    advancedLocalCurriculumSuccesses,
                EffectiveHardSeedReplayShare =
                    effectiveHardSeedReplayShare,
                HardSeedClusters =
                    new Dictionary<string, int>(
                        hardSeedPlan.Clusters,
                        StringComparer.Ordinal),
                HardSeedSourceCategories =
                    new Dictionary<string, int>(
                        hardSeedPlan.SourceCategories,
                        StringComparer.Ordinal),
                AdvancedTrainingCampaigns =
                    trainingSchedule.Count(slot => string.Equals(
                        slot.DifficultyId,
                        "advanced",
                        StringComparison.Ordinal)),
                EffectiveMinimumAdvancedReplayShare =
                    effectiveMinimumAdvancedReplayShare,
                CurriculumStage = curriculumPlan.Stage,
                NormalWilsonLowerBound =
                    curriculumPlan.NormalWilsonLowerBound,
                AdvancedWilsonLowerBound =
                    curriculumPlan.AdvancedWilsonLowerBound,
                SelfPlayExplorationProbability =
                    effectiveExplorationProbability,
                TeacherStudentPoolSourceFrames =
                    teacherStudentPool.SourceFrames,
                TeacherStudentPoolAvailableSourceFrames =
                    teacherStudentPool.AvailableSourceFrames,
                TeacherStudentPoolSelectedFrames =
                    teacherStudentPool.SelectedFrames,
                TeacherStudentPoolDroppedFrames =
                    teacherStudentPool.DroppedFrames,
                TeacherStudentPoolUnsafeEndTurnFrames =
                    teacherStudentPool.UnsafeEndTurnFrames,
                TeacherStudentPoolSourcePriorityMean =
                    teacherStudentPool.SourcePriorityMean,
                TeacherStudentPoolSelectedPriorityMean =
                    teacherStudentPool.SelectedPriorityMean,
                TeacherStudentPoolHighPriorityFrames =
                    teacherStudentPool.SelectedHighPriorityFrames,
                TeacherStudentPoolStrategyQuotaActive =
                    teacherStudentPool.StrategyQuotaActive,
                TeacherStudentPoolStrategyQuotaPassed =
                    teacherStudentPool.StrategyQuotaPassed,
                TeacherStudentPoolStrategyFrames =
                    new Dictionary<string, int>(
                        teacherStudentPool.StrategyFrames,
                        StringComparer.Ordinal),
                TeacherStudentPoolAvailableStrategyFrames =
                    new Dictionary<string, int>(
                        teacherStudentPool.AvailableStrategyFrames,
                        StringComparer.Ordinal),
                TeacherStudentPoolSourceStrategyFrames =
                    new Dictionary<string, int>(
                        teacherStudentPool.SourceStrategyFrames,
                        StringComparer.Ordinal),
                TeacherStudentPoolQuotaShortfalls =
                    new Dictionary<string, int>(
                        teacherStudentPool.StrategyQuotaShortfalls,
                        StringComparer.Ordinal),
                StrategyQuotaRepairAttempted =
                    teacherStudentPool.StrategyQuotaRepairAttempted,
                StrategyQuotaRepairSourceEpisodes =
                    teacherStudentPool.StrategyQuotaRepairSourceEpisodes,
                StrategyQuotaRepairAddedEpisodes =
                    teacherStudentPool.StrategyQuotaRepairAddedEpisodes,
                StrategyQuotaCollectionCampaigns =
                    strategyQuotaCollectionCampaigns,
                StrategyQuotaCollectionEpisodes =
                    strategyQuotaCollectionEpisodes,
                StrategyQuotaCollectionDifficultyCampaigns =
                    strategyQuotaYieldProfiles.ToDictionary(
                        item => item.Key,
                        item => item.Value.Campaigns,
                        StringComparer.Ordinal),
                StrategyQuotaCollectionYieldFrames =
                    strategyQuotaYieldProfiles
                        .SelectMany(item => item.Value.StrategyFrames.Select(
                            strategy => new
                            {
                                Key = item.Key + ":" + strategy.Key,
                                strategy.Value
                            }))
                        .ToDictionary(
                            item => item.Key,
                            item => item.Value,
                            StringComparer.Ordinal),
                ModelFrameStrata =
                    new Dictionary<string, int>(
                        trained.FrameStrata,
                        StringComparer.Ordinal),
                ModelEncodedStrategyFrames =
                    new Dictionary<string, int>(
                        trained.EncodedStrategyFrames,
                        StringComparer.Ordinal),
                InferenceHealth = inferenceHealth,
                ModelMinimumFrameWeight =
                    trained.MinimumFrameWeight,
                ModelMaximumFrameWeight =
                    trained.MaximumFrameWeight,
                ModelDroppedFramesByEpisodeCap =
                    trained.DroppedFramesByEpisodeCap,
                ModelTrainingFrameCount = trained.TrainingFrameCount,
                ModelDroppedUnsafeEndTurnFrames =
                    trained.DroppedUnsafeEndTurnFrames,
                ModelDroppedPolicyIntegrityFrames =
                    trained.DroppedPolicyIntegrityFrames,
                ModelEndTurnDecisionFrames =
                    trained.EndTurnDecisionFrames,
                ModelUnsafeEndTurnFrames =
                    trained.UnsafeEndTurnFrames,
                ModelUnsafeEndTurnPolicyFrames =
                    trained.UnsafeEndTurnPolicyFrames,
                ModelUnsafeEndTurnRiskAuxiliaryFrames =
                    trained.UnsafeEndTurnRiskAuxiliaryFrames,
                ModelBaselineValidationMetrics =
                    trained.BaselineValidationMetrics,
                ModelMeanPolicyTargetMaximum =
                    trained.MeanPolicyTargetMaximum,
                ModelTrainingMetrics = trained.TrainingMetrics,
                ModelValidationMetrics = trained.ValidationMetrics,
                ModelSelectionAnchorMetrics = selectionAnchorMetrics,
                TransformerTeacher = transformerTeacherReport,
                ModelTestMetrics = trained.TestMetrics,
                ModelEpochHistory = trained.EpochHistory
                    .Select(item => CloneEpochMetrics(
                        item,
                        iterationNumber))
                    .ToList(),
                CandidateModelId = trained.Model.ModelId,
                TuningSelectedEpoch = selectedTuningEpoch,
                TuningSelectedScore = selectedTuningScore,
                TuningCandidateCount = tuning.CandidateCount,
                TuningOfflineRejectedCandidates =
                    tuning.OfflineRejectedCandidates,
                TuningAllCandidatesRejectedOffline =
                    tuning.AllCandidatesRejectedOffline,
                TuningEvaluationRan = tuning.EvaluationRan,
                TuningInvalidCampaigns = tuning.InvalidCampaigns,
                TuningFinalistCount = tuning.FinalistCount,
                TuningCampaignsExecuted = tuning.CampaignsExecuted,
                TuningCampaignsSaved = tuning.CampaignsSaved,
                ChampionArenaScore = championScore,
                CandidateArenaScore = candidateScore,
                ChampionNormalWinRate = championNormal,
                CandidateNormalWinRate = candidateNormal,
                ChampionAdvancedWinRate = championAdvanced,
                CandidateAdvancedWinRate = candidateAdvanced,
                InvalidCandidateCampaigns = invalidCandidate,
                InvalidChampionCampaigns = invalidChampion,
                ArenaRetryAttempts =
                    result.ArenaRetryAttempts - arenaRetryAttemptsBefore,
                ArenaRecoveredCampaigns =
                    result.ArenaRecoveredCampaigns
                    - arenaRecoveredCampaignsBefore,
                ArenaIsolatedPairs =
                    result.ArenaIsolatedPairs - arenaIsolatedPairsBefore,
                ArenaReplacementPairs =
                    result.ArenaReplacementPairs
                    - arenaReplacementPairsBefore,
                ArenaInvalidSignatures =
                    result.ArenaInvalidSignatures
                        .Where(item =>
                            item.Value
                            > (arenaInvalidSignaturesBefore.TryGetValue(
                                item.Key,
                                out var before)
                                ? before
                                : 0))
                        .ToDictionary(
                            item => item.Key,
                            item => item.Value
                                    - (arenaInvalidSignaturesBefore.TryGetValue(
                                        item.Key,
                                        out var before)
                                        ? before
                                        : 0),
                            StringComparer.Ordinal),
                ValidArenaPairs = validPairIndexes.Count,
                ArenaScreeningPairs =
                    screeningPairsExecutedPerDifficulty * 2,
                ArenaScreeningPairsSaved = ArenaScreeningPairsSaved(
                    arenaPerDifficulty,
                    screeningPairsActuallyExecuted),
                ArenaScreeningDiagnosticOnly =
                    arenaScreeningDiagnosticOnly,
                ArenaScreeningStoppedEarly =
                    screeningStoppedEarly
                    || screeningPairsExecutedPerDifficulty
                       < arenaPerDifficulty,
                ArenaConfirmationPairs = confirmationRan
                    ? confirmationPairsExecutedPerDifficulty * 2
                    : 0,
                ArenaConfirmationStoppedEarly = confirmationStoppedEarly,
                ArenaConfirmationAcceptedEarly = confirmationAcceptedEarly,
                ArenaConfirmationPairsSaved = confirmationRan
                    ? Math.Max(
                        0,
                        (scheduledArenaConfirmationPerDifficulty
                         - confirmationPairsExecutedPerDifficulty) * 2)
                    : 0,
                ArenaHardSeedsMined = arenaHardSeedsMined,
                ValidNormalArenaPairs = validNormalPairs,
                ValidAdvancedArenaPairs = validAdvancedPairs,
                CandidateOnlyWins = candidateOnlyWins,
                ChampionOnlyWins = championOnlyWins,
                PairedWinWilsonLowerBound =
                    pairedWinWilsonLowerBound,
                ArenaDiscordantPairs = discordantPairs,
                ArenaEvidenceGatePassed = arenaEvidenceGatePassed,
                PairedEvidenceKind = pairedEvidenceKind,
                PairedRegressionWilsonUpperBound =
                    pairedRegressionWilsonUpperBound,
                NonInferiorityGatePassed = nonInferiorityGatePassed,
                AbsoluteNormalGatePassed = absoluteNormalGatePassed,
                AbsoluteAdvancedGatePassed =
                    absoluteAdvancedGatePassed,
                AbsoluteQualificationGatePassed =
                    absoluteQualificationGatePassed,
                OfflineHeadRegressionGatePassed =
                    offlineHeadRegressionGatePassed,
                StrategyQuotaGatePassed = strategyQuotaGatePassed,
                StateFeatureCollisionRate = stateFeatureCollisionRate,
                ActionFeatureCollisionRate = actionFeatureCollisionRate,
                FeatureCollisionGatePassed = featureCollisionGatePassed,
                CandidateScoreGain =
                    candidateScore - championScore,
                CandidateDepthGain =
                    candidateAverageDepth - championAverageDepth,
                IterativeGainKind = bootstrapPromotion
                    ? "bootstrap"
                    : meaningfulWinGain
                        ? "paired-win"
                        : meaningfulProgressGain
                            ? "score-depth"
                            : "none",
                PromotionProtocolVersion =
                    CombatFoundationPromotionProtocol.Version,
                ChampionAverageCompletedBattles = championAverageDepth,
                CandidateAverageCompletedBattles = candidateAverageDepth,
                Promoted = promoted,
                ProvisionalChampionSelected = provisionalChampionSelected,
                CurriculumCheckpointAccepted = curriculumCheckpoint,
                WorkingCheckpointAccepted = workingCheckpoint,
                WorkingModelAccepted = workingWindowAccepted,
                PromotionKind = promoted
                    ? "formal-champion"
                    : provisionalChampionSelected
                        ? "provisional-champion"
                        : absoluteQualificationGatePassed
                            ? "absolute-only-diagnostic"
                            : workingWindowAccepted
                                ? "working-window"
                                : curriculumCheckpoint
                                    ? "checkpoint-only"
                                    : "rejected",
                PromotionReason = absoluteQualificationGatePassed
                    ? "absolute-qualified"
                    : promotionReason,
                ConsecutiveRejectedIterations = workingWindowAccepted
                                                || absoluteQualificationGatePassed
                    ? 0
                    : ConsecutiveRejectedIterations(
                          result.Iterations,
                          stagnationAttemptStartIndex) + 1
            };
            completedIteration.ParetoProgress = ParetoFrontierProgress(
                completedIteration,
                result.Iterations);
            completedIteration.BehavioralProductiveProgressReasons =
                ProductiveProgressReasons(
                        completedIteration,
                        result.Iterations)
                    .ToList();
            completedIteration.BehavioralProductiveProgress =
                completedIteration.BehavioralProductiveProgressReasons.Count
                > 0;
            completedIteration.ProductiveProgressReasons =
                new List<string>(
                    completedIteration.BehavioralProductiveProgressReasons);
            completedIteration.ProductiveProgress =
                completedIteration.BehavioralProductiveProgress;
            completedIteration.DataPipelineProgressReasons =
                DataPipelineProgressReasons(
                        completedIteration,
                        result.Iterations)
                    .ToList();
            completedIteration.DataPipelineProgress =
                completedIteration.DataPipelineProgressReasons.Count > 0;
            completedIteration.ConsecutiveUnproductiveIterations =
                completedIteration.TrainingOnlyIteration
                    ? ConsecutiveUnproductiveIterations(
                        result.Iterations,
                        stagnationAttemptStartIndex)
                : completedIteration.BehavioralProductiveProgress
                || completedIteration.WorkingModelAccepted
                || completedIteration.AbsoluteQualificationGatePassed
                    ? 0
                    : ConsecutiveUnproductiveIterations(
                          result.Iterations,
                          stagnationAttemptStartIndex) + 1;
            completedIteration.ConsecutiveDataOnlyIterations =
                completedIteration.TrainingOnlyIteration
                    ? ConsecutiveDataOnlyIterations(
                        result.Iterations,
                        stagnationAttemptStartIndex)
                : completedIteration.DataPipelineProgress
                && !completedIteration.BehavioralProductiveProgress
                && !completedIteration.WorkingModelAccepted
                && !completedIteration.AbsoluteQualificationGatePassed
                    ? ConsecutiveDataOnlyIterations(
                          result.Iterations,
                          stagnationAttemptStartIndex) + 1
                    : 0;
            if (completedIteration.TrainingOnlyIteration)
            {
                completedIteration.ConsecutiveRejectedIterations =
                    ConsecutiveRejectedIterations(
                        result.Iterations,
                        stagnationAttemptStartIndex);
                completedIteration.WorkingModelBankSlot =
                    "training-continuation";
            }
            var updatedWorkingSlots = completedIteration.TrainingOnlyIteration
                ? Array.Empty<string>()
                : workingModelBank.AddCandidate(
                    trained.Model,
                    completedIteration);
            if (updatedWorkingSlots.Count > 0)
            {
                completedIteration.WorkingModelBankSlot = string.Join(
                    ",",
                    updatedWorkingSlots);
            }
            var persistedQualifiedBest = workingModelBank.QualifiedBest;
            if (persistedQualifiedBest != null)
            {
                result.AbsoluteQualifiedBestModel =
                    persistedQualifiedBest.Model;
                result.AbsoluteQualifiedBestEvidence =
                    persistedQualifiedBest.Evidence;
            }
            result.Iterations.Add(completedIteration);
            if (systemicArenaFailure
                || invalidCandidate > 0
                || invalidChampion > 0)
            {
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
                result.Message =
                    "竞技场阶段出现无效战役（基准 "
                    + invalidChampion
                    + "，候选 "
                    + invalidCandidate
                    + "）；本轮未计分、未晋级，训练已停止。失败定位："
                    + FormatArenaFailureSummary(
                        result.ArenaFailures.Where(item =>
                            item.Iteration == iterationNumber),
                        4);
                telemetry.ApplyTo(result);
                FinalizeCaseAnalysis(result);
                return result;
            }
            latestTrainingModel = freshlyTrainedModel;
            result.LatestTrainingModel = latestTrainingModel;
            if (arenaEvaluationRan && workingWindowAccepted)
            {
                workingChampion = trained.Model;
            }
            if (arenaEvaluationRan)
            {
                result.BestPendingArenaCandidate = null;
                workingChampion = workingModelBank.Select(
                    CombatFoundationPromotionProtocol.AbsoluteQualifiedBest,
                    workingModelBank.Select(
                        PreferredWorkingModelSlot(curriculumPlan.Stage),
                        workingChampion));
            }
            result.WorkingChampion = workingChampion;
            if (promoted || provisionalChampionSelected)
            {
                championModel = CreateParallelPolicyValueModel(
                    trained.Model,
                    request,
                    parallelism);
                result.Champion = trained.Model;
                result.AcceptanceKind = promoted
                    ? CombatFoundationPromotionProtocol.SignificantImprovement
                    : CombatFoundationPromotionProtocol.EquivalentNonInferior;
            }
            var latestIteration = result.Iterations.Last();
            result.ConsecutiveRejectedIterations =
                latestIteration.ConsecutiveRejectedIterations;
            result.ConsecutiveUnproductiveIterations =
                latestIteration.ConsecutiveUnproductiveIterations;
            result.ConsecutiveDataOnlyIterations =
                latestIteration.ConsecutiveDataOnlyIterations;
            if (inferenceHealth.RevalidationRequired
                && string.Equals(
                    executionPlan.Profile,
                    CombatFoundationExecutionProfileNames.Auto,
                    StringComparison.Ordinal))
            {
                RecordInferenceHealthFailure(
                    autoTune,
                    inferenceHealth,
                    parallelism,
                    DateTime.UtcNow);
                configuredInferenceMode =
                    CombatFoundationExecutionProfileNames.DirectInference;
                configuredInferenceParallelism = parallelism;
                configuredInferenceLaneCount = parallelism;
                configuredInferenceBatchSize = 1;
                ApplyEffectiveExecutionPlan(
                    executionPlan,
                    request,
                    parallelism,
                    configuredInferenceMode,
                    configuredInferenceParallelism,
                    configuredThreadPoolMinimumWorkerThreads,
                    configuredCheckpointSerializationParallelism,
                    configuredInferenceLaneCount,
                    configuredInferenceBatchSize);
                result.AutoTune = autoTune;
                request.AutoTuneCache = autoTune;
                result.InferenceExecutionMode = request.InferenceExecutionMode;
                result.InferenceParallelism = request.InferenceParallelism;
                result.InferenceLaneCount = request.InferenceLaneCount;
                result.InferenceBatchSizePerLane = request.InferenceBatchSize;
                request.AutoTuneCompleted?.Invoke(autoTune);
            }
            else if (string.Equals(
                         executionPlan.Profile,
                         CombatFoundationExecutionProfileNames.Auto,
                         StringComparison.Ordinal)
                     && autoTune.InferenceCalibrated
                     && RecordInferenceHealthSuccess(autoTune, inferenceHealth))
            {
                // A prior failure count is cleared only after the selected
                // plan has survived a complete production health window.
                result.AutoTune = autoTune;
                request.AutoTuneCache = autoTune;
                request.AutoTuneCompleted?.Invoke(autoTune);
            }
            var stagnationStop = ShouldStopForStagnation(
                request,
                result.Iterations,
                workingChampion != null,
                stagnationAttemptStartIndex);
            latestIteration.StagnationStopTriggered = stagnationStop;
            PublishCheckpoint(
                request,
                CreateResumeState(
                    "iteration-complete",
                    iteration + 1,
                    completedCampaigns,
                    result,
                    telemetry,
                    workingChampion,
                    modelTraining: null));
            resume = null;
            if (stagnationStop)
            {
                result.StoppedForStagnation = true;
                result.IterationStopReason =
                    CombatFoundationStagnationProtocol.Version
                    + ": consecutive unproductive candidates="
                    + result.ConsecutiveUnproductiveIterations
                    + ", rejected candidates="
                    + result.ConsecutiveRejectedIterations;
                break;
            }
        }

        var nextIteration = result.Iterations.LastOrDefault()?.Iteration
                            ?? startIteration;
        result.NextIteration = nextIteration;
        if (!result.StoppedForStagnation
            && nextIteration >= iterationInvocationLimit
            && nextIteration < iterations)
        {
            result.ContinuationRequired = true;
            result.Success = true;
            result.AcceptancePassed = false;
            result.Message = "第 "
                             + nextIteration
                             + " 轮已完成并持久化；正在通过独立进程继续第 "
                             + (nextIteration + 1)
                             + "/"
                             + iterations
                             + " 轮。";
            result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
            telemetry.ApplyTo(result);
            FinalizeCaseAnalysis(result);
            return result;
        }

        var qualifiedBest = workingModelBank.QualifiedBest;
        result.QualifiedCandidateCount = result.Iterations.Count(item =>
            item.AbsoluteQualificationGatePassed
            && (item.NonInferiorityGatePassed
                || !item.HadIncumbentModel));
        if (qualifiedBest != null
            && !result.Iterations.Any(item =>
                item.AbsoluteQualificationGatePassed
                && (item.NonInferiorityGatePassed
                    || !item.HadIncumbentModel)
                && string.Equals(
                    item.CandidateModelId,
                    qualifiedBest.Model.ModelId,
                    StringComparison.Ordinal)))
        {
            result.QualifiedCandidateCount++;
        }
        foreach (var item in result.Iterations)
        {
            item.QualifiedCandidateSelected = false;
        }
        if (qualifiedBest != null)
        {
            result.AbsoluteQualifiedBestModel = qualifiedBest.Model;
            result.AbsoluteQualifiedBestEvidence = qualifiedBest.Evidence;
            result.Champion = qualifiedBest.Model;
            result.WorkingChampion = qualifiedBest.Model;
            workingChampion = qualifiedBest.Model;
            result.AcceptanceKind =
                CombatFoundationPromotionProtocol.AbsoluteQualifiedBest;
            result.SelectedQualifiedCandidateIteration =
                qualifiedBest.Evidence.Iteration;
            result.SelectedQualifiedCandidateModelId =
                qualifiedBest.Model.ModelId;
            var canonicalSelectedEvidence = result.Iterations.FirstOrDefault(
                item => item.Iteration == qualifiedBest.Evidence.Iteration
                        && string.Equals(
                            item.CandidateModelId,
                            qualifiedBest.Model.ModelId,
                            StringComparison.Ordinal));
            (canonicalSelectedEvidence ?? qualifiedBest.Evidence)
                .QualifiedCandidateSelected = true;
            championModel = CreateParallelPolicyValueModel(
                qualifiedBest.Model,
                request,
                parallelism);
        }

        var evaluationModel = qualifiedBest?.Model
                              ?? result.BestPendingArenaCandidate?.Model
                              ?? result.LatestTrainingModel
                              ?? result.WorkingChampion
                              ?? result.Champion;
        if (evaluationModel == null)
        {
            result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
            result.Message = "训练未产出可供诊断的模型；无法执行隔离验证。";
            telemetry.ApplyTo(result);
            FinalizeCaseAnalysis(result);
            return result;
        }
        result.EvaluatedModelId = evaluationModel.ModelId;
        result.EvaluatedModelIteration = result.Iterations
            .Where(item => string.Equals(
                item.CandidateModelId,
                evaluationModel.ModelId,
                StringComparison.Ordinal))
            .Select(item => item.Iteration)
            .LastOrDefault();
        result.EvaluatedModelDeploymentQualified = qualifiedBest != null;
        if (qualifiedBest == null)
        {
            result.AcceptanceKind = "diagnostic-unqualified-candidate";
            championModel = CreateParallelPolicyValueModel(
                evaluationModel,
                request,
                parallelism);
        }

        if (string.IsNullOrWhiteSpace(result.AcceptanceKind))
        {
            result.AcceptanceKind = "retained-champion";
        }

        if (capabilityProbeCampaigns > 0)
        {
            result.CapabilityProbe = RunCapabilityProbe(
                request,
                ruleset,
                evaluationModel,
                telemetry,
                capabilityProbeCampaigns,
                governance.CapabilityProbeTeacherCampaignsPerDifficulty,
                seedPlan.ValidationSeedStart,
                parallelism,
                ref completedCampaigns,
                totalCampaigns,
                cancellationToken);
        }

        PublishCheckpoint(
            request,
            CreateResumeState(
                "validation",
                iterations,
                completedCampaigns,
                result,
                telemetry,
                workingChampion,
                modelTraining: null));
        telemetry.BeginPhase("validation");
        var earlyStopReason = "";
        var validationVoluntaryEndTurns = 0;
        var validationEmptyEndTurns = 0;
        var validationEndTurnsWithUnusedEnergy = 0;
        var validationUnusedEnergyAtEndTurns = 0;
        var validationAvoidableEndTurnsWithUnusedEnergy = 0;
        var validationAvoidableUnusedEnergyAtEndTurns = 0;
        var validationSaturatedEndTurnsWithUnusedEnergy = 0;
        var validationSevereEndTurnMistakes = 0;
        var validationDominatedEndTurns = 0;
        var validationEndTurnsIntoAvoidableLethal = 0;
        var validationEndTurnsWithCertifiedCycle = 0;
        var validationEndTurnsWithUnknownLifecycle = 0;
        var validationEndTurnsWithBankedSurplus = 0;
        var validationBankedSurplusAtEndTurns = 0;
        var validationMaximumConsecutiveNoProgressTurns = 0;
        var validationNoEffectActionAttempts = 0;
        var validationRepeatedNoEffectActionAttempts = 0;
        var validationGuaranteedNoEffectActionAttempts = 0;
        var validationInteractiveActionContractFailures = 0;
        var validationSeedPlan = CombatFoundationValidationSeedSampler.Create(
            result.RunSeed,
            seedPlan.ValidationSeedStart,
            normalValidationCampaigns,
            advancedValidationCampaigns);
        foreach (var difficulty in new[] { "normal", "advanced" })
        {
            if (!string.IsNullOrWhiteSpace(earlyStopReason))
            {
                break;
            }
            var validationCount = difficulty == "normal"
                ? normalValidationCampaigns
                : advancedValidationCampaigns;
            var difficultySeeds = string.Equals(
                difficulty,
                "normal",
                StringComparison.Ordinal)
                ? validationSeedPlan.NormalWorldSeeds
                : validationSeedPlan.AdvancedWorldSeeds;
            var difficultyRuns = RunRollingValidation(
                validationCount,
                parallelism,
                validationEarlyStopBatchSize,
                cancellationToken,
                index =>
                {
                    var validationRun = RunCampaign(
                        request.ValidationCampaign,
                        difficulty,
                        difficultySeeds[index],
                        ruleset,
                        new CombatDecisionSimulationPolicyFactory(
                            deploymentProfile,
                            policyValueModel: championModel),
                        telemetry,
                        "validation:" + difficulty,
                        cancellationToken);
                    ReportProgress(
                        request,
                        telemetry,
                        validationRun,
                        ref completedCampaigns,
                        totalCampaigns,
                        "最终隔离验证：" + difficulty);
                    return validationRun;
                },
                (observedCount, victories, hardFailureObserved) =>
                {
                    if (!request.EnableEarlyValidationStop)
                    {
                        return false;
                    }
                    if (hardFailureObserved)
                    {
                        earlyStopReason =
                            "隔离验收检测到严重结束回合失误、无效果动作"
                            + "或交互动作契约失败；这些指标必须为 0";
                        return true;
                    }
                    var bestPossibleVictories =
                        victories + validationCount - observedCount;
                    var acceptanceRate = string.Equals(
                        difficulty,
                        "normal",
                        StringComparison.Ordinal)
                        ? normalAcceptanceRate
                        : advancedAcceptanceRate;
                    var confidenceThreshold = EffectiveWilsonThreshold(
                        validationCount,
                        acceptanceRate);
                    var bestPossibleWilson =
                        CombatFoundationCurriculum.WilsonLowerBound(
                            bestPossibleVictories,
                            validationCount);
                    if (bestPossibleWilson >= confidenceThreshold)
                    {
                        return false;
                    }
                    earlyStopReason = string.Equals(
                        difficulty,
                        "normal",
                        StringComparison.Ordinal)
                        ? "普通难度剩余样本不足以达到 "
                          + normalAcceptanceRate.ToString("P0")
                          + " 验收线"
                        : "高级难度剩余样本不足以达到 "
                          + advancedAcceptanceRate.ToString("P0")
                          + " 验收线";
                    return true;
                },
                (index, campaign) =>
                {
                    RecordCase(
                        result,
                        campaign,
                        "validation",
                        iterations,
                        "champion",
                        ruleset.RulesetHash,
                        request.DecisionProfile,
                        evaluationModel.ModelId,
                        episodes: null,
                        request: request);
                    result.TerminalConsistencyViolations +=
                        CountTerminalConsistencyViolations(campaign);
                    foreach (var battle in campaign.Battles)
                    {
                        validationVoluntaryEndTurns +=
                            battle.Metrics.VoluntaryEndTurns;
                        validationEmptyEndTurns +=
                            battle.Metrics.EmptyEndTurns;
                        validationEndTurnsWithUnusedEnergy +=
                            battle.Metrics.EndTurnsWithUnusedEnergy;
                        validationUnusedEnergyAtEndTurns +=
                            battle.Metrics.UnusedEnergyAtEndTurns;
                        validationAvoidableEndTurnsWithUnusedEnergy +=
                            battle.Metrics
                                .AvoidableEndTurnsWithUnusedEnergy;
                        validationAvoidableUnusedEnergyAtEndTurns +=
                            battle.Metrics
                                .AvoidableUnusedEnergyAtEndTurns;
                        validationSaturatedEndTurnsWithUnusedEnergy +=
                            battle.Metrics
                                .SaturatedEndTurnsWithUnusedEnergy;
                        validationSevereEndTurnMistakes +=
                            battle.Metrics.SevereEndTurnMistakes;
                        validationDominatedEndTurns +=
                            battle.Metrics.DominatedEndTurns;
                        validationEndTurnsIntoAvoidableLethal +=
                            battle.Metrics.EndTurnsIntoAvoidableLethal;
                        validationEndTurnsWithCertifiedCycle +=
                            battle.Metrics.EndTurnsWithCertifiedCycle;
                        validationEndTurnsWithUnknownLifecycle +=
                            battle.Metrics.EndTurnsWithUnknownLifecycle;
                        validationEndTurnsWithBankedSurplus +=
                            battle.Metrics.EndTurnsWithBankedSurplus;
                        validationBankedSurplusAtEndTurns +=
                            battle.Metrics.BankedSurplusAtEndTurns;
                        validationMaximumConsecutiveNoProgressTurns = Math.Max(
                            validationMaximumConsecutiveNoProgressTurns,
                            battle.Metrics.MaximumConsecutiveNoProgressTurns);
                        validationNoEffectActionAttempts +=
                            battle.Metrics.NoEffectActionAttempts;
                        validationRepeatedNoEffectActionAttempts +=
                            battle.Metrics.RepeatedNoEffectActionAttempts;
                        validationGuaranteedNoEffectActionAttempts +=
                            battle.Metrics.GuaranteedNoEffectActionAttempts;
                        validationInteractiveActionContractFailures +=
                            battle.Metrics.InteractiveActionContractFailures;
                    }
                    return request.RetainValidationRunDetails
                        ? campaign
                        : CompactValidationRun(campaign);
                });
            result.ValidationRuns.AddRange(difficultyRuns);
        }

        var normalRuns = result.ValidationRuns.Where(item =>
            string.Equals(item.DifficultyId, "normal", StringComparison.Ordinal)).ToList();
        var advancedRuns = result.ValidationRuns.Where(item =>
            string.Equals(item.DifficultyId, "advanced", StringComparison.Ordinal)).ToList();
        result.Validation = new CombatCampaignFoundationValidation
        {
            SampleProtocol = CombatFoundationValidationSeedSampler.Version,
            RandomSampling = true,
            SamplePlanHash = validationSeedPlan.PlanHash,
            NormalWorldSeeds = new List<ulong>(
                validationSeedPlan.NormalWorldSeeds),
            AdvancedWorldSeeds = new List<ulong>(
                validationSeedPlan.AdvancedWorldSeeds),
            CampaignsPerDifficulty = normalValidationCampaigns == advancedValidationCampaigns
                ? normalValidationCampaigns
                : 0,
            NormalPlannedCampaigns = normalValidationCampaigns,
            AdvancedPlannedCampaigns = advancedValidationCampaigns,
            NormalCampaigns = normalRuns.Count,
            AdvancedCampaigns = advancedRuns.Count,
            NormalStatus = normalRuns.Count == 0
                ? "not-run"
                : "executed",
            AdvancedStatus = advancedRuns.Count == 0
                ? "not-run"
                : "executed",
            NormalVictories = normalRuns.Count(item => item.FinalBossVictory),
            AdvancedVictories = advancedRuns.Count(item => item.FinalBossVictory),
            RequiredNormalVictories = requiredNormalVictories,
            RequiredAdvancedVictories = requiredAdvancedVictories,
            RequiredNormalWinRate = normalAcceptanceRate,
            RequiredAdvancedWinRate = advancedAcceptanceRate,
            InvalidCampaigns = result.ValidationRuns.Count(item => item.Invalid),
            NormalWinRate = normalRuns.Count == 0
                ? 0d
                : normalRuns.Count(item => item.FinalBossVictory) / (double)normalRuns.Count,
            AdvancedWinRate = advancedRuns.Count == 0
                ? 0d
                : advancedRuns.Count(item => item.FinalBossVictory) / (double)advancedRuns.Count,
            NormalWilsonLowerBound =
                CombatFoundationCurriculum.WilsonLowerBound(
                    normalRuns.Count(item =>
                        !item.Invalid && item.FinalBossVictory),
                    normalRuns.Count),
            AdvancedWilsonLowerBound =
                CombatFoundationCurriculum.WilsonLowerBound(
                    advancedRuns.Count(item =>
                        !item.Invalid && item.FinalBossVictory),
                    advancedRuns.Count),
            VoluntaryEndTurns = validationVoluntaryEndTurns,
            EmptyEndTurns = validationEmptyEndTurns,
            EndTurnsWithUnusedEnergy = validationEndTurnsWithUnusedEnergy,
            UnusedEnergyAtEndTurns = validationUnusedEnergyAtEndTurns,
            AvoidableEndTurnsWithUnusedEnergy =
                validationAvoidableEndTurnsWithUnusedEnergy,
            AvoidableUnusedEnergyAtEndTurns =
                validationAvoidableUnusedEnergyAtEndTurns,
            SaturatedEndTurnsWithUnusedEnergy =
                validationSaturatedEndTurnsWithUnusedEnergy,
            SevereEndTurnMistakes = validationSevereEndTurnMistakes,
            DominatedEndTurns = validationDominatedEndTurns,
            EndTurnsIntoAvoidableLethal =
                validationEndTurnsIntoAvoidableLethal,
            EndTurnsWithCertifiedCycle =
                validationEndTurnsWithCertifiedCycle,
            EndTurnsWithUnknownLifecycle =
                validationEndTurnsWithUnknownLifecycle,
            EndTurnsWithBankedSurplus =
                validationEndTurnsWithBankedSurplus,
            BankedSurplusAtEndTurns =
                validationBankedSurplusAtEndTurns,
            MaximumConsecutiveNoProgressTurns =
                validationMaximumConsecutiveNoProgressTurns,
            NoEffectActionAttempts = validationNoEffectActionAttempts,
            RepeatedNoEffectActionAttempts =
                validationRepeatedNoEffectActionAttempts,
            GuaranteedNoEffectActionAttempts =
                validationGuaranteedNoEffectActionAttempts,
            InteractiveActionContractFailures =
                validationInteractiveActionContractFailures,
            BehaviorPassed = validationSevereEndTurnMistakes == 0
                             && validationDominatedEndTurns == 0
                             && validationEndTurnsIntoAvoidableLethal == 0
                             && validationEndTurnsWithCertifiedCycle == 0
                             && validationAvoidableEndTurnsWithUnusedEnergy
                             == 0
                             && validationNoEffectActionAttempts == 0
                             && validationRepeatedNoEffectActionAttempts == 0
                             && validationGuaranteedNoEffectActionAttempts == 0
                             && validationInteractiveActionContractFailures == 0,
            EarlyStopped = !string.IsNullOrWhiteSpace(earlyStopReason),
            EarlyStopReason = earlyStopReason
        };
        result.Validation.Passed = result.Validation.InvalidCampaigns == 0
                                   && result.Validation.BehaviorPassed
                                   && result.TerminalConsistencyViolations == 0
                                   && result.FeatureLeakageViolations == 0
                                   && result.Validation.NormalCampaigns
                                   == normalValidationCampaigns
                                   && result.Validation.AdvancedCampaigns
                                   == advancedValidationCampaigns
                                   && result.Validation.NormalWilsonLowerBound
                                   >= EffectiveWilsonThreshold(
                                       normalValidationCampaigns,
                                       normalAcceptanceRate)
                                   && result.Validation.AdvancedWilsonLowerBound
                                   >= EffectiveWilsonThreshold(
                                       advancedValidationCampaigns,
                                       advancedAcceptanceRate);
        var capabilityGatePassed =
            !request.RequireCapabilityProbeBaselineGain
            || capabilityProbeCampaigns <= 0
            || result.CapabilityProbe.PassedBaselineGate;
        result.AcceptancePassed = result.Validation.Passed
                                  && qualifiedBest != null
                                  && capabilityGatePassed;
        if (result.AcceptancePassed)
        {
            var acceptedIteration = result.Iterations.FirstOrDefault(item =>
                item.QualifiedCandidateSelected
                || item.Iteration == result.SelectedQualifiedCandidateIteration
                && string.Equals(
                    item.CandidateModelId,
                    result.SelectedQualifiedCandidateModelId,
                    StringComparison.Ordinal));
            if (acceptedIteration != null)
            {
                acceptedIteration.CandidateQualificationState =
                    CombatFoundationPromotionProtocol.Accepted;
            }
        }
        result.Success = true;
        result.GeneratedReplayEpisodes = Math.Max(
            result.GeneratedReplayEpisodes,
            result.Replay.Count);
        var persistedReplay = CombatFoundationReplaySampler.Select(
            result.Replay,
            Math.Min(1024, foundationTrainingOptions.ReplayEpisodeLimit),
            request.EnableStratifiedReplay,
            new CombatFoundationReplayBalanceOptions
            {
                MinimumAdvancedShare =
                    request.MinimumAdvancedReplayShare,
                MinimumAdvancedDefeatShare =
                    request.MinimumAdvancedDefeatReplayShare,
                EnablePrioritySampling =
                    request.EnablePrioritizedReplay,
                AllowCrossDifficultyBackfill = false
            });
        result.Replay = persistedReplay.Episodes;
        result.PersistedReplayEpisodes = result.Replay.Count;
        result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
        result.EarlyStopReason = earlyStopReason;
        result.Message = result.AcceptancePassed
            ? "底模通过隔离验收：普通 "
              + result.Validation.NormalVictories
              + "/"
              + normalValidationCampaigns
              + "，高级 "
              + result.Validation.AdvancedVictories
              + "/"
              + advancedValidationCampaigns
            : "底模尚未达到隔离验收线：普通 "
              + result.Validation.NormalVictories
              + "/"
              + result.Validation.NormalCampaigns
              + "（已执行；计划 "
              + normalValidationCampaigns
              + "，要求至少 "
              + requiredNormalVictories
              + "）"
              + "，高级 "
              + result.Validation.AdvancedVictories
              + "/"
              + result.Validation.AdvancedCampaigns
              + "（已执行；计划 "
              + advancedValidationCampaigns
              + "，要求至少 "
              + requiredAdvancedVictories
              + "）"
              + (string.IsNullOrWhiteSpace(earlyStopReason)
                  ? ""
                  : "；已提前结束验证：" + earlyStopReason)
              + (qualifiedBest == null
                  ? "；模型未通过竞技场部署资格门禁，本轮结果仅作为诊断候选"
                  : "")
              + (!capabilityGatePassed
                  ? "；能力探针未通过："
                    + result.CapabilityProbe.BaselineGateReason
                  : "");
        telemetry.ApplyTo(result);
        FinalizeCaseAnalysis(result);
        return result;
    }

    internal static List<CombatCampaignResult> RunRollingValidation(
        int requestedCampaigns,
        int parallelism,
        int decisionInterval,
        CancellationToken cancellationToken,
        Func<int, CombatCampaignResult> run,
        Func<int, int, bool, bool> shouldStop,
        Func<int, CombatCampaignResult, CombatCampaignResult> complete)
    {
        var count = Math.Max(0, requestedCampaigns);
        var observedCount = 0;
        var observedVictories = 0;
        var hardFailureObserved = false;
        var scheduled = CombatFoundationWorkScheduler.RunOrdered(
            count,
            parallelism,
            decisionInterval,
            cancellationToken,
            run,
            (index, campaign) =>
            {
                observedCount++;
                if (!campaign.Invalid && campaign.FinalBossVictory)
                {
                    observedVictories++;
                }
                hardFailureObserved |= HasHardValidationFailure(campaign);
                return complete(index, campaign);
            },
            _ => shouldStop(
                observedCount,
                observedVictories,
                hardFailureObserved));
        return scheduled.Items;
    }

    private static bool HasHardValidationFailure(CombatCampaignResult campaign)
    {
        return (campaign.Battles ?? new List<CombatSimulationResult>()).Any(
            battle => battle.Metrics.SevereEndTurnMistakes > 0
                      || battle.Metrics.DominatedEndTurns > 0
                      || battle.Metrics.EndTurnsIntoAvoidableLethal > 0
                      || battle.Metrics.EndTurnsWithCertifiedCycle > 0
                      || battle.Metrics.AvoidableEndTurnsWithUnusedEnergy > 0
                      || battle.Metrics.NoEffectActionAttempts > 0
                      || battle.Metrics.RepeatedNoEffectActionAttempts > 0
                      || battle.Metrics.GuaranteedNoEffectActionAttempts > 0
                      || battle.Metrics.InteractiveActionContractFailures > 0);
    }

    private static CombatCampaignResult CompactValidationRun(
        CombatCampaignResult source)
    {
        return new CombatCampaignResult
        {
            CampaignId = source.CampaignId,
            CampaignVersion = source.CampaignVersion,
            DifficultyId = source.DifficultyId,
            WorldSeed = source.WorldSeed,
            RoleId = source.RoleId,
            PartnerId = source.PartnerId,
            GameParameterPresetId = source.GameParameterPresetId,
            GameParameterHash = source.GameParameterHash,
            SkillCardIds = new List<string>(
                source.SkillCardIds ?? new List<string>()),
            FamiliarBlessingIds =
                new List<string>(
                    source.FamiliarBlessingIds ?? new List<string>()),
            EnabledRewardCardPackIds =
                new List<string>(
                    source.EnabledRewardCardPackIds ?? new List<string>()),
            PlanHash = source.PlanHash,
            PolicyId = source.PolicyId,
            ReachedFinalBoss = source.ReachedFinalBoss,
            FinalBossVictory = source.FinalBossVictory,
            CampaignVictory = source.CampaignVictory,
            Invalid = source.Invalid,
            CompletedBattles = source.CompletedBattles,
            TotalBattles = source.TotalBattles,
            BattleSemanticCoverage = source.BattleSemanticCoverage,
            ProgressionSemanticCoverage =
                source.ProgressionSemanticCoverage,
            UnsupportedDefinitions =
                new List<string>(
                    source.UnsupportedDefinitions ?? new List<string>())
        };
    }

    private static void RecordCase(
        CombatCampaignFoundationTrainingResult result,
        CombatCampaignResult campaign,
        string sourceStage,
        int iteration,
        string competitor,
        string rulesetHash,
        string decisionProfile,
        string modelId,
        IReadOnlyList<CombatEpisode>? episodes,
        CombatCampaignFoundationTrainingRequest request)
    {
        for (var battleIndex = 0;
             battleIndex < campaign.Battles.Count - 1;
             battleIndex++)
        {
            // Turn summaries, metrics and terminal state remain available for
            // longitudinal analysis. Keeping only the terminal battle's full
            // event stream prevents the durable case library from retaining
            // every low-level event from thousands of campaigns in memory.
            campaign.Battles[battleIndex].Events.Clear();
        }
        var campaignDefinition = string.Equals(
            sourceStage,
            "validation",
            StringComparison.OrdinalIgnoreCase)
            ? request.ValidationCampaign
            : request.TrainingCampaign;
        var observation = CombatFoundationCaseLearning.Observe(
            campaign,
            sourceStage,
            iteration,
            competitor,
            rulesetHash,
            CampaignFingerprint(campaignDefinition),
            request.NativeProgramPackageHash,
            request.TrainingPolicyVersion,
            decisionProfile,
            modelId,
            episodes);
        if (!string.IsNullOrWhiteSpace(
                request.CaseArchiveCompatibilityKey))
        {
            observation.CompatibilityKey =
                request.CaseArchiveCompatibilityKey;
        }
        result.CampaignObservations.Add(observation);
        request.ObservationRecorded?.Invoke(observation);
        if (observation.ArchiveEligible
            && episodes != null
            && episodes.Count > 0)
        {
            var successCase = CombatFoundationCaseLearning.CreateSuccessCase(
                campaign,
                observation,
                episodes);
            var consumed = false;
            if (request.SuccessCaseSink != null)
            {
                try
                {
                    consumed = request.SuccessCaseSink(successCase);
                }
                catch
                {
                    // Keep the case for the worker's final archive fallback.
                    consumed = false;
                }
            }
            if (!consumed)
            {
                result.SuccessCases.Add(successCase);
            }
            request.SuccessCaseRecorded?.Invoke(successCase);
        }
    }

    private static void FinalizeCaseAnalysis(
        CombatCampaignFoundationTrainingResult result)
    {
        result.CaseAnalysis = CombatFoundationCaseLearning.Analyze(
            result.CampaignObservations);
    }

    internal static bool ResumeCompatible(
        CombatCampaignFoundationResumeState resume)
    {
        if (!ModelCompatible(resume.Champion)
            || !ModelCompatible(resume.WorkingChampion)
            || !ModelCompatible(resume.LatestTrainingModel)
            || !ModelCompatible(resume.BestPendingArenaCandidate?.Model)
            || !ModelCompatible(resume.AbsoluteQualifiedBestModel)
            || !ModelCompatible(resume.ModelTraining?.Model)
            || !ModelCompatible(resume.ModelTraining?.BestModel))
        {
            return false;
        }
        if (resume.BestPendingArenaCandidate != null
            && !PendingArenaCandidateEligible(
                resume.BestPendingArenaCandidate))
        {
            return false;
        }
        if (resume.AbsoluteQualifiedBestModel != null
            && (resume.AbsoluteQualifiedBestEvidence == null
                || !resume.AbsoluteQualifiedBestEvidence
                    .AbsoluteQualificationGatePassed
                || !string.Equals(
                    resume.AbsoluteQualifiedBestEvidence.CandidateModelId,
                    resume.AbsoluteQualifiedBestModel.ModelId,
                    StringComparison.Ordinal)))
        {
            return false;
        }
        return (resume.Replay ?? new List<CombatEpisode>()).All(episode =>
            episode != null
            && episode.ModelProtocol
            == CombatPolicyValueProtocol.EpisodeProtocol
            && episode.FeatureSchemaVersion
            == CombatPolicyValueProtocol.FeatureSchemaVersion);
    }

    internal static bool ManifestCompatible(
        CombatFoundationCompatibilityManifest? checkpoint,
        CombatFoundationCompatibilityManifest current)
    {
        return checkpoint != null
               && checkpoint.SchemaVersion == current.SchemaVersion
               && checkpoint.FeatureSchemaVersion
               == current.FeatureSchemaVersion
               && checkpoint.StateDimensions == current.StateDimensions
               && checkpoint.ActionDimensions == current.ActionDimensions
               && checkpoint.HiddenDimensions == current.HiddenDimensions
               && string.Equals(
                   checkpoint.RulesetHash,
                   current.RulesetHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.ContentSetHash,
                   current.ContentSetHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.OwnerModSetHash,
                   current.OwnerModSetHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.ActionContractVersion,
                   current.ActionContractVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.SemanticGateVersion,
                   current.SemanticGateVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.IntegritySeedCorpusVersion,
                   current.IntegritySeedCorpusVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.CampaignId,
                   current.CampaignId,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.CampaignVersion,
                   current.CampaignVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.TrainingCampaignHash,
                   current.TrainingCampaignHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.ValidationCampaignHash,
                   current.ValidationCampaignHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.FeatureEncodingMode,
                   current.FeatureEncodingMode,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.SearchPolicyVersion,
                   current.SearchPolicyVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.CurriculumVersion,
                   current.CurriculumVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.TrainingPolicyVersion,
                   current.TrainingPolicyVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   checkpoint.TrainingSemanticsVersion,
                   current.TrainingSemanticsVersion,
                   StringComparison.Ordinal);
    }

    internal static string CampaignFingerprint(
        CombatCampaignDefinition campaign)
    {
        var canonical = new StringBuilder(16_384);
        AppendCanonical(canonical, campaign, 0);
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var value in Encoding.UTF8.GetBytes(canonical.ToString()))
        {
            hash ^= value;
            hash *= prime;
        }
        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static void AppendCanonical(
        StringBuilder target,
        object? value,
        int depth)
    {
        if (value == null)
        {
            target.Append("null;");
            return;
        }
        if (depth > 32)
        {
            throw new InvalidOperationException(
                "Campaign fingerprint exceeded the supported object depth.");
        }
        if (value is string text)
        {
            target.Append('"').Append(text.Length).Append(':')
                .Append(text).Append("\";");
            return;
        }
        var type = value.GetType();
        if (type.IsEnum)
        {
            target.Append(type.FullName).Append(':')
                .Append(Convert.ToInt64(value, CultureInfo.InvariantCulture))
                .Append(';');
            return;
        }
        if (value is bool boolean)
        {
            target.Append(boolean ? "true;" : "false;");
            return;
        }
        if (value is IFormattable formattable
            && (type.IsPrimitive || value is decimal))
        {
            target.Append(formattable.ToString(null, CultureInfo.InvariantCulture))
                .Append(';');
            return;
        }
        if (value is IDictionary dictionary)
        {
            target.Append('{');
            var entries = dictionary.Keys
                .Cast<object>()
                .OrderBy(key => Convert.ToString(
                    key,
                    CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ToList();
            foreach (var key in entries)
            {
                AppendCanonical(target, key, depth + 1);
                AppendCanonical(target, dictionary[key], depth + 1);
            }
            target.Append("};");
            return;
        }
        if (value is IEnumerable enumerable)
        {
            target.Append('[');
            foreach (var item in enumerable)
            {
                AppendCanonical(target, item, depth + 1);
            }
            target.Append("];");
            return;
        }
        target.Append(type.FullName).Append('{');
        foreach (var property in type.GetProperties(
                     BindingFlags.Instance | BindingFlags.Public)
                 .Where(property =>
                     property.CanRead
                     && property.GetIndexParameters().Length == 0)
                 .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            target.Append(property.Name).Append('=');
            AppendCanonical(
                target,
                CampaignCompatibilityValue(value, property),
                depth + 1);
        }
        target.Append("};");
    }

    private static object? CampaignCompatibilityValue(
        object owner,
        PropertyInfo property)
    {
        if (owner is not CombatCampaignDefinition)
        {
            return property.GetValue(owner, null);
        }
        // Reward residuals are learned, run-local policy data.  Normalize them
        // to the original empty campaign defaults so applying an archive does
        // not change structural training identity and existing empty-residual
        // manifests remain byte-for-byte compatible.
        if (string.Equals(
                property.Name,
                nameof(CombatCampaignDefinition.RewardScoreResiduals),
                StringComparison.Ordinal)
            || string.Equals(
                property.Name,
                nameof(CombatCampaignDefinition
                    .RewardScoreConditionalResiduals),
                StringComparison.Ordinal))
        {
            return new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
        }
        if (string.Equals(
                property.Name,
                nameof(CombatCampaignDefinition
                    .RewardScoreResidualMaximumAbsolute),
                StringComparison.Ordinal))
        {
            return 0.20d;
        }
        return property.GetValue(owner, null);
    }

    private static bool ModelCompatible(
        CombatPolicyValueNetworkDefinition? model)
    {
        return model == null
               || CombatPolicyValueNetworkValidator.TryValidate(
                   model,
                   out _);
    }

    private static CombatCampaignFoundationResumeState CreateResumeState(
        string stage,
        int nextIteration,
        int completedCampaigns,
        CombatCampaignFoundationTrainingResult result,
        FoundationTelemetryTracker telemetry,
        CombatPolicyValueNetworkDefinition? workingChampion,
        CombatPolicyValueTrainingResumeState? modelTraining,
        IReadOnlyList<CombatFoundationTrainingSlot>? trainingSchedule = null)
    {
        return new CombatCampaignFoundationResumeState
        {
            Stage = stage,
            NextIteration = Math.Max(0, nextIteration),
            CompletedCampaigns = Math.Max(0, completedCampaigns),
            GeneratedReplayEpisodes =
                result.GeneratedReplayEpisodes,
            RunSeed = result.RunSeed,
            TrainingSeedStart = result.TrainingSeedStart,
            ArenaSeedStart = result.ArenaSeedStart,
            TuningSeedStart = result.TuningSeedStart,
            ValidationSeedStart = result.ValidationSeedStart,
            ModelRandomSeed = result.ModelRandomSeed,
            Champion = result.Champion,
            WorkingChampion = workingChampion,
            LatestTrainingModel = result.LatestTrainingModel,
            BestPendingArenaCandidate =
                result.BestPendingArenaCandidate,
            AbsoluteQualifiedBestModel =
                result.AbsoluteQualifiedBestModel,
            AbsoluteQualifiedBestEvidence =
                result.AbsoluteQualifiedBestEvidence,
            Replay = new List<CombatEpisode>(result.Replay),
            Iterations = new List<CombatCampaignFoundationIteration>(
                result.Iterations),
            Preflight = result.Preflight,
            ModelTraining = modelTraining,
            Telemetry = telemetry.Current(stage),
            HardSeedHistory =
                new List<CombatFoundationHardSeedHistoryEntry>(
                    result.HardSeedHistory),
            TrainingSchedule = new List<CombatFoundationTrainingSlot>(
                trainingSchedule
                ?? Array.Empty<CombatFoundationTrainingSlot>()),
            ArenaReplacementCursor = result.ArenaReplacementPairs,
            Compatibility = result.Compatibility
        };
    }

    private static void PublishCheckpoint(
        CombatCampaignFoundationTrainingRequest request,
        CombatCampaignFoundationResumeState checkpoint)
    {
        request.Checkpoint?.Invoke(checkpoint);
    }

    private static void ReportProgress(
        CombatCampaignFoundationTrainingRequest request,
        FoundationTelemetryTracker telemetry,
        CombatCampaignResult campaign,
        ref int completedCampaigns,
        int totalCampaigns,
        string message)
    {
        var current = Interlocked.Increment(ref completedCampaigns);
        request.Progress?.Invoke(current, totalCampaigns, message);
        telemetry.CampaignCompleted(current, campaign, message);
    }

    private CombatCampaignResult RunCampaign(
        CombatCampaignDefinition campaign,
        string difficulty,
        ulong seed,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory factory,
        FoundationTelemetryTracker telemetry,
        string stage,
        CancellationToken cancellationToken,
        Action<CombatCampaignCheckpoint>? encounterStart = null,
        CombatCampaignWorldPlan? preparedPlan = null)
    {
        var campaignWorkId = telemetry.EnterCampaign(stage);
        CombatCampaignResult? result = null;
        try
        {
            var plan = preparedPlan
                       ?? CombatCampaignWorldPlanner.Build(
                           campaign,
                           difficulty,
                           seed);
            result = encounterStart == null
                ? campaignRunner.RunMonitored(
                    campaign,
                    plan,
                    ruleset,
                    factory,
                    (depth, battle) =>
                        telemetry.BattleCompleted(
                            campaignWorkId,
                            depth,
                            battle,
                            stage),
                    cancellationToken)
                : campaignRunner.RunMonitoredWithEncounterStarts(
                    campaign,
                    plan,
                    ruleset,
                    factory,
                    (depth, battle) =>
                        telemetry.BattleCompleted(
                            campaignWorkId,
                            depth,
                            battle,
                            stage),
                    encounterStart,
                    cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Foundation campaign failed. stage="
                + stage
                + ", difficulty="
                + difficulty
                + ", seed="
                + seed,
                ex);
        }
        finally
        {
            telemetry.ExitCampaign(campaignWorkId, result, stage);
        }
    }

    private CombatCampaignResult RunCampaignSegment(
        CombatCampaignDefinition campaign,
        string difficulty,
        ulong seed,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory factory,
        CombatCampaignCheckpoint checkpoint,
        FoundationTelemetryTracker telemetry,
        string stage,
        CancellationToken cancellationToken)
    {
        var campaignWorkId = telemetry.EnterCampaign(stage);
        CombatCampaignResult? result = null;
        try
        {
            result = campaignRunner.RunMonitoredSegment(
                campaign,
                CombatCampaignWorldPlanner.Build(campaign, difficulty, seed),
                ruleset,
                factory,
                checkpoint,
                1,
                (depth, battle) =>
                    telemetry.BattleCompleted(
                        campaignWorkId,
                        checkpoint.NextEncounterIndex + depth,
                        battle,
                        stage),
                cancellationToken);
            return result;
        }
        finally
        {
            telemetry.ExitCampaign(campaignWorkId, result, stage);
        }
    }

    private static CombatCampaignCheckpoint CompactEncounterCheckpoint(
        CombatCampaignCheckpoint source)
    {
        return new CombatCampaignCheckpoint
        {
            CampaignId = source.CampaignId,
            CampaignVersion = source.CampaignVersion,
            DifficultyId = source.DifficultyId,
            WorldSeed = source.WorldSeed,
            PlanHash = source.PlanHash,
            PolicyId = source.PolicyId,
            NextEncounterIndex = source.NextEncounterIndex,
            State = CloneCampaignState(source.State),
            Completed = false
        };
    }

    internal static IReadOnlyList<LocalCurriculumCheckpoint>
        BuildLocalCurriculumCheckpoints(CombatCampaignCheckpoint source)
    {
        var curriculumBand = LocalCurriculumBand(source);
        var checkpoints = new List<LocalCurriculumCheckpoint>
        {
            new()
            {
                Checkpoint = CloneEncounterCheckpoint(source),
                HpFloorPercent = 0,
                CurriculumBand = curriculumBand,
                Repaired = false
            }
        };
        if (!string.Equals(
                source.DifficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(curriculumBand)
            || source.State.MaxHp <= 0)
        {
            return checkpoints;
        }
        var seenHp = new HashSet<int> { source.State.CurrentHp };
        var hpFloors = string.Equals(
                curriculumBand,
                "finale",
                StringComparison.Ordinal)
            ? new[] { 85, 100 }
            : string.Equals(
                curriculumBand,
                "late",
                StringComparison.Ordinal)
                ? new[] { 75, 90, 100 }
                : new[] { 65, 85, 100 };
        foreach (var hpFloorPercent in hpFloors)
        {
            var targetHp = Math.Max(
                source.State.CurrentHp,
                (int)Math.Ceiling(
                    source.State.MaxHp * hpFloorPercent / 100d));
            targetHp = Math.Min(source.State.MaxHp, targetHp);
            if (!seenHp.Add(targetHp))
            {
                continue;
            }
            var repaired = CloneEncounterCheckpoint(source);
            repaired.State.CurrentHp = targetHp;
            checkpoints.Add(new LocalCurriculumCheckpoint
            {
                Checkpoint = repaired,
                HpFloorPercent = hpFloorPercent,
                CurriculumBand = curriculumBand,
                Repaired = true
            });
        }
        return checkpoints;
    }

    private static string LocalCurriculumBand(CombatCampaignCheckpoint source)
    {
        // These are campaign-position bands, not encounter identities.  They
        // therefore work for every content provider that follows the shared
        // seven-layer campaign contract.
        if (source.NextEncounterIndex >= 36 || source.State.CurrentLayer >= 7)
        {
            return "finale";
        }
        if (source.NextEncounterIndex >= 30 || source.State.CurrentLayer >= 6)
        {
            return "late";
        }
        return source.NextEncounterIndex is >= 2 and <= 5
            ? "early-build-check"
            : "";
    }

    internal static int PolicyTeacherFreshnessAge(
        int iterationNumber,
        CombatTransformerTeacherReport report,
        IReadOnlyList<CombatCampaignFoundationIteration>? iterations)
    {
        if (report == null
            || !report.Requested
            || report.PolicyTeacherApplied)
        {
            return 0;
        }
        var lastAppliedIteration = (iterations
                                    ?? Array.Empty<
                                        CombatCampaignFoundationIteration>())
            .Where(item => item?.TransformerTeacher?.PolicyTeacherApplied
                           == true)
            .Select(item => item.Iteration)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(
            1,
            Math.Max(1, iterationNumber) - lastAppliedIteration);
    }

    internal static DistillationWeightDecision
        EffectiveTransformerDistillationWeight(
            CombatTransformerTeacherReport report,
            double configuredWeight,
            CombatPolicyValueNetworkDefinition? workingChampion,
            IReadOnlyList<CombatCampaignFoundationIteration>? iterations,
            int minimumTrainingFrames = 1024)
    {
        _ = workingChampion;
        _ = iterations;
        _ = minimumTrainingFrames;
        var requested = double.IsNaN(configuredWeight)
                        || double.IsInfinity(configuredWeight)
            ? 0d
            : Math.Max(0d, Math.Min(0.75d, configuredWeight));
        if (report == null || !report.Applied)
        {
            return new DistillationWeightDecision
            {
                Guarded = requested > 0d,
                Reason = requested > 0d
                    ? "configured fixed distillation disabled because no qualified teacher is applied"
                    : "fixed distillation disabled by configuration"
            };
        }
        return new DistillationWeightDecision
        {
            Weight = requested,
            Guarded = false,
            Reason = "fixed trainer-configured distillation weight"
        };
    }

    private static int StudentLossRegressionStreak(
        IReadOnlyList<CombatCampaignFoundationIteration>? iterations)
    {
        var history = (iterations
                       ?? Array.Empty<CombatCampaignFoundationIteration>())
            .Where(item => MetricAvailable(item.ModelValidationMetrics)
                           && MetricAvailable(item.ModelTestMetrics))
            .ToList();
        var streak = 0;
        for (var index = history.Count - 1; index > 0; index--)
        {
            var current = history[index];
            var baseline = history[index - 1];
            if (!LossRegressed(
                    baseline.ModelValidationMetrics.CompositeLoss,
                    current.ModelValidationMetrics.CompositeLoss)
                && !LossRegressed(
                    baseline.ModelTestMetrics.CompositeLoss,
                    current.ModelTestMetrics.CompositeLoss))
            {
                break;
            }
            streak++;
        }
        return streak;
    }

    private static bool MetricAvailable(CombatPolicyValueMetricSnapshot metric)
    {
        return metric != null
               && metric.FrameCount > 0
               && metric.CompositeLoss > 0d
               && !double.IsNaN(metric.CompositeLoss)
               && !double.IsInfinity(metric.CompositeLoss);
    }

    private static bool LossRegressed(double baseline, double current)
    {
        return baseline > 0d
               && current > baseline
                  * (1d + CombatFoundationPromotionProtocol
                      .DefaultMaximumOfflineHeadRegression);
    }

    private static CombatCampaignCheckpoint CloneEncounterCheckpoint(
        CombatCampaignCheckpoint source)
    {
        return new CombatCampaignCheckpoint
        {
            CampaignId = source.CampaignId,
            CampaignVersion = source.CampaignVersion,
            DifficultyId = source.DifficultyId,
            WorldSeed = source.WorldSeed,
            PlanHash = source.PlanHash,
            PolicyId = source.PolicyId,
            NextEncounterIndex = source.NextEncounterIndex,
            State = CloneCampaignState(source.State),
            Completed = false
        };
    }

    private static CombatCampaignState CloneCampaignState(
        CombatCampaignState source)
    {
        return new CombatCampaignState
        {
            WorldSeed = source.WorldSeed,
            DifficultyId = source.DifficultyId,
            CurrentLayer = source.CurrentLayer,
            CurrentGameLevel = source.CurrentGameLevel,
            MaxHp = source.MaxHp,
            CurrentHp = source.CurrentHp,
            Money = source.Money,
            Attributes = new Dictionary<string, int>(
                source.Attributes,
                StringComparer.OrdinalIgnoreCase),
            LayerBaseAttributes = new Dictionary<string, int>(
                source.LayerBaseAttributes,
                StringComparer.OrdinalIgnoreCase),
            PermanentAttributeBonuses = new Dictionary<string, int>(
                source.PermanentAttributeBonuses,
                StringComparer.OrdinalIgnoreCase),
            AttributeUpperBounds = new Dictionary<string, int>(
                source.AttributeUpperBounds,
                StringComparer.OrdinalIgnoreCase),
            Deck = new List<string>(source.Deck),
            ReserveCards = new List<string>(source.ReserveCards),
            Relics = new List<string>(source.Relics),
            Blessings = new List<string>(source.Blessings),
            InnateBlessings = new List<string>(source.InnateBlessings),
            RewardVariables = source.RewardVariables.ToDictionary(
                item => item.Key,
                item => new Dictionary<string, string>(
                    item.Value,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            SpecialVariables = new Dictionary<string, string>(
                source.SpecialVariables,
                StringComparer.OrdinalIgnoreCase),
            UnsupportedProgressionRules =
                new List<string>(source.UnsupportedProgressionRules),
            BuildPlan = source.BuildPlan.Clone()
        };
    }

    private static double CounterfactualCurriculumScore(
        CombatCampaignResult campaign)
    {
        var battle = campaign.Battles.LastOrDefault();
        if (battle == null)
        {
            return double.NegativeInfinity;
        }
        return (battle.Outcome == CombatSimulationOutcome.Victory
                   ? 1_000_000d
                   : 0d)
               + Math.Max(0, battle.Metrics?.DamageDealt ?? 0) * 100d
               + Math.Max(0, battle.Turns);
    }

    private static CombatPolicyValueInput[] BuildInferenceCalibrationInputs(
        IReadOnlyList<CombatEpisode>? replay,
        int maximumInputs)
    {
        var samples = new List<InferenceCalibrationInputSample>();
        foreach (var episode in replay ?? Array.Empty<CombatEpisode>())
        {
            if (episode == null) continue;
            foreach (var frame in episode.Frames
                         ?? new List<CombatEpisodeFrame>())
            {
                if (frame == null) continue;
                var legalCandidates = (frame.Candidates
                                       ?? new List<CombatEpisodeCandidate>())
                    .Count(candidate => candidate != null && candidate.Legal);
                if (legalCandidates <= 0) continue;
                samples.Add(new InferenceCalibrationInputSample
                {
                    Key = (episode.JourneyRunId ?? "") + "|"
                          + (episode.EpisodeId ?? "") + "|"
                          + frame.ActionSequence + "|"
                          + (frame.StateFingerprint ?? ""),
                    CandidateBucket = legalCandidates <= 2
                        ? 0
                        : legalCandidates <= 4
                            ? 1
                            : legalCandidates <= 8
                                ? 2
                                : 3,
                    Frame = frame
                });
            }
        }

        var buckets = samples
            .GroupBy(sample => sample.CandidateBucket)
            .OrderBy(group => group.Key)
            .Select(group => new Queue<InferenceCalibrationInputSample>(
                group.OrderBy(sample => StableModelSelectionHash(sample.Key))
                    .ThenBy(sample => sample.Key, StringComparer.Ordinal)))
            .ToList();
        var selected = new List<InferenceCalibrationInputSample>();
        var limit = Math.Max(1, maximumInputs);
        while (selected.Count < limit && buckets.Any(bucket => bucket.Count > 0))
        {
            foreach (var bucket in buckets)
            {
                if (selected.Count >= limit) break;
                if (bucket.Count > 0) selected.Add(bucket.Dequeue());
            }
        }
        return selected.Select(sample => new CombatPolicyValueInput
        {
            StateFeatures = sample.Frame.StateFeatures,
            Candidates = (sample.Frame.Candidates
                          ?? new List<CombatEpisodeCandidate>())
                .Where(candidate => candidate != null && candidate.Legal)
                .Select(candidate => new CombatPolicyValueCandidate
                {
                    CandidateId = candidate.CandidateId,
                    SourceId = candidate.SourceId,
                    Features = candidate.Features
                })
                .ToList()
        }).ToArray();
    }

    private CombatFoundationAutoTuneResult CalibrateInferenceExecution(
        CombatCampaignFoundationTrainingRequest request,
        CombatPolicyValueNetworkDefinition definition,
        IReadOnlyList<CombatEpisode> replay,
        CombatFoundationAutoTuneResult autoTune,
        int parallelism,
        FoundationTelemetryTracker telemetry,
        CancellationToken cancellationToken)
    {
        var inputs = BuildInferenceCalibrationInputs(
            replay,
            Math.Max(8, Math.Min(32, request.AutoTuneSampleCampaigns)));
        if (inputs.Length == 0)
        {
            autoTune.InferenceCalibrated = false;
            return autoTune;
        }

        telemetry.BeginPhase("inference-auto-tune");
        var candidates = new List<InferenceExecutionCandidate>
        {
            new()
            {
                Mode = CombatFoundationExecutionProfileNames.DirectInference,
                LaneCount = parallelism,
                BatchSize = 1
            }
        };
        foreach (var laneCount in new[] { 1, 2, 4, 8 }
                      .Where(value => value <= Math.Max(1, parallelism / 2)))
        {
            foreach (var batchSize in new[] { 2, 4, 8 }
                         .Where(value => value * laneCount <= parallelism))
            {
                candidates.Add(new InferenceExecutionCandidate
                {
                    Mode = CombatFoundationExecutionProfileNames
                        .ShardedBatchInference,
                    LaneCount = laneCount,
                    BatchSize = batchSize
                });
            }
        }

        var measurements = new List<CombatFoundationAutoTuneMeasurement>();
        // Inference execution is a property of the hardware, model family,
        // tensor shape and caller concurrency. Running complete campaigns for
        // every candidate repeats search, rewards and world simulation that do
        // not contribute to that decision. Exercise the same Evaluate path on
        // real replay inputs instead, with enough concurrent samples to expose
        // queue fill and timeout behavior.
        var sampleEvaluations = InferenceMicrobenchmarkSampleCount(
            request.AutoTuneSampleCampaigns,
            parallelism);
        var usefulUnitsPerEvaluation = inputs
            .Average(input => Math.Max(1, input.Candidates.Count));
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.BatchSize > 2)
            {
                var directMeasurement = measurements.FirstOrDefault(item =>
                    string.Equals(
                        item.InferenceMode,
                        CombatFoundationExecutionProfileNames.DirectInference,
                        StringComparison.Ordinal));
                var previousBatch = measurements.FirstOrDefault(item =>
                    string.Equals(
                        item.InferenceMode,
                        CombatFoundationExecutionProfileNames
                            .ShardedBatchInference,
                        StringComparison.Ordinal)
                    && item.InferenceLaneCount == candidate.LaneCount
                    && item.InferenceBatchSize == candidate.BatchSize / 2);
                if (!ShouldExpandInferenceCandidate(
                        previousBatch,
                        directMeasurement,
                        request.AutoTuneObjective))
                {
                    continue;
                }
            }
            ICombatPolicyValueModel model =
                new ManagedCombatPolicyValueModel(definition);
            if (string.Equals(
                    candidate.Mode,
                    CombatFoundationExecutionProfileNames.ShardedBatchInference,
                    StringComparison.Ordinal))
            {
                model = candidate.LaneCount == 1
                    ? new ConcurrentBatchedCombatPolicyValueModel(
                        model,
                        candidate.BatchSize)
                    : new ShardedBatchedCombatPolicyValueModel(
                        model,
                        candidate.LaneCount,
                        candidate.BatchSize);
            }
            var warmupEvaluations = Math.Max(
                parallelism,
                candidate.LaneCount * candidate.BatchSize);
            CombatFoundationWorkScheduler.For(
                warmupEvaluations,
                parallelism,
                cancellationToken,
                index => _ = model.Evaluate(inputs[index % inputs.Length]),
                progress: null);
            var diagnosticsStart = CombatPolicyValueBatchDiagnostics.Capture();
            using var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime.TotalSeconds;
            var allocationStart = ReadManagedAllocationCounter();
            var gen2Start = GC.CollectionCount(2);
            var latencies = new long[sampleEvaluations];
            var stopwatch = Stopwatch.StartNew();
            var completedEvaluations = 0;
            var waveParallelism = InferenceCalibrationWaveParallelism(
                parallelism);
            for (var wave = 0; wave < waveParallelism.Length; wave++)
            {
                var remaining = sampleEvaluations - completedEvaluations;
                var remainingWaves = waveParallelism.Length - wave;
                var waveEvaluations = remaining / remainingWaves;
                var offset = completedEvaluations;
                CombatFoundationWorkScheduler.For(
                    waveEvaluations,
                    waveParallelism[wave],
                    cancellationToken,
                    index =>
                    {
                        var globalIndex = offset + index;
                        var started = Stopwatch.GetTimestamp();
                        _ = model.Evaluate(
                            inputs[globalIndex % inputs.Length]);
                        latencies[globalIndex] =
                            Stopwatch.GetTimestamp() - started;
                    },
                    progress: null);
                completedEvaluations += waveEvaluations;
            }
            stopwatch.Stop();
            Array.Sort(latencies);
            var elapsed = Math.Max(0.000001d, stopwatch.Elapsed.TotalSeconds);
            var allocationRate = Math.Max(
                0d,
                (ReadManagedAllocationCounter() - allocationStart)
                / elapsed
                / (1024d * 1024d));
            var gen2Rate = Math.Max(
                0d,
                (GC.CollectionCount(2) - gen2Start) / elapsed);
            var usefulWork = sampleEvaluations
                             * usefulUnitsPerEvaluation
                             / elapsed;
            var diagnostics = CombatPolicyValueBatchDiagnostics
                .Capture()
                .DeltaFrom(diagnosticsStart);
            measurements.Add(new CombatFoundationAutoTuneMeasurement
            {
                MeasurementKind = CombatFoundationAutoTuneProtocol
                    .InferenceCalibrationKind,
                Parallelism = parallelism,
                InferenceMode = candidate.Mode,
                InferenceLaneCount = candidate.LaneCount,
                InferenceBatchSize = candidate.BatchSize,
                Campaigns = 0,
                Battles = 0,
                SearchSimulations = 0L,
                ElapsedSeconds = elapsed,
                CpuUtilizationPercent = Math.Max(
                    0d,
                    (process.TotalProcessorTime.TotalSeconds - cpuStart)
                    / elapsed
                    / Math.Max(1, Environment.ProcessorCount)
                    * 100d),
                AllocationMegabytesPerSecond = allocationRate,
                Gen2CollectionsPerSecond = gen2Rate,
                UsefulWorkPerSecond = usefulWork,
                EfficiencyScore = CombatFoundationAutoTuneSelector.Score(
                    usefulWork,
                    gen2Rate,
                    allocationRate),
                P95LatencyMicroseconds = latencies[
                    Math.Min(
                        latencies.Length - 1,
                        (int)Math.Ceiling(latencies.Length * 0.95d) - 1)]
                    * 1_000_000d
                    / Stopwatch.Frequency,
                AverageBatchFill = candidate.BatchSize <= 1
                    ? 1d
                    : Math.Max(
                        0d,
                        Math.Min(
                            1d,
                            diagnostics.AverageBatchSize
                            / candidate.BatchSize)),
                InferenceRequests = diagnostics.Requests,
                InferenceBatchEvaluations = diagnostics.BatchEvaluations,
                InferenceTimeoutFlushes = diagnostics.TimeoutFlushes,
                InvalidCampaigns = 0
            });
        }
        var selected = CombatFoundationAutoTuneSelector.SelectInference(
            measurements,
            request.AutoTuneThroughputTolerance,
            request.AutoTuneObjective);
        autoTune.Measurements ??= new List<CombatFoundationAutoTuneMeasurement>();
        autoTune.Measurements.RemoveAll(item =>
            (item.MeasurementKind ?? "").StartsWith(
                "inference",
                StringComparison.Ordinal));
        autoTune.Measurements.AddRange(measurements);
        if (selected == null)
        {
            return autoTune;
        }
        autoTune.InferenceCalibrated = true;
        autoTune.InferenceMeasuredUtc = DateTime.UtcNow;
        autoTune.InferenceCalibrationKind = CombatFoundationAutoTuneProtocol
            .InferenceCalibrationKind;
        autoTune.InferenceCalibrationSamples = sampleEvaluations;
        autoTune.InferenceFallbackActive = false;
        autoTune.InferenceRecalibrationNotBeforeUtc = default;
        autoTune.InferenceCacheKey = BuildInferenceAutoTuneCacheKey(
            request,
            definition,
            parallelism);
        autoTune.SelectedInferenceMode = selected.InferenceMode;
        autoTune.SelectedInferenceLaneCount = selected.InferenceLaneCount;
        autoTune.SelectedInferenceBatchSize = selected.InferenceBatchSize;
        request.InferenceExecutionMode = selected.InferenceMode;
        request.InferenceParallelism = parallelism;
        request.InferenceLaneCount = selected.InferenceLaneCount;
        request.InferenceBatchSize = selected.InferenceBatchSize;
        return autoTune;
    }

    internal static int InferenceMicrobenchmarkSampleCount(
        int configuredSamples,
        int parallelism)
    {
        var requested = Math.Max(1, configuredSamples) * 8;
        var concurrencyFloor = Math.Max(1, parallelism) * 4;
        return Math.Max(
            CombatFoundationAutoTuneProtocol.MinimumInferenceMicrobenchmarkSamples,
            Math.Min(
                CombatFoundationAutoTuneProtocol.MaximumInferenceMicrobenchmarkSamples,
                Math.Max(requested, concurrencyFloor)));
    }

    internal static int[] InferenceCalibrationWaveParallelism(int parallelism)
    {
        var maximum = Math.Max(1, parallelism);
        return new[]
        {
            1,
            Math.Max(1, maximum / 4),
            Math.Max(1, maximum / 2),
            maximum,
            Math.Max(1, maximum / 2),
            1,
            maximum,
            Math.Max(1, maximum / 4)
        };
    }

    internal static bool ShouldExpandInferenceCandidate(
        CombatFoundationAutoTuneMeasurement? previousBatch,
        CombatFoundationAutoTuneMeasurement? direct,
        string? objective)
    {
        if (previousBatch == null || direct == null)
        {
            return false;
        }
        var directScore = AutoTuneObjectiveScore(direct, objective);
        var previousScore = AutoTuneObjectiveScore(previousBatch, objective);
        var timeoutDenominator = previousBatch.InferenceBatchEvaluations > 0L
            ? previousBatch.InferenceBatchEvaluations
            : previousBatch.InferenceRequests;
        var timeoutRate = timeoutDenominator <= 0
            ? 0d
            : previousBatch.InferenceTimeoutFlushes
              / (double)timeoutDenominator;
        return previousBatch.InvalidCampaigns == 0
               && previousBatch.AverageBatchFill >= 0.35d
               && timeoutRate <= 0.80d
               && directScore > 0d
               && previousScore >= directScore * 0.95d;
    }

    internal static string BuildAutoTuneCacheKey(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset)
    {
        return string.Join(
            "|",
            CombatFoundationAutoTuneProtocol.Version,
            CombatFoundationAutoTuneProtocol.CampaignKernelVersion,
            request.AutoTuneHardwareKey ?? "",
            ruleset.Version ?? "",
            ruleset.CardCount.ToString(CultureInfo.InvariantCulture),
            ruleset.EnemyCount.ToString(CultureInfo.InvariantCulture),
            ruleset.StatusCount.ToString(CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(request.AutoTuneCampaignKey)
                ? CampaignFingerprint(request.TrainingCampaign)
                : request.AutoTuneCampaignKey,
            CombatPolicyValueProtocol.TrainingSemanticsVersion,
            request.DecisionProfile ?? "",
            request.AutoTuneObjective ?? "",
            request.MaximumDegreeOfParallelism.ToString(
                CultureInfo.InvariantCulture),
            request.Profile?.SearchBudgetMode ?? "",
            request.Profile?.SearchSimulationBudget.ToString(
                CultureInfo.InvariantCulture) ?? "",
            request.Profile?.SearchNodeBudget.ToString(
                CultureInfo.InvariantCulture) ?? "",
            request.Profile?.SearchMaxPly.ToString(
                CultureInfo.InvariantCulture) ?? "");
    }

    internal static string BuildInferenceAutoTuneCacheKey(
        CombatCampaignFoundationTrainingRequest request,
        CombatPolicyValueTrainingOptions options,
        int parallelism)
    {
        options ??= new CombatPolicyValueTrainingOptions();
        return BuildInferenceAutoTuneCacheKey(
            request,
            "aura.combat-policy-value.mlp.v2",
            2,
            CombatPolicyValueProtocol.FeatureSchemaVersion,
            options.StateDimensions,
            options.ActionDimensions,
            options.HiddenDimensions,
            options.ActionQuantileCount,
            options.ActionQuantileCount > 0,
            options.FeatureEncodingMode,
            parallelism);
    }

    internal static string BuildInferenceAutoTuneCacheKey(
        CombatCampaignFoundationTrainingRequest request,
        CombatPolicyValueNetworkDefinition definition,
        int parallelism)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        return BuildInferenceAutoTuneCacheKey(
            request,
            definition.ModelProtocol,
            definition.ProtocolVersion,
            definition.FeatureSchemaVersion,
            definition.StateDimensions,
            definition.ActionDimensions,
            definition.HiddenDimensions,
            definition.ActionQuantileCount,
            definition.ActionQuantileHeadReady,
            definition.FeatureEncodingMode,
            parallelism);
    }

    private static string BuildInferenceAutoTuneCacheKey(
        CombatCampaignFoundationTrainingRequest request,
        string? modelProtocol,
        int modelProtocolVersion,
        int featureSchemaVersion,
        int stateDimensions,
        int actionDimensions,
        int hiddenDimensions,
        int actionQuantileCount,
        bool actionQuantileHeadReady,
        string? featureEncodingMode,
        int parallelism)
    {
        var calibrationCampaignKey = string.IsNullOrWhiteSpace(
            request.AutoTuneCampaignKey)
            ? CampaignFingerprint(request.TrainingCampaign)
            : request.AutoTuneCampaignKey;
        var calibrationSampleCampaigns = Math.Max(
            4,
            Math.Min(64, request.AutoTuneSampleCampaigns));
        return string.Join(
            "|",
            CombatFoundationAutoTuneProtocol.Version,
            CombatFoundationAutoTuneProtocol.InferenceKernelVersion,
            request.AutoTuneHardwareKey ?? "",
            CombatFoundationAutoTuneObjectiveNames.Normalize(
                request.AutoTuneObjective),
            NormalizedAutoTuneThroughputTolerance(
                    request.AutoTuneThroughputTolerance)
                .ToString("R", CultureInfo.InvariantCulture),
            modelProtocol ?? "",
            Math.Max(1, modelProtocolVersion).ToString(
                CultureInfo.InvariantCulture),
            Math.Max(1, featureSchemaVersion).ToString(
                CultureInfo.InvariantCulture),
            calibrationCampaignKey,
            calibrationSampleCampaigns.ToString(CultureInfo.InvariantCulture),
            InferenceConcurrencyClass(parallelism).ToString(
                CultureInfo.InvariantCulture),
            Math.Max(1, parallelism).ToString(CultureInfo.InvariantCulture),
            Math.Max(1, stateDimensions).ToString(CultureInfo.InvariantCulture),
            Math.Max(1, actionDimensions).ToString(CultureInfo.InvariantCulture),
            Math.Max(1, hiddenDimensions).ToString(CultureInfo.InvariantCulture),
            Math.Max(1, actionQuantileCount).ToString(
                CultureInfo.InvariantCulture),
            actionQuantileHeadReady ? "quantile-ready" : "quantile-disabled",
            featureEncodingMode ?? "");
    }

    internal static int InferenceConcurrencyClass(int parallelism)
    {
        var value = Math.Max(1, parallelism);
        var upperBound = 1;
        while (upperBound < value
               && upperBound
                  < CombatFoundationParallelismProtocol.MaximumSupportedParallelism)
        {
            upperBound <<= 1;
        }
        return Math.Min(
            CombatFoundationParallelismProtocol.MaximumSupportedParallelism,
            upperBound);
    }

    private static double NormalizedAutoTuneThroughputTolerance(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0.02d
            : Math.Max(0d, Math.Min(0.20d, value));
    }

    private static void ApplyEffectiveExecutionPlan(
        CombatFoundationExecutionPlan target,
        CombatCampaignFoundationTrainingRequest request,
        int parallelism,
        string inferenceMode,
        int inferenceParallelism,
        int threadPoolMinimumWorkerThreads,
        int checkpointSerializationParallelism,
        int inferenceLaneCount,
        int inferenceBatchSize)
    {
        var effective = CombatFoundationExecutionProfiles.Resolve(
            CombatFoundationExecutionProfileNames.Custom,
            Math.Max(1, parallelism),
            inferenceMode,
            inferenceParallelism,
            threadPoolMinimumWorkerThreads,
            checkpointSerializationParallelism,
            null,
            inferenceLaneCount,
            inferenceBatchSize);
        target.CampaignParallelism = effective.CampaignParallelism;
        target.InferenceMode = effective.InferenceMode;
        target.InferenceParallelism = effective.InferenceParallelism;
        target.InferenceLaneCount = effective.InferenceLaneCount;
        target.InferenceBatchSize = effective.InferenceBatchSize;
        target.ThreadPoolMinimumWorkerThreads =
            effective.ThreadPoolMinimumWorkerThreads;
        target.CheckpointSerializationParallelism =
            effective.CheckpointSerializationParallelism;
        request.MaximumDegreeOfParallelism = target.CampaignParallelism;
        request.InferenceExecutionMode = target.InferenceMode;
        request.InferenceParallelism = target.InferenceParallelism;
        request.InferenceLaneCount = target.InferenceLaneCount;
        request.InferenceBatchSize = target.InferenceBatchSize;
        request.ThreadPoolMinimumWorkerThreads =
            target.ThreadPoolMinimumWorkerThreads;
        request.CheckpointSerializationParallelism =
            target.CheckpointSerializationParallelism;
    }

    private static CombatFoundationParallelismDecision
        PrepareParallelismDecision(
            CombatCampaignFoundationTrainingRequest request,
            int iteration,
            int maximumParallelism)
    {
        request.IterationResourceBarrier?.Invoke();
        var trim = CombatRiskAwareRootSamplingPuctPlanner
            .TrimRetainedSearchMemory();
        if (iteration > 1 || trim.ReleasedEstimatedBytes > 0L)
        {
            CompactManagedHeap();
        }
        var resources = CombatFoundationResourceSnapshot.Capture();
        return CombatFoundationParallelismPlanner.Select(
            iteration,
            maximumParallelism,
            resources,
            trim,
            request.ParallelismPerLaneBytes,
            request.ParallelismMemoryReserveBytes);
    }

    private static void CompactManagedHeap()
    {
        GCSettings.LargeObjectHeapCompactionMode =
            GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(
            2,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
    }

    private static bool AutoTuneCacheCompatible(
        CombatFoundationAutoTuneResult? cached,
        string cacheKey,
        int maximumParallelism)
    {
        return cached != null
               && string.Equals(
                   cached.Version,
                   CombatFoundationAutoTuneProtocol.Version,
                   StringComparison.Ordinal)
               && string.Equals(
                   string.IsNullOrWhiteSpace(cached.CampaignCacheKey)
                       ? cached.CacheKey
                       : cached.CampaignCacheKey,
                   cacheKey,
                   StringComparison.Ordinal)
               && !cached.LowConfidence
               && cached.SelectedParallelism > 0
               && cached.SelectedParallelism <= maximumParallelism
               && cached.MeasuredUtc >= DateTime.UtcNow.AddDays(-30d);
    }

    internal static string AutoTuneCacheMissReason(
        CombatFoundationAutoTuneResult? cached,
        string cacheKey,
        int maximumParallelism,
        string? hardwareKey)
    {
        if (cached == null) return "cache-not-found";
        if (!string.Equals(
                cached.Version,
                CombatFoundationAutoTuneProtocol.Version,
                StringComparison.Ordinal))
        {
            return "protocol-version-changed";
        }
        if (!string.Equals(
                cached.HardwareKey,
                hardwareKey ?? "",
                StringComparison.Ordinal))
        {
            return "hardware-signature-changed";
        }
        if (cached.MeasuredUtc < DateTime.UtcNow.AddDays(-30d))
        {
            return "cache-expired";
        }
        if (cached.LowConfidence) return "low-confidence-cache";
        if (cached.SelectedParallelism <= 0
            || cached.SelectedParallelism > Math.Max(1, maximumParallelism))
        {
            return "parallelism-out-of-range";
        }
        var campaignKey = string.IsNullOrWhiteSpace(cached.CampaignCacheKey)
            ? cached.CacheKey
            : cached.CampaignCacheKey;
        return string.Equals(campaignKey, cacheKey, StringComparison.Ordinal)
            ? "cache-incomplete"
            : "campaign-signature-changed";
    }

    private static bool InferenceAutoTuneCacheCompatible(
        CombatFoundationAutoTuneResult cached,
        string cacheKey,
        int parallelism,
        DateTime utcNow)
    {
        var measuredUtc = cached.InferenceMeasuredUtc == default
            ? cached.MeasuredUtc
            : cached.InferenceMeasuredUtc;
        return cached.InferenceCalibrated
               && measuredUtc >= utcNow.AddDays(-30d)
               && string.Equals(
                   cached.InferenceCalibrationKind,
                   CombatFoundationAutoTuneProtocol.InferenceCalibrationKind,
                   StringComparison.Ordinal)
               && string.Equals(
                   cached.InferenceCacheKey,
                   cacheKey,
                   StringComparison.Ordinal)
               && InferencePlanFits(cached, parallelism);
    }

    internal static bool TryRestoreInferenceAutoTuneState(
        CombatFoundationAutoTuneResult? source,
        CombatFoundationAutoTuneResult target,
        string cacheKey,
        int parallelism,
        DateTime utcNow)
    {
        if (source == null) return false;
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (!string.Equals(
                source.Version,
                CombatFoundationAutoTuneProtocol.Version,
                StringComparison.Ordinal))
        {
            return false;
        }
        var signatureAndPlanMatch = string.Equals(
                                        source.InferenceCacheKey,
                                        cacheKey,
                                        StringComparison.Ordinal)
                                    && InferencePlanFits(source, parallelism);
        if (!signatureAndPlanMatch) return false;
        var calibrated = InferenceAutoTuneCacheCompatible(
            source,
            cacheKey,
            parallelism,
            utcNow);

        var copy = CloneAutoTuneResult(source);
        target.InferenceCacheKey = copy.InferenceCacheKey;
        // Stale calibration plans must be measured again, but their failure
        // history remains part of the same inference-plan identity. Only a
        // real healthy production window is allowed to clear that history.
        target.InferenceCalibrated = calibrated;
        target.InferenceMeasuredUtc = copy.InferenceMeasuredUtc == default
            ? copy.MeasuredUtc
            : copy.InferenceMeasuredUtc;
        target.InferenceCalibrationKind = copy.InferenceCalibrationKind;
        target.InferenceCalibrationSamples = copy.InferenceCalibrationSamples;
        target.InferenceFallbackActive = copy.InferenceFallbackActive;
        target.InferenceHealthFailureCount = copy.InferenceHealthFailureCount;
        target.LastInferenceHealthFailureReason =
            copy.LastInferenceHealthFailureReason;
        target.InferenceRecalibrationNotBeforeUtc =
            copy.InferenceRecalibrationNotBeforeUtc;
        target.SelectedInferenceMode = copy.SelectedInferenceMode;
        target.SelectedInferenceLaneCount = copy.SelectedInferenceLaneCount;
        target.SelectedInferenceBatchSize = copy.SelectedInferenceBatchSize;
        target.Measurements ??= new List<CombatFoundationAutoTuneMeasurement>();
        target.Measurements.RemoveAll(item =>
            (item?.MeasurementKind ?? "").StartsWith(
                "inference",
                StringComparison.Ordinal));
        target.Measurements.AddRange(copy.Measurements.Where(item =>
            (item?.MeasurementKind ?? "").StartsWith(
                "inference",
                StringComparison.Ordinal)));
        return true;
    }

    internal static bool InferencePlanFits(
        CombatFoundationAutoTuneResult cached,
        int parallelism)
    {
        if (cached == null || cached.SelectedInferenceBatchSize <= 0)
        {
            return false;
        }
        if (string.Equals(
                cached.SelectedInferenceMode,
                CombatFoundationExecutionProfileNames.DirectInference,
                StringComparison.Ordinal))
        {
            return true;
        }
        var callers = Math.Max(1, parallelism);
        return cached.SelectedInferenceLaneCount > 0
               && cached.SelectedInferenceLaneCount <= callers
               && cached.SelectedInferenceLaneCount
                  * cached.SelectedInferenceBatchSize <= callers;
    }

    internal static bool InferenceCalibrationCooldownActive(
        CombatFoundationAutoTuneResult autoTune,
        string cacheKey,
        int parallelism,
        DateTime utcNow)
    {
        return autoTune != null
               && autoTune.InferenceFallbackActive
               && string.Equals(
                   autoTune.InferenceCacheKey,
                   cacheKey,
                   StringComparison.Ordinal)
               && autoTune.InferenceRecalibrationNotBeforeUtc > utcNow
               && InferencePlanFits(autoTune, parallelism);
    }

    internal static bool ShouldCalibrateInference(
        CombatFoundationAutoTuneResult autoTune,
        string cacheKey,
        int parallelism,
        DateTime utcNow)
    {
        return !InferenceAutoTuneCacheCompatible(
                   autoTune,
                   cacheKey,
                   parallelism,
                   utcNow)
               && !InferenceCalibrationCooldownActive(
                   autoTune,
                   cacheKey,
                   parallelism,
                   utcNow);
    }

    internal static void RecordInferenceHealthFailure(
        CombatFoundationAutoTuneResult autoTune,
        CombatFoundationInferenceHealth health,
        int parallelism,
        DateTime utcNow)
    {
        if (autoTune == null) throw new ArgumentNullException(nameof(autoTune));
        if (health == null) throw new ArgumentNullException(nameof(health));
        var failureCount = Math.Max(1, autoTune.InferenceHealthFailureCount + 1);
        var cooldownMultiplier = 1 << Math.Min(3, failureCount - 1);
        var cooldownMinutes = Math.Min(
            CombatFoundationAutoTuneProtocol.MaximumInferenceHealthCooldownMinutes,
            CombatFoundationAutoTuneProtocol.InferenceHealthCooldownMinutes
            * cooldownMultiplier);
        autoTune.InferenceCalibrated = false;
        autoTune.InferenceFallbackActive = true;
        autoTune.InferenceHealthFailureCount = failureCount;
        autoTune.LastInferenceHealthFailureReason = health.Reason ?? "";
        autoTune.InferenceRecalibrationNotBeforeUtc = utcNow.AddMinutes(
            cooldownMinutes);
        autoTune.SelectedInferenceMode =
            CombatFoundationExecutionProfileNames.DirectInference;
        autoTune.SelectedInferenceLaneCount = Math.Max(1, parallelism);
        autoTune.SelectedInferenceBatchSize = 1;
    }

    internal static bool RecordInferenceHealthSuccess(
        CombatFoundationAutoTuneResult autoTune,
        CombatFoundationInferenceHealth health)
    {
        if (autoTune == null) throw new ArgumentNullException(nameof(autoTune));
        if (health == null) throw new ArgumentNullException(nameof(health));
        if (!autoTune.InferenceCalibrated
            || health.RevalidationRequired
            || health.Requests < CombatFoundationInferenceHealthProtocol
                .MinimumRequests)
        {
            return false;
        }
        var changed = autoTune.InferenceFallbackActive
                      || autoTune.InferenceHealthFailureCount != 0
                      || !string.IsNullOrWhiteSpace(
                          autoTune.LastInferenceHealthFailureReason)
                      || autoTune.InferenceRecalibrationNotBeforeUtc != default;
        if (!changed) return false;
        autoTune.InferenceFallbackActive = false;
        autoTune.InferenceHealthFailureCount = 0;
        autoTune.LastInferenceHealthFailureReason = "";
        autoTune.InferenceRecalibrationNotBeforeUtc = default;
        return true;
    }

    private static CombatFoundationAutoTuneResult CloneAutoTuneResult(
        CombatFoundationAutoTuneResult source)
    {
        return new CombatFoundationAutoTuneResult
        {
            Version = source.Version,
            CacheKey = source.CacheKey,
            CampaignCacheKey = source.CampaignCacheKey,
            InferenceCacheKey = source.InferenceCacheKey,
            HardwareKey = source.HardwareKey,
            MeasuredUtc = source.MeasuredUtc,
            CacheHit = source.CacheHit,
            CacheMissReason = source.CacheMissReason,
            CampaignCalibrationKind = source.CampaignCalibrationKind,
            LowConfidence = source.LowConfidence,
            SelectedParallelism = source.SelectedParallelism,
            InferenceCalibrated = source.InferenceCalibrated,
            InferenceMeasuredUtc = source.InferenceMeasuredUtc,
            InferenceCalibrationKind = source.InferenceCalibrationKind,
            InferenceCalibrationSamples = source.InferenceCalibrationSamples,
            InferenceFallbackActive = source.InferenceFallbackActive,
            InferenceHealthFailureCount = source.InferenceHealthFailureCount,
            LastInferenceHealthFailureReason =
                source.LastInferenceHealthFailureReason,
            InferenceRecalibrationNotBeforeUtc =
                source.InferenceRecalibrationNotBeforeUtc,
            SelectedInferenceMode = source.SelectedInferenceMode,
            SelectedInferenceLaneCount = source.SelectedInferenceLaneCount,
            SelectedInferenceBatchSize = source.SelectedInferenceBatchSize,
            MeasurementCampaignsPerTrial = source.MeasurementCampaignsPerTrial,
            MinimumCampaignWaves = source.MinimumCampaignWaves,
            ThroughputTolerance = source.ThroughputTolerance,
            Objective = source.Objective,
            Measurements = (source.Measurements
                            ?? new List<CombatFoundationAutoTuneMeasurement>())
                .Select(item => new CombatFoundationAutoTuneMeasurement
                {
                    MeasurementKind = item.MeasurementKind,
                    Parallelism = item.Parallelism,
                    InferenceMode = item.InferenceMode,
                    InferenceLaneCount = item.InferenceLaneCount,
                    InferenceBatchSize = item.InferenceBatchSize,
                    TrialCount = item.TrialCount,
                    Campaigns = item.Campaigns,
                    Battles = item.Battles,
                    SearchSimulations = item.SearchSimulations,
                    ElapsedSeconds = item.ElapsedSeconds,
                    CpuUtilizationPercent = item.CpuUtilizationPercent,
                    AllocationMegabytesPerSecond =
                        item.AllocationMegabytesPerSecond,
                    Gen2CollectionsPerSecond =
                        item.Gen2CollectionsPerSecond,
                    UsefulWorkPerSecond = item.UsefulWorkPerSecond,
                    MinimumUsefulWorkPerSecond =
                        item.MinimumUsefulWorkPerSecond,
                    MaximumUsefulWorkPerSecond =
                        item.MaximumUsefulWorkPerSecond,
                    UsefulWorkStandardDeviation =
                        item.UsefulWorkStandardDeviation,
                    EfficiencyScore = item.EfficiencyScore,
                    P95LatencyMicroseconds = item.P95LatencyMicroseconds,
                    AverageBatchFill = item.AverageBatchFill,
                    InferenceRequests = item.InferenceRequests,
                    InferenceBatchEvaluations = item.InferenceBatchEvaluations,
                    InferenceTimeoutFlushes = item.InferenceTimeoutFlushes,
                    InvalidCampaigns = item.InvalidCampaigns
                })
                .ToList()
        };
    }

    private static long ReadManagedAllocationCounter()
    {
#if NET8_0_OR_GREATER
        return GC.GetTotalAllocatedBytes(false);
#else
        return GC.GetTotalMemory(false);
#endif
    }

    private CombatCampaignFoundationIntegrityReport RunIntegrityPreflight(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        ICombatPolicyValueModel policyValueModel,
        FoundationTelemetryTracker telemetry,
        int campaignsPerDifficulty,
        ulong seedStart,
        int parallelism,
        string autoTuneCacheKey,
        bool calibrateAutoTune,
        out CombatFoundationAutoTuneResult? measuredAutoTune,
        CancellationToken cancellationToken)
    {
        measuredAutoTune = null;
        var deploymentPolicyFactory =
            new CombatDecisionSimulationPolicyFactory(
                CombatSearchBudgetPolicy.WithContext(
                    request.Profile,
                    "deployment"),
                policyValueModel: policyValueModel);
        var difficulties = new[] { "normal", "advanced" };
        var schedule = new List<CombatFoundationIntegritySeed>();
        for (var index = 0;
             index < campaignsPerDifficulty * difficulties.Length;
             index++)
        {
            schedule.Add(new CombatFoundationIntegritySeed
            {
                DifficultyId = difficulties[index % difficulties.Length],
                WorldSeed = seedStart + (ulong)index
            });
        }
        schedule.AddRange(CombatFoundationIntegritySeedCorpus.KnownFailures);
        var calibrationSchedule = new List<CombatFoundationIntegritySeed>(
            schedule);
        var runs = new CombatCampaignResult?[schedule.Count];
        var effectiveParallelism = parallelism;
        if (calibrateAutoTune && schedule.Count > 0)
        {
            var maximumParallelism = Math.Max(1, parallelism);
            var allCandidates = BuildAutoTuneParallelismCandidates(
                    maximumParallelism)
                .Distinct()
                .ToList();
            var compatibleCachedParallelism = request.AutoTuneCache != null
                                              && string.Equals(
                                                  request.AutoTuneCache.Version,
                                                  CombatFoundationAutoTuneProtocol.Version,
                                                  StringComparison.Ordinal)
                                              && string.Equals(
                                                  request.AutoTuneCache.HardwareKey,
                                                  request.AutoTuneHardwareKey,
                                                  StringComparison.Ordinal)
                                              && request.AutoTuneCache
                                                     .SelectedParallelism > 0
                                              && request.AutoTuneCache
                                                     .SelectedParallelism
                                                 <= maximumParallelism
                ? request.AutoTuneCache.SelectedParallelism
                : 0;
            var alternate = compatibleCachedParallelism > 0
                ? compatibleCachedParallelism
                : allCandidates
                    .Where(value => value != maximumParallelism)
                    .OrderBy(value => Math.Abs(
                        value - maximumParallelism / 2d))
                    .ThenByDescending(value => value)
                    .FirstOrDefault();
            var candidates = new[] { maximumParallelism, alternate }
                .Where(value => value > 0)
                .Distinct()
                .ToList();
            // One full scheduler wave per pilot is enough to compare the two
            // candidates. The confirmation supplies the second wave required
            // for a reusable high-confidence cache entry.
            var campaignsPerTrial = Math.Max(8, maximumParallelism);
            var warmupCampaigns = Math.Min(
                maximumParallelism,
                8);
            var requiredCalibrationCampaigns = campaignsPerTrial
                                               * CombatFoundationAutoTuneProtocol
                                                   .MinimumCampaignWaves;
            var pilotCampaignPlan = warmupCampaigns
                                    + candidates.Count * campaignsPerTrial;
            var maximumConfirmationCampaigns = Math.Min(
                3,
                candidates.Count) * campaignsPerTrial;
            telemetry.BeginPhase(
                "auto-tune",
                pilotCampaignPlan + maximumConfirmationCampaigns);
            while (calibrationSchedule.Count < requiredCalibrationCampaigns)
            {
                var index = calibrationSchedule.Count;
                calibrationSchedule.Add(new CombatFoundationIntegritySeed
                {
                    DifficultyId = difficulties[index % difficulties.Length],
                    WorldSeed = seedStart + 100000UL + (ulong)index
                });
            }
            var measurements = new List<CombatFoundationAutoTuneMeasurement>();
            var calibrationRuns = new Dictionary<
                int,
                CombatCampaignResult?[]>();
            // Warm the JIT, ThreadPool, search kernels and the maximum campaign
            // fan-out before comparing candidates. Candidate thread startup is
            // not part of sustained training throughput.
            CombatFoundationWorkScheduler.For(
                warmupCampaigns,
                maximumParallelism,
                cancellationToken,
                index => RunCalibrationCampaign(
                    new CombatFoundationIntegritySeed
                    {
                        DifficultyId = difficulties[index % difficulties.Length],
                        WorldSeed = seedStart + 200000UL + (ulong)index
                    }),
                telemetry.SchedulerProgress);
            var pilotMeasurements = new List<AutoTuneCampaignMeasurement>();
            foreach (var candidate in candidates.OrderByDescending(value => value))
            {
                cancellationToken.ThrowIfCancellationRequested();
                pilotMeasurements.Add(Measure(
                    candidate,
                    campaignsPerTrial,
                    scheduleOffset: 0));
            }
            measurements.AddRange(pilotMeasurements.Select(item =>
                item.Measurement));
            var finalists = pilotMeasurements
                .OrderByDescending(item => AutoTuneObjectiveScore(
                    item.Measurement,
                    request.AutoTuneObjective))
                .ThenByDescending(item => item.Measurement.Parallelism)
                .Take(Math.Min(2, pilotMeasurements.Count))
                .ToList();
            var maximumPilot = pilotMeasurements.First(item =>
                item.Measurement.Parallelism == maximumParallelism);
            if (finalists.All(item =>
                    item.Measurement.Parallelism != maximumParallelism))
            {
                finalists.Add(maximumPilot);
            }
            telemetry.SetPhaseCampaignPlan(
                pilotCampaignPlan
                + finalists.Count * campaignsPerTrial);
            foreach (var pilot in finalists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var confirmation = Measure(
                    pilot.Measurement.Parallelism,
                    campaignsPerTrial,
                    campaignsPerTrial);
                var measured = CombineAutoTuneTrials(pilot, confirmation);
                measurements.RemoveAll(item =>
                    item.Parallelism == measured.Measurement.Parallelism);
                measurements.Add(measured.Measurement);
                calibrationRuns[measured.Measurement.Parallelism] = measured.Runs;
            }
            effectiveParallelism = CombatFoundationAutoTuneSelector.Select(
                measurements,
                request.AutoTuneThroughputTolerance,
                request.AutoTuneObjective);
            measuredAutoTune = new CombatFoundationAutoTuneResult
            {
                CacheKey = autoTuneCacheKey,
                CampaignCacheKey = autoTuneCacheKey,
                InferenceCacheKey = BuildInferenceAutoTuneCacheKey(
                    request,
                    request.Training,
                    effectiveParallelism),
                HardwareKey = request.AutoTuneHardwareKey ?? "",
                MeasuredUtc = DateTime.UtcNow,
                LowConfidence = !CombatFoundationAutoTuneSelector
                    .HasCampaignConfidence(
                        measurements,
                        maximumParallelism),
                SelectedParallelism = effectiveParallelism,
                CampaignCalibrationKind = compatibleCachedParallelism > 0
                    ? "compatible-cache-verification-v1"
                    : "short-two-candidate-v1",
                SelectedInferenceMode = request.InferenceExecutionMode,
                SelectedInferenceLaneCount = request.InferenceLaneCount,
                SelectedInferenceBatchSize = request.InferenceBatchSize,
                ThroughputTolerance = request.AutoTuneThroughputTolerance,
                Objective = request.AutoTuneObjective,
                MeasurementCampaignsPerTrial = campaignsPerTrial,
                MinimumCampaignWaves = CombatFoundationAutoTuneProtocol
                    .MinimumCampaignWaves,
                Measurements = measurements
            };
            if (calibrationRuns.TryGetValue(
                    effectiveParallelism,
                    out var selectedRuns))
            {
                Array.Copy(
                    selectedRuns,
                    runs,
                    Math.Min(selectedRuns.Length, runs.Length));
            }
        }
        telemetry.BeginPhase(
            "preflight",
            runs.Count(item => item == null));
        CombatFoundationWorkScheduler.For(
            runs.Length,
            effectiveParallelism,
            cancellationToken,
            index =>
            {
                if (runs[index] != null)
                {
                    return;
                }
                var difficulty = schedule[index].DifficultyId;
                var seed = schedule[index].WorldSeed;
                runs[index] = RunCampaign(
                    request.TrainingCampaign,
                    difficulty,
                    seed,
                    ruleset,
                    deploymentPolicyFactory,
                    telemetry,
                    "preflight:" + difficulty,
                    cancellationToken);
            },
            telemetry.SchedulerProgress);
        var completed = runs
            .Where(item => item != null)
            .Select(item => item!)
            .ToList();
        var report = new CombatCampaignFoundationIntegrityReport
        {
            CampaignsPerDifficulty = campaignsPerDifficulty,
            RegressionSeedCampaigns =
                CombatFoundationIntegritySeedCorpus.KnownFailures.Count,
            CompletedCampaigns = completed.Count,
            InvalidCampaigns = completed.Count(item => item.Invalid),
            TerminalConsistencyViolations = completed.Sum(
                CountTerminalConsistencyViolations)
        };
        var semanticAudit = AggregateSemanticAudit(completed);
        report.SelectedInvalidActions =
            semanticAudit.SelectedInvalidActions;
        report.SelectedUnexplainedMismatchActions =
            semanticAudit.SelectedUnexplainedMismatchActions;
        report.SelectedSourceProjectionInvalidActions =
            semanticAudit.SelectedSourceProjectionInvalidActions;
        report.SelectedSourceProjectionUnexplainedMismatchActions =
            semanticAudit
                .SelectedSourceProjectionUnexplainedMismatchActions;
        var sourceProjectionAudits =
            semanticAudit.InvalidActions + semanticAudit.ValidActions;
        report.SourceProjectionInvalidRate = sourceProjectionAudits <= 0
            ? 0d
            : semanticAudit.InvalidActions
              / (double)sourceProjectionAudits;
        report.SourceProjectionMismatchRate =
            semanticAudit.ValidActions <= 0
                ? 0d
                : semanticAudit.UnexplainedMismatchActions
                  / (double)semanticAudit.ValidActions;
        report.SemanticGatePassed = SemanticGateSatisfied(semanticAudit);
        foreach (var campaign in completed.Where(item => item.Invalid))
        {
            AddIntegrityFailure(
                report.Failures,
                report.FailureCounts,
                campaign);
        }
        report.Failures = report.Failures
            .OrderBy(item => item.WorldSeed)
            .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
            .ToList();
        report.Passed = report.CompletedCampaigns == runs.Length
                        && report.InvalidCampaigns == 0
                        && report.TerminalConsistencyViolations == 0
                        && report.SemanticGatePassed;
        return report;

        CombatCampaignResult RunCalibrationCampaign(
            CombatFoundationIntegritySeed item)
        {
            return RunCampaign(
                request.TrainingCampaign,
                item.DifficultyId,
                item.WorldSeed,
                ruleset,
                deploymentPolicyFactory,
                telemetry,
                "auto-tune:" + item.DifficultyId,
                cancellationToken);
        }

        AutoTuneCampaignMeasurement Measure(
            int candidateParallelism,
            int sampleCampaigns,
            int scheduleOffset)
        {
            var measured = new CombatCampaignResult?[sampleCampaigns];
            using var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime.TotalSeconds;
            var allocationStart = ReadManagedAllocationCounter();
            var gen2Start = GC.CollectionCount(2);
            var stopwatch = Stopwatch.StartNew();
            CombatFoundationWorkScheduler.For(
                sampleCampaigns,
                candidateParallelism,
                cancellationToken,
                index =>
                {
                    measured[index] = RunCalibrationCampaign(
                        calibrationSchedule[scheduleOffset + index]);
                },
                telemetry.SchedulerProgress);
            stopwatch.Stop();
            var elapsed = Math.Max(0.001d, stopwatch.Elapsed.TotalSeconds);
            var completed = measured
                .Where(item => item != null)
                .Select(item => item!)
                .ToList();
            var battles = completed.Sum(item => item.CompletedBattles);
            var searchSimulations = completed
                .SelectMany(item => item.Battles
                    ?? new List<CombatSimulationResult>())
                .Sum(item => Math.Max(
                    0L,
                    item?.Metrics?.SearchSimulations ?? 0L));
            // The calibration schedule fixes campaign seeds and count, so
            // campaign/s is the least ambiguous wall-clock objective. Battle
            // and search throughput remain diagnostics on each measurement.
            var usefulWork = completed.Count / elapsed;
            var cpuPercent = Math.Max(
                0d,
                (process.TotalProcessorTime.TotalSeconds - cpuStart)
                / elapsed
                / Math.Max(1, Environment.ProcessorCount)
                * 100d);
            var allocationRate = Math.Max(
                0d,
                (ReadManagedAllocationCounter() - allocationStart)
                / elapsed
                / (1024d * 1024d));
            var gen2Rate = Math.Max(
                0d,
                (GC.CollectionCount(2) - gen2Start) / elapsed);
            return new AutoTuneCampaignMeasurement
            {
                Runs = measured,
                Measurement = new CombatFoundationAutoTuneMeasurement
                {
                    MeasurementKind = "campaign",
                    Parallelism = candidateParallelism,
                    InferenceMode = request.InferenceExecutionMode,
                    InferenceLaneCount = request.InferenceLaneCount,
                    InferenceBatchSize = request.InferenceBatchSize,
                    Campaigns = completed.Count,
                    Battles = battles,
                    SearchSimulations = searchSimulations,
                    ElapsedSeconds = elapsed,
                    CpuUtilizationPercent = cpuPercent,
                    AllocationMegabytesPerSecond = allocationRate,
                    Gen2CollectionsPerSecond = gen2Rate,
                    UsefulWorkPerSecond = usefulWork,
                    MinimumUsefulWorkPerSecond = usefulWork,
                    MaximumUsefulWorkPerSecond = usefulWork,
                    UsefulWorkStandardDeviation = 0d,
                    TrialCount = 1,
                    EfficiencyScore = CombatFoundationAutoTuneSelector.Score(
                        usefulWork,
                        gen2Rate,
                        allocationRate)
                }
            };
        }

        AutoTuneCampaignMeasurement CombineAutoTuneTrials(
            AutoTuneCampaignMeasurement first,
            AutoTuneCampaignMeasurement second)
        {
            var a = first.Measurement;
            var b = second.Measurement;
            var elapsed = Math.Max(0.001d, a.ElapsedSeconds + b.ElapsedSeconds);
            var usefulWork = (a.Campaigns + b.Campaigns) / elapsed;
            var trialMean = (a.UsefulWorkPerSecond + b.UsefulWorkPerSecond) / 2d;
            var deviation = Math.Sqrt(
                (Math.Pow(a.UsefulWorkPerSecond - trialMean, 2d)
                 + Math.Pow(b.UsefulWorkPerSecond - trialMean, 2d)) / 2d);
            var cpu = (a.CpuUtilizationPercent * a.ElapsedSeconds
                       + b.CpuUtilizationPercent * b.ElapsedSeconds) / elapsed;
            var allocation = (a.AllocationMegabytesPerSecond * a.ElapsedSeconds
                              + b.AllocationMegabytesPerSecond * b.ElapsedSeconds)
                             / elapsed;
            var gen2 = (a.Gen2CollectionsPerSecond * a.ElapsedSeconds
                        + b.Gen2CollectionsPerSecond * b.ElapsedSeconds)
                       / elapsed;
            return new AutoTuneCampaignMeasurement
            {
                Runs = first.Runs,
                Measurement = new CombatFoundationAutoTuneMeasurement
                {
                    MeasurementKind = "campaign-steady-state",
                    Parallelism = a.Parallelism,
                    InferenceMode = a.InferenceMode,
                    InferenceLaneCount = a.InferenceLaneCount,
                    InferenceBatchSize = a.InferenceBatchSize,
                    Campaigns = a.Campaigns + b.Campaigns,
                    Battles = a.Battles + b.Battles,
                    SearchSimulations = a.SearchSimulations + b.SearchSimulations,
                    ElapsedSeconds = elapsed,
                    CpuUtilizationPercent = cpu,
                    AllocationMegabytesPerSecond = allocation,
                    Gen2CollectionsPerSecond = gen2,
                    UsefulWorkPerSecond = usefulWork,
                    EfficiencyScore = CombatFoundationAutoTuneSelector.Score(
                        usefulWork,
                        gen2,
                        allocation),
                    InvalidCampaigns = a.InvalidCampaigns + b.InvalidCampaigns,
                    TrialCount = 2,
                    MinimumUsefulWorkPerSecond = Math.Min(
                        a.UsefulWorkPerSecond,
                        b.UsefulWorkPerSecond),
                    MaximumUsefulWorkPerSecond = Math.Max(
                        a.UsefulWorkPerSecond,
                        b.UsefulWorkPerSecond),
                    UsefulWorkStandardDeviation = deviation
                }
            };
        }
    }

    internal static int[] BuildAutoTuneParallelismCandidates(int maximum)
    {
        var limit = Math.Max(
            1,
            Math.Min(
                CombatFoundationParallelismProtocol.MaximumSupportedParallelism,
                maximum));
        return new[]
            {
                (int)Math.Ceiling(limit * 0.50d),
                (int)Math.Ceiling(limit * 0.75d),
                limit
            }
            .Select(value => Math.Max(1, Math.Min(limit, value)))
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }

    internal static int CalibratedParallelismCeiling(
        int maximumParallelism,
        int calibratedParallelism)
    {
        var maximum = Math.Max(1, maximumParallelism);
        return Math.Max(
            1,
            Math.Min(
                maximum,
                calibratedParallelism > 0
                    ? calibratedParallelism
                    : maximum));
    }

    private static long PredictedPeakPrivateBytes(
        long fixedBytes,
        long perLaneBytes,
        int parallelism)
    {
        fixedBytes = Math.Max(0L, fixedBytes);
        perLaneBytes = Math.Max(0L, perLaneBytes);
        parallelism = Math.Max(0, parallelism);
        if (parallelism > 0
            && perLaneBytes > (long.MaxValue - fixedBytes) / parallelism)
        {
            return long.MaxValue;
        }
        return fixedBytes + perLaneBytes * parallelism;
    }

    private static double AutoTuneObjectiveScore(
        CombatFoundationAutoTuneMeasurement measurement,
        string? objective)
    {
        return string.Equals(
                CombatFoundationAutoTuneObjectiveNames.Normalize(objective),
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput,
                StringComparison.Ordinal)
            ? measurement.UsefulWorkPerSecond
            : measurement.EfficiencyScore;
    }

    private static CombatSemanticAuditMetrics AggregateSemanticAudit(
        IEnumerable<CombatCampaignResult> campaigns)
    {
        var result = new CombatSemanticAuditMetrics();
        foreach (var battle in (campaigns ?? Array.Empty<CombatCampaignResult>())
                     .Where(item => item != null)
                     .SelectMany(item => item.Battles
                         ?? new List<CombatSimulationResult>()))
        {
            result.MergeFrom(battle?.Metrics?.SemanticAudit);
        }
        return result;
    }

    private static bool SemanticGateSatisfied(
        CombatSemanticAuditMetrics? audit)
    {
        if (audit == null)
        {
            return true;
        }
        var audited = audit.InvalidActions + audit.ValidActions;
        var invalidRate = audited <= 0
            ? 0d
            : audit.InvalidActions / (double)audited;
        var mismatchRate = audit.ValidActions <= 0
            ? 0d
            : audit.UnexplainedMismatchActions
              / (double)audit.ValidActions;
        return audit.SelectedInvalidActions == 0
               && audit.SelectedUnexplainedMismatchActions == 0
               && audit.SelectedSourceProjectionInvalidActions == 0
               && audit.SelectedSourceProjectionUnexplainedMismatchActions
                  == 0
               && invalidRate
                  <= CombatFoundationSemanticGateProtocol
                      .MaximumSourceProjectionInvalidRate
               && mismatchRate
                  <= CombatFoundationSemanticGateProtocol
                      .MaximumSourceProjectionMismatchRate;
    }

    private static string DescribeSemanticGateFailure(
        CombatSemanticAuditMetrics audit)
    {
        var audited = audit.InvalidActions + audit.ValidActions;
        var invalidRate = audited <= 0
            ? 0d
            : audit.InvalidActions / (double)audited;
        var mismatchRate = audit.ValidActions <= 0
            ? 0d
            : audit.UnexplainedMismatchActions
              / (double)audit.ValidActions;
        return "selected realized invalid="
               + audit.SelectedInvalidActions
               + ", selected realized mismatch="
               + audit.SelectedUnexplainedMismatchActions
               + ", selected decision-input invalid="
               + audit.SelectedSourceProjectionInvalidActions
               + ", selected decision-input mismatch="
               + audit.SelectedSourceProjectionUnexplainedMismatchActions
               + ", decision-input invalid rate="
               + invalidRate.ToString("P2", CultureInfo.InvariantCulture)
               + ", decision-input mismatch rate="
               + mismatchRate.ToString("P2", CultureInfo.InvariantCulture);
    }

    private CombatFoundationCapabilityProbe RunCapabilityProbe(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        CombatPolicyValueNetworkDefinition champion,
        FoundationTelemetryTracker telemetry,
        int campaignsPerDifficulty,
        int teacherCampaignsPerDifficulty,
        ulong seedStart,
        int parallelism,
        ref int completedCampaigns,
        int totalCampaigns,
        CancellationToken cancellationToken)
    {
        telemetry.BeginPhase("capability-probe");
        var initialCampaignsPerDifficulty = Math.Max(1, campaignsPerDifficulty);
        var maximumCampaignsPerDifficulty = Math.Max(
            initialCampaignsPerDifficulty,
            CombatFoundationTrainingProtocol
                .MaximumAdaptiveCapabilityProbeCampaignsPerDifficulty);
        var model = CreateParallelPolicyValueModel(
            champion,
            request,
            parallelism,
            competingModelCount: 2);
        var definitions = new[]
        {
            (
                Id: "rule-baseline",
                Campaigns: maximumCampaignsPerDifficulty,
                Factory: (Func<ICombatSimulationPolicyFactory>)(() =>
                    new CombatDecisionSimulationPolicyFactory(
                        CombatSearchBudgetPolicy.WithContext(
                            request.Profile,
                            "deployment")))),
            (
                Id: "champion-deployment",
                Campaigns: maximumCampaignsPerDifficulty,
                Factory: (Func<ICombatSimulationPolicyFactory>)(() =>
                    new CombatDecisionSimulationPolicyFactory(
                        CombatSearchBudgetPolicy.WithContext(
                            request.Profile,
                            "deployment"),
                        policyValueModel: model))),
            (
                Id: "champion-teacher-hard",
                Campaigns: Math.Max(
                    0,
                    Math.Min(
                        initialCampaignsPerDifficulty,
                        teacherCampaignsPerDifficulty)),
                Factory: (Func<ICombatSimulationPolicyFactory>)(() =>
                    new CombatAuthoritativeTeacherPolicyFactory(
                        CombatSearchBudgetPolicy.WithContext(
                            request.Profile,
                            "teacher-hard"),
                        model,
                        new CombatAuthoritativeTeacherOptions
                        {
                            AuditProbability =
                                request.HardTeacherExactBranchProbability,
                            RandomSeed = CombatFoundationSeedPlan.ToRandomSeed(
                                seedStart ^ 0x54454143484552UL)
                        },
                        campaignRunner.SimulationEngine)))
        };
        var report = new CombatFoundationCapabilityProbe
        {
            CampaignsPerDifficulty = initialCampaignsPerDifficulty,
            MaximumCampaignsPerDifficulty = maximumCampaignsPerDifficulty,
            SeedStart = seedStart,
            BaselineGateRequired =
                request.RequireCapabilityProbeBaselineGain
        };
        var difficulties = new[] { "normal", "advanced" };
        var runsByArm = definitions
            .Select(definition => new CombatCampaignResult?[
                difficulties.Length * definition.Campaigns])
            .ToArray();
        var completed = completedCampaigns;
        var probeBatchSize = Math.Max(
            1,
            Math.Min(
                maximumCampaignsPerDifficulty,
                request.CapabilityProbeBatchSize));
        var pairedCampaignsCompleted = 0;
        for (var batchStart = 0;
             batchStart < maximumCampaignsPerDifficulty;)
        {
            var stageLimit = batchStart < initialCampaignsPerDifficulty
                ? initialCampaignsPerDifficulty
                : maximumCampaignsPerDifficulty;
            var batchCount = Math.Min(
                probeBatchSize,
                stageLimit - batchStart);
            var skipSaturatedNormal = batchStart >= initialCampaignsPerDifficulty
                                      && CapabilityDifficultySaturatedAtVictory(
                                          runsByArm[0],
                                          runsByArm[1],
                                          maximumCampaignsPerDifficulty,
                                          difficultyIndex: 0,
                                          completedCampaigns:
                                          pairedCampaignsCompleted);
            var activeDifficultyIndexes = skipSaturatedNormal
                ? new[] { 1 }
                : new[] { 0, 1 };
            report.SaturatedNormalExpansionSkipped |= skipSaturatedNormal;
            var workPerArm = activeDifficultyIndexes.Length * batchCount;
            CombatFoundationWorkScheduler.For(
                2 * workPerArm,
                parallelism,
                cancellationToken,
                workIndex =>
                {
                    var armIndex = workIndex / workPerArm;
                    var armOffset = workIndex % workPerArm;
                    var difficultyIndex = activeDifficultyIndexes[
                        armOffset / batchCount];
                    var campaignIndex = batchStart + armOffset % batchCount;
                    var definition = definitions[armIndex];
                    var difficulty = difficulties[difficultyIndex];
                    var campaign = RunCampaign(
                        request.ValidationCampaign,
                        difficulty,
                        seedStart + (ulong)campaignIndex,
                        ruleset,
                        definition.Factory(),
                        telemetry,
                        "capability-probe:" + definition.Id,
                        cancellationToken);
                    runsByArm[armIndex][
                        difficultyIndex * maximumCampaignsPerDifficulty
                        + campaignIndex] = campaign;
                    ReportProgress(
                        request,
                        telemetry,
                        campaign,
                        ref completed,
                        totalCampaigns,
                        "能力上限诊断：" + definition.Id);
                },
                telemetry.SchedulerProgress);
            pairedCampaignsCompleted += batchCount;
            report.AdaptiveExpansionUsed |= pairedCampaignsCompleted
                                            > initialCampaignsPerDifficulty;
            report.CompletedStages.Add(pairedCampaignsCompleted);
            batchStart += batchCount;
            if (pairedCampaignsCompleted < initialCampaignsPerDifficulty)
            {
                continue;
            }
            var interim = BuildCapabilityProbeEvidence(
                request,
                runsByArm[0],
                runsByArm[1],
                maximumCampaignsPerDifficulty,
                seedStart);
            if (!ShouldExpandCapabilityProbe(
                    request,
                    interim,
                    pairedCampaignsCompleted,
                    initialCampaignsPerDifficulty,
                    maximumCampaignsPerDifficulty))
            {
                report.StoppedEarly = pairedCampaignsCompleted
                                      < maximumCampaignsPerDifficulty;
                break;
            }
        }
        var teacherCampaigns = definitions[2].Campaigns;
        CombatFoundationWorkScheduler.For(
            difficulties.Length * teacherCampaigns,
            parallelism,
            cancellationToken,
            armOffset =>
            {
                var difficultyIndex = armOffset / teacherCampaigns;
                var campaignIndex = armOffset % teacherCampaigns;
                var definition = definitions[2];
                var difficulty = difficulties[difficultyIndex];
                var campaign = RunCampaign(
                    request.ValidationCampaign,
                    difficulty,
                    seedStart + (ulong)campaignIndex,
                    ruleset,
                    definition.Factory(),
                    telemetry,
                    "capability-probe:" + definition.Id,
                    cancellationToken);
                runsByArm[2][armOffset] = campaign;
                ReportProgress(
                    request,
                    telemetry,
                    campaign,
                    ref completed,
                    totalCampaigns,
                    "能力上限诊断：" + definition.Id);
            },
            telemetry.SchedulerProgress);
        report.CampaignsSaved = Math.Max(
            0,
            maximumCampaignsPerDifficulty * 4
            - runsByArm[0].Count(item => item != null)
            - runsByArm[1].Count(item => item != null));
        completedCampaigns = completed;
        for (var armIndex = 0; armIndex < definitions.Length; armIndex++)
        {
            var definition = definitions[armIndex];
            var runs = runsByArm[armIndex]
                .Where(item => item != null)
                .Select(item => item!)
                .ToList();
            var normal = runs.Where(item => string.Equals(
                item.DifficultyId,
                "normal",
                StringComparison.Ordinal)).ToList();
            var advanced = runs.Where(item => string.Equals(
                item.DifficultyId,
                "advanced",
                StringComparison.Ordinal)).ToList();
            var normalVictories =
                normal.Count(item => item.FinalBossVictory);
            var advancedVictories =
                advanced.Count(item => item.FinalBossVictory);
            report.Arms.Add(new CombatFoundationCapabilityProbeArm
            {
                ArmId = definition.Id,
                NormalCampaigns = normal.Count,
                NormalVictories = normalVictories,
                AdvancedCampaigns = advanced.Count,
                AdvancedVictories = advancedVictories,
                InvalidCampaigns = runs.Count(item => item.Invalid),
                AverageCompletedBattles = runs.Count == 0
                    ? 0d
                    : runs.Average(item => item.CompletedBattles),
                NormalWilsonLowerBound =
                    CombatFoundationCurriculum.WilsonLowerBound(
                        normalVictories,
                        normal.Count),
                AdvancedWilsonLowerBound =
                    CombatFoundationCurriculum.WilsonLowerBound(
                        advancedVictories,
                        advanced.Count)
            });
        }
        report.NormalCampaignsExecuted = report.Arms
            .First(item => string.Equals(
                item.ArmId,
                "champion-deployment",
                StringComparison.Ordinal))
            .NormalCampaigns;
        report.AdvancedCampaignsExecuted = report.Arms
            .First(item => string.Equals(
                item.ArmId,
                "champion-deployment",
                StringComparison.Ordinal))
            .AdvancedCampaigns;
        for (var difficultyIndex = 0;
             difficultyIndex < difficulties.Length;
             difficultyIndex++)
        {
            for (var campaignIndex = 0;
             campaignIndex < maximumCampaignsPerDifficulty;
                 campaignIndex++)
            {
                var offset =
                    difficultyIndex * maximumCampaignsPerDifficulty
                    + campaignIndex;
                var baselineRun = runsByArm[0][offset];
                var championRun = runsByArm[1][offset];
                if (baselineRun == null || championRun == null)
                {
                    continue;
                }
                report.Pairs.Add(
                    new CombatFoundationCapabilityProbePair
                    {
                        DifficultyId = difficulties[difficultyIndex],
                        WorldSeed = seedStart + (ulong)campaignIndex,
                        BaselineVictory = baselineRun.FinalBossVictory,
                        ChampionVictory = championRun.FinalBossVictory,
                        BaselineCompletedBattles =
                            baselineRun.CompletedBattles,
                        ChampionCompletedBattles =
                            championRun.CompletedBattles,
                        BaselineInvalid = baselineRun.Invalid,
                        ChampionInvalid = championRun.Invalid
                    });
            }
        }
        EvaluateCapabilityBaselineGate(request, report);
        if (request.EnableCapabilityDecisionDifferenceDiagnostics)
        {
            report.DecisionDifferences.AddRange(
                RunCapabilityDecisionDifferenceDiagnostics(
                    request,
                    ruleset,
                    model,
                    telemetry,
                    report.Pairs,
                    parallelism,
                    ref completedCampaigns,
                    totalCampaigns,
                    cancellationToken));
        }
        return report;
    }

    internal static bool ShouldExpandCapabilityProbe(
        CombatCampaignFoundationTrainingRequest request,
        CombatFoundationCapabilityProbe evidence,
        int completedCampaignsPerDifficulty,
        int initialCampaignsPerDifficulty,
        int maximumCampaignsPerDifficulty)
    {
        var completed = Math.Max(0, completedCampaignsPerDifficulty);
        var initial = Math.Max(1, initialCampaignsPerDifficulty);
        var maximum = Math.Max(initial, maximumCampaignsPerDifficulty);
        if (completed < initial)
        {
            return true;
        }
        if (completed >= maximum)
        {
            return false;
        }
        if (string.Equals(
                evidence.BaselineGateVerdict,
                "fail",
                StringComparison.Ordinal))
        {
            return false;
        }
        var discordant = evidence.ChampionOnlyWins
                         + evidence.BaselineOnlyWins;
        var conclusivePass = evidence.PassedBaselineGate
                             && (evidence.DepthGainEvidencePassed
                                 || discordant >= Math.Max(
                                     1,
                                     request.MinimumArenaDiscordantPairs));
        return !conclusivePass;
    }

    private static CombatFoundationCapabilityProbe BuildCapabilityProbeEvidence(
        CombatCampaignFoundationTrainingRequest request,
        IReadOnlyList<CombatCampaignResult?> baselineRuns,
        IReadOnlyList<CombatCampaignResult?> championRuns,
        int campaignCapacity,
        ulong seedStart)
    {
        var report = new CombatFoundationCapabilityProbe
        {
            SeedStart = seedStart,
            CampaignsPerDifficulty = campaignCapacity,
            MaximumCampaignsPerDifficulty = campaignCapacity,
            BaselineGateRequired = request.RequireCapabilityProbeBaselineGain
        };
        report.Arms.Add(BuildCapabilityProbeArm(
            "rule-baseline",
            baselineRuns));
        report.Arms.Add(BuildCapabilityProbeArm(
            "champion-deployment",
            championRuns));
        for (var difficultyIndex = 0; difficultyIndex < 2; difficultyIndex++)
        {
            for (var campaignIndex = 0;
                 campaignIndex < campaignCapacity;
                 campaignIndex++)
            {
                var offset = difficultyIndex * campaignCapacity + campaignIndex;
                var baseline = baselineRuns[offset];
                var champion = championRuns[offset];
                if (baseline == null || champion == null)
                {
                    continue;
                }
                report.Pairs.Add(new CombatFoundationCapabilityProbePair
                {
                    DifficultyId = difficultyIndex == 0 ? "normal" : "advanced",
                    WorldSeed = seedStart + (ulong)campaignIndex,
                    BaselineVictory = baseline.FinalBossVictory,
                    ChampionVictory = champion.FinalBossVictory,
                    BaselineCompletedBattles = baseline.CompletedBattles,
                    ChampionCompletedBattles = champion.CompletedBattles,
                    BaselineInvalid = baseline.Invalid,
                    ChampionInvalid = champion.Invalid
                });
            }
        }
        EvaluateCapabilityBaselineGate(request, report);
        return report;
    }

    private IReadOnlyList<CombatFoundationDecisionDifferenceCase>
        RunCapabilityDecisionDifferenceDiagnostics(
            CombatCampaignFoundationTrainingRequest request,
            CombatRuleset ruleset,
            ICombatPolicyValueModel championModel,
            FoundationTelemetryTracker telemetry,
            IReadOnlyList<CombatFoundationCapabilityProbePair> pairs,
            int parallelism,
            ref int completedCampaigns,
            int totalCampaigns,
            CancellationToken cancellationToken)
    {
        var maximumCases = Math.Max(
            0,
            request.MaximumCapabilityDecisionDifferenceCases);
        if (maximumCases <= 0)
        {
            return Array.Empty<CombatFoundationDecisionDifferenceCase>();
        }
        var selected = (pairs
                        ?? Array.Empty<CombatFoundationCapabilityProbePair>())
            .Where(pair => !pair.BaselineInvalid
                           && !pair.ChampionInvalid
                           && pair.BaselineVictory != pair.ChampionVictory)
            .OrderByDescending(pair => string.Equals(
                pair.DifficultyId,
                "advanced",
                StringComparison.Ordinal))
            .ThenByDescending(pair => pair.BaselineVictory
                                      && !pair.ChampionVictory)
            .ThenBy(pair => pair.WorldSeed)
            .Take(maximumCases)
            .ToArray();
        if (selected.Length == 0)
        {
            return Array.Empty<CombatFoundationDecisionDifferenceCase>();
        }

        telemetry.BeginPhase("capability-decision-difference");
        var deploymentProfile = CombatSearchBudgetPolicy.WithContext(
            request.Profile,
            "deployment");
        var cases = new CombatFoundationDecisionDifferenceCase?[selected.Length];
        var completed = completedCampaigns;
        CombatFoundationWorkScheduler.For(
            selected.Length,
            Math.Max(1, Math.Min(parallelism, selected.Length)),
            cancellationToken,
            index =>
            {
                var pair = selected[index];
                var baselineFactory = new RecordingCampaignPolicyFactory(
                    deploymentProfile,
                    NullCombatPolicyValueModel.Instance,
                    "capability-rule-baseline-diagnostic",
                    0d,
                    1d,
                    pair.WorldSeed,
                    0d,
                    campaignRunner.SimulationEngine,
                    request.ContentSetHash,
                    request.OwnerModSetHash,
                    recordWorldModelObservations: false);
                var baseline = RunCampaign(
                    request.ValidationCampaign,
                    pair.DifficultyId,
                    pair.WorldSeed,
                    ruleset,
                    baselineFactory,
                    telemetry,
                    "capability-decision-difference:rule-baseline",
                    cancellationToken);
                var baselineEpisodes = baselineFactory.Complete(
                    baseline,
                    journeyRunSuffix: ":decision-difference:baseline");
                ReportProgress(
                    request,
                    telemetry,
                    baseline,
                    ref completed,
                    totalCampaigns,
                    "能力失败决策差异：规则基线");

                var championFactory = new RecordingCampaignPolicyFactory(
                    deploymentProfile,
                    championModel,
                    "capability-champion-diagnostic",
                    0d,
                    1d,
                    pair.WorldSeed,
                    0d,
                    campaignRunner.SimulationEngine,
                    request.ContentSetHash,
                    request.OwnerModSetHash,
                    recordWorldModelObservations: false);
                var champion = RunCampaign(
                    request.ValidationCampaign,
                    pair.DifficultyId,
                    pair.WorldSeed,
                    ruleset,
                    championFactory,
                    telemetry,
                    "capability-decision-difference:champion",
                    cancellationToken);
                var championEpisodes = championFactory.Complete(
                    champion,
                    journeyRunSuffix: ":decision-difference:champion");
                ReportProgress(
                    request,
                    telemetry,
                    champion,
                    ref completed,
                    totalCampaigns,
                    "能力失败决策差异：候选模型");
                cases[index] = FindFirstDecisionDifference(
                    pair,
                    baselineEpisodes,
                    championEpisodes);
                ReleaseTransientEpisodeStorage(baselineEpisodes);
                ReleaseTransientEpisodeStorage(championEpisodes);
            },
            telemetry.SchedulerProgress);
        completedCampaigns = completed;
        return cases.Where(item => item != null)
            .Select(item => item!)
            .ToArray();
    }

    internal static CombatFoundationDecisionDifferenceCase?
        FindFirstDecisionDifference(
            CombatFoundationCapabilityProbePair pair,
            IReadOnlyList<CombatEpisode> baselineEpisodes,
            IReadOnlyList<CombatEpisode> championEpisodes)
    {
        if (pair == null)
        {
            return null;
        }
        var championByState = (championEpisodes ?? Array.Empty<CombatEpisode>())
            .SelectMany(episode => (episode.Frames
                                    ?? new List<CombatEpisodeFrame>())
                .Select(frame => new
                {
                    episode.JourneyBattleIndex,
                    Frame = frame
                }))
            .GroupBy(item => (
                item.JourneyBattleIndex,
                item.Frame.DecisionSequence,
                item.Frame.StateFingerprint ?? ""))
            .ToDictionary(group => group.Key, group => group.First().Frame);
        foreach (var baselineEpisode in (baselineEpisodes
                     ?? Array.Empty<CombatEpisode>())
                 .OrderBy(episode => episode.JourneyBattleIndex))
        {
            foreach (var baselineFrame in baselineEpisode.Frames
                         ?? new List<CombatEpisodeFrame>())
            {
                var key = (
                    baselineEpisode.JourneyBattleIndex,
                    baselineFrame.DecisionSequence,
                    baselineFrame.StateFingerprint ?? "");
                if (!championByState.TryGetValue(key, out var championFrame)
                    || string.Equals(
                        baselineFrame.ExecutedCandidateId,
                        championFrame.ExecutedCandidateId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                var championViewOfBaseline = (championFrame.Candidates
                                              ?? new List<CombatEpisodeCandidate>())
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.CandidateId,
                        baselineFrame.ExecutedCandidateId,
                        StringComparison.Ordinal));
                var championSelected = (championFrame.Candidates
                                        ?? new List<CombatEpisodeCandidate>())
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.CandidateId,
                        championFrame.ExecutedCandidateId,
                        StringComparison.Ordinal));
                var baselineSelected = (baselineFrame.Candidates
                                        ?? new List<CombatEpisodeCandidate>())
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.CandidateId,
                        baselineFrame.ExecutedCandidateId,
                        StringComparison.Ordinal));
                var preferred = pair.BaselineVictory != pair.ChampionVictory
                    ? pair.BaselineVictory
                        ? baselineFrame.ExecutedCandidateId
                        : championFrame.ExecutedCandidateId
                    : pair.BaselineCompletedBattles
                      >= pair.ChampionCompletedBattles
                        ? baselineFrame.ExecutedCandidateId
                        : championFrame.ExecutedCandidateId;
                return new CombatFoundationDecisionDifferenceCase
                {
                    DataPartition = CombatFoundationDecisionDifferenceProtocol
                        .AcceptanceDiagnosticPartition,
                    TrainingEligible = false,
                    AcceptanceSeedRetired = false,
                    DifficultyId = pair.DifficultyId,
                    WorldSeed = pair.WorldSeed,
                    JourneyBattleIndex = baselineEpisode.JourneyBattleIndex,
                    DecisionSequence = baselineFrame.DecisionSequence,
                    StateFingerprint = baselineFrame.StateFingerprint ?? "",
                    BaselineVictory = pair.BaselineVictory,
                    ChampionVictory = pair.ChampionVictory,
                    BaselineCompletedBattles = pair.BaselineCompletedBattles,
                    ChampionCompletedBattles = pair.ChampionCompletedBattles,
                    PreferredCandidateId = preferred,
                    FailureCategory = DecisionDifferenceCategory(
                        pair,
                        championSelected,
                        championViewOfBaseline),
                    Confidence = pair.BaselineVictory != pair.ChampionVictory
                        ? 1d
                        : Math.Min(
                            1d,
                            Math.Abs(
                                pair.BaselineCompletedBattles
                                - pair.ChampionCompletedBattles) / 10d),
                    BaselineDecision = CandidateTrace(baselineSelected),
                    ChampionDecision = CandidateTrace(championSelected),
                    ChampionViewOfBaselineAction = CandidateTrace(
                        championViewOfBaseline)
                };
            }
        }
        return null;
    }

    private static string DecisionDifferenceCategory(
        CombatFoundationCapabilityProbePair pair,
        CombatEpisodeCandidate? championSelected,
        CombatEpisodeCandidate? championViewOfBaseline)
    {
        if (pair.ChampionVictory && !pair.BaselineVictory)
        {
            return "model-improvement";
        }
        if (championSelected == null || championViewOfBaseline == null)
        {
            return "candidate-set-divergence";
        }
        if (championViewOfBaseline.SearchValue
            > championSelected.SearchValue + 0.000001d)
        {
            return "search-selection";
        }
        if (championSelected.SearchDeathRisk
            > championViewOfBaseline.SearchDeathRisk + 0.10d)
        {
            return "risk-calibration";
        }
        return "policy-value-ranking";
    }

    private static CombatFoundationDecisionCandidateTrace CandidateTrace(
        CombatEpisodeCandidate? candidate)
    {
        return candidate == null
            ? new CombatFoundationDecisionCandidateTrace()
            : new CombatFoundationDecisionCandidateTrace
            {
                CandidateId = candidate.CandidateId,
                SearchVisits = candidate.SearchVisits,
                SearchPrior = candidate.SearchPrior,
                SearchValue = candidate.SearchValue,
                SearchDeathRisk = candidate.SearchDeathRisk,
                SearchMeanReturn = candidate.SearchMeanReturn,
                SearchReturnStandardError =
                    candidate.SearchReturnStandardError,
                BaseRuleScore = candidate.BaseRuleScore,
                RawResidualScore = candidate.RawResidualScore,
                ResidualApplicability = candidate.ResidualApplicability,
                AppliedResidualScore = candidate.AppliedResidualScore,
                RuleScore = candidate.RuleScore,
                SearchLowerTailMean = candidate.SearchLowerTailMean
            };
    }

    private static CombatFoundationCapabilityProbeArm BuildCapabilityProbeArm(
        string armId,
        IReadOnlyList<CombatCampaignResult?> source)
    {
        var runs = source.Where(item => item != null)
            .Select(item => item!)
            .ToList();
        var normal = runs.Where(item => string.Equals(
            item.DifficultyId,
            "normal",
            StringComparison.Ordinal)).ToList();
        var advanced = runs.Where(item => string.Equals(
            item.DifficultyId,
            "advanced",
            StringComparison.Ordinal)).ToList();
        var normalVictories = normal.Count(item => item.FinalBossVictory);
        var advancedVictories = advanced.Count(item => item.FinalBossVictory);
        return new CombatFoundationCapabilityProbeArm
        {
            ArmId = armId,
            NormalCampaigns = normal.Count,
            NormalVictories = normalVictories,
            AdvancedCampaigns = advanced.Count,
            AdvancedVictories = advancedVictories,
            InvalidCampaigns = runs.Count(item => item.Invalid),
            AverageCompletedBattles = runs.Count == 0
                ? 0d
                : runs.Average(item => item.CompletedBattles),
            NormalWilsonLowerBound =
                CombatFoundationCurriculum.WilsonLowerBound(
                    normalVictories,
                    normal.Count),
            AdvancedWilsonLowerBound =
                CombatFoundationCurriculum.WilsonLowerBound(
                    advancedVictories,
                    advanced.Count)
        };
    }

    internal static bool CapabilityDifficultySaturatedAtVictory(
        IReadOnlyList<CombatCampaignResult?> baseline,
        IReadOnlyList<CombatCampaignResult?> champion,
        int campaignCapacity,
        int difficultyIndex,
        int completedCampaigns)
    {
        var completed = Math.Max(
            0,
            Math.Min(campaignCapacity, completedCampaigns));
        if (completed == 0)
        {
            return false;
        }
        var offset = Math.Max(0, difficultyIndex) * campaignCapacity;
        for (var index = 0; index < completed; index++)
        {
            var baselineRun = baseline[offset + index];
            var championRun = champion[offset + index];
            if (baselineRun == null
                || championRun == null
                || baselineRun.Invalid
                || championRun.Invalid
                || !baselineRun.FinalBossVictory
                || !championRun.FinalBossVictory)
            {
                return false;
            }
        }
        return true;
    }

    internal static int RequiredWilsonVictories(
        int trials,
        double requiredLowerBound)
    {
        var count = Math.Max(0, trials);
        var effectiveLowerBound = EffectiveWilsonThreshold(
            count,
            requiredLowerBound);
        for (var victories = 0; victories <= count; victories++)
        {
            if (CombatFoundationCurriculum.WilsonLowerBound(
                    victories,
                    count)
                >= effectiveLowerBound)
            {
                return victories;
            }
        }
        return count + 1;
    }

    public static int EstimateTuningCampaigns(
        int candidateCount,
        int normalCampaigns,
        int advancedCampaigns,
        bool progressive,
        int screeningNormalCampaigns,
        int screeningAdvancedCampaigns,
        int finalistCount)
    {
        var candidates = Math.Max(0, candidateCount);
        var normal = Math.Max(0, normalCampaigns);
        var advanced = Math.Max(0, advancedCampaigns);
        var fullPerCandidate = normal + advanced;
        if (!progressive || candidates <= 1 || fullPerCandidate == 0)
        {
            return candidates * fullPerCandidate;
        }
        var screeningNormal = Math.Max(
            0,
            Math.Min(normal, screeningNormalCampaigns));
        var screeningAdvanced = Math.Max(
            0,
            Math.Min(advanced, screeningAdvancedCampaigns));
        var screeningPerCandidate =
            screeningNormal + screeningAdvanced;
        var finalists = Math.Max(
            1,
            Math.Min(candidates, finalistCount));
        if (screeningPerCandidate == 0
            || screeningPerCandidate >= fullPerCandidate
            || finalists >= candidates)
        {
            return candidates * fullPerCandidate;
        }
        return candidates * screeningPerCandidate
               + finalists * (fullPerCandidate - screeningPerCandidate);
    }

    private static double EffectiveWilsonThreshold(
        int trials,
        double requestedLowerBound)
    {
        var count = Math.Max(0, trials);
        return Math.Min(
            Math.Max(0d, Math.Min(1d, requestedLowerBound)),
            CombatFoundationCurriculum.WilsonLowerBound(count, count));
    }

    internal static int ResolveIterationLimit(
        CombatCampaignFoundationTrainingRequest request)
    {
        var iterations = Math.Max(1, Math.Min(20, request.Iterations));
        if (request.Resume == null
            || request.Resume.SchemaVersion
               != CombatFoundationWorkerProtocol.SchemaVersion
            || !ResumeCompatible(request.Resume)
            || !(string.Equals(
                     request.Resume.Stage,
                     "validation",
                     StringComparison.Ordinal)
                 || string.Equals(
                     request.Resume.Stage,
                     "iteration-complete",
                     StringComparison.Ordinal))
            || request.AdditionalIterationsOnResume <= 0)
        {
            return iterations;
        }
        return Math.Min(
            20,
            Math.Max(
                iterations,
                request.Resume.NextIteration
                + request.AdditionalIterationsOnResume));
    }

    internal static void EvaluateCapabilityBaselineGate(
        CombatCampaignFoundationTrainingRequest request,
        CombatFoundationCapabilityProbe report)
    {
        report.BaselineGateRequired =
            request.RequireCapabilityProbeBaselineGain;
        var baseline = report.Arms.FirstOrDefault(item => string.Equals(
            item.ArmId,
            "rule-baseline",
            StringComparison.Ordinal));
        var champion = report.Arms.FirstOrDefault(item => string.Equals(
            item.ArmId,
            "champion-deployment",
            StringComparison.Ordinal));
        var teacher = report.Arms.FirstOrDefault(item => string.Equals(
            item.ArmId,
            "champion-teacher-hard",
            StringComparison.Ordinal));
        if (!report.BaselineGateRequired)
        {
            report.PassedBaselineGate = true;
            report.BaselineGateVerdict = "pass";
            report.BaselineGateReason = "baseline gain gate disabled";
            return;
        }
        if (baseline == null || champion == null)
        {
            report.PassedBaselineGate = false;
            report.BaselineGateVerdict = "fail";
            report.BaselineGateReason =
                "baseline or champion probe arm is missing";
            return;
        }
        report.ChampionVictoryGain =
            champion.NormalVictories
            + champion.AdvancedVictories
            - baseline.NormalVictories
            - baseline.AdvancedVictories;
        report.ChampionDepthGain =
            champion.AverageCompletedBattles
            - baseline.AverageCompletedBattles;
        var validPairs = report.Pairs.Where(item =>
            !item.BaselineInvalid && !item.ChampionInvalid).ToList();
        if (baseline.InvalidCampaigns > 0
            || champion.InvalidCampaigns > 0
            || report.Pairs.Any(item =>
                item.BaselineInvalid || item.ChampionInvalid))
        {
            report.PassedBaselineGate = false;
            report.BaselineGateVerdict = "fail";
            report.BaselineGateReason =
                "invalid campaign in paired capability probe";
            return;
        }
        if (validPairs.Count == 0)
        {
            // Compatibility fallback for persisted reports created before
            // paired outcomes were retained.
            var noDifficultyRegression =
                champion.NormalVictories >= baseline.NormalVictories
                && champion.AdvancedVictories >= baseline.AdvancedVictories;
            var meaningfulGain =
                report.ChampionVictoryGain
                >= Math.Max(
                    1,
                    request.CapabilityProbeMinimumVictoryGain);
            report.BaselineGateVerdict =
                noDifficultyRegression && meaningfulGain
                    ? "pass"
                    : noDifficultyRegression
                        ? "inconclusive"
                        : "fail";
            report.PassedBaselineGate = string.Equals(
                report.BaselineGateVerdict,
                "pass",
                StringComparison.Ordinal);
            report.BaselineGateReason =
                "legacy aggregate probe; verdict="
                + report.BaselineGateVerdict
                + ", victoryGain="
                + report.ChampionVictoryGain;
            return;
        }

        report.ChampionOnlyWins = validPairs.Count(item =>
            item.ChampionVictory && !item.BaselineVictory);
        report.BaselineOnlyWins = validPairs.Count(item =>
            item.BaselineVictory && !item.ChampionVictory);
        report.NormalChampionOnlyWins = validPairs.Count(item =>
            string.Equals(
                item.DifficultyId,
                "normal",
                StringComparison.Ordinal)
            && item.ChampionVictory
            && !item.BaselineVictory);
        report.NormalBaselineOnlyWins = validPairs.Count(item =>
            string.Equals(
                item.DifficultyId,
                "normal",
                StringComparison.Ordinal)
            && item.BaselineVictory
            && !item.ChampionVictory);
        report.AdvancedChampionOnlyWins = validPairs.Count(item =>
            string.Equals(
                item.DifficultyId,
                "advanced",
                StringComparison.Ordinal)
            && item.ChampionVictory
            && !item.BaselineVictory);
        report.AdvancedBaselineOnlyWins = validPairs.Count(item =>
            string.Equals(
                item.DifficultyId,
                "advanced",
                StringComparison.Ordinal)
            && item.BaselineVictory
            && !item.ChampionVictory);
        var discordant =
            report.ChampionOnlyWins + report.BaselineOnlyWins;
        report.PairedWinWilsonLowerBound =
            CombatFoundationCurriculum.WilsonLowerBound(
                report.ChampionOnlyWins,
                discordant);
        report.PairedWinWilsonUpperBound = discordant <= 0
            ? 1d
            : 1d - CombatFoundationCurriculum.WilsonLowerBound(
                report.BaselineOnlyWins,
                discordant);
        var pairedLossDepthGains = validPairs
            .Where(item =>
                !item.BaselineVictory && !item.ChampionVictory)
            .Select(item =>
                (double)(item.ChampionCompletedBattles
                         - item.BaselineCompletedBattles))
            .ToList();
        report.PairedLossPairs = pairedLossDepthGains.Count;
        report.PairedLossMedianDepthGain = Median(pairedLossDepthGains);

        var normalRegression = CrediblePairedRegression(
            report.NormalChampionOnlyWins,
            report.NormalBaselineOnlyWins);
        var advancedRegression = CrediblePairedRegression(
            report.AdvancedChampionOnlyWins,
            report.AdvancedBaselineOnlyWins);
        var credibleWinGain =
            discordant > 0
            && report.PairedWinWilsonLowerBound > 0.5d
            && report.ChampionOnlyWins - report.BaselineOnlyWins
            >= Math.Max(
                1,
                request.CapabilityProbeMinimumVictoryGain);
        var aggregateRegression = CrediblePairedRegression(
            report.ChampionOnlyWins,
            report.BaselineOnlyWins);
        var minimumDepthGain = Math.Max(
            0d,
            request.CapabilityProbeMinimumDepthGain);
        var minimumDepthPairs = Math.Max(
            CombatFoundationTrainingProtocol.MinimumCapabilityDepthEvidencePairs,
            request.MinimumArenaDiscordantPairs);
        report.DepthGainEvidencePassed =
            minimumDepthGain > 0d
            &&
            !aggregateRegression
            && !normalRegression
            && !advancedRegression
            && report.PairedLossPairs >= minimumDepthPairs
            && report.PairedLossMedianDepthGain >= minimumDepthGain
            && report.ChampionDepthGain >= minimumDepthGain;
        if (aggregateRegression || normalRegression || advancedRegression)
        {
            report.BaselineGateVerdict = "fail";
        }
        else if (credibleWinGain || report.DepthGainEvidencePassed)
        {
            report.BaselineGateVerdict = "pass";
        }
        else
        {
            report.BaselineGateVerdict = "inconclusive";
        }
        // An inconclusive probe is not publication evidence.  Stop before the
        // expensive formal validation and collect more training/probe evidence
        // instead of treating absence of proven regression as proven quality.
        report.PassedBaselineGate = string.Equals(
            report.BaselineGateVerdict,
            "pass",
            StringComparison.Ordinal);
        report.BaselineGateReason =
            "verdict="
              + report.BaselineGateVerdict
              + ", victoryGain="
              + report.ChampionVictoryGain
              + ", paired="
              + report.ChampionOnlyWins
              + ":"
              + report.BaselineOnlyWins
              + ", pairedWilson="
              + report.PairedWinWilsonLowerBound.ToString(
                  "0.###",
                  System.Globalization.CultureInfo.InvariantCulture)
              + ".."
              + report.PairedWinWilsonUpperBound.ToString(
                  "0.###",
                  System.Globalization.CultureInfo.InvariantCulture)
              + ", pairedLossMedianDepthGain="
              + report.PairedLossMedianDepthGain.ToString(
                  "0.###",
                  System.Globalization.CultureInfo.InvariantCulture)
              + ", pairedLossPairs="
              + report.PairedLossPairs
              + ", depthEvidence="
              + (report.DepthGainEvidencePassed ? "pass" : "insufficient")
              + "(minimumGain="
              + minimumDepthGain.ToString(
                  "0.###",
                  System.Globalization.CultureInfo.InvariantCulture)
              + ", minimumPairs="
              + minimumDepthPairs
              + ")"
              + ", aggregateDepthGain="
              + report.ChampionDepthGain.ToString(
                  "0.###",
                  System.Globalization.CultureInfo.InvariantCulture)
              + "; deployment="
              + FormatCapabilityProbeArm(champion)
              + "; baseline="
              + FormatCapabilityProbeArm(baseline)
              + (teacher == null
                  ? ""
                  : "; teacher-hard=" + FormatCapabilityProbeArm(teacher));
    }

    private static bool CrediblePairedRegression(
        int championOnlyWins,
        int baselineOnlyWins)
    {
        var discordant = Math.Max(
            0,
            championOnlyWins + baselineOnlyWins);
        return discordant > 0
               && CombatFoundationCurriculum.WilsonLowerBound(
                   baselineOnlyWins,
                   discordant) > 0.5d;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0d;
        }
        var ordered = values.OrderBy(item => item).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static string FormatCapabilityProbeArm(
        CombatFoundationCapabilityProbeArm arm)
    {
        return "normal "
               + arm.NormalVictories
               + "/"
               + arm.NormalCampaigns
               + ", advanced "
               + arm.AdvancedVictories
               + "/"
               + arm.AdvancedCampaigns
               + ", depth "
               + arm.AverageCompletedBattles.ToString(
                   "0.###",
                   System.Globalization.CultureInfo.InvariantCulture)
               + ", invalid "
               + arm.InvalidCampaigns;
    }

    private static int CountTerminalConsistencyViolations(
        CombatCampaignResult campaign)
    {
        return campaign?.Battles.Count(battle =>
            !battle.TerminalConsistencyValid) ?? 0;
    }

    private static int SanitizeEpisodeFeatures(
        IEnumerable<CombatEpisode> episodes)
    {
        var removed = 0;
        foreach (var frame in (episodes ?? Array.Empty<CombatEpisode>())
                     .SelectMany(episode =>
                         episode.Frames ?? new List<CombatEpisodeFrame>()))
        {
            // Recorder-built compact vectors are sanitized while they are
            // encoded. Avoid materializing their compatibility dictionary
            // merely to repeat the same boundary check.
            if (frame.CompactStateFeatures != null
                && !frame.HasMaterializedStateFeatures)
            {
                continue;
            }
            var forbidden = frame.StateFeatures.Keys
                .Where(key =>
                    !CombatPolicyValueEncoding.IsPermittedStateFeature(key))
                .ToList();
            foreach (var key in forbidden)
            {
                if (frame.StateFeatures.Remove(key))
                {
                    removed++;
                }
            }
        }
        return removed;
    }

    private static void ReleaseTransientEpisodeStorage(
        IEnumerable<CombatEpisode>? episodes,
        ISet<CombatEpisodeFrame>? protectedFrames = null)
    {
        foreach (var frame in (episodes ?? Array.Empty<CombatEpisode>())
                     .Where(episode => episode != null)
                     .SelectMany(episode =>
                         episode.Frames ?? new List<CombatEpisodeFrame>()))
        {
            if (frame != null
                && (protectedFrames == null
                    || !protectedFrames.Contains(frame)))
            {
                frame.ReleaseTransientStorage();
            }
        }
    }

    private static void AddIntegrityFailure(
        ICollection<CombatCampaignFoundationIntegrityFailure> failures,
        IDictionary<string, int> failureCounts,
        CombatCampaignResult campaign)
    {
        var failure = CreateIntegrityFailure(campaign);
        failures.Add(failure);
        foreach (var reason in failure.Reasons)
        {
            failureCounts.TryGetValue(reason, out var count);
            failureCounts[reason] = count + 1;
        }
    }

    private static void AddArenaFailure(
        CombatCampaignFoundationTrainingResult result,
        int iteration,
        string competitor,
        CombatCampaignResult campaign)
    {
        var integrityFailure = CreateIntegrityFailure(campaign);
        result.ArenaFailures.Add(new CombatCampaignFoundationArenaFailure
        {
            Iteration = iteration,
            Competitor = competitor,
            DifficultyId = integrityFailure.DifficultyId,
            WorldSeed = integrityFailure.WorldSeed,
            CompletedBattles = integrityFailure.CompletedBattles,
            Reasons = integrityFailure.Reasons
        });
        foreach (var reason in integrityFailure.Reasons)
        {
            result.ArenaFailureCounts.TryGetValue(reason, out var count);
            result.ArenaFailureCounts[reason] = count + 1;
        }
    }

    private bool RecoverArenaPairs(
        FoundationArenaPair?[] pairs,
        CombatCampaignFoundationTrainingRequest request,
        CombatCampaignFoundationTrainingResult result,
        CombatRuleset ruleset,
        CombatDecisionProfile profile,
        ICombatPolicyValueModel championModel,
        ICombatPolicyValueModel candidateModel,
        FoundationTelemetryTracker telemetry,
        int iteration,
        string stage,
        ulong replacementSeedStart,
        ref int replacementCursor,
        ref int persistentInvalidSides,
        int plannedArenaSides,
        IDictionary<string, HashSet<ulong>> invalidSignatureSeeds,
        ref int completedCampaigns,
        int totalCampaigns,
        CancellationToken cancellationToken)
    {
        if (!request.EnableArenaRecovery)
        {
            return false;
        }
        var systemic = false;
        for (var pairIndex = 0; pairIndex < pairs.Length; pairIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pair = pairs[pairIndex]!;
            RetryInvalidSides(
                pair,
                request,
                result,
                ruleset,
                profile,
                championModel,
                candidateModel,
                telemetry,
                iteration,
                stage,
                ref completedCampaigns,
                totalCampaigns,
                cancellationToken);
            if (!pair.Champion.Invalid && !pair.Candidate.Invalid)
            {
                continue;
            }

            result.ArenaIsolatedPairs++;
            if (pair.Champion.Invalid)
            {
                persistentInvalidSides++;
                AddArenaFailure(result, iteration, "champion", pair.Champion);
                systemic |= RegisterArenaInvalidSignature(
                    result,
                    invalidSignatureSeeds,
                    pair.Champion);
            }
            if (pair.Candidate.Invalid)
            {
                persistentInvalidSides++;
                AddArenaFailure(result, iteration, "candidate", pair.Candidate);
                systemic |= RegisterArenaInvalidSignature(
                    result,
                    invalidSignatureSeeds,
                    pair.Candidate);
            }
            systemic |= persistentInvalidSides
                          / (double)Math.Max(1, plannedArenaSides)
                          > ArenaInvalidRateLimit(request);
            if (systemic)
            {
                continue;
            }

            var replacementAttempts = 0;
            while (replacementAttempts++ < 16)
            {
                var seed = replacementSeedStart
                           + (ulong)Math.Max(0, replacementCursor++);
                CombatCampaignResult? replacementChampion = null;
                CombatCampaignResult? replacementCandidate = null;
                Parallel.Invoke(
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = 2
                    },
                    () => replacementChampion = RunCampaign(
                        request.TrainingCampaign,
                        pair.Champion.DifficultyId,
                        seed,
                        ruleset,
                        new CombatDecisionSimulationPolicyFactory(
                            profile,
                            policyValueModel: championModel),
                        telemetry,
                        "arena-replacement:" + stage + ":champion",
                        cancellationToken),
                    () => replacementCandidate = RunCampaign(
                        request.TrainingCampaign,
                        pair.Candidate.DifficultyId,
                        seed,
                        ruleset,
                        new CombatDecisionSimulationPolicyFactory(
                            profile,
                            policyValueModel: candidateModel),
                        telemetry,
                        "arena-replacement:" + stage + ":candidate",
                        cancellationToken));
                var replacement = new FoundationArenaPair
                {
                    Champion = replacementChampion!,
                    Candidate = replacementCandidate!
                };
                result.ArenaReplacementPairs++;
                ReportProgress(
                    request,
                    telemetry,
                    replacement.Champion,
                    ref completedCampaigns,
                    totalCampaigns,
                    "第 " + iteration + " 轮：竞技场替补冠军");
                ReportProgress(
                    request,
                    telemetry,
                    replacement.Candidate,
                    ref completedCampaigns,
                    totalCampaigns,
                    "第 " + iteration + " 轮：竞技场替补候选");
                RetryInvalidSides(
                    replacement,
                    request,
                    result,
                    ruleset,
                    profile,
                    championModel,
                    candidateModel,
                    telemetry,
                    iteration,
                    stage + ":replacement",
                    ref completedCampaigns,
                    totalCampaigns,
                    cancellationToken);
                if (!replacement.Champion.Invalid
                    && !replacement.Candidate.Invalid)
                {
                    pairs[pairIndex] = replacement;
                    break;
                }
                if (replacement.Champion.Invalid)
                {
                    persistentInvalidSides++;
                    AddArenaFailure(
                        result,
                        iteration,
                        "replacement-champion",
                        replacement.Champion);
                    systemic |= RegisterArenaInvalidSignature(
                        result,
                        invalidSignatureSeeds,
                        replacement.Champion);
                }
                if (replacement.Candidate.Invalid)
                {
                    persistentInvalidSides++;
                    AddArenaFailure(
                        result,
                        iteration,
                        "replacement-candidate",
                        replacement.Candidate);
                    systemic |= RegisterArenaInvalidSignature(
                        result,
                        invalidSignatureSeeds,
                        replacement.Candidate);
                }
                systemic |= persistentInvalidSides
                              / (double)Math.Max(1, plannedArenaSides)
                              > ArenaInvalidRateLimit(request);
                if (systemic)
                {
                    break;
                }
            }
        }
        return systemic;
    }

    private static double ArenaInvalidRateLimit(
        CombatCampaignFoundationTrainingRequest request)
    {
        var configured = request.ArenaInvalidRateLimit;
        return double.IsNaN(configured) || double.IsInfinity(configured)
            ? 0.02d
            : Math.Max(0.0001d, Math.Min(1d, configured));
    }

    private void RetryInvalidSides(
        FoundationArenaPair pair,
        CombatCampaignFoundationTrainingRequest request,
        CombatCampaignFoundationTrainingResult result,
        CombatRuleset ruleset,
        CombatDecisionProfile profile,
        ICombatPolicyValueModel championModel,
        ICombatPolicyValueModel candidateModel,
        FoundationTelemetryTracker telemetry,
        int iteration,
        string stage,
        ref int completedCampaigns,
        int totalCampaigns,
        CancellationToken cancellationToken)
    {
        var retryLimit = Math.Max(
            0,
            Math.Min(3, request.ArenaInvalidRetryCount));
        for (var retry = 0; retry < retryLimit; retry++)
        {
            var retryChampion = pair.Champion.Invalid;
            var retryCandidate = pair.Candidate.Invalid;
            if (!retryChampion && !retryCandidate)
            {
                return;
            }
            result.ArenaRetryAttempts +=
                (retryChampion ? 1 : 0) + (retryCandidate ? 1 : 0);
            CombatCampaignResult? championRetry = null;
            CombatCampaignResult? candidateRetry = null;
            var actions = new List<Action>(2);
            if (retryChampion)
            {
                var difficulty = pair.Champion.DifficultyId;
                var seed = pair.Champion.WorldSeed;
                actions.Add(() => championRetry = RunCampaign(
                    request.TrainingCampaign,
                    difficulty,
                    seed,
                    ruleset,
                    new CombatDecisionSimulationPolicyFactory(
                        profile,
                        policyValueModel: championModel),
                    telemetry,
                    "arena-retry:" + stage + ":champion",
                    cancellationToken));
            }
            if (retryCandidate)
            {
                var difficulty = pair.Candidate.DifficultyId;
                var seed = pair.Candidate.WorldSeed;
                actions.Add(() => candidateRetry = RunCampaign(
                    request.TrainingCampaign,
                    difficulty,
                    seed,
                    ruleset,
                    new CombatDecisionSimulationPolicyFactory(
                        profile,
                        policyValueModel: candidateModel),
                    telemetry,
                    "arena-retry:" + stage + ":candidate",
                    cancellationToken));
            }
            Parallel.Invoke(
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 2
                },
                actions.ToArray());
            if (championRetry != null)
            {
                pair.Champion = championRetry;
                ReportProgress(
                    request,
                    telemetry,
                    pair.Champion,
                    ref completedCampaigns,
                    totalCampaigns,
                    "第 " + iteration + " 轮：竞技场冠军确定性重试");
                if (!pair.Champion.Invalid)
                {
                    result.ArenaRecoveredCampaigns++;
                }
            }
            if (candidateRetry != null)
            {
                pair.Candidate = candidateRetry;
                ReportProgress(
                    request,
                    telemetry,
                    pair.Candidate,
                    ref completedCampaigns,
                    totalCampaigns,
                    "第 " + iteration + " 轮：竞技场候选确定性重试");
                if (!pair.Candidate.Invalid)
                {
                    result.ArenaRecoveredCampaigns++;
                }
            }
        }
    }

    private static bool RegisterArenaInvalidSignature(
        CombatCampaignFoundationTrainingResult result,
        IDictionary<string, HashSet<ulong>> signatureSeeds,
        CombatCampaignResult campaign)
    {
        var failure = CreateIntegrityFailure(campaign);
        var signature = string.Join("|", failure.Reasons);
        if (string.IsNullOrWhiteSpace(signature))
        {
            signature = "campaign:invalid-state";
        }
        if (!signatureSeeds.TryGetValue(signature, out var seeds))
        {
            seeds = new HashSet<ulong>();
            signatureSeeds[signature] = seeds;
        }
        seeds.Add(campaign.WorldSeed);
        result.ArenaInvalidSignatures[signature] = seeds.Count;
        return seeds.Count >= 2;
    }

    private static CombatCampaignFoundationIntegrityFailure
        CreateIntegrityFailure(CombatCampaignResult campaign)
    {
        var reasons = campaign.Battles
            .Where(item => item.Outcome == CombatSimulationOutcome.Invalid)
            .SelectMany(item => new[]
            {
                "battle:" + item.TerminationReason,
                "battle-scenario:" + item.ScenarioId
            })
            .Concat(campaign.Battles.SelectMany(item =>
                item.UnsupportedDefinitions.Select(definition =>
                    "unsupported:" + definition)))
            .Concat(campaign.Battles
                .Where(item => item.Outcome == CombatSimulationOutcome.Invalid
                               && !string.IsNullOrWhiteSpace(
                                   item.FailureDiagnostics.LimitScope))
                .SelectMany(item => new[]
                    {
                        "command-limit-scope:"
                        + item.FailureDiagnostics.LimitScope,
                        "command-limit-action:"
                        + item.FailureDiagnostics.ActionDefinitionId,
                        "command-limit-pending:"
                        + item.FailureDiagnostics.PendingCommand,
                        "command-limit-count:"
                        + item.FailureDiagnostics.TotalCommandCount
                        + "/"
                        + item.FailureDiagnostics.ActionCommandCount
                    }
                    .Concat(item.FailureDiagnostics.RecentCommands.Select(
                        command => "command-recent:" + command))
                    .Concat(item.FailureDiagnostics.StateSummary.Select(
                        summary => "failure-state:" + summary))))
            .Concat(campaign.UnsupportedDefinitions.Select(definition =>
                "campaign:" + definition))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        if (reasons.Count == 0)
        {
            reasons.Add("campaign:invalid-state");
        }
        return new CombatCampaignFoundationIntegrityFailure
        {
            DifficultyId = campaign.DifficultyId,
            WorldSeed = campaign.WorldSeed,
            CompletedBattles = campaign.CompletedBattles,
            Reasons = reasons
        };
    }

    private static string FormatIntegrityFailureSummary(
        IEnumerable<CombatCampaignFoundationIntegrityFailure> failures,
        int maximumFailures)
    {
        return string.Join(
            "；",
            failures.Take(Math.Max(1, maximumFailures)).Select(failure =>
            {
                var reason = failure.Reasons.FirstOrDefault()
                             ?? "campaign:invalid-state";
                if (reason.Length > 180)
                {
                    reason = reason.Substring(0, 180) + "...";
                }
                return failure.DifficultyId
                       + "/"
                       + failure.WorldSeed
                       + "@"
                       + failure.CompletedBattles
                       + "："
                       + reason;
            }));
    }

    private static string FormatArenaFailureSummary(
        IEnumerable<CombatCampaignFoundationArenaFailure> failures,
        int maximumFailures)
    {
        return string.Join(
            "；",
            failures.Take(Math.Max(1, maximumFailures)).Select(failure =>
            {
                var reason = failure.Reasons.FirstOrDefault()
                             ?? "campaign:invalid-state";
                if (reason.Length > 180)
                {
                    reason = reason.Substring(0, 180) + "...";
                }
                return "iteration="
                       + failure.Iteration
                       + ", competitor="
                       + failure.Competitor
                       + ", difficulty="
                       + failure.DifficultyId
                       + ", seed="
                       + failure.WorldSeed
                       + ", completedBattles="
                       + failure.CompletedBattles
                       + ", reason="
                       + reason;
            }));
    }

    internal static void ApplyCampaignTargets(
        IReadOnlyList<CombatEpisode> episodes,
        CombatCampaignResult campaign,
        string curriculumStage,
        int trainingIteration)
    {
        var totalBattles = Math.Max(
            1,
            campaign.TotalBattles > 0
                ? campaign.TotalBattles
                : Math.Max(campaign.CompletedBattles, episodes.Count));
        var failureEpisodeIndex = campaign.FinalBossVictory
            ? -1
            : Math.Max(
                0,
                Math.Min(
                    episodes.Count - 1,
                    campaign.CompletedBattles - 1));
        var terminalDoomPower = 0;
        if (campaign.FinalState.SpecialVariables.TryGetValue(
                "DoomPower",
                out var terminalDoomText))
        {
            _ = int.TryParse(terminalDoomText, out terminalDoomPower);
        }
        for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
        {
            var episode = episodes[episodeIndex];
            var battle = episodeIndex < campaign.Battles.Count
                ? campaign.Battles[episodeIndex]
                : null;
            var localVictory =
                battle?.Outcome == CombatSimulationOutcome.Victory;
            episode.Campaign ??= new CombatCampaignEpisodeMetadata();
            episode.Campaign.WorldSeed = campaign.WorldSeed;
            episode.Campaign.DifficultyId = campaign.DifficultyId;
            episode.Campaign.FinalBossVictory = campaign.FinalBossVictory;
            episode.Campaign.ReachedFinalBoss = campaign.ReachedFinalBoss;
            episode.Campaign.CampaignCompletedBattles =
                campaign.CompletedBattles;
            episode.Campaign.CampaignTotalBattles = totalBattles;
            episode.Campaign.FailureBattleIndex =
                campaign.FinalBossVictory
                    ? -1
                    : Math.Max(0, campaign.CompletedBattles - 1);
            episode.Campaign.TerminalScenarioId =
                campaign.Battles.LastOrDefault()?.ScenarioId ?? "";
            episode.Campaign.OutcomeClass = campaign.Invalid
                ? "invalid"
                : localVictory || campaign.FinalBossVictory
                    ? campaign.FinalBossVictory
                        ? "victory"
                        : "battle-victory"
                    : "defeat";
            episode.Campaign.TerminalSnapshotKnown = true;
            episode.Campaign.TerminalBattleIndex = Math.Max(
                0,
                campaign.CompletedBattles - 1);
            episode.Campaign.TerminalPlayerHp = Math.Max(
                0,
                campaign.FinalState.CurrentHp);
            episode.Campaign.TerminalPlayerMaxHp = Math.Max(
                1,
                campaign.FinalState.MaxHp);
            episode.Campaign.TerminalDoomPower = Math.Max(
                0,
                terminalDoomPower);
            episode.Campaign.CurriculumStage = curriculumStage ?? "";
            episode.Campaign.TrainingIteration =
                Math.Max(0, trainingIteration);
            episode.Campaign.IntegrityValid =
                !campaign.Invalid
                && campaign.Battles.All(battle =>
                    battle.TerminalConsistencyValid);
            var distanceFromFailure = failureEpisodeIndex < 0
                ? Math.Max(0, episodes.Count - episodeIndex - 1)
                : Math.Max(0, failureEpisodeIndex - episodeIndex);
            var journeySignal = campaign.FinalBossVictory
                ? 0.65d
                  + 0.35d
                  * (episodeIndex + 1d)
                  / Math.Max(1d, episodes.Count)
                : localVictory
                    ? CombatFoundationTerminalCreditProtocol.WonBattleCredit
                      - 0.75d
                      * Math.Pow(
                          CombatFoundationTerminalCreditProtocol
                              .FailureBackpropagationDecay,
                          distanceFromFailure)
                    : -1d;
            journeySignal = Math.Max(-1d, Math.Min(1d, journeySignal));
            for (var frameIndex = 0;
                 frameIndex < episode.Frames.Count;
                 frameIndex++)
            {
                var frame = episode.Frames[frameIndex];
                if (!campaign.FinalBossVictory && !localVictory)
                {
                    var terminalProgress = (frameIndex + 1d)
                                           / Math.Max(
                                               1d,
                                               episode.Frames.Count);
                    frame.LongTermReturn =
                        -0.75d - 0.25d * terminalProgress;
                }
                else
                {
                    frame.LongTermReturn = journeySignal;
                }
            }
        }
    }

    internal static void ApplyHardEncounterTargets(
        IReadOnlyList<CombatEpisode> episodes,
        CombatCampaignResult campaign,
        string curriculumStage,
        int trainingIteration)
    {
        var battle = campaign.Battles.LastOrDefault();
        var victory =
            battle?.Outcome == CombatSimulationOutcome.Victory;
        foreach (var episode in episodes)
        {
            episode.Campaign ??= new CombatCampaignEpisodeMetadata();
            episode.Campaign.WorldSeed = campaign.WorldSeed;
            episode.Campaign.DifficultyId = campaign.DifficultyId;
            episode.Campaign.FinalBossVictory = false;
            episode.Campaign.ReachedFinalBoss = false;
            episode.Campaign.CampaignCompletedBattles = 1;
            episode.Campaign.CampaignTotalBattles = 1;
            episode.Campaign.FailureBattleIndex =
                victory ? -1 : episode.JourneyBattleIndex;
            episode.Campaign.TerminalScenarioId =
                battle?.ScenarioId ?? "";
            episode.Campaign.OutcomeClass = victory
                ? "encounter-victory"
                : campaign.Invalid
                    ? "invalid"
                    : "encounter-defeat";
            episode.Campaign.TerminalSnapshotKnown = false;
            episode.Campaign.TerminalBattleIndex = -1;
            episode.Campaign.TerminalPlayerHp = 0;
            episode.Campaign.TerminalPlayerMaxHp = 0;
            episode.Campaign.TerminalDoomPower = 0;
            episode.Campaign.CurriculumStage =
                (curriculumStage ?? "") + ":hard-encounter";
            episode.Campaign.TrainingIteration =
                Math.Max(0, trainingIteration);
            episode.Campaign.IntegrityValid =
                !campaign.Invalid
                && campaign.Battles.All(item =>
                    item.TerminalConsistencyValid);
            foreach (var frame in episode.Frames)
            {
                frame.LongTermReturn = victory ? 0.75d : -1d;
            }
        }
    }

    private static void ApplyImprovedCounterfactualTargets(
        IReadOnlyList<CombatEpisode> episodes)
    {
        foreach (var episode in episodes)
        {
            episode.Campaign ??= new CombatCampaignEpisodeMetadata();
            episode.Campaign.OutcomeClass =
                "encounter-improved-defeat";
            episode.Campaign.CurriculumStage =
                (episode.Campaign.CurriculumStage ?? "")
                + ":improved";
            episode.Campaign.TrainingWeight =
                CombatFoundationCounterfactualProtocol
                    .ImprovedEpisodeWeight;
            foreach (var frame in episode.Frames)
            {
                frame.LongTermReturn = Math.Max(
                    frame.LongTermReturn,
                    -0.50d);
            }
        }
    }

    internal static CombatFoundationCounterfactualAdmission
        ClassifyCounterfactual(
            CombatCampaignResult baseline,
            CombatCampaignResult counterfactual)
    {
        if (baseline == null
            || counterfactual == null
            || counterfactual.Invalid)
        {
            return CombatFoundationCounterfactualAdmission.Rejected;
        }
        var counterfactualBattle =
            counterfactual.Battles.LastOrDefault();
        if (counterfactualBattle?.Outcome
            == CombatSimulationOutcome.Victory)
        {
            return CombatFoundationCounterfactualAdmission.Victory;
        }
        var baselineBattle = baseline.Battles.LastOrDefault();
        if (baselineBattle == null
            || counterfactualBattle == null
            || baselineBattle.Outcome == CombatSimulationOutcome.Victory)
        {
            return CombatFoundationCounterfactualAdmission.Rejected;
        }
        var baselineDamage = Math.Max(
            0,
            baselineBattle.Metrics?.DamageDealt ?? 0);
        var counterfactualDamage = Math.Max(
            0,
            counterfactualBattle.Metrics?.DamageDealt ?? 0);
        var requiredDamageGain = Math.Max(
            CombatFoundationCounterfactualProtocol
                .MinimumDamageImprovement,
            (int)Math.Ceiling(
                baselineDamage
                * CombatFoundationCounterfactualProtocol
                    .MinimumDamageImprovementRatio));
        return counterfactualDamage - baselineDamage >= requiredDamageGain
               && counterfactualBattle.Turns >= baselineBattle.Turns
            ? CombatFoundationCounterfactualAdmission.Improved
            : CombatFoundationCounterfactualAdmission.Rejected;
    }

    private static List<CombatEpisode> EnsureModelSelectionAnchor(
        CombatCampaignFoundationTrainingRequest request,
        IReadOnlyList<CombatEpisode> replay)
    {
        var existing = (request.ModelSelectionAnchorEpisodes
                        ?? new List<CombatEpisode>())
            .Where(episode => episode != null
                              && (episode.Frames?.Count ?? 0) > 0)
            .ToList();
        if (existing.Count > 0)
        {
            return existing;
        }
        var groups = (replay ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode != null
                              && episode.Authoritative
                              && (episode.Campaign?.IntegrityValid ?? true)
                              && (episode.Frames?.Count ?? 0) > 0)
            .GroupBy(ModelSelectionRunKey, StringComparer.Ordinal)
            .OrderBy(group => StableModelSelectionHash(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        if (groups.Count < 10)
        {
            return existing;
        }
        var targetRuns = Math.Min(64, Math.Max(2, groups.Count / 10));
        existing = groups
            .Take(targetRuns)
            .SelectMany(group => group)
            .OrderBy(ModelSelectionRunKey, StringComparer.Ordinal)
            .ThenBy(episode => episode.JourneyBattleIndex)
            .ThenBy(episode => episode.EpisodeId, StringComparer.Ordinal)
            .ToList();
        request.ModelSelectionAnchorEpisodes = existing;
        try
        {
            request.ModelSelectionAnchorCreated?.Invoke(existing);
        }
        catch
        {
            // Anchor persistence is diagnostic; the in-memory split remains valid.
        }
        return existing;
    }

    private static string ModelSelectionRunKey(CombatEpisode episode)
    {
        return string.IsNullOrWhiteSpace(episode.JourneyRunId)
            ? "episode:" + (episode.EpisodeId ?? "")
            : "journey:" + episode.JourneyRunId;
    }

    private static uint StableModelSelectionHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    internal static double EffectiveHardSeedReplayShare(
        CombatCampaignFoundationTrainingRequest request,
        IReadOnlyList<CombatCampaignFoundationIteration> iterations)
    {
        var configured = Math.Max(
            0d,
            Math.Min(0.75d, request.HardSeedReplayShare));
        var all = iterations
                  ?? Array.Empty<CombatCampaignFoundationIteration>();
        var recent = all
            .Skip(Math.Max(
                0,
                all.Count
                - CombatFoundationStagnationProtocol
                    .HardSeedSolveRateWindow))
            .ToList();
        var latestChampionArena = all.LastOrDefault(item =>
            item.Promoted && item.ValidAdvancedArenaPairs > 0);
        var acceptance = double.IsNaN(request.AdvancedAcceptanceRate)
                         || double.IsInfinity(
                             request.AdvancedAcceptanceRate)
            ? 0.30d
            : Math.Max(
                0d,
                Math.Min(1d, request.AdvancedAcceptanceRate));
        if (latestChampionArena == null
            || latestChampionArena.CandidateAdvancedWinRate + 0.0000001d
               < acceptance)
        {
            return configured;
        }
        var attempts = recent.Sum(item =>
            item.HardSeedCounterfactualCampaigns);
        var solved = recent.Sum(item =>
            item.HardSeedCounterfactualVictories
            + item.HardSeedCounterfactualImprovements);
        if (attempts < 8
            || solved / (double)Math.Max(1, attempts)
            >= CombatFoundationStagnationProtocol.MinimumHardSeedSolveRate)
        {
            return configured;
        }
        return Math.Min(
            configured,
            CombatFoundationStagnationProtocol.ReducedHardSeedReplayShare);
    }

    private static int ConsecutiveRejectedIterations(
        IReadOnlyList<CombatCampaignFoundationIteration> iterations,
        int startIndex = 0)
    {
        var count = 0;
        var floor = Math.Max(0, Math.Min(iterations.Count, startIndex));
        for (var index = iterations.Count - 1; index >= floor; index--)
        {
            if (iterations[index].TrainingOnlyIteration)
            {
                continue;
            }
            if (iterations[index].WorkingModelAccepted)
            {
                break;
            }
            count++;
        }
        return count;
    }

    private static int ConsecutiveUnproductiveIterations(
        IReadOnlyList<CombatCampaignFoundationIteration> iterations,
        int startIndex = 0)
    {
        var count = 0;
        var floor = Math.Max(0, Math.Min(iterations.Count, startIndex));
        for (var index = iterations.Count - 1; index >= floor; index--)
        {
            var iteration = iterations[index];
            if (iteration.TrainingOnlyIteration)
            {
                continue;
            }
            if (iteration.WorkingModelAccepted || iteration.ProductiveProgress)
            {
                break;
            }
            count++;
        }
        return count;
    }

    private static int ConsecutiveDataOnlyIterations(
        IReadOnlyList<CombatCampaignFoundationIteration> iterations,
        int startIndex = 0)
    {
        var count = 0;
        var floor = Math.Max(0, Math.Min(iterations.Count, startIndex));
        for (var index = iterations.Count - 1; index >= floor; index--)
        {
            var iteration = iterations[index];
            if (iteration.TrainingOnlyIteration)
            {
                continue;
            }
            if (iteration.WorkingModelAccepted
                || iteration.BehavioralProductiveProgress
                || !iteration.DataPipelineProgress)
            {
                break;
            }
            count++;
        }
        return count;
    }

    internal static IReadOnlyList<string> ProductiveProgressReasons(
        CombatCampaignFoundationIteration current,
        IReadOnlyList<CombatCampaignFoundationIteration>? previous)
    {
        if (current == null)
        {
            return Array.Empty<string>();
        }
        var history = previous
                      ?? Array.Empty<CombatCampaignFoundationIteration>();
        var reasons = new List<string>();
        if (current.WorkingModelAccepted)
        {
            reasons.Add("working-model-accepted");
        }
        if (current.ParetoProgress)
        {
            reasons.Add("pareto-frontier");
        }
        AddFirstGatePass(
            reasons,
            "arena-evidence-first-pass",
            current.ArenaEvidenceGatePassed,
            history.Any(item => item.ArenaEvidenceGatePassed));
        AddFirstGatePass(
            reasons,
            "advanced-absolute-first-pass",
            current.AbsoluteAdvancedGatePassed,
            history.Any(item => item.AbsoluteAdvancedGatePassed));
        AddFirstGatePass(
            reasons,
            "offline-head-first-pass",
            current.OfflineHeadRegressionGatePassed,
            history.Any(item => item.OfflineHeadRegressionGatePassed));
        AddFirstGatePass(
            reasons,
            "strategy-quota-first-pass",
            current.StrategyQuotaGatePassed,
            history.Any(item => item.StrategyQuotaGatePassed));
        AddFirstGatePass(
            reasons,
            "feature-collision-first-pass",
            current.FeatureCollisionGatePassed,
            history.Any(item => item.FeatureCollisionGatePassed));

        var priorIteration = history.LastOrDefault();
        var priorShortfall = priorIteration == null
            ? 0
            : StrategyQuotaShortfallTotal(
                priorIteration.TeacherStudentPoolQuotaShortfalls);
        var currentShortfall = StrategyQuotaShortfallTotal(
            current.TeacherStudentPoolQuotaShortfalls);
        if (priorShortfall > 0
            && currentShortfall < priorShortfall
            && (priorShortfall - currentShortfall) / (double)priorShortfall
            + 0.0000001d
            >= CombatFoundationStagnationProtocol
                .MinimumStrategyQuotaImprovementRatio)
        {
            reasons.Add("strategy-quota-improved");
        }

        var priorValidation = history
            .Where(item => MetricAvailable(item.ModelValidationMetrics))
            .Select(item => item.ModelValidationMetrics)
            .OrderBy(item => item.CompositeLoss)
            .FirstOrDefault();
        if (priorValidation != null
            && MetricAvailable(current.ModelValidationMetrics)
            && ValidationLossImproved(
                priorValidation,
                current.ModelValidationMetrics))
        {
            reasons.Add("validation-loss-improved");
        }

        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> DataPipelineProgressReasons(
        CombatCampaignFoundationIteration current,
        IReadOnlyList<CombatCampaignFoundationIteration>? previous)
    {
        if (current == null)
        {
            return Array.Empty<string>();
        }
        var history = previous
                      ?? Array.Empty<CombatCampaignFoundationIteration>();
        var reasons = new List<string>();
        var priorTeacherGeneration = history.Count == 0
            ? 0
            : history.Max(item => item.TransformerTeacher?.TeacherGeneration
                                  ?? 0);
        if (current.TransformerTeacher?.Applied == true
            && current.TransformerTeacher.TeacherGeneration
            > priorTeacherGeneration)
        {
            reasons.Add("teacher-generation-advanced");
        }
        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static bool ParetoFrontierProgress(
        CombatCampaignFoundationIteration current,
        IReadOnlyList<CombatCampaignFoundationIteration>? previous)
    {
        if (current == null
            || current.ValidNormalArenaPairs <= 0
            || current.ValidAdvancedArenaPairs <= 0
            || !current.OfflineHeadRegressionGatePassed
            || !current.FeatureCollisionGatePassed)
        {
            return false;
        }
        var history = (previous
                       ?? Array.Empty<CombatCampaignFoundationIteration>())
            .Where(item => item.ValidNormalArenaPairs > 0
                           && item.ValidAdvancedArenaPairs > 0
                           && item.OfflineHeadRegressionGatePassed
                           && item.FeatureCollisionGatePassed)
            .ToList();
        return history.Count == 0
               || !history.Any(item =>
                   item.CandidateNormalWinRate + 0.0000001d
                   >= current.CandidateNormalWinRate
                   && item.CandidateAdvancedWinRate + 0.0000001d
                   >= current.CandidateAdvancedWinRate);
    }

    internal static string PreferredWorkingModelSlot(string? curriculumStage)
    {
        var stage = (curriculumStage ?? "").Trim().ToLowerInvariant();
        if (stage.IndexOf("advanced", StringComparison.Ordinal) >= 0
            || stage.IndexOf("dual", StringComparison.Ordinal) >= 0)
        {
            return "advanced-best";
        }
        return stage.IndexOf("normal", StringComparison.Ordinal) >= 0
            ? "normal-best"
            : "balanced-best";
    }

    private static void AddFirstGatePass(
        ICollection<string> reasons,
        string reason,
        bool currentPassed,
        bool previouslyPassed)
    {
        if (currentPassed && !previouslyPassed)
        {
            reasons.Add(reason);
        }
    }

    private static bool ValidationLossImproved(
        CombatPolicyValueMetricSnapshot previous,
        CombatPolicyValueMetricSnapshot current)
    {
        if (current.CompositeLoss >= previous.CompositeLoss)
        {
            return false;
        }
        if (current.CompositeLossCiUpper > 0d
            && previous.CompositeLossCiLower > 0d)
        {
            return current.CompositeLossCiUpper + 0.0000001d
                   < previous.CompositeLossCiLower;
        }
        return (previous.CompositeLoss - current.CompositeLoss)
               / Math.Max(0.0000001d, previous.CompositeLoss)
               + 0.0000001d
               >= CombatFoundationStagnationProtocol
                   .MinimumValidationLossImprovementRatio;
    }

    internal static bool ShouldStopForStagnation(
        CombatCampaignFoundationTrainingRequest request,
        IReadOnlyList<CombatCampaignFoundationIteration> iterations,
        bool hasChampion,
        int startIndex = 0)
    {
        if (!hasChampion)
        {
            return false;
        }
        var limit = Math.Max(
            0,
            request.MaximumConsecutiveRejectedIterations);
        if (limit <= 0)
        {
            return false;
        }
        var history = iterations
                      ?? Array.Empty<CombatCampaignFoundationIteration>();
        if (history.LastOrDefault()?.TrainingOnlyIteration == true)
        {
            return false;
        }
        var dataOnlyLimitReached = ConsecutiveDataOnlyIterations(
                                       history,
                                       startIndex)
                                   >= CombatFoundationStagnationProtocol
                                       .MaximumConsecutiveDataOnlyIterations;
        return dataOnlyLimitReached
               || ConsecutiveUnproductiveIterations(history, startIndex)
                  >= limit;
    }

    private static bool TrainingObjectiveVictory(
        FoundationTrainingCampaignRun trainingRun)
    {
        return trainingRun.LocalEncounter
            ? trainingRun.Campaign.Battles.LastOrDefault()?.Outcome
              == CombatSimulationOutcome.Victory
            : trainingRun.Campaign.FinalBossVictory;
    }

    internal static bool ShouldRunCounterfactualHardEncounter(
        CombatCampaignFoundationTrainingRequest request,
        bool localEncounter,
        CombatCampaignResult campaign)
    {
        return request.EnableCounterfactualHardEncounters
               && localEncounter
               && !campaign.Invalid
               && campaign.Battles.LastOrDefault()?.Outcome
               != CombatSimulationOutcome.Victory;
    }

    internal static void ApplyAdvancedTrainingFloor(
        CombatFoundationCurriculum.Plan plan,
        double configuredFloor)
    {
        var floor = double.IsNaN(configuredFloor)
                    || double.IsInfinity(configuredFloor)
            ? 0.35d
            : Math.Max(0d, Math.Min(0.60d, configuredFloor));
        if (floor <= 0d || plan.AdvancedShare >= floor)
        {
            return;
        }
        plan.AdvancedShare = floor;
        plan.MinimumAdvancedShare = Math.Max(
            plan.MinimumAdvancedShare,
            floor);
        plan.MaximumAdvancedShare = Math.Max(
            plan.MaximumAdvancedShare,
            floor);
    }

    internal static double EffectiveAdvancedTrainingFloor(
        double configuredFloor,
        CombatFoundationExpertReplaySelection? expertReplay)
    {
        var floor = double.IsNaN(configuredFloor)
                    || double.IsInfinity(configuredFloor)
            ? 0.35d
            : Math.Max(0d, Math.Min(0.60d, configuredFloor));
        if (expertReplay == null
            || !expertReplay.QuotaShortfalls.TryGetValue(
                "advanced",
                out var shortfall)
            || shortfall <= 0)
        {
            return floor;
        }
        var selected = Math.Max(
            1,
            expertReplay.SelectedNormalEpisodes
            + expertReplay.SelectedAdvancedEpisodes);
        var shortageShare = Math.Min(0.25d, shortfall / (double)selected);
        return Math.Min(0.60d, floor + shortageShare);
    }

    private static void UpdateHardSeedHistory(
        IList<CombatFoundationHardSeedHistoryEntry> history,
        CombatCampaignResult campaign,
        CombatFoundationTrainingSlot? slot,
        int iteration,
        CombatCampaignCheckpoint? failureEncounterCheckpoint = null,
        bool localEncounter = false)
    {
        if (campaign == null || campaign.Invalid || campaign.WorldSeed == 0UL)
        {
            return;
        }
        var difficulty = string.Equals(
            campaign.DifficultyId,
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
        var existing = history.FirstOrDefault(item =>
            item.WorldSeed == campaign.WorldSeed
            && string.Equals(
                item.DifficultyId,
                difficulty,
                StringComparison.Ordinal));
        if (existing == null)
        {
            existing = new CombatFoundationHardSeedHistoryEntry
            {
                WorldSeed = campaign.WorldSeed,
                DifficultyId = difficulty,
                FirstSeenIteration = Math.Max(1, iteration)
            };
            history.Add(existing);
        }
        var objectiveVictory = localEncounter
            ? campaign.Battles.LastOrDefault()?.Outcome
              == CombatSimulationOutcome.Victory
            : campaign.FinalBossVictory;
        existing.CompletedBattles = Math.Max(
            existing.CompletedBattles,
            localEncounter
                ? (slot?.FailureEncounterCheckpoint?.NextEncounterIndex ?? 0)
                  + campaign.CompletedBattles
                : campaign.CompletedBattles);
        existing.LastSeenIteration = Math.Max(
            existing.LastSeenIteration,
            iteration);
        if (slot?.HardSeed == true)
        {
            existing.TrainingAttempts++;
            existing.LastTrainedIteration = iteration;
        }
        if (objectiveVictory)
        {
            if (slot?.HardSeed == true)
            {
                existing.RecoverySuccesses++;
                existing.Resolved = true;
            }
            return;
        }
        existing.FailureOccurrences++;
        existing.Resolved = false;
        existing.TerminalScenarioId =
            campaign.Battles.LastOrDefault()?.ScenarioId
            ?? slot?.FailureCluster
            ?? existing.TerminalScenarioId;
        if (!localEncounter && failureEncounterCheckpoint != null)
        {
            existing.FailureEncounterCheckpoint =
                failureEncounterCheckpoint;
        }
    }

    private static void UpdateHardSeedSolvability(
        IList<CombatFoundationHardSeedHistoryEntry> history,
        CombatCampaignResult campaign,
        CombatFoundationCounterfactualAdmission admission)
    {
        var difficulty = string.Equals(
            campaign.DifficultyId,
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
        var entry = history.FirstOrDefault(item =>
            item.WorldSeed == campaign.WorldSeed
            && string.Equals(
                item.DifficultyId,
                difficulty,
                StringComparison.Ordinal));
        if (entry == null)
        {
            return;
        }
        entry.CounterfactualAttempts++;
        if (admission != CombatFoundationCounterfactualAdmission.Rejected)
        {
            entry.CounterfactualAccepted++;
        }
        entry.SolvabilityClass = admission switch
        {
            CombatFoundationCounterfactualAdmission.Victory =>
                "action-solvable",
            CombatFoundationCounterfactualAdmission.Improved =>
                "action-sensitive",
            _ when entry.CounterfactualAttempts >= 2
                   && entry.CounterfactualAccepted == 0 =>
                "build-limited",
            _ when entry.TrainingAttempts >= 2
                   && entry.CounterfactualAccepted == 0 =>
                "build-limited-provisional",
            _ => "unknown"
        };
    }

    private TuningSelection SelectTunedCandidate(
        CombatPolicyValueTrainingResult trained,
        CombatCampaignFoundationTrainingResult foundationResult,
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        CombatDecisionProfile profile,
        ulong tuningSeedStart,
        int iteration,
        int normalCampaigns,
        int advancedCampaigns,
        int parallelism,
        FoundationTelemetryTracker telemetry,
        ref int completedCampaigns,
        int totalCampaigns,
        CancellationToken cancellationToken)
    {
        var candidates = (trained.CandidateModels
                          ?? new List<CombatPolicyValueModelCandidate>())
            .Where(item => item?.Model != null)
            .OrderBy(item => item.ValidationLoss)
            .ThenBy(item => item.Epoch)
            .Take(Math.Max(1, request.Training.RetainedModelCandidates))
            .ToList();
        if (candidates.Count == 0 && trained.Model != null)
        {
            candidates.Add(new CombatPolicyValueModelCandidate
            {
                Epoch = trained.BestEpoch,
                ValidationLoss = trained.Model.Metrics.TryGetValue(
                    "validationCompositeLoss",
                    out var validationLoss)
                    ? validationLoss
                    : double.MaxValue,
                Model = trained.Model,
                TrainingMetrics =
                    CloneMetricSnapshot(trained.TrainingMetrics),
                ValidationMetrics =
                    CloneMetricSnapshot(trained.ValidationMetrics),
                TestMetrics = CloneMetricSnapshot(trained.TestMetrics)
            });
        }
        var originalCandidateCount = candidates.Count;
        var offlineRejectedCandidates = 0;
        var allCandidatesRejectedOffline = false;
        if (request.EnableOfflineTuningGate && candidates.Count > 0)
        {
            var rankedCandidates = candidates;
            candidates = rankedCandidates
                .Where(candidate => OfflineHeadRegressionPassed(
                    trained.BaselineValidationMetrics,
                    candidate.ValidationMetrics,
                    request.MaximumOfflineHeadRegression))
                .ToList();
            offlineRejectedCandidates = Math.Max(
                0,
                rankedCandidates.Count - candidates.Count);
            allCandidatesRejectedOffline = candidates.Count == 0;
            if (allCandidatesRejectedOffline)
            {
                // Keep one model only as a diagnostic/training-continuation
                // object. The explicit flag prevents tuning and Arena from
                // treating it as a promotable business candidate.
                candidates = rankedCandidates.Take(1).ToList();
            }
            else if (candidates.Count > 1)
            {
                var bestOffline = candidates[0];
                candidates = candidates
                    .Where(candidate => ReferenceEquals(candidate, bestOffline)
                                        || !OfflineDominated(
                                            candidate,
                                            bestOffline))
                    .ToList();
                offlineRejectedCandidates = Math.Max(
                    0,
                    originalCandidateCount - candidates.Count);
            }
        }
        if (!request.EnableTuningArena
            || normalCampaigns + advancedCampaigns <= 0
            || candidates.Count <= 1)
        {
            var only = candidates.First();
            return new TuningSelection
            {
                Model = CombatPolicyValueBatchTrainer.Clone(only.Model),
                Epoch = only.Epoch,
                Score = -only.ValidationLoss,
                CandidateCount = originalCandidateCount,
                OfflineRejectedCandidates = offlineRejectedCandidates,
                AllCandidatesRejectedOffline = allCandidatesRejectedOffline,
                FinalistCount = candidates.Count,
                EvaluationRan = false,
                CampaignsSaved = offlineRejectedCandidates
                                 * (normalCampaigns + advancedCampaigns),
                TrainingMetrics =
                    CloneMetricSnapshot(only.TrainingMetrics),
                ValidationMetrics =
                    CloneMetricSnapshot(only.ValidationMetrics),
                TestMetrics = CloneMetricSnapshot(only.TestMetrics)
            };
        }

        var seedStride = (ulong)Math.Max(
            1,
            normalCampaigns + advancedCampaigns);
        var iterationSeedStart = tuningSeedStart
                                 + (ulong)Math.Max(0, iteration)
                                 * seedStride;
        telemetry.BeginPhase("tuning");
        var campaignCount = normalCampaigns + advancedCampaigns;
        var models = candidates
            .Select(candidate => CreateParallelPolicyValueModel(
                candidate.Model,
                request,
                parallelism,
                competingModelCount: candidates.Count))
            .ToArray();
        var runsByCandidate = candidates
            .Select(_ => new CombatCampaignResult?[campaignCount])
            .ToArray();
        var tuningCompletedCampaigns = completedCampaigns;
        var screeningNormal = request.EnableProgressiveTuning
            ? Math.Max(
                0,
                Math.Min(
                    normalCampaigns,
                    request.TuningScreeningNormalCampaigns))
            : normalCampaigns;
        var screeningAdvanced = request.EnableProgressiveTuning
            ? Math.Max(
                0,
                Math.Min(
                    advancedCampaigns,
                    request.TuningScreeningAdvancedCampaigns))
            : advancedCampaigns;
        var requestedFinalists = Math.Max(
            1,
            Math.Min(candidates.Count, request.TuningFinalistCount));
        var progressive = request.EnableProgressiveTuning
                          && requestedFinalists < candidates.Count
                          && (screeningNormal < normalCampaigns
                              || screeningAdvanced < advancedCampaigns)
                          && screeningNormal + screeningAdvanced > 0;
        var screeningIndices = progressive
            ? Enumerable.Range(0, screeningNormal)
                .Concat(Enumerable.Range(
                    normalCampaigns,
                    screeningAdvanced))
                .ToArray()
            : Enumerable.Range(0, campaignCount).ToArray();
        RunTuningStage(
            Enumerable.Range(0, candidates.Count).ToArray(),
            screeningIndices,
            progressive ? "初筛" : "完整评估");

        var finalists = progressive
            ? Enumerable.Range(0, candidates.Count)
                .OrderByDescending(candidateIndex => TuningScore(
                    runsByCandidate[candidateIndex]
                        .Where(item => item != null)
                        .Select(item => item!)
                        .ToList()))
                .ThenBy(candidateIndex =>
                    candidates[candidateIndex].ValidationLoss)
                .ThenBy(candidateIndex => candidates[candidateIndex].Epoch)
                .Take(requestedFinalists)
                .ToHashSet()
            : Enumerable.Range(0, candidates.Count).ToHashSet();
        if (progressive)
        {
            var screeningSet = new HashSet<int>(screeningIndices);
            var remainingIndices = Enumerable.Range(0, campaignCount)
                .Where(index => !screeningSet.Contains(index))
                .ToArray();
            RunTuningStage(
                finalists.OrderBy(index => index).ToArray(),
                remainingIndices,
                "决选");
        }
        completedCampaigns = tuningCompletedCampaigns;
        var campaignsExecuted = runsByCandidate.Sum(items =>
            items.Count(item => item != null));
        var campaignsSaved = Math.Max(
            0,
            originalCandidateCount * campaignCount - campaignsExecuted);
        TuningSelection? best = null;
        for (var candidateIndex = 0;
             candidateIndex < candidates.Count;
             candidateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[candidateIndex];
            var completed = runsByCandidate[candidateIndex]
                .Where(item => item != null)
                .Select(item => item!)
                .ToList();
            foreach (var campaign in completed)
            {
                RecordCase(
                    foundationResult,
                    campaign,
                    progressive && !finalists.Contains(candidateIndex)
                        ? "tuning-screening"
                        : "tuning",
                    iteration + 1,
                    "epoch-" + candidate.Epoch,
                    ruleset.RulesetHash,
                    request.DecisionProfile,
                    candidate.Model.ModelId,
                    episodes: null,
                    request);
            }
            if (!finalists.Contains(candidateIndex))
            {
                continue;
            }
            var invalid = completed.Count(item => item.Invalid);
            var score = TuningScore(completed);
            var selection = new TuningSelection
            {
                Model = CombatPolicyValueBatchTrainer.Clone(candidate.Model),
                Epoch = candidate.Epoch,
                Score = score,
                ValidationLoss = candidate.ValidationLoss,
                CandidateCount = originalCandidateCount,
                OfflineRejectedCandidates = offlineRejectedCandidates,
                AllCandidatesRejectedOffline = allCandidatesRejectedOffline,
                FinalistCount = finalists.Count,
                EvaluationRan = true,
                CampaignsExecuted = campaignsExecuted,
                CampaignsSaved = campaignsSaved,
                InvalidCampaigns = invalid,
                TrainingMetrics =
                    CloneMetricSnapshot(candidate.TrainingMetrics),
                ValidationMetrics =
                    CloneMetricSnapshot(candidate.ValidationMetrics),
                TestMetrics = CloneMetricSnapshot(candidate.TestMetrics)
            };
            if (best == null
                || selection.Score > best.Score + 0.0000001d
                || Math.Abs(selection.Score - best.Score) <= 0.0000001d
                && selection.ValidationLoss < best.ValidationLoss)
            {
                best = selection;
            }
        }
        best ??= new TuningSelection
        {
            Model = CombatPolicyValueBatchTrainer.Clone(trained.Model!),
            Epoch = trained.BestEpoch,
            CandidateCount = originalCandidateCount,
            OfflineRejectedCandidates = offlineRejectedCandidates,
            AllCandidatesRejectedOffline = allCandidatesRejectedOffline,
            FinalistCount = finalists.Count,
            EvaluationRan = true,
            CampaignsExecuted = campaignsExecuted,
            CampaignsSaved = campaignsSaved,
            TrainingMetrics =
                CloneMetricSnapshot(trained.TrainingMetrics),
            ValidationMetrics =
                CloneMetricSnapshot(trained.ValidationMetrics),
            TestMetrics = CloneMetricSnapshot(trained.TestMetrics)
        };
        best.Model.ModelId =
            (best.Model.ModelId ?? "aura-combat-policy-value")
            + "-epoch-"
            + best.Epoch;
        return best;

        static bool OfflineDominated(
            CombatPolicyValueModelCandidate candidate,
            CombatPolicyValueModelCandidate best)
        {
            var candidateMetrics = candidate.ValidationMetrics;
            var bestMetrics = best.ValidationMetrics;
            return candidateMetrics.FrameCount > 0
                   && bestMetrics.FrameCount > 0
                   && candidateMetrics.CompositeLossCiLower > 0d
                   && bestMetrics.CompositeLossCiUpper > 0d
                   && candidateMetrics.CompositeLossCiLower
                      > bestMetrics.CompositeLossCiUpper + 0.0000001d
                   && candidateMetrics.CriticalPolicyAccuracy
                      <= bestMetrics.CriticalPolicyAccuracy + 0.0000001d
                   && candidateMetrics.DeathBrier
                      >= bestMetrics.DeathBrier - 0.0000001d;
        }

        void RunTuningStage(
            IReadOnlyList<int> candidateIndices,
            IReadOnlyList<int> campaignIndices,
            string stage)
        {
            if (candidateIndices.Count == 0 || campaignIndices.Count == 0)
            {
                return;
            }
            CombatFoundationWorkScheduler.For(
                candidateIndices.Count * campaignIndices.Count,
                parallelism,
                cancellationToken,
                workIndex =>
                {
                    var candidateIndex = candidateIndices[
                        workIndex / campaignIndices.Count];
                    var campaignIndex = campaignIndices[
                        workIndex % campaignIndices.Count];
                    var candidate = candidates[candidateIndex];
                    var normal = campaignIndex < normalCampaigns;
                    var offset = normal
                        ? campaignIndex
                        : campaignIndex - normalCampaigns;
                    var seed = iterationSeedStart
                               + (normal
                                   ? (ulong)offset
                                   : (ulong)(normalCampaigns + offset));
                    var campaign = RunCampaign(
                        request.TrainingCampaign,
                        normal ? "normal" : "advanced",
                        seed,
                        ruleset,
                        new CombatDecisionSimulationPolicyFactory(
                            profile,
                            policyValueModel: models[candidateIndex]),
                        telemetry,
                        "tuning:epoch-" + candidate.Epoch,
                        cancellationToken);
                    runsByCandidate[candidateIndex][campaignIndex] = campaign;
                    ReportProgress(
                        request,
                        telemetry,
                        campaign,
                        ref tuningCompletedCampaigns,
                        totalCampaigns,
                        "第 "
                        + (iteration + 1)
                        + " 轮：调参"
                        + stage
                        + " Epoch "
                        + candidate.Epoch);
                },
                telemetry.SchedulerProgress);
        }
    }

    private static ICombatPolicyValueModel CreateParallelPolicyValueModel(
        CombatPolicyValueNetworkDefinition definition,
        CombatCampaignFoundationTrainingRequest request,
        int parallelism,
        int competingModelCount = 1)
    {
        var model = new ManagedCombatPolicyValueModel(definition);
        var execution = CombatFoundationExecutionProfiles.Resolve(
            CombatFoundationExecutionProfileNames.Custom,
            parallelism,
            request.InferenceExecutionMode,
            request.InferenceParallelism,
            request.ThreadPoolMinimumWorkerThreads,
            request.CheckpointSerializationParallelism,
            null,
            request.InferenceLaneCount,
            request.InferenceBatchSize);
        var perModelParallelism = Math.Max(
            1,
            parallelism / Math.Max(1, competingModelCount));
        if (perModelParallelism < 4
            || string.Equals(
                execution.InferenceMode,
                CombatFoundationExecutionProfileNames.DirectInference,
                StringComparison.Ordinal))
        {
            return model;
        }
        var batchSize = execution.InferenceBatchSize;
        if (perModelParallelism < batchSize)
        {
            return model;
        }
        var laneCount = Math.Min(
            execution.InferenceLaneCount,
            Math.Max(1, perModelParallelism / batchSize));
        return laneCount == 1
            ? new ConcurrentBatchedCombatPolicyValueModel(model, batchSize)
            : new ShardedBatchedCombatPolicyValueModel(
                model,
                laneCount,
                batchSize);
    }

    private static void EnsureThreadPoolCapacity(int minimumWorkerThreads)
    {
        ThreadPool.GetMinThreads(out var currentWorkers, out var currentIo);
        var requested = Math.Max(currentWorkers, minimumWorkerThreads);
        if (requested > currentWorkers)
        {
            ThreadPool.SetMinThreads(requested, currentIo);
        }
    }

    private static int EffectiveInferenceLaneCount(int parallelism)
    {
        return CombatFoundationExecutionProfiles.EffectiveLaneCount(parallelism);
    }

    private static int EffectiveInferenceBatchSize(int parallelism)
    {
        return CombatFoundationExecutionProfiles.EffectiveBatchSize(parallelism);
    }

    private static double TuningScore(
        IReadOnlyList<CombatCampaignResult> completed)
    {
        var invalid = completed.Count(item => item.Invalid);
        if (invalid > 0)
        {
            return -1000d - invalid;
        }
        var normalRuns = completed.Where(item => string.Equals(
            item.DifficultyId,
            "normal",
            StringComparison.Ordinal)).ToList();
        var advancedRuns = completed.Where(item => string.Equals(
            item.DifficultyId,
            "advanced",
            StringComparison.Ordinal)).ToList();
        var depth = completed.Count == 0
            ? 0d
            : completed.Average(item =>
                item.CompletedBattles
                / (double)Math.Max(1, item.TotalBattles));
        return WinRate(normalRuns, "normal") * 0.40d
               + WinRate(advancedRuns, "advanced") * 0.40d
               + depth * 0.20d;
    }

    internal static bool ArenaNoRegressionStillPossible(
        IReadOnlyList<CombatCampaignResult> champion,
        IReadOnlyList<CombatCampaignResult> candidate,
        int remainingPairsPerDifficulty,
        bool requireAdvancedStrictGain)
    {
        var remaining = Math.Max(0, remainingPairsPerDifficulty);
        foreach (var difficulty in new[] { "normal", "advanced" })
        {
            var championVictories = champion.Count(item =>
                !item.Invalid
                && item.FinalBossVictory
                && string.Equals(
                    item.DifficultyId,
                    difficulty,
                    StringComparison.Ordinal));
            var candidateVictories = candidate.Count(item =>
                !item.Invalid
                && item.FinalBossVictory
                && string.Equals(
                    item.DifficultyId,
                    difficulty,
                    StringComparison.Ordinal));
            var requiredLead = requireAdvancedStrictGain
                               && string.Equals(
                                   difficulty,
                                   "advanced",
                                   StringComparison.Ordinal)
                ? 1
                : 0;
            if (candidateVictories + remaining
                < championVictories + requiredLead)
            {
                return false;
            }
        }
        return true;
    }

    internal static bool ShouldStopArenaScreening(
        IReadOnlyList<CombatCampaignResult> champion,
        IReadOnlyList<CombatCampaignResult> candidate,
        int remainingPairsPerDifficulty,
        double normalAcceptanceRate,
        double advancedAcceptanceRate)
    {
        // Relative evidence and absolute qualification are independent
        // advancement paths. A screening prefix may stop only after the
        // remaining paired seeds can recover neither path.
        var relativeStillPossible = ArenaNoRegressionStillPossible(
            champion,
            candidate,
            remainingPairsPerDifficulty,
            requireAdvancedStrictGain: false);
        var absoluteStillPossible = ArenaAbsoluteQualificationStillPossible(
            candidate,
            remainingPairsPerDifficulty,
            normalAcceptanceRate,
            advancedAcceptanceRate);
        return !relativeStillPossible && !absoluteStillPossible;
    }

    internal static int ArenaScreeningPairsSaved(
        int configuredPairsPerDifficulty,
        int actuallyExecutedPairs)
    {
        var configured = Math.Max(1, configuredPairsPerDifficulty) * 2;
        return Math.Max(
            0,
            configured - Math.Max(0, actuallyExecutedPairs));
    }

    internal static FoundationArenaSequentialDecision ArenaSequentialDecision(
        IReadOnlyList<CombatCampaignResult> champion,
        IReadOnlyList<CombatCampaignResult> candidate,
        int remainingPairsPerDifficulty,
        int minimumDiscordantPairs,
        double normalAcceptanceRate,
        double advancedAcceptanceRate,
        bool requireAdvancedStrictGain)
    {
        var relativeStillPossible = ArenaAdvancedAcceptanceStillPossible(
                                        candidate,
                                        remainingPairsPerDifficulty,
                                        advancedAcceptanceRate)
                                    && ArenaNoRegressionStillPossible(
                                        champion,
                                        candidate,
                                        remainingPairsPerDifficulty,
                                        requireAdvancedStrictGain);
        var absoluteStillPossible = ArenaAbsoluteQualificationStillPossible(
            candidate,
            remainingPairsPerDifficulty,
            normalAcceptanceRate,
            advancedAcceptanceRate);
        if (!relativeStillPossible && !absoluteStillPossible)
        {
            return FoundationArenaSequentialDecision.Reject;
        }

        var pairCount = Math.Min(champion.Count, candidate.Count);
        var validPairs = Enumerable.Range(0, pairCount)
            .Where(index => !champion[index].Invalid
                            && !candidate[index].Invalid)
            .ToList();
        var candidateOnlyWins = validPairs.Count(index =>
            candidate[index].FinalBossVictory
            && !champion[index].FinalBossVictory);
        var championOnlyWins = validPairs.Count(index =>
            champion[index].FinalBossVictory
            && !candidate[index].FinalBossVictory);
        var discordant = candidateOnlyWins + championOnlyWins;
        if (discordant < Math.Max(1, minimumDiscordantPairs)
            || candidateOnlyWins <= championOnlyWins
            || SequentialWilsonLowerBound(candidateOnlyWins, discordant)
               < CombatFoundationPromotionProtocol
                   .MinimumPairedWinWilsonLowerBound)
        {
            return FoundationArenaSequentialDecision.Continue;
        }

        foreach (var difficulty in new[] { "normal", "advanced" })
        {
            var difficultyPairs = validPairs.Where(index => string.Equals(
                    candidate[index].DifficultyId,
                    difficulty,
                    StringComparison.Ordinal))
                .ToList();
            if (difficultyPairs.Count < 8)
            {
                return FoundationArenaSequentialDecision.Continue;
            }
            var candidateVictories = difficultyPairs.Count(index =>
                candidate[index].FinalBossVictory);
            var championVictories = difficultyPairs.Count(index =>
                champion[index].FinalBossVictory);
            var requiredLead = requireAdvancedStrictGain
                               && string.Equals(
                                   difficulty,
                                   "advanced",
                                   StringComparison.Ordinal)
                ? 1
                : 0;
            if (candidateVictories < championVictories + requiredLead)
            {
                return FoundationArenaSequentialDecision.Continue;
            }
            if (string.Equals(
                    difficulty,
                    "advanced",
                    StringComparison.Ordinal)
                && SequentialWilsonLowerBound(
                    candidateVictories,
                    difficultyPairs.Count) + 0.0000001d
                   < advancedAcceptanceRate)
            {
                return FoundationArenaSequentialDecision.Continue;
            }
        }
        return FoundationArenaSequentialDecision.Accept;
    }

    internal static bool ShouldStopArenaConfirmation(
        FoundationArenaSequentialDecision decision)
    {
        // Acceptance is not monotonic: the unplayed paired seeds can reverse
        // the candidate lead. Rejection is safe only after the existing
        // remaining-budget checks prove recovery impossible.
        return decision == FoundationArenaSequentialDecision.Reject;
    }

    internal static int EffectiveArenaScreeningPairsPerDifficulty(
        int configuredPairs,
        int evaluationBatchSize,
        bool diagnosticOnly)
    {
        if (configuredPairs <= 0)
        {
            return 0;
        }
        var configured = Math.Max(1, configuredPairs);
        if (!diagnosticOnly)
        {
            return configured;
        }
        var batch = Math.Max(1, evaluationBatchSize);
        return Math.Min(configured, Math.Max(4, Math.Min(8, batch)));
    }

    internal static bool ArenaAdvancedAcceptanceStillPossible(
        IReadOnlyList<CombatCampaignResult> candidate,
        int remainingPairs,
        double advancedAcceptanceRate)
    {
        return ArenaDifficultyAcceptanceStillPossible(
            candidate,
            "advanced",
            remainingPairs,
            advancedAcceptanceRate);
    }

    internal static bool ArenaAbsoluteQualificationStillPossible(
        IReadOnlyList<CombatCampaignResult> candidate,
        int remainingPairsPerDifficulty,
        double normalAcceptanceRate,
        double advancedAcceptanceRate)
    {
        return ArenaDifficultyAcceptanceStillPossible(
                   candidate,
                   "normal",
                   remainingPairsPerDifficulty,
                   normalAcceptanceRate)
               && ArenaDifficultyAcceptanceStillPossible(
                   candidate,
                   "advanced",
                   remainingPairsPerDifficulty,
                   advancedAcceptanceRate);
    }

    private static bool ArenaDifficultyAcceptanceStillPossible(
        IReadOnlyList<CombatCampaignResult> candidate,
        string difficulty,
        int remainingPairs,
        double acceptanceRate)
    {
        var validRuns = (candidate ?? Array.Empty<CombatCampaignResult>())
            .Where(item => item != null
                           && !item.Invalid
                           && string.Equals(
                               item.DifficultyId,
                               difficulty,
                               StringComparison.Ordinal))
            .ToList();
        var remaining = Math.Max(0, remainingPairs);
        var finalTrials = validRuns.Count + remaining;
        if (finalTrials <= 0)
        {
            return true;
        }
        var victories = validRuns.Count(item => item.FinalBossVictory);
        var bestPossibleRate = (victories + remaining) / (double)finalTrials;
        return bestPossibleRate + 0.0000001d
               >= Math.Max(0d, Math.Min(1d, acceptanceRate));
    }

    internal static int StrategyQuotaCollectionCampaignLimit(
        IReadOnlyDictionary<string, int> shortfalls)
    {
        var total = StrategyQuotaShortfallTotal(shortfalls);
        if (total <= 0)
        {
            return 0;
        }
        return total <= 32 ? 4 : total <= 96 ? 6 : 8;
    }

    internal static string StrategyQuotaCollectionDifficulty(
        IReadOnlyDictionary<string, int> shortfalls,
        int collectionIndex,
        IReadOnlyDictionary<string, FoundationStrategyQuotaYieldProfile>?
            yieldProfiles = null)
    {
        var survival = StrategyQuotaShortfall(
            shortfalls,
            "strategy-survival");
        var lateJourney = StrategyQuotaShortfall(
                              shortfalls,
                              "strategy-growth")
                          + StrategyQuotaShortfall(
                              shortfalls,
                              "strategy-growth-negative")
                          + StrategyQuotaShortfall(
                              shortfalls,
                              "strategy-transform")
                          + StrategyQuotaShortfall(
                              shortfalls,
                              "strategy-transform-negative")
                          + StrategyQuotaShortfall(
                              shortfalls,
                              "strategy-bank")
                          + StrategyQuotaShortfall(
                              shortfalls,
                              "strategy-finale");
        var slot = Math.Max(0, collectionIndex) % 4;
        var heuristic = survival > lateJourney
            ? slot == 2 ? "normal" : "advanced"
            : lateJourney > 0
                ? slot == 2 ? "advanced" : "normal"
                : slot % 2 == 0 ? "advanced" : "normal";
        if (yieldProfiles == null || yieldProfiles.Count == 0)
        {
            return heuristic;
        }
        var normalObserved = yieldProfiles.TryGetValue(
            "normal",
            out var normalProfile) && normalProfile.Campaigns > 0;
        var advancedObserved = yieldProfiles.TryGetValue(
            "advanced",
            out var advancedProfile) && advancedProfile.Campaigns > 0;
        if (!normalObserved || !advancedObserved)
        {
            return !normalObserved ? "normal" : "advanced";
        }
        var normalYield = StrategyQuotaYieldScore(
            shortfalls,
            normalProfile!);
        var advancedYield = StrategyQuotaYieldScore(
            shortfalls,
            advancedProfile!);
        if (normalYield > advancedYield + 0.0000001d)
        {
            return "normal";
        }
        if (advancedYield > normalYield + 0.0000001d)
        {
            return "advanced";
        }
        return heuristic;
    }

    internal static void RecordStrategyQuotaYield(
        IDictionary<string, FoundationStrategyQuotaYieldProfile> yieldProfiles,
        string difficulty,
        IEnumerable<CombatEpisode>? episodes)
    {
        if (yieldProfiles == null)
        {
            return;
        }
        var key = string.Equals(
            difficulty,
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
        if (!yieldProfiles.TryGetValue(key, out var profile))
        {
            profile = new FoundationStrategyQuotaYieldProfile();
            yieldProfiles[key] = profile;
        }
        profile.Campaigns++;
        foreach (var frame in (episodes ?? Array.Empty<CombatEpisode>())
                     .Where(episode => episode != null)
                     .SelectMany(episode => episode.Frames
                                            ?? new List<CombatEpisodeFrame>()))
        {
            var stratum = CombatPolicyValueBatchTrainer
                .StrategicFrameStratumForFrame(frame);
            var classes = new HashSet<string>(StringComparer.Ordinal);
            if (stratum.StartsWith("strategy-", StringComparison.Ordinal)
                && !string.Equals(
                    stratum,
                    "strategy-baseline",
                    StringComparison.Ordinal)
                && !string.Equals(
                    stratum,
                    "strategy-other",
                    StringComparison.Ordinal))
            {
                classes.Add(stratum);
            }
            var supervision = CombatPolicyValueBatchTrainer
                .StrategicFrameSupervisionForExecutedAction(frame);
            foreach (var label in supervision.ApplicableLabels)
            {
                classes.Add(
                    "strategy-"
                    + label
                    + (supervision.PositiveLabels.Contains(
                        label,
                        StringComparer.Ordinal)
                        ? ""
                        : "-negative"));
            }
            foreach (var strategyClass in classes)
            {
                profile.StrategyFrames[strategyClass] =
                    (profile.StrategyFrames.TryGetValue(
                        strategyClass,
                        out var count)
                    ? count
                    : 0) + 1;
            }
        }
    }

    internal static Dictionary<string, int> RequiredStrategyClassFrames(
        CombatTransformerTeacherReport? report)
    {
        var required = new Dictionary<string, int>(StringComparer.Ordinal);
        if (report == null) return required;
        foreach (var label in new[] { "transform", "growth" })
        {
            var applicable = StrategyReportCount(
                report.StrategyApplicableCounts,
                label);
            if (applicable < 8) continue;
            if (StrategyReportCount(report.StrategyLabelCounts, label) < 4)
            {
                required["strategy-" + label] = 4;
            }
            if (StrategyReportCount(report.StrategyNegativeCounts, label) < 4)
            {
                required["strategy-" + label + "-negative"] = 4;
            }
        }
        return required;
    }

    private static int StrategyReportCount(
        IReadOnlyDictionary<string, int>? counts,
        string label)
    {
        if (counts == null) return 0;
        foreach (var key in new[] { label, "strategy-" + label })
        {
            if (counts.TryGetValue(key, out var count))
            {
                return Math.Max(0, count);
            }
        }
        return 0;
    }

    private static double StrategyQuotaYieldScore(
        IReadOnlyDictionary<string, int> shortfalls,
        FoundationStrategyQuotaYieldProfile profile)
    {
        if (profile == null || profile.Campaigns <= 0)
        {
            return 0d;
        }
        return (shortfalls ?? new Dictionary<string, int>()).Sum(item =>
            Math.Max(0, item.Value)
            * (profile.StrategyFrames.TryGetValue(item.Key, out var frames)
                ? Math.Max(0, frames)
                : 0d)) / profile.Campaigns;
    }

    internal static double OverallEstimatedRemainingSeconds(
        double remainingSimulationSeconds,
        double remainingCurrentPhaseSeconds,
        bool phaseEstimateActive)
    {
        var simulation = Math.Max(0d, remainingSimulationSeconds);
        var phase = phaseEstimateActive
            ? Math.Max(0d, remainingCurrentPhaseSeconds)
            : 0d;
        if (simulation <= 0d)
        {
            return phase;
        }
        if (phase <= 0d)
        {
            return simulation;
        }
        return Math.Max(simulation, phase);
    }

    internal static CombatFoundationEtaEstimate StageAwareEta(
        double elapsedSeconds,
        int completedBattles,
        double remainingBattleWork,
        IReadOnlyDictionary<string, double>? phaseElapsedSeconds,
        int currentIteration,
        int totalIterations,
        string? currentPhase,
        double currentPhaseRemainingSeconds)
    {
        var recurringPhaseNames = new[]
        {
            "transformer-teacher",
            "model-training",
            "replay-selection"
        };
        var recurringElapsed = recurringPhaseNames.Sum(name =>
            phaseElapsedSeconds != null
            && phaseElapsedSeconds.TryGetValue(name, out var value)
                ? Math.Max(0d, value)
                : 0d);
        var simulationElapsed = Math.Max(
            0.001d,
            Math.Max(0d, elapsedSeconds) - recurringElapsed);
        var battleRate = Math.Max(0, completedBattles) / simulationElapsed;
        var simulationSeconds = battleRate <= 0d
            ? 0d
            : Math.Max(0d, remainingBattleWork) / battleRate;

        var observedIterations = Math.Max(1, currentIteration);
        var remainingIterations = Math.Max(
            0,
            totalIterations - Math.Max(0, currentIteration));
        var recurringSeconds = recurringElapsed / observedIterations
                               * remainingIterations;
        var currentRecurringSeconds = recurringPhaseNames.Contains(
            currentPhase ?? "",
            StringComparer.OrdinalIgnoreCase)
            ? Math.Max(0d, currentPhaseRemainingSeconds)
            : 0d;
        var expected = simulationSeconds
                       + recurringSeconds
                       + currentRecurringSeconds;
        if (expected <= 0d)
        {
            expected = Math.Max(0d, currentPhaseRemainingSeconds);
        }
        return new CombatFoundationEtaEstimate
        {
            ExpectedSeconds = expected,
            LowerSeconds = expected * 0.80d,
            UpperSeconds = expected * 1.25d,
            StageSeconds = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["simulation"] = simulationSeconds,
                ["recurring-training"] = recurringSeconds,
                ["current-phase"] = currentRecurringSeconds
            }
        };
    }

    private static int StrategyQuotaShortfallTotal(
        IReadOnlyDictionary<string, int> shortfalls)
    {
        return (shortfalls ?? new Dictionary<string, int>())
            .Values.Sum(value => Math.Max(0, value));
    }

    private static int StrategyQuotaShortfall(
        IReadOnlyDictionary<string, int> shortfalls,
        string key)
    {
        return shortfalls != null
               && shortfalls.TryGetValue(key, out var value)
            ? Math.Max(0, value)
            : 0;
    }

    private static double Clamp01(double value)
    {
        return Math.Max(0d, Math.Min(1d, value));
    }

    private static string ReplayEpisodeKey(CombatEpisode episode)
    {
        if (episode == null)
        {
            return "";
        }
        return (episode.JourneyRunId ?? "")
               + "|"
               + episode.JourneyBattleIndex.ToString("D4")
               + "|"
               + episode.Seed.ToString("D20")
               + "|"
               + (episode.ScenarioId ?? "")
               + "|"
               + (episode.EpisodeId ?? "");
    }

    private static double SequentialWilsonLowerBound(int successes, int total)
    {
        if (total <= 0)
        {
            return 0d;
        }
        const double z = 3.290526731d;
        var safeSuccesses = Math.Max(0, Math.Min(total, successes));
        var probability = safeSuccesses / (double)total;
        var zSquared = z * z;
        var denominator = 1d + zSquared / total;
        var center = probability + zSquared / (2d * total);
        var margin = z * Math.Sqrt(
            (probability * (1d - probability) + zSquared / (4d * total))
            / total);
        return Math.Max(0d, (center - margin) / denominator);
    }

    internal static bool ShouldAcceptWorkingModel(
        bool workingCheckpoint,
        bool bootstrapPromotion,
        bool meaningfulWinGain,
        bool meaningfulProgressGain)
    {
        // Arena scores from different seed windows are not comparable. A
        // working model advances only on paired non-regression plus a gain
        // measured against the champion in the current window.
        return workingCheckpoint
               && (bootstrapPromotion
                   || meaningfulWinGain
                   || meaningfulProgressGain);
    }

    internal static bool OfflineHeadRegressionPassed(
        CombatPolicyValueMetricSnapshot? baseline,
        CombatPolicyValueMetricSnapshot? candidate,
        double maximumRegression)
    {
        if (baseline == null || candidate == null)
        {
            return false;
        }
        var allowed = double.IsNaN(maximumRegression)
                      || double.IsInfinity(maximumRegression)
            ? CombatFoundationPromotionProtocol
                .DefaultMaximumOfflineHeadRegression
            : Math.Max(0d, Math.Min(0.50d, maximumRegression));
        return HeadNoRegression(
                   baseline.CompositeLoss,
                   candidate.CompositeLoss,
                   allowed)
               && HeadNoRegression(
                   baseline.ValueMae,
                   candidate.ValueMae,
                   allowed)
               && HeadNoRegression(
                   baseline.Brier,
                   candidate.Brier,
                   allowed)
               && HeadNoRegression(
                   baseline.DeathBrier,
                   candidate.DeathBrier,
                   allowed);
    }

    internal static CombatFoundationCompatibilityManifest
        BuildCompatibilityManifest(
            CombatCampaignFoundationTrainingRequest request,
            string rulesetHash,
            CombatPolicyValueTrainingOptions? normalizedTraining = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var training = normalizedTraining ?? request.Training.Normalized();
        return new CombatFoundationCompatibilityManifest
        {
            RulesetHash = rulesetHash ?? "",
            ContentSetHash = request.ContentSetHash,
            OwnerModSetHash = request.OwnerModSetHash,
            ActionContractVersion = CombatActionContractProtocol.Version,
            SemanticGateVersion =
                CombatFoundationSemanticGateProtocol.Version,
            IntegritySeedCorpusVersion =
                CombatFoundationIntegritySeedCorpus.Version,
            NativeProgramPackageHash = request.NativeProgramPackageHash ?? "",
            CampaignId = request.TrainingCampaign.CampaignId ?? "",
            CampaignVersion =
                request.TrainingCampaign.CampaignVersion ?? "",
            TrainingCampaignHash =
                CampaignFingerprint(request.TrainingCampaign),
            ValidationCampaignHash =
                CampaignFingerprint(request.ValidationCampaign),
            FeatureSchemaVersion =
                CombatPolicyValueProtocol.FeatureSchemaVersion,
            FeatureEncodingMode = training.FeatureEncodingMode,
            TrainingPolicyVersion = request.TrainingPolicyVersion ?? "",
            TrainingSemanticsVersion =
                CombatPolicyValueProtocol.TrainingSemanticsVersion,
            StateDimensions = training.StateDimensions,
            ActionDimensions = training.ActionDimensions,
            HiddenDimensions = training.HiddenDimensions
        };
    }

    private static double FiniteOrDefault(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : value;
    }

    private static double ModelMetric(
        CombatPolicyValueNetworkDefinition? model,
        string key,
        double fallback)
    {
        if (model?.Metrics == null
            || !model.Metrics.TryGetValue(key, out var value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            return fallback;
        }
        return value;
    }

    internal static bool FeatureCollisionGatePassed(
        CombatPolicyValueNetworkDefinition? model,
        double maximumStateRate,
        double maximumActionRate)
    {
        var stateRate = ModelMetric(
            model,
            "stateFeatureCollisionRate",
            double.PositiveInfinity);
        var actionRate = ModelMetric(
            model,
            "actionFeatureCollisionRate",
            double.PositiveInfinity);
        return stateRate <= maximumStateRate + 0.0000001d
               && actionRate <= maximumActionRate + 0.0000001d;
    }

    internal static bool FormalPromotionGatePassed(
        bool bootstrap,
        bool arenaEvidence,
        bool absoluteAdvanced,
        bool offlineHeads,
        bool strategyQuota,
        bool featureCollision)
    {
        return !bootstrap
               && arenaEvidence
               && absoluteAdvanced
               && offlineHeads
               && strategyQuota
               && featureCollision;
    }

    internal static bool AbsoluteQualificationGatePassed(
        int validArenaPairs,
        int expectedArenaPairs,
        bool absoluteNormal,
        bool absoluteAdvanced,
        bool offlineHeads,
        bool strategyQuota,
        bool featureCollision)
    {
        return expectedArenaPairs > 0
               && validArenaPairs == expectedArenaPairs
               && absoluteNormal
               && absoluteAdvanced
               && offlineHeads
               && strategyQuota
               && featureCollision;
    }

    internal static bool ShouldRunArenaConfirmation(
        bool relativeScreeningPassed,
        bool absoluteScreeningPassed,
        int confirmationPairsPerDifficulty,
        bool bootstrap,
        bool offlineHeads,
        bool strategyQuota,
        bool featureCollision)
    {
        return (relativeScreeningPassed || absoluteScreeningPassed)
               && confirmationPairsPerDifficulty > 0
               // A bootstrap candidate may skip a relative-only comparison,
               // but it must not enter absolute-qualified-best on the smaller
               // screening sample. Absolute evidence always gets the full
               // configured confirmation budget.
               && (!bootstrap || absoluteScreeningPassed)
               && offlineHeads
               && strategyQuota
               && featureCollision;
    }

    internal static int ExpectedArenaQualificationPairs(
        int screeningPairsPerDifficulty,
        int confirmationPairsPerDifficulty,
        bool confirmationRan)
    {
        var confirmation = Math.Max(0, confirmationPairsPerDifficulty);
        if (!confirmationRan || confirmation <= 0)
        {
            // Screening is diagnostic evidence. Absolute qualification fails
            // closed until the configured formal confirmation is complete.
            return -1;
        }
        return (Math.Max(0, screeningPairsPerDifficulty) + confirmation) * 2;
    }

    internal static bool ConfirmedQualificationEvidence(
        CombatCampaignFoundationIteration? evidence,
        int confirmationPairsPerDifficulty)
    {
        var requiredConfirmationPairs = Math.Max(
            0,
            confirmationPairsPerDifficulty) * 2;
        return evidence != null
               && requiredConfirmationPairs > 0
               && evidence.ArenaConfirmationPairs
                  == requiredConfirmationPairs
               && !evidence.ArenaConfirmationStoppedEarly;
    }

    internal static bool NonInferiorityGatePassed(
        bool workingCheckpoint,
        int validNormalPairs,
        int validAdvancedPairs,
        int candidateOnlyWins,
        int championOnlyWins,
        double pairedRegressionWilsonUpperBound,
        bool absoluteNormal,
        bool absoluteAdvanced,
        bool offlineHeads,
        bool strategyQuota,
        bool featureCollision)
    {
        return workingCheckpoint
               && validNormalPairs
                  >= CombatFoundationPromotionProtocol
                      .MinimumNonInferiorityPairsPerDifficulty
               && validAdvancedPairs
                  >= CombatFoundationPromotionProtocol
                      .MinimumNonInferiorityPairsPerDifficulty
               && candidateOnlyWins >= championOnlyWins
               && pairedRegressionWilsonUpperBound
                  <= CombatFoundationPromotionProtocol
                         .MaximumPairedRegressionWilsonUpperBound
                     + 0.0000001d
               && absoluteNormal
               && absoluteAdvanced
               && offlineHeads
               && strategyQuota
               && featureCollision;
    }

    private static bool HeadNoRegression(
        double baseline,
        double candidate,
        double allowed)
    {
        if (double.IsNaN(baseline)
            || double.IsInfinity(baseline)
            || double.IsNaN(candidate)
            || double.IsInfinity(candidate))
        {
            return false;
        }
        var tolerance = Math.Max(0.000001d, Math.Abs(baseline) * allowed);
        return candidate <= baseline + tolerance;
    }

    internal static bool CapabilityNoRegressionStillPossible(
        IReadOnlyList<CombatCampaignResult?> baseline,
        IReadOnlyList<CombatCampaignResult?> champion,
        int campaignsPerDifficulty,
        int completedPerDifficulty)
    {
        var total = Math.Max(0, campaignsPerDifficulty);
        var completed = Math.Max(0, Math.Min(total, completedPerDifficulty));
        var remaining = total - completed;
        for (var difficultyIndex = 0; difficultyIndex < 2; difficultyIndex++)
        {
            var offset = difficultyIndex * total;
            var baselineVictories = 0;
            var championVictories = 0;
            for (var index = 0; index < completed; index++)
            {
                var baselineRun = baseline[offset + index];
                var championRun = champion[offset + index];
                if (baselineRun?.Invalid == true || championRun?.Invalid == true)
                {
                    return false;
                }
                if (baselineRun?.FinalBossVictory == true)
                {
                    baselineVictories++;
                }
                if (championRun?.FinalBossVictory == true)
                {
                    championVictories++;
                }
            }
            if (championVictories + remaining < baselineVictories)
            {
                return false;
            }
        }
        return true;
    }

    private static double WinRate(
        IReadOnlyList<CombatCampaignResult> results,
        string difficulty)
    {
        var selected = results.Where(item => string.Equals(
            item.DifficultyId,
            difficulty,
            StringComparison.Ordinal)).ToList();
        return selected.Count == 0
            ? 0d
            : selected.Count(item => item.FinalBossVictory) / (double)selected.Count;
    }

    private static double Score(CombatCampaignResult result)
    {
        if (result.Invalid)
        {
            return -10_000d;
        }
        var hpRatio = result.FinalState.MaxHp <= 0
            ? 0d
            : result.FinalState.CurrentHp / (double)result.FinalState.MaxHp;
        return (result.FinalBossVictory ? 10_000d : 0d)
               + result.CompletedBattles * 100d
               + hpRatio * 10d;
    }

    private static CombatPolicyValueEpochMetrics CloneEpochMetrics(
        CombatPolicyValueEpochMetrics? source,
        int? iteration = null)
    {
        source ??= new CombatPolicyValueEpochMetrics();
        return new CombatPolicyValueEpochMetrics
        {
            Iteration = iteration ?? source.Iteration,
            Epoch = source.Epoch,
            Calibrated = source.Calibrated,
            EventKind = source.EventKind,
            TrainingMeasurement = source.TrainingMeasurement,
            ElapsedSeconds = source.ElapsedSeconds,
            LearningRate = source.LearningRate,
            GradientNorm = source.GradientNorm,
            GradientClipCount = source.GradientClipCount,
            Improved = source.Improved,
            BestEpoch = source.BestEpoch,
            BestValidationLoss = source.BestValidationLoss,
            StaleEpochs = source.StaleEpochs,
            EarlyStopped = source.EarlyStopped,
            TrainingSplitHash = source.TrainingSplitHash,
            ValidationSplitHash = source.ValidationSplitHash,
            Training = CloneMetricSnapshot(source.Training),
            Validation = CloneMetricSnapshot(source.Validation)
        };
    }

    internal static CombatPolicyValueMetricSnapshot CloneMetricSnapshot(
        CombatPolicyValueMetricSnapshot? source)
    {
        source ??= new CombatPolicyValueMetricSnapshot();
        return new CombatPolicyValueMetricSnapshot
        {
            FrameCount = source.FrameCount,
            RunCount = source.RunCount,
            CompositeLoss = source.CompositeLoss,
            CompositeLossStandardError =
                source.CompositeLossStandardError,
            CompositeLossCiLower = source.CompositeLossCiLower,
            CompositeLossCiUpper = source.CompositeLossCiUpper,
            PolicyAccuracy = source.PolicyAccuracy,
            CriticalPolicyAccuracy = source.CriticalPolicyAccuracy,
            PolicyCrossEntropy = source.PolicyCrossEntropy,
            ValueMae = source.ValueMae,
            Brier = source.Brier,
            DeathBrier = source.DeathBrier,
            HpMae = source.HpMae,
            TurnHuber = source.TurnHuber,
            ActionQuantilePinball = source.ActionQuantilePinball,
            ActionQuantileMae = source.ActionQuantileMae,
            ActionQuantileLabelCount = source.ActionQuantileLabelCount
        };
    }

    private static CombatFoundationPendingArenaCandidate
        CreatePendingArenaCandidate(
            int sourceIteration,
            CombatPolicyValueTrainingResult trained,
            CombatPolicyValueMetricSnapshot selectionAnchorMetrics,
            int selectedEpoch,
            double selectedScore,
            bool offlineHeadRegressionGatePassed,
            bool strategyQuotaGatePassed,
            bool featureCollisionGatePassed,
            double stateFeatureCollisionRate,
            double actionFeatureCollisionRate)
    {
        return new CombatFoundationPendingArenaCandidate
        {
            SourceIteration = Math.Max(1, sourceIteration),
            Model = trained.Model,
            BaselineValidationMetrics = CloneMetricSnapshot(
                trained.BaselineValidationMetrics),
            TrainingMetrics = CloneMetricSnapshot(trained.TrainingMetrics),
            ValidationMetrics = CloneMetricSnapshot(trained.ValidationMetrics),
            SelectionAnchorMetrics = CloneMetricSnapshot(
                selectionAnchorMetrics),
            TestMetrics = CloneMetricSnapshot(trained.TestMetrics),
            EpochHistory = (trained.EpochHistory
                            ?? new List<CombatPolicyValueEpochMetrics>())
                .Select(item => CloneEpochMetrics(item, item.Iteration))
                .ToList(),
            SelectedEpoch = selectedEpoch,
            SelectedScore = selectedScore,
            OfflineHeadRegressionGatePassed =
                offlineHeadRegressionGatePassed,
            StrategyQuotaGatePassed = strategyQuotaGatePassed,
            FeatureCollisionGatePassed = featureCollisionGatePassed,
            StateFeatureCollisionRate = stateFeatureCollisionRate,
            ActionFeatureCollisionRate = actionFeatureCollisionRate
        };
    }

    private static void ApplyPendingArenaCandidate(
        CombatPolicyValueTrainingResult trained,
        CombatFoundationPendingArenaCandidate pending)
    {
        trained.Model = pending.Model;
        trained.BaselineValidationMetrics = CloneMetricSnapshot(
            pending.BaselineValidationMetrics);
        trained.TrainingMetrics = CloneMetricSnapshot(pending.TrainingMetrics);
        trained.ValidationMetrics = CloneMetricSnapshot(
            pending.ValidationMetrics);
        trained.TestMetrics = CloneMetricSnapshot(pending.TestMetrics);
        trained.EpochHistory = (pending.EpochHistory
                                ?? new List<CombatPolicyValueEpochMetrics>())
            .Select(item => CloneEpochMetrics(item, item.Iteration))
            .ToList();
    }

    internal static bool PendingArenaCandidateEligible(
        CombatFoundationPendingArenaCandidate? candidate)
    {
        return candidate?.Model != null
               && candidate.SourceIteration > 0
               && candidate.OfflineHeadRegressionGatePassed
               && candidate.StrategyQuotaGatePassed
               && candidate.FeatureCollisionGatePassed;
    }

    internal static CombatFoundationPendingArenaCandidate?
        BetterPendingArenaCandidate(
            CombatFoundationPendingArenaCandidate? candidate,
            CombatFoundationPendingArenaCandidate? existing)
    {
        if (!PendingArenaCandidateEligible(candidate)) return existing;
        if (!PendingArenaCandidateEligible(existing)) return candidate;
        return ComparePendingArenaCandidates(candidate!, existing!) > 0
            ? candidate
            : existing;
    }

    internal static int ComparePendingArenaCandidates(
        CombatFoundationPendingArenaCandidate candidate,
        CombatFoundationPendingArenaCandidate existing)
    {
        var candidatePrimary = PendingCandidatePrimaryMetric(candidate);
        var existingPrimary = PendingCandidatePrimaryMetric(existing);
        if (candidatePrimary + 0.0000001d < existingPrimary) return 1;
        if (existingPrimary + 0.0000001d < candidatePrimary) return -1;

        var comparison = CompareLower(
            candidate.ValidationMetrics.ValueMae,
            existing.ValidationMetrics.ValueMae);
        if (comparison != 0) return comparison;
        comparison = CompareLower(
            candidate.ValidationMetrics.DeathBrier,
            existing.ValidationMetrics.DeathBrier);
        if (comparison != 0) return comparison;
        comparison = CompareLower(
            candidate.ValidationMetrics.Brier,
            existing.ValidationMetrics.Brier);
        if (comparison != 0) return comparison;
        comparison = CompareHigher(
            candidate.ValidationMetrics.CriticalPolicyAccuracy,
            existing.ValidationMetrics.CriticalPolicyAccuracy);
        if (comparison != 0) return comparison;
        comparison = CompareHigher(
            candidate.ValidationMetrics.PolicyAccuracy,
            existing.ValidationMetrics.PolicyAccuracy);
        if (comparison != 0) return comparison;
        if (candidate.SourceIteration != existing.SourceIteration)
        {
            return candidate.SourceIteration > existing.SourceIteration
                ? 1
                : -1;
        }
        return string.CompareOrdinal(
                   existing.Model?.ModelId ?? "",
                   candidate.Model?.ModelId ?? "") > 0
            ? 1
            : 0;
    }

    private static double PendingCandidatePrimaryMetric(
        CombatFoundationPendingArenaCandidate candidate)
    {
        return MetricAvailable(candidate.SelectionAnchorMetrics)
            ? candidate.SelectionAnchorMetrics.CompositeLoss
            : candidate.ValidationMetrics.CompositeLoss;
    }

    private static int CompareLower(double candidate, double existing)
    {
        if (candidate + 0.0000001d < existing) return 1;
        if (existing + 0.0000001d < candidate) return -1;
        return 0;
    }

    private sealed class FoundationWorkingModelBank
    {
        private readonly Dictionary<string, FoundationWorkingModelEntry> slots =
            new(StringComparer.Ordinal);
        private readonly CombatPolicyValueNetworkDefinition? fallback;

        public FoundationWorkingModelBank(
            CombatPolicyValueNetworkDefinition? fallbackModel,
            CombatCampaignFoundationIteration? fallbackEvidence = null,
            CombatPolicyValueNetworkDefinition? qualifiedModel = null,
            CombatCampaignFoundationIteration? qualifiedEvidence = null)
        {
            fallback = fallbackModel;
            if (fallbackModel != null && fallbackEvidence != null)
            {
                AddCandidate(fallbackModel, fallbackEvidence);
            }
            if (qualifiedModel != null
                && qualifiedEvidence?.AbsoluteQualificationGatePassed == true
                && (qualifiedEvidence.NonInferiorityGatePassed
                    || !qualifiedEvidence.HadIncumbentModel)
                && string.Equals(
                    qualifiedEvidence.CandidateModelId,
                    qualifiedModel.ModelId,
                    StringComparison.Ordinal))
            {
                slots[CombatFoundationPromotionProtocol.AbsoluteQualifiedBest] =
                    CreateEntry(qualifiedModel, qualifiedEvidence);
            }
        }

        public FoundationWorkingModelEntry? QualifiedBest =>
            slots.TryGetValue(
                CombatFoundationPromotionProtocol.AbsoluteQualifiedBest,
                out var qualified)
                ? qualified
                : null;

        public IReadOnlyList<string> AddCandidate(
            CombatPolicyValueNetworkDefinition model,
            CombatCampaignFoundationIteration iteration)
        {
            if (model == null
                || iteration == null
                || !iteration.OfflineHeadRegressionGatePassed
                || !iteration.FeatureCollisionGatePassed
                || !iteration.ParetoProgress
                   && !iteration.WorkingModelAccepted
                   && !iteration.AbsoluteQualificationGatePassed)
            {
                return Array.Empty<string>();
            }
            var candidate = CreateEntry(model, iteration);
            var updated = new List<string>();
            TryUpdate("normal-best", candidate, updated);
            TryUpdate("advanced-best", candidate, updated);
            TryUpdate("balanced-best", candidate, updated);
            if (iteration.AbsoluteQualificationGatePassed
                && (iteration.NonInferiorityGatePassed
                    || !iteration.HadIncumbentModel))
            {
                TryUpdate(
                    CombatFoundationPromotionProtocol.AbsoluteQualifiedBest,
                    candidate,
                    updated);
            }
            return updated;
        }

        private static FoundationWorkingModelEntry CreateEntry(
            CombatPolicyValueNetworkDefinition model,
            CombatCampaignFoundationIteration iteration)
        {
            return new FoundationWorkingModelEntry
            {
                Model = model,
                Evidence = iteration,
                NormalWinRate = iteration.CandidateNormalWinRate,
                AdvancedWinRate = iteration.CandidateAdvancedWinRate,
                ValidationLoss = MetricAvailable(iteration.ModelValidationMetrics)
                    ? iteration.ModelValidationMetrics.CompositeLoss
                    : double.PositiveInfinity
            };
        }

        public CombatPolicyValueNetworkDefinition? Select(
            string preferredSlot,
            CombatPolicyValueNetworkDefinition? current)
        {
            if (slots.TryGetValue(preferredSlot ?? "", out var preferred))
            {
                return preferred.Model;
            }
            if (slots.TryGetValue("balanced-best", out var balanced))
            {
                return balanced.Model;
            }
            return current ?? fallback;
        }

        private void TryUpdate(
            string slot,
            FoundationWorkingModelEntry candidate,
            ICollection<string> updated)
        {
            if (slots.TryGetValue(slot, out var existing)
                && (string.Equals(
                        slot,
                        CombatFoundationPromotionProtocol.AbsoluteQualifiedBest,
                        StringComparison.Ordinal)
                    ? CompareAbsoluteQualifiedCandidates(
                        candidate.Evidence,
                        existing.Evidence)
                    : CompareWorkingModelSlot(slot, candidate, existing)) <= 0)
            {
                return;
            }
            slots[slot] = candidate;
            updated.Add(slot);
        }
    }

    internal static int CompareAbsoluteQualifiedCandidates(
        CombatCampaignFoundationIteration candidate,
        CombatCampaignFoundationIteration existing)
    {
        if (candidate == null) return -1;
        if (existing == null) return 1;
        if (candidate.AbsoluteQualificationGatePassed
            != existing.AbsoluteQualificationGatePassed)
        {
            return candidate.AbsoluteQualificationGatePassed ? 1 : -1;
        }

        var candidateScore = QualifiedCandidateArenaScore(candidate);
        var existingScore = QualifiedCandidateArenaScore(existing);
        var comparison = CompareHigher(candidateScore, existingScore);
        if (comparison != 0) return comparison;
        comparison = CompareHigher(
            candidate.CandidateAdvancedWinRate,
            existing.CandidateAdvancedWinRate);
        if (comparison != 0) return comparison;
        comparison = CompareHigher(
            candidate.CandidateNormalWinRate,
            existing.CandidateNormalWinRate);
        if (comparison != 0) return comparison;
        comparison = CompareHigher(
            candidate.CandidateArenaScore,
            existing.CandidateArenaScore);
        if (comparison != 0) return comparison;
        comparison = CompareHigher(
            candidate.CandidateAverageCompletedBattles,
            existing.CandidateAverageCompletedBattles);
        if (comparison != 0) return comparison;

        var candidateLoss = MetricAvailable(candidate.ModelValidationMetrics)
            ? candidate.ModelValidationMetrics.CompositeLoss
            : double.PositiveInfinity;
        var existingLoss = MetricAvailable(existing.ModelValidationMetrics)
            ? existing.ModelValidationMetrics.CompositeLoss
            : double.PositiveInfinity;
        if (candidateLoss + 0.0000001d < existingLoss) return 1;
        if (existingLoss + 0.0000001d < candidateLoss) return -1;
        return string.CompareOrdinal(
                   existing.CandidateModelId ?? "",
                   candidate.CandidateModelId ?? "") > 0
            ? 1
            : 0;
    }

    internal static double QualifiedCandidateArenaScore(
        CombatCampaignFoundationIteration iteration)
    {
        if (iteration == null) return double.NegativeInfinity;
        return Math.Min(
                   iteration.CandidateNormalWinRate,
                   iteration.CandidateAdvancedWinRate) * 2d
               + (iteration.CandidateNormalWinRate
                  + iteration.CandidateAdvancedWinRate) * 0.25d;
    }

    private static int CompareHigher(double candidate, double existing)
    {
        if (candidate > existing + 0.0000001d) return 1;
        if (candidate + 0.0000001d < existing) return -1;
        return 0;
    }

    private static int CompareWorkingModelSlot(
        string slot,
        FoundationWorkingModelEntry candidate,
        FoundationWorkingModelEntry existing)
    {
        var candidatePrimary = WorkingModelSlotScore(slot, candidate);
        var existingPrimary = WorkingModelSlotScore(slot, existing);
        if (candidatePrimary > existingPrimary + 0.0000001d)
        {
            return 1;
        }
        if (candidatePrimary + 0.0000001d < existingPrimary)
        {
            return -1;
        }
        return candidate.ValidationLoss + 0.0000001d < existing.ValidationLoss
            ? 1
            : 0;
    }

    private static double WorkingModelSlotScore(
        string slot,
        FoundationWorkingModelEntry entry)
    {
        if (string.Equals(slot, "normal-best", StringComparison.Ordinal))
        {
            return entry.NormalWinRate * 2d + entry.AdvancedWinRate * 0.25d;
        }
        if (string.Equals(slot, "advanced-best", StringComparison.Ordinal))
        {
            return entry.AdvancedWinRate * 2d + entry.NormalWinRate * 0.25d;
        }
        return Math.Min(entry.NormalWinRate, entry.AdvancedWinRate) * 2d
               + (entry.NormalWinRate + entry.AdvancedWinRate) * 0.25d;
    }

    private sealed class FoundationWorkingModelEntry
    {
        public CombatPolicyValueNetworkDefinition Model { get; set; } = new();

        public CombatCampaignFoundationIteration Evidence { get; set; } = new();

        public double NormalWinRate { get; set; }

        public double AdvancedWinRate { get; set; }

        public double ValidationLoss { get; set; }
    }

    private sealed class FoundationTelemetryTracker
    {
        private readonly CombatCampaignFoundationTrainingRequest request;
        private int effectiveParallelism;
        private CombatFoundationParallelismDecision parallelismDecision =
            new();
        private readonly int requestedCampaigns;
        private readonly int totalIterations;
        private readonly int runStartIteration;
        private readonly int runTotalIterations;
        private readonly int runInitialCompletedCampaigns;
        private readonly int runInitialExecutedCampaigns;
        private readonly int runInitialCompletedBattles;
        private readonly long runInitialSearchSimulations;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly Process process = Process.GetCurrentProcess();
        private readonly TimeSpan initialCpuTime;
        private readonly long initialAllocatedBytes;
        private readonly CombatPolicyValueBatchDiagnosticsSnapshot
            initialInferenceDiagnostics;
        private readonly double elapsedSecondsOffset;
        private readonly double cpuSecondsOffset;
        private readonly long allocatedBytesOffset;
        private readonly object workerGate = new();
        private readonly HashSet<int> observedWorkerThreads = new();
        private readonly Dictionary<long, int> activeCampaignDepths = new();
        private readonly Dictionary<long, string> activeCampaignPhases = new();
        private readonly Dictionary<string, int> phaseActiveWork =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> phasePeakConcurrentWork =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<int>> phaseWorkerThreads =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int>
            phaseObservedWorkerThreadOffsets =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly int[] completedDepthBuckets = new int[5];
        private readonly int initialGen0 = GC.CollectionCount(0);
        private readonly int initialGen1 = GC.CollectionCount(1);
        private readonly int initialGen2 = GC.CollectionCount(2);
        private readonly CombatEpisodeStorageSnapshot initialEpisodeStorage;
        private int activeCampaigns;
        private int peakConcurrentCampaigns;
        private int schedulerQueuedWork;
        private int schedulerRunningWork;
        private int schedulerCompletedWork;
        private int schedulerCommittedWork;
        private int schedulerPeakRunningWork;
        private long schedulerRefillCount;
        private int schedulerSpeculativeDiscardedWork;
        private double schedulerTailIdleCoreSeconds;
        private int completedCampaigns;
        private int executedCampaigns;
        private int completedBattles;
        private int maximumCompletedBattleDepth;
        private long completedCampaignDepthTotal;
        private int completedCampaignDepthCount;
        private long policyDecisions;
        private long searchSimulations;
        private long searchNodes;
        private long searchMicroseconds;
        private long observationProjectionAllocatedBytes;
        private long decisionEngineAllocatedBytes;
        private long searchModelEvaluations;
        private long searchModelCacheHits;
        private long searchOriginalCandidates;
        private long searchRetainedCandidates;
        private int searchTimeBudgetStops;
        private int searchModelBudgetStops;
        private int searchEarlyStops;
        private readonly Dictionary<string, int> searchBudgetTierCounts =
            new(StringComparer.OrdinalIgnoreCase);
        private int ruleTerminalOverrides;
        private int certifiedLoops;
        private int sustainableControlLoops;
        private int fakeLoops;
        private int blockedLoops;
        private long explorationDecisions;
        private long explorationActionOverrides;
        private double rootMaximumVisitShareTotal;
        private int rootMaximumVisitShareSamples;
        private long authoritativeActionsAudited;
        private long authoritativeSemanticMismatches;
        private long authoritativeSelectedActionsAudited;
        private long authoritativeSelectedSemanticMismatches;
        private long authoritativeTeacherOverrides;
        private readonly Dictionary<string, int>
            authoritativeSemanticMismatchKinds =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int>
            authoritativeSemanticMismatchSources =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int>
            authoritativeSemanticMismatchScenarios =
                new(StringComparer.OrdinalIgnoreCase);
        private readonly CombatSemanticAuditMetrics semanticAudit = new();
        private readonly Dictionary<string, double> phaseElapsedSeconds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> phaseCpuSeconds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> phaseAllocatedBytes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> phaseExternalCpuSeconds =
            new(StringComparer.OrdinalIgnoreCase);
        private string currentPhase = "setup";
        private double currentPhaseStartedSeconds;
        private double currentPhaseStartedCpuSeconds;
        private long currentPhaseStartedAllocatedBytes;
        private int currentPhaseStartedExecutedCampaigns;
        private int currentPhaseStartedBattles;
        private int currentPhaseRequestedCampaigns;
        private long currentPhaseStartedSearchSimulations;
        private long nextCampaignWorkId;
        private long lastReportMilliseconds = -1000L;
        private int modelIteration;
        private int modelEpoch;
        private int modelTotalEpochs;
        private int modelCompletedFrames;
        private int modelTotalFrames;
        private double modelEpochsPerSecond;
        private double modelTrainingLoss;
        private double modelValidationLoss;
        private double modelBestValidationLoss;
        private int modelBestEpoch;
        private int modelStaleEpochs;
        private bool modelEarlyStopped;
        private double phaseEstimatedRemainingSeconds;
        private string transformerTeacherStage = "";
        private int transformerTeacherEpoch;
        private int transformerTeacherTotalEpochs;
        private int transformerTeacherCompletedFrames;
        private int transformerTeacherTotalFrames;
        private double transformerTeacherFramesPerSecond;
        private double transformerTeacherElapsedSeconds;
        private double transformerTeacherCpuPercent;
        private double transformerTeacherProcessCpuSeconds;
        private double transformerTeacherAccumulatedCpuSeconds;
        private int transformerTeacherCpuIteration;
        private long transformerTeacherWorkingSetBytes;
        private long transformerTeacherPeakWorkingSetBytes;
        private double transformerTeacherStageElapsedSeconds;
        private bool transformerTeacherWarmStarted;
        private bool transformerTeacherTrainingEnabled;
        private string transformerTeacherMessage = "";
        private readonly List<CombatPolicyValueEpochMetrics> modelEpochHistory =
            new();

        public FoundationTelemetryTracker(
            CombatCampaignFoundationTrainingRequest request,
            int effectiveParallelism,
            int requestedCampaigns,
            int totalIterations,
            CombatCampaignFoundationTelemetry? initial = null,
            int initialCompletedCampaigns = 0,
            int runStartIteration = 1,
            int runTotalIterations = 1)
        {
            this.request = request;
            this.effectiveParallelism = effectiveParallelism;
            parallelismDecision = initial?.ParallelismDecision
                                  ?? new CombatFoundationParallelismDecision
                                  {
                                      SelectedParallelism =
                                          effectiveParallelism,
                                      RequestedMaximumParallelism =
                                          effectiveParallelism
                                  };
            this.requestedCampaigns = requestedCampaigns;
            this.totalIterations = Math.Max(1, totalIterations);
            this.runStartIteration = Math.Max(1, runStartIteration);
            this.runTotalIterations = Math.Max(0, runTotalIterations);
            initialCpuTime = process.TotalProcessorTime;
            initialAllocatedBytes = ReadManagedAllocationCounter();
            initialInferenceDiagnostics =
                CombatPolicyValueBatchDiagnostics.Capture();
            initialEpisodeStorage = CombatEpisodeStorageDiagnostics.Capture();
            elapsedSecondsOffset = Math.Max(
                0d,
                initial?.ElapsedSeconds ?? 0d);
            cpuSecondsOffset = Math.Max(0d, initial?.CpuSeconds ?? 0d);
            allocatedBytesOffset = Math.Max(
                0L,
                initial?.AllocatedBytes ?? 0L);
            foreach (var pair in initial?.PhaseElapsedSeconds
                                 ?? new Dictionary<string, double>())
            {
                phaseElapsedSeconds[pair.Key] = Math.Max(0d, pair.Value);
            }
            foreach (var pair in initial?.PhaseCpuSeconds
                                 ?? new Dictionary<string, double>())
            {
                phaseCpuSeconds[pair.Key] = Math.Max(0d, pair.Value);
            }
            foreach (var pair in initial?.PhaseAllocatedBytes
                                 ?? new Dictionary<string, long>())
            {
                phaseAllocatedBytes[pair.Key] = Math.Max(0L, pair.Value);
            }
            foreach (var pair in initial?.PhaseExternalCpuSeconds
                                 ?? new Dictionary<string, double>())
            {
                phaseExternalCpuSeconds[pair.Key] = Math.Max(0d, pair.Value);
            }
            foreach (var pair in initial?.PhasePeakConcurrentWork
                                 ?? new Dictionary<string, int>())
            {
                phasePeakConcurrentWork[pair.Key] = Math.Max(0, pair.Value);
            }
            foreach (var pair in initial?.PhaseObservedWorkerThreads
                                 ?? new Dictionary<string, int>())
            {
                phaseObservedWorkerThreadOffsets[pair.Key] =
                    Math.Max(0, pair.Value);
            }
            completedCampaigns = Math.Max(
                initialCompletedCampaigns,
                initial?.CompletedCampaigns ?? 0);
            executedCampaigns = Math.Max(
                completedCampaigns,
                initial?.ExecutedCampaigns ?? 0);
            completedBattles = Math.Max(0, initial?.CompletedBattles ?? 0);
            runInitialCompletedCampaigns = completedCampaigns;
            runInitialExecutedCampaigns = executedCampaigns;
            runInitialCompletedBattles = completedBattles;
            runInitialSearchSimulations = Math.Max(
                0L,
                initial?.SearchSimulations ?? 0L);
            maximumCompletedBattleDepth = Math.Max(
                0,
                initial?.MaximumCompletedBattleDepth ?? 0);
            if (initial != null)
            {
                completedDepthBuckets[0] = initial.Depth1To5Campaigns;
                completedDepthBuckets[1] = initial.Depth6To10Campaigns;
                completedDepthBuckets[2] = initial.Depth11To20Campaigns;
                completedDepthBuckets[3] = initial.Depth21To30Campaigns;
                completedDepthBuckets[4] = initial.Depth31To37Campaigns;
                completedCampaignDepthCount = completedDepthBuckets.Sum();
                completedCampaignDepthTotal = (long)Math.Round(
                    initial.ProjectedBattleDepth
                    * completedCampaignDepthCount);
                policyDecisions = Math.Max(0L, initial.PolicyDecisions);
                searchSimulations = Math.Max(0L, initial.SearchSimulations);
                searchNodes = Math.Max(0L, initial.SearchNodes);
                searchMicroseconds = Math.Max(
                    0L,
                    (long)Math.Round(initial.SearchMillisecondsTotal * 1000d));
                searchModelEvaluations = Math.Max(
                    0L,
                    initial.SearchModelEvaluations);
                searchModelCacheHits = Math.Max(
                    0L,
                    initial.SearchModelCacheHits);
                searchOriginalCandidates = Math.Max(
                    0L,
                    initial.SearchOriginalCandidates);
                searchRetainedCandidates = Math.Max(
                    0L,
                    initial.SearchRetainedCandidates);
                searchTimeBudgetStops = Math.Max(
                    0,
                    initial.SearchTimeBudgetStops);
                searchModelBudgetStops = Math.Max(
                    0,
                    initial.SearchModelBudgetStops);
                searchEarlyStops = Math.Max(0, initial.SearchEarlyStops);
                foreach (var pair in initial.SearchBudgetTierCounts
                             ?? new Dictionary<string, int>())
                {
                    searchBudgetTierCounts[pair.Key] = Math.Max(0, pair.Value);
                }
                ruleTerminalOverrides = Math.Max(
                    0,
                    initial.RuleTerminalOverrides);
                certifiedLoops = Math.Max(0, initial.CertifiedLoops);
                sustainableControlLoops = Math.Max(
                    0,
                    initial.SustainableControlLoops);
                fakeLoops = Math.Max(0, initial.FakeLoops);
                blockedLoops = Math.Max(0, initial.BlockedLoops);
                explorationDecisions = Math.Max(
                    0L,
                    initial.ExplorationDecisions);
                explorationActionOverrides = Math.Max(
                    0L,
                    initial.ExplorationActionOverrides);
                rootMaximumVisitShareSamples = Math.Max(
                    0,
                    initial.RootMaximumVisitShareSamples);
                rootMaximumVisitShareTotal =
                    Math.Max(0d, initial.RootMaximumVisitShareMean)
                    * rootMaximumVisitShareSamples;
                authoritativeActionsAudited = Math.Max(
                    0L,
                    initial.AuthoritativeActionsAudited);
                authoritativeSemanticMismatches = Math.Max(
                    0L,
                    initial.AuthoritativeSemanticMismatches);
                authoritativeSelectedActionsAudited = Math.Max(
                    0L,
                    initial.AuthoritativeSelectedActionsAudited);
                authoritativeSelectedSemanticMismatches = Math.Max(
                    0L,
                    initial.AuthoritativeSelectedSemanticMismatches);
                authoritativeTeacherOverrides = Math.Max(
                    0L,
                    initial.AuthoritativeTeacherOverrides);
                MergeCounts(
                    authoritativeSemanticMismatchKinds,
                    initial.AuthoritativeSemanticMismatchKinds);
                MergeCounts(
                    authoritativeSemanticMismatchSources,
                    initial.AuthoritativeSemanticMismatchSources);
                MergeCounts(
                    authoritativeSemanticMismatchScenarios,
                    initial.AuthoritativeSemanticMismatchScenarios);
                semanticAudit.MergeFrom(initial.SemanticAudit);
                peakConcurrentCampaigns = Math.Max(
                    0,
                    initial.PeakConcurrentCampaigns);
                modelIteration = Math.Max(0, initial.Iteration);
                modelEpoch = Math.Max(0, initial.ModelEpoch);
                modelTotalEpochs = Math.Max(0, initial.ModelTotalEpochs);
                modelCompletedFrames = Math.Max(
                    0,
                    initial.ModelCompletedFrames);
                modelTotalFrames = Math.Max(0, initial.ModelTotalFrames);
                modelEpochsPerSecond = Math.Max(
                    0d,
                    initial.ModelEpochsPerSecond);
                modelTrainingLoss = Math.Max(0d, initial.ModelTrainingLoss);
                modelValidationLoss = Math.Max(0d, initial.ModelValidationLoss);
                modelBestValidationLoss = Math.Max(
                    0d,
                    initial.ModelBestValidationLoss);
                modelBestEpoch = Math.Max(0, initial.ModelBestEpoch);
                modelStaleEpochs = Math.Max(0, initial.ModelStaleEpochs);
                modelEarlyStopped = initial.ModelEarlyStopped;
                modelEpochHistory.AddRange(
                    (initial.ModelEpochHistory
                     ?? new List<CombatPolicyValueEpochMetrics>())
                    .Select(item => CloneEpochMetrics(item)));
            }
            currentPhaseStartedExecutedCampaigns = executedCampaigns;
            currentPhaseStartedBattles = completedBattles;
            currentPhaseStartedSearchSimulations = searchSimulations;
        }

        public CombatCampaignFoundationTelemetry Current(string stage)
        {
            return Snapshot(stage);
        }

        public void BeginIteration(int iteration)
        {
            lock (workerGate)
            {
                modelIteration = Math.Max(1, iteration);
            }
            Report(
                "iteration:"
                + Math.Max(1, iteration)
                + ":starting",
                force: true);
        }

        public void ModelTrainingProgress(
            int iteration,
            int totalIterations,
            CombatPolicyValueTrainingProgress progress)
        {
            if (progress == null)
            {
                return;
            }
            lock (workerGate)
            {
                modelIteration = Math.Max(0, iteration);
                modelEpoch = Math.Max(0, progress.Epoch);
                modelTotalEpochs = Math.Max(0, progress.TotalEpochs);
                modelCompletedFrames = Math.Max(
                    0,
                    progress.CompletedFrames);
                modelTotalFrames = Math.Max(0, progress.TotalFrames);
                modelEpochsPerSecond = Math.Max(
                    0d,
                    progress.EpochsPerSecond);
                if (progress.ValidationLoss > 0d
                    || string.Equals(
                        progress.Stage,
                        "completed",
                        StringComparison.Ordinal)
                    || string.Equals(
                        progress.Stage,
                        "early-stopped",
                        StringComparison.Ordinal))
                {
                    modelValidationLoss = progress.ValidationLoss;
                }
                if (progress.Metrics != null)
                {
                    var metrics = CloneEpochMetrics(
                        progress.Metrics,
                        Math.Max(1, iteration));
                    modelTrainingLoss =
                        Math.Max(0d, metrics.Training.CompositeLoss);
                    modelEpochHistory.RemoveAll(item =>
                        item.Iteration == metrics.Iteration
                        && item.Epoch == metrics.Epoch
                        && item.Calibrated == metrics.Calibrated);
                    modelEpochHistory.Add(metrics);
                }
                modelBestValidationLoss = progress.BestValidationLoss;
                modelBestEpoch = Math.Max(0, progress.BestEpoch);
                modelStaleEpochs = Math.Max(0, progress.StaleEpochs);
                modelEarlyStopped = progress.EarlyStopped;
                phaseEstimatedRemainingSeconds = Math.Max(
                    0d,
                    progress.EstimatedRemainingSeconds);
            }
            var terminal = string.Equals(
                               progress.Stage,
                               "completed",
                               StringComparison.Ordinal)
                           || string.Equals(
                               progress.Stage,
                               "early-stopped",
                               StringComparison.Ordinal);
            Report(
                "model-training:"
                + Math.Max(1, iteration)
                + ":"
                + (progress.Stage ?? "training"),
                force: terminal);
        }

        public void TransformerTeacherProgress(
            CombatTransformerTeacherProgress progress)
        {
            if (progress == null)
            {
                return;
            }
            lock (workerGate)
            {
                modelIteration = Math.Max(1, progress.Iteration);
                transformerTeacherStage = progress.Stage ?? "";
                transformerTeacherEpoch = Math.Max(0, progress.Epoch);
                transformerTeacherTotalEpochs = Math.Max(
                    0,
                    progress.TotalEpochs);
                transformerTeacherCompletedFrames = Math.Max(
                    0,
                    progress.CompletedFrames);
                transformerTeacherTotalFrames = Math.Max(
                    0,
                    progress.TotalFrames);
                transformerTeacherFramesPerSecond = Math.Max(
                    0d,
                    progress.FramesPerSecond);
                transformerTeacherElapsedSeconds = Math.Max(
                    0d,
                    progress.ElapsedSeconds);
                transformerTeacherCpuPercent = Math.Max(
                    0d,
                    progress.ProcessCpuPercent);
                if (transformerTeacherCpuIteration > 0
                    && transformerTeacherCpuIteration != progress.Iteration)
                {
                    transformerTeacherAccumulatedCpuSeconds +=
                        transformerTeacherProcessCpuSeconds;
                    transformerTeacherProcessCpuSeconds = 0d;
                }
                transformerTeacherCpuIteration = Math.Max(
                    1,
                    progress.Iteration);
                transformerTeacherProcessCpuSeconds = Math.Max(
                    transformerTeacherProcessCpuSeconds,
                    Math.Max(0d, progress.ProcessCpuSeconds));
                transformerTeacherWorkingSetBytes = Math.Max(
                    0L,
                    progress.WorkingSetBytes);
                transformerTeacherPeakWorkingSetBytes = Math.Max(
                    transformerTeacherPeakWorkingSetBytes,
                    Math.Max(
                        progress.WorkingSetBytes,
                        progress.PeakWorkingSetBytes));
                transformerTeacherStageElapsedSeconds = Math.Max(
                    0d,
                    progress.StageElapsedSeconds);
                transformerTeacherWarmStarted = progress.WarmStarted;
                transformerTeacherTrainingEnabled = progress.TrainingEnabled;
                transformerTeacherMessage = progress.Message ?? "";
                phaseEstimatedRemainingSeconds = Math.Max(
                    0d,
                    progress.EstimatedRemainingSeconds);
            }
            Report(
                "transformer-teacher:"
                + Math.Max(1, progress.Iteration)
                + ":"
                + (progress.Stage ?? "working"),
                force: true);
        }

        public void ModelSelection(
            int iteration,
            int epoch,
            CombatPolicyValueMetricSnapshot training,
            CombatPolicyValueMetricSnapshot validation)
        {
            CombatPolicyValueEpochMetrics metrics;
            lock (workerGate)
            {
                modelIteration = Math.Max(1, iteration);
                modelEpoch = Math.Max(1, epoch);
                modelTrainingLoss = Math.Max(0d, training.CompositeLoss);
                modelValidationLoss = Math.Max(
                    0d,
                    validation.CompositeLoss);
                metrics = new CombatPolicyValueEpochMetrics
                {
                    Iteration = modelIteration,
                    Epoch = modelEpoch,
                    Calibrated = true,
                    EventKind = "selected",
                    TrainingMeasurement = "full-evaluation",
                    Training = CloneMetricSnapshot(training),
                    Validation = CloneMetricSnapshot(validation)
                };
                modelEpochHistory.RemoveAll(item =>
                    item.Iteration == metrics.Iteration
                    && item.Epoch == metrics.Epoch
                    && item.Calibrated);
                modelEpochHistory.Add(metrics);
            }
            try
            {
                request.ModelMetricRecorded?.Invoke(
                    CloneEpochMetrics(metrics));
            }
            catch
            {
                // Independent diagnostics must not abort training.
            }
            Report(
                "model-training:"
                + Math.Max(1, iteration)
                + ":selected",
                force: true);
        }

        public long EnterCampaign(string stage)
        {
            var workId = Interlocked.Increment(ref nextCampaignWorkId);
            lock (workerGate)
            {
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var phase = currentPhase;
                observedWorkerThreads.Add(threadId);
                activeCampaignDepths[workId] = 0;
                activeCampaignPhases[workId] = phase;
                var phaseActive = phaseActiveWork.TryGetValue(
                    phase,
                    out var currentActive)
                    ? currentActive + 1
                    : 1;
                phaseActiveWork[phase] = phaseActive;
                phasePeakConcurrentWork[phase] = Math.Max(
                    phasePeakConcurrentWork.TryGetValue(
                        phase,
                        out var currentPeak)
                        ? currentPeak
                        : 0,
                    phaseActive);
                if (!phaseWorkerThreads.TryGetValue(
                        phase,
                        out var workerThreads))
                {
                    workerThreads = new HashSet<int>();
                    phaseWorkerThreads[phase] = workerThreads;
                }
                workerThreads.Add(threadId);
            }
            var active = Interlocked.Increment(ref activeCampaigns);
            UpdateMaximum(ref peakConcurrentCampaigns, active);
            Report(stage, force: active == Volatile.Read(ref peakConcurrentCampaigns));
            return workId;
        }

        public void ExitCampaign(
            long workId,
            CombatCampaignResult? result,
            string stage)
        {
            lock (workerGate)
            {
                activeCampaignDepths.Remove(workId);
                if (activeCampaignPhases.TryGetValue(
                        workId,
                        out var phase))
                {
                    activeCampaignPhases.Remove(workId);
                    phaseActiveWork[phase] = Math.Max(
                        0,
                        (phaseActiveWork.TryGetValue(
                            phase,
                            out var active)
                            ? active
                            : 0) - 1);
                }
                if (result != null
                    && !stage.StartsWith(
                        "preflight",
                        StringComparison.Ordinal))
                {
                    var depth = Math.Max(0, result.CompletedBattles);
                    completedDepthBuckets[DepthBucket(depth)]++;
                    completedCampaignDepthTotal += depth;
                    completedCampaignDepthCount++;
                    maximumCompletedBattleDepth = Math.Max(
                        maximumCompletedBattleDepth,
                        depth);
                }
            }
            if (result != null)
            {
                Interlocked.Increment(ref executedCampaigns);
            }
            Interlocked.Decrement(ref activeCampaigns);
            Report(stage);
        }

        public void BattleCompleted(
            long workId,
            int depth,
            CombatSimulationResult battle,
            string stage)
        {
            lock (workerGate)
            {
                activeCampaignDepths[workId] = Math.Max(0, depth);
            }
            Interlocked.Increment(ref completedBattles);
            if (battle?.Metrics != null)
            {
                Interlocked.Add(
                    ref policyDecisions,
                    Math.Max(0, battle.Metrics.PolicyDecisions));
                Interlocked.Add(
                    ref searchSimulations,
                    Math.Max(0L, battle.Metrics.SearchSimulations));
                Interlocked.Add(
                    ref searchNodes,
                    Math.Max(0L, battle.Metrics.SearchNodes));
                Interlocked.Add(
                    ref searchMicroseconds,
                    Math.Max(
                        0L,
                        (long)Math.Round(
                            battle.Metrics.SearchMillisecondsTotal * 1000d)));
                Interlocked.Add(
                    ref observationProjectionAllocatedBytes,
                    Math.Max(
                        0L,
                        battle.Metrics.ObservationProjectionAllocatedBytes));
                Interlocked.Add(
                    ref decisionEngineAllocatedBytes,
                    Math.Max(
                        0L,
                        battle.Metrics.DecisionEngineAllocatedBytes));
                Interlocked.Add(
                    ref searchModelEvaluations,
                    Math.Max(0L, battle.Metrics.ModelEvaluations));
                Interlocked.Add(
                    ref searchModelCacheHits,
                    Math.Max(0L, battle.Metrics.ModelCacheHits));
                Interlocked.Add(
                    ref searchOriginalCandidates,
                    Math.Max(0L, battle.Metrics.OriginalSearchCandidates));
                Interlocked.Add(
                    ref searchRetainedCandidates,
                    Math.Max(0L, battle.Metrics.RetainedSearchCandidates));
                Interlocked.Add(
                    ref searchTimeBudgetStops,
                    Math.Max(0, battle.Metrics.SearchTimeBudgetStops));
                Interlocked.Add(
                    ref searchModelBudgetStops,
                    Math.Max(0, battle.Metrics.SearchModelBudgetStops));
                Interlocked.Add(
                    ref searchEarlyStops,
                    Math.Max(0, battle.Metrics.SearchEarlyStops));
                lock (workerGate)
                {
                    foreach (var pair in battle.Metrics.SearchBudgetTierCounts
                                 ?? new Dictionary<string, int>())
                    {
                        searchBudgetTierCounts[pair.Key] =
                            searchBudgetTierCounts.TryGetValue(
                                pair.Key,
                                out var current)
                                ? current + Math.Max(0, pair.Value)
                                : Math.Max(0, pair.Value);
                    }
                }
                Interlocked.Add(
                    ref ruleTerminalOverrides,
                    Math.Max(0, battle.Metrics.RuleTerminalOverrides));
                Interlocked.Add(
                    ref certifiedLoops,
                    Math.Max(0, battle.Metrics.CertifiedLoops));
                Interlocked.Add(
                    ref sustainableControlLoops,
                    Math.Max(0, battle.Metrics.SustainableControlLoops));
                Interlocked.Add(
                    ref fakeLoops,
                    Math.Max(0, battle.Metrics.FakeLoops));
                Interlocked.Add(
                    ref blockedLoops,
                    Math.Max(0, battle.Metrics.BlockedLoops));
                Interlocked.Add(
                    ref explorationDecisions,
                    Math.Max(0, battle.Metrics.ExplorationDecisions));
                Interlocked.Add(
                    ref explorationActionOverrides,
                    Math.Max(
                        0,
                        battle.Metrics.ExplorationActionOverrides));
                Interlocked.Add(
                    ref authoritativeActionsAudited,
                    Math.Max(
                        0,
                        battle.Metrics.AuthoritativeActionsAudited));
                Interlocked.Add(
                    ref authoritativeSemanticMismatches,
                    Math.Max(
                        0,
                        battle.Metrics
                            .AuthoritativeSemanticMismatches));
                Interlocked.Add(
                    ref authoritativeSelectedActionsAudited,
                    Math.Max(
                        0,
                        battle.Metrics.AuthoritativeSelectedActionsAudited));
                Interlocked.Add(
                    ref authoritativeSelectedSemanticMismatches,
                    Math.Max(
                        0,
                        battle.Metrics
                            .AuthoritativeSelectedSemanticMismatches));
                Interlocked.Add(
                    ref authoritativeTeacherOverrides,
                    Math.Max(
                        0,
                        battle.Metrics.AuthoritativeTeacherOverrides));
                lock (workerGate)
                {
                    MergeCounts(
                        authoritativeSemanticMismatchKinds,
                        battle.Metrics.AuthoritativeSemanticMismatchKinds);
                    MergeCounts(
                        authoritativeSemanticMismatchSources,
                        battle.Metrics.AuthoritativeSemanticMismatchSources);
                    MergeCounts(
                        authoritativeSemanticMismatchScenarios,
                        battle.Metrics.AuthoritativeSemanticMismatchScenarios);
                    semanticAudit.MergeFrom(battle.Metrics.SemanticAudit);
                    rootMaximumVisitShareTotal += Math.Max(
                        0d,
                        battle.Metrics.RootMaximumVisitShareTotal);
                    rootMaximumVisitShareSamples += Math.Max(
                        0,
                        battle.Metrics.RootMaximumVisitShareSamples);
                }
            }
            Report(stage);
        }

        public void CampaignCompleted(
            int completed,
            CombatCampaignResult campaign,
            string stage)
        {
            Volatile.Write(ref completedCampaigns, completed);
            Report(stage);
        }

        public void BeginPhase(
            string phase,
            int requestedCampaigns = 0)
        {
            var normalized = string.IsNullOrWhiteSpace(phase)
                ? "unknown"
                : phase.Trim().ToLowerInvariant();
            lock (workerGate)
            {
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var cpu = Math.Max(
                    0d,
                    (process.TotalProcessorTime - initialCpuTime).TotalSeconds);
                var allocated = Math.Max(
                    0L,
                    ReadManagedAllocationCounter() - initialAllocatedBytes);
                if (!string.Equals(
                        currentPhase,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    phaseElapsedSeconds[currentPhase] =
                        phaseElapsedSeconds.TryGetValue(
                            currentPhase,
                            out var accumulated)
                            ? accumulated
                              + Math.Max(
                                  0d,
                                  elapsed - currentPhaseStartedSeconds)
                            : Math.Max(
                                0d,
                                elapsed - currentPhaseStartedSeconds);
                    phaseCpuSeconds[currentPhase] =
                        phaseCpuSeconds.TryGetValue(
                            currentPhase,
                            out var accumulatedCpu)
                            ? accumulatedCpu
                              + Math.Max(
                                  0d,
                                  cpu - currentPhaseStartedCpuSeconds)
                            : Math.Max(
                                0d,
                                cpu - currentPhaseStartedCpuSeconds);
                    phaseAllocatedBytes[currentPhase] =
                        phaseAllocatedBytes.TryGetValue(
                            currentPhase,
                            out var accumulatedAllocated)
                            ? accumulatedAllocated
                              + Math.Max(
                                  0L,
                                  allocated
                                  - currentPhaseStartedAllocatedBytes)
                            : Math.Max(
                                0L,
                                allocated
                                - currentPhaseStartedAllocatedBytes);
                    currentPhase = normalized;
                    currentPhaseStartedSeconds = elapsed;
                    currentPhaseStartedCpuSeconds = cpu;
                    currentPhaseStartedAllocatedBytes = allocated;
                    currentPhaseStartedExecutedCampaigns = Volatile.Read(
                        ref executedCampaigns);
                    currentPhaseStartedBattles = Volatile.Read(
                        ref completedBattles);
                    currentPhaseRequestedCampaigns = Math.Max(
                        0,
                        requestedCampaigns);
                    currentPhaseStartedSearchSimulations = Volatile.Read(
                        ref searchSimulations);
                }
                else if (requestedCampaigns > 0)
                {
                    currentPhaseRequestedCampaigns = Math.Max(
                        requestedCampaigns,
                        Math.Max(
                            0,
                            Volatile.Read(ref executedCampaigns)
                            - currentPhaseStartedExecutedCampaigns));
                }
            }
            Report(normalized, force: true);
        }

        public void SetPhaseCampaignPlan(int requestedCampaigns)
        {
            lock (workerGate)
            {
                currentPhaseRequestedCampaigns = Math.Max(
                    requestedCampaigns,
                    Math.Max(
                        0,
                        Volatile.Read(ref executedCampaigns)
                        - currentPhaseStartedExecutedCampaigns));
            }
            Report(currentPhase, force: true);
        }

        public void ReportStage(string stage)
        {
            Report(stage, force: true);
        }

        public void SetEffectiveParallelism(int value)
        {
            Volatile.Write(ref effectiveParallelism, Math.Max(1, value));
            Report("auto-tune:selected", force: true);
        }

        public void SetParallelismDecision(
            CombatFoundationParallelismDecision decision)
        {
            if (decision == null)
            {
                return;
            }
            lock (workerGate)
            {
                parallelismDecision = decision;
            }
            Volatile.Write(
                ref effectiveParallelism,
                Math.Max(1, decision.SelectedParallelism));
            Report("parallelism:memory-capacity", force: true);
        }

        public void SchedulerProgress(CombatFoundationSchedulerSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            lock (workerGate)
            {
                schedulerQueuedWork = Math.Max(0, snapshot.QueuedWork);
                schedulerRunningWork = Math.Max(0, snapshot.RunningWork);
                schedulerCompletedWork = Math.Max(0, snapshot.CompletedWork);
                schedulerCommittedWork = Math.Max(0, snapshot.CommittedWork);
                schedulerPeakRunningWork = Math.Max(
                    0,
                    snapshot.PeakRunningWork);
                schedulerRefillCount = Math.Max(0L, snapshot.RefillCount);
                schedulerSpeculativeDiscardedWork = Math.Max(
                    0,
                    snapshot.SpeculativeDiscardedWork);
                schedulerTailIdleCoreSeconds = Math.Max(
                    0d,
                    snapshot.TailIdleCoreSeconds);
            }
        }

        public void ApplyTo(CombatCampaignFoundationTrainingResult result)
        {
            var snapshot = Snapshot("completed");
            result.EffectiveParallelism = snapshot.EffectiveParallelism;
            result.ParallelismDecision = snapshot.ParallelismDecision;
            result.ModelTrainingParallelism =
                snapshot.ModelTrainingParallelism;
            result.GovernanceProfile = snapshot.GovernanceProfile;
            result.ParallelismProfile = snapshot.ParallelismProfile;
            result.InferenceExecutionMode =
                snapshot.InferenceExecutionMode;
            result.InferenceParallelism = snapshot.InferenceParallelism;
            result.InferenceLaneCount = snapshot.InferenceLaneCount;
            result.InferenceBatchSizePerLane =
                snapshot.InferenceBatchSizePerLane;
            result.InferenceRequests = snapshot.InferenceRequests;
            result.InferenceBatchEvaluations =
                snapshot.InferenceBatchEvaluations;
            result.InferenceBatchedInputs = snapshot.InferenceBatchedInputs;
            result.InferenceAverageBatchSize =
                snapshot.InferenceAverageBatchSize;
            result.InferenceFullBatchEvaluations =
                snapshot.InferenceFullBatchEvaluations;
            result.InferenceTimeoutFlushes = snapshot.InferenceTimeoutFlushes;
            result.InferenceDirectFallbackRequests =
                snapshot.InferenceDirectFallbackRequests;
            result.InferenceAdaptiveFallbackActivations =
                snapshot.InferenceAdaptiveFallbackActivations;
            result.InferenceAverageWaitMicroseconds =
                snapshot.InferenceAverageWaitMicroseconds;
            result.InferenceDirectEvaluations =
                snapshot.InferenceDirectEvaluations;
            result.InferenceAverageDirectEvaluationMicroseconds =
                snapshot.InferenceAverageDirectEvaluationMicroseconds;
            result.InferenceAverageDirectAllocatedBytes =
                snapshot.InferenceAverageDirectAllocatedBytes;
            result.InferenceAverageSparseFeatureCount =
                snapshot.InferenceAverageSparseFeatureCount;
            result.InferenceSparseFeatureDensity =
                snapshot.InferenceSparseFeatureDensity;
            result.InferenceWeightMultiplicationReduction =
                snapshot.InferenceWeightMultiplicationReduction;
            result.PeakConcurrentCampaigns = snapshot.PeakConcurrentCampaigns;
            result.SchedulerPeakRunningWork = snapshot.SchedulerPeakRunningWork;
            result.SchedulerRefillCount = snapshot.SchedulerRefillCount;
            result.SchedulerSpeculativeDiscardedWork =
                snapshot.SchedulerSpeculativeDiscardedWork;
            result.SchedulerTailIdleCoreSeconds =
                snapshot.SchedulerTailIdleCoreSeconds;
            result.ObservedWorkerThreads = snapshot.ObservedWorkerThreads;
            result.ExecutedCampaigns = snapshot.ExecutedCampaigns;
            result.CompletedBattles = snapshot.CompletedBattles;
            result.MaximumCompletedBattleDepth =
                snapshot.MaximumCompletedBattleDepth;
            result.Depth1To5Campaigns = snapshot.Depth1To5Campaigns;
            result.Depth6To10Campaigns = snapshot.Depth6To10Campaigns;
            result.Depth11To20Campaigns = snapshot.Depth11To20Campaigns;
            result.Depth21To30Campaigns = snapshot.Depth21To30Campaigns;
            result.Depth31To37Campaigns = snapshot.Depth31To37Campaigns;
            result.ProjectedBattleDepth = snapshot.ProjectedBattleDepth;
            result.EstimatedRemainingSeconds =
                snapshot.EstimatedRemainingSeconds;
            result.EstimatedRemainingLowerSeconds =
                snapshot.EstimatedRemainingLowerSeconds;
            result.EstimatedRemainingUpperSeconds =
                snapshot.EstimatedRemainingUpperSeconds;
            result.EtaEstimatorVersion = snapshot.EtaEstimatorVersion;
            result.EtaStageSeconds = new Dictionary<string, double>(
                snapshot.EtaStageSeconds,
                StringComparer.OrdinalIgnoreCase);
            result.TransformerTeacherPeakWorkingSetBytes =
                snapshot.TransformerTeacherPeakWorkingSetBytes;
            result.PolicyDecisions = snapshot.PolicyDecisions;
            result.SearchSimulations = snapshot.SearchSimulations;
            result.SearchNodes = snapshot.SearchNodes;
            result.SearchMillisecondsTotal = snapshot.SearchMillisecondsTotal;
            result.ObservationProjectionAllocatedBytes =
                snapshot.ObservationProjectionAllocatedBytes;
            result.DecisionEngineAllocatedBytes =
                snapshot.DecisionEngineAllocatedBytes;
            result.SearchModelEvaluations = snapshot.SearchModelEvaluations;
            result.SearchModelCacheHits = snapshot.SearchModelCacheHits;
            result.SearchOriginalCandidates = snapshot.SearchOriginalCandidates;
            result.SearchRetainedCandidates = snapshot.SearchRetainedCandidates;
            result.SearchTimeBudgetStops = snapshot.SearchTimeBudgetStops;
            result.SearchModelBudgetStops = snapshot.SearchModelBudgetStops;
            result.SearchEarlyStops = snapshot.SearchEarlyStops;
            result.SearchBudgetTierCounts =
                new Dictionary<string, int>(
                    snapshot.SearchBudgetTierCounts,
                    StringComparer.OrdinalIgnoreCase);
            result.RuleTerminalOverrides = snapshot.RuleTerminalOverrides;
            result.CertifiedLoops = snapshot.CertifiedLoops;
            result.SustainableControlLoops =
                snapshot.SustainableControlLoops;
            result.FakeLoops = snapshot.FakeLoops;
            result.BlockedLoops = snapshot.BlockedLoops;
            result.ExplorationDecisions =
                snapshot.ExplorationDecisions;
            result.ExplorationActionOverrides =
                snapshot.ExplorationActionOverrides;
            result.RootMaximumVisitShareMean =
                snapshot.RootMaximumVisitShareMean;
            result.RootMaximumVisitShareSamples =
                snapshot.RootMaximumVisitShareSamples;
            result.AuthoritativeActionsAudited =
                snapshot.AuthoritativeActionsAudited;
            result.AuthoritativeSemanticMismatches =
                snapshot.AuthoritativeSemanticMismatches;
            result.AuthoritativeSelectedActionsAudited =
                snapshot.AuthoritativeSelectedActionsAudited;
            result.AuthoritativeSelectedSemanticMismatches =
                snapshot.AuthoritativeSelectedSemanticMismatches;
            result.AuthoritativeTeacherOverrides =
                snapshot.AuthoritativeTeacherOverrides;
            result.AuthoritativeSemanticMismatchKinds =
                new Dictionary<string, int>(
                    snapshot.AuthoritativeSemanticMismatchKinds,
                    StringComparer.OrdinalIgnoreCase);
            result.AuthoritativeSemanticMismatchSources =
                new Dictionary<string, int>(
                    snapshot.AuthoritativeSemanticMismatchSources,
                    StringComparer.OrdinalIgnoreCase);
            result.AuthoritativeSemanticMismatchScenarios =
                new Dictionary<string, int>(
                    snapshot.AuthoritativeSemanticMismatchScenarios,
                    StringComparer.OrdinalIgnoreCase);
            result.SemanticAudit = snapshot.SemanticAudit;
            result.ModelCompletedEpochs = snapshot.ModelEpoch;
            result.ModelConfiguredEpochs = snapshot.ModelTotalEpochs;
            result.ModelBestEpoch = snapshot.ModelBestEpoch;
            result.ModelEarlyStopped = snapshot.ModelEarlyStopped;
            result.ModelBestValidationLoss =
                snapshot.ModelBestValidationLoss;
            result.ModelTrainingLoss = snapshot.ModelTrainingLoss;
            result.ModelValidationLoss = snapshot.ModelValidationLoss;
            result.ModelEpochHistory = (
                    request.IncludeMetricHistoryInTelemetry
                        ? snapshot.ModelEpochHistory
                        : (result.Iterations
                           ?? new List<CombatCampaignFoundationIteration>())
                        .SelectMany(item =>
                            item.ModelEpochHistory
                            ?? new List<CombatPolicyValueEpochMetrics>()))
                .Select(item => CloneEpochMetrics(item))
                .ToList();
            result.ElapsedSeconds = snapshot.ElapsedSeconds;
            result.Gen0Collections = snapshot.Gen0Collections;
            result.Gen1Collections = snapshot.Gen1Collections;
            result.Gen2Collections = snapshot.Gen2Collections;
            result.AllocatedBytes = snapshot.AllocatedBytes;
            result.EpisodeCompactStateVectors =
                snapshot.EpisodeCompactStateVectors;
            result.EpisodeCompactCandidateVectors =
                snapshot.EpisodeCompactCandidateVectors;
            result.EpisodeStateDictionaryMaterializations =
                snapshot.EpisodeStateDictionaryMaterializations;
            result.EpisodeCandidateDictionaryMaterializations =
                snapshot.EpisodeCandidateDictionaryMaterializations;
            result.WorldModelObservationsBuilt =
                snapshot.WorldModelObservationsBuilt;
            result.WorldModelObservationsSkipped =
                snapshot.WorldModelObservationsSkipped;
            result.WorkingSetBytes = snapshot.WorkingSetBytes;
            result.PrivateMemoryBytes = snapshot.PrivateMemoryBytes;
            result.GcHeapSizeBytes = snapshot.GcHeapSizeBytes;
            result.GcFragmentedBytes = snapshot.GcFragmentedBytes;
            result.MemoryLoadBytes = snapshot.MemoryLoadBytes;
            result.TotalAvailableMemoryBytes =
                snapshot.TotalAvailableMemoryBytes;
            result.CpuSeconds = snapshot.CpuSeconds;
            result.CpuUtilizationPercent = snapshot.CpuUtilizationPercent;
            result.AllocationMegabytesPerSecond =
                snapshot.AllocationMegabytesPerSecond;
            result.PhaseElapsedSeconds =
                new Dictionary<string, double>(
                    snapshot.PhaseElapsedSeconds,
                    StringComparer.OrdinalIgnoreCase);
            result.PhaseCpuSeconds =
                new Dictionary<string, double>(
                    snapshot.PhaseCpuSeconds,
                    StringComparer.OrdinalIgnoreCase);
            result.PhaseAllocatedBytes =
                new Dictionary<string, long>(
                    snapshot.PhaseAllocatedBytes,
                    StringComparer.OrdinalIgnoreCase);
            result.PhaseExternalCpuSeconds =
                new Dictionary<string, double>(
                    snapshot.PhaseExternalCpuSeconds,
                    StringComparer.OrdinalIgnoreCase);
            result.PhasePeakConcurrentWork =
                new Dictionary<string, int>(
                    snapshot.PhasePeakConcurrentWork,
                    StringComparer.OrdinalIgnoreCase);
            result.PhaseObservedWorkerThreads =
                new Dictionary<string, int>(
                    snapshot.PhaseObservedWorkerThreads,
                    StringComparer.OrdinalIgnoreCase);
        }

        private void Report(string stage, bool force = false)
        {
            var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            if (!force)
            {
                var previous = Volatile.Read(ref lastReportMilliseconds);
                if (elapsedMilliseconds - previous < 1000L
                    || Interlocked.CompareExchange(
                        ref lastReportMilliseconds,
                        elapsedMilliseconds,
                        previous) != previous)
                {
                    return;
                }
            }
            else
            {
                Interlocked.Exchange(
                    ref lastReportMilliseconds,
                    elapsedMilliseconds);
            }

            try
            {
                request.Telemetry?.Invoke(Snapshot(stage));
            }
            catch
            {
                // Diagnostics must never abort formal training.
            }
        }

        private CombatCampaignFoundationTelemetry Snapshot(string stage)
        {
            int observedThreads;
            int activeMaximumDepth;
            int maximumDepth;
            int[] depthBuckets;
            long completedDepthTotal;
            int completedDepthCount;
            int activeDepthCount;
            int snapshotModelIteration;
            int snapshotModelEpoch;
            int snapshotModelTotalEpochs;
            int snapshotModelCompletedFrames;
            int snapshotModelTotalFrames;
            double snapshotModelEpochsPerSecond;
            double snapshotModelTrainingLoss;
            double snapshotModelValidationLoss;
            double snapshotModelBestValidationLoss;
            int snapshotModelBestEpoch;
            int snapshotModelStaleEpochs;
            bool snapshotModelEarlyStopped;
            string snapshotCurrentPhase;
            double snapshotPhaseRemainingSeconds;
            CombatTransformerTeacherProgress snapshotTransformerTeacher;
            double snapshotRootMaximumVisitShareTotal;
            int snapshotRootMaximumVisitShareSamples;
            Dictionary<string, int> snapshotMismatchKinds;
            Dictionary<string, int> snapshotMismatchSources;
            Dictionary<string, int> snapshotMismatchScenarios;
            CombatSemanticAuditMetrics snapshotSemanticAudit;
            Dictionary<string, double> snapshotPhaseElapsedSeconds;
            Dictionary<string, double> snapshotPhaseCpuSeconds;
            Dictionary<string, long> snapshotPhaseAllocatedBytes;
            Dictionary<string, double> snapshotPhaseExternalCpuSeconds;
            Dictionary<string, int> snapshotPhasePeakConcurrentWork;
            Dictionary<string, int> snapshotPhaseObservedWorkerThreads;
            List<CombatPolicyValueEpochMetrics> snapshotModelEpochHistory;
            double snapshotCurrentPhaseElapsedSeconds;
            double snapshotCurrentPhaseCpuSeconds;
            long snapshotCurrentPhaseAllocatedBytes;
            int snapshotCurrentPhaseStartedExecutedCampaigns;
            int snapshotCurrentPhaseStartedBattles;
            int snapshotCurrentPhaseRequestedCampaigns;
            long snapshotCurrentPhaseStartedSearchSimulations;
            var currentCpuSeconds = Math.Max(
                0d,
                (process.TotalProcessorTime - initialCpuTime).TotalSeconds);
            var currentAllocatedBytes = Math.Max(
                0L,
                ReadManagedAllocationCounter() - initialAllocatedBytes);
            lock (workerGate)
            {
                observedThreads = observedWorkerThreads.Count;
                activeDepthCount = activeCampaignDepths.Count;
                activeMaximumDepth = activeCampaignDepths.Count == 0
                    ? 0
                    : activeCampaignDepths.Values.Max();
                maximumDepth = maximumCompletedBattleDepth;
                depthBuckets = (int[])completedDepthBuckets.Clone();
                completedDepthTotal = completedCampaignDepthTotal;
                completedDepthCount = completedCampaignDepthCount;
                snapshotModelIteration = modelIteration;
                snapshotModelEpoch = modelEpoch;
                snapshotModelTotalEpochs = modelTotalEpochs;
                snapshotModelCompletedFrames = modelCompletedFrames;
                snapshotModelTotalFrames = modelTotalFrames;
                snapshotModelEpochsPerSecond = modelEpochsPerSecond;
                snapshotModelTrainingLoss = modelTrainingLoss;
                snapshotModelValidationLoss = modelValidationLoss;
                snapshotModelBestValidationLoss = modelBestValidationLoss;
                snapshotModelBestEpoch = modelBestEpoch;
                snapshotModelStaleEpochs = modelStaleEpochs;
                snapshotModelEarlyStopped = modelEarlyStopped;
                snapshotCurrentPhase = currentPhase;
                snapshotPhaseRemainingSeconds =
                    phaseEstimatedRemainingSeconds;
                snapshotTransformerTeacher = new CombatTransformerTeacherProgress
                {
                    Iteration = modelIteration,
                    TotalIterations = totalIterations,
                    Stage = transformerTeacherStage,
                    Epoch = transformerTeacherEpoch,
                    TotalEpochs = transformerTeacherTotalEpochs,
                    CompletedFrames = transformerTeacherCompletedFrames,
                    TotalFrames = transformerTeacherTotalFrames,
                    FramesPerSecond = transformerTeacherFramesPerSecond,
                    ElapsedSeconds = transformerTeacherElapsedSeconds,
                    EstimatedRemainingSeconds = phaseEstimatedRemainingSeconds,
                    ProcessCpuPercent = transformerTeacherCpuPercent,
                    ProcessCpuSeconds = transformerTeacherProcessCpuSeconds,
                    WorkingSetBytes = transformerTeacherWorkingSetBytes,
                    PeakWorkingSetBytes =
                        transformerTeacherPeakWorkingSetBytes,
                    StageElapsedSeconds =
                        transformerTeacherStageElapsedSeconds,
                    WarmStarted = transformerTeacherWarmStarted,
                    TrainingEnabled = transformerTeacherTrainingEnabled,
                    Message = transformerTeacherMessage
                };
                snapshotRootMaximumVisitShareTotal =
                    rootMaximumVisitShareTotal;
                snapshotRootMaximumVisitShareSamples =
                    rootMaximumVisitShareSamples;
                snapshotMismatchKinds = new Dictionary<string, int>(
                    authoritativeSemanticMismatchKinds,
                    StringComparer.OrdinalIgnoreCase);
                snapshotMismatchSources = new Dictionary<string, int>(
                    authoritativeSemanticMismatchSources,
                    StringComparer.OrdinalIgnoreCase);
                snapshotMismatchScenarios = new Dictionary<string, int>(
                    authoritativeSemanticMismatchScenarios,
                    StringComparer.OrdinalIgnoreCase);
                snapshotSemanticAudit = new CombatSemanticAuditMetrics();
                snapshotSemanticAudit.MergeFrom(semanticAudit);
                snapshotPhaseElapsedSeconds =
                    new Dictionary<string, double>(
                        phaseElapsedSeconds,
                        StringComparer.OrdinalIgnoreCase);
                snapshotPhaseCpuSeconds =
                    new Dictionary<string, double>(
                        phaseCpuSeconds,
                        StringComparer.OrdinalIgnoreCase);
                snapshotPhaseAllocatedBytes =
                    new Dictionary<string, long>(
                        phaseAllocatedBytes,
                        StringComparer.OrdinalIgnoreCase);
                snapshotPhaseExternalCpuSeconds =
                    new Dictionary<string, double>(
                        phaseExternalCpuSeconds,
                        StringComparer.OrdinalIgnoreCase);
                var teacherCpuSeconds =
                    transformerTeacherAccumulatedCpuSeconds
                    + transformerTeacherProcessCpuSeconds;
                if (teacherCpuSeconds > 0d)
                {
                    snapshotPhaseExternalCpuSeconds["transformer-teacher"] =
                        snapshotPhaseExternalCpuSeconds.TryGetValue(
                            "transformer-teacher",
                            out var accumulatedTeacherCpu)
                            ? accumulatedTeacherCpu + teacherCpuSeconds
                            : teacherCpuSeconds;
                }
                snapshotPhasePeakConcurrentWork =
                    new Dictionary<string, int>(
                        phasePeakConcurrentWork,
                        StringComparer.OrdinalIgnoreCase);
                snapshotPhaseObservedWorkerThreads =
                    phaseObservedWorkerThreadOffsets.Keys
                        .Concat(phaseWorkerThreads.Keys)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            key => key,
                            key => Math.Max(
                                phaseObservedWorkerThreadOffsets.TryGetValue(
                                    key,
                                    out var priorCount)
                                    ? priorCount
                                    : 0,
                                phaseWorkerThreads.TryGetValue(
                                    key,
                                    out var currentThreads)
                                    ? currentThreads.Count
                                    : 0),
                            StringComparer.OrdinalIgnoreCase);
                snapshotModelEpochHistory = request
                    .IncludeMetricHistoryInTelemetry
                    ? modelEpochHistory
                        .OrderBy(item => item.Iteration)
                        .ThenBy(item => item.Epoch)
                        .ThenBy(item => item.Calibrated)
                        .Select(item => CloneEpochMetrics(item))
                        .ToList()
                    : new List<CombatPolicyValueEpochMetrics>();
                var phaseElapsed = Math.Max(
                    0d,
                    stopwatch.Elapsed.TotalSeconds
                    - currentPhaseStartedSeconds);
                snapshotPhaseElapsedSeconds[currentPhase] =
                    snapshotPhaseElapsedSeconds.TryGetValue(
                        currentPhase,
                        out var accumulated)
                        ? accumulated + phaseElapsed
                        : phaseElapsed;
                var phaseCpu = Math.Max(
                    0d,
                    currentCpuSeconds - currentPhaseStartedCpuSeconds);
                snapshotPhaseCpuSeconds[currentPhase] =
                    snapshotPhaseCpuSeconds.TryGetValue(
                        currentPhase,
                        out var accumulatedCpu)
                        ? accumulatedCpu + phaseCpu
                        : phaseCpu;
                var phaseAllocated = Math.Max(
                    0L,
                    currentAllocatedBytes
                    - currentPhaseStartedAllocatedBytes);
                snapshotPhaseAllocatedBytes[currentPhase] =
                    snapshotPhaseAllocatedBytes.TryGetValue(
                        currentPhase,
                        out var accumulatedAllocated)
                        ? accumulatedAllocated + phaseAllocated
                        : phaseAllocated;
                snapshotCurrentPhaseElapsedSeconds = phaseElapsed;
                snapshotCurrentPhaseCpuSeconds = phaseCpu;
                snapshotCurrentPhaseAllocatedBytes = phaseAllocated;
                snapshotCurrentPhaseStartedExecutedCampaigns =
                    currentPhaseStartedExecutedCampaigns;
                snapshotCurrentPhaseStartedBattles =
                    currentPhaseStartedBattles;
                snapshotCurrentPhaseRequestedCampaigns =
                    currentPhaseRequestedCampaigns;
                snapshotCurrentPhaseStartedSearchSimulations =
                    currentPhaseStartedSearchSimulations;
            }
            var elapsedSeconds = Math.Max(
                0.001d,
                elapsedSecondsOffset + stopwatch.Elapsed.TotalSeconds);
            var cpuSeconds = cpuSecondsOffset
                             + Math.Max(
                                 0d,
                                 (process.TotalProcessorTime - initialCpuTime)
                                 .TotalSeconds);
            var allocatedBytes = allocatedBytesOffset
                                 + Math.Max(
                                     0L,
                                     ReadManagedAllocationCounter()
                                     - initialAllocatedBytes);
            var campaigns = Volatile.Read(ref completedCampaigns);
            var campaignExecutions = Volatile.Read(ref executedCampaigns);
            var battles = Volatile.Read(ref completedBattles);
            var battleRate = battles / elapsedSeconds;
            var averageCompletedDepth = completedDepthCount <= 0
                ? 0d
                : completedDepthTotal / (double)completedDepthCount;
            var projectedDepth = Math.Max(
                1d,
                Math.Min(
                    37d,
                    Math.Max(averageCompletedDepth, activeMaximumDepth)));
            var remainingCampaigns = Math.Max(0, requestedCampaigns - campaigns);
            var remainingBattleWork = Math.Max(
                0d,
                remainingCampaigns * projectedDepth
                - activeDepthCount);
            var simulationCount = Volatile.Read(ref searchSimulations);
            var currentPhaseElapsedSeconds = Math.Max(
                0.001d,
                snapshotCurrentPhaseElapsedSeconds);
            var currentPhaseCampaigns = Math.Max(
                0,
                campaignExecutions
                - snapshotCurrentPhaseStartedExecutedCampaigns);
            var currentPhaseBattles = Math.Max(
                0,
                battles - snapshotCurrentPhaseStartedBattles);
            var currentPhaseCampaignRate = currentPhaseCampaigns
                                           / currentPhaseElapsedSeconds;
            var measuredPhaseRemainingSeconds =
                snapshotCurrentPhaseRequestedCampaigns <= 0
                || currentPhaseCampaignRate <= 0d
                    ? 0d
                    : Math.Max(
                          0,
                          snapshotCurrentPhaseRequestedCampaigns
                          - currentPhaseCampaigns)
                      / currentPhaseCampaignRate;
            var currentPhaseSearchSimulations = Math.Max(
                0L,
                simulationCount
                - snapshotCurrentPhaseStartedSearchSimulations);
            var phase = string.IsNullOrWhiteSpace(snapshotCurrentPhase)
                ? ResolvePhase(stage)
                : snapshotCurrentPhase;
            var phaseRemainingSeconds = Math.Max(
                snapshotPhaseRemainingSeconds,
                measuredPhaseRemainingSeconds);
            var eta = StageAwareEta(
                elapsedSeconds,
                battles,
                remainingBattleWork,
                snapshotPhaseElapsedSeconds,
                snapshotModelIteration,
                totalIterations,
                phase,
                phaseRemainingSeconds);
            var execution = CombatFoundationExecutionProfiles.Resolve(
                CombatFoundationExecutionProfileNames.Custom,
                effectiveParallelism,
                request.InferenceExecutionMode,
                request.InferenceParallelism,
                request.ThreadPoolMinimumWorkerThreads,
                request.CheckpointSerializationParallelism,
                null,
                request.InferenceLaneCount,
                request.InferenceBatchSize);
            var inferenceDiagnostics = CombatPolicyValueBatchDiagnostics
                .Capture()
                .DeltaFrom(initialInferenceDiagnostics);
            var episodeStorage = CombatEpisodeStorageDiagnostics.Capture();
            process.Refresh();
            var workingSetBytes = Math.Max(0L, process.WorkingSet64);
            var privateMemoryBytes = Math.Max(0L, process.PrivateMemorySize64);
            var gcHeapSizeBytes = 0L;
            var gcFragmentedBytes = 0L;
            var memoryLoadBytes = 0L;
            var totalAvailableMemoryBytes = 0L;
#if NET8_0_OR_GREATER
            var memoryInfo = GC.GetGCMemoryInfo();
            gcHeapSizeBytes = Math.Max(0L, memoryInfo.HeapSizeBytes);
            gcFragmentedBytes = Math.Max(0L, memoryInfo.FragmentedBytes);
            memoryLoadBytes = Math.Max(0L, memoryInfo.MemoryLoadBytes);
            totalAvailableMemoryBytes = Math.Max(
                0L,
                memoryInfo.TotalAvailableMemoryBytes);
#endif
            return new CombatCampaignFoundationTelemetry
            {
                Stage = stage ?? "",
                Phase = phase,
                Iteration = snapshotModelIteration,
                TotalIterations = totalIterations,
                RunStartIteration = runStartIteration,
                RunIteration = snapshotModelIteration < runStartIteration
                    ? 0
                    : Math.Min(
                        runTotalIterations,
                        snapshotModelIteration - runStartIteration + 1),
                RunTotalIterations = runTotalIterations,
                EffectiveParallelism = effectiveParallelism,
                ParallelismDecision = parallelismDecision,
                ModelTrainingParallelism = Math.Max(
                    1,
                    Math.Min(64, request.ModelTrainingParallelism)),
                GovernanceProfile = request.GovernanceProfile,
                ParallelismProfile = request.ParallelismProfile,
                InferenceExecutionMode = execution.InferenceMode,
                InferenceParallelism = execution.InferenceParallelism,
                AutoTune = request.AutoTuneCache == null
                    ? new CombatFoundationAutoTuneResult
                    {
                        HardwareKey = request.AutoTuneHardwareKey ?? "",
                        SelectedParallelism = effectiveParallelism
                    }
                    : CloneAutoTuneResult(request.AutoTuneCache),
                InferenceLaneCount = execution.InferenceLaneCount,
                InferenceBatchSizePerLane = execution.InferenceBatchSize,
                InferenceRequests = inferenceDiagnostics.Requests,
                InferenceBatchEvaluations =
                    inferenceDiagnostics.BatchEvaluations,
                InferenceBatchedInputs = inferenceDiagnostics.BatchedInputs,
                InferenceAverageBatchSize =
                    inferenceDiagnostics.AverageBatchSize,
                InferenceFullBatchEvaluations =
                    inferenceDiagnostics.FullBatchEvaluations,
                InferenceTimeoutFlushes = inferenceDiagnostics.TimeoutFlushes,
                InferenceDirectFallbackRequests =
                    inferenceDiagnostics.DirectFallbackRequests,
                InferenceAdaptiveFallbackActivations =
                    inferenceDiagnostics.AdaptiveFallbackActivations,
                InferenceAverageWaitMicroseconds =
                    inferenceDiagnostics.AverageWaitMicroseconds,
                InferenceDirectEvaluations =
                    inferenceDiagnostics.DirectEvaluations,
                InferenceAverageDirectEvaluationMicroseconds =
                    inferenceDiagnostics.AverageDirectEvaluationMicroseconds,
                InferenceAverageDirectAllocatedBytes =
                    inferenceDiagnostics.AverageDirectAllocatedBytes,
                InferenceAverageSparseFeatureCount =
                    inferenceDiagnostics.AverageSparseFeatureCount,
                InferenceSparseFeatureDensity =
                    inferenceDiagnostics.SparseFeatureDensity,
                InferenceWeightMultiplicationReduction =
                    inferenceDiagnostics.WeightMultiplicationReduction,
                ActiveCampaigns = Math.Max(0, Volatile.Read(ref activeCampaigns)),
                PeakConcurrentCampaigns = Volatile.Read(ref peakConcurrentCampaigns),
                SchedulerQueuedWork = schedulerQueuedWork,
                SchedulerRunningWork = schedulerRunningWork,
                SchedulerCompletedWork = schedulerCompletedWork,
                SchedulerCommittedWork = schedulerCommittedWork,
                SchedulerPeakRunningWork = schedulerPeakRunningWork,
                SchedulerRefillCount = schedulerRefillCount,
                SchedulerSpeculativeDiscardedWork =
                    schedulerSpeculativeDiscardedWork,
                SchedulerTailIdleCoreSeconds = schedulerTailIdleCoreSeconds,
                ObservedWorkerThreads = observedThreads,
                CompletedCampaigns = campaigns,
                RequestedCampaigns = requestedCampaigns,
                RunInitialCompletedCampaigns =
                    runInitialCompletedCampaigns,
                RunCompletedCampaigns = Math.Max(
                    0,
                    campaigns - runInitialCompletedCampaigns),
                RunRequestedCampaigns = Math.Max(
                    0,
                    requestedCampaigns - runInitialCompletedCampaigns),
                ExecutedCampaigns = campaignExecutions,
                RunInitialExecutedCampaigns =
                    runInitialExecutedCampaigns,
                RunExecutedCampaigns = Math.Max(
                    0,
                    campaignExecutions - runInitialExecutedCampaigns),
                CurrentPhaseCompletedCampaigns =
                    currentPhaseCampaigns,
                CurrentPhaseRequestedCampaigns = Math.Max(
                    0,
                    snapshotCurrentPhaseRequestedCampaigns),
                CompletedBattles = battles,
                RunCompletedBattles = Math.Max(
                    0,
                    battles - runInitialCompletedBattles),
                CurrentPhaseCompletedBattles = currentPhaseBattles,
                MaximumCompletedBattleDepth = maximumDepth,
                MaximumActiveBattleDepth = activeMaximumDepth,
                Depth1To5Campaigns = depthBuckets[0],
                Depth6To10Campaigns = depthBuckets[1],
                Depth11To20Campaigns = depthBuckets[2],
                Depth21To30Campaigns = depthBuckets[3],
                Depth31To37Campaigns = depthBuckets[4],
                ProjectedBattleDepth = projectedDepth,
                EstimatedRemainingSeconds = eta.ExpectedSeconds,
                EstimatedRemainingLowerSeconds = eta.LowerSeconds,
                EstimatedRemainingUpperSeconds = eta.UpperSeconds,
                EtaEstimatorVersion = CombatFoundationEtaEstimate.CurrentVersion,
                EtaStageSeconds = eta.StageSeconds,
                ModelEpoch = snapshotModelEpoch,
                ModelTotalEpochs = snapshotModelTotalEpochs,
                ModelCompletedFrames = snapshotModelCompletedFrames,
                ModelTotalFrames = snapshotModelTotalFrames,
                ModelEpochsPerSecond = snapshotModelEpochsPerSecond,
                ModelTrainingLoss = snapshotModelTrainingLoss,
                ModelValidationLoss = snapshotModelValidationLoss,
                ModelBestValidationLoss = snapshotModelBestValidationLoss,
                ModelBestEpoch = snapshotModelBestEpoch,
                ModelStaleEpochs = snapshotModelStaleEpochs,
                ModelEarlyStopped = snapshotModelEarlyStopped,
                ModelEpochHistory = snapshotModelEpochHistory,
                PhaseEstimatedRemainingSeconds = Math.Max(
                    snapshotPhaseRemainingSeconds,
                    measuredPhaseRemainingSeconds),
                TransformerTeacherStage = snapshotTransformerTeacher.Stage,
                TransformerTeacherEpoch = snapshotTransformerTeacher.Epoch,
                TransformerTeacherTotalEpochs =
                    snapshotTransformerTeacher.TotalEpochs,
                TransformerTeacherCompletedFrames =
                    snapshotTransformerTeacher.CompletedFrames,
                TransformerTeacherTotalFrames =
                    snapshotTransformerTeacher.TotalFrames,
                TransformerTeacherFramesPerSecond =
                    snapshotTransformerTeacher.FramesPerSecond,
                TransformerTeacherElapsedSeconds =
                    snapshotTransformerTeacher.ElapsedSeconds,
                TransformerTeacherCpuPercent =
                    snapshotTransformerTeacher.ProcessCpuPercent,
                TransformerTeacherProcessCpuSeconds =
                    snapshotPhaseExternalCpuSeconds.TryGetValue(
                        "transformer-teacher",
                        out var totalTeacherCpuSeconds)
                        ? totalTeacherCpuSeconds
                        : 0d,
                TransformerTeacherWorkingSetBytes =
                    snapshotTransformerTeacher.WorkingSetBytes,
                TransformerTeacherPeakWorkingSetBytes =
                    snapshotTransformerTeacher.PeakWorkingSetBytes,
                TransformerTeacherStageElapsedSeconds =
                    snapshotTransformerTeacher.StageElapsedSeconds,
                TransformerTeacherWarmStarted =
                    snapshotTransformerTeacher.WarmStarted,
                TransformerTeacherTrainingEnabled =
                    snapshotTransformerTeacher.TrainingEnabled,
                TransformerTeacherMessage =
                    snapshotTransformerTeacher.Message,
                PolicyDecisions = Volatile.Read(ref policyDecisions),
                SearchSimulations = simulationCount,
                RunSearchSimulations = Math.Max(
                    0L,
                    simulationCount - runInitialSearchSimulations),
                SearchNodes = Volatile.Read(ref searchNodes),
                SearchMillisecondsTotal =
                    Volatile.Read(ref searchMicroseconds) / 1000d,
                ObservationProjectionAllocatedBytes = Volatile.Read(
                    ref observationProjectionAllocatedBytes),
                DecisionEngineAllocatedBytes = Volatile.Read(
                    ref decisionEngineAllocatedBytes),
                SearchModelEvaluations =
                    Volatile.Read(ref searchModelEvaluations),
                SearchModelCacheHits =
                    Volatile.Read(ref searchModelCacheHits),
                SearchOriginalCandidates =
                    Volatile.Read(ref searchOriginalCandidates),
                SearchRetainedCandidates =
                    Volatile.Read(ref searchRetainedCandidates),
                SearchTimeBudgetStops =
                    Volatile.Read(ref searchTimeBudgetStops),
                SearchModelBudgetStops =
                    Volatile.Read(ref searchModelBudgetStops),
                SearchEarlyStops = Volatile.Read(ref searchEarlyStops),
                SearchBudgetTierCounts =
                    new Dictionary<string, int>(
                        searchBudgetTierCounts,
                        StringComparer.OrdinalIgnoreCase),
                RuleTerminalOverrides =
                    Volatile.Read(ref ruleTerminalOverrides),
                CertifiedLoops = Volatile.Read(ref certifiedLoops),
                SustainableControlLoops =
                    Volatile.Read(ref sustainableControlLoops),
                FakeLoops = Volatile.Read(ref fakeLoops),
                BlockedLoops = Volatile.Read(ref blockedLoops),
                ExplorationDecisions =
                    Volatile.Read(ref explorationDecisions),
                ExplorationActionOverrides =
                    Volatile.Read(ref explorationActionOverrides),
                RootMaximumVisitShareMean =
                    snapshotRootMaximumVisitShareSamples <= 0
                        ? 0d
                        : snapshotRootMaximumVisitShareTotal
                          / snapshotRootMaximumVisitShareSamples,
                RootMaximumVisitShareSamples =
                    snapshotRootMaximumVisitShareSamples,
                AuthoritativeActionsAudited =
                    Volatile.Read(ref authoritativeActionsAudited),
                AuthoritativeSemanticMismatches =
                    Volatile.Read(ref authoritativeSemanticMismatches),
                AuthoritativeSelectedActionsAudited =
                    Volatile.Read(ref authoritativeSelectedActionsAudited),
                AuthoritativeSelectedSemanticMismatches =
                    Volatile.Read(
                        ref authoritativeSelectedSemanticMismatches),
                AuthoritativeTeacherOverrides =
                    Volatile.Read(ref authoritativeTeacherOverrides),
                AuthoritativeSemanticMismatchKinds = snapshotMismatchKinds,
                AuthoritativeSemanticMismatchSources =
                    snapshotMismatchSources,
                AuthoritativeSemanticMismatchScenarios =
                    snapshotMismatchScenarios,
                SemanticAudit = snapshotSemanticAudit,
                SearchSimulationsPerSecond = simulationCount / elapsedSeconds,
                CurrentPhaseSearchSimulationsPerSecond =
                    currentPhaseSearchSimulations
                    / currentPhaseElapsedSeconds,
                ElapsedSeconds = elapsedSeconds,
                CampaignsPerSecond = campaigns / elapsedSeconds,
                CurrentPhaseCampaignsPerSecond = currentPhaseCampaignRate,
                BattlesPerSecond = battleRate,
                Gen0Collections = Math.Max(0, GC.CollectionCount(0) - initialGen0),
                Gen1Collections = Math.Max(0, GC.CollectionCount(1) - initialGen1),
                Gen2Collections = Math.Max(0, GC.CollectionCount(2) - initialGen2),
                AllocatedBytes = allocatedBytes,
                EpisodeCompactStateVectors = Math.Max(
                    0L,
                    episodeStorage.CompactStateVectors
                    - initialEpisodeStorage.CompactStateVectors),
                EpisodeCompactCandidateVectors = Math.Max(
                    0L,
                    episodeStorage.CompactCandidateVectors
                    - initialEpisodeStorage.CompactCandidateVectors),
                EpisodeStateDictionaryMaterializations = Math.Max(
                    0L,
                    episodeStorage.StateDictionaryMaterializations
                    - initialEpisodeStorage.StateDictionaryMaterializations),
                EpisodeCandidateDictionaryMaterializations = Math.Max(
                    0L,
                    episodeStorage.CandidateDictionaryMaterializations
                    - initialEpisodeStorage.CandidateDictionaryMaterializations),
                WorldModelObservationsBuilt = Math.Max(
                    0L,
                    episodeStorage.WorldModelObservationsBuilt
                    - initialEpisodeStorage.WorldModelObservationsBuilt),
                WorldModelObservationsSkipped = Math.Max(
                    0L,
                    episodeStorage.WorldModelObservationsSkipped
                    - initialEpisodeStorage.WorldModelObservationsSkipped),
                WorkingSetBytes = workingSetBytes,
                PrivateMemoryBytes = privateMemoryBytes,
                GcHeapSizeBytes = gcHeapSizeBytes,
                GcFragmentedBytes = gcFragmentedBytes,
                MemoryLoadBytes = memoryLoadBytes,
                TotalAvailableMemoryBytes = totalAvailableMemoryBytes,
                CpuSeconds = cpuSeconds,
                CpuUtilizationPercent = Math.Max(
                    0d,
                    string.Equals(
                        phase,
                        "transformer-teacher",
                        StringComparison.Ordinal)
                    && snapshotTransformerTeacher.ElapsedSeconds > 0d
                        ? snapshotTransformerTeacher.ProcessCpuPercent
                        : cpuSeconds
                          / elapsedSeconds
                          / Math.Max(1, Environment.ProcessorCount)
                          * 100d),
                CurrentPhaseCpuUtilizationPercent = Math.Max(
                    0d,
                    snapshotCurrentPhaseCpuSeconds
                    / currentPhaseElapsedSeconds
                    / Math.Max(1, Environment.ProcessorCount)
                    * 100d),
                AllocationMegabytesPerSecond = Math.Max(
                    0d,
                    allocatedBytes / elapsedSeconds / (1024d * 1024d)),
                CurrentPhaseAllocationMegabytesPerSecond = Math.Max(
                    0d,
                    snapshotCurrentPhaseAllocatedBytes
                    / currentPhaseElapsedSeconds
                    / (1024d * 1024d)),
                PhaseElapsedSeconds = snapshotPhaseElapsedSeconds,
                PhaseCpuSeconds = snapshotPhaseCpuSeconds,
                PhaseAllocatedBytes = snapshotPhaseAllocatedBytes,
                PhaseExternalCpuSeconds = snapshotPhaseExternalCpuSeconds,
                PhasePeakConcurrentWork = snapshotPhasePeakConcurrentWork,
                PhaseObservedWorkerThreads =
                    snapshotPhaseObservedWorkerThreads
            };
        }

        private static long ReadManagedAllocationCounter()
        {
#if NET8_0_OR_GREATER
            return GC.GetTotalAllocatedBytes(false);
#else
            // The shipped net472 runtime lacks the cumulative allocation API.
            return GC.GetTotalMemory(false);
#endif
        }

        private static string ResolvePhase(string stage)
        {
            var value = stage ?? "";
            if (value.StartsWith(
                    "model-training",
                    StringComparison.Ordinal))
            {
                return "model-training";
            }
            if (value.StartsWith(
                    "transformer-teacher",
                    StringComparison.Ordinal))
            {
                return "transformer-teacher";
            }
            if (value.StartsWith("training", StringComparison.Ordinal)
                || value.Contains("七层训练推演"))
            {
                return "self-play";
            }
            if (value.StartsWith("arena", StringComparison.Ordinal)
                || value.Contains("竞技场"))
            {
                return "arena";
            }
            if (value.StartsWith("validation", StringComparison.Ordinal)
                || value.Contains("验证"))
            {
                return "validation";
            }
            return value;
        }

        private static void MergeCounts(
            IDictionary<string, int> target,
            IReadOnlyDictionary<string, int>? source)
        {
            if (source == null)
            {
                return;
            }
            foreach (var pair in source)
            {
                target[pair.Key] = target.TryGetValue(
                    pair.Key,
                    out var current)
                    ? current + Math.Max(0, pair.Value)
                    : Math.Max(0, pair.Value);
            }
        }

        private static int DepthBucket(int depth)
        {
            if (depth <= 5) return 0;
            if (depth <= 10) return 1;
            if (depth <= 20) return 2;
            if (depth <= 30) return 3;
            return 4;
        }

        private static void UpdateMaximum(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref target,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }
                current = observed;
            }
        }
    }

    private sealed class FoundationTrainingCampaignRun
    {
        public CombatCampaignResult Campaign { get; set; } = new();

        public List<CombatEpisode> Episodes { get; set; } = new();

        public CombatCampaignResult? CounterfactualCampaign { get; set; }

        public List<CombatEpisode> CounterfactualEpisodes { get; set; } =
            new();

        public bool HardSeed { get; set; }

        public CombatFoundationTrainingSlot Schedule { get; set; } = new();

        public bool LocalEncounter { get; set; }

        public CombatCampaignCheckpoint? FailureEncounterCheckpoint {
            get;
            set;
        }

        public CombatSemanticAuditMetrics SemanticAudit { get; set; } = new();

        public int FeatureLeakageViolations { get; set; }

        public CombatFoundationCounterfactualAdmission
            CounterfactualAdmission { get; set; } =
                CombatFoundationCounterfactualAdmission.Rejected;

        public int CounterfactualAttemptsExecuted { get; set; }

        public int AdvancedLocalCurriculumAttempts { get; set; }

        public bool AdvancedLocalCurriculumRepaired { get; set; }

        public int AdvancedLocalCurriculumHpFloorPercent { get; set; }
    }

    internal sealed class LocalCurriculumCheckpoint
    {
        public CombatCampaignCheckpoint Checkpoint { get; set; } = new();

        public int HpFloorPercent { get; set; }

        public string CurriculumBand { get; set; } = "";

        public bool Repaired { get; set; }
    }

    internal sealed class DistillationWeightDecision
    {
        public double Weight { get; set; }

        public bool Guarded { get; set; }

        public string Reason { get; set; } = "";
    }

    private sealed class TuningSelection
    {
        public CombatPolicyValueNetworkDefinition Model { get; set; } = new();

        public int Epoch { get; set; }

        public double Score { get; set; }

        public double ValidationLoss { get; set; }

        public int CandidateCount { get; set; }

        public int OfflineRejectedCandidates { get; set; }

        public bool AllCandidatesRejectedOffline { get; set; }

        public bool EvaluationRan { get; set; }

        public int InvalidCampaigns { get; set; }

        public int FinalistCount { get; set; }

        public int CampaignsExecuted { get; set; }

        public int CampaignsSaved { get; set; }

        public CombatPolicyValueMetricSnapshot TrainingMetrics { get; set; } =
            new();

        public CombatPolicyValueMetricSnapshot ValidationMetrics { get; set; } =
            new();

        public CombatPolicyValueMetricSnapshot TestMetrics { get; set; } =
            new();
    }

    private sealed class AutoTuneCampaignMeasurement
    {
        public CombatFoundationAutoTuneMeasurement Measurement { get; set; } =
            new();

        public CombatCampaignResult?[] Runs { get; set; } =
            Array.Empty<CombatCampaignResult?>();
    }

    private sealed class InferenceExecutionCandidate
    {
        public string Mode { get; set; } =
            CombatFoundationExecutionProfileNames.DirectInference;

        public int LaneCount { get; set; }

        public int BatchSize { get; set; } = 1;
    }

    private sealed class InferenceCalibrationInputSample
    {
        public string Key { get; set; } = "";

        public int CandidateBucket { get; set; }

        public CombatEpisodeFrame Frame { get; set; } = null!;
    }

    private sealed class FoundationArenaPair
    {
        public CombatCampaignResult Champion { get; set; } = null!;

        public CombatCampaignResult Candidate { get; set; } = null!;
    }

    private sealed class FoundationArenaSide
    {
        public int DifficultyIndex { get; set; }

        public int ArenaIndex { get; set; }

        public bool ChampionSide { get; set; }

        public CombatCampaignResult Campaign { get; set; } = null!;
    }

    private static void ValidateSeedPartitions(
        ulong trainingSeedStart,
        ulong arenaSeedStart,
        ulong tuningSeedStart,
        ulong validationSeedStart,
        int iterations,
        int trainingCampaigns,
        int arenaPerDifficulty,
        int tuningCampaigns,
        int normalValidationCampaigns,
        int advancedValidationCampaigns)
    {
        var trainingEnd = trainingSeedStart
                          + (ulong)(iterations * trainingCampaigns);
        var arenaEnd = arenaSeedStart
                       + (ulong)(iterations * arenaPerDifficulty * 2);
        var tuningEnd = tuningSeedStart
                        + (ulong)(iterations * tuningCampaigns);
        var validationEnd = validationSeedStart
                            + (ulong)(normalValidationCampaigns + advancedValidationCampaigns);
        var ranges = new[]
        {
            (Start: trainingSeedStart, End: trainingEnd, Name: "training"),
            (Start: arenaSeedStart, End: arenaEnd, Name: "arena"),
            (Start: tuningSeedStart, End: tuningEnd, Name: "tuning"),
            (Start: validationSeedStart, End: validationEnd, Name: "validation")
        };
        for (var left = 0; left < ranges.Length; left++)
        {
            for (var right = left + 1; right < ranges.Length; right++)
            {
                if (ranges[left].Start < ranges[right].End
                    && ranges[right].Start < ranges[left].End)
                {
                    throw new ArgumentException(
                        "Foundation seed partitions overlap: "
                        + ranges[left].Name
                        + " / "
                        + ranges[right].Name);
                }
            }
        }
    }

    private sealed class RecordingCampaignPolicyFactory : ICombatSimulationPolicyFactory
    {
        private readonly CombatDecisionProfile profile;
        private readonly ICombatPolicyValueModel policyValue;
        private readonly string decisionProfile;
        private readonly double explorationProbability;
        private readonly double explorationTemperature;
        private readonly ulong campaignSeed;
        private readonly double authoritativeAuditProbability;
        private readonly CombatSimulationEngine authoritativeEngine;
        private readonly string contentSetHash;
        private readonly string ownerModSetHash;
        private readonly bool recordWorldModelObservations;
        private readonly ThreadLocal<CombatDecisionEngine> decisionEngines;
        private readonly List<CombatEpisodeRecordingPolicy> policies = new();

        public RecordingCampaignPolicyFactory(
            CombatDecisionProfile profile,
            ICombatPolicyValueModel policyValue,
            string decisionProfile,
            double explorationProbability,
            double explorationTemperature,
            ulong campaignSeed,
            double authoritativeAuditProbability,
            CombatSimulationEngine authoritativeEngine,
            string contentSetHash,
            string ownerModSetHash,
            bool recordWorldModelObservations)
        {
            this.profile = profile;
            this.policyValue = policyValue;
            this.decisionProfile = decisionProfile;
            this.explorationProbability = explorationProbability;
            this.explorationTemperature = explorationTemperature;
            this.campaignSeed = campaignSeed;
            this.authoritativeAuditProbability = Math.Max(
                0d,
                Math.Min(1d, authoritativeAuditProbability));
            this.authoritativeEngine = authoritativeEngine
                ?? throw new ArgumentNullException(
                    nameof(authoritativeEngine));
            this.contentSetHash = contentSetHash;
            this.ownerModSetHash = ownerModSetHash;
            this.recordWorldModelObservations = recordWorldModelObservations;
            var decisionPreparation =
                CombatAiRegistry.SnapshotDecisionPreparation();
            decisionEngines = new ThreadLocal<CombatDecisionEngine>(() =>
                new CombatDecisionEngine(
                    useRuntimeRegistries: false,
                    policyValueModel: this.policyValue,
                    decisionPreparation: decisionPreparation));
        }

        public string PolicyId => "aura-foundation-training:" + decisionProfile;

        public ICombatSimulationPolicy Create()
        {
            var decisionPolicy = new CombatDecisionSimulationPolicy(
                    decisionEngines.Value!,
                    profile,
                    new CombatSelfPlayExplorationOptions
                    {
                        Probability = explorationProbability,
                        Temperature = explorationTemperature,
                        RandomSeed = CombatFoundationSeedPlan.ToRandomSeed(
                            campaignSeed
                            ^ (ulong)(policies.Count + 1))
                    });
            ICombatSimulationPolicy teacher =
                new CombatAuthoritativeBranchTeacherPolicy(
                    decisionPolicy,
                    new CombatAuthoritativeTeacherOptions
                    {
                        AuditProbability = authoritativeAuditProbability,
                        RandomSeed = CombatFoundationSeedPlan.ToRandomSeed(
                            campaignSeed
                            ^ (ulong)(policies.Count + 1)
                            ^ 0x4558414354UL)
                    },
                    authoritativeEngine);
            var policy = new CombatEpisodeRecordingPolicy(
                teacher,
                decisionProfile,
                contentSetHash,
                ownerModSetHash,
                policyValue.ModelId,
                recordWorldModelObservations);
            policies.Add(policy);
            return policy;
        }

        public List<CombatEpisode> Complete(
            CombatCampaignResult result,
            int journeyBattleIndexOffset = 0,
            string journeyRunSuffix = "")
        {
            if (policies.Count != result.Battles.Count)
            {
                throw new InvalidOperationException(
                    "Campaign policy/battle count mismatch: "
                    + policies.Count
                    + "/"
                    + result.Battles.Count);
            }
            var journeyRunId = result.CampaignId
                               + ":"
                               + result.DifficultyId
                               + ":"
                               + result.WorldSeed
                               + (journeyRunSuffix ?? "");
            return policies.Select((policy, index) =>
            {
                var episode = policy.Complete(result.Battles[index]);
                episode.JourneyRunId = journeyRunId;
                episode.JourneyBattleIndex =
                    Math.Max(0, journeyBattleIndexOffset) + index;
                return episode;
            }).ToList();
        }
    }
}
