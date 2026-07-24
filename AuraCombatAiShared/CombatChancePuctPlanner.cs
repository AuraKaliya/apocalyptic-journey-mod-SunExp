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
}

public sealed class CombatChancePuctPlanner
{
    private readonly IDecisionResidualModel residualModel;
    private readonly ICombatSearchGuidanceModel guidanceModel;
    private readonly bool useRuntimeRegistries;
    private readonly Dictionary<ulong, SearchNode> transpositions = new();
    private IReadOnlyList<SearchAction> actions = Array.Empty<SearchAction>();
    private CombatStateObservation rootObservation = new();
    private CombatDecisionProfile profile = new();
    private int nodeBudget;
    private int nodeCount;
    private int transpositionHits;
    private ICombatSimulationRule[] simulationRules = Array.Empty<ICombatSimulationRule>();

    public CombatChancePuctPlanner(
        IDecisionResidualModel? residualModel = null,
        ICombatSearchGuidanceModel? guidanceModel = null,
        bool useRuntimeRegistries = true)
    {
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
        this.guidanceModel = guidanceModel ?? NullCombatSearchGuidanceModel.Instance;
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
        nodeCount = 0;
        transpositionHits = 0;
        simulationRules = useRuntimeRegistries
            ? CombatAiRegistry.SnapshotSimulationRules()
            : Array.Empty<ICombatSimulationRule>();
        nodeBudget = Math.Max(256, Math.Min(65536, profile.SearchNodeBudget));
        actions = BuildActions(state, candidates);
        if (actions.Count == 0)
        {
            return new CombatSearchResult { Summary = "no legal search action" };
        }

        var useGroupCount = actions.Count == 0 ? 0 : actions.Max(action => action.UseGroupIndex) + 1;
        var rootState = CombatForwardModel.Create(state, useGroupCount);
        var root = NewNode(rootState);
        EnsureEdges(root, includeStop: false);

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
            Math.Min(20000, profile.SearchSimulationBudget));
        while (simulations < simulationBudget && nodeCount < nodeBudget)
        {
            Simulate(root, null);
            simulations++;
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
                TranspositionHits = transpositionHits
            };
        }

        var safe = rootEdges.Where(edge => edge.MeanRisk <= profile.DeathRiskLimit).ToList();
        var selectionPool = safe.Count > 0 ? safe : rootEdges;
        var best = selectionPool
            .OrderByDescending(RootSelectionValue)
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
            Summary = BuildSummary(best, steps, simulations)
        };
    }

    private void Simulate(SearchNode root, SearchEdge? forcedRoot)
    {
        var node = root;
        var nodePath = new List<SearchNode>(profile.SearchMaxPly + 1) { root };
        var edgePath = new List<SearchEdge>(profile.SearchMaxPly);
        var value = 0d;
        var risk = 0d;
        var pathReward = 0d;

        for (var ply = 0; ply < Math.Max(1, profile.SearchMaxPly); ply++)
        {
            if (node.State.AllEnemiesDefeated)
            {
                var terminal = EvaluateLeaf(node.State);
                value = pathReward + terminal.Value;
                risk = terminal.DeathRisk;
                break;
            }

            EnsureEdges(node, includeStop: node.State.StepCount > 0);
            var edge = ply == 0 && forcedRoot != null
                ? forcedRoot
                : SelectEdge(node);
            if (edge == null || edge.ActionIndex < 0)
            {
                var leaf = EvaluateLeaf(node.State);
                value = pathReward + leaf.Value;
                risk = leaf.DeathRisk;
                if (edge != null)
                {
                    edgePath.Add(edge);
                }
                break;
            }

            var searchAction = actions[edge.ActionIndex];
            if (!IsUsable(node.State, searchAction))
            {
                edge.Disabled = true;
                ply--;
                continue;
            }

            edgePath.Add(edge);
            var outcome = SelectOutcome(edge);
            var immediate = Score(node.State, searchAction.Action);
            pathReward += immediate * Math.Pow(0.985d, ply);
            var nextState = CombatForwardModel.Apply(
                node.State,
                searchAction.Action,
                searchAction.UseGroupIndex,
                outcome.Outcome,
                profile);
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
                value = pathReward + leaf.Value;
                risk = leaf.DeathRisk;
                break;
            }

            outcome.Child = child;
            outcome.Visits++;
            node = child;
            nodePath.Add(node);
            if (node.Visits == 0)
            {
                var leaf = EvaluateLeaf(node.State);
                value = pathReward + leaf.Value;
                risk = leaf.DeathRisk;
                break;
            }

            if (ply == profile.SearchMaxPly - 1)
            {
                var leaf = EvaluateLeaf(node.State);
                value = pathReward + leaf.Value;
                risk = leaf.DeathRisk;
            }
        }

        for (var i = 0; i < nodePath.Count; i++)
        {
            nodePath[i].Visits++;
            nodePath[i].ValueSum += value;
            nodePath[i].RiskSum += risk;
        }
        for (var i = 0; i < edgePath.Count; i++)
        {
            edgePath[i].Visits++;
            edgePath[i].ValueSum += value;
            edgePath[i].RiskSum += risk;
        }
    }

    private void EnsureEdges(SearchNode node, bool includeStop)
    {
        var priorTotal = 0d;
        for (var i = 0; i < actions.Count; i++)
        {
            if (!IsUsable(node.State, actions[i]))
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

        if (includeStop && !node.Edges.ContainsKey(-1))
        {
            node.Edges[-1] = new SearchEdge
            {
                ActionIndex = -1,
                Prior = Math.Max(0.05d, 1d / Math.Max(2, actions.Count))
            };
            priorTotal += node.Edges[-1].Prior;
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

    private SearchEdge? SelectEdge(SearchNode node)
    {
        SearchEdge? best = null;
        var bestScore = double.NegativeInfinity;
        var parentVisits = Math.Max(1, node.Visits);
        foreach (var edge in node.Edges.Values)
        {
            if (edge.Disabled
                || (edge.ActionIndex >= 0
                    && !IsUsable(node.State, actions[edge.ActionIndex])))
            {
                continue;
            }

            var exploitation = edge.Visits == 0
                ? 0d
                : edge.MeanValue - profile.TailRiskPenalty * edge.MeanRisk;
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
        var action = CloneAction(source, effectiveCost);
        var utility = CombatDecisionEngine.BuildUtility(state, action, profile);
        var features = CombatDecisionEngine.BuildFeatures(state, action, utility, profile);
        var graph = DecisionGraphEvaluator.Evaluate(profile.Graph, features);
        if (graph.Rejected)
        {
            return -1000d;
        }
        utility.Add(graph.UtilityDelta);
        var residual = CombatDecisionEngine.EvaluateResidual(residualModel, features);
        return profile.Weights.Score(utility) + residual.AppliedCorrection;
    }

    private CombatStateObservation ToObservation(CombatSimulationState simulation)
    {
        var threat = new CombatThreatForecast();
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
        return new CombatStateObservation
        {
            BattleSessionId = rootObservation.BattleSessionId,
            Sequence = rootObservation.Sequence,
            Player = new CombatUnitObservation
            {
                RuntimeId = simulation.PlayerRuntimeId,
                Kind = CombatTargetKind.Self,
                CurrentHp = simulation.PlayerHp,
                MaxHp = simulation.PlayerMaxHp,
                Defend = simulation.PlayerDefend
            },
            Enemies = simulation.Enemies.Select(enemy => new CombatUnitObservation
            {
                RuntimeId = enemy.RuntimeId,
                Kind = CombatTargetKind.Enemy,
                CurrentHp = enemy.Hp,
                MaxHp = enemy.MaxHp,
                Defend = enemy.Defend
            }).ToList(),
            CurrentPower = simulation.Power,
            MaxPower = simulation.MaxPower,
            HandCount = simulation.HandCount,
            ExpectedIncomingDamage = threat.ExpectedBlockableDamage
                                     + threat.ExpectedUnblockableDamage
                                     + threat.ExpectedDamageOverTime,
            Threat = threat,
            Features = new Dictionary<string, double>(rootObservation.Features, StringComparer.OrdinalIgnoreCase)
            {
                ["handLimit"] = simulation.HandLimit
            },
            IsPlayerActionWindow = true
        };
    }

    private IReadOnlyList<SearchAction> BuildActions(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates)
    {
        var legal = candidates
            .Where(candidate => candidate.Legal
                                && candidate.Action != null
                                && candidate.Action.Kind != CombatActionKind.EndTurn)
            .ToList();
        if (legal.Count == 0)
        {
            return Array.Empty<SearchAction>();
        }

        var logits = legal
            .Select(candidate => candidate.RuleScore
                                 + guidanceModel.PolicyLogit(candidate.Action.Features))
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
        if (state.AllEnemiesDefeated
            || ReferenceEquals(guidanceModel, NullCombatSearchGuidanceModel.Instance))
        {
            return baseline;
        }
        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["playerHp"] = state.PlayerHp,
            ["playerMaxHp"] = state.PlayerMaxHp,
            ["playerHpRatio"] = state.PlayerMaxHp <= 0
                ? 0d
                : (double)state.PlayerHp / state.PlayerMaxHp,
            ["playerDefend"] = state.PlayerDefend,
            ["power"] = state.Power,
            ["maxPower"] = state.MaxPower,
            ["handCount"] = state.HandCount,
            ["handLimit"] = state.HandLimit,
            ["enemyCount"] = state.Enemies.Count(enemy => enemy.Hp > 0),
            ["enemyHpTotal"] = state.Enemies.Sum(enemy => Math.Max(0, enemy.Hp)),
            ["blockableThreat"] = state.ActiveBlockableThreat(profile.ThreatRiskTolerance),
            ["stepCount"] = state.StepCount,
            ["uncertainty"] = state.Uncertainty
        };
        return new CombatLeafEvaluation
        {
            Value = baseline.Value + guidanceModel.LeafValue(features),
            DeathRisk = Math.Max(baseline.DeathRisk, guidanceModel.DeathRisk(features))
        };
    }

    private bool IsUsable(
        CombatSimulationState state,
        SearchAction searchAction)
    {
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
        return edge.MeanValue - profile.TailRiskPenalty * edge.MeanRisk;
    }

    private List<CombatPlanStep> BuildPrincipalVariation(SearchNode root, SearchEdge first)
    {
        var result = new List<CombatPlanStep>();
        var edge = first;
        var node = root;
        for (var depth = 0; depth < Math.Min(profile.SearchMaxPly, 16); depth++)
        {
            if (edge.ActionIndex < 0)
            {
                break;
            }
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
            if (outcome?.Child == null)
            {
                break;
            }
            node = outcome.Child;
            EnsureEdges(node, includeStop: true);
            edge = node.Edges.Values
                .Where(candidate => candidate.Visits > 0 && !candidate.Disabled)
                .OrderByDescending(candidate =>
                    candidate.MeanValue - profile.TailRiskPenalty * candidate.MeanRisk)
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
        return "chance-puct(simulations="
               + simulations
               + ", nodes="
               + nodeCount
               + ", transpositions="
               + transpositionHits
               + ", rootVisits="
               + best.Visits
               + ", risk="
               + best.MeanRisk.ToString("0.000")
               + "); plan="
               + string.Join(" -> ", steps.Select(step => step.DisplayName))
               + "; value="
               + RootSelectionValue(best).ToString("0.00");
    }

    private static CombatActionObservation CloneAction(
        CombatActionObservation source,
        int cost)
    {
        return new CombatActionObservation
        {
            CandidateId = source.CandidateId,
            SourceId = source.SourceId,
            DisplayName = source.DisplayName,
            Kind = source.Kind,
            RuntimeId = source.RuntimeId,
            TargetRuntimeId = source.TargetRuntimeId,
            TargetKind = source.TargetKind,
            Cost = cost,
            Legal = source.Legal,
            RejectionReason = source.RejectionReason,
            Semantics = source.Semantics,
            Features = new Dictionary<string, double>(source.Features, StringComparer.OrdinalIgnoreCase)
        };
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

        public double MeanValue => Visits <= 0 ? 0d : ValueSum / Visits;

        public double MeanRisk => Visits <= 0 ? 1d : RiskSum / Visits;
    }

    private sealed class SearchOutcome
    {
        public CombatActionOutcome Outcome { get; set; } = new();

        public SearchNode? Child { get; set; }

        public int Visits { get; set; }
    }
}
