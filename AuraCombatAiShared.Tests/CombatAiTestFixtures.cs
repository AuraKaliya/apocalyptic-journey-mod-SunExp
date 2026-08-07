using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;

internal sealed record CombatAiSimulationTestContext(
    CombatSimulationEngine Engine,
    CombatRulesetBuildResult Rules,
    CombatRulesetBuildResult BundledRules);

internal sealed class CombatAiTrainingTestContext
{
    public CombatAiTrainingTestContext(
        CombatAiSimulationTestContext simulation,
        List<CombatEpisode> episodes,
        CombatPolicyValueTrainingResult policyValueTraining,
        ManagedCombatPolicyValueModel policyValueModel,
        CombatStateObservation reusableState,
        List<CombatCandidateEvaluation> reusableCandidates)
    {
        Simulation = simulation;
        Episodes = episodes;
        PolicyValueTraining = policyValueTraining;
        PolicyValueModel = policyValueModel;
        ReusableState = reusableState;
        ReusableCandidates = reusableCandidates;
    }

    public CombatAiSimulationTestContext Simulation { get; }

    public List<CombatEpisode> Episodes { get; }

    public CombatPolicyValueTrainingResult PolicyValueTraining { get; }

    public ManagedCombatPolicyValueModel PolicyValueModel { get; }

    public CombatStateObservation ReusableState { get; }

    public List<CombatCandidateEvaluation> ReusableCandidates { get; }

    public CombatCampaignDefinition Campaign { get; set; } = null!;

    public CombatRulesetBuildResult CampaignRules { get; set; } = null!;

    public CombatCampaignFoundationTrainingResult FoundationTraining { get; set; }
        = null!;

    public CombatFoundationModelPackage FoundationPackage { get; set; } = null!;
}

internal static class CombatAiTestFixtures
{
    public static int Assertions { get; private set; }

    public static void ResetAssertions()
    {
        Assertions = 0;
    }

