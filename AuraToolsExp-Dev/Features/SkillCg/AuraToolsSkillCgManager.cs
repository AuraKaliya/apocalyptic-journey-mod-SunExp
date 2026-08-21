using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.SkillCg;

public static class AuraToolsSkillCgManager
{
    private static Transform? content;
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.SkillCgManager",
            parent,
            "\u5361\u724c\u4f7f\u7528CG",
            RefreshRows);
        var toolbar = AuraToolsUi.CreateLayout("Toolbar", window.transform);
        AuraToolsUi.SetFixedHeight(toolbar, AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 10f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;
        hintText = AuraToolsUi.AddText(
            toolbar.transform,
            "\u5df2\u6ce8\u518c\u7684\u5361\u724c\u4f7f\u7528CG\uff1a\u53ef\u9884\u89c8\u3001\u542f\u7528\u6216\u5173\u95ed\uff0c\u4e0d\u4fee\u6539\u5185\u5bb9 Mod \u914d\u7f6e\u3002",
            14,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            34f,
            1f);
        AuraToolsUi.AddButton(toolbar.transform, "\u5237\u65b0", RefreshRows, 82f);

        content = AuraToolsUi.CreateScroll(window.transform, "SkillCgRegisteredEntries");
        RefreshRows();
    }

    private static void RefreshRows()
    {
        if (content == null)
        {
            return;
        }

        var viewState = AuraUiViewState.CaptureForContent(content);
        AuraToolsUi.ClearChildren(content);
        var entries = SkillCgArbiterRuntime.GetRegisteredCardUseCgEntries().ToList();
        SetHint("\u5171 " + entries.Count + " \u4e2a\u5df2\u6ce8\u518c\u5361\u724c\u4f7f\u7528CG\u3002");
        if (entries.Count == 0)
        {
            AuraToolsUi.AddText(
                content,
                "\u5c1a\u672a\u68c0\u6d4b\u5230\u5df2\u6ce8\u518c\u7684\u5361\u724c\u4f7f\u7528CG\u3002",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
            return;
        }

        foreach (var group in entries.GroupBy(entry => entry.OwnerModId).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            CreateOwnerHeader(group.Key, group.Count());
            foreach (var entry in group.OrderBy(entry => DisplayName(entry), StringComparer.OrdinalIgnoreCase))
            {
                CreateEntryRow(entry);
            }
        }
        AuraUiViewState.RestoreAfterLayout(
            content,
            viewState,
            "AuraTools.CardUseCg.Rows");
    }

    private static void CreateOwnerHeader(string ownerModId, int count)
    {
        var header = AuraToolsUi.CreateLayout("Owner-" + ownerModId, content!);
        AuraToolsUi.SetFixedHeight(header, AuraToolsUi.SectionHeight);
        AuraToolsUi.AddImage(header, AuraToolsUi.Header);
        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 3, 3);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        AuraToolsUi.AddText(
            header.transform,
            ownerModId + "  (" + count + ")",
            AuraToolsUi.SectionFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            AuraToolsUi.TextMinHeight,
            1f);
    }

    private static void CreateEntryRow(SkillCgRegisteredEntryView entry)
    {
        var row = AuraToolsUi.CreateLayout("SkillCg-" + entry.QualifiedCgId, content!);
        AuraUiStableId.Assign(row, "card-use-cg." + entry.QualifiedCgId);
        AuraToolsUi.SetFixedHeight(row, 54f);
        AuraToolsUi.AddPanelImage(row, entry.Enabled ? AuraToolsUi.ActiveRow : AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 6, 6);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var toggle = AuraToolsUi.AddToggle(row.transform, entry.Enabled, enabled =>
        {
            AuraToolsConfigService.SkillCg.CardUseCg.RegisteredEntries[entry.QualifiedCgId] = enabled;
            AuraCgActivationRuntime.SetLocalOverride(
                AuraToolsIds.ModId,
                entry.OwnerModId,
                entry.CgId,
                enabled);
            AuraToolsConfigService.SaveCardUseCg();
            SetHint((enabled ? "\u5df2\u542f\u7528\uff1a" : "\u5df2\u5173\u95ed\uff1a") + DisplayName(entry));
            RefreshRows();
        });
        AuraUiStableId.Assign(
            toggle.gameObject,
            "card-use-cg." + entry.QualifiedCgId + ".toggle");

        var text = AuraToolsUi.AddText(
            row.transform,
            DisplayName(entry),
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        AuraToolsUi.AddButton(row.transform, "\u9884\u89c8", () => PreviewEntry(entry), 68f, 34f);
        AuraToolsUi.AddButton(row.transform, "\u914d\u7f6e", () => OpenEntrySettings(entry), 68f, 34f);
        AuraToolsUi.AddButton(row.transform, "\u76ee\u5f55", () => OpenEntryDirectory(entry), 68f, 34f);
    }

    private static void PreviewEntry(SkillCgRegisteredEntryView entry)
    {
        var played = AuraToolsSkillCgRuntime.PreviewRegisteredCardUseCg(entry.OwnerModId, entry.CgId);
        SetHint((played ? "\u5df2\u9884\u89c8\uff1a" : "\u9884\u89c8\u5931\u8d25\uff1a") + DisplayName(entry));
    }

    private static void OpenEntrySettings(SkillCgRegisteredEntryView entry)
    {
        var overrides = AuraToolsConfigService.SkillCg.CardUseCg.PresentationOverrides;
        if (!overrides.TryGetValue(entry.QualifiedCgId, out var settings) || settings == null)
        {
            settings = new CardUseCgPresentationOverrideSettings();
            overrides[entry.QualifiedCgId] = settings;
        }

        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.CardUseCg.Settings." + entry.QualifiedCgId,
            content!,
            DisplayName(entry),
            () =>
            {
                settings.Normalize();
                AuraToolsConfigService.SaveCardUseCg();
            });
        var editor = AuraToolsUi.CreateScroll(window.transform, "CardUseCgPresentation");
        AddChoiceSettingRow(editor, settings);
        AddSettingRow(editor, "\u65f6\u957f", new[]
        {
            Field("\u6de1\u5165", Format(settings.FadeIn), value => settings.FadeIn = ParseFloat(value)),
            Field("\u505c\u7559", Format(settings.Hold), value => settings.Hold = ParseFloat(value)),
            Field("\u6de1\u51fa", Format(settings.FadeOut), value => settings.FadeOut = ParseFloat(value))
        });
        AddSettingRow(editor, "\u5e8f\u5217", new[]
        {
            Field("\u5e27\u95f4\u9694", Format(settings.FrameSeconds), value => settings.FrameSeconds = ParseFloat(value)),
            Field("\u9ed1\u952e", Format(settings.KeyThreshold), value => settings.KeyThreshold = ParseFloat(value)),
            Field("\u8fb9\u7f18", Format(settings.KeySoftness), value => settings.KeySoftness = ParseFloat(value))
        });
        AddSettingRow(editor, "\u95ea\u5c4f", new[]
        {
            Field("\u65f6\u523b", Format(settings.FlashAtSeconds), value => settings.FlashAtSeconds = ParseFloat(value)),
            Field("\u65f6\u957f", Format(settings.FlashDuration), value => settings.FlashDuration = ParseFloat(value)),
            Field("\u5f3a\u5ea6", Format(settings.FlashStrength), value => settings.FlashStrength = ParseFloat(value))
        });
        AddSettingRow(editor, "\u5e27\u95ea\u5c4f", new[]
        {
            Field("\u8d77\u59cb\u5e27", Format(settings.FlashStartFrame), value => settings.FlashStartFrame = ParseInt(value)),
            Field("\u7ed3\u675f\u5e27", Format(settings.FlashEndFrame), value => settings.FlashEndFrame = ParseInt(value)),
            Field("\u8109\u51b2\u95f4\u9694", Format(settings.FlashPulseEveryFrames), value => settings.FlashPulseEveryFrames = ParseInt(value))
        });
    }

    private static void AddChoiceSettingRow(Transform parent, CardUseCgPresentationOverrideSettings settings)
    {
        var row = AuraToolsUi.CreateLayout("CardUseCgChoices", parent);
        AuraToolsUi.SetFixedHeight(row, 42f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 5, 5);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        AuraToolsUi.AddText(row.transform, "\u6a21\u5f0f", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 70f);
        AddChoice(row.transform, "\u8868\u73b0", settings.PresentationMode,
            new[] { "", SkillCgPresentationModes.Slide, SkillCgPresentationModes.FullscreenFade, SkillCgPresentationModes.CenterFade },
            value => settings.PresentationMode = value);
        AddChoice(row.transform, "\u9002\u914d", settings.FitMode,
            new[] { "", SkillCgFitModes.Contain, SkillCgFitModes.Cover, SkillCgFitModes.Stretch },
            value => settings.FitMode = value);
        AddChoice(row.transform, "Alpha", settings.AlphaMode,
            new[] { "", SkillCgAlphaModes.None, SkillCgAlphaModes.BlackKey },
            value => settings.AlphaMode = value);
        AddChoice(row.transform, "\u95ea\u5c4f", settings.FlashMode,
            new[] { "", SkillCgFlashModes.Screen, SkillCgFlashModes.MaskedInvert, SkillCgFlashModes.ScreenBwPulse, SkillCgFlashModes.HybridBwPulse },
            value => settings.FlashMode = value);
    }

    private static void AddChoice(Transform parent, string label, string current, string[] values, Action<string> apply)
    {
        AuraToolsUi.AddText(parent, label, AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 48f);
        var labels = values.Select(value => string.IsNullOrWhiteSpace(value) ? "\u7ee7\u627f" : value).ToArray();
        var selected = Array.FindIndex(values, value => string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
        AuraToolsUi.AddSelectButton(parent, labels, Math.Max(0, selected), value => apply(values[value]), 126f);
    }

    private static void AddSettingRow(Transform parent, string title, System.Collections.Generic.IEnumerable<SettingField> fields)
    {
        var row = AuraToolsUi.CreateLayout(title, parent);
        AuraToolsUi.SetFixedHeight(row, 42f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 5, 5);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        AuraToolsUi.AddText(row.transform, title, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 70f);
        foreach (var field in fields)
        {
            AuraToolsUi.AddText(row.transform, field.Label, AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
                AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 58f);
            AuraToolsUi.AddInput(row.transform, field.Value, value => field.Apply(value.Trim()), 102f);
        }
    }

    private static SettingField Field(string label, string value, Action<string> apply)
    {
        return new SettingField(label, value, apply);
    }

    private static string Format(float? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static float? ParseFloat(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static int? ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private readonly struct SettingField
    {
        public SettingField(string label, string value, Action<string> apply)
        {
            Label = label;
            Value = value;
            Apply = apply;
        }

        public string Label { get; }
        public string Value { get; }
        public Action<string> Apply { get; }
    }

    private static void OpenEntryDirectory(SkillCgRegisteredEntryView entry)
    {
        var resource = string.IsNullOrWhiteSpace(entry.Resource) ? entry.FallbackImage : entry.Resource;
        var path = SkillCgArbiterRuntime.ResolveImagePath(entry.OwnerModId, resource);
        var directory = "";
        if (Directory.Exists(path))
        {
            directory = path;
        }
        else if (File.Exists(path))
        {
            directory = Path.GetDirectoryName(path) ?? "";
        }
        else if (!string.IsNullOrWhiteSpace(entry.BundlePath))
        {
            var bundlePath = AuraToolsConfigService.ResolveConfiguredPath(entry.BundlePath);
            directory = File.Exists(bundlePath)
                ? Path.GetDirectoryName(bundlePath) ?? ""
                : Path.GetDirectoryName(bundlePath) ?? "";
        }

        if (!string.IsNullOrWhiteSpace(directory))
        {
            FileResourceUtil.OpenDirectory(directory);
        }
    }

    private static string DisplayName(SkillCgRegisteredEntryView entry)
    {
        return string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.CgId : entry.DisplayName;
    }

    private static string JoinOrAny(System.Collections.Generic.IEnumerable<string> values)
    {
        var list = (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(4)
            .ToList();
        return list.Count == 0 ? "*" : string.Join(", ", list);
    }

    private static void SetHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }
}
