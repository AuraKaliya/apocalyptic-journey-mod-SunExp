using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal sealed class ReplayAudioAttachmentCaptureV11
{
    private static readonly MethodInfo? AudioClipGetData = typeof(AudioClip)
        .GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
    private readonly Dictionary<string, ReplayAttachmentV11> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CaptureState> pending = new(StringComparer.Ordinal);
    private long generation;

    internal int PendingCount => pending.Count;

    internal event Action? Drained;

    internal void Request(
        AudioClip? clip,
        string usage,
        Action<ReplayAttachmentV11?> completion)
    {
        if (clip == null || clip.samples <= 0 || clip.channels is < 1 or > 2 || clip.frequency <= 0)
        {
            completion?.Invoke(null);
            return;
        }

        var key = Key(clip);
        if (completed.TryGetValue(key, out var cached))
        {
            completion?.Invoke(cached);
            return;
        }
        if (pending.TryGetValue(key, out var existing))
        {
            existing.Add(completion);
            return;
        }
        if ((long)clip.samples * clip.channels > 32L * 1024L * 1024L)
        {
            completion?.Invoke(null);
            return;
        }

        var state = new CaptureState(key, Interlocked.Increment(ref generation), clip, usage);
        state.Add(completion);
        pending[key] = state;
        var queued = AuraSharedFrameScheduler.RunCooperative(new AuraSharedFrameWorkRequest
        {
            OwnerId = "AuraToolsExp",
            Key = "ReplayV11.AudioRead." + state.Generation,
            Source = "MatchRecords.ReplayV11.AudioRead",
            DelayFrames = 1,
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 5,
            EstimatedCost = 1,
            SliceBudgetMilliseconds = 1.5d,
            MaximumSlices = 2048,
            IsCancelled = () => state.Cancelled,
            ExecuteSlice = _ => state.ReadSlice(),
            OnCompleted = _ => QueueFinalize(state),
            OnCancelled = _ => Complete(state, null),
            OnFailed = (_, _) => Complete(state, null)
        });
        if (!queued) Complete(state, null);
    }

    internal void Cancel()
    {
        foreach (var state in pending.Values)
        {
            state.Cancelled = true;
            state.Finish(null);
        }
        pending.Clear();
        Drained = null;
    }

    private void QueueFinalize(CaptureState state)
    {
        if (state.Cancelled)
        {
            Complete(state, null);
            return;
        }
        var chunks = state.DetachChunks();
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<ReplayAttachmentV11>
            {
                OwnerId = "AuraToolsExp",
                Key = "ReplayV11.AudioFinalize." + state.Generation,
                Source = "MatchRecords.ReplayV11.AudioFinalize",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = cancellation => BuildAttachment(state, chunks, cancellation),
                IsStillCurrent = () => !state.Cancelled,
                ApplyOnMainThread = attachment => Complete(state, attachment),
                OnFailedOnMainThread = _ => Complete(state, null)
            });
        if (!accepted) Complete(state, null);
    }

    private static ReplayAttachmentV11 BuildAttachment(
        CaptureState state,
        IReadOnlyList<byte[]> chunks,
        CancellationToken cancellation)
    {
        var payload = new byte[checked(44 + state.PcmBytes)];
        WriteWaveHeader(payload, state.PcmBytes, state.Channels, state.Frequency);
        var offset = 44;
        foreach (var chunk in chunks)
        {
            cancellation.ThrowIfCancellationRequested();
            Buffer.BlockCopy(chunk, 0, payload, offset, chunk.Length);
            offset += chunk.Length;
        }
        var hash = ReplayCanonicalJsonV11.Sha256(payload);
        return new ReplayAttachmentV11
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

    private void Complete(CaptureState state, ReplayAttachmentV11? attachment)
    {
        if (!pending.TryGetValue(state.Key, out var current) || !ReferenceEquals(current, state)) return;
        pending.Remove(state.Key);
        if (attachment != null) completed[state.Key] = attachment;
        state.Finish(attachment);
        if (pending.Count == 0) Drained?.Invoke();
    }

    private static string Key(AudioClip clip)
    {
        return clip.GetInstanceID() + "|" + clip.samples + "|" + clip.channels + "|" + clip.frequency;
    }

    private static void WriteWaveHeader(byte[] target, int dataBytes, int channels, int frequency)
    {
        WriteAscii(target, 0, "RIFF");
        WriteInt32(target, 4, 36 + dataBytes);
        WriteAscii(target, 8, "WAVEfmt ");
        WriteInt32(target, 16, 16);
        WriteInt16(target, 20, 1);
        WriteInt16(target, 22, channels);
        WriteInt32(target, 24, frequency);
        WriteInt32(target, 28, frequency * channels * 2);
        WriteInt16(target, 32, channels * 2);
        WriteAscii(target, 36, "data");
        WriteInt32(target, 40, dataBytes);
    }

    private static void WriteAscii(byte[] target, int offset, string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, target, offset, bytes.Length);
    }

    private static void WriteInt16(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private sealed class CaptureState
    {
        private const int MaximumValuesPerSlice = 65_536;
        private readonly AudioClip clip;
        private readonly List<byte[]> chunks = new();
        private readonly List<Action<ReplayAttachmentV11?>> completions = new();
        private int offsetFrames;

        internal CaptureState(string key, long generation, AudioClip clip, string usage)
        {
            Key = key;
            Generation = generation;
            this.clip = clip;
            Usage = string.IsNullOrWhiteSpace(usage) ? "ReplayAudio" : usage.Trim();
            SampleFrames = clip.samples;
            Channels = clip.channels;
            Frequency = clip.frequency;
        }

        internal string Key { get; }
        internal long Generation { get; }
        internal string Usage { get; }
        internal int SampleFrames { get; }
        internal int Channels { get; }
        internal int Frequency { get; }
        internal int PcmBytes { get; private set; }
        internal bool Cancelled { get; set; }

        internal void Add(Action<ReplayAttachmentV11?> completion)
        {
            if (completion != null) completions.Add(completion);
        }

        internal bool ReadSlice()
        {
            if (Cancelled || offsetFrames >= SampleFrames) return true;
            var maximumFrames = Math.Max(1, MaximumValuesPerSlice / Channels);
            var frames = Math.Min(maximumFrames, SampleFrames - offsetFrames);
            var samples = new float[checked(frames * Channels)];
            if (AudioClipGetData?.Invoke(clip, new object[] { samples, offsetFrames }) is not true)
            {
                Cancelled = true;
                return true;
            }
            var pcm = new byte[checked(samples.Length * 2)];
            for (var index = 0; index < samples.Length; index++)
            {
                var value = (short)Math.Round(Math.Max(-1f, Math.Min(1f, samples[index])) * short.MaxValue);
                pcm[index * 2] = (byte)value;
                pcm[index * 2 + 1] = (byte)(value >> 8);
            }
            chunks.Add(pcm);
            PcmBytes = checked(PcmBytes + pcm.Length);
            offsetFrames += frames;
            return offsetFrames >= SampleFrames;
        }

        internal IReadOnlyList<byte[]> DetachChunks()
        {
            var result = chunks.ToArray();
            chunks.Clear();
            return result;
        }

        internal void Finish(ReplayAttachmentV11? attachment)
        {
            foreach (var completion in completions.ToArray())
            {
                try { completion(attachment); } catch { }
            }
            completions.Clear();
            chunks.Clear();
        }
    }
}
