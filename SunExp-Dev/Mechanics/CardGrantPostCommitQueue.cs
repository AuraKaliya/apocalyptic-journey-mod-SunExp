using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

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
    private const int MaterializeRetryBudget = 40;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PendingPostCommit> Pending = new(StringComparer.Ordinal);
    private static int FlushBudgetPerFrame => Math.Max(4, SunExpPerformanceSettings.FrameSchedulerBudget / 2);

    public static void Request(CardGrantPostCommitRequest? request)
    {
        if (request?.Config == null)
        {
            return;
        }

        var key = ConfigKey(request.Config);
        if (key.Length == 0)
        {
            FlushOne(new PendingPostCommit(request.Config, request.Source, request.RefreshTags, request.RefreshVisuals, request.DataUpdate, 0));
            return;
        }

        lock (SyncRoot)
        {
            Pending[key] = Pending.TryGetValue(key, out var existing)
                ? existing.Merge(request)
                : new PendingPostCommit(request.Config, request.Source, request.RefreshTags, request.RefreshVisuals, request.DataUpdate, 0);
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
        if (!SunExpFrameDispatcher.RunOnceNextFrame("CardGrantPostCommitQueue.Flush", Flush))
        {
            SunExpPerformanceCounters.Record("CardGrantPostCommitQueue.Deduped");
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

        var start = SunExpPerformanceCounters.Timestamp();
        foreach (var item in items)
        {
            FlushOne(item);
        }

        SunExpPerformanceCounters.RecordDuration("CardGrantPostCommitQueue.Flush", start);

        bool hasMore;
        lock (SyncRoot)
        {
            hasMore = Pending.Count > 0;
        }

        if (hasMore)
        {
            SunExpPerformanceCounters.Record("CardGrantPostCommitQueue.FlushContinued");
            ScheduleFlush();
        }
    }

    private static void FlushOne(PendingPostCommit item)
    {
        try
        {
            if (item.RefreshTags)
            {
                SunExpCardRefreshQueue.RequestConfigTagRefresh(item.Config, "CardGrantPostCommit:" + item.Source);
            }

            var root = SunExpCardPresentationRouter.FindCombatCardRoot(item.Config);
            if (root != null)
            {
                if (item.DataUpdate)
                {
                    var card = root.GetComponent<CardItem>();
                    SunExpCardRefreshQueue.RequestDataUpdate(card, "CardGrantPostCommit:" + item.Source);
                }

                if (item.RefreshVisuals)
                {
                    SunExpCardPresentationRouter.RequestApply(
                        root,
                        item.Config,
                        "CardGrantPostCommit:" + item.Source,
                        SunExpCardPresentationSurface.PostCommit);
                }

                return;
            }

            if (item.RefreshVisuals)
            {
                if (item.Attempts < MaterializeRetryBudget)
                {
                    Requeue(item.NextMaterializeAttempt());
                    return;
                }

                SunExpPerformanceCounters.Record("CardGrantPostCommitQueue.VisualRootMiss");
                SunExpLog.Warn("Card grant post-commit visual root missing: cardId="
                    + CardConfigApi.Id(item.Config)
                    + ", source="
                    + item.Source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card grant post-commit flush failed from " + item.Source, ex);
        }
    }

    private static void Requeue(PendingPostCommit item)
    {
        var key = ConfigKey(item.Config);
        if (key.Length == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            Pending[key] = Pending.TryGetValue(key, out var existing)
                ? existing.Merge(item)
                : item;
        }

        ScheduleFlush();
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
        public PendingPostCommit(IDataConfig config, string source, bool refreshTags, bool refreshVisuals, bool dataUpdate, int attempts)
        {
            Config = config;
            Source = source ?? "";
            RefreshTags = refreshTags;
            RefreshVisuals = refreshVisuals;
            DataUpdate = dataUpdate;
            Attempts = Math.Max(0, attempts);
        }

        public IDataConfig Config { get; }

        public string Source { get; }

        public bool RefreshTags { get; }

        public bool RefreshVisuals { get; }

        public bool DataUpdate { get; }

        public int Attempts { get; }

        public PendingPostCommit Merge(CardGrantPostCommitRequest request)
        {
            return new PendingPostCommit(
                request.Config ?? Config,
                string.IsNullOrWhiteSpace(request.Source) ? Source : request.Source,
                RefreshTags || request.RefreshTags,
                RefreshVisuals || request.RefreshVisuals,
                DataUpdate || request.DataUpdate,
                Attempts);
        }

        public PendingPostCommit Merge(PendingPostCommit request)
        {
            return new PendingPostCommit(
                Config,
                string.IsNullOrWhiteSpace(request.Source) ? Source : request.Source,
                RefreshTags || request.RefreshTags,
                RefreshVisuals || request.RefreshVisuals,
                DataUpdate || request.DataUpdate,
                Math.Max(Attempts, request.Attempts));
        }

        public PendingPostCommit NextMaterializeAttempt()
        {
            return new PendingPostCommit(Config, Source, false, RefreshVisuals, DataUpdate, Attempts + 1);
        }
    }
}
