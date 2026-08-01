using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public static class CombatSimulationExpressionEvaluator
{
    public static double Evaluate(
        CombatSimulationValueExpression? expression,
        CombatBattleState state,
        CombatRuleset ruleset,
        int sourceActorId,
        int targetActorId)
    {
        if (expression == null)
        {
            return 0d;
        }
        var source = state.FindActor(sourceActorId);
        var target = state.FindActor(targetActorId);
        return Finite(EvaluateCore(expression, state, source, target, ruleset));
    }

    public static double ResolveVariable(
        CombatActorState? actor,
        CombatRuleset ruleset,
        string key,
        double fallback = 0d)
    {
        if (actor == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }
        var value = actor.Variables.TryGetValue(key, out var stored) ? stored : fallback;
        foreach (var status in actor.Statuses)
        {
            if (ruleset.TryGetStatusCore(status.StatusId, out var definition)
                && definition.DynamicModifiersPerStack.TryGetValue(key, out var modifier))
            {
                value += modifier * status.Stacks;
            }
        }
        return Finite(value);
    }

    private static double EvaluateCore(
        CombatSimulationValueExpression expression,
        CombatBattleState state,
        CombatActorState? source,
        CombatActorState? target,
        CombatRuleset ruleset)
    {
        switch (expression.Operation)
        {
            case CombatSimulationValueOperation.Constant:
                return expression.Constant;
            case CombatSimulationValueOperation.SourceVariable:
                return ResolveVariable(source, ruleset, expression.Key);
            case CombatSimulationValueOperation.TargetVariable:
                return ResolveVariable(target, ruleset, expression.Key);
            case CombatSimulationValueOperation.SourceStatusStacks:
                return Stacks(source, expression.Key);
            case CombatSimulationValueOperation.TargetStatusStacks:
                return Stacks(target, expression.Key);
            case CombatSimulationValueOperation.SourceStatusCounter:
                return StatusCounter(source, expression.Key);
            case CombatSimulationValueOperation.SourceStatusTagStacks:
                return StatusTagStacks(source, ruleset, expression.Key);
            case CombatSimulationValueOperation.SourceHandCount:
                return source?.Kind == CombatSimulationActorKind.Player
                    ? state.Hand.Count
                    : 0d;
            case CombatSimulationValueOperation.SourceHandTagCount:
                return source?.Kind == CombatSimulationActorKind.Player
                    ? state.Hand.Count(instanceId =>
                        state.FindCard(instanceId) is { } instance
                        && ruleset.TryGetCardCore(instance.CardId, out var card)
                        && HasTag(instance, card, expression.Key))
                    : 0d;
            case CombatSimulationValueOperation.PlayerHandCount:
                return state.Hand.Count;
            case CombatSimulationValueOperation.SourceHp:
                return source?.Hp ?? 0d;
            case CombatSimulationValueOperation.TargetHp:
                return target?.Hp ?? 0d;
            case CombatSimulationValueOperation.SourceMaxHp:
                return source?.MaxHp ?? 0d;
            case CombatSimulationValueOperation.TargetMaxHp:
                return target?.MaxHp ?? 0d;
            case CombatSimulationValueOperation.SourceBlock:
                return source?.Block ?? 0d;
            case CombatSimulationValueOperation.TargetBlock:
                return target?.Block ?? 0d;
            case CombatSimulationValueOperation.SourceEnergy:
                return source?.Energy ?? 0d;
            case CombatSimulationValueOperation.SourceMaxEnergy:
                return source?.BaseEnergy ?? 0d;
            case CombatSimulationValueOperation.LivingEnemyCount:
                return state.LivingEnemies.Count();
        }

        var values = expression.Arguments
            .Select(argument => EvaluateCore(argument, state, source, target, ruleset))
            .ToList();
        if (values.Count == 0)
        {
            return 0d;
        }
        switch (expression.Operation)
        {
            case CombatSimulationValueOperation.Add:
                return values.Sum();
            case CombatSimulationValueOperation.Subtract:
                return values.Skip(1).Aggregate(values[0], (current, value) => current - value);
            case CombatSimulationValueOperation.Multiply:
                return values.Aggregate(1d, (current, value) => current * value);
            case CombatSimulationValueOperation.Divide:
                return values.Skip(1).Aggregate(
                    values[0],
                    (current, value) => Math.Abs(value) < 1e-9d ? current : current / value);
            case CombatSimulationValueOperation.Minimum:
                return values.Min();
            case CombatSimulationValueOperation.Maximum:
                return values.Max();
            case CombatSimulationValueOperation.GreaterThan:
                return values.Count >= 2 && values[0] > values[1] ? 1d : 0d;
            case CombatSimulationValueOperation.GreaterThanOrEqual:
                return values.Count >= 2 && values[0] >= values[1] ? 1d : 0d;
            case CombatSimulationValueOperation.LessThan:
                return values.Count >= 2 && values[0] < values[1] ? 1d : 0d;
            case CombatSimulationValueOperation.LessThanOrEqual:
                return values.Count >= 2 && values[0] <= values[1] ? 1d : 0d;
            case CombatSimulationValueOperation.Equal:
                return values.Count >= 2 && Math.Abs(values[0] - values[1]) < 1e-9d
                    ? 1d
                    : 0d;
            case CombatSimulationValueOperation.Conditional:
                return values.Count >= 3
                    ? values[0] > 0d ? values[1] : values[2]
                    : 0d;
            case CombatSimulationValueOperation.Floor:
                return Math.Floor(values[0]);
            case CombatSimulationValueOperation.Ceiling:
                return Math.Ceiling(values[0]);
            default:
                return 0d;
        }
    }

    private static int Stacks(CombatActorState? actor, string statusId)
    {
        return actor?.Statuses
                   .Where(item => string.Equals(
                       item.StatusId,
                       statusId,
                       StringComparison.OrdinalIgnoreCase))
                   .Sum(item => item.Stacks)
               ?? 0;
    }

    private static int StatusCounter(CombatActorState? actor, string key)
    {
        if (actor == null || string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }
        var separator = key.IndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
        {
            return 0;
        }
        var statusId = key.Substring(0, separator);
        var counterKey = key.Substring(separator + 1);
        var status = actor.Statuses.FirstOrDefault(item => string.Equals(
            item.StatusId,
            statusId,
            StringComparison.OrdinalIgnoreCase));
        return status != null
               && status.TriggerCounts.TryGetValue(counterKey, out var value)
            ? value
            : 0;
    }

    private static int StatusTagStacks(
        CombatActorState? actor,
        CombatRuleset ruleset,
        string tag)
    {
        if (actor == null || string.IsNullOrWhiteSpace(tag))
        {
            return 0;
        }
        return actor.Statuses.Sum(status =>
            ruleset.TryGetStatusCore(status.StatusId, out var definition)
            && definition.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)
                ? status.Stacks
                : 0);
    }

    private static bool HasTag(
        CombatCardInstanceState instance,
        CombatCardDefinition definition,
        string tag)
    {
        return instance.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)
               || definition.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
