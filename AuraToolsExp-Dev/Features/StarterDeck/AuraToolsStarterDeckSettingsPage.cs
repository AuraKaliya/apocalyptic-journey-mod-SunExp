using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckSettingsPage
{
    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.StarterDeckSettings",
            parent,
            "开局卡组设置");
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        var content = AuraToolsUi.CreateScroll(window.transform, "StarterDeckSettings");
        var modeRow = CreateInlineRow(content, "Mode");
        var modeText = AuraToolsUi.AddText(
            modeRow.transform,
            settings.Mode == StarterDeckModes.RoleSpecific
                ? "当前模式：按角色"
                : "当前模式：全局",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        Button? modeButton = null;
        modeButton = AuraToolsUi.AddButton(
            modeRow.transform,
            settings.Mode == StarterDeckModes.RoleSpecific ? "切到全局" : "切到按角色",
            () =>
            {
                settings.Mode = settings.Mode == StarterDeckModes.RoleSpecific
                    ? StarterDeckModes.Global
                    : StarterDeckModes.RoleSpecific;
                AuraToolsConfigService.SaveMatchExperience();
                modeText.text = settings.Mode == StarterDeckModes.RoleSpecific
                    ? "当前模式：按角色"
                    : "当前模式：全局";
                AuraToolsUi.SetButtonLabel(
                    modeButton,
                    settings.Mode == StarterDeckModes.RoleSpecific
                        ? "切到全局"
                        : "切到按角色");
            },
            112f);

        var globalRow = CreateInlineRow(content, "Global");
        AuraToolsUi.AddText(
            globalRow.transform,
            "全局卡组：" + settings.GlobalProfile.CardIds.Count
            + "/" + settings.GlobalProfile.DeckSize + " 张",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            globalRow.transform,
            "编辑全局卡组",
            () => AuraToolsStarterDeckEditor.ShowGlobal(window.transform),
            124f);

        var roleRow = CreateInlineRow(content, "Roles");
        AuraToolsUi.AddText(
            roleRow.transform,
            "本地角色卡组：" + settings.Roles.Count
            + "；MOD 注册 Profile："
            + StarterDeckArbiterRuntime.GetRegisteredProfiles(AuraToolsIds.ModId).Count,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            roleRow.transform,
            "管理角色卡组",
            () => AuraToolsStarterDeckRoleManager.Show(window.transform),
            124f);
        AuraToolsUi.AddText(
            content,
            "没有本地角色卡组时，会优先使用角色所属 MOD 注册的推荐 Profile，再回退到全局卡组。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
    }

    private static GameObject CreateInlineRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout("StarterDeck-" + name, parent);
        AuraUiStableId.Assign(row, "starter-deck-settings." + name);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.InlineRowHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }
}
