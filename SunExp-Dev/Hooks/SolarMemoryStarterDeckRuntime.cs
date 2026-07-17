using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraUi.Shared;
using StarterDeckArbiter.Shared;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryStarterDeckRuntime
{
    private const int StarterDeckSize = 11;
    private const float CardInfoHeaderHeight = 40f;
    private const float CardRowHeight = 40f;
    private const float CardImageColumnWidth = 38f;
    private const float CardIconSize = 32f;
    private const float InlineButtonWidth = 96f;
    private const float MainButtonWidth = 112f;
    private const float FooterHeight = 64f;
    private const float ButtonHeight = 40f;
    private static readonly Color Gold = new(0.82f, 0.72f, 0.42f);
    private static readonly Color PaleGold = new(0.93f, 0.86f, 0.58f);
    private static readonly Color DimGold = new(0.55f, 0.46f, 0.25f);
    private static readonly Color DeepBlue = new(0.02f, 0.02f, 0.16f, 0.98f);
    private static readonly Color HeaderTint = new(0.025f, 0.025f, 0.14f, 0.98f);
    private static readonly Color AreaTint = new(0.018f, 0.018f, 0.105f, 0.98f);
    private static readonly Color FooterTint = new(0.018f, 0.018f, 0.115f, 0.96f);
    private static readonly Color RowTint = new(0.07f, 0.07f, 0.21f, 0.98f);
    private static readonly HashSet<string> selectedPacks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> editingDeck = new();
    private static readonly Dictionary<string, Sprite?> cardIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static RoleTable? pendingRoleTable;
    private static GameObject? activePanel;
    private static Transform? candidateListContent;
    private static Transform? deckListContent;
    private static Text? deckCounterText;
    private static Text? hintText;
    private static readonly SunExpDirtyState deckListDirty = new();
    private static bool promptShown;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "RoleTable.Init", MarkPendingFromRoleTableInit);
        RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", MarkPendingFromRoleInit);
        RegisterAfter(modConfig, "MapManager.MapUIStart", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "NormalMapManager.MapUIStart", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "MapSelectUI.Start", TryShowStarterDeckEditor);
    }

    public static void CaptureSelectedPacks(IEnumerable<string> packs)
    {
        selectedPacks.Clear();
        foreach (var pack in packs.Where(IsValidPackForCurrentLobby))
        {
            selectedPacks.Add(pack);
        }

        SolarMemoryPlayerSetupState.SetSelectedPacks(RoleTable.Instance, selectedPacks);
        pendingRoleTable = null;
        promptShown = false;
        ClosePanel();
        SunExpLog.Info("[SolarMemoryStarterDeck] captured packs: " + string.Join("|", selectedPacks.OrderBy(id => id)));
    }

    public static void MarkPending(RoleTable roleTable, string source)
    {
        if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || IsApplied(roleTable))
        {
            return;
        }

        pendingRoleTable = roleTable;
        ClaimStarterDeckOwnership(roleTable, SunExpIds.StarterDeckStatePending);
        if (selectedPacks.Count > 0)
        {
            SolarMemoryPlayerSetupState.SetSelectedPacks(roleTable, selectedPacks);
        }

        promptShown = false;
        SunExpLog.Info("[SolarMemoryStarterDeck] pending after " + source + "; currentDeck=" + roleTable.cardList.Count);
    }

    public static bool OpenOrResume()
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun() || RoleTable.Instance == null)
            {
                SunExpLog.Warn("[SolarMemoryStarterDeck] OpenOrResume skipped: run or role table unavailable.");
                return false;
            }

            if (IsApplied(RoleTable.Instance))
            {
                SunExpLog.Info("[SolarMemoryStarterDeck] OpenOrResume skipped: starter deck already applied.");
                return false;
            }

            MarkPending(RoleTable.Instance, "SolarMemoryPreparationRuntime.OpenOrResume");
            return TryShowStarterDeckEditor("SolarMemoryPreparationRuntime.OpenOrResume");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory starter deck OpenOrResume failed", ex);
            return false;
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "SolarMemoryStarterDeck");
    }

    private static void MarkPendingFromRoleInit(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            var roleTable = ResolveRoleTable(context);
            if (roleTable == null)
            {
                SunExpLog.Warn("[SolarMemoryStarterDeck] role table is null after NormalMapManager.InitRoleTable.");
                return;
            }

            SolarMemoryDeckIsolationRuntime.SanitizeSolarMemoryRoleCards(roleTable, "NormalMapManager.InitRoleTable");
            MarkPending(roleTable, "NormalMapManager.InitRoleTable");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory starter deck pending hook failed", ex);
        }
    }

    private static void MarkPendingFromRoleTableInit(ModHookContext context)
    {
        MarkPendingFromRoleHook(context, "RoleTable.Init");
    }

    private static void MarkPendingFromRoleHook(ModHookContext context, string source)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            var roleTable = ResolveRoleTable(context);
            if (roleTable == null)
            {
                SunExpLog.Warn("[SolarMemoryStarterDeck] role table is null after " + source + ".");
                return;
            }

            SolarMemoryDeckIsolationRuntime.SanitizeSolarMemoryRoleCards(roleTable, source);
            MarkPending(roleTable, source);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory starter deck pending hook failed", ex);
        }
    }

    private static RoleTable? ResolveRoleTable(ModHookContext context)
    {
        if (context.Arguments != null
            && context.Arguments.Length > 0
            && context.Arguments[0] is RoleTable argumentRole)
        {
            return argumentRole;
        }

        return context.Target as RoleTable ?? RoleTable.Instance;
    }

    private static RoleTable? ActiveRoleTable()
    {
        return RoleTable.Instance ?? pendingRoleTable;
    }

    private static void TryShowStarterDeckEditor(ModHookContext context)
    {
        TryShowStarterDeckEditor("hook");
    }

    private static bool TryShowStarterDeckEditor(string source)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return false;
            }

            var roleTable = ActiveRoleTable();
            if (roleTable == null || promptShown || IsApplied(roleTable))
            {
                return false;
            }

            SolarMemoryDeckIsolationRuntime.SanitizeSolarMemoryRoleCards(roleTable, "TryShowStarterDeckEditor:" + source);
            ClaimStarterDeckOwnership(roleTable, SunExpIds.StarterDeckStatePending);
            var candidates = BuildCandidateCardIds();
            if (candidates.Count == 0)
            {
                KeepOfficialDeck(roleTable, "no-candidate");
                SunExpLog.Warn("[SolarMemoryStarterDeck] no valid card candidates; keeping official starter deck.");
                return true;
            }

            promptShown = true;
            SunExpLog.Info("[SolarMemoryStarterDeck] opening editor from "
                + source
                + "; candidates="
                + candidates.Count
                + "; currentDeck="
                + roleTable.cardList.Count);
            ShowStarterDeckEditor(roleTable, candidates);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to show solar memory starter deck editor", ex);
            return false;
        }
    }

    private static List<string> BuildCandidateCardIds()
    {
        IEnumerable<string> packs = selectedPacks.Count > 0 ? selectedPacks : SolarMemoryDeckIsolationRuntime.CurrentPackSelection();
        return CardIdsFromPacks(packs)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal))
            .Where(id => !SolarMemoryDeckIsolationRuntime.IsSolarMemoryEventCard(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    private static List<string> CardIdsFromPacks(IEnumerable<string> packIds)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packId in packIds.Where(IsValidPackForCurrentLobby).OrderBy(id => id))
        {
            foreach (var card in GameCompatibilityApi.GetItemsByPack(DataType.Card, packId))
            {
                if (card.TryGetValue("Id", out var id) && !string.IsNullOrWhiteSpace(id) && seen.Add(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static bool IsValidPackForCurrentLobby(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && (!string.Equals(id, "cardpack_13", StringComparison.OrdinalIgnoreCase) || GameCompatibilityApi.ShouldEnableOnlineCardPack());
    }

    private static void ShowStarterDeckEditor(RoleTable roleTable, IReadOnlyList<string> candidates)
    {
        ClosePanel();
        editingDeck.Clear();
        editingDeck.AddRange(BuildAutoDeck(candidates));

        var parent = SunExpModalHost.ModalParent();
        if (parent == null)
        {
            SunExpLog.Warn("[SolarMemoryStarterDeck] skipped: UI canvas unavailable.");
            return;
        }

        activePanel = SunExpModalHost.CreateFullscreenRoot(
            "SunExpSolarMemoryStarterDeck",
            parent,
            new Color(0f, 0f, 0f, 0.74f));

        var window = CreateRect("Window", activePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ResolveWindowSize(parent));
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
        AddTextBlock(header.transform, "\u65e5\u8000\u56de\u5fc6\u00b7\u521d\u59cb\u5957\u5361", 28, TextAnchor.MiddleCenter, PaleGold, 34f);
        AddTextBlock(header.transform, "\u4ece\u672c\u6b21\u65e5\u8000\u56de\u5fc6\u542f\u7528\u5361\u5305\u4e2d\u9009\u62e9 11 \u5f20\u521d\u59cb\u724c\u3002\u53ea\u66ff\u6362\u672c\u5c40\u521d\u59cb\u724c\u7ec4\uff0c\u4e0d\u5199\u5165\u5956\u52b1\u5019\u9009\u6c60\u3002", 15, TextAnchor.MiddleCenter, Gold, 22f);

        var labelRow = CreateLayoutObject("ColumnLabels", window.transform);
        labelRow.AddComponent<LayoutElement>().preferredHeight = 48f;
        var labelLayout = labelRow.AddComponent<HorizontalLayoutGroup>();
        labelLayout.spacing = 34f;
        labelLayout.childControlWidth = true;
        labelLayout.childControlHeight = true;
        labelLayout.childForceExpandWidth = true;
        labelLayout.childForceExpandHeight = true;
        CreateColumnHeader(labelRow.transform, "\u53ef\u9009\u5361\u724c", out _);
        CreateColumnHeader(labelRow.transform, "\u5df2\u9009\u5957\u5361", out deckCounterText);

        var listRow = CreateLayoutObject("ListRow", window.transform);
        var listElement = listRow.AddComponent<LayoutElement>();
        listElement.flexibleHeight = 1f;
        listElement.minHeight = 400f;
        var listLayout = listRow.AddComponent<HorizontalLayoutGroup>();
        listLayout.spacing = 34f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = true;

        candidateListContent = CreateScroll(listRow.transform, "CandidateCards");
        foreach (var cardId in candidates)
        {
            CreateCandidateRow(candidateListContent, cardId);
        }

        deckListContent = CreateScroll(listRow.transform, "SelectedDeck");
        deckListDirty.Reset();

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
        footerButtonsElement.minWidth = MainButtonWidth * 4f + 14f * 3f;
        footerButtonsElement.preferredWidth = footerButtonsElement.minWidth;
        footerButtonsElement.minHeight = ButtonHeight;
        footerButtonsElement.preferredHeight = ButtonHeight;
        var footerButtonsLayout = footerButtons.AddComponent<HorizontalLayoutGroup>();
        footerButtonsLayout.spacing = 14f;
        footerButtonsLayout.childControlWidth = true;
        footerButtonsLayout.childControlHeight = true;
        footerButtonsLayout.childForceExpandWidth = false;
        footerButtonsLayout.childForceExpandHeight = false;

        CreateButton(footerButtons.transform, "\u81ea\u52a8\u586b\u5145", new Vector2(MainButtonWidth, ButtonHeight), () =>
        {
            editingDeck.Clear();
            editingDeck.AddRange(BuildAutoDeck(candidates));
            RefreshDeckList(roleTable);
        });
        CreateButton(footerButtons.transform, "\u6e05\u7a7a", new Vector2(MainButtonWidth, ButtonHeight), () =>
        {
            editingDeck.Clear();
            RefreshDeckList(roleTable);
        });
        CreateButton(footerButtons.transform, "\u4fdd\u7559\u9ed8\u8ba4", new Vector2(MainButtonWidth, ButtonHeight), () => KeepOfficialDeck(roleTable));
        CreateButton(footerButtons.transform, "\u4f7f\u7528\u5957\u5361", new Vector2(MainButtonWidth, ButtonHeight), () =>
        {
            if (editingDeck.Count == StarterDeckSize)
            {
                ApplyStarterDeck(roleTable, editingDeck.ToList());
                return;
            }

            UpdateHint("\u9700\u8981\u9009\u62e9\u6ee1 11 \u5f20\u724c\u624d\u80fd\u4f7f\u7528\u5957\u5361\u3002");
        });

        RefreshDeckList(roleTable);
    }

    private static List<string> BuildAutoDeck(IReadOnlyList<string> candidates)
    {
        var deck = candidates.Take(StarterDeckSize).ToList();
        for (var i = 0; deck.Count < StarterDeckSize; i++)
        {
            deck.Add(candidates[i % candidates.Count]);
        }

        return deck;
    }

    private static void CreateCandidateRow(Transform parent, string cardId)
    {
        var row = AcquireCardRow(parent, "Candidate-" + cardId);
        row.Bind(
            CardDisplayName(cardId),
            CardRarity(cardId),
            CardCost(cardId),
            TryLoadCardIcon(cardId),
            CardCost(cardId),
            "\u6dfb\u52a0",
            () =>
        {
            if (editingDeck.Count >= StarterDeckSize)
            {
                UpdateHint("\u5957\u5361\u5df2\u6ee1\uff0c\u9700\u8981\u5148\u79fb\u9664\u4e00\u5f20\u724c\u3002");
                return;
            }

            editingDeck.Add(cardId);
            RefreshDeckList(ActiveRoleTable());
        });
    }

    private static void RefreshDeckList(RoleTable? roleTable)
    {
        if (deckListContent == null)
        {
            return;
        }

        var key = string.Join("|", editingDeck.Select((id, index) => index + ":" + id));
        if (!deckListDirty.ShouldRefresh(key))
        {
            RefreshDeckCounterAndHint();
            return;
        }

        SunExpUiPool.ReleaseOrDestroyChildren(deckListContent, "SolarMemoryStarterDeck.RefreshDeckList", "[SolarMemoryStarterDeck]");

        for (var i = 0; i < editingDeck.Count; i++)
        {
            var index = i;
            var cardId = editingDeck[i];
            var row = AcquireCardRow(deckListContent, "Deck-" + i);
            row.Bind(
                CardDisplayName(cardId),
                CardRarity(cardId),
                CardCost(cardId),
                TryLoadCardIcon(cardId),
                (i + 1).ToString(),
                "\u79fb\u9664",
                () =>
            {
                if (index >= 0 && index < editingDeck.Count)
                {
                    editingDeck.RemoveAt(index);
                    RefreshDeckList(roleTable);
                }
            });
        }

        RefreshDeckCounterAndHint();
    }

    private static void RefreshDeckCounterAndHint()
    {
        if (deckCounterText != null)
        {
            deckCounterText.text = editingDeck.Count + "/" + StarterDeckSize;
            deckCounterText.color = editingDeck.Count == StarterDeckSize ? new Color(0.62f, 0.94f, 0.62f) : PaleGold;
        }

        UpdateHint(editingDeck.Count == StarterDeckSize
            ? "\u53ef\u4ee5\u786e\u8ba4\u3002"
            : "\u9700\u8981\u9009\u62e9\u6ee1 11 \u5f20\u724c\u624d\u80fd\u4f7f\u7528\u5957\u5361\u3002");
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

        var header = CreateCardInfoHeader(root.transform);
        var headerRect = header.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(-8f, CardInfoHeaderHeight);
        headerRect.anchoredPosition = new Vector2(0f, -4f);

        var viewport = CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.offsetMin = new Vector2(4f, 4f);
        viewportRect.offsetMax = new Vector2(-4f, -(CardInfoHeaderHeight + 12f));
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateRect("Content", viewport.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f));
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

    private static GameObject CreateCardInfoHeader(Transform parent)
    {
        var header = CreateLayoutObject("CardInfoHeader", parent);
        var element = header.AddComponent<LayoutElement>();
        element.minHeight = CardInfoHeaderHeight;
        element.preferredHeight = CardInfoHeaderHeight;
        element.flexibleHeight = 0f;
        ApplyPanelImage(header, HeaderTint);

        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 0, 0);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        AddTextBlock(header.transform, "\u5361\u56fe", 14, TextAnchor.MiddleCenter, PaleGold, CardInfoHeaderHeight, 0f, CardImageColumnWidth);
        AddTextBlock(header.transform, "\u5361\u724c\u540d\u79f0", 14, TextAnchor.MiddleCenter, PaleGold, CardInfoHeaderHeight, 1f);
        AddTextBlock(header.transform, "\u7a00\u6709\u5ea6", 14, TextAnchor.MiddleCenter, PaleGold, CardInfoHeaderHeight, 0f, 58f);
        AddTextBlock(header.transform, "\u8d39\u7528", 14, TextAnchor.MiddleCenter, PaleGold, CardInfoHeaderHeight, 0f, 48f);
        AddTextBlock(header.transform, "", 14, TextAnchor.MiddleCenter, PaleGold, CardInfoHeaderHeight, 0f, InlineButtonWidth);
        return header;
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        var row = CreateLayoutObject(name, parent);
        var layoutElement = row.AddComponent<LayoutElement>();
        layoutElement.minHeight = CardRowHeight;
        layoutElement.preferredHeight = CardRowHeight;
        layoutElement.flexibleHeight = 0f;
        ApplyPanelImage(row, RowTint);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
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
        image.sprite = SunExpUiSprites.Button("[SolarMemoryStarterDeck]");
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
        image.sprite = SunExpUiSprites.Panel("[SolarMemoryStarterDeck]");
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
        SunExpUiBuilder.ApplyPanelImage(go, SunExpUiSprites.Panel("[SolarMemoryStarterDeck]"), fallbackOrTint);
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
        counter = AddTextBlock(header.transform, "", 16, TextAnchor.MiddleCenter, new Color(0.62f, 0.94f, 0.62f), 32f, 0f, 86f);
        return header;
    }

    private static void CreateBadge(Transform parent, string value)
    {
        var badge = CreateLayoutObject("Badge", parent);
        var element = badge.AddComponent<LayoutElement>();
        element.minWidth = CardImageColumnWidth;
        element.preferredWidth = CardImageColumnWidth;
        element.minHeight = 32f;
        element.preferredHeight = 32f;
        ApplyPanelImage(badge, DeepBlue);
        AddTextFill(badge.transform, value, 18, TextAnchor.MiddleCenter, PaleGold);
    }

    private static void CreateCardIconCell(Transform parent, string cardId, string fallbackText)
    {
        var sprite = TryLoadCardIcon(cardId);
        if (sprite == null)
        {
            CreateBadge(parent, fallbackText);
            return;
        }

        var cell = CreateLayoutObject("CardIcon", parent);
        var element = cell.AddComponent<LayoutElement>();
        element.minWidth = CardImageColumnWidth;
        element.preferredWidth = CardImageColumnWidth;
        element.minHeight = CardIconSize;
        element.preferredHeight = CardIconSize;

        var icon = CreateRect("Image", cell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(CardIconSize, CardIconSize));
        var image = icon.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
    }

    private static Text AddTextBlock(Transform parent, string value, int fontSize, TextAnchor anchor, Color color, float preferredHeight, float flexibleWidth = 0f, float preferredWidth = 0f)
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

        var text = ConfigureText(go, value, fontSize, anchor, color);
        return text;
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
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
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

        var width = Mathf.Min(1120f, Mathf.Max(760f, available.x - 60f));
        var height = Mathf.Min(760f, Mathf.Max(660f, available.y - 28f));
        return new Vector2(width, height);
    }

    private static string CardDisplayName(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
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
            return cardId;
        }

        return cardId;
    }

    private static Sprite? TryLoadCardIcon(string cardId)
    {
        if (cardIconCache.TryGetValue(cardId, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            if (data.TryGetValue("Icon", out var iconPath) && !string.IsNullOrWhiteSpace(iconPath))
            {
                sprite = SunExpResourceCache.Load<Sprite>(iconPath, true);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SolarMemoryStarterDeck] failed to load card icon for " + cardId + ": " + ex.Message);
        }

        cardIconCache[cardId] = sprite;
        return sprite;
    }

    private static string CardRarity(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return data.TryGetValue("Rarity", out var rarity) && !string.IsNullOrWhiteSpace(rarity) ? "R" + rarity : "?";
        }
        catch
        {
            return "";
        }
    }

    private static string CardCost(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return data.TryGetValue("Expend", out var cost) && !string.IsNullOrWhiteSpace(cost) ? cost : "?";
        }
        catch
        {
            return "?";
        }
    }

    private static string CardSortKey(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var rarity = data.TryGetValue("Rarity", out var r) ? r : "9";
            var cost = data.TryGetValue("Expend", out var c) ? c : "9";
            return rarity.PadLeft(2, '0') + "|" + cost.PadLeft(2, '0') + "|" + cardId;
        }
        catch
        {
            return "99|99|" + cardId;
        }
    }

    private static void ApplyStarterDeck(RoleTable roleTable, IReadOnlyCollection<string> deck)
    {
        try
        {
            if (IsApplied(roleTable) || deck.Count != StarterDeckSize)
            {
                return;
            }

            var filteredDeck = deck
                .Where(id => !SolarMemoryDeckIsolationRuntime.IsSolarMemoryEventCard(id))
                .ToList();
            if (filteredDeck.Count != StarterDeckSize)
            {
                UpdateHint("\u4e8b\u4ef6\u5361\u5df2\u88ab\u8fc7\u6ee4\uff0c\u9700\u8981\u91cd\u65b0\u9009\u6ee1 11 \u5f20\u724c\u3002");
                return;
            }

            var originalDeckCount = roleTable.cardList.Count;
            if (!StarterDeckArbiterRuntime.ApplyDeck(
                    roleTable,
                    filteredDeck,
                    CreateClaim("custom"),
                    SolarMemoryDeckIsolationRuntime.IsSolarMemoryEventCard,
                    sync: false))
            {
                UpdateHint("\u5957\u5361\u5199\u5165\u5931\u8d25\uff0c\u8bf7\u91cd\u65b0\u9009\u62e9\u3002");
                return;
            }

            SolarMemoryDeckIsolationRuntime.SanitizeSolarMemoryRoleCards(roleTable, "ApplyStarterDeck");
            MarkPlayerApplied(roleTable, "custom");
            SolarMemoryDeckIsolationRuntime.ClearSolarMemoryReservePool(roleTable);
            ClosePanel();
            SolarMemoryPreparationRuntime.CompleteDeckSelection();

            SunExpLog.Info("[SolarMemoryStarterDeck] applied custom starter deck; originalDeck="
                + originalDeckCount
                + "; deck=" + roleTable.cardList.Count
                + "; cards=" + string.Join("|", filteredDeck));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to apply solar memory starter deck", ex);
        }
    }

    private static void KeepOfficialDeck(RoleTable roleTable)
    {
        KeepOfficialDeck(roleTable, "official");
    }

    private static void KeepOfficialDeck(RoleTable roleTable, string mode)
    {
        SolarMemoryDeckIsolationRuntime.SanitizeSolarMemoryRoleCards(roleTable, "KeepOfficialDeck");
        StarterDeckArbiterRuntime.KeepOfficialDeck(roleTable, CreateClaim(mode), sync: false);
        MarkPlayerApplied(roleTable, mode);
        SolarMemoryDeckIsolationRuntime.ClearSolarMemoryReservePool(roleTable);
        ClosePanel();
        SolarMemoryPreparationRuntime.CompleteDeckSelection();
        SunExpLog.Info("[SolarMemoryStarterDeck] kept official starter deck; deck=" + roleTable.cardList.Count);
    }

    private static bool IsApplied(RoleTable roleTable)
    {
        return SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryStarterDeckAppliedKey)
            || StarterDeckArbiterRuntime.HasApplied(
                roleTable,
                SunExpIds.SolarMemoryStarterDeckAppliedKey,
                SunExpIds.StarterDeckOwnerSolarMemory);
    }

    private static void MarkPlayerApplied(RoleTable roleTable, string mode)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[SunExpIds.SolarMemoryStarterDeckAppliedKey] = "1";
        roleTable.SpecialVarMap[SunExpIds.SolarMemoryStarterDeckModeKey] = mode;
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryStarterDeckAppliedKey, true);
        SolarMemoryPlayerSetupState.SetValue(SunExpIds.SolarMemoryStarterDeckModeKey, mode);
        pendingRoleTable = null;
    }

    private static void ClaimStarterDeckOwnership(RoleTable roleTable, string state)
    {
        StarterDeckArbiterRuntime.ClaimOwnership(
            roleTable,
            CreateClaim("pending"),
            string.IsNullOrWhiteSpace(state) ? StarterDeckArbiterRuntime.StatePending : state,
            false);
    }

    private static StarterDeckClaim CreateClaim(string mode)
    {
        return new StarterDeckClaim
        {
            Owner = SunExpIds.StarterDeckOwnerSolarMemory,
            Scope = SunExpIds.SolarMemoryModeKey,
            ModeId = "SunExp.SolarMemory",
            Source = "direct-ui",
            State = string.Equals(mode, "official", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mode, "no-candidate", StringComparison.OrdinalIgnoreCase)
                ? StarterDeckArbiterRuntime.StateOfficial
                : StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = SunExpIds.SolarMemoryStarterDeckAppliedKey,
            AppliedModeKey = SunExpIds.SolarMemoryStarterDeckModeKey,
            AppliedMode = mode,
            LegacyMode = "sunexp-solar-memory",
            DeckSize = StarterDeckSize,
            SourceName = "SunExp.SolarMemory.StarterDeck"
        };
    }

    private static void UpdateHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static CardRowView AcquireCardRow(Transform parent, string name)
    {
        return SunExpUiPool.AcquireComponent(
            "SolarMemoryStarterDeck.Row",
            parent,
            name,
            CreateCardRowTemplate);
    }

    private static CardRowView CreateCardRowTemplate(Transform parent, string name)
    {
        var row = CreateRow(parent, name);
        var iconCell = CreateLayoutObject("CardIcon", row.transform);
        var iconElement = iconCell.AddComponent<LayoutElement>();
        iconElement.minWidth = CardImageColumnWidth;
        iconElement.preferredWidth = CardImageColumnWidth;
        iconElement.minHeight = CardIconSize;
        iconElement.preferredHeight = CardIconSize;
        ApplyPanelImage(iconCell, DeepBlue);

        var icon = CreateRect("Image", iconCell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(CardIconSize, CardIconSize));
        var iconImage = icon.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.color = Color.white;

        var badgeText = AddTextFill(iconCell.transform, "", 18, TextAnchor.MiddleCenter, PaleGold);
        var nameText = AddTextBlock(row.transform, "", 15, TextAnchor.MiddleCenter, PaleGold, 36f, 1f);
        var rarityText = AddTextBlock(row.transform, "", 12, TextAnchor.MiddleCenter, Gold, 36f, 0f, 58f);
        var costText = AddTextBlock(row.transform, "", 12, TextAnchor.MiddleCenter, Gold, 36f, 0f, 48f);
        var button = CreateInlineButton(row.transform, "", () => { });
        var buttonText = button.GetComponentInChildren<Text>();

        var view = row.AddComponent<CardRowView>();
        view.Initialize(iconImage, badgeText, nameText, rarityText, costText, button, buttonText);
        return view;
    }

    private sealed class CardRowView : SunExpPooledUiBehaviour
    {
        private readonly SunExpUiLifetimeScope lifetime = new();
        private Image? iconImage;
        private Text? badgeText;
        private Text? nameText;
        private Text? rarityText;
        private Text? costText;
        private Button? button;
        private Text? buttonText;

        public void Initialize(
            Image iconImage,
            Text badgeText,
            Text nameText,
            Text rarityText,
            Text costText,
            Button button,
            Text? buttonText)
        {
            this.iconImage = iconImage;
            this.badgeText = badgeText;
            this.nameText = nameText;
            this.rarityText = rarityText;
            this.costText = costText;
            this.button = button;
            this.buttonText = buttonText;
        }

        public void Bind(
            string name,
            string rarity,
            string cost,
            Sprite? icon,
            string badge,
            string buttonLabel,
            Action action)
        {
            lifetime.Clear();
            SetText(nameText, name);
            SetText(rarityText, rarity);
            SetText(costText, cost);
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
            SetText(rarityText, "");
            SetText(costText, "");
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

    private static void ClosePanel()
    {
        SunExpUiPool.ReleaseOrDestroyChildren(candidateListContent, "SolarMemoryStarterDeck.ClosePanel.Candidates", "[SolarMemoryStarterDeck]");
        SunExpUiPool.ReleaseOrDestroyChildren(deckListContent, "SolarMemoryStarterDeck.ClosePanel.Deck", "[SolarMemoryStarterDeck]");
        SunExpModalHost.Close(ref activePanel, "SolarMemoryStarterDeck.ClosePanel", "[SolarMemoryStarterDeck]");

        candidateListContent = null;
        deckListContent = null;
        deckCounterText = null;
        hintText = null;
        editingDeck.Clear();
        deckListDirty.Reset();
    }
}
