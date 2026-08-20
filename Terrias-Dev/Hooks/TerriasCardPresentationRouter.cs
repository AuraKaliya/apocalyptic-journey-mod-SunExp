using System;
using System.Collections.Generic;
using System.Threading;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public enum TerriasCardPresentationSurface
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

public sealed class TerriasCardPresentationContext
{
    public Transform? Root { get; set; }
    public IDataConfig? Config { get; set; }
    public CardItem? Card { get; set; }
    public string Source { get; set; } = "";
    public TerriasCardPresentationSurface Surface { get; set; }
}

public sealed class TerriasCardPresentationSubscription
{
    public Action<TerriasCardPresentationContext>? Apply { get; set; }
}

public static class TerriasCardPresentationRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, TerriasCardPresentationSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedCombatRootMissDiagnostics = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, PendingReapply> PendingReapplyByDelay = new();
    private static KeyValuePair<string, TerriasCardPresentationSubscription>[]? cachedSubscriptions;

    public static IDisposable Register(string id, TerriasCardPresentationSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null)
        {
            return EmptyDisposable.Instance;
        }

        lock (SyncRoot)
        {
            Subscriptions[id.Trim()] = subscription;
            cachedSubscriptions = null;
        }

        TerriasPerformanceCounters.Record("CardPresentation.HandlerRegistered");
        TerriasLog.InfoAlways("Card presentation handler registered: id=" + id.Trim() + ", count=" + SubscriptionCount());
        return new RegistrationHandle(id.Trim(), subscription);
    }

    public static void RequestApply(TerriasCardPresentationContext context)
    {
        if (context.Config == null)
        {
            return;
        }

        Dispatch(context);
    }

    public static void RequestApply(Transform? root, IDataConfig? config, string source, TerriasCardPresentationSurface surface)
    {
        RequestApply(new TerriasCardPresentationContext
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

        if (!TerriasFrameScheduler.RunOnceAfterFrames(
                "CardPresentation.ReapplyActiveCombatCards." + normalizedDelay,
                normalizedDelay,
                () => FlushActiveCombatCardsReapply(normalizedDelay)))
        {
            TerriasPerformanceCounters.Record("CardPresentation.ReapplyDeduped");
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
            TerriasLog.Debug("Card presentation combat-card lookup failed: " + ex.Message);
        }

        return null;
    }

    private static void RecordCombatRootMiss(IDataConfig config)
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
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
                TerriasPerformanceCounters.Record("CardPresentation.CombatRootMiss.IdMatch");
                if (LoggedCombatRootMissDiagnostics.Count < 16 && LoggedCombatRootMissDiagnostics.Add(cardId))
                {
                    TerriasLog.Warn("Card presentation combat root lookup missed by IDataConfig reference but found same-id card(s): cardId="
                        + cardId
                        + ", sameIdCards="
                        + idMatches
                        + ", totalCombatCards="
                        + total);
                }
            }
            else
            {
                TerriasPerformanceCounters.Record("CardPresentation.CombatRootMiss.NoIdMatch");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Card presentation combat-root miss diagnostics failed: " + ex.Message);
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
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            var count = ApplyCards(ActiveCombatCardSnapshot(), source);
            if (count > 0)
            {
                TerriasLog.Debug("Card presentation reapplied from " + source + ": " + count);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Card presentation active-combat reapply failed from " + source, ex);
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("CardPresentation.ReapplyActiveCombatCards", start);
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

            RequestApply(new TerriasCardPresentationContext
            {
                Root = reference.Root,
                Config = reference.Config,
                Card = reference.Card,
                Source = source + ":" + PresentationSourceSuffix(reference.Zone),
                Surface = TerriasCardPresentationSurface.CombatCard
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

    private static void Dispatch(TerriasCardPresentationContext context)
    {
        var snapshot = SnapshotSubscriptions();
        if (snapshot.Length == 0)
        {
            TerriasPerformanceCounters.Record("CardPresentation.DispatchNoHandlers");
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
                TerriasLog.Error("Card presentation handler failed: " + pair.Key + " @ " + context.Source, ex);
            }
        }
    }

    private static KeyValuePair<string, TerriasCardPresentationSubscription>[] SnapshotSubscriptions()
    {
        var cached = Volatile.Read(ref cachedSubscriptions);
        if (cached != null)
        {
            return cached;
        }

        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, TerriasCardPresentationSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }

    private static void Unregister(string id, TerriasCardPresentationSubscription subscription)
    {
        lock (SyncRoot)
        {
            if (Subscriptions.TryGetValue(id, out var current) && ReferenceEquals(current, subscription))
            {
                Subscriptions.Remove(id);
                cachedSubscriptions = null;
            }
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

    private sealed class RegistrationHandle : IDisposable
    {
        private readonly TerriasCardPresentationSubscription subscription;
        private string? id;

        public RegistrationHandle(string id, TerriasCardPresentationSubscription subscription)
        {
            this.id = id;
            this.subscription = subscription;
        }

        public void Dispose()
        {
            var current = id;
            if (current == null) return;
            id = null;
            Unregister(current, subscription);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
