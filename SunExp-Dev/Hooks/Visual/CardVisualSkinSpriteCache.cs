using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

public static class CardVisualSkinSpriteCache
{
    private static readonly Dictionary<string, Sprite?> Cache = new(StringComparer.Ordinal);

    public static Sprite? Load(string path, string logPrefix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            sprite = ResourceLoader.Load<Sprite>(path, true);
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

        Cache[path] = sprite;
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
