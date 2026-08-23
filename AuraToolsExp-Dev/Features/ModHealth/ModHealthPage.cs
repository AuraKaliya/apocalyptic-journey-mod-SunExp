using System;
using System.Linq;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch;

namespace AuraToolsExp.Dll.Features.ModHealth;

internal static class ModHealthPage
{
    private static Transform? content;
    private static Text? summary;

    internal static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay("AuraTools.ModHealth", parent, "MOD 健康检查");
        var toolbar = Row(window.transform, "Toolbar", AuraToolsUi.ToolbarHeight);
        AuraToolsUi.AddText(toolbar.transform, "打开时扫描", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 82f);
        AuraToolsUi.AddToggle(toolbar.transform, AuraToolsExp.Dll.Config.AuraToolsConfigService.ModHealth.ScanOnOpen, value =>
        {
            AuraToolsExp.Dll.Config.AuraToolsConfigService.ModHealth.ScanOnOpen = value;
            AuraToolsExp.Dll.Config.AuraToolsConfigService.SaveModHealth();
        });
        summary = AuraToolsUi.AddText(toolbar.transform, "", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(toolbar.transform, "重新扫描", Scan, 96f);
        AuraToolsUi.AddButton(toolbar.transform, "导出报告", Export, 96f);
        AuraToolsUi.AddButton(toolbar.transform, "打开 MOD 目录", () => FileResourceUtil.OpenDirectory(Globals.ModsPath), 116f);
        content = AuraToolsUi.CreateScroll(window.transform, "ModHealthIssues");
        if (AuraToolsExp.Dll.Config.AuraToolsConfigService.ModHealth.ScanOnOpen || ModHealthRuntime.Current.ScannedUtc.Length == 0) Scan();
        else Refresh(ModHealthRuntime.Current);
    }

    private static void Scan()
    {
        try { Refresh(ModHealthRuntime.Scan()); }
        catch (Exception ex) { if (summary != null) summary.text = "扫描失败：" + ex.Message; }
    }

    private static void Refresh(ModHealthReport report)
    {
        if (summary != null)
        {
            summary.text = report.Issues.Count == 0
                ? "未发现需要处理的问题"
                : "需要处理 " + report.Issues.Count + " 项";
            summary.color = report.CriticalCount + report.ErrorCount > 0
                ? AuraToolsUi.WarningText
                : report.WarningCount > 0 ? AuraToolsUi.Accent : AuraToolsUi.SuccessText;
        }
        if (content == null) return;
        AuraToolsUi.ClearChildren(content);
        foreach (var issue in report.Issues
                     .OrderBy(issue => SeverityOrder(issue.Severity))
                     .ThenBy(issue => issue.ModId)
                     .ThenBy(issue => issue.Code))
        {
            var row = Row(content, "Issue-" + issue.Code, 64f);
            var modName = report.Mods.FirstOrDefault(mod => string.Equals(
                mod.ModId,
                issue.ModId,
                StringComparison.OrdinalIgnoreCase))?.ModName ?? "";
            AuraToolsUi.AddText(row.transform,
                SeverityLabel(issue.Severity)
                + (string.IsNullOrWhiteSpace(modName) ? "" : " · " + modName)
                + "\n" + Compact(issue.Message, 132),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
                issue.Severity == ModHealthSeverities.Info ? AuraToolsUi.MutedText : AuraToolsUi.WarningText,
                56f, 1f);
        }
        if (report.Issues.Count == 0)
        {
            AuraToolsUi.AddText(content, "游戏 MOD 加载契约检查通过。", AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleCenter, AuraToolsUi.SuccessText, 80f, 1f);
        }
    }

    private static void Export()
    {
        try
        {
            _ = ModHealthRuntime.ExportReport();
            if (summary != null) summary.text = "报告已导出";
        }
        catch (Exception ex)
        {
            if (summary != null) summary.text = "导出失败：" + ex.Message;
        }
    }

    private static int SeverityOrder(string severity)
    {
        return severity == ModHealthSeverities.Critical ? 0
            : severity == ModHealthSeverities.Error ? 1
            : severity == ModHealthSeverities.Warning ? 2 : 3;
    }

    private static string SeverityLabel(string severity)
    {
        return severity == ModHealthSeverities.Critical ? "严重"
            : severity == ModHealthSeverities.Error ? "错误"
            : severity == ModHealthSeverities.Warning ? "警告" : "信息";
    }

    private static GameObject Row(Transform parent, string name, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        AuraToolsUi.AddListRowImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static string Compact(string value, int maximum)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maximum ? text : text.Substring(0, maximum - 3) + "...";
    }
}
