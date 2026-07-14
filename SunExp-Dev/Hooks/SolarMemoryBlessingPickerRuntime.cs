using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryBlessingPickerRuntime
{
    public const int Tier4Quota = 2;
    public const int Tier3Quota = 3;
    public const int Tier2Quota = 5;
    public const int Tier1Quota = 5;
    public const int TotalBlessingQuota = Tier4Quota + Tier3Quota + Tier2Quota + Tier1Quota;

    private const string PanelName = "SunExp_SolarMemoryBlessingPicker";
    private const float HeaderHeight = 42f;
    private const float BlessRowHeight = 54f;
    private const float IconColumnWidth = 44f;
    private const float BlessIconSize = 36f;
    private const float TierColumnWidth = 58f;
    private const float InlineButtonWidth = 96f;
    private const float MainButtonWidth = 112f;
    private const float FooterHeight = 64f;
    private const float ButtonHeight = 40f;

    private static readonly Color Gold = new(0.82f, 0.72f, 0.42f);
    private static readonly Color PaleGold = new(0.93f, 0.86f, 0.58f);
    private static readonly Color Green = new(0.62f, 0.94f, 0.62f);
    private static readonly Color DeepBlue = new(0.02f, 0.02f, 0.16f, 0.98f);
    private static readonly Color HeaderTint = new(0.025f, 0.025f, 0.14f, 0.98f);
    private static readonly Color AreaTint = new(0.018f, 0.018f, 0.105f, 0.98f);
    private static readonly Color FooterTint = new(0.018f, 0.018f, 0.115f, 0.96f);
    private static readonly Color RowTint = new(0.07f, 0.07f, 0.21f, 0.98f);

    private static readonly Dictionary<int, List<BlessingEntry>> blessingPools = new();
    private static readonly Dictionary<int, List<string>> selectedByTier = new();
    private static readonly Dictionary<string, Sprite?> blessIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Text> tierCounterTexts = new();
    private static readonly List<BlessingRowView> selectedRows = new();
    private static readonly SunExpDirtyState candidateListDirty = new();
    private static readonly SunExpDirtyState selectedListDirty = new();
    private static GameObject? activePanel;
    private static Transform? candidateListContent;
    private static Transform? selectedListContent;
    private static Text? selectedCounterText;
    private static Text? hintText;
    private static bool isConfirming;
    private static int activeTier = 4;

    public static bool IsOpen => activePanel != null;

    public static void Open(Action onCompleted)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || RoleTable.Instance == null)
            {
                return;
            }

            if (SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryBlessConfiguredKey))
            {
                onCompleted();
                return;
            }

            Close();
            isConfirming = false;
            activeTier = 4;
            BuildBlessingPools();
            LoadSelectionFromGameVar();
            ShowPanel(onCompleted);
        }
        catch (Exception ex)
        {
            Close();
            SunExpLog.Error("Solar memory custom blessing picker failed", ex);
        }
    }

    public static void Close()
    {
        ReleaseTransientRows();
        SunExpModalHost.Close(ref activePanel, "SolarMemoryBlessingPicker.Close", "[SolarMemoryBlessingPicker]");

        candidateListContent = null;
        selectedListContent = null;
        selectedCounterText = null;
        hintText = null;
        tierCounterTexts.Clear();
        candidateListDirty.Reset();
        selectedListDirty.Reset();
    }

    private static void ShowPanel(Action onCompleted)
    {
        var parent = SunExpModalHost.ModalParent();
        if (parent == null)
        {
            return;
        }

        activePanel = SunExpModalHost.CreateFullscreenRoot(
            PanelName,
            parent,
            new Color(0f, 0f, 0f, 0.74f));

        var window = CreateRect("Window", activePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), ResolveWindowSize(parent));
        ApplyPanelImage(window, DeepBlue);
        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(24, 24, 18, 14);
        windowLayout.spacing = 8f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        var header = CreateLayoutObject("Header", window.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 76f;
        ApplyPanelImage(header, HeaderTint);
        var headerLayout = header.AddComponent<VerticalLayoutGroup>();
        headerLayout.padding = new RectOffset(16, 16, 8, 8);
        headerLayout.spacing = 3f;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandHeight = false;
        AddTextBlock(header.transform, "\u65e5\u8000\u56de\u5fc6\u00b7\u795d\u798f\u9009\u53d6", 28, TextAnchor.MiddleCenter, PaleGold, 34f);
        AddTextBlock(header.transform, "\u4e00\u6b21\u6027\u9009\u5b9a\u672c\u5c40\u521d\u59cb\u795d\u798f\u3002", 15, TextAnchor.MiddleCenter, Gold, 22f);

        var tierTabs = CreateLayoutObject("TierTabs", window.transform);
        tierTabs.AddComponent<LayoutElement>().preferredHeight = 42f;
        var tierTabsLayout = tierTabs.AddComponent<HorizontalLayoutGroup>();
        tierTabsLayout.spacing = 12f;
        tierTabsLayout.childControlWidth = true;
        tierTabsLayout.childControlHeight = true;
        tierTabsLayout.childForceExpandWidth = true;
        tierTabsLayout.childForceExpandHeight = true;
        foreach (var tier in OrderedTiers())
        {
            CreateTierButton(tierTabs.transform, tier);
        }

        var labelRow = CreateLayoutObject("ColumnLabels", window.transform);
        labelRow.AddComponent<LayoutElement>().preferredHeight = 48f;
        var labelLayout = labelRow.AddComponent<HorizontalLayoutGroup>();
        labelLayout.spacing = 34f;
        labelLayout.childControlWidth = true;
        labelLayout.childControlHeight = true;
        labelLayout.childForceExpandWidth = true;
        labelLayout.childForceExpandHeight = true;
        CreateColumnHeader(labelRow.transform, "\u53ef\u9009\u795d\u798f", out _);
        CreateColumnHeader(labelRow.transform, "\u5df2\u9009\u795d\u798f", out selectedCounterText);

        var listRow = CreateLayoutObject("ListRow", window.transform);
        var listElement = listRow.AddComponent<LayoutElement>();
        listElement.flexibleHeight = 1f;
        listElement.minHeight = 380f;
        var listLayout = listRow.AddComponent<HorizontalLayoutGroup>();
        listLayout.spacing = 34f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = true;

        candidateListContent = CreateScroll(listRow.transform, "CandidateBlessings");
        selectedListContent = CreateScroll(listRow.transform, "SelectedBlessings");
        candidateListDirty.Reset();
        selectedListDirty.Reset();

        var footer = CreateLayoutObject("Footer", window.transform);
        var footerElement = footer.AddComponent<LayoutElement>();
        footerElement.minHeight = FooterHeight;
        footerElement.preferredHeight = FooterHeight;
        ApplyPanelImage(footer, FooterTint);
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.padding = new RectOffset(14, 14, 12, 12);
        footerLayout.spacing = 9f;
        footerLayout.childControlHeight = true;
        footerLayout.childControlWidth = true;
        footerLayout.childForceExpandHeight = false;
        footerLayout.childForceExpandWidth = false;
        hintText = AddTextBlock(footer.transform, "", 14, TextAnchor.MiddleCenter, PaleGold, ButtonHeight, 1f);

        var footerButtons = CreateLayoutObject("FooterButtons", footer.transform);
        var footerButtonsElement = footerButtons.AddComponent<LayoutElement>();
        footerButtonsElement.minWidth = MainButtonWidth * 3f + 14f * 2f;
        footerButtonsElement.preferredWidth = footerButtonsElement.minWidth;
        footerButtonsElement.minHeight = ButtonHeight;
        footerButtonsElement.preferredHeight = ButtonHeight;
        var footerButtonsLayout = footerButtons.AddComponent<HorizontalLayoutGroup>();
        footerButtonsLayout.spacing = 14f;
        footerButtonsLayout.childControlWidth = true;
        footerButtonsLayout.childControlHeight = true;
        footerButtonsLayout.childForceExpandWidth = false;
        footerButtonsLayout.childForceExpandHeight = false;

        CreateButton(footerButtons.transform, "\u81ea\u52a8\u586b\u5145", new Vector2(MainButtonWidth, ButtonHeight), AutoFillSelection);
        CreateButton(footerButtons.transform, "\u6e05\u7a7a", new Vector2(MainButtonWidth, ButtonHeight), ClearSelection);
        CreateButton(footerButtons.transform, "\u786e\u8ba4", new Vector2(MainButtonWidth, ButtonHeight), () => ConfirmSelection(onCompleted));

        RefreshAll();
    }

    private static void BuildBlessingPools()
    {
        blessingPools.Clear();
        foreach (var tier in OrderedTiers())
        {
            blessingPools[tier] = new List<BlessingEntry>();
        }

        try
        {
            var rows = SunExpConfigIndex.Rows(DataType.Bless);
            var checkedRows = Singleton<GameConfigManager>.Instance.CardPackCheck(rows);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in checkedRows)
            {
                if (!row.TryGetValue("Id", out var id)
                    || string.IsNullOrWhiteSpace(id)
                    || IsTechnicalBlessing(id)
                    || !seen.Add(id))
                {
                    continue;
                }

                if (Singleton<GameRuntimeData>.Instance.IsLocked(id))
                {
                    continue;
                }

                var tier = row.TryGetValue("Rarity", out var rarity)
                    ? DictionaryUtil.ParseInt(rarity, -1)
                    : -1;
                if (!blessingPools.ContainsKey(tier))
                {
                    continue;
                }

                blessingPools[tier].Add(new BlessingEntry(id, tier, BlessDisplayName(id), BlessDescription(id), BlessIconPath(id)));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to build solar memory blessing pools", ex);
        }

        foreach (var tier in OrderedTiers())
        {
            blessingPools[tier] = blessingPools[tier]
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static void LoadSelectionFromGameVar()
    {
        selectedByTier.Clear();
        foreach (var tier in OrderedTiers())
        {
            selectedByTier[tier] = new List<string>();
        }

        var savedIds = SolarMemoryPlayerSetupState.SelectedBlessings();
        if (savedIds.Count == 0)
        {
            return;
        }

        foreach (var id in savedIds)
        {
            var entry = FindEntry(id);
            if (entry == null)
            {
                continue;
            }

            var selected = selectedByTier[entry.Tier];
            if (selected.Count < QuotaForTier(entry.Tier))
            {
                selected.Add(entry.Id);
            }
        }
    }

    private static void RefreshAll()
    {
        RefreshCandidateList();
        RefreshSelectedList();
        RefreshCounters();
        PersistSelection();
    }

    private static void RefreshCandidateList()
    {
        if (candidateListContent == null)
        {
            return;
        }

        if (!blessingPools.TryGetValue(activeTier, out var entries) || entries.Count == 0)
        {
            if (!candidateListDirty.ShouldRefresh("empty:" + activeTier))
            {
                return;
            }

            ClearChildren(candidateListContent);
            AddTextBlock(candidateListContent, "\u5f53\u524d\u9636\u5c42\u6ca1\u6709\u53ef\u9009\u795d\u798f\u3002", 15, TextAnchor.MiddleCenter, Gold, 40f);
            return;
        }

        var key = activeTier + ":" + string.Join("|", entries.Select(entry => entry.Id));
        if (!candidateListDirty.ShouldRefresh(key))
        {
            return;
        }

        ClearChildren(candidateListContent);
        foreach (var entry in entries)
        {
            CreateCandidateRow(candidateListContent, entry);
        }
    }

    private static void RefreshSelectedList()
    {
        if (selectedListContent == null)
        {
            return;
        }

        var key = string.Join("|", OrderedTiers().SelectMany(tier =>
            selectedByTier[tier].Select((id, index) => tier + ":" + index + ":" + id)));
        if (!selectedListDirty.ShouldRefresh(key))
        {
            return;
        }

        var desired = new List<(BlessingEntry Entry, int Tier, int Index)>();
        foreach (var tier in OrderedTiers())
        {
            for (var i = 0; i < selectedByTier[tier].Count; i++)
            {
                var id = selectedByTier[tier][i];
                var entry = FindEntry(id);
                if (entry != null)
                {
                    desired.Add((entry, tier, i));
                }
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < selectedRows.Count && selectedRows[i] != null)
            {
                var row = selectedRows[i];
                row.gameObject.name = "Selected-" + item.Entry.Id + "-" + item.Index;
                if (row.transform.parent != selectedListContent)
                {
                    row.transform.SetParent(selectedListContent, false);
                }

                BindSelectedRow(row, item.Entry, item.Tier, item.Index);
                continue;
            }

            var created = CreateSelectedRow(selectedListContent, item.Entry, item.Tier, item.Index);
            if (i < selectedRows.Count)
            {
                selectedRows[i] = created;
            }
            else
            {
                selectedRows.Add(created);
            }
        }

        for (var i = selectedRows.Count - 1; i >= desired.Count; i--)
        {
            var row = selectedRows[i];
            selectedRows.RemoveAt(i);
            if (row != null)
            {
                SunExpUiPool.Release(
                    row.gameObject,
                    "SolarMemoryBlessingPicker.RefreshSelectedList",
                    "[SolarMemoryBlessingPicker]");
            }
        }
    }

    private static void RefreshCounters()
    {
        foreach (var tier in OrderedTiers())
        {
            if (tierCounterTexts.TryGetValue(tier, out var text))
            {
                text.text = TierLabel(tier) + " " + SelectedCount(tier) + "/" + QuotaForTier(tier);
                text.color = SelectedCount(tier) == QuotaForTier(tier) ? Green : PaleGold;
            }
        }

        if (selectedCounterText != null)
        {
            selectedCounterText.text = TotalSelectedCount() + "/" + TotalBlessingQuota;
            selectedCounterText.color = IsSelectionComplete() ? Green : PaleGold;
        }

        UpdateHint(IsSelectionComplete()
            ? "\u53ef\u4ee5\u786e\u8ba4\u3002"
            : "\u8bf7\u6309\u5404\u9636\u5c42\u914d\u989d\u9009\u6ee1\u795d\u798f\u3002");
    }

    private static void CreateCandidateRow(Transform parent, BlessingEntry entry)
    {
        AcquireBlessingRow(parent, "Candidate-" + entry.Id, row => row.Bind(
            entry.Name,
            TierLabel(entry.Tier),
            entry.Description,
            TryLoadBlessIcon(entry),
            entry.Tier.ToString(),
            "\u6dfb\u52a0",
            () => AddBlessing(entry)));
    }

    private static BlessingRowView CreateSelectedRow(Transform parent, BlessingEntry entry, int tier, int index)
    {
        return AcquireBlessingRow(
            parent,
            "Selected-" + entry.Id + "-" + index,
            row => BindSelectedRow(row, entry, tier, index));
    }

    private static void BindSelectedRow(BlessingRowView row, BlessingEntry entry, int tier, int index)
    {
        row.Bind(
            entry.Name,
            TierLabel(entry.Tier),
            entry.Description,
            TryLoadBlessIcon(entry),
            entry.Tier.ToString(),
            "\u79fb\u9664",
            () => RemoveBlessing(tier, index));
    }

    private static void AddBlessing(BlessingEntry entry)
    {
        var selected = selectedByTier[entry.Tier];
        if (selected.Count >= QuotaForTier(entry.Tier))
        {
            UpdateHint(TierLabel(entry.Tier) + "\u5df2\u8fbe\u5230\u53ef\u9009\u6570\u91cf\u4e0a\u9650\u3002");
            return;
        }

        selected.Add(entry.Id);
        RefreshAll();
    }

    private static void RemoveBlessing(int tier, int index)
    {
        if (selectedByTier.TryGetValue(tier, out var selected) && index >= 0 && index < selected.Count)
        {
            selected.RemoveAt(index);
        }

        RefreshAll();
    }

    private static void AutoFillSelection()
    {
        foreach (var tier in OrderedTiers())
        {
            if (!blessingPools.TryGetValue(tier, out var entries))
            {
                continue;
            }

            var selected = selectedByTier[tier];
            if (entries.Count == 0)
            {
                continue;
            }

            var index = 0;
            while (selected.Count < QuotaForTier(tier))
            {
                selected.Add(entries[index % entries.Count].Id);
                index++;
            }
        }

        RefreshAll();
    }

    private static void ClearSelection()
    {
        foreach (var selected in selectedByTier.Values)
        {
            selected.Clear();
        }

        RefreshAll();
    }

    private static void ConfirmSelection(Action onCompleted)
    {
        if (isConfirming)
        {
            return;
        }

        if (!IsSelectionComplete())
        {
            UpdateHint("\u8bf7\u5148\u9009\u6ee1\u6240\u6709\u9636\u5c42\u914d\u989d\u3002");
            return;
        }

        try
        {
            isConfirming = true;
            var ids = SelectedIds().ToList();
            SolarMemoryPlayerSetupState.SetSelectedBlessings(ids);
            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryBlessConfiguredKey, true);
            foreach (var id in ids)
            {
                PlayerApi.AddBless(id);
            }

            Close();
            onCompleted();
        }
        catch (Exception ex)
        {
            isConfirming = false;
            SunExpLog.Error("Failed to confirm solar memory blessings", ex);
            UpdateHint("\u795d\u798f\u53d1\u653e\u5931\u8d25\uff0c\u8bf7\u91cd\u8bd5\u3002");
        }
    }

    private static bool IsSelectionComplete()
    {
        foreach (var tier in OrderedTiers())
        {
            if (SelectedCount(tier) != QuotaForTier(tier))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> SelectedIds()
    {
        foreach (var tier in OrderedTiers())
        {
            foreach (var id in selectedByTier[tier])
            {
                yield return id;
            }
        }
    }

    private static void PersistSelection()
    {
        SolarMemoryPlayerSetupState.SetSelectedBlessings(SelectedIds());
    }

    private static int TotalSelectedCount()
    {
        return OrderedTiers().Sum(SelectedCount);
    }

    private static int SelectedCount(int tier)
    {
        return selectedByTier.TryGetValue(tier, out var selected) ? selected.Count : 0;
    }

    private static BlessingEntry? FindEntry(string id)
    {
        foreach (var entries in blessingPools.Values)
        {
            var entry = entries.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                return entry;
            }
        }

        return null;
    }

    private static int QuotaForTier(int tier)
    {
        return tier switch
        {
            4 => Tier4Quota,
            3 => Tier3Quota,
            2 => Tier2Quota,
            1 => Tier1Quota,
            _ => 0
        };
    }

    private static bool IsTechnicalBlessing(string id)
    {
        return id.Equals("dusk_afterheat_recovery", StringComparison.OrdinalIgnoreCase)
            || id.Equals("SunExp_sunexp_dusk_afterheat_recovery", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("_dusk_afterheat_recovery", StringComparison.OrdinalIgnoreCase)
            || id.Equals("star_clay_doll_placeholder", StringComparison.OrdinalIgnoreCase)
            || id.Equals(SunExpIds.StarClayDollBlessingId, StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("_star_clay_doll_placeholder", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<int> OrderedTiers()
    {
        yield return 4;
        yield return 3;
        yield return 2;
        yield return 1;
    }

    private static string TierLabel(int tier)
    {
        return tier + "\u9636";
    }

    private static void CreateTierButton(Transform parent, int tier)
    {
        var go = CreateLayoutObject("Tier-" + tier, parent);
        var element = go.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minHeight = ButtonHeight;
        element.preferredHeight = ButtonHeight;
        var image = ApplyButtonImage(go);
        var button = go.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, PaleGold);
        button.onClick.AddListener(() =>
        {
            activeTier = tier;
            RefreshAll();
        });
        tierCounterTexts[tier] = AddTextFill(go.transform, "", 14, TextAnchor.MiddleCenter, PaleGold);
    }

    private static Transform CreateScroll(Transform parent, string name)
    {
        var root = CreateLayoutObject("Scroll-" + name, parent);
        var rootElement = root.AddComponent<LayoutElement>();
        rootElement.flexibleWidth = 1f;
        rootElement.minWidth = 300f;
        rootElement.flexibleHeight = 1f;
        rootElement.minHeight = 260f;
        ApplyPanelImage(root, AreaTint);

        var header = CreateBlessInfoHeader(root.transform);
        var headerRect = header.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(-8f, HeaderHeight);
        headerRect.anchoredPosition = new Vector2(0f, -4f);

        var viewport = CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.offsetMin = new Vector2(4f, 4f);
        viewportRect.offsetMax = new Vector2(-4f, -(HeaderHeight + 12f));
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateRect("Content", viewport.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.spacing = 8f;
        contentLayout.padding = new RectOffset(2, 2, 0, 0);
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 24f;
        return content.transform;
    }

    private static GameObject CreateBlessInfoHeader(Transform parent)
    {
        var header = CreateLayoutObject("BlessInfoHeader", parent);
        var element = header.AddComponent<LayoutElement>();
        element.minHeight = HeaderHeight;
        element.preferredHeight = HeaderHeight;
        element.flexibleHeight = 0f;
        ApplyPanelImage(header, HeaderTint);

        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 0, 0);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        AddTextBlock(header.transform, "\u56fe\u6807", 14, TextAnchor.MiddleCenter, PaleGold, HeaderHeight, 0f, IconColumnWidth);
        AddTextBlock(header.transform, "\u540d\u79f0", 14, TextAnchor.MiddleCenter, PaleGold, HeaderHeight, 0f, 124f);
        AddTextBlock(header.transform, "\u9636\u5c42", 14, TextAnchor.MiddleCenter, PaleGold, HeaderHeight, 0f, TierColumnWidth);
        AddTextBlock(header.transform, "\u6548\u679c", 14, TextAnchor.MiddleCenter, PaleGold, HeaderHeight, 1f);
        AddTextBlock(header.transform, "", 14, TextAnchor.MiddleCenter, PaleGold, HeaderHeight, 0f, InlineButtonWidth);
        return header;
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        var row = CreateLayoutObject(name, parent);
        var layoutElement = row.AddComponent<LayoutElement>();
        layoutElement.minHeight = BlessRowHeight;
        layoutElement.preferredHeight = BlessRowHeight;
        layoutElement.flexibleHeight = 0f;
        ApplyPanelImage(row, RowTint);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 6, 6);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        return row;
    }

    private static Button CreateInlineButton(Transform parent, string label, Action action)
    {
        var go = CreateLayoutObject("InlineButton-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = InlineButtonWidth;
        element.preferredWidth = InlineButtonWidth;
        element.minHeight = 32f;
        element.preferredHeight = 32f;
        var image = ApplyInlineButtonImage(go);
        var button = go.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, PaleGold);
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, 14, TextAnchor.MiddleCenter, PaleGold);
        return button;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Action action)
    {
        var go = CreateLayoutObject("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        var width = Mathf.Max(80f, size.x);
        element.minWidth = width;
        element.preferredWidth = width;
        element.minHeight = size.y;
        element.preferredHeight = size.y;
        var image = ApplyButtonImage(go);
        var button = go.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, PaleGold);
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, 14, TextAnchor.MiddleCenter, PaleGold);
        return button;
    }

    private static Image ApplyButtonImage(GameObject go)
    {
        var image = go.AddComponent<Image>();
        image.color = Color.white;
        image.sprite = SunExpUiSprites.Button("[SolarMemoryBlessingPicker]");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        if (image.sprite == null)
        {
            image.color = new Color(0.05f, 0.05f, 0.22f, 0.96f);
        }

        return image;
    }

    private static Image ApplyInlineButtonImage(GameObject go)
    {
        var image = go.AddComponent<Image>();
        image.sprite = SunExpUiSprites.Panel("[SolarMemoryBlessingPicker]");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, 0.96f) : new Color(0.04f, 0.04f, 0.18f, 0.96f);
        if (image.sprite != null)
        {
            SunExpUiBuilder.AddPanelTint(go, new Color(0.035f, 0.035f, 0.15f, 0.96f));
        }

        return image;
    }

    private static void ApplyPanelImage(GameObject go, Color fallbackOrTint)
    {
        SunExpUiBuilder.ApplyPanelImage(go, SunExpUiSprites.Panel("[SolarMemoryBlessingPicker]"), fallbackOrTint);
    }

    private static GameObject CreateColumnHeader(Transform parent, string title, out Text? counter)
    {
        var header = CreateLayoutObject("ColumnHeader-" + title, parent);
        var element = header.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minWidth = 300f;
        ApplyPanelImage(header, HeaderTint);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(14, 14, 6, 6);
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        AddTextBlock(header.transform, title, 17, TextAnchor.MiddleCenter, PaleGold, 32f, 1f);
        counter = AddTextBlock(header.transform, "", 16, TextAnchor.MiddleCenter, Green, 32f, 0f, 90f);
        return header;
    }

    private static void CreateBadge(Transform parent, string value)
    {
        var badge = CreateLayoutObject("Badge", parent);
        var element = badge.AddComponent<LayoutElement>();
        element.minWidth = IconColumnWidth;
        element.preferredWidth = IconColumnWidth;
        element.minHeight = BlessIconSize;
        element.preferredHeight = BlessIconSize;
        ApplyPanelImage(badge, DeepBlue);
        AddTextFill(badge.transform, value, 18, TextAnchor.MiddleCenter, PaleGold);
    }

    private static void CreateBlessIconCell(Transform parent, BlessingEntry entry)
    {
        var sprite = TryLoadBlessIcon(entry);
        if (sprite == null)
        {
            CreateBadge(parent, entry.Tier.ToString());
            return;
        }

        var cell = CreateLayoutObject("BlessIcon", parent);
        var element = cell.AddComponent<LayoutElement>();
        element.minWidth = IconColumnWidth;
        element.preferredWidth = IconColumnWidth;
        element.minHeight = BlessIconSize;
        element.preferredHeight = BlessIconSize;

        var icon = CreateRect("Image", cell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(BlessIconSize, BlessIconSize));
        var image = icon.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
    }

    private static Text AddTextBlock(Transform parent, string value, int fontSize, TextAnchor anchor, Color color,
        float preferredHeight, float flexibleWidth = 0f, float preferredWidth = 0f)
    {
        var go = CreateLayoutObject("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = preferredHeight;
        element.preferredHeight = preferredHeight;
        if (flexibleWidth > 0f)
        {
            element.flexibleWidth = flexibleWidth;
        }

        if (preferredWidth > 0f)
        {
            element.minWidth = preferredWidth;
            element.preferredWidth = preferredWidth;
        }

        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text AddTextFill(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var text = go.AddComponent<Text>();
        text.text = value;
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Math.Max(9, fontSize - 5);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateLayoutObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        return go;
    }

    private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = Vector2.zero;
        return go;
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var available = new Vector2(Screen.width, Screen.height);
        if (parent is RectTransform rect && rect.rect.width > 0f && rect.rect.height > 0f)
        {
            available = rect.rect.size;
        }

        var width = Mathf.Min(1240f, Mathf.Max(820f, available.x - 60f));
        var height = Mathf.Min(800f, Mathf.Max(680f, available.y - 28f));
        return new Vector2(width, height);
    }

    private static Sprite? TryLoadBlessIcon(BlessingEntry entry)
    {
        if (blessIconCache.TryGetValue(entry.Id, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.IconPath))
            {
                sprite = SunExpResourceCache.Load<Sprite>(entry.IconPath, true);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryBlessingPicker] failed to load bless icon for " + entry.Id + ": " + ex.Message);
        }

        blessIconCache[entry.Id] = sprite;
        return sprite;
    }

    private static string BlessDisplayName(string blessId)
    {
        try
        {
            var data = new DataConfig(blessId, DataType.Bless).data;
            var localizedName = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != "Name")
            {
                return localizedName;
            }

            if (data.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch
        {
            return blessId;
        }

        return blessId;
    }

    private static string BlessDescription(string blessId)
    {
        try
        {
            var description = new DataConfig(blessId, DataType.Bless).Description();
            return string.IsNullOrWhiteSpace(description) ? blessId : description;
        }
        catch
        {
            return blessId;
        }
    }

    private static string BlessIconPath(string blessId)
    {
        try
        {
            var data = new DataConfig(blessId, DataType.Bless).data;
            return data.TryGetValue("Icon", out var icon) ? icon : "";
        }
        catch
        {
            return "";
        }
    }

    private static void ClearChildren(Transform? parent)
    {
        SunExpUiPool.ReleaseOrDestroyChildren(parent, "SolarMemoryBlessingPicker.ClearChildren", "[SolarMemoryBlessingPicker]");
    }

    private static void ReleaseTransientRows()
    {
        ClearChildren(candidateListContent);
        ClearChildren(selectedListContent);
        selectedRows.Clear();
    }

    private static void UpdateHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static BlessingRowView AcquireBlessingRow(
        Transform parent,
        string name,
        Action<BlessingRowView> configureBeforeActivation)
    {
        return SunExpUiPool.AcquireConfiguredComponent(
            "SolarMemoryBlessingPicker.Row",
            parent,
            name,
            CreateBlessingRowTemplate,
            configureBeforeActivation);
    }

    private static BlessingRowView CreateBlessingRowTemplate(Transform parent, string name)
    {
        var row = CreateRow(parent, name);
        var iconCell = CreateLayoutObject("BlessIcon", row.transform);
        var iconElement = iconCell.AddComponent<LayoutElement>();
        iconElement.minWidth = IconColumnWidth;
        iconElement.preferredWidth = IconColumnWidth;
        iconElement.minHeight = BlessIconSize;
        iconElement.preferredHeight = BlessIconSize;
        ApplyPanelImage(iconCell, DeepBlue);

        var icon = CreateRect("Image", iconCell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(BlessIconSize, BlessIconSize));
        var iconImage = icon.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.color = Color.white;

        var badgeText = AddTextFill(iconCell.transform, "", 18, TextAnchor.MiddleCenter, PaleGold);
        var nameText = AddTextBlock(row.transform, "", 15, TextAnchor.MiddleCenter, PaleGold, 42f, 0f, 124f);
        var tierText = AddTextBlock(row.transform, "", 13, TextAnchor.MiddleCenter, Gold, 42f, 0f, TierColumnWidth);
        var descriptionText = AddTextBlock(row.transform, "", 12, TextAnchor.MiddleLeft, PaleGold, 42f, 1f);
        var button = CreateInlineButton(row.transform, "", () => { });
        var buttonText = button.GetComponentInChildren<Text>();

        var view = row.AddComponent<BlessingRowView>();
        view.Initialize(iconImage, badgeText, nameText, tierText, descriptionText, button, buttonText);
        return view;
    }

    private sealed class BlessingRowView : SunExpPooledUiBehaviour
    {
        private readonly SunExpUiLifetimeScope lifetime = new();
        private Image? iconImage;
        private Text? badgeText;
        private Text? nameText;
        private Text? tierText;
        private Text? descriptionText;
        private Button? button;
        private Text? buttonText;

        public void Initialize(
            Image iconImage,
            Text badgeText,
            Text nameText,
            Text tierText,
            Text descriptionText,
            Button button,
            Text? buttonText)
        {
            this.iconImage = iconImage;
            this.badgeText = badgeText;
            this.nameText = nameText;
            this.tierText = tierText;
            this.descriptionText = descriptionText;
            this.button = button;
            this.buttonText = buttonText;
        }

        public void Bind(
            string name,
            string tier,
            string description,
            Sprite? icon,
            string badge,
            string buttonLabel,
            Action action)
        {
            lifetime.Clear();
            SetText(nameText, name);
            SetText(tierText, tier);
            SetText(descriptionText, description);
            SetText(buttonText, buttonLabel);
            SetText(badgeText, badge);

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.gameObject.SetActive(icon != null);
            }

            if (badgeText != null)
            {
                badgeText.gameObject.SetActive(icon == null);
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.interactable = true;
                lifetime.Listen(button, () => action());
            }
        }

        public override void ResetForPool()
        {
            lifetime.Clear();
            SetText(nameText, "");
            SetText(tierText, "");
            SetText(descriptionText, "");
            SetText(buttonText, "");
            SetText(badgeText, "");
            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.gameObject.SetActive(false);
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.interactable = false;
            }
        }

        private static void SetText(Text? text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }
    }

    private sealed class BlessingEntry
    {
        public BlessingEntry(string id, int tier, string name, string description, string iconPath)
        {
            Id = id;
            Tier = tier;
            Name = name;
            Description = description;
            IconPath = iconPath;
        }

        public string Id { get; }

        public int Tier { get; }

        public string Name { get; }

        public string Description { get; }

        public string IconPath { get; }
    }
}
