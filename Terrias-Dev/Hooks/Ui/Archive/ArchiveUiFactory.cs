using System;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui.Archive;

public static class ArchiveUiFactory
{
    public static RectTransform CreateTopLeft(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height)
    {
        var rect = TerriasUiBuilder.CreateRect(
            name,
            parent,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(width, height));
        rect.anchoredPosition = new Vector2(x, -y);
        return rect;
    }

    public static RectTransform CreateFromRect(string name, Transform parent, Rect bounds)
    {
        return CreateTopLeft(name, parent, bounds.x, bounds.y, bounds.width, bounds.height);
    }

    public static RectTransform CreateFill(string name, Transform parent, Vector4 insets)
    {
        var rect = TerriasUiBuilder.CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        rect.offsetMin = new Vector2(insets.x, insets.w);
        rect.offsetMax = new Vector2(-insets.z, -insets.y);
        return rect;
    }

    public static Image ApplyPanel(GameObject root, Color color, bool raycastTarget = false)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    public static Text CreateText(
        string name,
        Transform parent,
        string value,
        int fontSize,
        TextAnchor alignment,
        Color color,
        bool bestFit = false)
    {
        var rect = TerriasUiBuilder.CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Normal;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = bestFit;
        if (bestFit)
        {
            text.resizeTextMinSize = Math.Max(11, fontSize - 7);
            text.resizeTextMaxSize = fontSize;
        }

        text.supportRichText = true;
        text.raycastTarget = false;
        TerriasLocalizationScope.BindLegacyIfAvailable(text, value);
        return text;
    }

    public static Text CreateAutoHeightText(
        string name,
        Transform parent,
        string value,
        int fontSize,
        Color color,
        float minimumHeight = 30f,
        float lineSpacing = 1.15f)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>();
        rect.pivot = new Vector2(0.5f, 1f);
        var element = root.AddComponent<LayoutElement>();
        element.minHeight = minimumHeight;
        element.flexibleWidth = 1f;
        var text = root.AddComponent<Text>();
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.text = value;
        text.fontSize = fontSize;
        text.lineSpacing = lineSpacing;
        text.color = color;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        text.raycastTarget = false;
        TerriasLocalizationScope.BindLegacyIfAvailable(text, value);
        return text;
    }

    public static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Color background,
        Color textColor,
        int fontSize,
        Action onClick)
    {
        var button = CreateButtonSurface(name, parent, background, onClick);
        var text = CreateText("Label", button.transform, label, fontSize, TextAnchor.MiddleCenter, textColor, true);
        var textRect = text.rectTransform;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);
        return button;
    }

    public static Button CreateButtonSurface(
        string name,
        Transform parent,
        Color background,
        Action onClick)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);
        var image = root.GetComponent<Image>();
        image.color = background;
        image.raycastTarget = true;
        var button = root.GetComponent<Button>();
        button.targetGraphic = image;
        AuraUiButtonFeedback.Apply(button, image, ArchiveUiTheme.Accent);
        button.onClick.AddListener(() => onClick());
        return button;
    }

    public static ScrollRect CreateVerticalScroll(
        string name,
        Transform parent,
        out RectTransform content,
        Vector4 insets,
        bool showScrollbar = false)
    {
        var root = CreateFill(name, parent, insets);
        var viewport = TerriasUiBuilder.CreateRect("Viewport", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (showScrollbar)
        {
            viewport.offsetMax = new Vector2(-12f, 0f);
        }

        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        content = TerriasUiBuilder.CreateRect(
            "Content",
            viewport,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 16, 4, 16);
        layout.spacing = 16f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;
        if (showScrollbar)
        {
            var scrollbarRect = TerriasUiBuilder.CreateRect(
                "VerticalScrollbar",
                root,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(6f, 0f));
            scrollbarRect.anchoredPosition = new Vector2(-2f, 0f);
            var trackImage = scrollbarRect.gameObject.AddComponent<Image>();
            trackImage.color = ArchiveUiTheme.Divider;
            trackImage.raycastTarget = true;

            var slidingArea = CreateFill("SlidingArea", scrollbarRect, new Vector4(0f, 4f, 0f, 4f));
            var handle = CreateFill("Handle", slidingArea, Vector4.zero);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = ArchiveUiTheme.AccentMuted;
            handleImage.raycastTarget = true;

            var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 4f;
        }

        return scroll;
    }

}
