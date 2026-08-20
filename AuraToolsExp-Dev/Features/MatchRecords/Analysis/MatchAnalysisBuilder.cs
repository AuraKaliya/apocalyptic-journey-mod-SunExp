using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Analysis;

internal static class MatchAnalysisBuilder
{
    internal static MatchAnalysisReport BuildV10(MatchRecord record, ReplayDocumentV10 document)
    {
        var snapshot = ReadSnapshot(record.StatisticsJson);
        var turns = new Dictionary<int, MatchAnalysisTurn>();
        var cards = new Dictionary<string, MatchAnalysisCard>(StringComparer.Ordinal);
        var flows = new Dictionary<string, MatchAnalysisDamageFlow>(StringComparer.Ordinal);
        var moments = new List<MatchAnalysisMoment>();
        var teams = (document.InitialState.Actors ?? new List<ReplayActorStateV10>())
            .Where(item => !string.IsNullOrWhiteSpace(item.InstanceId))
            .ToDictionary(item => item.InstanceId, item => item.Team ?? "Unknown", StringComparer.Ordinal);
        var definitions = (document.Content.Definitions ?? new List<ReplayContentDefinitionV10>())
            .ToDictionary(item => item.Content.Key, item => item.Display, StringComparer.Ordinal);
        var allCards = document.InitialState.Cards
            .Concat(document.Events.Where(item => item.Delta != null).SelectMany(item => item.Delta!.CardUpserts))
            .Where(item => !string.IsNullOrWhiteSpace(item.InstanceId))
            .GroupBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (var value in document.Events.OrderBy(item => item.Sequence))
        {
            var turnIndex = Math.Max(1, value.TurnIndex);
            if (!turns.TryGetValue(turnIndex, out var turn))
            {
                turn = new MatchAnalysisTurn { TurnIndex = turnIndex, FirstEventSequence = value.Sequence };
                turns[turnIndex] = turn;
            }

            turn.LastEventSequence = value.Sequence;
            if (string.Equals(value.EventType, ReplayEventTypesV10.ActionCompleted, StringComparison.Ordinal))
            {
                turn.ActionCount++;
                allCards.TryGetValue(value.SourceInstanceId ?? "", out var source);
                if (source != null)
                {
                    var cardId = source.Content.StableContentId;
                    if (!cards.TryGetValue(cardId, out var card))
                    {
                        definitions.TryGetValue(source.Content.Key, out var display);
                        card = new MatchAnalysisCard
                        {
                            CardId = cardId,
                            DisplayName = string.IsNullOrWhiteSpace(display?.Name) ? cardId : display?.Name ?? cardId,
                            FirstEventSequence = value.Sequence,
                            AttributionConfidence = MatchAttributionConfidence.Exact
                        };
                        cards[cardId] = card;
                    }

                    card.Uses++;
                    turn.CardUses++;
                }
            }

            foreach (var semantic in value.Semantics ?? new List<ReplaySemanticEventV10>())
            {
                if (!string.Equals(semantic.Kind, ReplaySemanticKindsV10.Damage, StringComparison.Ordinal)
                    || semantic.Value <= 0)
                {
                    continue;
                }

                turn.Damage += semantic.Value;
                var sourceTeam = Team(teams, semantic.ActorId, value.ActorId);
                var targetTeam = Team(teams, semantic.TargetId, semantic.TargetId);
                var key = sourceTeam + "|" + targetTeam;
                if (!flows.TryGetValue(key, out var flow))
                {
                    flow = new MatchAnalysisDamageFlow { SourceTeam = sourceTeam, TargetTeam = targetTeam };
                    flows[key] = flow;
                }

                if (string.Equals(semantic.Action, "ShieldDamage", StringComparison.Ordinal)) flow.ShieldDamage += semantic.Value;
                else flow.HpDamage += semantic.Value;
                var sourceCard = cards.Values.LastOrDefault(item => item.FirstEventSequence <= value.Sequence);
                if (sourceCard != null) sourceCard.AttributedDamage += semantic.Value;
                if (moments.Count < 24)
                {
                    moments.Add(new MatchAnalysisMoment
                    {
                        Kind = semantic.Kind,
                        Label = semantic.Label,
                        TurnIndex = turnIndex,
                        EventSequence = value.Sequence,
                        ElapsedMilliseconds = value.TimeTicks * 1000L / ReplayProtocolV10.TimebaseTicksPerSecond,
                        Value = semantic.Value
                    });
                }
            }
        }

        var turnCount = Math.Max(1, Math.Max(record.TurnCount, turns.Count == 0 ? 0 : turns.Keys.Max()));
        var orderedTurns = Enumerable.Range(1, turnCount)
            .Select(index => turns.TryGetValue(index, out var value) ? value : new MatchAnalysisTurn { TurnIndex = index })
            .ToList();
        ApplyAuthoritativeTurnDamage(orderedTurns, snapshot);
        var combatants = BuildCombatants(snapshot);
        var report = new MatchAnalysisReport
        {
            Protocol = MatchAnalysisProtocol.Version,
            RecordId = record.RecordId,
            GeneratedUtc = DateTime.UtcNow.ToString("O"),
            TurnCount = turnCount,
            Turns = orderedTurns,
            Combatants = combatants,
            Cards = cards.Values.OrderByDescending(item => item.Uses).ThenBy(item => item.CardId, StringComparer.Ordinal).ToList(),
            DamageFlows = flows.Values.OrderBy(item => item.SourceTeam, StringComparer.Ordinal).ThenBy(item => item.TargetTeam, StringComparer.Ordinal).ToList(),
            KeyMoments = moments.OrderBy(item => item.EventSequence).Take(24).ToList()
        };
        report.TotalDamage = combatants.Sum(item => item.Damage);
        report.FriendlyDamageDealt = combatants.Where(item => item.Team == ReplayTeamsV10.Friendly).Sum(item => item.Damage);
        report.EnemyDamageDealt = combatants.Where(item => item.Team == ReplayTeamsV10.Enemy).Sum(item => item.Damage);
        report.FriendlyDamageTaken = report.DamageFlows.Where(item => item.TargetTeam == ReplayTeamsV10.Friendly).Sum(FlowDamage);
        report.EnemyDamageTaken = report.DamageFlows.Where(item => item.TargetTeam == ReplayTeamsV10.Enemy).Sum(FlowDamage);
        report.HpDamage = report.DamageFlows.Sum(item => item.HpDamage);
        report.ShieldDamage = report.DamageFlows.Sum(item => item.ShieldDamage);
        var best = orderedTurns.OrderByDescending(item => item.Damage).ThenBy(item => item.TurnIndex).FirstOrDefault();
        report.BestTurnDamage = best?.Damage ?? 0;
        report.BestTurnIndex = best?.TurnIndex ?? 0;
        report.CardUseCount = report.Cards.Sum(item => item.Uses);
        return report;
    }

