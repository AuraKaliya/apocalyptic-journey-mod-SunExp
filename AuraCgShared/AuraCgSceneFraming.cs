using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCg.Shared;

internal readonly struct AuraCgNormalizedBounds
{
    internal static readonly AuraCgNormalizedBounds Full = new(0f, 0f, 1f, 1f);

    internal AuraCgNormalizedBounds(float x, float y, float width, float height)
    {
        X = Clamp01(x);
        Y = Clamp01(y);
        Width = Math.Max(0.001f, Math.Min(1f - X, width));
        Height = Math.Max(0.001f, Math.Min(1f - Y, height));
    }

    internal float X { get; }
    internal float Y { get; }
    internal float Width { get; }
    internal float Height { get; }

    internal AuraCgNormalizedBounds Union(AuraCgNormalizedBounds other)
    {
        var left = Math.Min(X, other.X);
        var bottom = Math.Min(Y, other.Y);
        var right = Math.Max(X + Width, other.X + other.Width);
        var top = Math.Max(Y + Height, other.Y + other.Height);
        return new AuraCgNormalizedBounds(left, bottom, right - left, top - bottom);
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}

internal readonly struct AuraCgSceneFramingResult
{
    internal AuraCgSceneFramingResult(float imageWidth, float imageHeight, float offsetX, float offsetY)
    {
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    internal float ImageWidth { get; }
    internal float ImageHeight { get; }
    internal float OffsetX { get; }
    internal float OffsetY { get; }
}

internal static class AuraCgSceneFramingMath
{
    internal static AuraCgSceneFramingResult FitVisibleBounds(
        AuraCgNormalizedBounds bounds,
        float canvasWidth,
        float canvasHeight,
        float slotWidth,
        float slotHeight)
    {
        var safeCanvasWidth = Math.Max(1f, canvasWidth);
        var safeCanvasHeight = Math.Max(1f, canvasHeight);
        var safeSlotWidth = Math.Max(1f, slotWidth);
        var safeSlotHeight = Math.Max(1f, slotHeight);
        var visibleWidth = Math.Max(1f, safeCanvasWidth * bounds.Width);
        var visibleHeight = Math.Max(1f, safeCanvasHeight * bounds.Height);
        var scale = Math.Min(safeSlotWidth / visibleWidth, safeSlotHeight / visibleHeight);
        var imageWidth = safeCanvasWidth * scale;
        var imageHeight = safeCanvasHeight * scale;
        var visibleCenterX = bounds.X + bounds.Width * 0.5f;
        return new AuraCgSceneFramingResult(
            imageWidth,
            imageHeight,
            (0.5f - visibleCenterX) * imageWidth,
            -bounds.Y * imageHeight);
    }

    internal static float VisibleAspect(
        AuraCgNormalizedBounds bounds,
        float canvasWidth,
        float canvasHeight)
    {
        return Math.Max(0.001f, canvasWidth * bounds.Width)
               / Math.Max(0.001f, canvasHeight * bounds.Height);
    }
}

internal static class AuraCgSceneLayoutFallbackPolicy
{
    internal static bool UsePortraitPanels(int participantCount, IEnumerable<float>? visibleAspects)
    {
        if (participantCount < 7) return false;
        var aspects = (visibleAspects ?? Array.Empty<float>()).Where(value => value > 0f).ToArray();
        return aspects.Any(value => value >= 0.86f);
    }
}

internal readonly struct AuraCgSceneProfileIdentity
{
    internal AuraCgSceneProfileIdentity(string id, string title, string subtitle)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
    }

    internal string Id { get; }
    internal string Title { get; }
    internal string Subtitle { get; }

    internal static AuraCgSceneProfileIdentity Resolve(string presentationProfileId, string sceneId)
    {
        var key = ((presentationProfileId ?? "") + "|" + (sceneId ?? "")).ToLowerInvariant();
        if (key.Contains("midas")) return new AuraCgSceneProfileIdentity("midas", "点金手胜利", "财富终局");
        if (key.Contains("ritual")) return new AuraCgSceneProfileIdentity("ritual", "仪式胜利", "仪式终局");
        if (key.Contains("curse")) return new AuraCgSceneProfileIdentity("curse", "诅咒胜利", "七咒终局");
        if (key.Contains("defeat")) return new AuraCgSceneProfileIdentity("defeat", "战斗失败", "队伍撤退");
        if (key.Contains("opening")) return new AuraCgSceneProfileIdentity("opening", "战斗开场", "队伍集结");
        if (key.Contains("settlement")) return new AuraCgSceneProfileIdentity("settlement", "冒险结算", "旅途留影");
        return new AuraCgSceneProfileIdentity("victory", "普通胜利", "冒险队伍");
    }
}
