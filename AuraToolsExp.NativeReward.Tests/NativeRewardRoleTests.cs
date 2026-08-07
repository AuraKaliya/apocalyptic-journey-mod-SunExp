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
    internal static void ValidateAuthoritativeRoleProgram(
        string roleId,
        string partnerId,
        string skillCardId,
        int startingCooldown,
        CombatSimulationEventKind eventKind,
        int expectedCooldown,
        CombatGameSubjectCatalog catalog,
        CombatCampaignDefinition campaignTemplate,
        CombatRuleset ruleset,
        ICollection<string> failures)
    {
        var subject = new CombatGameSubjectPreset
        {
            Id = "native-role-test-" + roleId,
            RoleId = roleId,
            PartnerId = partnerId
        };
        catalog.ResolveReferences(subject);
        var campaign = JsonConvert.DeserializeObject<CombatCampaignDefinition>(
                           JsonConvert.SerializeObject(campaignTemplate))
                       ?? new CombatCampaignDefinition();
        CombatGameSubjectPresetRuntime.Apply(subject, campaign);
        var audit = AuraToolsNativeProgramPackageAudit.Validate(campaign, ruleset);
        if (!audit.Success)
        {
            failures.Add(
                "native-role-package:" + roleId + ":" + string.Join("|", audit.Errors));
            return;
        }
        if (campaign.Player.InitialStatuses.Count != 0)
        {
            failures.Add(
                "native-role-initialization:" + roleId
                + ": declarative statuses would duplicate the native role script");
            return;
        }

        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "native-role-event-" + roleId,
            Player = campaign.Player,
            CampaignVariables = campaign.Player.Variables.ToDictionary(
                item => item.Key,
                item => item.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase)
        };
        var context = new NativePoolTestContext(scenario, ruleset);
        context.State.PlayerActorId = 1;
        context.State.Actors.Add(new CombatActorState
        {
            ActorId = 1,
            DefinitionId = roleId,
            Kind = CombatSimulationActorKind.Player,
            Hp = campaign.Player.MaxHp,
            MaxHp = campaign.Player.MaxHp
        });
        context.State.Cards.Add(new CombatCardInstanceState
        {
            InstanceId = 1,
            CardId = skillCardId,
            CreationSource = "role-skill",
            CreationSourceId = roleId
        });
        context.State.SkillCards.Add(1);
        context.State.SkillCooldowns[1] = startingCooldown;
        var rule = new CombatScenarioRewardRule
        {
            RewardId = roleId,
            Kind = "Role",
            NativeScriptHash = campaign.Player.RoleNativeScriptHash,
            FightScript = campaign.Player.RoleFightScript
        };
        var globals = new NativeRewardScriptGlobals(context, rule);
        var execution = globals.RunScript(rule, null);
        if (!execution.Success)
        {
            failures.Add(
                "native-role-execution:" + roleId + ":" + execution.Message);
            return;
        }
        context.State.SkillCooldowns[1] = startingCooldown;
        globals.Dispatch(new CombatSimulationEvent
        {
            Kind = eventKind,
            SourceActorId = 1,
            TargetActorId = 1,
            DefinitionId = eventKind == CombatSimulationEventKind.CardDrawn
                ? "nocard_1"
                : roleId
        });
        if (context.State.SkillCooldowns[1] != expectedCooldown)
        {
            failures.Add(
                "native-role-cooldown:" + roleId
                + ": expected=" + expectedCooldown
                + ", actual=" + context.State.SkillCooldowns[1]);
        }
    }

    internal static IEnumerable<string> ValidateAuthoritativeRoleSkillSemantics()
    {
        var failures = new List<string>();
        AuraToolsAuthoritativeRoleSemantics.Initialize();
        failures.AddRange(
            AuraToolsAuthoritativeRoleSemantics
                .ValidateFrozenTrainingPreparation()
                .Select(error => "frozen-role-preparation:" + error));
        var coverageState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_1",
                Kind = CombatTargetKind.Self,
                CurrentHp = 70,
                MaxHp = 100,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_bleeding",
                        Level = 4,
                        Rarity = 2,
                        Type = "Negative"
                    },
                    new CombatStatusObservation
                    {
                        StatusId = "buff_DoomPower",
                        Level = 10,
                        Rarity = 4,
                        Type = "Special"
                    }
                }
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = 1,
            HandCards =
            {
                new CombatCardInstanceObservation
                {
                    RuntimeId = 10,
                    CardId = "fixture-core-card",
                    EffectiveCost = 1,
                    Retained = true,
                    EnhancementCount = 1
                }
            },
            DeckCardIds = { "fixture-core-card", "fixture-sacrifice-card" },
            DeckKnowledge = new CombatDeckKnowledge { DrawPileCount = 2 },
            Features =
            {
                ["handLimit"] = 10d,
                ["drawPileCount"] = 2d,
                [CombatCampaignContextFeatureNames.RemainingBattles] = 8d
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    DefinitionId = "fixture-enemy",
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 120,
                    MaxHp = 120,
                    Features = { ["actionCount"] = 2d },
                    Statuses =
                    {
                        new CombatStatusObservation
                        {
                            StatusId = "buff_bleeding",
                            Level = 8,
                            Rarity = 2,
                            Type = "Negative"
                        },
                        new CombatStatusObservation
                        {
                            StatusId = "buff_fixture_positive",
                            Level = 3,
                            Rarity = 2,
                            Type = "Positive"
                        }
                    }
                }
            },
            Threat = new CombatThreatForecast
            {
                CurrentIntentKnown = true,
                Intents =
                {
                    new CombatIntentObservation
                    {
                        SourceRuntimeId = 2,
                        Kind = CombatIntentKind.Attack,
                        BlockableDamage = 18d,
                        Probability = 1d,
                        Current = true
                    }
                }
            },
            ExpectedIncomingDamage = 18d
        };
        foreach (var pair in new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                 {
                     ["careercard_1"] = 1,
                     ["careercard_2"] = 5,
                     ["careercard_3"] = 2,
                     ["careercard_4"] = 12,
                     ["careercard_5"] = 3,
                     ["careercard_6"] = 3,
                     ["careercard_7"] = 2,
                     ["careercard_8"] = 5,
                     ["careercard_9"] = 2,
                     ["careercard_10"] = 2,
                     ["careercard_11"] = 1,
                     ["careercard_12"] = 2,
                     ["careercard_13"] = 99,
                     ["careercard_14"] = 2,
                     ["careercard_15"] = 5,
                     ["careercard_16"] = 1,
                     ["careercard_17"] = 2
                 })
        {
            var action = new CombatActionObservation
            {
                CandidateId = "timing-coverage-" + pair.Key,
                SourceId = pair.Key,
                Kind = CombatActionKind.UseSkill,
                TargetRuntimeId = pair.Key == "careercard_2" ? 1 : 2,
                TargetKind = pair.Key == "careercard_2"
                    ? CombatTargetKind.Self
                    : CombatTargetKind.Enemy
            };
            coverageState.Actions.Clear();
            coverageState.Actions.Add(action);
            CombatAiRegistry.ApplySemantics(coverageState, action);
            CombatAiRegistry.EnrichSkillTimings(coverageState);
            if (action.SemanticFidelity != CombatKnowledgeFidelity.Authoritative
                || action.Features.GetValueOrDefault(
                    CombatSkillTimingFeatureNames.Active) != 1d
                || action.Features.GetValueOrDefault(
                    CombatSkillTimingFeatureNames.CooldownAfterUse) != pair.Value
                || double.IsNaN(action.Features.GetValueOrDefault(
                    CombatSkillTimingFeatureNames.TimingAdvantage)))
            {
                failures.Add("base-skill-timing-coverage:" + pair.Key);
            }
        }
        var lowChoice = new CombatActionObservation
        {
            SourceId = "fixture-low-card",
            Semantics = new CombatActionSemantics { Damage = 1d },
            Features =
            {
                ["choice:cost"] = 0d,
                ["choice:rarity"] = 1d
            }
        };
        var highChoice = new CombatActionObservation
        {
            SourceId = "fixture-high-card",
            Semantics = new CombatActionSemantics
            {
                Damage = 18d,
                Draw = 2d,
                Scaling = 3d
            },
            Features =
            {
                ["choice:cost"] = 3d,
                ["choice:rarity"] = 4d
            }
        };
        foreach (var skillId in new[]
                 {
                     "careercard_1",
                     "careercard_9",
                     "careercard_12",
                     "careercard_13"
                 })
        {
            var skillAction = new CombatActionObservation
            {
                SourceId = skillId,
                Kind = CombatActionKind.UseSkill
            };
            AuraToolsWitchSkillInteraction.Prepare(coverageState, skillAction);
            var hint = CombatInteractionBroker.ConsumeNextHint(
                new CombatInteractionHint());
            var lowScore = 0d;
            var highScore = 0d;
            var hasLow = hint.ChoiceScorer?.TryScore(
                hint,
                lowChoice,
                out lowScore) == true;
            var hasHigh = hint.ChoiceScorer?.TryScore(
                hint,
                highChoice,
                out highScore) == true;
            var shouldPreferLow = skillId == "careercard_9";
            if (hint.SourceId != skillId
                || hint.ChoiceScorer == null
                || !hasLow
                || !hasHigh
                || (shouldPreferLow ? lowScore <= highScore : highScore <= lowScore))
            {
                failures.Add("base-skill-choice-policy:" + skillId);
            }
        }
        foreach (var test in new[]
                 {
                     (Soul: 99, Tier: 1, CardId: "nocard_1", Amount: 16d),
                     (Soul: 100, Tier: 2, CardId: "nocard_2", Amount: 18d),
                     (Soul: 199, Tier: 2, CardId: "nocard_2", Amount: 30d),
                     (Soul: 200, Tier: 3, CardId: "nocard_3", Amount: 33d)
                 })
        {
            var state = new CombatStateObservation
            {
                Player = new CombatUnitObservation
                {
                    RuntimeId = 1,
                    Kind = CombatTargetKind.Self,
                    CurrentHp = 95,
                    MaxHp = 95,
                    Statuses =
                    {
                        new CombatStatusObservation
                        {
                            StatusId = "buff_Soul",
                            Level = test.Soul
                        }
                    }
                },
                Enemies =
                {
                    new CombatUnitObservation
                    {
                        RuntimeId = 2,
                        Kind = CombatTargetKind.Enemy,
                        CurrentHp = 1000,
                        MaxHp = 1000
                    }
                }
            };
            var action = new CombatActionObservation
            {
                CandidateId = "royal-command-" + test.Soul,
                SourceId = "careercard_4",
                Kind = CombatActionKind.UseSkill
            };
            CombatAiRegistry.ApplySemantics(state, action);
            var transform = action.Semantics.HandTransform;
            if (action.SemanticFidelity != CombatKnowledgeFidelity.Authoritative
                || transform == null
                || transform.TargetTier != test.Tier
                || !string.Equals(
                    transform.TargetCardId,
                    test.CardId,
                    StringComparison.OrdinalIgnoreCase)
                || transform.TargetCardSemantics.Damage != test.Amount
                || transform.TargetCardSemantics.TrueDamage
                   != (test.Tier >= 2 ? test.Amount : 0d)
                || action.Semantics.Damage != 0d
                || !transform.TransformAllHandCards
                || !transform.PreserveInstances
                || !transform.ClearsEnhancements
                || !transform.TargetRetained
                || !transform.TargetExhaustsOnUse)
            {
                failures.Add(
                    "royal-command-semantics:soul=" + test.Soul);
            }
        }

        var nanaState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_2",
                Kind = CombatTargetKind.Self,
                CurrentHp = 70,
                MaxHp = 115,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_DoomPower",
                        Level = 10,
                        Type = "Special"
                    },
                    new CombatStatusObservation
                    {
                        StatusId = "buff_burn",
                        Level = 5,
                        Rarity = 2,
                        Type = "Negative"
                    }
                }
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    DefinitionId = "fixture-enemy",
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 50,
                    MaxHp = 50,
                    Statuses =
                    {
                        new CombatStatusObservation
                        {
                            StatusId = "buff_burn",
                            Level = 10,
                            Rarity = 1,
                            Type = "Negative"
                        }
                    }
                }
            }
        };
        var selfDevour = new CombatActionObservation
        {
            CandidateId = "nana-devour-self",
            SourceId = "careercard_2",
            Kind = CombatActionKind.UseSkill,
            TargetRuntimeId = 1,
            TargetKind = CombatTargetKind.Self
        };
        CombatAiRegistry.ApplySemantics(nanaState, selfDevour);
        if (selfDevour.SemanticFidelity != CombatKnowledgeFidelity.Authoritative
            || selfDevour.Semantics.Damage != 0d
            || selfDevour.Semantics.Cleanse != 5d
            || selfDevour.Semantics.Scaling != 2d
            || selfDevour.Semantics.Heal != 12d
            || selfDevour.Semantics.PersistentValue != 12d
            || selfDevour.Semantics.CooldownTurns != 5d
            || selfDevour.Semantics.StateChanges.GetValueOrDefault(
                "player.hp") != 12d
            || selfDevour.Semantics.TargetEffects.SingleOrDefault(effect =>
                   effect.Kind == CombatSemanticEffectKind.Heal)
               ?.TargetRuntimeId != 1
            || selfDevour.Features.GetValueOrDefault(
                "nana:projected-doom-gain") != 2d)
        {
            failures.Add("nana-devour-self-semantics");
        }
        var enemyDevour = new CombatActionObservation
        {
            CandidateId = "nana-devour-enemy",
            SourceId = "careercard_2",
            Kind = CombatActionKind.UseSkill,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy
        };
        CombatAiRegistry.ApplySemantics(nanaState, enemyDevour);
        if (enemyDevour.Semantics.Damage != 5d
            || enemyDevour.Semantics.Heal != 12d
            || enemyDevour.Semantics.Cleanse != 0d
            || enemyDevour.Semantics.Risk != 12d
            || enemyDevour.Semantics.TargetEffects.SingleOrDefault(effect =>
                   effect.Kind == CombatSemanticEffectKind.Heal)
               ?.TargetRuntimeId != 1
            || enemyDevour.Features.GetValueOrDefault(
                "nana:enemy-cleanse-cost") != 12d)
        {
            failures.Add("nana-devour-enemy-semantics");
        }
        var growthState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_2",
                Kind = CombatTargetKind.Self,
                CurrentHp = 880,
                MaxHp = 880,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_DoomPower",
                        Level = 40,
                        Type = "Special"
                    }
                }
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    DefinitionId = "growth-fixture-enemy",
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 100,
                    MaxHp = 100,
                    Statuses =
                    {
                        new CombatStatusObservation
                        {
                            StatusId = "buff_burn",
                            Level = 1,
                            Rarity = 1,
                            Type = "Negative"
                        }
                    }
                }
            },
            CurrentPower = 2,
            MaxPower = 3,
            ExpectedIncomingDamage = 0,
            Features =
            {
                [CombatCampaignContextFeatureNames.ContextKnown] = 1d,
                [CombatCampaignContextFeatureNames.Progress] = 0.25d,
                [CombatCampaignContextFeatureNames.FinalBoss] = 0d
            }
        };
        var growthDevour = new CombatActionObservation
        {
            CandidateId = "nana-growth-devour",
            SourceId = "careercard_2",
            Kind = CombatActionKind.UseSkill,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 0
        };
        var growthBuilder = new CombatActionObservation
        {
            CandidateId = "nana-growth-builder",
            SourceId = "burningcard_2",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 30,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics
            {
                Damage = 6d,
                Debuff = 2d
            }
        };
        growthState.Actions.Add(growthDevour);
        growthState.Actions.Add(growthBuilder);
        CombatAiRegistry.ApplySemantics(growthState, growthDevour);
        CombatAiRegistry.EnrichRoleStrategies(growthState);
        if (growthDevour.Features.GetValueOrDefault(
                "nana:projected-doom-gain") != 1d
            || growthDevour.Features.GetValueOrDefault(
                "nana:projected-max-hp-gain") != 41d
            || growthDevour.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d
            || growthBuilder.Features.GetValueOrDefault(
                "roleStrategy:nana.growth-builder") != 1d
            || growthState.Features.GetValueOrDefault(
                "roleStrategy:nana.safe-growth-window") != 1d
            || growthState.Features.GetValueOrDefault(
                "roleStrategy:nana.growth-target-doom") != 15d)
        {
            failures.Add("nana-safe-growth-window-sequencing");
        }
        growthState.Actions.Remove(growthBuilder);
        growthDevour.Features.Clear();
        CombatAiRegistry.ApplySemantics(growthState, growthDevour);
        CombatAiRegistry.EnrichRoleStrategies(growthState);
        if (growthDevour.Features.GetValueOrDefault(
                "nana:conservative-devour-target") != 0d
            || growthDevour.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
        {
            failures.Add("nana-single-kind-enemy-devour-conservative-gate");
        }
        growthState.Enemies[0].Statuses.Add(new CombatStatusObservation
        {
            StatusId = "buff_toxin",
            Level = 1,
            Rarity = 1,
            Type = "Negative"
        });
        growthDevour.Features.Clear();
        CombatAiRegistry.ApplySemantics(growthState, growthDevour);
        CombatAiRegistry.EnrichRoleStrategies(growthState);
        if (growthDevour.Features.GetValueOrDefault(
                "nana:projected-doom-gain") != 2d
            || growthDevour.Features.GetValueOrDefault(
                "nana:devour-event-max-hp-gain") != 42d
            || growthDevour.Features.GetValueOrDefault(
                "roleStrategy:nana.preferred-harvest") != 1d
            || growthDevour.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 0d
            || growthDevour.Features.GetValueOrDefault(
                "nana:devour-net-value") <= 0d)
        {
            failures.Add("nana-two-kind-enemy-devour-net-value");
        }
        var firstTransform = new CombatActionObservation
        {
            CandidateId = "nana-transform-first",
            SourceId = "careercard_3",
            Kind = CombatActionKind.UseSkill
        };
        CombatAiRegistry.ApplySemantics(nanaState, firstTransform);
        if (firstTransform.SemanticFidelity
            != CombatKnowledgeFidelity.Authoritative
            || firstTransform.Semantics.SelfHpLoss != 0d
            || firstTransform.Semantics.Damage != 2d
            || firstTransform.Semantics.Buff != 20d
            || firstTransform.Semantics.Scaling != 10d
            || firstTransform.Semantics.StateChanges.GetValueOrDefault(
                "playerMaxHp") != 0d
            || firstTransform.Features.GetValueOrDefault(
                "nana:first-transform") != 1d)
        {
            failures.Add("nana-first-transform-semantics");
        }
        nanaState.Player.DefinitionId = "career_4";
        nanaState.Player.CurrentHp = 51;
        nanaState.Player.MaxHp = 95;
        var repeatTransform = new CombatActionObservation
        {
            CandidateId = "nana-transform-repeat",
            SourceId = "careercard_3",
            Kind = CombatActionKind.UseSkill
        };
        CombatAiRegistry.ApplySemantics(nanaState, repeatTransform);
        if (repeatTransform.Semantics.SelfHpLoss != 0d
            || repeatTransform.Semantics.Damage != 1d
            || repeatTransform.Semantics.Buff != 0d
            || repeatTransform.Semantics.StateChanges.GetValueOrDefault(
                "playerMaxHp") != 0d
            || repeatTransform.Features.GetValueOrDefault(
                "nana:repeat-transform") != 1d)
        {
            failures.Add("nana-repeat-transform-semantics");
        }
        nanaState.Actions.Add(repeatTransform);
        CombatAiRegistry.EnrichRoleStrategies(nanaState);
        if (repeatTransform.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
        {
            failures.Add("nana-repeat-transform-strategy-gate");
        }
        nanaState.Player.DefinitionId = "transient-form";
        nanaState.Player.Statuses.Add(new CombatStatusObservation
        {
            StatusId = "SpecialBuff_CalamityIncarnates",
            Level = 1,
            Type = "Special"
        });
        repeatTransform.Features.Clear();
        CombatAiRegistry.ApplySemantics(nanaState, repeatTransform);
        CombatAiRegistry.EnrichRoleStrategies(nanaState);
        if (repeatTransform.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.Active) != 1d
            || repeatTransform.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
        {
            failures.Add("nana-calamity-status-preserves-role-identity");
        }

        var nightmareState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_2",
                Kind = CombatTargetKind.Self,
                CurrentHp = 100,
                MaxHp = 120,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_DoomPower",
                        Level = 10,
                        Type = "Special"
                    }
                }
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    DefinitionId = "nightmare-fixture-enemy",
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 80,
                    MaxHp = 80,
                    Statuses =
                    {
                        new CombatStatusObservation
                        {
                            StatusId = "buff_burn",
                            Level = 1,
                            Rarity = 2,
                            Type = "Negative"
                        },
                        new CombatStatusObservation
                        {
                            StatusId = "buff_toxin",
                            Level = 1,
                            Rarity = 1,
                            Type = "Negative"
                        }
                    }
                }
            },
            CurrentPower = 2,
            MaxPower = 3,
            Features =
            {
                ["blessing:blessing_40"] = 1d
            }
        };
        var nightmareBuilder = new CombatActionObservation
        {
            CandidateId = "nightmare-two-events",
            SourceId = "fixture-two-debuffs",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 30,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics
            {
                Debuff = 4d,
                TargetEffects =
                {
                    new CombatTargetedSemanticEffect
                    {
                        Kind = CombatSemanticEffectKind.AddStatus,
                        TargetRuntimeId = 2,
                        DefinitionId = "buff_burn",
                        RawAmount = 3d,
                        EffectiveAmount = 3d,
                        Probability = 1d
                    },
                    new CombatTargetedSemanticEffect
                    {
                        Kind = CombatSemanticEffectKind.AddStatus,
                        TargetRuntimeId = 2,
                        DefinitionId = "buff_toxin",
                        RawAmount = 1d,
                        EffectiveAmount = 1d,
                        Probability = 1d
                    }
                }
            }
        };
        var nightmareDevour = new CombatActionObservation
        {
            CandidateId = "nightmare-devour",
            SourceId = "careercard_2",
            Kind = CombatActionKind.UseSkill,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy
        };
        nightmareState.Actions.Add(nightmareBuilder);
        nightmareState.Actions.Add(nightmareDevour);
        CombatAiRegistry.ApplySemantics(nightmareState, nightmareDevour);
        CombatAiRegistry.EnrichRoleStrategies(nightmareState);
        if (nightmareBuilder.Features.GetValueOrDefault(
                "nightmare:eligible-negative-events") != 2d
            || Math.Abs(nightmareBuilder.Features.GetValueOrDefault(
                "nightmare:expected-extra-stacks") - 0.4d) > 0.000001d
            || Math.Abs(nightmareBuilder.Features.GetValueOrDefault(
                "nightmare:expected-devour-threshold-gain") - 0.2d) > 0.000001d
            || nightmareBuilder.Features.GetValueOrDefault(
                "roleStrategy:nana.priority-builder") != 1d
            || nightmareDevour.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
        {
            failures.Add(
                "nana-nightmare-event-and-threshold-strategy:events="
                + nightmareBuilder.Features.GetValueOrDefault(
                    "nightmare:eligible-negative-events")
                + ",extra="
                + nightmareBuilder.Features.GetValueOrDefault(
                    "nightmare:expected-extra-stacks")
                + ",threshold="
                + nightmareBuilder.Features.GetValueOrDefault(
                    "nightmare:expected-devour-threshold-gain")
                + ",priority="
                + nightmareBuilder.Features.GetValueOrDefault(
                    "roleStrategy:nana.priority-builder")
                + ",devourProhibited="
                + nightmareDevour.Features.GetValueOrDefault(
                    CombatRoleStrategyFeatureNames.StrategicallyProhibited));
        }
        nightmareBuilder.Semantics.RandomOutcome = true;
        nightmareBuilder.Semantics.Uncertainty = 0.5d;
        nightmareBuilder.Features.Clear();
        nightmareDevour.Features.Clear();
        CombatAiRegistry.ApplySemantics(nightmareState, nightmareDevour);
        CombatAiRegistry.EnrichRoleStrategies(nightmareState);
        if (nightmareDevour.Features.GetValueOrDefault(
                "roleStrategy:nana.defer-harvest-random-builder") != 1d
            || nightmareDevour.Features.GetValueOrDefault(
                "roleStrategy:nana.defer-harvest-same-turn") != 0d
            || nightmareDevour.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 0d)
        {
            failures.Add("nana-random-builder-remains-soft-guidance");
        }

        var burstState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_2",
                Kind = CombatTargetKind.Self,
                CurrentHp = 100,
                MaxHp = 120,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_DoomPower",
                        Level = 10,
                        Type = "Special"
                    }
                }
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 100,
                    MaxHp = 100
                }
            },
            CurrentPower = 2,
            MaxPower = 3
        };
        var burstTransform = new CombatActionObservation
        {
            CandidateId = "burst-transform",
            SourceId = "careercard_3",
            Kind = CombatActionKind.UseSkill
        };
        burstState.Actions.Add(burstTransform);
        burstState.Actions.Add(new CombatActionObservation
        {
            CandidateId = "burst-card-a",
            SourceId = "fixture-a",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 31,
            Cost = 1
        });
        burstState.Actions.Add(new CombatActionObservation
        {
            CandidateId = "burst-card-b",
            SourceId = "fixture-b",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 32,
            Cost = 1
        });
        CombatAiRegistry.ApplySemantics(burstState, burstTransform);
        CombatAiRegistry.EnrichRoleStrategies(burstState);
        if (burstTransform.Features.GetValueOrDefault(
                "nana:post-transform-max-hp") != 120d
            || burstTransform.Features.GetValueOrDefault(
                "nana:post-transform-damage-per-action") != 2d
            || burstTransform.Features.GetValueOrDefault(
                "nana:executable-burst-actions") != 2d
             || burstTransform.Features.GetValueOrDefault(
                 "roleStrategy:nana.transform-ready") != 1d
             || burstTransform.Features.GetValueOrDefault(
                 CombatSkillTimingFeatureNames.PositiveOpportunity) != 1d
             || burstTransform.Features.GetValueOrDefault(
                 CombatSkillTimingFeatureNames.TimingAdvantage) <= 0d
             || burstTransform.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 0d)
        {
            failures.Add("nana-transform-threshold-and-executable-burst");
        }

        burstState.Player.MaxHp = 60;
        burstState.Player.CurrentHp = 60;
        burstTransform.Features.Clear();
        CombatAiRegistry.ApplySemantics(burstState, burstTransform);
        CombatAiRegistry.EnrichRoleStrategies(burstState);
        if (burstTransform.Features.GetValueOrDefault(
                "nana:post-transform-max-hp") != 60d
            || burstTransform.Features.GetValueOrDefault(
                "nana:post-transform-damage-per-action") != 1d
            || burstTransform.Features.GetValueOrDefault(
                "nana:next-transform-damage-threshold-max-hp") != 100d)
        {
            failures.Add("nana-transform-preserves-low-max-hp-threshold");
        }

        var survivalState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_2",
                Kind = CombatTargetKind.Self,
                CurrentHp = 10,
                MaxHp = 100
            },
            CurrentPower = 1,
            MaxPower = 3,
            ExpectedIncomingDamage = 20d
        };
        var survivalAction = new CombatActionObservation
        {
            CandidateId = "nana-survival-block",
            SourceId = "fixture-block",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 1,
            TargetKind = CombatTargetKind.Self,
            Cost = 1,
            Semantics = new CombatActionSemantics { Defend = 25d }
        };
        survivalState.Actions.Add(survivalAction);
        CombatAiRegistry.EnrichRoleStrategies(survivalState);
        if (survivalState.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.Phase) != 4d
            || survivalState.Features.GetValueOrDefault(
                "roleStrategy:nana.survival-override") != 1d
            || survivalAction.Features.GetValueOrDefault(
                "roleStrategy:nana.survival-action") != 1d
            || survivalAction.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.Synergy) <= 6d)
        {
            failures.Add("nana-survival-override-priority");
        }

        var finaleState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_2",
                Kind = CombatTargetKind.Self,
                CurrentHp = 80,
                MaxHp = 120,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_DoomPower",
                        Level = 12,
                        Type = "Special"
                    },
                    new CombatStatusObservation
                    {
                        StatusId = "buff_burn",
                        Level = 5,
                        Rarity = 2,
                        Type = "Negative"
                    }
                }
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = 3,
            HandCardIds =
            {
                "Crowdfundingcard_43",
                "attack-a",
                "attack-b"
            },
            DeckCardIds =
            {
                "Crowdfundingcard_43",
                "attack-a",
                "attack-b",
                "attack-c",
                "attack-d",
                "attack-e",
                "attack-f"
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "nana-finale",
                    SourceId = "Crowdfundingcard_43",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 10,
                    Cost = 3
                },
                new CombatActionObservation
                {
                    CandidateId = "nana-finale-devour",
                    SourceId = "careercard_2",
                    Kind = CombatActionKind.UseSkill,
                    RuntimeId = 20,
                    TargetRuntimeId = 1,
                    TargetKind = CombatTargetKind.Self,
                    Cost = 0
                },
                new CombatActionObservation
                {
                    CandidateId = "nana-finale-transform",
                    SourceId = "careercard_3",
                    Kind = CombatActionKind.UseSkill,
                    RuntimeId = 21,
                    Cost = 0
                }
            }
        };
        foreach (var action in finaleState.Actions)
        {
            CombatAiRegistry.ApplySemantics(finaleState, action);
        }
        CombatAiRegistry.EnrichRoleStrategies(finaleState);
        CombatArchetypePolicy.Enrich(finaleState);
        var finaleAction = finaleState.Actions.Single(action =>
            action.SourceId == "Crowdfundingcard_43");
        var finaleLegal = CombatArchetypePolicy.IsLegal(
            finaleState,
            finaleAction,
            out var finaleReason);
        var finaleDevour = finaleState.Actions.Single(action =>
            action.SourceId == "careercard_2");
        var finaleTransform = finaleState.Actions.Single(action =>
            action.SourceId == "careercard_3");
        var finaleTransformLegal = CombatArchetypePolicy.IsLegal(
            finaleState,
            finaleTransform,
            out _);
        if (!finaleLegal
            || finaleAction.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.SafeContinuationCertified) != 1d
            || finaleAction.Semantics.EndOfCycleSelfHpLoss != 0d
            || finaleDevour.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.Synergy) <= 0d
            || finaleTransform.Features.GetValueOrDefault(
                CombatSkillTimingFeatureNames.Active) != 1d
            || finaleTransform.Features.GetValueOrDefault(
                CombatSkillTimingFeatureNames.WaitValue) <= 0d
            || !finaleTransformLegal)
        {
            failures.Add(
                "nana-finale-role-strategy:legal="
                + finaleLegal
                + ", reason="
                + finaleReason);
        }
        var unsafeFinaleState = CombatPlayerObservationBoundary.Normalize(
            finaleState);
        unsafeFinaleState.Actions.RemoveAll(action =>
            action.SourceId == "careercard_2");
        foreach (var action in unsafeFinaleState.Actions)
        {
            action.Features.Remove(
                CombatRoleStrategyFeatureNames.SafeContinuationCertified);
        }
        CombatAiRegistry.EnrichRoleStrategies(unsafeFinaleState);
        CombatArchetypePolicy.Enrich(unsafeFinaleState);
        var unsafeFinale = unsafeFinaleState.Actions.Single(action =>
            action.SourceId == "Crowdfundingcard_43");
        if (CombatArchetypePolicy.IsLegal(
                unsafeFinaleState,
                unsafeFinale,
                out _))
        {
            failures.Add("nana-finale-without-devour-was-legal");
        }
        var nanaCampaignDefaults = new CombatCampaignDefinition
        {
            Player = new CombatPlayerSetup
            {
                RoleId = "career_2",
                FamiliarBlessingIds = { "blessing_40" }
            }
        };
        AuraToolsRoleCampaignStrategy.Apply(nanaCampaignDefaults);
        if (nanaCampaignDefaults.RolePrior.GetValueOrDefault("cycling") <= 0d
            || nanaCampaignDefaults.RolePrior.GetValueOrDefault(
                "nightmare-debuff-events") <= 0d
            || nanaCampaignDefaults.RewardScoreBiases.GetValueOrDefault(
                "Crowdfundingcard_43") <= 0d
            || nanaCampaignDefaults.RewardScoreBiases.GetValueOrDefault(
                "blood_13") <= 0d)
        {
            failures.Add("nana-campaign-strategy-defaults");
        }
        var diagnostics = AuraToolsRoleTrainingDiagnostics.Analyze(new[]
        {
            new CombatEpisode
            {
                EpisodeId = "nana-journey-final",
                JourneyRunId = "nana-journey",
                JourneyBattleIndex = 2,
                FinalPlayerMaxHp = 1200,
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    DifficultyId = "normal",
                    FinalBossVictory = true,
                    TerminalSnapshotKnown = true,
                    TerminalBattleIndex = 36,
                    TerminalPlayerHp = 140,
                    TerminalPlayerMaxHp = 180,
                    TerminalDoomPower = 12
                },
                Frames =
                {
                    new CombatEpisodeFrame
                    {
                        Turn = 2,
                        ActionSequence = 1,
                        ExecutedCandidateId = "devour",
                        Candidates =
                        {
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "devour",
                                SourceId = "careercard_2",
                                Features =
                                {
                                    [CombatRoleStrategyFeatureNames.Active] = 1d,
                                    ["nana:devour-net-value"] = 5d
                                }
                            }
                        }
                    },
                    new CombatEpisodeFrame
                    {
                        Turn = 3,
                        ActionSequence = 2,
                        ExecutedCandidateId = "transform",
                        Candidates =
                        {
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "transform",
                                SourceId = "careercard_3",
                                Features =
                                {
                                     [CombatRoleStrategyFeatureNames.Active] = 1d,
                                     ["nana:first-transform"] = 1d,
                                     ["roleStrategy:nana.transform-ready"] = 1d,
                                     [CombatSkillTimingFeatureNames.Active] = 1d,
                                     [CombatSkillTimingFeatureNames.PositiveOpportunity] = 1d,
                                     [CombatSkillTimingFeatureNames.TimingAdvantage] = 3d
                                }
                            }
                        }
                    },
                    new CombatEpisodeFrame
                    {
                        Turn = 4,
                        ActionSequence = 3,
                        ExecutedCandidateId = "repeat-transform",
                        StateFeatures =
                        {
                            ["playerRole:career_4"] = 1d
                        },
                        Candidates =
                        {
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "repeat-transform",
                                SourceId = "careercard_3",
                                Features =
                                {
                                     [CombatRoleStrategyFeatureNames.Active] = 1d,
                                     ["nana:repeat-transform"] = 1d,
                                     [CombatSkillTimingFeatureNames.Active] = 1d,
                                     [CombatSkillTimingFeatureNames.BetterToWait] = 1d,
                                     [CombatSkillTimingFeatureNames.TimingAdvantage] = -40d,
                                     [CombatSkillTimingFeatureNames.RedundancyCost] = 40d
                                }
                            }
                        }
                    },
                    new CombatEpisodeFrame
                    {
                        Turn = 4,
                        ActionSequence = 4,
                        StateFeatures =
                        {
                            ["playerRole:career_2"] = 1d
                        }
                    }
                }
            },
            new CombatEpisode
            {
                EpisodeId = "nana-journey-earlier",
                JourneyRunId = "nana-journey",
                JourneyBattleIndex = 1,
                FinalPlayerMaxHp = 100,
                Campaign = new CombatCampaignEpisodeMetadata
                {
                    DifficultyId = "normal",
                    FinalBossVictory = true,
                    TerminalSnapshotKnown = true,
                    TerminalBattleIndex = 36,
                    TerminalPlayerHp = 140,
                    TerminalPlayerMaxHp = 180,
                    TerminalDoomPower = 12
                }
            },
            new CombatEpisode
            {
                EpisodeId = "generic-skill-expired",
                Frames =
                {
                    new CombatEpisodeFrame
                    {
                        Turn = 1,
                        ActionSequence = 1,
                        ExecutedCandidateId = "end-turn",
                        Candidates =
                        {
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "generic-skill",
                                SourceId = "generic-skill",
                                Features =
                                {
                                    [CombatSkillTimingFeatureNames.Active] = 1d,
                                    [CombatSkillTimingFeatureNames.PositiveOpportunity] = 1d,
                                    [CombatSkillTimingFeatureNames.TimingAdvantage] = 2d
                                }
                            },
                            new CombatEpisodeCandidate
                            {
                                CandidateId = "end-turn",
                                SourceId = "simulation:end-turn"
                            }
                        }
                    }
                }
            }
        }, new[]
        {
            new CombatFoundationCampaignObservation
            {
                SourceStage = "training",
                DifficultyId = "normal",
                FinalBossVictory = true,
                FinalHp = 190,
                FinalMaxHp = 222,
                FinalDoomPower = 17
            }
        });
        if (diagnostics.GetValueOrDefault("final-max-hp.maximum") != 222d
            || diagnostics.GetValueOrDefault("journey-final-doom.mean") != 17d
            || diagnostics.GetValueOrDefault(
                "journey-normal-victory-final-max-hp.mean") != 222d
            || diagnostics.GetValueOrDefault(
                "journey-normal-victory-final-doom.mean") != 17d
            || diagnostics.GetValueOrDefault(
                "nana.devour-transform-link-rate") != 1d
            || diagnostics.GetValueOrDefault("nana.first-transforms") != 1d
            || diagnostics.GetValueOrDefault("nana.repeat-transforms") != 1d
            || diagnostics.GetValueOrDefault(
                "nana.role-strategy-observed-frames") != 4d
            || diagnostics.GetValueOrDefault(
                "nana.role-strategy-non-actionable-frames") != 1d
            || diagnostics.GetValueOrDefault(
                "nana.role-strategy-eligible-frames") != 3d
            || diagnostics.GetValueOrDefault(
                "nana.role-strategy-frame-coverage") != 1d
            || diagnostics.GetValueOrDefault(
                "nana.selected-nonpositive-devours") != 0d
            || diagnostics.GetValueOrDefault(
                "nana.selected-underprepared-transforms") != 0d
            || diagnostics.GetValueOrDefault(
                "skill-timing.evaluated-candidates") != 3d
            || diagnostics.GetValueOrDefault(
                "skill-timing.positive-opportunity-frames") != 2d
            || diagnostics.GetValueOrDefault(
                "skill-timing.expired-positive-skills") != 1d
            || diagnostics.GetValueOrDefault(
                "skill-timing.selected-positive-activations") != 1d
            || diagnostics.GetValueOrDefault(
                "skill-timing.selected-better-to-wait") != 1d
            || diagnostics.GetValueOrDefault(
                "skill-timing.selected-redundant") != 1d
            || diagnostics.GetValueOrDefault(
                "skill-timing.skill.careercard_3.selected-activations") != 2d
            || diagnostics.GetValueOrDefault(
                "skill-timing.skill.careercard_3.selected-positive-activations") != 1d
            || diagnostics.GetValueOrDefault(
                "skill-timing.skill.careercard_3.selected-better-to-wait") != 1d
            || !diagnostics.ContainsKey(
                "skill-timing.skill.careercard_13.evaluated-candidates"))
        {
            failures.Add("nana-training-diagnostics");
        }
        return failures;
    }

}
