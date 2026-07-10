using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace AuraShared.Core;

public enum AuraSharedBackgroundWorkKind
{
    Cpu,
    Io
}

public sealed class AuraSharedBackgroundWorkRequest<T>
{
    public string OwnerId { get; set; } = "";

    public string Key { get; set; } = "";

    public string Source { get; set; } = "";

    public AuraSharedBackgroundWorkKind Kind { get; set; } = AuraSharedBackgroundWorkKind.Cpu;

    public int CompletionPriority { get; set; }

    public Func<CancellationToken, T>? Work { get; set; }

    public Func<bool>? IsStillCurrent { get; set; }

    public Action<T>? ApplyOnMainThread { get; set; }

    public Action<Exception>? OnFailedOnMainThread { get; set; }
}

public static class AuraSharedBackgroundWorkScheduler
{
    private const string DefaultOwnerId = "AuraShared";
    private const int DefaultOwnerPendingLimit = 4;
    private static readonly object Gate = new();
    private static readonly LinkedList<WorkItem> CpuQueue = new();
    private static readonly LinkedList<WorkItem> IoQueue = new();
    private static readonly Dictionary<string, WorkItem> QueuedByScopedKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LatestGenerationByScopedKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> PendingByOwner = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<Completion> Completions = new();
    private static int activeCpu;
    private static int activeIo;
    private static long nextGeneration;

    public static int MaxCpuConcurrency { get; set; } = 1;

    public static int MaxIoConcurrency { get; set; } = 1;

    public static int MaxPendingCpu { get; set; } = 16;

    public static int MaxPendingIo { get; set; } = 8;

    public static int MaxPendingPerOwner { get; set; } = DefaultOwnerPendingLimit;

    public static int MaxCompletionsPerFrame { get; set; } = 4;

    public static int PendingCpuCount
    {
        get
        {
            lock (Gate)
            {
                return CpuQueue.Count;
            }
        }
    }

    public static int PendingIoCount
    {
        get
        {
            lock (Gate)
            {
                return IoQueue.Count;
            }
        }
    }

    public static bool Queue<T>(AuraSharedBackgroundWorkRequest<T>? request)
    {
        if (request?.Work == null || request.ApplyOnMainThread == null)
        {
            return false;
        }

        if (!AuraSharedFrameScheduler.EnsureMainThreadRunner())
        {
            return false;
        }

        var item = new WorkItem<T>(request);
        List<WorkItem> launch;
        lock (Gate)
        {
            var queue = item.Kind == AuraSharedBackgroundWorkKind.Io ? IoQueue : CpuQueue;
            var queueLimit = item.Kind == AuraSharedBackgroundWorkKind.Io
                ? Math.Max(1, MaxPendingIo)
                : Math.Max(1, MaxPendingCpu);
            if (item.ScopedKey.Length > 0 && QueuedByScopedKey.TryGetValue(item.ScopedKey, out var previous))
            {
                RemoveQueuedItemNoLock(previous, cancel: true);
            }

            if (queue.Count >= queueLimit || OwnerPendingCountNoLock(item.OwnerId) >= Math.Max(1, MaxPendingPerOwner))
            {
                return false;
            }

            if (item.ScopedKey.Length > 0)
            {
                item.Generation = ++nextGeneration;
                LatestGenerationByScopedKey[item.ScopedKey] = item.Generation;
            }

            item.Node = queue.AddLast(item);
            if (item.ScopedKey.Length > 0)
            {
                QueuedByScopedKey[item.ScopedKey] = item;
            }
            IncrementOwnerNoLock(item.OwnerId);
            launch = StartAvailableNoLock();
        }

        Launch(launch);
        return true;
    }

