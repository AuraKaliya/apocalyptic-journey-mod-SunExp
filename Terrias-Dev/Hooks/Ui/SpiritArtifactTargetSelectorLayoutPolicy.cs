using System;

namespace Terrias.Dll.Hooks.Ui;

internal readonly struct SpiritArtifactTargetSelectorLayout
{
    public SpiritArtifactTargetSelectorLayout(
        float width,
        float height,
        int columns,
        float cellWidth,
        float cellHeight,
        float gridHeight)
    {
        Width = width;
        Height = height;
        Columns = columns;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        GridHeight = gridHeight;
    }

    public float Width { get; }
    public float Height { get; }
    public int Columns { get; }
    public float CellWidth { get; }
    public float CellHeight { get; }
    public float GridHeight { get; }
}

internal static class SpiritArtifactTargetSelectorLayoutPolicy
{
    public const float OuterHorizontalMargin = 16f;
    public const float OuterVerticalMargin = 24f;
    public const float MinimumWidth = 480f;
    public const float MaximumWidth = 680f;
    public const float MinimumHeight = 400f;
    public const float MaximumHeight = 480f;
    public const float CellHeight = 104f;
    public const float ColumnThreshold = 620f;

    public static SpiritArtifactTargetSelectorLayout Calculate(float workspaceWidth, float workspaceHeight)
    {
        workspaceWidth = Math.Max(1f, workspaceWidth);
        workspaceHeight = Math.Max(1f, workspaceHeight);
        var width = Clamp(
            workspaceWidth - OuterHorizontalMargin * 2f,
            Math.Min(MinimumWidth, workspaceWidth),
            Math.Min(MaximumWidth, workspaceWidth));
        var height = Clamp(
            workspaceHeight - OuterVerticalMargin * 2f,
            Math.Min(MinimumHeight, workspaceHeight),
            Math.Min(MaximumHeight, workspaceHeight));
        var columns = width >= ColumnThreshold ? 4 : 3;
        var cellWidth = Math.Max(112f, (width - 36f - (columns - 1) * 8f) / columns);
        var gridHeight = Math.Max(220f, height - 108f);
        return new SpiritArtifactTargetSelectorLayout(
            width,
            height,
            columns,
            cellWidth,
            CellHeight,
            gridHeight);
    }

    private static float Clamp(float value, float minimum, float maximum)
        => Math.Max(minimum, Math.Min(maximum, value));
}
