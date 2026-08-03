using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public static class AutoBattleFoundationExecutionProfileNames
{
    public const string Auto = "auto";
    public const string Cpu16 = "cpu-16";
    public const string Cpu32 = "cpu-32";
    public const string Custom = "custom";
    public const string DirectInference = "direct";
    public const string ShardedBatchInference = "sharded-batch";
}

public sealed class AutoBattleSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("startActive")]
    public bool StartActive { get; set; }

    [JsonProperty("profile")]
    public string Profile { get; set; } = "balanced";

    [JsonProperty("decisionIntervalMs")]
    public int DecisionIntervalMs { get; set; } = 350;

    [JsonProperty("actionTimeoutSeconds")]
    public float ActionTimeoutSeconds { get; set; } = 12f;

    [JsonProperty("unknownActionPolicy")]
    public string UnknownActionPolicy { get; set; } = "conservative";

    [JsonProperty("captureTrainingSamples")]
    public bool CaptureTrainingSamples { get; set; }

    [JsonProperty("trainingMode")]
    public string TrainingMode { get; set; } = "hybrid";

    [JsonProperty("trainedModelMode")]
    public string TrainedModelMode { get; set; } = "off";

    [JsonProperty("selectedModelId")]
    public string SelectedModelId { get; set; } = "";

    [JsonProperty("evaluationModelId")]
    public string EvaluationModelId { get; set; } = "";

    [JsonProperty("showPredictionMarkers")]
    public bool ShowPredictionMarkers { get; set; } = true;

    [JsonProperty("searchQuality")]
    public string SearchQuality { get; set; } = "balanced";

    [JsonProperty("decisionTimeBudgetMs")]
    public int DecisionTimeBudgetMs { get; set; } = 250;

    [JsonProperty("inferenceParallelism")]
    public int InferenceParallelism { get; set; } = 2;

    [JsonProperty("lowConfidenceFallback")]
    public bool LowConfidenceFallback { get; set; } = true;

    [JsonProperty("minimumSearchConfidence")]
    public double MinimumSearchConfidence { get; set; } = 0.35d;

    [JsonProperty("gameParameters")]
    public AutoBattleGameParameterSettings GameParameters { get; set; } = new();

    [JsonProperty("training")]
    public AutoBattleTrainingSettings Training { get; set; } = AutoBattleTrainingSettings.CreateSteady();

    [JsonProperty("foundationTraining")]
    public AutoBattleFoundationTrainingSettings FoundationTraining { get; set; } = new();

    [JsonProperty("simulation")]
    public AutoBattleSimulationSettings Simulation { get; set; } = new();

    [JsonProperty("gameValidation")]
    public AutoBattleGameValidationSettings GameValidation { get; set; } = new();

    public void Normalize()
    {
        GameParameters ??= new AutoBattleGameParameterSettings();
        Training ??= AutoBattleTrainingSettings.CreateSteady();

        Profile = NormalizeChoice(Profile, "balanced", "aggressive", "defensive");
        UnknownActionPolicy = NormalizeChoice(
            UnknownActionPolicy,
            "conservative",
            "allow",
            "handoff");
        TrainingMode = NormalizeChoice(TrainingMode, "auto", "shadow", "hybrid");
        TrainedModelMode = NormalizeChoice(TrainedModelMode, "off", "shadow", "active");
        SelectedModelId = SelectedModelId?.Trim() ?? "";
        EvaluationModelId = EvaluationModelId?.Trim() ?? "";
        DecisionIntervalMs = Math.Max(150, Math.Min(2000, DecisionIntervalMs));
        ActionTimeoutSeconds = Math.Max(3f, Math.Min(60f, ActionTimeoutSeconds));
        SearchQuality = NormalizeChoice(
            SearchQuality,
            "balanced",
            "fast",
            "deep");
        DecisionTimeBudgetMs = Math.Max(
            50,
            Math.Min(1000, DecisionTimeBudgetMs));
        InferenceParallelism = Math.Max(1, Math.Min(2, InferenceParallelism));
        MinimumSearchConfidence = Math.Max(
            0.1d,
            Math.Min(0.8d, MinimumSearchConfidence));
        GameParameters.Normalize();
        Training.Normalize();
        FoundationTraining ??= new AutoBattleFoundationTrainingSettings();
        FoundationTraining.Normalize();
        Simulation ??= new AutoBattleSimulationSettings();
        Simulation.Normalize();
        GameValidation ??= new AutoBattleGameValidationSettings();
        GameValidation.Normalize();
    }

    private static string NormalizeChoice(string value, params string[] choices)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        for (var i = 0; i < choices.Length; i++)
        {
            if (string.Equals(normalized, choices[i], StringComparison.Ordinal))
            {
                return normalized;
            }
        }

        return choices[0];
    }
}

