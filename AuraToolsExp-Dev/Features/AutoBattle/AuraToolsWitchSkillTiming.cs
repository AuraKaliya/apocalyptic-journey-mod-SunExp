using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal sealed class AuraToolsWitchSkillTimingProvider :
    ICombatSkillTimingProvider
{
    internal static readonly IReadOnlyDictionary<string, int> Cooldowns =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
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
            ["careercard_13"] = 99
        };

    public bool TryEnrich(CombatStateObservation state)
    {
        if (state?.Actions == null)
        {
            return false;
        }

        var enriched = false;
        var summary = SkillTimingStateSummary.Create(state);
        foreach (var action in state.Actions.Where(action =>
                     action != null
                     && action.Kind == CombatActionKind.UseSkill
                     && action.Legal
                     && Cooldowns.ContainsKey(action.SourceId)))
        {
            if (string.Equals(
                    action.SourceId,
                    "careercard_3",
                    StringComparison.OrdinalIgnoreCase)
                && CombatSkillTimingPolicy.Value(
                    action.Features,
                    CombatSkillTimingFeatureNames.Active) > 0.5d)
            {
                CombatSkillTimingPolicy.Enrich(action);
                enriched = true;
                continue;
            }

            ResetComponents(action);
            action.Features[CombatSkillTimingFeatureNames.Active] = 1d;
            action.Features[CombatSkillTimingFeatureNames.ResetsEachBattle] = 1d;
            if (CombatSkillTimingPolicy.Value(
                    action.Features,
                    CombatSkillTimingFeatureNames.CooldownAfterUse) <= 0d)
            {
                action.Features[CombatSkillTimingFeatureNames.CooldownAfterUse] =
                    Cooldowns[action.SourceId];
            }

            EnrichOne(state, action, summary);
            CombatSkillTimingPolicy.Enrich(action);
            enriched = true;
        }
        return enriched;
    }

    private static void EnrichOne(
        CombatStateObservation state,
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        switch (action.SourceId.ToLowerInvariant())
        {
            case "careercard_1":
                EnrichDeckSearch(action, summary);
                break;
            case "careercard_2":
                EnrichDoomDevour(action, summary);
                break;
            case "careercard_3":
                EnrichCalamityForm(action, summary);
                break;
            case "careercard_4":
                EnrichRoyalDecree(action, summary);
                break;
            case "careercard_5":
                EnrichWailingWall(state, action, summary);
                break;
            case "careercard_6":
                EnrichLightlessAegis(state, action, summary);
                break;
            case "careercard_7":
                EnrichChaosControl(state, action, summary);
                break;
            case "careercard_8":
                EnrichImitation(state, action, summary);
                break;
            case "careercard_9":
                EnrichSketchWorld(action, summary);
                break;
            case "careercard_10":
                EnrichSlaughterTime(state, action, summary);
                break;
            case "careercard_11":
                EnrichCrimson(action, summary);
                break;
            case "careercard_12":
                EnrichMirrorShade(action, summary);
                break;
            case "careercard_13":
                EnrichAbyssalCalling(action, summary);
                break;
        }
    }

    private static void EnrichDeckSearch(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var usable = summary.DrawPileCount > 0 && summary.HandCapacity > 0;
        Set(action, CombatSkillTimingFeatureNames.CooldownCycleValue,
            usable ? 2d + Math.Min(2d, summary.HandCapacity * 0.35d) : 0d);
        Set(action, CombatSkillTimingFeatureNames.ExpiryRisk,
            usable && summary.BattleHorizon <= 1.5d ? 2d : 0d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            summary.HandCapacity <= 0 ? 6d : 0d);
        Set(action, CombatSkillTimingFeatureNames.RedundancyCost,
            summary.DrawPileCount <= 0 ? 12d : 0d);
    }

    private static void EnrichDoomDevour(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var doomGain = Value(action, "nana:projected-doom-gain");
        var maxHpGain = Value(action, "nana:projected-max-hp-gain");
        var cleanse = Math.Max(0d, action.Semantics?.Cleanse ?? 0d);
        var net = doomGain * 0.9d + maxHpGain * 0.15d + cleanse * 0.35d;
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue, net);
        Set(action, CombatSkillTimingFeatureNames.CooldownCycleValue,
            net > 0d && summary.BattleHorizon > 5d ? Math.Min(4d, net * 0.2d) : 0d);
        Set(action, CombatSkillTimingFeatureNames.ExpiryRisk,
            net > 0d && summary.BattleHorizon <= 2d ? Math.Min(5d, net) : 0d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            net <= 0d ? 2.5d : 0d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            Math.Max(0d, Value(action, "nana:enemy-cleanse-cost")));
    }

    private static void EnrichCalamityForm(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var repeated = Value(action, "nana:repeat-transform") > 0.5d;
        var ongoing = Math.Max(0d, action.Semantics?.PersistentValue ?? 0d)
                      * Math.Min(1d, summary.BattleHorizon / 3d);
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue, ongoing);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            Math.Max(0d, Value(action, "roleStrategy:nana.best-devour-net-value")));
        Set(action, CombatSkillTimingFeatureNames.RedundancyCost,
            repeated ? 40d : 0d);
        Set(action, CombatSkillTimingFeatureNames.ExpiryRisk,
            !repeated && summary.BattleHorizon <= 2d ? Math.Min(8d, ongoing) : 0d);
    }

    private static void EnrichRoyalDecree(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var net = Value(action, "handTransformNetValue");
        var growth = Value(action, "expectedGrowthFromTransform");
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            Math.Max(0d, net * 0.2d + growth * 0.1d));
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            summary.HandCount < 3 ? 3d - summary.HandCount * 0.5d : 0d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            Math.Max(0d, -net));
        Set(action, CombatSkillTimingFeatureNames.ExpiryRisk,
            net > 0d && summary.BattleHorizon <= 1.5d ? Math.Min(5d, net * 0.25d) : 0d);
    }

    private static void EnrichWailingWall(
        CombatStateObservation state,
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var target = FindTarget(state, action);
        var selfTarget = target?.RuntimeId == state.Player.RuntimeId;
        var convertible = Math.Min(5d, summary.HandCapacity)
                          * (selfTarget ? summary.ActionBudgetFactor : 0.8d);
        Set(action, CombatSkillTimingFeatureNames.CooldownCycleValue,
            convertible * 0.8d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            summary.HandCapacity < 3 ? (3 - summary.HandCapacity) * 1.5d : 0d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            Math.Max(0d, 5d - convertible) * 0.8d);
    }

    private static void EnrichLightlessAegis(
        CombatStateObservation state,
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var intents = (state.Threat?.Intents ?? new List<CombatIntentObservation>())
            .Where(intent => intent.SourceRuntimeId == action.TargetRuntimeId
                             && intent.Kind == CombatIntentKind.Attack)
            .ToList();
        var threat = intents.Sum(intent =>
            Math.Max(0d, intent.BlockableDamage + intent.UnblockableDamage)
            * Math.Max(0d, Math.Min(1d, intent.Probability)));
        if (threat <= 0d && state.Threat?.CurrentIntentKnown == true)
        {
            threat = Math.Max(0d, state.ExpectedIncomingDamage);
        }
        Set(action, CombatSkillTimingFeatureNames.ExpiryRisk,
            Math.Min(20d, threat * 0.45d));
        Set(action, CombatSkillTimingFeatureNames.ReserveValue,
            threat <= 0d ? 7d : 0d);
        Set(action, CombatSkillTimingFeatureNames.RedundancyCost,
            threat <= 0d ? 5d : 0d);
    }

    private static void EnrichChaosControl(
        CombatStateObservation state,
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var hpRatio = state.Player.MaxHp <= 0
            ? 0d
            : (double)state.Player.CurrentHp / state.Player.MaxHp;
        var safeRandomWindow = hpRatio >= 0.35d;
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            safeRandomWindow ? 3d + Math.Min(2d, state.Player.Statuses.Count * 0.2d) : 0d);
        Set(action, CombatSkillTimingFeatureNames.ReserveValue,
            safeRandomWindow ? 0d : 8d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            safeRandomWindow ? 0.5d : 5d);
    }

    private static void EnrichImitation(
        CombatStateObservation state,
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var target = FindTarget(state, action);
        var positive = StatusValue(target, "Positive");
        var negative = StatusValue(target, "Negative");
        var alreadyOwned = target == null ? 0d : target.Statuses.Sum(status =>
            state.Player.Statuses.Any(owned => string.Equals(
                owned.StatusId,
                status.StatusId,
                StringComparison.OrdinalIgnoreCase))
                ? Math.Max(0, status.Level) * 0.35d
                : 0d);
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            Math.Max(0d, positive - alreadyOwned) * 0.55d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            negative * 0.7d);
        Set(action, CombatSkillTimingFeatureNames.RedundancyCost,
            positive <= alreadyOwned ? 4d : 0d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            positive <= negative ? 2d : 0d);
    }

    private static void EnrichSketchWorld(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var future = Math.Max(0d, summary.RemainingBattles);
        var hasChoice = summary.DrawPileCount > 0;
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            hasChoice ? Math.Min(16d, 2d + future * 0.45d) : 0d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            hasChoice ? Math.Max(1d, 4d - summary.DeckSize * 0.08d) : 0d);
        Set(action, CombatSkillTimingFeatureNames.RedundancyCost,
            hasChoice ? 0d : 12d);
        Set(action, CombatSkillTimingFeatureNames.ExpiryRisk,
            hasChoice && summary.BattleHorizon <= 1.5d ? 3d : 0d);
    }

    private static void EnrichSlaughterTime(
        CombatStateObservation state,
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var target = FindTarget(state, action);
        if (target == null)
        {
            Set(action, CombatSkillTimingFeatureNames.RedundancyCost, 10d);
            return;
        }
        var currentStacks = target.Statuses
            .Where(status => string.Equals(status.StatusId, "buff_oniblood", StringComparison.OrdinalIgnoreCase))
            .Select(status => Math.Max(0, status.Level))
            .DefaultIfEmpty(0)
            .Max();
        var projectedActions = target.Kind == CombatTargetKind.Enemy
            ? Math.Max(1d, target.Features.TryGetValue("actionCount", out var count) ? count : 1d)
            : Math.Max(1d, summary.ActionBudgetFactor * 3d);
        var missingHp = Math.Max(0, target.MaxHp - target.CurrentHp);
        var heal = Math.Min(missingHp, (currentStacks + 1d) * 4d * projectedActions);
        var friendlyNet = heal - 16d;
        var enemyNet = 16d - heal;
        var net = target.Kind == CombatTargetKind.Enemy ? enemyNet : friendlyNet;
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            Math.Max(0d, net) * 0.5d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            Math.Max(0d, -net) * 0.5d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            net <= 0d ? 2d : 0d);
    }

    private static void EnrichCrimson(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var useValue = summary.TotalBleeding * Math.Max(1, summary.LivingEnemyCount) * 0.25d;
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            Math.Min(20d, useValue));
        Set(action, CombatSkillTimingFeatureNames.CooldownCycleValue,
            summary.TotalBleeding > 0d ? 2.5d : 0d);
        Set(action, CombatSkillTimingFeatureNames.RedundancyCost,
            summary.TotalBleeding <= 0d ? 10d : 0d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            summary.TotalBleeding <= 0d ? 2d : 0d);
    }

    private static void EnrichMirrorShade(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var best = summary.BestHandCardValue;
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            best * 0.65d);
        Set(action, CombatSkillTimingFeatureNames.CooldownCycleValue,
            best > 0d && summary.BattleHorizon > 2d ? Math.Min(3d, best * 0.2d) : 0d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            best < 2d ? 4d : 0d);
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            summary.HandCapacity <= 0 ? 3d : 0d);
    }

    private static void EnrichAbyssalCalling(
        CombatActionObservation action,
        SkillTimingStateSummary summary)
    {
        var futureMultiplier = 1d + Math.Min(8d, summary.RemainingBattles) * 0.25d;
        var best = summary.BestHandCardValue * futureMultiplier;
        Set(action, CombatSkillTimingFeatureNames.OngoingEffectValue,
            Math.Min(24d, best));
        Set(action, CombatSkillTimingFeatureNames.OpportunityCost,
            summary.BestHandCardCost + summary.BestHandExtraCost * 1.5d);
        Set(action, CombatSkillTimingFeatureNames.DelayGain,
            summary.BestHandCardValue < 3d ? 6d : 0d);
        Set(action, CombatSkillTimingFeatureNames.RedundancyCost,
            summary.HandCount <= 0 ? 12d : 0d);
        Set(action, CombatSkillTimingFeatureNames.ExpiryRisk,
            summary.BestHandCardValue >= 3d && summary.BattleHorizon <= 1.5d ? 5d : 0d);
    }

    private static void ResetComponents(CombatActionObservation action)
    {
        foreach (var key in new[]
                 {
                     CombatSkillTimingFeatureNames.OngoingEffectValue,
                     CombatSkillTimingFeatureNames.CooldownCycleValue,
                     CombatSkillTimingFeatureNames.ExpiryRisk,
                     CombatSkillTimingFeatureNames.DelayGain,
                     CombatSkillTimingFeatureNames.ReserveValue,
                     CombatSkillTimingFeatureNames.RedundancyCost,
                     CombatSkillTimingFeatureNames.OpportunityCost
                 })
        {
            action.Features[key] = 0d;
        }
    }

    private static CombatUnitObservation? FindTarget(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (state.Player.RuntimeId == action.TargetRuntimeId)
        {
            return state.Player;
        }
        return state.Friendlies.Concat(state.Enemies).FirstOrDefault(unit =>
            unit.RuntimeId == action.TargetRuntimeId);
    }

    private static double StatusValue(CombatUnitObservation? target, string type)
    {
        return target?.Statuses
                   .Where(status => string.Equals(status.Type, type, StringComparison.OrdinalIgnoreCase))
                   .Sum(status => Math.Max(0, status.Level) * Math.Max(1, status.Rarity))
               ?? 0d;
    }

    private static double Value(CombatActionObservation action, string key)
    {
        return CombatSkillTimingPolicy.Value(action.Features, key);
    }

    private static void Set(CombatActionObservation action, string key, double value)
    {
        action.Features[key] = Math.Max(0d, Math.Min(40d, value));
    }

    private sealed class SkillTimingStateSummary
    {
        public int HandCount { get; private set; }
        public int HandCapacity { get; private set; }
        public int DrawPileCount { get; private set; }
        public int DeckSize { get; private set; }
        public int LivingEnemyCount { get; private set; }
        public double TotalBleeding { get; private set; }
        public double BattleHorizon { get; private set; }
        public double RemainingBattles { get; private set; }
        public double ActionBudgetFactor { get; private set; }
        public double BestHandCardValue { get; private set; }
        public double BestHandCardCost { get; private set; }
        public double BestHandExtraCost { get; private set; }

        public static SkillTimingStateSummary Create(CombatStateObservation state)
        {
            var handLimit = Feature(state, "handLimit", 10d);
            var enemyHp = state.Enemies.Where(enemy => enemy.Alive)
                .Sum(enemy => Math.Max(0, enemy.CurrentHp));
            var best = state.HandCards
                .Select(card => new
                {
                    Card = card,
                    Value = CardValue(card)
                })
                .OrderByDescending(item => item.Value)
                .FirstOrDefault();
            return new SkillTimingStateSummary
            {
                HandCount = Math.Max(0, state.HandCount),
                HandCapacity = Math.Max(0, (int)Math.Floor(handLimit) - state.HandCount),
                DrawPileCount = Math.Max(0, (int)Math.Floor(Feature(
                    state,
                    "drawPileCount",
                    state.DeckKnowledge?.DrawPileCount ?? 0))),
                DeckSize = state.DeckCardIds?.Count ?? 0,
                LivingEnemyCount = state.Enemies.Count(enemy => enemy.Alive),
                TotalBleeding = state.Player.Statuses
                    .Concat(state.Friendlies.SelectMany(unit => unit.Statuses))
                    .Concat(state.Enemies.SelectMany(unit => unit.Statuses))
                    .Where(status => string.Equals(status.StatusId, "buff_bleeding", StringComparison.OrdinalIgnoreCase))
                    .Sum(status => Math.Max(0, status.Level)),
                BattleHorizon = Math.Max(1d, Math.Min(8d, enemyHp / 18d)),
                RemainingBattles = Feature(
                    state,
                    CombatCampaignContextFeatureNames.RemainingBattles,
                    0d),
                ActionBudgetFactor = Math.Max(0.25d, Math.Min(1d,
                    (state.CurrentPower + Math.Max(0, state.HandCount - 1)) / 6d)),
                BestHandCardValue = best?.Value ?? 0d,
                BestHandCardCost = best?.Card.EffectiveCost ?? 0d,
                BestHandExtraCost = best == null
                    ? 0d
                    : CardFeature(best.Card, "mechanic:total-extra-cost")
            };
        }

        private static double CardValue(CombatCardInstanceObservation card)
        {
            var value = 1d + Math.Max(0d, 3d - card.EffectiveCost) * 0.35d;
            if (card.Retained) value += 0.5d;
            if (card.ExhaustsOnUse) value -= 0.35d;
            value += Math.Max(0, card.EnhancementCount) * 0.4d;
            value += CardFeature(card, "choice:semantic-value");
            var extraUses = CardFeature(card, "mechanic:extra-use-count");
            return Math.Max(0d, value * (1d + Math.Min(4d, extraUses) * 0.5d));
        }

        private static double CardFeature(CombatCardInstanceObservation card, string key)
        {
            return card.Features != null
                   && card.Features.TryGetValue(key, out var value)
                   && !double.IsNaN(value)
                   && !double.IsInfinity(value)
                ? value
                : 0d;
        }

        private static double Feature(
            CombatStateObservation state,
            string key,
            double fallback)
        {
            return state.Features != null
                   && state.Features.TryGetValue(key, out var value)
                   && !double.IsNaN(value)
                   && !double.IsInfinity(value)
                ? value
                : fallback;
        }
    }
}

