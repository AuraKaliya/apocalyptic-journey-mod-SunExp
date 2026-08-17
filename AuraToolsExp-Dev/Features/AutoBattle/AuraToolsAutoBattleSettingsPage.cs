using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;
using AuraToolsUi = AuraToolsExp.Dll.Features.Settings.AuraToolsUi;

namespace AuraToolsExp.Dll.Features.AutoBattle;

public static class AuraToolsAutoBattleSettingsPage
{
    private static bool AutoBattleEvolutionView;
    private static readonly Dictionary<string, bool> FoldoutStates =
        new(StringComparer.Ordinal);

    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.AutoBattleSettings",
            parent,
            "战斗策略实验室",
            maxWidth: 1240f);
        var content = AuraToolsUi.CreateScroll(
            window.transform,
            "AutoBattleSettings");
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
            settings.Profile,
            settings.SelectedModelId);
        BuildAutoBattleDetails(content, settings);
    }

    private static void BuildAutoBattleDetails(
        Transform content,
        AutoBattleSettings autoBattle)
    {
        CreateSectionLabel(content, "当前策略");
        CreateAutoBattleModelApplicationRows(content);
        AuraToolsUi.AddText(
            content,
            "应用模式下，可执行的模型决策不会被规则评分、低置信度或质量门禁替换。只有模型未能加载、推理超时或连续无进展时，才会临时使用技术兜底。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        CreateAutoBattleToggleRow(
            content,
            "完整应用时进入战斗自动接管",
            autoBattle.StartActive,
            value =>
            {
                autoBattle.StartActive = value;
                AuraToolsConfigService.SaveAutoBattle();
            });
        CreateAutoBattleToggleRow(
            content,
            "显示 AI 预测标记",
            autoBattle.ShowPredictionMarkers,
            value =>
            {
                autoBattle.ShowPredictionMarkers = value;
                AuraToolsConfigService.SaveAutoBattle();
            });

        var modelLibrary = CreateCompactFoldout(
            content,
            "模型库与导入",
            "AutoBattle.ModelLibrary");
        CreateAutoBattleModelManagementSection(modelLibrary, autoBattle);

        var developerTools = CreateCompactFoldout(
            content,
            "训练、评估与开发者工具",
            "AutoBattle.DeveloperTools");
        CreateGameParametersSection(developerTools);
        CreateSectionLabel(developerTools, "玩家适配");
        CreateAutoBattlePlayerAdaptationSection(developerTools, autoBattle);
        CreateSectionLabel(developerTools, "评估与诊断");
        CreateAutoBattleAdvancedDiagnosticsSection(developerTools, autoBattle);
    }

    private static void CreateAutoBattlePlayerAdaptationSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            "记录实战决策并在已选底模之上训练玩家偏好残差；底模始终保持冻结。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var contentAdapterIds = AuraToolsCombatContentRuntime
            .SnapshotPolicyAdapters(autoBattle.SelectedModelId)
            .Select(item => item.Manifest.AdapterId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var activeAdapterIds = AuraToolsAutoBattleModelRuntime
            .SnapshotActiveAdapterIds(
                autoBattle.Profile,
                autoBattle.SelectedModelId);
        var personalAdapterCount = activeAdapterIds.Count(id =>
            !contentAdapterIds.Contains(id, StringComparer.Ordinal));
        AuraToolsUi.AddText(
            parent,
            "适配器链：内容 LoRA/低秩 "
            + contentAdapterIds.Length
            + " · 玩家残差 "
            + personalAdapterCount,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);

        CreateAutoBattleToggleRow(
            parent,
            "自动记录实战与完整旅程",
            autoBattle.CaptureTrainingSamples,
            value =>
            {
                autoBattle.CaptureTrainingSamples = value;
                AuraToolsConfigService.SaveAutoBattle();
            });
        var journeyCaptureText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        journeyCaptureText.gameObject
            .AddComponent<AuraToolsAutoBattleJourneyStatusView>()
            .Configure(journeyCaptureText);

        var trainingModeRow = CreateInlineRow(
            parent,
            "AutoBattleTrainingModeRow");
        var trainingModeText = AuraToolsUi.AddText(
            trainingModeRow.transform,
            "采集模式：" + AutoBattleTrainingModeLabel(autoBattle.TrainingMode),
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var trainingModeButton = AuraToolsUi.AddButton(
            trainingModeRow.transform,
            "切换模式",
            () =>
            {
                autoBattle.TrainingMode =
                    NextAutoBattleTrainingMode(autoBattle.TrainingMode);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
                trainingModeText.text =
                    "采集模式："
                    + AutoBattleTrainingModeLabel(autoBattle.TrainingMode);
            },
            96f);
        AttachAutoBattleWorkLock(trainingModeRow, trainingModeButton);

        var trainingPresetRow = CreateInlineRow(
            parent,
            "AutoBattleTrainingPresetRow");
        AuraToolsUi.AddText(
            trainingPresetRow.transform,
            "残差训练预设",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            112f);
        Text? trainingPresetSummary = null;
        var trainingPresetButton = AuraToolsUi.AddSelectButton(
            trainingPresetRow.transform,
            new[] { "稳健", "标准", "强适应", "自定义" },
            AutoBattleTrainingPresetIndex(autoBattle.Training.Preset),
            index =>
            {
                if (index < 3)
                {
                    autoBattle.Training.ApplyPreset(index switch
                    {
                        1 => AutoBattleTrainingSettings.StandardPreset,
                        2 => AutoBattleTrainingSettings.AdaptivePreset,
                        _ => AutoBattleTrainingSettings.SteadyPreset
                    });
                }
                else
                {
                    autoBattle.Training.MarkCustom();
                }
                AuraToolsConfigService.SaveAutoBattle();
                if (trainingPresetSummary != null)
                {
                    trainingPresetSummary.text =
                        AutoBattleTrainingPresetSummary(autoBattle.Training);
                }
            },
            144f);
        AttachAutoBattleWorkLock(trainingPresetRow, trainingPresetButton);
        trainingPresetSummary = AuraToolsUi.AddText(
            trainingPresetRow.transform,
            AutoBattleTrainingPresetSummary(autoBattle.Training),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var parameterContent = CreateCompactFoldout(
            parent,
            "残差训练参数",
            "AutoBattle.PlayerResidualParameters");
        CreateAutoBattleTrainingParameterRows(parameterContent, autoBattle);

        var modelRow = CreateInlineRow(parent, "AutoBattleModelActionRow");
        var trainingStatusText = AuraToolsUi.AddText(
            modelRow.transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var generateButton = AuraToolsUi.AddButton(
            modelRow.transform,
            "训练玩家残差",
            () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueGenerateCandidate(
                        autoBattle.Profile))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][Training] 玩家残差任务正在运行或未能提交");
                }
            },
            128f);
        var cancelTrainingButton = AuraToolsUi.AddButton(
            modelRow.transform,
            "取消",
            () => AuraToolsAutoBattleModelRuntime.CancelTraining(
                autoBattle.Profile),
            66f);

        var promotionRow = CreateInlineRow(
            parent,
            "AutoBattlePromotionActionRow");
        AuraToolsUi.AddText(
            promotionRow.transform,
            "玩家残差版本",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var importButton = AuraToolsUi.AddButton(
            promotionRow.transform,
            "保存已验证版本",
            () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueImportCandidate(
                        autoBattle.Profile))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][Import] 候选尚未通过门禁或保存任务正在运行");
                }
            },
            128f);
        var rollbackButton = AuraToolsUi.AddButton(
            promotionRow.transform,
            "回退上一版本",
            () =>
            {
                if (!AuraToolsAutoBattleModelRuntime.QueueRollbackChampion(
                        autoBattle.Profile))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][Rollback] 没有可回退版本或任务未能提交");
                }
            },
            112f);
        parent.gameObject
            .AddComponent<AuraToolsAutoBattleTrainingStatusView>()
            .Configure(
                autoBattle.Profile,
                trainingStatusText,
                generateButton,
                importButton,
                rollbackButton,
                cancelTrainingButton);
    }

    private static void CreateAutoBattleAdvancedDiagnosticsSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var datasetRow = CreateInlineRow(parent, "AutoBattleDatasetExportRow");
        var datasetStatus = AuraToolsUi.AddText(
            parent,
            "导出当前游戏版本已加载的卡牌、Buff、敌人、关卡、遗物与祝福。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddText(
            datasetRow.transform,
            "游戏数据集",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            datasetRow.transform,
            "导出数据表",
            () =>
            {
                if (AuraToolsCombatKnowledgeRuntime.TryExportBaseGameTables(
                        out var exportedPath,
                        out var exportMessage))
                {
                    datasetStatus.text = exportMessage + "："
                                         + Path.GetFileName(exportedPath);
                    datasetStatus.color = AuraToolsUi.SuccessText;
                }
                else
                {
                    datasetStatus.text = exportMessage;
                    datasetStatus.color = AuraToolsUi.WarningText;
                }
            },
            104f);
        AuraToolsUi.AddButton(
            datasetRow.transform,
            "打开导出目录",
            AuraToolsCombatKnowledgeRuntime.OpenBaseGameTableExportDirectory,
            112f);

        var packageRow = CreateInlineRow(parent, "AutoBattleKnowledgePackageRow");
        AuraToolsUi.AddText(
            packageRow.transform,
            "发布版知识包",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            packageRow.transform,
            "导出并安装",
            () =>
            {
                if (AuraToolsCombatKnowledgeRuntime.TryExportAndInstallRuntimeKnowledgePackage(
                        out var installedPath,
                        out var installMessage))
                {
                    datasetStatus.text = installMessage + "：" + Path.GetFileName(installedPath);
                    datasetStatus.color = AuraToolsUi.SuccessText;
                }
                else
                {
                    datasetStatus.text = installMessage;
                    datasetStatus.color = AuraToolsUi.WarningText;
                }
            },
            112f);
        AuraToolsUi.AddButton(
            packageRow.transform,
            "重载知识包",
            () =>
            {
                AuraToolsCombatKnowledgeRuntime.RequestPackageReload();
                datasetStatus.text = "已提交知识包重载；加载结果请查看本页状态或日志";
                datasetStatus.color = AuraToolsUi.SuccessText;
            },
            112f);
        AuraToolsUi.AddButton(
            packageRow.transform,
            "打开知识目录",
            AuraToolsCombatKnowledgeRuntime.OpenKnowledgeDirectory,
            112f);

        CreateAutoBattleEvaluationSection(parent, autoBattle);

        AuraToolsUi.AddText(
            parent,
            "实机验证",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var gameValidationStatusText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var gameValidationRow = CreateInlineRow(
            parent,
            "AutoBattleGameValidationActions");
        var runGameValidationButton = AuraToolsUi.AddButton(
            gameValidationRow.transform,
            "开始验证",
            () =>
            {
                if (!AuraToolsAutoBattleGameValidationRuntime.Queue(
                        autoBattle,
                        out var validationMessage))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][GameValidation] " + validationMessage);
                }
            },
            96f);
        var cancelGameValidationButton = AuraToolsUi.AddButton(
            gameValidationRow.transform,
            "取消",
            AuraToolsAutoBattleGameValidationRuntime.Cancel,
            66f);
        var openGameValidationButton = AuraToolsUi.AddButton(
            gameValidationRow.transform,
            "打开回执目录",
            AuraToolsAutoBattleGameValidationRuntime.OpenResultDirectory,
            112f);
        gameValidationRow
            .AddComponent<AuraToolsAutoBattleGameValidationStatusView>()
            .Configure(
                gameValidationStatusText,
                runGameValidationButton,
                cancelGameValidationButton,
                openGameValidationButton);

        var gameValidationSettings = autoBattle.GameValidation;
        var gameValidationOptionsRow = CreateInlineRow(
            parent,
            "AutoBattleGameValidationOptionsRow");
        AuraToolsUi.AddToggle(
            gameValidationOptionsRow.transform,
            gameValidationSettings.HidePresentation,
            value =>
            {
                gameValidationSettings.HidePresentation = value;
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
            });
        AuraToolsUi.AddText(
            gameValidationOptionsRow.transform,
            "隐藏战斗画面",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            112f);
        AddAutoBattleSimulationInt(
            gameValidationOptionsRow.transform,
            "每名最终首领场次",
            gameValidationSettings.RepetitionsPerFinalBoss,
            1,
            20,
            value => gameValidationSettings.RepetitionsPerFinalBoss = value,
            autoBattle);

        var clearDataRow = CreateInlineRow(parent, "AutoBattleClearDataRow");
        AuraToolsUi.AddText(
            clearDataRow.transform,
            "危险操作：永久清空实战样本、玩家残差与评估结果",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.WarningText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(
            clearDataRow.transform,
            "清空玩家训练数据",
            () =>
            {
                if (AuraToolsAutoBattleModelRuntime
                        .TryClearAllCombatLearningData(out var clearMessage))
                {
                    AuraToolsLog.Info("[AutoBattle][Clear] " + clearMessage);
                }
                else
                {
                    AuraToolsLog.Warn("[AutoBattle][Clear] " + clearMessage);
                }
            },
            144f);
    }

    private static void CreateGameParametersSection(Transform parent)
    {
        var host = CreateVerticalStack(
            parent,
            "AutoBattleGameParametersSection");
        RebuildGameParametersSection(host.transform);
    }

    private static void RebuildGameParametersSection(Transform host)
    {
        var viewState = AuraUiViewState.CaptureForContent(host);
        AuraToolsUi.ClearChildren(host);
        BuildGameParametersSection(host);
        AuraUiViewState.RestoreAfterLayout(
            host,
            viewState,
            "AuraTools.AutoBattle.GameParameters");
    }

    private static void BuildGameParametersSection(Transform parent)
    {
        CreateSectionLabel(parent, "适用游戏主体");
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        autoBattle.Normalize();
        var parameters = autoBattle.GameParameters;
        var preset = parameters.ActivePreset;

        var presetRow = CreateInlineRow(parent, "AutoBattleGamePresetRow");
        AuraToolsUi.AddText(
            presetRow.transform,
            "角色 + 使魔预设",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            128f);
        var presetButton = AuraToolsUi.AddSelectButton(
            presetRow.transform,
            parameters.Presets.Select(item => item.DisplayName).ToArray(),
            Math.Max(
                0,
                parameters.Presets.FindIndex(item => string.Equals(
                    item.Id,
                    parameters.SelectedPresetId,
                    StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                if (index < 0 || index >= parameters.Presets.Count)
                {
                    return;
                }
                parameters.SelectedPresetId = parameters.Presets[index].Id;
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
                RebuildGameParametersSection(parent);
            },
            172f);
        var addPresetButton = AuraToolsUi.AddButton(
            presetRow.transform,
            "新增预设",
            () =>
            {
                var number = parameters.Presets.Count + 1;
                var id = "preset-" + number;
                while (parameters.Presets.Any(item => string.Equals(
                           item.Id,
                           id,
                           StringComparison.OrdinalIgnoreCase)))
                {
                    id = "preset-" + ++number;
                }
                var clone = preset.CloneAs(id, "游戏预设 " + number);
                parameters.Presets.Add(clone);
                parameters.SelectedPresetId = clone.Id;
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
                RebuildGameParametersSection(parent);
            },
            88f);
        var deletePresetButton = AuraToolsUi.AddButton(
            presetRow.transform,
            "删除",
            () =>
            {
                if (parameters.Presets.Count <= 1)
                {
                    return;
                }
                parameters.Presets.Remove(preset);
                parameters.SelectedPresetId = parameters.Presets[0].Id;
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
                RebuildGameParametersSection(parent);
            },
            66f);
        deletePresetButton.interactable = parameters.Presets.Count > 1;
        AttachAutoBattleWorkLock(
            presetRow,
            presetButton,
            addPresetButton,
            deletePresetButton);

        var roles = RoleCatalog.GetRoles();
        var partners = PartnerCatalog.GetPartners();
        var identityRow = CreateInlineRow(parent, "AutoBattleGameIdentityRow");
        AuraToolsUi.AddText(
            identityRow.transform,
            "角色",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            56f);
        var roleItems = roles.Count > 0
            ? roles.ToList()
            : new List<RoleInfo>
            {
                new() { Id = preset.RoleId, DisplayName = preset.RoleId }
            };
        var roleButton = AuraToolsUi.AddSelectButton(
            identityRow.transform,
            roleItems.Select(item => item.DisplayName).ToArray(),
            Math.Max(
                0,
                roleItems.FindIndex(item => string.Equals(
                    item.Id,
                    preset.RoleId,
                    StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                if (index < 0 || index >= roleItems.Count)
                {
                    return;
                }
                preset.RoleId = roleItems[index].Id;
                preset.ResolvedRoleSkillIds = roleItems[index].Skills
                    .Select(item => item.Id)
                    .ToList();
                preset.ResolvedRoleInitialStatuses =
                    new Dictionary<string, int>(
                        roleItems[index].InitialStatuses,
                        StringComparer.OrdinalIgnoreCase);
                preset.ResolvedRoleSkillCooldownTurns = roleItems[index].Skills
                    .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => Math.Max(
                            1,
                            group.First().CooldownTurns),
                        StringComparer.OrdinalIgnoreCase);
                AuraToolsAutoBattleGameParameterRuntime
                    .ResolvePresetReferences(autoBattle);
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
            },
            164f);
        AuraToolsUi.AddText(
            identityRow.transform,
            "使魔",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            56f);
        var partnerItems = partners.Count > 0
            ? partners.ToList()
            : new List<PartnerInfo>
            {
                new() { Id = preset.PartnerId, DisplayName = preset.PartnerId }
            };
        var partnerButton = AuraToolsUi.AddSelectButton(
            identityRow.transform,
            partnerItems.Select(item => item.DisplayName).ToArray(),
            Math.Max(
                0,
                partnerItems.FindIndex(item => string.Equals(
                    item.Id,
                    preset.PartnerId,
                    StringComparison.OrdinalIgnoreCase))),
            index =>
            {
                if (index < 0 || index >= partnerItems.Count)
                {
                    return;
                }
                preset.PartnerId = partnerItems[index].Id;
                preset.ResolvedFamiliarBlessingIds =
                    partnerItems[index].BlessingIds.ToList();
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
            },
            164f);
        AttachAutoBattleWorkLock(identityRow, roleButton, partnerButton);

        var deckRow = CreateInlineRow(parent, "AutoBattlePreferredDeckSizeRow");
        var deckMinimum = AddAutoBattleSimulationInt(
            deckRow.transform,
            "卡组倾向下限",
            preset.PreferredDeckSizeMinimum,
            1,
            80,
            value => preset.PreferredDeckSizeMinimum = value,
            autoBattle);
        var deckMaximum = AddAutoBattleSimulationInt(
            deckRow.transform,
            "卡组倾向上限",
            preset.PreferredDeckSizeMaximum,
            1,
            80,
            value => preset.PreferredDeckSizeMaximum = value,
            autoBattle);
        AttachAutoBattleWorkLock(deckRow, deckMinimum, deckMaximum);

        const string packFoldoutKey = "AutoBattle.GameParameters.CardPacks";
        var packExpanded = FoldoutStates.TryGetValue(
            packFoldoutKey,
            out var storedExpanded)
            && storedExpanded;
        var packHeader = CreateInlineRow(parent, "AutoBattleRewardCardPackHeader");
        var packSummaryText = AuraToolsUi.AddText(
            packHeader.transform,
            "奖励卡包范围：" + preset.EnabledRewardCardPackIds.Count + " 个",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var packFoldoutButton = AuraToolsUi.AddButton(
            packHeader.transform,
            packExpanded ? "收起" : "展开",
            () =>
            {
                FoldoutStates[packFoldoutKey] = !packExpanded;
                RebuildGameParametersSection(parent);
            },
            72f);
        AttachAutoBattleWorkLock(packHeader, packFoldoutButton);
        if (!packExpanded)
        {
            return;
        }

        foreach (var pack in AuraToolsAutoBattleGameParameterRuntime
                     .GetRewardCardPacks())
        {
            var packRow = CreateInlineRow(
                parent,
                "AutoBattleRewardCardPack-" + pack.Id);
            var enabled = preset.EnabledRewardCardPackIds.Contains(
                pack.Id,
                StringComparer.OrdinalIgnoreCase);
            var packToggle = AuraToolsUi.AddToggle(
                packRow.transform,
                enabled,
                value =>
                {
                    preset.EnabledRewardCardPackIds.RemoveAll(item =>
                        string.Equals(
                            item,
                            pack.Id,
                            StringComparison.OrdinalIgnoreCase));
                    if (value || pack.Required)
                    {
                        preset.EnabledRewardCardPackIds.Add(pack.Id);
                    }
                    autoBattle.Normalize();
                    AuraToolsConfigService.SaveAutoBattle();
                    packSummaryText.text =
                        "奖励卡包范围："
                        + preset.EnabledRewardCardPackIds.Count
                        + " 个";
                });
            packToggle.interactable = !pack.Required;
            AuraToolsUi.AddText(
                packRow.transform,
                pack.DisplayName
                + "  ["
                + pack.Id
                + "]"
                + (pack.Required ? "（基础包，固定开启）" : ""),
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                pack.Required ? AuraToolsUi.MutedText : AuraToolsUi.Text,
                AuraToolsUi.TextMinHeight,
                1f);
            AttachAutoBattleWorkLock(packRow, packToggle);
        }
    }
    private static void CreateSectionLabel(Transform parent, string title)
    {
        var label = AuraToolsUi.CreateLayout("Section-" + title, parent);
        var labelElement = label.AddComponent<LayoutElement>();
        labelElement.minHeight = AuraToolsUi.SectionHeight;
        labelElement.preferredHeight = AuraToolsUi.SectionHeight;
        AuraToolsUi.AddImage(label, AuraToolsUi.Header);
        var layout = label.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 3, 3);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        AuraToolsUi.AddText(label.transform, title, AuraToolsUi.SectionFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Accent, AuraToolsUi.TextMinHeight, 1f);
    }
    private static GameObject CreateInlineRow(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraUiStableId.Assign(row, "row." + name);
        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = AuraToolsUi.InlineRowHeight;
        rowElement.preferredHeight = AuraToolsUi.InlineRowHeight;
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }

    private static GameObject CreateVerticalStack(
        Transform parent,
        string name,
        float spacing = 6f)
    {
        var host = AuraToolsUi.CreateLayout(name, parent);
        AuraUiStableId.Assign(host, "stack." + name);
        var layout = host.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return host;
    }

    private static Transform CreateCompactFoldout(
        Transform parent,
        string title,
        string stateKey)
    {
        var box = CreateVerticalStack(
            parent,
            "CompactFoldout-" + stateKey);
        var header = CreateInlineRow(
            box.transform,
            "CompactFoldoutHeader-" + stateKey);
        AuraToolsUi.AddText(
            header.transform,
            title,
            AuraToolsUi.ModuleTitleFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            AuraToolsUi.TextMinHeight,
            1f);
        var expanded = FoldoutStates.TryGetValue(
                           stateKey,
                           out var stored)
                       && stored;
        var body = CreateVerticalStack(
            box.transform,
            "CompactFoldoutBody-" + stateKey);
        Text? buttonLabel = null;
        var button = AuraToolsUi.AddButton(
            header.transform,
            expanded ? "收起" : "展开",
            () =>
            {
                expanded = !expanded;
                FoldoutStates[stateKey] = expanded;
                AuraToolsUi.SetFoldoutExpanded(
                    body,
                    expanded,
                    box.transform);
                if (buttonLabel != null)
                {
                    buttonLabel.text = expanded ? "收起" : "展开";
                }
            },
            72f);
        buttonLabel = button.GetComponentInChildren<Text>(true);
        AuraToolsUi.SetFoldoutExpanded(
            body,
            expanded,
            box.transform);
        return body.transform;
    }

    private static void SetSegmentActive(Button button, bool active)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.color = active ? AuraToolsUi.Accent : AuraToolsUi.Text;
        }
        var image = button.GetComponent<Image>();
        if (image != null && active)
        {
            image.color = new Color(0.22f, 0.17f, 0.32f, 1f);
        }
    }

    private static void AttachAutoBattleWorkLock(
        GameObject host,
        params Selectable[] controls)
    {
        host.AddComponent<AuraToolsAutoBattleWorkLockView>().Configure(controls);
    }
    private static Toggle CreateAutoBattleToggleRow(
        Transform parent,
        string label,
        bool value,
        Action<bool> changed)
    {
        var row = CreateInlineRow(parent, "AutoBattleToggle-" + label);
        var toggle = AuraToolsUi.AddToggle(row.transform, value, changed);
        AuraToolsUi.AddText(row.transform, label, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        return toggle;
    }

    private static void CreateAutoBattleTrainingParameterRows(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var first = CreateInlineRow(parent, "AutoBattleTrainingParameters1");
        AddAutoBattleTrainingInt(
            first.transform,
            "训练轮数",
            autoBattle.Training.Epochs,
            value => autoBattle.Training.Epochs = Math.Max(20, Math.Min(300, value)),
            autoBattle);
        AddAutoBattleTrainingDouble(
            first.transform,
            "学习率",
            autoBattle.Training.LearningRate,
            value => autoBattle.Training.LearningRate = Math.Max(0.005d, Math.Min(0.1d, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            first,
            first.GetComponentsInChildren<Selectable>(true));

        var second = CreateInlineRow(parent, "AutoBattleTrainingParameters2");
        AddAutoBattleTrainingDouble(
            second.transform,
            "L2 正则",
            autoBattle.Training.L2,
            value => autoBattle.Training.L2 = Math.Max(0d, Math.Min(0.02d, value)),
            autoBattle);
        AddAutoBattleTrainingDouble(
            second.transform,
            "最大修正",
            autoBattle.Training.MaximumCorrection,
            value => autoBattle.Training.MaximumCorrection = Math.Max(0.25d, Math.Min(2d, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            second,
            second.GetComponentsInChildren<Selectable>(true));

        var third = CreateInlineRow(parent, "AutoBattleTrainingParameters3");
        AddAutoBattleTrainingInt(
            third.transform,
            "最低偏好对",
            autoBattle.Training.MinimumPreferencePairs,
            value => autoBattle.Training.MinimumPreferencePairs = Math.Max(5, Math.Min(200, value)),
            autoBattle);
        AddAutoBattleTrainingInt(
            third.transform,
            "类别最低样本",
            autoBattle.Training.MinimumCategoryObservations,
            value => autoBattle.Training.MinimumCategoryObservations = Math.Max(3, Math.Min(100, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            third,
            third.GetComponentsInChildren<Selectable>(true));

        var fourth = CreateInlineRow(parent, "AutoBattleTrainingParameters4");
        AddAutoBattleTrainingInt(
            fourth.transform,
            "完整战斗数",
            autoBattle.Training.MinimumEpisodes,
            value => autoBattle.Training.MinimumEpisodes = Math.Max(2, Math.Min(10000, value)),
            autoBattle);
        AddAutoBattleTrainingInt(
            fourth.transform,
            "网络隐藏维度",
            autoBattle.Training.PolicyValueHiddenDimensions,
            value => autoBattle.Training.PolicyValueHiddenDimensions = Math.Max(8, Math.Min(256, value)),
            autoBattle);
        AttachAutoBattleWorkLock(
            fourth,
            fourth.GetComponentsInChildren<Selectable>(true));
    }

    private static void CreateAutoBattleModelManagementSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var host = CreateVerticalStack(
            parent,
            "AutoBattleModelManagementSection");
        void Build()
        {
            AuraToolsUi.ClearChildren(host.transform);
            CreateAutoBattleSimulationRows(
                host.transform,
                autoBattle,
                includeModelManagement: true,
                includeEvaluation: false);
        }
        Build();
        host.AddComponent<AuraToolsLocalSectionRefreshView>().Configure(
            () =>
            {
                var snapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId);
                var external =
                    AuraToolsAutoBattleModelRuntime
                        .SnapshotExternalValidationModel();
                return snapshot.Revision
                       + "|"
                       + autoBattle.Profile
                       + "|"
                       + autoBattle.SelectedModelId
                       + "|"
                       + autoBattle.ExperimentalModelAcknowledgement
                       + "|"
                       + (external?.PackageSha256 ?? "none");
            },
            Build);
    }

    private static void CreateAutoBattleModelApplicationRows(Transform parent)
    {
        var statusText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var row = CreateInlineRow(
            parent,
            "AutoBattleModelApplicationModeRow");
        AuraToolsUi.AddText(
            row.transform,
            "运行方式",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            92f);
        AuraToolsAutoBattleModelApplicationStatusView? view = null;
        Button AddModeButton(string mode, string label)
        {
            return AuraToolsUi.AddButton(
                row.transform,
                label,
                () =>
                {
                    if (!AuraToolsAutoBattleRuntime
                            .TrySetModelApplicationMode(
                                mode,
                                out _,
                                out var message))
                    {
                        AuraToolsLog.Warn(
                            "[AutoBattle][ModelActivation] " + message);
                    }
                    else
                    {
                        AuraToolsLog.Info(
                            "[AutoBattle][ModelActivation] " + message);
                    }
                    view?.RefreshNow();
                },
                104f);
        }
        var shadowButton = AddModeButton("shadow", "影子评估");
        var trialButton = AddModeButton("trial", "实机试用");
        var fullButton = AddModeButton("full", "完整应用");
        view = row.AddComponent<
            AuraToolsAutoBattleModelApplicationStatusView>();
        view.Configure(
            statusText,
            shadowButton,
            trialButton,
            fullButton);
    }

    private static void CreateAutoBattleEvaluationSection(
        Transform parent,
        AutoBattleSettings autoBattle)
    {
        var host = CreateVerticalStack(
            parent,
            "AutoBattleEvaluationSection");
        void Build()
        {
            AuraToolsUi.ClearChildren(host.transform);
            CreateAutoBattleSimulationRows(
                host.transform,
                autoBattle,
                includeModelManagement: false,
                includeEvaluation: true);
        }
        Build();
        host.AddComponent<AuraToolsLocalSectionRefreshView>().Configure(
            () =>
            {
                var evaluationModelId = string.IsNullOrWhiteSpace(
                    autoBattle.EvaluationModelId)
                    ? autoBattle.SelectedModelId
                    : autoBattle.EvaluationModelId;
                var snapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
                    autoBattle.Profile,
                    evaluationModelId);
                return snapshot.Revision
                       + "|"
                       + autoBattle.Profile
                       + "|"
                       + autoBattle.Simulation.ScenarioId
                       + "|"
                       + autoBattle.Simulation.DifficultyId
                       + "|"
                       + AutoBattleEvolutionView;
            },
            Build);
    }

    private static void CreateAutoBattleSimulationRows(
        Transform parent,
        AutoBattleSettings autoBattle,
        bool includeModelManagement,
        bool includeEvaluation)
    {
        var evaluationModelId = string.IsNullOrWhiteSpace(
            autoBattle.EvaluationModelId)
            ? autoBattle.SelectedModelId
            : autoBattle.EvaluationModelId;
        var uiSnapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
            autoBattle.Profile,
            evaluationModelId);
        if (!uiSnapshot.Ready)
        {
            AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                autoBattle.Profile,
                evaluationModelId);
        }
        var fixedCampaignSelected = string.Equals(
            autoBattle.Simulation.ScenarioId,
            "witch.world-simulation.standard-v2",
            StringComparison.OrdinalIgnoreCase);
        if (fixedCampaignSelected)
        {
            AutoBattleEvolutionView = false;
        }
        var lockedControls = new List<Selectable>();
        if (includeModelManagement)
        {
        var library = uiSnapshot.Models
            .Where(item => string.Equals(
                item.ModelPurpose,
                "foundation",
                StringComparison.Ordinal))
            .ToList();
        var modelRow = CreateInlineRow(parent, "AutoBattleModelLibraryRow");
        AuraToolsUi.AddText(
            modelRow.transform,
            "模型",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            64f);
        var modelLabels = new List<string> { "未选择底模" };
        modelLabels.AddRange(library.Select(item =>
            (string.Equals(
                item.DeploymentTier,
                CombatFoundationDeploymentTier.Experimental,
                StringComparison.OrdinalIgnoreCase)
                ? string.Equals(
                    item.CapabilityStatus,
                    CombatFoundationModelPackageProtocol.CapabilityStatusFail,
                    StringComparison.Ordinal)
                    ? "【实验·回退】"
                    : "【实验】"
                : "【正式】")
            + item.DisplayName));
        var selectedModelIndex = library.FindIndex(item => string.Equals(
            item.ModelId,
            autoBattle.SelectedModelId,
            StringComparison.Ordinal));
        var modelButton = AuraToolsUi.AddSelectButton(
            modelRow.transform,
            modelLabels,
            selectedModelIndex < 0 ? 0 : selectedModelIndex + 1,
            index =>
            {
                autoBattle.SelectedModelId = index <= 0 || index > library.Count
                    ? ""
                    : library[index - 1].ModelId;
                autoBattle.EvaluationModelId = "";
                if (AuraToolsAutoBattleModelRuntime
                    .IsExperimentalFoundationModel(
                        autoBattle.SelectedModelId)
                    && !AuraToolsAutoBattleModelRuntime
                        .IsExperimentalFoundationAcknowledged(
                            autoBattle.SelectedModelId))
                {
                    autoBattle.TrainedModelMode = "shadow";
                }
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
                AuraToolsAutoBattleRuntime.ReloadModels();
                AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId,
                    force: true);
            },
            320f);
        lockedControls.Add(modelButton);
        var renameValue = selectedModelIndex >= 0
            ? library[selectedModelIndex].DisplayName
            : "";
        var renameRow = CreateInlineRow(
            parent,
            "AutoBattleModelRenameRow");
        AuraToolsUi.AddText(
            renameRow.transform,
            "名称",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            64f);
        var renameInput = AuraToolsUi.AddInput(
            renameRow.transform,
            renameValue,
            value => renameValue = value,
            260f);
        renameInput.interactable = selectedModelIndex >= 0;
        var renameButton = AuraToolsUi.AddButton(
            renameRow.transform,
            "改名",
            () =>
            {
                if (AuraToolsAutoBattleModelRuntime.TryRenameLibraryModel(
                        autoBattle.SelectedModelId,
                        renameValue,
                        out var renameMessage))
                {
                    AuraToolsLog.Info("[AutoBattle][ModelLibrary] " + renameMessage);
                }
                else
                {
                    AuraToolsLog.Warn("[AutoBattle][ModelLibrary] " + renameMessage);
                }
                AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId,
                    force: true);
            },
            66f);
        renameButton.interactable = selectedModelIndex >= 0;
        var restoreNameButton = AuraToolsUi.AddButton(
            renameRow.transform,
            "自动命名",
            () =>
            {
                if (AuraToolsAutoBattleModelRuntime
                    .TryRestoreGeneratedLibraryModelName(
                        autoBattle.SelectedModelId,
                        out var restoreMessage))
                {
                    AuraToolsLog.Info(
                        "[AutoBattle][ModelLibrary] " + restoreMessage);
                }
                else
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][ModelLibrary] " + restoreMessage);
                }
                AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId,
                    force: true);
            },
            88f);
        restoreNameButton.interactable = selectedModelIndex >= 0
                                         && string.Equals(
                                             library[selectedModelIndex]
                                                 .ModelPurpose,
                                             "foundation",
                                             StringComparison.Ordinal);
        renameRow.SetActive(selectedModelIndex >= 0);
        var selectedEntry = selectedModelIndex >= 0
            ? library[selectedModelIndex]
            : null;
        var selectedExperimental = selectedEntry != null
                                   && string.Equals(
                                       selectedEntry.DeploymentTier,
                                       CombatFoundationDeploymentTier.Experimental,
                                       StringComparison.OrdinalIgnoreCase);
        var selectedCapabilityRegression = selectedExperimental
                                           && string.Equals(
                                               selectedEntry!.CapabilityStatus,
                                               CombatFoundationModelPackageProtocol
                                                   .CapabilityStatusFail,
                                               StringComparison.Ordinal);
        AuraToolsUi.AddText(
            parent,
            library.Count == 0
                ? "模型库为空"
                : selectedModelIndex < 0
                    ? "模型库：" + library.Count + " 个"
                    : "主体："
                      + library[selectedModelIndex].RoleId
                      + " / "
                      + library[selectedModelIndex].PartnerId
                      + " · 卡包 "
                      + library[selectedModelIndex]
                          .EnabledRewardCardPackIds.Count
                      + " · "
                      + (library[selectedModelIndex].CoverageLevel == "full"
                          ? "完全覆盖"
                          : "部分覆盖")
                      + " · "
                      + (selectedCapabilityRegression
                          ? "实验底模（能力回退）"
                          : selectedExperimental ? "实验底模" : "正式底模")
                      + " · 来源 "
                      + (string.IsNullOrWhiteSpace(
                            library[selectedModelIndex].DistributionOrigin)
                          ? "未知"
                          : library[selectedModelIndex].DistributionOrigin),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        if (selectedExperimental)
        {
            var acknowledged = AuraToolsAutoBattleModelRuntime
                .IsExperimentalFoundationAcknowledged(
                    autoBattle.SelectedModelId);
            AuraToolsUi.AddText(
                parent,
                selectedCapabilityRegression
                    ? acknowledged
                        ? "⚠ 高风险实验底模：能力探针已检测到相对基线回退，已确认仅用于实机配置测试与问题收集。"
                        : "⚠ 高风险实验底模：能力探针已检测到相对基线回退；确认前不能主动接管战斗。"
                    : acknowledged
                        ? "⚠ 实验底模：已确认效果可能与正式底模存在差异；主动运行期间持续按实验模型标识。"
                        : "⚠ 实验底模：技术格式与运行安全已通过，但尚未取得正式质量认证；确认前不能主动接管战斗。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText,
                AuraToolsUi.TextMinHeight,
                1f);
            var acknowledgementRow = CreateInlineRow(
                parent,
                "AutoBattleExperimentalFoundationAcknowledgement");
            AuraToolsUi.AddText(
                acknowledgementRow.transform,
                acknowledged ? "实验风险已确认" : "需要显式确认",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText,
                AuraToolsUi.TextMinHeight,
                1f);
            var acknowledgementButton = AuraToolsUi.AddButton(
                acknowledgementRow.transform,
                acknowledged ? "已确认" : "确认使用实验底模",
                () =>
                {
                    AuraToolsAutoBattleModelRuntime
                        .TryAcknowledgeExperimentalFoundation(
                            autoBattle.SelectedModelId,
                            out var acknowledgementMessage);
                    AuraToolsLog.Warn(
                        "[AutoBattle][ExperimentalFoundation] "
                        + acknowledgementMessage);
                    AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                        autoBattle.Profile,
                        autoBattle.SelectedModelId,
                        force: true);
                },
                156f);
            acknowledgementButton.interactable = !acknowledged;
        }

        var bundledStatus =
            AuraToolsBundledFoundationModelRuntime.SnapshotStatus();
        var bundledStatusText = AuraToolsUi.AddText(
            parent,
            "Model 批量导入：" + bundledStatus.Message,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddText(
            parent,
            "按 Model/角色名 [RoleId]/使魔名 [PartnerId]/[可选的用户发布名]/ 放入固定模型文件；哈希、卡包和版本由程序识别，注册后不会自动选择或启用。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var bundledRow = CreateInlineRow(
            parent,
            "AutoBattleBundledFoundationActions");
        var bundledImportButton = AuraToolsUi.AddButton(
            bundledRow.transform,
            "导入底模",
            () =>
            {
                if (AuraToolsBundledFoundationModelRuntime.TryQueueRescan(
                        out var scanMessage))
                {
                    bundledStatusText.text = "Model 批量导入：" + scanMessage;
                    AuraToolsLog.Info(
                        "[AutoBattle][BundledModels] " + scanMessage);
                }
                else
                {
                    bundledStatusText.text = "Model 批量导入：" + scanMessage;
                    AuraToolsLog.Warn(
                        "[AutoBattle][BundledModels] " + scanMessage);
                }
            },
            104f);
        bundledStatusText.gameObject
            .AddComponent<AuraToolsBundledFoundationImportStatusView>()
            .Configure(bundledStatusText, bundledImportButton);

        var externalEntry =
            AuraToolsAutoBattleModelRuntime.SnapshotExternalValidationModel();
        var externalStatusText = AuraToolsUi.AddText(
            parent,
            externalEntry == null
                ? "待验底模：未选择"
                : "待验底模：" + externalEntry.DisplayName,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var externalRow = CreateInlineRow(
            parent,
            "AutoBattleExternalFoundationValidationActions");
        var selectExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "导入外部待验包",
            () =>
            {
                OptionalFileDialog.PickFileAsync(
                    "选择外部待验底模包",
                    new[]
                    {
                        new OptionalFileDialogFilter(
                            "Aura 待验底模包",
                            "foundation-model-package-v5.json;foundation-model-package-v4.json;foundation-model-package-v3.json;*.aura-model.json"),
                        new OptionalFileDialogFilter("JSON 文件", "*.json")
                    },
                    "json",
                    AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
                    result =>
                    {
                        if (!result.Selected)
                        {
                            if (result.Status
                                != OptionalFileDialogStatus.Cancelled)
                            {
                                AuraToolsLog.Warn(
                                    "[AutoBattle][ExternalValidation] "
                                    + result.Message);
                            }
                            return;
                        }
                        if (AuraToolsAutoBattleModelRuntime
                            .TryStageExternalFoundationPackage(
                                result.Path,
                                out var externalModelId,
                                out var stageMessage))
                        {
                            autoBattle.EvaluationModelId = externalModelId;
                            autoBattle.Normalize();
                            AuraToolsConfigService.SaveAutoBattle();
                            AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                                autoBattle.Profile,
                                externalModelId,
                                force: true);
                            AuraToolsLog.Info(
                                "[AutoBattle][ExternalValidation] "
                                + stageMessage);
                        }
                        else
                        {
                            AuraToolsLog.Warn(
                                "[AutoBattle][ExternalValidation] "
                                + stageMessage);
                        }
                    });
            },
            142f);
        var promoteExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "加入模型库",
            () =>
            {
                var targetId = autoBattle.EvaluationModelId;
                if (!AuraToolsAutoBattleModelRuntime
                    .ExternalValidationMeetsGate(
                        autoBattle.Profile,
                        targetId,
                        out var gateMessage))
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][ExternalValidation] 尚不能入库："
                        + gateMessage);
                    return;
                }
                if (AuraToolsAutoBattleModelRuntime
                    .TryPromoteExternalValidationModel(
                        autoBattle.Profile,
                        targetId,
                        out var promotedModelId,
                        out var promoteMessage))
                {
                    autoBattle.SelectedModelId = promotedModelId;
                    autoBattle.EvaluationModelId = "";
                    autoBattle.TrainedModelMode = "off";
                    AuraToolsAutoBattleModelRuntime
                        .ClearExternalValidationModel();
                    AuraToolsConfigService.SaveAutoBattle();
                    AuraToolsAutoBattleRuntime.ReloadModels();
                    AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                        autoBattle.Profile,
                        promotedModelId,
                        force: true);
                    AuraToolsLog.Info(
                        "[AutoBattle][ExternalValidation] "
                        + promoteMessage);
                }
                else
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][ExternalValidation] "
                        + promoteMessage);
                }
            },
            96f);
        var clearExternalButton = AuraToolsUi.AddButton(
            externalRow.transform,
            "清除待验",
            () =>
            {
                AuraToolsAutoBattleModelRuntime.ClearExternalValidationModel();
                autoBattle.EvaluationModelId = "";
                AuraToolsConfigService.SaveAutoBattle();
            },
            80f);
        externalRow
            .AddComponent<AuraToolsAutoBattleExternalValidationStatusView>()
            .Configure(
                autoBattle.Profile,
                externalStatusText,
                selectExternalButton,
                promoteExternalButton,
                clearExternalButton);
        }

        if (!includeEvaluation)
        {
            AttachAutoBattleWorkLock(
                parent.gameObject,
                lockedControls.ToArray());
            return;
        }

        AuraToolsUi.AddText(
            parent,
            "标准评估（仅用于本地候选晋级）",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var modeRow = CreateInlineRow(parent, "AutoBattleSimulationModeRow");
        AuraToolsUi.AddText(
            modeRow.transform,
            "评估方式",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var pairedModeButton = AuraToolsUi.AddButton(
            modeRow.transform,
            "对照评估",
            () =>
            {
                AutoBattleEvolutionView = false;
            },
            92f);
        Button? evolutionModeButton = null;
        if (!fixedCampaignSelected)
        {
            evolutionModeButton = AuraToolsUi.AddButton(
                modeRow.transform,
                "高级：策略进化",
                () =>
                {
                    AutoBattleEvolutionView = true;
                },
                128f);
        }
        SetSegmentActive(pairedModeButton, !AutoBattleEvolutionView);
        if (evolutionModeButton != null)
        {
            SetSegmentActive(evolutionModeButton, AutoBattleEvolutionView);
        }
        AuraToolsUi.AddText(
            parent,
            AuraToolsCombatKnowledgeRuntime.DescribeLoadedPackages(),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        var scenarios = uiSnapshot.ScenarioIds.ToList();
        var scenarioLabels = scenarios.Count == 0
            ? new List<string> { "未注册场景" }
            : scenarios;
        var selectedScenario = Math.Max(
            0,
            scenarios.FindIndex(id => string.Equals(
                id,
                autoBattle.Simulation.ScenarioId,
                StringComparison.OrdinalIgnoreCase)));
        var scenarioRow = CreateInlineRow(parent, "AutoBattleSimulationScenarioRow");
        AuraToolsUi.AddText(
            scenarioRow.transform,
            "场景",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var scenarioButton = AuraToolsUi.AddSelectButton(
            scenarioRow.transform,
            scenarioLabels,
            selectedScenario,
            index =>
            {
                if (index >= 0 && index < scenarios.Count)
                {
                    autoBattle.Simulation.ScenarioId = scenarios[index];
                    autoBattle.Normalize();
                    AuraToolsConfigService.SaveAutoBattle();
                }
            },
            260f);
        scenarioButton.interactable = scenarios.Count > 0;
        if (scenarios.Count > 0)
        {
            lockedControls.Add(scenarioButton);
        }
        AuraToolsUi.AddButton(
            scenarioRow.transform,
            "刷新",
            () => AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                autoBattle.Profile,
                evaluationModelId,
                force: true),
            66f);
        AuraToolsUi.AddButton(
            scenarioRow.transform,
            "高级输入目录",
            AuraToolsAutoBattleSimulationRuntime.OpenInputDirectory,
            112f);
        AuraToolsUi.AddText(
            parent,
            scenarios.Count == 0
                ? "未找到随 MOD 发布的标准评估包。请检查 AuraToolsExp/Config/combat-simulation 是否完整，或重新安装 MOD。"
                : "标准 v2 固定 7 层：前 6 层均为 2普通＋1精英＋2普通＋1首领，第 7 层从勇者卡洛琳、永夜化身、魔王、神圣审判机关中抽取最终首领。只使用游戏主体内容。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            scenarios.Count == 0
                ? new Color(1f, 0.68f, 0.3f, 1f)
                : AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        if (fixedCampaignSelected)
        {
            AuraToolsUi.AddText(
                parent,
                "提示：固定七层战役用于正式对照评估；“策略进化”只对高级输入目录中的单场 *.scenario.json 开放。",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
        }

        var difficultyRow = CreateInlineRow(parent, "AutoBattleSimulationDifficultyRow");
        AuraToolsUi.AddText(
            difficultyRow.transform,
            "敌人难度",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var difficulties = new List<string> { "普通难度（无词条）", "高级难度（本体满词条）" };
        var difficultyButton = AuraToolsUi.AddSelectButton(
            difficultyRow.transform,
            difficulties,
            string.Equals(
                autoBattle.Simulation.DifficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0,
            index =>
            {
                autoBattle.Simulation.DifficultyId = index == 1 ? "advanced" : "normal";
                autoBattle.Normalize();
                AuraToolsConfigService.SaveAutoBattle();
            },
            260f);
        lockedControls.Add(difficultyButton);
        AuraToolsUi.AddText(
            difficultyRow.transform,
            "普通与高级分别产生一枚验证标记，不要求同时通过。",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);

        if (AutoBattleEvolutionView)
        {
            var evolutionRow = CreateInlineRow(parent, "AutoBattleEvolutionParameterRow");
            lockedControls.Add(AddAutoBattleSimulationInt(
                evolutionRow.transform,
                "进化轮数",
                autoBattle.Simulation.EvolutionIterations,
                1,
                20,
                value => autoBattle.Simulation.EvolutionIterations = value,
                autoBattle));
            lockedControls.Add(AddAutoBattleSimulationInt(
                evolutionRow.transform,
                "每轮训练局",
                autoBattle.Simulation.EvolutionEpisodesPerIteration,
                8,
                10000,
                value => autoBattle.Simulation.EvolutionEpisodesPerIteration = value,
                autoBattle));
            lockedControls.Add(AddAutoBattleSimulationInt(
                evolutionRow.transform,
                "竞技场局数",
                autoBattle.Simulation.EvolutionArenaEpisodes,
                2,
                10000,
                value => autoBattle.Simulation.EvolutionArenaEpisodes = value,
                autoBattle));
            AuraToolsUi.AddText(
                parent,
                "本次工作量："
                + autoBattle.Simulation.EvolutionIterations
                * (autoBattle.Simulation.EvolutionEpisodesPerIteration
                   + autoBattle.Simulation.EvolutionArenaEpisodes * 2)
                + " 场战斗",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
        }
        else
        {
            var parameterRow = CreateInlineRow(parent, "AutoBattleSimulationParameterRow");
            lockedControls.Add(AddAutoBattleSimulationInt(
                parameterRow.transform,
                "对照局数",
                autoBattle.Simulation.SimulationCount,
                1,
                100000,
                value => autoBattle.Simulation.SimulationCount = value,
                autoBattle));
            lockedControls.Add(AddAutoBattleSimulationInt(
                parameterRow.transform,
                "并行度",
                autoBattle.Simulation.Parallelism,
                1,
                16,
                value => autoBattle.Simulation.Parallelism = value,
                autoBattle));
            lockedControls.Add(CreateAutoBattleToggleRow(
                parent,
                "保留分歧与失败轨迹",
                autoBattle.Simulation.RetainDivergentTraces,
                value =>
                {
                    autoBattle.Simulation.RetainDivergentTraces = value;
                    AuraToolsConfigService.SaveAutoBattle();
                }));
        }

        var statusText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var actionRow = CreateInlineRow(
            parent,
            "AutoBattleSimulationActionRow");
        var primaryButton = AuraToolsUi.AddButton(
            actionRow.transform,
            AutoBattleEvolutionView ? "开始进化" : "运行标准评估",
            () =>
            {
                var queued = AutoBattleEvolutionView
                    ? AuraToolsAutoBattleSimulationRuntime.QueueEvolution(
                        autoBattle,
                        out var message)
                    : AuraToolsAutoBattleSimulationRuntime.QueueRun(
                        autoBattle,
                        out message);
                if (!queued)
                {
                    AuraToolsLog.Warn("[AutoBattle][Simulation] " + message);
                }
            },
            92f);
        var cancelButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "取消",
            AuraToolsAutoBattleSimulationRuntime.Cancel,
            66f);
        var resultButton = AuraToolsUi.AddButton(
            actionRow.transform,
            "打开结果",
            () => AuraToolsAutoBattleSimulationRuntime.OpenResultDirectory(
                autoBattle.Profile,
                evaluationModelId),
            84f);
        var operationDetailText = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        actionRow.AddComponent<AuraToolsAutoBattleSimulationStatusView>().Configure(
            autoBattle.Profile,
            evaluationModelId,
            AutoBattleEvolutionView,
            statusText,
            operationDetailText,
            primaryButton,
            cancelButton,
            resultButton,
            scenarios.Count > 0,
            lockedControls);

        AuraToolsUi.AddText(
            parent,
            "最近结果",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultTitle = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultPrimary = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultSecondary = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        var resultDetail = AuraToolsUi.AddText(
            parent,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        actionRow.AddComponent<AuraToolsAutoBattleSimulationResultView>().Configure(
            autoBattle.Profile,
            evaluationModelId,
            resultTitle,
            resultPrimary,
            resultSecondary,
            resultDetail);
    }

    private static InputField AddAutoBattleSimulationInt(
        Transform parent,
        string label,
        int value,
        int minimum,
        int maximum,
        Action<int> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            86f);
        InputField? input = null;
        input = AuraToolsUi.AddInput(parent, value.ToString(CultureInfo.InvariantCulture), raw =>
        {
            var parsed = int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var configured)
                ? configured
                : value;
            parsed = Math.Max(minimum, Math.Min(maximum, parsed));
            apply(parsed);
            autoBattle.Normalize();
            AuraToolsConfigService.SaveAutoBattle();
            if (input != null)
            {
                input.text = parsed.ToString(CultureInfo.InvariantCulture);
            }
        }, 72f);
        input.contentType = InputField.ContentType.IntegerNumber;
        return input;
    }

    private static void AddAutoBattleTrainingInt(
        Transform parent,
        string label,
        int value,
        Action<int> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var input = AuraToolsUi.AddInput(parent, value.ToString(CultureInfo.InvariantCulture), raw =>
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                apply(parsed);
            }
            autoBattle.Training.MarkCustom();
            autoBattle.Normalize();
            AuraToolsConfigService.SaveAutoBattle();
        }, 86f);
        input.contentType = InputField.ContentType.IntegerNumber;
    }

    private static void AddAutoBattleTrainingDouble(
        Transform parent,
        string label,
        double value,
        Action<double> apply,
        AutoBattleSettings autoBattle)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            0f,
            106f);
        var input = AuraToolsUi.AddInput(parent, value.ToString("0.####", CultureInfo.InvariantCulture), raw =>
        {
            if (TryParseTrainingDouble(raw, out var parsed))
            {
                apply(parsed);
            }
            autoBattle.Training.MarkCustom();
            autoBattle.Normalize();
            AuraToolsConfigService.SaveAutoBattle();
        }, 86f);
        input.contentType = InputField.ContentType.DecimalNumber;
    }

    private static bool TryParseTrainingDouble(string value, out double result)
    {
        return double.TryParse(
                   value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out result)
               || double.TryParse(
                   value,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out result);
    }

    private static int AutoBattleTrainingPresetIndex(string value)
    {
        return value switch
        {
            AutoBattleTrainingSettings.StandardPreset => 1,
            AutoBattleTrainingSettings.AdaptivePreset => 2,
            AutoBattleTrainingSettings.CustomPreset => 3,
            _ => 0
        };
    }

    private static string AutoBattleTrainingPresetSummary(AutoBattleTrainingSettings settings)
    {
        return settings.Epochs
               + "轮 · 学习率"
               + settings.LearningRate.ToString("0.####", CultureInfo.InvariantCulture)
               + " · 修正"
               + settings.MaximumCorrection.ToString("0.##", CultureInfo.InvariantCulture)
               + " · 偏好对"
               + settings.MinimumPreferencePairs;
    }

    private static string NextAutoBattleProfile(string value)
    {
        return value switch
        {
            "aggressive" => "defensive",
            "defensive" => "balanced",
            _ => "aggressive"
        };
    }

    private static string AutoBattleProfileLabel(string value)
    {
        return value switch
        {
            "aggressive" => "进攻",
            "defensive" => "稳健",
            _ => "均衡"
        };
    }

    private static string NextAutoBattleTrainingMode(string value)
    {
        return value switch
        {
            "auto" => "shadow",
            "shadow" => "hybrid",
            _ => "auto"
        };
    }

    private static string AutoBattleTrainingModeLabel(string value)
    {
        return value switch
        {
            "auto" => "自动轨迹采集",
            "shadow" => "人工示范采集",
            _ => "全部轨迹采集"
        };
    }

    private static string NextAutoBattleUnknownPolicy(string value)
    {
        return value switch
        {
            "allow" => "handoff",
            "handoff" => "conservative",
            _ => "allow"
        };
    }

    private static string AutoBattleUnknownPolicyLabel(string value)
    {
        return value switch
        {
            "allow" => "允许尝试",
            "handoff" => "交还玩家",
            _ => "保守降权"
        };
    }
}

internal sealed class AuraToolsAutoBattleWorkLockView : MonoBehaviour
{
    private IReadOnlyList<Selectable> controls = Array.Empty<Selectable>();
    private float nextRefreshAt;

    public void Configure(IReadOnlyList<Selectable> values)
    {
        controls = values ?? Array.Empty<Selectable>();
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var busy = AuraToolsAutoBattleModelRuntime.AnyTrainingBusy()
                   || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy;
        foreach (var control in controls)
        {
            if (control != null)
            {
                control.interactable = !busy;
            }
        }
    }
}

internal sealed class AuraToolsAutoBattleJourneyStatusView : MonoBehaviour
{
    private Text? text;
    private float nextRefreshAt;

    public void Configure(Text target)
    {
        text = target;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.5f;
        Refresh();
    }

    private void Refresh()
    {
        if (text != null)
        {
            text.text = AuraToolsAutoBattleJourneyRuntime.DescribeCurrentCapture();
        }
    }
}

internal sealed class AuraToolsBundledFoundationImportStatusView : MonoBehaviour
{
    private Text? statusText;
    private Button? importButton;
    private float nextRefreshAt;

    public void Configure(Text text, Button button)
    {
        statusText = text;
        importButton = button;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var current = AuraToolsBundledFoundationModelRuntime.SnapshotStatus();
        if (statusText != null)
        {
            statusText.text = "Model 批量导入：" + current.Message;
            statusText.color = current.Stage == BundledFoundationImportStage.Failed
                ? AuraToolsUi.WarningText
                : current.Stage == BundledFoundationImportStage.Completed
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText;
        }
        if (importButton != null)
        {
            importButton.interactable = !current.Busy;
        }
    }
}

internal sealed class AuraToolsLocalSectionRefreshView : MonoBehaviour
{
    private Func<string>? signatureProvider;
    private Action? rebuild;
    private string signature = "";
    private float nextRefreshAt;
    private bool rebuilding;

    public void Configure(
        Func<string> currentSignature,
        Action rebuildAction)
    {
        signatureProvider = currentSignature;
        rebuild = rebuildAction;
        signature = signatureProvider?.Invoke() ?? "";
    }

    private void Update()
    {
        if (rebuilding
            || Time.unscaledTime < nextRefreshAt
            || signatureProvider == null
            || rebuild == null)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.25f;
        var current = signatureProvider();
        if (string.Equals(current, signature, StringComparison.Ordinal))
        {
            return;
        }
        rebuilding = true;
        try
        {
            var viewState = AuraUiViewState.CaptureForContent(transform);
            signature = current;
            rebuild();
            Canvas.ForceUpdateCanvases();
            if (transform is RectTransform sectionRect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
            }
            if (transform.parent is RectTransform parentRect)
            {
                LayoutRebuilder.MarkLayoutForRebuild(parentRect);
            }
            AuraUiViewState.RestoreAfterLayout(
                transform,
                viewState,
                "AuraTools.Settings.LocalSection");
            signature = signatureProvider?.Invoke() ?? current;
        }
        finally
        {
            rebuilding = false;
        }
    }
}

internal sealed class AuraToolsAutoBattleModelApplicationStatusView :
    MonoBehaviour
{
    private Text? statusText;
    private Button? shadowButton;
    private Button? trialButton;
    private Button? fullButton;
    private float nextRefreshAt;

    public void Configure(
        Text text,
        Button shadow,
        Button trial,
        Button full)
    {
        statusText = text;
        shadowButton = shadow;
        trialButton = trial;
        fullButton = full;
        RefreshNow();
    }

    public void RefreshNow()
    {
        nextRefreshAt = 0f;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var status =
            AuraToolsAutoBattleRuntime.SnapshotModelApplicationStatus();
        var busy = AuraToolsAutoBattleModelRuntime.AnyTrainingBusy()
                   || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleGameValidationRuntime.GetStatus().Busy;
        if (statusText != null)
        {
            var mismatch = !string.Equals(
                status.ConfiguredMode,
                status.EffectiveMode,
                StringComparison.Ordinal);
            var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
            var snapshot = AuraToolsAutoBattleUiSnapshotRuntime.Snapshot(
                settings.Profile,
                status.SelectedModelId);
            var entry = snapshot.Models.FirstOrDefault(item => string.Equals(
                item.ModelId,
                status.SelectedModelId,
                StringComparison.Ordinal));
            var modelName = entry?.DisplayName;
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = status.ModelLoading || snapshot.Loading
                    ? "正在读取模型"
                    : string.IsNullOrWhiteSpace(status.SelectedModelId)
                        ? "尚未选择"
                        : "已选择模型";
            }
            var tier = entry == null
                ? ""
                : string.Equals(
                    entry.DeploymentTier,
                    CombatFoundationDeploymentTier.Experimental,
                    StringComparison.OrdinalIgnoreCase)
                    ? "（实验底模）"
                    : "（正式底模）";
            var details = status.ModelLoading
                ? "模型正在加载，接管前会等待加载完成"
                : status.EmergencyFallbackCount > 0
                    ? "技术兜底 "
                      + status.EmergencyFallbackCount
                      + " 次 · "
                      + CompactDiagnostic(status.LastFallbackReason)
                    : mismatch
                        ? CompactDiagnostic(status.Diagnostic)
                        : "模型正常时，决策完全由模型输出";
            statusText.text = "当前："
                              + Label(status.EffectiveMode)
                              + " · "
                              + DecisionOwnerLabel(status.DecisionOwner)
                              + "\n模型："
                              + modelName
                              + tier
                              + " · "
                              + details;
            statusText.color = mismatch || status.ModelIsolatedForBattle
                ? AuraToolsUi.WarningText
                : string.Equals(
                    status.EffectiveMode,
                    "off",
                    StringComparison.Ordinal)
                    ? AuraToolsUi.MutedText
                    : AuraToolsUi.SuccessText;
        }
        if (shadowButton != null)
        {
            shadowButton.interactable = !busy
                                        && !string.IsNullOrWhiteSpace(
                                            status.SelectedModelId)
                                        && !string.Equals(
                                            status.ConfiguredMode,
                                            "shadow",
                                            StringComparison.Ordinal);
        }
        if (trialButton != null)
        {
            trialButton.interactable = !busy
                                       && !string.IsNullOrWhiteSpace(
                                           status.SelectedModelId)
                                       && !string.Equals(
                                           status.ConfiguredMode,
                                           "trial",
                                           StringComparison.Ordinal);
        }
        if (fullButton != null)
        {
            fullButton.interactable = !busy
                                      && !string.IsNullOrWhiteSpace(
                                          status.SelectedModelId)
                                      && !string.Equals(
                                          status.ConfiguredMode,
                                          "full",
                                          StringComparison.Ordinal);
        }
    }

    private static string Label(string mode)
    {
        return mode switch
        {
            "shadow" => "影子评估",
            "trial" => "实机试用",
            "full" => "完整应用",
            _ => "关闭"
        };
    }

    private static string DecisionOwnerLabel(string owner)
    {
        return owner switch
        {
            "model" => "模型决策",
            "emergency-baseline" => "技术兜底",
            _ => "观察/基础策略"
        };
    }

    private static string CompactDiagnostic(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "等待重新加载";
        }
        return value.Length <= 30
            ? value
            : value.Substring(0, 27) + "...";
    }
}

