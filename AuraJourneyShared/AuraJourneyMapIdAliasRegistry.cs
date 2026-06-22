using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraJourney.Shared;

public static class AuraJourneyMapIdAliasRegistry
{
    private static readonly object Gate = new();
    private static readonly List<AuraJourneyMapIdPrefixAlias> PrefixAliases = new();

    public static void RegisterPrefixAlias(string ruleId, string sourcePrefix, string targetPrefix)
    {
        var rule = new AuraJourneyMapIdPrefixAlias
        {
            RuleId = (ruleId ?? "").Trim(),
            SourcePrefix = sourcePrefix ?? "",
            TargetPrefix = targetPrefix ?? ""
        };
        if (string.IsNullOrWhiteSpace(rule.RuleId) || string.IsNullOrWhiteSpace(rule.SourcePrefix))
        {
            return;
        }

        lock (Gate)
        {
            PrefixAliases.RemoveAll(existing => string.Equals(existing.RuleId, rule.RuleId, StringComparison.OrdinalIgnoreCase));
            PrefixAliases.Add(rule);
        }
    }

    public static IReadOnlyList<string> Expand(string mapId)
    {
        var normalized = (mapId ?? "").Trim();
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        List<AuraJourneyMapIdPrefixAlias> aliases;
        lock (Gate)
        {
            aliases = PrefixAliases.ToList();
        }

        var values = new List<string> { normalized };
        var unstarred = normalized.StartsWith("*", StringComparison.Ordinal) ? normalized.Substring(1) : normalized;
        if (!string.Equals(unstarred, normalized, StringComparison.Ordinal))
        {
            values.Add(unstarred);
        }

        foreach (var alias in aliases)
        {
            AddAlias(values, normalized, alias);
            if (!string.Equals(unstarred, normalized, StringComparison.Ordinal))
            {
                AddAlias(values, unstarred, alias);
            }
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddAlias(List<string> values, string value, AuraJourneyMapIdPrefixAlias alias)
    {
        if (value.StartsWith(alias.SourcePrefix, StringComparison.Ordinal))
        {
            values.Add(alias.TargetPrefix + value.Substring(alias.SourcePrefix.Length));
        }
    }
}

public sealed class AuraJourneyMapIdPrefixAlias
{
    public string RuleId { get; set; } = "";

    public string SourcePrefix { get; set; } = "";

    public string TargetPrefix { get; set; } = "";
}
