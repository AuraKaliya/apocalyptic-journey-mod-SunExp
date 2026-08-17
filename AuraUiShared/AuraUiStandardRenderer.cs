using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AuraUi.Shared;

public sealed class AuraUiTextHandle
{
    internal AuraUiTextHandle(GameObject root, TextMeshProUGUI label)
    {
        Root = root;
        Label = label;
    }

    public GameObject Root { get; }
    internal TextMeshProUGUI Label { get; }
    public RectTransform RectTransform => (RectTransform)Root.transform;
    public string Text { get => Label.text; set => Label.text = value ?? ""; }
    public Color Color { get => Label.color; set => Label.color = value; }
}

public sealed class AuraUiButtonHandle
{
    private readonly Button button;
    private readonly TextMeshProUGUI label;

    internal AuraUiButtonHandle(GameObject root, Button button, TextMeshProUGUI label)
    {
        Root = root;
        this.button = button;
        this.label = label;
    }

    public GameObject Root { get; }
    public RectTransform RectTransform => (RectTransform)Root.transform;
    public string Text { get => label.text; set => label.text = value ?? ""; }
    public bool Interactable { get => button.interactable; set => button.interactable = value; }
    public void AddClickListener(UnityAction listener) => button.onClick.AddListener(listener);
    public void RemoveClickListener(UnityAction listener) => button.onClick.RemoveListener(listener);
}

public sealed class AuraUiToggleHandle
{
    private readonly Toggle toggle;
    private readonly TextMeshProUGUI label;

    internal AuraUiToggleHandle(GameObject root, Toggle toggle, TextMeshProUGUI label)
    {
        Root = root;
        this.toggle = toggle;
        this.label = label;
    }

    public GameObject Root { get; }
    public string Text { get => label.text; set => label.text = value ?? ""; }
    public bool Value { get => toggle.isOn; set => toggle.isOn = value; }
    public bool Interactable { get => toggle.interactable; set => toggle.interactable = value; }
    public void SetValueWithoutNotify(bool value) => toggle.SetIsOnWithoutNotify(value);
    public void AddValueChangedListener(UnityAction<bool> listener) => toggle.onValueChanged.AddListener(listener);
    public void RemoveValueChangedListener(UnityAction<bool> listener) => toggle.onValueChanged.RemoveListener(listener);
}

public sealed class AuraUiInputHandle
{
    private readonly TMP_InputField input;

    internal AuraUiInputHandle(GameObject root, TMP_InputField input)
    {
        Root = root;
        this.input = input;
    }

    public GameObject Root { get; }
    public string Text { get => input.text; set => input.text = value ?? ""; }
    public bool Interactable { get => input.interactable; set => input.interactable = value; }
    public void SetTextWithoutNotify(string value) => input.SetTextWithoutNotify(value ?? "");
    public void AddValueChangedListener(UnityAction<string> listener) => input.onValueChanged.AddListener(listener);
    public void RemoveValueChangedListener(UnityAction<string> listener) => input.onValueChanged.RemoveListener(listener);
}

public sealed class AuraUiDropdownHandle
{
    private readonly TMP_Dropdown dropdown;

    internal AuraUiDropdownHandle(GameObject root, TMP_Dropdown dropdown)
    {
        Root = root;
        this.dropdown = dropdown;
    }

    public GameObject Root { get; }
    public int Value { get => dropdown.value; set => dropdown.value = value; }
    public bool Interactable { get => dropdown.interactable; set => dropdown.interactable = value; }
    public void SetValueWithoutNotify(int value) => dropdown.SetValueWithoutNotify(value);
    public void AddValueChangedListener(UnityAction<int> listener) => dropdown.onValueChanged.AddListener(listener);
    public void RemoveValueChangedListener(UnityAction<int> listener) => dropdown.onValueChanged.RemoveListener(listener);
    public void SetOptions(IEnumerable<string> options)
    {
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options ?? Array.Empty<string>()));
    }
}

public sealed class AuraUiScrollHandle
{
    internal AuraUiScrollHandle(GameObject root, ScrollRect scroll, RectTransform viewport, RectTransform content)
    {
        Root = root;
        Scroll = scroll;
        Viewport = viewport;
        Content = content;
    }

    public GameObject Root { get; }
    public RectTransform Viewport { get; }
    public RectTransform Content { get; }
    public float VerticalNormalizedPosition { get => Scroll.verticalNormalizedPosition; set => Scroll.verticalNormalizedPosition = value; }
    private ScrollRect Scroll { get; }
    public void StopMovement() => Scroll.StopMovement();
    public AuraUiViewStateSnapshot CaptureViewState() => AuraUiViewState.Capture(Scroll);
    public void RestoreViewStateAfterLayout(AuraUiViewStateSnapshot snapshot, string source = "AuraUi.Scroll") =>
        AuraUiViewState.RestoreAfterLayout(Content, snapshot, source);
}

