using System;
using System.IO;
using System.Linq;
using System.Globalization;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.PresetLibrary;

internal static class AuraPresetLibraryPage
{
    private static Transform? content;
    private static InputField? nameInput;
    private static InputField? maximumInput;
    private static Text? statusText;
    private static Transform? overlayRoot;

    internal static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay("AuraTools.PresetLibrary", parent, "妙妙方案库", Refresh);
        overlayRoot = window.transform;
        var toolbar = Row(window.transform, "Toolbar", AuraToolsUi.ToolbarHeight);
        AuraToolsUi.AddSectionImage(toolbar);
        nameInput = AuraToolsUi.AddInput(toolbar.transform, "我的妙妙方案", _ => { }, 180f,
            AuraToolsUi.StandardButtonHeight, flexibleWidth: true);
        AuraToolsUi.AddButton(toolbar.transform, "保存当前", CreateCurrent, 96f, AuraToolsUi.CompactButtonHeight);
        AuraToolsUi.AddButton(toolbar.transform, "导入", Import, 76f, AuraToolsUi.CompactButtonHeight);
        AuraToolsUi.AddButton(toolbar.transform, "配置范围", ShowAudit, 96f, AuraToolsUi.CompactButtonHeight);

        var options = Row(window.transform, "Options", AuraToolsUi.ToolbarHeight);
        AuraToolsUi.AddSectionImage(options);
        AuraToolsUi.AddText(options.transform, "保留上限", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 72f);
        maximumInput = AuraToolsUi.AddInput(options.transform,
            AuraToolsExp.Dll.Config.AuraToolsConfigService.PresetLibrary.MaximumPresets.ToString(CultureInfo.InvariantCulture), _ => { }, 58f);
        AuraToolsUi.AddButton(options.transform, "应用", ApplyMaximum, 60f, AuraToolsUi.CompactButtonHeight);
        statusText = AuraToolsUi.AddText(options.transform, "", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        ToolboxIconButtonV2.Create(options.transform, "action.folder", "打开方案目录",
            () => FileResourceUtil.OpenDirectory(AuraPresetLibraryService.DirectoryPath), 40f, "夹");
        content = AuraToolsUi.CreateScroll(window.transform, "PresetLibrary");
        Refresh();
    }

    private static void Refresh()
    {
        if (content == null) return;
        AuraToolsUi.ClearChildren(content);
        foreach (var path in AuraPresetLibraryService.ListPresetFiles())
        {
            var captured = path;
            var summary = AuraPresetLibraryService.ReadSummary(path);
            var row = Row(content, "Preset-" + Path.GetFileName(path), 68f);
            AuraToolsUi.AddText(row.transform,
                summary.DisplayName + "\n" + summary.ModuleCount + " 个模块"
                + (string.IsNullOrWhiteSpace(summary.Warning) ? " · 预检后显示差异" : " · " + summary.Warning),
                AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
                summary.CompatibleFormat ? AuraToolsUi.Text : AuraToolsUi.WarningText, 60f, 1f);
            AuraToolsUi.AddButton(row.transform, "预检", () => ShowInspection(captured), 72f, 34f);
            AuraToolsUi.AddButton(row.transform, "复制", () => Run(() => AuraPresetLibraryService.Duplicate(captured), "已复制方案。"), 68f, 34f);
            AuraToolsUi.AddButton(row.transform, "重命名", () => Run(() => AuraPresetLibraryService.Rename(captured, nameInput?.text ?? ""), "已重命名方案。"), 82f, 34f);
            AuraToolsUi.AddButton(row.transform, "删除", () =>
            {
                try { AuraPresetLibraryService.Delete(captured); SetStatus("已删除方案。"); Refresh(); }
                catch (Exception ex) { SetStatus("删除失败：" + ex.Message); }
            }, 68f, 34f);
        }
    }

    private static void CreateCurrent()
    {
        Run(() => AuraPresetLibraryService.CreateFromCurrent(nameInput?.text ?? ""), "已保存当前配置方案。");
    }

