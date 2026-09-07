using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.MatchRecords;

internal static partial class MatchRecordLibraryPresenter
{
    private const string OverlayName = "AuraToolsMatchRecordLibrary";
    private const string AdventureCollection = "Adventures";
    private static Transform? host;
    private static Transform? body;
    private static Transform? recordScrollContent;
    private static MatchRecordLibraryScrollState? pendingScrollRestore;
    private static string collection = MatchRecordCollections.Auto;
    private static readonly List<long> Cursors = new() { 0 };
    private static int pageIndex;
    private static string message = "";
    private static string armedDeleteId = "";
    private static bool clearArmed;
    private static string pendingImportPath = "";
    private static MatchReplayImportPreview? pendingImportPreview;
    private static string searchText = "";
    private static string resultFilter = "";
    private static int dateRangeDays;
    private static bool compatibleOnly;
    private static readonly HashSet<string> SelectedIds = new(StringComparer.Ordinal);
    private static string editingId = "";
    private static string editingTags = "";
    private static string editingNotes = "";

    internal static void Show(Transform parent)
    {
        Show(parent, null, "");
    }

    internal static void Show(
        Transform parent,
        MatchRecordLibraryViewState? returnState,
        string returnMessage)
    {
        ResetQueryState();
        var viewGeneration = queryGeneration;
        host = parent;
        RestoreState(returnState);
        message = returnMessage ?? "";
        armedDeleteId = "";
        clearArmed = false;
        pendingImportPath = "";
        pendingImportPreview = null;
        var window = AuraToolsUi.CreateOverlay(
            OverlayName,
            parent,
            "对局记录",
            () => { if (viewGeneration == queryGeneration) ResetState(); },
            maxWidth: 1120f);
        body = AuraToolsUi.CreateLayout("MatchRecordBody", window.transform).transform;
        var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        AuraToolsUi.EnsureLayoutElement(body.gameObject).flexibleHeight = 1f;
        Build();
    }

    private static void Build()
    {
        if (body == null)
        {
            return;
        }

        if (collection == AdventureCollection)
        {
            if (!EnsureQuery()) return;
            var adventureScroll = pendingScrollRestore ?? CaptureScrollState();
            pendingScrollRestore = null;
            AuraToolsUi.ClearChildren(body);
            BuildAdventureView(adventureScroll);
            return;
        }
        if (!EnsureQuery() || cachedPage == null) return;
        var page = cachedPage;
        var viewState = pendingScrollRestore ?? CaptureScrollState();
        pendingScrollRestore = null;
        recordScrollContent = null;
        var hadSearchFocus = searchField != null && searchField.isFocused;
        var caret = searchField?.caretPosition ?? 0;
        SelectionViews.Clear();
        RecordRows.Clear();
        AuraToolsUi.ClearChildren(body);
        var tabs = AuraToolsUi.CreateLayout("CollectionTabs", body);
        AuraToolsUi.SetFixedHeight(tabs, AuraToolsUi.ToolbarHeight);
        var tabsLayout = tabs.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 8f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = false;
        tabsLayout.childForceExpandHeight = false;
        AddCompactButton(tabs.transform, "自动记录 " + AuraToolsMatchRecordsRuntime.AutoRecordCount, () => SwitchCollection(MatchRecordCollections.Auto), 112f);
        AddCompactButton(tabs.transform, "收藏对局 " + AuraToolsMatchRecordsRuntime.FavoriteRecordCount, () => SwitchCollection(MatchRecordCollections.Favorite), 112f);
        AddCompactButton(tabs.transform, "冒险统计 " + MatchRecordStorage.AdventureCount, () => SwitchCollection(AdventureCollection), 112f);
        AuraToolsUi.AddText(
            tabs.transform,
            collection == MatchRecordCollections.Auto
                ? "上限 " + AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit + " 场 · " + DatabaseSizeLabel()
                : "永久收藏 · " + DatabaseSizeLabel(),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AddCompactButton(
            tabs.transform,
            pendingImportPreview == null ? "导入回放" : "确认导入",
            pendingImportPreview == null ? PickPackage : ConfirmImport,
            104f);
        ToolboxIconButtonV2.Create(
            tabs.transform,
            "action.folder",
            "打开导入目录",
            () => FileResourceUtil.OpenDirectory(MatchRecordStorage.ImportsDirectory),
            42f,
            "夹");
        ToolboxIconButtonV2.Create(
            tabs.transform,
            "action.search",
            "扫描导入目录",
            ImportPackages,
            42f,
            "扫");
        ToolboxIconButtonV2.Create(
            tabs.transform,
            "action.clear",
            clearArmed ? "再次点击，确认清空当前分类" : "清空当前分类",
            ClearCurrent,
            42f,
            clearArmed ? "!" : "清");

        var filters = AuraToolsUi.CreateLayout("LibraryFilters", body);
        AuraToolsUi.SetFixedHeight(filters, AuraToolsUi.ToolbarHeight);
        var filterLayout = filters.AddComponent<HorizontalLayoutGroup>();
        filterLayout.spacing = 8f;
        filterLayout.childControlWidth = true;
        filterLayout.childControlHeight = true;
        filterLayout.childForceExpandWidth = false;
        filterLayout.childForceExpandHeight = false;
        searchField = AddFlexibleInput(filters.transform, searchText, "搜索关卡、标签或备注…", value => SetSearch(value));
        AddCompactButton(filters.transform, ResultFilterLabel(), CycleResultFilter, 88f);
        AddCompactButton(filters.transform, DateFilterLabel(), CycleDateFilter, 88f);
        AddCompactButton(filters.transform, compatibleOnly ? "仅可回放" : "全部兼容", () =>
        {
            compatibleOnly = !compatibleOnly;
            ResetPaging();
            Build();
        }, 96f);
        ToolboxIconButtonV2.Create(filters.transform, "selection.all", "选择本页", () =>
        {
            foreach (var item in page.Items) SelectedIds.Add(item.RecordId);
            UpdateSelectionUi();
        }, 42f, "全");
            selectionExportButton = AddCompactButton(filters.transform, "导出已选", ExportSelected, 112f);
            ToolboxIconButtonV2.Create(
                filters.transform,
                "action.clear",
                "取消全部选择",
                () => { SelectedIds.Clear(); UpdateSelectionUi(); },
                42f,
                "消");
        statusLabel = AuraToolsUi.AddText(body, message, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 44f, 1f);
        statusLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));


