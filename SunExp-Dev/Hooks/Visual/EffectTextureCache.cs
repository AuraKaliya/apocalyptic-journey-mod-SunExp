using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

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
            var texture = SunExpResourceCache.Load<Texture>(key, true, "visual.effect-texture");
            if (texture == null)
            {
                SunExpLog.Warn(logPrefix + " effect texture missing: " + key);
            }

            return texture;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(logPrefix + " effect texture load failed: " + key + " (" + ex.Message + ")");
            return null;
        }
    }
}