internal sealed class AuraToolsAutoBattleExternalValidationStatusView :
    MonoBehaviour
{
    private string profile = "balanced";
    private Text? statusText;
    private Button? selectButton;
    private Button? promoteButton;
    private Button? clearButton;
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        Text text,
        Button select,
        Button promote,
        Button clear)
    {
        profile = string.IsNullOrWhiteSpace(decisionProfile)
            ? "balanced"
            : decisionProfile;
        statusText = text;
        selectButton = select;
        promoteButton = promote;
        clearButton = clear;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.25f;
        Refresh();
    }

    private void Refresh()
    {
        var entry =
            AuraToolsAutoBattleModelRuntime.SnapshotExternalValidationModel();
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var busy = AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleGameValidationRuntime.GetStatus().Busy
                   || AuraToolsAutoBattleModelRuntime.AnyTrainingBusy();
        var selected = entry != null
                       && string.Equals(
                           settings.EvaluationModelId,
                           entry.ModelId,
                           StringComparison.Ordinal);
        var gateReason = "";
        var ready = selected
                    && AuraToolsAutoBattleModelRuntime
                        .ExternalValidationMeetsGate(
                         profile,
                         entry!.ModelId,
                         out gateReason);
        if (!selected)
        {
            gateReason = entry == null
                ? "尚未选择外部底模包"
                : "该底模尚未设为当前评估目标";
        }
        if (statusText != null)
        {
            statusText.text = entry == null
                ? "待验底模：未选择"
                : entry.DisplayName
                  + " · "
                  + DescribeTrainingSubject(entry.TrainingSubject)
                  + " · "
                  + (ready
                      ? "校验通过"
                      : CompactGateReason(gateReason));
            statusText.color = ready
                ? AuraToolsUi.SuccessText
                : entry == null
                    ? AuraToolsUi.MutedText
                    : AuraToolsUi.WarningText;
        }
        if (selectButton != null)
        {
            selectButton.interactable = !busy;
        }
        if (promoteButton != null)
        {
            promoteButton.interactable = !busy && ready;
        }
        if (clearButton != null)
        {
            clearButton.interactable = !busy && entry != null;
        }
    }

    private static string DescribeTrainingSubject(
        CombatFoundationTrainingSubject? subject)
    {
        if (subject == null)
        {
            return "旧模型主体";
        }
        var packs = subject.EnabledRewardCardPackIds
            .Select(id => id.StartsWith(
                "cardpack_",
                StringComparison.OrdinalIgnoreCase)
                ? id.Substring("cardpack_".Length)
                : id)
            .Take(2)
            .ToList();
        var packText = string.Join(",", packs);
        if (subject.EnabledRewardCardPackIds.Count > packs.Count)
        {
            packText += "+" + (subject.EnabledRewardCardPackIds.Count - packs.Count);
        }
        return subject.RoleId
               + " / "
               + subject.PartnerId
               + " / "
               + (string.IsNullOrWhiteSpace(packText) ? "无卡包" : packText);
    }

    private static string CompactGateReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "等待校验";
        }
        return value.Length <= 24
            ? value
            : value.Substring(0, 21) + "...";
    }
}

