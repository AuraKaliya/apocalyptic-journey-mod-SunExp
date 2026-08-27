using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Cg;

public static class AuraToolsRoleCgSettingsPage
{
    private const float CandidateRowHeight = AuraToolsCgSettingsLayoutPolicy.CandidateRowHeight;
    private static Transform? windowRoot;
    private static Transform? contextHost;
    private static Transform? resourceContent;
    private static Text? statusText;
    private static Button? roleButton;
    private static Button? skillTab;
    private static Button? feastTab;
    private static Button? lowHealthTab;
    private static string selectedRoleId = "";
    private static string selectedChannel = AuraToolsRoleCgChannels.Skill;
    private static string selectedSkillId = "";

    public static void Show(Transform parent)
    {
        SelectInitialRole();
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.RoleCgSettings",
            parent,
            "角色 CG 配置",
            Cleanup,
            true,
            AuraToolsCgSettingsLayoutPolicy.MaximumRolePageWidth);
        windowRoot = window.transform;

        var roleRow = Horizontal("Role", window.transform, AuraToolsUi.ToolbarHeight, 8f);
        AuraToolsUi.AddText(roleRow.transform, "角色", AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 52f);
        roleButton = AuraToolsUi.AddButton(roleRow.transform, RoleName(), () => ShowRolePicker(window.transform), 300f);
        AuraToolsUi.EnsureLayoutElement(roleButton.gameObject).flexibleWidth = 1f;
        AuraToolsUi.AddToggle(roleRow.transform, AuraToolsConfigService.SkillCg.SyncRemote, enabled =>
        {
            AuraToolsConfigService.SkillCg.SyncRemote = enabled;
            AuraToolsConfigService.SaveSkillCg();
            SetStatus(enabled ? "已开启联机同步。" : "已关闭联机同步。", warning: !enabled);
        });
        AuraToolsUi.AddText(roleRow.transform, "联机同步", AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 90f);

        var tabs = Horizontal("Types", window.transform, 44f, 8f);
        skillTab = AuraToolsUi.AddButton(tabs.transform, "技能", () => SelectChannel(AuraToolsRoleCgChannels.Skill), 148f, 40f);
        feastTab = AuraToolsUi.AddButton(tabs.transform, "美餐", () => SelectChannel(AuraToolsRoleCgChannels.Feast), 148f, 40f);
        lowHealthTab = AuraToolsUi.AddButton(tabs.transform, "低生命", () => SelectChannel(AuraToolsRoleCgChannels.LowHealth), 148f, 40f);
        AuraToolsUi.AddText(tabs.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);

        contextHost = AuraToolsUi.CreateLayout("Context", window.transform).transform;
        AuraToolsUi.SetFixedHeight(contextHost.gameObject, 48f);
        resourceContent = AuraToolsUi.CreateScroll(window.transform, "RoleCgCandidates");

        var footer = Horizontal("Footer", window.transform, AuraToolsUi.FooterHeight, 8f);
        statusText = AuraToolsUi.AddText(footer.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(footer.transform, "导入图片", PickImage, 104f, 40f);
        AuraToolsUi.AddButton(footer.transform, "恢复默认", ResetCurrent, 104f, 40f);
        Refresh();
    }

    private static void Refresh()
    {
        if (contextHost == null || resourceContent == null)
        {
            return;
        }

        AuraToolsUi.SetButtonLabel(roleButton, RoleName());
        if (skillTab != null) skillTab.interactable = !IsChannel(AuraToolsRoleCgChannels.Skill);
        if (feastTab != null) feastTab.interactable = !IsChannel(AuraToolsRoleCgChannels.Feast);
        if (lowHealthTab != null) lowHealthTab.interactable = !IsChannel(AuraToolsRoleCgChannels.LowHealth);
        RefreshContext();
        RefreshCandidates();
    }

