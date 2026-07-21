using System;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.GameApi;

public readonly struct MapItemIconBaseline
{
    public MapItemIconBaseline(Vector3 localScale, Vector2 anchoredPosition)
    {
        LocalScale = localScale;
        AnchoredPosition = anchoredPosition;
    }

    public Vector3 LocalScale { get; }

    public Vector2 AnchoredPosition { get; }
}

public static class MapItemApi
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    public static bool TryCaptureIconBaseline(MapItem item, out MapItemIconBaseline baseline)
    {
        baseline = default;
        if (!TryGetIconParts(item, out var icon, out _, out var rectTransform))
        {
            return false;
        }

        baseline = new MapItemIconBaseline(
            icon.localScale,
            rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero);
        return true;
    }

    public static bool ApplyCardBackgroundTexture(
        MapItem item,
        Texture texture,
        bool hideIcon,
        out string appliedTarget)
    {
        appliedTarget = "";
        if (item == null || texture == null)
        {
            return false;
        }

        var background = item.transform.Find("Front/background");
        var backgroundRenderer = background != null ? background.GetComponent<MeshRenderer>() : null;
        if (backgroundRenderer != null)
        {
            ApplyRendererTexture(backgroundRenderer, texture);
            if (hideIcon)
            {
                var icon = item.transform.Find("Front/icon");
                if (icon != null)
                {
                    icon.gameObject.SetActive(false);
                }
            }

            appliedTarget = "Front/background";
            return true;
        }

        if (TryGetIconParts(item, out var iconTransform, out var iconRenderer, out _))
        {
            iconTransform.gameObject.SetActive(true);
            ApplyRendererTexture(iconRenderer, texture);
            appliedTarget = "Front/icon";
            return true;
        }

        return false;
    }

    public static bool ApplyTexture(
        MapItem item,
        Texture texture,
        MapNodeCardArtSpec spec,
        MapItemIconBaseline? baseline)
    {
        if (texture == null || spec == null)
        {
            return false;
        }

        if (!TryGetIconParts(item, out var icon, out var renderer, out var rectTransform))
        {
            return false;
        }

        icon.gameObject.SetActive(true);
        if (baseline.HasValue)
        {
            icon.localScale = baseline.Value.LocalScale;
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = baseline.Value.AnchoredPosition;
            }
        }

        ApplyRendererTexture(renderer, texture);
        var fit = MapNodeTextureFitService.Fit(
            TextureBounds(texture, spec.AlphaThreshold),
            spec.FitMode,
            spec.BoundsWidth,
            spec.BoundsHeight,
            spec.OffsetX,
            spec.OffsetY);
        if (!fit.ShouldApplyTransform)
        {
            return true;
        }

        var z = baseline?.LocalScale.z ?? icon.localScale.z;
        icon.localScale = new Vector3(fit.ScaleX, fit.ScaleY, z);
        if (rectTransform != null)
        {
            var anchor = baseline?.AnchoredPosition ?? rectTransform.anchoredPosition;
            rectTransform.anchoredPosition = anchor + new Vector2(fit.OffsetX, fit.OffsetY);
        }

        return true;
    }

    private static void ApplyRendererTexture(MeshRenderer renderer, Texture texture)
    {
        var material = renderer.material;
        if (material != null)
        {
            material.mainTexture = texture;
            return;
        }

        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetTexture(MainTexId, texture);
        renderer.SetPropertyBlock(block);
    }

    private static bool TryGetIconParts(
        MapItem item,
        out Transform icon,
        out MeshRenderer renderer,
        out RectTransform? rectTransform)
    {
        icon = null!;
        renderer = null!;
        rectTransform = null;
        if (item == null)
        {
            return false;
        }

        icon = item.transform.Find("Front/icon");
        if (icon == null)
        {
            return false;
        }

        renderer = icon.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            return false;
        }

        rectTransform = icon.GetComponent<RectTransform>();
        return true;
    }

    private static MapNodeTextureBounds TextureBounds(Texture texture, float alphaThreshold)
    {
        if (texture is Texture2D texture2D)
        {
            try
            {
                var edges = TextureTransparencyAnalyzer.AnalyzeAllEdges(texture2D, alphaThreshold);
                return new MapNodeTextureBounds(
                    texture.width,
                    texture.height,
                    edges.leftTransparentWidth,
                    edges.rightTransparentWidth,
                    edges.topTransparentHeight,
                    edges.bottomTransparentHeight);
            }
            catch (Exception)
            {
                return new MapNodeTextureBounds(texture.width, texture.height, 0, 0, 0, 0);
            }
        }

        return new MapNodeTextureBounds(texture.width, texture.height, 0, 0, 0, 0);
    }
}
