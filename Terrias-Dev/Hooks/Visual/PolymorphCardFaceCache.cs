using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Visual;

public sealed class PolymorphCardFaceAsset
{
    public PolymorphCardFaceAsset(Texture2D texture, Sprite sprite)
    {
        Texture = texture;
        Sprite = sprite;
    }

    public Texture2D Texture { get; }

    public Sprite Sprite { get; }
}

public static class PolymorphCardFaceCache
{
    public const int OutputSize = 256;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PolymorphCardFaceAsset> Generated = new(StringComparer.Ordinal);

    public static PolymorphCardFaceAsset? GetOrCreate(PolymorphRoleSpec role)
    {
        return GetOrCreate(role.Id, role.CardFacePath, role.CropOffsetX, role.CropOffsetY, role.CropSize);
    }

    public static PolymorphCardFaceAsset? GetOrCreate(IDataConfig? config)
    {
        if (!IsPolymorphRoleCard(config))
        {
            return null;
        }

        var roleId = FirstNonEmpty(
            DictionaryUtil.Get(config?.Vars, SunExpIds.PolymorphRoleIdKey),
            DictionaryUtil.Get(config?.Vars, SunExpIds.ProjectionRoleIdKey));
        var role = PolymorphRoleRegistry.Find(roleId);
        var path = FirstNonEmpty(
            DictionaryUtil.Get(config?.Vars, SunExpIds.PolymorphRoleCardFacePathKey),
            DictionaryUtil.Get(config?.Vars, SunExpIds.ProjectionRoleCardFacePathKey),
            role?.CardFacePath ?? "",
            DictionaryUtil.Get(config?.data, "Icon"),
            SunExpIds.PolymorphPlaceholderCardIconPath);
        var crop = PolymorphRoleCropRegistry.CropFor(roleId);
        var offsetX = DictionaryUtil.GetInt(config?.Vars, SunExpIds.PolymorphRoleCropXKey, crop.OffsetX);
        var offsetY = DictionaryUtil.GetInt(config?.Vars, SunExpIds.PolymorphRoleCropYKey, crop.OffsetY);
        var cropSize = role?.CropSize ?? crop.Size;
        return GetOrCreate(roleId, path, offsetX, offsetY, cropSize);
    }

