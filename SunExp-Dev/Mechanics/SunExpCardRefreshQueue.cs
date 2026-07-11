using System;
using System.Collections.Generic;
using System.Diagnostics;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class SunExpCardRefreshQueue
{
    private const double SlowRefreshWarningMilliseconds = 8.0;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PendingCardRefresh> PendingCards = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingConfigRefresh> PendingConfigs = new(StringComparer.Ordinal);
    private const double CooperativeSliceBudgetMilliseconds = 2.0;

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
        if (!AuraSharedFrameScheduler.RunCooperative(new AuraSharedFrameWorkRequest
            {
                OwnerId = SunExpIds.ModId,
                Key = "SunExpCardRefreshQueue.Flush",
                Source = "SunExp.CardRefreshQueue",
                DelayFrames = 1,
                Phase = AuraSharedFramePhase.Presentation,
                Priority = 100,
                EstimatedCost = 4,
                SliceBudgetMilliseconds = CooperativeSliceBudgetMilliseconds,
                ExecuteSlice = FlushSlice,
                OnSliceExecuted = report =>
                {
                    SunExpPerformanceCounters.Record("CardRefreshQueue.CooperativeSlice");
                    if (report.ElapsedMilliseconds >= 8d)
                    {
                        SunExpPerformanceCounters.Record("CardRefreshQueue.CooperativeSliceOverBudget");
                    }
                }
            }))
        {
            SunExpPerformanceCounters.Record("CardRefreshQueue.Deduped");
        }
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

        var start = SunExpPerformanceCounters.Timestamp();
        if (config.HasValue)
        {
            RefreshConfigNow(config.Value.Config, config.Value.Source);
        }
        else if (card.HasValue)
        {
            RefreshNow(card.Value.Card, card.Value.Source, card.Value.RefreshTags, card.Value.DataUpdate);
        }

        SunExpPerformanceCounters.RecordDuration("CardRefreshQueue.Flush", start);
        lock (SyncRoot)
        {
            var completed = PendingCards.Count == 0 && PendingConfigs.Count == 0;
            if (!completed)
            {
                SunExpPerformanceCounters.Record("CardRefreshQueue.FlushContinued");
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

    private static void RefreshNow(CardItem card, string source, bool refreshTags, bool dataUpdate)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            if (refreshTags)
            {
                var tagStart = SunExpPerformanceCounters.Timestamp();
                card.RefreshTag();
                SunExpPerformanceCounters.RecordDuration("CardRefreshQueue.Card.RefreshTag", tagStart);
            }

            if (dataUpdate)
            {
                var dataStart = SunExpPerformanceCounters.Timestamp();
                card.DataUpdate();
                SunExpPerformanceCounters.RecordDuration("CardRefreshQueue.Card.DataUpdate", dataStart);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Queued card refresh skipped from " + source + ": " + ex.Message);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("CardRefreshQueue.Card.Refresh", start);
            LogSlowRefresh("card", source, start);
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
            LogSlowRefresh("config", source, start);
        }
    }

    private static void LogSlowRefresh(string kind, string source, long startTimestamp)
    {
        if (!SunExpPerformanceSettings.CountersEnabled)
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

        SunExpLog.Warn("Slow SunExp card refresh: kind="
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
