using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AuraShared.Core;

public enum AuraSharedBackgroundWorkKind
{
    Cpu,
    Io
}

public enum AuraSharedWorkAdmission
{
    Accepted,
    Replaced,
    BackPressure,
    InvalidRequest,
    RunnerUnavailable
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

    public Action<string>? OnCancelledOnMainThread { get; set; }
}

public static class AuraSharedBackgroundWorkScheduler
{
    private const string DefaultOwnerId = "AuraShared";
    private const int DefaultOwnerPendingLimit = 4;
    private static readonly object Gate = new();
    private static readonly LinkedList<WorkItem> CpuQueue = new();
    private static readonly LinkedList<WorkItem> IoQueue = new();
    private static readonly Dictionary<string, WorkItem> QueuedByScopedKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, WorkItem> ActiveByScopedKey = new(StringComparer.Ordinal);
    private static readonly HashSet<WorkItem> ActiveItems = new();
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
        var result = TryQueue(request);
        return result == AuraSharedWorkAdmission.Accepted || result == AuraSharedWorkAdmission.Replaced;
    }

    // Admission and replacement commit together. A rejected replacement must
    // leave the previously accepted owner and its completion intact.
    public static AuraSharedWorkAdmission TryQueue<T>(AuraSharedBackgroundWorkRequest<T>? request)
    {
        if (request?.Work == null || request.ApplyOnMainThread == null)
        {
            return AuraSharedWorkAdmission.InvalidRequest;
        }

        if (!AuraSharedFrameScheduler.EnsureMainThreadRunner())
        {
            return AuraSharedWorkAdmission.RunnerUnavailable;
        }

        var item = new WorkItem<T>(request);
        List<WorkItem> launch;
        var replaced = false;
        lock (Gate)
        {
            var queue = item.Kind == AuraSharedBackgroundWorkKind.Io ? IoQueue : CpuQueue;
            var queueLimit = item.Kind == AuraSharedBackgroundWorkKind.Io
                ? Math.Max(1, MaxPendingIo)
                : Math.Max(1, MaxPendingCpu);
            WorkItem? previous = null;
            WorkItem? activePrevious = null;
            if (item.ScopedKey.Length > 0)
            {
                QueuedByScopedKey.TryGetValue(item.ScopedKey, out previous);
                ActiveByScopedKey.TryGetValue(item.ScopedKey, out activePrevious);
            }
            var releasedQueueSlot = previous?.Kind == item.Kind ? 1 : 0;
            var releasedOwnerSlot = previous != null ? 1 : 0;
            if (queue.Count - releasedQueueSlot >= queueLimit
                || OwnerPendingCountNoLock(item.OwnerId) - releasedOwnerSlot >= Math.Max(1, MaxPendingPerOwner))
            {
                return AuraSharedWorkAdmission.BackPressure;
            }
            if (previous != null)
            {
                RemoveQueuedItemNoLock(previous, cancel: true, cancellationReason: "superseded");
                replaced = true;
            }
            if (activePrevious != null)
            {
                activePrevious.Cancel("superseded");
                replaced = true;
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
        return replaced ? AuraSharedWorkAdmission.Replaced : AuraSharedWorkAdmission.Accepted;
    }

    public static int CancelOwner(string ownerId)
    {
        var owner = string.IsNullOrWhiteSpace(ownerId)
            ? DefaultOwnerId
            : ownerId.Trim();
        var cancelled = 0;
        lock (Gate)
        {
            var queued = CpuQueue
                .Concat(IoQueue)
                .Where(item => string.Equals(
                    item.OwnerId,
                    owner,
                    StringComparison.Ordinal))
                .ToArray();
            for (var i = 0; i < queued.Length; i++)
            {
                RemoveQueuedItemNoLock(queued[i], cancel: true, cancellationReason: "owner-cancelled");
                cancelled++;
            }

            foreach (var item in ActiveItems.Where(item => string.Equals(
                         item.OwnerId,
                         owner,
                         StringComparison.Ordinal)).ToArray())
            {
                item.Cancel("owner-cancelled");
                cancelled++;
            }

            var prefix = owner + ":";
            foreach (var key in LatestGenerationByScopedKey.Keys
                         .Where(key => key.StartsWith(
                             prefix,
                             StringComparison.Ordinal))
                         .ToArray())
            {
                LatestGenerationByScopedKey.Remove(key);
            }
        }

        return cancelled;
    }

    internal static void PumpMainThreadCompletions()
    {
        var limit = Math.Max(1, MaxCompletionsPerFrame);
        while (limit-- > 0 && Completions.TryDequeue(out var completion))
        {
            if (!AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
            {
                OwnerId = completion.OwnerId,
                Source = completion.Source + ":apply",
                DelayFrames = 1,
                Phase = AuraSharedFramePhase.Reconcile,
                Priority = completion.Priority,
                Action = completion.Apply
            }))
            {
                Completions.Enqueue(completion);
                break;
            }
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
                ActiveItems.Remove(item);
                if (item.ScopedKey.Length > 0
                    && ActiveByScopedKey.TryGetValue(
                        item.ScopedKey,
                        out var active)
                    && ReferenceEquals(active, item))
                {
                    ActiveByScopedKey.Remove(item.ScopedKey);
                }
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
            ActiveItems.Add(item);
            if (item.ScopedKey.Length > 0)
            {
                ActiveByScopedKey[item.ScopedKey] = item;
            }
            launch.Add(item);
        }
    }

    private static void RemoveQueuedItemNoLock(
        WorkItem item,
        bool cancel,
        string cancellationReason = "cancelled")
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
            item.Cancel(cancellationReason);
            var completion = item.CreateCancellationCompletion();
            if (completion != null) Completions.Enqueue(completion);
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

        public string CancellationReason { get; private set; } = "cancelled";

        public long Generation { get; set; }

        public LinkedListNode<WorkItem>? Node { get; set; }

        public abstract Completion? Run();

        public abstract Completion? CreateCancellationCompletion();

        public void Cancel(string reason)
        {
            CancellationReason = string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason.Trim();
            Cancellation.Cancel();
        }
    }

    private sealed class WorkItem<T> : WorkItem
    {
        private readonly AuraSharedBackgroundWorkRequest<T> request;
        private int cancellationCompletionCreated;

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
                    return CreateCancellationCompletion();
                }

                var result = request.Work!(Cancellation.Token);
                if (Cancellation.IsCancellationRequested)
                {
                    return CreateCancellationCompletion();
                }

                return new Completion(OwnerId, Source, Priority, () =>
                {
                    if (!IsLatest(ScopedKey, Generation))
                    {
                        CreateCancellationCompletion()?.Apply();
                        return;
                    }

                    if (request.IsStillCurrent?.Invoke() == false)
                    {
                        ReleaseLatest(ScopedKey, Generation);
                        CreateCancellationCompletion()?.Apply();
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
                return CreateCancellationCompletion();
            }
            catch (Exception ex)
            {
                return new Completion(OwnerId, Source, Priority, () =>
                {
                    if (!IsLatest(ScopedKey, Generation))
                    {
                        CreateCancellationCompletion()?.Apply();
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

        public override Completion? CreateCancellationCompletion()
        {
            if (request.OnCancelledOnMainThread == null
                || Interlocked.Exchange(ref cancellationCompletionCreated, 1) != 0)
            {
                return null;
            }
            var reason = CancellationReason;
            return new Completion(
                OwnerId,
                Source,
                Priority,
                () => request.OnCancelledOnMainThread?.Invoke(reason));
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
