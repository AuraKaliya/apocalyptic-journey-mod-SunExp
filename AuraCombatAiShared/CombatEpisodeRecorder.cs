using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatEpisodeRecordingPolicy :
    ICombatSimulationPolicy,
    ICombatSimulationBorrowedStatePolicy,
    ICombatSimulationPolicyMetricsProvider
{
    private readonly ICombatSimulationPolicy inner;
    private readonly string decisionProfile;
    private readonly List<CombatEpisodeFrame> frames = new();

    public CombatEpisodeRecordingPolicy(
        ICombatSimulationPolicy inner,
        string decisionProfile)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.decisionProfile = string.IsNullOrWhiteSpace(decisionProfile)
            ? "balanced"
            : decisionProfile.Trim().ToLowerInvariant();
    }

    public string PolicyId => inner.PolicyId + ":episode";

    public IReadOnlyList<CombatEpisodeFrame> Frames => frames;

    public CombatSimulationPolicyDecisionMetrics LastDecisionMetrics =>
        inner is ICombatSimulationPolicyMetricsProvider metrics
            ? metrics.LastDecisionMetrics
            : EmptyDecisionMetrics;

    private static CombatSimulationPolicyDecisionMetrics EmptyDecisionMetrics { get; } =
        new();

    public CombatSimulationAction? SelectAction(CombatSimulationPolicyContext context)
    {
        var selected = inner.SelectAction(context);
        if (inner is CombatDecisionSimulationPolicy decisionPolicy
            && decisionPolicy.LastObservation != null
            && decisionPolicy.LastDecision != null)
        {
            frames.Add(CreateFrame(
                context,
                decisionPolicy.LastObservation,
                decisionPolicy.LastDecision,
                selected));
        }
        return selected;
    }

    public CombatEpisode Complete(CombatSimulationResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }
        var victory = result.Outcome == CombatSimulationOutcome.Victory;
        var defeat = result.Outcome == CombatSimulationOutcome.Defeat
                     || result.Outcome == CombatSimulationOutcome.Invalid;
        var terminal = victory ? 1d : defeat ? -1d : -0.25d;
        var maximumHp = Math.Max(1, result.FinalState?.Player?.MaxHp ?? result.FinalPlayerHp);
        var hpRatio = Math.Max(0d, Math.Min(1d, (double)result.FinalPlayerHp / maximumHp));
        for (var i = 0; i < frames.Count; i++)
        {
            var remainingTurns = Math.Max(0, result.Turns - frames[i].Turn);
            frames[i].LongTermReturn = terminal * Math.Pow(0.99d, remainingTurns);
            frames[i].WinTarget = victory ? 1d : 0d;
            frames[i].DeathTarget = defeat ? 1d : 0d;
            frames[i].RemainingHpRatioTarget = hpRatio;
            frames[i].RemainingTurnsTarget = remainingTurns;
        }
        return new CombatEpisode
        {
            EpisodeId = result.ScenarioId
                        + ":"
                        + result.Seed
                        + ":"
                        + Guid.NewGuid().ToString("N"),
            ScenarioId = result.ScenarioId,
            Seed = result.Seed,
            RulesetHash = result.RulesetHash,
            PolicyId = inner.PolicyId,
            DecisionProfile = decisionProfile,
            Frames = new List<CombatEpisodeFrame>(frames),
            Outcome = result.Outcome.ToString().ToLowerInvariant(),
            Turns = result.Turns,
            FinalPlayerHp = result.FinalPlayerHp,
            FinalPlayerMaxHp = maximumHp,
            DamageTaken = result.Metrics?.DamageTaken ?? 0,
            SemanticCoverage = result.SemanticCoverage,
            Authoritative = result.SemanticCoverage >= 1d
                            && result.Outcome != CombatSimulationOutcome.Invalid
        };
    }

    private static CombatEpisodeFrame CreateFrame(
        CombatSimulationPolicyContext context,
        CombatStateObservation observation,
        CombatDecision decision,
        CombatSimulationAction? selected)
    {
        var frame = new CombatEpisodeFrame
        {
            Turn = context.State?.Turn ?? 0,
            ActionSequence = context.State?.ActionSequence ?? 0,
            StateFingerprint = observation.Fingerprint,
            StateFeatures = CombatPolicyValueEncoding.BuildStateFeatures(observation),
            ExecutedCandidateId = selected?.CandidateId
                                  ?? decision.Action?.CandidateId
                                  ?? ""
        };
        foreach (var candidate in decision.Candidates ?? new List<CombatCandidateEvaluation>())
        {
            if (candidate?.Action == null)
            {
                continue;
            }
            frame.Candidates.Add(new CombatEpisodeCandidate
            {
                CandidateId = candidate.Action.CandidateId,
                SourceId = candidate.Action.SourceId,
                Legal = candidate.Legal,
                SearchVisits = Math.Max(0, candidate.SearchVisits),
                SearchPrior = Finite(candidate.SearchPrior),
                SearchValue = Finite(candidate.PlanScore),
                SearchDeathRisk = Finite(candidate.SearchDeathRisk),
                Features = CombatPolicyValueEncoding.BuildCandidateFeatures(candidate)
            });
        }
        return frame;
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
