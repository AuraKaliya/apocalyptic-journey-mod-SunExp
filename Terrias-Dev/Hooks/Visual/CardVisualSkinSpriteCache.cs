using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

public static class CardVisualSkinSpriteCache
{
    public static Sprite? Load(string path, string logPrefix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        Sprite? sprite = null;
        try
        {
            sprite = TerriasResourceCache.Load<Sprite>(path, true, "visual.card-skin");
            if (sprite == null)
            {
                TerriasLog.Warn(logPrefix + " sprite missing: " + path);
            }
            else
            {
                ConfigureTexture(sprite.texture);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(logPrefix + " sprite load failed: " + path + " (" + ex.Message + ")");
        }

        return sprite;
    }

    private static void ConfigureTexture(Texture? texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
    }
}
