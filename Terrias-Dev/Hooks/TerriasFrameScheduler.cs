using System;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasFrameScheduler
{
    private const double SlowActionWarningMilliseconds = 16.0;

    public static void Initialize(ModConfig modConfig)
    {
        if (!TerriasPerformanceSettings.TrySetCountersEnabled(true))
        {
            TerriasLog.Warn("Unable to enable TerriasPerfCounters=1; performance diagnostics remain controlled by the current GameVar value.");
        }

        AuraSharedFrameScheduler.MaxActionsPerFrame = Math.Max(
            AuraSharedFrameScheduler.MaxActionsPerFrame,
            TerriasPerformanceSettings.FrameSchedulerBudget);
        TerriasFrameDispatcher.Register(RunOnceNextFrame);
        TerriasFrameDispatcher.RegisterDelayed(RunOnceAfterFrames);
        TerriasLog.InfoAlways("Terrias performance frame scheduler initialized through AuraSharedFrameScheduler");
        TerriasLog.InfoAlways(TerriasPerformanceSettings.DiagnosticsSummary());
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
            OwnerId = TerriasIds.ModId,
            Key = normalizedKey,
            Source = "Terrias." + normalizedKey,
            DelayFrames = Math.Max(1, delayFrames),
            Phase = phase,
            Priority = priority,
            EstimatedCost = estimatedCost,
            Action = () => ExecuteScheduledAction(normalizedKey, action),
            OnScheduled = RecordScheduled,
            OnDeduplicated = RecordDeduplicated,
            OnExecuting = RecordFrameDiagnostics,
            OnExecuted = _ => TerriasPerformanceCounters.MaybeLogSummary()
        };

        return AuraSharedFrameScheduler.RunOnceAfterFrames(request);
    }

    private static void RecordScheduled(AuraSharedFrameActionReport report)
    {
        TerriasPerformanceCounters.Record("FrameScheduler.Enqueued");
        if (report.TargetFrame >= 0 && report.EnqueuedFrame >= 0 && report.TargetFrame - report.EnqueuedFrame > 1)
        {
            TerriasPerformanceCounters.Record("FrameScheduler.EnqueuedDelayed");
        }

        if (report.EnqueuedDuringDrain)
        {
            TerriasPerformanceCounters.Record("FrameScheduler.EnqueuedDuringDrain");
        }
    }

    private static void RecordDeduplicated(AuraSharedFrameActionReport report)
    {
        TerriasPerformanceCounters.Record("FrameScheduler.Deduped");
        if (report.EnqueuedDuringDrain)
        {
            TerriasPerformanceCounters.Record("FrameScheduler.DedupedDuringDrain");
        }
    }

    private static void ExecuteScheduledAction(string key, Action action)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        double actionElapsed;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Scheduled Terrias frame action failed: " + key, ex);
        }
        finally
        {
            actionElapsed = TerriasPerformanceCounters.ElapsedMilliseconds(start);
            var instrumentationStart = TerriasPerformanceCounters.Timestamp();
            TerriasPerformanceCounters.RecordDuration("FrameScheduler.Action", start);
            TerriasPerformanceCounters.RecordDuration("FrameScheduler.Action." + CounterKeyFor(key), start);
            LogSlowAction(key, actionElapsed);
            TerriasPerformanceCounters.RecordDuration("FrameScheduler.Instrumentation", instrumentationStart);
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
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        if (elapsed < SlowActionWarningMilliseconds)
        {
            return;
        }

        TerriasLog.Warn("Slow Terrias frame action: key="
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
            TerriasPerformanceCounters.Record("FrameScheduler.DelayFrames0");
            TerriasLog.WarnOnce("FrameScheduler.SameFrameExecution",
                "Terrias frame scheduler executed work in the same Unity frame it was queued: key="
                + report.Key
                + ", frame="
                + report.ExecuteFrame
                + ", queuedDuringDrain="
                + report.EnqueuedDuringDrain);
        }
        else if (delayFrames == 1)
        {
            TerriasPerformanceCounters.Record("FrameScheduler.DelayFrames1");
        }
        else
        {
            TerriasPerformanceCounters.Record("FrameScheduler.DelayFrames2Plus");
        }

        if (report.TargetFrame >= 0 && report.ExecuteFrame < report.TargetFrame)
        {
            TerriasPerformanceCounters.Record("FrameScheduler.ExecutedBeforeTargetFrame");
            TerriasLog.WarnOnce("FrameScheduler.ExecutedBeforeTargetFrame",
                "Terrias frame scheduler executed work before its target Unity frame: key="
                + report.Key
                + ", frame="
                + report.ExecuteFrame
                + ", targetFrame="
                + report.TargetFrame);
        }

        if (report.EnqueuedDuringDrain)
        {
            TerriasPerformanceCounters.Record("FrameScheduler.ExecutedDrainQueued");
            if (delayFrames <= 0)
            {
                TerriasPerformanceCounters.Record("FrameScheduler.ReentrantSameFrameExecution");
                TerriasLog.WarnOnce("FrameScheduler.ReentrantSameFrameExecution",
                    "Terrias frame scheduler executed work that was queued during the same scheduler drain: key="
                    + report.Key
                    + ", frame="
                    + report.ExecuteFrame);
            }
        }
    }
}
