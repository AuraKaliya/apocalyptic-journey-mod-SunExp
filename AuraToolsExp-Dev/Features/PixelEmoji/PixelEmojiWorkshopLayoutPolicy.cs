namespace AuraToolsExp.Dll.Features.PixelEmoji;

internal enum PixelEmojiWorkshopLayoutTier
{
    Stacked,
    Compact,
    Wide
}

internal readonly struct PixelEmojiWorkshopLayoutMetrics
{
    internal PixelEmojiWorkshopLayoutMetrics(
        PixelEmojiWorkshopLayoutTier tier,
        bool stackVertically,
        float workspaceHeight,
        float contentHeight,
        float canvasColumnWidth,
        float canvasSize,
        float toolsMinimumWidth,
        float toolsPreferredWidth,
        float paletteCellWidth,
        float paletteCellHeight,
        float paletteSpacing,
        float paletteHeight,
        float frameSlotWidth,
        float frameArtSize,
        float animationPanelHeight)
    {
        Tier = tier;
        StackVertically = stackVertically;
        WorkspaceHeight = workspaceHeight;
        ContentHeight = contentHeight;
        CanvasColumnWidth = canvasColumnWidth;
        CanvasSize = canvasSize;
        ToolsMinimumWidth = toolsMinimumWidth;
        ToolsPreferredWidth = toolsPreferredWidth;
        PaletteCellWidth = paletteCellWidth;
        PaletteCellHeight = paletteCellHeight;
        PaletteSpacing = paletteSpacing;
        PaletteHeight = paletteHeight;
        FrameSlotWidth = frameSlotWidth;
        FrameArtSize = frameArtSize;
        AnimationPanelHeight = animationPanelHeight;
    }

    internal PixelEmojiWorkshopLayoutTier Tier { get; }
    internal bool StackVertically { get; }
    internal float WorkspaceHeight { get; }
    internal float ContentHeight { get; }
    internal float CanvasColumnWidth { get; }
    internal float CanvasSize { get; }
    internal float ToolsMinimumWidth { get; }
    internal float ToolsPreferredWidth { get; }
    internal float PaletteCellWidth { get; }
    internal float PaletteCellHeight { get; }
    internal float PaletteSpacing { get; }
    internal float PaletteHeight { get; }
    internal float FrameSlotWidth { get; }
    internal float FrameArtSize { get; }
    internal float AnimationPanelHeight { get; }
}

internal static class PixelEmojiWorkshopLayoutPolicy
{
    internal const float WideMinimumWidth = 820f;
    internal const float CompactMinimumWidth = 740f;
    internal const float ColumnGap = 12f;
    internal const float ContentHeight = 620f;

    internal static PixelEmojiWorkshopLayoutMetrics Resolve(float availableWidth)
    {
        if (availableWidth >= WideMinimumWidth)
        {
            return Wide(false);
        }

        if (availableWidth >= CompactMinimumWidth)
        {
            return new PixelEmojiWorkshopLayoutMetrics(
                PixelEmojiWorkshopLayoutTier.Compact,
                false,
                ContentHeight,
                ContentHeight,
                376f,
                360f,
                352f,
                360f,
                34f,
                34f,
                8f,
                160f,
                42f,
                34f,
                188f);
        }

        return Wide(true);
    }

    private static PixelEmojiWorkshopLayoutMetrics Wide(bool stacked)
    {
        return new PixelEmojiWorkshopLayoutMetrics(
            stacked ? PixelEmojiWorkshopLayoutTier.Stacked : PixelEmojiWorkshopLayoutTier.Wide,
            stacked,
            stacked ? ContentHeight * 2f + ColumnGap : ContentHeight,
            ContentHeight,
            424f,
            408f,
            384f,
            416f,
            38f,
            36f,
            8f,
            168f,
            48f,
            38f,
            188f);
    }
}
