using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords;

internal static partial class MatchRecordLibraryPresenter
{
    private static MatchRecordPage? cachedPage;
    private static DamageHistoryPage<OutOfRunDamageHistoryRecord>? cachedAdventurePage;
    private static string cachedQuery = "";
    private static bool queryRunning;
    private static bool operationRunning;
    private static long queryGeneration;
    private static long queryTaskVersion;
    private static long operationVersion;
    private static float queryDue;
    private static UnityEngine.UI.InputField? searchField;
    private static UnityEngine.UI.Text? statusLabel;
    private static UnityEngine.UI.Button? selectionExportButton;
    private static readonly Dictionary<string, ToolboxCheckboxV2> SelectionViews = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (Transform Root, MatchRecord Data)> RecordRows = new(StringComparer.Ordinal);

    private static void UpdateSelectionUi()
    {
        foreach (var pair in SelectionViews)
            if (pair.Value != null) pair.Value.SetValueWithoutNotify(SelectedIds.Contains(pair.Key));
        if (selectionExportButton != null)
            AuraToolsUi.SetButtonAvailable(selectionExportButton, SelectedIds.Count > 0, "请先选择对局");
    }

    private static void RefreshRecordRow(string id)
    {
        if (!RecordRows.TryGetValue(id, out var row) || row.Root == null) return;
        AuraToolsUi.ClearChildren(row.Root);
        AddRecordContents(row.Root, row.Data);
    }

    private static void ResetQueryState()
    {
        queryGeneration++; queryTaskVersion++; operationVersion++;
        queryRunning = false; operationRunning = false; cachedQuery = "";
        cachedPage = null; cachedAdventurePage = null;
        searchField = null; statusLabel = null; selectionExportButton = null;
        SelectionViews.Clear(); RecordRows.Clear();
    }

    private static string QueryKey() => collection + ":" + searchText.Length + ":" + searchText + ":"
        + resultFilter + ":" + dateRangeDays + ":" + compatibleOnly + ":" + pageIndex + ":" + Cursors[pageIndex];

    private static bool EnsureQuery()
    {
        if (cachedQuery == QueryKey()) return true;
        if (body != null && body.childCount == 0)
            statusLabel = AuraToolsUi.AddText(body, "正在读取对局资料…", AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 44f, 1f);
        PumpQuery();
        return false;
    }

    internal static void PumpQuery()
    {
        if (body == null || queryRunning || operationRunning || Time.unscaledTime < queryDue) return;
        if (!MatchRecordStorage.Ready)
        {
            if (statusLabel != null) statusLabel.text = MatchRecordStorage.Status;
            return;
        }
        var key = QueryKey();
        if (key == cachedQuery) return;
        var generation = queryGeneration;
        var selectedCollection = collection;
        var queryText = searchText;
        var result = resultFilter;
        var onlyCompatible = compatibleOnly;
        var days = dateRangeDays;
        var index = pageIndex;
        var cursor = Cursors[pageIndex];
        queryRunning = true;
        var taskVersion = ++queryTaskVersion;
        if (!ReplayBackgroundWork.Storage.TryEnqueue("LibraryQuery", () =>
        {
            if (selectedCollection == AdventureCollection)
                return new LibraryQueryResult { Adventures = DamageHistoryStorage.Database.LoadAdventurePage(cursor, DamageHistoryDatabase.DefaultPageSize) };
            var database = MatchRecordStorage.Database;
            if (queryText.Length == 0 && result.Length == 0 && days == 0 && !onlyCompatible)
                return new LibraryQueryResult { Page = database.LoadPage(selectedCollection, cursor) };
            var since = days <= 0 ? (DateTime?)null : DateTime.UtcNow.AddDays(-days);
            var records = database.SearchRecords(selectedCollection, queryText, result, since)
                .Where(item => !onlyCompatible || CanPlayV17(item)).ToList();
            var offset = index * MatchRecordDatabase.DefaultPageSize;
            var items = records.Skip(offset).Take(MatchRecordDatabase.DefaultPageSize).ToList();
            var more = offset + items.Count < records.Count;
            return new LibraryQueryResult { Page = new MatchRecordPage(items, more ? index + 2 : 0, more, records.Count) };
        }, loaded =>
        {
            if (taskVersion != queryTaskVersion) return;
            queryRunning = false;
            if (body == null || generation != queryGeneration || key != QueryKey()) return;
            cachedPage = loaded.Page; cachedAdventurePage = loaded.Adventures; cachedQuery = key;
            Build();
        }, ex =>
        {
            if (taskVersion != queryTaskVersion) return;
            queryRunning = false;
            message = "读取对局资料失败：" + ex.Message;
            queryDue = Time.unscaledTime + 2f;
            RefreshStatus();
        }, 512 * 1024)) queryRunning = false;
    }

    private static void RunLibraryOperation<T>(string source, Func<T> work, Action<T> apply)
    {
        if (operationRunning) { message = "已有资料操作正在执行，请稍候。"; RefreshStatus(); return; }
        var generation = queryGeneration;
        operationRunning = true;
        var version = ++operationVersion;
        message = "正在处理对局资料…"; RefreshStatus();
        ReplayBackgroundWork.Storage.Enqueue(source, work, result =>
        {
            if (version != operationVersion) return;
            operationRunning = false;
            cachedQuery = "";
            if (body == null || generation != queryGeneration) return;
            apply(result); Build();
        }, ex =>
        {
            if (version != operationVersion) return;
            operationRunning = false;
            if (body == null || generation != queryGeneration) return;
            message = "操作未完成：" + ex.Message; RefreshStatus();
        });
    }

    private static void RefreshStatus()
    {
        if (body == null) return;
        if (statusLabel == null)
            statusLabel = AuraToolsUi.AddText(body, message, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 44f, 1f);
        else statusLabel.text = message;
        statusLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
    }

    private sealed class LibraryQueryResult
    {
        internal MatchRecordPage? Page;
        internal DamageHistoryPage<OutOfRunDamageHistoryRecord>? Adventures;
    }
}
