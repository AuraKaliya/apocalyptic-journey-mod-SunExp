using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

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
    public const string Version = "foundation-stagnation-v1";

    public const int DefaultMaximumConsecutiveRejectedIterations = 3;

    public const int HardSeedSolveRateWindow = 2;

    public const double MinimumHardSeedSolveRate = 0.05d;

    public const double ReducedHardSeedReplayShare = 0.12d;
}

public static class CombatFoundationPromotionProtocol
{
    public const string Version = "paired-incremental-v2";

    public const double MinimumPairedWinWilsonLowerBound = 0.20d;

    public const double MinimumScoreGain = 0.01d;

    public const double MinimumDepthGain = 0.25d;
}

public enum CombatFoundationCounterfactualAdmission
{
    Rejected = 0,
    Improved = 1,
    Victory = 2
}

public sealed class CombatCampaignFoundationTrainingRequest
{
    public ulong RunSeed { get; set; }

    public string DecisionProfile { get; set; } = "balanced";

    public int Iterations { get; set; } = 8;

    public int TrainingCampaignsPerIteration { get; set; } = 64;

    public int ArenaCampaignsPerDifficulty { get; set; } = 32;

    public int ArenaConfirmationCampaignsPerDifficulty { get; set; } = 64;

    public int NormalValidationCampaigns { get; set; } = 200;

    public int AdvancedValidationCampaigns { get; set; } = 500;

    public int CapabilityProbeCampaignsPerDifficulty { get; set; } = 16;

    public int PreflightCampaignsPerDifficulty { get; set; }

    public ulong PreflightSeedStart { get; set; } = 1_000_000UL;

    public bool PreflightOnly { get; set; }

    public int MaximumDegreeOfParallelism { get; set; } = 1;

    public bool EnableEarlyValidationStop { get; set; } = true;

    public bool EnableCurriculum { get; set; } = true;

    public bool EnableStratifiedReplay { get; set; } = true;

    public bool EnableHardSeedCurriculum { get; set; } = true;

    public bool EnableCounterfactualHardEncounters { get; set; } = true;

    public bool EnableSuccessCaseArchive { get; set; } = true;

    public bool EnableArenaRecovery { get; set; } = true;

    public int ArenaInvalidRetryCount { get; set; } = 1;

    public double ArenaInvalidRateLimit { get; set; } = 0.02d;

    public bool EnableTuningArena { get; set; } = true;

    public int TuningNormalCampaigns { get; set; } = 24;

    public int TuningAdvancedCampaigns { get; set; } = 24;

    public ulong TuningSeedStart { get; set; } = 1_500_000UL;

    public double NormalAcceptanceRate { get; set; } = 0.90d;

    public double AdvancedAcceptanceRate { get; set; } = 0.50d;

    public string NativeProgramPackageHash { get; set; } = "";

    public string TrainingPolicyVersion { get; set; } =
        "foundation-governance-v8";

    public double HardSeedReplayShare { get; set; } = 0.35d;

    public double MinimumAdvancedReplayShare { get; set; } = 0.35d;

    public int ExpertReplayEpisodeLimit { get; set; }

    public int MaximumConsecutiveRejectedIterations { get; set; } =
        CombatFoundationStagnationProtocol
            .DefaultMaximumConsecutiveRejectedIterations;

    public List<CombatEpisode> ExpertReplayEpisodes { get; set; } = new();

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

    public double SelfPlayExplorationProbability { get; set; } = 0.15d;

    public double SelfPlayExplorationTemperature { get; set; } = 1d;

    public double TeacherExactBranchProbability { get; set; } = 0.15d;

    public double HardTeacherExactBranchProbability { get; set; } = 1d;

    public ulong TrainingSeedStart { get; set; } = 10_000UL;

    public ulong ArenaSeedStart { get; set; } = 1_000_000UL;

    public ulong ValidationSeedStart { get; set; } = 2_000_000UL;

    public CombatDecisionProfile Profile { get; set; } = new();

    public CombatPolicyValueTrainingOptions Training { get; set; } = new();

    public CombatCampaignDefinition TrainingCampaign { get; set; } = new();

    public CombatCampaignDefinition ValidationCampaign { get; set; } = new();

    public Action<int, int, string>? Progress { get; set; }

    public Action<CombatCampaignFoundationTelemetry>? Telemetry { get; set; }

    public Action<CombatFoundationCampaignObservation>? ObservationRecorded {
        get;
        set;
    }

    public Action<CombatFoundationSuccessCase>? SuccessCaseRecorded {
        get;
        set;
    }

    public CombatCampaignFoundationResumeState? Resume { get; set; }

    public Action<CombatCampaignFoundationResumeState>? Checkpoint { get; set; }
}

public sealed class CombatCampaignFoundationResumeState
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string Stage { get; set; } = "";

    public int NextIteration { get; set; }

    public int CompletedCampaigns { get; set; }

    public int GeneratedReplayEpisodes { get; set; }

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public CombatPolicyValueNetworkDefinition? WorkingChampion { get; set; }

    public List<CombatEpisode> Replay { get; set; } = new();

    public List<CombatCampaignFoundationIteration> Iterations { get; set; } =
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

public sealed class CombatFoundationCompatibilityManifest
{
    public int SchemaVersion { get; set; } =
        CombatFoundationWorkerProtocol.SchemaVersion;

    public string RulesetHash { get; set; } = "";

    public string NativeProgramPackageHash { get; set; } = "";

    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public string TrainingCampaignHash { get; set; } = "";

    public string ValidationCampaignHash { get; set; } = "";

    public int FeatureSchemaVersion { get; set; }

    public string FeatureEncodingMode { get; set; } = "";

    public string SearchPolicyVersion { get; set; } = "dynamic-search-v3";

    public string CurriculumVersion { get; set; } = "curriculum-v7";

    public string TrainingPolicyVersion { get; set; } = "";

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

    public int EffectiveParallelism { get; set; }

    public int ActiveCampaigns { get; set; }

    public int PeakConcurrentCampaigns { get; set; }

    public int ObservedWorkerThreads { get; set; }

    public int CompletedCampaigns { get; set; }

    public int RequestedCampaigns { get; set; }

    public int CompletedBattles { get; set; }

    public int MaximumCompletedBattleDepth { get; set; }

    public int MaximumActiveBattleDepth { get; set; }

    public int Depth1To5Campaigns { get; set; }

    public int Depth6To10Campaigns { get; set; }

    public int Depth11To20Campaigns { get; set; }

    public int Depth21To30Campaigns { get; set; }

    public int Depth31To37Campaigns { get; set; }

