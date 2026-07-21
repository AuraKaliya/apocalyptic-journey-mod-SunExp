using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

public static class EffectTextureCache
{
    public static Texture? Load(string path, string logPrefix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var key = path.Trim();
        try
        {
            var texture = TerriasResourceCache.Load<Texture>(key, true, "visual.effect-texture");
            if (texture == null)
            {
                TerriasLog.Warn(logPrefix + " effect texture missing: " + key);
            }

            return texture;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(logPrefix + " effect texture load failed: " + key + " (" + ex.Message + ")");
            return null;
        }
    }
}
