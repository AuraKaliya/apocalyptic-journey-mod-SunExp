using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace AuraShared.Core;

public static class AuraSharedFrameScheduler
{
    private const string RunnerName = "AuraShared.FrameScheduler";
    private static readonly object QueueGate = new();
    private static readonly Queue<FrameAction> CurrentMainThreadActions = new();
    private static readonly Queue<FrameAction> NextMainThreadActions = new();
    private static readonly Queue<KeyedFrameAction> CurrentKeyedActions = new();
    private static readonly Queue<KeyedFrameAction> NextKeyedActions = new();
    private static readonly List<KeyedFrameAction> DelayedKeyedActions = new();
    private static readonly HashSet<string> PendingKeyedActionKeys = new(StringComparer.Ordinal);
    private static readonly object RunnerGate = new();
    private static FrameSchedulerRunner? runner;
    private static bool isDraining;

    public static int MaxActionsPerFrame { get; set; } = 32;

    public static double FrameBudgetMilliseconds { get; set; } = 2.0d;

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
                return CurrentKeyedActions.Count + NextKeyedActions.Count + DelayedKeyedActions.Count;
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

        owner.StartManagedCoroutine(DelayFrames(source ?? "", Math.Max(0, frames), action));
        return true;
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
        if (work == null)
        {
            return false;
        }

        if (EnsureRunner() == null)
        {
            try
            {
                var result = work();
                onCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                onFailed?.Invoke(ex);
            }

            return false;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var result = work();
                Enqueue(source + ":complete", () => onCompleted?.Invoke(result));
            }
            catch (Exception ex)
            {
                Enqueue(source + ":failed", () => onFailed?.Invoke(ex));
            }
        });
        return true;
    }

    private static FrameSchedulerRunner? EnsureRunner()
    {
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
        lock (QueueGate)
        {
            if (CurrentKeyedActions.Count > 0)
            {
                item = CurrentKeyedActions.Dequeue();
                if (item.ScopedKey.Length > 0)
                {
                    PendingKeyedActionKeys.Remove(item.ScopedKey);
                }

                return true;
            }
        }

        item = default;
        return false;
    }

    private static void PromoteReadyKeyedActions(int frame)
    {
        while (NextKeyedActions.Count > 0)
        {
            var scheduled = NextKeyedActions.Dequeue();
            if (IsKeyedActionReady(scheduled, frame))
            {
                CurrentKeyedActions.Enqueue(scheduled);
            }
            else
            {
                DelayedKeyedActions.Add(scheduled);
            }
        }

        for (var i = DelayedKeyedActions.Count - 1; i >= 0; i--)
        {
            var scheduled = DelayedKeyedActions[i];
            if (!IsKeyedActionReady(scheduled, frame))
            {
                continue;
            }

            DelayedKeyedActions.RemoveAt(i);
            CurrentKeyedActions.Enqueue(scheduled);
        }
    }

    private static bool IsKeyedActionReady(KeyedFrameAction scheduled, int frame)
    {
        return scheduled.TargetFrame < 0 || frame < 0 || scheduled.TargetFrame <= frame;
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
            bool enqueuedDuringDrain)
        {
            ScopedKey = scopedKey ?? "";
            Source = source ?? "";
            Request = request;
            EnqueuedFrame = enqueuedFrame;
            TargetFrame = targetFrame;
            EnqueuedDuringDrain = enqueuedDuringDrain;
        }

        public string ScopedKey { get; }

        public string Source { get; }

        public AuraSharedFrameActionRequest Request { get; }

        public int EnqueuedFrame { get; }

        public int TargetFrame { get; }

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
                EnqueuedDuringDrain = EnqueuedDuringDrain,
                Scheduled = !immediate,
                Immediate = immediate
            };
        }
    }

    private sealed class FrameSchedulerRunner : MonoBehaviour
    {
        public void StartManagedCoroutine(IEnumerator routine)
        {
            StartCoroutine(routine);
        }

        private void Update()
        {
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

    public bool EnqueuedDuringDrain { get; set; }

    public bool Scheduled { get; set; }

    public bool Immediate { get; set; }

    public bool Deduplicated { get; set; }

    public double ActionElapsedMilliseconds { get; set; }
}
