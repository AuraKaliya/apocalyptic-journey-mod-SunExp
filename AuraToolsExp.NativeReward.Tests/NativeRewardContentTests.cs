using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraToolsExp.Dll.Features.AutoBattle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class NativeRewardTestSuite
{
    internal static IEnumerable<string> ValidateSkillTimingCatalog(
        string path,
        CombatGameSubjectCatalog subjectCatalog)
    {
        var failures = new List<string>();
        if (!File.Exists(path))
        {
            failures.Add("skill-timing-catalog: bundled catalog is missing");
            return failures;
        }

        var root = JObject.Parse(File.ReadAllText(path));
        var entries = (root["skills"] as JArray)?.OfType<JObject>().ToList()
                      ?? new List<JObject>();
        var expected = subjectCatalog.Roles
            .SelectMany(role => role.SkillCooldownTurns)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);
        var actualIds = entries
            .Select(entry => (string?)entry["skillId"] ?? "")
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if ((int?)root["schemaVersion"] != 1
            || (string?)root["gameBuild"] != subjectCatalog.GameBuild
            || entries.Count != 17
            || actualIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 17
            || expected.Count != 17
            || expected.Keys.Any(id => !actualIds.Contains(
                id,
                StringComparer.OrdinalIgnoreCase))
            || entries.Any(entry =>
                string.IsNullOrWhiteSpace((string?)entry["evaluator"])
                || string.IsNullOrWhiteSpace((string?)entry["choicePolicy"])
                || (entry["roleIds"] as JArray)?.Count is not > 0)
            || expected.Any(pair =>
                !AuraToolsWitchSkillTimingProvider.Cooldowns.TryGetValue(
                    pair.Key,
                    out var cooldown)
                || cooldown != pair.Value))
        {
            failures.Add(
                "skill-timing-catalog: catalog, subject cooldowns, and runtime provider diverged");
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateNanaStatusDerivedMaximumHp(
        CombatGameSubjectCatalog catalog,
        CombatCampaignDefinition campaignTemplate,
        CombatRuleset ruleset)
    {
        var failures = new List<string>();
        var subject = new CombatGameSubjectPreset
        {
            Id = "nana-status-derived-maximum-hp",
            RoleId = "career_2",
            PartnerId = "Partner_10003"
        };
        catalog.ResolveReferences(subject);
        var campaign = JsonConvert.DeserializeObject<CombatCampaignDefinition>(
                           JsonConvert.SerializeObject(campaignTemplate))
                       ?? new CombatCampaignDefinition();
        CombatGameSubjectPresetRuntime.Apply(subject, campaign);
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "nana-status-derived-maximum-hp",
            Player = campaign.Player,
            CampaignVariables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["DoomPower"] = "1"
            }
        };
        scenario.RewardRules.Add(new CombatScenarioRewardRule
        {
            RewardId = "blessing_40",
            Kind = "Blessing",
            Stacks = 1
        });
        var context = new NativePoolTestContext(scenario, ruleset);
        context.State.PlayerActorId = 1;
        var actor = new CombatActorState
        {
            ActorId = 1,
            DefinitionId = "career_2",
            Kind = CombatSimulationActorKind.Player,
            Hp = 61,
            MaxHp = 61,
            Statuses =
            {
                new CombatStatusState
                {
                    StatusId = "buff_DoomPower",
                    Stacks = 1,
                    SourceActorId = 1
                }
            }
        };
        context.State.Actors.Add(actor);
        var extension = new AuraToolsNativeRewardExtension();
        extension.Initialize(context);
        var initialContributionCorrect = actor.MaxHp == 61;
        actor.Statuses.Single(status => string.Equals(
            status.StatusId,
            "buff_DoomPower",
            StringComparison.OrdinalIgnoreCase)).Stacks = 2;
        extension.OnEvent(context, new CombatSimulationEvent
        {
            Kind = CombatSimulationEventKind.StatusAdded,
            SourceActorId = 1,
            TargetActorId = 1,
            DefinitionId = "buff_DoomPower",
            Amount = 1
        });
        var firstGrowthCorrect = actor.MaxHp == 63 && actor.Hp == 63;
        actor.Statuses.Single(status => string.Equals(
            status.StatusId,
            "buff_DoomPower",
            StringComparison.OrdinalIgnoreCase)).Stacks = 4;
        extension.OnEvent(context, new CombatSimulationEvent
        {
            Kind = CombatSimulationEventKind.StatusAdded,
            SourceActorId = 1,
            TargetActorId = 1,
            DefinitionId = "buff_DoomPower",
            Amount = 2
        });
        var secondGrowthCorrect = actor.MaxHp == 67 && actor.Hp == 67;
        var persistentGrowthCorrect =
            context.PersistentVariableDeltas.GetValueOrDefault("MaxHp") == 6;
        var globals = new NativeRewardScriptGlobals(
            context,
            new CombatScenarioRewardRule
            {
                RewardId = "career_2",
                Kind = "Role",
                NativeScriptHash = campaign.Player.RoleNativeScriptHash,
                FightScript = campaign.Player.RoleFightScript
            });
        globals.SetStatus("Self");
        globals.ChangeCareer("career_4");
        var transformedCorrect = actor.DefinitionId == "career_4"
                                 && actor.MaxHp == 67
                                 && actor.Hp == 67;
        actor.Statuses.Add(new CombatStatusState
        {
            StatusId = "SpecialBuff_CalamityIncarnates",
            Stacks = 1,
            SourceActorId = 1
        });
        context.State.Actors.Add(new CombatActorState
        {
            ActorId = 2,
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 100,
            MaxHp = 100
        });
        context.State.Actors.Add(new CombatActorState
        {
            ActorId = 3,
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 100,
            MaxHp = 100
        });
        extension.OnEvent(context, new CombatSimulationEvent
        {
            Kind = CombatSimulationEventKind.StatusAdded,
            SourceActorId = 1,
            TargetActorId = 1,
            DefinitionId = "SpecialBuff_CalamityIncarnates",
            Amount = 1
        });
        context.AppliedEffects.Clear();
        extension.OnEvent(context, new CombatSimulationEvent
        {
            Kind = CombatSimulationEventKind.ActionResolved,
            SourceActorId = 1,
            TargetActorId = 2,
            DefinitionId = "fixture-action"
        });
        var calamityDamageCorrect = context.AppliedEffects.Count(item =>
            item.Effect.Kind == CombatSimulationEffectKind.Damage
            && item.Effect.Amount == 1) == 2;
        actor.Hp = 42;
        actor.Statuses.RemoveAll(status => string.Equals(
            status.StatusId,
            "SpecialBuff_CalamityIncarnates",
            StringComparison.OrdinalIgnoreCase));
        extension.OnEvent(context, new CombatSimulationEvent
        {
            Kind = CombatSimulationEventKind.StatusRemoved,
            SourceActorId = 1,
            TargetActorId = 1,
            DefinitionId = "SpecialBuff_CalamityIncarnates",
            Amount = 1
        });
        var restoredCorrect = actor.DefinitionId == "career_2"
                              && actor.MaxHp == 67
                              && actor.Hp == 42;
        var nextScenario = new CombatScenarioDefinition
        {
            ScenarioId = "nana-doom-next-battle",
            Player = scenario.Player,
            CampaignVariables = new Dictionary<string, string>(
                scenario.CampaignVariables,
                StringComparer.OrdinalIgnoreCase)
        };
        var nextContext = new NativePoolTestContext(nextScenario, ruleset);
        nextContext.State.PlayerActorId = 1;
        nextContext.State.Actors.Add(new CombatActorState
        {
            ActorId = 1,
            DefinitionId = "career_2",
            Kind = CombatSimulationActorKind.Player,
            Hp = 42,
            MaxHp = 67,
            Statuses =
            {
                new CombatStatusState
                {
                    StatusId = "buff_DoomPower",
                    Stacks = 4,
                    SourceActorId = 1
                }
            }
        });
        new AuraToolsNativeRewardExtension().Initialize(nextContext);
        var adventurePersistenceCorrect =
            nextScenario.CampaignVariables.GetValueOrDefault("DoomPower") == "4"
            && nextContext.State.Player?.MaxHp == 67
            && nextContext.State.Player?.Hp == 42
            && nextContext.PersistentVariableDeltas.Count == 0;
        if (!initialContributionCorrect
            || !firstGrowthCorrect
            || !secondGrowthCorrect
            || !persistentGrowthCorrect
            || !transformedCorrect
            || !calamityDamageCorrect
            || !restoredCorrect
            || !adventurePersistenceCorrect)
        {
            failures.Add(
                "nana-status-derived-maximum-hp: initial="
                + initialContributionCorrect
                + ", firstGrowth="
                + firstGrowthCorrect
                + ", secondGrowth="
                + secondGrowthCorrect
                + ", persistentGrowth="
                + persistentGrowthCorrect
                + ", transformed="
                + transformedCorrect
                + ", calamityDamage="
                + calamityDamageCorrect
                + ", restored="
                + restoredCorrect
                + ", adventurePersistence="
                + adventurePersistenceCorrect
                + ", hp="
                + actor.Hp
                + ", maxHp="
                + actor.MaxHp);
        }
        if (!ruleset.TryGetCard("careercard_2", out var devour)
            || devour.RequiresEnemyTarget
            || devour.TargetScope != CombatCardTargetScope.AnyActor)
        {
            failures.Add("nana-devour-target-scope-definition");
        }
        else
        {
            var targetState = new CombatBattleState
            {
                Phase = CombatSimulationPhase.PlayerAction,
                PlayerActorId = 1,
                Actors =
                {
                    actor,
                    new CombatActorState
                    {
                        ActorId = 2,
                        Kind = CombatSimulationActorKind.Friendly,
                        Hp = 20,
                        MaxHp = 20
                    },
                    new CombatActorState
                    {
                        ActorId = 3,
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
                        CardId = "careercard_2"
                    }
                },
                SkillCards = { 1 },
                SkillCooldowns = { [1] = 0 }
            };
            var targetActions = new CombatSimulationEngine()
                .GetInvocablePlayerActions(scenario, ruleset, targetState)
                .Where(action => action.DefinitionId == "careercard_2")
                .ToList();
            var targetIds = targetActions
                .Select(action => action.TargetActorId)
                .OrderBy(id => id)
                .ToArray();
            if (!targetIds.SequenceEqual(new[] { 1, 2, 3 }))
            {
                failures.Add(
                    "nana-devour-target-scope-actions:"
                    + string.Join(",", targetIds));
            }
            if (!scenario.RewardRules.Any(item => string.Equals(
                    item.RewardId,
                    "blessing_40",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Kind,
                    "Blessing",
                    StringComparison.OrdinalIgnoreCase)))
            {
                scenario.RewardRules.Add(new CombatScenarioRewardRule
                {
                    RewardId = "blessing_40",
                    Kind = "Blessing",
                    Stacks = 1
                });
            }
            var observation = PlayerEquivalentSimulationObservationProjector.Project(
                new CombatSimulationPolicyContext
                {
                    Scenario = scenario,
                    Ruleset = ruleset,
                    State = targetState,
                    LegalActions = targetActions
                });
            var projectedKinds = observation.Actions
                .OrderBy(action => action.TargetRuntimeId)
                .Select(action => action.TargetKind)
                .ToArray();
            var friendlyFeatures = CombatDecisionEngine.BuildFeatures(
                observation,
                observation.Actions.Single(action =>
                    action.TargetKind == CombatTargetKind.Friendly));
            if (!projectedKinds.SequenceEqual(new[]
                {
                    CombatTargetKind.Self,
                    CombatTargetKind.Friendly,
                    CombatTargetKind.Enemy
                })
                || observation.Friendlies.Count != 1
                 || observation.Features.GetValueOrDefault(
                     "playerRole:career_2") != 1d
                 || observation.Features.GetValueOrDefault(
                     "blessing:blessing_40") != 1d
                 || friendlyFeatures.GetValueOrDefault(
                    "targetKindFriendly") != 1d)
            {
                failures.Add(
                    "nana-devour-target-observation:"
                    + string.Join(",", projectedKinds)
                    + ":friendlies="
                    + observation.Friendlies.Count
                    + ":role="
                    + observation.Features.GetValueOrDefault(
                        "playerRole:career_2")
                    + ":nightmare="
                    + observation.Features.GetValueOrDefault(
                        "blessing:blessing_40"));
            }
        }
        var transformState = new CombatBattleState
        {
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    InstanceKey = "nana-transform-player",
                    DefinitionId = "career_2",
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 61,
                    MaxHp = 61,
                    Energy = 3,
                    BaseEnergy = 3
                },
                new CombatActorState
                {
                    ActorId = 2,
                    InstanceKey = "nana-transform-enemy",
                    DefinitionId = "enemy_1",
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 100,
                    MaxHp = 100
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = "careercard_3",
                    CreationSource = "role-skill",
                    CreationSourceId = "career_2"
                }
            },
            SkillCards = { 1 },
            SkillCooldowns = { [1] = 0 }
        };
        var transformEngine = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory());
        var transformAction = transformEngine.GetInvocablePlayerActions(
                scenario,
                ruleset,
                transformState)
            .Single(action => string.Equals(
                action.DefinitionId,
                "careercard_3",
                StringComparison.OrdinalIgnoreCase));
        var transformResult = transformEngine.ForkAndApplyPlayerAction(
            scenario,
            ruleset,
            transformState,
            transformAction,
            allowPolicyIneligible: true);
        if (!transformResult.Success
            || transformResult.State.SkillCooldowns.GetValueOrDefault(1) != 2
            || !string.Equals(
                transformResult.State.Player?.DefinitionId,
                "career_4",
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                "nana-native-transform-cooldown: success="
                + transformResult.Success
                + ", cooldown="
                + transformResult.State.SkillCooldowns.GetValueOrDefault(1)
                + ", role="
                + transformResult.State.Player?.DefinitionId);
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateNightmarePrototypeDuplication(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        var failures = new List<string>();
        var blessing = campaign.Rewards.Single(item => string.Equals(
            item.RewardId,
            "blessing_40",
            StringComparison.OrdinalIgnoreCase));
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "nightmare-prototype-one-layer",
            Player = new CombatPlayerSetup
            {
                RoleId = "career_2"
            },
            RewardRules =
            {
                new CombatScenarioRewardRule
                {
                    RewardId = blessing.RewardId,
                    Kind = blessing.Kind.ToString(),
                    NativeScriptHash = blessing.NativeScriptHash,
                    FightScript = blessing.FightScript
                }
            }
        };
        var context = new NativePoolTestContext(scenario, ruleset)
        {
            RandomValue = 99
        };
        context.State.PlayerActorId = 1;
        context.State.Actors.Add(new CombatActorState
        {
            ActorId = 1,
            InstanceKey = "nightmare-player",
            DefinitionId = "career_2",
            Kind = CombatSimulationActorKind.Player,
            Hp = 60,
            MaxHp = 60
        });
        context.State.Actors.Add(new CombatActorState
        {
            ActorId = 2,
            InstanceKey = "nightmare-enemy",
            DefinitionId = "enemy_1",
            Kind = CombatSimulationActorKind.Enemy,
            Hp = 100,
            MaxHp = 100
        });
        var negativeStatus = ruleset.SnapshotStatuses().First(status =>
            status.Tags.Contains("Negative", StringComparer.OrdinalIgnoreCase));
        var extension = new AuraToolsNativeRewardExtension();
        extension.Initialize(context);
        extension.OnEvent(context, new CombatSimulationEvent
        {
            Kind = CombatSimulationEventKind.StatusAdded,
            SourceActorId = 1,
            TargetActorId = 2,
            DefinitionId = negativeStatus.StatusId,
            Amount = 3,
            SourceRewardId = "fixture-debuff-card",
            SourceActionId = 1
        });
        var duplicate = context.AppliedEffects.SingleOrDefault();
        var duplicatedOneLayer = duplicate.Effect != null
                                 && duplicate.Effect.Kind
                                 == CombatSimulationEffectKind.AddStatus
                                 && duplicate.Effect.Amount == 1
                                 && string.Equals(
                                     duplicate.Effect.DefinitionId,
                                     negativeStatus.StatusId,
                                     StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(
                                     duplicate.SourceEvent?.SourceRewardId,
                                     "blessing_40",
                                     StringComparison.OrdinalIgnoreCase);
        if (duplicate.SourceEvent != null)
        {
            extension.OnEvent(context, new CombatSimulationEvent
            {
                Kind = CombatSimulationEventKind.StatusAdded,
                SourceActorId = 1,
                TargetActorId = 2,
                DefinitionId = negativeStatus.StatusId,
                Amount = 1,
                SourceRewardId = duplicate.SourceEvent.SourceRewardId,
                SourceActionId = 1
            });
        }
        if (!duplicatedOneLayer || context.AppliedEffects.Count != 1)
        {
            failures.Add(
                "nightmare-prototype-one-layer: duplicated="
                + duplicatedOneLayer
                + ", effects="
                + context.AppliedEffects.Count);
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateAdelaWholeHandTransform(
        CombatRuleset ruleset)
    {
        var failures = new List<string>();
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "adela-whole-hand-transform",
            Player = new CombatPlayerSetup
            {
                RoleId = "career_3",
                SkillCardIds = { "careercard_4" },
                SkillCooldownTurns = { ["careercard_4"] = 12 }
            },
            CampaignVariables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Soul"] = "100"
            },
            HandLimit = 10
        };
        var state = new CombatBattleState
        {
            Turn = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            NextCardInstanceId = 4,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    DefinitionId = "career_3",
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 95,
                    MaxHp = 95,
                    Energy = 3
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = "careercard_4",
                    CreationSource = "role-skill",
                    CreationSourceId = "career_3"
                },
                new CombatCardInstanceState
                {
                    InstanceId = 2,
                    CardId = "card_1",
                    CreationSource = "fixture",
                    EnchantmentIds = { "fixture-enhancement" },
                    Variables = { ["fixture"] = "old" }
                },
                new CombatCardInstanceState
                {
                    InstanceId = 3,
                    CardId = "card_2",
                    CreationSource = "fixture"
                }
            },
            SkillCards = { 1 },
            SkillCooldowns = { [1] = 0 },
            Hand = { 2, 3 }
        };
        var engine = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory());
        var skill = engine.GetInvocablePlayerActions(scenario, ruleset, state)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        var applied = engine.ForkAndApplyPlayerAction(
            scenario,
            ruleset,
            state,
            skill,
            allowPolicyIneligible: true);
        var transformed = applied.State.Hand
            .Select(applied.State.FindCard)
            .Where(item => item != null)
            .Select(item => item!)
            .ToList();
        if (!applied.Success
            || transformed.Count != 2
            || transformed.Any(item => !string.Equals(
                item.CardId,
                "nocard_2",
                StringComparison.OrdinalIgnoreCase))
            || transformed.Any(item => item.EnchantmentIds.Count != 0)
            || transformed.Any(item => !item.Tags.Contains(
                "Burnout",
                StringComparer.OrdinalIgnoreCase))
            || transformed.Any(item => !item.Tags.Contains(
                "Retain",
                StringComparer.OrdinalIgnoreCase))
            || applied.State.SkillCooldowns.GetValueOrDefault(1) != 12
            || scenario.CampaignVariables.GetValueOrDefault("Soul") != "100")
        {
            failures.Add(
                "adela-whole-hand-transform:success="
                + applied.Success
                + ", hand="
                + string.Join(",", transformed.Select(item => item.CardId))
                + ", cooldown="
                + applied.State.SkillCooldowns.GetValueOrDefault(1));
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateDivineChoiceActionContract(
        CombatRuleset ruleset)
    {
        var failures = new List<string>();
        if (!ruleset.TryGetCard("careercard_1", out var divineChoice)
            || divineChoice.RequiresEnemyTarget
            || divineChoice.ActionContract == null
            || divineChoice.ActionContract.Version
               != CombatActionContractProtocol.Version
            || divineChoice.VerificationSource
               != "Decompiler:v1.0.24591395")
        {
            failures.Add(
                "divine-choice-contract: bundled semantic contract is missing or invalid");
            return failures;
        }

        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "divine-choice-contract",
            HandLimit = 1,
            Player = new CombatPlayerSetup
            {
                RoleId = "career_1",
                SkillCardIds = { "careercard_1" },
                SkillCooldownTurns = { ["careercard_1"] = 1 }
            }
        };
        CombatBattleState State(bool drawCard, bool fullHand)
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
                        CardId = "careercard_1",
                        CreationSource = "role-skill",
                        CreationSourceId = "career_1"
                    }
                },
                SkillCards = { 1 },
                SkillCooldowns = { [1] = 0 },
                NextCardInstanceId = 2
            };
            if (drawCard)
            {
                state.Cards.Add(new CombatCardInstanceState
                {
                    InstanceId = 2,
                    CardId = "card_1",
                    CreationSource = "fixture"
                });
                state.DrawPile.Add(2);
                state.NextCardInstanceId = 3;
            }
            if (fullHand)
            {
                state.Cards.Add(new CombatCardInstanceState
                {
                    InstanceId = 3,
                    CardId = "card_1",
                    CreationSource = "fixture"
                });
                state.Hand.Add(3);
                state.NextCardInstanceId = 4;
            }
            return state;
        }

        var engine = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory());
        var emptyDraw = State(drawCard: false, fullHand: false);
        var emptyDrawSkill = engine.GetInvocablePlayerActions(
                scenario,
                ruleset,
                emptyDraw)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        var forcedEmptyDraw = engine.ForkAndApplyPlayerAction(
            scenario,
            ruleset,
            emptyDraw,
            emptyDrawSkill,
            allowPolicyIneligible: true);
        if (emptyDrawSkill.PolicyEligible
            || emptyDrawSkill.ExpectedOutcome
               != CombatActionApplicationOutcome.NoEffect
            || !forcedEmptyDraw.Success
            || forcedEmptyDraw.Outcome
               != CombatActionApplicationOutcome.NoEffect
            || forcedEmptyDraw.State.DrawPile.Count != 0
            || forcedEmptyDraw.State.Hand.Count != 0
            || forcedEmptyDraw.State.SkillCooldowns[1] != 0)
        {
            failures.Add(
                "divine-choice-contract: empty draw pile must remain a no-effect, no-cooldown invocation");
        }

        var fullHand = State(drawCard: true, fullHand: true);
        var fullHandSkill = engine.GetInvocablePlayerActions(
                scenario,
                ruleset,
                fullHand)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        if (fullHandSkill.PolicyEligible
            || fullHandSkill.ExpectedOutcome
               != CombatActionApplicationOutcome.NoEffect)
        {
            failures.Add(
                "divine-choice-contract: full hand must be excluded from policy candidates");
        }

        var applicable = State(drawCard: true, fullHand: false);
        var applicableSkill = engine.GetLegalPlayerActions(
                scenario,
                ruleset,
                applicable)
            .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
        var applied = engine.ForkAndApplyPlayerAction(
            scenario,
            ruleset,
            applicable,
            applicableSkill);
        if (!applied.Success
            || applied.Outcome != CombatActionApplicationOutcome.Applied
            || !applied.State.Hand.SequenceEqual(new[] { 2 })
            || applied.State.DrawPile.Count != 0
            || applied.State.SkillCooldowns[1] != 1)
        {
            failures.Add(
                "divine-choice-contract: successful selection must move one draw-pile card to hand and start cooldown");
        }

        Console.WriteLine("Divine Choice action contract checks: 3 cases.");
        return failures;
    }

    internal static IEnumerable<string> ValidateFinalBossAuthority(CombatRuleset ruleset)
    {
        var failures = new List<string>();
        if (!ruleset.TryGetEnemy("enemy_10007", out var lostWitch)
            || lostWitch.Intents.Any(item =>
                item.IntentId.StartsWith("enemycard_HJE_", StringComparison.Ordinal)))
        {
            failures.Add(
                "final-boss-authority: HJE dynamic intents leaked into enemy_10007");
        }
        var requiredHjeIntents = new[]
        {
            "enemycard_HJE_Judgment",
            "enemycard_HJE_Dawn",
            "enemycard_HJE_HolyMachine"
        };
        if (!ruleset.TryGetEnemy("enemy_10055", out var hje)
            || requiredHjeIntents.Any(expected =>
                hje.Intents.All(item =>
                    !string.Equals(
                        item.IntentId,
                        expected,
                        StringComparison.Ordinal))))
        {
            failures.Add(
                "final-boss-authority: enemy_10055 is missing one or more dynamic fate intents");
        }
        if (!ruleset.TryGetStatus(
                "SpecialBuff_CAR_Deadline",
                out var deadline)
            || !deadline.Metadata.TryGetValue(
                "NativeApplyScript",
                out var deadlineScript)
            || deadlineScript.IndexOf(
                "Damage(Object[0].MaxHp.ToString(),\"True\")",
                StringComparison.Ordinal) < 0
            || deadlineScript.IndexOf(
                "ChangeHp((-Object[0].MaxHp)",
                StringComparison.Ordinal) >= 0)
        {
            failures.Add(
                "final-boss-authority: Caroline deadline must execute max-HP true damage, not direct HP loss");
        }
        Console.WriteLine("Final boss authority checks: 3 cases.");
        return failures;
    }

    internal static IEnumerable<string> ValidateHardAffixes(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy)
    {
        var failures = new List<string>();
        var advanced = campaign.Difficulties.FirstOrDefault(item =>
            string.Equals(
                item.DifficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase));
        var expectedAffixes = new[] { "Hard_5", "Hard_7", "Hard_8", "Hard_13" };
        foreach (var affixId in expectedAffixes)
        {
            if (advanced?.HardAffixes.Any(item =>
                    item.Implemented
                    && string.Equals(
                        item.AffixId,
                        affixId,
                        StringComparison.OrdinalIgnoreCase)) != true)
            {
                failures.Add("affix-definition:" + affixId);
            }
        }
        foreach (var statusId in new[]
                 {
                     "buff_elements",
                     "buff_elementalBody",
                     "SpecialBuff_Restrain",
                     "SpecialBuff_Irritable",
                     "SpecialBuff_Hysteresis"
                 })
        {
            var status = ruleset.SnapshotStatuses().FirstOrDefault(item =>
                string.Equals(
                    item.StatusId,
                    statusId,
                    StringComparison.OrdinalIgnoreCase));
            if (status?.Fidelity != CombatRuleFidelity.Authoritative)
            {
                failures.Add("affix-status:" + statusId);
            }
        }
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "hard-affix-smoke",
            RulesetVersion = ruleset.Version,
            Seed = 772025UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                SkillCardIds = new List<string>(
                    campaign.Player.SkillCardIds),
                MaxHp = 10000,
                CurrentHp = 10000,
                BaseEnergy = 0,
                Deck = new List<string> { campaign.Player.Deck[0] },
                Variables = new Dictionary<string, double>
                {
                    ["Difficulty"] = 5,
                    ["EncounterKind"] = (int)CombatCampaignEncounterKind.Elite
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = enemy.EnemyId,
                    InstanceKey = "hard-low",
                    HpScale = 1d
                },
                new CombatEnemySetup
                {
                    EnemyId = enemy.EnemyId,
                    InstanceKey = "hard-high",
                    HpScale = 2d
                }
            },
            InitialDraw = 1,
            DrawPerTurn = 1,
            HandLimit = 10,
            RequireAuthoritativeRules = false,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 10,
                MaximumCommands = 10000
            }
        };
        var result = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(scenario, ruleset, new EndTurnPolicy());
        var enemies = result.FinalState.Actors
            .Where(item => item.Kind == CombatSimulationActorKind.Enemy)
            .ToList();
        if (enemies.Count != 2
            || enemies.Any(item => item.Statuses.FirstOrDefault(status =>
                   status.StatusId == "buff_elements")?.Stacks != 4)
            || enemies.Any(item => item.Statuses.All(status =>
                   status.StatusId != "buff_elementalBody")))
        {
            failures.Add("affix-runtime:Hard_7/Hard_13");
        }
        var traits = new HashSet<string>(
            new[]
            {
                "SpecialBuff_Restrain",
                "SpecialBuff_Irritable",
                "SpecialBuff_Hysteresis"
            },
            StringComparer.OrdinalIgnoreCase);
        var traitOwners = enemies.Where(item =>
                item.Statuses.Any(status => traits.Contains(status.StatusId)))
            .ToList();
        if (traitOwners.Count != 1
            || !string.Equals(
                traitOwners[0].InstanceKey,
                "hard-high",
                StringComparison.Ordinal))
        {
            failures.Add("affix-runtime:Hard_8");
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateProgressionSemantics(
        CombatCampaignDefinition campaign)
    {
        var failures = new List<string>();
        CombatCampaignState NewState()
        {
            var state = new CombatCampaignState
            {
                WorldSeed = 772025UL,
                MaxHp = campaign.Player.MaxHp,
                CurrentHp = campaign.Player.CurrentHp,
                Money = campaign.InitialMoney,
                Deck = new List<string>(campaign.Player.Deck),
                CurrentGameLevel = 1
            };
            foreach (var attribute in campaign.AttributeIds)
            {
                state.Attributes[attribute] = 0;
                state.LayerBaseAttributes[attribute] = 0;
                state.PermanentAttributeBonuses[attribute] = 0;
                state.AttributeUpperBounds[attribute] = 100;
            }
            return state;
        }

        CombatCampaignRewardDecision Acquire(
            CombatCampaignState state,
            string relicId,
            int index)
        {
            return CombatCampaignRewardSelector.Apply(
                campaign,
                new CombatCampaignPlannedEncounter
                {
                    Index = index,
                    EncounterId = "progression-" + index,
                    RewardOffer = new CombatCampaignRewardOffer
                    {
                        RelicId = relicId
                    }
                },
                state);
        }

        var replacementState = NewState();
        var replacement = Acquire(
            replacementState,
            "CrowdFundingRelic_29",
            1);
        if (string.IsNullOrWhiteSpace(replacement.Relic.ResolvedId)
            || string.Equals(
                replacement.Relic.ResolvedId,
                "CrowdFundingRelic_29",
                StringComparison.OrdinalIgnoreCase)
            || !replacementState.Relics.Contains(
                replacement.Relic.ResolvedId,
                StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("progression:random-relic-replacement");
        }

        var oneTimeState = NewState();
        Acquire(oneTimeState, "CrowdFundingRelic_12", 2);
        var cardCount = oneTimeState.Deck.Count(item =>
            string.Equals(
                item,
                "Crowdfundingcard_13",
                StringComparison.OrdinalIgnoreCase));
        var blessingCount = oneTimeState.Blessings.Count(item =>
            string.Equals(
                item,
                "CrowdfundingBlessing_8",
                StringComparison.OrdinalIgnoreCase));
        oneTimeState.Relics.RemoveAll(item => string.Equals(
            item,
            "CrowdFundingRelic_12",
            StringComparison.OrdinalIgnoreCase));
        Acquire(oneTimeState, "CrowdFundingRelic_12", 3);
        if (oneTimeState.SpecialVariables.GetValueOrDefault(
                "CrowdFundingRelic12First",
                "") != "1"
            || oneTimeState.Deck.Count(item => string.Equals(
                item,
                "Crowdfundingcard_13",
                StringComparison.OrdinalIgnoreCase)) != cardCount
            || oneTimeState.Blessings.Count(item => string.Equals(
                item,
                "CrowdfundingBlessing_8",
                StringComparison.OrdinalIgnoreCase)) != blessingCount)
        {
            failures.Add("progression:one-time-own-script");
        }

        var removalState = NewState();
        removalState.Deck.AddRange(new[]
        {
            "card_1", "card_2", "card_3", "card_4"
        });
        var beforeRemoval = removalState.Deck.Count;
        Acquire(removalState, "CrowdFundingRelic_60", 4);
        if (removalState.Deck.Count != beforeRemoval - 4)
        {
            failures.Add("progression:random-card-removal");
        }

        var moonSetState = NewState();
        Acquire(moonSetState, "CrowdFundingRelic_64", 5);
        Acquire(moonSetState, "CrowdFundingRelic_66", 6);
        if (moonSetState.Relics.Contains(
                "CrowdFundingRelic_69",
                StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("progression:moon-relic-set-triggered-early");
        }
        Acquire(moonSetState, "CrowdFundingRelic_67", 7);
        var moonParts = new[]
        {
            "CrowdFundingRelic_64",
            "CrowdFundingRelic_66",
            "CrowdFundingRelic_67"
        };
        if (!moonSetState.Relics.Contains(
                "CrowdFundingRelic_69",
                StringComparer.OrdinalIgnoreCase)
            || moonParts.Any(part => moonSetState.Relics.Contains(
                part,
                StringComparer.OrdinalIgnoreCase)))
        {
            failures.Add("progression:moon-relic-set-transformation");
        }
        return failures;
    }

}
