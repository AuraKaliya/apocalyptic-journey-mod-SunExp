using System;
using System.Collections.Generic;
using System.Linq;
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

    public string BudgetTier { get; set; } = "";

    public string BudgetReason { get; set; } = "";

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }
}

public sealed class CombatChancePuctPlanner
{
    private readonly IDecisionResidualModel residualModel;
    private readonly ICombatSearchGuidanceModel guidanceModel;
    private readonly ICombatPolicyValueModel policyValueModel;
    private readonly bool useRuntimeRegistries;
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

    public CombatChancePuctPlanner(
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        bool useRuntimeRegistries = true,
        ICombatPolicyValueModel? policyValueModel = null)
    {
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
        this.guidanceModel = guidanceModel ?? NullCombatSearchGuidanceModel.Instance;
        this.policyValueModel = policyValueModel ?? NullCombatPolicyValueModel.Instance;
        this.useRuntimeRegistries = useRuntimeRegistries;
    }

    public CombatSearchResult Choose(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile selectedProfile)
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
        determinizationIndex = 0;
        rootBelief = CombatBeliefTracker.FromObservation(state);
        actions = BuildActions(state, candidates);
        var budget = CombatSearchBudgetPolicy.Resolve(
            state,
            candidates,
            selectedProfile);
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
            : Array.Empty<ICombatSimulationRule>();
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
        while (simulations < simulationBudget && nodeCount < nodeBudget)
        {
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
                BudgetReason = budget.Reason
            };
        }

        var safe = rootEdges.Where(edge => edge.MeanRisk <= profile.DeathRiskLimit).ToList();
        var selectionPool = safe.Count > 0 ? safe : rootEdges;
        var best = selectionPool
            .OrderByDescending(edge => edge.LowerTailCvar(profile.TailRiskQuantile))
            .ThenByDescending(RootSelectionValue)
            .ThenByDescending(edge => edge.Visits)
            .ThenByDescending(edge => edge.Prior)
            .First();
        for (var i = 0; i < candidates.Count; i++)
        {
            var matching = rootEdges.FirstOrDefault(edge =>
                string.Equals(
                    actions[edge.ActionIndex].Action.CandidateId,
                    candidates[i].Action.CandidateId,
                    StringComparison.Ordinal));
            candidates[i].PlanScore = matching == null ? 0d : RootSelectionValue(matching);
            candidates[i].SearchPrior = matching?.Prior ?? 0d;
            candidates[i].SearchVisits = matching?.Visits ?? 0;
            candidates[i].SearchDeathRisk = matching?.MeanRisk ?? 0d;
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
            BudgetTier = budget.Tier,
            BudgetReason = budget.Reason,
            CertifiedLoops = certifiedLoops,
            SustainableControlLoops = sustainableControlLoops,
            FakeLoops = fakeLoops,
            BlockedLoops = blockedLoops,
            Summary = BuildSummary(best, steps, simulations)
                      + (stoppedEarly ? "; early-stop=stable" : "")
                      + BuildLoopSummary()
        };
    }

    private int StableBestAction(SearchNode root)
    {
        var rootEdges = root.Edges.Values
            .Where(edge => edge.ActionIndex >= 0 && edge.Visits > 0)
            .ToList();
        if (rootEdges.Count != actions.Count)
        {
            return -1;
        }
        var safe = rootEdges.Where(edge => edge.MeanRisk <= profile.DeathRiskLimit).ToList();
        var pool = safe.Count > 0 ? safe : rootEdges;
        return pool
            .OrderByDescending(edge => edge.LowerTailCvar(profile.TailRiskQuantile))
            .ThenByDescending(RootSelectionValue)
            .ThenByDescending(edge => edge.Visits)
            .ThenByDescending(edge => edge.Prior)
            .First()
            .ActionIndex;
    }

    private bool RootLeadIsStable(
        SearchNode root,
        int bestActionIndex,
        int stabilityWindow)
    {
        if (!root.Edges.TryGetValue(bestActionIndex, out var best)
            || best.Visits <= 0)
        {
            return false;
        }
        var secondVisits = 0;
        var secondValue = double.NegativeInfinity;
        foreach (var candidate in root.Edges.Values)
        {
            if (candidate.ActionIndex == bestActionIndex
                || candidate.Disabled
                || candidate.Visits <= 0)
            {
                continue;
            }
            secondVisits = Math.Max(secondVisits, candidate.Visits);
            secondValue = Math.Max(secondValue, RootSelectionValue(candidate));
        }
        var requiredVisitLead = Math.Max(4, stabilityWindow / 4);
        return best.Visits - secondVisits >= requiredVisitLead
               && RootSelectionValue(best) + 0.0000001d >= secondValue;
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

            outcome.Child = child;
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
                    Prior = actions[i].Prior
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
                ? 0d
                : edge.MeanValue * 0.65d
                  + edge.LowerTailCvar(profile.TailRiskQuantile) * 0.35d
                  - profile.TailRiskPenalty * edge.MeanRisk;
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
        if (legal.Count == 0)
        {
            return Array.Empty<SearchAction>();
        }

        var networkPrediction = ReferenceEquals(
                policyValueModel,
                NullCombatPolicyValueModel.Instance)
            ? new CombatPolicyValuePrediction()
            : policyValueModel.Evaluate(
                CombatPolicyValueEncoding.BuildInput(state, legal));
        var logits = legal
            .Select(candidate => candidate.RuleScore
                                 + guidanceModel.PolicyLogit(candidate.Action.Features)
                                 + NetworkPolicyLogit(
                                     networkPrediction,
                                     candidate.Action.CandidateId) * 0.35d)
            .Select(value => Math.Max(-30d, Math.Min(30d, value)))
            .ToArray();
        var maximum = logits.Max();
        var unnormalized = legal
            .Select((candidate, index) => Math.Exp(logits[index] - maximum))
            .ToArray();
        var total = Math.Max(0.000001d, unnormalized.Sum());
        var result = new List<SearchAction>(legal.Count);
        var useGroups = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < legal.Count; i++)
        {
            var action = legal[i].Action;
            var useKey = action.RuntimeId != 0
                ? "runtime:" + action.RuntimeId
                : "candidate:" + action.CandidateId;
            if (!useGroups.TryGetValue(useKey, out var useGroupIndex))
            {
                useGroupIndex = useGroups.Count;
                useGroups[useKey] = useGroupIndex;
            }
            result.Add(new SearchAction
            {
                Action = action,
                Evaluation = legal[i],
                Model = CombatForwardModel.Resolve(state, action, useRuntimeRegistries),
                Prior = unnormalized[i] / total,
                UseGroupIndex = useGroupIndex
            });
        }
        return result;
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
            ? value
            : 0d;
    }

    private bool IsUsable(
        CombatSimulationState state,
        SearchAction searchAction)
    {
        if (searchAction.Action.Kind == CombatActionKind.EndTurn)
        {
            return searchAction.Action.Legal;
        }
        if (state.WasUsed(searchAction.UseGroupIndex)
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

    private SearchNode NewNode(CombatSimulationState state)
    {
        nodeCount++;
        return new SearchNode { State = state };
    }

    private double RootSelectionValue(SearchEdge edge)
    {
        return edge.MeanValue * 0.65d
               + edge.LowerTailCvar(profile.TailRiskQuantile) * 0.35d
               - profile.TailRiskPenalty * edge.MeanRisk;
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
                .Where(item => item.Child != null)
                .OrderByDescending(item => item.Visits)
                .ThenByDescending(item => item.Outcome.Probability)
                .FirstOrDefault();
            result.Add(new CombatPlanStep
            {
                CandidateId = action.CandidateId,
                SourceId = action.SourceId,
                DisplayName = action.DisplayName,
                StepScore = edge.MeanValue,
                CumulativeScore = RootSelectionValue(edge),
                RemainingPower = outcome?.Child?.State.Power ?? node.State.Power,
                DeathRisk = edge.MeanRisk,
                Visits = edge.Visits
            });
            if (action.Kind == CombatActionKind.EndTurn)
            {
                break;
            }
            if (outcome?.Child == null)
            {
                break;
            }
            node = outcome.Child;
            EnsureEdges(node);
            edge = node.Edges.Values
                .Where(candidate => candidate.Visits > 0 && !candidate.Disabled)
                .OrderByDescending(candidate =>
                    candidate.LowerTailCvar(profile.TailRiskQuantile))
                .ThenByDescending(candidate =>
                    candidate.MeanValue
                    - profile.TailRiskPenalty * candidate.MeanRisk)
                .ThenByDescending(candidate => candidate.Visits)
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
        return "root-sampling-chance-puct-mpc(simulations="
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
               + best.LowerTailCvar(profile.TailRiskQuantile).ToString("0.00")
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
        public CombatActionObservation Action { get; set; } = new();

        public CombatCandidateEvaluation Evaluation { get; set; } = new();

        public CombatActionModel Model { get; set; } = new();

        public double Prior { get; set; }

        public int UseGroupIndex { get; set; }
    }

    private sealed class SearchNode
    {
        public CombatSimulationState State { get; set; } = new();

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

        public int Visits { get; set; }

        public double ValueSum { get; set; }

        public double RiskSum { get; set; }

        public bool Disabled { get; set; }

        public List<SearchOutcome> Outcomes { get; } = new();

        private List<double> ReturnSamples { get; } = new();

        public double MeanValue => Visits <= 0 ? 0d : ValueSum / Visits;

        public double MeanRisk => Visits <= 0 ? 1d : RiskSum / Visits;

        public void Record(double value, double risk)
        {
            Visits++;
            ValueSum += value;
            RiskSum += risk;
            if (ReturnSamples.Count < 2048)
            {
                ReturnSamples.Add(value);
            }
            else
            {
                ReturnSamples[Visits % ReturnSamples.Count] = value;
            }
        }

        public double LowerTailCvar(double quantile)
        {
            if (ReturnSamples.Count == 0)
            {
                return MeanValue;
            }
            var normalized = Math.Max(0.01d, Math.Min(1d, quantile));
            var count = Math.Max(
                1,
                (int)Math.Ceiling(ReturnSamples.Count * normalized));
            return ReturnSamples
                .OrderBy(value => value)
                .Take(count)
                .Average();
        }
    }

    private sealed class SearchOutcome
    {
        public CombatActionOutcome Outcome { get; set; } = new();

        public SearchNode? Child { get; set; }

        public int Visits { get; set; }
    }
}
