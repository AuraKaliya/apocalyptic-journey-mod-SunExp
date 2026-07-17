using System;

namespace AuraCg.Shared;

internal readonly struct AuraCgLayoutPoint
{
    public AuraCgLayoutPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float X { get; }

    public float Y { get; }
}

internal static class AuraCgPresentationMath
{
    private const float SlideImageHeightRatio = 0.85f;
    private const float SlideStartXRatio = 1.18f;
    internal const float SlideEndXRatio = -0.18f;
    private const float SlideCenterSlowStrength = 0.65f;
    private const float AlphaFadeInStartXRatio = 1.05f;
    private const float AlphaFadeInEndXRatio = 0.82f;
    private const float AlphaFadeOutStartXRatio = 0.18f;
    private const float AlphaFadeOutEndXRatio = -0.05f;

    public static AuraCgLayoutPoint CalculateSlideImageSize(
        float spriteWidth,
        float spriteHeight,
        float viewportWidth,
        float viewportHeight)
    {
        var aspect = spriteHeight <= 0f ? 1f : spriteWidth / spriteHeight;
        var height = Math.Max(1f, viewportHeight * SlideImageHeightRatio);
        return new AuraCgLayoutPoint(height * aspect, height);
    }

    public static AuraCgLayoutPoint CalculateCoverImageSize(
        float spriteWidth,
        float spriteHeight,
        float viewportWidth,
        float viewportHeight,
        float safeScale)
    {
        var aspect = spriteHeight <= 0f ? 1f : spriteWidth / spriteHeight;
        var viewportAspect = viewportHeight <= 0f ? 1f : viewportWidth / viewportHeight;
        var scale = Clamp(safeScale <= 0f ? 1f : safeScale, 1f, 3f);
        if (aspect >= viewportAspect)
        {
            var height = Math.Max(1f, viewportHeight) * scale;
            return new AuraCgLayoutPoint(height * aspect, height);
        }

        var width = Math.Max(1f, viewportWidth) * scale;
        return new AuraCgLayoutPoint(width, width / Math.Max(0.001f, aspect));
    }

    public static AuraCgLayoutPoint CalculateCoverImageOffset(
        float imageWidth,
        float imageHeight,
        float viewportWidth,
        float viewportHeight,
        float focusX,
        float focusY)
    {
        var overflowX = Math.Max(0f, imageWidth - viewportWidth);
        var overflowY = Math.Max(0f, imageHeight - viewportHeight);
        return new AuraCgLayoutPoint(
            Clamp((0.5f - Clamp01(focusX)) * overflowX, -overflowX * 0.5f, overflowX * 0.5f),
            Clamp((Clamp01(focusY) - 0.5f) * overflowY, -overflowY * 0.5f, overflowY * 0.5f));
    }

    public static float EvaluateSlideXRatio(float progress)
    {
        var t = Clamp01(progress);
        var remappedProgress = Clamp01(
            t + SlideCenterSlowStrength * (float)Math.Sin(2f * Math.PI * t) / (2f * (float)Math.PI));
        return Lerp(SlideStartXRatio, SlideEndXRatio, remappedProgress);
    }

    public static float EvaluateSlideAlpha(float xRatio)
    {
        if (xRatio >= AlphaFadeInStartXRatio || xRatio <= AlphaFadeOutEndXRatio)
        {
            return 0f;
        }

        if (xRatio > AlphaFadeInEndXRatio)
        {
            return InverseLerp(AlphaFadeInStartXRatio, AlphaFadeInEndXRatio, xRatio);
        }

        if (xRatio < AlphaFadeOutStartXRatio)
        {
            return InverseLerp(AlphaFadeOutEndXRatio, AlphaFadeOutStartXRatio, xRatio);
        }

        return 1f;
    }

    public static float ScreenBwPulse(int localFrame)
    {
        return localFrame switch
        {
            0 => 1.0f,
            1 => 0.82f,
            2 => 0.68f,
            3 => 0.48f,
            4 => 0.34f,
            5 => 0.24f,
            6 => 0.16f,
            _ => 0.08f
        };
    }

    private static float InverseLerp(float from, float to, float value)
    {
        if (Math.Abs(to - from) <= float.Epsilon)
        {
            return 0f;
        }

        return Clamp01((value - from) / (to - from));
    }

    private static float Lerp(float from, float to, float amount)
    {
        return from + (to - from) * Clamp01(amount);
    }

    private static float Clamp01(float value)
    {
        return Clamp(value, 0f, 1f);
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return Math.Min(maximum, Math.Max(minimum, value));
    }
}
