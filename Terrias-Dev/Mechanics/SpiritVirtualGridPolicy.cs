using System;

namespace Terrias.Dll.Mechanics;

public static class SpiritVirtualGridPolicy
{
    public const int OverscanRows = 2;

    public static int RequiredCellCount(float viewportHeight, float cellHeight, float verticalSpacing, int columns)
    {
        var safeColumns = Math.Max(1, columns);
        var rowHeight = Math.Max(1f, cellHeight + verticalSpacing);
        var visibleRows = (int)Math.Ceiling(Math.Max(1f, viewportHeight) / rowHeight);
        return Math.Max(safeColumns * 2, (visibleRows + OverscanRows) * safeColumns);
    }

    public static int FirstVisibleRow(float contentOffsetY, float topPadding, float cellHeight, float verticalSpacing)
    {
        var rowHeight = Math.Max(1f, cellHeight + verticalSpacing);
        return Math.Max(0, (int)Math.Floor((Math.Max(0f, contentOffsetY) - Math.Max(0f, topPadding)) / rowHeight) - 1);
    }

    public static float ContentHeight(int itemCount, int columns, float cellHeight, float verticalSpacing, float topPadding, float bottomPadding)
    {
        var safeColumns = Math.Max(1, columns);
        var rows = itemCount <= 0 ? 0 : (itemCount + safeColumns - 1) / safeColumns;
        return Math.Max(0f, Math.Max(0f, topPadding) + Math.Max(0f, bottomPadding)
                            + rows * Math.Max(0f, cellHeight)
                            + Math.Max(0, rows - 1) * Math.Max(0f, verticalSpacing));
    }
}
