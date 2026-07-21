using System.Collections.Generic;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;

namespace Terrias.Dll.Hooks.Visual;

public static class CardVisualSkinApplier
{
    private const string LogPrefix = "[CardVisualSkin]";
    private static readonly HashSet<string> LoggedSkins = new();
    private static readonly HashSet<string> LoggedResolvedSkins = new();
    private static readonly HashSet<string> LoggedUnresolvedCards = new();

    public static bool Apply(Transform? cardRoot, IDataConfig? config)
    {
        return Apply(cardRoot, config, "");
    }

    public static bool Apply(Transform? cardRoot, IDataConfig? config, string source)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
        if (cardRoot == null)
        {
            return false;
        }

        if (!CardVisualInterestIndex.MayAffect(config))
        {
            TerriasPerformanceCounters.Record("CardVisualSkin.InterestMiss.Applier");
            return false;
        }

        var marker = cardRoot.GetComponent<CardVisualSkinMarker>() ?? cardRoot.gameObject.AddComponent<CardVisualSkinMarker>();
        var resumedFrameEffect = marker.ResumeFrameEffectOverlayFor(config);
        var visualSignature = VisualSignature(config);
        var rootInstanceId = cardRoot.GetInstanceID();
        var forceRefresh = string.Equals(source, TerriasHookTargets.ICardSetCardStyle, System.StringComparison.Ordinal);
        if (!forceRefresh
            && !resumedFrameEffect
            && visualSignature.Length > 0
            && marker.LastAppliedRootInstanceId == rootInstanceId
            && marker.LastVisualSignature == visualSignature)
        {
            TerriasPerformanceCounters.Record("CardVisualSkin.SignatureSkip");
            return false;
        }

        var skin = CardVisualThemeCatalog.Resolve(config);
        var appliedFrame = false;
        var appliedBackground = false;
        var clearedSkin = false;
        if (skin == null)
        {
            clearedSkin = marker.ClearSkinVisuals();
            TerriasPerformanceCounters.Record("CardVisualSkin.SkinMiss");
            LogUnresolvedTerriasCard(config);
        }
        else
        {
            marker.CaptureSkinBaseline();
            TerriasPerformanceCounters.Record("CardVisualSkin.SkinResolved");
            appliedFrame = ApplySprite(marker, background: false, skin.FramePath, skin.Id, "frame", required: true);
            appliedBackground = ApplySprite(marker, background: true, skin.BackgroundPath, skin.Id, "background", required: false);
            if (appliedFrame || appliedBackground)
            {
                marker.LastSkinId = skin.Id;
            }

            if ((appliedFrame || appliedBackground) && LoggedSkins.Add(skin.Id))
            {
                TerriasLog.Info(LogPrefix + " applied " + skin.DisplayName + " skin to card: " + DictionaryUtil.Get(config?.data, "Id", "unknown"));
            }
        }

        var effectStart = TerriasPerformanceCounters.Timestamp();
        var appliedEffect = CardVisualEffectApplier.Apply(marker, config);
        TerriasPerformanceCounters.RecordDuration("CardVisualSkin.ApplyEffect", effectStart);
        if (skin != null)
        {
            LogResolvedSkin(marker, config, skin.Id, source, appliedFrame, appliedBackground, appliedEffect);
        }

        if (appliedFrame)
        {
            TerriasPerformanceCounters.Record("CardVisualSkin.FrameApplied");
        }

        if (appliedBackground)
        {
            TerriasPerformanceCounters.Record("CardVisualSkin.BackgroundApplied");
        }

        if (appliedEffect)
        {
            TerriasPerformanceCounters.Record("CardVisualSkin.EffectApplied");
        }

        if (!appliedFrame && !appliedBackground && !appliedEffect && !clearedSkin)
        {
            TerriasPerformanceCounters.Record("CardVisualSkin.ApplyNoChange");
        }

