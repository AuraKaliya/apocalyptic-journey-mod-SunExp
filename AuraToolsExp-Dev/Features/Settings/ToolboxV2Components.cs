using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class ToolboxSurfaceV2
{
    // The decorated surface belongs to the toolbox home. Settings windows use
    // a cornerless double border so nested pages do not repeat gold ornaments.
    internal static Image ApplyToolboxHome(GameObject root)
    {
        return ApplyDecorated(root);
    }

    internal static Image ApplyDecoratedReplay(GameObject root)
    {
        return ApplyDecorated(root);
    }

    private static Image ApplyDecorated(GameObject root)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.sprite = AuraToolsToolboxAssets.Surface;
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        image.color = image.sprite == null ? ToolboxVisualSpec.Workspace : Color.white;
        return image;
    }

    internal static Image ApplySettingsWindow(GameObject root)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.sprite = AuraToolsToolboxAssets.Control;
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        image.color = image.sprite == null ? ToolboxVisualSpec.Workspace : Color.white;
        return image;
    }

    internal static Image ApplySection(GameObject root)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.sprite = AuraToolsToolboxAssets.Control;
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        image.color = image.sprite == null ? ToolboxVisualSpec.Control : Color.white;
        return image;
    }

    internal static Image ApplyRow(GameObject root, Color color)
    {
        var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
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
        button.onClick.AddListener(() => AuraToolsUi.RunConfigAction(action));
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

internal sealed class ToolboxTextButtonV2 : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    ISelectHandler,
    IDeselectHandler
{
    internal enum ActionState
    {
        Ready,
        Busy,
        Unavailable
    }

    private Button? button;
    private Image? background;
    private bool hovered;
    private bool pressed;
    private bool focused;
    private bool lastInteractable;
    private bool stateInitialized;
    private ActionState actionState = ActionState.Ready;
    private string unavailableReason = "";

    internal static Button Create(
        Transform parent,
        string label,
        Action action,
        float width,
        float height)
    {
        var root = AuraToolsUi.CreateLayout("ToolboxTextButton-" + label, parent);
        var element = AuraToolsUi.EnsureLayoutElement(root);
        element.minWidth = Mathf.Min(Mathf.Max(64f, width * 0.55f), 112f);
        element.preferredWidth = Mathf.Max(width, 64f);
        element.minHeight = Mathf.Max(34f, height);
        element.preferredHeight = Mathf.Max(34f, height);
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        var image = ToolboxSurfaceV2.ApplyControl(root);
        var button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => AuraToolsUi.RunConfigAction(action));
        var relay = root.AddComponent<AuraUiButtonSoundRelay>();
        relay.Configure(button, AuraUiButtonSoundStyle.Pure);
        AuraToolsUi.AddTmpFillText(
            root.transform,
            label,
            ToolboxVisualSpec.CategorySize,
            TextAnchor.MiddleCenter,
            ToolboxVisualSpec.Text,
            true);
        var view = root.AddComponent<ToolboxTextButtonV2>();
        view.button = button;
        view.background = image;
        view.lastInteractable = button.interactable;
        view.stateInitialized = true;
        view.Refresh();
        return button;
    }

    internal void SetActionState(ActionState state, string reason = "")
    {
        actionState = state;
        unavailableReason = (reason ?? "").Trim();
        if (button != null)
        {
            button.interactable = state == ActionState.Ready;
            lastInteractable = button.interactable;
            stateInitialized = true;
        }
        ToolboxTooltipTrigger.Attach(
            gameObject,
            state == ActionState.Ready
                ? ""
                : unavailableReason.Length > 0
                    ? unavailableReason
                    : state == ActionState.Busy ? "正在处理" : "当前不可用");
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData) { hovered = true; Refresh(); }
    public void OnPointerExit(PointerEventData eventData) { hovered = false; pressed = false; Refresh(); }
    public void OnPointerDown(PointerEventData eventData) { pressed = eventData.button == PointerEventData.InputButton.Left; Refresh(); }
    public void OnPointerUp(PointerEventData eventData) { pressed = false; Refresh(); }
    public void OnSelect(BaseEventData eventData) { focused = true; Refresh(); }
    public void OnDeselect(BaseEventData eventData) { focused = false; Refresh(); }

    private void OnDisable()
    {
        hovered = false;
        pressed = false;
        focused = false;
        Refresh();
    }

    private void Update()
    {
        if (button == null) return;
        if (stateInitialized && button.interactable == lastInteractable) return;
        lastInteractable = button.interactable;
        stateInitialized = true;
        actionState = button.interactable ? ActionState.Ready : ActionState.Unavailable;
        Refresh();
    }

    private void Refresh()
    {
        if (background == null || button == null) return;
        background.color = !button.interactable
            ? actionState == ActionState.Busy
                ? Color.Lerp(ToolboxVisualSpec.Disabled, ToolboxVisualSpec.Accent, 0.14f)
                : new Color(ToolboxVisualSpec.Disabled.r, ToolboxVisualSpec.Disabled.g, ToolboxVisualSpec.Disabled.b, 1f)
            : pressed
                ? ToolboxVisualSpec.RowHighlighted
                : hovered || focused
                    ? Color.Lerp(Color.white, ToolboxVisualSpec.Accent, 0.24f)
                    : Color.white;
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
        var visual = AuraToolsUi.CreateRect(
            "Square",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(size, size));
        var image = visual.AddComponent<Image>();
        var toggle = visual.AddComponent<Toggle>();
        toggle.targetGraphic = image;
        toggle.transition = Selectable.Transition.None;
        toggle.SetIsOnWithoutNotify(value);
        var committedValue = value;
        toggle.onValueChanged.AddListener(enabled =>
        {
            if (AuraToolsUi.RunConfigAction(() => changed(enabled))) committedValue = toggle.isOn;
            else toggle.SetIsOnWithoutNotify(committedValue);
        });
        var view = root.AddComponent<ToolboxCheckboxV2>();
        view.toggle = toggle;
        view.image = image;
        toggle.onValueChanged.AddListener(_ => view.Refresh());
        view.Refresh();
        return view;
    }

    internal GameObject Root => gameObject;

    internal Toggle Toggle => toggle!;

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

internal sealed class ToolboxSearchOption
{
    internal ToolboxSearchOption(string value, string label, string searchText = "")
    {
        Value = (value ?? "").Trim();
        Label = (label ?? "").Trim();
        SearchText = string.IsNullOrWhiteSpace(searchText)
            ? Label
            : searchText.Trim();
    }

    internal string Value { get; }

    internal string Label { get; }

    internal string SearchText { get; }
}

internal sealed class ToolboxSearchPickerV3 : MonoBehaviour
{
    private const int MaximumVisibleCandidates = 12;
    private readonly List<ToolboxSearchOption> options = new();
    private readonly List<ToolboxSearchOption> filtered = new();
    private string selectedValue = "";
    private Action<string>? queryChanged;
    private Action<string>? selectionChanged;
    private TextMeshProUGUI? caption;
    private Button? candidateButton;

    internal static ToolboxSearchPickerV3 Create(
        Transform parent,
        IReadOnlyList<ToolboxSearchOption> options,
        string query,
        string selectedValue,
        Action<string> queryChanged,
        Action<string> selectionChanged,
        float preferredWidth = 500f)
    {
        var root = AuraToolsUi.CreateLayout("ToolboxSearchPicker", parent);
        var element = AuraToolsUi.EnsureLayoutElement(root);
        element.minWidth = 340f;
        element.preferredWidth = Mathf.Max(340f, preferredWidth);
        element.flexibleWidth = 1f;
        element.minHeight = AuraToolsUi.StandardButtonHeight;
        element.preferredHeight = AuraToolsUi.StandardButtonHeight;
        element.flexibleHeight = 0f;
        AuraToolsUi.ConfigureHorizontalLayout(root, 8f);

        var picker = root.AddComponent<ToolboxSearchPickerV3>();
        picker.options.AddRange(options ?? Array.Empty<ToolboxSearchOption>());
        picker.selectedValue = (selectedValue ?? "").Trim();
        picker.queryChanged = queryChanged;
        picker.selectionChanged = selectionChanged;

        var input = AuraToolsUi.AddTmpInput(
            root.transform,
            query ?? "",
            "输入名称搜索…",
            picker.OnQueryChanged,
            240f,
            AuraToolsUi.StandardButtonHeight);
        var inputElement = AuraToolsUi.EnsureLayoutElement(input.gameObject);
        inputElement.minWidth = 120f;
        inputElement.preferredWidth = 240f;
        inputElement.flexibleWidth = 1f;

        var buttonRoot = AuraToolsUi.CreateLayout("Candidates", root.transform);
        AuraToolsUi.SetFixedSize(
            buttonRoot,
            220f,
            AuraToolsUi.StandardButtonHeight);
        var background = AuraToolsUi.AddButtonImage(
            buttonRoot,
            new Color(0.025f, 0.022f, 0.045f, 0.98f));
        var labelRoot = AuraToolsUi.CreateRect(
            "Label",
            buttonRoot.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var labelRect = labelRoot.GetComponent<RectTransform>();
        labelRect.offsetMin = new Vector2(8f, 2f);
        labelRect.offsetMax = new Vector2(-30f, -2f);
        picker.caption = AuraToolsUi.AddTmpFillText(
            labelRoot.transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text);
        var arrowRoot = AuraToolsUi.CreateRect(
            "Arrow",
            buttonRoot.transform,
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0.5f),
            new Vector2(24f, 0f));
        arrowRoot.GetComponent<RectTransform>().anchoredPosition = new Vector2(-8f, 0f);
        AuraToolsUi.AddTmpFillText(
            arrowRoot.transform,
            "v",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter,
            AuraToolsUi.Accent);
        picker.candidateButton = buttonRoot.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(picker.candidateButton, background, AuraToolsUi.Accent);
        picker.candidateButton.onClick.AddListener(picker.ShowCandidates);
        picker.ApplyFilter(query ?? "", notifySelection: true);
        return picker;
    }

    internal string SelectedValue => selectedValue;

    internal int CandidateCount => filtered.Count;

    private void OnQueryChanged(string query)
    {
        queryChanged?.Invoke(query ?? "");
        ApplyFilter(query ?? "", notifySelection: true);
    }

    private void ApplyFilter(string query, bool notifySelection)
    {
        var terms = (query ?? "")
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        filtered.Clear();
        filtered.AddRange(options
            .Where(option => terms.All(term =>
                option.SearchText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                || option.Label.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
            .Take(MaximumVisibleCandidates));

        if (filtered.Count == 1)
        {
            Select(filtered[0], notifySelection);
        }
        else if (selectedValue.Length > 0
                 && filtered.All(option => !string.Equals(
                     option.Value,
                     selectedValue,
                     StringComparison.OrdinalIgnoreCase)))
        {
            selectedValue = "";
            if (notifySelection)
            {
                selectionChanged?.Invoke("");
            }
        }
        RefreshCaption();
    }

    private void ShowCandidates()
    {
        if (candidateButton == null || filtered.Count == 0)
        {
            return;
        }
        var labels = filtered.Select(option => option.Label).ToArray();
        var selectedIndex = Math.Max(0, filtered.FindIndex(option => string.Equals(
            option.Value,
            selectedValue,
            StringComparison.OrdinalIgnoreCase)));
        AuraToolsUi.ShowSelectPopup(
            candidateButton.gameObject,
            labels,
            selectedIndex,
            index =>
            {
                if (index >= 0 && index < filtered.Count)
                {
                    Select(filtered[index], notify: true);
                    RefreshCaption();
                }
            },
            AuraToolsUi.StandardButtonHeight);
    }

    private void Select(ToolboxSearchOption option, bool notify)
    {
        if (string.Equals(
                selectedValue,
                option.Value,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        selectedValue = option.Value;
        if (notify)
        {
            selectionChanged?.Invoke(selectedValue);
        }
    }

    private void RefreshCaption()
    {
        if (caption == null || candidateButton == null)
        {
            return;
        }
        var selected = options.FirstOrDefault(option => string.Equals(
            option.Value,
            selectedValue,
            StringComparison.OrdinalIgnoreCase));
        caption.text = selected?.Label
                       ?? (filtered.Count == 0
                           ? "没有匹配项"
                           : "选择候选（" + filtered.Count + "）");
        candidateButton.interactable = filtered.Count > 0;
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
