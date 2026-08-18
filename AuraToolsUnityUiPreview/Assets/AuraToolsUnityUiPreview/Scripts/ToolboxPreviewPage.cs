using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraTools.UnityUiPreview
{
    internal sealed class ToolboxPreviewPage
    {
        private sealed class CategoryView
        {
            internal PreviewCategory Model;
            internal GameObject Root;
            internal Button Button;
            internal Image Background;
            internal Image Marker;
            internal Text Label;
            internal Text Count;
        }

        private sealed class ModuleRowView
        {
            internal PreviewModule Module;
            internal GameObject Root;
            internal Image Marker;
            internal Text Status;
            internal Image Icon;
            internal PreviewToolboxCheckboxControl Checkbox;

            internal void Refresh()
            {
                var color = StatusColor(Module);
                Marker.color = color;
                Status.color = color;
                Status.text = string.IsNullOrWhiteSpace(Module.Attention)
                    ? Module.Summary
                    : Module.Summary + "  ·  " + Module.Attention;
                Icon.color = Module.Enabled ? PreviewTheme.Text : PreviewTheme.MutedText;
                Checkbox.Value = Module.Enabled;
                Checkbox.Interactable = Module.Availability != "error" && Module.Availability != "busy";
            }

            private static Color StatusColor(PreviewModule module)
            {
                if (module.Availability == "error") return PreviewTheme.Error;
                if (!string.IsNullOrWhiteSpace(module.Attention)
                    || module.Availability == "warning"
                    || module.Availability == "busy") return PreviewTheme.Warning;
                return module.Enabled ? PreviewTheme.Success : PreviewTheme.Disabled;
            }
        }

        private readonly SettingsPreviewController controller;
        private readonly Dictionary<string, CategoryView> categories = new Dictionary<string, CategoryView>(StringComparer.Ordinal);
        private readonly List<ModuleRowView> rows = new List<ModuleRowView>();
        private List<PreviewModule> modules = new List<PreviewModule>();
        private string selectedCategory = "all";
        private string scenario = "default";
        private GameObject categoryRail;
        private PreviewScrollArea list;
        private InputField search;
        private Button clearSearch;
        private Button folder;
        private Text resultTitle;
        private GameObject emptyState;
        private Text emptyText;

        internal ToolboxPreviewPage(SettingsPreviewController controller)
        {
            this.controller = controller;
        }

        internal GameObject Root { get; private set; }

        internal string Scenario
        {
            get { return scenario; }
        }

        internal int VisibleRowCount
        {
            get { return rows.Count; }
        }

        internal void Build(Transform parent)
        {
            Root = PreviewUi.Stretch("AuraToolsSettingsPanel", parent, Vector4.zero);
            var background = PreviewUi.Image(Root, PreviewTheme.Background);
            background.raycastTarget = true;

            var underlay = PreviewUi.Stretch("NativeLeakProbe", Root.transform, Vector4.zero);
            var probeText = PreviewUi.Text(underlay, "模式　无边框　窗口", 32, TextAnchor.UpperCenter, PreviewTheme.Probe);
            probeText.raycastTarget = true;
            underlay.GetComponent<RectTransform>().offsetMin = new Vector2(0f, 300f);
            underlay.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -88f);
            underlay.transform.SetAsFirstSibling();

            var workspace = PreviewUi.Stretch("ToolboxWorkspace", Root.transform, new Vector4(10f, 10f, 10f, 10f));
            PreviewUi.Image(workspace, Color.white, PreviewAssets.ToolboxSurface).raycastTarget = true;

            categoryRail = PreviewUi.Rect(
                "ToolboxCategoryRail",
                workspace.transform,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                new Vector2(PreviewTheme.CategoryWidth, 0f),
                Vector2.zero);
            PreviewUi.Image(categoryRail, PreviewTheme.Background);

            var content = PreviewUi.Stretch("ToolboxContent", workspace.transform, new Vector4(PreviewTheme.CategoryWidth + 10f, 0f, 0f, 0f));
            var header = PreviewUi.Rect(
                "ToolboxHeader",
                content.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, PreviewTheme.ToolboxHeaderHeight),
                Vector2.zero);
            PreviewUi.Image(header, Color.white, PreviewAssets.ToolboxControl);
            resultTitle = PreviewUi.FillText("Result", header.transform, "", 20, TextAnchor.MiddleLeft, PreviewTheme.Text, new Vector4(14f, 5f, 390f, 5f), true);

            folder = PreviewUi.ToolboxIconButton("DataDirectory", header.transform, "folder", () => controller.ShowToast("数据目录动作已触发（独立预览）"), 42f);
            PlaceRight(folder.GetComponent<RectTransform>(), 10f, 42f);
            clearSearch = PreviewUi.ToolboxIconButton("ClearSearch", header.transform, "clear", ClearSearch, 42f);
            PlaceRight(clearSearch.GetComponent<RectTransform>(), 60f, 42f);
            search = PreviewUi.ToolboxInput("Search", header.transform, "", "搜索工具…", OnSearchChanged);
            PlaceRight(search.GetComponent<RectTransform>(), 110f, 252f);

            var listHost = PreviewUi.Stretch("ToolboxListHost", content.transform, new Vector4(0f, PreviewTheme.ToolboxHeaderHeight + PreviewTheme.Spacing, 0f, 0f));
            PreviewUi.Image(listHost, PreviewTheme.Background).raycastTarget = false;
            list = PreviewUi.Scroll("ToolboxModules", listHost.transform, Vector4.zero, PreviewTheme.Spacing);
            emptyState = PreviewUi.Stretch("ToolboxEmpty", listHost.transform, Vector4.zero);
            emptyText = PreviewUi.Text(emptyState, "当前分类暂无工具。", 15, TextAnchor.MiddleCenter, PreviewTheme.MutedText);
            emptyText.raycastTarget = false;
            emptyState.SetActive(false);

            BuildCategories();
            SetScenario("default");
        }

        internal void SetScenario(string value)
        {
            scenario = NormalizeScenario(value);
            modules = PreviewCatalog.ForScenario(scenario);
            selectedCategory = "all";
            search.SetTextWithoutNotify(scenario == "empty" ? "不存在的工具" : "");
            Refresh();
        }

        internal void ClearSearch()
        {
            search.SetTextWithoutNotify("");
            Refresh();
            EventSystem.current?.SetSelectedGameObject(search.gameObject);
        }

        internal void SelectCategoryForPreview(string id)
        {
            var normalized = string.IsNullOrWhiteSpace(id) ? "all" : id.Trim();
            if (categories.ContainsKey(normalized)) SelectCategory(normalized);
        }

        internal List<string> Validate(IReadOnlyList<GameObject> nativePages)
        {
            var errors = new List<string>();
            Canvas.ForceUpdateCanvases();
            if (Root == null || !Root.activeInHierarchy)
            {
                errors.Add("toolbox root is inactive");
                return errors;
            }

            var image = Root.GetComponent<Image>();
            if (image == null || image.color.a < 0.999f)
            {
                errors.Add("toolbox root is not opaque");
            }
            foreach (var nativePage in nativePages)
            {
                if (nativePage != null && nativePage.activeSelf)
                {
                    errors.Add("native page remained active: " + nativePage.name);
                }
            }
            foreach (var category in categories.Values.Where(view => view.Root.activeSelf))
            {
                category.Label.cachedTextGenerator.Populate(
                    category.Label.text,
                    category.Label.GetGenerationSettings(category.Label.rectTransform.rect.size));
                if (category.Label.cachedTextGenerator.characterCountVisible < category.Label.text.Length)
                {
                    errors.Add("category label truncated: " + category.Model.Label);
                }
            }
            foreach (var row in rows)
            {
                var height = row.Root.GetComponent<RectTransform>().rect.height;
                if (Mathf.Abs(height - PreviewTheme.ModuleRowHeight) > 1f)
                {
                    errors.Add("module row height drifted: " + row.Module.Id);
                }
            }

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || !hits[0].gameObject.transform.IsChildOf(Root.transform))
            {
                errors.Add("toolbox center raycast escaped the owned page");
            }
            return errors;
        }

        private void BuildCategories()
        {
            var y = -8f;
            foreach (var model in PreviewCatalog.Categories)
            {
                var root = PreviewUi.Rect(
                    "Category-" + model.Id,
                    categoryRail.transform,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(-16f, 48f),
                    new Vector2(0f, y));
                var background = PreviewUi.Image(root, Color.white);
                var button = root.AddComponent<Button>();
                button.targetGraphic = background;
                var markerRoot = PreviewUi.Rect("Selected", root.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(3f, 30f), new Vector2(7f, 0f));
                var marker = PreviewUi.Image(markerRoot, PreviewTheme.Accent);
                marker.raycastTarget = false;
                var iconRoot = PreviewUi.Rect("Icon", root.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 22f), new Vector2(22f, 0f));
                PreviewUi.Image(iconRoot, PreviewTheme.MutedText, PreviewAssets.Icon(model.Icon)).raycastTarget = false;
                var labelRoot = PreviewUi.Stretch("Label", root.transform, new Vector4(52f, 5f, 30f, 5f));
                var label = PreviewUi.Text(labelRoot, model.Label, 16, TextAnchor.MiddleLeft, PreviewTheme.MutedText, true);
                var countRoot = PreviewUi.Rect("Count", root.transform, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(20f, 0f), new Vector2(-6f, 0f));
                var count = PreviewUi.Text(countRoot, "0", 12, TextAnchor.MiddleRight, PreviewTheme.MutedText, true);
                var id = model.Id;
                button.onClick.AddListener(() => SelectCategory(id));
                categories[id] = new CategoryView
                {
                    Model = model,
                    Root = root,
                    Button = button,
                    Background = background,
                    Marker = marker,
                    Label = label,
                    Count = count
                };
                y -= 52f;
            }
        }

        private void SelectCategory(string id)
        {
            if (selectedCategory == id)
            {
                return;
            }
            selectedCategory = id;
            Refresh();
            list.Scroll.verticalNormalizedPosition = 1f;
        }

        private void OnSearchChanged(string value)
        {
            Refresh();
        }

        private void Refresh()
        {
            RefreshCategories();
            RebuildRows();
            var visible = FilteredModules();
            resultTitle.text = string.IsNullOrWhiteSpace(search.text)
                ? CategoryLabel(selectedCategory) + "  ·  " + visible.Count
                : "搜索结果  ·  " + visible.Count;
            clearSearch.gameObject.SetActive(!string.IsNullOrWhiteSpace(search.text));
            PlaceRight(search.GetComponent<RectTransform>(), clearSearch.gameObject.activeSelf ? 110f : 60f, 252f);
            emptyText.text = string.IsNullOrWhiteSpace(search.text)
                ? "当前分类暂无工具。"
                : "没有符合搜索条件的工具。";
        }

        private void RefreshCategories()
        {
            var counts = PreviewCatalog.Categories.ToDictionary(category => category.Id, category => 0, StringComparer.Ordinal);
            foreach (var module in modules)
            {
                counts["all"]++;
                if (counts.ContainsKey(module.Category)) counts[module.Category]++;
            }
            var projectedSelection = string.IsNullOrWhiteSpace(search.text) ? selectedCategory : "all";
            foreach (var pair in categories)
            {
                var view = pair.Value;
                view.Root.SetActive(pair.Key != "extensions" || counts["extensions"] > 0);
                view.Count.text = counts[pair.Key].ToString();
                var selected = pair.Key == projectedSelection;
                view.Marker.enabled = false;
                view.Label.color = selected ? PreviewTheme.Text : PreviewTheme.MutedText;
                view.Count.color = selected ? PreviewTheme.Accent : PreviewTheme.MutedText;
                view.Background.sprite = selected ? PreviewAssets.ToolboxCategorySelected : null;
                view.Background.type = selected ? Image.Type.Sliced : Image.Type.Simple;
                PreviewUi.ApplyButtonColors(
                    view.Button,
                    selected ? Color.white : PreviewTheme.Panel,
                    PreviewTheme.ControlHighlighted);
            }
        }

        private void RebuildRows()
        {
            for (var i = list.Content.childCount - 1; i >= 0; i--)
            {
                var child = list.Content.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            rows.Clear();
            foreach (var module in FilteredModules())
            {
                rows.Add(CreateRow(module));
            }
            emptyState.SetActive(rows.Count == 0);
            Canvas.ForceUpdateCanvases();
        }

        private ModuleRowView CreateRow(PreviewModule module)
        {
            var root = PreviewUi.Rect("ToolModule-" + module.Id, list.Content, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, 0f, PreviewTheme.ModuleRowHeight);
            PreviewUi.Image(root, new Color(0.063f, 0.078f, 0.227f, 1f));
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var markerRoot = PreviewUi.Rect("StatusMarker", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(markerRoot, 4f, 72f);
            var marker = PreviewUi.Image(markerRoot, PreviewTheme.Disabled);
            marker.raycastTarget = false;

            var iconHolder = PreviewUi.Rect("ModuleIcon", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(iconHolder, 46f, 46f);
            PreviewUi.Image(iconHolder, Color.white, PreviewAssets.ToolboxControl).raycastTarget = false;
            var iconRoot = PreviewUi.Rect("Icon", iconHolder.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28f, 28f), Vector2.zero);
            var icon = PreviewUi.Image(iconRoot, PreviewTheme.Text, PreviewAssets.Icon(module.Icon));
            icon.raycastTarget = false;

            var copy = PreviewUi.Rect("Copy", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Flexible(copy, 1f, 0f).minWidth = 140f;
            var copyLayout = copy.AddComponent<VerticalLayoutGroup>();
            copyLayout.spacing = 0f;
            copyLayout.childControlWidth = true;
            copyLayout.childControlHeight = true;
            copyLayout.childForceExpandWidth = true;
            copyLayout.childForceExpandHeight = false;
            var titleRoot = PreviewUi.Rect("Title", copy.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(titleRoot, 0f, 27f);
            PreviewUi.Text(titleRoot, module.Name + (module.Experimental ? "  ·  实验" : ""), 20, TextAnchor.MiddleLeft, PreviewTheme.Text, true);
            var statusRoot = PreviewUi.Rect("Status", copy.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(statusRoot, 0f, 26f);
            var status = PreviewUi.Text(statusRoot, "", 16, TextAnchor.MiddleLeft, PreviewTheme.MutedText, true);
            var descriptionRoot = PreviewUi.Rect("Description", copy.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(descriptionRoot, 0f, 23f);
            PreviewUi.Text(descriptionRoot, module.Description, 14, TextAnchor.MiddleLeft, PreviewTheme.MutedText, true);

            var settingsHolder = PreviewUi.Rect("Settings", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(settingsHolder, 42f, 42f);
            if (module.HasSettings)
            {
                var settings = PreviewUi.ToolboxIconButton("OpenSettings", settingsHolder.transform, "settings", () => controller.ShowToolSettings(module.Name, module.Summary, module.Description), 42f);
                var rect = settings.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                settingsHolder.AddComponent<CanvasGroup>().alpha = 0f;
            }

            var enableRoot = PreviewUi.Rect("EnableControl", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(enableRoot, 88f, 42f);
            var enableLayout = enableRoot.AddComponent<HorizontalLayoutGroup>();
            enableLayout.spacing = 6f;
            enableLayout.childAlignment = TextAnchor.MiddleCenter;
            enableLayout.childControlWidth = true;
            enableLayout.childControlHeight = true;
            enableLayout.childForceExpandWidth = false;
            enableLayout.childForceExpandHeight = false;
            var enableLabel = PreviewUi.Rect("Label", enableRoot.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(enableLabel, 44f, 32f);
            PreviewUi.Text(enableLabel, "启用", 14, TextAnchor.MiddleRight, PreviewTheme.MutedText);

            ModuleRowView row = null;
            var checkbox = PreviewToolboxCheckboxControl.Create(enableRoot.transform, module.Enabled, enabled =>
            {
                module.Enabled = enabled;
                row.Refresh();
            }, 32f);
            row = new ModuleRowView
            {
                Module = module,
                Root = root,
                Marker = marker,
                Status = status,
                Icon = icon,
                Checkbox = checkbox
            };
            row.Refresh();
            return row;
        }

        private List<PreviewModule> FilteredModules()
        {
            var query = (search == null ? "" : search.text).Trim();
            return modules
                .Where(module => !string.IsNullOrWhiteSpace(query)
                                 || selectedCategory == "all"
                                 || module.Category == selectedCategory)
                .Where(module => string.IsNullOrWhiteSpace(query)
                                 || Contains(module.Name, query)
                                 || Contains(module.Description, query)
                                 || Contains(module.Summary, query)
                                 || Contains(module.Attention, query))
                .ToList();
        }

        private static bool Contains(string value, string query)
        {
            return (value ?? "").IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void PlaceRight(RectTransform rect, float right, float width)
        {
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(width, 42f);
            rect.anchoredPosition = new Vector2(-right, 0f);
        }

        private static string NormalizeScenario(string value)
        {
            var normalized = (value ?? "default").Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "long-text":
                case "warning":
                case "empty":
                case "extensions":
                    return normalized;
                default:
                    return "default";
            }
        }

        private static string CategoryLabel(string id)
        {
            var category = PreviewCatalog.Categories.FirstOrDefault(item => item.Id == id);
            return category == null ? "全部" : category.Label;
        }
    }
}
