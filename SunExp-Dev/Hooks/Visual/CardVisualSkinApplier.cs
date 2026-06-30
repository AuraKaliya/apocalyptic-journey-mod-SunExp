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
        if (cardRoot == null)
        {
            return false;
        }

        var marker = cardRoot.GetComponent<CardVisualSkinMarker>() ?? cardRoot.gameObject.AddComponent<CardVisualSkinMarker>();
        var skin = CardVisualThemeCatalog.Resolve(config);
        if (skin == null)
        {
            var clearedEffect = CardFrameEffectApplier.Clear(marker);
            LogUnresolvedSunExpCard(config);
            return clearedEffect;
        }

        var appliedFrame = ApplySprite(marker, background: false, skin.FramePath, skin.Id, "frame", required: true);
        var appliedBackground = ApplySprite(marker, background: true, skin.BackgroundPath, skin.Id, "background", required: false);
        var appliedEffect = CardFrameEffectApplier.Apply(marker, skin);
        if (appliedFrame || appliedBackground)
        {
            marker.LastSkinId = skin.Id;
        }

        if ((appliedFrame || appliedBackground) && LoggedSkins.Add(skin.Id))
        {
            SunExpLog.Info(LogPrefix + " applied " + skin.DisplayName + " skin to card: " + DictionaryUtil.Get(config?.data, "Id", "unknown"));
        }

        return appliedFrame || appliedBackground || appliedEffect;
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

        var node = background ? marker.BackgroundNode : marker.FrameNode;
        if (node == null)
        {
            if (required)
            {
                SunExpLog.Warn(LogPrefix + " " + skinId + " " + layerName + " node missing: " + path);
            }

            return false;
        }

        var sprite = CardVisualSkinSpriteCache.Load(path, LogPrefix);
        if (sprite == null)
        {
            return false;
        }

        var image = background ? marker.BackgroundImage : marker.FrameImage;
        if (image != null)
        {
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
}
