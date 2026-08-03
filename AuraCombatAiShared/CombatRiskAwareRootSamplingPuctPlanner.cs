using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatSearchResult
{
    public bool HasAction { get; set; }

    public CombatActionObservation? Action { get; set; }

    public double Score { get; set; }

    public double DeathRisk { get; set; }

    public List<CombatPlanStep> Steps { get; set; } = new();

    public string Summary { get; set; } = "";

    public int Simulations { get; set; }

    public int Nodes { get; set; }

    public int TranspositionHits { get; set; }

    public bool StoppedEarly { get; set; }

    public bool StoppedByTime { get; set; }

    public double Confidence { get; set; }

    public double ValueGap { get; set; }

    public int BestVisits { get; set; }

    public int SecondBestVisits { get; set; }

    public int CandidateCount { get; set; }

    public int OriginalCandidateCount { get; set; }

    public string BudgetTier { get; set; } = "";

    public string BudgetReason { get; set; } = "";

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }
}

public sealed class CombatRiskAwareRootSamplingPuctPlanner
{
    private readonly IDecisionResidualModel residualModel;
    private readonly ICombatSearchGuidanceModel guidanceModel;
    private readonly ICombatPolicyValueModel policyValueModel;
    private readonly bool useRuntimeRegistries;
    private readonly ICombatSimulationRule[] isolatedSimulationRules;
    private readonly Dictionary<ulong, SearchNode> transpositions = new();
    private readonly Dictionary<ulong, CombatPolicyValuePrediction> policyValueCache = new();
    private SearchNode[] nodePathBuffer = Array.Empty<SearchNode>();
    private SearchEdge[] edgePathBuffer = Array.Empty<SearchEdge>();
    private double[] rewardPathBuffer = Array.Empty<double>();
    private ulong[] cycleHashPathBuffer = Array.Empty<ulong>();
    private CombatSimulationState[] cycleStatePathBuffer =
        Array.Empty<CombatSimulationState>();
    private readonly CombatStateObservation scoreObservation = new();
    private readonly CombatActionObservation scoreAction = new();
    private readonly Dictionary<string, double> scoreStateFeatures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> scoreActionFeatures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> leafFeatures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CombatPolicyValueInput leafInput = new();
    private IReadOnlyList<SearchAction> actions = Array.Empty<SearchAction>();
    private CombatStateObservation rootObservation = new();
    private CombatDecisionProfile profile = new();
    private int nodeBudget;
    private int nodeCount;
    private int transpositionHits;
    private int certifiedLoops;
    private int sustainableControlLoops;
    private int fakeLoops;
    private int blockedLoops;
    private int searchMaxPly;
    private ICombatSimulationRule[] simulationRules = Array.Empty<ICombatSimulationRule>();
    private CombatBeliefState rootBelief = new();
    private int determinizationIndex;
    private CombatSearchExplorationOptions? rootExploration;
    private int originalCandidateCount;

    public CombatRiskAwareRootSamplingPuctPlanner(
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        bool useRuntimeRegistries = true,
        ICombatPolicyValueModel? policyValueModel = null,
        IReadOnlyList<ICombatSimulationRule>? simulationRules = null)
    {
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
        this.guidanceModel = guidanceModel ?? NullCombatSearchGuidanceModel.Instance;
        this.policyValueModel = policyValueModel ?? NullCombatPolicyValueModel.Instance;
        this.useRuntimeRegistries = useRuntimeRegistries;
        isolatedSimulationRules = simulationRules?.Where(rule => rule != null).ToArray()
                                  ?? Array.Empty<ICombatSimulationRule>();
    }

