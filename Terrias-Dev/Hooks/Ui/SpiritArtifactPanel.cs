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

internal static class SpiritArtifactPanel
{
    private static readonly Color Panel = new(0.035f, 0.052f, 0.078f, 0.98f);
    private static readonly Color Item = new(0.052f, 0.073f, 0.105f, 0.98f);
    private static readonly Color Pale = new(0.90f, 0.94f, 0.97f);
    private static readonly Color Muted = new(0.62f, 0.70f, 0.77f);
    private static readonly Color Gold = new(0.95f, 0.76f, 0.34f);
    private static readonly Color Selected = new(0.105f, 0.205f, 0.235f, 0.99f);
    private static readonly Color RarityOne = new(0.47f, 0.53f, 0.61f, 0.96f);
    private static readonly Color RarityTwo = new(0.28f, 0.58f, 0.41f, 0.96f);
    private static readonly Color RarityThree = new(0.22f, 0.49f, 0.70f, 0.98f);
    private static readonly Color CardText = new(0.13f, 0.17f, 0.24f, 1f);
    private static readonly SpiritArtifactSelectionState Selection = new();
    private static readonly SpiritArtifactBatchSelectionState BatchSelection = new();
    private static readonly SpiritArtifactInventoryFilter InventoryFilter = new();

    private static GameObject? root;
    private static Transform? equipmentHost;
    private static SpiritArtifactPreviewView? preview;
    private static Transform? targetHost;
    private static Transform? categoryHost;
    private static Transform? actionHost;
    private static SpiritArtifactVirtualizedGridView? grid;
    private static Func<string>? selectedSpiritUid;
    private static int category;
    private static GameObject? targetSelector;
    private static GameObject? targetOverlay;
    private static GameObject? filterOverlay;
    private static GameObject? filterPanel;
    private static SpiritArtifactInventoryFilter? pendingFilter;
    private static GameObject? dismantleOverlay;
    private static GameObject? presetButton;
    private static SpiritCollectionDocument boundCollection = new();
    private static IReadOnlyList<SpiritArtifactInstance> filteredArtifacts = Array.Empty<SpiritArtifactInstance>();
    private static HashSet<string> presetProtectedUids = new(StringComparer.Ordinal);
    private static SpiritArtifactEquipmentCarousel? equipmentCarousel;
    private static Image? equipmentPortrait;
    private static SpiritPreviewAnimator? equipmentPortraitAnimator;
    private static readonly Dictionary<string, SpiritArtifactEquipmentSlotView> EquipmentSlots =
        new(StringComparer.Ordinal);
    private static string equipmentBindingKey = "";
    private static string equipmentPortraitSpiritUid = "";
    private static string boundSpiritUid = "";
    private static bool previewInitialized;
    private static float panelWidth = 760f;

    public static GameObject Build(
        Transform parent,
        Func<string> getSelectedSpiritUid,
        float contentWidth)
    {
        Release();
        selectedSpiritUid = getSelectedSpiritUid;
        var parentWidth = Mathf.Max(560f, contentWidth);
        panelWidth = parentWidth;
        var compact = parentWidth < 760f;
        var topHeight = compact ? 250f : 264f;
        const float targetHeight = 44f;
        const float categoryHeight = 34f;
        const float actionHeight = 40f;
        const float gridHeight = SpiritArtifactCardStylePolicy.InventoryHeight;
        var equipmentPanelWidth = Mathf.Clamp(parentWidth * 0.56f, 320f, 540f);
        root = Layout("ArtifactPage", parent, 500f, 1f);
        TerriasUiComponents.ConfigureVerticalLayout(root, new RectOffset(4, 4, 4, 4), 6f);

        var top = Layout("ArtifactTop", root.transform, topHeight);
        TerriasUiComponents.ConfigureHorizontalLayout(top, new RectOffset(0, 0, 0, 0), 10f,
            childForceExpandHeight: true, alignment: TextAnchor.UpperLeft);
        var equipment = Layout("Equipment", top.transform, topHeight, 0f, equipmentPanelWidth);
        ApplyPanel(equipment, Panel);
        equipment.AddComponent<SpiritArtifactSelectionDismissSurface>()
            .Configure(ClearArtifactSelection);
        var equipmentCanvas = TerriasUiComponents.CreateFillRect("EquipmentCanvas", equipment.transform);
        var equipmentCanvasRect = (RectTransform)equipmentCanvas.transform;
        equipmentCanvasRect.offsetMin = new Vector2(8f, 8f);
        equipmentCanvasRect.offsetMax = new Vector2(-8f, -8f);
        var equipmentBackground = equipmentCanvas.AddComponent<Image>();
        equipmentBackground.color = new Color(0f, 0f, 0f, 0.001f);
        equipmentBackground.raycastTarget = true;
        equipmentCanvas.AddComponent<SpiritArtifactSelectionDismissSurface>()
            .Configure(ClearArtifactSelection);
        var equipmentMotionRoot = TerriasUiComponents.CreateFillRect(
            "EquipmentMotionRoot",
            equipmentCanvas.transform);
        equipmentHost = equipmentMotionRoot.transform;
        equipmentCarousel = equipmentMotionRoot.AddComponent<SpiritArtifactEquipmentCarousel>();
        var equipmentTitleRoot = TerriasUiComponents.CreateRect(
            "EquipmentTitle",
            equipment.transform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(160f, 24f));
        ((RectTransform)equipmentTitleRoot.transform).anchoredPosition = new Vector2(10f, -6f);
        TerriasUiComponents.ConfigureText(
            equipmentTitleRoot,
            "当前精灵装备",
            15,
            TextAnchor.MiddleLeft,
            Gold).raycastTarget = false;
        presetButton = TerriasUiComponents.CreateTextButton(
            equipment.transform,
            "我的预设 0/20 ›",
            new Vector2(132f, 28f),
            TerriasUiSprites.Button("[SpiritArtifact.Preset]"),
            Item,
            Pale,
            12,
            OpenPresetPanel).gameObject;
        var presetElement = presetButton.GetComponent<LayoutElement>();
        if (presetElement != null) presetElement.ignoreLayout = true;
        var presetRect = (RectTransform)presetButton.transform;
        presetRect.anchorMin = new Vector2(1f, 1f);
        presetRect.anchorMax = new Vector2(1f, 1f);
        presetRect.pivot = new Vector2(1f, 1f);
        presetRect.sizeDelta = new Vector2(132f, 28f);
        presetRect.anchoredPosition = new Vector2(-8f, -4f);
        BuildEquipmentView();
        var summary = Layout("Summary", top.transform, topHeight, 1f);
        var summaryElement = summary.GetComponent<LayoutElement>();
        summaryElement.minWidth = compact ? 210f : 260f;
        summaryElement.flexibleWidth = 1f;
        ApplyPanel(summary, Panel);
        TerriasUiComponents.ConfigureVerticalLayout(summary, new RectOffset(12, 12, 10, 10), 4f);
        var summaryScroll = TerriasUiComponents.CreateVerticalScrollArea(
            summary.transform, "ArtifactSummary", Math.Max(120f, topHeight - 24f), 1f, 3f, 22f,
            new Color(0f, 0f, 0f, 0.05f));
        preview = SpiritArtifactPreviewView.Create(summaryScroll);

        var target = Layout("TargetAndDraw", root.transform, targetHeight);
        ApplyPanel(target, Panel);
        target.AddComponent<SpiritArtifactSelectionDismissSurface>()
            .Configure(ClearArtifactSelection);
        TerriasUiComponents.ConfigureHorizontalLayout(target, new RectOffset(8, 8, 5, 5), 8f);
        targetHost = target.transform;

        var categories = Layout("Categories", root.transform, categoryHeight);
        ApplyPanel(categories, Panel);
        categories.AddComponent<SpiritArtifactSelectionDismissSurface>()
            .Configure(ClearArtifactSelection);
        TerriasUiComponents.ConfigureHorizontalLayout(categories, new RectOffset(6, 6, 2, 2), 6f);
        categoryHost = categories.transform;

        var inventoryArea = TerriasUiComponents.CreateVirtualizedGridScrollArea(
            root.transform, "Artifacts", gridHeight, 1f, 32f, new Color(0f, 0f, 0f, 0.12f));
        inventoryArea.Viewport.gameObject.AddComponent<SpiritArtifactSelectionDismissSurface>()
            .Configure(ClearArtifactSelection);
        var columns = SpiritArtifactCardStylePolicy.ColumnsForWidth(parentWidth);
        grid = inventoryArea.Root.AddComponent<SpiritArtifactVirtualizedGridView>();
        grid.Configure(
            inventoryArea,
            columns,
            new Vector2(
                SpiritArtifactCardStylePolicy.CellWidth,
                SpiritArtifactCardStylePolicy.CellHeight),
            new Vector2(
                SpiritArtifactCardStylePolicy.Spacing,
                SpiritArtifactCardStylePolicy.Spacing),
            new RectOffset(
                SpiritArtifactCardStylePolicy.Padding,
                SpiritArtifactCardStylePolicy.Padding,
                SpiritArtifactCardStylePolicy.Padding,
                SpiritArtifactCardStylePolicy.Padding),
            CreateCell,
            BindCell);

        var actions = Layout("ArtifactActions", root.transform, actionHeight);
        ApplyPanel(actions, Panel);
        TerriasUiComponents.ConfigureHorizontalLayout(actions, new RectOffset(8, 8, 4, 4), 8f);
        actionHost = actions.transform;
        return root;
    }

