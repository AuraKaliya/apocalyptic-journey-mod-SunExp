using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class SunExpCardRefreshQueue
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PendingCardRefresh> PendingCards = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingConfigRefresh> PendingConfigs = new(StringComparer.Ordinal);
    private static int RefreshBudgetPerFrame => Math.Max(4, SunExpPerformanceSettings.FrameSchedulerBudget / 2);

    public static void RequestDataUpdate(CardItem? card, string source)
    {
        Request(card, source, refreshTags: false, dataUpdate: true);
    }

    public static void RequestTagRefresh(CardItem? card, string source)
    {
        Request(card, source, refreshTags: true, dataUpdate: false);
    }

    public static void RequestFullRefresh(CardItem? card, string source)
    {
        Request(card, source, refreshTags: true, dataUpdate: true);
    }

    public static void RequestConfigTagRefresh(IDataConfig? config, string source)
    {
        if (config == null)
        {
            return;
        }

        var key = ConfigKey(config);
        if (key.Length == 0)
        {
            RefreshConfigNow(config, source);
            return;
        }

        lock (SyncRoot)
        {
            PendingConfigs[key] = new PendingConfigRefresh(config, source);
        }

        ScheduleFlush();
    }

    private static void Request(CardItem? card, string source, bool refreshTags, bool dataUpdate)
    {
        if (card == null)
        {
            return;
        }

        var key = CardKey(card);
        if (key.Length == 0)
        {
            RefreshNow(card, source, refreshTags, dataUpdate);
            return;
        }

        lock (SyncRoot)
        {
            PendingCards[key] = PendingCards.TryGetValue(key, out var existing)
                ? new PendingCardRefresh(card, existing.RefreshTags || refreshTags, existing.DataUpdate || dataUpdate, source)
                : new PendingCardRefresh(card, refreshTags, dataUpdate, source);
        }

        ScheduleFlush();
    }

    private static void ScheduleFlush()
    {
        if (!SunExpFrameDispatcher.RunOnceNextFrame("SunExpCardRefreshQueue.Flush", Flush))
        {
            SunExpPerformanceCounters.Record("CardRefreshQueue.Deduped");
        }
    }

    private static void Flush()
    {
        var budget = RefreshBudgetPerFrame;
        PendingConfigRefresh[] configs;
        PendingCardRefresh[] cards;
        lock (SyncRoot)
        {
            if (PendingCards.Count == 0 && PendingConfigs.Count == 0)
            {
                return;
            }

            var configCount = Math.Min(PendingConfigs.Count, budget);
            configs = new PendingConfigRefresh[configCount];
            CopyAndRemoveConfigs(configs);

            var cardBudget = budget - configCount;
            var cardCount = Math.Min(PendingCards.Count, cardBudget);
            cards = new PendingCardRefresh[cardCount];
            CopyAndRemoveCards(cards);
        }

        var start = SunExpPerformanceCounters.Timestamp();
        foreach (var item in configs)
        {
            RefreshConfigNow(item.Config, item.Source);
        }

        foreach (var item in cards)
        {
            RefreshNow(item.Card, item.Source, item.RefreshTags, item.DataUpdate);
        }

        SunExpPerformanceCounters.RecordDuration("CardRefreshQueue.Flush", start);

        bool hasMore;
        lock (SyncRoot)
        {
            hasMore = PendingCards.Count > 0 || PendingConfigs.Count > 0;
        }

        if (hasMore)
        {
            SunExpPerformanceCounters.Record("CardRefreshQueue.FlushContinued");
            ScheduleFlush();
        }
    }

    private static void CopyAndRemoveConfigs(PendingConfigRefresh[] target)
    {
        if (target.Length == 0)
        {
            return;
        }

        var keys = new string[target.Length];
        var index = 0;
        foreach (var item in PendingConfigs)
        {
            target[index] = item.Value;
            keys[index] = item.Key;
            index++;
            if (index >= target.Length)
            {
                break;
            }
        }

        for (var i = 0; i < index; i++)
        {
            PendingConfigs.Remove(keys[i]);
        }
    }

    private static void CopyAndRemoveCards(PendingCardRefresh[] target)
    {
        if (target.Length == 0)
        {
            return;
        }

        var keys = new string[target.Length];
        var index = 0;
        foreach (var item in PendingCards)
        {
            target[index] = item.Value;
            keys[index] = item.Key;
            index++;
            if (index >= target.Length)
            {
                break;
            }
        }

        for (var i = 0; i < index; i++)
        {
            PendingCards.Remove(keys[i]);
        }
    }

    private static void RefreshNow(CardItem card, string source, bool refreshTags, bool dataUpdate)
    {
        try
        {
            if (refreshTags)
            {
                card.RefreshTag();
            }

            if (dataUpdate)
            {
                card.DataUpdate();
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Queued card refresh skipped from " + source + ": " + ex.Message);
        }
    }

    private static void RefreshConfigNow(IDataConfig config, string source)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            FightCardManager.Instance?.RefreshTag(config);
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Queued config tag refresh skipped from " + source + ": " + ex.Message);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("FightCardManager.RefreshTag", start);
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

    private static string ConfigKey(IDataConfig config)
    {
        try
        {
            var instanceId = config.InstanceID;
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                return instanceId!;
            }
        }
        catch
        {
            // Fall back to the managed object hash below.
        }

        try
        {
            return config.GetHashCode().ToString();
        }
        catch
        {
            return "";
        }
    }

    private readonly struct PendingCardRefresh
    {
        public PendingCardRefresh(CardItem card, bool refreshTags, bool dataUpdate, string source)
        {
            Card = card;
            RefreshTags = refreshTags;
            DataUpdate = dataUpdate;
            Source = source;
        }

        public CardItem Card { get; }

        public bool RefreshTags { get; }

        public bool DataUpdate { get; }

        public string Source { get; }
    }

    private readonly struct PendingConfigRefresh
    {
        public PendingConfigRefresh(IDataConfig config, string source)
        {
            Config = config;
            Source = source;
        }

        public IDataConfig Config { get; }

        public string Source { get; }
    }
}
