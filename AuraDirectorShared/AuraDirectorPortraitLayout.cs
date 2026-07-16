using System;

namespace AuraDirector.Shared;

public readonly struct AuraDirectorPortraitLayoutResult
{
    public AuraDirectorPortraitLayoutResult(
        double barHeight,
        double safeHeight,
        double displayWidth,
        double displayHeight,
        double sourceCenterX,
        double sourceCenterY,
        double unitsToPixels)
    {
        BarHeight = barHeight;
        SafeHeight = safeHeight;
        DisplayWidth = displayWidth;
        DisplayHeight = displayHeight;
        SourceCenterX = sourceCenterX;
        SourceCenterY = sourceCenterY;
        UnitsToPixels = unitsToPixels;
    }

    public double BarHeight { get; }

    public double SafeHeight { get; }

    public double DisplayWidth { get; }

    public double DisplayHeight { get; }

    public double SourceCenterX { get; }

    public double SourceCenterY { get; }

    public double UnitsToPixels { get; }
}

public static class AuraDirectorPortraitLayout
{
    public const double VerticalInsetPixels = 10d;
    public const double OffscreenMarginPixels = 10d;

    public static AuraDirectorPortraitLayoutResult Calculate(
        double screenHeight,
        double focusBarRatio,
        double sourceMinX,
        double sourceMinY,
        double sourceMaxX,
        double sourceMaxY)
    {
        var height = PositiveOrFallback(screenHeight, 1d);
        var ratio = Clamp(focusBarRatio, 0d, 0.45d, 0d);
        var maximumBarHeight = Math.Max(0d, (height - VerticalInsetPixels * 2d - 1d) * 0.5d);
        var barHeight = Math.Min(height * ratio, maximumBarHeight);
        var safeHeight = Math.Max(1d, height - barHeight * 2d - VerticalInsetPixels * 2d);

        var sourceWidth = sourceMaxX - sourceMinX;
        var sourceHeight = sourceMaxY - sourceMinY;
        if (!IsFinitePositive(sourceWidth) || !IsFinitePositive(sourceHeight))
        {
            sourceMinX = -0.5d;
            sourceMaxX = 0.5d;
            sourceMinY = -0.5d;
            sourceMaxY = 0.5d;
            sourceWidth = 1d;
            sourceHeight = 1d;
        }

        var unitsToPixels = safeHeight / sourceHeight;
        var displayWidth = Math.Max(1d, sourceWidth * unitsToPixels);
        return new AuraDirectorPortraitLayoutResult(
            barHeight,
            safeHeight,
            displayWidth,
            safeHeight,
            (sourceMinX + sourceMaxX) * 0.5d,
            (sourceMinY + sourceMaxY) * 0.5d,
            unitsToPixels);
    }

    public static double ResolveAnchoredX(
        double focusRatio,
        double screenWidth,
        double displayWidth)
    {
        var width = PositiveOrFallback(screenWidth, 1d);
        var portraitWidth = PositiveOrFallback(displayWidth, 1d);
        var ratio = Clamp(focusRatio, -2d, 3d, 0.5d);
        var planned = (ratio - 0.5d) * width;
        var fullyOutside = width * 0.5d + portraitWidth * 0.5d + OffscreenMarginPixels;
        if (ratio >= 1d)
        {
            return Math.Max(planned, fullyOutside);
        }
        if (ratio <= 0d)
        {
            return Math.Min(planned, -fullyOutside);
        }
        return planned;
    }

    private static double PositiveOrFallback(double value, double fallback)
    {
        return IsFinitePositive(value) ? value : fallback;
    }

    private static bool IsFinitePositive(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}
