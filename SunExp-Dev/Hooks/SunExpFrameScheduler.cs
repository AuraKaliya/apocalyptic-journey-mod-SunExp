using System;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpFrameScheduler
{
    private const double SlowActionWarningMilliseconds = 16.0;

    public static void Initialize(ModConfig modConfig)
    {
        AuraSharedFrameScheduler.MaxActionsPerFrame = Math.Max(
            AuraSharedFrameScheduler.MaxActionsPerFrame,
            SunExpPerformanceSettings.FrameSchedulerBudget);
        SunExpFrameDispatcher.Register(RunOnceNextFrame);
        SunExpFrameDispatcher.RegisterDelayed(RunOnceAfterFrames);
        SunExpLog.InfoAlways("SunExp performance frame scheduler initialized through AuraSharedFrameScheduler");
        SunExpLog.InfoAlways(SunExpPerformanceSettings.DiagnosticsSummary());
    }

    public static bool RunOnceNextFrame(string key, Action action)
    {
        return RunOnceAfterFrames(key, 1, action);
    }

    public static bool RunOnceAfterFrames(
        string key,
        int delayFrames,
        Action action,
        AuraSharedFramePhase phase,
        int priority = 0,
        int estimatedCost = 1)
    {
        return Schedule(key, delayFrames, action, phase, priority, estimatedCost);
    }

    public static bool RunOnceAfterFrames(string key, int delayFrames, Action action)
    {
        return Schedule(key, delayFrames, action, AuraSharedFramePhase.Presentation);
    }

    private static bool Schedule(
        string key,
        int delayFrames,
        Action action,
        AuraSharedFramePhase phase,
        int priority = 0,
        int estimatedCost = 1)
    {
        if (action == null)
        {
            return false;
        }

        var normalizedKey = (key ?? "").Trim();
        var request = new AuraSharedFrameActionRequest
        {
            OwnerId = SunExpIds.ModId,
            Key = normalizedKey,
            Source = "SunExp." + normalizedKey,
            DelayFrames = Math.Max(1, delayFrames),
            Phase = phase,
            Priority = priority,
            EstimatedCost = estimatedCost,
            Action = () => ExecuteScheduledAction(normalizedKey, action),
            OnScheduled = RecordScheduled,
            OnDeduplicated = RecordDeduplicated,
            OnExecuting = RecordFrameDiagnostics,
            OnExecuted = _ => SunExpPerformanceCounters.MaybeLogSummary()
        };

        return AuraSharedFrameScheduler.RunOnceAfterFrames(request);
    }

    private static void RecordScheduled(AuraSharedFrameActionReport report)
    {
        SunExpPerformanceCounters.Record("FrameScheduler.Enqueued");
        if (report.TargetFrame >= 0 && report.EnqueuedFrame >= 0 && report.TargetFrame - report.EnqueuedFrame > 1)
        {
            SunExpPerformanceCounters.Record("FrameScheduler.EnqueuedDelayed");
        }

        if (report.EnqueuedDuringDrain)
        {
            SunExpPerformanceCounters.Record("FrameScheduler.EnqueuedDuringDrain");
        }
    }

    private static void RecordDeduplicated(AuraSharedFrameActionReport report)
    {
        SunExpPerformanceCounters.Record("FrameScheduler.Deduped");
        if (report.EnqueuedDuringDrain)
        {
            SunExpPerformanceCounters.Record("FrameScheduler.DedupedDuringDrain");
        }
    }

    private static void ExecuteScheduledAction(string key, Action action)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        double actionElapsed;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Scheduled SunExp frame action failed: " + key, ex);
        }
        finally
        {
            actionElapsed = SunExpPerformanceCounters.ElapsedMilliseconds(start);
            var instrumentationStart = SunExpPerformanceCounters.Timestamp();
            SunExpPerformanceCounters.RecordDuration("FrameScheduler.Action", start);
            SunExpPerformanceCounters.RecordDuration("FrameScheduler.Action." + CounterKeyFor(key), start);
            LogSlowAction(key, actionElapsed);
            SunExpPerformanceCounters.RecordDuration("FrameScheduler.Instrumentation", instrumentationStart);
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

    private static void RecordFrameDiagnostics(AuraSharedFrameActionReport report)
    {
        if (report.EnqueuedFrame < 0 || report.ExecuteFrame < 0)
        {
            return;
        }

        var delayFrames = report.ExecuteFrame - report.EnqueuedFrame;
        if (delayFrames <= 0)
        {
            SunExpPerformanceCounters.Record("FrameScheduler.DelayFrames0");
            SunExpLog.WarnOnce("FrameScheduler.SameFrameExecution",
                "SunExp frame scheduler executed work in the same Unity frame it was queued: key="
                + report.Key
                + ", frame="
                + report.ExecuteFrame
                + ", queuedDuringDrain="
                + report.EnqueuedDuringDrain);
        }
        else if (delayFrames == 1)
        {
            SunExpPerformanceCounters.Record("FrameScheduler.DelayFrames1");
        }
        else
        {
            SunExpPerformanceCounters.Record("FrameScheduler.DelayFrames2Plus");
        }

        if (report.TargetFrame >= 0 && report.ExecuteFrame < report.TargetFrame)
        {
            SunExpPerformanceCounters.Record("FrameScheduler.ExecutedBeforeTargetFrame");
            SunExpLog.WarnOnce("FrameScheduler.ExecutedBeforeTargetFrame",
                "SunExp frame scheduler executed work before its target Unity frame: key="
                + report.Key
                + ", frame="
                + report.ExecuteFrame
                + ", targetFrame="
                + report.TargetFrame);
        }

        if (report.EnqueuedDuringDrain)
        {
            SunExpPerformanceCounters.Record("FrameScheduler.ExecutedDrainQueued");
            if (delayFrames <= 0)
            {
                SunExpPerformanceCounters.Record("FrameScheduler.ReentrantSameFrameExecution");
                SunExpLog.WarnOnce("FrameScheduler.ReentrantSameFrameExecution",
                    "SunExp frame scheduler executed work that was queued during the same scheduler drain: key="
                    + report.Key
                    + ", frame="
                    + report.ExecuteFrame);
            }
        }
    }
}
