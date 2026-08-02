using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatTurnFeatureNames
{
    public const string ActionsTakenThisTurn = "turnActionsTaken";
    public const string EnergySpentThisTurn = "turnEnergySpent";
    public const string EnemyHpAtTurnStart = "enemyHpAtTurnStart";
    public const string ConsecutiveNoProgressTurns = "consecutiveNoProgressTurns";
    public const string NoEffectActionAttemptsThisTurn =
        "noEffectActionAttemptsThisTurn";
    public const string TurnSequence = "turnSequence";
    public const string EndTurnPurposeValue = "endTurnPurposeValue";
    public const string EndTurnPurposeCount = "endTurnPurposeCount";
    public const string EndTurnSevereMistake = "endTurnSevereMistake";
    public const string EndTurnSafeAlternativeCount = "endTurnSafeAlternativeCount";
    public const string EndTurnPlayableCardCount = "endTurnPlayableCardCount";
    public const string EndTurnUnusedEnergy = "endTurnUnusedEnergy";
    public const string EndTurnAvoidableUnusedEnergy =
        "endTurnAvoidableUnusedEnergy";
    public const string EndTurnExpiringEnergy = "endTurnExpiringEnergy";
    public const string EndTurnBankedSurplusEnergy =
        "endTurnBankedSurplusEnergy";
    public const string EndTurnDominated = "endTurnDominated";
    public const string EndTurnAvoidableLethal = "endTurnAvoidableLethal";
    public const string EndTurnCertifiedCycleCount =
        "endTurnCertifiedCycleCount";
    public const string EndTurnReachableCycleCount =
        "endTurnReachableCycleCount";
    public const string EndTurnDominanceMargin = "endTurnDominanceMargin";
    public const string EndTurnUnknownLifecycleCount =
        "endTurnUnknownLifecycleCount";
    public const string EndTurnLifecycleHpLoss = "endTurnLifecycleHpLoss";
    public const string EndTurnLifecycleHeal = "endTurnLifecycleHeal";
    public const string EndTurnLifecycleDefend = "endTurnLifecycleDefend";
    public const string EndTurnLifecyclePowerGain =
        "endTurnLifecyclePowerGain";
    public const string EndTurnLifecyclePowerLoss =
        "endTurnLifecyclePowerLoss";
    public const string EndTurnLifecycleDraw = "endTurnLifecycleDraw";
    public const string StartTurnLifecycleHpLoss =
        "startTurnLifecycleHpLoss";
    public const string StartTurnLifecycleHeal = "startTurnLifecycleHeal";
    public const string StartTurnLifecycleDefend =
        "startTurnLifecycleDefend";
    public const string StartTurnLifecyclePowerGain =
        "startTurnLifecyclePowerGain";
    public const string StartTurnLifecyclePowerLoss =
        "startTurnLifecyclePowerLoss";
    public const string StartTurnLifecycleDraw = "startTurnLifecycleDraw";
    public const string UnknownLifecycleEffectCount =
        "unknownLifecycleEffectCount";
}

public sealed class CombatEndTurnAssessment
{
    public bool Prohibited { get; set; }

    public bool SevereMistake { get; set; }

    public bool HasDeliberatePurpose { get; set; }

    public int SafeAlternativeCount { get; set; }

    public int PlayableCardCount { get; set; }

    public int ActionsTakenThisTurn { get; set; }

    public int UnusedEnergy { get; set; }

    public int AvoidableUnusedEnergy { get; set; }

    public int ConsecutiveNoProgressTurns { get; set; }

    public double PurposeValue { get; set; }

    public double OpportunityCost { get; set; }

    public CombatEndTurnVerdict Verdict { get; set; }

    public CombatEndTurnProjection Projection { get; set; } = new();

    public CombatEndTurnDecisionTrace Trace { get; set; } = new();

    public string BestAlternativeId { get; set; } = "";

    public double BestAlternativeNetBenefit { get; set; }