internal static class AuraToolsWitchSkillInteraction
{
    public static void Prepare(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (state == null
            || action == null
            || action.Kind != CombatActionKind.UseSkill
            || !RequiresChoice(action.SourceId))
        {
            CombatInteractionBroker.ClearNextHint();
            return;
        }

        CombatInteractionBroker.SetNextHint(new CombatInteractionHint
        {
            OwnerModId = "AuraToolsExp",
            SourceId = action.SourceId,
            Purpose = "role-skill:" + action.SourceId,
            Kind = action.SourceId == "careercard_1" || action.SourceId == "careercard_9"
                ? CombatPromptKind.ChooseCards
                : CombatPromptKind.ChooseHandCards,
            Zone = action.SourceId == "careercard_1" || action.SourceId == "careercard_9"
                ? CombatPromptZone.Deck
                : CombatPromptZone.Hand,
            Forced = true,
            PreferLowestValue = false,
            ChoiceScorer = new WitchSkillChoiceScorer(
                action.SourceId,
                RemainingBattles(state))
        });
    }

    private static bool RequiresChoice(string sourceId)
    {
        return string.Equals(sourceId, "careercard_1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sourceId, "careercard_9", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sourceId, "careercard_12", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sourceId, "careercard_13", StringComparison.OrdinalIgnoreCase);
    }

    private static double RemainingBattles(CombatStateObservation state)
    {
        return state.Features.TryGetValue(
            CombatCampaignContextFeatureNames.RemainingBattles,
            out var value)
            ? Math.Max(0d, value)
            : 0d;
    }

    private sealed class WitchSkillChoiceScorer : ICombatInteractionChoiceScorer
    {
        private readonly string sourceId;
        private readonly double remainingBattles;

        public WitchSkillChoiceScorer(string sourceId, double remainingBattles)
        {
            this.sourceId = sourceId ?? "";
            this.remainingBattles = Math.Max(0d, remainingBattles);
        }

        public bool TryScore(
            CombatInteractionHint hint,
            CombatActionObservation choice,
            out double score)
        {
            var baseValue = SemanticValue(choice.Semantics);
            var cost = Feature(choice, "choice:cost");
            var rarity = Math.Max(1d, Feature(choice, "choice:rarity"));
            var extraCost = Feature(choice, "choice:total-extra-cost");
            var extraUses = Feature(choice, "choice:extra-use-count");
            switch (sourceId.ToLowerInvariant())
            {
                case "careercard_1":
                case "careercard_12":
                    score = baseValue;
                    return true;
                case "careercard_9":
                    var blessingValue = (rarity >= 3d ? 4d : 2d)
                                        + Math.Max(0d, cost) * 0.75d;
                    score = blessingValue * (1d + Math.Min(8d, remainingBattles) * 0.08d)
                            - baseValue;
                    return true;
                case "careercard_13":
                    score = baseValue
                            * (1d + Math.Min(10d, remainingBattles) * 0.18d)
                            / (1d + Math.Max(0d, cost + extraCost) * 0.2d)
                            / (1d + Math.Max(0d, extraUses) * 0.35d);
                    return true;
                default:
                    score = 0d;
                    return false;
            }
        }

        private static double SemanticValue(CombatActionSemantics? semantics)
        {
            if (semantics == null) return 0d;
            return Math.Max(0d,
                semantics.Damage
                + semantics.TrueDamage
                + semantics.Defend * 0.8d
                + semantics.Heal * 0.9d
                + semantics.Draw * 1.5d
                + semantics.EnergyGain * 2d
                + semantics.Scaling
                + semantics.DeckValue
                + semantics.PersistentValue
                + semantics.CardGeneration);
        }

        private static double Feature(CombatActionObservation choice, string key)
        {
            return choice.Features.TryGetValue(key, out var value)
                ? value
                : 0d;
        }
    }
}
