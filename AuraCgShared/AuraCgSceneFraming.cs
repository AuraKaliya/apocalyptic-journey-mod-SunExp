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