internal sealed class AuraToolsAutoBattleGameValidationStatusView : MonoBehaviour
{
    private Text? statusText;
    private Button? runButton;
    private Button? cancelButton;
    private Button? openButton;
    private float nextRefreshAt;

    public void Configure(
        Text text,
        Button run,
        Button cancel,
        Button open)
    {
        statusText = text;
        runButton = run;
        cancelButton = cancel;
        openButton = open;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var status = AuraToolsAutoBattleGameValidationRuntime.GetStatus();
        var otherBusy = AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy
                        || AuraToolsAutoBattleModelRuntime.GetTrainingStatus(
                            AuraToolsConfigService.MatchExperience.AutoBattle.Profile).Busy;
        var startReady =
            AuraToolsAutoBattleGameValidationRuntime
                .IsStartEnvironmentReady(out var startReason);
        if (statusText != null)
        {
            var progress = status.RequestedBattles <= 0
                ? ""
                : " · "
                  + status.CompletedBattles
                  + "/"
                  + status.RequestedBattles
                  + " 战";
            statusText.text = !status.Busy && !startReady
                ? CompactStatus(startReason) + " · 可选"
                : CompactStatus(status.Message) + progress;
            statusText.color = status.Stage == AutoBattleGameValidationStage.Failed
                               || status.Stage == AutoBattleGameValidationStage.Cancelled
                ? AuraToolsUi.WarningText
                : status.Stage == AutoBattleGameValidationStage.Passed
                    ? AuraToolsUi.SuccessText
                    : AuraToolsUi.MutedText;
        }
        if (runButton != null)
        {
            runButton.interactable =
                !status.Busy && !otherBusy && startReady;
            SetButtonLabel(runButton, status.Busy ? "验证中..." : "实机验证");
        }
        if (cancelButton != null)
        {
            cancelButton.interactable = status.Busy
                                        && status.Stage
                                        != AutoBattleGameValidationStage.Cancelling;
        }
        if (openButton != null)
        {
            openButton.interactable = Directory.Exists(
                AuraToolsAutoBattleGameValidationRuntime.ResultsRootDirectory);
        }
    }

