using System;
using System.Collections.Generic;
using System.Globalization;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpUiSprites
{
    public const string ButtonSpritePath = "Mods/SunExp/ModResource/Images/UI/button-\u4e5d\u5bab\u683c.png";
    public const string PanelSpritePath = "Mods/SunExp/ModResource/Images/UI/background-\u4e5d\u5bab\u683c.png";
    public const string LabelSpritePath = "Mods/SunExp/ModResource/Images/UI/Label-\u5c0f-\u4e5d\u5bab\u683c.png";
    public const string SubMenuSpritePath = "Mods/SunExp/ModResource/Images/UI/\u5b50\u83dc\u5355/\u5b50\u83dc\u5355\u6563\u4ef6.png";
    public const string SubMenuNormalButtonPath = "Mods/SunExp/ModResource/Images/UI/\u5b50\u83dc\u5355/button-normal.png";

    private static readonly Dictionary<string, Sprite?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Sprite? Button(string logPrefix)
    {
        return NineSlice(ButtonSpritePath, new Vector4(14f, 14f, 14f, 14f), logPrefix, new Rect(17f, 16f, 135f, 49f));
    }

    public static Sprite? Panel(string logPrefix)
    {
        return NineSlice(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f), logPrefix);
    }

    public static Sprite? Label(string logPrefix)
    {
        return NineSlice(LabelSpritePath, new Vector4(8f, 8f, 8f, 8f), logPrefix);
    }

    public static Sprite? LibrarySubMenuButton(string logPrefix)
    {
        return NineSlice(SubMenuNormalButtonPath, Vector4.zero, logPrefix);
    }

    public static Sprite? LibrarySubMenuButtonHighlighted(string logPrefix)
    {
        return LibrarySubMenuButton(logPrefix);
    }

    public static Sprite? NineSlice(string path, Vector4 border, string logPrefix, Rect? sourceCrop = null)
    {
        var key = path
                  + "|"
                  + border.x.ToString("0.###", CultureInfo.InvariantCulture)
                  + ","
                  + border.y.ToString("0.###", CultureInfo.InvariantCulture)
                  + ","
                  + border.z.ToString("0.###", CultureInfo.InvariantCulture)
                  + ","
                  + border.w.ToString("0.###", CultureInfo.InvariantCulture)
                  + "|"
                  + CropKey(sourceCrop);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            var source = SunExpResourceCache.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                SunExpLog.Warn(logPrefix + " UI sprite missing: " + path);
            }
            else
            {
                var texture = source.texture;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                var rect = ResolveSpriteRect(source, sourceCrop);
                sprite = Sprite.Create(
                    texture,
                    rect,
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

    private static string CropKey(Rect? sourceCrop)
    {
        if (sourceCrop == null)
        {
            return "full";
        }

        var crop = sourceCrop.Value;
        return crop.x.ToString("0.###", CultureInfo.InvariantCulture)
               + ","
               + crop.y.ToString("0.###", CultureInfo.InvariantCulture)
               + ","
               + crop.width.ToString("0.###", CultureInfo.InvariantCulture)
               + ","
               + crop.height.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static Rect ResolveSpriteRect(Sprite source, Rect? sourceCrop)
    {
        if (sourceCrop == null)
        {
            return source.rect;
        }

        var crop = sourceCrop.Value;
        var x = Mathf.Clamp(source.rect.x + crop.x, source.rect.x, source.rect.xMax);
        var y = Mathf.Clamp(source.rect.y + crop.y, source.rect.y, source.rect.yMax);
        var width = Mathf.Clamp(crop.width, 1f, source.rect.xMax - x);
        var height = Mathf.Clamp(crop.height, 1f, source.rect.yMax - y);
        return new Rect(x, y, width, height);
    }
}