    public double DominanceMargin { get; set; }

    public int CertifiedCycleCount { get; set; }

    public int ReachableCycleCount { get; set; }

    public bool AvoidableLethal { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatEndTurnSafety
{
    public static CombatEndTurnAssessment AssessObservation(
        CombatStateObservation state,
        CombatDecisionProfile? profile = null,
        bool useRuntimeRegistries = true)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var selectedProfile = profile ?? new CombatDecisionProfile();
        selectedProfile.Weights ??= new AuraDecision.Shared.DecisionWeights();
        state = CombatPlayerObservationBoundary.Normalize(state);
        var evaluations = new List<CombatCandidateEvaluation>(
            state.Actions.Count);
        foreach (var action in state.Actions)
        {
            if (action == null)
            {
                continue;
            }
            var legal = action.Legal;
            var rejectionReason = action.RejectionReason;
            if (legal && action.Kind != CombatActionKind.EndTurn)
            {
                legal = CombatArchetypePolicy.IsLegal(
                    state,
                    action,
                    out rejectionReason);
            }
            if (legal
                && action.Kind != CombatActionKind.EndTurn
                && useRuntimeRegistries)
            {
                legal = CombatAiRegistry.EvaluatePreflight(
                    state,
                    action,
                    out rejectionReason);
                if (legal)
                {
                    CombatAiRegistry.ApplySemantics(state, action);
                    action.Semantics =
                        CombatPlayerObservationBoundary.NormalizeSemantics(
                            action.Semantics);
                }
            }
            var utility = action.Kind == CombatActionKind.EndTurn
                ? new AuraDecision.Shared.DecisionUtility()
                : CombatDecisionEngine.BuildUtility(
                    state,
                    action,
                    selectedProfile);
            action.Features = CombatDecisionEngine.BuildFeatures(
                state,
                action,
                utility,
                selectedProfile);
            evaluations.Add(new CombatCandidateEvaluation
            {
                Action = action,
                Legal = legal,
                RejectionReason = legal ? "" : rejectionReason,
                Utility = utility,
                BaseRuleScore = legal
                    ? selectedProfile.Weights.Score(utility)
                    : 0d,
                RuleScore = legal
                    ? selectedProfile.Weights.Score(utility)
                    : 0d
            });
        }
        return Assess(state, evaluations, selectedProfile);
    }

    public static CombatEndTurnAssessment Assess(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile profile)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var purposeValue = Feature(
            state.Features,
            CombatTurnFeatureNames.EndTurnPurposeValue);
        var actionsTaken = Math.Max(
            0,
            (int)Math.Round(Feature(
                state.Features,
                CombatTurnFeatureNames.ActionsTakenThisTurn)));
        var noProgressTurns = Math.Max(
            0,
            (int)Math.Round(Feature(
                state.Features,
                CombatTurnFeatureNames.ConsecutiveNoProgressTurns)));
        var projection = CombatTurnRules.ProjectEndTurn(state, profile);
        var opportunities = candidates
            .Where(candidate =>
                candidate?.Action != null
                && !IsEndTurnEquivalent(candidate.Action)
                && candidate.Legal
                && !IsVisibleFake(candidate.Action)
                && candidate.Action.Cost <= state.CurrentPower)
            .Select(candidate =>
            {
                var productivity =
                    CombatActionProductivity.Assess(state, candidate);
                return new CombatContinuationOpportunity
                {
                    Candidate = candidate,
                    Productivity = productivity,
                    CycleClassification =
                        productivity.CycleClassification,
                    NetBenefit = productivity.NetBenefit,
                    EnergyCarryOpportunityCost =
                        productivity.EnergyCarryOpportunityCost,
                    PreventsProjectedLethal =
                        PreventsProjectedLethal(
                            state,
                            candidate,
                            productivity,
                            projection)
                };
            })
            .ToList();
        var safe = opportunities
            .Where(opportunity => opportunity.Productivity.Productive)
            .ToList();
        var playableCards = safe.Count(opportunity =>
            opportunity.Candidate.Action.Kind == CombatActionKind.PlayCard);
        var certifiedCycles = opportunities.Count(opportunity =>
            opportunity.CycleClassification
            == CombatCycleOpportunityClassification.Certified);
        var reachableCycles = opportunities.Count(opportunity =>
            opportunity.CycleClassification
            == CombatCycleOpportunityClassification.Reachable);
        var avoidableLethal = projection.ProjectedLethal
                              && opportunities.Any(opportunity =>
                                  opportunity.PreventsProjectedLethal);
        var best = safe
            .OrderByDescending(opportunity => opportunity.NetBenefit)
            .ThenBy(
                opportunity => opportunity.Candidate.Action.CandidateId,
                StringComparer.Ordinal)
            .FirstOrDefault();
        var hasPurpose = purposeValue > 0.000001d;
        var unusedEnergy = Math.Max(0, state.CurrentPower);
        var avoidableUnusedEnergy = safe.Count > 0
            ? projection.ExpiringPower
            : 0;
        var verdict = avoidableLethal
            ? CombatEndTurnVerdict.BlockedLethal
            : certifiedCycles > 0
                ? CombatEndTurnVerdict.BlockedCycle
                : safe.Count > 0
                    ? CombatEndTurnVerdict.BlockedImmediate
                    : opportunities.Count == 0
                        ? CombatEndTurnVerdict.Forced
                        : hasPurpose
                            ? CombatEndTurnVerdict.AllowedStrategic
                            : projection.UnknownLifecycleEffectCount > 0
                                ? CombatEndTurnVerdict.Uncertain
                                : CombatEndTurnVerdict.AllowedExhausted;
        var severe = verdict is CombatEndTurnVerdict.BlockedImmediate
            or CombatEndTurnVerdict.BlockedLethal
            or CombatEndTurnVerdict.BlockedCycle;
        var dominanceMargin = Math.Max(
            0d,
            best != null
                ? best.NetBenefit
                : certifiedCycles > 0
                    ? 2d
                    : 0d);
        var reason = severe
            ? "end turn blocked: " + VerdictReason(verdict)
              + ", actions=" + actionsTaken
              + ", playableCards=" + playableCards
              + ", unusedEnergy=" + unusedEnergy
              + ", expiringEnergy=" + projection.ExpiringPower
              + ", bankedSurplus=" + projection.BankedSurplusPower
              + ", certifiedCycles=" + certifiedCycles
              + ", reachableCycles=" + reachableCycles
              + ", noProgressTurns=" + noProgressTurns
            : hasPurpose
                ? "end turn lifecycle purpose is admissible because no productive action remains"
                : verdict == CombatEndTurnVerdict.Uncertain
                    ? "no productive action remains; lifecycle projection contains unknown effects"
                    : verdict == CombatEndTurnVerdict.Forced
                        ? "no executable non-end action remains"
                        : "no safe positive action remains";
        var trace = new CombatEndTurnDecisionTrace
        {
            Verdict = verdict,
            BestAlternativeId = best?.Candidate.Action.CandidateId ?? "",
            BestAlternativeNetBenefit = best?.NetBenefit ?? 0d,
            DominanceMargin = dominanceMargin,
            ProductiveAlternativeCount = safe.Count,
            CertifiedCycleCount = certifiedCycles,
            ReachableCycleCount = reachableCycles,
            AvoidableLethal = avoidableLethal,
            Projection = projection
        };
        return new CombatEndTurnAssessment
        {
            Prohibited = severe,
            SevereMistake = severe,
            HasDeliberatePurpose = hasPurpose,
            SafeAlternativeCount = safe.Count,
            PlayableCardCount = playableCards,
            ActionsTakenThisTurn = actionsTaken,
            UnusedEnergy = unusedEnergy,
            AvoidableUnusedEnergy = avoidableUnusedEnergy,
            ConsecutiveNoProgressTurns = noProgressTurns,
            PurposeValue = purposeValue,
            Verdict = verdict,
            Projection = projection,
            Trace = trace,
            BestAlternativeId = trace.BestAlternativeId,
            BestAlternativeNetBenefit = trace.BestAlternativeNetBenefit,
            DominanceMargin = dominanceMargin,
            CertifiedCycleCount = certifiedCycles,
            ReachableCycleCount = reachableCycles,
            AvoidableLethal = avoidableLethal,
            OpportunityCost = severe
                ? 100d
                  + safe.Count * 8d
                  + playableCards * 10d
                  + avoidableUnusedEnergy * 12d
                  + (actionsTaken == 0 ? 24d : 0d)
                  + noProgressTurns * 16d
                  + certifiedCycles * 24d
                  + (avoidableLethal ? 80d : 0d)
                  + dominanceMargin * 4d
                : 0d,
            Reason = reason
        };
    }

