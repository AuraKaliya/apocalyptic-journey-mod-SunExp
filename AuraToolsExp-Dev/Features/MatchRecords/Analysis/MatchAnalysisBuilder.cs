using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Analysis;

internal static class MatchAnalysisBuilder
{
    internal static MatchAnalysisReport Build(MatchRecord record, IReadOnlyList<MatchReplayEvent> events)
    {
        var source = (events ?? Array.Empty<MatchReplayEvent>())
            .Where(item => item != null)
            .OrderBy(item => item.Sequence)
            .ToList();
        var snapshot = ReadSnapshot(record.StatisticsJson);
        var turns = BuildTurns(record, source, snapshot);
        var report = new MatchAnalysisReport
        {
            RecordId = record.RecordId,
            GeneratedUtc = DateTime.UtcNow.ToString("O"),
            TurnCount = Math.Max(record.TurnCount, turns.Count),
            Turns = turns,
            Combatants = BuildCombatants(snapshot),
            Cards = BuildCards(source)
        };
        report.TotalDamage = report.Combatants.Sum(item => item.Damage);
        var bestTurn = turns.OrderByDescending(item => item.Damage).ThenBy(item => item.TurnIndex).FirstOrDefault();
        report.BestTurnDamage = bestTurn?.Damage ?? 0;
        report.BestTurnIndex = bestTurn?.TurnIndex ?? 0;
        report.CardUseCount = report.Cards.Sum(item => item.Uses);
        report.KeyMoments = BuildKeyMoments(source, bestTurn);
        return report;
    }

    private static List<MatchAnalysisTurn> BuildTurns(
        MatchRecord record,
        IReadOnlyList<MatchReplayEvent> events,
        DamageMeterSnapshot? snapshot)
    {
        var count = Math.Max(1, Math.Max(record.TurnCount, events.Count == 0 ? 0 : events.Max(item => item.TurnIndex)));
        var result = Enumerable.Range(1, count)
            .Select(index => new MatchAnalysisTurn { TurnIndex = index })
            .ToList();
        foreach (var group in events.GroupBy(item => Math.Max(1, item.TurnIndex)))
        {
            var turn = result[Math.Min(result.Count, group.Key) - 1];
            turn.ActionCount = group.Count();
            turn.CardUses = group.Count(item => item.Semantic?.Category == MatchSemanticCategories.Card);
            turn.FirstEventSequence = group.Min(item => item.Sequence);
            turn.LastEventSequence = group.Max(item => item.Sequence);
            turn.Damage = group.Where(IsDamage).Sum(item => Math.Max(0, item.Semantic?.Value ?? 0));
        }

        if (snapshot?.Combatants != null)
        {
            foreach (var combatant in snapshot.Combatants.Where(item => item != null))
            {
                foreach (var round in combatant.Rounds ?? new List<DamageRoundStat>())
                {
                    if (round.RoundIndex > 0 && round.RoundIndex <= result.Count)
                    {
                        result[round.RoundIndex - 1].Damage += Math.Max(0, round.HpDamage) + Math.Max(0, round.ShieldDamage);
                    }
                }
            }

            // The DPT ledger is authoritative. Avoid double counting command display events when round data exists.
            var hasRoundData = snapshot.Combatants.Any(item => item?.Rounds != null && item.Rounds.Count > 0);
            if (hasRoundData)
            {
                foreach (var turn in result)
                {
                    turn.Damage = snapshot.Combatants
                        .Where(item => item != null)
                        .SelectMany(item => item.Rounds ?? new List<DamageRoundStat>())
                        .Where(item => item.RoundIndex == turn.TurnIndex)
                        .Sum(item => Math.Max(0, item.HpDamage) + Math.Max(0, item.ShieldDamage));
                }
            }
        }

        return result;
    }

