using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class ControllerSettings
{
    public const int CurrentSchemaVersion = 16;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

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
        // Keep the independent trainer's vetted development preset explicit.
        // Persisted controller settings take precedence except for the
        // independently calibrated CPU execution contract.
        return new CombatFoundationTrainingParameters
        {
            GovernanceProfile =
                CombatFoundationGovernanceProfileNames.Development,
            Iterations = 8,
            AdditionalIterationsOnResume = 2,
            TrainingCampaignsPerIteration = 96,
            ArenaCampaignsPerDifficulty = 16,
            ArenaConfirmationCampaignsPerDifficulty = 48,
            NormalValidationCampaigns = 100,
            AdvancedValidationCampaigns = 200,
            CapabilityProbeCampaignsPerDifficulty = 64,
            CapabilityProbeTeacherCampaignsPerDifficulty = 16,
            CapabilityProbeBatchSize = 16,
            PreflightCampaignsPerDifficulty = 16,
            TuningInterval = 2,
            ParallelismProfile =
                CombatFoundationExecutionProfileNames.Auto,
            InferenceExecutionMode =
                CombatFoundationExecutionProfileNames.DirectInference,
            InferenceParallelism = 0,
            InferenceLaneCount = 0,
            InferenceBatchSize = 0,
            ReuseAutoTuneCache = false,
            EnableOfflineTuningGate = true,
            EnableSequentialArenaStop = true,
            ArenaEvaluationBatchSize = 16,
            SuccessExpertReplayShare = 0.10d,
            MinimumAdvancedReplayShare = 0.40d,
            MinimumAdvancedDefeatReplayShare = 0.25d,
            SelfPlayExplorationProbability = 0.15d,
            ModelEpochs = 40,
            ModelBatchSize = 64,
            ModelGradientShardCount = 0,
            ModelMaximumUnsafeEndTurnFrameShare = 0.20d,
            ModelUnsafeEndTurnRiskAuxiliaryShare = 0.10d,
            MinimumArenaDiscordantPairs = 8,
            MaximumOfflineHeadRegression = 0.05d,
            MaximumStateFeatureCollisionRate = 0.20d,
            MaximumActionFeatureCollisionRate = 0.06d,
            ModelLearningRate = 0.004d,
            ModelL2 = 0.002d,
            ModelStateDimensions = 1024,
            ModelActionDimensions = 1024,
            ModelHiddenDimensions = 512,
            TransformerTeacherBackend =
                CombatTransformerTeacherBackendNames.Auto,
            TransformerTeacherEpochs = 12,
            TransformerTeacherBatchSize = 64,
            TransformerTeacherStateDimensions = 1024,
            TransformerTeacherActionDimensions = 1024,
            TransformerTeacherHiddenDimensions = 384,
            TransformerTeacherLayers = 6,
            TransformerTeacherAttentionHeads = 8,
            TransformerTeacherFeedForwardDimensions = 1536,
            TransformerTeacherHistoryLength = 12,
            TransformerTeacherMinimumFrames = 1024,
            TransformerTeacherMaximumFrames = 10000,
            TransformerTeacherEnableWarmStart = true,
            TransformerTeacherCpuRefreshInterval = 4,
            TransformerTeacherCpuEpochs = 4,
            TransformerTeacherCpuIncrementalEpochs = 1,
            TransformerTeacherCpuFinalEpochs = 4,
            TransformerTeacherEnableAdaptiveRefresh = true,
            TransformerTeacherAdaptiveRefreshDriftThreshold = 0.15d,
            TransformerTeacherEnableFixedAnchorValidation = true,
            TransformerTeacherMaximumHeadRegression = 0.05d,
            TransformerTeacherIncrementalEpochs = 4,
            TransformerTeacherFinalEpochs = 12,
            TransformerTeacherCpuThreads = 0,
            TransformerTeacherCpuInteropThreads = 0,
            TransformerTeacherMicroBatchSize = 0,
            TransformerTeacherDataLoaderWorkers = 2,
            TransformerTeacherPrefetchBatches = 2,
            TransformerTeacherMemoryReserveBytes =
                CombatFoundationParallelismProtocol.DefaultTeacherReserveBytes,
            TransformerTeacherEnablePinnedMemory = true,
            TransformerTeacherEnableMixedPrecision = true,
            TransformerDistillationWeight = 0.35d,
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

internal sealed class ControllerCheckpointChoice
{
    public string Label { get; set; } = "";

    public CombatFoundationCheckpointCatalogEntry Entry { get; set; } = new();
}

internal sealed class ControllerResumeModeChoice
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";
}

internal sealed class ControllerWorkerResultSummary
{
    public int SchemaVersion { get; set; }

    public string JobId { get; set; } = "";

    public bool Success { get; set; }

    public bool WorkerCompleted { get; set; }

    public bool TrainingSucceeded { get; set; }

    public bool ModelAccepted { get; set; }

    public int EpochsExecuted { get; set; }

    public int SelectedEpoch { get; set; }

    public int BestValidationEpoch { get; set; }

    public int DeploymentSelectedEpoch { get; set; }

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

    public string RequestedStartMode { get; set; } = "";

    public string EffectiveStartMode { get; set; } = "";

    public bool Resumable { get; set; }

    public int CheckpointWriteFailures { get; set; }

    public string CheckpointWarning { get; set; } = "";

    public int EffectiveCheckpointSerializationParallelism { get; set; }

    public bool CheckpointSerializationAutoScaled { get; set; }

    public double CheckpointSerializationSeconds { get; set; }

    public long CheckpointWritesEnqueued { get; set; }

    public long CheckpointWritesExecuted { get; set; }

    public long CheckpointWritesCoalesced { get; set; }

    public ControllerTrainingResultSummary? Training { get; set; }
}

internal sealed class ControllerTrainingResultSummary
{
    public bool Success { get; set; }

    public bool AcceptancePassed { get; set; }

    public string Message { get; set; } = "";

    public int GeneratedReplayEpisodes { get; set; }

    public int PersistedReplayEpisodes { get; set; }

    public bool SemanticGatePassed { get; set; } = true;

    public int SemanticRejectedCampaigns { get; set; }

    public int DiscardedSemanticEpisodes { get; set; }

    public string SemanticGateFailureReason { get; set; } = "";

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
