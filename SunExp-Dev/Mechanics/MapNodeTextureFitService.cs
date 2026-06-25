using System;

namespace SunExp.Dll.Mechanics;

public static class MapNodeTextureFitService
{
    public const float DefaultFightBoundsWidth = 160f;
    public const float DefaultFightBoundsHeight = 238f;
    public const float DefaultAlphaThreshold = 0.1f;

    public static MapNodeCardArtFitResult Fit(
        MapNodeTextureBounds texture,
        MapNodeCardArtFitMode fitMode,
        float boundsWidth = DefaultFightBoundsWidth,
        float boundsHeight = DefaultFightBoundsHeight,
        float offsetX = 0f,
        float offsetY = 0f)
    {
        if (fitMode == MapNodeCardArtFitMode.StretchLegacy)
        {
            return new MapNodeCardArtFitResult(false, 0f, 0f, offsetX, offsetY);
        }

        var width = Math.Max(1, texture.Width);
        var height = Math.Max(1, texture.Height);
        var targetWidth = boundsWidth > 0f ? boundsWidth : DefaultFightBoundsWidth;
        var targetHeight = boundsHeight > 0f ? boundsHeight : DefaultFightBoundsHeight;
        var trim = NormalizeTrim(texture, width, height);

        var visibleWidth = width;
        var visibleHeight = height;
        var pivotOffsetX = 0f;
        var pivotOffsetY = 0f;
        if (fitMode == MapNodeCardArtFitMode.ContainTrimmed)
        {
            visibleWidth = Math.Max(1, width - trim.Left - trim.Right);
            visibleHeight = Math.Max(1, height - trim.Top - trim.Bottom);
            pivotOffsetX = (trim.Right - trim.Left) / 2f;
            pivotOffsetY = (trim.Top - trim.Bottom) / 2f;
        }

        var scaleX = (float)width;
        var scaleY = (float)height;
        var factor = 1f;
        if (visibleWidth > targetWidth)
        {
            factor = targetWidth / visibleWidth;
            scaleX *= factor;
            scaleY *= factor;
            visibleWidth = (int)Math.Round(visibleWidth * factor);
            visibleHeight = (int)Math.Round(visibleHeight * factor);
            pivotOffsetX *= factor;
            pivotOffsetY *= factor;
        }

        if (visibleHeight > targetHeight)
        {
            factor = targetHeight / visibleHeight;
            scaleX *= factor;
            scaleY *= factor;
            pivotOffsetX *= factor;
            pivotOffsetY *= factor;
        }

        return new MapNodeCardArtFitResult(
            true,
            scaleX,
            scaleY,
            pivotOffsetX + offsetX,
            pivotOffsetY + offsetY);
    }

    private static Trim NormalizeTrim(MapNodeTextureBounds texture, int width, int height)
    {
        var left = Clamp(texture.LeftTransparentWidth, 0, width - 1);
        var right = Clamp(texture.RightTransparentWidth, 0, width - 1);
        if (left + right >= width)
        {
            left = 0;
            right = 0;
        }

        var top = Clamp(texture.TopTransparentHeight, 0, height - 1);
        var bottom = Clamp(texture.BottomTransparentHeight, 0, height - 1);
        if (top + bottom >= height)
        {
            top = 0;
            bottom = 0;
        }

        return new Trim(left, right, top, bottom);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private readonly struct Trim
    {
        public Trim(int left, int right, int top, int bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public int Left { get; }

        public int Right { get; }

        public int Top { get; }

        public int Bottom { get; }
    }
}