    public CombatSearchResult Choose(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile selectedProfile,
        CombatSearchExplorationOptions? exploration = null)
    {
        rootObservation = state;
        profile = selectedProfile;
        transpositions.Clear();
        policyValueCache.Clear();
        nodeCount = 0;
        transpositionHits = 0;
        certifiedLoops = 0;
        sustainableControlLoops = 0;
        fakeLoops = 0;
        blockedLoops = 0;
        determinizationIndex = Math.Max(
            0,
            exploration?.DeterminizationOffset ?? 0);
        rootExploration = exploration;
        rootBelief = CombatBeliefTracker.FromObservation(state);
        actions = BuildActions(state, candidates);
        var budget = CombatSearchBudgetPolicy.Resolve(
            state,
            candidates,
            selectedProfile);
        var searchStarted = Stopwatch.GetTimestamp();
        searchMaxPly = Math.Max(1, Math.Min(32, budget.MaxPly));
        var pathCapacity = Math.Max(2, searchMaxPly + 1);
        if (nodePathBuffer.Length < pathCapacity)
        {
            nodePathBuffer = new SearchNode[pathCapacity];
        }
        else
        {
            Array.Clear(nodePathBuffer, 0, nodePathBuffer.Length);
        }
        if (edgePathBuffer.Length < pathCapacity - 1)
        {
            edgePathBuffer = new SearchEdge[pathCapacity - 1];
            rewardPathBuffer = new double[pathCapacity - 1];
        }
        else
        {
            Array.Clear(edgePathBuffer, 0, edgePathBuffer.Length);
            Array.Clear(rewardPathBuffer, 0, rewardPathBuffer.Length);
        }
        if (cycleHashPathBuffer.Length < pathCapacity)
        {
            cycleHashPathBuffer = new ulong[pathCapacity];
            cycleStatePathBuffer = new CombatSimulationState[pathCapacity];
        }
        else
        {
            Array.Clear(cycleHashPathBuffer, 0, cycleHashPathBuffer.Length);
            Array.Clear(cycleStatePathBuffer, 0, cycleStatePathBuffer.Length);
        }
        simulationRules = useRuntimeRegistries
            ? CombatAiRegistry.SnapshotSimulationRules()
            : isolatedSimulationRules;
        nodeBudget = Math.Max(256, Math.Min(65536, budget.NodeBudget));
        if (actions.Count == 0)
        {
            return new CombatSearchResult { Summary = "no legal search action" };
        }

        var useGroupCount = actions.Count == 0 ? 0 : actions.Max(action => action.UseGroupIndex) + 1;
        var rootState = CombatForwardModel.Create(
            state,
            useGroupCount,
            rootBelief,
            CombatPublicObservationHasher.Seed(state, determinizationIndex++));
        var root = NewNode(rootState);
        EnsureEdges(root);

        var simulations = 0;
        // Every legal root action receives evidence before PUCT may concentrate the budget.
        for (var i = 0; i < actions.Count && nodeCount < nodeBudget; i++)
        {
            if (!root.Edges.TryGetValue(i, out var edge))
            {
                continue;
            }
            Simulate(root, edge);
            simulations++;
        }

        var simulationBudget = Math.Max(
            actions.Count,
            Math.Min(20000, budget.SimulationBudget));
        var minimumSimulations = Math.Min(
            simulationBudget,
            Math.Max(actions.Count, Math.Max(1, budget.MinimumSimulations)));
        var stabilityWindow = Math.Max(1, budget.StabilityWindow);
        var requiredStableChecks = Math.Max(1, budget.StableChecks);
        var lastBestAction = -1;
        var stableChecks = 0;
        var stoppedEarly = false;
        var stoppedByTime = false;
        while (simulations < simulationBudget && nodeCount < nodeBudget)
        {
            if (budget.TimeBudgetMilliseconds > 0
                && ElapsedMilliseconds(searchStarted)
                >= budget.TimeBudgetMilliseconds)
            {
                stoppedEarly = true;
                stoppedByTime = true;
                break;
            }
            Simulate(root, null);
            simulations++;
            if (simulations < minimumSimulations
                || simulations % stabilityWindow != 0)
            {
                continue;
            }

            var currentBest = StableBestAction(root);
            if (currentBest >= 0
                && currentBest == lastBestAction
                && RootLeadIsStable(root, currentBest, stabilityWindow))
            {
                stableChecks++;
                if (stableChecks >= requiredStableChecks)
                {
                    stoppedEarly = true;
                    break;
                }
            }
            else
            {
                stableChecks = currentBest >= 0
                               && RootLeadIsStable(
                                   root,
                                   currentBest,
                                   stabilityWindow)
                    ? 1
                    : 0;
            }
            lastBestAction = currentBest;
        }

        var rootEdges = root.Edges.Values
            .Where(edge => edge.ActionIndex >= 0 && edge.Visits > 0)
            .ToList();
        if (rootEdges.Count == 0)
        {
            return new CombatSearchResult
            {
                Summary = "search produced no root evidence",
                Simulations = simulations,
                Nodes = nodeCount,
                TranspositionHits = transpositionHits,
                BudgetTier = budget.Tier,
                BudgetReason = budget.Reason,
                StoppedEarly = stoppedEarly,
                StoppedByTime = stoppedByTime,
                CandidateCount = actions.Count,
                OriginalCandidateCount = originalCandidateCount
            };
        }

        var rankedRootEdges = RankRootEdges(rootEdges).ToList();
        var best = rankedRootEdges[0];
        var second = rankedRootEdges.Count > 1
            ? rankedRootEdges[1]
            : null;
        var valueGap = second == null
            ? Math.Abs(RootSelectionValue(best)) + 1d
            : RootSelectionValue(best) - RootSelectionValue(second);
        var confidence = RootConfidence(
            rootEdges,
            best,
            second,
            stoppedByTime);
        for (var i = 0; i < candidates.Count; i++)
        {
            var matching = rootEdges.FirstOrDefault(edge =>
                actions[edge.ActionIndex].MemberCandidateIds.Contains(
                    candidates[i].Action.CandidateId));
            var estimate = matching?.RiskEstimate(profile.TailRiskQuantile);
            candidates[i].PlanScore = matching == null ? 0d : RootSelectionValue(matching);
            candidates[i].SearchPrior = matching?.Prior ?? 0d;
            candidates[i].SearchVisits = matching?.Visits ?? 0;
            candidates[i].SearchDeathRisk = matching?.MeanRisk ?? 0d;
            candidates[i].SearchMeanReturn = estimate?.Mean ?? 0d;
            candidates[i].SearchReturnStandardError =
                estimate?.StandardError ?? 0d;
            candidates[i].SearchLowerTailMean =
                estimate?.RawLowerTailMean ?? 0d;
            candidates[i].SearchReturnQuantiles = matching?.ReturnQuantiles(16)
                                                   .ToList()
                                               ?? new List<double>();
        }

        var steps = BuildPrincipalVariation(root, best);
        return new CombatSearchResult
        {
            HasAction = true,
            Action = actions[best.ActionIndex].Action,
            Score = RootSelectionValue(best),
            DeathRisk = best.MeanRisk,
            Steps = steps,
            Simulations = simulations,
            Nodes = nodeCount,
            TranspositionHits = transpositionHits,
            StoppedEarly = stoppedEarly,
            StoppedByTime = stoppedByTime,
            Confidence = confidence,
            ValueGap = valueGap,
            BestVisits = best.Visits,
            SecondBestVisits = second?.Visits ?? 0,
            CandidateCount = actions.Count,
            OriginalCandidateCount = originalCandidateCount,
            BudgetTier = budget.Tier,
            BudgetReason = budget.Reason,
            CertifiedLoops = certifiedLoops,
            SustainableControlLoops = sustainableControlLoops,
            FakeLoops = fakeLoops,
            BlockedLoops = blockedLoops,
            Summary = BuildSummary(best, steps, simulations)
                      + (stoppedEarly && !stoppedByTime
                          ? "; early-stop=stable"
                          : "")
                      + (stoppedByTime ? "; time-budget-exhausted" : "")
                      + "; budget=" + budget.Tier
                      + "[" + budget.Reason + "]"
                      + "; confidence=" + confidence.ToString("0.000")
                      + "; root-candidates=" + actions.Count
                      + "/" + originalCandidateCount
                      + BuildLoopSummary()
        };
    }

    private double RootConfidence(
        IReadOnlyList<SearchEdge> rootEdges,
        SearchEdge best,
        SearchEdge? second,
        bool stoppedByTime)
    {
        if (second == null)
        {
            return 1d;
        }
        var totalVisits = Math.Max(1, rootEdges.Sum(edge => edge.Visits));
        var visitShare = best.Visits / (double)totalVisits;
        var evidence = Math.Min(
            1d,
            best.Visits / (double)Math.Max(4, actions.Count * 2));
        var gap = Math.Max(
            0d,
            RootSelectionValue(best) - RootSelectionValue(second));
        var scale = 1d
                    + Math.Abs(RootSelectionValue(best))
                    + Math.Abs(RootSelectionValue(second));
        var gapSignal = Math.Min(1d, gap / scale * 8d);
        var confidence = 0.45d * evidence
                         + 0.35d * visitShare
                         + 0.20d * gapSignal;
        if (stoppedByTime)
        {
            confidence *= 0.85d;
        }
        return Math.Max(0d, Math.Min(1d, confidence));
    }

