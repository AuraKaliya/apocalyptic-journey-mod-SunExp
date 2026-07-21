using System;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpUiBuilder
{
    public static Image ApplyPanelImage(GameObject go, Sprite? sprite, Color fallbackOrTint, bool raycastTarget = false)
    {
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, fallbackOrTint.a) : fallbackOrTint;
        image.raycastTarget = raycastTarget;

        if (image.sprite != null)
        {
            AddPanelTint(go, fallbackOrTint, raycastTarget);
        }

        return image;
    }

    public static Image ApplyLabelImage(GameObject go, Sprite? sprite, Color fallbackOrTint, bool raycastTarget = false)
    {
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = fallbackOrTint;
        image.raycastTarget = raycastTarget;
        return image;
    }

    public static Image AddPanelTint(GameObject target, Color color, bool raycastTarget = false)
    {
        var tint = new GameObject("PanelTint", typeof(RectTransform));
        tint.transform.SetParent(target.transform, false);
        tint.transform.SetAsFirstSibling();

        var rect = tint.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);

        var layout = tint.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var image = tint.AddComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, Mathf.Min(0.62f, color.a));
        image.raycastTarget = raycastTarget;
        return image;
    }

    public static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }

    public static Text AddText(RectTransform parent, string name, string value, int fontSize, FontStyle style, TextAnchor alignment,
        Color color, Vector2 anchoredPosition, Vector2 size, int minSizePadding = 6)
    {
        var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size);
        rect.anchoredPosition = anchoredPosition;

        var text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Math.Max(10, fontSize - minSizePadding);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        return text;
    }
}
