using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCg.Shared;

/// <summary>Local artwork framing. Coordinates use the source image's top-left origin.</summary>
public sealed class AuraCgPortraitFraming
{
    public bool Enabled { get; set; }
    public float FaceX { get; set; } = 0.5f;
    public float FaceY { get; set; } = 0.28f;
    public float FaceWidth { get; set; } = 0.20f;
    public float FaceHeight { get; set; } = 0.18f;
    public bool CanMirror { get; set; }

    public void Normalize()
    {
        FaceX = AuraCgArtValues.Clamp(FaceX, 0.02f, 0.98f, 0.5f);
        FaceY = AuraCgArtValues.Clamp(FaceY, 0.02f, 0.98f, 0.28f);
        FaceWidth = AuraCgArtValues.Clamp(FaceWidth, 0.02f, 0.80f, 0.20f);
        FaceHeight = AuraCgArtValues.Clamp(FaceHeight, 0.02f, 0.80f, 0.18f);
    }
}

/// <summary>Locally resolved companion artwork; no file paths or metadata enter the scene RPC.</summary>
public sealed class AuraCgSceneArtLayerSpec
{
    public AuraCgSceneAssetReference Asset { get; set; } = new();
    public bool Foreground { get; set; } = true;
    public bool Required { get; set; }
    public float Opacity { get; set; } = 1f;
    public float MotionX { get; set; }
    public float MotionY { get; set; }
    public float Pulse { get; set; }

    public void Normalize()
    {
        Asset ??= new AuraCgSceneAssetReference();
        Asset.Normalize();
        Opacity = AuraCgArtValues.Clamp(Opacity, 0f, 1f, 1f);
        MotionX = AuraCgArtValues.Clamp(MotionX, -0.03f, 0.03f, 0f);
        MotionY = AuraCgArtValues.Clamp(MotionY, -0.03f, 0.03f, 0f);
        Pulse = AuraCgArtValues.Clamp(Pulse, 0f, 0.20f, 0f);
    }
}

public sealed class AuraCgSceneArtwork
{
    public AuraCgPortraitFraming Portrait { get; set; } = new();
    public bool DarkTitle { get; set; }
    public float CameraPush { get; set; } = 0.02f;
    public List<AuraCgSceneArtLayerSpec> Layers { get; set; } = new();

    public void Normalize()
    {
        Portrait ??= new AuraCgPortraitFraming();
        Portrait.Normalize();
        CameraPush = AuraCgArtValues.Clamp(CameraPush, 0f, 0.03f, 0.02f);
        Layers = (Layers ?? new List<AuraCgSceneArtLayerSpec>()).Where(layer => layer != null).Take(4).ToList();
        foreach (var layer in Layers) layer.Normalize();
    }
}

internal static class AuraCgArtValues
{
    internal static float Clamp(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
}

internal static class AuraCgPortraitFramingMath
{
    internal static AuraCgSceneFramingResult Fit(
        AuraCgPortraitFraming framing, AuraCgNormalizedBounds bounds,
        float canvasWidth, float canvasHeight, float faceWidth, float faceHeight,
        float topSpace)
    {
        framing.Normalize();
        var width = Math.Max(1f, canvasWidth);
        var height = Math.Max(1f, canvasHeight);
        var scale = Math.Min(Math.Max(1f, faceWidth) / (width * framing.FaceWidth),
            Math.Max(1f, faceHeight) / (height * framing.FaceHeight));
        var visibleTop = 1f - bounds.Y - bounds.Height;
        var headAboveFace = Math.Max(0.01f, framing.FaceY - visibleTop) * height;
        scale = Math.Min(scale, Math.Max(1f, topSpace) / headAboveFace);
        return new AuraCgSceneFramingResult(width * scale, height * scale,
            (0.5f - framing.FaceX) * width * scale,
            (framing.FaceY - 0.5f) * height * scale);
    }
}
