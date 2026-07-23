using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraDecision.Shared;

public static class MultiSelectPlanner
{
    public static IReadOnlyList<int> ChooseIndices(
        IReadOnlyList<DecisionUtility> candidates,
        int count,
        DecisionWeights? weights = null,
        bool preferLowest = false)
    {
        if (candidates == null || candidates.Count == 0 || count <= 0)
        {
            return Array.Empty<int>();
        }

        var scorer = weights ?? new DecisionWeights();
        var ranked = Enumerable.Range(0, candidates.Count)
            .Select(index => new
            {
                Index = index,
                Score = scorer.Score(candidates[index] ?? new DecisionUtility())
            });
        ranked = preferLowest
            ? ranked.OrderBy(item => item.Score).ThenBy(item => item.Index)
            : ranked.OrderByDescending(item => item.Score).ThenBy(item => item.Index);
        return ranked
            .Take(Math.Min(count, candidates.Count))
            .Select(item => item.Index)
            .ToArray();
    }
}