    public static bool HasDeliberatePurpose(
        IReadOnlyDictionary<string, double>? features)
    {
        return Feature(features, CombatTurnFeatureNames.EndTurnPurposeValue)
               > 0.000001d;
    }

    public static bool IsSafeAlternative(
        CombatStateObservation state,
        CombatCandidateEvaluation candidate,
        CombatDecisionProfile profile)
    {
        if (candidate?.Action == null
            || !candidate.Legal
            || IsEndTurnEquivalent(candidate.Action)
            || IsVisibleFake(candidate.Action)
            || candidate.Action.Cost > state.CurrentPower)
        {
            return false;
        }

        return CombatActionProductivity.Assess(state, candidate).Productive;
    }

    public static bool IsSafeAlternative(
        CombatSimulationState state,
        CombatActionObservation action,
        int effectiveCost,
        CombatDecisionProfile profile)
    {
        return CombatActionProductivity.IsProductive(
            state,
            action,
            effectiveCost,
            profile);
    }

    public static void Annotate(
        CombatActionObservation endTurn,
        CombatCandidateEvaluation evaluation,
        CombatEndTurnAssessment assessment)
    {
        if (endTurn == null) throw new ArgumentNullException(nameof(endTurn));
        if (evaluation == null) throw new ArgumentNullException(nameof(evaluation));
        if (assessment == null) throw new ArgumentNullException(nameof(assessment));

        endTurn.Features[CombatTurnFeatureNames.EndTurnSevereMistake] =
            assessment.SevereMistake ? 1d : 0d;
        endTurn.Features[CombatTurnFeatureNames.EndTurnSafeAlternativeCount] =
            assessment.SafeAlternativeCount;
        endTurn.Features[CombatTurnFeatureNames.EndTurnPlayableCardCount] =
            assessment.PlayableCardCount;
        endTurn.Features[CombatTurnFeatureNames.EndTurnUnusedEnergy] =
            assessment.UnusedEnergy;
        endTurn.Features[
                CombatTurnFeatureNames.EndTurnAvoidableUnusedEnergy] =
            assessment.AvoidableUnusedEnergy;
        endTurn.Features[CombatTurnFeatureNames.EndTurnExpiringEnergy] =
            assessment.Projection.ExpiringPower;
        endTurn.Features[CombatTurnFeatureNames.EndTurnBankedSurplusEnergy] =
            assessment.Projection.BankedSurplusPower;
        endTurn.Features[CombatTurnFeatureNames.EndTurnDominated] =
            assessment.Prohibited ? 1d : 0d;
        endTurn.Features[CombatTurnFeatureNames.EndTurnAvoidableLethal] =
            assessment.AvoidableLethal ? 1d : 0d;
        endTurn.Features[CombatTurnFeatureNames.EndTurnCertifiedCycleCount] =
            assessment.CertifiedCycleCount;
        endTurn.Features[CombatTurnFeatureNames.EndTurnReachableCycleCount] =
            assessment.ReachableCycleCount;
        endTurn.Features[CombatTurnFeatureNames.EndTurnDominanceMargin] =
            assessment.DominanceMargin;
        endTurn.Features[CombatTurnFeatureNames.EndTurnUnknownLifecycleCount] =
            assessment.Projection.UnknownLifecycleEffectCount;
        endTurn.Features[CombatTurnFeatureNames.EndTurnPurposeValue] =
            assessment.PurposeValue;
        if (assessment.Prohibited)
        {
            evaluation.Legal = false;
            evaluation.BaseRuleScore = -assessment.OpportunityCost;
            evaluation.RuleScore = -assessment.OpportunityCost;
            evaluation.RejectionReason = assessment.Reason;
        }
    }