    private static void RefreshContext()
    {
        AuraToolsUi.ClearChildren(contextHost!);
        var row = Horizontal("ContextRow", contextHost!, 48f, 8f);
        if (IsChannel(AuraToolsRoleCgChannels.Skill))
        {
            var skills = AuraToolsRoleCgCatalog.SkillOptions(selectedRoleId).ToList();
            if (skills.Count == 0)
            {
                skills.Add(new RoleSkillInfo { Id = "*", DisplayName = "任意技能", Slot = 1 });
            }
            if (!skills.Any(skill => string.Equals(skill.Id, selectedSkillId, StringComparison.OrdinalIgnoreCase)))
            {
                selectedSkillId = skills[0].Id;
            }

            AuraToolsUi.AddText(row.transform, "触发技能", AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 88f);
            var selected = Math.Max(0, skills.FindIndex(skill =>
                string.Equals(skill.Id, selectedSkillId, StringComparison.OrdinalIgnoreCase)));
            AuraToolsUi.AddSelectButton(
                row.transform,
                skills.Select(SkillName).ToList(),
                selected,
                index =>
                {
                    selectedSkillId = skills[Mathf.Clamp(index, 0, skills.Count - 1)].Id;
                    Refresh();
                },
                360f,
                40f);
            AuraToolsUi.AddText(row.transform, "", AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
            return;
        }

        if (IsChannel(AuraToolsRoleCgChannels.LowHealth))
        {
            AuraToolsUi.AddText(row.transform, "触发生命值", AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 96f);
            var valueText = AuraToolsUi.AddText(row.transform, ThresholdLabel(), AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleCenter, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 56f);
            CreateThresholdSlider(row.transform, valueText);
            AuraToolsUi.AddText(row.transform, "生命值从上方向下越过阈值时播放", AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
            return;
        }

        var feastEnabled = AuraToolsConfigService.MatchExperience.Feast.Enabled;
        AuraToolsUi.AddText(row.transform,
            feastEnabled ? "一键美餐完成后，按当前角色播放所选资源。" : "一键美餐未开启；资源配置会保留，但不会自动播放。",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            feastEnabled ? AuraToolsUi.Text : AuraToolsUi.WarningText,
            AuraToolsUi.TextMinHeight,
            1f);
    }

    private static void RefreshCandidates()
    {
        var viewState = AuraUiViewState.CaptureForContent(resourceContent!);
        AuraToolsUi.ClearChildren(resourceContent!);
        if (IsChannel(AuraToolsRoleCgChannels.Feast))
        {
            RefreshFeastCandidates();
        }
        else
        {
            var candidates = AuraToolsRoleCgCatalog.Query(selectedRoleId, selectedChannel, selectedSkillId);
            foreach (var candidate in candidates)
            {
                CreateRoleCandidateRow(candidate);
            }
            if (candidates.Count == 0)
            {
                AddEmptyState("当前角色和类型还没有可用 CG。可使用下方“导入图片”创建玩家资源。");
            }
        }
        AuraUiViewState.RestoreAfterLayout(resourceContent!, viewState, "AuraTools.RoleCg.Candidates");
    }

    private static void RefreshFeastCandidates()
    {
        var role = AuraToolsFeastRuntime.EnsureRoleSettings(selectedRoleId, RoleName());
        var candidates = AuraToolsFeastRuntime.BuildCandidateCgsForRole(selectedRoleId);
        foreach (var candidate in candidates)
        {
            var enabled = role.IsCandidateEnabled(candidate.QualifiedCgId);
            CreateCandidateRow(
                candidate.QualifiedCgId,
                candidate.DisplayName,
                FeastSource(candidate),
                candidate.OwnerModId,
                candidate.ImageResource,
                enabled,
                candidate.SourceKind == FeastCgSourceKind.Manual,
                () =>
                {
                    foreach (var item in candidates)
                    {
                        role.ResourceOverrides[item.QualifiedCgId] = string.Equals(
                            item.QualifiedCgId,
                            candidate.QualifiedCgId,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    AuraToolsFeastRuntime.SaveRoleSettings(role);
                    Refresh();
                },
                () =>
                {
                    var played = AuraToolsFeastRuntime.PreviewCandidate(selectedRoleId, candidate.QualifiedCgId);
                    SetStatus(played ? "正在预览“" + candidate.DisplayName + "”。" : "无法预览该资源。", !played);
                },
                () => OpenFeastPresentation(role),
                candidate.SourceKind == FeastCgSourceKind.Manual
                    ? () =>
                    {
                        AuraToolsFeastManualResourceStore.RemoveRoleImage(selectedRoleId, out var message);
                        SetStatus(message);
                        Refresh();
                    }
                    : null);
        }

        if (candidates.Count == 0)
        {
            AddEmptyState("当前角色没有可用的美餐 CG。可导入一张图片作为玩家资源。");
        }
    }

    private static void CreateRoleCandidateRow(AuraToolsRoleCgCandidate candidate)
    {
        CreateCandidateRow(
            candidate.QualifiedCgId,
            candidate.DisplayName,
            candidate.Manual ? "玩家资源" : AuraToolsPlayerDisplay.OwnerName(candidate.OwnerModId) + " · 默认资源",
            candidate.OwnerModId,
            candidate.Resource,
            candidate.Enabled,
            candidate.Manual,
            () =>
            {
                AuraToolsRoleCgCatalog.Select(
                    selectedRoleId,
                    selectedChannel,
                    selectedSkillId,
                    candidate.QualifiedCgId);
                SetStatus("已选择“" + candidate.DisplayName + "”。");
                Refresh();
            },
            () =>
            {
                var played = AuraToolsRoleCgCatalog.Preview(candidate);
                SetStatus(played ? "正在预览“" + candidate.DisplayName + "”。" : "无法预览该资源。", !played);
            },
            () => OpenRolePresentation(candidate),
            candidate.Manual
                ? () =>
                {
                    AuraToolsRoleCgCatalog.RemoveManual(candidate.QualifiedCgId);
                    SetStatus("已移除玩家资源配置。");
                    Refresh();
                }
                : null);
    }

    private static void CreateCandidateRow(
        string stableId,
        string displayName,
        string source,
        string ownerModId,
        string resource,
        bool enabled,
        bool manual,
        Action select,
        Action preview,
        Action settings,
        Action? remove)
    {
        var row = AuraToolsUi.CreateLayout("Candidate-" + stableId, resourceContent!);
        AuraUiStableId.Assign(row, "role-cg.candidate." + stableId);
        AuraToolsUi.SetFixedHeight(row, CandidateRowHeight);
        AuraToolsUi.AddListRowImage(row, enabled ? AuraToolsUi.ActiveRow : AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        AddThumbnail(row.transform, ownerModId, resource);
        AuraToolsUi.AddText(
            row.transform,
            displayName + "\n" + source,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            enabled ? AuraToolsUi.Text : AuraToolsUi.MutedText,
            66f,
            1f);
        AuraToolsUi.AddText(row.transform, enabled ? "已选择" : "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter, AuraToolsUi.Accent, AuraToolsUi.TextMinHeight, 0f, 58f);
        AuraToolsUi.AddButton(row.transform, "预览", preview, 68f, 36f);
        var selectButton = AuraToolsUi.AddButton(row.transform, enabled ? "已选择" : "选择", select, 72f, 36f);
        selectButton.interactable = !enabled;
        AuraToolsUi.AddButton(row.transform, "调整", settings, 68f, 36f);
        if (manual && remove != null)
        {
            AuraToolsUi.AddButton(row.transform, "移除", remove, 68f, 36f);
        }
    }

    private static void AddThumbnail(Transform parent, string ownerModId, string resource)
    {
        var root = AuraToolsUi.CreateLayout("Thumbnail", parent);
        AuraToolsUi.SetFixedSize(root, 64f, 64f);
        var image = root.AddComponent<Image>();
        image.color = new Color(0.08f, 0.075f, 0.13f, 1f);
        image.raycastTarget = false;
        image.preserveAspect = true;
        var sprite = LoadPreviewSprite(ownerModId, resource);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
        }
    }

    private static Sprite? LoadPreviewSprite(string ownerModId, string resource)
    {
        try
        {
            return AuraToolsResourceCache.Load<Sprite>(resource, true);
        }
        catch
        {
            return null;
        }
    }

    private static void ShowRolePicker(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.RoleCg.RolePicker",
            parent,
            "选择角色",
            null,
            true,
            720f);
        var filterRow = Horizontal("Filter", window.transform, AuraToolsUi.ToolbarHeight, 8f);
        AuraToolsUi.AddText(filterRow.transform, "搜索", AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 52f);
        var list = AuraToolsUi.CreateScroll(window.transform, "RoleCgRolePicker");
        void Rebuild(string filter)
        {
            AuraToolsUi.ClearChildren(list);
            var query = (filter ?? "").Trim();
            foreach (var role in RoleCatalog.GetRoles()
                         .Where(role => string.IsNullOrWhiteSpace(query)
                                        || role.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                                        || role.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(role => role.DisplayName)
                         .ThenBy(role => role.Id))
            {
                var captured = role;
                var row = Horizontal("Role-" + role.Id, list, 48f, 8f);
                AuraToolsUi.AddText(row.transform, role.DisplayName, AuraToolsUi.BodyFontSize,
                    TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
                AuraToolsUi.AddButton(row.transform,
                    string.Equals(role.Id, selectedRoleId, StringComparison.OrdinalIgnoreCase) ? "当前" : "选择",
                    () =>
                    {
                        selectedRoleId = captured.Id;
                        selectedSkillId = "";
                        AuraToolsUi.CloseOverlay(parent, "AuraTools.RoleCg.RolePicker");
                        Refresh();
                    },
                    76f,
                    36f);
            }
        }
        AuraToolsUi.AddTmpInput(filterRow.transform, "", "输入角色名称", Rebuild, 420f, 40f);
        Rebuild("");
    }

    private static void PickImage()
    {
        var directory = IsChannel(AuraToolsRoleCgChannels.Feast)
            ? AuraToolsFeastManualResourceStore.RoleDirectory(selectedRoleId)
            : FileResourceUtil.RoleSkillCgDirectory(selectedRoleId);
        SetStatus("正在打开图片选择器……");
        OptionalFileDialog.PickImageFileAsync(directory, result =>
        {
            if (!result.Selected)
            {
                SetStatus(result.Status == OptionalFileDialogStatus.Cancelled
                    ? "已取消选择。"
                    : "无法打开图片选择器：" + result.Message, result.Status != OptionalFileDialogStatus.Cancelled);
                return;
            }

            bool imported;
            string message;
            if (IsChannel(AuraToolsRoleCgChannels.Feast))
            {
                imported = AuraToolsFeastManualResourceStore.ImportRoleImage(selectedRoleId, result.Path, out message);
                if (imported)
                {
                    var role = AuraToolsFeastRuntime.EnsureRoleSettings(selectedRoleId, RoleName());
                    var candidates = AuraToolsFeastRuntime.BuildCandidateCgsForRole(selectedRoleId);
                    var manual = candidates.FirstOrDefault(candidate => candidate.SourceKind == FeastCgSourceKind.Manual);
                    if (manual != null)
                    {
                        foreach (var candidate in candidates)
                        {
                            role.ResourceOverrides[candidate.QualifiedCgId] = string.Equals(
                                candidate.QualifiedCgId,
                                manual.QualifiedCgId,
                                StringComparison.OrdinalIgnoreCase);
                        }
                        AuraToolsFeastRuntime.SaveRoleSettings(role);
                    }
                }
            }
            else
            {
                imported = AuraToolsRoleCgCatalog.Import(
                    selectedRoleId,
                    selectedChannel,
                    selectedSkillId,
                    result.Path,
                    out message);
            }

            SetStatus(message, !imported);
            Refresh();
        });
    }

    private static void ResetCurrent()
    {
        if (IsChannel(AuraToolsRoleCgChannels.Feast))
        {
            var role = AuraToolsFeastRuntime.EnsureRoleSettings(selectedRoleId, RoleName());
            role.ResourceOverrides.Clear();
            role.Presentation = SkillCgPresentationSettings.CreateInherited();
            AuraToolsFeastManualResourceStore.RemoveRoleImage(selectedRoleId, out _);
            AuraToolsFeastRuntime.SaveRoleSettings(role);
        }
        else
        {
            AuraToolsRoleCgCatalog.ResetContext(selectedRoleId, selectedChannel, selectedSkillId);
        }
        SetStatus("已恢复当前角色与类型的默认方案。");
        Refresh();
    }

    private static void OpenRolePresentation(AuraToolsRoleCgCandidate candidate)
    {
        var local = AuraToolsRoleCgCatalog.GetOrCreateOverride(candidate.QualifiedCgId);
        OpenPresentationEditor(
            candidate.DisplayName,
            local.Presentation,
            () =>
            {
                local.Normalize();
                AuraToolsConfigService.SaveSkillCg();
                AuraToolsSkillCgRuntime.ApplyRoleCgConfiguration();
            },
            null);
    }

    private static void OpenFeastPresentation(FeastRoleSettings role)
    {
        var bridge = new CardUseCgPresentationOverrideSettings
        {
            PresentationMode = role.EffectivePresentation.Mode,
            FitMode = role.EffectivePresentation.Fit,
            FadeIn = role.EffectivePresentation.FadeIn,
            Hold = role.EffectivePresentation.Hold,
            FadeOut = role.EffectivePresentation.FadeOut,
            FocusX = role.EffectivePresentation.FocusX,
            FocusY = role.EffectivePresentation.FocusY,
            SafeScale = role.EffectivePresentation.SafeScale
        };
        OpenPresentationEditor(
            "美餐 CG 表现",
            bridge,
            () =>
            {
                bridge.Normalize();
                role.Presentation = new SkillCgPresentationSettings
                {
                    Mode = bridge.PresentationMode,
                    Fit = bridge.FitMode,
                    FadeIn = bridge.FadeIn ?? 0.35f,
                    Hold = bridge.Hold ?? 1.5f,
                    FadeOut = bridge.FadeOut ?? 0.5f,
                    FocusX = bridge.FocusX ?? 0.5f,
                    FocusY = bridge.FocusY ?? 0.5f,
                    SafeScale = bridge.SafeScale ?? 1f
                };
                AuraToolsFeastRuntime.SaveRoleSettings(role);
            },
            () =>
            {
                role.Presentation = SkillCgPresentationSettings.CreateInherited();
                AuraToolsFeastRuntime.SaveRoleSettings(role);
            });
    }

    private static void OpenPresentationEditor(
        string title,
        CardUseCgPresentationOverrideSettings presentation,
        Action save,
        Action? reset)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.RoleCg.Presentation",
            windowRoot!,
            title,
            save,
            true,
            780f);
        var content = AuraToolsUi.CreateScroll(window.transform, "RoleCgPresentation");
        AddChoiceRow(content, presentation, save);
        AddNumberRow(content, "时长", new[]
        {
            Field("淡入", presentation.FadeIn, value => presentation.FadeIn = value),
            Field("停留", presentation.Hold, value => presentation.Hold = value),
            Field("淡出", presentation.FadeOut, value => presentation.FadeOut = value)
        }, save);
        AddNumberRow(content, "画面", new[]
        {
            Field("横向焦点", presentation.FocusX, value => presentation.FocusX = value),
            Field("纵向焦点", presentation.FocusY, value => presentation.FocusY = value),
            Field("缩放", presentation.SafeScale, value => presentation.SafeScale = value)
        }, save);
        var resetRow = Horizontal("Reset", content, 48f, 8f);
        AuraToolsUi.AddText(resetRow.transform, "清空表现覆盖后，将重新使用资源提供方的默认参数。",
            AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(resetRow.transform, "沿用默认", () =>
        {
            if (reset != null)
            {
                reset();
                AuraToolsUi.CloseOverlay(windowRoot!, "AuraTools.RoleCg.Presentation");
                SetStatus("已恢复资源默认表现。");
                return;
            }
            presentation.PresentationMode = "";
            presentation.FitMode = "";
            presentation.FadeIn = null;
            presentation.Hold = null;
            presentation.FadeOut = null;
            presentation.FocusX = null;
            presentation.FocusY = null;
            presentation.SafeScale = null;
            save();
            AuraToolsUi.CloseOverlay(windowRoot!, "AuraTools.RoleCg.Presentation");
            SetStatus("已恢复资源默认表现。");
        }, 104f, 40f);
    }

    private static void AddChoiceRow(
        Transform parent,
        CardUseCgPresentationOverrideSettings presentation,
        Action save)
    {
        var row = Horizontal("Choices", parent, 48f, 8f);
        AuraToolsUi.AddText(row.transform, "演出", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 52f);
        var modes = new[] { "", SkillCgPresentationModes.Slide, SkillCgPresentationModes.FullscreenFade, SkillCgPresentationModes.CenterFade };
        var modeLabels = new[] { "沿用默认", "从右到左", "全屏淡入淡出", "居中淡入淡出" };
        AuraToolsUi.AddSelectButton(row.transform, modeLabels,
            Math.Max(0, Array.FindIndex(modes, value => string.Equals(value, presentation.PresentationMode, StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                presentation.PresentationMode = modes[Mathf.Clamp(index, 0, modes.Length - 1)];
                save();
            }, 220f, 40f);
        AuraToolsUi.AddText(row.transform, "适配", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 52f);
        var fits = new[] { "", SkillCgFitModes.Contain, SkillCgFitModes.Cover, SkillCgFitModes.Stretch };
        var fitLabels = new[] { "沿用默认", "完整显示", "裁切填满", "拉伸填充" };
        AuraToolsUi.AddSelectButton(row.transform, fitLabels,
            Math.Max(0, Array.FindIndex(fits, value => string.Equals(value, presentation.FitMode, StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                presentation.FitMode = fits[Mathf.Clamp(index, 0, fits.Length - 1)];
                save();
            }, 180f, 40f);
        AuraToolsUi.AddText(row.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
    }

    private static void AddNumberRow(
        Transform parent,
        string title,
        IEnumerable<NumberField> fields,
        Action save)
    {
        var row = Horizontal("Numbers-" + title, parent, 48f, 8f);
        AuraToolsUi.AddText(row.transform, title, AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 52f);
        foreach (var field in fields)
        {
            AuraToolsUi.AddText(row.transform, field.Label, AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 74f);
            AuraToolsUi.AddInput(row.transform, Format(field.Value), raw =>
            {
                field.Apply(ParseNullable(raw));
                save();
            }, 90f, 40f);
        }
        AuraToolsUi.AddText(row.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
    }

    private static void CreateThresholdSlider(Transform parent, Text valueText)
    {
        var root = AuraToolsUi.CreateLayout("ThresholdSlider", parent);
        AuraToolsUi.SetFixedSize(root, 260f, 40f);
        var background = AuraToolsUi.CreateRect("Background", root.transform,
            new Vector2(0f, 0.42f), new Vector2(1f, 0.58f), new Vector2(0.5f, 0.5f), Vector2.zero);
        var backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = AuraToolsUi.RowHighlighted;
        var fill = AuraToolsUi.CreateRect("Fill", root.transform,
            new Vector2(0f, 0.42f), new Vector2(1f, 0.58f), new Vector2(0f, 0.5f), Vector2.zero);
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = AuraToolsUi.Accent;
        var handle = AuraToolsUi.CreateRect("Handle", root.transform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22f, 22f));
        var handleImage = handle.AddComponent<Image>();
        handleImage.color = AuraToolsUi.Text;
        var slider = root.AddComponent<Slider>();
        slider.minValue = 5f;
        slider.maxValue = 95f;
        slider.wholeNumbers = true;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        slider.value = Mathf.Round(AuraToolsConfigService.SkillCg.LowHealthThreshold * 100f);
        slider.onValueChanged.AddListener(value =>
        {
            AuraToolsConfigService.SkillCg.LowHealthThreshold = value / 100f;
            valueText.text = ThresholdLabel();
            AuraToolsConfigService.SaveSkillCg();
        });
    }

    private static GameObject Horizontal(string name, Transform parent, float height, float spacing)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static void AddEmptyState(string message)
    {
        AuraToolsUi.AddText(resourceContent!, message, AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 72f, 1f);
    }

    private static void SelectInitialRole()
    {
        var roles = RoleCatalog.GetRoles();
        var current = RoleCatalog.NormalizeRoleId(AuraToolsSkillCgRuntime.ReadCurrentCareerId());
        selectedRoleId = roles.FirstOrDefault(role => RoleCatalog.MatchesRole(current, role.Id))?.Id
                         ?? roles.FirstOrDefault()?.Id
                         ?? current;
    }

    private static void SelectChannel(string channel)
    {
        selectedChannel = channel;
        Refresh();
    }

    private static bool IsChannel(string channel)
    {
        return string.Equals(selectedChannel, channel, StringComparison.OrdinalIgnoreCase);
    }

    private static string RoleName()
    {
        var role = RoleCatalog.GetRoles().FirstOrDefault(value =>
            string.Equals(value.Id, selectedRoleId, StringComparison.OrdinalIgnoreCase));
        return role == null || string.IsNullOrWhiteSpace(role.DisplayName) ? selectedRoleId : role.DisplayName;
    }

    private static string SkillName(RoleSkillInfo skill)
    {
        return string.IsNullOrWhiteSpace(skill.DisplayName) ? skill.Id : skill.DisplayName;
    }

    private static string ThresholdLabel()
    {
        return Math.Round(AuraToolsConfigService.SkillCg.LowHealthThreshold * 100f) + "%";
    }

    private static string FeastSource(FeastCgCandidate candidate)
    {
        if (candidate.SourceKind == FeastCgSourceKind.Manual) return "玩家资源";
        if (candidate.SourceKind == FeastCgSourceKind.Default) return "AuraToolsExp 默认资源";
        return AuraToolsPlayerDisplay.OwnerName(candidate.OwnerModId) + " · 注册资源";
    }

    private static string Format(float? value)
    {
        return value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
    }

    private static float? ParseNullable(string value)
    {
        var normalized = (value ?? "").Trim().Replace(',', '.');
        return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static NumberField Field(string label, float? value, Action<float?> apply)
    {
        return new NumberField(label, value, apply);
    }

    private static void SetStatus(string message, bool warning = false)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = warning ? AuraToolsUi.WarningText : AuraToolsUi.MutedText;
    }

    private static void Cleanup()
    {
        windowRoot = null;
        contextHost = null;
        resourceContent = null;
        statusText = null;
        roleButton = null;
        skillTab = null;
        feastTab = null;
        lowHealthTab = null;
    }

    private readonly struct NumberField
    {
        public NumberField(string label, float? value, Action<float?> apply)
        {
            Label = label;
            Value = value;
            Apply = apply;
        }

        public string Label { get; }
        public float? Value { get; }
        public Action<float?> Apply { get; }
    }
}
