using System.Collections.Generic;
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
    private static readonly HashSet<string> LoggedUnresolvedCards = new();

    public static bool Apply(Transform? cardRoot, IDataConfig? config)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
        if (cardRoot == null)
        {
            return false;
        }

        var marker = cardRoot.GetComponent<CardVisualSkinMarker>() ?? cardRoot.gameObject.AddComponent<CardVisualSkinMarker>();
        var visualSignature = VisualSignature(config);
        if (visualSignature.Length > 0 && marker.LastVisualSignature == visualSignature)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.SignatureSkip");
            return false;
        }

        var skin = CardVisualThemeCatalog.Resolve(config);
        var appliedFrame = false;
        var appliedBackground = false;
        if (skin == null)
        {
            LogUnresolvedSunExpCard(config);
        }
        else
        {
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
        marker.LastVisualSignature = visualSignature;
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
            + SunExpPerformanceSettings.Quality;
    }
}
