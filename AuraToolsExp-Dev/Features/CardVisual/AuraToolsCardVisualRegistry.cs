using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.CardVisual;

public static class AuraToolsCardVisualRegistry
{
    private const string RegistryFileName = "card-visual.registry.json";
    public const int CurrentProtocolVersion = 4;
    private static CardVisualRegistryDocument document = new();

    public static IReadOnlyList<CardFrameThemeDefinition> Themes => document.Themes;
    public static IReadOnlyList<CardDynamicEffectDefinition> Effects => document.Effects;

    public static bool Load(ModConfig modConfig)
    {
        try
        {
            var path = Path.Combine(modConfig.DirectoryName, RegistryFileName);
            var loaded = JsonConvert.DeserializeObject<CardVisualRegistryDocument>(File.ReadAllText(path))
                         ?? new CardVisualRegistryDocument();
            loaded.Normalize();
            Validate(loaded);
            document = loaded;
            AuraToolsLog.Info("[CardVisual] registry loaded: themes=" + Themes.Count + ", effects=" + Effects.Count + ".");
            return true;
        }
        catch (Exception ex)
        {
            document = new CardVisualRegistryDocument();
            AuraToolsLog.Error("Card visual registry load failed", ex);
            return false;
        }
    }

    public static CardFrameThemeDefinition? Theme(string themeId)
    {
        return Themes.FirstOrDefault(value => value.Enabled
            && string.Equals(value.ThemeId, themeId, StringComparison.OrdinalIgnoreCase));
    }

    public static CardFrameSkinDefinition? Skin(string themeId, string skinId)
    {
        return Theme(themeId)?.Skins.FirstOrDefault(value =>
            string.Equals(value.SkinId, skinId, StringComparison.OrdinalIgnoreCase));
    }