        if (pendingImportPreview != null)
        {
            var preview = pendingImportPreview;
            AuraToolsUi.AddText(
                body,
                "导入预览：" + AuraToolsPlayerDisplay.LevelName(preview.LevelId)
                + " · " + FormatBytes(preview.PackageBytes)
                + (preview.Duplicate ? " · 已在资料库中" : " · 可以导入")
                + "\n" + Path.GetFileName(preview.Path),
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                preview.Duplicate ? AuraToolsUi.WarningText : AuraToolsUi.MutedText,
                72f,
                1f);
        }

        var scroll = AuraToolsUi.CreateScroll(body, "MatchRecordRows");
        recordScrollContent = scroll;
        if (page.Items.Count == 0)
        {
            AuraToolsUi.AddText(scroll, "这里还没有可回放的对局。", AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 72f, 1f);
        }
        else
        {
            foreach (var item in page.Items)
            {
                AddRecordRow(scroll, item);
            }
        }

        var footer = AuraToolsUi.CreateLayout("Paging", body);
        AuraToolsUi.SetFixedHeight(footer, AuraToolsUi.FooterHeight);
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 8f;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;
        var previous = AuraToolsUi.AddButton(footer.transform, "上一页", PreviousPage, 88f);
        AuraToolsUi.SetButtonAvailable(previous, pageIndex > 0, "已经是第一页");
        AuraToolsUi.AddText(footer.transform, "第 " + (pageIndex + 1) + " 页，共 " + page.TotalCount + " 条", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        var next = AuraToolsUi.AddButton(footer.transform, "下一页", () => NextPage(page.NextCursor), 88f);
        AuraToolsUi.SetButtonAvailable(next, page.HasMore, "已经是最后一页");
        RestoreScrollState(scroll, viewState);
        UpdateSelectionUi();
        if (hadSearchFocus && searchField != null)
        {
            searchField.ActivateInputField();
            searchField.caretPosition = Math.Min(caret, searchField.text.Length);
        }
    }

    private static void AddRecordRow(Transform parent, MatchRecord item)
    {
        var unit = AuraToolsUi.CreateLayout("RecordUnit-" + item.RecordId, parent).transform;
        var layout = unit.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        RecordRows[item.RecordId] = (unit, item);
        AddRecordContents(unit, item);
    }

