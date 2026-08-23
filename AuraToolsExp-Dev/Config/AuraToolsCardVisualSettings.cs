using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsCardVisualSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("themes")]
    public Dictionary<string, CardFrameThemeSettings> Themes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("dynamicEffectOverrides")]
    public Dictionary<string, CardDynamicEffectSettings> DynamicEffectOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Schema v1 copied every effective effect mapping into the user file. It
    // is read once and folded into explicit overrides; schema v2 resolves
    // shipped defaults from card-visual.registry.json at runtime.
    [JsonProperty("dynamicEffects", NullValueHandling = NullValueHandling.Ignore)]
    private Dictionary<string, CardDynamicEffectSettings>? LegacyDynamicEffects { get; set; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, CardDynamicEffectSettings> DynamicEffects => DynamicEffectOverrides;

    public void Normalize()
    {
        if (SchemaVersion < 2 && LegacyDynamicEffects != null)
        {
            foreach (var pair in LegacyDynamicEffects)
            {
                if (!DynamicEffectOverrides.ContainsKey(pair.Key))
                {
                    DynamicEffectOverrides[pair.Key] = pair.Value;
                }
            }
        }

        SchemaVersion = 2;
        Themes = NormalizeDictionary(Themes, (key, value) => value.Normalize(key));
        DynamicEffectOverrides = NormalizeDictionary(
            DynamicEffectOverrides,
            (key, value) => value.Normalize());
        LegacyDynamicEffects = null;
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