    internal static MatchAnalysisReport Build(MatchRecord record, IEnumerable<MatchReplayEvent> events)
    {
        var stream = (events ?? Array.Empty<MatchReplayEvent>()).Where(item => item != null).ToList();
        var framed = stream.Any(item => item.Kind == MatchReplayEventKinds.ActionFrame);
        var transactional = stream.Any(item => item.Kind == MatchReplayEventKinds.ActionBegin);
        var snapshot = ReadSnapshot(record.StatisticsJson);
        var teams = (snapshot?.Combatants ?? new List<CombatantDamageStat>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.InstanceId))
            .ToDictionary(item => item.InstanceId, item => item.Team.ToString(), StringComparer.Ordinal);
        var turns = new Dictionary<int, MatchAnalysisTurn>();
        var cards = new Dictionary<string, MatchAnalysisCard>(StringComparer.OrdinalIgnoreCase);
        var actions = new Dictionary<string, MatchAnalysisCard>(StringComparer.Ordinal);
        var flows = new Dictionary<string, MatchAnalysisDamageFlow>(StringComparer.Ordinal);
        var moments = new List<MatchAnalysisMoment>(25);
        MatchAnalysisCard? activeCard = null;
        var activeTurn = 0;
        var lastSequence = 0L;

        foreach (var item in stream)
        {
            if (item == null) continue;
            if (item.Sequence <= lastSequence) throw new InvalidOperationException("Replay events are not in strictly increasing sequence order.");
            lastSequence = item.Sequence;
            var turnIndex = Math.Max(1, item.TurnIndex);
            if (!turns.TryGetValue(turnIndex, out var turn))
            {
                turn = new MatchAnalysisTurn { TurnIndex = turnIndex, FirstEventSequence = item.Sequence };
                turns[turnIndex] = turn;
            }

            turn.LastEventSequence = item.Sequence;
            if (framed)
            {
                if (item.Kind == MatchReplayEventKinds.ActionFrame)
                {
                    turn.ActionCount++;
                }
            }
            else if (transactional)
            {
                if (item.Kind == MatchReplayEventKinds.ActionBegin
                    && string.IsNullOrWhiteSpace(item.ActionBoundary?.ParentActionId))
                {
                    turn.ActionCount++;
                }
            }
            else if (item.Kind != MatchReplayEventKinds.Checkpoint)
            {
                turn.ActionCount++;
            }
            if (item.ActionFrame != null
                && string.Equals(
                    item.ActionFrame.Kind,
                    MatchReplayActionKinds.EnemyIntentUse,
                    StringComparison.Ordinal))
            {
                // Enemy intent damage must never inherit attribution from the player's
                // last card in the same numbered round.
                activeCard = null;
                actions.Clear();
                activeTurn = turnIndex;
            }
            var semantics = new List<MatchSemanticEvent>();
            if (item.Semantic != null)
            {
                semantics.Add(item.Semantic);
            }

            if (item.ActionFrame?.Semantics != null)
            {
                semantics.AddRange(item.ActionFrame.Semantics.Where(semantic =>
                    semantic != null
                    && !semantics.Any(existing => !string.IsNullOrWhiteSpace(semantic.EventId)
                                                  && string.Equals(existing.EventId, semantic.EventId, StringComparison.Ordinal))));
            }

            foreach (var semantic in semantics)
            {
                if (semantic.Category == MatchSemanticCategories.Card
                    && (!transactional || item.Kind == MatchReplayEventKinds.ActionBegin)
                    && (!framed || ReferenceEquals(semantic, item.Semantic)))
                {
                    if (activeTurn != turnIndex) actions.Clear();
                    var cardId = string.IsNullOrWhiteSpace(semantic.SourceId) ? semantic.Label : semantic.SourceId;
                    cardId = string.IsNullOrWhiteSpace(cardId) ? "UnknownCard" : cardId;
                    if (!cards.TryGetValue(cardId, out activeCard))
                    {
                        activeCard = new MatchAnalysisCard
                        {
                            CardId = cardId,
                            DisplayName = string.IsNullOrWhiteSpace(semantic.Label) ? cardId : semantic.Label,
                            FirstEventSequence = item.Sequence
                        };
                        cards[cardId] = activeCard;
                    }

                    activeCard.Uses++;
                    turn.CardUses++;
                    if (!string.IsNullOrWhiteSpace(semantic.RootActionId)) actions[semantic.RootActionId] = activeCard;
                    activeTurn = turnIndex;
                }
                else if (semantic.Category == MatchSemanticCategories.Damage && semantic.Value > 0)
                {
                    var damage = Math.Max(0, semantic.Value);
                    turn.Damage += damage;
                    if (!string.IsNullOrWhiteSpace(semantic.RootActionId)
                        && actions.TryGetValue(semantic.RootActionId, out var attributed))
                    {
                        attributed.AttributedDamage += damage;
                        attributed.AttributionConfidence = semantic.AttributionConfidence;
                    }
                    else if (activeCard != null && activeTurn == turnIndex)
                    {
                        activeCard.ObservedFollowUpDamage += damage;
                        if (activeCard.AttributionConfidence == MatchAttributionConfidence.Unknown)
                        {
                            activeCard.AttributionConfidence = MatchAttributionConfidence.Inferred;
                        }
                    }

                    var sourceTeam = Team(teams, semantic.SourceInstanceId, semantic.ActorId);
                    var targetTeam = Team(teams, semantic.TargetInstanceId, semantic.TargetId);
                    var flowKey = sourceTeam + "|" + targetTeam;
                    if (!flows.TryGetValue(flowKey, out var flow))
                    {
                        flow = new MatchAnalysisDamageFlow { SourceTeam = sourceTeam, TargetTeam = targetTeam };
                        flows[flowKey] = flow;
                    }

                    if (string.Equals(semantic.Action, "ShieldDamage", StringComparison.Ordinal))
                    {
                        flow.ShieldDamage += damage;
                    }
                    else
                    {
                        flow.HpDamage += damage;
                    }
                    AddMoment(moments, item, semantic);
                }
                else if (semantic.IsKeyEvent)
                {
                    AddMoment(moments, item, semantic);
                }
            }

            if (activeTurn != turnIndex)
            {
                activeCard = null;
                actions.Clear();
                activeTurn = turnIndex;
            }
        }

