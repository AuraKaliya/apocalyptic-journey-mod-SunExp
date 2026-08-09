using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using static CombatAiTestFixtures;

internal static class SemanticCausalityBehaviorTests
{
    public static void Run()
    {
        GrossSelfHarmAndContextHealSurviveNetCancellation();
        ContextDamageKeepsItsCausalOwner();
        IntermediateLethalitySurvivesLaterRecovery();
        UntracedHpMutationFailsConservationGate();
        CoverageInventoryIncludesStructuredAndNativeSemantics();
        RandomAndInteractionFactsAreProjectedBeforeFork();
        DeferredInteractionDoesNotClaimImmediateDraw();
        RandomActionFactsCarryCausalOwnership();
        TerminalActionAndRuntimeForkBoundariesAreExplicit();
        SelectedTransitionIsRecordedWhenTeacherSamplingIsZero();
    }

    private static void GrossSelfHarmAndContextHealSurviveNetCancellation()
    {
        var before = State(playerHp: 10, enemyHp: 20);
        var after = before.Clone();
        var events = new[]
        {
            Event(
                1,
                CombatSimulationEventKind.DamageDealt,
                targetActorId: 1,
                amount: 4,
                rewardId: "buff_frenzy",
                handlerId: "buff_frenzy:ActionAfter",
                message: "DirectHpLoss"),
            Event(
                2,
                CombatSimulationEventKind.Healed,
                targetActorId: 1,
                amount: 4,
                rewardId: "buff_oniblood",
                handlerId: "buff_oniblood:ActionAfter",
                previousAmount: 6,
                currentAmount: 10)
        };
        var action = Action();
        var realized = CombatSemanticAuditor.ProjectRealized(
            before,
            after,
            events,
            action,
            null);
        var audit = CombatSemanticAuditor.Audit(
            before,
            after,
            events,
            realized,
            action,
            null);

        Assert(realized.ObservedNetHpDelta == 0d
               && realized.SelfHpLoss == 4d
               && realized.ContextSelfHpLoss == 4d
               && realized.Heal == 4d
               && realized.ContextHeal == 4d
               && realized.MinimumHpDuringAction == 6d
               && realized.TargetEffects.Count == 2
               && realized.TargetEffects.All(item =>
                   item.Attribution
                   == CombatSemanticEffectAttribution.ActionTriggeredContext)
               && audit.Valid
               && !audit.Mismatch,
            "gross contextual self-harm and healing remain visible when their net HP change is zero");
    }

    private static void ContextDamageKeepsItsCausalOwner()
    {
        var before = State(playerHp: 10, enemyHp: 20);
        var after = before.Clone();
        after.FindActor(2)!.Hp = 17;
        var events = new[]
        {
            Event(
                1,
                CombatSimulationEventKind.DamageDealt,
                targetActorId: 2,
                amount: 3,
                rewardId: "blessing_32",
                handlerId: "blessing_32:Action",
                message: "Damage")
        };
        var action = Action();
        var directAudit = CombatSemanticAuditor.Audit(
            before,
            after,
            events,
            new CombatActionSemantics(),
            action,
            null);
        var realized = CombatSemanticAuditor.ProjectRealized(
            before,
            after,
            events,
            action,
            null);

        Assert(realized.DirectDamage == 0d
               && realized.ContextDamage == 3d
               && realized.TargetEffects.Single().SourceDefinitionId
                   == "blessing_32"
               && realized.TargetEffects.Single().Phase
                   == CombatSemanticEffectPhase.PostAction
               && directAudit.Valid
               && !directAudit.Mismatch
               && directAudit.ExplainedKinds.Contains("damage"),
            "blessing damage is attributed to action context instead of being mislabeled as direct card damage");
    }

    private static void IntermediateLethalitySurvivesLaterRecovery()
    {
        var before = State(playerHp: 3, enemyHp: 20);
        var after = before.Clone();
        after.Player!.Hp = 5;
        var events = new[]
        {
            Event(
                1,
                CombatSimulationEventKind.DamageDealt,
                targetActorId: 1,
                amount: 3,
                rewardId: "buff_frenzy",
                handlerId: "buff_frenzy:ActionAfter",
                message: "DirectHpLoss"),
            Event(
                2,
                CombatSimulationEventKind.Healed,
                targetActorId: 1,
                amount: 5,
                rewardId: "buff_oniblood",
                handlerId: "buff_oniblood:ActionAfter",
                previousAmount: 0,
                currentAmount: 5)
        };
        var realized = CombatSemanticAuditor.ProjectRealized(
            before,
            after,
            events,
            Action(),
            null);

        Assert(realized.MinimumHpDuringAction == 0d
               && realized.LethalBeforeRecovery,
            "an intermediate lethal state remains a safety fact even when a later trigger heals the player");
    }

