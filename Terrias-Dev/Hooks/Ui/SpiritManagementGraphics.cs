using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritActiveStampGraphic : MaskableGraphic
{
    private Color fillTint = new(0.33f, 0.22f, 0.08f, 0.34f);
    private Color outerTint = new(0.94f, 0.72f, 0.30f, 0.72f);
    private Color innerTint = new(0.94f, 0.72f, 0.30f, 0.40f);
    private float outerWidth = 1.5f;
    private float innerWidth = 1f;
    private float innerInset = 3.5f;

    public SpiritActiveStampGraphic()
    {
        raycastTarget = false;
        useLegacyMeshGeneration = false;
    }

    public void Configure(Color fill, Color outer, Color inner)
    {
        fillTint = fill;
        outerTint = outer;
        innerTint = inner;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;

        AddQuad(vh, rect, fillTint);
        AddOutline(vh, rect, outerWidth, outerTint);
        var inner = Rect.MinMaxRect(
            rect.xMin + innerInset,
            rect.yMin + innerInset,
            rect.xMax - innerInset,
            rect.yMax - innerInset);
        if (inner.width > innerWidth * 2f && inner.height > innerWidth * 2f)
        {
            AddOutline(vh, inner, innerWidth, innerTint);
        }
    }

    private static void AddOutline(VertexHelper vh, Rect rect, float width, Color tint)
    {
        var thickness = Mathf.Clamp(width, 0.5f, Mathf.Min(rect.width, rect.height) * 0.5f);
        AddQuad(vh, Rect.MinMaxRect(rect.xMin, rect.yMax - thickness, rect.xMax, rect.yMax), tint);
        AddQuad(vh, Rect.MinMaxRect(rect.xMin, rect.yMin, rect.xMax, rect.yMin + thickness), tint);
        AddQuad(vh, Rect.MinMaxRect(rect.xMin, rect.yMin + thickness, rect.xMin + thickness, rect.yMax - thickness), tint);
        AddQuad(vh, Rect.MinMaxRect(rect.xMax - thickness, rect.yMin + thickness, rect.xMax, rect.yMax - thickness), tint);
    }

    private static void AddQuad(VertexHelper vh, Rect rect, Color tint)
    {
        var start = vh.currentVertCount;
        vh.AddVert(new Vector2(rect.xMin, rect.yMin), tint, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMin, rect.yMax), tint, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMax), tint, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMin), tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
