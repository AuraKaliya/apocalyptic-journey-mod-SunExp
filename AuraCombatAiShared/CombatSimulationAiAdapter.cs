using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public interface ICombatDecisionTracePolicy
{
    CombatDecision? LastDecision { get; }

    CombatStateObservation? LastObservation { get; }
}

public sealed class CombatSelfPlayExplorationOptions
{
    public double Probability { get; set; }

    public double Temperature { get; set; } = 1d;

    public int RandomSeed { get; set; }

    public double RootDirichletAlpha { get; set; } = 0.30d;

    public double RootNoiseFraction { get; set; } = 0.25d;

    public double ActionUniformMixture { get; set; } = 0.25d;
}

public sealed class CombatSearchExplorationOptions
{
    public double RootDirichletAlpha { get; set; } = 0.30d;

    public double RootNoiseFraction { get; set; } = 0.25d;

    public int RandomSeed { get; set; }

    public int DeterminizationOffset { get; set; }
}

public sealed class CombatDecisionSimulationPolicy :
    ICombatSimulationPolicy,
    ICombatSimulationBorrowedStatePolicy,
    ICombatSimulationPolicyMetricsProvider,
    ICombatDecisionTracePolicy
{
    private readonly CombatDecisionEngine decisionEngine;
    private readonly CombatDecisionProfile profile;
    private readonly CombatSelfPlayExplorationOptions? exploration;
    private readonly Random? explorationRandom;

    public CombatDecisionSimulationPolicy(
        CombatDecisionProfile? profile = null,
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        ICombatPolicyValueModel? policyValueModel = null,
        CombatSelfPlayExplorationOptions? exploration = null,
        CombatDecisionPreparationSnapshot? decisionPreparation = null)
    {
        this.profile = profile ?? new CombatDecisionProfile();
        this.exploration = Normalize(exploration);
        explorationRandom = this.exploration == null
            ? null
            : new Random(this.exploration.RandomSeed);
        decisionEngine = new CombatDecisionEngine(
            residualModel,
            guidanceModel,
            useRuntimeRegistries: false,
            policyValueModel,
            decisionPreparation: decisionPreparation
                ?? CombatAiRegistry.SnapshotDecisionPreparation());
    }

    internal CombatDecisionSimulationPolicy(
        CombatDecisionEngine reusableDecisionEngine,
        CombatDecisionProfile profile,
        CombatSelfPlayExplorationOptions? exploration = null)
    {
        decisionEngine = reusableDecisionEngine
            ?? throw new ArgumentNullException(nameof(reusableDecisionEngine));
        this.profile = profile ?? new CombatDecisionProfile();
        this.exploration = Normalize(exploration);
        explorationRandom = this.exploration == null
            ? null
            : new Random(this.exploration.RandomSeed);
    }

    public string PolicyId => "aura-combat-decision:" + profile.Id;

    public CombatDecision? LastDecision { get; private set; }

    public CombatStateObservation? LastObservation { get; private set; }

    public CombatSimulationPolicyDecisionMetrics LastDecisionMetrics { get; } =
        new();

    public CombatSimulationAction? SelectAction(CombatSimulationPolicyContext context)
    {
        return SelectAction(context, null);
    }

    internal CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context,
        CombatStateObservation? authoritativeObservation)
    {
        var allocationStart = ReadThreadAllocatedBytes();
        var observation = authoritativeObservation
                          ?? PlayerEquivalentSimulationObservationProjector.Project(
                              context);
        var projectionAllocated = Math.Max(
            0L,
            ReadThreadAllocatedBytes() - allocationStart);
        var decisionStart = ReadThreadAllocatedBytes();
        var searchExploration = BeginExploration();
        var decision = decisionEngine.Choose(
            observation,
            profile,
            searchExploration,
            out var decisionPreparedObservation,
            stateIsNormalizedAndOwned: true);
        var decisionAllocated = Math.Max(
            0L,
            ReadThreadAllocatedBytes() - decisionStart);
        LastObservation = decisionPreparedObservation ?? observation;
        LastDecision = decision;
        LastDecisionMetrics.SearchSimulations = decision.SearchSimulations;
        LastDecisionMetrics.SearchNodes = decision.SearchNodes;
        LastDecisionMetrics.SearchStoppedEarly = decision.SearchStoppedEarly;
        LastDecisionMetrics.SearchBudgetTier = decision.SearchBudgetTier;
        LastDecisionMetrics.SearchMilliseconds = decision.Performance.TotalMilliseconds;
        LastDecisionMetrics.ObservationProjectionAllocatedBytes =
            projectionAllocated;
        LastDecisionMetrics.DecisionEngineAllocatedBytes = decisionAllocated;
        LastDecisionMetrics.ModelEvaluations = decision.Performance.ModelEvaluations;
        LastDecisionMetrics.ModelCacheHits = decision.Performance.ModelCacheHits;
        LastDecisionMetrics.OriginalCandidates = decision.Performance.OriginalCandidates;
        LastDecisionMetrics.RetainedCandidates = decision.Performance.RetainedCandidates;
        LastDecisionMetrics.SearchStoppedByTime = decision.Performance.StoppedByTime;
        LastDecisionMetrics.SearchStoppedByModelBudget =
            decision.Performance.StoppedByModelBudget;
        LastDecisionMetrics.SearchStopReason = decision.Performance.StopReason;
        LastDecisionMetrics.CertifiedLoops = decision.CertifiedLoops;
        LastDecisionMetrics.SustainableControlLoops =
            decision.SustainableControlLoops;
        LastDecisionMetrics.FakeLoops = decision.FakeLoops;
        LastDecisionMetrics.BlockedLoops = decision.BlockedLoops;
        LastDecisionMetrics.ExplorationDecisions =
            searchExploration == null ? 0 : 1;
        LastDecisionMetrics.ExplorationActionOverrides = 0;
        LastDecisionMetrics.RootMaximumVisitShare =
            searchExploration == null
                ? 0d
                : MaximumRootVisitShare(decision);
        LastDecisionMetrics.AuthoritativeActionsAudited = 0;
        LastDecisionMetrics.AuthoritativeSemanticMismatches = 0;
        LastDecisionMetrics.AuthoritativeSelectedActionsAudited = 0;
        LastDecisionMetrics.AuthoritativeSelectedSemanticMismatches = 0;
        LastDecisionMetrics.AuthoritativeTeacherOverrides = 0;
        LastDecisionMetrics.EndTurnSafetyAssessed = false;
        LastDecisionMetrics.SelectedEndTurnSevereMistake = false;
        LastDecisionMetrics.EndTurnSafeAlternativeCount = 0;
        LastDecisionMetrics.EndTurnAvoidableUnusedEnergy = 0;
        LastDecisionMetrics.EndTurnVerdict = "";
        LastDecisionMetrics.EndTurnDominanceMargin = 0d;
        LastDecisionMetrics.EndTurnCertifiedCycleCount = 0;
        LastDecisionMetrics.EndTurnReachableCycleCount = 0;
        LastDecisionMetrics.EndTurnAvoidableLethal = false;
        LastDecisionMetrics.EndTurnExpiringEnergy = 0;
        LastDecisionMetrics.EndTurnBankedSurplusEnergy = 0;
        LastDecisionMetrics.EndTurnUnknownLifecycleEffectCount = 0;
        LastDecisionMetrics.AuthoritativeSemanticMismatchKinds.Clear();
        LastDecisionMetrics.AuthoritativeSemanticMismatchSources.Clear();
        LastDecisionMetrics.AuthoritativeSemanticMismatchScenarios.Clear();
        LastDecisionMetrics.SemanticAudit = new CombatSemanticAuditMetrics();
        if (!decision.HasAction || decision.Action == null)
        {
            return context.LegalActions.FirstOrDefault(action =>
                action.Kind == CombatSimulationActionKind.EndTurn);
        }
        var selected = SelectExplorationAction(
            context,
            decision,
            searchExploration != null);
        if (selected != null
            && !string.Equals(
                selected.CandidateId,
                decision.Action.CandidateId,
                StringComparison.Ordinal))
        {
            LastDecisionMetrics.ExplorationActionOverrides = 1;
        }
        var resolved = selected
                       ?? context.LegalActions.FirstOrDefault(action =>
                           string.Equals(
                               action.CandidateId,
                               decision.Action.CandidateId,
                               StringComparison.Ordinal))
                       ?? context.LegalActions.FirstOrDefault(action =>
                           action.Kind
                           == CombatSimulationActionKind.EndTurn);
        if (resolved?.Kind == CombatSimulationActionKind.EndTurn)
        {
            var endTurnAssessment = CombatEndTurnSafety.Assess(
                observation,
                decision.Candidates
                ?? new List<CombatCandidateEvaluation>(),
                profile);
            LastDecisionMetrics.EndTurnSafetyAssessed = true;
            LastDecisionMetrics.SelectedEndTurnSevereMistake =
                endTurnAssessment.SevereMistake;
            LastDecisionMetrics.EndTurnSafeAlternativeCount =
                endTurnAssessment.SafeAlternativeCount;
            LastDecisionMetrics.EndTurnAvoidableUnusedEnergy =
                endTurnAssessment.AvoidableUnusedEnergy;
            LastDecisionMetrics.EndTurnVerdict =
                endTurnAssessment.Verdict.ToString();
            LastDecisionMetrics.EndTurnDominanceMargin =
                endTurnAssessment.DominanceMargin;
            LastDecisionMetrics.EndTurnCertifiedCycleCount =
                endTurnAssessment.CertifiedCycleCount;
            LastDecisionMetrics.EndTurnReachableCycleCount =
                endTurnAssessment.ReachableCycleCount;
            LastDecisionMetrics.EndTurnAvoidableLethal =
                endTurnAssessment.AvoidableLethal;
            LastDecisionMetrics.EndTurnExpiringEnergy =
                endTurnAssessment.Projection.ExpiringPower;
            LastDecisionMetrics.EndTurnBankedSurplusEnergy =
                endTurnAssessment.Projection.BankedSurplusPower;
            LastDecisionMetrics.EndTurnUnknownLifecycleEffectCount =
                endTurnAssessment.Projection.UnknownLifecycleEffectCount;
        }
        return resolved;
    }

    private static long ReadThreadAllocatedBytes()
    {
#if NET8_0_OR_GREATER
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return 0L;
#endif
    }

    private CombatSimulationAction? SelectExplorationAction(
        CombatSimulationPolicyContext context,
        CombatDecision decision,
        bool explorationActive)
    {
        if (!explorationActive
            || exploration == null
            || explorationRandom == null
            || decision.SearchSimulations <= 0)
        {
            return null;
        }
        var legal = (decision.Candidates ?? new List<CombatCandidateEvaluation>())
            .Where(candidate => candidate?.Action != null
                                && candidate.Legal
                                && context.LegalActions.Any(action =>
                                    string.Equals(
                                        action.CandidateId,
                                        candidate.Action.CandidateId,
                                        StringComparison.Ordinal)))
            .ToList();
        if (legal.Count <= 1)
        {
            return null;
        }
        var inverseTemperature = 1d / exploration.Temperature;
        var weights = legal
            .Select(candidate => Math.Pow(
                Math.Max(1d, candidate.SearchVisits),
                inverseTemperature))
            .ToArray();
        var visitTotal = weights.Sum();
        var uniform = 1d / legal.Count;
        for (var index = 0; index < weights.Length; index++)
        {
            var visitShare = visitTotal <= 0d
                ? uniform
                : weights[index] / visitTotal;
            weights[index] =
                (1d - exploration.ActionUniformMixture) * visitShare
                + exploration.ActionUniformMixture * uniform;
        }
        var sample = explorationRandom.NextDouble();
        for (var index = 0; index < legal.Count; index++)
        {
            sample -= weights[index];
            if (sample <= 0d)
            {
                var candidateId = legal[index].Action.CandidateId;
                return context.LegalActions.First(action =>
                    string.Equals(
                        action.CandidateId,
                        candidateId,
                        StringComparison.Ordinal));
            }
        }
        return null;
    }

    private CombatSearchExplorationOptions? BeginExploration()
    {
        if (exploration == null
            || explorationRandom == null
            || explorationRandom.NextDouble() >= exploration.Probability)
        {
            return null;
        }
        return new CombatSearchExplorationOptions
        {
            RootDirichletAlpha = exploration.RootDirichletAlpha,
            RootNoiseFraction = exploration.RootNoiseFraction,
            RandomSeed = explorationRandom.Next()
        };
    }

    private static double MaximumRootVisitShare(CombatDecision decision)
    {
        var visits = (decision.Candidates
                      ?? new List<CombatCandidateEvaluation>())
            .Where(candidate => candidate?.Legal == true)
            .Select(candidate => Math.Max(0, candidate.SearchVisits))
            .ToArray();
        var total = visits.Sum();
        return total <= 0 ? 0d : visits.Max() / (double)total;
    }

    private static CombatSelfPlayExplorationOptions? Normalize(
        CombatSelfPlayExplorationOptions? options)
    {
        if (options == null
            || double.IsNaN(options.Probability)
            || options.Probability <= 0d)
        {
            return null;
        }
        return new CombatSelfPlayExplorationOptions
        {
            Probability = Math.Min(1d, options.Probability),
            Temperature =
                double.IsNaN(options.Temperature)
                || double.IsInfinity(options.Temperature)
                    ? 1d
                    : Math.Max(0.1d, Math.Min(5d, options.Temperature)),
            RandomSeed = options.RandomSeed,
            RootDirichletAlpha =
                double.IsNaN(options.RootDirichletAlpha)
                || double.IsInfinity(options.RootDirichletAlpha)
                    ? 0.30d
                    : Math.Max(
                        0.03d,
                        Math.Min(2d, options.RootDirichletAlpha)),
            RootNoiseFraction =
                double.IsNaN(options.RootNoiseFraction)
                || double.IsInfinity(options.RootNoiseFraction)
                    ? 0.25d
                    : Math.Max(
                        0d,
                        Math.Min(0.75d, options.RootNoiseFraction)),
            ActionUniformMixture =
                double.IsNaN(options.ActionUniformMixture)
                || double.IsInfinity(options.ActionUniformMixture)
                    ? 0.25d
                    : Math.Max(
                        0d,
                        Math.Min(0.75d, options.ActionUniformMixture))
        };
    }
}

