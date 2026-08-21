using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.CardVisual;

public static class AuraToolsCardVisualRuntime
{
    private static ModConfig? modConfig;
    private static IDisposable? presentationRegistration;
    private static bool initialized;

    public static void Initialize(ModConfig config)
    {
        modConfig = config;
        if (initialized) return;
        initialized = true;
        AuraToolsCardVisualRegistry.Load(config);
        presentationRegistration = AuraCardPresentationRuntime.Register(
            config,
            AuraToolsIds.ModId,
            "CardVisual",
            new AuraCardPresentationSubscription { Apply = Apply });
        AuraToolsConfigService.SubscribeModule(AuraToolModuleIds.CardVisual, Reconfigure);
        EnsureThemePresetState();
    }

    public static void ApplyModuleActivation(bool enabled)
    {
        if (!initialized || modConfig == null) return;
        AuraToolsConfigService.CardVisual.Enabled = enabled;
        if (enabled && presentationRegistration == null)
        {
            presentationRegistration = AuraCardPresentationRuntime.Register(
                modConfig,
                AuraToolsIds.ModId,
                "CardVisual",
                new AuraCardPresentationSubscription { Apply = Apply });
            EnsureThemePresetState();
        }
        else if (!enabled)
        {
            presentationRegistration?.Dispose();
            presentationRegistration = null;
            ClearActiveCombatCards();
        }
    }

    public static void Reconfigure()
    {
        AuraToolsConfigService.CardVisual.Normalize();
        EnsureThemePresetState();
        ReapplyActiveCombatCards("config");
    }

    public static void ResetThemePreset(string themeId)
    {
        var definition = AuraToolsCardVisualRegistry.Theme(themeId);
        if (definition == null || !AuraGameDataHostApi.IsNativeCatalogReady) return;
        var profile = new CardFrameThemeSettings { Enabled = true };
        ExpandPreset(definition, profile.Cards, replaceExisting: true);
        profile.Initialized = true;
        profile.AppliedPresetVersion = definition.PresetVersion;
        AuraToolsConfigService.CardVisual.Themes[definition.ThemeId] = profile;
        AuraToolsConfigService.SaveCardVisual();
        ReapplyActiveCombatCards("preset-reset");
    }

    public static int ApplyThemeSelection(string themeId, string skinId, string mode, string selector)
    {
        var theme = AuraToolsCardVisualRegistry.Theme(themeId);
        if (theme == null || AuraToolsCardVisualRegistry.Skin(themeId, skinId) == null) return 0;
        var profile = EnsureThemeProfile(theme);
        var cards = SelectCards(mode, selector).ToList();
        foreach (var card in cards)
        {
            RemoveCardFromOtherThemes(card, theme.ThemeId);
            profile.Cards[card] = skinId;
        }
        AuraToolsConfigService.SaveCardVisual();
        ReapplyActiveCombatCards("theme-batch");
        return cards.Count;
    }

    public static void RemoveThemeCard(string themeId, string qualifiedCardId)
    {
        if (AuraToolsConfigService.CardVisual.Themes.TryGetValue(themeId, out var profile))
        {
            profile.Cards.Remove(qualifiedCardId);
            AuraToolsConfigService.SaveCardVisual();
            ReapplyActiveCombatCards("theme-remove");
        }
    }

    public static void SetDynamicEffect(
        string qualifiedCardId,
        string effectId,
        IReadOnlyDictionary<string, float>? parameters = null)
    {
        var card = (qualifiedCardId ?? "").Trim();
        if (card.Length == 0) return;
        if (string.IsNullOrWhiteSpace(effectId))
        {
            AuraToolsConfigService.CardVisual.DynamicEffects.Remove(card);
        }
        else if (AuraToolsCardVisualRegistry.Effect(effectId) != null)
        {
            AuraToolsConfigService.CardVisual.DynamicEffects[card] = new CardDynamicEffectSettings
            {
                Enabled = true,
                EffectId = effectId.Trim(),
                Parameters = (parameters ?? new Dictionary<string, float>())
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };
        }
        AuraToolsConfigService.SaveCardVisual();
        ReapplyActiveCombatCards("effect-change");
    }

