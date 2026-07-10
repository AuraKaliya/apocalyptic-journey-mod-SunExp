using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace AuraShared.Core;

public enum AuraSharedFramePhase
{
    CriticalLifecycle = 0,
    GameplayMutation = 100,
    Reconcile = 200,
    Presentation = 300,
    Background = 400
}

public static class AuraSharedFrameScheduler
{
    private const string RunnerName = "AuraShared.FrameScheduler";
    private const string DefaultOwnerId = "AuraShared";
    private static readonly object QueueGate = new();
    private static readonly Queue<FrameAction> CurrentMainThreadActions = new();
    private static readonly Queue<FrameAction> NextMainThreadActions = new();
    private static readonly Queue<KeyedFrameAction> NextKeyedActions = new();
    private static readonly List<KeyedFrameAction> DelayedKeyedActions = new();
    private static readonly SortedDictionary<int, SortedDictionary<int, ReadyKeyedBucket>> ReadyKeyedActions = new();
    private static readonly HashSet<string> PendingKeyedActionKeys = new(StringComparer.Ordinal);
    private static readonly object RunnerGate = new();
    private static FrameSchedulerRunner? runner;
    private static int mainThreadId;
    private static bool isDraining;
    private static long nextSequence;

    public static int MaxActionsPerFrame { get; set; } = 32;

    public static double FrameBudgetMilliseconds { get; set; } = 2.0d;

    public static int OwnerQuantum { get; set; } = 8;

    public static int PendingMainThreadActions
    {
        get
        {
            lock (QueueGate)
            {
                return CurrentMainThreadActions.Count + NextMainThreadActions.Count;
            }
        }
    }

    public static int PendingKeyedActions
    {
        get
        {
            lock (QueueGate)
            {
                return ReadyKeyedActionCountNoLock() + NextKeyedActions.Count + DelayedKeyedActions.Count;
            }
        }
    }

    public static bool Enqueue(string source, Action action)
    {
        if (action == null)
        {
            return false;
        }

        if (EnsureRunner() == null)
        {
            SafeInvoke(source, action);
            return false;
        }

        lock (QueueGate)
        {
            NextMainThreadActions.Enqueue(new FrameAction(source ?? "", action));
        }

        return true;
    }

    public static bool RunAfterFrames(string source, int frames, Action action)
    {
        return RunAfterFramesBudgeted(source, frames, action);
    }

    public static bool RunAfterFramesBudgeted(string source, int frames, Action action)
    {
        if (action == null)
        {
            return false;
        }

        var owner = EnsureRunner();
        if (owner == null)
        {
            SafeInvoke(source, action);
            return false;
        }

        var safeSource = source ?? "";
        owner.StartManagedCoroutine(DelayFrames(safeSource, Math.Max(0, frames), () => Enqueue(safeSource, action)));
        return true;
    }

    public static bool RunOnceNextFrame(AuraSharedFrameActionRequest? request)
    {
        if (request != null)
        {
            request.DelayFrames = 1;
        }

        return RunOnceAfterFrames(request);
    }

