using System;

namespace AuraToolsExp.Dll.Features.Settings;

internal readonly struct AuraToolsPreparationDockPlacement
{
    internal AuraToolsPreparationDockPlacement(float x, float y)
    {
        X = x;
        Y = y;
    }

    internal float X { get; }
    internal float Y { get; }
}

internal static class AuraToolsPreparationDockLayoutPolicy
{
    internal static AuraToolsPreparationDockPlacement AboveReadyButton(
        float readyX,
        float readyY,
        float readyWidth,
        float readyHeight,
        float readyPivotX,
        float readyPivotY,
        float dockWidth,
        float parentLeft,
        float parentRight,
        float gap = 8f,
        float horizontalMargin = 8f)
    {
        readyWidth = Math.Max(0f, readyWidth);
        readyHeight = Math.Max(0f, readyHeight);
        dockWidth = Math.Max(0f, dockWidth);
        var readyCenterX = readyX + readyWidth * (0.5f - readyPivotX);
        var minimumCenter = parentLeft + horizontalMargin + dockWidth * 0.5f;
        var maximumCenter = parentRight - horizontalMargin - dockWidth * 0.5f;
        var centerX = minimumCenter <= maximumCenter
            ? Math.Max(minimumCenter, Math.Min(maximumCenter, readyCenterX))
            : (parentLeft + parentRight) * 0.5f;
        var readyTop = readyY + readyHeight * (1f - readyPivotY);
        return new AuraToolsPreparationDockPlacement(centerX, readyTop + Math.Max(0f, gap));
    }
}
