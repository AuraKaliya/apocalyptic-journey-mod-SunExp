using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui.Archive;

public static class WitchArchivePanel
{
    private const string RegistryKey = "WitchArchive";
    private const string LogPrefix = "[WitchArchive]";
    private static GameObject? activeRoot;
    private static ArchiveIdentityHeader? identityHeader;
    private static ArchiveCharacterRail? characterRail;
    private static ArchiveSectionTabs? sectionTabs;
    private static ArchivePortraitViewport? portraitViewport;
    private static ArchiveInfoPanel? infoPanel;
    private static IReadOnlyList<WitchArchiveDisplayEntry> entries = Array.Empty<WitchArchiveDisplayEntry>();
    private static string selectedCharacterId = "";
    private static WitchArchiveSection selectedSection = WitchArchiveSection.Basic;

    public static bool IsOpen => activeRoot != null;

    public static bool Open()
    {
        try
        {
            Close("WitchArchive.Open");
            entries = WitchArchiveCatalog.DisplayEntries();
            if (entries.Count == 0)
            {
                TerriasLog.Warn(LogPrefix + " " + WitchArchiveStrings.Empty);
                return false;
            }

            var parent = TerriasModalHost.ModalParent();
            if (parent == null)
            {
                TerriasLog.Warn(LogPrefix + " skipped: UI canvas unavailable.");
                return false;
            }

            var shell = ArchiveWindowShell.Create(parent);
            activeRoot = shell.Root;
            portraitViewport = ArchivePortraitViewport.Create(shell.PortraitLayer);
            identityHeader = ArchiveIdentityHeader.Create(shell.ChromeLayer);
            characterRail = ArchiveCharacterRail.Create(shell.ChromeLayer, SelectCharacter);
            sectionTabs = ArchiveSectionTabs.Create(shell.ChromeLayer, SelectSection);
            infoPanel = ArchiveInfoPanel.Create(shell.ChromeLayer);
            CreateNavigationFooter(shell.ChromeLayer);
            CreateCloseButton(shell.ChromeLayer);
            CreateInputController(shell.Frame);

            characterRail.Bind(entries);
            if (entries.All(entry => !string.Equals(entry.Id, selectedCharacterId, StringComparison.Ordinal)))
            {
                selectedCharacterId = entries[0].Id;
            }

            SelectCharacter(selectedCharacterId);
            SelectSection(selectedSection);
            TerriasTransientUiRegistry.Register(RegistryKey, Close);
            TerriasLog.Info(LogPrefix + " opened; entries=" + entries.Count);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Witch archive panel failed", ex);
            Close("WitchArchive.OpenFailed");
            return false;
        }
    }

    public static void Close(string source)
    {
        TerriasModalHost.Close(ref activeRoot, source, LogPrefix);
        TerriasTransientUiRegistry.Unregister(RegistryKey);
        identityHeader = null;
        characterRail = null;
        sectionTabs = null;
        portraitViewport = null;
        infoPanel = null;
        entries = Array.Empty<WitchArchiveDisplayEntry>();
    }

    private static void SelectCharacter(string id)
    {
        var entry = entries.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (entry == null)
        {
            return;
        }

        selectedCharacterId = entry.Id;
        identityHeader?.Bind(entry);
        portraitViewport?.Bind(entry);
        infoPanel?.Bind(entry);
        characterRail?.SetSelected(entry.Id);
    }

    private static void SelectSection(WitchArchiveSection section)
    {
        selectedSection = section;
        sectionTabs?.SetSelected(section);
        infoPanel?.SetSection(section);
    }

    private static void MoveCharacter(int delta)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var index = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Id, selectedCharacterId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        index = WitchArchiveSelectionPolicy.Move(index, entries.Count, delta);
        SelectCharacter(entries[index].Id);
    }

    private static void MoveSection(int delta)
    {
        var next = selectedSection == WitchArchiveSection.Basic
            ? WitchArchiveSection.Background
            : WitchArchiveSection.Basic;
        SelectSection(next);
    }

    private static void CreateCloseButton(Transform parent)
    {
        var rect = ArchiveUiFactory.CreateFromRect("CloseSlot", parent, ArchiveLayoutMetrics.CloseButton);
        var button = ArchiveUiFactory.CreateButton(
            "Close",
            rect,
            "×",
            ArchiveUiTheme.ControlSelected,
            ArchiveUiTheme.TextPrimary,
            32,
            () => Close("WitchArchive.CloseButton"));
        var buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
    }

    private static void CreateInputController(Transform parent)
    {
        var controller = parent.gameObject.AddComponent<ArchiveInputController>();
        controller.Initialize(
            () => Close("WitchArchive.Escape"),
            MoveCharacter,
            MoveSection);
    }

    private static void CreateNavigationFooter(Transform parent)
    {
        var footer = ArchiveUiFactory.CreateFromRect("NavigationFooter", parent, ArchiveLayoutMetrics.Footer);
        ArchiveUiFactory.ApplyPanel(footer.gameObject, ArchiveUiTheme.TopBar, false);
        var divider = ArchiveUiFactory.CreateTopLeft(
            "Divider",
            footer,
            0f,
            0f,
            ArchiveLayoutMetrics.ReferenceWidth,
            2f);
        ArchiveUiFactory.ApplyPanel(divider.gameObject, ArchiveUiTheme.Divider, false);
        var rect = ArchiveUiFactory.CreateTopLeft("NavigationHint", footer, 32f, 8f, 560f, 48f);
        ArchiveUiFactory.CreateText(
            "Text",
            rect,
            "Q / E  ·  " + WitchArchiveStrings.SwitchCharacter + "    W / S  ·  " + WitchArchiveStrings.SwitchSection,
            16,
            TextAnchor.MiddleLeft,
            ArchiveUiTheme.TextSecondary,
            true);
        var closeRect = ArchiveUiFactory.CreateTopLeft("CloseHint", footer, 1328f, 8f, 240f, 48f);
        ArchiveUiFactory.CreateText(
            "Text",
            closeRect,
            "ESC  ·  " + WitchArchiveStrings.Close,
            16,
            TextAnchor.MiddleRight,
            ArchiveUiTheme.TextSecondary,
            true);
    }
}
