using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public enum SunExpCardPresentationSurface
{
    Unknown,
    CombatCard,
    CombatCardInternal,
    CardStyle,
    RewardChoice,
    Display,
    Shop,
    Warehouse,
    SafeBox,
    Dictionary,
    CardPack
}

public sealed class SunExpCardPresentationContext
{
    public Transform? Root { get; set; }
    public IDataConfig? Config { get; set; }
    public CardItem? Card { get; set; }
    public string Source { get; set; } = "";
    public SunExpCardPresentationSurface Surface { get; set; }
}

public sealed class SunExpCardPresentationSubscription
{
    public Action<SunExpCardPresentationContext>? Apply { get; set; }
}

public static class SunExpCardPresentationRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, SunExpCardPresentationSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, SunExpCardPresentationSubscription>[]? cachedSubscriptions;
    private static string pendingReapplySource = "";
    private static int pendingReapplyCount;

    public static void Register(string id, SunExpCardPresentationSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            Subscriptions[id.Trim()] = subscription;
            cachedSubscriptions = null;
        }

        SunExpPerformanceCounters.Record("CardPresentation.HandlerRegistered");
    }

    public static void RequestApply(SunExpCardPresentationContext context)
    {
        if (context.Config == null)
        {
            return;
        }

        Dispatch(context);
    }

    public static void RequestApply(Transform? root, IDataConfig? config, string source, SunExpCardPresentationSurface surface)
    {
        RequestApply(new SunExpCardPresentationContext
        {
            Root = root,
            Config = config,
            Source = source,
            Surface = surface
        });
    }

    public static void RequestActiveCombatCardsReapply(string source)
    {
        lock (SyncRoot)
        {
            pendingReapplySource = source;
            pendingReapplyCount++;
        }

        if (!SunExpFrameScheduler.RunOnceNextFrame("CardPresentation.ReapplyActiveCombatCards", FlushActiveCombatCardsReapply))
        {
            SunExpPerformanceCounters.Record("CardPresentation.ReapplyDeduped");
        }
    }

    public static Transform? FindCombatCardRoot(IDataConfig config)
    {
        try
        {
            foreach (var item in FightUI.cardItemList ?? new List<CardItem>())
            {
                if (item != null && ReferenceEquals(item.dataConfig, config))
                {
                    return item.transform;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Card presentation combat-card lookup failed: " + ex.Message);
        }

        return null;
    }

    private static void FlushActiveCombatCardsReapply()
    {
        string source;
        int count;
        lock (SyncRoot)
        {
            source = pendingReapplySource;
            count = pendingReapplyCount;
            pendingReapplySource = "";
            pendingReapplyCount = 0;
        }

        ReapplyActiveCombatCards(count > 1 ? source + ".merged" + count : source + ".merged");
    }

    private static void ReapplyActiveCombatCards(string source)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var count = 0;
            count += ApplyCards(FightUI.cardItemList, source + ":fight-ui");
            count += ApplyCards(FightUI.WaitCard, source + ":wait-ui");
            if (count > 0)
            {
                SunExpLog.Debug("Card presentation reapplied from " + source + ": " + count);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card presentation active-combat reapply failed from " + source, ex);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("CardPresentation.ReapplyActiveCombatCards", start);
        }
    }

    private static int ApplyCards(IEnumerable<CardItem>? cards, string source)
    {
        if (cards == null)
        {
            return 0;
        }

        var count = 0;
        foreach (var item in cards)
        {
            if (item?.dataConfig == null)
            {
                continue;
            }

            RequestApply(new SunExpCardPresentationContext
            {
                Root = item.transform,
                Config = item.dataConfig,
                Card = item,
                Source = source,
                Surface = SunExpCardPresentationSurface.CombatCard
            });
            count++;
        }

        return count;
    }

    private static void Dispatch(SunExpCardPresentationContext context)
    {
        foreach (var pair in SnapshotSubscriptions())
        {
            var action = pair.Value.Apply;
            if (action == null)
            {
                continue;
            }

            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                SunExpLog.Error("Card presentation handler failed: " + pair.Key + " @ " + context.Source, ex);
            }
        }
    }

    private static KeyValuePair<string, SunExpCardPresentationSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, SunExpCardPresentationSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