    private static void ApplyMaximum()
    {
        if (!int.TryParse(maximumInput?.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            SetStatus("请输入 1 到 256 的整数。");
            return;
        }
        value = Math.Max(1, Math.Min(256, value));
        AuraToolsExp.Dll.Config.AuraToolsConfigService.PresetLibrary.MaximumPresets = value;
        AuraToolsExp.Dll.Config.AuraToolsConfigService.SavePresetLibrary();
        if (maximumInput != null) maximumInput.text = value.ToString(CultureInfo.InvariantCulture);
        SetStatus("方案库上限已更新。");
    }

    private static void Import()
    {
        OptionalFileDialog.PickFileAsync(
            "导入妙妙方案",
            new[]
            {
                new OptionalFileDialogFilter("妙妙方案", "*.aurapreset.json;*.json"),
                new OptionalFileDialogFilter("JSON 文件", "*.json")
            },
            "json",
            AuraPresetLibraryService.DirectoryPath,
            result =>
            {
                if (!result.Selected)
                {
                    if (result.Status == OptionalFileDialogStatus.Error) SetStatus("文件选择失败：" + result.Message);
                    return;
                }
                Run(() => AuraPresetLibraryService.Import(result.Path), "方案导入完成。");
            });
    }

    private static void ShowInspection(string path)
    {
        if (overlayRoot == null) return;
        var inspection = AuraPresetLibraryService.Inspect(path);
        var window = AuraToolsUi.CreateOverlay("AuraTools.PresetInspection", overlayRoot, "方案预检 - " + inspection.Document.DisplayName);
        var list = AuraToolsUi.CreateScroll(window.transform, "PresetInspection");
        foreach (var module in inspection.Modules)
        {
            var row = Row(list, "Inspect-" + module.ModuleId, 64f);
            AuraToolsUi.AddText(row.transform,
                module.DisplayName + "\n" + (module.Changed ? "将更新" : "无变化")
                + (string.IsNullOrWhiteSpace(module.Error) ? "" : " · " + module.Error),
                AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft,
                module.Compatible ? (module.Changed ? AuraToolsUi.SuccessText : AuraToolsUi.MutedText) : AuraToolsUi.WarningText,
                56f, 1f);
            AuraToolsUi.AddText(row.transform,
                module.Warnings.Count == 0
                    ? ""
                    : "警告 " + module.Warnings.Count + " · " + Compact(module.Warnings[0], 72),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 56f, 1f);
        }
        foreach (var warning in inspection.Warnings)
        {
            AuraToolsUi.AddText(list, warning, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText, AuraToolsUi.TextMinHeight, 1f);
        }
        var footer = Row(window.transform, "Footer", AuraToolsUi.FooterHeight);
        AuraToolsUi.AddText(footer.transform,
            inspection.Compatible ? "将更新 " + inspection.ChangedCount + " 个模块" : "方案不兼容",
            AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
            inspection.Compatible ? AuraToolsUi.SuccessText : AuraToolsUi.WarningText,
            AuraToolsUi.TextMinHeight, 1f);
        var apply = AuraToolsUi.AddButton(footer.transform, "应用方案", () =>
        {
            try
            {
                AuraPresetLibraryService.Apply(inspection);
                SetStatus("方案已应用；配置变更已统一发布。");
                if (overlayRoot != null)
                {
                    AuraToolsUi.CloseOverlay(overlayRoot, "AuraTools.PresetInspection", "preset applied");
                }
                Refresh();
            }
            catch (Exception ex)
            {
                SetStatus("应用失败：" + ex.Message);
            }
        }, 96f);
        apply.interactable = inspection.Compatible;
    }

    private static void ShowAudit()
    {
        if (overlayRoot == null) return;
        var window = AuraToolsUi.CreateOverlay("AuraTools.CodecAudit", overlayRoot, "方案包含范围");
        var list = AuraToolsUi.CreateScroll(window.transform, "CodecAudit");
        foreach (var codec in AuraToolConfigCodecRegistry.All)
        {
            var audit = codec.Audit;
            var row = Row(list, "Audit-" + codec.ModuleId, 78f);
            AuraToolsUi.AddText(row.transform,
                audit.DisplayName + " · 配置版本 " + codec.SchemaVersion + " · " + RiskLabel(audit.Risk)
                + "\n包含：" + audit.ExportedSurface + "　排除：" + audit.ExcludedSurface,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 70f, 1f);
            AuraToolsUi.AddText(row.transform,
                audit.Dependencies.Length == 0 ? "独立" : "依赖 " + string.Join("、", audit.Dependencies.Select(ModuleName)),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 70f, 0f, 180f);
        }
    }

    private static void Run(Func<string> action, string success)
    {
        try { _ = action(); SetStatus(success); Refresh(); }
        catch (Exception ex) { SetStatus("操作失败：" + ex.Message); }
    }

    private static GameObject Row(Transform parent, string name, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        AuraToolsUi.AddListRowImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message ?? "";
    }

    private static string Compact(string value, int maximum)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maximum ? text : text.Substring(0, maximum - 3) + "...";
    }

    private static string ModuleName(string moduleId)
    {
        return AuraToolModuleHost.Catalog.TryGet(moduleId, out var module)
            ? module.Descriptor.DisplayName
            : "相关模块";
    }

    private static string RiskLabel(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "resource" => "包含资源引用",
            "behavior" => "影响功能行为",
            "data" => "包含本地数据",
            _ => "普通设置"
        };
    }
}
