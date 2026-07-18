using System;
using System.Collections.Generic;
using System.Linq;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckRoleManager
{
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.StarterDeckRoleManager", parent, "【世界推演】角色开局卡组");
        var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        hintText = Settings.AuraToolsUi.AddText(toolbar.transform, "MOD 注册 Profile 为只读；复制后会生成 AuraTools 本地可编辑卡组。", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "刷新角色", () => Show(parent), 96f);

        var content = Settings.AuraToolsUi.CreateScroll(window.transform, "StarterDeckRoles");
        var roles = RoleCatalog.GetRoles(true);
        if (roles.Count == 0)
        {
            Settings.AuraToolsUi.AddText(content, "未扫描到可配置角色。", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
            return;
        }

        foreach (var role in roles)
        {
            CreateRoleRow(content, window.transform, role);
        }
    }

    private static void CreateRoleRow(Transform parent, Transform overlayParent, RoleInfo role)
    {
        var row = CreateRow(parent, "Role-" + role.Id, Settings.AuraToolsUi.RoleRowHeight);
        Settings.AuraToolsUi.AddText(row.transform, role.DisplayName, Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 0f, 220f);

        var resolved = AuraToolsStarterDeckRuntime.ResolveEffectiveProfileForPreview(role.Id);
        var status = resolved == null
            ? "生效：无完整卡组"
            : "生效：" + resolved.Profile.DisplayName;
        Settings.AuraToolsUi.AddText(row.transform, status, Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(row.transform, "候选", () => ShowProfilePicker(overlayParent, role), 82f, 34f);
        Settings.AuraToolsUi.AddButton(row.transform, "编辑本地", () => AuraToolsStarterDeckEditor.ShowRole(overlayParent, role.Id, role.DisplayName), 94f, 34f);
        if (AuraToolsConfigService.MatchExperience.StarterDeck.Roles.ContainsKey(role.Id))
        {
            Settings.AuraToolsUi.AddButton(row.transform, "删除本地", () =>
            {
                AuraToolsStarterDeckRuntime.DeleteRoleProfileSettings(role.Id);
                SetHint("已删除 " + role.DisplayName + " 的 AuraTools 本地卡组。");
            }, 94f, 34f);
        }
    }

    private static void ShowProfilePicker(Transform parent, RoleInfo role)
    {
        new StarterDeckProfilePickerSession(parent, role).Show();
    }

    private sealed class StarterDeckProfilePickerSession
    {
        private readonly Transform parent;
        private readonly RoleInfo role;
        private Transform? content;
        private Transform? overlayParent;
        private Text? localHintText;

        public StarterDeckProfilePickerSession(Transform parent, RoleInfo role)
        {
            this.parent = parent;
            this.role = role;
        }

        public void Show()
        {
            var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.StarterDeckProfilePicker", parent, "选择开局卡组 - " + role.DisplayName);
            overlayParent = window.transform;
            var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
            Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
            var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
            toolbarLayout.spacing = 8f;
            toolbarLayout.childControlWidth = true;
            toolbarLayout.childControlHeight = true;
            toolbarLayout.childForceExpandWidth = false;
            var isGlobalMode = AuraToolsStarterDeckRuntime.IsGlobalModeEnabled();
            Settings.AuraToolsUi.AddText(
                toolbar.transform,
                isGlobalMode ? "当前为全局模式：本页选择会保存，但只在切回按角色后生效。" : "当前为按角色模式：绿色项会用于该角色开局。",
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                isGlobalMode ? Settings.AuraToolsUi.WarningText : Settings.AuraToolsUi.SuccessText,
                Settings.AuraToolsUi.TextMinHeight,
                0f,
                360f);
            localHintText = Settings.AuraToolsUi.AddText(toolbar.transform, "同一角色可存在多套候选；默认优先角色所属 MOD 的只读注册 Profile。", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddButton(toolbar.transform, "恢复自动", () =>
            {
                AuraToolsStarterDeckRuntime.ClearSelectedProfileForRole(role.Id);
                RefreshProfiles();
                SetLocalHint("已恢复 " + role.DisplayName + " 的自动选择。", Settings.AuraToolsUi.SuccessText);
            }, 96f);

            content = Settings.AuraToolsUi.CreateScroll(window.transform, "StarterDeckProfiles");
            RefreshProfiles();
        }

        private void RefreshProfiles()
        {
            if (content == null)
            {
                return;
            }

            Settings.AuraToolsUi.ClearChildren(content);
            var profiles = AuraToolsStarterDeckRuntime.BuildCandidateProfilesForRole(role.Id);
            if (profiles.Count == 0)
            {
                Settings.AuraToolsUi.AddText(content, "暂无可用候选。可以先编辑本地角色卡组，或等待角色 MOD 注册 Profile。", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
                return;
            }

            foreach (var profile in profiles)
            {
                CreateProfileRow(content, profile);
            }
        }

        private void CreateProfileRow(Transform parent, StarterDeckProfile profile)
        {
            var row = CreateRow(parent, "Profile-" + profile.ProfileId, Settings.AuraToolsUi.DataRowHeight);
            var isGlobalMode = AuraToolsStarterDeckRuntime.IsGlobalModeEnabled();
            var selectedProfileId = AuraToolsStarterDeckRuntime.ConfiguredSelectedProfileIdForRole(role.Id);
            var isConfiguredSelected = AuraToolsStarterDeckRuntime.ProfileMatchesId(profile, selectedProfileId);
            var effective = AuraToolsStarterDeckRuntime.ResolveEffectiveProfileForPreview(role.Id);
            var isEffective = effective != null && AuraToolsStarterDeckRuntime.ProfileMatchesId(profile, effective.Profile.QualifiedProfileId);
            var highlighted = isConfiguredSelected || isEffective;
            var status = ProfileSelectionStatus(isGlobalMode, isConfiguredSelected, isEffective);
            var rowImage = row.GetComponent<Image>();
            if (highlighted && rowImage != null)
            {
                rowImage.color = Settings.AuraToolsUi.ActiveRow;
            }

            var titleColor = highlighted ? Settings.AuraToolsUi.SuccessText : Settings.AuraToolsUi.Text;
            var detailColor = highlighted ? Settings.AuraToolsUi.SuccessText : Settings.AuraToolsUi.MutedText;
            Settings.AuraToolsUi.AddText(row.transform, profile.DisplayName + status, Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, titleColor, Settings.AuraToolsUi.TextMinHeight, 0f, 260f);
            Settings.AuraToolsUi.AddText(row.transform, DescribeSource(profile) + " / " + DeckStatus(profile) + "\n" + profile.QualifiedProfileId, Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, detailColor, Settings.AuraToolsUi.TextMinHeight, 1f);
            var enableButton = Settings.AuraToolsUi.AddButton(row.transform, isConfiguredSelected ? "已选择" : "启用此卡组", () =>
            {
                AuraToolsStarterDeckRuntime.SelectProfileForRole(role.Id, profile.QualifiedProfileId);
                RefreshProfiles();
                SetLocalHint(
                    isGlobalMode
                        ? "已为 " + role.DisplayName + " 保存选择：" + profile.DisplayName + "。当前是全局模式，切回按角色后生效。"
                        : "已启用 " + role.DisplayName + " 的卡组：" + profile.DisplayName,
                    Settings.AuraToolsUi.SuccessText);
            }, 92f, 34f);
            enableButton.interactable = !isConfiguredSelected;

            if (profile.SourceKind == StarterDeckProfileSourceKind.Registered)
            {
                Settings.AuraToolsUi.AddButton(row.transform, "复制为本角色", () =>
                {
                    if (overlayParent != null)
                    {
                        AuraToolsStarterDeckEditor.CopyRegisteredToRole(overlayParent, role.Id, role.DisplayName, profile);
                    }

                    RefreshProfiles();
                    SetLocalHint("已复制只读 Profile 到本地，并设为该角色选择。", Settings.AuraToolsUi.SuccessText);
                }, 104f, 34f);
                return;
            }

            if (string.Equals(profile.QualifiedProfileId, AuraToolsStarterDeckRuntime.LocalGlobalProfileId(), StringComparison.OrdinalIgnoreCase))
            {
                if (overlayParent != null)
                {
                    Settings.AuraToolsUi.AddButton(row.transform, "编辑全局", () => AuraToolsStarterDeckEditor.ShowGlobal(overlayParent), 82f, 34f);
                }

                return;
            }

            if (overlayParent != null)
            {
                Settings.AuraToolsUi.AddButton(row.transform, "编辑本角色", () => AuraToolsStarterDeckEditor.ShowRole(overlayParent, role.Id, role.DisplayName), 92f, 34f);
            }

            Settings.AuraToolsUi.AddButton(row.transform, "删除", () =>
            {
                AuraToolsStarterDeckRuntime.DeleteRoleProfileSettings(role.Id);
                RefreshProfiles();
                SetLocalHint("已删除 " + role.DisplayName + " 的 AuraTools 本地卡组。", Settings.AuraToolsUi.WarningText);
            }, 78f, 34f);
        }

        private void SetLocalHint(string message, Color? color = null)
        {
            if (localHintText != null)
            {
                localHintText.text = message;
                localHintText.color = color ?? Settings.AuraToolsUi.MutedText;
            }
        }
    }

    private static GameObject CreateRow(Transform parent, string name, float height)
    {
        var row = Settings.AuraToolsUi.CreateLayout(name, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, height);
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static string DescribeSource(StarterDeckProfile profile)
    {
        if (profile.SourceKind == StarterDeckProfileSourceKind.Registered)
        {
            return "MOD只读/" + profile.OwnerModId;
        }

        return string.Equals(profile.QualifiedProfileId, AuraToolsStarterDeckRuntime.LocalGlobalProfileId(), StringComparison.OrdinalIgnoreCase)
            ? "AuraTools全局 fallback"
            : "AuraTools本角色";
    }

    private static string DescribeReason(string reason)
    {
        return reason switch
        {
            StarterDeckProfileResolutionReasons.Selected => "显式选择",
            StarterDeckProfileResolutionReasons.LocalRole => "本角色优先",
            StarterDeckProfileResolutionReasons.RoleOwnerRegistered => "角色MOD推荐",
            StarterDeckProfileResolutionReasons.LocalGlobal => "全局回退",
            _ => reason
        };
    }

    private static string DeckStatus(StarterDeckProfile profile)
    {
        var validation = StarterDeckArbiterRuntime.ValidateProfile(profile, null, AuraToolsStarterDeckRuntime.BuildDeckFromProfile);
        return validation.DeckCount + "/" + validation.DeckSize + (validation.Complete ? "" : " " + validation.Summary);
    }

    private static string ProfileSelectionStatus(bool isGlobalMode, bool isConfiguredSelected, bool isEffective)
    {
        if (isGlobalMode && isConfiguredSelected)
        {
            return "  [已选择，按角色模式生效]";
        }

        if (isConfiguredSelected)
        {
            return "  [当前启用]";
        }

        if (!isGlobalMode && isEffective)
        {
            return "  [当前自动生效]";
        }

        return "";
    }

    private static void SetHint(string message, Color? color = null)
    {
        if (hintText != null)
        {
            hintText.text = message;
            hintText.color = color ?? Settings.AuraToolsUi.MutedText;
        }
    }
}
