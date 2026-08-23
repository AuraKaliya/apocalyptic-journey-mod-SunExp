using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckSettingsPage
{
    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.CustomStartSettings",
            parent,
            "自定义开局");
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Normalize();
        var content = AuraToolsUi.CreateScroll(window.transform, "CustomStartSettings");

        var modeRow = CreateInlineRow(content, "Mode");
        AuraToolsUi.AddText(
            modeRow.transform,
            "配置范围",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        Button? globalButton = null;
        Button? roleButton = null;
        void RefreshModeButtons()
        {
            if (globalButton != null)
            {
                globalButton.interactable = settings.Mode != StarterDeckModes.Global;
                AuraToolsUi.SetButtonLabel(globalButton, settings.Mode == StarterDeckModes.Global ? "全局配置 ✓" : "全局配置");
            }
            if (roleButton != null)
            {
                roleButton.interactable = settings.Mode != StarterDeckModes.RoleSpecific;
                AuraToolsUi.SetButtonLabel(roleButton, settings.Mode == StarterDeckModes.RoleSpecific ? "按角色配置 ✓" : "按角色配置");
            }
        }
        globalButton = AuraToolsUi.AddButton(modeRow.transform, "全局配置", () =>
        {
            settings.Mode = StarterDeckModes.Global;
            AuraToolsConfigService.SaveStarterDeck();
            RefreshModeButtons();
        }, 118f);
        roleButton = AuraToolsUi.AddButton(modeRow.transform, "按角色配置", () =>
        {
            settings.Mode = StarterDeckModes.RoleSpecific;
            AuraToolsConfigService.SaveStarterDeck();
            RefreshModeButtons();
        }, 126f);
        RefreshModeButtons();

        var globalRow = CreateInlineRow(content, "Global");
        var cards = settings.GlobalProfile.CardIds.Count;
        var relics = settings.GlobalProfile.RelicIds.Count;
        AuraToolsUi.AddText(
            globalRow.transform,
            "全局　卡牌 " + cards + "/15"
            + (cards == 0 ? "（游戏默认）" : "")
            + "　遗物 " + relics + "/6"
            + (relics == 0 ? "（开局清空）" : ""),
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            globalRow.transform,
            "编辑全局",
            () => AuraToolsStarterDeckEditor.ShowGlobal(window.transform),
            104f);

        var roleRow = CreateInlineRow(content, "Roles");
        AuraToolsUi.AddText(
            roleRow.transform,
            "角色本地覆盖　" + settings.Roles.Count,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            roleRow.transform,
            "管理角色",
            () => AuraToolsStarterDeckRoleManager.Show(window.transform),
            104f);
    }

    private static GameObject CreateInlineRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout("CustomStart-" + name, parent);
        AuraUiStableId.Assign(row, "custom-start-settings." + name);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.InlineRowHeight);
        AuraToolsUi.AddImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 2, 2);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }
}
