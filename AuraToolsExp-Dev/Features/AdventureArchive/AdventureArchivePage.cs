using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
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
        var window = AuraToolsUi.CreateOverlay("AuraTools.AdventureArchive", parent, "冒险档案馆", Refresh);
        overlayRoot = window.transform;
        var toolbar = Row(window.transform, "Toolbar", AuraToolsUi.ToolbarHeight);
        AuraToolsUi.AddText(toolbar.transform, "保留档案", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 72f);
        maximumInput = AuraToolsUi.AddInput(toolbar.transform,
            AuraToolsConfigService.AdventureArchive.MaximumAdventures.ToString(CultureInfo.InvariantCulture), _ => { }, 72f);
        AuraToolsUi.AddButton(toolbar.transform, "应用", ApplyMaximum, 64f);
        AuraToolsUi.AddText(toolbar.transform, "关键快照", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 68f);
        AuraToolsUi.AddToggle(toolbar.transform, AuraToolsConfigService.AdventureArchive.CaptureSnapshots, value =>
        {
            AuraToolsConfigService.AdventureArchive.CaptureSnapshots = value;
            AuraToolsConfigService.SaveAdventureArchive();
        });
        AuraToolsUi.AddButton(toolbar.transform, "刷新", Refresh, 68f);
        AuraToolsUi.AddButton(toolbar.transform, "打开数据目录", OpenDirectory, 108f);
        status = AuraToolsUi.AddText(toolbar.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
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
                var row = Row(content, "Adventure-" + captured, 76f);
                AuraToolsUi.AddText(row.transform,
                    FormatDate(record.StartedUtc) + " · " + Display(record.RoleId, "未知角色") + " · " + Display(record.ModeId, "默认模式")
                    + "\n" + StatusLabel(record) + " · 阶段 " + Display(record.LatestStage, "未知")
                    + " · 事件 " + record.EventCount + " · 快照 " + record.SnapshotCount + " · 战斗 " + record.BattleCount,
                    AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
                    record.Status == "complete" ? AuraToolsUi.Text : AuraToolsUi.SuccessText, 68f, 1f);
                AuraToolsUi.AddButton(row.transform, "查看", () => ShowDetails(captured), 68f, 34f);
                AuraToolsUi.AddButton(row.transform, "删除", () => Delete(captured), 68f, 34f);
            }
            if (rows.Count == 0)
            {
                AuraToolsUi.AddText(content, "暂无冒险档案。", AuraToolsUi.BodyFontSize,
                    TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 80f, 1f);
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
            SetStatus("档案已不存在。");
            return;
        }
        var record = details.Record;
        var window = AuraToolsUi.CreateOverlay("AuraTools.AdventureArchive.Details", overlayRoot,
            "冒险详情 - " + FormatDate(record.StartedUtc));
        var summary = Row(window.transform, "Summary", 72f);
        AuraToolsUi.AddText(summary.transform,
            Display(record.RoleId, "未知角色") + " · " + Display(record.ModeId, "默认模式") + " · " + StatusLabel(record)
            + "\n事件 " + record.EventCount + " · 快照 " + record.SnapshotCount + " · 战斗记录 " + record.BattleCount,
            AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 64f, 1f);
        AuraToolsUi.AddText(summary.transform,
            ShortId(record.AdventureId) + "\n游戏 " + record.GameBuild,
            AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 64f, 0f, 220f);

        var list = AuraToolsUi.CreateScroll(window.transform, "AdventureTimeline");
        foreach (var item in details.Events.OrderBy(item => item.Sequence))
        {
            var row = Row(list, "Event-" + item.Sequence, 62f);
            AuraToolsUi.AddText(row.transform,
                "#" + item.Sequence + "  " + FormatTime(item.OccurredUtc) + "  " + item.Title
                + (string.IsNullOrWhiteSpace(item.Detail) ? "" : "\n" + Compact(item.Detail, 120)),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 54f, 1f);
            AuraToolsUi.AddText(row.transform, item.Kind, AuraToolsUi.HintFontSize,
                TextAnchor.MiddleRight, AuraToolsUi.MutedText, 54f, 0f, 130f);
        }

        var latest = details.Snapshots.LastOrDefault();
        if (latest != null)
        {
            var cards = CountJsonArray(latest.CardsJson);
            var relics = CountJsonArray(latest.RelicsJson);
            AuraToolsUi.AddText(list,
                "最新快照 · " + latest.Stage + " · 卡牌 " + cards + " · 遗物 " + relics,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.SuccessText,
                AuraToolsUi.TextMinHeight, 1f);
        }
        if (details.BattleRecordIds.Count > 0)
        {
            AuraToolsUi.AddText(list,
                "关联战斗：" + string.Join("，", details.BattleRecordIds.Select(ShortId)),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight, 1f);
        }
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
            SetStatus("档案已删除；关联战斗记录保持不变。");
            Refresh();
        }
        catch (Exception ex) { SetStatus("删除失败：" + ex.Message); }
    }

    private static void OpenDirectory()
    {
        var directory = Path.GetDirectoryName(AdventureArchiveStorage.Database.DatabasePath) ?? ".";
        FileResourceUtil.OpenDirectory(directory);
    }

    private static int CountJsonArray(string value)
    {
        try { return JArray.Parse(string.IsNullOrWhiteSpace(value) ? "[]" : value).Count; }
        catch { return 0; }
    }

    private static string StatusLabel(AdventureArchiveRecord record)
    {
        return record.Status == "complete"
            ? "已结束" + (string.IsNullOrWhiteSpace(record.Result) ? "" : " · " + record.Result)
            : "进行中";
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
            : value;
    }

    private static string Display(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string ShortId(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Substring(0, Math.Min(12, value.Length));

    private static string Compact(string value, int maximum)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maximum ? text : text.Substring(0, maximum - 3) + "...";
    }

    private static GameObject Row(Transform parent, string name, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        AuraToolsUi.AddPanelImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }

    private static void SetStatus(string message)
    {
        if (status != null) status.text = message ?? "";
    }
}
