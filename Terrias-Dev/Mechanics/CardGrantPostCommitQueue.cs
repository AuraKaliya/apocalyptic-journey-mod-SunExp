using System;
using System.Collections.Generic;
using System.Reflection;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public sealed class CardGrantPostCommitRequest
{
    public IDataConfig? Config { get; set; }

    public string Source { get; set; } = "";

    public bool RefreshTags { get; set; }

    public bool RefreshVisuals { get; set; }

    public bool DataUpdate { get; set; }
}

public static class CardGrantPostCommitQueue
{
    private const bool CombatVisualPostCommitRefreshEnabled = false;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PendingPostCommit> Pending = new(StringComparer.Ordinal);
    private static PropertyInfo? frameCountProperty;
    private static bool frameCountResolved;
    private static int FlushBudgetPerFrame => Math.Max(4, TerriasPerformanceSettings.FrameSchedulerBudget / 2);

    public static void Request(CardGrantPostCommitRequest? request)
    {
        if (request?.Config == null)
        {
            return;
        }

        var key = ConfigKey(request.Config);
        var requestFrame = FrameCount();
        if (request.RefreshVisuals)
        {
            TerriasPerformanceCounters.Record("CardGrantPostCommitQueue.RequestVisuals");
        }

        if (request.RefreshTags)
        {
            TerriasPerformanceCounters.Record("CardGrantPostCommitQueue.RequestTags");
        }

        if (key.Length == 0)
        {
            FlushOne(new PendingPostCommit(
                request.Config,
                request.Source,
                request.RefreshTags,
                request.RefreshVisuals,
                request.DataUpdate,
                0,
                requestFrame,
                -1));
            return;
        }

        lock (SyncRoot)
        {
            Pending[key] = Pending.TryGetValue(key, out var existing)
                ? existing.Merge(request)
                : new PendingPostCommit(
                    request.Config,
                    request.Source,
                    request.RefreshTags,
                    request.RefreshVisuals,
                    request.DataUpdate,
                    0,
                    requestFrame,
                    -1);
        }

        ScheduleFlush();
    }

    public static void RequestVisualRefresh(IDataConfig? config, string source)
    {
        Request(new CardGrantPostCommitRequest
        {
            Config = config,
            Source = source,
            RefreshVisuals = true
        });
    }

    public static void RequestTagRefresh(IDataConfig? config, string source)
    {
        Request(new CardGrantPostCommitRequest
        {
            Config = config,
            Source = source,
            RefreshTags = true
        });
    }

    public static void RequestFullRefresh(IDataConfig? config, string source)
    {
        Request(new CardGrantPostCommitRequest
        {
            Config = config,
            Source = source,
            RefreshTags = true,
            RefreshVisuals = true,
            DataUpdate = true
        });
    }

    private static void ScheduleFlush()
    {
        if (!TerriasFrameDispatcher.RunOnceNextFrame("CardGrantPostCommitQueue.Flush", Flush))
        {
            TerriasPerformanceCounters.Record("CardGrantPostCommitQueue.Deduped");
        }
    }

    private static void Flush()
    {
        PendingPostCommit[] items;
        lock (SyncRoot)
        {
            if (Pending.Count == 0)
            {
                return;
            }

            var count = Math.Min(Pending.Count, FlushBudgetPerFrame);
            items = new PendingPostCommit[count];
            var keys = new string[count];
            var index = 0;
            foreach (var pair in Pending)
            {
                items[index] = pair.Value;
                keys[index] = pair.Key;
                index++;
                if (index >= count)
                {
                    break;
                }
            }

            for (var i = 0; i < index; i++)
            {
                Pending.Remove(keys[i]);
            }
        }

        var start = TerriasPerformanceCounters.Timestamp();
        foreach (var item in items)
        {
            FlushOne(item);
        }

        TerriasPerformanceCounters.RecordDuration("CardGrantPostCommitQueue.Flush", start);

        bool hasMore;
        lock (SyncRoot)
        {
            hasMore = Pending.Count > 0;
        }

        if (hasMore)
        {
            TerriasPerformanceCounters.Record("CardGrantPostCommitQueue.FlushContinued");
            ScheduleFlush();
        }
    }

