using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

internal static class CombatAiCampaignBehaviorTests
{
    public static void Run(CombatAiTrainingTestContext context)
    {
        var simulationEngine = context.Simulation.Engine;
        var simulationRules = context.Simulation.Rules;
        var bundledRulesV2 = context.Simulation.BundledRules;
        var episodes = context.Episodes;
        var policyValueTraining = context.PolicyValueTraining;
        var policyValueModel = context.PolicyValueModel;
        var reusableState = context.ReusableState;
        var reusableCandidates = context.ReusableCandidates;
        var campaign = BuildStandardCampaign();
        CombatCampaignWorldPlanner.Validate(campaign);
        var normalPlan = CombatCampaignWorldPlanner.Build(campaign, "normal", 700UL);
        var advancedPlan = CombatCampaignWorldPlanner.Build(campaign, "advanced", 700UL);
        Assert(normalPlan.Encounters.Count == 37
               && normalPlan.Encounters.Take(12).All(item =>
                   item.LayerNumber is 1 or 2 && item.GameLevel is >= 0 and <= 11)
               && normalPlan.Encounters.Skip(12).Take(12).All(item =>
                   item.LayerNumber is 3 or 4 && item.GameLevel is >= 12 and <= 23)
               && normalPlan.Encounters.Skip(24).Take(12).All(item =>
                   item.LayerNumber is 5 or 6 && item.GameLevel is >= 24 and <= 35)
               && normalPlan.Encounters[36].Kind == CombatCampaignEncounterKind.FinalBoss,
            "campaign v2 maps six fixed layers to the native 0/1/2 encounter bands");
        Assert(normalPlan.Encounters.Take(36).All(item =>
                   item.RewardOffer.CardRounds.Count
                   == (item.Kind == CombatCampaignEncounterKind.Normal ? 1 : 0)
                   && item.RewardOffer.CardRounds.All(round => round.Count == 3)
                   && !string.IsNullOrWhiteSpace(item.RewardOffer.RelicId)
                   && !string.IsNullOrWhiteSpace(item.RewardOffer.BlessingId))
               && normalPlan.Encounters.Take(36).Sum(item =>
                   item.RewardOffer.CardRounds.Count) == 24
               && normalPlan.Encounters.SelectMany(item =>
                       item.RewardOffer.CardRounds)
                   .SelectMany(round => round)
                   .All(id => !CombatCampaignCardAcquisitionPolicy
                       .IsGeneratedOnlyIdentifier(id))
               && normalPlan.Encounters[36].RewardOffer.CardRounds.Count == 0,
            "campaign v2 limits card growth and excludes generated-only cards from every reward offer");
        Assert(normalPlan.PlanHash != advancedPlan.PlanHash
               && normalPlan.Encounters.Select(item => item.EncounterId)
                   .SequenceEqual(advancedPlan.Encounters.Select(item => item.EncounterId)),
            "difficulty is part of evaluation identity without changing the paired encounter stream");

        var rewardPoolPackRule = new CombatCampaignRewardDefinition
        {
            RewardId = "pack-3-card",
            Kind = CombatCampaignRewardKind.Card,
            RewardCardPackId = "cardpack_3",
            CardAcquisition = CombatCampaignCardAcquisition.RewardPool
        };
        var blankPackRule = new CombatCampaignRewardDefinition
        {
            RewardId = "blank-pack-card",
            Kind = CombatCampaignRewardKind.Card,
            CardAcquisition = CombatCampaignCardAcquisition.RewardPool
        };
        var roleSkillRule = new CombatCampaignRewardDefinition
        {
            RewardId = "careercard_test",
            Kind = CombatCampaignRewardKind.Card,
            RewardCardPackId = "cardpack_1",
            CardAcquisition = CombatCampaignCardAcquisition.SkillOnly
        };
        Assert(!CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
                   rewardPoolPackRule,
                   new[] { "cardpack_1", "cardpack_2" })
               && CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
                   rewardPoolPackRule,
                   new[] { "cardpack_1", "cardpack_2", "cardpack_3" })
               && CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
                   blankPackRule,
                   Array.Empty<string>())
               && !CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(
                   roleSkillRule,
                   new[] { "cardpack_1", "cardpack_2" }),
            "reward packs filter only RewardPool cards, map blank ownership to cardpack_1, and never leak role skills");

        var strategyCampaign = new CombatCampaignDefinition
        {
            Player = new CombatPlayerSetup
            {
                RoleId = "strategy-role",
                SkillCardIds = { "careercard_test" }
            },
            EnabledRewardCardPackIds =
            {
                "cardpack_1",
                "cardpack_2",
                "cardpack_3"
            },
            TargetDeckSizeMinimum = 1,
            TargetDeckSizeMaximum = 24,
            DeckSizeAlertThreshold = 25
        };
        var strategyCards = new[]
        {
            "cycle-a", "cycle-b", "cycle-c", "cycle-d",
            "cycle-e", "cycle-f", "cycle-g", "cycle-h"
        };
        for (var index = 0; index < strategyCards.Length; index++)
        {
            strategyCampaign.Rewards.Add(new CombatCampaignRewardDefinition
            {
                RewardId = strategyCards[index],
                Kind = CombatCampaignRewardKind.Card,
                RewardCardPackId = index == 7 ? "cardpack_18" : "cardpack_3",
                CardAcquisition = CombatCampaignCardAcquisition.RewardPool
            });
            strategyCampaign.Rewards.Add(new CombatCampaignRewardDefinition
            {
                RewardId = "strategy-relic-" + index,
                Kind = CombatCampaignRewardKind.Relic
            });
            strategyCampaign.Strategies.Add(new CombatCampaignStrategyDefinition
            {
                StrategyId = "deterministic-strategy-" + index,
                Kind = index < 2
                    ? CombatCampaignStrategyKind.Infinite
                    : CombatCampaignStrategyKind.Cycle,
                Deterministic = true,
                RequiredCardIds = { strategyCards[index] },
                RequiredRelicIds = { "strategy-relic-" + index },
                MaximumActiveDeckSize = 24,
                RewardCompletionBonus = 4d
            });
        }
        var strategyLookup = strategyCampaign.Rewards.ToDictionary(
            item => item.RewardId,
            StringComparer.OrdinalIgnoreCase);
        var completedStrategyState = new CombatCampaignState
        {
            Deck = strategyCards.ToList(),
            Relics = Enumerable.Range(0, 8)
                .Select(index => "strategy-relic-" + index)
                .ToList()
        };
        var completedStrategies = CombatCampaignStrategyEvaluator.Evaluate(
            strategyCampaign,
            completedStrategyState,
            strategyLookup);
        Assert(completedStrategies.Count == 8
               && completedStrategies.All(item =>
                   item.Accessible
                   && item.Executable
                   && Math.Abs(item.Completion - 1d) < 0.0001d),
            "all eight deterministic cycle and infinite strategy presets certify when their active-deck components are present");
        strategyCampaign.EnabledRewardCardPackIds.RemoveAll(item =>
            string.Equals(item, "cardpack_18", StringComparison.OrdinalIgnoreCase));
        var disabledComponentProgress = CombatCampaignStrategyEvaluator.Evaluate(
                strategyCampaign,
                new CombatCampaignState(),
                strategyLookup)
            .Single(item => item.StrategyId == "deterministic-strategy-7");
        Assert(!disabledComponentProgress.Accessible
               && disabledComponentProgress.Completion == 0d,
            "components from disabled reward packs cannot contribute phantom strategy completion");
        strategyCampaign.EnabledRewardCardPackIds.Add("cardpack_18");
        var partialStrategyState = new CombatCampaignState
        {
            Relics = { "strategy-relic-0" }
        };
        var componentScore = CombatCampaignStrategyEvaluator.MarginalRewardValue(
            strategyCampaign,
            partialStrategyState,
            strategyLookup,
            strategyLookup["cycle-a"]);
        Assert(componentScore > 0d,
            "a reward that closes a deterministic strategy receives a positive completion bonus");

        var strategyProjectionRulesBuilder =
            new CombatRulesetBuilder("strategy-projection");
        strategyProjectionRulesBuilder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "cycle-a",
            Cost = 0,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.GainBlock,
                    Target = CombatSimulationTarget.Self,
                    Amount = 1
                }
            }
        });
        var strategyProjectionRules = strategyProjectionRulesBuilder.Freeze();
        var strategyProjectionState = new CombatBattleState
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
                    Energy = 1
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = "cycle-a"
                }
            },
            Hand = { 1 }
        };
        var strategyProjectionAction = new CombatSimulationAction
        {
            CandidateId = "card:1",
            Kind = CombatSimulationActionKind.PlayCard,
            ActorId = 1,
            CardInstanceId = 1,
            DefinitionId = "cycle-a"
        };
        var strategyProjectionPolicy = new CombatDecisionSimulationPolicy(
            new CombatDecisionProfile
            {
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 1,
                SearchNodeBudget = 8,
                SearchMaxPly = 1,
                SearchMinimumSimulations = 1
            });
        strategyProjectionPolicy.SelectAction(new CombatSimulationPolicyContext
        {
            Scenario = new CombatScenarioDefinition
            {
                StrategyProgress =
                {
                    new CombatScenarioStrategyProgress
                    {
                        StrategyId = "deterministic-strategy-0",
                        Kind = "Infinite",
                        Deterministic = true,
                        Executable = true,
                        Completion = 1d,
                        PlayPriority = 1.5d,
                        ComponentCardIds = { "cycle-a" }
                    }
                }
            },
            Ruleset = strategyProjectionRules.Ruleset,
            State = strategyProjectionState,
            LegalActions = new List<CombatSimulationAction>
            {
                strategyProjectionAction,
                new CombatSimulationAction
                {
                    CandidateId = "end-turn",
                    Kind = CombatSimulationActionKind.EndTurn,
                    ActorId = 1
                }
            }
        });
        var projectedStrategyAction =
            strategyProjectionPolicy.LastObservation!.Actions.Single(item =>
                item.CandidateId == strategyProjectionAction.CandidateId);
        Assert(projectedStrategyAction.Features["synergy"] > 0d
               && projectedStrategyAction.Features["strategyInfinite"] == 1d,
            "completed deterministic strategy progress is projected into card-play ordering features");

        CombatDecisionSimulationPolicy frozenPreparationPolicy;
        using (CombatAiRegistry.RegisterSemanticProvider(
                   "Tests",
                   "frozen-preparation-semantic",
                   new FrozenPreparationSemanticProvider("cycle-a"),
                   10000))
        using (CombatAiRegistry.RegisterRoleStrategyProvider(
                   "Tests",
                   "frozen-preparation-role",
                   new FrozenPreparationRoleStrategyProvider(),
                   10000))
        {
            frozenPreparationPolicy = new CombatDecisionSimulationPolicy(
                new CombatDecisionProfile
                {
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 1,
                    SearchNodeBudget = 8,
                    SearchMaxPly = 1,
                    SearchMinimumSimulations = 1
                });
        }
        frozenPreparationPolicy.SelectAction(new CombatSimulationPolicyContext
        {
            Scenario = new CombatScenarioDefinition(),
            Ruleset = strategyProjectionRules.Ruleset,
            State = strategyProjectionState.Clone(),
            LegalActions = new List<CombatSimulationAction>
            {
                strategyProjectionAction,
                new CombatSimulationAction
                {
                    CandidateId = "end-turn-frozen",
                    Kind = CombatSimulationActionKind.EndTurn,
                    ActorId = 1
                }
            }
        });
        var frozenPreparedAction = frozenPreparationPolicy.LastDecision!.Candidates
            .Single(item => item.Action.CandidateId
                            == strategyProjectionAction.CandidateId)
            .Action;
        Assert(
            frozenPreparedAction.SemanticFidelity
            == CombatKnowledgeFidelity.Authoritative
            && frozenPreparedAction.Semantics.Buff == 17d
            && frozenPreparedAction.Features[CombatRoleStrategyFeatureNames.Active]
               == 1d,
            "isolated simulation policies retain a frozen semantic and role-strategy snapshot after registry lifetimes end");

        var countingSemanticProvider = new CountingSemanticProvider("cycle-a");
        var registryRevisionBeforeCounting = CombatAiRegistry.Revision;
        using (CombatAiRegistry.RegisterSemanticProvider(
                   "Tests",
                   "single-pass-decision-semantics",
                   countingSemanticProvider,
                   20000))
        {
            var countingState = CombatPlayerObservationBoundary.Normalize(
                frozenPreparationPolicy.LastObservation!);
            var countingEngine = new CombatDecisionEngine();
            var preparedCountingState = countingEngine
                .PrepareNormalizedOwnedStateForIsolatedWorker(countingState);
            countingEngine.ChoosePrepared(
                preparedCountingState,
                new CombatDecisionProfile
                {
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 1,
                    SearchNodeBudget = 8,
                    SearchMaxPly = 1,
                    SearchMinimumSimulations = 1
                });
        }
        Assert(countingSemanticProvider.CallCount == 1
               && CombatAiRegistry.Revision
                  > registryRevisionBeforeCounting,
            "published registry snapshots advance their revision and prepared decisions apply authoritative semantics once per legal candidate");

        var roleSkillRulesBuilder = new CombatRulesetBuilder("role-skill-rules");
        roleSkillRulesBuilder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "careercard_test",
            Cost = 9,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.GainBlock,
                    Target = CombatSimulationTarget.Self,
                    Amount = 3
                }
            }
        });
        var roleSkillRules = roleSkillRulesBuilder.Freeze();
        var roleSkillState = new CombatBattleState
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
                    Energy = 0
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = "careercard_test"
                }
            },
            SkillCards = { 1 },
            SkillCooldowns = { [1] = 0 }
        };
        var roleSkillScenario = new CombatScenarioDefinition
        {
            Player = new CombatPlayerSetup
            {
                SkillCardIds = { "careercard_test" },
                SkillCooldownTurns = { ["careercard_test"] = 5 }
            }
        };
        var roleSkillEngine = new CombatSimulationEngine();
        var legalRoleSkill = roleSkillEngine.GetLegalPlayerActions(
                roleSkillScenario,
                roleSkillRules.Ruleset,
                roleSkillState)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        var appliedRoleSkill = roleSkillEngine.ForkAndApplyPlayerAction(
            roleSkillScenario,
            roleSkillRules.Ruleset,
            roleSkillState,
            legalRoleSkill);
        Assert(appliedRoleSkill.Success
               && appliedRoleSkill.State.SkillCards.SequenceEqual(new[] { 1 })
               && appliedRoleSkill.State.Hand.Count == 0
               && appliedRoleSkill.State.DiscardPile.Count == 0
               && appliedRoleSkill.State.Player?.Block == 3
               && appliedRoleSkill.State.Player?.Energy == 0
               && appliedRoleSkill.State.SkillCooldowns[1] == 5
               && appliedRoleSkill.State.SkillActivationCounts.GetValueOrDefault(
                   "careercard_test") == 1,
            "role skills remain outside the deck, ignore printed card energy cost, and use role-specific cooldowns");

        var projectedRoleSkillState = PlayerEquivalentSimulationObservationProjector.Project(
            new CombatSimulationPolicyContext
            {
                Scenario = roleSkillScenario,
                Ruleset = roleSkillRules.Ruleset,
                State = roleSkillState,
                LegalActions = new List<CombatSimulationAction> { legalRoleSkill }
            });
        var projectedRoleSkill = projectedRoleSkillState.Actions.Single();
        Assert(projectedRoleSkill.Features.GetValueOrDefault(
                   CombatSkillTimingFeatureNames.ResetsEachBattle) == 1d
               && projectedRoleSkill.Features.GetValueOrDefault(
                   CombatSkillTimingFeatureNames.CooldownAfterUse) == 5d
               && projectedRoleSkill.Features.GetValueOrDefault(
                   CombatSkillTimingFeatureNames.CurrentCooldown) == 0d
               && projectedRoleSkill.Features.GetValueOrDefault(
                   CombatSkillTimingFeatureNames.ActivationsThisBattle) == 0d,
            "simulation observations expose generic per-battle role-skill lifecycle features");

        var waitForSkill = new CombatActionObservation
        {
            SourceId = "timing-skill",
            Kind = CombatActionKind.UseSkill,
            Features =
            {
                [CombatSkillTimingFeatureNames.Active] = 1d,
                [CombatSkillTimingFeatureNames.OngoingEffectValue] = 2d,
                [CombatSkillTimingFeatureNames.DelayGain] = 5d
            }
        };
        var waitTiming = CombatSkillTimingPolicy.Enrich(waitForSkill);
        var waitUtility = CombatDecisionEngine.BuildUtility(
            new CombatStateObservation
            {
                Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 }
            },
            waitForSkill,
            new CombatDecisionProfile());
        Assert(waitTiming.Active
               && waitTiming.BetterToWait
               && waitTiming.TimingAdvantage == -3d
               && waitForSkill.Features.GetValueOrDefault(
                   CombatSkillTimingFeatureNames.PositiveOpportunity) == 0d
               && waitUtility.Risk >= 3d
               && waitForSkill.Features.GetValueOrDefault(
                   CombatRoleStrategyFeatureNames.StrategicallyProhibited) == 0d,
            "generic skill timing can prefer waiting without turning non-use into a hard prohibition");

        waitForSkill.Features[CombatSkillTimingFeatureNames.ExpiryRisk] = 6d;
        var useNowTiming = CombatSkillTimingPolicy.Enrich(waitForSkill);
        var useNowUtility = CombatDecisionEngine.BuildUtility(
            new CombatStateObservation
            {
                Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 }
            },
            waitForSkill,
            new CombatDecisionProfile());
        Assert(useNowTiming.PositiveOpportunity
               && useNowTiming.TimingAdvantage == 3d
               && useNowUtility.Coordination >= 3d,
            "generic skill timing favors activation when ongoing value and expiry risk exceed the value of waiting");

        var contractRulesBuilder = new CombatRulesetBuilder("action-contract-rules");
        contractRulesBuilder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "contract_draw_skill",
            VerificationSource = "Decompiler:test",
            ActionContract = new CombatActionContractDefinition
            {
                Preconditions =
                {
                    new CombatActionPreconditionDefinition
                    {
                        Kind = CombatActionPreconditionKind.DrawPileCountAtLeast,
                        Amount = 1
                    },
                    new CombatActionPreconditionDefinition
                    {
                        Kind = CombatActionPreconditionKind.AvailableHandSlotsAtLeast,
                        Amount = 1
                    }
                },
                MinimumCardsMovedFromDrawPileToHandOnApplied = 1
            },
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Draw,
                    Target = CombatSimulationTarget.Self,
                    Amount = 1
                }
            }
        });
        contractRulesBuilder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "contract_draw_target"
        });
        var contractRules = contractRulesBuilder.Freeze();
        Assert(contractRules.Success
               && contractRules.Ruleset.TryGetCard(
                   "contract_draw_skill",
                   out var frozenContractSkill)
               && frozenContractSkill.VerificationSource == "Decompiler:test"
               && frozenContractSkill.ActionContract?.Version
               == CombatActionContractProtocol.Version,
            "action contracts are validated, cloned, and retained by the ruleset");
        var invalidContractRules = new CombatRulesetBuilder("invalid-action-contract")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "invalid_contract_skill",
                ActionContract = new CombatActionContractDefinition
                {
                    MinimumCardsMovedFromDrawPileToHandOnApplied = 1
                }
            })
            .Freeze();
        Assert(!invalidContractRules.Success
               && invalidContractRules.Errors.Any(item =>
                   item.Contains(
                       "postcondition lacks matching prerequisites",
                       StringComparison.Ordinal)),
            "ruleset validation rejects interactive postconditions without matching prerequisites");
        var contractScenario = new CombatScenarioDefinition
        {
            HandLimit = 1,
            Player = new CombatPlayerSetup
            {
                SkillCardIds = { "contract_draw_skill" },
                SkillCooldownTurns = { ["contract_draw_skill"] = 1 }
            }
        };
        CombatBattleState ContractState(bool withDrawCard, bool handFull)
        {
            var state = new CombatBattleState
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
                        Energy = 0
                    }
                },
                Cards =
                {
                    new CombatCardInstanceState
                    {
                        InstanceId = 1,
                        CardId = "contract_draw_skill"
                    }
                },
                SkillCards = { 1 },
                SkillCooldowns = { [1] = 0 },
                NextCardInstanceId = 2
            };
            if (withDrawCard)
            {
                state.Cards.Add(new CombatCardInstanceState
                {
                    InstanceId = 2,
                    CardId = "contract_draw_target"
                });
                state.DrawPile.Add(2);
                state.NextCardInstanceId = 3;
            }
            if (handFull)
            {
                state.Cards.Add(new CombatCardInstanceState
                {
                    InstanceId = 3,
                    CardId = "contract_draw_target"
                });
                state.Hand.Add(3);
                state.NextCardInstanceId = 4;
            }
            return state;
        }

        var contractEngine = new CombatSimulationEngine();
        var emptyDrawState = ContractState(withDrawCard: false, handFull: false);
        var invocableEmptyDrawSkill = contractEngine.GetInvocablePlayerActions(
                contractScenario,
                contractRules.Ruleset,
                emptyDrawState)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        Assert(invocableEmptyDrawSkill.GameInvocable
               && !invocableEmptyDrawSkill.PolicyEligible
               && invocableEmptyDrawSkill.ExpectedOutcome
               == CombatActionApplicationOutcome.NoEffect
               && !contractEngine.GetLegalPlayerActions(
                       contractScenario,
                       contractRules.Ruleset,
                       emptyDrawState)
                   .Any(item => item.Kind == CombatSimulationActionKind.UseSkill),
            "a guaranteed no-effect skill stays game-invocable but is excluded from policy candidates");
        var forcedEmptyDraw = contractEngine.ForkAndApplyPlayerAction(
            contractScenario,
            contractRules.Ruleset,
            emptyDrawState,
            invocableEmptyDrawSkill,
            allowPolicyIneligible: true);
        Assert(forcedEmptyDraw.Success
               && forcedEmptyDraw.Outcome == CombatActionApplicationOutcome.NoEffect
               && forcedEmptyDraw.State.ActionSequence == emptyDrawState.ActionSequence
               && forcedEmptyDraw.State.Hand.Count == 0
               && forcedEmptyDraw.State.DrawPile.Count == 0
               && forcedEmptyDraw.State.SkillCooldowns[1] == 0
               && forcedEmptyDraw.State.NoEffectActionAttemptsThisTurn.Values.Single()
               == 1,
            "forced no-effect execution preserves battle state and records controller failure memory");
        var suppressedEmptyDrawSkill = contractEngine.GetInvocablePlayerActions(
                contractScenario,
                contractRules.Ruleset,
                forcedEmptyDraw.State)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        var forcedRepeatedNoEffect = contractEngine.ForkAndApplyPlayerAction(
            contractScenario,
            contractRules.Ruleset,
            forcedEmptyDraw.State,
            suppressedEmptyDrawSkill,
            allowPolicyIneligible: true);
        Assert(forcedRepeatedNoEffect.Success
               && forcedRepeatedNoEffect.Outcome
               == CombatActionApplicationOutcome.NoEffect
               && forcedRepeatedNoEffect.State.NoEffectActionAttemptsThisTurn
                   .Values.Single() == 2,
            "no-effect attempt memory persists for the remainder of the simulated turn");

        var fullHandState = ContractState(withDrawCard: true, handFull: true);
        var fullHandSkill = contractEngine.GetInvocablePlayerActions(
                contractScenario,
                contractRules.Ruleset,
                fullHandState)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        Assert(!fullHandSkill.PolicyEligible
               && fullHandSkill.ExpectedOutcome
               == CombatActionApplicationOutcome.NoEffect,
            "hand-capacity preconditions exclude a guaranteed no-effect skill");

        var applicableContractState =
            ContractState(withDrawCard: true, handFull: false);
        var applicableContractSkill = contractEngine.GetLegalPlayerActions(
                contractScenario,
                contractRules.Ruleset,
                applicableContractState)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        var appliedContractSkill = contractEngine.ForkAndApplyPlayerAction(
            contractScenario,
            contractRules.Ruleset,
            applicableContractState,
            applicableContractSkill);
        Assert(appliedContractSkill.Success
               && appliedContractSkill.Outcome
               == CombatActionApplicationOutcome.Applied
               && appliedContractSkill.State.DrawPile.Count == 0
               && appliedContractSkill.State.Hand.SequenceEqual(new[] { 2 })
               && appliedContractSkill.State.SkillCooldowns[1] == 1,
            "successful contract execution satisfies its draw-to-hand postcondition before cooldown");

        var causalContract = new CombatActionContractDefinition
        {
            MinimumCardsMovedFromDrawPileToHandOnApplied = 1
        };
        var causalBeforeState = new CombatBattleState
        {
            DrawPile = { 2 },
            Hand = { 3 }
        };
        var causalAfterState = new CombatBattleState
        {
            DrawPile = { 4, 5 },
            Hand = { 2, 3 }
        };
        var causalPostconditionPassed =
            CombatActionContractEvaluator.AppliedPostconditionsSatisfied(
                causalContract,
                CombatActionContractSnapshot.Capture(causalBeforeState),
                CombatActionContractSnapshot.Capture(causalAfterState),
                new[]
                {
                    new CombatSimulationEvent
                    {
                        Kind = CombatSimulationEventKind.CardDrawn,
                        CardInstanceId = 2,
                        SourceActionId = 7
                    },
                    new CombatSimulationEvent
                    {
                        Kind = CombatSimulationEventKind.CardCreated,
                        CardInstanceId = 4,
                        SourceActionId = 7
                    }
                },
                7,
                out _);
        Assert(causalPostconditionPassed,
            "action-contract-v2 accepts a causally proven draw-to-hand move even when concurrent card creation makes both net zone counts grow");

        var familiarRule = new CombatCampaignRewardDefinition
        {
            RewardId = "familiar-blessing",
            Kind = CombatCampaignRewardKind.Blessing,
            BlessingAcquisition = CombatCampaignBlessingAcquisition.FamiliarInnate,
            FightScript = "SetStatus(\"Self\");"
        };
        var familiarRules = CombatCampaignRewardRuleProjector.Build(
            new CombatCampaignDefinition
            {
                Rewards = { familiarRule }
            },
            new CombatCampaignState
            {
                InnateBlessings = { "familiar-blessing" }
            });
        Assert(familiarRules.Count == 1
               && familiarRules[0].RewardId == "familiar-blessing"
               && familiarRules[0].Stacks == 1,
            "familiar blessings are projected as innate battle rules without entering ordinary blessing rewards");

        var campaignRulesBuilder = new CombatRulesetBuilder(campaign.RulesetVersion);
        foreach (var cardId in new[] { "strike", "guard", "skip-me" })
        {
            campaignRulesBuilder.RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = cardId,
                Cost = 0,
                RequiresEnemyTarget = true,
                Fidelity = CombatRuleFidelity.Authoritative,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 1
                    }
                }
            });
        }
        foreach (var enemyId in campaign.Encounters
                     .SelectMany(item => item.EnemyIds)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            campaignRulesBuilder.RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = enemyId,
                MaxHp = 1,
                Fidelity = CombatRuleFidelity.Authoritative,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "wait",
                        Weight = 1,
                        Effects = new List<CombatSimulationEffectDefinition>()
                    }
                }
            });
        }
        campaignRulesBuilder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "attribute-card",
            Cost = 0,
            Exhaust = true,
            RequiresEnemyTarget = true,
            Fidelity = CombatRuleFidelity.Authoritative,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.GainBlock,
                    Target = CombatSimulationTarget.Self,
                    Amount = 5
                },
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.ModifyVariable,
                    Target = CombatSimulationTarget.Self,
                    DefinitionId = "StrengthUpperBound",
                    Amount = 5,
                    PersistAcrossBattles = true
                },
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Damage,
                    Target = CombatSimulationTarget.SelectedEnemy,
                    Amount = 5
                }
            }
        });
        campaignRulesBuilder.RegisterEnemy(new CombatEnemyDefinition
        {
            OwnerModId = "Tests",
            EnemyId = "attribute-dummy",
            MaxHp = 6,
            Fidelity = CombatRuleFidelity.Authoritative,
            Intents =
            {
                new CombatEnemyIntentDefinition
                {
                    IntentId = "wait",
                    Weight = 1
                }
            }
        });
        var campaignRules = campaignRulesBuilder.Freeze();
        var attributeResult = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "attribute-scaling",
                RulesetVersion = campaign.RulesetVersion,
                Seed = 1,
                Player = new CombatPlayerSetup
                {
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck = { "attribute-card" },
                    Variables =
                    {
                        ["Strength"] = 10,
                        ["Perceive"] = 5
                    }
                },
                Enemies =
                {
                    new CombatEnemySetup { EnemyId = "attribute-dummy" }
                },
                InitialDraw = 1,
                DrawPerTurn = 0,
                RequireAuthoritativeRules = true
            },
            campaignRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        Assert(attributeResult.Outcome == CombatSimulationOutcome.Victory
               && attributeResult.Metrics.DamageDealt == 6
               && attributeResult.Metrics.BlockGained == 6
               && attributeResult.PersistentVariableDeltas["StrengthUpperBound"] == 5,
            "campaign attributes use Witch scaling and explicitly project persistent cap effects");
        var moneyRulesBuilder = new CombatRulesetBuilder("money-rules");
        moneyRulesBuilder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "chip",
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
        });
        moneyRulesBuilder.RegisterEnemy(new CombatEnemyDefinition
        {
            OwnerModId = "Tests",
            EnemyId = "pickpocket",
            MaxHp = 2,
            Intents =
            {
                new CombatEnemyIntentDefinition
                {
                    IntentId = "steal-and-refund",
                    Weight = 1,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.ModifyVariable,
                            Target = CombatSimulationTarget.Player,
                            DefinitionId = "Money",
                            Amount = -15,
                            PersistAcrossBattles = true,
                            MinimumVariableValue = 0
                        },
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.DeferVariableUntilVictory,
                            Target = CombatSimulationTarget.Player,
                            DefinitionId = "Money",
                            Amount = 15,
                            PersistAcrossBattles = true,
                            MinimumVariableValue = 0
                        }
                    }
                }
            }
        });
        var moneyRules = moneyRulesBuilder.Freeze();
        var moneyResult = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "money-clamp-and-refund",
                RulesetVersion = "money-rules",
                Seed = 7,
                Player = new CombatPlayerSetup
                {
                    MaxHp = 20,
                    CurrentHp = 20,
                    Deck = { "chip" },
                    Variables = { ["Money"] = 10 }
                },
                Enemies = { new CombatEnemySetup { EnemyId = "pickpocket" } },
                InitialDraw = 1,
                DrawPerTurn = 1,
                RequireAuthoritativeRules = true
            },
            moneyRules.Ruleset,
            new GreedyCombatSimulationPolicy());
        var changedMoneyState = moneyResult.FinalState.Clone();
        changedMoneyState.Player!.Variables["Money"]++;
        Assert(moneyRules.Success
               && moneyResult.Outcome == CombatSimulationOutcome.Victory
               && moneyResult.FinalState.Player!.Variables["Money"] == 15d
               && moneyResult.PersistentVariableDeltas["Money"] == 5
               && moneyResult.FinalState.DeferredVictoryVariableChanges.Count == 0
               && CombatBattleStateHasher.Hash(moneyResult.FinalState)
                  != CombatBattleStateHasher.Hash(changedMoneyState),
            "combat money clamps theft to the available balance, refunds on victory, persists the actual delta, and participates in state identity");
        var phaseTruthRules = new CombatRulesetBuilder("phase-truth-v1")
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "phase-life",
                MaximumStacks = 3,
                DecayAtRoundEnd = false
            })
            .RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "Tests",
                StatusId = "buff_rotten",
                MaximumStacks = 1,
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "rotten-action",
                        EventKind = CombatSimulationEventKind.ActionResolved,
                        OwnerRelation =
                            CombatStatusTriggerOwnerRelation.EventSource,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.SetBlock,
                                Target = CombatSimulationTarget.Self,
                                Amount = 0
                            }
                        }
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "phase-down",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.AddStatus,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "phase-life",
                        Amount = -1
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "phase-up",
                Cost = 0,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.AddStatus,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "phase-life",
                        Amount = 1
                    }
                }
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "rotten-guard",
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
                CardId = "filtered-strike",
                Cost = 0,
                RequiresEnemyTarget = true,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Damage,
                        Target = CombatSimulationTarget.SelectedEnemy,
                        Amount = 10
                    }
                }
            })
            .Freeze();
        CombatBattleState BuildPhaseTruthState(
            string cardId,
            int phaseStacks = 0)
        {
            var state = new CombatBattleState
            {
                PlayerActorId = 1,
                Phase = CombatSimulationPhase.PlayerAction,
                NextActorId = 3,
                NextCardInstanceId = 2,
                Actors =
                {
                    new CombatActorState
                    {
                        ActorId = 1,
                        InstanceKey = "player",
                        Kind = CombatSimulationActorKind.Player,
                        Hp = 20,
                        MaxHp = 20,
                        Energy = 3,
                        BaseEnergy = 3
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
                    new CombatCardInstanceState
                    {
                        InstanceId = 1,
                        CardId = cardId
                    }
                },
                Hand = { 1 }
            };
            if (phaseStacks > 0)
            {
                state.Player!.Statuses.Add(new CombatStatusState
                {
                    StatusId = "phase-life",
                    Stacks = phaseStacks
                });
            }
            return state;
        }
        var phaseTruthScenario = new CombatScenarioDefinition
        {
            ScenarioId = "phase-truth",
            RulesetVersion = "phase-truth-v1",
            Player = new CombatPlayerSetup
            {
                RoleId = "Tests",
                MaxHp = 20,
                CurrentHp = 20,
                Deck = { "phase-down" }
            },
            Enemies = { new CombatEnemySetup { EnemyId = "unused" } }
        };
        var phaseDecrement = new CombatSimulationEngine().ForkAndApplyPlayerAction(
            phaseTruthScenario,
            phaseTruthRules.Ruleset,
            BuildPhaseTruthState("phase-down", 3),
            new CombatSimulationAction
            {
                CandidateId = "card:1",
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 1,
                DefinitionId = "phase-down"
            });
        var phaseRemoval = new CombatSimulationEngine().ForkAndApplyPlayerAction(
            phaseTruthScenario,
            phaseTruthRules.Ruleset,
            BuildPhaseTruthState("phase-down", 1),
            new CombatSimulationAction
            {
                CandidateId = "card:1",
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 1,
                DefinitionId = "phase-down"
            });
        var phaseAtMaximum = new CombatSimulationEngine().ForkAndApplyPlayerAction(
            phaseTruthScenario,
            phaseTruthRules.Ruleset,
            BuildPhaseTruthState("phase-up", 3),
            new CombatSimulationAction
            {
                CandidateId = "card:1",
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 1,
                DefinitionId = "phase-up"
            });
        var rottenState = BuildPhaseTruthState("rotten-guard");
        rottenState.Player!.Statuses.Add(new CombatStatusState
        {
            StatusId = "buff_rotten",
            Stacks = 1
        });
        var rottenGuard = new CombatSimulationEngine().ForkAndApplyPlayerAction(
            phaseTruthScenario,
            phaseTruthRules.Ruleset,
            rottenState,
            new CombatSimulationAction
            {
                CandidateId = "card:1",
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 1,
                DefinitionId = "rotten-guard"
            });
        var filteredState = BuildPhaseTruthState("filtered-strike");
        filteredState.FindActor(2)!.Variables["DamageFilter.Normal"] = 60d;
        var filteredStrike = new CombatSimulationEngine().ForkAndApplyPlayerAction(
            phaseTruthScenario,
            phaseTruthRules.Ruleset,
            filteredState,
            new CombatSimulationAction
            {
                CandidateId = "card:1:target:2",
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 1,
                TargetActorId = 2,
                DefinitionId = "filtered-strike"
            });
        var phaseLockedState = BuildPhaseTruthState("filtered-strike");
        phaseLockedState.FindActor(2)!.Variables["MaxChangeHp"] = 0d;
        var phaseLockedStrike = new CombatSimulationEngine().ForkAndApplyPlayerAction(
            phaseTruthScenario,
            phaseTruthRules.Ruleset,
            phaseLockedState,
            new CombatSimulationAction
            {
                CandidateId = "card:1:target:2",
                Kind = CombatSimulationActionKind.PlayCard,
                ActorId = 1,
                CardInstanceId = 1,
                TargetActorId = 2,
                DefinitionId = "filtered-strike"
            });
        var rottenProjection = CombatSemanticAuditor.ProjectEffective(
            rottenState,
            new CombatSimulationAction
            {
                ActorId = 1,
                CardInstanceId = 1,
                DefinitionId = "rotten-guard"
            },
            new CombatActionSemantics { Defend = 5d });
        var filteredProjection = CombatSemanticAuditor.ProjectEffective(
            filteredState,
            new CombatSimulationAction
            {
                ActorId = 1,
                CardInstanceId = 1,
                TargetActorId = 2,
                DefinitionId = "filtered-strike"
            },
            new CombatActionSemantics { Damage = 10d });
        Assert(phaseDecrement.Success
               && phaseDecrement.State.Player!.Statuses.Single(
                   item => item.StatusId == "phase-life").Stacks == 2
               && phaseRemoval.Success
               && phaseRemoval.State.Player!.Statuses.All(
                   item => item.StatusId != "phase-life")
               && phaseRemoval.Events.Any(item =>
                   item.Kind == CombatSimulationEventKind.StatusRemoved
                   && item.DefinitionId == "phase-life")
               && phaseAtMaximum.Success
               && phaseAtMaximum.State.Player!.Statuses.Single(
                   item => item.StatusId == "phase-life").Stacks == 3
               && phaseAtMaximum.Events.All(item =>
                   item.Kind != CombatSimulationEventKind.StatusAdded
                   || item.DefinitionId != "phase-life")
               && rottenGuard.Success
               && rottenGuard.State.Player!.Block == 0
               && filteredStrike.Success
               && filteredStrike.State.FindActor(2)!.Hp == 16
               && phaseLockedStrike.Success
               && phaseLockedStrike.State.FindActor(2)!.Hp == 20
               && rottenProjection.IntrinsicDefend == 5d
               && rottenProjection.Defend == 0d
               && filteredProjection.Damage == 4d,
            "signed status deltas consume phase lives, capped statuses emit no false level change, corrosion clears newly gained block after the action, and typed filters plus phase limits constrain authoritative damage");
        var unsafeFinaleState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                CurrentHp = 100,
                MaxHp = 100
            },
            CurrentPower = 5,
            MaxPower = 5,
            HandCount = 5,
            HandCardIds =
            {
                "Crowdfundingcard_43",
                "engine_a",
                "engine_b",
                "engine_c",
                "engine_d"
            },
            DeckCardIds =
            {
                "engine_a",
                "engine_b",
                "engine_c",
                "engine_d",
                "engine_e",
                "engine_f"
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "finale",
                    SourceId = "Crowdfundingcard_43",
                    Cost = 3
                }
            }
        };
        CombatArchetypePolicy.Enrich(unsafeFinaleState);
        var unsafeFinaleLegal = CombatArchetypePolicy.IsLegal(
            unsafeFinaleState,
            unsafeFinaleState.Actions[0],
            out var unsafeFinaleReason);
        var protectedFinaleState =
            CombatPlayerObservationBoundary.Normalize(unsafeFinaleState);
        protectedFinaleState.Player.Statuses.Add(new CombatStatusObservation
        {
            StatusId = "buff_chrysalis",
            Level = 1
        });
        CombatArchetypePolicy.Enrich(protectedFinaleState);
        var protectedFinaleLegal = CombatArchetypePolicy.IsLegal(
            protectedFinaleState,
            protectedFinaleState.Actions[0],
            out _);
        var rottenPolicyState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                CurrentHp = 20,
                MaxHp = 20,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_rotten",
                        Level = 1
                    }
                }
            }
        };
        var rottenPolicyLegal = CombatArchetypePolicy.IsLegal(
            rottenPolicyState,
            new CombatActionObservation
            {
                SourceId = "card_2",
                Semantics = new CombatActionSemantics { Defend = 5d }
            },
            out var rottenPolicyReason);
        Assert(!unsafeFinaleLegal
               && unsafeFinaleReason.Contains("Solar", StringComparison.Ordinal)
               && protectedFinaleLegal
               && !rottenPolicyLegal
               && rottenPolicyReason.Contains("corrosion", StringComparison.Ordinal),
            "high-risk starter and corrosion legality gates reject unsupported Finale and block-only actions while admitting a protected coherent launch");
        var concentratedDeckDefinition = new CombatCampaignDefinition
        {
            CampaignId = "deck-concentration",
            TargetDeckSizeMinimum = 15,
            TargetDeckSizeMaximum = 21,
            Player = new CombatPlayerSetup
            {
                Deck =
                {
                    "card_1", "card_1", "card_1",
                    "card_2", "card_2", "card_2", "card_2"
                }
            }
        };
        var reserveAcquisitionDefinition = new CombatCampaignDefinition
        {
            AllowSkipCardReward = false,
            TargetDeckSizeMinimum = 1,
            TargetDeckSizeMaximum = 2,
            Rewards =
            {
                new CombatCampaignRewardDefinition
                {
                    RewardId = "reward-to-reserve",
                    Kind = CombatCampaignRewardKind.Card,
                    RewardCardPackId = "cardpack_1",
                    BaseValue = 5d
                }
            }
        };
        var reserveAcquisitionState = new CombatCampaignState
        {
            Deck = { "fixed-starter" },
            AttributeUpperBounds =
            {
                ["Strength"] = 40,
                ["Lucky"] = 20,
                ["Perceive"] = 20,
                ["Wisdom"] = 39
            }
        };
        var reserveAcquisitionDecision = CombatCampaignRewardSelector.Apply(
            reserveAcquisitionDefinition,
            new CombatCampaignPlannedEncounter
            {
                EncounterId = "normal-reward",
                EndsLayer = false,
                RewardOffer = new CombatCampaignRewardOffer
                {
                    CardRounds =
                    {
                        new List<string> { "reward-to-reserve" }
                    }
                }
            },
            reserveAcquisitionState);
        Assert(reserveAcquisitionDecision.Cards.Single().SelectedId
               == "reward-to-reserve"
               && reserveAcquisitionState.Deck.SequenceEqual(
                   new[] { "fixed-starter" })
               && reserveAcquisitionState.ReserveCards.SequenceEqual(
                   new[] { "reward-to-reserve" })
               && !reserveAcquisitionDecision.DeckAdjustment.Applied,
            "card rewards enter reserve without mutating the fixed active deck before a layer-end adjustment");
        var concentratedDeckState = new CombatCampaignState
        {
            CurrentLayer = 3,
            CurrentHp = 80,
            MaxHp = 100,
            Deck =
            {
                "card_1", "card_1", "card_1",
                "card_2", "card_2", "card_2", "card_2",
                "engine_1", "engine_2", "engine_3", "engine_4",
                "engine_5", "engine_6", "engine_7", "engine_8",
                "engine_9", "engine_10", "engine_11", "engine_12",
                "engine_13", "engine_14", "engine_15"
            }
        };
        foreach (var cardId in concentratedDeckState.Deck.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            concentratedDeckDefinition.Rewards.Add(new CombatCampaignRewardDefinition
            {
                RewardId = cardId,
                Kind = CombatCampaignRewardKind.Card,
                RewardCardPackId = "cardpack_1",
                BaseValue = cardId.StartsWith("engine_", StringComparison.Ordinal)
                    ? 5d
                    : 0d
            });
        }
        var concentratedDeckDecision = CombatCampaignRewardSelector.Apply(
            concentratedDeckDefinition,
            new CombatCampaignPlannedEncounter
            {
                EncounterId = "layer-3-end",
                LayerNumber = 3,
                EndsLayer = true
            },
            concentratedDeckState);
        Assert(concentratedDeckDecision.RemovedCardIds.Count == 0
               && concentratedDeckDecision.DeckAdjustment.Applied
               && concentratedDeckDecision.DeckAdjustment.MovedToReserveIds.Count >= 1
               && concentratedDeckDecision.DeckAdjustment.MovedToReserveIds.All(id =>
                   id == "card_1" || id == "card_2")
               && concentratedDeckState.Deck.Count is >= 15 and <= 21
               && concentratedDeckState.Deck.Count
                  + concentratedDeckState.ReserveCards.Count == 22,
            "layer-end adjustment concentrates the active deck within its tendency interval, moves weak base cards to reserve, and does not delete ownership");
        var resurrectionRules = new CombatRulesetBuilder("resurrection-settlement-v1")
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "wait-resurrection",
                Cost = 1,
                Fidelity = CombatRuleFidelity.Authoritative
            })
            .RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "Tests",
                CardId = "escape-resurrection",
                Cost = 1,
                Fidelity = CombatRuleFidelity.Authoritative,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.WinBattle,
                        Target = CombatSimulationTarget.Self
                    }
                }
            })
            .RegisterEnemy(new CombatEnemyDefinition
            {
                OwnerModId = "Tests",
                EnemyId = "lethal-resurrection-enemy",
                MaxHp = 100,
                Fidelity = CombatRuleFidelity.Authoritative,
                Intents =
                {
                    new CombatEnemyIntentDefinition
                    {
                        IntentId = "lethal",
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.Damage,
                                Target = CombatSimulationTarget.Player,
                                Amount = 20
                            }
                        }
                    }
                }
            })
            .Freeze();
        var resurrectedPlayerResult = new CombatSimulationEngine(
                new TestResurrectionExtensionFactory())
            .Run(
                new CombatScenarioDefinition
                {
                    ScenarioId = "player-resurrection-settlement",
                    RulesetVersion = "resurrection-settlement-v1",
                    Player = new CombatPlayerSetup
                    {
                        MaxHp = 10,
                        CurrentHp = 10,
                        BaseEnergy = 1,
                        Deck = { "wait-resurrection" }
                    },
                    Enemies =
                    {
                        new CombatEnemySetup
                        {
                            EnemyId = "lethal-resurrection-enemy"
                        }
                    },
                    InitialDraw = 1,
                    DrawPerTurn = 0,
                    Limits = new CombatSimulationLimits
                    {
                        MaximumTurns = 1,
                        MaximumActions = 5
                    }
                },
                resurrectionRules.Ruleset,
                FirstLegalCombatSimulationPolicy.Instance);
        var explicitVictoryResult = new CombatSimulationEngine().Run(
            new CombatScenarioDefinition
            {
                ScenarioId = "explicit-victory-settlement",
                RulesetVersion = "resurrection-settlement-v1",
                Player = new CombatPlayerSetup
                {
                    MaxHp = 10,
                    CurrentHp = 10,
                    BaseEnergy = 1,
                    Deck = { "escape-resurrection" }
                },
                Enemies =
                {
                    new CombatEnemySetup
                    {
                        EnemyId = "lethal-resurrection-enemy"
                    }
                },
                InitialDraw = 1,
                DrawPerTurn = 0
            },
            resurrectionRules.Ruleset,
            FirstLegalCombatSimulationPolicy.Instance);
        var lateEscapeResult = new CombatSimulationEngine(
                new TestLateEscapeExtensionFactory())
            .Run(
                new CombatScenarioDefinition
                {
                    ScenarioId = "late-escape-settlement",
                    RulesetVersion = "resurrection-settlement-v1",
                    Player = new CombatPlayerSetup
                    {
                        MaxHp = 10,
                        CurrentHp = 10,
                        BaseEnergy = 1,
                        Deck = { "wait-resurrection" }
                    },
                    Enemies =
                    {
                        new CombatEnemySetup
                        {
                            EnemyId = "lethal-resurrection-enemy"
                        }
                    },
                    InitialDraw = 1,
                    DrawPerTurn = 0,
                    Limits = new CombatSimulationLimits
                    {
                        MaximumTurns = 2,
                        MaximumActions = 5
                    }
                },
                resurrectionRules.Ruleset,
                FirstLegalCombatSimulationPolicy.Instance);
        Assert(resurrectedPlayerResult.Outcome == CombatSimulationOutcome.Draw
               && resurrectedPlayerResult.FinalPlayerHp == 5
               && resurrectedPlayerResult.TerminalConsistencyValid
               && explicitVictoryResult.Outcome == CombatSimulationOutcome.Victory
               && explicitVictoryResult.ExplicitRuleTermination
               && explicitVictoryResult.TerminalConsistencyValid
               && explicitVictoryResult.FinalState.LivingEnemies.Any(),
            "physical outcome waits for resurrection settlement while explicit rule termination may end with living enemies");
        var lateEndedEvent = lateEscapeResult.Events.Last(item =>
            item.Kind == CombatSimulationEventKind.BattleEnded);
        Assert(lateEscapeResult.Outcome == CombatSimulationOutcome.Victory
               && lateEscapeResult.TerminalResolution
               == CombatTerminalResolution.ResurrectionEscapeOverride
               && lateEscapeResult.InitialTerminalOutcome
               == CombatSimulationOutcome.Defeat
               && lateEscapeResult.InitialTerminalPlayerHp == 0
               && lateEscapeResult.FinalPlayerHp == 5
               && lateEscapeResult.ExplicitRuleTermination
               && lateEscapeResult.TerminalConsistencyValid
               && lateEscapeResult.Metrics.RuleTerminalOverrides == 1
               && lateEscapeResult.FailureDiagnostics.TerminalOutcome == "Defeat"
               && lateEscapeResult.FailureDiagnostics.RecentEvents.Count > 0
               && lateEndedEvent.Amount == (int)CombatSimulationOutcome.Victory
               && lateEndedEvent.Message
               == CombatTerminalResolution.ResurrectionEscapeOverride.ToString(),
            "late resurrection escape explicitly overrides only a physical defeat and preserves the original terminal snapshot");
        var overflowState = new CombatCampaignState
        {
            Attributes = { ["Strength"] = 40 },
            LayerBaseAttributes = { ["Strength"] = 40 },
            PermanentAttributeBonuses = { ["Strength"] = 5 },
            AttributeUpperBounds = { ["Strength"] = 40 }
        };
        CombatCampaignRewardSelector.ClampAttributes(overflowState);
        overflowState.AttributeUpperBounds["Strength"] = 45;
        CombatCampaignRewardSelector.ClampAttributes(overflowState);
        Assert(overflowState.Attributes["Strength"] == 40
               && overflowState.PermanentAttributeBonuses["Strength"] == 0,
            "attribute overflow is discarded and does not return after a later cap increase");
        var mechanicBuildDefinition = new CombatCampaignDefinition
        {
            CampaignId = "mechanic-build-test",
            Rewards =
            {
                new CombatCampaignRewardDefinition
                {
                    RewardId = "Crowdfundingcard_6",
                    Kind = CombatCampaignRewardKind.Card
                },
                new CombatCampaignRewardDefinition
                {
                    RewardId = "Crowdfundingcard_8",
                    Kind = CombatCampaignRewardKind.Card
                },
                new CombatCampaignRewardDefinition
                {
                    RewardId = "Crowdfundingcard_10",
                    Kind = CombatCampaignRewardKind.Card
                },
                new CombatCampaignRewardDefinition
                {
                    RewardId = "Crowdfundingcard_11",
                    Kind = CombatCampaignRewardKind.Card
                },
                new CombatCampaignRewardDefinition
                {
                    RewardId = "luckycard_4",
                    Kind = CombatCampaignRewardKind.Card,
                    BaseValue = 50d,
                    Features = { ["risk"] = 1d }
                },
                new CombatCampaignRewardDefinition
                {
                    RewardId = "safe-card",
                    Kind = CombatCampaignRewardKind.Card,
                    BaseValue = 1d,
                    Features = { ["defense"] = 1d }
                }
            }
        };
        var mechanicBuildState = new CombatCampaignState
        {
            CurrentLayer = 3,
            MaxHp = 100,
            CurrentHp = 100,
            Deck =
            {
                "Crowdfundingcard_6",
                "Crowdfundingcard_8",
                "Crowdfundingcard_10",
                "Crowdfundingcard_11"
            }
        };
        var boundedResidualDefinition = new CombatCampaignDefinition
        {
            RewardScoreResidualMaximumAbsolute = 0.20d,
            RewardScoreResiduals =
            {
                ["residual-positive"] = 10d,
                ["residual-negative"] = -10d
            },
            Rewards =
            {
                new CombatCampaignRewardDefinition
                {
                    RewardId = "residual-positive",
                    Kind = CombatCampaignRewardKind.Card,
                    BaseValue = 1d
                },
                new CombatCampaignRewardDefinition
                {
                    RewardId = "residual-negative",
                    Kind = CombatCampaignRewardKind.Card,
                    BaseValue = 1d
                }
            }
        };
        var boundedResidualScores = CombatCampaignRewardSelector.ScoreRewards(
            boundedResidualDefinition,
            new CombatCampaignState
            {
                CurrentLayer = 1,
                MaxHp = 100,
                CurrentHp = 100
            },
            new[] { "residual-negative", "residual-positive" },
            boundedResidualDefinition.Rewards.ToDictionary(
                item => item.RewardId,
                StringComparer.OrdinalIgnoreCase),
            0,
            CombatCampaignRewardKind.Card);
        Assert(boundedResidualScores[0].RewardId == "residual-positive"
               && boundedResidualScores[0].LearnedResidual == 0.20d
               && boundedResidualScores[1].LearnedResidual == -0.20d,
            "campaign reward scoring applies learned residuals without exceeding the configured safety bound");
        var configuredRelicBiasDefinition = new CombatCampaignDefinition
        {
            RewardScoreResidualMaximumAbsolute = 0.20d,
            RewardScoreResiduals = { ["relic-keep"] = 0.10d },
            RewardScoreBiasMaximumAbsolute = 8d,
            RewardScoreBiases = { ["relic-avoid"] = -4d },
            Rewards =
            {
                new CombatCampaignRewardDefinition
                {
                    RewardId = "relic-keep",
                    Kind = CombatCampaignRewardKind.Relic,
                    BaseValue = 1d,
                    OfferWeight = 1d
                },
                new CombatCampaignRewardDefinition
                {
                    RewardId = "relic-avoid",
                    Kind = CombatCampaignRewardKind.Relic,
                    BaseValue = 1d,
                    OfferWeight = 0d
                }
            }
        };
        var configuredRelicBiasScores = CombatCampaignRewardSelector.ScoreRewards(
            configuredRelicBiasDefinition,
            new CombatCampaignState
            {
                CurrentLayer = 1,
                MaxHp = 100,
                CurrentHp = 100
            },
            new[] { "relic-avoid", "relic-keep" },
            configuredRelicBiasDefinition.Rewards.ToDictionary(
                item => item.RewardId,
                StringComparer.OrdinalIgnoreCase),
            0,
            CombatCampaignRewardKind.Relic);
        var weightedRelicPick = CombatCampaignWorldPlanner.PickWeightedUnused(
            configuredRelicBiasDefinition.Rewards,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            123UL,
            "relic-offer-test",
            0);
        Assert(configuredRelicBiasScores[0].RewardId == "relic-keep"
               && configuredRelicBiasScores[0].LearnedResidual == 0.10d
               && configuredRelicBiasScores[1].ConfiguredBias == -4d
               && weightedRelicPick == "relic-keep",
            "configured relic avoidance lowers both deterministic offer probability and reward selection score");
        var mechanicPlan = CombatCampaignRewardSelector.RefreshBuildPlan(
            mechanicBuildDefinition,
            mechanicBuildState);
        var mechanicRewardLookup = mechanicBuildDefinition.Rewards.ToDictionary(
            item => item.RewardId,
            StringComparer.OrdinalIgnoreCase);
        var mechanicRewardScores = CombatCampaignRewardSelector.ScoreRewards(
            mechanicBuildDefinition,
            mechanicBuildState,
            new[] { "luckycard_4", "safe-card" },
            mechanicRewardLookup,
            12,
            CombatCampaignRewardKind.Card);
        Assert(mechanicPlan.PrimaryArchetype == "rebirth"
               && mechanicRewardScores.Single(item =>
                   item.RewardId == "luckycard_4").RiskPenalty >= 100d
               && mechanicRewardScores[0].RewardId == "safe-card",
            "campaign planning recognizes mechanic archetypes and hard-demotes curse alchemy despite an inflated base value");
        var publicCampaignContext = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        CombatCampaignContextFeatureNames.ProjectScenario(
            new CombatScenarioDefinition
            {
                Player = new CombatPlayerSetup
                {
                    Variables =
                    {
                        [CombatCampaignPublicContextKeys.BattleIndex] = 8d,
                        [CombatCampaignPublicContextKeys.TotalBattles] = 37d,
                        [CombatCampaignPublicContextKeys.RemainingBattles] = 28d,
                        [CombatCampaignPublicContextKeys.Progress] = 8d / 36d,
                        [CombatCampaignPublicContextKeys.LayerNumber] = 2d,
                        [CombatCampaignPublicContextKeys.TotalLayers] = 4d,
                        [CombatCampaignPublicContextKeys.EncounterKind] = 1d,
                        [CombatCampaignPublicContextKeys.GameLevel] = 9d,
                        [CombatCampaignPublicContextKeys.FinalBoss] = 0d
                    }
                }
            },
            publicCampaignContext);
        Assert(publicCampaignContext.GetValueOrDefault(
                   CombatCampaignContextFeatureNames.ContextKnown) == 1d
               && publicCampaignContext.GetValueOrDefault(
                   CombatCampaignContextFeatureNames.BattleIndex) == 8d
               && publicCampaignContext.GetValueOrDefault(
                   CombatCampaignContextFeatureNames.Progress) == 8d / 36d
               && publicCampaignContext.GetValueOrDefault(
                   CombatCampaignContextFeatureNames.FinalBoss) == 0d,
            "campaign context projection exposes current public progress without future encounter or reward data");
        var roleSpecificBuildDefinition = new CombatCampaignDefinition
        {
            RolePrior = { ["doom-growth"] = 1d },
            BuildTendency = { ["doom-growth"] = 0.5d },
            Rewards =
            {
                new CombatCampaignRewardDefinition
                {
                    RewardId = "doom-growth-card",
                    Kind = CombatCampaignRewardKind.Card,
                    Features = { ["doom-growth"] = 1d }
                }
            }
        };
        var roleSpecificPlan = CombatCampaignRewardSelector.RefreshBuildPlan(
            roleSpecificBuildDefinition,
            new CombatCampaignState
            {
                CurrentLayer = 1,
                Deck = { "doom-growth-card" }
            });
        Assert(roleSpecificPlan.PrimaryArchetype == "doom-growth",
            "campaign build planning accepts role-owned archetypes declared through public campaign priors");
        var campaignPair = new CombatCampaignRunner().RunPaired(
            campaign,
            "normal",
            700UL,
            campaignRules.Ruleset,
            new GreedyCombatSimulationPolicyFactory(),
            new GreedyCombatSimulationPolicyFactory());
        Assert(campaignPair.Baseline.CampaignVictory
               && campaignPair.Learned.CampaignVictory
               && campaignPair.Baseline.CompletedBattles == 37
               && campaignPair.Baseline.Rewards.Count == 36
               && campaignPair.Baseline.FinalState.MaxHp == 260
               && campaignPair.Baseline.FinalState.CurrentHp == 260
               && campaignPair.Baseline.FinalState.Attributes["Strength"] == 40
               && campaignPair.Baseline.FinalState.Attributes["Wisdom"] == 39
               && campaignPair.Baseline.FinalState.Attributes["Lucky"] == 20
               && campaignPair.Baseline.FinalState.Money == 100
               && campaignPair.Baseline.FinalState.Relics.Count == 6
               && campaignPair.Baseline.FinalState.Deck.Count <= 35
               && campaignPair.Baseline.FinalState.BuildPlan.PrimaryArchetype == "burst"
               && campaignPair.Baseline.FinalState.BuildPlan.SynergySources["card"]
                  == campaignPair.Baseline.FinalState.Deck.Count
               && campaignPair.Baseline.FinalState.BuildPlan.SynergySources["relic"]
                  == campaignPair.Baseline.FinalState.Relics.Count
               && campaignPair.Baseline.Rewards.All(item =>
                   item.BuildPlan.TargetDeckSizeMaximum
                   == campaign.TargetDeckSizeMaximum),
            "campaign runner carries full state, applies the configured deck-size tendency, and records the build plan separately from battle policy");
        var encounterStarts = new List<CombatCampaignCheckpoint>();
        var encounterPlan = CombatCampaignWorldPlanner.Build(
            campaign,
            "normal",
            701UL);
        _ = new CombatCampaignRunner().RunMonitoredWithEncounterStarts(
            campaign,
            encounterPlan,
            campaignRules.Ruleset,
            new GreedyCombatSimulationPolicyFactory(),
            battleProgress: null,
            encounterStart: checkpoint => encounterStarts.Add(checkpoint));
        var localEncounterCheckpoint = encounterStarts[10];
        localEncounterCheckpoint.Battles.Clear();
        localEncounterCheckpoint.Rewards.Clear();
        localEncounterCheckpoint.Completed = false;
        localEncounterCheckpoint.State.Blessings.RemoveAll(item =>
            string.Equals(
                item,
                "blessing_105",
                StringComparison.OrdinalIgnoreCase));
        var localEncounterResult = new CombatCampaignRunner().RunMonitoredSegment(
            campaign,
            encounterPlan,
            campaignRules.Ruleset,
            new GreedyCombatSimulationPolicyFactory(),
            localEncounterCheckpoint,
            maximumEncounters: 1,
            battleProgress: null);
        Assert(localEncounterResult.Battles.Count == 1
               && localEncounterResult.Battles[0].ScenarioId.Contains(
                   ":10:",
                   StringComparison.Ordinal)
               && localEncounterResult.Checkpoint.NextEncounterIndex == 11
               && localEncounterResult.FinalState.Blessings.Contains(
                   "blessing_105",
                   StringComparer.OrdinalIgnoreCase),
            "campaign resume repairs origin threshold blessings before replaying one failed encounter");
        Assert(campaignRules.Ruleset.TryGetCardCore("strike", out var projectedStrike),
            "foundation fixture resolves the starter attack definition");
        projectedStrike!.Fidelity = CombatRuleFidelity.Authoritative;
        context.Campaign = campaign;
        context.CampaignRules = campaignRules;
    }
}
