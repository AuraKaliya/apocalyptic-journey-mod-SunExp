using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Features.SharedResources;
using AuraToolsExp.Dll.Modules;
using AuraToolsExp.Dll.Modules.Contracts;
using AuraUi.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Settings;

internal sealed class ToolboxSettingsShellState
{
    public string CategoryId { get; set; } = "all";

    public string SearchText { get; set; } = "";

    public Dictionary<string, AuraUiViewStateSnapshot> ScrollByCategory { get; } =
        new(StringComparer.Ordinal);
}

internal sealed class ToolboxSettingsShell : MonoBehaviour
{
    private static readonly ToolboxCategoryDefinition[] Categories =
    {
        new() { Id = "all", Label = "全部", IconKey = "category.all" },
        new() { Id = "gameplay", Label = "游戏体验", IconKey = "category.gameplay" },
        new() { Id = "presentation", Label = "表现资源", IconKey = "category.presentation" },
        new() { Id = "records", Label = "对局记录", IconKey = "category.records" },
        new() { Id = "multiplayer", Label = "联机工具", IconKey = "category.multiplayer" },
        new() { Id = "intelligence", Label = "智能战斗", IconKey = "category.intelligence" },
        new() { Id = "extensions", Label = "扩展工具", IconKey = "category.extensions" },
        new() { Id = "system", Label = "系统数据", IconKey = "category.system" }
    };

    private static readonly ToolboxSettingsShellState SessionState = new();
    private readonly Dictionary<string, ToolboxModuleListItem> visibleRows =
        new(StringComparer.Ordinal);
    private AuraUiKeyedListReconciler<string, IAuraToolModule>? reconciler;
    private ToolboxCategoryRail? categoryRail;
    private Transform? listContent;
    private ScrollRect? listScroll;
    private TextMeshProUGUI? resultText;
    private TMP_InputField? searchInput;
    private ToolboxIconButtonV2? clearSearchButton;
    private GameObject? emptyState;
    private TextMeshProUGUI? emptyStateText;
    private GameObject? workspace;
    private float nextRefreshAt;
    private bool built;

    public static ToolboxSettingsShell Build(Transform panel)
    {
        var shell = panel.GetComponent<ToolboxSettingsShell>()
                    ?? panel.gameObject.AddComponent<ToolboxSettingsShell>();
        shell.BuildOnce(panel);
        return shell;
    }

    public void Activate()
    {
        RefreshVisibleStates();
    }