    public static bool RunOnceAfterFrames(AuraSharedFrameActionRequest? request)
    {
        if (request?.Action == null)
        {
            return false;
        }

        var normalizedDelay = Math.Max(1, request.DelayFrames);
        var scopedKey = ScopedKey(request.OwnerId, request.Key);
        var source = string.IsNullOrWhiteSpace(request.Source) ? scopedKey : request.Source.Trim();
        var owner = EnsureRunner();
        if (owner == null)
        {
            ExecuteKeyedAction(new KeyedFrameAction(
                scopedKey,
                source,
                request,
                SafeFrameCount(),
                SafeFrameCount(),
                0,
                false),
                immediate: true);
            return true;
        }

        var enqueuedFrame = SafeFrameCount();
        var targetFrame = enqueuedFrame < 0 ? -1 : enqueuedFrame + normalizedDelay;
        KeyedFrameAction scheduled;
        lock (QueueGate)
        {
            var enqueuedDuringDrain = isDraining;
            if (scopedKey.Length > 0 && !PendingKeyedActionKeys.Add(scopedKey))
            {
                request.OnDeduplicated?.Invoke(new AuraSharedFrameActionReport
                {
                    OwnerId = request.OwnerId ?? "",
                    Key = request.Key ?? "",
                    ScopedKey = scopedKey,
                    Source = source,
                    EnqueuedFrame = enqueuedFrame,
                    TargetFrame = targetFrame,
                    ExecuteFrame = SafeFrameCount(),
                    DelayFrames = 0,
                    Phase = NormalizePhase(request.Phase),
                    Priority = NormalizePriority(request.Priority),
                    EstimatedCost = NormalizeEstimatedCost(request.EstimatedCost),
                    EnqueuedDuringDrain = enqueuedDuringDrain,
                    Deduplicated = true
                });
                return false;
            }

            scheduled = new KeyedFrameAction(
                scopedKey,
                source,
                request,
                enqueuedFrame,
                targetFrame,
                ++nextSequence,
                enqueuedDuringDrain);
            NextKeyedActions.Enqueue(scheduled);
        }

        request.OnScheduled?.Invoke(scheduled.ToReport(SafeFrameCount(), immediate: false));
        return true;
    }

    public static bool StartCoroutine(string source, IEnumerator routine)
    {
        if (routine == null)
        {
            return false;
        }

        var owner = EnsureRunner();
        if (owner == null)
        {
            return false;
        }

        owner.StartManagedCoroutine(WrapCoroutine(source ?? "", routine));
        return true;
    }

