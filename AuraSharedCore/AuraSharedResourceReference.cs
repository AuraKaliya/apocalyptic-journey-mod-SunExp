using System;
using System.Collections.Generic;

namespace AuraShared.Core;

public sealed class AuraSharedResourceAlias
{
    public AuraSharedResourceAlias(string firstPrefix, string secondPrefix)
    {
        FirstPrefix = Normalize(firstPrefix);
        SecondPrefix = Normalize(secondPrefix);
    }

    public string FirstPrefix { get; }

    public string SecondPrefix { get; }

    private static string Normalize(string value)
    {
        return (value ?? "").Trim().Trim('"').Replace('\\', '/');
    }
}

public static class AuraSharedResourceReference
{
    public static IReadOnlyList<string> BuildCandidates(
        string? declaredResource,
        params AuraSharedResourceAlias[] aliases)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declared = Normalize(declaredResource);
        Add(candidates, seen, declared);

        foreach (var alias in aliases ?? Array.Empty<AuraSharedResourceAlias>())
        {
            if (alias == null)
            {
                continue;
            }

            AddAliasCandidate(candidates, seen, declared, alias.FirstPrefix, alias.SecondPrefix);
            AddAliasCandidate(candidates, seen, declared, alias.SecondPrefix, alias.FirstPrefix);
        }

        return candidates;
    }

    private static void AddAliasCandidate(
        ICollection<string> candidates,
        ISet<string> seen,
        string declared,
        string sourcePrefix,
        string targetPrefix)
    {
        if (sourcePrefix.Length == 0
            || !declared.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Add(candidates, seen, targetPrefix + declared.Substring(sourcePrefix.Length));
    }

    private static void Add(ICollection<string> candidates, ISet<string> seen, string value)
    {
        if (value.Length > 0 && seen.Add(value))
        {
            candidates.Add(value);
        }
    }

    private static string Normalize(string? value)
    {
        return (value ?? "").Trim().Trim('"').Replace('\\', '/');
    }
}
