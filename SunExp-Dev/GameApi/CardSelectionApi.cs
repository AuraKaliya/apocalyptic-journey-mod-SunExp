using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class CardSelectionApi
{
    public static bool SelectOneFromRoleDeck(
        ScriptExecutor self,
        Func<IDataConfig, bool> predicate,
        Action<IDataConfig> onSelected,
        string caption)
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
}