    public static void SetVisible(bool visible)
    {
        if (root == null) return;
        if (!visible)
        {
            CloseTargetSelector();
            CloseFilter();
            CloseDismantleConfirmation();
            SpiritArtifactPresetPanel.ForceClose();
            Selection.Clear();
            BatchSelection.Exit();
            equipmentCarousel?.Resume();
            previewInitialized = false;
            root.SetActive(false);
            return;
        }
        root.SetActive(true);
        Refresh(false);
    }

    public static void Refresh(bool resetScroll)
    {
        if (root == null || !root.activeSelf) return;
        var collection = SpiritCollectionApi.Collection();
        boundCollection = collection;
        var uid = selectedSpiritUid?.Invoke() ?? "";
        var spirit = collection.Instances.FirstOrDefault(value => value.SpiritUid == uid);
        var nextSpiritUid = spirit?.SpiritUid ?? "";
        if (!string.Equals(boundSpiritUid, nextSpiritUid, StringComparison.Ordinal))
            Selection.Clear();
        boundSpiritUid = nextSpiritUid;
        Selection.Reconcile(collection.ArtifactInventory.Artifacts.Select(value => value.ArtifactUid));
        BatchSelection.Reconcile(collection.ArtifactInventory.Artifacts.Select(value => value.ArtifactUid));
        presetProtectedUids = SpiritArtifactPresetService.ProtectedArtifactUids(collection);
        BatchSelection.Remove(presetProtectedUids);

        BindEquipment(collection, spirit);
        RebuildTarget(collection);
        RebuildCategories();
        InventoryFilter.SlotId = category == 0 ? "" : SpiritArtifactSlots.All[category - 1];
        filteredArtifacts = SpiritArtifactInventoryQueryService.Filter(collection, InventoryFilter);
        grid?.SetItems(filteredArtifacts, resetScroll);
        grid?.SetSelectionState(Selection.SelectedArtifactUid, BatchSelection.SelectedArtifactUids);
        RenderPreview(collection, spirit, animate: false);
        RebuildActions(collection, spirit);
        SynchronizeCarouselFocus(spirit, animate: false);
        UpdatePresetButton();
    }

    public static void Release()
    {
        grid?.Release();
        grid = null;
        Selection.Clear();
        BatchSelection.Exit();
        preview?.Clear();
        preview = null;
        equipmentCarousel?.ResetBindings();
        equipmentCarousel = null;
        equipmentPortrait = null;
        equipmentPortraitAnimator = null;
        EquipmentSlots.Clear();
        equipmentBindingKey = "";
        equipmentPortraitSpiritUid = "";
        boundSpiritUid = "";
        previewInitialized = false;
        CloseTargetSelector();
        CloseFilter();
        CloseDismantleConfirmation();
        SpiritArtifactPresetPanel.ForceClose();
        boundCollection = new SpiritCollectionDocument();
        filteredArtifacts = Array.Empty<SpiritArtifactInstance>();
        presetProtectedUids.Clear();
        root = null;
        panelWidth = 760f;
        equipmentHost = targetHost = categoryHost = actionHost = null;
        presetButton = null;
    }

    private static void BuildEquipmentView()
    {
        if (equipmentHost == null) return;
        EquipmentSlots.Clear();
        equipmentCarousel?.ResetBindings();

        var portraitRoot = TerriasUiComponents.CreateRect(
            "SpiritPortrait",
            equipmentHost,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(150f, 190f));
        equipmentPortrait = portraitRoot.AddComponent<Image>();
        equipmentPortrait.preserveAspect = true;
        equipmentPortrait.raycastTarget = false;
        equipmentPortrait.color = new Color(1f, 1f, 1f, 0.12f);
        equipmentPortraitAnimator = portraitRoot.AddComponent<SpiritPreviewAnimator>();
        equipmentPortraitAnimator.enabled = false;
        equipmentCarousel?.BindPortrait((RectTransform)portraitRoot.transform);

        foreach (var slotId in SpiritArtifactSlots.All)
        {
            var view = CreateEquipmentSlotView(equipmentHost, slotId);
            EquipmentSlots[slotId] = view;
            equipmentCarousel?.BindSlot(slotId, (RectTransform)view.transform);
        }
        equipmentCarousel?.Apply();
    }

    private static SpiritArtifactEquipmentSlotView CreateEquipmentSlotView(Transform parent, string slotId)
    {
        var root = TerriasUiComponents.CreateRect("Equipped-" + slotId, parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(
                SpiritArtifactCardStylePolicy.EquipmentSlotSize,
                SpiritArtifactCardStylePolicy.EquipmentSlotSize));
        var border = root.AddComponent<Image>();
        border.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-equipment-border",
            56,
            56,
            8f,
            Color.white);
        border.type = Image.Type.Simple;

