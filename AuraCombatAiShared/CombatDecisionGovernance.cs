using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public enum CombatGovernanceDecision
{
    Accept,
    RequireSearch,
    RequireMoreSearch,
    UseSafeFallback,
    Reject
}

public sealed class CombatDecisionPerformanceTelemetry
{
    public double TotalMilliseconds { get; set; }

    public int Simulations { get; set; }

    public int Nodes { get; set; }

    public int ModelEvaluations { get; set; }

    public int ModelCacheHits { get; set; }

    public int OriginalCandidates { get; set; }

    public int RetainedCandidates { get; set; }

    public bool StoppedByTime { get; set; }

    public bool StoppedByModelBudget { get; set; }

    public int MinimumTimeMilliseconds { get; set; }

    public bool MinimumTimeSatisfied { get; set; }

    public bool EarlyStopCertified { get; set; }

    public string StopReason { get; set; } = "";

    public CombatDecisionPerformanceTelemetry Clone()
    {
        return new CombatDecisionPerformanceTelemetry
        {
            TotalMilliseconds = TotalMilliseconds,
            Simulations = Simulations,
            Nodes = Nodes,
            ModelEvaluations = ModelEvaluations,
            ModelCacheHits = ModelCacheHits,
            OriginalCandidates = OriginalCandidates,
            RetainedCandidates = RetainedCandidates,
            StoppedByTime = StoppedByTime,
            StoppedByModelBudget = StoppedByModelBudget,
            MinimumTimeMilliseconds = MinimumTimeMilliseconds,
            MinimumTimeSatisfied = MinimumTimeSatisfied,
            EarlyStopCertified = EarlyStopCertified,
            StopReason = StopReason
        };
    }

    public static CombatDecisionPerformanceTelemetry FromSearch(
        CombatSearchResult search)
    {
        if (search == null)
        {
            return new CombatDecisionPerformanceTelemetry();
        }
        return new CombatDecisionPerformanceTelemetry
        {
            TotalMilliseconds = search.ElapsedMilliseconds,
            Simulations = search.Simulations,
            Nodes = search.Nodes,
            ModelEvaluations = search.ModelEvaluations,
            ModelCacheHits = search.ModelCacheHits,
            OriginalCandidates = search.OriginalCandidateCount,
            RetainedCandidates = search.CandidateCount,
            StoppedByTime = search.StoppedByTime,
            StoppedByModelBudget = search.StoppedByModelBudget,
            MinimumTimeMilliseconds = search.MinimumTimeMilliseconds,
            MinimumTimeSatisfied = search.MinimumTimeSatisfied,
            EarlyStopCertified = search.EarlyStopCertified,
            StopReason = search.StoppedByTime
                ? "time-budget"
                : search.StoppedByModelBudget
                    ? "model-evaluation-budget"
                    : string.IsNullOrWhiteSpace(search.StopReason)
                        ? "completed"
                        : search.StopReason
        };
    }
}

public sealed class CombatDecisionGovernanceVerdict
{
    public CombatGovernanceDecision Decision { get; set; }

    public CombatCandidateEvaluation? Candidate { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatDecisionGovernance
{
    public static CombatDecisionGovernanceVerdict ReviewSearch(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatEndTurnAssessment endTurn,
        CombatSearchResult search,
        CombatDecisionProfile profile)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        if (endTurn == null) throw new ArgumentNullException(nameof(endTurn));
        if (search == null) throw new ArgumentNullException(nameof(search));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var proposed = candidates.FirstOrDefault(candidate =>
            candidate.Legal
            && search.Action != null
            && ReferenceEquals(candidate.Action, search.Action));
        var forcedSingleAction = search.CandidateCount <= 1
                                 || candidates.Count(candidate =>
                                     candidate?.Legal == true) <= 1;
        var certifiedLethal = proposed?.Utility?.Lethal > 0d;
        var minimumSearchEvidenceAccepted =
            search.MinimumTimeSatisfied
            || search.MinimumTimeMilliseconds <= 0
            || search.EarlyStopCertified
            || forcedSingleAction
            || certifiedLethal;
        var confidenceAccepted = !profile.UseLowConfidenceFallback
                                 || search.Confidence
                                 >= Clamp01(profile.MinimumSearchConfidence);
        var proposedPassesEndTurn = proposed != null
                                    && (!CombatEndTurnSafety
                                            .IsEndTurnEquivalent(
                                                proposed.Action)
                                        || !endTurn.Prohibited
                                        || proposed.Utility.Lethal > 0d);
        if (proposedPassesEndTurn
            && minimumSearchEvidenceAccepted
            && confidenceAccepted)
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.Accept,
                Candidate = proposed,
                Reason = "search proposal accepted"
            };
        }

