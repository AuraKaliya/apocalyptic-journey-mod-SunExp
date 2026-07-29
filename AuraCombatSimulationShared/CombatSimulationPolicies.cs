using System;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public sealed class FirstLegalCombatSimulationPolicy : ICombatSimulationPolicy
{
    public static readonly FirstLegalCombatSimulationPolicy Instance = new();

    public string PolicyId => "first-legal";

    public CombatSimulationAction? SelectAction(CombatSimulationPolicyContext context)
    {
        return context.LegalActions.FirstOrDefault(action =>
                   (action.Kind is CombatSimulationActionKind.PlayCard
                       or CombatSimulationActionKind.UseSkill)
                   && context.State.FindCard(action.CardInstanceId)
                       ?.IsVisibleFake != true)
               ?? context.LegalActions.FirstOrDefault(action =>
                   action.Kind == CombatSimulationActionKind.EndTurn);
    }
}

public sealed class GreedyCombatSimulationPolicy : ICombatSimulationPolicy
{
    public string PolicyId => "greedy-authoritative-semantics";

    public CombatSimulationAction? SelectAction(CombatSimulationPolicyContext context)
    {
        CombatSimulationAction? best = null;
        var bestScore = 0d;
        foreach (var action in context.LegalActions)
        {
            if (action.Kind == CombatSimulationActionKind.EndTurn
                || context.State.FindCard(action.CardInstanceId)
                    ?.IsVisibleFake == true
                || !context.Ruleset.TryGetCard(action.DefinitionId, out var card))
            {
                continue;
            }
            var score = card.Effects.Sum(effect => Score(effect, context.State, action))
                        - action.Cost * 0.5d;
            if (best == null || score > bestScore)
            {
                best = action;
                bestScore = score;
            }
        }
        return bestScore > 0d
            ? best
            : context.LegalActions.FirstOrDefault(action =>
                action.Kind == CombatSimulationActionKind.EndTurn);
    }

    private static double Score(
        CombatSimulationEffectDefinition effect,
        CombatBattleState state,
        CombatSimulationAction action)
    {
        var probability = Math.Max(0d, Math.Min(1d, effect.Probability));
        switch (effect.Kind)
        {
            case CombatSimulationEffectKind.Damage:
            case CombatSimulationEffectKind.TrueDamage:
            {
                var target = state.FindActor(action.TargetActorId);
                return Math.Min(effect.Amount, target?.Hp ?? effect.Amount) * probability;
            }
            case CombatSimulationEffectKind.GainBlock:
                return effect.Amount * 0.8d * probability;
            case CombatSimulationEffectKind.Heal:
                var player = state.Player;
                return Math.Min(effect.Amount, Math.Max(0, (player?.MaxHp ?? 0) - (player?.Hp ?? 0)))
                       * probability;
            case CombatSimulationEffectKind.Draw:
                return effect.Amount * 1.1d * probability;
            case CombatSimulationEffectKind.GainEnergy:
                return effect.Amount * 1.4d * probability;
            case CombatSimulationEffectKind.AddStatus:
                return Math.Max(1, effect.Amount) * probability;
            case CombatSimulationEffectKind.CreateCard:
                return 0.75d * probability;
            default:
                return 0d;
        }
    }
}

public interface ICombatSimulationPolicyFactory
{
    string PolicyId { get; }

    ICombatSimulationPolicy Create();
}

public sealed class GreedyCombatSimulationPolicyFactory : ICombatSimulationPolicyFactory
{
    public string PolicyId => "greedy-authoritative-semantics";

    public ICombatSimulationPolicy Create()
    {
        return new GreedyCombatSimulationPolicy();
    }
}
