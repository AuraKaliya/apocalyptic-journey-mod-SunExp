using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.74f);
    private static readonly Color WindowTint = new(0.025f, 0.035f, 0.065f, 0.99f);
    private static readonly Color BandTint = new(0.07f, 0.075f, 0.12f, 0.98f);
    private static readonly Color ItemTint = new(0.09f, 0.105f, 0.15f, 0.98f);
    private static readonly Color SelectedTint = new(0.20f, 0.27f, 0.32f, 0.98f);
    private static readonly Color Gold = new(0.92f, 0.78f, 0.42f);
    private static readonly Color Pale = new(0.92f, 0.94f, 0.96f);
    private static readonly Color Green = new(0.47f, 0.88f, 0.66f);

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
    private static int growthAxis;

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
            growthAxis = 0;
            var party = Party();
            selectedUid = party.ActiveSpiritUid;
            if (string.IsNullOrWhiteSpace(selectedUid))
            {
                selectedUid = party.PartySlots.FirstOrDefault(uid => !string.IsNullOrWhiteSpace(uid))
                              ?? SpiritCollectionApi.Collection().Instances.FirstOrDefault()?.SpiritUid
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
        var windowSize = ResolveWindowSize();
        var window = TerriasUiComponents.CreateRect(
            "Window",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            windowSize);
        ApplyPanel(window, WindowTint);
        TerriasUiComponents.ConfigureVerticalLayout(window, new RectOffset(18, 18, 14, 14), 9f);

        var header = LayoutObject("Header", window.transform, 46f);
        ApplyPanel(header, BandTint);
        TerriasUiComponents.AddTextBlock(
            header.transform,
            mode == PanelMode.Adventure ? "精灵背包" : "精灵仓库",
            26,
            TextAnchor.MiddleCenter,
            Gold,
            46f,
            1f);

        var body = LayoutObject("Body", window.transform, 390f, 1f);
        var bodyLayout = TerriasUiComponents.ConfigureHorizontalLayout(body, new RectOffset(0, 0, 0, 0), 12f);
        bodyLayout.childForceExpandHeight = true;

        if (mode == PanelMode.Warehouse)
        {
            var columnWidth = Mathf.Clamp((windowSize.x - 64f) / 3f, 230f, 410f);
            var left = LayoutObject("Roster", body.transform, 390f, 1f, columnWidth);
            ApplyPanel(left, BandTint);
            TerriasUiComponents.ConfigureVerticalLayout(left, new RectOffset(8, 8, 8, 8), 7f);
            CreateFilterBar(left.transform);
            var grid = TerriasUiComponents.CreateUniformGridScrollArea(
                left.transform,
                "Spirits",
                260f,
                1f,
                columnWidth >= 338f ? 3 : 2,
                new Vector2(104f, 126f),
                new Vector2(7f, 7f),
                new RectOffset(4, 4, 4, 4),
                28f,
                new Color(0f, 0f, 0f, 0.12f));
            gridContent = grid.Content;
        }

        var contentColumnWidth = mode == PanelMode.Warehouse
            ? Mathf.Clamp((windowSize.x - 64f) / 3f, 230f, 410f)
            : Mathf.Clamp((windowSize.x - 48f) / 2f, 330f, 630f);
        var preview = LayoutObject("Preview", body.transform, 390f, 1f, contentColumnWidth);
        previewContent = preview.transform;
        CreatePreviewShell(preview.transform);

        var detail = LayoutObject("Detail", body.transform, 390f, 1f, contentColumnWidth);
        ApplyPanel(detail, BandTint);
        TerriasUiComponents.ConfigureVerticalLayout(detail, new RectOffset(14, 14, 12, 12), 4f);
        detailContent = detail.transform;

        var partyBand = LayoutObject("PartyBand", window.transform, 142f);
        ApplyPanel(partyBand, BandTint);
        TerriasUiComponents.ConfigureVerticalLayout(partyBand, new RectOffset(10, 10, 7, 7), 5f);
        TerriasUiComponents.AddTextBlock(
            partyBand.transform,
            mode == PanelMode.Adventure ? "本次冒险携带" : "下次冒险携带（点击槽位进行配置）",
            14,
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
        IEnumerable<SpiritInstance> items = collection.Instances;
        if (mode == PanelMode.Adventure) items = items.Where(item => carried.Contains(item.SpiritUid));
        else if (warehouseFilter == 1) items = items.Where(item => carried.Contains(item.SpiritUid));
        else if (warehouseFilter == 2) items = items.Where(item => !carried.Contains(item.SpiritUid));
        IOrderedEnumerable<SpiritInstance> ordered = warehouseSort switch
        {
            1 => items.OrderByDescending(item => item.Favorite).ThenByDescending(item => item.Aptitude).ThenByDescending(item => item.Level),
            2 => items.OrderByDescending(item => item.Favorite).ThenByDescending(SpiritGrowthRegistry.TierFor).ThenByDescending(item => item.Level),
            3 => items.OrderByDescending(item => item.Favorite).ThenByDescending(item => item.CapturedAt, StringComparer.Ordinal),
            _ => items.OrderByDescending(item => item.Favorite).ThenByDescending(item => item.Level).ThenByDescending(item => item.Aptitude)
        };
        foreach (var item in ordered.ThenBy(item => item.Snapshot.DisplayName, StringComparer.Ordinal))
        {
            CreateSpiritCell(gridContent, item, carried.Contains(item.SpiritUid), Same(item.SpiritUid, party.ActiveSpiritUid));
        }
        if (gridContent.childCount == 0)
        {
            TerriasUiComponents.AddTextBlock(gridContent, "暂无精灵", 15, TextAnchor.MiddleCenter, Pale, 90f);
        }
    }

    private static void CreateSpiritCell(Transform parent, SpiritInstance item, bool carried, bool active)
    {
        var cell = LayoutObject("Spirit-" + item.SpiritUid, parent, 126f);
        ApplyPanel(cell, Same(item.SpiritUid, selectedUid) ? SelectedTint : ItemTint, true);
        TerriasUiComponents.ConfigureVerticalLayout(cell, new RectOffset(5, 5, 5, 5), 2f);
        var imageRoot = LayoutObject("Portrait", cell.transform, 76f);
        var image = imageRoot.AddComponent<Image>();
        image.sprite = Portrait(item.Snapshot);
        image.color = image.sprite == null ? new Color(0.18f, 0.20f, 0.24f, 1f) : Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var markers = (active ? "出战 " : "") + (carried ? "携带" : "仓库")
                      + (item.Favorite ? " 收藏" : "") + (item.Locked ? " 锁定" : "");
        TerriasUiComponents.AddTextBlock(cell.transform, item.Snapshot.DisplayName, 13, TextAnchor.MiddleCenter, Pale, 22f);
        TerriasUiComponents.AddTextBlock(cell.transform,
            "Lv." + item.Level + " 资质" + item.Aptitude + "  " + markers,
            11, TextAnchor.MiddleCenter, active ? Green : Gold, 18f);
        var button = cell.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, cell.GetComponent<Image>(), Gold);
        button.onClick.AddListener(() =>
        {
            selectedUid = item.SpiritUid;
            Refresh();
        });
    }

    private static void CreatePreviewShell(Transform parent)
    {
        ApplyPanel(parent.gameObject, new Color(0.025f, 0.05f, 0.075f, 0.96f));
        var title = TerriasUiComponents.CreateRect("PreviewTitle", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 42f));
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -8f);
        TerriasUiComponents.AddTextFill(title.transform, "动态立绘", 15, TextAnchor.MiddleCenter, Gold);
    }

    private static void RefreshPreviewAndDetail()
    {
        if (previewContent == null || detailContent == null) return;
        foreach (Transform child in previewContent)
        {
            if (child.name != "PreviewTitle") Object.Destroy(child.gameObject);
        }
        ClearChildren(detailContent);
        var item = SpiritCollectionApi.Find(selectedUid);
        if (item == null)
        {
            TerriasUiComponents.AddTextBlock(detailContent, "请选择一只精灵", 17, TextAnchor.MiddleCenter, Pale, 80f);
            return;
        }

        var imageRoot = TerriasUiComponents.CreateRect("AnimatedPortrait", previewContent, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.zero);
        var image = imageRoot.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        imageRoot.AddComponent<SpiritPreviewAnimator>().Configure(item.Snapshot.IdlePath, Portrait(item.Snapshot));

        var growth = SpiritCollectionApi.GrowthView(item);
        TerriasUiComponents.AddTextBlock(detailContent,
            item.Snapshot.DisplayName + "  Lv." + item.Level,
            22, TextAnchor.MiddleLeft, Gold, 34f);
        TerriasUiComponents.AddTextBlock(detailContent,
            "个体 " + ShortUid(item.SpiritUid) + "    " + TierName(growth.Tier)
            + (string.IsNullOrWhiteSpace(growth.FormLabel) ? "" : " · " + growth.FormLabel),
            13, TextAnchor.MiddleLeft, Pale, 22f);
        CreateDetailTabs(detailContent);
        var scroll = TerriasUiComponents.CreateVerticalScrollArea(
            detailContent, "GrowthDetail", 230f, 1f, 5f, 24f, new Color(0f, 0f, 0f, 0.08f));
        if (detailTab == 0) BuildAttributeView(scroll.Content, item, growth);
        else BuildGrowthView(scroll.Content, growth);
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
            ApplyPanel(cell, Same(uid, party.ActiveSpiritUid) ? SelectedTint : ItemTint, true);
            TerriasUiComponents.ConfigureVerticalLayout(cell, new RectOffset(5, 5, 4, 4), 2f, alignment: TextAnchor.MiddleCenter);
            var portrait = LayoutObject("Icon", cell.transform, 58f);
            var image = portrait.AddComponent<Image>();
            image.sprite = item == null ? null : Portrait(item.Snapshot);
            image.color = image.sprite == null ? new Color(0.15f, 0.16f, 0.20f, 1f) : Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var label = item == null ? (index + 1) + "  空位" : item.Snapshot.DisplayName + "\nLv." + item.Level;
            TerriasUiComponents.AddTextBlock(cell.transform, label, 12, TextAnchor.MiddleCenter, Pale, 28f, 1f);
            var button = cell.AddComponent<Button>();
            AuraUiButtonFeedback.Apply(button, cell.GetComponent<Image>(), Gold);
            button.onClick.AddListener(() => OnPartySlot(index, uid));
        }
    }

    private static void RefreshActions()
    {
        if (actionContent == null) return;
        ClearChildren(actionContent);
        var party = Party();
        var selected = SpiritCollectionApi.Find(selectedUid);
        TerriasUiComponents.AddTextBlock(actionContent,
            selected == null ? "未选择精灵" : selected.Snapshot.DisplayName + "  #" + ShortUid(selected.SpiritUid),
            14, TextAnchor.MiddleLeft, Pale, 34f, 1f);
        if (selected != null && party.PartySlots.Contains(selected.SpiritUid, StringComparer.Ordinal))
        {
            TerriasUiComponents.CreateTextButton(actionContent, "出战", new Vector2(104f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, SetActive);
        }
        if (mode == PanelMode.Warehouse && selected != null
            && !party.PartySlots.Contains(selected.SpiritUid, StringComparer.Ordinal)
            && party.PartySlots.Any(string.IsNullOrWhiteSpace))
        {
            TerriasUiComponents.CreateTextButton(actionContent, "加入携带", new Vector2(112f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, AddSelected);
        }
        if (mode == PanelMode.Warehouse && selected != null && party.PartySlots.Contains(selected.SpiritUid, StringComparer.Ordinal))
        {
            TerriasUiComponents.CreateTextButton(actionContent, "移出携带", new Vector2(116f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, RemoveSelected);
        }
        if (selected != null)
        {
            TerriasUiComponents.CreateTextButton(actionContent, selected.Favorite ? "取消收藏" : "收藏", new Vector2(104f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, ToggleFavorite);
            TerriasUiComponents.CreateTextButton(actionContent, selected.Locked ? "解除锁定" : "锁定", new Vector2(104f, 34f), TerriasUiSprites.Button("[SpiritManagement]"), BandTint, Pale, 14, ToggleLocked);
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

    private static void OnPartySlot(int slot, string currentUid)
    {
        if (mode == PanelMode.Adventure)
        {
            if (!string.IsNullOrWhiteSpace(currentUid)) selectedUid = currentUid;
            Refresh();
            return;
        }
        if (string.IsNullOrWhiteSpace(selectedUid)) return;
        SpiritCollectionApi.ConfigureDefaultPartySlot(slot, selectedUid);
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
        SpiritCollectionApi.RemoveFromDefaultParty(selectedUid);
        Refresh();
    }

    private static void AddSelected()
    {
        SpiritCollectionApi.AddToDefaultParty(selectedUid);
        Refresh();
    }

    private static void ToggleFavorite()
    {
        SpiritCollectionApi.ToggleFavorite(selectedUid);
        Refresh();
    }

    private static void ToggleLocked()
    {
        SpiritCollectionApi.ToggleLocked(selectedUid);
        Refresh();
    }

    private static void Rebuild()
    {
        var rememberedMode = mode;
        var rememberedSelection = selectedUid;
        var rememberedFilter = warehouseFilter;
        var rememberedSort = warehouseSort;
        var rememberedTab = detailTab;
        var rememberedAxis = growthAxis;
        Close();
        mode = rememberedMode;
        selectedUid = rememberedSelection;
        warehouseFilter = rememberedFilter;
        warehouseSort = rememberedSort;
        detailTab = rememberedTab;
        growthAxis = rememberedAxis;
        Build();
    }

    private static SpiritAdventureParty Party()
    {
        return mode == PanelMode.Adventure ? SpiritCollectionApi.CurrentParty() : SpiritCollectionService.DefaultParty();
    }

    private static void AddStat(Transform parent, string label, int value)
    {
        var row = LayoutObject("Stat-" + label, parent, 25f);
        TerriasUiComponents.ConfigureHorizontalLayout(row, new RectOffset(0, 0, 0, 0), 8f);
        TerriasUiComponents.AddTextBlock(row.transform, label, 13, TextAnchor.MiddleLeft, Pale, 25f, 0f, 120f);
        TerriasUiComponents.AddTextBlock(row.transform, value.ToString(), 14, TextAnchor.MiddleRight, Gold, 25f, 1f);
    }

    private static void CreateDetailTabs(Transform parent)
    {
        var tabs = LayoutObject("DetailTabs", parent, 32f);
        TerriasUiComponents.ConfigureHorizontalLayout(tabs, new RectOffset(0, 0, 0, 0), 6f);
        TerriasUiComponents.CreateTextButton(tabs.transform, detailTab == 0 ? "属性 ·" : "属性", new Vector2(92f, 30f), TerriasUiSprites.Button("[SpiritManagement]"), detailTab == 0 ? SelectedTint : BandTint, Pale, 13, () =>
        {
            detailTab = 0;
            RefreshPreviewAndDetail();
        });
        TerriasUiComponents.CreateTextButton(tabs.transform, detailTab == 1 ? "成长 ·" : "成长", new Vector2(92f, 30f), TerriasUiSprites.Button("[SpiritManagement]"), detailTab == 1 ? SelectedTint : BandTint, Pale, 13, () =>
        {
            detailTab = 1;
            RefreshPreviewAndDetail();
        });
    }

    private static void BuildAttributeView(Transform parent, SpiritInstance item, SpiritGrowthViewSnapshot growth)
    {
        var radarRoot = LayoutObject("Radar", parent, 178f);
        ApplyPanel(radarRoot, new Color(0.035f, 0.055f, 0.075f, 0.92f));
        var radarSurface = TerriasUiComponents.CreateFillRect("RadarSurface", radarRoot.transform);
        var graphic = radarSurface.AddComponent<SpiritRadarGraphic>();
        graphic.color = Color.white;
        AddOverlayLabel(radarRoot.transform, "魔力", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(56f, 20f), new Vector2(0f, -2f));
        AddOverlayLabel(radarRoot.transform, "感知", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(48f, 20f), new Vector2(-3f, 0f));
        AddOverlayLabel(radarRoot.transform, "精神", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(56f, 20f), new Vector2(0f, 2f));
        AddOverlayLabel(radarRoot.transform, "幸运", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 20f), new Vector2(3f, 0f));
        var hover = TerriasUiComponents.AddTextBlock(parent, "悬停雷达轴查看数值", 12, TextAnchor.MiddleCenter, Pale, 24f);
        graphic.Configure(growth.RadarAxes, index =>
        {
            hover.text = index.HasValue && index.Value < growth.RadarAxes.Count
                ? AxisSummary(growth.RadarAxes[index.Value])
                : "当前值 / 当前资质下的满级潜力";
        });
        TerriasUiComponents.AddTextBlock(parent,
            "当前 " + FormatOrigins(growth.CurrentOrigins) + "\n潜力 " + FormatOrigins(growth.MaxLevelOriginsAtCurrentAptitude),
            12, TextAnchor.MiddleLeft, Pale, 44f, 1f);
        TerriasUiComponents.AddTextBlock(parent,
            "资质 " + growth.Aptitude + " / 100    经验 "
            + (growth.Level >= growth.MaxLevel ? "MAX" : growth.Experience + " / " + growth.ExperienceToNextLevel),
            13, TextAnchor.MiddleLeft, Green, 26f, 1f);
        TerriasUiComponents.AddTextBlock(parent, "战斗基础值", 15, TextAnchor.MiddleLeft, Gold, 25f);
        TerriasUiComponents.AddTextBlock(parent,
            "生命 " + growth.BattleStats.MaxHp + "    攻击 " + growth.BattleStats.Attack
            + "    护甲 " + growth.BattleStats.Armor + "    意图能量 " + growth.BattleStats.MaxMagic,
            13, TextAnchor.MiddleLeft, Pale, 30f, 1f);
        if (!string.IsNullOrWhiteSpace(item.Snapshot.Description))
        {
            TerriasUiComponents.AddTextBlock(parent, item.Snapshot.Description, 12, TextAnchor.UpperLeft, Pale, 62f, 1f);
        }
    }

    private static void BuildGrowthView(Transform parent, SpiritGrowthViewSnapshot growth)
    {
        var selector = LayoutObject("GrowthAxis", parent, 31f);
        TerriasUiComponents.ConfigureHorizontalLayout(selector, new RectOffset(0, 0, 0, 0), 4f);
        var keys = new[] { "total", "magic", "spirit", "luck", "perception" };
        for (var index = 0; index < keys.Length; index++)
        {
            var selectedIndex = index;
            TerriasUiComponents.CreateTextButton(selector.transform,
                SpiritGrowthQueryService.Label(keys[index]) + (growthAxis == index ? " ·" : ""),
                new Vector2(36f, 29f), TerriasUiSprites.Button("[SpiritManagement]"),
                growthAxis == index ? SelectedTint : BandTint, Pale, 11, () =>
                {
                    growthAxis = selectedIndex;
                    RefreshPreviewAndDetail();
                });
        }

        var chartRoot = LayoutObject("GrowthCurve", parent, 184f);
        ApplyPanel(chartRoot, new Color(0.035f, 0.055f, 0.075f, 0.92f));
        var chartSurface = TerriasUiComponents.CreateFillRect("GrowthCurveSurface", chartRoot.transform);
        var chart = chartSurface.AddComponent<SpiritGrowthCurveGraphic>();
        chart.color = Color.white;
        chart.Configure(growth.CurrentAptitudeCurve, growth.StandardAptitudeCurve, growth.TheoreticalAptitudeCurve,
            keys[Math.Max(0, Math.Min(keys.Length - 1, growthAxis))], growth.Level);
        AddOverlayLabel(chartRoot.transform, "Lv.1", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(42f, 18f), new Vector2(5f, 1f));
        AddOverlayLabel(chartRoot.transform, "Lv.50", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(48f, 18f), new Vector2(-4f, 1f));
        TerriasUiComponents.AddTextBlock(parent,
            "绿色 当前资质    金色 标准资质60    灰色 理论资质100",
            11, TextAnchor.MiddleCenter, Pale, 22f, 1f);
        TerriasUiComponents.AddTextBlock(parent,
            "种族基础 " + FormatOrigins(growth.BaseOrigins) + "\n成长预算 " + FormatOrigins(growth.GrowthOrigins),
            12, TextAnchor.MiddleLeft, Pale, 42f, 1f);
        TerriasUiComponents.AddTextBlock(parent,
            "Lv." + growth.MaxLevel + " 当前资质潜力 " + FormatOrigins(growth.MaxLevelOriginsAtCurrentAptitude)
            + "\nLv.50 / 资质60基准 " + FormatOrigins(growth.StandardOriginsAtLevel50Aptitude60),
            12, TextAnchor.MiddleLeft, Gold, 46f, 1f);
        TerriasUiComponents.AddTextBlock(parent,
            "SpeciesId  " + growth.SpeciesId + "\nProfileId  " + growth.ProfileId,
            11, TextAnchor.MiddleLeft, new Color(0.68f, 0.74f, 0.80f), 40f, 1f);
    }

    private static string AxisSummary(SpiritRadarAxisSnapshot axis)
    {
        return axis.Label + "  当前 " + axis.RawCurrent + " / 潜力 " + axis.RawPotential
               + " / 雷达上限 " + axis.Cap + "    基础 " + axis.BaseValue + " + 成长 " + axis.GrowthBudget;
    }

    private static string FormatOrigins(SpiritOriginVector value)
    {
        return "魔" + value.Magic + " 精" + value.Spirit + " 运" + value.Luck + " 感" + value.Perception + " 总" + value.Total;
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

    private static void ApplyPanel(GameObject go, Color color, bool raycast = false)
    {
        TerriasUiBuilder.ApplyPanelImage(go, TerriasUiSprites.Panel("[SpiritManagement]"), color, raycast);
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

    private static float PartySlotWidth() => Mathf.Clamp((ResolveWindowSize().x - 106f) / 6f, 104f, 168f);

    private static string FilterName() => warehouseFilter == 1 ? "筛选：携带" : warehouseFilter == 2 ? "筛选：仓库" : "筛选：全部";
    private static string SortName() => warehouseSort switch
    {
        1 => "排序：资质",
        2 => "排序：阶级",
        3 => "排序：捕获",
        _ => "排序：等级"
    };
    private static string ShortUid(string uid) => string.IsNullOrWhiteSpace(uid) ? "----" : uid.Substring(Math.Max(0, uid.Length - 6));
    private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    private static string TierName(SpiritSpeciesTier tier) => tier switch
    {
        SpiritSpeciesTier.Elite => "精英种族",
        SpiritSpeciesTier.Boss => "首领种族",
        SpiritSpeciesTier.FinalBoss => "最终首领种族",
        _ => "普通种族"
    };
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
