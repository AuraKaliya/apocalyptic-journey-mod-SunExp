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
            StopReason = search.StoppedByTime
                ? "time-budget"
                : search.StoppedByModelBudget
                    ? "model-evaluation-budget"
                    : search.StoppedEarly
                        ? "stable"
                        : "completed"
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
        if (proposed != null
            && (!CombatEndTurnSafety.IsEndTurnEquivalent(proposed.Action)
                || !endTurn.Prohibited
                || proposed.Utility.Lethal > 0d)
            && (!search.StoppedByTime
                || search.Confidence >= Clamp01(profile.MinimumSearchConfidence)))
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.Accept,
                Candidate = proposed,
                Reason = "search proposal accepted"
            };
        }

        var fallback = SelectSafeFallback(state, candidates, profile);
        if (fallback != null)
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.UseSafeFallback,
                Candidate = fallback,
                Reason = search.StoppedByTime
                    ? "search deadline requires safe fallback"
                    : search.StoppedByModelBudget
                        ? "model evaluation budget requires safe fallback"
                        : "search proposal did not pass governance"
            };
        }

        var endTurnCandidate = candidates.FirstOrDefault(candidate =>
            candidate.Legal
            && CombatEndTurnSafety.IsEndTurnEquivalent(candidate.Action));
        if (endTurnCandidate != null && !endTurn.Prohibited)
        {
            return new CombatDecisionGovernanceVerdict
            {
                Decision = CombatGovernanceDecision.UseSafeFallback,
                Candidate = endTurnCandidate,
                Reason = "end turn is certified by end-turn governance"
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