    private static void SetButtonLabel(Button button, string value)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }

    private static string CompactStatus(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "就绪";
        }
        return value.Length <= 32
            ? value
            : value.Substring(0, 29) + "...";
    }
}

internal sealed class AuraToolsAutoBattleTrainingStatusView : MonoBehaviour
{
    private string profile = "balanced";
    private Text? statusText;
    private Button? generateButton;
    private Button? importButton;
    private Button? rollbackButton;
    private Button? cancelButton;
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        Text text,
        Button generate,
        Button import,
        Button rollback,
        Button cancel)
    {
        profile = decisionProfile ?? "balanced";
        statusText = text;
        generateButton = generate;
        importButton = import;
        rollbackButton = rollback;
        cancelButton = cancel;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        profile = AuraToolsConfigService.MatchExperience.AutoBattle.Profile;
        var status = AuraToolsAutoBattleModelRuntime.GetTrainingStatus(profile);
        var simulationBusy =
            AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy;
        var candidateExists =
            AuraToolsAutoBattleModelRuntime.CandidateExists(profile);
        var candidateModelId = candidateExists
            ? AuraToolsAutoBattleModelRuntime.CandidateModelId(profile)
            : "none";
        var promotionReason = candidateExists
            ? "尚未完成标准评估"
            : "请先训练候选";
        var promotionReady = candidateExists
                             && AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                                 profile,
                                 candidateModelId,
                                 out promotionReason);
        if (statusText != null)
        {
            statusText.text = Describe(status, candidateExists, promotionReady, promotionReason);
            statusText.color = status.Stage == AutoBattleTrainingStage.Failed
                ? new Color(1f, 0.46f, 0.42f, 1f)
                : status.Stage == AutoBattleTrainingStage.CandidateReady
                  && promotionReady
                  || status.Stage == AutoBattleTrainingStage.Imported
                    ? AuraToolsUi.SuccessText
                    : status.Stage == AutoBattleTrainingStage.CandidateReady
                      && !promotionReady
                      ? new Color(1f, 0.78f, 0.32f, 1f)
                    : status.Stage == AutoBattleTrainingStage.Cancelling
                      || status.Stage == AutoBattleTrainingStage.Cancelled
                        ? new Color(1f, 0.78f, 0.32f, 1f)
                    : AuraToolsUi.MutedText;
        }

        if (generateButton != null)
        {
            var generating = status.Busy
                             && status.Stage != AutoBattleTrainingStage.Importing;
            generateButton.interactable = !status.Busy && !simulationBusy;
            SetButtonLabel(generateButton, generating ? "训练中..." : "训练候选");
        }
        if (importButton != null)
        {
            importButton.interactable = !status.Busy
                                        && !simulationBusy
                                        && promotionReady;
            SetButtonLabel(
                importButton,
                status.Stage == AutoBattleTrainingStage.Importing ? "保存中..." : "保存新冠军");
        }
        if (rollbackButton != null)
        {
            rollbackButton.interactable = !status.Busy && !simulationBusy;
        }
        if (cancelButton != null)
        {
            cancelButton.interactable = status.Busy
                                        && status.Stage != AutoBattleTrainingStage.Importing
                                        && status.Stage != AutoBattleTrainingStage.Cancelling;
            SetButtonLabel(
                cancelButton,
                status.Stage == AutoBattleTrainingStage.Cancelling ? "取消中..." : "取消");
        }
    }

    private string Describe(
        AutoBattleTrainingStatus status,
        bool candidateExists,
        bool promotionReady,
        string promotionReason)
    {
        var profileLabel = profile switch
        {
            "aggressive" => "进攻",
            "defensive" => "稳健",
            _ => "均衡"
        };
        var counts = status.SampleCount > 0
            ? " · 样本 " + status.SampleCount
              + " / 偏好对 " + status.PreferencePairCount
            : "";
        return status.Stage switch
        {
            AutoBattleTrainingStage.Queued => profileLabel + " · 已排队",
            AutoBattleTrainingStage.ReadingSamples => profileLabel + " · 正在读取样本",
            AutoBattleTrainingStage.Training => profileLabel + " · 训练中" + counts,
            AutoBattleTrainingStage.WritingCandidate => profileLabel + " · 正在写入候选" + counts,
            AutoBattleTrainingStage.Cancelling => profileLabel + " · 正在取消训练",
            AutoBattleTrainingStage.Cancelled => profileLabel + " · 训练已取消",
            AutoBattleTrainingStage.CandidateReady => status.WeightCount > 0
                ? profileLabel + " · 候选已生成 · 偏好对 "
                  + status.PreferencePairCount
                  + " / 权重 " + status.WeightCount
                  + " · "
                  + (promotionReady
                      ? "标准评估已通过"
                      : "下一步："
                        + Compact(promotionReason))
                : profileLabel + " · 检测到可导入的候选模型",
            AutoBattleTrainingStage.Importing => profileLabel + " · 正在导入候选",
            AutoBattleTrainingStage.Imported => profileLabel + " · 已导入"
                                                + ModelModeSuffix(
                                                    AuraToolsConfigService
                                                        .MatchExperience
                                                        .AutoBattle
                                                        .TrainedModelMode)
                                                + " · 权重 " + status.WeightCount,
            AutoBattleTrainingStage.Failed => profileLabel + " · " + Compact(status.Message),
            _ => profileLabel + " · " + status.Message
                 + (candidateExists && !promotionReady
                     ? " · 待评估：" + Compact(promotionReason)
                     : "")
        };
    }

    private static string ModelModeSuffix(string mode)
    {
        return mode switch
        {
            "shadow" => "，正在影子评估",
            "trial" => "，正在实机试用",
            "full" => "，正在完整应用",
            "active" => "，正在实机试用",
            _ => "，尚未启用"
        };
    }

    private static string Compact(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "操作失败" : value.Trim();
        return text.Length <= 72 ? text : text.Substring(0, 69) + "...";
    }

    private static void SetButtonLabel(Button button, string value)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }
}