        marker.LastVisualSignature = visualSignature;
        marker.LastAppliedRootInstanceId = rootInstanceId;
        marker.LastAppliedStage = source ?? "";
        return appliedFrame || appliedBackground || appliedEffect;
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("CardVisualSkin.Apply", start);
            TerriasCombatCardUiDiagnostics.RecordCurrentSegment("CardVisualSkin.Apply", start);
        }
    }

    private static void LogUnresolvedTerriasCard(IDataConfig? config)
    {
        if (!CardVisualThemeCatalog.IsTerriasCard(config) || LoggedUnresolvedCards.Count >= 24)
        {
            return;
        }

        var id = DictionaryUtil.Get(config?.data, "Id", "unknown");
        var icon = DictionaryUtil.Get(config?.data, "Icon");
        var key = id + "|" + icon;
        if (LoggedUnresolvedCards.Add(key))
        {
            TerriasLog.Debug(LogPrefix + " no themed skin resolved for Terrias card: id="
                + id
                + ", pack="
                + DictionaryUtil.Get(config?.data, "PackBelong")
                + ", icon="
                + icon);
        }
    }

    public static bool ClearForUnmatchedCard(Transform? cardRoot)
    {
        if (cardRoot == null)
        {
            return false;
        }

        var marker = cardRoot.GetComponent<CardVisualSkinMarker>();
        return marker != null && marker.ClearAllVisualOverrides();
    }

    private static void LogResolvedSkin(
        CardVisualSkinMarker marker,
        IDataConfig? config,
        string skinId,
        string source,
        bool appliedFrame,
        bool appliedBackground,
        bool appliedEffect)
    {
        var cardId = DictionaryUtil.Get(config?.data, "Id", "unknown");
        var key = skinId
            + "\u001f"
            + cardId
            + "\u001f"
            + (source ?? "");
        if (LoggedResolvedSkins.Count >= 32 || !LoggedResolvedSkins.Add(key))
        {
            return;
        }

        TerriasLog.Info(LogPrefix
            + " resolved skin: card="
            + cardId
            + ", skin="
            + skinId
            + ", source="
            + source
            + ", appliedFrame="
            + appliedFrame
            + ", appliedBackground="
            + appliedBackground
            + ", appliedEffect="
            + appliedEffect
            + ", marker={"
            + marker.FrameEffectDiagnosticSummary()
            + "}");
    }

    private static bool ApplySprite(CardVisualSkinMarker marker, bool background, string path, string skinId, string layerName, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var sprite = CardVisualSkinSpriteCache.Load(path, LogPrefix);
        if (sprite == null)
        {
            return false;
        }

        if (!background)
        {
            marker.LastFrameSprite = sprite;
            marker.LastFrameTexture = sprite.texture;
        }

        var node = background ? marker.BackgroundNode : marker.FrameNode;
        if (node == null)
        {
            if (required)
            {
                TerriasLog.Warn(LogPrefix + " " + skinId + " " + layerName + " node missing: " + path);
            }

            return false;
        }

        var image = background ? marker.BackgroundImage : marker.FrameImage;
        if (image != null)
        {
            if (!background)
            {
                marker.LastFrameSprite = sprite;
                marker.LastFrameTexture = sprite.texture;
            }

            if (image.sprite == sprite)
            {
                return false;
            }

            image.sprite = sprite;
            return true;
        }

        var mesh = background ? marker.BackgroundMesh : marker.FrameMesh;
        if (mesh != null)
        {
            var textureId = sprite.texture.GetInstanceID();
            if (!background)
            {
                marker.LastFrameTexture = sprite.texture;
            }

            var material = background ? marker.BackgroundMaterial : marker.FrameMaterial;
            if (material == null)
            {
                if (required)
                {
                    TerriasLog.Warn(LogPrefix + " " + skinId + " " + layerName + " material missing: " + node.name);
                }

                return false;
            }

            var currentTexture = material.mainTexture;
            var cachedTextureId = background ? marker.LastBackgroundTextureId : marker.LastFrameTextureId;
            var changed = !ReferenceEquals(currentTexture, sprite.texture) || cachedTextureId != textureId;
            material.mainTexture = sprite.texture;
            if (background)
            {
                marker.LastBackgroundTextureId = textureId;
                marker.LastFaceTexture = sprite.texture;
            }
            else
            {
                marker.LastFrameTextureId = textureId;
            }

            return changed;
        }

        if (required)
        {
            TerriasLog.Warn(LogPrefix + " " + skinId + " " + layerName + " component missing: " + node.name);
        }

        return false;
    }

    private static string VisualSignature(IDataConfig? config)
    {
        if (config == null)
        {
            return "";
        }

        return DictionaryUtil.Get(config.data, "Id")
            + "\u001f"
            + DictionaryUtil.Get(config.data, "PackBelong")
            + "\u001f"
            + DictionaryUtil.Get(config.data, "Icon")
            + "\u001f"
            + DictionaryUtil.Get(config.data, "Tag")
            + "\u001f"
            + DictionaryUtil.Get(config.Vars, "Tag")
            + "\u001f"
            + DictionaryUtil.Get(config.Vars, "SpecialTag")
            + "\u001f"
            + DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey)
            + "\u001f"
            + (CardVisualThemeCatalog.Resolve(config)?.Id ?? "")
            + "\u001f"
            + (CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, config)?.Id ?? "")
            + "\u001f"
            + (CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, config)?.Id ?? "");
    }
}