    private static double ElapsedMilliseconds(long startedTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startedTimestamp)
               * 1000d
               / Stopwatch.Frequency;
    }

    private int StableBestAction(SearchNode root)
    {
        var rootEdges = root.Edges.Values
            .Where(edge => edge.ActionIndex >= 0 && edge.Visits > 0)
            .ToList();
        var usableRootActions = actions.Count(action =>
            IsUsable(root.State, action));
        if (rootEdges.Count != usableRootActions)
        {
            return -1;
        }
        return RankRootEdges(rootEdges).First().ActionIndex;
    }

    private bool RootLeadIsStable(
        SearchNode root,
        int bestActionIndex,
        int stabilityWindow)
    {
        var ranked = RankRootEdges(
                root.Edges.Values
                    .Where(edge => edge.ActionIndex >= 0
                                   && edge.Visits > 0
                                   && !edge.Disabled)
                    .ToList())
            .ToList();
        if (ranked.Count == 0
            || ranked[0].ActionIndex != bestActionIndex)
        {
            return false;
        }
        if (ranked.Count == 1)
        {
            return ranked[0].Visits >= 2;
        }
        var best = ranked[0];
        var second = ranked[1];
        if (best.Visits < 2 || second.Visits < 1)
        {
            return false;
        }

        var requiredVisitLead = Math.Max(4, stabilityWindow / 4);
        var requiredValueLead = 0.1d
                                * profile.UncertaintyPenalty
                                * (best.RiskEstimate(profile.TailRiskQuantile)
                                       .StandardError
                                   + second.RiskEstimate(profile.TailRiskQuantile)
                                       .StandardError);
        return best.Visits - second.Visits >= requiredVisitLead
               && RootSelectionValue(best) - RootSelectionValue(second)
               > requiredValueLead;
    }

    private void Simulate(SearchNode root, SearchEdge? forcedRoot)
    {
        var useGroupCount = actions.Count == 0
            ? 0
            : actions.Max(action => action.UseGroupIndex) + 1;
        var currentState = CombatForwardModel.Create(
            rootObservation,
            useGroupCount,
            rootBelief,
            CombatPublicObservationHasher.Seed(
                rootObservation,
                determinizationIndex++));
        var node = root;
        var nodePathCount = 1;
        var edgePathCount = 0;
        nodePathBuffer[0] = root;
        var terminalValue = 0d;
        var risk = 0d;
        var resolved = false;
        var cyclePathCount = 1;
        cycleHashPathBuffer[0] = currentState.CycleHash();
        cycleStatePathBuffer[0] = currentState;

        for (var ply = 0; ply < searchMaxPly; ply++)
        {
            if (currentState.AllEnemiesDefeated)
            {
                var terminal = EvaluateLeaf(currentState);
                terminalValue = terminal.Value;
                risk = terminal.DeathRisk;
                resolved = true;
                break;
            }

            EnsureEdges(node, currentState);
            var edge = ply == 0 && forcedRoot != null
                ? forcedRoot
                : SelectEdge(node, currentState);
            if (edge == null)
            {
                var leaf = EvaluateLeaf(currentState);
                terminalValue = leaf.Value;
                risk = leaf.DeathRisk;
                resolved = true;
                break;
            }

            var searchAction = actions[edge.ActionIndex];
            if (!IsUsable(currentState, searchAction))
            {
                var leaf = EvaluateLeaf(currentState);
                terminalValue = leaf.Value - 25d;
                risk = Math.Max(leaf.DeathRisk, 0.1d);
                resolved = true;
                break;
            }

            edgePathBuffer[edgePathCount] = edge;
            rewardPathBuffer[edgePathCount] = 0d;
            edgePathCount++;
            if (searchAction.Action.Kind == CombatActionKind.EndTurn)
            {
                var endState = CombatForwardModel.ApplyEndTurn(currentState, profile);
                var endLeaf = EvaluateLeaf(endState);
                terminalValue = endLeaf.Value;
                risk = endLeaf.DeathRisk;
                resolved = true;
                break;
            }

            var outcome = SelectOutcome(edge);
            var immediate = Score(currentState, searchAction.Action);
            rewardPathBuffer[edgePathCount - 1] = immediate;
            var nextState = CombatForwardModel.Apply(
                currentState,
                searchAction.Action,
                searchAction.UseGroupIndex,
                outcome.Outcome,
                profile);
            if (RequiresFreshObservation(searchAction.Action))
            {
                var observationLeaf = EvaluateLeaf(nextState);
                terminalValue = observationLeaf.Value;
                risk = observationLeaf.DeathRisk;
                resolved = true;
                goto CompleteSimulation;
            }
            var cycleHash = nextState.CycleHash();
            var cycleStartIndex = FindCycleStart(
                cycleHash,
                cyclePathCount);
            if (cycleStartIndex >= 0)
            {
                var assessment = CombatLoopSafetyAnalyzer.Analyze(
                    cycleStatePathBuffer[cycleStartIndex],
                    nextState,
                    profile);
                var leaf = EvaluateLeaf(nextState);
                switch (assessment.Classification)
                {
                    case CombatLoopClassification.CertifiedLethal:
                        certifiedLoops++;
                        terminalValue = Math.Max(leaf.Value, 100d)
                                        + Math.Min(
                                            25d,
                                            assessment.EffectiveEnemyProgress * 0.25d);
                        risk = leaf.DeathRisk;
                        resolved = true;
                        goto CompleteSimulation;
                    case CombatLoopClassification.Fake:
                        fakeLoops++;
                        terminalValue = leaf.Value - 120d;
                        risk = 1d;
                        resolved = true;
                        goto CompleteSimulation;
                    case CombatLoopClassification.Blocked:
                        blockedLoops++;
                        terminalValue = leaf.Value - 45d;
                        risk = Math.Max(0.35d, leaf.DeathRisk);
                        resolved = true;
                        goto CompleteSimulation;
                    case CombatLoopClassification.SustainableControl:
                        sustainableControlLoops++;
                        terminalValue = leaf.Value - 15d;
                        risk = leaf.DeathRisk;
                        resolved = true;
                        goto CompleteSimulation;
                }
            }
            if (cyclePathCount < cycleHashPathBuffer.Length)
            {
                cycleHashPathBuffer[cyclePathCount] = cycleHash;
                cycleStatePathBuffer[cyclePathCount] = nextState;
                cyclePathCount++;
            }
            var hash = nextState.Hash();
            SearchNode child;
            if (transpositions.TryGetValue(hash, out var existing))
            {
                child = existing;
                transpositionHits++;
            }
            else if (nodeCount < nodeBudget)
            {
                child = NewNode(nextState);
                transpositions[hash] = child;
            }
            else
            {
                var leaf = EvaluateLeaf(nextState);
                terminalValue = leaf.Value;
                risk = leaf.DeathRisk;
                resolved = true;
                break;
            }

            outcome.RecordChild(hash, child);
            outcome.Visits++;
            node = child;
            currentState = child.State;
            nodePathBuffer[nodePathCount++] = node;
            if (node.Visits == 0)
            {
                var leaf = EvaluateLeaf(currentState);
                terminalValue = leaf.Value;
                risk = leaf.DeathRisk;
                resolved = true;
                break;
            }

            if (ply == searchMaxPly - 1)
            {
                var leaf = EvaluateLeaf(currentState);
                terminalValue = leaf.Value;
                risk = leaf.DeathRisk;
                resolved = true;
            }
        }

CompleteSimulation:
        if (!resolved)
        {
            var leaf = EvaluateLeaf(currentState);
            terminalValue = leaf.Value;
            risk = leaf.DeathRisk;
        }
        if (nodePathCount > edgePathCount)
        {
            var leafNode = nodePathBuffer[nodePathCount - 1];
            leafNode.Visits++;
            leafNode.ValueSum += terminalValue;
            leafNode.RiskSum += risk;
        }
        var value = terminalValue;
        for (var i = edgePathCount - 1; i >= 0; i--)
        {
            value = rewardPathBuffer[i] + value * 0.985d;
            edgePathBuffer[i].Record(value, risk);
            nodePathBuffer[i].Visits++;
            nodePathBuffer[i].ValueSum += value;
            nodePathBuffer[i].RiskSum += risk;
        }
    }

    private int FindCycleStart(ulong cycleHash, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            if (cycleHashPathBuffer[i] == cycleHash)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool RequiresFreshObservation(CombatActionObservation action)
    {
        var semantics = action?.Semantics;
        return semantics != null
               && (semantics.Draw > 0d
                   || semantics.CardGeneration > 0d
                   || semantics.OpensInteraction);
    }

    private string BuildLoopSummary()
    {
        var total = certifiedLoops
                    + sustainableControlLoops
                    + fakeLoops
                    + blockedLoops;
        return total == 0
            ? ""
            : "; loops=certified:"
              + certifiedLoops
              + ",control:"
              + sustainableControlLoops
              + ",fake:"
              + fakeLoops
              + ",blocked:"
              + blockedLoops;
    }

    private void EnsureEdges(
        SearchNode node,
        CombatSimulationState? stateOverride = null)
    {
        var legalityState = stateOverride ?? node.State;
        CombatPolicyValuePrediction? networkPrediction = null;
        if (node.Edges.Count == 0
            && !ReferenceEquals(
                policyValueModel,
                NullCombatPolicyValueModel.Instance))
        {
            var usable = actions
                .Where(action => IsUsable(legalityState, action))
                .Select(action => action.Evaluation)
                .ToList();
            if (usable.Count > 0)
            {
                networkPrediction = policyValueModel.Evaluate(
                    CombatPolicyValueEncoding.BuildInput(
                        ToObservation(legalityState),
                        usable));
            }
        }
        var priorTotal = 0d;
        for (var i = 0; i < actions.Count; i++)
        {
            if (!IsUsable(legalityState, actions[i]))
            {
                continue;
            }
            if (!node.Edges.TryGetValue(i, out var edge))
            {
                edge = new SearchEdge
                {
                    ActionIndex = i,
                    Prior = actions[i].Prior,
                    PredictedValue = NetworkActionValue(
                        networkPrediction,
                        actions[i].Action.CandidateId,
                        profile.TailRiskQuantile)
                };
                for (var outcomeIndex = 0; outcomeIndex < actions[i].Model.Outcomes.Count; outcomeIndex++)
                {
                    edge.Outcomes.Add(new SearchOutcome
                    {
                        Outcome = actions[i].Model.Outcomes[outcomeIndex]
                    });
                }
                node.Edges[i] = edge;
            }
            priorTotal += edge.Prior;
        }

        if (priorTotal <= 0d)
        {
            return;
        }
        foreach (var edge in node.Edges.Values)
        {
            edge.NormalizedPrior = edge.Prior / priorTotal;
        }
    }

    private SearchEdge? SelectEdge(
        SearchNode node,
        CombatSimulationState? stateOverride = null)
    {
        var legalityState = stateOverride ?? node.State;
        SearchEdge? best = null;
        var bestScore = double.NegativeInfinity;
        var parentVisits = Math.Max(1, node.Visits);
        foreach (var edge in node.Edges.Values)
        {
            if (edge.Disabled
                || (edge.ActionIndex >= 0
                    && !IsUsable(legalityState, actions[edge.ActionIndex])))
            {
                continue;
            }

            var exploitation = edge.Visits == 0
                ? edge.PredictedValue
                : RootSelectionValue(edge);
            var exploration = profile.SearchExploration
                              * edge.NormalizedPrior
                              * Math.Sqrt(parentVisits)
                              / (1d + edge.Visits);
            var score = exploitation + exploration;
            if (score > bestScore)
            {
                best = edge;
                bestScore = score;
            }
        }
        return best;
    }

    private static SearchOutcome SelectOutcome(SearchEdge edge)
    {
        if (edge.Outcomes.Count == 1)
        {
            return edge.Outcomes[0];
        }
        return edge.Outcomes
            .OrderBy(outcome =>
                outcome.Visits / Math.Max(0.000001d, outcome.Outcome.Probability))
            .ThenByDescending(outcome => outcome.Outcome.Probability)
            .First();
    }

    private double Score(CombatSimulationState simulation, CombatActionObservation source)
    {
        var state = ToObservation(simulation);
        var effectiveCost = CombatForwardModel.EffectiveCost(simulation, source);
        PrepareScoreAction(source, effectiveCost);
        var utility = CombatDecisionEngine.BuildUtility(state, scoreAction, profile);
        CombatDecisionEngine.BuildFeaturesInto(
            scoreActionFeatures,
            state,
            scoreAction,
            utility,
            profile);
        var graph = DecisionGraphEvaluator.Evaluate(
            profile.Graph,
            scoreActionFeatures);
        if (graph.Rejected)
        {
            return -1000d;
        }
        utility.Add(graph.UtilityDelta);
        var residual = CombatDecisionEngine.EvaluateResidual(
            residualModel,
            scoreActionFeatures);
        return profile.Weights.Score(utility) + residual.AppliedCorrection;
    }

    private CombatStateObservation ToObservation(CombatSimulationState simulation)
    {
        var threat = scoreObservation.Threat;
        threat.ExpectedBlockableDamage = 0d;
        threat.MaximumBlockableDamage = 0d;
        threat.ExpectedUnblockableDamage = 0d;
        threat.ExpectedDamageOverTime = 0d;
        threat.AttackProbability = 0d;
        threat.LethalProbability = 0d;
        threat.IntentPoolSize = 0;
        threat.Summary = "";
        threat.Intents.Clear();
        for (var i = 0; i < simulation.Threats.Length; i++)
        {
            var item = simulation.Threats[i];
            if (item.SourceRuntimeId != 0
                && !simulation.Enemies.Any(enemy => enemy.RuntimeId == item.SourceRuntimeId && enemy.Hp > 0))
            {
                continue;
            }
            threat.ExpectedBlockableDamage += item.BlockableDamage * item.Probability;
            threat.MaximumBlockableDamage += item.BlockableDamage;
            threat.ExpectedUnblockableDamage += item.UnblockableDamage * item.Probability;
            threat.ExpectedDamageOverTime += item.DamageOverTime * item.Probability;
            threat.AttackProbability = Math.Max(threat.AttackProbability, item.Probability);
        }
        threat.CurrentIntentKnown = rootObservation.Threat?.CurrentIntentKnown == true;
        threat.Confidence = rootObservation.Threat?.Confidence ?? 0d;
        var player = scoreObservation.Player;
        player.RuntimeId = simulation.PlayerRuntimeId;
        player.Kind = CombatTargetKind.Self;
        player.CurrentHp = simulation.PlayerHp;
        player.MaxHp = simulation.PlayerMaxHp;
        player.Defend = simulation.PlayerDefend;
        while (scoreObservation.Enemies.Count < simulation.Enemies.Length)
        {
            scoreObservation.Enemies.Add(new CombatUnitObservation());
        }
        if (scoreObservation.Enemies.Count > simulation.Enemies.Length)
        {
            scoreObservation.Enemies.RemoveRange(
                simulation.Enemies.Length,
                scoreObservation.Enemies.Count - simulation.Enemies.Length);
        }
        for (var i = 0; i < simulation.Enemies.Length; i++)
        {
            var source = simulation.Enemies[i];
            var target = scoreObservation.Enemies[i];
            target.RuntimeId = source.RuntimeId;
            target.Kind = CombatTargetKind.Enemy;
            target.CurrentHp = source.Hp;
            target.MaxHp = source.MaxHp;
            target.Defend = source.Defend;
        }
        CombatSearchFeatureProjector.ProjectLeafInto(
            scoreStateFeatures,
            simulation,
            profile,
            rootObservation.Features);
        scoreStateFeatures["cardCostMultiplier"] =
            simulation.CardCostMultiplier;
        scoreObservation.BattleSessionId = rootObservation.BattleSessionId;
        scoreObservation.Sequence = rootObservation.Sequence;
        scoreObservation.CurrentPower = simulation.Power;
        scoreObservation.MaxPower = simulation.MaxPower;
        scoreObservation.HandCount = simulation.HandCount;
        scoreObservation.ExpectedIncomingDamage =
            threat.ExpectedBlockableDamage
            + threat.ExpectedUnblockableDamage
            + threat.ExpectedDamageOverTime;
        scoreObservation.Features = scoreStateFeatures;
        scoreObservation.IsPlayerActionWindow = true;
        return scoreObservation;
    }

    private IReadOnlyList<SearchAction> BuildActions(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates)
    {
        var legal = candidates
            .Where(candidate => candidate.Legal
                                && candidate.Action != null)
            .ToList();
        originalCandidateCount = legal.Count;
        if (legal.Count == 0)
        {
            return Array.Empty<SearchAction>();
        }

        var groups = legal
            .GroupBy(
                candidate => CandidateEquivalenceKey(candidate.Action),
                StringComparer.Ordinal)
            .Select(group => new
            {
                Members = group
                    .OrderByDescending(candidate => candidate.RuleScore)
                    .ThenBy(candidate =>
                        candidate.Action.CandidateId,
                        StringComparer.Ordinal)
                    .ToList()
            })
            .OrderBy(group =>
                group.Members[0].Action.CandidateId,
                StringComparer.Ordinal)
            .ToList();
        legal = groups.Select(group => group.Members[0]).ToList();
        var networkPrediction = ReferenceEquals(
                policyValueModel,
                NullCombatPolicyValueModel.Instance)
            ? new CombatPolicyValuePrediction()
            : policyValueModel.Evaluate(
                CombatPolicyValueEncoding.BuildInput(state, legal));
        var ruleMean = legal.Average(candidate => candidate.RuleScore);
        var ruleVariance = legal.Average(candidate =>
        {
            var delta = candidate.RuleScore - ruleMean;
            return delta * delta;
        });
        var ruleDeviation = Math.Sqrt(Math.Max(0d, ruleVariance));
        var logits = legal
            .Select(candidate =>
                NormalizeRuleScore(
                    candidate.RuleScore,
                    ruleMean,
                    ruleDeviation)
                + ClampFinite(
                    guidanceModel.PolicyLogit(
                        candidate.Action.Features),
                    -4d,
                    4d)
                + NetworkPolicyLogit(
                    networkPrediction,
                    candidate.Action.CandidateId)
                + ContinuationPriorLogit(candidate.Action))
            .Select(value => Math.Max(-30d, Math.Min(30d, value)))
            .ToArray();
        var maximum = logits.Max();
        var unnormalized = legal
            .Select((candidate, index) => Math.Exp(logits[index] - maximum))
            .ToArray();
        var total = Math.Max(0.000001d, unnormalized.Sum());
        var priors = unnormalized
            .Select(value => value / total)
            .ToArray();
        ApplyRootExploration(priors);
        var result = new List<SearchAction>(legal.Count);
        var useGroupIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var useGroupMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var useKey = CandidateUseEquivalenceKey(group.Members[0].Action);
            if (!useGroupIndexes.ContainsKey(useKey))
            {
                useGroupIndexes[useKey] = useGroupIndexes.Count;
                useGroupMembers[useKey] = new HashSet<string>(StringComparer.Ordinal);
            }
            foreach (var member in group.Members)
            {
                useGroupMembers[useKey].Add(member.Action.RuntimeId != 0
                    ? "runtime:" + member.Action.RuntimeId
                    : "candidate:" + member.Action.CandidateId);
            }
        }
        for (var i = 0; i < legal.Count; i++)
        {
            var action = legal[i].Action;
            var useKey = CandidateUseEquivalenceKey(action);
            result.Add(new SearchAction
            {
                Action = action,
                Evaluation = legal[i],
                Model = CombatForwardModel.Resolve(state, action, useRuntimeRegistries),
                Prior = priors[i],
                UseGroupIndex = useGroupIndexes[useKey],
                UseLimit = Math.Max(1, useGroupMembers[useKey].Count),
                MemberCandidateIds = groups[i].Members
                    .Select(member => member.Action.CandidateId)
                    .ToHashSet(StringComparer.Ordinal)
            });
        }
        return result;
    }

    private static string CandidateEquivalenceKey(
        CombatActionObservation action)
    {
        var builder = new StringBuilder(384);
        builder.Append((int)action.Kind).Append('|')
            .Append(action.SourceId).Append('|')
            .Append((int)action.TargetKind).Append('|')
            .Append(action.TargetRuntimeId).Append('|')
            .Append(action.Cost).Append('|');
        foreach (var feature in action.Features
                     .Where(pair => !string.Equals(
                         pair.Key,
                         "handIndex",
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(feature.Key).Append('=')
                .Append(feature.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append(';');
        }
        var semantics = action.Semantics ?? new CombatActionSemantics();
        builder.Append("|set=")
            .Append(semantics.EnergySetAmount?.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
            .Append(',')
            .Append(semantics.EnergyMinimum?.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
            .Append(',')
            .Append(semantics.RestoreEnergyToMaximum ? '1' : '0');
        foreach (var retrieval in semantics.CardRetrievals)
        {
            builder.Append("|get=")
                .Append((int)retrieval.SourceZone).Append(',')
                .Append((int)retrieval.DestinationZone).Append(',')
                .Append(retrieval.Amount).Append(',')
                .Append(retrieval.RequiredCardTag).Append(',')
                .Append(retrieval.CandidateBranchCount);
        }
        foreach (var change in semantics.StateChanges
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("|change=").Append(change.Key).Append(',')
                .Append(change.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private static string CandidateUseEquivalenceKey(
        CombatActionObservation action)
    {
        var builder = new StringBuilder(160);
        builder.Append((int)action.Kind).Append('|')
            .Append(action.SourceId).Append('|')
            .Append(action.Cost);
        var keys = new[]
        {
            "retain",
            "recycle",
            "ouroboros",
            "exhaustOnUse",
            "cardBaseCost",
            "cardTotalExCost",
            "cardExCost",
            "cardOnceExCost",
            "mechanic:card-use-count"
        };
        foreach (var key in keys)
        {
            if (action.Features.TryGetValue(key, out var value))
            {
                builder.Append('|').Append(key).Append('=')
                    .Append(value.ToString(
                        "R",
                        System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return builder.ToString();
    }

    private void ApplyRootExploration(double[] priors)
    {
        if (rootExploration == null
            || priors.Length <= 1
            || rootExploration.RootNoiseFraction <= 0d)
        {
            return;
        }
        var random = new Random(rootExploration.RandomSeed);
        var noise = SampleDirichlet(
            random,
            priors.Length,
            Math.Max(0.03d, rootExploration.RootDirichletAlpha));
        var fraction = Math.Max(
            0d,
            Math.Min(0.75d, rootExploration.RootNoiseFraction));
        for (var index = 0; index < priors.Length; index++)
        {
            priors[index] =
                (1d - fraction) * priors[index]
                + fraction * noise[index];
        }
    }

    private static double[] SampleDirichlet(
        Random random,
        int count,
        double alpha)
    {
        var result = new double[count];
        var total = 0d;
        for (var index = 0; index < count; index++)
        {
            result[index] = SampleGamma(random, alpha);
            total += result[index];
        }
        if (total <= 0d)
        {
            for (var index = 0; index < count; index++)
            {
                result[index] = 1d / count;
            }
            return result;
        }
        for (var index = 0; index < count; index++)
        {
            result[index] /= total;
        }
        return result;
    }

    private static double SampleGamma(Random random, double shape)
    {
        if (shape < 1d)
        {
            var sample = SampleGamma(random, shape + 1d);
            return sample * Math.Pow(
                Math.Max(0.000000001d, random.NextDouble()),
                1d / shape);
        }
        var d = shape - 1d / 3d;
        var c = 1d / Math.Sqrt(9d * d);
        while (true)
        {
            var x = SampleStandardNormal(random);
            var v = 1d + c * x;
            if (v <= 0d)
            {
                continue;
            }
            v *= v * v;
            var u = random.NextDouble();
            if (u < 1d - 0.0331d * x * x * x * x
                || Math.Log(Math.Max(0.000000001d, u))
                   < 0.5d * x * x
                     + d * (1d - v + Math.Log(v)))
            {
                return d * v;
            }
        }
    }

    private static double SampleStandardNormal(Random random)
    {
        var u1 = Math.Max(0.000000001d, random.NextDouble());
        var u2 = random.NextDouble();
        return Math.Sqrt(-2d * Math.Log(u1))
               * Math.Cos(2d * Math.PI * u2);
    }

    private static double NormalizeRuleScore(
        double value,
        double mean,
        double standardDeviation)
    {
        if (double.IsNaN(value)
            || double.IsInfinity(value)
            || double.IsNaN(mean)
            || double.IsInfinity(mean)
            || double.IsNaN(standardDeviation)
            || double.IsInfinity(standardDeviation)
            || standardDeviation < 0.000001d)
        {
            return 0d;
        }
        return Math.Max(
            -3d,
            Math.Min(3d, (value - mean) / standardDeviation));
    }

    private static double ClampFinite(
        double value,
        double minimum,
        double maximum)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Max(minimum, Math.Min(maximum, value));
    }

    private CombatLeafEvaluation EvaluateLeaf(CombatSimulationState state)
    {
        var baseline = state.EvaluateLeaf(profile);
        if (state.AllEnemiesDefeated)
        {
            return baseline;
        }
        CombatSearchFeatureProjector.ProjectLeafInto(
            leafFeatures,
            state,
            profile,
            rootObservation.Features);
        var stateHash = state.Hash();
        if (!policyValueCache.TryGetValue(stateHash, out var network))
        {
            network = ReferenceEquals(
                    policyValueModel,
                    NullCombatPolicyValueModel.Instance)
                ? new CombatPolicyValuePrediction()
                : policyValueModel.Evaluate(PrepareLeafInput());
            policyValueCache[stateHash] = network;
        }
        return new CombatLeafEvaluation
        {
            Value = baseline.Value
                    + guidanceModel.LeafValue(leafFeatures)
                    + network.ExpectedReturn * 8d,
            DeathRisk = Math.Max(
                baseline.DeathRisk,
                Math.Max(
                    guidanceModel.DeathRisk(leafFeatures),
                    baseline.DeathRisk
                    + Math.Max(
                        0d,
                        network.DeathProbability - baseline.DeathRisk)
                    * 0.25d))
        };
    }

    private CombatPolicyValueInput PrepareLeafInput()
    {
        leafInput.StateFeatures = leafFeatures;
        leafInput.Candidates.Clear();
        return leafInput;
    }

    private static double NetworkPolicyLogit(
        CombatPolicyValuePrediction prediction,
        string candidateId)
    {
        return prediction.PolicyLogits.TryGetValue(candidateId ?? "", out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? Math.Max(-4d, Math.Min(4d, value))
            : 0d;
    }

    private static double NetworkActionValue(
        CombatPolicyValuePrediction? prediction,
        string candidateId,
        double tailQuantile)
    {
        if (prediction?.ActionReturnQuantiles == null
            || !prediction.ActionReturnQuantiles.TryGetValue(
                candidateId ?? "",
                out var quantiles)
            || quantiles == null
            || quantiles.Count < 4)
        {
            return 0d;
        }
        var ordered = quantiles
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .OrderBy(value => value)
            .ToArray();
        if (ordered.Length < 4)
        {
            return 0d;
        }
        var tailCount = Math.Max(
            1,
            Math.Min(
                ordered.Length,
                (int)Math.Ceiling(
                    ordered.Length * Math.Max(0.05d, Math.Min(0.5d, tailQuantile)))));
        var mean = ordered.Average();
        var lowerTail = ordered.Take(tailCount).Average();
        return ClampFinite((mean * 0.70d + lowerTail * 0.30d) * 8d, -8d, 8d);
    }

    private bool IsUsable(
        CombatSimulationState state,
        SearchAction searchAction)
    {
        if (searchAction.Action.Kind == CombatActionKind.EndTurn)
        {
            return searchAction.Action.Legal && CanEndTurn(state);
        }
        return IsNonEndUsable(state, searchAction);
    }

    private bool IsNonEndUsable(
        CombatSimulationState state,
        SearchAction searchAction)
    {
        if (state.UseCount(searchAction.UseGroupIndex) >= searchAction.UseLimit
            || !state.TargetAlive(searchAction.Action.TargetRuntimeId)
            || CombatForwardModel.EffectiveCost(state, searchAction.Action) > state.Power)
        {
            return false;
        }
        for (var i = 0; i < simulationRules.Length; i++)
        {
            if (!simulationRules[i].IsLegal(state, searchAction.Action, out _))
            {
                return false;
            }
        }
        return true;
    }

    private bool CanEndTurn(CombatSimulationState state)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var candidate = actions[i];
            if (candidate.Action.Kind == CombatActionKind.EndTurn
                || !IsNonEndUsable(state, candidate))
            {
                continue;
            }
            if (CombatEndTurnSafety.IsSafeAlternative(
                    state,
                    candidate.Action,
                    CombatForwardModel.EffectiveCost(
                        state,
                        candidate.Action),
                    profile))
            {
                return false;
            }
        }
        return true;
    }

    private SearchNode NewNode(CombatSimulationState state)
    {
        nodeCount++;
        return new SearchNode { State = state };
    }

    private double RootSelectionValue(SearchEdge edge)
    {
        var estimate = edge.RiskEstimate(profile.TailRiskQuantile);
        return CombatRiskAdjustedSearchValue.Calculate(
            estimate,
            edge.MeanRisk,
            profile);
    }

    private IOrderedEnumerable<SearchEdge> RankRootEdges(
        IReadOnlyList<SearchEdge> rootEdges)
    {
        if (rootEdges.Count == 0)
        {
            return Enumerable.Empty<SearchEdge>()
                .OrderBy(edge => edge.ActionIndex);
        }
        var safe = rootEdges
            .Where(edge => edge.MeanRisk <= profile.DeathRiskLimit)
            .ToList();
        IReadOnlyList<SearchEdge> pool;
        if (safe.Count > 0)
        {
            pool = safe;
        }
        else
        {
            var minimumRisk = rootEdges.Min(edge => edge.MeanRisk);
            pool = rootEdges
                .Where(edge => edge.MeanRisk <= minimumRisk + 0.01d)
                .ToList();
        }
        return pool
            .OrderByDescending(RootSelectionValue)
            .ThenByDescending(edge => edge.Visits)
            .ThenByDescending(edge => edge.Prior)
            .ThenBy(
                edge => actions[edge.ActionIndex].Action.CandidateId,
                StringComparer.Ordinal);
    }

    private List<CombatPlanStep> BuildPrincipalVariation(SearchNode root, SearchEdge first)
    {
        var result = new List<CombatPlanStep>();
        var edge = first;
        var node = root;
        for (var depth = 0; depth < Math.Min(searchMaxPly, 16); depth++)
        {
            var action = actions[edge.ActionIndex].Action;
            var outcome = edge.Outcomes
                .Where(item => item.RepresentativeChild != null)
                .OrderByDescending(item => item.Visits)
                .ThenByDescending(item => item.Outcome.Probability)
                .FirstOrDefault();
            var representativeChild = outcome?.RepresentativeChild;
            result.Add(new CombatPlanStep
            {
                CandidateId = action.CandidateId,
                SourceId = action.SourceId,
                DisplayName = action.DisplayName,
                StepScore = edge.MeanValue,
                CumulativeScore = RootSelectionValue(edge),
                RemainingPower = representativeChild?.State.Power ?? node.State.Power,
                DeathRisk = edge.MeanRisk,
                Visits = edge.Visits
            });
            if (action.Kind == CombatActionKind.EndTurn)
            {
                break;
            }
            if (representativeChild == null)
            {
                break;
            }
            node = representativeChild;
            EnsureEdges(node);
            edge = RankRootEdges(
                    node.Edges.Values
                        .Where(candidate => candidate.Visits > 0
                                            && !candidate.Disabled)
                        .ToList())
                .FirstOrDefault()!;
            if (edge == null)
            {
                break;
            }
        }
        return result;
    }

    private string BuildSummary(
        SearchEdge best,
        IReadOnlyList<CombatPlanStep> steps,
        int simulations)
    {
        return "risk-aware-root-sampling-puct-mpc(simulations="
               + simulations
               + ", nodes="
               + nodeCount
               + ", transpositions="
               + transpositionHits
               + ", rootVisits="
               + best.Visits
               + ", risk="
               + best.MeanRisk.ToString("0.000")
               + ", cvar="
               + best.RiskEstimate(profile.TailRiskQuantile)
                   .EffectiveLowerTailMean.ToString("0.00")
               + ", tailSamples="
               + best.RiskEstimate(profile.TailRiskQuantile).TailSampleCount
               + ", tailConfidence="
               + best.RiskEstimate(profile.TailRiskQuantile)
                   .TailConfidence.ToString("0.00")
               + "); plan="
               + string.Join(" -> ", steps.Select(step => step.DisplayName))
               + "; value="
               + RootSelectionValue(best).ToString("0.00");
    }

    private void PrepareScoreAction(
        CombatActionObservation source,
        int cost)
    {
        scoreAction.CandidateId = source.CandidateId;
        scoreAction.SourceId = source.SourceId;
        scoreAction.DisplayName = source.DisplayName;
        scoreAction.Kind = source.Kind;
        scoreAction.RuntimeId = source.RuntimeId;
        scoreAction.TargetRuntimeId = source.TargetRuntimeId;
        scoreAction.TargetKind = source.TargetKind;
        scoreAction.Cost = cost;
        scoreAction.Legal = source.Legal;
        scoreAction.RejectionReason = source.RejectionReason;
        scoreAction.Semantics = source.Semantics;
        scoreAction.SemanticSource = source.SemanticSource;
        scoreAction.SemanticFidelity = source.SemanticFidelity;
        scoreAction.Features = source.Features;
    }

    private sealed class SearchAction
    {
        public CombatActionObservation Action { get; set; } = null!;

        public CombatCandidateEvaluation Evaluation { get; set; } = null!;

        public CombatActionModel Model { get; set; } = null!;

        public double Prior { get; set; }

        public int UseGroupIndex { get; set; }

        public int UseLimit { get; set; } = 1;

        public HashSet<string> MemberCandidateIds { get; set; } =
            new(StringComparer.Ordinal);
    }

    private static double ContinuationPriorLogit(
        CombatActionObservation action)
    {
        return action.Features.TryGetValue("continuationHint", out var value)
               && value > 0d
            ? 0.45d * Math.Min(1d, value)
            : 0d;
    }

    private sealed class SearchNode
    {
        public CombatSimulationState State { get; set; } = null!;

        public Dictionary<int, SearchEdge> Edges { get; } = new();

        public int Visits { get; set; }

        public double ValueSum { get; set; }

        public double RiskSum { get; set; }
    }

    private sealed class SearchEdge
    {
        public int ActionIndex { get; set; }

        public double Prior { get; set; }

        public double NormalizedPrior { get; set; }

        public double PredictedValue { get; set; }

        private CombatSearchRiskStatistics Statistics { get; } = new();

        public bool Disabled { get; set; }

        public List<SearchOutcome> Outcomes { get; } = new();

        public int Visits => Statistics.Count;

        public double MeanValue => Statistics.Mean;

        public double MeanRisk => Statistics.MeanRisk;

        public void Record(double value, double risk)
        {
            Statistics.Record(value, risk);
        }

        public CombatSearchRiskEstimate RiskEstimate(double quantile)
        {
            return Statistics.Estimate(quantile);
        }

        public double[] ReturnQuantiles(int count)
        {
            return Statistics.Quantiles(count);
        }
    }

    private sealed class SearchOutcome
    {
        public CombatActionOutcome Outcome { get; set; } = null!;

        public int Visits { get; set; }

        private Dictionary<ulong, ChildEvidence> Children { get; } = new();

        public SearchNode? RepresentativeChild => Children.Values
            .OrderByDescending(item => item.Visits)
            .ThenBy(item => item.Hash)
            .Select(item => item.Node)
            .FirstOrDefault();

        public void RecordChild(ulong hash, SearchNode child)
        {
            if (!Children.TryGetValue(hash, out var evidence))
            {
                evidence = new ChildEvidence
                {
                    Hash = hash,
                    Node = child
                };
                Children[hash] = evidence;
            }
            evidence.Visits++;
        }
    }

    private sealed class ChildEvidence
    {
        public ulong Hash { get; set; }

        public SearchNode Node { get; set; } = null!;

        public int Visits { get; set; }
    }
}
