using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class SunExpCardTagService
{
    public static int ApplyWhiteRadianceToRunDeck()
    {
        var changed = 0;
        try
        {
            var cards = RoleTable.Instance?.cardList;
            if (cards == null)
            {
                return changed;
            }

            foreach (var card in cards)
            {
                if (EnsureDataConfigTag(card, SunExpIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("White radiance run deck scan skipped: " + ex.Message);
        }

        return changed;
    }

    public static int ApplyWhiteRadianceToFightZones(ScriptExecutor? executor = null)
    {
        var changed = 0;
        try
        {
            foreach (var card in FightUI.cardItemList ?? new List<CardItem>())
            {
                if (EnsureCardItemTag(card, SunExpIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("White radiance hand scan skipped: " + ex.Message);
        }

        try
        {
            var manager = FightCardManager.Instance;
            if (manager == null)
            {
                return changed;
            }

            foreach (var card in manager.cardList)
            {
                if (EnsureDataConfigTag(card, SunExpIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }

            foreach (var card in manager.usedCardList)
            {
                if (EnsureDataConfigTag(card, SunExpIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("White radiance fight deck scan skipped: " + ex.Message);
        }

        if (executor != null)
        {
            try
            {
                foreach (var card in executor.HandCard ?? Enumerable.Empty<CardItem>())
                {
                    if (EnsureCardItemTag(card, SunExpIds.WhiteRadianceTag))
                    {
                        changed++;
                    }
                }

                foreach (var card in executor.DeckCard ?? new List<DataConfig>())
                {
                    if (EnsureDataConfigTag(card, SunExpIds.WhiteRadianceTag))
                    {
                        changed++;
                    }
                }

                foreach (var card in executor.UsedCard ?? new List<DataConfig>())
                {
                    if (EnsureDataConfigTag(card, SunExpIds.WhiteRadianceTag))
                    {
                        changed++;
                    }
                }
            }
            catch (Exception ex)
            {
                SunExpLog.Debug("White radiance executor scan skipped: " + ex.Message);
            }
        }

        return changed;
    }

    public static bool EnsureCardItemTag(CardItem? card, string tag)
    {
        if (card == null || string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var changed = EnsureDataConfigTag(card.dataConfig, tag);
        if (CardItemHasTag(card, tag))
        {
            return changed;
        }

        var next = AppendToken(DictionaryUtil.Get(card.Vars, "SpecialTag"), tag);
        DictionaryUtil.Set(card.Vars, "SpecialTag", next);
        if (card.Tags != null && !card.Tags.Contains(tag))
        {
            card.Tags.Add(tag);
        }

        try
        {
            card.RefreshTag();
            card.DataUpdate();
            FightCardManager.Instance?.RefreshTag(card.dataConfig);
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("White radiance card refresh skipped: " + ex.Message);
        }

        return true;
    }

    public static bool EnsureDataConfigTag(IDataConfig? config, string tag)
    {
        if (config == null || string.IsNullOrWhiteSpace(tag) || DataConfigHasTag(config, tag))
        {
            return false;
        }

        var next = AppendToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), tag);
        DictionaryUtil.Set(config.Vars, "SpecialTag", next);
        try
        {
            FightCardManager.Instance?.RefreshTag(config);
        }
        catch
        {
            // RefreshTag is best effort for deck/discard data configs.
        }

        return true;
    }

    private static bool CardItemHasTag(CardItem card, string tag)
    {
        return DataConfigHasTag(card.dataConfig, tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.data, "Tag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, "SpecialTag"), tag)
            || card.Tags?.Contains(tag) == true;
    }

    private static bool DataConfigHasTag(IDataConfig? config, string tag)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.Vars, "SpecialTag"), tag);
    }

    private static string AppendToken(string existing, string tag)
    {
        return string.IsNullOrWhiteSpace(existing) ? tag : existing + "," + tag;
    }
}
