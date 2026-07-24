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
        return Finite(EvaluateCore(expression, source, target, ruleset));
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
            case CombatSimulationValueOperation.SourceHp:
                return source?.Hp ?? 0d;
            case CombatSimulationValueOperation.TargetHp:
                return target?.Hp ?? 0d;
            case CombatSimulationValueOperation.SourceMaxHp:
                return source?.MaxHp ?? 0d;
            case CombatSimulationValueOperation.TargetMaxHp:
                return target?.MaxHp ?? 0d;
        }

        var values = expression.Arguments
            .Select(argument => EvaluateCore(argument, source, target, ruleset))
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

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
