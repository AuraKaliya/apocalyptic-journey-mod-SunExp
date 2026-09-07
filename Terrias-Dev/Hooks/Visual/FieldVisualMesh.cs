using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

/// <summary>Bounded, reused geometry owned solely by a field presentation instance.</summary>
public sealed class FieldVisualMesh : IDisposable
{
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly bool worldCoordinates;
    private readonly List<Vector3> vertices = new(2048);
    private readonly List<Color32> colors = new(2048);
    private readonly List<Vector2> uvs = new(2048);
    private readonly List<int> triangles = new(4096);
    private bool disposed;

    public FieldVisualMesh(string name, Transform parent, Material material, string layer, int order, bool worldCoordinates = false)
    {
        this.worldCoordinates = worldCoordinates;
        root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        root.transform.SetParent(parent, false);
        mesh = new Mesh { name = name + ".Mesh" };
        mesh.MarkDynamic();
        root.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = root.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerName = layer;
        renderer.sortingOrder = order;
        renderer.allowOcclusionWhenDynamic = false;
    }

    public void Clear()
    {
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
    }

    public void Quad(Rect rect, Color bottom, Color top, Rect? uv = null)
    {
        var texture = uv ?? new Rect(0f, 0f, 1f, 1f);
        var index = vertices.Count;
        Vertex(new Vector2(rect.xMin, rect.yMin), bottom, new Vector2(texture.xMin, texture.yMin));
        Vertex(new Vector2(rect.xMin, rect.yMax), top, new Vector2(texture.xMin, texture.yMax));
        Vertex(new Vector2(rect.xMax, rect.yMax), top, new Vector2(texture.xMax, texture.yMax));
        Vertex(new Vector2(rect.xMax, rect.yMin), bottom, new Vector2(texture.xMax, texture.yMin));
        Triangle(index, index + 1, index + 2);
        Triangle(index, index + 2, index + 3);
    }

    public void Glow(Vector2 center, float radiusX, float radiusY, Color color, int segments = 24)
    {
        if (color.a <= 0.001f || radiusX <= 0f || radiusY <= 0f) return;
        var index = vertices.Count;
        Vertex(center, color, Vector2.one * 0.5f);
        var edge = new Color(color.r, color.g, color.b, 0f);
        for (var i = 0; i <= segments; i++)
        {
            var angle = i * Mathf.PI * 2f / segments;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vertex(center + new Vector2(direction.x * radiusX, direction.y * radiusY), edge,
                direction * 0.5f + Vector2.one * 0.5f);
            if (i > 0) Triangle(index, index + i + 1, index + i);
        }
    }

    public void Ring(Vector2 center, float radiusX, float radiusY, float thickness, Color color, int segments = 48)
    {
        if (color.a <= 0.001f) return;
        for (var i = 0; i < segments; i++)
        {
            var start = i * Mathf.PI * 2f / segments;
            var end = (i + 1) * Mathf.PI * 2f / segments;
            var a = new Vector2(Mathf.Cos(start), Mathf.Sin(start));
            var b = new Vector2(Mathf.Cos(end), Mathf.Sin(end));
            var index = vertices.Count;
            Vertex(center + new Vector2(a.x * radiusX, a.y * radiusY), color, Vector2.zero);
            Vertex(center + new Vector2(b.x * radiusX, b.y * radiusY), color, Vector2.zero);
            Vertex(center + new Vector2(b.x * (radiusX - thickness), b.y * Math.Max(0f, radiusY - thickness)), color, Vector2.zero);
            Vertex(center + new Vector2(a.x * (radiusX - thickness), a.y * Math.Max(0f, radiusY - thickness)), color, Vector2.zero);
            Triangle(index, index + 1, index + 2);
            Triangle(index, index + 2, index + 3);
        }
    }

    public void Petal(Vector2 center, float size, float angle, Color color)
    {
        var index = vertices.Count;
        var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size;
        var across = new Vector2(-along.y, along.x) * 0.45f;
        Vertex(center - along, color, Vector2.zero);
        Vertex(center + across, color, Vector2.zero);
        Vertex(center + along, color, Vector2.zero);
        Vertex(center - across, color, Vector2.zero);
        Triangle(index, index + 1, index + 2);
        Triangle(index, index + 2, index + 3);
    }

    public void Commit()
    {
        if (disposed) return;
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, true);
    }

    private void Vertex(Vector2 position, Color color, Vector2 uv)
    {
        var point = new Vector3(position.x, position.y, 0f);
        vertices.Add(worldCoordinates ? root.transform.InverseTransformPoint(point) : point);
        colors.Add(color);
        uvs.Add(uv);
    }

    private void Triangle(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (root != null)
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
        UnityEngine.Object.Destroy(mesh);
    }
}
