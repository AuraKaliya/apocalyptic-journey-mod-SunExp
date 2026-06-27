using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

public static class EffectTextureCache
{
    private static readonly Dictionary<string, Texture?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Texture? Load(string path, string logPrefix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var key = path.Trim();
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var texture = ResourceLoader.Load<Texture>(key, true);
            if (texture == null)
            {
                SunExpLog.Warn(logPrefix + " effect texture missing: " + key);
            }

            Cache[key] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            Cache[key] = null;
            SunExpLog.Warn(logPrefix + " effect texture load failed: " + key + " (" + ex.Message + ")");
            return null;
        }
    }
}
