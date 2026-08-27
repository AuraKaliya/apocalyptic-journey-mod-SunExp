using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.AdventureArchive;

internal static class AdventureArchivePage
{
    private static Transform? content;
    private static Transform? overlayRoot;
    private static Text? status;
    private static InputField? maximumInput;

    internal static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay("AuraTools.AdventureArchive", parent, "冒险历程", Refresh);
        overlayRoot = window.transform;
        var toolbar = Row(window.transform, "Toolbar", AuraToolsUi.ToolbarHeight, section: true);
        AuraToolsUi.AddText(toolbar.transform, "保留记录", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 76f);
        maximumInput = AuraToolsUi.AddInput(toolbar.transform,
            AuraToolsConfigService.AdventureArchive.MaximumAdventures.ToString(CultureInfo.InvariantCulture),
            _ => { }, 72f, AuraToolsUi.StandardButtonHeight);
        AuraToolsUi.AddButton(toolbar.transform, "应用", ApplyMaximum, 64f, AuraToolsUi.CompactButtonHeight);
        status = AuraToolsUi.AddText(toolbar.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        ToolboxIconButtonV2.Create(toolbar.transform, "action.refresh", "刷新冒险历程", Refresh, 40f, "刷");
        ToolboxIconButtonV2.Create(toolbar.transform, "action.folder", "打开冒险数据目录", OpenDirectory, 40f, "夹");
        content = AuraToolsUi.CreateScroll(window.transform, "AdventureArchiveList");
        Refresh();
    }

    private static void Refresh()
    {
        if (content == null) return;
        AuraToolsUi.ClearChildren(content);
        try
        {
            var rows = AdventureArchiveStorage.Database.List(AuraToolsConfigService.AdventureArchive.MaximumAdventures);
            AdventureArchiveRuntime.RefreshCount();
            foreach (var record in rows)
            {
                var captured = record.AdventureId;
                var row = Row(content, "Adventure-" + captured, 84f);
                var title = RoleLabel(record) + " · " + ModeLabel(record) + " · " + StatusLabel(record);
                var detail = FormatDate(record.StartedUtc)
                             + " · " + DurationLabel(record)
                             + " · " + StageLabel(record.LatestStage)
                             + " · 战斗 " + record.BattleCount;
                if (record.DataCompleteness == AdventureArchiveSchema.SummaryOnly)
                {
                    detail += " · 旧版简要记录";
                }
                else if (record.DataCompleteness == AdventureArchiveSchema.Partial)
                {
                    detail += " · 旧版续接记录";
                }
                AuraToolsUi.AddText(row.transform, title + "\n" + detail,
                    AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
                    record.Status == "complete" ? AuraToolsUi.Text : AuraToolsUi.SuccessText, 68f, 1f);
                AuraToolsUi.AddButton(row.transform, "查看", () => ShowDetails(captured), 64f, AuraToolsUi.CompactButtonHeight);
                AuraToolsUi.AddButton(row.transform, "删除", () => Delete(captured), 64f, AuraToolsUi.CompactButtonHeight);
            }
            if (rows.Count == 0)
            {
                AuraToolsUi.AddText(content, "完成一次冒险后，这里会显示完整历程。",
                    AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 80f, 1f);
            }
            SetStatus("共 " + rows.Count + " 轮冒险");
        }
        catch (Exception ex)
        {
            SetStatus("读取失败：" + ex.Message);
        }
    }

    private static void ShowDetails(string adventureId)
    {
        if (overlayRoot == null) return;
        var details = AdventureArchiveStorage.Database.Load(adventureId);
        if (details == null)
        {
            SetStatus("记录已不存在。");
            return;
        }

        var record = details.Record;
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.AdventureArchive.Details",
            overlayRoot,
            "冒险历程 · " + FormatDate(record.StartedUtc),
            maxWidth: 1120f);
        var list = AuraToolsUi.CreateScroll(window.transform, "AdventureTimeline");

        var summary = Row(list, "Summary", 96f, section: true);
        AuraToolsUi.AddText(summary.transform,
            RoleLabel(record) + " · " + ModeLabel(record) + " · " + StatusLabel(record)
            + "\n" + FormatDate(record.StartedUtc) + " · " + DurationLabel(record)
            + " · " + StageLabel(record.LatestStage) + " · 战斗 " + record.BattleCount,
            AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 80f, 1f);
        if (record.DataCompleteness == AdventureArchiveSchema.SummaryOnly)
        {
            AuraToolsUi.AddText(summary.transform, "旧版简要记录", AuraToolsUi.HintFontSize,
                TextAnchor.MiddleCenter, AuraToolsUi.WarningText, 48f, 0f, 120f);
        }
        else if (record.DataCompleteness == AdventureArchiveSchema.Partial)
        {
            AuraToolsUi.AddText(summary.transform, "旧版续接记录", AuraToolsUi.HintFontSize,
                TextAnchor.MiddleCenter, AuraToolsUi.WarningText, 48f, 0f, 120f);
        }

