using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public enum CombatEndTurnVerdict
{
    Forced,
    BlockedImmediate,
    BlockedLethal,
    BlockedCycle,
    AllowedExhausted,
    AllowedStrategic,
    Uncertain
}

public enum CombatCycleOpportunityClassification
{
    None,
    Speculative,
    Reachable,
    Certified
}

public sealed class CombatEndTurnProjection
{
    public int CurrentPower { get; set; }

    public int MaxPower { get; set; }

    public int NextTurnPower { get; set; }

    public int RefilledPower { get; set; }

    public int BankedSurplusPower { get; set; }

    public int ExpiringPower { get; set; }

    public int RetainedHandCount { get; set; }

    public int UnretainedHandCount { get; set; }

    public int DrawPileCount { get; set; }

    public int DiscardPileCount { get; set; }

    public int EffectiveNextDraw { get; set; }

    public bool ReshuffleDuringNextDraw { get; set; }

    public int UnretainedReturnDelayTurns { get; set; }

    public double EndTurnHpLoss { get; set; }

    public double EndTurnHeal { get; set; }

    public double EndTurnDefend { get; set; }

    public double EndTurnPowerGain { get; set; }

    public double EndTurnPowerLoss { get; set; }

    public double EndTurnDraw { get; set; }

    public double StartTurnHpLoss { get; set; }

    public double StartTurnHeal { get; set; }

    public double StartTurnDefend { get; set; }

    public double StartTurnPowerGain { get; set; }

    public double StartTurnPowerLoss { get; set; }

    public double StartTurnDraw { get; set; }

    public double ExpectedThreatHpLoss { get; set; }

    public int ProjectedPlayerHp { get; set; }

    public bool ProjectedLethal { get; set; }

    public int UnknownLifecycleEffectCount { get; set; }

    public double PurposeValue { get; set; }

    public double LowerBoundValue { get; set; }

    public double UpperBoundValue { get; set; }
}

public sealed class CombatContinuationOpportunity
{
    public CombatCandidateEvaluation Candidate { get; set; } = new();

    public CombatActionProductivityAssessment Productivity { get; set; } = new();

    public CombatCycleOpportunityClassification CycleClassification { get; set; }

    public double NetBenefit { get; set; }

    public double EnergyCarryOpportunityCost { get; set; }

    public bool PreventsProjectedLethal { get; set; }
}

public sealed class CombatEndTurnDecisionTrace
{
    public CombatEndTurnVerdict Verdict { get; set; }

    public string BestAlternativeId { get; set; } = "";

    public double BestAlternativeNetBenefit { get; set; }

    public double DominanceMargin { get; set; }

    public int ProductiveAlternativeCount { get; set; }

    public int CertifiedCycleCount { get; set; }

    public int ReachableCycleCount { get; set; }

    public bool AvoidableLethal { get; set; }

    public CombatEndTurnProjection Projection { get; set; } = new();

    public string ToCompactString()
    {
        var projection = Projection ?? new CombatEndTurnProjection();
        return "endTurnVerdict=" + Verdict
               + " best=" + (BestAlternativeId.Length == 0 ? "none" : BestAlternativeId)
               + " bestNet=" + Number(BestAlternativeNetBenefit)
               + " margin=" + Number(DominanceMargin)
               + " alternatives=" + ProductiveAlternativeCount
               + " cycles=" + CertifiedCycleCount + "/" + ReachableCycleCount
               + " lethal=" + (AvoidableLethal ? "1" : "0")
               + " power=" + projection.CurrentPower
               + "/" + projection.MaxPower
               + "->" + projection.NextTurnPower
               + " refill=" + projection.RefilledPower
               + " surplus=" + projection.BankedSurplusPower
               + " handDrop=" + projection.UnretainedHandCount
               + " retain=" + projection.RetainedHandCount
               + " draw=" + projection.EffectiveNextDraw
               + " reshuffle=" + (projection.ReshuffleDuringNextDraw ? "1" : "0")
               + " returnTurns=" + projection.UnretainedReturnDelayTurns
               + " threatLoss=" + Number(projection.ExpectedThreatHpLoss)
               + " lifecycleLoss="
               + Number(projection.EndTurnHpLoss + projection.StartTurnHpLoss)
               + " lifecycleUnknown=" + projection.UnknownLifecycleEffectCount
               + " projectedHp=" + projection.ProjectedPlayerHp;
    }