    private static void AddRecordContents(Transform parent, MatchRecord item)
    {
        var canPlay = CanPlayV17(item);
        var row = AuraToolsUi.CreateLayout("MatchRecord-" + item.RecordId, parent);
        AuraUiStableId.Assign(row, "match-record." + item.RecordId);
        AuraToolsUi.SetFixedHeight(row, 72f);
        AuraToolsUi.AddImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var title = (string.IsNullOrWhiteSpace(item.BattleTitle)
                        ? AuraToolsPlayerDisplay.LevelName(item.LevelId)
                        : item.BattleTitle)
                    + "   " + ResultLabel(item.Result)
                    + "   " + LocalTime(item.EndedUtc);
        var detail = "回合 " + item.TurnCount
                     + "   事件 " + item.EventCount
                     + "   DPT伤害 " + TotalDamage(item.StatisticsJson)
                     + "   " + FormatBytes(item.CompressedBytes)
                     + "   " + ReplayAvailabilityLabel(item)
                     + (string.IsNullOrWhiteSpace(item.Tags) ? "" : "   标签 " + item.Tags);
        SelectionViews[item.RecordId] = ToolboxCheckboxV2.Create(
            row.transform,
            SelectedIds.Contains(item.RecordId),
            _ => ToggleSelection(item.RecordId),
            30f);
        AuraToolsUi.AddText(row.transform, title + "\n" + detail, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 56f, 1f);
        ToolboxIconButtonV2.Create(
            row.transform,
            "record.favorite",
            item.Collection == MatchRecordCollections.Favorite ? "移回自动记录" : "收藏此对局",
            () => Move(item),
            42f,
            item.Collection == MatchRecordCollections.Favorite ? "★" : "☆");
        AddCompactButton(row.transform, "分析", () => MatchAnalysisPresenter.Show(host!, item), 68f);
        var replayButton = AddCompactButton(
            row.transform,
            canPlay ? "回放" : "仅分析",
            () => Replay(item.RecordId),
            68f);
        AuraToolsUi.SetButtonAvailable(
            replayButton,
            canPlay,
            ReplayAvailabilityDetail(item));
        ToolboxIconButtonV2.Create(
            row.transform,
            "record.more",
            editingId == item.RecordId ? "收起更多操作" : "标签、备注与删除",
            () => EditMetadata(item),
            42f,
            editingId == item.RecordId ? "⌃" : "⋯");

        if (editingId == item.RecordId)
        {
            var editor = AuraToolsUi.CreateLayout("MetadataEditor-" + item.RecordId, parent);
            AuraToolsUi.SetFixedHeight(editor, 104f);
            ToolboxSurfaceV2.ApplyControl(editor).raycastTarget = false;
            var editorLayout = editor.AddComponent<VerticalLayoutGroup>();
            editorLayout.padding = new RectOffset(12, 12, 6, 6);
            editorLayout.spacing = 4f;
            editorLayout.childControlWidth = true;
            editorLayout.childControlHeight = true;
            editorLayout.childForceExpandWidth = true;
            editorLayout.childForceExpandHeight = false;

            var fields = AuraToolsUi.CreateLayout("MetadataFields", editor.transform);
            AuraToolsUi.SetFixedHeight(fields, 44f);
            var fieldLayout = fields.AddComponent<HorizontalLayoutGroup>();
            fieldLayout.spacing = 8f;
            fieldLayout.childControlWidth = true;
            fieldLayout.childControlHeight = true;
            fieldLayout.childForceExpandWidth = false;
            fieldLayout.childForceExpandHeight = false;
            AddFlexibleInput(fields.transform, editingTags, "标签（用逗号分隔）", value => editingTags = value, 180f);
            AddFlexibleInput(fields.transform, editingNotes, "备注", value => editingNotes = value, 260f);
            AddCompactButton(fields.transform, "保存", SaveMetadata, 68f);

            var actions = AuraToolsUi.CreateLayout("SecondaryActions", editor.transform);
            AuraToolsUi.SetFixedHeight(actions, 42f);
            var actionLayout = actions.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 8f;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = false;
            actionLayout.childForceExpandHeight = false;
            AuraToolsUi.AddText(actions.transform, "次要操作", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 40f, 1f);
            AddCompactButton(
                actions.transform,
                item.Collection == MatchRecordCollections.Favorite ? "移回自动记录" : "移入收藏",
                () => Move(item),
                112f,
                42f);
            AddCompactButton(
                actions.transform,
                armedDeleteId == item.RecordId ? "确认删除" : "删除记录",
                () => Delete(item.RecordId),
                92f,
                42f);
        }
    }