internal sealed class AuraToolsAutoBattleSimulationStatusView : MonoBehaviour
{
    private string profile = "balanced";
    private string modelId = "";
    private bool evolutionView;
    private Text? statusText;
    private Text? operationDetailText;
    private Button? primaryButton;
    private Button? cancelButton;
    private Button? resultButton;
    private bool hasScenario;
    private IReadOnlyList<Selectable> lockedControls = Array.Empty<Selectable>();
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        string selectedModelId,
        bool showEvolution,
        Text text,
        Text operationDetail,
        Button primary,
        Button cancel,
        Button result,
        bool scenarioAvailable,
        IReadOnlyList<Selectable> controls)
    {
        profile = decisionProfile ?? "balanced";
        modelId = selectedModelId ?? "";
        evolutionView = showEvolution;
        statusText = text;
        operationDetailText = operationDetail;
        primaryButton = primary;
        cancelButton = cancel;
        resultButton = result;
        hasScenario = scenarioAvailable;
        lockedControls = controls ?? Array.Empty<Selectable>();
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        var status = AuraToolsAutoBattleSimulationRuntime.GetStatus();
        var modelBusy = AuraToolsAutoBattleModelRuntime.AnyTrainingBusy();
        var workBusy = status.Busy || modelBusy;
        if (statusText != null)
        {
            var progress = status.RequestedPairs > 0
                ? " · "
                  + status.CompletedPairs
                  + "/"
                  + status.RequestedPairs
                  + " "
                  + status.ProgressUnit
                : "";
            var message = string.IsNullOrWhiteSpace(status.Message)
                ? "尚未运行模拟评估"
                : status.Message.Trim();
            statusText.text = (message.Length <= 84 ? message : message.Substring(0, 81) + "...")
                              + progress;
            statusText.color = status.Stage == AutoBattleSimulationStage.Failed
                ? new Color(1f, 0.46f, 0.42f, 1f)
                : status.Stage == AutoBattleSimulationStage.Completed && status.GatePassed
                    ? AuraToolsUi.SuccessText
                    : status.Stage == AutoBattleSimulationStage.Cancelling
                      || status.Stage == AutoBattleSimulationStage.Cancelled
                      || status.Stage == AutoBattleSimulationStage.Completed
                        ? new Color(1f, 0.78f, 0.32f, 1f)
                    : AuraToolsUi.MutedText;
        }
        if (operationDetailText != null)
        {
            operationDetailText.text = status.Stage == AutoBattleSimulationStage.Failed
                ? status.Message
                : "";
            operationDetailText.color = status.Stage == AutoBattleSimulationStage.Failed
                ? new Color(1f, 0.46f, 0.42f, 1f)
                : AuraToolsUi.MutedText;
        }
        if (primaryButton != null)
        {
            primaryButton.interactable = !workBusy && hasScenario;
            SetButtonLabel(
                primaryButton,
                status.Busy
                    ? status.Operation == AutoBattleSimulationOperation.PolicyEvolution
                        ? "进化中..."
                        : "评估中..."
                    : evolutionView
                        ? "开始进化"
                        : "运行标准评估");
        }
        if (cancelButton != null)
        {
            cancelButton.interactable = status.Busy
                                        && status.Stage
                                        != AutoBattleSimulationStage.Cancelling;
            SetButtonLabel(
                cancelButton,
                status.Stage == AutoBattleSimulationStage.Cancelling ? "取消中..." : "取消");
        }
        if (resultButton != null)
        {
            resultButton.interactable =
                AuraToolsAutoBattleUiSnapshotRuntime
                    .Snapshot(profile, modelId)
                    .Result
                    .Available;
        }
        foreach (var control in lockedControls)
        {
            if (control != null)
            {
                control.interactable = !workBusy;
            }
        }
    }

    private static void SetButtonLabel(Button button, string value)
    {
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }
}