    public static bool IsEndTurnEquivalent(CombatActionObservation? action)
    {
        return action != null
               && (action.Kind == CombatActionKind.EndTurn
                   || action.Semantics?.EndsTurn == true);
    }

    private static bool PreventsProjectedLethal(
        CombatStateObservation state,
        CombatCandidateEvaluation candidate,
        CombatActionProductivityAssessment productivity,
        CombatEndTurnProjection projection)
    {
        if (!projection.ProjectedLethal
            || !productivity.Productive
            || candidate?.Action == null)
        {
            return false;
        }
        var action = candidate.Action;
        if (Positive(action.Features, "immediateDefend") > 0d
            || Positive(action.Features, "effectiveHeal") > 0d)
        {
            return true;
        }
        var livingEnemies = state.Enemies.Count(enemy => enemy.Alive);
        return livingEnemies == 1
               && (Positive(action.Features, "lethal") > 0.5d
                   || Positive(
                       action.Features,
                       "effectiveHpDamage") >= state.Enemies
                       .Where(enemy => enemy.Alive)
                       .Sum(enemy => Math.Max(0, enemy.CurrentHp)));
    }

    private static string VerdictReason(CombatEndTurnVerdict verdict)
    {
        return verdict switch
        {
            CombatEndTurnVerdict.BlockedLethal =>
                "a legal action prevents projected lethal damage",
            CombatEndTurnVerdict.BlockedCycle =>
                "a deterministic executable positive cycle remains",
            _ => "a positive action remains after carry-resource cost"
        };
    }

