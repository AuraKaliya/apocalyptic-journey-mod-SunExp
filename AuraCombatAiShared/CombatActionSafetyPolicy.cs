using System;
using System.Collections.Generic;
using System.Linq;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

/// <summary>
/// Runtime safety rules that the learned policy and tree value are not allowed
/// to override.  The rules deliberately operate on structured semantics rather
/// than content ids so newly discovered cards receive the same protection.
/// </summary>
public static class CombatActionSafetyPolicy
{
    public const string MinimumLossCertifiedFeature =
        "safety:minimum-loss-certified";

    public static bool IsAdmissible(
        CombatStateObservation state,
        CombatActionObservation action,
        DecisionUtility utility,
        out string reason)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (action == null) throw new ArgumentNullException(nameof(action));

        var selfLoss = ProjectedSelfHpLoss(state, action);
        if (selfLoss >= Math.Max(1, state.Player?.CurrentHp ?? 0)
            && !HasImmediateBattleWin(state, action)
            && !HasCertifiedRebirth(state))
        {
            reason =
                "projected action is fatal without an immediate win or certified rebirth";
            return false;
        }

        if (selfLoss > 0d
            && IsRepeatable(action)
            && !HasEnemyProgress(action)
            && !HasStructuredSystemProgress(action))
        {
            reason =
                "repeatable self-harm has no enemy progress or structured system progress";
            return false;
        }

        reason = "";
        return true;
    }

    public static bool IsRepeatableSelfHarmWithoutProgress(
        CombatActionObservation action)
    {
        return action != null
               && ProjectedSelfHpLoss(null, action) > 0d
               && IsRepeatable(action)
               && !HasEnemyProgress(action)
               && !HasStructuredSystemProgress(action);
    }

    public static double ProjectedIrreversibleLoss(
        CombatStateObservation state,
        CombatCandidateEvaluation candidate)
    {
        if (candidate?.Action == null)
        {
            return double.PositiveInfinity;
        }
        var action = candidate.Action;
        var selfLoss = ProjectedSelfHpLoss(state, action);
        var maximumHpLoss = Math.Max(
            0d,
            -Value(action.Semantics?.StateChanges, "playerMaxHp"));
        var risk = Math.Max(0d, candidate.SearchDeathRisk) * 100d;
        var semanticUnknown = Math.Max(0d, action.Semantics?.Uncertainty ?? 0d);
        return selfLoss * 4d
               + maximumHpLoss * 6d
               + risk
               + semanticUnknown * 2d
               + Math.Max(0d, -candidate.RuleScore);
    }

    public static void CertifyMinimumLoss(CombatActionObservation action)
    {
        if (action?.Features != null)
        {
            action.Features[MinimumLossCertifiedFeature] = 1d;
        }
    }

    public static bool HasMinimumLossCertificate(
        CombatActionObservation? action)
    {
        return action?.Features != null
               && Value(action.Features, MinimumLossCertifiedFeature) > 0.5d;
    }

    public static double ProjectedSelfHpLoss(
        CombatStateObservation? state,
        CombatActionObservation action)
    {
        var semantics = action?.Semantics ?? new CombatActionSemantics();
        var loss = Math.Max(0d, semantics.SelfHpLoss)
                   + Math.Max(0d, semantics.EndOfCycleSelfHpLoss);
        loss = Math.Max(
            loss,
            Math.Max(0d, -Value(semantics.StateChanges, "player.hp")));
        if (action?.Features != null
            && state?.Player != null
            && Value(action.Features, "selfMaxHpLossFraction") > 0d)
        {
            loss = Math.Max(
                loss,
                state.Player.MaxHp
                * Value(action.Features, "selfMaxHpLossFraction"));
        }
        return loss;
    }

    private static bool HasCertifiedRebirth(CombatStateObservation state)
    {
        var committed = Value(
            state.Features,
            CombatArchetypePolicy.RebirthCommittedFeature) > 0.5d;
        var stacks = Math.Max(
            Value(state.Features, CombatArchetypePolicy.RebirthStacksFeature),
            state.Player?.Statuses?
                .FirstOrDefault(status => string.Equals(
                    status.StatusId,
                    "buff_rebirth",
                    StringComparison.OrdinalIgnoreCase))?.Level ?? 0d);
        return committed && stacks >= 30d;
    }

    private static bool HasImmediateBattleWin(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var alive = state.Enemies
            .Where(enemy => enemy != null && enemy.Alive)
            .ToList();
        if (alive.Count == 0)
        {
            return true;
        }
        if (action.TargetRuntimeId != 0
            && (alive.Count != 1
                || alive[0].RuntimeId != action.TargetRuntimeId))
        {
            return false;
        }

        var semantics = action.Semantics ?? new CombatActionSemantics();
        var normalDamage = Math.Max(0d, semantics.Damage)
                           * Math.Max(1d, semantics.HitCount);
        var bypassDamage = Math.Max(0d, semantics.TrueDamage)
                           + Math.Max(0d, semantics.DamageOverTime);
        return alive.All(enemy =>
        {
            if (action.TargetRuntimeId != 0
                && enemy.RuntimeId != action.TargetRuntimeId)
            {
                return false;
            }
            var projection = CombatDamageLimitPolicy.Project(
                enemy,
                normalDamage,
                bypassDamage);
            return projection.HpDamage >= Math.Max(1, enemy.CurrentHp);
        });
    }

    private static bool IsRepeatable(CombatActionObservation action)
    {
        return Value(action.Features, "recycle") > 0d
               || Value(action.Features, "ouroboros") > 0d
               || Value(action.Features, "repeatable") > 0d
               || action.Cost <= 0
               && (action.Semantics?.CardGeneration ?? 0d) > 0d;
    }

    private static bool HasEnemyProgress(CombatActionObservation action)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        if (CombatActionSemanticMetrics.ImmediateHpDamage(semantics) > 0d
            || CombatActionSemanticMetrics.DeferredHpDamage(semantics) > 0d)
        {
            return true;
        }
        return semantics.TargetEffects.Any(effect =>
            effect.TargetRuntimeId != 0
            && effect.TargetRuntimeId != action.RuntimeId
            && effect.Kind is CombatSemanticEffectKind.Damage
                or CombatSemanticEffectKind.TrueDamage
                or CombatSemanticEffectKind.DirectHpLoss
                or CombatSemanticEffectKind.AddStatus
            && effect.EffectiveAmount > 0d);
    }

    private static bool HasStructuredSystemProgress(
        CombatActionObservation action)
    {
        return Value(action.Features, "systemProgressValue") > 0d
               || Value(action.Features, "systemProgressCertified") > 0.5d;
    }

    private static double Value(
        IReadOnlyDictionary<string, double>? values,
        string key)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }
}
