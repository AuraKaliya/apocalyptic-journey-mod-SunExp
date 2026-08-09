using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationSemanticProbeResult
{
    public const string CurrentCanaryVersion =
        "causal-gross-semantic-pipeline-v9-decision-input-transition";

    public string Version { get; set; } =
        CombatPolicyValueProtocol.TrainingSemanticsVersion;

    public string CanaryVersion { get; set; } = CurrentCanaryVersion;

    public List<string> Errors { get; set; } = new();

    public bool Success => Errors.Count == 0;
}

public static class CombatFoundationSemanticProbe
{
    public static CombatFoundationSemanticProbeResult Validate(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        return Validate(campaign, ruleset, null, false);
    }

    public static CombatFoundationSemanticProbeResult Validate(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatSimulationEngine? engine,
        bool requireNativeProgramCanary = false)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));

        var result = new CombatFoundationSemanticProbeResult();
        var coverage = CombatSemanticCoverageAudit.Analyze(campaign, ruleset);
        foreach (var error in coverage.Errors)
        {
            result.Errors.Add("semantic coverage: " + error);
        }
        if (!coverage.Complete)
        {
            result.Errors.Add(
                "semantic coverage inventory contains unsupported effects: "
                + coverage.UnsupportedCount);
        }
        ValidateBladeAndShield(campaign, ruleset, result.Errors);
        ValidateResourceRecurrence(result.Errors);
        ValidateRetainAndReshuffle(result.Errors);
        ValidateDirectedRiskScenarios(result.Errors);
        if (engine != null)
        {
            ValidateSemanticAuditPipeline(
                campaign,
                ruleset,
                engine,
                result.Errors);
            if (requireNativeProgramCanary)
            {
                ValidateNativeProgramProvenance(
                    campaign,
                    ruleset,
                    engine,
                    result.Errors);
            }
        }
        return result;
    }

    private static void ValidateSemanticAuditPipeline(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatSimulationEngine engine,
        ICollection<string> errors)
    {
        foreach (var cardId in new[]
                 {
                     "card_1",
                     "card_2",
                     "card_3",
                     "burningcard_1",
                     "burningcard_2",
                     "card_4",
                     "card_10",
                     "blood_3",
                     "nocard_2",
                     "elementscard_9",
                     "timekeeper_11",
                     "Crowdfundingcard_48"
                 })
        {
            if (!ruleset.TryGetCard(cardId, out var card))
            {
                errors.Add(
                    "semantic audit canary card is missing: " + cardId);
                continue;
            }

            var scenario = CanaryScenario(campaign, ruleset);
            var before = CanaryState(card);
            var legal = engine.GetLegalPlayerActions(
                scenario,
                ruleset,
                before);
            var action = legal.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    cardId,
                    StringComparison.OrdinalIgnoreCase));
            if (action == null)
            {
                errors.Add(
                    "semantic audit canary action is not legal: " + cardId);
                continue;
            }

            var observation =
                PlayerEquivalentSimulationObservationProjector.Project(
                    new CombatSimulationPolicyContext
                    {
                        Scenario = scenario,
                        Ruleset = ruleset,
                        State = before,
                        LegalActions = legal
                    });
            var projected = observation.Actions.FirstOrDefault(item =>
                string.Equals(
                    item.CandidateId,
                    action.CandidateId,
                    StringComparison.Ordinal))?.Semantics;
            var applied = engine.ForkAndApplyPlayerAction(
                scenario,
                ruleset,
                before,
                action,
                captureSemanticEvents: true);
            if (!applied.Success)
            {
                errors.Add(
                    "semantic audit canary action failed: "
                    + cardId
                    + ":"
                    + applied.Reason);
                continue;
            }

            var decisionInput = CombatSemanticAuditor.ProjectRealized(
                before,
                applied.State,
                applied.Events,
                action,
                ruleset,
                projected);
            var audit = CombatSemanticAuditor.Audit(
                before,
                applied.State,
                applied.Events,
                decisionInput,
                action,
                ruleset);
            if (audit.Invalid)
            {
                errors.Add(
                    "decision-input semantic canary trace is invalid: "
                    + audit.Describe(cardId));
            }
            else if (audit.Mismatch)
            {
                errors.Add(
                    "decision-input semantic canary mismatch: "
                    + audit.Describe(cardId));
            }
            if (card.Effects.Count == 0)
            {
                // Native scripts are intentionally realized-only. Their
                // immutable transition is the decision input; an empty
                // static effect list is coverage metadata, not a false zero
                // prediction that should reject training.
                continue;
            }
            var declaredAudit = CombatSemanticAuditor.Audit(
                before,
                applied.State,
                applied.Events,
                projected,
                action,
                ruleset);
            if (declaredAudit.Invalid || declaredAudit.Mismatch)
            {
                errors.Add(
                    "structured semantic canary contradicts execution: "
                    + declaredAudit.Describe(cardId));
            }
        }
    }

    private static void ValidateNativeProgramProvenance(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatSimulationEngine engine,
        ICollection<string> errors)
    {
        const string cardId = "card_10";
        if (!ruleset.TryGetCard(cardId, out var card))
        {
            errors.Add("native semantic canary card is missing: " + cardId);
            return;
        }

        var scenario = CanaryScenario(campaign, ruleset);
        var before = CanaryState(card);
        var legal = engine.GetLegalPlayerActions(scenario, ruleset, before);
        var action = legal.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.PlayCard
            && string.Equals(
                item.DefinitionId,
                cardId,
                StringComparison.OrdinalIgnoreCase));
        if (action == null)
        {
            errors.Add("native semantic canary action is not legal: " + cardId);
            return;
        }

        var applied = engine.ForkAndApplyPlayerAction(
            scenario,
            ruleset,
            before,
            action,
            captureSemanticEvents: true);
        if (!applied.Success)
        {
            errors.Add(
                "native semantic canary action failed: "
                + cardId
                + ":"
                + applied.Reason);
            return;
        }

        var sourceActionId = before.ActionSequence + 1L;
        var nativeDamage = applied.Events.FirstOrDefault(item =>
            item.Kind == CombatSimulationEventKind.DamageDealt
            && item.TargetActorId == 2
            && item.CardInstanceId == action.CardInstanceId
            && item.SourceActionId == sourceActionId
            && string.Equals(
                item.SourceRewardId,
                cardId,
                StringComparison.OrdinalIgnoreCase));
        if (nativeDamage == null || nativeDamage.Amount <= 0)
        {
            errors.Add(
                "native semantic canary did not emit immutable attributed "
                + "damage: "
                + cardId);
        }
    }

    private static CombatScenarioDefinition CanaryScenario(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        return new CombatScenarioDefinition
        {
            ScenarioId = "training-semantic-canary",
            RulesetVersion = ruleset.Version,
            InitialDraw = 0,
            DrawPerTurn = 0,
            HandLimit = Math.Max(10, campaign.HandLimit),
            RequireAuthoritativeRules = true,
            TraceLevel = campaign.TraceLevel,
            Player = new CombatPlayerSetup
            {
                RoleId = "career_1",
                MaxHp = 100,
                CurrentHp = 50,
                BaseEnergy = 10
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "enemy_10001" }
            },
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 32,
                MaximumCommands = 1000,
                MaximumCommandsPerAction = 500,
                MaximumTriggerWavesPerAction = 50
            }
        };
    }

    private static CombatBattleState CanaryState(CombatCardDefinition card)
    {
        var state = new CombatBattleState
        {
            Turn = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            NextActorId = 3,
            NextCardInstanceId = 204,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    InstanceKey = "player",
                    DefinitionId = "career_1",
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 50,
                    MaxHp = 100,
                    Energy = 5,
                    BaseEnergy = 10
                },
                new CombatActorState
                {
                    ActorId = 2,
                    InstanceKey = "semantic-canary-enemy",
                    DefinitionId = "enemy_10001",
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 100,
                    MaxHp = 100
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 101,
                    CardId = card.CardId,
                    ApparentCardId = card.CardId,
                    Tags = new List<string>(card.Tags)
                },
                new CombatCardInstanceState
                {
                    InstanceId = 201,
                    CardId = "card_1",
                    ApparentCardId = "card_1"
                },
                new CombatCardInstanceState
                {
                    InstanceId = 202,
                    CardId = "card_1",
                    ApparentCardId = "card_1"
                }
            },
            Hand = { 101 },
            DrawPile = { 201, 202 }
        };
        return state;
    }

    private static void ValidateBladeAndShield(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        ICollection<string> errors)
    {
        var reward = campaign.Rewards.FirstOrDefault(item =>
            string.Equals(
                item.RewardId,
                "ritualcard_8",
                StringComparison.OrdinalIgnoreCase));
        if (reward == null
            || reward.Fidelity != CombatRuleFidelity.Authoritative
            || reward.BaseValue < 1.2d
            || Feature(reward.Features, "defense") < 1d
            || Feature(reward.Features, "cycling") < 0.8d
            || Feature(reward.Features, "reliability") < 0.95d)
        {
            errors.Add(
                "ritualcard_8 must remain an authoritative high-value "
                + "defense/cycling starter");
        }
        if (!ruleset.TryGetCard("ritualcard_8", out var card)
            || card.Fidelity != CombatRuleFidelity.Authoritative)
        {
            errors.Add("ritualcard_8 authoritative card semantics are missing");
        }
        if (!ruleset.TryGetStatus("buff_ritualcourage", out var status)
            || status.Fidelity != CombatRuleFidelity.Authoritative)
        {
            errors.Add(
                "buff_ritualcourage authoritative status semantics are missing");
        }
    }

    private static void ValidateResourceRecurrence(ICollection<string> errors)
    {
        var start = new CombatSimulationState
        {
            PlayerHp = 100,
            PlayerMaxHp = 100,
            PlayerDefend = 0,
            Power = 3,
            MaxPower = 3,
            HandCount = 2,
            HandLimit = 10,
            DrawPileKnown = true,
            Turn = 1,
            HandCardValues = new List<double> { 1d, 1d },
            Enemies = new[]
            {
                new CombatSimulationUnit
                {
                    RuntimeId = 2,
                    Hp = 100,
                    MaxHp = 100,
                    Features = new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["damageLimitActive"] = 1d
                    }
                }
            }
        };
        var end = start.Clone();
        end.PlayerDefend = 20;
        end.SetupValue = 1d;
        end.Enemies[0].Hp = 99;

        if (start.CycleHash() != end.CycleHash())
        {
            errors.Add(
                "cycle identity must reproduce finite resources and ignore "
                + "monotonic damage/block/state gains");
        }

        var assessment = CombatLoopSafetyAnalyzer.Analyze(
            start,
            end,
            new CombatDecisionProfile());
        if (assessment.Classification
                != CombatLoopClassification.SustainableControl
            || !assessment.EnemyLimitDamageActive
            || assessment.PlayerBlockDelta != 20
            || assessment.MonotonicStateGain <= 0d)
        {
            errors.Add(
                "limit-damage recurrence must retain separately measured "
                + "block and monotonic-state growth");
        }
    }

    private static void ValidateRetainAndReshuffle(
        ICollection<string> errors)
    {
        var state = new CombatSimulationState
        {
            PlayerHp = 30,
            PlayerMaxHp = 30,
            HandCount = 4,
            HandLimit = 5,
            HandCardValues = new List<double> { 1d, 1d, 1d, 1d },
            RetainedHandCardValues = new List<double> { 1d, 1d },
            DrawPileValues = new List<double> { 1d },
            DiscardPileValues = new List<double> { 1d, 1d, 1d },
            Features = new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["drawPerTurn"] = 4d
            }
        };
        var projected = CombatSearchFeatureProjector.ProjectLeaf(
            state,
            new CombatDecisionProfile());
        if (Feature(projected, "lockedHandCount") != 2d
            || Feature(projected, "effectiveNextDraw") != 3d
            || Feature(projected, "drawPileShortfall") != 2d
            || Feature(projected, "reshuffleWithinNextDraw") != 1d
            || Feature(projected, "recyclableCardCount") != 6d)
        {
            errors.Add(
                "retain, hand-limit, discard recycling, and draw-pile "
                + "reshuffle projection is inconsistent");
        }
    }

    private static void ValidateDirectedRiskScenarios(
        ICollection<string> errors)
    {
        var observation = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                CurrentHp = 50,
                MaxHp = 100,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_ReturnAgain",
                        Level = 20
                    }
                }
            },
            HandCardIds = { "ReturnAgain_11", "ReturnAgain_15" },
            DeckCardIds = { "ReturnAgain_1", "ReturnAgain_2" }
        };
        var bridge = new CombatActionObservation
        {
            CandidateId = "directed:return-again-bridge",
            SourceId = "ReturnAgain_11",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics
            {
                SelfHpLoss = 10d,
                StateChanges =
                {
                    ["status:buff_ReturnAgain"] = -10d
                }
            },
            Features =
            {
                ["recycle"] = 1d,
                ["systemProgressValue"] = 10d
            }
        };
        if (!CombatActionSafetyPolicy.IsAdmissible(
                observation,
                bridge,
                new AuraDecision.Shared.DecisionUtility(),
                out _))
        {
            errors.Add(
                "ReturnAgain self-harm conversion must remain legal when structured system progress is present");
        }
        bridge.Features["systemProgressValue"] = 0d;
        if (CombatActionSafetyPolicy.IsAdmissible(
                observation,
                bridge,
                new AuraDecision.Shared.DecisionUtility(),
                out _))
        {
            errors.Add(
                "repeatable self-harm must be rejected outside its structured system");
        }

        var attritionStart = DirectedLoopState();
        var attritionEnd = attritionStart.Clone();
        attritionEnd.PlayerHp -= 10;
        if (CombatLoopSafetyAnalyzer.Analyze(
                attritionStart,
                attritionEnd,
                new CombatDecisionProfile()).Classification
            != CombatLoopClassification.Fake)
        {
            errors.Add("self-harm recurrence must be classified as a fake loop");
        }

        var zeroCostEnd = attritionStart.Clone();
        if (CombatLoopSafetyAnalyzer.Analyze(
                attritionStart,
                zeroCostEnd,
                new CombatDecisionProfile()).Classification
            == CombatLoopClassification.CertifiedLethal)
        {
            errors.Add(
                "zero-cost recurrence without enemy progress must never be certified lethal");
        }

        var limitedHealingStart = DirectedLoopState();
        limitedHealingStart.Enemies[0].Features[
            CombatDamageLimitPolicy.ActiveFeature] = 1d;
        limitedHealingStart.Enemies[0].Features["escalationPressure"] = 1d;
        var limitedHealingEnd = limitedHealingStart.Clone();
        if (CombatLoopSafetyAnalyzer.Analyze(
                limitedHealingStart,
                limitedHealingEnd,
                new CombatDecisionProfile()).Classification
            != CombatLoopClassification.Blocked)
        {
            errors.Add(
                "enemy limit-damage plus healing/no-progress recurrence must be blocked");
        }

        var statusAction = new CombatActionObservation
        {
            CandidateId = "directed:status-decrement",
            Kind = CombatActionKind.PlayCard,
            Semantics = new CombatActionSemantics
            {
                StateChanges =
                {
                    ["status:buff_ReturnAgain"] = -10d
                }
            }
        };
        var statusState = DirectedLoopState();
        statusState.Features["status:buff_ReturnAgain"] = 20d;
        var statusModel = CombatForwardModel.Resolve(
            observation,
            statusAction,
            useRegisteredResolvers: false);
        var statusAfter = CombatForwardModel.Apply(
            statusState,
            statusAction,
            0,
            statusModel.Outcomes[0],
            new CombatDecisionProfile());
        if (Feature(statusAfter.Features, "status:buff_ReturnAgain") != 10d)
        {
            errors.Add(
                "direct status-level decrements must survive the forward transition structurally");
        }

        var dynamicBefore = DirectedLoopState();
        dynamicBefore.ActionCostAdjustments = new[] { 0 };
        var dynamicAfter = dynamicBefore.Clone();
        dynamicAfter.ActionCostAdjustments[0] = -1;
        if (dynamicBefore.CycleHash() == dynamicAfter.CycleHash())
        {
            errors.Add(
                "dynamic held-card cost changes must break false cycle identity");
        }
    }

    private static CombatSimulationState DirectedLoopState()
    {
        return new CombatSimulationState
        {
            PlayerRuntimeId = 1,
            PlayerHp = 50,
            PlayerMaxHp = 100,
            Power = 3,
            MaxPower = 3,
            HandCount = 1,
            HandLimit = 10,
            HandCardIds = new List<string> { "directed-card" },
            HandCardValues = new List<double> { 1d },
            Enemies = new[]
            {
                new CombatSimulationUnit
                {
                    RuntimeId = 2,
                    Hp = 50,
                    MaxHp = 50
                }
            },
            UsedActionWords = new ulong[1],
            UsedActionCounts = new int[1]
        };
    }

    private static double Feature(
        IReadOnlyDictionary<string, double> features,
        string key)
    {
        return features.TryGetValue(key, out var value)
            ? value
            : 0d;
    }
}
