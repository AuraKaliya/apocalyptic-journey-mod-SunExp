using System;
using System.Globalization;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
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
                    AuraToolsConfigService.SaveMatchExperience();
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
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(qualityButton, replay.Video.Quality);
            },
            86f);
        Button? fpsButton = null;
        fpsButton = AuraToolsUi.AddButton(
            videoRow.transform,
            replay.Video.FramesPerSecond + " FPS",
            () =>
            {
                replay.Video.FramesPerSecond = replay.Video.FramesPerSecond >= 60
                    ? 30
                    : 60;
                AuraToolsConfigService.SaveMatchExperience();
                AuraToolsUi.SetButtonLabel(
                    fpsButton,
                    replay.Video.FramesPerSecond + " FPS");
            },
            86f);

        CreateToggle(content, "导出战斗 HUD", replay.Video.IncludeUi, value =>
        {
            replay.Video.IncludeUi = value;
            AuraToolsConfigService.SaveMatchExperience();
        });
        CreateToggle(content, "导出音频", replay.Video.IncludeAudio, value =>
        {
            replay.Video.IncludeAudio = value;
            AuraToolsConfigService.SaveMatchExperience();
        });
        CreateToggle(content, "配置 FFmpeg 时优先 MP4", replay.Video.PreferMp4, value =>
        {
            replay.Video.PreferMp4 = value;
            AuraToolsConfigService.SaveMatchExperience();
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
