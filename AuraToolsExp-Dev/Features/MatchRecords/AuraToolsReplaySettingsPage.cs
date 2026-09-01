using System;
using System.Globalization;
using AuraToolsExp.Dll.Config;
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

        var migrationRow = CreateInlineRow(content, "ReplayProtocol");
        AuraToolsUi.AddText(
            migrationRow.transform,
            "Replay Document v17 · 实测原生布局 · 屏幕空间状态 UI · pre-v17 仅保留摘要、分析与已有视频",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
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
        return AuraToolsUi.CreateSettingsRow(
            parent,
            "Replay-" + name,
            "replay-settings." + name);
    }
}
