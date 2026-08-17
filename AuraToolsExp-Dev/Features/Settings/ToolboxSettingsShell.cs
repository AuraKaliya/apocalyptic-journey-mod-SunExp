using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using AuraToolsExp.Dll.Modules.Contracts;
using AuraUi.Shared;
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
    private sealed class Category
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private static readonly Category[] Categories =
    {
        new() { Id = "all", Label = "全部" },
        new() { Id = "gameplay", Label = "游戏体验" },
        new() { Id = "presentation", Label = "表现与资源" },
        new() { Id = "records", Label = "对局记录" },
        new() { Id = "multiplayer", Label = "联机工具" },
        new() { Id = "intelligence", Label = "智能战斗" },
        new() { Id = "system", Label = "系统与数据" }
    };

    private static readonly ToolboxSettingsShellState SessionState = new();
    private readonly Dictionary<string, Button> categoryButtons =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolboxModuleRow> visibleRows =
        new(StringComparer.Ordinal);
    private AuraUiKeyedListReconciler<string, IAuraToolModule>? reconciler;
    private Transform? listContent;
    private ScrollRect? listScroll;
    private Text? resultText;
    private GameObject? emptyState;
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
        if (built)
        {
            return;
        }

        built = true;
        AuraToolModuleHost.States.Changed += OnModuleStateChanged;

        var toolbar = AuraToolsUi.CreateLayout("ToolboxToolbar", panel);
        AuraToolsUi.SetFixedHeight(toolbar, AuraToolsUi.ToolbarHeight);
        AuraUiStableId.Assign(toolbar, "toolbox.toolbar");
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 6f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;

        foreach (var category in Categories)
        {
            var width = category.Id == "all" ? 64f : 96f;
            var button = AuraToolsUi.AddButton(
                toolbar.transform,
                category.Label,
                () => SelectCategory(category.Id),
                width);
            AuraUiStableId.Assign(
                button.gameObject,
                "toolbox.category." + category.Id);
            categoryButtons[category.Id] = button;
        }

        var actionBar = AuraToolsUi.CreateLayout("ToolboxActions", panel);
        AuraToolsUi.SetFixedHeight(actionBar, AuraToolsUi.ToolbarHeight);
        AuraUiStableId.Assign(actionBar, "toolbox.actions");
        var actionLayout = actionBar.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 6f;
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = false;
        actionLayout.childForceExpandHeight = false;
        AuraToolsUi.AddText(
            actionBar.transform,
            "搜索",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleRight,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            44f);
        var search = AuraToolsUi.AddInput(
            actionBar.transform,
            SessionState.SearchText,
            value =>
            {
                SessionState.SearchText = (value ?? "").Trim();
                RebuildRows(preserveCurrentView: true);
            },
            196f);
        AuraUiStableId.Assign(search.gameObject, "toolbox.search");
        AuraToolsUi.AddButton(
            actionBar.transform,
            "数据目录",
            () => FileResourceUtil.OpenDirectory(
                AuraToolsExp.Dll.Config.AuraToolsConfigService.DataRootDirectory),
            92f);

        var summary = AuraToolsUi.CreateLayout("ToolboxSummary", panel);
        AuraToolsUi.SetFixedHeight(summary, 42f);
        var summaryLayout = summary.AddComponent<HorizontalLayoutGroup>();
        summaryLayout.spacing = 8f;
        summaryLayout.childControlWidth = true;
        summaryLayout.childControlHeight = true;
        summaryLayout.childForceExpandWidth = false;
        resultText = AuraToolsUi.AddText(
            summary.transform,
            "",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        listContent = AuraToolsUi.CreateScroll(panel, "ToolboxModules");
        listScroll = AuraUiViewState.ResolveScroll(listContent);
        reconciler = new AuraUiKeyedListReconciler<string, IAuraToolModule>(
            listContent,
            StringComparer.Ordinal,
            module => module.Descriptor.ModuleId,
            CreateModuleRow,
            UpdateModuleRow);

        emptyState = AuraToolsUi.CreateLayout("ToolboxEmpty", panel);
        AuraToolsUi.SetFixedHeight(emptyState, 80f);
        AuraToolsUi.AddText(
            emptyState.transform,
            "没有符合当前分类和搜索条件的工具。",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText,
            72f,
            1f);
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
        if (resultText != null)
        {
            resultText.text = CategoryLabel(SessionState.CategoryId)
                              + " · " + modules.Count + " 个工具"
                              + (string.IsNullOrWhiteSpace(SessionState.SearchText)
                                  ? ""
                                  : " · 搜索：" + SessionState.SearchText);
        }

        RefreshCategoryButtons();
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
        var search = SessionState.SearchText;
        return AuraToolModuleHost.Catalog.VisibleModules
            .Where(module => categoryId == "all"
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
        var view = row.AddComponent<ToolboxModuleRow>();
        view.Build(module);
        visibleRows[module.Descriptor.ModuleId] = view;
        return row;
    }

    private void UpdateModuleRow(GameObject row, IAuraToolModule module)
    {
        var view = row.GetComponent<ToolboxModuleRow>();
        if (view == null)
        {
            view = row.AddComponent<ToolboxModuleRow>();
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

    private void RefreshCategoryButtons()
    {
        foreach (var pair in categoryButtons)
        {
            var label = pair.Value.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.color = string.Equals(
                    pair.Key,
                    SessionState.CategoryId,
                    StringComparison.Ordinal)
                    ? AuraToolsUi.Accent
                    : AuraToolsUi.Text;
            }
        }
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

internal sealed class ToolboxModuleRow : MonoBehaviour
{
    private IAuraToolModule? module;
    private Image? background;
    private Text? titleText;
    private Text? descriptionText;
    private Text? statusText;
    private Toggle? toggle;
    private Button? settingsButton;
    private bool suppressToggle;

    public void Build(IAuraToolModule value)
    {
        module = value;
        AuraToolsUi.SetFixedHeight(gameObject, 102f);
        background = AuraToolsUi.AddImage(gameObject, AuraToolsUi.Row);
        var layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var marker = AuraToolsUi.CreateLayout("CategoryMarker", transform);
        AuraToolsUi.SetFixedSize(marker, 6f, 76f);
        AuraToolsUi.AddImage(marker, AuraToolsUi.Accent);

        var copy = AuraToolsUi.CreateLayout("Copy", transform);
        var copyElement = AuraToolsUi.EnsureLayoutElement(copy);
        copyElement.flexibleWidth = 1f;
        var copyLayout = copy.AddComponent<VerticalLayoutGroup>();
        copyLayout.spacing = 0f;
        copyLayout.childControlWidth = true;
        copyLayout.childControlHeight = true;
        copyLayout.childForceExpandWidth = true;
        copyLayout.childForceExpandHeight = false;
        titleText = AuraToolsUi.AddText(
            copy.transform,
            "",
            AuraToolsUi.ModuleTitleFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            40f,
            1f);
        descriptionText = AuraToolsUi.AddText(
            copy.transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            40f,
            1f);

        statusText = AuraToolsUi.AddText(
            transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            72f,
            0f,
            230f);

        settingsButton = AuraToolsUi.AddButton(
            transform,
            "设置",
            OpenSettings,
            82f);
        AuraUiStableId.Assign(
            settingsButton.gameObject,
            "toolbox.module." + value.Descriptor.ModuleId + ".settings");
        toggle = AuraToolsUi.AddToggle(transform, false, ToggleChanged);
        AuraUiStableId.Assign(
            toggle.gameObject,
            "toolbox.module." + value.Descriptor.ModuleId + ".toggle");
    }

    public void Bind(IAuraToolModule value, AuraToolModuleState state)
    {
        module = value;
        if (titleText != null)
        {
            titleText.text = value.Descriptor.DisplayName
                             + (value.Descriptor.Experimental ? "（实验）" : "");
        }
        if (descriptionText != null)
        {
            descriptionText.text = value.Descriptor.Description;
        }
        if (settingsButton != null)
        {
            settingsButton.gameObject.SetActive(value.Descriptor.HasSettingsPage);
        }
        Refresh(state);
    }

    public void Refresh(AuraToolModuleState state)
    {
        if (state == null)
        {
            return;
        }

        suppressToggle = true;
        toggle?.SetIsOnWithoutNotify(state.ConfiguredEnabled);
        suppressToggle = false;
        if (toggle != null)
        {
            toggle.interactable = state.Availability
                                  != AuraToolModuleAvailability.Unavailable
                                  && state.Availability
                                  != AuraToolModuleAvailability.Busy;
        }
        if (settingsButton != null)
        {
            settingsButton.interactable = state.Availability
                                          != AuraToolModuleAvailability.Unavailable;
        }
        if (statusText != null)
        {
            statusText.text = string.IsNullOrWhiteSpace(state.Attention)
                ? state.Summary
                : state.Summary + "\n" + state.Attention;
            statusText.color = !string.IsNullOrWhiteSpace(state.Attention)
                ? AuraToolsUi.WarningText
                : state.EffectiveEnabled
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText;
        }
        if (background != null)
        {
            background.color = state.EffectiveEnabled
                ? AuraToolsUi.ActiveRow
                : AuraToolsUi.Row;
        }
    }

    private void ToggleChanged(bool enabled)
    {
        if (suppressToggle || module == null)
        {
            return;
        }

        var result = AuraToolModuleHost.SetEnabled(
            module.Descriptor.ModuleId,
            enabled);
        var state = AuraToolModuleHost.RefreshState(module.Descriptor.ModuleId);
        if (!result.Success)
        {
            state.Attention = result.Message;
        }
        Refresh(state);
    }

    private void OpenSettings()
    {
        if (module != null)
        {
            ToolboxSettingsPageRouter.Open(module, transform);
        }
    }
}

internal static class ToolboxSettingsPageRouter
{
    private static IAuraToolSettingsPage? activePage;

    public static void Open(IAuraToolModule module, Transform source)
    {
        Close();
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
}