    private static void UntracedHpMutationFailsConservationGate()
    {
        var before = State(playerHp: 10, enemyHp: 20);
        var after = before.Clone();
        after.Player!.Hp = 6;
        var events = new[]
        {
            Event(
                1,
                CombatSimulationEventKind.ActionResolved,
                targetActorId: 2,
                amount: 1,
                rewardId: "test-card",
                handlerId: "")
        };
        var action = Action();
        var realized = CombatSemanticAuditor.ProjectRealized(
            before,
            after,
            events,
            action,
            null);
        var audit = CombatSemanticAuditor.Audit(
            before,
            after,
            events,
            realized,
            action,
            null);

        Assert(realized.StateChanges.ContainsKey("trace.hp.unattributed")
               && audit.InvalidKinds.Contains("hp-conservation"),
            "state-only HP changes cannot pass the semantic gate without a causal fact");
    }

    private static void CoverageInventoryIncludesStructuredAndNativeSemantics()
    {
        var builder = new CombatRulesetBuilder("semantic-coverage-test");
        builder.RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "tests",
            StatusId = "coverage-status",
            Metadata = new Dictionary<string, string>
            {
                ["NativeExecution"] = "Script",
                ["NativeApplyScript"] = "ChangeMaxHp(1);"
            },
            Triggers =
            {
                new CombatStatusTriggerDefinition
                {
                    TriggerId = "after-action",
                    EventKind = CombatSimulationEventKind.ActionResolved,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.Heal,
                            Target = CombatSimulationTarget.Player,
                            Amount = 2
                        }
                    }
                },
                new CombatStatusTriggerDefinition
                {
                    TriggerId = "on-damage",
                    EventKind = CombatSimulationEventKind.DamageDealt,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.GainEnergy,
                            Target = CombatSimulationTarget.Player,
                            Amount = 1
                        }
                    }
                }
            }
        });
        var frozen = builder.Freeze();
        var campaign = new CombatCampaignDefinition
        {
            Rewards =
            {
                new CombatCampaignRewardDefinition
                {
                    RewardId = "coverage-blessing",
                    Kind = CombatCampaignRewardKind.Blessing,
                    FightScript = "AddEvent(\"Action\", () => ChangePower(1));"
                }
            }
        };
        var coverage = CombatSemanticCoverageAudit.Analyze(
            campaign,
            frozen.Ruleset);

        Assert(frozen.Success
               && coverage.Complete
               && coverage.ProjectedCount == 1
               && coverage.RealizedOnlyCount == 3
               && coverage.Entries.Any(item =>
                   item.OwnerId == "coverage-blessing")
               && coverage.Entries.Any(item =>
                   item.OwnerId == "coverage-status"
                   && item.Trigger == "native-event-bridge"),
            "semantic coverage inventory includes structured triggers and native BUFF/blessing scripts with explicit projection tiers");
    }

    private static void RandomAndInteractionFactsAreProjectedBeforeFork()
    {
        var card = new CombatCardDefinition
        {
            OwnerModId = "tests",
            CardId = "random-interaction",
            Interaction = new CombatInteractionDefinition
            {
                Kind = CombatInteractionKind.BurnCards,
                Zone = CombatInteractionZone.Hand,
                MinSelections = 0,
                MaxSelections = 2,
                CanConfirmEarly = true,
                CanConfirmEmpty = true,
                EffectsComplete = true,
                SelectionEffects =
                {
                    new CombatInteractionEffectDefinition
                    {
                        Kind = CombatInteractionEffectKind.BurnSelected
                    }
                }
            },
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.DiscardRandom,
                    Target = CombatSimulationTarget.Player,
                    Amount = 1
                }
            }
        };
        var frozen = new CombatRulesetBuilder("random-projection")
            .RegisterCard(card)
            .Freeze();
        var state = State(playerHp: 10, enemyHp: 20);
        var action = Action();
        action.DefinitionId = card.CardId;
        var semantics = CombatAuthoritativeSemanticProjector.Project(
            frozen.Ruleset,
            state,
            card,
            action);

        Assert(frozen.Success
               && semantics.RandomOutcome
               && semantics.OpensInteraction
               && semantics.Interaction?.EffectsComplete == true,
            "random resolution and follow-up interaction contracts are present in the source projection before authoritative execution");
    }

    private static void DeferredInteractionDoesNotClaimImmediateDraw()
    {
        var before = State(playerHp: 10, enemyHp: 20);
        var after = before.Clone();
        var action = Action();
        action.DefinitionId = "deferred-deck-choice";
        var declared = new CombatActionSemantics
        {
            Draw = 1d,
            OpensInteraction = true,
            Interaction = new CombatInteractionDefinition
            {
                Kind = CombatInteractionKind.ChooseCards,
                Zone = CombatInteractionZone.DrawPile,
                MinSelections = 1,
                MaxSelections = 1,
                EffectsComplete = true
            }
        };
        var events = new[]
        {
            Event(
                1,
                CombatSimulationEventKind.ActionResolved,
                targetActorId: 1,
                amount: 0,
                rewardId: action.DefinitionId,
                handlerId: "")
        };
        var realized = CombatSemanticAuditor.ProjectRealized(
            before,
            after,
            events,
            action,
            null,
            declared);

        Assert(realized.Draw == 0d
               && realized.OpensInteraction
               && realized.Interaction?.Zone
                  == CombatInteractionZone.DrawPile
               && realized.StateChanges.GetValueOrDefault(
                   "projection.realized") == 1d,
            "a parent action preserves its follow-up interaction while immediate draw remains an observed transition fact");
    }

    private static void RandomActionFactsCarryCausalOwnership()
    {
        var randomCard = new CombatCardDefinition
        {
            OwnerModId = "tests",
            CardId = "causal-random-discard",
            Cost = 0,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.DiscardRandom,
                    Target = CombatSimulationTarget.Player,
                    Amount = 1
                }
            }
        };
        var filler = new CombatCardDefinition
        {
            OwnerModId = "tests",
            CardId = "causal-filler",
            Cost = 9
        };
        var frozen = new CombatRulesetBuilder("random-causality")
            .RegisterCard(randomCard)
            .RegisterCard(filler)
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "tests",
                EnemyId = "causal-dummy",
                MaxHp = 20,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Weight = 1
                    }
                }
            })
            .Freeze();
        var result = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "random-causal-ownership",
                RulesetVersion = "random-causality",
                Seed = 991,
                InitialDraw = 3,
                DrawPerTurn = 0,
                TraceLevel = CombatSimulationTraceLevel.Full,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tester",
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck =
                    {
                        randomCard.CardId,
                        filler.CardId,
                        filler.CardId
                    }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "causal-dummy" }
                },
                Limits = new CombatSimulationLimits
                {
                    MaximumTurns = 1,
                    MaximumActions = 10,
                    MaximumCommands = 100
                }
            },
            frozen.Ruleset,
            new PlayCardOnceThenEndPolicy(randomCard.CardId));
        var random = result.Events.FirstOrDefault(item =>
            item.Kind == CombatSimulationEventKind.RandomResolved
            && item.Message == CombatCardZone.DiscardPile.ToString());

        Assert(frozen.Success
               && random != null
               && random.SourceActionId > 0
               && random.CardInstanceId > 0
               && random.DefinitionId == randomCard.CardId
               && random.SourceRewardId == randomCard.CardId,
            "random action facts carry the card instance, source action and reward identity required by causal semantic audit");
    }

    private static void SelectedTransitionIsRecordedWhenTeacherSamplingIsZero()
    {
        var card = new CombatCardDefinition
        {
            OwnerModId = "tests",
            CardId = "selected-transition",
            Cost = 0,
            RequiresEnemyTarget = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Damage,
                    Target = CombatSimulationTarget.SelectedEnemy,
                    Amount = 20
                }
            }
        };
        var frozen = new CombatRulesetBuilder("selected-transition")
            .RegisterCard(card)
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "tests",
                EnemyId = "selected-dummy",
                MaxHp = 10,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Weight = 1
                    }
                }
            })
            .Freeze();
        var teacher = new CombatAuthoritativeBranchTeacherPolicy(
            new CombatDecisionSimulationPolicy(new CombatDecisionProfile
            {
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 8,
                SearchNodeBudget = 64,
                SearchMinimumSimulations = 1,
                SearchMaxPly = 2
            }),
            new CombatAuthoritativeTeacherOptions
            {
                AuditProbability = 1d,
                RandomSeed = 992
            },
            new CombatSimulationEngine(
                new TestUnforkableCampaignMutationExtensionFactory()));
        var recording = new CombatEpisodeRecordingPolicy(
            teacher,
            "selected-transition-test");
        var result = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "selected-transition-zero-teacher-sampling",
                RulesetVersion = "selected-transition",
                Seed = 992,
                InitialDraw = 1,
                DrawPerTurn = 0,
                TraceLevel = CombatSimulationTraceLevel.Full,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tester",
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck = { card.CardId }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "selected-dummy" }
                }
            },
            frozen.Ruleset,
            recording);
        var episode = recording.Complete(result);
        var recorded = teacher.LastObservation?.Actions.FirstOrDefault(item =>
            item.SourceId == card.CardId);
        var recordedFrameCandidate = episode.Frames
            .SelectMany(frame => frame.Candidates)
            .FirstOrDefault(item => item.SourceId == card.CardId);

        Assert(frozen.Success
               && result.Outcome == CombatSimulationOutcome.Victory
               && result.Metrics.AuthoritativeSelectedActionsAudited > 0
               && result.Metrics.SemanticAudit.SelectedValidActions > 0
               && result.Metrics.SemanticAudit
                   .CounterfactualBranchUnavailableActions > 0
               && result.Metrics.SemanticAudit
                   .CounterfactualBranchUnavailableSources.ContainsKey(
                       card.CardId)
               && recorded?.Features.GetValueOrDefault(
                   "authoritativeTransitionSemantics") == 1d
               && recorded.Semantics.Damage > 0d
               && recordedFrameCandidate?.Features.GetValueOrDefault("damage")
                   > 0d,
            "the chosen action and recorded training frame receive the actual factual transition even when multi-candidate teacher sampling is disabled");
    }

    private static void TerminalActionAndRuntimeForkBoundariesAreExplicit()
    {
        var card = new CombatCardDefinition
        {
            OwnerModId = "tests",
            CardId = "terminal-context-card",
            Cost = 0,
            RequiresEnemyTarget = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Damage,
                    Target = CombatSimulationTarget.SelectedEnemy,
                    Amount = 1
                }
            }
        };
        var frozen = new CombatRulesetBuilder("terminal-context")
            .RegisterCard(card)
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "tests",
                EnemyId = "terminal-context-enemy",
                MaxHp = 20,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Weight = 1
                    }
                }
            })
            .Freeze();
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "terminal-action-boundary",
            RulesetVersion = "terminal-context",
            DirectHpLossAfterPlayerCard = 1,
            Player = new CombatPlayerSetup
            {
                RoleId = "tester",
                MaxHp = 20,
                CurrentHp = 1,
                Deck = { card.CardId }
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "terminal-context-enemy" }
            }
        };
        var state = new CombatBattleState
        {
            Turn = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            NextActorId = 3,
            NextCardInstanceId = 2,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    InstanceKey = "player",
                    Kind = CombatSimulationActorKind.Player,
                    DefinitionId = "tester",
                    Hp = 1,
                    MaxHp = 20,
                    Energy = 3,
                    BaseEnergy = 3
                },
                new CombatActorState
                {
                    ActorId = 2,
                    InstanceKey = "enemy",
                    Kind = CombatSimulationActorKind.Enemy,
                    DefinitionId = "terminal-context-enemy",
                    Hp = 20,
                    MaxHp = 20,
                    CurrentIntentId = "wait"
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = card.CardId
                }
            },
            Hand = { 1 }
        };
        var plainEngine = new CombatSimulationEngine();
        var legal = plainEngine.GetLegalPlayerActions(
            scenario,
            frozen.Ruleset,
            state);
        var selected = legal.First(item =>
            item.Kind == CombatSimulationActionKind.PlayCard);
        var observation = PlayerEquivalentSimulationObservationProjector.Project(
            new CombatSimulationPolicyContext
            {
                Scenario = scenario,
                Ruleset = frozen.Ruleset,
                State = state,
                LegalActions = legal
            });
        var projected = observation.Actions.First(item =>
            item.CandidateId == selected.CandidateId);
        var safe = CombatActionSafetyPolicy.IsAdmissible(
            observation,
            projected,
            new AuraDecision.Shared.DecisionUtility(),
            out var safetyReason);
        var applied = plainEngine.ForkAndApplyPlayerAction(
            scenario,
            frozen.Ruleset,
            state,
            selected,
            captureSemanticEvents: true);

        var extensionEngine = new CombatSimulationEngine(
            new TestUnforkableCampaignMutationExtensionFactory());
        var unavailable = extensionEngine.ForkAndApplyPlayerAction(
            scenario,
            frozen.Ruleset,
            state,
            selected,
            captureSemanticEvents: true,
            requireExactRuntimeContinuation: true);
        var isolatedScenario = CombatScenarioCloner.Clone(scenario);
        isolatedScenario.DirectHpLossAfterPlayerCard = 0;
        var isolatedState = state.Clone();
        isolatedState.Player!.Hp = 10;
        var isolated = extensionEngine.ForkAndApplyPlayerAction(
            isolatedScenario,
            frozen.Ruleset,
            isolatedState,
            selected,
            captureSemanticEvents: true);

        Assert(frozen.Success
               && projected.Semantics.ContextSelfHpLoss == 1d
               && projected.Semantics.MinimumHpDuringAction == 0d
               && !safe
               && safetyReason.Contains("fatal", StringComparison.Ordinal)
               && applied.Success
               && applied.Outcome == CombatActionApplicationOutcome.Applied
               && applied.BattleOutcome == CombatSimulationOutcome.Defeat
               && applied.TerminationReason == CombatTerminationReason.Defeat
               && applied.FailureKind == CombatActionApplicationFailureKind.None
               && unavailable.FailureKind
                   == CombatActionApplicationFailureKind.RuntimeContinuationUnavailable
               && unavailable.BranchFidelity
                   == CombatSimulationBranchFidelity.RuntimeContinuationUnavailable
               && isolated.Success
               && isolated.CampaignVariables.GetValueOrDefault("branch-fact")
                   == "mutated"
               && !isolatedScenario.CampaignVariables.ContainsKey(
                   "branch-fact"),
            "fatal action context is projected before search, a physically terminal action remains successfully applied, and runtime branches either isolate scenario state or fail closed when exact continuation is unavailable");
    }

    private static CombatBattleState State(int playerHp, int enemyHp)
    {
        return new CombatBattleState
        {
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            ActionSequence = 0,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    Kind = CombatSimulationActorKind.Player,
                    Hp = playerHp,
                    MaxHp = 20
                },
                new CombatActorState
                {
                    ActorId = 2,
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = enemyHp,
                    MaxHp = enemyHp
                }
            }
        };
    }

    private static CombatSimulationAction Action()
    {
        return new CombatSimulationAction
        {
            ActorId = 1,
            TargetActorId = 2,
            CardInstanceId = 41,
            DefinitionId = "test-card"
        };
    }

    private static CombatSimulationEvent Event(
        long sequence,
        CombatSimulationEventKind kind,
        int targetActorId,
        int amount,
        string rewardId,
        string handlerId,
        string message = "",
        int previousAmount = 0,
        int currentAmount = 0)
    {
        return new CombatSimulationEvent
        {
            Sequence = sequence,
            ParentSequence = sequence == 1 ? 0 : sequence - 1,
            CausalChainId = 1,
            SourceActionId = 1,
            TriggerWave = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            SourceActorId = 1,
            TargetActorId = targetActorId,
            CardInstanceId = 41,
            Kind = kind,
            DefinitionId = rewardId,
            SourceRewardId = rewardId,
            HandlerId = handlerId,
            Amount = amount,
            RawAmount = amount,
            DurabilityAmount = Math.Max(0, amount),
            Message = message,
            PreviousAmount = previousAmount,
            CurrentAmount = currentAmount
        };
    }
}
