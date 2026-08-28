using System;
using System.Collections.Generic;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.GameApi;

internal static class SpiritElementUiApi
{
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.Ordinal);

    public static (GameObject Root, Image Icon, Text Label) CreateBadge(
        Transform parent,
        string name,
        float width,
        float height,
        bool ignoreLayout = false)
    {
        var root = CreateRect(
            name,
            parent,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(width, height));
        var layout = root.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;
        layout.minWidth = width;
        layout.minHeight = height;
        layout.ignoreLayout = ignoreLayout;
        var row = root.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(1, 1, 1, 1);
        row.spacing = 3f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleCenter;

        var iconRoot = CreateRect(
            "Icon",
            root.transform,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(height - 2f, height - 2f));
        var iconLayout = iconRoot.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = height - 2f;
        iconLayout.preferredHeight = height - 2f;
        var icon = iconRoot.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        var labelRoot = CreateRect("Text", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var labelLayout = labelRoot.AddComponent<LayoutElement>();
        labelLayout.preferredHeight = height;
        labelLayout.flexibleWidth = 1f;
        var label = AuraUiComponents.ConfigureText(
            labelRoot,
            "",
            Math.Max(10, Mathf.RoundToInt(height * 0.62f)),
            10,
            TextAnchor.MiddleLeft,
            Color.white,
            resizeForBestFit: true);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        return (root, icon, label);
    }

    public static (GameObject Root, Image Icon) CreateIcon(
        Transform parent,
        string name,
        float size)
    {
        var normalizedSize = Math.Max(8f, size);
        var root = CreateRect(
            name,
            parent,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(normalizedSize, normalizedSize));
        var layout = root.AddComponent<LayoutElement>();
        layout.preferredWidth = normalizedSize;
        layout.preferredHeight = normalizedSize;
        layout.minWidth = normalizedSize;
        layout.minHeight = normalizedSize;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
        var icon = root.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        return (root, icon);
    }

    public static void Bind(
        Image? icon,
        Text? label,
        string normalizedElementId,
        string displayName,
        string iconPath)
    {
        BindIcon(icon, iconPath);

        if (label != null)
        {
            label.text = displayName ?? "";
            label.color = Tint(normalizedElementId);
        }
    }

    public static void BindIcon(Image? icon, string iconPath)
    {
        var sprite = Resolve(iconPath);
        if (icon == null) return;
        icon.sprite = sprite;
        icon.color = sprite == null ? Color.clear : Color.white;
    }

    public static Color Tint(string normalizedElementId)
    {
        return (normalizedElementId ?? "").Trim().ToLowerInvariant() switch
        {
            "pyro" => new Color(0.96f, 0.43f, 0.31f, 1f),
            "hydro" => new Color(0.35f, 0.68f, 0.96f, 1f),
            "geo" => new Color(0.91f, 0.70f, 0.28f, 1f),
            "dendro" => new Color(0.45f, 0.78f, 0.30f, 1f),
            "electro" => new Color(0.72f, 0.51f, 0.94f, 1f),
            "cryo" => new Color(0.54f, 0.88f, 0.93f, 1f),
            "anemo" => new Color(0.38f, 0.83f, 0.70f, 1f),
            _ => Color.white
        };
    }

    private static GameObject CreateRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size)
    {
        var root = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        return root;
    }

    private static Sprite? Resolve(string iconPath)
    {
        var path = (iconPath ?? "").Trim();
        if (path.Length == 0)
        {
            return null;
        }
        if (Sprites.TryGetValue(path, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = TerriasResourceCache.Load<Sprite>(path, true, "ui.spirit-element");
        if (sprite != null)
        {
            Sprites[path] = sprite;
        }
        return sprite;
    }
}
