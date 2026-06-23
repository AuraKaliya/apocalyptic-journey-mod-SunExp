using System;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class CardApi
{
    public static bool AddCardToHand(ScriptExecutor self, string cardId, string runtimeTag = "")
    {
        var resolved = ResolveCardId(cardId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            SunExpLog.Warn("AddCardToHand skipped unknown cardId=" + cardId);
            return false;
        }

        var cards = FightCardManager.Instance?.cardList;
        var previousCount = cards?.Count ?? 0;
        try
        {
            self.AddCardById(resolved);
        }
        catch
        {
            try
            {
                self.AddCardByData(resolved, "");
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("AddCardToHand fallback used: cardId=" + resolved + ", error=" + ex.Message);
                self.AddCard(resolved);
            }
        }

        if (cards == null || cards.Count <= previousCount)
        {
            SunExpLog.Warn("AddCardToHand could not verify added card: cardId=" + resolved);
            return false;
        }

        var added = cards[cards.Count - 1];
        if (!string.IsNullOrWhiteSpace(runtimeTag))
        {
            var tags = DictionaryUtil.Get(added.Vars, "SpecialTag");
            DictionaryUtil.Set(added.Vars, "SpecialTag", AppendToken(tags, runtimeTag));
        }

        return true;
    }

    public static string ResolveCardId(string cardId)
    {
        var id = (cardId ?? "").Replace("*", "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }

        foreach (var candidate in Candidates(id))
        {
            try
            {
                if (Singleton<GameConfigManager>.Instance.GetOne(DataType.Card, candidate) != null)
                {
                    return candidate;
                }
            }
            catch
            {
                // Keep trying fallbacks.
            }
        }

        return id;
    }

    private static string[] Candidates(string id)
    {
        if (id.StartsWith("SunExp_", StringComparison.Ordinal))
        {
            return new[] { id };
        }

        return new[]
        {
            id,
            "SunExp_sunexp_" + id,
            "SunExp_loneer_" + id,
            "SunExp_wuna_" + id
        };
    }

    private static string AppendToken(string value, string token)
    {
        if (DictionaryUtil.ContainsToken(value, token))
        {
            return value;
        }

        return string.IsNullOrWhiteSpace(value) ? token : value + "," + token;
    }
}