    public static bool IsPolymorphRoleCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        return string.Equals(DictionaryUtil.Get(config.data, "Id"), SunExpIds.PolymorphRoleTemplateCardId, StringComparison.Ordinal)
            || string.Equals(DictionaryUtil.Get(config.data, "Id"), SunExpIds.PolymorphRoleTemplateShortId, StringComparison.Ordinal)
            || string.Equals(DictionaryUtil.Get(config.data, "Id"), SunExpIds.ProjectionRoleTemplateCardId, StringComparison.Ordinal)
            || string.Equals(DictionaryUtil.Get(config.data, "Id"), SunExpIds.ProjectionRoleTemplateShortId, StringComparison.Ordinal)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.PolymorphRoleCardMarker)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.ProjectionRoleCardMarker);
    }

    public static void Warmup(IEnumerable<PolymorphRoleSpec> roles, int immediateCount, int batchSize)
    {
        var list = roles.Where(role => !string.IsNullOrWhiteSpace(role.CardFacePath)).ToList();
        if (list.Count == 0)
        {
            return;
        }

        var immediate = Math.Max(0, immediateCount);
        for (var i = 0; i < Math.Min(immediate, list.Count); i++)
        {
            GetOrCreate(list[i]);
        }

        var start = Math.Min(immediate, list.Count);
        if (start < list.Count)
        {
            ScheduleWarmupBatch(list, start, Math.Max(1, batchSize));
        }
    }

    public static void ClearGenerated(string source)
    {
        PolymorphCardFaceAsset[] assets;
        lock (SyncRoot)
        {
            if (Generated.Count == 0)
            {
                return;
            }

            assets = Generated.Values.ToArray();
            Generated.Clear();
        }

        foreach (var asset in assets)
        {
            SafeDestroy(asset.Sprite);
            SafeDestroy(asset.Texture);
        }

        SunExpPerformanceCounters.Record("Polymorph.CardFacesCleared");
        SunExpLog.Debug("[PolymorphCardFace] cleared generated faces from " + source + ": " + assets.Length);
    }

    private static PolymorphCardFaceAsset? GetOrCreate(string roleId, string path, int offsetX, int offsetY, int cropSize)
    {
        var normalizedPath = FirstNonEmpty(path, SunExpIds.PolymorphPlaceholderCardIconPath);
        var key = roleId + "\u001f" + normalizedPath + "\u001f" + offsetX + "\u001f" + offsetY + "\u001f" + cropSize + "\u001f" + OutputSize;
        lock (SyncRoot)
        {
            if (Generated.TryGetValue(key, out var cached))
            {
                SunExpPerformanceCounters.Record("Polymorph.CardFaceCacheHit");
                return cached;
            }
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var source = SunExpResourceCache.Load<Sprite>(
                normalizedPath,
                true,
                SunExpIds.PolymorphSourceResourceCategory);
            if (source == null || source.texture == null)
            {
                return null;
            }

            var texture = RenderCropTexture(source, offsetX, offsetY, cropSize);
            if (texture == null)
            {
                return null;
            }

            texture.name = "SunExp_PolymorphCardFace_" + Sanitize(roleId);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, OutputSize, OutputSize),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = texture.name + "_Sprite";
            var asset = new PolymorphCardFaceAsset(texture, sprite);
            lock (SyncRoot)
            {
                Generated[key] = asset;
            }

            SunExpPerformanceCounters.Record("Polymorph.CardFaceGenerated");
            SunExpPerformanceCounters.Record("Polymorph.CardFaceGenerated.256");
            return asset;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[PolymorphCardFace] failed to create face for " + roleId + ": " + ex.Message);
            return null;
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Polymorph.CardFaceGenerate", start);
            SunExpCombatCardUiDiagnostics.RecordCurrentSegment("Polymorph.CardFaceGenerate", start);
        }
    }

    private static Texture2D? RenderCropTexture(Sprite source, int offsetX, int offsetY, int cropSize)
    {
        var texture = source.texture;
        var sourceRect = source.rect;
        var size = Mathf.Min(Mathf.Max(1, cropSize), sourceRect.width, sourceRect.height);
        var x = sourceRect.x + (sourceRect.width - size) * 0.5f + offsetX;
        var y = sourceRect.y + sourceRect.height - size - offsetY;
        x = Mathf.Clamp(x, sourceRect.x, sourceRect.xMax - size);
        y = Mathf.Clamp(y, sourceRect.y, sourceRect.yMax - size);

        var uv = new Rect(
            x / texture.width,
            y / texture.height,
            size / texture.width,
            size / texture.height);
        var target = new Texture2D(OutputSize, OutputSize, TextureFormat.RGBA32, false);
        var rt = RenderTexture.GetTemporary(OutputSize, OutputSize, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var pushed = false;
        try
        {
            RenderTexture.active = rt;
            GL.PushMatrix();
            pushed = true;
            GL.LoadPixelMatrix(0, OutputSize, OutputSize, 0);
            GL.Clear(true, true, Color.clear);
            Graphics.DrawTexture(new Rect(0, 0, OutputSize, OutputSize), texture, uv, 0, 0, 0, 0);
            target.ReadPixels(new Rect(0, 0, OutputSize, OutputSize), 0, 0, false);
            target.Apply(false, false);
            return target;
        }
        catch
        {
            SafeDestroy(target);
            throw;
        }
        finally
        {
            if (pushed)
            {
                GL.PopMatrix();
            }

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static void ScheduleWarmupBatch(IReadOnlyList<PolymorphRoleSpec> roles, int start, int batchSize)
    {
        SunExpFrameScheduler.RunOnceNextFrame("PolymorphCardFace.Warmup." + start, () =>
        {
            var end = Math.Min(roles.Count, start + batchSize);
            for (var i = start; i < end; i++)
            {
                GetOrCreate(roles[i]);
            }

            if (end < roles.Count)
            {
                ScheduleWarmupBatch(roles, end, batchSize);
            }
        });
    }

    private static void SafeDestroy(Object? target)
    {
        if (target == null)
        {
            return;
        }

        try
        {
            Object.Destroy(target);
        }
        catch
        {
            // Unity can reject destruction during unusual teardown; the cache has already released the reference.
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string Sanitize(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            chars[i] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_';
        }

        return new string(chars);
    }
}
