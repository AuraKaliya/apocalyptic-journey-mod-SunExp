using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckDeckBuilder
{
    internal static List<string> Build(
        IEnumerable<string>? configuredCardIds,
        int deckSize,
        Func<string, bool> isValid,
        Func<string, bool> isExcluded,
        IEnumerable<string>? fallbackCardIds,
        Func<string, string>? resolveId = null)
    {
        var result = new List<string>();
        Append(configuredCardIds, deckSize, isValid, isExcluded, result, resolveId);
        Append(fallbackCardIds, deckSize, isValid, isExcluded, result, resolveId);
        return result;
    }

    private static void Append(
        IEnumerable<string>? candidates,
        int deckSize,
        Func<string, bool> isValid,
        Func<string, bool> isExcluded,
        ICollection<string> result,
        Func<string, string>? resolveId)
    {
        foreach (var cardId in candidates ?? Array.Empty<string>())
        {
            if (result.Count >= deckSize)
            {
                return;
            }

            var resolvedId = string.IsNullOrWhiteSpace(cardId)
                ? ""
                : (resolveId?.Invoke(cardId) ?? cardId).Trim();
            if (!string.IsNullOrWhiteSpace(resolvedId)
                && isValid(resolvedId)
                && !isExcluded(resolvedId))
            {
                result.Add(resolvedId);
            }
        }
    }
}
