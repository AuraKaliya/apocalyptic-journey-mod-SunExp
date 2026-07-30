using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationSemanticProbeResult
{
    public const string CurrentCanaryVersion =
        "targeted-phased-semantic-pipeline-v3";

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
        ValidateBladeAndShield(campaign, ruleset, result.Errors);
        ValidateResourceRecurrence(result.Errors);
        ValidateRetainAndReshuffle(result.Errors);
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

            var audit = CombatSemanticAuditor.Audit(
                before,
                applied.State,
                applied.Events,
                projected,
                action,
                ruleset);
            if (audit.Invalid)
            {
                errors.Add(
                    "semantic audit canary trace is invalid: "
                    + audit.Describe(cardId));
            }
            else if (audit.Mismatch)
            {
                errors.Add(
                    "semantic audit canary mismatch: "
                    + audit.Describe(cardId));
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

    private static double Feature(
        IReadOnlyDictionary<string, double> features,
        string key)
    {
        return features.TryGetValue(key, out var value)
            ? value
            : 0d;
    }
}
