using System;
using System.Collections.Generic;
using System.Globalization;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpUiSprites
{
    public const string ButtonSpritePath = "Mods/SunExp/ModResource/Images/UI/button-\u4e5d\u5bab\u683c.png";
    public const string PanelSpritePath = "Mods/SunExp/ModResource/Images/UI/background-\u4e5d\u5bab\u683c.png";

    private static readonly Dictionary<string, Sprite?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Sprite? Button(string logPrefix)
    {
        return NineSlice(ButtonSpritePath, new Vector4(24f, 12f, 24f, 12f), logPrefix);
    }

    public static Sprite? Panel(string logPrefix)
    {
        return NineSlice(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f), logPrefix);
    }

    public static Sprite? NineSlice(string path, Vector4 border, string logPrefix)
    {
        var key = path
                  + "|"
                  + border.x.ToString("0.###", CultureInfo.InvariantCulture)
                  + ","
                  + border.y.ToString("0.###", CultureInfo.InvariantCulture)
                  + ","
                  + border.z.ToString("0.###", CultureInfo.InvariantCulture)
                  + ","
                  + border.w.ToString("0.###", CultureInfo.InvariantCulture);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            var source = ResourceLoader.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                SunExpLog.Warn(logPrefix + " UI sprite missing: " + path);
            }
            else
            {
                var texture = source.texture;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                sprite = Sprite.Create(
                    texture,
                    source.rect,
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    border);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(logPrefix + " failed to load UI sprite " + path + ": " + ex.Message);
        }

        Cache[key] = sprite;
        return sprite;
    }
}
