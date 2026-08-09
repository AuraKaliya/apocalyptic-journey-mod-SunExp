using System;
using System.Collections.Generic;
using System.Linq;
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

    [JsonProperty("networkDeathRiskWeight")]
    public double NetworkDeathRiskWeight { get; set; } = 1d;

    [JsonProperty("semanticCoverageRiskWeight")]
    public double SemanticCoverageRiskWeight { get; set; } = 0.5d;

    [JsonProperty("searchModelEvaluationBudget")]
    public int SearchModelEvaluationBudget { get; set; } = 384;

    [JsonProperty("riskPreference")]
    public double RiskPreference { get; set; } = -1d;

    [JsonProperty("enableActorCandidatePruning")]
    public bool EnableActorCandidatePruning { get; set; }

    [JsonProperty("actorCandidateTopK")]
    public int ActorCandidateTopK { get; set; } = 12;

    [JsonProperty("actorCandidateProbabilityMass")]
    public double ActorCandidateProbabilityMass { get; set; } = 0.995d;

    [JsonProperty("gameParameters")]
    public AutoBattleGameParameterSettings GameParameters { get; set; } = new();

    [JsonProperty("training")]
    public AutoBattleTrainingSettings Training { get; set; } = AutoBattleTrainingSettings.CreateSteady();

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
        NetworkDeathRiskWeight = NormalizeUnitWeight(
            NetworkDeathRiskWeight,
            1d);
        SemanticCoverageRiskWeight = NormalizeUnitWeight(
            SemanticCoverageRiskWeight,
            0.5d);
        SearchModelEvaluationBudget = Math.Max(
            32,
            Math.Min(4096, SearchModelEvaluationBudget));
        RiskPreference = double.IsNaN(RiskPreference)
                         || double.IsInfinity(RiskPreference)
            ? -1d
            : Math.Max(-1d, Math.Min(1d, RiskPreference));
        ActorCandidateTopK = Math.Max(4, Math.Min(64, ActorCandidateTopK));
        ActorCandidateProbabilityMass = double.IsNaN(
                                                ActorCandidateProbabilityMass)
                                            || double.IsInfinity(
                                                ActorCandidateProbabilityMass)
            ? 0.995d
            : Math.Max(0.80d, Math.Min(1d, ActorCandidateProbabilityMass));
        GameParameters.Normalize();
        Training.Normalize();
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

    private static double NormalizeUnitWeight(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Max(0d, Math.Min(1d, value));
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

    [JsonProperty("resolvedRoleInitialSkillCooldownTurns")]
    public Dictionary<string, int> ResolvedRoleInitialSkillCooldownTurns {
        get;
        set;
    } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("resolvedRoleMaximumHp")]
    public int ResolvedRoleMaximumHp { get; set; }

    [JsonProperty("resolvedRoleInitialVariables")]
    public Dictionary<string, double> ResolvedRoleInitialVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("resolvedRoleNativeScriptHash")]
    public string ResolvedRoleNativeScriptHash { get; set; } = "";

    [JsonProperty("resolvedRoleFightScript")]
    public string ResolvedRoleFightScript { get; set; } = "";

    [JsonProperty("resolvedRoleNativeManagedSkillCooldownIds")]
    public List<string> ResolvedRoleNativeManagedSkillCooldownIds { get; set; } =
        new();

    [JsonProperty("resolvedRoleRuntimeForms")]
    public List<AutoBattleRoleRuntimeForm> ResolvedRoleRuntimeForms { get; set; } =
        new();

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
            ResolvedRoleInitialSkillCooldownTurns =
                new Dictionary<string, int>(
                    ResolvedRoleInitialSkillCooldownTurns,
                    StringComparer.OrdinalIgnoreCase),
            ResolvedRoleMaximumHp = ResolvedRoleMaximumHp,
            ResolvedRoleInitialVariables = new Dictionary<string, double>(
                ResolvedRoleInitialVariables,
                StringComparer.OrdinalIgnoreCase),
            ResolvedRoleNativeScriptHash = ResolvedRoleNativeScriptHash,
            ResolvedRoleFightScript = ResolvedRoleFightScript,
            ResolvedRoleNativeManagedSkillCooldownIds =
                ResolvedRoleNativeManagedSkillCooldownIds.ToList(),
            ResolvedRoleRuntimeForms = ResolvedRoleRuntimeForms
                .Select(item => item.Clone())
                .ToList(),
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
        ResolvedRoleInitialSkillCooldownTurns =
            (ResolvedRoleInitialSkillCooldownTurns
             ?? new Dictionary<string, int>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && item.Value >= 0)
            .ToDictionary(
                item => item.Key.Trim(),
                item => Math.Min(99, item.Value),
                StringComparer.OrdinalIgnoreCase);
        ResolvedRoleMaximumHp = Math.Max(
            0,
            Math.Min(1000000, ResolvedRoleMaximumHp));
        ResolvedRoleInitialVariables = (ResolvedRoleInitialVariables
                                        ?? new Dictionary<string, double>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && !double.IsNaN(item.Value)
                           && !double.IsInfinity(item.Value))
            .ToDictionary(
                item => item.Key.Trim(),
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        ResolvedRoleNativeScriptHash =
            (ResolvedRoleNativeScriptHash ?? "").Trim();
        ResolvedRoleFightScript ??= "";
        ResolvedRoleNativeManagedSkillCooldownIds = NormalizeIds(
            ResolvedRoleNativeManagedSkillCooldownIds);
        ResolvedRoleRuntimeForms = (ResolvedRoleRuntimeForms
                                    ?? new List<AutoBattleRoleRuntimeForm>())
            .Where(item => item != null
                           && !string.IsNullOrWhiteSpace(item.RoleId))
            .GroupBy(item => item.RoleId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Clone())
            .ToList();
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

public sealed class AutoBattleRoleRuntimeForm
{
    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("maximumHp")]
    public int MaximumHp { get; set; }

    [JsonProperty("skillCardIds")]
    public List<string> SkillCardIds { get; set; } = new();

    [JsonProperty("skillCooldownTurns")]
    public Dictionary<string, int> SkillCooldownTurns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AutoBattleRoleRuntimeForm Clone()
    {
        return new AutoBattleRoleRuntimeForm
        {
            RoleId = RoleId,
            MaximumHp = MaximumHp,
            SkillCardIds = new List<string>(SkillCardIds),
            SkillCooldownTurns = new Dictionary<string, int>(
                SkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase)
        };
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
