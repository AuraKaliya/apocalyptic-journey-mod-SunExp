using System;
using System.Collections.Generic;

namespace AuraShared.Core;

public sealed class AuraSharedFrameStep
{
    public string Name { get; set; } = "";

    public int DelayFrames { get; set; } = 1;

    public Action? Action { get; set; }
}

public sealed class AuraSharedFrameStepSequence
{
    public string Source { get; set; } = "";

    public string DeduplicateKey { get; set; } = "";

    public int InitialDelayFrames { get; set; } = 1;

    public int DefaultStepDelayFrames { get; set; } = 1;

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
        var source = state.Source + "." + index;
        var scheduled = AuraSharedFrameScheduler.RunAfterFramesBudgeted(source, Math.Max(1, delayFrames), () => RunStep(state, index));
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
            step.Action?.Invoke();

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
