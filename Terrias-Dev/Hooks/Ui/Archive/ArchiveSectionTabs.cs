using System;
using System.Collections.Generic;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui.Archive;

public sealed class ArchiveSectionTabs : MonoBehaviour
{
    private readonly Dictionary<WitchArchiveSection, ArchiveSectionTabItem> buttons = new();
    private Action<WitchArchiveSection>? onSelected;

    public static ArchiveSectionTabs Create(Transform parent, Action<WitchArchiveSection> onSelected)
    {
        var root = ArchiveUiFactory.CreateFromRect("SectionTabs", parent, ArchiveLayoutMetrics.SectionTabs);
        ArchiveUiFactory.ApplyPanel(root.gameObject, ArchiveUiTheme.Panel, true);
        var view = root.gameObject.AddComponent<ArchiveSectionTabs>();
        view.onSelected = onSelected;
        view.AddButton(WitchArchiveSection.Basic, WitchArchiveStrings.Basic, 24f);
        view.AddButton(WitchArchiveSection.Background, WitchArchiveStrings.Background, 88f);
        return view;
    }

    public void SetSelected(WitchArchiveSection section)
    {
        foreach (var pair in buttons)
        {
            pair.Value.SetSelected(pair.Key == section);
        }
    }

    private void AddButton(WitchArchiveSection section, string label, float y)
    {
        var rect = ArchiveUiFactory.CreateTopLeft("Slot-" + section, transform, 16f, y, 208f, 56f);
        var surface = ArchiveUiFactory.ApplyPanel(rect.gameObject, ArchiveUiTheme.Control, true);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = surface;
        AuraUiButtonFeedback.Apply(button, surface, ArchiveUiTheme.Accent);
        button.onClick.AddListener(() => onSelected?.Invoke(section));

        var selectedSurface = ArchiveUiFactory.CreateFill("SelectedSurface", rect, new Vector4(0f, 0f, 0f, 0f)).gameObject;
        ArchiveUiFactory.ApplyPanel(selectedSurface, ArchiveUiTheme.ControlSelected, false);
        var indicator = ArchiveUiFactory.CreateTopLeft("SelectedIndicator", rect, 0f, 8f, 4f, 40f).gameObject;
        ArchiveUiFactory.ApplyPanel(indicator, ArchiveUiTheme.Accent, false);
        var labelRect = ArchiveUiFactory.CreateTopLeft("Label", rect, 24f, 0f, 168f, 56f);
        var text = ArchiveUiFactory.CreateText(
            "Value",
            labelRect,
            label,
            20,
            TextAnchor.MiddleLeft,
            ArchiveUiTheme.TextSecondary,
            true);
        buttons[section] = new ArchiveSectionTabItem(selectedSurface, indicator, text);
    }
}

internal sealed class ArchiveSectionTabItem
{
    private readonly GameObject selectedSurface;
    private readonly GameObject indicator;
    private readonly Text label;

    public ArchiveSectionTabItem(GameObject selectedSurface, GameObject indicator, Text label)
    {
        this.selectedSurface = selectedSurface;
        this.indicator = indicator;
        this.label = label;
    }

    public void SetSelected(bool selected)
    {
        selectedSurface.SetActive(selected);
        indicator.SetActive(selected);
        label.color = selected ? ArchiveUiTheme.TextPrimary : ArchiveUiTheme.TextSecondary;
    }
}
