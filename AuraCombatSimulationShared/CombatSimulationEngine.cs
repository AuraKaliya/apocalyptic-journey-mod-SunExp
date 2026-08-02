using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AuraCombatSimulation.Shared;

public sealed class CombatSimulationEngine
{
    private readonly ICombatSimulationRuntimeExtensionFactory? extensionFactory;

    public CombatSimulationEngine(
        ICombatSimulationRuntimeExtensionFactory? extensionFactory = null)
    {
        this.extensionFactory = extensionFactory;
    }

    public CombatSimulationResult Run(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        ICombatSimulationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (policy == null) throw new ArgumentNullException(nameof(policy));

        var session = new Session(
            scenario,
            ruleset,
            policy,
            extensionFactory?.Create(scenario, ruleset));
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
            return session.CompleteResult();
        }
        catch (Exception ex)
        {
            session.AddUnsupported("engine-error:" + ex.GetType().Name + ":" + ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                var stack = ex.StackTrace!
                    .Replace(Environment.NewLine, " <- ");
                session.AddUnsupported(
                    "engine-stack:"
                    + stack.Substring(0, Math.Min(2000, stack.Length)));
            }
            session.Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.EngineError);
            return session.CompleteResultAfterFailure();
        }
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

    public IReadOnlyList<CombatSimulationAction> GetInvocablePlayerActions(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        CombatBattleState state)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (state == null) throw new ArgumentNullException(nameof(state));
        return Session.BuildInvocableActions(scenario, ruleset, state);
    }

    public CombatActionApplicationResult ForkAndApplyPlayerAction(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        CombatBattleState source,
        CombatSimulationAction action,
        bool captureSemanticEvents = false,
        bool allowPolicyIneligible = false)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (action == null) throw new ArgumentNullException(nameof(action));

        var session = new Session(
            scenario,
            ruleset,
            FirstLegalCombatSimulationPolicy.Instance,
            extensionFactory?.Create(scenario, ruleset),
            source.Clone(),
            captureSemanticEvents);
        var candidates = allowPolicyIneligible
            ? Session.BuildInvocableActions(scenario, ruleset, session.State)
            : Session.BuildLegalActions(scenario, ruleset, session.State);
        var selected = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.CandidateId, action.CandidateId, StringComparison.Ordinal));
        if (selected == null || selected.Kind == CombatSimulationActionKind.EndTurn)
        {
            return new CombatActionApplicationResult
            {
                Reason = allowPolicyIneligible
                    ? "action is not game-invocable"
                    : "action is not policy-eligible",
                Outcome = CombatActionApplicationOutcome.Rejected,
                State = source.Clone()
            };
        }

        var success = session.ApplyPlayerAction(selected);
        return new CombatActionApplicationResult
        {
            Success = success,
            Reason = success
                ? session.LastActionOutcome
                  == CombatActionApplicationOutcome.NoEffect
                    ? selected.EligibilityReason
                    : ""
                : session.State.TerminationReason.ToString(),
            Outcome = session.LastActionOutcome,
            PolicyEligible = selected.PolicyEligible,
            ActionContractVersion = session.LastActionContractVersion,
            State = session.State.Clone(),
            Events = new List<CombatSimulationEvent>(session.Events)
        };
    }

    private sealed class Session :
        ICombatSimulationRuntimeContext,
        ICombatPersistentProgressionContext
    {
        private readonly CombatScenarioDefinition scenario;
        private readonly CombatRuleset ruleset;
        private readonly ICombatSimulationPolicy policy;
        private readonly CombatSimulationLimits limits;
        private readonly HashSet<string> unsupported = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> referencedDefinitions = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> authoritativeDefinitions = new(StringComparer.OrdinalIgnoreCase);
        private readonly CombatSimulationMetrics metrics = new();
        private readonly Dictionary<string, int> persistentVariableDeltas =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CombatSimulationRewardMutation> rewardMutations =
            new();
        private readonly List<CombatTurnSummary> turnSummaries = new();
        private int currentActionCommandCount;
        private string currentActionDefinitionId = "";
        private readonly Queue<string> recentCommands = new();
        private CombatSimulationFailureDiagnostics failureDiagnostics = new();
        private readonly List<CombatSimulationEvent> secondaryCommandEvents = new();
        private List<CombatSimulationEvent>? currentActionContractEvents;
        private readonly ICombatSimulationRuntimeExtension? extension;
        private readonly HashSet<long> extensionEventSequences = new();
        private bool extensionInitialized;
        private bool extensionCompleted;
        private bool explicitRuleTermination;
        private CombatTerminalResolution terminalResolution;
        private CombatSimulationOutcome initialTerminalOutcome;
        private CombatTerminationReason initialTerminationReason;
        private int initialTerminalPlayerHp;
        private int initialTerminalLivingEnemyCount;
        private bool terminalSettlementInProgress;
        private bool terminalConsistencyValid = true;
        private string terminalConsistencyReason = "";
        private int terminalPlayerHp;
        private int terminalLivingEnemyCount;
        private bool terminalStateCaptured;
        private readonly bool captureSemanticEvents;

        public Session(
            CombatScenarioDefinition scenario,
            CombatRuleset ruleset,
            ICombatSimulationPolicy policy,
            ICombatSimulationRuntimeExtension? extension = null,
            CombatBattleState? initialState = null,
            bool captureSemanticEvents = false)
        {
            this.scenario = scenario;
            this.ruleset = ruleset;
            this.policy = policy;
            this.extension = extension;
            this.captureSemanticEvents = captureSemanticEvents;
            limits = (scenario.Limits ?? new CombatSimulationLimits()).Normalize();
            State = initialState ?? new CombatBattleState();
        }

        public CombatBattleState State { get; }

        public List<CombatSimulationEvent> Events { get; } = new();

        public CombatActionApplicationOutcome LastActionOutcome { get; private set; } =
            CombatActionApplicationOutcome.Rejected;

        public string LastActionContractVersion { get; private set; } = "";

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
            var duplicateRewardRule = scenario.RewardRules
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.RewardId))
                .GroupBy(item => item.RewardId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateRewardRule != null)
            {
                AddUnsupported("duplicate-reward-rule:" + duplicateRewardRule.Key);
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
                BaseEnergy = Math.Max(0, scenario.Player.BaseEnergy),
                Variables = new Dictionary<string, double>(
                    scenario.Player.Variables ?? new Dictionary<string, double>(),
                    StringComparer.OrdinalIgnoreCase)
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
                var enemyVariables = new Dictionary<string, double>(
                    definition.Variables,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var variable in setup.Variables)
                {
                    enemyVariables[variable.Key] = variable.Value;
                }
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
                    Block = Math.Max(0, definition.InitialBlock + setup.InitialBlockBonus),
                    Variables = enemyVariables
                };
                actor.Variables["PercentDamage"] =
                    Variable(actor, "PercentDamage", 1d) * Math.Max(0d, setup.AttackScale);
                actor.Variables["AttackScale"] = Math.Max(0d, setup.AttackScale);
                State.Actors.Add(actor);
                AddInitialStatuses(actor, definition.InitialStatuses);
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
                    CardId = definition.CardId,
                    CreationSource = "starting-deck",
                    CreationSourceId = scenario.Player.RoleId
                };
                State.Cards.Add(instance);
                State.DrawPile.Add(instance.InstanceId);
            }
            foreach (var cardId in scenario.Player.SkillCardIds
                         ?? new List<string>())
            {
                if (!ruleset.TryGetCardCore(cardId, out var definition))
                {
                    AddUnsupported("skill-card:" + cardId);
                    continue;
                }
                Reference("card:" + definition.CardId, definition.Fidelity);
                var instance = new CombatCardInstanceState
                {
                    InstanceId = State.NextCardInstanceId++,
                    CardId = definition.CardId,
                    CreationSource = "role-skill",
                    CreationSourceId = scenario.Player.RoleId
                };
                State.Cards.Add(instance);
                State.SkillCards.Add(instance.InstanceId);
                State.SkillCooldowns[instance.InstanceId] =
                    scenario.Player.InitialSkillCooldownTurns.TryGetValue(
                        definition.CardId,
                        out var initialCooldown)
                        ? Math.Max(0, initialCooldown)
                        : 0;
            }
            foreach (var cardId in scenario.InitialDiscardCards
                         ?? new List<string>())
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
                    CardId = definition.CardId,
                    CreationSource = "initial-discard",
                    CreationSourceId = scenario.Player.RoleId
                };
                State.Cards.Add(instance);
                State.DiscardPile.Add(instance.InstanceId);
            }

            extension?.Initialize(this);
            extensionInitialized = extension != null;
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
            OrderInherentCardsForDraw();
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
            State.PlayerActionsThisTurn = 0;
            State.PlayerEnergySpentThisTurn = 0;
            State.NoEffectActionAttemptsThisTurn.Clear();
            State.Phase = CombatSimulationPhase.PlayerTurnStart;
            ResetHpLossWindow();
            var player = State.Player;
            if (player == null || !player.Alive)
            {
                Terminate(CombatSimulationOutcome.Defeat, CombatTerminationReason.Defeat);
                return false;
            }
            player.Energy = CombatTurnTransitionRules.NextTurnPower(
                player.Energy,
                WitchRounded(
                    Variable(player, "BaseEnergy", player.BaseEnergy)));
            // The native player-turn entry point clears shield on every
            // registered combat status before StartRound is dispatched.
            foreach (var actor in State.Actors)
            {
                actor.Block = 0;
            }
            foreach (var skillId in State.SkillCooldowns.Keys.ToList())
            {
                var skillCardId = State.FindCard(skillId)?.CardId ?? "";
                if (scenario.Player.NativeManagedSkillCooldownIds.Contains(
                        skillCardId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                State.SkillCooldowns[skillId] = Math.Max(
                    0,
                    State.SkillCooldowns[skillId] - 1);
            }
            SelectEnemyIntents();
            State.EnemyHpAtTurnStart =
                State.LivingEnemies.Sum(enemy => Math.Max(0, enemy.Hp));
            State.EndTurnPurposeValue = ComputeEndTurnPurposeValue();
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
            var playerTurnSkipped = ConsumeTurnSkip(player);
            if (!playerTurnSkipped)
            {
                DrawCards(
                    Math.Max(
                        0,
                        (State.Turn == 1
                            ? scenario.InitialDraw
                            : scenario.DrawPerTurn)
                        + WitchRounded(Variable(
                            player,
                            "DrawPerTurnModifier",
                            0d))),
                    player.ActorId,
                    0);

                State.Phase = CombatSimulationPhase.PlayerAction;
                var enemyHpAtLastProgress =
                    State.LivingEnemies.Sum(enemy => enemy.Hp);
                var actionsWithoutEnemyHpProgress = 0;
                while (State.Outcome == CombatSimulationOutcome.None)
                {
                    if (State.ActionSequence >= limits.MaximumActions)
                    {
                        CaptureActionFailure();
                        Terminate(
                            CombatSimulationOutcome.Invalid,
                            CombatTerminationReason.MaximumActions);
                        return false;
                    }

                    if (extension
                        is ICombatSimulationDecisionRuntimeExtension
                        decisionExtension)
                    {
                        decisionExtension.BeforePolicyDecision(this);
                    }
                    var legal = BuildLegalActions(scenario, ruleset, State);
                    var context = new CombatSimulationPolicyContext
                    {
                        Scenario = scenario,
                        Ruleset = ruleset,
                        State = policy is ICombatSimulationBorrowedStatePolicy
                            ? State
                            : State.Clone(),
                        LegalActions = legal
                    };
                    var requested = policy.SelectAction(context);
                    if (policy is ICombatSimulationPolicyMetricsProvider
                        metricsProvider)
                    {
                        metrics.PolicyDecisions++;
                        metrics.SearchSimulations += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics.SearchSimulations);
                        metrics.SearchNodes += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics.SearchNodes);
                        if (metricsProvider.LastDecisionMetrics.SearchStoppedEarly)
                        {
                            metrics.SearchEarlyStops++;
                        }
                        var searchTier =
                            metricsProvider.LastDecisionMetrics.SearchBudgetTier;
                        if (!string.IsNullOrWhiteSpace(searchTier))
                        {
                            metrics.SearchBudgetTierCounts[searchTier] =
                                metrics.SearchBudgetTierCounts.TryGetValue(
                                    searchTier,
                                    out var tierCount)
                                    ? tierCount + 1
                                    : 1;
                        }
                        metrics.CertifiedLoops += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics.CertifiedLoops);
                        metrics.SustainableControlLoops += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics
                                .SustainableControlLoops);
                        metrics.FakeLoops += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics.FakeLoops);
                        metrics.BlockedLoops += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics.BlockedLoops);
                        metrics.ExplorationDecisions += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics
                                .ExplorationDecisions);
                        metrics.ExplorationActionOverrides += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics
                                .ExplorationActionOverrides);
                        var rootVisitShare = metricsProvider
                            .LastDecisionMetrics.RootMaximumVisitShare;
                        if (!double.IsNaN(rootVisitShare)
                            && !double.IsInfinity(rootVisitShare)
                            && rootVisitShare > 0d)
                        {
                            metrics.RootMaximumVisitShareTotal +=
                                Math.Min(1d, rootVisitShare);
                            metrics.RootMaximumVisitShareSamples++;
                        }
                        metrics.AuthoritativeActionsAudited += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics
                                .AuthoritativeActionsAudited);
                        metrics.AuthoritativeSemanticMismatches += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics
                                .AuthoritativeSemanticMismatches);
                        metrics.AuthoritativeSelectedActionsAudited += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics
                                .AuthoritativeSelectedActionsAudited);
                        metrics.AuthoritativeSelectedSemanticMismatches +=
                            Math.Max(
                                0,
                                metricsProvider.LastDecisionMetrics
                                    .AuthoritativeSelectedSemanticMismatches);
                        metrics.AuthoritativeTeacherOverrides += Math.Max(
                            0,
                            metricsProvider.LastDecisionMetrics
                                .AuthoritativeTeacherOverrides);
                        MergeCounts(
                            metrics.AuthoritativeSemanticMismatchKinds,
                            metricsProvider.LastDecisionMetrics
                                .AuthoritativeSemanticMismatchKinds);
                        MergeCounts(
                            metrics.AuthoritativeSemanticMismatchSources,
                            metricsProvider.LastDecisionMetrics
                                .AuthoritativeSemanticMismatchSources);
                        MergeCounts(
                            metrics.AuthoritativeSemanticMismatchScenarios,
                            metricsProvider.LastDecisionMetrics
                                .AuthoritativeSemanticMismatchScenarios);
                        metrics.SemanticAudit.MergeFrom(
                            metricsProvider.LastDecisionMetrics.SemanticAudit);
                    }
                    var selected = requested == null
                        ? null
                        : legal.FirstOrDefault(candidate =>
                            string.Equals(
                                candidate.CandidateId,
                                requested.CandidateId,
                                StringComparison.Ordinal));
                    if (selected == null)
                    {
                        Terminate(
                            CombatSimulationOutcome.Invalid,
                            CombatTerminationReason.IllegalPolicyAction);
                        return false;
                    }
                    if (selected.Kind == CombatSimulationActionKind.EndTurn)
                    {
                        RecordEndTurnDecision(forced: false);
                        break;
                    }

                    summary.Actions++;
                    if (!ApplyPlayerAction(selected))
                    {
                        return false;
                    }
                    var currentEnemyHp =
                        State.LivingEnemies.Sum(enemy => enemy.Hp);
                    if (currentEnemyHp < enemyHpAtLastProgress)
                    {
                        enemyHpAtLastProgress = currentEnemyHp;
                        actionsWithoutEnemyHpProgress = 0;
                    }
                    else
                    {
                        actionsWithoutEnemyHpProgress++;
                    }
                    if (actionsWithoutEnemyHpProgress >= 32)
                    {
                        // The player always has a legal EndTurn action. A
                        // deterministic policy that keeps playing an
                        // energy-neutral, non-damaging cycle should yield the
                        // turn instead of invalidating the whole campaign at
                        // the global action/command safety limit.
                        metrics.ForcedEndTurns++;
                        RecordEndTurnDecision(forced: true);
                        break;
                    }
                }
            }

            if (State.Outcome != CombatSimulationOutcome.None)
            {
                FinishSummary(summary);
                return false;
            }

            State.Phase = CombatSimulationPhase.PlayerTurnEnd;
            ResetHpLossWindow();
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
            if (!DiscardUnretainedHand(player.ActorId))
            {
                FinishSummary(summary);
                return false;
            }
            State.Phase = CombatSimulationPhase.EnemyAction;
            foreach (var enemy in State.LivingEnemies.OrderBy(actor => actor.ActorId).ToList())
            {
                if (!ProcessLifecycleEvent(
                        CombatSimulationEventKind.TurnStarted,
                        enemy.ActorId,
                        enemy.ActorId,
                        enemy.DefinitionId,
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
                if (ConsumeTurnSkip(enemy))
                {
                    if (!ProcessLifecycleEvent(
                            CombatSimulationEventKind.TurnEnded,
                            enemy.ActorId,
                            enemy.ActorId,
                            enemy.DefinitionId,
                            State.Turn))
                    {
                        FinishSummary(summary);
                        return false;
                    }
                    continue;
                }
                var intentIds = enemy.CurrentIntentIds.Count > 0
                    ? enemy.CurrentIntentIds.ToList()
                    : string.IsNullOrWhiteSpace(enemy.CurrentIntentId)
                        ? new List<string>()
                        : new List<string> { enemy.CurrentIntentId };
                foreach (var intentId in intentIds)
                {
                    if (!ExecuteEnemyIntent(enemy, intentId))
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
                if (!ProcessLifecycleEvent(
                        CombatSimulationEventKind.TurnEnded,
                        enemy.ActorId,
                        enemy.ActorId,
                        enemy.DefinitionId,
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
            }

            State.Phase = CombatSimulationPhase.RoundEnd;
            DecayStatuses();
            FinishSummary(summary);
            return State.Outcome == CombatSimulationOutcome.None;
        }

        private void RecordEndTurnDecision(bool forced)
        {
            var player = State.Player;
            if (player == null)
            {
                return;
            }

            var legal = BuildLegalActions(scenario, ruleset, State);
            var hasSafeAlternative = legal.Any(action =>
                action.Kind != CombatSimulationActionKind.EndTurn
                && State.FindCard(action.CardInstanceId)?.IsVisibleFake != true);
            var madeProgress = State.LivingEnemies.Sum(enemy =>
                Math.Max(0, enemy.Hp)) < State.EnemyHpAtTurnStart;
            State.ConsecutiveNoProgressTurns =
                madeProgress || State.EndTurnPurposeValue > 0d
                    ? 0
                    : State.ConsecutiveNoProgressTurns + 1;

            if (!forced)
            {
                metrics.VoluntaryEndTurns++;
            }
            if (State.PlayerActionsThisTurn == 0)
            {
                metrics.EmptyEndTurns++;
            }
            if (player.Energy > 0 && hasSafeAlternative)
            {
                metrics.EndTurnsWithUnusedEnergy++;
                metrics.UnusedEnergyAtEndTurns += player.Energy;
            }
            var policyMetrics =
                policy is ICombatSimulationPolicyMetricsProvider provider
                    ? provider.LastDecisionMetrics
                    : null;
            var bankedSurplus = policyMetrics?.EndTurnSafetyAssessed == true
                ? Math.Max(
                    0,
                    policyMetrics.EndTurnBankedSurplusEnergy)
                : Math.Max(0, player.Energy - player.BaseEnergy);
            if (bankedSurplus > 0)
            {
                metrics.EndTurnsWithBankedSurplus++;
                metrics.BankedSurplusAtEndTurns += bankedSurplus;
            }
            if (!forced
                && policyMetrics?.EndTurnSafetyAssessed == true)
            {
                if (policyMetrics.SelectedEndTurnSevereMistake)
                {
                    metrics.DominatedEndTurns++;
                }
                if (policyMetrics.EndTurnAvoidableLethal)
                {
                    metrics.EndTurnsIntoAvoidableLethal++;
                }
                if (policyMetrics.EndTurnCertifiedCycleCount > 0)
                {
                    metrics.EndTurnsWithCertifiedCycle++;
                }
                if (policyMetrics.EndTurnUnknownLifecycleEffectCount > 0)
                {
                    metrics.EndTurnsWithUnknownLifecycle++;
                }
            }
            if (!forced && player.Energy > 0)
            {
                if (policyMetrics?.EndTurnSafetyAssessed == true
                    && policyMetrics.EndTurnSafeAlternativeCount > 0)
                {
                    metrics.AvoidableEndTurnsWithUnusedEnergy++;
                    metrics.AvoidableUnusedEnergyAtEndTurns +=
                        Math.Max(
                            0,
                            policyMetrics.EndTurnAvoidableUnusedEnergy);
                }
                else
                {
                    metrics.SaturatedEndTurnsWithUnusedEnergy++;
                }
            }
            var severeEndTurn = !forced
                                && (policyMetrics?.EndTurnSafetyAssessed == true
                                    ? policyMetrics
                                        .SelectedEndTurnSevereMistake
                                    : State.EndTurnPurposeValue <= 0d
                                      && hasSafeAlternative);
            if (severeEndTurn)
            {
                metrics.SevereEndTurnMistakes++;
            }
            metrics.MaximumConsecutiveNoProgressTurns = Math.Max(
                metrics.MaximumConsecutiveNoProgressTurns,
                State.ConsecutiveNoProgressTurns);
        }

        private double ComputeEndTurnPurposeValue()
        {
            var player = State.Player;
            if (player == null)
            {
                return 0d;
            }

            var purpose = 0d;
            foreach (var status in player.Statuses)
            {
                if (!ruleset.TryGetStatusCore(status.StatusId, out var definition))
                {
                    continue;
                }
                foreach (var trigger in definition.Triggers.Where(trigger =>
                             trigger.EventKind == CombatSimulationEventKind.TurnEnded))
                {
                    purpose += trigger.Effects.Count(IsBeneficialEndTurnEffect)
                               * Math.Max(1, status.Stacks);
                }
            }

            foreach (var reward in scenario.RewardRules)
            {
                var script = reward?.FightScript ?? "";
                if (ContainsEndTurnMarker(script)
                    && ContainsBeneficialEndTurnMarker(script))
                {
                    purpose += Math.Max(1, reward!.Stacks);
                }
            }
            return purpose;
        }

        private static bool IsBeneficialEndTurnEffect(
            CombatSimulationEffectDefinition effect)
        {
            return effect.Kind == CombatSimulationEffectKind.Damage
                   || effect.Kind == CombatSimulationEffectKind.TrueDamage
                   || effect.Kind == CombatSimulationEffectKind.GainBlock
                   || effect.Kind == CombatSimulationEffectKind.SetBlock
                   || effect.Kind == CombatSimulationEffectKind.Heal
                   || effect.Kind == CombatSimulationEffectKind.SetHp
                   || effect.Kind == CombatSimulationEffectKind.SetHpToMax
                   || effect.Kind == CombatSimulationEffectKind.Draw
                   || effect.Kind == CombatSimulationEffectKind.GainEnergy
                   || effect.Kind == CombatSimulationEffectKind.DrawToHandLimit
                   || effect.Kind == CombatSimulationEffectKind.CreateRandomCard
                   || effect.Kind == CombatSimulationEffectKind.RetrieveCards
                   || effect.Kind == CombatSimulationEffectKind.WinBattle
                   || effect.Kind == CombatSimulationEffectKind.AddStatus
                   || effect.Kind == CombatSimulationEffectKind.RemoveStatus
                   || effect.Kind == CombatSimulationEffectKind.CreateCard
                   || effect.Kind == CombatSimulationEffectKind.ChangeCardCost
                   || effect.Kind == CombatSimulationEffectKind.ModifyVariable
                   || effect.Kind == CombatSimulationEffectKind.ModifyVariablePercent
                   || effect.Kind == CombatSimulationEffectKind.ScaleVariablePercent
                   || effect.Kind == CombatSimulationEffectKind.ScaleMaxHpPercent
                   || effect.Kind == CombatSimulationEffectKind.CopyStatuses;
        }

        private static bool ContainsEndTurnMarker(string script)
        {
            return script.IndexOf(
                       "EndRound",
                       StringComparison.OrdinalIgnoreCase) >= 0
                   || script.IndexOf(
                       "TurnEnded",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsBeneficialEndTurnMarker(string script)
        {
            var markers = new[]
            {
                "GiveWin", "Damage", "Defend", "Shield", "Heal", "Cure",
                "Draw", "GetCard", "CreateCard", "AddCard", "AddPower",
                "GainEnergy", "AddBuff", "RemoveBuff", "ClearBuff"
            };
            return markers.Any(marker => script.IndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public bool ApplyPlayerAction(CombatSimulationAction action)
        {
            LastActionOutcome = CombatActionApplicationOutcome.Rejected;
            LastActionContractVersion = "";
            var player = State.Player;
            var instance = State.FindCard(action.CardInstanceId);
            var useSkill = action.Kind == CombatSimulationActionKind.UseSkill;
            var validSource = useSkill
                ? State.SkillCards.Contains(action.CardInstanceId)
                  && (!State.SkillCooldowns.TryGetValue(
                          action.CardInstanceId,
                          out var cooldown)
                      || cooldown <= 0)
                : action.Kind == CombatSimulationActionKind.PlayCard
                  && State.Hand.Contains(action.CardInstanceId);
            if (player == null
                || instance == null
                || !validSource
                || !TryGetEffectiveCardCore(ruleset, instance, out var definition))
            {
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.IllegalPolicyAction);
                return false;
            }
            LastActionContractVersion = definition.ActionContract?.Version ?? "";
            var eligibility = CombatActionContractEvaluator.Evaluate(
                scenario,
                State,
                definition,
                action);
            CombatActionContractEvaluator.Apply(action, eligibility);
            if (eligibility.ExpectedOutcome
                == CombatActionApplicationOutcome.NoEffect)
            {
                RecordNoEffectAction(action, eligibility.GuaranteedNoEffect);
                LastActionOutcome = CombatActionApplicationOutcome.NoEffect;
                return true;
            }
            if (!eligibility.PolicyEligible
                || eligibility.ExpectedOutcome
                != CombatActionApplicationOutcome.Applied)
            {
                Terminate(
                    CombatSimulationOutcome.Invalid,
                    CombatTerminationReason.IllegalPolicyAction);
                return false;
            }

            var cost = useSkill
                ? 0
                : Math.Max(0, definition.Cost + instance.CostModifier);
            if (player.Energy < cost)
            {
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.IllegalPolicyAction);
                return false;
            }

            var contractSnapshot =
                CombatActionContractSnapshot.Capture(State);
            currentActionContractEvents = new List<CombatSimulationEvent>();
            State.ActionSequence++;
            var sourceActionId = State.ActionSequence;
            ResetHpLossWindow();
            currentActionCommandCount = 0;
            currentActionDefinitionId = definition.CardId;
            player.Energy -= cost;
            State.PlayerActionsThisTurn++;
            State.PlayerEnergySpentThisTurn += cost;
            metrics.EnergySpent += cost;
            metrics.CardsPlayed++;
            metrics.CardPlayCounts[definition.CardId] =
                metrics.CardPlayCounts.TryGetValue(definition.CardId, out var count) ? count + 1 : 1;
            CombatSimulationEvent? playedCardZoneEvent = null;
            if (!useSkill && !scenario.MovePlayedCardAfterResolution)
            {
                playedCardZoneEvent = MovePlayedCardToDestination(
                    player,
                    instance,
                    definition,
                    0);
            }
            var played = Emit(
                CombatSimulationEventKind.CardPlayed,
                player.ActorId,
                action.TargetActorId,
                instance.InstanceId,
                definition.CardId,
                cost);
            var queue = new Queue<CombatSimulationCommand>();
            EnqueueTriggers(played, queue, 1);
            var actionStarted = Emit(
                CombatSimulationEventKind.ActionStarted,
                player.ActorId,
                action.TargetActorId,
                instance.InstanceId,
                definition.CardId,
                cost,
                played.Sequence);
            EnqueueTriggers(actionStarted, queue, 1);
            if (scenario.DirectHpLossAfterPlayerCard > 0)
            {
                queue.Enqueue(new CombatSimulationCommand
                {
                    Kind = CombatSimulationEffectKind.DirectHpLoss,
                    SourceActorId = player.ActorId,
                    TargetActorId = player.ActorId,
                    CardInstanceId = instance.InstanceId,
                    Amount = scenario.DirectHpLossAfterPlayerCard,
                    DefinitionId = "difficulty:player-card-hp-loss",
                    ParentSequence = played.Sequence,
                    CausalChainId = played.CausalChainId,
                    SourceActionId = State.ActionSequence
                });
            }
            CompileEffects(
                definition.Effects,
                player.ActorId,
                action.TargetActorId,
                instance.InstanceId,
                played,
                null,
                0,
                queue);
            if (!ExecuteQueue(queue))
            {
                return false;
            }
            var contractEvents = currentActionContractEvents
                                 ?? new List<CombatSimulationEvent>();
            currentActionContractEvents = null;
            if (!CombatActionContractEvaluator.AppliedPostconditionsSatisfied(
                    definition.ActionContract,
                    contractSnapshot,
                    CombatActionContractSnapshot.Capture(State),
                    contractEvents,
                    sourceActionId,
                    out var contractFailureReason))
            {
                metrics.InteractiveActionContractFailures++;
                LastActionOutcome = CombatActionApplicationOutcome.NoEffect;
                AddUnsupported(
                    "action-contract:"
                    + definition.CardId
                    + ":"
                    + contractFailureReason);
                Terminate(
                    CombatSimulationOutcome.Invalid,
                    CombatTerminationReason.UnsupportedRule);
                return false;
            }
            if (!ProcessLifecycleEvent(
                    CombatSimulationEventKind.ActionResolved,
                    player.ActorId,
                    action.TargetActorId,
                    definition.CardId,
                    1,
                    instance.InstanceId))
            {
                return false;
            }
            ReduceStatuses(player, definition => definition.ReducePerUse);

            if (!useSkill && scenario.MovePlayedCardAfterResolution)
            {
                playedCardZoneEvent = MovePlayedCardToDestination(
                    player,
                    instance,
                    definition,
                    played.Sequence);
            }
            if (playedCardZoneEvent != null)
            {
                var zoneEventQueue = new Queue<CombatSimulationCommand>();
                EnqueueTriggers(playedCardZoneEvent, zoneEventQueue, 1);
                if (!ExecuteQueue(zoneEventQueue))
                {
                    return false;
                }
            }
            if (useSkill
                && (definition.ActionContract?.CooldownOnApplied ?? true))
            {
                State.SkillCooldowns[instance.InstanceId] =
                    scenario.Player.SkillCooldownTurns.TryGetValue(
                        definition.CardId,
                        out var configuredCooldown)
                        ? Math.Max(1, configuredCooldown)
                        : 1;
            }
            LastActionOutcome = CombatActionApplicationOutcome.Applied;
            return ValidateState();
        }

        private void RecordNoEffectAction(
            CombatSimulationAction action,
            bool guaranteedNoEffect)
        {
            var key = CombatActionContractEvaluator.ActionKey(action);
            var previous = State.NoEffectActionAttemptsThisTurn.TryGetValue(
                key,
                out var count)
                ? Math.Max(0, count)
                : 0;
            State.NoEffectActionAttemptsThisTurn[key] = previous + 1;
            metrics.NoEffectActionAttempts++;
            if (previous > 0)
            {
                metrics.RepeatedNoEffectActionAttempts++;
            }
            if (guaranteedNoEffect)
            {
                metrics.GuaranteedNoEffectActionAttempts++;
            }
        }

        private CombatSimulationEvent? MovePlayedCardToDestination(
            CombatActorState player,
            CombatCardInstanceState instance,
            CombatCardDefinition definition,
            long parentSequence)
        {
            if (HasTag(instance, definition, "Recycle"))
            {
                return null;
            }
            if (!State.Hand.Contains(instance.InstanceId))
            {
                // A native use script may explicitly burn, discard, or
                // otherwise relocate the played card before the delayed
                // post-resolution move used by advanced difficulty.
                return null;
            }
            if (definition.Exhaust
                || HasTag(instance, definition, "Burnout")
                || HasTag(instance, definition, "Fragmented"))
            {
                MoveCardToZone(instance.InstanceId, CombatCardZone.ExhaustPile);
                return Emit(
                    CombatSimulationEventKind.CardExhausted,
                    player.ActorId,
                    player.ActorId,
                    instance.InstanceId,
                    definition.CardId,
                    1,
                    parentSequence);
            }
            if (HasTag(instance, definition, "Ouroboros"))
            {
                MoveCardToZone(instance.InstanceId, CombatCardZone.DrawPile);
                return Emit(
                    CombatSimulationEventKind.CardCreated,
                    player.ActorId,
                    player.ActorId,
                    instance.InstanceId,
                    definition.CardId,
                    1,
                    parentSequence);
            }
            MoveCardToZone(instance.InstanceId, CombatCardZone.DiscardPile);
            return Emit(
                CombatSimulationEventKind.CardDiscarded,
                player.ActorId,
                player.ActorId,
                instance.InstanceId,
                definition.CardId,
                1,
                parentSequence);
        }

        public CombatSimulationResult CompleteResult()
        {
            CompleteExtension();
            return BuildResult();
        }

        public CombatSimulationResult CompleteResultAfterFailure()
        {
            extensionCompleted = true;
            return BuildResult();
        }

        private CombatSimulationResult BuildResult()
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
                TerminalConsistencyValid = terminalConsistencyValid,
                TerminalConsistencyReason = terminalConsistencyReason,
                ExplicitRuleTermination = explicitRuleTermination,
                TerminalResolution = terminalResolution,
                InitialTerminalOutcome = initialTerminalOutcome,
                InitialTerminationReason = initialTerminationReason,
                InitialTerminalPlayerHp = initialTerminalPlayerHp,
                InitialTerminalLivingEnemyCount = initialTerminalLivingEnemyCount,
                TerminalPlayerHp = terminalPlayerHp,
                TerminalLivingEnemyCount = terminalLivingEnemyCount,
                Turns = State.Turn,
                FinalPlayerHp = player?.Hp ?? 0,
                FinalStateHash = CombatBattleStateHasher.Hash(State),
                SemanticCoverage = referencedDefinitions.Count <= 0
                    ? 0d
                    : (double)authoritativeDefinitions.Count / referencedDefinitions.Count,
                UnsupportedDefinitions = unsupported.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                Metrics = metrics,
                FailureDiagnostics = failureDiagnostics.Clone(),
                PersistentVariableDeltas = new Dictionary<string, int>(
                    persistentVariableDeltas,
                    StringComparer.OrdinalIgnoreCase),
                RewardVariables = scenario.RewardRules
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.RewardId))
                    .GroupBy(item => item.RewardId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => new Dictionary<string, string>(
                            group.First().Variables,
                            StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase),
                CampaignVariables = new Dictionary<string, string>(
                    scenario.CampaignVariables,
                    StringComparer.OrdinalIgnoreCase),
                RewardMutations = rewardMutations.Select(item =>
                    new CombatSimulationRewardMutation
                    {
                        Operation = item.Operation,
                        Kind = item.Kind,
                        RewardId = item.RewardId
                    }).ToList(),
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
            TerminateCore(outcome, reason, explicitRule: false);
        }

        private void TerminateCore(
            CombatSimulationOutcome outcome,
            CombatTerminationReason reason,
            bool explicitRule)
        {
            if (State.Outcome != CombatSimulationOutcome.None)
            {
                TryOverridePhysicalDefeat(outcome, reason, explicitRule);
                return;
            }
            explicitRuleTermination = explicitRule;
            terminalResolution = explicitRule
                ? CombatTerminalResolution.ExplicitRule
                : CombatTerminalResolution.Physical;
            CaptureTerminalState(outcome, explicitRule);
            initialTerminalOutcome = outcome;
            initialTerminationReason = reason;
            initialTerminalPlayerHp = terminalPlayerHp;
            initialTerminalLivingEnemyCount = terminalLivingEnemyCount;
            if (!terminalConsistencyValid)
            {
                outcome = CombatSimulationOutcome.Invalid;
                reason = CombatTerminationReason.EngineError;
            }
            State.Outcome = outcome;
            State.TerminationReason = reason;
            State.Phase = CombatSimulationPhase.Completed;
            if (outcome == CombatSimulationOutcome.Defeat)
            {
                CaptureTerminalDiagnostics(outcome);
            }
            var ended = Emit(
                CombatSimulationEventKind.BattleEnded,
                State.PlayerActorId,
                0,
                0,
                reason.ToString(),
                (int)outcome);
            terminalSettlementInProgress = true;
            try
            {
                NotifyExtension(ended);
                CompleteExtension();
            }
            finally
            {
                terminalSettlementInProgress = false;
            }
            ended.DefinitionId = State.TerminationReason.ToString();
            ended.Amount = (int)State.Outcome;
            ended.Message = terminalResolution.ToString();
            if (State.Outcome == CombatSimulationOutcome.Victory)
            {
                ApplyDeferredVictoryVariableChanges();
            }
            else
            {
                State.DeferredVictoryVariableChanges.Clear();
            }
        }

        private void TryOverridePhysicalDefeat(
            CombatSimulationOutcome outcome,
            CombatTerminationReason reason,
            bool explicitRule)
        {
            var player = State.Player;
            if (!terminalSettlementInProgress
                || !explicitRule
                || explicitRuleTermination
                || State.Outcome != CombatSimulationOutcome.Defeat
                || outcome != CombatSimulationOutcome.Victory
                || player?.Alive != true)
            {
                return;
            }

            explicitRuleTermination = true;
            terminalResolution =
                CombatTerminalResolution.ResurrectionEscapeOverride;
            State.Outcome = outcome;
            State.TerminationReason = reason;
            terminalPlayerHp = player.Hp;
            terminalLivingEnemyCount = State.LivingEnemies.Count();
            terminalConsistencyValid = true;
            terminalConsistencyReason = "";
            metrics.RuleTerminalOverrides++;
            failureDiagnostics.TerminalResolution =
                terminalResolution.ToString();
        }

        public static IReadOnlyList<CombatSimulationAction> BuildLegalActions(
            CombatScenarioDefinition scenario,
            CombatRuleset ruleset,
            CombatBattleState state)
        {
            return BuildInvocableActions(scenario, ruleset, state)
                .Where(action => action.PolicyEligible)
                .ToList();
        }

        public static IReadOnlyList<CombatSimulationAction> BuildInvocableActions(
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
            foreach (var instanceId in state.Hand)
            {
                var instance = state.FindCard(instanceId);
                if (instance == null
                    || !TryGetEffectiveCardCore(ruleset, instance, out var definition))
                {
                    continue;
                }
                if (HasTag(instance, definition, "Unusable"))
                {
                    continue;
                }
                var cost = Math.Max(0, definition.Cost + instance.CostModifier);
                if (cost > player.Energy)
                {
                    continue;
                }
                foreach (var targetActorId in ResolveCardTargetActorIds(
                             definition,
                             state,
                             player))
                {
                    AddInvocableAction(
                        result,
                        scenario,
                        state,
                        definition,
                        new CombatSimulationAction
                        {
                            CandidateId = TargetedCandidateId(
                                "card:" + instance.InstanceId,
                                targetActorId),
                            Kind = CombatSimulationActionKind.PlayCard,
                            ActorId = player.ActorId,
                            CardInstanceId = instance.InstanceId,
                            TargetActorId = targetActorId,
                            Cost = cost,
                            DefinitionId = definition.CardId
                        });
                }
            }
            foreach (var instanceId in state.SkillCards)
            {
                var instance = state.FindCard(instanceId);
                if (instance == null
                    || state.SkillCooldowns.TryGetValue(
                        instanceId,
                        out var cooldown)
                    && cooldown > 0
                    || !TryGetEffectiveCardCore(
                        ruleset,
                        instance,
                        out var definition)
                    || HasTag(instance, definition, "Unusable"))
                {
                    continue;
                }
                foreach (var targetActorId in ResolveCardTargetActorIds(
                             definition,
                             state,
                             player))
                {
                    AddInvocableAction(
                        result,
                        scenario,
                        state,
                        definition,
                        new CombatSimulationAction
                        {
                            CandidateId = TargetedCandidateId(
                                "skill:" + instance.InstanceId,
                                targetActorId),
                            Kind = CombatSimulationActionKind.UseSkill,
                            ActorId = player.ActorId,
                            CardInstanceId = instance.InstanceId,
                            TargetActorId = targetActorId,
                            Cost = 0,
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

        private static IReadOnlyList<int> ResolveCardTargetActorIds(
            CombatCardDefinition definition,
            CombatBattleState state,
            CombatActorState player)
        {
            var scope = definition.TargetScope;
            if (scope == CombatCardTargetScope.None)
            {
                scope = definition.RequiresEnemyTarget
                    ? CombatCardTargetScope.Enemy
                    : CombatCardTargetScope.None;
            }
            if (scope == CombatCardTargetScope.None)
            {
                return new[] { 0 };
            }

            var result = new List<int>();
            if ((scope & CombatCardTargetScope.Self) != 0)
            {
                result.Add(player.ActorId);
            }
            if ((scope & CombatCardTargetScope.Friendly) != 0)
            {
                result.AddRange(state.LivingFriendlies
                    .OrderBy(actor => actor.ActorId)
                    .Select(actor => actor.ActorId));
            }
            if ((scope & CombatCardTargetScope.Enemy) != 0)
            {
                result.AddRange(state.LivingEnemies
                    .OrderBy(actor => actor.ActorId)
                    .Select(actor => actor.ActorId));
            }
            return result;
        }

        private static string TargetedCandidateId(
            string baseCandidateId,
            int targetActorId)
        {
            return targetActorId > 0
                ? baseCandidateId + ":target:" + targetActorId
                : baseCandidateId;
        }

        private static void AddInvocableAction(
            ICollection<CombatSimulationAction> result,
            CombatScenarioDefinition scenario,
            CombatBattleState state,
            CombatCardDefinition definition,
            CombatSimulationAction action)
        {
            CombatActionContractEvaluator.Apply(
                action,
                CombatActionContractEvaluator.Evaluate(
                    scenario,
                    state,
                    definition,
                    action));
            result.Add(action);
        }

        private void AddInitialStatuses(
            CombatActorState actor,
            IEnumerable<CombatInitialStatus>? initialStatuses)
        {
            foreach (var initial in initialStatuses ?? Array.Empty<CombatInitialStatus>())
            {
                if (initial.ConditionExpression != null
                    && CombatSimulationExpressionEvaluator.Evaluate(
                        initial.ConditionExpression,
                        State,
                        ruleset,
                        actor.ActorId,
                        actor.ActorId) <= 0d)
                {
                    continue;
                }
                if (!ruleset.TryGetStatusCore(initial.StatusId, out var definition))
                {
                    AddUnsupported("status:" + initial.StatusId);
                    continue;
                }
                Reference("status:" + definition.StatusId, definition.Fidelity);
                var requestedStacks = initial.StacksExpression == null
                    ? initial.Stacks
                    : RoundEffectValue(
                        CombatSimulationExpressionEvaluator.Evaluate(
                            initial.StacksExpression,
                            State,
                            ruleset,
                            actor.ActorId,
                            actor.ActorId),
                        CombatSimulationValueRounding.Truncate);
                var stacks = Math.Min(
                    Math.Max(1, definition.MaximumStacks),
                    Math.Max(1, requestedStacks));
                var existing = actor.Statuses.FirstOrDefault(status =>
                    string.Equals(
                        status.StatusId,
                        definition.StatusId,
                        StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Stacks = Math.Min(
                        Math.Max(1, definition.MaximumStacks),
                        existing.Stacks + stacks);
                    existing.Duration = Math.Max(existing.Duration, initial.Duration);
                    continue;
                }
                actor.Statuses.Add(new CombatStatusState
                {
                    StatusId = definition.StatusId,
                    Stacks = stacks,
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
                enemy.PreviousIntentIds = enemy.CurrentIntentIds.Count > 0
                    ? new List<string>(enemy.CurrentIntentIds)
                    : string.IsNullOrWhiteSpace(enemy.CurrentIntentId)
                        ? new List<string>()
                        : new List<string> { enemy.CurrentIntentId };
                enemy.PreviousIntentId = enemy.PreviousIntentIds.FirstOrDefault() ?? "";
                enemy.CurrentIntentIds.Clear();
                var ratio = enemy.MaxHp <= 0 ? 0d : (double)enemy.Hp / enemy.MaxHp;
                var actionCount = Math.Min(
                    4,
                    Math.Max(
                        1,
                        definition.ActionCount
                        + (int)Math.Truncate(Variable(enemy, "ActionCountBonus", 0d))));
                for (var slot = 0; slot < actionCount; slot++)
                {
                    var eligible = definition.Intents
                        .Where(intent => State.Turn >= intent.MinimumTurn
                                         && State.Turn <= intent.MaximumTurn
                                         && ratio >= intent.MinimumHpRatio
                                         && ratio <= intent.MaximumHpRatio
                                         && IsIntentAvailable(enemy, intent)
                                         && !enemy.CurrentIntentIds.Contains(
                                             intent.IntentId,
                                             StringComparer.OrdinalIgnoreCase)
                                         && (!enemy.IntentCooldowns.TryGetValue(
                                                 intent.IntentId,
                                                 out var cooldown)
                                             || cooldown <= 0))
                        .ToList();
                    var withoutRepeat = eligible
                        .Where(intent => !intent.PreventConsecutiveUse
                                         || !enemy.PreviousIntentIds.Contains(
                                             intent.IntentId,
                                             StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    if (withoutRepeat.Count > 0)
                    {
                        eligible = withoutRepeat;
                    }
                    var maximumPriority = eligible.Count == 0
                        ? 0
                        : eligible.Max(intent => ResolveIntentPriority(enemy, intent));
                    if (maximumPriority != 0)
                    {
                        eligible = eligible
                            .Where(intent =>
                                ResolveIntentPriority(enemy, intent) == maximumPriority)
                            .ToList();
                    }
                    var totalWeight = eligible.Sum(intent => Math.Max(0, intent.Weight));
                    if (eligible.Count == 0 || totalWeight <= 0)
                    {
                        break;
                    }
                    var selectedValue = CombatDeterministicRandom.NextInt(
                        scenario.Seed,
                        State.Random,
                        "enemy.intent:" + enemy.InstanceKey + ":slot-" + slot,
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
                    enemy.CurrentIntentIds.Add(selected.IntentId);
                    var cooldownTurns = ResolveIntentCooldown(enemy, selected);
                    if (cooldownTurns > 0)
                    {
                        enemy.IntentCooldowns[selected.IntentId] = cooldownTurns + 1;
                    }
                    Emit(
                        CombatSimulationEventKind.IntentSelected,
                        enemy.ActorId,
                        State.PlayerActorId,
                        0,
                        selected.IntentId,
                        slot + 1,
                        selectedValue,
                        draw);
                }
                enemy.CurrentIntentId = enemy.CurrentIntentIds.FirstOrDefault() ?? "";
            }
            State.Phase = CombatSimulationPhase.PlayerTurnStart;
        }

        private bool IsIntentAvailable(
            CombatActorState enemy,
            CombatEnemyIntentDefinition intent)
        {
            return intent.AvailabilityExpression == null
                   || CombatSimulationExpressionEvaluator.Evaluate(
                       intent.AvailabilityExpression,
                       State,
                       ruleset,
                       enemy.ActorId,
                       State.PlayerActorId) > 0d;
        }

        private int ResolveIntentPriority(
            CombatActorState enemy,
            CombatEnemyIntentDefinition intent)
        {
            return intent.PriorityExpression == null
                ? intent.Priority
                : RoundEffectValue(
                    CombatSimulationExpressionEvaluator.Evaluate(
                        intent.PriorityExpression,
                        State,
                        ruleset,
                        enemy.ActorId,
                        State.PlayerActorId),
                    CombatSimulationValueRounding.Truncate);
        }

        private int ResolveIntentCooldown(
            CombatActorState enemy,
            CombatEnemyIntentDefinition intent)
        {
            var value = intent.CooldownExpression == null
                ? intent.CooldownTurns
                : RoundEffectValue(
                    CombatSimulationExpressionEvaluator.Evaluate(
                        intent.CooldownExpression,
                        State,
                        ruleset,
                        enemy.ActorId,
                        State.PlayerActorId),
                    CombatSimulationValueRounding.Truncate);
            return Math.Max(0, value);
        }

        private bool ExecuteEnemyIntent(CombatActorState enemy, string intentId)
        {
            if (!ruleset.TryGetEnemyCore(enemy.DefinitionId, out var definition))
            {
                AddUnsupported("enemy:" + enemy.DefinitionId);
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                return false;
            }
            var intent = ResolveEnemyIntent(definition, intentId);
            if (intent == null)
            {
                AddUnsupported(
                    "enemy-intent:"
                    + enemy.DefinitionId
                    + ":"
                    + intentId);
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.UnsupportedRule);
                return false;
            }

            State.ActionSequence++;
            ResetHpLossWindow();
            currentActionCommandCount = 0;
            currentActionDefinitionId = intent.IntentId;
            var queue = new Queue<CombatSimulationCommand>();
            var actionStarted = Emit(
                CombatSimulationEventKind.ActionStarted,
                enemy.ActorId,
                State.PlayerActorId,
                0,
                intent.IntentId,
                0);
            EnqueueTriggers(actionStarted, queue, 1);
            CompileEffects(
                intent.Effects,
                enemy.ActorId,
                State.PlayerActorId,
                0,
                null,
                null,
                0,
                queue);
            if (!ExecuteQueue(queue))
            {
                return false;
            }
            return ProcessLifecycleEvent(
                CombatSimulationEventKind.ActionResolved,
                enemy.ActorId,
                State.PlayerActorId,
                intent.IntentId,
                1);
        }

        private CombatEnemyIntentDefinition? ResolveEnemyIntent(
            CombatEnemyDefinition owner,
            string intentId)
        {
            return owner.Intents.FirstOrDefault(candidate =>
                       string.Equals(
                           candidate.IntentId,
                           intentId,
                           StringComparison.OrdinalIgnoreCase))
                   ?? ruleset.SnapshotEnemies()
                       .OrderBy(item => item.EnemyId, StringComparer.Ordinal)
                       .SelectMany(item => item.Intents)
                       .FirstOrDefault(candidate => string.Equals(
                           candidate.IntentId,
                           intentId,
                           StringComparison.OrdinalIgnoreCase));
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
            var effectList = effects.ToList();
            var selectedChoices =
                new Dictionary<string, CombatSimulationEffectDefinition>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (var group in effectList
                         .Where(item => !string.IsNullOrWhiteSpace(item.RandomChoiceGroup))
                         .GroupBy(item => item.RandomChoiceGroup, StringComparer.OrdinalIgnoreCase))
            {
                var candidates = group
                    .Where(item => item.RandomChoiceWeight > 0d)
                    .ToList();
                if (candidates.Count == 0)
                {
                    continue;
                }
                var totalWeight = candidates.Sum(item => item.RandomChoiceWeight);
                var roll = CombatDeterministicRandom.NextUnit(
                    scenario.Seed,
                    State.Random,
                    "effect.choice:" + sourceActorId + ":" + group.Key,
                    out var choiceDraw);
                TraceRandomDraw(choiceDraw, "effect random choice");
                var cursor = roll * totalWeight;
                var chosen = candidates[candidates.Count - 1];
                foreach (var candidate in candidates)
                {
                    cursor -= candidate.RandomChoiceWeight;
                    if (cursor < 0d)
                    {
                        chosen = candidate;
                        break;
                    }
                }
                selectedChoices[group.Key] = chosen;
            }

            foreach (var effect in effectList)
            {
                if (!string.IsNullOrWhiteSpace(effect.RandomChoiceGroup)
                    && (!selectedChoices.TryGetValue(effect.RandomChoiceGroup, out var chosen)
                        || !ReferenceEquals(chosen, effect)))
                {
                    continue;
                }
                if (effect.ConditionExpression != null
                    && CombatSimulationExpressionEvaluator.Evaluate(
                        effect.ConditionExpression,
                        State,
                        ruleset,
                        sourceActorId,
                        selectedTargetId) <= 0d)
                {
                    continue;
                }
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
                    || effect.Kind == CombatSimulationEffectKind.CreateCard
                     || effect.Kind == CombatSimulationEffectKind.DrawToHandLimit
                     || effect.Kind == CombatSimulationEffectKind.CreateRandomCard
                     || effect.Kind == CombatSimulationEffectKind.AddCardTag
                     || effect.Kind == CombatSimulationEffectKind.RetrieveCards
                     || effect.Kind == CombatSimulationEffectKind.EqualizeHealthByStatus
                    || effect.Kind == CombatSimulationEffectKind.ModifyStatusCounter
                    || effect.Kind == CombatSimulationEffectKind.WinBattle
                    || effect.Kind == CombatSimulationEffectKind.EmitEvent)
                {
                    if (targets.Count == 0)
                    {
                        targets.Add(sourceActorId);
                    }
                }
                foreach (var targetId in targets)
                {
                    var expressionAmount = effect.AmountExpression == null
                        ? effect.Amount
                        : RoundEffectValue(
                            CombatSimulationExpressionEvaluator.Evaluate(
                                effect.AmountExpression,
                                State,
                                ruleset,
                                sourceActorId,
                                targetId),
                            effect.Rounding);
                    var scaledAmount = expressionAmount
                                       * (effect.ScaleWithStatusStacks
                                           ? Math.Max(1, statusStacks)
                                           : 1);
                    queue.Enqueue(new CombatSimulationCommand
                    {
                        Kind = effect.Kind,
                        SourceActorId = sourceActorId,
                        TargetActorId = targetId,
                        CardInstanceId = cardInstanceId,
                        Amount = effect.Kind == CombatSimulationEffectKind.AddStatus
                                 || effect.Kind == CombatSimulationEffectKind.ChangeCardCost
                                 || effect.Kind == CombatSimulationEffectKind.ModifyVariable
                                 || effect.Kind == CombatSimulationEffectKind.ModifyVariablePercent
                                 || effect.Kind == CombatSimulationEffectKind.ScaleVariablePercent
                                 || effect.Kind == CombatSimulationEffectKind.DeferVariableUntilVictory
                                 || effect.Kind == CombatSimulationEffectKind.GainEnergy
                            ? scaledAmount
                            : Math.Max(
                                0,
                                scaledAmount),
                        DefinitionId = effect.DefinitionId ?? "",
                        SecondaryDefinitionId = effect.SecondaryDefinitionId ?? "",
                        CounterKey = effect.CounterKey ?? "",
                        RequiredStatusTag = effect.RequiredStatusTag ?? "",
                        RequiredCardTag = effect.RequiredCardTag ?? "",
                        MinimumRarity = Math.Max(1, effect.MinimumRarity),
                        MaximumRarity = Math.Max(effect.MinimumRarity, effect.MaximumRarity),
                        CounterLimit = effect.CounterLimit,
                        RemoveStatusAtCounterLimit = effect.RemoveStatusAtCounterLimit,
                        EmittedEventKind = effect.EmittedEventKind,
                        DestinationZone = effect.DestinationZone,
                        SourceZone = effect.SourceZone,
                        UseEventCard = effect.UseEventCard,
                        RandomizeDestination = effect.RandomizeDestination,
                        Duration = Math.Max(0, effect.Duration),
                        PersistAcrossBattles = effect.PersistAcrossBattles,
                        MinimumVariableValue = effect.MinimumVariableValue,
                        MaximumVariableValue = Math.Max(
                            effect.MinimumVariableValue,
                            effect.MaximumVariableValue),
                        ParentSequence = parent?.Sequence ?? triggerEvent?.Sequence ?? 0,
                        CausalChainId = parent?.CausalChainId
                                        ?? triggerEvent?.CausalChainId
                                        ?? 0,
                        HandlerId = parent?.HandlerId
                                    ?? triggerEvent?.HandlerId
                                    ?? "",
                        SourceRewardId = parent?.SourceRewardId
                                         ?? triggerEvent?.SourceRewardId
                                         ?? "",
                        SourceActionId = parent?.SourceActionId
                                         ?? triggerEvent?.SourceActionId
                                         ?? State.ActionSequence,
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
                case CombatSimulationTarget.AllAllies:
                {
                    var source = State.FindActor(sourceActorId);
                    return source == null
                        ? new List<int>()
                        : State.Actors
                            .Where(actor => actor.Alive && AreAllies(source, actor))
                            .OrderBy(actor => actor.ActorId)
                            .Select(actor => actor.ActorId)
                            .ToList();
                }
                case CombatSimulationTarget.AllAlliesExceptSelf:
                {
                    var source = State.FindActor(sourceActorId);
                    return source == null
                        ? new List<int>()
                        : State.Actors
                            .Where(actor =>
                                actor.Alive
                                && actor.ActorId != sourceActorId
                                && AreAllies(source, actor))
                            .OrderBy(actor => actor.ActorId)
                            .Select(actor => actor.ActorId)
                            .ToList();
                }
                case CombatSimulationTarget.AllOpponents:
                {
                    var source = State.FindActor(sourceActorId);
                    return source == null
                        ? new List<int>()
                        : State.Actors
                            .Where(actor => actor.Alive && !AreAllies(source, actor))
                            .OrderBy(actor => actor.ActorId)
                            .Select(actor => actor.ActorId)
                            .ToList();
                }
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
                var pending = queue.Peek();
                RememberCommand(pending);
                if (State.CommandCount >= limits.MaximumCommands)
                {
                    CaptureCommandFailure("battle-total", pending);
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.MaximumCommands);
                    return false;
                }
                if (++currentActionCommandCount > limits.MaximumCommandsPerAction)
                {
                    CaptureCommandFailure("single-action", pending);
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.MaximumCommands);
                    return false;
                }
                var command = queue.Dequeue();
                if (command.TriggerWave > limits.MaximumTriggerWavesPerAction)
                {
                    CaptureCommandFailure("trigger-wave", command);
                    Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.TriggerLoop);
                    return false;
                }
                State.CommandCount++;
                secondaryCommandEvents.Clear();
                var emitted = ExecuteCommand(command);
                if (emitted != null)
                {
                    EnqueueCardLifecycleEffects(
                        emitted,
                        queue,
                        command.TriggerWave + 1);
                    EnqueueTriggers(emitted, queue, command.TriggerWave + 1);
                    if (emitted.Kind == CombatSimulationEventKind.DamageDealt
                        && emitted.Amount > 0)
                    {
                        var damagedActor = State.FindActor(emitted.TargetActorId);
                        if (damagedActor != null)
                        {
                            ReduceStatuses(
                                damagedActor,
                                definition => definition.ReducePerAttacked);
                        }
                    }
                }
                foreach (var secondaryCommandEvent in
                         secondaryCommandEvents.ToArray())
                {
                    EnqueueCardLifecycleEffects(
                        secondaryCommandEvent,
                        queue,
                        command.TriggerWave + 1);
                    EnqueueTriggers(secondaryCommandEvent, queue, command.TriggerWave + 1);
                }
            }
            if (queue.Count == 0
                && State.Outcome == CombatSimulationOutcome.None)
            {
                CheckOutcome();
            }
            return State.Outcome == CombatSimulationOutcome.None
                   || State.Outcome == CombatSimulationOutcome.Victory;
        }

        private void RememberCommand(CombatSimulationCommand command)
        {
            recentCommands.Enqueue(CommandDescription(command));
            while (recentCommands.Count > 16)
            {
                recentCommands.Dequeue();
            }
        }

        private void CaptureCommandFailure(
            string scope,
            CombatSimulationCommand pending)
        {
            failureDiagnostics = new CombatSimulationFailureDiagnostics
            {
                LimitScope = scope,
                Turn = State.Turn,
                ActionSequence = State.ActionSequence,
                TotalCommandCount = State.CommandCount,
                ActionCommandCount = currentActionCommandCount,
                ActionDefinitionId = string.IsNullOrWhiteSpace(
                    currentActionDefinitionId)
                    ? !string.IsNullOrWhiteSpace(pending.SourceRewardId)
                        ? pending.SourceRewardId
                        : pending.DefinitionId
                    : currentActionDefinitionId,
                PendingCommand = CommandDescription(pending),
                CausalChainId = pending.CausalChainId,
                HandlerId = pending.HandlerId,
                SourceRewardId = pending.SourceRewardId,
                SourceActionId = pending.SourceActionId,
                RecentCommands = recentCommands.ToList(),
                StateSummary = BuildFailureStateSummary()
            };
        }

        private void CaptureActionFailure()
        {
            failureDiagnostics = new CombatSimulationFailureDiagnostics
            {
                LimitScope = "battle-actions",
                Turn = State.Turn,
                ActionSequence = State.ActionSequence,
                TotalCommandCount = State.CommandCount,
                ActionCommandCount = currentActionCommandCount,
                ActionDefinitionId = currentActionDefinitionId,
                PendingCommand = "policy-decision",
                RecentCommands = recentCommands.ToList(),
                StateSummary = BuildFailureStateSummary()
            };
        }

        private void CaptureTerminalDiagnostics(CombatSimulationOutcome outcome)
        {
            failureDiagnostics.TerminalOutcome = outcome.ToString();
            failureDiagnostics.TerminalResolution = terminalResolution.ToString();
            failureDiagnostics.Turn = State.Turn;
            failureDiagnostics.ActionSequence = State.ActionSequence;
            failureDiagnostics.TotalCommandCount = State.CommandCount;
            failureDiagnostics.ActionCommandCount = currentActionCommandCount;
            failureDiagnostics.ActionDefinitionId =
                currentActionDefinitionId;
            failureDiagnostics.RecentCommands = recentCommands.ToList();
            failureDiagnostics.RecentEvents = Events
                .Skip(Math.Max(0, Events.Count - 16))
                .Select(EventDescription)
                .ToList();
            failureDiagnostics.StateSummary = BuildFailureStateSummary();
        }

        private static string EventDescription(CombatSimulationEvent item)
        {
            return item.Sequence
                   + ":"
                   + item.Kind
                   + ":source="
                   + item.SourceActorId
                   + ":target="
                   + item.TargetActorId
                   + ":definition="
                   + (item.DefinitionId ?? "")
                   + ":amount="
                   + item.Amount
                   + ":message="
                   + (item.Message ?? "");
        }

        private List<string> BuildFailureStateSummary()
        {
            var result = new List<string>
            {
                "zones:hand="
                + State.Hand.Count
                + ",draw="
                + State.DrawPile.Count
                + ",discard="
                + State.DiscardPile.Count
                + ",exhaust="
                + State.ExhaustPile.Count
            };
            foreach (var actor in State.Actors.OrderBy(item => item.ActorId))
            {
                result.Add(
                    "actor:"
                    + actor.ActorId
                    + ":"
                    + actor.DefinitionId
                    + ":hp="
                    + actor.Hp
                    + "/"
                    + actor.MaxHp
                    + ":energy="
                    + actor.Energy
                    + ":intent="
                    + actor.CurrentIntentId
                    + ":statuses="
                    + string.Join(
                        ",",
                        actor.Statuses.Select(status =>
                            status.StatusId + "=" + status.Stacks)));
            }
            foreach (var instanceId in State.Hand)
            {
                var card = State.FindCard(instanceId);
                if (card == null)
                {
                    continue;
                }
                result.Add(
                    "hand:"
                    + card.InstanceId
                    + ":"
                    + card.CardId
                    + ":apparent="
                    + card.ApparentCardId
                    + ":cost="
                    + card.CostModifier
                    + ":fake="
                    + card.IsVisibleFake
                    + ":tags="
                    + string.Join(",", card.Tags));
            }
            return result;
        }

        private static string CommandDescription(
            CombatSimulationCommand command)
        {
            return command.Kind
                   + ":"
                   + (command.DefinitionId ?? "")
                   + ":source="
                   + command.SourceActorId
                   + ":target="
                   + command.TargetActorId
                   + ":card="
                   + command.CardInstanceId
                   + ":wave="
                   + command.TriggerWave
                   + ":chain="
                   + command.CausalChainId
                   + ":handler="
                   + (command.HandlerId ?? "")
                   + ":reward="
                   + (command.SourceRewardId ?? "")
                   + ":action="
                   + command.SourceActionId;
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
                case CombatSimulationEffectKind.DirectHpLoss:
                    if (target == null || !target.Alive)
                    {
                        return null;
                    }
                    var damage = CombatDamageResolver.Resolve(
                        source,
                        target,
                        ruleset,
                        command.Kind,
                        command.Amount,
                        command.DefinitionId);
                    var blocked = damage.BlockedAmount;
                    target.Block -= blocked;
                    var hpDamage = LimitHpLoss(
                        target,
                        damage.UnboundedHpDamage);
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
                    damageEvent.Message = command.Kind.ToString();
                    if (target.Hp <= 0)
                    {
                        secondaryCommandEvents.Add(Emit(
                            CombatSimulationEventKind.ActorDefeated,
                            command.SourceActorId,
                            target.ActorId,
                            command.CardInstanceId,
                            target.DefinitionId,
                            1,
                            damageEvent.Sequence));
                    }
                    return damageEvent;

                case CombatSimulationEffectKind.GainBlock:
                    if (target == null || !target.Alive) return null;
                    var blockAmount = Math.Max(
                        0,
                        WitchRounded(
                            command.Amount
                            * Variable(target, "DefendPercent", 1d)
                            * (target.Kind == CombatSimulationActorKind.Player
                                ? 1d + Math.Max(0d, Variable(target, "Perceive", 0d)) * 0.04d
                                : 1d)));
                    target.Block += blockAmount;
                    if (target.Kind == CombatSimulationActorKind.Player)
                    {
                        metrics.BlockGained += blockAmount;
                    }
                    return EmitFromCommand(
                        CombatSimulationEventKind.BlockGained,
                        command,
                        blockAmount,
                        beforeHash);

                case CombatSimulationEffectKind.SetBlock:
                    if (target == null || !target.Alive) return null;
                    var previousBlock = target.Block;
                    target.Block = Math.Max(0, command.Amount);
                    return EmitFromCommand(
                        CombatSimulationEventKind.BlockChanged,
                        command,
                        target.Block - previousBlock,
                        beforeHash);

                case CombatSimulationEffectKind.Heal:
                    if (target == null || !target.Alive) return null;
                    var modifiedHealing = Math.Max(
                        0,
                        (int)Math.Round(command.Amount * Variable(target, "HealMultiplier", 1d)));
                    var missingHp = Math.Max(0, target.MaxHp - target.Hp);
                    var healing = Math.Min(
                        modifiedHealing,
                        missingHp);
                    var overflowHealing = Math.Max(0, modifiedHealing - missingHp);
                    target.Hp += healing;
                    if (target.Kind == CombatSimulationActorKind.Player)
                    {
                        metrics.Healing += healing;
                    }
                    var healEvent = EmitFromCommand(
                        CombatSimulationEventKind.Healed,
                        command,
                        healing,
                        beforeHash);
                    var conversionRate = Math.Max(
                        0d,
                        Variable(target, "ConversionRate", 0d));
                    var convertedBase = (int)(overflowHealing * conversionRate);
                    var convertedBlock = Math.Max(
                        0,
                        WitchRounded(
                            convertedBase
                            * Variable(target, "DefendPercent", 1d)
                            * (target.Kind == CombatSimulationActorKind.Player
                                ? 1d + Math.Max(
                                    0d,
                                    Variable(target, "Perceive", 0d)) * 0.04d
                                : 1d)));
                    if (convertedBlock > 0)
                    {
                        target.Block += convertedBlock;
                        if (target.Kind == CombatSimulationActorKind.Player)
                        {
                            metrics.BlockGained += convertedBlock;
                        }
                        secondaryCommandEvents.Add(Emit(
                            CombatSimulationEventKind.BlockGained,
                            command.SourceActorId,
                            target.ActorId,
                            command.CardInstanceId,
                            command.DefinitionId,
                            convertedBlock,
                            healEvent.Sequence));
                    }
                    return healEvent;

                case CombatSimulationEffectKind.SetHp:
                    if (target == null || !target.Alive) return null;
                    // Witch ScriptExecutor.SetHp assigns CurHp directly. It is
                    // intentionally neither healing nor damage and therefore
                    // must not emit either lifecycle event.
                    var previousSetHp = target.Hp;
                    target.Hp = command.Amount;
                    var setHpEvent = EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        target.Hp - previousSetHp,
                        beforeHash);
                    setHpEvent.Message = "Hp";
                    return setHpEvent;

                case CombatSimulationEffectKind.SetHpToMax:
                    if (target == null) return null;
                    var previousMaxHp = target.Hp;
                    target.Hp = Math.Max(1, target.MaxHp);
                    var setMaxHpEvent = EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        target.Hp - previousMaxHp,
                        beforeHash);
                    setMaxHpEvent.Message = "Hp";
                    return setMaxHpEvent;

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
                    var previousEnergy = target.Energy;
                    target.Energy = Math.Max(0, target.Energy + command.Amount);
                    return EmitFromCommand(
                        CombatSimulationEventKind.EnergyChanged,
                        command,
                        target.Energy - previousEnergy,
                        beforeHash);

                case CombatSimulationEffectKind.SetEnergy:
                    if (target == null || !target.Alive) return null;
                    var energyBeforeSet = target.Energy;
                    target.Energy = Math.Max(0, command.Amount);
                    return EmitFromCommand(
                        CombatSimulationEventKind.EnergyChanged,
                        command,
                        target.Energy - energyBeforeSet,
                        beforeHash);

                case CombatSimulationEffectKind.DrawToHandLimit:
                    DrawCards(
                        Math.Max(0, scenario.HandLimit - State.Hand.Count),
                        command.TargetActorId,
                        command.ParentSequence);
                    return null;

                case CombatSimulationEffectKind.CreateRandomCard:
                {
                    var allowCrossRoleSkill =
                        AllowsCrossRoleSkill(command.SourceRewardId);
                    var candidates = ruleset.SnapshotCards()
                        .Where(card => card.Rarity >= command.MinimumRarity
                                       && card.Rarity <= command.MaximumRarity
                                       && (string.IsNullOrWhiteSpace(command.DefinitionId)
                                            || card.Tags.Contains(
                                                command.DefinitionId,
                                                StringComparer.OrdinalIgnoreCase))
                                       && CanEnterDynamicCardPool(
                                           card.CardId,
                                           allowCrossRoleSkill))
                        .OrderBy(card => card.CardId, StringComparer.Ordinal)
                        .ToList();
                    if (candidates.Count == 0)
                    {
                        AddUnsupported(
                            "random-card:"
                            + command.MinimumRarity
                            + "-"
                            + command.MaximumRarity
                            + ":"
                            + command.DefinitionId);
                        Terminate(
                            CombatSimulationOutcome.Invalid,
                            CombatTerminationReason.UnsupportedRule);
                        return null;
                    }
                    CombatSimulationEvent? lastRandomCreatedEvent = null;
                    for (var createdIndex = 0;
                         createdIndex < Math.Max(1, command.Amount);
                         createdIndex++)
                    {
                        var selectedIndex = CombatDeterministicRandom.NextInt(
                            scenario.Seed,
                            State.Random,
                            "card.create.random:"
                            + command.MinimumRarity
                            + ":"
                            + command.MaximumRarity
                            + ":"
                            + command.DefinitionId,
                            candidates.Count,
                            out var selectionDraw);
                        TraceRandomDraw(selectionDraw, "random card definition");
                        var randomCardDefinition = candidates[selectedIndex];
                        Reference(
                            "card:" + randomCardDefinition.CardId,
                            randomCardDefinition.Fidelity);
                        var card = new CombatCardInstanceState
                        {
                            InstanceId = State.NextCardInstanceId++,
                            CardId = randomCardDefinition.CardId,
                            CreationSource = "effect-random-card",
                            CreationSourceId = command.SourceRewardId,
                            CreationParentInstanceId =
                                command.CardInstanceId,
                            CreationRandomStreamId =
                                selectionDraw.StreamId,
                            CreationCrossRoleSkillAuthorized =
                                allowCrossRoleSkill
                        };
                        State.Cards.Add(card);
                        lastRandomCreatedEvent = PlaceCreatedCard(card, command);
                    }
                    return lastRandomCreatedEvent;
                }

                case CombatSimulationEffectKind.AddCardTag:
                {
                    if (string.IsNullOrWhiteSpace(command.DefinitionId))
                    {
                        return null;
                    }
                    var affectedIds = command.UseEventCard && command.CardInstanceId > 0
                        ? new List<int> { command.CardInstanceId }
                        : CardZone(command.SourceZone).ToList();
                    CombatSimulationEvent? firstTagEvent = null;
                    foreach (var instanceId in affectedIds)
                    {
                        var instance = State.FindCard(instanceId);
                        if (instance == null
                            || instance.Tags.Contains(
                                command.DefinitionId,
                                StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        instance.Tags.Add(command.DefinitionId);
                        var tagEvent = Emit(
                            CombatSimulationEventKind.CardTagChanged,
                            command.SourceActorId,
                            command.TargetActorId,
                            instance.InstanceId,
                            command.DefinitionId,
                            1,
                            command.ParentSequence);
                        if (firstTagEvent == null)
                        {
                            firstTagEvent = tagEvent;
                        }
                        else
                        {
                            secondaryCommandEvents.Add(tagEvent);
                        }
                    }
                    return firstTagEvent;
                }

                case CombatSimulationEffectKind.RetrieveCards:
                {
                    if (target?.Kind != CombatSimulationActorKind.Player)
                    {
                        return null;
                    }
                    var sourceZone = CardZone(command.SourceZone);
                    var candidates = sourceZone
                        .Select(State.FindCard)
                        .Where(instance => instance != null
                                           && ruleset.TryGetCardCore(
                                               instance.CardId,
                                               out var definition)
                                           && (string.IsNullOrWhiteSpace(command.RequiredCardTag)
                                               || HasTag(
                                                   instance,
                                                   definition,
                                                   command.RequiredCardTag)))
                        .Select(instance => instance!)
                        .OrderByDescending(instance =>
                            TryGetEffectiveCardCore(ruleset, instance, out var definition)
                                ? definition.Rarity
                                : 0)
                        .ThenBy(instance =>
                            TryGetEffectiveCardCore(ruleset, instance, out var definition)
                                ? definition.Cost + instance.CostModifier
                                : int.MaxValue)
                        .ThenBy(instance => instance.CardId, StringComparer.Ordinal)
                        .ThenBy(instance => instance.InstanceId)
                        .Take(Math.Max(0, command.Amount))
                        .ToList();
                    if (command.DestinationZone == CombatCardZone.Hand)
                    {
                        candidates = candidates
                            .Take(Math.Max(0, scenario.HandLimit - State.Hand.Count))
                            .ToList();
                    }
                    CombatSimulationEvent? firstRetrievedEvent = null;
                    foreach (var instance in candidates)
                    {
                        sourceZone.Remove(instance.InstanceId);
                        var destinationZone = CardZone(command.DestinationZone);
                        destinationZone.Add(instance.InstanceId);
                        var eventKind = command.DestinationZone switch
                        {
                            CombatCardZone.Hand => CombatSimulationEventKind.CardDrawn,
                            CombatCardZone.DiscardPile =>
                                CombatSimulationEventKind.CardDiscarded,
                            CombatCardZone.ExhaustPile =>
                                CombatSimulationEventKind.CardExhausted,
                            _ => CombatSimulationEventKind.CardCreated
                        };
                        if (command.DestinationZone == CombatCardZone.Hand)
                        {
                            metrics.CardsDrawn++;
                        }
                        var retrievedEvent = Emit(
                            eventKind,
                            command.SourceActorId,
                            command.TargetActorId,
                            instance.InstanceId,
                            instance.CardId,
                            1,
                            command.ParentSequence);
                        if (firstRetrievedEvent == null)
                        {
                            firstRetrievedEvent = retrievedEvent;
                        }
                        else
                        {
                            secondaryCommandEvents.Add(retrievedEvent);
                        }
                    }
                    return firstRetrievedEvent;
                }

                case CombatSimulationEffectKind.EqualizeHealthByStatus:
                {
                    var linked = State.Actors
                        .Where(actor => actor.Alive
                                        && actor.Statuses.Any(status =>
                                            string.Equals(
                                                status.StatusId,
                                                command.DefinitionId,
                                                StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(actor => actor.ActorId)
                        .ToList();
                    if (linked.Count <= 1)
                    {
                        return null;
                    }
                    var equalizedHp = Math.Min(
                        linked.Min(actor => actor.MaxHp),
                        linked.Sum(actor => actor.Hp) / linked.Count);
                    foreach (var actor in linked)
                    {
                        actor.Hp = equalizedHp;
                    }
                    return EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        equalizedHp,
                        beforeHash);
                }

                case CombatSimulationEffectKind.ModifyStatusCounter:
                {
                    if (target == null || string.IsNullOrWhiteSpace(command.CounterKey))
                    {
                        return null;
                    }
                    var statuses = target.Statuses
                        .Where(status =>
                            (string.IsNullOrWhiteSpace(command.DefinitionId)
                             || string.Equals(
                                 status.StatusId,
                                 command.DefinitionId,
                                 StringComparison.OrdinalIgnoreCase))
                            && (string.IsNullOrWhiteSpace(command.RequiredStatusTag)
                                || (ruleset.TryGetStatusCore(
                                        status.StatusId,
                                        out var statusDefinition)
                                    && statusDefinition.Tags.Contains(
                                        command.RequiredStatusTag,
                                        StringComparer.OrdinalIgnoreCase))))
                        .ToList();
                    foreach (var status in statuses)
                    {
                        var currentCounterValue = status.TriggerCounts.TryGetValue(
                            command.CounterKey,
                            out var stored)
                            ? stored
                            : 0;
                        var next = currentCounterValue + command.Amount;
                        status.TriggerCounts[command.CounterKey] = next;
                        if (command.RemoveStatusAtCounterLimit
                            && next >= command.CounterLimit)
                        {
                            target.Statuses.Remove(status);
                        }
                    }
                    command.DefinitionId =
                        (string.IsNullOrWhiteSpace(command.DefinitionId)
                            ? command.RequiredStatusTag
                            : command.DefinitionId)
                        + "|"
                        + command.CounterKey;
                    return statuses.Count > 0
                        ? EmitFromCommand(
                            CombatSimulationEventKind.VariableChanged,
                            command,
                            command.Amount,
                            beforeHash)
                        : null;
                }

                case CombatSimulationEffectKind.WinBattle:
                    TerminateCore(
                        CombatSimulationOutcome.Victory,
                        CombatTerminationReason.Victory,
                        explicitRule: true);
                    return null;

                case CombatSimulationEffectKind.EmitEvent:
                    return EmitFromCommand(
                        command.EmittedEventKind,
                        command,
                        command.Amount,
                        beforeHash);

                case CombatSimulationEffectKind.SkipTurn:
                    if (target == null || !target.Alive) return null;
                    target.Variables["SkipCurrentTurn"] = 1d;
                    return EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        1,
                        beforeHash);

                case CombatSimulationEffectKind.AddStatus:
                    if (target == null || !target.Alive)
                    {
                        // A prior command in the same authoritative effect
                        // queue may have defeated the target. Native gameplay
                        // treats the remaining status mutation as a no-op.
                        return null;
                    }
                    if (!ruleset.TryGetStatusCore(
                            command.DefinitionId,
                            out var statusDefinition))
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
                    var appliedStackDelta = 0;
                    if (existing == null)
                    {
                        if (command.Amount <= 0)
                        {
                            return null;
                        }
                        var addedStacks = Math.Min(
                            Math.Max(1, statusDefinition.MaximumStacks),
                            command.Amount);
                        target.Statuses.Add(new CombatStatusState
                        {
                            StatusId = command.DefinitionId,
                            Stacks = addedStacks,
                            Duration = command.Duration,
                            SourceActorId = command.SourceActorId,
                            LastStackGainActionId = command.SourceActionId,
                            StacksGainedInLastAction = Math.Max(0, addedStacks)
                        });
                        appliedStackDelta = addedStacks;
                    }
                    else
                    {
                        var previousStacks = existing.Stacks;
                        var nextStacks = Math.Min(
                            Math.Max(1, statusDefinition.MaximumStacks),
                            existing.Stacks + command.Amount);
                        if (nextStacks <= 0)
                        {
                            target.Statuses.Remove(existing);
                            return EmitFromCommand(
                                CombatSimulationEventKind.StatusRemoved,
                                command,
                                Math.Abs(command.Amount),
                                beforeHash);
                        }
                        existing.Stacks = nextStacks;
                        appliedStackDelta = nextStacks - previousStacks;
                        existing.Duration = Math.Max(existing.Duration, command.Duration);
                        var gainedStacks = Math.Max(0, nextStacks - previousStacks);
                        if (gainedStacks > 0)
                        {
                            if (existing.LastStackGainActionId
                                == command.SourceActionId)
                            {
                                existing.StacksGainedInLastAction +=
                                    gainedStacks;
                            }
                            else
                            {
                                existing.LastStackGainActionId =
                                    command.SourceActionId;
                                existing.StacksGainedInLastAction =
                                    gainedStacks;
                            }
                        }
                    }
                    if (appliedStackDelta == 0)
                    {
                        return null;
                    }
                    return EmitFromCommand(
                        CombatSimulationEventKind.StatusAdded,
                        command,
                        appliedStackDelta,
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
                    CombatSimulationEvent? lastCreatedEvent = null;
                    for (var createdIndex = 0;
                         createdIndex < Math.Max(1, command.Amount);
                         createdIndex++)
                    {
                        var card = new CombatCardInstanceState
                        {
                            InstanceId = State.NextCardInstanceId++,
                            CardId = cardDefinition.CardId,
                            CreationSource = "effect-create-card",
                            CreationSourceId = command.SourceRewardId,
                            CreationParentInstanceId =
                                command.CardInstanceId,
                            CreationRandomStreamId =
                                command.RandomStreamId
                        };
                        State.Cards.Add(card);
                        lastCreatedEvent = PlaceCreatedCard(card, command);
                    }
                    return lastCreatedEvent;

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

                case CombatSimulationEffectKind.ModifyVariable:
                    if (target == null || string.IsNullOrWhiteSpace(command.DefinitionId))
                    {
                        return null;
                    }
                    var variableBefore = Variable(target, command.DefinitionId, 0d);
                    var variableAfter = Math.Max(
                        command.MinimumVariableValue,
                        Math.Min(
                            command.MaximumVariableValue,
                            variableBefore + command.Amount));
                    target.Variables[command.DefinitionId] = variableAfter;
                    var actualVariableDelta = WitchRounded(variableAfter - variableBefore);
                    if (command.PersistAcrossBattles
                        && target.Kind == CombatSimulationActorKind.Player)
                    {
                        persistentVariableDeltas[command.DefinitionId] =
                            persistentVariableDeltas.TryGetValue(
                                command.DefinitionId,
                                out var persistentCurrent)
                                ? persistentCurrent + actualVariableDelta
                                : actualVariableDelta;
                    }
                    return EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        actualVariableDelta,
                        beforeHash);

                case CombatSimulationEffectKind.ModifyVariablePercent:
                    if (target == null || string.IsNullOrWhiteSpace(command.DefinitionId))
                    {
                        return null;
                    }
                    target.Variables[command.DefinitionId] =
                        Variable(target, command.DefinitionId, 1d)
                        + command.Amount / 100d;
                    return EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        command.Amount,
                        beforeHash);

                case CombatSimulationEffectKind.ScaleVariablePercent:
                    if (target == null || string.IsNullOrWhiteSpace(command.DefinitionId))
                    {
                        return null;
                    }
                    var storedVariableBefore = target.Variables.TryGetValue(
                        command.DefinitionId,
                        out var storedVariable)
                        ? storedVariable
                        : 1d;
                    var storedVariableAfter = Math.Max(
                        command.MinimumVariableValue,
                        Math.Min(
                            command.MaximumVariableValue,
                            storedVariableBefore * command.Amount / 100d));
                    target.Variables[command.DefinitionId] = storedVariableAfter;
                    return EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        WitchRounded(storedVariableAfter - storedVariableBefore),
                        beforeHash);

                case CombatSimulationEffectKind.ScaleMaxHpPercent:
                    if (target == null)
                    {
                        return null;
                    }
                    var maxHpBefore = target.MaxHp;
                    target.MaxHp = Math.Max(
                        1,
                        (int)((long)target.MaxHp * Math.Max(0, command.Amount) / 100L));
                    target.Hp = Math.Min(target.Hp, target.MaxHp);
                    return EmitFromCommand(
                        CombatSimulationEventKind.VariableChanged,
                        command,
                        target.MaxHp - maxHpBefore,
                        beforeHash);

                case CombatSimulationEffectKind.DeferVariableUntilVictory:
                    if (target == null || string.IsNullOrWhiteSpace(command.DefinitionId))
                    {
                        return null;
                    }
                    State.DeferredVictoryVariableChanges.Add(
                        new CombatDeferredVariableChangeState
                        {
                            ActorId = target.ActorId,
                            DefinitionId = command.DefinitionId,
                            Amount = command.Amount,
                            PersistAcrossBattles = command.PersistAcrossBattles,
                            MinimumVariableValue = command.MinimumVariableValue,
                            MaximumVariableValue = command.MaximumVariableValue
                        });
                    return EmitFromCommand(
                        CombatSimulationEventKind.DeferredEffectTriggered,
                        command,
                        command.Amount,
                        beforeHash);

                case CombatSimulationEffectKind.CopyStatuses:
                {
                    if (source == null || target == null)
                    {
                        return null;
                    }
                    var copied = 0;
                    foreach (var status in target.Statuses.ToList())
                    {
                        if (!ruleset.TryGetStatusCore(status.StatusId, out var copiedDefinition))
                        {
                            AddUnsupported("status:" + status.StatusId);
                            continue;
                        }
                        Reference("status:" + copiedDefinition.StatusId, copiedDefinition.Fidelity);
                        var copiedExisting = source.Statuses.FirstOrDefault(item =>
                            string.Equals(
                                item.StatusId,
                                status.StatusId,
                                StringComparison.OrdinalIgnoreCase));
                        if (copiedExisting == null)
                        {
                            source.Statuses.Add(new CombatStatusState
                            {
                                StatusId = status.StatusId,
                                Stacks = Math.Min(
                                    Math.Max(1, copiedDefinition.MaximumStacks),
                                    Math.Max(1, status.Stacks)),
                                SourceActorId = source.ActorId
                            });
                        }
                        else
                        {
                            copiedExisting.Stacks = Math.Min(
                                Math.Max(1, copiedDefinition.MaximumStacks),
                                copiedExisting.Stacks + Math.Max(1, status.Stacks));
                        }
                        copied++;
                    }
                    return copied > 0
                        ? EmitFromCommand(
                            CombatSimulationEventKind.StatusAdded,
                            command,
                            copied,
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
                            Block = Math.Max(0, enemyDefinition.InitialBlock),
                            Variables = source?.Variables == null
                                ? new Dictionary<string, double>(
                                    StringComparer.OrdinalIgnoreCase)
                                : new Dictionary<string, double>(
                                    source.Variables,
                                    StringComparer.OrdinalIgnoreCase)
                        };
                        State.Actors.Add(summoned);
                        AddInitialStatuses(summoned, enemyDefinition.InitialStatuses);
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
            NotifyExtension(sourceEvent);
            var matches = new List<TriggerMatch>();
            foreach (var actor in State.Actors
                         .Where(actor => actor.Alive
                                         || sourceEvent.Kind
                                         == CombatSimulationEventKind.ActorDefeated)
                         .OrderBy(actor => actor.ActorId))
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
                        if (!MatchesOwnerRelation(actor, trigger, sourceEvent)
                            || status.Stacks < trigger.MinimumStacks
                            || status.Stacks > trigger.MaximumStacks
                            || sourceEvent.Amount < trigger.MinimumEventAmount
                            || (trigger.ConditionExpression != null
                                && CombatSimulationExpressionEvaluator.Evaluate(
                                    trigger.ConditionExpression,
                                    State,
                                    ruleset,
                                    actor.ActorId,
                                    sourceEvent.TargetActorId) <= 0d)
                            || (!string.IsNullOrWhiteSpace(trigger.RequiredDefinitionId)
                                && !string.Equals(
                                    trigger.RequiredDefinitionId,
                                    sourceEvent.DefinitionId,
                                    StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrWhiteSpace(trigger.RequiredEventMessage)
                                && !string.Equals(
                                    trigger.RequiredEventMessage,
                                    sourceEvent.Message,
                                    StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrWhiteSpace(trigger.RequiredActionTag)
                                && !EventHasActionTag(
                                    sourceEvent,
                                    trigger.RequiredActionTag))
                            || (!string.IsNullOrWhiteSpace(trigger.ForbiddenActionTag)
                                && EventHasActionTag(
                                    sourceEvent,
                                    trigger.ForbiddenActionTag)))
                        {
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(trigger.CounterKey))
                        {
                            var counter = status.TriggerCounts.TryGetValue(
                                trigger.CounterKey,
                                out var storedCounter)
                                ? storedCounter
                                : 0;
                            counter += ResolveCounterIncrement(
                                trigger,
                                actor,
                                sourceEvent);
                            status.TriggerCounts[trigger.CounterKey] = counter;
                            if (counter < trigger.MinimumCounterValue
                                || counter > trigger.MaximumCounterValue
                                || (trigger.CounterStep > 0
                                    && (counter < trigger.CounterStepOrigin
                                        || (counter - trigger.CounterStepOrigin)
                                        % trigger.CounterStep != 0)))
                            {
                                continue;
                            }
                        }
                        if (trigger.EveryNthEvent > 1)
                        {
                            var count = status.TriggerCounts.TryGetValue(
                                trigger.TriggerId,
                                out var previous)
                                ? previous + 1
                                : 1;
                            status.TriggerCounts[trigger.TriggerId] = count;
                            if (count % trigger.EveryNthEvent != 0)
                            {
                                continue;
                            }
                        }
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
                var triggerStacks = match.Status.Stacks;
                if (match.Trigger.ExcludeStacksAcquiredFromSameAction
                    && sourceEvent.SourceActionId > 0
                    && match.Status.LastStackGainActionId
                    == sourceEvent.SourceActionId)
                {
                    triggerStacks = Math.Max(
                        0,
                        triggerStacks
                        - Math.Min(
                            triggerStacks,
                            match.Status.StacksGainedInLastAction));
                }
                if (triggerStacks <= 0)
                {
                    continue;
                }
                CompileEffects(
                    match.Trigger.Effects,
                    match.Actor.ActorId,
                    sourceEvent.TargetActorId,
                    sourceEvent.CardInstanceId,
                    null,
                    sourceEvent,
                    wave,
                    queue,
                    triggerStacks);
                if (match.Trigger.ResetCounterAfterTrigger
                    && !string.IsNullOrWhiteSpace(match.Trigger.CounterKey))
                {
                    match.Status.TriggerCounts[match.Trigger.CounterKey] = 0;
                }
                if (match.Trigger.ConsumeStacks > 0)
                {
                    match.Status.Stacks = Math.Max(
                        0,
                        match.Status.Stacks - match.Trigger.ConsumeStacks);
                    if (match.Status.Stacks == 0 && !match.Definition.CanRemainAtZero)
                    {
                        match.Actor.Statuses.Remove(match.Status);
                    }
                }
                if (match.Trigger.RemoveStatusAfterTrigger)
                {
                    match.Actor.Statuses.Remove(match.Status);
                }
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
                    OrderInherentCardsForDraw();
                    if (!ProcessLifecycleEvent(
                            CombatSimulationEventKind.DeckShuffled,
                            actorId,
                            actorId,
                            "",
                            State.DrawPile.Count,
                            0,
                            parentSequence,
                            false))
                    {
                        return;
                    }
                }
                if (State.DrawPile.Count == 0)
                {
                    break;
                }
                var instanceId = State.DrawPile[State.DrawPile.Count - 1];
                MoveCardToZone(instanceId, CombatCardZone.Hand);
                metrics.CardsDrawn++;
                var card = State.FindCard(instanceId);
                if (!ProcessLifecycleEvent(
                        CombatSimulationEventKind.CardDrawn,
                        actorId,
                        actorId,
                        card == null ? "" : EffectiveCardDefinitionId(card),
                        1,
                        instanceId,
                        parentSequence,
                        false))
                {
                    return;
                }
            }
        }

        private bool DiscardUnretainedHand(int actorId)
        {
            foreach (var instanceId in State.Hand.ToList())
            {
                var card = State.FindCard(instanceId);
                if (card != null
                    && TryGetEffectiveCardCore(ruleset, card, out var definition)
                    && HasTag(card, definition, "Retain"))
                {
                    continue;
                }
                if (!State.Hand.Contains(instanceId))
                {
                    // A prior discard lifecycle callback may have moved a
                    // later card from this hand snapshot to another zone.
                    continue;
                }
                MoveCardToZone(instanceId, CombatCardZone.DiscardPile);
                if (!ProcessLifecycleEvent(
                        CombatSimulationEventKind.CardDiscarded,
                        actorId,
                        actorId,
                        State.FindCard(instanceId) is { } discarded
                            ? EffectiveCardDefinitionId(discarded)
                            : "",
                        1,
                        instanceId,
                        0,
                        false))
                {
                    return false;
                }
            }
            return true;
        }

        private void EnqueueCardLifecycleEffects(
            CombatSimulationEvent sourceEvent,
            Queue<CombatSimulationCommand> queue,
            int wave)
        {
            if (sourceEvent.CardInstanceId <= 0)
            {
                return;
            }
            var card = State.FindCard(sourceEvent.CardInstanceId);
            if (card == null
                || !TryGetEffectiveCardCore(ruleset, card, out var definition))
            {
                return;
            }
            var effects = sourceEvent.Kind switch
            {
                CombatSimulationEventKind.CardDrawn => definition.DrawEffects,
                CombatSimulationEventKind.CardDiscarded => definition.DiscardEffects,
                _ => null
            };
            if (effects == null || effects.Count == 0)
            {
                return;
            }
            CompileEffects(
                effects,
                sourceEvent.SourceActorId,
                sourceEvent.TargetActorId,
                sourceEvent.CardInstanceId,
                sourceEvent,
                sourceEvent,
                wave,
                queue);
        }

        private bool EventHasActionTag(
            CombatSimulationEvent sourceEvent,
            string actionTag)
        {
            if (string.IsNullOrWhiteSpace(actionTag))
            {
                return false;
            }
            var source = State.FindActor(sourceEvent.SourceActorId);
            if (source?.Kind == CombatSimulationActorKind.Enemy
                && ruleset.TryGetEnemyCore(source.DefinitionId, out var enemy))
            {
                var intent = ResolveEnemyIntent(
                    enemy,
                    sourceEvent.DefinitionId);
                return intent != null && intent.Tags.Contains(
                    actionTag,
                    StringComparer.OrdinalIgnoreCase);
            }
            var instance = State.FindCard(sourceEvent.CardInstanceId);
            if (instance != null
                && TryGetEffectiveCardCore(
                    ruleset,
                    instance,
                    out var instanceDefinition))
            {
                return HasTag(instance, instanceDefinition, actionTag);
            }
            return ruleset.TryGetCardCore(sourceEvent.DefinitionId, out var card)
                   && HasTag(null, card, actionTag);
        }

        private int ResolveCounterIncrement(
            CombatStatusTriggerDefinition trigger,
            CombatActorState actor,
            CombatSimulationEvent sourceEvent)
        {
            switch (trigger.CounterIncrementMode)
            {
                case CombatStatusCounterIncrementMode.Fixed:
                    return trigger.CounterIncrement;
                case CombatStatusCounterIncrementMode.EventAmount:
                    return sourceEvent.Amount * trigger.CounterIncrement;
                case CombatStatusCounterIncrementMode.HandCount:
                    return State.Hand.Count * trigger.CounterIncrement;
                case CombatStatusCounterIncrementMode.HandTagCount:
                    return State.Hand.Count(instanceId =>
                        State.FindCard(instanceId) is { } instance
                        && TryGetEffectiveCardCore(ruleset, instance, out var card)
                        && HasTag(instance, card, trigger.CounterFilter))
                           * trigger.CounterIncrement;
                case CombatStatusCounterIncrementMode.StatusTagStacks:
                    return actor.Statuses.Sum(status =>
                        ruleset.TryGetStatusCore(status.StatusId, out var definition)
                        && definition.Tags.Contains(
                            trigger.CounterFilter,
                            StringComparer.OrdinalIgnoreCase)
                            ? status.Stacks
                            : 0)
                           * trigger.CounterIncrement;
                default:
                    return 0;
            }
        }

        private bool MatchesOwnerRelation(
            CombatActorState actor,
            CombatStatusTriggerDefinition trigger,
            CombatSimulationEvent sourceEvent)
        {
            if (trigger.OwnerRelation == CombatStatusTriggerOwnerRelation.Any
                && (sourceEvent.Kind == CombatSimulationEventKind.TurnStarted
                    || sourceEvent.Kind == CombatSimulationEventKind.TurnEnded))
            {
                // Native StartRound/EndRound events are keyed by the acting
                // status instance. "Any" means no extra source/target filter
                // inside that actor's event, not every actor's round event.
                return actor.ActorId == sourceEvent.SourceActorId
                       || actor.ActorId == sourceEvent.TargetActorId;
            }
            return trigger.OwnerRelation switch
            {
                CombatStatusTriggerOwnerRelation.EventSource =>
                    actor.ActorId == sourceEvent.SourceActorId,
                CombatStatusTriggerOwnerRelation.EventTarget =>
                    actor.ActorId == sourceEvent.TargetActorId,
                CombatStatusTriggerOwnerRelation.EventTargetAllyExceptSelf =>
                    actor.Alive
                    && actor.ActorId != sourceEvent.TargetActorId
                    && State.FindActor(sourceEvent.TargetActorId) is { } eventTarget
                    && AreAllies(actor, eventTarget),
                _ => true
            };
        }

        private CombatSimulationEvent PlaceCreatedCard(
            CombatCardInstanceState card,
            CombatSimulationCommand command)
        {
            var destination = command.DestinationZone;
            if (destination == CombatCardZone.Hand
                && State.Hand.Count >= Math.Max(1, scenario.HandLimit))
            {
                // Witch keeps a generated card on top of the draw pile when
                // the hand is full. It is not a discard and must not execute
                // the generated card's discard lifecycle.
                destination = CombatCardZone.DrawPile;
            }

            List<int> zone;
            CombatSimulationEventKind eventKind;
            switch (destination)
            {
                case CombatCardZone.DrawPile:
                    zone = State.DrawPile;
                    eventKind = CombatSimulationEventKind.CardCreated;
                    break;
                case CombatCardZone.ExhaustPile:
                    zone = State.ExhaustPile;
                    eventKind = CombatSimulationEventKind.CardExhausted;
                    break;
                case CombatCardZone.DiscardPile:
                    zone = State.DiscardPile;
                    eventKind = CombatSimulationEventKind.CardDiscarded;
                    break;
                default:
                    zone = State.Hand;
                    eventKind = CombatSimulationEventKind.CardDrawn;
                    metrics.CardsDrawn++;
                    break;
            }

            CombatRandomDraw? randomDraw = null;
            if (destination == CombatCardZone.DrawPile && command.RandomizeDestination)
            {
                var insertAt = CombatDeterministicRandom.NextInt(
                    scenario.Seed,
                    State.Random,
                    "card.create.insert:" + card.CardId,
                    zone.Count + 1,
                    out var draw);
                zone.Insert(insertAt, card.InstanceId);
                randomDraw = draw;
                TraceRandomDraw(draw, "created card insertion");
            }
            else
            {
                zone.Add(card.InstanceId);
            }

            return Emit(
                eventKind,
                command.SourceActorId,
                command.TargetActorId,
                card.InstanceId,
                card.CardId,
                1,
                command.ParentSequence,
                randomDraw);
        }

        private void OrderInherentCardsForDraw()
        {
            if (State.DrawPile.Count <= 1)
            {
                return;
            }
            var normal = new List<int>();
            var inherent = new List<int>();
            foreach (var instanceId in State.DrawPile)
            {
                var card = State.FindCard(instanceId);
                if (card != null
                    && TryGetEffectiveCardCore(ruleset, card, out var definition)
                    && HasTag(card, definition, "Inherent"))
                {
                    inherent.Add(instanceId);
                }
                else
                {
                    normal.Add(instanceId);
                }
            }
            State.DrawPile.Clear();
            State.DrawPile.AddRange(normal);
            State.DrawPile.AddRange(inherent);
        }

        private List<int> CardZone(CombatCardZone zone)
        {
            return zone switch
            {
                CombatCardZone.Hand => State.Hand,
                CombatCardZone.DiscardPile => State.DiscardPile,
                CombatCardZone.ExhaustPile => State.ExhaustPile,
                _ => State.DrawPile
            };
        }

        private void MoveCardToZone(
            int instanceId,
            CombatCardZone destination)
        {
            State.DrawPile.RemoveAll(item => item == instanceId);
            State.Hand.RemoveAll(item => item == instanceId);
            State.DiscardPile.RemoveAll(item => item == instanceId);
            State.ExhaustPile.RemoveAll(item => item == instanceId);
            CardZone(destination).Add(instanceId);
        }

        private static bool HasTag(
            CombatCardInstanceState? instance,
            CombatCardDefinition definition,
            string tag)
        {
            return (instance?.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) ?? false)
                   || definition.Tags.Any(candidate =>
                       string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetEffectiveCardCore(
            CombatRuleset ruleset,
            CombatCardInstanceState instance,
            out CombatCardDefinition definition)
        {
            return ruleset.TryGetCardCore(
                EffectiveCardDefinitionId(instance),
                out definition);
        }

        private static string EffectiveCardDefinitionId(
            CombatCardInstanceState instance)
        {
            return string.IsNullOrWhiteSpace(instance.ApparentCardId)
                ? instance.CardId
                : instance.ApparentCardId;
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
                MoveCardToZone(instanceId, destination);
                secondaryCommandEvents.Add(Emit(
                    destination == CombatCardZone.ExhaustPile
                        ? CombatSimulationEventKind.CardExhausted
                        : CombatSimulationEventKind.CardDiscarded,
                    command.SourceActorId,
                    command.TargetActorId,
                    instanceId,
                    State.FindCard(instanceId) is { } moved
                        ? EffectiveCardDefinitionId(moved)
                        : "",
                    1,
                    command.ParentSequence,
                    draw));
            }
        }

        private void DecayStatuses()
        {
            foreach (var actor in State.Actors)
            {
                foreach (var intentId in actor.IntentCooldowns.Keys.ToList())
                {
                    actor.IntentCooldowns[intentId] =
                        Math.Max(0, actor.IntentCooldowns[intentId] - 1);
                }
                foreach (var status in actor.Statuses.ToList())
                {
                    if (!ruleset.TryGetStatusCore(status.StatusId, out var definition)
                        || !definition.DecayAtRoundEnd)
                    {
                        continue;
                    }
                    var durationExpired = false;
                    if (status.Duration > 0)
                    {
                        status.Duration--;
                        durationExpired = status.Duration == 0;
                    }
                    if (definition.ReducePerTurn > 0)
                    {
                        status.Stacks = Math.Max(0, status.Stacks - definition.ReducePerTurn);
                    }
                    var stacksExpired = definition.ReducePerTurn > 0 && status.Stacks <= 0;
                    if ((durationExpired || stacksExpired) && !definition.CanRemainAtZero)
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

        private void ReduceStatuses(
            CombatActorState actor,
            Func<CombatStatusDefinition, int> amountSelector)
        {
            foreach (var status in actor.Statuses.ToList())
            {
                if (!ruleset.TryGetStatusCore(status.StatusId, out var definition))
                {
                    continue;
                }
                var amount = Math.Max(0, amountSelector(definition));
                if (amount <= 0)
                {
                    continue;
                }
                status.Stacks = Math.Max(0, status.Stacks - amount);
                if (status.Stacks == 0 && !definition.CanRemainAtZero)
                {
                    actor.Statuses.Remove(status);
                    Emit(
                        CombatSimulationEventKind.StatusRemoved,
                        actor.ActorId,
                        actor.ActorId,
                        0,
                        status.StatusId,
                        amount);
                }
            }
        }

        private double Variable(CombatActorState? actor, string key, double fallback)
        {
            return CombatSimulationExpressionEvaluator.ResolveVariable(
                actor,
                ruleset,
                key,
                fallback);
        }

        private static bool ConsumeTurnSkip(CombatActorState actor)
        {
            if (!actor.Variables.TryGetValue("SkipCurrentTurn", out var value)
                || value <= 0d)
            {
                return false;
            }
            actor.Variables.Remove("SkipCurrentTurn");
            return true;
        }

        private static int WitchRounded(double value)
        {
            if (double.IsNaN(value)) return 0;
            if (value >= int.MaxValue) return int.MaxValue;
            if (value <= int.MinValue) return int.MinValue;
            var ceiling = Math.Ceiling(value);
            return (int)(ceiling - value <= 0.01d
                ? ceiling
                : Math.Floor(value));
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

        private static bool AreAllies(
            CombatActorState first,
            CombatActorState second)
        {
            return first.Kind == CombatSimulationActorKind.Enemy
                ? second.Kind == CombatSimulationActorKind.Enemy
                : second.Kind != CombatSimulationActorKind.Enemy;
        }

        private void CheckOutcome()
        {
            var player = State.Player;
            if (player == null || !player.Alive)
            {
                TerminateCore(
                    CombatSimulationOutcome.Defeat,
                    CombatTerminationReason.Defeat,
                    explicitRule: false);
            }
            else if (!State.LivingEnemies.Any())
            {
                TerminateCore(
                    CombatSimulationOutcome.Victory,
                    CombatTerminationReason.Victory,
                    explicitRule: false);
            }
        }

        private void CaptureTerminalState(
            CombatSimulationOutcome outcome,
            bool explicitRule)
        {
            if (terminalStateCaptured)
            {
                return;
            }
            terminalStateCaptured = true;
            var playerAlive = State.Player?.Alive == true;
            var livingEnemies = State.LivingEnemies.Count();
            terminalPlayerHp = State.Player?.Hp ?? 0;
            terminalLivingEnemyCount = livingEnemies;
            if (explicitRule
                || outcome == CombatSimulationOutcome.None
                || outcome == CombatSimulationOutcome.Draw
                || outcome == CombatSimulationOutcome.Invalid)
            {
                return;
            }
            var consistent = outcome switch
            {
                CombatSimulationOutcome.Defeat => !playerAlive,
                CombatSimulationOutcome.Victory => playerAlive && livingEnemies == 0,
                _ => true
            };
            if (consistent)
            {
                return;
            }
            terminalConsistencyValid = false;
            terminalConsistencyReason =
                "outcome="
                + outcome
                + ",playerAlive="
                + playerAlive
                + ",livingEnemies="
                + livingEnemies;
            AddUnsupported(
                "terminal-consistency:" + terminalConsistencyReason);
        }

        private void ApplyDeferredVictoryVariableChanges()
        {
            foreach (var deferred in State.DeferredVictoryVariableChanges.ToList())
            {
                var target = State.FindActor(deferred.ActorId);
                if (target == null || string.IsNullOrWhiteSpace(deferred.DefinitionId))
                {
                    continue;
                }
                var before = Variable(target, deferred.DefinitionId, 0d);
                var after = Math.Max(
                    deferred.MinimumVariableValue,
                    Math.Min(
                        deferred.MaximumVariableValue,
                        before + deferred.Amount));
                target.Variables[deferred.DefinitionId] = after;
                var actualDelta = WitchRounded(after - before);
                if (deferred.PersistAcrossBattles
                    && target.Kind == CombatSimulationActorKind.Player)
                {
                    persistentVariableDeltas[deferred.DefinitionId] =
                        persistentVariableDeltas.TryGetValue(
                            deferred.DefinitionId,
                            out var persistentCurrent)
                            ? persistentCurrent + actualDelta
                            : actualDelta;
                }
                Emit(
                    CombatSimulationEventKind.VariableChanged,
                    target.ActorId,
                    target.ActorId,
                    0,
                    deferred.DefinitionId,
                    actualDelta);
            }
            State.DeferredVictoryVariableChanges.Clear();
        }

        private bool ProcessLifecycleEvent(
            CombatSimulationEventKind kind,
            int sourceActorId,
            int targetActorId,
            string definitionId,
            int amount,
            int cardInstanceId = 0,
            long parentSequence = 0,
            bool resetActionCommandCount = true)
        {
            if (resetActionCommandCount)
            {
                currentActionCommandCount = 0;
            }
            var item = Emit(
                kind,
                sourceActorId,
                targetActorId,
                cardInstanceId,
                definitionId,
                amount,
                parentSequence);
            var queue = new Queue<CombatSimulationCommand>();
            EnqueueCardLifecycleEffects(item, queue, 1);
            EnqueueTriggers(item, queue, 1);
            return ExecuteQueue(queue);
        }

        private bool ValidateState()
        {
            var zones = State.DrawPile
                .Concat(State.Hand)
                .Concat(State.DiscardPile)
                .Concat(State.ExhaustPile)
                .Concat(State.SkillCards)
                .ToList();
            var zoneCountValid = zones.Count == State.Cards.Count;
            var zonesDistinct = zones.Distinct().Count() == zones.Count;
            var actorIdsDistinct =
                State.Actors.Select(actor => actor.ActorId).Distinct().Count()
                == State.Actors.Count;
            var cardIdsDistinct =
                State.Cards.Select(card => card.InstanceId).Distinct().Count()
                == State.Cards.Count;
            var invalidActors = State.Actors
                .Where(actor =>
                    actor.Hp < 0
                    || actor.Hp > actor.MaxHp
                    || actor.Block < 0
                    || actor.Energy < 0)
                .ToList();
            var valid = zoneCountValid
                        && zonesDistinct
                        && actorIdsDistinct
                        && cardIdsDistinct
                        && invalidActors.Count == 0;
            if (!valid)
            {
                AddUnsupported("state-invariant");
                if (!zoneCountValid)
                {
                    AddUnsupported(
                        "state-invariant:zone-count:"
                        + zones.Count
                        + "/"
                        + State.Cards.Count);
                }
                if (!zonesDistinct)
                {
                    AddUnsupported("state-invariant:duplicate-zone-card");
                }
                if (!actorIdsDistinct)
                {
                    AddUnsupported("state-invariant:duplicate-actor-id");
                }
                if (!cardIdsDistinct)
                {
                    AddUnsupported("state-invariant:duplicate-card-id");
                }
                foreach (var actor in invalidActors)
                {
                    AddUnsupported(
                        "state-invariant:actor:"
                        + actor.ActorId
                        + ":hp="
                        + actor.Hp
                        + "/"
                        + actor.MaxHp
                        + ":block="
                        + actor.Block
                        + ":energy="
                        + actor.Energy);
                }
                Terminate(CombatSimulationOutcome.Invalid, CombatTerminationReason.EngineError);
            }
            return valid;
        }

        private bool CanEnterDynamicCardPool(
            string cardId,
            bool allowCrossRoleSkill)
        {
            var entry = scenario.RewardCatalog.FirstOrDefault(item =>
                item.Kind.Equals(
                    "Card",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.RewardId,
                    cardId,
                    StringComparison.OrdinalIgnoreCase));
            return CombatCampaignCardAcquisitionPolicy
                .CanEnterDynamicGenerationPool(
                    entry,
                    scenario.Player.SkillCardIds,
                    scenario.EnabledRewardCardPackIds,
                    allowCrossRoleSkill);
        }

        private bool AllowsCrossRoleSkill(string sourceRewardId)
        {
            var source = scenario.RewardRules.FirstOrDefault(item =>
                string.Equals(
                    item.RewardId,
                    sourceRewardId,
                    StringComparison.OrdinalIgnoreCase));
            return source?.Variables.TryGetValue(
                       "AllowCrossRoleSkill",
                       out var value) == true
                   && bool.TryParse(value, out var parsed)
                   && parsed;
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
                beforeHash,
                command.CausalChainId,
                command.HandlerId,
                command.SourceRewardId,
                command.SourceActionId);
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
            string? beforeStateHash = null,
            long causalChainId = 0,
            string? handlerId = null,
            string? sourceRewardId = null,
            long sourceActionId = 0)
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
                CausalChainId = causalChainId > 0
                    ? causalChainId
                    : parentSequence > 0
                        ? parentSequence
                        : State.EventSequence,
                HandlerId = handlerId ?? "",
                SourceRewardId = sourceRewardId ?? "",
                SourceActionId = sourceActionId > 0
                    ? sourceActionId
                    : State.ActionSequence,
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
            currentActionContractEvents?.Add(item);
            if (ShouldTrace(kind))
            {
                Events.Add(item);
            }
            return item;
        }

        private bool ShouldTrace(CombatSimulationEventKind kind)
        {
            if (captureSemanticEvents)
            {
                return true;
            }
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

        CombatScenarioDefinition ICombatSimulationRuntimeContext.Scenario => scenario;

        CombatRuleset ICombatSimulationRuntimeContext.Ruleset => ruleset;

        void ICombatSimulationRuntimeContext.ApplyEffects(
            IEnumerable<CombatSimulationEffectDefinition> effects,
            int sourceActorId,
            int selectedTargetId,
            CombatSimulationEvent? sourceEvent)
        {
            if (effects == null || State.Outcome != CombatSimulationOutcome.None)
            {
                return;
            }
            var queue = new Queue<CombatSimulationCommand>();
            CompileEffects(
                effects,
                sourceActorId,
                selectedTargetId,
                sourceEvent?.CardInstanceId ?? 0,
                sourceEvent,
                sourceEvent,
                1,
                queue);
            ExecuteQueue(queue);
        }

        int ICombatSimulationRuntimeContext.NextRandomInt(
            string streamId,
            int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                return 0;
            }
            var value = CombatDeterministicRandom.NextInt(
                scenario.Seed,
                State.Random,
                "extension:" + (streamId ?? ""),
                exclusiveMaximum,
                out var draw);
            TraceRandomDraw(draw, "runtime extension");
            return value;
        }

        void ICombatSimulationRuntimeContext.RecordRewardMutation(
            string operation,
            string kind,
            string rewardId)
        {
            if (string.IsNullOrWhiteSpace(operation)
                || string.IsNullOrWhiteSpace(kind)
                || string.IsNullOrWhiteSpace(rewardId))
            {
                return;
            }
            rewardMutations.Add(new CombatSimulationRewardMutation
            {
                Operation = operation,
                Kind = kind,
                RewardId = rewardId
            });
        }

        void ICombatSimulationRuntimeContext.Terminate(
            CombatSimulationOutcome outcome,
            CombatTerminationReason reason)
        {
            TerminateCore(outcome, reason, explicitRule: true);
        }

        void ICombatPersistentProgressionContext.RecordPersistentVariableDelta(
            string variableId,
            int amount)
        {
            if (string.IsNullOrWhiteSpace(variableId) || amount == 0)
            {
                return;
            }
            persistentVariableDeltas[variableId] =
                persistentVariableDeltas.TryGetValue(
                    variableId,
                    out var current)
                    ? current + amount
                    : amount;
        }

        private void NotifyExtension(CombatSimulationEvent sourceEvent)
        {
            if (extension == null
                || !extensionEventSequences.Add(sourceEvent.Sequence))
            {
                return;
            }
            extension.OnEvent(this, sourceEvent);
        }

        private void CompleteExtension()
        {
            if (extension == null || !extensionInitialized || extensionCompleted)
            {
                return;
            }
            extensionCompleted = true;
            extension.Complete(this);
        }

        private void ResetHpLossWindow()
        {
            foreach (var actor in State.Actors)
            {
                if (actor.Variables.ContainsKey("MaxChangeHp")
                    || actor.Variables.ContainsKey("HpLossThisAction"))
                {
                    actor.Variables["HpLossThisAction"] = 0d;
                }
            }
        }

        private int LimitHpLoss(
            CombatActorState target,
            int requested)
        {
            requested = Math.Max(0, requested);
            if (requested <= 0
                || !target.Variables.TryGetValue(
                    "MaxChangeHp",
                    out var maximumChangeRatio))
            {
                return requested;
            }
            var ratio = Math.Max(0d, Math.Min(1d, maximumChangeRatio));
            var maximumLoss = Math.Max(
                0,
                (int)Math.Floor(target.MaxHp * ratio));
            var alreadyLost = Math.Max(
                0,
                WitchRounded(Variable(
                    target,
                    "HpLossThisAction",
                    0d)));
            var applied = Math.Min(
                requested,
                Math.Max(0, maximumLoss - alreadyLost));
            target.Variables["HpLossThisAction"] = alreadyLost + applied;
            return applied;
        }

        private double DamageFilterMultiplier(
            CombatActorState target,
            CombatSimulationEffectKind kind,
            string definitionId)
        {
            var damageType = kind switch
            {
                CombatSimulationEffectKind.TrueDamage => "True",
                CombatSimulationEffectKind.DirectHpLoss
                    when (definitionId ?? "").StartsWith(
                        "buff_",
                        StringComparison.OrdinalIgnoreCase) => "Dot",
                CombatSimulationEffectKind.DirectHpLoss => "DirectHpLoss",
                _ => "Normal"
            };
            var typedMultiplier = Math.Max(
                0d,
                Variable(
                    target,
                    "DamageTakenMultiplier." + damageType,
                    1d));
            var typeReduction = Math.Max(
                0d,
                Variable(
                    target,
                    "DamageFilter." + damageType,
                    0d));
            var sourceReduction = string.IsNullOrWhiteSpace(definitionId)
                ? 0d
                : Math.Max(
                    0d,
                    Variable(
                        target,
                        "DamageFilter." + definitionId,
                        0d));
            return typedMultiplier
                   * Math.Max(
                       0d,
                       1d - Math.Max(typeReduction, sourceReduction) / 100d);
        }

        private static void MergeCounts(
            IDictionary<string, int> target,
            IReadOnlyDictionary<string, int>? source)
        {
            if (source == null)
            {
                return;
            }
            foreach (var pair in source)
            {
                target[pair.Key] = target.TryGetValue(
                    pair.Key,
                    out var current)
                    ? current + Math.Max(0, pair.Value)
                    : Math.Max(0, pair.Value);
            }
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
