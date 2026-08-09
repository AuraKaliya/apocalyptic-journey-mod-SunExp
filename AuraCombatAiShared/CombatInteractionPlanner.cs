using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatInteractionPlan
{
    public List<int> SelectedIndices { get; set; } = new();

    public List<string> SelectedCandidateIds { get; set; } = new();

    public double Score { get; set; }

    public bool EffectsComplete { get; set; }

    public string Reason { get; set; } = "";
}

/// <summary>
/// Plans the complete native selection, including selection count.  This is a
/// best-response layer shared by live prompts and forward simulation so an
/// optional interaction is never silently treated as an exact-count prompt.
/// </summary>
public static class CombatInteractionPlanner
{
    private const int ExactSubsetLimit = 14;

    public static CombatInteractionPlan Plan(
        CombatInteractionDefinition? interaction,
        IReadOnlyList<CombatActionObservation>? choices,
        Func<CombatActionObservation, double>? choiceScore = null,
        bool preferLowestFallback = false)
    {
        choices ??= Array.Empty<CombatActionObservation>();
        interaction = (interaction ?? new CombatInteractionDefinition
        {
            MinSelections = Math.Min(1, choices.Count),
            MaxSelections = Math.Min(1, choices.Count),
            EffectsComplete = false
        }).Normalize();

        var minimum = Math.Min(choices.Count, interaction.MinSelections);
        var maximum = Math.Min(choices.Count, interaction.MaxSelections);
        if (maximum < minimum) maximum = minimum;
        var result = choices.Count <= ExactSubsetLimit
            ? Exact(interaction, choices, minimum, maximum, choiceScore, preferLowestFallback)
            : Greedy(interaction, choices, minimum, maximum, choiceScore, preferLowestFallback);
        result.EffectsComplete = interaction.EffectsComplete;
        result.SelectedCandidateIds = result.SelectedIndices
            .Where(index => index >= 0 && index < choices.Count)
            .Select(index => choices[index].CandidateId ?? "")
            .ToList();
        return result;
    }

    public static double EstimateSelectionValue(
        CombatInteractionDefinition interaction,
        IReadOnlyList<double> selectedCardValues,
        IReadOnlyList<double>? selectedCosts = null)
    {
        var count = selectedCardValues.Count;
        var cardTotal = selectedCardValues.Sum();
        var costTotal = selectedCosts?.Sum() ?? 0d;
        var score = 0d;
        foreach (var effect in interaction.SelectionEffects)
        {
            switch (effect.Kind)
            {
                case CombatInteractionEffectKind.BurnSelected:
                    score -= cardTotal;
                    break;
                case CombatInteractionEffectKind.DiscardSelected:
                    score -= cardTotal * 0.45d;
                    break;
                case CombatInteractionEffectKind.RetainSelected:
                    score += cardTotal * 0.65d;
                    break;
                case CombatInteractionEffectKind.DuplicateSelected:
                case CombatInteractionEffectKind.TransferSelectedCopy:
                    score += cardTotal;
                    break;
                case CombatInteractionEffectKind.ModifySelectedCost:
                    score += Math.Max(0d, -effect.Amount) * count * 1.8d
                             + costTotal * 0.25d;
                    break;
                case CombatInteractionEffectKind.ModifySelectedPersistentCost:
                    score -= Math.Max(0d, effect.Amount) * count * 1.4d;
                    break;
                case CombatInteractionEffectKind.ModifySelectedExtraUses:
                    score += Math.Max(0d, effect.Amount) * cardTotal * 0.55d;
                    break;
                case CombatInteractionEffectKind.AddStatusPerSelected:
                    score += Math.Max(0d, effect.Amount) * count * 0.8d;
                    break;
                case CombatInteractionEffectKind.AddStatusBySelectionCount:
                    score += (effect.BaseAmount + effect.AmountPerSelection * count) * 0.8d;
                    break;
            }
        }
        if (!interaction.EffectsComplete)
        {
            score -= count * 2d;
        }
        return Finite(score);
    }

