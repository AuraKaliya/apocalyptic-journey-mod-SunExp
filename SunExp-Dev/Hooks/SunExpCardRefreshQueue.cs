using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SunExpCardRefreshQueue
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PendingRefresh> Pending = new(StringComparer.Ordinal);

    public static void RequestDataUpdate(CardItem? card, string source)
    {
        Request(card, source, refreshTags: false);
    }

    public static void RequestTagRefresh(CardItem? card, string source)
    {
        Request(card, source, refreshTags: true);
    }

    private static void Request(CardItem? card, string source, bool refreshTags)
    {
        if (card == null)
        {
            return;
        }

        var key = CardKey(card);
        if (key.Length == 0)
        {
            RefreshNow(card, source, refreshTags);
            return;
        }

        lock (SyncRoot)
        {
            Pending[key] = Pending.TryGetValue(key, out var existing)
                ? new PendingRefresh(card, existing.RefreshTags || refreshTags, source)
                : new PendingRefresh(card, refreshTags, source);
        }

        if (!SunExpFrameScheduler.RunOnceNextFrame("SunExpCardRefreshQueue.Flush", Flush))
        {
            SunExpPerformanceCounters.Record("CardRefreshQueue.Deduped");
        }
    }

    private static void Flush()
    {
        PendingRefresh[] items;
        lock (SyncRoot)
        {
            if (Pending.Count == 0)
            {
                return;
            }

            items = new PendingRefresh[Pending.Count];
            Pending.Values.CopyTo(items, 0);
            Pending.Clear();
        }

        var start = SunExpPerformanceCounters.Timestamp();
        foreach (var item in items)
        {
            RefreshNow(item.Card, item.Source, item.RefreshTags);
        }

        SunExpPerformanceCounters.RecordDuration("CardRefreshQueue.Flush", start);
    }

    private static void RefreshNow(CardItem card, string source, bool refreshTags)
    {
        try
        {
            if (refreshTags)
            {
                card.RefreshTag();
            }
            else
            {
                card.DataUpdate();
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Queued card refresh skipped from " + source + ": " + ex.Message);
        }
    }

    private static string CardKey(CardItem card)
    {
        try
        {
            var instanceId = card.dataConfig?.InstanceID;
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                return instanceId!;
            }
        }
        catch
        {
            // Fall back to Unity instance id below.
        }

        try
        {
            return card.GetInstanceID().ToString();
        }
        catch
        {
            return "";
        }
    }

    private readonly struct PendingRefresh
    {
        public PendingRefresh(CardItem card, bool refreshTags, string source)
        {
            Card = card;
            RefreshTags = refreshTags;
            Source = source;
        }

        public CardItem Card { get; }

        public bool RefreshTags { get; }

        public string Source { get; }
    }
}
