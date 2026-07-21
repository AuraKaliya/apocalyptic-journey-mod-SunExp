using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class TerriasCardTagService
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
                if (EnsureDataConfigTag(card, TerriasIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("White radiance run deck scan skipped: " + ex.Message);
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
                if (EnsureCardItemTag(card, TerriasIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("White radiance hand scan skipped: " + ex.Message);
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
                if (EnsureDataConfigTag(card, TerriasIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }

            foreach (var card in manager.usedCardList)
            {
                if (EnsureDataConfigTag(card, TerriasIds.WhiteRadianceTag))
                {
                    changed++;
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("White radiance fight deck scan skipped: " + ex.Message);
        }

        if (executor != null)
        {
            try
            {
                foreach (var card in executor.HandCard ?? Enumerable.Empty<CardItem>())
                {
                    if (EnsureCardItemTag(card, TerriasIds.WhiteRadianceTag))
                    {
                        changed++;
                    }
                }

                foreach (var card in executor.DeckCard ?? new List<DataConfig>())
                {
                    if (EnsureDataConfigTag(card, TerriasIds.WhiteRadianceTag))
                    {
                        changed++;
                    }
                }

                foreach (var card in executor.UsedCard ?? new List<DataConfig>())
                {
                    if (EnsureDataConfigTag(card, TerriasIds.WhiteRadianceTag))
                    {
                        changed++;
                    }
                }
            }
            catch (Exception ex)
            {
                TerriasLog.Debug("White radiance executor scan skipped: " + ex.Message);
            }
        }

        return changed;
    }

    public static int ApplyBurnoutAndWhiteRadianceToFriendlyHands(ScriptExecutor? executor = null)
    {
        var result = RuntimeCardAttachmentService.AttachToCurrentHand(
            executor,
            RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment());
        TerriasLog.Info("Wuna hand temporary attachment pass: " + result.ToLogString());
        return result.Changed;
    }

    public static bool RequestBurnoutAndWhiteRadianceForFriendlyHands(ScriptExecutor? executor = null, string source = "")
    {
        return RuntimeCardAttachmentService.RequestAttachToCurrentHand(
            executor,
            RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment(),
            string.IsNullOrWhiteSpace(source) ? "WunaWhiteSunPrayer" : source);
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
