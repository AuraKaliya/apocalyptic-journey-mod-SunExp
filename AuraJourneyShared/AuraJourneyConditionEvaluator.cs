using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraJourney.Shared;

public static class AuraJourneyConditionEvaluator
{
    public static bool EvaluateAll(IEnumerable<AuraJourneyCondition>? conditions, AuraJourneyConditionContext? context)
    {
        var list = conditions?.Where(condition => condition != null).ToArray() ?? Array.Empty<AuraJourneyCondition>();
        return list.Length == 0 || list.All(condition => Evaluate(condition, context));
    }

    public static bool Evaluate(AuraJourneyCondition condition, AuraJourneyConditionContext? context)
    {
        context ??= new AuraJourneyConditionContext();
        var kind = (condition.Kind ?? AuraJourneyConditionKinds.Always).Trim();
        var key = (condition.Key ?? "").Trim();
        if (string.Equals(kind, AuraJourneyConditionKinds.Always, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.Flag, StringComparison.OrdinalIgnoreCase))
        {
            return context.Flags.TryGetValue(key, out var value) && value;
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.NotFlag, StringComparison.OrdinalIgnoreCase))
        {
            return !context.Flags.TryGetValue(key, out var value) || !value;
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.Equals, StringComparison.OrdinalIgnoreCase))
        {
            return context.Values.TryGetValue(key, out var value)
                   && string.Equals(value, condition.Value ?? "", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.NotEquals, StringComparison.OrdinalIgnoreCase))
        {
            return !context.Values.TryGetValue(key, out var value)
                   || !string.Equals(value, condition.Value ?? "", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.MinCounter, StringComparison.OrdinalIgnoreCase))
        {
            return context.Counters.TryGetValue(key, out var value) && value >= condition.Number;
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.MaxCounter, StringComparison.OrdinalIgnoreCase))
        {
            return !context.Counters.TryGetValue(key, out var value) || value <= condition.Number;
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.AnyRole, StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(context.RoleIds, condition.Values.Count > 0 ? condition.Values : new List<string> { condition.Value });
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.AllRoles, StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAll(context.RoleIds, condition.Values.Count > 0 ? condition.Values : new List<string> { condition.Value });
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.PlayerCountAtLeast, StringComparison.OrdinalIgnoreCase))
        {
            return context.PlayerCount >= condition.Number;
        }

        if (string.Equals(kind, AuraJourneyConditionKinds.PlayerCountAtMost, StringComparison.OrdinalIgnoreCase))
        {
            return context.PlayerCount <= condition.Number;
        }

        return false;
    }

    private static bool ContainsAny(IEnumerable<string> source, IEnumerable<string> values)
    {
        var set = new HashSet<string>(source.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
        return values.Any(value => !string.IsNullOrWhiteSpace(value) && set.Contains(value));
    }

    private static bool ContainsAll(IEnumerable<string> source, IEnumerable<string> values)
    {
        var set = new HashSet<string>(source.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).All(set.Contains);
    }
}