public sealed class AutoBattleGameParameterSettings
{
    [JsonProperty("selectedPresetId")]
    public string SelectedPresetId { get; set; } = "standard";

    [JsonProperty("presets")]
    public List<AutoBattleGameParameterPreset> Presets { get; set; } =
        new() { AutoBattleGameParameterPreset.CreateDefault() };

    [JsonIgnore]
    public AutoBattleGameParameterPreset ActivePreset =>
        Presets.FirstOrDefault(item => string.Equals(
            item.Id,
            SelectedPresetId,
            StringComparison.OrdinalIgnoreCase))
        ?? Presets[0];

    public void Normalize()
    {
        Presets ??= new List<AutoBattleGameParameterPreset>();
        Presets = Presets
            .Where(item => item != null)
            .ToList();
        if (Presets.Count == 0)
        {
            Presets.Add(AutoBattleGameParameterPreset.CreateDefault());
        }

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Presets.Count; index++)
        {
            var preset = Presets[index];
            preset.Normalize(index);
            var baseId = preset.Id;
            var suffix = 2;
            while (!usedIds.Add(preset.Id))
            {
                preset.Id = baseId + "-" + suffix++;
            }
        }

        SelectedPresetId = (SelectedPresetId ?? "").Trim();
        if (!Presets.Any(item => string.Equals(
                item.Id,
                SelectedPresetId,
                StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPresetId = Presets[0].Id;
        }
    }
}

public sealed class AutoBattleGameParameterPreset
{
    private static readonly string[] DefaultRewardCardPacks =
    {
        "cardpack_1",
        "cardpack_2",
        "cardpack_3",
        "cardpack_4",
        "cardpack_5",
        "cardpack_6",
        "cardpack_7",
        "cardpack_8",
        "cardpack_9",
        "cardpack_10",
        "cardpack_11",
        "cardpack_12",
        "cardpack_14",
        "cardpack_15",
        "cardpack_16",
        "cardpack_17",
        "cardpack_18",
        "cardpack_19"
    };

    [JsonProperty("id")]
    public string Id { get; set; } = "standard";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "标准预设";

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "career_1";

    [JsonProperty("partnerId")]
    public string PartnerId { get; set; } = "Partner_10001";

    [JsonProperty("enabledRewardCardPackIds")]
    public List<string> EnabledRewardCardPackIds { get; set; } =
        DefaultRewardCardPacks.ToList();

    [JsonProperty("preferredDeckSizeMinimum")]
    public int PreferredDeckSizeMinimum { get; set; } = 15;

    [JsonProperty("preferredDeckSizeMaximum")]
    public int PreferredDeckSizeMaximum { get; set; } = 24;

    // Resolved on the Unity thread before a background training snapshot starts.
    [JsonProperty("resolvedRoleSkillIds")]
    public List<string> ResolvedRoleSkillIds { get; set; } = new();

