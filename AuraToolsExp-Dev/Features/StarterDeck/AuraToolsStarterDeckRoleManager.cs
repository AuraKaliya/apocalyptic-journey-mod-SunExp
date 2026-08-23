using System;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckRoleManager
{
    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay(
            "AuraTools.CustomStartRoleManager",
            parent,
            "【世界推演】按角色自定义开局");
        var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddText(
            toolbar.transform,
            "",
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "刷新", () => Show(parent), 78f);

        var content = Settings.AuraToolsUi.CreateScroll(window.transform, "CustomStartRoles");
        var roles = RoleCatalog.GetRoles(true);
        if (roles.Count == 0)
        {
            Settings.AuraToolsUi.AddText(
                content,
                "未检索到可配置角色。",
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            return;
        }

        foreach (var role in roles)
        {
            CreateRoleRow(content, window.transform, role);
        }
    }

    private static void CreateRoleRow(Transform parent, Transform overlayParent, RoleInfo role)
    {
        var row = Settings.AuraToolsUi.CreateLayout("Role-" + role.Id, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, Settings.AuraToolsUi.RoleRowHeight);
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddText(
            row.transform,
            role.DisplayName,
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.Text,
            Settings.AuraToolsUi.TextMinHeight,
            0f,
            210f);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Roles.TryGetValue(RoleCatalog.NormalizeRoleId(role.Id), out var local);
        var resolved = AuraToolsStarterDeckRuntime.ResolveEffectiveLoadout(role.Id);
        var cardStatus = local == null || local.InheritCards
            ? "卡牌：继承全局 " + resolved.CardIds.Count + "/15"
            : local.CardIds.Count == 0 ? "卡牌：游戏默认" : "卡牌：本角色 " + local.CardIds.Count + "/15";
        var relicStatus = local == null || local.InheritRelics
            ? "遗物：继承全局 " + resolved.RelicIds.Count + "/6"
            : "遗物：本角色 " + local.RelicIds.Count + "/6";
        Settings.AuraToolsUi.AddText(
            row.transform,
            cardStatus + "　" + relicStatus,
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(
            row.transform,
            "编辑",
            () => AuraToolsStarterDeckEditor.ShowRole(overlayParent, role.Id, role.DisplayName),
            76f,
            34f);
        if (local != null)
        {
            Settings.AuraToolsUi.AddButton(
                row.transform,
                "恢复全局",
                () =>
                {
                    AuraToolsStarterDeckRuntime.DeleteRoleSettings(role.Id);
                    UnityEngine.Object.Destroy(row);
                },
                92f,
                34f);
        }
    }
}
