using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class EndlessSeaCardAffixService
{
    private const string BurnoutTag = "Burnout";
    private static int starterDeckWriteDepth;
    private static readonly CardAttachmentSpec BurnoutSpec = new(
        nativeTags: new[] { BurnoutTag },
        markers: new[] { SunExpIds.EndlessSeaAutoBurnoutMarker },
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
            SunExpLog.Debug("[EndlessSeaCardAffix] applied Burnout from " + source);
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
            SunExpLog.Debug("[EndlessSeaCardAffix] applied Burnout to card item from " + source);
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
            SunExpLog.Info("[EndlessSeaCardAffix] marked starter deck baseline from "
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
            SunExpLog.Info("[EndlessSeaCardAffix] normalized owned cards from " + source + ": " + changed + ".");
        }

        return changed;
    }

    public static int NormalizeRecentOwnedCards(int count, string source)
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return 0;
        }

        var safeCount = Math.Max(1, Math.Min(16, count));
        var changed = 0;
        changed += ApplyToRecent(role.cardList, safeCount, source + ":deck-recent");
        changed += ApplyToRecent(role.UnCardList, safeCount, source + ":reserve-recent");
        if (changed > 0)
        {
            TryPersistRole(role, source + ":normalize-recent-owned");
            SunExpLog.Info("[EndlessSeaCardAffix] normalized recent owned cards from " + source + ": " + changed + ".");
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
            SunExpLog.Warn("[EndlessSeaCardAffix] role persist skipped from "
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
            || CardMutationService.HasRuntimeMarker(config, SunExpIds.EndlessSeaStarterDeckBaselineMarker);
    }

    public static int NormalizeCombatCards(ScriptExecutor? executor, string source)
    {
        var changed = 0;
        var snapshot = AuraCombatCardZoneSnapshot.Capture(executor, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true,
            IncludeExecutorHand = executor != null,
            IncludeExecutorWait = executor != null
        });

        foreach (var reference in snapshot.Cards)
        {
            if (reference.Card != null && ApplyBurnout(reference.Card, source + ":" + SourceSuffix(reference.Zone)))
            {
                changed++;
            }
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

    private static int ApplyToRecent(IEnumerable<IDataConfig>? cards, int count, string source)
    {
        if (cards == null)
        {
            return 0;
        }

        var changed = 0;
        if (cards is IList<IDataConfig> list)
        {
            for (var i = Math.Max(0, list.Count - count); i < list.Count; i++)
            {
                if (ApplyBurnout(list[i], source))
                {
                    changed++;
                }
            }

            return changed;
        }

        foreach (var card in cards.Reverse().Take(count))
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
            if (CardMutationService.SetRuntimeMarkers(card, SunExpIds.EndlessSeaStarterDeckBaselineMarker))
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

    private static string SourceSuffix(AuraCombatCardZoneKind zone)
    {
        return zone switch
        {
            AuraCombatCardZoneKind.FightUiActive => "fight-ui",
            AuraCombatCardZoneKind.FightUiWait => "wait-ui",
            AuraCombatCardZoneKind.ExecutorHand => "hand",
            AuraCombatCardZoneKind.ExecutorWait => "wait",
            _ => "combat"
        };
    }
}
