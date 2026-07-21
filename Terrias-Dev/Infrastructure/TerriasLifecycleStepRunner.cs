using System;
using System.Collections.Generic;
using AuraShared.Core;

namespace Terrias.Dll.Infrastructure;

public static class TerriasLifecycleStepRunner
{
    public static bool RunBattleOnce(
        string featureId,
        string lifecycleId,
        IEnumerable<TerriasFrameStep> steps,
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
            OwnerId = TerriasIds.ModId,
            FeatureId = featureId,
            LifecycleId = lifecycleId,
            SessionId = AuraBattleLifecycleRouter.EnsureBattleSession().ToString(),
            Source = "Terrias." + key,
            DeduplicateScope = AuraSharedLifecycleDeduplicateScope.OwnerFeatureLifecycleSession,
            InitialDelayFrames = 1,
            DefaultStepDelayFrames = 1,
            Phase = phase,
            Priority = priority,
            EstimatedCost = estimatedCost,
            Steps = steps,
            IsCancelled = isCancelled,
            OnStepFailed = (stepName, ex) => TerriasLog.Error("Lifecycle step failed: " + key + "." + stepName, ex),
            OnFailed = ex => TerriasPerformanceCounters.Record("LifecycleStep.Failed"),
            OnCompleted = onCompleted
        });

        TerriasPerformanceCounters.Record(enqueued ? "LifecycleStep.Enqueued" : "LifecycleStep.Deduped");
        TerriasPerformanceCounters.Record(enqueued
            ? "LifecycleStep.Enqueued." + key
            : "LifecycleStep.Deduped." + key);
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
            TerriasPerformanceCounters.RecordDuration("LifecycleStep.Action", start);
            TerriasPerformanceCounters.RecordDuration("LifecycleStep.Action." + CounterKeyFor(key, step.Name), start);
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
