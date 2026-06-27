using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class CardSelectionApi
{
    public static bool SelectOneFromCards(
        ScriptExecutor self,
        IReadOnlyList<IDataConfig> source,
        Func<IDataConfig, bool> predicate,
        Action<IDataConfig> onSelected,
        string caption,
        Action? onCancelled = null)
    {
        var cards = source?
            .Where(card => card != null && (predicate == null || predicate(card)))
            .ToList() ?? new List<IDataConfig>();
        if (self == null || cards.Count == 0 || onSelected == null)
        {
            return false;
        }

        try
        {
            PlayerApi.ShowCaption(caption);
            self.OutFightSelectCardToAction("1", cards, selected =>
            {
                var card = selected?.FirstOrDefault();
                if (card == null)
                {
                    onCancelled?.Invoke();
                    return;
                }

                onSelected(card);
            });
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Card selection UI failed: " + ex.Message);
            return false;
        }
    }

    public static bool SelectOneFromRoleDeck(
        ScriptExecutor self,
        Func<IDataConfig, bool> predicate,
        Action<IDataConfig> onSelected,
        string caption,
        Action? onCancelled = null)
    {
        var source = RoleDeckCards(predicate);
        if (self == null || source.Count == 0 || onSelected == null)
        {
            return false;
        }

        try
        {
            PlayerApi.ShowCaption(caption);
            self.OutFightSelectCardToAction("1", source, selected =>
            {
                var card = selected?.FirstOrDefault();
                if (card == null)
                {
                    onCancelled?.Invoke();
                    return;
                }

                onSelected(card);
            });
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Card selection UI failed: " + ex.Message);
            return false;
        }
    }

    public static List<IDataConfig> RoleDeckCards(Func<IDataConfig, bool> predicate)
    {
        var result = new List<IDataConfig>();
        try
        {
            var cards = RoleTable.Instance?.cardList;
            if (cards == null)
            {
                return result;
            }

            foreach (var card in cards)
            {
                if (card != null && (predicate == null || predicate(card)))
                {
                    result.Add(card);
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Role deck card collection failed: " + ex.Message);
        }

        return result;
    }

    public static List<IDataConfig> CombatDrawAndDiscardCards(ScriptExecutor? self, Func<IDataConfig, bool> predicate)
    {
        var result = new List<IDataConfig>();
        try
        {
            foreach (var card in self?.DeckCard ?? new List<DataConfig>())
            {
                if (card != null && (predicate == null || predicate(card)))
                {
                    result.Add(card);
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Draw pile card collection failed: " + ex.Message);
        }

        var discardCollected = false;
        try
        {
            foreach (var card in self?.UsedCard ?? new List<DataConfig>())
            {
                if (card != null && (predicate == null || predicate(card)))
                {
                    result.Add(card);
                    discardCollected = true;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Discard pile card collection failed: " + ex.Message);
        }

        if (discardCollected)
        {
            return result;
        }

        try
        {
            var usedCards = FightCardManager.Instance?.usedCardList;
            if (usedCards == null)
            {
                return result;
            }

            foreach (var card in usedCards)
            {
                if (card != null && (predicate == null || predicate(card)))
                {
                    result.Add(card);
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Fallback discard pile card collection failed: " + ex.Message);
        }

        return result;
    }
}
