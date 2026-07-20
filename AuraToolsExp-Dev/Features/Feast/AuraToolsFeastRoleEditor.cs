using System;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.Feast;

public static class AuraToolsFeastRoleEditor
{
    private static Transform? roleContent;
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.FeastRoleEditor", parent, "一键美餐 - 按角色配置", RefreshAndSave);
        var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 10f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;

        hintText = Settings.AuraToolsUi.AddText(
            toolbar.transform,
            "CG 列表来自 AuraShared 注册表；这里只显示已启用 MOD 实际扫描到的角色。",
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "重新扫描", () => RefreshRoles(true), 92f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "保存", RefreshAndSave, 78f);

        roleContent = Settings.AuraToolsUi.CreateScroll(window.transform, "FeastRoles");
        RefreshRoles(false);
    }

    private static void RefreshRoles(bool forceScan)
    {
        EnsureRoleEntries(forceScan);
        RefreshRows(forceScan);
    }

    private static void EnsureRoleEntries(bool forceScan)
    {
        AuraToolsFeastDefaultMaterializer.EnsureCurrent(forceScan);
        foreach (var role in RoleCatalog.GetRoles(forceScan))
        {
            AuraToolsFeastRuntime.EnsureRoleSettings(role.Id, role.DisplayName);
        }
    }

    private static void RefreshRows(bool forceScan)
    {
        if (roleContent == null)
        {
            return;
        }

        Settings.AuraToolsUi.ClearChildren(roleContent);
        var roles = RoleCatalog.GetRoles(forceScan);
        if (roles.Count == 0)
        {
            Settings.AuraToolsUi.AddText(
                roleContent,
                "未扫描到可配置角色。",
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            return;
        }

        foreach (var role in roles.OrderBy(role => role.DisplayName).ThenBy(role => role.Id))
        {
            CreateRoleBlock(role);
        }
    }

    private static void CreateRoleBlock(RoleInfo role)
    {
        var settings = AuraToolsFeastRuntime.EnsureRoleSettings(role.Id, role.DisplayName);
        var candidates = AuraToolsFeastRuntime.BuildCandidateCgsForRole(role.Id).ToList();
        var effective = AuraToolsFeastRuntime.ResolveEffectiveCandidateForPreview(role.Id);

        var block = Settings.AuraToolsUi.CreateLayout("FeastRole-" + role.Id, roleContent!);
        Settings.AuraToolsUi.SetFixedHeight(block, 126f + candidates.Count * 36f);
        Settings.AuraToolsUi.AddImage(block, effective == null ? Settings.AuraToolsUi.Row : Settings.AuraToolsUi.ActiveRow);
        var blockLayout = block.AddComponent<VerticalLayoutGroup>();
        blockLayout.padding = new RectOffset(8, 8, 6, 6);
        blockLayout.spacing = 6f;
        blockLayout.childControlWidth = true;
        blockLayout.childControlHeight = true;
        blockLayout.childForceExpandWidth = true;
        blockLayout.childForceExpandHeight = false;

        var top = Settings.AuraToolsUi.CreateLayout("Top", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(top, Settings.AuraToolsUi.ButtonHeight);
        var topLayout = top.AddComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 8f;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = true;
        topLayout.childForceExpandWidth = false;
        topLayout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddToggle(top.transform, settings.Enabled, value =>
            AuraToolsFeastRuntime.SetRoleEnabled(role.Id, value));
        Settings.AuraToolsUi.AddText(top.transform, RoleTitle(role), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(top.transform, "预览", () => AuraToolsFeastRuntime.PreviewRole(role.Id), 78f, 34f);

        var bottom = Settings.AuraToolsUi.CreateLayout("Bottom", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(bottom, Settings.AuraToolsUi.ButtonHeight);
        var bottomLayout = bottom.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.spacing = 8f;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childForceExpandWidth = false;
        bottomLayout.childForceExpandHeight = false;

        var modes = new[] { AuraCg.Shared.AuraCgSelectionModes.Priority, AuraCg.Shared.AuraCgSelectionModes.Random, AuraCg.Shared.AuraCgSelectionModes.Sequential };
        var modeLabels = new[] { "按优先级", "随机", "按顺序" };
        var selectedMode = Array.FindIndex(modes, mode => string.Equals(mode, settings.SelectionMode, StringComparison.OrdinalIgnoreCase));
        Settings.AuraToolsUi.AddText(bottom.transform, "选择方式", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 72f);
        Settings.AuraToolsUi.AddSelectButton(bottom.transform, modeLabels.ToList(), Math.Max(0, selectedMode), index =>
        {
            if (index < 0 || index >= modes.Length)
            {
                return;
            }

            AuraToolsFeastRuntime.SetSelectionModeForRole(role.Id, modes[index]);
            RefreshRows(false);
        }, 180f);
        var enabledCount = candidates.Count(candidate => settings.IsCandidateEnabled(candidate.QualifiedCgId));
        Settings.AuraToolsUi.AddText(bottom.transform, "已启用 " + enabledCount + "/" + candidates.Count, Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);

        var candidateIds = candidates.Select(candidate => candidate.QualifiedCgId).ToArray();
        foreach (var candidate in candidates)
        {
            var candidateRow = Settings.AuraToolsUi.CreateLayout("Candidate-" + candidate.QualifiedCgId, block.transform);
            Settings.AuraToolsUi.SetFixedHeight(candidateRow, 30f);
            var candidateLayout = candidateRow.AddComponent<HorizontalLayoutGroup>();
            candidateLayout.spacing = 8f;
            candidateLayout.childControlWidth = true;
            candidateLayout.childControlHeight = true;
            candidateLayout.childForceExpandWidth = false;
            candidateLayout.childForceExpandHeight = false;
            Settings.AuraToolsUi.AddToggle(candidateRow.transform, settings.IsCandidateEnabled(candidate.QualifiedCgId), enabled =>
            {
                AuraToolsFeastRuntime.SetCandidateEnabledForRole(
                    role.Id,
                    candidate.QualifiedCgId,
                    enabled,
                    candidateIds);
                RefreshRows(false);
            });
            Settings.AuraToolsUi.AddText(candidateRow.transform, candidate.DisplayName + " / " + candidate.OwnerModId, Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
        }

        var actions = Settings.AuraToolsUi.CreateLayout("Actions", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(actions, Settings.AuraToolsUi.ButtonHeight);
        var actionsLayout = actions.AddComponent<HorizontalLayoutGroup>();
        actionsLayout.spacing = 8f;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = false;
        actionsLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddText(
            actions.transform,
            "资源：" + AuraToolsFeastDefaultMaterializer.DescribeRoleResource(role.Id),
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            settings.LocalCustomized ? Settings.AuraToolsUi.SuccessText : Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(actions.transform, "打开目录", () =>
            FileResourceUtil.OpenDirectory(AuraToolsFeastDefaultMaterializer.RoleDirectory(role.Id)), 88f, 30f);
        Settings.AuraToolsUi.AddButton(actions.transform, "选择PNG", () => PickRoleImage(role), 88f, 30f);
        Settings.AuraToolsUi.AddButton(actions.transform, "重置默认", () => ResetRoleImage(role), 88f, 30f);
    }

    private static void PickRoleImage(RoleInfo role)
    {
        SetHint("正在打开图片选择器……");
        OptionalFileDialog.PickImageFileAsync(AuraToolsFeastDefaultMaterializer.RoleDirectory(role.Id), result =>
        {
            if (result.Selected)
            {
                if (AuraToolsFeastDefaultMaterializer.ImportRoleImage(role.Id, result.Path, out var message))
                {
                    SetHint(message);
                    RefreshRows(false);
                    return;
                }

                SetHint(message);
                return;
            }

            SetHint(result.Status == OptionalFileDialogStatus.Cancelled
                ? "已取消选择图片。"
                : "无法打开文件选择器：" + result.Message);
        });
    }

    private static void ResetRoleImage(RoleInfo role)
    {
        AuraToolsFeastDefaultMaterializer.ResetRoleImage(role.Id, out var message);
        SetHint(message);
        RefreshRows(false);
    }

    private static string RoleTitle(RoleInfo role)
    {
        return string.IsNullOrWhiteSpace(role.DisplayName) ? role.Id : role.DisplayName;
    }

    private static void RefreshAndSave()
    {
        AuraToolsConfigService.MatchExperience.Feast.Normalize();
        AuraToolsConfigService.SaveMatchExperience();
        SetHint("已保存一键美餐角色配置。");
    }

    private static void SetHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

}
