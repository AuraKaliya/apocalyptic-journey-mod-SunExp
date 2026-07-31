using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatActionSettlementPolicy
{
    public static bool HasMeaningfulProgress(
        CombatStateObservation before,
        CombatStateObservation after,
        CombatActionObservation action,
        out string reason)
    {
        if (before == null) throw new ArgumentNullException(nameof(before));
        if (after == null) throw new ArgumentNullException(nameof(after));
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (before.BattleSessionId != after.BattleSessionId)
        {
            reason = "battle session advanced";
            return true;
        }
        if (before.CurrentPower != after.CurrentPower
            || before.MaxPower != after.MaxPower
            || before.HandCount != after.HandCount)
        {
            reason = "energy or hand state changed";
            return true;
        }
        if (UnitChanged(before.Player, after.Player))
        {
            reason = "player combat state changed";
            return true;
        }
        if (UnitsChanged(before.Enemies, after.Enemies))
        {
            reason = "enemy combat state changed";
            return true;
        }
        if (DeckChanged(before, after))
        {
            reason = "deck zones changed";
            return true;
        }
        if (SourceAvailabilityChanged(before, after, action))
        {
            reason = "action availability or cooldown changed";
            return true;
        }
        if (MeaningfulFeaturesChanged(before.Features, after.Features))
        {
            reason = "public combat mechanic state changed";
            return true;
        }
        reason = "no semantic game-state effect observed";
        return false;
    }

    private static bool UnitChanged(
        CombatUnitObservation before,
        CombatUnitObservation after)
    {
        return before.CurrentHp != after.CurrentHp
               || before.MaxHp != after.MaxHp
               || before.Defend != after.Defend
               || !SameStatuses(before.Statuses, after.Statuses)
               || MeaningfulFeaturesChanged(
                   before.Features,
                   after.Features);
    }

    private static bool UnitsChanged(
        IReadOnlyList<CombatUnitObservation> before,
        IReadOnlyList<CombatUnitObservation> after)
    {
        if (before.Count != after.Count)
        {
            return true;
        }
        foreach (var unit in before)
        {
            var current = after.FirstOrDefault(candidate =>
                candidate.RuntimeId == unit.RuntimeId);
            if (current == null || UnitChanged(unit, current))
            {
                return true;
            }
        }
        return false;
    }

    private static bool SameStatuses(
        IReadOnlyList<CombatStatusObservation> before,
        IReadOnlyList<CombatStatusObservation> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }
        return before
            .OrderBy(status => status.StatusId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Level)
            .Zip(
                after
                    .OrderBy(
                        status => status.StatusId,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(status => status.Level),
                (left, right) =>
                    string.Equals(
                        left.StatusId,
                        right.StatusId,
                        StringComparison.OrdinalIgnoreCase)
                    && left.Level == right.Level
                    && left.UpperBound == right.UpperBound)
            .All(equal => equal);
    }

    private static bool DeckChanged(
        CombatStateObservation before,
        CombatStateObservation after)
    {
        return before.DeckKnowledge.DrawPileCount
               != after.DeckKnowledge.DrawPileCount
               || before.DeckKnowledge.DiscardPileCount
               != after.DeckKnowledge.DiscardPileCount
               || before.DeckKnowledge.ExhaustPileCount
               != after.DeckKnowledge.ExhaustPileCount
               || !SameCards(before.HandCardIds, after.HandCardIds)
               || !SameCards(
                   before.DiscardPileCardIds,
                   after.DiscardPileCardIds)
               || !SameCards(
                   before.ExhaustPileCardIds,
                   after.ExhaustPileCardIds);
    }

    private static bool SameCards(
        IReadOnlyList<string> before,
        IReadOnlyList<string> after)
    {
        return before.Count == after.Count
               && before
                   .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                   .SequenceEqual(
                       after.OrderBy(
                           value => value,
                           StringComparer.OrdinalIgnoreCase),
                       StringComparer.OrdinalIgnoreCase);
    }

    private static bool SourceAvailabilityChanged(
        CombatStateObservation before,
        CombatStateObservation after,
        CombatActionObservation selected)
    {
        var wasAvailable = before.Actions.Any(candidate =>
            SameSource(candidate, selected) && candidate.Legal);
        var isAvailable = after.Actions.Any(candidate =>
            SameSource(candidate, selected) && candidate.Legal);
        return wasAvailable != isAvailable;
    }

    private static bool SameSource(
        CombatActionObservation candidate,
        CombatActionObservation selected)
    {
        return candidate.Kind == selected.Kind
               && candidate.RuntimeId == selected.RuntimeId
               && string.Equals(
                   candidate.SourceId,
                   selected.SourceId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool MeaningfulFeaturesChanged(
        IReadOnlyDictionary<string, double> before,
        IReadOnlyDictionary<string, double> after)
    {
        foreach (var key in before.Keys.Concat(after.Keys).Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            if (IsBookkeepingFeature(key))
            {
                continue;
            }
            var left = before.TryGetValue(key, out var beforeValue)
                ? Finite(beforeValue)
                : 0d;
            var right = after.TryGetValue(key, out var afterValue)
                ? Finite(afterValue)
                : 0d;
            if (Math.Abs(left - right) > 0.000001d)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsBookkeepingFeature(string key)
    {
        return string.Equals(
                   key,
                   CombatTurnFeatureNames.ActionsTakenThisTurn,
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   key,
                   CombatTurnFeatureNames.EnergySpentThisTurn,
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   key,
                   CombatTurnFeatureNames.ConsecutiveNoProgressTurns,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
