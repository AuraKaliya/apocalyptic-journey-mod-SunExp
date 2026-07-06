using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace AuraShared.Core;

public static class AuraSharedFrameScheduler
{
    private const string RunnerName = "AuraShared.FrameScheduler";
    private static readonly ConcurrentQueue<FrameAction> MainThreadActions = new();
    private static readonly object RunnerGate = new();
    private static FrameSchedulerRunner? runner;

    public static int MaxActionsPerFrame { get; set; } = 32;

    public static double FrameBudgetMilliseconds { get; set; } = 2.0d;

    public static int PendingMainThreadActions => MainThreadActions.Count;

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

        MainThreadActions.Enqueue(new FrameAction(source ?? "", action));
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
        while (processed < maxActions && MainThreadActions.TryDequeue(out var item))
        {
            SafeInvoke(item.Source, item.Action);
            processed++;
            if (processed > 0 && stopwatch.Elapsed.TotalMilliseconds >= budgetMs)
            {
                break;
            }
        }
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
