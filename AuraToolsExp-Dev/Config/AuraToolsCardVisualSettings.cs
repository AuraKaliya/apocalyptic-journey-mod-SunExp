using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsCardVisualSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("themes")]
    public Dictionary<string, CardFrameThemeSettings> Themes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("dynamicEffects")]
    public Dictionary<string, CardDynamicEffectSettings> DynamicEffects { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        Themes = NormalizeDictionary(Themes, (key, value) => value.Normalize(key));
        DynamicEffects = NormalizeDictionary(DynamicEffects, (key, value) => value.Normalize());
    }

    private static Dictionary<string, T> NormalizeDictionary<T>(
        Dictionary<string, T>? source,
        Action<string, T> normalize) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source ?? new Dictionary<string, T>())
        {
            var key = (pair.Key ?? "").Trim();
            if (key.Length == 0 || pair.Value == null) continue;
            normalize(key, pair.Value);
            result[key] = pair.Value;
        }
        return result;
    }
}

public sealed class CardFrameThemeSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("initialized")]
    public bool Initialized { get; set; }

    [JsonProperty("appliedPresetVersion")]
    public int AppliedPresetVersion { get; set; }

    [JsonProperty("cards")]
    public Dictionary<string, string> Cards { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize(string themeId)
    {
        AppliedPresetVersion = Math.Max(0, AppliedPresetVersion);
        Cards = (Cards ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CardDynamicEffectSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("effectId")]
    public string EffectId { get; set; } = "";

    [JsonProperty("parameters")]
    public Dictionary<string, float> Parameters { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        EffectId = EffectId?.Trim() ?? "";
        Parameters = new Dictionary<string, float>(Parameters ?? new Dictionary<string, float>(), StringComparer.Ordinal);
    }
}
