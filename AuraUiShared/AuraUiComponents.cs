using System;
using TMPro;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraUi.Shared;

public static class AuraUiComponents
{
    public static GameObject CreateRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = Vector2.zero;
        return go;
    }

    public static GameObject CreateLayout(string name, Transform parent)
    {
        return CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
    }

    public static LayoutElement EnsureLayoutElement(GameObject go)
    {
        return go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
    }

    public static Text ConfigureText(
        GameObject go,
        string value,
        int fontSize,
        int minimumFontSize,
        TextAnchor anchor,
        Color color,
        bool resizeForBestFit = false)
    {
        var text = go.AddComponent<Text>();
        AuraUiNativeBridge.Apply(text);
        text.text = value;
        text.fontSize = Math.Max(fontSize, minimumFontSize);
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = resizeForBestFit;
        if (resizeForBestFit)
        {
            text.resizeTextMinSize = Math.Max(10, fontSize - 5);
            text.resizeTextMaxSize = fontSize;
        }

        text.raycastTarget = false;
        return text;
    }

    public static TextMeshProUGUI ConfigureTmpText(
        GameObject go,
        string value,
        float fontSize,
        float minimumFontSize,
        TextAnchor anchor,
        Color color,
        bool resizeForBestFit = false,
        AuraUiTheme? theme = null)
    {
        var text = go.AddComponent<TextMeshProUGUI>();
        AuraUiNativeBridge.Apply(text, theme);
        text.text = value;
        text.fontSize = Math.Max(fontSize, minimumFontSize);
        text.color = color;
        text.alignment = AuraUiNativeBridge.ToTmpAlignment(anchor);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Truncate;
        text.enableAutoSizing = resizeForBestFit;
        if (resizeForBestFit)
        {
            text.fontSizeMin = Math.Max(8f, minimumFontSize);
            text.fontSizeMax = Math.Max(fontSize, minimumFontSize);
        }

        text.raycastTarget = false;
        return text;
    }

    public static void ClearChildren(Transform? transform, string source, Action<string>? debug = null)
    {
        if (transform == null)
        {
            return;
        }

        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            UiRaycastSafeDestroyRuntime.DisableAndHide(child.gameObject, source, debug);
            Object.Destroy(child.gameObject);
        }

        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(2, source + ":children", debug);
    }
}
