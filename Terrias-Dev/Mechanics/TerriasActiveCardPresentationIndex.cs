using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class TerriasActiveCardPresentationIndex
{
    private static readonly Dictionary<string, HashSet<CardItem>> CardsById = new(StringComparer.Ordinal);
    private static readonly Dictionary<CardItem, string> IdByCard = new();
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        TerriasCardLifecycleRouter.Register("ActiveCardPresentationIndex", new TerriasCardLifecycleSubscription
        {
            AfterCardItemInit = ObserveFromHook,
            AfterAttackCardItemInit = ObserveFromHook,
            AfterCardItemDataUpdate = ObserveFromHook,
            AfterAttackCardItemDataUpdate = ObserveFromHook
        });
        TerriasBattleLifecycleRouter.Register("ActiveCardPresentationIndex", new TerriasBattleLifecycleSubscription
        {
            BattleInitializing = _ => Clear(),
            BattleSettling = _ => Clear(),
            BattleRestarting = _ => Clear()
        });
    }

    public static void Observe(CardItem? card)
    {
        if (card?.dataConfig == null)
        {
            return;
        }

        var id = LocalId(card.dataConfig);
        if (id.Length == 0)
        {
            return;
        }

        if (IdByCard.TryGetValue(card, out var previousId))
        {
            if (string.Equals(previousId, id, StringComparison.Ordinal))
            {
                return;
            }

            RemoveFromBucket(card, previousId);
        }

        if (!CardsById.TryGetValue(id, out var cards))
        {
            cards = new HashSet<CardItem>();
            CardsById[id] = cards;
        }

        cards.Add(card);
        IdByCard[card] = id;
    }

    public static IReadOnlyList<CardItem> Snapshot(IReadOnlyList<string> cardIds)
    {
        var result = new List<CardItem>();
        var seen = new HashSet<CardItem>();
        if (cardIds == null || cardIds.Count == 0)
        {
            return result;
        }

        if (cardIds.Any(id => id == "*"))
        {
            foreach (var card in FightUI.cardItemList ?? new List<CardItem>())
            {
                if (card != null && card.gameObject != null && seen.Add(card)) result.Add(card);
            }

            return result;
        }

        foreach (var requested in cardIds)
        {
            var id = LocalId(requested);
            if (!CardsById.TryGetValue(id, out var cards))
            {
                continue;
            }

            List<CardItem>? stale = null;
            foreach (var card in cards)
            {
                if (!IsActive(card))
                {
                    stale ??= new List<CardItem>();
                    stale.Add(card);
                    continue;
                }

                if (seen.Add(card)) result.Add(card);
            }

            if (stale != null)
            {
                foreach (var card in stale)
                {
                    cards.Remove(card);
                    IdByCard.Remove(card);
                }
            }
        }

        return result;
    }

    public static bool HasCompleteActiveCardCoverage()
    {
        var cards = FightUI.cardItemList;
        if (cards == null)
        {
            return true;
        }

        foreach (var card in cards)
        {
            if (card == null || card.gameObject == null)
            {
                continue;
            }

            if (card.dataConfig == null || !CardVisualThemeCatalog.IsTerriasCard(card.dataConfig))
            {
                return false;
            }

            var currentId = LocalId(card.dataConfig);
            if (currentId.Length == 0
                || !IdByCard.TryGetValue(card, out var observedId)
                || !string.Equals(observedId, currentId, StringComparison.Ordinal)
                || !CardsById.TryGetValue(currentId, out var indexedCards)
                || !indexedCards.Contains(card))
            {
                return false;
            }
        }

        return true;
    }

    public static void Forget(CardItem? card)
    {
        if (card == null)
        {
            return;
        }

        if (!IdByCard.TryGetValue(card, out var id))
        {
            return;
        }

        IdByCard.Remove(card);
        RemoveFromBucket(card, id);
    }

    public static void Clear()
    {
        CardsById.Clear();
        IdByCard.Clear();
    }

    private static void ObserveFromHook(ModHookContext context)
    {
        Observe(context.Target as CardItem);
    }

    private static bool IsActive(CardItem? card)
    {
        return card != null
               && card.gameObject != null
               && FightUI.cardItemList?.Contains(card) == true
               && IdByCard.ContainsKey(card);
    }

    private static string LocalId(IDataConfig? config)
    {
        return TerriasContentIdCompatibility.LocalId(CardConfigApi.Id(config)).TrimStart('*');
    }

    private static string LocalId(string id)
    {
        return TerriasContentIdCompatibility.LocalId(id).TrimStart('*');
    }

    private static void RemoveFromBucket(CardItem card, string id)
    {
        if (!CardsById.TryGetValue(id, out var cards))
        {
            return;
        }

        cards.Remove(card);
        if (cards.Count == 0)
        {
            CardsById.Remove(id);
        }
    }
}
