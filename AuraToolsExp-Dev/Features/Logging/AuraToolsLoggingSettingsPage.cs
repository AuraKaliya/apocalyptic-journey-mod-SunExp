using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Logging;

public static class AuraToolsLoggingSettingsPage
{
    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.LoggingSettings",
            parent,
            "文件日志设置");
        var content = AuraToolsUi.CreateScroll(
            window.transform,
            "LoggingSettings");
        var status = "更改选项后会立即保存";

        void Rebuild()
        {
            var viewState = AuraUiViewState.CaptureForContent(content);
            AuraToolsUi.ClearChildren(content);
            BuildDetails(content, Commit, status);
            AuraUiViewState.RestoreAfterLayout(
                content,
                viewState,
                "AuraTools.LoggingSettings.Rows");
        }

        void Commit(Action<AuraToolsLoggingSettings> update)
        {
            var success = AuraToolsConfigService.TryUpdateLogging(
                update,
                out var message);
            status = success
                ? "已保存，重新打开页面或重启游戏后仍会保留"
                : "保存失败：" + message;
            if (!success)
            {
                AuraToolsLog.Warn("[LoggingSettings] " + message);
            }
            Rebuild();
        }

        Rebuild();
    }

    private static void BuildDetails(
        Transform content,
        Action<Action<AuraToolsLoggingSettings>> commit,
        string status)
    {
        var settings = AuraToolsConfigService.Logging;
        AuraToolsUi.AddText(
            content,
            status,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            status.StartsWith("保存失败", StringComparison.Ordinal)
                ? AuraToolsUi.ErrorText
                : status.StartsWith("已保存", StringComparison.Ordinal)
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        CreateToggleRow(
            content,
            "Enabled",
            "写入妙妙工具文件日志",
            settings.Enabled,
            value => commit(candidate => candidate.Enabled = value));
        CreateToggleRow(
            content,
            "PerformanceDiagnostics",
            "性能诊断（重启后完整生效）",
            settings.PerformanceDiagnostics,
            value => commit(candidate => candidate.PerformanceDiagnostics = value));

        var levelRow = CreateRow(content, "Level");
        var levelLabels = new[] { "调试", "一般", "警告", "错误" };
        var levelValues = new[]
        {
            LoggingLevelNames.Debug,
            LoggingLevelNames.Info,
            LoggingLevelNames.Warning,
            LoggingLevelNames.Error
        };
        AddFieldLabel(levelRow.transform, "最低记录等级");
        AuraToolsUi.AddSelectButton(
            levelRow.transform,
            levelLabels,
            SelectedLevelIndex(settings.MinimumLevel),
            index =>
            {
                if (index >= 0 && index < levelValues.Length)
                {
                    commit(candidate => candidate.MinimumLevel = levelValues[index]);
                }
            },
            180f,
            AuraToolsUi.StandardButtonHeight);

        CreateToggleRow(
            content,
            "MirrorUnity",
            "同时记录游戏引擎日志",
            settings.MirrorUnityLog,
            value => commit(candidate => candidate.MirrorUnityLog = value));

        var unityRow = CreateRow(content, "UnityTypes");
        AddFieldLabel(unityRow.transform, "游戏引擎日志类型", 132f);
        foreach (var type in new[]
                 {
                     (Value: "Log", Label: "一般"),
                     (Value: "Warning", Label: "警告"),
                     (Value: "Error", Label: "错误"),
                     (Value: "Exception", Label: "异常"),
                     (Value: "Assert", Label: "断言")
                 })
        {
            CreateMembershipToggle(
                unityRow.transform,
                settings.UnityLogTypes,
                type.Value,
                type.Label,
                selected => commit(candidate =>
                    SetMembership(candidate.UnityLogTypes, type.Value, selected)));
        }
        if (settings.MirrorUnityLog && settings.UnityLogTypes.Count == 0)
        {
            AuraToolsUi.AddText(
                content,
                "尚未选择游戏引擎日志类型，因此当前不会写入引擎日志。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText,
                AuraToolsUi.TextMinHeight,
                1f);
        }

        CreateToggleRow(
            content,
            "MirrorCommands",
            "同时记录控制台命令",
            settings.MirrorCommandsLog,
            value => commit(candidate => candidate.MirrorCommandsLog = value));

        var stackRow = CreateRow(content, "Stack");
        var stackLabels = new[] { "不记录", "仅错误", "全部" };
        var stackValues = new[]
        {
            LoggingStackTraceModes.Off,
            LoggingStackTraceModes.ErrorsOnly,
            LoggingStackTraceModes.All
        };
        AddFieldLabel(stackRow.transform, "堆栈信息");
        AuraToolsUi.AddSelectButton(
            stackRow.transform,
            stackLabels,
            SelectedStackIndex(settings.StackTraceMode),
            index =>
            {
                if (index >= 0 && index < stackValues.Length)
                {
                    commit(candidate => candidate.StackTraceMode = stackValues[index]);
                }
            },
            180f,
            AuraToolsUi.StandardButtonHeight);

        var queueRow = CreateRow(content, "Writer");
        AddFieldLabel(queueRow.transform, "缓存记录数");
        AuraToolsUi.AddInput(
            queueRow.transform,
            settings.MaxQueueLength.ToString(CultureInfo.InvariantCulture),
            value =>
            {
                if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    commit(candidate => candidate.MaxQueueLength = parsed);
                }
            },
            110f,
            AuraToolsUi.StandardButtonHeight);
        AddFieldLabel(queueRow.transform, "写入间隔（毫秒）", 136f);
        AuraToolsUi.AddInput(
            queueRow.transform,
            settings.FlushIntervalMs.ToString(CultureInfo.InvariantCulture),
            value =>
            {
                if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    commit(candidate => candidate.FlushIntervalMs = parsed);
                }
            },
            110f,
            AuraToolsUi.StandardButtonHeight);

        var directoryRow = CreateRow(content, "Directory");
        AuraToolsUi.AddText(
            directoryRow.transform,
            "日志保存在妙妙工具的数据目录中",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            directoryRow.transform,
            "打开日志目录",
            () => FileResourceUtil.OpenDirectory(
                AuraToolsConfigService.LogsDirectory),
            116f,
            AuraToolsUi.StandardButtonHeight);
    }

    private static void CreateToggleRow(
        Transform parent,
        string name,
        string label,
        bool value,
        Action<bool> changed)
    {
        var row = CreateRow(parent, name);
        AuraToolsUi.AddToggle(row.transform, value, changed);
        AuraToolsUi.AddText(
            row.transform,
            label,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
    }

    private static void CreateMembershipToggle(
        Transform parent,
        IReadOnlyCollection<string> values,
        string value,
        string label,
        Action<bool> changed)
    {
        var enabled = values.Any(item => string.Equals(
            item,
            value,
            StringComparison.OrdinalIgnoreCase));
        AuraToolsUi.AddToggle(parent, enabled, changed);
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            58f);
    }

    private static void SetMembership(
        List<string> values,
        string value,
        bool selected)
    {
        values.RemoveAll(item => string.Equals(
            item,
            value,
            StringComparison.OrdinalIgnoreCase));
        if (selected)
        {
            values.Add(value);
        }
    }

    private static void AddFieldLabel(
        Transform parent,
        string label,
        float width = 112f)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            width);
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        return AuraToolsUi.CreateSettingsRow(
            parent,
            "Logging-" + name,
            "logging-settings." + name);
    }

    private static int SelectedLevelIndex(string level)
    {
        return LoggingLevelNames.Normalize(level) switch
        {
            LoggingLevelNames.Debug => 0,
            LoggingLevelNames.Warning => 2,
            LoggingLevelNames.Error => 3,
            _ => 1
        };
    }

    private static int SelectedStackIndex(string mode)
    {
        return LoggingStackTraceModes.Normalize(mode) switch
        {
            LoggingStackTraceModes.Off => 0,
            LoggingStackTraceModes.All => 2,
            _ => 1
        };
    }
}
