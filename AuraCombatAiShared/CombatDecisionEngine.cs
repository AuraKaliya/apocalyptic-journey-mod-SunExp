using System;
using System.Collections.Generic;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatDecisionEngine
{
    private readonly IDecisionResidualModel residualModel;

    public CombatDecisionEngine(IDecisionResidualModel? residualModel = null)
    {
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
    }

    public CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile? profile = null)
    {
        var selectedProfile = profile ?? new CombatDecisionProfile();
        if (state == null || state.Actions == null || state.Actions.Count == 0)
        {
            return new CombatDecision { Reason = "no candidates" };
        }

        var endTurn = (CombatActionObservation?)null;
        var candidates = new List<DecisionCandidate<CombatActionObservation>>(state.Actions.Count);
        for (var i = 0; i < state.Actions.Count; i++)
        {
            var action = state.Actions[i];
            if (action == null)
            {
                continue;
            }

            if (action.Kind == CombatActionKind.EndTurn)
            {
                endTurn = action;
                continue;
            }

            var rejectionReason = action.RejectionReason;
            var legal = action.Legal;
            if (legal)
            {
                legal = CombatAiRegistry.EvaluatePreflight(state, action, out rejectionReason);
            }
            if (legal)
            {
                CombatAiRegistry.ApplySemantics(state, action);
            }

            var utility = BuildUtility(state, action, selectedProfile);
            var features = BuildFeatures(state, action);
            action.Features = features;
            candidates.Add(new DecisionCandidate<CombatActionObservation>
            {
                Id = action.CandidateId,
                Action = action,
                Legal = legal,
                RejectionReason = legal ? "" : rejectionReason,
                Utility = utility,
                Features = features
            });
        }

        var engine = new DecisionEngine<CombatActionObservation>
        {
            Weights = selectedProfile.Weights ?? new DecisionWeights(),
            Graph = selectedProfile.Graph,
            ResidualModel = residualModel
        };
        var result = engine.Choose(candidates);
        if (result.HasAction && result.Score >= selectedProfile.MinimumActionScore)
        {
            return new CombatDecision
            {
                HasAction = true,
                Action = result.Action,
                Score = result.Score,
                Reason = result.Reason
            };
        }

        if (endTurn != null && endTurn.Legal)
        {
            return new CombatDecision
            {
                HasAction = true,
                Action = endTurn,
                Score = 0d,
                Reason = result.HasAction ? "best action below threshold" : "no positive legal action"
            };
        }

        return new CombatDecision { Reason = result.Reason };
    }

    public static DecisionUtility BuildUtility(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatDecisionProfile profile)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        var target = FindTarget(state, action.TargetRuntimeId);
        var player = state.Player ?? new CombatUnitObservation();
        var missingHp = Math.Max(0, player.MaxHp - player.CurrentHp);
        var lethal = target != null && target.Kind == CombatTargetKind.Enemy && semantics.Damage >= target.CurrentHp
            ? 8d
            : 0d;
        var unknown = Math.Max(0d, semantics.Uncertainty);
        var defend = semantics.Defend;
        var heal = semantics.Heal;
        var risk = semantics.Risk;
        if (action.TargetKind == CombatTargetKind.Enemy)
        {
            risk += defend + heal;
            defend = 0d;
            heal = 0d;
        }
        if (semantics.Damage == 0d
            && semantics.Defend == 0d
            && semantics.Heal == 0d
            && semantics.Draw == 0d
            && semantics.EnergyGain == 0d
            && semantics.Scaling == 0d
            && semantics.DeckValue == 0d)
        {
            unknown = Math.Max(unknown, profile.UnknownActionPenalty);
        }

        return new DecisionUtility
        {
            Survival = defend + Math.Min(missingHp, heal) * 1.15d,
            Lethal = lethal + semantics.Damage,
            Tempo = semantics.Damage * 0.55d + semantics.Defend * 0.35d,
            Resource = semantics.EnergyGain * 1.5d - action.Cost * 0.35d,
            DeckEconomy = semantics.DeckValue,
            Scaling = semantics.Scaling,
            Synergy = action.Features.TryGetValue("synergy", out var synergy) ? synergy : 0d,
            Continuation = semantics.Draw + semantics.EnergyGain,
            Risk = risk,
            Uncertainty = unknown,
            Coordination = action.Features.TryGetValue("coordination", out var coordination) ? coordination : 0d
        };
    }

    private static Dictionary<string, double> BuildFeatures(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var features = new Dictionary<string, double>(action.Features, StringComparer.OrdinalIgnoreCase)
        {
            ["power"] = state.CurrentPower,
            ["handCount"] = state.HandCount,
            ["playerHp"] = state.Player.CurrentHp,
            ["playerHpRatio"] = state.Player.MaxHp <= 0
                ? 0d
                : (double)state.Player.CurrentHp / state.Player.MaxHp,
            ["cost"] = action.Cost,
            ["damage"] = action.Semantics.Damage,
            ["defend"] = action.Semantics.Defend,
            ["heal"] = action.Semantics.Heal,
            ["draw"] = action.Semantics.Draw,
            ["uncertainty"] = action.Semantics.Uncertainty
        };
        var target = FindTarget(state, action.TargetRuntimeId);
        if (target != null)
        {
            features["targetHp"] = target.CurrentHp;
            features["targetHpRatio"] = target.MaxHp <= 0
                ? 0d
                : (double)target.CurrentHp / target.MaxHp;
        }

        return features;
    }

    private static CombatUnitObservation? FindTarget(CombatStateObservation state, int runtimeId)
    {
        if (runtimeId == 0)
        {
            return null;
        }

        if (state.Player.RuntimeId == runtimeId)
        {
            return state.Player;
        }

        for (var i = 0; i < state.Enemies.Count; i++)
        {
            if (state.Enemies[i].RuntimeId == runtimeId)
            {
                return state.Enemies[i];
            }
        }

        for (var i = 0; i < state.Friendlies.Count; i++)
        {
            if (state.Friendlies[i].RuntimeId == runtimeId)
            {
                return state.Friendlies[i];
            }
        }

        return null;
    }
}
