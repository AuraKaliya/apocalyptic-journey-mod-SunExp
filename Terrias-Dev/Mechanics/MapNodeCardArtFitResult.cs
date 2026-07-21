namespace SunExp.Dll.Mechanics;

public readonly struct MapNodeCardArtFitResult
{
    public MapNodeCardArtFitResult(
        bool shouldApplyTransform,
        float scaleX,
        float scaleY,
        float offsetX,
        float offsetY)
    {
        ShouldApplyTransform = shouldApplyTransform;
        ScaleX = scaleX;
        ScaleY = scaleY;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public bool ShouldApplyTransform { get; }

    public float ScaleX { get; }

    public float ScaleY { get; }

    public float OffsetX { get; }

    public float OffsetY { get; }
}
