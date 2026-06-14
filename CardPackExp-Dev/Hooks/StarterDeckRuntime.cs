using System;
using System.Collections.Generic;
using System.Linq;
using CardPackExp.Dll.Infrastructure;
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
    private const int StarterDeckSize = 11;
    private static readonly HashSet<string> selectedPacks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> editingDeck = new();
    private static RoleTable? pendingRoleTable;
    private static GameObject? activePanel;
    private static Transform? deckListContent;
    private static Text? deckCounterText;
    private static Text? hintText;
    private static bool promptShown;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "MapManager.MapUIStart", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "NormalMapManager.MapUIStart", TryShowStarterDeckEditor);
        RegisterAfter(modConfig, "MapSelectUI.Start", TryShowStarterDeckEditor);
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
        if (IsApplied(roleTable))
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
        try
        {
            var roleTable = pendingRoleTable ?? RoleTable.Instance;
            if (roleTable == null || promptShown || IsApplied(roleTable))
            {
                return;
            }

            var candidates = BuildCandidateCardIds();
            if (candidates.Count == 0)
            {
                MarkApplied(roleTable, "no-candidate");
                CardPackExpLog.Warn("[StarterDeck] no valid card candidates; keeping official starter deck.");
                return;
            }

            promptShown = true;
            ShowStarterDeckEditor(roleTable, candidates);
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to show starter deck editor", ex);
        }
    }

    private static List<string> BuildCandidateCardIds()
    {
        return CardPackSelectionRuntime.CardIdsFromPacks(selectedPacks)
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
        var windowImage = window.AddComponent<Image>();
        windowImage.color = new Color(0.075f, 0.085f, 0.1f, 0.98f);
        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(24, 24, 20, 20);
        windowLayout.spacing = 12f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        var header = CreateLayoutObject("Header", window.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 70f;
        var headerLayout = header.AddComponent<VerticalLayoutGroup>();
        headerLayout.spacing = 4f;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandHeight = false;
        AddTextBlock(header.transform, "\u521d\u59cb\u5957\u5361", 27, TextAnchor.MiddleLeft, Color.white, 36f);
        AddTextBlock(header.transform, "\u4ece\u5f53\u524d\u542f\u7528\u5361\u5305\u4e2d\u9009\u62e9 11 \u5f20\u521d\u59cb\u724c\u3002\u53ea\u66ff\u6362\u672c\u5c40\u724c\u7ec4\uff0c\u4e0d\u5199\u5165\u5956\u52b1\u5019\u9009\u6c60\u3002", 15, TextAnchor.MiddleLeft, new Color(0.82f, 0.86f, 0.9f), 28f);

        var content = CreateLayoutObject("Content", window.transform);
        var contentElement = content.AddComponent<LayoutElement>();
        contentElement.flexibleHeight = 1f;
        contentElement.minHeight = 230f;
        var contentLayout = content.AddComponent<HorizontalLayoutGroup>();
        contentLayout.spacing = 18f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = true;

        var candidateColumn = CreateColumn(content.transform, "\u53ef\u9009\u5361\u724c", out _);
        var candidateContent = CreateScroll(candidateColumn.transform);
        foreach (var cardId in candidates)
        {
            CreateCandidateRow(candidateContent, cardId);
        }

        var deckColumn = CreateColumn(content.transform, "\u5df2\u9009\u5957\u5361", out deckCounterText);
        deckListContent = CreateScroll(deckColumn.transform);

        var footer = CreateLayoutObject("Footer", window.transform);
        footer.AddComponent<LayoutElement>().preferredHeight = 46f;
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 10f;
        footerLayout.childControlHeight = true;
        footerLayout.childControlWidth = true;
        footerLayout.childForceExpandHeight = true;
        footerLayout.childForceExpandWidth = false;
        hintText = AddTextBlock(footer.transform, "", 15, TextAnchor.MiddleLeft, new Color(1f, 0.86f, 0.44f), 42f, 1f);
        CreateButton(footer.transform, "\u81ea\u52a8\u586b\u5145", new Vector2(106f, 38f), () =>
        {
            editingDeck.Clear();
            editingDeck.AddRange(BuildAutoDeck(candidates));
            RefreshDeckList(roleTable);
        });
        CreateButton(footer.transform, "\u6e05\u7a7a", new Vector2(76f, 38f), () =>
        {
            editingDeck.Clear();
            RefreshDeckList(roleTable);
        });
        CreateButton(footer.transform, "\u4fdd\u7559\u9ed8\u8ba4", new Vector2(106f, 38f), () => KeepOfficialDeck(roleTable));
        CreateButton(footer.transform, "\u4f7f\u7528\u5957\u5361", new Vector2(106f, 38f), () =>
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
        AddTextBlock(row.transform, CardDisplayName(cardId), 14, TextAnchor.MiddleLeft, Color.white, 32f, 1f);
        AddTextBlock(row.transform, CardMeta(cardId), 12, TextAnchor.MiddleRight, new Color(0.78f, 0.82f, 0.86f), 32f, 0f, 78f);
        CreateButton(row.transform, "\u6dfb\u52a0", new Vector2(68f, 30f), () =>
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
            AddTextBlock(row.transform, (i + 1).ToString("00") + ". " + CardDisplayName(cardId), 14, TextAnchor.MiddleLeft, Color.white, 32f, 1f);
            AddTextBlock(row.transform, CardMeta(cardId), 12, TextAnchor.MiddleRight, new Color(0.78f, 0.82f, 0.86f), 32f, 0f, 72f);
            CreateButton(row.transform, "\u79fb\u9664", new Vector2(68f, 30f), () =>
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
            deckCounterText.color = editingDeck.Count == StarterDeckSize ? new Color(0.56f, 0.92f, 0.66f) : new Color(1f, 0.72f, 0.4f);
        }

        UpdateHint(editingDeck.Count == StarterDeckSize
            ? "\u53ef\u4ee5\u786e\u8ba4\u3002"
            : "\u9700\u8981\u9009\u62e9\u6ee1 11 \u5f20\u724c\u624d\u80fd\u4f7f\u7528\u5957\u5361\u3002");
    }

    private static Transform CreateScroll(Transform parent)
    {
        var root = CreateLayoutObject("Scroll", parent);
        var rootElement = root.AddComponent<LayoutElement>();
        rootElement.flexibleHeight = 1f;
        rootElement.minHeight = 180f;
        var rootImage = root.AddComponent<Image>();
        rootImage.color = new Color(0.02f, 0.025f, 0.03f, 0.76f);

        var viewport = CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateRect("Content", viewport.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f));
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 6f;
        layout.padding = new RectOffset(8, 8, 8, 8);
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

    private static GameObject CreateRow(Transform parent, string name)
    {
        var row = CreateLayoutObject(name, parent);
        var layoutElement = row.AddComponent<LayoutElement>();
        layoutElement.minHeight = 36f;
        layoutElement.preferredHeight = 36f;
        var image = row.AddComponent<Image>();
        image.color = new Color(0.16f, 0.18f, 0.21f, 0.92f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 8, 3, 3);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        return row;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Action action)
    {
        var go = CreateLayoutObject("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = size.x;
        element.preferredWidth = size.x;
        element.minHeight = size.y;
        element.preferredHeight = size.y;
        var image = go.AddComponent<Image>();
        image.color = new Color(0.24f, 0.32f, 0.42f, 0.96f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, 14, TextAnchor.MiddleCenter, Color.white);
        return button;
    }

    private static GameObject CreateColumn(Transform parent, string title, out Text? counter)
    {
        var column = CreateLayoutObject("Column-" + title, parent);
        var element = column.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minWidth = 300f;
        var layout = column.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var header = CreateLayoutObject("ColumnHeader", column.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 28f;
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        AddTextBlock(header.transform, title, 17, TextAnchor.MiddleLeft, Color.white, 26f, 1f);
        counter = AddTextBlock(header.transform, "", 16, TextAnchor.MiddleRight, new Color(0.56f, 0.92f, 0.66f), 26f, 0f, 80f);
        return column;
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
        text.alignment = anchor;
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

        var width = Mathf.Min(1040f, Mathf.Max(720f, available.x - 96f));
        var height = Mathf.Min(560f, Mathf.Max(420f, available.y - 110f));
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

    private static string CardMeta(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var rarity = data.TryGetValue("Rarity", out var r) ? r : "?";
            var cost = data.TryGetValue("Expend", out var c) ? c : "?";
            return "R" + rarity + " / C" + cost;
        }
        catch
        {
            return "";
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
