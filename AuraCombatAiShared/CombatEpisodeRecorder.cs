using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatEpisodeRecordingPolicy :
    ICombatSimulationPolicy,
    ICombatSimulationBorrowedStatePolicy,
    ICombatSimulationPolicyMetricsProvider,
    ICombatSimulationActionExecutionObserver
{
    private readonly ICombatSimulationPolicy inner;
    private readonly string decisionProfile;
    private readonly string contentSetHash;
    private readonly string ownerModSetHash;
    private readonly string baseModelId;
    private readonly bool recordWorldModelObservation;
    private readonly List<CombatEpisodeFrame> frames = new();

    public CombatEpisodeRecordingPolicy(
        ICombatSimulationPolicy inner,
        string decisionProfile,
        string contentSetHash = "",
        string ownerModSetHash = "",
        string baseModelId = "",
        bool recordWorldModelObservation = true)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.decisionProfile = string.IsNullOrWhiteSpace(decisionProfile)
            ? "balanced"
            : decisionProfile.Trim().ToLowerInvariant();
        this.contentSetHash = string.IsNullOrWhiteSpace(contentSetHash)
            ? CombatContentSetProtocol.EmptyContentSetHash
            : contentSetHash;
        this.ownerModSetHash = string.IsNullOrWhiteSpace(ownerModSetHash)
            ? CombatContentSetProtocol.EmptyOwnerModSetHash
            : ownerModSetHash;
        this.baseModelId = baseModelId ?? "";
        this.recordWorldModelObservation = recordWorldModelObservation;
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
        if (inner is ICombatDecisionTracePolicy decisionPolicy
            && decisionPolicy.LastObservation != null
            && decisionPolicy.LastDecision != null)
        {
            var frame = CreateFrame(
                context,
                decisionPolicy.LastObservation,
                decisionPolicy.LastDecision,
                selected,
                inner is ICombatSimulationPolicyMetricsProvider metrics
                && metrics.LastDecisionMetrics
                       .AuthoritativeTeacherOverrides > 0);
            LinkPreviousFrame(frame);
            frames.Add(frame);
        }
        return selected;
    }

    public void OnActionExecuted(CombatSimulationActionExecution execution)
    {
        if (execution == null) throw new ArgumentNullException(nameof(execution));
        if (inner is ICombatSimulationActionExecutionObserver observer)
        {
            observer.OnActionExecuted(execution);
        }
        if (frames.Count == 0
            || inner is not ICombatDecisionTracePolicy decisionPolicy
            || decisionPolicy.LastDecision == null
            || decisionPolicy.LastObservation == null)
        {
            return;
        }

        var frame = frames[frames.Count - 1];
        var evaluation = decisionPolicy.LastDecision.Candidates
            .FirstOrDefault(item => string.Equals(
                item?.Action?.CandidateId,
                execution.Action.CandidateId,
                StringComparison.Ordinal));
        var recorded = frame.Candidates.FirstOrDefault(item => string.Equals(
            item.CandidateId,
            execution.Action.CandidateId,
            StringComparison.Ordinal));
        if (evaluation?.Action != null && recorded != null)
        {
            recorded.SearchDeathRisk = Finite(evaluation.SearchDeathRisk);
            recorded.SearchValue = Finite(evaluation.PlanScore);
            recorded.SearchMeanReturn = Finite(evaluation.SearchMeanReturn);
            recorded.SearchReturnStandardError =
                Finite(evaluation.SearchReturnStandardError);
            recorded.SearchLowerTailMean =
                Finite(evaluation.SearchLowerTailMean);
            recorded.SetCompactFeatures(
                CombatPolicyValueEncoding.BuildCompactCandidateFeatures(
                    evaluation));
        }
        if (recordWorldModelObservation)
        {
            frame.Observation = CombatWorldModelTokenizer.BuildNormalizedOwned(
                decisionPolicy.LastObservation);
        }
        RefreshStrategicSupervision(frame);
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
            frames[i].TerminalKnown = true;
            frames[i].Terminal = i == frames.Count - 1;
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
            ContentSetHash = contentSetHash,
            OwnerModSetHash = ownerModSetHash,
            BaseModelId = baseModelId,
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

    private CombatEpisodeFrame CreateFrame(
        CombatSimulationPolicyContext context,
        CombatStateObservation observation,
        CombatDecision decision,
        CombatSimulationAction? selected,
        bool authoritativeTeacherOverride)
    {
        var frame = new CombatEpisodeFrame
        {
            Turn = context.State?.Turn ?? 0,
            ActionSequence = context.State?.ActionSequence ?? 0,
            DecisionSequence = frames.Count + 1L,
            BattleSessionId = observation.BattleSessionId,
            StateFingerprint = observation.Fingerprint,
            ExecutedCandidateId = selected?.CandidateId
                                  ?? decision.Action?.CandidateId
                                  ?? "",
            TrainingWeight = authoritativeTeacherOverride ? 1.5d : 1d
        };
        frame.SetCompactStateFeatures(
            CombatPolicyValueEncoding.BuildCompactStateFeatures(observation));
        if (recordWorldModelObservation)
        {
            frame.Observation =
                CombatWorldModelTokenizer.BuildNormalizedOwned(observation);
            CombatEpisodeStorageDiagnostics.WorldModelObservation(built: true);
        }
        else
        {
            CombatEpisodeStorageDiagnostics.WorldModelObservation(built: false);
        }
        foreach (var candidate in (IReadOnlyList<CombatCandidateEvaluation>?)
                     decision.Candidates
                 ?? Array.Empty<CombatCandidateEvaluation>())
        {
            if (candidate?.Action == null)
            {
                continue;
            }
            var episodeCandidate = new CombatEpisodeCandidate
            {
                CandidateId = candidate.Action.CandidateId,
                SourceId = candidate.Action.SourceId,
                Legal = candidate.Legal,
                SearchVisits = Math.Max(0, candidate.SearchVisits),
                SearchPrior = Finite(candidate.SearchPrior),
                SearchValue = Finite(candidate.PlanScore),
                SearchDeathRisk = Finite(candidate.SearchDeathRisk),
                SearchMeanReturn = Finite(candidate.SearchMeanReturn),
                SearchReturnStandardError =
                    Finite(candidate.SearchReturnStandardError),
                SearchLowerTailMean = Finite(candidate.SearchLowerTailMean),
                SearchReturnQuantiles = candidate.SearchReturnQuantiles
                    .Select(Finite)
                    .Take(16)
                    .ToList()
            };
            episodeCandidate.SetCompactFeatures(
                CombatPolicyValueEncoding.BuildCompactCandidateFeatures(candidate));
            frame.Candidates.Add(episodeCandidate);
        }
        RefreshStrategicSupervision(frame);
        return frame;
    }

    private static void RefreshStrategicSupervision(CombatEpisodeFrame frame)
    {
        var strategy = CombatPolicyValueBatchTrainer
            .StrategicFrameSupervisionForExecutedAction(frame);
        frame.StrategyApplicabilityKnown = strategy.Known;
        frame.StrategyApplicableLabels = strategy.ApplicableLabels.ToList();
        frame.StrategyLabelsKnown = strategy.Known
                                    && strategy.ApplicableLabels.Count > 0;
        frame.StrategyLabels = strategy.PositiveLabels.ToList();
        frame.StrategyLabelSource = strategy.Source;
        frame.StrategyPhase = CombatPolicyValueBatchTrainer
            .StrategicPhaseForFrame(frame);
    }

    private void LinkPreviousFrame(CombatEpisodeFrame next)
    {
        if (frames.Count == 0)
        {
            return;
        }
        CombatEpisodeTransitionProtocol.Link(frames[frames.Count - 1], next);
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
