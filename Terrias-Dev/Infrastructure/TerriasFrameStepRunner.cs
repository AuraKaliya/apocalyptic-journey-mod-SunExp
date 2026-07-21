using System;
using System.Collections.Generic;
using AuraShared.Core;

namespace Terrias.Dll.Infrastructure;

public sealed class TerriasFrameStep
{
    public TerriasFrameStep(string name, Action action, int delayFrames = 1)
    {
        Name = name ?? "";
        Action = action;
        DelayFrames = Math.Max(1, delayFrames);
    }

    public string Name { get; }

    public Action Action { get; }

    public int DelayFrames { get; }
}

public static class TerriasFrameStepRunner
{
    public static bool RunOnce(
        string key,
        IEnumerable<TerriasFrameStep> steps,
        Func<bool>? isCancelled = null,
        Action? onCompleted = null)
    {
        if (steps == null)
        {
            return false;
        }

        var sharedSteps = new List<AuraSharedFrameStep>();
        foreach (var step in steps)
        {
            if (step?.Action == null)
            {
                continue;
            }

            sharedSteps.Add(new AuraSharedFrameStep
            {
                Name = step.Name,
                DelayFrames = Math.Max(1, step.DelayFrames),
                Action = () => RunMeasuredStep(key, step)
            });
        }

        if (sharedSteps.Count == 0)
        {
            return false;
        }

        var enqueued = AuraSharedFrameStepRunner.Run(new AuraSharedFrameStepSequence
        {
            Source = key,
            DeduplicateKey = key,
            InitialDelayFrames = 1,
            DefaultStepDelayFrames = 1,
            Steps = sharedSteps,
            IsCancelled = isCancelled,
            OnStepFailed = (stepName, ex) => TerriasLog.Error("Frame step failed: " + key + "." + stepName, ex),
            OnFailed = ex => TerriasPerformanceCounters.Record("FrameStep.Failed"),
            OnCompleted = onCompleted
        });

        TerriasPerformanceCounters.Record(enqueued ? "FrameStep.Enqueued" : "FrameStep.Deduped");
        return enqueued;
    }

    private static void RunMeasuredStep(string key, TerriasFrameStep step)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            step.Action();
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("FrameStep.Action", start);
            TerriasPerformanceCounters.RecordDuration("FrameStep.Action." + CounterKeyFor(key, step.Name), start);
        }
    }

    private static string CounterKeyFor(string key, string stepName)
    {
        var value = ((key ?? "") + "." + (stepName ?? "")).Trim('.');
        if (value.Length == 0)
        {
            return "Unknown";
        }

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '.' && chars[i] != '_' && chars[i] != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
