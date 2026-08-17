using System;
using System.IO;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Audio;

public static class AuraToolsAudioSettingsPage
{
    public static void ShowBattleBgm(Transform parent) => Show(parent, true);

    public static void ShowCardUse(Transform parent) => Show(parent, false);

    private static void Show(Transform parent, bool battleBgm)
    {
        var title = battleBgm ? "战斗背景音乐设置" : "出牌音效设置";
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.AudioSettings." + (battleBgm ? "BattleBgm" : "CardUse"),
            parent,
            title);
        var content = AuraToolsUi.CreateScroll(window.transform, "AudioSettings");
        var settings = battleBgm
            ? AuraToolsConfigService.Audio.BattleBgm
            : AuraToolsConfigService.Audio.CardUse;
        CreateModeRow(content, settings, battleBgm, window.transform);
        CreateAudioCommonRows(content, settings, battleBgm);
        AuraToolsUi.AddText(
            content,
            battleBgm
                ? "通用模式对所有角色使用同一首战斗音乐；高级模式可为每个角色单独配置。"
                : "通用模式对所有角色使用同一出牌音效；高级模式可为每个角色单独配置。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
    }

    private static void CreateModeRow(
        Transform parent,
        AudioFeatureSettings settings,
        bool battleBgm,
        Transform overlayParent)
    {
        var row = CreateInlineRow(parent, "ModeRow");
        var modeText = AuraToolsUi.AddText(
            row.transform,
            "模式：" + (settings.Mode == AudioModes.Advanced ? "高级（按角色）" : "通用"),
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        Button? modeButton = null;
        modeButton = AuraToolsUi.AddButton(
            row.transform,
            settings.Mode == AudioModes.Advanced ? "切到通用" : "切到高级",
            () =>
            {
                settings.Mode = settings.Mode == AudioModes.Advanced
                    ? AudioModes.Common
                    : AudioModes.Advanced;
                AuraToolsConfigService.SaveAudioFeature(battleBgm);
                modeText.text = "模式：" + (settings.Mode == AudioModes.Advanced
                    ? "高级（按角色）"
                    : "通用");
                AuraToolsUi.SetButtonLabel(
                    modeButton,
                    settings.Mode == AudioModes.Advanced ? "切到通用" : "切到高级");
            },
            96f);
        AuraToolsUi.AddButton(
            row.transform,
            "角色配置",
            () => AuraToolsAudioRoleEditor.Show(overlayParent, battleBgm),
            96f);
    }

    private static void CreateAudioCommonRows(
        Transform parent,
        AudioFeatureSettings settings,
        bool battleBgm)
    {
        var pathRow = CreateInlineRow(parent, "CommonAudioPathRow");
        AuraToolsUi.AddText(
            pathRow.transform,
            "通用音频",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            86f);
        InputField? pathInput = null;

        var actionRow = CreateInlineRow(parent, "CommonAudioActionRow");
        var pathStatus = AuraToolsUi.AddText(
            actionRow.transform,
            DescribeAudioPathStatus(settings.Common.RelativePath)
            + " / 优先级 " + settings.Common.Priority,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        void RefreshPath()
        {
            if (pathInput != null)
            {
                pathInput.text = settings.Common.RelativePath;
            }
            pathStatus.text = DescribeAudioPathStatus(settings.Common.RelativePath)
                              + " / 优先级 " + settings.Common.Priority;
        }

        pathInput = AuraToolsUi.AddInput(
            pathRow.transform,
            settings.Common.RelativePath,
            value => ApplyCommonAudioPath(settings, battleBgm, value, RefreshPath),
            620f);
        AuraToolsUi.AddButton(actionRow.transform, "选择音频", () =>
        {
            OptionalFileDialog.PickAudioFileAsync(
                FileResourceUtil.CommonAudioDirectory(),
                result =>
                {
                    if (result.Selected)
                    {
                        ApplyCommonAudioPath(
                            settings,
                            battleBgm,
                            result.Path,
                            RefreshPath);
                        return;
                    }

                    if (result.Status != OptionalFileDialogStatus.Cancelled)
                    {
                        AuraToolsLog.Warn(
                            "[AudioSettings] audio picker unavailable: "
                            + result.Message);
                    }
                });
        }, 88f);
        AuraToolsUi.AddButton(
            actionRow.transform,
            "打开目录",
            () => FileResourceUtil.OpenDirectory(FileResourceUtil.CommonAudioDirectory()),
            88f);
    }

    private static void ApplyCommonAudioPath(
        AudioFeatureSettings settings,
        bool battleBgm,
        string path,
        Action refreshed)
    {
        var trimmed = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            settings.Common.RelativePath = "";
            AuraToolsConfigService.SaveAudioFeature(battleBgm);
            refreshed();
            return;
        }

        var baseName = battleBgm ? "battle_bgm" : "card_use";
        var imported = FileResourceUtil.ImportAudioPath(
            trimmed,
            FileResourceUtil.CommonAudioDirectory(),
            baseName,
            out var message);
        if (string.IsNullOrWhiteSpace(imported))
        {
            AuraToolsLog.Warn(
                "[AudioSettings] common audio import rejected; current configuration preserved: "
                + message);
            refreshed();
            return;
        }

        settings.Common.RelativePath = imported;
        FileResourceUtil.RegisterManualDirectory(
            AuraSharedSystems.Audio,
            "LocalAudio",
            "Global",
            "all",
            AuraToolsIds.ModId,
            "user-imports",
            FileResourceUtil.CommonAudioDirectory(),
            out _);
        AuraToolsConfigService.SaveAudioFeature(battleBgm);
        refreshed();
    }

    private static string DescribeAudioPathStatus(string relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
        {
            return "未设置音频";
        }

        return File.Exists(
            AuraToolsConfiguredResourceResolver.ResolveAudioPath(relativeOrAbsolute))
            ? "文件存在"
            : "文件缺失";
    }

    private static GameObject CreateInlineRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraUiStableId.Assign(row, "audio-settings." + name);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.InlineRowHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }
}