        var turnCount = Math.Max(1, Math.Max(record.TurnCount, turns.Count == 0 ? 0 : turns.Keys.Max()));
        var orderedTurns = Enumerable.Range(1, turnCount)
            .Select(index => turns.TryGetValue(index, out var value) ? value : new MatchAnalysisTurn { TurnIndex = index })
            .ToList();
        ApplyAuthoritativeTurnDamage(orderedTurns, snapshot);
        var combatants = BuildCombatants(snapshot);
        var report = new MatchAnalysisReport
        {
            Protocol = MatchAnalysisProtocol.Version,
            RecordId = record.RecordId,
            GeneratedUtc = DateTime.UtcNow.ToString("O"),
            TurnCount = turnCount,
            Turns = orderedTurns,
            Combatants = combatants,
            Cards = cards.Values
                .OrderByDescending(item => item.Uses)
                .ThenByDescending(item => item.AttributedDamage + item.ObservedFollowUpDamage)
                .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
                .ToList(),
            DamageFlows = flows.Values
                .OrderBy(item => item.SourceTeam, StringComparer.Ordinal)
                .ThenBy(item => item.TargetTeam, StringComparer.Ordinal)
                .ToList()
        };
        report.TotalDamage = combatants.Sum(item => item.Damage);
        report.FriendlyDamageDealt = combatants.Where(item => item.Team == "Friendly").Sum(item => item.Damage);
        report.EnemyDamageDealt = combatants.Where(item => item.Team == "Enemy").Sum(item => item.Damage);
        report.FriendlyDamageTaken = report.DamageFlows.Where(item => item.TargetTeam == "Friendly").Sum(FlowDamage);
        report.EnemyDamageTaken = report.DamageFlows.Where(item => item.TargetTeam == "Enemy").Sum(FlowDamage);
        if (report.DamageFlows.Count == 0)
        {
            report.FriendlyDamageTaken = report.EnemyDamageDealt;
            report.EnemyDamageTaken = report.FriendlyDamageDealt;
        }