public sealed class CombatAuthoritativeTeacherOptions
{
    public double AuditProbability { get; set; } = 0.15d;

    public int MaximumCandidates { get; set; } = 6;

    public double MinimumOverrideGain { get; set; } = 0.5d;

    public int RandomSeed { get; set; }
}

public sealed class CombatAuthoritativeBranchTeacherPolicy :
    ICombatSimulationPolicy,
    ICombatSimulationBorrowedStatePolicy,
    ICombatSimulationPolicyMetricsProvider,
    ICombatDecisionTracePolicy,
    ICombatSimulationActionExecutionObserver
{
    private readonly CombatDecisionSimulationPolicy inner;
    private readonly CombatSimulationEngine engine;
    private readonly CombatAuthoritativeTeacherOptions options;
    private readonly Random random;

    public CombatAuthoritativeBranchTeacherPolicy(
        CombatDecisionSimulationPolicy inner,
        CombatAuthoritativeTeacherOptions? options = null,
        CombatSimulationEngine? engine = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.options = Normalize(options);
        this.engine = engine ?? new CombatSimulationEngine();
        random = new Random(this.options.RandomSeed);
    }

    public string PolicyId => inner.PolicyId + ":authoritative-teacher";

    public CombatDecision? LastDecision => inner.LastDecision;

    public CombatStateObservation? LastObservation => inner.LastObservation;

    public CombatSimulationPolicyDecisionMetrics LastDecisionMetrics =>
        inner.LastDecisionMetrics;

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (!ShouldAudit(context))
        {
            return SelectWithSelectedTransition(context);
        }
        var observation = PlayerEquivalentSimulationObservationProjector.Project(
            context);
        var candidates = SelectCandidates(context, observation);
        if (candidates.Count == 0)
        {
            return inner.SelectAction(context, observation);
        }

        CombatSimulationAction? bestAction = null;
        var bestScore = double.NegativeInfinity;
        var audits = new Dictionary<string, CombatSemanticAuditResult>(
            StringComparer.Ordinal);
        var transitions = new Dictionary<string, AuthoritativeTransition>(
            StringComparer.Ordinal);
        var unavailableBranches = new List<KeyValuePair<string, string>>();
        var attemptedCandidateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            attemptedCandidateIds.Add(candidate.CandidateId);
            var transition = BuildTransition(
                context,
                observation,
                candidate,
                (action, reason) => unavailableBranches.Add(
                    new KeyValuePair<string, string>(
                        action.DefinitionId,
                        reason)));
            if (transition == null)
            {
                continue;
            }
            transitions[candidate.CandidateId] = transition;
            audits[candidate.CandidateId] = transition.Audit;
            if (transition.Score > bestScore)
            {
                bestScore = transition.Score;
                bestAction = candidate;
            }
        }
        var baseline = inner.SelectAction(context, observation);
        if (baseline != null
            && !transitions.ContainsKey(baseline.CandidateId)
            && !attemptedCandidateIds.Contains(baseline.CandidateId)
            && baseline.Kind is CombatSimulationActionKind.PlayCard
                or CombatSimulationActionKind.UseSkill)
        {
            attemptedCandidateIds.Add(baseline.CandidateId);
            var baselineTransition = BuildTransition(
                context,
                observation,
                baseline,
                (action, reason) => unavailableBranches.Add(
                    new KeyValuePair<string, string>(
                        action.DefinitionId,
                        reason)));
            if (baselineTransition != null)
            {
                transitions[baseline.CandidateId] = baselineTransition;
                audits[baseline.CandidateId] = baselineTransition.Audit;
                if (baselineTransition.Score > bestScore)
                {
                    bestScore = baselineTransition.Score;
                    bestAction = baseline;
                }
            }
        }
        RecordCounterfactualBranchUnavailable(unavailableBranches);
        var decisionLegalIds = new HashSet<string>(
            (LastDecision?.Candidates
             ?? new List<CombatCandidateEvaluation>())
            .Where(item => item?.Action != null && item.Legal)
            .Select(item => item.Action.CandidateId),
            StringComparer.Ordinal);
        bestAction = null;
        bestScore = double.NegativeInfinity;
        foreach (var item in transitions.Where(item =>
                     decisionLegalIds.Contains(item.Key)))
        {
            var candidate = context.LegalActions.FirstOrDefault(action =>
                string.Equals(
                    action.CandidateId,
                    item.Key,
                    StringComparison.Ordinal));
            if (candidate != null)
            {
                RecordAuthoritativeAudit(
                    context,
                    candidate,
                    item.Value.Audit);
                if (item.Value.Score > bestScore)
                {
                    bestScore = item.Value.Score;
                    bestAction = candidate;
                }
            }
        }
        var baselineScore = baseline != null
                            && transitions.TryGetValue(
                                baseline.CandidateId,
                                out var knownBaseline)
            ? knownBaseline.Score
            : baseline?.Kind
                is CombatSimulationActionKind.PlayCard
                or CombatSimulationActionKind.UseSkill
                ? double.NegativeInfinity
                : 0d;
        var selected = baseline;
        if (bestAction != null
            && bestScore
               >= baselineScore + options.MinimumOverrideGain
            && (baseline == null
                || !string.Equals(
                    bestAction.CandidateId,
                    baseline.CandidateId,
                    StringComparison.Ordinal)))
        {
            LastDecisionMetrics.AuthoritativeTeacherOverrides = 1;
            MakeTeacherTarget(bestAction.CandidateId);
            selected = bestAction;
        }
        return selected;
    }

    private CombatSimulationAction? SelectWithSelectedTransition(
        CombatSimulationPolicyContext context)
    {
        return inner.SelectAction(context);
    }

    public void OnActionExecuted(CombatSimulationActionExecution execution)
    {
        if (execution == null) throw new ArgumentNullException(nameof(execution));
        var selected = execution.Action;
        if (selected.Kind is not (
                CombatSimulationActionKind.PlayCard
                or CombatSimulationActionKind.UseSkill)
            || LastObservation == null)
        {
            return;
        }

        var transition = BuildTransition(
            execution,
            LastObservation,
            selected);
        var context = new CombatSimulationPolicyContext
        {
            Scenario = execution.Scenario,
            Ruleset = execution.Ruleset,
            State = execution.BeforeState
        };
        RecordAuthoritativeAudit(context, selected, transition.Audit);
        RecordSelectedAudit(selected, transition.Audit);
    }

    private void RecordSelectedAudit(
        CombatSimulationAction selected,
        CombatSemanticAuditResult audit)
    {
        LastDecisionMetrics.AuthoritativeSelectedActionsAudited =
            audit.Valid ? 1 : 0;
        LastDecisionMetrics.AuthoritativeSelectedSemanticMismatches =
            audit.Mismatch ? 1 : 0;
        var selectedSource = string.IsNullOrWhiteSpace(selected.DefinitionId)
            ? "unknown"
            : selected.DefinitionId;
        Increment(
            LastDecisionMetrics.SemanticAudit.SelectedAuditedSources,
            selectedSource);
        if (audit.Invalid)
        {
            LastDecisionMetrics.SemanticAudit.SelectedInvalidActions = 1;
            Increment(
                LastDecisionMetrics.SemanticAudit.SelectedInvalidSources,
                selectedSource);
        }
        else
        {
            LastDecisionMetrics.SemanticAudit.SelectedValidActions = 1;
        }
        if (audit.ExplainedDifference)
        {
            LastDecisionMetrics.SemanticAudit.SelectedExplainedActions = 1;
            LastDecisionMetrics.SemanticAudit
                .SelectedContextAdjustedActions = 1;
        }
        if (audit.Mismatch)
        {
            LastDecisionMetrics.SemanticAudit
                .SelectedUnexplainedMismatchActions = 1;
            Increment(
                LastDecisionMetrics.SemanticAudit
                    .SelectedUnexplainedMismatchSources,
                selectedSource);
            foreach (var kind in audit.MismatchKinds.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                Increment(
                    LastDecisionMetrics.SemanticAudit
                        .SelectedUnexplainedMismatchKinds,
                    kind);
                var key = SourceKindKey(selectedSource, kind);
                Increment(
                    LastDecisionMetrics.SemanticAudit
                        .SelectedSourceKindUnexplainedMismatches,
                    key);
                if (LastDecisionMetrics.SemanticAudit
                        .SelectedUnexplainedExamples.Count
                    < CombatSemanticAuditMetrics.MaximumExamples
                    && !LastDecisionMetrics.SemanticAudit
                        .SelectedUnexplainedExamples.ContainsKey(key))
                {
                    LastDecisionMetrics.SemanticAudit
                        .SelectedUnexplainedExamples[key] =
                        audit.Describe(selectedSource);
                }
            }
        }
        RecordSelectedSourceProjectionAudit(
            LastDecisionMetrics.SemanticAudit,
            audit,
            selectedSource);
    }

    private AuthoritativeTransition? BuildTransition(
        CombatSimulationPolicyContext context,
        CombatStateObservation observation,
        CombatSimulationAction candidate,
        Action<CombatSimulationAction, string>? onBranchUnavailable = null)
    {
        var applied = engine.ForkAndApplyPlayerAction(
            context.Scenario,
            context.Ruleset,
            context.State,
            candidate,
            captureSemanticEvents: true,
            requireExactRuntimeContinuation: true);
        if (applied.FailureKind
            == CombatActionApplicationFailureKind.RuntimeContinuationUnavailable)
        {
            onBranchUnavailable?.Invoke(candidate, applied.Reason);
            return null;
        }
        if (!applied.Success)
        {
            return new AuthoritativeTransition(
                new CombatSemanticAuditResult
                {
                    InvalidKinds = { "action-transition" },
                    Comparisons =
                    {
                        new CombatSemanticAuditComparison
                        {
                            Kind = "action-transition",
                            Classification = "invalid",
                            Explanation = string.IsNullOrWhiteSpace(applied.Reason)
                                ? "authoritative action branch failed"
                                : applied.Reason
                        }
                    }
                },
                double.NegativeInfinity);
        }
        var projected = observation.Actions.FirstOrDefault(item =>
            string.Equals(
                item.CandidateId,
                candidate.CandidateId,
                StringComparison.Ordinal));
        var declared = projected?.Semantics;
        var realized = CombatSemanticAuditor.ProjectRealized(
            context.State,
            applied.State,
            applied.Events,
            candidate,
            context.Ruleset,
            declared);
        ProjectCampaignVariableChanges(
            context.Scenario.CampaignVariables,
            applied.CampaignVariables,
            realized);
        if (projected != null)
        {
            projected.Semantics = realized;
            projected.Features["authoritativeTransitionSemantics"] = 1d;
            var effective = CombatSemanticAuditor.ProjectEffective(
                context.State,
                candidate,
                realized,
                context.Ruleset);
            projected.Features["effectiveHpDamage"] = effective.Damage;
            projected.Features["effectiveDurabilityDamage"] =
                effective.DurabilityDamage;
            projected.Features["effectiveDefend"] = effective.Defend;
            projected.Features["effectiveHeal"] = effective.Heal;
            projected.Features["deferredHpDamage"] =
                CombatActionSemanticMetrics.DeferredHpDamage(realized);
            projected.Features["affectedEnemyCount"] =
                realized.AffectedEnemyCount;
        }
        var audit = CombatSemanticAuditor.Audit(
            context.State,
            applied.State,
            applied.Events,
            realized,
            candidate,
            context.Ruleset);
        return new AuthoritativeTransition(
            audit,
            ScoreTransition(
                context.State,
                applied.State,
                candidate,
                realized));
    }

    private void RecordCounterfactualBranchUnavailable(
        IEnumerable<KeyValuePair<string, string>> unavailable)
    {
        foreach (var item in unavailable ??
                 Array.Empty<KeyValuePair<string, string>>())
        {
            var source = string.IsNullOrWhiteSpace(item.Key)
                ? "unknown"
                : item.Key;
            LastDecisionMetrics.SemanticAudit
                .CounterfactualBranchUnavailableActions++;
            Increment(
                LastDecisionMetrics.SemanticAudit
                    .CounterfactualBranchUnavailableSources,
                source);
            if (LastDecisionMetrics.SemanticAudit
                    .CounterfactualBranchUnavailableExamples.Count
                < CombatSemanticAuditMetrics.MaximumExamples
                && !LastDecisionMetrics.SemanticAudit
                    .CounterfactualBranchUnavailableExamples.ContainsKey(
                        source))
            {
                LastDecisionMetrics.SemanticAudit
                    .CounterfactualBranchUnavailableExamples[source] =
                    string.IsNullOrWhiteSpace(item.Value)
                        ? "runtime continuation unavailable"
                        : item.Value;
            }
        }
    }

    private AuthoritativeTransition BuildTransition(
        CombatSimulationActionExecution execution,
        CombatStateObservation observation,
        CombatSimulationAction candidate)
    {
        if (!execution.ApplicationSucceeded)
        {
            var explanation = string.IsNullOrWhiteSpace(execution.FailureReason)
                ? execution.FailureKind.ToString()
                : execution.FailureKind + ":" + execution.FailureReason;
            return new AuthoritativeTransition(
                InvalidTransitionAudit(explanation),
                double.NegativeInfinity);
        }

        var projected = observation.Actions.FirstOrDefault(item =>
            string.Equals(
                item.CandidateId,
                candidate.CandidateId,
                StringComparison.Ordinal));
        var declared = projected?.Semantics;
        var realized = CombatSemanticAuditor.ProjectRealized(
            execution.BeforeState,
            execution.AfterState,
            execution.Events,
            candidate,
            execution.Ruleset,
            declared);
        ProjectCampaignVariableChanges(
            execution.BeforeCampaignVariables,
            execution.AfterCampaignVariables,
            realized);
        ApplyRealizedProjection(
            projected,
            realized,
            execution.BeforeState,
            candidate,
            execution.Ruleset);
        var decisionAction = LastDecision?.Candidates
            .FirstOrDefault(item => string.Equals(
                item?.Action?.CandidateId,
                candidate.CandidateId,
                StringComparison.Ordinal))?.Action;
        if (!ReferenceEquals(decisionAction, projected))
        {
            ApplyRealizedProjection(
                decisionAction,
                realized,
                execution.BeforeState,
                candidate,
                execution.Ruleset);
        }
        var audit = CombatSemanticAuditor.Audit(
            execution.BeforeState,
            execution.AfterState,
            execution.Events,
            realized,
            candidate,
            execution.Ruleset);
        return new AuthoritativeTransition(
            audit,
            ScoreTransition(
                execution.BeforeState,
                execution.AfterState,
                candidate,
                realized));
    }

    private static CombatSemanticAuditResult InvalidTransitionAudit(
        string explanation)
    {
        return new CombatSemanticAuditResult
        {
            InvalidKinds = { "action-transition" },
            Comparisons =
            {
                new CombatSemanticAuditComparison
                {
                    Kind = "action-transition",
                    Classification = "invalid",
                    Explanation = string.IsNullOrWhiteSpace(explanation)
                        ? "authoritative action execution failed"
                        : explanation
                }
            }
        };
    }

    private static void ApplyRealizedProjection(
        CombatActionObservation? projected,
        CombatActionSemantics realized,
        CombatBattleState before,
        CombatSimulationAction candidate,
        CombatRuleset ruleset)
    {
        if (projected == null)
        {
            return;
        }
        projected.Semantics = realized;
        projected.Features["authoritativeTransitionSemantics"] = 1d;
        var effective = CombatSemanticAuditor.ProjectEffective(
            before,
            candidate,
            realized,
            ruleset);
        projected.Features["effectiveHpDamage"] = effective.Damage;
        projected.Features["effectiveDurabilityDamage"] =
            effective.DurabilityDamage;
        projected.Features["effectiveDefend"] = effective.Defend;
        projected.Features["effectiveHeal"] = effective.Heal;
        projected.Features["deferredHpDamage"] =
            CombatActionSemanticMetrics.DeferredHpDamage(realized);
        projected.Features["affectedEnemyCount"] =
            realized.AffectedEnemyCount;
    }

    private static void ProjectCampaignVariableChanges(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        CombatActionSemantics semantics)
    {
        var changed = 0;
        foreach (var key in (before?.Keys ?? Array.Empty<string>())
                     .Concat(after?.Keys ?? Array.Empty<string>())
                     .Where(key => !string.IsNullOrWhiteSpace(key))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var beforeValue = before != null
                              && before.TryGetValue(key, out var knownBefore)
                ? knownBefore ?? ""
                : "";
            var afterValue = after != null
                             && after.TryGetValue(key, out var knownAfter)
                ? knownAfter ?? ""
                : "";
            if (string.Equals(
                    beforeValue,
                    afterValue,
                    StringComparison.Ordinal))
            {
                continue;
            }

            changed++;
            var delta = 1d;
            if (double.TryParse(
                    beforeValue,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var numericBefore)
                && double.TryParse(
                    afterValue,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var numericAfter)
                && !double.IsNaN(numericBefore)
                && !double.IsInfinity(numericBefore)
                && !double.IsNaN(numericAfter)
                && !double.IsInfinity(numericAfter))
            {
                delta = numericAfter - numericBefore;
            }
            semantics.StateChanges["campaign:" + key] = delta;
        }
        if (changed > 0)
        {
            semantics.StateChanges["campaign.changed"] = changed;
        }
    }

    private void RecordAuthoritativeAudit(
        CombatSimulationPolicyContext context,
        CombatSimulationAction candidate,
        CombatSemanticAuditResult audit)
    {
        RecordAudit(
            LastDecisionMetrics,
            audit,
            candidate.DefinitionId,
            context.Scenario.ScenarioId);
        if (audit.Valid)
        {
            LastDecisionMetrics.AuthoritativeActionsAudited++;
        }
        if (!audit.Mismatch)
        {
            return;
        }
        LastDecisionMetrics.AuthoritativeSemanticMismatches++;
        foreach (var kind in audit.MismatchKinds.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            Increment(
                LastDecisionMetrics.AuthoritativeSemanticMismatchKinds,
                kind);
        }
        Increment(
            LastDecisionMetrics.AuthoritativeSemanticMismatchSources,
            string.IsNullOrWhiteSpace(candidate.DefinitionId)
                ? "unknown"
                : candidate.DefinitionId);
        Increment(
            LastDecisionMetrics.AuthoritativeSemanticMismatchScenarios,
            string.IsNullOrWhiteSpace(context.Scenario.ScenarioId)
                ? "unknown"
                : context.Scenario.ScenarioId);
    }

    private sealed class AuthoritativeTransition
    {
        public AuthoritativeTransition(
            CombatSemanticAuditResult audit,
            double score)
        {
            Audit = audit;
            Score = score;
        }

        public CombatSemanticAuditResult Audit { get; }

        public double Score { get; }
    }

    private static long ReadThreadAllocatedBytes()
    {
#if NET8_0_OR_GREATER
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return 0L;
#endif
    }

    private static void RecordAudit(
        CombatSimulationPolicyDecisionMetrics metrics,
        CombatSemanticAuditResult audit,
        string sourceId,
        string scenarioId)
    {
        var source = string.IsNullOrWhiteSpace(sourceId)
            ? "unknown"
            : sourceId;
        Increment(metrics.SemanticAudit.AuditedSources, source);
        if (audit.Invalid)
        {
            metrics.SemanticAudit.InvalidActions++;
            Increment(metrics.SemanticAudit.InvalidSources, source);
            foreach (var kind in audit.InvalidKinds.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                Increment(metrics.SemanticAudit.InvalidKinds, kind);
                var key = SourceKindKey(source, kind);
                if (metrics.SemanticAudit.InvalidExamples.Count
                    < CombatSemanticAuditMetrics.MaximumExamples
                    && !metrics.SemanticAudit.InvalidExamples.ContainsKey(key))
                {
                    metrics.SemanticAudit.InvalidExamples[key] =
                        audit.Describe(source);
                }
            }
            return;
        }
        metrics.SemanticAudit.ValidActions++;
        foreach (var kind in audit.AuditedKinds.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            Increment(metrics.SemanticAudit.AuditedKinds, kind);
            Increment(
                metrics.SemanticAudit.SourceKindAudits,
                SourceKindKey(source, kind));
        }
        if (audit.ExplainedDifference)
        {
            metrics.SemanticAudit.ExplainedActions++;
            foreach (var kind in audit.ExplainedKinds.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                Increment(metrics.SemanticAudit.ExplainedKinds, kind);
            }
        }
        if (audit.Mismatch)
        {
            metrics.SemanticAudit.UnexplainedMismatchActions++;
        }
        foreach (var kind in audit.MismatchKinds.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            var key = SourceKindKey(source, kind);
            Increment(metrics.SemanticAudit.SourceKindMismatches, key);
            if (metrics.SemanticAudit.Examples.Count
                < CombatSemanticAuditMetrics.MaximumExamples
                && !metrics.SemanticAudit.Examples.ContainsKey(key))
            {
                metrics.SemanticAudit.Examples[key] = audit.Describe(source);
            }
        }
    }

    private static void RecordSelectedSourceProjectionAudit(
        CombatSemanticAuditMetrics metrics,
        CombatSemanticAuditResult audit,
        string source)
    {
        if (audit.Invalid)
        {
            metrics.SelectedSourceProjectionInvalidActions++;
            Increment(metrics.SelectedSourceProjectionInvalidSources, source);
        }
        else
        {
            metrics.SelectedSourceProjectionValidActions++;
        }
        if (!audit.Mismatch)
        {
            return;
        }

        metrics.SelectedSourceProjectionUnexplainedMismatchActions++;
        Increment(metrics.SelectedSourceProjectionMismatchSources, source);
        foreach (var kind in audit.MismatchKinds.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            Increment(metrics.SelectedSourceProjectionMismatchKinds, kind);
            var key = SourceKindKey(source, kind);
            if (metrics.SelectedSourceProjectionExamples.Count
                    < CombatSemanticAuditMetrics.MaximumExamples
                && !metrics.SelectedSourceProjectionExamples.ContainsKey(key))
            {
                metrics.SelectedSourceProjectionExamples[key] =
                    audit.Describe(source);
            }
        }
    }

    private static string SourceKindKey(string source, string kind)
    {
        return source + "|" + kind;
    }

    private static void Increment(
        IDictionary<string, int> counts,
        string key)
    {
        counts[key] = counts.TryGetValue(key, out var current)
            ? current + 1
            : 1;
    }

    private bool ShouldAudit(CombatSimulationPolicyContext context)
    {
        var player = context.State.Player;
        var critical = player != null
                       && player.Hp
                          <= Math.Max(1, (int)Math.Ceiling(
                              player.MaxHp * 0.40d));
        return critical || random.NextDouble() < options.AuditProbability;
    }

    private List<CombatSimulationAction> SelectCandidates(
        CombatSimulationPolicyContext context,
        CombatStateObservation observation)
    {
        var legalCandidateIds = new HashSet<string>(
            observation.Actions
                .Where(item => item != null && item.Legal)
                .Select(item => item.CandidateId),
            StringComparer.Ordinal);
        return context.LegalActions
            .Where(item => (item.Kind is CombatSimulationActionKind.PlayCard
                or CombatSimulationActionKind.UseSkill)
                && legalCandidateIds.Contains(item.CandidateId))
            .OrderByDescending(item => RequiresTransitionOracle(
                context.Ruleset,
                item))
            .ThenBy(item => item.CandidateId, StringComparer.Ordinal)
            .Take(options.MaximumCandidates)
            .ToList();
    }

    private static bool RequiresTransitionOracle(
        CombatRuleset ruleset,
        CombatSimulationAction action)
    {
        if (action.Kind == CombatSimulationActionKind.UseSkill)
        {
            return true;
        }
        if (!ruleset.TryGetCard(action.DefinitionId, out var card))
        {
            return true;
        }
        return card.Interaction != null
               || card.Effects.Count == 0
               || card.Metadata.TryGetValue(
                   "NativeExecution",
                   out var execution)
               && string.Equals(
                   execution,
                   "Script",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static double ScoreTransition(
        CombatBattleState before,
        CombatBattleState after,
        CombatSimulationAction action,
        CombatActionSemantics? projected)
    {
        var beforePlayer = before.Player;
        var afterPlayer = after.Player;
        var enemyDurabilityGain = before.LivingEnemies.Sum(item =>
                                      Math.Max(0, item.Hp)
                                      + Math.Max(0, item.Block))
                                  - after.LivingEnemies.Sum(item =>
                                      Math.Max(0, item.Hp)
                                      + Math.Max(0, item.Block));
        var defeated = before.LivingEnemies.Count()
                       - after.LivingEnemies.Count();
        var hpGain = (afterPlayer?.Hp ?? 0) - (beforePlayer?.Hp ?? 0);
        var blockGain =
            (afterPlayer?.Block ?? 0) - (beforePlayer?.Block ?? 0);
        var energyGain =
            (afterPlayer?.Energy ?? 0) - (beforePlayer?.Energy ?? 0)
            + action.Cost;
        var handGain = after.Hand.Count - before.Hand.Count + 1;
        var semantics = projected ?? new CombatActionSemantics();
        var continuationValue = ContinuationValue(after);
        return enemyDurabilityGain * 2d
               + defeated * 40d
               + hpGain * 4d
               + blockGain * 0.75d
               + energyGain
               + handGain * 1.5d
               + Math.Max(0d, semantics.Debuff) * 1.25d
               + Math.Max(0d, semantics.Buff) * 0.9d
               + Math.Max(0d, semantics.Draw) * 0.5d
               + continuationValue * 0.35d
               + (after.Outcome == CombatSimulationOutcome.Victory
                   ? 200d
                   : 0d)
               - (after.Outcome == CombatSimulationOutcome.Defeat
                   ? 200d
                   : 0d);
    }

    private static double ContinuationValue(CombatBattleState state)
    {
        var player = state.Player;
        if (player == null)
        {
            return -100d;
        }
        var hpRatio = player.MaxHp <= 0
            ? 0d
            : Math.Max(0d, Math.Min(1d, player.Hp / (double)player.MaxHp));
        var enemyPressure = state.LivingEnemies.Sum(item =>
            Math.Max(0, item.Hp) + Math.Max(0, item.Block));
        return hpRatio * 24d
               + Math.Max(0, player.Block) * 0.25d
               + Math.Max(0, player.Energy) * 0.3d
               + state.Hand.Count * 0.4d
               - enemyPressure * 0.08d
               - state.LivingEnemies.Count() * 2d;
    }

    private void MakeTeacherTarget(string candidateId)
    {
        if (LastDecision?.Candidates == null)
        {
            return;
        }
        var targetVisits = Math.Max(
            1,
            LastDecision.Candidates.Sum(item =>
                Math.Max(0, item.SearchVisits)));
        foreach (var candidate in LastDecision.Candidates)
        {
            candidate.SearchVisits = string.Equals(
                candidate.Action?.CandidateId,
                candidateId,
                StringComparison.Ordinal)
                ? targetVisits
                : 0;
        }
    }

    private static CombatAuthoritativeTeacherOptions Normalize(
        CombatAuthoritativeTeacherOptions? source)
    {
        source ??= new CombatAuthoritativeTeacherOptions();
        return new CombatAuthoritativeTeacherOptions
        {
            AuditProbability = double.IsNaN(source.AuditProbability)
                ? 0.15d
                : Math.Max(0d, Math.Min(1d, source.AuditProbability)),
            MaximumCandidates = Math.Max(
                1,
                Math.Min(16, source.MaximumCandidates)),
            MinimumOverrideGain =
                double.IsNaN(source.MinimumOverrideGain)
                || double.IsInfinity(source.MinimumOverrideGain)
                    ? 0.5d
                    : Math.Max(0d, source.MinimumOverrideGain),
            RandomSeed = source.RandomSeed
        };
    }
}

public sealed class CombatDecisionSimulationPolicyFactory : ICombatSimulationPolicyFactory
{
    private readonly CombatDecisionProfile profile;
    private readonly IDecisionResidualModel residualModel;
    private readonly ICombatSearchGuidanceModel guidanceModel;
    private readonly ICombatPolicyValueModel policyValueModel;
    private readonly CombatDecisionPreparationSnapshot decisionPreparation;
    private readonly ThreadLocal<CombatDecisionEngine> decisionEngines;

    public CombatDecisionSimulationPolicyFactory(
        CombatDecisionProfile? profile = null,
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        ICombatPolicyValueModel? policyValueModel = null)
    {
        this.profile = profile ?? new CombatDecisionProfile();
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
        this.guidanceModel = guidanceModel ?? NullCombatSearchGuidanceModel.Instance;
        this.policyValueModel = policyValueModel ?? NullCombatPolicyValueModel.Instance;
        decisionPreparation = CombatAiRegistry.SnapshotDecisionPreparation();
        decisionEngines = new ThreadLocal<CombatDecisionEngine>(() =>
            new CombatDecisionEngine(
                this.residualModel,
                this.guidanceModel,
                useRuntimeRegistries: false,
                this.policyValueModel,
                decisionPreparation: decisionPreparation));
    }

    public string PolicyId => "aura-combat-decision:" + profile.Id;

    public ICombatSimulationPolicy Create()
    {
        return new CombatDecisionSimulationPolicy(
            decisionEngines.Value!,
            profile);
    }
}

public sealed class CombatAuthoritativeTeacherPolicyFactory :
    ICombatSimulationPolicyFactory
{
    private readonly CombatDecisionProfile profile;
    private readonly ICombatPolicyValueModel policyValueModel;
    private readonly CombatAuthoritativeTeacherOptions options;
    private readonly CombatSimulationEngine? engine;
    private readonly CombatDecisionPreparationSnapshot decisionPreparation;
    private readonly ThreadLocal<CombatDecisionEngine> decisionEngines;

    public CombatAuthoritativeTeacherPolicyFactory(
        CombatDecisionProfile? profile = null,
        ICombatPolicyValueModel? policyValueModel = null,
        CombatAuthoritativeTeacherOptions? options = null,
        CombatSimulationEngine? engine = null)
    {
        this.profile = profile ?? new CombatDecisionProfile();
        this.policyValueModel =
            policyValueModel ?? NullCombatPolicyValueModel.Instance;
        this.options = options ?? new CombatAuthoritativeTeacherOptions();
        this.engine = engine;
        decisionPreparation = CombatAiRegistry.SnapshotDecisionPreparation();
        decisionEngines = new ThreadLocal<CombatDecisionEngine>(() =>
            new CombatDecisionEngine(
                useRuntimeRegistries: false,
                policyValueModel: this.policyValueModel,
                decisionPreparation: decisionPreparation));
    }

    public string PolicyId => "aura-combat-authoritative-teacher:"
                              + profile.Id;

    public ICombatSimulationPolicy Create()
    {
        return new CombatAuthoritativeBranchTeacherPolicy(
            new CombatDecisionSimulationPolicy(
                decisionEngines.Value!,
                profile),
            options,
            engine);
    }
}

public static class PlayerEquivalentSimulationObservationProjector
{
    public static CombatStateObservation Project(CombatSimulationPolicyContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var state = context.State;
        var player = state.Player ?? new CombatActorState();
        var battleSessionId = StableSessionId(
            context.Scenario.ScenarioId,
            context.Scenario.Seed);
        var handCardIds = CardIds(state, state.Hand);
        var deckCardIds = CardIds(
            state,
            state.DrawPile.Count
            + state.Hand.Count
            + state.DiscardPile.Count
            + state.ExhaustPile.Count,
            state.DrawPile,
            state.Hand,
            state.DiscardPile,
            state.ExhaustPile);
        var observation = new CombatStateObservation
        {
            BattleSessionId = battleSessionId,
            Sequence = state.ActionSequence,
            ObservationId = CombatPlayerObservationBoundary.BuildObservationId(
                battleSessionId,
                state.ActionSequence),
            Player = ProjectActor(player, CombatTargetKind.Self, context.Ruleset),
            CurrentPower = player.Energy,
            MaxPower = player.BaseEnergy,
            HandCount = state.Hand.Count,
            HandCardIds = handCardIds,
            HandCards = ProjectHandCards(context),
            RetainedHandCardIds = handCardIds
                .Where(cardId => context.Ruleset.TryGetCard(cardId, out var card)
                                 && HasTag(card, "Retain"))
                .ToList(),
            DeckCardIds = deckCardIds,
            DiscardPileCardIds = CardIds(state, state.DiscardPile),
            ExhaustPileCardIds = CardIds(state, state.ExhaustPile),
            DeferredEffects = state.DeferredEffects
                .Where(item => item.ActorId == state.PlayerActorId
                               && string.Equals(
                                   item.StatusId,
                                   "buff_timelock",
                                   StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Sequence)
                .Select(item => new CombatDeferredEffectObservation
                {
                    Sequence = item.Sequence,
                    StatusId = item.StatusId,
                    SourceId = item.SourceCardId,
                    TargetRuntimeId = item.TargetActorId
                })
                .ToList(),
            DeckKnowledge = new CombatDeckKnowledge
            {
                DrawPileCount = state.DrawPile.Count,
                DiscardPileCount = state.DiscardPile.Count,
                ExhaustPileCount = state.ExhaustPile.Count,
                DiscardContentsVisible = true,
                ExhaustContentsVisible = true
            },
            IsPlayerActionWindow = state.Phase == CombatSimulationPhase.PlayerAction,
            UiBusy = false,
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["turn"] = state.Turn,
                ["handLimit"] = context.Scenario.HandLimit,
                ["drawPile"] = state.DrawPile.Count,
                ["drawPileCount"] = state.DrawPile.Count,
                ["discardPile"] = state.DiscardPile.Count,
                ["discardPileCount"] = state.DiscardPile.Count,
                ["exhaustPile"] = state.ExhaustPile.Count,
                ["exhaustPileCount"] = state.ExhaustPile.Count,
                ["drawPerTurn"] = context.Scenario.DrawPerTurn,
                [CombatTurnFeatureNames.ActionsTakenThisTurn] =
                    state.PlayerActionsThisTurn,
                [CombatTurnFeatureNames.EnergySpentThisTurn] =
                    state.PlayerEnergySpentThisTurn,
                [CombatTurnFeatureNames.EnemyHpAtTurnStart] =
                    state.EnemyHpAtTurnStart,
                [CombatTurnFeatureNames.ConsecutiveNoProgressTurns] =
                    state.ConsecutiveNoProgressTurns,
                [CombatTurnFeatureNames.NoEffectActionAttemptsThisTurn] =
                    state.NoEffectActionAttemptsThisTurn.Values.Sum(),
                [CombatTurnFeatureNames.EndTurnPurposeValue] =
                    state.EndTurnPurposeValue,
                [CombatTurnFeatureNames.EndTurnPurposeCount] =
                    state.EndTurnPurposeValue > 0d ? 1d : 0d
            }
        };
        observation.Features["playerRole:" + player.DefinitionId] = 1d;
        foreach (var cardId in observation.DeckCardIds
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (context.Ruleset.TryGetCard(cardId, out var knownCard))
            {
                observation.CardTagsById[cardId] = knownCard.Tags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        CombatCampaignContextFeatureNames.ProjectScenario(
            context.Scenario,
            observation.Features);
        ProjectOwnedRewardFeatures(
            context.Scenario,
            observation.Features);
        ProjectLifecycleFeatures(
            context.Scenario,
            context.Ruleset,
            state,
            observation.Features);
        observation.DeckKnowledge.KnownDeckCardIds.AddRange(observation.DeckCardIds);
        if (context.Scenario.CampaignVariables.TryGetValue(
                "ResurrectionCount",
                out var resurrectionRaw)
            && int.TryParse(resurrectionRaw, out var resurrectionCount))
        {
            observation.Features[CombatArchetypePolicy.ResurrectionCountFeature] =
                Math.Max(0, resurrectionCount);
        }

        foreach (var friendly in state.LivingFriendlies
                     .OrderBy(actor => actor.ActorId))
        {
            observation.Friendlies.Add(ProjectActor(
                friendly,
                CombatTargetKind.Friendly,
                context.Ruleset));
        }
        foreach (var enemy in state.LivingEnemies.OrderBy(enemy => enemy.ActorId))
        {
            var projectedEnemy = ProjectActor(
                enemy,
                CombatTargetKind.Enemy,
                context.Ruleset);
            if (context.Ruleset.TryGetEnemy(enemy.DefinitionId, out var enemyDefinition))
            {
                projectedEnemy.Attack = Math.Max(
                    0d,
                    enemy.Variables.TryGetValue("Attack", out var attack)
                        ? attack
                        : 0d);
                projectedEnemy.Features["attack"] = projectedEnemy.Attack;
                projectedEnemy.Features["actionCount"] = Math.Max(
                    1,
                    enemyDefinition.ActionCount);
            }
            observation.Enemies.Add(projectedEnemy);
            AddThreat(context.Ruleset, enemy, observation.Threat);
        }
        observation.Threat.CurrentIntentKnown = observation.Enemies.Count > 0;
        observation.Threat.Confidence = observation.Threat.CurrentIntentKnown ? 1d : 0d;
        observation.ExpectedIncomingDamage =
            observation.Threat.ExpectedBlockableDamage
            + observation.Threat.ExpectedUnblockableDamage
            + observation.Threat.ExpectedDamageOverTime;

        var actionIndex = 0;
        foreach (var legal in context.LegalActions)
        {
            var action = ProjectAction(
                context.Scenario,
                context.Ruleset,
                state,
                legal);
            action.ObservationId = observation.ObservationId;
            action.ActionToken = "a" + actionIndex++;
            observation.Actions.Add(action);
        }
        return CombatPlayerObservationBoundary
            .FinalizeOwnedSimulationProjection(observation);
    }

    private static void ProjectOwnedRewardFeatures(
        CombatScenarioDefinition scenario,
        IDictionary<string, double> features)
    {
        foreach (var reward in scenario.RewardRules.Where(item =>
                     item != null
                     && !string.IsNullOrWhiteSpace(item.RewardId)))
        {
            var prefix = reward.Kind.Equals(
                "Blessing",
                StringComparison.OrdinalIgnoreCase)
                ? "blessing:"
                : reward.Kind.Equals(
                    "Relic",
                    StringComparison.OrdinalIgnoreCase)
                    ? "relic:"
                    : "";
            if (prefix.Length == 0)
            {
                continue;
            }
            var key = prefix + reward.RewardId;
            features[key] = features.TryGetValue(key, out var previous)
                ? previous + Math.Max(1, reward.Stacks)
                : Math.Max(1, reward.Stacks);
        }
    }

    private static void ProjectLifecycleFeatures(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        CombatBattleState state,
        IDictionary<string, double> features)
    {
        var player = state.Player;
        if (player == null)
        {
            return;
        }
        var unknown = 0;
        foreach (var status in player.Statuses)
        {
            if (!ruleset.TryGetStatus(status.StatusId, out var definition))
            {
                unknown++;
                continue;
            }
            foreach (var trigger in definition.Triggers)
            {
                if (trigger.EventKind != CombatSimulationEventKind.TurnEnded
                    && trigger.EventKind
                    != CombatSimulationEventKind.TurnStarted)
                {
                    continue;
                }
                if (!LifecycleTriggerMatches(
                        trigger,
                        status,
                        player,
                        state,
                        ruleset))
                {
                    continue;
                }
                var startTurn =
                    trigger.EventKind == CombatSimulationEventKind.TurnStarted;
                foreach (var effect in trigger.Effects)
                {
                    if (!LifecycleEffectConditionMatches(
                            effect,
                            player,
                            state,
                            ruleset))
                    {
                        continue;
                    }
                    if (!ProjectsToPlayer(effect.Target, effect.Kind))
                    {
                        if (effect.Target
                            is not CombatSimulationTarget.SelectedEnemy
                            and not CombatSimulationTarget.AllEnemies
                            and not CombatSimulationTarget.AllOpponents)
                        {
                            unknown++;
                        }
                        continue;
                    }
                    var amount = ProjectLifecycleAmount(
                        effect,
                        status,
                        player,
                        state,
                        ruleset);
                    if (!ProjectLifecycleEffect(
                            features,
                            effect.Kind,
                            amount,
                            startTurn,
                            player,
                            state,
                            scenario.HandLimit))
                    {
                        unknown++;
                    }
                }
                if (definition.Fidelity != CombatRuleFidelity.Authoritative)
                {
                    unknown++;
                }
            }
        }
        features[CombatTurnFeatureNames.UnknownLifecycleEffectCount] =
            Math.Max(0, unknown);
    }

    private static bool LifecycleTriggerMatches(
        CombatStatusTriggerDefinition trigger,
        CombatStatusState status,
        CombatActorState player,
        CombatBattleState state,
        CombatRuleset ruleset)
    {
        if (status.Stacks < trigger.MinimumStacks
            || status.Stacks > trigger.MaximumStacks
            || trigger.OwnerRelation
            == CombatStatusTriggerOwnerRelation.EventTargetAllyExceptSelf
            || !string.IsNullOrWhiteSpace(trigger.RequiredDefinitionId)
            || !string.IsNullOrWhiteSpace(trigger.RequiredEventMessage)
            || !string.IsNullOrWhiteSpace(trigger.RequiredActionTag))
        {
            return false;
        }
        if (trigger.ConditionExpression != null
            && CombatSimulationExpressionEvaluator.Evaluate(
                trigger.ConditionExpression,
                state,
                ruleset,
                player.ActorId,
                player.ActorId) <= 0d)
        {
            return false;
        }
        if (trigger.EveryNthEvent > 1)
        {
            var next = status.TriggerCounts.TryGetValue(
                trigger.TriggerId,
                out var previous)
                ? previous + 1
                : 1;
            if (next % trigger.EveryNthEvent != 0)
            {
                return false;
            }
        }
        if (string.IsNullOrWhiteSpace(trigger.CounterKey))
        {
            return true;
        }
        var counter = status.TriggerCounts.TryGetValue(
            trigger.CounterKey,
            out var stored)
            ? stored
            : 0;
        counter += trigger.CounterIncrementMode switch
        {
            CombatStatusCounterIncrementMode.Fixed =>
                trigger.CounterIncrement,
            CombatStatusCounterIncrementMode.EventAmount =>
                state.Turn * trigger.CounterIncrement,
            CombatStatusCounterIncrementMode.HandCount =>
                state.Hand.Count * trigger.CounterIncrement,
            _ => 0
        };
        return counter >= trigger.MinimumCounterValue
               && counter <= trigger.MaximumCounterValue
               && (trigger.CounterStep <= 0
                   || counter >= trigger.CounterStepOrigin
                   && (counter - trigger.CounterStepOrigin)
                   % trigger.CounterStep == 0);
    }

    private static bool LifecycleEffectConditionMatches(
        CombatSimulationEffectDefinition effect,
        CombatActorState player,
        CombatBattleState state,
        CombatRuleset ruleset)
    {
        return effect.ConditionExpression == null
               || CombatSimulationExpressionEvaluator.Evaluate(
                   effect.ConditionExpression,
                   state,
                   ruleset,
                   player.ActorId,
                   player.ActorId) > 0d;
    }

    private static bool ProjectsToPlayer(
        CombatSimulationTarget target,
        CombatSimulationEffectKind kind)
    {
        if (target is CombatSimulationTarget.Self
            or CombatSimulationTarget.Player
            or CombatSimulationTarget.EventSource
            or CombatSimulationTarget.EventTarget
            or CombatSimulationTarget.AllAllies)
        {
            return true;
        }
        return target == CombatSimulationTarget.None
               && kind is CombatSimulationEffectKind.Draw
                   or CombatSimulationEffectKind.DrawToHandLimit
                   or CombatSimulationEffectKind.GainEnergy;
    }

    private static double ProjectLifecycleAmount(
        CombatSimulationEffectDefinition effect,
        CombatStatusState status,
        CombatActorState player,
        CombatBattleState state,
        CombatRuleset ruleset)
    {
        var amount = effect.AmountExpression == null
            ? effect.Amount
            : CombatSimulationExpressionEvaluator.Evaluate(
                effect.AmountExpression,
                state,
                ruleset,
                player.ActorId,
                player.ActorId);
        if (effect.ScaleWithStatusStacks)
        {
            amount *= Math.Max(1, status.Stacks);
        }
        return amount * Math.Max(0d, Math.Min(1d, effect.Probability));
    }

    private static bool ProjectLifecycleEffect(
        IDictionary<string, double> features,
        CombatSimulationEffectKind kind,
        double amount,
        bool startTurn,
        CombatActorState player,
        CombatBattleState state,
        int handLimit)
    {
        var prefix = startTurn ? "startTurn" : "endTurn";
        switch (kind)
        {
            case CombatSimulationEffectKind.Damage:
            case CombatSimulationEffectKind.TrueDamage:
            case CombatSimulationEffectKind.DirectHpLoss:
                AddFeature(
                    features,
                    prefix + "LifecycleHpLoss",
                    Math.Max(0d, amount));
                return true;
            case CombatSimulationEffectKind.Heal:
                AddFeature(
                    features,
                    prefix + "LifecycleHeal",
                    Math.Max(0d, amount));
                return true;
            case CombatSimulationEffectKind.SetHp:
                if (amount >= player.Hp)
                {
                    AddFeature(
                        features,
                        prefix + "LifecycleHeal",
                        amount - player.Hp);
                }
                else
                {
                    AddFeature(
                        features,
                        prefix + "LifecycleHpLoss",
                        player.Hp - amount);
                }
                return true;
            case CombatSimulationEffectKind.SetHpToMax:
                AddFeature(
                    features,
                    prefix + "LifecycleHeal",
                    Math.Max(0, player.MaxHp - player.Hp));
                return true;
            case CombatSimulationEffectKind.GainBlock:
                AddFeature(
                    features,
                    prefix + "LifecycleDefend",
                    Math.Max(0d, amount));
                return true;
            case CombatSimulationEffectKind.SetBlock:
                AddFeature(
                    features,
                    prefix + "LifecycleDefend",
                    Math.Max(0d, amount - player.Block));
                return true;
            case CombatSimulationEffectKind.GainEnergy:
                AddFeature(
                    features,
                    prefix
                    + (amount >= 0d
                        ? "LifecyclePowerGain"
                        : "LifecyclePowerLoss"),
                    Math.Abs(amount));
                return true;
            case CombatSimulationEffectKind.Draw:
                AddFeature(
                    features,
                    prefix + "LifecycleDraw",
                    Math.Max(0d, amount));
                return true;
            case CombatSimulationEffectKind.DrawToHandLimit:
                AddFeature(
                    features,
                    prefix + "LifecycleDraw",
                    Math.Max(0, handLimit - state.Hand.Count));
                return true;
            default:
                return false;
        }
    }

    private static void AddFeature(
        IDictionary<string, double> features,
        string key,
        double value)
    {
        if (value <= 0d
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            return;
        }
        features[key] = features.TryGetValue(key, out var previous)
            ? previous + value
            : value;
    }

    private static List<string> CardIds(
        CombatBattleState state,
        IEnumerable<int> instanceIds)
    {
        var result = instanceIds is ICollection<int> collection
            ? new List<string>(collection.Count)
            : new List<string>();
        AppendCardIds(state, instanceIds, result);
        return result;
    }

    private static List<string> CardIds(
        CombatBattleState state,
        int capacity,
        IEnumerable<int> first,
        IEnumerable<int> second,
        IEnumerable<int> third,
        IEnumerable<int> fourth)
    {
        var result = new List<string>(Math.Max(0, capacity));
        AppendCardIds(state, first, result);
        AppendCardIds(state, second, result);
        AppendCardIds(state, third, result);
        AppendCardIds(state, fourth, result);
        return result;
    }

    private static void AppendCardIds(
        CombatBattleState state,
        IEnumerable<int> instanceIds,
        ICollection<string> destination)
    {
        foreach (var instanceId in instanceIds)
        {
            var card = state.FindCard(instanceId);
            if (card != null)
            {
                destination.Add(card.CardId);
            }
        }
    }

    private static List<CombatCardInstanceObservation> ProjectHandCards(
        CombatSimulationPolicyContext context)
    {
        var result = new List<CombatCardInstanceObservation>(
            context.State.Hand.Count);
        foreach (var instanceId in context.State.Hand)
        {
            var instance = context.State.FindCard(instanceId);
            if (instance == null)
            {
                continue;
            }
            context.Ruleset.TryGetCard(instance.CardId, out var definition);
            var retained = HasCardTag(instance, definition, "Retain");
            var exhaustsOnUse = definition?.Exhaust == true
                                || HasCardTag(instance, definition, "Burnout")
                                || HasCardTag(instance, definition, "Exhaust")
                                || HasCardTag(instance, definition, "Fragmented");
            var totalExtraCost = ParseCardVariable(instance, "TotalExCost");
            var extraUseCount = ParseCardVariable(instance, "ExUseCount");
            result.Add(new CombatCardInstanceObservation
            {
                RuntimeId = instance.InstanceId,
                CardId = instance.CardId,
                EffectiveCost = Math.Max(
                    0,
                    (definition?.Cost ?? 0)
                    + instance.CostModifier
                    + (int)Math.Round(totalExtraCost)),
                Retained = retained,
                ExhaustsOnUse = exhaustsOnUse,
                CreatedThisBattle = !string.IsNullOrWhiteSpace(
                    instance.CreationSource)
                                    && !string.Equals(
                                        instance.CreationSource,
                                        "starting-deck",
                                        StringComparison.OrdinalIgnoreCase),
                EnhancementCount = instance.EnchantmentIds.Count,
                Features = new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["choice:cost"] = definition?.Cost ?? 0,
                    ["choice:rarity"] = definition?.Rarity ?? 1,
                    ["mechanic:total-extra-cost"] = totalExtraCost,
                    ["mechanic:extra-use-count"] = extraUseCount,
                    ["hasVisibleWarning"] =
                        instance.EnchantmentIds.Count > 0 ? 1d : 0d,
                    ["retain"] = retained ? 1d : 0d,
                    ["exhaustOnUse"] = exhaustsOnUse ? 1d : 0d
                }
            });
        }
        return result;
    }

    private static bool HasCardTag(
        CombatCardInstanceState instance,
        CombatCardDefinition? definition,
        string tag)
    {
        return instance.Tags.Any(value => string.Equals(
                   value,
                   tag,
                   StringComparison.OrdinalIgnoreCase))
               || (definition?.Tags.Any(value => string.Equals(
                       value,
                       tag,
                       StringComparison.OrdinalIgnoreCase))
                   ?? false);
    }

    private static double ParseCardVariable(
        CombatCardInstanceState instance,
        string key)
    {
        return instance.Variables.TryGetValue(key, out var raw)
               && double.TryParse(
                   raw,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static CombatActionObservation ProjectAction(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset,
        CombatBattleState state,
        CombatSimulationAction action)
    {
        if (action.Kind == CombatSimulationActionKind.EndTurn)
        {
            return new CombatActionObservation
            {
                CandidateId = action.CandidateId,
                SourceId = "simulation:end-turn",
                DisplayName = "End Turn",
                Kind = CombatActionKind.EndTurn,
                RuntimeId = 0,
                Legal = true
            };
        }

        ruleset.TryGetCardCore(action.DefinitionId, out var definition);
        var semantics = definition == null
            ? new CombatActionSemantics { Uncertainty = 10d }
            : ProjectSemantics(ruleset, state, definition, action);
        ApplyScenarioActionContextSemantics(
            scenario,
            state,
            action,
            semantics);
        var instance = state.FindCard(action.CardInstanceId);
        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["turn"] = state.Turn,
            ["isSkill"] =
                action.Kind == CombatSimulationActionKind.UseSkill ? 1d : 0d,
            ["visibleFake"] = instance?.IsVisibleFake == true ? 1d : 0d,
            ["curse"] = definition != null && HasTag(definition, "Curse")
                ? 1d
                : 0d,
            ["unplayable"] =
                definition != null && HasTag(definition, "Unusable")
                    ? 1d
                    : 0d,
            ["hasVisibleWarning"] = instance?.EnchantmentIds.Count > 0 ? 1d : 0d,
            ["retain"] = definition != null && HasTag(definition, "Retain") ? 1d : 0d,
            ["inherent"] = definition != null && HasTag(definition, "Inherent") ? 1d : 0d,
            ["recycle"] = definition != null && HasTag(definition, "Recycle") ? 1d : 0d,
            ["ouroboros"] = definition != null && HasTag(definition, "Ouroboros") ? 1d : 0d,
            ["exhaustOnUse"] = definition?.Exhaust == true
                               || definition != null
                               && (HasTag(definition, "Burnout")
                                   || HasTag(definition, "Fragmented")
                                   || HasTag(definition, "Exhaust"))
                ? 1d
                : 0d
        };
        if (action.Kind == CombatSimulationActionKind.UseSkill)
        {
            features[CombatSkillTimingFeatureNames.ResetsEachBattle] = 1d;
            features[CombatSkillTimingFeatureNames.CurrentCooldown] =
                state.SkillCooldowns.TryGetValue(
                    action.CardInstanceId,
                    out var currentCooldown)
                    ? Math.Max(0, currentCooldown)
                    : 0d;
            features[CombatSkillTimingFeatureNames.CooldownAfterUse] =
                scenario.Player.SkillCooldownTurns.TryGetValue(
                    action.DefinitionId,
                    out var cooldownAfterUse)
                    ? Math.Max(0, cooldownAfterUse)
                    : Math.Max(0d, semantics.CooldownTurns);
            features[CombatSkillTimingFeatureNames.ActivationsThisBattle] =
                state.SkillActivationCounts.TryGetValue(
                    action.DefinitionId,
                    out var activationCount)
                    ? Math.Max(0, activationCount)
                    : 0d;
        }
        var strategyMatches = (scenario.StrategyProgress
                               ?? new List<CombatScenarioStrategyProgress>())
            .Where(item => item.ComponentCardIds.Contains(
                action.DefinitionId,
                StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (strategyMatches.Count > 0)
        {
            features["strategyCompletion"] =
                strategyMatches.Max(item => item.Completion);
            features["synergy"] = strategyMatches.Max(item =>
                Math.Max(0d, item.PlayPriority)
                * (0.5d + Math.Max(0d, Math.Min(1d, item.Completion)))
                * (item.Executable ? 1.25d : 1d));
            features["strategyInfinite"] = strategyMatches.Any(item =>
                string.Equals(
                    item.Kind,
                    "Infinite",
                    StringComparison.OrdinalIgnoreCase))
                ? 1d
                : 0d;
            features["strategyExecutable"] = strategyMatches.Any(item =>
                item.Executable)
                ? 1d
                : 0d;
            features["strategyDeterministic"] = strategyMatches.Any(item =>
                item.Deterministic)
                ? 1d
                : 0d;
        }
        var effectiveProjection = CombatSemanticAuditor.ProjectEffective(
            state,
            action,
            semantics,
            ruleset);
        features["effectiveHpDamage"] = effectiveProjection.Damage;
        features["effectiveDurabilityDamage"] =
            effectiveProjection.DurabilityDamage;
        features["effectiveDefend"] = effectiveProjection.Defend;
        features["effectiveHeal"] = effectiveProjection.Heal;
        features["deferredHpDamage"] =
            CombatActionSemanticMetrics.DeferredHpDamage(semantics);
        features["affectedEnemyCount"] = semantics.AffectedEnemyCount;
        var targetActor = state.FindActor(action.TargetActorId);
        var remainingBossPhases = RemainingBossPhases(targetActor);
        features["remainingBossPhases"] = remainingBossPhases;
        features["terminalLethalEligible"] =
            remainingBossPhases <= 0 ? 1d : 0d;
        if (instance?.Variables.TryGetValue("ThisCount", out var useCountRaw) == true
            && double.TryParse(
                useCountRaw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var useCount))
        {
            features["mechanic:card-use-count"] = Math.Max(0d, useCount);
        }
        return new CombatActionObservation
        {
            CandidateId = action.CandidateId,
            SourceId = definition?.CardId ?? action.DefinitionId,
            DisplayName = definition?.DisplayName ?? action.DefinitionId,
            Kind = action.Kind == CombatSimulationActionKind.UseSkill
                ? CombatActionKind.UseSkill
                : CombatActionKind.PlayCard,
            RuntimeId = action.CardInstanceId,
            TargetRuntimeId = action.TargetActorId,
            TargetKind = ResolveTargetKind(state, action.TargetActorId),
            Cost = action.Cost,
            Legal = true,
            Semantics = semantics,
            Features = features
        };
    }

    private static int RemainingBossPhases(CombatActorState? target)
    {
        if (target == null)
        {
            return 0;
        }
        var remaining = 0;
        foreach (var statusId in new[]
                 {
                     "SpecialBuff_OriginalSin",
                     "SpecialBuff_HJE_AbsoluteShield"
                 })
        {
            remaining = Math.Max(
                remaining,
                target.Statuses.FirstOrDefault(status => string.Equals(
                    status.StatusId,
                    statusId,
                    StringComparison.OrdinalIgnoreCase))?.Stacks ?? 0);
        }
        var immortalGodhead = target.Statuses.FirstOrDefault(status =>
            string.Equals(
                status.StatusId,
                "SpecialBuff_ImmortalGodhead",
                StringComparison.OrdinalIgnoreCase))?.Stacks ?? 0;
        return Math.Max(remaining, Math.Max(0, immortalGodhead - 1));
    }

    private static bool HasTag(CombatCardDefinition card, string tag)
    {
        return card.Tags.Any(value =>
            string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
    }

    private static CombatActionSemantics ProjectSemantics(
        CombatRuleset ruleset,
        CombatBattleState state,
        CombatCardDefinition card,
        CombatSimulationAction action)
    {
        return CombatAuthoritativeSemanticProjector.Project(
            ruleset,
            state,
            card,
            action);
    }

    private static void ApplyScenarioActionContextSemantics(
        CombatScenarioDefinition scenario,
        CombatBattleState state,
        CombatSimulationAction action,
        CombatActionSemantics semantics)
    {
        if (scenario == null
            || state == null
            || semantics == null
            || action.Kind is not (
                CombatSimulationActionKind.PlayCard
                or CombatSimulationActionKind.UseSkill))
        {
            return;
        }
        var configuredLoss = Math.Max(0, scenario.DirectHpLossAfterPlayerCard);
        var player = state.Player;
        if (configuredLoss <= 0 || player == null)
        {
            return;
        }

        var previousProjectedLoss = Math.Max(0d, semantics.SelfHpLoss);
        var hpBeforeContext = Math.Max(0d, player.Hp - previousProjectedLoss);
        var effectiveLoss = Math.Min(hpBeforeContext, configuredLoss);
        semantics.ContextSelfHpLoss += effectiveLoss;
        semantics.SelfHpLoss += effectiveLoss;
        semantics.Risk += effectiveLoss;
        semantics.MinimumHpDuringAction = Math.Max(
            0d,
            Math.Min(
                semantics.MinimumHpDuringAction > 0d
                    ? semantics.MinimumHpDuringAction
                    : hpBeforeContext,
                hpBeforeContext - effectiveLoss));
        semantics.StateChanges["player.hp"] =
            semantics.StateChanges.TryGetValue("player.hp", out var hpDelta)
                ? hpDelta - effectiveLoss
                : -effectiveLoss;
        semantics.StateChanges["context:direct-hp-loss-after-player-card"] =
            effectiveLoss;
        semantics.TargetEffects.Add(new CombatTargetedSemanticEffect
        {
            Phase = CombatSemanticEffectPhase.Immediate,
            Kind = CombatSemanticEffectKind.DirectHpLoss,
            Attribution = CombatSemanticEffectAttribution.ActionTriggeredContext,
            TargetRuntimeId = player.ActorId,
            DefinitionId = "difficulty:player-card-hp-loss",
            Trigger = CombatSimulationEventKind.CardPlayed.ToString(),
            SourceDefinitionId = "difficulty:player-card-hp-loss",
            SourceActionId = Math.Max(1L, state.ActionSequence + 1L),
            RawAmount = configuredLoss,
            EffectiveAmount = effectiveLoss,
            Probability = 1d,
            BypassesBlock = true,
            Contextual = true
        });
    }

    private static double MarginalStatusStacks(
        CombatRuleset ruleset,
        CombatBattleState state,
        CombatSimulationEffectDefinition effect,
        int targetActorId,
        int amount)
    {
        if (!ruleset.TryGetStatus(effect.DefinitionId, out var definition))
        {
            return Math.Max(1d, amount)
                   * Math.Max(0d, Math.Min(1d, effect.Probability));
        }
        IEnumerable<CombatActorState> targets = effect.Target switch
        {
            CombatSimulationTarget.AllEnemies => state.LivingEnemies,
            CombatSimulationTarget.AllAllies => state.Actors.Where(actor =>
                actor.Alive
                && actor.Kind != CombatSimulationActorKind.Enemy),
            _ => state.FindActor(targetActorId) is { } target
                ? new[] { target }
                : Array.Empty<CombatActorState>()
        };
        var requested = Math.Max(1, amount);
        var maximum = Math.Max(1, definition.MaximumStacks);
        var marginal = targets.Sum(actor =>
        {
            var current = actor.Statuses.FirstOrDefault(status =>
                string.Equals(
                    status.StatusId,
                    effect.DefinitionId,
                    StringComparison.OrdinalIgnoreCase))?.Stacks ?? 0;
            return Math.Max(0, Math.Min(maximum, current + requested) - current);
        });
        return marginal * Math.Max(0d, Math.Min(1d, effect.Probability));
    }

    private static int RoundEffectValue(
        double value,
        CombatSimulationValueRounding rounding)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= int.MaxValue) return int.MaxValue;
        if (value <= int.MinValue) return int.MinValue;
        return rounding switch
        {
            CombatSimulationValueRounding.Truncate => (int)value,
            CombatSimulationValueRounding.Floor => (int)Math.Floor(value),
            CombatSimulationValueRounding.Ceiling => (int)Math.Ceiling(value),
            _ => (int)Math.Round(value)
        };
    }

    private static CombatUnitObservation ProjectActor(
        CombatActorState actor,
        CombatTargetKind targetKind,
        CombatRuleset ruleset)
    {
        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var statuses = new List<CombatStatusObservation>();
        foreach (var status in actor.Statuses)
        {
            features["status:" + status.StatusId] = status.Stacks;
            AddMechanicFeatures(features, status.StatusId, status.Stacks);
            ruleset.TryGetStatus(status.StatusId, out var definition);
            var type = definition?.Tags.Contains(
                "Negative",
                StringComparer.OrdinalIgnoreCase) == true
                ? "Negative"
                : definition?.Tags.Contains(
                    "Positive",
                    StringComparer.OrdinalIgnoreCase) == true
                    ? "Positive"
                    : definition?.Metadata.GetValueOrDefault("Type", "") ?? "";
            var rarity = 1;
            if (definition?.Metadata.TryGetValue("Rarity", out var rarityRaw)
                == true)
            {
                int.TryParse(rarityRaw, out rarity);
                rarity = Math.Max(1, rarity);
            }
            statuses.Add(new CombatStatusObservation
            {
                StatusId = status.StatusId,
                DisplayName = definition?.DisplayName ?? status.StatusId,
                Level = status.Stacks,
                Rarity = rarity,
                UpperBound = definition?.MaximumStacks ?? 0,
                ReducePerTurn = definition?.ReducePerTurn ?? 0,
                ReducePerUse = definition?.ReducePerUse ?? 0,
                ReducePerAttacked = definition?.ReducePerAttacked ?? 0,
                Type = type
            });
        }
        return new CombatUnitObservation
        {
            RuntimeId = actor.ActorId,
            DefinitionId = actor.DefinitionId,
            Name = actor.DisplayName,
            Kind = targetKind,
            CurrentHp = actor.Hp,
            MaxHp = actor.MaxHp,
            Defend = actor.Block,
            Statuses = statuses,
            Features = features
        };
    }

    private static CombatTargetKind ResolveTargetKind(
        CombatBattleState state,
        int targetActorId)
    {
        if (targetActorId <= 0)
        {
            return CombatTargetKind.None;
        }
        var actor = state.FindActor(targetActorId);
        return actor?.Kind switch
        {
            CombatSimulationActorKind.Player => CombatTargetKind.Self,
            CombatSimulationActorKind.Friendly => CombatTargetKind.Friendly,
            CombatSimulationActorKind.Enemy => CombatTargetKind.Enemy,
            _ => CombatTargetKind.None
        };
    }

    private static void AddMechanicFeatures(
        IDictionary<string, double> features,
        string statusId,
        int stacks)
    {
        var id = statusId ?? "";
        if (id.IndexOf(
                "limitdamage",
                StringComparison.OrdinalIgnoreCase) >= 0)
        {
            features["damageLimitActive"] = 1d;
            features["damageLimitLevel"] = Math.Max(0, stacks);
        }
        if (id.IndexOf("frenzy", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("keenedge", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("counterattack", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("thorns", StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("extraordinary", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            features["escalationPressure"] =
                features.TryGetValue("escalationPressure", out var current)
                    ? current + Math.Max(1, stacks)
                    : Math.Max(1, stacks);
        }
    }

    private static void AddThreat(
        CombatRuleset ruleset,
        CombatActorState enemy,
        CombatThreatForecast threat)
    {
        if (!ruleset.TryGetEnemyCore(enemy.DefinitionId, out var definition))
        {
            return;
        }
        var intentIds = enemy.CurrentIntentIds.Count > 0
            ? enemy.CurrentIntentIds
            : string.IsNullOrWhiteSpace(enemy.CurrentIntentId)
                ? new List<string>()
                : new List<string> { enemy.CurrentIntentId };
        foreach (var intentId in intentIds)
        {
            var intent = definition.Intents.FirstOrDefault(candidate =>
                string.Equals(candidate.IntentId, intentId, StringComparison.OrdinalIgnoreCase));
            if (intent == null)
            {
                continue;
            }
            var item = new CombatIntentObservation
            {
                SourceRuntimeId = enemy.ActorId,
                SourceId = intent.IntentId,
                DisplayName = intent.DisplayName,
                Kind = CombatIntentKind.Unknown,
                Probability = 1d,
                Confidence = 1d,
                Current = true
            };
            foreach (var effect in intent.Effects)
            {
                var expected = effect.Amount * Math.Max(0d, Math.Min(1d, effect.Probability));
                if (effect.Kind == CombatSimulationEffectKind.Damage)
                {
                    item.Kind = CombatIntentKind.Attack;
                    item.BlockableDamage += expected;
                }
                else if (effect.Kind == CombatSimulationEffectKind.TrueDamage)
                {
                    item.Kind = CombatIntentKind.Attack;
                    item.UnblockableDamage += expected;
                }
            }
            threat.Intents.Add(item);
            threat.ExpectedBlockableDamage += item.BlockableDamage;
            threat.MaximumBlockableDamage += item.BlockableDamage;
            threat.ExpectedUnblockableDamage += item.UnblockableDamage;
            threat.AttackProbability = Math.Max(threat.AttackProbability, item.Probability);
        }
    }

    private static long StableSessionId(string scenarioId, ulong seed)
    {
        unchecked
        {
            var hash = 1469598103934665603UL ^ seed;
            foreach (var character in scenarioId ?? "")
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return (long)(hash & long.MaxValue);
        }
    }
}

public static class CombatSimulationObservationProjector
{
    public static CombatStateObservation Project(CombatSimulationPolicyContext context)
    {
        return PlayerEquivalentSimulationObservationProjector.Project(context);
    }
}
