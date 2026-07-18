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
        IEnumerable<string>? fallbackCardIds)
    {
        var result = new List<string>();
        Append(configuredCardIds, deckSize, isValid, isExcluded, result);
        Append(fallbackCardIds, deckSize, isValid, isExcluded, result);
        return result;
    }

    private static void Append(
        IEnumerable<string>? candidates,
        int deckSize,
        Func<string, bool> isValid,
        Func<string, bool> isExcluded,
        ICollection<string> result)
    {
        foreach (var cardId in candidates ?? Array.Empty<string>())
        {
            if (result.Count >= deckSize)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(cardId) && isValid(cardId) && !isExcluded(cardId))
            {
                result.Add(cardId);
            }
        }
    }
}
