using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationTrainingParameters
{
    public string GovernanceProfile { get; set; } =
        CombatFoundationGovernanceProfileNames.Release;

    public ulong RunSeed { get; set; }

    public string DecisionProfile { get; set; } = "balanced";

    public int Iterations { get; set; } = 8;

    public int AdditionalIterationsOnResume { get; set; } = 3;

    public int TrainingCampaignsPerIteration { get; set; } = 64;

    public int ArenaCampaignsPerDifficulty { get; set; } = 32;

    public int ArenaConfirmationCampaignsPerDifficulty { get; set; } = 64;

    public int NormalValidationCampaigns { get; set; } = 200;

    public int AdvancedValidationCampaigns { get; set; } = 500;

    public int CapabilityProbeCampaignsPerDifficulty { get; set; } = 128;

    public int CapabilityProbeTeacherCampaignsPerDifficulty { get; set; } = 128;

    public int CapabilityProbeBatchSize { get; set; } = 16;

    public bool RequireCapabilityProbeBaselineGain { get; set; } = true;

    public int CapabilityProbeMinimumVictoryGain { get; set; } = 1;

    public double CapabilityProbeMinimumDepthGain { get; set; } = 0.5d;

    public int PreflightCampaignsPerDifficulty { get; set; } = 32;

    public int MaximumDegreeOfParallelism { get; set; } =
        Math.Max(1, Math.Min(32, Environment.ProcessorCount));

    public int ModelTrainingParallelism { get; set; } =
        Math.Max(1, Math.Min(16, Environment.ProcessorCount));

    public string ParallelismProfile { get; set; } =
        CombatFoundationExecutionProfileNames.Auto;

    public string InferenceExecutionMode { get; set; } =
        CombatFoundationExecutionProfileNames.DirectInference;

    public int InferenceParallelism { get; set; }

    public int InferenceLaneCount { get; set; }

    public int InferenceBatchSize { get; set; }

    public int ThreadPoolMinimumWorkerThreads { get; set; }

    public int CheckpointSerializationParallelism { get; set; }

    public bool EnableMemoryCapacityParallelism { get; set; } = true;

    public long ParallelismPerLaneBytes { get; set; }

    public long ParallelismMemoryReserveBytes { get; set; }

    public bool ReuseAutoTuneCache { get; set; }

    public int AutoTuneSampleCampaigns { get; set; } = 32;

    public double AutoTuneThroughputTolerance { get; set; } = 0.02d;

    public string AutoTuneObjective { get; set; } =
        CombatFoundationAutoTuneObjectiveNames.MaximumThroughput;

    public bool EnableEarlyValidationStop { get; set; } = true;

    public int ValidationEarlyStopBatchSize { get; set; } = 32;

    public bool EnableCurriculum { get; set; } = true;

    public bool EnableStratifiedReplay { get; set; } = true;

    public bool EnablePrioritizedReplay { get; set; } = true;

    public bool EnableHardSeedCurriculum { get; set; } = true;

    public bool EnableCounterfactualHardEncounters { get; set; } = true;

    public bool EnableSuccessCaseArchive { get; set; } = true;

    public bool EnableArenaRecovery { get; set; } = true;

    public int ArenaInvalidRetryCount { get; set; } = 1;

    public double ArenaInvalidRateLimit { get; set; } = 0.02d;

    public bool EnableTuningArena { get; set; } = true;

    public int TuningNormalCampaigns { get; set; } = 32;

    public int TuningAdvancedCampaigns { get; set; } = 64;

    public bool EnableProgressiveTuning { get; set; } = true;

    public int TuningInterval { get; set; } = 1;

    public bool EnableOfflineTuningGate { get; set; } = true;

    public int TuningScreeningNormalCampaigns { get; set; } = 8;

    public int TuningScreeningAdvancedCampaigns { get; set; } = 16;

    public int TuningFinalistCount { get; set; } = 2;

    public bool EnableSequentialArenaStop { get; set; } = true;

    public int ArenaEvaluationBatchSize { get; set; } = 16;

    public int MaximumConsecutiveRejectedIterations { get; set; } = 3;

    public double NormalAcceptanceRate { get; set; } = 0.80d;

    public double AdvancedAcceptanceRate { get; set; } = 0.30d;

    public int MinimumArenaDiscordantPairs { get; set; } = 8;

    public double MaximumOfflineHeadRegression { get; set; } = 0.05d;

    public double MaximumStateFeatureCollisionRate { get; set; } = 0.20d;

    public double MaximumActionFeatureCollisionRate { get; set; } = 0.06d;

    public double SuccessExpertReplayShare { get; set; } = 0.20d;

    public double AuthoritativeContentReplayShare { get; set; } = 0.20d;

    public double HardSeedReplayShare { get; set; } = 0.35d;

    public Dictionary<string, double> HardEncounterWeights { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public double MinimumAdvancedReplayShare { get; set; } = 0.40d;

    public double MinimumAdvancedDefeatReplayShare { get; set; } = 0.25d;

    public double SelfPlayExplorationProbability { get; set; } = 0.15d;

    public double SelfPlayExplorationTemperature { get; set; } = 1d;

    public int ModelEpochs { get; set; } = 40;

    public int ModelMinimumEpochs { get; set; } = 8;

    public int ModelEarlyStoppingPatience { get; set; } = 8;

    public double ModelEarlyStoppingMinimumDelta { get; set; } = 0.0002d;

    public int ModelBatchSize { get; set; } = 64;

    public int ModelGradientShardCount { get; set; } = 12;

    public bool EnableFrameStratification { get; set; } = true;

    public bool EnableEndTurnSpecialization { get; set; } = true;

    public double ModelEndTurnFrameWeight { get; set; } = 1d;

    public double ModelMaximumUnsafeEndTurnFrameShare { get; set; } = 0.20d;

    public double ModelUnsafeEndTurnRiskAuxiliaryShare { get; set; } = 0.10d;

    public int ModelMinimumValidationRunGroups { get; set; } = 16;

    public int ModelMinimumTestRunGroups { get; set; } = 16;

    public double ModelPolicyTargetTemperature { get; set; } = 1.25d;

    public double ModelMaximumPolicyTargetProbability { get; set; } = 0.90d;

    public double ModelMaximumFrameStratumWeight { get; set; } = 3d;

    public int ModelMaximumFramesPerEpisode { get; set; } = 96;

    public int ModelReplayEpisodeLimit { get; set; } = 8000;

    public int ModelReplayFrameLimit { get; set; } = 384000;

    public long ModelReplayEstimatedBytesLimit { get; set; } =
        3L * 1024L * 1024L * 1024L;

    public int ModelRetainedCandidates { get; set; } = 3;

    public double ModelLearningRate { get; set; } = 0.00625d;

    public double ModelL2 { get; set; } = 0.0015d;

    public int ModelStateDimensions { get; set; } = 1024;

    public int ModelActionDimensions { get; set; } = 1024;

    public int ModelHiddenDimensions { get; set; } = 512;

    public string TransformerTeacherBackend { get; set; } =
        CombatTransformerTeacherBackendNames.Disabled;

    public string TransformerPythonExecutable { get; set; } =
        CombatTransformerRuntimeProtocol.AutomaticExecutable;

    public int TransformerTeacherEpochs { get; set; } = 12;

    public int TransformerTeacherBatchSize { get; set; } = 64;

    public int TransformerTeacherStateDimensions { get; set; } = 1024;

    public int TransformerTeacherActionDimensions { get; set; } = 1024;

    public int TransformerTeacherHiddenDimensions { get; set; } = 384;

    public int TransformerTeacherLayers { get; set; } = 6;

    public int TransformerTeacherAttentionHeads { get; set; } = 8;

    public int TransformerTeacherFeedForwardDimensions { get; set; } = 1536;

    public int TransformerTeacherHistoryLength { get; set; } = 12;

    public int TransformerTeacherMinimumFrames { get; set; } = 1024;

    public int TransformerTeacherMaximumFrames { get; set; } = 10000;

    public bool TransformerTeacherEnableWarmStart { get; set; } = true;

    public int TransformerTeacherCpuRefreshInterval { get; set; } = 4;

    public int TransformerTeacherCpuEpochs { get; set; } = 4;

    public int TransformerTeacherCpuIncrementalEpochs { get; set; } = 1;

    public int TransformerTeacherCpuFinalEpochs { get; set; } = 4;

    public bool TransformerTeacherEnableAdaptiveRefresh { get; set; } = true;

    public double TransformerTeacherAdaptiveRefreshDriftThreshold { get; set; } =
        0.15d;

    public bool TransformerTeacherEnableFixedAnchorValidation { get; set; } =
        true;

    public double TransformerTeacherMaximumHeadRegression { get; set; } =
        0.05d;

    public int TransformerTeacherIncrementalEpochs { get; set; } = 4;

    public int TransformerTeacherFinalEpochs { get; set; } = 12;

    public int TransformerTeacherCpuThreads { get; set; }

    public int TransformerTeacherCpuInteropThreads { get; set; }

    public int TransformerTeacherMicroBatchSize { get; set; }

    public int TransformerTeacherDataLoaderWorkers { get; set; }

    public int TransformerTeacherPrefetchBatches { get; set; } = 2;

    public bool TransformerTeacherEnablePinnedMemory { get; set; } = true;

    public bool TransformerTeacherEnableMixedPrecision { get; set; } = true;

    public double TransformerDistillationWeight { get; set; } = 0.35d;

    public string ModelFeatureEncodingMode { get; set; } = "partitioned-v3";

    public int MinimumEpisodes { get; set; } = 8;

    public ulong TrainingSeedStart { get; set; } = 10_000UL;

    public ulong ArenaSeedStart { get; set; } = 1_000_000UL;

    public ulong TuningSeedStart { get; set; } = 1_500_000UL;

    public ulong ValidationSeedStart { get; set; } = 2_000_000UL;

    public CombatFoundationTrainingParameters Normalized()
    {
        GovernanceProfile = CombatFoundationGovernanceProfiles.Normalize(
            GovernanceProfile);
        Iterations = Math.Max(1, Math.Min(20, Iterations));
        AdditionalIterationsOnResume = Math.Max(
            0,
            Math.Min(20, AdditionalIterationsOnResume));
        TrainingCampaignsPerIteration = Math.Max(
            2,
            Math.Min(1000, TrainingCampaignsPerIteration));
        ArenaCampaignsPerDifficulty = Math.Max(
            1,
            Math.Min(100, ArenaCampaignsPerDifficulty));
        ArenaConfirmationCampaignsPerDifficulty = Math.Max(
            0,
            Math.Min(200, ArenaConfirmationCampaignsPerDifficulty));
        NormalValidationCampaigns = Math.Max(
            10,
            Math.Min(1000, NormalValidationCampaigns));
        AdvancedValidationCampaigns = Math.Max(
            10,
            Math.Min(1000, AdvancedValidationCampaigns));
        CapabilityProbeCampaignsPerDifficulty = Math.Max(
            0,
            Math.Min(128, CapabilityProbeCampaignsPerDifficulty));
        CapabilityProbeTeacherCampaignsPerDifficulty = Math.Max(
            0,
            Math.Min(128, CapabilityProbeTeacherCampaignsPerDifficulty));
        CapabilityProbeBatchSize = Math.Max(
            1,
            Math.Min(128, CapabilityProbeBatchSize));
        CapabilityProbeMinimumVictoryGain = Math.Max(
            1,
            Math.Min(64, CapabilityProbeMinimumVictoryGain));
        CapabilityProbeMinimumDepthGain = Clamp(
            CapabilityProbeMinimumDepthGain,
            0d,
            37d,
            0.5d);
        PreflightCampaignsPerDifficulty = Math.Max(
            1,
            Math.Min(100, PreflightCampaignsPerDifficulty));
        ModelTrainingParallelism = Math.Max(
            1,
            Math.Min(64, ModelTrainingParallelism));
        ParallelismPerLaneBytes = Math.Max(0L, ParallelismPerLaneBytes);
        ParallelismMemoryReserveBytes = Math.Max(
            0L,
            ParallelismMemoryReserveBytes);
        var requestedInferenceParallelism = InferenceParallelism;
        var requestedInferenceLaneCount = InferenceLaneCount;
        var requestedInferenceBatchSize = InferenceBatchSize;
        var requestedThreadPoolMinimumWorkerThreads =
            ThreadPoolMinimumWorkerThreads;
        var requestedCheckpointSerializationParallelism =
            CheckpointSerializationParallelism;
        var execution = CombatFoundationExecutionProfiles.Resolve(
            ParallelismProfile,
            MaximumDegreeOfParallelism,
            InferenceExecutionMode,
            InferenceParallelism,
            ThreadPoolMinimumWorkerThreads,
            CheckpointSerializationParallelism,
            null,
            InferenceLaneCount,
            InferenceBatchSize);
        ParallelismProfile = execution.Profile;
        MaximumDegreeOfParallelism = execution.CampaignParallelism;
        InferenceExecutionMode = execution.InferenceMode;
        var automaticExecution = string.Equals(
            execution.Profile,
            CombatFoundationExecutionProfileNames.Auto,
            StringComparison.Ordinal);
        InferenceParallelism = automaticExecution
                               && requestedInferenceParallelism <= 0
            ? 0
            : execution.InferenceParallelism;
        InferenceLaneCount = automaticExecution
                             && requestedInferenceLaneCount <= 0
            ? 0
            : execution.InferenceLaneCount;
        InferenceBatchSize = automaticExecution
                             && requestedInferenceBatchSize <= 0
            ? 0
            : execution.InferenceBatchSize;
        ThreadPoolMinimumWorkerThreads = automaticExecution
                                         && requestedThreadPoolMinimumWorkerThreads
                                         <= 0
            ? 0
            : execution.ThreadPoolMinimumWorkerThreads;
        CheckpointSerializationParallelism = automaticExecution
                                             && requestedCheckpointSerializationParallelism
                                             <= 0
            ? 0
            : execution.CheckpointSerializationParallelism;
        AutoTuneSampleCampaigns = Math.Max(
            4,
            Math.Min(64, AutoTuneSampleCampaigns));
        AutoTuneObjective = CombatFoundationAutoTuneObjectiveNames.Normalize(
            AutoTuneObjective);
        AutoTuneThroughputTolerance = Clamp(
            AutoTuneThroughputTolerance,
            0d,
            0.20d,
            0.02d);
        ValidationEarlyStopBatchSize = Math.Max(
            1,
            Math.Min(128, ValidationEarlyStopBatchSize));
        ArenaInvalidRetryCount = Math.Max(0, Math.Min(3, ArenaInvalidRetryCount));
        ArenaInvalidRateLimit = Clamp(ArenaInvalidRateLimit, 0.0001d, 1d, 0.02d);
        TuningNormalCampaigns = Math.Max(0, Math.Min(64, TuningNormalCampaigns));
        TuningAdvancedCampaigns = Math.Max(0, Math.Min(64, TuningAdvancedCampaigns));
        TuningInterval = Math.Max(1, Math.Min(8, TuningInterval));
        TuningScreeningNormalCampaigns = Math.Max(
            0,
            Math.Min(TuningNormalCampaigns, TuningScreeningNormalCampaigns));
        TuningScreeningAdvancedCampaigns = Math.Max(
            0,
            Math.Min(TuningAdvancedCampaigns, TuningScreeningAdvancedCampaigns));
        TuningFinalistCount = Math.Max(
            1,
            Math.Min(ModelRetainedCandidates, TuningFinalistCount));
        ArenaEvaluationBatchSize = Math.Max(
            1,
            Math.Min(64, ArenaEvaluationBatchSize));
        MaximumConsecutiveRejectedIterations = Math.Max(
            0,
            Math.Min(8, MaximumConsecutiveRejectedIterations));
        NormalAcceptanceRate = Clamp(NormalAcceptanceRate, 0d, 1d, 0.80d);
        AdvancedAcceptanceRate = Clamp(AdvancedAcceptanceRate, 0d, 1d, 0.30d);
        MinimumArenaDiscordantPairs = Math.Max(
            1,
            Math.Min(128, MinimumArenaDiscordantPairs));
        MaximumOfflineHeadRegression = Clamp(
            MaximumOfflineHeadRegression,
            0d,
            0.50d,
            0.05d);
        MaximumStateFeatureCollisionRate = Clamp(
            MaximumStateFeatureCollisionRate,
            0d,
            1d,
            0.20d);
        MaximumActionFeatureCollisionRate = Clamp(
            MaximumActionFeatureCollisionRate,
            0d,
            1d,
            0.06d);
        SuccessExpertReplayShare = Clamp(
            SuccessExpertReplayShare,
            0d,
            0.40d,
            0.20d);
        AuthoritativeContentReplayShare = Clamp(
            AuthoritativeContentReplayShare,
            0d,
            0.50d,
            0.20d);
        HardSeedReplayShare = Clamp(HardSeedReplayShare, 0d, 0.75d, 0.35d);
        MinimumAdvancedReplayShare = Clamp(
            MinimumAdvancedReplayShare,
            0d,
            0.90d,
            0.40d);
        MinimumAdvancedDefeatReplayShare = Clamp(
            MinimumAdvancedDefeatReplayShare,
            0d,
            MinimumAdvancedReplayShare,
            0.25d);
        HardEncounterWeights ??=
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        SelfPlayExplorationProbability = Clamp(
            SelfPlayExplorationProbability,
            0d,
            0.5d,
            0.15d);
        SelfPlayExplorationTemperature = Clamp(
            SelfPlayExplorationTemperature,
            0.1d,
            5d,
            1d);
        ModelEpochs = Math.Max(5, Math.Min(200, ModelEpochs));
        ModelMinimumEpochs = Math.Max(1, Math.Min(ModelEpochs, ModelMinimumEpochs));
        ModelEarlyStoppingPatience = Math.Max(
            1,
            Math.Min(30, ModelEarlyStoppingPatience));
        ModelEarlyStoppingMinimumDelta = Clamp(
            ModelEarlyStoppingMinimumDelta,
            0.0000001d,
            0.1d,
            0.0002d);
        ModelBatchSize = Math.Max(8, Math.Min(512, ModelBatchSize));
        ModelGradientShardCount = Math.Max(
            0,
            Math.Min(32, ModelGradientShardCount));
        ModelMaximumFrameStratumWeight = Clamp(
            ModelMaximumFrameStratumWeight,
            1d,
            5d,
            3d);
        ModelEndTurnFrameWeight = Clamp(
            ModelEndTurnFrameWeight,
            0.25d,
            1d,
            1d);
        ModelMaximumUnsafeEndTurnFrameShare = Clamp(
            ModelMaximumUnsafeEndTurnFrameShare,
            0.10d,
            0.80d,
            0.20d);
        ModelUnsafeEndTurnRiskAuxiliaryShare = Clamp(
            ModelUnsafeEndTurnRiskAuxiliaryShare,
            0d,
            0.40d,
            0.10d);
        ModelMinimumValidationRunGroups = Math.Max(
            1,
            Math.Min(256, ModelMinimumValidationRunGroups));
        ModelMinimumTestRunGroups = Math.Max(
            1,
            Math.Min(256, ModelMinimumTestRunGroups));
        ModelPolicyTargetTemperature = Clamp(
            ModelPolicyTargetTemperature,
            1d,
            3d,
            1.25d);
        ModelMaximumPolicyTargetProbability = Clamp(
            ModelMaximumPolicyTargetProbability,
            0.55d,
            1d,
            0.90d);
        ModelMaximumFramesPerEpisode = Math.Max(
            8,
            Math.Min(512, ModelMaximumFramesPerEpisode));
        ModelReplayEpisodeLimit = Math.Max(
            64,
            Math.Min(20000, ModelReplayEpisodeLimit));
        ModelReplayFrameLimit = Math.Max(
            4096,
            Math.Min(2_000_000, ModelReplayFrameLimit));
        ModelReplayEstimatedBytesLimit = Math.Max(
            256L * 1024L * 1024L,
            Math.Min(
                16L * 1024L * 1024L * 1024L,
                ModelReplayEstimatedBytesLimit));
        ModelRetainedCandidates = Math.Max(1, Math.Min(5, ModelRetainedCandidates));
        ModelLearningRate = Clamp(ModelLearningRate, 0.0001d, 0.1d, 0.00625d);
        ModelL2 = Clamp(ModelL2, 0d, 0.05d, 0.0015d);
        ModelStateDimensions = Math.Max(16, Math.Min(2048, ModelStateDimensions));
        ModelActionDimensions = Math.Max(16, Math.Min(2048, ModelActionDimensions));
        ModelHiddenDimensions = Math.Max(8, Math.Min(1024, ModelHiddenDimensions));
        var transformer = new CombatTransformerTeacherOptions
        {
            Backend = TransformerTeacherBackend,
            PythonExecutable = TransformerPythonExecutable,
            Epochs = TransformerTeacherEpochs,
            BatchSize = TransformerTeacherBatchSize,
            StateDimensions = TransformerTeacherStateDimensions,
            ActionDimensions = TransformerTeacherActionDimensions,
            HiddenDimensions = TransformerTeacherHiddenDimensions,
            Layers = TransformerTeacherLayers,
            AttentionHeads = TransformerTeacherAttentionHeads,
            FeedForwardDimensions =
                TransformerTeacherFeedForwardDimensions,
            HistoryLength = TransformerTeacherHistoryLength,
            MinimumFrames = TransformerTeacherMinimumFrames,
            MaximumFrames = TransformerTeacherMaximumFrames,
            EnableWarmStart = TransformerTeacherEnableWarmStart,
            CpuRefreshInterval = TransformerTeacherCpuRefreshInterval,
            CpuEpochs = TransformerTeacherCpuEpochs,
            CpuIncrementalEpochs =
                TransformerTeacherCpuIncrementalEpochs,
            CpuFinalEpochs = TransformerTeacherCpuFinalEpochs,
            EnableAdaptiveRefresh =
                TransformerTeacherEnableAdaptiveRefresh,
            AdaptiveRefreshDriftThreshold =
                TransformerTeacherAdaptiveRefreshDriftThreshold,
            EnableFixedAnchorValidation =
                TransformerTeacherEnableFixedAnchorValidation,
            MaximumHeadRegression =
                TransformerTeacherMaximumHeadRegression,
            IncrementalEpochs = TransformerTeacherIncrementalEpochs,
            FinalEpochs = TransformerTeacherFinalEpochs,
            CpuThreads = TransformerTeacherCpuThreads,
            CpuInteropThreads = TransformerTeacherCpuInteropThreads,
            MicroBatchSize = TransformerTeacherMicroBatchSize,
            DataLoaderWorkers = TransformerTeacherDataLoaderWorkers,
            PrefetchBatches = TransformerTeacherPrefetchBatches,
            EnablePinnedMemory = TransformerTeacherEnablePinnedMemory,
            EnableMixedPrecision = TransformerTeacherEnableMixedPrecision,
            DistillationWeight = TransformerDistillationWeight
        }.Normalized();
        TransformerTeacherBackend = transformer.Backend;
        TransformerPythonExecutable = transformer.PythonExecutable;
        TransformerTeacherEpochs = transformer.Epochs;
        TransformerTeacherBatchSize = transformer.BatchSize;
        TransformerTeacherStateDimensions = transformer.StateDimensions;
        TransformerTeacherActionDimensions = transformer.ActionDimensions;
        TransformerTeacherHiddenDimensions = transformer.HiddenDimensions;
        TransformerTeacherLayers = transformer.Layers;
        TransformerTeacherAttentionHeads = transformer.AttentionHeads;
        TransformerTeacherFeedForwardDimensions =
            transformer.FeedForwardDimensions;
        TransformerTeacherHistoryLength = transformer.HistoryLength;
        TransformerTeacherMinimumFrames = transformer.MinimumFrames;
        TransformerTeacherMaximumFrames = transformer.MaximumFrames;
        TransformerTeacherEnableWarmStart = transformer.EnableWarmStart;
        TransformerTeacherCpuRefreshInterval = transformer.CpuRefreshInterval;
        TransformerTeacherCpuEpochs = transformer.CpuEpochs;
        TransformerTeacherCpuIncrementalEpochs =
            transformer.CpuIncrementalEpochs;
        TransformerTeacherCpuFinalEpochs = transformer.CpuFinalEpochs;
        TransformerTeacherEnableAdaptiveRefresh =
            transformer.EnableAdaptiveRefresh;
        TransformerTeacherAdaptiveRefreshDriftThreshold =
            transformer.AdaptiveRefreshDriftThreshold;
        TransformerTeacherEnableFixedAnchorValidation =
            transformer.EnableFixedAnchorValidation;
        TransformerTeacherMaximumHeadRegression =
            transformer.MaximumHeadRegression;
        TransformerTeacherIncrementalEpochs = transformer.IncrementalEpochs;
        TransformerTeacherFinalEpochs = transformer.FinalEpochs;
        TransformerTeacherCpuThreads = transformer.CpuThreads;
        TransformerTeacherCpuInteropThreads = transformer.CpuInteropThreads;
        TransformerTeacherMicroBatchSize = transformer.MicroBatchSize;
        TransformerTeacherDataLoaderWorkers = transformer.DataLoaderWorkers;
        TransformerTeacherPrefetchBatches = transformer.PrefetchBatches;
        TransformerTeacherEnablePinnedMemory = transformer.EnablePinnedMemory;
        TransformerTeacherEnableMixedPrecision = transformer.EnableMixedPrecision;
        TransformerDistillationWeight = transformer.DistillationWeight;
        ModelFeatureEncodingMode = "partitioned-v3";
        MinimumEpisodes = Math.Max(
            2,
            Math.Min(TrainingCampaignsPerIteration, MinimumEpisodes));
        DecisionProfile = NormalizeProfile(DecisionProfile);
        TrainingSeedStart = TrainingSeedStart == 0UL ? 10_000UL : TrainingSeedStart;
        ArenaSeedStart = ArenaSeedStart == 0UL ? 1_000_000UL : ArenaSeedStart;
        TuningSeedStart = TuningSeedStart == 0UL ? 1_500_000UL : TuningSeedStart;
        ValidationSeedStart = ValidationSeedStart == 0UL
            ? 2_000_000UL
            : ValidationSeedStart;
        return this;
    }

    public int EstimatedCampaigns()
    {
        Normalized();
        var governance = CombatFoundationGovernanceProfiles.Resolve(
            GovernanceProfile,
            TuningInterval,
            TuningNormalCampaigns,
            TuningAdvancedCampaigns,
            TuningScreeningNormalCampaigns,
            TuningScreeningAdvancedCampaigns,
            TuningFinalistCount,
            CapabilityProbeTeacherCampaignsPerDifficulty,
            AutoTuneSampleCampaigns);
        var tuningCampaigns = EnableTuningArena
            ? CombatCampaignFoundationTrainer.EstimateTuningCampaigns(
                ModelRetainedCandidates,
                governance.TuningNormalCampaigns,
                governance.TuningAdvancedCampaigns,
                EnableProgressiveTuning,
                governance.TuningScreeningNormalCampaigns,
                governance.TuningScreeningAdvancedCampaigns,
                governance.TuningFinalistCount)
            : 0;
        return Iterations
               * (TrainingCampaignsPerIteration
                  + (ArenaCampaignsPerDifficulty
                     + (ArenaCampaignsPerDifficulty >= 16
                         ? ArenaConfirmationCampaignsPerDifficulty
                         : 0)) * 4)
               + governance.ScheduledTuningIterations(Iterations)
               * tuningCampaigns
               + NormalValidationCampaigns
               + AdvancedValidationCampaigns
               + CapabilityProbeCampaignsPerDifficulty * 2 * 2
               + governance.CapabilityProbeTeacherCampaignsPerDifficulty * 2;
    }

    private static string NormalizeProfile(string value)
    {
        var profile = (value ?? "").Trim().ToLowerInvariant();
        return profile == "aggressive" || profile == "defensive"
            ? profile
            : "balanced";
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Max(minimum, Math.Min(maximum, value));
    }
}

public sealed class CombatFoundationWorkerJobBuildRequest
{
    public string JobId { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public string SuccessArchiveDirectory { get; set; } = "";

    public string CheckpointPath { get; set; } = "";

    public string CheckpointEpisodesPath { get; set; } = "";

    public string ExpectedRulesetHash { get; set; } = "";

    public string NativeProgramPackageHash { get; set; } = "";

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public CombatFoundationTrainingParameters Parameters { get; set; } = new();

    public CombatDecisionProfile Profile { get; set; } = new();

    public CombatCampaignDefinition TrainingCampaign { get; set; } = new();

    public CombatCampaignDefinition ValidationCampaign { get; set; } = new();

    public CombatRulesetDocument Ruleset { get; set; } = new();

    public CombatPolicyValueNetworkDefinition? InitialChampion { get; set; }

    public List<CombatEpisode> AuthoritativeContentEpisodes { get; set; } = new();
}

public static class CombatFoundationWorkerJobFactory
{
    public static CombatFoundationWorkerJob Create(
        CombatFoundationWorkerJobBuildRequest source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var parameters = (source.Parameters ?? new CombatFoundationTrainingParameters())
            .Normalized();
        var resultDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(source.ResultDirectory)
                ? throw new InvalidOperationException("底模训练结果目录为空")
                : source.ResultDirectory);
        var jobId = string.IsNullOrWhiteSpace(source.JobId)
            ? "foundation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")
            : source.JobId.Trim();
        var profile = source.Profile ?? new CombatDecisionProfile();
        profile.Id = parameters.DecisionProfile;
        var expertEpisodeLimit = parameters.EnableSuccessCaseArchive
            ? Math.Min(
                1024,
                (int)Math.Round(
                    parameters.ModelReplayEpisodeLimit
                    * parameters.SuccessExpertReplayShare))
            : 0;
        return new CombatFoundationWorkerJob
        {
            JobId = jobId,
            ExpectedRulesetHash = source.ExpectedRulesetHash ?? "",
            ResultDirectory = resultDirectory,
            ProgressPath = Path.Combine(
                resultDirectory,
                "foundation-worker-progress.json"),
            ResultPath = Path.Combine(
                resultDirectory,
                "foundation-worker-result.json"),
            CancellationPath = Path.Combine(
                resultDirectory,
                "foundation-worker.cancel"),
            CheckpointPath = string.IsNullOrWhiteSpace(source.CheckpointPath)
                ? Path.Combine(
                    resultDirectory,
                    CombatFoundationWorkerProtocol.CheckpointFileName)
                : Path.GetFullPath(source.CheckpointPath),
            CheckpointEpisodesPath =
                string.IsNullOrWhiteSpace(source.CheckpointEpisodesPath)
                    ? Path.Combine(
                        resultDirectory,
                        CombatFoundationWorkerProtocol.CheckpointEpisodesFileName)
                    : Path.GetFullPath(source.CheckpointEpisodesPath),
            SuccessArchiveDirectory = string.IsNullOrWhiteSpace(
                source.SuccessArchiveDirectory)
                ? Path.Combine(resultDirectory, "foundation-success-cases")
                : Path.GetFullPath(source.SuccessArchiveDirectory),
            TrainingMetricsPath = Path.Combine(
                resultDirectory,
                CombatFoundationWorkerProtocol.TrainingMetricsFileName),
            TrainingAnalysisPath = Path.Combine(
                resultDirectory,
                CombatFoundationWorkerProtocol.TrainingAnalysisFileName),
            Request = new CombatCampaignFoundationTrainingRequest
            {
                GovernanceProfile = parameters.GovernanceProfile,
                ContentSetHash = source.ContentSetHash,
                OwnerModSetHash = source.OwnerModSetHash,
                AuthoritativeContentEpisodes = new List<CombatEpisode>(
                    source.AuthoritativeContentEpisodes
                    ?? new List<CombatEpisode>()),
                RunSeed = parameters.RunSeed,
                DecisionProfile = parameters.DecisionProfile,
                Iterations = parameters.Iterations,
                AdditionalIterationsOnResume =
                    parameters.AdditionalIterationsOnResume,
                TrainingCampaignsPerIteration =
                    parameters.TrainingCampaignsPerIteration,
                ArenaCampaignsPerDifficulty =
                    parameters.ArenaCampaignsPerDifficulty,
                ArenaConfirmationCampaignsPerDifficulty =
                    parameters.ArenaConfirmationCampaignsPerDifficulty,
                NormalValidationCampaigns =
                    parameters.NormalValidationCampaigns,
                AdvancedValidationCampaigns =
                    parameters.AdvancedValidationCampaigns,
                CapabilityProbeCampaignsPerDifficulty =
                    parameters.CapabilityProbeCampaignsPerDifficulty,
                CapabilityProbeTeacherCampaignsPerDifficulty =
                    parameters.CapabilityProbeTeacherCampaignsPerDifficulty,
                CapabilityProbeBatchSize = parameters.CapabilityProbeBatchSize,
                RequireCapabilityProbeBaselineGain =
                    parameters.RequireCapabilityProbeBaselineGain,
                CapabilityProbeMinimumVictoryGain =
                    parameters.CapabilityProbeMinimumVictoryGain,
                CapabilityProbeMinimumDepthGain =
                    parameters.CapabilityProbeMinimumDepthGain,
                PreflightCampaignsPerDifficulty =
                    parameters.PreflightCampaignsPerDifficulty,
                PreflightSeedStart = parameters.TrainingSeedStart,
                MaximumDegreeOfParallelism =
                    parameters.MaximumDegreeOfParallelism,
                ModelTrainingParallelism =
                    parameters.ModelTrainingParallelism,
                ParallelismProfile = parameters.ParallelismProfile,
                InferenceExecutionMode = parameters.InferenceExecutionMode,
                InferenceParallelism = parameters.InferenceParallelism,
                InferenceLaneCount = parameters.InferenceLaneCount,
                InferenceBatchSize = parameters.InferenceBatchSize,
                ThreadPoolMinimumWorkerThreads =
                    parameters.ThreadPoolMinimumWorkerThreads,
                CheckpointSerializationParallelism =
                    parameters.CheckpointSerializationParallelism,
                EnableMemoryCapacityParallelism =
                    parameters.EnableMemoryCapacityParallelism,
                ParallelismPerLaneBytes =
                    parameters.ParallelismPerLaneBytes,
                ParallelismMemoryReserveBytes =
                    parameters.ParallelismMemoryReserveBytes,
                ReuseAutoTuneCache = parameters.ReuseAutoTuneCache,
                AutoTuneSampleCampaigns =
                    parameters.AutoTuneSampleCampaigns,
                AutoTuneThroughputTolerance =
                    parameters.AutoTuneThroughputTolerance,
                AutoTuneObjective = parameters.AutoTuneObjective,
                RetainValidationRunDetails = false,
                EnableEarlyValidationStop =
                    parameters.EnableEarlyValidationStop,
                ValidationEarlyStopBatchSize =
                    parameters.ValidationEarlyStopBatchSize,
                EnableCurriculum = parameters.EnableCurriculum,
                EnableStratifiedReplay =
                    parameters.EnableStratifiedReplay,
                EnablePrioritizedReplay =
                    parameters.EnablePrioritizedReplay,
                EnableHardSeedCurriculum =
                    parameters.EnableHardSeedCurriculum,
                EnableCounterfactualHardEncounters =
                    parameters.EnableCounterfactualHardEncounters,
                EnableSuccessCaseArchive =
                    parameters.EnableSuccessCaseArchive,
                EnableArenaRecovery = parameters.EnableArenaRecovery,
                ArenaInvalidRetryCount =
                    parameters.ArenaInvalidRetryCount,
                ArenaInvalidRateLimit =
                    parameters.ArenaInvalidRateLimit,
                EnableTuningArena = parameters.EnableTuningArena,
                TuningNormalCampaigns = parameters.TuningNormalCampaigns,
                TuningAdvancedCampaigns =
                    parameters.TuningAdvancedCampaigns,
                EnableProgressiveTuning =
                    parameters.EnableProgressiveTuning,
                TuningInterval = parameters.TuningInterval,
                EnableOfflineTuningGate = parameters.EnableOfflineTuningGate,
                TuningScreeningNormalCampaigns =
                    parameters.TuningScreeningNormalCampaigns,
                TuningScreeningAdvancedCampaigns =
                    parameters.TuningScreeningAdvancedCampaigns,
                TuningFinalistCount = parameters.TuningFinalistCount,
                EnableSequentialArenaStop =
                    parameters.EnableSequentialArenaStop,
                ArenaEvaluationBatchSize = parameters.ArenaEvaluationBatchSize,
                MaximumConsecutiveRejectedIterations =
                    parameters.MaximumConsecutiveRejectedIterations,
                NormalAcceptanceRate = parameters.NormalAcceptanceRate,
                AdvancedAcceptanceRate =
                    parameters.AdvancedAcceptanceRate,
                MinimumArenaDiscordantPairs =
                    parameters.MinimumArenaDiscordantPairs,
                MaximumOfflineHeadRegression =
                    parameters.MaximumOfflineHeadRegression,
                MaximumStateFeatureCollisionRate =
                    parameters.MaximumStateFeatureCollisionRate,
                MaximumActionFeatureCollisionRate =
                    parameters.MaximumActionFeatureCollisionRate,
                NativeProgramPackageHash =
                    source.NativeProgramPackageHash ?? "",
                ExpertReplayEpisodeLimit = expertEpisodeLimit,
                AuthoritativeContentReplayShare =
                    parameters.AuthoritativeContentReplayShare,
                CaseArchiveLoad = new CombatFoundationCaseArchiveLoadDiagnostics
                {
                    ProtocolVersion = CombatFoundationCaseArchiveProtocol.Version,
                    OwnerRuntime = "deferred to .NET 8 worker",
                    StorageVersion =
                        CombatFoundationCaseArchiveProtocol.StorageVersion,
                    ArchiveExists = Directory.Exists(
                        source.SuccessArchiveDirectory ?? ""),
                    Message = "archive loading deferred to worker"
                },
                HardSeedReplayShare = parameters.HardSeedReplayShare,
                HardEncounterWeights = new Dictionary<string, double>(
                    parameters.HardEncounterWeights,
                    StringComparer.OrdinalIgnoreCase),
                MinimumAdvancedReplayShare =
                    parameters.MinimumAdvancedReplayShare,
                MinimumAdvancedDefeatReplayShare =
                    parameters.MinimumAdvancedDefeatReplayShare,
                SelfPlayExplorationProbability =
                    parameters.SelfPlayExplorationProbability,
                SelfPlayExplorationTemperature =
                    parameters.SelfPlayExplorationTemperature,
                TrainingSeedStart = parameters.TrainingSeedStart,
                ArenaSeedStart = parameters.ArenaSeedStart,
                TuningSeedStart = parameters.TuningSeedStart,
                ValidationSeedStart = parameters.ValidationSeedStart,
                Profile = profile,
                TrainingCampaign = source.TrainingCampaign
                                   ?? throw new InvalidOperationException(
                                       "底模训练战役为空"),
                ValidationCampaign = source.ValidationCampaign
                                     ?? throw new InvalidOperationException(
                                         "底模验证战役为空"),
                Training = new CombatPolicyValueTrainingOptions
                {
                    Epochs = parameters.ModelEpochs,
                    LearningRate = parameters.ModelLearningRate,
                    L2 = parameters.ModelL2,
                    StateDimensions = parameters.ModelStateDimensions,
                    ActionDimensions = parameters.ModelActionDimensions,
                    HiddenDimensions = parameters.ModelHiddenDimensions,
                    FeatureEncodingMode =
                        parameters.ModelFeatureEncodingMode,
                    BatchSize = parameters.ModelBatchSize,
                    GradientShardCount =
                        parameters.ModelGradientShardCount,
                    EnableFrameStratification =
                        parameters.EnableFrameStratification,
                    EnableEndTurnSpecialization =
                        parameters.EnableEndTurnSpecialization,
                    EndTurnFrameWeight =
                        parameters.ModelEndTurnFrameWeight,
                    MaximumUnsafeEndTurnFrameShare =
                        parameters.ModelMaximumUnsafeEndTurnFrameShare,
                    UnsafeEndTurnRiskAuxiliaryShare =
                        parameters.ModelUnsafeEndTurnRiskAuxiliaryShare,
                    MinimumValidationRunGroups =
                        parameters.ModelMinimumValidationRunGroups,
                    MinimumTestRunGroups =
                        parameters.ModelMinimumTestRunGroups,
                    PolicyTargetTemperature =
                        parameters.ModelPolicyTargetTemperature,
                    MaximumPolicyTargetProbability =
                        parameters.ModelMaximumPolicyTargetProbability,
                    MaximumFrameStratumWeight =
                        parameters.ModelMaximumFrameStratumWeight,
                    MaximumFramesPerEpisode =
                        parameters.ModelMaximumFramesPerEpisode,
                    MaximumDegreeOfParallelism =
                        parameters.ModelTrainingParallelism,
                    MinimumEpochs = parameters.ModelMinimumEpochs,
                    EarlyStoppingPatience =
                        parameters.ModelEarlyStoppingPatience,
                    EarlyStoppingMinimumDelta =
                        parameters.ModelEarlyStoppingMinimumDelta,
                    ReplayEpisodeLimit =
                        parameters.ModelReplayEpisodeLimit,
                    ReplayFrameLimit =
                        parameters.ModelReplayFrameLimit,
                    ReplayEstimatedBytesLimit =
                        parameters.ModelReplayEstimatedBytesLimit,
                    RetainedModelCandidates =
                        parameters.ModelRetainedCandidates,
                    MinimumEpisodes = parameters.MinimumEpisodes
                }.Normalized(),
                TransformerTeacher = new CombatTransformerTeacherOptions
                {
                    Backend = parameters.TransformerTeacherBackend,
                    PythonExecutable = parameters.TransformerPythonExecutable,
                    Epochs = parameters.TransformerTeacherEpochs,
                    BatchSize = parameters.TransformerTeacherBatchSize,
                    StateDimensions =
                        parameters.TransformerTeacherStateDimensions,
                    ActionDimensions =
                        parameters.TransformerTeacherActionDimensions,
                    HiddenDimensions =
                        parameters.TransformerTeacherHiddenDimensions,
                    Layers = parameters.TransformerTeacherLayers,
                    AttentionHeads =
                        parameters.TransformerTeacherAttentionHeads,
                    FeedForwardDimensions =
                        parameters.TransformerTeacherFeedForwardDimensions,
                    HistoryLength =
                        parameters.TransformerTeacherHistoryLength,
                    MinimumFrames =
                        parameters.TransformerTeacherMinimumFrames,
                    MaximumFrames =
                        parameters.TransformerTeacherMaximumFrames,
                    EnableWarmStart =
                        parameters.TransformerTeacherEnableWarmStart,
                    CpuRefreshInterval =
                        parameters.TransformerTeacherCpuRefreshInterval,
                    CpuEpochs = parameters.TransformerTeacherCpuEpochs,
                    CpuIncrementalEpochs =
                        parameters.TransformerTeacherCpuIncrementalEpochs,
                    CpuFinalEpochs =
                        parameters.TransformerTeacherCpuFinalEpochs,
                    EnableAdaptiveRefresh =
                        parameters.TransformerTeacherEnableAdaptiveRefresh,
                    AdaptiveRefreshDriftThreshold = parameters
                        .TransformerTeacherAdaptiveRefreshDriftThreshold,
                    EnableFixedAnchorValidation = parameters
                        .TransformerTeacherEnableFixedAnchorValidation,
                    MaximumHeadRegression = parameters
                        .TransformerTeacherMaximumHeadRegression,
                    IncrementalEpochs =
                        parameters.TransformerTeacherIncrementalEpochs,
                    FinalEpochs =
                        parameters.TransformerTeacherFinalEpochs,
                    CpuThreads = parameters.TransformerTeacherCpuThreads,
                    CpuInteropThreads =
                        parameters.TransformerTeacherCpuInteropThreads,
                    MicroBatchSize =
                        parameters.TransformerTeacherMicroBatchSize,
                    DataLoaderWorkers =
                        parameters.TransformerTeacherDataLoaderWorkers,
                    PrefetchBatches =
                        parameters.TransformerTeacherPrefetchBatches,
                    EnablePinnedMemory =
                        parameters.TransformerTeacherEnablePinnedMemory,
                    EnableMixedPrecision =
                        parameters.TransformerTeacherEnableMixedPrecision,
                    DistillationWeight =
                        parameters.TransformerDistillationWeight,
                    RandomSeed = unchecked((int)parameters.RunSeed)
                }.Normalized()
            },
            Ruleset = source.Ruleset
                      ?? throw new InvalidOperationException("底模规则集为空"),
            InitialChampion = source.InitialChampion
        };
    }
}