    public double ProjectedBattleDepth { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public int ModelEpoch { get; set; }

    public int ModelTotalEpochs { get; set; }

    public int ModelCompletedFrames { get; set; }

    public int ModelTotalFrames { get; set; }

    public double ModelEpochsPerSecond { get; set; }

    public double ModelValidationLoss { get; set; }

    public double ModelBestValidationLoss { get; set; }

    public int ModelBestEpoch { get; set; }

    public int ModelStaleEpochs { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public double PhaseEstimatedRemainingSeconds { get; set; }

    public long PolicyDecisions { get; set; }

    public long SearchSimulations { get; set; }

    public long SearchNodes { get; set; }

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

    public long AuthoritativeTeacherOverrides { get; set; }

    public double SearchSimulationsPerSecond { get; set; }

    public double ElapsedSeconds { get; set; }

    public double CampaignsPerSecond { get; set; }

    public double BattlesPerSecond { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }
}

public sealed class CombatCampaignFoundationIteration
{
    public int Iteration { get; set; }

    public int ReplayEpisodes { get; set; }

    public int TrainingReplayEpisodes { get; set; }

    public int TrainingReplayNormalEpisodes { get; set; }

    public int TrainingReplayAdvancedEpisodes { get; set; }

    public int TrainingReplaySuccessfulEpisodes { get; set; }

    public int TrainingReplayDroppedDuplicates { get; set; }

    public double TrainingReplayTargetNormalShare { get; set; }

    public int TrainingReplaySourceCampaigns { get; set; }

    public int TrainingReplaySelectedCampaigns { get; set; }

    public int TrainingReplaySuccessfulCampaigns { get; set; }

    public Dictionary<string, int> TrainingReplayQuotaShortfalls { get; set; } =
        new(StringComparer.Ordinal);

    public int HardSeedSourceCampaigns { get; set; }

    public int HardSeedTrainingCampaigns { get; set; }

    public int HardSeedTrainingVictories { get; set; }

    public int HardSeedEncounterCampaigns { get; set; }

    public int HardSeedCounterfactualCampaigns { get; set; }

    public int HardSeedCounterfactualVictories { get; set; }

    public int HardSeedCounterfactualImprovements { get; set; }

    public int HardSeedCounterfactualRejected { get; set; }

    public double EffectiveHardSeedReplayShare { get; set; }

    public Dictionary<string, int> HardSeedClusters { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> HardSeedSourceCategories { get; set; } =
        new(StringComparer.Ordinal);

    public int AdvancedTrainingCampaigns { get; set; }

    public string CurriculumStage { get; set; } = "";

    public double NormalWilsonLowerBound { get; set; }

    public double AdvancedWilsonLowerBound { get; set; }

    public double SelfPlayExplorationProbability { get; set; }

    public Dictionary<string, int> ModelFrameStrata { get; set; } =
        new(StringComparer.Ordinal);

    public double ModelMinimumFrameWeight { get; set; } = 1d;

    public double ModelMaximumFrameWeight { get; set; } = 1d;

    public string CandidateModelId { get; set; } = "";

    public int TuningSelectedEpoch { get; set; }

    public double TuningSelectedScore { get; set; }

    public int TuningCandidateCount { get; set; }

    public int TuningInvalidCampaigns { get; set; }

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

    public int ArenaConfirmationPairs { get; set; }

    public int ValidNormalArenaPairs { get; set; }

    public int ValidAdvancedArenaPairs { get; set; }

    public int CandidateOnlyWins { get; set; }

    public int ChampionOnlyWins { get; set; }

    public double PairedWinWilsonLowerBound { get; set; }

    public double CandidateScoreGain { get; set; }

    public double CandidateDepthGain { get; set; }

    public string IterativeGainKind { get; set; } = "";

    public string PromotionProtocolVersion { get; set; } = "";

    public double ChampionAverageCompletedBattles { get; set; }

    public double CandidateAverageCompletedBattles { get; set; }

    public bool Promoted { get; set; }

    public bool CurriculumCheckpointAccepted { get; set; }

    public string PromotionKind { get; set; } = "rejected";

    public string PromotionReason { get; set; } = "";

    public int ConsecutiveRejectedIterations { get; set; }

    public bool StagnationStopTriggered { get; set; }
}

public sealed class CombatCampaignFoundationValidation
{
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
    public int CampaignsPerDifficulty { get; set; }

    public int CompletedCampaigns { get; set; }

    public int InvalidCampaigns { get; set; }

    public int TerminalConsistencyViolations { get; set; }

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

public sealed class CombatFoundationCapabilityProbe
{
    public int CampaignsPerDifficulty { get; set; }

    public ulong SeedStart { get; set; }

    public List<CombatFoundationCapabilityProbeArm> Arms { get; set; } =
        new();
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

    public string Message { get; set; } = "";

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public List<CombatEpisode> Replay { get; set; } = new();

    public int GeneratedReplayEpisodes { get; set; }

    public int PersistedReplayEpisodes { get; set; }

    public int DiscardedCounterfactualEpisodes { get; set; }

    public int LoadedExpertReplayEpisodes { get; set; }

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

    public string SuccessArchiveDirectory { get; set; } = "";

    public string SuccessCaseIndexPath { get; set; } = "";

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

    public bool StoppedForStagnation { get; set; }

    public int ConsecutiveRejectedIterations { get; set; }

    public string IterationStopReason { get; set; } = "";

    public CombatCampaignFoundationValidation Validation { get; set; } = new();

    public CombatCampaignFoundationIntegrityReport Preflight { get; set; } = new();

    public CombatFoundationCapabilityProbe CapabilityProbe { get; set; } =
        new();

    public List<CombatCampaignResult> ValidationRuns { get; set; } = new();

    public int RequestedCampaigns { get; set; }

    public int CompletedCampaigns { get; set; }

    public int InvalidTrainingCampaigns { get; set; }

    public int DiscardedInvalidEpisodes { get; set; }

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

    public int PeakConcurrentCampaigns { get; set; }

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

    public long PolicyDecisions { get; set; }

    public long SearchSimulations { get; set; }

    public long SearchNodes { get; set; }

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

    public long AuthoritativeTeacherOverrides { get; set; }

    public int ModelCompletedEpochs { get; set; }

    public int ModelConfiguredEpochs { get; set; }

    public int ModelBestEpoch { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public double ModelBestValidationLoss { get; set; }

    public double ElapsedSeconds { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }
}

public sealed class CombatCampaignFoundationTrainer
{
    private readonly CombatCampaignRunner campaignRunner;

    public CombatCampaignFoundationTrainer(CombatCampaignRunner? campaignRunner = null)
    {
        this.campaignRunner = campaignRunner ?? new CombatCampaignRunner();
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

        var iterations = Math.Max(1, Math.Min(20, request.Iterations));
        var trainingCampaigns = Math.Max(
            2,
            Math.Min(1000, request.TrainingCampaignsPerIteration));
        var arenaPerDifficulty = Math.Max(
            1,
            Math.Min(100, request.ArenaCampaignsPerDifficulty));
        var arenaConfirmationPerDifficulty =
            arenaPerDifficulty >= 32
                ? Math.Max(
                    0,
                    Math.Min(
                        200,
                        request.ArenaConfirmationCampaignsPerDifficulty))
                : 0;
        var normalValidationCampaigns = Math.Max(
            5,
            Math.Min(1000, request.NormalValidationCampaigns));
        var advancedValidationCampaigns = Math.Max(
            5,
            Math.Min(1000, request.AdvancedValidationCampaigns));
        var capabilityProbeCampaigns = Math.Max(
            0,
            Math.Min(32, request.CapabilityProbeCampaignsPerDifficulty));
        var tuningNormalCampaigns = request.EnableTuningArena
            ? Math.Max(0, Math.Min(64, request.TuningNormalCampaigns))
            : 0;
        var tuningAdvancedCampaigns = request.EnableTuningArena
            ? Math.Max(0, Math.Min(64, request.TuningAdvancedCampaigns))
            : 0;
        var normalAcceptanceRate =
            double.IsNaN(request.NormalAcceptanceRate)
            || double.IsInfinity(request.NormalAcceptanceRate)
                ? 0.90d
                : Math.Max(0d, Math.Min(1d, request.NormalAcceptanceRate));
        var advancedAcceptanceRate =
            double.IsNaN(request.AdvancedAcceptanceRate)
            || double.IsInfinity(request.AdvancedAcceptanceRate)
                ? 0.50d
                : Math.Max(
                    0d,
                    Math.Min(1d, request.AdvancedAcceptanceRate));
        var requiredNormalVictories = (int)Math.Ceiling(
            normalValidationCampaigns * normalAcceptanceRate);
        var requiredAdvancedVictories = (int)Math.Ceiling(
            advancedValidationCampaigns * advancedAcceptanceRate);
        var parallelism = Math.Max(
            1,
            Math.Min(Environment.ProcessorCount, request.MaximumDegreeOfParallelism));
        var preflightPerDifficulty = Math.Max(
            0,
            Math.Min(100, request.PreflightCampaignsPerDifficulty));
        var seedPlan = request.RunSeed == 0UL
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
        var foundationTrainingOptions = request.Training.Normalized();
        foundationTrainingOptions.RequireAuthoritativeEpisodes = true;
        foundationTrainingOptions.MaximumDegreeOfParallelism = parallelism;
        foundationTrainingOptions.RandomSeed = seedPlan.ModelRandomSeed;
        var compatibility = new CombatFoundationCompatibilityManifest
        {
            RulesetHash = ruleset.RulesetHash,
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
            FeatureEncodingMode =
                foundationTrainingOptions.FeatureEncodingMode,
            TrainingPolicyVersion = request.TrainingPolicyVersion ?? "",
            StateDimensions = foundationTrainingOptions.StateDimensions,
            ActionDimensions = foundationTrainingOptions.ActionDimensions,
            HiddenDimensions = foundationTrainingOptions.HiddenDimensions
        };
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

        var resume = request.Resume?.SchemaVersion
                         == CombatFoundationWorkerProtocol.SchemaVersion
                     && ResumeCompatible(request.Resume)
                     && ManifestCompatible(
                         request.Resume.Compatibility,
                         compatibility)
            ? request.Resume
            : null;
        var compatibleInitialChampion =
            CombatPolicyValueNetworkValidator.TryValidate(
                initialChampion,
                out _)
                ? initialChampion
                : null;
        var result = new CombatCampaignFoundationTrainingResult
        {
            Champion = resume?.Champion ?? compatibleInitialChampion,
            RunSeed = seedPlan.RunSeed,
            TrainingSeedStart = seedPlan.TrainingSeedStart,
            ArenaSeedStart = seedPlan.ArenaSeedStart,
            TuningSeedStart = seedPlan.TuningSeedStart,
            ValidationSeedStart = seedPlan.ValidationSeedStart,
            ModelRandomSeed = seedPlan.ModelRandomSeed,
            Compatibility = compatibility
        };
        if (resume != null)
        {
            result.GeneratedReplayEpisodes = Math.Max(
                resume.GeneratedReplayEpisodes,
                resume.Replay?.Count ?? 0);
            result.Replay.AddRange(resume.Replay ?? new List<CombatEpisode>());
            result.Iterations.AddRange(
                resume.Iterations
                ?? new List<CombatCampaignFoundationIteration>());
            result.HardSeedHistory.AddRange(
                resume.HardSeedHistory
                ?? new List<CombatFoundationHardSeedHistoryEntry>());
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
        result.Replay.AddRange(expertReplay);
        var workingChampion = resume?.WorkingChampion ?? result.Champion;
        ICombatPolicyValueModel championModel = workingChampion == null
            ? NullCombatPolicyValueModel.Instance
            : new ManagedCombatPolicyValueModel(workingChampion);
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
        var totalCampaigns = iterations
                             * (trainingCampaigns
                                 + (arenaPerDifficulty
                                    + arenaConfirmationPerDifficulty) * 4
                                 + (tuningNormalCampaigns
                                    + tuningAdvancedCampaigns)
                                   * foundationTrainingOptions
                                       .RetainedModelCandidates)
                             + normalValidationCampaigns
                             + advancedValidationCampaigns
                             + capabilityProbeCampaigns * 2 * 3;
        result.RequestedCampaigns = totalCampaigns;
        var telemetry = new FoundationTelemetryTracker(
            request,
            parallelism,
            totalCampaigns,
            resume?.Telemetry,
            completedCampaigns);
        telemetry.ReportStage(
            resume == null ? "starting" : "resumed");

        if (preflightPerDifficulty > 0)
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
                cancellationToken);
            result.TerminalConsistencyViolations +=
                result.Preflight.TerminalConsistencyViolations;
            if (!result.Preflight.Passed)
            {
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
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
            ConsecutiveRejectedIterations(result.Iterations);
        if (ShouldStopForStagnation(
                request,
                result.Iterations,
                workingChampion != null))
        {
            result.StoppedForStagnation = true;
            result.IterationStopReason =
                CombatFoundationStagnationProtocol.Version
                + ": resumed after consecutive rejected candidates="
                + result.ConsecutiveRejectedIterations;
        }

        for (var iteration = startIteration;
             iteration < iterations;
             iteration++)
        {
            if (result.StoppedForStagnation)
            {
                break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var iterationNumber = iteration + 1;
            var priorNormalTrials = result.Iterations.Sum(item =>
                item.ValidNormalArenaPairs);
            var priorAdvancedTrials = result.Iterations.Sum(item =>
                item.ValidAdvancedArenaPairs);
            var priorNormalWins = result.Iterations.Sum(item =>
                (int)Math.Round(
                    item.CandidateNormalWinRate
                    * item.ValidNormalArenaPairs));
            var priorAdvancedWins = result.Iterations.Sum(item =>
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
                request.EnableHardSeedCurriculum);
            var hardSeedTrainingVictories = 0;
            var hardSeedCounterfactualCampaigns = 0;
            var hardSeedCounterfactualVictories = 0;
            var hardSeedCounterfactualImprovements = 0;
            var hardSeedCounterfactualRejected = 0;
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
                var invalidTrainingCampaignsBefore =
                    result.InvalidTrainingCampaigns;
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
                Parallel.For(
                    0,
                    trainingCampaigns,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = parallelism
                    },
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
                            campaignRunner.SimulationEngine);
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
                            var counterfactualFactory =
                                new RecordingCampaignPolicyFactory(
                                    hardTeacherProfile,
                                    NullCombatPolicyValueModel.Instance,
                                    request.DecisionProfile,
                                    0d,
                                    1d,
                                    campaignSeed,
                                    request.HardTeacherExactBranchProbability,
                                    campaignRunner.SimulationEngine);
                            counterfactualCampaign = RunCampaignSegment(
                                request.TrainingCampaign,
                                difficulty,
                                campaignSeed,
                                ruleset,
                                counterfactualFactory,
                                slot.FailureEncounterCheckpoint!,
                                telemetry,
                                "training-hard-counterfactual:"
                                + iterationNumber,
                                cancellationToken);
                            counterfactualEpisodes =
                                counterfactualFactory.Complete(
                                    counterfactualCampaign,
                                    encounterStartIndex,
                                    ":hard-counterfactual:"
                                    + iterationNumber
                                    + ":"
                                    + campaignIndex);
                        }
                        trainingRuns[campaignIndex] =
                            new FoundationTrainingCampaignRun
                            {
                                Campaign = campaign,
                                Episodes = episodes,
                                CounterfactualCampaign =
                                    counterfactualCampaign,
                                CounterfactualEpisodes =
                                    counterfactualEpisodes,
                                HardSeed = slot.HardSeed,
                                Schedule = slot,
                                LocalEncounter = localEncounter,
                                FailureEncounterCheckpoint =
                                    failureEncounterCheckpoint
                            };
                        ReportProgress(
                            request,
                            telemetry,
                            campaign,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 "
                            + iterationNumber
                            + " 轮：七层训练推演");
                    });
                for (var campaignIndex = 0;
                     campaignIndex < trainingRuns.Length;
                     campaignIndex++)
                {
                    var trainingRun = trainingRuns[campaignIndex]!;
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
                    else
                    {
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
                        result.FeatureLeakageViolations +=
                            SanitizeEpisodeFeatures(trainingRun.Episodes);
                        result.GeneratedReplayEpisodes +=
                            trainingRun.Episodes.Count;
                        if (trainingRun.CounterfactualCampaign != null)
                        {
                            hardSeedCounterfactualCampaigns++;
                            var admission = ClassifyCounterfactual(
                                trainingRun.Campaign,
                                trainingRun.CounterfactualCampaign);
                            if (admission
                                != CombatFoundationCounterfactualAdmission
                                    .Rejected)
                            {
                                ApplyHardEncounterTargets(
                                    trainingRun.CounterfactualEpisodes,
                                    trainingRun.CounterfactualCampaign,
                                    curriculumPlan.Stage
                                    + ":counterfactual",
                                    iterationNumber);
                                if (admission
                                    == CombatFoundationCounterfactualAdmission
                                        .Improved)
                                {
                                    ApplyImprovedCounterfactualTargets(
                                        trainingRun.CounterfactualEpisodes);
                                    hardSeedCounterfactualImprovements++;
                                }
                                result.FeatureLeakageViolations +=
                                    SanitizeEpisodeFeatures(
                                        trainingRun.CounterfactualEpisodes);
                                result.GeneratedReplayEpisodes +=
                                    trainingRun.CounterfactualEpisodes.Count;
                                result.Replay.AddRange(
                                    trainingRun.CounterfactualEpisodes);
                                if (trainingRun.CounterfactualCampaign
                                        .Battles.LastOrDefault()?.Outcome
                                    == CombatSimulationOutcome.Victory)
                                {
                                    hardSeedCounterfactualVictories++;
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
                PublishCheckpoint(
                    request,
                    CreateResumeState(
                        "model-training",
                        iteration,
                        completedCampaigns,
                        result,
                        telemetry,
                        workingChampion,
                        modelTraining: null,
                        trainingSchedule));
            }

            var replaySelection = CombatFoundationReplaySampler.Select(
                result.Replay,
                foundationTrainingOptions.ReplayEpisodeLimit,
                request.EnableStratifiedReplay,
                new CombatFoundationReplayBalanceOptions
                {
                    MinimumAdvancedShare =
                        request.MinimumAdvancedReplayShare,
                    AllowCrossDifficultyBackfill = false
                });
            var replayWindow = replaySelection.Episodes;
            result.Replay = replayWindow;
            telemetry.ReportStage("model-training:" + iterationNumber);
            var trainingSession = new CombatPolicyValueTrainingSession
            {
                Resume = resumeModelTraining
                    ? resume!.ModelTraining
                    : workingChampion == null
                        ? null
                        : new CombatPolicyValueTrainingResumeState
                        {
                            Model = workingChampion,
                            BestModel = workingChampion,
                            BestValidationLoss = double.MaxValue
                        },
                Progress = progress => telemetry.ModelTrainingProgress(
                    iterationNumber,
                    iterations,
                    progress),
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
                replayWindow,
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
            var tuning = SelectTunedCandidate(
                trained,
                result,
                request,
                ruleset,
                deploymentProfile,
                seedPlan.TuningSeedStart,
                iteration,
                tuningNormalCampaigns,
                tuningAdvancedCampaigns,
                parallelism,
                telemetry,
                ref completedCampaigns,
                totalCampaigns,
                cancellationToken);
            trained.Model = tuning.Model;
            var candidateModel =
                new ManagedCombatPolicyValueModel(trained.Model);
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
                (arenaPerDifficulty + arenaConfirmationPerDifficulty) * 4);
            foreach (var difficulty in new[] { "normal", "advanced" })
            {
                var arenaSeedBase = arenaSeed;
                arenaSeed += (ulong)arenaPerDifficulty;
                var arenaPairs = new FoundationArenaPair?[arenaPerDifficulty];
                Parallel.For(
                    0,
                    arenaPerDifficulty,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = parallelism
                    },
                    arenaIndex =>
                    {
                        var seed = arenaSeedBase + (ulong)arenaIndex;
                        var champion = RunCampaign(
                            request.TrainingCampaign,
                            difficulty,
                            seed,
                            ruleset,
                            new CombatDecisionSimulationPolicyFactory(
                                deploymentProfile,
                                policyValueModel: championModel),
                            telemetry,
                            "arena:" + difficulty + ":champion",
                            cancellationToken);
                        ReportProgress(
                            request,
                            telemetry,
                            champion,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 " + iterationNumber + " 轮：隔离种子竞技场");
                        var candidate = RunCampaign(
                            request.TrainingCampaign,
                            difficulty,
                            seed,
                            ruleset,
                            new CombatDecisionSimulationPolicyFactory(
                                deploymentProfile,
                                policyValueModel: candidateModel),
                            telemetry,
                            "arena:" + difficulty + ":candidate",
                            cancellationToken);
                        ReportProgress(
                            request,
                            telemetry,
                            candidate,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 " + iterationNumber + " 轮：隔离种子竞技场");
                        arenaPairs[arenaIndex] = new FoundationArenaPair
                        {
                            Champion = champion,
                            Candidate = candidate
                        };
                    });
                systemicArenaFailure |= RecoverArenaPairs(
                    arenaPairs,
                    request,
                    result,
                    ruleset,
                    deploymentProfile,
                    championModel,
                    candidateModel,
                    telemetry,
                    iterationNumber,
                    "screening:" + difficulty,
                    replacementSeedStart,
                    ref replacementCursor,
                    ref arenaInvalidSides,
                    plannedArenaSides,
                    invalidSignatureSeeds,
                    ref completedCampaigns,
                    totalCampaigns,
                    cancellationToken);
                for (var arenaIndex = 0;
                     arenaIndex < arenaPairs.Length;
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
            var screeningPassed =
                screeningPairIndexes.Count == arenaPerDifficulty * 2
                && screeningCandidateNormal + 0.0000001d
                   >= screeningChampionNormal
                && screeningCandidateAdvanced + 0.0000001d
                   >= screeningChampionAdvanced
                && (workingChampion == null
                    || screeningCandidateOnlyWins
                       > screeningChampionOnlyWins
                    || screeningProgressGain);
            var confirmationRan =
                screeningPassed && arenaConfirmationPerDifficulty > 0;
            if (confirmationRan)
            {
                foreach (var difficulty in new[] { "normal", "advanced" })
                {
                    var arenaSeedBase = arenaSeed;
                    arenaSeed += (ulong)arenaConfirmationPerDifficulty;
                    var arenaPairs =
                        new FoundationArenaPair?[arenaConfirmationPerDifficulty];
                    Parallel.For(
                        0,
                        arenaConfirmationPerDifficulty,
                        new ParallelOptions
                        {
                            CancellationToken = cancellationToken,
                            MaxDegreeOfParallelism = parallelism
                        },
                        arenaIndex =>
                        {
                            var seed = arenaSeedBase + (ulong)arenaIndex;
                            var champion = RunCampaign(
                                request.TrainingCampaign,
                                difficulty,
                                seed,
                                ruleset,
                                new CombatDecisionSimulationPolicyFactory(
                                    deploymentProfile,
                                    policyValueModel: championModel),
                                telemetry,
                                "arena-confirmation:"
                                + difficulty
                                + ":champion",
                                cancellationToken);
                            ReportProgress(
                                request,
                                telemetry,
                                champion,
                                ref completedCampaigns,
                                totalCampaigns,
                                "第 "
                                + iterationNumber
                                + " 轮：晋级确认竞技场");
                            var candidate = RunCampaign(
                                request.TrainingCampaign,
                                difficulty,
                                seed,
                                ruleset,
                                new CombatDecisionSimulationPolicyFactory(
                                    deploymentProfile,
                                    policyValueModel: candidateModel),
                                telemetry,
                                "arena-confirmation:"
                                + difficulty
                                + ":candidate",
                                cancellationToken);
                            ReportProgress(
                                request,
                                telemetry,
                                candidate,
                                ref completedCampaigns,
                                totalCampaigns,
                                "第 "
                                + iterationNumber
                                + " 轮：晋级确认竞技场");
                            arenaPairs[arenaIndex] = new FoundationArenaPair
                            {
                                Champion = champion,
                                Candidate = candidate
                            };
                        });
                    systemicArenaFailure |= RecoverArenaPairs(
                        arenaPairs,
                        request,
                        result,
                        ruleset,
                        deploymentProfile,
                        championModel,
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
                    foreach (var pair in arenaPairs)
                    {
                        championArena.Add(pair!.Champion);
                        candidateArena.Add(pair.Candidate);
                    }
                }
            }
            else
            {
                // Reserve the confirmation seed partition even when screening
                // rejects the candidate so resumed and uninterrupted runs use
                // identical later-iteration seeds.
                arenaSeed += (ulong)(arenaConfirmationPerDifficulty * 2);
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
            var expectedArenaPairs =
                (arenaPerDifficulty
                 + (confirmationRan
                     ? arenaConfirmationPerDifficulty
                     : 0)) * 2;
            var curriculumCheckpoint =
                validPairIndexes.Count == expectedArenaPairs
                                       && candidateNormal + 0.0000001d >= championNormal
                                       && candidateAdvanced + 0.0000001d >= championAdvanced;
            var discordantPairs =
                candidateOnlyWins + championOnlyWins;
            var pairedWinWilsonLowerBound =
                CombatFoundationCurriculum.WilsonLowerBound(
                    candidateOnlyWins,
                    discordantPairs);
            var meaningfulWinGain =
                candidateOnlyWins > championOnlyWins
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
            var bootstrapPromotion = workingChampion == null;
            var promoted = curriculumCheckpoint
                           && (bootstrapPromotion || iterativeGain);
            var promotionReason = !curriculumCheckpoint
                ? "regression-or-incomplete-arena"
                : bootstrapPromotion
                    ? "bootstrap-champion"
                : !iterativeGain
                    ? "no-iterative-gain"
                    : promoted
                        ? meaningfulWinGain
                            ? "paired-win-gain"
                            : "score-depth-gain"
                        : "no-meaningful-gain";
            result.Iterations.Add(new CombatCampaignFoundationIteration
            {
                Iteration = iteration + 1,
                ReplayEpisodes = result.Replay.Count,
                TrainingReplayEpisodes = replaySelection.Episodes.Count,
                TrainingReplayNormalEpisodes =
                    replaySelection.NormalEpisodes,
                TrainingReplayAdvancedEpisodes =
                    replaySelection.AdvancedEpisodes,
                TrainingReplaySuccessfulEpisodes =
                    replaySelection.SuccessfulEpisodes,
                TrainingReplayDroppedDuplicates =
                    replaySelection.DroppedDuplicateEpisodes,
                TrainingReplayTargetNormalShare =
                    replaySelection.TargetNormalShare,
                TrainingReplaySourceCampaigns =
                    replaySelection.SourceCampaigns,
                TrainingReplaySelectedCampaigns =
                    replaySelection.SelectedCampaigns,
                TrainingReplaySuccessfulCampaigns =
                    replaySelection.SuccessfulCampaigns,
                TrainingReplayQuotaShortfalls =
                    new Dictionary<string, int>(
                        replaySelection.QuotaShortfalls,
                        StringComparer.Ordinal),
                HardSeedSourceCampaigns =
                    hardSeedPlan.SourceCampaigns,
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
                CurriculumStage = curriculumPlan.Stage,
                NormalWilsonLowerBound =
                    curriculumPlan.NormalWilsonLowerBound,
                AdvancedWilsonLowerBound =
                    curriculumPlan.AdvancedWilsonLowerBound,
                SelfPlayExplorationProbability =
                    effectiveExplorationProbability,
                ModelFrameStrata =
                    new Dictionary<string, int>(
                        trained.FrameStrata,
                        StringComparer.Ordinal),
                ModelMinimumFrameWeight =
                    trained.MinimumFrameWeight,
                ModelMaximumFrameWeight =
                    trained.MaximumFrameWeight,
                CandidateModelId = trained.Model.ModelId,
                TuningSelectedEpoch = tuning.Epoch,
                TuningSelectedScore = tuning.Score,
                TuningCandidateCount = tuning.CandidateCount,
                TuningInvalidCampaigns = tuning.InvalidCampaigns,
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
                ArenaScreeningPairs = arenaPerDifficulty * 2,
                ArenaConfirmationPairs = confirmationRan
                    ? arenaConfirmationPerDifficulty * 2
                    : 0,
                ValidNormalArenaPairs = validNormalPairs,
                ValidAdvancedArenaPairs = validAdvancedPairs,
                CandidateOnlyWins = candidateOnlyWins,
                ChampionOnlyWins = championOnlyWins,
                PairedWinWilsonLowerBound =
                    pairedWinWilsonLowerBound,
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
                CurriculumCheckpointAccepted = curriculumCheckpoint,
                PromotionKind = promoted
                    ? "formal-champion"
                    : "rejected",
                PromotionReason = promotionReason,
                ConsecutiveRejectedIterations = promoted
                    ? 0
                    : ConsecutiveRejectedIterations(result.Iterations) + 1
            });
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
            if (promoted)
            {
                championModel = candidateModel;
                workingChampion = trained.Model;
            }
            if (promoted)
            {
                result.Champion = trained.Model;
            }
            var latestIteration = result.Iterations.Last();
            result.ConsecutiveRejectedIterations =
                latestIteration.ConsecutiveRejectedIterations;
            var stagnationStop = ShouldStopForStagnation(
                request,
                result.Iterations,
                workingChampion != null);
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
                    + ": consecutive rejected candidates="
                    + result.ConsecutiveRejectedIterations;
                break;
            }
        }

        if (result.Champion == null)
        {
            result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
            result.Message = result.Iterations.Any(item =>
                item.CurriculumCheckpointAccepted)
                ? "工作模型已完成课程迭代，但尚无最终 Boss 胜利；未执行正式隔离验证，也不会发布为正式底模。"
                : "没有候选通过竞技场课程门槛；未执行正式隔离验证，也不会发布为正式底模。";
            telemetry.ApplyTo(result);
            FinalizeCaseAnalysis(result);
            return result;
        }

        if (capabilityProbeCampaigns > 0)
        {
            result.CapabilityProbe = RunCapabilityProbe(
                request,
                ruleset,
                result.Champion,
                telemetry,
                capabilityProbeCampaigns,
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
        var earlyStopReason = "";
        foreach (var difficulty in new[] { "normal", "advanced" })
        {
            if (!string.IsNullOrWhiteSpace(earlyStopReason))
            {
                break;
            }
            var validationCount = difficulty == "normal"
                ? normalValidationCampaigns
                : advancedValidationCampaigns;
            var difficultySeedStart = seedPlan.ValidationSeedStart
                                      + (ulong)(difficulty == "advanced"
                                          ? normalValidationCampaigns
                                          : 0);
            var difficultyRuns = new CombatCampaignResult?[validationCount];
            for (var batchStart = 0;
                 batchStart < validationCount;
                 batchStart += parallelism)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batchCount = Math.Min(parallelism, validationCount - batchStart);
                Parallel.For(
                    0,
                    batchCount,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = parallelism
                    },
                    batchOffset =>
                    {
                        var index = batchStart + batchOffset;
                        var validationRun = RunCampaign(
                            request.ValidationCampaign,
                            difficulty,
                            difficultySeedStart + (ulong)index,
                            ruleset,
                            new CombatDecisionSimulationPolicyFactory(
                                deploymentProfile,
                                policyValueModel: championModel),
                            telemetry,
                            "validation:" + difficulty,
                            cancellationToken);
                        for (var battleIndex = 0;
                             battleIndex < validationRun.Battles.Count - 1;
                             battleIndex++)
                        {
                            validationRun.Battles[battleIndex].Events.Clear();
                        }
                        difficultyRuns[index] = validationRun;
                        ReportProgress(
                            request,
                            telemetry,
                            validationRun,
                            ref completedCampaigns,
                            totalCampaigns,
                            "最终隔离验证：" + difficulty);
                    });
                if (!request.EnableEarlyValidationStop)
                {
                    continue;
                }
                var completedRuns = difficultyRuns
                    .Where(item => item != null)
                    .Select(item => item!)
                    .ToList();
                var failures = completedRuns.Count(item =>
                    item.Invalid || !item.FinalBossVictory);
                var allowedFailures = string.Equals(
                    difficulty,
                    "normal",
                    StringComparison.Ordinal)
                    ? validationCount - requiredNormalVictories
                    : validationCount - requiredAdvancedVictories;
                if (failures > allowedFailures)
                {
                    earlyStopReason = string.Equals(
                        difficulty,
                        "normal",
                        StringComparison.Ordinal)
                        ? "普通难度剩余样本不足以达到 "
                          + normalAcceptanceRate.ToString("P0")
                          + " 验收线"
                        : "高级难度失败数已超过 20% 验收上限";
                    break;
                }
            }
            result.ValidationRuns.AddRange(
                difficultyRuns
                    .Where(item => item != null)
                    .Select(item => item!));
        }
        foreach (var campaign in result.ValidationRuns)
        {
            RecordCase(
                result,
                campaign,
                "validation",
                iterations,
                "champion",
                ruleset.RulesetHash,
                request.DecisionProfile,
                result.Champion?.ModelId ?? "",
                episodes: null,
                request: request);
        }

        var normalRuns = result.ValidationRuns.Where(item =>
            string.Equals(item.DifficultyId, "normal", StringComparison.Ordinal)).ToList();
        var advancedRuns = result.ValidationRuns.Where(item =>
            string.Equals(item.DifficultyId, "advanced", StringComparison.Ordinal)).ToList();
        result.Validation = new CombatCampaignFoundationValidation
        {
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
            EarlyStopped = !string.IsNullOrWhiteSpace(earlyStopReason),
            EarlyStopReason = earlyStopReason
        };
        result.TerminalConsistencyViolations +=
            result.ValidationRuns.Sum(CountTerminalConsistencyViolations);
        result.Validation.Passed = result.Validation.InvalidCampaigns == 0
                                   && result.TerminalConsistencyViolations == 0
                                   && result.FeatureLeakageViolations == 0
                                   && result.Validation.NormalVictories
                                   >= requiredNormalVictories
                                   && result.Validation.AdvancedVictories
                                   >= requiredAdvancedVictories;
        result.AcceptancePassed = result.Validation.Passed;
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
                  : "；已提前结束验证：" + earlyStopReason);
        telemetry.ApplyTo(result);
        FinalizeCaseAnalysis(result);
        return result;
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
        var observation = CombatFoundationCaseLearning.Observe(
            campaign,
            sourceStage,
            iteration,
            competitor,
            rulesetHash,
            decisionProfile,
            modelId,
            episodes);
        result.CampaignObservations.Add(observation);
        request.ObservationRecorded?.Invoke(observation);
        if (observation.ArchiveEligible)
        {
            var successCase = CombatFoundationCaseLearning.CreateSuccessCase(
                campaign,
                observation,
                episodes);
            result.SuccessCases.Add(successCase);
            request.SuccessCaseRecorded?.Invoke(successCase);
        }
    }

    private static void FinalizeCaseAnalysis(
        CombatCampaignFoundationTrainingResult result)
    {
        result.CaseAnalysis = CombatFoundationCaseLearning.Analyze(
            result.CampaignObservations);
    }

    private static bool ResumeCompatible(
        CombatCampaignFoundationResumeState resume)
    {
        if (!ModelCompatible(resume.Champion)
            || !ModelCompatible(resume.WorkingChampion)
            || !ModelCompatible(resume.ModelTraining?.Model)
            || !ModelCompatible(resume.ModelTraining?.BestModel))
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

    private static bool ManifestCompatible(
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
                   checkpoint.NativeProgramPackageHash,
                   current.NativeProgramPackageHash,
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
                   StringComparison.Ordinal);
    }

    private static string CampaignFingerprint(
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
            AppendCanonical(target, property.GetValue(value, null), depth + 1);
        }
        target.Append("};");
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
            Champion = result.Champion,
            WorkingChampion = workingChampion,
            Replay = new List<CombatEpisode>(result.Replay),
            Iterations = new List<CombatCampaignFoundationIteration>(
                result.Iterations),
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
        Action<CombatCampaignCheckpoint>? encounterStart = null)
    {
        var campaignWorkId = telemetry.EnterCampaign(stage);
        CombatCampaignResult? result = null;
        try
        {
            var plan =
                CombatCampaignWorldPlanner.Build(campaign, difficulty, seed);
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
            State = source.State,
            Completed = false
        };
    }

    private CombatCampaignFoundationIntegrityReport RunIntegrityPreflight(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        ICombatPolicyValueModel policyValueModel,
        FoundationTelemetryTracker telemetry,
        int campaignsPerDifficulty,
        ulong seedStart,
        int parallelism,
        CancellationToken cancellationToken)
    {
        telemetry.ReportStage("preflight");
        var difficulties = new[] { "normal", "advanced" };
        var runs =
            new CombatCampaignResult?[campaignsPerDifficulty * difficulties.Length];
        Parallel.For(
            0,
            runs.Length,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelism
            },
            index =>
            {
                var difficulty = difficulties[index % difficulties.Length];
                var seed = seedStart + (ulong)index;
                runs[index] = RunCampaign(
                    request.TrainingCampaign,
                    difficulty,
                    seed,
                    ruleset,
                    new CombatDecisionSimulationPolicyFactory(
                        CombatSearchBudgetPolicy.WithContext(
                            request.Profile,
                            "deployment"),
                        policyValueModel: policyValueModel),
                    telemetry,
                    "preflight:" + difficulty,
                    cancellationToken);
            });
        var completed = runs
            .Where(item => item != null)
            .Select(item => item!)
            .ToList();
        var report = new CombatCampaignFoundationIntegrityReport
        {
            CampaignsPerDifficulty = campaignsPerDifficulty,
            CompletedCampaigns = completed.Count,
            InvalidCampaigns = completed.Count(item => item.Invalid),
            TerminalConsistencyViolations = completed.Sum(
                CountTerminalConsistencyViolations)
        };
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
                        && report.TerminalConsistencyViolations == 0;
        return report;
    }

    private CombatFoundationCapabilityProbe RunCapabilityProbe(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        CombatPolicyValueNetworkDefinition champion,
        FoundationTelemetryTracker telemetry,
        int campaignsPerDifficulty,
        ulong seedStart,
        int parallelism,
        ref int completedCampaigns,
        int totalCampaigns,
        CancellationToken cancellationToken)
    {
        _ = parallelism;
        telemetry.ReportStage("capability-probe");
        var model = new ManagedCombatPolicyValueModel(champion);
        var definitions = new[]
        {
            (
                Id: "rule-baseline",
                Factory: (Func<ICombatSimulationPolicyFactory>)(() =>
                    new CombatDecisionSimulationPolicyFactory(
                        CombatSearchBudgetPolicy.WithContext(
                            request.Profile,
                            "deployment")))),
            (
                Id: "champion-deployment",
                Factory: (Func<ICombatSimulationPolicyFactory>)(() =>
                    new CombatDecisionSimulationPolicyFactory(
                        CombatSearchBudgetPolicy.WithContext(
                            request.Profile,
                            "deployment"),
                        policyValueModel: model))),
            (
                Id: "champion-teacher-hard",
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
            CampaignsPerDifficulty = campaignsPerDifficulty,
            SeedStart = seedStart
        };
        foreach (var definition in definitions)
        {
            var runs = new List<CombatCampaignResult>();
            foreach (var difficulty in new[] { "normal", "advanced" })
            {
                for (var index = 0;
                     index < campaignsPerDifficulty;
                     index++)
                {
                    var campaign = RunCampaign(
                        request.ValidationCampaign,
                        difficulty,
                        seedStart + (ulong)index,
                        ruleset,
                        definition.Factory(),
                        telemetry,
                        "capability-probe:" + definition.Id,
                        cancellationToken);
                    runs.Add(campaign);
                    ReportProgress(
                        request,
                        telemetry,
                        campaign,
                        ref completedCampaigns,
                        totalCampaigns,
                        "能力上限诊断：" + definition.Id);
                }
            }
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
        return report;
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
                var replacement = new FoundationArenaPair
                {
                    Champion = RunCampaign(
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
                    Candidate = RunCampaign(
                        request.TrainingCampaign,
                        pair.Candidate.DifficultyId,
                        seed,
                        ruleset,
                        new CombatDecisionSimulationPolicyFactory(
                            profile,
                            policyValueModel: candidateModel),
                        telemetry,
                        "arena-replacement:" + stage + ":candidate",
                        cancellationToken)
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
            if (!pair.Champion.Invalid && !pair.Candidate.Invalid)
            {
                return;
            }
            if (pair.Champion.Invalid)
            {
                result.ArenaRetryAttempts++;
                pair.Champion = RunCampaign(
                    request.TrainingCampaign,
                    pair.Champion.DifficultyId,
                    pair.Champion.WorldSeed,
                    ruleset,
                    new CombatDecisionSimulationPolicyFactory(
                        profile,
                        policyValueModel: championModel),
                    telemetry,
                    "arena-retry:" + stage + ":champion",
                    cancellationToken);
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
            if (pair.Candidate.Invalid)
            {
                result.ArenaRetryAttempts++;
                pair.Candidate = RunCampaign(
                    request.TrainingCampaign,
                    pair.Candidate.DifficultyId,
                    pair.Candidate.WorldSeed,
                    ruleset,
                    new CombatDecisionSimulationPolicyFactory(
                        profile,
                        policyValueModel: candidateModel),
                    telemetry,
                    "arena-retry:" + stage + ":candidate",
                    cancellationToken);
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
        IReadOnlyList<CombatCampaignFoundationIteration> iterations)
    {
        var count = 0;
        for (var index = iterations.Count - 1; index >= 0; index--)
        {
            if (iterations[index].Promoted)
            {
                break;
            }
            count++;
        }
        return count;
    }

    internal static bool ShouldStopForStagnation(
        CombatCampaignFoundationTrainingRequest request,
        IReadOnlyList<CombatCampaignFoundationIteration> iterations,
        bool hasChampion)
    {
        if (!hasChampion)
        {
            return false;
        }
        var limit = Math.Max(
            0,
            request.MaximumConsecutiveRejectedIterations);
        return limit > 0
               && ConsecutiveRejectedIterations(
                   iterations
                   ?? Array.Empty<CombatCampaignFoundationIteration>())
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
                Model = trained.Model
            });
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
                CandidateCount = candidates.Count
            };
        }

        var seedStride = (ulong)Math.Max(
            1,
            normalCampaigns + advancedCampaigns);
        var iterationSeedStart = tuningSeedStart
                                 + (ulong)Math.Max(0, iteration)
                                 * seedStride;
        TuningSelection? best = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = new ManagedCombatPolicyValueModel(candidate.Model);
            var runs = new CombatCampaignResult?[
                normalCampaigns + advancedCampaigns];
            var tuningCompletedCampaigns = completedCampaigns;
            Parallel.For(
                0,
                runs.Length,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = parallelism
                },
                index =>
                {
                    var normal = index < normalCampaigns;
                    var offset = normal
                        ? index
                        : index - normalCampaigns;
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
                            policyValueModel: model),
                        telemetry,
                        "tuning:epoch-" + candidate.Epoch,
                        cancellationToken);
                    runs[index] = campaign;
                    ReportProgress(
                        request,
                        telemetry,
                        campaign,
                        ref tuningCompletedCampaigns,
                        totalCampaigns,
                        "第 "
                        + (iteration + 1)
                        + " 轮：调参竞技场 Epoch "
                        + candidate.Epoch);
                });
            completedCampaigns = tuningCompletedCampaigns;
            var completed = runs
                .Where(item => item != null)
                .Select(item => item!)
                .ToList();
            foreach (var campaign in completed)
            {
                RecordCase(
                    foundationResult,
                    campaign,
                    "tuning",
                    iteration + 1,
                    "epoch-" + candidate.Epoch,
                    ruleset.RulesetHash,
                    request.DecisionProfile,
                    candidate.Model.ModelId,
                    episodes: null,
                    request);
            }
            var invalid = completed.Count(item => item.Invalid);
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
            var score = invalid > 0
                ? -1000d - invalid
                : WinRate(normalRuns, "normal") * 0.40d
                  + WinRate(advancedRuns, "advanced") * 0.40d
                  + depth * 0.20d;
            var selection = new TuningSelection
            {
                Model = CombatPolicyValueBatchTrainer.Clone(candidate.Model),
                Epoch = candidate.Epoch,
                Score = score,
                ValidationLoss = candidate.ValidationLoss,
                CandidateCount = candidates.Count,
                InvalidCampaigns = invalid
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
            CandidateCount = candidates.Count
        };
        best.Model.ModelId =
            (best.Model.ModelId ?? "aura-combat-policy-value")
            + "-epoch-"
            + best.Epoch;
        return best;
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

    private sealed class FoundationTelemetryTracker
    {
        private readonly CombatCampaignFoundationTrainingRequest request;
        private readonly int effectiveParallelism;
        private readonly int requestedCampaigns;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly object workerGate = new();
        private readonly HashSet<int> observedWorkerThreads = new();
        private readonly Dictionary<long, int> activeCampaignDepths = new();
        private readonly int[] completedDepthBuckets = new int[5];
        private readonly int initialGen0 = GC.CollectionCount(0);
        private readonly int initialGen1 = GC.CollectionCount(1);
        private readonly int initialGen2 = GC.CollectionCount(2);
        private int activeCampaigns;
        private int peakConcurrentCampaigns;
        private int completedCampaigns;
        private int completedBattles;
        private int maximumCompletedBattleDepth;
        private long completedCampaignDepthTotal;
        private int completedCampaignDepthCount;
        private long policyDecisions;
        private long searchSimulations;
        private long searchNodes;
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
        private long authoritativeTeacherOverrides;
        private long nextCampaignWorkId;
        private long lastReportMilliseconds = -1000L;
        private int modelIteration;
        private int modelEpoch;
        private int modelTotalEpochs;
        private int modelCompletedFrames;
        private int modelTotalFrames;
        private double modelEpochsPerSecond;
        private double modelValidationLoss;
        private double modelBestValidationLoss;
        private int modelBestEpoch;
        private int modelStaleEpochs;
        private bool modelEarlyStopped;
        private double phaseEstimatedRemainingSeconds;

        public FoundationTelemetryTracker(
            CombatCampaignFoundationTrainingRequest request,
            int effectiveParallelism,
            int requestedCampaigns,
            CombatCampaignFoundationTelemetry? initial = null,
            int initialCompletedCampaigns = 0)
        {
            this.request = request;
            this.effectiveParallelism = effectiveParallelism;
            this.requestedCampaigns = requestedCampaigns;
            completedCampaigns = Math.Max(
                initialCompletedCampaigns,
                initial?.CompletedCampaigns ?? 0);
            completedBattles = Math.Max(0, initial?.CompletedBattles ?? 0);
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
                authoritativeTeacherOverrides = Math.Max(
                    0L,
                    initial.AuthoritativeTeacherOverrides);
                peakConcurrentCampaigns = Math.Max(
                    0,
                    initial.PeakConcurrentCampaigns);
            }
        }

        public CombatCampaignFoundationTelemetry Current(string stage)
        {
            return Snapshot(stage);
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
                modelBestValidationLoss = progress.BestValidationLoss;
                modelBestEpoch = Math.Max(0, progress.BestEpoch);
                modelStaleEpochs = Math.Max(0, progress.StaleEpochs);
                modelEarlyStopped = progress.EarlyStopped;
                phaseEstimatedRemainingSeconds = Math.Max(
                    0d,
                    progress.EstimatedRemainingSeconds);
            }
            Report(
                "model-training:"
                + Math.Max(1, iteration)
                + ":"
                + (progress.Stage ?? "training"),
                force: true);
        }

        public long EnterCampaign(string stage)
        {
            var workId = Interlocked.Increment(ref nextCampaignWorkId);
            lock (workerGate)
            {
                observedWorkerThreads.Add(Thread.CurrentThread.ManagedThreadId);
                activeCampaignDepths[workId] = 0;
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
                    ref authoritativeTeacherOverrides,
                    Math.Max(
                        0,
                        battle.Metrics.AuthoritativeTeacherOverrides));
                lock (workerGate)
                {
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
            Report(stage, force: true);
        }

        public void ReportStage(string stage)
        {
            Report(stage, force: true);
        }

        public void ApplyTo(CombatCampaignFoundationTrainingResult result)
        {
            var snapshot = Snapshot("completed");
            result.EffectiveParallelism = snapshot.EffectiveParallelism;
            result.PeakConcurrentCampaigns = snapshot.PeakConcurrentCampaigns;
            result.ObservedWorkerThreads = snapshot.ObservedWorkerThreads;
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
            result.PolicyDecisions = snapshot.PolicyDecisions;
            result.SearchSimulations = snapshot.SearchSimulations;
            result.SearchNodes = snapshot.SearchNodes;
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
            result.AuthoritativeTeacherOverrides =
                snapshot.AuthoritativeTeacherOverrides;
            result.ModelCompletedEpochs = snapshot.ModelEpoch;
            result.ModelConfiguredEpochs = snapshot.ModelTotalEpochs;
            result.ModelBestEpoch = snapshot.ModelBestEpoch;
            result.ModelEarlyStopped = snapshot.ModelEarlyStopped;
            result.ModelBestValidationLoss =
                snapshot.ModelBestValidationLoss;
            result.ElapsedSeconds = snapshot.ElapsedSeconds;
            result.Gen0Collections = snapshot.Gen0Collections;
            result.Gen1Collections = snapshot.Gen1Collections;
            result.Gen2Collections = snapshot.Gen2Collections;
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
            double snapshotModelValidationLoss;
            double snapshotModelBestValidationLoss;
            int snapshotModelBestEpoch;
            int snapshotModelStaleEpochs;
            bool snapshotModelEarlyStopped;
            double snapshotPhaseRemainingSeconds;
            double snapshotRootMaximumVisitShareTotal;
            int snapshotRootMaximumVisitShareSamples;
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
                snapshotModelValidationLoss = modelValidationLoss;
                snapshotModelBestValidationLoss = modelBestValidationLoss;
                snapshotModelBestEpoch = modelBestEpoch;
                snapshotModelStaleEpochs = modelStaleEpochs;
                snapshotModelEarlyStopped = modelEarlyStopped;
                snapshotPhaseRemainingSeconds =
                    phaseEstimatedRemainingSeconds;
                snapshotRootMaximumVisitShareTotal =
                    rootMaximumVisitShareTotal;
                snapshotRootMaximumVisitShareSamples =
                    rootMaximumVisitShareSamples;
            }
            var elapsedSeconds = Math.Max(0.001d, stopwatch.Elapsed.TotalSeconds);
            var campaigns = Volatile.Read(ref completedCampaigns);
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
            var battleEstimatedRemainingSeconds = battleRate <= 0d
                ? 0d
                : remainingBattleWork / battleRate;
            var phase = ResolvePhase(stage);
            return new CombatCampaignFoundationTelemetry
            {
                Stage = stage ?? "",
                Phase = phase,
                Iteration = snapshotModelIteration,
                TotalIterations = Math.Max(1, request.Iterations),
                EffectiveParallelism = effectiveParallelism,
                ActiveCampaigns = Math.Max(0, Volatile.Read(ref activeCampaigns)),
                PeakConcurrentCampaigns = Volatile.Read(ref peakConcurrentCampaigns),
                ObservedWorkerThreads = observedThreads,
                CompletedCampaigns = campaigns,
                RequestedCampaigns = requestedCampaigns,
                CompletedBattles = battles,
                MaximumCompletedBattleDepth = maximumDepth,
                MaximumActiveBattleDepth = activeMaximumDepth,
                Depth1To5Campaigns = depthBuckets[0],
                Depth6To10Campaigns = depthBuckets[1],
                Depth11To20Campaigns = depthBuckets[2],
                Depth21To30Campaigns = depthBuckets[3],
                Depth31To37Campaigns = depthBuckets[4],
                ProjectedBattleDepth = projectedDepth,
                EstimatedRemainingSeconds =
                    string.Equals(
                        phase,
                        "model-training",
                        StringComparison.Ordinal)
                    && snapshotPhaseRemainingSeconds > 0d
                        ? snapshotPhaseRemainingSeconds
                        : battleEstimatedRemainingSeconds,
                ModelEpoch = snapshotModelEpoch,
                ModelTotalEpochs = snapshotModelTotalEpochs,
                ModelCompletedFrames = snapshotModelCompletedFrames,
                ModelTotalFrames = snapshotModelTotalFrames,
                ModelEpochsPerSecond = snapshotModelEpochsPerSecond,
                ModelValidationLoss = snapshotModelValidationLoss,
                ModelBestValidationLoss = snapshotModelBestValidationLoss,
                ModelBestEpoch = snapshotModelBestEpoch,
                ModelStaleEpochs = snapshotModelStaleEpochs,
                ModelEarlyStopped = snapshotModelEarlyStopped,
                PhaseEstimatedRemainingSeconds =
                    snapshotPhaseRemainingSeconds,
                PolicyDecisions = Volatile.Read(ref policyDecisions),
                SearchSimulations = simulationCount,
                SearchNodes = Volatile.Read(ref searchNodes),
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
                AuthoritativeTeacherOverrides =
                    Volatile.Read(ref authoritativeTeacherOverrides),
                SearchSimulationsPerSecond = simulationCount / elapsedSeconds,
                ElapsedSeconds = elapsedSeconds,
                CampaignsPerSecond = campaigns / elapsedSeconds,
                BattlesPerSecond = battleRate,
                Gen0Collections = Math.Max(0, GC.CollectionCount(0) - initialGen0),
                Gen1Collections = Math.Max(0, GC.CollectionCount(1) - initialGen1),
                Gen2Collections = Math.Max(0, GC.CollectionCount(2) - initialGen2)
            };
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
    }

    private sealed class TuningSelection
    {
        public CombatPolicyValueNetworkDefinition Model { get; set; } = new();

        public int Epoch { get; set; }

        public double Score { get; set; }

        public double ValidationLoss { get; set; }

        public int CandidateCount { get; set; }

        public int InvalidCampaigns { get; set; }
    }

    private sealed class FoundationArenaPair
    {
        public CombatCampaignResult Champion { get; set; } = new();

        public CombatCampaignResult Candidate { get; set; } = new();
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
        private readonly List<CombatEpisodeRecordingPolicy> policies = new();

        public RecordingCampaignPolicyFactory(
            CombatDecisionProfile profile,
            ICombatPolicyValueModel policyValue,
            string decisionProfile,
            double explorationProbability,
            double explorationTemperature,
            ulong campaignSeed,
            double authoritativeAuditProbability,
            CombatSimulationEngine authoritativeEngine)
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
        }

        public string PolicyId => "aura-foundation-training:" + decisionProfile;

        public ICombatSimulationPolicy Create()
        {
            var decisionPolicy = new CombatDecisionSimulationPolicy(
                    profile,
                    policyValueModel: policyValue,
                    exploration: new CombatSelfPlayExplorationOptions
                    {
                        Probability = explorationProbability,
                        Temperature = explorationTemperature,
                        RandomSeed = CombatFoundationSeedPlan.ToRandomSeed(
                            campaignSeed
                            ^ (ulong)(policies.Count + 1))
                    });
            ICombatSimulationPolicy teacher = authoritativeAuditProbability
                <= 0d
                ? decisionPolicy
                : new CombatAuthoritativeBranchTeacherPolicy(
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
                decisionProfile);
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