    public static IReadOnlyList<string> SelectCards(string mode, string selector)
    {
        var text = (selector ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        if (string.Equals(mode, "card", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = AuraGameDataHostApi.Resolve(DataType.Card, text);
            return new[] { snapshot == null ? QualifyCard(InferOwner(text), text) : QualifyCard(snapshot.OwnerModId, snapshot.Id) };
        }

        return AuraGameDataHostApi.Table(DataType.Card)
            .Where(card => string.Equals(mode, "pack", StringComparison.OrdinalIgnoreCase)
                ? Field(card.Fields, "PackBelong").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => string.Equals(value.Trim(), text, StringComparison.OrdinalIgnoreCase))
                : string.Equals(mode, "rarity", StringComparison.OrdinalIgnoreCase)
                  && string.Equals(Field(card.Fields, "Rarity"), text, StringComparison.OrdinalIgnoreCase))
            .Select(card => QualifyCard(card.OwnerModId, card.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureThemePresetState()
    {
        if (!AuraToolsConfigService.CardVisual.Enabled || !AuraGameDataHostApi.IsNativeCatalogReady) return;
        var changed = false;
        foreach (var theme in AuraToolsCardVisualRegistry.Themes.Where(value => value.Enabled))
        {
            var profile = EnsureThemeProfile(theme);
            if (profile.Initialized) continue;
            ExpandPreset(theme, profile.Cards, replaceExisting: false);
            profile.Initialized = true;
            profile.AppliedPresetVersion = theme.PresetVersion;
            changed = true;
        }
        if (changed) AuraToolsConfigService.SaveCardVisual();
    }

    private static CardFrameThemeSettings EnsureThemeProfile(CardFrameThemeDefinition theme)
    {
        if (!AuraToolsConfigService.CardVisual.Themes.TryGetValue(theme.ThemeId, out var profile) || profile == null)
        {
            profile = new CardFrameThemeSettings();
            AuraToolsConfigService.CardVisual.Themes[theme.ThemeId] = profile;
        }
        return profile;
    }

    private static void ExpandPreset(
        CardFrameThemeDefinition theme,
        IDictionary<string, string> target,
        bool replaceExisting)
    {
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in theme.MappingPreset)
        {
            foreach (var cardId in mapping.CardIds)
            {
                AddPreset(staged, QualifyCard(mapping.ContentOwnerModId, cardId), mapping.SkinId, theme.ThemeId);
            }
            foreach (var card in AuraGameDataHostApi.Table(DataType.Card)
                         .Where(card => string.IsNullOrWhiteSpace(mapping.ContentOwnerModId)
                                        || string.Equals(card.OwnerModId, mapping.ContentOwnerModId, StringComparison.OrdinalIgnoreCase))
                         .Where(card => mapping.CardPackIds.Any(packId => BelongsToPack(card.Fields, packId))))
            {
                AddPreset(staged, QualifyCard(card.OwnerModId, card.Id), mapping.SkinId, theme.ThemeId);
            }
        }
        foreach (var pair in staged)
        {
            if (!replaceExisting && IsAssignedByAnyTheme(pair.Key)) continue;
            RemoveCardFromOtherThemes(pair.Key, theme.ThemeId);
            target[pair.Key] = pair.Value;
        }
    }

    private static void AddPreset(IDictionary<string, string> staged, string card, string skin, string theme)
    {
        if (staged.TryGetValue(card, out var existing) && !string.Equals(existing, skin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Theme preset maps one card to multiple skins: " + theme + " / " + card);
        staged[card] = skin;
    }

    private static bool IsAssignedByAnyTheme(string card)
    {
        return AuraToolsConfigService.CardVisual.Themes.Values.Any(value => value.Cards.ContainsKey(card));
    }

    private static void RemoveCardFromOtherThemes(string card, string keepTheme)
    {
        foreach (var pair in AuraToolsConfigService.CardVisual.Themes)
        {
            if (!string.Equals(pair.Key, keepTheme, StringComparison.OrdinalIgnoreCase)) pair.Value.Cards.Remove(card);
        }
    }

    private static void Apply(AuraCardPresentationContext context)
    {
        if (!AuraToolsConfigService.CardVisual.Enabled || context.Config == null || context.Root == null) return;
        EnsureThemePresetState();
        var cardId = ReadId(context.Config);
        if (cardId.Length == 0) return;
        var definition = AuraGameDataHostApi.Resolve(DataType.Card, cardId);
        var qualifiedCard = QualifyCard(definition?.OwnerModId ?? InferOwner(cardId), definition?.Id ?? cardId);
        var root = FindVisualRoot(context.Root);
        if (root == null) return;
        var marker = root.GetComponent<AuraToolsCardVisualMarker>() ?? root.gameObject.AddComponent<AuraToolsCardVisualMarker>();

        CardFrameThemeDefinition? theme = null;
        CardFrameSkinDefinition? skin = null;
        foreach (var pair in AuraToolsConfigService.CardVisual.Themes)
        {
            if (!pair.Value.Enabled || !pair.Value.Cards.TryGetValue(qualifiedCard, out var skinId)) continue;
            theme = AuraToolsCardVisualRegistry.Theme(pair.Key);
            skin = theme == null ? null : AuraToolsCardVisualRegistry.Skin(theme.ThemeId, skinId);
            if (skin != null) break;
        }

        marker.ApplySkin(theme, skin);
        AuraToolsConfigService.CardVisual.DynamicEffects.TryGetValue(qualifiedCard, out var effectSettings);
        var effect = effectSettings?.Enabled == true ? AuraToolsCardVisualRegistry.Effect(effectSettings.EffectId) : null;
        marker.ApplyEffect(effect, effectSettings);
    }

    private static Transform? FindVisualRoot(Transform root)
    {
        if (root.Find("Front/background") != null || root.Find("Front/FrontBack") != null) return root;
        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        var scanned = 0;
        while (queue.Count > 0 && scanned++ < 96)
        {
            var value = queue.Dequeue();
            if (value.Find("Front/background") != null || value.Find("Front/FrontBack") != null) return value;
            for (var index = 0; index < value.childCount; index++) queue.Enqueue(value.GetChild(index));
        }
        return null;
    }

    private static void ReapplyActiveCombatCards(string source)
    {
        var snapshot = AuraCombatCardZoneSnapshot.Capture(null, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true
        });
        foreach (var card in snapshot.Cards.Where(value => value.Config != null && value.Root != null))
        {
            Apply(new AuraCardPresentationContext
            {
                Root = card.Root,
                Config = card.Config,
                Card = card.Card,
                Source = "AuraTools.CardVisual." + source,
                Surface = AuraCardPresentationSurface.CombatCard
            });
        }
    }

    private static void ClearActiveCombatCards()
    {
        var snapshot = AuraCombatCardZoneSnapshot.Capture(null, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true
        });
        foreach (var root in snapshot.Cards.Select(value => value.Root).Where(value => value != null))
            root!.GetComponent<AuraToolsCardVisualMarker>()?.ClearAll();
    }

    private static string ReadId(IDataConfig config)
    {
        return config.data != null && config.data.TryGetValue("Id", out var id) ? id?.Trim() ?? "" : "";
    }

    private static string Field(IReadOnlyDictionary<string, string> fields, string name)
    {
        return fields.TryGetValue(name, out var value) ? value?.Trim() ?? "" : "";
    }

    private static bool BelongsToPack(IReadOnlyDictionary<string, string> fields, string packId)
    {
        return Field(fields, "PackBelong")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(value => string.Equals(value.Trim(), packId, StringComparison.OrdinalIgnoreCase));
    }

    private static string InferOwner(string cardId)
    {
        var id = (cardId ?? "").Trim();
        var separator = id.IndexOf('_');
        return separator > 0 ? id.Substring(0, separator) : "Witch";
    }

    private static string QualifyCard(string owner, string cardId)
    {
        return (string.IsNullOrWhiteSpace(owner) ? InferOwner(cardId) : owner.Trim()) + ":" + (cardId ?? "").Trim();
    }
}

internal sealed class AuraToolsCardVisualMarker : MonoBehaviour
{
    private Sprite? originalFrame;
    private Sprite? originalBackground;
    private Texture? originalFrameTexture;
    private Texture? originalBackgroundTexture;
    private Sprite? appliedFrame;
    private Sprite? appliedBackground;
    private Texture? appliedFrameTexture;
    private Texture? appliedBackgroundTexture;
    private bool captured;
    private Image? effectOverlay;
    private Material? effectMaterial;
    private string skinSignature = "";
    private string effectSignature = "";

    public void ApplySkin(CardFrameThemeDefinition? theme, CardFrameSkinDefinition? skin)
    {
        CaptureNativeState();
        var signature = theme == null || skin == null ? "" : theme.ThemeId + ":" + skin.SkinId;
        if (signature != skinSignature)
        {
            RestoreSkin();
            CaptureNativeState();
            skinSignature = signature;
        }
        if (theme == null || skin == null) return;
        var frame = LoadSprite(AuraToolsCardVisualRegistry.ResolveThemeAsset(theme, skin.Frame));
        var background = string.IsNullOrWhiteSpace(skin.Background)
            ? null
            : LoadSprite(AuraToolsCardVisualRegistry.ResolveThemeAsset(theme, skin.Background));
        var frameImage = transform.Find("Front/FrontBack")?.GetComponent<Image>();
        var backgroundImage = transform.Find("Front/background")?.GetComponent<Image>();
        if (frameImage != null && frame != null) frameImage.sprite = appliedFrame = frame;
        if (backgroundImage != null && background != null) backgroundImage.sprite = appliedBackground = background;
        var frameMesh = transform.Find("Front/FrontBack")?.GetComponent<MeshRenderer>();
        var backgroundMesh = transform.Find("Front/background")?.GetComponent<MeshRenderer>();
        if (frameMesh?.material != null && frame != null) frameMesh.material.mainTexture = appliedFrameTexture = frame.texture;
        if (backgroundMesh?.material != null && background != null) backgroundMesh.material.mainTexture = appliedBackgroundTexture = background.texture;
    }

    public void ApplyEffect(CardDynamicEffectDefinition? effect, CardDynamicEffectSettings? settings)
    {
        var signature = effect == null ? "" : effect.EffectId + ":" + string.Join(";", settings?.Parameters.OrderBy(value => value.Key).Select(value => value.Key + "=" + value.Value) ?? Enumerable.Empty<string>());
        var source = effect == null
            ? null
            : string.Equals(effect.TargetLayer, "face", StringComparison.OrdinalIgnoreCase)
                ? transform.Find("Front/background")?.GetComponent<Image>()
                : transform.Find("Front/FrontBack")?.GetComponent<Image>();
        if (signature == effectSignature
            && effectOverlay != null
            && source?.sprite != null
            && effectOverlay.sprite == source.sprite)
        {
            return;
        }
        ClearEffect();
        effectSignature = signature;
        if (effect == null) return;
        if (source == null || source.sprite == null) return;
        effectMaterial = AuraToolsCardVisualAssets.CreateMaterial(effect, settings);
        if (effectMaterial == null) return;
        var go = new GameObject("AuraTools_CardVisualEffect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = source.gameObject.layer;
        go.transform.SetParent(source.transform.parent, false);
        var sourceRect = source.transform as RectTransform;
        var rect = go.GetComponent<RectTransform>();
        if (sourceRect != null)
        {
            rect.anchorMin = sourceRect.anchorMin; rect.anchorMax = sourceRect.anchorMax;
            rect.anchoredPosition = sourceRect.anchoredPosition; rect.sizeDelta = sourceRect.sizeDelta;
            rect.pivot = sourceRect.pivot; rect.localScale = sourceRect.localScale; rect.localRotation = sourceRect.localRotation;
        }
        effectOverlay = go.GetComponent<Image>();
        effectOverlay.sprite = source.sprite;
        effectOverlay.material = effectMaterial;
        effectOverlay.color = Color.white;
        effectOverlay.raycastTarget = false;
        effectOverlay.maskable = source.maskable;
        go.transform.SetSiblingIndex(Math.Min(source.transform.GetSiblingIndex() + 1, go.transform.parent.childCount - 1));
    }

    public void ClearAll()
    {
        RestoreSkin();
        ClearEffect();
        skinSignature = "";
        effectSignature = "";
    }

    private void CaptureNativeState()
    {
        var frameImage = transform.Find("Front/FrontBack")?.GetComponent<Image>();
        var backgroundImage = transform.Find("Front/background")?.GetComponent<Image>();
        var frameTexture = transform.Find("Front/FrontBack")?.GetComponent<MeshRenderer>()?.material?.mainTexture;
        var backgroundTexture = transform.Find("Front/background")?.GetComponent<MeshRenderer>()?.material?.mainTexture;
        if (!captured || frameImage?.sprite != appliedFrame) originalFrame = frameImage?.sprite;
        if (!captured || backgroundImage?.sprite != appliedBackground) originalBackground = backgroundImage?.sprite;
        if (!captured || frameTexture != appliedFrameTexture) originalFrameTexture = frameTexture;
        if (!captured || backgroundTexture != appliedBackgroundTexture) originalBackgroundTexture = backgroundTexture;
        captured = true;
    }

    private void RestoreSkin()
    {
        if (!captured) return;
        var frameImage = transform.Find("Front/FrontBack")?.GetComponent<Image>();
        var backgroundImage = transform.Find("Front/background")?.GetComponent<Image>();
        if (frameImage != null && frameImage.sprite == appliedFrame) frameImage.sprite = originalFrame;
        if (backgroundImage != null && backgroundImage.sprite == appliedBackground) backgroundImage.sprite = originalBackground;
        var frameMesh = transform.Find("Front/FrontBack")?.GetComponent<MeshRenderer>();
        var backgroundMesh = transform.Find("Front/background")?.GetComponent<MeshRenderer>();
        if (frameMesh?.material != null && frameMesh.material.mainTexture == appliedFrameTexture) frameMesh.material.mainTexture = originalFrameTexture;
        if (backgroundMesh?.material != null && backgroundMesh.material.mainTexture == appliedBackgroundTexture) backgroundMesh.material.mainTexture = originalBackgroundTexture;
        appliedFrame = null;
        appliedBackground = null;
        appliedFrameTexture = null;
        appliedBackgroundTexture = null;
    }

    private void ClearEffect()
    {
        if (effectOverlay != null) Object.Destroy(effectOverlay.gameObject);
        if (effectMaterial != null) Object.Destroy(effectMaterial);
        effectOverlay = null;
        effectMaterial = null;
    }

    private static Sprite? LoadSprite(string path) => AuraToolsCardVisualAssets.LoadSprite(path);
    private void OnDestroy() => ClearEffect();
}

internal static class AuraToolsCardVisualAssets
{
    private static readonly Dictionary<string, Sprite?> Sprites = new(StringComparer.OrdinalIgnoreCase);

    public static Sprite? LoadSprite(string path)
    {
        if (Sprites.TryGetValue(path, out var cached)) return cached;
        try
        {
            if (!File.Exists(path)) return Sprites[path] = null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadTextureBytes(texture, File.ReadAllBytes(path)))
            {
                Object.Destroy(texture);
                return Sprites[path] = null;
            }
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = Path.GetFileNameWithoutExtension(path);
            return Sprites[path] = sprite;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[CardVisual] sprite load failed: " + path + " -> " + ex.Message);
            return Sprites[path] = null;
        }
    }

    private static bool LoadTextureBytes(Texture2D texture, byte[] payload)
    {
        var method = typeof(ImageConversion).GetMethod(
            "LoadImage",
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
        return method?.Invoke(null, new object[] { texture, payload, false }) is true;
    }

    public static Material? CreateMaterial(CardDynamicEffectDefinition effect, CardDynamicEffectSettings? settings)
    {
        try
        {
            var source = AuraToolsVisualBundleRuntime.LoadAsset<Material>(effect.BundlePath, effect.MaterialPath);
            if (source == null) return null;
            var material = new Material(source) { name = "AuraTools_CardVisual_" + effect.EffectId };
            foreach (var pair in effect.Floats)
                if (material.HasProperty(pair.Key)) material.SetFloat(pair.Key, pair.Value);
            foreach (var pair in effect.Colors)
                if (material.HasProperty(pair.Key) && ColorUtility.TryParseHtmlString(pair.Value, out var color)) material.SetColor(pair.Key, color);
            foreach (var pair in effect.Textures)
            {
                if (!material.HasProperty(pair.Key)) continue;
                var sprite = LoadSprite(AuraToolsCardVisualRegistry.ResolveEffectAsset(pair.Value));
                if (sprite != null) material.SetTexture(pair.Key, sprite.texture);
            }
            foreach (var pair in settings?.Parameters ?? new Dictionary<string, float>())
            {
                if (!effect.ExposedParameters.TryGetValue(pair.Key, out var range) || !material.HasProperty(pair.Key)) continue;
                material.SetFloat(pair.Key, Mathf.Clamp(pair.Value, Math.Min(range.Min, range.Max), Math.Max(range.Min, range.Max)));
            }
            return material;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[CardVisual] material load failed: " + effect.EffectId + " -> " + ex.Message);
            return null;
        }
    }
}
