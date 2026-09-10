using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

namespace Terrias.Dll.Hooks.Ui;

internal readonly struct UiSilhouetteSource
{
    public UiSilhouetteSource(Texture texture, Rect bounds, Vector2 origin, Vector2 axisX, Vector2 axisY)
    {
        Texture = texture;
        Bounds = bounds;
        UvOrigin = origin;
        UvAxisX = axisX;
        UvAxisY = axisY;
    }
    public Texture Texture { get; }
    public Rect Bounds { get; }
    public Vector2 UvOrigin { get; }
    public Vector2 UvAxisX { get; }
    public Vector2 UvAxisY { get; }

    public static UiSilhouetteSource FromImage(Image image, Transform parent)
    {
        var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
        if (sprite == null) throw new InvalidOperationException("Selection image has no sprite.");
        var uv = DataUtility.GetOuterUV(sprite);
        var rect = image.GetPixelAdjustedRect();
        if (image.preserveAspect)
        {
            var ratio = sprite.rect.width / sprite.rect.height;
            if (rect.width / rect.height > ratio)
            {
                var width = rect.height * ratio;
                rect.x += (rect.width - width) * image.rectTransform.pivot.x;
                rect.width = width;
            }
            else
            {
                var height = rect.width / ratio;
                rect.y += (rect.height - height) * image.rectTransform.pivot.y;
                rect.height = height;
            }
        }
        var bottomLeft = Project(parent, image.transform.TransformPoint(new Vector3(rect.xMin, rect.yMin)));
        var topRight = Project(parent, image.transform.TransformPoint(new Vector3(rect.xMax, rect.yMax)));
        return new UiSilhouetteSource(sprite.texture, Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y),
            new Vector2(uv.x, uv.y), new Vector2(uv.z - uv.x, 0f), new Vector2(0f, uv.w - uv.y));
    }

    public static UiSilhouetteSource FromNativeCard(Transform card, Transform parent)
    {
        var frame = card.Find("Front/FrontBack");
        if (frame == null) throw new InvalidOperationException("Native card frame is missing.");
        if (card.Find("Front/background")?.GetComponent<MeshRenderer>() == null)
            return FromImage(frame.GetComponent<Image>() ?? throw new InvalidOperationException("Native card frame image is missing."), parent);

        // Follow the same explicit mesh/image branch as ICard.SetCardStyle.
        // Read geometry, texture and UVs without modifying the native material.
        var renderer = frame.GetComponent<MeshRenderer>();
        var mesh = frame.GetComponent<MeshFilter>()?.sharedMesh;
        var material = renderer != null ? renderer.sharedMaterial : null;
        var texture = material != null ? material.mainTexture : null;
        if (mesh == null || texture == null) throw new InvalidOperationException("Native card frame geometry or texture is missing.");
        var vertices = mesh.vertices;
        var uvs = mesh.uv;
        if (vertices.Length < 3 || vertices.Length != uvs.Length) throw new InvalidOperationException("Native card frame UVs are missing.");
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var points = new Vector2[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            points[i] = Project(parent, frame.TransformPoint(vertices[i]));
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
        Vector2 UvAt(Vector2 point)
        {
            var index = 0;
            for (var i = 1; i < points.Length; i++)
                if ((points[i] - point).sqrMagnitude < (points[index] - point).sqrMagnitude) index = i;
            return Vector2.Scale(uvs[index], material!.mainTextureScale) + material.mainTextureOffset;
        }
        var origin = UvAt(min);
        return new UiSilhouetteSource(texture, Rect.MinMaxRect(min.x, min.y, max.x, max.y), origin,
            UvAt(new Vector2(max.x, min.y)) - origin, UvAt(new Vector2(min.x, max.y)) - origin);
    }

    private static Vector2 Project(Transform parent, Vector3 worldPoint)
    {
        var canvas = parent.GetComponentInParent<Canvas>()?.rootCanvas;
        if (parent is not RectTransform rect || canvas == null) return parent.InverseTransformPoint(worldPoint);
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (camera == null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) return parent.InverseTransformPoint(worldPoint);
        var screen = RectTransformUtility.WorldToScreenPoint(camera, worldPoint);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, camera, out var point))
            throw new InvalidOperationException("Native card frame cannot be projected into its selection canvas.");
        return point;
    }
}
