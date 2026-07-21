using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

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
            sprite = SunExpResourceCache.Load<Sprite>(path, true, "visual.card-skin");
            if (sprite == null)
            {
                SunExpLog.Warn(logPrefix + " sprite missing: " + path);
            }
            else
            {
                ConfigureTexture(sprite.texture);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(logPrefix + " sprite load failed: " + path + " (" + ex.Message + ")");
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
