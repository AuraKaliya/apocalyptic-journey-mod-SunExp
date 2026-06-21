using System;
using System.Linq;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll.GameApi;

public static class CardApi
{
    public static void AddCardToHand(ScriptExecutor self, string cardId)
    {
        AddCardToHand(self, cardId, "");
    }

    public static void AddCardToHand(ScriptExecutor self, string cardId, string addTag)
    {
        var tag = addTag ?? "";
        try
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                self.AddCardById(cardId);
            }
            else
            {
                self.AddCardByData(cardId, tag);
            }
        }
        catch
        {
            try
            {
                self.AddCardByData(cardId, tag);
            }
            catch
            {
                self.AddCard(cardId);
                EnsureNewestCardTag(self, cardId, tag);
            }
        }

        EnsureNewestCardTag(self, cardId, tag);
    }

    public static void CreateCardInHand(ScriptExecutor self, string cardId, string addTag)
    {
        var tag = addTag ?? "";
        try
        {
            var dataConfig = new DataConfig(cardId, DataType.Card);
            AppendConfigTag(dataConfig, tag);
            self.CreateCard(dataConfig);
            EnsureNewestCardTag(self, cardId, tag);
        }
        catch (Exception ex)
        {
            GoldExpLog.Warn("CreateCardInHand fallback used: cardId=" + cardId + ", error=" + ex.Message);
            AddCardToHand(self, cardId, tag);
            self.DrawCount("1");
        }
    }

    public static int DiscardAllHand(ScriptExecutor self)
    {
        var count = self.HandCard?.Count(card => card != null) ?? 0;
        if (count > 0)
        {
            self.ThrowCard(count.ToString(), "0");
        }

        return count;
    }

    public static int EnsureHandTags(ScriptExecutor self, params string[] tags)
    {
        var changed = 0;
        foreach (var card in self.HandCard ?? Enumerable.Empty<CardItem>())
        {
            foreach (var tag in tags)
            {
                if (EnsureCardTag(card, tag))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    public static int RefreshUsableByLocalId(ScriptExecutor self, string localId, bool usable)
    {
        var changed = 0;
        var value = usable ? "1" : "0";
        foreach (var card in self.HandCard ?? Enumerable.Empty<CardItem>())
        {
            if (card == null || !CardMatchesId(card, localId))
            {
                continue;
            }

            DictionaryUtil.Set(card.Vars, "Usable", value);
            DictionaryUtil.Set(card.dataConfig?.Vars, "Usable", value);
            card.DataUpdate();
            changed++;
        }

        return changed;
    }

    public static bool EnsureCardTag(CardItem card, string tag)
    {
        if (card == null || string.IsNullOrWhiteSpace(tag) || CardHasTag(card, tag))
        {
            return false;
        }

        var existing = DictionaryUtil.Get(card.Vars, "SpecialTag");
        var next = string.IsNullOrWhiteSpace(existing) ? tag : existing + "," + tag;
        DictionaryUtil.Set(card.Vars, "SpecialTag", next);
        DictionaryUtil.Set(card.dataConfig?.Vars, "SpecialTag", next);
        card.Tags?.Add(tag);
        if (tag == GoldExpIds.GoldDreamTag)
        {
            MarkTemporaryGoldDream(card);
        }

        card.RefreshTag();
        card.DataUpdate();
        FightCardManager.Instance?.RefreshTag(card.dataConfig);
        return true;
    }

    private static void AppendConfigTag(DataConfig config, string tag)
    {
        if (config == null || string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        var existing = DictionaryUtil.Get(config.Vars, "Tag");
        if (DictionaryUtil.ContainsToken(existing, tag))
        {
            return;
        }

        DictionaryUtil.Set(config.Vars, "Tag", string.IsNullOrWhiteSpace(existing) ? tag : existing + "," + tag);
    }

    private static void EnsureNewestCardTag(ScriptExecutor self, string cardId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || self.HandCard == null)
        {
            return;
        }

        foreach (var card in self.HandCard.Where(card => card != null).Reverse())
        {
            if (CardMatchesId(card, cardId))
            {
                EnsureCardTag(card, tag);
                return;
            }
        }

        var newest = self.HandCard.LastOrDefault(card => card != null);
        if (newest != null)
        {
            EnsureCardTag(newest, tag);
        }
    }

    private static bool CardMatchesId(CardItem card, string cardId)
    {
        if (card == null || string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        var localId = DictionaryUtil.Get(card.data, "Id");
        var configId = DictionaryUtil.Get(card.dataConfig?.data, "Id");
        return string.Equals(localId, cardId, StringComparison.Ordinal)
            || string.Equals(configId, cardId, StringComparison.Ordinal)
            || cardId.EndsWith("_" + localId, StringComparison.Ordinal)
            || cardId.EndsWith("_" + configId, StringComparison.Ordinal);
    }

    private static void MarkTemporaryGoldDream(CardItem card)
    {
        var lockId = ExecutorApi.CombatIntAdd(GoldExpIds.TempGoldDreamLockSeq, 1).ToString();
        DictionaryUtil.Set(card.Vars, GoldExpIds.TempGoldDream, "1");
        DictionaryUtil.Set(card.Vars, GoldExpIds.TempGoldDreamLockId, lockId);
        DictionaryUtil.Set(card.Vars, GoldExpIds.TempGoldDreamResolved, "0");
        DictionaryUtil.Set(card.dataConfig?.Vars, GoldExpIds.TempGoldDream, "1");
        DictionaryUtil.Set(card.dataConfig?.Vars, GoldExpIds.TempGoldDreamLockId, lockId);
        DictionaryUtil.Set(card.dataConfig?.Vars, GoldExpIds.TempGoldDreamResolved, "0");
    }

    private static bool CardHasTag(CardItem card, string tag)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.data, "Tag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, "SpecialTag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.dataConfig?.Vars, "SpecialTag"), tag);
    }
}
