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

    [JsonProperty("useTrainedModel")]
    public bool UseTrainedModel { get; set; }

    [JsonProperty("trainedModelMode")]
    public string TrainedModelMode { get; set; } = "off";

    [JsonProperty("showPredictionMarkers")]
    public bool ShowPredictionMarkers { get; set; } = true;

    [JsonProperty("training")]
    public AutoBattleTrainingSettings Training { get; set; } = AutoBattleTrainingSettings.CreateSteady();

    public void Normalize(int sourceSchemaVersion = 14)
    {
        Training ??= sourceSchemaVersion < 14
            ? AutoBattleTrainingSettings.CreateLegacy()
            : AutoBattleTrainingSettings.CreateSteady();
        if (sourceSchemaVersion < 14)
        {
            Training = AutoBattleTrainingSettings.CreateLegacy();
        }

        Profile = NormalizeChoice(Profile, "balanced", "aggressive", "defensive");
        UnknownActionPolicy = NormalizeChoice(
            UnknownActionPolicy,
            "conservative",
            "allow",
            "handoff");
        TrainingMode = NormalizeChoice(TrainingMode, "auto", "shadow", "hybrid");
        if (UseTrainedModel
            && string.Equals(TrainedModelMode, "off", StringComparison.OrdinalIgnoreCase))
        {
            TrainedModelMode = "active";
        }
        TrainedModelMode = NormalizeChoice(TrainedModelMode, "off", "shadow", "active");
        UseTrainedModel = !string.Equals(TrainedModelMode, "off", StringComparison.Ordinal);
        DecisionIntervalMs = Math.Max(150, Math.Min(2000, DecisionIntervalMs));
        ActionTimeoutSeconds = Math.Max(3f, Math.Min(60f, ActionTimeoutSeconds));
        Training.Normalize();
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

    public static AutoBattleTrainingSettings CreateSteady()
    {
        var settings = new AutoBattleTrainingSettings();
        settings.ApplyPreset(SteadyPreset);
        return settings;
    }

    public static AutoBattleTrainingSettings CreateLegacy()
    {
        return new AutoBattleTrainingSettings
        {
            Preset = CustomPreset,
            Epochs = 100,
            LearningRate = 0.05d,
            L2 = 0.001d,
            MaximumCorrection = 2d,
            MinimumPreferencePairs = 1,
            MinimumCategoryObservations = 5
        };
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
                break;

            case AdaptivePreset:
                Preset = AdaptivePreset;
                Epochs = 180;
                LearningRate = 0.03d;
                L2 = 0.001d;
                MaximumCorrection = 2d;
                MinimumPreferencePairs = 30;
                MinimumCategoryObservations = 15;
                break;

            default:
                Preset = SteadyPreset;
                Epochs = 80;
                LearningRate = 0.03d;
                L2 = 0.003d;
                MaximumCorrection = 0.75d;
                MinimumPreferencePairs = 15;
                MinimumCategoryObservations = 10;
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