    public static CardDynamicEffectDefinition? Effect(string effectId)
    {
        return Effects.FirstOrDefault(value => value.Enabled
            && string.Equals(value.EffectId, effectId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryGetDefaultEffect(
        string qualifiedCardId,
        out CardDynamicEffectSettings settings)
    {
        var card = (qualifiedCardId ?? "").Trim();
        foreach (var effect in Effects.Where(value => value.Enabled))
        {
            var mapping = effect.MappingPreset.FirstOrDefault(value =>
                value.QualifiedCardIds.Contains(card, StringComparer.OrdinalIgnoreCase));
            if (mapping == null)
            {
                continue;
            }

            settings = new CardDynamicEffectSettings
            {
                Enabled = true,
                EffectId = effect.EffectId,
                Parameters = new Dictionary<string, float>(mapping.Parameters, StringComparer.Ordinal)
            };
            return true;
        }

        settings = new CardDynamicEffectSettings { Enabled = false };
        return false;
    }

    public static IReadOnlyDictionary<string, CardDynamicEffectSettings> DefaultEffects()
    {
        var result = new Dictionary<string, CardDynamicEffectSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var effect in Effects.Where(value => value.Enabled))
        {
            foreach (var mapping in effect.MappingPreset)
            {
                foreach (var card in mapping.QualifiedCardIds)
                {
                    result[card] = new CardDynamicEffectSettings
                    {
                        Enabled = true,
                        EffectId = effect.EffectId,
                        Parameters = new Dictionary<string, float>(mapping.Parameters, StringComparer.Ordinal)
                    };
                }
            }
        }

        return result;
    }

    public static string ResolveThemeAsset(CardFrameThemeDefinition theme, string relative)
    {
        var logical = Join(theme.ResourceRoot, relative);
        return AuraSharedResourceProtocol.ResolvePath(AuraToolsIds.ModId, logical);
    }

    public static string ResolveEffectAsset(string logical)
    {
        return AuraSharedResourceProtocol.ResolvePath(AuraToolsIds.ModId, logical);
    }

    private static string Join(string left, string right)
    {
        return (left ?? "").Trim().TrimEnd('/') + "/" + (right ?? "").Trim().TrimStart('/');
    }

    private static void Validate(CardVisualRegistryDocument value)
    {
        if (value.SchemaVersion != 4) throw new InvalidDataException("Unsupported card visual schemaVersion=" + value.SchemaVersion);
        if (value.Protocol.MinVersion > CurrentProtocolVersion
            || value.Protocol.PreferredVersion < value.Protocol.MinVersion)
            throw new InvalidDataException("Card visual protocol is incompatible.");
        if (!string.Equals(value.OwnerModId, AuraToolsIds.ModId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Card visual registry owner must be " + AuraToolsIds.ModId + ".");
        foreach (var theme in value.Themes)
        {
            if (string.IsNullOrWhiteSpace(theme.ThemeId) || string.IsNullOrWhiteSpace(theme.ResourceRoot))
                throw new InvalidDataException("Theme id or resource root is empty.");
            if (theme.Skins.Count == 0) throw new InvalidDataException("Theme has no skins: " + theme.ThemeId);
            if (theme.Skins.Any(skin => string.IsNullOrWhiteSpace(skin.SkinId) || string.IsNullOrWhiteSpace(skin.Frame)))
                throw new InvalidDataException("Theme skin id or frame is empty: " + theme.ThemeId);
            var skinIds = theme.Skins.Select(skin => skin.SkinId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (skinIds.Count != theme.Skins.Count) throw new InvalidDataException("Theme has duplicate skin ids: " + theme.ThemeId);
            if (theme.MappingPreset.Any(mapping => !skinIds.Contains(mapping.SkinId)))
                throw new InvalidDataException("Theme preset references an unknown skin: " + theme.ThemeId);
            if (theme.MappingPreset.Any(mapping => string.IsNullOrWhiteSpace(mapping.ContentOwnerModId)
                                                   || mapping.CardIds.Any(id => id.Contains("*"))))
                throw new InvalidDataException("Theme preset must use an owner and explicit card ids: " + theme.ThemeId);
        }
        if (value.Themes.Select(theme => theme.ThemeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Themes.Count)
            throw new InvalidDataException("Duplicate theme id.");
        if (value.Effects.Select(effect => effect.EffectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Effects.Count)
            throw new InvalidDataException("Duplicate effect id.");
        foreach (var effect in value.Effects)
        {
            if (string.IsNullOrWhiteSpace(effect.EffectId)
                || !string.Equals(effect.RendererId, "aura.card-visual.material-v2", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(effect.BundlePath)
                || string.IsNullOrWhiteSpace(effect.MaterialPath)
                || (!string.Equals(effect.TargetLayer, "face", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(effect.TargetLayer, "frame", StringComparison.OrdinalIgnoreCase))
                || string.Equals(effect.TargetLayer, "frame", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(effect.CoverageProfile, "native-frame-v1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.TargetLayer, "face", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(effect.CoverageProfile, "native-face-v1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Dynamic effect protocol is invalid: " + effect.EffectId);
            }
            if (effect.MappingPreset.Any(mapping => string.IsNullOrWhiteSpace(mapping.ContentOwnerModId)
                                                    || mapping.CardIds.Count == 0
                                                    || mapping.CardIds.Any(id => id.Contains("*"))))
                throw new InvalidDataException("Dynamic effect preset must use an owner and explicit card ids: " + effect.EffectId);
            if (effect.ExposedParameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
                                                     || string.IsNullOrWhiteSpace(pair.Value.DisplayName)
                                                     || pair.Value.Min > pair.Value.Max
                                                     || pair.Value.Step <= 0f
                                                     || pair.Value.Decimals < 0
                                                     || pair.Value.Decimals > 4))
                throw new InvalidDataException("Dynamic effect exposed parameter range is invalid: " + effect.EffectId);
        }
        var duplicateEffectCard = value.Effects
            .SelectMany(effect => effect.MappingPreset.SelectMany(mapping => mapping.QualifiedCardIds)
                .Select(card => new { effect.EffectId, Card = card }))
            .GroupBy(value => value.Card, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(value => value.EffectId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicateEffectCard != null)
            throw new InvalidDataException("Dynamic effect presets map one card to multiple effects: " + duplicateEffectCard.Key);
    }
}

public sealed class CardVisualRegistryDocument
{
    [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 4;
    [JsonProperty("ownerModId")] public string OwnerModId { get; set; } = "";
    [JsonProperty("protocol")] public CardVisualProtocolManifest Protocol { get; set; } = new();
    [JsonProperty("themes")] public List<CardFrameThemeDefinition> Themes { get; set; } = new();
    [JsonProperty("effects")] public List<CardDynamicEffectDefinition> Effects { get; set; } = new();

    public void Normalize()
    {
        OwnerModId = OwnerModId?.Trim() ?? "";
        Protocol ??= new CardVisualProtocolManifest();
        Protocol.Normalize();
        Themes ??= new List<CardFrameThemeDefinition>();
        Effects ??= new List<CardDynamicEffectDefinition>();
        Themes.ForEach(value => value.Normalize());
        Effects.ForEach(value => value.Normalize());
    }
}

public sealed class CardVisualProtocolManifest
{
    [JsonProperty("minVersion")] public int MinVersion { get; set; } = 1;
    [JsonProperty("preferredVersion")] public int PreferredVersion { get; set; } = 1;

    public void Normalize()
    {
        MinVersion = Math.Max(1, MinVersion);
        PreferredVersion = Math.Max(1, PreferredVersion);
    }
}

public sealed class CardFrameThemeDefinition
{
    [JsonProperty("themeId")] public string ThemeId { get; set; } = "";
    [JsonProperty("displayName")] public string DisplayName { get; set; } = "";
    [JsonProperty("resourceRoot")] public string ResourceRoot { get; set; } = "";
    [JsonProperty("presetVersion")] public int PresetVersion { get; set; }
    [JsonProperty("skins")] public List<CardFrameSkinDefinition> Skins { get; set; } = new();
    [JsonProperty("mappingPreset")] public List<CardFramePresetMapping> MappingPreset { get; set; } = new();
    [JsonProperty("enabled")] public bool Enabled { get; set; } = true;

    public void Normalize()
    {
        ThemeId = ThemeId?.Trim() ?? "";
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? ThemeId : DisplayName.Trim();
        ResourceRoot = AuraSharedPaths.NormalizeRelativePath(ResourceRoot);
        PresetVersion = Math.Max(1, PresetVersion);
        Skins ??= new List<CardFrameSkinDefinition>();
        MappingPreset ??= new List<CardFramePresetMapping>();
        Skins.ForEach(value => value.Normalize());
        MappingPreset.ForEach(value => value.Normalize());
    }
}

public sealed class CardFrameSkinDefinition
{
    [JsonProperty("skinId")] public string SkinId { get; set; } = "";
    [JsonProperty("displayName")] public string DisplayName { get; set; } = "";
    [JsonProperty("frame")] public string Frame { get; set; } = "";
    [JsonProperty("background")] public string Background { get; set; } = "";

    public void Normalize()
    {
        SkinId = SkinId?.Trim() ?? "";
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? SkinId : DisplayName.Trim();
        Frame = AuraSharedPaths.NormalizeRelativePath(Frame);
        Background = AuraSharedPaths.NormalizeRelativePath(Background);
    }
}

public sealed class CardFramePresetMapping
{
    [JsonProperty("skinId")] public string SkinId { get; set; } = "";
    [JsonProperty("contentOwnerModId")] public string ContentOwnerModId { get; set; } = "";
    [JsonProperty("cardPackIds")] public List<string> CardPackIds { get; set; } = new();
    [JsonProperty("cardIds")] public List<string> CardIds { get; set; } = new();

    public void Normalize()
    {
        SkinId = SkinId?.Trim() ?? "";
        ContentOwnerModId = ContentOwnerModId?.Trim() ?? "";
        CardPackIds = NormalizeIds(CardPackIds);
        CardIds = NormalizeIds(CardIds);
    }

    private static List<string> NormalizeIds(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>()).Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class CardDynamicEffectDefinition
{
    [JsonProperty("effectId")] public string EffectId { get; set; } = "";
    [JsonProperty("displayName")] public string DisplayName { get; set; } = "";
    [JsonProperty("rendererId")] public string RendererId { get; set; } = "";
    [JsonProperty("targetLayer")] public string TargetLayer { get; set; } = "";
    [JsonProperty("coverageProfile")] public string CoverageProfile { get; set; } = "";
    [JsonProperty("bundlePath")] public string BundlePath { get; set; } = "";
    [JsonProperty("materialPath")] public string MaterialPath { get; set; } = "";
    [JsonProperty("textures")] public Dictionary<string, string> Textures { get; set; } = new(StringComparer.Ordinal);
    [JsonProperty("floats")] public Dictionary<string, float> Floats { get; set; } = new(StringComparer.Ordinal);
    [JsonProperty("colors")] public Dictionary<string, string> Colors { get; set; } = new(StringComparer.Ordinal);
    [JsonProperty("exposedParameters")] public Dictionary<string, CardVisualParameterRange> ExposedParameters { get; set; } = new(StringComparer.Ordinal);
    [JsonProperty("presetVersion")] public int PresetVersion { get; set; } = 1;
    [JsonProperty("mappingPreset")] public List<CardDynamicEffectPresetMapping> MappingPreset { get; set; } = new();
    [JsonProperty("enabled")] public bool Enabled { get; set; } = true;

    public void Normalize()
    {
        EffectId = EffectId?.Trim() ?? "";
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? EffectId : DisplayName.Trim();
        RendererId = RendererId?.Trim() ?? "";
        TargetLayer = TargetLayer?.Trim() ?? "";
        CoverageProfile = CoverageProfile?.Trim() ?? "";
        BundlePath = BundlePath?.Trim() ?? "";
        MaterialPath = MaterialPath?.Trim() ?? "";
        Textures ??= new Dictionary<string, string>(StringComparer.Ordinal);
        Floats ??= new Dictionary<string, float>(StringComparer.Ordinal);
        Colors ??= new Dictionary<string, string>(StringComparer.Ordinal);
        ExposedParameters ??= new Dictionary<string, CardVisualParameterRange>(StringComparer.Ordinal);
        PresetVersion = Math.Max(1, PresetVersion);
        MappingPreset ??= new List<CardDynamicEffectPresetMapping>();
        MappingPreset.ForEach(value => value.Normalize());
    }
}

public sealed class CardDynamicEffectPresetMapping
{
    [JsonProperty("contentOwnerModId")] public string ContentOwnerModId { get; set; } = "";
    [JsonProperty("cardIds")] public List<string> CardIds { get; set; } = new();
    [JsonProperty("parameters")] public Dictionary<string, float> Parameters { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public IReadOnlyList<string> QualifiedCardIds => CardIds
        .Select(card => ContentOwnerModId + ":" + card)
        .ToArray();

    public void Normalize()
    {
        ContentOwnerModId = ContentOwnerModId?.Trim() ?? "";
        CardIds = (CardIds ?? new List<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Parameters = new Dictionary<string, float>(Parameters ?? new Dictionary<string, float>(), StringComparer.Ordinal);
    }
}

public sealed class CardVisualParameterRange
{
    [JsonProperty("displayName")] public string DisplayName { get; set; } = "";
    [JsonProperty("unit")] public string Unit { get; set; } = "";
    [JsonProperty("order")] public int Order { get; set; }
    [JsonProperty("min")] public float Min { get; set; }
    [JsonProperty("max")] public float Max { get; set; }
    [JsonProperty("step")] public float Step { get; set; } = 0.05f;
    [JsonProperty("decimals")] public int Decimals { get; set; } = 2;
}
