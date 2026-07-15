using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.CardRefresh;

public static class CardRefreshPoolPolicy
{
    public static List<T> PreferDifferentChoices<T>(
        IEnumerable<T>? candidates,
        IEnumerable<string>? currentIds,
        int choiceCount,
        Func<T, string> idSelector)
    {
        var all = new List<T>();
        if (candidates != null)
        {
            foreach (var candidate in candidates)
            {
                if (candidate != null && !string.IsNullOrWhiteSpace(idSelector(candidate)))
                {
                    all.Add(candidate);
                }
            }
        }

        if (choiceCount <= 0 || all.Count == 0)
        {
            return all;
        }

        var current = new HashSet<string>(currentIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (current.Count == 0)
        {
            return all;
        }

        var alternatives = new List<T>(all.Count);
        foreach (var candidate in all)
        {
            if (!current.Contains(idSelector(candidate)))
            {
                alternatives.Add(candidate);
            }
        }

        return alternatives.Count >= choiceCount ? alternatives : all;
    }
}
