using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Application;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal static class SpiritArtifactPresetPanel
{
    private static readonly Color Panel = new(0.035f, 0.052f, 0.078f, 0.995f);
    private static readonly Color Item = new(0.052f, 0.073f, 0.105f, 0.98f);
    private static readonly Color Selected = new(0.105f, 0.205f, 0.235f, 0.99f);
    private static readonly Color Pale = new(0.90f, 0.94f, 0.97f);
    private static readonly Color Muted = new(0.62f, 0.70f, 0.77f);
    private static readonly Color Gold = new(0.95f, 0.76f, 0.34f);
    private static readonly Color Danger = new(0.66f, 0.20f, 0.20f, 1f);
    private static readonly Dictionary<string, PresetRowBinding> PresetRows = new(StringComparer.Ordinal);

    private static GameObject? overlay;
    private static GameObject? window;
    private static Transform? listHost;
    private static Transform? detailHost;
    private static ScrollRect? detailScrollRect;
    private static Text? presetCountText;
    private static Button? saveCurrentButton;
    private static Button? createButton;
    private static SpiritArtifactVirtualizedGridView? pickerGrid;
    private static SpiritCollectionDocument collection = new();
    private static HashSet<string> protectedArtifactUids = new(StringComparer.Ordinal);
    private static string currentSpiritUid = "";
    private static string selectedPresetUid = "";
    private static string selectedDraftSlot = SpiritArtifactSlots.Flower;
    private static SpiritArtifactPreset? draft;
    private static InputField? nameInput;
    private static bool editing;
    private static bool editDirty;
    private static bool saveArmed;
    private static bool deleteArmed;
    private static bool discardArmed;
    private static float contentWidth = 680f;
    private static SpiritArtifactPresetLayout layout;
    private static Action? closed;

    private sealed class PresetRowBinding
    {
        public Image Background { get; set; } = null!;

        public Image Accent { get; set; } = null!;
    }

    public static bool IsOpen => overlay != null;

    public static void Open(
        Transform parent,
        float workspaceWidth,
        string spiritUid,
        Action onClosed)
    {
        CloseInternal(invokeCallback: false);
        currentSpiritUid = (spiritUid ?? "").Trim();
        closed = onClosed;
        collection = SpiritCollectionApi.Collection();
        var parentRect = parent as RectTransform;
        var availableHeight = parentRect != null && parentRect.rect.height > 1f ? parentRect.rect.height : 560f;
        layout = SpiritArtifactPresetLayoutPolicy.Calculate(workspaceWidth, availableHeight);
        contentWidth = layout.Width;
        overlay = TerriasModalHost.CreateFullscreenRoot(
            "SpiritArtifactPresetOverlay",
            parent,
            new Color(0f, 0f, 0f, 0.68f));
        var overlayElement = overlay.AddComponent<LayoutElement>();
        overlayElement.ignoreLayout = true;
        var overlayRect = (RectTransform)overlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.pivot = Vector2.zero;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlay.AddComponent<SpiritArtifactSelectionDismissSurface>().Configure(Close);
        overlay.AddComponent<SpiritArtifactEscapeHandler>().Configure(Close);

        window = TerriasUiComponents.CreateRect(
            "SpiritArtifactPresetWindow",
            overlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(layout.Width, layout.Height));
        ApplyPanel(window, Panel);
        window.AddComponent<SpiritArtifactPointerBlocker>();
        TerriasUiComponents.ConfigureVerticalLayout(window, new RectOffset(16, 16, 12, 12), 10f);
        BuildHeader(window.transform);
        BuildBody(window.transform);

        selectedPresetUid = collection.ArtifactInventory.Presets.FirstOrDefault()?.PresetUid ?? "";
        Rebuild();
        window.transform.SetAsLastSibling();
    }

    public static void Close()
    {
        if (editing && editDirty && !discardArmed)
        {
            discardArmed = true;
            PlayerApi.ShowCaption("预设修改尚未保存，再次关闭将放弃修改。");
            RebuildDetail();
            return;
        }
        CloseInternal(invokeCallback: true);
    }

    public static void ForceClose(bool invokeCallback = false)
        => CloseInternal(invokeCallback);

    private static void CloseInternal(bool invokeCallback)
    {
        pickerGrid?.Release();
        pickerGrid = null;
        TerriasModalHost.Close(ref overlay, "SpiritArtifact.Preset.Close", "[SpiritArtifact]");
        window = null;
        listHost = null;
        detailHost = null;
        detailScrollRect = null;
        presetCountText = null;
        saveCurrentButton = null;
        createButton = null;
        collection = new SpiritCollectionDocument();
        protectedArtifactUids.Clear();
        PresetRows.Clear();
        currentSpiritUid = "";
        selectedPresetUid = "";
        draft = null;
        nameInput = null;
        editing = false;
        editDirty = false;
        saveArmed = false;
        deleteArmed = false;
        discardArmed = false;
        if (!invokeCallback) return;
        var callback = closed;
        closed = null;
        callback?.Invoke();
    }

    private static void BuildHeader(Transform parent)
    {
        var header = Layout("PresetHeader", parent, 42f);
        TerriasUiComponents.ConfigureHorizontalLayout(header, new RectOffset(0, 0, 2, 2), 8f);
        Add(header.transform, "我的圣遗物预设", 20, Gold, 38f, 0f, 180f);
        Add(header.transform, "", 10, Muted, 38f, 1f);
        saveCurrentButton = TerriasUiComponents.CreateTextButton(
            header.transform,
            "保存当前搭配",
            new Vector2(112f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"),
            Item,
            Pale,
            12,
            SaveCurrent);
        createButton = TerriasUiComponents.CreateTextButton(
            header.transform,
            "＋新建预设",
            new Vector2(102f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"),
            Selected,
            Pale,
            12,
            BeginCreate);
        var closeButton = TerriasUiComponents.CreateTextButton(
            header.transform,
            "×",
            new Vector2(36f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"),
            Item,
            Pale,
            18,
            Close);
        var full = collection.ArtifactInventory.Presets.Count >= SpiritSystemContract.ArtifactPresetCapacity;
        saveCurrentButton.interactable = !full;
        createButton.interactable = !full;
        closeButton.interactable = true;
    }

    private static void BuildBody(Transform parent)
    {
        var body = Layout("PresetBody", parent, 340f, 1f);
        TerriasUiComponents.ConfigureHorizontalLayout(
            body,
            new RectOffset(0, 0, 0, 0),
            12f,
            childForceExpandHeight: true,
            alignment: TextAnchor.UpperLeft);

        var listWidth = layout.ListWidth;
        var listPanel = Layout("PresetListPanel", body.transform, 340f, 0f, listWidth);
        ApplyPanel(listPanel, new Color(0.025f, 0.038f, 0.058f, 0.98f));
        TerriasUiComponents.ConfigureVerticalLayout(listPanel, new RectOffset(8, 8, 8, 8), 6f);
        var listTitle = Layout("PresetListTitle", listPanel.transform, 28f);
        TerriasUiComponents.ConfigureHorizontalLayout(listTitle, new RectOffset(2, 2, 0, 0), 4f);
        Add(listTitle.transform, "预设列表", 13, Pale, 28f, 1f);
        presetCountText = Add(listTitle.transform,
            collection.ArtifactInventory.Presets.Count + "/" + SpiritSystemContract.ArtifactPresetCapacity,
            12,
            Muted,
            28f,
            0f,
            48f);
        var listScroll = TerriasUiComponents.CreateVerticalScrollArea(
            listPanel.transform,
            "ArtifactPresets",
            280f,
            1f,
            8f,
            24f,
            new Color(0f, 0f, 0f, 0.08f));
        listHost = listScroll.Content;

        var detail = Layout("PresetDetailPanel", body.transform, 340f, 1f);
        ApplyPanel(detail, new Color(0.025f, 0.038f, 0.058f, 0.98f));
        TerriasUiComponents.ConfigureVerticalLayout(detail, new RectOffset(0, 0, 0, 0), 0f);
        var detailScroll = TerriasUiComponents.CreateVerticalScrollArea(
            detail.transform,
            "PresetDetail",
            300f,
            1f,
            8f,
            24f,
            new Color(0f, 0f, 0f, 0.04f));
        detailScrollRect = detailScroll.Scroll;
        var detailLayout = detailScroll.Content.GetComponent<VerticalLayoutGroup>();
        if (detailLayout != null) detailLayout.padding = new RectOffset(12, 12, 10, 10);
        detailHost = detailScroll.Content;
    }

    private static void Rebuild()
    {
        collection = SpiritCollectionApi.Collection();
        protectedArtifactUids = SpiritArtifactPresetService.ProtectedArtifactUids(collection);
        var full = collection.ArtifactInventory.Presets.Count >= SpiritSystemContract.ArtifactPresetCapacity;
        if (presetCountText != null)
            presetCountText.text = collection.ArtifactInventory.Presets.Count + "/" + SpiritSystemContract.ArtifactPresetCapacity;
        if (saveCurrentButton != null) saveCurrentButton.interactable = !full && !editing;
        if (createButton != null) createButton.interactable = !full && !editing;
        var presets = collection.ArtifactInventory.Presets;
        if (selectedPresetUid.Length > 0 && presets.All(value => !Same(value.PresetUid, selectedPresetUid)))
            selectedPresetUid = presets.FirstOrDefault()?.PresetUid ?? "";
        RebuildList();
        RebuildDetail();
    }

    private static void RebuildList()
    {
        Clear(listHost);
        PresetRows.Clear();
        if (listHost == null) return;
        var presets = collection.ArtifactInventory.Presets;
        if (presets.Count == 0)
        {
            var empty = Layout("PresetEmpty", listHost, 160f);
            TerriasUiComponents.ConfigureVerticalLayout(empty, new RectOffset(10, 10, 18, 18), 8f);
            Add(empty.transform, "尚未创建预设", 15, Pale, 32f);
            Add(empty.transform, "可保存当前精灵搭配，或从空白五槽开始新建。", 12, Muted, 72f);
            return;
        }
        for (var index = 0; index < presets.Count; index++) CreatePresetRow(presets[index], index);
    }

    private static void CreatePresetRow(SpiritArtifactPreset preset, int index)
    {
        if (listHost == null) return;
        var selected = Same(preset.PresetUid, selectedPresetUid);
        var row = Layout("PresetRow-" + preset.PresetUid, listHost, 60f);
        var image = row.AddComponent<Image>();
        image.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-preset-row",
            210,
            60,
            6f,
            Color.white);
        image.type = Image.Type.Simple;
        image.color = selected ? Selected : Item;
        var button = row.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = !editing;
        button.onClick.AddListener(() => SelectPreset(preset.PresetUid));

        var stripe = TerriasUiComponents.CreateRect(
            "Accent",
            row.transform,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            new Vector2(3f, 0f));
        ((RectTransform)stripe.transform).anchoredPosition = new Vector2(1f, 0f);
        var stripeImage = stripe.AddComponent<Image>();
        stripeImage.color = selected ? Gold : Color.clear;
        stripeImage.raycastTarget = false;
        PresetRows[preset.PresetUid] = new PresetRowBinding
        {
            Background = image,
            Accent = stripeImage
        };

        var content = TerriasUiComponents.CreateFillRect("Content", row.transform);
        ((RectTransform)content.transform).offsetMin = new Vector2(8f, 5f);
        ((RectTransform)content.transform).offsetMax = new Vector2(-6f, -5f);
        TerriasUiComponents.ConfigureHorizontalLayout(content, new RectOffset(0, 0, 0, 0), 8f,
            childForceExpandHeight: true, alignment: TextAnchor.MiddleLeft);
        var iconRoot = Layout("Icon", content.transform, 44f, 0f, 44f);
        var iconBackground = iconRoot.AddComponent<Image>();
        iconBackground.sprite = TerriasUiSprites.RoundedSolid("spirit-artifact-preset-icon", 44, 44, 6f, Color.white);
        iconBackground.type = Image.Type.Simple;
        iconBackground.color = new Color(0.08f, 0.11f, 0.16f, 1f);
        iconBackground.raycastTarget = false;
        var flower = FindArtifact(preset.Get(SpiritArtifactSlots.Flower));
        var icon = TerriasUiComponents.CreateFillRect("Image", iconRoot.transform).AddComponent<Image>();
        icon.sprite = flower == null ? null : SpiritArtifactPanel.ArtifactSprite(flower);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        var copy = Layout("Copy", content.transform, 44f, 1f);
        TerriasUiComponents.ConfigureVerticalLayout(copy, new RectOffset(0, 0, 0, 0), 1f);
        var title = Add(copy.transform, (index + 1).ToString("00") + "  " + preset.Name, 13, Pale, 23f);
        title.fontStyle = FontStyle.Bold;
        var view = SpiritArtifactPresetService.ResolveView(collection, preset);
        var statusColor = !view.IsValid ? Danger : Same(view.MatchingSpiritUid, currentSpiritUid) ? Gold : Muted;
        Add(copy.transform, SetSummary(preset) + " · " + StatusText(preset), 10, statusColor, 19f);
    }

    private static void SelectPreset(string presetUid)
    {
        if (editing) return;
        selectedPresetUid = presetUid ?? "";
        deleteArmed = false;
        discardArmed = false;
        UpdateListSelection();
        RebuildDetail();
    }

    private static void UpdateListSelection()
    {
        foreach (var pair in PresetRows)
        {
            var active = Same(pair.Key, selectedPresetUid);
            if (pair.Value.Background != null) pair.Value.Background.color = active ? Selected : Item;
            if (pair.Value.Accent != null) pair.Value.Accent.color = active ? Gold : Color.clear;
        }
    }

    private static void RebuildDetail()
    {
        pickerGrid?.Release();
        pickerGrid = null;
        Clear(detailHost);
        if (detailHost == null) return;
        if (editing)
        {
            BuildEditor();
            if (detailScrollRect != null) detailScrollRect.verticalNormalizedPosition = 1f;
            return;
        }
        var preset = SelectedPreset();
        if (preset == null)
        {
            Add(detailHost, "我的预设", 20, Gold, 36f);
            Add(detailHost, "保存五个具体圣遗物实例后，可以一键转移并装备到当前精灵。", 14, Muted, 88f);
            if (detailScrollRect != null) detailScrollRect.verticalNormalizedPosition = 1f;
            return;
        }
        BuildPresetDetail(preset);
        if (detailScrollRect != null) detailScrollRect.verticalNormalizedPosition = 1f;
    }

    private static void BuildPresetDetail(SpiritArtifactPreset preset)
    {
        if (detailHost == null) return;
        var view = SpiritArtifactPresetService.ResolveView(collection, preset);
        var titleRow = Layout("PresetDetailTitle", detailHost, 36f);
        TerriasUiComponents.ConfigureHorizontalLayout(titleRow, new RectOffset(0, 0, 0, 0), 8f);
        var title = Add(titleRow.transform, preset.Name, 18, Gold, 34f, 1f);
        title.fontStyle = FontStyle.Bold;
        Add(titleRow.transform, SetSummary(preset), 11, Muted, 34f, 0f, 120f);
        Add(detailHost, StatusText(preset), 12, view.IsValid ? Pale : Danger, 24f);

        var slots = Layout("PresetSlots", detailHost, 82f);
        TerriasUiComponents.ConfigureHorizontalLayout(slots, new RectOffset(0, 0, 0, 0), layout.MiniCardSpacing,
            alignment: TextAnchor.MiddleCenter);
        foreach (var slot in SpiritArtifactSlots.All)
            CreateMiniArtifact(slots.transform, slot, FindArtifact(preset.Get(slot)));

        Add(detailHost, "套装效果", 12, Gold, 22f);
        Add(detailHost, SetEffectSummary(preset), 12, Pale, 48f);
        Add(detailHost, "总词条", 12, Gold, 22f);
        Add(detailHost, StatSummary(preset), 12, Pale, 58f);

        var transfer = view.TransferCountFor(currentSpiritUid);
        var transferText = transfer > 0
            ? "应用后将从 " + transfer + " 只其他精灵的装备中自动转移圣遗物。"
            : "应用后不会从其他精灵身上转移圣遗物。";
        Add(detailHost, transferText, 12, transfer > 0 ? Gold : Muted, 32f);
        Add(detailHost, "", 10, Muted, 0f, 1f);

        var managementActions = Layout("PresetManagementActions", detailHost, 38f);
        TerriasUiComponents.ConfigureHorizontalLayout(managementActions, new RectOffset(0, 0, 2, 2), 6f);
        TerriasUiComponents.CreateTextButton(managementActions.transform, "上移", new Vector2(58f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"), Item, Pale, 11, () => MovePreset(-1));
        TerriasUiComponents.CreateTextButton(managementActions.transform, "下移", new Vector2(58f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"), Item, Pale, 11, () => MovePreset(1));
        TerriasUiComponents.CreateTextButton(managementActions.transform, "编辑", new Vector2(58f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"), Item, Pale, 11, BeginEdit);
        TerriasUiComponents.CreateTextButton(
            managementActions.transform,
            deleteArmed ? "确认删除" : "删除",
            new Vector2(deleteArmed ? 78f : 58f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"),
            Danger,
            Pale,
            11,
            DeletePreset);
        var applyActions = Layout("PresetApplyActions", detailHost, 38f);
        TerriasUiComponents.ConfigureHorizontalLayout(applyActions, new RectOffset(0, 0, 2, 2), 8f);
        Add(applyActions.transform, "", 10, Muted, 34f, 1f);
        var current = view.IsValid && Same(view.MatchingSpiritUid, currentSpiritUid);
        var apply = TerriasUiComponents.CreateTextButton(
            applyActions.transform,
            current ? "当前已使用" : "应用到当前精灵",
            new Vector2(132f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"),
            Selected,
            view.IsValid && !current ? Pale : Muted,
            12,
            ApplyPreset);
        apply.interactable = view.IsValid && !current;
    }

    private static void BuildEditor()
    {
        if (detailHost == null || draft == null) return;
        var titleRow = Layout("PresetEditorTitle", detailHost, 38f);
        TerriasUiComponents.ConfigureHorizontalLayout(titleRow, new RectOffset(0, 0, 0, 0), 8f);
        Add(titleRow.transform,
            draft.PresetUid.Length == 0 ? "新建预设" : "编辑预设",
            18,
            Gold,
            36f,
            0f,
            layout.Width >= 620f ? 100f : 72f);
        nameInput = CreateInput(titleRow.transform, draft.Name, layout.Width >= 620f ? 180f : 150f, 1f);
        nameInput.characterLimit = SpiritSystemContract.ArtifactPresetNameMaximumLength;
        nameInput.onValueChanged.AddListener(value =>
        {
            if (draft == null) return;
            draft.Name = value ?? "";
            editDirty = true;
            saveArmed = false;
            discardArmed = false;
        });

        var slots = Layout("PresetEditorSlots", detailHost, 82f);
        TerriasUiComponents.ConfigureHorizontalLayout(slots, new RectOffset(0, 0, 0, 0), layout.MiniCardSpacing,
            alignment: TextAnchor.MiddleCenter);
        foreach (var slot in SpiritArtifactSlots.All)
        {
            var captured = slot;
            CreateDraftSlot(slots.transform, captured, FindArtifact(draft.Get(captured)), () =>
            {
                selectedDraftSlot = captured;
                saveArmed = false;
                RebuildDetail();
            });
        }

        var pickerTitle = Layout("PresetPickerTitle", detailHost, 26f);
        TerriasUiComponents.ConfigureHorizontalLayout(pickerTitle, new RectOffset(0, 0, 0, 0), 6f);
        Add(pickerTitle.transform, "选择" + SpiritArtifactSlots.DisplayName(selectedDraftSlot), 12, Gold, 24f, 1f);
        Add(pickerTitle.transform, "可与其他预设共享", 10, Muted, 24f, 0f, 100f);
        var pickerArea = TerriasUiComponents.CreateVirtualizedGridScrollArea(
            detailHost,
            "PresetPicker",
            150f,
            1f,
            28f,
            new Color(0f, 0f, 0f, 0.10f));
        pickerGrid = pickerArea.Root.AddComponent<SpiritArtifactVirtualizedGridView>();
        pickerGrid.Configure(
            pickerArea,
            4,
            new Vector2(SpiritArtifactCardStylePolicy.CellWidth, SpiritArtifactCardStylePolicy.CellHeight),
            new Vector2(SpiritArtifactCardStylePolicy.Spacing, SpiritArtifactCardStylePolicy.Spacing),
            new RectOffset(6, 6, 6, 6),
            (parent, name) => SpiritArtifactPanel.CreateSharedCell(parent, name, ChooseDraftArtifact),
            BindPickerCell,
            nextMinimumColumns: 3,
            nextMaximumColumns: 5,
            nextPoolKey: "SpiritArtifact.PresetPickerCell");
        var choices = collection.ArtifactInventory.Artifacts
            .Where(value => string.Equals(value.SlotId, selectedDraftSlot, StringComparison.Ordinal))
            .OrderByDescending(value => value.Rarity)
            .ThenByDescending(value => value.Level)
            .ThenByDescending(value => value.AcquiredAt, StringComparer.Ordinal)
            .ToArray();
        pickerGrid.SetItems(choices, resetScroll: true);
        pickerGrid.SetSelectedUid(draft.Get(selectedDraftSlot));

        var footer = Layout("PresetEditorActions", detailHost, 38f);
        TerriasUiComponents.ConfigureHorizontalLayout(footer, new RectOffset(0, 0, 2, 2), 8f);
        var completed = SpiritArtifactSlots.All.All(slot => draft.Get(slot).Length > 0);
        Add(footer.transform,
            completed ? "五个部件已完整" : "请选择五个不同部件后保存",
            11,
            completed ? Pale : Muted,
            34f,
            1f);
        TerriasUiComponents.CreateTextButton(footer.transform, "取消", new Vector2(72f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"), Item, Pale, 12, CancelEdit);
        var existing = draft.PresetUid.Length > 0;
        var save = TerriasUiComponents.CreateTextButton(
            footer.transform,
            existing && saveArmed ? "确认覆盖" : "保存预设",
            new Vector2(92f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"),
            Selected,
            completed ? Pale : Muted,
            12,
            SaveDraft);
        save.interactable = completed;
    }

    private static void BindPickerCell(SpiritArtifactCellView cell, SpiritArtifactInstance artifact)
    {
        SpiritArtifactPanel.BindSharedCell(
            cell,
            artifact,
            collection,
            draft != null && Same(draft.Get(selectedDraftSlot), artifact.ArtifactUid),
            false,
            protectedArtifactUids.Contains(artifact.ArtifactUid));
    }

    private static void ChooseDraftArtifact(string artifactUid)
    {
        if (!editing || draft == null) return;
        var artifact = FindArtifact(artifactUid);
        if (artifact == null || !Same(artifact.SlotId, selectedDraftSlot)) return;
        draft.Set(selectedDraftSlot, artifact.ArtifactUid);
        editDirty = true;
        saveArmed = false;
        discardArmed = false;
        RebuildDetail();
    }

    private static void SaveCurrent()
    {
        var result = SpiritArtifactApplicationService.SaveCurrentPreset(currentSpiritUid);
        PlayerApi.ShowCaption(result.Success ? "已保存当前圣遗物搭配。" : result.Reason);
        if (!result.Success) return;
        selectedPresetUid = result.Preset?.PresetUid ?? selectedPresetUid;
        Rebuild();
    }

    private static void BeginCreate()
    {
        if (collection.ArtifactInventory.Presets.Count >= SpiritSystemContract.ArtifactPresetCapacity) return;
        editing = true;
        editDirty = false;
        saveArmed = false;
        deleteArmed = false;
        discardArmed = false;
        selectedDraftSlot = SpiritArtifactSlots.Flower;
        draft = new SpiritArtifactPreset
        {
            Name = SpiritArtifactPresetService.SuggestName(collection, null)
        };
        Rebuild();
    }

    private static void BeginEdit()
    {
        var preset = SelectedPreset();
        if (preset == null) return;
        editing = true;
        editDirty = false;
        saveArmed = false;
        deleteArmed = false;
        discardArmed = false;
        selectedDraftSlot = SpiritArtifactSlots.Flower;
        draft = preset.Clone();
        Rebuild();
    }

    private static void CancelEdit()
    {
        editing = false;
        draft = null;
        nameInput = null;
        editDirty = false;
        saveArmed = false;
        discardArmed = false;
        Rebuild();
    }

    private static void SaveDraft()
    {
        if (draft == null) return;
        draft.Name = nameInput?.text ?? draft.Name;
        if (draft.PresetUid.Length > 0 && !saveArmed)
        {
            saveArmed = true;
            RebuildDetail();
            return;
        }
        var result = SpiritArtifactApplicationService.SavePreset(draft);
        PlayerApi.ShowCaption(result.Success ? "圣遗物预设已经保存。" : result.Reason);
        if (!result.Success)
        {
            saveArmed = false;
            RebuildDetail();
            return;
        }
        selectedPresetUid = result.Preset?.PresetUid ?? selectedPresetUid;
        editing = false;
        draft = null;
        editDirty = false;
        saveArmed = false;
        discardArmed = false;
        Rebuild();
    }

    private static void ApplyPreset()
    {
        var preset = SelectedPreset();
        if (preset == null) return;
        var result = SpiritArtifactApplicationService.ApplyPreset(currentSpiritUid, preset.PresetUid);
        PlayerApi.ShowCaption(result.Success
            ? "已应用预设「" + preset.Name + "」，从其他精灵转移 " + result.TransferredArtifactCount + " 件圣遗物。"
            : result.Reason);
        if (result.Success) CloseInternal(invokeCallback: true);
        else Rebuild();
    }

    private static void DeletePreset()
    {
        var preset = SelectedPreset();
        if (preset == null) return;
        if (!deleteArmed)
        {
            deleteArmed = true;
            RebuildDetail();
            return;
        }
        var result = SpiritArtifactApplicationService.DeletePreset(preset.PresetUid);
        PlayerApi.ShowCaption(result.Success ? "圣遗物预设已经删除。" : result.Reason);
        deleteArmed = false;
        if (result.Success) selectedPresetUid = "";
        Rebuild();
    }

    private static void MovePreset(int delta)
    {
        var preset = SelectedPreset();
        if (preset == null) return;
        var result = SpiritArtifactApplicationService.MovePreset(preset.PresetUid, delta);
        if (!result.Success) PlayerApi.ShowCaption(result.Reason);
        Rebuild();
    }

    private static SpiritArtifactPreset? SelectedPreset()
        => collection.ArtifactInventory.Presets.FirstOrDefault(value => Same(value.PresetUid, selectedPresetUid));

    private static SpiritArtifactInstance? FindArtifact(string artifactUid)
        => collection.ArtifactInventory.Artifacts.FirstOrDefault(value => Same(value.ArtifactUid, artifactUid));

    private static void CreateMiniArtifact(Transform parent, string slot, SpiritArtifactInstance? artifact)
    {
        var width = layout.MiniCardWidth;
        var root = Layout("PresetMini-" + slot, parent, 76f, 0f, width);
        var background = root.AddComponent<Image>();
        background.sprite = TerriasUiSprites.RoundedSolid("spirit-artifact-preset-mini", Mathf.RoundToInt(width), 76, 6f, Color.white);
        background.type = Image.Type.Simple;
        background.color = artifact == null ? new Color(0.08f, 0.11f, 0.16f, 1f) : Color.white;
        background.raycastTarget = false;
        if (artifact != null)
        {
            var rarity = TerriasUiComponents.CreateFillRect("Rarity", root.transform).AddComponent<Image>();
            rarity.sprite = SpiritArtifactPanel.CardBackgroundSprite(artifact.Rarity);
            rarity.color = Color.white;
            rarity.raycastTarget = false;
            var iconRoot = TerriasUiComponents.CreateRect("Icon", root.transform,
                new Vector2(0.12f, 0.22f), new Vector2(0.88f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero);
            var icon = iconRoot.AddComponent<Image>();
            icon.sprite = SpiritArtifactPanel.ArtifactSprite(artifact);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var level = TerriasUiComponents.CreateRect("Level", root.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f));
            TerriasUiComponents.ConfigureText(level, "Lv." + artifact.Level, 9, TextAnchor.MiddleCenter,
                new Color(0.12f, 0.16f, 0.22f, 1f)).raycastTarget = false;
        }
        var label = TerriasUiComponents.CreateRect("Slot", root.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 14f));
        TerriasUiComponents.ConfigureText(label, SpiritArtifactSlots.DisplayName(slot), 9, TextAnchor.MiddleCenter, Pale)
            .raycastTarget = false;
    }

    private static void CreateDraftSlot(
        Transform parent,
        string slot,
        SpiritArtifactInstance? artifact,
        Action choose)
    {
        var active = Same(slot, selectedDraftSlot);
        var width = layout.MiniCardWidth;
        var root = Layout("PresetDraft-" + slot, parent, 76f, 0f, width);
        var image = root.AddComponent<Image>();
        image.sprite = TerriasUiSprites.RoundedSolid("spirit-artifact-preset-draft", Mathf.RoundToInt(width), 76, 6f, Color.white);
        image.type = Image.Type.Simple;
        image.color = active ? Selected : Item;
        var button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => choose());
        var iconRoot = TerriasUiComponents.CreateRect("Icon", root.transform,
            new Vector2(0.15f, 0.20f), new Vector2(0.85f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero);
        var icon = iconRoot.AddComponent<Image>();
        icon.sprite = artifact == null ? null : SpiritArtifactPanel.ArtifactSprite(artifact);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var label = TerriasUiComponents.CreateRect("Label", root.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f));
        TerriasUiComponents.ConfigureText(label,
            artifact == null ? SpiritArtifactSlots.DisplayName(slot) + " ＋" : SpiritArtifactSlots.DisplayName(slot),
            9,
            TextAnchor.MiddleCenter,
            active ? Gold : Pale).raycastTarget = false;
    }

    private static string StatusText(SpiritArtifactPreset preset)
    {
        var view = SpiritArtifactPresetService.ResolveView(collection, preset);
        if (!view.IsValid) return "失效 · " + view.InvalidReason;
        if (Same(view.MatchingSpiritUid, currentSpiritUid)) return "当前精灵使用";
        if (view.MatchingSpiritUid.Length > 0) return "由「" + SpiritName(view.MatchingSpiritUid) + "」使用";
        if (view.OwnerSpiritUids.Count > 1) return "分散于 " + view.OwnerSpiritUids.Count + " 只精灵";
        if (view.OwnerSpiritUids.Count == 1) return "部分由「" + SpiritName(view.OwnerSpiritUids[0]) + "」装备";
        return "全部位于仓库";
    }

    private static string SpiritName(string spiritUid)
    {
        var spirit = collection.Instances.FirstOrDefault(value => Same(value.SpiritUid, spiritUid));
        return spirit == null ? "未知精灵" : SpiritPresentationResolver.Name(spirit);
    }

    private static string SetSummary(SpiritArtifactPreset preset)
    {
        var groups = preset.ArtifactUids()
            .Select(FindArtifact)
            .Where(value => value != null)
            .Cast<SpiritArtifactInstance>()
            .GroupBy(value => value.SetId, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => SpiritArtifactRegistry.Name(SpiritArtifactRegistry.Set(group.Key)) + group.Count() + "件")
            .ToArray();
        return groups.Length == 0 ? "无有效部件" : string.Join(" + ", groups);
    }

    private static string SetEffectSummary(SpiritArtifactPreset preset)
    {
        var counts = preset.ArtifactUids().Select(FindArtifact).Where(value => value != null)
            .Cast<SpiritArtifactInstance>()
            .GroupBy(value => value.SetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (var pair in counts.OrderByDescending(value => value.Value).ThenBy(value => value.Key, StringComparer.Ordinal))
        {
            var set = SpiritArtifactRegistry.Set(pair.Key);
            if (set == null) continue;
            foreach (var bonus in set.Bonuses.Where(value => value.RequiredPieces <= pair.Value)
                         .OrderBy(value => value.RequiredPieces))
            {
                lines.Add(SpiritArtifactRegistry.Name(set) + " " + bonus.RequiredPieces + "件："
                          + SpiritArtifactRegistry.Description(bonus));
            }
        }
        return lines.Count == 0 ? "未激活套装效果" : string.Join("\n", lines);
    }

    private static string StatSummary(SpiritArtifactPreset preset)
    {
        var items = preset.ArtifactUids().Select(FindArtifact).Where(value => value != null)
            .Cast<SpiritArtifactInstance>().Select(SpiritArtifactLoadoutResolver.ToBattleItem).ToArray();
        var battle = SpiritArtifactLoadoutResolver.Build(items, 0).Battle;
        var values = new List<string>();
        AddStat(values, "生命", battle.FlatLife);
        AddStat(values, "魔力", battle.OriginMagic);
        AddStat(values, "精神", battle.OriginSpirit);
        AddStat(values, "幸运", battle.OriginLuck);
        AddStat(values, "感知", battle.OriginPerception);
        AddStat(values, "速度", battle.Speed);
        AddStat(values, "魔能上限", battle.MaxMagic);
        AddStat(values, "开局超凡", battle.StartExtraordinary);
        return values.Count == 0 ? "无词条加成" : string.Join("　", values);
    }

    private static void AddStat(ICollection<string> values, string name, int value)
    {
        if (value != 0) values.Add(name + " +" + value);
    }

    private static InputField CreateInput(Transform parent, string value, float minWidth, float flexibleWidth)
    {
        var root = Layout("PresetNameInput", parent, 36f, flexibleWidth, minWidth);
        ApplyPanel(root, new Color(0.02f, 0.02f, 0.05f, 0.92f));
        var input = root.AddComponent<InputField>();
        var textRoot = TerriasUiComponents.CreateFillRect("Text", root.transform);
        var rect = (RectTransform)textRoot.transform;
        rect.offsetMin = new Vector2(10f, 0f);
        rect.offsetMax = new Vector2(-10f, 0f);
        var text = TerriasUiComponents.ConfigureText(textRoot, value, 14, TextAnchor.MiddleLeft, Pale);
        text.resizeTextForBestFit = false;
        input.textComponent = text;
        input.text = value;
        return input;
    }

    private static GameObject Layout(string name, Transform parent, float height, float flexible = 0f, float width = 0f)
    {
        var go = TerriasUiComponents.CreateLayoutObject(name, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = Math.Max(0f, height);
        element.preferredHeight = Math.Max(0f, height);
        element.flexibleHeight = flexible;
        if (width > 0f)
        {
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = flexible;
        }
        else if (flexible > 0f)
        {
            element.flexibleWidth = flexible;
        }
        return go;
    }

    private static Text Add(
        Transform parent,
        string value,
        int size,
        Color color,
        float height,
        float flexible = 0f,
        float width = 0f)
        => TerriasUiComponents.AddTextBlock(parent, value, size, TextAnchor.MiddleLeft, color, height, flexible, width);

    private static void ApplyPanel(GameObject go, Color color)
    {
        var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.sprite = TerriasUiSprites.Panel("[SpiritArtifact.Preset]");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = image.sprite != null ? Color.white : color;
    }

    private static void Clear(Transform? parent)
    {
        if (parent == null) return;
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            var child = parent.GetChild(index).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }

    private static bool Same(string? left, string? right)
        => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
}
