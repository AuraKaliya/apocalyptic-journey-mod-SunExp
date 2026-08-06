using System;
using System.Collections.Generic;
using System.Linq;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatDecisionEngine
{
    private readonly IDecisionResidualModel residualModel;
    private readonly ICombatSearchGuidanceModel searchGuidance;
    private readonly ICombatPolicyValueModel policyValueModel;
    private readonly bool useRuntimeRegistries;
    private readonly ICombatSimulationRule[] isolatedSimulationRules;
    private readonly CombatDecisionPreparationSnapshot decisionPreparation;
    private readonly CombatRiskAwareRootSamplingPuctPlanner chancePuctPlanner;

    public CombatDecisionEngine(
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? searchGuidance = null,
        bool useRuntimeRegistries = true,
        ICombatPolicyValueModel? policyValueModel = null,
        IReadOnlyList<ICombatSimulationRule>? simulationRules = null,
        CombatDecisionPreparationSnapshot? decisionPreparation = null)
    {
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
        this.searchGuidance = searchGuidance ?? NullCombatSearchGuidanceModel.Instance;
        this.policyValueModel = policyValueModel ?? NullCombatPolicyValueModel.Instance;
        this.useRuntimeRegistries = useRuntimeRegistries;
        this.decisionPreparation = decisionPreparation
                                   ?? CombatDecisionPreparationSnapshot.Empty;
        isolatedSimulationRules = simulationRules?.Where(rule => rule != null).ToArray()
                                  ?? Array.Empty<ICombatSimulationRule>();
        chancePuctPlanner = new CombatRiskAwareRootSamplingPuctPlanner(
            this.residualModel,
            this.searchGuidance,
            this.useRuntimeRegistries,
            this.policyValueModel,
            isolatedSimulationRules);
    }

    public CombatStateObservation PrepareStateForIsolatedWorker(
        CombatStateObservation state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var prepared = CombatPlayerObservationBoundary.Normalize(state);
        if (!HasDecisionPreparation)
        {
            return prepared;
        }

        foreach (var action in prepared.Actions)
        {
            if (action == null || !action.Legal)
            {
                continue;
            }
            if (!EvaluatePreflight(prepared, action, out var reason))
            {
                action.Legal = false;
                action.RejectionReason = reason;
                continue;
            }
            var observedMechanics =
                CombatPlayerObservationBoundary.NormalizeSemantics(
                    action.Semantics);
            ApplySemantics(prepared, action);
            MergeMechanicalSemantics(action.Semantics, observedMechanics);
            action.Semantics =
                CombatPlayerObservationBoundary.NormalizeSemantics(
                    action.Semantics);
        }
        EnrichRoleStrategies(prepared);
        CombatArchetypePolicy.Enrich(prepared);
        foreach (var action in prepared.Actions)
        {
            CombatHandTransformPolicy.Enrich(prepared, action);
        }
        EnrichSkillTimings(prepared);
        return CombatPlayerObservationBoundary.Normalize(prepared);
    }

    public ICombatSimulationRule[] SnapshotSimulationRulesForIsolatedWorker()
    {
        return useRuntimeRegistries
            ? CombatAiRegistry.SnapshotSimulationRules()
            : isolatedSimulationRules.ToArray();
    }

    public CombatDecisionEngine CreateIsolatedWorker(
        IReadOnlyList<ICombatSimulationRule>? simulationRules)
    {
        return new CombatDecisionEngine(
            residualModel,
            searchGuidance,
            useRuntimeRegistries: false,
            policyValueModel,
            simulationRules);
    }

    public CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile? profile = null,
        CombatSearchExplorationOptions? exploration = null)
    {
        return Choose(state, profile, exploration, out _);
    }

    internal CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile? profile,
        CombatSearchExplorationOptions? exploration,
        out CombatStateObservation? preparedState,
        bool stateIsNormalizedAndOwned = false)
    {
        var allocationStart = ReadThreadAllocatedBytes();
        preparedState = null;
        var selectedProfile = profile ?? new CombatDecisionProfile();
        selectedProfile.Weights ??= new DecisionWeights();
        if (state == null || state.Actions == null || state.Actions.Count == 0)
        {
            return new CombatDecision { Reason = "no candidates" };
        }
        if (!stateIsNormalizedAndOwned)
        {
            state = CombatPlayerObservationBoundary.Normalize(state);
        }
        preparedState = state;
        if (HasDecisionPreparation)
        {
            foreach (var action in state.Actions.Where(action =>
                         action != null
                         && action.Legal
                         && action.Kind != CombatActionKind.EndTurn))
            {
                var observedMechanics =
                    CombatPlayerObservationBoundary.NormalizeSemantics(
                        action.Semantics);
                ApplySemantics(state, action);
                MergeMechanicalSemantics(
                    action.Semantics,
                    observedMechanics);
                action.Semantics =
                    CombatPlayerObservationBoundary.NormalizeSemantics(
                        action.Semantics);
            }
            EnrichRoleStrategies(state);
            CombatArchetypePolicy.Enrich(state);
            foreach (var action in state.Actions)
            {
                CombatHandTransformPolicy.Enrich(state, action);
            }
            EnrichSkillTimings(state);
        }

        var endTurn = (CombatActionObservation?)null;
        var endTurnEvaluation = (CombatCandidateEvaluation?)null;
        var evaluations = new List<CombatCandidateEvaluation>(state.Actions.Count);
        var hasNonFakeLegalAction = state.Actions.Exists(action =>
            action != null
            && action.Kind != CombatActionKind.EndTurn
            && action.Legal
            && !IsVisibleFake(action));
        for (var i = 0; i < state.Actions.Count; i++)
        {
            var action = state.Actions[i];
            if (action == null)
            {
                continue;
            }

            if (action.Kind == CombatActionKind.EndTurn)
            {
                endTurn = action;
                var endTurnUtility = BuildUtility(
                    state,
                    action,
                    selectedProfile);
                BuildFeaturesInto(
                    action.Features,
                    state,
                    action,
                    endTurnUtility,
                    selectedProfile);
                endTurnEvaluation = new CombatCandidateEvaluation
                {
                    Action = action,
                    Legal = action.Legal,
                    RejectionReason = action.RejectionReason,
                    RuleScore = 0d
                };
                evaluations.Add(endTurnEvaluation);
                continue;
            }

            var rejectionReason = action.RejectionReason;
            var legal = action.Legal;
            if (legal && hasNonFakeLegalAction && IsVisibleFake(action))
            {
                legal = false;
                rejectionReason = "visible fake card is dominated by a safe action";
            }
            if (legal)
            {
                legal = CombatArchetypePolicy.IsLegal(
                    state,
                    action,
                    out rejectionReason);
            }
            if (legal && HasDecisionPreparation)
            {
                legal = EvaluatePreflight(state, action, out rejectionReason);
            }
            CombatHandTransformPolicy.Enrich(state, action);

            var utility = BuildUtility(state, action, selectedProfile);
            BuildFeaturesInto(
                action.Features,
                state,
                action,
                utility,
                selectedProfile);
            var features = action.Features;
            var evaluatedUtility = utility.Clone();
            var graphEvaluation = DecisionGraphEvaluator.Evaluate(selectedProfile.Graph, features);
            evaluatedUtility.Add(graphEvaluation.UtilityDelta);
            if (graphEvaluation.Rejected)
            {
                legal = false;
                rejectionReason = "decision graph rejected candidate";
            }

            var baseRuleScore = legal
                ? selectedProfile.Weights.Score(evaluatedUtility)
                : 0d;
            var residual = legal
                ? EvaluateResidual(residualModel, features)
                : new DecisionResidualPrediction();
            evaluations.Add(new CombatCandidateEvaluation
            {
                Action = action,
                Legal = legal,
                RejectionReason = legal ? "" : rejectionReason,
                Utility = evaluatedUtility,
                BaseRuleScore = baseRuleScore,
                RawResidualScore = residual.RawCorrection,
                ResidualApplicability = residual.Applicability,
                AppliedResidualScore = residual.AppliedCorrection,
                RuleScore = baseRuleScore + residual.AppliedCorrection
            });
        }

        var endTurnAssessment = CombatEndTurnSafety.Assess(
            state,
            evaluations,
            selectedProfile);
        if (endTurn != null && endTurnEvaluation != null)
        {
            CombatEndTurnSafety.Annotate(
                endTurn,
                endTurnEvaluation,
                endTurnAssessment);
        }
        foreach (var terminal in evaluations.Where(candidate =>
                     candidate?.Action != null
                     && candidate.Action.Kind != CombatActionKind.EndTurn
                     && candidate.Action.Semantics?.EndsTurn == true
                     && candidate.Utility.Lethal <= 0d))
        {
            CombatEndTurnSafety.Annotate(
                terminal.Action,
                terminal,
                endTurnAssessment);
        }

        var dominantSetup = CombatActionDominance.SelectDamageToBlockSetup(
                                state,
                                evaluations)
                            ?? CombatActionDominance.SelectSafeFreeSetup(
                                state,
                                evaluations,
                                selectedProfile);
        if (dominantSetup != null)
        {
            dominantSetup.PlanScore = dominantSetup.RuleScore;
            return new CombatDecision
            {
                HasAction = true,
                Action = dominantSetup.Action,
                Score = dominantSetup.RuleScore,
                Reason = dominantSetup.Action.Semantics.DamageToBlockSetup
                    ? "damage-to-block setup dominance"
                    : "safe free setup dominance",
                ProfileId = selectedProfile.Id,
                Candidates = evaluations,
                EndTurnTrace = endTurnAssessment.Trace.ToCompactString(),
                Plan = new List<CombatPlanStep>
                {
                    new()
                    {
                        CandidateId = dominantSetup.Action.CandidateId,
                        SourceId = dominantSetup.Action.SourceId,
                        DisplayName = dominantSetup.Action.DisplayName,
                        StepScore = dominantSetup.RuleScore,
                        CumulativeScore = dominantSetup.RuleScore,
                        RemainingPower = state.CurrentPower
                    }
                },
                PlanSummary = "dominant-free-setup; plan=" + dominantSetup.Action.DisplayName,
                SearchAlgorithm = "dominance"
            };
        }

        var searchStart = ReadThreadAllocatedBytes();
        var search = chancePuctPlanner.Choose(
            state,
            evaluations,
            selectedProfile,
            exploration);
        var searchEnd = ReadThreadAllocatedBytes();
        CombatDecisionAllocationDiagnostics.Record(
            searchStart - allocationStart,
            searchEnd - searchStart);
        var hasPlanAction = search.HasAction;
        var planAction = search.Action;
        var planScore = search.Score;
        var planSteps = search.Steps;
        var planSummary = search.Summary;
        var governance = CombatDecisionGovernance.ReviewSearch(
            state,
            evaluations,
            endTurnAssessment,
            search,
            selectedProfile);
        var usingGovernanceFallback = false;
        if (selectedProfile.UseLowConfidenceFallback
            && governance.Decision
               == CombatGovernanceDecision.UseSafeFallback
            && governance.Candidate != null)
        {
            var fallback = governance.Candidate;
            usingGovernanceFallback = true;
            hasPlanAction = true;
            planAction = fallback.Action;
            planScore = fallback.RuleScore;
            planSteps = new List<CombatPlanStep>
            {
                new()
                {
                    CandidateId = fallback.Action.CandidateId,
                    SourceId = fallback.Action.SourceId,
                    DisplayName = fallback.Action.DisplayName,
                    StepScore = fallback.RuleScore,
                    CumulativeScore = fallback.RuleScore,
                    RemainingPower = Math.Max(
                        0,
                        state.CurrentPower - fallback.Action.Cost),
                    DeathRisk = fallback.SearchDeathRisk,
                    Visits = fallback.SearchVisits
                }
            };
            planSummary += "; governance-safe-fallback="
                           + fallback.Action.DisplayName;
        }
        else if (governance.Decision != CombatGovernanceDecision.Accept)
        {
            hasPlanAction = false;
            planAction = null;
            planSteps = new List<CombatPlanStep>();
            planSummary += "; governance=" + governance.Decision
                           + ":" + governance.Reason;
        }
        if (hasPlanAction
            && planAction != null
            && (!CombatEndTurnSafety.IsEndTurnEquivalent(planAction)
                || !endTurnAssessment.Prohibited
                || evaluations.Any(candidate =>
                    ReferenceEquals(candidate.Action, planAction)
                    && candidate.Utility.Lethal > 0d))
            && (usingGovernanceFallback
                || planScore >= selectedProfile.MinimumActionScore))
        {
            return new CombatDecision
            {
                HasAction = true,
                Action = planAction,
                Score = planScore,
                Reason = "player-equivalent risk-aware root-sampling search",
                ProfileId = selectedProfile.Id,
                Candidates = evaluations,
                EndTurnTrace = endTurnAssessment.Trace.ToCompactString(),
                Plan = planSteps,
                PlanSummary = planSummary,
                SearchAlgorithm = "risk-aware-root-sampling-puct-mpc",
                SearchSimulations = search.Simulations,
                SearchNodes = search.Nodes,
                SearchTranspositionHits = search.TranspositionHits,
                SearchStoppedEarly = search.StoppedEarly,
                SearchStoppedByTime = search.StoppedByTime,
                SearchConfidence = search.Confidence,
                SearchValueGap = search.ValueGap,
                SearchBestVisits = search.BestVisits,
                SearchSecondBestVisits = search.SecondBestVisits,
                SearchCandidateCount = search.CandidateCount,
                SearchOriginalCandidateCount = search.OriginalCandidateCount,
                SearchBudgetTier = search.BudgetTier,
                Performance = CombatDecisionPerformanceTelemetry.FromSearch(search),
                CertifiedLoops = search.CertifiedLoops,
                SustainableControlLoops =
                    search.SustainableControlLoops,
                FakeLoops = search.FakeLoops,
                BlockedLoops = search.BlockedLoops
            };
        }

        if (endTurn != null
            && endTurn.Legal
            && !endTurnAssessment.Prohibited)
        {
            return new CombatDecision
            {
                HasAction = true,
                Action = endTurn,
                Score = 0d,
                Reason = hasPlanAction ? "best plan below threshold" : "no positive legal action",
                ProfileId = selectedProfile.Id,
                Candidates = evaluations,
                EndTurnTrace = endTurnAssessment.Trace.ToCompactString(),
                PlanSummary = planSummary,
                SearchAlgorithm = "risk-aware-root-sampling-puct-mpc",
                SearchSimulations = search.Simulations,
                SearchNodes = search.Nodes,
                SearchTranspositionHits = search.TranspositionHits,
                SearchStoppedEarly = search.StoppedEarly,
                SearchStoppedByTime = search.StoppedByTime,
                SearchConfidence = search.Confidence,
                SearchValueGap = search.ValueGap,
                SearchBestVisits = search.BestVisits,
                SearchSecondBestVisits = search.SecondBestVisits,
                SearchCandidateCount = search.CandidateCount,
                SearchOriginalCandidateCount = search.OriginalCandidateCount,
                SearchBudgetTier = search.BudgetTier,
                Performance = CombatDecisionPerformanceTelemetry.FromSearch(search),
                CertifiedLoops = search.CertifiedLoops,
                SustainableControlLoops =
                    search.SustainableControlLoops,
                FakeLoops = search.FakeLoops,
                BlockedLoops = search.BlockedLoops
            };
        }

        if (endTurnAssessment.Prohibited)
        {
            var safeFallback = evaluations
                .Where(candidate =>
                    CombatEndTurnSafety.IsSafeAlternative(
                        state,
                        candidate,
                        selectedProfile))
                .OrderByDescending(candidate => candidate.RuleScore)
                .ThenBy(candidate => candidate.Action.CandidateId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (safeFallback != null)
            {
                return new CombatDecision
                {
                    HasAction = true,
                    Action = safeFallback.Action,
                    Score = safeFallback.RuleScore,
                    Reason = "end-turn safety fallback: "
                             + endTurnAssessment.Reason,
                    ProfileId = selectedProfile.Id,
                    Candidates = evaluations,
                    EndTurnTrace = endTurnAssessment.Trace.ToCompactString(),
                    PlanSummary = planSummary,
                    SearchAlgorithm = "end-turn-safety",
                    SearchSimulations = search.Simulations,
                    SearchNodes = search.Nodes,
                    SearchTranspositionHits = search.TranspositionHits,
                    SearchStoppedEarly = search.StoppedEarly,
                    SearchStoppedByTime = search.StoppedByTime,
                    SearchConfidence = search.Confidence,
                    SearchValueGap = search.ValueGap,
                    SearchBestVisits = search.BestVisits,
                    SearchSecondBestVisits = search.SecondBestVisits,
                    SearchCandidateCount = search.CandidateCount,
                    SearchOriginalCandidateCount = search.OriginalCandidateCount,
                    SearchBudgetTier = search.BudgetTier,
                    Performance = CombatDecisionPerformanceTelemetry.FromSearch(search)
                };
            }
        }

        return new CombatDecision
        {
            Reason = planSummary,
            ProfileId = selectedProfile.Id,
            Candidates = evaluations,
            EndTurnTrace = endTurnAssessment.Trace.ToCompactString(),
            SearchAlgorithm = "risk-aware-root-sampling-puct-mpc",
            SearchSimulations = search.Simulations,
            SearchNodes = search.Nodes,
            SearchTranspositionHits = search.TranspositionHits,
            SearchStoppedEarly = search.StoppedEarly,
            SearchStoppedByTime = search.StoppedByTime,
            SearchConfidence = search.Confidence,
            SearchValueGap = search.ValueGap,
            SearchBestVisits = search.BestVisits,
            SearchSecondBestVisits = search.SecondBestVisits,
            SearchCandidateCount = search.CandidateCount,
            SearchOriginalCandidateCount = search.OriginalCandidateCount,
            SearchBudgetTier = search.BudgetTier,
            Performance = CombatDecisionPerformanceTelemetry.FromSearch(search),
            CertifiedLoops = search.CertifiedLoops,
            SustainableControlLoops =
                search.SustainableControlLoops,
            FakeLoops = search.FakeLoops,
            BlockedLoops = search.BlockedLoops
        };
    }

    private static long ReadThreadAllocatedBytes()
    {
#if NET8_0_OR_GREATER
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return 0L;
#endif
    }

    private static void MergeMechanicalSemantics(
        CombatActionSemantics target,
        CombatActionSemantics observed)
    {
        if (target == null || observed == null)
        {
            return;
        }
        if (target.CardRetrievals.Count == 0
            && observed.CardRetrievals.Count > 0)
        {
            target.CardRetrievals = observed.CardRetrievals.Select(item =>
                new CombatCardRetrievalSemantic
                {
                    SourceZone = item.SourceZone,
                    DestinationZone = item.DestinationZone,
                    Amount = item.Amount,
                    RequiredCardTag = item.RequiredCardTag,
                    CandidateBranchCount = item.CandidateBranchCount
                }).ToList();
            target.CardGeneration = 0d;
            target.Draw = 0d;
            target.DeckValue = Math.Max(
                target.DeckValue,
                observed.DeckValue);
            target.OpensInteraction = true;
        }
        if (!target.EnergySetAmount.HasValue
            && !target.EnergyMinimum.HasValue
            && !target.RestoreEnergyToMaximum)
        {
            target.EnergySetAmount = observed.EnergySetAmount;
            target.EnergyMinimum = observed.EnergyMinimum;
            target.RestoreEnergyToMaximum =
                observed.RestoreEnergyToMaximum;
            if (target.EnergySetAmount.HasValue
                || target.EnergyMinimum.HasValue
                || target.RestoreEnergyToMaximum)
            {
                target.EnergyGain = Math.Max(
                    target.EnergyGain,
                    observed.EnergyGain);
            }
        }
    }

    public static DecisionUtility BuildUtility(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatDecisionProfile profile)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        var target = FindTarget(state, action.TargetRuntimeId);
        var player = state.Player ?? new CombatUnitObservation();
        var missingHp = Math.Max(0, player.MaxHp - player.CurrentHp);
        var hpRatio = player.MaxHp <= 0 ? 1d : (double)player.CurrentHp / player.MaxHp;
        var targetHp = target != null && target.Kind == CombatTargetKind.Enemy
            ? Math.Max(0, target.CurrentHp)
            : state.Enemies.Where(enemy => enemy.Alive).Sum(enemy => enemy.CurrentHp);
        var projection = CombatDamageLimitPolicy.Project(state, action);
        var projectedHpDamage = projection.HpDamage;
        var hpDamage = Feature(
            action,
            "effectiveHpDamage",
            projectedHpDamage);
        var projectedEffectiveDamage = projection.DurabilityDamage;
        var effectiveDamage = Feature(
            action,
            "effectiveDurabilityDamage",
            projectedEffectiveDamage);
        var deferredHpDamage = Feature(
            action,
            "deferredHpDamage",
            CombatActionSemanticMetrics.DeferredHpDamage(semantics));
        var overkill = targetHp > 0 ? Math.Max(0d, hpDamage - targetHp) : 0d;
        var terminalLethalEligible =
            Feature(action, "terminalLethalEligible", 1d) > 0.5d;
        var lethal = targetHp > 0
                     && hpDamage >= targetHp
                     && terminalLethalEligible
            ? 16d + Math.Min(8d, targetHp * 0.25d)
            : 0d;
        var unknown = Math.Max(0d, semantics.Uncertainty);
        var defend = Math.Max(
            0d,
            Feature(action, "effectiveDefend", semantics.Defend));
        var heal = Math.Min(
            missingHp,
            Math.Max(0d, Feature(action, "effectiveHeal", semantics.Heal)));
        var risk = semantics.Risk
                   + Math.Max(0d, semantics.SelfHpLoss) * 2d
                   + Math.Max(0d, semantics.EndOfCycleSelfHpLoss) * 1.5d;
        risk += Math.Max(
            0d,
            Feature(
                action,
                CombatRoleStrategyFeatureNames.Risk,
                0d));
        if (action.TargetKind == CombatTargetKind.Enemy)
        {
            risk += defend + heal;
            defend = 0d;
            heal = 0d;
        }
        var threat = state.Threat ?? new CombatThreatForecast();
        var riskAdjustedBlockable = threat.RiskAdjustedBlockableDamage(profile.ThreatRiskTolerance);
        if (!threat.CurrentIntentKnown
            && riskAdjustedBlockable <= 0d
            && state.ExpectedIncomingDamage > 0d)
        {
            riskAdjustedBlockable = state.ExpectedIncomingDamage;
        }
        var incomingGap = Math.Max(0d, riskAdjustedBlockable - player.Defend);
        var surplusDefend = Math.Max(0d, defend - incomingGap);
        var effectiveDefend = Math.Min(defend, incomingGap)
                              + surplusDefend * Math.Max(0d, profile.SurplusDefendRetention);
        if (!threat.CurrentIntentKnown && riskAdjustedBlockable <= 0d)
        {
            effectiveDefend += defend * (1d - hpRatio) * 0.1d;
        }
        var emergency = hpRatio <= profile.EmergencyHpRatio && (effectiveDefend > 0d || heal > 0d)
            ? 4d
            : 0d;
        var handCapacity = Math.Max(0, 10 - state.HandCount);
        var effectiveDraw = Math.Min(Math.Max(0d, semantics.Draw), handCapacity);
        var followUpCount = Math.Max(0, state.HandCount - 1);
        var setupValue = Math.Max(0d, semantics.Buff) * 0.8d
                         + Math.Max(0d, semantics.Debuff) * 0.9d
                         + Math.Max(0d, deferredHpDamage) * 0.75d
                         + Math.Max(0d, semantics.Cleanse)
                         + Math.Max(0d, semantics.PersistentValue)
                         + Math.Max(0d, semantics.CostReduction) * Math.Min(3, followUpCount) * 0.65d
                         + Math.Max(0d, semantics.CardGeneration) * Math.Min(2, handCapacity) * 0.8d;
        var scarcity = state.MaxPower <= 0
            ? 1d
            : 1d - Math.Min(1d, (double)state.CurrentPower / state.MaxPower);
        var energyOpportunityCost = Math.Max(0, action.Cost) * (0.75d + scarcity * 0.5d);
        var skillTiming = CombatSkillTimingPolicy.Enrich(action);
        var cooldownCost = action.Kind == CombatActionKind.UseSkill
                            && !skillTiming.Active
            ? Math.Max(0d, semantics.CooldownTurns) * profile.SkillCooldownPenalty
            : 0d;
        var knownPositive = effectiveDamage + effectiveDefend + heal + effectiveDraw
                            + semantics.EnergyGain + semantics.Scaling + semantics.DeckValue
                            + setupValue > 0d;
        var freeActionOrderValue = action.Cost == 0
                                   && knownPositive
                                   && !semantics.RandomOutcome
            ? profile.FreeActionTieBreaker
            : 0d;
        risk += overkill * 0.15d;
        if (semantics.RandomOutcome)
        {
            risk += 0.35d;
        }
        if (semantics.OpensInteraction)
        {
            risk += 0.1d;
        }
        risk += Math.Max(0d, -skillTiming.TimingAdvantage);
        var transformNetValue = Feature(
            action,
            "handTransformNetValue",
            0d);
        var transformDepletionRisk = Feature(
            action,
            "postTransformDepletionRisk",
            0d);
        var transformExpectedGrowth = Feature(
            action,
            "expectedGrowthFromTransform",
            0d);
        var transformLethal = Feature(
            action,
            "postTransformLethalCertified",
            0d) > 0.5d
            ? 14d
            : 0d;
        risk += transformDepletionRisk;
        if (IsVisibleFake(action))
        {
            risk += 24d;
            unknown = Math.Max(unknown, 3d);
        }
        if (semantics.Damage == 0d
            && semantics.Defend == 0d
            && semantics.Heal == 0d
            && semantics.Draw == 0d
            && semantics.EnergyGain == 0d
            && semantics.Scaling == 0d
            && semantics.DeckValue == 0d
            && semantics.HandTransform == null
            && setupValue == 0d)
        {
            unknown = Math.Max(unknown, profile.UnknownActionPenalty);
        }

        return new DecisionUtility
        {
            Survival = emergency + effectiveDefend + heal * 1.15d,
            Lethal = lethal + transformLethal,
            Tempo = effectiveDamage + effectiveDefend * 0.2d,
            Resource = semantics.EnergyGain * 1.5d
                       + semantics.CostReduction * 0.8d
                       - energyOpportunityCost
                       - cooldownCost,
            DeckEconomy = semantics.DeckValue
                          + semantics.CardGeneration * 0.5d
                          + transformNetValue
                          + transformExpectedGrowth * 0.15d,
            Scaling = semantics.Scaling
                      + setupValue
                      + Feature(
                          action,
                          CombatRoleStrategyFeatureNames.Scaling,
                          0d),
            Synergy = (action.Features.TryGetValue("synergy", out var synergy)
                ? synergy
                : 0d)
                      + Feature(
                          action,
                          CombatRoleStrategyFeatureNames.Synergy,
                          0d),
            Continuation = effectiveDraw
                           + semantics.EnergyGain
                           + semantics.CardGeneration * 0.5d
                           + Math.Max(0d, transformNetValue) * 0.25d
                           + Feature(
                               action,
                               CombatRoleStrategyFeatureNames.Continuation,
                               0d),
            Risk = risk,
            Uncertainty = unknown,
            Coordination = freeActionOrderValue
                           + Math.Max(0d, skillTiming.TimingAdvantage)
                           + (action.Features.TryGetValue("coordination", out var coordination) ? coordination : 0d)
                           + Feature(
                               action,
                               CombatRoleStrategyFeatureNames.Coordination,
                               0d)
        };
    }

    private static double Feature(
        CombatActionObservation action,
        string key,
        double fallback)
    {
        return action.Features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : fallback;
    }

    public static Dictionary<string, double> BuildFeatures(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var profile = new CombatDecisionProfile();
        var utility = BuildUtility(state, action, profile);
        return BuildFeatures(state, action, utility, profile);
    }

    public static Dictionary<string, double> BuildFeatures(
        CombatStateObservation state,
        CombatActionObservation action,
        DecisionUtility utility,
        CombatDecisionProfile profile)
    {
        var features = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        BuildFeaturesInto(features, state, action, utility, profile);
        return features;
    }

    public static void BuildFeaturesInto(
        IDictionary<string, double> features,
        CombatStateObservation state,
        CombatActionObservation action,
        DecisionUtility utility,
        CombatDecisionProfile profile)
    {
        if (features == null) throw new ArgumentNullException(nameof(features));
        var semantics = action.Semantics ?? new CombatActionSemantics();
        if (!ReferenceEquals(features, action.Features))
        {
            features.Clear();
            foreach (var pair in action.Features)
            {
                features[pair.Key] = pair.Value;
            }
        }
        features["power"] = state.CurrentPower;
        features["handCount"] = state.HandCount;
        features["playerHp"] = state.Player.CurrentHp;
        features["playerHpRatio"] = state.Player.MaxHp <= 0
            ? 0d
            : (double)state.Player.CurrentHp / state.Player.MaxHp;
        features["cost"] = action.Cost;
        features["damage"] = semantics.Damage;
        features["trueDamage"] = semantics.TrueDamage;
        features["damageOverTime"] = semantics.DamageOverTime;
        features["immediateHpDamage"] =
            CombatActionSemanticMetrics.ImmediateHpDamage(semantics);
        features["immediateDurabilityDamage"] =
            semantics.ImmediateDurabilityDamage;
        features["deferredHpDamage"] =
            CombatActionSemanticMetrics.DeferredHpDamage(semantics);
        features["affectedEnemyCount"] = semantics.AffectedEnemyCount;
        features["selfHpLoss"] = semantics.SelfHpLoss;
        features["endOfCycleSelfHpLoss"] =
            semantics.EndOfCycleSelfHpLoss;
        features["hitCount"] = semantics.HitCount;
        features["defend"] = semantics.Defend;
        features["heal"] = semantics.Heal;
        features["draw"] = semantics.Draw;
        features["energyGain"] = semantics.EnergyGain;
        features["buff"] = semantics.Buff;
        features["debuff"] = semantics.Debuff;
        features["cleanse"] = semantics.Cleanse;
        features["costReduction"] = semantics.CostReduction;
        features["cardGeneration"] = semantics.CardGeneration;
        features["persistentValue"] = semantics.PersistentValue;
        features["cooldownTurns"] = semantics.CooldownTurns;
        features["expectedIncomingDamage"] = state.ExpectedIncomingDamage;
        features["expectedBlockableDamage"] =
            state.Threat?.ExpectedBlockableDamage ?? 0d;
        features["maximumBlockableDamage"] =
            state.Threat?.MaximumBlockableDamage ?? 0d;
        features["expectedUnblockableDamage"] =
            state.Threat?.ExpectedUnblockableDamage ?? 0d;
        features["expectedDamageOverTime"] =
            state.Threat?.ExpectedDamageOverTime ?? 0d;
        features["attackProbability"] =
            state.Threat?.AttackProbability ?? 0d;
        features["threatConfidence"] = state.Threat?.Confidence ?? 0d;
        features["currentIntentKnown"] =
            state.Threat?.CurrentIntentKnown == true ? 1d : 0d;
        features["isFreeAction"] = action.Cost == 0 ? 1d : 0d;
        features["actionKindPlayCard"] =
            action.Kind == CombatActionKind.PlayCard ? 1d : 0d;
        features["actionKindUseSkill"] =
            action.Kind == CombatActionKind.UseSkill ? 1d : 0d;
        features["actionKindEndTurn"] =
            action.Kind == CombatActionKind.EndTurn ? 1d : 0d;
        features["targetKindNone"] =
            action.TargetKind == CombatTargetKind.None ? 1d : 0d;
        features["targetKindSelf"] =
            action.TargetKind == CombatTargetKind.Self ? 1d : 0d;
        features["targetKindFriendly"] =
            action.TargetKind == CombatTargetKind.Friendly ? 1d : 0d;
        features["targetKindEnemy"] =
            action.TargetKind == CombatTargetKind.Enemy ? 1d : 0d;
        features["uncertainty"] = semantics.Uncertainty;
        foreach (var pair in state.Features)
        {
            if (!features.ContainsKey(pair.Key))
            {
                features[pair.Key] = pair.Value;
            }
        }
        var target = FindTarget(state, action.TargetRuntimeId);
        if (target != null)
        {
            features["targetHp"] = target.CurrentHp;
            features["targetHpRatio"] = target.MaxHp <= 0
                ? 0d
                : (double)target.CurrentHp / target.MaxHp;
        }

        AddContextualFeatures(features, state, action, utility, profile, target);
    }

    public static DecisionResidualPrediction EvaluateResidual(
        IDecisionResidualModel model,
        IReadOnlyDictionary<string, double> features)
    {
        if (model is IContextualDecisionResidualModel contextual)
        {
            return contextual.Evaluate(features);
        }

        var correction = model?.Predict(features) ?? 0d;
        return new DecisionResidualPrediction
        {
            ModelId = model?.ModelId ?? "none",
            RawCorrection = correction,
            Applicability = correction == 0d ? 0d : 1d,
            AppliedCorrection = correction
        };
    }

    private static void AddContextualFeatures(
        IDictionary<string, double> features,
        CombatStateObservation state,
        CombatActionObservation action,
        DecisionUtility utility,
        CombatDecisionProfile profile,
        CombatUnitObservation? target)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        var player = state.Player ?? new CombatUnitObservation();
        var threat = state.Threat ?? new CombatThreatForecast();
        var riskAdjustedBlockable = threat.RiskAdjustedBlockableDamage(profile.ThreatRiskTolerance);
        if (!threat.CurrentIntentKnown
            && riskAdjustedBlockable <= 0d
            && state.ExpectedIncomingDamage > 0d)
        {
            riskAdjustedBlockable = state.ExpectedIncomingDamage;
        }

        var defend = action.TargetKind == CombatTargetKind.Enemy
            ? 0d
            : Math.Max(0d, semantics.Defend);
        var requiredDefend = Math.Max(0d, riskAdjustedBlockable - player.Defend);
        var immediateDefend = Math.Min(defend, requiredDefend);
        var shieldCarryGain = Math.Max(0d, defend - immediateDefend);
        var usefulDefend = defend;
        var wastedDefend = 0d;
        var missingHp = Math.Max(0d, player.MaxHp - player.CurrentHp);
        var heal = Math.Max(0d, semantics.Heal);
        var handCapacity = Math.Max(0d, 10 - state.HandCount);
        var draw = Math.Max(0d, semantics.Draw);
        var normalDamage = Math.Max(0d, semantics.Damage)
                           * Math.Max(1d, semantics.HitCount);
        var bypassDamage = Math.Max(0d, semantics.TrueDamage)
                           + Math.Max(0d, semantics.DamageOverTime);
        var targetHp = target != null && target.Kind == CombatTargetKind.Enemy
            ? Math.Max(0d, target.CurrentHp)
            : state.Enemies.Where(enemy => enemy.Alive).Sum(enemy => enemy.CurrentHp);
        var projection = CombatDamageLimitPolicy.Project(state, action);
        var hpDamage = projection.HpDamage;
        var effectiveDamage = projection.DurabilityDamage;
        var setupValue = CombatActionProductivity.SetupValue(semantics);
        var marginalSetupValue =
            CombatActionProductivity.MarginalSetupValue(state, semantics);
        var usefulNow = effectiveDamage + usefulDefend
                        + Math.Min(heal, missingHp)
                        + Math.Min(draw, handCapacity)
                        + Math.Max(0d, semantics.EnergyGain)
                        + marginalSetupValue > 0d;
        var recognizedSemantics = normalDamage + bypassDamage
                                  + defend
                                  + heal
                                  + draw
                                  + Math.Max(0d, semantics.EnergyGain)
                                  + setupValue > 0d
                                  || semantics.HandTransform != null;
        var semanticConfidence = recognizedSemantics
            ? 1d - Math.Min(1d, Math.Max(0d, semantics.Uncertainty) / 3d)
            : 0d;
        if (semantics.RandomOutcome)
        {
            semanticConfidence *= 0.7d;
        }

        features["requiredDefend"] = requiredDefend;
        features["immediateDefend"] = immediateDefend;
        features["shieldCarryGain"] = shieldCarryGain;
        features["usefulDefend"] = usefulDefend;
        features["wastedDefend"] = wastedDefend;
        features["effectiveHeal"] = Math.Min(heal, missingHp);
        features["overheal"] = Math.Max(0d, heal - missingHp);
        features["effectiveDraw"] = Math.Min(draw, handCapacity);
        features["overdraw"] = Math.Max(0d, draw - handCapacity);
        features["effectiveDamage"] = effectiveDamage;
        features["effectiveHpDamage"] = hpDamage;
        features["effectiveDurabilityDamage"] = effectiveDamage;
        features["damagePreventedByLimit"] = projection.PreventedHpDamage;
        features["damageLimitActive"] =
            projection.LimitDamageActive ? 1d : 0d;
        features["marginalSetupValue"] = marginalSetupValue;
        features["overkill"] = targetHp > 0d ? Math.Max(0d, hpDamage - targetHp) : 0d;
        features["lethal"] = targetHp > 0d && hpDamage >= targetHp ? 1d : 0d;
        features["energyScarcity"] = state.MaxPower <= 0
            ? 1d
            : 1d - Math.Min(1d, (double)state.CurrentPower / state.MaxPower);
        features["freeKnownValue"] = action.Cost == 0 && usefulNow && !semantics.RandomOutcome ? 1d : 0d;
        features["semanticConfidence"] = Math.Max(0d, Math.Min(1d, semanticConfidence));
        features["utilitySurvival"] = utility.Survival;
        features["utilityLethal"] = utility.Lethal;
        features["utilityTempo"] = utility.Tempo;
        features["utilityResource"] = utility.Resource;
        features["utilityDeckEconomy"] = utility.DeckEconomy;
        features["utilityScaling"] = utility.Scaling;
        features["utilitySynergy"] = utility.Synergy;
        features["utilityContinuation"] = utility.Continuation;
        features["utilityRisk"] = utility.Risk;
        features["utilityUncertainty"] = utility.Uncertainty;
        features["utilityCoordination"] = utility.Coordination;

        var category = CategoryOf(action);
        features["categoryAttack"] = category == "attack" ? 1d : 0d;
        features["categoryDefend"] = category == "defend" ? 1d : 0d;
        features["categorySupport"] = category == "support" ? 1d : 0d;
        features["categorySkill"] = category == "skill" ? 1d : 0d;
        features["categoryOther"] = category == "other" ? 1d : 0d;
    }

    private bool HasDecisionPreparation => useRuntimeRegistries
                                           || !decisionPreparation.IsEmpty;

    private bool EvaluatePreflight(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        return useRuntimeRegistries
            ? CombatAiRegistry.EvaluatePreflight(state, action, out reason)
            : decisionPreparation.EvaluatePreflight(state, action, out reason);
    }

    private void ApplySemantics(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (useRuntimeRegistries)
        {
            CombatAiRegistry.ApplySemantics(state, action);
            return;
        }
        decisionPreparation.ApplySemantics(state, action);
    }

    private void EnrichRoleStrategies(CombatStateObservation state)
    {
        if (useRuntimeRegistries)
        {
            CombatAiRegistry.EnrichRoleStrategies(state);
            return;
        }
        decisionPreparation.EnrichRoleStrategies(state);
    }

    private void EnrichSkillTimings(CombatStateObservation state)
    {
        if (useRuntimeRegistries)
        {
            CombatAiRegistry.EnrichSkillTimings(state);
            return;
        }
        decisionPreparation.EnrichSkillTimings(state);
    }

    private static bool IsVisibleFake(CombatActionObservation action)
    {
        return action?.Features != null
               && action.Features.TryGetValue("visibleFake", out var value)
               && value > 0.5d;
    }

    private static string CategoryOf(CombatActionObservation action)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        if (semantics.Damage > 0d || semantics.TrueDamage > 0d || semantics.DamageOverTime > 0d)
        {
            return "attack";
        }
        if (semantics.Defend > 0d)
        {
            return "defend";
        }
        if (semantics.Heal > 0d
            || semantics.Draw > 0d
            || semantics.EnergyGain > 0d
            || semantics.Buff > 0d
            || semantics.Debuff > 0d
            || semantics.Cleanse > 0d
            || semantics.CostReduction > 0d
            || semantics.CardGeneration > 0d
            || semantics.PersistentValue > 0d
            || semantics.Scaling > 0d
            || semantics.HandTransform != null)
        {
            return "support";
        }
        return action.Kind == CombatActionKind.UseSkill ? "skill" : "other";
    }

    private static CombatUnitObservation? FindTarget(CombatStateObservation state, int runtimeId)
    {
        if (runtimeId == 0)
        {
            return null;
        }

        if (state.Player.RuntimeId == runtimeId)
        {
            return state.Player;
        }

        for (var i = 0; i < state.Enemies.Count; i++)
        {
            if (state.Enemies[i].RuntimeId == runtimeId)
            {
                return state.Enemies[i];
            }
        }

        for (var i = 0; i < state.Friendlies.Count; i++)
        {
            if (state.Friendlies[i].RuntimeId == runtimeId)
            {
                return state.Friendlies[i];
            }
        }

        return null;
    }
}
