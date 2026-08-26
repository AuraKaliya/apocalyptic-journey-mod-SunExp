using System;
using System.Globalization;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Cg;

public static class AuraToolsEventCgSettingsPage
{
    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.EventCgSettings",
            parent,
            "事件 CG 配置",
            Save);
        var content = AuraToolsUi.CreateScroll(window.transform, "EventCgSettings");
        var settings = AuraToolsConfigService.SkillCg.EventCg;
        settings.Normalize();

        AddToggleRow(content, "联机同步", settings.SyncRemote, value =>
        {
            settings.SyncRemote = value;
            Save();
        });
        AddToggleRow(content, "特殊战斗开场", settings.SpecialOpeningEnabled, value =>
        {
            settings.SpecialOpeningEnabled = value;
            Save();
        });
        AddToggleRow(content, "特殊战斗胜利", settings.SpecialVictoryEnabled, value =>
        {
            settings.SpecialVictoryEnabled = value;
            Save();
        });
        AddToggleRow(content, "战斗失败", settings.BattleDefeatEnabled, value =>
        {
            settings.BattleDefeatEnabled = value;
            Save();
        });
        AddToggleRow(content, "冒险结算", settings.AdventureSettlementEnabled, value =>
        {
            settings.AdventureSettlementEnabled = value;
            Save();
        });

        AddTextRow(
            content,
            "特殊战斗 ID",
            string.Join(", ", settings.SpecialBattleIds),
            value =>
            {
                settings.SpecialBattleIds = (value ?? "")
                    .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();
                Save();
            },
            flexibleWidth: true);
        AddTextRow(
            content,
            "背景资源",
            settings.BackgroundResource,
            value =>
            {
                settings.BackgroundResource = value;
                Save();
            },
            flexibleWidth: true);

        AddNumberRow(content, "淡入（秒）", settings.FadeIn, 0f, 5f, value => settings.FadeIn = value);
        AddNumberRow(content, "停留（秒）", settings.Hold, 0.1f, 30f, value => settings.Hold = value);
        AddNumberRow(content, "淡出（秒）", settings.FadeOut, 0f, 5f, value => settings.FadeOut = value);
        AddIntegerRow(content, "画面宽度", settings.BaseWidth, 1, 8192, value => settings.BaseWidth = value);
        AddIntegerRow(content, "画面高度", settings.BaseHeight, 1, 8192, value => settings.BaseHeight = value);
    }

    private static void AddToggleRow(
        Transform parent,
        string label,
        bool value,
        Action<bool> changed)
    {
        var row = CreateRow(parent, "Toggle-" + label);
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

    private static void AddTextRow(
        Transform parent,
        string label,
        string value,
        Action<string> changed,
        bool flexibleWidth)
    {
        var row = CreateRow(parent, "Text-" + label);
        AddLabel(row.transform, label);
        AuraToolsUi.AddInput(
            row.transform,
            value ?? "",
            changed,
            280f,
            AuraToolsUi.StandardButtonHeight,
            flexibleWidth);
    }

    private static void AddNumberRow(
        Transform parent,
        string label,
        float value,
        float minimum,
        float maximum,
        Action<float> changed)
    {
        var row = CreateRow(parent, "Number-" + label);
        AddLabel(row.transform, label);
        var input = AuraToolsUi.AddInput(
            row.transform,
            value.ToString("0.###", CultureInfo.InvariantCulture),
            raw =>
            {
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    changed(Math.Max(minimum, Math.Min(maximum, parsed)));
                    Save();
                }
            },
            120f,
            AuraToolsUi.StandardButtonHeight);
        input.contentType = InputField.ContentType.DecimalNumber;
    }

    private static void AddIntegerRow(
        Transform parent,
        string label,
        int value,
        int minimum,
        int maximum,
        Action<int> changed)
    {
        var row = CreateRow(parent, "Integer-" + label);
        AddLabel(row.transform, label);
        var input = AuraToolsUi.AddInput(
            row.transform,
            value.ToString(CultureInfo.InvariantCulture),
            raw =>
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    changed(Math.Max(minimum, Math.Min(maximum, parsed)));
                    Save();
                }
            },
            120f,
            AuraToolsUi.StandardButtonHeight);
        input.contentType = InputField.ContentType.IntegerNumber;
    }

    private static void AddLabel(Transform parent, string label)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            116f);
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraUiStableId.Assign(row, "event-cg." + name);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.InlineRowHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static void Save()
    {
        AuraToolsConfigService.SaveEventCg();
    }
}