public static class CombatFoundationModelPackageProtocol
{
    public const int SchemaVersion = 4;

    public const int LegacySchemaVersion = 3;

    public const long SoftMaximumUncompressedBytes = 45_000_000L;

    public const long MaximumUncompressedBytes = 50_000_000L;

    public static bool TryValidateSerializedSize(
        long bytes,
        out string diagnostic)
    {
        if (bytes <= 0L)
        {
            diagnostic = "底模包为空";
            return false;
        }
        if (bytes > MaximumUncompressedBytes)
        {
            diagnostic = "底模包超过 50 MB 硬上限："
                         + bytes
                         + " > "
                         + MaximumUncompressedBytes;
            return false;
        }
        diagnostic = bytes > SoftMaximumUncompressedBytes
            ? "底模包已超过 45 MB 预警线：" + bytes
            : "";
        return true;
    }

    public const string ArtifactKind = "aura.foundation-model-package";

    public const string FileName = "foundation-model-package-v4.json";

    public const string CurrentModelVersion = "4.0.0";

    public const string LegacyModelVersion = "3.0.0";

    public static CombatFoundationModelPackage Create(
        CombatFoundationWorkerJob job,
        CombatFoundationWorkerResult result,
        string workerSha256)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));
        if (result == null) throw new ArgumentNullException(nameof(result));
        var training = result.Training
                       ?? throw new InvalidOperationException(
                           "Worker 结果缺少底模训练结果");
        if (!result.Success
            || !training.Success
            || !training.AcceptancePassed
            || !training.Validation.Passed
            || !training.Validation.BehaviorPassed
            || training.Validation.SevereEndTurnMistakes != 0
            || training.Validation.DominatedEndTurns != 0
            || training.Validation.EndTurnsIntoAvoidableLethal != 0
            || training.Validation.EndTurnsWithCertifiedCycle != 0
            || training.Validation.AvoidableEndTurnsWithUnusedEnergy != 0
            || training.Validation.NoEffectActionAttempts != 0
            || training.Validation.RepeatedNoEffectActionAttempts != 0
            || training.Validation.GuaranteedNoEffectActionAttempts != 0
            || training.Validation.InteractiveActionContractFailures != 0
            || training.Champion == null
            || !string.Equals(
                result.CompletionKind,
                "training-accepted",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "只有通过正式隔离验收的 Worker 结果才能导出底模包");
        }
        var model = training.Champion;
        var campaign = job.Request.TrainingCampaign;
        var player = campaign.Player ?? new CombatPlayerSetup();
        var trainingSubject =
            CombatFoundationModelCoverageProtocol.CreateTrainingSubject(
                campaign);
        var declaredCoverage =
            CombatFoundationModelCoverageProtocol.CreateDeclaredCoverage(
                campaign,
                job.Ruleset ?? new CombatRulesetDocument(),
                trainingSubject);
        var cardPoolScope = BuildCardPoolScope(
            player.PartnerId,
            campaign.EnabledRewardCardPackIds,
            campaign.TargetDeckSizeMinimum,
            campaign.TargetDeckSizeMaximum);
        return new CombatFoundationModelPackage
        {
            PackageId = "foundation-package-"
                        + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")
                        + "-"
                        + Guid.NewGuid().ToString("N").Substring(0, 8),
            DisplayName = player.RoleId
                          + " + "
                          + player.PartnerId
                          + " 外部底模 "
                          + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ModelVersion = CurrentModelVersion,
            Profile = model.DecisionProfile,
            RoleId = player.RoleId ?? "",
            PartnerId = player.PartnerId ?? "",
            GameParameterPresetId = player.GameParameterPresetId ?? "",
            GameParameterHash = player.GameParameterHash ?? "",
            EnabledRewardCardPackIds =
                campaign.EnabledRewardCardPackIds
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            StartingDeckHash = HashIds(player.Deck, preserveOrder: true),
            PreferredDeckSizeMinimum = campaign.TargetDeckSizeMinimum,
            PreferredDeckSizeMaximum = campaign.TargetDeckSizeMaximum,
            CardPoolScope = cardPoolScope,
            JobId = job.JobId,
            CompletionKind = result.CompletionKind,
            WorkerSha256 = workerSha256 ?? "",
            RulesetHash = result.RulesetHash,
            ContentSetHash = job.Request.ContentSetHash,
            OwnerModSetHash = job.Request.OwnerModSetHash,
            Compatibility = training.Compatibility,
            Validation = training.Validation,
            Acceptance = CreateAcceptance(training),
            TrainingSubject = trainingSubject,
            DeclaredCoverage = declaredCoverage,
            Model = model
        };
    }

    public static bool TryValidate(
        CombatFoundationModelPackage? package,
        out string diagnostic)
    {
        if (package == null)
        {
            diagnostic = "底模包为空";
            return false;
        }
        var legacy = package.SchemaVersion == LegacySchemaVersion;
        if ((!legacy && package.SchemaVersion != SchemaVersion)
            || !string.Equals(
                package.ArtifactKind,
                ArtifactKind,
                StringComparison.Ordinal))
        {
            diagnostic = "底模包协议不兼容";
            return false;
        }
        if (string.IsNullOrWhiteSpace(package.PackageId)
            || string.IsNullOrWhiteSpace(package.JobId)
            || !string.Equals(
                package.ModelVersion,
                legacy ? LegacyModelVersion : CurrentModelVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                package.CompletionKind,
                "training-accepted",
                StringComparison.Ordinal))
        {
            diagnostic = "底模包缺少已验收训练来源";
            return false;
        }
        if (!legacy && !ValidAcceptance(package.Acceptance))
        {
            diagnostic = "底模包缺少有效的配对证据验收证明";
            return false;
        }
        if (string.IsNullOrWhiteSpace(package.RoleId)
            || string.IsNullOrWhiteSpace(package.PartnerId)
            || string.IsNullOrWhiteSpace(package.GameParameterPresetId)
            || string.IsNullOrWhiteSpace(package.GameParameterHash)
            || string.IsNullOrWhiteSpace(package.StartingDeckHash)
            || package.PreferredDeckSizeMinimum < 1
            || package.PreferredDeckSizeMaximum
               < package.PreferredDeckSizeMinimum
            || package.EnabledRewardCardPackIds == null
            || !package.EnabledRewardCardPackIds.Contains(
                "cardpack_1",
                StringComparer.OrdinalIgnoreCase)
            || !package.EnabledRewardCardPackIds.Contains(
                "cardpack_2",
                StringComparer.OrdinalIgnoreCase)
            || !string.Equals(
                package.CardPoolScope,
                BuildCardPoolScope(
                    package.PartnerId,
                    package.EnabledRewardCardPackIds,
                    package.PreferredDeckSizeMinimum,
                    package.PreferredDeckSizeMaximum),
                StringComparison.Ordinal))
        {
            diagnostic = "底模包缺少有效的角色、使魔、奖励卡包或卡组倾向作用域";
            return false;
        }
        if (package.Validation == null
            || !package.Validation.Passed
            || !package.Validation.BehaviorPassed
            || package.Validation.SevereEndTurnMistakes != 0
            || package.Validation.DominatedEndTurns != 0
            || package.Validation.EndTurnsIntoAvoidableLethal != 0
            || package.Validation.EndTurnsWithCertifiedCycle != 0
            || package.Validation.AvoidableEndTurnsWithUnusedEnergy != 0
            || package.Validation.NoEffectActionAttempts != 0
            || package.Validation.RepeatedNoEffectActionAttempts != 0
            || package.Validation.GuaranteedNoEffectActionAttempts != 0
            || package.Validation.InteractiveActionContractFailures != 0
            || package.Validation.InvalidCampaigns != 0)
        {
            diagnostic = "底模包没有通过正式隔离验证";
            return false;
        }
        if (package.Model == null)
        {
            diagnostic = "底模网络为空";
            return false;
        }
        if (!CombatPolicyValueNetworkValidator.TryValidate(
                package.Model,
                out diagnostic))
        {
            diagnostic = "底模网络无效：" + diagnostic;
            return false;
        }
        if (!string.Equals(
                NormalizeProfile(package.Profile),
                NormalizeProfile(package.Model.DecisionProfile),
                StringComparison.Ordinal))
        {
            diagnostic = "底模包风格与模型风格不一致";
            return false;
        }
        if (package.Compatibility == null
            || package.Compatibility.FeatureSchemaVersion
               != package.Model.FeatureSchemaVersion
            || package.Compatibility.FeatureSchemaVersion
               != CombatPolicyValueProtocol.FeatureSchemaVersion
            || !string.Equals(
                package.Compatibility.FeatureEncodingMode,
                package.Model.FeatureEncodingMode,
                StringComparison.Ordinal)
            || !string.Equals(
                package.Compatibility.TrainingSemanticsVersion,
                CombatPolicyValueProtocol.TrainingSemanticsVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                package.Compatibility.ActionContractVersion,
                CombatActionContractProtocol.Version,
                StringComparison.Ordinal)
            || !string.Equals(
                package.Compatibility.SearchPolicyVersion,
                CombatFoundationTrainingProtocol.SearchPolicyVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                package.Compatibility.TrainingPolicyVersion,
                CombatFoundationTrainingProtocol.TrainingPolicyVersion,
                StringComparison.Ordinal)
            || package.Compatibility.StateDimensions
               != package.Model.StateDimensions
            || package.Compatibility.ActionDimensions
               != package.Model.ActionDimensions
            || package.Compatibility.HiddenDimensions
               != package.Model.HiddenDimensions)
        {
            diagnostic =
                "底模包兼容清单与当前特征、搜索、动作契约或模型维度不一致";
            return false;
        }
        if (string.IsNullOrWhiteSpace(package.RulesetHash)
            || !string.Equals(
                package.RulesetHash,
                package.Compatibility.RulesetHash,
                StringComparison.Ordinal))
        {
            diagnostic = "底模包规则集哈希不一致";
            return false;
        }
        if (!ValidHash(package.ContentSetHash)
            || !ValidHash(package.OwnerModSetHash)
            || !string.Equals(
                package.ContentSetHash,
                package.Compatibility.ContentSetHash,
                StringComparison.Ordinal)
            || !string.Equals(
                package.OwnerModSetHash,
                package.Compatibility.OwnerModSetHash,
                StringComparison.Ordinal))
        {
            diagnostic = "底模包内容集合绑定缺失或不一致";
            return false;
        }
        if (package.TrainingSubject != null)
        {
            var subject = CombatFoundationModelCoverageProtocol.Normalize(
                package.TrainingSubject);
            if (!string.Equals(
                    subject.RoleId,
                    package.RoleId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    subject.PartnerId,
                    package.PartnerId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    subject.GameParameterPresetId,
                    package.GameParameterPresetId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    subject.GameParameterHash,
                    package.GameParameterHash,
                    StringComparison.Ordinal)
                || subject.PreferredDeckSizeMinimum
                   != package.PreferredDeckSizeMinimum
                || subject.PreferredDeckSizeMaximum
                   != package.PreferredDeckSizeMaximum
                || !SameIds(
                    subject.EnabledRewardCardPackIds,
                    package.EnabledRewardCardPackIds))
            {
                diagnostic = "底模包训练主体元数据与兼容字段不一致";
                return false;
            }
        }
        if (package.DeclaredCoverage != null
            && package.DeclaredCoverage.SchemaVersion != 1)
        {
            diagnostic = "底模包训练覆盖清单协议不兼容";
            return false;
        }
        diagnostic = "";
        return true;
    }

    public static CombatFoundationModelAcceptance NormalizeAcceptance(
        CombatFoundationModelPackage package)
    {
        if (package.Acceptance != null
            && package.Acceptance.SchemaVersion == 1)
        {
            return package.Acceptance;
        }
        return new CombatFoundationModelAcceptance
        {
            Classification = "legacy-formal-acceptance",
            PromotionProtocolVersion = "legacy-v3",
            FormalIsolationPassed = package.Validation?.Passed == true
        };
    }

    public static bool IsValidAcceptance(
        CombatFoundationModelAcceptance? acceptance)
    {
        return ValidAcceptance(acceptance);
    }

    private static CombatFoundationModelAcceptance CreateAcceptance(
        CombatCampaignFoundationTrainingResult training)
    {
        var evidence = training.Iterations.LastOrDefault(item =>
                           item.ProvisionalChampionSelected || item.Promoted)
                       ?? training.Iterations.LastOrDefault();
        return new CombatFoundationModelAcceptance
        {
            Classification = string.IsNullOrWhiteSpace(training.AcceptanceKind)
                ? "retained-champion"
                : training.AcceptanceKind,
            PromotionProtocolVersion = evidence?.PromotionProtocolVersion
                                       ?? CombatFoundationPromotionProtocol.Version,
            SourceIteration = evidence?.Iteration ?? 0,
            SignificantImprovement = evidence?.Promoted == true,
            EquivalentNonInferior = evidence?.NonInferiorityGatePassed == true,
            ProvisionalChampionSelected =
                evidence?.ProvisionalChampionSelected == true,
            ValidPairedCampaigns = evidence?.ValidArenaPairs ?? 0,
            ValidNormalPairs = evidence?.ValidNormalArenaPairs ?? 0,
            ValidAdvancedPairs = evidence?.ValidAdvancedArenaPairs ?? 0,
            CandidateOnlyWins = evidence?.CandidateOnlyWins ?? 0,
            ChampionOnlyWins = evidence?.ChampionOnlyWins ?? 0,
            PairedRegressionWilsonUpperBound =
                evidence?.PairedRegressionWilsonUpperBound ?? 0d,
            FormalIsolationPassed = training.Validation.Passed
        };
    }

    private static bool ValidAcceptance(
        CombatFoundationModelAcceptance? acceptance)
    {
        if (acceptance == null
            || acceptance.SchemaVersion != 1
            || !acceptance.FormalIsolationPassed
            || string.IsNullOrWhiteSpace(acceptance.Classification)
            || string.IsNullOrWhiteSpace(
                acceptance.PromotionProtocolVersion))
        {
            return false;
        }
        if (string.Equals(
                acceptance.Classification,
                CombatFoundationPromotionProtocol.SignificantImprovement,
                StringComparison.Ordinal))
        {
            return acceptance.SignificantImprovement;
        }
        if (string.Equals(
                acceptance.Classification,
                CombatFoundationPromotionProtocol.EquivalentNonInferior,
                StringComparison.Ordinal))
        {
            return acceptance.EquivalentNonInferior
                   && acceptance.ValidNormalPairs
                      >= CombatFoundationPromotionProtocol
                          .MinimumNonInferiorityPairsPerDifficulty
                   && acceptance.ValidAdvancedPairs
                      >= CombatFoundationPromotionProtocol
                          .MinimumNonInferiorityPairsPerDifficulty
                   && acceptance.CandidateOnlyWins
                      >= acceptance.ChampionOnlyWins
                   && acceptance.PairedRegressionWilsonUpperBound
                      <= CombatFoundationPromotionProtocol
                             .MaximumPairedRegressionWilsonUpperBound
                         + 0.0000001d;
        }
        return string.Equals(
            acceptance.Classification,
            "retained-champion",
            StringComparison.Ordinal);
    }

    public static string BuildCardPoolScope(
        string partnerId,
        IEnumerable<string>? enabledRewardCardPackIds,
        int preferredDeckSizeMinimum,
        int preferredDeckSizeMaximum)
    {
        var canonical = "partner="
                        + (partnerId ?? "").Trim().ToLowerInvariant()
                        + ";packs="
                        + string.Join(
                            ",",
                            (enabledRewardCardPackIds ?? Array.Empty<string>())
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Select(item => item.Trim().ToLowerInvariant())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(item => item, StringComparer.Ordinal))
                        + ";deck="
                        + preferredDeckSizeMinimum
                        + "-"
                        + preferredDeckSizeMaximum;
        return "witch.reward-scope.v2:" + HashText(canonical).Substring(0, 24);
    }

    private static string HashIds(
        IEnumerable<string>? values,
        bool preserveOrder)
    {
        var source = (values ?? Array.Empty<string>())
            .Select(item => (item ?? "").Trim());
        if (!preserveOrder)
        {
            source = source.OrderBy(item => item, StringComparer.Ordinal);
        }
        return HashText(string.Join("\n", source));
    }

    private static string HashText(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")))
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static string NormalizeProfile(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized == "aggressive" || normalized == "defensive"
            ? normalized
            : "balanced";
    }

    private static bool ValidHash(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Length == 64
               && value.All(character =>
                   character >= '0' && character <= '9'
                   || character >= 'a' && character <= 'f');
    }

    private static bool SameIds(
        IEnumerable<string>? left,
        IEnumerable<string>? right)
    {
        return new HashSet<string>(
                   left ?? Array.Empty<string>(),
                   StringComparer.OrdinalIgnoreCase)
               .SetEquals(right ?? Array.Empty<string>());
    }
}

