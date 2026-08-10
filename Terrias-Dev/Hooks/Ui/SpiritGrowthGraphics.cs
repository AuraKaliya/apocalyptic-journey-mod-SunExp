using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritRadarGraphic : MaskableGraphic, IPointerMoveHandler, IPointerExitHandler
{
    private readonly List<SpiritRadarAxisSnapshot> axes = new();
    private Action<int?>? hoverChanged;
    private int hoveredAxis = -1;

    public SpiritRadarGraphic()
    {
        raycastTarget = true;
        useLegacyMeshGeneration = false;
    }

    public void Configure(IReadOnlyList<SpiritRadarAxisSnapshot> values, Action<int?> onHover)
    {
        axes.Clear();
        axes.AddRange((values ?? Array.Empty<SpiritRadarAxisSnapshot>()).Take(4));
        hoverChanged = onHover;
        SetVerticesDirty();
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (axes.Count == 0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out var local)) return;
        var direction = local - rectTransform.rect.center;
        if (direction.sqrMagnitude < 36f)
        {
            SetHovered(-1);
            return;
        }

        var best = 0;
        var bestDot = float.MinValue;
        direction.Normalize();
        for (var index = 0; index < axes.Count; index++)
        {
            var dot = Vector2.Dot(direction, Direction(index, axes.Count));
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = index;
        }
        SetHovered(best);
    }

    public void OnPointerExit(PointerEventData eventData) => SetHovered(-1);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (axes.Count < 3) return;
        var center = rectTransform.rect.center;
        var radius = Mathf.Max(10f, Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f - 12f);
        var grid = new Color(0.54f, 0.62f, 0.70f, 0.25f);
        for (var ring = 1; ring <= 4; ring++)
        {
            AddOutline(vh, Points(center, radius * ring / 4f, _ => 1f), 1f, grid);
        }
        for (var index = 0; index < axes.Count; index++)
        {
            AddLine(vh, center, center + Direction(index, axes.Count) * radius, 1f, grid);
        }

        var potential = Points(center, radius, index => axes[index].NormalizedPotential);
        var current = Points(center, radius, index => axes[index].NormalizedCurrent);
        AddFill(vh, potential, new Color(0.94f, 0.77f, 0.36f, 0.11f));
        AddOutline(vh, potential, 2f, new Color(0.94f, 0.77f, 0.36f, 0.92f));
        AddFill(vh, current, new Color(0.25f, 0.82f, 0.72f, 0.24f));
        AddOutline(vh, current, 2.4f, new Color(0.34f, 0.94f, 0.78f, 1f));
    }

    private Vector2[] Points(Vector2 center, float radius, Func<int, float> scale)
    {
        var result = new Vector2[axes.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = center + Direction(index, result.Length) * radius * Mathf.Clamp01(scale(index));
        }
        return result;
    }

    private void SetHovered(int value)
    {
        if (hoveredAxis == value) return;
        hoveredAxis = value;
        hoverChanged?.Invoke(value < 0 ? null : value);
    }

    private static Vector2 Direction(int index, int count)
    {
        var angle = Mathf.PI * 0.5f - index * Mathf.PI * 2f / count;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static void AddFill(VertexHelper vh, IReadOnlyList<Vector2> points, Color tint)
    {
        if (points.Count < 3) return;
        var start = vh.currentVertCount;
        foreach (var point in points) vh.AddVert(point, tint, Vector2.zero);
        for (var index = 1; index < points.Count - 1; index++) vh.AddTriangle(start, start + index, start + index + 1);
    }

    internal static void AddOutline(VertexHelper vh, IReadOnlyList<Vector2> points, float width, Color tint)
    {
        for (var index = 0; index < points.Count; index++)
        {
            AddLine(vh, points[index], points[(index + 1) % points.Count], width, tint);
        }
    }

    internal static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width, Color tint)
    {
        var direction = to - from;
        if (direction.sqrMagnitude < 0.001f) return;
        direction.Normalize();
        var normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
        var start = vh.currentVertCount;
        vh.AddVert(from - normal, tint, Vector2.zero);
        vh.AddVert(from + normal, tint, Vector2.zero);
        vh.AddVert(to + normal, tint, Vector2.zero);
        vh.AddVert(to - normal, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}

internal sealed class SpiritGrowthCurveGraphic : MaskableGraphic
{
    private IReadOnlyList<SpiritGrowthCurvePoint> current = Array.Empty<SpiritGrowthCurvePoint>();
    private IReadOnlyList<SpiritGrowthCurvePoint> standard = Array.Empty<SpiritGrowthCurvePoint>();
    private IReadOnlyList<SpiritGrowthCurvePoint> theoretical = Array.Empty<SpiritGrowthCurvePoint>();
    private string axisKey = "total";
    private int currentLevel = 1;

    public SpiritGrowthCurveGraphic()
    {
        raycastTarget = false;
        useLegacyMeshGeneration = false;
    }

    public void Configure(
        IReadOnlyList<SpiritGrowthCurvePoint> currentCurve,
        IReadOnlyList<SpiritGrowthCurvePoint> standardCurve,
        IReadOnlyList<SpiritGrowthCurvePoint> theoreticalCurve,
        string selectedAxis,
        int level)
    {
        current = currentCurve ?? Array.Empty<SpiritGrowthCurvePoint>();
        standard = standardCurve ?? Array.Empty<SpiritGrowthCurvePoint>();
        theoretical = theoreticalCurve ?? Array.Empty<SpiritGrowthCurvePoint>();
        axisKey = string.IsNullOrWhiteSpace(selectedAxis) ? "total" : selectedAxis;
        currentLevel = Math.Max(1, level);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (current.Count < 2) return;
        var rect = rectTransform.rect;
        var left = rect.xMin + 14f;
        var right = rect.xMax - 8f;
        var bottom = rect.yMin + 12f;
        var top = rect.yMax - 8f;
        var grid = new Color(0.54f, 0.62f, 0.70f, 0.22f);
        for (var index = 0; index <= 4; index++)
        {
            var y = Mathf.Lerp(bottom, top, index / 4f);
            SpiritRadarGraphic.AddLine(vh, new Vector2(left, y), new Vector2(right, y), 1f, grid);
        }
        for (var index = 0; index <= 5; index++)
        {
            var x = Mathf.Lerp(left, right, index / 5f);
            SpiritRadarGraphic.AddLine(vh, new Vector2(x, bottom), new Vector2(x, top), 1f, grid);
        }

        var maximum = Math.Max(1, current.Concat(standard).Concat(theoretical)
            .Max(point => SpiritGrowthQueryService.Value(point.Origins, axisKey)));
        DrawCurve(vh, theoretical, left, right, bottom, top, maximum, new Color(0.58f, 0.62f, 0.70f, 0.72f), 1.2f);
        DrawCurve(vh, standard, left, right, bottom, top, maximum, new Color(0.94f, 0.77f, 0.36f, 0.9f), 1.6f);
        DrawCurve(vh, current, left, right, bottom, top, maximum, new Color(0.34f, 0.94f, 0.78f, 1f), 2.3f);

        var marker = current.OrderBy(point => Math.Abs(point.Level - currentLevel)).First();
        var markerPosition = Position(marker, current, left, right, bottom, top, maximum);
        AddDiamond(vh, markerPosition, 4.5f, new Color(0.98f, 0.98f, 1f, 1f));
    }

    private void DrawCurve(VertexHelper vh, IReadOnlyList<SpiritGrowthCurvePoint> curve, float left, float right, float bottom, float top, int maximum, Color tint, float width)
    {
        for (var index = 1; index < curve.Count; index++)
        {
            SpiritRadarGraphic.AddLine(vh,
                Position(curve[index - 1], curve, left, right, bottom, top, maximum),
                Position(curve[index], curve, left, right, bottom, top, maximum),
                width,
                tint);
        }
    }

    private Vector2 Position(SpiritGrowthCurvePoint point, IReadOnlyList<SpiritGrowthCurvePoint> curve, float left, float right, float bottom, float top, int maximum)
    {
        var minLevel = curve[0].Level;
        var maxLevel = curve[curve.Count - 1].Level;
        var x = Mathf.InverseLerp(minLevel, maxLevel, point.Level);
        var y = Mathf.Clamp01(SpiritGrowthQueryService.Value(point.Origins, axisKey) / (float)maximum);
        return new Vector2(Mathf.Lerp(left, right, x), Mathf.Lerp(bottom, top, y));
    }

    private static void AddDiamond(VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        var start = vh.currentVertCount;
        vh.AddVert(center + Vector2.up * radius, tint, Vector2.zero);
        vh.AddVert(center + Vector2.right * radius, tint, Vector2.zero);
        vh.AddVert(center + Vector2.down * radius, tint, Vector2.zero);
        vh.AddVert(center + Vector2.left * radius, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
