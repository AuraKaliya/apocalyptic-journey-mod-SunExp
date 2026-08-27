using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class ReplayOfflineAudioMixer
{
    internal const int SampleRate = 48_000;
    internal const int Channels = 2;
    private const int BlockFrames = 4096;
    private const int MaximumDecodedClipSamples = 32 * 1024 * 1024;

    internal static long MixToWave(
        ReplayDocumentV12 document,
        long videoFrameCount,
        int framesPerSecond,
        Func<string, string> resolveAsset,
        string outputPath)
    {
        var sampleFrames = Math.Max(1L, videoFrameCount * SampleRate / Math.Max(1, framesPerSecond));
        if (sampleFrames > (int.MaxValue - 44L) / (Channels * 2L))
        {
            throw new IOException("离线音频轨道超过 WAV 安全大小。");
        }

        var cache = new Dictionary<string, DecodedAudio>(StringComparer.OrdinalIgnoreCase);
        var cues = new List<DecodedCue>();
        long decodedSampleCount = 0;
        foreach (var cue in document.PresentationEvents
                     .Select(item => item.Presentation?.Audio)
                     .Where(item => item != null)
                     .Select(item => item!))
        {
            if (string.IsNullOrWhiteSpace(cue.AssetSha256))
                throw new InvalidDataException("v12 回放音频缺少冻结的 PCM 资源。");
            if (!cache.TryGetValue(cue.AssetSha256, out var audio))
            {
                var path = resolveAsset(cue.AssetSha256);
                if (!TryReadPcm16Wave(path, out var decoded))
                    throw new InvalidDataException("v12 回放音频资源无法解码：" + cue.AssetSha256);
                audio = decoded;
                decodedSampleCount += audio.Samples.LongLength;
                if (decodedSampleCount > 64L * 1024L * 1024L)
                {
                    throw new InvalidDataException("回放音频附件的解码内存预算超限。");
                }
                cache[cue.AssetSha256] = audio;
            }
            if (audio.Samples.Length > 0) cues.Add(new DecodedCue(cue, audio));
        }

        using var transaction = AuraSharedFileStore.BeginWrite(AuraToolsIds.ModId, outputPath, overwrite: true);
        using (var writer = new BinaryWriter(transaction.Stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(ReplayPcm16WaveContractV12.BuildHeader(sampleFrames, Channels, SampleRate));
            var block = new float[BlockFrames * Channels];
            for (var blockStart = 0L; blockStart < sampleFrames; blockStart += BlockFrames)
            {
                var count = (int)Math.Min(BlockFrames, sampleFrames - blockStart);
                Array.Clear(block, 0, count * Channels);
                foreach (var cue in cues) MixBlock(block, blockStart, count, cue, sampleFrames);
                for (var index = 0; index < count * Channels; index++)
                {
                    writer.Write((short)Math.Round(Clamp(block[index]) * short.MaxValue));
                }
            }
            writer.Flush();
        }
        transaction.Commit();
        return sampleFrames;
    }

    private static void MixBlock(
        float[] output,
        long blockStart,
        int blockFrames,
        DecodedCue decoded,
        long totalOutputFrames)
    {
        var cue = decoded.Cue;
        var audio = decoded.Audio;
        var sourceFrames = audio.Samples.Length / audio.Channels;
        var sourceStart = (int)Math.Max(0, Math.Min(sourceFrames, cue.SourceOffsetSample));
        var playbackRate = Math.Max(1d / 16d, cue.PlaybackRateQ16 / 65536d);
        var availableOutputFrames = (long)Math.Floor(
            (sourceFrames - sourceStart) / (double)audio.SampleRate * SampleRate / playbackRate);
        var targetStart = Math.Max(0, cue.StartSample);
        var requested = cue.DurationSamples > 0
            ? Math.Min(cue.DurationSamples, availableOutputFrames)
            : cue.LoopEndSample > cue.LoopStartSample
                ? Math.Max(0, totalOutputFrames - targetStart)
                : availableOutputFrames;
        var targetEnd = Math.Min(totalOutputFrames, targetStart + requested);
        var overlapStart = Math.Max(blockStart, targetStart);
        var overlapEnd = Math.Min(blockStart + blockFrames, targetEnd);
        if (overlapStart >= overlapEnd) return;

        var gain = cue.GainQ16 / 65536f;
        var pan = Math.Max(-1f, Math.Min(1f, cue.PanQ16 / 65536f));
        var leftGain = gain * (pan > 0f ? 1f - pan : 1f);
        var rightGain = gain * (pan < 0f ? 1f + pan : 1f);
        for (var targetFrame = overlapStart; targetFrame < overlapEnd; targetFrame++)
        {
            var cueFrame = targetFrame - targetStart;
            var sourcePosition = sourceStart + cueFrame * audio.SampleRate / (double)SampleRate * playbackRate;
            if (cue.LoopEndSample > cue.LoopStartSample && sourcePosition >= cue.LoopEndSample)
            {
                var loopLength = cue.LoopEndSample - cue.LoopStartSample;
                sourcePosition = cue.LoopStartSample + (sourcePosition - cue.LoopStartSample) % loopLength;
            }
            var firstFrame = Math.Min(sourceFrames - 1, (int)Math.Floor(sourcePosition));
            var secondFrame = Math.Min(sourceFrames - 1, firstFrame + 1);
            var fraction = (float)(sourcePosition - firstFrame);
            var firstIndex = firstFrame * audio.Channels;
            var secondIndex = secondFrame * audio.Channels;
            var left = Lerp(audio.Samples[firstIndex], audio.Samples[secondIndex], fraction);
            var right = audio.Channels == 1
                ? left
                : Lerp(audio.Samples[firstIndex + 1], audio.Samples[secondIndex + 1], fraction);
            var envelope = 1f;
            if (cue.FadeInSamples > 0 && cueFrame < cue.FadeInSamples)
            {
                envelope = Math.Min(envelope, cueFrame / (float)cue.FadeInSamples);
            }
            if (cue.FadeOutSamples > 0 && requested - cueFrame <= cue.FadeOutSamples)
            {
                envelope = Math.Min(envelope, (requested - cueFrame) / (float)cue.FadeOutSamples);
            }
            var outputIndex = (int)(targetFrame - blockStart) * Channels;
            output[outputIndex] = Clamp(output[outputIndex] + left * leftGain * envelope);
            output[outputIndex + 1] = Clamp(output[outputIndex + 1] + right * rightGain * envelope);
        }
    }

    private static bool TryReadPcm16Wave(string path, out DecodedAudio result)
    {
        result = DecodedAudio.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            var payload = File.ReadAllBytes(path);
            if (!ReplayPcm16WaveContractV12.TryRead(payload, out var wave, out _)) return false;
            var samples = ReplayPcm16WaveContractV12.DecodeSamples(
                payload,
                wave,
                MaximumDecodedClipSamples);
            result = new DecodedAudio(samples, wave.SampleRate, wave.Channels);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static float Clamp(float value) => Math.Max(-1f, Math.Min(1f, value));

    private static float Lerp(float first, float second, float amount) => first + (second - first) * amount;

    private readonly struct DecodedCue
    {
        internal DecodedCue(ReplayAudioCueV12 cue, DecodedAudio audio)
        {
            Cue = cue;
            Audio = audio;
        }
        internal ReplayAudioCueV12 Cue { get; }
        internal DecodedAudio Audio { get; }
    }

    private readonly struct DecodedAudio
    {
        internal static readonly DecodedAudio Empty = new(Array.Empty<float>(), 0, 0);
        internal DecodedAudio(float[] samples, int sampleRate, int channels)
        {
            Samples = samples;
            SampleRate = sampleRate;
            Channels = channels;
        }
        internal float[] Samples { get; }
        internal int SampleRate { get; }
        internal int Channels { get; }
    }
}
