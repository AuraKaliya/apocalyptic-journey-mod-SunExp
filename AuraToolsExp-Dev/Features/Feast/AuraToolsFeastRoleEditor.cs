using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.Feast;

public static class AuraToolsFeastRoleEditor
{
    private static Transform? roleContent;
    private static Transform? resourceContent;
    private static Text? statusText;
    private static RoleInfo? activeRole;
    private static Transform? editorRoot;

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay(
            "AuraTools.FeastRoleEditor",
            parent,
            "一键美餐 - 角色资源",
            RefreshAndSave);
        editorRoot = window.transform;
        var toolbar = CreateHorizontal("Toolbar", window.transform, Settings.AuraToolsUi.ToolbarHeight);
        statusText = Settings.AuraToolsUi.AddText(
            toolbar.transform,
            "",
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "重新扫描", () => RefreshRoles(true), 104f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "历史资源", () => ShowHistory(window.transform), 96f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "保存", RefreshAndSave, 78f);

        roleContent = Settings.AuraToolsUi.CreateScroll(window.transform, "FeastRoles");
        RefreshRoles(false);
    }

    private static void RefreshRoles(bool forceScan)
    {
        if (forceScan)
        {
            AuraToolsFeastRuntime.RefreshCatalog();
        }

        EnsureRoleEntries(forceScan);
        RefreshRoleCards(forceScan);
    }

    private static void EnsureRoleEntries(bool forceScan)
    {
        foreach (var role in AllRoles(forceScan))
        {
            AuraToolsFeastRuntime.EnsureRoleSettings(role.Id, role.DisplayName);
        }
    }

    private static IReadOnlyList<RoleInfo> AllRoles(bool forceScan)
    {
        return RoleCatalog.GetRoles(forceScan)
            .OrderBy(role => role.DisplayName)
            .ThenBy(role => role.Id)
            .ToArray();
    }

    private static void RefreshRoleCards(bool forceScan)
    {
        if (roleContent == null)
        {
            return;
        }

        var viewState = AuraUiViewState.CaptureForContent(roleContent);
        Settings.AuraToolsUi.ClearChildren(roleContent);
        var roles = AllRoles(forceScan);
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

        foreach (var role in roles)
        {
            CreateRoleCard(role);
        }
        AuraUiViewState.RestoreAfterLayout(
            roleContent,
            viewState,
            "AuraTools.Feast.Roles");
    }

    private static void CreateRoleCard(RoleInfo role)
    {
        var roleSettings = AuraToolsFeastRuntime.EnsureRoleSettings(role.Id, role.DisplayName);
        var candidates = AuraToolsFeastRuntime.BuildCandidateCgsForRole(role.Id);
        var registeredCount = candidates.Count(candidate => candidate.SourceKind == FeastCgSourceKind.Registered);
        var manualCount = candidates.Count(candidate => candidate.SourceKind == FeastCgSourceKind.Manual);
        var enabledCount = candidates.Count(candidate => roleSettings.IsCandidateEnabled(candidate.QualifiedCgId));

        var card = Settings.AuraToolsUi.CreateLayout("FeastRole-" + role.Id, roleContent!);
        AuraUiStableId.Assign(card, "feast.role." + role.Id);
        Settings.AuraToolsUi.SetFixedHeight(card, 126f);
        Settings.AuraToolsUi.AddPanelImage(
            card,
            enabledCount > 0 && roleSettings.Enabled
                ? Settings.AuraToolsUi.ActiveRow
                : Settings.AuraToolsUi.Row);
        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 7, 7);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var top = CreateHorizontal("Top", card.transform, Settings.AuraToolsUi.ButtonHeight);
        var roleToggle = Settings.AuraToolsUi.AddToggle(top.transform, roleSettings.Enabled, enabled =>
        {
            AuraToolsFeastRuntime.SetRoleEnabled(role.Id, enabled);
            RefreshRoleCards(false);
        });
        AuraUiStableId.Assign(roleToggle.gameObject, "feast.role." + role.Id + ".toggle");
        Settings.AuraToolsUi.AddText(
            top.transform,
            RoleTitle(role) + "\n" + role.Id,
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.Text,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(top.transform, "预览", () => AuraToolsFeastRuntime.PreviewRole(role.Id), 76f);
        Settings.AuraToolsUi.AddButton(top.transform, "资源管理", () => ShowResources(editorRoot ?? card.transform, role), 104f);

        var bottom = CreateHorizontal("Bottom", card.transform, Settings.AuraToolsUi.ButtonHeight);
        Settings.AuraToolsUi.AddText(
            bottom.transform,
            "注册 " + registeredCount
            + " · 人工 " + manualCount
            + " · 待选 " + enabledCount + "/" + candidates.Count,
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        AddSelectionMode(bottom.transform, role, roleSettings);
    }

    private static void AddSelectionMode(
        Transform parent,
        RoleInfo role,
        FeastRoleSettings roleSettings)
    {
        var modes = new[]
        {
            AuraCgSelectionModes.Priority,
            AuraCgSelectionModes.Random,
            AuraCgSelectionModes.Sequential
        };
        var labels = new[] { "按优先级", "随机", "按顺序" };
        var selected = Array.FindIndex(
            modes,
            mode => string.Equals(mode, roleSettings.SelectionMode, StringComparison.OrdinalIgnoreCase));
        Settings.AuraToolsUi.AddSelectButton(parent, labels, Math.Max(0, selected), index =>
        {
            if (index >= 0 && index < modes.Length)
            {
                AuraToolsFeastRuntime.SetSelectionModeForRole(role.Id, modes[index]);
                RefreshRoleCards(false);
            }
        }, 150f, 38f);
    }

    private static void ShowResources(Transform parent, RoleInfo role)
    {
        activeRole = role;
        var window = Settings.AuraToolsUi.CreateOverlay(
            "AuraTools.FeastResourceEditor",
            parent,
            "一键美餐 - " + RoleTitle(role),
            () => activeRole = null,
            true,
            1040f);
        var toolbar = CreateHorizontal("Toolbar", window.transform, Settings.AuraToolsUi.ToolbarHeight);
        Settings.AuraToolsUi.AddText(
            toolbar.transform,
            role.Id,
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "打开人工目录", () =>
            FileResourceUtil.OpenDirectory(AuraToolsFeastManualResourceStore.RoleDirectory(role.Id)), 128f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "导入图片", () => PickRoleImage(role), 96f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "移除人工项", () => RemoveRoleImage(role), 112f);
        resourceContent = Settings.AuraToolsUi.CreateScroll(window.transform, "FeastResources");
        RefreshResourceCards();
    }

    private static void ShowHistory(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay(
            "AuraTools.SharedResourceHistory",
            parent,
            "共享资源 - 历史资源",
            null,
            true,
            1120f);
        var history = AuraSharedResourceProtocol.QueryCatalog(AuraToolsIds.ModId, new AuraSharedCatalogQueryV4
        {
            Visibility = AuraSharedCatalogVisibilities.All
        });
        var content = Settings.AuraToolsUi.CreateScroll(window.transform, "SharedResourceHistory");
        var entries = history.Entries.Where(entry => entry.HistoryReasons.Count > 0 || IsInapplicableRoleResource(entry)).ToArray();
        if (entries.Length == 0)
        {
            Settings.AuraToolsUi.AddText(content, "当前没有历史资源。", Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
            return;
        }
        foreach (var entry in entries)
        {
            var reasons = entry.HistoryReasons.ToList();
            if (IsInapplicableRoleResource(entry)) reasons.Add(AuraSharedHistoryReasons.Inapplicable);
            var row = Settings.AuraToolsUi.CreateLayout("History-" + entry.QualifiedResourceId, content);
            Settings.AuraToolsUi.SetFixedHeight(row, 78f);
            Settings.AuraToolsUi.AddPanelImage(row, Settings.AuraToolsUi.Row);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            Settings.AuraToolsUi.AddText(row.transform,
                entry.QualifiedResourceId + "\n" + entry.Resource.OriginKind + " · "
                + string.Join(" / ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)),
                Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, 62f, 1f);
            Settings.AuraToolsUi.AddButton(row.transform, "打开目录", () =>
            {
                var absolute = AuraToolsConfigService.ResolveConfiguredPath(entry.CanonicalPath);
                var directory = Directory.Exists(absolute) ? absolute : Path.GetDirectoryName(absolute);
                if (!string.IsNullOrWhiteSpace(directory)) FileResourceUtil.OpenDirectory(directory);
            }, 92f);
        }
    }

    private static bool IsInapplicableRoleResource(AuraSharedCatalogEntryV4 entry)
    {
        return string.Equals(entry.Resource.ScopeType, "Role", StringComparison.OrdinalIgnoreCase)
               && !RoleCatalog.GetRoles().Any(role => RoleCatalog.MatchesRole(
                   role.Id,
                   entry.Resource.ScopeId,
                   entry.Resource.ScopeAliases));
    }

    private static void RefreshResourceCards()
    {
        if (resourceContent == null || activeRole == null)
        {
            return;
        }

        var viewState = AuraUiViewState.CaptureForContent(resourceContent);
        Settings.AuraToolsUi.ClearChildren(resourceContent);
        var role = activeRole;
        var roleSettings = AuraToolsFeastRuntime.EnsureRoleSettings(role.Id, role.DisplayName);
        var candidates = AuraToolsFeastRuntime.BuildCandidateCgsForRole(role.Id);
        if (candidates.Count == 0)
        {
            Settings.AuraToolsUi.AddText(
                resourceContent,
                "当前没有可用资源。",
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            return;
        }

        var candidateIds = candidates.Select(candidate => candidate.QualifiedCgId).ToArray();
        foreach (var candidate in candidates)
        {
            CreateResourceCard(role, roleSettings, candidate, candidateIds);
        }
        AuraUiViewState.RestoreAfterLayout(
            resourceContent,
            viewState,
            "AuraTools.Feast.Resources");
    }

    private static void CreateResourceCard(
        RoleInfo role,
        FeastRoleSettings roleSettings,
        FeastCgCandidate candidate,
        IReadOnlyList<string> candidateIds)
    {
        var enabled = roleSettings.IsCandidateEnabled(candidate.QualifiedCgId);
        var card = Settings.AuraToolsUi.CreateLayout(
            "FeastResource-" + candidate.QualifiedCgId,
            resourceContent!);
        AuraUiStableId.Assign(card, "feast.resource." + candidate.QualifiedCgId);
        Settings.AuraToolsUi.SetFixedHeight(card, 88f);
        Settings.AuraToolsUi.AddPanelImage(
            card,
            enabled ? Settings.AuraToolsUi.ActiveRow : Settings.AuraToolsUi.Row);
        var layout = card.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var resourceToggle = Settings.AuraToolsUi.AddToggle(card.transform, enabled, value =>
        {
            AuraToolsFeastRuntime.SetCandidateEnabledForRole(
                role.Id,
                candidate.QualifiedCgId,
                value,
                candidateIds);
            RefreshResourceCards();
            RefreshRoleCards(false);
        });
        AuraUiStableId.Assign(
            resourceToggle.gameObject,
            "feast.resource." + candidate.QualifiedCgId + ".toggle");
        Settings.AuraToolsUi.AddText(
            card.transform,
            candidate.DisplayName
            + " · " + SourceLabel(candidate.SourceKind)
            + " · " + candidate.OwnerModId
            + "\n" + candidate.QualifiedCgId,
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            enabled ? Settings.AuraToolsUi.Text : Settings.AuraToolsUi.MutedText,
            70f,
            1f);
        Settings.AuraToolsUi.AddButton(card.transform, "打开目录", () =>
            OpenCandidateDirectory(role.Id, candidate), 92f);
    }

    private static void PickRoleImage(RoleInfo role)
    {
        SetStatus("正在打开图片选择器……");
        OptionalFileDialog.PickImageFileAsync(
            AuraToolsFeastManualResourceStore.RoleDirectory(role.Id),
            result =>
            {
                if (result.Selected)
                {
                    AuraToolsFeastManualResourceStore.ImportRoleImage(role.Id, result.Path, out var message);
                    SetStatus(message);
                    RefreshResourceCards();
                    RefreshRoleCards(false);
                    return;
                }

                SetStatus(result.Status == OptionalFileDialogStatus.Cancelled
                    ? "已取消选择。"
                    : "无法打开文件选择器：" + result.Message);
            });
    }

    private static void RemoveRoleImage(RoleInfo role)
    {
        AuraToolsFeastManualResourceStore.RemoveRoleImage(role.Id, out var message);
        SetStatus(message);
        RefreshResourceCards();
        RefreshRoleCards(false);
    }

    private static void OpenCandidateDirectory(string roleId, FeastCgCandidate candidate)
    {
        if (candidate.SourceKind == FeastCgSourceKind.Manual)
        {
            FileResourceUtil.OpenDirectory(AuraToolsFeastManualResourceStore.RoleDirectory(roleId));
            return;
        }

        var path = AuraToolsConfigService.ResolveConfiguredPath(candidate.ImageResource);
        var directory = string.IsNullOrWhiteSpace(path) ? "" : Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            FileResourceUtil.OpenDirectory(directory);
        }
    }

    private static GameObject CreateHorizontal(string name, Transform parent, float height)
    {
        var row = Settings.AuraToolsUi.CreateLayout(name, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, height);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static string RoleTitle(RoleInfo role)
    {
        return string.IsNullOrWhiteSpace(role.DisplayName) ? role.Id : role.DisplayName;
    }

    private static string SourceLabel(FeastCgSourceKind sourceKind)
    {
        return sourceKind == FeastCgSourceKind.Manual
            ? "人工配置"
            : sourceKind == FeastCgSourceKind.Default
                ? "默认资源"
                : "注册资源";
    }

    private static void RefreshAndSave()
    {
        AuraToolsConfigService.MatchExperience.Feast.Normalize();
        AuraToolsConfigService.SaveMatchExperience();
        SetStatus("已保存一键美餐配置。");
    }

    private static void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message ?? "";
        }
    }
}