    private static string Number(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

public static class CombatTurnRules
{
    private const double Epsilon = 0.000001d;

    public static int NextTurnPower(int currentPower, int maxPower)
    {
        return CombatTurnTransitionRules.NextTurnPower(
            currentPower,
            maxPower);
    }

    public static double NextTurnPower(double currentPower, double maxPower)
    {
        return CombatTurnTransitionRules.NextTurnPower(
            currentPower,
            maxPower);
    }

    public static double EnergyCarryOpportunityCost(
        double currentPower,
        double maxPower,
        double actionCost,
        double actionEnergyGain)
    {
        return CombatTurnTransitionRules.EnergyCarryOpportunityCost(
            currentPower,
            maxPower,
            actionCost,
            actionEnergyGain);
    }

    public static CombatEndTurnProjection ProjectEndTurn(
        CombatStateObservation state,
        CombatDecisionProfile profile)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var retained = Math.Min(
            Math.Max(0, state.HandCount),
            state.RetainedHandCardIds?.Count ?? 0);
        var unretained = Math.Max(0, state.HandCount - retained);
        var drawPile = Math.Max(
            0,
            state.DeckKnowledge?.DrawPileCount
            ?? IntegerFeature(state.Features, "drawPileCount"));
        var discardPile = Math.Max(
            0,
            state.DeckKnowledge?.DiscardPileCount
            ?? IntegerFeature(state.Features, "discardPileCount"));
        var handLimit = Math.Max(
            1,
            IntegerFeature(state.Features, "handLimit", 10));
        var requestedDraw = Math.Max(
            0,
            IntegerFeature(state.Features, "drawPerTurn", 5));
        var effectiveDraw = Math.Min(
            requestedDraw,
            Math.Max(0, handLimit - retained));
        var reshuffle = effectiveDraw > drawPile
                        && discardPile + unretained > 0;
        var returnDelay = drawPile <= 0
            ? 0
            : effectiveDraw <= 0
                ? int.MaxValue
                : Math.Max(
                    1,
                    (int)Math.Ceiling(drawPile / (double)effectiveDraw));

        var endHpLoss = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.EndTurnLifecycleHpLoss);
        var endHeal = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.EndTurnLifecycleHeal);
        var endDefend = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.EndTurnLifecycleDefend);
        var endPowerGain = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.EndTurnLifecyclePowerGain);
        var endPowerLoss = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.EndTurnLifecyclePowerLoss);
        var endDraw = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.EndTurnLifecycleDraw);
        var startHpLoss = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.StartTurnLifecycleHpLoss);
        var startHeal = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.StartTurnLifecycleHeal);
        var startDefend = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.StartTurnLifecycleDefend);
        var startPowerGain = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.StartTurnLifecyclePowerGain);
        var startPowerLoss = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.StartTurnLifecyclePowerLoss);
        var startDraw = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.StartTurnLifecycleDraw);
        effectiveDraw = Math.Min(
            Math.Max(0, handLimit - retained),
            requestedDraw + Math.Max(0, (int)Math.Floor(startDraw)));
        reshuffle = effectiveDraw > drawPile
                    && discardPile + unretained > 0;
        returnDelay = drawPile <= 0
            ? 0
            : effectiveDraw <= 0
                ? int.MaxValue
                : Math.Max(
                    1,
                    (int)Math.Ceiling(drawPile / (double)effectiveDraw));
        var unknownLifecycle = Math.Max(
            0,
            IntegerFeature(
                state.Features,
                CombatTurnFeatureNames.UnknownLifecycleEffectCount));

        var threat = state.Threat ?? new CombatThreatForecast();
        var expectedBlockable = Math.Max(0d, threat.ExpectedBlockableDamage);
        var expectedUnblockable = Math.Max(
            0d,
            threat.ExpectedUnblockableDamage);
        var expectedDot = Math.Max(0d, threat.ExpectedDamageOverTime);
        var availableDefend = Math.Max(
            0d,
            (state.Player?.Defend ?? 0) + endDefend);
        var threatHpLoss = Math.Max(0d, expectedBlockable - availableDefend)
                           + expectedUnblockable
                           + expectedDot;
        var currentHp = Math.Max(0, state.Player?.CurrentHp ?? 0);
        var projectedHp = Math.Max(
            0,
            (int)Math.Floor(
                currentHp
                + endHeal
                - endHpLoss
                - threatHpLoss
                + startHeal
                - startHpLoss));
        var powerBeforeRefill = Math.Max(
            0,
            state.CurrentPower
            + (int)Math.Floor(endPowerGain)
            - (int)Math.Ceiling(endPowerLoss));
        var nextPower = NextTurnPower(powerBeforeRefill, state.MaxPower)
                        + Math.Max(0, (int)Math.Floor(startPowerGain))
                        - Math.Max(0, (int)Math.Ceiling(startPowerLoss));
        nextPower = Math.Max(0, nextPower);
        var purpose = PositiveFeature(
            state.Features,
            CombatTurnFeatureNames.EndTurnPurposeValue);
        var knownBenefit = purpose
                           + endHeal
                           + startHeal
                           + endDefend * 0.5d
                           + startDefend * 0.5d
                           + endPowerGain
                           + startPowerGain
                           + startDraw * 0.5d;
        var knownHarm = (endHpLoss + startHpLoss) * 2d
                        + threatHpLoss
                        + endPowerLoss
                        + startPowerLoss;
        var uncertainty = unknownLifecycle
                          * Math.Max(0d, profile.UncertaintyPenalty);
        return new CombatEndTurnProjection
        {
            CurrentPower = Math.Max(0, state.CurrentPower),
            MaxPower = Math.Max(0, state.MaxPower),
            NextTurnPower = nextPower,
            RefilledPower = Math.Max(0, nextPower - powerBeforeRefill),
            BankedSurplusPower = Math.Max(
                0,
                state.CurrentPower - state.MaxPower),
            ExpiringPower = state.CurrentPower <= state.MaxPower
                ? Math.Max(0, state.CurrentPower)
                : 0,
            RetainedHandCount = retained,
            UnretainedHandCount = unretained,
            DrawPileCount = drawPile,
            DiscardPileCount = discardPile,
            EffectiveNextDraw = effectiveDraw,
            ReshuffleDuringNextDraw = reshuffle,
            UnretainedReturnDelayTurns = returnDelay,
            EndTurnHpLoss = endHpLoss,
            EndTurnHeal = endHeal,
            EndTurnDefend = endDefend,
            EndTurnPowerGain = endPowerGain,
            EndTurnPowerLoss = endPowerLoss,
            EndTurnDraw = endDraw,
            StartTurnHpLoss = startHpLoss,
            StartTurnHeal = startHeal,
            StartTurnDefend = startDefend,
            StartTurnPowerGain = startPowerGain,
            StartTurnPowerLoss = startPowerLoss,
            StartTurnDraw = startDraw,
            ExpectedThreatHpLoss = threatHpLoss,
            ProjectedPlayerHp = projectedHp,
            ProjectedLethal = currentHp > 0 && projectedHp <= 0,
            UnknownLifecycleEffectCount = unknownLifecycle,
            PurposeValue = purpose,
            LowerBoundValue = knownBenefit - knownHarm - uncertainty,
            UpperBoundValue = knownBenefit - knownHarm + uncertainty
        };
    }

    private static int IntegerFeature(
        IReadOnlyDictionary<string, double>? features,
        string key,
        int fallback = 0)
    {
        if (features == null
            || !features.TryGetValue(key, out var value)
            || !IsFinite(value))
        {
            return fallback;
        }
        return (int)Math.Max(
            int.MinValue,
            Math.Min(int.MaxValue, Math.Round(value)));
    }

    private static double PositiveFeature(
        IReadOnlyDictionary<string, double>? features,
        string key)
    {
        if (features == null
            || !features.TryGetValue(key, out var value)
            || !IsFinite(value))
        {
            return 0d;
        }
        return Math.Max(0d, value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public static class CombatCycleOpportunityClassifier
{
    public static CombatCycleOpportunityClassification Classify(
        CombatActionObservation action)
    {
        if (action == null)
        {
            return CombatCycleOpportunityClassification.None;
        }
        var infinite = Flag(action.Features, "strategyInfinite");
        var executable = Flag(action.Features, "strategyExecutable");
        var deterministic = Flag(action.Features, "strategyDeterministic");
        var completion = Positive(action.Features, "strategyCompletion");
        if (infinite && executable && deterministic)
        {
            return CombatCycleOpportunityClassification.Certified;
        }
        if (infinite && (executable || completion >= 0.75d))
        {
            return CombatCycleOpportunityClassification.Reachable;
        }
        if (infinite
            || Flag(action.Features, "recycle")
            || Flag(action.Features, "ouroboros"))
        {
            return CombatCycleOpportunityClassification.Speculative;
        }
        return CombatCycleOpportunityClassification.None;
    }

    public static double ConnectorValue(
        CombatCycleOpportunityClassification classification,
        IReadOnlyDictionary<string, double> features)
    {
        var synergy = Positive(features, "synergy");
        return classification switch
        {
            CombatCycleOpportunityClassification.Certified =>
                Math.Max(2d, synergy),
            CombatCycleOpportunityClassification.Reachable =>
                Math.Max(0.75d, synergy),
            CombatCycleOpportunityClassification.Speculative =>
                Math.Max(0d, synergy * 0.25d),
            _ => 0d
        };
    }

    private static bool Flag(
        IReadOnlyDictionary<string, double> features,
        string key)
    {
        return features != null
               && features.TryGetValue(key, out var value)
               && IsFinite(value)
               && value > 0.5d;
    }

    private static double Positive(
        IReadOnlyDictionary<string, double> features,
        string key)
    {
        return features != null
               && features.TryGetValue(key, out var value)
               && IsFinite(value)
            ? Math.Max(0d, value)
            : 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
