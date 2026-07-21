using System;

namespace SunExp.Dll.Mechanics;

public readonly struct ModeChoiceDragRange
{
    public ModeChoiceDragRange(
        float viewportWidth,
        float minOffset,
        float maxOffset,
        float defaultOffset)
    {
        ViewportWidth = viewportWidth;
        MinOffset = minOffset;
        MaxOffset = maxOffset;
        DefaultOffset = defaultOffset;
    }

    public float ViewportWidth { get; }

    public float MinOffset { get; }

    public float MaxOffset { get; }

    public float DefaultOffset { get; }

    public bool DragEnabled => MaxOffset - MinOffset > 1f;
}

public static class ModeChoiceDragRangeService
{
    public static ModeChoiceDragRange Calculate(
        float contentMinX,
        float contentMaxX,
        float entryWidth,
        int entryCount,
        float entryGap,
        float parentViewportWidth,
        int visibleEntryCount,
        float sidePadding)
    {
        var safeEntryWidth = Math.Max(1f, entryWidth);
        var safeEntryCount = Math.Max(1, entryCount);
        var safeVisibleCount = Math.Min(safeEntryCount, Math.Max(1, visibleEntryCount));
        var safeGap = Math.Max(0f, entryGap);
        var availableWidth = Math.Max(safeEntryWidth, parentViewportWidth - (Math.Max(0f, sidePadding) * 2f));
        var visibleSlotsWidth = (safeEntryWidth * safeVisibleCount) + (safeGap * Math.Max(0, safeVisibleCount - 1));
        var viewportWidth = Math.Max(safeEntryWidth, Math.Min(availableWidth, visibleSlotsWidth));
        var viewMinX = -viewportWidth / 2f;
        var viewMaxX = viewportWidth / 2f;
        var minOffset = Math.Min(0f, viewMaxX - contentMaxX);
        var maxOffset = Math.Max(0f, viewMinX - contentMinX);

        return new ModeChoiceDragRange(
            viewportWidth,
            minOffset,
            maxOffset,
            maxOffset);
    }
}
