using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpFrameScheduler
{
    private static SchedulerRunner? runner;
    private static bool createFailureLogged;

    public static void Initialize(ModConfig modConfig)
    {
        EnsureRunner();
        SunExpFrameDispatcher.Register(RunOnceNextFrame);
        SunExpLog.Info("SunExp performance frame scheduler initialized");
    }

    public static bool RunOnceNextFrame(string key, Action action)
    {
        if (action == null)
        {
            return false;
        }

        var current = EnsureRunner();
        if (current == null)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                SunExpLog.Error("Immediate SunExp frame action failed: " + key, ex);
            }

            return true;
        }

        return current.Enqueue(key, action);
    }

    private static SchedulerRunner? EnsureRunner()
    {
        if (runner != null)
        {
            return runner;
        }

        try
        {
            var go = new GameObject("SunExp_PerformanceRuntime");
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<SchedulerRunner>();
            return runner;
        }
        catch (Exception ex)
        {
            if (!createFailureLogged)
            {
                SunExpLog.Warn("SunExp frame scheduler unavailable; falling back to immediate execution: " + ex.Message);
                createFailureLogged = true;
            }

            return null;
        }
    }

    private readonly struct ScheduledAction
    {
        public ScheduledAction(string key, Action action)
        {
            Key = key;
            Action = action;
        }

        public string Key { get; }

        public Action Action { get; }
    }

    private sealed class SchedulerRunner : MonoBehaviour
    {
        private readonly Queue<ScheduledAction> queue = new();
        private readonly HashSet<string> pendingKeys = new(StringComparer.Ordinal);

        public bool Enqueue(string key, Action action)
        {
            var normalizedKey = (key ?? "").Trim();
            lock (queue)
            {
                if (normalizedKey.Length > 0 && !pendingKeys.Add(normalizedKey))
                {
                    SunExpPerformanceCounters.Record("FrameScheduler.Deduped");
                    return false;
                }

                queue.Enqueue(new ScheduledAction(normalizedKey, action));
            }

            SunExpPerformanceCounters.Record("FrameScheduler.Enqueued");
            return true;
        }

        private void Update()
        {
            var budget = Math.Max(1, SunExpPerformanceSettings.FrameSchedulerBudget);
            for (var i = 0; i < budget; i++)
            {
                ScheduledAction scheduled;
                lock (queue)
                {
                    if (queue.Count == 0)
                    {
                        break;
                    }

                    scheduled = queue.Dequeue();
                    if (scheduled.Key.Length > 0)
                    {
                        pendingKeys.Remove(scheduled.Key);
                    }
                }

                var start = SunExpPerformanceCounters.Timestamp();
                try
                {
                    scheduled.Action();
                }
                catch (Exception ex)
                {
                    SunExpLog.Error("Scheduled SunExp frame action failed: " + scheduled.Key, ex);
                }
                finally
                {
                    SunExpPerformanceCounters.RecordDuration("FrameScheduler.Action", start);
                }
            }

            SunExpPerformanceCounters.MaybeLogSummary();
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
