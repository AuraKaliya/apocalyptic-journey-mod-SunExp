using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Visual;

internal sealed class StarScoreArrivalFlashGraphic : MaskableGraphic
{
    private const int RingSegments = 28;
    private Vector2 center;
    private float progress;
    private float strength = 1f;

    public StarScoreArrivalFlashGraphic()
    {
        raycastTarget = false;
        useLegacyMeshGeneration = false;
    }

    public void Configure(Vector2 localCenter, float visualStrength)
    {
        center = localCenter;
        strength = Mathf.Clamp(visualStrength, 1f, 2.5f);
        SetVerticesDirty();
    }

    public void SetProgress(float value)
    {
        progress = Mathf.Clamp01(value);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (progress >= 1f)
        {
            return;
        }

        var fade = 1f - progress;
        var eased = 1f - Mathf.Pow(1f - progress, 3f);
        var radius = 34f * Mathf.Lerp(0.35f, 1.25f + (strength - 1f) * 0.22f, eased);
        var ringWidth = Mathf.Lerp(4.5f * strength, 1.2f, progress);
        AddRing(vh, radius, ringWidth, new Color(1f, 0.88f, 0.54f, fade * 0.88f));
        AddStar(vh, radius * 0.72f, new Color(0.62f, 0.86f, 1f, fade));
    }

    private void AddRing(VertexHelper vh, float radius, float width, Color tint)
    {
        var baseVertex = vh.currentVertCount;
        var inner = Mathf.Max(0f, radius - width);
        for (var i = 0; i <= RingSegments; i++)
        {
            var angle = i / (float)RingSegments * Mathf.PI * 2f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(center + direction * inner, tint, Vector2.zero);
            vh.AddVert(center + direction * radius, tint, Vector2.one);
        }

        for (var i = 0; i < RingSegments; i++)
        {
            var index = baseVertex + i * 2;
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index + 1, index + 3, index + 2);
        }
    }

    private void AddStar(VertexHelper vh, float length, Color tint)
    {
        var centerIndex = vh.currentVertCount;
        vh.AddVert(center, tint, new Vector2(0.5f, 0.5f));
        var points = new[]
        {
            new Vector2(0f, length), new Vector2(length * 0.12f, length * 0.12f),
            new Vector2(length * 0.46f, 0f), new Vector2(length * 0.12f, -length * 0.12f),
            new Vector2(0f, -length), new Vector2(-length * 0.12f, -length * 0.12f),
            new Vector2(-length * 0.46f, 0f), new Vector2(-length * 0.12f, length * 0.12f)
        };
        foreach (var point in points)
        {
            vh.AddVert(center + point, tint, Vector2.zero);
        }

        for (var i = 0; i < points.Length; i++)
        {
            vh.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + ((i + 1) % points.Length));
        }
    }
}
