using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Logging;

public static class AuraToolsLoggingSettingsPage
{
    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.LoggingSettings",
            parent,
            "文件日志设置");
        var content = AuraToolsUi.CreateScroll(window.transform, "LoggingSettings");
        BuildDetails(content);
    }

    private static void BuildDetails(Transform content)
    {
        var settings = AuraToolsConfigService.Logging;
        var diagnosticsRow = CreateInlineRow(content, "PerformanceDiagnostics");
        AuraToolsUi.AddToggle(
            diagnosticsRow.transform,
            settings.PerformanceDiagnostics,
            value =>
            {
                settings.PerformanceDiagnostics = value;
                AuraToolsConfigService.SaveLogging();
            });
        AuraToolsUi.AddText(
            diagnosticsRow.transform,
            "性能诊断（重启后生效）",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        var levelRow = CreateInlineRow(content, "Level");
        var levelLabels = new List<string> { "Debug", "Info", "Warning", "Error" };
        AuraToolsUi.AddText(
            levelRow.transform,
            "最低等级",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            90f);
        AuraToolsUi.AddSelectButton(
            levelRow.transform,
            levelLabels,
            SelectedLevelIndex(settings.MinimumLevel),
            index =>
            {
                if (index >= 0 && index < levelLabels.Count)
                {
                    settings.MinimumLevel = levelLabels[index];
                    settings.Normalize();
                    AuraToolsConfigService.SaveLogging();
                }
            },
            180f);

        var mirrorRow = CreateInlineRow(content, "Mirror");
        AuraToolsUi.AddToggle(mirrorRow.transform, settings.MirrorUnityLog, value =>
        {
            settings.MirrorUnityLog = value;
            AuraToolsConfigService.SaveLogging();
        });
        AuraToolsUi.AddText(
            mirrorRow.transform,
            "镜像 Unity 日志",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddToggle(mirrorRow.transform, settings.MirrorCommandsLog, value =>
        {
            settings.MirrorCommandsLog = value;
            AuraToolsConfigService.SaveLogging();
        });
        AuraToolsUi.AddText(
            mirrorRow.transform,
            "镜像 Commands 日志",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        var sourceRow = CreateInlineRow(content, "Source");
        AuraToolsUi.AddText(
            sourceRow.transform,
            "来源",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            48f);
        CreateListToggle(sourceRow.transform, settings.EnabledSources, "AuraTools");
        CreateListToggle(sourceRow.transform, settings.EnabledSources, "Unity");
        CreateListToggle(sourceRow.transform, settings.EnabledSources, "Command");

        var unityRow = CreateInlineRow(content, "UnityTypes");
        AuraToolsUi.AddText(
            unityRow.transform,
            "Unity 类型",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            82f);
        foreach (var type in new[] { "Log", "Warning", "Error", "Exception", "Assert" })
        {
            CreateListToggle(unityRow.transform, settings.UnityLogTypes, type);
        }

        var stackRow = CreateInlineRow(content, "Stack");
        var stackLabels = new List<string> { "关闭", "仅错误", "全部" };
        var stackValues = new List<string>
        {
            LoggingStackTraceModes.Off,
            LoggingStackTraceModes.ErrorsOnly,
            LoggingStackTraceModes.All
        };
        AuraToolsUi.AddText(
            stackRow.transform,
            "堆栈",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            60f);
        AuraToolsUi.AddSelectButton(
            stackRow.transform,
            stackLabels,
            SelectedStackIndex(settings.StackTraceMode),
            index =>
            {
                if (index >= 0 && index < stackValues.Count)
                {
                    settings.StackTraceMode = stackValues[index];
                    AuraToolsConfigService.SaveLogging();
                }
            },
            180f);

        var queueRow = CreateInlineRow(content, "Queue");
        AuraToolsUi.AddText(
            queueRow.transform,
            "队列上限",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            72f);
        AuraToolsUi.AddInput(queueRow.transform, settings.MaxQueueLength.ToString(), value =>
        {
            if (int.TryParse(value, out var parsed))
            {
                settings.MaxQueueLength = parsed;
                settings.Normalize();
                AuraToolsConfigService.SaveLogging();
            }
        }, 110f);
        AuraToolsUi.AddText(
            queueRow.transform,
            "Flush ms",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            72f);
        AuraToolsUi.AddInput(queueRow.transform, settings.FlushIntervalMs.ToString(), value =>
        {
            if (int.TryParse(value, out var parsed))
            {
                settings.FlushIntervalMs = parsed;
                settings.Normalize();
                AuraToolsConfigService.SaveLogging();
            }
        }, 110f);

        var directoryRow = CreateInlineRow(content, "Directory");
        AuraToolsUi.AddText(
            directoryRow.transform,
            "日志目录：" + AuraToolsConfigService.LogsDirectory,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            directoryRow.transform,
            "打开目录",
            () => FileResourceUtil.OpenDirectory(AuraToolsConfigService.LogsDirectory),
            92f);
    }

    private static int SelectedLevelIndex(string level)
    {
        var normalized = LoggingLevelNames.Normalize(level);
        if (string.Equals(normalized, LoggingLevelNames.Debug, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (string.Equals(normalized, LoggingLevelNames.Warning, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        return string.Equals(normalized, LoggingLevelNames.Error, StringComparison.OrdinalIgnoreCase)
            ? 3
            : 1;
    }

    private static int SelectedStackIndex(string mode)
    {
        var normalized = LoggingStackTraceModes.Normalize(mode);
        if (string.Equals(normalized, LoggingStackTraceModes.Off, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        return string.Equals(normalized, LoggingStackTraceModes.All, StringComparison.OrdinalIgnoreCase)
            ? 2
            : 1;
    }

    private static void CreateListToggle(
        Transform parent,
        List<string> values,
        string value)
    {
        var enabled = values.Any(item =>
            string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        AuraToolsUi.AddToggle(parent, enabled, selected =>
        {
            values.RemoveAll(item =>
                string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
            if (selected)
            {
                values.Add(value);
            }
            AuraToolsConfigService.Logging.Normalize();
            AuraToolsConfigService.SaveLogging();
        });
        AuraToolsUi.AddText(
            parent,
            value,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            82f);
    }

    private static GameObject CreateInlineRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout("Logging-" + name, parent);
        AuraUiStableId.Assign(row, "logging-settings." + name);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.InlineRowHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }
}
