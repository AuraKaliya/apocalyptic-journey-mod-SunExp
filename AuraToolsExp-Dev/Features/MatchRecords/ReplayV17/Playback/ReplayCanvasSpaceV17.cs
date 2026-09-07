using System;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal static class ReplayCanvasSpaceV17
{
    // Owner-attached presentation protocol v1 uses the native 1080p UI reference.
    internal const float ReferencePixelHeight = 1080f;

    internal static void Apply(
        RectTransform target, RectTransform canvas, Vector2 recordedResolution,
        Vector2 position, Vector2 size, Vector3 scale, float rotation)
    {
        if (target == null || canvas == null || target.parent == null)
            throw new ArgumentException("A recorded UI pose needs a target, parent and canvas.");
        if (recordedResolution.x <= 0f || recordedResolution.y <= 0f)
            throw new ArgumentOutOfRangeException(nameof(recordedResolution));
        // Recorded X is centred, while Y is measured from the bottom of the screen.
        // Both describe the canvas, independently of the native container's offset.
        var bounds = canvas.rect;
        if (bounds.width <= 0f || bounds.height <= 0f)
            throw new InvalidOperationException("The replay canvas layout is not ready.");
        target.position = canvas.TransformPoint(new Vector3(
            bounds.xMin + (position.x / recordedResolution.x + 0.5f) * bounds.width,
            bounds.yMin + position.y / recordedResolution.y * bounds.height, 0f));
        target.sizeDelta = size;
        target.rotation = canvas.rotation * Quaternion.Euler(0f, 0f, rotation);
        var parentScale = target.parent.lossyScale;
        var canvasScale = canvas.lossyScale;
        target.localScale = new Vector3(
            Relative(scale.x * canvasScale.x, parentScale.x),
            Relative(scale.y * canvasScale.y, parentScale.y),
            Relative(scale.z * canvasScale.z, parentScale.z));
    }

    internal static float WorldHeight(Camera camera, Vector3 at, float referencePixels)
    {
        var viewport = camera.WorldToViewportPoint(at);
        if (viewport.z <= 0f) throw new InvalidOperationException("Presentation anchor is behind its camera.");
        var top = viewport + Vector3.up * (referencePixels / ReferencePixelHeight);
        return Vector3.Distance(camera.ViewportToWorldPoint(viewport), camera.ViewportToWorldPoint(top));
    }

    private static float Relative(float value, float parent)
    {
        if (Mathf.Abs(parent) < 0.000001f)
            throw new InvalidOperationException("Replay UI parent has a zero scale.");
        return value / parent;
    }
}
