using System;
using System.Collections.Generic;

namespace AuraDecision.Shared;

public sealed class DecisionGraphEvaluation
{
    public bool Rejected { get; set; }

    public string TerminalNodeId { get; set; } = "";

    public DecisionUtility UtilityDelta { get; set; } = new();
}

public static class DecisionGraphEvaluator
{
    private const int MaxSteps = 128;

    public static DecisionGraphEvaluation Evaluate(
        DecisionGraph? graph,
        IReadOnlyDictionary<string, double> features)
    {
        var result = new DecisionGraphEvaluation();
        EvaluateInto(graph, features, result);
        return result;
    }

    public static void EvaluateInto(
        DecisionGraph? graph,
        IReadOnlyDictionary<string, double> features,
        DecisionGraphEvaluation result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.Rejected = false;
        result.TerminalNodeId = "";
        Reset(result.UtilityDelta);
        if (graph == null || graph.Nodes == null || graph.Nodes.Count == 0)
        {
            return;
        }

        var currentId = graph.RootNodeId;
        for (var step = 0; step < MaxSteps && !string.IsNullOrWhiteSpace(currentId); step++)
        {
            var node = FindLastNode(graph.Nodes, currentId);
            if (node == null)
            {
                break;
            }

            result.UtilityDelta.Add(node.UtilityDelta);
            if (node.Reject)
            {
                result.Rejected = true;
                result.TerminalNodeId = node.Id;
                return;
            }

            if (node.Terminal)
            {
                result.TerminalNodeId = node.Id;
                return;
            }

            currentId = Matches(node.Condition, features)
                ? node.TrueNodeId
                : node.FalseNodeId;
        }

        result.TerminalNodeId = currentId ?? "";
    }

    private static DecisionGraphNode? FindLastNode(
        IReadOnlyList<DecisionGraphNode> nodes,
        string id)
    {
        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            var node = nodes[index];
            if (node != null
                && !string.IsNullOrWhiteSpace(node.Id)
                && string.Equals(
                    node.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }
        return null;
    }

    private static void Reset(DecisionUtility utility)
    {
        utility.Survival = 0d;
        utility.Lethal = 0d;
        utility.Tempo = 0d;
        utility.Resource = 0d;
        utility.DeckEconomy = 0d;
        utility.Scaling = 0d;
        utility.Synergy = 0d;
        utility.Continuation = 0d;
        utility.Risk = 0d;
        utility.Uncertainty = 0d;
        utility.Coordination = 0d;
    }

    private static bool Matches(
        DecisionCondition? condition,
        IReadOnlyDictionary<string, double> features)
    {
        if (condition == null || condition.Comparison == DecisionComparison.Always)
        {
            return true;
        }

        var actual = 0d;
        if (!string.IsNullOrWhiteSpace(condition.Feature))
        {
            features.TryGetValue(condition.Feature, out actual);
        }

        return condition.Comparison switch
        {
            DecisionComparison.Equal => Math.Abs(actual - condition.Value) < 0.000001d,
            DecisionComparison.NotEqual => Math.Abs(actual - condition.Value) >= 0.000001d,
            DecisionComparison.GreaterThan => actual > condition.Value,
            DecisionComparison.GreaterThanOrEqual => actual >= condition.Value,
            DecisionComparison.LessThan => actual < condition.Value,
            DecisionComparison.LessThanOrEqual => actual <= condition.Value,
            _ => true
        };
    }
}

public sealed class DecisionEngine<TAction>
{
    public DecisionWeights Weights { get; set; } = new();

    public DecisionGraph? Graph { get; set; }

    public IDecisionResidualModel ResidualModel { get; set; } = NullDecisionResidualModel.Instance;

    public DecisionResult<TAction> Choose(IReadOnlyList<DecisionCandidate<TAction>> candidates)
    {
        var result = new DecisionResult<TAction>
        {
            Reason = "no legal candidate"
        };
        if (candidates == null || candidates.Count == 0)
        {
            return result;
        }

        var bestScore = double.NegativeInfinity;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate == null || !candidate.Legal)
            {
                continue;
            }

            var utility = candidate.Utility?.Clone() ?? new DecisionUtility();
            var graphResult = DecisionGraphEvaluator.Evaluate(Graph, candidate.Features);
            if (graphResult.Rejected)
            {
                continue;
            }

            utility.Add(graphResult.UtilityDelta);
            var score = Weights.Score(utility) + ResidualModel.Predict(candidate.Features);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            result.HasAction = true;
            result.Action = candidate.Action;
            result.CandidateId = candidate.Id;
            result.Score = score;
            result.Reason = string.IsNullOrWhiteSpace(graphResult.TerminalNodeId)
                ? "weighted utility"
                : "decision graph: " + graphResult.TerminalNodeId;
        }

        return result;
    }
}
