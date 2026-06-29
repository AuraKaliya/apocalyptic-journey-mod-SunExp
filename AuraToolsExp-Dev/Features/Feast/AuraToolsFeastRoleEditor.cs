using System;
using System.Collections.Generic;
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
        Settings.AuraToolsUi.SetFixedHeight(block, 110f);
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

        Settings.AuraToolsUi.AddToggle(top.transform, settings.Enabled, value => settings.Enabled = value);
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

        var options = BuildCgOptions(candidates, settings.SelectedCgId);
        Settings.AuraToolsUi.AddText(bottom.transform, "CG", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 36f);
        Settings.AuraToolsUi.AddSelectButton(bottom.transform, options.Select(option => option.Label).ToList(), SelectedOptionIndex(options, settings.SelectedCgId), index =>
        {
            if (index < 0 || index >= options.Count)
            {
                return;
            }

            settings.SelectedCgId = options[index].QualifiedCgId;
            RefreshRows(false);
        }, 620f);
    }

    private static List<CgOption> BuildCgOptions(IReadOnlyList<FeastCgCandidate> candidates, string selected)
    {
        var options = new List<CgOption>
        {
            new() { Label = "自动选择", QualifiedCgId = "" }
        };
        foreach (var candidate in candidates)
        {
            options.Add(new CgOption
            {
                Label = candidate.DisplayName + " / " + candidate.OwnerModId,
                QualifiedCgId = candidate.QualifiedCgId
            });
        }

        if (!string.IsNullOrWhiteSpace(selected)
            && !options.Any(option => string.Equals(option.QualifiedCgId, selected, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new CgOption
            {
                Label = "已失效：" + selected,
                QualifiedCgId = selected
            });
        }

        return options;
    }

    private static int SelectedOptionIndex(IReadOnlyList<CgOption> options, string selected)
    {
        if (string.IsNullOrWhiteSpace(selected))
        {
            return 0;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].QualifiedCgId, selected, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
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

    private sealed class CgOption
    {
        public string Label { get; set; } = "";

        public string QualifiedCgId { get; set; } = "";
    }
}
