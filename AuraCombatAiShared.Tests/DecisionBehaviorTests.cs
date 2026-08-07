using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

internal static class CombatAiDecisionBehaviorTests
{
    public static void Run()
    {
        var gameSubjectCatalog = new CombatGameSubjectCatalog
        {
            Roles =
            {
                new CombatGameSubjectRole
                {
                    Id = "career_test",
                    DisplayName = "Test Role",
                    SkillCardIds = { "skill_test" },
                    SkillCooldownTurns = { ["skill_test"] = 3 },
                    InitialSkillCooldownTurns = { ["skill_test"] = 2 },
                    MaximumHp = 60,
                    InitialVariables = { ["DoomPower"] = 0d },
                    InitialStatuses = { ["status_test"] = 2 },
                    NativeScriptHash = "native-role-hash",
                    FightScript = "AddEvent(\"StartRound\", () => { });",
                    NativeManagedSkillCooldownIds = { "skill_test" },
                    TransformRoleIds = { "career_test_form" }
                },
                new CombatGameSubjectRole
                {
                    Id = "career_test_form",
                    DisplayName = "Test Role Form",
                    SkillCardIds = { "skill_test" },
                    SkillCooldownTurns = { ["skill_test"] = 1 },
                    MaximumHp = 40
                }
            },
            Familiars =
            {
                new CombatGameSubjectFamiliar
                {
                    Id = "partner_test",
                    DisplayName = "Test Familiar",
                    BlessingIds = { "blessing_test" }
                }
            },
            CardPacks =
            {
                new CombatGameSubjectCardPack
                {
                    Id = "cardpack_1",
                    Required = true
                },
                new CombatGameSubjectCardPack
                {
                    Id = "cardpack_2",
                    Required = true
                },
                new CombatGameSubjectCardPack
                {
                    Id = "cardpack_3"
                }
            }
        }.Normalize();
        var gameSubject = new CombatGameSubjectPreset
        {
            Id = "test-subject",
            RoleId = "career_test",
            PartnerId = "partner_test",
            EnabledRewardCardPackIds = { "cardpack_3" },
            PreferredDeckSizeMinimum = 12,
            PreferredDeckSizeMaximum = 20
        };
        gameSubjectCatalog.ResolveReferences(gameSubject);
        var gameSubjectCampaign = new CombatCampaignDefinition
        {
            Player = new CombatPlayerSetup
            {
                Deck = { "strike", "guard" }
            }
        };
        CombatGameSubjectPresetRuntime.Apply(gameSubject, gameSubjectCampaign);
        Assert(
            gameSubjectCampaign.Player.RoleId == "career_test"
            && gameSubjectCampaign.Player.PartnerId == "partner_test"
            && gameSubjectCampaign.Player.SkillCardIds.SequenceEqual(
                new[] { "skill_test" })
            && gameSubjectCampaign.Player.SkillCooldownTurns["skill_test"] == 3
            && gameSubjectCampaign.Player.InitialSkillCooldownTurns["skill_test"] == 2
            && gameSubjectCampaign.Player.MaxHp == 60
            && gameSubjectCampaign.Player.CurrentHp == 60
            && gameSubjectCampaign.Player.Variables["DoomPower"] == 0d
            && gameSubjectCampaign.Player.InitialStatuses.Count == 0
            && gameSubject.ResolvedRoleInitialStatuses["status_test"] == 2
            && gameSubjectCampaign.Player.RoleNativeScriptHash == "native-role-hash"
            && gameSubjectCampaign.Player.NativeManagedSkillCooldownIds.SequenceEqual(
                new[] { "skill_test" })
            && gameSubjectCampaign.Player.RoleRuntimeForms.Count == 2
            && gameSubjectCampaign.Player.RoleRuntimeForms.Any(item =>
                item.RoleId == "career_test")
            && gameSubjectCampaign.Player.RoleRuntimeForms.Single(item =>
                item.RoleId == "career_test_form")
               .SkillCooldownTurns["skill_test"] == 1
            && gameSubjectCampaign.Player.FamiliarBlessingIds.SequenceEqual(
                new[] { "blessing_test" })
            && gameSubjectCampaign.EnabledRewardCardPackIds.SequenceEqual(
                new[] { "cardpack_1", "cardpack_2", "cardpack_3" })
            && gameSubjectCampaign.Player.GameParameterHash
               == CombatGameSubjectPresetRuntime.ComputeHash(
                   gameSubject,
                   gameSubjectCampaign.Player.Deck),
            "game subject preset resolves and applies one immutable campaign snapshot");

        var lowValueTransformState = BuildHandTransformFixture(valuableHand: false);
        var lowValueTransformAction = lowValueTransformState.Actions[0];
        CombatHandTransformPolicy.Enrich(
            lowValueTransformState,
            lowValueTransformAction);
        var lowValueTransform = CombatHandTransformPolicy.Assess(
            lowValueTransformState,
            lowValueTransformAction);
        var highValueTransformState = BuildHandTransformFixture(valuableHand: true);
        var highValueTransformAction = highValueTransformState.Actions[0];
        CombatHandTransformPolicy.Enrich(
            highValueTransformState,
            highValueTransformAction);
        var highValueTransform = CombatHandTransformPolicy.Assess(
            highValueTransformState,
            highValueTransformAction);
        Assert(
            lowValueTransform.CleanupValue > 0d
            && highValueTransform.EngineLoss > lowValueTransform.EngineLoss
            && highValueTransform.EnhancementLoss > 0d
            && lowValueTransform.NetValue > highValueTransform.NetValue
            && lowValueTransform.DepletionRisk > 0d,
            "whole-hand transform values cleanup, engine sacrifice, enhancements and depletion separately");
        var distributedLethalState = BuildHandTransformFixture(valuableHand: false);
        distributedLethalState.HandCount = 2;
        distributedLethalState.HandCardIds.RemoveAt(2);
        distributedLethalState.HandCards.RemoveAt(2);
        distributedLethalState.Enemies.Clear();
        distributedLethalState.Enemies.AddRange(new[]
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 100,
                MaxHp = 100
            },
            new CombatUnitObservation
            {
                RuntimeId = 3,
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 1,
                MaxHp = 1
            }
        });
        distributedLethalState.Actions[0].Semantics.HandTransform!
            .TargetCardSemantics.Damage = 30d;
        distributedLethalState.Actions[0].Semantics.HandTransform!
            .TargetCardSemantics.AffectedEnemyCount = 2;
        Assert(
            !CombatHandTransformPolicy.Assess(
                distributedLethalState,
                distributedLethalState.Actions[0]).LethalCertified,
            "whole-hand transform lethal certification is per enemy rather than aggregate damage");
        var transformSimulation = CombatForwardModel.Create(
            lowValueTransformState,
            lowValueTransformState.Actions.Count);
        var transformOutcome = CombatForwardModel.Resolve(
                lowValueTransformState,
                lowValueTransformAction,
                useRegisteredResolvers: false)
            .Outcomes.Single();
        var transformedSimulation = CombatForwardModel.Apply(
            transformSimulation,
            lowValueTransformAction,
            0,
            transformOutcome,
            new CombatDecisionProfile());
        Assert(
            transformedSimulation.HandCount == lowValueTransformState.HandCount
            && transformedSimulation.HandCardIds.Count
               == lowValueTransformState.HandCount
            && transformedSimulation.HandCardIds.All(id => id == "nocard_1")
            && transformedSimulation.RetainedHandCardIds.Count
               == lowValueTransformState.HandCount,
            "forward model replaces every hand instance and preserves transformed retain semantics");
        var selfGrowthRoot = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                Kind = CombatTargetKind.Self,
                CurrentHp = 50,
                MaxHp = 100
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
            }
        };
        var selfGrowthAction = new CombatActionObservation
        {
            CandidateId = "self-growth",
            SourceId = "self-growth",
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics
            {
                Damage = 5d,
                Heal = 12d,
                StateChanges =
                {
                    ["playerMaxHp"] = 12d,
                    ["player.hp"] = 12d
                },
                TargetEffects =
                {
                    new CombatTargetedSemanticEffect
                    {
                        Kind = CombatSemanticEffectKind.Heal,
                        TargetRuntimeId = 1,
                        RawAmount = 12d,
                        EffectiveAmount = 12d
                    }
                }
            }
        };
        var selfGrowthOutcome = CombatForwardModel.Resolve(
                selfGrowthRoot,
                selfGrowthAction,
                useRegisteredResolvers: false)
            .Outcomes.Single();
        var selfGrowthSimulation = CombatForwardModel.Apply(
            CombatForwardModel.Create(selfGrowthRoot, 1),
            selfGrowthAction,
            0,
            selfGrowthOutcome,
            new CombatDecisionProfile());
        Assert(selfGrowthSimulation.PlayerMaxHp == 112
               && selfGrowthSimulation.PlayerHp == 62
               && selfGrowthSimulation.Enemies.Single().Hp == 95,
            "forward model applies maximum-health growth before its paired self-heal without healing the Devour target");

        var graph = new DecisionGraph
        {
            RootNodeId = "low-health",
            Nodes =
            {
                new DecisionGraphNode
                {
                    Id = "low-health",
                    Condition = new DecisionCondition
                    {
                        Feature = "playerHpRatio",
                        Comparison = DecisionComparison.LessThan,
                        Value = 0.3d
                    },
                    TrueNodeId = "survive",
                    FalseNodeId = "attack"
                },
                new DecisionGraphNode
                {
                    Id = "survive",
                    Terminal = true,
                    UtilityDelta = new DecisionUtility { Survival = 10d }
                },
                new DecisionGraphNode
                {
                    Id = "attack",
                    Terminal = true,
                    UtilityDelta = new DecisionUtility { Lethal = 3d }
                }
            }
        };
        var lowHealth = DecisionGraphEvaluator.Evaluate(
            graph,
            new Dictionary<string, double> { ["playerHpRatio"] = 0.2d });
        Assert(lowHealth.TerminalNodeId == "survive" && lowHealth.UtilityDelta.Survival == 10d,
            "decision graph follows true branch");

        var multi = MultiSelectPlanner.ChooseIndices(
            new[]
            {
                new DecisionUtility { Lethal = 8d },
                new DecisionUtility { Lethal = 1d },
                new DecisionUtility { Survival = 3d }
            },
            2,
            preferLowest: true);
        Assert(multi.SequenceEqual(new[] { 1, 2 }), "burn planner keeps the weakest choices");

        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                Kind = CombatTargetKind.Self,
                CurrentHp = 20,
                MaxHp = 30
            },
            CurrentPower = 3,
            HandCount = 2,
            IsPlayerActionWindow = true,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 6,
                    MaxHp = 20
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "attack",
                    SourceId = "attack",
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 2,
                    TargetKind = CombatTargetKind.Enemy,
                    Semantics = new CombatActionSemantics { Damage = 6d }
                },
                new CombatActionObservation
                {
                    CandidateId = "guard",
                    Kind = CombatActionKind.PlayCard,
                    Semantics = new CombatActionSemantics { Defend = 5d }
                },
                new CombatActionObservation
                {
                    CandidateId = "end",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var combatDecision = new CombatDecisionEngine().Choose(state);
        Assert(combatDecision.HasAction && combatDecision.Action?.CandidateId == "attack",
            "lethal action wins balanced utility");

        using (CombatAiRegistry.RegisterPreflightRule(
                   "Tests",
                   "RejectAttack",
                   new RejectCandidateRule("attack"),
                   100))
        {
            var guardedDecision = new CombatDecisionEngine().Choose(state);
            Assert(guardedDecision.HasAction && guardedDecision.Action?.CandidateId == "guard",
                "registered preflight rule filters candidates");
        }

        CombatInteractionBroker.SetNextHint(new CombatInteractionHint
        {
            OwnerModId = "Tests",
            Purpose = "burn",
            Kind = CombatPromptKind.BurnCards,
            PreferLowestValue = true
        });
        var request = CombatInteractionBroker.Begin(
            new CombatInteractionHint { Purpose = "fallback" },
            2,
            state.Actions);
        Assert(request.Hint.Purpose == "burn"
               && request.RequiredCount == 2
               && request.State == CombatInteractionState.AwaitingUi,
            "interaction hint is bound to the next native prompt");
        Assert(CombatInteractionBroker.Transition(
                request.RequestId,
                CombatInteractionState.Completed,
                "done"),
            "interaction transition");
        CombatInteractionBroker.Clear(request.RequestId);
        Assert(CombatInteractionBroker.Snapshot() == null, "interaction cleanup");

        var transaction = new CombatActionTransaction();
        Assert(transaction.TryBegin(7, "card:test", 10d, 2d),
            "action transaction accepts the first root submission");
        Assert(!transaction.TryBegin(7, "card:duplicate", 10.1d, 2d)
               && transaction.SubmitCount == 1,
            "action transaction rejects duplicate root submissions");
        transaction.AwaitPrompt();
        transaction.Selecting();
        Assert(!transaction.CheckDeadline(12d), "transaction remains active at its exact deadline");
        Assert(transaction.CheckDeadline(12.01d)
               && transaction.State == CombatActionTransactionState.TimedOut,
            "pending interaction obeys the root action deadline");

        transaction.Reset();
        Assert(transaction.TryBegin(8, "card:handoff", 0d, 5d), "handoff transaction starts");
        transaction.HandOff("player takeover");
        Assert(transaction.State == CombatActionTransactionState.HandedOff
               && transaction.TerminalReason == "player takeover",
            "player takeover is a terminal transaction state");

        foreach (var intermediate in new[]
                 {
                     CombatActionTransactionState.ExecutingRoot,
                     CombatActionTransactionState.AwaitingPrompt,
                     CombatActionTransactionState.Selecting,
                     CombatActionTransactionState.AwaitingSettlement
                 })
        {
            transaction.Reset();
            Assert(transaction.TryBegin(9, "battle-end", 0d, 5d), "battle-end transaction starts");
            if (intermediate == CombatActionTransactionState.AwaitingPrompt)
            {
                transaction.AwaitPrompt();
            }
            else if (intermediate == CombatActionTransactionState.Selecting)
            {
                transaction.Selecting();
            }
            else if (intermediate == CombatActionTransactionState.AwaitingSettlement)
            {
                transaction.AwaitSettlement();
            }
            transaction.Cancel("battle ended");
            Assert(transaction.State == CombatActionTransactionState.Cancelled,
                "battle end cancels " + intermediate);
        }

        var selection = new CombatPromptSelectionTracker(2, 0.8d);
        Assert(selection.Observe(0, 0d) == CombatSelectionProgress.Ready
               && selection.TryBeginAttempt(0, 0d),
            "multi-select starts one selection attempt");
        Assert(selection.Observe(0, 0.5d) == CombatSelectionProgress.Pending,
            "selection waits for native progress");
        Assert(selection.Observe(1, 0.6d) == CombatSelectionProgress.Advanced
               && selection.TryBeginAttempt(1, 0.7d),
            "selection verifies count growth before advancing");
        Assert(selection.Observe(1, 1.6d) == CombatSelectionProgress.TimedOut,
            "selection no-progress timeout is detected");

        selection = new CombatPromptSelectionTracker(2);
        Assert(selection.TryBeginAttempt(0, 0d)
               && selection.Observe(1, 0.1d) == CombatSelectionProgress.Advanced
               && selection.TryBeginAttempt(1, 0.2d)
               && selection.Observe(2, 0.3d) == CombatSelectionProgress.Complete,
            "multi-select reaches the exact required count");
        Assert(selection.TryIssueConfirm(2, 0.3d) && !selection.TryIssueConfirm(2, 0.3d),
            "prompt confirmation can only be issued once");
        Assert(selection.Observe(2, 0.4d)
               == CombatSelectionProgress.AwaitingNativeClose
               && selection.Observe(2, 2.31d)
               == CombatSelectionProgress.TimedOut,
            "confirmed prompts wait for native closure without selecting again and fail on a bounded close timeout");

        var setupState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 10,
                Kind = CombatTargetKind.Self,
                CurrentHp = 25,
                MaxHp = 30
            },
            CurrentPower = 3,
            MaxPower = 3,
            HandCount = 4,
            IsPlayerActionWindow = true,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 20,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "small-attack",
                    SourceId = "small-attack",
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 20,
                    TargetKind = CombatTargetKind.Enemy,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Damage = 3d }
                },
                new CombatActionObservation
                {
                    CandidateId = "free-setup",
                    SourceId = "free-setup",
                    Kind = CombatActionKind.PlayCard,
                    Cost = 0,
                    Semantics = new CombatActionSemantics
                    {
                        Draw = 2d,
                        Buff = 2d,
                        CostReduction = 1d
                    }
                },
                new CombatActionObservation
                {
                    CandidateId = "end-setup-test",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var setupDecision = new CombatDecisionEngine().Choose(setupState);
        Assert(setupDecision.Action?.CandidateId == "free-setup",
            "known positive zero-cost setup can precede a weak attack");

        setupState.Actions[1].Semantics = new CombatActionSemantics
        {
            RandomOutcome = true,
            Uncertainty = 2d
        };
        var uncertainFreeDecision = new CombatDecisionEngine().Choose(setupState);
        Assert(uncertainFreeDecision.Action?.CandidateId == "small-attack",
            "zero cost alone does not force an uncertain action");

        var projectedLeafFeatures = CombatSearchFeatureProjector.ProjectLeaf(
            new CombatSimulationState
            {
                PlayerHp = 18,
                PlayerMaxHp = 30,
                PlayerDefend = 3,
                Power = 2,
                MaxPower = 3,
                HandCount = 4,
                SetupValue = 2.5d,
                DamageMultiplier = 1.2d,
                Threats =
                [
                    new CombatSimulationThreat
                    {
                        BlockableDamage = 10d,
                        UnblockableDamage = 2d,
                        DamageOverTime = 1d,
                        Probability = 0.7d
                    }
                ]
            },
            new CombatDecisionProfile(),
            new Dictionary<string, double> { ["turn"] = 3d });
        Assert(projectedLeafFeatures["expectedBlockableDamage"] == 7d
               && projectedLeafFeatures["maximumBlockableDamage"] == 10d
               && projectedLeafFeatures["expectedUnblockableDamage"] == 1.4d
               && projectedLeafFeatures["expectedDamageOverTime"] == 0.7d
               && projectedLeafFeatures["playerHp"] == 18d
               && projectedLeafFeatures["setupValue"] == 2.5d
               && projectedLeafFeatures["damageMultiplier"] == 1.2d
               && projectedLeafFeatures["turn"] == 3d,
            "search leaf inference uses the same feature names as training");
        var cycleLeafFeatures = CombatSearchFeatureProjector.ProjectLeaf(
            new CombatSimulationState
            {
                PlayerHp = 20,
                PlayerMaxHp = 20,
                HandCount = 4,
                HandLimit = 5,
                HandCardValues = { 1d, 2d, 3d, 4d },
                RetainedHandCardValues = { 3d, 4d },
                DrawPileValues = { 5d },
                DiscardPileValues = { 6d, 7d, 8d },
                Features = { ["drawPerTurn"] = 4d }
            },
            new CombatDecisionProfile());
        Assert(cycleLeafFeatures["recyclableCardCount"] == 6d
               && cycleLeafFeatures["unretainedHandCount"] == 2d
               && cycleLeafFeatures["lockedHandCount"] == 2d
               && cycleLeafFeatures["availableHandSlots"] == 1d
               && cycleLeafFeatures["effectiveNextDraw"] == 3d
               && cycleLeafFeatures["drawPileShortfall"] == 2d
               && cycleLeafFeatures["reshuffleWithinNextDraw"] == 1d
               && cycleLeafFeatures["cycleAccessRate"] == 0.5d,
            "cycle features separate locked hand slots from cards that can return through the discard pile");
        var setupLeafState = new CombatSimulationState
        {
            PlayerHp = 20,
            PlayerMaxHp = 20,
            SetupValue = 5d,
            PersistentValue = 3d,
            Enemies =
            [
                new CombatSimulationUnit { RuntimeId = 9, Hp = 10, MaxHp = 10 }
            ]
        };
        var emptyLeafState = setupLeafState.Clone();
        emptyLeafState.SetupValue = 0d;
        emptyLeafState.PersistentValue = 0d;
        Assert(setupLeafState.EvaluateLeaf(new CombatDecisionProfile()).Value
               > emptyLeafState.EvaluateLeaf(new CombatDecisionProfile()).Value,
            "forward leaf value retains setup and persistent benefits");

        var cooldownState = new CombatStateObservation
        {
            Player = setupState.Player,
            CurrentPower = 2,
            MaxPower = 3,
            HandCount = 2,
            IsPlayerActionWindow = true,
            Enemies = setupState.Enemies,
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "free-card",
                    Kind = CombatActionKind.PlayCard,
                    Cost = 0,
                    Semantics = new CombatActionSemantics { Buff = 2d }
                },
                new CombatActionObservation
                {
                    CandidateId = "long-cooldown-skill",
                    Kind = CombatActionKind.UseSkill,
                    Cost = 0,
                    Semantics = new CombatActionSemantics { Buff = 2d, CooldownTurns = 6d }
                },
                new CombatActionObservation
                {
                    CandidateId = "end-cooldown-test",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var cooldownDecision = new CombatDecisionEngine().Choose(cooldownState);
        Assert(cooldownDecision.Action?.CandidateId == "free-card",
            "long-cooldown skill carries an opportunity cost");

        var poisonThreatState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 30,
                Kind = CombatTargetKind.Self,
                CurrentHp = 100,
                MaxHp = 100,
                Defend = 24
            },
            CurrentPower = 1,
            MaxPower = 3,
            HandCount = 2,
            IsPlayerActionWindow = true,
            Threat = new CombatThreatForecast
            {
                CurrentIntentKnown = true,
                ExpectedDamageOverTime = 12d,
                AttackProbability = 1d,
                Confidence = 1d
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 31,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 12,
                    MaxHp = 12
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "poison-shield",
                    SourceId = "shield",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 301,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Defend = 6d }
                },
                new CombatActionObservation
                {
                    CandidateId = "poison-attack",
                    SourceId = "attack",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 302,
                    TargetRuntimeId = 31,
                    TargetKind = CombatTargetKind.Enemy,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Damage = 3d }
                },
                new CombatActionObservation
                {
                    CandidateId = "poison-end",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var poisonDecision = new CombatDecisionEngine().Choose(poisonThreatState);
        var poisonShieldFeatures = poisonDecision.Candidates
            .Single(candidate => candidate.Action.CandidateId == "poison-shield")
            .Action.Features;
        Assert(poisonShieldFeatures["immediateDefend"] == 0d
               && poisonShieldFeatures["shieldCarryGain"] == 6d
               && poisonShieldFeatures["wastedDefend"] == 0d,
            "damage-over-time does not consume shield, while persistent shield keeps future value");

        var attackThreatState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 40,
                Kind = CombatTargetKind.Self,
                CurrentHp = 5,
                MaxHp = 30
            },
            CurrentPower = 1,
            MaxPower = 3,
            HandCount = 2,
            IsPlayerActionWindow = true,
            Threat = new CombatThreatForecast
            {
                CurrentIntentKnown = true,
                ExpectedBlockableDamage = 10d,
                MaximumBlockableDamage = 10d,
                AttackProbability = 1d,
                Confidence = 1d
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 41,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 30,
                    MaxHp = 30
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "needed-shield",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 401,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Defend = 10d }
                },
                new CombatActionObservation
                {
                    CandidateId = "nonlethal-attack",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 402,
                    TargetRuntimeId = 41,
                    TargetKind = CombatTargetKind.Enemy,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Damage = 4d }
                },
                new CombatActionObservation
                {
                    CandidateId = "attack-threat-end",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var survivalDecision = new CombatDecisionEngine().Choose(attackThreatState);
        Assert(survivalDecision.Action?.CandidateId == "needed-shield",
            "known lethal blockable threat still prioritizes sufficient shield");

        using (CombatAiRegistry.RegisterThreatProvider(
                   "Tests",
                   "PoisonForecast",
                   new FixedThreatProvider(new CombatThreatForecast
                   {
                       CurrentIntentKnown = true,
                       ExpectedUnblockableDamage = 9d,
                       Confidence = 1d
                   }),
                   100))
        {
            Assert(CombatAiRegistry.TryResolveThreat(state, out var registeredThreat)
                   && registeredThreat.ExpectedUnblockableDamage == 9d,
                "registered threat provider can override the native forecast");
        }

        var model = new BoundedLinearDecisionResidualModel(new DecisionResidualModelDefinition
        {
            ModelId = "bounded-test",
            FeatureSchemaVersion = 6,
            MaximumCorrection = 2d,
            Weights = new Dictionary<string, double> { ["effectiveDamage"] = 100d },
            FeatureMinimums = new Dictionary<string, double> { ["effectiveDamage"] = 0d },
            FeatureMaximums = new Dictionary<string, double> { ["effectiveDamage"] = 5d },
            FeatureObservationCounts = new Dictionary<string, double> { ["effectiveDamage"] = 10d },
            CategoryObservationCounts = new Dictionary<string, double> { ["categoryAttack"] = 10d }
        });
        var boundedPrediction = model.Evaluate(new Dictionary<string, double>
        {
            ["effectiveDamage"] = 5d,
            ["semanticConfidence"] = 1d,
            ["categoryAttack"] = 1d
        });
        Assert(boundedPrediction.RawCorrection == 2d
               && boundedPrediction.Applicability == 1d
               && boundedPrediction.AppliedCorrection == 2d,
            "learned residual is bounded before it reaches the rule engine");
        Assert(model.Predict(new Dictionary<string, double>
               {
                   ["effectiveDamage"] = 5d,
                   ["semanticConfidence"] = 0d,
                   ["categoryAttack"] = 1d
               }) == 0d,
            "unknown mechanics fall back to the base policy");

        combatDecision.Action!.Features["nonFinite"] = double.NaN;
        var afterState = new CombatStateObservation
        {
            BattleSessionId = state.BattleSessionId,
            Sequence = state.Sequence + 1,
            Fingerprint = "after",
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                Kind = CombatTargetKind.Self,
                CurrentHp = 20,
                MaxHp = 30,
                Defend = 0
            },
            CurrentPower = 2,
            MaxPower = 3,
            HandCount = 1,
            IsPlayerActionWindow = true
        };
        var trainingSample = CombatTrainingSampleBuilder.Create(
            state,
            afterState,
            combatDecision,
            1,
            42,
            CombatActionTransactionState.Completed.ToString(),
            "settled",
            terminal: true,
            gameBuild: "test-game",
            sharedBuild: "test-shared");
        Assert(trainingSample.ModelProtocol == "aura.combat-ai.sample.v7"
               && trainingSample.FeatureSchemaVersion == 10
               && trainingSample.Candidates.Count == state.Actions.Count
               && trainingSample.Candidates.Single(candidate =>
                   candidate.CandidateId == "attack").SourceId == "attack",
            "training sample captures the selected action and every candidate");
        Assert(trainingSample.Selection.Protocol == "aura.combat-ai.selection.v1"
               && trainingSample.Selection.ExecutedBy == "policy"
               && trainingSample.Selection.LabelKind == "policy-trajectory"
               && trainingSample.Selection.ExecutedCandidateId == "attack"
               && trainingSample.Selection.PolicyPreselectedCandidateId == "attack"
               && trainingSample.Selection.PolicyWasExecuted
               && trainingSample.Candidates.Single(candidate =>
                   candidate.CandidateId == "attack").IsExecutedAction
               && trainingSample.Candidates.Single(candidate =>
                   candidate.CandidateId == "attack").IsPolicyPreselection,
            "policy sample marks its preselected and executed action");
        Assert(trainingSample.Terminal
               && trainingSample.BattleOutcome == "victory"
               && trainingSample.RewardComponents.TerminalBonus == 50d,
            "training sample captures terminal outcome reward");
        Assert(!trainingSample.Features.ContainsKey("nonFinite"),
            "training features reject non-finite values");

        var humanSample = CombatTrainingSampleBuilder.Create(
            state,
            afterState,
            combatDecision,
            2,
            43,
            CombatActionTransactionState.Completed.ToString(),
            "human settled",
            terminal: false,
            gameBuild: "test-game",
            sharedBuild: "test-shared",
            demonstrator: "human",
            recommendedCandidateId: "guard",
            policyVisibleToHuman: true);
        humanSample.BattleSessionId = 43;
        var humanActionCandidate = humanSample.Candidates.Single(candidate =>
            candidate.CandidateId == "attack");
        var policyCandidate = humanSample.Candidates.Single(candidate =>
            candidate.CandidateId == "guard");
        Assert(humanSample.Selection.ExecutedBy == "human"
               && humanSample.Selection.LabelKind == "human-preference"
               && humanSample.Selection.PolicyVisibleToHuman
               && !humanSample.Selection.PolicyWasExecuted
               && !humanSample.Selection.HumanPolicyAgreement
               && humanActionCandidate.IsExecutedAction
               && humanActionCandidate.IsHumanSelection
               && !humanActionCandidate.IsPolicyPreselection
               && !policyCandidate.IsExecutedAction
               && policyCandidate.IsPolicyPreselection,
            "human sample visibly separates the executed action from policy preselection");

        var noThreatDefendFeatures = CombatDecisionEngine.BuildFeatures(
            state,
            state.Actions.Single(action => action.CandidateId == "guard"));
        var neededDefendFeatures = CombatDecisionEngine.BuildFeatures(
            attackThreatState,
            attackThreatState.Actions.Single(action => action.CandidateId == "needed-shield"));
        Assert(noThreatDefendFeatures["usefulDefend"] == 5d
               && noThreatDefendFeatures["immediateDefend"] == 0d
               && noThreatDefendFeatures["shieldCarryGain"] == 5d
               && noThreatDefendFeatures["wastedDefend"] == 0d
               && noThreatDefendFeatures["semanticConfidence"] == 1d
               && neededDefendFeatures["immediateDefend"] > 0d,
            "context features distinguish immediate defense from persistent shield value");

        var originalSemanticAction = new CombatActionObservation
        {
            CandidateId = "original-attack",
            SourceId = "BaseGame:card",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 6d }
        };
        var sameSemanticAction = new CombatActionObservation
        {
            CandidateId = "mod-attack",
            SourceId = "SomeOtherMod:custom-card",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 2,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 6d }
        };
        var originalFeatures = CombatDecisionEngine.BuildFeatures(state, originalSemanticAction);
        var modFeatures = CombatDecisionEngine.BuildFeatures(state, sameSemanticAction);
        Assert(originalFeatures.All(pair =>
                modFeatures.TryGetValue(pair.Key, out var value) && value == pair.Value)
               && modFeatures.All(pair =>
                   originalFeatures.TryGetValue(pair.Key, out var value) && value == pair.Value),
            "residual context is independent of original or MOD content identity");

        var trained = CombatResidualTrainer.Train(new[] { humanSample }, "balanced");
        Assert(trained.Success
               && trained.Model?.FeatureSchemaVersion == 10
               && trained.Model.DecisionProfile == "balanced"
               && trained.Model.FeatureMinimums.Count > 0
               && trained.Model.CategoryObservationCounts.Count > 0,
            "in-process trainer produces a contextual candidate with applicability metadata");
        var gatedTraining = CombatResidualTrainer.Train(
            new[] { humanSample },
            "balanced",
            new CombatResidualTrainingOptions
            {
                PresetId = "steady",
                Epochs = 80,
                LearningRate = 0.03d,
                L2 = 0.003d,
                MaximumCorrection = 0.75d,
                MinimumPreferencePairs = 2,
                MinimumCategoryObservations = 10
            });
        Assert(!gatedTraining.Success
               && gatedTraining.PreferencePairCount == 1
               && gatedTraining.Message.Contains("最低要求 2", StringComparison.Ordinal),
            "training options reject undersized preference sets before producing a model");
        var configuredTraining = CombatResidualTrainer.Train(
            new[] { humanSample },
            "balanced",
            new CombatResidualTrainingOptions
            {
                PresetId = "steady",
                Epochs = 80,
                LearningRate = 0.03d,
                L2 = 0.003d,
                MaximumCorrection = 0.75d,
                MinimumPreferencePairs = 1,
                MinimumCategoryObservations = 10
            });
        Assert(configuredTraining.Success
               && configuredTraining.Model?.MaximumCorrection == 0.75d
               && configuredTraining.Model.TrainingPreset == "steady"
               && configuredTraining.Model.MinimumCategoryObservations == 10d
               && configuredTraining.Model.TrainingParameters["epochs"] == 80d
               && configuredTraining.Model.TrainingParameters["randomSeed"] == 7d,
            "training options are bounded and persisted in the candidate model metadata");
        Assert(configuredTraining.Model!.Metrics["battleSessionCount"] == 1d
               && configuredTraining.Model.Metrics["groupedValidationAccuracy"] == 0d,
            "grouped validation refuses to report in-sample accuracy for one battle");
        var secondBattleHumanSample = CombatTrainingSampleBuilder.Create(
            state,
            afterState,
            combatDecision,
            3,
            44,
            CombatActionTransactionState.Completed.ToString(),
            "second battle human settled",
            terminal: false,
            gameBuild: "test-game",
            sharedBuild: "test-shared",
            demonstrator: "human",
            recommendedCandidateId: "guard",
            policyVisibleToHuman: false);
        secondBattleHumanSample.BattleSessionId = 44;
        var groupedTraining = CombatResidualTrainer.Train(
            new[] { humanSample, secondBattleHumanSample },
            "balanced");
        Assert(groupedTraining.Success
               && groupedTraining.Model?.Metrics["battleSessionCount"] == 2d
               && groupedTraining.Model.Metrics["groupedValidationAccuracy"] >= 0d
               && groupedTraining.Model.Metrics["groupedValidationAccuracy"] <= 1d,
            "residual validation holds out complete battle sessions");
        var wrongProfileTraining = CombatResidualTrainer.Train(new[] { humanSample }, "defensive");
        Assert(!wrongProfileTraining.Success && wrongProfileTraining.PreferencePairCount == 0,
            "training keeps decision profiles separate without gating on MOD identity");
        var guidanceTraining = CombatSearchGuidanceTrainer.Train(
            new[] { trainingSample, humanSample },
            "balanced",
            rounds: 12,
            learningRate: 0.08d);
        Assert(guidanceTraining.Success
               && guidanceTraining.Model != null
               && guidanceTraining.Model.Policy.Trees.Count > 0
               && !double.IsNaN(guidanceTraining.Model.Value.Bias)
               && !guidanceTraining.Model.RiskTrained
               && guidanceTraining.Model.Risk.Trees.Count == 0,
            "search guidance trainer produces bounded policy and value tree ensembles");
        var guidanceModel = new BoundedTreeCombatSearchGuidanceModel(guidanceTraining.Model!);
        Assert(!double.IsNaN(guidanceModel.PolicyLogit(originalFeatures))
               && guidanceModel.DeathRisk(humanSample.StateFeatures) == 0d,
            "untrained one-class terminal risk does not manufacture a death predictor");
        var incompatibleLegacySample = new CombatTrainingSample
        {
            ModelProtocol = "aura.combat-ai.sample.v3",
            FeatureSchemaVersion = 3,
            CompletionState = "Completed",
            DecisionProfile = "balanced",
            BattleSessionId = 77,
            StateFeatures = new Dictionary<string, double>
            {
                ["playerHp"] = 20d,
                ["playerMaxHp"] = 30d,
                ["playerDefend"] = 0d,
                ["expectedBlockableDamage"] = 0d,
                ["power"] = 2d,
                ["maxPower"] = 3d,
                ["handCount"] = 2d
            },
            Selection = new CombatTrainingSelectionTrace
            {
                ExecutedCandidateId = "legacy-defend"
            }
        };
        var legacyContext = CombatResidualTrainer.ContextualFeatures(
            incompatibleLegacySample,
            new CombatTrainingCandidate
            {
                ActionKind = "PlayCard",
                Semantics = new CombatActionSemantics { Defend = 6d }
            });
        Assert(legacyContext["wastedDefend"] == 6d
               && legacyContext["semanticConfidence"] == 1d
               && legacyContext["categoryDefend"] == 1d,
            "context reconstruction remains deterministic for isolated diagnostics");
        var incompatibleTraining = CombatResidualTrainer.Train(
            new[] { incompatibleLegacySample },
            "balanced");
        Assert(!CombatTrainingProtocol.IsCompatible(incompatibleLegacySample)
               && incompatibleTraining.CompletedSampleCount == 0
               && !incompatibleTraining.Success
               && CombatLiveEpisodeAssembler.Assemble(
                   new[] { incompatibleLegacySample }).Count == 0,
            "legacy omniscient samples are rejected at every player-equivalent training ingress");

        var forwardRoot = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 70,
                CurrentHp = 20,
                MaxHp = 30
            },
            CurrentPower = 0,
            MaxPower = 3,
            HandCount = 9
        };
        var forwardState = CombatForwardModel.Create(forwardRoot, 4);
        var reductionAction = new CombatActionObservation
        {
            CandidateId = "reduce-three",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { CostReduction = 3d }
        };
        forwardState = CombatForwardModel.Apply(
            forwardState,
            reductionAction,
            0,
            CombatForwardModel.Resolve(forwardRoot, reductionAction).Outcomes[0],
            new CombatDecisionProfile());
        var firstDiscounted = new CombatActionObservation
        {
            CandidateId = "cost-two-a",
            Kind = CombatActionKind.PlayCard,
            Cost = 2,
            Semantics = new CombatActionSemantics { Draw = 3d }
        };
        forwardState = CombatForwardModel.Apply(
            forwardState,
            firstDiscounted,
            1,
            CombatForwardModel.Resolve(forwardRoot, firstDiscounted).Outcomes[0],
            new CombatDecisionProfile());
        Assert(forwardState.CostReduction == 1
               && forwardState.Power == 0
               && forwardState.HandCount == 10,
            "forward model consumes cost reduction by base cost and applies the hand limit");
        var secondDiscounted = new CombatActionObservation
        {
            CandidateId = "cost-two-b",
            Kind = CombatActionKind.PlayCard,
            Cost = 2
        };
        Assert(CombatForwardModel.EffectiveCost(forwardState, secondDiscounted) == 1,
            "surplus cost reduction cannot make every later card free");
        var setEnergyRoot = new CombatStateObservation
        {
            Player = new CombatUnitObservation { RuntimeId = 71, CurrentHp = 20, MaxHp = 20 },
            CurrentPower = 1,
            MaxPower = 4
        };
        var setEnergyAction = new CombatActionObservation
        {
            CandidateId = "restore-energy",
            SourceId = "timekeeper_2",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { RestoreEnergyToMaximum = true }
        };
        var setEnergyState = CombatForwardModel.Create(setEnergyRoot, 1);
        setEnergyState = CombatForwardModel.Apply(
            setEnergyState,
            setEnergyAction,
            0,
            CombatForwardModel.Resolve(setEnergyRoot, setEnergyAction).Outcomes[0],
            new CombatDecisionProfile());
        Assert(setEnergyState.Power == 4,
            "authoritative set-energy semantics restore the simulated resource to its maximum");

        var forwardRetrievalRoot = new CombatStateObservation
        {
            Player = new CombatUnitObservation { RuntimeId = 72, CurrentHp = 20, MaxHp = 20 },
            HandCount = 1,
            HandCardIds = { "timekeeper_18" },
            CardTagsById =
            {
                ["timekeeper_18"] = new List<string> { "Froze" }
            }
        };
        var forwardRetrievalAction = new CombatActionObservation
        {
            CandidateId = "retrieve-self",
            SourceId = "timekeeper_18",
            Kind = CombatActionKind.PlayCard,
            Features = { ["cardBaseCost"] = 0d },
            Semantics = new CombatActionSemantics
            {
                CardRetrievals =
                {
                    new CombatCardRetrievalSemantic
                    {
                        SourceZone = CombatCardZoneKind.DiscardPile,
                        DestinationZone = CombatCardZoneKind.Hand,
                        Amount = 1,
                        RequiredCardTag = "Froze"
                    }
                }
            }
        };
        var forwardRetrievalState = CombatForwardModel.Create(forwardRetrievalRoot, 1);
        forwardRetrievalState = CombatForwardModel.Apply(
            forwardRetrievalState,
            forwardRetrievalAction,
            0,
            CombatForwardModel.Resolve(
                forwardRetrievalRoot,
                forwardRetrievalAction).Outcomes[0],
            new CombatDecisionProfile());
        Assert(forwardRetrievalState.HandCardIds.Contains("timekeeper_18")
               && forwardRetrievalState.HandCount == 1
               && forwardRetrievalState.UseCount(0) == 0,
            "generic tagged retrieval moves the real card between zones and re-enables a retrieved card for loop analysis");

        var chaosRoot = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 73,
                CurrentHp = 20,
                MaxHp = 20,
                Statuses =
                {
                    new CombatStatusObservation { StatusId = "buff_chaos", DisplayName = "混乱" }
                }
            },
            CurrentPower = 4,
            MaxPower = 4,
            HandCount = 2,
            Features = { ["cardCostMultiplier"] = 1d }
        };
        var chaosAction = new CombatActionObservation
        {
            CandidateId = "chaos-card-a",
            SourceId = "chaos-card-a",
            Kind = CombatActionKind.PlayCard,
            Cost = 2,
            Features =
            {
                ["cardBaseCost"] = 2d,
                ["cardCostCap"] = 4d
            }
        };
        var chaosFollowUp = new CombatActionObservation
        {
            CandidateId = "chaos-card-b",
            SourceId = "chaos-card-b",
            Kind = CombatActionKind.PlayCard,
            Cost = 2,
            Features =
            {
                ["cardBaseCost"] = 2d,
                ["cardCostCap"] = 4d
            }
        };
        var chaosModel = CombatForwardModel.Resolve(chaosRoot, chaosAction);
        var chaosHashes = new List<ulong>();
        var chaosCosts = chaosModel.Outcomes
            .Select(outcome =>
            {
                var branch = CombatForwardModel.Apply(
                    CombatForwardModel.Create(chaosRoot, 2),
                    chaosAction,
                    0,
                    outcome,
                    new CombatDecisionProfile());
                chaosHashes.Add(branch.Hash());
                return CombatForwardModel.EffectiveCost(branch, chaosFollowUp);
            })
            .OrderBy(value => value)
            .ToArray();
        Assert(chaosModel.Outcomes.Count == 3
               && chaosCosts.SequenceEqual(new[] { 0, 2, 4 })
               && chaosHashes.Distinct().Count() == 3,
            "chaos branches the post-action card-cost multiplier and recomputes later card costs in the search tree");
        var multiplierRoot = new CombatStateObservation
        {
            Player = new CombatUnitObservation { RuntimeId = 75, CurrentHp = 20, MaxHp = 20 },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 76,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            }
        };
        var multiplierState = CombatForwardModel.Create(multiplierRoot, 2);
        var multiplierSetup = new CombatActionObservation
        {
            CandidateId = "element-setup",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics { DamageMultiplierGain = 0.5d }
        };
        multiplierState = CombatForwardModel.Apply(
            multiplierState,
            multiplierSetup,
            0,
            CombatForwardModel.Resolve(multiplierRoot, multiplierSetup).Outcomes[0],
            new CombatDecisionProfile());
        var multipliedAttack = new CombatActionObservation
        {
            CandidateId = "multiplied-attack",
            Kind = CombatActionKind.PlayCard,
            TargetRuntimeId = 76,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 8d }
        };
        multiplierState = CombatForwardModel.Apply(
            multiplierState,
            multipliedAttack,
            1,
            CombatForwardModel.Resolve(multiplierRoot, multipliedAttack).Outcomes[0],
            new CombatDecisionProfile());
        Assert(multiplierState.Enemies[0].Hp == 8,
            "typed setup state changes the simulated value of later damage");
        var selfAttritionAction = new CombatActionObservation
        {
            CandidateId = "self-attrition",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics
            {
                Damage = 2d,
                SelfHpLoss = 3d,
                EndOfCycleSelfHpLoss = 1d
            }
        };
        var selfAttritionState = CombatForwardModel.Apply(
            CombatForwardModel.Create(multiplierRoot, 1),
            selfAttritionAction,
            0,
            CombatForwardModel.Resolve(multiplierRoot, selfAttritionAction).Outcomes[0],
            new CombatDecisionProfile());
        Assert(selfAttritionState.PlayerHp == 16,
            "forward model charges immediate and end-of-cycle self hp loss");
        var loopStart = new CombatSimulationState
        {
            PlayerHp = 20,
            PlayerMaxHp = 20,
            Power = 3,
            MaxPower = 3,
            HandCount = 2,
            Enemies =
            [
                new CombatSimulationUnit
                {
                    RuntimeId = 751,
                    Hp = 20,
                    MaxHp = 20
                }
            ]
        };
        var safeLoopEnd = loopStart.Clone();
        safeLoopEnd.Enemies[0].Hp = 15;
        var fakeLoopEnd = safeLoopEnd.Clone();
        fakeLoopEnd.PlayerHp = 4;
        var limitedLoopEnd = loopStart.Clone();
        limitedLoopEnd.Enemies[0].Hp = 19;
        limitedLoopEnd.Enemies[0].Features["damageLimitActive"] = 1d;
        var controlLoopEnd = loopStart.Clone();
        var shieldControlLoopEnd = limitedLoopEnd.Clone();
        shieldControlLoopEnd.Enemies[0].Hp = 20;
        shieldControlLoopEnd.PlayerDefend = 12;
        var stackingControlLoopEnd = limitedLoopEnd.Clone();
        stackingControlLoopEnd.Enemies[0].Hp = 20;
        stackingControlLoopEnd.Features["status:buff_elements"] = 5d;
        var drainingResourceLoopStart = loopStart.Clone();
        drainingResourceLoopStart.Features["status:buff_revelation"] = 2d;
        var drainingResourceLoopEnd = drainingResourceLoopStart.Clone();
        drainingResourceLoopEnd.Features["status:buff_revelation"] = 1d;
        var monotonicCycleStart = loopStart.Clone();
        var monotonicCycleEnd = monotonicCycleStart.Clone();
        monotonicCycleEnd.PlayerDefend = 25;
        monotonicCycleEnd.SetupValue = 3d;
        monotonicCycleEnd.Features["status:buff_elements"] = 7d;
        monotonicCycleEnd.Power = 8;
        monotonicCycleEnd.TurnActionsTaken = 12;
        monotonicCycleEnd.TurnEnergySpent = 9;
        var drainingEnergyLoopEnd = loopStart.Clone();
        drainingEnergyLoopEnd.Power = 2;
        var growingEnergyLoopEnd = loopStart.Clone();
        growingEnergyLoopEnd.Power = 6;
        var growingEnergyAssessment = CombatLoopSafetyAnalyzer.Analyze(
            loopStart,
            growingEnergyLoopEnd,
            new CombatDecisionProfile());
        Assert(CombatLoopSafetyAnalyzer.Analyze(
                   loopStart,
                   safeLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.CertifiedLethal
               && CombatLoopSafetyAnalyzer.Analyze(
                   loopStart,
                   fakeLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.Fake
               && CombatLoopSafetyAnalyzer.Analyze(
                   loopStart,
                   limitedLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.Blocked
               && CombatLoopSafetyAnalyzer.Analyze(
                   loopStart,
                   controlLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.SustainableControl
               && CombatLoopSafetyAnalyzer.Analyze(
                   loopStart,
                   shieldControlLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.SustainableControl
               && CombatLoopSafetyAnalyzer.Analyze(
                   loopStart,
                   stackingControlLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.SustainableControl
               && CombatLoopSafetyAnalyzer.Analyze(
                   drainingResourceLoopStart,
                   drainingResourceLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.Fake
               && CombatLoopSafetyAnalyzer.Analyze(
                   loopStart,
                   drainingEnergyLoopEnd,
                   new CombatDecisionProfile()).Classification
               == CombatLoopClassification.Fake
               && growingEnergyAssessment.Classification
               == CombatLoopClassification.SustainableControl
               && growingEnergyAssessment.EnergyDelta == 3
               && monotonicCycleStart.CycleHash() == monotonicCycleEnd.CycleHash(),
            "structural loop safety ignores growing energy and counters, permits energy growth, and rejects finite energy loss");

        var threatRoot = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 80,
                CurrentHp = 5,
                MaxHp = 20
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 81,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 4,
                    MaxHp = 4
                }
            },
            Threat = new CombatThreatForecast
            {
                CurrentIntentKnown = true,
                Intents =
                {
                    new CombatIntentObservation
                    {
                        SourceRuntimeId = 81,
                        Kind = CombatIntentKind.Attack,
                        Probability = 1d,
                        BlockableDamage = 10d
                    }
                }
            }
        };
        var killThreat = new CombatActionObservation
        {
            CandidateId = "kill-threat",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 801,
            TargetRuntimeId = 81,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 4d }
        };
        var threatState = CombatForwardModel.Create(threatRoot, 1);
        threatState = CombatForwardModel.Apply(
            threatState,
            killThreat,
            0,
            CombatForwardModel.Resolve(threatRoot, killThreat).Outcomes[0],
            new CombatDecisionProfile());
        Assert(threatState.AllEnemiesDefeated
               && threatState.ActiveBlockableThreat(1d) == 0d
               && threatState.EvaluateLeaf(new CombatDecisionProfile()).DeathRisk == 0d,
            "defeated enemies no longer contribute incoming threat");

        var selectiveCloneSource = new CombatSimulationState
        {
            PlayerRuntimeId = 82,
            PlayerHp = 20,
            PlayerMaxHp = 20,
            HandCount = 1,
            HandCardValues = { 1d },
            DrawPileValues = { 2d },
            DiscardPileValues = { 3d },
            Features = { ["drawPerTurn"] = 1d },
            Enemies =
            [
                new CombatSimulationUnit { RuntimeId = 83, Hp = 10, MaxHp = 10 }
            ],
            UsedActionWords = new ulong[1]
        };
        var skillAction = new CombatActionObservation
        {
            CandidateId = "allocation-skill",
            Kind = CombatActionKind.UseSkill,
            TargetRuntimeId = 83,
            TargetKind = CombatTargetKind.Enemy,
            Semantics = new CombatActionSemantics { Damage = 1d }
        };
        var selectiveCloneResult = CombatForwardModel.Apply(
            selectiveCloneSource,
            skillAction,
            0,
            CombatForwardModel.Resolve(root: threatRoot, action: skillAction).Outcomes[0],
            new CombatDecisionProfile());
        Assert(ReferenceEquals(
                   selectiveCloneSource.HandCardValues,
                   selectiveCloneResult.HandCardValues)
               && ReferenceEquals(
                   selectiveCloneSource.DrawPileValues,
                   selectiveCloneResult.DrawPileValues)
               && ReferenceEquals(
                   selectiveCloneSource.Features,
                   selectiveCloneResult.Features)
               && !ReferenceEquals(
                   selectiveCloneSource.Enemies,
                   selectiveCloneResult.Enemies)
               && selectiveCloneSource.Enemies[0].Hp == 10
               && selectiveCloneResult.Enemies[0].Hp == 9,
            "forward transitions share immutable card/feature buffers while isolating mutable combat units");
        var playAction = new CombatActionObservation
        {
            CandidateId = "allocation-card",
            Kind = CombatActionKind.PlayCard,
            SourceId = "allocation-card"
        };
        var playCloneResult = CombatForwardModel.Apply(
            selectiveCloneSource,
            playAction,
            0,
            CombatForwardModel.Resolve(threatRoot, playAction).Outcomes[0],
            new CombatDecisionProfile());
        Assert(!ReferenceEquals(
                   selectiveCloneSource.HandCardValues,
                   playCloneResult.HandCardValues)
               && !ReferenceEquals(
                   selectiveCloneSource.DiscardPileValues,
                   playCloneResult.DiscardPileValues)
               && selectiveCloneSource.HandCardValues.Count == 1
               && selectiveCloneSource.DiscardPileValues.Count == 1
               && playCloneResult.HandCardValues.Count == 0
               && playCloneResult.DiscardPileValues.Count == 2,
            "card-pile mutations use copy-on-write and never change the parent search node");

        var coverageProfile = new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 1,
            SearchNodeBudget = 512,
            SearchMaxPly = 4
        };
        var coverageState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 90,
                CurrentHp = 20,
                MaxHp = 20
            },
            CurrentPower = 1,
            MaxPower = 1,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 91,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "coverage-a",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 901,
                    Cost = 1,
                    TargetRuntimeId = 91,
                    TargetKind = CombatTargetKind.Enemy,
                    Semantics = new CombatActionSemantics { Damage = 2d }
                },
                new CombatActionObservation
                {
                    CandidateId = "coverage-b",
                    Kind = CombatActionKind.PlayCard,
                    RuntimeId = 902,
                    Cost = 1,
                    TargetRuntimeId = 91,
                    TargetKind = CombatTargetKind.Enemy,
                    Semantics = new CombatActionSemantics { Damage = 3d }
                },
                new CombatActionObservation
                {
                    CandidateId = "coverage-end",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var coverageDecision = new CombatDecisionEngine().Choose(coverageState, coverageProfile);
        Assert(coverageDecision.SearchAlgorithm == "risk-aware-root-sampling-puct-mpc"
               && coverageDecision.SearchSimulations >= 2
               && coverageDecision.Candidates
                   .Where(candidate => candidate.Action.Kind != CombatActionKind.EndTurn)
                   .All(candidate => candidate.PlanScore != 0d),
            "risk-aware root-sampling PUCT gives every legal root action search evidence");
        var guardedEndTurn = coverageDecision.Candidates.Single(candidate =>
            candidate.Action.Kind == CombatActionKind.EndTurn);
        Assert(coverageDecision.Action?.Kind != CombatActionKind.EndTurn
               && !guardedEndTurn.Legal
               && guardedEndTurn.Action.Features.TryGetValue(
                   CombatTurnFeatureNames.EndTurnSevereMistake,
                   out var severeEndTurn)
               && severeEndTurn == 1d
               && guardedEndTurn.SearchVisits == 0,
            "end-turn safety makes passing with playable cards and unused energy a non-searchable severe mistake");
        var deliberateEndTurnState = new CombatStateObservation
        {
            CurrentPower = 1,
            Features =
            {
                [CombatTurnFeatureNames.EndTurnPurposeValue] = 5d
            }
        };
        var deliberateEndTurnAssessment = CombatEndTurnSafety.Assess(
            deliberateEndTurnState,
            coverageDecision.Candidates,
            coverageProfile);
        Assert(deliberateEndTurnAssessment.HasDeliberatePurpose
               && deliberateEndTurnAssessment.Prohibited,
            "explicit end-of-round purpose cannot override a productive executable action");
        var saturatedCandidates = new List<CombatCandidateEvaluation>
        {
            new()
            {
                Legal = true,
                Action = new CombatActionObservation
                {
                    CandidateId = "saturated-defense",
                    Kind = CombatActionKind.PlayCard,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Defend = 5d },
                    Features =
                    {
                        ["immediateDefend"] = 0d,
                        ["effectiveDurabilityDamage"] = 0d,
                        ["effectiveHeal"] = 0d,
                        ["effectiveDraw"] = 0d,
                        ["marginalSetupValue"] = 0d
                    }
                }
            }
        };
        var saturatedEndTurnAssessment = CombatEndTurnSafety.Assess(
            deliberateEndTurnState,
            saturatedCandidates,
            coverageProfile);
        Assert(!saturatedEndTurnAssessment.Prohibited
               && saturatedEndTurnAssessment.UnusedEnergy == 1
               && saturatedEndTurnAssessment.AvoidableUnusedEnergy == 0,
            "unused energy is rational when every recognized action is saturated");
        var unknownPlayable = new CombatCandidateEvaluation
        {
            Legal = true,
            Action = new CombatActionObservation
            {
                CandidateId = "unknown-playable",
                Kind = CombatActionKind.PlayCard,
                Cost = 1
            }
        };
        Assert(CombatActionProductivity.Assess(
                   deliberateEndTurnState,
                   unknownPlayable).Productive,
            "unknown playable non-curse cards conservatively outrank end turn");
        unknownPlayable.Action.Features["curse"] = 1d;
        Assert(!CombatActionProductivity.Assess(
                deliberateEndTurnState,
                unknownPlayable).Productive,
            "curse cards are excluded from the end-turn productivity gate");

        Assert(CombatTurnTransitionRules.NextTurnPower(2, 3) == 3
               && CombatTurnTransitionRules.NextTurnPower(5, 3) == 5
               && Math.Abs(
                   CombatTurnTransitionRules.EnergyCarryOpportunityCost(
                       3,
                       3,
                       2,
                       0)) < 0.000001d
               && Math.Abs(
                   CombatTurnTransitionRules.EnergyCarryOpportunityCost(
                       5,
                       3,
                       2,
                       0) - 2d) < 0.000001d,
            "shared turn rules refill energy at or below the cap and price only banked surplus above it");
        var bankedPowerState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 910,
                CurrentHp = 20,
                MaxHp = 20
            },
            CurrentPower = 5,
            MaxPower = 3,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 911,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            }
        };
        var lowYieldSurplusAction = new CombatCandidateEvaluation
        {
            Legal = true,
            Action = new CombatActionObservation
            {
                CandidateId = "surplus-low-yield",
                Kind = CombatActionKind.PlayCard,
                Cost = 1,
                TargetRuntimeId = 911,
                Semantics = new CombatActionSemantics { Damage = 1d },
                Features = { ["effectiveDurabilityDamage"] = 1d }
            }
        };
        var highYieldSurplusAction = new CombatCandidateEvaluation
        {
            Legal = true,
            Action = new CombatActionObservation
            {
                CandidateId = "surplus-high-yield",
                Kind = CombatActionKind.PlayCard,
                Cost = 1,
                TargetRuntimeId = 911,
                Semantics = new CombatActionSemantics { Damage = 2d },
                Features = { ["effectiveDurabilityDamage"] = 2d }
            }
        };
        Assert(!CombatActionProductivity.Assess(
                   bankedPowerState,
                   lowYieldSurplusAction).Productive
               && CombatActionProductivity.Assess(
                   bankedPowerState,
                   highYieldSurplusAction).Productive,
            "end-turn productivity compares action value with the next-turn cost of spending banked surplus energy");
        var lethalProjectionState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 912,
                CurrentHp = 4,
                MaxHp = 20
            },
            CurrentPower = 1,
            MaxPower = 1,
            HandCount = 2,
            HandCardIds = { "guard", "keep" },
            RetainedHandCardIds = { "keep" },
            DeckKnowledge = new CombatDeckKnowledge
            {
                DrawPileCount = 0,
                DiscardPileCount = 2
            },
            Threat = new CombatThreatForecast
            {
                ExpectedBlockableDamage = 5d,
                CurrentIntentKnown = true,
                Confidence = 1d
            },
            Features =
            {
                ["handLimit"] = 10d,
                ["drawPerTurn"] = 5d,
                [CombatTurnFeatureNames.UnknownLifecycleEffectCount] = 1d
            }
        };
        var lifeSavingGuard = new CombatCandidateEvaluation
        {
            Legal = true,
            Action = new CombatActionObservation
            {
                CandidateId = "life-saving-guard",
                Kind = CombatActionKind.PlayCard,
                Cost = 1,
                Semantics = new CombatActionSemantics { Defend = 5d },
                Features = { ["immediateDefend"] = 5d }
            }
        };
        var lethalEndTurnAssessment = CombatEndTurnSafety.Assess(
            lethalProjectionState,
            new[] { lifeSavingGuard },
            new CombatDecisionProfile());
        Assert(lethalEndTurnAssessment.Prohibited
               && lethalEndTurnAssessment.AvoidableLethal
               && lethalEndTurnAssessment.Verdict
               == CombatEndTurnVerdict.BlockedLethal
               && lethalEndTurnAssessment.Projection.UnretainedHandCount == 1
               && lethalEndTurnAssessment.Projection.RetainedHandCount == 1
               && lethalEndTurnAssessment.Projection.ReshuffleDuringNextDraw
               && lethalEndTurnAssessment.Projection.UnretainedReturnDelayTurns == 0,
            "end-turn projection combines retain/discard/reshuffle rules with avoidable lethal intent");
        var certifiedCycleAction = new CombatCandidateEvaluation
        {
            Legal = true,
            Action = new CombatActionObservation
            {
                CandidateId = "certified-cycle-connector",
                Kind = CombatActionKind.PlayCard,
                Cost = 0,
                Features =
                {
                    ["strategyInfinite"] = 1d,
                    ["strategyExecutable"] = 1d,
                    ["strategyDeterministic"] = 1d
                }
            }
        };
        var certifiedCycleAssessment = CombatEndTurnSafety.Assess(
            new CombatStateObservation
            {
                Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 },
                CurrentPower = 3,
                MaxPower = 3
            },
            new[] { certifiedCycleAction },
            new CombatDecisionProfile());
        Assert(certifiedCycleAssessment.Prohibited
               && certifiedCycleAssessment.Verdict
               == CombatEndTurnVerdict.BlockedCycle
               && certifiedCycleAssessment.CertifiedCycleCount == 1,
            "deterministic executable infinite components block end turn even when the connector has no immediate numeric payoff");

        var terminalCardState = new CombatStateObservation
        {
            Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 },
            CurrentPower = 2,
            MaxPower = 2,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 701,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "terminal-card",
                    SourceId = "terminal-card",
                    Kind = CombatActionKind.PlayCard,
                    Cost = 1,
                    Semantics = new CombatActionSemantics
                    {
                        Damage = 3d,
                        EndsTurn = true
                    }
                },
                new CombatActionObservation
                {
                    CandidateId = "continue-attack",
                    SourceId = "continue-attack",
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 701,
                    TargetKind = CombatTargetKind.Enemy,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Damage = 4d }
                },
                new CombatActionObservation
                {
                    CandidateId = "terminal-end",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var terminalCardDecision = new CombatDecisionEngine(
                useRuntimeRegistries: false)
            .Choose(terminalCardState, new CombatDecisionProfile());
        var terminalCardEvaluation = terminalCardDecision.Candidates.Single(item =>
            item.Action.CandidateId == "terminal-card");
        Assert(terminalCardDecision.Action?.CandidateId == "continue-attack"
               && !terminalCardEvaluation.Legal
               && terminalCardEvaluation.Action.Features.TryGetValue(
                   CombatTurnFeatureNames.EndTurnDominated,
                   out var terminalDominated)
               && terminalDominated == 1d,
            "cards that end the turn pass through the same continuation safety gate as the end-turn button");

        var damageToBlockState = new CombatStateObservation
        {
            Player = new CombatUnitObservation { CurrentHp = 20, MaxHp = 20 },
            CurrentPower = 2,
            MaxPower = 2,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 702,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "damage-to-block-setup",
                    SourceId = "damage-to-block-setup",
                    Kind = CombatActionKind.PlayCard,
                    Cost = 1,
                    Semantics = new CombatActionSemantics
                    {
                        DamageToBlockSetup = true,
                        Buff = 1d
                    }
                },
                new CombatActionObservation
                {
                    CandidateId = "damage-payoff",
                    SourceId = "damage-payoff",
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 702,
                    TargetKind = CombatTargetKind.Enemy,
                    Cost = 1,
                    Semantics = new CombatActionSemantics { Damage = 5d }
                },
                new CombatActionObservation
                {
                    CandidateId = "damage-to-block-end",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var damageToBlockDecision = new CombatDecisionEngine(
                useRuntimeRegistries: false)
            .Choose(damageToBlockState, new CombatDecisionProfile());
        Assert(damageToBlockDecision.Action?.CandidateId == "damage-to-block-setup"
               && damageToBlockDecision.SearchAlgorithm == "dominance",
            "damage-recording block setup strictly precedes an affordable damage payoff without card-name rules");

        var crossDefinitionPackage = new CombatKnowledgePackage
        {
            OwnerId = "tests",
            PackageId = "cross-definition-semantics",
            GameBuild = "test",
            SourceHash = "test-hash",
            Actions =
            {
                new CombatKnowledgeActionDefinition
                {
                    SourceId = "setup-from-status",
                    Fidelity = CombatKnowledgeFidelity.Authoritative,
                    Semantics = new CombatActionSemantics
                    {
                        StateChanges = { ["status:damage-recorder"] = 1d }
                    },
                    TableFields = { ["UseScript"] = "ChangeRound();" }
                }
            },
            Statuses =
            {
                new CombatKnowledgeStatusDefinition
                {
                    StatusId = "damage-recorder",
                    Fidelity = CombatKnowledgeFidelity.Authoritative,
                    Triggers = { "Hurt", "EndRound" },
                    Operations =
                    {
                        new CombatKnowledgeOperation { Api = "ChangeDefence" }
                    }
                }
            }
        };
        using (CombatKnowledgeRegistry.RegisterPackage(
                   crossDefinitionPackage,
                   out var crossDefinitionErrors))
        {
            var foundCrossDefinition = CombatKnowledgeRegistry.TryDescribeAction(
                new CombatActionObservation { SourceId = "setup-from-status" },
                out var crossDefinitionSemantics,
                out _,
                out _);
            Assert(crossDefinitionErrors.Count == 0
                   && foundCrossDefinition
                   && crossDefinitionSemantics.EndsTurn
                   && crossDefinitionSemantics.DamageToBlockSetup,
                "knowledge registration derives terminal and damage-to-block setup semantics from scripts and status lifecycle definitions");
        }

        var limitedDamageState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 920,
                CurrentHp = 20,
                MaxHp = 20
            },
            CurrentPower = 1,
            MaxPower = 1,
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 921,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 10,
                    MaxHp = 10,
                    Defend = 3,
                    Features =
                    {
                        [CombatDamageLimitPolicy.ActiveFeature] = 1d,
                        [CombatDamageLimitPolicy.RemainingFeature] = 2d
                    }
                }
            }
        };
        var limitedAttack = new CombatActionObservation
        {
            CandidateId = "limited-attack",
            Kind = CombatActionKind.PlayCard,
            RuntimeId = 922,
            TargetRuntimeId = 921,
            TargetKind = CombatTargetKind.Enemy,
            Cost = 1,
            Semantics = new CombatActionSemantics
            {
                Damage = 5d,
                TrueDamage = 4d
            }
        };
        var limitedProjection = CombatDamageLimitPolicy.Project(
            limitedDamageState,
            limitedAttack);
        var limitedForward = CombatForwardModel.Apply(
            CombatForwardModel.Create(limitedDamageState, 1),
            limitedAttack,
            0,
            CombatForwardModel.Resolve(
                limitedDamageState,
                limitedAttack).Outcomes[0],
            new CombatDecisionProfile());
        Assert(limitedProjection.BlockDamage == 3d
               && limitedProjection.HpDamage == 2d
               && limitedProjection.DurabilityDamage == 5d
               && limitedForward.Enemies[0].Defend == 0
               && limitedForward.Enemies[0].Hp == 8
               && limitedForward.Enemies[0].Features[
                   CombatDamageLimitPolicy.RemainingFeature] == 0d,
            "damage projection and forward simulation consume the enemy's remaining per-turn hp-damage budget");
        limitedDamageState.Enemies[0].Defend = 0;
        limitedDamageState.Enemies[0].Features[
            CombatDamageLimitPolicy.RemainingFeature] = 0d;
        limitedAttack.Features = CombatDecisionEngine.BuildFeatures(
            limitedDamageState,
            limitedAttack);
        var exhaustedAttackEvaluation = new CombatCandidateEvaluation
        {
            Action = limitedAttack,
            Legal = true
        };
        Assert(!CombatActionProductivity.Assess(
                limitedDamageState,
                exhaustedAttackEvaluation).Productive,
            "pure attacks stop blocking end turn once the enemy damage budget is exhausted");
        limitedAttack.Semantics.Draw = 1d;
        limitedAttack.Features = CombatDecisionEngine.BuildFeatures(
            limitedDamageState,
            limitedAttack);
        Assert(CombatActionProductivity.Assess(
                limitedDamageState,
                exhaustedAttackEvaluation).Productive,
            "an attack with an independent useful side effect remains productive after damage is capped");

        var noEffectBefore = CombatPlayerObservationBoundary.Normalize(
            limitedDamageState);
        var noEffectAfter = CombatPlayerObservationBoundary.Normalize(
            new CombatStateObservation
            {
                BattleSessionId = noEffectBefore.BattleSessionId,
                Player = new CombatUnitObservation
                {
                    RuntimeId = noEffectBefore.Player.RuntimeId,
                    CurrentHp = noEffectBefore.Player.CurrentHp,
                    MaxHp = noEffectBefore.Player.MaxHp,
                    Defend = noEffectBefore.Player.Defend
                },
                CurrentPower = noEffectBefore.CurrentPower,
                MaxPower = noEffectBefore.MaxPower,
                HandCount = noEffectBefore.HandCount,
                Enemies =
                {
                    new CombatUnitObservation
                    {
                        RuntimeId = 921,
                        Kind = CombatTargetKind.Enemy,
                        CurrentHp = 10,
                        MaxHp = 10,
                        Features =
                        {
                            [CombatDamageLimitPolicy.ActiveFeature] = 1d,
                            [CombatDamageLimitPolicy.RemainingFeature] = 0d
                        }
                    }
                },
                Actions = { limitedAttack }
            });
        noEffectBefore.Actions.Add(limitedAttack);
        noEffectBefore.Features[
            CombatTurnFeatureNames.ActionsTakenThisTurn] = 0d;
        noEffectAfter.Features[
            CombatTurnFeatureNames.ActionsTakenThisTurn] = 1d;
        Assert(!CombatActionSettlementPolicy.HasMeaningfulProgress(
                noEffectBefore,
                noEffectAfter,
                limitedAttack,
                out _),
            "transaction bookkeeping alone cannot make a no-effect game action settle successfully");

        var divineChoice = new CombatActionObservation
        {
            CandidateId = "skill:careercard_1:1:1",
            SourceId = CombatActionExecutionPolicy.DivineChoiceSourceId,
            Kind = CombatActionKind.UseSkill,
            RuntimeId = 1,
            TargetRuntimeId = 1,
            TargetKind = CombatTargetKind.Self,
            Legal = true
        };
        CombatStateObservation DivineChoiceState(
            int drawPileCount,
            int discardPileCount,
            int handCount,
            long battleSessionId = 77)
        {
            var result = new CombatStateObservation
            {
                BattleSessionId = battleSessionId,
                Sequence = 1,
                Player = new CombatUnitObservation
                {
                    RuntimeId = 1,
                    Kind = CombatTargetKind.Self,
                    CurrentHp = 20,
                    MaxHp = 20
                },
                HandCount = handCount,
                CurrentPower = 3,
                MaxPower = 3,
                IsPlayerActionWindow = true,
                DeckKnowledge = new CombatDeckKnowledge
                {
                    DrawPileCount = drawPileCount,
                    DiscardPileCount = discardPileCount
                },
                Actions = { divineChoice }
            };
            for (var index = 0; index < handCount; index++)
            {
                result.HandCardIds.Add("hand-" + index);
            }
            return CombatPlayerObservationBoundary.Normalize(result);
        }

        var emptyDivineChoiceState = DivineChoiceState(0, 5, 3);
        var fullHandDivineChoiceState = DivineChoiceState(3, 0, 10);
        var legalDivineChoiceState = DivineChoiceState(3, 0, 9);
        var expandedHandDivineChoiceState = DivineChoiceState(3, 0, 10);
        expandedHandDivineChoiceState.Features["handLimit"] = 99;
        Assert(!CombatActionExecutionPolicy.IsLiveEligible(
                   emptyDivineChoiceState,
                   emptyDivineChoiceState.Actions[0],
                   out var emptyDivineChoiceReason)
               && emptyDivineChoiceReason.Contains("draw pile", StringComparison.Ordinal)
               && !CombatActionExecutionPolicy.IsLiveEligible(
                   fullHandDivineChoiceState,
                   fullHandDivineChoiceState.Actions[0],
                   out var fullHandDivineChoiceReason)
               && fullHandDivineChoiceReason.Contains("hand slot", StringComparison.Ordinal)
               && CombatActionExecutionPolicy.IsLiveEligible(
                   legalDivineChoiceState,
                   legalDivineChoiceState.Actions[0],
                   out _)
               && CombatActionExecutionPolicy.IsLiveEligible(
                   expandedHandDivineChoiceState,
                   expandedHandDivineChoiceState.Actions[0],
                   out _),
            "divine choice live eligibility requires a draw-pile card and the runtime-configured free hand slot without counting discard cards");

        var unrelatedDivineChoiceAfter = DivineChoiceState(3, 0, 9);
        unrelatedDivineChoiceAfter.Enemies.Add(new CombatUnitObservation
        {
            RuntimeId = 2,
            Kind = CombatTargetKind.Enemy,
            CurrentHp = 8,
            MaxHp = 10
        });
        var settledDivineChoiceAfter = DivineChoiceState(2, 0, 10);
        settledDivineChoiceAfter.HandCardIds[9] = "chosen-card";
        Assert(!CombatActionSettlementPolicy.HasMeaningfulProgress(
                   legalDivineChoiceState,
                   unrelatedDivineChoiceAfter,
                   legalDivineChoiceState.Actions[0],
                   out _)
               && CombatActionSettlementPolicy.HasMeaningfulProgress(
                   legalDivineChoiceState,
                   settledDivineChoiceAfter,
                   legalDivineChoiceState.Actions[0],
                   out var divineChoiceSettlementReason)
               && divineChoiceSettlementReason.Contains(
                   "draw-pile card into hand",
                   StringComparison.Ordinal),
            "divine choice settlement ignores unrelated state changes and requires the card transfer postcondition");

        var sameEligibilityDivineChoiceState = DivineChoiceState(8, 2, 4);
        sameEligibilityDivineChoiceState.Player.CurrentHp = 7;
        var changedEligibilityDivineChoiceState = DivineChoiceState(0, 10, 4);
        var divineSuppressionKey =
            CombatActionExecutionPolicy.BuildFailureSuppressionKey(
                legalDivineChoiceState,
                legalDivineChoiceState.Actions[0]);
        Assert(divineSuppressionKey ==
               CombatActionExecutionPolicy.BuildFailureSuppressionKey(
                   sameEligibilityDivineChoiceState,
                   sameEligibilityDivineChoiceState.Actions[0])
               && divineSuppressionKey !=
               CombatActionExecutionPolicy.BuildFailureSuppressionKey(
                   changedEligibilityDivineChoiceState,
                   changedEligibilityDivineChoiceState.Actions[0]),
            "divine choice failure suppression follows only draw and hand eligibility changes instead of unrelated fingerprints");

        var divineDecision = new CombatDecision
        {
            HasAction = true,
            Action = legalDivineChoiceState.Actions[0]
        };
        Assert(CombatDecisionFreshnessPolicy.TryBindCurrent(
                   legalDivineChoiceState.BattleSessionId,
                   legalDivineChoiceState.Fingerprint,
                   legalDivineChoiceState,
                   divineDecision,
                   out var reboundDivineDecision,
                   out _)
               && reboundDivineDecision.Action == legalDivineChoiceState.Actions[0]
               && !CombatDecisionFreshnessPolicy.TryBindCurrent(
                   legalDivineChoiceState.BattleSessionId,
                   legalDivineChoiceState.Fingerprint,
                   fullHandDivineChoiceState,
                   divineDecision,
                   out _,
                   out _),
            "background decisions bind only to the unchanged battle observation and pass execution-time eligibility again");

        var isolatedSourceState = CombatPlayerObservationBoundary.Normalize(
            new CombatStateObservation
            {
                BattleSessionId = 88,
                Sequence = 1,
                Player = new CombatUnitObservation
                {
                    RuntimeId = 1,
                    Kind = CombatTargetKind.Self,
                    CurrentHp = 20,
                    MaxHp = 20
                },
                Enemies =
                {
                    new CombatUnitObservation
                    {
                        RuntimeId = 2,
                        Kind = CombatTargetKind.Enemy,
                        CurrentHp = 5,
                        MaxHp = 5
                    }
                },
                Actions =
                {
                    new CombatActionObservation
                    {
                        CandidateId = "isolated-reject",
                        SourceId = "isolated-reject",
                        Kind = CombatActionKind.PlayCard,
                        TargetRuntimeId = 2,
                        TargetKind = CombatTargetKind.Enemy,
                        Legal = true,
                        Semantics = new CombatActionSemantics { Damage = 5d }
                    },
                    new CombatActionObservation
                    {
                        CandidateId = "isolated-fallback",
                        SourceId = "isolated-fallback",
                        Kind = CombatActionKind.PlayCard,
                        TargetRuntimeId = 2,
                        TargetKind = CombatTargetKind.Enemy,
                        Legal = true,
                        Semantics = new CombatActionSemantics { Damage = 5d }
                    }
                }
            });
        var isolatedSourceEngine = new CombatDecisionEngine();
        CombatStateObservation isolatedPreparedState;
        CombatDecisionEngine isolatedWorker;
        using (CombatAiRegistry.RegisterPreflightRule(
                   "Tests",
                   "IsolatedWorkerPreflight",
                   new RejectCandidateRule("isolated-reject"),
                   100))
        {
            isolatedPreparedState =
                isolatedSourceEngine.PrepareStateForIsolatedWorker(
                    isolatedSourceState);
            isolatedWorker = isolatedSourceEngine.CreateIsolatedWorker(
                isolatedSourceEngine.SnapshotSimulationRulesForIsolatedWorker());
        }
        var isolatedWorkerDecision = Task.Run(() => isolatedWorker.Choose(
                isolatedPreparedState,
                new CombatDecisionProfile
                {
                    SearchBudgetMode = "fixed",
                    SearchSimulationBudget = 8,
                    SearchMinimumSimulations = 2,
                    SearchStabilityWindow = 2,
                    SearchStableChecks = 1,
                    SearchMaxPly = 3,
                    SearchNodeBudget = 256
                }))
            .GetAwaiter()
            .GetResult();
        Assert(!isolatedPreparedState.Actions.Single(action =>
                   action.CandidateId == "isolated-reject").Legal
               && isolatedWorkerDecision.Action?.CandidateId
               == "isolated-fallback",
            "isolated decision workers consume a main-thread-prepared snapshot without consulting mutable runtime registries");
        var earlyStopProfile = new CombatDecisionProfile
        {
            SearchBudgetMode = "fixed",
            SearchSimulationBudget = 128,
            SearchMinimumSimulations = 4,
            SearchStabilityWindow = 2,
            SearchStableChecks = 1,
            SearchNodeBudget = 512,
            SearchMaxPly = 4
        };
        var earlyStopDecision = new CombatDecisionEngine().Choose(coverageState, earlyStopProfile);
        Assert(earlyStopDecision.SearchStoppedEarly
               && earlyStopDecision.SearchSimulations < earlyStopProfile.SearchSimulationBudget,
            "risk-aware root-sampling PUCT stops when the root ranking and graph have stabilized");
        var sparseTailStatistics = new CombatSearchRiskStatistics();
        sparseTailStatistics.Record(-40d, 0d);
        var sparseTailEstimate = sparseTailStatistics.Estimate(0.1d);
        Assert(sparseTailEstimate.TailConfidence == 0d
               && sparseTailEstimate.EffectiveLowerTailMean == sparseTailEstimate.Mean,
            "tail-risk search shrinks a single return sample fully back to the mean");
        var highMeanStatistics = new CombatSearchRiskStatistics();
        var lowMeanStatistics = new CombatSearchRiskStatistics();
        for (var index = 0; index < 80; index++)
        {
            highMeanStatistics.Record(index < 8 ? 0d : 100d, 0d);
            lowMeanStatistics.Record(index < 8 ? 1d : 10d, 0d);
        }
        var rankingProfile = new CombatDecisionProfile
        {
            TailRiskPenalty = 0d,
            UncertaintyPenalty = 0d
        };
        var highMeanValue = CombatRiskAdjustedSearchValue.Calculate(
            highMeanStatistics.Estimate(0.1d),
            0d,
            rankingProfile);
        var lowMeanValue = CombatRiskAdjustedSearchValue.Calculate(
            lowMeanStatistics.Estimate(0.1d),
            0d,
            rankingProfile);
        Assert(highMeanStatistics.Estimate(0.1d).RawLowerTailMean
               < lowMeanStatistics.Estimate(0.1d).RawLowerTailMean
               && highMeanValue > lowMeanValue,
            "root ranking uses the configured mean-tail objective instead of lexicographic raw CVaR");

        var targetVariantState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 95,
                CurrentHp = 20,
                MaxHp = 20
            },
            Enemies =
            {
                new CombatUnitObservation { RuntimeId = 96, Kind = CombatTargetKind.Enemy, CurrentHp = 10, MaxHp = 10 },
                new CombatUnitObservation { RuntimeId = 97, Kind = CombatTargetKind.Enemy, CurrentHp = 10, MaxHp = 10 }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "same-card:96",
                    RuntimeId = 950,
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 96,
                    TargetKind = CombatTargetKind.Enemy,
                    Semantics = new CombatActionSemantics { Damage = 2d }
                },
                new CombatActionObservation
                {
                    CandidateId = "same-card:97",
                    RuntimeId = 950,
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 97,
                    TargetKind = CombatTargetKind.Enemy,
                    Semantics = new CombatActionSemantics { Damage = 2d }
                },
                new CombatActionObservation { CandidateId = "same-card-end", Kind = CombatActionKind.EndTurn }
            }
        };
        var targetVariantDecision = new CombatDecisionEngine().Choose(
            targetVariantState,
            new CombatDecisionProfile
            {
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 128,
                SearchNodeBudget = 512
            });
        Assert(targetVariantDecision.Plan.Count(step => step.CandidateId.StartsWith("same-card:", StringComparison.Ordinal)) <= 1,
            "target variants of one runtime card share a single-use group");

        var duplicateCardState = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 951,
                CurrentHp = 20,
                MaxHp = 20
            },
            HandCount = 2,
            HandCardIds = { "duplicate-strike", "duplicate-strike" },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 952,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 12,
                    MaxHp = 12
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "duplicate-strike:1",
                    SourceId = "duplicate-strike",
                    RuntimeId = 953,
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 952,
                    TargetKind = CombatTargetKind.Enemy,
                    Features = { ["handIndex"] = 0d },
                    Semantics = new CombatActionSemantics { Damage = 3d }
                },
                new CombatActionObservation
                {
                    CandidateId = "duplicate-strike:2",
                    SourceId = "duplicate-strike",
                    RuntimeId = 954,
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 952,
                    TargetKind = CombatTargetKind.Enemy,
                    Features = { ["handIndex"] = 1d },
                    Semantics = new CombatActionSemantics { Damage = 3d }
                },
                new CombatActionObservation
                {
                    CandidateId = "duplicate-end",
                    Kind = CombatActionKind.EndTurn
                }
            }
        };
        var duplicateCardDecision = new CombatDecisionEngine().Choose(
            duplicateCardState,
            new CombatDecisionProfile
            {
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 96,
                SearchMinimumSimulations = 16,
                SearchNodeBudget = 512,
                SearchMaxPly = 4
            });
        Assert(duplicateCardDecision.SearchCandidateCount
               < duplicateCardDecision.SearchOriginalCandidateCount
               && duplicateCardDecision.Plan.Count(step =>
                   step.SourceId == "duplicate-strike") == 2,
            "search candidate compression merges equivalent physical copies while preserving their total usable count"
            + $" (compressed={duplicateCardDecision.SearchCandidateCount}, original={duplicateCardDecision.SearchOriginalCandidateCount}, plan={string.Join(",", duplicateCardDecision.Plan.Select(step => step.CandidateId))})");

        var transpositionState = new CombatStateObservation
        {
            Player = new CombatUnitObservation { RuntimeId = 98, CurrentHp = 20, MaxHp = 20 },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 99,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 20,
                    MaxHp = 20
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "transpose-a",
                    RuntimeId = 980,
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 99,
                    TargetKind = CombatTargetKind.Enemy,
                    Semantics = new CombatActionSemantics { Damage = 1d }
                },
                new CombatActionObservation
                {
                    CandidateId = "transpose-b",
                    RuntimeId = 981,
                    Kind = CombatActionKind.PlayCard,
                    TargetRuntimeId = 99,
                    TargetKind = CombatTargetKind.Enemy,
                    Semantics = new CombatActionSemantics { Damage = 1d }
                },
                new CombatActionObservation { CandidateId = "transpose-end", Kind = CombatActionKind.EndTurn }
            }
        };
        var transpositionDecision = new CombatDecisionEngine().Choose(
            transpositionState,
            new CombatDecisionProfile
            {
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 256,
                SearchNodeBudget = 1024,
                SearchMaxPly = 4
            });
        Assert(transpositionDecision.SearchTranspositionHits > 0,
            "commutative action orders reuse a physical-state transposition node");

        var persistentShieldTurn = CombatForwardModel.ApplyEndTurn(
            new CombatSimulationState
            {
                PlayerHp = 30,
                PlayerMaxHp = 30,
                PlayerDefend = 12,
                Power = 0,
                MaxPower = 3,
                HandCount = 2,
                HandLimit = 5,
                HandCardValues = { 1d, 2d },
                DiscardPileValues = { 3d },
                DrawPileKnown = true,
                Features = { ["drawPerTurn"] = 2d },
                Enemies =
                [
                    new CombatSimulationUnit
                    {
                        RuntimeId = 1,
                        Hp = 20,
                        MaxHp = 20
                    }
                ],
                Threats =
                [
                    new CombatSimulationThreat
                    {
                        SourceRuntimeId = 1,
                        BlockableDamage = 7d,
                        Probability = 1d
                    }
                ]
            },
            new CombatDecisionProfile());
        Assert(persistentShieldTurn.PlayerHp == 30
               && persistentShieldTurn.PlayerDefend == 0
               && persistentShieldTurn.Power == 3
               && persistentShieldTurn.HandCount == 2
               && persistentShieldTurn.HandCardValues.Count == 2
               && persistentShieldTurn.DrawPileValues.Count == 1
               && persistentShieldTurn.DiscardPileValues.Count == 0
               && persistentShieldTurn.Threats.Length == 1
               && persistentShieldTurn.Threats[0].BlockableDamage > 0d,
            "end-turn baseline resolves enemy damage, clears shield, models card cycling, and projects next intent");
        var retainedCycleTurn = CombatForwardModel.ApplyEndTurn(
            new CombatSimulationState
            {
                PlayerHp = 20,
                PlayerMaxHp = 20,
                HandCount = 2,
                HandLimit = 5,
                HandCardValues = { 1d, 2d },
                RetainedHandCardValues = { 2d },
                DrawPileValues = { 4d, 5d },
                DrawPileKnown = true,
                Features = { ["drawPerTurn"] = 2d }
            },
            new CombatDecisionProfile());
        Assert(retainedCycleTurn.HandCount == 3
               && retainedCycleTurn.HandCardValues.Contains(2d)
               && retainedCycleTurn.RetainedHandCardValues.Count == 1
               && retainedCycleTurn.DiscardPileValues.SequenceEqual(new[] { 1d }),
            "end-turn cycle keeps retained cards while only unretained cards enter the discard pile");
        var lifecycleForwardTurn = CombatForwardModel.ApplyEndTurn(
            new CombatSimulationState
            {
                PlayerHp = 10,
                PlayerMaxHp = 20,
                PlayerDefend = 0,
                Power = 3,
                MaxPower = 3,
                HandLimit = 10,
                DrawPileKnown = true,
                Features =
                {
                    ["drawPerTurn"] = 0d,
                    [CombatTurnFeatureNames.EndTurnLifecycleHpLoss] = 1d,
                    [CombatTurnFeatureNames.EndTurnLifecycleDefend] = 3d,
                    [CombatTurnFeatureNames.StartTurnLifecycleHpLoss] = 1d,
                    [CombatTurnFeatureNames.StartTurnLifecycleHeal] = 2d,
                    [CombatTurnFeatureNames.StartTurnLifecycleDefend] = 4d
                },
                Enemies =
                [
                    new CombatSimulationUnit
                    {
                        RuntimeId = 2,
                        Hp = 10,
                        MaxHp = 10
                    }
                ],
                Threats =
                [
                    new CombatSimulationThreat
                    {
                        SourceRuntimeId = 2,
                        Probability = 1d,
                        BlockableDamage = 8d
                    }
                ]
            },
            new CombatDecisionProfile());
        Assert(lifecycleForwardTurn.PlayerHp == 5
               && lifecycleForwardTurn.PlayerDefend == 4
               && lifecycleForwardTurn.Power == 3,
            "forward end-turn transition resolves player end effects, enemy intent, reset, and next-turn start effects in order");

        using (CombatAiRegistry.RegisterEffectResolver(
                   "Tests",
                   "ChanceDamage",
                   new FixedEffectResolver("chance-action"),
                   100))
        {
            var chanceAction = new CombatActionObservation
            {
                CandidateId = "chance-action",
                Semantics = new CombatActionSemantics { RandomOutcome = true }
            };
            var chanceModel = CombatForwardModel.Resolve(coverageState, chanceAction);
            Assert(chanceModel.Outcomes.Count == 2
                   && Math.Abs(chanceModel.Outcomes.Sum(outcome => outcome.Probability) - 1d) < 0.000001d,
                "content effect resolvers provide normalized chance outcomes");
        }

    }
}
