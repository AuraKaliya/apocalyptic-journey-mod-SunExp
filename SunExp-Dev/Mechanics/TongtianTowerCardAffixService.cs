using System;
using System.Collections.Generic;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerCardAffixService
{
    private const string BurnoutTag = "Burnout";
    private static int starterDeckWriteDepth;
    private static readonly CardAttachmentSpec BurnoutSpec = new(
        nativeTags: new[] { BurnoutTag },
        markers: new[] { SunExpIds.TongtianTowerAutoBurnoutMarker },
        scope: CardAttachmentScope.RunPermanent);

    public static bool RunWithStarterDeckSuppressed(Func<bool> action)
    {
        if (action == null)
        {
            return false;
        }

        starterDeckWriteDepth++;
        try
        {
            return action();
        }
        finally
        {
            starterDeckWriteDepth = Math.Max(0, starterDeckWriteDepth - 1);
        }
    }

    public static bool ApplyBurnout(IDataConfig? config, string source)
    {
        if (ShouldSkipAutoBurnout(config))
        {
            return false;
        }

        var changed = CardAttachmentService.AttachToConfig(config, BurnoutSpec, source) > 0;
        if (changed)
        {
            SunExpLog.Debug("[TongtianTowerCardAffix] applied Burnout from " + source);
        }

        return changed;
    }

    public static bool ApplyBurnout(CardItem? card, string source)
    {
        if (ShouldSkipAutoBurnout(card?.dataConfig))
        {
            return false;
        }

        var changed = CardAttachmentService.AttachToCardItem(card, BurnoutSpec, source) > 0;
        if (changed)
        {
            SunExpLog.Debug("[TongtianTowerCardAffix] applied Burnout to card item from " + source);
        }

        return changed;
    }

    public static int MarkStarterDeckBaseline(RoleTable? role, string source)
    {
        if (role == null)
        {
            return 0;
        }

        var changed = 0;
        changed += MarkList(role.cardList);
        changed += MarkList(role.UnCardList);
        if (changed > 0)
        {
            TryPersistRole(role, source + ":starter-baseline");
            SunExpLog.Info("[TongtianTowerCardAffix] marked starter deck baseline from "
                + source
                + ": "
                + changed
                + ".");
        }

        return changed;
    }

    public static int NormalizeOwnedCards(string source)
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return 0;
        }

        var changed = 0;
        changed += ApplyToList(role.cardList, source + ":deck");
        changed += ApplyToList(role.UnCardList, source + ":reserve");
        if (changed > 0)
        {
            TryPersistRole(role, source + ":normalize-owned");
            SunExpLog.Info("[TongtianTowerCardAffix] normalized owned cards from " + source + ": " + changed + ".");
        }

        return changed;
    }

    public static bool TryPersistCurrentRole(string source)
    {
        return TryPersistRole(RoleTable.Instance, source);
    }

    public static bool TryPersistRole(RoleTable? role, string source)
    {
        if (role == null)
        {
            return false;
        }

        try
        {
            GameSaveManager.UpdateRoles(role);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerCardAffix] role persist skipped from "
                + source
                + ": "
                + ex.Message);
            return false;
        }
    }

    public static bool ShouldSkipAutoBurnout(IDataConfig? config)
    {
        return config == null
            || starterDeckWriteDepth > 0
            || CardMutationService.HasRuntimeMarker(config, SunExpIds.TongtianTowerStarterDeckBaselineMarker);
    }

    public static int NormalizeCombatCards(ScriptExecutor? executor, string source)
    {
        var changed = 0;
        changed += ApplyToCardItems(FightUI.cardItemList, source + ":fight-ui");
        changed += ApplyToCardItems(FightUI.WaitCard, source + ":wait-ui");
        if (executor != null)
        {
            changed += ApplyToCardItems(executor.HandCard, source + ":hand");
            changed += ApplyToCardItems(executor.WaitCard, source + ":wait");
        }

        return changed;
    }

    private static int ApplyToList(IEnumerable<IDataConfig>? cards, string source)
    {
        if (cards == null)
        {
            return 0;
        }

        var changed = 0;
        foreach (var card in cards)
        {
            if (ApplyBurnout(card, source))
            {
                changed++;
            }
        }

        return changed;
    }

    private static int MarkList(IEnumerable<IDataConfig>? cards)
    {
        if (cards == null)
        {
            return 0;
        }

        var changed = 0;
        foreach (var card in cards)
        {
            if (CardMutationService.SetRuntimeMarkers(card, SunExpIds.TongtianTowerStarterDeckBaselineMarker))
            {
                changed++;
            }
        }

        return changed;
    }

    private static int ApplyToCardItems(IEnumerable<CardItem>? cards, string source)
    {
        if (cards == null)
        {
            return 0;
        }

        var changed = 0;
        foreach (var card in cards)
        {
            if (ApplyBurnout(card, source))
            {
                changed++;
            }
        }

        return changed;
    }
}
