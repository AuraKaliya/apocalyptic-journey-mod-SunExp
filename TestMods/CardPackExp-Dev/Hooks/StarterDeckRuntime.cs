using System;
using System.Collections.Generic;
using System.Linq;
using CardPackExp.Dll.Infrastructure;
using Data.Save;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Object = UnityEngine.Object;

namespace CardPackExp.Dll.Hooks;

public static class StarterDeckRuntime
{
    private const string AppliedKey = "CardPackExp.StarterDeckApplied";
    private const string Owner = "CardPackExp.StarterDeck";
    private const string SharedOwnerKey = "StarterDeck.Owner";
    private const string SharedScopeKey = "StarterDeck.Scope";
    private const string SharedStateKey = "StarterDeck.State";
    private const string SunExpSolarMemoryModeKey = "SunExp_SolarMemoryMode";
    private const int StarterDeckSize = 11;
    private const string ButtonSpritePath = "Mods/CardPackExp/ui-img/button-\u4e5d\u5bab\u683c.png";
    private const string PanelSpritePath = "Mods/CardPackExp/ui-img/background-\u4e5d\u5bab\u683c.png";
    private const float CardInfoHeaderHeight = 40f;
    private const float CardRowHeight = 40f;
    private const float CardImageColumnWidth = 38f;
    private const float CardIconSize = 32f;
    private const float InlineButtonWidth = 96f;
    private const float MainButtonWidth = 112f;
    private const float ButtonHeight = 34f;
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
    private static Transform? deckListContent;
    private static Text? deckCounterText;
    private static Text? hintText;
    private static Sprite? buttonSprite;
    private static Sprite? panelSprite;
    private static bool buttonSpriteLoadAttempted;
    private static bool panelSpriteLoadAttempted;
    private static bool promptShown;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "RoleTable.Init", MarkPendingFromRoleTableInit);
        RegisterAfter(modConfig, "MapManager.MapUIStart", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "NormalMapManager.MapUIStart", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "MapSelectUI.Start", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "MapSelectUI.DataUpdate", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "MapSelectUI.ReadyToSelect", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "MapSelectUI.MapAnimation", TryShowStarterDeckEditor);
    }

    public static void CaptureSelectedPacks(IEnumerable<string> packs)
    {
        selectedPacks.Clear();
        foreach (var pack in packs.Where(CardPackSelectionRuntime.IsValidPackForCurrentLobby))
        {
            selectedPacks.Add(pack);
        }

        pendingRoleTable = null;
        promptShown = false;
        ClosePanel();
        CardPackExpLog.Info("[StarterDeck] captured packs: " + string.Join("|", selectedPacks.OrderBy(id => id)));
    }

    public static void MarkPending(RoleTable roleTable, string source)
    {
        if (ShouldSkipForExternalOwner(roleTable) || IsApplied(roleTable))
        {
            return;
        }

        pendingRoleTable = roleTable;
        promptShown = false;
        CardPackExpLog.Info("[StarterDeck] pending after " + source + "; currentDeck=" + roleTable.cardList.Count);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            CardPackExpLog.Info("Hook registered: " + target);
        }
        catch (Exception ex)
        {
            CardPackExpLog.Warn("Hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void TryShowStarterDeckEditor(ModHookContext context)
    {
        TryShowStarterDeckEditor("hook");
    }

    private static void MarkPendingFromRoleTableInit(ModHookContext context)
    {
        try
        {
            var roleTable = context.Target as RoleTable ?? RoleTable.Instance;
            if (roleTable == null)
            {
                CardPackExpLog.Warn("[StarterDeck] RoleTable.Init hook ran but RoleTable is null.");
                return;
            }

            MarkPending(roleTable, "RoleTable.Init");
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to mark starter deck pending from RoleTable.Init", ex);
        }
    }

    private static bool TryShowStarterDeckEditor(string source)
    {
        try
        {
            var roleTable = pendingRoleTable ?? RoleTable.Instance;
            if (roleTable == null || promptShown || ShouldSkipForExternalOwner(roleTable) || IsApplied(roleTable))
            {
                return false;
            }

            var candidates = BuildCandidateCardIds();
            if (candidates.Count == 0)
            {
                MarkApplied(roleTable, "no-candidate");
                CardPackExpLog.Warn("[StarterDeck] no valid card candidates; keeping official starter deck.");
                return true;
            }

            promptShown = true;
            CardPackExpLog.Info("[StarterDeck] opening editor from "
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
            CardPackExpLog.Error("Failed to show starter deck editor", ex);
            return false;
        }
    }

    private static bool ShouldSkipForExternalOwner(RoleTable roleTable)
    {
        if (IsSunExpSolarMemoryRun())
        {
            CardPackExpLog.Info("[StarterDeck] skipped: SunExp Solar Memory owns this run.");
            return true;
        }

        if (roleTable.SpecialVarMap == null)
        {
            return false;
        }

        if (roleTable.SpecialVarMap.TryGetValue(SharedOwnerKey, out var owner)
            && !string.IsNullOrWhiteSpace(owner)
            && !string.Equals(owner, Owner, StringComparison.OrdinalIgnoreCase))
        {
            CardPackExpLog.Info("[StarterDeck] skipped: starter deck owner=" + owner + ".");
            return true;
        }

        if (roleTable.SpecialVarMap.TryGetValue(AppliedKey + ".Mode", out var legacyMode)
            && string.Equals(legacyMode, "sunexp-solar-memory", StringComparison.OrdinalIgnoreCase))
        {
            CardPackExpLog.Info("[StarterDeck] skipped: compatibility owner is SunExp Solar Memory.");
            return true;
        }

        return false;
    }

    private static bool IsSunExpSolarMemoryRun()
    {
        try
        {
            return GameSaveManager.GetValue<string>(SunExpSolarMemoryModeKey) == "1";
        }
        catch
        {
            return false;
        }
    }

    private static List<string> BuildCandidateCardIds()
    {
        var packs = selectedPacks.Count > 0
            ? selectedPacks
            : Singleton<GameRuntimeData>.Instance.UseCardPack;

        return CardPackSelectionRuntime.CardIdsFromPacks(packs)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    private static void ShowStarterDeckEditor(RoleTable roleTable, IReadOnlyList<string> candidates)
    {
        ClosePanel();
        editingDeck.Clear();
        editingDeck.AddRange(BuildAutoDeck(candidates));

        var parent = UIManager.Instance.upperCanvasTf != null
            ? UIManager.Instance.upperCanvasTf
            : UIManager.Instance.canvasTf;
        activePanel = CreateRect("CardPackExpStarterDeck", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        activePanel.transform.SetAsLastSibling();
        var blocker = activePanel.AddComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.74f);

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
        AddTextBlock(header.transform, "\u521d\u59cb\u5957\u5361", 28, TextAnchor.MiddleCenter, PaleGold, 34f);
        AddTextBlock(header.transform, "\u4ece\u5f53\u524d\u542f\u7528\u5361\u5305\u4e2d\u9009\u62e9 11 \u5f20\u521d\u59cb\u724c\u3002\u53ea\u66ff\u6362\u672c\u5c40\u724c\u7ec4\uff0c\u4e0d\u5199\u5165\u5956\u52b1\u5019\u9009\u6c60\u3002", 15, TextAnchor.MiddleCenter, Gold, 22f);

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
        listElement.minHeight = 420f;
        var listLayout = listRow.AddComponent<HorizontalLayoutGroup>();
        listLayout.spacing = 34f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = true;

        var candidateContent = CreateScroll(listRow.transform, "CandidateCards");
        foreach (var cardId in candidates)
        {
            CreateCandidateRow(candidateContent, cardId);
        }

        deckListContent = CreateScroll(listRow.transform, "SelectedDeck");

        var footer = CreateLayoutObject("Footer", window.transform);
        footer.AddComponent<LayoutElement>().preferredHeight = 44f;
        ApplyPanelImage(footer, FooterTint);
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.padding = new RectOffset(14, 14, 5, 5);
        footerLayout.spacing = 9f;
        footerLayout.childControlHeight = true;
        footerLayout.childControlWidth = true;
        footerLayout.childForceExpandHeight = true;
        footerLayout.childForceExpandWidth = false;
        hintText = AddTextBlock(footer.transform, "", 14, TextAnchor.MiddleCenter, PaleGold, 34f, 1f);

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
        footerButtonsLayout.childForceExpandHeight = true;

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
        var row = CreateRow(parent, "Candidate-" + cardId);
        CreateCardIconCell(row.transform, cardId, CardCost(cardId));
        AddTextBlock(row.transform, CardDisplayName(cardId), 15, TextAnchor.MiddleCenter, PaleGold, 36f, 1f);
        AddTextBlock(row.transform, CardRarity(cardId), 12, TextAnchor.MiddleCenter, Gold, 36f, 0f, 58f);
        AddTextBlock(row.transform, CardCost(cardId), 12, TextAnchor.MiddleCenter, Gold, 36f, 0f, 48f);
        CreateInlineButton(row.transform, "\u6dfb\u52a0", () =>
        {
            if (editingDeck.Count >= StarterDeckSize)
            {
                UpdateHint("\u5957\u5361\u5df2\u6ee1\uff0c\u9700\u8981\u5148\u79fb\u9664\u4e00\u5f20\u724c\u3002");
                return;
            }

            editingDeck.Add(cardId);
            RefreshDeckList(pendingRoleTable ?? RoleTable.Instance);
        });
    }

    private static void RefreshDeckList(RoleTable? roleTable)
    {
        if (deckListContent == null)
        {
            return;
        }

        foreach (Transform child in deckListContent)
        {
            Object.Destroy(child.gameObject);
        }

        for (var i = 0; i < editingDeck.Count; i++)
        {
            var index = i;
            var cardId = editingDeck[i];
            var row = CreateRow(deckListContent, "Deck-" + i);
            CreateCardIconCell(row.transform, cardId, (i + 1).ToString());
            AddTextBlock(row.transform, CardDisplayName(cardId), 15, TextAnchor.MiddleCenter, PaleGold, 36f, 1f);
            AddTextBlock(row.transform, CardRarity(cardId), 12, TextAnchor.MiddleCenter, Gold, 36f, 0f, 58f);
            AddTextBlock(row.transform, CardCost(cardId), 12, TextAnchor.MiddleCenter, Gold, 36f, 0f, 48f);
            CreateInlineButton(row.transform, "\u79fb\u9664", () =>
            {
                if (index >= 0 && index < editingDeck.Count)
                {
                    editingDeck.RemoveAt(index);
                    RefreshDeckList(roleTable);
                }
            });
        }

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
        button.targetGraphic = image;
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
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, 14, TextAnchor.MiddleCenter, PaleGold);
        return button;
    }

    private static Image ApplyButtonImage(GameObject go)
    {
        var image = go.AddComponent<Image>();
        image.color = Color.white;
        image.sprite = GetButtonSprite();
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
        image.sprite = GetPanelSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, 0.96f) : new Color(0.04f, 0.04f, 0.18f, 0.96f);
        if (image.sprite != null)
        {
            AddPanelTint(go, new Color(0.035f, 0.035f, 0.15f, 0.96f));
        }

        return image;
    }

    private static void ApplyPanelImage(GameObject go, Color fallbackOrTint)
    {
        var image = go.AddComponent<Image>();
        image.sprite = GetPanelSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, fallbackOrTint.a) : fallbackOrTint;
        if (image.sprite != null)
        {
            AddPanelTint(go, fallbackOrTint);
        }
    }

    private static void AddPanelTint(GameObject target, Color color)
    {
        var tint = new GameObject("PanelTint", typeof(RectTransform));
        tint.transform.SetParent(target.transform, false);
        tint.transform.SetAsFirstSibling();
        var rect = tint.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);
        var layout = tint.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        var image = tint.AddComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, Mathf.Min(0.62f, color.a));
        image.raycastTarget = false;
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
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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

    private static Sprite? GetButtonSprite()
    {
        if (buttonSprite != null)
        {
            return buttonSprite;
        }

        if (buttonSpriteLoadAttempted)
        {
            return null;
        }

        buttonSpriteLoadAttempted = true;
        buttonSprite = CreateNineSliceSprite(ButtonSpritePath, new Vector4(24f, 12f, 24f, 12f));
        return buttonSprite;
    }

    private static Sprite? GetPanelSprite()
    {
        if (panelSprite != null)
        {
            return panelSprite;
        }

        if (panelSpriteLoadAttempted)
        {
            return null;
        }

        panelSpriteLoadAttempted = true;
        panelSprite = CreateNineSliceSprite(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f));
        return panelSprite;
    }

    private static Sprite? CreateNineSliceSprite(string path, Vector4 border)
    {
        try
        {
            var source = ResourceLoader.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                CardPackExpLog.Warn("[StarterDeck] UI sprite missing: " + path);
                return null;
            }

            var texture = source.texture;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(
                texture,
                source.rect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
        }
        catch (Exception ex)
        {
            CardPackExpLog.Warn("[StarterDeck] failed to load UI sprite " + path + ": " + ex.Message);
            return null;
        }
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
                sprite = ResourceLoader.Load<Sprite>(iconPath, true);
            }
        }
        catch (Exception ex)
        {
            CardPackExpLog.Warn("[StarterDeck] failed to load card icon for " + cardId + ": " + ex.Message);
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

            var originalDeckCount = roleTable.cardList.Count;
            roleTable.cardList.Clear();
            foreach (var cardId in deck)
            {
                roleTable.cardList.Add(new DataConfig(cardId, DataType.Card));
            }

            roleTable.CardTopCount = Math.Max(roleTable.CardTopCount, roleTable.cardList.Count);
            roleTable.CardBottomCount = Math.Min(roleTable.CardBottomCount, roleTable.cardList.Count);
            MarkApplied(roleTable, "custom");
            ClosePanel();

            CardPackExpLog.Info("[StarterDeck] applied custom starter deck; originalDeck="
                + originalDeckCount
                + "; deck=" + roleTable.cardList.Count
                + "; cards=" + string.Join("|", deck));
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to apply starter deck", ex);
        }
    }

    private static void KeepOfficialDeck(RoleTable roleTable)
    {
        MarkApplied(roleTable, "official");
        ClosePanel();
        CardPackExpLog.Info("[StarterDeck] kept official starter deck; deck=" + roleTable.cardList.Count);
    }

    private static bool IsApplied(RoleTable roleTable)
    {
        return roleTable.SpecialVarMap != null
            && roleTable.SpecialVarMap.TryGetValue(AppliedKey, out var value)
            && value == "1";
    }

    private static void MarkApplied(RoleTable roleTable, string mode)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[AppliedKey] = "1";
        roleTable.SpecialVarMap[AppliedKey + ".Mode"] = mode;
        roleTable.SpecialVarMap[SharedOwnerKey] = Owner;
        roleTable.SpecialVarMap[SharedScopeKey] = "CardPackExp";
        roleTable.SpecialVarMap[SharedStateKey] = string.Equals(mode, "official", StringComparison.OrdinalIgnoreCase)
            ? "official"
            : "applied";
        pendingRoleTable = null;
    }

    private static void UpdateHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static void ClosePanel()
    {
        if (activePanel != null)
        {
            Object.Destroy(activePanel);
            activePanel = null;
        }

        deckListContent = null;
        deckCounterText = null;
        hintText = null;
        editingDeck.Clear();
    }
}
