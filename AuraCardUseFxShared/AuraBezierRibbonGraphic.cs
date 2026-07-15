using System;
using UnityEngine;
using UnityEngine.UI;

namespace AuraCardUseFx.Shared;

/// <summary>A raycast-free, allocation-stable cubic Bezier ribbon for transient UI presentation.</summary>
public sealed class AuraBezierRibbonGraphic : MaskableGraphic
{
    private const int MinimumSamples = 8;
    private const int MaximumSamples = 64;

    private Vector2 start;
    private Vector2 control1;
    private Vector2 control2;
    private Vector2 end;
    private float progress;
    private float tailFraction = 0.3f;
    private float outerWidth = 16f;
    private float coreWidth = 3f;
    private int samples = 32;
    private Color outerColor = new(0.27f, 0.34f, 0.78f, 0.68f);
    private Color coreColor = new(0.95f, 0.98f, 1f, 0.95f);

    public AuraBezierRibbonGraphic()
    {
        raycastTarget = false;
        useLegacyMeshGeneration = false;
    }

    public void Configure(
        Vector2 from,
        Vector2 firstControl,
        Vector2 secondControl,
        Vector2 to,
        float width,
        float innerWidth,
        int sampleCount,
        float visibleTailFraction,
        Color bandColor,
        Color lightColor)
    {
        start = from;
        control1 = firstControl;
        control2 = secondControl;
        end = to;
        outerWidth = Mathf.Max(1f, width);
        coreWidth = Mathf.Clamp(innerWidth, 0.5f, outerWidth);
        samples = Mathf.Clamp(sampleCount, MinimumSamples, MaximumSamples);
        tailFraction = Mathf.Clamp(visibleTailFraction, 0.05f, 1f);
        outerColor = bandColor;
        coreColor = lightColor;
        SetVerticesDirty();
    }

    public void SetProgress(float value)
    {
        var next = Mathf.Clamp01(value);
        if (Mathf.Abs(progress - next) <= 0.0001f)
        {
            return;
        }

        progress = next;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (progress <= 0.0001f)
        {
            return;
        }

        var tailStart = Mathf.Max(0f, progress - tailFraction);
        PopulateBand(vh, outerWidth, outerColor, tailStart, progress, 0);
        PopulateBand(vh, coreWidth, coreColor, tailStart, progress, 1);
    }

    private void PopulateBand(VertexHelper vh, float width, Color tint, float from, float to, int layer)
    {
        var segmentCount = Math.Max(2, Mathf.CeilToInt(samples * Mathf.Max(0.05f, to - from)));
        var baseVertex = vh.currentVertCount;
        for (var i = 0; i <= segmentCount; i++)
        {
            var ratio = i / (float)segmentCount;
            var t = Mathf.Lerp(from, to, ratio);
            var position = Bezier(t);
            var tangent = BezierTangent(t);
            var normal = tangent.sqrMagnitude <= 0.0001f
                ? Vector2.up
                : new Vector2(-tangent.y, tangent.x).normalized;
            var envelope = Mathf.Sin(Mathf.Clamp01(ratio) * Mathf.PI * 0.5f);
            var headFade = 1f - Mathf.Pow(Mathf.Clamp01((ratio - 0.9f) / 0.1f), 2f) * 0.15f;
            var halfWidth = width * 0.5f * Mathf.Lerp(0.18f, 1f, envelope) * headFade;
            var vertexColor = tint;
            vertexColor.a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ratio * 4f));
            vh.AddVert(position - normal * halfWidth, vertexColor, new Vector2(t, layer));
            vh.AddVert(position + normal * halfWidth, vertexColor, new Vector2(t, layer + 1));
        }

        for (var i = 0; i < segmentCount; i++)
        {
            var index = baseVertex + i * 2;
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index + 1, index + 3, index + 2);
        }
    }

    private Vector2 Bezier(float t)
    {
        var oneMinus = 1f - t;
        return oneMinus * oneMinus * oneMinus * start
               + 3f * oneMinus * oneMinus * t * control1
               + 3f * oneMinus * t * t * control2
               + t * t * t * end;
    }

    private Vector2 BezierTangent(float t)
    {
        var oneMinus = 1f - t;
        return 3f * oneMinus * oneMinus * (control1 - start)
               + 6f * oneMinus * t * (control2 - control1)
               + 3f * t * t * (end - control2);
    }
}
