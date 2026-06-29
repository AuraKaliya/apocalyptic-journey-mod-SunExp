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
    private static readonly Dictionary<int, int> MeshTextureIds = new();
    private static readonly MaterialPropertyBlock SharedPropertyBlock = new();
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    public static bool Apply(Transform? cardRoot, IDataConfig? config)
    {
        var skin = CardVisualThemeCatalog.Resolve(config);
        if (skin == null)
        {
            LogUnresolvedSunExpCard(config);
            return false;
        }

        if (cardRoot == null)
        {
            return false;
        }

        var appliedFrame = ApplySprite(cardRoot.Find("Front/FrontBack"), skin.FramePath, skin.Id, "frame", required: true);
        var appliedBackground = ApplySprite(cardRoot.Find("Front/background"), skin.BackgroundPath, skin.Id, "background", required: false);
        if ((appliedFrame || appliedBackground) && LoggedSkins.Add(skin.Id))
        {
            SunExpLog.Info(LogPrefix + " applied " + skin.DisplayName + " skin to card: " + DictionaryUtil.Get(config?.data, "Id", "unknown"));
        }

        return appliedFrame || appliedBackground;
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

    private static bool ApplySprite(Transform? node, string path, string skinId, string layerName, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

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

        var image = node.GetComponent<Image>();
        if (image != null)
        {
            if (image.sprite == sprite)
            {
                return false;
            }

            image.sprite = sprite;
            return true;
        }

        var mesh = node.GetComponent<MeshRenderer>();
        if (mesh != null)
        {
            var rendererId = mesh.GetInstanceID();
            var textureId = sprite.texture.GetInstanceID();
            var changed = !MeshTextureIds.TryGetValue(rendererId, out var currentTextureId) || currentTextureId != textureId;
            SharedPropertyBlock.Clear();
            mesh.GetPropertyBlock(SharedPropertyBlock);
            SharedPropertyBlock.SetTexture(MainTexId, sprite.texture);
            mesh.SetPropertyBlock(SharedPropertyBlock);
            MeshTextureIds[rendererId] = textureId;
            return changed;
        }

        if (required)
        {
            SunExpLog.Warn(LogPrefix + " " + skinId + " " + layerName + " component missing: " + node.name);
        }

        return false;
    }
}
