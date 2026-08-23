using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using AuraUi.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckEditor
{
    public static void ShowGlobal(Transform parent)
    {
        var profile = AuraToolsConfigService.MatchExperience.StarterDeck.GlobalProfile;
        profile.Normalize("", "全局自定义开局");
        Show(parent, profile, "", "【世界推演】全局自定义开局", global: true);
    }

    public static void ShowRole(Transform parent, string roleId, string displayName = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var profile = AuraToolsStarterDeckRuntime.EnsureRoleSettings(normalizedRole, displayName);
        Show(
            parent,
            profile,
            normalizedRole,
            "【世界推演】角色自定义开局 - " + (string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName),
            global: false);
    }

    private static void Show(
        Transform parent,
        StarterDeckLocalProfileSettings profile,
        string roleId,
        string title,
        bool global)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.CustomStartEditor", parent, title);
        new CustomStartEditorSession(profile, roleId, global).Build(window.transform);
    }

    private sealed class CustomStartEditorSession
    {
        private readonly StarterDeckLocalProfileSettings profile;
        private readonly string roleId;
        private readonly bool global;
        private readonly List<string> cards = new();
        private readonly List<string> relics = new();
        private readonly HashSet<string> expandedGroups = new(StringComparer.OrdinalIgnoreCase);
        private Transform? body;
        private Transform? candidateContent;
        private Transform? selectedContent;
        private Text? hintText;
        private Text? countText;
        private TMP_InputField? searchInput;
        private Button? cardTabButton;
        private Button? relicTabButton;
        private bool cardTab = true;
        private bool inheritCards;
        private bool inheritRelics;
        private string search = "";

        internal CustomStartEditorSession(StarterDeckLocalProfileSettings profile, string roleId, bool global)
        {
            this.profile = profile;
            this.roleId = RoleCatalog.NormalizeRoleId(roleId);
            this.global = global;
            inheritCards = !global && profile.InheritCards;
            inheritRelics = !global && profile.InheritRelics;
            var globalProfile = AuraToolsConfigService.MatchExperience.StarterDeck.GlobalProfile;
            cards.AddRange(inheritCards ? globalProfile.CardIds : profile.CardIds);
            relics.AddRange(inheritRelics ? globalProfile.RelicIds : profile.RelicIds);
        }

        internal void Build(Transform window)
        {
            var toolbar = Settings.AuraToolsUi.CreateLayout("CustomStartToolbar", window);
            Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
            var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
            toolbarLayout.spacing = 8f;
            toolbarLayout.childControlWidth = true;
            toolbarLayout.childControlHeight = true;
            toolbarLayout.childForceExpandWidth = false;
            toolbarLayout.childForceExpandHeight = false;

            cardTabButton = Settings.AuraToolsUi.AddButton(toolbar.transform, "卡牌 " + cards.Count + "/15", () => SwitchTab(true), 128f);
            relicTabButton = Settings.AuraToolsUi.AddButton(toolbar.transform, "遗物 " + relics.Count + "/6", () => SwitchTab(false), 128f);
            searchInput = Settings.AuraToolsUi.AddTmpInput(
                toolbar.transform,
                "",
                "搜索名称",
                value =>
                {
                    search = (value ?? "").Trim();
                    RebuildBody();
                },
                260f,
                40f);
            Settings.AuraToolsUi.AddText(
                toolbar.transform,
                "",
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            Settings.AuraToolsUi.AddButton(toolbar.transform, "导入", PickImport, 78f);
            Settings.AuraToolsUi.AddButton(toolbar.transform, "导出", Export, 78f);

            body = Settings.AuraToolsUi.CreateLayout("CustomStartBody", window).transform;
            var bodyElement = body.gameObject.AddComponent<LayoutElement>();
            bodyElement.flexibleHeight = 1f;
            bodyElement.minHeight = 420f;

            var footer = Settings.AuraToolsUi.CreateLayout("CustomStartFooter", window);
            Settings.AuraToolsUi.SetFixedHeight(footer, Settings.AuraToolsUi.FooterHeight);
            var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 8f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = false;
            hintText = Settings.AuraToolsUi.AddText(
                footer.transform,
                "",
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            if (!global)
            {
                Settings.AuraToolsUi.AddButton(footer.transform, "恢复全局", RestoreCurrentTab, 96f);
            }
            Settings.AuraToolsUi.AddButton(footer.transform, "清空当前", ClearCurrentTab, 96f);
            Settings.AuraToolsUi.AddButton(footer.transform, "保存", Save, 88f);
            RefreshTabButtons();
            RebuildBody();
        }

        private void SwitchTab(bool cardsTab)
        {
            cardTab = cardsTab;
            search = "";
            if (searchInput != null)
            {
                searchInput.SetTextWithoutNotify("");
            }
            RefreshTabButtons();
            RebuildBody();
        }

        private void RebuildBody()
        {
            if (body == null)
            {
                return;
            }

            Settings.AuraToolsUi.ClearChildren(body);
            var layout = body.gameObject.GetComponent<HorizontalLayoutGroup>() ?? body.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            candidateContent = CreateColumn(body, cardTab ? "可选卡牌" : "可选遗物", out _);
            selectedContent = CreateColumn(body, cardTab ? "当前卡牌" : "当前遗物", out countText);
            if (cardTab)
            {
                BuildCardCandidates();
            }
            else
            {
                BuildRelicCandidates();
            }
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
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            Settings.AuraToolsUi.AddText(
                header.transform,
                title,
                Settings.AuraToolsUi.ModuleTitleFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.Accent,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            counter = Settings.AuraToolsUi.AddText(
                header.transform,
                "",
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleRight,
                Settings.AuraToolsUi.Text,
                Settings.AuraToolsUi.TextMinHeight,
                0f,
                110f);
            return Settings.AuraToolsUi.CreateScroll(column.transform, title);
        }

        private void BuildCardCandidates()
        {
            if (candidateContent == null)
            {
                return;
            }

            foreach (var group in AuraToolsStarterDeckRuntime.BuildCandidateCardPackGroups())
            {
                var filtered = group.CardIds
                    .Where(id => MatchesSearch(id, AuraToolsStarterDeckRuntime.CardDisplayName(id)))
                    .ToList();
                CreateGroup(
                    candidateContent,
                    "card:" + group.PackId,
                    group.DisplayName,
                    filtered,
                    CreateCardCandidateRow);
            }
        }

        private void BuildRelicCandidates()
        {
            if (candidateContent == null)
            {
                return;
            }

            foreach (var group in AuraToolsStarterDeckRuntime.BuildRelicPackGroups())
            {
                var filtered = group.RelicIds
                    .Where(id => MatchesSearch(id, AuraToolsStarterDeckRuntime.RelicDisplayName(id)))
                    .ToList();
                CreateGroup(
                    candidateContent,
                    "relic:" + group.PackId,
                    group.DisplayName,
                    filtered,
                    CreateRelicCandidateRow);
            }
        }

        private void CreateGroup(
            Transform parent,
            string groupId,
            string title,
            IReadOnlyList<string> ids,
            Action<Transform, string> createRow)
        {
            if (ids.Count == 0)
            {
                return;
            }

            var expanded = !string.IsNullOrWhiteSpace(search) || expandedGroups.Contains(groupId);
            var root = Settings.AuraToolsUi.CreateLayout("Group-" + groupId, parent);
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.spacing = 6f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;
            var header = Settings.AuraToolsUi.CreateLayout("Header-" + groupId, root.transform);
            Settings.AuraToolsUi.SetFixedHeight(header, 34f);
            var image = Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
            var button = header.AddComponent<Button>();
            AuraUiButtonFeedback.Apply(button, image, Settings.AuraToolsUi.Accent);
            var content = Settings.AuraToolsUi.CreateLayout("Content-" + groupId, root.transform);
            var contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 6f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            var rowsBuilt = false;
            void Bind()
            {
                if (expanded && !rowsBuilt)
                {
                    foreach (var id in ids)
                    {
                        createRow(content.transform, id);
                    }
                    rowsBuilt = true;
                }
                Settings.AuraToolsUi.SetFoldoutExpanded(content, expanded, root.transform);
            }
            button.onClick.AddListener(() =>
            {
                expanded = !expanded;
                if (expanded) expandedGroups.Add(groupId); else expandedGroups.Remove(groupId);
                Bind();
            });
            var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(10, 10, 2, 2);
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            Settings.AuraToolsUi.AddText(
                header.transform,
                title,
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.Accent,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            Settings.AuraToolsUi.AddText(
                header.transform,
                ids.Count.ToString(),
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleRight,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                0f,
                52f);
            Bind();
        }

        private void CreateCardCandidateRow(Transform parent, string cardId)
        {
            var row = CreateRow(parent, "Card-" + cardId);
            CreateIcon(row.transform, AuraToolsStarterDeckRuntime.TryLoadCardIcon(cardId), AuraToolsStarterDeckRuntime.CardCost(cardId));
            Settings.AuraToolsUi.AddText(
                row.transform,
                AuraToolsStarterDeckRuntime.CardDisplayNameWithSpecialMarker(cardId),
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.Text,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            Settings.AuraToolsUi.AddText(
                row.transform,
                AuraToolsStarterDeckRuntime.CardRarity(cardId) + " / " + AuraToolsStarterDeckRuntime.CardCost(cardId),
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleCenter,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                0f,
                94f);
            Settings.AuraToolsUi.AddButton(row.transform, "添加", () =>
            {
                if (cards.Count >= StarterDeckSettings.MaximumCardCount)
                {
                    SetHint("卡牌已达到 15 张上限。");
                    return;
                }
                inheritCards = false;
                cards.Add(cardId);
                RefreshSelected();
            }, 68f, 30f);
        }

        private void CreateRelicCandidateRow(Transform parent, string relicId)
        {
            var row = CreateRow(parent, "Relic-" + relicId);
            CreateIcon(row.transform, AuraToolsStarterDeckRuntime.TryLoadRelicIcon(relicId), AuraToolsStarterDeckRuntime.RelicRarity(relicId));
            Settings.AuraToolsUi.AddText(
                row.transform,
                AuraToolsStarterDeckRuntime.RelicDisplayName(relicId),
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.Text,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            Settings.AuraToolsUi.AddText(
                row.transform,
                AuraToolsStarterDeckRuntime.RelicRarity(relicId),
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleCenter,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                0f,
                64f);
            var add = Settings.AuraToolsUi.AddButton(row.transform, relics.Contains(relicId, StringComparer.OrdinalIgnoreCase) ? "已选择" : "添加", () =>
            {
                if (relics.Contains(relicId, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }
                if (relics.Count >= StarterDeckSettings.MaximumRelicCount)
                {
                    SetHint("遗物已达到 6 个上限。");
                    return;
                }
                inheritRelics = false;
                relics.Add(relicId);
                RebuildBody();
            }, 68f, 30f);
            add.interactable = !relics.Contains(relicId, StringComparer.OrdinalIgnoreCase);
        }

        private void RefreshSelected()
        {
            if (selectedContent == null)
            {
                return;
            }

            Settings.AuraToolsUi.ClearChildren(selectedContent);
            var ids = cardTab ? cards : relics;
            for (var index = 0; index < ids.Count; index++)
            {
                var captured = index;
                var id = ids[index];
                var row = CreateRow(selectedContent, "Selected-" + index);
                var icon = cardTab
                    ? AuraToolsStarterDeckRuntime.TryLoadCardIcon(id)
                    : AuraToolsStarterDeckRuntime.TryLoadRelicIcon(id);
                CreateIcon(row.transform, icon, (index + 1).ToString());
                Settings.AuraToolsUi.AddText(
                    row.transform,
                    cardTab
                        ? AuraToolsStarterDeckRuntime.CardDisplayNameWithSpecialMarker(id)
                        : AuraToolsStarterDeckRuntime.RelicDisplayName(id),
                    Settings.AuraToolsUi.BodyFontSize,
                    TextAnchor.MiddleLeft,
                    Settings.AuraToolsUi.Text,
                    Settings.AuraToolsUi.TextMinHeight,
                    1f);
                Settings.AuraToolsUi.AddButton(row.transform, "↑", () => Move(ids, captured, -1), 42f, 30f).interactable = index > 0;
                Settings.AuraToolsUi.AddButton(row.transform, "↓", () => Move(ids, captured, 1), 42f, 30f).interactable = index + 1 < ids.Count;
                Settings.AuraToolsUi.AddButton(row.transform, "移除", () =>
                {
                    if (captured >= 0 && captured < ids.Count)
                    {
                        if (cardTab) inheritCards = false; else inheritRelics = false;
                        ids.RemoveAt(captured);
                        RebuildBody();
                    }
                }, 66f, 30f);
            }

            UpdateStatus();
        }

        private void Move(List<string> ids, int index, int offset)
        {
            var target = index + offset;
            if (index < 0 || index >= ids.Count || target < 0 || target >= ids.Count)
            {
                return;
            }
            var value = ids[index];
            ids[index] = ids[target];
            ids[target] = value;
            if (cardTab) inheritCards = false; else inheritRelics = false;
            RefreshSelected();
        }

        private void RestoreCurrentTab()
        {
            var globalProfile = AuraToolsConfigService.MatchExperience.StarterDeck.GlobalProfile;
            if (cardTab)
            {
                inheritCards = true;
                cards.Clear();
                cards.AddRange(globalProfile.CardIds);
            }
            else
            {
                inheritRelics = true;
                relics.Clear();
                relics.AddRange(globalProfile.RelicIds);
            }
            RebuildBody();
        }

        private void ClearCurrentTab()
        {
            if (cardTab)
            {
                inheritCards = false;
                cards.Clear();
            }
            else
            {
                inheritRelics = false;
                relics.Clear();
            }
            RebuildBody();
        }

        private void Save()
        {
            _ = TrySaveCurrent();
        }

        private bool TrySaveCurrent()
        {
            if (AuraToolsConfigService.IsModuleConfigReadOnly(AuraToolModuleIds.StarterDeck))
            {
                SetHint("当前配置由更新版本创建，处于只读状态。");
                return false;
            }

            var previous = profile.Clone();
            profile.InheritCards = !global && inheritCards;
            profile.InheritRelics = !global && inheritRelics;
            profile.CardIds = profile.InheritCards ? new List<string>() : cards.ToList();
            profile.RelicIds = profile.InheritRelics ? new List<string>() : relics.ToList();
            profile.Normalize(roleId, global ? "全局自定义开局" : RoleCatalog.GetDisplayName(roleId) + " 自定义开局");
            bool saved;
            try
            {
                saved = AuraToolsConfigService.TrySaveStarterDeck();
            }
            catch (Exception ex)
            {
                RestorePrevious();
                SetHint("保存失败：" + ex.Message);
                return false;
            }
            if (!saved)
            {
                RestorePrevious();
                SetHint("保存失败，已恢复保存前配置。");
                return false;
            }
            SetHint("已保存：卡牌 " + (profile.InheritCards ? "继承全局" : profile.CardIds.Count + "/15")
                    + "；遗物 " + (profile.InheritRelics ? "继承全局" : profile.RelicIds.Count + "/6") + "。");
            return true;

            void RestorePrevious()
            {
                profile.RoleId = previous.RoleId;
                profile.DisplayName = previous.DisplayName;
                profile.InheritCards = previous.InheritCards;
                profile.InheritRelics = previous.InheritRelics;
                profile.CardIds = previous.CardIds;
                profile.RelicIds = previous.RelicIds;
            }
        }

        private void Export()
        {
            try
            {
                if (!TrySaveCurrent())
                {
                    return;
                }
                var path = CustomStartTransferService.Export(roleId, global);
                SetHint("已导出到 " + path);
            }
            catch (Exception ex)
            {
                SetHint("导出失败：" + ex.Message);
            }
        }

        private void PickImport()
        {
            OptionalFileDialog.PickFileAsync(
                "导入自定义开局",
                new[]
                {
                    new OptionalFileDialogFilter("自定义开局", "*.aurastart.json;*.json"),
                    new OptionalFileDialogFilter("JSON 文件", "*.json")
                },
                "json",
                PathForImports(),
                result =>
                {
                    if (!result.Selected)
                    {
                        if (result.Status == OptionalFileDialogStatus.Error)
                        {
                            SetHint("文件选择失败：" + result.Message);
                        }
                        return;
                    }
                    var plans = CustomStartTransferService.InspectAll(result.Path);
                    if (plans.Count == 1)
                    {
                        ShowImportPreview(plans[0]);
                    }
                    else
                    {
                        ShowImportCandidatePicker(plans);
                    }
                });
        }

        private void ShowImportCandidatePicker(IReadOnlyList<CustomStartImportPlan> plans)
        {
            if (body == null)
            {
                return;
            }
            var window = Settings.AuraToolsUi.CreateOverlay(
                "AuraTools.CustomStartImportCandidates",
                body,
                "选择要导入的旧版 Profile");
            var content = Settings.AuraToolsUi.CreateScroll(window.transform, "CustomStartImportCandidates");
            foreach (var plan in plans)
            {
                var captured = plan;
                var row = CreateRow(content, "Import-" + plan.DisplayName);
                Settings.AuraToolsUi.AddText(
                    row.transform,
                    plan.DisplayName,
                    Settings.AuraToolsUi.BodyFontSize,
                    TextAnchor.MiddleLeft,
                    Settings.AuraToolsUi.Text,
                    Settings.AuraToolsUi.TextMinHeight,
                    1f);
                Settings.AuraToolsUi.AddText(
                    row.transform,
                    plan.Summary,
                    Settings.AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleRight,
                    Settings.AuraToolsUi.MutedText,
                    Settings.AuraToolsUi.TextMinHeight,
                    0f,
                    180f);
                Settings.AuraToolsUi.AddButton(row.transform, "预览", () =>
                {
                    UnityEngine.Object.Destroy(window);
                    ShowImportPreview(captured);
                }, 72f, 30f).interactable = plan.Compatible;
            }
        }

        private void ShowImportPreview(CustomStartImportPlan plan)
        {
            if (body == null)
            {
                return;
            }
            var window = Settings.AuraToolsUi.CreateOverlay(
                "AuraTools.CustomStartImportPreview",
                body,
                "导入预览 - " + (string.IsNullOrWhiteSpace(plan.DisplayName) ? "自定义开局" : plan.DisplayName));
            var content = Settings.AuraToolsUi.CreateScroll(window.transform, "CustomStartImportPreview");
            Settings.AuraToolsUi.AddText(
                content,
                plan.Summary,
                Settings.AuraToolsUi.ModuleTitleFontSize,
                TextAnchor.MiddleLeft,
                plan.Compatible ? Settings.AuraToolsUi.SuccessText : Settings.AuraToolsUi.WarningText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            foreach (var warning in plan.Warnings)
            {
                Settings.AuraToolsUi.AddText(
                    content,
                    warning,
                    Settings.AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    Settings.AuraToolsUi.WarningText,
                    Settings.AuraToolsUi.TextMinHeight,
                    1f);
            }
            var footer = Settings.AuraToolsUi.CreateLayout("ImportFooter", window.transform);
            Settings.AuraToolsUi.SetFixedHeight(footer, Settings.AuraToolsUi.FooterHeight);
            var layout = footer.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            Settings.AuraToolsUi.AddText(
                footer.transform,
                "确认后将覆盖当前配置",
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            var replace = Settings.AuraToolsUi.AddButton(footer.transform, "替换当前配置", () =>
            {
                try
                {
                    CustomStartTransferService.Commit(plan, roleId, global);
                    inheritCards = false;
                    inheritRelics = false;
                    cards.Clear();
                    cards.AddRange(plan.CardIds);
                    relics.Clear();
                    relics.AddRange(plan.RelicIds);
                    profile.InheritCards = false;
                    profile.InheritRelics = false;
                    profile.CardIds = cards.ToList();
                    profile.RelicIds = relics.ToList();
                    UnityEngine.Object.Destroy(window);
                    RebuildBody();
                    SetHint("导入完成：" + plan.Summary + "。");
                }
                catch (Exception ex)
                {
                    SetHint("导入失败：" + ex.Message);
                }
            }, 128f);
            replace.interactable = plan.Compatible;
        }

        private static string PathForImports()
        {
            return System.IO.Path.Combine(AuraToolsConfigService.DataRootDirectory, "Exports", "CustomStart");
        }

        private bool MatchesSearch(string id, string displayName)
        {
            return string.IsNullOrWhiteSpace(search)
                   || id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                   || displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateStatus()
        {
            RefreshTabButtons();
            if (countText != null)
            {
                countText.text = cardTab ? cards.Count + "/15" : relics.Count + "/6";
            }
            var inherited = !global && (cardTab ? inheritCards : inheritRelics);
            if (cardTab)
            {
                SetHint(inherited
                    ? "卡牌继承全局配置。"
                    : cards.Count == 0 ? "卡牌为空：开局保留游戏默认卡组。" : "开局精确替换为 " + cards.Count + " 张卡牌。");
            }
            else
            {
                SetHint(inherited
                    ? "遗物继承全局配置。"
                    : relics.Count == 0 ? "遗物为空：开局精确清空装备遗物。" : "开局精确替换为 " + relics.Count + " 个遗物。");
            }
        }

        private void RefreshTabButtons()
        {
            if (cardTabButton != null)
            {
                Settings.AuraToolsUi.SetButtonLabel(cardTabButton, "卡牌 " + cards.Count + "/15");
                cardTabButton.interactable = !cardTab;
            }
            if (relicTabButton != null)
            {
                Settings.AuraToolsUi.SetButtonLabel(relicTabButton, "遗物 " + relics.Count + "/6");
                relicTabButton.interactable = cardTab;
            }
        }

        private void SetHint(string message)
        {
            if (hintText != null)
            {
                hintText.text = message;
            }
        }
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

    private static void CreateIcon(Transform parent, Sprite? sprite, string fallback)
    {
        var root = Settings.AuraToolsUi.CreateLayout("Icon", parent);
        Settings.AuraToolsUi.SetFixedSize(root, AuraToolsStarterDeckRuntime.CardImageColumnWidth, Settings.AuraToolsUi.TextMinHeight);
        var image = Settings.AuraToolsUi.AddImage(root, new Color(0.025f, 0.022f, 0.045f, 0.98f));
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
        }
        else
        {
            Settings.AuraToolsUi.AddFillText(
                root.transform,
                fallback,
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleCenter,
                Settings.AuraToolsUi.Accent);
        }
    }
}