    private static Button AddCompactButton(
        Transform parent,
        string label,
        Action action,
        float width,
        float height = AuraToolsUi.ButtonHeight)
    {
        var button = AuraToolsUi.AddButton(parent, label, action, width, height);
        AuraToolsUi.SetFixedSize(button.gameObject, width, height);
        ToolboxSurfaceV2.ApplyControl(button.gameObject);
        return button;
    }

    private static InputField AddFlexibleInput(
        Transform parent,
        string value,
        string placeholder,
        Action<string> changed,
        float minimumWidth = 220f)
    {
        var input = AuraToolsUi.AddInput(parent, value, changed, minimumWidth, 42f);
        var element = AuraToolsUi.EnsureLayoutElement(input.gameObject);
        element.minWidth = minimumWidth;
        element.preferredWidth = minimumWidth;
        element.minHeight = 42f;
        element.preferredHeight = 42f;
        element.flexibleWidth = 1f;
        ToolboxSurfaceV2.ApplyControl(input.gameObject);
        var placeholderText = input.placeholder as Text;
        if (placeholderText != null)
        {
            placeholderText.text = placeholder;
        }
        return input;
    }

    private static void Replay(string recordId)
    {
        message = "正在读取回放数据…";
        RefreshStatus();
        var returnState = CaptureReturnState(recordId);
        MatchReplayLaunchCoordinator.Start(
            recordId,
            0,
            returnState,
            detail =>
            {
                message = detail;
                Build();
            });
    }

    private static bool CanPlayV17(MatchRecord item)
    {
        return item.ReplayProtocol == MatchReplayProtocol.Version
               && string.Equals(item.ReplayState, MatchReplayStates.Ready, StringComparison.Ordinal);
    }

    private static string ReplayAvailabilityLabel(MatchRecord item)
    {
        if (CanPlayV17(item)) return "v17 可回放";
        if (string.Equals(item.ReplayState, MatchReplayStates.Rejected, StringComparison.OrdinalIgnoreCase))
            return "记录已拒绝，仅保留摘要";
        if (string.Equals(item.ReplayState, MatchReplayStates.SummaryOnly, StringComparison.OrdinalIgnoreCase))
            return item.CaptureDiagnostics.Count > 0 ? "回放捕获失败" : "仅保留对局摘要";
        if (string.Equals(item.ReplayState, MatchReplayStates.Corrupt, StringComparison.OrdinalIgnoreCase))
            return "记录已损坏";
        return "回放未完成，仅可分析";
    }

    private static string ReplayAvailabilityDetail(MatchRecord item)
    {
        var label = ReplayAvailabilityLabel(item);
        return item.CaptureDiagnostics.Count == 0
            ? label
            : label + "；诊断草稿已保留：" + string.Join("；", item.CaptureDiagnostics);
    }

    private static void SetSearch(string value)
    {
        searchText = (value ?? "").Trim();
        ResetPaging();
        queryDue = Time.unscaledTime + 0.15f;
        Build();
    }

    private static void CycleResultFilter()
    {
        resultFilter = resultFilter.Length == 0 ? "win" : resultFilter == "win" ? "loss" : "";
        ResetPaging();
        Build();
    }

    private static string ResultFilterLabel()
    {
        return resultFilter == "win" ? "仅胜利" : resultFilter == "loss" ? "仅失败" : "全部结果";
    }

    private static void CycleDateFilter()
    {
        dateRangeDays = dateRangeDays == 0 ? 7 : dateRangeDays == 7 ? 30 : 0;
        ResetPaging();
        Build();
    }

    private static string DateFilterLabel()
    {
        return dateRangeDays == 7 ? "最近7天" : dateRangeDays == 30 ? "最近30天" : "全部日期";
    }

    private static void ToggleSelection(string recordId)
    {
        if (!SelectedIds.Add(recordId)) SelectedIds.Remove(recordId);
        UpdateSelectionUi();
    }

    private static void EditMetadata(MatchRecord item)
    {
        var previous = editingId;
        if (editingId == item.RecordId)
        {
            editingId = "";
        }
        else
        {
            editingId = item.RecordId;
            editingTags = item.Tags;
            editingNotes = item.Notes;
        }

        RefreshRecordRow(previous);
        RefreshRecordRow(item.RecordId);
    }

