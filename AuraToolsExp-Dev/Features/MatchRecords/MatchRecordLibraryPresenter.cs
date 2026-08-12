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
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords;

internal static class MatchRecordLibraryPresenter
{
    private const string OverlayName = "AuraToolsMatchRecordLibrary";
    private const string ReplayFailureOverlayName = "AuraToolsMatchReplayFailure";
    private const string AdventureCollection = "Adventures";
    private static Transform? host;
    private static Transform? body;
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
        host = parent;
        collection = MatchRecordCollections.Auto;
        Cursors.Clear();
        Cursors.Add(0);
        pageIndex = 0;
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
        var window = AuraToolsUi.CreateOverlay(
            OverlayName,
            parent,
            "对局记录",
            () => ResetState(),
            maxWidth: 1320f);
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

        AuraToolsUi.ClearChildren(body);
        if (collection == AdventureCollection)
        {
            BuildAdventureView();
            return;
        }

        MatchRecordPage page;
        try
        {
            var filtering = searchText.Length > 0 || resultFilter.Length > 0 || dateRangeDays > 0 || compatibleOnly;
            if (!filtering)
            {
                page = MatchRecordStorage.Database.LoadPage(collection, Cursors[pageIndex]);
            }
            else
            {
                var since = dateRangeDays <= 0 ? (DateTime?)null : DateTime.UtcNow.AddDays(-dateRangeDays);
                var filtered = MatchRecordStorage.Database.SearchRecords(collection, searchText, resultFilter, since)
                    .Where(item => !compatibleOnly || MatchReplayCompatibility.Evaluate(item).CanPlay)
                    .ToList();
                var offset = pageIndex * MatchRecordDatabase.DefaultPageSize;
                var items = filtered.Skip(offset).Take(MatchRecordDatabase.DefaultPageSize).ToList();
                var hasMore = offset + items.Count < filtered.Count;
                page = new MatchRecordPage(items, hasMore ? pageIndex + 2 : 0, hasMore, filtered.Count);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] library query failed: " + ex.Message);
            AuraToolsUi.AddText(body, "读取对局记录失败：" + ex.Message, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 52f, 1f);
            return;
        }

