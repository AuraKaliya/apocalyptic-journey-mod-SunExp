using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterHudPresenter
{
    internal static DamageMeterHudPresentation Build(
        DamageLedger ledger,
        DamageRunLedger runAggregate,
        DamageHistoryStore history,
        DamageMeterSettings settings,
        string networkStatus)
    {
        var inFight = ledger.InFight;
        var runTotal = runAggregate.DisplayGrandTotal(
            settings.CountShieldLoss,
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam);
        if (!inFight)
        {
            var message = history.Records.Count > 0
                ? "当前没有进行中的战斗。\n可通过“查看历史”回顾本轮冒险的输出记录。"
                : "等待下一场战斗开始。\n悬浮球会在世界推演的备战、地图和战斗界面保持可用。";
            if (runAggregate.HasDamage)
            {
                message = "本轮冒险累计伤害 " + runTotal
                          + "\n战斗 " + runAggregate.EncounterCount
                          + " 场 / 回合 " + runAggregate.TotalRounds;
            }

            return new DamageMeterHudPresentation(
                false,
                250f,
                "DPS统计（世界推演）",
                message,
                networkStatus + "  /  拖动悬浮球可调整位置",
                history.Records.Count > 0,
                Array.Empty<CombatantDamageStat>(),
                0);
        }

        var grandTotal = ledger.DisplayGrandTotal(
            settings.CountShieldLoss,
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam);
        return new DamageMeterHudPresentation(
            true,
            Math.Min(720f, 132f + Math.Max(1, settings.MaxRows) * 48f),
            "DPS统计（按回合/DPT）  回合 " + ledger.CurrentRoundIndex,
            "",
            "本场合计 " + grandTotal
            + "  /  Run total " + runTotal
            + "  /  已完成 " + ledger.CompletedRoundCount + " 回合"
            + "  /  " + networkStatus
            + "  /  拖动悬浮球可调整位置",
            history.Records.Count > 0,
            ledger.VisibleRows(
                settings.FriendlyOnly,
                settings.IncludeUnknownTeam,
                settings.CountShieldLoss,
                settings.MaxRows),
            grandTotal);
    }
}

internal sealed class DamageMeterHudPresentation
{
    internal DamageMeterHudPresentation(
        bool inFight,
        float height,
        string title,
        string emptyMessage,
        string footer,
        bool hasHistory,
        IReadOnlyList<CombatantDamageStat> visibleRows,
        long grandTotal)
    {
        InFight = inFight;
        Height = height;
        Title = title;
        EmptyMessage = emptyMessage;
        Footer = footer;
        HasHistory = hasHistory;
        VisibleRows = visibleRows;
        GrandTotal = grandTotal;
    }

    internal bool InFight { get; }
    internal float Height { get; }
    internal string Title { get; }
    internal string EmptyMessage { get; }
    internal string Footer { get; }
    internal bool HasHistory { get; }
    internal IReadOnlyList<CombatantDamageStat> VisibleRows { get; }
    internal long GrandTotal { get; }
}
