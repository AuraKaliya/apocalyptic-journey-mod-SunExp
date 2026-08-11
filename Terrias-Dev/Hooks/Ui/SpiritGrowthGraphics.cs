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
