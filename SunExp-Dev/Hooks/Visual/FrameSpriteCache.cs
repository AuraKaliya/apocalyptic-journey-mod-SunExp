using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

public static class FrameSpriteCache
{
    private static readonly Dictionary<string, Sprite[]> Cache = new(StringComparer.Ordinal);

    public static Sprite[] LoadFrames(FrameSpriteAnimationSpec spec, string logPrefix)
    {
        if (!spec.IsValid)
        {
            return Array.Empty<Sprite>();
        }

        var key = CacheKey(spec);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var loaded = new List<Sprite>();
        foreach (var path in spec.FramePaths)
        {
            try
            {
                var sprite = ResourceLoader.Load<Sprite>(path, true);
                if (sprite == null)
                {
                    SunExpLog.Warn(logPrefix + " animation frame missing: " + path);
                    continue;
                }

                ConfigureTexture(sprite.texture);
                loaded.Add(sprite);
            }
            catch (Exception ex)
            {
                SunExpLog.Warn(logPrefix + " animation frame load failed: " + path + " (" + ex.Message + ")");
            }
        }

        var frames = loaded.ToArray();
        Cache[key] = frames;
        return frames;
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

    private static string CacheKey(FrameSpriteAnimationSpec spec)
    {
        return spec.Id + "\u001f" + string.Join("\u001e", spec.FramePaths.Select(path => path ?? ""));
    }
}
