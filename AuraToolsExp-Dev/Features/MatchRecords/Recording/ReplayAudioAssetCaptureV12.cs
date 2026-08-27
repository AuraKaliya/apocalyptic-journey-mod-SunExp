using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using AuraShared.Core;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal sealed class ReplayAudioAssetCaptureV12
{
    private static readonly MethodInfo? AudioClipGetData = typeof(AudioClip)
        .GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
    private readonly Dictionary<string, ReplayAssetV12> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CaptureState> pending = new(StringComparer.Ordinal);
    private long generation;

    internal int PendingCount => pending.Count;

    internal event Action? Drained;

    internal void Request(
        AudioClip? clip,
        string usage,
        Action<ReplayAudioCaptureResultV12> completion)
    {
        if (clip == null)
        {
            completion?.Invoke(ReplayAudioCaptureResultV12.Failed("clip-missing", "the native playback clip is null"));
            return;
        }
        if (clip.samples <= 0 || clip.channels is < 1 or > 2 || clip.frequency <= 0)
        {
            completion?.Invoke(ReplayAudioCaptureResultV12.Failed(
                "clip-shape-invalid",
                Describe(clip)));
            return;
        }

        var key = Key(clip);
        if (completed.TryGetValue(key, out var cached))
        {
            completion?.Invoke(ReplayAudioCaptureResultV12.Succeeded(cached));
            return;
        }
        if (pending.TryGetValue(key, out var existing))
        {
            existing.Add(completion);
            return;
        }
        if ((long)clip.samples * clip.channels > 32L * 1024L * 1024L)
        {
            completion?.Invoke(ReplayAudioCaptureResultV12.Failed(
                "clip-too-large",
                Describe(clip)));
            return;
        }

        var state = new CaptureState(key, Interlocked.Increment(ref generation), clip, usage);
        state.Add(completion);
        pending[key] = state;
        var queued = AuraSharedFrameScheduler.RunCooperative(new AuraSharedFrameWorkRequest
        {
            OwnerId = "AuraToolsExp",
            Key = "ReplayV12.AudioRead." + state.Generation,
            Source = "MatchRecords.ReplayV12.AudioRead",
            DelayFrames = 1,
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 5,
            EstimatedCost = 1,
            SliceBudgetMilliseconds = 1.5d,
            MaximumSlices = 2048,
            IsCancelled = () => state.Cancelled,
            ExecuteSlice = _ => state.ReadSlice(),
            OnCompleted = _ => QueueFinalize(state),
            OnCancelled = _ => Complete(state, ReplayAudioCaptureResultV12.Failed("capture-cancelled", state.Description)),
            OnFailed = (_, ex) => Complete(state, ReplayAudioCaptureResultV12.Failed("frame-read-failed", ex.Message))
        });
        if (!queued)
        {
            Complete(state, ReplayAudioCaptureResultV12.Failed("frame-scheduler-rejected", state.Description));
        }
    }

    internal void Cancel()
    {
        foreach (var state in pending.Values)
        {
            state.Cancelled = true;
            state.Finish(ReplayAudioCaptureResultV12.Failed("capture-cancelled", state.Description));
        }
        pending.Clear();
        completed.Clear();
        Drained = null;
    }

    private void QueueFinalize(CaptureState state)
    {
        if (state.Cancelled)
        {
            Complete(state, ReplayAudioCaptureResultV12.Failed("capture-cancelled", state.Description));
            return;
        }
        if (state.FailureCode.Length > 0)
        {
            Complete(state, ReplayAudioCaptureResultV12.Failed(state.FailureCode, state.FailureMessage));
            return;
        }
        var chunks = state.DetachChunks();
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<ReplayAssetV12>
            {
                OwnerId = "AuraToolsExp",
                Key = "ReplayV12.AudioFinalize." + state.Generation,
                Source = "MatchRecords.ReplayV12.AudioFinalize",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = cancellation => BuildAttachment(state, chunks, cancellation),
                IsStillCurrent = () => !state.Cancelled,
                ApplyOnMainThread = attachment => Complete(state, ReplayAudioCaptureResultV12.Succeeded(attachment)),
                OnFailedOnMainThread = ex => Complete(state, ReplayAudioCaptureResultV12.Failed("pcm-finalize-failed", ex.Message))
            });
        if (!accepted)
        {
            Complete(state, ReplayAudioCaptureResultV12.Failed("background-scheduler-rejected", state.Description));
        }
    }

    private static ReplayAssetV12 BuildAttachment(
        CaptureState state,
        IReadOnlyList<byte[]> chunks,
        CancellationToken cancellation)
    {
        foreach (var _ in chunks) cancellation.ThrowIfCancellationRequested();
        var payload = ReplayPcm16WaveContractV12.BuildPayload(
            chunks,
            state.SampleFrames,
            state.Channels,
            state.Frequency);
        var hash = ReplayCanonicalJsonV12.Sha256(payload);
        return new ReplayAssetV12
        {
            Sha256 = hash,
            MediaType = "audio/wav",
            Extension = ".wav",
            Usage = state.Usage,
            ByteLength = payload.LongLength,
            SampleRate = state.Frequency,
            Channels = state.Channels,
            SampleFrames = state.SampleFrames,
            Required = true,
            Payload = payload
        };
    }

    private void Complete(CaptureState state, ReplayAudioCaptureResultV12 result)
    {
        if (!pending.TryGetValue(state.Key, out var current) || !ReferenceEquals(current, state)) return;
        pending.Remove(state.Key);
        if (result.Attachment != null) completed[state.Key] = result.Attachment;
        state.Finish(result);
        if (pending.Count == 0) Drained?.Invoke();
    }

    private static string Key(AudioClip clip)
    {
        return clip.GetInstanceID() + "|" + clip.samples + "|" + clip.channels + "|" + clip.frequency;
    }

    private static string Describe(AudioClip clip)
    {
        return "clip=" + (clip.name ?? "<unnamed>")
               + ", samples=" + clip.samples
               + ", channels=" + clip.channels
               + ", frequency=" + clip.frequency
               + ", loadType=" + clip.loadType;
    }

    private sealed class CaptureState
    {
        private const int MaximumValuesPerSlice = 65_536;
        private const int MaximumConsecutiveEmptyProviderReads = 120;
        private readonly AudioClip clip;
        private readonly List<byte[]> chunks = new();
        private readonly List<Action<ReplayAudioCaptureResultV12>> completions = new();
        private AudioClipPcmReadApi.AudioClipPcmReader? pcmReader;
        private int offsetFrames;
        private int loadWaitSlices;
        private int emptyProviderReads;
        private bool loadRequested;

        internal CaptureState(string key, long generation, AudioClip clip, string usage)
        {
            Key = key;
            Generation = generation;
            this.clip = clip;
            Usage = string.IsNullOrWhiteSpace(usage) ? "ReplayAudio" : usage.Trim();
            SampleFrames = clip.samples;
            Channels = clip.channels;
            Frequency = clip.frequency;
            Description = Describe(clip);
        }

        internal string Key { get; }
        internal long Generation { get; }
        internal string Usage { get; }
        internal int SampleFrames { get; }
        internal int Channels { get; }
        internal int Frequency { get; }
        internal string Description { get; }
        internal int PcmBytes { get; private set; }
        internal bool Cancelled { get; set; }
        internal string FailureCode { get; private set; } = "";
        internal string FailureMessage { get; private set; } = "";

        internal void Add(Action<ReplayAudioCaptureResultV12> completion)
        {
            if (completion != null) completions.Add(completion);
        }

        internal bool ReadSlice()
        {
            if (Cancelled || offsetFrames >= SampleFrames) return true;
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                Fail("clip-load-failed", Description);
                return true;
            }
            if (clip.loadState == AudioDataLoadState.Unloaded && !loadRequested)
            {
                loadRequested = true;
                if (!clip.LoadAudioData())
                {
                    Fail("clip-load-rejected", Description);
                    return true;
                }
            }
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                loadWaitSlices++;
                if (loadWaitSlices > 240)
                {
                    Fail("clip-load-timeout", Description + ", state=" + clip.loadState);
                    return true;
                }
                return false;
            }
            if (ReplayAudioCapturePolicy.SelectReadPath(
                    clip.loadType == AudioClipLoadType.Streaming)
                == ReplayAudioReadPath.UnitySampleProvider)
            {
                return ReadStreamingSlice();
            }
            var maximumFrames = Math.Max(1, MaximumValuesPerSlice / Channels);
            var frames = Math.Min(maximumFrames, SampleFrames - offsetFrames);
            var samples = new float[checked(frames * Channels)];
            if (AudioClipGetData == null)
            {
                Fail("get-data-unavailable", Description);
                return true;
            }
            try
            {
                if (AudioClipGetData.Invoke(clip, new object[] { samples, offsetFrames }) is not true)
                {
                    Fail("get-data-returned-false", Description + ", offset=" + offsetFrames);
                    return true;
                }
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException { InnerException: not null }
                    ? ex.InnerException
                    : ex;
                Fail("get-data-threw", Description + ", error=" + cause.Message);
                return true;
            }
            AppendSamples(samples, samples.Length);
            offsetFrames += frames;
            return offsetFrames >= SampleFrames;
        }

        private bool ReadStreamingSlice()
        {
            if (pcmReader == null
                && !AudioClipPcmReadApi.TryCreate(clip, out pcmReader, out var createFailure))
            {
                Fail("sample-provider-create-failed", Description + ", error=" + createFailure);
                return true;
            }

            var maximumFrames = Math.Max(1, MaximumValuesPerSlice / Channels);
            var frames = Math.Min(maximumFrames, SampleFrames - offsetFrames);
            var samples = new float[checked(frames * Channels)];
            var consumedFrames = 0;
            var readFailure = "sample provider is unavailable";
            if (pcmReader == null
                || !pcmReader.TryRead(samples, frames, out consumedFrames, out readFailure))
            {
                Fail("sample-provider-read-failed", Description + ", error=" + readFailure);
                return true;
            }
            if (consumedFrames == 0)
            {
                emptyProviderReads++;
                if (emptyProviderReads > MaximumConsecutiveEmptyProviderReads)
                {
                    Fail("sample-provider-stalled", Description + ", offset=" + offsetFrames);
                    return true;
                }
                return false;
            }

            emptyProviderReads = 0;
            AppendSamples(samples, checked(consumedFrames * Channels));
            offsetFrames += consumedFrames;
            return offsetFrames >= SampleFrames;
        }

        private void AppendSamples(float[] samples, int valueCount)
        {
            var pcm = new byte[checked(valueCount * 2)];
            for (var index = 0; index < valueCount; index++)
            {
                var value = (short)Math.Round(
                    Math.Max(-1f, Math.Min(1f, samples[index])) * short.MaxValue);
                pcm[index * 2] = (byte)value;
                pcm[index * 2 + 1] = (byte)(value >> 8);
            }
            chunks.Add(pcm);
            PcmBytes = checked(PcmBytes + pcm.Length);
        }

        internal IReadOnlyList<byte[]> DetachChunks()
        {
            var result = chunks.ToArray();
            chunks.Clear();
            return result;
        }

        internal void Finish(ReplayAudioCaptureResultV12 result)
        {
            pcmReader?.Dispose();
            pcmReader = null;
            foreach (var completion in completions.ToArray())
            {
                try { completion(result); } catch { }
            }
            completions.Clear();
            chunks.Clear();
        }

        private void Fail(string code, string message)
        {
            FailureCode = code;
            FailureMessage = message;
        }
    }
}

internal sealed class ReplayAudioCaptureResultV12
{
    internal ReplayAssetV12? Attachment { get; private set; }

    internal string FailureCode { get; private set; } = "";

    internal string Message { get; private set; } = "";

    internal bool Success => Attachment != null;

    internal static ReplayAudioCaptureResultV12 Succeeded(ReplayAssetV12 attachment)
    {
        return new ReplayAudioCaptureResultV12 { Attachment = attachment };
    }

    internal static ReplayAudioCaptureResultV12 Failed(string code, string message)
    {
        return new ReplayAudioCaptureResultV12
        {
            FailureCode = string.IsNullOrWhiteSpace(code) ? "unknown" : code.Trim(),
            Message = message ?? ""
        };
    }
}