    [JsonProperty("resolvedRoleInitialStatuses")]
    public Dictionary<string, int> ResolvedRoleInitialStatuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("resolvedRoleSkillCooldownTurns")]
    public Dictionary<string, int> ResolvedRoleSkillCooldownTurns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("resolvedFamiliarBlessingIds")]
    public List<string> ResolvedFamiliarBlessingIds { get; set; } = new();

    public static AutoBattleGameParameterPreset CreateDefault()
    {
        return new AutoBattleGameParameterPreset();
    }

    public AutoBattleGameParameterPreset CloneAs(string id, string displayName)
    {
        return new AutoBattleGameParameterPreset
        {
            Id = id,
            DisplayName = displayName,
            RoleId = RoleId,
            PartnerId = PartnerId,
            EnabledRewardCardPackIds = EnabledRewardCardPackIds.ToList(),
            PreferredDeckSizeMinimum = PreferredDeckSizeMinimum,
            PreferredDeckSizeMaximum = PreferredDeckSizeMaximum,
            ResolvedRoleSkillIds = ResolvedRoleSkillIds.ToList(),
            ResolvedRoleInitialStatuses = new Dictionary<string, int>(
                ResolvedRoleInitialStatuses,
                StringComparer.OrdinalIgnoreCase),
            ResolvedRoleSkillCooldownTurns = new Dictionary<string, int>(
                ResolvedRoleSkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase),
            ResolvedFamiliarBlessingIds = ResolvedFamiliarBlessingIds.ToList()
        };
    }

    public void Normalize(int index = 0)
    {
        Id = NormalizeId(Id, "preset-" + (index + 1));
        DisplayName = string.IsNullOrWhiteSpace(DisplayName)
            ? "游戏预设 " + (index + 1)
            : DisplayName.Trim();
        RoleId = string.IsNullOrWhiteSpace(RoleId) ? "career_1" : RoleId.Trim();
        PartnerId = string.IsNullOrWhiteSpace(PartnerId)
            ? "Partner_10001"
            : PartnerId.Trim();
        EnabledRewardCardPackIds ??= new List<string>();
        EnabledRewardCardPackIds = EnabledRewardCardPackIds
            .Select(item => (item ?? "").Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item)
                           && !string.Equals(
                               item,
                               "cardpack_13",
                               StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { "cardpack_1", "cardpack_2" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardPackOrder)
            .ThenBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        PreferredDeckSizeMinimum = Math.Max(
            1,
            Math.Min(80, PreferredDeckSizeMinimum));
        PreferredDeckSizeMaximum = Math.Max(
            PreferredDeckSizeMinimum,
            Math.Min(80, PreferredDeckSizeMaximum));
        ResolvedRoleSkillIds = NormalizeIds(ResolvedRoleSkillIds);
        ResolvedRoleInitialStatuses =
            (ResolvedRoleInitialStatuses
             ?? new Dictionary<string, int>(
                 StringComparer.OrdinalIgnoreCase))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && item.Value > 0)
            .ToDictionary(
                item => item.Key.Trim(),
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        ResolvedRoleSkillCooldownTurns =
            (ResolvedRoleSkillCooldownTurns
             ?? new Dictionary<string, int>(
                 StringComparer.OrdinalIgnoreCase))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(
                item => item.Key.Trim(),
                item => Math.Max(1, Math.Min(99, item.Value)),
                StringComparer.OrdinalIgnoreCase);
        ResolvedFamiliarBlessingIds = NormalizeIds(
            ResolvedFamiliarBlessingIds);
    }

    private static List<string> NormalizeIds(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(item => (item ?? "").Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeId(string value, string fallback)
    {
        var normalized = new string((value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(character =>
                char.IsLetterOrDigit(character) || character == '-'
                    ? character
                    : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static int CardPackOrder(string id)
    {
        const string prefix = "cardpack_";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id.Substring(prefix.Length), out var value)
            ? value
            : int.MaxValue;
    }
}

public sealed class AutoBattleGameValidationSettings
{
    [JsonProperty("requiredForPromotion")]
    public bool RequiredForPromotion { get; set; } = true;

    [JsonProperty("hidePresentation")]
    public bool HidePresentation { get; set; } = true;

    [JsonProperty("repetitionsPerFinalBoss")]
    public int RepetitionsPerFinalBoss { get; set; } = 1;

    [JsonProperty("minimumWinsPerFinalBoss")]
    public int MinimumWinsPerFinalBoss { get; set; }

    [JsonProperty("maximumInvalidRuns")]
    public int MaximumInvalidRuns { get; set; }

    [JsonProperty("maximumActionsPerBattle")]
    public int MaximumActionsPerBattle { get; set; } = 400;

    [JsonProperty("minimumDecisionsPerBattle")]
    public int MinimumDecisionsPerBattle { get; set; } = 1;

    [JsonProperty("battleTimeoutSeconds")]
    public int BattleTimeoutSeconds { get; set; } = 180;

    public void Normalize()
    {
        RepetitionsPerFinalBoss = Math.Max(1, Math.Min(20, RepetitionsPerFinalBoss));
        MinimumWinsPerFinalBoss = Math.Max(
            0,
            Math.Min(RepetitionsPerFinalBoss, MinimumWinsPerFinalBoss));
        MaximumInvalidRuns = Math.Max(0, Math.Min(20, MaximumInvalidRuns));
        MaximumActionsPerBattle = Math.Max(20, Math.Min(2000, MaximumActionsPerBattle));
        MinimumDecisionsPerBattle = Math.Max(
            1,
            Math.Min(MaximumActionsPerBattle, MinimumDecisionsPerBattle));
        BattleTimeoutSeconds = Math.Max(30, Math.Min(1800, BattleTimeoutSeconds));
    }
}

public sealed class AutoBattleFoundationTrainingSettings
{
    [JsonProperty("randomizeRunSeed")]
    public bool RandomizeRunSeed { get; set; } = true;

    [JsonProperty("runSeed")]
    public ulong RunSeed { get; set; }

    [JsonProperty("iterations")]
    public int Iterations { get; set; } = 8;

    [JsonProperty("trainingCampaignsPerIteration")]
    public int TrainingCampaignsPerIteration { get; set; } = 64;

    [JsonProperty("arenaCampaignsPerDifficulty")]
    public int ArenaCampaignsPerDifficulty { get; set; } = 32;

    [JsonProperty("arenaConfirmationCampaignsPerDifficulty")]
    public int ArenaConfirmationCampaignsPerDifficulty { get; set; } = 64;

    [JsonProperty("normalValidationCampaigns")]
    public int NormalValidationCampaigns { get; set; } = 200;

    [JsonProperty("advancedValidationCampaigns")]
    public int AdvancedValidationCampaigns { get; set; } = 500;

    [JsonProperty("capabilityProbeCampaignsPerDifficulty")]
    public int CapabilityProbeCampaignsPerDifficulty { get; set; } = 16;

    [JsonProperty("requireCapabilityProbeBaselineGain")]
    public bool RequireCapabilityProbeBaselineGain { get; set; } = true;

    [JsonProperty("capabilityProbeMinimumVictoryGain")]
    public int CapabilityProbeMinimumVictoryGain { get; set; } = 1;

    [JsonProperty("capabilityProbeMinimumDepthGain")]
    public double CapabilityProbeMinimumDepthGain { get; set; } = 0.5d;

    [JsonProperty("preflightCampaignsPerDifficulty")]
    public int PreflightCampaignsPerDifficulty { get; set; } = 32;

    [JsonProperty("parallelism")]
    public int Parallelism { get; set; } =
        Math.Max(1, Math.Min(16, Environment.ProcessorCount));

    [JsonProperty("parallelismProfile")]
    public string ParallelismProfile { get; set; } =
        AutoBattleFoundationExecutionProfileNames.Auto;

    [JsonProperty("inferenceExecutionMode")]
    public string InferenceExecutionMode { get; set; } =
        AutoBattleFoundationExecutionProfileNames.DirectInference;

    [JsonProperty("inferenceParallelism")]
    public int InferenceParallelism { get; set; }

    [JsonProperty("threadPoolMinimumWorkerThreads")]
    public int ThreadPoolMinimumWorkerThreads { get; set; }

    [JsonProperty("checkpointSerializationParallelism")]
    public int CheckpointSerializationParallelism { get; set; }

    [JsonProperty("reuseAutoTuneCache")]
    public bool ReuseAutoTuneCache { get; set; } = true;

    [JsonProperty("autoTuneSampleCampaigns")]
    public int AutoTuneSampleCampaigns { get; set; } = 32;

    [JsonProperty("autoTuneThroughputTolerance")]
    public double AutoTuneThroughputTolerance { get; set; } = 0.02d;

    [JsonProperty("executionMode")]
    public string ExecutionMode { get; set; } = "external";

    [JsonProperty("earlyValidationStop")]
    public bool EarlyValidationStop { get; set; } = true;

    [JsonProperty("validationEarlyStopBatchSize")]
    public int ValidationEarlyStopBatchSize { get; set; } = 32;

    [JsonProperty("enableCurriculum")]
    public bool EnableCurriculum { get; set; } = true;

    [JsonProperty("enableStratifiedReplay")]
    public bool EnableStratifiedReplay { get; set; } = true;

    [JsonProperty("enableHardSeedCurriculum")]
    public bool EnableHardSeedCurriculum { get; set; } = true;

    [JsonProperty("enableCounterfactualHardEncounters")]
    public bool EnableCounterfactualHardEncounters { get; set; } = true;

    [JsonProperty("enableSuccessCaseArchive")]
    public bool EnableSuccessCaseArchive { get; set; } = true;

    [JsonProperty("enableArenaRecovery")]
    public bool EnableArenaRecovery { get; set; } = true;

    [JsonProperty("arenaInvalidRetryCount")]
    public int ArenaInvalidRetryCount { get; set; } = 1;

    [JsonProperty("arenaInvalidRateLimit")]
    public double ArenaInvalidRateLimit { get; set; } = 0.02d;

    [JsonProperty("enableTuningArena")]
    public bool EnableTuningArena { get; set; } = true;

    [JsonProperty("tuningNormalCampaigns")]
    public int TuningNormalCampaigns { get; set; } = 32;

    [JsonProperty("tuningAdvancedCampaigns")]
    public int TuningAdvancedCampaigns { get; set; } = 64;

    [JsonProperty("enableProgressiveTuning")]
    public bool EnableProgressiveTuning { get; set; } = true;

    [JsonProperty("tuningScreeningNormalCampaigns")]
    public int TuningScreeningNormalCampaigns { get; set; } = 8;

    [JsonProperty("tuningScreeningAdvancedCampaigns")]
    public int TuningScreeningAdvancedCampaigns { get; set; } = 16;

    [JsonProperty("tuningFinalistCount")]
    public int TuningFinalistCount { get; set; } = 2;

    [JsonProperty("maximumConsecutiveRejectedIterations")]
    public int MaximumConsecutiveRejectedIterations { get; set; } = 3;

    [JsonProperty("normalAcceptanceRate")]
    public double NormalAcceptanceRate { get; set; } = 0.80d;

    [JsonProperty("advancedAcceptanceRate")]
    public double AdvancedAcceptanceRate { get; set; } = 0.30d;

    [JsonProperty("successExpertReplayShare")]
    public double SuccessExpertReplayShare { get; set; } = 0.20d;

    [JsonProperty("authoritativeContentReplayShare")]
    public double AuthoritativeContentReplayShare { get; set; } = 0.20d;

    [JsonProperty("hardSeedReplayShare")]
    public double HardSeedReplayShare { get; set; } = 0.35d;

    [JsonProperty("hardEncounterWeights")]
    public Dictionary<string, double> HardEncounterWeights { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["level_10011"] = 0.25d,
            ["level_10040"] = 0.15d,
            ["level_10004"] = 0.15d,
            ["level_10001"] = 0.15d,
            ["level_10009"] = 0.12d,
            ["level_10006"] = 0.10d,
            ["@other"] = 0.05d,
            ["@final-boss"] = 0.03d
        };

    [JsonProperty("selfPlayExplorationProbability")]
    public double SelfPlayExplorationProbability { get; set; } = 0.15d;

    [JsonProperty("selfPlayExplorationTemperature")]
    public double SelfPlayExplorationTemperature { get; set; } = 1d;

    [JsonProperty("modelEpochs")]
    public int ModelEpochs { get; set; } = 40;

    [JsonProperty("modelMinimumEpochs")]
    public int ModelMinimumEpochs { get; set; } = 8;

    [JsonProperty("modelEarlyStoppingPatience")]
    public int ModelEarlyStoppingPatience { get; set; } = 8;

    [JsonProperty("modelEarlyStoppingMinimumDelta")]
    public double ModelEarlyStoppingMinimumDelta { get; set; } = 0.0002d;

    [JsonProperty("modelBatchSize")]
    public int ModelBatchSize { get; set; } = 64;

    [JsonProperty("modelGradientShardCount")]
    public int ModelGradientShardCount { get; set; } = 12;

    [JsonProperty("enableFrameStratification")]
    public bool EnableFrameStratification { get; set; } = true;

    [JsonProperty("modelMaximumFrameStratumWeight")]
    public double ModelMaximumFrameStratumWeight { get; set; } = 3d;

    [JsonProperty("modelMaximumFramesPerEpisode")]
    public int ModelMaximumFramesPerEpisode { get; set; } = 96;

    [JsonProperty("modelReplayEpisodeLimit")]
    public int ModelReplayEpisodeLimit { get; set; } = 8000;

    [JsonProperty("modelRetainedCandidates")]
    public int ModelRetainedCandidates { get; set; } = 3;

    [JsonProperty("modelLearningRate")]
    public double ModelLearningRate { get; set; } = 0.00625d;

    [JsonProperty("modelL2")]
    public double ModelL2 { get; set; } = 0.0015d;

    [JsonProperty("modelStateDimensions")]
    public int ModelStateDimensions { get; set; } = 256;

    [JsonProperty("modelActionDimensions")]
    public int ModelActionDimensions { get; set; } = 192;

    [JsonProperty("modelHiddenDimensions")]
    public int ModelHiddenDimensions { get; set; } = 64;

    [JsonProperty("modelFeatureEncodingMode")]
    public string ModelFeatureEncodingMode { get; set; } = "partitioned-v3";

    [JsonProperty("trainingSeedStart")]
    public ulong TrainingSeedStart { get; set; } = 10_000UL;

    [JsonProperty("arenaSeedStart")]
    public ulong ArenaSeedStart { get; set; } = 1_000_000UL;

    [JsonProperty("tuningSeedStart")]
    public ulong TuningSeedStart { get; set; } = 1_500_000UL;

    [JsonProperty("validationSeedStart")]
    public ulong ValidationSeedStart { get; set; } = 2_000_000UL;

    public void Normalize()
    {
        Iterations = Math.Max(1, Math.Min(20, Iterations));
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
            Math.Min(32, CapabilityProbeCampaignsPerDifficulty));
        CapabilityProbeMinimumVictoryGain = Math.Max(
            1,
            Math.Min(32, CapabilityProbeMinimumVictoryGain));
        CapabilityProbeMinimumDepthGain = ClampFinite(
            CapabilityProbeMinimumDepthGain,
            0d,
            37d,
            0.5d);
        PreflightCampaignsPerDifficulty = Math.Max(
            1,
            Math.Min(100, PreflightCampaignsPerDifficulty));
        ParallelismProfile = NormalizeExecutionProfile(ParallelismProfile);
        var processors = Math.Max(1, Environment.ProcessorCount);
        Parallelism = ParallelismProfile switch
        {
            AutoBattleFoundationExecutionProfileNames.Cpu16 =>
                Math.Min(16, processors),
            AutoBattleFoundationExecutionProfileNames.Cpu32 =>
                Math.Min(32, processors),
            AutoBattleFoundationExecutionProfileNames.Auto =>
                processors >= 32 ? 32 : processors >= 16 ? 16 : processors,
            _ => Math.Max(1, Math.Min(processors, Parallelism))
        };
        InferenceExecutionMode = string.Equals(
            InferenceExecutionMode,
            AutoBattleFoundationExecutionProfileNames.ShardedBatchInference,
            StringComparison.OrdinalIgnoreCase)
            ? AutoBattleFoundationExecutionProfileNames.ShardedBatchInference
            : AutoBattleFoundationExecutionProfileNames.DirectInference;
        InferenceParallelism = InferenceParallelism <= 0
            ? Parallelism
            : Math.Max(1, Math.Min(Parallelism, InferenceParallelism));
        ThreadPoolMinimumWorkerThreads = ThreadPoolMinimumWorkerThreads <= 0
            ? Parallelism + 8
            : Math.Max(Parallelism, Math.Min(256, ThreadPoolMinimumWorkerThreads));
        CheckpointSerializationParallelism =
            CheckpointSerializationParallelism <= 0
                ? Parallelism >= 32 ? 2 : 1
                : Math.Max(1, Math.Min(2, CheckpointSerializationParallelism));
        AutoTuneSampleCampaigns = Math.Max(
            4,
            Math.Min(64, AutoTuneSampleCampaigns));
        AutoTuneThroughputTolerance = ClampFinite(
            AutoTuneThroughputTolerance,
            0d,
            0.20d,
            0.02d);
        ValidationEarlyStopBatchSize = Math.Max(
            1,
            Math.Min(128, ValidationEarlyStopBatchSize));
        ModelEpochs = Math.Max(5, Math.Min(200, ModelEpochs));
        ModelMinimumEpochs = Math.Max(
            1,
            Math.Min(ModelEpochs, ModelMinimumEpochs));
        ModelEarlyStoppingPatience = Math.Max(
            1,
            Math.Min(30, ModelEarlyStoppingPatience));
        ModelEarlyStoppingMinimumDelta = ClampFinite(
            ModelEarlyStoppingMinimumDelta,
            0.0000001d,
            0.1d,
            0.0002d);
        ModelBatchSize = Math.Max(8, Math.Min(512, ModelBatchSize));
        ModelGradientShardCount = Math.Max(
            1,
            Math.Min(32, ModelGradientShardCount));
        ModelMaximumFrameStratumWeight = ClampFinite(
            ModelMaximumFrameStratumWeight,
            1d,
            5d,
            3d);
        ModelMaximumFramesPerEpisode = Math.Max(
            8,
            Math.Min(512, ModelMaximumFramesPerEpisode));
        ModelReplayEpisodeLimit = Math.Max(
            64,
            Math.Min(20000, ModelReplayEpisodeLimit));
        ModelRetainedCandidates = Math.Max(
            1,
            Math.Min(5, ModelRetainedCandidates));
        ModelLearningRate = ClampFinite(
            ModelLearningRate,
            0.0001d,
            0.1d,
            0.00625d);
        ModelL2 = ClampFinite(ModelL2, 0d, 0.05d, 0.0015d);
        ModelStateDimensions = Math.Max(
            16,
            Math.Min(512, ModelStateDimensions));
        ModelActionDimensions = Math.Max(
            16,
            Math.Min(512, ModelActionDimensions));
        ModelHiddenDimensions = Math.Max(
            8,
            Math.Min(256, ModelHiddenDimensions));
        ModelFeatureEncodingMode = "partitioned-v3";
        ArenaInvalidRetryCount = Math.Max(
            0,
            Math.Min(3, ArenaInvalidRetryCount));
        ArenaInvalidRateLimit = ClampFinite(
            ArenaInvalidRateLimit,
            0.0001d,
            1d,
            0.02d);
        TuningNormalCampaigns = Math.Max(
            0,
            Math.Min(64, TuningNormalCampaigns));
        TuningAdvancedCampaigns = Math.Max(
            0,
            Math.Min(64, TuningAdvancedCampaigns));
        TuningScreeningNormalCampaigns = Math.Max(
            0,
            Math.Min(
                TuningNormalCampaigns,
                TuningScreeningNormalCampaigns));
        TuningScreeningAdvancedCampaigns = Math.Max(
            0,
            Math.Min(
                TuningAdvancedCampaigns,
                TuningScreeningAdvancedCampaigns));
        TuningFinalistCount = Math.Max(
            1,
            Math.Min(ModelRetainedCandidates, TuningFinalistCount));
        MaximumConsecutiveRejectedIterations = Math.Max(
            0,
            Math.Min(8, MaximumConsecutiveRejectedIterations));
        NormalAcceptanceRate = ClampFinite(
            NormalAcceptanceRate,
            0d,
            1d,
            0.80d);
        AdvancedAcceptanceRate = ClampFinite(
            AdvancedAcceptanceRate,
            0d,
            1d,
            0.30d);
        HardSeedReplayShare = double.IsNaN(HardSeedReplayShare)
                              || double.IsInfinity(HardSeedReplayShare)
            ? 0.35d
            : Math.Max(0d, Math.Min(0.75d, HardSeedReplayShare));
        HardEncounterWeights = (HardEncounterWeights
                                ?? new Dictionary<string, double>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && item.Value > 0d
                           && !double.IsNaN(item.Value)
                           && !double.IsInfinity(item.Value))
            .ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        SuccessExpertReplayShare =
            double.IsNaN(SuccessExpertReplayShare)
            || double.IsInfinity(SuccessExpertReplayShare)
                ? 0.20d
                : Math.Max(
                    0d,
                    Math.Min(0.40d, SuccessExpertReplayShare));
        AuthoritativeContentReplayShare =
            double.IsNaN(AuthoritativeContentReplayShare)
            || double.IsInfinity(AuthoritativeContentReplayShare)
                ? 0.20d
                : Math.Max(
                    0d,
                    Math.Min(0.50d, AuthoritativeContentReplayShare));
        var executionMode = (ExecutionMode ?? "").Trim().ToLowerInvariant();
        ExecutionMode = executionMode == "inprocess"
            ? "inprocess"
            : "external";
        TrainingSeedStart = TrainingSeedStart == 0UL ? 10_000UL : TrainingSeedStart;
        ArenaSeedStart = ArenaSeedStart == 0UL ? 1_000_000UL : ArenaSeedStart;
        TuningSeedStart = TuningSeedStart == 0UL
            ? 1_500_000UL
            : TuningSeedStart;
        ValidationSeedStart = ValidationSeedStart == 0UL ? 2_000_000UL : ValidationSeedStart;
        SelfPlayExplorationProbability = double.IsNaN(
                SelfPlayExplorationProbability)
            || double.IsInfinity(SelfPlayExplorationProbability)
                ? 0.15d
                : Math.Max(
                    0d,
                    Math.Min(0.5d, SelfPlayExplorationProbability));
        SelfPlayExplorationTemperature = double.IsNaN(
                SelfPlayExplorationTemperature)
            || double.IsInfinity(SelfPlayExplorationTemperature)
                ? 1d
                : Math.Max(
                    0.1d,
                    Math.Min(5d, SelfPlayExplorationTemperature));
    }

    private static string NormalizeExecutionProfile(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            AutoBattleFoundationExecutionProfileNames.Cpu16 => normalized,
            AutoBattleFoundationExecutionProfileNames.Cpu32 => normalized,
            AutoBattleFoundationExecutionProfileNames.Custom => normalized,
            _ => AutoBattleFoundationExecutionProfileNames.Auto
        };
    }

    private static double ClampFinite(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        var finite = double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : value;
        return Math.Max(minimum, Math.Min(maximum, finite));
    }
}

public sealed class AutoBattleSimulationSettings
{
    [JsonProperty("scenarioId")]
    public string ScenarioId { get; set; } = "";

    [JsonProperty("difficultyId")]
    public string DifficultyId { get; set; } = "normal";

    [JsonProperty("simulationCount")]
    public int SimulationCount { get; set; } = 8;

    [JsonProperty("parallelism")]
    public int Parallelism { get; set; } = 2;

    [JsonProperty("seedStart")]
    public ulong SeedStart { get; set; } = 1UL;

    [JsonProperty("retainDivergentTraces")]
    public bool RetainDivergentTraces { get; set; } = true;

    [JsonProperty("minimumAuthoritativeCoverage")]
    public double MinimumAuthoritativeCoverage { get; set; } = 1d;

    [JsonProperty("maximumWinRateRegression")]
    public double MaximumWinRateRegression { get; set; } = 0.01d;

    [JsonProperty("collectPolicyValueEpisodes")]
    public bool CollectPolicyValueEpisodes { get; set; } = true;

    [JsonProperty("evolutionIterations")]
    public int EvolutionIterations { get; set; } = 3;

    [JsonProperty("evolutionEpisodesPerIteration")]
    public int EvolutionEpisodesPerIteration { get; set; } = 32;

    [JsonProperty("evolutionArenaEpisodes")]
    public int EvolutionArenaEpisodes { get; set; } = 16;

    public void Normalize()
    {
        ScenarioId = ScenarioId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(ScenarioId))
        {
            ScenarioId = "witch.world-simulation.standard-v2";
        }
        DifficultyId = string.Equals(
            DifficultyId?.Trim(),
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
        SimulationCount = Math.Max(1, Math.Min(100000, SimulationCount));
        Parallelism = Math.Max(1, Math.Min(16, Parallelism));
        MinimumAuthoritativeCoverage = Clamp(
            MinimumAuthoritativeCoverage,
            0d,
            1d,
            1d);
        MaximumWinRateRegression = Clamp(
            MaximumWinRateRegression,
            0d,
            0.25d,
            0.01d);
        EvolutionIterations = Math.Max(1, Math.Min(20, EvolutionIterations));
        EvolutionEpisodesPerIteration = Math.Max(8, Math.Min(10000, EvolutionEpisodesPerIteration));
        EvolutionArenaEpisodes = Math.Max(2, Math.Min(10000, EvolutionArenaEpisodes));
    }

    private static double Clamp(double value, double min, double max, double fallback)
    {
        var finite = double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        return Math.Max(min, Math.Min(max, finite));
    }
}

public sealed class AutoBattleTrainingSettings
{
    public const string SteadyPreset = "steady";
    public const string StandardPreset = "standard";
    public const string AdaptivePreset = "adaptive";
    public const string CustomPreset = "custom";

    [JsonProperty("preset")]
    public string Preset { get; set; } = SteadyPreset;

    [JsonProperty("epochs")]
    public int Epochs { get; set; } = 80;

    [JsonProperty("learningRate")]
    public double LearningRate { get; set; } = 0.03d;

    [JsonProperty("l2")]
    public double L2 { get; set; } = 0.003d;

    [JsonProperty("maximumCorrection")]
    public double MaximumCorrection { get; set; } = 0.75d;

    [JsonProperty("minimumPreferencePairs")]
    public int MinimumPreferencePairs { get; set; } = 15;

    [JsonProperty("minimumCategoryObservations")]
    public int MinimumCategoryObservations { get; set; } = 10;

    [JsonProperty("minimumEpisodes")]
    public int MinimumEpisodes { get; set; } = 8;

    [JsonProperty("policyValueHiddenDimensions")]
    public int PolicyValueHiddenDimensions { get; set; } = 48;

    public static AutoBattleTrainingSettings CreateSteady()
    {
        var settings = new AutoBattleTrainingSettings();
        settings.ApplyPreset(SteadyPreset);
        return settings;
    }

    public void ApplyPreset(string preset)
    {
        switch ((preset ?? "").Trim().ToLowerInvariant())
        {
            case StandardPreset:
                Preset = StandardPreset;
                Epochs = 100;
                LearningRate = 0.05d;
                L2 = 0.001d;
                MaximumCorrection = 1.25d;
                MinimumPreferencePairs = 10;
                MinimumCategoryObservations = 5;
                MinimumEpisodes = 12;
                PolicyValueHiddenDimensions = 64;
                break;

            case AdaptivePreset:
                Preset = AdaptivePreset;
                Epochs = 180;
                LearningRate = 0.03d;
                L2 = 0.001d;
                MaximumCorrection = 2d;
                MinimumPreferencePairs = 30;
                MinimumCategoryObservations = 15;
                MinimumEpisodes = 30;
                PolicyValueHiddenDimensions = 96;
                break;

            default:
                Preset = SteadyPreset;
                Epochs = 80;
                LearningRate = 0.03d;
                L2 = 0.003d;
                MaximumCorrection = 0.75d;
                MinimumPreferencePairs = 15;
                MinimumCategoryObservations = 10;
                MinimumEpisodes = 8;
                PolicyValueHiddenDimensions = 48;
                break;
        }
        Normalize();
    }

    public void MarkCustom()
    {
        Preset = CustomPreset;
    }

    public void Normalize()
    {
        Preset = NormalizePreset(Preset);
        Epochs = Math.Max(20, Math.Min(300, Epochs));
        LearningRate = ClampFinite(LearningRate, 0.005d, 0.1d, 0.03d);
        L2 = ClampFinite(L2, 0d, 0.02d, 0.003d);
        MaximumCorrection = ClampFinite(MaximumCorrection, 0.25d, 2d, 0.75d);
        MinimumPreferencePairs = Math.Max(1, Math.Min(200, MinimumPreferencePairs));
        MinimumCategoryObservations = Math.Max(3, Math.Min(100, MinimumCategoryObservations));
        MinimumEpisodes = Math.Max(2, Math.Min(10000, MinimumEpisodes));
        PolicyValueHiddenDimensions = Math.Max(8, Math.Min(256, PolicyValueHiddenDimensions));
    }

    private static string NormalizePreset(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            StandardPreset => StandardPreset,
            AdaptivePreset => AdaptivePreset,
            CustomPreset => CustomPreset,
            _ => SteadyPreset
        };
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        var finite = double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        return Math.Max(minimum, Math.Min(maximum, finite));
    }
}
