using System;
using System.Collections.Generic;
using AuraShared.Core;

namespace SunExp.Dll.Infrastructure;

public static class SunExpLifecycleStepRunner
{
    public static bool RunBattleOnce(
        string featureId,
        string lifecycleId,
        IEnumerable<SunExpFrameStep> steps,
        AuraSharedFramePhase phase = AuraSharedFramePhase.GameplayMutation,
        int priority = 0,
        int estimatedCost = 1,
        Func<bool>? isCancelled = null,
        Action? onCompleted = null)
    {
        if (steps == null)
        {
            return false;
        }

        var key = FeatureLifecycleKeyFor(featureId, lifecycleId);
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

        return RunBattleOnce(
            featureId,
            lifecycleId,
            sharedSteps,
            phase,
            priority,
            estimatedCost,
            isCancelled,
            onCompleted);
    }

    public static bool RunBattleOnce(
        string featureId,
        string lifecycleId,
        IReadOnlyList<AuraSharedFrameStep> steps,
        AuraSharedFramePhase phase = AuraSharedFramePhase.GameplayMutation,
        int priority = 0,
        int estimatedCost = 1,
        Func<bool>? isCancelled = null,
        Action? onCompleted = null)
    {
        if (steps == null || steps.Count == 0)
        {
            return false;
        }

        var key = FeatureLifecycleKeyFor(featureId, lifecycleId);
        var enqueued = AuraSharedLifecycleStepRunner.Run(new AuraSharedLifecycleStepRequest
        {
            OwnerId = SunExpIds.ModId,
            FeatureId = featureId,
            LifecycleId = lifecycleId,
            SessionId = AuraBattleLifecycleRouter.EnsureBattleSession().ToString(),
            Source = "SunExp." + key,
            DeduplicateScope = AuraSharedLifecycleDeduplicateScope.OwnerFeatureLifecycleSession,
            InitialDelayFrames = 1,
            DefaultStepDelayFrames = 1,
            Phase = phase,
            Priority = priority,
            EstimatedCost = estimatedCost,
            Steps = steps,
            IsCancelled = isCancelled,
            OnStepFailed = (stepName, ex) => SunExpLog.Error("Lifecycle step failed: " + key + "." + stepName, ex),
            OnFailed = ex => SunExpPerformanceCounters.Record("LifecycleStep.Failed"),
            OnCompleted = onCompleted
        });

        SunExpPerformanceCounters.Record(enqueued ? "LifecycleStep.Enqueued" : "LifecycleStep.Deduped");
        SunExpPerformanceCounters.Record(enqueued
            ? "LifecycleStep.Enqueued." + key
            : "LifecycleStep.Deduped." + key);
        return enqueued;
    }

    private static void RunMeasuredStep(string key, SunExpFrameStep step)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            step.Action();
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("LifecycleStep.Action", start);
            SunExpPerformanceCounters.RecordDuration("LifecycleStep.Action." + CounterKeyFor(key, step.Name), start);
        }
    }

    private static string FeatureLifecycleKeyFor(string featureId, string lifecycleId)
    {
        return CounterKeyFor((featureId ?? "") + "." + (lifecycleId ?? ""), "");
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
