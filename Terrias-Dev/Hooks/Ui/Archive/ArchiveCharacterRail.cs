using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui.Archive;

public sealed class ArchiveCharacterRail : MonoBehaviour
{
    private readonly List<ArchiveCharacterRailItem> items = new();
    private RectTransform? content;
    private Action<string>? onSelected;

    public static ArchiveCharacterRail Create(Transform parent, Action<string> onSelected)
    {
        var root = ArchiveUiFactory.CreateFromRect("CharacterRail", parent, ArchiveLayoutMetrics.CharacterRail);
        var viewport = ArchiveUiFactory.CreateFill("Viewport", root, Vector4.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = TerriasUiBuilder.CreateRect(
            "Characters",
            viewport,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, 96f));
        var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        var view = root.gameObject.AddComponent<ArchiveCharacterRail>();
        view.content = content;
        view.onSelected = onSelected;
        return view;
    }

    public void Bind(IReadOnlyList<WitchArchiveDisplayEntry> entries)
    {
        if (content == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        items.Clear();
        foreach (var entry in entries ?? Array.Empty<WitchArchiveDisplayEntry>())
        {
            var item = ArchiveCharacterRailItem.Create(content, entry, () => onSelected?.Invoke(entry.Id));
            items.Add(item);
        }
    }

    public void SetSelected(string id)
    {
        foreach (var item in items.Where(item => item != null))
        {
            item.SetSelected(string.Equals(item.EntryId, id, StringComparison.Ordinal));
        }
    }
}

public sealed class ArchiveCharacterRailItem : MonoBehaviour
{
    private GameObject? selectedSurface;
    private Image? avatar;
    private Text? fallback;
    private GameObject? underline;

    public string EntryId { get; private set; } = "";

    public static ArchiveCharacterRailItem Create(
        Transform parent,
        WitchArchiveDisplayEntry entry,
        Action onClick)
    {
        var root = new GameObject("Character-" + entry.Id, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);
        var element = root.GetComponent<LayoutElement>();
        element.minWidth = 88f;
        element.preferredWidth = 88f;
        element.minHeight = 88f;
        element.preferredHeight = 88f;
        var background = root.GetComponent<Image>();
        background.color = ArchiveUiTheme.Control;
        background.raycastTarget = true;
        var button = root.GetComponent<Button>();
        button.targetGraphic = background;
        AuraUiButtonFeedback.Apply(button, background, ArchiveUiTheme.Accent);
        button.onClick.AddListener(() => onClick());

        var selectedSurface = ArchiveUiFactory.CreateTopLeft("SelectedSurface", root.transform, 2f, 2f, 84f, 78f).gameObject;
        ArchiveUiFactory.ApplyPanel(selectedSurface, ArchiveUiTheme.ControlSelected, false);
        var avatarRect = ArchiveUiFactory.CreateTopLeft("Avatar", root.transform, 8f, 4f, 72f, 72f);
        var avatar = avatarRect.gameObject.AddComponent<Image>();
        avatar.color = Color.white;
        avatar.preserveAspect = true;
        avatar.raycastTarget = false;
        var fallback = ArchiveUiFactory.CreateText(
            "Fallback",
            avatarRect,
            string.IsNullOrWhiteSpace(entry.Name) ? "?" : entry.Name.Substring(0, 1),
            28,
            TextAnchor.MiddleCenter,
            ArchiveUiTheme.TextPrimary,
            true);
        var underline = ArchiveUiFactory.CreateTopLeft("Selected", root.transform, 16f, 84f, 56f, 4f).gameObject;
        ArchiveUiFactory.ApplyPanel(underline, ArchiveUiTheme.Accent, false);

        var sprite = TerriasResourceCache.Load<Sprite>(
            entry.AvatarPath,
            true,
            TerriasIds.WitchArchiveResourceCategory);
        avatar.sprite = sprite;
        avatar.enabled = sprite != null;
        fallback.gameObject.SetActive(sprite == null);

        var view = root.AddComponent<ArchiveCharacterRailItem>();
        view.EntryId = entry.Id;
        view.selectedSurface = selectedSurface;
        view.avatar = avatar;
        view.fallback = fallback;
        view.underline = underline;
        view.SetSelected(false);
        return view;
    }

    public void SetSelected(bool selected)
    {
        selectedSurface?.SetActive(selected);
        if (avatar != null)
        {
            avatar.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.72f);
        }

        if (fallback != null)
        {
            fallback.color = selected ? ArchiveUiTheme.TextPrimary : ArchiveUiTheme.TextTertiary;
        }

        underline?.SetActive(selected);
    }
}