    public static bool RunBackground<T>(
        string source,
        Func<T> work,
        Action<T>? onCompleted = null,
        Action<Exception>? onFailed = null)
    {
        return AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<T>
        {
            OwnerId = DefaultOwnerId,
            Key = source ?? "",
            Source = source ?? "AuraShared.RunBackground",
            Kind = AuraSharedBackgroundWorkKind.Cpu,
            Work = _ => work(),
            ApplyOnMainThread = result => onCompleted?.Invoke(result),
            OnFailedOnMainThread = error => onFailed?.Invoke(error)
        });
    }

    internal static bool EnsureMainThreadRunner()
    {
        return IsMainThreadOrUninitialized() && EnsureRunner() != null;
    }

    private static FrameSchedulerRunner? EnsureRunner()
    {
        if (!IsMainThreadOrUninitialized())
        {
            return null;
        }

        if (runner != null)
        {
            return runner;
        }

        lock (RunnerGate)
        {
            if (runner != null)
            {
                return runner;
            }

            try
            {
                var existing = GameObject.Find(RunnerName);
                var gameObject = existing != null ? existing : new GameObject(RunnerName);
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
                runner = gameObject.GetComponent<FrameSchedulerRunner>()
                         ?? gameObject.AddComponent<FrameSchedulerRunner>();
                return runner;
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool IsMainThreadOrUninitialized()
    {
        var knownMainThread = Volatile.Read(ref mainThreadId);
        return knownMainThread == 0 || Thread.CurrentThread.ManagedThreadId == knownMainThread;
    }

    private static void Pump()
    {
        var processed = 0;
        var stopwatch = Stopwatch.StartNew();
        var maxActions = Math.Max(1, MaxActionsPerFrame);
        var budgetMs = Math.Max(0.25d, FrameBudgetMilliseconds);
        var frame = SafeFrameCount();
        lock (QueueGate)
        {
            while (NextMainThreadActions.Count > 0)
            {
                CurrentMainThreadActions.Enqueue(NextMainThreadActions.Dequeue());
            }

            PromoteReadyKeyedActions(frame);
            isDraining = true;
        }

        try
        {
            while (processed < maxActions)
            {
                if (TryDequeueKeyedAction(out var keyed))
                {
                    ExecuteKeyedAction(keyed, immediate: false);
                    processed++;
                }
                else if (TryDequeueMainThreadAction(out var item))
                {
                    SafeInvoke(item.Source, item.Action);
                    processed++;
                }
                else
                {
                    break;
                }

                if (processed > 0 && stopwatch.Elapsed.TotalMilliseconds >= budgetMs)
                {
                    break;
                }
            }
        }
        finally
        {
            lock (QueueGate)
            {
                isDraining = false;
            }
        }
    }

    private static bool TryDequeueMainThreadAction(out FrameAction item)
    {
        lock (QueueGate)
        {
            if (CurrentMainThreadActions.Count > 0)
            {
                item = CurrentMainThreadActions.Dequeue();
                return true;
            }
        }

        item = default;
        return false;
    }

    private static bool TryDequeueKeyedAction(out KeyedFrameAction item)
    {
        item = default;
        lock (QueueGate)
        {
            var emptyPhaseKey = int.MinValue;
            var emptyPriorityKey = int.MinValue;
            var removeEmptyBucket = false;
            var foundAction = false;

            foreach (var phasePair in ReadyKeyedActions)
            {
                foreach (var priorityPair in phasePair.Value)
                {
                    var bucket = priorityPair.Value;
                    if (bucket.TryDequeue(out item))
                    {
                        if (item.ScopedKey.Length > 0)
                        {
                            PendingKeyedActionKeys.Remove(item.ScopedKey);
                        }

                        if (bucket.Count == 0)
                        {
                            emptyPhaseKey = phasePair.Key;
                            emptyPriorityKey = priorityPair.Key;
                            removeEmptyBucket = true;
                        }

                        foundAction = true;
                        break;
                    }

                    if (bucket.Count == 0 && !removeEmptyBucket)
                    {
                        emptyPhaseKey = phasePair.Key;
                        emptyPriorityKey = priorityPair.Key;
                        removeEmptyBucket = true;
                    }
                }

                if (foundAction)
                {
                    break;
                }
            }

            if (removeEmptyBucket)
            {
                RemoveReadyBucketNoLock(emptyPhaseKey, emptyPriorityKey);
            }

            if (foundAction)
            {
                return true;
            }
        }

        return false;
    }

    private static void PromoteReadyKeyedActions(int frame)
    {
        while (NextKeyedActions.Count > 0)
        {
            var scheduled = NextKeyedActions.Dequeue();
            if (IsKeyedActionReady(scheduled, frame))
            {
                EnqueueReadyKeyedActionNoLock(scheduled);
            }
            else
            {
                PushDelayedKeyedActionNoLock(scheduled);
            }
        }

        while (DelayedKeyedActions.Count > 0)
        {
            var scheduled = DelayedKeyedActions[0];
            if (!IsKeyedActionReady(scheduled, frame))
            {
                break;
            }

            PopDelayedKeyedActionNoLock();
            EnqueueReadyKeyedActionNoLock(scheduled);
        }
    }

    private static void EnqueueReadyKeyedActionNoLock(KeyedFrameAction scheduled)
    {
        var phaseKey = (int)NormalizePhase(scheduled.Request.Phase);
        var priorityKey = -NormalizePriority(scheduled.Request.Priority);
        if (!ReadyKeyedActions.TryGetValue(phaseKey, out var priorities))
        {
            priorities = new SortedDictionary<int, ReadyKeyedBucket>();
            ReadyKeyedActions[phaseKey] = priorities;
        }

        if (!priorities.TryGetValue(priorityKey, out var bucket))
        {
            bucket = new ReadyKeyedBucket();
            priorities[priorityKey] = bucket;
        }

        bucket.Enqueue(scheduled);
    }

    private static void RemoveReadyBucketNoLock(int phaseKey, int priorityKey)
    {
        if (!ReadyKeyedActions.TryGetValue(phaseKey, out var priorities))
        {
            return;
        }

        priorities.Remove(priorityKey);
        if (priorities.Count == 0)
        {
            ReadyKeyedActions.Remove(phaseKey);
        }
    }

    private static int ReadyKeyedActionCountNoLock()
    {
        var count = 0;
        foreach (var phase in ReadyKeyedActions.Values)
        {
            foreach (var bucket in phase.Values)
            {
                count += bucket.Count;
            }
        }

        return count;
    }

    private static void PushDelayedKeyedActionNoLock(KeyedFrameAction item)
    {
        DelayedKeyedActions.Add(item);
        var index = DelayedKeyedActions.Count - 1;
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (CompareDelayed(DelayedKeyedActions[parent], item) <= 0)
            {
                break;
            }

            DelayedKeyedActions[index] = DelayedKeyedActions[parent];
            index = parent;
        }

        DelayedKeyedActions[index] = item;
    }

    private static KeyedFrameAction PopDelayedKeyedActionNoLock()
    {
        var result = DelayedKeyedActions[0];
        var last = DelayedKeyedActions[DelayedKeyedActions.Count - 1];
        DelayedKeyedActions.RemoveAt(DelayedKeyedActions.Count - 1);
        if (DelayedKeyedActions.Count == 0)
        {
            return result;
        }

        var index = 0;
        while (true)
        {
            var left = index * 2 + 1;
            if (left >= DelayedKeyedActions.Count)
            {
                break;
            }

            var right = left + 1;
            var child = right < DelayedKeyedActions.Count
                        && CompareDelayed(DelayedKeyedActions[right], DelayedKeyedActions[left]) < 0
                ? right
                : left;
            if (CompareDelayed(last, DelayedKeyedActions[child]) <= 0)
            {
                break;
            }

            DelayedKeyedActions[index] = DelayedKeyedActions[child];
            index = child;
        }

        DelayedKeyedActions[index] = last;
        return result;
    }

    private static bool IsKeyedActionReady(KeyedFrameAction scheduled, int frame)
    {
        return scheduled.TargetFrame < 0 || frame < 0 || scheduled.TargetFrame <= frame;
    }

    private static int CompareDelayed(KeyedFrameAction left, KeyedFrameAction right)
    {
        var frame = left.TargetFrame.CompareTo(right.TargetFrame);
        if (frame != 0)
        {
            return frame;
        }

        var phase = ((int)NormalizePhase(left.Request.Phase)).CompareTo((int)NormalizePhase(right.Request.Phase));
        if (phase != 0)
        {
            return phase;
        }

        var priority = NormalizePriority(right.Request.Priority).CompareTo(NormalizePriority(left.Request.Priority));
        return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
    }

    private static IEnumerator DelayFrames(string source, int frames, Action action)
    {
        for (var i = 0; i < frames; i++)
        {
            yield return null;
        }

        SafeInvoke(source, action);
    }

    private static IEnumerator WrapCoroutine(string source, IEnumerator routine)
    {
        while (true)
        {
            object? current;
            try
            {
                if (!routine.MoveNext())
                {
                    yield break;
                }

                current = routine.Current;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[AuraSharedFrameScheduler] Coroutine failed. source="
                                             + source + " -> " + ex.Message);
                yield break;
            }

            yield return current;
        }
    }

    private static void SafeInvoke(string source, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[AuraSharedFrameScheduler] Action failed. source="
                                         + source + " -> " + ex.Message);
        }
    }

    private static void ExecuteKeyedAction(KeyedFrameAction item, bool immediate)
    {
        var executeFrame = SafeFrameCount();
        var report = item.ToReport(executeFrame, immediate);
        item.Request.OnExecuting?.Invoke(report);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            item.Request.Action?.Invoke();
        }
        catch (Exception ex)
        {
            if (item.Request.OnFailed != null)
            {
                item.Request.OnFailed(report, ex);
            }
            else
            {
                UnityEngine.Debug.LogWarning("[AuraSharedFrameScheduler] Action failed. source="
                                             + item.Source + " -> " + ex.Message);
            }
        }
        finally
        {
            stopwatch.Stop();
            report.ActionElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            item.Request.OnExecuted?.Invoke(report);
        }
    }

    private static int SafeFrameCount()
    {
        try
        {
            return Time.frameCount;
        }
        catch
        {
            return -1;
        }
    }

    private static string ScopedKey(string ownerId, string key)
    {
        var normalizedKey = string.IsNullOrWhiteSpace(key) ? "" : key.Trim();
        if (normalizedKey.Length == 0)
        {
            return "";
        }

        var normalizedOwner = string.IsNullOrWhiteSpace(ownerId) ? "" : ownerId.Trim();
        return normalizedOwner.Length == 0 ? normalizedKey : normalizedOwner + ":" + normalizedKey;
    }

    private static AuraSharedFramePhase NormalizePhase(AuraSharedFramePhase phase)
    {
        return Enum.IsDefined(typeof(AuraSharedFramePhase), phase)
            ? phase
            : AuraSharedFramePhase.Presentation;
    }

    private static int NormalizePriority(int priority)
    {
        if (priority < -1000)
        {
            return -1000;
        }

        return priority > 1000 ? 1000 : priority;
    }

    private static int NormalizeEstimatedCost(int estimatedCost)
    {
        if (estimatedCost <= 0)
        {
            return 1;
        }

        return estimatedCost > 64 ? 64 : estimatedCost;
    }

    private static string NormalizeOwnerId(string ownerId)
    {
        return string.IsNullOrWhiteSpace(ownerId) ? DefaultOwnerId : ownerId.Trim();
    }

    private readonly struct FrameAction
    {
        public FrameAction(string source, Action action)
        {
            Source = source;
            Action = action;
        }

        public string Source { get; }

        public Action Action { get; }
    }

    private readonly struct KeyedFrameAction
    {
        public KeyedFrameAction(
            string scopedKey,
            string source,
            AuraSharedFrameActionRequest request,
            int enqueuedFrame,
            int targetFrame,
            long sequence,
            bool enqueuedDuringDrain)
        {
            ScopedKey = scopedKey ?? "";
            Source = source ?? "";
            Request = request;
            EnqueuedFrame = enqueuedFrame;
            TargetFrame = targetFrame;
            Sequence = sequence;
            EnqueuedDuringDrain = enqueuedDuringDrain;
        }

        public string ScopedKey { get; }

        public string Source { get; }

        public AuraSharedFrameActionRequest Request { get; }

        public int EnqueuedFrame { get; }

        public int TargetFrame { get; }

        public long Sequence { get; }

        public bool EnqueuedDuringDrain { get; }

        public AuraSharedFrameActionReport ToReport(int executeFrame, bool immediate)
        {
            return new AuraSharedFrameActionReport
            {
                OwnerId = Request.OwnerId ?? "",
                Key = Request.Key ?? "",
                ScopedKey = ScopedKey,
                Source = Source,
                EnqueuedFrame = EnqueuedFrame,
                TargetFrame = TargetFrame,
                ExecuteFrame = executeFrame,
                DelayFrames = EnqueuedFrame < 0 || executeFrame < 0 ? -1 : executeFrame - EnqueuedFrame,
                Phase = NormalizePhase(Request.Phase),
                Priority = NormalizePriority(Request.Priority),
                EstimatedCost = NormalizeEstimatedCost(Request.EstimatedCost),
                EnqueuedDuringDrain = EnqueuedDuringDrain,
                Scheduled = !immediate,
                Immediate = immediate
            };
        }
    }

    private sealed class ReadyKeyedBucket
    {
        private readonly Dictionary<string, OwnerLane> lanes = new(StringComparer.Ordinal);
        private readonly List<string> ownerOrder = new();
        private int nextOwnerIndex;

        public int Count { get; private set; }

        public void Enqueue(KeyedFrameAction action)
        {
            var owner = NormalizeOwnerId(action.Request.OwnerId);
            if (!lanes.TryGetValue(owner, out var lane))
            {
                lane = new OwnerLane();
                lanes[owner] = lane;
                ownerOrder.Add(owner);
            }

            lane.Actions.Enqueue(action);
            Count++;
        }

        public bool TryDequeue(out KeyedFrameAction action)
        {
            action = default;
            if (Count <= 0 || ownerOrder.Count == 0)
            {
                Count = 0;
                return false;
            }

            var attempts = Math.Max(1, ownerOrder.Count * 2);
            for (var i = 0; i < attempts && Count > 0 && ownerOrder.Count > 0; i++)
            {
                if (nextOwnerIndex >= ownerOrder.Count)
                {
                    nextOwnerIndex = 0;
                }

                var owner = ownerOrder[nextOwnerIndex];
                if (!lanes.TryGetValue(owner, out var lane) || lane.Actions.Count == 0)
                {
                    RemoveCurrentLane(owner);
                    continue;
                }

                lane.Deficit += Math.Max(1, OwnerQuantum);
                var next = lane.Actions.Peek();
                var cost = NormalizeEstimatedCost(next.Request.EstimatedCost);
                if (lane.Deficit < cost)
                {
                    nextOwnerIndex++;
                    continue;
                }

                action = lane.Actions.Dequeue();
                lane.Deficit -= cost;
                Count--;
                if (lane.Actions.Count == 0)
                {
                    RemoveCurrentLane(owner);
                }
                else
                {
                    nextOwnerIndex++;
                }

                return true;
            }

            return false;
        }

        private void RemoveCurrentLane(string owner)
        {
            lanes.Remove(owner);
            if (nextOwnerIndex >= 0 && nextOwnerIndex < ownerOrder.Count)
            {
                ownerOrder.RemoveAt(nextOwnerIndex);
            }
            else
            {
                ownerOrder.Remove(owner);
                if (nextOwnerIndex > ownerOrder.Count)
                {
                    nextOwnerIndex = ownerOrder.Count;
                }
            }
        }
    }

    private sealed class OwnerLane
    {
        public Queue<KeyedFrameAction> Actions { get; } = new();

        public int Deficit { get; set; }
    }

    private sealed class FrameSchedulerRunner : MonoBehaviour
    {
        private void Awake()
        {
            Interlocked.CompareExchange(ref mainThreadId, Thread.CurrentThread.ManagedThreadId, 0);
        }

        public void StartManagedCoroutine(IEnumerator routine)
        {
            StartCoroutine(routine);
        }

        private void Update()
        {
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions();
            Pump();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(runner, this))
            {
                runner = null;
            }
        }
    }
}

