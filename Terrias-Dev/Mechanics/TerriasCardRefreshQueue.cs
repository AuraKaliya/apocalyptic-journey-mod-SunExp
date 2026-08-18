using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class TerriasCardRefreshQueue
{
    private const double SlowRefreshWarningMilliseconds = 8.0;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PendingCardRefresh> PendingCards = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingConfigRefresh> PendingConfigs = new(StringComparer.Ordinal);
    private const double CooperativeSliceBudgetMilliseconds = 2.0;

    public static void RequestDataUpdate(CardItem? card, string source)
    {
        Request(card, source, refreshTags: false, dataUpdate: true, costUpdate: false, descriptionUpdate: false);
    }

    public static void RequestCostUpdate(CardItem? card, string source)
    {
        Request(card, source, refreshTags: false, dataUpdate: false, costUpdate: true, descriptionUpdate: false);
    }

    public static void RequestDescriptionUpdate(CardItem? card, string source)
    {
        Request(card, source, refreshTags: false, dataUpdate: false, costUpdate: false, descriptionUpdate: true);
    }

    public static void RequestTagRefresh(CardItem? card, string source)
    {
        Request(card, source, refreshTags: true, dataUpdate: false, costUpdate: false, descriptionUpdate: false);
    }

    public static void RequestFullRefresh(CardItem? card, string source)
    {
        Request(card, source, refreshTags: true, dataUpdate: true, costUpdate: false, descriptionUpdate: false);
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

    private static void Request(
        CardItem? card,
        string source,
        bool refreshTags,
        bool dataUpdate,
        bool costUpdate,
        bool descriptionUpdate)
    {
        if (card == null)
        {
            return;
        }

        var key = CardKey(card);
        if (key.Length == 0)
        {
            RefreshNow(card, source, refreshTags, dataUpdate, costUpdate, descriptionUpdate);
            return;
        }

        lock (SyncRoot)
        {
            PendingCards[key] = PendingCards.TryGetValue(key, out var existing)
                ? new PendingCardRefresh(
                    card,
                    existing.RefreshTags || refreshTags,
                    existing.DataUpdate || dataUpdate,
                    existing.CostUpdate || costUpdate,
                    existing.DescriptionUpdate || descriptionUpdate,
                    source)
                : new PendingCardRefresh(card, refreshTags, dataUpdate, costUpdate, descriptionUpdate, source);
        }

        ScheduleFlush();
    }

    private static void ScheduleFlush()
    {
        if (!AuraSharedFrameScheduler.RunCooperative(new AuraSharedFrameWorkRequest
            {
                OwnerId = TerriasIds.ModId,
                Key = "TerriasCardRefreshQueue.Flush",
                Source = "Terrias.CardRefreshQueue",
                DelayFrames = 1,
                Phase = AuraSharedFramePhase.Presentation,
                Priority = 100,
                EstimatedCost = 4,
                SliceBudgetMilliseconds = CooperativeSliceBudgetMilliseconds,
                ExecuteSlice = FlushSlice,
                OnSliceExecuted = report =>
                {
                    TerriasPerformanceCounters.Record("CardRefreshQueue.CooperativeSlice");
                    if (report.ElapsedMilliseconds >= 8d)
                    {
                        TerriasPerformanceCounters.Record("CardRefreshQueue.CooperativeSliceOverBudget");
                    }
                }
            }))
        {
            TerriasPerformanceCounters.Record("CardRefreshQueue.Deduped");
        }
    }

    public static int RequestDataUpdateForHandCards(
        IEnumerable<CardItem>? handCards,
        IEnumerable<string>? cardIds,
        string source)
    {
        if (handCards == null || cardIds == null)
        {
            return 0;
        }

        var ids = new HashSet<string>(cardIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return 0;
        }

        var requested = 0;
        foreach (var card in handCards)
        {
            var id = CardConfigApi.Id(card?.dataConfig);
            if (card == null || !ids.Contains(id))
            {
                continue;
            }

            RequestDataUpdate(card, source);
            requested++;
        }

        if (requested > 0)
        {
            for (var i = 0; i < requested; i++)
            {
                TerriasPerformanceCounters.Record("CardRefreshQueue.DescriptionSubsetRequested");
            }
        }

        return requested;
    }

    public static int RequestDescriptionUpdateForHandCards(
        IEnumerable<CardItem>? handCards,
        IEnumerable<string>? cardIds,
        string source)
    {
        if (handCards == null || cardIds == null)
        {
            return 0;
        }

        var ids = new HashSet<string>(cardIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(TerriasContentIdCompatibility.Canonicalize), StringComparer.Ordinal);
        var requested = 0;
        foreach (var card in handCards)
        {
            if (card == null
                || !ids.Contains(TerriasContentIdCompatibility.Canonicalize(CardConfigApi.Id(card.dataConfig))))
            {
                continue;
            }

            RequestDescriptionUpdate(card, source);
            requested++;
            TerriasPerformanceCounters.Record("CardRefreshQueue.DescriptionDeltaRequested");
        }

        return requested;
    }

    private static bool FlushSlice(AuraSharedFrameSliceContext context)
    {
        PendingConfigRefresh? config = null;
        PendingCardRefresh? card = null;
        lock (SyncRoot)
        {
            if (PendingCards.Count == 0 && PendingConfigs.Count == 0)
            {
                return true;
            }

            if (PendingConfigs.Count > 0)
            {
                var entry = First(PendingConfigs);
                config = entry.Value;
                PendingConfigs.Remove(entry.Key);
            }
            else
            {
                var entry = First(PendingCards);
                card = entry.Value;
                PendingCards.Remove(entry.Key);
            }
        }

        var start = TerriasPerformanceCounters.Timestamp();
        if (config.HasValue)
        {
            RefreshConfigNow(config.Value.Config, config.Value.Source);
        }
        else if (card.HasValue)
        {
            RefreshNow(
                card.Value.Card,
                card.Value.Source,
                card.Value.RefreshTags,
                card.Value.DataUpdate,
                card.Value.CostUpdate,
                card.Value.DescriptionUpdate);
        }

        TerriasPerformanceCounters.RecordDuration("CardRefreshQueue.Flush", start);
        lock (SyncRoot)
        {
            var completed = PendingCards.Count == 0 && PendingConfigs.Count == 0;
            if (!completed)
            {
                TerriasPerformanceCounters.Record("CardRefreshQueue.FlushContinued");
            }

            return completed;
        }
    }

    private static KeyValuePair<string, T> First<T>(Dictionary<string, T> values)
    {
        foreach (var pair in values)
        {
            return pair;
        }

        return default;
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

    private static void RefreshNow(
        CardItem card,
        string source,
        bool refreshTags,
        bool dataUpdate,
        bool costUpdate,
        bool descriptionUpdate)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            if (refreshTags)
            {
                var tagStart = TerriasPerformanceCounters.Timestamp();
                card.RefreshTag();
                TerriasPerformanceCounters.RecordDuration("CardRefreshQueue.Card.RefreshTag", tagStart);
            }

            if (dataUpdate)
            {
                var dataStart = TerriasPerformanceCounters.Timestamp();
                card.DataUpdate();
                TerriasPerformanceCounters.RecordDuration("CardRefreshQueue.Card.DataUpdate", dataStart);
            }
            else
            {
                if (costUpdate)
                {
                    var costStart = TerriasPerformanceCounters.Timestamp();
                    if (!AuraCardPresentationDelta.TrySetCost(
                            card.transform,
                            CardConfigApi.NativeDisplayCost(card.dataConfig, FightPlayer.Instance?.Status).ToString()))
                    {
                        card.DataUpdate();
                        TerriasPerformanceCounters.Record("CardRefreshQueue.Card.CostFallback");
                        return;
                    }

                    TerriasPerformanceCounters.RecordDuration("CardRefreshQueue.Card.CostUpdate", costStart);
                }

                if (descriptionUpdate)
                {
                    var descriptionStart = TerriasPerformanceCounters.Timestamp();
                    if (!TerriasCardDescriptionProjector.TryRefresh(card))
                    {
                        card.DataUpdate();
                        TerriasPerformanceCounters.Record("CardRefreshQueue.Card.DescriptionFallback");
                    }
                    else
                    {
                        TerriasPerformanceCounters.Record("CardRefreshQueue.Card.DescriptionDelta");
                    }

                    TerriasPerformanceCounters.RecordDuration(
                        "CardRefreshQueue.Card.DescriptionUpdate",
                        descriptionStart);
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Queued card refresh skipped from " + source + ": " + ex.Message);
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("CardRefreshQueue.Card.Refresh", start);
            LogSlowRefresh("card", source, start);
        }
    }

    private static void RefreshConfigNow(IDataConfig config, string source)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            FightCardManager.Instance?.RefreshTag(config);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Queued config tag refresh skipped from " + source + ": " + ex.Message);
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("FightCardManager.RefreshTag", start);
            LogSlowRefresh("config", source, start);
        }
    }

    private static void LogSlowRefresh(string kind, string source, long startTimestamp)
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        if (startTimestamp <= 0L)
        {
            return;
        }

        var elapsed = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (elapsed < SlowRefreshWarningMilliseconds)
        {
            return;
        }

        TerriasLog.Warn("Slow Terrias card refresh: kind="
            + kind
            + ", elapsedMs="
            + elapsed.ToString("0.###")
            + ", source="
            + source);
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
        public PendingCardRefresh(
            CardItem card,
            bool refreshTags,
            bool dataUpdate,
            bool costUpdate,
            bool descriptionUpdate,
            string source)
        {
            Card = card;
            RefreshTags = refreshTags;
            DataUpdate = dataUpdate;
            CostUpdate = costUpdate;
            DescriptionUpdate = descriptionUpdate;
            Source = source;
        }

        public CardItem Card { get; }

        public bool RefreshTags { get; }

        public bool DataUpdate { get; }

        public bool CostUpdate { get; }

        public bool DescriptionUpdate { get; }

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