        var tabs = AuraToolsUi.CreateLayout("CollectionTabs", body);
        AuraToolsUi.SetFixedHeight(tabs, AuraToolsUi.ToolbarHeight);
        var tabsLayout = tabs.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 8f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = false;
        tabsLayout.childForceExpandHeight = false;
        AuraToolsUi.AddButton(tabs.transform, "自动记录 " + AuraToolsMatchRecordsRuntime.AutoRecordCount, () => SwitchCollection(MatchRecordCollections.Auto), 132f);
        AuraToolsUi.AddButton(tabs.transform, "收藏对局 " + AuraToolsMatchRecordsRuntime.FavoriteRecordCount, () => SwitchCollection(MatchRecordCollections.Favorite), 132f);
        AuraToolsUi.AddButton(tabs.transform, "冒险统计 " + AuraToolsDamageMeterRuntime.OutOfRunHistoryCount, () => SwitchCollection(AdventureCollection), 132f);
        AuraToolsUi.AddText(
            tabs.transform,
            collection == MatchRecordCollections.Auto
                ? "自动回放上限 " + AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit + " 场"
                : "收藏不受自动清理影响",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddText(tabs.transform, DatabaseSizeLabel(), AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 150f);
        AuraToolsUi.AddButton(tabs.transform, "导入目录", () => FileResourceUtil.OpenDirectory(MatchRecordStorage.ImportsDirectory), 92f);
        AuraToolsUi.AddButton(tabs.transform, "导入回放包", PickPackage, 104f);
        if (pendingImportPreview != null) AuraToolsUi.AddButton(tabs.transform, "确认导入", ConfirmImport, 92f);
        AuraToolsUi.AddButton(tabs.transform, "扫描目录", ImportPackages, 92f);
        if (SelectedIds.Count > 0) AuraToolsUi.AddButton(tabs.transform, "批量导出 " + SelectedIds.Count, ExportSelected, 104f);
        AuraToolsUi.AddButton(tabs.transform, clearArmed ? "确认清空" : "清空当前", ClearCurrent, 104f);

        var filters = AuraToolsUi.CreateLayout("LibraryFilters", body);
        AuraToolsUi.SetFixedHeight(filters, AuraToolsUi.ToolbarHeight);
        var filterLayout = filters.AddComponent<HorizontalLayoutGroup>();
        filterLayout.spacing = 8f;
        filterLayout.childControlWidth = true;
        filterLayout.childControlHeight = true;
        filterLayout.childForceExpandWidth = false;
        filterLayout.childForceExpandHeight = false;
        AuraToolsUi.AddText(filters.transform, "搜索", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 42f);
        AuraToolsUi.AddInput(filters.transform, searchText, value => SetSearch(value), 250f);
        AuraToolsUi.AddButton(filters.transform, ResultFilterLabel(), CycleResultFilter, 94f);
        AuraToolsUi.AddButton(filters.transform, DateFilterLabel(), CycleDateFilter, 94f);
        AuraToolsUi.AddButton(filters.transform, compatibleOnly ? "仅可回放" : "全部兼容性", () =>
        {
            compatibleOnly = !compatibleOnly;
            ResetPaging();
            Build();
        }, 104f);
        AuraToolsUi.AddButton(filters.transform, "选择本页", () =>
        {
            foreach (var item in page.Items) SelectedIds.Add(item.RecordId);
            Build();
        }, 92f);
        if (SelectedIds.Count > 0) AuraToolsUi.AddButton(filters.transform, "取消选择", () => { SelectedIds.Clear(); Build(); }, 92f);

        if (!string.IsNullOrWhiteSpace(message))
        {
            AuraToolsUi.AddText(body, message, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 44f, 1f);
        }


        if (pendingImportPreview != null)
        {
            var preview = pendingImportPreview;
            var dependencies = preview.ContentDependencies.Count == 0 ? "无额外内容依赖" : string.Join("、", preview.ContentDependencies.Take(4));
            AuraToolsUi.AddText(
                body,
                "导入预览：" + preview.LevelId + "   协议 v" + preview.ReplayProtocol + "   " + FormatBytes(preview.PackageBytes)
                + "   兼容性 " + preview.Compatibility + (preview.Duplicate ? "   检测到重复内容" : "")
                + "\n来源：" + Path.GetFileName(preview.Path) + "   内容依赖：" + dependencies
                + "   隐私：" + preview.PrivacySummary
                + (string.IsNullOrWhiteSpace(preview.Tags) ? "" : "   标签：" + preview.Tags)
                + (string.IsNullOrWhiteSpace(preview.Notes) ? "" : "   备注：" + preview.Notes),
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                preview.Duplicate ? AuraToolsUi.WarningText : AuraToolsUi.MutedText,
                72f,
                1f);
        }

        var scroll = AuraToolsUi.CreateScroll(body, "MatchRecordRows");
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
        previous.interactable = pageIndex > 0;
        AuraToolsUi.AddText(footer.transform, "第 " + (pageIndex + 1) + " 页，共 " + page.TotalCount + " 条", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        var next = AuraToolsUi.AddButton(footer.transform, "下一页", () => NextPage(page.NextCursor), 88f);
        next.interactable = page.HasMore;
    }

    private static void AddRecordRow(Transform parent, MatchRecord item)
    {
        var compatibility = MatchReplayCompatibility.Evaluate(item);
        var row = AuraToolsUi.CreateLayout("MatchRecord-" + item.RecordId, parent);
        AuraToolsUi.SetFixedHeight(row, 76f);
        AuraToolsUi.AddImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var title = (string.IsNullOrWhiteSpace(item.LevelId) ? "未知战斗" : item.LevelId)
                    + "   " + ResultLabel(item.Result)
                    + "   " + LocalTime(item.EndedUtc);
        var detail = "回合 " + item.TurnCount
                     + "   事件 " + item.EventCount
                     + "   DPT伤害 " + TotalDamage(item.StatisticsJson)
                     + "   " + FormatBytes(item.CompressedBytes)
                     + "   " + CompatibilityLabel(compatibility.Level)
                     + (string.IsNullOrWhiteSpace(item.Tags) ? "" : "   标签 " + item.Tags);
        AuraToolsUi.AddText(row.transform, title + "\n" + detail, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 60f, 1f);
        AuraToolsUi.AddButton(row.transform, SelectedIds.Contains(item.RecordId) ? "已选" : "选择", () => ToggleSelection(item.RecordId), 64f);
        AuraToolsUi.AddButton(row.transform, "分析", () => MatchAnalysisPresenter.Show(host!, item), 76f);
        var replayButton = AuraToolsUi.AddButton(
            row.transform,
            compatibility.CanPlay ? "回放" : "仅分析",
            () => Replay(item.RecordId),
            76f);
        replayButton.interactable = compatibility.CanPlay;
        AuraToolsUi.AddButton(row.transform, editingId == item.RecordId ? "收起" : "标签备注", () => EditMetadata(item), 82f);
        AuraToolsUi.AddButton(
            row.transform,
            item.Collection == MatchRecordCollections.Favorite ? "移回自动" : "收藏",
            () => Move(item),
            92f);
        AuraToolsUi.AddButton(row.transform, armedDeleteId == item.RecordId ? "确认删除" : "删除", () => Delete(item.RecordId), 86f);

        if (editingId == item.RecordId)
        {
            var editor = AuraToolsUi.CreateLayout("MetadataEditor-" + item.RecordId, parent);
            AuraToolsUi.SetFixedHeight(editor, AuraToolsUi.ToolbarHeight);
            AuraToolsUi.AddImage(editor, AuraToolsUi.Row);
            var editorLayout = editor.AddComponent<HorizontalLayoutGroup>();
            editorLayout.padding = new RectOffset(10, 10, 6, 6);
            editorLayout.spacing = 8f;
            editorLayout.childControlWidth = true;
            editorLayout.childControlHeight = true;
            editorLayout.childForceExpandWidth = false;
            AuraToolsUi.AddText(editor.transform, "标签", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 42f);
            AuraToolsUi.AddInput(editor.transform, editingTags, value => editingTags = value, 220f);
            AuraToolsUi.AddText(editor.transform, "备注", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 42f);
            AuraToolsUi.AddInput(editor.transform, editingNotes, value => editingNotes = value, 420f);
            AuraToolsUi.AddButton(editor.transform, "保存", SaveMetadata, 76f);
        }
    }

    private static void Replay(string recordId)
    {
        MatchReplayLaunchCoordinator.Start(
            recordId,
            0,
            () =>
            {
                AuraToolsUi.CloseOwnedOverlays("Match record replay launch");
                WitchUiManager.Instance?.CloseUI("SettingUI");
                ResetState();
            },
            result =>
            {
                message = result;
                MatchReplayFailurePresenter.Schedule("无法开始对局回放", result);
            });
    }

    private static void ShowReplayFailure(string detail)
    {
        if (host == null)
        {
            return;
        }

        var window = AuraToolsUi.CreateOverlay(
            ReplayFailureOverlayName,
            host,
            "无法开始对局回放",
            maxWidth: 680f);
        AuraToolsUi.AddText(
            window.transform,
            detail,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.WarningText,
            96f,
            1f);
    }

    private static string CompatibilityLabel(string level)
    {
        return level == MatchReplayCompatibilityLevels.Compatible
            ? "可回放"
            : level == MatchReplayCompatibilityLevels.Degraded
                ? "兼容回放"
                : "仅分析";
    }

    private static void SetSearch(string value)
    {
        searchText = (value ?? "").Trim();
        ResetPaging();
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
        Build();
    }

    private static void EditMetadata(MatchRecord item)
    {
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

        Build();
    }

    private static void SaveMetadata()
    {
        if (MatchRecordStorage.Database.UpdateMetadata(editingId, editingTags, editingNotes))
        {
            message = "标签和备注已保存。";
            editingId = "";
        }
        else
        {
            message = "标签和备注保存失败：记录不存在。";
        }

        Build();
    }

    private static void ExportSelected()
    {
        var exported = 0;
        var failed = new List<string>();
        foreach (var recordId in SelectedIds.ToList())
        {
            try
            {
                MatchReplayPackageService.Export(recordId);
                exported++;
            }
            catch (Exception ex)
            {
                failed.Add(ex.Message);
            }
        }

        message = "已批量导出 " + exported + " 条回放。";
        if (failed.Count > 0) message += " 失败 " + failed.Count + " 条：" + string.Join("；", failed.Take(2));
        SelectedIds.Clear();
        Build();
    }

    private static void Move(MatchRecord item)
    {
        var destination = item.Collection == MatchRecordCollections.Favorite
            ? MatchRecordCollections.Auto
            : MatchRecordCollections.Favorite;
        MatchRecordStorage.Database.SetCollection(item.RecordId, destination);
        if (destination == MatchRecordCollections.Auto)
        {
            MatchRecordStorage.Database.EnforceAutoLimit(
                AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit);
        }

        message = destination == MatchRecordCollections.Favorite ? "已移入收藏对局。" : "已移回自动记录。";
        Build();
    }

    private static void ImportPackages()
    {
        try
        {
            var files = Directory.GetFiles(MatchRecordStorage.ImportsDirectory, "*.aurareplay", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                message = "导入目录中没有回放包。";
            }
            else
            {
                pendingImportPath = files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First();
                pendingImportPreview = MatchReplayPackageService.Inspect(pendingImportPath);
                message = "导入目录中有 " + files.Length + " 个回放包；请先确认当前预览，再继续扫描下一条。";
            }
        }
        catch (Exception ex)
        {
            message = "导入失败：" + ex.Message;
        }

        Build();
    }

    private static void PickPackage()
    {
        OptionalFileDialog.PickFileAsync(
            "导入 AuraTools 对局回放",
            new[]
            {
                new OptionalFileDialogFilter("AuraTools 回放包", "*.aurareplay"),
                new OptionalFileDialogFilter("所有文件", "*.*")
            },
            "aurareplay",
            MatchRecordStorage.ImportsDirectory,
            result =>
            {
                if (result.Selected)
                {
                    try
                    {
                        pendingImportPreview = MatchReplayPackageService.Inspect(result.Path);
                        pendingImportPath = result.Path;
                        message = pendingImportPreview.CompatibilityMessage + " 请检查来源、体积、依赖和重复状态后确认导入。";
                    }
                    catch (Exception ex)
                    {
                        message = "导入失败：" + ex.Message;
                    }

                    Build();
                }
                else if (result.Status != OptionalFileDialogStatus.Cancelled)
                {
                    message = "文件选择器不可用，可将回放包放入导入目录后再打开本页面。";
                    Build();
                }
            });
    }

    private static void ConfirmImport()
    {
        if (pendingImportPreview == null || string.IsNullOrWhiteSpace(pendingImportPath)) return;
        try
        {
            var importedPath = pendingImportPath;
            MatchReplayPackageService.Import(importedPath);
            var archiveWarning = "";
            var inbox = Path.GetFullPath(MatchRecordStorage.ImportsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sourceDirectory = Path.GetFullPath(Path.GetDirectoryName(importedPath) ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(inbox, sourceDirectory, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var completed = Path.Combine(MatchRecordStorage.ImportsDirectory, "Imported");
                    Directory.CreateDirectory(completed);
                    File.Move(importedPath, UniqueLibraryPath(Path.Combine(completed, Path.GetFileName(importedPath))));
                }
                catch (Exception ex)
                {
                    archiveWarning = " 但源文件未能归档：" + ex.Message;
                }
            }
            collection = MatchRecordCollections.Favorite;
            pageIndex = 0;
            Cursors.Clear();
            Cursors.Add(0);
            pendingImportPath = "";
            pendingImportPreview = null;
            message = "回放包已原子写入收藏对局。" + archiveWarning;
        }
        catch (Exception ex)
        {
            message = "导入失败：" + ex.Message;
        }

        Build();
    }

    private static void Delete(string recordId)
    {
        if (!string.Equals(armedDeleteId, recordId, StringComparison.Ordinal))
        {
            armedDeleteId = recordId;
            message = "再次点击同一条记录的“确认删除”即可永久删除。";
            Build();
            return;
        }

        MatchRecordStorage.Database.Delete(recordId);
        SelectedIds.Remove(recordId);
        armedDeleteId = "";
        message = "对局记录已删除。";
        Build();
    }

    private static void ClearCurrent()
    {
        if (!clearArmed)
        {
            clearArmed = true;
            message = "再次点击“确认清空”将删除当前分类中的全部回放。";
            Build();
            return;
        }

        var removed = collection == AdventureCollection
            ? DamageHistoryStorage.Database.ClearAdventures()
            : MatchRecordStorage.Database.Clear(collection);
        clearArmed = false;
        SelectedIds.Clear();
        pageIndex = 0;
        Cursors.Clear();
        Cursors.Add(0);
        message = collection == AdventureCollection
            ? "已清空 " + removed + " 条冒险统计。"
            : "已清空 " + removed + " 条对局记录。";
        Build();
    }

    private static void BuildAdventureView()
    {
        if (body == null) return;
        DamageHistoryPage<OutOfRunDamageHistoryRecord> page;
        try
        {
            page = DamageHistoryStorage.Database.LoadAdventurePage(Cursors[pageIndex], DamageHistoryDatabase.DefaultPageSize);
        }
        catch (Exception ex)
        {
            AuraToolsUi.AddText(body, "读取冒险统计失败：" + ex.Message, AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 52f, 1f);
            return;
        }

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
        previous.interactable = pageIndex > 0;
        AuraToolsUi.AddText(footer.transform, "第 " + (pageIndex + 1) + " 页，共 " + page.TotalCount + " 条",
            AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        var next = AuraToolsUi.AddButton(footer.transform, "下一页", () => NextPage(page.NextCursor), 88f);
        next.interactable = page.HasMore;
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
            var path = MatchRecordStorage.Database.DatabasePath;
            return File.Exists(path) ? "数据库 " + FormatBytes(new FileInfo(path).Length) : "数据库 0 B";
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
        host = null;
        body = null;
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
