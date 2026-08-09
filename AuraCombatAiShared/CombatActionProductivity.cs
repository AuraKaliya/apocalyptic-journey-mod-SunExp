using System;
using System.Collections.Generic;

namespace AuraCombatAi.Shared;

public sealed class CombatActionProductivityAssessment
{
    public bool Productive { get; set; }

    public bool RecognizedSemantics { get; set; }

    public bool ExplicitlyHarmful { get; set; }

    public double MarginalBenefit { get; set; }

    public double MarginalHarm { get; set; }

    public double EnergyCarryOpportunityCost { get; set; }

    public double NetBenefit { get; set; }

    public bool UrgentDefense { get; set; }

    public CombatCycleOpportunityClassification CycleClassification { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatActionProductivity
{
    private const double Epsilon = 0.000001d;

    public static CombatActionProductivityAssessment Assess(
        CombatStateObservation state,
        CombatCandidateEvaluation candidate)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (candidate?.Action == null)
        {
            return Rejected("missing action");
        }
        var action = candidate.Action;
        if (!candidate.Legal
            || CombatEndTurnSafety.IsEndTurnEquivalent(action)
            || action.Cost > state.CurrentPower)
        {
            return Rejected("action is not currently executable");
        }
        if (Flag(action.Features, "visibleFake")
            || Flag(action.Features, "curse")
            || Flag(action.Features, "unplayable")
            || Flag(action.Features, "semanticUnavailable"))
        {
            return Rejected("action is explicitly unavailable or harmful");
        }

        var semantics = action.Semantics ?? new CombatActionSemantics();
        var cycleClassification =
            CombatCycleOpportunityClassifier.Classify(action);
        var cycleConnectorValue =
            CombatCycleOpportunityClassifier.ConnectorValue(
                cycleClassification,
                action.Features);
        var recognized = HasRecognizedSemantics(semantics)
                         || cycleClassification
                         != CombatCycleOpportunityClassification.None;
        var marginalBenefit =
            Positive(action.Features, "effectiveDurabilityDamage", "effectiveDamage")
            + Positive(action.Features, "immediateDefend")
            + Positive(action.Features, "effectiveHeal")
            + Positive(action.Features, "effectiveDraw")
            + Positive(action.Features, "marginalSetupValue")
            + Positive(action.Features, "handTransformNetValue")
            + Positive(action.Features, "postTransformLethalCertified") * 12d
            + Math.Max(0d, semantics.EnergyGain)
            + cycleConnectorValue;
        var marginalHarm =
            Math.Max(0d, semantics.SelfHpLoss) * 2d
            + Math.Max(0d, semantics.EndOfCycleSelfHpLoss) * 1.5d
            + Math.Max(0d, semantics.Risk);
        var energyCarryCost = CombatTurnRules.EnergyCarryOpportunityCost(
            state.CurrentPower,
            state.MaxPower,
            Math.Max(0, action.Cost),
            semantics.EnergyGain);
        var netBenefit = marginalBenefit - marginalHarm - energyCarryCost;
        var urgentDefense =
            Positive(action.Features, "immediateDefend") > Epsilon
            && state.ExpectedIncomingDamage > state.Player.Defend;
        var explicitlyHarmful =
            marginalHarm + energyCarryCost > marginalBenefit + Epsilon
            && marginalBenefit <= Epsilon;

        if (!recognized)
        {
            return new CombatActionProductivityAssessment
            {
                Productive = true,
                RecognizedSemantics = false,
                MarginalBenefit = marginalBenefit,
                MarginalHarm = marginalHarm,
                EnergyCarryOpportunityCost = energyCarryCost,
                NetBenefit = netBenefit,
                UrgentDefense = urgentDefense,
                CycleClassification = cycleClassification,
                Reason =
                    "playable non-curse action has unknown semantics; conservatively keep it ahead of end turn"
            };
        }
        if (explicitlyHarmful)
        {
            return new CombatActionProductivityAssessment
            {
                Productive = false,
                RecognizedSemantics = true,
                ExplicitlyHarmful = true,
                MarginalBenefit = marginalBenefit,
                MarginalHarm = marginalHarm,
                EnergyCarryOpportunityCost = energyCarryCost,
                NetBenefit = netBenefit,
                UrgentDefense = urgentDefense,
                CycleClassification = cycleClassification,
                Reason = "recognized action is purely harmful"
            };
        }
        var productive = netBenefit > Epsilon;
        return new CombatActionProductivityAssessment
        {
            Productive = productive,
            RecognizedSemantics = true,
            ExplicitlyHarmful = explicitlyHarmful,
            MarginalBenefit = marginalBenefit,
            MarginalHarm = marginalHarm,
            EnergyCarryOpportunityCost = energyCarryCost,
            NetBenefit = netBenefit,
            UrgentDefense = urgentDefense,
            CycleClassification = cycleClassification,
            Reason = productive
                ? cycleClassification
                  is CombatCycleOpportunityClassification.Certified
                    or CombatCycleOpportunityClassification.Reachable
                    ? "action has positive continuation value toward an executable cycle"
                    : "action has positive marginal combat value after energy carry cost"
                : energyCarryCost > Epsilon
                  && marginalBenefit > Epsilon
                    ? "action benefit does not exceed preserved surplus-energy cost"
                    : "recognized action is saturated in the current state"
        };
    }

    public static bool IsProductive(
        CombatSimulationState state,
        CombatActionObservation action,
        int effectiveCost,
        CombatDecisionProfile profile)
    {
        if (state == null
            || action == null
            || CombatEndTurnSafety.IsEndTurnEquivalent(action)
            || effectiveCost > state.Power
            || Flag(action.Features, "visibleFake")
            || Flag(action.Features, "curse")
            || Flag(action.Features, "unplayable")
            || Flag(action.Features, "semanticUnavailable"))
        {
            return false;
        }
        var semantics = action.Semantics ?? new CombatActionSemantics();
        if (!HasRecognizedSemantics(semantics))
        {
            return true;
        }

        var normalDamage = Math.Max(0d, semantics.Damage)
                           * Math.Max(1d, semantics.HitCount);
        var bypassDamage = Math.Max(0d, semantics.TrueDamage)
                           + Math.Max(0d, semantics.DamageOverTime);
        var damage = 0d;
        for (var i = 0; i < state.Enemies.Length; i++)
        {
            var enemy = state.Enemies[i];
            if (enemy.Hp <= 0
                || action.TargetRuntimeId != 0
                   && action.TargetRuntimeId != enemy.RuntimeId)
            {
                continue;
            }
            damage += CombatDamageLimitPolicy.Project(
                enemy,
                normalDamage,
                bypassDamage).DurabilityDamage;
        }
        var requiredDefend = Math.Max(
            0d,
            state.ActiveBlockableThreat(profile.ThreatRiskTolerance)
            - state.PlayerDefend);
        var immediateDefend = Math.Min(
            Math.Max(0d, semantics.Defend),
            requiredDefend);
        var missingHp = Math.Max(0, state.PlayerMaxHp - state.PlayerHp);
        var handCapacity = Math.Max(0, state.HandLimit - state.HandCount);
        var setup = MarginalSetupValue(state, semantics);
        var cycleClassification =
            CombatCycleOpportunityClassifier.Classify(action);
        var cycleConnectorValue =
            CombatCycleOpportunityClassifier.ConnectorValue(
                cycleClassification,
                action.Features);
        var benefit = damage
                      + immediateDefend
                      + Math.Min(missingHp, Math.Max(0d, semantics.Heal))
                      + Math.Min(handCapacity, Math.Max(0d, semantics.Draw))
                      + Math.Max(0d, semantics.EnergyGain)
                      + setup
                      + cycleConnectorValue
                      + Positive(action.Features, "handTransformNetValue")
                      + Positive(
                          action.Features,
                          "postTransformLethalCertified") * 12d;
        var harm = Math.Max(0d, semantics.SelfHpLoss) * 2d
                   + Math.Max(0d, semantics.EndOfCycleSelfHpLoss) * 1.5d
                   + Math.Max(0d, semantics.Risk);
        var energyCarryCost = CombatTurnRules.EnergyCarryOpportunityCost(
            state.Power,
            state.MaxPower,
            effectiveCost,
            semantics.EnergyGain);
        return benefit - harm - energyCarryCost > Epsilon;
    }

    public static double SetupValue(CombatActionSemantics semantics)
    {
        return Math.Max(0d, semantics.Buff)
               + Math.Max(0d, semantics.Debuff)
               + Math.Max(0d, semantics.Cleanse)
               + Math.Max(0d, semantics.CostReduction)
               + Math.Max(0d, semantics.CardGeneration)
               + Math.Max(0d, semantics.PersistentValue)
               + Math.Max(0d, semantics.Scaling)
               + Math.Max(0d, semantics.DamageMultiplierGain)
               + CombatActionSemanticMetrics.DeferredHpDamage(semantics) * 0.75d;
    }

    public static double MarginalSetupValue(
        CombatStateObservation state,
        CombatActionSemantics semantics)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (semantics == null) throw new ArgumentNullException(nameof(semantics));

        var handCapacity = Math.Max(0, 10 - state.HandCount);
        return Math.Max(0d, semantics.Buff)
               + Math.Max(0d, semantics.Debuff)
               + (HasCleansableStatus(state.Player.Statuses)
                   ? Math.Max(0d, semantics.Cleanse)
                   : 0d)
               + (state.HandCount > 1
                   ? Math.Max(0d, semantics.CostReduction)
                   : 0d)
               + (handCapacity > 0
                   ? Math.Max(0d, semantics.CardGeneration)
                   : 0d)
               + Math.Max(0d, semantics.PersistentValue)
               + Math.Max(0d, semantics.Scaling)
               + Math.Max(0d, semantics.DamageMultiplierGain)
               + CombatActionSemanticMetrics.DeferredHpDamage(semantics)
                 * 0.75d;
    }

