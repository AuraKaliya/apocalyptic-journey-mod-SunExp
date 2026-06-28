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

    public static int ApplyBurnoutAndWhiteRadianceToFriendlyHands(ScriptExecutor? executor = null)
    {
        var result = RuntimeCardAttachmentService.AttachToCurrentHand(
            executor,
            RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment());
        SunExpLog.Info("Wuna hand temporary attachment pass: " + result.ToLogString());
        return result.Changed;
    }

    public static bool EnsureCardItemTag(CardItem? card, string tag)
    {
        return CardMutationService.AddSpecialTags(card, tag);
    }

    public static bool EnsureDataConfigTag(IDataConfig? config, string tag)
    {
        return CardMutationService.AddSpecialTags(config, tag);
    }

}
