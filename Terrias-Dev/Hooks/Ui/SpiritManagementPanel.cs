using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using AuraUi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Terrias.Dll.Hooks.Ui;

public static class SpiritManagementPanel
{
    private enum PanelMode { Adventure, Warehouse }

    private const string PanelName = "Terrias_SpiritManagementPanel";
    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.82f);
    private static readonly Color WindowTint = new(0.018f, 0.026f, 0.046f, 0.99f);
    private static readonly Color BandTint = new(0.035f, 0.052f, 0.078f, 0.98f);
    private static readonly Color ItemTint = new(0.052f, 0.073f, 0.105f, 0.98f);
    private static readonly Color SelectedTint = new(0.105f, 0.205f, 0.235f, 0.99f);
    private static readonly Color TargetTint = new(0.183f, 0.139f, 0.061f, 0.99f);
    private static readonly Color Gold = new(0.95f, 0.76f, 0.34f);
    private static readonly Color Pale = new(0.90f, 0.94f, 0.97f);
    private static readonly Color Muted = new(0.62f, 0.70f, 0.77f);
    private static readonly Color Cyan = new(0.35f, 0.84f, 0.90f);
    private static readonly Color Green = new(0.45f, 0.88f, 0.65f);
    private static readonly Color SelectionStroke = new(0.514f, 0.843f, 0.871f, 1f);
    private static readonly Color TargetStroke = new(0.95f, 0.76f, 0.34f, 0.94f);
    private static readonly Color QualityGray = new(0.204f, 0.216f, 0.239f, 0.98f);
    private static readonly Color QualityWhite = new(0.278f, 0.302f, 0.322f, 0.98f);
    private static readonly Color QualityGreen = new(0.188f, 0.294f, 0.239f, 0.98f);
    private static readonly Color QualityBlue = new(0.196f, 0.259f, 0.345f, 0.98f);
    private static readonly Color QualityPurple = new(0.290f, 0.216f, 0.345f, 0.98f);
    private static readonly Color QualityGold = new(0.357f, 0.302f, 0.200f, 0.98f);
    private static readonly Color QualityRed = new(0.373f, 0.208f, 0.224f, 0.98f);
    private static readonly Color DisabledCardTint = new(0.075f, 0.078f, 0.085f, 0.96f);
    private static readonly Color StarGold = new(0.88f, 0.72f, 0.38f, 0.96f);
    private static readonly Color ActiveStampFill = new(0.33f, 0.22f, 0.08f, 0.34f);
    private static readonly Color ActiveStampOuter = new(0.94f, 0.72f, 0.30f, 0.72f);
    private static readonly Color ActiveStampInner = new(0.94f, 0.72f, 0.30f, 0.40f);
    private static readonly Color ActiveStampText = new(0.98f, 0.82f, 0.48f, 0.96f);

    private static GameObject? root;
    private static Transform? gridContent;
    private static Transform? previewContent;
    private static Transform? detailContent;
    private static Transform? partyContent;
    private static Transform? actionContent;
    private static PanelMode mode;
    private static string selectedUid = "";
    private static int warehouseFilter;
    private static int warehouseSort;
    private static int detailTab;
    private static readonly SpiritTrainingSelectionState TrainingSelection = new();
    private static bool guiyuanSelectingDonors;
    private static string guiyuanTargetUid = "";
    private static readonly HashSet<string> GuiyuanDonorUids = new(StringComparer.Ordinal);
    private static bool guiyuanConfirmArmed;

    public static bool IsOpen => root != null;

    public static void OpenAdventure() => Open(PanelMode.Adventure);

    public static void OpenWarehouse() => Open(PanelMode.Warehouse);

    public static void Close()
    {
        ClearChildren(gridContent);
        ClearChildren(previewContent);
        ClearChildren(detailContent);
        ClearChildren(partyContent);
        ClearChildren(actionContent);
        TerriasModalHost.Close(ref root, "SpiritManagementPanel.Close", "[SpiritManagement]");
        gridContent = null;
        previewContent = null;
        detailContent = null;
        partyContent = null;
        actionContent = null;
        ResetGuiyuanSelection();
    }

    private static void Open(PanelMode requestedMode)
    {
        try
        {
            Close();
            mode = requestedMode;
            warehouseFilter = 0;
            warehouseSort = 0;
            detailTab = 0;
            TrainingSelection.Reset();
            ResetGuiyuanSelection();
            var party = Party();
            if (mode == PanelMode.Adventure)
            {
                var collectionCount = SpiritCollectionApi.Collection().Instances.Count;
                TerriasLog.Info("[SpiritManagement] adventure panel opened; collection="
                                + collectionCount
                                + ", carried="
                                + party.PartySlots.Count(uid => !string.IsNullOrWhiteSpace(uid))
                                + ", active="
                                + (!string.IsNullOrWhiteSpace(party.ActiveSpiritUid))
                                + ".");
            }
            selectedUid = party.ActiveSpiritUid;
            if (string.IsNullOrWhiteSpace(selectedUid))
            {
                selectedUid = party.PartySlots.FirstOrDefault(uid => !string.IsNullOrWhiteSpace(uid))
                              ?? (mode == PanelMode.Warehouse
                                  ? SpiritCollectionApi.Collection().Instances.FirstOrDefault()?.SpiritUid
                                  : null)
                              ?? "";
            }
            Build();
        }
        catch (Exception ex)
        {
            Close();
            TerriasLog.Error("Spirit management panel failed", ex);
        }
    }

    private static void Build()
    {
        var parent = TerriasModalHost.ModalParent();
        if (parent == null) return;
        root = TerriasModalHost.CreateFullscreenRoot(PanelName, parent, Backdrop);
        TerriasLocalizationScope.Attach(root).RegisterRefresh(Refresh);
        var windowSize = ResolveWindowSize();
        var window = TerriasUiComponents.CreateRect(
            "Window",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            windowSize);
        ApplyPanel(window, WindowTint);
        TerriasUiComponents.ConfigureVerticalLayout(window, new RectOffset(20, 20, 16, 16), 12f);

        var header = LayoutObject("Header", window.transform, 46f);
        ApplyPanel(header, BandTint);
        TerriasUiComponents.AddTextBlock(
            header.transform,
            mode == PanelMode.Adventure ? "精灵背包" : "精灵仓库",
            24,
            TextAnchor.MiddleCenter,
            Gold,
            46f,
            1f);

        var body = LayoutObject("Body", window.transform, 390f, 1f);
        var bodyLayout = TerriasUiComponents.ConfigureHorizontalLayout(body, new RectOffset(0, 0, 0, 0), 12f);
        bodyLayout.childForceExpandHeight = true;

        if (mode == PanelMode.Warehouse)
        {
            var rosterWidth = RosterWidth(windowSize);
            var left = LayoutObject("Roster", body.transform, 390f, 1f, rosterWidth);
            ApplyPanel(left, BandTint);
            TerriasUiComponents.ConfigureVerticalLayout(left, new RectOffset(10, 10, 10, 10), 8f);
            CreateFilterBar(left.transform);
            const int gridColumns = 3;
            var gridCellWidth = Mathf.Clamp((rosterWidth - 52f) / gridColumns, 76f, 132f);
            var grid = TerriasUiComponents.CreateUniformGridScrollArea(
                left.transform,
                "Spirits",
                260f,
                1f,
                gridColumns,
                new Vector2(gridCellWidth, 166f),
                new Vector2(8f, 8f),
                new RectOffset(4, 4, 4, 4),
                28f,
                new Color(0f, 0f, 0f, 0.12f));
            gridContent = grid.Content;
        }

        var contentColumnWidth = mode == PanelMode.Warehouse
            ? Mathf.Clamp((windowSize.x - RosterWidth(windowSize) - 76f) * 0.38f, 210f, 380f)
            : Mathf.Clamp((windowSize.x - 52f) * 0.45f, 320f, 570f);
        var preview = LayoutObject("Preview", body.transform, 390f, 1f, contentColumnWidth);
        previewContent = preview.transform;
        CreatePreviewShell(preview.transform);

        var detailWidth = mode == PanelMode.Warehouse
            ? Mathf.Max(196f, windowSize.x - 64f - RosterWidth(windowSize) - contentColumnWidth)
            : Mathf.Max(320f, windowSize.x - 52f - contentColumnWidth);
        var detail = LayoutObject("Detail", body.transform, 390f, 1f, detailWidth);
        ApplyPanel(detail, BandTint);
        TerriasUiComponents.ConfigureVerticalLayout(detail, new RectOffset(14, 14, 12, 12), 4f);
        detailContent = detail.transform;

        var partyBand = LayoutObject("PartyBand", window.transform, 142f);
        ApplyPanel(partyBand, BandTint);
        TerriasUiComponents.ConfigureVerticalLayout(partyBand, new RectOffset(10, 10, 7, 7), 5f);
        TerriasUiComponents.AddTextBlock(
            partyBand.transform,
            mode == PanelMode.Adventure
                ? "本次旅程编队 · 调整将在下一场战斗生效"
                : "下次旅程编队 · 通过下方按钮加入或移除精灵",
            13,
            TextAnchor.MiddleLeft,
            Gold,
            24f);
        var slots = LayoutObject("Slots", partyBand.transform, 98f);
        TerriasUiComponents.ConfigureHorizontalLayout(slots, new RectOffset(0, 0, 0, 0), 8f);
        partyContent = slots.transform;

        var actions = LayoutObject("Actions", window.transform, 42f);
        ApplyPanel(actions, BandTint);
        TerriasUiComponents.ConfigureHorizontalLayout(actions, new RectOffset(8, 8, 4, 4), 10f);
        actionContent = actions.transform;
        Refresh();
    }

    private static void Refresh()
    {
        RefreshGrid();
        RefreshPreviewAndDetail();
        RefreshParty();
        RefreshActions();
    }

    private static void RefreshGrid()
    {
        if (gridContent == null) return;
        ClearChildren(gridContent);
        var collection = SpiritCollectionApi.Collection();
        var party = Party();
        var carried = new HashSet<string>(party.PartySlots.Where(uid => !string.IsNullOrWhiteSpace(uid)), StringComparer.Ordinal);
        var forbiddenDonors = guiyuanSelectingDonors ? GuiyuanForbiddenUids() : new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<SpiritInstance> items = collection.Instances;
        if (!guiyuanSelectingDonors)
        {
            if (mode == PanelMode.Adventure) items = items.Where(item => carried.Contains(item.SpiritUid));
            else if (warehouseFilter == 1) items = items.Where(item => carried.Contains(item.SpiritUid));
            else if (warehouseFilter == 2) items = items.Where(item => !carried.Contains(item.SpiritUid));
        }
        IOrderedEnumerable<SpiritInstance> ordered = warehouseSort switch
        {
            1 => items.OrderByDescending(item => item.Aptitude).ThenByDescending(item => item.Level),
            2 => items.OrderByDescending(SpiritGrowthRegistry.TierFor).ThenByDescending(item => item.Level),
            3 => items.OrderByDescending(item => item.CapturedAt, StringComparer.Ordinal),
            _ => items.OrderByDescending(item => item.Level).ThenByDescending(item => item.Aptitude)
        };
        foreach (var item in ordered.ThenBy(item => SpiritPresentationResolver.Name(item), StringComparer.Ordinal))
        {
            CreateSpiritCell(gridContent, item, carried.Contains(item.SpiritUid), Same(item.SpiritUid, party.ActiveSpiritUid), forbiddenDonors);
        }
        if (gridContent.childCount == 0)
        {
            TerriasUiComponents.AddTextBlock(gridContent, "暂无精灵", 15, TextAnchor.MiddleCenter, Pale, 90f);
        }
    }

    private static void CreateSpiritCell(
        Transform parent,
        SpiritInstance item,
        bool carried,
        bool active,
        IReadOnlyCollection<string> forbiddenDonors)
    {
        var target = guiyuanSelectingDonors && Same(item.SpiritUid, guiyuanTargetUid);
        var sameSpecies = !guiyuanSelectingDonors || SameSpeciesAsGuiyuanTarget(item);
        var donorEligible = guiyuanSelectingDonors
                            && !target
                            && sameSpecies
                            && !item.Locked
                            && !forbiddenDonors.Contains(item.SpiritUid);
        var donorSelected = donorEligible && GuiyuanDonorUids.Contains(item.SpiritUid);
        var disabledForGuiyuan = guiyuanSelectingDonors && !donorEligible && !target;
        var cell = LayoutObject("Spirit-" + item.SpiritUid, parent, 166f);
        ApplyPanel(cell, disabledForGuiyuan ? DisabledCardTint : QualityTint(item.Aptitude), true);
        TerriasUiComponents.ConfigureVerticalLayout(cell, new RectOffset(6, 6, 6, 6), 2f, alignment: TextAnchor.MiddleCenter);
        if (donorSelected || target) AddTargetOutline(cell);
        else if (Same(item.SpiritUid, selectedUid)) AddSelectionOutline(cell);
        CreateCenteredPortrait(
            cell.transform,
            "Portrait",
            72f,
            68f,
            Portrait(item.Snapshot),
            new Color(0.18f, 0.20f, 0.24f, 1f));
        var markers = target ? L("ui.spirit.guiyuan.target")
            : donorSelected ? L("ui.spirit.guiyuan.selected", "value", SpiritAscensionService.ContributionOf(item).ToString())
            : guiyuanSelectingDonors && !sameSpecies ? L("ui.spirit.guiyuan.different_species")
            : guiyuanSelectingDonors && item.Locked ? L("ui.spirit.locked")
            : guiyuanSelectingDonors && forbiddenDonors.Contains(item.SpiritUid) ? L("ui.spirit.in_party")
            : guiyuanSelectingDonors ? L("ui.spirit.guiyuan.available", "value", SpiritAscensionService.ContributionOf(item).ToString())
            : active ? L("ui.spirit.active") : carried ? L("ui.spirit.carried") : L("ui.spirit.warehouse");
        TerriasUiComponents.AddTextBlock(cell.transform, SpiritPresentationResolver.Name(item), 13,
            TextAnchor.MiddleCenter, disabledForGuiyuan ? Muted : QualityAccent(item.Aptitude), 20f, 1f);
        TerriasUiComponents.AddTextBlock(cell.transform, StarText(item), 12,
            TextAnchor.MiddleCenter, disabledForGuiyuan ? Muted : StarGold, 18f, 1f);
        var meta = LayoutObject("Meta", cell.transform, 18f);
        TerriasUiComponents.ConfigureHorizontalLayout(meta, new RectOffset(2, 2, 0, 0), 4f);
        AddFixedTextBlock(meta.transform, "Lv." + item.Level, 11, TextAnchor.MiddleCenter,
            disabledForGuiyuan ? Muted : Pale, 18f, 1f);
        AddFixedTextBlock(meta.transform, L("ui.spirit.aptitude_value", "value", item.Aptitude.ToString()), 11, TextAnchor.MiddleCenter,
            disabledForGuiyuan ? Muted : QualityAccent(item.Aptitude), 18f, 1f);
        TerriasUiComponents.AddTextBlock(cell.transform, markers,
            11, TextAnchor.MiddleCenter, donorSelected || target ? Gold : active ? Green : Muted, 16f, 1f);
        if (active) AddActiveStamp(cell.transform);
        var button = cell.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, cell.GetComponent<Image>(), Gold);
        button.interactable = !guiyuanSelectingDonors || donorEligible;
        button.onClick.AddListener(() =>
        {
            if (guiyuanSelectingDonors)
            {
                ToggleGuiyuanDonor(item.SpiritUid);
            }
            else
            {
                SelectSpirit(item.SpiritUid);
            }
            Refresh();
        });
    }

    private static void CreatePreviewShell(Transform parent)
    {
        ApplyPanel(parent.gameObject, new Color(0.025f, 0.052f, 0.072f, 0.96f));
    }

    private static void RefreshPreviewAndDetail()
    {
        if (previewContent == null || detailContent == null) return;
        ClearChildren(previewContent);
        ClearChildren(detailContent);
        var item = SpiritCollectionApi.Find(selectedUid);
        if (item == null)
        {
            TerriasUiComponents.AddTextBlock(detailContent, "请选择一只精灵", 17, TextAnchor.MiddleCenter, Pale, 80f);
            return;
        }

        var imageRoot = TerriasUiComponents.CreateRect("AnimatedPortrait", previewContent, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), new Vector2(0.5f, 0.5f), Vector2.zero);
        var image = imageRoot.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        imageRoot.AddComponent<SpiritPreviewAnimator>().Configure(item.Snapshot.IdlePath, Portrait(item.Snapshot));

        var growth = SpiritCollectionApi.GrowthView(item);
        var detailHeader = LayoutObject("DetailHeader", detailContent, 36f);
        TerriasUiComponents.ConfigureHorizontalLayout(detailHeader, new RectOffset(0, 0, 0, 0), 8f);
        AddFixedTextBlock(detailHeader.transform, SpiritPresentationResolver.Name(item), 23,
            TextAnchor.MiddleLeft, QualityAccent(item.Aptitude), 36f, 1f, FontStyle.Normal);
        AddFixedTextBlock(detailHeader.transform, "Lv." + item.Level, 19,
            TextAnchor.MiddleRight, Pale, 36f, 0f, FontStyle.Normal, 58f);
        AddFixedTextBlock(detailHeader.transform, StarText(item), 17,
            TextAnchor.MiddleRight, StarGold, 36f, 0f, FontStyle.Normal, 92f);
        var summary = LayoutObject("DetailSummary", detailContent, 24f);
        TerriasUiComponents.ConfigureHorizontalLayout(summary, new RectOffset(0, 0, 0, 0), 8f);
        AddFixedTextBlock(summary.transform,
            TierName(growth.Tier) + (string.IsNullOrWhiteSpace(growth.FormLabel) ? "" : " · " + TerriasTextCatalog.ResolveLegacy(growth.FormLabel)),
            13, TextAnchor.MiddleLeft, Muted, 24f, 1f);
        AddFixedTextBlock(summary.transform, L("ui.spirit.aptitude_value", "value", growth.Aptitude.ToString()), 13,
            TextAnchor.MiddleRight, QualityAccent(growth.Aptitude), 24f, 0f, FontStyle.Normal, 82f);
        CreateDetailTabs(detailContent);
        var scroll = TerriasUiComponents.CreateVerticalScrollArea(
            detailContent, "GrowthDetail", 230f, 1f, 5f, 24f, new Color(0f, 0f, 0f, 0.08f));
        if (detailTab == 0) BuildAttributeView(scroll.Content, item, growth);
        else if (detailTab == 1) BuildTrainingView(scroll.Content, item);
        else BuildGuiyuanView(scroll.Content, item, growth);
    }

    private static void RefreshParty()
    {
        if (partyContent == null) return;
        ClearChildren(partyContent);
        var party = Party();
        for (var slot = 0; slot < SpiritCollectionService.PartyCapacity; slot++)
        {
            var index = slot;
            var uid = party.PartySlots[index];
            var item = SpiritCollectionApi.Find(uid);
            var cell = LayoutObject("PartySlot-" + index, partyContent, 96f, 0f, PartySlotWidth());
            ApplyPanel(cell, item == null ? ItemTint : QualityTint(item.Aptitude), true);
            TerriasUiComponents.ConfigureVerticalLayout(cell, new RectOffset(5, 5, 4, 4), 2f, alignment: TextAnchor.MiddleCenter);
            if (Same(uid, selectedUid)) AddSelectionOutline(cell);
            CreateCenteredPortrait(
                cell.transform,
                "Icon",
                48f,
                46f,
                item == null ? null : Portrait(item.Snapshot),
                new Color(0.15f, 0.16f, 0.20f, 1f));
            if (item == null)
            {
                TerriasUiComponents.AddTextBlock(cell.transform, L("ui.spirit.party.empty_slot", "slot", (index + 1).ToString()), 12,
                    TextAnchor.MiddleCenter, Pale, 38f, 1f);
            }
            else
            {
                TerriasUiComponents.AddTextBlock(cell.transform, SpiritPresentationResolver.Name(item), 12,
                    TextAnchor.MiddleCenter, QualityAccent(item.Aptitude), 18f, 1f);
                TerriasUiComponents.AddTextBlock(cell.transform, "Lv." + item.Level + "  " + StarText(item), 11,
                    TextAnchor.MiddleCenter, StarGold, 18f, 1f);
            }
            if (item != null && Same(uid, party.ActiveSpiritUid)) AddActiveStamp(cell.transform);
            var button = cell.AddComponent<Button>();
            AuraUiButtonFeedback.Apply(button, cell.GetComponent<Image>(), Gold);
            button.onClick.AddListener(() => OnPartySlot(uid));
        }
    }

    private static void RefreshActions()
    {
        if (actionContent == null) return;
        ClearChildren(actionContent);
        if (guiyuanSelectingDonors)
        {
            RefreshGuiyuanActions();
            return;
        }
        var party = Party();
        var selected = SpiritCollectionApi.Find(selectedUid);
        TerriasUiComponents.AddTextBlock(actionContent,
            selected == null
                ? L("ui.spirit.none_selected")
                : mode == PanelMode.Adventure
                    ? L("ui.spirit.adventure_configuration", "name", SpiritPresentationResolver.Name(selected))
                    : SpiritPresentationResolver.Name(selected),
            14, TextAnchor.MiddleLeft, Muted, 34f, 1f);
        if (selected != null && party.PartySlots.Contains(selected.SpiritUid, StringComparer.Ordinal))
        {
            TerriasUiComponents.CreateTextButton(actionContent, "设为出战", new Vector2(112f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), SelectedTint, Pale, 14, SetActive);
        }
        if (mode == PanelMode.Warehouse && selected != null
            && !party.PartySlots.Contains(selected.SpiritUid, StringComparer.Ordinal))
        {
            var hasEmptySlot = party.PartySlots.Any(string.IsNullOrWhiteSpace);
            var addButton = TerriasUiComponents.CreateTextButton(
                actionContent,
                hasEmptySlot ? "加入携带" : "队伍已满",
                new Vector2(112f, 34f),
                TerriasUiSprites.Button("[SpiritManagement]"),
                BandTint,
                hasEmptySlot ? Pale : Muted,
                14,
                AddSelected);
            addButton.interactable = hasEmptySlot;
        }
        if (mode == PanelMode.Warehouse && selected != null && party.PartySlots.Contains(selected.SpiritUid, StringComparer.Ordinal))
        {
            TerriasUiComponents.CreateTextButton(actionContent, "放回仓库", new Vector2(124f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, RemoveSelected);
        }
        if (mode == PanelMode.Adventure && selected != null
            && party.PartySlots.Contains(selected.SpiritUid, StringComparer.Ordinal))
        {
            TerriasUiComponents.CreateTextButton(actionContent, "放回仓库", new Vector2(124f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, RemoveSelected);
        }
        TerriasUiComponents.CreateTextButton(actionContent, "关闭", new Vector2(96f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, Close);
    }

    private static void CreateFilterBar(Transform parent)
    {
        var bar = LayoutObject("Filters", parent, 34f);
        TerriasUiComponents.ConfigureHorizontalLayout(bar, new RectOffset(0, 0, 0, 0), 6f);
        TerriasUiComponents.CreateTextButton(bar.transform, FilterName(), new Vector2(98f, 32f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 12, () =>
        {
            warehouseFilter = (warehouseFilter + 1) % 3;
            Rebuild();
        });
        TerriasUiComponents.CreateTextButton(bar.transform, SortName(), new Vector2(98f, 32f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 12, () =>
        {
            warehouseSort = (warehouseSort + 1) % 4;
            Rebuild();
        });
    }

    private static void OnPartySlot(string currentUid)
    {
        if (!SpiritPartySlotInteraction.TrySelectOccupant(currentUid, out var occupantUid))
        {
            if (mode == PanelMode.Warehouse)
            {
                PlayerApi.ShowCaption(L("caption.spirit.empty_party_slot"));
            }
            return;
        }

        SelectSpirit(occupantUid);
        Refresh();
    }

    private static void SetActive()
    {
        if (mode == PanelMode.Adventure) SpiritCollectionApi.SetActiveForAdventure(selectedUid);
        else SpiritCollectionApi.SetDefaultActive(selectedUid);
        Refresh();
    }

    private static void RemoveSelected()
    {
        if (mode == PanelMode.Adventure)
        {
            if (SpiritCollectionApi.RemoveFromCurrentAdventureParty(selectedUid))
            {
                var party = SpiritCollectionApi.CurrentParty();
                var nextUid = party.ActiveSpiritUid;
                if (string.IsNullOrWhiteSpace(nextUid))
                {
                    nextUid = party.PartySlots.FirstOrDefault(uid => !string.IsNullOrWhiteSpace(uid)) ?? "";
                }
                SelectSpirit(nextUid);
            }
        }
        else
        {
            SpiritCollectionApi.RemoveFromDefaultParty(selectedUid);
        }
        Refresh();
    }

    private static void AddSelected()
    {
        SpiritCollectionApi.AddToDefaultParty(selectedUid);
        Refresh();
    }

    private static void Rebuild()
    {
        var rememberedMode = mode;
        var rememberedSelection = selectedUid;
        var rememberedFilter = warehouseFilter;
        var rememberedSort = warehouseSort;
        var rememberedTab = detailTab;
        var rememberedGuiyuanSelecting = guiyuanSelectingDonors;
        var rememberedGuiyuanTarget = guiyuanTargetUid;
        var rememberedGuiyuanDonors = GuiyuanDonorUids.ToArray();
        var rememberedGuiyuanConfirm = guiyuanConfirmArmed;
        Close();
        mode = rememberedMode;
        selectedUid = rememberedSelection;
        warehouseFilter = rememberedFilter;
        warehouseSort = rememberedSort;
        detailTab = rememberedTab;
        guiyuanSelectingDonors = rememberedGuiyuanSelecting;
        guiyuanTargetUid = rememberedGuiyuanTarget;
        foreach (var uid in rememberedGuiyuanDonors) GuiyuanDonorUids.Add(uid);
        guiyuanConfirmArmed = rememberedGuiyuanConfirm;
        Build();
    }

    private static SpiritAdventureParty Party()
    {
        return mode == PanelMode.Adventure ? SpiritCollectionApi.CurrentParty() : SpiritCollectionApi.DefaultParty();
    }

    private static void SelectSpirit(string? uid)
    {
        var normalized = (uid ?? "").Trim();
        if (!Same(selectedUid, normalized))
        {
            TrainingSelection.Reset();
            ResetGuiyuanSelection();
        }
        selectedUid = normalized;
    }

    private static void CreateDetailTabs(Transform parent)
    {
        var tabs = LayoutObject("DetailTabs", parent, 36f);
        TerriasUiComponents.ConfigureHorizontalLayout(tabs, new RectOffset(0, 0, 0, 0), 8f);
        CreateDetailTab(tabs.transform, "属性", 0);
        CreateDetailTab(tabs.transform, "养成", 1);
        CreateDetailTab(tabs.transform, "归元", 2);
    }

    private static void CreateDetailTab(Transform parent, string label, int index)
    {
        var selected = detailTab == index;
        var tab = TerriasUiComponents.CreateTextButton(
            parent,
            label,
            new Vector2(96f, 34f),
            TerriasUiSprites.Button("[SpiritManagement]"),
            selected ? SelectedTint : BandTint,
            selected ? Pale : Muted,
            14,
            () =>
            {
                if (index != 2) ResetGuiyuanSelection();
                detailTab = index;
                Refresh();
            });
        if (!selected) return;
        var underline = TerriasUiComponents.CreateRect(
            "ActiveUnderline",
            tab.transform,
            new Vector2(0.08f, 0f),
            new Vector2(0.92f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 3f));
        underline.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 1f);
        ApplyPanel(underline, Gold);
    }

    private static void BuildAttributeView(Transform parent, SpiritInstance item, SpiritGrowthViewSnapshot growth)
    {
        var overview = LayoutObject("AttributeOverview", parent, 218f);
        TerriasUiComponents.ConfigureHorizontalLayout(overview, new RectOffset(0, 0, 0, 0), 12f);
        var radarRoot = LayoutObject("Radar", overview.transform, 218f, 0f, 218f);
        ApplyPanel(radarRoot, new Color(0.025f, 0.050f, 0.070f, 0.94f));
        var radarSurface = TerriasUiComponents.CreateFillRect("RadarSurface", radarRoot.transform);
        var graphic = radarSurface.AddComponent<SpiritRadarGraphic>();
        graphic.color = Color.white;
        AddOverlayLabel(radarRoot.transform, "魔力", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(64f, 24f), new Vector2(0f, -4f));
        AddOverlayLabel(radarRoot.transform, "感知", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(60f, 24f), new Vector2(-4f, 0f));
        AddOverlayLabel(radarRoot.transform, "精神", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(64f, 24f), new Vector2(0f, 4f));
        AddOverlayLabel(radarRoot.transform, "幸运", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(60f, 24f), new Vector2(4f, 0f));
        var hover = TerriasUiComponents.AddTextBlock(radarRoot.transform, "", 1, TextAnchor.MiddleCenter, new Color(0f, 0f, 0f, 0f), 1f);
        graphic.Configure(growth.RadarAxes, index =>
        {
            hover.text = index.HasValue && index.Value < growth.RadarAxes.Count ? AxisSummary(growth.RadarAxes[index.Value]) : "";
        });

        var stats = LayoutObject("BattleStats", overview.transform, 218f, 0f);
        ApplyPanel(stats, ItemTint);
        TerriasUiComponents.ConfigureVerticalLayout(stats, new RectOffset(12, 12, 10, 10), 4f);
        CreateVerticalStat(stats.transform, "生命", growth.BattleStats.MaxHp);
        CreateVerticalStat(stats.transform, "攻击", growth.BattleStats.Attack);
        CreateVerticalStat(stats.transform, "护甲", growth.BattleStats.Armor);
        CreateVerticalStat(stats.transform, "魔能", growth.BattleStats.MaxMagic);
        CreateVerticalStat(stats.transform, "速度", growth.BattleStats.Speed);

        CreateExperienceProgress(parent, growth);
        var localizedDescription = SpiritPresentationResolver.Description(item);
        if (!string.IsNullOrWhiteSpace(localizedDescription))
        {
            TerriasUiComponents.AddTextBlock(parent, PlayerFacingDescription(localizedDescription), 12, TextAnchor.UpperLeft, Muted, 72f, 1f);
        }
    }

    private static void BuildTrainingView(Transform parent, SpiritInstance item)
    {
        var training = SpiritCollectionApi.TrainingView(item);
        TrainingSelection.EnsureInitialized(training.EquippedIntents.Select(ability => ability.Id).ToArray());
        var focused = ResolveFocusedAbility(training);
        var equipped = LayoutObject("EquippedIntents", parent, 64f);
        TerriasUiComponents.ConfigureHorizontalLayout(equipped, new RectOffset(0, 0, 0, 0), 8f);
        for (var index = 0; index < SpiritTrainingService.EquippedIntentCapacity; index++)
        {
            var slot = index;
            var ability = index < training.EquippedIntents.Count ? training.EquippedIntents[index] : null;
            CreateCompactAbilityCard(equipped.transform, ability, 62f, focused: false,
                targeted: TrainingSelection.TargetsIntentSlot(index),
                TextAnchor.MiddleCenter, compactMeta: true, () =>
            {
                TrainingSelection.SelectIntentSlot(slot, ability?.Id);
                RefreshPreviewAndDetail();
            }, L("ui.spirit.ability.slot", "slot", (index + 1).ToString()));
        }

        TerriasUiComponents.AddTextBlock(parent, "已装备被动", 12, TextAnchor.MiddleLeft, Muted, 22f, 1f);
        CreateCompactAbilityCard(parent, training.EquippedPassive, 58f, focused: false,
            targeted: TrainingSelection.TargetsPassiveSlot, TextAnchor.MiddleLeft, compactMeta: false, () =>
            {
                TrainingSelection.SelectPassiveSlot(training.EquippedPassive?.Id);
                RefreshPreviewAndDetail();
            }, L("ui.spirit.ability.passive_empty"));

        TerriasUiComponents.AddTextBlock(parent, "当前查看能力", 13, TextAnchor.MiddleLeft, Pale, 26f, 1f);
        CreateTrainingAbilityDetail(parent, item, training, focused);

        TerriasUiComponents.AddTextBlock(parent, "已学会意图", 13, TextAnchor.MiddleLeft, Pale, 26f, 1f);
        CreateAbilityList(parent, training.LearnedIntents, ability =>
        {
            TrainingSelection.PreviewAbility(ability.Id);
            RefreshPreviewAndDetail();
        });

        TerriasUiComponents.AddTextBlock(parent, "已学会被动", 13, TextAnchor.MiddleLeft, Pale, 26f, 1f);
        CreateAbilityList(parent, training.LearnedPassives, ability =>
        {
            TrainingSelection.PreviewAbility(ability.Id);
            RefreshPreviewAndDetail();
        });
    }

    private static void BuildGuiyuanView(Transform parent, SpiritInstance item, SpiritGrowthViewSnapshot growth)
    {
        var rank = SpiritAscensionService.StarRankFor(item.GuiyuanValue);
        var budget = SpiritAscensionService.PointBudgetForStar(rank);
        var allocations = SpiritAscensionService.NormalizeAllocations(item.GuiyuanAllocations, item.GuiyuanValue);
        var status = LayoutObject("GuiyuanStatus", parent, 58f);
        ApplyPanel(status, ItemTint);
        TerriasUiComponents.ConfigureVerticalLayout(status, new RectOffset(10, 10, 7, 7), 2f,
            childForceExpandHeight: false);
        var title = LayoutObject("GuiyuanTitle", status.transform, 23f);
        TerriasUiComponents.ConfigureHorizontalLayout(title, new RectOffset(0, 0, 0, 0), 8f);
        AddFixedTextBlock(title.transform, StarText(rank), 18, TextAnchor.MiddleLeft, StarGold, 23f, 1f);
        AddFixedTextBlock(title.transform, L("ui.spirit.guiyuan.value", "current", item.GuiyuanValue.ToString(),
                "maximum", SpiritAscensionService.MaximumGuiyuanValue.ToString()),
            13, TextAnchor.MiddleRight, Pale, 23f, 0f, FontStyle.Normal, 112f);
        AddFixedTextBlock(status.transform,
            rank >= SpiritAscensionService.MaximumStarRank
                ? L("ui.spirit.guiyuan.max_star")
                : L("ui.spirit.guiyuan.next_star", "value", SpiritAscensionService.ThresholdForStar(rank + 1).ToString()),
            12, TextAnchor.MiddleLeft, rank >= SpiritAscensionService.MaximumStarRank ? Gold : Muted, 20f, 1f);

        var points = LayoutObject("OriginPointSummary", parent, 34f);
        ApplyPanel(points, new Color(0.025f, 0.050f, 0.070f, 0.94f));
        TerriasUiComponents.ConfigureHorizontalLayout(points, new RectOffset(10, 10, 0, 0), 8f);
        AddFixedTextBlock(points.transform, "四大本源", 14, TextAnchor.MiddleLeft, Pale, 32f, 1f, FontStyle.Bold);
        AddFixedTextBlock(points.transform, L("ui.spirit.guiyuan.allocated", "current", allocations.Total.ToString(), "maximum", budget.ToString()),
            13, TextAnchor.MiddleRight, allocations.Total < budget ? Cyan : Gold, 32f, 0f, FontStyle.Normal, 118f);

        CreateOriginAllocationRow(parent, item, growth, allocations, "魔力", "magic");
        CreateOriginAllocationRow(parent, item, growth, allocations, "感知", "perception");
        CreateOriginAllocationRow(parent, item, growth, allocations, "精神", "spirit");
        CreateOriginAllocationRow(parent, item, growth, allocations, "幸运", "luck");

        var action = LayoutObject("GuiyuanEntry", parent, 42f);
        TerriasUiComponents.ConfigureHorizontalLayout(action, new RectOffset(0, 0, 2, 2), 8f);
        var canSelect = mode == PanelMode.Warehouse
                        && rank < SpiritAscensionService.MaximumStarRank
                        && !guiyuanSelectingDonors;
        var label = mode != PanelMode.Warehouse
            ? L("ui.spirit.guiyuan.go_to_warehouse")
            : rank >= SpiritAscensionService.MaximumStarRank
                ? L("ui.spirit.guiyuan.max_star")
                : guiyuanSelectingDonors ? L("ui.spirit.guiyuan.selecting") : L("ui.spirit.guiyuan.select");
        var button = TerriasUiComponents.CreateTextButton(
            action.transform,
            label,
            new Vector2(188f, 36f),
            TerriasUiSprites.Button("[SpiritManagement]"),
            canSelect ? TargetTint : BandTint,
            canSelect ? Pale : Muted,
            13,
            BeginGuiyuanSelection);
        button.interactable = canSelect;
    }

    private static void CreateOriginAllocationRow(
        Transform parent,
        SpiritInstance item,
        SpiritGrowthViewSnapshot growth,
        SpiritOriginVector allocations,
        string label,
        string key)
    {
        var allocated = SpiritGrowthQueryService.Value(allocations, key);
        var effective = SpiritGrowthQueryService.Value(growth.CurrentOrigins, key);
        var budget = SpiritAscensionService.PointBudgetFor(item);
        var row = LayoutObject("OriginAllocation-" + key, parent, 42f);
        ApplyPanel(row, ItemTint);
        TerriasUiComponents.ConfigureHorizontalLayout(row, new RectOffset(8, 8, 5, 5), 5f);
        AddFixedTextBlock(row.transform, L("ui.spirit.guiyuan.origin_value", "name", TerriasTextCatalog.ResolveLegacy(label),
                "base", (effective - allocated).ToString(), "allocated", allocated.ToString()),
            12, TextAnchor.MiddleLeft, Pale, 30f, 0f, FontStyle.Normal, 92f);
        var minus = TerriasUiComponents.CreateTextButton(row.transform, "−", new Vector2(30f, 30f),
            TerriasUiSprites.Button("[SpiritManagement]"), BandTint, allocated > 0 ? Pale : Muted, 16,
            () => AdjustGuiyuanAllocation(item.SpiritUid, key, -1));
        minus.interactable = allocated > 0;

        var cells = LayoutObject("AllocationCells", row.transform, 24f);
        cells.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TerriasUiComponents.ConfigureHorizontalLayout(cells, new RectOffset(0, 0, 3, 3), 2f,
            childForceExpandHeight: true, alignment: TextAnchor.MiddleCenter);
        for (var index = 0; index < SpiritAscensionService.MaximumAllocationPerOrigin; index++)
        {
            var cell = LayoutObject("Point-" + index, cells.transform, 16f, 0f, 14f);
            ApplyPanel(cell, index < allocated ? Cyan : new Color(0.12f, 0.15f, 0.17f, 0.92f));
        }

        var canAdd = allocated < SpiritAscensionService.MaximumAllocationPerOrigin && allocations.Total < budget;
        var plus = TerriasUiComponents.CreateTextButton(row.transform, "+", new Vector2(30f, 30f),
            TerriasUiSprites.Button("[SpiritManagement]"), BandTint, canAdd ? Pale : Muted, 16,
            () => AdjustGuiyuanAllocation(item.SpiritUid, key, 1));
        plus.interactable = canAdd;
    }

    private static void AdjustGuiyuanAllocation(string uid, string key, int delta)
    {
        var item = SpiritCollectionApi.Find(uid);
        if (item == null) return;
        var value = SpiritAscensionService.NormalizeAllocations(item.GuiyuanAllocations, item.GuiyuanValue);
        switch (key)
        {
            case "magic": value.Magic += delta; break;
            case "perception": value.Perception += delta; break;
            case "spirit": value.Spirit += delta; break;
            case "luck": value.Luck += delta; break;
            default: return;
        }
        if (!SpiritCollectionApi.SetGuiyuanAllocations(uid, value)) return;
        RefreshPreviewAndDetail();
    }

    private static void BeginGuiyuanSelection()
    {
        var target = SpiritCollectionApi.Find(selectedUid);
        if (mode != PanelMode.Warehouse || target == null
            || SpiritAscensionService.StarRankFor(target.GuiyuanValue) >= SpiritAscensionService.MaximumStarRank) return;
        guiyuanSelectingDonors = true;
        guiyuanTargetUid = target.SpiritUid;
        GuiyuanDonorUids.Clear();
        guiyuanConfirmArmed = false;
        Refresh();
    }

    private static void ToggleGuiyuanDonor(string uid)
    {
        if (!guiyuanSelectingDonors || string.IsNullOrWhiteSpace(uid)) return;
        if (!GuiyuanDonorUids.Add(uid)) GuiyuanDonorUids.Remove(uid);
        guiyuanConfirmArmed = false;
    }

    private static void RefreshGuiyuanActions()
    {
        if (actionContent == null) return;
        var target = SpiritCollectionApi.Find(guiyuanTargetUid);
        if (target == null)
        {
            ResetGuiyuanSelection();
            Refresh();
            return;
        }
        var donors = GuiyuanDonorUids.Select(SpiritCollectionApi.Find).Where(item => item != null).Cast<SpiritInstance>().ToList();
        var preview = SpiritAscensionService.Preview(target, donors);
        var summary = donors.Count == 0
            ? L("ui.spirit.guiyuan.choose_same_species")
            : L(preview.OverflowValue > 0
                    ? "ui.spirit.guiyuan.preview_overflow"
                    : "ui.spirit.guiyuan.preview",
                "count", donors.Count.ToString(),
                "offered", preview.OfferedValue.ToString(),
                "applied", preview.AppliedValue.ToString(),
                "overflow", preview.OverflowValue.ToString(),
                "stars", StarText(preview.ResultStarRank));
        AddFixedTextBlock(actionContent, summary, 13, TextAnchor.MiddleLeft,
            preview.OverflowValue > 0 ? Gold : Muted, 34f, 1f);
        TerriasUiComponents.CreateTextButton(actionContent, "取消", new Vector2(88f, 34f),
            TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, () =>
            {
                ResetGuiyuanSelection();
                Refresh();
            });
        var confirmLabel = guiyuanConfirmArmed
            ? L("ui.spirit.guiyuan.confirm_consume", "count", donors.Count.ToString())
            : preview.OverflowValue > 0 ? L("ui.spirit.guiyuan.confirm_overflow") : L("ui.spirit.guiyuan.action");
        var confirm = TerriasUiComponents.CreateTextButton(actionContent, confirmLabel, new Vector2(164f, 34f),
            TerriasUiSprites.Button("[SpiritManagement]"), TargetTint,
            donors.Count > 0 ? Pale : Muted, 13, ConfirmGuiyuan);
        confirm.interactable = donors.Count > 0;
    }

    private static void ConfirmGuiyuan()
    {
        if (!guiyuanConfirmArmed)
        {
            guiyuanConfirmArmed = true;
            RefreshActions();
            return;
        }
        var result = SpiritCollectionApi.Guiyuan(guiyuanTargetUid, GuiyuanDonorUids.ToArray());
        if (!result.Success)
        {
            guiyuanConfirmArmed = false;
            PlayerApi.ShowCaption(string.IsNullOrWhiteSpace(result.Reason) ? L("caption.spirit.guiyuan_failed") : result.Reason);
            Refresh();
            return;
        }
        selectedUid = result.Target?.SpiritUid ?? guiyuanTargetUid;
        var donorCount = result.Preview.DonorCount;
        var gained = result.Preview.AppliedValue;
        ResetGuiyuanSelection();
        PlayerApi.ShowCaption(L("caption.spirit.guiyuan_complete", "count", donorCount.ToString(), "value", gained.ToString()));
        Refresh();
    }

    private static HashSet<string> GuiyuanForbiddenUids()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var uid in SpiritCollectionApi.DefaultParty().PartySlots.Where(uid => !string.IsNullOrWhiteSpace(uid))) result.Add(uid);
        foreach (var uid in SpiritCollectionApi.CurrentParty().PartySlots.Where(uid => !string.IsNullOrWhiteSpace(uid))) result.Add(uid);
        return result;
    }

    private static bool SameSpeciesAsGuiyuanTarget(SpiritInstance item)
    {
        var target = SpiritCollectionApi.Find(guiyuanTargetUid);
        return target != null && Same(target.SpeciesId, item.SpeciesId);
    }

    private static void ResetGuiyuanSelection()
    {
        guiyuanSelectingDonors = false;
        guiyuanTargetUid = "";
        GuiyuanDonorUids.Clear();
        guiyuanConfirmArmed = false;
    }

    private static void CreateVerticalStat(Transform parent, string label, int value)
    {
        var row = LayoutObject("Stat-" + label, parent, 34f);
        TerriasUiComponents.ConfigureHorizontalLayout(row, new RectOffset(4, 4, 0, 0), 8f);
        TerriasUiComponents.AddTextBlock(row.transform, label, 12, TextAnchor.MiddleLeft, Muted, 32f, 1f);
        TerriasUiComponents.AddTextBlock(row.transform, value.ToString(), 17, TextAnchor.MiddleRight, Pale, 32f, 1f);
    }

    private static void CreateExperienceProgress(Transform parent, SpiritGrowthViewSnapshot growth)
    {
        var progress = LayoutObject("ExperienceProgress", parent, 58f);
        ApplyPanel(progress, ItemTint);
        TerriasUiComponents.ConfigureVerticalLayout(progress, new RectOffset(10, 10, 7, 8), 6f,
            childForceExpandHeight: false, alignment: TextAnchor.MiddleCenter);
        var label = growth.Level >= growth.MaxLevel
            ? L("ui.spirit.experience_max")
            : L("ui.spirit.experience", "current", growth.Experience.ToString(), "maximum", growth.ExperienceToNextLevel.ToString());
        AddFixedTextBlock(progress.transform, label, 12, TextAnchor.MiddleCenter, Cyan, 22f, 1f);
        var track = LayoutObject("Track", progress.transform, 10f);
        track.GetComponent<LayoutElement>().minHeight = 10f;
        ApplyPanel(track, new Color(0.04f, 0.07f, 0.09f, 1f));
        var fill = TerriasUiComponents.CreateFillRect("Fill", track.transform);
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = Cyan;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = growth.Level >= growth.MaxLevel
            ? 1f
            : Mathf.Clamp01(growth.Experience / (float)Math.Max(1, growth.ExperienceToNextLevel));
        fillImage.raycastTarget = false;
    }

    private static void CreateAbilityList(Transform parent, IReadOnlyList<SpiritAbilityView> abilities, Action<SpiritAbilityView> onClick)
    {
        foreach (var ability in abilities)
        {
            var current = ability;
            CreateCompactAbilityCard(parent, current, 58f,
                focused: Same(current.Id, TrainingSelection.FocusedAbilityId), targeted: false,
                TextAnchor.MiddleLeft, compactMeta: false, () => onClick(current));
        }
    }

    private static void CreateCompactAbilityCard(
        Transform parent,
        SpiritAbilityView? ability,
        float height,
        bool focused,
        bool targeted,
        TextAnchor anchor,
        bool compactMeta,
        Action? onClick,
        string emptyLabel = "")
    {
        var card = LayoutObject("Ability-" + (ability?.Id ?? "empty"), parent, height);
        card.GetComponent<LayoutElement>().flexibleWidth = 1f;
        ApplyPanel(card, targeted ? TargetTint : focused ? SelectedTint : ItemTint, onClick != null);
        if (targeted) AddTargetOutline(card);
        else if (focused) AddSelectionOutline(card);
        TerriasUiComponents.ConfigureVerticalLayout(card, new RectOffset(8, 8, 5, 5), 0f,
            childForceExpandHeight: false, alignment: anchor);
        AddFixedTextBlock(
            card.transform,
            (targeted ? L("ui.spirit.ability.target_prefix") : "")
            + (ability == null ? emptyLabel : (ability.IsNew ? "NEW · " : "") + ability.DisplayName),
            14,
            anchor,
            targeted || ability?.IsNew == true ? Gold : Pale,
            28f,
            1f,
            ability == null ? FontStyle.Normal : FontStyle.Bold);
        if (ability != null)
        {
            AddFixedTextBlock(card.transform, AbilityMeta(ability, compactMeta), 12, anchor, Muted, 20f, 1f);
        }
        if (onClick == null) return;
        var button = card.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, card.GetComponent<Image>(), Gold);
        button.onClick.AddListener(() => onClick());
    }

    private static SpiritAbilityView? ResolveFocusedAbility(SpiritTrainingViewSnapshot training)
    {
        var all = training.EquippedIntents
            .Concat(training.EquippedPassive == null ? Array.Empty<SpiritAbilityView>() : new[] { training.EquippedPassive })
            .Concat(training.LearnedIntents)
            .Concat(training.LearnedPassives)
            .ToList();
        return all.FirstOrDefault(ability => Same(ability.Id, TrainingSelection.FocusedAbilityId));
    }

    private static void CreateTrainingAbilityDetail(
        Transform parent,
        SpiritInstance item,
        SpiritTrainingViewSnapshot training,
        SpiritAbilityView? ability)
    {
        var card = LayoutObject("AbilityDetail", parent, 140f);
        ApplyPanel(card, ItemTint);
        TerriasUiComponents.ConfigureVerticalLayout(card, new RectOffset(10, 10, 8, 8), 4f,
            childForceExpandHeight: false);
        if (ability == null)
        {
            var emptyTitle = TrainingSelection.TargetKind switch
            {
                SpiritTrainingTargetKind.IntentSlot =>
                    L("ui.spirit.ability.slot_empty", "slot", (TrainingSelection.IntentSlotIndex + 1).ToString()),
                SpiritTrainingTargetKind.PassiveSlot => L("ui.spirit.ability.passive_empty"),
                _ => L("ui.spirit.ability.choose_detail")
            };
            var emptyHint = TrainingSelection.TargetKind switch
            {
                SpiritTrainingTargetKind.IntentSlot => L("ui.spirit.ability.choose_intent_hint"),
                SpiritTrainingTargetKind.PassiveSlot => L("ui.spirit.ability.choose_passive_hint"),
                _ => L("ui.spirit.ability.choose_slot_hint")
            };
            AddFixedTextBlock(card.transform, emptyTitle, 16,
                TextAnchor.LowerCenter, Pale, 42f, 1f, FontStyle.Bold);
            AddFixedTextBlock(card.transform, emptyHint, 12,
                TextAnchor.UpperCenter, Muted, 62f, 1f);
            return;
        }

        var header = LayoutObject("AbilityDetailHeader", card.transform, 28f);
        TerriasUiComponents.ConfigureHorizontalLayout(header, new RectOffset(0, 0, 0, 0), 8f);
        AddFixedTextBlock(header.transform, (ability.IsNew ? "NEW · " : "") + ability.DisplayName,
            16, TextAnchor.MiddleLeft, ability.IsNew ? Gold : Pale, 28f, 1f, FontStyle.Bold);
        AddFixedTextBlock(header.transform, AbilityMeta(ability), 12,
            TextAnchor.MiddleRight, Muted, 28f, 0f, FontStyle.Normal, 168f);

        var description = PlayerFacingDescription(ability.Description).Replace("\n", "  ");
        AddFixedTextBlock(card.transform,
            description.Length == 0 ? L("ui.spirit.ability.no_description") : description,
            13,
            TextAnchor.UpperLeft,
            Pale,
            52f,
            1f,
            FontStyle.Normal,
            0f,
            1.15f);

        var actions = LayoutObject("AbilityDetailActions", card.transform, 32f);
        TerriasUiComponents.ConfigureHorizontalLayout(actions, new RectOffset(0, 0, 0, 0), 8f);
        if (string.Equals(ability.Kind, "Intent", StringComparison.Ordinal))
        {
            if (TrainingSelection.TargetKind != SpiritTrainingTargetKind.IntentSlot)
            {
                AddFixedTextBlock(actions.transform, L("ui.spirit.ability.choose_intent_slot"), 12,
                    TextAnchor.MiddleRight, Gold, 32f, 1f);
                return;
            }

            var intentSlot = TrainingSelection.IntentSlotIndex;
            var currentId = intentSlot < training.EquippedIntents.Count
                ? training.EquippedIntents[intentSlot].Id
                : "";
            if (Same(currentId, ability.Id))
            {
                AddFixedTextBlock(actions.transform, L("ui.spirit.ability.slot_equipped"), 12,
                    TextAnchor.MiddleRight, Green, 32f, 1f);
            }
            else
            {
                var hasCurrentIntent = currentId.Length > 0;
                AddFixedTextBlock(actions.transform,
                    L(hasCurrentIntent ? "ui.spirit.ability.will_replace_slot" : "ui.spirit.ability.will_equip_slot",
                        "slot", (intentSlot + 1).ToString()), 12,
                    TextAnchor.MiddleRight, Muted, 32f, 1f);
                TerriasUiComponents.CreateTextButton(actions.transform,
                    L(hasCurrentIntent ? "ui.spirit.ability.replace_slot" : "ui.spirit.ability.equip_slot",
                        "slot", (intentSlot + 1).ToString()),
                    new Vector2(138f, 32f),
                    TerriasUiSprites.Button("[SpiritManagement]"),
                    SelectedTint,
                    Pale,
                    13,
                    () =>
                    {
                        if (!SpiritCollectionApi.EquipIntent(item.SpiritUid, intentSlot, ability.Id)) return;
                        PlayerApi.ShowCaption(L("caption.spirit.configuration_updated"));
                        RefreshPreviewAndDetail();
                    });
            }
        }
        else if (TrainingSelection.TargetKind != SpiritTrainingTargetKind.PassiveSlot)
        {
            AddFixedTextBlock(actions.transform, L("ui.spirit.ability.choose_passive_slot"), 12,
                TextAnchor.MiddleRight, Gold, 32f, 1f);
        }
        else if (Same(training.EquippedPassive?.Id ?? "", ability.Id))
        {
            AddFixedTextBlock(actions.transform, L("ui.spirit.ability.passive_equipped"), 12,
                TextAnchor.MiddleRight, Green, 32f, 1f);
        }
        else
        {
            AddFixedTextBlock(actions.transform, L("ui.spirit.ability.passive_replace_free"), 12,
                TextAnchor.MiddleRight, Muted, 32f, 1f);
            TerriasUiComponents.CreateTextButton(actions.transform,
                L("ui.spirit.ability.set_passive"),
                new Vector2(138f, 32f),
                TerriasUiSprites.Button("[SpiritManagement]"),
                SelectedTint,
                Pale,
                13,
                () =>
                {
                    if (!SpiritCollectionApi.EquipPassive(item.SpiritUid, ability.Id)) return;
                    PlayerApi.ShowCaption(L("caption.spirit.configuration_updated"));
                    RefreshPreviewAndDetail();
                });
        }
    }

    private static string AbilityMeta(SpiritAbilityView ability, bool compact = false)
    {
        var type = AbilityTypeLabel(ability.Type);
        if (compact && string.Equals(ability.Kind, "Intent", StringComparison.Ordinal))
        {
            return L("ui.spirit.ability.meta_compact", "cost", ability.Cost.ToString(), "cooldown", ability.Cooldown.ToString());
        }
        return string.Equals(ability.Kind, "Intent", StringComparison.Ordinal)
            ? L("ui.spirit.ability.meta", "type", type, "cost", ability.Cost.ToString(), "cooldown", ability.Cooldown.ToString())
            : type;
    }

    private static string AbilityTypeLabel(string type)
    {
        var normalized = (type ?? "").Trim();
        return normalized switch
        {
            "Attack" => L("ui.spirit.ability.type.attack"),
            "Defense" => L("ui.spirit.ability.type.defense"),
            "Support" => L("ui.spirit.ability.type.support"),
            "Recovery" => L("ui.spirit.ability.type.recovery"),
            "Interference" => L("ui.spirit.ability.type.interference"),
            "Species" => L("ui.spirit.ability.type.species"),
            "Common.Core" => L("ui.spirit.ability.type.common_core"),
            "Common.Advanced" => L("ui.spirit.ability.type.common_advanced"),
            "Common.Basic" => L("ui.spirit.ability.type.common_basic"),
            "Common.Tactical" => L("ui.spirit.ability.type.common_tactical"),
            _ => normalized.Length == 0 ? L("ui.spirit.ability.type.default") : normalized
        };
    }

    private static Text AddFixedTextBlock(
        Transform parent,
        string value,
        int fontSize,
        TextAnchor anchor,
        Color color,
        float preferredHeight,
        float flexibleWidth = 0f,
        FontStyle style = FontStyle.Normal,
        float preferredWidth = 0f,
        float lineSpacing = 1f)
    {
        var text = TerriasUiComponents.AddTextBlock(
            parent, value, fontSize, anchor, color, preferredHeight, flexibleWidth, preferredWidth);
        text.resizeTextForBestFit = false;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.lineSpacing = lineSpacing;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static string AxisSummary(SpiritRadarAxisSnapshot axis)
    {
        return L("ui.spirit.radar.summary",
            "name", TerriasTextCatalog.ResolveLegacy(axis.Label),
            "current", axis.RawCurrent.ToString(),
            "potential", axis.RawPotential.ToString(),
            "cap", axis.Cap.ToString(),
            "base", axis.BaseValue.ToString(),
            "growth", axis.GrowthBudget.ToString());
    }

    private static string PlayerFacingDescription(string raw)
    {
        var value = raw ?? "";
        value = Regex.Replace(value, "</(?:title|main|name|des|cd)>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "<(?:title|main|name|des|cd)>", "", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "<[^>]+>", "");
        var lines = WebUtility.HtmlDecode(value)
            .Replace("\r", "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
        return string.Join("\n", lines);
    }

    private static void AddOverlayLabel(Transform parent, string value, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 offset)
    {
        var root = TerriasUiComponents.CreateRect("Label-" + value, parent, anchor, anchor, pivot, size);
        root.GetComponent<RectTransform>().anchoredPosition = offset;
        var label = TerriasUiComponents.AddTextFill(root.transform, value, 11, TextAnchor.MiddleCenter, Pale);
        label.raycastTarget = false;
    }

    private static Sprite? Portrait(CapturedEnemySnapshot snapshot)
    {
        try
        {
            return TerriasResourceCache.LoadAll<Sprite>(snapshot.DictPath, "spirit-management")?.FirstOrDefault()
                   ?? TerriasResourceCache.LoadAll<Sprite>(snapshot.IdlePath, "spirit-management")?.FirstOrDefault();
        }
        catch { return null; }
    }

    private static GameObject LayoutObject(string name, Transform parent, float preferredHeight, float flexibleHeight = 0f, float preferredWidth = 0f)
    {
        var go = TerriasUiComponents.CreateFillRect(name, parent);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        element.minHeight = Math.Min(preferredHeight, 34f);
        element.flexibleHeight = flexibleHeight;
        if (preferredWidth > 0f)
        {
            element.minWidth = preferredWidth;
            element.preferredWidth = preferredWidth;
            element.flexibleWidth = 0f;
        }
        return go;
    }

    private static void CreateCenteredPortrait(
        Transform parent,
        string name,
        float slotHeight,
        float imageSize,
        Sprite? sprite,
        Color fallback)
    {
        var slot = LayoutObject(name + "Slot", parent, slotHeight);
        var imageRoot = TerriasUiComponents.CreateRect(
            name,
            slot.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(imageSize, imageSize));
        var image = imageRoot.AddComponent<Image>();
        image.sprite = sprite;
        image.color = sprite == null ? fallback : Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static void ApplyPanel(GameObject go, Color color, bool raycast = false)
    {
        TerriasUiBuilder.ApplyPanelImage(go, TerriasUiSprites.Panel("[SpiritManagement]"), color, raycast);
    }

    private static void AddSelectionOutline(GameObject go)
    {
        var outline = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        outline.effectColor = SelectionStroke;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;
    }

    private static void AddTargetOutline(GameObject go)
    {
        var outline = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        outline.effectColor = TargetStroke;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;
    }

    private static void AddActiveStamp(Transform parent)
    {
        var stamp = TerriasUiComponents.CreateRect(
            "ActiveStamp",
            parent,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(46f, 28f));
        var rect = stamp.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(-4f, -5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, -10f);
        var layout = stamp.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        stamp.AddComponent<SpiritActiveStampGraphic>().Configure(
            ActiveStampFill,
            ActiveStampOuter,
            ActiveStampInner);
        var text = TerriasUiComponents.AddTextFill(stamp.transform, "出战", 13, TextAnchor.MiddleCenter, ActiveStampText);
        text.resizeTextForBestFit = false;
        text.fontSize = 13;
        text.fontStyle = FontStyle.Bold;
        text.raycastTarget = false;
    }

    private static Color QualityTint(int aptitude)
    {
        if (aptitude >= 100) return QualityRed;
        if (aptitude >= 90) return QualityGold;
        if (aptitude >= 80) return QualityPurple;
        if (aptitude >= 70) return QualityBlue;
        if (aptitude >= 60) return QualityGreen;
        if (aptitude >= 40) return QualityWhite;
        return QualityGray;
    }

    private static Color QualityAccent(int aptitude)
    {
        if (aptitude >= 100) return new Color(0.97f, 0.52f, 0.55f, 1f);
        if (aptitude >= 90) return new Color(0.95f, 0.79f, 0.43f, 1f);
        if (aptitude >= 80) return new Color(0.78f, 0.64f, 0.91f, 1f);
        if (aptitude >= 70) return new Color(0.57f, 0.73f, 0.93f, 1f);
        if (aptitude >= 60) return new Color(0.55f, 0.84f, 0.67f, 1f);
        if (aptitude >= 40) return new Color(0.90f, 0.91f, 0.89f, 1f);
        return new Color(0.69f, 0.70f, 0.72f, 1f);
    }

    private static string StarText(SpiritInstance item)
    {
        return StarText(SpiritAscensionService.StarRankFor(item?.GuiyuanValue ?? 0));
    }

    private static string StarText(int rank)
    {
        var normalized = Math.Max(0, Math.Min(SpiritAscensionService.MaximumStarRank, rank));
        return new string('★', normalized) + new string('☆', SpiritAscensionService.MaximumStarRank - normalized);
    }

    private static void ClearChildren(Transform? parent)
    {
        if (parent == null) return;
        for (var index = parent.childCount - 1; index >= 0; index--) Object.Destroy(parent.GetChild(index).gameObject);
    }

    private static Vector2 ResolveWindowSize()
    {
        return new Vector2(Mathf.Clamp(Screen.width - 48f, 760f, 1320f), Mathf.Clamp(Screen.height - 48f, 620f, 820f));
    }

    private static float RosterWidth(Vector2 windowSize) => Mathf.Clamp(windowSize.x * 0.36f, 280f, 460f);

    private static float PartySlotWidth() => Mathf.Clamp((ResolveWindowSize().x - 106f) / 6f, 104f, 168f);

    private static string FilterName() => warehouseFilter == 1
        ? L("ui.spirit.filter.carried")
        : warehouseFilter == 2 ? L("ui.spirit.filter.warehouse") : L("ui.spirit.filter.all");
    private static string SortName() => warehouseSort switch
    {
        1 => L("ui.spirit.sort.aptitude"),
        2 => L("ui.spirit.sort.tier"),
        3 => L("ui.spirit.sort.captured"),
        _ => L("ui.spirit.sort.level")
    };
    private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    private static string TierName(SpiritSpeciesTier tier) => tier switch
    {
        SpiritSpeciesTier.Elite => L("ui.spirit.tier.elite"),
        SpiritSpeciesTier.Boss => L("ui.spirit.tier.boss"),
        SpiritSpeciesTier.FinalBoss => L("ui.spirit.tier.final_boss"),
        _ => L("ui.spirit.tier.normal")
    };

    private static string L(string key, params string[] argumentPairs)
    {
        return TerriasTextCatalog.Format(key, argumentPairs);
    }
}

public sealed class SpiritPreviewAnimator : MonoBehaviour
{
    private Sprite[] frames = Array.Empty<Sprite>();
    private Image? target;
    private int frame;
    private float elapsed;

    public void Configure(string idlePath, Sprite? fallback)
    {
        target = GetComponent<Image>();
        try
        {
            frames = (TerriasResourceCache.LoadAll<Sprite>(idlePath, "spirit-preview") ?? Array.Empty<Sprite>())
                .Where(sprite => sprite != null)
                .ToArray();
        }
        catch { frames = Array.Empty<Sprite>(); }
        if (target != null) target.sprite = frames.FirstOrDefault() ?? fallback;
        enabled = frames.Length > 1;
    }

    private void Update()
    {
        if (target == null || frames.Length <= 1) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < 0.16f) return;
        elapsed = 0f;
        frame = (frame + 1) % frames.Length;
        target.sprite = frames[frame];
    }
}