    private void BuildOnce(Transform panel)
    {
        if (built && workspace != null)
        {
            return;
        }

        if (!built)
        {
            built = true;
            AuraToolModuleHost.States.Changed += OnModuleStateChanged;
            AuraToolModuleHost.Catalog.Changed += OnCatalogChanged;
        }
        else
        {
            visibleRows.Clear();
            reconciler = null;
            listContent = null;
            listScroll = null;
        }

        workspace = AuraToolsUi.CreateLayout("ToolboxWorkspace", panel);
        AuraUiStableId.Assign(workspace, "toolbox.workspace");
        ToolboxSurfaceV2.ApplyToolboxHome(workspace).raycastTarget = false;
        var workspaceElement = AuraToolsUi.EnsureLayoutElement(workspace);
        workspaceElement.flexibleWidth = 1f;
        workspaceElement.flexibleHeight = 1f;
        var workspaceLayout = workspace.AddComponent<HorizontalLayoutGroup>();
        workspaceLayout.spacing = 10f;
        workspaceLayout.childControlWidth = true;
        workspaceLayout.childControlHeight = true;
        workspaceLayout.childForceExpandWidth = false;
        workspaceLayout.childForceExpandHeight = true;

        categoryRail = ToolboxCategoryRail.Create(
            workspace.transform,
            Categories,
            SelectCategory);

        var content = AuraToolsUi.CreateLayout("ToolboxContent", workspace.transform);
        var contentElement = AuraToolsUi.EnsureLayoutElement(content);
        contentElement.flexibleWidth = 1f;
        contentElement.flexibleHeight = 1f;
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = ToolboxVisualSpec.Spacing;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var header = AuraToolsUi.CreateLayout("ToolboxHeader", content.transform);
        AuraToolsUi.SetFixedHeight(header, AuraToolsUi.ToolboxHeaderHeight);
        AuraUiStableId.Assign(header, "toolbox.header");
        ToolboxSurfaceV2.ApplyControl(header);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(14, 10, 8, 8);
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        resultText = AuraToolsUi.AddTmpText(
            header.transform,
            "",
            ToolboxVisualSpec.TitleSize,
            TextAnchor.MiddleLeft,
            ToolboxVisualSpec.Text,
            44f,
            1f,
            autoSize: true);
        resultText.textWrappingMode = TextWrappingModes.NoWrap;

        searchInput = ToolboxSearchFieldV2.Create(
            header.transform,
            SessionState.SearchText,
            value =>
            {
                SessionState.SearchText = value ?? "";
                RebuildRows(preserveCurrentView: true);
            },
            ToolboxVisualSpec.SearchWidth);
        AuraUiStableId.Assign(searchInput.gameObject, "toolbox.search");

        clearSearchButton = ToolboxIconButtonV2.Create(
            header.transform,
            "action.clear",
            "清空搜索",
            ClearSearch,
            ToolboxVisualSpec.IconButtonSize,
            "×");
        AuraUiStableId.Assign(clearSearchButton.Root, "toolbox.search.clear");

        var directoryButton = ToolboxIconButtonV2.Create(
            header.transform,
            "action.folder",
            "打开数据目录",
            () => FileResourceUtil.OpenDirectory(
                AuraToolsExp.Dll.Config.AuraToolsConfigService.DataRootDirectory),
            ToolboxVisualSpec.IconButtonSize,
            "夹");
        AuraUiStableId.Assign(directoryButton.Root, "toolbox.data-directory");

        var refreshResourcesButton = ToolboxIconButtonV2.Create(
            header.transform,
            "action.refresh",
            "刷新共享资源",
            () => AuraToolsSharedResourceDiscoveryRuntime.Refresh("toolbox"),
            ToolboxVisualSpec.IconButtonSize,
            "刷");
        AuraUiStableId.Assign(refreshResourcesButton.Root, "toolbox.resources.refresh");

        var listArea = AuraToolsUi.CreateLayout("ToolboxListArea", content.transform);
        var listAreaElement = AuraToolsUi.EnsureLayoutElement(listArea);
        listAreaElement.flexibleWidth = 1f;
        listAreaElement.flexibleHeight = 1f;
        AuraToolsUi.AddImage(listArea, ToolboxVisualSpec.Workspace).raycastTarget = false;

        listContent = AuraToolsUi.CreateScroll(listArea.transform, "ToolboxModules");
        listScroll = AuraUiViewState.ResolveScroll(listContent);
        reconciler = new AuraUiKeyedListReconciler<string, IAuraToolModule>(
            listContent,
            StringComparer.Ordinal,
            module => module.Descriptor.ModuleId,
            CreateModuleRow,
            UpdateModuleRow);

        emptyState = AuraToolsUi.CreateRect(
            "ToolboxEmpty",
            listArea.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        emptyStateText = AuraToolsUi.AddTmpFillText(
            emptyState.transform,
            "没有符合当前分类和搜索条件的工具。",
            ToolboxVisualSpec.StatusSize,
            TextAnchor.MiddleCenter,
            ToolboxVisualSpec.MutedText);
        emptyState.SetActive(false);

        if (!Categories.Any(category => category.Id == SessionState.CategoryId))
        {
            SessionState.CategoryId = "all";
        }
        RebuildRows(preserveCurrentView: false);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = Time.unscaledTime + 1f;
        RefreshVisibleStates();
    }

    private void OnDestroy()
    {
        AuraToolModuleHost.States.Changed -= OnModuleStateChanged;
        AuraToolModuleHost.Catalog.Changed -= OnCatalogChanged;
        ToolboxSettingsPageRouter.Close();
    }

    private void SelectCategory(string categoryId)
    {
        if (string.Equals(SessionState.CategoryId, categoryId, StringComparison.Ordinal))
        {
            return;
        }

        CaptureCurrentCategoryView();
        SessionState.CategoryId = categoryId;
        RebuildRows(preserveCurrentView: false);
    }

    private void ClearSearch()
    {
        if (string.IsNullOrWhiteSpace(SessionState.SearchText))
        {
            return;
        }

        SessionState.SearchText = "";
        searchInput?.SetTextWithoutNotify("");
        RebuildRows(preserveCurrentView: true);
    }

    private void CaptureCurrentCategoryView()
    {
        if (listScroll == null)
        {
            return;
        }

        SessionState.ScrollByCategory[SessionState.CategoryId] =
            AuraUiViewState.Capture(listScroll);
    }

    private void RebuildRows(bool preserveCurrentView)
    {
        if (reconciler == null)
        {
            return;
        }

        var modules = FilteredModules();
        reconciler.Reconcile(modules, preserveCurrentView);
        emptyState?.SetActive(modules.Count == 0);
        if (emptyStateText != null)
        {
            emptyStateText.text = string.IsNullOrWhiteSpace(SessionState.SearchText)
                ? "当前分类暂无工具。"
                : "没有符合搜索条件的工具。";
        }
        if (resultText != null)
        {
            resultText.text = string.IsNullOrWhiteSpace(SessionState.SearchText)
                ? CategoryLabel(SessionState.CategoryId) + "  ·  " + modules.Count
                : "搜索结果  ·  " + modules.Count;
        }
        clearSearchButton?.SetVisible(
            !string.IsNullOrWhiteSpace(SessionState.SearchText));

        RefreshCategoryRail();
        if (!preserveCurrentView
            && listContent != null
            && SessionState.ScrollByCategory.TryGetValue(
                SessionState.CategoryId,
                out var saved))
        {
            AuraUiViewState.RestoreAfterLayout(
                listContent,
                saved,
                "AuraTools.Toolbox.Category");
        }
        else if (!preserveCurrentView && listScroll != null)
        {
            listScroll.verticalNormalizedPosition = 1f;
        }

        RefreshVisibleStates();
    }

    private List<IAuraToolModule> FilteredModules()
    {
        var categoryId = SessionState.CategoryId;
        var search = (SessionState.SearchText ?? "").Trim();
        return AuraToolModuleHost.Catalog.VisibleModules
            .Where(module => !string.IsNullOrWhiteSpace(search)
                             || categoryId == "all"
                             || string.Equals(
                                 module.Descriptor.CategoryId,
                                 categoryId,
                                 StringComparison.Ordinal))
            .Where(module => MatchesSearch(module.Descriptor, search))
            .OrderBy(module => module.Descriptor.Order)
            .ThenBy(module => module.Descriptor.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private GameObject CreateModuleRow(IAuraToolModule module)
    {
        var row = AuraToolsUi.CreateLayout(
            "ToolModule-" + module.Descriptor.ModuleId,
            listContent!);
        AuraUiStableId.Assign(row, "toolbox.module." + module.Descriptor.ModuleId);
        var view = row.AddComponent<ToolboxModuleListItem>();
        view.Build(module);
        visibleRows[module.Descriptor.ModuleId] = view;
        return row;
    }

    private void UpdateModuleRow(GameObject row, IAuraToolModule module)
    {
        var view = row.GetComponent<ToolboxModuleListItem>();
        if (view == null)
        {
            view = row.AddComponent<ToolboxModuleListItem>();
            view.Build(module);
        }
        visibleRows[module.Descriptor.ModuleId] = view;
        view.Bind(module, AuraToolModuleHost.RefreshState(module.Descriptor.ModuleId));
    }

    private void RefreshVisibleStates()
    {
        foreach (var pair in visibleRows.ToArray())
        {
            if (pair.Value == null || !pair.Value.gameObject.activeInHierarchy)
            {
                visibleRows.Remove(pair.Key);
                continue;
            }
            AuraToolModuleHost.RefreshState(pair.Key);
        }
    }

    private void OnModuleStateChanged(AuraToolModuleState state)
    {
        if (state != null
            && visibleRows.TryGetValue(state.ModuleId, out var row)
            && row != null)
        {
            row.Refresh(state);
        }
    }

    private void OnCatalogChanged()
    {
        ToolboxSettingsPageRouter.CloseIfUnavailable();
        if (string.Equals(
                SessionState.CategoryId,
                "extensions",
                StringComparison.Ordinal)
            && !HasExtensionModules())
        {
            SessionState.CategoryId = "all";
        }
        RebuildRows(preserveCurrentView: true);
    }

    private void RefreshCategoryRail()
    {
        if (categoryRail == null)
        {
            return;
        }

        var counts = Categories.ToDictionary(
            category => category.Id,
            _ => 0,
            StringComparer.Ordinal);
        foreach (var module in AuraToolModuleHost.Catalog.VisibleModules)
        {
            counts["all"]++;
            if (counts.ContainsKey(module.Descriptor.CategoryId))
            {
                counts[module.Descriptor.CategoryId]++;
            }
        }
        categoryRail.Refresh(
            string.IsNullOrWhiteSpace(SessionState.SearchText)
                ? SessionState.CategoryId
                : "all",
            counts,
            HasExtensionModules());
    }

    private static bool HasExtensionModules()
    {
        return AuraToolModuleHost.Catalog.VisibleModules.Any(module =>
            string.Equals(
                module.Descriptor.CategoryId,
                "extensions",
                StringComparison.Ordinal));
    }

    private static bool MatchesSearch(
        AuraToolModuleDescriptor descriptor,
        string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return Contains(descriptor.DisplayName, search)
               || Contains(descriptor.Description, search)
               || descriptor.SearchTerms.Any(term => Contains(term, search));
    }

    private static bool Contains(string value, string search)
    {
        return (value ?? "").IndexOf(
                   search ?? "",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string CategoryLabel(string categoryId)
    {
        return Categories.FirstOrDefault(category => category.Id == categoryId)?.Label
               ?? "全部";
    }
}

internal static class ToolboxSettingsPageRouter
{
    private static IAuraToolSettingsPage? activePage;

    public static void Open(IAuraToolModule module, Transform source)
    {
        Close();
        if (AuraToolsConfigService.IsModuleConfigReadOnly(
                module.Descriptor.ModuleId))
        {
            return;
        }
        activePage = module.CreateSettingsPage();
        if (activePage == null)
        {
            return;
        }

        activePage.Build(new AuraToolSettingsPageContext(source));
        activePage.Activate();
    }

    public static void Close()
    {
        if (activePage == null)
        {
            return;
        }

        activePage.Deactivate();
        activePage.Dispose();
        activePage = null;
    }

    public static void CloseIfUnavailable()
    {
        if (activePage != null
            && !AuraToolModuleHost.Catalog.TryGet(activePage.ModuleId, out _))
        {
            Close();
        }
    }
}
