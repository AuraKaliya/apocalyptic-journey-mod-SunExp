namespace Terrias.Dll.Mechanics;

public readonly struct MapNodeTextureBounds
{
    public MapNodeTextureBounds(
        int width,
        int height,
        int leftTransparentWidth,
        int rightTransparentWidth,
        int topTransparentHeight,
        int bottomTransparentHeight)
    {
        Width = width;
        Height = height;
        LeftTransparentWidth = leftTransparentWidth;
        RightTransparentWidth = rightTransparentWidth;
        TopTransparentHeight = topTransparentHeight;
        BottomTransparentHeight = bottomTransparentHeight;
    }

    public int Width { get; }

    public int Height { get; }

    public int LeftTransparentWidth { get; }

    public int RightTransparentWidth { get; }

    public int TopTransparentHeight { get; }

    public int BottomTransparentHeight { get; }
}