        var fallback = SelectSafeFallback(state, candidates, profile);
        if (proposedPassesEndTurn
            && minimumSearchEvidenceAccepted
            && !confidenceAccepted
            && (fallback == null
                || ReferenceEquals(fallback, proposed)
                || !IsProvenSaferFallback(
                    state,
                    proposed!,
                    fallback,
                    profile)))
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.Accept,
                Candidate = proposed,
                Reason = fallback == null
                    ? "search confidence is low, but no legal safety fallback exists"
                    : ReferenceEquals(fallback, proposed)
                        ? "search confidence is low, but the proposal is already the safest candidate"
                        : "search confidence is low, but the alternative has no minimum-loss safety proof"
            };
        }
        var endTurnCandidate = candidates.FirstOrDefault(candidate =>
            candidate.Legal
            && CombatEndTurnSafety.IsEndTurnEquivalent(candidate.Action));
        if (!minimumSearchEvidenceAccepted)
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.RequireMoreSearch,
                Reason = "minimum search time was not satisfied and no forced, lethal, or dominance certificate exists"
            };
        }
        if (fallback != null
            && (fallback.RuleScore >= profile.MinimumActionScore
                || endTurn.Prohibited))
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.UseSafeFallback,
                Candidate = fallback,
                Reason = !confidenceAccepted
                    ? "search confidence is below the safe threshold"
                    : search.StoppedByTime
                        ? "search deadline requires safe fallback"
                        : search.StoppedByModelBudget
                        ? "model evaluation budget requires safe fallback"
                        : "search proposal did not pass governance"
            };
        }

        if (endTurnCandidate != null && !endTurn.Prohibited)
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.UseSafeFallback,
                Candidate = endTurnCandidate,
                Reason = "end turn is certified by end-turn governance"
            };
        }

        if (fallback != null)
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.UseSafeFallback,
                Candidate = fallback,
                Reason =
                    "minimum-loss fallback is required because end turn is unsafe or unavailable"
            };
        }

        return new CombatDecisionGovernanceVerdict
        {
            Decision = search.StoppedByTime || search.StoppedByModelBudget
                ? CombatGovernanceDecision.RequireMoreSearch
                : CombatGovernanceDecision.Reject,
            Reason = "no governance-approved action is available"
        };
    }

    private static bool IsProvenSaferFallback(
        CombatStateObservation state,
        CombatCandidateEvaluation proposed,
        CombatCandidateEvaluation fallback,
        CombatDecisionProfile profile)
    {
        if (fallback.RuleScore < profile.MinimumActionScore
            && !CombatActionSafetyPolicy.HasMinimumLossCertificate(
                fallback.Action))
        {
            return false;
        }
        var proposedRisk = Math.Max(0d, proposed.SearchDeathRisk);
        var fallbackRisk = Math.Max(0d, fallback.SearchDeathRisk);
        if (fallbackRisk + 0.02d < proposedRisk)
        {
            return true;
        }
        var proposedLoss = CombatActionSafetyPolicy.ProjectedIrreversibleLoss(
            state,
            proposed);
        var fallbackLoss = CombatActionSafetyPolicy.ProjectedIrreversibleLoss(
            state,
            fallback);
        return fallbackLoss + 0.000001d < proposedLoss
               && fallbackRisk <= proposedRisk + 0.005d;
    }

    public static CombatCandidateEvaluation? SelectSafeFallback(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile profile)
    {
        var legal = candidates
            .Where(candidate => candidate?.Legal == true
                                && candidate.Action != null
                                && !CombatEndTurnSafety.IsEndTurnEquivalent(
                                    candidate.Action))
            .ToList();
        if (legal.Count == 0)
        {
            return null;
        }
        var minimumRisk = legal.Min(candidate => candidate.SearchDeathRisk);
        var safe = legal
            .Where(candidate =>
                candidate.SearchDeathRisk <= profile.DeathRiskLimit
                || candidate.SearchDeathRisk <= minimumRisk + 0.01d)
            .Where(candidate => CombatEndTurnSafety.IsSafeAlternative(
                state,
                candidate,
                profile))
            .ToList();
        if (safe.Count == 0)
        {
            safe = legal
                .Where(candidate =>
                    candidate.SearchDeathRisk <= minimumRisk + 0.01d)
                .ToList();
        }
        var aboveMinimum = safe
            .Where(candidate =>
                candidate.RuleScore >= profile.MinimumActionScore)
            .ToList();
        if (aboveMinimum.Count > 0)
        {
            safe = aboveMinimum;
        }

        else
        {
            var minimumLoss = safe.Min(candidate =>
                CombatActionSafetyPolicy.ProjectedIrreversibleLoss(
                    state,
                    candidate));
            safe = safe
                .Where(candidate =>
                    CombatActionSafetyPolicy.ProjectedIrreversibleLoss(
                        state,
                        candidate) <= minimumLoss + 0.000001d)
                .ToList();
            foreach (var candidate in safe)
            {
                CombatActionSafetyPolicy.CertifyMinimumLoss(
                    candidate.Action);
            }
        }
        return safe
            .OrderBy(candidate =>
                candidate.Action.Features.TryGetValue("curse", out var curse)
                && curse > 0d
                    ? 1
                    : 0)
            .ThenBy(candidate => candidate.Action.Semantics.Uncertainty)
            .ThenBy(candidate => candidate.SearchDeathRisk)
            .ThenByDescending(candidate => candidate.RuleScore)
            .ThenBy(candidate => candidate.Action.CandidateId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static double Clamp01(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Max(0d, Math.Min(1d, value));
    }
}
