using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatActionDominance
{
    public static CombatCandidateEvaluation? SelectDamageToBlockSetup(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates)
    {
        return candidates
            .Where(candidate => candidate?.Action != null
                                && candidate.Legal
                                && candidate.Action.Cost <= state.CurrentPower
                                && candidate.Action.Semantics
                                    ?.DamageToBlockSetup == true
                                && candidate.Action.Semantics.Risk <= 0d
                                && !candidate.Action.Semantics.RandomOutcome
                                && !candidate.Action.Semantics.EndsTurn)
            .Where(setup => candidates.Any(payoff =>
                payoff?.Action != null
                && payoff.Legal
                && !ReferenceEquals(payoff, setup)
                && !CombatEndTurnSafety.IsEndTurnEquivalent(payoff.Action)
                && payoff.Action.Cost
                   <= state.CurrentPower - setup.Action.Cost
                && CombatActionSemanticMetrics.ImmediateHpDamage(
                    payoff.Action.Semantics) > 0d))
            .OrderByDescending(candidate => candidate.RuleScore)
            .ThenBy(
                candidate => candidate.Action.CandidateId,
                StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static CombatCandidateEvaluation? SelectSafeFreeSetup(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile profile)
    {
        if (!profile.PreferDominantFreeSetup)
        {
            return null;
        }

        if (candidates.Any(candidate => candidate.Legal && candidate.Utility.Lethal > 0d))
        {
            return null;
        }

        return candidates
            .Where(candidate => IsSafeFreeSetup(state, candidate))
            .OrderByDescending(candidate => candidate.RuleScore)
            .ThenByDescending(candidate => candidate.Utility.Scaling)
            .ThenByDescending(candidate => candidate.Utility.Continuation)
            .ThenBy(candidate => candidate.Action.CandidateId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static bool IsSafeFreeSetup(
        CombatStateObservation state,
        CombatCandidateEvaluation candidate)
    {
        if (candidate == null || !candidate.Legal || candidate.Action == null)
        {
            return false;
        }

        var action = candidate.Action;
        var semantics = action.Semantics ?? new CombatActionSemantics();
        if (action.Cost != 0
            || semantics.RandomOutcome
            || semantics.OpensInteraction
            || semantics.Risk > 0d
            || semantics.Uncertainty > 0d
            || semantics.Damage > 0d
            || semantics.TrueDamage > 0d
            || semantics.DamageOverTime > 0d
            || action.TargetKind == CombatTargetKind.Enemy)
        {
            return false;
        }

        var handCapacity = Math.Max(0, 10 - state.HandCount);
        var effectiveDraw = Math.Min(Math.Max(0d, semantics.Draw), handCapacity);
        var knownPositive = effectiveDraw > 0d
                            || semantics.EnergyGain > 0d
                            || semantics.Buff > 0d
                            || semantics.CostReduction > 0d
                            || semantics.CardGeneration > 0d
                            || semantics.Scaling > 0d
                            || semantics.PersistentValue > 0d
                            || semantics.DamageMultiplierGain > 0d
                            || (semantics.StateChanges?.Values.Any(value => value > 0d) == true);
        return knownPositive && candidate.RuleScore > 0d;
    }
}