    private static List<MatchAnalysisCombatant> BuildCombatants(DamageMeterSnapshot? snapshot)
    {
        var completedTurns = Math.Max(1, snapshot?.CompletedRoundCount ?? snapshot?.CurrentRoundIndex ?? 1);
        return (snapshot?.Combatants ?? new List<CombatantDamageStat>())
            .Where(item => item != null)
            .Select(item => new MatchAnalysisCombatant
            {
                InstanceId = item.InstanceId ?? "",
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.InstanceId ?? "未知单位" : item.DisplayName,
                Team = item.Team.ToString(),
                Damage = Math.Max(0, item.TotalHpDamage) + Math.Max(0, item.TotalShieldDamage),
                BestTurnDamage = item.HighestRound(true),
                AverageDamagePerTurn = item.AveragePerCompletedRound(true, completedTurns)
            })
            .OrderByDescending(item => item.Damage)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private static List<MatchAnalysisCard> BuildCards(IReadOnlyList<MatchReplayEvent> events)
    {
        var cards = new Dictionary<string, MatchAnalysisCard>(StringComparer.OrdinalIgnoreCase);
        MatchAnalysisCard? active = null;
        var activeTurn = 0;
        foreach (var item in events)
        {
            var semantic = item.Semantic;
            if (semantic?.Category == MatchSemanticCategories.Card)
            {
                var id = string.IsNullOrWhiteSpace(semantic.SourceId) ? semantic.Label : semantic.SourceId;
                id = string.IsNullOrWhiteSpace(id) ? "UnknownCard" : id;
                if (!cards.TryGetValue(id, out active))
                {
                    active = new MatchAnalysisCard
                    {
                        CardId = id,
                        DisplayName = string.IsNullOrWhiteSpace(semantic.Label) ? id : semantic.Label,
                        FirstEventSequence = item.Sequence
                    };
                    cards[id] = active;
                }

                active.Uses++;
                activeTurn = item.TurnIndex;
                continue;
            }

            if (active != null && activeTurn == item.TurnIndex && IsDamage(item))
            {
                active.ObservedFollowUpDamage += Math.Max(0, semantic?.Value ?? 0);
            }
            else if (activeTurn != item.TurnIndex)
            {
                active = null;
            }
        }

        return cards.Values
            .OrderByDescending(item => item.Uses)
            .ThenByDescending(item => item.ObservedFollowUpDamage)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private static List<MatchAnalysisMoment> BuildKeyMoments(
        IReadOnlyList<MatchReplayEvent> events,
        MatchAnalysisTurn? bestTurn)
    {
        var result = new List<MatchAnalysisMoment>();
        if (bestTurn != null && bestTurn.Damage > 0)
        {
            result.Add(new MatchAnalysisMoment
            {
                Kind = "BestTurn",
                Label = "本局最高伤害回合",
                TurnIndex = bestTurn.TurnIndex,
                EventSequence = bestTurn.FirstEventSequence,
                Value = bestTurn.Damage
            });
        }

        foreach (var item in events
                     .Where(value => value.Semantic?.IsKeyEvent == true || IsDamage(value))
                     .OrderByDescending(value => value.Semantic?.Value ?? 0)
                     .ThenBy(value => value.Sequence)
                     .Take(24))
        {
            var semantic = item.Semantic!;
            result.Add(new MatchAnalysisMoment
            {
                Kind = semantic.Category,
                Label = Describe(semantic),
                TurnIndex = item.TurnIndex,
                EventSequence = item.Sequence,
                ElapsedMilliseconds = item.ElapsedMilliseconds,
                Value = semantic.Value
            });
        }

        return result
            .GroupBy(item => item.Kind + "|" + item.EventSequence, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.EventSequence)
            .Take(24)
            .ToList();
    }

    private static bool IsDamage(MatchReplayEvent item)
    {
        return item.Semantic?.Category == MatchSemanticCategories.Damage && item.Semantic.Value > 0;
    }

    private static string Describe(MatchSemanticEvent semantic)
    {
        if (semantic.Category == MatchSemanticCategories.Damage)
        {
            return (string.IsNullOrWhiteSpace(semantic.ActorId) ? "未知来源" : semantic.ActorId)
                   + " 对 " + (string.IsNullOrWhiteSpace(semantic.TargetId) ? "目标" : semantic.TargetId)
                   + " 造成 " + semantic.Value + " 点伤害";
        }

        if (semantic.Category == MatchSemanticCategories.Status && semantic.Value <= 0)
        {
            return (string.IsNullOrWhiteSpace(semantic.TargetId) ? "单位" : semantic.TargetId) + " 离场";
        }

        return string.IsNullOrWhiteSpace(semantic.Label) ? semantic.Action : semantic.Label;
    }

    private static DamageMeterSnapshot? ReadSnapshot(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? null : AuraSharedJson.Deserialize<DamageMeterSnapshot>(json);
        }
        catch
        {
            return null;
        }
    }
}
