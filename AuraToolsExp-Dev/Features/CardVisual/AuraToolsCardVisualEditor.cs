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
    private static int modeIndex;
    private static int effectIndex;
    private static int effectModeIndex;
    private static string selector = "";
    private static Transform? currentContent;
    private static readonly Dictionary<string, Dictionary<string, float>> EffectDrafts =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay("AuraTools.CardVisual", parent, "卡牌视觉", Save);
        currentContent = AuraToolsUi.CreateScroll(window.transform, "CardVisualSettings");
        RefreshContent();
    }

    private static void RefreshContent()
    {
        if (currentContent == null) return;
        var state = AuraUiViewState.CaptureForContent(currentContent);
        AuraToolsUi.ClearChildren(currentContent);
        AddThemeEditor(currentContent);
        AddEffectEditor(currentContent);
        AddCurrentMappings(currentContent);
        AuraUiViewState.RestoreAfterLayout(currentContent, state, "AuraTools.CardVisual.Rows");
    }

    private static void AddThemeEditor(Transform parent)
    {
        var themes = AuraToolsCardVisualRegistry.Themes.ToArray();
        if (themes.Length == 0) return;
        themeIndex = Math.Max(0, Math.Min(themeIndex, themes.Length - 1));
        var themeRow = Row(parent, "Theme");
        AuraToolsUi.AddText(themeRow, "卡框主题", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 100f);
        AuraToolsUi.AddSelectButton(themeRow, themes.Select(value => value.DisplayName).ToArray(), themeIndex,
            value => { themeIndex = value; skinIndex = 0; RefreshContent(); }, 190f);
        var currentTheme = themes[themeIndex];
        var skins = currentTheme.Skins.ToArray();
        skinIndex = Math.Max(0, Math.Min(skinIndex, skins.Length - 1));
        AuraToolsUi.AddSelectButton(themeRow, skins.Select(value => value.DisplayName).ToArray(), skinIndex,
            value => skinIndex = value, 170f);
        if (!AuraToolsConfigService.CardVisual.Themes.TryGetValue(currentTheme.ThemeId, out var profile) || profile == null)
        {
            profile = new CardFrameThemeSettings();
            AuraToolsConfigService.CardVisual.Themes[currentTheme.ThemeId] = profile;
        }
        AuraToolsUi.AddToggle(themeRow, profile.Enabled, value =>
        {
            profile.Enabled = value;
            Save();
        });
        AuraToolsUi.AddButton(themeRow, "恢复映射预设", () =>
        {
            AuraToolsCardVisualRuntime.ResetThemePreset(themes[Math.Max(0, Math.Min(themeIndex, themes.Length - 1))].ThemeId);
            RefreshContent();
        }, 130f);

        var batchRow = Row(parent, "Batch");
        var modes = new[] { "按卡牌", "按卡包", "按稀有度" };
        AuraToolsUi.AddSelectButton(batchRow, modes, modeIndex, value => modeIndex = value, 130f);
        AuraToolsUi.AddInput(batchRow, selector, value => selector = value.Trim(), 520f);
        AuraToolsUi.AddButton(batchRow, "应用", () =>
        {
            var theme = themes[Math.Max(0, Math.Min(themeIndex, themes.Length - 1))];
            var availableSkins = theme.Skins.ToArray();
            if (availableSkins.Length == 0) return;
            var mode = modeIndex == 0 ? "card" : modeIndex == 1 ? "pack" : "rarity";
            AuraToolsCardVisualRuntime.ApplyThemeSelection(
                theme.ThemeId,
                availableSkins[Math.Max(0, Math.Min(skinIndex, availableSkins.Length - 1))].SkinId,
                mode,
                selector);
            RefreshContent();
        }, 90f);
    }

    private static void AddEffectEditor(Transform parent)
    {
        var effects = AuraToolsCardVisualRegistry.Effects.ToArray();
        if (effects.Length == 0) return;
        effectIndex = Math.Max(0, Math.Min(effectIndex, effects.Length - 1));
        var row = Row(parent, "Effect");
        AuraToolsUi.AddText(row, "动态效果", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 100f);
        AuraToolsUi.AddSelectButton(row, effects.Select(value => value.DisplayName).ToArray(), effectIndex,
            value => { effectIndex = value; RefreshContent(); }, 160f);
        var modes = new[] { "按卡牌", "按卡包", "按稀有度" };
        AuraToolsUi.AddSelectButton(row, modes, effectModeIndex, value => effectModeIndex = value, 120f);
        AuraToolsUi.AddInput(row, selector, value => selector = value.Trim(), 330f);
        AuraToolsUi.AddButton(row, "应用", () =>
        {
            var effect = effects[Math.Max(0, Math.Min(effectIndex, effects.Length - 1))];
            var mode = effectModeIndex == 0 ? "card" : effectModeIndex == 1 ? "pack" : "rarity";
            var draft = Draft(effect);
            foreach (var card in AuraToolsCardVisualRuntime.SelectCards(mode, selector))
                AuraToolsCardVisualRuntime.SetDynamicEffect(card, effect.EffectId, draft);
            RefreshContent();
        }, 76f);
        AuraToolsUi.AddButton(row, "恢复原生", () =>
        {
            var mode = effectModeIndex == 0 ? "card" : effectModeIndex == 1 ? "pack" : "rarity";
            foreach (var card in AuraToolsCardVisualRuntime.SelectCards(mode, selector))
                AuraToolsCardVisualRuntime.SetDynamicEffect(card, "");
            RefreshContent();
        }, 88f);

        var selected = effects[effectIndex];
        var parameters = selected.ExposedParameters.OrderBy(value => value.Key).ToArray();
        if (parameters.Length > 0)
        {
            var parameterRow = Row(parent, "EffectParameters");
            AuraToolsUi.AddText(parameterRow, "效果参数", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
                AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 100f);
            var draft = Draft(selected);
            foreach (var parameter in parameters)
            {
                AuraToolsUi.AddText(parameterRow, parameter.Key, AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleCenter, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 150f);
                AuraToolsUi.AddInput(parameterRow,
                    draft[parameter.Key].ToString("0.###", CultureInfo.InvariantCulture),
                    value =>
                    {
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                            draft[parameter.Key] = Math.Max(parameter.Value.Min, Math.Min(parameter.Value.Max, parsed));
                    }, 90f);
            }
        }
    }

    private static void AddCurrentMappings(Transform parent)
    {
        foreach (var theme in AuraToolsConfigService.CardVisual.Themes.OrderBy(value => value.Key))
        {
            foreach (var mapping in theme.Value.Cards.OrderBy(value => value.Key))
            {
                var row = Row(parent, "Mapping");
                AuraToolsUi.AddText(row, mapping.Key, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
                    AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
                AuraToolsUi.AddText(row, theme.Key + " / " + mapping.Value, AuraToolsUi.HintFontSize,
                    TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 230f);
                var themeId = theme.Key;
                var cardId = mapping.Key;
                AuraToolsUi.AddButton(row, "移除", () =>
                {
                    AuraToolsCardVisualRuntime.RemoveThemeCard(themeId, cardId);
                    RefreshContent();
                }, 72f);
            }
        }
        foreach (var mapping in AuraToolsConfigService.CardVisual.DynamicEffects.OrderBy(value => value.Key))
        {
            var row = Row(parent, "EffectMapping");
            AuraToolsUi.AddText(row, mapping.Key, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
                AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
            AuraToolsUi.AddText(row, mapping.Value.EffectId, AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 230f);
            var cardId = mapping.Key;
            AuraToolsUi.AddButton(row, "移除效果", () =>
            {
                AuraToolsCardVisualRuntime.SetDynamicEffect(cardId, "");
                RefreshContent();
            }, 82f);
        }
    }

    private static Dictionary<string, float> Draft(CardDynamicEffectDefinition effect)
    {
        if (EffectDrafts.TryGetValue(effect.EffectId, out var existing)) return existing;
        var draft = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var parameter in effect.ExposedParameters)
        {
            var value = effect.Floats.TryGetValue(parameter.Key, out var configured)
                ? configured
                : parameter.Value.Min;
            draft[parameter.Key] = Math.Max(parameter.Value.Min, Math.Min(parameter.Value.Max, value));
        }
        EffectDrafts[effect.EffectId] = draft;
        return draft;
    }

    private static Transform Row(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout("CardVisual" + name, parent);
        AuraToolsUi.SetFixedHeight(row, AuraToolsUi.InlineRowHeight);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row.transform;
    }

    private static void Save()
    {
        AuraToolsConfigService.SaveCardVisual();
        AuraToolsCardVisualRuntime.Reconfigure();
    }
}
