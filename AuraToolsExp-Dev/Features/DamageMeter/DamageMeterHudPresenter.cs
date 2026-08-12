using System;
using System.Collections.Generic;
using System.Linq;
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
        var adventureScope = settings.DisplayScope == DamageMeterDisplayScopes.Adventure;
        var source = adventureScope
            ? runAggregate.Combatants
            : ledger.Combatants;
        var averagingRounds = adventureScope
            ? Math.Max(1, runAggregate.TotalRounds)
            : Math.Max(1, ledger.AveragingRoundCount);
        var rows = BuildRows(source, settings.TeamFilter, averagingRounds, adventureScope ? null : ledger);
        var showStats = adventureScope ? runAggregate.HasDamage : ledger.InFight;
        var total = rows.Sum(row => row.Total);
        var scopeLabel = adventureScope ? "本轮冒险" : "本场战斗";
        var title = adventureScope
            ? "DPS统计（DPT）  本轮冒险 · " + runAggregate.EncounterCount + " 场"
            : "DPS统计（DPT）  本场 · 回合 " + ledger.CurrentRoundIndex;
        var emptyMessage = adventureScope
            ? "本轮冒险尚无伤害记录。"
            : history.TotalCount > 0
                ? "当前没有进行中的战斗。\n可通过“查看历史”回顾本轮冒险的输出记录。"
                : "等待下一场战斗开始。";
        var footer = scopeLabel + "合计 " + total
                     + "  /  " + averagingRounds + " 回合"
                     + "  /  " + networkStatus;

        return new DamageMeterHudPresentation(
            showStats,
            showStats ? Math.Min(650f, 188f + Math.Max(1, Math.Min(8, rows.Count)) * 48f) : 250f,
            title,
            emptyMessage,
            footer,
            history.TotalCount > 0,
            rows,
            scopeLabel,
            averagingRounds,
            settings.DisplayMode == DamageMeterDisplayModes.Bars);
    }

    internal static IReadOnlyList<DamageMeterHudRow> BuildRows(
        IEnumerable<CombatantDamageStat>? source,
        string teamFilter,
        int averagingRounds,
        DamageLedger? fightLedger = null)
    {
        var filtered = (source ?? Array.Empty<CombatantDamageStat>())
            .Where(stat => stat != null && MatchesTeamFilter(stat.Team, teamFilter))
            .Select(stat => new
            {
                Stat = stat,
                Total = Math.Max(0, stat.DisplayTotal(true)),
                Current = fightLedger == null ? 0 : Math.Max(0, stat.DisplayCurrentRound(true)),
                Dpt = stat.AveragePerCompletedRound(true, Math.Max(1, averagingRounds))
            })
            .OrderBy(item => TeamOrder(item.Stat.Team))
            .ThenByDescending(item => item.Dpt)
            .ThenByDescending(item => item.Total)
            .ThenBy(item => item.Stat.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totals = filtered
            .GroupBy(item => item.Stat.Team)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Total));
        var maxima = filtered
            .GroupBy(item => item.Stat.Team)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Dpt));
        var ranks = new Dictionary<DamageTeam, int>();
        var result = new List<DamageMeterHudRow>(filtered.Count);
        foreach (var item in filtered)
        {
            ranks.TryGetValue(item.Stat.Team, out var rank);
            rank++;
            ranks[item.Stat.Team] = rank;
            var teamTotal = totals[item.Stat.Team];
            var teamMaximum = maxima[item.Stat.Team];
            result.Add(new DamageMeterHudRow(
                item.Stat,
                rank,
                item.Current,
                item.Total,
                item.Dpt,
                teamTotal <= 0 ? 0d : item.Total / (double)teamTotal,
                teamMaximum <= 0d ? 0d : item.Dpt / teamMaximum));
        }

        return result;
    }

    internal static bool MatchesTeamFilter(DamageTeam team, string teamFilter)
    {
        return teamFilter == DamageMeterTeamFilters.Friendly
            ? team == DamageTeam.Friendly
            : teamFilter == DamageMeterTeamFilters.Enemy
                ? team == DamageTeam.Enemy
                : true;
    }

    private static int TeamOrder(DamageTeam team)
    {
        return team == DamageTeam.Friendly ? 0 : team == DamageTeam.Enemy ? 1 : 2;
    }
}

internal sealed class DamageMeterHudRow
{
    internal DamageMeterHudRow(
        CombatantDamageStat stat,
        int rank,
        long currentRound,
        long total,
        double dpt,
        double share,
        double barFraction)
    {
        Stat = stat;
        Rank = rank;
        CurrentRound = currentRound;
        Total = total;
        Dpt = dpt;
        Share = share;
        BarFraction = Math.Max(0d, Math.Min(1d, barFraction));
    }

    internal CombatantDamageStat Stat { get; }
    internal int Rank { get; }
    internal long CurrentRound { get; }
    internal long Total { get; }
    internal double Dpt { get; }
    internal double Share { get; }
    internal double BarFraction { get; }
}

internal sealed class DamageMeterHudPresentation
{
    internal DamageMeterHudPresentation(
        bool showStats,
        float height,
        string title,
        string emptyMessage,
        string footer,
        bool hasHistory,
        IReadOnlyList<DamageMeterHudRow> visibleRows,
        string scopeLabel,
        int averagingRounds,
        bool barsMode)
    {
        ShowStats = showStats;
        Height = height;
        Title = title;
        EmptyMessage = emptyMessage;
        Footer = footer;
        HasHistory = hasHistory;
        VisibleRows = visibleRows;
        ScopeLabel = scopeLabel;
        AveragingRounds = averagingRounds;
        BarsMode = barsMode;
    }

    internal bool ShowStats { get; }
    internal float Height { get; }
    internal string Title { get; }
    internal string EmptyMessage { get; }
    internal string Footer { get; }
    internal bool HasHistory { get; }
    internal IReadOnlyList<DamageMeterHudRow> VisibleRows { get; }
    internal string ScopeLabel { get; }
    internal int AveragingRounds { get; }
    internal bool BarsMode { get; }
}
