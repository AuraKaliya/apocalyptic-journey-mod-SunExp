using System;

namespace Terrias.Dll.Hooks.Ui;

internal static class SpiritArtifactCardStylePolicy
{
    public const float CellWidth = 76f;
    public const float CellHeight = 88f;
    public const float CardWidth = 72f;
    public const float CardHeight = 84f;
    public const float ArtHeight = 68f;
    public const float CardRadius = 6f;
    public const float ArtBottomRightRadius = 14f;
    public const float HoverStrokeWidth = 2f;
    public const float SelectionHaloWidth = 76f;
    public const float SelectionHaloHeight = 88f;
    public const float SelectionHaloRadius = 8f;
    public const float EquipmentSlotSize = 56f;
    public const float EquippedIconSize = 42f;
    public const float EmptyIconSize = 36f;
    public const float Spacing = 6f;
    public const int Padding = 8;
    public const float InventoryHeight = 198f;

    public static int ColumnsForWidth(float width)
        => ColumnsForWidth(width, 5, 10);

    public static int ColumnsForWidth(float width, int minimumColumns, int maximumColumns)
    {
        minimumColumns = Math.Max(1, minimumColumns);
        maximumColumns = Math.Max(minimumColumns, maximumColumns);
        var available = Math.Max(CellWidth, width - Padding * 2f);
        var columns = (int)Math.Floor((available + Spacing) / (CellWidth + Spacing));
        return Math.Max(minimumColumns, Math.Min(maximumColumns, columns));
    }

    public static int HorizontalPaddingForWidth(float width, int columns)
    {
        columns = Math.Max(1, columns);
        var used = columns * CellWidth + Math.Max(0, columns - 1) * Spacing;
        return Math.Max(Padding, (int)Math.Floor(Math.Max(0f, width - used) * 0.5f));
    }
}

internal readonly struct SpiritArtifactSelectionPulse
{
    public SpiritArtifactSelectionPulse(float alpha, float scale)
    {
        Alpha = alpha;
        Scale = scale;
    }

    public float Alpha { get; }

    public float Scale { get; }
}

internal static class SpiritArtifactCardMotionPolicy
{
    public const float SelectionPeriodSeconds = 2f;
    public const float HoverEnterSeconds = 0.12f;
    public const float HoverExitSeconds = 0.09f;

    public static SpiritArtifactSelectionPulse SelectionPulse(float elapsedSeconds)
    {
        var normalized = Math.Max(0f, elapsedSeconds) / SelectionPeriodSeconds;
        var eased = 0.5f - 0.5f * (float)Math.Cos(Math.PI * 2d * normalized);
        return new SpiritArtifactSelectionPulse(
            0.58f + 0.38f * eased,
            1f + 0.018f * eased);
    }

    public static bool ShouldRestartSelection(bool wasSelected, bool nextSelected)
        => nextSelected && !wasSelected;
}
