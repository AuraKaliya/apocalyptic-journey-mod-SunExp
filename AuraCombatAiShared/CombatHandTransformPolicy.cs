using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public sealed class CombatHandTransformAssessment
{
    public int CardCount { get; set; }

    public double OriginalHandValue { get; set; }

    public double TransformedHandValue { get; set; }

    public double CleanupValue { get; set; }

    public double EngineLoss { get; set; }

    public double EnhancementLoss { get; set; }

    public double RenewableDeckValue { get; set; }

    public int RenewableCardCount { get; set; }

    public double DepletionRisk { get; set; }

    public double ExpectedGrowth { get; set; }

    public double NetValue { get; set; }

    public double TargetCardValue { get; set; }

    public double TargetDamagePerCard { get; set; }

    public bool LethalCertified { get; set; }

    public int SoulToNextTier { get; set; }

    public bool ThresholdOpportunity { get; set; }
}

public static class CombatHandTransformPolicy
{
    private const double Epsilon = 0.000001d;

    public static void Enrich(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (state == null || action?.Semantics?.HandTransform == null)
        {
            return;
        }

        action.Semantics.OpensInteraction = true;
        var assessment = Assess(state, action);
        action.Features["handTransform"] = 1d;
        action.Features["handTransformCount"] = assessment.CardCount;
        action.Features["handTransformTargetTier"] =
            action.Semantics.HandTransform.TargetTier;
        action.Features["handTransformOriginalValue"] =
            assessment.OriginalHandValue;
        action.Features["handTransformGrossValue"] =
            assessment.TransformedHandValue;
        action.Features["handTransformTargetCardValue"] =
            assessment.TargetCardValue;
        action.Features["handTransformCleanupValue"] = assessment.CleanupValue;
        action.Features["handTransformEngineLoss"] = assessment.EngineLoss;
        action.Features["handTransformEnhancementLoss"] =
            assessment.EnhancementLoss;
        action.Features["handTransformRenewableDeckValue"] =
            assessment.RenewableDeckValue;
        action.Features["handTransformRenewableCardCount"] =
            assessment.RenewableCardCount;
        action.Features["handTransformNetValue"] = assessment.NetValue;
        action.Features["postTransformDepletionRisk"] =
            assessment.DepletionRisk;
        action.Features["postTransformLethalCertified"] =
            assessment.LethalCertified ? 1d : 0d;
        action.Features["expectedGrowthFromTransform"] =
            assessment.ExpectedGrowth;
        action.Features["growthToNextTransformTier"] =
            assessment.SoulToNextTier;
        action.Features["transformTierThresholdOpportunity"] =
            assessment.ThresholdOpportunity ? 1d : 0d;
        action.Features["transformCooldownProgressRequired"] =
            Math.Max(
                0d,
                action.Semantics.HandTransform.CooldownProgressRequired);
    }

