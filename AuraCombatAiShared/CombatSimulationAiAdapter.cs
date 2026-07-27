using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatSelfPlayExplorationOptions
{
    public double Probability { get; set; }

    public double Temperature { get; set; } = 1d;

    public int RandomSeed { get; set; }
}

public sealed class CombatDecisionSimulationPolicy :
    ICombatSimulationPolicy,
    ICombatSimulationBorrowedStatePolicy,
    ICombatSimulationPolicyMetricsProvider
{
    private readonly CombatDecisionEngine decisionEngine;
    private readonly CombatDecisionProfile profile;
    private readonly CombatSelfPlayExplorationOptions? exploration;
    private readonly Random? explorationRandom;

    public CombatDecisionSimulationPolicy(
        CombatDecisionProfile? profile = null,
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        ICombatPolicyValueModel? policyValueModel = null,
        CombatSelfPlayExplorationOptions? exploration = null)
    {
        this.profile = profile ?? new CombatDecisionProfile();
        this.exploration = Normalize(exploration);
        explorationRandom = this.exploration == null
            ? null
            : new Random(this.exploration.RandomSeed);
        decisionEngine = new CombatDecisionEngine(
            residualModel,
            guidanceModel,
            useRuntimeRegistries: false,
            policyValueModel);
    }

    public string PolicyId => "aura-combat-decision:" + profile.Id;

    public CombatDecision? LastDecision { get; private set; }

    public CombatStateObservation? LastObservation { get; private set; }

    public CombatSimulationPolicyDecisionMetrics LastDecisionMetrics { get; } =
        new();

    public CombatSimulationAction? SelectAction(CombatSimulationPolicyContext context)
    {
        var observation = CombatSimulationObservationProjector.Project(context);
        var decision = decisionEngine.Choose(observation, profile);
        LastObservation = observation;
        LastDecision = decision;
        LastDecisionMetrics.SearchSimulations = decision.SearchSimulations;
        LastDecisionMetrics.SearchNodes = decision.SearchNodes;
        LastDecisionMetrics.SearchStoppedEarly = decision.SearchStoppedEarly;
        LastDecisionMetrics.SearchBudgetTier = decision.SearchBudgetTier;
        LastDecisionMetrics.CertifiedLoops = decision.CertifiedLoops;
        LastDecisionMetrics.SustainableControlLoops =
            decision.SustainableControlLoops;
        LastDecisionMetrics.FakeLoops = decision.FakeLoops;
        LastDecisionMetrics.BlockedLoops = decision.BlockedLoops;
        if (!decision.HasAction || decision.Action == null)
        {
            return context.LegalActions.FirstOrDefault(action =>
                action.Kind == CombatSimulationActionKind.EndTurn);
        }
        var selected = SelectExplorationAction(context, decision);
        return selected
               ?? context.LegalActions.FirstOrDefault(action =>
                   string.Equals(
                       action.CandidateId,
                       decision.Action.CandidateId,
                       StringComparison.Ordinal))
               ?? context.LegalActions.FirstOrDefault(action =>
                   action.Kind == CombatSimulationActionKind.EndTurn);
    }

    private CombatSimulationAction? SelectExplorationAction(
        CombatSimulationPolicyContext context,
        CombatDecision decision)
    {
        if (exploration == null
            || explorationRandom == null
            || explorationRandom.NextDouble() >= exploration.Probability)
        {
            return null;
        }
        var legal = (decision.Candidates ?? new List<CombatCandidateEvaluation>())
            .Where(candidate => candidate?.Action != null
                                && candidate.Legal
                                && context.LegalActions.Any(action =>
                                    string.Equals(
                                        action.CandidateId,
                                        candidate.Action.CandidateId,
                                        StringComparison.Ordinal)))
            .ToList();
        if (legal.Count <= 1)
        {
            return null;
        }
        var inverseTemperature = 1d / exploration.Temperature;
        var weights = legal
            .Select(candidate => Math.Pow(
                Math.Max(1d, candidate.SearchVisits),
                inverseTemperature))
            .ToArray();
        var total = weights.Sum();
        var sample = explorationRandom.NextDouble() * total;
        for (var index = 0; index < legal.Count; index++)
        {
            sample -= weights[index];
            if (sample <= 0d)
            {
                var candidateId = legal[index].Action.CandidateId;
                return context.LegalActions.First(action =>
                    string.Equals(
                        action.CandidateId,
                        candidateId,
                        StringComparison.Ordinal));
            }
        }
        return null;
    }

    private static CombatSelfPlayExplorationOptions? Normalize(
        CombatSelfPlayExplorationOptions? options)
    {
        if (options == null
            || double.IsNaN(options.Probability)
            || options.Probability <= 0d)
        {
            return null;
        }
        return new CombatSelfPlayExplorationOptions
        {
            Probability = Math.Min(1d, options.Probability),
            Temperature =
                double.IsNaN(options.Temperature)
                || double.IsInfinity(options.Temperature)
                    ? 1d
                    : Math.Max(0.1d, Math.Min(5d, options.Temperature)),
            RandomSeed = options.RandomSeed
        };
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
            HandCardIds = CardIds(state, state.Hand),
            RetainedHandCardIds = CardIds(state, state.Hand)
                .Where(cardId => context.Ruleset.TryGetCard(cardId, out var card)
                                 && HasTag(card, "Retain"))
                .ToList(),
            DeckCardIds = CardIds(
                state,
                state.DrawPile
                    .Concat(state.Hand)
                    .Concat(state.DiscardPile)
                    .ToList()),
            DrawPileCardIds = CardIds(state, state.DrawPile),
            DiscardPileCardIds = CardIds(state, state.DiscardPile),
            ExhaustPileCardIds = CardIds(state, state.ExhaustPile),
            IsPlayerActionWindow = state.Phase == CombatSimulationPhase.PlayerAction,
            UiBusy = false,
            Fingerprint = CombatBattleStateHasher.Hash(state),
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["turn"] = state.Turn,
                ["handLimit"] = context.Scenario.HandLimit,
                ["drawPile"] = state.DrawPile.Count,
                ["drawPileCount"] = state.DrawPile.Count,
                ["discardPile"] = state.DiscardPile.Count,
                ["discardPileCount"] = state.DiscardPile.Count,
                ["exhaustPile"] = state.ExhaustPile.Count,
                ["exhaustPileCount"] = state.ExhaustPile.Count,
                ["drawPerTurn"] = context.Scenario.DrawPerTurn
            }
        };
        foreach (var variable in player.Variables)
        {
            observation.Features["player." + variable.Key] = variable.Value;
        }

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

    private static List<string> CardIds(
        CombatBattleState state,
        IEnumerable<int> instanceIds)
    {
        return instanceIds
            .Select(state.FindCard)
            .Where(card => card != null)
            .Select(card => card!.CardId)
            .ToList();
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
            : ProjectSemantics(ruleset, state, definition, action);
        var instance = state.FindCard(action.CardInstanceId);
        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["authoritativeSimulation"] = 1d,
            ["cardInstanceId"] = action.CardInstanceId,
            ["turn"] = state.Turn,
            ["visibleFake"] = instance?.IsVisibleFake == true ? 1d : 0d,
            ["hasVisibleWarning"] = instance?.EnchantmentIds.Count > 0 ? 1d : 0d,
            ["retain"] = definition != null && HasTag(definition, "Retain") ? 1d : 0d,
            ["inherent"] = definition != null && HasTag(definition, "Inherent") ? 1d : 0d,
            ["recycle"] = definition != null && HasTag(definition, "Recycle") ? 1d : 0d,
            ["ouroboros"] = definition != null && HasTag(definition, "Ouroboros") ? 1d : 0d,
            ["exhaustOnUse"] = definition?.Exhaust == true
                               || definition != null
                               && (HasTag(definition, "Burnout")
                                   || HasTag(definition, "Fragmented")
                                   || HasTag(definition, "Exhaust"))
                ? 1d
                : 0d
        };
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
            Features = features
        };
    }

    private static bool HasTag(CombatCardDefinition card, string tag)
    {
        return card.Tags.Any(value =>
            string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
    }

    private static CombatActionSemantics ProjectSemantics(
        CombatRuleset ruleset,
        CombatBattleState state,
        CombatCardDefinition card,
        CombatSimulationAction action)
    {
        var semantics = new CombatActionSemantics();
        foreach (var effect in card.Effects)
        {
            var targetActorId = effect.Target == CombatSimulationTarget.Self
                || effect.Target == CombatSimulationTarget.Player
                ? state.PlayerActorId
                : action.TargetActorId;
            if (effect.ConditionExpression != null
                && CombatSimulationExpressionEvaluator.Evaluate(
                    effect.ConditionExpression,
                    state,
                    ruleset,
                    state.PlayerActorId,
                    targetActorId) <= 0d)
            {
                continue;
            }
            var amount = effect.AmountExpression == null
                ? effect.Amount
                : RoundEffectValue(
                    CombatSimulationExpressionEvaluator.Evaluate(
                        effect.AmountExpression,
                        state,
                        ruleset,
                        state.PlayerActorId,
                        targetActorId),
                    effect.Rounding);
            var expected = amount * Math.Max(0d, Math.Min(1d, effect.Probability));
            switch (effect.Kind)
            {
                case CombatSimulationEffectKind.Damage:
                    semantics.Damage += expected;
                    break;
                case CombatSimulationEffectKind.TrueDamage:
                    semantics.TrueDamage += expected;
                    break;
                case CombatSimulationEffectKind.DirectHpLoss:
                    if (effect.Target == CombatSimulationTarget.Self
                        || effect.Target == CombatSimulationTarget.Player)
                    {
                        semantics.SelfHpLoss += expected;
                        semantics.Risk += expected;
                    }
                    else
                    {
                        semantics.TrueDamage += expected;
                    }
                    break;
                case CombatSimulationEffectKind.GainBlock:
                    semantics.Defend += expected;
                    break;
                case CombatSimulationEffectKind.Heal:
                    semantics.Heal += expected;
                    break;
                case CombatSimulationEffectKind.SetHp:
                {
                    var currentHp = state.Player?.Hp ?? 0;
                    var hpDelta = expected - currentHp;
                    semantics.StateChanges["player.hp"] = hpDelta;
                    if (hpDelta < 0d)
                    {
                        semantics.Risk += -hpDelta;
                    }
                    else
                    {
                        semantics.Heal += hpDelta;
                    }
                    break;
                }
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
                case CombatSimulationEffectKind.ModifyVariable:
                    if (effect.Target == CombatSimulationTarget.Self
                        || effect.Target == CombatSimulationTarget.Player)
                    {
                        var key = "player." + effect.DefinitionId;
                        var current = state.Player?.Variables.TryGetValue(
                            effect.DefinitionId,
                            out var value) == true
                            ? value
                            : 0d;
                        var after = Math.Max(
                            effect.MinimumVariableValue,
                            Math.Min(effect.MaximumVariableValue, current + amount));
                        semantics.StateChanges[key] =
                            semantics.StateChanges.TryGetValue(key, out var delta)
                                ? delta + after - current
                                : after - current;
                    }
                    break;
                case CombatSimulationEffectKind.SummonEnemy:
                    semantics.Risk += Math.Max(1d, expected);
                    break;
                case CombatSimulationEffectKind.Despawn:
                    semantics.TrueDamage += Math.Max(1d, expected);
                    break;
                case CombatSimulationEffectKind.AddStatus:
                    var marginalStatusStacks = MarginalStatusStacks(
                        ruleset,
                        state,
                        effect,
                        targetActorId,
                        amount);
                    if (marginalStatusStacks <= 0d)
                    {
                        break;
                    }
                    if (effect.Target == CombatSimulationTarget.SelectedEnemy
                        || effect.Target == CombatSimulationTarget.AllEnemies
                        || effect.Target == CombatSimulationTarget.RandomEnemy)
                    {
                        semantics.Debuff += marginalStatusStacks;
                    }
                    else
                    {
                        semantics.Buff += marginalStatusStacks;
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

    private static double MarginalStatusStacks(
        CombatRuleset ruleset,
        CombatBattleState state,
        CombatSimulationEffectDefinition effect,
        int targetActorId,
        int amount)
    {
        if (!ruleset.TryGetStatus(effect.DefinitionId, out var definition))
        {
            return Math.Max(1d, amount)
                   * Math.Max(0d, Math.Min(1d, effect.Probability));
        }
        IEnumerable<CombatActorState> targets = effect.Target switch
        {
            CombatSimulationTarget.AllEnemies => state.LivingEnemies,
            CombatSimulationTarget.AllAllies => state.Actors.Where(actor =>
                actor.Alive
                && actor.Kind != CombatSimulationActorKind.Enemy),
            _ => state.FindActor(targetActorId) is { } target
                ? new[] { target }
                : Array.Empty<CombatActorState>()
        };
        var requested = Math.Max(1, amount);
        var maximum = Math.Max(1, definition.MaximumStacks);
        var marginal = targets.Sum(actor =>
        {
            var current = actor.Statuses.FirstOrDefault(status =>
                string.Equals(
                    status.StatusId,
                    effect.DefinitionId,
                    StringComparison.OrdinalIgnoreCase))?.Stacks ?? 0;
            return Math.Max(0, Math.Min(maximum, current + requested) - current);
        });
        return marginal * Math.Max(0d, Math.Min(1d, effect.Probability));
    }

    private static int RoundEffectValue(
        double value,
        CombatSimulationValueRounding rounding)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= int.MaxValue) return int.MaxValue;
        if (value <= int.MinValue) return int.MinValue;
        return rounding switch
        {
            CombatSimulationValueRounding.Truncate => (int)value,
            CombatSimulationValueRounding.Floor => (int)Math.Floor(value),
            CombatSimulationValueRounding.Ceiling => (int)Math.Ceiling(value),
            _ => (int)Math.Round(value)
        };
    }

    private static CombatUnitObservation ProjectActor(
        CombatActorState actor,
        CombatTargetKind targetKind)
    {
        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in actor.Statuses)
        {
            features["status:" + status.StatusId] = status.Stacks;
            AddMechanicFeatures(features, status.StatusId, status.Stacks);
        }
        foreach (var variable in actor.Variables)
        {
            features[variable.Key] = variable.Value;
        }
        return new CombatUnitObservation
        {
            RuntimeId = actor.ActorId,
            DefinitionId = actor.DefinitionId,
            Name = actor.DisplayName,
            Kind = targetKind,
            CurrentHp = actor.Hp,
            MaxHp = actor.MaxHp,
            Defend = actor.Block,
            Features = features
        };
    }

    private static void AddMechanicFeatures(
        IDictionary<string, double> features,
        string statusId,
        int stacks)
    {
        var id = statusId ?? "";
        if (id.IndexOf(
                "limitdamage",
                StringComparison.OrdinalIgnoreCase) >= 0)
        {
            features["damageLimitActive"] = 1d;
            features["damageLimitLevel"] = Math.Max(0, stacks);
        }
        if (id.IndexOf("frenzy", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("keenedge", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("counterattack", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("thorns", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            features["escalationPressure"] =
                features.TryGetValue("escalationPressure", out var current)
                    ? current + Math.Max(1, stacks)
                    : Math.Max(1, stacks);
        }
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
        var intentIds = enemy.CurrentIntentIds.Count > 0
            ? enemy.CurrentIntentIds
            : string.IsNullOrWhiteSpace(enemy.CurrentIntentId)
                ? new List<string>()
                : new List<string> { enemy.CurrentIntentId };
        foreach (var intentId in intentIds)
        {
            var intent = definition.Intents.FirstOrDefault(candidate =>
                string.Equals(candidate.IntentId, intentId, StringComparison.OrdinalIgnoreCase));
            if (intent == null)
            {
                continue;
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
