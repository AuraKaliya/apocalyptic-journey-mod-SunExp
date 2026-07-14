using System;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpUiComponents
{
    public static AuraUiTheme Theme => SunExpUiTheme.Current;

    public sealed class ScrollArea
    {
        public ScrollArea(GameObject root, RectTransform viewport, RectTransform content, ScrollRect scroll)
        {
            Root = root;
            Viewport = viewport;
            Content = content;
            Scroll = scroll;
        }

        public GameObject Root { get; }

        public RectTransform Viewport { get; }

        public RectTransform Content { get; }

        public ScrollRect Scroll { get; }
    }

    public static GameObject CreateRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size)
    {
        return SunExpUiBuilder.CreateRect(name, parent, anchorMin, anchorMax, pivot, size).gameObject;
    }

    public static RectTransform CreateRectTransform(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size)
    {
        return SunExpUiBuilder.CreateRect(name, parent, anchorMin, anchorMax, pivot, size);
    }

    public static GameObject CreateFillRect(string name, Transform parent)
    {
        return CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    public static GameObject CreateVerticalWindow(
        string name,
        Transform parent,
        Vector2 size,
        Sprite? sprite,
        Color tint,
        RectOffset padding,
        float spacing)
    {
        var window = CreateRect(
            name,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            size);
        SunExpUiBuilder.ApplyPanelImage(window, sprite, tint, true);
        ConfigureVerticalLayout(window, padding, spacing, childForceExpandHeight: false);
        return window;
    }

    public static GameObject CreatePanelSection(
        string name,
        Transform parent,
        Sprite? sprite,
        Color tint,
        float minHeight,
        float preferredHeight,
        float flexibleHeight = 0f)
    {
        var section = CreateFillRect(name, parent);
        var element = section.AddComponent<LayoutElement>();
        element.minHeight = minHeight;
        element.preferredHeight = preferredHeight;
        element.flexibleHeight = flexibleHeight;
        SunExpUiBuilder.ApplyPanelImage(section, sprite, tint, true);
        return section;
    }

    public static GameObject CreateLayoutObject(string name, Transform parent)
    {
        return CreateFillRect(name, parent);
    }

    public static GameObject CreateFlexibleSpacer(Transform parent, string name = "Spacer")
    {
        var spacer = CreateLayoutObject(name, parent);
        var element = spacer.AddComponent<LayoutElement>();
        element.minHeight = 0f;
        element.preferredHeight = 0f;
        element.flexibleHeight = 1f;
        return spacer;
    }

    public static VerticalLayoutGroup ConfigureVerticalLayout(
        GameObject go,
        RectOffset padding,
        float spacing,
        bool childControlWidth = true,
        bool childControlHeight = true,
        bool childForceExpandWidth = true,
        bool childForceExpandHeight = false,
        TextAnchor alignment = TextAnchor.UpperLeft)
    {
        var layout = go.GetComponent<VerticalLayoutGroup>() ?? go.AddComponent<VerticalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = spacing;
        layout.childControlWidth = childControlWidth;
        layout.childControlHeight = childControlHeight;
        layout.childForceExpandWidth = childForceExpandWidth;
        layout.childForceExpandHeight = childForceExpandHeight;
        layout.childAlignment = alignment;
        return layout;
    }

    public static HorizontalLayoutGroup ConfigureHorizontalLayout(
        GameObject go,
        RectOffset padding,
        float spacing,
        bool childControlWidth = true,
        bool childControlHeight = true,
        bool childForceExpandWidth = false,
        bool childForceExpandHeight = false,
        TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var layout = go.GetComponent<HorizontalLayoutGroup>() ?? go.AddComponent<HorizontalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = spacing;
        layout.childControlWidth = childControlWidth;
        layout.childControlHeight = childControlHeight;
        layout.childForceExpandWidth = childForceExpandWidth;
        layout.childForceExpandHeight = childForceExpandHeight;
        layout.childAlignment = alignment;
        return layout;
    }

    public static GameObject CreateFooterRow(
        Transform parent,
        float height,
        RectOffset padding,
        float spacing)
    {
        var footer = CreateLayoutObject("Footer", parent);
        var element = footer.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        ConfigureHorizontalLayout(footer, padding, spacing);
        return footer;
    }

    public static ScrollArea CreateVerticalScrollArea(
        Transform parent,
        string name,
        float minHeight,
        float flexibleHeight,
        float spacing,
        float scrollSensitivity,
        Color viewportColor)
    {
        var root = CreateLayoutObject("Scroll-" + name, parent);
        var element = root.AddComponent<LayoutElement>();
        element.minHeight = minHeight;
        element.flexibleHeight = flexibleHeight;
        element.flexibleWidth = 1f;

        var viewport = SunExpUiBuilder.CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = viewportColor;
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var content = SunExpUiBuilder.CreateRect(
            "Rows",
            viewport,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        ConfigureVerticalLayout(content.gameObject, new RectOffset(0, 0, 0, 0), spacing);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = scrollSensitivity;
        return new ScrollArea(root, viewport, content, scroll);
    }

    public static Text AddTextFill(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        return ConfigureText(CreateFillRect("Text", parent), value, fontSize, anchor, color);
    }

    public static Text AddTextBlock(
        Transform parent,
        string value,
        int fontSize,
        TextAnchor anchor,
        Color color,
        float preferredHeight,
        float flexibleWidth = 0f,
        float preferredWidth = 0f)
    {
        var go = CreateFillRect("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        element.flexibleWidth = flexibleWidth;
        if (preferredWidth > 0f)
        {
            element.minWidth = preferredWidth;
            element.preferredWidth = preferredWidth;
        }

        return ConfigureText(go, value, fontSize, anchor, color);
    }

    public static Button CreateTextButton(
        Transform parent,
        string label,
        Vector2 size,
        Sprite? sprite,
        Color fallback,
        Color textColor,
        int fontSize,
        Action action)
    {
        var go = CreateFillRect("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = size.x;
        element.preferredWidth = size.x;
        element.minHeight = size.y;
        element.preferredHeight = size.y;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = image.sprite != null ? Color.white : fallback;
        var button = go.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, Theme.Accent);
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, fontSize, TextAnchor.MiddleCenter, textColor);
        return button;
    }

    public static Text ConfigureText(
        GameObject go,
        string value,
        int fontSize,
        TextAnchor anchor,
        Color color,
        int minimumFontSize = 0,
        bool supportRichText = true)
    {
        var text = AuraUiComponents.ConfigureText(
            go,
            value,
            fontSize,
            minimumFontSize > 0 ? minimumFontSize : Math.Max(10, fontSize - 5),
            anchor,
            color,
            resizeForBestFit: true);
        text.supportRichText = supportRichText;
        text.fontStyle = FontStyle.Normal;
        return text;
    }
}
