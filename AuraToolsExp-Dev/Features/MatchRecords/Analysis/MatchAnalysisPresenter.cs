using System;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Media;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.MatchRecords.Analysis;

internal static class MatchAnalysisPresenter
{
    private const string OverlayName = "AuraToolsMatchAnalysis";
    private static Transform? host;
    private static Transform? body;
    private static MatchRecord? record;
    private static MatchAnalysisReport? report;
    private static string tab = "Overview";
    private static string message = "";

    internal static void Show(Transform parent, MatchRecord selected)
    {
        host = parent;
        record = selected;
        tab = "Overview";
        message = "";
        try
        {
            report = MatchRecordStorage.Database.GetAnalysis(selected.RecordId);
            if (report == null || report.Protocol != MatchAnalysisProtocol.Version)
            {
                var document = selected.ReplayProtocol == ReplayProtocolV11.DocumentVersion
                    ? MatchRecordStorage.Database.LoadV11(selected.RecordId)
                    : null;
                report = document != null
                    ? MatchAnalysisBuilder.BuildV11(selected, document)
                    : MatchAnalysisBuilder.Build(
                        selected,
                        MatchReplayChunker.Decode(MatchRecordStorage.Database.LoadChunks(selected.RecordId)));
                MatchRecordStorage.Database.SaveAnalysis(report);
            }
        }
        catch (Exception ex)
        {
            report = null;
            message = "分析数据读取失败：" + ex.Message;
        }

        var window = AuraToolsUi.CreateOverlay(OverlayName, parent, "牌局分析", Reset, maxWidth: 1240f);
        body = AuraToolsUi.CreateLayout("MatchAnalysisBody", window.transform).transform;
        var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        AuraToolsUi.EnsureLayoutElement(body.gameObject).flexibleHeight = 1f;
        Build();
    }

    private static void Build()
    {
        if (body == null || record == null)
        {
            return;
        }

        AuraToolsUi.ClearChildren(body);
        var tabs = Row("AnalysisTabs", body, AuraToolsUi.ToolbarHeight);
        AddTab(tabs, "总览", "Overview");
        AddTab(tabs, "回合", "Turns");
        AddTab(tabs, "卡牌", "Cards");
        AddTab(tabs, "关键节点", "Moments");
        AddTab(tabs, "媒体", "Media");
        AuraToolsUi.AddText(tabs, "事实统计，不生成未观测到的反事实建议", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleRight, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 1f);

        var actions = Row("AnalysisActions", body, AuraToolsUi.ToolbarHeight);
        AddReplayButton(actions, "完整回放", 0, 96f);
        var packageButton = AuraToolsUi.AddButton(actions, "导出回放包", ExportPackage, 108f);
        AuraToolsUi.SetButtonAvailable(
            packageButton,
            CanReplay,
            "当前记录仅保留摘要，不能导出结构化回放包");
        var videoButton = AuraToolsUi.AddButton(actions, "导出视频", () => StartVideoExport(record.RecordId), 96f);
        AuraToolsUi.SetButtonAvailable(
            videoButton,
            CanReplay,
            "当前记录没有可用于视频导出的完整回放");
        AuraToolsUi.AddButton(actions, "打开导出目录", () => FileResourceUtil.OpenDirectory(MatchRecordStorage.ExportsDirectory), 120f);
        if (!string.IsNullOrWhiteSpace(message))
        {
            AuraToolsUi.AddText(body, message, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
                AuraToolsUi.WarningText, 44f, 1f);
        }

        var scroll = AuraToolsUi.CreateScroll(body, "AnalysisContent");
        if (report == null)
        {
            AuraToolsUi.AddText(scroll, "本对局没有可用的分析数据。", AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 72f, 1f);
            return;
        }

        switch (tab)
        {
            case "Turns":
                BuildTurns(scroll);
                break;
            case "Cards":
                BuildCards(scroll);
                break;
            case "Moments":
                BuildMoments(scroll);
                break;
            case "Media":
                MatchReplayMediaSection.Build(host!, scroll, record, SetMessageAndBuild);
                break;
            default:
                BuildOverview(scroll);
                break;
        }
    }

    private static void BuildOverview(Transform parent)
    {
        if (record == null || report == null)
        {
            return;
        }

        AuraToolsUi.AddText(parent,
            (string.IsNullOrWhiteSpace(record.BattleTitle)
                ? AuraToolsPlayerDisplay.LevelName(record.LevelId)
                : record.BattleTitle)
            + "   " + AuraToolsPlayerDisplay.BattleResult(record.Result) + "   " + report.TurnCount + " 回合\n"
            + "我方造成 " + report.FriendlyDamageDealt + "   敌方造成 " + report.EnemyDamageDealt
            + "   我方承受 " + report.FriendlyDamageTaken + "\n"
            + "生命伤害 " + report.HpDamage + "   护盾伤害 " + report.ShieldDamage
            + "   最高回合 " + report.BestTurnDamage
            + (report.BestTurnIndex > 0 ? "（第 " + report.BestTurnIndex + " 回合）" : "")
            + "   使用卡牌 " + report.CardUseCount,
            AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 72f, 1f);

        foreach (var item in report.Combatants)
        {
            var row = Row("Combatant-" + item.InstanceId, parent, 58f, withBackground: true);
            AuraToolsUi.AddText(row, item.DisplayName + "   " + TeamLabel(item.Team), AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.Text, 44f, 1f);
            AuraToolsUi.AddText(row, "伤害 " + item.Damage + "   平均/回合 " + item.AverageDamagePerTurn.ToString("0.0")
                                      + "   最高回合 " + item.BestTurnDamage,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 44f, 0f, 360f);
        }
    }

