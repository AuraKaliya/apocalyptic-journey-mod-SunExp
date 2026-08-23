using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CardVisual;

public static class AuraToolsCardVisualEditor
{
    private static int themeIndex;
    private static int skinIndex;
    private static int themeModeIndex;
    private static int effectIndex;
    private static int effectModeIndex;
    private static string themeQuery = "";
    private static string themeTarget = "";
    private static string effectQuery = "";
    private static string effectTarget = "";
    private static Transform? currentContent;
    private static readonly Dictionary<string, Dictionary<string, float>> EffectDrafts =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.CardVisual",
            parent,
            "卡牌视觉",
            Save);
        currentContent = AuraToolsUi.CreateScroll(
            window.transform,
            "CardVisualSettings");
        RefreshContent();
    }

    private static void RefreshContent()
    {
        if (currentContent == null)
        {
            return;
        }
        var state = AuraUiViewState.CaptureForContent(currentContent);
        AuraToolsUi.ClearChildren(currentContent);
        AddThemeEditor(currentContent);
        AddEffectEditor(currentContent);
        AddCurrentMappings(currentContent);
        AuraUiViewState.RestoreAfterLayout(
            currentContent,
            state,
            "AuraTools.CardVisual.Rows");
    }

    private static void AddThemeEditor(Transform parent)
    {
        AddSection(parent, "卡框主题");
        var themes = AuraToolsCardVisualRegistry.Themes.ToArray();
        if (themes.Length == 0)
        {
            AddEmptyState(parent, "当前没有可用的卡框主题资源。");
            return;
        }

        themeIndex = ClampIndex(themeIndex, themes.Length);
        var currentTheme = themes[themeIndex];
        var skins = currentTheme.Skins.ToArray();
        skinIndex = ClampIndex(skinIndex, skins.Length);
        var profile = EnsureThemeProfile(currentTheme.ThemeId);

        var resourceRow = Row(parent, "ThemeResource");
        AddFieldLabel(resourceRow.transform, "主题");
        AuraToolsUi.AddSelectButton(
            resourceRow.transform,
            themes.Select(value => value.DisplayName).ToArray(),
            themeIndex,
            value =>
            {
                themeIndex = value;
                skinIndex = 0;
                RefreshContent();
            },
            190f,
            AuraToolsUi.StandardButtonHeight);
        AuraToolsUi.AddSelectButton(
            resourceRow.transform,
            skins.Select(value => value.DisplayName).ToArray(),
            skinIndex,
            value => skinIndex = value,
            170f,
            AuraToolsUi.StandardButtonHeight);
        AuraToolsUi.AddToggle(resourceRow.transform, profile.Enabled, value =>
        {
            profile.Enabled = value;
            Save();
        });
        AuraToolsUi.AddText(
            resourceRow.transform,
            "启用",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            52f);
        var mode = ModeValue(themeModeIndex);
        var options = AuraToolsCardVisualTargetCatalog.Options(mode);
        Text? previewText = null;
        Button? applyButton = null;
        var targetRow = Row(parent, "ThemeTarget");
        AddFieldLabel(targetRow.transform, "应用范围");
        AuraToolsUi.AddSelectButton(
            targetRow.transform,
            ModeLabels,
            themeModeIndex,
            value =>
            {
                themeModeIndex = value;
                themeQuery = "";
                themeTarget = "";
                RefreshContent();
            },
            126f,
            AuraToolsUi.StandardButtonHeight);
        ToolboxSearchPickerV3.Create(
            targetRow.transform,
            options,
            themeQuery,
            themeTarget,
            value => themeQuery = value,
            value =>
            {
                themeTarget = value;
                if (previewText != null)
                {
                    previewText.text = AuraToolsCardVisualTargetCatalog
                        .SelectionSummary(mode, themeTarget);
                    previewText.color = HasMatches(mode, themeTarget)
                        ? AuraToolsUi.SuccessText
                        : AuraToolsUi.MutedText;
                }
                if (applyButton != null)
                {
                    applyButton.interactable = HasMatches(mode, themeTarget);
                }
            });

        var actionRow = Row(parent, "ThemeAction");
        previewText = AuraToolsUi.AddText(
            actionRow.transform,
            AuraToolsCardVisualTargetCatalog.SelectionSummary(mode, themeTarget),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            string.IsNullOrWhiteSpace(themeTarget)
                ? AuraToolsUi.MutedText
                : AuraToolsUi.SuccessText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            actionRow.transform,
            "恢复主题预设",
            () =>
            {
                AuraToolsCardVisualRuntime.ResetThemePreset(
                    themes[ClampIndex(themeIndex, themes.Length)].ThemeId);
                RefreshContent();
            },
            124f,
            AuraToolsUi.StandardButtonHeight);
        applyButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "应用卡框",
            () =>
            {
                var theme = themes[ClampIndex(themeIndex, themes.Length)];
                var availableSkins = theme.Skins.ToArray();
                if (availableSkins.Length == 0)
                {
                    return;
                }
                AuraToolsCardVisualRuntime.ApplyThemeSelection(
                    theme.ThemeId,
                    availableSkins[ClampIndex(skinIndex, availableSkins.Length)].SkinId,
                    mode,
                    themeTarget);
                RefreshContent();
            },
            104f,
            AuraToolsUi.StandardButtonHeight);
        applyButton.interactable = HasMatches(mode, themeTarget);
    }

    private static void AddEffectEditor(Transform parent)
    {
        AddSection(parent, "动态效果");
        var effects = AuraToolsCardVisualRegistry.Effects.ToArray();
        if (effects.Length == 0)
        {
            AddEmptyState(parent, "当前没有可用的动态效果资源。");
            return;
        }

        effectIndex = ClampIndex(effectIndex, effects.Length);
        var selected = effects[effectIndex];
        var resourceRow = Row(parent, "EffectResource");
        AddFieldLabel(resourceRow.transform, "效果");
        AuraToolsUi.AddSelectButton(
            resourceRow.transform,
            effects.Select(value => value.DisplayName).ToArray(),
            effectIndex,
            value =>
            {
                effectIndex = value;
                RefreshContent();
            },
            210f,
            AuraToolsUi.StandardButtonHeight);
        AuraToolsUi.AddText(
            resourceRow.transform,
            "先选择范围，再应用、停用或恢复随工具提供的默认效果",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var mode = ModeValue(effectModeIndex);
        var options = AuraToolsCardVisualTargetCatalog.Options(mode);
        Text? previewText = null;
        Button? applyButton = null;
        Button? restoreButton = null;
        Button? defaultButton = null;
        var targetRow = Row(parent, "EffectTarget");
        AddFieldLabel(targetRow.transform, "应用范围");
        AuraToolsUi.AddSelectButton(
            targetRow.transform,
            ModeLabels,
            effectModeIndex,
            value =>
            {
                effectModeIndex = value;
                effectQuery = "";
                effectTarget = "";
                RefreshContent();
            },
            126f,
            AuraToolsUi.StandardButtonHeight);
        ToolboxSearchPickerV3.Create(
            targetRow.transform,
            options,
            effectQuery,
            effectTarget,
            value => effectQuery = value,
            value =>
            {
                effectTarget = value;
                var hasMatches = HasMatches(mode, effectTarget);
                if (previewText != null)
                {
                    previewText.text = AuraToolsCardVisualTargetCatalog
                        .SelectionSummary(mode, effectTarget);
                    previewText.color = hasMatches
                        ? AuraToolsUi.SuccessText
                        : AuraToolsUi.MutedText;
                }
                if (applyButton != null)
                {
                    applyButton.interactable = hasMatches;
                }
                if (restoreButton != null)
                {
                    restoreButton.interactable = hasMatches;
                }
                if (defaultButton != null)
                {
                    defaultButton.interactable = hasMatches;
                }
            });

        var actionRow = Row(parent, "EffectAction");
        previewText = AuraToolsUi.AddText(
            actionRow.transform,
            AuraToolsCardVisualTargetCatalog.SelectionSummary(mode, effectTarget),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            string.IsNullOrWhiteSpace(effectTarget)
                ? AuraToolsUi.MutedText
                : AuraToolsUi.SuccessText,
            AuraToolsUi.TextMinHeight,
            1f);
        applyButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "应用效果",
            () =>
            {
                var effect = effects[ClampIndex(effectIndex, effects.Length)];
                var draft = Draft(effect);
                AuraToolsCardVisualRuntime.ApplyDynamicEffectSelection(
                    AuraToolsCardVisualRuntime.SelectCards(mode, effectTarget),
                    effect.EffectId,
                    draft);
                RefreshContent();
            },
            88f,
            AuraToolsUi.StandardButtonHeight);
        restoreButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "停用效果",
            () =>
            {
                AuraToolsCardVisualRuntime.ApplyDynamicEffectSelection(
                    AuraToolsCardVisualRuntime.SelectCards(mode, effectTarget),
                    "");
                RefreshContent();
            },
            88f,
            AuraToolsUi.StandardButtonHeight);
        defaultButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "恢复默认",
            () =>
            {
                AuraToolsCardVisualRuntime.RestoreDynamicEffectDefaults(
                    AuraToolsCardVisualRuntime.SelectCards(mode, effectTarget));
                RefreshContent();
            },
            88f,
            AuraToolsUi.StandardButtonHeight);
        applyButton.interactable = HasMatches(mode, effectTarget);
        restoreButton.interactable = applyButton.interactable;
        defaultButton.interactable = applyButton.interactable;

        AddEffectParameters(parent, selected);
    }

    private static void AddEffectParameters(
        Transform parent,
        CardDynamicEffectDefinition effect)
    {
        var parameters = effect.ExposedParameters
            .OrderBy(value => value.Value.Order)
            .ThenBy(value => value.Value.DisplayName, StringComparer.Ordinal)
            .ToArray();
        if (parameters.Length == 0)
        {
            return;
        }

        AddSection(parent, "效果参数");
        var draft = Draft(effect);
        foreach (var parameter in parameters)
        {
            var parameterRow = Row(parent, "EffectParameter-" + parameter.Key);
            AuraToolsUi.AddText(
                parameterRow.transform,
                parameter.Value.DisplayName,
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                0f,
                180f);
            AuraToolsUi.AddInput(
                parameterRow.transform,
                FormatParameter(draft[parameter.Key], parameter.Value),
                value =>
                {
                    if (float.TryParse(
                            value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsed))
                    {
                        draft[parameter.Key] = NormalizeParameter(
                            parsed,
                            parameter.Value);
                    }
                },
                110f,
                AuraToolsUi.StandardButtonHeight);
            AuraToolsUi.AddText(
                parameterRow.transform,
                string.IsNullOrWhiteSpace(parameter.Value.Unit)
                    ? "可调范围 "
                      + FormatParameter(parameter.Value.Min, parameter.Value)
                      + " ～ "
                      + FormatParameter(parameter.Value.Max, parameter.Value)
                    : parameter.Value.Unit,
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
        }
    }

    private static void AddCurrentMappings(Transform parent)
    {
        AddSection(parent, "当前配置");
        var count = 0;
        foreach (var theme in AuraToolsConfigService.CardVisual.Themes
                     .OrderBy(value => value.Key))
        {
            foreach (var mapping in theme.Value.Cards
                         .OrderBy(
                             value => AuraToolsPlayerDisplay.CardName(value.Key),
                             StringComparer.Ordinal))
            {
                count++;
                var row = Row(parent, "Mapping");
                AuraToolsUi.AddText(
                    row.transform,
                    AuraToolsPlayerDisplay.CardName(mapping.Key),
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.Text,
                    AuraToolsUi.TextMinHeight,
                    1f);
                AuraToolsUi.AddText(
                    row.transform,
                    (AuraToolsCardVisualRegistry.Theme(theme.Key)?.DisplayName
                     ?? "卡框主题")
                    + " / "
                    + (AuraToolsCardVisualRegistry.Skin(theme.Key, mapping.Value)
                           ?.DisplayName
                       ?? "默认卡框"),
                    AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft,
                    AuraToolsUi.MutedText,
                    AuraToolsUi.TextMinHeight,
                    0f,
                    230f);
                var themeId = theme.Key;
                var cardId = mapping.Key;
                AuraToolsUi.AddButton(row.transform, "移除", () =>
                {
                    AuraToolsCardVisualRuntime.RemoveThemeCard(themeId, cardId);
                    RefreshContent();
                }, 72f, AuraToolsUi.CompactButtonHeight);
            }
        }

        var effectiveEffects = AuraToolsCardVisualRuntime.EffectiveDynamicEffects();
        foreach (var mapping in effectiveEffects
                     .OrderBy(
                         value => AuraToolsPlayerDisplay.CardName(value.Key),
                         StringComparer.Ordinal))
        {
            count++;
            var row = Row(parent, "EffectMapping");
            AuraToolsUi.AddText(
                row.transform,
                AuraToolsPlayerDisplay.CardName(mapping.Key),
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddText(
                row.transform,
                (AuraToolsCardVisualRegistry.Effect(mapping.Value.EffectId)
                    ?.DisplayName
                 ?? "卡牌效果")
                + (AuraToolsConfigService.CardVisual.DynamicEffectOverrides.ContainsKey(mapping.Key)
                    ? "（本地设置）"
                    : "（工具默认）"),
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                0f,
                230f);
            var cardId = mapping.Key;
            var hasOverride = AuraToolsConfigService.CardVisual.DynamicEffectOverrides.ContainsKey(cardId);
            AuraToolsUi.AddButton(row.transform, hasOverride ? "恢复默认" : "停用", () =>
            {
                if (hasOverride)
                    AuraToolsCardVisualRuntime.RestoreDynamicEffectDefault(cardId);
                else
                    AuraToolsCardVisualRuntime.SetDynamicEffect(cardId, "");
                RefreshContent();
            }, 88f, AuraToolsUi.CompactButtonHeight);
        }

        foreach (var mapping in AuraToolsConfigService.CardVisual.DynamicEffectOverrides
                     .Where(value => !value.Value.Enabled)
                     .OrderBy(value => AuraToolsPlayerDisplay.CardName(value.Key), StringComparer.Ordinal))
        {
            count++;
            var row = Row(parent, "DisabledEffectMapping");
            AuraToolsUi.AddText(
                row.transform,
                AuraToolsPlayerDisplay.CardName(mapping.Key),
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AuraToolsUi.AddText(
                row.transform,
                "默认动态效果已停用",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                0f,
                230f);
            var cardId = mapping.Key;
            AuraToolsUi.AddButton(row.transform, "恢复默认", () =>
            {
                AuraToolsCardVisualRuntime.RestoreDynamicEffectDefault(cardId);
                RefreshContent();
            }, 88f, AuraToolsUi.CompactButtonHeight);
        }

        if (count == 0)
        {
            AddEmptyState(parent, "尚未为任何卡牌设置自定义视觉。");
        }
    }

    private static CardFrameThemeSettings EnsureThemeProfile(string themeId)
    {
        if (!AuraToolsConfigService.CardVisual.Themes.TryGetValue(
                themeId,
                out var profile)
            || profile == null)
        {
            profile = new CardFrameThemeSettings();
            AuraToolsConfigService.CardVisual.Themes[themeId] = profile;
        }
        return profile;
    }

    private static Dictionary<string, float> Draft(
        CardDynamicEffectDefinition effect)
    {
        if (EffectDrafts.TryGetValue(effect.EffectId, out var existing))
        {
            return existing;
        }
        var draft = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var parameter in effect.ExposedParameters)
        {
            var value = effect.Floats.TryGetValue(
                parameter.Key,
                out var configured)
                ? configured
                : parameter.Value.Min;
            draft[parameter.Key] = NormalizeParameter(value, parameter.Value);
        }
        EffectDrafts[effect.EffectId] = draft;
        return draft;
    }

    private static float NormalizeParameter(
        float value,
        CardVisualParameterRange parameter)
    {
        var clamped = Math.Max(parameter.Min, Math.Min(parameter.Max, value));
        var stepped = parameter.Min
                      + (float)Math.Round(
                          (clamped - parameter.Min) / parameter.Step)
                      * parameter.Step;
        return Math.Max(parameter.Min, Math.Min(parameter.Max, stepped));
    }

    private static string FormatParameter(
        float value,
        CardVisualParameterRange parameter)
    {
        return NormalizeParameter(value, parameter).ToString(
            "F" + Math.Max(0, Math.Min(4, parameter.Decimals)),
            CultureInfo.InvariantCulture);
    }

    private static void AddSection(Transform parent, string title)
    {
        var row = AuraToolsUi.CreateSettingsRow(
            parent,
            "CardVisualSection-" + title,
            "card-visual.section." + title,
            AuraToolsUi.SectionHeight,
            padding: new RectOffset(12, 12, 3, 3));
        AuraToolsUi.AddSectionImage(row);
        AuraToolsUi.AddText(
            row.transform,
            title,
            AuraToolsUi.SectionFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            AuraToolsUi.TextMinHeight,
            1f);
    }

    private static void AddEmptyState(Transform parent, string message)
    {
        AuraToolsUi.AddText(
            parent,
            message,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
    }

    private static void AddFieldLabel(
        Transform parent,
        string value,
        float width = 92f)
    {
        AuraToolsUi.AddText(
            parent,
            value,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            width);
    }

    private static GameObject Row(Transform parent, string name)
    {
        return AuraToolsUi.CreateSettingsRow(
            parent,
            "CardVisual-" + name,
            "card-visual.row." + name);
    }

    private static bool HasMatches(string mode, string target)
    {
        return !string.IsNullOrWhiteSpace(target)
               && AuraToolsCardVisualRuntime.SelectCards(mode, target).Count > 0;
    }

    private static int ClampIndex(int index, int count)
    {
        return count <= 0 ? 0 : Math.Max(0, Math.Min(index, count - 1));
    }

    private static string ModeValue(int index)
    {
        return index == 1 ? "pack" : index == 2 ? "rarity" : "card";
    }

    private static readonly string[] ModeLabels =
        { "按卡牌", "按卡包", "按稀有度" };

    private static void Save()
    {
        AuraToolsConfigService.SaveCardVisual();
        AuraToolsCardVisualRuntime.Reconfigure();
    }
}