        report.HpDamage = (snapshot?.Combatants ?? new List<CombatantDamageStat>()).Sum(item => Math.Max(0, item?.TotalHpDamage ?? 0));
        report.ShieldDamage = (snapshot?.Combatants ?? new List<CombatantDamageStat>()).Sum(item => Math.Max(0, item?.TotalShieldDamage ?? 0));
        var bestTurn = orderedTurns.OrderByDescending(item => item.Damage).ThenBy(item => item.TurnIndex).FirstOrDefault();
        report.BestTurnDamage = bestTurn?.Damage ?? 0;
        report.BestTurnIndex = bestTurn?.TurnIndex ?? 0;
        report.CardUseCount = report.Cards.Sum(item => item.Uses);
        if (bestTurn != null && bestTurn.Damage > 0)
        {
            moments.Add(new MatchAnalysisMoment
            {
                Kind = "BestTurn",
                Label = "本局最高伤害回合",
                TurnIndex = bestTurn.TurnIndex,
                EventSequence = bestTurn.FirstEventSequence,
                Value = bestTurn.Damage
            });
        }

        report.KeyMoments = moments
            .GroupBy(item => item.Kind + "|" + item.EventSequence, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.EventSequence)
            .Take(24)
            .ToList();
        return report;
    }

    private static void ApplyAuthoritativeTurnDamage(List<MatchAnalysisTurn> turns, DamageMeterSnapshot? snapshot)
    {
        if (!(snapshot?.Combatants?.Any(item => item?.Rounds != null && item.Rounds.Count > 0) == true)) return;
        foreach (var turn in turns)
        {
            turn.Damage = snapshot.Combatants
                .Where(item => item != null && item.Team == DamageTeam.Friendly)
                .SelectMany(item => item.Rounds ?? new List<DamageRoundStat>())
                .Where(item => item.RoundIndex == turn.TurnIndex)
                .Sum(item => Math.Max(0, item.HpDamage) + Math.Max(0, item.ShieldDamage));
        }
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

    private static void AddMoment(List<MatchAnalysisMoment> moments, MatchReplayEvent item, MatchSemanticEvent semantic)
    {
        moments.Add(new MatchAnalysisMoment
        {
            Kind = semantic.Category,
            Label = Describe(semantic),
            TurnIndex = item.TurnIndex,
            EventSequence = item.Sequence,
            ElapsedMilliseconds = item.ElapsedMilliseconds,
            Value = semantic.Value
        });
        if (moments.Count <= 24) return;
        var least = moments.OrderBy(value => value.Value).ThenByDescending(value => value.EventSequence).First();
        moments.Remove(least);
    }

    private static long FlowDamage(MatchAnalysisDamageFlow item) => item.HpDamage + item.ShieldDamage;

    private static string Team(IReadOnlyDictionary<string, string> teams, params string[] ids)
    {
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && teams.TryGetValue(id, out var team)) return team;
        }

        return "Unknown";
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
        try { return string.IsNullOrWhiteSpace(json) ? null : AuraSharedJson.Deserialize<DamageMeterSnapshot>(json); }
        catch { return null; }
    }
}