    internal static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + name);
        }

        Assertions++;
    }

    internal static CombatStateObservation BuildHandTransformFixture(bool valuableHand)
    {
        var handIds = valuableHand
            ? new[] { "engine", "enhanced-strike", "cycle" }
            : new[] { "curse", "blank", "risky" };
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                Kind = CombatTargetKind.Self,
                CurrentHp = 35,
                MaxHp = 40
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 200,
                    MaxHp = 200
                }
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = handIds.Length,
            HandCardIds = handIds.ToList(),
            DeckCardIds = handIds.ToList(),
            DeckKnowledge = new CombatDeckKnowledge(),
            IsPlayerActionWindow = true
        };
        var transform = new CombatActionObservation
        {
            CandidateId = "transform",
            SourceId = "careercard_4",
            Kind = CombatActionKind.UseSkill,
            Semantics = new CombatActionSemantics
            {
                OpensInteraction = true,
                HandTransform = new CombatHandTransformSemantic
                {
                    TargetCardId = "nocard_1",
                    TargetCardSemantics = new CombatActionSemantics
                    {
                        Damage = 10d,
                        Defend = 10d,
                        AffectedEnemyCount = 1
                    },
                    TransformAllHandCards = true,
                    PreserveInstances = true,
                    ClearsEnhancements = true,
                    ClearsVariables = true,
                    TargetRetained = true,
                    TargetExhaustsOnUse = true,
                    GrowthStateKey = "playerStatus:buff_Soul",
                    GrowthPerExhaust = 1d,
                    CurrentGrowthValue = 50d,
                    TargetTier = 1,
                    NextTierThreshold = 100,
                    CooldownProgressRequired = 12d
                }
            }
        };
        state.Actions.Add(transform);
        for (var index = 0; index < handIds.Length; index++)
        {
            var semantics = valuableHand
                ? index == 0
                    ? new CombatActionSemantics
                    {
                        Draw = 3d,
                        EnergyGain = 2d,
                        CardGeneration = 1d
                    }
                    : index == 1
                        ? new CombatActionSemantics { Damage = 30d }
                        : new CombatActionSemantics
                        {
                            Draw = 2d,
                            EnergyGain = 1d
                        }
                : index == 0
                    ? new CombatActionSemantics
                    {
                        Risk = 8d,
                        SelfHpLoss = 2d
                    }
                    : index == 1
                        ? new CombatActionSemantics()
                        : new CombatActionSemantics
                        {
                            Risk = 5d,
                            EndOfCycleSelfHpLoss = 3d
                        };
            var action = new CombatActionObservation
            {
                CandidateId = "card-" + index,
                SourceId = handIds[index],
                RuntimeId = 100 + index,
                Kind = CombatActionKind.PlayCard,
                Semantics = semantics
            };
            if (valuableHand && index == 0)
            {
                action.Features["strategyInfinite"] = 1d;
                action.Features["strategyExecutable"] = 1d;
            }
            state.Actions.Add(action);
            state.HandCards.Add(new CombatCardInstanceObservation
            {
                RuntimeId = action.RuntimeId,
                CardId = action.SourceId,
                EnhancementCount = valuableHand && index == 1 ? 2 : 0
            });
        }
        if (!valuableHand)
        {
            state.CardTagsById["curse"] = new List<string> { "Curse" };
            state.CardTagsById["blank"] = new List<string> { "Unusable" };
        }
        return state;
    }

    internal static CombatStateObservation BuildPlayerEquivalentFixture(bool reverseHiddenDrawOrder)
    {
        var state = new CombatStateObservation
        {
            BattleSessionId = 77,
            Sequence = 4,
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                Kind = CombatTargetKind.Self,
                CurrentHp = 24,
                MaxHp = 30,
                Features =
                {
                    ["ResurrectionSource"] = reverseHiddenDrawOrder ? 99d : 1d
                }
            },
            CurrentPower = 2,
            MaxPower = 3,
            HandCount = 1,
            HandCardIds = { "strike" },
            DeckCardIds = reverseHiddenDrawOrder
                ? new List<string> { "setup", "guard", "strike" }
                : new List<string> { "strike", "guard", "setup" },
            DeckKnowledge = new CombatDeckKnowledge
            {
                DrawPileCount = 2,
                KnownDeckCardIds = { "strike", "guard", "setup" }
            },
            Features =
            {
                ["turn"] = 1d,
                ["drawPileCount"] = 2d,
                ["secretRngCounter"] = reverseHiddenDrawOrder ? 700d : 3d
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    DefinitionId = "test-enemy",
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 8,
                    MaxHp = 8
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "strike:enemy",
                    SourceId = "strike",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 2000,
                    TargetRuntimeId = 2,
                    TargetKind = CombatTargetKind.Enemy,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Damage = 5d },
                    Features = { ["isCard"] = 1d }
                },
                new CombatActionObservation
                {
                    CandidateId = "end-turn",
                    SourceId = "end-turn",
                    Kind = CombatActionKind.EndTurn,
                    RuntimeId = 9000
                }
            }
        };
        return state;
    }

    internal static CombatStateObservation ProjectPlayerEquivalentHiddenOrder(
        CombatRuleset ruleset,
        bool reverseHiddenDrawOrder,
        double hiddenVariable)
    {
        var state = new CombatBattleState
        {
            Turn = 1,
            ActionSequence = 2,
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
                    Energy = 2,
                    BaseEnergy = 3,
                    Variables = { ["SecretCounter"] = hiddenVariable }
                }
            },
            Cards =
            {
                new CombatCardInstanceState { InstanceId = 1, CardId = "luckycard_3" },
                new CombatCardInstanceState { InstanceId = 2, CardId = "card_1" },
                new CombatCardInstanceState { InstanceId = 3, CardId = "ritualcard_1" }
            },
            Hand = { 1 }
        };
        if (reverseHiddenDrawOrder)
        {
            state.DrawPile.Add(3);
            state.DrawPile.Add(2);
        }
        else
        {
            state.DrawPile.Add(2);
            state.DrawPile.Add(3);
        }
        return PlayerEquivalentSimulationObservationProjector.Project(
            new CombatSimulationPolicyContext
            {
                Scenario = new CombatScenarioDefinition
                {
                    ScenarioId = "player-equivalent-projection",
                    Seed = 81,
                    HandLimit = 10
                },
                Ruleset = ruleset,
                State = state,
                LegalActions = new List<CombatSimulationAction>
                {
                    new CombatSimulationAction
                    {
                        CandidateId = "play-visible-card",
                        Kind = CombatSimulationActionKind.PlayCard,
                        CardInstanceId = 1,
                        DefinitionId = "luckycard_3",
                        Cost = 1
                    }
                }
            });
    }

    internal static CombatCampaignDefinition BuildStandardCampaign()
    {
        var result = new CombatCampaignDefinition
        {
            CampaignId = "tests.standard-campaign",
            CampaignVersion = "2.0.0",
            RulesetVersion = "campaign-test-rules",
            MainAttributeId = "Strength",
            SecondaryAttributeId = "Wisdom",
            Player = new CombatPlayerSetup
            {
                RoleId = "Tests",
                MaxHp = 20,
                CurrentHp = 20,
                BaseEnergy = 3,
                Deck = new List<string> { "strike" }
            },
            RolePrior = { ["burst"] = 0.5d },
            BuildTendency = { ["burst"] = 0.5d },
            BossPreference = { ["burst"] = 0.5d }
        };
        var presets = new[]
        {
            (10, 7, 5), (20, 10, 7), (25, 15, 10), (30, 20, 15),
            (35, 30, 17), (35, 35, 20), (40, 39, 20)
        };
        var route = new List<CombatCampaignEncounterKind>
        {
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Elite,
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Boss
        };
        for (var layer = 1; layer <= 7; layer++)
        {
            var preset = presets[layer - 1];
            result.Layers.Add(new CombatCampaignLayerDefinition
            {
                LayerNumber = layer,
                NativeBand = layer == 7 ? 3 : (layer - 1) / 2,
                Attributes = new CombatCampaignAttributePreset
                {
                    Main = preset.Item1,
                    Secondary = preset.Item2,
                    Unselected = preset.Item3
                },
                Route = layer == 7
                    ? new List<CombatCampaignEncounterKind>
                    {
                        CombatCampaignEncounterKind.FinalBoss
                    }
                    : new List<CombatCampaignEncounterKind>(route)
            });
        }
        for (var band = 0; band <= 2; band++)
        {
            foreach (var kind in new[]
                     {
                         CombatCampaignEncounterKind.Normal,
                         CombatCampaignEncounterKind.Elite,
                         CombatCampaignEncounterKind.Boss
                     })
            {
                result.Encounters.Add(new CombatCampaignEncounterDefinition
                {
                    EncounterId = "band-" + band + "-" + kind,
                    NativeBand = band,
                    Kind = kind,
                    EnemyIds = new List<string> { "enemy_" + band + "_" + kind }
                });
                result.Enemies.Add(new CombatCampaignEnemyCatalogEntry
                {
                    EnemyId = "enemy_" + band + "_" + kind,
                    NativeLevel = band + 1
                });
            }
        }
        result.Encounters.Add(new CombatCampaignEncounterDefinition
        {
            EncounterId = "universal-normal",
            NativeBand = -1,
            Kind = CombatCampaignEncounterKind.Normal,
            EnemyIds = new List<string> { "enemy_universal" }
        });
        result.Enemies.Add(new CombatCampaignEnemyCatalogEntry
        {
            EnemyId = "enemy_universal",
            NativeLevel = 1
        });
        foreach (var finalId in new[]
                 {
                     "caroline-final", "evernight-final", "demon-king-final", "judgment-final"
                 })
        {
            result.Encounters.Add(new CombatCampaignEncounterDefinition
            {
                EncounterId = finalId,
                NativeBand = 3,
                Kind = CombatCampaignEncounterKind.FinalBoss,
                EnemyIds = new List<string> { "enemy_" + finalId }
            });
            result.Enemies.Add(new CombatCampaignEnemyCatalogEntry
            {
                EnemyId = "enemy_" + finalId,
                NativeLevel = 4
            });
        }
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "strike",
            Kind = CombatCampaignRewardKind.Card,
            Tier = 1,
            BaseValue = 1d,
            Fidelity = CombatRuleFidelity.Authoritative,
            Features = { ["burst"] = 1d }
        });
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "guard",
            Kind = CombatCampaignRewardKind.Card,
            Tier = 1,
            BaseValue = 0.2d,
            Fidelity = CombatRuleFidelity.Authoritative
        });
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "skip-me",
            Kind = CombatCampaignRewardKind.Card,
            Tier = 1,
            BaseValue = -2d,
            Fidelity = CombatRuleFidelity.Authoritative
        });
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "SpellCard_1",
            Kind = CombatCampaignRewardKind.Card,
            CardAcquisition = CombatCampaignCardAcquisition.GeneratedOnly,
            Tier = 4,
            BaseValue = 100d,
            Fidelity = CombatRuleFidelity.Authoritative
        });
        for (var index = 1; index <= 40; index++)
        {
            result.Rewards.Add(new CombatCampaignRewardDefinition
            {
                RewardId = "relic_" + index,
                Kind = CombatCampaignRewardKind.Relic,
                Tier = (index - 1) % 4 + 1,
                BaseValue = index / 100d,
                Fidelity = CombatRuleFidelity.Authoritative
            });
            result.Rewards.Add(new CombatCampaignRewardDefinition
            {
                RewardId = "blessing_" + index,
                Kind = CombatCampaignRewardKind.Blessing,
                Tier = (index - 1) % 4 + 1,
                BaseValue = index / 100d,
                Fidelity = CombatRuleFidelity.Authoritative
            });
        }
        result.Rewards.Add(new CombatCampaignRewardDefinition
        {
            RewardId = "negative-blessing",
            Kind = CombatCampaignRewardKind.Blessing,
            Tier = 3,
            Negative = true,
            Fidelity = CombatRuleFidelity.Authoritative
        });
        foreach (var thresholdReward in new[]
                 {
                     ("Strength", 10, "blessing_101"),
                     ("Strength", 20, "blessing_105"),
                     ("Strength", 30, "blessing_109"),
                     ("Strength", 40, "blessing_113"),
                     ("Lucky", 10, "blessing_102"),
                     ("Lucky", 20, "blessing_106"),
                     ("Lucky", 30, "blessing_110"),
                     ("Lucky", 40, "blessing_114"),
                     ("Perceive", 10, "blessing_104"),
                     ("Perceive", 20, "blessing_108"),
                     ("Perceive", 30, "blessing_112"),
                     ("Perceive", 40, "blessing_116"),
                     ("Wisdom", 10, "blessing_103"),
                     ("Wisdom", 20, "blessing_107"),
                     ("Wisdom", 30, "blessing_111"),
                     ("Wisdom", 40, "blessing_115")
                 })
        {
            result.AttributeThresholdRewards.Add(
                new CombatCampaignAttributeThresholdRewardDefinition
                {
                    AttributeId = thresholdReward.Item1,
                    Threshold = thresholdReward.Item2,
                    RewardId = thresholdReward.Item3
                });
            result.Rewards.Add(new CombatCampaignRewardDefinition
            {
                RewardId = thresholdReward.Item3,
                Kind = CombatCampaignRewardKind.Blessing,
                Tier = thresholdReward.Item2 / 10,
                Fidelity = CombatRuleFidelity.Authoritative
            });
        }
        result.Difficulties.Add(new CombatCampaignDifficultyDefinition
        {
            DifficultyId = "normal",
            DisplayName = "普通难度"
        });
        result.Difficulties.Add(new CombatCampaignDifficultyDefinition
        {
            DifficultyId = "advanced",
            DisplayName = "高级难度",
            EnemyHpMultiplier = 1.4d,
            EnemyAttackMultiplier = 1.4d,
            ApplyGameLevelShield = true,
            HardAffixes =
            {
                new CombatCampaignHardAffixDefinition
                {
                    AffixId = "Hard_3",
                    Stacks = 4,
                    CombatRelevant = true,
                    Implemented = true
                }
            }
        });
        return result;
    }

    internal static CombatRulesetBuildResult BuildSimulationRuleset(string version = "test-v1")
    {
        return new CombatRulesetBuilder(version)
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "looping",
                DisplayName = "Looping",
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "repeat-status",
                        EventKind = CombatSimulationEventKind.StatusAdded,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.AddStatus,
                                Target = CombatSimulationTarget.Self,
                                DefinitionId = "looping",
                                Amount = 1
                            }
                        }
                    }
                }
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "training",
                DisplayName = "Training",
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "guard-after-card",
                        EventKind = CombatSimulationEventKind.CardPlayed,
                        Priority = 10,
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
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "loop-seed",
                DisplayName = "Loop Seed",
                Cost = 0,
                Exhaust = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.AddStatus,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "looping",
                        Amount = 1
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "strike",
                DisplayName = "Strike",
                Cost = 1,
                RequiresEnemyTarget = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 6
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "guard",
                DisplayName = "Guard",
                Cost = 1,
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
                CardId = "insight",
                DisplayName = "Insight",
                Cost = 0,
                Exhaust = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Draw,
                        Target = CombatSimulationTarget.Self,
                        Amount = 1
                    }
                }
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "dummy",
                DisplayName = "Training Dummy",
                MaxHp = 18,
                ActionCount = 2,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "hit",
                        DisplayName = "Hit",
                        Weight = 1,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.Damage,
                                Target = CombatSimulationTarget.Player,
                                Amount = 4
                            }
                        }
                    },
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        DisplayName = "Wait",
                        Weight = 1,
                        Effects = new List<CombatSimulationEffectDefinition>()
                    }
                }
            })
            .Freeze();
    }

    internal static CombatScenarioDefinition BuildSimulationScenario(
        ulong seed,
        CombatSimulationTraceLevel traceLevel)
    {
        return new CombatScenarioDefinition
        {
            ScenarioId = "training-dummy",
            RulesetVersion = "test-v1",
            Seed = seed,
            InitialDraw = 4,
            DrawPerTurn = 4,
            HandLimit = 10,
            TraceLevel = traceLevel,
            Player = new CombatPlayerSetup
            {
                RoleId = "tester",
                MaxHp = 30,
                CurrentHp = 30,
                BaseEnergy = 3,
                Deck = { "strike", "strike", "guard", "insight" },
                InitialStatuses =
                {
                    new CombatInitialStatus
                    {
                        StatusId = "training",
                        Stacks = 1
                    }
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = "dummy",
                    InstanceKey = "dummy:1"
                }
            },
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 20,
                MaximumActions = 100,
                MaximumCommands = 1000
            }
        };
    }

}