    private static void FlushOne(PendingPostCommit item)
    {
        try
        {
            if (item.RefreshTags)
            {
                TerriasCardRefreshQueue.RequestConfigTagRefresh(item.Config, "CardGrantPostCommit:" + item.Source);
            }

            if (!item.DataUpdate)
            {
                if (item.RefreshVisuals)
                {
                    RecordSuppressedVisualRefresh(item);
                }

                return;
            }

            var root = TerriasCardPresentationRouter.FindCombatCardRoot(item.Config);
            if (root != null)
            {
                if (item.DataUpdate)
                {
                    var card = root.GetComponent<CardItem>();
                    TerriasCardRefreshQueue.RequestDataUpdate(card, "CardGrantPostCommit:" + item.Source);
                }

                if (item.RefreshVisuals)
                {
                    RecordSuppressedVisualRefresh(item);
                }

                return;
            }

            if (item.RefreshVisuals)
            {
                RecordSuppressedVisualRefresh(item);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Card grant post-commit flush failed from " + item.Source, ex);
        }
    }

    private static void RecordSuppressedVisualRefresh(PendingPostCommit item)
    {
        if (!CombatVisualPostCommitRefreshEnabled)
        {
            TerriasPerformanceCounters.Record("CardGrantPostCommitQueue.VisualRefreshSuppressed");
        }
    }

    private static int FrameCount()
    {
        try
        {
            if (!frameCountResolved)
            {
                var timeType = Type.GetType("UnityEngine.Time, UnityEngine.CoreModule")
                    ?? Type.GetType("UnityEngine.Time, UnityEngine");
                frameCountProperty = timeType?.GetProperty("frameCount", BindingFlags.Public | BindingFlags.Static);
                frameCountResolved = true;
            }

            var value = frameCountProperty?.GetValue(null, null);
            return value == null ? -1 : Convert.ToInt32(value);
        }
        catch
        {
            return -1;
        }
    }

    private static string ConfigKey(IDataConfig config)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(config.InstanceID))
            {
                return config.InstanceID;
            }
        }
        catch
        {
            // Fall through to object hash.
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

    private readonly struct PendingPostCommit
    {
        public PendingPostCommit(
            IDataConfig config,
            string source,
            bool refreshTags,
            bool refreshVisuals,
            bool dataUpdate,
            int attempts,
            int createdFrame,
            int lastAttemptFrame)
        {
            Config = config;
            Source = source ?? "";
            RefreshTags = refreshTags;
            RefreshVisuals = refreshVisuals;
            DataUpdate = dataUpdate;
            Attempts = Math.Max(0, attempts);
            CreatedFrame = createdFrame;
            LastAttemptFrame = lastAttemptFrame;
        }

        public IDataConfig Config { get; }

        public string Source { get; }

        public bool RefreshTags { get; }

        public bool RefreshVisuals { get; }

        public bool DataUpdate { get; }

        public int Attempts { get; }

        public int CreatedFrame { get; }

        public int LastAttemptFrame { get; }

        public PendingPostCommit Merge(CardGrantPostCommitRequest request)
        {
            return new PendingPostCommit(
                request.Config ?? Config,
                string.IsNullOrWhiteSpace(request.Source) ? Source : request.Source,
                RefreshTags || request.RefreshTags,
                RefreshVisuals || request.RefreshVisuals,
                DataUpdate || request.DataUpdate,
                Attempts,
                CreatedFrame,
                LastAttemptFrame);
        }

        public PendingPostCommit Merge(PendingPostCommit request)
        {
            var createdFrame = CreatedFrame < 0
                ? request.CreatedFrame
                : request.CreatedFrame < 0
                    ? CreatedFrame
                    : Math.Min(CreatedFrame, request.CreatedFrame);
            return new PendingPostCommit(
                Config,
                string.IsNullOrWhiteSpace(request.Source) ? Source : request.Source,
                RefreshTags || request.RefreshTags,
                RefreshVisuals || request.RefreshVisuals,
                DataUpdate || request.DataUpdate,
                Math.Max(Attempts, request.Attempts),
                createdFrame,
                Math.Max(LastAttemptFrame, request.LastAttemptFrame));
        }
    }
}