    private static void BuildTurns(Transform parent)
    {
        foreach (var item in report!.Turns)
        {
            var row = Row("Turn-" + item.TurnIndex, parent, 54f, withBackground: true);
            AuraToolsUi.AddText(row, "第 " + item.TurnIndex + " 回合", AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft, AuraToolsUi.Text, 42f, 0f, 110f);
            AuraToolsUi.AddText(row, "伤害 " + item.Damage + "   卡牌 " + item.CardUses + "   事件 " + item.ActionCount,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 42f, 1f);
            if (item.FirstEventSequence > 0)
            {
                var sequence = item.FirstEventSequence;
                AddReplayButton(row, "跳转", sequence, 76f);
            }
        }
    }

    private static void BuildCards(Transform parent)
    {
        foreach (var item in report!.Cards)
        {
            var row = Row("Card-" + item.CardId, parent, 54f, withBackground: true);
            AuraToolsUi.AddText(row, item.DisplayName, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft,
                AuraToolsUi.Text, 42f, 1f);
            AuraToolsUi.AddText(row, "使用 " + item.Uses + "   归因伤害 " + item.AttributedDamage
                                      + (item.ObservedFollowUpDamage > 0 ? "   推断 " + item.ObservedFollowUpDamage : ""),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 42f, 0f, 280f);
            var sequence = item.FirstEventSequence;
            AddReplayButton(row, "首次", sequence, 72f);
        }
    }

    private static void BuildMoments(Transform parent)
    {
        foreach (var item in report!.KeyMoments)
        {
            var row = Row("Moment-" + item.EventSequence, parent, 58f, withBackground: true);
            AuraToolsUi.AddText(row, "第 " + item.TurnIndex + " 回合   " + item.Label,
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 46f, 1f);
            if (item.EventSequence > 0)
            {
                var sequence = item.EventSequence;
                AddReplayButton(row, "回看", sequence, 76f);
            }
        }
    }

    private static void ExportPackage()
    {
        try
        {
            var path = MatchReplayPackageService.Export(record!.RecordId);
            message = "回放包已导出：" + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            message = "导出失败：" + ex.Message;
        }

        Build();
    }

    private static void StartVideoExport(string recordId)
    {
        if (!MatchReplayVideoExporter.TryStart(
                recordId,
                MatchRecordLibraryPresenter.CaptureReturnState(recordId),
                out var result))
        {
            message = result;
            Build();
        }
    }

    private static void StartReplay(long sequence)
    {
        MatchReplayLaunchCoordinator.Start(
            record!.RecordId,
            sequence,
            MatchRecordLibraryPresenter.CaptureReturnState(record.RecordId),
            detail =>
            {
                message = detail;
                Build();
            });
    }

    private static bool CanReplay => record != null
                                     && record.ReplayProtocol == ReplayProtocolV11.DocumentVersion
                                     && string.Equals(record.ReplayState, MatchReplayStates.Ready, StringComparison.Ordinal);

    private static Button AddReplayButton(Transform parent, string label, long sequence, float width)
    {
        var button = AuraToolsUi.AddButton(parent, label, () => StartReplay(sequence), width);
        AuraToolsUi.SetButtonAvailable(
            button,
            CanReplay,
            "当前记录仅保留摘要，不能进行结构化回放");
        return button;
    }

    private static void AddTab(Transform parent, string label, string value)
    {
        AuraToolsUi.AddButton(parent, (tab == value ? "· " : "") + label, () =>
        {
            tab = value;
            message = "";
            Build();
        }, 96f);
    }

    private static Transform Row(string name, Transform parent, float height, bool withBackground = false)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        if (withBackground)
        {
            AuraToolsUi.AddImage(row, AuraToolsUi.Row);
        }

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = withBackground ? new RectOffset(10, 10, 6, 6) : new RectOffset();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row.transform;
    }

    private static string TeamLabel(string value)
    {
        return string.Equals(value, "Friendly", StringComparison.OrdinalIgnoreCase) ? "友方"
            : string.Equals(value, "Enemy", StringComparison.OrdinalIgnoreCase) ? "敌方"
            : "未知阵营";
    }

    private static void SetMessageAndBuild(string value)
    {
        message = value;
        Build();
    }

    private static void Reset()
    {
        host = null;
        body = null;
        record = null;
        report = null;
        message = "";
    }
}