    private static CombatInteractionPlan Exact(
        CombatInteractionDefinition interaction,
        IReadOnlyList<CombatActionObservation> choices,
        int minimum,
        int maximum,
        Func<CombatActionObservation, double>? choiceScore,
        bool preferLowestFallback)
    {
        var best = new CombatInteractionPlan
        {
            Score = double.NegativeInfinity,
            Reason = "exact-subset"
        };
        var selected = new List<int>();
        void Visit(int index)
        {
            if (selected.Count > maximum) return;
            if (index == choices.Count)
            {
                if (selected.Count < minimum || selected.Count > maximum) return;
                var score = Score(
                    interaction,
                    choices,
                    selected,
                    choiceScore,
                    preferLowestFallback);
                if (score > best.Score + 1e-9d
                    || (Math.Abs(score - best.Score) <= 1e-9d
                        && selected.Count < best.SelectedIndices.Count))
                {
                    best.Score = score;
                    best.SelectedIndices = new List<int>(selected);
                }
                return;
            }
            Visit(index + 1);
            selected.Add(index);
            Visit(index + 1);
            selected.RemoveAt(selected.Count - 1);
        }
        Visit(0);
        if (double.IsNegativeInfinity(best.Score)) best.Score = 0d;
        return best;
    }

    private static CombatInteractionPlan Greedy(
        CombatInteractionDefinition interaction,
        IReadOnlyList<CombatActionObservation> choices,
        int minimum,
        int maximum,
        Func<CombatActionObservation, double>? choiceScore,
        bool preferLowestFallback)
    {
        var ranked = Enumerable.Range(0, choices.Count)
            .OrderByDescending(index => IndividualScore(
                interaction,
                choices[index],
                choiceScore,
                preferLowestFallback))
            .ThenBy(index => choices[index].CandidateId, StringComparer.Ordinal)
            .ToList();
        var best = new CombatInteractionPlan
        {
            Score = double.NegativeInfinity,
            Reason = "greedy-count-scan"
        };
        for (var count = minimum; count <= maximum; count++)
        {
            var indices = ranked.Take(count).OrderBy(index => index).ToList();
            var score = Score(
                interaction,
                choices,
                indices,
                choiceScore,
                preferLowestFallback);
            if (score > best.Score)
            {
                best.Score = score;
                best.SelectedIndices = indices;
            }
        }
        if (double.IsNegativeInfinity(best.Score)) best.Score = 0d;
        return best;
    }

    private static double Score(
        CombatInteractionDefinition interaction,
        IReadOnlyList<CombatActionObservation> choices,
        IReadOnlyList<int> selected,
        Func<CombatActionObservation, double>? choiceScore,
        bool preferLowestFallback)
    {
        var values = selected.Select(index => SemanticValue(choices[index])).ToList();
        var costs = selected.Select(index => Feature(choices[index], "choice:cost", choices[index].Cost)).ToList();
        var effectValue = EstimateSelectionValue(interaction, values, costs);
        var custom = choiceScore == null
            ? 0d
            : selected.Sum(index => Finite(choiceScore(choices[index])));
        if (interaction.SelectionEffects.Count == 0)
        {
            var fallback = selected.Sum(index => SemanticValue(choices[index]));
            effectValue += preferLowestFallback ? -fallback : fallback;
        }
        return Finite(effectValue + custom);
    }

    private static double IndividualScore(
        CombatInteractionDefinition interaction,
        CombatActionObservation choice,
        Func<CombatActionObservation, double>? choiceScore,
        bool preferLowestFallback)
    {
        var value = SemanticValue(choice);
        var effect = EstimateSelectionValue(
            interaction,
            new[] { value },
            new[] { Feature(choice, "choice:cost", choice.Cost) });
        if (interaction.SelectionEffects.Count == 0)
        {
            effect += preferLowestFallback ? -value : value;
        }
        return effect + (choiceScore == null ? 0d : Finite(choiceScore(choice)));
    }

    private static double SemanticValue(CombatActionObservation choice)
    {
        var semantics = choice.Semantics ?? new CombatActionSemantics();
        return Math.Max(0d,
            semantics.Damage
            + semantics.TrueDamage
            + semantics.Defend * 0.8d
            + semantics.Heal * 0.9d
            + semantics.Draw * 1.5d
            + semantics.EnergyGain * 2d
            + semantics.Scaling
            + semantics.DeckValue
            + semantics.PersistentValue
            + semantics.CardGeneration
            - semantics.Risk
            - semantics.Uncertainty * 0.5d);
    }

    private static double Feature(CombatActionObservation choice, string key, double fallback)
    {
        return choice.Features != null
               && choice.Features.TryGetValue(key, out var value)
            ? Finite(value)
            : fallback;
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
