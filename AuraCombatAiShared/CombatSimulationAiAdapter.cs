using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatDecisionSimulationPolicy : ICombatSimulationPolicy
{
    private readonly CombatDecisionEngine decisionEngine;
    private readonly CombatDecisionProfile profile;

    public CombatDecisionSimulationPolicy(
        CombatDecisionProfile? profile = null,
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        ICombatPolicyValueModel? policyValueModel = null)
    {
        this.profile = profile ?? new CombatDecisionProfile();
        decisionEngine = new CombatDecisionEngine(
            residualModel,
            guidanceModel,
            useRuntimeRegistries: false,
            policyValueModel);
    }

    public string PolicyId => "aura-combat-decision:" + profile.Id;

    public CombatDecision? LastDecision { get; private set; }

    public CombatStateObservation? LastObservation { get; private set; }

    public CombatSimulationAction? SelectAction(CombatSimulationPolicyContext context)
    {
        var observation = CombatSimulationObservationProjector.Project(context);
        var decision = decisionEngine.Choose(observation, profile);
        LastObservation = observation;
        LastDecision = decision;
        if (!decision.HasAction || decision.Action == null)
        {
            return context.LegalActions.FirstOrDefault(action =>
                action.Kind == CombatSimulationActionKind.EndTurn);
        }
        return context.LegalActions.FirstOrDefault(action =>
                   string.Equals(
                       action.CandidateId,
                       decision.Action.CandidateId,
                       StringComparison.Ordinal))
               ?? context.LegalActions.FirstOrDefault(action =>
                   action.Kind == CombatSimulationActionKind.EndTurn);
    }
}

public sealed class CombatDecisionSimulationPolicyFactory : ICombatSimulationPolicyFactory
{
    private readonly CombatDecisionProfile profile;
    private readonly IDecisionResidualModel residualModel;
    private readonly ICombatSearchGuidanceModel guidanceModel;
    private readonly ICombatPolicyValueModel policyValueModel;

    public CombatDecisionSimulationPolicyFactory(
        CombatDecisionProfile? profile = null,
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        ICombatPolicyValueModel? policyValueModel = null)
    {
        this.profile = profile ?? new CombatDecisionProfile();
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
        this.guidanceModel = guidanceModel ?? NullCombatSearchGuidanceModel.Instance;
        this.policyValueModel = policyValueModel ?? NullCombatPolicyValueModel.Instance;
    }

    public string PolicyId => "aura-combat-decision:" + profile.Id;

    public ICombatSimulationPolicy Create()
    {
        return new CombatDecisionSimulationPolicy(
            profile,
            residualModel,
            guidanceModel,
            policyValueModel);
    }
}

public static class CombatSimulationObservationProjector
{
    public static CombatStateObservation Project(CombatSimulationPolicyContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var state = context.State;
        var player = state.Player ?? new CombatActorState();
        var observation = new CombatStateObservation
        {
            BattleSessionId = StableSessionId(context.Scenario.ScenarioId, context.Scenario.Seed),
            Sequence = state.ActionSequence,
            Player = ProjectActor(player, CombatTargetKind.Self),
            CurrentPower = player.Energy,
            MaxPower = player.BaseEnergy,
            HandCount = state.Hand.Count,
            IsPlayerActionWindow = state.Phase == CombatSimulationPhase.PlayerAction,
            UiBusy = false,
            Fingerprint = CombatBattleStateHasher.Hash(state),
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["turn"] = state.Turn,
                ["handLimit"] = context.Scenario.HandLimit,
                ["drawPile"] = state.DrawPile.Count,
                ["discardPile"] = state.DiscardPile.Count,
                ["exhaustPile"] = state.ExhaustPile.Count
            }
        };

        foreach (var enemy in state.LivingEnemies.OrderBy(enemy => enemy.ActorId))
        {
            observation.Enemies.Add(ProjectActor(enemy, CombatTargetKind.Enemy));
            AddThreat(context.Ruleset, enemy, observation.Threat);
        }
        observation.Threat.CurrentIntentKnown = observation.Enemies.Count > 0;
        observation.Threat.Confidence = observation.Threat.CurrentIntentKnown ? 1d : 0d;
        observation.ExpectedIncomingDamage =
            observation.Threat.ExpectedBlockableDamage
            + observation.Threat.ExpectedUnblockableDamage
            + observation.Threat.ExpectedDamageOverTime;

