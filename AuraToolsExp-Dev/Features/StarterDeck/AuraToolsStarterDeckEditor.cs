using System;
using System.Collections.Generic;
using System.Linq;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckEditor
{
    public static void Show(Transform parent)
    {
        ShowGlobal(parent);
    }

    public static void ShowGlobal(Transform parent)
    {
        var profile = AuraToolsConfigService.MatchExperience.StarterDeck.GlobalProfile;
        profile.Normalize("", "全局自定义卡组");
        ShowLocalProfile(parent, profile, "", "【世界推演】全局开局卡组配置");
    }

    public static void ShowRole(Transform parent, string roleId, string displayName = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var profile = AuraToolsStarterDeckRuntime.EnsureRoleProfileSettings(normalizedRole, displayName);
        ShowLocalProfile(parent, profile, normalizedRole, "【世界推演】角色开局卡组配置 - " + (string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName));
    }

    public static void CopyRegisteredToRole(Transform parent, string roleId, string displayName, StarterDeckProfile source)
    {
        var profile = AuraToolsStarterDeckRuntime.EnsureRoleProfileSettings(roleId, displayName);
        profile.DeckSize = source.DeckSize;
        profile.CardIds = AuraToolsStarterDeckRuntime.BuildDeckFromProfile(source);
        profile.DerivedFromProfileId = source.QualifiedProfileId;
        profile.DisplayName = (string.IsNullOrWhiteSpace(displayName) ? RoleCatalog.NormalizeRoleId(roleId) : displayName) + " 自定义卡组";
        AuraToolsStarterDeckRuntime.SelectProfileForRole(roleId, AuraToolsStarterDeckRuntime.LocalRoleProfileId(roleId));
        AuraToolsConfigService.SaveMatchExperience();
        ShowRole(parent, roleId, displayName);
    }

    private static void ShowLocalProfile(Transform parent, StarterDeckLocalProfileSettings profile, string roleId, string title)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.StarterDeckEditor", parent, title);
        var session = new StarterDeckEditorSession(profile, roleId);
        session.Build(window.transform);
    }

    private sealed class StarterDeckEditorSession
    {
        private readonly List<string> editingDeck = new();
        private readonly List<string> autoFillCandidates = new();
        private readonly List<StarterDeckCardPackGroup> candidateGroups = new();
        private readonly Dictionary<string, CandidateGroupView> candidateGroupViews = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> expandedCandidateGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SelectedCardRowView> selectedRowViews = new();
        private readonly StarterDeckLocalProfileSettings profile;
        private readonly string editingRoleId;
        private Transform? candidateContent;
        private Transform? selectedContent;
        private Text? counterText;
        private Text? hintText;

        public StarterDeckEditorSession(StarterDeckLocalProfileSettings profile, string roleId)
        {
            this.profile = profile;
            editingRoleId = RoleCatalog.NormalizeRoleId(roleId);
            editingDeck.AddRange(profile.CardIds);
        }

        public void Build(Transform window)
        {
            candidateGroups.Clear();
            candidateGroups.AddRange(AuraToolsStarterDeckRuntime.BuildCandidateCardPackGroups());
            autoFillCandidates.Clear();
            autoFillCandidates.AddRange(candidateGroups
                .SelectMany(group => group.CardIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(AuraToolsStarterDeckRuntime.CardSortKey)
                .ToList());

            var body = Settings.AuraToolsUi.CreateLayout("Body", window);
            var bodyElement = body.AddComponent<LayoutElement>();
            bodyElement.flexibleHeight = 1f;
            bodyElement.minHeight = 420f;
            var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
            bodyLayout.spacing = 12f;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = true;

            var candidatePanel = CreateColumn(body.transform, "按卡包选择", out _);
            candidateContent = candidatePanel;
            BuildCandidateGroups();

            var selectedPanel = CreateColumn(body.transform, "当前预设", out counterText);
            selectedContent = selectedPanel;

            var footer = Settings.AuraToolsUi.CreateLayout("Footer", window);
            Settings.AuraToolsUi.SetFixedHeight(footer, Settings.AuraToolsUi.FooterHeight);
            var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f;
            footerLayout.childControlHeight = true;
            footerLayout.childControlWidth = true;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = false;
            hintText = Settings.AuraToolsUi.AddText(footer.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddButton(footer.transform, "自动填充", () =>
            {
                editingDeck.Clear();
                editingDeck.AddRange(autoFillCandidates.Take(CurrentDeckSize()));
                RefreshSelected();
            });
            Settings.AuraToolsUi.AddButton(footer.transform, "清空", () =>
            {
                editingDeck.Clear();
                RefreshSelected();
            });
            Settings.AuraToolsUi.AddButton(footer.transform, "保存", Save);

            RefreshSelected();
        }

        private Transform CreateColumn(Transform parent, string title, out Text? counter)
        {
            var column = Settings.AuraToolsUi.CreateLayout("Column-" + title, parent);
            column.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var header = Settings.AuraToolsUi.CreateLayout("Header", column.transform);
            Settings.AuraToolsUi.SetFixedHeight(header, Settings.AuraToolsUi.ColumnHeaderHeight);
            Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
            var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(10, 10, 2, 2);
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandHeight = false;
            Settings.AuraToolsUi.AddText(header.transform, title, Settings.AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
            counter = Settings.AuraToolsUi.AddText(header.transform, "", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleRight, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 0f, 110f);

            CreateCardInfoHeader(column.transform);
            return Settings.AuraToolsUi.CreateScroll(column.transform, title);
        }

        private void BuildCandidateGroups()
        {
            if (candidateContent == null || candidateGroupViews.Count > 0)
            {
                return;
            }

            foreach (var group in candidateGroups)
            {
                var view = CreateCandidateGroup(candidateContent, group);
                candidateGroupViews[group.PackId] = view;
            }
        }

        private CandidateGroupView CreateCandidateGroup(Transform parent, StarterDeckCardPackGroup group)
        {
            var expanded = expandedCandidateGroups.Contains(group.PackId);
            var root = Settings.AuraToolsUi.CreateLayout("PackGroup-" + group.PackId, parent);
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.spacing = 8f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var header = Settings.AuraToolsUi.CreateLayout("Pack-" + group.PackId, root.transform);
            Settings.AuraToolsUi.SetFixedHeight(header, 34f);
            var image = Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
            var button = header.AddComponent<Button>();
            AuraUiButtonFeedback.Apply(button, image, Settings.AuraToolsUi.Accent);
            button.onClick.AddListener(() => ToggleCandidateGroup(group.PackId));
            var layout = header.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 2, 2);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var titleText = Settings.AuraToolsUi.AddText(header.transform, CandidateGroupTitle(group, expanded), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddText(header.transform, group.CardIds.Count.ToString(), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 52f);

            var cardContent = Settings.AuraToolsUi.CreateLayout("PackCards-" + group.PackId, root.transform);
            var cardLayout = cardContent.AddComponent<VerticalLayoutGroup>();
            cardLayout.spacing = 8f;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;

            var view = new CandidateGroupView(root, cardContent, titleText, group);
            if (expanded)
            {
                EnsureCandidateRows(view);
            }

            Settings.AuraToolsUi.SetFoldoutExpanded(cardContent, expanded, root.transform);
            return view;
        }

        private void ToggleCandidateGroup(string packId)
        {
            if (!candidateGroupViews.TryGetValue(packId, out var view))
            {
                return;
            }

            var expanded = !expandedCandidateGroups.Contains(packId);
            if (expanded)
            {
                expandedCandidateGroups.Add(packId);
                EnsureCandidateRows(view);
            }
            else
            {
                expandedCandidateGroups.Remove(packId);
            }

            view.TitleText.text = CandidateGroupTitle(view.Group, expanded);
            Settings.AuraToolsUi.SetFoldoutExpanded(view.CardContent, expanded, view.Root.transform);
        }

        private static string CandidateGroupTitle(StarterDeckCardPackGroup group, bool expanded)
        {
            return (expanded ? "\u25be " : "\u25b8 ") + group.DisplayName;
        }

        private void EnsureCandidateRows(CandidateGroupView view)
        {
            if (view.RowsBuilt)
            {
                return;
            }

            foreach (var cardId in view.Group.CardIds)
            {
                CreateCandidateRow(view.CardContent.transform, cardId);
            }

            view.RowsBuilt = true;
        }

        private void CreateCandidateRow(Transform parent, string cardId)
        {
            var row = CreateRow(parent, "Candidate-" + cardId);
            CreateCardIconCell(row.transform, cardId, AuraToolsStarterDeckRuntime.CardCost(cardId));
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardDisplayNameWithSpecialMarker(cardId), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardRarity(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardCost(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
            Settings.AuraToolsUi.AddButton(row.transform, "添加", () =>
            {
                if (editingDeck.Count >= CurrentDeckSize())
                {
                    SetHint("预设已满，请先移除一张。");
                    return;
                }

                editingDeck.Add(cardId);
                RefreshSelected();
            }, 70f, 30f);
        }

        private void RefreshSelected()
        {
            if (selectedContent == null)
            {
                return;
            }

            while (selectedRowViews.Count < editingDeck.Count)
            {
                selectedRowViews.Add(CreateSelectedRow(selectedContent, selectedRowViews.Count));
            }

            for (var i = 0; i < selectedRowViews.Count; i++)
            {
                var view = selectedRowViews[i];
                var visible = i < editingDeck.Count;
                if (visible)
                {
                    BindSelectedRow(view, i, editingDeck[i]);
                }

                Settings.AuraToolsUi.SetActiveIfChanged(view.Root, visible);
            }

            var size = CurrentDeckSize();
            if (counterText != null)
            {
                counterText.text = editingDeck.Count + "/" + size;
                counterText.color = editingDeck.Count == size ? new Color(0.58f, 0.94f, 0.62f) : Settings.AuraToolsUi.Text;
            }

            SetHint(editingDeck.Count == size ? "预设完整，可以保存。" : "需要配置满 " + size + " 张牌。");
        }

        private SelectedCardRowView CreateSelectedRow(Transform parent, int slot)
        {
            var row = CreateRow(parent, "SelectedSlot-" + slot);
            var icon = CreateCardIconCellView(row.transform);
            var nameText = Settings.AuraToolsUi.AddText(row.transform, "", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
            var rarityText = Settings.AuraToolsUi.AddText(row.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
            var costText = Settings.AuraToolsUi.AddText(row.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
            var view = new SelectedCardRowView(row, icon, nameText, rarityText, costText);
            Settings.AuraToolsUi.AddButton(row.transform, "移除", () => RemoveSelectedRow(view), 70f, 30f);
            return view;
        }

        private static void BindSelectedRow(SelectedCardRowView view, int index, string cardId)
        {
            view.Index = index;
            view.Root.name = "Selected-" + index;
            BindCardIconCell(view.Icon, cardId, (index + 1).ToString());
            view.NameText.text = AuraToolsStarterDeckRuntime.CardDisplayNameWithSpecialMarker(cardId);
            view.RarityText.text = AuraToolsStarterDeckRuntime.CardRarity(cardId);
            view.CostText.text = AuraToolsStarterDeckRuntime.CardCost(cardId);
        }

        private void RemoveSelectedRow(SelectedCardRowView view)
        {
            var index = view.Index;
            if (index < 0 || index >= editingDeck.Count)
            {
                return;
            }

            editingDeck.RemoveAt(index);
            RefreshSelected();
        }

        private sealed class CandidateGroupView
        {
            public CandidateGroupView(GameObject root, GameObject cardContent, Text titleText, StarterDeckCardPackGroup group)
            {
                Root = root;
                CardContent = cardContent;
                TitleText = titleText;
                Group = group;
            }

            public GameObject Root { get; }
            public GameObject CardContent { get; }
            public Text TitleText { get; }
            public StarterDeckCardPackGroup Group { get; }
            public bool RowsBuilt { get; set; }
        }

        private sealed class SelectedCardRowView
        {
            public SelectedCardRowView(GameObject root, CardIconCellView icon, Text nameText, Text rarityText, Text costText)
            {
                Root = root;
                Icon = icon;
                NameText = nameText;
                RarityText = rarityText;
                CostText = costText;
            }

            public GameObject Root { get; }
            public CardIconCellView Icon { get; }
            public Text NameText { get; }
            public Text RarityText { get; }
            public Text CostText { get; }
            public int Index { get; set; } = -1;
        }

        private void Save()
        {
            if (editingDeck.Count != profile.DeckSize)
            {
                SetHint("保存失败：需要正好 " + profile.DeckSize + " 张牌。");
                return;
            }

            profile.CardIds = editingDeck.ToList();
            profile.Enabled = true;
            var fallbackDisplayName = string.IsNullOrWhiteSpace(editingRoleId)
                ? "全局自定义卡组"
                : RoleCatalog.GetDisplayName(editingRoleId) + " 自定义卡组";
            profile.Normalize(editingRoleId, fallbackDisplayName);
            if (!string.IsNullOrWhiteSpace(editingRoleId))
            {
                AuraToolsStarterDeckRuntime.SelectProfileForRole(editingRoleId, AuraToolsStarterDeckRuntime.LocalRoleProfileId(editingRoleId));
            }

            AuraToolsConfigService.SaveMatchExperience();
            SetHint(string.IsNullOrWhiteSpace(editingRoleId) ? "已保存全局开局卡组预设。" : "已保存本角色开局卡组预设。");
        }

        private int CurrentDeckSize()
        {
            return Math.Max(1, profile.DeckSize);
        }

        private void SetHint(string message)
        {
            if (hintText != null)
            {
                hintText.text = message;
            }
        }
    }

    private static void CreateCardInfoHeader(Transform parent)
    {
        var header = Settings.AuraToolsUi.CreateLayout("CardInfoHeader", parent);
        Settings.AuraToolsUi.SetFixedHeight(header, AuraToolsStarterDeckRuntime.CardInfoHeaderHeight);
        Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 0, 0);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        Settings.AuraToolsUi.AddText(header.transform, "卡图", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardImageColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "卡牌名称", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddText(header.transform, "稀有度", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "费用", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardActionColumnWidth);
    }

    private static void CreateCardIconCell(Transform parent, string cardId, string fallbackText)
    {
        var view = CreateCardIconCellView(parent);
        BindCardIconCell(view, cardId, fallbackText);
    }

    private static CardIconCellView CreateCardIconCellView(Transform parent)
    {
        var cell = Settings.AuraToolsUi.CreateLayout("CardIcon", parent);
        var element = Settings.AuraToolsUi.EnsureLayoutElement(cell);
        element.minWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.preferredWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.minHeight = Settings.AuraToolsUi.TextMinHeight;
        element.preferredHeight = Settings.AuraToolsUi.TextMinHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        var background = Settings.AuraToolsUi.AddImage(cell, new Color(0.025f, 0.022f, 0.045f, 0.98f));
        var icon = Settings.AuraToolsUi.CreateRect("Image", cell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(AuraToolsStarterDeckRuntime.CardIconSize, AuraToolsStarterDeckRuntime.CardIconSize));
        var image = icon.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
        var fallback = Settings.AuraToolsUi.AddFillText(cell.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent);
        return new CardIconCellView(background, image, fallback);
    }

    private static void BindCardIconCell(CardIconCellView view, string cardId, string fallbackText)
    {
        var sprite = AuraToolsStarterDeckRuntime.TryLoadCardIcon(cardId);
        var hasIcon = sprite != null;
        view.Background.enabled = !hasIcon;
        view.Image.sprite = sprite;
        view.Fallback.text = fallbackText;
        Settings.AuraToolsUi.SetActiveIfChanged(view.Image.gameObject, hasIcon);
        Settings.AuraToolsUi.SetActiveIfChanged(view.Fallback.gameObject, !hasIcon);
    }

    private sealed class CardIconCellView
    {
        public CardIconCellView(Image background, Image image, Text fallback)
        {
            Background = background;
            Image = image;
            Fallback = fallback;
        }

        public Image Background { get; }
        public Image Image { get; }
        public Text Fallback { get; }
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        var row = Settings.AuraToolsUi.CreateLayout(name, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, Settings.AuraToolsUi.DataRowHeight);
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }
}
