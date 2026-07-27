using System;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

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

    [JsonProperty("showPredictionMarkers")]
    public bool ShowPredictionMarkers { get; set; } = true;

    [JsonProperty("searchQuality")]
    public string SearchQuality { get; set; } = "balanced";

    [JsonProperty("training")]
    public AutoBattleTrainingSettings Training { get; set; } = AutoBattleTrainingSettings.CreateSteady();

    [JsonProperty("foundationTraining")]
    public AutoBattleFoundationTrainingSettings FoundationTraining { get; set; } = new();

    [JsonProperty("simulation")]
    public AutoBattleSimulationSettings Simulation { get; set; } = new();

    public void Normalize()
    {
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
        DecisionIntervalMs = Math.Max(150, Math.Min(2000, DecisionIntervalMs));
        ActionTimeoutSeconds = Math.Max(3f, Math.Min(60f, ActionTimeoutSeconds));
        SearchQuality = NormalizeChoice(
            SearchQuality,
            "balanced",
            "fast",
            "deep");
        Training.Normalize();
        FoundationTraining ??= new AutoBattleFoundationTrainingSettings();
        FoundationTraining.Normalize();
        Simulation ??= new AutoBattleSimulationSettings();
        Simulation.Normalize();
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

    [JsonProperty("preflightCampaignsPerDifficulty")]
    public int PreflightCampaignsPerDifficulty { get; set; } = 32;

    [JsonProperty("parallelism")]
    public int Parallelism { get; set; } =
        Math.Max(1, Math.Min(16, Environment.ProcessorCount));

    [JsonProperty("executionMode")]
    public string ExecutionMode { get; set; } = "external";

    [JsonProperty("earlyValidationStop")]
    public bool EarlyValidationStop { get; set; } = true;

    [JsonProperty("enableCurriculum")]
    public bool EnableCurriculum { get; set; } = true;

    [JsonProperty("enableStratifiedReplay")]
    public bool EnableStratifiedReplay { get; set; } = true;

    [JsonProperty("enableHardSeedCurriculum")]
    public bool EnableHardSeedCurriculum { get; set; } = true;

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
    public int TuningNormalCampaigns { get; set; } = 8;

    [JsonProperty("tuningAdvancedCampaigns")]
    public int TuningAdvancedCampaigns { get; set; } = 12;

    [JsonProperty("normalAcceptanceRate")]
    public double NormalAcceptanceRate { get; set; } = 0.90d;

    [JsonProperty("advancedAcceptanceRate")]
    public double AdvancedAcceptanceRate { get; set; } = 0.50d;

    [JsonProperty("successExpertReplayShare")]
    public double SuccessExpertReplayShare { get; set; } = 0.20d;

    [JsonProperty("hardSeedReplayShare")]
    public double HardSeedReplayShare { get; set; } = 0.35d;

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

    [JsonProperty("modelReplayEpisodeLimit")]
    public int ModelReplayEpisodeLimit { get; set; } = 6000;

    [JsonProperty("modelRetainedCandidates")]
    public int ModelRetainedCandidates { get; set; } = 3;

    [JsonProperty("modelLearningRate")]
    public double ModelLearningRate { get; set; } = 0.0125d;

    [JsonProperty("modelL2")]
    public double ModelL2 { get; set; } = 0.0015d;

    [JsonProperty("modelStateDimensions")]
    public int ModelStateDimensions { get; set; } = 128;

    [JsonProperty("modelActionDimensions")]
    public int ModelActionDimensions { get; set; } = 96;

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
        PreflightCampaignsPerDifficulty = Math.Max(
            1,
            Math.Min(100, PreflightCampaignsPerDifficulty));
        Parallelism = Math.Max(
            1,
            Math.Min(Math.Max(1, Environment.ProcessorCount), Parallelism));
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
            0.0125d);
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
        NormalAcceptanceRate = ClampFinite(
            NormalAcceptanceRate,
            0d,
            1d,
            0.90d);
        AdvancedAcceptanceRate = ClampFinite(
            AdvancedAcceptanceRate,
            0d,
            1d,
            0.50d);
        HardSeedReplayShare = double.IsNaN(HardSeedReplayShare)
                              || double.IsInfinity(HardSeedReplayShare)
            ? 0.35d
            : Math.Max(0d, Math.Min(0.75d, HardSeedReplayShare));
        SuccessExpertReplayShare =
            double.IsNaN(SuccessExpertReplayShare)
            || double.IsInfinity(SuccessExpertReplayShare)
                ? 0.20d
                : Math.Max(
                    0d,
                    Math.Min(0.40d, SuccessExpertReplayShare));
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
