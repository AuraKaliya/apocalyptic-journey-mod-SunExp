using System;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Audio;

public static class AuraToolsAudioRoleEditor
{
    private static Transform? roleContent;
    private static bool editingBattleBgm;
    private static Text? hintText;

    public static void Show(Transform parent, bool battleBgm)
    {
        editingBattleBgm = battleBgm;
        var title = battleBgm ? "高级战斗BGM：角色配置" : "高级出牌音效：角色配置";
        var window = AuraToolsUi.CreateOverlay("AuraTools.AudioRoleEditor", parent, title, Save);

        var toolbar = AuraToolsUi.CreateLayout("Toolbar", window.transform);
        AuraToolsUi.SetFixedHeight(toolbar, AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 10f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandHeight = false;
        hintText = AuraToolsUi.AddText(toolbar.transform, "提示：选择音频后会复制到 ModsData/AuraShared/Audio/Roles/ 下。", 14, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 34f, 1f);
        AuraToolsUi.AddButton(toolbar.transform, "扫描角色", () =>
        {
            EnsureRoleEntries(true);
            RefreshRows();
        }, 92f);
        AuraToolsUi.AddButton(toolbar.transform, "保存", Save, 78f);

        roleContent = AuraToolsUi.CreateScroll(window.transform, "AudioRoles");
        EnsureRoleEntries(false);
        RefreshRows();
    }

    private static AudioFeatureSettings Feature => editingBattleBgm
        ? AuraToolsConfigService.Audio.BattleBgm
        : AuraToolsConfigService.Audio.CardUse;

    private static void EnsureRoleEntries(bool forceScan)
    {
        foreach (var role in RoleCatalog.GetRoles(forceScan))
        {
            if (!Feature.Roles.TryGetValue(role.Id, out var settings) || settings == null)
            {
                Feature.Roles[role.Id] = new AudioRoleSettings
                {
                    Enabled = false,
                    RoleId = role.Id,
                    DisplayName = role.DisplayName,
                    Priority = editingBattleBgm ? 100 : 100,
                    HardClaim = false
                };
            }
            else if (string.IsNullOrWhiteSpace(settings.DisplayName))
            {
                settings.DisplayName = role.DisplayName;
            }
        }
    }

    private static void RefreshRows()
    {
        if (roleContent == null)
        {
            return;
        }

        AuraToolsUi.ClearChildren(roleContent);
        foreach (var pair in Feature.Roles.OrderBy(pair => pair.Value.DisplayName).ThenBy(pair => pair.Key))
        {
            CreateRoleBlock(pair.Key, pair.Value);
        }
    }

    private static void CreateRoleBlock(string key, AudioRoleSettings settings)
    {
        var block = AuraToolsUi.CreateLayout("RoleAudioBlock-" + key, roleContent!);
        AuraToolsUi.SetFixedHeight(block, AuraToolsUi.RuleBlockHeight);
        AuraToolsUi.AddImage(block, AuraToolsUi.Row);
        var blockLayout = block.AddComponent<VerticalLayoutGroup>();
        blockLayout.padding = new RectOffset(8, 8, 5, 5);
        blockLayout.spacing = 6f;
        blockLayout.childControlWidth = true;
        blockLayout.childControlHeight = true;
        blockLayout.childForceExpandWidth = true;
        blockLayout.childForceExpandHeight = false;

        var top = AuraToolsUi.CreateLayout("AudioRoleTop", block.transform);
        AuraToolsUi.SetFixedHeight(top, AuraToolsUi.ButtonHeight);
        var topLayout = top.AddComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 8f;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = true;
        topLayout.childForceExpandWidth = false;
        topLayout.childForceExpandHeight = false;

        AuraToolsUi.AddToggle(top.transform, settings.Enabled, value => settings.Enabled = value);
        AuraToolsUi.AddText(top.transform, RoleDisplayName(settings), AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddText(top.transform, "\u4f18\u5148", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 52f);
        AuraToolsUi.AddInput(top.transform, settings.Priority.ToString(), value =>
        {
            if (int.TryParse(value, out var priority))
            {
                settings.Priority = priority;
            }
        }, 80f);
        AuraToolsUi.AddButton(top.transform, "\u9009\u62e9\u97f3\u9891", () =>
        {
            PickRoleAudio(settings);
        });
        AuraToolsUi.AddButton(top.transform, "\u6253\u5f00\u76ee\u5f55", () => FileResourceUtil.OpenDirectory(FileResourceUtil.RoleAudioDirectory(settings.RoleId)));

        var bottom = AuraToolsUi.CreateLayout("AudioRoleBottom", block.transform);
        AuraToolsUi.SetFixedHeight(bottom, AuraToolsUi.ButtonHeight);
        var bottomLayout = bottom.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.spacing = 8f;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childForceExpandWidth = false;
        bottomLayout.childForceExpandHeight = false;

        AuraToolsUi.AddText(bottom.transform, "\u97f3\u9891", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 52f);
        AuraToolsUi.AddInput(bottom.transform, settings.RelativePath, value => ApplyRoleAudioPath(settings, value, false), 760f);
    }

    private static void PickRoleAudio(AudioRoleSettings settings)
    {
        var directory = FileResourceUtil.RoleAudioDirectory(settings.RoleId);
        SetHint("正在打开音频选择器...");
        OptionalFileDialog.PickAudioFileAsync(directory, result =>
        {
            if (result.Selected)
            {
                ApplyRoleAudioPath(settings, result.Path, true);
                return;
            }

            if (result.Status == OptionalFileDialogStatus.Cancelled)
            {
                SetHint("已取消选择音频。");
                return;
            }

            AuraToolsLog.Warn("[AudioRoleEditor] audio picker unavailable: " + result.Message);
            SetHint("无法打开系统文件选择器；请使用路径输入框修改，或先把音频放进角色目录。");
        });
    }

    private static void ApplyRoleAudioPath(AudioRoleSettings settings, string path, bool refresh)
    {
        var trimmed = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            settings.RelativePath = "";
            SetHint("已清空音频路径。");
            if (refresh)
            {
                RefreshRows();
            }

            return;
        }

        var baseName = editingBattleBgm ? "battle_bgm" : "card_use";
        var imported = FileResourceUtil.ImportAudioPath(trimmed, FileResourceUtil.RoleAudioDirectory(settings.RoleId), baseName, out var message);
        if (string.IsNullOrWhiteSpace(imported))
        {
            settings.RelativePath = trimmed;
            SetHint(message + " 已保留输入路径。");
        }
        else
        {
            settings.RelativePath = imported;
            settings.Enabled = true;
            SetHint(message + " " + settings.RelativePath);
        }

        if (refresh)
        {
            RefreshRows();
        }
    }

    private static void CreateRoleRow(string key, AudioRoleSettings settings)
    {
        var row = AuraToolsUi.CreateLayout("RoleAudio-" + key, roleContent!);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.RoleRowHeight);
        AuraToolsUi.AddImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        AuraToolsUi.AddToggle(row.transform, settings.Enabled, value => settings.Enabled = value);
        AuraToolsUi.AddText(row.transform, RoleDisplayName(settings), AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 220f);
        AuraToolsUi.AddInput(row.transform, settings.RelativePath, value => ApplyRoleAudioPath(settings, value, false), 320f);
        AuraToolsUi.AddText(row.transform, "优先级", 12, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 30f, 0f, 48f);
        AuraToolsUi.AddInput(row.transform, settings.Priority.ToString(), value =>
        {
            if (int.TryParse(value, out var priority))
            {
                settings.Priority = priority;
            }
        }, 80f);
        AuraToolsUi.AddButton(row.transform, "选择音频", () =>
        {
            PickRoleAudio(settings);
        }, 88f, 30f);
        AuraToolsUi.AddButton(row.transform, "打开目录", () => FileResourceUtil.OpenDirectory(FileResourceUtil.RoleAudioDirectory(settings.RoleId)), 82f, 30f);
    }

    private static string RoleDisplayName(AudioRoleSettings settings)
    {
        var displayName = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? RoleCatalog.GetDisplayName(settings.RoleId)
            : settings.DisplayName.Trim();
        return string.IsNullOrWhiteSpace(displayName) ? settings.RoleId : displayName;
    }

    private static void Save()
    {
        Feature.Normalize(editingBattleBgm ? "Audio/Common/battle_bgm.mp3" : "Audio/Common/card_use.mp3", -1000, false);
        AuraToolsConfigService.SaveAudio();
        AuraToolsAudioRuntime.RegisterProviders();
        SetHint("已保存角色音频配置。");
    }

    private static void SetHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }
}