internal sealed class AuraToolsAutoBattleSimulationResultView : MonoBehaviour
{
    private string profile = "balanced";
    private string modelId = "";
    private Text? title;
    private Text? primary;
    private Text? secondary;
    private Text? detail;
    private float nextRefreshAt;

    public void Configure(
        string decisionProfile,
        string selectedModelId,
        Text titleText,
        Text primaryText,
        Text secondaryText,
        Text detailText)
    {
        profile = decisionProfile ?? "balanced";
        modelId = selectedModelId ?? "";
        title = titleText;
        primary = primaryText;
        secondary = secondaryText;
        detail = detailText;
        AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
            profile,
            modelId);
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }
        nextRefreshAt = Time.unscaledTime + 0.75f;
        Refresh();
    }

    private void Refresh()
    {
        var result = AuraToolsAutoBattleUiSnapshotRuntime
            .Snapshot(profile, modelId)
            .Result;
        if (title != null)
        {
            title.text = result.Title;
            title.color = result.Available
                ? result.GatePassed
                    ? AuraToolsUi.SuccessText
                    : new Color(1f, 0.78f, 0.32f, 1f)
                : AuraToolsUi.MutedText;
        }
        if (primary != null)
        {
            primary.text = result.Primary;
        }
        if (secondary != null)
        {
            secondary.text = result.Secondary;
        }
        if (detail != null)
        {
            detail.text = result.Detail;
        }
    }
}
