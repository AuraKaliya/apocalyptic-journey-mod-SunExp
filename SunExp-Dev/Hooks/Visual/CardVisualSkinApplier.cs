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

    public static bool Apply(Transform? cardRoot, IDataConfig? config)
    {
        var skin = CardVisualThemeCatalog.Resolve(config);
        if (cardRoot == null || skin == null)
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
            image.sprite = sprite;
            return true;
        }

        var mesh = node.GetComponent<MeshRenderer>();
        if (mesh != null)
        {
            mesh.material.mainTexture = sprite.texture;
            return true;
        }

        if (required)
        {
            SunExpLog.Warn(LogPrefix + " " + skinId + " " + layerName + " component missing: " + node.name);
        }

        return false;
    }
}