    public static CombatHandTransformAssessment Assess(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var transform = action.Semantics.HandTransform
                        ?? new CombatHandTransformSemantic();
        var cards = ResolveHandCards(state);
        var result = new CombatHandTransformAssessment
        {
            CardCount = cards.Count,
            TargetCardValue = EstimateSemanticsValue(
                transform.TargetCardSemantics,
                Math.Max(1, state.Enemies.Count(enemy => enemy.Alive)))
        };
        result.TargetDamagePerCard = TargetDamagePerCard(
            transform.TargetCardSemantics,
            Math.Max(1, state.Enemies.Count(enemy => enemy.Alive)));

        foreach (var card in cards)
        {
            var actionForCard = state.Actions.FirstOrDefault(candidate =>
                candidate.Kind == CombatActionKind.PlayCard
                && (card.RuntimeId != 0
                    && candidate.RuntimeId == card.RuntimeId
                    || card.RuntimeId == 0
                    && string.Equals(
                        candidate.SourceId,
                        card.CardId,
                        StringComparison.OrdinalIgnoreCase)));
            var semantics = ResolveCardSemantics(card.CardId, actionForCard);
            var positiveValue = EstimateSemanticsValue(
                semantics,
                Math.Max(1, state.Enemies.Count(enemy => enemy.Alive)));
            var isCurse = HasTag(state, card.CardId, "Curse")
                          || Flag(actionForCard?.Features, "curse");
            var isUnplayable = HasTag(state, card.CardId, "Unusable")
                               || Flag(actionForCard?.Features, "unplayable");
            var contextualRisk = Math.Max(0d, semantics.Risk)
                                 + Math.Max(0d, semantics.SelfHpLoss) * 0.75d
                                 + Math.Max(
                                     0d,
                                     semantics.EndOfCycleSelfHpLoss) * 0.5d;
            var cleanup = contextualRisk;
            if (isCurse || isUnplayable)
            {
                cleanup += positiveValue > 3d ? 1.5d : 5d;
            }
            var engineLoss = Math.Max(0d, semantics.Draw) * 1.75d
                             + Math.Max(0d, semantics.CardGeneration) * 1.5d
                             + Math.Max(0d, semantics.EnergyGain) * 1.25d
                             + semantics.CardRetrievals.Sum(item =>
                                 Math.Max(0, item.Amount)) * 1.5d;
            if (Flag(actionForCard?.Features, "strategyInfinite"))
            {
                engineLoss += Flag(
                        actionForCard?.Features,
                        "strategyExecutable")
                    ? 14d
                    : 8d;
            }
            if (Flag(actionForCard?.Features, "recycle")
                || Flag(actionForCard?.Features, "ouroboros"))
            {
                engineLoss += 5d;
            }
            var enhancementLoss = Math.Max(0, card.EnhancementCount) * 2d;
            result.CleanupValue += cleanup;
            result.EngineLoss += engineLoss;
            result.EnhancementLoss += enhancementLoss;
            result.OriginalHandValue += Math.Max(0d, positiveValue);
        }

        result.TransformedHandValue = result.TargetCardValue * result.CardCount;
        var drawCount = Math.Max(0, state.DeckKnowledge?.DrawPileCount ?? 0);
        var discardCount = Math.Max(
            0,
            state.DeckKnowledge?.DiscardPileCount ?? 0);
        result.RenewableCardCount = drawCount + discardCount;
        result.RenewableDeckValue = EstimateRenewableDeckValue(state);

        var livingEnemies = state.Enemies.Where(enemy => enemy.Alive).ToList();
        var enemyHp = livingEnemies
            .Sum(enemy => Math.Max(0, enemy.CurrentHp));
        var totalTransformDamage = result.TargetDamagePerCard * result.CardCount;
        var damageLimitActive = livingEnemies.Any(enemy =>
            enemy.Features.TryGetValue("damageLimitActive", out var active)
            && active > 0.5d);
        var normalDamagePerEnemy = Math.Max(
            0d,
            transform.TargetCardSemantics.Damage)
            * Math.Max(1d, transform.TargetCardSemantics.HitCount);
        var trueDamagePerEnemy = Math.Max(
            0d,
            transform.TargetCardSemantics.TrueDamage);
        result.LethalCertified = livingEnemies.Count > 0
                                 && !damageLimitActive
                                 && livingEnemies.All(enemy =>
                                     Math.Max(
                                         0d,
                                         normalDamagePerEnemy
                                         * result.CardCount
                                         - Math.Max(0, enemy.Defend))
                                     + trueDamagePerEnemy * result.CardCount
                                     + Epsilon
                                     >= Math.Max(0, enemy.CurrentHp));
        result.DepletionRisk = DepletionRisk(
            state,
            transform,
            result,
            Math.Max(0d, enemyHp - totalTransformDamage));

        var expectedBurns = result.LethalCertified
            ? Math.Min(
                result.CardCount,
                (int)Math.Ceiling(
                    enemyHp / Math.Max(1d, result.TargetDamagePerCard)))
            : Math.Max(0d, result.CardCount * 0.6d);
        result.ExpectedGrowth = expectedBurns
                                * Math.Max(0d, transform.GrowthPerExhaust);
        result.SoulToNextTier = transform.NextTierThreshold <= 0
            ? 0
            : Math.Max(
                0,
                transform.NextTierThreshold
                - (int)Math.Floor(transform.CurrentGrowthValue));
        var burnableBeforeTransform = cards.Count(card => card.ExhaustsOnUse);
        result.ThresholdOpportunity = result.SoulToNextTier > 0
                                      && result.SoulToNextTier <= burnableBeforeTransform;
        var thresholdPenalty = result.ThresholdOpportunity ? 4d : 0d;
        result.NetValue = Clamp(
            (result.TransformedHandValue
             - result.OriginalHandValue
             + result.CleanupValue
             + result.ExpectedGrowth * 0.35d
             - result.EngineLoss
             - result.EnhancementLoss
             - result.DepletionRisk
             - thresholdPenalty) / Math.Max(4d, result.CardCount * 2d),
            -18d,
            18d);
        return result;
    }

    private static List<CombatCardInstanceObservation> ResolveHandCards(
        CombatStateObservation state)
    {
        if (state.HandCards != null && state.HandCards.Count > 0)
        {
            return state.HandCards.ToList();
        }
        return (state.HandCardIds ?? new List<string>())
            .Select((cardId, index) =>
            {
                var action = state.Actions.FirstOrDefault(candidate =>
                    candidate.Kind == CombatActionKind.PlayCard
                    && string.Equals(
                        candidate.SourceId,
                        cardId,
                        StringComparison.OrdinalIgnoreCase));
                return new CombatCardInstanceObservation
                {
                    RuntimeId = action?.RuntimeId ?? 0,
                    CardId = cardId,
                    EffectiveCost = action?.Cost ?? 0,
                    Retained = Flag(action?.Features, "retain"),
                    ExhaustsOnUse = Flag(action?.Features, "exhaustOnUse"),
                    EnhancementCount = Flag(
                        action?.Features,
                        "hasVisibleWarning") ? 1 : 0,
                    Features = new Dictionary<string, double>(
                        action?.Features
                        ?? new Dictionary<string, double>(),
                        StringComparer.OrdinalIgnoreCase)
                };
            })
            .ToList();
    }