    private static double Positive(
        IReadOnlyDictionary<string, double> features,
        string key)
    {
        return features.TryGetValue(key, out var value)
               && IsFinite(value)
            ? Math.Max(0d, value)
            : 0d;
    }

    public static double ScoreNativeEndTurnPurpose(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)
            || !ContainsAny(
                script!,
                "AddEvent(\"EndRound\"",
                "AddEvent (\"EndRound\"",
                "TurnEnded"))
        {
            return 0d;
        }

        var source = script!;
        var score = 0d;
        if (ContainsAny(source, "GiveWin", "WinTheFight"))
        {
            score += 100d;
        }
        if (ContainsAny(source, "ChangeDefence", "GainBlock"))
        {
            score += 8d;
        }
        if (ContainsAny(source, "UseCard(", "RunImmediately("))
        {
            score += 8d;
        }
        if (ContainsAny(source, "CreateCard(", "AddCard", "DrawCount("))
        {
            score += 5d;
        }
        if (ContainsAny(source, "ChangePower(", "GainEnergy"))
        {
            score += 4d;
        }
        if (ContainsAny(source, "AddBuff(", "RemoveAllBadBuff", "Cleanse"))
        {
            score += 3d;
        }
        if (ContainsAny(source, "ChangeHp((")
            && !ContainsAny(source, "ChangeHp((-", "ChangeHp(\"-"))
        {
            score += 2d;
        }
        return score;
    }

    private static bool IsVisibleFake(CombatActionObservation action)
    {
        return action.Features.TryGetValue("visibleFake", out var value)
               && value > 0.5d;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token =>
            value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static double Feature(
        IReadOnlyDictionary<string, double>? values,
        string key)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