public sealed class CombatFoundationModelAcceptance
{
    public int SchemaVersion { get; set; } = 1;

    public string Classification { get; set; } = "";

    public string PromotionProtocolVersion { get; set; } = "";

    public int SourceIteration { get; set; }

    public bool SignificantImprovement { get; set; }

    public bool EquivalentNonInferior { get; set; }

    public bool ProvisionalChampionSelected { get; set; }

    public int ValidPairedCampaigns { get; set; }

    public int ValidNormalPairs { get; set; }

    public int ValidAdvancedPairs { get; set; }

    public int CandidateOnlyWins { get; set; }

    public int ChampionOnlyWins { get; set; }

    public double PairedRegressionWilsonUpperBound { get; set; }

    public bool FormalIsolationPassed { get; set; }
}

public sealed class CombatFoundationModelPackage
{
    public int SchemaVersion { get; set; } =
        CombatFoundationModelPackageProtocol.SchemaVersion;

    public string ArtifactKind { get; set; } =
        CombatFoundationModelPackageProtocol.ArtifactKind;

    public string PackageId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string ModelVersion { get; set; } = "";

    public string ModelPurpose { get; set; } = "foundation";

    public string Profile { get; set; } = "balanced";

    public string RoleId { get; set; } = "";

    public string PartnerId { get; set; } = "";

    public string GameParameterPresetId { get; set; } = "";

    public string GameParameterHash { get; set; } = "";

    public List<string> EnabledRewardCardPackIds { get; set; } = new();

    public string StartingDeckHash { get; set; } = "";

    public int PreferredDeckSizeMinimum { get; set; }

    public int PreferredDeckSizeMaximum { get; set; }

    public string CardPoolScope { get; set; } = "";

    public string JobId { get; set; } = "";

    public string CompletionKind { get; set; } = "";

    public string WorkerSha256 { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public CombatFoundationCompatibilityManifest Compatibility { get; set; } =
        new();

    public CombatCampaignFoundationValidation Validation { get; set; } = new();

    public CombatFoundationModelAcceptance? Acceptance { get; set; }

    public CombatFoundationTrainingSubject? TrainingSubject { get; set; }

    public CombatFoundationDeclaredCoverage? DeclaredCoverage { get; set; }

    public CombatPolicyValueNetworkDefinition? Model { get; set; }
}