        AddSectionTitle(list, "时间线");
        foreach (var item in details.Events.OrderBy(item => item.Sequence))
        {
            var hasDetail = !string.IsNullOrWhiteSpace(item.Detail);
            var row = Row(list, "Event-" + item.Sequence, hasDetail ? 70f : 52f);
            AuraToolsUi.AddText(row.transform,
                FormatTime(item.OccurredUtc) + "  " + item.Title
                + (hasDetail ? "\n" + Compact(item.Detail, 150) : ""),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text,
                hasDetail ? 58f : 40f, 1f);
            AuraToolsUi.AddText(row.transform, AuraToolsPlayerDisplay.TimelineKind(item.Kind),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText,
                hasDetail ? 58f : 40f, 0f, 100f);
        }
        if (details.Events.Count == 0)
        {
            AddEmptyRow(list, "这轮冒险没有可读取的时间线。", 54f);
        }

        AddSectionTitle(list, "当前状态");
        var latest = details.Snapshots.LastOrDefault();
        if (latest == null)
        {
            AddEmptyRow(list, "这条旧记录没有可读取的状态快照。", 54f);
        }
        else
        {
            AddStateRow(list, latest);
            AddContentGroup(list, "卡牌", AdventureArchiveProjection.ReadEntries(latest.CardsJson, "牌组"),
                entry => string.IsNullOrWhiteSpace(entry.DisplayName) ? AuraToolsPlayerDisplay.CardName(entry.Id) : entry.DisplayName);
            AddContentGroup(list, "遗物", AdventureArchiveProjection.ReadEntries(latest.RelicsJson, "遗物"),
                entry => string.IsNullOrWhiteSpace(entry.DisplayName) ? AuraToolsPlayerDisplay.RelicName(entry.Id) : entry.DisplayName);
            AddContentGroup(list, "祝福", AdventureArchiveProjection.ReadEntries(latest.BlessingsJson, "祝福"),
                entry => string.IsNullOrWhiteSpace(entry.DisplayName) ? AuraToolsPlayerDisplay.BlessingName(entry.Id) : entry.DisplayName);
        }