    private static CombatActionSemantics ResolveCardSemantics(
        string cardId,
        CombatActionObservation? action)
    {
        if (action?.Semantics != null
            && HasRecognizedValue(action.Semantics))
        {
            return action.Semantics;
        }
        return CombatKnowledgeRegistry.TryDescribeAction(
            new CombatActionObservation
            {
                SourceId = cardId,
                Kind = CombatActionKind.PlayCard
            },
            out var semantics,
            out _,
            out _)
            ? semantics
            : action?.Semantics ?? new CombatActionSemantics();
    }

    private static double EstimateRenewableDeckValue(
        CombatStateObservation state)
    {
        var ids = (state.DiscardPileCardIds ?? new List<string>())
            .Concat(state.DeckKnowledge?.KnownDeckCardIds
                    ?? state.DeckCardIds
                    ?? new List<string>())
            .ToList();
        if (ids.Count == 0)
        {
            return 0d;
        }
        return ids.Select(id => EstimateSemanticsValue(
                ResolveCardSemantics(id, null),
                Math.Max(1, state.Enemies.Count(enemy => enemy.Alive))))
            .Where(value => value > 0d)
            .DefaultIfEmpty(0d)
            .Average();
    }

    private static double DepletionRisk(
        CombatStateObservation state,
        CombatHandTransformSemantic transform,
        CombatHandTransformAssessment assessment,
        double remainingEnemyHp)
    {
        if (assessment.LethalCertified || remainingEnemyHp <= 0d)
        {
            return 0d;
        }
        var risk = assessment.RenewableCardCount <= 0
            ? 8d
            : assessment.RenewableCardCount <= 2
                ? 3d
                : 0d;
        var futureDamage = Math.Max(1d, assessment.TargetDamagePerCard);
        var turnsNeeded = Math.Ceiling(remainingEnemyHp / futureDamage);
        var threat = state.Threat ?? new CombatThreatForecast();
        var blockable = Math.Max(
            state.ExpectedIncomingDamage,
            threat.ExpectedBlockableDamage);
        var unavoidable = Math.Max(0d, threat.ExpectedUnblockableDamage)
                          + Math.Max(0d, threat.ExpectedDamageOverTime);
        var hpLossPerTurn = Math.Max(
            0d,
            blockable
            - Math.Max(0d, transform.TargetCardSemantics.Defend))
                            + unavoidable;
        if (hpLossPerTurn <= Epsilon)
        {
            return risk;
        }
        var survivableTurns = Math.Floor(
            Math.Max(0d, state.Player.CurrentHp - 1d) / hpLossPerTurn);
        if (turnsNeeded > survivableTurns)
        {
            risk += Math.Min(30d, (turnsNeeded - survivableTurns) * 6d);
        }
        return risk;
    }

    private static double TargetDamagePerCard(
        CombatActionSemantics semantics,
        int enemyCount)
    {
        var perEnemy = Math.Max(0d, semantics.Damage)
                       * Math.Max(1d, semantics.HitCount)
                       + Math.Max(0d, semantics.TrueDamage)
                       + Math.Max(0d, semantics.DamageOverTime);
        return perEnemy * Math.Max(
            1,
            semantics.AffectedEnemyCount > 0
                ? semantics.AffectedEnemyCount
                : enemyCount);
    }

    private static double EstimateSemanticsValue(
        CombatActionSemantics semantics,
        int enemyCount)
    {
        var affected = Math.Max(
            1,
            semantics.AffectedEnemyCount > 0
                ? semantics.AffectedEnemyCount
                : enemyCount);
        return Math.Max(0d, semantics.Damage) * affected * 0.45d
               + Math.Max(0d, semantics.TrueDamage) * affected * 0.6d
               + Math.Max(0d, semantics.DamageOverTime) * affected * 0.45d
               + Math.Max(0d, semantics.Defend) * 0.3d
               + Math.Max(0d, semantics.Heal) * 0.35d
               + Math.Max(0d, semantics.Draw) * 1.4d
               + Math.Max(0d, semantics.EnergyGain)
               + Math.Max(0d, semantics.CardGeneration)
               + Math.Max(0d, semantics.Buff) * 0.5d
               + Math.Max(0d, semantics.Debuff) * 0.45d
               + Math.Max(0d, semantics.Scaling)
               + Math.Max(0d, semantics.PersistentValue)
               + Math.Max(0d, semantics.DamageMultiplierGain) * 100d;
    }

    private static bool HasRecognizedValue(CombatActionSemantics semantics)
    {
        return EstimateSemanticsValue(semantics, 1) > Epsilon
               || semantics.Risk > Epsilon
               || semantics.SelfHpLoss > Epsilon
               || semantics.EndOfCycleSelfHpLoss > Epsilon;
    }

    private static bool HasTag(
        CombatStateObservation state,
        string cardId,
        string tag)
    {
        return state.CardTagsById.TryGetValue(cardId ?? "", out var tags)
               && tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }

    private static bool Flag(
        IReadOnlyDictionary<string, double>? values,
        string key)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && value > 0.5d;
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}
