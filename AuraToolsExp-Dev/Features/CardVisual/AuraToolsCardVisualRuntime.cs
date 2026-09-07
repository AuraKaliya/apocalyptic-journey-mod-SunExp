using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.CardVisual;

public static class AuraToolsCardVisualRuntime
{
    internal const string ReplayThemeIdKey = "AuraReplay.CardVisual.ThemeId";
    internal const string ReplaySkinIdKey = "AuraReplay.CardVisual.SkinId";
    internal const string ReplayEffectIdKey = "AuraReplay.CardVisual.EffectId";
    internal const string ReplayEffectParametersKey = "AuraReplay.CardVisual.EffectParameters";
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
            new AuraCardPresentationSubscription { Priority = 1000, Apply = Apply, Reset = Reset });
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
                new AuraCardPresentationSubscription { Priority = 1000, Apply = Apply, Reset = Reset });
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
            // An empty effect is an explicit local tombstone. Without this
            // record a shipped default would immediately become effective
            // again and the player could never turn it off.
            AuraToolsConfigService.CardVisual.DynamicEffectOverrides[card] = new CardDynamicEffectSettings
            {
                Enabled = false,
                EffectId = ""
            };
        }
        else if (AuraToolsCardVisualRegistry.Effect(effectId) != null)
        {
            AuraToolsConfigService.CardVisual.DynamicEffectOverrides[card] = new CardDynamicEffectSettings
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

    public static int ApplyDynamicEffectSelection(
        IEnumerable<string> qualifiedCardIds,
        string effectId,
        IReadOnlyDictionary<string, float>? parameters = null)
    {
        var effect = string.IsNullOrWhiteSpace(effectId)
            ? null
            : AuraToolsCardVisualRegistry.Effect(effectId);
        if (!string.IsNullOrWhiteSpace(effectId) && effect == null)
        {
            return 0;
        }

        var cards = (qualifiedCardIds ?? Array.Empty<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var card in cards)
        {
            AuraToolsConfigService.CardVisual.DynamicEffectOverrides[card] = new CardDynamicEffectSettings
            {
                Enabled = effect != null,
                EffectId = effect?.EffectId ?? "",
                Parameters = effect == null
                    ? new Dictionary<string, float>(StringComparer.Ordinal)
                    : (parameters ?? new Dictionary<string, float>())
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };
        }

        if (cards.Length == 0)
        {
            return 0;
        }

        AuraToolsConfigService.SaveCardVisual();
        ReapplyActiveCombatCards("effect-batch");
        return cards.Length;
    }

    public static void RestoreDynamicEffectDefault(string qualifiedCardId)
    {
        var card = (qualifiedCardId ?? "").Trim();
        if (card.Length == 0) return;
        if (!AuraToolsConfigService.CardVisual.DynamicEffectOverrides.Remove(card)) return;
        AuraToolsConfigService.SaveCardVisual();
        ReapplyActiveCombatCards("effect-restore-default");
    }

    public static int RestoreDynamicEffectDefaults(IEnumerable<string> qualifiedCardIds)
    {
        var removed = 0;
        foreach (var card in (qualifiedCardIds ?? Array.Empty<string>())
                     .Select(value => (value ?? "").Trim())
                     .Where(value => value.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (AuraToolsConfigService.CardVisual.DynamicEffectOverrides.Remove(card)) removed++;
        }

        if (removed == 0) return 0;
        AuraToolsConfigService.SaveCardVisual();
        ReapplyActiveCombatCards("effect-restore-default-batch");
        return removed;
    }

    public static IReadOnlyDictionary<string, CardDynamicEffectSettings> EffectiveDynamicEffects()
    {
        var effective = AuraToolsCardVisualRegistry.DefaultEffects()
            .ToDictionary(pair => pair.Key, pair => CloneEffect(pair.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in AuraToolsConfigService.CardVisual.DynamicEffectOverrides)
        {
            if (pair.Value.Enabled && AuraToolsCardVisualRegistry.Effect(pair.Value.EffectId) != null)
            {
                effective[pair.Key] = CloneEffect(pair.Value);
            }
            else
            {
                effective.Remove(pair.Key);
            }
        }

        return effective;
    }

    internal static IReadOnlyDictionary<string, string> CaptureReplaySnapshot(IDataConfig config)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (config == null || !AuraToolsConfigService.CardVisual.Enabled) return result;
        EnsureThemePresetState();
        var qualifiedCard = QualifiedCard(config);
        foreach (var pair in AuraToolsConfigService.CardVisual.Themes)
        {
            if (!pair.Value.Enabled || !pair.Value.Cards.TryGetValue(qualifiedCard, out var skinId)) continue;
            if (AuraToolsCardVisualRegistry.Skin(pair.Key, skinId) == null) continue;
            result[ReplayThemeIdKey] = pair.Key;
            result[ReplaySkinIdKey] = skinId;
            break;
        }
        var effect = ResolveDynamicEffect(qualifiedCard);
        if (effect?.Enabled == true && AuraToolsCardVisualRegistry.Effect(effect.EffectId) != null)
        {
            result[ReplayEffectIdKey] = effect.EffectId;
            result[ReplayEffectParametersKey] = AuraSharedJson.SerializeCompact(effect.Parameters);
        }
        return result;
    }

    public static IReadOnlyList<string> SelectCards(string mode, string selector)
    {
        var text = (selector ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        if (string.Equals(mode, "card", StringComparison.OrdinalIgnoreCase))
        {
            var identity = AuraToolsContentIdentity.Parse(text);
            var snapshot = AuraGameDataHostApi.Table(DataType.Card)
                .FirstOrDefault(card => string.Equals(
                                            card.Id,
                                            identity.ContentId,
                                            StringComparison.OrdinalIgnoreCase)
                                        && (!identity.IsQualified
                                            || string.Equals(
                                                card.OwnerModId,
                                                identity.OwnerModId,
                                                StringComparison.OrdinalIgnoreCase)));
            return snapshot == null
                ? Array.Empty<string>()
                : new[] { QualifyCard(snapshot.OwnerModId, snapshot.Id) };
        }

        var target = AuraToolsContentIdentity.Parse(text);
        return AuraGameDataHostApi.Table(DataType.Card)
            .Where(card => string.Equals(mode, "pack", StringComparison.OrdinalIgnoreCase)
                ? (!target.IsQualified
                   || string.Equals(
                       card.OwnerModId,
                       target.OwnerModId,
                       StringComparison.OrdinalIgnoreCase))
                  && Field(card.Fields, "PackBelong").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => string.Equals(value.Trim(), target.ContentId, StringComparison.OrdinalIgnoreCase))
                : string.Equals(mode, "rarity", StringComparison.OrdinalIgnoreCase)
                  && string.Equals(Field(card.Fields, "Rarity"), target.ContentId, StringComparison.OrdinalIgnoreCase))
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
        var qualifiedCard = QualifiedCard(context.Config);
        var root = FindVisualRoot(context.Root, context.Surface);
        if (root == null) return;
        var marker = root.GetComponent<AuraToolsCardVisualMarker>() ?? root.gameObject.AddComponent<AuraToolsCardVisualMarker>();
        marker.BindDiagnostic(qualifiedCard, context.Surface);

        CardFrameThemeDefinition? theme = null;
        CardFrameSkinDefinition? skin = null;
        var replayThemeId = ReadRuntimeValue(context.Config, ReplayThemeIdKey);
        var replaySkinId = ReadRuntimeValue(context.Config, ReplaySkinIdKey);
        if (replayThemeId.Length > 0 && replaySkinId.Length > 0)
        {
            theme = AuraToolsCardVisualRegistry.Theme(replayThemeId);
            skin = theme == null ? null : AuraToolsCardVisualRegistry.Skin(theme.ThemeId, replaySkinId);
        }
        foreach (var pair in theme == null
                     ? AuraToolsConfigService.CardVisual.Themes
                     : new Dictionary<string, CardFrameThemeSettings>())
        {
            if (!pair.Value.Enabled || !pair.Value.Cards.TryGetValue(qualifiedCard, out var skinId)) continue;
            theme = AuraToolsCardVisualRegistry.Theme(pair.Key);
            skin = theme == null ? null : AuraToolsCardVisualRegistry.Skin(theme.ThemeId, skinId);
            if (skin != null) break;
        }

        marker.ApplySkin(theme, skin);
        var effectSettings = ReplayEffect(context.Config) ?? ResolveDynamicEffect(qualifiedCard);
        var effect = effectSettings?.Enabled == true ? AuraToolsCardVisualRegistry.Effect(effectSettings.EffectId) : null;
        marker.ApplyEffect(effect, effectSettings);
    }

    private static void Reset(AuraCardPresentationContext context)
    {
        if (context.Root == null) return;
        var root = FindVisualRoot(context.Root, context.Surface) ?? context.Root;
        var marker = root.GetComponent<AuraToolsCardVisualMarker>();
        if (context.ResetKind == AuraCardPresentationResetKind.NativeExit) marker?.PrepareNativeExit();
        else marker?.ClearAll();
    }

    private static CardDynamicEffectSettings? ResolveDynamicEffect(string qualifiedCard)
    {
        if (AuraToolsConfigService.CardVisual.DynamicEffectOverrides.TryGetValue(qualifiedCard, out var local))
        {
            return local.Enabled ? local : null;
        }

        return AuraToolsCardVisualRegistry.TryGetDefaultEffect(qualifiedCard, out var shipped)
            ? shipped
            : null;
    }

    private static CardDynamicEffectSettings CloneEffect(CardDynamicEffectSettings value)
    {
        return new CardDynamicEffectSettings
        {
            Enabled = value.Enabled,
            EffectId = value.EffectId,
            Parameters = new Dictionary<string, float>(value.Parameters, StringComparer.Ordinal)
        };
    }

    private static Transform? FindVisualRoot(Transform root, AuraCardPresentationSurface surface)
    {
        if (root.Find("Front/background") != null || root.Find("Front/FrontBack") != null) return root;
        if (surface == AuraCardPresentationSurface.CombatCard) return null;
        var cardRoot = root.Find("CardItem");
        if (cardRoot != null
            && (cardRoot.Find("Front/background") != null || cardRoot.Find("Front/FrontBack") != null))
            return cardRoot;
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

    private static string ReadRuntimeValue(IDataConfig config, string key)
    {
        if (config.Vars != null && config.Vars.TryGetValue(key, out var runtime)) return runtime?.Trim() ?? "";
        if (config.data != null && config.data.TryGetValue(key, out var stored)) return stored?.Trim() ?? "";
        return "";
    }

    private static string QualifiedCard(IDataConfig config)
    {
        var cardId = ReadId(config);
        var definition = AuraGameDataHostApi.Resolve(DataType.Card, cardId);
        return QualifyCard(definition?.OwnerModId ?? InferOwner(cardId), definition?.Id ?? cardId);
    }

    private static CardDynamicEffectSettings? ReplayEffect(IDataConfig config)
    {
        var effectId = ReadRuntimeValue(config, ReplayEffectIdKey);
        if (effectId.Length == 0 || AuraToolsCardVisualRegistry.Effect(effectId) == null) return null;
        var parametersJson = ReadRuntimeValue(config, ReplayEffectParametersKey);
        Dictionary<string, float>? parameters;
        try
        {
            parameters = string.IsNullOrWhiteSpace(parametersJson)
                ? new Dictionary<string, float>(StringComparer.Ordinal)
                : AuraSharedJson.Deserialize<Dictionary<string, float>>(parametersJson);
        }
        catch
        {
            parameters = new Dictionary<string, float>(StringComparer.Ordinal);
        }
        return new CardDynamicEffectSettings
        {
            Enabled = true,
            EffectId = effectId,
            Parameters = new Dictionary<string, float>(parameters ?? new Dictionary<string, float>(), StringComparer.Ordinal)
        };
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
    private CardVisualRenderTargetKind skinTargetKind;
    private Image? effectImageTarget;
    private MeshRenderer? effectMeshTarget;
    private Material? originalEffectImageMaterial;
    private Material? originalEffectMeshMaterial;
    private Material? effectMaterial;
    private AuraPresentationMaterialLease? effectMaterialLease;
    private string skinSignature = "";
    private string effectSignature = "";
    private string diagnosticCardId = "";
    private AuraCardPresentationSurface diagnosticSurface;

    public void BindDiagnostic(string cardId, AuraCardPresentationSurface surface)
    {
        diagnosticCardId = cardId ?? "";
        diagnosticSurface = surface;
    }

    public void ApplySkin(CardFrameThemeDefinition? theme, CardFrameSkinDefinition? skin)
    {
        var frameNode = transform.Find("Front/FrontBack");
        var targetKind = ResolveNativeTargetKind(frameNode, "skin");
        var signature = theme == null || skin == null
            ? ""
            : theme.ThemeId + ":" + skin.SkinId + ":" + targetKind;
        if (signature != skinSignature || targetKind != skinTargetKind)
        {
            RestoreSkin();
            ResetSkinCapture();
            skinTargetKind = targetKind;
            skinSignature = signature;
        }
        CaptureNativeState();
        if (theme == null || skin == null) return;
        if (targetKind == CardVisualRenderTargetKind.None)
        {
            WarnMissingNativeTarget("skin", frameNode);
            return;
        }
        var frame = LoadSprite(AuraToolsCardVisualRegistry.ResolveThemeAsset(theme, skin.Frame));
        var background = string.IsNullOrWhiteSpace(skin.Background)
            ? null
            : LoadSprite(AuraToolsCardVisualRegistry.ResolveThemeAsset(theme, skin.Background));
        var frameImage = transform.Find("Front/FrontBack")?.GetComponent<Image>();
        var backgroundImage = transform.Find("Front/background")?.GetComponent<Image>();
        var frameMesh = transform.Find("Front/FrontBack")?.GetComponent<MeshRenderer>();
        var backgroundMesh = transform.Find("Front/background")?.GetComponent<MeshRenderer>();
        if (targetKind == CardVisualRenderTargetKind.Image)
        {
            if (frameImage != null && frame != null) frameImage.sprite = appliedFrame = frame;
            if (backgroundImage != null && background != null) backgroundImage.sprite = appliedBackground = background;
            return;
        }
        if (targetKind == CardVisualRenderTargetKind.Mesh && frameMesh != null && frame != null)
        {
            appliedFrameTexture = frame.texture;
            SetMeshTexture(frameMesh, frame.texture);
        }
        if (targetKind == CardVisualRenderTargetKind.Mesh && backgroundMesh != null && background != null)
        {
            appliedBackgroundTexture = background.texture;
            SetMeshTexture(backgroundMesh, background.texture);
        }
    }

    public void ApplyEffect(CardDynamicEffectDefinition? effect, CardDynamicEffectSettings? settings)
    {
        if (effect == null)
        {
            if (ClearEffect())
            {
                effectSignature = "";
            }
            return;
        }
        var node = string.Equals(effect.TargetLayer, "face", StringComparison.OrdinalIgnoreCase)
            ? transform.Find("Front/background")
            : transform.Find("Front/FrontBack");
        var image = node?.GetComponent<Image>();
        var mesh = node?.GetComponent<MeshRenderer>();
        var targetKind = ResolveNativeTargetKind(node, "effect");
        var useMesh = targetKind == CardVisualRenderTargetKind.Mesh;
        var runtimeTexture = useMesh
            ? (SameUnityObject(effectMeshTarget, mesh) && originalEffectMeshMaterial != null
                ? originalEffectMeshMaterial.mainTexture
                : mesh?.material?.mainTexture)
            : image?.sprite?.texture;
        var signature = effect.EffectId
                        + ":" + effect.TargetLayer
                        + ":" + effect.CoverageProfile
                        + ":" + targetKind
                        + ":" + (runtimeTexture == null ? 0 : runtimeTexture.GetInstanceID())
                        + ":" + string.Join(";", settings?.Parameters
                            .OrderBy(value => value.Key)
                            .Select(value => value.Key + "=" + value.Value)
                            ?? Enumerable.Empty<string>());
        if (signature == effectSignature && effectMaterial != null)
        {
            var stillAttached = effectMaterialLease?.OwnsCurrent == true;
            if (stillAttached)
            {
                AuraToolsCardVisualAssets.ApplyRuntimeTexture(effectMaterial, runtimeTexture);
                return;
            }
        }

        if (!ClearEffect())
        {
            return;
        }
        effectSignature = signature;
        if (targetKind == CardVisualRenderTargetKind.None)
        {
            WarnMissingNativeTarget("effect", node);
            return;
        }
        if (node == null || runtimeTexture == null) return;
        effectMaterial = AuraToolsCardVisualAssets.CreateMaterial(effect, settings, runtimeTexture, useMesh);
        if (effectMaterial == null) return;

        if (targetKind == CardVisualRenderTargetKind.Mesh && mesh != null)
        {
            originalEffectMeshMaterial = mesh.material;
            effectMeshTarget = mesh;
            if (!AcquireEffectMaterial(mesh, effectMaterial))
            {
                effectMeshTarget = null;
                originalEffectMeshMaterial = null;
                Object.Destroy(effectMaterial);
                effectMaterial = null;
                effectSignature = "";
            }
            return;
        }

        if (targetKind == CardVisualRenderTargetKind.Image && image != null)
        {
            originalEffectImageMaterial = image.material;
            effectImageTarget = image;
            if (!AcquireEffectMaterial(image, effectMaterial))
            {
                effectImageTarget = null;
                originalEffectImageMaterial = null;
                Object.Destroy(effectMaterial);
                effectMaterial = null;
                effectSignature = "";
            }
            return;
        }

        Object.Destroy(effectMaterial);
        effectMaterial = null;
    }

    public void ClearAll()
    {
        RestoreSkin();
        ClearEffect();
        ResetSkinCapture();
        skinSignature = "";
        effectSignature = "";
    }

    public void PrepareNativeExit()
    {
        // Native burn copies the current card-face textures. Release only the
        // temporary effect material and preserve the selected static skin.
        ClearEffect();
        effectSignature = "";
    }

    private void CaptureNativeState()
    {
        if (skinTargetKind == CardVisualRenderTargetKind.None) return;
        var frameImage = transform.Find("Front/FrontBack")?.GetComponent<Image>();
        var backgroundImage = transform.Find("Front/background")?.GetComponent<Image>();
        var frameTexture = ReadMeshTexture(transform.Find("Front/FrontBack")?.GetComponent<MeshRenderer>());
        var backgroundTexture = ReadMeshTexture(transform.Find("Front/background")?.GetComponent<MeshRenderer>());
        if (skinTargetKind == CardVisualRenderTargetKind.Image)
        {
            if (!captured || frameImage?.sprite != appliedFrame) originalFrame = frameImage?.sprite;
            if (!captured || backgroundImage?.sprite != appliedBackground) originalBackground = backgroundImage?.sprite;
        }
        else if (skinTargetKind == CardVisualRenderTargetKind.Mesh)
        {
            if (!captured || frameTexture != appliedFrameTexture) originalFrameTexture = frameTexture;
            if (!captured || backgroundTexture != appliedBackgroundTexture) originalBackgroundTexture = backgroundTexture;
        }
        captured = true;
    }

    private void RestoreSkin()
    {
        if (!captured) return;
        var frameImage = transform.Find("Front/FrontBack")?.GetComponent<Image>();
        var backgroundImage = transform.Find("Front/background")?.GetComponent<Image>();
        var frameMesh = transform.Find("Front/FrontBack")?.GetComponent<MeshRenderer>();
        var backgroundMesh = transform.Find("Front/background")?.GetComponent<MeshRenderer>();
        if (skinTargetKind == CardVisualRenderTargetKind.Image)
        {
            if (frameImage != null && frameImage.sprite == appliedFrame) frameImage.sprite = originalFrame;
            if (backgroundImage != null && backgroundImage.sprite == appliedBackground) backgroundImage.sprite = originalBackground;
        }
        else if (skinTargetKind == CardVisualRenderTargetKind.Mesh)
        {
            RestoreMeshTexture(frameMesh, appliedFrameTexture, originalFrameTexture);
            RestoreMeshTexture(backgroundMesh, appliedBackgroundTexture, originalBackgroundTexture);
        }
        appliedFrame = null;
        appliedBackground = null;
        appliedFrameTexture = null;
        appliedBackgroundTexture = null;
    }

    private void ResetSkinCapture()
    {
        captured = false;
        skinTargetKind = CardVisualRenderTargetKind.None;
        originalFrame = null;
        originalBackground = null;
        originalFrameTexture = null;
        originalBackgroundTexture = null;
        appliedFrame = null;
        appliedBackground = null;
        appliedFrameTexture = null;
        appliedBackgroundTexture = null;
    }

    private bool ClearEffect()
    {
        var coordinated = effectMaterialLease != null;
        var release = effectMaterialLease?.Release()
                      ?? new AuraPresentationMaterialReleaseResult(
                          AuraPresentationMaterialReleaseDisposition.Clean,
                          "");
        if (release.IsBlocked)
        {
            AuraToolsLog.WarnOnce(
                "card-visual-material-stack-blocked:"
                + (effectMeshTarget != null
                    ? effectMeshTarget.GetInstanceID()
                    : effectImageTarget != null
                        ? effectImageTarget.GetInstanceID()
                        : 0),
                "[CardVisual] coordinated dynamic material release was blocked: card="
                + (diagnosticCardId.Length == 0 ? "<unknown>" : diagnosticCardId)
                + ", surface="
                + diagnosticSurface
                + ", detail="
                + release.Diagnostic);
        }

        if (!coordinated && effectMaterial != null)
            Object.Destroy(effectMaterial);
        effectMaterialLease = null;
        effectImageTarget = null;
        effectMeshTarget = null;
        originalEffectImageMaterial = null;
        originalEffectMeshMaterial = null;
        effectMaterial = null;
        return release.IsClean;
    }

    private bool AcquireEffectMaterial(MeshRenderer target, Material material)
    {
        var viewRoot = transform;
        var generation = viewRoot.GetComponent<AuraCardPresentationViewMarker>()?.Generation ?? 0;
        return AuraPresentationMaterialCoordinator.TryAcquire(
            new AuraPresentationMaterialAcquireRequest
            {
                ViewRootInstanceId = viewRoot.GetInstanceID(),
                ViewGeneration = generation,
                TargetInstanceId = target.GetInstanceID(),
                OwnerId = "AuraTools.CardVisual.DynamicEffect",
                AppliedMaterial = material,
                IsTargetAlive = () => target != null,
                ReadCurrentMaterial = () => target == null ? null : target.sharedMaterial,
                WriteCurrentMaterial = value =>
                {
                    if (target != null) target.sharedMaterial = value as Material;
                },
                MaterialInstanceId = MaterialInstanceId,
                ReleaseAppliedMaterial = DestroyMaterial
            },
            out effectMaterialLease,
            out var failure)
            || WarnAcquireFailure(failure);
    }

    private bool AcquireEffectMaterial(Image target, Material material)
    {
        var viewRoot = transform;
        var generation = viewRoot.GetComponent<AuraCardPresentationViewMarker>()?.Generation ?? 0;
        return AuraPresentationMaterialCoordinator.TryAcquire(
            new AuraPresentationMaterialAcquireRequest
            {
                ViewRootInstanceId = viewRoot.GetInstanceID(),
                ViewGeneration = generation,
                TargetInstanceId = target.GetInstanceID(),
                OwnerId = "AuraTools.CardVisual.DynamicEffect",
                AppliedMaterial = material,
                IsTargetAlive = () => target != null,
                ReadCurrentMaterial = () => target == null ? null : target.material,
                WriteCurrentMaterial = value =>
                {
                    if (target != null) target.material = value as Material;
                },
                MaterialInstanceId = MaterialInstanceId,
                ReleaseAppliedMaterial = DestroyMaterial
            },
            out effectMaterialLease,
            out var failure)
            || WarnAcquireFailure(failure);
    }

    private bool WarnAcquireFailure(string failure)
    {
        AuraToolsLog.WarnOnce(
            "card-visual-material-acquire-failed:"
            + diagnosticCardId
            + ":"
            + diagnosticSurface,
            "[CardVisual] coordinated dynamic material acquisition failed: card="
            + (diagnosticCardId.Length == 0 ? "<unknown>" : diagnosticCardId)
            + ", surface="
            + diagnosticSurface
            + ", detail="
            + failure);
        return false;
    }

    private static void DestroyMaterial(object material)
    {
        if (material is Material value && value != null)
        {
            Object.Destroy(value);
        }
    }

    private Texture? ReadMeshTexture(MeshRenderer? mesh)
    {
        if (mesh == null) return null;
        return SameUnityObject(effectMeshTarget, mesh) && originalEffectMeshMaterial != null
            ? originalEffectMeshMaterial.mainTexture
            : mesh.sharedMaterial?.mainTexture;
    }

    private void SetMeshTexture(MeshRenderer mesh, Texture texture)
    {
        if (SameUnityObject(effectMeshTarget, mesh) && originalEffectMeshMaterial != null)
        {
            originalEffectMeshMaterial.mainTexture = texture;
            AuraToolsCardVisualAssets.ApplyRuntimeTexture(effectMaterial, texture);
            return;
        }
        var material = mesh.material;
        if (material != null) material.mainTexture = texture;
    }

    private void RestoreMeshTexture(MeshRenderer? mesh, Texture? applied, Texture? original)
    {
        if (mesh == null || applied == null) return;
        var target = SameUnityObject(effectMeshTarget, mesh) && originalEffectMeshMaterial != null
            ? originalEffectMeshMaterial
            : mesh.material;
        if (target != null && SameUnityObject(target.mainTexture, applied)) target.mainTexture = original;
        if (SameUnityObject(effectMeshTarget, mesh))
            AuraToolsCardVisualAssets.ApplyRuntimeTexture(effectMaterial, original);
    }

    private static bool SameUnityObject(Object? left, Object? right)
    {
        return left != null
               && right != null
               && left.GetInstanceID() == right.GetInstanceID();
    }

    private static int MaterialInstanceId(object? material)
    {
        return material is Material value && value != null ? value.GetInstanceID() : 0;
    }

    private CardVisualRenderTargetKind ResolveNativeTargetKind(Transform? targetNode, string purpose)
    {
        var backgroundNode = transform.Find("Front/background");
        var backgroundHasMesh = backgroundNode?.GetComponent<MeshRenderer>() != null;
        var frameHasImage = targetNode?.GetComponent<Image>() != null;
        var frameHasMesh = targetNode?.GetComponent<MeshRenderer>() != null;
        var kind = CardVisualRenderTargetPolicy.Resolve(
            backgroundHasMesh,
            frameHasImage,
            frameHasMesh);
        var nodeName = targetNode == null ? "<missing>" : targetNode.name;
        AuraSharedLog.InfoOnce(
            "AuraTools",
            "card-visual-native-target:"
            + diagnosticCardId
            + ":"
            + purpose
            + ":"
            + kind,
            "[CardVisual] native renderer resolved: card="
            + (diagnosticCardId.Length == 0 ? "<unknown>" : diagnosticCardId)
            + ", surface="
            + diagnosticSurface
            + ", purpose="
            + purpose
            + ", node="
            + nodeName
            + ", backgroundMesh="
            + backgroundHasMesh
            + ", targetImage="
            + frameHasImage
            + ", targetMesh="
            + frameHasMesh
            + ", selected="
            + kind,
            mirrorCommands: false);
        return kind;
    }

    private void WarnMissingNativeTarget(string purpose, Transform? targetNode)
    {
        AuraToolsLog.WarnOnce(
            "card-visual-native-target-missing:"
            + diagnosticCardId
            + ":"
            + purpose,
            "[CardVisual] native renderer contract rejected card="
            + (diagnosticCardId.Length == 0 ? "<unknown>" : diagnosticCardId)
            + ", surface="
            + diagnosticSurface
            + ", purpose="
            + purpose
            + ", node="
            + (targetNode == null ? "<missing>" : targetNode.name));
    }

    private static Sprite? LoadSprite(string path) => AuraToolsCardVisualAssets.LoadSprite(path);
    private void OnDestroy()
    {
        ClearAll();
        var generation = GetComponent<AuraCardPresentationViewMarker>()?.Generation ?? 0;
        AuraPresentationMaterialCoordinator.AbandonView(
            transform.GetInstanceID(),
            generation);
    }
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

    public static Material? CreateMaterial(
        CardDynamicEffectDefinition effect,
        CardDynamicEffectSettings? settings,
        Texture? runtimeTexture,
        bool useMesh)
    {
        try
        {
            var materialPath = useMesh ? effect.MeshMaterialPath : effect.ImageMaterialPath;
            var source = AuraToolsVisualBundleRuntime.LoadAsset<Material>(effect.BundlePath, materialPath);
            if (source == null)
                throw new InvalidDataException("material asset is missing: " + materialPath);
            if (source.shader == null || !source.shader.isSupported || source.passCount <= 0)
                throw new InvalidDataException(
                    "shader is unsupported for " + (useMesh ? "MeshRenderer" : "Image")
                    + ": " + (source.shader?.name ?? "<missing>"));
            if (string.Equals(source.shader.name, "Hidden/InternalErrorShader", StringComparison.Ordinal))
                throw new InvalidDataException("material resolved to Unity's internal error shader: " + materialPath);
            if (!GraphicsSettings.isScriptableRenderPipelineEnabled)
                throw new InvalidDataException("the active game renderer is not a scriptable render pipeline");
            var renderPipeline = source.GetTag("RenderPipeline", false, "");
            if (!string.Equals(renderPipeline, "UniversalPipeline", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "material does not declare the UniversalPipeline contract: " + materialPath);
            var requiredPass = useMesh ? "CardFrameEffectURP" : "Default";
            if (source.FindPass(requiredPass) < 0)
                throw new InvalidDataException(
                    "material is missing the required " + requiredPass + " pass: " + materialPath);
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
            ApplyRuntimeTexture(material, runtimeTexture);
            return material;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[CardVisual] material load failed: " + effect.EffectId + " -> " + ex.Message);
            return null;
        }
    }

    public static void ApplyRuntimeTexture(Material? material, Texture? texture)
    {
        if (material == null || texture == null || !material.HasProperty("_MainTex")) return;
        material.SetTexture("_MainTex", texture);
    }
}
