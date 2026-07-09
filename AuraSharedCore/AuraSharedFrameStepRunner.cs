using System;
using System.Collections.Generic;

namespace AuraShared.Core;

public sealed class AuraSharedFrameStep
{
    public string Name { get; set; } = "";

    public int DelayFrames { get; set; } = 1;

    public AuraSharedFramePhase? Phase { get; set; }

    public int? Priority { get; set; }

    public int? EstimatedCost { get; set; }

    public Action? Action { get; set; }

    public Func<AuraSharedFrameStepResult>? Work { get; set; }
}

public enum AuraSharedFrameStepStatus
{
    Complete,
    ContinueNextFrame,
    WaitFrames,
    Cancel
}

public sealed class AuraSharedFrameStepResult
{
    public static readonly AuraSharedFrameStepResult Complete = new(AuraSharedFrameStepStatus.Complete, 0);

    public static readonly AuraSharedFrameStepResult ContinueNextFrame = new(AuraSharedFrameStepStatus.ContinueNextFrame, 1);

    public static readonly AuraSharedFrameStepResult Cancel = new(AuraSharedFrameStepStatus.Cancel, 0);

    private AuraSharedFrameStepResult(AuraSharedFrameStepStatus status, int waitFrames)
    {
        Status = status;
        WaitFrames = waitFrames;
    }

    public AuraSharedFrameStepStatus Status { get; }

    public int WaitFrames { get; }

    public static AuraSharedFrameStepResult Wait(int frames)
    {
        return new AuraSharedFrameStepResult(AuraSharedFrameStepStatus.WaitFrames, Math.Max(1, frames));
    }
}

public sealed class AuraSharedFrameStepSequence
{
    public string OwnerId { get; set; } = "";

    public string Source { get; set; } = "";

    public string DeduplicateKey { get; set; } = "";

    public int InitialDelayFrames { get; set; } = 1;

    public int DefaultStepDelayFrames { get; set; } = 1;

    public AuraSharedFramePhase Phase { get; set; } = AuraSharedFramePhase.Presentation;

    public int Priority { get; set; }

    public int EstimatedCost { get; set; } = 1;

    public IReadOnlyList<AuraSharedFrameStep> Steps { get; set; } = Array.Empty<AuraSharedFrameStep>();

    public Func<bool>? IsCancelled { get; set; }

    public Action<string, Exception>? OnStepFailed { get; set; }

    public Action<Exception>? OnFailed { get; set; }

    public Action? OnCompleted { get; set; }
}

public static class AuraSharedFrameStepRunner
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> ActiveKeys = new(StringComparer.Ordinal);

    public static bool Run(AuraSharedFrameStepSequence? sequence)
    {
        if (sequence == null || sequence.Steps == null || sequence.Steps.Count == 0)
        {
            return false;
        }

        var key = Normalize(sequence.DeduplicateKey);
        if (key.Length > 0)
        {
            lock (SyncRoot)
            {
                if (!ActiveKeys.Add(key))
                {
                    return false;
                }
            }
        }

        var state = new SequenceState(sequence, key);
        return ScheduleStep(state, 0, Math.Max(1, sequence.InitialDelayFrames));
    }

    private static bool ScheduleStep(SequenceState state, int index, int delayFrames)
    {
        var step = index >= 0 && index < state.Sequence.Steps.Count
            ? state.Sequence.Steps[index]
            : null;
        var stepName = string.IsNullOrWhiteSpace(step?.Name) ? index.ToString() : step!.Name.Trim();
        var source = state.Source + "." + stepName;
        var key = state.Key.Length == 0 ? "" : state.Key + "." + index;
        var scheduled = AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = state.Sequence.OwnerId ?? "",
            Key = key,
            Source = source,
            DelayFrames = Math.Max(1, delayFrames),
            Phase = step?.Phase ?? state.Sequence.Phase,
            Priority = step?.Priority ?? state.Sequence.Priority,
            EstimatedCost = step?.EstimatedCost ?? state.Sequence.EstimatedCost,
            Action = () => RunStep(state, index)
        });
        if (!scheduled)
        {
            ReleaseKey(state.Key);
        }

        return scheduled;
    }

    private static void RunStep(SequenceState state, int index)
    {
        try
        {
            if (state.Sequence.IsCancelled?.Invoke() == true)
            {
                Complete(state);
                return;
            }

            if (index < 0 || index >= state.Sequence.Steps.Count)
            {
                Complete(state);
                return;
            }

            var step = state.Sequence.Steps[index];
            var result = RunStepWork(step);
            if (result.Status == AuraSharedFrameStepStatus.Cancel)
            {
                Complete(state);
                return;
            }

            if (result.Status == AuraSharedFrameStepStatus.ContinueNextFrame)
            {
                ScheduleStep(state, index, 1);
                return;
            }

            if (result.Status == AuraSharedFrameStepStatus.WaitFrames)
            {
                ScheduleStep(state, index, Math.Max(1, result.WaitFrames));
                return;
            }

            var next = index + 1;
            if (next >= state.Sequence.Steps.Count)
            {
                Complete(state);
                return;
            }

            var delay = step.DelayFrames > 0
                ? step.DelayFrames
                : Math.Max(1, state.Sequence.DefaultStepDelayFrames);
            ScheduleStep(state, next, delay);
        }
        catch (Exception ex)
        {
            var stepName = index >= 0 && index < state.Sequence.Steps.Count
                ? state.Sequence.Steps[index].Name
                : "";
            try
            {
                state.Sequence.OnStepFailed?.Invoke(stepName, ex);
                state.Sequence.OnFailed?.Invoke(ex);
            }
            finally
            {
                ReleaseKey(state.Key);
            }
        }
    }

    private static AuraSharedFrameStepResult RunStepWork(AuraSharedFrameStep step)
    {
        if (step.Work != null)
        {
            return step.Work() ?? AuraSharedFrameStepResult.Complete;
        }

        step.Action?.Invoke();
        return AuraSharedFrameStepResult.Complete;
    }

    private static void Complete(SequenceState state)
    {
        try
        {
            state.Sequence.OnCompleted?.Invoke();
        }
        finally
        {
            ReleaseKey(state.Key);
        }
    }

    private static void ReleaseKey(string key)
    {
        if (key.Length == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            ActiveKeys.Remove(key);
        }
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private sealed class SequenceState
    {
        public SequenceState(AuraSharedFrameStepSequence sequence, string key)
        {
            Sequence = sequence;
            Key = key;
            Source = Normalize(sequence.Source);
            if (Source.Length == 0)
            {
                Source = "AuraSharedFrameStep";
            }
        }

        public AuraSharedFrameStepSequence Sequence { get; }

        public string Key { get; }

        public string Source { get; }
    }
}
