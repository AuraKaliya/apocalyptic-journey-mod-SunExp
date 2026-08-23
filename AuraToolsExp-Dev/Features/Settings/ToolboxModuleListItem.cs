using AuraToolsExp.Dll.Modules;
using AuraToolsExp.Dll.Modules.Contracts;
using AuraUi.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Settings;

internal sealed class ToolboxModuleListItem : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private IAuraToolModule? module;
    private Image? background;
    private Image? statusMarker;
    private Image? icon;
    private TextMeshProUGUI? iconFallback;
    private TextMeshProUGUI? titleText;
    private TextMeshProUGUI? statusText;
    private ToolboxCheckboxV2? checkbox;
    private ToolboxIconButtonV2? settingsButton;
    private bool suppressToggle;
    private bool hovered;
    private bool hasSettings;

    internal void Build(IAuraToolModule value)
    {
        module = value;
        AuraToolsUi.SetFixedHeight(gameObject, AuraToolsUi.ToolboxModuleRowHeight);
        background = AuraToolsUi.AddImage(gameObject, ToolboxVisualSpec.Row);
        var layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var marker = AuraToolsUi.CreateLayout("StatusMarker", transform);
        AuraToolsUi.SetFixedSize(marker, 4f, 52f);
        statusMarker = AuraToolsUi.AddImage(marker, ToolboxVisualSpec.MutedText);
        statusMarker.raycastTarget = false;

        var iconHolder = AuraToolsUi.CreateLayout("ModuleIcon", transform);
        AuraToolsUi.SetFixedSize(iconHolder, ToolboxVisualSpec.ModuleIconSize, ToolboxVisualSpec.ModuleIconSize);
        var iconBackground = ToolboxSurfaceV2.ApplyControl(iconHolder);
        iconBackground.raycastTarget = false;
        var iconRoot = AuraToolsUi.CreateRect(
            "Icon",
            iconHolder.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(28f, 28f));
        icon = AuraToolsUi.AddImage(iconRoot, ToolboxVisualSpec.Text);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        iconFallback = AuraToolsUi.AddTmpFillText(
            iconRoot.transform,
            Initial(value.Descriptor.DisplayName),
            ToolboxVisualSpec.TitleSize,
            TextAnchor.MiddleCenter,
            ToolboxVisualSpec.Text,
            true);

        var copy = AuraToolsUi.CreateLayout("Copy", transform);
        var copyElement = AuraToolsUi.EnsureLayoutElement(copy);
        copyElement.flexibleWidth = 1f;
        copyElement.minWidth = 120f;
        var copyLayout = copy.AddComponent<VerticalLayoutGroup>();
        copyLayout.spacing = 0f;
        copyLayout.childControlWidth = true;
        copyLayout.childControlHeight = true;
        copyLayout.childForceExpandWidth = true;
        copyLayout.childForceExpandHeight = false;

        titleText = AuraToolsUi.AddTmpText(
            copy.transform,
            "",
            ToolboxVisualSpec.TitleSize,
            TextAnchor.MiddleLeft,
            ToolboxVisualSpec.Text,
            27f,
            1f,
            autoSize: true);
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        statusText = AuraToolsUi.AddTmpText(
            copy.transform,
            "",
            ToolboxVisualSpec.StatusSize,
            TextAnchor.MiddleLeft,
            ToolboxVisualSpec.MutedText,
            24f,
            1f,
            autoSize: true);
        statusText.textWrappingMode = TextWrappingModes.NoWrap;

        settingsButton = ToolboxIconButtonV2.Create(
            transform,
            "action.settings",
            "设置 " + value.Descriptor.DisplayName,
            OpenSettings,
            ToolboxVisualSpec.IconButtonSize,
            "设");
        AuraUiStableId.Assign(
            settingsButton.Root,
            "toolbox.module." + value.Descriptor.ModuleId + ".settings");

        var enableRoot = AuraToolsUi.CreateLayout("EnableControl", transform);
        AuraToolsUi.SetFixedSize(enableRoot, 88f, 42f);
        var enableLayout = enableRoot.AddComponent<HorizontalLayoutGroup>();
        enableLayout.spacing = 6f;
        enableLayout.childAlignment = TextAnchor.MiddleCenter;
        enableLayout.childControlWidth = true;
        enableLayout.childControlHeight = true;
        enableLayout.childForceExpandWidth = false;
        enableLayout.childForceExpandHeight = false;
        AuraToolsUi.AddTmpText(
            enableRoot.transform,
            "启用",
            ToolboxVisualSpec.DescriptionSize,
            TextAnchor.MiddleRight,
            ToolboxVisualSpec.MutedText,
            32f,
            0f,
            44f);
        checkbox = ToolboxCheckboxV2.Create(enableRoot.transform, false, ToggleChanged);
        AuraUiStableId.Assign(
            checkbox.Root,
            "toolbox.module." + value.Descriptor.ModuleId + ".toggle");
        enableRoot.SetActive(value.Descriptor.ShowEnableControl);
        Bind(value, AuraToolModuleHost.RefreshState(value.Descriptor.ModuleId));
    }

    internal void Bind(IAuraToolModule value, AuraToolModuleState state)
    {
        module = value;
        if (titleText != null)
        {
            titleText.text = value.Descriptor.DisplayName
                             + (value.Descriptor.Experimental ? "  ·  实验" : "");
        }
        if (settingsButton != null)
        {
            hasSettings = value.Descriptor.HasSettingsPage;
            var group = settingsButton.Root.GetComponent<CanvasGroup>()
                        ?? settingsButton.Root.AddComponent<CanvasGroup>();
            group.alpha = hasSettings ? 1f : 0f;
            group.blocksRaycasts = hasSettings;
            group.interactable = hasSettings;
        }
        var enableRoot = transform.Find("EnableControl");
        if (enableRoot != null)
        {
            enableRoot.gameObject.SetActive(value.Descriptor.ShowEnableControl);
        }

        var resolvedIcon = AuraToolsIconRegistry.Resolve(
            value.Descriptor.IconKey,
            "category." + value.Descriptor.CategoryId);
        if (icon != null)
        {
            icon.sprite = resolvedIcon;
            icon.enabled = resolvedIcon != null;
        }
        if (iconFallback != null)
        {
            iconFallback.text = Initial(value.Descriptor.DisplayName);
            iconFallback.gameObject.SetActive(resolvedIcon == null);
        }
        Refresh(state);
    }

    internal void Refresh(AuraToolModuleState state)
    {
        if (state == null)
        {
            return;
        }

        suppressToggle = true;
        checkbox?.SetValueWithoutNotify(state.ConfiguredEnabled);
        suppressToggle = false;
        checkbox?.SetInteractable(
            state.EnableControlInteractable
            && state.Availability != AuraToolModuleAvailability.Unavailable
            && state.Availability != AuraToolModuleAvailability.Busy);
        if (settingsButton != null)
        {
            settingsButton.Interactable = hasSettings
                                         && state.SettingsControlInteractable
                                         && state.Availability
                                         != AuraToolModuleAvailability.Unavailable;
        }

        var statusColor = ResolveStatusColor(state);
        if (statusText != null)
        {
            statusText.text = AuraToolsPlayerDisplay.ModuleStatus(state);
            statusText.color = statusColor;
            statusText.gameObject.SetActive(statusText.text.Length > 0);
        }
        if (statusMarker != null)
        {
            statusMarker.color = statusColor;
        }
        if (icon != null)
        {
            icon.color = state.Availability == AuraToolModuleAvailability.Unavailable
                ? ToolboxVisualSpec.MutedText
                : state.EffectiveEnabled
                    ? ToolboxVisualSpec.Text
                    : ToolboxVisualSpec.MutedText;
        }
        RefreshBackground();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        RefreshBackground();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        RefreshBackground();
    }

    private void OnDisable()
    {
        hovered = false;
    }

    private void RefreshBackground()
    {
        if (background != null)
        {
            background.color = hovered ? ToolboxVisualSpec.RowHighlighted : ToolboxVisualSpec.Row;
        }
    }

    private static Color ResolveStatusColor(AuraToolModuleState state)
    {
        if (state.Availability == AuraToolModuleAvailability.Unavailable)
        {
            return ToolboxVisualSpec.Error;
        }
        if (!string.IsNullOrWhiteSpace(state.Attention)
            || state.Availability == AuraToolModuleAvailability.Degraded
            || state.Availability == AuraToolModuleAvailability.RestartRequired)
        {
            return ToolboxVisualSpec.Warning;
        }
        return state.EffectiveEnabled
            ? ToolboxVisualSpec.Success
            : ToolboxVisualSpec.Disabled;
    }

    private void ToggleChanged(bool enabled)
    {
        if (suppressToggle || module == null)
        {
            return;
        }

        var result = AuraToolModuleHost.SetEnabled(
            module.Descriptor.ModuleId,
            enabled);
        var state = AuraToolModuleHost.RefreshState(module.Descriptor.ModuleId);
        if (!result.Success)
        {
            state.Attention = result.Message;
        }
        Refresh(state);
    }

    private void OpenSettings()
    {
        if (module != null)
        {
            ToolboxSettingsPageRouter.Open(module, transform);
        }
    }

    private static string Initial(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "?" : value.Trim().Substring(0, 1);
    }
}