    private static void BuildAdventureView(MatchRecordLibraryScrollState? viewState)
    {
        if (body == null) return;
        var page = cachedAdventurePage;
        if (page == null) return;

        var tabs = AuraToolsUi.CreateLayout("CollectionTabs", body);
        AuraToolsUi.SetFixedHeight(tabs, AuraToolsUi.ToolbarHeight);
        var layout = tabs.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        AuraToolsUi.AddButton(tabs.transform, "自动记录 " + AuraToolsMatchRecordsRuntime.AutoRecordCount, () => SwitchCollection(MatchRecordCollections.Auto), 132f);
        AuraToolsUi.AddButton(tabs.transform, "收藏对局 " + AuraToolsMatchRecordsRuntime.FavoriteRecordCount, () => SwitchCollection(MatchRecordCollections.Favorite), 132f);
        AuraToolsUi.AddButton(tabs.transform, "冒险统计 " + page.TotalCount, () => SwitchCollection(AdventureCollection), 132f);
        AuraToolsUi.AddText(tabs.transform, "DPT 冒险结算与完整对局共用一个资料库入口", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(tabs.transform, clearArmed ? "确认清空" : "清空冒险统计", ClearCurrent, 112f);

        if (!string.IsNullOrWhiteSpace(message))
        {
            AuraToolsUi.AddText(body, message, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 44f, 1f);
        }

        var scroll = AuraToolsUi.CreateScroll(body, "AdventureRows");
        recordScrollContent = scroll;
        if (page.Items.Count == 0)
        {
            AuraToolsUi.AddText(scroll, "这里还没有冒险结算统计。", AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 72f, 1f);
        }
        else
        {
            DamageHistoryWindowRenderer.RenderOutOfRunHeader(scroll);
            foreach (var item in page.Items)
            {
                DamageHistoryWindowRenderer.RenderOutOfRunRow(scroll, item, _ =>
                {
                    message = "冒险统计已删除。";
                    Build();
                });
            }
        }

        var footer = AuraToolsUi.CreateLayout("Paging", body);
        AuraToolsUi.SetFixedHeight(footer, AuraToolsUi.FooterHeight);
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 8f;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;
        var previous = AuraToolsUi.AddButton(footer.transform, "上一页", PreviousPage, 88f);
        AuraToolsUi.SetButtonAvailable(previous, pageIndex > 0, "已经是第一页");
        AuraToolsUi.AddText(footer.transform, "第 " + (pageIndex + 1) + " 页，共 " + page.TotalCount + " 条",
            AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        var next = AuraToolsUi.AddButton(footer.transform, "下一页", () => NextPage(page.NextCursor), 88f);
        AuraToolsUi.SetButtonAvailable(next, page.HasMore, "已经是最后一页");
        RestoreScrollState(scroll, viewState);
    }

    internal static MatchRecordLibraryViewState CaptureReturnState(string focusRecordId)
    {
        if (host == null || body == null)
        {
            return new MatchRecordLibraryViewState
            {
                FocusRecordId = focusRecordId ?? "",
                Scroll = new MatchRecordLibraryScrollState
                {
                    AnchorId = string.IsNullOrWhiteSpace(focusRecordId)
                        ? ""
                        : "match-record." + focusRecordId
                }
            }.CloneNormalized();
        }

        return new MatchRecordLibraryViewState
        {
            Collection = collection,
            Cursors = Cursors.ToList(),
            PageIndex = pageIndex,
            SearchText = searchText,
            ResultFilter = resultFilter,
            DateRangeDays = dateRangeDays,
            CompatibleOnly = compatibleOnly,
            SelectedIds = new HashSet<string>(SelectedIds, StringComparer.Ordinal),
            EditingId = editingId,
            EditingTags = editingTags,
            EditingNotes = editingNotes,
            FocusRecordId = focusRecordId ?? "",
            Scroll = CaptureScrollState()
        }.CloneNormalized();
    }

    private static void RestoreState(MatchRecordLibraryViewState? returnState)
    {
        var state = (returnState ?? new MatchRecordLibraryViewState()).CloneNormalized();
        collection = state.Collection;
        Cursors.Clear();
        Cursors.AddRange(state.Cursors);
        pageIndex = state.PageIndex;
        searchText = state.SearchText;
        resultFilter = state.ResultFilter;
        dateRangeDays = state.DateRangeDays;
        compatibleOnly = state.CompatibleOnly;
        SelectedIds.Clear();
        foreach (var selectedId in state.SelectedIds)
        {
            SelectedIds.Add(selectedId);
        }
        editingId = state.EditingId;
        editingTags = state.EditingTags;
        editingNotes = state.EditingNotes;
        pendingScrollRestore = state.Scroll?.Clone();
        if (pendingScrollRestore != null
            && !string.IsNullOrWhiteSpace(state.FocusRecordId))
        {
            pendingScrollRestore.AnchorId = "match-record." + state.FocusRecordId;
        }
    }

    private static MatchRecordLibraryScrollState? CaptureScrollState()
    {
        if (recordScrollContent == null)
        {
            return null;
        }

        var snapshot = AuraUiViewState.CaptureForContent(recordScrollContent);
        return snapshot == null
            ? null
            : new MatchRecordLibraryScrollState
            {
                FocusedId = snapshot.FocusedId,
                AnchorId = snapshot.AnchorId,
                AnchorOffsetY = snapshot.AnchorOffsetY,
                NormalizedFallback = snapshot.NormalizedFallback
            };
    }

    private static void RestoreScrollState(
        Transform content,
        MatchRecordLibraryScrollState? state)
    {
        if (state == null)
        {
            return;
        }

        AuraUiViewState.RestoreAfterLayout(
            content,
            new AuraUiViewStateSnapshot
            {
                FocusedId = state.FocusedId,
                AnchorId = state.AnchorId,
                AnchorOffsetY = state.AnchorOffsetY,
                NormalizedFallback = state.NormalizedFallback
            },
            "AuraTools.MatchRecords.Return");
    }

    private static void SwitchCollection(string value)
    {
        collection = value;
        pageIndex = 0;
        Cursors.Clear();
        Cursors.Add(0);
        message = "";
        armedDeleteId = "";
        clearArmed = false;
        Build();
    }

    private static void ResetPaging()
    {
        pageIndex = 0;
        Cursors.Clear();
        Cursors.Add(0);
    }

    private static void NextPage(long cursor)
    {
        if (cursor <= 0)
        {
            return;
        }

        if (pageIndex + 1 >= Cursors.Count)
        {
            Cursors.Add(cursor);
        }

        pageIndex++;
        Build();
    }

    private static void PreviousPage()
    {
        if (pageIndex > 0)
        {
            pageIndex--;
            Build();
        }
    }

    private static long TotalDamage(string json)
    {
        try
        {
            return (AuraSharedJson.Deserialize<DamageMeterSnapshot>(json)?.Combatants ?? new List<CombatantDamageStat>())
                .Where(item => item != null)
                .Sum(item => Math.Max(0, item.TotalHpDamage) + Math.Max(0, item.TotalShieldDamage));
        }
        catch
        {
            return 0;
        }
    }

    private static string ResultLabel(string value)
    {
        return value.IndexOf("win", StringComparison.OrdinalIgnoreCase) >= 0 ? "胜利"
            : value.IndexOf("loss", StringComparison.OrdinalIgnoreCase) >= 0 ? "失败"
            : value.IndexOf("escape", StringComparison.OrdinalIgnoreCase) >= 0 ? "撤退"
            : value.IndexOf("restart", StringComparison.OrdinalIgnoreCase) >= 0 ? "重开"
            : value;
    }

    private static string LocalTime(string value)
    {
        return DateTime.TryParse(value, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : value;
    }

    private static string DatabaseSizeLabel()
    {
        try
        {
            return "数据库 " + FormatBytes(MatchRecordStorage.DatabaseBytes);
        }
        catch
        {
            return "";
        }
    }

    private static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024L)
        {
            return (value / (1024d * 1024d)).ToString("0.0") + " MB";
        }

        return value >= 1024L ? (value / 1024d).ToString("0.0") + " KB" : value + " B";
    }

    private static string UniqueLibraryPath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension);
    }

    private static void ResetState()
    {
        ResetQueryState();
        host = null;
        body = null;
        recordScrollContent = null;
        pendingScrollRestore = null;
        message = "";
        armedDeleteId = "";
        clearArmed = false;
        pendingImportPath = "";
        pendingImportPreview = null;
        searchText = "";
        resultFilter = "";
        dateRangeDays = 0;
        compatibleOnly = false;
        SelectedIds.Clear();
        editingId = "";
        editingTags = "";
        editingNotes = "";
    }
}
