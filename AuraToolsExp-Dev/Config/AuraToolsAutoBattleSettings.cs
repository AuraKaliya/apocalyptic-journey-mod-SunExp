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

    public void Normalize()
    {
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
