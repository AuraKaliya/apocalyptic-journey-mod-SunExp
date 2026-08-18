using System;
using AuraUi.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class ToolboxSurfaceV2
{
    internal static Image Apply(GameObject root)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.sprite = AuraToolsToolboxAssets.Surface;
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        image.color = image.sprite == null ? ToolboxVisualSpec.Workspace : Color.white;
        return image;
    }

    internal static Image ApplyControl(GameObject root)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.sprite = AuraToolsToolboxAssets.Control;
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        image.color = image.sprite == null ? ToolboxVisualSpec.Control : Color.white;
        return image;
    }
}

internal sealed class ToolboxIconButtonV2 : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    ISelectHandler,
    IDeselectHandler
{
    private Button? button;
    private Image? background;
    private bool hovered;
    private bool pressed;
    private bool focused;

    internal static ToolboxIconButtonV2 Create(
        Transform parent,
        string iconKey,
        string tooltip,
        Action action,
        float size = ToolboxVisualSpec.IconButtonSize,
        string fallbackLabel = "")
    {
        var root = AuraToolsUi.CreateLayout("ToolboxIconButton-" + iconKey, parent);
        AuraToolsUi.SetFixedSize(root, size, size);
        var image = root.AddComponent<Image>();
        var button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => action());
        var relay = root.AddComponent<AuraUiButtonSoundRelay>();
        relay.Configure(button, AuraUiButtonSoundStyle.Pure);

        var iconRoot = AuraToolsUi.CreateRect(
            "Icon",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(size * 0.48f, size * 0.48f));
        var sprite = AuraToolsIconRegistry.Resolve(iconKey);
        if (sprite != null)
        {
            var icon = AuraToolsUi.AddImage(iconRoot, ToolboxVisualSpec.Text);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }
        else
        {
            AuraToolsUi.AddTmpFillText(
                iconRoot.transform,
                string.IsNullOrWhiteSpace(fallbackLabel) ? "?" : fallbackLabel,
                ToolboxVisualSpec.StatusSize,
                TextAnchor.MiddleCenter,
                ToolboxVisualSpec.Text,
                true);
        }
        if (!string.IsNullOrWhiteSpace(tooltip))
        {
            ToolboxTooltipTrigger.Attach(root, tooltip);
        }

        var view = root.AddComponent<ToolboxIconButtonV2>();
        view.button = button;
        view.background = image;
        view.Refresh();
        return view;
    }

    internal GameObject Root => gameObject;

    internal Button Button => button!;

    internal bool Interactable
    {
        get => button != null && button.interactable;
        set
        {
            if (button != null) button.interactable = value;
            Refresh();
        }
    }

    internal void SetVisible(bool visible)
    {
        Root.SetActive(visible);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
        Refresh();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = eventData.button == PointerEventData.InputButton.Left;
        Refresh();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
        Refresh();
    }

    public void OnSelect(BaseEventData eventData)
    {
        focused = true;
        Refresh();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        focused = false;
        Refresh();
    }

    private void OnDisable()
    {
        hovered = false;
        pressed = false;
        focused = false;
    }

    private void Refresh()
    {
        if (background == null || button == null)
        {
            return;
        }
        var state = !button.interactable
            ? ToolboxIconButtonVisualState.Disabled
            : pressed
                ? ToolboxIconButtonVisualState.Pressed
                : hovered || focused
                    ? ToolboxIconButtonVisualState.Hover
                    : ToolboxIconButtonVisualState.Normal;
        background.sprite = AuraToolsToolboxAssets.IconButton(state);
        background.type = Image.Type.Simple;
        background.color = background.sprite == null ? ToolboxVisualSpec.Control : Color.white;
    }
}

internal sealed class ToolboxCheckboxV2 : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    ISelectHandler,
    IDeselectHandler
{
    private Toggle? toggle;
    private Image? image;
    private bool hovered;
    private bool focused;

    internal static ToolboxCheckboxV2 Create(
        Transform parent,
        bool value,
        Action<bool> changed,
        float size = ToolboxVisualSpec.CheckboxSize)
    {
        var root = AuraToolsUi.CreateLayout("ToolboxCheckbox", parent);
        AuraToolsUi.SetFixedSize(root, size, size);
        var image = root.AddComponent<Image>();
        var toggle = root.AddComponent<Toggle>();
        toggle.targetGraphic = image;
        toggle.transition = Selectable.Transition.None;
        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener(enabled => changed(enabled));
        var view = root.AddComponent<ToolboxCheckboxV2>();
        view.toggle = toggle;
        view.image = image;
        toggle.onValueChanged.AddListener(_ => view.Refresh());
        view.Refresh();
        return view;
    }

    internal GameObject Root => gameObject;

    internal void SetValueWithoutNotify(bool value)
    {
        toggle?.SetIsOnWithoutNotify(value);
        Refresh();
    }

    internal void SetInteractable(bool value)
    {
        if (toggle != null) toggle.interactable = value;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        Refresh();
    }

    public void OnSelect(BaseEventData eventData)
    {
        focused = true;
        Refresh();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        focused = false;
        Refresh();
    }

    private void Refresh()
    {
        if (toggle == null || image == null)
        {
            return;
        }
        var state = !toggle.interactable
            ? ToolboxCheckboxVisualState.Disabled
            : hovered || focused
                ? toggle.isOn
                    ? ToolboxCheckboxVisualState.HoverOn
                    : ToolboxCheckboxVisualState.HoverOff
                : toggle.isOn
                    ? ToolboxCheckboxVisualState.On
                    : ToolboxCheckboxVisualState.Off;
        image.sprite = AuraToolsToolboxAssets.Checkbox(state);
        image.type = Image.Type.Simple;
        image.color = image.sprite == null ? ToolboxVisualSpec.Control : Color.white;
    }
}

internal static class ToolboxSearchFieldV2
{
    internal static TMP_InputField Create(
        Transform parent,
        string value,
        Action<string> changed,
        float width = ToolboxVisualSpec.SearchWidth)
    {
        var input = AuraToolsUi.AddTmpInput(
            parent,
            value,
            "搜索工具…",
            changed,
            width,
            44f);
        var root = input.gameObject;
        ToolboxSurfaceV2.ApplyControl(root);
        var viewport = root.transform.Find("Viewport") as RectTransform;
        if (viewport != null)
        {
            viewport.offsetMin = new Vector2(38f, 3f);
            viewport.offsetMax = new Vector2(-10f, -3f);
        }
        var iconRoot = AuraToolsUi.CreateRect(
            "SearchIcon",
            root.transform,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(20f, 20f));
        iconRoot.GetComponent<RectTransform>().anchoredPosition = new Vector2(10f, 0f);
        var icon = AuraToolsUi.AddImage(iconRoot, ToolboxVisualSpec.MutedText);
        icon.sprite = AuraToolsIconRegistry.Resolve("action.search");
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        return input;
    }
}