    internal static void PumpMainThreadCompletions()
    {
        var limit = Math.Max(1, MaxCompletionsPerFrame);
        while (limit-- > 0 && Completions.TryDequeue(out var completion))
        {
            AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
            {
                OwnerId = completion.OwnerId,
                Source = completion.Source + ":apply",
                DelayFrames = 1,
                Phase = AuraSharedFramePhase.Reconcile,
                Priority = completion.Priority,
                Action = completion.Apply
            });
        }
    }

    private static void RunWork(WorkItem item)
    {
        Completion? completion = null;
        try
        {
            completion = item.Run();
        }
        finally
        {
            List<WorkItem> launch;
            lock (Gate)
            {
                if (item.Kind == AuraSharedBackgroundWorkKind.Io)
                {
                    activeIo = Math.Max(0, activeIo - 1);
                }
                else
                {
                    activeCpu = Math.Max(0, activeCpu - 1);
                }

                launch = StartAvailableNoLock();
            }

            if (completion != null)
            {
                Completions.Enqueue(completion);
            }

            Launch(launch);
        }
    }

    private static List<WorkItem> StartAvailableNoLock()
    {
        var launch = new List<WorkItem>();
        StartAvailableNoLock(CpuQueue, AuraSharedBackgroundWorkKind.Cpu, ref activeCpu, Math.Max(1, MaxCpuConcurrency), launch);
        StartAvailableNoLock(IoQueue, AuraSharedBackgroundWorkKind.Io, ref activeIo, Math.Max(1, MaxIoConcurrency), launch);
        return launch;
    }

    private static void StartAvailableNoLock(
        LinkedList<WorkItem> queue,
        AuraSharedBackgroundWorkKind kind,
        ref int active,
        int concurrency,
        List<WorkItem> launch)
    {
        while (active < concurrency && queue.First != null)
        {
            var item = queue.First.Value;
            RemoveQueuedItemNoLock(item, cancel: false);
            if (item.Kind != kind || item.Cancellation.IsCancellationRequested)
            {
                continue;
            }

            active++;
            launch.Add(item);
        }
    }

    private static void RemoveQueuedItemNoLock(WorkItem item, bool cancel)
    {
        if (item.Node != null)
        {
            item.Node.List?.Remove(item.Node);
            item.Node = null;
            DecrementOwnerNoLock(item.OwnerId);
        }

        if (item.ScopedKey.Length > 0
            && QueuedByScopedKey.TryGetValue(item.ScopedKey, out var current)
            && ReferenceEquals(current, item))
        {
            QueuedByScopedKey.Remove(item.ScopedKey);
        }

        if (cancel)
        {
            item.Cancellation.Cancel();
        }
    }

    private static void Launch(IEnumerable<WorkItem> items)
    {
        foreach (var item in items)
        {
            ThreadPool.QueueUserWorkItem(_ => RunWork(item));
        }
    }

    private static bool IsLatest(string scopedKey, long generation)
    {
        if (scopedKey.Length == 0)
        {
            return true;
        }

        lock (Gate)
        {
            return LatestGenerationByScopedKey.TryGetValue(scopedKey, out var latest) && latest == generation;
        }
    }

    private static void ReleaseLatest(string scopedKey, long generation)
    {
        if (scopedKey.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            if (LatestGenerationByScopedKey.TryGetValue(scopedKey, out var latest) && latest == generation)
            {
                LatestGenerationByScopedKey.Remove(scopedKey);
            }
        }
    }

    private static int OwnerPendingCountNoLock(string ownerId)
    {
        return PendingByOwner.TryGetValue(ownerId, out var count) ? count : 0;
    }

    private static void IncrementOwnerNoLock(string ownerId)
    {
        PendingByOwner[ownerId] = OwnerPendingCountNoLock(ownerId) + 1;
    }

    private static void DecrementOwnerNoLock(string ownerId)
    {
        var next = OwnerPendingCountNoLock(ownerId) - 1;
        if (next <= 0)
        {
            PendingByOwner.Remove(ownerId);
        }
        else
        {
            PendingByOwner[ownerId] = next;
        }
    }

    private abstract class WorkItem
    {
        protected WorkItem(string ownerId, string key, string source, AuraSharedBackgroundWorkKind kind, int priority)
        {
            OwnerId = string.IsNullOrWhiteSpace(ownerId) ? DefaultOwnerId : ownerId.Trim();
            Key = key?.Trim() ?? "";
            Source = string.IsNullOrWhiteSpace(source) ? OwnerId + ".BackgroundWork" : source.Trim();
            Kind = kind;
            Priority = priority;
            ScopedKey = Key.Length == 0 ? "" : OwnerId + ":" + Key;
        }

        public string OwnerId { get; }

        public string Key { get; }

        public string ScopedKey { get; }

        public string Source { get; }

        public AuraSharedBackgroundWorkKind Kind { get; }

        public int Priority { get; }

        public CancellationTokenSource Cancellation { get; } = new();

        public long Generation { get; set; }

        public LinkedListNode<WorkItem>? Node { get; set; }

        public abstract Completion? Run();
    }

    private sealed class WorkItem<T> : WorkItem
    {
        private readonly AuraSharedBackgroundWorkRequest<T> request;

        public WorkItem(AuraSharedBackgroundWorkRequest<T> request)
            : base(request.OwnerId, request.Key, request.Source, request.Kind, request.CompletionPriority)
        {
            this.request = request;
        }

        public override Completion? Run()
        {
            try
            {
                if (Cancellation.IsCancellationRequested)
                {
                    return null;
                }

                var result = request.Work!(Cancellation.Token);
                if (Cancellation.IsCancellationRequested)
                {
                    return null;
                }

                return new Completion(OwnerId, Source, Priority, () =>
                {
                    if (!IsLatest(ScopedKey, Generation))
                    {
                        return;
                    }

                    if (request.IsStillCurrent?.Invoke() == false)
                    {
                        ReleaseLatest(ScopedKey, Generation);
                        return;
                    }

                    try
                    {
                        request.ApplyOnMainThread!(result);
                    }
                    finally
                    {
                        ReleaseLatest(ScopedKey, Generation);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new Completion(OwnerId, Source, Priority, () =>
                {
                    if (!IsLatest(ScopedKey, Generation))
                    {
                        return;
                    }

                    try
                    {
                        request.OnFailedOnMainThread?.Invoke(ex);
                    }
                    finally
                    {
                        ReleaseLatest(ScopedKey, Generation);
                    }
                });
            }
        }
    }

    private sealed class Completion
    {
        public Completion(string ownerId, string source, int priority, Action apply)
        {
            OwnerId = ownerId;
            Source = source;
            Priority = priority;
            Apply = apply;
        }

        public string OwnerId { get; }

        public string Source { get; }

        public int Priority { get; }

        public Action Apply { get; }
    }
}