public sealed class AuraUiContext
{
    public AuraUiContext(Transform parent, string? styleKey = null)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Theme = AuraUiStyleRegistry.Resolve(styleKey ?? AuraUiStyleIds.WitchNative);
    }

    public Transform Parent { get; }
    public AuraUiTheme Theme { get; }

    public AuraUiContext For(Transform parent)
    {
        return new AuraUiContext(parent, Theme.Key);
    }

    public GameObject CreatePanel(string name, Vector2 size, Color? tint = null, Sprite? sprite = null)
    {
        var root = AuraUiComponents.CreateRect(
            name,
            Parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            size);
        var image = root.AddComponent<Image>();
        image.color = tint ?? Theme.Panel;
        image.sprite = sprite ?? Theme.PanelSprite;
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        return root;
    }

    public AuraUiTextHandle CreateText(
        string name,
        string value,
        AuraUiTextRole role = AuraUiTextRole.Body,
        TextAnchor anchor = TextAnchor.MiddleLeft,
        Color? color = null,
        bool autoSize = false,
        float? fontSize = null,
        float? minimumFontSize = null)
    {
        var root = AuraUiComponents.CreateLayout(name, Parent);
        var text = AuraUiComponents.ConfigureTmpText(
            root,
            value,
            fontSize ?? Theme.Typography.For(role),
            minimumFontSize ?? Theme.Typography.MinimumSize,
            anchor,
            color ?? (role == AuraUiTextRole.Hint ? Theme.MutedText : Theme.Text),
            autoSize,
            Theme);
        return new AuraUiTextHandle(root, text);
    }

    public AuraUiButtonHandle CreateButton(string name, string label, UnityAction? onClick = null)
    {
        var root = AuraUiComponents.CreateRect(
            name,
            Parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(140f, Theme.Metrics.ButtonHeight));
        var image = root.AddComponent<Image>();
        image.color = Theme.Control;
        image.sprite = Theme.ControlSprite;
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;

        var button = root.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, Theme);
        if (onClick != null)
        {
            button.onClick.AddListener(onClick);
        }

        var labelRoot = AuraUiComponents.CreateLayout("Label", root.transform);
        var text = AuraUiComponents.ConfigureTmpText(
            labelRoot,
            label,
            Theme.Typography.ButtonSize,
            Theme.Typography.MinimumSize,
            TextAnchor.MiddleCenter,
            Theme.Text,
            true,
            Theme);
        return new AuraUiButtonHandle(root, button, text);
    }

    public AuraUiToggleHandle CreateToggle(string name, string label, bool value, UnityAction<bool>? onChanged = null)
    {
        var root = AuraUiComponents.CreateLayout(name, Parent);
        var toggle = root.AddComponent<Toggle>();
        var background = AuraUiComponents.CreateRect(
            "Background",
            root.transform,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(28f, 28f));
        var backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = Theme.Control;
        var checkmark = AuraUiComponents.CreateLayout("Checkmark", background.transform);
        var checkmarkImage = checkmark.AddComponent<Image>();
        checkmarkImage.color = Theme.Accent;
        ((RectTransform)checkmark.transform).offsetMin = new Vector2(5f, 5f);
        ((RectTransform)checkmark.transform).offsetMax = new Vector2(-5f, -5f);
        toggle.targetGraphic = backgroundImage;
        toggle.graphic = checkmarkImage;
        toggle.isOn = value;
        if (onChanged != null)
        {
            toggle.onValueChanged.AddListener(onChanged);
        }

        var labelRoot = AuraUiComponents.CreateLayout("Label", root.transform);
        var labelRect = (RectTransform)labelRoot.transform;
        labelRect.offsetMin = new Vector2(38f, 0f);
        var text = AuraUiComponents.ConfigureTmpText(labelRoot, label, Theme.Typography.BodySize, Theme.Typography.MinimumSize, TextAnchor.MiddleLeft, Theme.Text, false, Theme);
        return new AuraUiToggleHandle(root, toggle, text);
    }

    public AuraUiInputHandle CreateInput(string name, string value, string placeholder)
    {
        var root = AuraUiComponents.CreateRect(
            name,
            Parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(240f, Theme.Metrics.InputHeight));
        var image = root.AddComponent<Image>();
        image.color = Theme.Control;
        var input = root.AddComponent<TMP_InputField>();
        input.targetGraphic = image;

        var textRoot = AuraUiComponents.CreateLayout("Text", root.transform);
        var textRect = (RectTransform)textRoot.transform;
        textRect.offsetMin = new Vector2(10f, 4f);
        textRect.offsetMax = new Vector2(-10f, -4f);
        var text = AuraUiComponents.ConfigureTmpText(textRoot, value, Theme.Typography.BodySize, Theme.Typography.MinimumSize, TextAnchor.MiddleLeft, Theme.Text, false, Theme);
        text.raycastTarget = true;
        input.textComponent = text;
        input.textViewport = textRect;

        var placeholderRoot = AuraUiComponents.CreateLayout("Placeholder", root.transform);
        var placeholderRect = (RectTransform)placeholderRoot.transform;
        placeholderRect.offsetMin = textRect.offsetMin;
        placeholderRect.offsetMax = textRect.offsetMax;
        var placeholderText = AuraUiComponents.ConfigureTmpText(placeholderRoot, placeholder, Theme.Typography.HintSize, Theme.Typography.MinimumSize, TextAnchor.MiddleLeft, Theme.MutedText, false, Theme);
        input.placeholder = placeholderText;
        input.text = value;
        return new AuraUiInputHandle(root, input);
    }

    public AuraUiDropdownHandle CreateDropdown(
        string name,
        IEnumerable<string> options,
        int value = 0,
        UnityAction<int>? onChanged = null)
    {
        var root = AuraUiComponents.CreateRect(
            name,
            Parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(240f, Theme.Metrics.InputHeight));
        var image = root.AddComponent<Image>();
        image.color = Theme.Control;
        var dropdown = root.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = image;

        var captionRoot = AuraUiComponents.CreateLayout("Caption", root.transform);
        var captionRect = (RectTransform)captionRoot.transform;
        captionRect.offsetMin = new Vector2(10f, 4f);
        captionRect.offsetMax = new Vector2(-34f, -4f);
        var caption = AuraUiComponents.ConfigureTmpText(captionRoot, "", Theme.Typography.BodySize, Theme.Typography.MinimumSize, TextAnchor.MiddleLeft, Theme.Text, false, Theme);
        dropdown.captionText = caption;

        var arrowRoot = AuraUiComponents.CreateRect("Arrow", root.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(28f, 28f));
        ((RectTransform)arrowRoot.transform).anchoredPosition = new Vector2(-7f, 0f);
        AuraUiComponents.ConfigureTmpText(arrowRoot, "▼", Theme.Typography.HintSize, Theme.Typography.MinimumSize, TextAnchor.MiddleCenter, Theme.Accent, false, Theme);

        var templateRoot = AuraUiComponents.CreateRect("Template", root.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 180f));
        var templateRect = (RectTransform)templateRoot.transform;
        templateRect.anchoredPosition = new Vector2(0f, -2f);
        var templateImage = templateRoot.AddComponent<Image>();
        templateImage.color = Theme.Panel;
        var scroll = templateRoot.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewportRoot = AuraUiComponents.CreateLayout("Viewport", templateRoot.transform);
        var viewportRect = (RectTransform)viewportRoot.transform;
        viewportRoot.AddComponent<RectMask2D>();
        var viewportImage = viewportRoot.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
        scroll.viewport = viewportRect;

        var contentRoot = AuraUiComponents.CreateLayout("Content", viewportRoot.transform);
        var contentRect = (RectTransform)contentRoot.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        scroll.content = contentRect;

        var itemRoot = AuraUiComponents.CreateRect("Item", contentRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, Theme.Metrics.InputHeight));
        var itemToggle = itemRoot.AddComponent<Toggle>();
        var itemBackground = itemRoot.AddComponent<Image>();
        itemBackground.color = Theme.Control;
        itemToggle.targetGraphic = itemBackground;
        var itemLabelRoot = AuraUiComponents.CreateLayout("Item Label", itemRoot.transform);
        var itemLabelRect = (RectTransform)itemLabelRoot.transform;
        itemLabelRect.offsetMin = new Vector2(10f, 3f);
        itemLabelRect.offsetMax = new Vector2(-10f, -3f);
        var itemLabel = AuraUiComponents.ConfigureTmpText(itemLabelRoot, "Option", Theme.Typography.BodySize, Theme.Typography.MinimumSize, TextAnchor.MiddleLeft, Theme.Text, false, Theme);

        dropdown.template = templateRect;
        dropdown.itemText = itemLabel;
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options ?? Array.Empty<string>()));
        dropdown.value = Mathf.Clamp(value, 0, Math.Max(0, dropdown.options.Count - 1));
        if (onChanged != null)
        {
            dropdown.onValueChanged.AddListener(onChanged);
        }

        templateRoot.SetActive(false);
        return new AuraUiDropdownHandle(root, dropdown);
    }

    public AuraUiScrollHandle CreateScrollArea(string name)
    {
        var root = AuraUiComponents.CreateLayout(name, Parent);
        var viewportObject = AuraUiComponents.CreateLayout("Viewport", root.transform);
        var viewport = (RectTransform)viewportObject.transform;
        var maskImage = viewportObject.AddComponent<Image>();
        maskImage.color = new Color(1f, 1f, 1f, 0.01f);
        viewportObject.AddComponent<RectMask2D>();
        var contentObject = AuraUiComponents.CreateLayout("Content", viewport);
        var content = (RectTransform)contentObject.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        return new AuraUiScrollHandle(root, scroll, viewport, content);
    }

    public GameObject CreateTooltip(string name, Vector2 size)
    {
        var root = CreatePanel(name, size);
        var group = root.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return root;
    }

    public GameObject CreateToast(string name, Vector2 size)
    {
        var root = CreateTooltip(name, size);
        root.transform.SetAsLastSibling();
        return root;
    }

    public GameObject CreateModalRoot(string name, Color? blockerColor = null)
    {
        return AuraUiModalHost.CreateFullscreenRoot(
            name,
            Parent,
            blockerColor ?? new Color(0f, 0f, 0f, 0.72f));
    }
}
