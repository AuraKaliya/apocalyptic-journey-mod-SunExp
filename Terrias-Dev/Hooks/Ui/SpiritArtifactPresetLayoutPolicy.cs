using System;

namespace Terrias.Dll.Hooks.Ui;

internal readonly struct SpiritArtifactPresetLayout
{
    public SpiritArtifactPresetLayout(
        float width,
        float height,
        float listWidth,
        float miniCardWidth,
        float miniCardSpacing)
    {
        Width = width;
        Height = height;
        ListWidth = listWidth;
        MiniCardWidth = miniCardWidth;
        MiniCardSpacing = miniCardSpacing;
    }

    public float Width { get; }

    public float Height { get; }

    public float ListWidth { get; }

    public float MiniCardWidth { get; }

    public float MiniCardSpacing { get; }
}

internal static class SpiritArtifactPresetLayoutPolicy
{
    public const float RowHeight = 60f;
    public const float RowSpacing = 8f;

    public static SpiritArtifactPresetLayout Calculate(float workspaceWidth, float workspaceHeight)
    {
        var width = Clamp(workspaceWidth - 40f, 520f, 680f);
        var height = Clamp(workspaceHeight - 24f, 430f, 500f);
        var expanded = width >= 620f;
        return new SpiritArtifactPresetLayout(
            width,
            height,
            expanded ? 210f : 180f,
            expanded ? 60f : 50f,
            expanded ? 8f : 4f);
    }

    public static int VisibleRows(float viewportHeight)
        => Math.Max(1, (int)Math.Floor((Math.Max(0f, viewportHeight) + RowSpacing) / (RowHeight + RowSpacing)));

    private static float Clamp(float value, float minimum, float maximum)
        => Math.Max(minimum, Math.Min(maximum, value));
}
