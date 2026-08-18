using System;

namespace AuraToolsExp.Dll.Features.Settings;

internal readonly struct ToolboxTooltipBounds
{
    internal ToolboxTooltipBounds(float minimumX, float minimumY, float maximumX, float maximumY)
    {
        MinimumX = Math.Min(minimumX, maximumX);
        MinimumY = Math.Min(minimumY, maximumY);
        MaximumX = Math.Max(minimumX, maximumX);
        MaximumY = Math.Max(minimumY, maximumY);
    }

    internal float MinimumX { get; }
    internal float MinimumY { get; }
    internal float MaximumX { get; }
    internal float MaximumY { get; }
    internal float CenterX => (MinimumX + MaximumX) * 0.5f;
}

internal readonly struct ToolboxTooltipPlacement
{
    internal ToolboxTooltipPlacement(float centerX, float centerY, bool aboveAnchor)
    {
        CenterX = centerX;
        CenterY = centerY;
        AboveAnchor = aboveAnchor;
    }

    internal float CenterX { get; }
    internal float CenterY { get; }
    internal bool AboveAnchor { get; }
}

internal static class ToolboxTooltipPlacementPolicy
{
    internal const float DefaultGap = 8f;
    internal const float DefaultMargin = 8f;

    internal static ToolboxTooltipPlacement Resolve(
        ToolboxTooltipBounds container,
        ToolboxTooltipBounds anchor,
        float tooltipWidth,
        float tooltipHeight,
        float gap = DefaultGap,
        float margin = DefaultMargin)
    {
        tooltipWidth = Math.Max(1f, tooltipWidth);
        tooltipHeight = Math.Max(1f, tooltipHeight);
        gap = Math.Max(0f, gap);
        margin = Math.Max(0f, margin);

        var halfWidth = tooltipWidth * 0.5f;
        var halfHeight = tooltipHeight * 0.5f;
        var minimumCenterX = container.MinimumX + margin + halfWidth;
        var maximumCenterX = container.MaximumX - margin - halfWidth;
        var centerX = Clamp(anchor.CenterX, minimumCenterX, maximumCenterX);

        var belowCenterY = anchor.MinimumY - gap - halfHeight;
        var aboveCenterY = anchor.MaximumY + gap + halfHeight;
        var fitsBelow = belowCenterY - halfHeight >= container.MinimumY + margin;
        var fitsAbove = aboveCenterY + halfHeight <= container.MaximumY - margin;
        var aboveAnchor = !fitsBelow && fitsAbove;
        var centerY = aboveAnchor ? aboveCenterY : belowCenterY;
        centerY = Clamp(
            centerY,
            container.MinimumY + margin + halfHeight,
            container.MaximumY - margin - halfHeight);
        return new ToolboxTooltipPlacement(centerX, centerY, aboveAnchor);
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        if (minimum > maximum)
        {
            return (minimum + maximum) * 0.5f;
        }

        return Math.Max(minimum, Math.Min(maximum, value));
    }
}
