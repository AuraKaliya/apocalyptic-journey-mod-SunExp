using System;
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

        AuraToolsUi.AddButton(row.transform, "\u76ee\u5f55", () => OpenEntryDirectory(entry), 76f, 34f);
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
