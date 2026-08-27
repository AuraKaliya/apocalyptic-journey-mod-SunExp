using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Cg;

public static class AuraToolsEventCgSettingsPage
{
    private const string CategoryVictory = "victory";
    private const string CategoryOpening = "opening";
    private const string CategoryDefeat = "defeat";
    private const string CategorySettlement = "settlement";
    private static Transform? windowRoot;
    private static Transform? contextHost;
    private static Transform? bodyContent;
    private static Text? statusText;
    private static Button? victoryTab;
    private static Button? openingTab;
    private static Button? defeatTab;
    private static Button? settlementTab;
    private static Button? configTab;
    private static Button? previewTab;
    private static string category = CategoryVictory;
    private static string victorySceneId = AuraToolsEventCgSceneIds.VictoryStandard;
    private static bool previewMode;
    private static int previewParticipants = 4;
    private static IDisposable? embeddedPreview;

    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.EventCgSettings",
            parent,
            "事件 CG 配置",
            Cleanup,
            true,
            AuraToolsCgSettingsLayoutPolicy.MaximumEventPageWidth);
        windowRoot = window.transform;

        var categories = Horizontal("Categories", window.transform, 44f, 8f);
        victoryTab = AuraToolsUi.AddButton(categories.transform, "胜利", () => SelectCategory(CategoryVictory), 150f, 40f);
        openingTab = AuraToolsUi.AddButton(categories.transform, "战斗开场", () => SelectCategory(CategoryOpening), 150f, 40f);
        defeatTab = AuraToolsUi.AddButton(categories.transform, "战斗失败", () => SelectCategory(CategoryDefeat), 150f, 40f);
        settlementTab = AuraToolsUi.AddButton(categories.transform, "冒险结算", () => SelectCategory(CategorySettlement), 150f, 40f);
        AuraToolsUi.AddText(categories.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);

        contextHost = AuraToolsUi.CreateLayout("Context", window.transform).transform;
        AuraToolsUi.SetFixedHeight(contextHost.gameObject, 48f);

        var views = Horizontal("Views", window.transform, 44f, 8f);
        configTab = AuraToolsUi.AddButton(views.transform, "配置", () => SelectView(false), 120f, 40f);
        previewTab = AuraToolsUi.AddButton(views.transform, "预览", () => SelectView(true), 120f, 40f);
        statusText = AuraToolsUi.AddText(views.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddToggle(views.transform, AuraToolsConfigService.SkillCg.EventCg.SyncRemote, enabled =>
        {
            AuraToolsConfigService.SkillCg.EventCg.SyncRemote = enabled;
            Save();
            SetStatus(enabled ? "已开启联机同步。" : "已关闭联机同步。", !enabled);
        });
        AuraToolsUi.AddText(views.transform, "联机同步", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 0f, 86f);

        bodyContent = AuraToolsUi.CreateScroll(window.transform, "EventCgScene");
        Refresh();
    }

    private static void Refresh()
    {
        if (contextHost == null || bodyContent == null)
        {
            return;
        }

        if (victoryTab != null) victoryTab.interactable = !string.Equals(category, CategoryVictory, StringComparison.Ordinal);
        if (openingTab != null) openingTab.interactable = !string.Equals(category, CategoryOpening, StringComparison.Ordinal);
        if (defeatTab != null) defeatTab.interactable = !string.Equals(category, CategoryDefeat, StringComparison.Ordinal);
        if (settlementTab != null) settlementTab.interactable = !string.Equals(category, CategorySettlement, StringComparison.Ordinal);
        if (configTab != null) configTab.interactable = previewMode;
        if (previewTab != null) previewTab.interactable = !previewMode;
        RefreshContext();
        ReleaseEmbeddedPreview();
        AuraToolsUi.ClearChildren(bodyContent);
        if (previewMode)
        {
            BuildPreview();
        }
        else
        {
            BuildConfiguration();
        }
    }

    private static void RefreshContext()
    {
        AuraToolsUi.ClearChildren(contextHost!);
        var row = Horizontal("ContextRow", contextHost!, 48f, 8f);
        if (string.Equals(category, CategoryVictory, StringComparison.Ordinal))
        {
            var ids = AuraToolsEventCgSceneIds.Victory;
            var labels = new[] { "普通胜利", "点金手胜利", "仪式胜利", "诅咒胜利" };
            var selected = Math.Max(0, Array.FindIndex(ids, value =>
                string.Equals(value, victorySceneId, StringComparison.OrdinalIgnoreCase)));
            AuraToolsUi.AddText(row.transform, "胜利类型", AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 88f);
            AuraToolsUi.AddSelectButton(row.transform, labels, selected, index =>
            {
                victorySceneId = ids[Mathf.Clamp(index, 0, ids.Length - 1)];
                Refresh();
            }, 300f, 40f);
        }
        else
        {
            AuraToolsUi.AddText(row.transform, SceneName(CurrentSceneId()), AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        }

        AuraToolsUi.AddText(row.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        var scene = CurrentScene();
        AuraToolsUi.AddToggle(row.transform, scene.Enabled, enabled =>
        {
            scene.Enabled = enabled;
            Save();
            Refresh();
        });
        AuraToolsUi.AddText(row.transform, "启用此场景", AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, scene.Enabled ? AuraToolsUi.Text : AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight, 0f, 104f);
    }

    private static void BuildConfiguration()
    {
        var scene = CurrentScene();
        AddSummaryRow(
            "使用方案",
            scene.UsesDefaultPresentation ? "AuraToolsExp 默认方案" : "本地自定义方案",
            scene.UsesDefaultPresentation ? AuraToolsUi.SuccessText : AuraToolsUi.Accent,
            scene.UsesDefaultPresentation ? null : ResetPresentation);
        AddSummaryRow(
            "背景",
            string.IsNullOrWhiteSpace(scene.BackgroundResource)
                ? "程序主题（无需背景图）"
                : "可选叠层：" + Path.GetFileName(scene.EffectiveBackgroundResource),
            AuraToolsUi.Text,
            PickBackground,
            "替换");
        AddSummaryRow(
            "冒险队伍",
            "跟随实际参与玩家 · 当前角色皮肤 · 1–8 人自动布局",
            AuraToolsUi.Text);

        var duration = Horizontal("Duration", bodyContent!, 50f, 8f);
        AuraToolsUi.AddText(duration.transform, "展示时长", AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 112f);
        var durationValues = new List<float?> { null, 2f, 3f, 5f };
        var durationLabels = new List<string> { "沿用默认", "短 · 2 秒", "标准 · 3 秒", "长 · 5 秒" };
        var selected = DurationIndex(scene.Hold);
        if (selected < 0 && scene.Hold.HasValue)
        {
            selected = durationValues.Count;
            durationValues.Add(scene.Hold);
            durationLabels.Add("自定义 · " + scene.Hold.Value.ToString("0.##", CultureInfo.InvariantCulture) + " 秒");
        }
        AuraToolsUi.AddSelectButton(duration.transform, durationLabels, selected, index =>
        {
            scene.Hold = durationValues[Mathf.Clamp(index, 0, durationValues.Count - 1)];
            Save();
            Refresh();
        }, 220f, 40f);
        AuraToolsUi.AddText(duration.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(duration.transform, "高级调整", OpenAdvanced, 104f, 40f);

        if (string.Equals(scene.SceneId, AuraToolsEventCgSceneIds.BattleOpening, StringComparison.OrdinalIgnoreCase))
        {
            AddSummaryRow(
                "适用战斗",
                scene.BattleIds.Count == 0 ? "全部战斗" : "保留迁移范围 · " + scene.BattleIds.Count + " 个战斗",
                AuraToolsUi.Text);
        }

        if (string.Equals(scene.SceneId, AuraToolsEventCgSceneIds.AdventureSettlement, StringComparison.OrdinalIgnoreCase))
        {
            var row = Horizontal("SettlementPolicy", bodyContent!, 50f, 8f);
            AuraToolsUi.AddText(row.transform, "连续终局", AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 112f);
            AuraToolsUi.AddToggle(row.transform,
                AuraToolsConfigService.SkillCg.EventCg.PlaySettlementAfterBattleScene,
                enabled =>
                {
                    AuraToolsConfigService.SkillCg.EventCg.PlaySettlementAfterBattleScene = enabled;
                    Save();
                });
            AuraToolsUi.AddText(row.transform,
                "战斗失败或胜利 CG 后仍播放冒险结算",
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
        }
    }

    private static void BuildPreview()
    {
        var scene = CurrentScene();
        var stageRow = Horizontal("StageRow", bodyContent!, 310f, 8f);
        AuraToolsUi.AddText(stageRow.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
        var stage = AuraToolsUi.CreateLayout("Stage", stageRow.transform);
        AuraToolsUi.SetFixedSize(
            stage,
            AuraToolsCgSettingsLayoutPolicy.EventPreviewWidth,
            AuraToolsCgSettingsLayoutPolicy.EventPreviewHeight);
        var stageBackground = stage.AddComponent<Image>();
        stageBackground.color = new Color(0.03f, 0.03f, 0.08f, 1f);
        stageBackground.raycastTarget = false;
        var request = AuraToolsCgEventSignalService.BuildPreviewRequest(
            scene.SceneId,
            previewParticipants);
        if (request != null)
        {
            embeddedPreview = SkillCgArbiterRuntime.ShowEmbeddedScenePreview(
                AuraToolsIds.ModId,
                stage.transform,
                request,
                success => SetStatus(
                    success
                        ? "嵌入预览与实际播放共用同一组件渲染器。"
                        : "场景资源尚未就绪，无法生成嵌入预览。",
                    !success));
        }
        else
        {
            SetStatus("当前场景不可预览，请确认场景已启用且角色目录可用。", true);
        }

        var captionRoot = AuraToolsUi.CreateRect("Caption", stage.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0.18f), Vector2.zero, Vector2.zero);
        var captionImage = captionRoot.AddComponent<Image>();
        captionImage.color = new Color(0f, 0f, 0f, 0.58f);
        captionImage.raycastTarget = false;
        AuraToolsUi.AddFillText(captionRoot.transform, SceneName(scene.SceneId) + " · 组件化队伍构图",
            AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text);
        AuraToolsUi.AddText(stageRow.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);

        var controls = Horizontal("PreviewControls", bodyContent!, 52f, 8f);
        AuraToolsUi.AddText(controls.transform, "预览人数", AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 92f);
        AuraToolsUi.AddButton(controls.transform, "−", () =>
        {
            previewParticipants = Math.Max(1, previewParticipants - 1);
            Refresh();
        }, 48f, 40f).interactable = previewParticipants > 1;
        AuraToolsUi.AddText(controls.transform, previewParticipants.ToString(CultureInfo.InvariantCulture),
            AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight, 0f, 42f);
        AuraToolsUi.AddButton(controls.transform, "+", () =>
        {
            previewParticipants = Math.Min(8, previewParticipants + 1);
            Refresh();
        }, 48f, 40f).interactable = previewParticipants < 8;
        AuraToolsUi.AddText(controls.transform,
            "实际播放使用当前冒险的真实玩家与角色。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(controls.transform, "播放预览", () =>
        {
            var played = AuraToolsCgEventSignalService.Preview(scene.SceneId, previewParticipants);
            SetStatus(played ? "正在播放场景预览。" : "当前场景不可预览，请确认场景已启用且角色目录可用。", !played);
        }, 112f, 40f);
    }

    private static void AddSummaryRow(
        string label,
        string value,
        Color valueColor,
        Action? action = null,
        string actionLabel = "恢复默认")
    {
        var row = Horizontal("Summary-" + label, bodyContent!, 54f, 8f);
        AuraToolsUi.AddText(row.transform, label, AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 112f);
        AuraToolsUi.AddText(row.transform, value, AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, valueColor, AuraToolsUi.TextMinHeight, 1f);
        if (action != null)
        {
            AuraToolsUi.AddButton(row.transform, actionLabel, action, 104f, 40f);
        }
    }

    private static void PickBackground()
    {
        var scene = CurrentScene();
        var directory = FileResourceUtil.EventCgDirectory(scene.SceneId);
        SetStatus("正在打开图片选择器……");
        OptionalFileDialog.PickImageFileAsync(directory, result =>
        {
            if (!result.Selected)
            {
                SetStatus(result.Status == OptionalFileDialogStatus.Cancelled
                    ? "已取消选择。"
                    : "无法打开图片选择器：" + result.Message,
                    result.Status != OptionalFileDialogStatus.Cancelled);
                return;
            }

            var imported = FileResourceUtil.ImportImagePath(
                result.Path,
                directory,
                "event_" + scene.SceneId.Replace('.', '_'),
                out var message);
            if (string.IsNullOrWhiteSpace(imported))
            {
                SetStatus(message, true);
                return;
            }

            scene.BackgroundResource = imported;
            FileResourceUtil.RegisterManualDirectory(
                "CG",
                "EventCg",
                "Event",
                scene.SceneId,
                AuraToolsIds.ModId,
                "user-imports",
                directory,
                out _);
            Save();
            SetStatus(message + " 已替换当前场景背景。");
            Refresh();
        });
    }

    private static void ResetPresentation()
    {
        CurrentScene().ResetPresentation();
        Save();
        SetStatus("已恢复 AuraToolsExp 默认场景方案。");
        Refresh();
    }

    private static void OpenAdvanced()
    {
        var scene = CurrentScene();
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.EventCg.Advanced",
            windowRoot!,
            SceneName(scene.SceneId) + " · 高级调整",
            Save,
            true,
            760f);
        var content = AuraToolsUi.CreateScroll(window.transform, "EventCgAdvanced");
        AddAdvancedRow(content, "时长", new[]
        {
            Field("淡入", scene.FadeIn, value => scene.FadeIn = value),
            Field("停留", scene.Hold, value => scene.Hold = value),
            Field("淡出", scene.FadeOut, value => scene.FadeOut = value)
        });
        AddAdvancedRow(content, "逻辑画布", new[]
        {
            Field("宽度", scene.BaseWidth, value => scene.BaseWidth = value.HasValue ? (int?)Math.Round(value.Value) : null),
            Field("高度", scene.BaseHeight, value => scene.BaseHeight = value.HasValue ? (int?)Math.Round(value.Value) : null)
        });
    }

    private static void AddAdvancedRow(Transform parent, string title, IEnumerable<NumberField> fields)
    {
        var row = Horizontal("Advanced-" + title, parent, 50f, 8f);
        AuraToolsUi.AddText(row.transform, title, AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 108f);
        foreach (var field in fields)
        {
            AuraToolsUi.AddText(row.transform, field.Label, AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 58f);
            AuraToolsUi.AddInput(row.transform, Format(field.Value), raw =>
            {
                field.Apply(ParseNullable(raw));
                Save();
            }, 92f, 40f);
        }
        AuraToolsUi.AddText(row.transform, "", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);
    }

    private static void SelectCategory(string value)
    {
        category = value;
        Refresh();
    }

    private static void SelectView(bool preview)
    {
        previewMode = preview;
        Refresh();
    }

    private static AuraToolsEventCgSceneSettings CurrentScene()
    {
        return AuraToolsConfigService.SkillCg.EventCg.GetScene(CurrentSceneId());
    }

    private static string CurrentSceneId()
    {
        if (string.Equals(category, CategoryVictory, StringComparison.Ordinal)) return victorySceneId;
        if (string.Equals(category, CategoryOpening, StringComparison.Ordinal)) return AuraToolsEventCgSceneIds.BattleOpening;
        if (string.Equals(category, CategoryDefeat, StringComparison.Ordinal)) return AuraToolsEventCgSceneIds.BattleDefeat;
        return AuraToolsEventCgSceneIds.AdventureSettlement;
    }

    private static string SceneName(string sceneId)
    {
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.VictoryStandard, StringComparison.OrdinalIgnoreCase)) return "普通胜利";
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.VictoryMidas, StringComparison.OrdinalIgnoreCase)) return "点金手胜利";
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.VictoryRitual, StringComparison.OrdinalIgnoreCase)) return "仪式胜利";
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.VictoryCurse, StringComparison.OrdinalIgnoreCase)) return "诅咒胜利";
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.BattleOpening, StringComparison.OrdinalIgnoreCase)) return "战斗开场";
        if (string.Equals(sceneId, AuraToolsEventCgSceneIds.BattleDefeat, StringComparison.OrdinalIgnoreCase)) return "战斗失败";
        return "冒险结算";
    }

    private static int DurationIndex(float? hold)
    {
        if (!hold.HasValue) return 0;
        if (Math.Abs(hold.Value - 2f) < 0.01f) return 1;
        if (Math.Abs(hold.Value - 3f) < 0.01f) return 2;
        if (Math.Abs(hold.Value - 5f) < 0.01f) return 3;
        return -1;
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

    private static void Save()
    {
        AuraToolsConfigService.SkillCg.EventCg.Normalize();
        AuraToolsConfigService.SaveEventCg();
        AuraToolsSkillCgRuntime.ApplyRoleCgConfiguration();
    }

    private static void SetStatus(string message, bool warning = false)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = warning ? AuraToolsUi.WarningText : AuraToolsUi.MutedText;
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

    private static NumberField Field(string label, int? value, Action<float?> apply)
    {
        return new NumberField(label, value, apply);
    }

    private static void Cleanup()
    {
        ReleaseEmbeddedPreview();
        windowRoot = null;
        contextHost = null;
        bodyContent = null;
        statusText = null;
        victoryTab = null;
        openingTab = null;
        defeatTab = null;
        settlementTab = null;
        configTab = null;
        previewTab = null;
    }

    private static void ReleaseEmbeddedPreview()
    {
        embeddedPreview?.Dispose();
        embeddedPreview = null;
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
