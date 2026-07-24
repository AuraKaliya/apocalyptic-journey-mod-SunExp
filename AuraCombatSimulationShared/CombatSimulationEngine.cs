using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AuraCombatSimulation.Shared;

public sealed class CombatSimulationEngine
{
    public CombatSimulationResult Run(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        ICombatSimulationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (policy == null) throw new ArgumentNullException(nameof(policy));

        var session = new Session(scenario, ruleset, policy);
        try
        {
            if (!session.Initialize())
            {
                return session.CompleteResult();
            }
            while (session.State.Outcome == CombatSimulationOutcome.None)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    session.Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.Cancelled);
                    break;
                }
                if (!session.RunTurn())
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            session.AddUnsupported("engine-error:" + ex.GetType().Name + ":" + ex.Message);
            session.Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.EngineError);
        }
        return session.CompleteResult();
    }

    public IReadOnlyList<CombatSimulationAction> GetLegalPlayerActions(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        CombatBattleState state)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (state == null) throw new ArgumentNullException(nameof(state));
        return Session.BuildLegalActions(scenario, ruleset, state);
    }

    public CombatActionApplicationResult ForkAndApplyPlayerAction(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        CombatBattleState source,
        CombatSimulationAction action)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (action == null) throw new ArgumentNullException(nameof(action));

        var session = new Session(scenario, ruleset, FirstLegalCombatSimulationPolicy.Instance, source.Clone());
        var legal = Session.BuildLegalActions(scenario, ruleset, session.State);
        var selected = legal.FirstOrDefault(candidate =>
            string.Equals(candidate.CandidateId, action.CandidateId, StringComparison.Ordinal));
        if (selected == null || selected.Kind == CombatSimulationActionKind.EndTurn)
        {
            return new CombatActionApplicationResult
            {
                Reason = "action is not a legal playable card",
                State = source.Clone()
            };
        }

        var success = session.ApplyPlayerAction(selected);
        return new CombatActionApplicationResult
        {
            Success = success,
            Reason = success ? "" : session.State.TerminationReason.ToString(),
            State = session.State.Clone(),
            Events = new List<CombatSimulationEvent>(session.Events)
        };
    }

    private sealed class Session
    {
        private readonly CombatScenarioDefinition scenario;
        private readonly CombatRuleset ruleset;
        private readonly ICombatSimulationPolicy policy;
        private readonly CombatSimulationLimits limits;
        private readonly HashSet<string> unsupported = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> referencedDefinitions = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> authoritativeDefinitions = new(StringComparer.OrdinalIgnoreCase);
        private readonly CombatSimulationMetrics metrics = new();
        private readonly List<CombatTurnSummary> turnSummaries = new();
        private int currentActionCommandCount;
        private CombatSimulationEvent? secondaryCommandEvent;

        public Session(
            CombatScenarioDefinition scenario,
            CombatRuleset ruleset,
            ICombatSimulationPolicy policy,
            CombatBattleState? initialState = null)
        {
            this.scenario = scenario;
            this.ruleset = ruleset;
            this.policy = policy;
            limits = (scenario.Limits ?? new CombatSimulationLimits()).Normalize();
            State = initialState ?? new CombatBattleState();
        }

        public CombatBattleState State { get; }

        public List<CombatSimulationEvent> Events { get; } = new();

        public bool Initialize()
        {
            if (string.IsNullOrWhiteSpace(scenario.ScenarioId)
                || scenario.Player == null
                || scenario.Player.MaxHp <= 0
                || scenario.Player.CurrentHp <= 0
                || !string.Equals(
                    scenario.RulesetVersion,
                    ruleset.Version,
                    StringComparison.Ordinal)
                || scenario.Player.Deck == null
                || scenario.Player.Deck.Count == 0
                || scenario.Enemies == null
                || scenario.Enemies.Count == 0)
            {
                AddUnsupported("invalid-scenario");
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.InvalidScenario);
                return false;
            }

            State.Phase = CombatSimulationPhase.Initialize;
            var player = new CombatActorState
            {
                ActorId = State.NextActorId++,
                InstanceKey = "player",
                DefinitionId = scenario.Player.RoleId,
                DisplayName = scenario.Player.RoleId,
                Kind = CombatSimulationActorKind.Player,
                Hp = Math.Min(scenario.Player.MaxHp, scenario.Player.CurrentHp),
                MaxHp = scenario.Player.MaxHp,
                BaseEnergy = Math.Max(0, scenario.Player.BaseEnergy)
            };
            State.PlayerActorId = player.ActorId;
            State.Actors.Add(player);
            AddInitialStatuses(player, scenario.Player.InitialStatuses);

            for (var i = 0; i < scenario.Enemies.Count; i++)
            {
                var setup = scenario.Enemies[i];
                if (!ruleset.TryGetEnemyCore(setup.EnemyId, out var definition))
                {
                    AddUnsupported("enemy:" + setup.EnemyId);
                    continue;
                }
                Reference("enemy:" + definition.EnemyId, definition.Fidelity);
                var maximumHp = Math.Max(1, (int)Math.Round(definition.MaxHp * Math.Max(0.01d, setup.HpScale)));
                var actor = new CombatActorState
                {
                    ActorId = State.NextActorId++,
                    InstanceKey = string.IsNullOrWhiteSpace(setup.InstanceKey)
                        ? setup.EnemyId + ":" + i
                        : setup.InstanceKey,
                    DefinitionId = definition.EnemyId,
                    DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                        ? definition.EnemyId
                        : definition.DisplayName,
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = maximumHp,
                    MaxHp = maximumHp,
                    Block = Math.Max(0, definition.InitialBlock)
                };
                State.Actors.Add(actor);
                AddInitialStatuses(actor, setup.InitialStatuses);
            }

            foreach (var cardId in scenario.Player.Deck)
            {
                if (!ruleset.TryGetCardCore(cardId, out var definition))
                {
                    AddUnsupported("card:" + cardId);
                    continue;
                }
                Reference("card:" + definition.CardId, definition.Fidelity);
                var instance = new CombatCardInstanceState
                {
                    InstanceId = State.NextCardInstanceId++,
                    CardId = definition.CardId
                };
                State.Cards.Add(instance);
                State.DrawPile.Add(instance.InstanceId);
            }

            if (unsupported.Count > 0)
            {
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                return false;
            }

            var shuffle = CombatDeterministicRandom.Shuffle(
                scenario.Seed,
                State.Random,
                "deck.shuffle.initial",
                State.DrawPile);
            TraceRandomDraws(shuffle, "initial shuffle");
            State.Phase = CombatSimulationPhase.BattleStart;
            return ProcessLifecycleEvent(
                CombatSimulationEventKind.BattleStarted,
                State.PlayerActorId,
                0,
                scenario.ScenarioId,
                0);
        }

        public bool RunTurn()
        {
            if (State.Turn >= limits.MaximumTurns)
            {
                Terminate(CombatSimulationOutcome.Draw, CombatTerminationReason.MaximumTurns);
                return false;
            }

            State.Turn++;
            State.Phase = CombatSimulationPhase.PlayerTurnStart;
            var player = State.Player;
            if (player == null || !player.Alive)
            {
                Terminate(CombatSimulationOutcome.Defeat, CombatTerminationReason.Defeat);
                return false;
            }
            if (!scenario.RetainBlockBetweenTurns)
            {
                player.Block = 0;
            }
            player.Energy = player.BaseEnergy;
            SelectEnemyIntents();
            var summary = new CombatTurnSummary
            {
                Turn = State.Turn,
                PlayerHpAtStart = player.Hp,
                EnemyHpAtStart = State.LivingEnemies.Sum(enemy => enemy.Hp),
                StartStateHash = CombatBattleStateHasher.Hash(State)
            };
            if (!ProcessLifecycleEvent(
                    CombatSimulationEventKind.TurnStarted,
                    player.ActorId,
                    player.ActorId,
                    "",
                    State.Turn))
            {
                FinishSummary(summary);
                return false;
            }
            if (State.Outcome != CombatSimulationOutcome.None)
            {
                FinishSummary(summary);
                return false;
            }
            DrawCards(State.Turn == 1 ? scenario.InitialDraw : scenario.DrawPerTurn, player.ActorId, 0);

            State.Phase = CombatSimulationPhase.PlayerAction;
            while (State.Outcome == CombatSimulationOutcome.None)
            {
                if (State.ActionSequence >= limits.MaximumActions)
                {
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.MaximumActions);
                    return false;
                }

                var legal = BuildLegalActions(scenario, ruleset, State);
                var context = new CombatSimulationPolicyContext
                {
                    Scenario = scenario,
                    Ruleset = ruleset,
                    State = State.Clone(),
                    LegalActions = legal
                };
                var requested = policy.SelectAction(context);
                var selected = requested == null
                    ? null
                    : legal.FirstOrDefault(candidate =>
                        string.Equals(candidate.CandidateId, requested.CandidateId, StringComparison.Ordinal));
                if (selected == null)
                {
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.IllegalPolicyAction);
                    return false;
                }
                if (selected.Kind == CombatSimulationActionKind.EndTurn)
                {
                    break;
                }

                summary.Actions++;
                if (!ApplyPlayerAction(selected))
                {
                    return false;
                }
            }

            if (State.Outcome != CombatSimulationOutcome.None)
            {
                FinishSummary(summary);
                return false;
            }

            State.Phase = CombatSimulationPhase.PlayerTurnEnd;
            if (!ProcessLifecycleEvent(
                    CombatSimulationEventKind.TurnEnded,
                    player.ActorId,
                    player.ActorId,
                    "",
                    State.Turn))
            {
                FinishSummary(summary);
                return false;
            }
            if (State.Outcome != CombatSimulationOutcome.None)
            {
                FinishSummary(summary);
                return false;
            }
            DiscardHand(player.ActorId);
            State.Phase = CombatSimulationPhase.EnemyAction;
            foreach (var enemy in State.LivingEnemies.OrderBy(actor => actor.ActorId).ToList())
            {
                enemy.Block = 0;
                if (!ExecuteEnemyIntent(enemy))
                {
                    FinishSummary(summary);
                    return false;
                }
                if (State.Outcome != CombatSimulationOutcome.None)
                {
                    FinishSummary(summary);
                    return false;
                }
            }

            State.Phase = CombatSimulationPhase.RoundEnd;
            DecayStatuses();
            FinishSummary(summary);
            return State.Outcome == CombatSimulationOutcome.None;
        }

        public bool ApplyPlayerAction(CombatSimulationAction action)
        {
            var player = State.Player;
            var instance = State.FindCard(action.CardInstanceId);
            if (player == null
                || instance == null
                || !State.Hand.Contains(instance.InstanceId)
                || !ruleset.TryGetCardCore(instance.CardId, out var definition))
            {
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.IllegalPolicyAction);
                return false;
            }

            var cost = Math.Max(0, definition.Cost + instance.CostModifier);
            if (player.Energy < cost)
            {
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.IllegalPolicyAction);
                return false;
            }

            State.ActionSequence++;
            currentActionCommandCount = 0;
            player.Energy -= cost;
            metrics.EnergySpent += cost;
            metrics.CardsPlayed++;
            metrics.CardPlayCounts[definition.CardId] =
                metrics.CardPlayCounts.TryGetValue(definition.CardId, out var count) ? count + 1 : 1;
            var played = Emit(
                CombatSimulationEventKind.CardPlayed,
                player.ActorId,
                action.TargetActorId,
                instance.InstanceId,
                definition.CardId,
                cost);
            var queue = new Queue<CombatSimulationCommand>();
            CompileEffects(
                definition.Effects,
                player.ActorId,
                action.TargetActorId,
                instance.InstanceId,
                played,
                null,
                0,
                queue);
            EnqueueTriggers(played, queue, 1);
            if (!ExecuteQueue(queue))
            {
                return false;
            }

            State.Hand.Remove(instance.InstanceId);
            if (definition.Exhaust)
            {
                State.ExhaustPile.Add(instance.InstanceId);
                Emit(
                    CombatSimulationEventKind.CardExhausted,
                    player.ActorId,
                    player.ActorId,
                    instance.InstanceId,
                    definition.CardId,
                    1,
                    played.Sequence);
            }
            else
            {
                State.DiscardPile.Add(instance.InstanceId);
                Emit(
                    CombatSimulationEventKind.CardDiscarded,
                    player.ActorId,
                    player.ActorId,
                    instance.InstanceId,
                    definition.CardId,
                    1,
                    played.Sequence);
            }
            return ValidateState();
        }

        public CombatSimulationResult CompleteResult()
        {
            var player = State.Player;
            return new CombatSimulationResult
            {
                ScenarioId = scenario.ScenarioId,
                Seed = scenario.Seed,
                RulesetHash = ruleset.RulesetHash,
                PolicyId = policy.PolicyId,
                Outcome = State.Outcome,
                TerminationReason = State.TerminationReason,
                Turns = State.Turn,
                FinalPlayerHp = player?.Hp ?? 0,
                FinalStateHash = CombatBattleStateHasher.Hash(State),
                SemanticCoverage = referencedDefinitions.Count <= 0
                    ? 0d
                    : (double)authoritativeDefinitions.Count / referencedDefinitions.Count,
                UnsupportedDefinitions = unsupported.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                Metrics = metrics,
                TurnsSummary = turnSummaries,
                Events = Events,
                FinalState = State.Clone()
            };
        }

        public void AddUnsupported(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                unsupported.Add(value);
            }
        }

        public void Terminate(
            CombatSimulationOutcome outcome,
            CombatTerminationReason reason)
        {
            if (State.Outcome != CombatSimulationOutcome.None)
            {
                return;
            }
            State.Outcome = outcome;
            State.TerminationReason = reason;
            State.Phase = CombatSimulationPhase.Completed;
            Emit(
                CombatSimulationEventKind.BattleEnded,
                State.PlayerActorId,
                0,
                0,
                reason.ToString(),
                (int)outcome);
        }

        public static IReadOnlyList<CombatSimulationAction> BuildLegalActions(
            CombatScenarioDefinition scenario,
            CombatRuleset ruleset,
            CombatBattleState state)
        {
            var result = new List<CombatSimulationAction>();
            var player = state.Player;
            if (player == null || !player.Alive)
            {
                return result;
            }
            var enemies = state.LivingEnemies.OrderBy(enemy => enemy.ActorId).ToList();
            foreach (var instanceId in state.Hand)
            {
                var instance = state.FindCard(instanceId);
                if (instance == null || !ruleset.TryGetCardCore(instance.CardId, out var definition))
                {
                    continue;
                }
                var cost = Math.Max(0, definition.Cost + instance.CostModifier);
                if (cost > player.Energy)
                {
                    continue;
                }
                if (definition.RequiresEnemyTarget)
                {
                    foreach (var enemy in enemies)
                    {
                        result.Add(new CombatSimulationAction
                        {
                            CandidateId = "card:" + instance.InstanceId + ":target:" + enemy.ActorId,
                            Kind = CombatSimulationActionKind.PlayCard,
                            ActorId = player.ActorId,
                            CardInstanceId = instance.InstanceId,
                            TargetActorId = enemy.ActorId,
                            Cost = cost,
                            DefinitionId = definition.CardId
                        });
                    }
                }
                else
                {
                    result.Add(new CombatSimulationAction
                    {
                        CandidateId = "card:" + instance.InstanceId,
                        Kind = CombatSimulationActionKind.PlayCard,
                        ActorId = player.ActorId,
                        CardInstanceId = instance.InstanceId,
                        Cost = cost,
                        DefinitionId = definition.CardId
                    });
                }
            }
            result.Add(new CombatSimulationAction
            {
                CandidateId = "end-turn",
                Kind = CombatSimulationActionKind.EndTurn,
                ActorId = player.ActorId
            });
            return result;
        }

        private void AddInitialStatuses(
            CombatActorState actor,
            IEnumerable<CombatInitialStatus>? initialStatuses)
        {
            foreach (var initial in initialStatuses ?? Array.Empty<CombatInitialStatus>())
            {
                if (!ruleset.TryGetStatusCore(initial.StatusId, out var definition))
                {
                    AddUnsupported("status:" + initial.StatusId);
                    continue;
                }
                Reference("status:" + definition.StatusId, definition.Fidelity);
                actor.Statuses.Add(new CombatStatusState
                {
                    StatusId = definition.StatusId,
                    Stacks = Math.Max(1, initial.Stacks),
                    Duration = Math.Max(0, initial.Duration),
                    SourceActorId = actor.ActorId
                });
            }
        }

        private void Reference(string definitionId, CombatRuleFidelity fidelity)
        {
            referencedDefinitions.Add(definitionId);
            if (fidelity == CombatRuleFidelity.Authoritative)
            {
                authoritativeDefinitions.Add(definitionId);
                return;
            }
            if (fidelity == CombatRuleFidelity.Unsupported
                || scenario.RequireAuthoritativeRules)
            {
                AddUnsupported(definitionId + ":" + fidelity);
            }
        }

        private void SelectEnemyIntents()
        {
            State.Phase = CombatSimulationPhase.EnemyIntent;
            foreach (var enemy in State.LivingEnemies.OrderBy(actor => actor.ActorId))
            {
                if (!ruleset.TryGetEnemyCore(enemy.DefinitionId, out var definition))
                {
                    AddUnsupported("enemy:" + enemy.DefinitionId);
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                    return;
                }
                var ratio = enemy.MaxHp <= 0 ? 0d : (double)enemy.Hp / enemy.MaxHp;
                var eligible = definition.Intents
                    .Where(intent => State.Turn >= intent.MinimumTurn
                                     && State.Turn <= intent.MaximumTurn
                                     && ratio >= intent.MinimumHpRatio
                                     && ratio <= intent.MaximumHpRatio)
                    .ToList();
                var withoutRepeat = eligible
                    .Where(intent => !intent.PreventConsecutiveUse
                                     || !string.Equals(
                                         intent.IntentId,
                                         enemy.PreviousIntentId,
                                         StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (withoutRepeat.Count > 0)
                {
                    eligible = withoutRepeat;
                }
                var totalWeight = eligible.Sum(intent => Math.Max(0, intent.Weight));
                if (eligible.Count == 0 || totalWeight <= 0)
                {
                    AddUnsupported("enemy-intent:" + enemy.DefinitionId);
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                    return;
                }
                var selectedValue = CombatDeterministicRandom.NextInt(
                    scenario.Seed,
                    State.Random,
                    "enemy.intent:" + enemy.InstanceKey,
                    totalWeight,
                    out var draw);
                var selected = eligible[0];
                var cursor = 0;
                foreach (var intent in eligible)
                {
                    cursor += Math.Max(0, intent.Weight);
                    if (selectedValue < cursor)
                    {
                        selected = intent;
                        break;
                    }
                }
                enemy.PreviousIntentId = enemy.CurrentIntentId;
                enemy.CurrentIntentId = selected.IntentId;
                Emit(
                    CombatSimulationEventKind.IntentSelected,
                    enemy.ActorId,
                    State.PlayerActorId,
                    0,
                    selected.IntentId,
                    selectedValue,
                    0,
                    draw);
            }
            State.Phase = CombatSimulationPhase.PlayerTurnStart;
        }

        private bool ExecuteEnemyIntent(CombatActorState enemy)
        {
            if (!ruleset.TryGetEnemyCore(enemy.DefinitionId, out var definition))
            {
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                return false;
            }
            var intent = definition.Intents.FirstOrDefault(candidate =>
                string.Equals(candidate.IntentId, enemy.CurrentIntentId, StringComparison.OrdinalIgnoreCase));
            if (intent == null)
            {
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                return false;
            }

            State.ActionSequence++;
            currentActionCommandCount = 0;
            var queue = new Queue<CombatSimulationCommand>();
            CompileEffects(
                intent.Effects,
                enemy.ActorId,
                State.PlayerActorId,
                0,
                null,
                null,
                0,
                queue);
            return ExecuteQueue(queue);
        }

        private void CompileEffects(
            IEnumerable<CombatSimulationEffectDefinition> effects,
            int sourceActorId,
            int selectedTargetId,
            int cardInstanceId,
            CombatSimulationEvent? parent,
            CombatSimulationEvent? triggerEvent,
            int triggerWave,
            Queue<CombatSimulationCommand> queue,
            int statusStacks = 1)
        {
            foreach (var effect in effects)
            {
                CombatRandomDraw? chanceDraw = null;
                if (effect.Probability < 1d)
                {
                    var roll = CombatDeterministicRandom.NextUnit(
                        scenario.Seed,
                        State.Random,
                        "effect.proc:" + sourceActorId + ":" + effect.Kind + ":" + effect.DefinitionId,
                        out var draw);
                    chanceDraw = draw;
                    TraceRandomDraw(draw, "effect probability");
                    if (roll >= Math.Max(0d, effect.Probability))
                    {
                        continue;
                    }
                }
                var targets = ResolveTargets(
                    effect.Target,
                    sourceActorId,
                    selectedTargetId,
                    triggerEvent,
                    out var targetDraw);
                if (targetDraw != null)
                {
                    TraceRandomDraw(targetDraw, "target selection");
                }
                if (effect.Kind == CombatSimulationEffectKind.Draw
                    || effect.Kind == CombatSimulationEffectKind.DiscardRandom
                    || effect.Kind == CombatSimulationEffectKind.ExhaustRandom
                    || effect.Kind == CombatSimulationEffectKind.GainEnergy
                    || effect.Kind == CombatSimulationEffectKind.CreateCard)
                {
                    if (targets.Count == 0)
                    {
                        targets.Add(sourceActorId);
                    }
                }
                foreach (var targetId in targets)
                {
                    queue.Enqueue(new CombatSimulationCommand
                    {
                        Kind = effect.Kind,
                        SourceActorId = sourceActorId,
                        TargetActorId = targetId,
                        CardInstanceId = cardInstanceId,
                        Amount = effect.Kind == CombatSimulationEffectKind.ChangeCardCost
                            ? effect.Amount
                              * (effect.ScaleWithStatusStacks ? Math.Max(1, statusStacks) : 1)
                            : Math.Max(
                                0,
                                effect.Amount
                                * (effect.ScaleWithStatusStacks ? Math.Max(1, statusStacks) : 1)),
                        DefinitionId = effect.DefinitionId ?? "",
                        Duration = Math.Max(0, effect.Duration),
                        ParentSequence = parent?.Sequence ?? triggerEvent?.Sequence ?? 0,
                        TriggerWave = triggerWave,
                        RandomStreamId = chanceDraw?.StreamId ?? targetDraw?.StreamId ?? "",
                        RandomCounter = chanceDraw?.Counter ?? targetDraw?.Counter ?? 0,
                        RandomValue = chanceDraw?.Value ?? targetDraw?.Value ?? 0
                    });
                }
            }
        }

        private List<int> ResolveTargets(
            CombatSimulationTarget target,
            int sourceActorId,
            int selectedTargetId,
            CombatSimulationEvent? triggerEvent,
            out CombatRandomDraw? randomDraw)
        {
            randomDraw = null;
            switch (target)
            {
                case CombatSimulationTarget.Self:
                    return new List<int> { sourceActorId };
                case CombatSimulationTarget.Player:
                    return new List<int> { State.PlayerActorId };
                case CombatSimulationTarget.SelectedEnemy:
                    return State.FindActor(selectedTargetId)?.Alive == true
                        ? new List<int> { selectedTargetId }
                        : new List<int>();
                case CombatSimulationTarget.AllEnemies:
                    return State.LivingEnemies.OrderBy(enemy => enemy.ActorId)
                        .Select(enemy => enemy.ActorId).ToList();
                case CombatSimulationTarget.RandomEnemy:
                {
                    var enemies = State.LivingEnemies.OrderBy(enemy => enemy.ActorId).ToList();
                    if (enemies.Count == 0)
                    {
                        return new List<int>();
                    }
                    var index = CombatDeterministicRandom.NextInt(
                        scenario.Seed,
                        State.Random,
                        "target.enemy:" + sourceActorId,
                        enemies.Count,
                        out var draw);
                    randomDraw = draw;
                    return new List<int> { enemies[index].ActorId };
                }
                case CombatSimulationTarget.EventSource:
                    return triggerEvent?.SourceActorId > 0
                        ? new List<int> { triggerEvent.SourceActorId }
                        : new List<int>();
                case CombatSimulationTarget.EventTarget:
                    return triggerEvent?.TargetActorId > 0
                        ? new List<int> { triggerEvent.TargetActorId }
                        : new List<int>();
                case CombatSimulationTarget.None:
                default:
                    return selectedTargetId > 0
                        ? new List<int> { selectedTargetId }
                        : new List<int> { sourceActorId };
            }
        }

        private bool ExecuteQueue(Queue<CombatSimulationCommand> queue)
        {
            while (queue.Count > 0 && State.Outcome == CombatSimulationOutcome.None)
            {
                if (State.CommandCount >= limits.MaximumCommands)
                {
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.MaximumCommands);
                    return false;
                }
                if (++currentActionCommandCount > limits.MaximumCommandsPerAction)
                {
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.MaximumCommands);
                    return false;
                }
                var command = queue.Dequeue();
                if (command.TriggerWave > limits.MaximumTriggerWavesPerAction)
                {
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.TriggerLoop);
                    return false;
                }
                State.CommandCount++;
                secondaryCommandEvent = null;
                var emitted = ExecuteCommand(command);
                if (emitted != null)
                {
                    EnqueueTriggers(emitted, queue, command.TriggerWave + 1);
                }
                if (secondaryCommandEvent != null)
                {
                    EnqueueTriggers(secondaryCommandEvent, queue, command.TriggerWave + 1);
                }
                CheckOutcome();
            }
            return State.Outcome == CombatSimulationOutcome.None
                   || State.Outcome == CombatSimulationOutcome.Victory;
        }

        private CombatSimulationEvent? ExecuteCommand(CombatSimulationCommand command)
        {
            var beforeHash = scenario.TraceLevel == CombatSimulationTraceLevel.Full
                ? CombatBattleStateHasher.Hash(State)
                : "";
            var source = State.FindActor(command.SourceActorId);
            var target = State.FindActor(command.TargetActorId);
            switch (command.Kind)
            {
                case CombatSimulationEffectKind.Damage:
                case CombatSimulationEffectKind.TrueDamage:
                    if (target == null || !target.Alive)
                    {
                        return null;
                    }
                    var incoming = command.Amount;
                    var blocked = command.Kind == CombatSimulationEffectKind.TrueDamage
                        ? 0
                        : Math.Min(target.Block, incoming);
                    target.Block -= blocked;
                    var hpDamage = Math.Min(target.Hp, Math.Max(0, incoming - blocked));
                    target.Hp -= hpDamage;
                    if (source?.Kind == CombatSimulationActorKind.Player
                        && target.Kind == CombatSimulationActorKind.Enemy)
                    {
                        metrics.DamageDealt += hpDamage;
                    }
                    if (target.Kind == CombatSimulationActorKind.Player)
                    {
                        metrics.DamageTaken += hpDamage;
                    }
                    var damageEvent = EmitFromCommand(
                        CombatSimulationEventKind.DamageDealt,
                        command,
                        hpDamage,
                        beforeHash);
                    if (target.Hp <= 0)
                    {
                        secondaryCommandEvent = Emit(
                            CombatSimulationEventKind.ActorDefeated,
                            command.SourceActorId,
                            target.ActorId,
                            command.CardInstanceId,
                            target.DefinitionId,
                            1,
                            damageEvent.Sequence);
                    }
                    return damageEvent;

                case CombatSimulationEffectKind.GainBlock:
                    if (target == null || !target.Alive) return null;
                    target.Block += command.Amount;
                    if (target.Kind == CombatSimulationActorKind.Player)
                    {
                        metrics.BlockGained += command.Amount;
                    }
                    return EmitFromCommand(
                        CombatSimulationEventKind.BlockGained,
                        command,
                        command.Amount,
                        beforeHash);

                case CombatSimulationEffectKind.Heal:
                    if (target == null || !target.Alive) return null;
                    var healing = Math.Min(command.Amount, Math.Max(0, target.MaxHp - target.Hp));
                    target.Hp += healing;
                    if (target.Kind == CombatSimulationActorKind.Player)
                    {
                        metrics.Healing += healing;
                    }
                    return EmitFromCommand(
                        CombatSimulationEventKind.Healed,
                        command,
                        healing,
                        beforeHash);

                case CombatSimulationEffectKind.Draw:
                    DrawCards(command.Amount, command.TargetActorId, command.ParentSequence);
                    return null;

                case CombatSimulationEffectKind.DiscardRandom:
                    RandomMoveFromHand(command, CombatCardZone.DiscardPile);
                    return null;

                case CombatSimulationEffectKind.ExhaustRandom:
                    RandomMoveFromHand(command, CombatCardZone.ExhaustPile);
                    return null;

                case CombatSimulationEffectKind.GainEnergy:
                    if (target == null || !target.Alive) return null;
                    target.Energy += command.Amount;
                    return EmitFromCommand(
                        CombatSimulationEventKind.EnergyChanged,
                        command,
                        command.Amount,
                        beforeHash);

                case CombatSimulationEffectKind.AddStatus:
                    if (target == null || !target.Alive
                        || !ruleset.TryGetStatusCore(command.DefinitionId, out var statusDefinition))
                    {
                        AddUnsupported("status:" + command.DefinitionId);
                        Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                        return null;
                    }
                    Reference("status:" + statusDefinition.StatusId, statusDefinition.Fidelity);
                    if (unsupported.Count > 0)
                    {
                        Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                        return null;
                    }
                    var existing = target.Statuses.FirstOrDefault(status =>
                        string.Equals(status.StatusId, command.DefinitionId, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        target.Statuses.Add(new CombatStatusState
                        {
                            StatusId = command.DefinitionId,
                            Stacks = Math.Max(1, command.Amount),
                            Duration = command.Duration,
                            SourceActorId = command.SourceActorId
                        });
                    }
                    else
                    {
                        existing.Stacks += Math.Max(1, command.Amount);
                        existing.Duration = Math.Max(existing.Duration, command.Duration);
                    }
                    return EmitFromCommand(
                        CombatSimulationEventKind.StatusAdded,
                        command,
                        command.Amount,
                        beforeHash);

                case CombatSimulationEffectKind.RemoveStatus:
                    if (target == null) return null;
                    var removed = target.Statuses.RemoveAll(status =>
                        string.Equals(status.StatusId, command.DefinitionId, StringComparison.OrdinalIgnoreCase));
                    return removed > 0
                        ? EmitFromCommand(
                            CombatSimulationEventKind.StatusRemoved,
                            command,
                            removed,
                            beforeHash)
                        : null;

                case CombatSimulationEffectKind.CreateCard:
                    if (!ruleset.TryGetCardCore(command.DefinitionId, out var cardDefinition))
                    {
                        AddUnsupported("card:" + command.DefinitionId);
                        Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                        return null;
                    }
                    Reference("card:" + cardDefinition.CardId, cardDefinition.Fidelity);
                    if (unsupported.Count > 0)
                    {
                        Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                        return null;
                    }
                    var card = new CombatCardInstanceState
                    {
                        InstanceId = State.NextCardInstanceId++,
                        CardId = cardDefinition.CardId
                    };
                    State.Cards.Add(card);
                    if (State.Hand.Count < Math.Max(1, scenario.HandLimit))
                    {
                        State.Hand.Add(card.InstanceId);
                        metrics.CardsDrawn++;
                        return Emit(
                            CombatSimulationEventKind.CardDrawn,
                            command.SourceActorId,
                            command.TargetActorId,
                            card.InstanceId,
                            card.CardId,
                            1,
                            command.ParentSequence);
                    }
                    State.DiscardPile.Add(card.InstanceId);
                    return Emit(
                        CombatSimulationEventKind.CardDiscarded,
                        command.SourceActorId,
                        command.TargetActorId,
                        card.InstanceId,
                        card.CardId,
                        1,
                        command.ParentSequence);

                case CombatSimulationEffectKind.ChangeCardCost:
                {
                    var affected = command.CardInstanceId > 0
                        ? State.Cards.Where(card => card.InstanceId == command.CardInstanceId).ToList()
                        : State.Hand
                            .Select(State.FindCard)
                            .Where(card => card != null
                                           && (string.IsNullOrWhiteSpace(command.DefinitionId)
                                               || string.Equals(
                                                   card.CardId,
                                                   command.DefinitionId,
                                                   StringComparison.OrdinalIgnoreCase)))
                            .Select(card => card!)
                            .ToList();
                    foreach (var affectedCard in affected)
                    {
                        affectedCard.CostModifier += command.Amount;
                    }
                    return affected.Count > 0
                        ? EmitFromCommand(
                            CombatSimulationEventKind.CardCostChanged,
                            command,
                            command.Amount,
                            beforeHash)
                        : null;
                }

                case CombatSimulationEffectKind.SummonEnemy:
                {
                    if (!ruleset.TryGetEnemyCore(command.DefinitionId, out var enemyDefinition))
                    {
                        AddUnsupported("enemy:" + command.DefinitionId);
                        Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                        return null;
                    }
                    var existingSummons = State.Actors.Count(actor =>
                        actor.InstanceKey.StartsWith("summon:", StringComparison.Ordinal));
                    var summonCount = Math.Max(1, command.Amount);
                    if (existingSummons + summonCount > limits.MaximumSummonedActors)
                    {
                        Terminate(
                            CombatSimulationOutcome.Invalid,
                            CombatTerminationReason.MaximumSummonedActors);
                        return null;
                    }
                    Reference("enemy:" + enemyDefinition.EnemyId, enemyDefinition.Fidelity);
                    if (unsupported.Count > 0)
                    {
                        Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                        return null;
                    }
                    CombatSimulationEvent? firstSummon = null;
                    for (var i = 0; i < summonCount; i++)
                    {
                        var summoned = new CombatActorState
                        {
                            ActorId = State.NextActorId++,
                            InstanceKey = "summon:" + enemyDefinition.EnemyId + ":" + State.NextActorId,
                            DefinitionId = enemyDefinition.EnemyId,
                            DisplayName = string.IsNullOrWhiteSpace(enemyDefinition.DisplayName)
                                ? enemyDefinition.EnemyId
                                : enemyDefinition.DisplayName,
                            Kind = CombatSimulationActorKind.Enemy,
                            Hp = enemyDefinition.MaxHp,
                            MaxHp = enemyDefinition.MaxHp,
                            Block = Math.Max(0, enemyDefinition.InitialBlock)
                        };
                        State.Actors.Add(summoned);
                        var summonEvent = Emit(
                            CombatSimulationEventKind.ActorSummoned,
                            command.SourceActorId,
                            summoned.ActorId,
                            command.CardInstanceId,
                            summoned.DefinitionId,
                            1,
                            command.ParentSequence,
                            null,
                            beforeHash);
                        firstSummon ??= summonEvent;
                    }
                    return firstSummon;
                }

                case CombatSimulationEffectKind.Despawn:
                    if (target == null || !target.Alive) return null;
                    target.Hp = 0;
                    return EmitFromCommand(
                        CombatSimulationEventKind.ActorDefeated,
                        command,
                        1,
                        beforeHash);
                default:
                    return null;
            }
        }

        private void EnqueueTriggers(
            CombatSimulationEvent sourceEvent,
            Queue<CombatSimulationCommand> queue,
            int wave)
        {
            var matches = new List<TriggerMatch>();
            foreach (var actor in State.Actors.Where(actor => actor.Alive).OrderBy(actor => actor.ActorId))
            {
                foreach (var status in actor.Statuses.ToList())
                {
                    if (!ruleset.TryGetStatusCore(status.StatusId, out var definition))
                    {
                        AddUnsupported("status:" + status.StatusId);
                        continue;
                    }
                    foreach (var trigger in definition.Triggers.Where(trigger =>
                                 trigger.EventKind == sourceEvent.Kind))
                    {
                        matches.Add(new TriggerMatch(actor, status, definition, trigger));
                    }
                }
            }
            foreach (var match in matches
                         .OrderBy(item => item.Trigger.Priority)
                         .ThenBy(item => item.Definition.OwnerModId, StringComparer.Ordinal)
                         .ThenBy(item => item.Definition.StatusId, StringComparer.Ordinal)
                         .ThenBy(item => item.Trigger.TriggerId, StringComparer.Ordinal)
                         .ThenBy(item => item.Actor.ActorId))
            {
                CompileEffects(
                    match.Trigger.Effects,
                    match.Actor.ActorId,
                    sourceEvent.TargetActorId,
                    sourceEvent.CardInstanceId,
                    null,
                    sourceEvent,
                    wave,
                    queue,
                    match.Status.Stacks);
            }
        }

        private void DrawCards(int count, int actorId, long parentSequence)
        {
            for (var i = 0; i < Math.Max(0, count); i++)
            {
                if (State.Hand.Count >= Math.Max(1, scenario.HandLimit))
                {
                    break;
                }
                if (State.DrawPile.Count == 0 && State.DiscardPile.Count > 0)
                {
                    State.DrawPile.AddRange(State.DiscardPile);
                    State.DiscardPile.Clear();
                    var shuffle = CombatDeterministicRandom.Shuffle(
                        scenario.Seed,
                        State.Random,
                        "deck.shuffle.recycle",
                        State.DrawPile);
                    TraceRandomDraws(shuffle, "discard recycle");
                }
                if (State.DrawPile.Count == 0)
                {
                    break;
                }
                var instanceId = State.DrawPile[State.DrawPile.Count - 1];
                State.DrawPile.RemoveAt(State.DrawPile.Count - 1);
                State.Hand.Add(instanceId);
                metrics.CardsDrawn++;
                var card = State.FindCard(instanceId);
                Emit(
                    CombatSimulationEventKind.CardDrawn,
                    actorId,
                    actorId,
                    instanceId,
                    card?.CardId ?? "",
                    1,
                    parentSequence);
            }
        }

        private void DiscardHand(int actorId)
        {
            foreach (var instanceId in State.Hand.ToList())
            {
                State.Hand.Remove(instanceId);
                State.DiscardPile.Add(instanceId);
                Emit(
                    CombatSimulationEventKind.CardDiscarded,
                    actorId,
                    actorId,
                    instanceId,
                    State.FindCard(instanceId)?.CardId ?? "",
                    1);
            }
        }

        private void RandomMoveFromHand(
            CombatSimulationCommand command,
            CombatCardZone destination)
        {
            for (var i = 0; i < command.Amount && State.Hand.Count > 0; i++)
            {
                var index = CombatDeterministicRandom.NextInt(
                    scenario.Seed,
                    State.Random,
                    destination == CombatCardZone.ExhaustPile ? "hand.exhaust" : "hand.discard",
                    State.Hand.Count,
                    out var draw);
                TraceRandomDraw(draw, destination.ToString());
                var instanceId = State.Hand[index];
                State.Hand.RemoveAt(index);
                if (destination == CombatCardZone.ExhaustPile)
                {
                    State.ExhaustPile.Add(instanceId);
                }
                else
                {
                    State.DiscardPile.Add(instanceId);
                }
                Emit(
                    destination == CombatCardZone.ExhaustPile
                        ? CombatSimulationEventKind.CardExhausted
                        : CombatSimulationEventKind.CardDiscarded,
                    command.SourceActorId,
                    command.TargetActorId,
                    instanceId,
                    State.FindCard(instanceId)?.CardId ?? "",
                    1,
                    command.ParentSequence,
                    draw);
            }
        }

        private void DecayStatuses()
        {
            foreach (var actor in State.Actors)
            {
                foreach (var status in actor.Statuses.ToList())
                {
                    if (!ruleset.TryGetStatusCore(status.StatusId, out var definition)
                        || !definition.DecayAtRoundEnd
                        || status.Duration <= 0)
                    {
                        continue;
                    }
                    status.Duration--;
                    if (status.Duration <= 0)
                    {
                        actor.Statuses.Remove(status);
                        Emit(
                            CombatSimulationEventKind.StatusRemoved,
                            actor.ActorId,
                            actor.ActorId,
                            0,
                            status.StatusId,
                            status.Stacks);
                    }
                }
            }
        }

        private void CheckOutcome()
        {
            var player = State.Player;
            if (player == null || !player.Alive)
            {
                Terminate(CombatSimulationOutcome.Defeat, CombatTerminationReason.Defeat);
            }
            else if (!State.LivingEnemies.Any())
            {
                Terminate(CombatSimulationOutcome.Victory, CombatTerminationReason.Victory);
            }
        }

        private bool ProcessLifecycleEvent(
            CombatSimulationEventKind kind,
            int sourceActorId,
            int targetActorId,
            string definitionId,
            int amount)
        {
            currentActionCommandCount = 0;
            var item = Emit(
                kind,
                sourceActorId,
                targetActorId,
                0,
                definitionId,
                amount);
            var queue = new Queue<CombatSimulationCommand>();
            EnqueueTriggers(item, queue, 1);
            return ExecuteQueue(queue);
        }

        private bool ValidateState()
        {
            var zones = State.DrawPile
                .Concat(State.Hand)
                .Concat(State.DiscardPile)
                .Concat(State.ExhaustPile)
                .ToList();
            var valid = zones.Count == State.Cards.Count
                        && zones.Distinct().Count() == zones.Count
                        && State.Actors.Select(actor => actor.ActorId).Distinct().Count() == State.Actors.Count
                        && State.Cards.Select(card => card.InstanceId).Distinct().Count() == State.Cards.Count
                        && State.Actors.All(actor =>
                            actor.Hp >= 0
                            && actor.Hp <= actor.MaxHp
                            && actor.Block >= 0
                            && actor.Energy >= 0);
            if (!valid)
            {
                AddUnsupported("state-invariant");
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.EngineError);
            }
            return valid;
        }

        private void FinishSummary(CombatTurnSummary summary)
        {
            var player = State.Player;
            summary.PlayerHpAtEnd = player?.Hp ?? 0;
            summary.EnemyHpAtEnd = State.LivingEnemies.Sum(enemy => enemy.Hp);
            summary.EndStateHash = CombatBattleStateHasher.Hash(State);
            turnSummaries.Add(summary);
        }

        private CombatSimulationEvent EmitFromCommand(
            CombatSimulationEventKind kind,
            CombatSimulationCommand command,
            int amount,
            string beforeHash)
        {
            var draw = string.IsNullOrWhiteSpace(command.RandomStreamId)
                ? null
                : new CombatRandomDraw
                {
                    StreamId = command.RandomStreamId,
                    Counter = command.RandomCounter,
                    Value = command.RandomValue
                };
            return Emit(
                kind,
                command.SourceActorId,
                command.TargetActorId,
                command.CardInstanceId,
                command.DefinitionId,
                amount,
                command.ParentSequence,
                draw,
                beforeHash);
        }

        private CombatSimulationEvent Emit(
            CombatSimulationEventKind kind,
            int sourceActorId,
            int targetActorId,
            int cardInstanceId,
            string definitionId,
            int amount,
            long parentSequence = 0,
            CombatRandomDraw? randomDraw = null,
            string? beforeStateHash = null)
        {
            var captureHashes = scenario.TraceLevel == CombatSimulationTraceLevel.Full;
            var beforeHash = captureHashes
                ? beforeStateHash ?? CombatBattleStateHasher.Hash(State)
                : "";
            State.EventSequence++;
            var item = new CombatSimulationEvent
            {
                Sequence = State.EventSequence,
                ParentSequence = parentSequence,
                Turn = State.Turn,
                Phase = State.Phase,
                Kind = kind,
                SourceActorId = sourceActorId,
                TargetActorId = targetActorId,
                CardInstanceId = cardInstanceId,
                DefinitionId = definitionId ?? "",
                Amount = amount,
                BeforeHash = beforeHash,
                AfterHash = captureHashes ? CombatBattleStateHasher.Hash(State) : "",
                RandomStreamId = randomDraw?.StreamId ?? "",
                RandomCounter = randomDraw?.Counter ?? 0,
                RandomValue = randomDraw?.Value ?? 0
            };
            if (ShouldTrace(kind))
            {
                Events.Add(item);
            }
            return item;
        }

        private bool ShouldTrace(CombatSimulationEventKind kind)
        {
            if (scenario.TraceLevel == CombatSimulationTraceLevel.Full)
            {
                return true;
            }
            if (scenario.TraceLevel == CombatSimulationTraceLevel.Summary)
            {
                return kind == CombatSimulationEventKind.BattleStarted
                       || kind == CombatSimulationEventKind.BattleEnded;
            }
            return kind != CombatSimulationEventKind.EnergyChanged
                   && kind != CombatSimulationEventKind.BlockGained
                   && kind != CombatSimulationEventKind.Healed;
        }

        private void TraceRandomDraws(IEnumerable<CombatRandomDraw> draws, string message)
        {
            foreach (var draw in draws)
            {
                TraceRandomDraw(draw, message);
            }
        }

        private void TraceRandomDraw(CombatRandomDraw draw, string message)
        {
            var item = Emit(
                CombatSimulationEventKind.RandomResolved,
                0,
                0,
                0,
                "",
                0,
                0,
                draw);
            item.Message = message;
        }

        private sealed class TriggerMatch
        {
            public TriggerMatch(
                CombatActorState actor,
                CombatStatusState status,
                CombatStatusDefinition definition,
                CombatStatusTriggerDefinition trigger)
            {
                Actor = actor;
                Status = status;
                Definition = definition;
                Trigger = trigger;
            }

            public CombatActorState Actor { get; }

            public CombatStatusState Status { get; }

            public CombatStatusDefinition Definition { get; }

            public CombatStatusTriggerDefinition Trigger { get; }
        }
    }
}
