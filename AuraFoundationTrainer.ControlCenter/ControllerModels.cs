using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class ControllerSettings
{
    public const int PreviousSchemaVersion = 21;

    public const int CurrentSchemaVersion = 22;

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

    internal bool MigrateFromPreviousSchema()
    {
        if (SchemaVersion < 17
            || SchemaVersion > PreviousSchemaVersion)
        {
            return false;
        }
        Parameters ??= new CombatFoundationTrainingParameters();
        if (SchemaVersion == 17)
        {
            // v18 moves the development preset to fixed training blocks with
            // sparse Arena checkpoints. Preserve unrelated user configuration.
            if (string.Equals(
                    Parameters.GovernanceProfile,
                    CombatFoundationGovernanceProfileNames.Development,
                    StringComparison.OrdinalIgnoreCase)
                && Parameters.Iterations == 8
                && Parameters.TrainingCampaignsPerIteration == 96
                && Parameters.ArenaCampaignsPerDifficulty == 16
                && Parameters.ArenaConfirmationCampaignsPerDifficulty == 48)
            {
                Parameters.Iterations = 12;
                Parameters.ArenaCampaignsPerDifficulty = 8;
                Parameters.ArenaEvaluationInterval = 6;
                Parameters.ArenaConfirmationFinalIterationOnly = true;
                Parameters.NormalValidationCampaigns = 50;
                Parameters.AdvancedValidationCampaigns = 50;
                Parameters.CapabilityProbeCampaignsPerDifficulty = 16;
                Parameters.CapabilityProbeTeacherCampaignsPerDifficulty = 4;
                Parameters.PreflightCampaignsPerDifficulty = 8;
                Parameters.TuningInterval = 6;
                Parameters.TuningNormalCampaigns = 8;
                Parameters.TuningAdvancedCampaigns = 16;
                Parameters.TuningScreeningNormalCampaigns = 4;
                Parameters.TuningScreeningAdvancedCampaigns = 8;
                Parameters.TuningFinalistCount = 1;
            }
            SchemaVersion = 18;
        }
        // v19 raises formal confirmation from 48 to 56 pairs per difficulty.
        // Together with the 8-pair screen this reaches the 64-pair
        // non-inferiority evidence contract. Only migrate the shipped preset.
        if (string.Equals(
                Parameters.GovernanceProfile,
                CombatFoundationGovernanceProfileNames.Development,
                StringComparison.OrdinalIgnoreCase)
            && Parameters.Iterations == 12
            && Parameters.TrainingCampaignsPerIteration == 96
            && Parameters.ArenaCampaignsPerDifficulty == 8
            && Parameters.ArenaConfirmationCampaignsPerDifficulty == 48
            && Parameters.ArenaEvaluationInterval == 6
            && Parameters.ArenaConfirmationFinalIterationOnly)
        {
            Parameters.ArenaConfirmationCampaignsPerDifficulty = 56;
        }
        // v20 batches several training iterations inside one isolated worker.
        // The field initializer already supplies 3 for old JSON that omitted
        // it; this guard also repairs hand-authored zero values.
        if (Parameters.IterationsPerIsolatedProcess <= 0)
        {
            Parameters.IterationsPerIsolatedProcess = 3;
        }
        // v21 moves the student and teacher state encoders to the
        // partitioned-v4 2048-slot layout. Preserve deliberately customized
        // dimensions and thresholds; migrate only the shipped v20 values.
        if (Parameters.ModelStateDimensions == 1024)
        {
            Parameters.ModelStateDimensions = 2048;
        }
        if (Parameters.TransformerTeacherStateDimensions == 1024)
        {
            Parameters.TransformerTeacherStateDimensions = 2048;
        }
        if (Math.Abs(
                Parameters.MaximumStateFeatureCollisionRate - 0.20d)
            < 0.000001d)
        {
            Parameters.MaximumStateFeatureCollisionRate = 0.05d;
        }
        // v22 makes the final audit a complete random 50 + 50 sample. The
        // capability probe and Arena remain separate evidence stages.
        if (Parameters.NormalValidationCampaigns == 64
            && Parameters.AdvancedValidationCampaigns == 128)
        {
            Parameters.NormalValidationCampaigns = 50;
            Parameters.AdvancedValidationCampaigns = 50;
            Parameters.EnableEarlyValidationStop = false;
        }
        if (Parameters.CapabilityProbeCampaignsPerDifficulty == 32
            && Parameters.CapabilityProbeTeacherCampaignsPerDifficulty == 8)
        {
            Parameters.CapabilityProbeCampaignsPerDifficulty = 16;
            Parameters.CapabilityProbeTeacherCampaignsPerDifficulty = 4;
            Parameters.CapabilityProbeBatchSize = 8;
        }
        SchemaVersion = CurrentSchemaVersion;
        return true;
    }

    private static CombatFoundationTrainingParameters CreateDefaultParameters()
    {
        // Keep the independent trainer's vetted development preset explicit.
        // Persisted controller settings take precedence except for the
        // independently calibrated CPU execution contract.
        return new CombatFoundationTrainingParameters
        {
            GovernanceProfile =
                CombatFoundationGovernanceProfileNames.Development,
            Iterations = 12,
            IterationsPerIsolatedProcess = 3,
            AdditionalIterationsOnResume = 2,
            TrainingCampaignsPerIteration = 96,
            ArenaCampaignsPerDifficulty = 8,
            ArenaConfirmationCampaignsPerDifficulty = 56,
            ArenaEvaluationInterval = 6,
            ArenaConfirmationFinalIterationOnly = true,
            NormalValidationCampaigns = 50,
            AdvancedValidationCampaigns = 50,
            EnableEarlyValidationStop = false,
            CapabilityProbeCampaignsPerDifficulty = 16,
            CapabilityProbeTeacherCampaignsPerDifficulty = 4,
            CapabilityProbeBatchSize = 8,
            PreflightCampaignsPerDifficulty = 8,
            TuningInterval = 6,
            ParallelismProfile =
                CombatFoundationExecutionProfileNames.Auto,
            InferenceExecutionMode =
                CombatFoundationExecutionProfileNames.DirectInference,
            InferenceParallelism = 0,
            InferenceLaneCount = 0,
            InferenceBatchSize = 0,
            ReuseAutoTuneCache = true,
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
            MaximumStateFeatureCollisionRate = 0.05d,
            MaximumActionFeatureCollisionRate = 0.06d,
            ModelLearningRate = 0.004d,
            ModelL2 = 0.002d,
            ModelStateDimensions = 2048,
            ModelActionDimensions = 1024,
            ModelHiddenDimensions = 512,
            TransformerTeacherBackend =
                CombatTransformerTeacherBackendNames.Auto,
            TransformerTeacherEpochs = 12,
            TransformerTeacherBatchSize = 64,
            TransformerTeacherStateDimensions = 2048,
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
            TransformerTeacherAcceleratorRefreshInterval = 3,
            TransformerTeacherMinimumFreshFramesForRefresh = 2048,
            TransformerTeacherCpuEpochs = 4,
            TransformerTeacherCpuIncrementalEpochs = 1,
            TransformerTeacherCpuFinalEpochs = 4,
            TransformerTeacherEnableAdaptiveRefresh = true,
            TransformerTeacherAdaptiveRefreshDriftThreshold = 0.15d,
            TransformerTeacherEnableFixedAnchorValidation = true,
            TransformerTeacherMaximumHeadRegression = 0.05d,
            TransformerTeacherIncrementalEpochs = 4,
            TransformerTeacherFinalEpochs = 12,
            TransformerTeacherIncrementalReplayFrames = 1024,
            TransformerTeacherMaximumIncrementalTrainingFrames = 4096,
            TransformerTeacherMaximumObjectTokens = 64,
            TransformerTeacherCpuThreads = 0,
            TransformerTeacherCpuInteropThreads = 0,
            TransformerTeacherMicroBatchSize = 0,
            TransformerTeacherDataLoaderWorkers = 2,
            TransformerTeacherPrefetchBatches = 2,
            TransformerTeacherEnableShardedDataset = true,
            TransformerTeacherDatasetShardFrames = 512,
            TransformerTeacherResidentDatasetMaximumFrames = 4096,
            TransformerTeacherMemoryReserveBytes =
                CombatFoundationParallelismProtocol.DefaultTeacherReserveBytes,
            TransformerTeacherEnablePinnedMemory = true,
            TransformerTeacherEnableMixedPrecision = true,
            TransformerTeacherEnableDeterministicTraining = true,
            TransformerDistillationWeight = 0.15d,
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

    public bool BusinessModelIncluded { get; set; }

    public bool HeavyTrainingPayloadOmitted { get; set; }

    public int OmittedModelPayloads { get; set; }

    public int OmittedHardSeedCheckpoints { get; set; }

    public string EvaluatedModelId { get; set; } = "";

    public int EvaluatedModelIteration { get; set; }

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

    public bool CandidateArtifactProduced { get; set; }

    public string ArtifactBundleDirectory { get; set; } = "";

    public string CapabilityReportPath { get; set; } = "";

    public string SimulationDatabasePath { get; set; } = "";

    public string ModelNodeGraphPath { get; set; } = "";

    public string ArtifactWarning { get; set; } = "";

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

    public bool ExperimentalEligibilityPassed { get; set; }

    public bool RuntimeSafetyPassed { get; set; }

    public bool RawIsolationPassed { get; set; }

    public string DeploymentTier { get; set; } =
        CombatFoundationDeploymentTier.Diagnostic;

    public string DeploymentTierReason { get; set; } = "";

    public bool SameModelEvidenceBound { get; set; }

    public string ValidationModelId { get; set; } = "";

    public string CapabilityProbeModelId { get; set; } = "";

    public string Message { get; set; } = "";

    public bool FormalModelBlocked { get; set; }

    public string FormalModelBlockReason { get; set; } = "";

    public ControllerModelIdentity? Champion { get; set; }

    public ControllerModelIdentity? WorkingChampion { get; set; }

    public ControllerModelIdentity? LatestTrainingModel { get; set; }

    public ControllerPendingArenaCandidateSummary? BestPendingArenaCandidate {
        get;
        set;
    }

    public ControllerModelIdentity? AbsoluteQualifiedBestModel { get; set; }

    public int QualifiedCandidateCount { get; set; }

    public string DecisionDifferencePath { get; set; } = "";

    public int DecisionDifferenceCases { get; set; }

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

internal sealed class ControllerModelIdentity
{
    public string ModelId { get; set; } = "";
}

internal sealed class ControllerPendingArenaCandidateSummary
{
    public int SourceIteration { get; set; }

    public ControllerModelIdentity? Model { get; set; }
}