    public static double MarginalSetupValue(
        CombatSimulationState state,
        CombatActionSemantics semantics)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (semantics == null) throw new ArgumentNullException(nameof(semantics));

        var handCapacity = Math.Max(0, state.HandLimit - state.HandCount);
        return Math.Max(0d, semantics.Buff)
               + Math.Max(0d, semantics.Debuff)
               + (state.HandCount > 1
                   ? Math.Max(0d, semantics.CostReduction)
                   : 0d)
               + (handCapacity > 0
                   ? Math.Max(0d, semantics.CardGeneration)
                   : 0d)
               + Math.Max(0d, semantics.PersistentValue)
               + Math.Max(0d, semantics.Scaling)
               + Math.Max(0d, semantics.DamageMultiplierGain)
               + CombatActionSemanticMetrics.DeferredHpDamage(semantics)
                 * 0.75d;
    }

    private static bool HasRecognizedSemantics(CombatActionSemantics semantics)
    {
        return Math.Max(0d, semantics.Damage)
               + Math.Max(0d, semantics.TrueDamage)
               + Math.Max(0d, semantics.DamageOverTime)
               + Math.Max(0d, semantics.SelfHpLoss)
               + Math.Max(0d, semantics.EndOfCycleSelfHpLoss)
               + Math.Max(0d, semantics.Defend)
               + Math.Max(0d, semantics.Heal)
               + Math.Max(0d, semantics.Draw)
               + Math.Max(0d, semantics.EnergyGain)
               + SetupValue(semantics) > Epsilon
               || semantics.HandTransform != null
               || semantics.Interaction?.EffectsComplete == true;
    }

    private static bool HasCleansableStatus(
        IReadOnlyList<CombatStatusObservation> statuses)
    {
        foreach (var status in statuses)
        {
            var type = status.Type ?? "";
            var id = status.StatusId ?? "";
            if (ContainsAny(
                    type,
                    "debuff",
                    "bad",
                    "negative",
                    "curse")
                || ContainsAny(
                    id,
                    "poison",
                    "burn",
                    "bleed",
                    "weak",
                    "vulnerable",
                    "curse",
                    "dot"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsAny(
        string value,
        params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (value.IndexOf(
                    token,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static CombatActionProductivityAssessment Rejected(string reason)
    {
        return new CombatActionProductivityAssessment { Reason = reason };
    }

    private static bool Flag(
        IReadOnlyDictionary<string, double> features,
        string key)
    {
        return features.TryGetValue(key, out var value)
               && IsFinite(value)
               && value > 0.5d;
    }

    private static double Positive(
        IReadOnlyDictionary<string, double> features,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (features.TryGetValue(key, out var value)
                && IsFinite(value))
            {
                return Math.Max(0d, value);
            }
        }
        return 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
