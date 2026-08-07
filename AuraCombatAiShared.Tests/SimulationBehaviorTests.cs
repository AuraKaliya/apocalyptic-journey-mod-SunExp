using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

internal static class CombatAiSimulationBehaviorTests
{
    public static CombatAiSimulationTestContext Run()
    {
        var simulationRules = BuildSimulationRuleset();
        Assert(simulationRules.Success
               && simulationRules.Ruleset.CardCount == 4
               && simulationRules.Ruleset.EnemyCount == 1
               && simulationRules.Ruleset.StatusCount == 2,
            "headless combat ruleset freezes authoritative card, enemy and status definitions");
        simulationRules.Ruleset.TryGetCard("strike", out var mutableCardSnapshot);
        mutableCardSnapshot.Cost = 99;
        simulationRules.Ruleset.TryGetCard("strike", out var unchangedCardSnapshot);
        Assert(unchangedCardSnapshot.Cost == 1,
            "frozen ruleset lookups return defensive definition snapshots");
        var simulationScenario = BuildSimulationScenario(seed: 42UL, CombatSimulationTraceLevel.Full);
        var simulationEngine = new CombatSimulationEngine();
        var firstSimulation = simulationEngine.Run(
            simulationScenario,
            simulationRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        var repeatedSimulation = simulationEngine.Run(
            BuildSimulationScenario(seed: 42UL, CombatSimulationTraceLevel.Full),
            simulationRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        Assert(firstSimulation.Outcome == CombatSimulationOutcome.Victory
               && firstSimulation.TerminationReason == CombatTerminationReason.Victory
               && firstSimulation.SemanticCoverage == 1d
               && firstSimulation.Metrics.CardsPlayed > 0
               && firstSimulation.Events.Any(item => item.Kind == CombatSimulationEventKind.CardPlayed)
               && firstSimulation.Events.Any(item => item.Kind == CombatSimulationEventKind.IntentSelected),
            "headless engine runs the complete player/enemy turn lifecycle and records a causal trace");
        Assert(firstSimulation.Events
                   .Where(item => item.Kind == CombatSimulationEventKind.IntentSelected)
                   .GroupBy(item => new { item.Turn, item.SourceActorId })
                   .Any(group => group.Count() == 2
                                 && group.Select(item => item.DefinitionId)
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .Count() == 2),
            "an enemy turn pops and records every ordered intent slot without replacement");
        Assert(firstSimulation.FinalStateHash == repeatedSimulation.FinalStateHash
               && firstSimulation.Events.Select(item => item.Kind)
                   .SequenceEqual(repeatedSimulation.Events.Select(item => item.Kind)),
            "same ruleset, scenario and seed produce a deterministic combat replay");
        var matchingTrace = CombatTraceComparer.Compare(firstSimulation.Events, repeatedSimulation.Events);
        var changedTraceEvent = repeatedSimulation.Events.First(item =>
            item.Kind == CombatSimulationEventKind.DamageDealt);
        changedTraceEvent.Amount++;
        var changedTrace = CombatTraceComparer.Compare(firstSimulation.Events, repeatedSimulation.Events);
        Assert(matchingTrace.Equivalent
               && !changedTrace.Equivalent
               && changedTrace.FirstDifference?.Field == "Amount",
            "trace comparison reports the first divergent combat fact");
        Assert(firstSimulation.FinalState.Cards.Count
               == firstSimulation.FinalState.DrawPile.Count
                  + firstSimulation.FinalState.Hand.Count
                  + firstSimulation.FinalState.DiscardPile.Count
                  + firstSimulation.FinalState.ExhaustPile.Count,
            "every simulated card instance remains in exactly one authoritative zone");
        Assert(firstSimulation.Metrics.BlockGained >= 2
               && firstSimulation.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.BlockGained
                   && item.ParentSequence > 0),
            "status triggers enqueue deterministic commands instead of mutating state recursively");
        var normalTimingScenario = BuildSimulationScenario(
            seed: 44UL,
            CombatSimulationTraceLevel.Full);
        normalTimingScenario.InitialDiscardCards.Add("guard");
        normalTimingScenario.DirectHpLossAfterPlayerCard = 1;
        normalTimingScenario.MovePlayedCardAfterResolution = false;
        var normalTimingResult = simulationEngine.Run(
            normalTimingScenario,
            simulationRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        var normalFirstPlayed = normalTimingResult.Events.First(item =>
            item.Kind == CombatSimulationEventKind.CardPlayed);
        var normalMoved = normalTimingResult.Events.First(item =>
            item.CardInstanceId == normalFirstPlayed.CardInstanceId
            && (item.Kind == CombatSimulationEventKind.CardDiscarded
                || item.Kind == CombatSimulationEventKind.CardExhausted
                || item.Kind == CombatSimulationEventKind.CardCreated));
        Assert(normalMoved.Sequence < normalFirstPlayed.Sequence
               && normalTimingResult.FinalState.Cards.Count
               == normalTimingScenario.Player.Deck.Count + 1
               && normalTimingResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.DamageDealt
                   && item.DefinitionId == "difficulty:player-card-hp-loss"),
            "normal timing moves a played card before resolution, seeds the discard pile, and applies per-card hp loss");
        var lateTimingScenario = BuildSimulationScenario(
            seed: 44UL,
            CombatSimulationTraceLevel.Full);
        lateTimingScenario.MovePlayedCardAfterResolution = true;
        var lateTimingResult = simulationEngine.Run(
            lateTimingScenario,
            simulationRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        var lateFirstPlayed = lateTimingResult.Events.First(item =>
            item.Kind == CombatSimulationEventKind.CardPlayed);
        var lateMoved = lateTimingResult.Events.First(item =>
            item.CardInstanceId == lateFirstPlayed.CardInstanceId
            && (item.Kind == CombatSimulationEventKind.CardDiscarded
                || item.Kind == CombatSimulationEventKind.CardExhausted
                || item.Kind == CombatSimulationEventKind.CardCreated));
        Assert(lateFirstPlayed.Sequence < lateMoved.Sequence,
            "late-throw difficulty moves the played card only after its effects resolve");

        var lifecycleRules = new CombatRulesetBuilder("card-lifecycle-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "unusable-curse",
                Cost = 0,
                Tags = { "Unusable" },
                DrawEffects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.ModifyVariable,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "DrawMark",
                        Amount = 1
                    }
                },
                DiscardEffects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.ModifyVariable,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "DropMark",
                        Amount = 1
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "burnout-set-hp",
                Cost = 0,
                Exhaust = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.SetHp,
                        Target = CombatSimulationTarget.Self,
                        Amount = 10
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 100
                    }
                },
                DiscardEffects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.ModifyVariable,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "DropMark",
                        Amount = 1
                    }
                },
                RequiresEnemyTarget = true
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "clamped-set-hp",
                Cost = 0,
                Exhaust = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.SetHp,
                        Target = CombatSimulationTarget.Self,
                        Amount = 25
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 100
                    }
                },
                RequiresEnemyTarget = true
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "lifecycle-dummy",
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
        var unusableCurseResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "unusable-curse-lifecycle",
                RulesetVersion = "card-lifecycle-v1",
                Seed = 1,
                InitialDraw = 1,
                DrawPerTurn = 0,
                TraceLevel = CombatSimulationTraceLevel.Full,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tester",
                    MaxHp = 30,
                    CurrentHp = 30,
                    Deck = { "unusable-curse" }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "lifecycle-dummy" }
                },
                Limits = new CombatSimulationLimits
                {
                    MaximumTurns = 1,
                    MaximumActions = 10,
                    MaximumCommands = 100
                }
            },
            lifecycleRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        Assert(lifecycleRules.Success
               && unusableCurseResult.FinalState.Player?.Variables["DrawMark"] == 1d
               && unusableCurseResult.FinalState.Player?.Variables["DropMark"] == 1d
               && !unusableCurseResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.CardPlayed)
               && unusableCurseResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.CardDrawn)
               && unusableCurseResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.CardDiscarded),
            "unusable cards execute native draw and discard scripts without becoming legal plays");
        var burnoutSetHpResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "burnout-set-hp",
                RulesetVersion = "card-lifecycle-v1",
                Seed = 2,
                InitialDraw = 1,
                DrawPerTurn = 0,
                TraceLevel = CombatSimulationTraceLevel.Full,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tester",
                    MaxHp = 30,
                    CurrentHp = 30,
                    Deck = { "burnout-set-hp" }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "lifecycle-dummy" }
                }
            },
            lifecycleRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        Assert(burnoutSetHpResult.Outcome == CombatSimulationOutcome.Victory
               && burnoutSetHpResult.FinalState.Player?.Hp == 10
               && !burnoutSetHpResult.FinalState.Player!.Variables.ContainsKey("DropMark")
               && burnoutSetHpResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.CardExhausted)
               && !burnoutSetHpResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.Healed),
            "SetHp assigns health directly and burnout bypasses native discard scripts");
        var clampedSetHpResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "clamped-set-hp",
                RulesetVersion = "card-lifecycle-v1",
                Seed = 3,
                InitialDraw = 1,
                DrawPerTurn = 0,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tester",
                    MaxHp = 24,
                    CurrentHp = 20,
                    Deck = { "clamped-set-hp" }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "lifecycle-dummy" }
                }
            },
            lifecycleRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        Assert(clampedSetHpResult.Outcome == CombatSimulationOutcome.Victory
               && clampedSetHpResult.FinalState.Player?.Hp == 24,
            "SetHp clamps the assigned value to current maximum HP after role transformations");

        var fullHandCreationRules = new CombatRulesetBuilder("full-hand-creation-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "overflow-generator",
                Cost = 0,
                Exhaust = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.CreateCard,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "overflow-a",
                        Amount = 1,
                        DestinationZone = CombatCardZone.Hand
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.CreateCard,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "overflow-b",
                        Amount = 1,
                        DestinationZone = CombatCardZone.Hand
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "overflow-filler",
                Cost = 99,
                Tags = { "Unusable" }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "overflow-a",
                Cost = 99,
                Tags = { "Retain", "Unusable" },
                DiscardEffects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.CreateCard,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "overflow-a",
                        Amount = 1,
                        DestinationZone = CombatCardZone.Hand
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "overflow-b",
                Cost = 99,
                Tags = { "Retain", "Unusable" },
                DiscardEffects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.CreateCard,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "overflow-b",
                        Amount = 1,
                        DestinationZone = CombatCardZone.Hand
                    }
                }
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "overflow-dummy",
                MaxHp = 100,
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
        var fullHandCreationResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "full-hand-created-card-overflow",
                RulesetVersion = "full-hand-creation-v1",
                Seed = 3,
                InitialDraw = 2,
                DrawPerTurn = 1,
                HandLimit = 2,
                MovePlayedCardAfterResolution = true,
                TraceLevel = CombatSimulationTraceLevel.Full,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tester",
                    MaxHp = 30,
                    CurrentHp = 30,
                    Deck = { "overflow-generator", "overflow-filler" }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "overflow-dummy" }
                },
                Limits = new CombatSimulationLimits
                {
                    MaximumTurns = 3,
                    MaximumActions = 10,
                    MaximumCommands = 100,
                    MaximumCommandsPerAction = 25
                }
            },
            fullHandCreationRules.Ruleset,
            new PlayCardOnceThenEndPolicy("overflow-generator"));
        var overflowCreated = fullHandCreationResult.Events
            .Where(item =>
                item.Kind == CombatSimulationEventKind.CardCreated
                && (item.DefinitionId == "overflow-a"
                    || item.DefinitionId == "overflow-b"))
            .OrderBy(item => item.Sequence)
            .ToList();
        var overflowDrawn = fullHandCreationResult.Events
            .Where(item =>
                item.Kind == CombatSimulationEventKind.CardDrawn
                && overflowCreated.Any(created =>
                    created.CardInstanceId == item.CardInstanceId))
            .OrderBy(item => item.Sequence)
            .ToList();
        Assert(fullHandCreationRules.Success
               && fullHandCreationResult.Outcome != CombatSimulationOutcome.Invalid
               && overflowCreated.Count == 2
               && overflowCreated[0].DefinitionId == "overflow-a"
               && overflowCreated[1].DefinitionId == "overflow-b"
               && !fullHandCreationResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.CardDiscarded
                   && overflowCreated.Any(created =>
                       created.CardInstanceId == item.CardInstanceId))
               && overflowDrawn.Count == 2
               && overflowDrawn[0].DefinitionId == "overflow-b"
               && overflowDrawn[1].DefinitionId == "overflow-a",
            "cards created into a full hand stack on the draw-pile top without firing discard effects");

        var triggerRules = new CombatRulesetBuilder("trigger-semantics-v1")
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "pulse",
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "every-second-action",
                        EventKind = CombatSimulationEventKind.ActionStarted,
                        OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                        EveryNthEvent = 2,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.GainBlock,
                                Target = CombatSimulationTarget.Self,
                                Amount = 2
                            }
                        }
                    }
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "bleed",
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "bleed-action",
                        EventKind = CombatSimulationEventKind.ActionStarted,
                        OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.DirectHpLoss,
                                Target = CombatSimulationTarget.Self,
                                DefinitionId = "bleed",
                                AmountExpression = new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                    Key = "bleed"
                                }
                            },
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.DirectHpLoss,
                                Target = CombatSimulationTarget.Self,
                                DefinitionId = "bleed",
                                AmountExpression = new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                    Key = "bleed"
                                },
                                ConditionExpression = new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.GreaterThan,
                                    Arguments =
                                    {
                                        new CombatSimulationValueExpression
                                        {
                                            Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                            Key = "bleed"
                                        },
                                        new CombatSimulationValueExpression
                                        {
                                            Operation = CombatSimulationValueOperation.Constant,
                                            Constant = 30
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "bleed-ward",
                DecayAtRoundEnd = false,
                MaximumStacks = 1,
                DynamicModifiersPerStack =
                {
                    ["DirectHpLossTaken.bleed"] = -1d
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "counter",
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "counter-attack",
                        EventKind = CombatSimulationEventKind.ActionStarted,
                        OwnerRelation = CombatStatusTriggerOwnerRelation.EventTarget,
                        RequiredActionTag = "Attack",
                        ConsumeStacks = int.MaxValue,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.Damage,
                                Target = CombatSimulationTarget.EventSource,
                                DefinitionId = "counter",
                                AmountExpression = new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                    Key = "counter"
                                }
                            }
                        }
                    }
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "armor",
                DecayAtRoundEnd = false,
                MaximumStacks = 1,
                DynamicModifiersPerStack =
                {
                    ["ConversionRate"] = 1d
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "stasis",
                DecayAtRoundEnd = true,
                ReducePerTurn = 1,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "skip-round",
                        EventKind = CombatSimulationEventKind.TurnStarted,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.SkipTurn,
                                Target = CombatSimulationTarget.Self,
                                Amount = 1
                            }
                        }
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "wait-a",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 0
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "wait-b",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 0
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "overheal",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Heal,
                        Target = CombatSimulationTarget.Self,
                        Amount = 10
                    }
                }
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "attacker",
                MaxHp = 5,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "attack",
                        Tags = { "Attack" },
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.Damage,
                                Target = CombatSimulationTarget.Player,
                                Amount = 3
                            }
                        }
                    }
                }
            })
            .Freeze();
        var triggerResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "trigger-semantics",
                RulesetVersion = "trigger-semantics-v1",
                Seed = 5,
                InitialDraw = 2,
                DrawPerTurn = 0,
                Player = new CombatPlayerSetup
                {
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck = { "wait-a", "wait-b" },
                    InitialStatuses =
                    {
                        new CombatInitialStatus { StatusId = "pulse" },
                        new CombatInitialStatus { StatusId = "bleed", Stacks = 31 },
                        new CombatInitialStatus { StatusId = "bleed-ward" },
                        new CombatInitialStatus { StatusId = "counter", Stacks = 5 }
                    }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "attacker" }
                },
                TraceLevel = CombatSimulationTraceLevel.Full
            },
            triggerRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        var pulseState = triggerResult.FinalState.Player?.Statuses.Single(item =>
            item.StatusId == "pulse");
        var overhealScenario = new CombatScenarioDefinition
        {
            ScenarioId = "overheal-conversion",
            RulesetVersion = "trigger-semantics-v1",
            Player = new CombatPlayerSetup
            {
                MaxHp = 20,
                CurrentHp = 20,
                Deck = { "overheal" }
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "attacker" }
            }
        };
        var overhealState = new CombatBattleState
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
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 20,
                    MaxHp = 20,
                    Energy = 3,
                    BaseEnergy = 3,
                    Statuses =
                    {
                        new CombatStatusState { StatusId = "armor", Stacks = 1 }
                    }
                },
                new CombatActorState
                {
                    ActorId = 2,
                    Kind = CombatSimulationActorKind.Enemy,
                    DefinitionId = "attacker",
                    Hp = 5,
                    MaxHp = 5
                }
            },
            Cards =
            {
                new CombatCardInstanceState { InstanceId = 1, CardId = "overheal" }
            },
            Hand = { 1 }
        };
        var overhealAction = simulationEngine.GetLegalPlayerActions(
                overhealScenario,
                triggerRules.Ruleset,
                overhealState)
            .Single(item => item.Kind == CombatSimulationActionKind.PlayCard);
        var overhealResult = simulationEngine.ForkAndApplyPlayerAction(
            overhealScenario,
            triggerRules.Ruleset,
            overhealState,
            overhealAction);
        var stasisScenario = new CombatScenarioDefinition
        {
            ScenarioId = "stasis-skip",
            RulesetVersion = "trigger-semantics-v1",
            Seed = 6,
            InitialDraw = 2,
            DrawPerTurn = 2,
            Player = new CombatPlayerSetup
            {
                MaxHp = 20,
                CurrentHp = 20,
                Deck = { "wait-a", "wait-b" },
                InitialStatuses =
                {
                    new CombatInitialStatus { StatusId = "stasis" }
                }
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "attacker" }
            },
            Limits = new CombatSimulationLimits { MaximumTurns = 3 }
        };
        var stasisResult = simulationEngine.Run(
            stasisScenario,
            triggerRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        Assert(triggerRules.Success
               && triggerResult.Outcome == CombatSimulationOutcome.Victory
               && triggerResult.FinalPlayerHp == 19
               && triggerResult.Metrics.BlockGained == 2
               && pulseState?.TriggerCounts["every-second-action"] == 2
               && triggerResult.Events.Count(item =>
                   item.Kind == CombatSimulationEventKind.DamageDealt
                   && item.DefinitionId == "bleed"
                   && item.Amount == 0) == 4
               && triggerResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.DamageDealt
                   && item.DefinitionId == "counter"
                   && item.Amount == 5)
                && triggerResult.FinalState.Player?.Statuses.All(item =>
                    item.StatusId != "counter") == true
                && overhealResult.Success
               && overhealResult.State.Player?.Block == 10
               && stasisResult.TurnsSummary.First().Actions == 0
               && stasisResult.Events.First(item =>
                   item.Kind == CombatSimulationEventKind.CardPlayed).Turn == 2,
            "status triggers preserve owner relation, nth-event counters, conditional effects, damage filters, and enemy pre-action counterattacks");

        var ritualRules = new CombatRulesetBuilder("ritual-semantics-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "ritual",
                Cost = 0,
                Tags = { "Ritual" },
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 0
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "roll",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.EmitEvent,
                        Target = CombatSimulationTarget.Self,
                        Amount = 1,
                        DefinitionId = "roll",
                        EmittedEventKind = CombatSimulationEventKind.DiceChecked
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 1,
                        RandomChoiceGroup = "roll-result",
                        RandomChoiceWeight = 1
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 2,
                        RandomChoiceGroup = "roll-result",
                        RandomChoiceWeight = 1
                    }
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "echo",
                DecayAtRoundEnd = false
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "ritual-cycle",
                DecayAtRoundEnd = false,
                Tags = { "Ritual" },
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "fourth-ritual",
                        EventKind = CombatSimulationEventKind.ActionStarted,
                        OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                        RequiredActionTag = "Ritual",
                        CounterKey = "ThisCount",
                        CounterIncrementMode = CombatStatusCounterIncrementMode.Fixed,
                        MinimumCounterValue = 4,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.GainEnergy,
                                Target = CombatSimulationTarget.Self,
                                AmountExpression = new CombatSimulationValueExpression
                                {
                                    Operation = CombatSimulationValueOperation.Add,
                                    Arguments =
                                    {
                                        new CombatSimulationValueExpression
                                        {
                                            Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                            Key = "ritual-cycle"
                                        },
                                        new CombatSimulationValueExpression
                                        {
                                            Operation = CombatSimulationValueOperation.SourceStatusStacks,
                                            Key = "echo"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "fate",
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "dice-damage",
                        EventKind = CombatSimulationEventKind.DiceChecked,
                        OwnerRelation = CombatStatusTriggerOwnerRelation.EventSource,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.Damage,
                                Target = CombatSimulationTarget.AllOpponents,
                                Amount = 2
                            }
                        }
                    }
                }
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "dummy",
                MaxHp = 100,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.GainBlock,
                                Target = CombatSimulationTarget.Self,
                                Amount = 0
                            }
                        }
                    }
                }
            })
            .Freeze();
        var ritualResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "ritual-counter",
                RulesetVersion = "ritual-semantics-v1",
                InitialDraw = 4,
                DrawPerTurn = 0,
                Player = new CombatPlayerSetup
                {
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck = { "ritual", "ritual", "ritual", "ritual" },
                    InitialStatuses =
                    {
                        new CombatInitialStatus { StatusId = "ritual-cycle", Stacks = 2 },
                        new CombatInitialStatus { StatusId = "echo", Stacks = 1 }
                    }
                },
                Enemies = { new CombatEnemySetup { EnemyId = "dummy" } },
                Limits = new CombatSimulationLimits { MaximumTurns = 1 }
            },
            ritualRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        var ritualCycleState = ritualResult.FinalState.Player?.Statuses.Single(item =>
            item.StatusId == "ritual-cycle");
        var rollScenario = new CombatScenarioDefinition
        {
            ScenarioId = "exclusive-dice-choice",
            RulesetVersion = "ritual-semantics-v1",
            Seed = 9,
            InitialDraw = 1,
            DrawPerTurn = 0,
            Player = new CombatPlayerSetup
            {
                MaxHp = 20,
                CurrentHp = 20,
                Deck = { "roll" },
                InitialStatuses = { new CombatInitialStatus { StatusId = "fate" } }
            },
            Enemies = { new CombatEnemySetup { EnemyId = "dummy", HpScale = 0.02d } }
        };
        var rollResult = simulationEngine.Run(
            rollScenario,
            ritualRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        Assert(ritualRules.Success
               && ritualCycleState?.TriggerCounts["ThisCount"] == 4
               && ritualResult.FinalState.Player?.Energy == 6
               && rollResult.Outcome == CombatSimulationOutcome.Victory
               && rollResult.Metrics.BlockGained is 1 or 2
               && rollResult.Events.Count(item =>
                   item.Kind == CombatSimulationEventKind.DiceChecked) == 1,
            "ritual counters, echo scaling, explicit dice checks, and exclusive random branches are deterministic");

        var surplusEnergyRules = new CombatRulesetBuilder(
                "surplus-energy-transition-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "charge-surplus",
                Cost = 0,
                Exhaust = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainEnergy,
                        Target = CombatSimulationTarget.Self,
                        Amount = 5
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "finish-after-charge",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 50
                    }
                }
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "surplus-dummy",
                MaxHp = 20,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.GainBlock,
                                Target = CombatSimulationTarget.Self,
                                Amount = 0
                            }
                        }
                    }
                }
            })
            .Freeze();
        var preserveSurplusPolicy = new PreserveSurplusThenFinishPolicy();
        var preserveSurplusResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "preserve-surplus-between-turns",
                RulesetVersion = "surplus-energy-transition-v1",
                InitialDraw = 2,
                DrawPerTurn = 1,
                Player = new CombatPlayerSetup
                {
                    MaxHp = 20,
                    CurrentHp = 20,
                    BaseEnergy = 3,
                    Deck = { "charge-surplus", "finish-after-charge" }
                },
                Enemies = { new CombatEnemySetup { EnemyId = "surplus-dummy" } },
                Limits = new CombatSimulationLimits { MaximumTurns = 3 }
            },
            surplusEnergyRules.Ruleset,
            preserveSurplusPolicy);
        Assert(surplusEnergyRules.Success
               && preserveSurplusPolicy.SecondTurnEnergy == 8,
            "authoritative simulator preserves above-cap energy at the next player turn instead of resetting it to base energy"
            + $" (rules={surplusEnergyRules.Success}, outcome={preserveSurplusResult.Outcome},"
            + $" secondTurnEnergy={preserveSurplusPolicy.SecondTurnEnergy},"
            + $" finalEnergy={preserveSurplusResult.FinalState.Player?.Energy})");

        var aiSimulation = simulationEngine.Run(
            BuildSimulationScenario(seed: 43UL, CombatSimulationTraceLevel.Actions),
            simulationRules.Ruleset,
            new CombatDecisionSimulationPolicy(
                new CombatDecisionProfile
                {
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 128,
                    SearchNodeBudget = 1024,
                    SearchMaxPly = 8
                }));
        Assert(aiSimulation.Outcome == CombatSimulationOutcome.Victory
               && aiSimulation.PolicyId.StartsWith("aura-combat-decision:", StringComparison.Ordinal),
            "existing Chance-PUCT decision AI consumes projected headless observations and completes a battle");

        var exploratorySimulation = simulationEngine.Run(
            BuildSimulationScenario(seed: 44UL, CombatSimulationTraceLevel.Summary),
            simulationRules.Ruleset,
            new CombatDecisionSimulationPolicy(
                new CombatDecisionProfile
                {
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 96,
                    SearchNodeBudget = 768,
                    SearchMaxPly = 6
                },
                exploration: new CombatSelfPlayExplorationOptions
                {
                    Probability = 1d,
                    Temperature = 1.25d,
                    RootDirichletAlpha = 0.30d,
                    RootNoiseFraction = 0.25d,
                    RandomSeed = 44
                }));
        Assert(exploratorySimulation.Metrics.ExplorationDecisions > 0
               && exploratorySimulation.Metrics
                      .RootMaximumVisitShareSamples > 0
               && exploratorySimulation.Metrics
                      .RootMaximumVisitShareTotal > 0d,
            "self-play injects deterministic Dirichlet noise before root search and records effective exploration telemetry");

        var authoritativeTeacherSimulation = simulationEngine.Run(
            BuildSimulationScenario(seed: 45UL, CombatSimulationTraceLevel.Summary),
            simulationRules.Ruleset,
            new CombatAuthoritativeBranchTeacherPolicy(
                new CombatDecisionSimulationPolicy(
                    new CombatDecisionProfile
                    {
                        SearchBudgetMode = "fixed",
                        SearchSimulationBudget = 96,
                        SearchNodeBudget = 768,
                        SearchMaxPly = 6
                    }),
                new CombatAuthoritativeTeacherOptions
                {
                    AuditProbability = 1d,
                    RandomSeed = 45
                }));
        Assert(authoritativeTeacherSimulation.Metrics
                   .AuthoritativeActionsAudited > 0
               && authoritativeTeacherSimulation.Metrics
                      .AuthoritativeSelectedActionsAudited > 0
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .AuditedSources.Count > 0
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .SourceKindAudits.Count > 0
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .InvalidActions == 0
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .SelectedInvalidActions == 0
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .ValidActions
                  == authoritativeTeacherSimulation.Metrics
                      .AuthoritativeActionsAudited
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .SelectedValidActions
                  == authoritativeTeacherSimulation.Metrics
                      .AuthoritativeSelectedActionsAudited
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .SelectedContextAdjustedActions
                  == authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .SelectedExplainedActions
               && authoritativeTeacherSimulation.Metrics.SemanticAudit
                      .SelectedUnexplainedMismatchActions
                  == authoritativeTeacherSimulation.Metrics
                      .AuthoritativeSelectedSemanticMismatches
               && (authoritativeTeacherSimulation.Metrics
                       .AuthoritativeSelectedSemanticMismatches == 0
                   || authoritativeTeacherSimulation.Metrics.SemanticAudit
                          .SelectedUnexplainedMismatchSources.Values.Sum()
                      == authoritativeTeacherSimulation.Metrics
                          .AuthoritativeSelectedSemanticMismatches),
            "teacher policy audits projected choices through authoritative immutable action branches");

        var teacherLegalityRules = new CombatRulesetBuilder(
                "authoritative-teacher-legality-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "teacher-safe",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 1
                    }
                },
                RequiresEnemyTarget = true
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "teacher-prohibited",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 100
                    }
                },
                RequiresEnemyTarget = true
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "teacher-dummy",
                MaxHp = 50,
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
        CombatAuthoritativeBranchTeacherPolicy teacherLegalityPolicy;
        using (CombatAiRegistry.RegisterRoleStrategyProvider(
                   "Tests",
                   "teacher-legality",
                   new ProhibitSourceRoleStrategyProvider("teacher-prohibited"),
                   10000))
        {
            teacherLegalityPolicy = new CombatAuthoritativeBranchTeacherPolicy(
                new CombatDecisionSimulationPolicy(new CombatDecisionProfile
                {
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 8,
                    SearchNodeBudget = 64,
                    SearchMaxPly = 2,
                    SearchMinimumSimulations = 1
                }),
                new CombatAuthoritativeTeacherOptions
                {
                    AuditProbability = 1d,
                    MinimumOverrideGain = 0d,
                    RandomSeed = 46
                });
        }
        var teacherLegalityResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "authoritative-teacher-legality",
                RulesetVersion = "authoritative-teacher-legality-v1",
                Seed = 46,
                InitialDraw = 2,
                DrawPerTurn = 0,
                Player = new CombatPlayerSetup
                {
                    RoleId = "tester",
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck = { "teacher-safe", "teacher-prohibited" }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "teacher-dummy" }
                },
                Limits = new CombatSimulationLimits
                {
                    MaximumTurns = 1,
                    MaximumActions = 10,
                    MaximumCommands = 100
                },
                TraceLevel = CombatSimulationTraceLevel.Full
            },
            teacherLegalityRules.Ruleset,
            teacherLegalityPolicy);
        Assert(teacherLegalityRules.Success
               && teacherLegalityPolicy.LastDecision!.Candidates.Any(item =>
                   item.Action.SourceId == "teacher-prohibited" && !item.Legal)
               && teacherLegalityResult.Events.All(item =>
                   item.Kind != CombatSimulationEventKind.CardPlayed
                   || item.DefinitionId != "teacher-prohibited")
               && teacherLegalityPolicy.LastObservation!.Features
                      .GetValueOrDefault("roleStrategy:test.prepared-state") == 1d,
            "authoritative teacher intersects simulator actions with decision legality and exposes prepared role-state telemetry");

        var semanticAuditState = new CombatBattleState
        {
            PlayerActorId = 1,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 20,
                    MaxHp = 30
                },
                new CombatActorState
                {
                    ActorId = 2,
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 5,
                    MaxHp = 5
                }
            }
        };
        var semanticAuditEvents = new List<CombatSimulationEvent>
        {
            new()
            {
                Kind = CombatSimulationEventKind.DamageDealt,
                TargetActorId = 2,
                Amount = 5
            },
            new()
            {
                Kind = CombatSimulationEventKind.CardDrawn,
                TargetActorId = 1,
                Amount = 1
            }
        };
        var matchingSemanticAudit = CombatSemanticAuditor.Audit(
            semanticAuditState,
            semanticAuditEvents,
            new CombatActionSemantics { Damage = 5d, Draw = 1d },
            new CombatSimulationAction { DefinitionId = "audit-card" });
        var mismatchingSemanticAudit = CombatSemanticAuditor.Audit(
            semanticAuditState,
            semanticAuditEvents,
            new CombatActionSemantics { Defend = 10d },
            new CombatSimulationAction { DefinitionId = "audit-card" });
        var invalidSemanticAudit = CombatSemanticAuditor.Audit(
            semanticAuditState,
            semanticAuditState,
            Array.Empty<CombatSimulationEvent>(),
            new CombatActionSemantics { Damage = 5d },
            new CombatSimulationAction { DefinitionId = "audit-card" },
            null);
        Assert(!matchingSemanticAudit.Mismatch
               && mismatchingSemanticAudit.MismatchKinds.Contains("damage")
               && mismatchingSemanticAudit.MismatchKinds.Contains("defend")
               && invalidSemanticAudit.Invalid
               && !invalidSemanticAudit.Mismatch
               && invalidSemanticAudit.Status == CombatSemanticAuditStatus.Invalid,
            "semantic auditing compares projected action meaning with causal authoritative events instead of noisy net state deltas");
        var nativeIntrinsicAudit = CombatSemanticAuditor.Audit(
            semanticAuditState,
            new[]
            {
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 41,
                    HandlerId = "native:universalcard_16:on-use",
                    SourceRewardId = "universalcard_16",
                    Kind = CombatSimulationEventKind.BlockGained,
                    TargetActorId = 1,
                    Amount = 8
                },
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 41,
                    HandlerId = "native:relic-trigger",
                    SourceRewardId = "relic_1",
                    Kind = CombatSimulationEventKind.BlockGained,
                    TargetActorId = 1,
                    Amount = 3
                }
            },
            new CombatActionSemantics { Defend = 8d },
            new CombatSimulationAction
            {
                CardInstanceId = 41,
                DefinitionId = "universalcard_16"
            });
        Assert(!nativeIntrinsicAudit.Mismatch
               && nativeIntrinsicAudit.Comparisons.Any(item =>
                   item.Kind == "defend"
                   && item.Classification == "explained"
                   && item.Explanation == "trigger-side-effect"),
            "native on-use events sourced from the played card are intrinsic while downstream reward triggers remain contextual");

        var realizedBefore = semanticAuditState.Clone();
        realizedBefore.ActionSequence = 0;
        realizedBefore.Cards.Add(new CombatCardInstanceState
        {
            InstanceId = 41,
            CardId = "dynamic-native-card"
        });
        realizedBefore.Hand.Add(41);
        var realizedAfter = realizedBefore.Clone();
        realizedAfter.Player!.Hp = 30;
        realizedAfter.FindActor(2)!.Hp = 0;
        realizedAfter.Hand.Remove(41);
        realizedAfter.Cards.Add(new CombatCardInstanceState
        {
            InstanceId = 42,
            CardId = "generated-card"
        });
        realizedAfter.Hand.Add(42);
        var realizedEvents = new List<CombatSimulationEvent>
        {
            new()
            {
                SourceActionId = 1,
                CardInstanceId = 41,
                SourceRewardId = "dynamic-native-card",
                Kind = CombatSimulationEventKind.DamageDealt,
                TargetActorId = 2,
                Amount = 5
            },
            new()
            {
                SourceActionId = 1,
                CardInstanceId = 41,
                SourceRewardId = "dynamic-native-card",
                Kind = CombatSimulationEventKind.VariableChanged,
                TargetActorId = 1,
                DefinitionId = "Hp",
                Message = "Hp",
                Amount = 10
            },
            new()
            {
                SourceActionId = 1,
                CardInstanceId = 42,
                SourceRewardId = "dynamic-native-card",
                Kind = CombatSimulationEventKind.CardDrawn,
                TargetActorId = 1,
                Amount = 1
            },
            new()
            {
                SourceActionId = 1,
                SourceRewardId = "dynamic-native-card",
                Kind = CombatSimulationEventKind.RandomResolved,
                Amount = 1
            }
        };
        var realizedAction = new CombatSimulationAction
        {
            ActorId = 1,
            CardInstanceId = 41,
            TargetActorId = 2,
            DefinitionId = "dynamic-native-card"
        };
        var realizedSemantics = CombatSemanticAuditor.ProjectRealized(
            realizedBefore,
            realizedAfter,
            realizedEvents,
            realizedAction,
            null);
        var realizedAudit = CombatSemanticAuditor.Audit(
            realizedBefore,
            realizedAfter,
            realizedEvents,
            realizedSemantics,
            realizedAction,
            null);
        var untracedCreationAfter = realizedBefore.Clone();
        untracedCreationAfter.Cards.Add(new CombatCardInstanceState
        {
            InstanceId = 43,
            CardId = "untraced-card"
        });
        var untracedCreationSemantics = CombatSemanticAuditor.ProjectRealized(
            realizedBefore,
            untracedCreationAfter,
            realizedEvents.Take(2).ToList(),
            realizedAction,
            null);
        var untracedCreationAudit = CombatSemanticAuditor.Audit(
            realizedBefore,
            untracedCreationAfter,
            realizedEvents.Take(2).ToList(),
            untracedCreationSemantics,
            realizedAction,
            null);
        Assert(realizedSemantics.Damage == 5d
               && realizedSemantics.Heal == 10d
               && realizedSemantics.CardGeneration == 1d
               && realizedSemantics.Draw == 0d
               && realizedSemantics.RandomOutcome
               && realizedAudit.Valid
               && !realizedAudit.Mismatch
               && untracedCreationAudit.InvalidKinds.Contains("card-generation"),
            "authoritative realized projection separates created-to-hand cards from draws, represents direct HP assignment, and still rejects untraced mutations");

        var branchState = new CombatBattleState
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
                    Hp = 30,
                    MaxHp = 30,
                    Energy = 3,
                    BaseEnergy = 3
                },
                new CombatActorState
                {
                    ActorId = 2,
                    InstanceKey = "dummy:branch",
                    Kind = CombatSimulationActorKind.Enemy,
                    DefinitionId = "dummy",
                    Hp = 18,
                    MaxHp = 18,
                    CurrentIntentId = "hit"
                }
            },
            Cards =
            {
                new CombatCardInstanceState { InstanceId = 1, CardId = "strike" }
            },
            Hand = { 1 }
        };
        var branchActions = simulationEngine.GetLegalPlayerActions(
            simulationScenario,
            simulationRules.Ruleset,
            branchState);
        var branchResult = simulationEngine.ForkAndApplyPlayerAction(
            simulationScenario,
            simulationRules.Ruleset,
            branchState,
            branchActions.First(action => action.Kind == CombatSimulationActionKind.PlayCard));
        Assert(branchResult.Success
               && branchState.FindActor(2)?.Hp == 18
               && branchResult.State.FindActor(2)?.Hp == 12
               && branchResult.Events.Any(item => item.Kind == CombatSimulationEventKind.DamageDealt),
            "authoritative action forks support search without mutating the source battle state");

        var unsupportedScenario = BuildSimulationScenario(seed: 1UL, CombatSimulationTraceLevel.Summary);
        unsupportedScenario.Player.Deck.Add("missing-card");
        var unsupportedSimulation = simulationEngine.Run(
            unsupportedScenario,
            simulationRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        Assert(unsupportedSimulation.Outcome == CombatSimulationOutcome.Invalid
               && unsupportedSimulation.TerminationReason == CombatTerminationReason.UnsupportedRule
               && unsupportedSimulation.UnsupportedDefinitions.Contains("card:missing-card"),
            "unknown combat rules fail closed instead of silently contributing zero-value effects");

        var duplicateRewardScenario = BuildSimulationScenario(
            seed: 4UL,
            CombatSimulationTraceLevel.Summary);
        duplicateRewardScenario.RewardRules.Add(new CombatScenarioRewardRule
        {
            RewardId = "stacked-blessing",
            Kind = "Blessing",
            Stacks = 3
        });
        duplicateRewardScenario.RewardRules.Add(new CombatScenarioRewardRule
        {
            RewardId = "stacked-blessing",
            Kind = "Blessing",
            Stacks = 3
        });
        var duplicateRewardSimulation = simulationEngine.Run(
            duplicateRewardScenario,
            simulationRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        Assert(duplicateRewardSimulation.Outcome == CombatSimulationOutcome.Invalid
               && duplicateRewardSimulation.TerminationReason
               == CombatTerminationReason.InvalidScenario
               && duplicateRewardSimulation.UnsupportedDefinitions.Contains(
                   "duplicate-reward-rule:stacked-blessing")
               && duplicateRewardSimulation.RewardVariables.Count == 1,
            "duplicate reward rules fail closed before scripts execute and result projection remains exception-safe");

        var reentrantDiscardScenario = BuildSimulationScenario(
            seed: 5UL,
            CombatSimulationTraceLevel.Full);
        reentrantDiscardScenario.Limits.MaximumTurns = 1;
        var reentrantDiscardSimulation = new CombatSimulationEngine(
                new ReentrantDiscardExtensionFactory())
            .Run(
                reentrantDiscardScenario,
                simulationRules.Ruleset,
                EndTurnSimulationPolicy.Instance);
        var reentrantDiscardZones = reentrantDiscardSimulation.FinalState.DrawPile
            .Concat(reentrantDiscardSimulation.FinalState.Hand)
            .Concat(reentrantDiscardSimulation.FinalState.DiscardPile)
            .Concat(reentrantDiscardSimulation.FinalState.ExhaustPile)
            .ToList();
        Assert(reentrantDiscardSimulation.Outcome != CombatSimulationOutcome.Invalid
               && reentrantDiscardZones.Count
                  == reentrantDiscardSimulation.FinalState.Cards.Count
               && reentrantDiscardZones.Distinct().Count()
                  == reentrantDiscardZones.Count
               && !reentrantDiscardSimulation.UnsupportedDefinitions.Any(item =>
                   item.StartsWith("state-invariant", StringComparison.Ordinal)),
            "reentrant discard callbacks cannot place one card in multiple zones");

        var stackedRewardDefinition = new CombatCampaignDefinition();
        stackedRewardDefinition.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "stacked-blessing",
            Kind = CombatCampaignRewardKind.Blessing,
            FightScript = "noop",
            InitialVariables = new Dictionary<string, string>
            {
                ["counter"] = "7"
            }
        });
        var stackedRewardState = new CombatCampaignState
        {
            Blessings = new List<string>
            {
                "stacked-blessing",
                "stacked-blessing",
                "stacked-blessing"
            }
        };
        var projectedRewardRules = CombatCampaignRewardRuleProjector.Build(
            stackedRewardDefinition,
            stackedRewardState);
        Assert(projectedRewardRules.Count == 1
               && projectedRewardRules[0].RewardId == "stacked-blessing"
               && projectedRewardRules[0].Stacks == 3
               && projectedRewardRules[0].Variables["counter"] == "7",
            "stacked campaign blessings project to one runtime rule with an explicit stack count");

        var mismatchedScenario = BuildSimulationScenario(seed: 2UL, CombatSimulationTraceLevel.Summary);
        mismatchedScenario.RulesetVersion = "wrong-version";
        var mismatchedSimulation = simulationEngine.Run(
            mismatchedScenario,
            simulationRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        Assert(mismatchedSimulation.Outcome == CombatSimulationOutcome.Invalid
               && mismatchedSimulation.TerminationReason == CombatTerminationReason.InvalidScenario,
            "scenario execution rejects a mismatched frozen ruleset version");

        var loopScenario = BuildSimulationScenario(seed: 3UL, CombatSimulationTraceLevel.Full);
        loopScenario.Player.Deck.Clear();
        loopScenario.Player.Deck.Add("loop-seed");
        loopScenario.Player.InitialStatuses.Clear();
        loopScenario.InitialDraw = 1;
        loopScenario.DrawPerTurn = 1;
        loopScenario.Limits.MaximumTriggerWavesPerAction = 3;
        var loopSimulation = simulationEngine.Run(
            loopScenario,
            simulationRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        Assert(loopSimulation.Outcome == CombatSimulationOutcome.Invalid
               && loopSimulation.TerminationReason == CombatTerminationReason.TriggerLoop
               && loopSimulation.FailureDiagnostics.LimitScope == "trigger-wave"
               && loopSimulation.FailureDiagnostics.RecentCommands.Count > 0
               && !string.IsNullOrWhiteSpace(
                   loopSimulation.FailureDiagnostics.PendingCommand),
            "trigger wave budgets terminate self-reinforcing status loops with actionable command diagnostics");

        var isolatedRandomA = new CombatRandomCounterState();
        var isolatedRandomB = new CombatRandomCounterState();
        var deckA1 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomA, "deck.shuffle", out _);
        _ = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomA, "enemy.intent:x", out _);
        var deckA2 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomA, "deck.shuffle", out _);
        var deckB1 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomB, "deck.shuffle", out _);
        var deckB2 = CombatDeterministicRandom.NextUInt64(99UL, isolatedRandomB, "deck.shuffle", out _);
        Assert(deckA1 == deckB1 && deckA2 == deckB2,
            "named deterministic random streams isolate deck order from unrelated enemy random calls");

        var serialBatch = new CombatBatchRunner().Run(
            new CombatBatchRequest
            {
                Scenario = BuildSimulationScenario(seed: 1UL, CombatSimulationTraceLevel.Summary),
                SeedStart = 100UL,
                SimulationCount = 12,
                MaximumDegreeOfParallelism = 1
            },
            simulationRules.Ruleset,
            new GreedyCombatSimulationPolicyFactory());
        var parallelBatch = new CombatBatchRunner().Run(
            new CombatBatchRequest
            {
                Scenario = BuildSimulationScenario(seed: 1UL, CombatSimulationTraceLevel.Summary),
                SeedStart = 100UL,
                SimulationCount = 12,
                MaximumDegreeOfParallelism = 4
            },
            simulationRules.Ruleset,
            new GreedyCombatSimulationPolicyFactory());
        Assert(serialBatch.Statistics.CompletedSimulations == 12
               && serialBatch.Statistics.AuthoritativeSimulations == 12
               && serialBatch.Statistics.WinRateLower95 >= 0d
               && serialBatch.Statistics.WinRateUpper95 <= 1d
               && serialBatch.Results.Select(item => item.FinalStateHash)
                   .SequenceEqual(parallelBatch.Results.Select(item => item.FinalStateHash)),
            "batch simulation is seed-ordered, parallel-safe and reports bounded Wilson confidence");

        using (CombatSimulationRegistry.RegisterProvider(
                   "Tests",
                   "headless",
                   new FixedRulesetProvider(),
                   10))
        {
            var registryRules = CombatSimulationRegistry.BuildRuleset("registry-v1");
            var repeatedRegistryRules = CombatSimulationRegistry.BuildRuleset("registry-v1");
            Assert(registryRules.Success
                   && CombatSimulationRegistry.SnapshotProviderIds().Contains("Tests:headless")
                   && registryRules.Ruleset.RulesetHash == repeatedRegistryRules.Ruleset.RulesetHash,
                "content-owned ruleset providers build a stable frozen shared ruleset");
        }
        using (CombatSimulationRegistry.RegisterScenarioProvider(
                   "Tests",
                   "scenarios-high",
                   new FixedScenarioProvider(77UL),
                   10))
        using (CombatSimulationRegistry.RegisterScenarioProvider(
                   "Tests",
                   "scenarios-low",
                   new FixedScenarioProvider(12UL),
                   0))
        {
            var registeredScenarios = CombatSimulationRegistry.SnapshotScenarios();
            Assert(registeredScenarios.Count == 1
                   && registeredScenarios[0].ScenarioId == "registered-headless"
                   && registeredScenarios[0].Seed == 77UL,
                "content-owned scenario providers publish cloned headless scenarios");
        }
        var sourceDocumentRules = BuildSimulationRuleset().Ruleset;
        var documentRules = CombatSimulationRegistry.BuildRuleset(new CombatRulesetDocument
        {
            Version = "document-v1",
            Cards = sourceDocumentRules.SnapshotCards().ToList(),
            Enemies = sourceDocumentRules.SnapshotEnemies().ToList(),
            Statuses = sourceDocumentRules.SnapshotStatuses().ToList()
        });
        Assert(documentRules.Success
               && documentRules.Ruleset.CardCount == sourceDocumentRules.CardCount
               && documentRules.Ruleset.EnemyCount == sourceDocumentRules.EnemyCount,
            "file-backed ruleset documents use the same validated builder path");

        var journeyDefinition = new CombatJourneyDefinition
        {
            JourneyId = "base-game-shaped-journey",
            RulesetVersion = "test-v1",
            Player = new CombatPlayerSetup
            {
                RoleId = "tester",
                MaxHp = 30,
                CurrentHp = 30,
                BaseEnergy = 3,
                Deck = { "strike", "strike", "guard", "insight" }
            },
            Stages =
            {
                new CombatJourneyStageDefinition
                {
                    StageId = "ordinary-1",
                    EncounterPool = { "dummy" }
                },
                new CombatJourneyStageDefinition
                {
                    StageId = "ordinary-2",
                    EncounterPool = { "dummy" }
                },
                new CombatJourneyStageDefinition
                {
                    StageId = "final-boss",
                    EncounterPool = { "dummy" },
                    IsBoss = true,
                    OfferRewardAfterVictory = false
                }
            },
            RewardPool =
            {
                new CombatRewardCardDefinition
                {
                    CardId = "strike",
                    BaseValue = 0.5d,
                    Features = { ["burst"] = 1d, ["reliability"] = 0.5d }
                },
                new CombatRewardCardDefinition
                {
                    CardId = "guard",
                    BaseValue = 0.5d,
                    Features = { ["defense"] = 1d, ["reliability"] = 1d }
                },
                new CombatRewardCardDefinition
                {
                    CardId = "insight",
                    BaseValue = 0.25d,
                    Features = { ["draw"] = 1d, ["cycling"] = 1d }
                }
            },
            RolePrior = { ["burst"] = 0.2d },
            BuildTendency = { ["defense"] = 0.2d },
            BossPreference = { ["reliability"] = 0.5d },
            RewardChoices = 3,
            TraceLevel = CombatSimulationTraceLevel.Summary,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 20,
                MaximumActions = 100,
                MaximumCommands = 1000
            }
        };
        var firstWorldPlan = CombatJourneyWorldPlanner.Build(journeyDefinition, 90210UL);
        var repeatedWorldPlan = CombatJourneyWorldPlanner.Build(journeyDefinition, 90210UL);
        Assert(firstWorldPlan.PlanHash == repeatedWorldPlan.PlanHash
               && firstWorldPlan.Encounters.Select(item => item.EnemyId)
                   .SequenceEqual(repeatedWorldPlan.Encounters.Select(item => item.EnemyId))
               && firstWorldPlan.Encounters.SelectMany(item => item.RewardOffer)
                   .SequenceEqual(repeatedWorldPlan.Encounters.SelectMany(item => item.RewardOffer)),
            "journey world planning isolates deterministic encounter and reward streams");

        var journeyRunner = new CombatJourneyRunner();
        var pairedJourney = journeyRunner.RunPaired(
            journeyDefinition,
            90210UL,
            sourceDocumentRules,
            new GreedyCombatSimulationPolicyFactory(),
            new GreedyCombatSimulationPolicyFactory());
        Assert(pairedJourney.Baseline.JourneyVictory
               && pairedJourney.Learned.JourneyVictory
               && pairedJourney.Baseline.ReachedBoss
               && pairedJourney.Baseline.CompletedBattles == 3
               && pairedJourney.Baseline.FinalDeck.Count == journeyDefinition.Player.Deck.Count + 2
               && pairedJourney.Baseline.Rewards.All(item => item.Scores.Count == 3)
               && pairedJourney.Baseline.Rewards.Select(item => item.SelectedCardId)
                   .SequenceEqual(pairedJourney.Learned.Rewards.Select(item => item.SelectedCardId)),
            "paired journeys share a world plan, carry hp and deck growth, and explain reward choices");

        CombatJourneyCheckpoint? interruptedCheckpoint = null;
        using (var stopAfterFirstBattle = new CancellationTokenSource())
        {
            try
            {
                journeyRunner.Run(
                    journeyDefinition,
                    firstWorldPlan,
                    sourceDocumentRules,
                    new GreedyCombatSimulationPolicyFactory(),
                    checkpointSink: checkpoint =>
                    {
                        interruptedCheckpoint = checkpoint;
                        stopAfterFirstBattle.Cancel();
                    },
                    cancellationToken: stopAfterFirstBattle.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
        Assert(interruptedCheckpoint?.NextEncounterIndex == 1
               && interruptedCheckpoint.Deck.Count == journeyDefinition.Player.Deck.Count + 1,
            "journey checkpoints persist inherited hp, deck and the next encounter boundary");
        var resumedJourney = journeyRunner.Run(
            journeyDefinition,
            firstWorldPlan,
            sourceDocumentRules,
            new GreedyCombatSimulationPolicyFactory(),
            interruptedCheckpoint);
        Assert(resumedJourney.JourneyVictory
               && resumedJourney.CompletedBattles == pairedJourney.Baseline.CompletedBattles
               && resumedJourney.FinalDeck.SequenceEqual(pairedJourney.Baseline.FinalDeck),
            "journey resume continues from the checkpoint without replaying completed battles");

        var liveBattleSamples = new List<CombatTrainingSample>
        {
            new()
            {
                GameBuild = "test",
                BattleSessionId = 7001,
                DecisionIndex = 0,
                Sequence = 1,
                StateFingerprint = "live-state-1",
                DecisionProfile = "balanced",
                Selection = new CombatTrainingSelectionTrace
                {
                    ExecutedBy = "human",
                    ExecutedCandidateId = "card_1:enemy"
                },
                StateFeatures =
                {
                    ["playerHp"] = 30d,
                    ["playerMaxHp"] = 30d,
                    ["turn"] = 1d
                },
                Candidates =
                {
                    new CombatTrainingCandidate
                    {
                        CandidateId = "card_1:enemy",
                        SourceId = "card_1",
                        Legal = true,
                        IsExecutedAction = true,
                        Cost = 1,
                        Semantics = new CombatActionSemantics { Damage = 5d }
                    }
                },
                CompletionState = "Completed",
                CreatedUtc = DateTime.UtcNow.AddSeconds(-1)
            },
            new()
            {
                GameBuild = "test",
                BattleSessionId = 7001,
                DecisionIndex = 1,
                Sequence = 2,
                StateFingerprint = "live-state-2",
                DecisionProfile = "balanced",
                Selection = new CombatTrainingSelectionTrace
                {
                    ExecutedBy = "human",
                    ExecutedCandidateId = "card_14:enemy"
                },
                StateFeatures =
                {
                    ["playerHp"] = 25d,
                    ["playerMaxHp"] = 30d,
                    ["turn"] = 2d
                },
                Candidates =
                {
                    new CombatTrainingCandidate
                    {
                        CandidateId = "card_14:enemy",
                        SourceId = "card_14",
                        Legal = true,
                        IsExecutedAction = true,
                        Cost = 1,
                        Semantics = new CombatActionSemantics { Damage = 12d, HitCount = 2d }
                    }
                },
                RewardComponents = new CombatTrainingReward { PlayerHpChange = 0d },
                Terminal = true,
                BattleOutcome = "victory",
                TerminalReason = "victory",
                CompletionState = "Completed",
                CreatedUtc = DateTime.UtcNow
            }
        };
        var assembledLiveEpisodes = CombatLiveEpisodeAssembler.Assemble(liveBattleSamples);
        Assert(assembledLiveEpisodes.Count == 1
               && assembledLiveEpisodes[0].Frames.Count == 2
               && assembledLiveEpisodes[0].BattleSessionId == 7001
               && assembledLiveEpisodes[0].Outcome == "victory"
               && assembledLiveEpisodes[0].Provenance == "live-world-simulation",
            "completed live battle samples assemble into an authoritative policy-value episode");
        CombatJourneyTrainingProjection.ApplyJourneyReturns(
            assembledLiveEpisodes,
            new[]
            {
                new CombatJourneyTrainingEpisode
                {
                    JourneyRunId = "live-journey-1",
                    Complete = true,
                    Outcome = "defeat",
                    Battles =
                    {
                        new CombatJourneyBattleTrainingRecord
                        {
                            BattleIndex = 0,
                            BattleSessionId = 7001,
                            Outcome = "victory"
                        },
                        new CombatJourneyBattleTrainingRecord
                        {
                            BattleIndex = 1,
                            BattleSessionId = 7002,
                            Outcome = "defeat"
                        }
                    }
                }
            });
        Assert(assembledLiveEpisodes[0].JourneyRunId == "live-journey-1"
               && assembledLiveEpisodes[0].JourneyBattleIndex == 0
               && assembledLiveEpisodes[0].Campaign.OutcomeClass == "defeat"
               && assembledLiveEpisodes[0].Campaign.CampaignCompletedBattles == 2
               && assembledLiveEpisodes[0].Frames.All(frame =>
                   frame.LongTermReturn < 0d
                   && frame.DeathTarget == 1d
                   && !frame.StateFeatures.ContainsKey("journeyRemainingBattles")),
            "complete journey outcome replaces local battle return while keeping post-hoc labels out of model features");

        var repositoryRoot = Directory.GetCurrentDirectory();
        var bundledRulesV2Path = Path.Combine(
            repositoryRoot,
            "AuraToolsExp",
            "Config",
            "combat-simulation",
            "witch-base-evaluation-v2.ruleset.json");
        if (!File.Exists(bundledRulesV2Path))
        {
            repositoryRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            bundledRulesV2Path = Path.Combine(
                repositoryRoot,
                "AuraToolsExp",
                "Config",
                "combat-simulation",
                "witch-base-evaluation-v2.ruleset.json");
        }
        var bundledJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        bundledJsonOptions.Converters.Add(new JsonStringEnumConverter());
        var bundledCampaignPath = Path.Combine(
            repositoryRoot,
            "AuraToolsExp",
            "Config",
            "combat-simulation",
            "witch-world-simulation-v2.campaign.json");
        var bundledCampaign = JsonSerializer.Deserialize<CombatCampaignDefinition>(
            File.ReadAllText(bundledCampaignPath),
            bundledJsonOptions)
            ?? throw new InvalidOperationException(
                "Bundled campaign v2 JSON could not be deserialized.");
        var bundledRulesV2Document = JsonSerializer.Deserialize<CombatRulesetDocument>(
            File.ReadAllText(bundledRulesV2Path),
            bundledJsonOptions);
        var bundledRulesV2 = CombatSimulationRegistry.BuildRuleset(bundledRulesV2Document);
        bundledRulesV2.Ruleset.TryGetStatus("buff_burn", out var bundledBurn);
        var lifecycleCoreRules = new CombatRulesetBuilder("lifecycle-core-current")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "cycle-guard",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.GainBlock,
                        Target = CombatSimulationTarget.Self,
                        Amount = 5
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "cycle-filler",
                Cost = 99
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "waiting-enemy",
                MaxHp = 100,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.GainBlock,
                                Target = CombatSimulationTarget.Self,
                                Amount = 0
                            }
                        }
                    }
                }
            })
            .Freeze();
        var midDrawShuffleResult = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "mid-draw-discard-recycle",
                RulesetVersion = lifecycleCoreRules.Ruleset.Version,
                InitialDraw = 2,
                DrawPerTurn = 0,
                InitialDiscardCards = { "cycle-filler" },
                Player = new CombatPlayerSetup
                {
                    MaxHp = 30,
                    CurrentHp = 30,
                    Deck = { "cycle-guard" }
                },
                Enemies = { new CombatEnemySetup { EnemyId = "waiting-enemy" } },
                Limits = new CombatSimulationLimits { MaximumTurns = 1 }
            },
            lifecycleCoreRules.Ruleset,
            EndTurnSimulationPolicy.Instance);
        var persistentBlockResult = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "persistent-player-block",
                RulesetVersion = lifecycleCoreRules.Ruleset.Version,
                InitialDraw = 2,
                DrawPerTurn = 0,
                Player = new CombatPlayerSetup
                {
                    MaxHp = 30,
                    CurrentHp = 30,
                    Deck = { "cycle-guard", "cycle-filler" }
                },
                Enemies = { new CombatEnemySetup { EnemyId = "waiting-enemy" } },
                Limits = new CombatSimulationLimits { MaximumTurns = 2 }
            },
            lifecycleCoreRules.Ruleset,
            new PlayCardsInOrderThenEndPolicy("cycle-guard"));
        var waitingEnemyActorId = persistentBlockResult.FinalState.Actors
            .Single(actor => actor.Kind == CombatSimulationActorKind.Enemy)
            .ActorId;
        Assert(lifecycleCoreRules.Success
               && midDrawShuffleResult.Metrics.CardsDrawn == 2
               && midDrawShuffleResult.Metrics.EmptyEndTurns == 1
               && midDrawShuffleResult.Metrics.SevereEndTurnMistakes == 1
               && midDrawShuffleResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.DeckShuffled)
               && persistentBlockResult.FinalState.Player?.Block == 0
               && persistentBlockResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.CardDiscarded
                   && item.DefinitionId == "cycle-filler")
               && persistentBlockResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.TurnStarted
                   && item.SourceActorId == waitingEnemyActorId)
               && persistentBlockResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.TurnEnded
                   && item.SourceActorId == waitingEnemyActorId),
            "combat lifecycle clears prior-turn block, cycles cards, and emits actor-scoped enemy round events");

        var ritualCourageResult = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "ritual-courage-damage-conversion",
                RulesetVersion = bundledRulesV2.Ruleset.Version,
                InitialDraw = 2,
                DrawPerTurn = 0,
                RequireAuthoritativeRules = true,
                TraceLevel = CombatSimulationTraceLevel.Full,
                Player = new CombatPlayerSetup
                {
                    RoleId = "career_1",
                    MaxHp = 100,
                    CurrentHp = 100,
                    BaseEnergy = 5,
                    Deck = { "ritualcard_8", "card_1" }
                },
                Enemies = { new CombatEnemySetup { EnemyId = "enemy_10001" } },
                Limits = new CombatSimulationLimits { MaximumTurns = 1 }
            },
            bundledRulesV2.Ruleset,
            new PlayCardsInOrderThenEndPolicy("ritualcard_8", "card_1"));
        Assert(ritualCourageResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.BlockGained
                   && item.SourceActorId == ritualCourageResult.FinalState.PlayerActorId
                   && item.Amount == 5)
               && ritualCourageResult.FinalState.Player?.Statuses.All(item =>
                   item.StatusId != "buff_ritualcourage") == true,
            "ritual courage converts actual player damage into block at turn end and then ends");
        var baseCardAuditState = new CombatBattleState
        {
            PlayerActorId = 1,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 30,
                    MaxHp = 30,
                    Variables = { ["Perceive"] = 5d }
                },
                new CombatActorState
                {
                    ActorId = 2,
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 20,
                    MaxHp = 20,
                    Block = 5
                }
            }
        };
        var strikeAfter = baseCardAuditState.Clone();
        strikeAfter.FindActor(2)!.Block = 0;
        var strikeAudit = CombatSemanticAuditor.Audit(
            baseCardAuditState,
            strikeAfter,
            new[]
            {
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 101,
                    Kind = CombatSimulationEventKind.DamageDealt,
                    TargetActorId = 2,
                    Amount = 0
                }
            },
            new CombatActionSemantics { Damage = 5d },
            new CombatSimulationAction
            {
                ActorId = 1,
                CardInstanceId = 101,
                TargetActorId = 2,
                DefinitionId = "card_1"
            },
            bundledRulesV2.Ruleset);
        var defendAfter = baseCardAuditState.Clone();
        defendAfter.Player!.Block = 6;
        var defendAudit = CombatSemanticAuditor.Audit(
            baseCardAuditState,
            defendAfter,
            new[]
            {
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 102,
                    Kind = CombatSimulationEventKind.BlockGained,
                    TargetActorId = 1,
                    Amount = 6
                }
            },
            new CombatActionSemantics { Defend = 5d },
            new CombatSimulationAction
            {
                ActorId = 1,
                CardInstanceId = 102,
                DefinitionId = "card_2"
            },
            bundledRulesV2.Ruleset);
        var cappedBurnBefore = baseCardAuditState.Clone();
        cappedBurnBefore.FindActor(2)!.Block = 0;
        cappedBurnBefore.FindActor(2)!.Statuses.Add(new CombatStatusState
        {
            StatusId = "buff_burn",
            Stacks = bundledBurn?.MaximumStacks ?? 99
        });
        var cappedBurnAfter = cappedBurnBefore.Clone();
        cappedBurnAfter.FindActor(2)!.Hp -= 6;
        var burningAudit = CombatSemanticAuditor.Audit(
            cappedBurnBefore,
            cappedBurnAfter,
            new[]
            {
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 103,
                    Kind = CombatSimulationEventKind.StatusAdded,
                    TargetActorId = 2,
                    DefinitionId = "buff_burn",
                    Amount = 2
                },
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 103,
                    Kind = CombatSimulationEventKind.DamageDealt,
                    TargetActorId = 2,
                    Amount = 6
                }
            },
            new CombatActionSemantics { Damage = 6d },
            new CombatSimulationAction
            {
                ActorId = 1,
                CardInstanceId = 103,
                TargetActorId = 2,
                DefinitionId = "burningcard_2"
            },
            bundledRulesV2.Ruleset);
        var elementBefore = baseCardAuditState.Clone();
        elementBefore.FindActor(2)!.Block = 0;
        var elementAfter = elementBefore.Clone();
        elementAfter.FindActor(2)!.Hp -= 7;
        elementAfter.Player!.Statuses.Add(new CombatStatusState
        {
            StatusId = "buff_elements",
            Stacks = 1
        });
        elementAfter.FindActor(2)!.Statuses.Add(new CombatStatusState
        {
            StatusId = "buff_burn",
            Stacks = 2
        });
        var elementAudit = CombatSemanticAuditor.Audit(
            elementBefore,
            elementAfter,
            new[]
            {
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 104,
                    Kind = CombatSimulationEventKind.StatusAdded,
                    TargetActorId = 1,
                    DefinitionId = "buff_elements",
                    Amount = 1
                },
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 104,
                    Kind = CombatSimulationEventKind.StatusAdded,
                    TargetActorId = 2,
                    DefinitionId = "buff_burn",
                    Amount = 2
                },
                new CombatSimulationEvent
                {
                    SourceActionId = 1,
                    CardInstanceId = 104,
                    Kind = CombatSimulationEventKind.DamageDealt,
                    TargetActorId = 2,
                    Amount = 7
                }
            },
            new CombatActionSemantics
            {
                Damage = 7d,
                Buff = 1d,
                Debuff = 2d
            },
            new CombatSimulationAction
            {
                ActorId = 1,
                CardInstanceId = 104,
                TargetActorId = 2,
                DefinitionId = "elementscard_9"
            },
            bundledRulesV2.Ruleset);
        Assert(!strikeAudit.Mismatch
               && strikeAudit.ExplainedKinds.Contains("damage")
               && !defendAudit.Mismatch
               && defendAudit.ExplainedKinds.Contains("defend")
               && !burningAudit.Mismatch
               && !burningAudit.MismatchKinds.Contains("debuff")
               && !elementAudit.Mismatch,
            "base deck semantic audits distinguish block, Perceive and status caps from unexplained card projection errors");

        var phasedSemanticState = new CombatBattleState
        {
            Turn = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            NextActorId = 4,
            NextCardInstanceId = 105,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    InstanceKey = "player",
                    DefinitionId = "career_1",
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 100,
                    MaxHp = 100,
                    Energy = 10,
                    BaseEnergy = 10
                },
                new CombatActorState
                {
                    ActorId = 2,
                    InstanceKey = "semantic-enemy-a",
                    DefinitionId = "enemy_10001",
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 200,
                    MaxHp = 200
                },
                new CombatActorState
                {
                    ActorId = 3,
                    InstanceKey = "semantic-enemy-b",
                    DefinitionId = "enemy_10001",
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 200,
                    MaxHp = 200
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 101,
                    CardId = "burningcard_1",
                    ApparentCardId = "burningcard_1"
                },
                new CombatCardInstanceState
                {
                    InstanceId = 102,
                    CardId = "burningcard_2",
                    ApparentCardId = "burningcard_2"
                },
                new CombatCardInstanceState
                {
                    InstanceId = 103,
                    CardId = "card_4",
                    ApparentCardId = "card_4"
                },
                new CombatCardInstanceState
                {
                    InstanceId = 104,
                    CardId = "elementscard_9",
                    ApparentCardId = "elementscard_9"
                }
            },
            Hand = { 101, 102, 103, 104 }
        };
        bundledRulesV2.Ruleset.TryGetCard(
            "burningcard_1",
            out var goldenFireRain);
        bundledRulesV2.Ruleset.TryGetCard(
            "burningcard_2",
            out var goldenBlazingNova);
        bundledRulesV2.Ruleset.TryGetCard(
            "card_4",
            out var goldenHeavyBlade);
        bundledRulesV2.Ruleset.TryGetCard(
            "elementscard_9",
            out var goldenCannedElement);
        var fireRainAction = new CombatSimulationAction
        {
            Kind = CombatSimulationActionKind.PlayCard,
            ActorId = 1,
            CardInstanceId = 101,
            TargetActorId = 2,
            DefinitionId = "burningcard_1"
        };
        var fireRainProjection = CombatAuthoritativeSemanticProjector.Project(
            bundledRulesV2.Ruleset,
            phasedSemanticState,
            goldenFireRain!,
            fireRainAction);
        var fireRainImmediate = fireRainProjection.TargetEffects.Where(item =>
            item.Phase == CombatSemanticEffectPhase.Immediate
            && item.Kind == CombatSemanticEffectKind.Damage).ToList();
        var fireRainBurn = fireRainProjection.TargetEffects.Where(item =>
            item.Phase == CombatSemanticEffectPhase.Immediate
            && item.Kind == CombatSemanticEffectKind.AddStatus
            && item.DefinitionId == "buff_burn").ToList();
        var fireRainDeferred = fireRainProjection.TargetEffects.Where(item =>
            item.Phase == CombatSemanticEffectPhase.Deferred
            && item.Kind == CombatSemanticEffectKind.DirectHpLoss
            && item.DefinitionId == "buff_burn").ToList();
        Assert(fireRainImmediate.Count == 2
               && fireRainImmediate.All(item =>
                   item.RawAmount == 4d
                   && item.EffectiveAmount == 4d)
               && fireRainProjection.ImmediateHpDamage == 8d
               && fireRainProjection.AffectedEnemyCount == 2
               && fireRainBurn.Count == 2
               && fireRainBurn.All(item => item.EffectiveAmount == 2d)
               && fireRainDeferred.Count == 2
               && fireRainDeferred.All(item =>
                   item.EffectiveAmount == 4d
                   && item.BypassesBlock)
               && fireRainProjection.DeferredHpDamage == 8d,
            "fire rain projects per-enemy immediate damage, aggregate damage, two burn stacks, and deferred shield-bypassing burn");

        var novaProjection = CombatAuthoritativeSemanticProjector.Project(
            bundledRulesV2.Ruleset,
            phasedSemanticState,
            goldenBlazingNova!,
            new CombatSimulationAction
            {
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 102,
                TargetActorId = 2,
                DefinitionId = "burningcard_2"
            });
        Assert(novaProjection.TargetEffects.Count(item =>
                   item.Phase == CombatSemanticEffectPhase.Immediate
                   && item.Kind == CombatSemanticEffectKind.Damage) == 1
               && novaProjection.ImmediateHpDamage == 6d
               && novaProjection.TargetEffects.Single(item =>
                   item.Phase == CombatSemanticEffectPhase.Immediate
                   && item.Kind == CombatSemanticEffectKind.AddStatus
                   && item.DefinitionId == "buff_burn").EffectiveAmount == 2d,
            "blazing nova keeps its single-target six damage and two burn semantics");

        var empoweredState = phasedSemanticState.Clone();
        empoweredState.Player!.Variables["Strength"] = 10d;
        empoweredState.Player.Statuses.Add(new CombatStatusState
        {
            StatusId = "buff_extraordinary",
            Stacks = 20
        });
        var heavyBladeProjection = CombatAuthoritativeSemanticProjector.Project(
            bundledRulesV2.Ruleset,
            empoweredState,
            goldenHeavyBlade!,
            new CombatSimulationAction
            {
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 103,
                TargetActorId = 2,
                DefinitionId = "card_4"
            });
        Assert(heavyBladeProjection.TargetEffects.Count(item =>
                   item.Phase == CombatSemanticEffectPhase.Immediate
                   && item.Kind == CombatSemanticEffectKind.Damage
                   && item.RawAmount == 6d
                   && item.EffectiveAmount == 9d) == 2
               && heavyBladeProjection.ImmediateHpDamage == 18d,
            "heavy blade uses the shared Strength and extraordinary resolver for every enemy");

        var phasedScenario = new CombatScenarioDefinition
        {
            ScenarioId = "targeted-phased-golden",
            RulesetVersion = bundledRulesV2.Ruleset.Version,
            InitialDraw = 0,
            DrawPerTurn = 0,
            HandLimit = 10,
            RequireAuthoritativeRules = true,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Player = new CombatPlayerSetup
            {
                RoleId = "career_1",
                MaxHp = 100,
                CurrentHp = 100,
                BaseEnergy = 10
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "enemy_10001" },
                new CombatEnemySetup { EnemyId = "enemy_10001" }
            },
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 2,
                MaximumActions = 20,
                MaximumCommands = 1000,
                MaximumCommandsPerAction = 500,
                MaximumTriggerWavesPerAction = 50
            }
        };
        var phasedEngine = new CombatSimulationEngine();
        var goldenFireActions = phasedEngine.GetLegalPlayerActions(
            phasedScenario,
            bundledRulesV2.Ruleset,
            phasedSemanticState);
        var goldenFireAction = goldenFireActions.Single(item =>
            item.Kind == CombatSimulationActionKind.PlayCard
            && item.DefinitionId == "burningcard_1");
        var goldenFireApplied = phasedEngine.ForkAndApplyPlayerAction(
            phasedScenario,
            bundledRulesV2.Ruleset,
            phasedSemanticState,
            goldenFireAction,
            captureSemanticEvents: true);
        var goldenFireAudit = CombatSemanticAuditor.Audit(
            phasedSemanticState,
            goldenFireApplied.State,
            goldenFireApplied.Events,
            fireRainProjection,
            goldenFireAction,
            bundledRulesV2.Ruleset);
        Assert(goldenFireApplied.Success
               && !goldenFireAudit.Invalid
               && !goldenFireAudit.Mismatch
               && goldenFireAudit.AuditedKinds.Contains("damage:target:2")
               && goldenFireAudit.AuditedKinds.Contains("damage:target:3"),
            "multi-target audit compares the same per-target vector and aggregate observed by the simulator");

        var elementTimingState = phasedSemanticState.Clone();
        elementTimingState.Actors.RemoveAll(item => item.ActorId == 3);
        elementTimingState.Hand.Clear();
        elementTimingState.Hand.AddRange(new[] { 104, 101 });
        var elementActions = phasedEngine.GetLegalPlayerActions(
            phasedScenario,
            bundledRulesV2.Ruleset,
            elementTimingState);
        var elementApplied = phasedEngine.ForkAndApplyPlayerAction(
            phasedScenario,
            bundledRulesV2.Ruleset,
            elementTimingState,
            elementActions.Single(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && item.DefinitionId == "elementscard_9"),
            captureSemanticEvents: true);
        var elementProjection = CombatAuthoritativeSemanticProjector.Project(
            bundledRulesV2.Ruleset,
            elementTimingState,
            goldenCannedElement!,
            elementActions.Single(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && item.DefinitionId == "elementscard_9"));
        var elementTimingScenario = new CombatScenarioDefinition
        {
            ScenarioId = "element-acquisition-action-boundary",
            RulesetVersion = bundledRulesV2.Ruleset.Version,
            InitialDraw = 2,
            DrawPerTurn = 0,
            HandLimit = 10,
            RequireAuthoritativeRules = true,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Player = new CombatPlayerSetup
            {
                RoleId = "career_1",
                MaxHp = 100,
                CurrentHp = 100,
                BaseEnergy = 10,
                Deck = { "elementscard_9", "burningcard_1" }
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "enemy_10001", HpScale = 5d }
            },
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 20,
                MaximumCommands = 1000,
                MaximumCommandsPerAction = 500,
                MaximumTriggerWavesPerAction = 50
            }
        };
        var elementSequenceResult = phasedEngine.Run(
            elementTimingScenario,
            bundledRulesV2.Ruleset,
            new PlayCardsInOrderThenEndPolicy(
                "elementscard_9",
                "burningcard_1"));
        Assert(elementProjection.TargetEffects.All(item =>
                   item.Phase != CombatSemanticEffectPhase.PostAction
                   || item.DefinitionId != "buff_extraordinary"),
            "the phased semantic projection excludes newly acquired elements from the acquisition action");
        Assert(elementSequenceResult.Outcome != CombatSimulationOutcome.Invalid
               && !elementSequenceResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.StatusAdded
                   && item.DefinitionId == "buff_extraordinary"
                   && item.SourceActionId == 1)
               && elementSequenceResult.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.StatusAdded
                   && item.DefinitionId == "buff_extraordinary"
                   && item.SourceActionId == 2
                   && item.Amount == 2)
               && elementSequenceResult.FinalState.Player?.Statuses.Single(item =>
                   item.StatusId == "buff_extraordinary").Stacks == 2,
            "new element stacks do not answer their acquisition action and start adding extraordinary after the next action: outcome="
            + elementSequenceResult.Outcome
            + ", statuses="
            + string.Join(",", elementSequenceResult.FinalState.Player?.Statuses.Select(
                item => item.StatusId + "=" + item.Stacks) ?? Array.Empty<string>())
            + ", unsupported="
            + string.Join(",", elementSequenceResult.UnsupportedDefinitions));

        bundledRulesV2.Ruleset.TryGetStatus(
            "buff_impregnable",
            out var bundledImpregnable);
        bundledRulesV2.Ruleset.TryGetStatus(
            "buff_weak",
            out var bundledWeak);
        bundledRulesV2.Ruleset.TryGetStatus(
            "buff_ritualbloodsacrifice",
            out var bundledBloodSacrifice);
        bundledRulesV2.Ruleset.TryGetStatus(
            "buff_ritualtimeprison",
            out var bundledTimePrison);
        bundledRulesV2.Ruleset.TryGetStatus(
            "buff_barkhide",
            out var bundledBarkhide);
        bundledRulesV2.Ruleset.TryGetStatus(
            "buff_bloodwall",
            out var bundledBloodWall);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_BlessedByHeaven",
            out var bundledBlessedByHeaven);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_CAR_Momentum",
            out var bundledCarMomentum);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_Dragon'sBlood",
            out var bundledDragonBlood);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_ThirstForBlood",
            out var bundledThirstForBlood);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_Transcendent",
            out var bundledTranscendent);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_AllogeneicConcentric",
            out var bundledAllogeneicConcentric);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_believer",
            out var bundledBeliever);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_expiation",
            out var bundledExpiation);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_fluster",
            out var bundledFluster);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_hunting",
            out var bundledHunting);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_Twins",
            out var bundledTwins);
        bundledRulesV2.Ruleset.TryGetStatus(
            "SpecialBuff_UnparalleledPower",
            out var bundledUnparalleledPower);
        bundledRulesV2.Ruleset.TryGetCard(
            "ritualcard_1",
            out var bundledRitualSearch);
        bundledRulesV2.Ruleset.TryGetCard(
            "timekeeper_16",
            out var bundledFrozenSearch);
        bundledRulesV2.Ruleset.TryGetCard(
            "timekeeper_2",
            out var bundledReturnEnergy);
        bundledRulesV2.Ruleset.TryGetCard(
            "Crowdfundingcard_16",
            out var bundledRestoreEnergy);
        bundledRulesV2.Ruleset.TryGetEnemy(
            "enemy_10005",
            out var bundledBloodWallEnemy);
        bundledRulesV2.Ruleset.TryGetEnemy(
            "enemy_10022",
            out var bundledSummonerEnemy);
        bundledRulesV2.Ruleset.TryGetEnemy(
            "enemy_10056",
            out var bundledHammerEnemy);
        bundledRulesV2.Ruleset.TryGetEnemy(
            "enemy_10003",
            out var bundledThiefEnemy);
        bundledRulesV2.Ruleset.TryGetCard(
            "luckycard_3",
            out var bundledMoneyThrow);
        bundledRulesV2.Ruleset.TryGetCard(
            "cursecard_1",
            out var bundledDrawCurse);
        bundledRulesV2.Ruleset.TryGetCard(
            "cursecard_13",
            out var bundledRecurringCurse);
        bundledRulesV2.Ruleset.TryGetCard(
            "universalcard_10",
            out var bundledBloodPactStrike);
        bundledRulesV2.Ruleset.TryGetCard(
            "nocard_5",
            out var bundledLuckyPrize);
        bundledRulesV2.Ruleset.TryGetCard(
            "careercard_1",
            out var bundledDivineChoice);
        CombatCampaignWorldPlanner.Validate(bundledCampaign);
        bundledCampaign.TraceLevel = CombatSimulationTraceLevel.Summary;
        var bundledSemanticProbe = CombatFoundationSemanticProbe.Validate(
            bundledCampaign,
            bundledRulesV2.Ruleset,
            new CombatSimulationEngine());
        Assert(
            bundledSemanticProbe.Success
            && bundledCampaign.TraceLevel == CombatSimulationTraceLevel.Summary
            && bundledSemanticProbe.Version
               == CombatPolicyValueProtocol.TrainingSemanticsVersion
            && bundledSemanticProbe.CanaryVersion
               == CombatFoundationSemanticProbeResult.CurrentCanaryVersion,
            "foundation semantic probe covers Blade and Shield, limit damage, "
            + "resource recurrence, retain, reshuffle, and the Summary-trace "
            + "semantic audit pipeline: "
            + "success="
            + bundledSemanticProbe.Success
            + ", trace="
            + bundledCampaign.TraceLevel
            + ", version="
            + bundledSemanticProbe.Version
            + ", canary="
            + bundledSemanticProbe.CanaryVersion
            + ", errors="
            + string.Join("; ", bundledSemanticProbe.Errors));
        var bundledCampaignNormal = CombatCampaignWorldPlanner.Build(
            bundledCampaign,
            "normal",
            23816797UL);
        var bundledCampaignAdvanced = CombatCampaignWorldPlanner.Build(
            bundledCampaign,
            "advanced",
            23816797UL);
        var bundledThresholdRewardIds = bundledCampaign.AttributeThresholdRewards
            .Select(item => item.RewardId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedThresholdRewards = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Strength"] =
                new[] { "blessing_101", "blessing_105", "blessing_109", "blessing_113" },
            ["Lucky"] =
                new[] { "blessing_102", "blessing_106", "blessing_110", "blessing_114" },
            ["Perceive"] =
                new[] { "blessing_104", "blessing_108", "blessing_112", "blessing_116" },
            ["Wisdom"] =
                new[] { "blessing_103", "blessing_107", "blessing_111", "blessing_115" }
        };
        Assert(
            bundledCampaign.CampaignVersion == "3.0.0"
            && bundledCampaign.AttributeThresholdRewards.Count == 16
            && expectedThresholdRewards.All(pair =>
                bundledCampaign.AttributeThresholdRewards
                    .Where(item => string.Equals(
                        item.AttributeId,
                        pair.Key,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Threshold)
                    .Select(item => item.RewardId)
                    .SequenceEqual(pair.Value))
            && bundledCampaignNormal.Encounters.All(item =>
                !bundledThresholdRewardIds.Contains(item.RewardOffer.BlessingId))
            && bundledCampaignAdvanced.Encounters.All(item =>
                !bundledThresholdRewardIds.Contains(item.RewardOffer.BlessingId)),
            "base-game origin threshold blessings are mapped authoritatively and excluded from ordinary blessing offers");

        foreach (var pair in expectedThresholdRewards)
        {
            foreach (var value in new[] { 9, 10, 19, 20, 29, 30, 39, 40 })
            {
                var boundaryState = new CombatCampaignState();
                foreach (var attributeId in bundledCampaign.AttributeIds)
                {
                    var attributeValue = string.Equals(
                        attributeId,
                        pair.Key,
                        StringComparison.OrdinalIgnoreCase)
                        ? value
                        : 0;
                    boundaryState.Attributes[attributeId] = attributeValue;
                    boundaryState.LayerBaseAttributes[attributeId] = attributeValue;
                    boundaryState.PermanentAttributeBonuses[attributeId] = 0;
                    boundaryState.AttributeUpperBounds[attributeId] = 100;
                }
                var expectedIds = pair.Value.Take(value / 10).ToList();
                var granted = CombatCampaignAttributeThresholdRewardReconciler.Reconcile(
                    bundledCampaign,
                    boundaryState);
                var grantedAgain =
                    CombatCampaignAttributeThresholdRewardReconciler.Reconcile(
                        bundledCampaign,
                        boundaryState);
                Assert(
                    boundaryState.Blessings
                        .OrderBy(item => item, StringComparer.Ordinal)
                        .SequenceEqual(expectedIds.OrderBy(
                            item => item,
                            StringComparer.Ordinal))
                    && granted == expectedIds.Count
                    && grantedAgain == 0
                    && boundaryState.Blessings.Count == expectedIds.Count,
                    pair.Key + " origin threshold " + value
                    + " grants every reached blessing exactly once");
            }
        }
        var projectedThresholdState = new CombatCampaignState
        {
            Attributes = { ["Strength"] = 10 },
            LayerBaseAttributes = { ["Strength"] = 10 },
            PermanentAttributeBonuses = { ["Strength"] = 0 },
            AttributeUpperBounds = { ["Strength"] = 40 }
        };
        CombatCampaignAttributeThresholdRewardReconciler.Reconcile(
            bundledCampaign,
            projectedThresholdState);
        Assert(
            CombatCampaignRewardRuleProjector.Build(
                    bundledCampaign,
                    projectedThresholdState)
                .Any(item => item.RewardId == "blessing_101"
                             && !string.IsNullOrWhiteSpace(item.FightScript)),
            "reconciled origin blessings project their authoritative fight scripts into the next battle");
        var firstBand = bundledCampaign.Encounters.Where(item =>
            item.NativeBand is 0 or -1).ToList();
        Assert(bundledRulesV2.Success
               && bundledRulesV2.Ruleset.CardCount == 297
               && bundledRulesV2.Ruleset.EnemyCount == 55
                && bundledRulesV2.Ruleset.StatusCount == 137
                && bundledRulesV2.Ruleset.SnapshotCards().Count(item =>
                    item.Fidelity == CombatRuleFidelity.Authoritative) == 297
                && bundledRulesV2.Ruleset.SnapshotStatuses().Count(item =>
                    item.Fidelity == CombatRuleFidelity.Authoritative) == 137
                && bundledRulesV2.Ruleset.SnapshotEnemies().Count(item =>
                    item.Fidelity == CombatRuleFidelity.Authoritative) == 55
                && bundledImpregnable.Fidelity == CombatRuleFidelity.Authoritative
               && bundledImpregnable.MaximumStacks == 8
               && bundledImpregnable.DynamicModifiersPerStack["AttackedPercentDamage"] == -0.1d
                && bundledWeak.Fidelity == CombatRuleFidelity.Authoritative
                && bundledWeak.ReducePerTurn == 1
                && bundledBloodSacrifice.Fidelity == CombatRuleFidelity.Authoritative
                && bundledBloodSacrifice.Triggers.Any(item =>
                    item.EventKind == CombatSimulationEventKind.CardExhausted
                    && item.CounterKey == "ThisCount"
                    && item.MinimumCounterValue == 10)
                && bundledTimePrison.Fidelity == CombatRuleFidelity.Authoritative
                && bundledTimePrison.Triggers.Any(item =>
                    item.EventKind == CombatSimulationEventKind.DeferredEffectTriggered)
                && bundledBarkhide.Fidelity == CombatRuleFidelity.Authoritative
                && bundledBarkhide.Triggers.Any(item =>
                    item.EventKind == CombatSimulationEventKind.DamageDealt
                    && item.OwnerRelation == CombatStatusTriggerOwnerRelation.EventTarget
                    && item.CounterIncrementMode == CombatStatusCounterIncrementMode.Fixed)
                && bundledBloodWall.Fidelity == CombatRuleFidelity.Authoritative
                && bundledBloodWall.Triggers.Single().Effects.Single().Kind
                == CombatSimulationEffectKind.GainBlock
                && bundledBlessedByHeaven.Fidelity == CombatRuleFidelity.Authoritative
                && bundledBlessedByHeaven.Triggers.Single().MaximumCounterValue == 7
                && bundledCarMomentum.Fidelity == CombatRuleFidelity.Authoritative
                && bundledCarMomentum.Triggers.Single().OwnerRelation
                == CombatStatusTriggerOwnerRelation.Any
                && bundledDragonBlood.Fidelity == CombatRuleFidelity.Authoritative
                && bundledDragonBlood.Triggers.Single().Effects.Any(effect =>
                    effect.Kind == CombatSimulationEffectKind.Heal
                    && effect.Rounding == CombatSimulationValueRounding.Floor)
                && bundledThirstForBlood.Fidelity == CombatRuleFidelity.Authoritative
                && bundledThirstForBlood.Triggers.Single(item =>
                    item.EventKind == CombatSimulationEventKind.BattleStarted)
                    .Effects.Single(effect =>
                        effect.DefinitionId == "buff_bloodriver").Amount == 3
                && bundledTranscendent.Fidelity == CombatRuleFidelity.Authoritative
                && bundledTranscendent.DynamicModifiersPerStack["AttackedDefaultDamage"] == -4d
                && bundledTranscendent.DynamicModifiersPerStack["AttackedPercentDamage"] == -0.3d
                && bundledTranscendent.DynamicModifiersPerStack["PercentDamage"] == 0.3d
                && bundledTranscendent.Triggers.Single().Effects.Single().Kind
                == CombatSimulationEffectKind.DirectHpLoss
                && bundledAllogeneicConcentric.Fidelity
                == CombatRuleFidelity.Authoritative
                && bundledAllogeneicConcentric.Triggers.Single().OwnerRelation
                == CombatStatusTriggerOwnerRelation.EventTargetAllyExceptSelf
                && bundledAllogeneicConcentric.Triggers.Single().Effects.Any(effect =>
                    effect.Kind == CombatSimulationEffectKind.ScaleMaxHpPercent
                    && effect.Amount == 150)
                && bundledBeliever.Fidelity == CombatRuleFidelity.Authoritative
                && bundledBeliever.Triggers.Single().Effects.Any(effect =>
                    effect.Kind == CombatSimulationEffectKind.ScaleVariablePercent
                    && effect.DefinitionId == "PercentDamage"
                    && effect.Amount == 130)
                && bundledExpiation.Fidelity == CombatRuleFidelity.Authoritative
                && bundledExpiation.Triggers.Single(item =>
                    item.TriggerId == "expiation-fourth-round")
                    .MinimumCounterValue == 4
                && bundledFluster.Fidelity == CombatRuleFidelity.Authoritative
                && bundledFluster.Triggers.Single().CounterStepOrigin == 4
                && bundledFluster.Triggers.Single().CounterStep == 3
                && bundledHunting.Fidelity == CombatRuleFidelity.Authoritative
                && bundledHunting.Triggers.Single().Effects.Any(effect =>
                    effect.AmountExpression?.Operation
                    == CombatSimulationValueOperation.SourceVariable
                    && effect.AmountExpression.Key == "BaseAttack")
                && bundledTwins.Fidelity == CombatRuleFidelity.Authoritative
                && bundledTwins.Triggers.Single().MaximumCounterValue == 1
                && bundledUnparalleledPower.Fidelity
                == CombatRuleFidelity.Authoritative
                && bundledUnparalleledPower.Triggers.Single().Effects.Any(effect =>
                    effect.Kind == CombatSimulationEffectKind.ScaleVariablePercent
                    && effect.Amount == 200)
                && bundledRitualSearch.Fidelity == CombatRuleFidelity.Authoritative
                && bundledRitualSearch.Effects.Single().Kind
                == CombatSimulationEffectKind.RetrieveCards
                && bundledFrozenSearch.Effects.Single().SourceZone
                == CombatCardZone.DiscardPile
                && bundledReturnEnergy.Effects.Single().Kind
                == CombatSimulationEffectKind.SetEnergy
                && bundledReturnEnergy.Effects.Single().AmountExpression?.Operation
                == CombatSimulationValueOperation.Maximum
                && bundledRestoreEnergy.Effects.Any(effect =>
                    effect.Kind == CombatSimulationEffectKind.Draw
                    && effect.Amount == 5)
                && bundledRestoreEnergy.Effects.Any(effect =>
                    effect.Kind == CombatSimulationEffectKind.SetEnergy
                    && effect.AmountExpression?.Operation
                    == CombatSimulationValueOperation.SourceMaxEnergy)
                && bundledBloodWallEnemy.InitialStatuses.Count == 2
                && bundledBloodWallEnemy.InitialStatuses.All(item =>
                    item.ConditionExpression != null)
                && bundledSummonerEnemy.Intents.Single(item =>
                    item.IntentId == "enemycard_Come").Effects.Any(effect =>
                        effect.Kind == CombatSimulationEffectKind.SummonEnemy
                        && effect.DefinitionId == "enemy_10023")
                && bundledHammerEnemy.Intents.Single(item =>
                    item.IntentId == "enemycard_CAR_Hammer").Effects.Any(effect =>
                        effect.Kind == CombatSimulationEffectKind.ModifyVariablePercent
                        && effect.DefinitionId == "HealMultiplier"
                        && effect.Amount == -20)
                && bundledThiefEnemy.Intents.Single(item =>
                    item.IntentId == "enemycard_obtainMoney").Effects.Any(effect =>
                        effect.Kind == CombatSimulationEffectKind.DeferVariableUntilVictory
                        && effect.DefinitionId == "Money"
                        && effect.Amount == 15
                        && effect.PersistAcrossBattles)
                && bundledMoneyThrow.Fidelity == CombatRuleFidelity.Authoritative
                && bundledMoneyThrow.Effects.Count(effect =>
                    effect.Kind == CombatSimulationEffectKind.Damage) == 20
                 && bundledMoneyThrow.Effects.Single(effect =>
                     effect.Kind == CombatSimulationEffectKind.ModifyVariable)
                     .PersistAcrossBattles
                 && bundledDrawCurse.Fidelity == CombatRuleFidelity.Authoritative
                 && bundledDrawCurse.Tags.Contains(
                     "Unusable",
                     StringComparer.OrdinalIgnoreCase)
                 && bundledDrawCurse.DrawEffects.Single().DefinitionId
                 == "buff_vulnerability"
                 && bundledRecurringCurse.DiscardEffects.Single().DefinitionId
                 == "cursecard_13"
               && bundledBloodPactStrike.Effects.First().Kind
                == CombatSimulationEffectKind.SetHp
               && bundledLuckyPrize.Fidelity == CombatRuleFidelity.Authoritative
               && bundledLuckyPrize.Metadata.GetValueOrDefault(
                   "NativeExecution",
                   "") == "Script"
               && !bundledDivineChoice.RequiresEnemyTarget
               && bundledDivineChoice.VerificationSource
                  == "Decompiler:v1.0.24591395"
               && bundledDivineChoice.ActionContract?.Version
                  == CombatActionContractProtocol.Version
               && bundledDivineChoice.ActionContract.Preconditions.Count == 2
               && bundledDivineChoice.ActionContract
                      .MinimumCardsMovedFromDrawPileToHandOnApplied == 1
                && bundledCampaign.Encounters.Count == 48
               && bundledCampaign.Rewards.Count == 553
               && bundledCampaign.Rewards
                   .Where(item => item.RewardId is
                       "SpellCard_1"
                       or "SpellCard_2"
                       or "SpellCard_3"
                       or "SpellCard_4")
                   .All(item => item.CardAcquisition
                       == CombatCampaignCardAcquisition.GeneratedOnly)
               && bundledCampaign.RequireAuthoritativeRules
               && bundledCampaign.InitialMoney == 100
               && bundledCampaign.Player.RoleId == "career_1"
               && bundledCampaign.Player.PartnerId == "Partner_10001"
               && bundledCampaign.Player.SkillCardIds.SequenceEqual(
                   new[] { "careercard_1" })
               && bundledCampaign.Player.FamiliarBlessingIds.SequenceEqual(
                   new[] { "blessing_38" })
               && bundledCampaign.Strategies.Count == 8
               && bundledCampaign.EnabledRewardCardPackIds.Contains("cardpack_1")
               && bundledCampaign.EnabledRewardCardPackIds.Contains("cardpack_2")
               && !bundledCampaign.EnabledRewardCardPackIds.Contains("cardpack_13")
               && bundledCampaign.TargetDeckSizeMinimum == 15
               && bundledCampaign.TargetDeckSizeMaximum == 24
               && bundledCampaign.Player.MaxHp == 100
               && bundledCampaign.Player.CurrentHp == 100
               && bundledCampaign.Player.Deck.SequenceEqual(new[]
               {
                   "card_1", "card_2", "card_1", "card_2", "card_1", "card_2",
                   "card_2", "burningcard_1", "card_4", "card_3", "burningcard_2",
                   "burningcard_2", "elementscard_9", "card_3", "elementscard_1"
               })
               && !bundledCampaign.Difficulties.Single(item =>
                   item.DifficultyId == "normal").MovePlayedCardAfterResolution
               && bundledCampaign.Difficulties.Single(item =>
                   item.DifficultyId == "advanced").MovePlayedCardAfterResolution
               && bundledCampaign.Difficulties.Single(item =>
                   item.DifficultyId == "advanced").EnemyHpMultiplier == 1.4d
               && bundledCampaign.Difficulties.Single(item =>
                   item.DifficultyId == "advanced").EnemyAttackMultiplier == 1.4d
               && bundledCampaign.Difficulties.Single(item =>
                   item.DifficultyId == "advanced").InitialDiscardCards.Count == 2
               && bundledCampaign.Difficulties.Single(item =>
                   item.DifficultyId == "advanced").DirectHpLossAfterPlayerCard == 1
               && bundledCampaign.Difficulties.Single(item =>
                   item.DifficultyId == "advanced").AdditionalEnemyHpMultiplier == 3d
               && bundledCampaign.Rewards
                   .Where(item => item.Kind == CombatCampaignRewardKind.Card)
                   .Where(item => item.CardAcquisition
                       == CombatCampaignCardAcquisition.RewardPool)
                   .All(item => item.OfferWeight is 8d or 5d or 2d or 1d)
               && bundledCampaign.Rewards
                   .Where(item => item.Kind == CombatCampaignRewardKind.Card
                                  && item.CardAcquisition
                                     != CombatCampaignCardAcquisition.RewardPool)
                   .All(item => item.OfferWeight == 0d)
               && bundledCampaign.Rewards.Single(item =>
                       item.RewardId == "ritualcard_8").BaseValue == 1.2d
               && bundledCampaign.Rewards.Single(item =>
                       item.RewardId == "ritualcard_8").Features["defense"] == 1d
               && bundledCampaign.Rewards.Single(item =>
                       item.RewardId == "ritualcard_8").Features["cycling"] == 0.8d
               && bundledCampaign.Rewards
                   .Where(item =>
                   item.Kind == CombatCampaignRewardKind.Card
                   && item.RewardId.StartsWith(
                       "curse",
                       StringComparison.OrdinalIgnoreCase))
                   .All(item => item.CardAcquisition
                       == CombatCampaignCardAcquisition.CurseOnly)
               && bundledRulesV2.Ruleset.SnapshotEnemies()
                   .All(item => item.ActionCount is >= 1 and <= 3)
               && bundledRulesV2.Ruleset.SnapshotEnemies()
                   .SelectMany(item => item.Intents)
                   .Any(item => item.Effects.Any(effect =>
                       effect.Kind == CombatSimulationEffectKind.CreateCard))
               && bundledRulesV2.Ruleset.SnapshotEnemies()
                   .SelectMany(item => item.Intents)
                   .Any(item => item.Effects.Any(effect =>
                       effect.Kind == CombatSimulationEffectKind.AddStatus))
               && bundledRulesV2.Ruleset.SnapshotEnemies()
                   .SelectMany(item => item.Intents)
                   .Where(item => item.IntentId == "enemycard_FiveHit")
                   .All(item => item.Effects.Count(effect =>
                       effect.Kind == CombatSimulationEffectKind.Damage) == 5)
               && bundledCampaign.Rewards.Single(item => item.RewardId == "blessing_1")
                   .PermanentAttributeBonuses["Strength"] == 2
               && bundledCampaign.Rewards.Single(item => item.RewardId == "blessing_7")
                   .MaxHpBonus == 5
               && firstBand.Count(item => item.Kind == CombatCampaignEncounterKind.Normal) == 12
               && firstBand.Count(item => item.Kind == CombatCampaignEncounterKind.Elite) == 8
               && firstBand.Count(item => item.Kind == CombatCampaignEncounterKind.Boss) == 3
               && bundledCampaignNormal.Encounters.Count == 37
               && bundledCampaignNormal.Encounters[36].Kind
               == CombatCampaignEncounterKind.FinalBoss
               && bundledCampaignNormal.Encounters[36].EncounterId is
                   "final-caroline-perfect-angel"
                   or "final-evernight-incarnation"
                   or "final-demon-king"
                   or "final-holy-judgment-engine"
               && bundledCampaignNormal.Encounters.Select(item => item.EncounterId)
                   .SequenceEqual(
                       bundledCampaignAdvanced.Encounters.Select(item => item.EncounterId))
               && bundledCampaignNormal.PlanHash != bundledCampaignAdvanced.PlanHash
               && bundledCampaign.Rewards
                   .Where(item => item.Kind == CombatCampaignRewardKind.Blessing
                                  && item.Negative)
                   .All(item => bundledCampaignNormal.Encounters
                       .All(encounter => !string.Equals(
                           encounter.RewardOffer.BlessingId,
                           item.RewardId,
                           StringComparison.OrdinalIgnoreCase)))
               && !File.ReadAllText(bundledCampaignPath)
                   .Contains("Terrias", StringComparison.OrdinalIgnoreCase)
               && !File.ReadAllText(bundledCampaignPath)
                   .Contains("Saya_", StringComparison.OrdinalIgnoreCase)
               && !File.ReadAllText(bundledRulesV2Path)
                   .Contains("Terrias", StringComparison.OrdinalIgnoreCase)
               && !File.ReadAllText(bundledRulesV2Path)
                   .Contains("Saya_", StringComparison.OrdinalIgnoreCase),
              "bundled campaign v3 fixes seven layers, acquisition classes, game presets, strategies, final bosses, and paired difficulty worlds");
        var bundledRewardLookup = bundledCampaign.Rewards
            .GroupBy(item => item.RewardId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var strategy in bundledCampaign.Strategies)
        {
            var strategyState = new CombatCampaignState
            {
                Deck = new List<string>(strategy.RequiredCardIds),
                Relics = new List<string>(strategy.RequiredRelicIds),
                Blessings = new List<string>(strategy.RequiredBlessingIds)
            };
            var strategyProgress = CombatCampaignStrategyEvaluator.Evaluate(
                    bundledCampaign,
                    strategyState,
                    bundledRewardLookup)
                .Single(item => string.Equals(
                    item.StrategyId,
                    strategy.StrategyId,
                    StringComparison.OrdinalIgnoreCase));
            Assert(strategy.Deterministic
                   && strategyProgress.Accessible
                   && strategyProgress.Executable
                   && Math.Abs(strategyProgress.Completion - 1d) < 0.0001d
                   && strategy.RequiredCardIds.All(cardId =>
                       bundledRewardLookup.TryGetValue(cardId, out var reward)
                       && CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
                           reward,
                           bundledCampaign.EnabledRewardCardPackIds))
                   && strategy.RequiredRelicIds.All(
                       bundledRewardLookup.ContainsKey),
                "bundled deterministic strategy is attainable and executable: "
                + strategy.StrategyId);
        }

        CombatSimulationResult RunBundledStatusScenario(
            string enemyId,
            string cardId,
            ulong seed,
            int cardCopies = 1,
            int initialDraw = 1,
            int maximumTurns = 1,
            IReadOnlyList<string>? additionalEnemyIds = null)
        {
            var deck = Enumerable.Repeat(cardId, Math.Max(1, cardCopies)).ToList();
            var enemyIds = new List<string> { enemyId };
            if (additionalEnemyIds != null)
            {
                enemyIds.AddRange(additionalEnemyIds);
            }
            return new CombatSimulationEngine().Run(
                new CombatScenarioDefinition
                {
                    ScenarioId = "bundled-status-" + enemyId,
                    RulesetVersion = "witch-base-evaluation-v2",
                    Seed = seed,
                    InitialDraw = initialDraw,
                    DrawPerTurn = initialDraw,
                    HandLimit = Math.Max(10, initialDraw),
                    RequireAuthoritativeRules = false,
                    TraceLevel = CombatSimulationTraceLevel.Full,
                    Player = new CombatPlayerSetup
                    {
                        RoleId = "witch",
                        MaxHp = 999,
                        CurrentHp = 999,
                        BaseEnergy = 20,
                        Deck = deck
                    },
                    Enemies = enemyIds.Select(id =>
                        new CombatEnemySetup { EnemyId = id }).ToList(),
                    Limits = new CombatSimulationLimits
                    {
                        MaximumTurns = maximumTurns,
                        MaximumActions = 500,
                        MaximumCommands = 10000,
                        MaximumCommandsPerAction = 1000,
                        MaximumTriggerWavesPerAction = 100
                    }
                },
                bundledRulesV2.Ruleset,
                new GreedyCombatSimulationPolicy());
        }

        var barkhideScenario = RunBundledStatusScenario("enemy_10024", "card_1", 2401UL);
        var bloodWallScenario = RunBundledStatusScenario("enemy_10005", "cursecard_1", 2402UL);
        var blessedScenario = RunBundledStatusScenario("enemy_10041", "cursecard_1", 2403UL);
        var momentumScenario = RunBundledStatusScenario("enemy_10056", "cursecard_1", 2404UL);
        var dragonScenario = RunBundledStatusScenario("enemy_10059", "cursecard_1", 2405UL);
        var thirstScenario = RunBundledStatusScenario("enemy_10039", "cursecard_1", 2406UL);
        var transcendentScenario =
            RunBundledStatusScenario("enemy_10027", "cursecard_1", 2407UL);
        var dragonEnemy = dragonScenario.FinalState.Actors.Single(actor =>
            actor.Kind == CombatSimulationActorKind.Enemy);
        Assert(
            barkhideScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.BlockGained
                && item.DefinitionId == "buff_barkhide"
                && item.Amount == 2),
            "barkhide grants twice the per-turn hit counter as persistent block");
        Assert(
            bloodWallScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.BlockGained
                && item.DefinitionId == "buff_bloodwall"
                && item.Amount == 3),
            "blood wall grants its current stacks as block on enemy actions");
        Assert(
            blessedScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.StatusAdded
                && item.DefinitionId == "buff_evergreen"
                && item.Amount == 2),
            "blessed by heaven adds floor five percent max hp evergreen on global turn start");
        Assert(
            momentumScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.StatusAdded
                && item.DefinitionId == "buff_extraordinary"
                && item.Amount == 20),
            "Caroline momentum triggers on the global turn start");
        Assert(
            dragonEnemy.Statuses.Any(item =>
                item.StatusId == "buff_impregnable"
                && item.Stacks >= 1)
            && dragonEnemy.Statuses.Any(item =>
                item.StatusId == "buff_extraordinary"
                && item.Stacks >= 20),
            "dragon blood resolves healing support statuses on the global turn end");
        Assert(
            thirstScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.StatusAdded
                && item.DefinitionId == "buff_bloodriver"
                && item.Amount == 3),
            "thirst for blood applies the scripted three blood-river stacks at battle start");
        Assert(
            transcendentScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.DamageDealt
                && item.DefinitionId == "SpecialBuff_Transcendent"
                && item.Amount == 4),
            "transcendent actions apply four direct hp loss to every opponent");

        var allogeneicScenario = RunBundledStatusScenario(
            "enemy_10005",
            "universalcard_17",
            2411UL,
            additionalEnemyIds: new[] { "enemy_10029" });
        var allogeneicEnemy = allogeneicScenario.FinalState.Actors.Single(actor =>
            actor.DefinitionId == "enemy_10029");
        Assert(
            allogeneicEnemy.MaxHp == 195
            && allogeneicEnemy.Hp == 195
            && Math.Abs(allogeneicEnemy.Variables["PercentDamage"] - 1.5d) < 0.000001d
            && Math.Abs(allogeneicEnemy.Variables["DefendPercent"] - 1.5d) < 0.000001d,
            "allogeneic concentric scales attack, defense, max hp, and fully heals after an ally dies");

        var believerScenario = RunBundledStatusScenario(
            "enemy_10009",
            "universalcard_17",
            2412UL,
            cardCopies: 3,
            initialDraw: 3);
        var believerEnemy = believerScenario.FinalState.Actors.Single(actor =>
            actor.Kind == CombatSimulationActorKind.Enemy);
        Assert(
            believerEnemy.Hp > believerEnemy.MaxHp / 2
            && Math.Abs(believerEnemy.Variables["PercentDamage"] - 1.3d) < 0.000001d
            && Math.Abs(believerEnemy.Variables["DefendPercent"] - 1.3d) < 0.000001d
            && believerEnemy.Statuses.All(status =>
                status.StatusId != "SpecialBuff_believer"),
            "believer heals and permanently scales attack and defense only after crossing half hp");

        var expiationScenario = RunBundledStatusScenario(
            "enemy_10018",
            "cursecard_1",
            2413UL,
            cardCopies: 4,
            maximumTurns: 4);
        Assert(
            expiationScenario.Outcome == CombatSimulationOutcome.Victory
            && expiationScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.DamageDealt
                && item.DefinitionId == "SpecialBuff_expiation"
                && item.TargetActorId != expiationScenario.FinalState.PlayerActorId),
            "expiation naturally ends the battle on its fourth round after healing the opponent");

        var flusterScenario = RunBundledStatusScenario(
            "enemy_10010",
            "card_1",
            2414UL,
            cardCopies: 5,
            initialDraw: 5);
        Assert(
            flusterScenario.Metrics.CardsPlayed == 4
            && flusterScenario.Events.Count(item =>
                item.Kind == CombatSimulationEventKind.CardDiscarded) == 5,
            "fluster ignores the first hit and discards on the fourth, seventh, and later third-hit intervals");

        var huntingScenario = RunBundledStatusScenario(
            "enemy_10028",
            "cursecard_11",
            2415UL,
            cardCopies: 2,
            initialDraw: 2);
        Assert(
            huntingScenario.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.DamageDealt
                && item.DefinitionId == "SpecialBuff_hunting"
                && item.Amount == 12),
            "hunting reads the witch hand parity and uses the enemy's native attack value");

        var twinsScenario = RunBundledStatusScenario(
            "enemy_10005",
            "universalcard_17",
            2416UL,
            additionalEnemyIds: new[] { "enemy_10036" });
        var twinsEnemy = twinsScenario.FinalState.Actors.Single(actor =>
            actor.DefinitionId == "enemy_10036");
        Assert(
            twinsEnemy.Statuses.Any(status =>
                status.StatusId == "buff_impregnable" && status.Stacks >= 2)
            && twinsEnemy.Statuses.Any(status =>
                status.StatusId == "buff_extraordinary" && status.Stacks >= 30)
            && twinsEnemy.Statuses.Any(status =>
                status.StatusId == "buff_thorns" && status.Stacks >= 3),
            "twins reacts once to an ally death while its owner is still alive");

        var unparalleledScenario = RunBundledStatusScenario(
            "enemy_10022",
            "cursecard_1",
            2417UL);
        var unparalleledEnemy = unparalleledScenario.FinalState.Actors.Single(actor =>
            actor.DefinitionId == "enemy_10022");
        Assert(
            unparalleledEnemy.Hp == 180
            && Math.Abs(unparalleledEnemy.Variables["PercentDamage"] - 2d) < 0.000001d,
            "unparalleled power loses one quarter max hp and doubles current attack at round end");

        var moneyObservation = CombatSimulationObservationProjector.Project(
            new CombatSimulationPolicyContext
            {
                Scenario = new CombatScenarioDefinition { HandLimit = 10 },
                Ruleset = bundledRulesV2.Ruleset,
                State = new CombatBattleState
                {
                    Turn = 1,
                    Phase = CombatSimulationPhase.PlayerAction,
                    PlayerActorId = 1,
                    Actors =
                    {
                        new CombatActorState
                        {
                            ActorId = 1,
                            Kind = CombatSimulationActorKind.Player,
                            Hp = 20,
                            MaxHp = 20,
                            Variables = { ["Money"] = 40 }
                        },
                        new CombatActorState
                        {
                            ActorId = 2,
                            Kind = CombatSimulationActorKind.Enemy,
                            DefinitionId = "enemy_10003",
                            Hp = 100,
                            MaxHp = 100
                        }
                    },
                    Cards =
                    {
                        new CombatCardInstanceState
                        {
                            InstanceId = 1,
                            CardId = "luckycard_3"
                        }
                    },
                    Hand = { 1 }
                },
                LegalActions = new List<CombatSimulationAction>
                {
                    new CombatSimulationAction
                    {
                        CandidateId = "money-throw",
                        Kind = CombatSimulationActionKind.PlayCard,
                        CardInstanceId = 1,
                        DefinitionId = "luckycard_3",
                        Cost = 1
                    }
                }
            });
        var moneyThrowObservation = moneyObservation.Actions.Single(item =>
            item.CandidateId == "money-throw");
        Assert(!moneyObservation.Features.ContainsKey("player.Money")
               && moneyThrowObservation.Semantics.Damage == 12d
               && !moneyThrowObservation.Semantics.StateChanges.ContainsKey("player.Money"),
            "player-equivalent observations exclude campaign money while retaining visible action damage");

        foreach (var lateMove in new[] { false, true })
        {
            var bloodState = new CombatBattleState
            {
                Turn = 1,
                Phase = CombatSimulationPhase.PlayerAction,
                PlayerActorId = 1,
                NextActorId = 3,
                NextCardInstanceId = 11,
                Actors =
                {
                    new CombatActorState
                    {
                        ActorId = 1,
                        InstanceKey = "player",
                        Kind = CombatSimulationActorKind.Player,
                        DefinitionId = "career_1",
                        Hp = 100,
                        MaxHp = 100,
                        Energy = 999,
                        BaseEnergy = 999
                    },
                    new CombatActorState
                    {
                        ActorId = 2,
                        InstanceKey = "dummy:blood",
                        Kind = CombatSimulationActorKind.Enemy,
                        DefinitionId = "enemy_1",
                        Hp = 100000,
                        MaxHp = 100000
                    }
                }
            };
            bloodState.Cards.Add(new CombatCardInstanceState
            {
                InstanceId = 1,
                CardId = "ritualcard_14"
            });
            bloodState.Hand.Add(1);
            for (var instanceId = 2; instanceId <= 10; instanceId++)
            {
                bloodState.Cards.Add(new CombatCardInstanceState
                {
                    InstanceId = instanceId,
                    CardId = "card_1"
                });
                bloodState.Hand.Add(instanceId);
            }

            var bloodScenario = new CombatScenarioDefinition
            {
                ScenarioId = "blood-sacrifice-" + lateMove,
                RulesetVersion = bundledRulesV2.Ruleset.Version,
                MovePlayedCardAfterResolution = lateMove,
                RequireAuthoritativeRules = true
            };
            for (var instanceId = 1; instanceId <= 10; instanceId++)
            {
                var legal = simulationEngine.GetLegalPlayerActions(
                    bloodScenario,
                    bundledRulesV2.Ruleset,
                    bloodState);
                var selected = legal.First(item =>
                    item.Kind == CombatSimulationActionKind.PlayCard
                    && item.CardInstanceId == instanceId);
                var applied = simulationEngine.ForkAndApplyPlayerAction(
                    bloodScenario,
                    bundledRulesV2.Ruleset,
                    bloodState,
                    selected);
                Assert(applied.Success, "blood sacrifice action fork succeeds");
                bloodState = applied.State;
                if (instanceId == 1)
                {
                    Assert(bloodState.Hand.All(cardInstanceId =>
                            bloodState.FindCard(cardInstanceId)?.Tags.Contains(
                                "Burnout",
                                StringComparer.OrdinalIgnoreCase) == true),
                        "blood sacrifice persistently marks every remaining hand instance");
                }
            }
            Assert(bloodState.ExhaustPile.Count == 10
                   && bloodState.Player?.Statuses.All(item =>
                       item.StatusId != "buff_ritualbloodsacrifice") == true
                   && bloodState.Player?.Statuses.Single(item =>
                       item.StatusId == "buff_extraordinary").Stacks == 444,
                "blood sacrifice counts ten actual burns and resolves identically for both card-move timings");
        }

        var retrievalState = new CombatBattleState
        {
            Turn = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            NextActorId = 3,
            NextCardInstanceId = 5,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    InstanceKey = "player",
                    Kind = CombatSimulationActorKind.Player,
                    DefinitionId = "career_1",
                    Hp = 100,
                    MaxHp = 100,
                    Energy = 99,
                    BaseEnergy = 99
                },
                new CombatActorState
                {
                    ActorId = 2,
                    InstanceKey = "dummy:retrieval",
                    Kind = CombatSimulationActorKind.Enemy,
                    DefinitionId = "enemy_1",
                    Hp = 100000,
                    MaxHp = 100000
                }
            },
            Cards =
            {
                new CombatCardInstanceState { InstanceId = 1, CardId = "ritualcard_1" },
                new CombatCardInstanceState { InstanceId = 2, CardId = "ritualcard_14" },
                new CombatCardInstanceState { InstanceId = 3, CardId = "card_1" },
                new CombatCardInstanceState { InstanceId = 4, CardId = "ritualcard_17" }
            },
            Hand = { 1 },
            DrawPile = { 2, 3, 4 }
        };
        var retrievalScenario = new CombatScenarioDefinition
        {
            ScenarioId = "tagged-retrieval",
            RulesetVersion = bundledRulesV2.Ruleset.Version,
            RequireAuthoritativeRules = true
        };
        var retrievalAction = simulationEngine.GetLegalPlayerActions(
                retrievalScenario,
                bundledRulesV2.Ruleset,
                retrievalState)
            .Single(item => item.Kind == CombatSimulationActionKind.PlayCard);
        var retrievalApplication = simulationEngine.ForkAndApplyPlayerAction(
            retrievalScenario,
            bundledRulesV2.Ruleset,
            retrievalState,
            retrievalAction);
        Assert(retrievalApplication.Success
               && retrievalApplication.State.Hand.Select(instanceId =>
                       retrievalApplication.State.FindCard(instanceId)?.CardId)
                   .OrderBy(item => item, StringComparer.Ordinal)
                   .SequenceEqual(new[] { "ritualcard_14", "ritualcard_17" })
               && retrievalApplication.State.DrawPile.Single() == 3,
            "tagged retrieval moves only matching card instances out of the requested source zone");

        var timePrisonRules = new CombatRulesetBuilder("time-prison-semantics-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "defer",
                Cost = 0,
                Tags = { "Inherent" },
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.EmitEvent,
                        Target = CombatSimulationTarget.Self,
                        Amount = 2,
                        DefinitionId = "buff_timelock",
                        EmittedEventKind = CombatSimulationEventKind.DeferredEffectTriggered
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "ritual-a",
                Cost = 99,
                Tags = { "Ritual" },
                Effects = { new CombatSimulationEffectDefinition { Kind = CombatSimulationEffectKind.GainBlock } }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "ritual-b",
                Cost = 99,
                Tags = { "Ritual" },
                Effects = { new CombatSimulationEffectDefinition { Kind = CombatSimulationEffectKind.GainBlock } }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "ritual-c",
                Cost = 99,
                Tags = { "Ritual" },
                Effects = { new CombatSimulationEffectDefinition { Kind = CombatSimulationEffectKind.GainBlock } }
            })
            .RegisterStatus(bundledTimePrison)
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "buff_ritualechostaff"
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "dummy",
                MaxHp = 1000,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.GainBlock,
                                Target = CombatSimulationTarget.Self
                            }
                        }
                    }
                }
            })
            .Freeze();
        var timePrisonResult = simulationEngine.Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "time-prison-deferred-count",
                RulesetVersion = "time-prison-semantics-v1",
                InitialDraw = 1,
                DrawPerTurn = 0,
                Player = new CombatPlayerSetup
                {
                    MaxHp = 30,
                    CurrentHp = 30,
                    Deck = { "ritual-a", "ritual-b", "ritual-c", "defer" },
                    InitialStatuses =
                    {
                        new CombatInitialStatus
                        {
                            StatusId = "buff_ritualtimeprison",
                            Stacks = 1
                        },
                        new CombatInitialStatus
                        {
                            StatusId = "buff_ritualechostaff",
                            Stacks = 1
                        }
                    }
                },
                Enemies = { new CombatEnemySetup { EnemyId = "dummy" } },
                Limits = new CombatSimulationLimits { MaximumTurns = 2 }
            },
            timePrisonRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        Assert(timePrisonRules.Success
               && timePrisonResult.Events.Count(item =>
                   item.Kind == CombatSimulationEventKind.CardDrawn
                   && item.DefinitionId.StartsWith("ritual-", StringComparison.Ordinal)) == 3
               && timePrisonResult.FinalState.Player?.Statuses.Single(item =>
                       item.StatusId == "buff_ritualtimeprison")
                   .TriggerCounts["ThisCount"] == 0,
            "time prison counts actual deferred executions, applies ritual echo repeats, retrieves by tag, and resets its counter");

        var bundledCampaignSmoke = new CombatCampaignRunner().Run(
            bundledCampaign,
            bundledCampaignNormal,
            bundledRulesV2.Ruleset,
            new GreedyCombatSimulationPolicyFactory());
        Assert(bundledCampaignSmoke.CompletedBattles >= 1
               && !bundledCampaignSmoke.Invalid
               && Math.Abs(bundledCampaignSmoke.BattleSemanticCoverage - 1d)
               < 0.000001d,
            "bundled campaign v2 executes with complete authoritative semantic coverage");

        var knowledgePackage = new CombatKnowledgePackage
        {
            OwnerId = "Tests",
            PackageId = "authoritative-combat",
            GameBuild = "test-build",
            SourceHash = "test-source-hash",
            Actions =
            {
                new CombatKnowledgeActionDefinition
                {
                    SourceId = "elementscard_1",
                    Fidelity = CombatKnowledgeFidelity.Authoritative,
                    Semantics = new CombatActionSemantics
                    {
                        Draw = 1d,
                        Buff = 2d,
                        DamageMultiplierGain = 0.04d,
                        StateChanges = { ["status:buff_elements"] = 2d }
                    }
                },
                new CombatKnowledgeActionDefinition
                {
                    SourceId = "finisher",
                    Fidelity = CombatKnowledgeFidelity.Authoritative,
                    Semantics = new CombatActionSemantics { Damage = 20d }
                }
            },
            Statuses =
            {
                new CombatKnowledgeStatusDefinition
                {
                    StatusId = "buff_elements",
                    Fidelity = CombatKnowledgeFidelity.Authoritative
                }
            },
            Enemies =
            {
                new CombatKnowledgeEnemyDefinition
                {
                    EnemyId = "enemy_test",
                    Fidelity = CombatKnowledgeFidelity.Authoritative
                }
            }
        };
        using (CombatKnowledgeRegistry.RegisterPackage(
                   knowledgePackage,
                   out var knowledgeErrors))
        {
            var knowledgeState = new CombatStateObservation
            {
                Player = new CombatUnitObservation
                {
                    RuntimeId = 1,
                    Statuses =
                    {
                        new CombatStatusObservation { StatusId = "buff_elements", Level = 2 }
                    }
                },
                Enemies =
                {
                    new CombatUnitObservation
                    {
                        RuntimeId = 2,
                        Kind = CombatTargetKind.Enemy,
                        DefinitionId = "enemy_test",
                        CurrentHp = 10,
                        MaxHp = 10
                    }
                },
                Actions =
                {
                    new CombatActionObservation
                    {
                        CandidateId = "ocean",
                        SourceId = "elementscard_1",
                        Kind = CombatActionKind.PlayCard
                    }
                }
            };
            CombatAiRegistry.ApplySemantics(knowledgeState, knowledgeState.Actions[0]);
            var coverage = CombatKnowledgeRegistry.EvaluateCoverage(knowledgeState);
            Assert(knowledgeErrors.Count == 0
                   && knowledgeState.Actions[0].Semantics.Draw == 1d
                   && knowledgeState.Actions[0].Semantics.DamageMultiplierGain == 0.04d
                   && knowledgeState.Actions[0].SemanticFidelity
                   == CombatKnowledgeFidelity.Authoritative
                   && coverage.IsAuthoritative
                   && coverage.AuthoritativeCoverage == 1d,
                "versioned combat knowledge overrides heuristics and gates the full live state");

            knowledgeState.Player.Statuses.Add(
                new CombatStatusObservation { StatusId = "buff_unknown", Level = 1 });
            var incompleteCoverage = CombatKnowledgeRegistry.EvaluateCoverage(knowledgeState);
            Assert(!incompleteCoverage.IsAuthoritative
                   && incompleteCoverage.UnknownDefinitions.Contains("status:buff_unknown"),
                "knowledge coverage fails closed for an unregistered active buff");

            var drawRoot = new CombatStateObservation
            {
                Player = new CombatUnitObservation
                {
                    RuntimeId = 1,
                    CurrentHp = 20,
                    MaxHp = 20
                },
                HandCount = 1,
                DeckCardIds = { "finisher" },
                DeckKnowledge = new CombatDeckKnowledge
                {
                    DrawPileCount = 1,
                    KnownDeckCardIds = { "finisher" },
                    KnownTopCardIds = { "finisher" }
                }
            };
            var drawAction = new CombatActionObservation
            {
                SourceId = "draw",
                Kind = CombatActionKind.PlayCard,
                Semantics = new CombatActionSemantics { Draw = 1d }
            };
            var drawSimulation = CombatForwardModel.Create(drawRoot, 1);
            var drawOutcome = CombatForwardModel.Resolve(
                drawRoot,
                drawAction,
                useRegisteredResolvers: false).Outcomes[0];
            var afterDraw = CombatForwardModel.Apply(
                drawSimulation,
                drawAction,
                0,
                drawOutcome,
                new CombatDecisionProfile());
            Assert(afterDraw.DrawPileValues.Count == 0
                   && afterDraw.HandCount == 1
                   && afterDraw.DrawnCardPotential > 0d,
                "forward search values the exact next high-value deck card instead of count-only draw");
        }

        var expressionRulesBuilder = new CombatRulesetBuilder("expression-v1");
        expressionRulesBuilder.RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "Tests",
            StatusId = "scaling",
            DecayAtRoundEnd = false,
            DynamicModifiersPerStack = { ["PercentDamage"] = 0.1d }
        });
        var expressionRules = expressionRulesBuilder.Freeze();
        var expressionState = new CombatBattleState
        {
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    Hp = 10,
                    MaxHp = 10,
                    Variables = { ["PercentDamage"] = 1d },
                    Statuses =
                    {
                        new CombatStatusState { StatusId = "scaling", Stacks = 2 }
                    }
                }
            }
        };
        var expressionValue = CombatSimulationExpressionEvaluator.Evaluate(
            new CombatSimulationValueExpression
            {
                Operation = CombatSimulationValueOperation.Multiply,
                Arguments =
                {
                    new CombatSimulationValueExpression
                    {
                        Operation = CombatSimulationValueOperation.SourceVariable,
                        Key = "PercentDamage"
                    },
                    new CombatSimulationValueExpression
                    {
                        Operation = CombatSimulationValueOperation.Constant,
                        Constant = 10d
                    }
                }
            },
            expressionState,
            expressionRules.Ruleset,
            1,
            1);
        Assert(expressionRules.Success && Math.Abs(expressionValue - 12d) < 1e-9d,
            "simulation expressions resolve source-owned status modifiers without UI state");

        var oceanRulesBuilder = new CombatRulesetBuilder("ocean-v1");
        oceanRulesBuilder.RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "Tests",
            StatusId = "buff_extraordinary",
            DecayAtRoundEnd = false,
            DynamicModifiersPerStack = { ["PercentDamage"] = 0.01d }
        });
        oceanRulesBuilder.RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "Tests",
            StatusId = "buff_elements",
            DecayAtRoundEnd = false,
            Triggers =
            {
                new CombatStatusTriggerDefinition
                {
                    TriggerId = "action-after",
                    EventKind = CombatSimulationEventKind.ActionResolved,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.AddStatus,
                            Target = CombatSimulationTarget.Self,
                            DefinitionId = "buff_extraordinary",
                            Amount = 2,
                            ScaleWithStatusStacks = true
                        }
                    }
                }
            }
        });
        oceanRulesBuilder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "elementscard_1",
            Cost = 0,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Draw,
                    Target = CombatSimulationTarget.Self,
                    Amount = 1
                },
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.AddStatus,
                    Target = CombatSimulationTarget.Self,
                    DefinitionId = "buff_elements",
                    Amount = 2
                }
            }
        });
        var oceanRules = oceanRulesBuilder.Freeze();
        var oceanState = new CombatBattleState
        {
            PlayerActorId = 1,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    InstanceKey = "player",
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 20,
                    MaxHp = 20,
                    Energy = 1
                },
                new CombatActorState
                {
                    ActorId = 2,
                    InstanceKey = "enemy",
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 20,
                    MaxHp = 20
                }
            },
            Cards =
            {
                new CombatCardInstanceState { InstanceId = 1, CardId = "elementscard_1" },
                new CombatCardInstanceState { InstanceId = 2, CardId = "drawn" }
            },
            Hand = { 1 },
            DrawPile = { 2 }
        };
        var oceanApplication = new CombatSimulationEngine().ForkAndApplyPlayerAction(
            new CombatScenarioDefinition
            {
                ScenarioId = "ocean-contract",
                RulesetVersion = "ocean-v1",
                Player = new CombatPlayerSetup
                {
                    RoleId = "Tests",
                    MaxHp = 20,
                    CurrentHp = 20
                },
                Enemies = { new CombatEnemySetup { EnemyId = "unused" } }
            },
            oceanRules.Ruleset,
            oceanState,
            new CombatSimulationAction
            {
                CandidateId = "card:1",
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 1,
                DefinitionId = "elementscard_1"
            });
        var oceanPlayer = oceanApplication.State.FindActor(1);
        Assert(oceanApplication.Success
               && oceanPlayer?.Statuses.First(item => item.StatusId == "buff_elements").Stacks == 2
               && oceanPlayer.Statuses.First(item => item.StatusId == "buff_extraordinary").Stacks == 4
               && oceanApplication.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.ActionResolved),
            "Ocean Dream resolves draw, elements, and the same-action 4 percent damage setup chain");

        return new CombatAiSimulationTestContext(
            simulationEngine,
            simulationRules,
            bundledRulesV2);
    }
}
