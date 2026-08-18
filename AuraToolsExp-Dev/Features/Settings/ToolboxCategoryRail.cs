using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Settings;

internal sealed class ToolboxCategoryDefinition
{
    internal string Id { get; set; } = "";

    internal string Label { get; set; } = "";

    internal string IconKey { get; set; } = "";
}

internal sealed class ToolboxCategoryRail : MonoBehaviour
{
    private readonly Dictionary<string, ToolboxCategoryRailItem> items =
        new(StringComparer.Ordinal);

    internal static ToolboxCategoryRail Create(
        Transform parent,
        IEnumerable<ToolboxCategoryDefinition> categories,
        Action<string> selected)
    {
        var root = AuraToolsUi.CreateLayout("ToolboxCategoryRail", parent);
        var element = AuraToolsUi.EnsureLayoutElement(root);
        element.minWidth = AuraToolsUi.ToolboxCategoryWidth;
        element.preferredWidth = AuraToolsUi.ToolboxCategoryWidth;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 1f;
        ToolboxSurfaceV2.Apply(root);
        var layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 10, 10);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var rail = root.AddComponent<ToolboxCategoryRail>();
        foreach (var category in categories)
        {
            var item = ToolboxCategoryRailItem.Create(
                root.transform,
                category,
                () => selected(category.Id));
            rail.items[category.Id] = item;
        }
        return rail;
    }

    internal void Refresh(
        string selectedId,
        IReadOnlyDictionary<string, int> counts,
        bool showExtensions)
    {
        foreach (var pair in items)
        {
            pair.Value.gameObject.SetActive(
                pair.Key != "extensions" || showExtensions);
            pair.Value.Refresh(
                string.Equals(pair.Key, selectedId, StringComparison.Ordinal),
                counts.TryGetValue(pair.Key, out var count) ? count : 0);
        }
    }
}

internal sealed class ToolboxCategoryRailItem : MonoBehaviour
{
    private Image? background;
    private TextMeshProUGUI? label;
    private TextMeshProUGUI? count;
    private Button? button;

    internal static ToolboxCategoryRailItem Create(
        Transform parent,
        ToolboxCategoryDefinition category,
        Action selected)
    {
        var root = AuraToolsUi.CreateLayout("Category-" + category.Id, parent);
        AuraToolsUi.SetFixedHeight(root, ToolboxVisualSpec.CategoryHeight);
        var background = AuraToolsUi.AddImage(root, Color.white);
        var button = root.AddComponent<Button>();
        AuraUi.Shared.AuraUiButtonFeedback.Apply(
            button,
            background,
            AuraToolsUi.Theme);
        button.onClick.AddListener(() => selected());
        var layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 8, 7, 7);
        layout.spacing = 7f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var iconRoot = AuraToolsUi.CreateLayout("Icon", root.transform);
        AuraToolsUi.SetFixedSize(iconRoot, 22f, 22f);
        var icon = AuraToolsUi.AddImage(iconRoot, AuraToolsUi.MutedText);
        icon.sprite = AuraToolsIconRegistry.Resolve(category.IconKey);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = icon.sprite != null;
        if (icon.sprite == null)
        {
            AuraToolsUi.AddTmpFillText(
                iconRoot.transform,
                string.IsNullOrWhiteSpace(category.Label)
                    ? "?"
                    : category.Label.Substring(0, 1),
                ToolboxVisualSpec.StatusSize,
                TextAnchor.MiddleCenter,
                AuraToolsUi.MutedText,
                true);
        }

        var label = AuraToolsUi.AddTmpText(
            root.transform,
            category.Label,
            ToolboxVisualSpec.CategorySize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            32f,
            1f,
            autoSize: true);
        label.textWrappingMode = TextWrappingModes.NoWrap;

        var count = AuraToolsUi.AddTmpText(
            root.transform,
            "0",
            ToolboxVisualSpec.CountSize,
            TextAnchor.MiddleRight,
            AuraToolsUi.MutedText,
            32f,
            0f,
            24f,
            true);

        var view = root.AddComponent<ToolboxCategoryRailItem>();
        view.background = background;
        view.label = label;
        view.count = count;
        view.button = button;
        view.Refresh(false, 0);
        return view;
    }

    internal void Refresh(bool selected, int itemCount)
    {
        if (label != null)
        {
            label.color = selected ? AuraToolsUi.Text : AuraToolsUi.MutedText;
        }
        if (count != null)
        {
            count.text = Math.Max(0, itemCount).ToString();
            count.color = selected ? AuraToolsUi.Accent : AuraToolsUi.MutedText;
        }
        if (button != null && background != null)
        {
            background.sprite = selected ? AuraToolsToolboxAssets.CategorySelected : null;
            background.type = background.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            var normal = selected && background.sprite != null
                ? Color.white
                : selected
                    ? AuraToolsUi.CategorySelected
                    : Color.clear;
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = selected
                ? normal
                : ToolboxVisualSpec.RowHighlighted;
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Color.Lerp(normal, Color.black, 0.18f);
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.45f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            background.CrossFadeColor(normal, 0f, true, true);
        }
    }
}
