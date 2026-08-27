using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Analysis;

internal static class MatchAnalysisBuilder
{
    internal static MatchAnalysisReport BuildV12(MatchRecord record, ReplayDocumentV12 document)
    {
        var snapshot = ReadSnapshot(record.StatisticsJson);
        var turns = new Dictionary<int, MatchAnalysisTurn>();
        var cards = new Dictionary<string, MatchAnalysisCard>(StringComparer.Ordinal);
        var transactionCards = new Dictionary<string, MatchAnalysisCard>(StringComparer.Ordinal);
        var transactions = new Dictionary<string, ReplayCausalTransactionV12>(StringComparer.Ordinal);
        var flows = new Dictionary<string, MatchAnalysisDamageFlow>(StringComparer.Ordinal);
        var moments = new List<MatchAnalysisMoment>();
        var descriptors = document.Presentation.Cards.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal);
        var reducer = new ReplayStateReducerV12();
        reducer.Reset(document.InitialState);
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence))
        {
            var round = Math.Max(1, value.RoundSequence);
            if (!turns.TryGetValue(round, out var turn))
            {
                turn = new MatchAnalysisTurn { TurnIndex = round, FirstEventSequence = value.Sequence };
                turns[round] = turn;
            }
            turn.LastEventSequence = value.Sequence;
            if (value.EventType == ReplayEventTypesV12.TransactionStarted && value.Transaction != null)
            {
                transactions[value.TransactionId] = value.Transaction;
                if (value.Transaction.Kind is ReplayTransactionKindsV12.Card or ReplayTransactionKindsV12.Skill)
                {
                    turn.ActionCount++;
                    turn.CardUses++;
                    var descriptorId = value.Transaction.SourceDescriptorId ?? "";
                    var cardId = descriptors.TryGetValue(descriptorId, out var descriptor)
                        ? descriptor.Provenance.StableContentId
                        : descriptorId;
                    if (string.IsNullOrWhiteSpace(cardId)) cardId = "UnknownCard";
                    if (!cards.TryGetValue(cardId, out var analysisCard))
                    {
                        analysisCard = new MatchAnalysisCard
                        {
                            CardId = cardId,
                            DisplayName = descriptor?.Name ?? value.Transaction.Label ?? cardId,
                            FirstEventSequence = value.Sequence,
                            AttributionConfidence = MatchAttributionConfidence.Exact
                        };
                        cards[cardId] = analysisCard;
                    }
                    analysisCard.Uses++;
                    transactionCards[value.TransactionId] = analysisCard;
                }
                else if (value.Transaction.Kind is ReplayTransactionKindsV12.Intent or ReplayTransactionKindsV12.ImplicitNative)
                {
                    turn.ActionCount++;
                }
            }

            var before = reducer.Current;
            reducer.Apply(value);
            var after = reducer.Current;
            var beforeEntities = before.Entities.ToDictionary(item => item.EntityId + "|" + item.SpawnGeneration, StringComparer.Ordinal);
            foreach (var target in after.Entities)
            {
                if (!beforeEntities.TryGetValue(target.EntityId + "|" + target.SpawnGeneration, out var previous)) continue;
                var hpDamage = Math.Max(0, previous.CurrentHp - target.CurrentHp);
                var shieldDamage = Math.Max(0, previous.Defense - target.Defense);
                var damage = hpDamage + shieldDamage;
                if (damage <= 0) continue;
                turn.Damage += damage;
                var sourceId = transactions.TryGetValue(value.TransactionId, out var transaction)
                    ? transaction.ActorId
                    : value.ActorId;
                var sourceTeam = TeamV12(after, sourceId);
                var targetTeam = target.Team ?? "Unknown";
                var flowKey = sourceTeam + "|" + targetTeam;
                if (!flows.TryGetValue(flowKey, out var flow))
                {
                    flow = new MatchAnalysisDamageFlow { SourceTeam = sourceTeam, TargetTeam = targetTeam };
                    flows[flowKey] = flow;
                }
                flow.HpDamage += hpDamage;
                flow.ShieldDamage += shieldDamage;
                if (transactionCards.TryGetValue(value.TransactionId, out var sourceCard))
                    sourceCard.AttributedDamage += damage;
                if (moments.Count < 24)
                    moments.Add(new MatchAnalysisMoment
                    {
                        Kind = "Damage",
                        Label = (transactions.TryGetValue(value.TransactionId, out var source) ? source.Label : "行动")
                                + " 造成 " + damage + " 点伤害",
                        TurnIndex = round,
                        EventSequence = value.Sequence,
                        ElapsedMilliseconds = value.TimeTicks * 1000L / ReplayProtocolV12.TimebaseTicksPerSecond,
                        Value = damage
                    });
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
            Cards = cards.Values.OrderByDescending(item => item.Uses)
                .ThenByDescending(item => item.AttributedDamage)
                .ThenBy(item => item.DisplayName, StringComparer.Ordinal).ToList(),
            DamageFlows = flows.Values.OrderBy(item => item.SourceTeam, StringComparer.Ordinal)
                .ThenBy(item => item.TargetTeam, StringComparer.Ordinal).ToList(),
            KeyMoments = moments.OrderBy(item => item.EventSequence).Take(24).ToList()
        };
        report.TotalDamage = combatants.Sum(item => item.Damage);
        report.FriendlyDamageDealt = combatants.Where(item => item.Team == ReplayTeamsV12.Friendly).Sum(item => item.Damage);
        report.EnemyDamageDealt = combatants.Where(item => item.Team == ReplayTeamsV12.Enemy).Sum(item => item.Damage);
        report.FriendlyDamageTaken = report.DamageFlows.Where(item => item.TargetTeam == ReplayTeamsV12.Friendly).Sum(FlowDamage);
        report.EnemyDamageTaken = report.DamageFlows.Where(item => item.TargetTeam == ReplayTeamsV12.Enemy).Sum(FlowDamage);
        report.HpDamage = report.DamageFlows.Sum(item => item.HpDamage);
        report.ShieldDamage = report.DamageFlows.Sum(item => item.ShieldDamage);
        var best = orderedTurns.OrderByDescending(item => item.Damage).ThenBy(item => item.TurnIndex).FirstOrDefault();
        report.BestTurnDamage = best?.Damage ?? 0;
        report.BestTurnIndex = best?.TurnIndex ?? 0;
        report.CardUseCount = report.Cards.Sum(item => item.Uses);
        return report;
    }

    internal static MatchAnalysisReport BuildSummary(MatchRecord record)
    {
        var snapshot = ReadSnapshot(record.StatisticsJson);
        var turnCount = Math.Max(1, record.TurnCount);
        var turns = Enumerable.Range(1, turnCount)
            .Select(index => new MatchAnalysisTurn { TurnIndex = index })
            .ToList();
        ApplyAuthoritativeTurnDamage(turns, snapshot);
        var combatants = BuildCombatants(snapshot);
        var report = new MatchAnalysisReport
        {
            Protocol = MatchAnalysisProtocol.Version,
            RecordId = record.RecordId,
            GeneratedUtc = DateTime.UtcNow.ToString("O"),
            TurnCount = turnCount,
            Turns = turns,
            Combatants = combatants
        };
        report.TotalDamage = combatants.Sum(item => item.Damage);
        report.FriendlyDamageDealt = combatants.Where(item => item.Team == ReplayTeamsV12.Friendly).Sum(item => item.Damage);
        report.EnemyDamageDealt = combatants.Where(item => item.Team == ReplayTeamsV12.Enemy).Sum(item => item.Damage);
        var best = turns.OrderByDescending(item => item.Damage).ThenBy(item => item.TurnIndex).FirstOrDefault();
        report.BestTurnDamage = best?.Damage ?? 0;
        report.BestTurnIndex = best?.TurnIndex ?? 0;
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

    private static long FlowDamage(MatchAnalysisDamageFlow item) => item.HpDamage + item.ShieldDamage;

    private static string TeamV12(ReplayPublicStateV12 state, string entityId)
    {
        return state.Entities.LastOrDefault(item => string.Equals(item.EntityId, entityId, StringComparison.Ordinal))?.Team
               ?? "Unknown";
    }

    private static DamageMeterSnapshot? ReadSnapshot(string json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : AuraSharedJson.Deserialize<DamageMeterSnapshot>(json); }
        catch { return null; }
    }
}