        foreach (var legal in context.LegalActions)
        {
            observation.Actions.Add(ProjectAction(context.Ruleset, state, legal));
        }
        return observation;
    }

    private static CombatActionObservation ProjectAction(
        CombatRuleset ruleset,
        CombatBattleState state,
        CombatSimulationAction action)
    {
        if (action.Kind == CombatSimulationActionKind.EndTurn)
        {
            return new CombatActionObservation
            {
                CandidateId = action.CandidateId,
                SourceId = "simulation:end-turn",
                DisplayName = "End Turn",
                Kind = CombatActionKind.EndTurn,
                RuntimeId = 0,
                Legal = true
            };
        }

        ruleset.TryGetCardCore(action.DefinitionId, out var definition);
        var semantics = definition == null
            ? new CombatActionSemantics { Uncertainty = 10d }
            : ProjectSemantics(definition, action);
        return new CombatActionObservation
        {
            CandidateId = action.CandidateId,
            SourceId = definition?.CardId ?? action.DefinitionId,
            DisplayName = definition?.DisplayName ?? action.DefinitionId,
            Kind = CombatActionKind.PlayCard,
            RuntimeId = action.CardInstanceId,
            TargetRuntimeId = action.TargetActorId,
            TargetKind = action.TargetActorId == 0
                ? CombatTargetKind.None
                : CombatTargetKind.Enemy,
            Cost = action.Cost,
            Legal = true,
            Semantics = semantics,
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["authoritativeSimulation"] = 1d,
                ["cardInstanceId"] = action.CardInstanceId,
                ["turn"] = state.Turn
            }
        };
    }

    private static CombatActionSemantics ProjectSemantics(
        CombatCardDefinition card,
        CombatSimulationAction action)
    {
        var semantics = new CombatActionSemantics();
        foreach (var effect in card.Effects)
        {
            var expected = effect.Amount * Math.Max(0d, Math.Min(1d, effect.Probability));
            switch (effect.Kind)
            {
                case CombatSimulationEffectKind.Damage:
                    semantics.Damage += expected;
                    break;
                case CombatSimulationEffectKind.TrueDamage:
                    semantics.TrueDamage += expected;
                    break;
                case CombatSimulationEffectKind.GainBlock:
                    semantics.Defend += expected;
                    break;
                case CombatSimulationEffectKind.Heal:
                    semantics.Heal += expected;
                    break;
                case CombatSimulationEffectKind.Draw:
                    semantics.Draw += expected;
                    break;
                case CombatSimulationEffectKind.GainEnergy:
                    semantics.EnergyGain += expected;
                    break;
                case CombatSimulationEffectKind.CreateCard:
                    semantics.CardGeneration += Math.Max(0d, effect.Probability);
                    break;
                case CombatSimulationEffectKind.ChangeCardCost:
                    semantics.CostReduction += Math.Max(0d, -expected);
                    break;
                case CombatSimulationEffectKind.SummonEnemy:
                    semantics.Risk += Math.Max(1d, expected);
                    break;
                case CombatSimulationEffectKind.Despawn:
                    semantics.TrueDamage += Math.Max(1d, expected);
                    break;
                case CombatSimulationEffectKind.AddStatus:
                    if (effect.Target == CombatSimulationTarget.SelectedEnemy
                        || effect.Target == CombatSimulationTarget.AllEnemies
                        || effect.Target == CombatSimulationTarget.RandomEnemy)
                    {
                        semantics.Debuff += Math.Max(1d, expected);
                    }
                    else
                    {
                        semantics.Buff += Math.Max(1d, expected);
                    }
                    break;
                case CombatSimulationEffectKind.RemoveStatus:
                    semantics.Cleanse += Math.Max(1d, expected);
                    break;
                case CombatSimulationEffectKind.DiscardRandom:
                case CombatSimulationEffectKind.ExhaustRandom:
                    semantics.Risk += expected;
                    break;
            }
            if (effect.Probability < 1d)
            {
                semantics.RandomOutcome = true;
                semantics.Uncertainty += 1d - Math.Max(0d, effect.Probability);
            }
        }
        return semantics;
    }

    private static CombatUnitObservation ProjectActor(
        CombatActorState actor,
        CombatTargetKind targetKind)
    {
        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in actor.Statuses)
        {
            features["status:" + status.StatusId] = status.Stacks;
        }
        return new CombatUnitObservation
        {
            RuntimeId = actor.ActorId,
            Name = actor.DisplayName,
            Kind = targetKind,
            CurrentHp = actor.Hp,
            MaxHp = actor.MaxHp,
            Defend = actor.Block,
            Features = features
        };
    }

    private static void AddThreat(
        CombatRuleset ruleset,
        CombatActorState enemy,
        CombatThreatForecast threat)
    {
        if (!ruleset.TryGetEnemyCore(enemy.DefinitionId, out var definition))
        {
            return;
        }
        var intent = definition.Intents.FirstOrDefault(candidate =>
            string.Equals(candidate.IntentId, enemy.CurrentIntentId, StringComparison.OrdinalIgnoreCase));
        if (intent == null)
        {
            return;
        }
        var item = new CombatIntentObservation
        {
            SourceRuntimeId = enemy.ActorId,
            SourceId = intent.IntentId,
            DisplayName = intent.DisplayName,
            Kind = CombatIntentKind.Unknown,
            Probability = 1d,
            Confidence = 1d,
            Current = true
        };
        foreach (var effect in intent.Effects)
        {
            var expected = effect.Amount * Math.Max(0d, Math.Min(1d, effect.Probability));
            if (effect.Kind == CombatSimulationEffectKind.Damage)
            {
                item.Kind = CombatIntentKind.Attack;
                item.BlockableDamage += expected;
            }
            else if (effect.Kind == CombatSimulationEffectKind.TrueDamage)
            {
                item.Kind = CombatIntentKind.Attack;
                item.UnblockableDamage += expected;
            }
        }
        threat.Intents.Add(item);
        threat.ExpectedBlockableDamage += item.BlockableDamage;
        threat.MaximumBlockableDamage += item.BlockableDamage;
        threat.ExpectedUnblockableDamage += item.UnblockableDamage;
        threat.AttackProbability = Math.Max(threat.AttackProbability, item.Probability);
    }

    private static long StableSessionId(string scenarioId, ulong seed)
    {
        unchecked
        {
            var hash = 1469598103934665603UL ^ seed;
            foreach (var character in scenarioId ?? "")
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return (long)(hash & long.MaxValue);
        }
    }
}
