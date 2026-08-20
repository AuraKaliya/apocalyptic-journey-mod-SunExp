using System;
using System.Globalization;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.LegacyMigration;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.MatchRecords;

public static class AuraToolsReplaySettingsPage
{
    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.ReplaySettings",
            parent,
            "战斗回放设置");
        var content = AuraToolsUi.CreateScroll(window.transform, "ReplaySettings");
        BuildDetails(content, window.transform);
    }

    private static void BuildDetails(Transform content, Transform overlayParent)
    {
        var replay = AuraToolsConfigService.MatchExperience.MatchRecords.Replay;
        var replayLimitRow = CreateInlineRow(content, "Limit");
        AuraToolsUi.AddText(
            replayLimitRow.transform,
            "自动回放保存上限",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddInput(
            replayLimitRow.transform,
            replay.AutoRecordLimit.ToString(CultureInfo.InvariantCulture),
            value =>
            {
                if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    replay.AutoRecordLimit = parsed;
                    replay.Normalize();
                    AuraToolsConfigService.SaveBattleReplay();
                }
            },
            104f);

        var videoRow = CreateInlineRow(content, "Video");
        AuraToolsUi.AddText(
            videoRow.transform,
            "视频导出",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        Button? qualityButton = null;
        qualityButton = AuraToolsUi.AddButton(
            videoRow.transform,
            replay.Video.Quality,
            () =>
            {
                replay.Video.Quality = replay.Video.Quality == "1080p"
                    ? "720p"
                    : "1080p";
                AuraToolsConfigService.SaveBattleReplay();
                AuraToolsUi.SetButtonLabel(qualityButton, replay.Video.Quality);
            },
            86f);
        AuraToolsUi.AddText(
            videoRow.transform,
            "统一 MP4 · 30 FPS",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            148f);

        CreateToggle(content, "导出战斗 HUD", replay.Video.IncludeUi, value =>
        {
            replay.Video.IncludeUi = value;
            AuraToolsConfigService.SaveBattleReplay();
        });
        CreateToggle(content, "导出音频", replay.Video.IncludeAudio, value =>
        {
            replay.Video.IncludeAudio = value;
            AuraToolsConfigService.SaveBattleReplay();
        });
        var libraryRow = CreateInlineRow(content, "Library");
        AuraToolsUi.AddText(
            libraryRow.transform,
            "自动记录 " + AuraToolsMatchRecordsRuntime.AutoRecordCount
            + " · 收藏 " + AuraToolsMatchRecordsRuntime.FavoriteRecordCount,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            libraryRow.transform,
            "打开对局资料库",
            () => AuraToolsMatchRecordsRuntime.OpenLibrary(overlayParent),
            132f);

        var migrationRow = CreateInlineRow(content, "LegacyMigration");
        var migrationStatus = AuraToolsUi.AddText(
            migrationRow.transform,
            "v8/v9 仅支持只读扫描；清理不会删除统计",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(migrationRow.transform, "扫描旧录像", () =>
        {
            try
            {
                var report = ReplayLegacyMigrationService.Scan();
                migrationStatus.text = "已扫描 " + report.Records.Count + " 条；旧 chunks "
                                       + report.ChunkRowsToDelete + " 个。请查看报告后再确认清理。";
            }
            catch (Exception ex)
            {
                migrationStatus.text = "扫描失败：" + ex.Message;
            }
        }, 108f);
        AuraToolsUi.AddButton(migrationRow.transform, "查看报告", () =>
        {
            var path = ReplayLegacyMigrationService.LatestReportPath;
            var directory = string.IsNullOrWhiteSpace(path)
                ? System.IO.Path.Combine(MatchRecordStorage.RootDirectory, "MigrationReports")
                : System.IO.Path.GetDirectoryName(path) ?? MatchRecordStorage.RootDirectory;
            FileResourceUtil.OpenDirectory(directory);
        }, 92f);
        AuraToolsUi.AddButton(migrationRow.transform, "确认清理旧回放", () =>
        {
            try
            {
                var report = ReplayLegacyMigrationService.ApplyLatest();
                migrationStatus.text = "已清理 " + report.ChunkRowsToDelete + " 个旧 chunks；统计已保留。";
            }
            catch (Exception ex)
            {
                migrationStatus.text = "清理未执行：" + ex.Message;
            }
        }, 132f);
    }

    private static void CreateToggle(
        Transform parent,
        string label,
        bool value,
        Action<bool> changed)
    {
        var row = CreateInlineRow(parent, "Toggle-" + label);
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

    private static GameObject CreateInlineRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout("Replay-" + name, parent);
        AuraUiStableId.Assign(row, "replay-settings." + name);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.InlineRowHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }
}
