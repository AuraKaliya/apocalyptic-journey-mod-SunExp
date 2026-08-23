using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UiRaycastSafetyShared;
using Witch.Core;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class AuraToolsUi
{
    public static AuraUiTheme Theme => AuraToolsUiTheme.Current;

    public static Color Background => Theme.Background;
    public static Color Panel => Theme.Panel;
    public static Color Header => Theme.Control;
    public static Color Row => ToolboxVisualSpec.Row;
    public static Color RowHighlighted => Theme.ControlHighlighted;
    public static Color CategorySelected => ToolboxVisualSpec.RowHighlighted;
    public static Color Accent => Theme.Accent;
    public static Color AuraAccent => new(0.667f, 0.573f, 0.863f, 1f);
    public static Color Text => Theme.Text;
    public static Color MutedText => Theme.MutedText;
    public static Color SuccessText => ToolboxVisualSpec.Success;
    public static Color WarningText => ToolboxVisualSpec.Warning;
    public static Color ErrorText => ToolboxVisualSpec.Error;
    public static Color ActiveRow => ToolboxVisualSpec.RowHighlighted;
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
    public const float CompactButtonHeight = 34f;
    public const float StandardButtonHeight = 40f;
    public const float ButtonMinWidth = 120f;
    public const float TextMinHeight = 40f;
    public const float ToolbarHeight = 52f;
    public const float FooterHeight = 54f;
    public const float ColumnHeaderHeight = 45f;
    public const float DataRowHeight = 50f;
    public const float RoleRowHeight = 48f;
    public const float RuleBlockHeight = 112f;
    public const float ToggleSize = 28f;
    public const float ToolboxCategoryWidth = ToolboxVisualSpec.CategoryWidth;
    public const float ToolboxHeaderHeight = ToolboxVisualSpec.HeaderHeight;
    public const float ToolboxModuleRowHeight = ToolboxVisualSpec.ModuleRowHeight;
    private static GameObject? activeSelectPopup;
    private static GameObject? activeSelectAnchor;

    public static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var created = AuraUiComponents.CreateRect(name, parent, anchorMin, anchorMax, pivot, sizeDelta);
        created.layer = parent.gameObject.layer;
        return created;
    }

    public static GameObject CreateLayout(string name, Transform parent)
    {
        return CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
    }

    public static Image AddImage(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    public static Image AddDecoratedReplayPanelImage(GameObject go, Color fallbackOrTint)
    {
        var image = ToolboxSurfaceV2.ApplyDecoratedReplay(go);
        image.fillCenter = true;
        image.color = image.sprite != null ? Color.white : fallbackOrTint;
        return image;
    }

    public static Image AddSettingsWindowImage(GameObject go)
    {
        var image = ToolboxSurfaceV2.ApplySettingsWindow(go);
        image.fillCenter = true;
        return image;
    }

    public static Image AddSectionImage(GameObject go)
    {
        var image = ToolboxSurfaceV2.ApplySection(go);
        image.fillCenter = true;
        return image;
    }

    public static Image AddListRowImage(GameObject go, Color color)
    {
        return ToolboxSurfaceV2.ApplyRow(go, color);
    }

    public static Image AddButtonImage(GameObject go, Color fallbackTint)
    {
        var image = ToolboxSurfaceV2.ApplyControl(go);
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

    public static HorizontalLayoutGroup ConfigureHorizontalLayout(
        GameObject root,
        float spacing = 8f,
        RectOffset? padding = null,
        bool expandWidth = false,
        bool expandHeight = false,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var layout = root.GetComponent<HorizontalLayoutGroup>()
                     ?? root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = padding ?? new RectOffset();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = expandWidth;
        layout.childForceExpandHeight = expandHeight;
        layout.childAlignment = alignment;
        return layout;
    }

    public static GameObject CreateSettingsRow(
        Transform parent,
        string name,
        string stableId = "",
        float height = InlineRowHeight,
        float spacing = 8f,
        RectOffset? padding = null)
    {
        var row = CreateLayout(name, parent);
        if (!string.IsNullOrWhiteSpace(stableId))
        {
            AuraUiStableId.Assign(row, stableId);
        }
        SetFixedHeight(row, height);
        ConfigureHorizontalLayout(
            row,
            spacing,
            padding,
            expandWidth: false,
            expandHeight: false,
            alignment: TextAnchor.MiddleLeft);
        return row;
    }

    public static bool SetActiveIfChanged(GameObject value, bool active)
    {
        if (value.activeSelf == active)
        {
            return false;
        }

        value.SetActive(active);
        return true;
    }

    public static void SetFoldoutExpanded(GameObject content, bool expanded, Transform? layoutRoot = null)
    {
        SetActiveIfChanged(content, expanded);

        if (content.transform.parent is RectTransform parentRect)
        {
            LayoutRebuilder.MarkLayoutForRebuild(parentRect);
        }

        if (layoutRoot is RectTransform layoutRect
            && !ReferenceEquals(layoutRect, content.transform.parent))
        {
            LayoutRebuilder.MarkLayoutForRebuild(layoutRect);
        }
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

    public static TextMeshProUGUI AddTmpText(
        Transform parent,
        string value,
        float fontSize,
        TextAnchor anchor,
        Color color,
        float preferredHeight = TextMinHeight,
        float flexibleWidth = 0f,
        float preferredWidth = 0f,
        bool autoSize = false)
    {
        var go = CreateLayout("Text", parent);
        var element = EnsureLayoutElement(go);
        var height = Mathf.Max(preferredHeight, 1f);
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
            element.flexibleWidth = 0f;
        }

        return AuraUiComponents.ConfigureTmpText(
            go,
            value,
            fontSize,
            Theme.Typography.MinimumSize,
            anchor,
            color,
            autoSize,
            Theme);
    }

    public static TextMeshProUGUI AddTmpFillText(
        Transform parent,
        string value,
        float fontSize,
        TextAnchor anchor,
        Color color,
        bool autoSize = false)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return AuraUiComponents.ConfigureTmpText(
            go,
            value,
            fontSize,
            Theme.Typography.MinimumSize,
            anchor,
            color,
            autoSize,
            Theme);
    }

    public static TMP_InputField AddTmpInput(
        Transform parent,
        string value,
        string placeholderValue,
        Action<string> changed,
        float width = 190f,
        float height = 42f)
    {
        var root = CreateLayout("Input", parent);
        SetFixedSize(root, Mathf.Max(width, 96f), Mathf.Max(height, 36f));
        var background = ToolboxSurfaceV2.ApplyControl(root);
        background.raycastTarget = true;

        var viewport = CreateRect(
            "Viewport",
            root.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.offsetMin = new Vector2(12f, 3f);
        viewportRect.offsetMax = new Vector2(-12f, -3f);
        viewport.AddComponent<RectMask2D>();

        var textObject = CreateRect(
            "Text",
            viewport.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var text = AuraUiComponents.ConfigureTmpText(
            textObject,
            value ?? "",
            Theme.Typography.BodySize,
            Theme.Typography.MinimumSize,
            TextAnchor.MiddleLeft,
            Text,
            false,
            Theme);
        text.raycastTarget = true;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Masking;

        var placeholderObject = CreateRect(
            "Placeholder",
            viewport.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var placeholder = AuraUiComponents.ConfigureTmpText(
            placeholderObject,
            placeholderValue,
            Theme.Typography.HintSize,
            Theme.Typography.MinimumSize,
            TextAnchor.MiddleLeft,
            MutedText,
            false,
            Theme);

        var input = root.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewportRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.text = value ?? "";
        input.onValueChanged.AddListener(v => changed(v));
        return input;
    }

    public static Button AddButton(Transform parent, string label, Action action, float width = 108f, float height = ButtonHeight)
    {
        return ToolboxTextButtonV2.Create(
            parent,
            label,
            action,
            Mathf.Max(width, 64f),
            Mathf.Max(height, CompactButtonHeight));
    }

    public static void SetButtonState(
        Button? button,
        ToolboxTextButtonV2.ActionState state,
        string reason = "")
    {
        if (button == null) return;
        var view = button.GetComponent<ToolboxTextButtonV2>();
        if (view != null)
        {
            view.SetActionState(state, reason);
            return;
        }
        button.interactable = state == ToolboxTextButtonV2.ActionState.Ready;
    }

    public static void SetButtonAvailable(Button? button, bool available, string reason = "")
    {
        SetButtonState(
            button,
            available
                ? ToolboxTextButtonV2.ActionState.Ready
                : ToolboxTextButtonV2.ActionState.Unavailable,
            reason);
    }

    public static void SetButtonLabel(Button? button, string label)
    {
        var text = button == null ? null : button.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label ?? "";
        }
        var tmp = button == null ? null : button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.text = label ?? "";
        }
    }

    public static Toggle AddToggle(Transform parent, bool value, Action<bool> changed, float size = ToggleSize)
    {
        var view = ToolboxCheckboxV2.Create(parent, value, changed, size);
        var toggle = view.Toggle;
        var parentId = parent.GetComponent<AuraUiStableId>()?.Value;
        AuraUiStableId.Assign(
            view.Root,
            string.IsNullOrWhiteSpace(parentId)
                ? "toggle." + parent.name + "." + view.Root.transform.GetSiblingIndex()
                : parentId + ".toggle." + view.Root.transform.GetSiblingIndex());
        return toggle;
    }

    public static InputField AddInput(
        Transform parent,
        string value,
        Action<string> changed,
        float width = 180f,
        float height = ButtonHeight,
        bool flexibleWidth = false)
    {
        var root = CreateLayout("Input", parent);
        var element = EnsureLayoutElement(root);
        var resolvedWidth = Mathf.Max(width, 80f);
        var resolvedHeight = Mathf.Max(height, CompactButtonHeight);
        element.minWidth = flexibleWidth ? Mathf.Min(160f, resolvedWidth) : resolvedWidth;
        element.preferredWidth = resolvedWidth;
        element.minHeight = resolvedHeight;
        element.preferredHeight = resolvedHeight;
        element.flexibleWidth = flexibleWidth ? 1f : 0f;
        element.flexibleHeight = 0f;
        ToolboxSurfaceV2.ApplyControl(root);

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
        var resolvedHeight = Mathf.Max(height, CompactButtonHeight);
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
        AuraUiButtonFeedback.Apply(button, image, Accent);
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
        AuraUiStableId.Assign(root, "scroll." + name);
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
        AuraUiStableId.Assign(content, "scroll." + name + ".content");
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

    public static Transform CreateFixedScroll(
        Transform parent,
        string name,
        float height = 520f)
    {
        var content = CreateScroll(parent, name);
        var root = content.parent?.parent?.gameObject;
        if (root != null)
        {
            SetFixedHeight(root, Mathf.Max(180f, height));
        }

        return content;
    }

    public static GameObject CreateOverlay(string name, Transform parent, string title, Action? onClose = null, bool singleInstance = true, float maxWidth = 1180f)
    {
        var returnFocus = EventSystem.current?.currentSelectedGameObject;
        var overlayRoot = ResolveOverlayRoot(parent);
        if (singleInstance)
        {
            CloseOverlay(overlayRoot, name, "AuraTools overlay single instance");
        }

        var overlay = CreateRect(name, overlayRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        overlay.AddComponent<AuraToolsOwnedOverlay>();
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
        AddSettingsWindowImage(window);
        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CreateLayout("Header", window.transform);
        SetFixedHeight(header, OverlayTitleHeight);
        ToolboxSurfaceV2.ApplyControl(header);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(12, 8, 4, 4);
        headerLayout.spacing = 8f;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        var titleText = AddTmpText(
            header.transform,
            title,
            Theme.Typography.SectionSize,
            TextAnchor.MiddleLeft,
            Accent,
            TextMinHeight,
            1f,
            autoSize: true);
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        ToolboxIconButtonV2.Create(header.transform, "action.clear", "关闭", () =>
        {
            CloseSelectPopup();
            onClose?.Invoke();
            UiRaycastSafeDestroyRuntime.DisableAndHide(overlay, "AuraTools overlay close");
            Object.Destroy(overlay);
            AuraSharedFrameScheduler.StartCoroutine(
                "AuraTools.Overlay.RestoreFocus",
                RestoreFocusNextFrame(returnFocus));
        }, 42f, "×");

        return window;
    }

    public static void ShowConfirmation(
        Transform parent,
        string name,
        string title,
        string message,
        string confirmLabel,
        Action confirmed)
    {
        var window = CreateOverlay(
            name,
            parent,
            title,
            singleInstance: true,
            maxWidth: 620f);
        var overlay = window.transform.parent?.gameObject;
        var messageText = AddText(
            window.transform,
            message,
            BodyFontSize,
            TextAnchor.MiddleLeft,
            Text,
            132f,
            1f);
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        messageText.verticalOverflow = VerticalWrapMode.Overflow;

        var actions = CreateSettingsRow(
            window.transform,
            "ConfirmationActions",
            "confirmation." + name + ".actions",
            FooterHeight);
        AddText(
            actions.transform,
            "",
            HintFontSize,
            TextAnchor.MiddleLeft,
            MutedText,
            TextMinHeight,
            1f);

        void Close()
        {
            CloseSelectPopup();
            if (overlay == null)
            {
                return;
            }
            UiRaycastSafeDestroyRuntime.DisableAndHide(
                overlay,
                "AuraTools confirmation close");
            Object.Destroy(overlay);
        }

        AddButton(actions.transform, "取消", Close, 88f, StandardButtonHeight);
        AddButton(actions.transform, confirmLabel, () =>
        {
            confirmed();
            Close();
        }, 156f, StandardButtonHeight);
    }

    private static IEnumerator RestoreFocusNextFrame(GameObject? target)
    {
        yield return null;
        if (target != null && target.activeInHierarchy)
        {
            EventSystem.current?.SetSelectedGameObject(target);
        }
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

    public static void CloseOwnedOverlays(string source = "AuraTools overlay owner close")
    {
        CloseSelectPopup();
        foreach (var marker in Object.FindObjectsByType<AuraToolsOwnedOverlay>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (marker == null || marker.gameObject == null)
            {
                continue;
            }

            UiRaycastSafeDestroyRuntime.DisableAndHide(marker.gameObject, source);
            Object.Destroy(marker.gameObject);
        }
    }

    internal static void ShowSelectPopup(GameObject anchor, IReadOnlyList<string> labels, int selectedIndex, Action<int> selected, float rowHeight)
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
        AddSectionImage(popup);

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
            AuraUiButtonFeedback.Apply(button, image, Accent);
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

    private static Transform ResolveOverlayRoot(Transform parent)
    {
        var current = parent;
        while (current != null)
        {
            if (current is RectTransform
                && (string.Equals(current.name, "Window", StringComparison.Ordinal)
                    || current.GetComponent<Canvas>() != null))
            {
                return current;
            }
            if (current.parent == null || current.parent is not RectTransform) break;
            current = current.parent;
        }
        return parent;
    }

}

internal sealed class AuraToolsOwnedOverlay : MonoBehaviour
{
}