        var surfaceRoot = TerriasUiComponents.CreateFillRect("Surface", root.transform);
        var surfaceRect = (RectTransform)surfaceRoot.transform;
        surfaceRect.offsetMin = new Vector2(2f, 2f);
        surfaceRect.offsetMax = new Vector2(-2f, -2f);
        var surface = surfaceRoot.AddComponent<Image>();
        surface.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-equipment-surface",
            52,
            52,
            7f,
            Color.white);
        surface.type = Image.Type.Simple;
        surface.color = new Color(0.13f, 0.17f, 0.23f, 0.99f);
        surface.raycastTarget = false;

        var iconRoot = TerriasUiComponents.CreateRect(
            "Icon",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.one * SpiritArtifactCardStylePolicy.EquippedIconSize);
        ((RectTransform)iconRoot.transform).anchoredPosition = new Vector2(0f, 4f);
        var icon = iconRoot.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        var button = root.AddComponent<Button>();
        button.targetGraphic = border;
        var labelRoot = TerriasUiComponents.CreateRect("SlotLabel", root.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f));
        var labelBackground = labelRoot.AddComponent<Image>();
        labelBackground.color = new Color(0.015f, 0.025f, 0.04f, 0.72f);
        labelBackground.raycastTarget = false;
        var label = TerriasUiComponents.AddTextFill(
            labelRoot.transform,
            SpiritArtifactSlots.DisplayName(slotId),
            9,
            TextAnchor.MiddleCenter,
            Pale);
        label.resizeTextForBestFit = false;
        label.raycastTarget = false;

        var view = root.AddComponent<SpiritArtifactEquipmentSlotView>();
        view.Configure(slotId, border, icon, (RectTransform)iconRoot.transform, button, SelectEquipmentSlot);
        view.Bind(
            "",
            EmptySlotSprite(slotId),
            new Color(0.49f, 0.76f, 0.87f, 0.94f),
            new Color(0.78f, 0.88f, 0.93f, 0.92f),
            SpiritArtifactCardStylePolicy.EmptyIconSize);
        return view;
    }

    private static void BindEquipment(SpiritCollectionDocument collection, SpiritInstance? spirit)
    {
        var loadout = spirit?.ArtifactLoadout;
        var bindingKey = (spirit?.SpiritUid ?? "")
                         + "|"
                         + (loadout?.Revision ?? 0)
                         + "|"
                         + (loadout?.LoadoutHash ?? "");
        if (string.Equals(equipmentBindingKey, bindingKey, StringComparison.Ordinal)) return;
        equipmentBindingKey = bindingKey;

        var spiritUid = spirit?.SpiritUid ?? "";
        if (!string.Equals(equipmentPortraitSpiritUid, spiritUid, StringComparison.Ordinal))
        {
            equipmentPortraitSpiritUid = spiritUid;
            var portraitSprite = spirit == null
                ? null
                : SpiritPortraitUi.Resolve(spirit.Snapshot, "spirit.artifact.equipment-portrait");
            if (equipmentPortrait != null)
            {
                equipmentPortrait.sprite = portraitSprite;
                equipmentPortrait.color = portraitSprite == null
                    ? new Color(1f, 1f, 1f, 0.12f)
                    : Color.white;
            }
            if (equipmentPortraitAnimator != null)
            {
                if (spirit == null) equipmentPortraitAnimator.enabled = false;
                else equipmentPortraitAnimator.Configure(spirit.Snapshot.IdlePath, portraitSprite);
            }
        }

        foreach (var slotId in SpiritArtifactSlots.All)
        {
            if (!EquipmentSlots.TryGetValue(slotId, out var view)) continue;
            var artifactUid = loadout?.Get(slotId) ?? "";
            var artifact = collection.ArtifactInventory.Artifacts.FirstOrDefault(
                value => value.ArtifactUid == artifactUid);
            var sprite = artifact == null ? EmptySlotSprite(slotId) : ArtifactSprite(artifact);
            var borderColor = artifact == null
                ? new Color(0.49f, 0.76f, 0.87f, 0.94f)
                : Color.Lerp(RarityColor(artifact.Rarity), Color.white, 0.22f);
            var iconColor = sprite == null
                ? new Color(1f, 1f, 1f, 0.12f)
                : artifact == null
                    ? new Color(0.78f, 0.88f, 0.93f, 0.92f)
                    : Color.white;
            view.Bind(
                artifact?.ArtifactUid ?? "",
                sprite,
                borderColor,
                iconColor,
                artifact == null
                    ? SpiritArtifactCardStylePolicy.EmptyIconSize
                    : SpiritArtifactCardStylePolicy.EquippedIconSize);
        }
        TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Equipment.IncrementalBind");
    }

    private static void RebuildTarget(SpiritCollectionDocument collection)
    {
        Clear(targetHost);
        if (targetHost == null) return;
        var inventory = collection.ArtifactInventory;
        var set = SpiritArtifactRegistry.Set(inventory.TargetSetId);
        var representative = SpiritArtifactRegistry.Piece(set?.RepresentativePieceId);
        var targetButton = TerriasUiComponents.CreateTextButton(targetHost,
            "", new Vector2(252f, 40f),
            TerriasUiSprites.Button("[SpiritArtifact]"), Item, Pale, 14, OpenTargetSelector);
        var targetLabel = targetButton.GetComponentInChildren<Text>();
        if (targetLabel != null) targetLabel.gameObject.SetActive(false);
        if (representative != null)
        {
            var icon = TerriasUiComponents.CreateRect("TargetIcon", targetButton.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(32f, 32f));
            var rect = icon.transform as RectTransform;
            if (rect != null) rect.anchoredPosition = new Vector2(5f, 0f);
            var image = icon.AddComponent<Image>();
            image.sprite = TerriasResourceCache.Load<Sprite>(representative.IconPath, true, "spirit.artifact.target");
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        var copy = TerriasUiComponents.CreateFillRect("TargetCopy", targetButton.transform);
        var copyRect = (RectTransform)copy.transform;
        copyRect.offsetMin = new Vector2(42f, 2f);
        copyRect.offsetMax = new Vector2(-24f, -2f);
        var targetCaptionRoot = TerriasUiComponents.CreateRect(
            "TargetCaption",
            copy.transform,
            new Vector2(0f, 0f),
            new Vector2(0.38f, 1f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        TerriasUiComponents.ConfigureText(
            targetCaptionRoot,
            "目标套装",
            12,
            TextAnchor.MiddleLeft,
            Muted).raycastTarget = false;
        var setNameRoot = TerriasUiComponents.CreateRect(
            "SetName",
            copy.transform,
            new Vector2(0.38f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        var setName = TerriasUiComponents.ConfigureText(
            setNameRoot,
            SpiritArtifactRegistry.Name(set),
            13,
            TextAnchor.MiddleCenter,
            Pale);
        setName.fontStyle = FontStyle.Bold;
        setName.raycastTarget = false;
        var chevronRoot = TerriasUiComponents.CreateRect(
            "Chevron",
            targetButton.transform,
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0.5f),
            new Vector2(22f, 0f));
        ((RectTransform)chevronRoot.transform).anchoredPosition = new Vector2(-2f, 0f);
        TerriasUiComponents.ConfigureText(
            chevronRoot,
            "›",
            18,
            TextAnchor.MiddleCenter,
            Muted).raycastTarget = false;
        Add(targetHost, "", 12, Muted, 40f, 1f);
        Add(targetHost, "精粹 " + inventory.Essence, 12, Gold, 40f, 0f, 80f);
        Add(targetHost, "真理之晶 " + SpiritArtifactApplicationService.TruthBalance(), 12, Gold, 40f, 0f, 112f);
        var pending = inventory.PendingReveals.FirstOrDefault();
        if (pending != null)
        {
            TerriasUiComponents.CreateTextButton(targetHost, "未确认结果", new Vector2(112f, 40f),
                TerriasUiSprites.Button("[SpiritArtifact]"), Item, Pale, 12, () =>
                {
                    ClearArtifactSelection();
                    var values = SpiritArtifactApplicationService.PendingReveal(pending.Token);
                    SpiritArtifactWishPresenter.Play(pending.Token, values, () => Refresh(true));
                });
        }
        TerriasUiComponents.CreateTextButton(targetHost, "抽取 ×10", new Vector2(124f, 40f),
            TerriasUiSprites.Button("[SpiritArtifact]"), Selected, Pale, 15, DrawTen);
    }

    private static void RebuildCategories()
    {
        Clear(categoryHost);
        if (categoryHost == null) return;
        var compact = panelWidth < 700f;
        var tabWidth = compact ? 48f : 62f;
        var actionWidth = compact ? 60f : 68f;
        var categoryLayout = categoryHost.GetComponent<HorizontalLayoutGroup>();
        if (categoryLayout != null)
        {
            categoryLayout.spacing = compact ? 4f : 6f;
            categoryLayout.padding = compact ? new RectOffset(4, 4, 2, 2) : new RectOffset(6, 6, 2, 2);
        }
        Add(
            categoryHost,
            "仓库 " + boundCollection.ArtifactInventory.Artifacts.Count + "/" + SpiritArtifactRegistry.InventoryCapacity,
            12,
            Gold,
            32f,
            0f,
            compact ? 84f : 96f);
        var labels = new[] { "全部", "生之花", "死之羽", "时之沙", "空之杯", "理之冠" };
        for (var index = 0; index < labels.Length; index++)
        {
            var captured = index;
            TerriasUiComponents.CreateTextButton(categoryHost, labels[index], new Vector2(tabWidth, 32f),
                TerriasUiSprites.Button("[SpiritArtifact]"), category == index ? Selected : Item,
                category == index ? Pale : Muted, 13, () => ChangeCategory(captured));
        }
        Add(categoryHost, "", 10, Muted, 32f, 1f);
        var filterCount = (InventoryFilter.RarityMask != 0 ? 1 : 0)
                          + (InventoryFilter.LevelBand > 0 ? 1 : 0)
                          + (InventoryFilter.SetId.Length > 0 ? 1 : 0)
                          + (InventoryFilter.MainStatId.Length > 0 ? 1 : 0)
                          + (InventoryFilter.CleanableOnly ? 1 : 0);
        TerriasUiComponents.CreateTextButton(
            categoryHost,
            filterCount > 0 ? "筛选·" + filterCount : "筛选",
            new Vector2(actionWidth, 32f),
            TerriasUiSprites.Button("[SpiritArtifact]"),
            filterCount > 0 ? Selected : Item,
            filterCount > 0 ? Pale : Muted,
            12,
            OpenFilter);
        TerriasUiComponents.CreateTextButton(
            categoryHost,
            BatchSelection.IsActive ? "完成" : "多选",
            new Vector2(actionWidth, 32f),
            TerriasUiSprites.Button("[SpiritArtifact]"),
            BatchSelection.IsActive ? Selected : Item,
            Pale,
            12,
            ToggleBatchMode);
    }

    private static void ToggleBatchMode()
    {
        if (BatchSelection.IsActive)
        {
            BatchSelection.Exit();
        }
        else
        {
            CloseTargetSelector();
            CloseFilter();
            SpiritArtifactPresetPanel.Close();
            BatchSelection.Enter(Selection.SelectedArtifactUid);
        }
        grid?.SetSelectionState(Selection.SelectedArtifactUid, BatchSelection.SelectedArtifactUids);
        RebuildCategories();
        RebuildActions(boundCollection, CurrentSpirit());
        UpdatePresetButton();
    }

    private static void UpdatePresetButton()
    {
        if (presetButton == null) return;
        var label = presetButton.GetComponentInChildren<Text>();
        if (label != null)
            label.text = "我的预设 " + boundCollection.ArtifactInventory.Presets.Count
                         + "/" + SpiritSystemContract.ArtifactPresetCapacity + " ›";
        var button = presetButton.GetComponent<Button>();
        if (button != null) button.interactable = !BatchSelection.IsActive;
    }

    private static void OpenPresetPanel()
    {
        if (root == null || BatchSelection.IsActive) return;
        CloseTargetSelector();
        CloseFilter();
        ClearArtifactSelection();
        if (equipmentCarousel != null) equipmentCarousel.enabled = false;
        SpiritArtifactPresetPanel.Open(
            root.transform,
            panelWidth,
            selectedSpiritUid?.Invoke() ?? "",
            () =>
            {
                equipmentCarousel?.Resume();
                Refresh(false);
            });
    }

    private static void RebuildActions(SpiritCollectionDocument collection, SpiritInstance? spirit)
    {
        Clear(actionHost);
        if (actionHost == null) return;
        if (BatchSelection.IsActive)
        {
            RebuildBatchActions(collection);
            return;
        }
        var artifact = collection.ArtifactInventory.Artifacts.FirstOrDefault(
            value => value.ArtifactUid == Selection.SelectedArtifactUid);
        if (artifact == null)
        {
            Add(actionHost, "选择圣遗物后可装备、强化、锁定或分解", 13, Muted, 34f, 1f);
            return;
        }
        var piece = SpiritArtifactRegistry.Piece(artifact.PieceId);
        Add(actionHost, SpiritArtifactRegistry.Name(piece) + "  Lv." + artifact.Level, 14, Gold, 34f, 1f);
        TerriasUiComponents.CreateTextButton(actionHost, "装备", new Vector2(82f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact]"), Item, Pale, 13, () => Apply(SpiritArtifactApplicationService.Equip(spirit?.SpiritUid ?? "", artifact.ArtifactUid)));
        if (spirit?.ArtifactLoadout?.Get(artifact.SlotId) == artifact.ArtifactUid)
            TerriasUiComponents.CreateTextButton(actionHost, "卸下", new Vector2(82f, 34f),
                TerriasUiSprites.Button("[SpiritArtifact]"), Item, Pale, 13, () => Apply(SpiritArtifactApplicationService.Unequip(spirit.SpiritUid, artifact.SlotId)));
        TerriasUiComponents.CreateTextButton(actionHost, artifact.Level >= 5 ? "已满级" : "强化", new Vector2(82f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact]"), Item, Pale, 13, () => Apply(SpiritArtifactApplicationService.Upgrade(artifact.ArtifactUid)));
        TerriasUiComponents.CreateTextButton(actionHost, artifact.Locked ? "解锁" : "锁定", new Vector2(82f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact]"), Item, Pale, 13, () => Apply(SpiritArtifactApplicationService.ToggleLock(artifact.ArtifactUid)));
        var protectedByPreset = presetProtectedUids.Contains(artifact.ArtifactUid);
        var equippedElsewhere = SpiritArtifactInventoryService.EquippedSpiritUid(collection, artifact.ArtifactUid).Length > 0;
        var dismantleBlocked = protectedByPreset || equippedElsewhere || artifact.Locked;
        var dismantleLabel = protectedByPreset
            ? "预设保护"
            : equippedElsewhere ? "已装备" : artifact.Locked ? "已锁定" : "分解";
        var dismantle = TerriasUiComponents.CreateTextButton(
            actionHost,
            dismantleLabel,
            new Vector2(dismantleBlocked ? 92f : 82f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact]"),
            new Color(0.35f, 0.12f, 0.12f, 1f),
            dismantleBlocked ? Muted : Pale,
            13,
            () => Apply(SpiritArtifactApplicationService.Dismantle(new[] { artifact.ArtifactUid })));
        dismantle.interactable = !dismantleBlocked;
    }

    private static void RebuildBatchActions(SpiritCollectionDocument collection)
    {
        if (actionHost == null) return;
        var summary = SpiritArtifactInventoryQueryService.Summarize(
            collection,
            BatchSelection.SelectedArtifactUids);
        var selectAllCandidates = SpiritArtifactInventoryQueryService
            .SelectAllCleanable(collection, filteredArtifacts);
        var protectedCount = Math.Max(0, summary.SelectedCount - summary.CleanableCount);
        Add(
            actionHost,
            "已选 " + summary.SelectedCount
            + "｜可分解 " + summary.CleanableCount
            + (protectedCount > 0 ? "｜受保护 " + protectedCount : "")
            + "｜预计精粹 +" + summary.EstimatedEssence,
            12,
            summary.SelectedCount > 0 ? Pale : Muted,
            34f,
            1f);
        TerriasUiComponents.CreateTextButton(actionHost, "全选 " + selectAllCandidates.Count, new Vector2(76f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Batch]"), Item, Pale, 12, () =>
            {
                BatchSelection.Replace(selectAllCandidates);
                grid?.SetSelectionState(Selection.SelectedArtifactUid, BatchSelection.SelectedArtifactUids);
                RebuildActions(boundCollection, CurrentSpirit());
            });
        TerriasUiComponents.CreateTextButton(actionHost, "清空", new Vector2(58f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Batch]"), Item, Pale, 12, () =>
            {
                BatchSelection.Clear();
                grid?.SetSelectionState(Selection.SelectedArtifactUid, BatchSelection.SelectedArtifactUids);
                RebuildActions(boundCollection, CurrentSpirit());
            });
        var canAct = summary.SelectedCount > 0;
        var lockButton = TerriasUiComponents.CreateTextButton(actionHost, "锁定", new Vector2(58f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Batch]"), Item, canAct ? Pale : Muted, 12,
            () => ApplyBatchLock(true));
        lockButton.interactable = canAct;
        var unlockButton = TerriasUiComponents.CreateTextButton(actionHost, "解锁", new Vector2(58f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Batch]"), Item, canAct ? Pale : Muted, 12,
            () => ApplyBatchLock(false));
        unlockButton.interactable = canAct;
        var canDismantle = summary.SelectedCount > 0 && summary.CleanableCount == summary.SelectedCount;
        var dismantle = TerriasUiComponents.CreateTextButton(
            actionHost,
            "分解 +" + summary.EstimatedEssence,
            new Vector2(92f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Batch]"),
            new Color(0.35f, 0.12f, 0.12f, 1f),
            canDismantle ? Pale : Muted,
            12,
            OpenDismantleConfirmation);
        dismantle.interactable = canDismantle;
    }

    private static void ApplyBatchLock(bool locked)
    {
        var result = SpiritArtifactApplicationService.SetLock(
            BatchSelection.SelectedArtifactUids,
            locked);
        PlayerApi.ShowCaption(result.Success
            ? (locked ? "已批量锁定圣遗物。" : "已批量解锁圣遗物。")
            : result.Reason);
        if (result.Success) Refresh(false);
    }

    private static SpiritArtifactCellView CreateCell(Transform parent, string name)
        => CreateSharedCell(parent, name, SelectArtifact);

    internal static SpiritArtifactCellView CreateSharedCell(
        Transform parent,
        string name,
        Action<string> onClick)
    {
        var root = TerriasUiComponents.CreateRect(
            name,
            parent,
            Vector2.zero,
            Vector2.zero,
            new Vector2(0f, 1f),
            new Vector2(
                SpiritArtifactCardStylePolicy.CellWidth,
                SpiritArtifactCardStylePolicy.CellHeight));
        var slot = root.AddComponent<Image>();
        slot.color = Color.clear;
        slot.raycastTarget = false;

        var visualRoot = TerriasUiComponents.CreateRect(
            "VisualRoot",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(
                SpiritArtifactCardStylePolicy.CardWidth,
                SpiritArtifactCardStylePolicy.CardHeight));
        var selectionHaloRoot = TerriasUiComponents.CreateRect(
            "SelectionHalo",
            visualRoot.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(
                SpiritArtifactCardStylePolicy.SelectionHaloWidth,
                SpiritArtifactCardStylePolicy.SelectionHaloHeight));
        var selectionHaloImage = selectionHaloRoot.AddComponent<Image>();
        selectionHaloImage.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-selection-halo",
            Mathf.RoundToInt(SpiritArtifactCardStylePolicy.SelectionHaloWidth),
            Mathf.RoundToInt(SpiritArtifactCardStylePolicy.SelectionHaloHeight),
            SpiritArtifactCardStylePolicy.SelectionHaloRadius,
            Color.white);
        selectionHaloImage.type = Image.Type.Simple;
        selectionHaloImage.color = new Color(1f, 0.82f, 0.38f, 1f);
        selectionHaloImage.raycastTarget = false;
        var selectionHalo = selectionHaloRoot.AddComponent<CanvasGroup>();
        selectionHalo.alpha = 0f;
        selectionHalo.blocksRaycasts = false;
        selectionHalo.interactable = false;
        selectionHaloRoot.SetActive(false);

        var card = TerriasUiComponents.CreateRect(
            "Card",
            visualRoot.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(
                SpiritArtifactCardStylePolicy.CardWidth,
                SpiritArtifactCardStylePolicy.CardHeight));
        var baseImage = card.AddComponent<Image>();
        baseImage.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-card-base",
            72,
            84,
            SpiritArtifactCardStylePolicy.CardRadius,
            Color.white);
        baseImage.type = Image.Type.Simple;
        baseImage.color = Color.white;
        baseImage.raycastTarget = true;

        var artRoot = TerriasUiComponents.CreateRect(
            "RarityArt",
            card.transform,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, SpiritArtifactCardStylePolicy.ArtHeight));
        var background = artRoot.AddComponent<Image>();
        background.sprite = CardBackgroundSprite(1);
        background.type = Image.Type.Simple;
        background.color = Color.white;
        background.raycastTarget = false;

        var iconRoot = TerriasUiComponents.CreateRect("Icon", artRoot.transform,
            new Vector2(0.10f, 0.19f), new Vector2(0.90f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero);
        var icon = iconRoot.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var starText = OverlayText(
            artRoot.transform,
            "Stars",
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(58f, 12f),
            new Vector2(0f, 1f),
            9,
            Gold);

        var levelRoot = TerriasUiComponents.CreateRect(
            "Level",
            card.transform,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 16f));
        var levelText = TerriasUiComponents.AddTextFill(
            levelRoot.transform,
            "",
            10,
            TextAnchor.MiddleCenter,
            CardText);
        levelText.raycastTarget = false;

        var lockBadge = CreateCornerBadge(
            card.transform,
            "Lock",
            new Vector2(0f, 1f),
            new Vector2(2f, -2f),
            new Vector2(16f, 16f),
            new Color(0.42f, 0.16f, 0.16f, 0.96f));
        TerriasUiComponents.AddTextFill(
            lockBadge.transform,
            "锁",
            9,
            TextAnchor.MiddleCenter,
            Color.white).raycastTarget = false;

        var presetBadge = CreateCornerBadge(
            card.transform,
            "Preset",
            new Vector2(0f, 1f),
            new Vector2(2f, -2f),
            new Vector2(16f, 16f),
            new Color(0.47f, 0.34f, 0.10f, 0.96f));
        TerriasUiComponents.AddTextFill(
            presetBadge.transform,
            "案",
            9,
            TextAnchor.MiddleCenter,
            Color.white).raycastTarget = false;
        presetBadge.SetActive(false);

        var ownerBadge = TerriasUiComponents.CreateRect(
            "Owner",
            card.transform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(20f, 20f));
        ((RectTransform)ownerBadge.transform).anchoredPosition = new Vector2(-2f, -2f);
        var ownerRing = ownerBadge.AddComponent<Image>();
        ownerRing.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-owner-ring",
            20,
            20,
            10f,
            Color.white);
        ownerRing.type = Image.Type.Simple;
        ownerRing.color = new Color(1f, 0.88f, 0.58f, 1f);
        ownerRing.raycastTarget = false;
        var ownerClip = TerriasUiComponents.CreateRect(
            "OwnerClip",
            ownerBadge.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(16f, 16f));
        var ownerClipImage = ownerClip.AddComponent<Image>();
        ownerClipImage.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-owner-mask",
            16,
            16,
            8f,
            Color.white);
        ownerClipImage.type = Image.Type.Simple;
        ownerClipImage.color = Color.white;
        ownerClipImage.raycastTarget = false;
        ownerClip.AddComponent<Mask>().showMaskGraphic = true;
        var ownerPortraitRoot = TerriasUiComponents.CreateFillRect("Portrait", ownerClip.transform);
        var ownerPortrait = ownerPortraitRoot.AddComponent<Image>();
        ownerPortrait.preserveAspect = true;
        ownerPortrait.raycastTarget = false;
        var ownerFallback = TerriasUiComponents.CreateFillRect("Fallback", ownerClip.transform);
        var fallbackText = TerriasUiComponents.ConfigureText(
            ownerFallback,
            "装",
            9,
            TextAnchor.MiddleCenter,
            new Color(0.23f, 0.18f, 0.09f, 1f));
        fallbackText.raycastTarget = false;
        ownerBadge.SetActive(false);

        var batchBadge = CreateCornerBadge(
            card.transform,
            "Batch",
            new Vector2(1f, 0f),
            new Vector2(-2f, 2f),
            new Vector2(16f, 16f),
            new Color(0.13f, 0.70f, 0.78f, 0.98f));
        TerriasUiComponents.AddTextFill(
            batchBadge.transform,
            "✓",
            10,
            TextAnchor.MiddleCenter,
            Color.white).raycastTarget = false;
        batchBadge.SetActive(false);

        var batchFrame = TerriasUiComponents.CreateFillRect("BatchFrame", card.transform);
        var batchStroke = batchFrame.AddComponent<SpiritArtifactRoundedStrokeGraphic>();
        batchStroke.Thickness = 2.5f;
        batchStroke.Radius = SpiritArtifactCardStylePolicy.CardRadius;
        batchStroke.color = new Color(0.55f, 0.93f, 0.96f, 0.98f);
        batchStroke.raycastTarget = false;
        batchFrame.SetActive(false);

        var hoverFrame = CreateCardFrame(
            card.transform,
            "HoverFrame",
            SpiritArtifactCardStylePolicy.HoverStrokeWidth,
            new Color(1f, 1f, 1f, 0.96f));

        var button = root.AddComponent<Button>();
        button.targetGraphic = baseImage;
        button.transition = Selectable.Transition.None;
        var hover = root.AddComponent<SpiritArtifactHoverProbe>();
        var motion = root.AddComponent<SpiritArtifactCardMotion>();
        motion.Configure((RectTransform)visualRoot.transform, selectionHalo, hoverFrame);
        var view = root.AddComponent<SpiritArtifactCellView>();
        view.Initialize(
            background,
            icon,
            starText,
            levelText,
            lockBadge,
            ownerBadge,
            presetBadge,
            batchBadge,
            batchFrame,
            ownerPortrait,
            ownerFallback,
            motion,
            button,
            hover,
            onClick);
        return view;
    }

    private static void BindCell(SpiritArtifactCellView cell, SpiritArtifactInstance artifact)
        => BindSharedCell(
            cell,
            artifact,
            boundCollection,
            artifact.ArtifactUid == Selection.SelectedArtifactUid,
            BatchSelection.Contains(artifact.ArtifactUid),
            presetProtectedUids.Contains(artifact.ArtifactUid));

    internal static void BindSharedCell(
        SpiritArtifactCellView cell,
        SpiritArtifactInstance artifact,
        SpiritCollectionDocument collection,
        bool focused,
        bool batchSelected,
        bool presetProtected)
    {
        var ownerUid = SpiritArtifactInventoryService.EquippedSpiritUid(collection, artifact.ArtifactUid);
        var owner = collection.Instances.FirstOrDefault(value => value.SpiritUid == ownerUid);
        var ownerSprite = owner == null
            ? null
            : SpiritPortraitUi.Resolve(owner.Snapshot, "spirit.artifact.owner-avatar");
        cell.Bind(
            artifact,
            ArtifactSprite(artifact),
            CardBackgroundSprite(artifact.Rarity),
            focused,
            batchSelected,
            presetProtected,
            ownerUid.Length > 0,
            ownerSprite);
    }

    private static void SelectArtifact(string uid)
    {
        if (BatchSelection.IsActive)
        {
            if (presetProtectedUids.Contains(uid))
            {
                PlayerApi.ShowCaption("该圣遗物受到玩家预设保护，不能加入清理选择。");
                return;
            }
            BatchSelection.Toggle(uid);
            var change = Selection.Select(uid);
            if (change.Changed)
            {
                RenderPreview(boundCollection, CurrentSpirit(), animate: true);
                SynchronizeCarouselFocus(CurrentSpirit(), animate: true);
            }
            grid?.SetSelectionState(Selection.SelectedArtifactUid, BatchSelection.SelectedArtifactUids);
            RebuildActions(boundCollection, CurrentSpirit());
            TerriasPerformanceCounters.Record("SpiritArtifact.Ui.BatchSelection.Changed");
            return;
        }
        ApplySelectionChange(Selection.Toggle(uid), animate: true);
    }

    private static void SelectEquipmentSlot(string slotId, string artifactUid)
    {
        if (string.IsNullOrWhiteSpace(artifactUid))
        {
            ApplySelectionChange(Selection.Clear(), animate: true);
            equipmentCarousel?.Focus(slotId, hold: false, animate: true);
            return;
        }
        ApplySelectionChange(Selection.Toggle(artifactUid), animate: true);
    }

    private static void ClearArtifactSelection()
    {
        var change = Selection.Clear();
        if (change.Changed) ApplySelectionChange(change, animate: true);
        else equipmentCarousel?.Resume();
        if (BatchSelection.IsActive)
            grid?.SetSelectionState(Selection.SelectedArtifactUid, BatchSelection.SelectedArtifactUids);
    }

    private static void ChangeCategory(int nextCategory)
    {
        Selection.Clear();
        equipmentCarousel?.Resume();
        category = Math.Max(0, Math.Min(SpiritArtifactSlots.All.Count, nextCategory));
        Refresh(true);
    }

    private static void ApplySelectionChange(SpiritArtifactSelectionChange change, bool animate)
    {
        if (!change.Changed) return;
        var spirit = CurrentSpirit();
        grid?.SetSelectionState(change.CurrentUid, BatchSelection.SelectedArtifactUids);
        RenderPreview(boundCollection, spirit, animate);
        RebuildActions(boundCollection, spirit);
        SynchronizeCarouselFocus(spirit, animate);
        TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Selection.Changed");
    }

    private static void RenderPreview(
        SpiritCollectionDocument collection,
        SpiritInstance? spirit,
        bool animate)
    {
        if (preview == null) return;
        var selected = collection.ArtifactInventory.Artifacts.FirstOrDefault(
            value => value.ArtifactUid == Selection.SelectedArtifactUid);
        var shouldAnimate = animate && previewInitialized;
        if (selected == null) preview.BindSummary(collection, spirit, shouldAnimate);
        else preview.BindArtifact(selected, collection, shouldAnimate);
        previewInitialized = true;
    }

    private static SpiritInstance? CurrentSpirit()
    {
        var uid = selectedSpiritUid?.Invoke() ?? "";
        return boundCollection.Instances.FirstOrDefault(value => value.SpiritUid == uid);
    }

    private static void SynchronizeCarouselFocus(SpiritInstance? spirit, bool animate)
    {
        var selectedUid = Selection.SelectedArtifactUid;
        if (selectedUid.Length == 0 || spirit?.ArtifactLoadout == null)
        {
            equipmentCarousel?.Resume();
            return;
        }
        foreach (var slotId in SpiritArtifactSlots.All)
        {
            if (!string.Equals(spirit.ArtifactLoadout.Get(slotId), selectedUid, StringComparison.Ordinal)) continue;
            equipmentCarousel?.Focus(slotId, hold: true, animate: animate);
            return;
        }
        equipmentCarousel?.Resume();
    }

    private static void DrawTen()
    {
        if (BatchSelection.IsActive)
        {
            PlayerApi.ShowCaption("请先完成圣遗物多选操作。");
            return;
        }
        ClearArtifactSelection();
        var result = SpiritArtifactApplicationService.DrawTen();
        if (!result.Success) { PlayerApi.ShowCaption(result.Reason); return; }
        SpiritArtifactWishPresenter.Play(result.Token, result.Artifacts, () => Refresh(true));
    }

    private static void Apply(SpiritArtifactOperationResult result)
    {
        PlayerApi.ShowCaption(result.Success ? "圣遗物操作成功。" : result.Reason);
        if (result.Success) Refresh(false);
    }

    private static void OpenFilter()
    {
        if (root == null) return;
        CloseTargetSelector();
        SpiritArtifactPresetPanel.Close();
        CloseFilter();
        pendingFilter = InventoryFilter.Clone();
        filterOverlay = TerriasModalHost.CreateFullscreenRoot(
            "ArtifactFilterOverlay",
            root.transform,
            new Color(0f, 0f, 0f, 0.62f));
        var overlayLayout = filterOverlay.AddComponent<LayoutElement>();
        overlayLayout.ignoreLayout = true;
        var overlayRect = (RectTransform)filterOverlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.pivot = Vector2.zero;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        filterOverlay.AddComponent<SpiritArtifactSelectionDismissSurface>().Configure(CloseFilter);
        filterOverlay.AddComponent<SpiritArtifactEscapeHandler>().Configure(CloseFilter);

        var workspaceRect = (RectTransform)root.transform;
        var workspaceWidth = workspaceRect.rect.width > 1f ? workspaceRect.rect.width : panelWidth;
        var workspaceHeight = workspaceRect.rect.height > 1f ? workspaceRect.rect.height : 560f;
        filterPanel = TerriasUiComponents.CreateRect(
            "ArtifactFilterPanel",
            filterOverlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(Mathf.Clamp(workspaceWidth - 40f, 500f, 620f), Mathf.Clamp(workspaceHeight - 32f, 420f, 500f)));
        ApplyPanel(filterPanel, new Color(0.02f, 0.03f, 0.05f, 0.995f));
        filterPanel.AddComponent<SpiritArtifactPointerBlocker>();
        RebuildFilterPanel();
        filterPanel.transform.SetAsLastSibling();
    }

    private static void RebuildFilterPanel()
    {
        if (filterPanel == null || pendingFilter == null) return;
        Clear(filterPanel.transform);
        TerriasUiComponents.ConfigureVerticalLayout(filterPanel, new RectOffset(16, 16, 12, 12), 8f);
        Add(filterPanel.transform, "筛选圣遗物", 20, Gold, 32f);
        CreateFilterButtonRow(
            filterPanel.transform,
            "星级",
            new[] { "全部", "1★", "2★", "3★" },
            index => index == 0 ? pendingFilter.RarityMask == 0 : (pendingFilter.RarityMask & (1 << index)) != 0,
            index =>
            {
                if (index == 0) pendingFilter.RarityMask = 0;
                else pendingFilter.RarityMask ^= 1 << index;
                RebuildFilterPanel();
            });
        CreateFilterButtonRow(
            filterPanel.transform,
            "等级",
            new[] { "全部", "Lv.1", "Lv.2–4", "Lv.5" },
            index => pendingFilter.LevelBand == index,
            index => { pendingFilter.LevelBand = index; RebuildFilterPanel(); });

        Add(filterPanel.transform, "套装", 12, Muted, 22f);
        var setGrid = TerriasUiComponents.CreateUniformGridScrollArea(
            filterPanel.transform,
            "FilterSets",
            112f,
            0f,
            3,
            new Vector2(146f, 30f),
            new Vector2(8f, 6f),
            new RectOffset(2, 2, 2, 2),
            20f,
            new Color(0f, 0f, 0f, 0.08f));
        CreateFilterChoice(setGrid.Content, "全部套装", pendingFilter.SetId.Length == 0, () =>
        {
            pendingFilter.SetId = "";
            RebuildFilterPanel();
        }, new Vector2(146f, 30f));
        foreach (var set in SpiritArtifactRegistry.Sets())
        {
            var captured = set.Id;
            CreateFilterChoice(setGrid.Content, SpiritArtifactRegistry.Name(set),
                string.Equals(pendingFilter.SetId, captured, StringComparison.Ordinal), () =>
                {
                    pendingFilter.SetId = captured;
                    RebuildFilterPanel();
                }, new Vector2(146f, 30f));
        }

        Add(filterPanel.transform, "主词条", 12, Muted, 22f);
        var statRow = Layout("FilterStats", filterPanel.transform, 34f);
        TerriasUiComponents.ConfigureHorizontalLayout(statRow, new RectOffset(0, 0, 0, 0), 6f);
        var stats = new[] { "", SpiritArtifactStats.Life, SpiritArtifactStats.Magic, SpiritArtifactStats.Spirit,
            SpiritArtifactStats.Luck, SpiritArtifactStats.Perception, SpiritArtifactStats.Speed };
        foreach (var stat in stats)
        {
            var captured = stat;
            CreateFilterChoice(statRow.transform,
                captured.Length == 0 ? "全部" : SpiritArtifactStats.DisplayName(captured),
                string.Equals(pendingFilter.MainStatId, captured, StringComparison.Ordinal), () =>
                {
                    pendingFilter.MainStatId = captured;
                    RebuildFilterPanel();
                }, new Vector2(60f, 32f));
        }

        var footer = Layout("FilterFooter", filterPanel.transform, 38f);
        TerriasUiComponents.ConfigureHorizontalLayout(footer, new RectOffset(0, 0, 2, 2), 8f);
        CreateFilterChoice(
            footer.transform,
            pendingFilter.CleanableOnly ? "✓ 仅可清理" : "仅可清理",
            pendingFilter.CleanableOnly,
            () => { pendingFilter.CleanableOnly = !pendingFilter.CleanableOnly; RebuildFilterPanel(); },
            new Vector2(110f, 34f));
        Add(footer.transform, "", 10, Muted, 34f, 1f);
        TerriasUiComponents.CreateTextButton(footer.transform, "重置", new Vector2(72f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Filter]"), Item, Pale, 12, () =>
            {
                pendingFilter.Reset();
                RebuildFilterPanel();
            });
        TerriasUiComponents.CreateTextButton(footer.transform, "取消", new Vector2(72f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Filter]"), Item, Pale, 12, CloseFilter);
        TerriasUiComponents.CreateTextButton(footer.transform, "应用", new Vector2(82f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Filter]"), Selected, Pale, 13, ApplyFilter);
    }

    private static void CreateFilterButtonRow(
        Transform parent,
        string label,
        IReadOnlyList<string> labels,
        Func<int, bool> selected,
        Action<int> choose)
    {
        var row = Layout("Filter-" + label, parent, 34f);
        TerriasUiComponents.ConfigureHorizontalLayout(row, new RectOffset(0, 0, 0, 0), 8f);
        Add(row.transform, label, 12, Muted, 32f, 0f, 64f);
        for (var index = 0; index < labels.Count; index++)
        {
            var captured = index;
            CreateFilterChoice(row.transform, labels[index], selected(index), () => choose(captured), new Vector2(88f, 32f));
        }
    }

    private static void CreateFilterChoice(
        Transform parent,
        string label,
        bool active,
        Action action,
        Vector2 size)
    {
        TerriasUiComponents.CreateTextButton(parent, label, size,
            TerriasUiSprites.Button("[SpiritArtifact.Filter]"),
            active ? Selected : Item,
            active ? Pale : Muted,
            11,
            action);
    }

    private static void ApplyFilter()
    {
        if (pendingFilter == null) return;
        InventoryFilter.RarityMask = pendingFilter.RarityMask;
        InventoryFilter.LevelBand = pendingFilter.LevelBand;
        InventoryFilter.SetId = pendingFilter.SetId;
        InventoryFilter.MainStatId = pendingFilter.MainStatId;
        InventoryFilter.CleanableOnly = pendingFilter.CleanableOnly;
        CloseFilter();
        Refresh(true);
    }

    private static void CloseFilter()
    {
        filterPanel = null;
        pendingFilter = null;
        TerriasModalHost.Close(ref filterOverlay, "SpiritArtifact.Filter.Close", "[SpiritArtifact]");
    }

    private static void OpenDismantleConfirmation()
    {
        if (root == null || !BatchSelection.IsActive) return;
        var summary = SpiritArtifactInventoryQueryService.Summarize(boundCollection, BatchSelection.SelectedArtifactUids);
        if (summary.SelectedCount == 0 || summary.CleanableCount != summary.SelectedCount) return;
        CloseDismantleConfirmation();
        dismantleOverlay = TerriasModalHost.CreateFullscreenRoot(
            "ArtifactDismantleOverlay",
            root.transform,
            new Color(0f, 0f, 0f, 0.68f));
        var overlayLayout = dismantleOverlay.AddComponent<LayoutElement>();
        overlayLayout.ignoreLayout = true;
        dismantleOverlay.AddComponent<SpiritArtifactSelectionDismissSurface>().Configure(CloseDismantleConfirmation);
        dismantleOverlay.AddComponent<SpiritArtifactEscapeHandler>().Configure(CloseDismantleConfirmation);
        var panel = TerriasUiComponents.CreateRect(
            "ArtifactDismantleConfirmation",
            dismantleOverlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(430f, 220f));
        ApplyPanel(panel, new Color(0.02f, 0.03f, 0.05f, 0.995f));
        panel.AddComponent<SpiritArtifactPointerBlocker>();
        TerriasUiComponents.ConfigureVerticalLayout(panel, new RectOffset(18, 18, 16, 16), 10f);
        Add(panel.transform, "确认分解圣遗物", 20, Gold, 34f);
        Add(panel.transform,
            "即将永久分解 " + summary.SelectedCount + " 件圣遗物，预计获得 "
            + summary.EstimatedEssence + " 精粹。该操作无法撤销。",
            14,
            Pale,
            72f);
        var footer = Layout("DismantleFooter", panel.transform, 38f);
        TerriasUiComponents.ConfigureHorizontalLayout(footer, new RectOffset(0, 0, 2, 2), 10f);
        Add(footer.transform, "", 10, Muted, 34f, 1f);
        TerriasUiComponents.CreateTextButton(footer.transform, "取消", new Vector2(92f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Dismantle]"), Item, Pale, 13, CloseDismantleConfirmation);
        TerriasUiComponents.CreateTextButton(footer.transform, "确认分解", new Vector2(112f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Dismantle]"), new Color(0.42f, 0.12f, 0.12f, 1f), Pale, 13,
            ConfirmBatchDismantle);
        panel.transform.SetAsLastSibling();
    }

    private static void ConfirmBatchDismantle()
    {
        var selected = BatchSelection.SelectedArtifactUids.ToArray();
        CloseDismantleConfirmation();
        var result = SpiritArtifactApplicationService.Dismantle(selected);
        PlayerApi.ShowCaption(result.Success
            ? "已分解 " + result.Artifacts.Count + " 件圣遗物，获得 " + result.EssenceDelta + " 精粹。"
            : result.Reason);
        if (result.Success)
        {
            BatchSelection.Exit();
            Selection.Clear();
            Refresh(true);
        }
        else
        {
            Refresh(false);
        }
    }

    private static void CloseDismantleConfirmation()
    {
        TerriasModalHost.Close(ref dismantleOverlay, "SpiritArtifact.Dismantle.Close", "[SpiritArtifact]");
    }

    private static void OpenTargetSelector()
    {
        if (root == null) return;
        if (BatchSelection.IsActive)
        {
            PlayerApi.ShowCaption("请先完成圣遗物多选操作。");
            return;
        }
        ClearArtifactSelection();
        CloseTargetSelector();
        targetOverlay = TerriasModalHost.CreateFullscreenRoot(
            "ArtifactTargetOverlay",
            root.transform,
            new Color(0f, 0f, 0f, 0.62f));
        var overlayLayout = targetOverlay.AddComponent<LayoutElement>();
        overlayLayout.ignoreLayout = true;
        var overlayRect = (RectTransform)targetOverlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.pivot = Vector2.zero;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        targetOverlay.AddComponent<SpiritArtifactSelectionDismissSurface>()
            .Configure(CloseTargetSelector);
        targetOverlay.AddComponent<SpiritArtifactEscapeHandler>().Configure(CloseTargetSelector);

        var workspaceRect = (RectTransform)root.transform;
        var workspaceWidth = workspaceRect.rect.width > 1f ? workspaceRect.rect.width : panelWidth;
        var workspaceHeight = workspaceRect.rect.height > 1f ? workspaceRect.rect.height : 560f;
        var selectorLayout = SpiritArtifactTargetSelectorLayoutPolicy.Calculate(
            workspaceWidth,
            workspaceHeight);
        targetSelector = TerriasUiComponents.CreateRect("ArtifactTargetSelector", targetOverlay.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(selectorLayout.Width, selectorLayout.Height));
        ApplyPanel(targetSelector, new Color(0.02f, 0.03f, 0.05f, 0.995f));
        targetSelector.AddComponent<SpiritArtifactPointerBlocker>();
        TerriasUiComponents.ConfigureVerticalLayout(targetSelector, new RectOffset(14, 14, 12, 12), 8f);
        Add(targetSelector.transform, "选择目标套装", 20, Gold, 34f);
        var gridArea = TerriasUiComponents.CreateUniformGridScrollArea(targetSelector.transform, "Targets", selectorLayout.GridHeight, 1f,
            selectorLayout.Columns, new Vector2(selectorLayout.CellWidth, selectorLayout.CellHeight),
            new Vector2(8f, 8f), new RectOffset(4, 4, 4, 4), 24f,
            new Color(0f, 0f, 0f, 0.08f));
        var inventory = boundCollection.ArtifactInventory;
        foreach (var pool in SpiritArtifactRegistry.Pools())
        foreach (var setId in pool.SetIds)
        {
            var set = SpiritArtifactRegistry.Set(setId);
            var piece = SpiritArtifactRegistry.Piece(set?.RepresentativePieceId);
            var button = TerriasUiComponents.CreateTextButton(gridArea.Content, SpiritArtifactRegistry.Name(set),
                new Vector2(selectorLayout.CellWidth, selectorLayout.CellHeight), TerriasUiSprites.Button("[SpiritArtifact.Target]"),
                string.Equals(setId, inventory.TargetSetId, StringComparison.Ordinal) ? Selected : Item,
                Pale, 12, () =>
                {
                    var result = SpiritArtifactApplicationService.SetTarget(pool.Id, setId);
                    if (!result.Success)
                    {
                        PlayerApi.ShowCaption(result.Reason);
                        return;
                    }
                    CloseTargetSelector();
                    Refresh(false);
                });
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(0.5f, 0f);
                labelRect.sizeDelta = new Vector2(0f, 26f);
                labelRect.anchoredPosition = Vector2.zero;
                label.alignment = TextAnchor.MiddleCenter;
                label.fontStyle = FontStyle.Bold;
            }
            if (piece != null)
            {
                var icon = TerriasUiComponents.CreateRect("Flower", button.transform,
                    new Vector2(0.5f, 0.63f), new Vector2(0.5f, 0.63f), new Vector2(0.5f, 0.5f), new Vector2(64f, 64f));
                var image = icon.AddComponent<Image>();
                image.sprite = TerriasResourceCache.Load<Sprite>(piece.IconPath, true, "spirit.artifact.target-selector");
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
        }
        TerriasUiComponents.CreateTextButton(targetSelector.transform, "关闭", new Vector2(100f, 34f),
            TerriasUiSprites.Button("[SpiritArtifact.Target]"), Item, Pale, 13, () =>
            {
                CloseTargetSelector();
            });
        targetSelector.transform.SetAsLastSibling();
    }

    private static void CloseTargetSelector()
    {
        targetSelector = null;
        TerriasModalHost.Close(ref targetOverlay, "SpiritArtifact.TargetSelector.Close", "[SpiritArtifact]");
    }

    internal static Sprite? ArtifactSprite(SpiritArtifactInstance artifact)
    {
        var piece = SpiritArtifactRegistry.Piece(artifact.PieceId);
        return piece == null ? null : TerriasResourceCache.Load<Sprite>(piece.IconPath, true, "spirit.artifact.icon");
    }

    private static Sprite? EmptySlotSprite(string slot)
    {
        var id = SpiritArtifactSlots.Normalize(slot) switch
        {
            SpiritArtifactSlots.Flower => "spirit.artifact.empty.flower",
            SpiritArtifactSlots.Plume => "spirit.artifact.empty.plume",
            SpiritArtifactSlots.Sands => "spirit.artifact.empty.sands",
            SpiritArtifactSlots.Goblet => "spirit.artifact.empty.goblet",
            SpiritArtifactSlots.Circlet => "spirit.artifact.empty.circlet",
            _ => ""
        };
        if (id.Length == 0) return null;
        var fileName = SpiritArtifactSlots.DisplayName(slot) + ".png";
        var fallback = "Mods/Terrias/ModResource/Images/Artifacts/空素材/" + fileName;
        var path = VisualRegistry.TexturePath(id, fallback) ?? fallback;
        return TerriasResourceCache.Load<Sprite>(path, true, "spirit.artifact.empty-slot");
    }

    private static CanvasGroup CreateCardFrame(
        Transform parent,
        string name,
        float thickness,
        Color color)
    {
        var frame = TerriasUiComponents.CreateFillRect(name, parent);
        var stroke = frame.AddComponent<SpiritArtifactRoundedStrokeGraphic>();
        stroke.Thickness = thickness;
        stroke.Radius = SpiritArtifactCardStylePolicy.CardRadius;
        stroke.color = color;
        stroke.raycastTarget = false;
        var group = frame.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        frame.SetActive(false);
        return group;
    }

    private static GameObject CreateCornerBadge(
        Transform parent,
        string name,
        Vector2 anchor,
        Vector2 offset,
        Vector2 size,
        Color color)
    {
        var badge = TerriasUiComponents.CreateRect(
            name,
            parent,
            anchor,
            anchor,
            anchor,
            size);
        ((RectTransform)badge.transform).anchoredPosition = offset;
        var image = badge.AddComponent<Image>();
        image.sprite = TerriasUiSprites.RoundedSolid(
            "spirit-artifact-" + name.ToLowerInvariant(),
            Mathf.RoundToInt(size.x),
            Mathf.RoundToInt(size.y),
            4f,
            Color.white);
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return badge;
    }

    internal static Sprite CardBackgroundSprite(int rarity)
    {
        Color top;
        Color bottom;
        if (rarity >= 3)
        {
            top = new Color(0.35f, 0.64f, 0.79f, 1f);
            bottom = new Color(0.16f, 0.38f, 0.60f, 1f);
        }
        else if (rarity == 2)
        {
            top = new Color(0.39f, 0.69f, 0.52f, 1f);
            bottom = new Color(0.22f, 0.48f, 0.34f, 1f);
        }
        else
        {
            top = new Color(0.57f, 0.62f, 0.67f, 1f);
            bottom = new Color(0.37f, 0.43f, 0.51f, 1f);
        }
        return TerriasUiSprites.RoundedGradientCorners(
            "spirit-artifact-rarity-" + Math.Max(1, Math.Min(3, rarity)),
            72,
            Mathf.RoundToInt(SpiritArtifactCardStylePolicy.ArtHeight),
            SpiritArtifactCardStylePolicy.CardRadius,
            SpiritArtifactCardStylePolicy.CardRadius,
            SpiritArtifactCardStylePolicy.ArtBottomRightRadius,
            1f,
            top,
            bottom);
    }

    private static Color RarityColor(int rarity) => rarity >= 3 ? RarityThree : rarity == 2 ? RarityTwo : RarityOne;

    private static Text Add(Transform parent, string value, int size, Color color, float height, float flexible = 0f, float width = 0f)
        => TerriasUiComponents.AddTextBlock(parent, value, size, TextAnchor.MiddleLeft, color, height, flexible, width);

    private static GameObject Layout(string name, Transform parent, float height, float flexible = 0f, float width = 0f)
    {
        var go = TerriasUiComponents.CreateFillRect(name, parent);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.flexibleHeight = flexible;
        if (width > 0f) { element.preferredWidth = width; element.minWidth = width; }
        return go;
    }

    private static void ApplyPanel(GameObject go, Color color)
    {
        var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.sprite = TerriasUiSprites.Panel("[SpiritArtifact]");
        image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        image.color = color;
    }

    private static Text OverlayText(Transform parent, string name, Vector2 anchor, Vector2 pivot,
        Vector2 size, Vector2 offset, int fontSize, Color color)
    {
        var go = TerriasUiComponents.CreateRect(name, parent, anchor, anchor, pivot, size);
        var rect = go.transform as RectTransform;
        if (rect != null) rect.anchoredPosition = offset;
        var text = TerriasUiComponents.ConfigureText(go, "", fontSize, TextAnchor.MiddleCenter, color);
        text.raycastTarget = false;
        return text;
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
}
