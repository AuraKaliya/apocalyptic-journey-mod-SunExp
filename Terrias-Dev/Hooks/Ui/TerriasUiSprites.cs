using System;
using System.Collections.Generic;
using System.Globalization;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Hooks.Ui;

public static class TerriasUiSprites
{
    public const string ButtonSpritePath = "Mods/Terrias/ModResource/Images/UI/button-\u4e5d\u5bab\u683c.png";
    public const string PanelSpritePath = "Mods/Terrias/ModResource/Images/UI/background-\u4e5d\u5bab\u683c.png";
    public const string LabelSpritePath = "Mods/Terrias/ModResource/Images/UI/Label-\u5c0f-\u4e5d\u5bab\u683c.png";
    public const string SubMenuSpritePath = "Mods/Terrias/ModResource/Images/UI/\u5b50\u83dc\u5355/\u5b50\u83dc\u5355\u6563\u4ef6.png";
    public const string SubMenuNormalButtonPath = "Mods/Terrias/ModResource/Images/UI/\u5b50\u83dc\u5355/button-normal.png";

    private static readonly Dictionary<string, Sprite?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D> GeneratedTextures = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear()
    {
        var destroyed = new HashSet<int>();
        foreach (var sprite in Cache.Values)
        {
            if (sprite != null && destroyed.Add(sprite.GetInstanceID()))
            {
                UnityEngine.Object.Destroy(sprite);
            }
        }

        Cache.Clear();
        foreach (var texture in GeneratedTextures.Values)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
        GeneratedTextures.Clear();
    }

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

    public static Sprite RoundedSolid(
        string key,
        int width,
        int height,
        float radius,
        Color color)
    {
        return RoundedGradient(key, width, height, radius, color, color);
    }

    public static Sprite RoundedGradient(
        string key,
        int width,
        int height,
        float radius,
        Color top,
        Color bottom)
        => RoundedGradientCorners(key, width, height, radius, radius, radius, radius, top, bottom);

    public static Sprite RoundedGradientCorners(
        string key,
        int width,
        int height,
        float topLeftRadius,
        float topRightRadius,
        float bottomRightRadius,
        float bottomLeftRadius,
        Color top,
        Color bottom)
    {
        width = Math.Max(4, width);
        height = Math.Max(4, height);
        var maximumRadius = Math.Min(width, height) * 0.5f;
        topLeftRadius = Mathf.Clamp(topLeftRadius, 0f, maximumRadius);
        topRightRadius = Mathf.Clamp(topRightRadius, 0f, maximumRadius);
        bottomRightRadius = Mathf.Clamp(bottomRightRadius, 0f, maximumRadius);
        bottomLeftRadius = Mathf.Clamp(bottomLeftRadius, 0f, maximumRadius);
        var cacheKey = "generated-rounded-corners|"
                        + (key ?? "")
                        + "|"
                        + width
                        + "x"
                        + height
                        + "|r="
                        + topLeftRadius.ToString("0.###", CultureInfo.InvariantCulture)
                        + ","
                        + topRightRadius.ToString("0.###", CultureInfo.InvariantCulture)
                        + ","
                        + bottomRightRadius.ToString("0.###", CultureInfo.InvariantCulture)
                        + ","
                        + bottomLeftRadius.ToString("0.###", CultureInfo.InvariantCulture)
                       + "|t="
                       + ColorKey(top)
                       + "|b="
                       + ColorKey(bottom);
        if (Cache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "TerriasRounded-" + (key ?? "ui"),
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            var gradient = height <= 1 ? 1f : (y + 0.5f) / height;
            var rowColor = Color.Lerp(bottom, top, gradient);
            for (var x = 0; x < width; x++)
            {
                var value = rowColor;
                value.a *= CornerAlpha(
                    x + 0.5f,
                    y + 0.5f,
                    width,
                    height,
                    topLeftRadius,
                    topRightRadius,
                    bottomRightRadius,
                    bottomLeftRadius);
                pixels[y * width + x] = value;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = texture.name;
        GeneratedTextures[cacheKey] = texture;
        Cache[cacheKey] = sprite;
        return sprite;
    }

    private static float CornerAlpha(
        float x,
        float y,
        float width,
        float height,
        float topLeftRadius,
        float topRightRadius,
        float bottomRightRadius,
        float bottomLeftRadius)
    {
        if (topLeftRadius > 0f && x < topLeftRadius && y > height - topLeftRadius)
            return CircleAlpha(x, y, topLeftRadius, height - topLeftRadius, topLeftRadius);
        if (topRightRadius > 0f && x > width - topRightRadius && y > height - topRightRadius)
            return CircleAlpha(x, y, width - topRightRadius, height - topRightRadius, topRightRadius);
        if (bottomRightRadius > 0f && x > width - bottomRightRadius && y < bottomRightRadius)
            return CircleAlpha(x, y, width - bottomRightRadius, bottomRightRadius, bottomRightRadius);
        if (bottomLeftRadius > 0f && x < bottomLeftRadius && y < bottomLeftRadius)
            return CircleAlpha(x, y, bottomLeftRadius, bottomLeftRadius, bottomLeftRadius);
        return 1f;
    }

    private static float CircleAlpha(float x, float y, float centerX, float centerY, float radius)
    {
        var deltaX = x - centerX;
        var deltaY = y - centerY;
        var distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return Mathf.Clamp01(radius + 0.5f - distance);
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
            var source = TerriasResourceCache.Load<Sprite>(path, true, "ui.sprite-source");
            if (source == null || source.texture == null)
            {
                TerriasLog.Warn(logPrefix + " UI sprite missing: " + path);
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
            TerriasLog.Warn(logPrefix + " failed to load UI sprite " + path + ": " + ex.Message);
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

    private static string ColorKey(Color color)
    {
        return color.r.ToString("0.###", CultureInfo.InvariantCulture)
               + ","
               + color.g.ToString("0.###", CultureInfo.InvariantCulture)
               + ","
               + color.b.ToString("0.###", CultureInfo.InvariantCulture)
               + ","
               + color.a.ToString("0.###", CultureInfo.InvariantCulture);
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
