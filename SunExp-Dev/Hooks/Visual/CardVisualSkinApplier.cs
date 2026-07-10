using System.Collections.Generic;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

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
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
        if (cardRoot == null)
        {
            return false;
        }

        if (!CardVisualInterestIndex.MayAffect(config))
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.InterestMiss.Applier");
            return false;
        }

        var marker = cardRoot.GetComponent<CardVisualSkinMarker>() ?? cardRoot.gameObject.AddComponent<CardVisualSkinMarker>();
        var resumedFrameEffect = marker.ResumeFrameEffectOverlayFor(config);
        var visualSignature = VisualSignature(config);
        var rootInstanceId = cardRoot.GetInstanceID();
        var forceRefresh = string.Equals(source, SunExpHookTargets.ICardSetCardStyle, System.StringComparison.Ordinal);
        if (!forceRefresh
            && !resumedFrameEffect
            && visualSignature.Length > 0
            && marker.LastAppliedRootInstanceId == rootInstanceId
            && marker.LastVisualSignature == visualSignature)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.SignatureSkip");
            return false;
        }

        var skin = CardVisualThemeCatalog.Resolve(config);
        var appliedFrame = false;
        var appliedBackground = false;
        var clearedSkin = false;
        if (skin == null)
        {
            clearedSkin = marker.ClearSkinVisuals();
            SunExpPerformanceCounters.Record("CardVisualSkin.SkinMiss");
            LogUnresolvedSunExpCard(config);
        }
        else
        {
            marker.CaptureSkinBaseline();
            SunExpPerformanceCounters.Record("CardVisualSkin.SkinResolved");
            appliedFrame = ApplySprite(marker, background: false, skin.FramePath, skin.Id, "frame", required: true);
            appliedBackground = ApplySprite(marker, background: true, skin.BackgroundPath, skin.Id, "background", required: false);
            if (appliedFrame || appliedBackground)
            {
                marker.LastSkinId = skin.Id;
            }

            if ((appliedFrame || appliedBackground) && LoggedSkins.Add(skin.Id))
            {
                SunExpLog.Info(LogPrefix + " applied " + skin.DisplayName + " skin to card: " + DictionaryUtil.Get(config?.data, "Id", "unknown"));
            }
        }

        var effectStart = SunExpPerformanceCounters.Timestamp();
        var appliedEffect = CardVisualEffectApplier.Apply(marker, config);
        SunExpPerformanceCounters.RecordDuration("CardVisualSkin.ApplyEffect", effectStart);
        if (skin != null)
        {
            LogResolvedSkin(marker, config, skin.Id, source, appliedFrame, appliedBackground, appliedEffect);
        }

        if (appliedFrame)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.FrameApplied");
        }

        if (appliedBackground)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.BackgroundApplied");
        }

        if (appliedEffect)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.EffectApplied");
        }

        if (!appliedFrame && !appliedBackground && !appliedEffect && !clearedSkin)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.ApplyNoChange");
        }

        marker.LastVisualSignature = visualSignature;
        marker.LastAppliedRootInstanceId = rootInstanceId;
        marker.LastAppliedStage = source ?? "";
        return appliedFrame || appliedBackground || appliedEffect;
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("CardVisualSkin.Apply", start);
        }
    }

    private static void LogUnresolvedSunExpCard(IDataConfig? config)
    {
        if (!CardVisualThemeCatalog.IsSunExpCard(config) || LoggedUnresolvedCards.Count >= 24)
        {
            return;
        }

        var id = DictionaryUtil.Get(config?.data, "Id", "unknown");
        var icon = DictionaryUtil.Get(config?.data, "Icon");
        var key = id + "|" + icon;
        if (LoggedUnresolvedCards.Add(key))
        {
            SunExpLog.Debug(LogPrefix + " no themed skin resolved for SunExp card: id="
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

        SunExpLog.Info(LogPrefix
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
                SunExpLog.Warn(LogPrefix + " " + skinId + " " + layerName + " node missing: " + path);
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
                    SunExpLog.Warn(LogPrefix + " " + skinId + " " + layerName + " material missing: " + node.name);
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
            SunExpLog.Warn(LogPrefix + " " + skinId + " " + layerName + " component missing: " + node.name);
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
            + DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey)
            + "\u001f"
            + (CardVisualThemeCatalog.Resolve(config)?.Id ?? "")
            + "\u001f"
            + (CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, config)?.Id ?? "")
            + "\u001f"
            + (CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, config)?.Id ?? "");
    }
}
