using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpFrameScheduler
{
    private const double SlowActionWarningMilliseconds = 16.0;
    private static SchedulerRunner? runner;
    private static bool createFailureLogged;

    public static void Initialize(ModConfig modConfig)
    {
        EnsureRunner();
        SunExpFrameDispatcher.Register(RunOnceNextFrame);
        SunExpFrameDispatcher.RegisterDelayed(RunOnceAfterFrames);
        SunExpLog.InfoAlways("SunExp performance frame scheduler initialized");
        SunExpLog.InfoAlways(SunExpPerformanceSettings.DiagnosticsSummary());
    }

    public static bool RunOnceNextFrame(string key, Action action)
    {
        return RunOnceAfterFrames(key, 1, action);
    }

    public static bool RunOnceAfterFrames(string key, int delayFrames, Action action)
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

        return current.Enqueue(key, Math.Max(1, delayFrames), action);
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
        public ScheduledAction(string key, Action action, int enqueuedFrame, int targetFrame, bool enqueuedDuringDrain)
        {
            Key = key;
            Action = action;
            EnqueuedFrame = enqueuedFrame;
            TargetFrame = targetFrame;
            EnqueuedDuringDrain = enqueuedDuringDrain;
        }

        public string Key { get; }

        public Action Action { get; }

        public int EnqueuedFrame { get; }

        public int TargetFrame { get; }

        public bool EnqueuedDuringDrain { get; }
    }

    private sealed class SchedulerRunner : MonoBehaviour
    {
        private readonly object syncRoot = new();
        private readonly Queue<ScheduledAction> currentQueue = new();
        private readonly Queue<ScheduledAction> nextQueue = new();
        private readonly List<ScheduledAction> delayedActions = new();
        private readonly HashSet<string> pendingKeys = new(StringComparer.Ordinal);
        private bool isDraining;

        public bool Enqueue(string key, int delayFrames, Action action)
        {
            var normalizedKey = (key ?? "").Trim();
            var enqueuedFrame = SafeFrameCount();
            var normalizedDelay = Math.Max(1, delayFrames);
            var targetFrame = enqueuedFrame < 0 ? -1 : enqueuedFrame + normalizedDelay;
            bool enqueuedDuringDrain;
            lock (syncRoot)
            {
                enqueuedDuringDrain = isDraining;
                if (normalizedKey.Length > 0 && !pendingKeys.Add(normalizedKey))
                {
                    SunExpPerformanceCounters.Record("FrameScheduler.Deduped");
                    if (enqueuedDuringDrain)
                    {
                        SunExpPerformanceCounters.Record("FrameScheduler.DedupedDuringDrain");
                    }

                    return false;
                }

                nextQueue.Enqueue(new ScheduledAction(normalizedKey, action, enqueuedFrame, targetFrame, enqueuedDuringDrain));
            }

            SunExpPerformanceCounters.Record("FrameScheduler.Enqueued");
            if (normalizedDelay > 1)
            {
                SunExpPerformanceCounters.Record("FrameScheduler.EnqueuedDelayed");
            }

            if (enqueuedDuringDrain)
            {
                SunExpPerformanceCounters.Record("FrameScheduler.EnqueuedDuringDrain");
            }

            return true;
        }

        private void Update()
        {
            var budget = Math.Max(1, SunExpPerformanceSettings.FrameSchedulerBudget);
            var frame = SafeFrameCount();
            lock (syncRoot)
            {
                PromoteReady(frame);
                isDraining = true;
            }

            try
            {
                for (var i = 0; i < budget; i++)
                {
                    ScheduledAction scheduled;
                    lock (syncRoot)
                    {
                        if (currentQueue.Count == 0)
                        {
                            break;
                        }

                        scheduled = currentQueue.Dequeue();
                        if (scheduled.Key.Length > 0)
                        {
                            pendingKeys.Remove(scheduled.Key);
                        }
                    }

                    RecordFrameDiagnostics(scheduled);
                    var start = SunExpPerformanceCounters.Timestamp();
                    double actionElapsed;
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
                        actionElapsed = SunExpPerformanceCounters.ElapsedMilliseconds(start);
                        var instrumentationStart = SunExpPerformanceCounters.Timestamp();
                        SunExpPerformanceCounters.RecordDuration("FrameScheduler.Action", start);
                        SunExpPerformanceCounters.RecordDuration("FrameScheduler.Action." + CounterKeyFor(scheduled.Key), start);
                        LogSlowAction(scheduled.Key, actionElapsed);
                        SunExpPerformanceCounters.RecordDuration("FrameScheduler.Instrumentation", instrumentationStart);
                    }
                }
            }
            finally
            {
                lock (syncRoot)
                {
                    isDraining = false;
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

        private void PromoteReady(int frame)
        {
            while (nextQueue.Count > 0)
            {
                var scheduled = nextQueue.Dequeue();
                if (IsReady(scheduled, frame))
                {
                    currentQueue.Enqueue(scheduled);
                }
                else
                {
                    delayedActions.Add(scheduled);
                }
            }

            for (var i = delayedActions.Count - 1; i >= 0; i--)
            {
                var scheduled = delayedActions[i];
                if (!IsReady(scheduled, frame))
                {
                    continue;
                }

                delayedActions.RemoveAt(i);
                currentQueue.Enqueue(scheduled);
            }
        }

        private static bool IsReady(ScheduledAction scheduled, int frame)
        {
            return scheduled.TargetFrame < 0 || frame < 0 || scheduled.TargetFrame <= frame;
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

        private static string CounterKeyFor(string key)
        {
            var value = (key ?? "").Trim();
            if (value.Length == 0)
            {
                return "Unknown";
            }

            if (value.StartsWith("WunaRadiance.BurnChanged.", StringComparison.Ordinal))
            {
                return "WunaRadiance.BurnChanged";
            }

            if (value.StartsWith("Loneer.StarStonePouchDraw.", StringComparison.Ordinal))
            {
                return "Loneer.StarStonePouchDraw";
            }

            if (value.StartsWith("Loneer.GuidanceSelection.", StringComparison.Ordinal))
            {
                return "Loneer.GuidanceSelection";
            }

            if (value.StartsWith("CardPresentation.ReapplyActiveCombatCards.", StringComparison.Ordinal))
            {
                return "CardPresentation.ReapplyActiveCombatCards";
            }

            var lastDot = value.LastIndexOf('.');
            if (lastDot > 0 && lastDot < value.Length - 1)
            {
                var tail = value.Substring(lastDot + 1);
                if (int.TryParse(tail, out _))
                {
                    return value.Substring(0, lastDot);
                }
            }

            return value.Length <= 80 ? value : value.Substring(0, 80);
        }

        private static void LogSlowAction(string key, double elapsed)
        {
            if (!SunExpPerformanceSettings.CountersEnabled)
            {
                return;
            }

            if (elapsed < SlowActionWarningMilliseconds)
            {
                return;
            }

            SunExpLog.Warn("Slow SunExp frame action: key="
                + key
                + ", elapsedMs="
                + elapsed.ToString("0.###")
                + ", category="
                + CounterKeyFor(key));
        }

        private static void RecordFrameDiagnostics(ScheduledAction scheduled)
        {
            var executeFrame = SafeFrameCount();
            if (scheduled.EnqueuedFrame < 0 || executeFrame < 0)
            {
                return;
            }

            var delayFrames = executeFrame - scheduled.EnqueuedFrame;
            if (delayFrames <= 0)
            {
                SunExpPerformanceCounters.Record("FrameScheduler.DelayFrames0");
                SunExpLog.WarnOnce("FrameScheduler.SameFrameExecution",
                    "SunExp frame scheduler executed work in the same Unity frame it was queued: key="
                    + scheduled.Key
                    + ", frame="
                    + executeFrame
                    + ", queuedDuringDrain="
                    + scheduled.EnqueuedDuringDrain);
            }
            else if (delayFrames == 1)
            {
                SunExpPerformanceCounters.Record("FrameScheduler.DelayFrames1");
            }
            else
            {
                SunExpPerformanceCounters.Record("FrameScheduler.DelayFrames2Plus");
            }

            if (scheduled.TargetFrame >= 0 && executeFrame < scheduled.TargetFrame)
            {
                SunExpPerformanceCounters.Record("FrameScheduler.ExecutedBeforeTargetFrame");
                SunExpLog.WarnOnce("FrameScheduler.ExecutedBeforeTargetFrame",
                    "SunExp frame scheduler executed work before its target Unity frame: key="
                    + scheduled.Key
                    + ", frame="
                    + executeFrame
                    + ", targetFrame="
                    + scheduled.TargetFrame);
            }

            if (scheduled.EnqueuedDuringDrain)
            {
                SunExpPerformanceCounters.Record("FrameScheduler.ExecutedDrainQueued");
                if (delayFrames <= 0)
                {
                    SunExpPerformanceCounters.Record("FrameScheduler.ReentrantSameFrameExecution");
                    SunExpLog.WarnOnce("FrameScheduler.ReentrantSameFrameExecution",
                        "SunExp frame scheduler executed work that was queued during the same scheduler drain: key="
                        + scheduled.Key
                        + ", frame="
                        + executeFrame);
                }
            }
        }
    }
}