        AddSectionTitle(list, "关联战斗");
        AddBattleRows(list, window.transform, details.BattleRecordIds);
    }

    private static void AddStateRow(Transform parent, AdventureArchiveSnapshot snapshot)
    {
        var state = ParseObject(snapshot.StateJson);
        var money = state.Value<int?>("money") ?? 0;
        var sanity = state.Value<int?>("sanity") ?? 0;
        var maximumSanity = state.Value<int?>("maximumSanity") ?? 0;
        var level = state.Value<int?>("level") ?? 0;
        var node = state.Value<string>("nodeName") ?? "";
        var row = Row(parent, "CurrentState", 54f);
        AuraToolsUi.AddText(row.transform,
            (level > 0 ? "第 " + level + " 层 · " : "")
            + (node.Length > 0 ? node + " · " : "")
            + "金币 " + money + " · 理智 " + sanity + "/" + maximumSanity,
            AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.SuccessText, 42f, 1f);
    }

    private static void AddContentGroup(
        Transform parent,
        string title,
        IReadOnlyList<AdventureArchiveContentEntry> entries,
        Func<AdventureArchiveContentEntry, string> displayName)
    {
        var count = entries.Sum(entry => entry.Count);
        var header = Row(parent, "Content-" + title, 46f, section: true);
        AuraToolsUi.AddText(header.transform, title + " · " + count,
            AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 36f, 1f);
        if (entries.Count == 0)
        {
            AuraToolsUi.AddText(header.transform, "暂无", AuraToolsUi.HintFontSize,
                TextAnchor.MiddleRight, AuraToolsUi.MutedText, 36f, 0f, 80f);
            return;
        }
        foreach (var entry in entries)
        {
            var row = Row(parent, "ContentItem-" + title + "-" + entry.Id + "-" + entry.Zone, 44f);
            AuraToolsUi.AddText(row.transform,
                displayName(entry) + (entry.Count > 1 ? " ×" + entry.Count : ""),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 34f, 1f);
            AuraToolsUi.AddText(row.transform, entry.Zone,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 34f, 0f, 110f);
        }
    }

    private static void AddBattleRows(Transform parent, Transform window, IReadOnlyList<string> recordIds)
    {
        if (recordIds.Count == 0)
        {
            AddEmptyRow(parent, "这轮冒险尚未关联可读取的战斗记录。", 54f);
            return;
        }
        foreach (var recordId in recordIds)
        {
            var record = MatchRecordStorage.Database.Get(recordId);
            if (record == null) continue;
            var row = Row(parent, "Battle-" + recordId, 64f);
            AuraToolsUi.AddText(row.transform,
                AuraToolsPlayerDisplay.LevelName(record.LevelId)
                + " · " + AuraToolsPlayerDisplay.BattleResult(record.Result)
                + "\n" + record.TurnCount + " 回合 · " + FormatTime(record.StartedUtc),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 52f, 1f);
            AuraToolsUi.AddButton(row.transform, "分析", () => MatchAnalysisPresenter.Show(window, record),
                64f, AuraToolsUi.CompactButtonHeight);
            var replay = AuraToolsUi.AddButton(row.transform, "回放", () =>
            {
                MatchReplayLaunchCoordinator.Start(
                    record.RecordId,
                    0,
                    MatchRecordLibraryPresenter.CaptureReturnState(record.RecordId),
                    result => SetStatus("无法播放：" + result));
            }, 64f, AuraToolsUi.CompactButtonHeight);
            AuraToolsUi.SetButtonAvailable(
                replay,
                record.ReplayProtocol == MatchReplayProtocol.Version
                && string.Equals(record.ReplayState, MatchReplayStates.Ready, StringComparison.Ordinal),
                "该战斗只保留了摘要，没有完整回放");
        }
        var open = Row(parent, "OpenBattleLibrary", 48f);
        AuraToolsUi.AddText(open.transform, "需要管理、导出或删除战斗记录时，可打开对局资料库。",
            AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 36f, 1f);
        AuraToolsUi.AddButton(open.transform, "打开资料库", () => AuraToolsMatchRecordsRuntime.OpenLibrary(window),
            104f, AuraToolsUi.CompactButtonHeight);
    }

    private static void AddSectionTitle(Transform parent, string title)
    {
        var row = Row(parent, "Section-" + title, 42f, section: true);
        AuraToolsUi.AddText(row.transform, title, AuraToolsUi.ModuleTitleFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.Accent, 34f, 1f);
    }

    private static void AddEmptyRow(Transform parent, string message, float height)
    {
        var row = Row(parent, "Empty", height);
        AuraToolsUi.AddText(row.transform, message, AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, height - 12f, 1f);
    }

    private static void ApplyMaximum()
    {
        if (!int.TryParse(maximumInput?.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximum))
        {
            SetStatus("请输入 10 到 2000 的整数。");
            return;
        }
        maximum = Math.Max(10, Math.Min(2000, maximum));
        AuraToolsConfigService.AdventureArchive.MaximumAdventures = maximum;
        AuraToolsConfigService.SaveAdventureArchive();
        AdventureArchiveStorage.Database.Prune(maximum);
        AdventureArchiveRuntime.RefreshCount();
        if (maximumInput != null) maximumInput.text = maximum.ToString(CultureInfo.InvariantCulture);
        Refresh();
    }

    private static void Delete(string adventureId)
    {
        try
        {
            AdventureArchiveStorage.Database.Delete(adventureId);
            AdventureArchiveRuntime.RefreshCount();
            SetStatus("冒险历程已删除；关联战斗记录保持不变。");
            Refresh();
        }
        catch (Exception ex) { SetStatus("删除失败：" + ex.Message); }
    }

    private static void OpenDirectory()
    {
        var directory = Path.GetDirectoryName(AdventureArchiveStorage.Database.DatabasePath) ?? ".";
        FileResourceUtil.OpenDirectory(directory);
    }

    private static string RoleLabel(AdventureArchiveRecord record)
    {
        return string.IsNullOrWhiteSpace(record.RoleName)
            ? AuraToolsPlayerDisplay.RoleName(record.RoleId)
            : record.RoleName;
    }

    private static string ModeLabel(AdventureArchiveRecord record)
    {
        return string.IsNullOrWhiteSpace(record.ModeName)
            ? AuraToolsPlayerDisplay.ModeName(record.ModeId)
            : record.ModeName;
    }

    private static string StatusLabel(AdventureArchiveRecord record)
    {
        return record.Status == "complete" ? AuraToolsPlayerDisplay.BattleResult(record.Result) : "进行中";
    }

    private static string StageLabel(string value)
    {
        return AuraToolsPlayerDisplay.AdventureStage(value);
    }

    private static string DurationLabel(AdventureArchiveRecord record)
    {
        if (!DateTime.TryParse(record.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
            return "时长未知";
        var end = DateTime.TryParse(record.EndedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedEnd)
            ? parsedEnd
            : DateTime.UtcNow;
        var duration = end.ToUniversalTime() - start.ToUniversalTime();
        if (duration.TotalHours >= 1) return ((int)duration.TotalHours) + "小时" + duration.Minutes + "分钟";
        return Math.Max(0, (int)duration.TotalMinutes) + "分钟";
    }

    private static string FormatDate(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : value;
    }

    private static string FormatTime(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date.ToLocalTime().ToString("HH:mm:ss")
            : "";
    }

    private static string Compact(string value, int maximum)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maximum ? text : text.Substring(0, maximum - 1) + "…";
    }

    private static JObject ParseObject(string value)
    {
        try { return JObject.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value); }
        catch { return new JObject(); }
    }

    private static GameObject Row(Transform parent, string name, float height, bool section = false)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        if (section) AuraToolsUi.AddSectionImage(row);
        else AuraToolsUi.AddListRowImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        return row;
    }

    private static void SetStatus(string message)
    {
        if (status != null) status.text = message ?? "";
    }
}
