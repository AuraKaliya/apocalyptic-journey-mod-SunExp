using System;
using System.Collections.Generic;
using AuraShared.Core;
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
    CardPack,
    PostCommit
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
    private static readonly HashSet<string> LoggedCombatRootMissDiagnostics = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, PendingReapply> PendingReapplyByDelay = new();
    private static KeyValuePair<string, SunExpCardPresentationSubscription>[]? cachedSubscriptions;

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
        SunExpLog.InfoAlways("Card presentation handler registered: id=" + id.Trim() + ", count=" + SubscriptionCount());
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
        RequestActiveCombatCardsReapply(source, 1);
    }

    public static void RequestActiveCombatCardsReapply(string source, int delayFrames)
    {
        var normalizedDelay = Math.Max(1, delayFrames);
        lock (SyncRoot)
        {
            PendingReapplyByDelay[normalizedDelay] = PendingReapplyByDelay.TryGetValue(normalizedDelay, out var pending)
                ? pending.Merge(source)
                : new PendingReapply(source, 1);
        }

        if (!SunExpFrameScheduler.RunOnceAfterFrames(
                "CardPresentation.ReapplyActiveCombatCards." + normalizedDelay,
                normalizedDelay,
                () => FlushActiveCombatCardsReapply(normalizedDelay)))
        {
            SunExpPerformanceCounters.Record("CardPresentation.ReapplyDeduped");
        }
    }

    public static Transform? FindCombatCardRoot(IDataConfig config)
    {
        try
        {
            foreach (var reference in ActiveCombatCardSnapshot().Cards)
            {
                if (reference.Root != null && ReferenceEquals(reference.Config, config))
                {
                    return reference.Root;
                }
            }

            RecordCombatRootMiss(config);
            return null;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Card presentation combat-card lookup failed: " + ex.Message);
        }

        return null;
    }

    private static void RecordCombatRootMiss(IDataConfig config)
    {
        if (!SunExpPerformanceSettings.CountersEnabled)
        {
            return;
        }

        try
        {
            var cardId = CardConfigApi.Id(config);
            var total = 0;
            var idMatches = 0;
            foreach (var reference in ActiveCombatCardSnapshot().Cards)
            {
                if (reference.Config == null)
                {
                    continue;
                }

                total++;
                if (string.Equals(reference.CardId, cardId, StringComparison.Ordinal))
                {
                    idMatches++;
                }
            }

            if (idMatches > 0)
            {
                SunExpPerformanceCounters.Record("CardPresentation.CombatRootMiss.IdMatch");
                if (LoggedCombatRootMissDiagnostics.Count < 16 && LoggedCombatRootMissDiagnostics.Add(cardId))
                {
                    SunExpLog.Warn("Card presentation combat root lookup missed by IDataConfig reference but found same-id card(s): cardId="
                        + cardId
                        + ", sameIdCards="
                        + idMatches
                        + ", totalCombatCards="
                        + total);
                }
            }
            else
            {
                SunExpPerformanceCounters.Record("CardPresentation.CombatRootMiss.NoIdMatch");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Card presentation combat-root miss diagnostics failed: " + ex.Message);
        }
    }

    private static void FlushActiveCombatCardsReapply(int delayFrames)
    {
        PendingReapply pending;
        lock (SyncRoot)
        {
            if (!PendingReapplyByDelay.TryGetValue(delayFrames, out pending))
            {
                return;
            }

            PendingReapplyByDelay.Remove(delayFrames);
        }

        ReapplyActiveCombatCards(pending.Count > 1
            ? pending.Source + ".d" + delayFrames + ".merged" + pending.Count
            : pending.Source + ".d" + delayFrames + ".merged");
    }

    private static void ReapplyActiveCombatCards(string source)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var count = ApplyCards(ActiveCombatCardSnapshot(), source);
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

    private static int ApplyCards(AuraCombatCardZoneSnapshot snapshot, string source)
    {
        var count = 0;
        foreach (var reference in snapshot.Cards)
        {
            if (reference.Config == null)
            {
                continue;
            }

            RequestApply(new SunExpCardPresentationContext
            {
                Root = reference.Root,
                Config = reference.Config,
                Card = reference.Card,
                Source = source + ":" + PresentationSourceSuffix(reference.Zone),
                Surface = SunExpCardPresentationSurface.CombatCard
            });
            count++;
        }

        return count;
    }

    private static AuraCombatCardZoneSnapshot ActiveCombatCardSnapshot()
    {
        return AuraCombatCardZoneSnapshot.Capture(null, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true,
            IncludeExecutorHand = false,
            IncludeExecutorWait = false
        });
    }

    private static string PresentationSourceSuffix(AuraCombatCardZoneKind zone)
    {
        return zone == AuraCombatCardZoneKind.FightUiWait ? "wait-ui" : "fight-ui";
    }

    private static void Dispatch(SunExpCardPresentationContext context)
    {
        var snapshot = SnapshotSubscriptions();
        if (snapshot.Length == 0)
        {
            SunExpPerformanceCounters.Record("CardPresentation.DispatchNoHandlers");
            return;
        }

        foreach (var pair in snapshot)
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

    private static int SubscriptionCount()
    {
        lock (SyncRoot)
        {
            return Subscriptions.Count;
        }
    }

    private readonly struct PendingReapply
    {
        public PendingReapply(string source, int count)
        {
            Source = source ?? "";
            Count = Math.Max(0, count);
        }

        public string Source { get; }

        public int Count { get; }

        public PendingReapply Merge(string source)
        {
            return new PendingReapply(string.IsNullOrWhiteSpace(source) ? Source : source, Count + 1);
        }
    }
}