public sealed class AuraSharedFrameActionRequest
{
    public string OwnerId { get; set; } = "";

    public string Key { get; set; } = "";

    public string Source { get; set; } = "";

    public int DelayFrames { get; set; } = 1;

    public AuraSharedFramePhase Phase { get; set; } = AuraSharedFramePhase.Presentation;

    public int Priority { get; set; }

    public int EstimatedCost { get; set; } = 1;

    public Action? Action { get; set; }

    public Action<AuraSharedFrameActionReport>? OnScheduled { get; set; }

    public Action<AuraSharedFrameActionReport>? OnDeduplicated { get; set; }

    public Action<AuraSharedFrameActionReport>? OnExecuting { get; set; }

    public Action<AuraSharedFrameActionReport>? OnExecuted { get; set; }

    public Action<AuraSharedFrameActionReport, Exception>? OnFailed { get; set; }
}

public sealed class AuraSharedFrameActionReport
{
    public string OwnerId { get; set; } = "";

    public string Key { get; set; } = "";

    public string ScopedKey { get; set; } = "";

    public string Source { get; set; } = "";

    public int EnqueuedFrame { get; set; }

    public int TargetFrame { get; set; }

    public int ExecuteFrame { get; set; }

    public int DelayFrames { get; set; }

    public AuraSharedFramePhase Phase { get; set; } = AuraSharedFramePhase.Presentation;

    public int Priority { get; set; }

    public int EstimatedCost { get; set; } = 1;

    public bool EnqueuedDuringDrain { get; set; }

    public bool Scheduled { get; set; }

    public bool Immediate { get; set; }

    public bool Deduplicated { get; set; }

    public double ActionElapsedMilliseconds { get; set; }
}
