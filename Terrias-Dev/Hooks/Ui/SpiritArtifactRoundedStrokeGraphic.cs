using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritArtifactRoundedStrokeGraphic : MaskableGraphic
{
    private const int CornerSegments = 5;
    private float thickness = 2f;
    private float radius = 6f;

    public float Thickness
    {
        get => thickness;
        set
        {
            thickness = Math.Max(0.5f, value);
            SetVerticesDirty();
        }
    }

    public float Radius
    {
        get => radius;
        set
        {
            radius = Math.Max(0f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        var outer = GetPixelAdjustedRect();
        var width = Math.Max(0f, outer.width);
        var height = Math.Max(0f, outer.height);
        if (width <= 0f || height <= 0f) return;

        var stroke = Mathf.Clamp(thickness, 0.5f, Math.Min(width, height) * 0.5f);
        var outerRadius = Mathf.Clamp(radius, stroke, Math.Min(width, height) * 0.5f);
        var inner = new Rect(
            outer.xMin + stroke,
            outer.yMin + stroke,
            Math.Max(0f, width - stroke * 2f),
            Math.Max(0f, height - stroke * 2f));
        var innerRadius = Math.Max(0f, outerRadius - stroke);
        var outerPoints = BuildPerimeter(outer, outerRadius);
        var innerPoints = BuildPerimeter(inner, innerRadius);
        var vertexColor = (Color32)color;

        for (var index = 0; index < outerPoints.Count; index++)
        {
            helper.AddVert(outerPoints[index], vertexColor, Vector2.zero);
            helper.AddVert(innerPoints[index], vertexColor, Vector2.zero);
        }

        for (var index = 0; index < outerPoints.Count; index++)
        {
            var next = (index + 1) % outerPoints.Count;
            var outerIndex = index * 2;
            var innerIndex = outerIndex + 1;
            var nextOuterIndex = next * 2;
            var nextInnerIndex = nextOuterIndex + 1;
            helper.AddTriangle(outerIndex, nextOuterIndex, nextInnerIndex);
            helper.AddTriangle(outerIndex, nextInnerIndex, innerIndex);
        }
    }

    private static List<Vector2> BuildPerimeter(Rect rect, float cornerRadius)
    {
        var result = new List<Vector2>((CornerSegments + 1) * 4);
        AddCorner(result, rect.xMax - cornerRadius, rect.yMax - cornerRadius, cornerRadius, 90f, 0f);
        AddCorner(result, rect.xMax - cornerRadius, rect.yMin + cornerRadius, cornerRadius, 0f, -90f);
        AddCorner(result, rect.xMin + cornerRadius, rect.yMin + cornerRadius, cornerRadius, -90f, -180f);
        AddCorner(result, rect.xMin + cornerRadius, rect.yMax - cornerRadius, cornerRadius, 180f, 90f);
        return result;
    }

    private static void AddCorner(
        ICollection<Vector2> points,
        float centerX,
        float centerY,
        float cornerRadius,
        float startDegrees,
        float endDegrees)
    {
        for (var segment = 0; segment <= CornerSegments; segment++)
        {
            var progress = segment / (float)CornerSegments;
            var angle = Mathf.Lerp(startDegrees, endDegrees, progress) * Mathf.Deg2Rad;
            points.Add(new Vector2(
                centerX + Mathf.Cos(angle) * cornerRadius,
                centerY + Mathf.Sin(angle) * cornerRadius));
        }
    }
}
