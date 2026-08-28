using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuraCg.Shared;

internal static class AuraCgSceneVisibleBoundsAnalyzer
{
    private const int MaximumCacheEntries = 256;
    private const byte AlphaThreshold = 8;
    private readonly static Dictionary<string, AuraCgNormalizedBounds> Cache = new(StringComparer.Ordinal);
    private readonly static Queue<string> CacheOrder = new();

    internal static AuraCgNormalizedBounds Resolve(IReadOnlyList<Sprite> frames, string assetKey)
    {
        if (frames == null || frames.Count == 0) return AuraCgNormalizedBounds.Full;
        var first = frames[0];
        var last = frames[frames.Count - 1];
        var key = (assetKey ?? "") + "|" + frames.Count + "|"
                  + (first == null ? 0 : first.GetInstanceID()) + "|"
                  + (last == null ? 0 : last.GetInstanceID());
        if (Cache.TryGetValue(key, out var cached)) return cached;

        AuraCgNormalizedBounds? union = null;
        foreach (var frame in frames)
        {
            if (frame == null || !TryAnalyze(frame, out var bounds)) continue;
            union = union.HasValue ? union.Value.Union(bounds) : bounds;
        }

        var result = union ?? AuraCgNormalizedBounds.Full;
        Cache[key] = result;
        CacheOrder.Enqueue(key);
        while (CacheOrder.Count > MaximumCacheEntries)
        {
            Cache.Remove(CacheOrder.Dequeue());
        }
        return result;
    }

    private static bool TryAnalyze(Sprite sprite, out AuraCgNormalizedBounds bounds)
    {
        bounds = AuraCgNormalizedBounds.Full;
        var texture = sprite.texture;
        if (texture == null || !texture.isReadable) return false;
        var spriteRect = sprite.textureRect;
        var width = Math.Max(1, Mathf.RoundToInt(spriteRect.width));
        var height = Math.Max(1, Mathf.RoundToInt(spriteRect.height));
        var originX = Math.Max(0, Mathf.RoundToInt(spriteRect.x));
        var originY = Math.Max(0, Mathf.RoundToInt(spriteRect.y));
        Color32[] pixels;
        try
        {
            pixels = texture.GetPixels32();
        }
        catch
        {
            return false;
        }
        if (pixels.Length < texture.width * texture.height) return false;

        var step = Math.Max(1, Math.Max(width, height) / 256);
        var minimumX = width;
        var minimumY = height;
        var maximumX = -1;
        var maximumY = -1;
        for (var localY = 0; localY < height; localY += step)
        {
            var textureY = Math.Min(texture.height - 1, originY + localY);
            var row = textureY * texture.width;
            for (var localX = 0; localX < width; localX += step)
            {
                var textureX = Math.Min(texture.width - 1, originX + localX);
                if (pixels[row + textureX].a <= AlphaThreshold) continue;
                minimumX = Math.Min(minimumX, localX);
                minimumY = Math.Min(minimumY, localY);
                maximumX = Math.Max(maximumX, localX);
                maximumY = Math.Max(maximumY, localY);
            }
        }
        if (maximumX < minimumX || maximumY < minimumY) return false;

        minimumX = Math.Max(0, minimumX - step);
        minimumY = Math.Max(0, minimumY - step);
        maximumX = Math.Min(width - 1, maximumX + step);
        maximumY = Math.Min(height - 1, maximumY + step);
        bounds = new AuraCgNormalizedBounds(
            minimumX / (float)width,
            minimumY / (float)height,
            (maximumX - minimumX + 1f) / width,
            (maximumY - minimumY + 1f) / height);
        return true;
    }
}
