using System;
using System.Collections.Generic;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatDecisionEngine
{
    private readonly IDecisionResidualModel residualModel;
    private readonly ICombatSearchGuidanceModel searchGuidance;
    private readonly bool useRuntimeRegistries;

    public CombatDecisionEngine(
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? searchGuidance = null,
        bool useRuntimeRegistries = true)
    {
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
        this.searchGuidance = searchGuidance ?? NullCombatSearchGuidanceModel.Instance;
        this.useRuntimeRegistries = useRuntimeRegistries;
    }

    public CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile? profile = null)
    {
        var selectedProfile = profile ?? new CombatDecisionProfile();
        selectedProfile.Weights ??= new DecisionWeights();
        if (state == null || state.Actions == null || state.Actions.Count == 0)
        {
            return new CombatDecision { Reason = "no candidates" };
        }

        var endTurn = (CombatActionObservation?)null;
        var evaluations = new List<CombatCandidateEvaluation>(state.Actions.Count);
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
                action.Features = BuildFeatures(state, action);
                evaluations.Add(new CombatCandidateEvaluation
                {
                    Action = action,
                    Legal = action.Legal,
                    RejectionReason = action.RejectionReason,
                    RuleScore = 0d
                });
                continue;
            }

            var rejectionReason = action.RejectionReason;
            var legal = action.Legal;
            if (legal && useRuntimeRegistries)
            {
                legal = CombatAiRegistry.EvaluatePreflight(state, action, out rejectionReason);
            }
            if (legal && useRuntimeRegistries)
            {
                CombatAiRegistry.ApplySemantics(state, action);
            }

            var utility = BuildUtility(state, action, selectedProfile);
            var features = BuildFeatures(state, action, utility, selectedProfile);
            action.Features = features;
            var evaluatedUtility = utility.Clone();
            var graphEvaluation = DecisionGraphEvaluator.Evaluate(selectedProfile.Graph, features);
            evaluatedUtility.Add(graphEvaluation.UtilityDelta);
            if (graphEvaluation.Rejected)
            {
                legal = false;
                rejectionReason = "decision graph rejected candidate";
            }

            var baseRuleScore = legal
                ? selectedProfile.Weights.Score(evaluatedUtility)
                : 0d;
            var residual = legal
                ? EvaluateResidual(residualModel, features)
                : new DecisionResidualPrediction();
            evaluations.Add(new CombatCandidateEvaluation
            {
                Action = action,
                Legal = legal,
                RejectionReason = legal ? "" : rejectionReason,
                Utility = evaluatedUtility,
                BaseRuleScore = baseRuleScore,
                RawResidualScore = residual.RawCorrection,
                ResidualApplicability = residual.Applicability,
                AppliedResidualScore = residual.AppliedCorrection,
                RuleScore = baseRuleScore + residual.AppliedCorrection
            });
        }

        var search = selectedProfile.UseChancePuct
            ? new CombatChancePuctPlanner(
                    residualModel,
                    searchGuidance,
                    useRuntimeRegistries)
                .Choose(state, evaluations, selectedProfile)
            : null;
        var plan = search == null
            ? new CombatTurnPlanner(residualModel).Choose(state, evaluations, selectedProfile)
            : null;
        var hasPlanAction = search?.HasAction == true || plan?.HasAction == true;
        var planAction = search?.Action ?? plan?.Action;
        var planScore = search?.Score ?? plan?.Score ?? 0d;
        var planSteps = search?.Steps ?? plan?.Steps ?? new List<CombatPlanStep>();
        var planSummary = search?.Summary ?? plan?.Summary ?? "";
        if (hasPlanAction
            && planAction != null
            && planScore >= selectedProfile.MinimumActionScore)
        {
            return new CombatDecision
            {
                HasAction = true,
                Action = planAction,
                Score = planScore,
                Reason = search == null ? "beam plan" : "risk-aware chance-puct",
                ProfileId = selectedProfile.Id,
                Candidates = evaluations,
                Plan = planSteps,
                PlanSummary = planSummary,
                SearchAlgorithm = search == null ? "bounded-beam" : "chance-puct",
                SearchSimulations = search?.Simulations ?? 0,
                SearchNodes = search?.Nodes ?? 0,
                SearchTranspositionHits = search?.TranspositionHits ?? 0
            };
        }

        if (endTurn != null && endTurn.Legal)
        {
            return new CombatDecision
            {
                HasAction = true,
                Action = endTurn,
                Score = 0d,
                Reason = hasPlanAction ? "best plan below threshold" : "no positive legal action",
                ProfileId = selectedProfile.Id,
                Candidates = evaluations,
                PlanSummary = planSummary,
                SearchAlgorithm = search == null ? "bounded-beam" : "chance-puct",
                SearchSimulations = search?.Simulations ?? 0,
                SearchNodes = search?.Nodes ?? 0,
                SearchTranspositionHits = search?.TranspositionHits ?? 0
            };
        }

        return new CombatDecision
        {
            Reason = planSummary,
            ProfileId = selectedProfile.Id,
            Candidates = evaluations,
            SearchAlgorithm = search == null ? "bounded-beam" : "chance-puct",
            SearchSimulations = search?.Simulations ?? 0,
            SearchNodes = search?.Nodes ?? 0,
            SearchTranspositionHits = search?.TranspositionHits ?? 0
        };
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
        var hpRatio = player.MaxHp <= 0 ? 1d : (double)player.CurrentHp / player.MaxHp;
        var targetHp = target != null && target.Kind == CombatTargetKind.Enemy
            ? Math.Max(0, target.CurrentHp)
            : 0;
        var targetDefend = target != null && target.Kind == CombatTargetKind.Enemy
            ? Math.Max(0, target.Defend)
            : 0;
        var hitCount = Math.Max(1d, semantics.HitCount);
        var normalDamage = Math.Max(0d, semantics.Damage) * hitCount;
        var bypassDamage = Math.Max(0d, semantics.TrueDamage) + Math.Max(0d, semantics.DamageOverTime);
        var hpDamage = Math.Max(0d, normalDamage - targetDefend) + bypassDamage;
        var effectiveDamage = targetHp > 0
            ? Math.Min(targetHp + targetDefend, normalDamage) + Math.Min(targetHp, bypassDamage)
            : normalDamage + bypassDamage;
        var overkill = targetHp > 0 ? Math.Max(0d, hpDamage - targetHp) : 0d;
        var lethal = targetHp > 0 && hpDamage >= targetHp
            ? 16d + Math.Min(8d, targetHp * 0.25d)
            : 0d;
        var unknown = Math.Max(0d, semantics.Uncertainty);
        var defend = Math.Max(0d, semantics.Defend);
        var heal = Math.Min(missingHp, Math.Max(0d, semantics.Heal));
        var risk = semantics.Risk;
        if (action.TargetKind == CombatTargetKind.Enemy)
        {
            risk += defend + heal;
            defend = 0d;
            heal = 0d;
        }
        var threat = state.Threat ?? new CombatThreatForecast();
        var riskAdjustedBlockable = threat.RiskAdjustedBlockableDamage(profile.ThreatRiskTolerance);
        if (!threat.CurrentIntentKnown
            && riskAdjustedBlockable <= 0d
            && state.ExpectedIncomingDamage > 0d)
        {
            riskAdjustedBlockable = state.ExpectedIncomingDamage;
        }
        var incomingGap = Math.Max(0d, riskAdjustedBlockable - player.Defend);
        var surplusDefend = Math.Max(0d, defend - incomingGap);
        var effectiveDefend = Math.Min(defend, incomingGap)
                              + surplusDefend * Math.Max(0d, profile.SurplusDefendRetention);
        if (!threat.CurrentIntentKnown && riskAdjustedBlockable <= 0d)
        {
            effectiveDefend += defend * (1d - hpRatio) * 0.1d;
        }
        var emergency = hpRatio <= profile.EmergencyHpRatio && (effectiveDefend > 0d || heal > 0d)
            ? 4d
            : 0d;
        var handCapacity = Math.Max(0, 10 - state.HandCount);
        var effectiveDraw = Math.Min(Math.Max(0d, semantics.Draw), handCapacity);
        var followUpCount = Math.Max(0, state.HandCount - 1);
        var setupValue = Math.Max(0d, semantics.Buff) * 0.8d
                         + Math.Max(0d, semantics.Debuff) * 0.9d
                         + Math.Max(0d, semantics.Cleanse)
                         + Math.Max(0d, semantics.PersistentValue)
                         + Math.Max(0d, semantics.CostReduction) * Math.Min(3, followUpCount) * 0.65d
                         + Math.Max(0d, semantics.CardGeneration) * Math.Min(2, handCapacity) * 0.8d;
        var scarcity = state.MaxPower <= 0
            ? 1d
            : 1d - Math.Min(1d, (double)state.CurrentPower / state.MaxPower);
        var energyOpportunityCost = Math.Max(0, action.Cost) * (0.75d + scarcity * 0.5d);
        var cooldownCost = action.Kind == CombatActionKind.UseSkill
            ? Math.Max(0d, semantics.CooldownTurns) * profile.SkillCooldownPenalty
            : 0d;
        var knownPositive = effectiveDamage + effectiveDefend + heal + effectiveDraw
                            + semantics.EnergyGain + semantics.Scaling + semantics.DeckValue
                            + setupValue > 0d;
        var freeActionOrderValue = action.Cost == 0
                                   && knownPositive
                                   && !semantics.RandomOutcome
            ? profile.FreeActionTieBreaker
            : 0d;
        risk += overkill * 0.15d;
        risk += surplusDefend * (threat.CurrentIntentKnown ? 0.12d : 0.04d);
        if (semantics.RandomOutcome)
        {
            risk += 0.35d;
        }
        if (semantics.OpensInteraction)
        {
            risk += 0.1d;
        }
        if (semantics.Damage == 0d
            && semantics.Defend == 0d
            && semantics.Heal == 0d
            && semantics.Draw == 0d
            && semantics.EnergyGain == 0d
            && semantics.Scaling == 0d
            && semantics.DeckValue == 0d
            && setupValue == 0d)
        {
            unknown = Math.Max(unknown, profile.UnknownActionPenalty);
        }

        return new DecisionUtility
        {
            Survival = emergency + effectiveDefend + heal * 1.15d,
            Lethal = lethal,
            Tempo = effectiveDamage + effectiveDefend * 0.2d,
            Resource = semantics.EnergyGain * 1.5d
                       + semantics.CostReduction * 0.8d
                       - energyOpportunityCost
                       - cooldownCost,
            DeckEconomy = semantics.DeckValue + semantics.CardGeneration * 0.5d,
            Scaling = semantics.Scaling + setupValue,
            Synergy = action.Features.TryGetValue("synergy", out var synergy) ? synergy : 0d,
            Continuation = effectiveDraw + semantics.EnergyGain + semantics.CardGeneration * 0.5d,
            Risk = risk,
            Uncertainty = unknown,
            Coordination = freeActionOrderValue
                           + (action.Features.TryGetValue("coordination", out var coordination) ? coordination : 0d)
        };
    }

    public static Dictionary<string, double> BuildFeatures(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var profile = new CombatDecisionProfile();
        var utility = BuildUtility(state, action, profile);
        return BuildFeatures(state, action, utility, profile);
    }

    public static Dictionary<string, double> BuildFeatures(
        CombatStateObservation state,
        CombatActionObservation action,
        DecisionUtility utility,
        CombatDecisionProfile profile)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        var features = new Dictionary<string, double>(action.Features, StringComparer.OrdinalIgnoreCase)
        {
            ["power"] = state.CurrentPower,
            ["handCount"] = state.HandCount,
            ["playerHp"] = state.Player.CurrentHp,
            ["playerHpRatio"] = state.Player.MaxHp <= 0
                ? 0d
                : (double)state.Player.CurrentHp / state.Player.MaxHp,
            ["cost"] = action.Cost,
            ["damage"] = semantics.Damage,
            ["trueDamage"] = semantics.TrueDamage,
            ["damageOverTime"] = semantics.DamageOverTime,
            ["hitCount"] = semantics.HitCount,
            ["defend"] = semantics.Defend,
            ["heal"] = semantics.Heal,
            ["draw"] = semantics.Draw,
            ["energyGain"] = semantics.EnergyGain,
            ["buff"] = semantics.Buff,
            ["debuff"] = semantics.Debuff,
            ["cleanse"] = semantics.Cleanse,
            ["costReduction"] = semantics.CostReduction,
            ["cardGeneration"] = semantics.CardGeneration,
            ["persistentValue"] = semantics.PersistentValue,
            ["cooldownTurns"] = semantics.CooldownTurns,
            ["expectedIncomingDamage"] = state.ExpectedIncomingDamage,
            ["expectedBlockableDamage"] = state.Threat?.ExpectedBlockableDamage ?? 0d,
            ["maximumBlockableDamage"] = state.Threat?.MaximumBlockableDamage ?? 0d,
            ["expectedUnblockableDamage"] = state.Threat?.ExpectedUnblockableDamage ?? 0d,
            ["expectedDamageOverTime"] = state.Threat?.ExpectedDamageOverTime ?? 0d,
            ["attackProbability"] = state.Threat?.AttackProbability ?? 0d,
            ["threatConfidence"] = state.Threat?.Confidence ?? 0d,
            ["currentIntentKnown"] = state.Threat?.CurrentIntentKnown == true ? 1d : 0d,
            ["isFreeAction"] = action.Cost == 0 ? 1d : 0d,
            ["uncertainty"] = semantics.Uncertainty
        };
        foreach (var pair in state.Features)
        {
            if (!features.ContainsKey(pair.Key))
            {
                features[pair.Key] = pair.Value;
            }
        }
        var target = FindTarget(state, action.TargetRuntimeId);
        if (target != null)
        {
            features["targetHp"] = target.CurrentHp;
            features["targetHpRatio"] = target.MaxHp <= 0
                ? 0d
                : (double)target.CurrentHp / target.MaxHp;
        }

        AddContextualFeatures(features, state, action, utility, profile, target);
        return features;
    }

    public static DecisionResidualPrediction EvaluateResidual(
        IDecisionResidualModel model,
        IReadOnlyDictionary<string, double> features)
    {
        if (model is IContextualDecisionResidualModel contextual)
        {
            return contextual.Evaluate(features);
        }

        var correction = model?.Predict(features) ?? 0d;
        return new DecisionResidualPrediction
        {
            ModelId = model?.ModelId ?? "none",
            RawCorrection = correction,
            Applicability = correction == 0d ? 0d : 1d,
            AppliedCorrection = correction
        };
    }

    private static void AddContextualFeatures(
        IDictionary<string, double> features,
        CombatStateObservation state,
        CombatActionObservation action,
        DecisionUtility utility,
        CombatDecisionProfile profile,
        CombatUnitObservation? target)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        var player = state.Player ?? new CombatUnitObservation();
        var threat = state.Threat ?? new CombatThreatForecast();
        var riskAdjustedBlockable = threat.RiskAdjustedBlockableDamage(profile.ThreatRiskTolerance);
        if (!threat.CurrentIntentKnown
            && riskAdjustedBlockable <= 0d
            && state.ExpectedIncomingDamage > 0d)
        {
            riskAdjustedBlockable = state.ExpectedIncomingDamage;
        }

        var defend = action.TargetKind == CombatTargetKind.Enemy
            ? 0d
            : Math.Max(0d, semantics.Defend);
        var requiredDefend = Math.Max(0d, riskAdjustedBlockable - player.Defend);
        var usefulDefend = Math.Min(defend, requiredDefend);
        var wastedDefend = Math.Max(0d, defend - usefulDefend);
        var missingHp = Math.Max(0d, player.MaxHp - player.CurrentHp);
        var heal = Math.Max(0d, semantics.Heal);
        var handCapacity = Math.Max(0d, 10 - state.HandCount);
        var draw = Math.Max(0d, semantics.Draw);
        var normalDamage = Math.Max(0d, semantics.Damage) * Math.Max(1d, semantics.HitCount);
        var bypassDamage = Math.Max(0d, semantics.TrueDamage)
                           + Math.Max(0d, semantics.DamageOverTime);
        var targetHp = target != null && target.Kind == CombatTargetKind.Enemy
            ? Math.Max(0d, target.CurrentHp)
            : 0d;
        var targetDefend = target != null && target.Kind == CombatTargetKind.Enemy
            ? Math.Max(0d, target.Defend)
            : 0d;
        var hpDamage = Math.Max(0d, normalDamage - targetDefend) + bypassDamage;
        var effectiveDamage = targetHp > 0d
            ? Math.Min(targetHp + targetDefend, normalDamage) + Math.Min(targetHp, bypassDamage)
            : normalDamage + bypassDamage;
        var setupValue = Math.Max(0d, semantics.Buff)
                         + Math.Max(0d, semantics.Debuff)
                         + Math.Max(0d, semantics.Cleanse)
                         + Math.Max(0d, semantics.CostReduction)
                         + Math.Max(0d, semantics.CardGeneration)
                         + Math.Max(0d, semantics.PersistentValue)
                         + Math.Max(0d, semantics.Scaling);
        var usefulNow = effectiveDamage + usefulDefend
                        + Math.Min(heal, missingHp)
                        + Math.Min(draw, handCapacity)
                        + Math.Max(0d, semantics.EnergyGain)
                        + setupValue > 0d;
        var recognizedSemantics = normalDamage + bypassDamage
                                  + defend
                                  + heal
                                  + draw
                                  + Math.Max(0d, semantics.EnergyGain)
                                  + setupValue > 0d;
        var semanticConfidence = recognizedSemantics
            ? 1d - Math.Min(1d, Math.Max(0d, semantics.Uncertainty) / 3d)
            : 0d;
        if (semantics.RandomOutcome)
        {
            semanticConfidence *= 0.7d;
        }

        features["requiredDefend"] = requiredDefend;
        features["usefulDefend"] = usefulDefend;
        features["wastedDefend"] = wastedDefend;
        features["effectiveHeal"] = Math.Min(heal, missingHp);
        features["overheal"] = Math.Max(0d, heal - missingHp);
        features["effectiveDraw"] = Math.Min(draw, handCapacity);
        features["overdraw"] = Math.Max(0d, draw - handCapacity);
        features["effectiveDamage"] = effectiveDamage;
        features["overkill"] = targetHp > 0d ? Math.Max(0d, hpDamage - targetHp) : 0d;
        features["lethal"] = targetHp > 0d && hpDamage >= targetHp ? 1d : 0d;
        features["energyScarcity"] = state.MaxPower <= 0
            ? 1d
            : 1d - Math.Min(1d, (double)state.CurrentPower / state.MaxPower);
        features["freeKnownValue"] = action.Cost == 0 && usefulNow && !semantics.RandomOutcome ? 1d : 0d;
        features["semanticConfidence"] = Math.Max(0d, Math.Min(1d, semanticConfidence));
        features["utilitySurvival"] = utility.Survival;
        features["utilityLethal"] = utility.Lethal;
        features["utilityTempo"] = utility.Tempo;
        features["utilityResource"] = utility.Resource;
        features["utilityDeckEconomy"] = utility.DeckEconomy;
        features["utilityScaling"] = utility.Scaling;
        features["utilitySynergy"] = utility.Synergy;
        features["utilityContinuation"] = utility.Continuation;
        features["utilityRisk"] = utility.Risk;
        features["utilityUncertainty"] = utility.Uncertainty;
        features["utilityCoordination"] = utility.Coordination;

        var category = CategoryOf(action);
        features["categoryAttack"] = category == "attack" ? 1d : 0d;
        features["categoryDefend"] = category == "defend" ? 1d : 0d;
        features["categorySupport"] = category == "support" ? 1d : 0d;
        features["categorySkill"] = category == "skill" ? 1d : 0d;
        features["categoryOther"] = category == "other" ? 1d : 0d;
    }

    private static string CategoryOf(CombatActionObservation action)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        if (semantics.Damage > 0d || semantics.TrueDamage > 0d || semantics.DamageOverTime > 0d)
        {
            return "attack";
        }
        if (semantics.Defend > 0d)
        {
            return "defend";
        }
        if (semantics.Heal > 0d
            || semantics.Draw > 0d
            || semantics.EnergyGain > 0d
            || semantics.Buff > 0d
            || semantics.Debuff > 0d
            || semantics.Cleanse > 0d
            || semantics.CostReduction > 0d
            || semantics.CardGeneration > 0d
            || semantics.PersistentValue > 0d
            || semantics.Scaling > 0d)
        {
            return "support";
        }
        return action.Kind == CombatActionKind.UseSkill ? "skill" : "other";
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
