using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationSemanticProbeResult
{
    public string Version { get; set; } =
        CombatPolicyValueProtocol.TrainingSemanticsVersion;

    public List<string> Errors { get; set; } = new();

    public bool Success => Errors.Count == 0;
}

public static class CombatFoundationSemanticProbe
{
    public static CombatFoundationSemanticProbeResult Validate(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));

        var result = new CombatFoundationSemanticProbeResult();
        ValidateBladeAndShield(campaign, ruleset, result.Errors);
        ValidateResourceRecurrence(result.Errors);
        ValidateRetainAndReshuffle(result.Errors);
        return result;
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
