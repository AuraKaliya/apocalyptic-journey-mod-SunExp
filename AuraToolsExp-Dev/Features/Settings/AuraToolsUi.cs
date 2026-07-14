using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;
using UiRaycastSafetyShared;
using Witch.Core;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class AuraToolsUi
{
    public static AuraUiTheme Theme => AuraToolsUiTheme.Current;

    private const string ButtonSpritePath = "Mods/AuraToolsExp/ui-img/button-\u4e5d\u5bab\u683c.png";
    private const string PanelSpritePath = "Mods/AuraToolsExp/ui-img/background-\u4e5d\u5bab\u683c.png";
    public static readonly Color Background = new(0.03f, 0.025f, 0.05f, 0.96f);
    public static readonly Color Panel = new(0.07f, 0.06f, 0.11f, 0.96f);
    public static readonly Color Header = new(0.12f, 0.10f, 0.18f, 0.98f);
    public static readonly Color Row = new(0.09f, 0.085f, 0.14f, 0.98f);
    public static readonly Color Accent = new(0.85f, 0.70f, 0.42f, 1f);
    public static readonly Color Text = new(0.93f, 0.90f, 0.78f, 1f);
    public static readonly Color MutedText = new(0.66f, 0.62f, 0.50f, 1f);
    public static readonly Color SuccessText = new(0.58f, 0.94f, 0.62f, 1f);
    public static readonly Color WarningText = new(0.98f, 0.78f, 0.36f, 1f);
    public static readonly Color ActiveRow = new(0.12f, 0.18f, 0.13f, 0.98f);
    public const int TabFontSize = 19;
    public const int SectionFontSize = 21;
    public const int ModuleTitleFontSize = 18;
    public const int BodyFontSize = 16;
    public const int HintFontSize = 15;
    public const int ButtonFontSize = 16;
    public const float SectionHeight = 40f;
    public const float OverlayTitleHeight = 52f;
    public const float ModuleHeaderHeight = 45f;
    public const float InlineRowHeight = 50f;
    public const float ButtonHeight = 46f;
    public const float ButtonMinWidth = 120f;
    public const float TextMinHeight = 40f;
    public const float ToolbarHeight = 52f;
    public const float FooterHeight = 54f;
    public const float ColumnHeaderHeight = 45f;
    public const float DataRowHeight = 50f;
    public const float RoleRowHeight = 48f;
    public const float RuleBlockHeight = 112f;
    public const float ToggleSize = 28f;
    private static Sprite? buttonSprite;
    private static Sprite? panelSprite;
    private static bool buttonSpriteLoadAttempted;
    private static bool panelSpriteLoadAttempted;
    private static GameObject? activeSelectPopup;
    private static GameObject? activeSelectAnchor;

    public static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        return AuraUiComponents.CreateRect(name, parent, anchorMin, anchorMax, pivot, sizeDelta);
    }

    public static GameObject CreateLayout(string name, Transform parent)
    {
        return AuraUiComponents.CreateLayout(name, parent);
    }

    public static Image AddImage(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    public static Image AddPanelImage(GameObject go, Color fallbackOrTint)
    {
        var image = go.AddComponent<Image>();
        image.sprite = GetPanelSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, fallbackOrTint.a) : fallbackOrTint;
        if (image.sprite != null)
        {
            AddPanelTint(go, fallbackOrTint);
        }

        return image;
    }

    public static Image AddButtonImage(GameObject go, Color fallbackTint)
    {
        var image = go.AddComponent<Image>();
        image.sprite = GetButtonSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? Color.white : fallbackTint;
        return image;
    }

    public static LayoutElement EnsureLayoutElement(GameObject go)
    {
        return AuraUiComponents.EnsureLayoutElement(go);
    }

    public static LayoutElement SetFixedHeight(GameObject go, float height)
    {
        var element = EnsureLayoutElement(go);
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
        return element;
    }

    public static LayoutElement SetFixedSize(GameObject go, float width, float height)
    {
        var element = SetFixedHeight(go, height);
        element.minWidth = width;
        element.preferredWidth = width;
        element.flexibleWidth = 0f;
        return element;
    }

    public static Text AddText(Transform parent, string value, int fontSize, TextAnchor anchor, Color color, float preferredHeight = TextMinHeight, float flexibleWidth = 0f, float preferredWidth = 0f)
    {
        var go = CreateLayout("Text", parent);
        var element = EnsureLayoutElement(go);
        var height = Mathf.Max(preferredHeight, TextMinHeight);
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
        if (flexibleWidth > 0f)
        {
            element.flexibleWidth = flexibleWidth;
        }

        if (preferredWidth > 0f)
        {
            element.minWidth = preferredWidth;
            element.preferredWidth = preferredWidth;
        }

        return ConfigureText(go, value, fontSize, anchor, color);
    }

    public static Text AddFillText(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    public static Button AddButton(Transform parent, string label, Action action, float width = 108f, float height = ButtonHeight)
    {
        var go = CreateLayout("Button-" + label, parent);
        var element = EnsureLayoutElement(go);
        var resolvedWidth = Mathf.Max(width, 48f);
        var resolvedHeight = Mathf.Max(height, ButtonHeight);
        element.minWidth = resolvedWidth;
        element.preferredWidth = resolvedWidth;
        element.minHeight = resolvedHeight;
        element.preferredHeight = resolvedHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        var image = AddButtonImage(go, new Color(0.16f, 0.13f, 0.22f, 0.98f));
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        AddFillText(go.transform, label, ButtonFontSize, TextAnchor.MiddleCenter, Text);
        return button;
    }

    public static Toggle AddToggle(Transform parent, bool value, Action<bool> changed, float size = ToggleSize)
    {
        var root = CreateLayout("Toggle", parent);
        var element = EnsureLayoutElement(root);
        element.minWidth = size;
        element.preferredWidth = size;
        element.minHeight = size;
        element.preferredHeight = size;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        var background = CreateRect("Background", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(size, size));
        AddImage(background, new Color(0.02f, 0.02f, 0.04f, 1f));

        var check = CreateRect("Checkmark", background.transform, new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero);
        AddImage(check, Accent);

        var toggle = root.AddComponent<Toggle>();
        toggle.targetGraphic = background.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = value;
        toggle.onValueChanged.AddListener(v => changed(v));
        return toggle;
    }

    public static InputField AddInput(Transform parent, string value, Action<string> changed, float width = 180f, float height = ButtonHeight)
    {
        var root = CreateLayout("Input", parent);
        var element = EnsureLayoutElement(root);
        var resolvedWidth = Mathf.Max(width, 80f);
        var resolvedHeight = Mathf.Max(height, ButtonHeight);
        element.minWidth = resolvedWidth;
        element.preferredWidth = resolvedWidth;
        element.minHeight = resolvedHeight;
        element.preferredHeight = resolvedHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        AddImage(root, new Color(0.025f, 0.022f, 0.045f, 0.98f));

        var textObject = CreateRect("Text", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.offsetMin = new Vector2(8f, 2f);
        textRect.offsetMax = new Vector2(-8f, -2f);
        var text = ConfigureText(textObject, value, HintFontSize, TextAnchor.MiddleLeft, Text);
        text.raycastTarget = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        var placeholderObject = CreateRect("Placeholder", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var placeholderRect = placeholderObject.GetComponent<RectTransform>();
        placeholderRect.offsetMin = new Vector2(8f, 2f);
        placeholderRect.offsetMax = new Vector2(-8f, -2f);
        var placeholder = ConfigureText(placeholderObject, "输入...", HintFontSize, TextAnchor.MiddleLeft, MutedText);

        var input = root.AddComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = value ?? "";
        input.onEndEdit.AddListener(changed.Invoke);
        return input;
    }

    public static Button AddSelectButton(Transform parent, IReadOnlyList<string> labels, int selectedIndex, Action<int> changed, float width = 220f, float height = ButtonHeight)
    {
        var root = CreateLayout("Dropdown", parent);
        var element = EnsureLayoutElement(root);
        var resolvedWidth = Mathf.Max(width, ButtonMinWidth);
        var resolvedHeight = Mathf.Max(height, ButtonHeight);
        element.minWidth = resolvedWidth;
        element.preferredWidth = resolvedWidth;
        element.minHeight = resolvedHeight;
        element.preferredHeight = resolvedHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        var image = AddButtonImage(root, new Color(0.025f, 0.022f, 0.045f, 0.98f));

        var labelObject = CreateRect("Label", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.offsetMin = new Vector2(8f, 2f);
        labelRect.offsetMax = new Vector2(-30f, -2f);
        var caption = ConfigureText(labelObject, "", HintFontSize, TextAnchor.MiddleLeft, Text);
        caption.horizontalOverflow = HorizontalWrapMode.Overflow;

        var arrowObject = CreateRect("Arrow", root.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(24f, 0f));
        var arrowRect = arrowObject.GetComponent<RectTransform>();
        arrowRect.anchoredPosition = new Vector2(-8f, 0f);
        ConfigureText(arrowObject, "v", HintFontSize, TextAnchor.MiddleCenter, Accent);

        var normalizedLabels = labels == null || labels.Count == 0 ? new List<string> { "" } : labels.Select(label => label ?? "").ToList();
        var currentIndex = Mathf.Clamp(selectedIndex, 0, normalizedLabels.Count - 1);
        caption.text = normalizedLabels[currentIndex];

        var button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            ShowSelectPopup(root, normalizedLabels, currentIndex, index =>
            {
                currentIndex = Mathf.Clamp(index, 0, normalizedLabels.Count - 1);
                caption.text = normalizedLabels[currentIndex];
                changed(currentIndex);
            }, resolvedHeight);
        });
        return button;
    }

    public static Transform CreateScroll(Transform parent, string name)
    {
        var root = CreateLayout("Scroll-" + name, parent);
        var element = root.AddComponent<LayoutElement>();
        element.flexibleHeight = 1f;
        element.flexibleWidth = 1f;
        AddImage(root, new Color(0f, 0f, 0f, 0.01f));

        var viewport = CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        viewport.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        AddImage(viewport, new Color(0f, 0f, 0f, 0.01f));
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateRect("Content", viewport.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 24f;
        return content.transform;
    }

    public static GameObject CreateOverlay(string name, Transform parent, string title, Action? onClose = null, bool singleInstance = true, float maxWidth = 1180f)
    {
        var overlayRoot = ResolveOverlayRoot(parent);
        if (singleInstance)
        {
            CloseOverlay(overlayRoot, name, "AuraTools overlay single instance");
        }

        var overlay = CreateRect(name, overlayRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.pivot = new Vector2(0.5f, 0.5f);
        overlayRect.offsetMin = new Vector2(8f, 8f);
        overlayRect.offsetMax = new Vector2(-8f, -8f);
        overlay.transform.SetAsLastSibling();
        EnsureLayoutElement(overlay).ignoreLayout = true;
        AddImage(overlay, new Color(0f, 0f, 0f, 0.68f));

        Canvas.ForceUpdateCanvases();
        var availableWidth = overlayRect.rect.width;
        var useFixedWidth = availableWidth > maxWidth + 36f;
        var window = useFixedWidth
            ? CreateRect("Window", overlay.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero)
            : CreateRect("Window", overlay.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        var windowRect = window.GetComponent<RectTransform>();
        if (useFixedWidth)
        {
            windowRect.sizeDelta = new Vector2(maxWidth, -16f);
            windowRect.anchoredPosition = Vector2.zero;
        }
        else
        {
            windowRect.offsetMin = new Vector2(10f, 8f);
            windowRect.offsetMax = new Vector2(-10f, -8f);
        }
        AddPanelImage(window, Background);
        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CreateLayout("Header", window.transform);
        SetFixedHeight(header, OverlayTitleHeight);
        AddPanelImage(header, Header);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(12, 8, 4, 4);
        headerLayout.spacing = 8f;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        AddText(header.transform, title, SectionFontSize, TextAnchor.MiddleLeft, Accent, TextMinHeight, 1f);
        AddButton(header.transform, "关闭", () =>
        {
            CloseSelectPopup();
            onClose?.Invoke();
            UiRaycastSafeDestroyRuntime.DisableAndHide(overlay, "AuraTools overlay close");
            Object.Destroy(overlay);
        }, ButtonMinWidth, ButtonHeight);

        return window;
    }

    public static void CloseOverlay(Transform parent, string name, string source = "AuraTools overlay close")
    {
        var overlayRoot = ResolveOverlayRoot(parent);
        for (var i = overlayRoot.childCount - 1; i >= 0; i--)
        {
            var child = overlayRoot.GetChild(i);
            if (!string.Equals(child.name, name, StringComparison.Ordinal))
            {
                continue;
            }

            CloseSelectPopup();
            UiRaycastSafeDestroyRuntime.DisableAndHide(child.gameObject, source);
            Object.Destroy(child.gameObject);
        }
    }

    public static void CloseSelectPopup()
    {
        if (activeSelectPopup != null)
        {
            UiRaycastSafeDestroyRuntime.DisableAndHide(activeSelectPopup, "AuraTools select popup close");
            Object.Destroy(activeSelectPopup);
            activeSelectPopup = null;
        }

        activeSelectAnchor = null;
    }

    private static void ShowSelectPopup(GameObject anchor, IReadOnlyList<string> labels, int selectedIndex, Action<int> selected, float rowHeight)
    {
        if (activeSelectPopup != null && activeSelectAnchor == anchor)
        {
            CloseSelectPopup();
            return;
        }

        CloseSelectPopup();

        var popupRoot = ResolvePopupRoot(anchor.transform);
        Canvas.ForceUpdateCanvases();
        var layer = CreateRect("SelectPopupLayer", popupRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        activeSelectPopup = layer;
        activeSelectAnchor = anchor;
        layer.transform.SetAsLastSibling();
        EnsureLayoutElement(layer).ignoreLayout = true;
        var layerRect = layer.GetComponent<RectTransform>();
        layerRect.pivot = new Vector2(0.5f, 0.5f);
        layerRect.offsetMin = Vector2.zero;
        layerRect.offsetMax = Vector2.zero;

        var layerImage = AddImage(layer, new Color(0f, 0f, 0f, 0.01f));
        var layerButton = layer.AddComponent<Button>();
        layerButton.targetGraphic = layerImage;
        layerButton.onClick.AddListener(CloseSelectPopup);

        var popup = CreateLayout("SelectPopup", layer.transform);
        AddPanelImage(popup, Panel);

        var popupRect = popup.GetComponent<RectTransform>();
        var anchorRect = anchor.GetComponent<RectTransform>();
        if (anchorRect != null && layerRect != null)
        {
            PositionSelectPopup(anchorRect, layerRect, popupRect, rowHeight, labels.Count);
        }

        var layout = popup.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 2f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var label = labels[i];
            var row = CreateLayout("SelectItem-" + index, popup.transform);
            SetFixedHeight(row, rowHeight);
            var image = AddImage(row, index == selectedIndex ? Header : Row);
            var button = row.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                selected(index);
                CloseSelectPopup();
            });
            AddFillText(row.transform, label, HintFontSize, TextAnchor.MiddleLeft, Text);
        }
    }

    private static void PositionSelectPopup(RectTransform anchorRect, RectTransform rootRect, RectTransform popupRect, float rowHeight, int itemCount)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);

        var camera = ResolveCanvasCamera(rootRect);
        var corners = new Vector3[4];
        anchorRect.GetWorldCorners(corners);
        var bottomLeft = WorldToLocal(rootRect, corners[0], camera);
        var topRight = WorldToLocal(rootRect, corners[2], camera);
        var width = Mathf.Max(180f, topRight.x - bottomLeft.x);
        var height = Mathf.Min(260f, Mathf.Max(rowHeight, itemCount * rowHeight));
        var margin = 4f;
        var rect = rootRect.rect;

        var minX = rect.xMin + margin;
        var maxX = rect.xMax - width - margin;
        var x = maxX >= minX ? Mathf.Clamp(bottomLeft.x, minX, maxX) : minX;

        var downTopY = bottomLeft.y - margin;
        var downBottomY = downTopY - height;
        var upBottomY = topRight.y + margin;
        var upTopY = upBottomY + height;
        var openUp = downBottomY < rect.yMin + margin && upTopY <= rect.yMax - margin;

        popupRect.anchorMin = new Vector2(0.5f, 0.5f);
        popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.pivot = openUp ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
        popupRect.sizeDelta = new Vector2(width, height);

        var y = openUp ? upBottomY : downTopY;
        if (openUp)
        {
            y = Mathf.Clamp(y, rect.yMin + margin, rect.yMax - height - margin);
        }
        else
        {
            y = Mathf.Clamp(y, rect.yMin + height + margin, rect.yMax - margin);
        }

        popupRect.anchoredPosition = new Vector2(x, y);
    }

    private static Vector2 WorldToLocal(RectTransform rootRect, Vector3 worldPoint, Camera? camera)
    {
        var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, worldPoint);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenPoint, camera, out var localPoint)
            ? localPoint
            : (Vector2)rootRect.InverseTransformPoint(worldPoint);
    }

    private static Camera? ResolveCanvasCamera(RectTransform source)
    {
        var canvas = source.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera;
    }

    private static Transform ResolvePopupRoot(Transform source)
    {
        var current = source;
        while (current != null)
        {
            if (current.name == "Window")
            {
                return current;
            }

            current = current.parent;
        }

        return source.root;
    }

    public static void ClearChildren(Transform transform)
    {
        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            UiRaycastSafeDestroyRuntime.DisableAndHide(transform.GetChild(i).gameObject, "AuraTools clear children");
            Object.Destroy(transform.GetChild(i).gameObject);
        }
    }

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        return AuraUiComponents.ConfigureText(go, value, fontSize, HintFontSize, anchor, color);
    }

    private static Sprite? GetButtonSprite()
    {
        if (buttonSprite != null)
        {
            return buttonSprite;
        }

        if (buttonSpriteLoadAttempted)
        {
            return null;
        }

        buttonSpriteLoadAttempted = true;
        buttonSprite = CreateNineSliceSprite(ButtonSpritePath, new Vector4(14f, 14f, 14f, 14f), false, new Rect(17f, 16f, 135f, 49f));
        return buttonSprite;
    }

    private static Sprite? GetPanelSprite()
    {
        if (panelSprite != null)
        {
            return panelSprite;
        }

        if (panelSpriteLoadAttempted)
        {
            return null;
        }

        panelSpriteLoadAttempted = true;
        panelSprite = CreateNineSliceSprite(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f), true);
        return panelSprite;
    }

    private static Sprite? CreateNineSliceSprite(string path, Vector4 fallbackBorder, bool preferSourceBorder, Rect? sourceCrop = null)
    {
        try
        {
            var source = AuraToolsResourceCache.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                return null;
            }

            var texture = source.texture;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var rect = ResolveSpriteRect(source, sourceCrop);
            var border = ResolveSpriteBorder(rect, source, fallbackBorder, preferSourceBorder);
            return Sprite.Create(
                texture,
                rect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
        }
        catch
        {
            return null;
        }
    }

    private static void AddPanelTint(GameObject target, Color color)
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
        image.raycastTarget = false;
    }

    private static Transform ResolveOverlayRoot(Transform parent)
    {
        if (parent is RectTransform)
        {
            return parent;
        }

        var current = parent;
        while (current.parent != null && current.parent is RectTransform)
        {
            current = current.parent;
        }

        return current;
    }

    private static Rect ResolveSpriteRect(Sprite source, Rect? sourceCrop)
    {
        if (sourceCrop == null)
        {
            return source.rect;
        }

        var crop = sourceCrop.Value;
        var x = Mathf.Clamp(source.rect.x + crop.x, source.rect.x, source.rect.xMax);
        var y = Mathf.Clamp(source.rect.y + crop.y, source.rect.y, source.rect.yMax);
        var width = Mathf.Clamp(crop.width, 1f, source.rect.xMax - x);
        var height = Mathf.Clamp(crop.height, 1f, source.rect.yMax - y);
        return new Rect(x, y, width, height);
    }

    private static Vector4 ResolveSpriteBorder(Rect rect, Sprite source, Vector4 fallbackBorder, bool preferSourceBorder)
    {
        if (preferSourceBorder && source.border.sqrMagnitude > 0.01f)
        {
            return source.border;
        }

        var width = rect.width;
        var height = rect.height;
        if (width <= 0f || height <= 0f)
        {
            return fallbackBorder;
        }

        var x = Mathf.Clamp(width * 0.22f, 6f, fallbackBorder.x);
        var y = Mathf.Clamp(height * 0.30f, 6f, fallbackBorder.y);
        return new Vector4(x, y, x, y);
    }
}
