using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class ReplayOfflineAudioMixer
{
    internal const int SampleRate = 48_000;
    internal const int Channels = 2;
    private const int BlockFrames = 4096;
    private const int MaximumDecodedClipSamples = 32 * 1024 * 1024;

    internal static long MixToWave(
        ReplayDocumentV11 document,
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
        foreach (var cue in document.Events.SelectMany(item => item.Audio ?? new List<ReplayAudioCueV11>()))
        {
            if (string.IsNullOrWhiteSpace(cue.AssetSha256))
                throw new InvalidDataException("v11 回放音频缺少冻结的 PCM 附件。");
            if (!cache.TryGetValue(cue.AssetSha256, out var audio))
            {
                var path = resolveAsset(cue.AssetSha256);
                if (!TryReadPcm16Wave(path, out var decoded))
                    throw new InvalidDataException("v11 回放音频附件无法解码：" + cue.AssetSha256);
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
            WriteHeader(writer, sampleFrames);
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
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (new string(reader.ReadChars(4)) != "RIFF") return false;
            reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") return false;
            short format = 0;
            short bits = 0;
            var sampleRate = 0;
            var channels = 0;
            byte[] data = Array.Empty<byte>();
            while (stream.Position + 8 <= stream.Length)
            {
                var chunk = new string(reader.ReadChars(4));
                var length = reader.ReadInt32();
                if (length < 0 || stream.Position + length > stream.Length) return false;
                if (chunk == "fmt ")
                {
                    format = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bits = reader.ReadInt16();
                    stream.Position += Math.Max(0, length - 16);
                }
                else if (chunk == "data") data = reader.ReadBytes(length);
                else stream.Position += length;
                if ((length & 1) != 0 && stream.Position < stream.Length) stream.Position++;
            }
            if (format != 1 || bits != 16 || sampleRate <= 0 || channels is < 1 or > 2
                || data.Length % 2 != 0 || data.Length / 2 > MaximumDecodedClipSamples)
            {
                return false;
            }
            var samples = new float[data.Length / 2];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = BitConverter.ToInt16(data, index * 2) / 32768f;
            }
            result = new DecodedAudio(samples, sampleRate, channels);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteHeader(BinaryWriter writer, long sampleFrames)
    {
        var dataBytes = checked((int)(sampleFrames * Channels * 2));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * 2);
        writer.Write((short)(Channels * 2));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
    }

    private static float Clamp(float value) => Math.Max(-1f, Math.Min(1f, value));

    private static float Lerp(float first, float second, float amount) => first + (second - first) * amount;

    private readonly struct DecodedCue
    {
        internal DecodedCue(ReplayAudioCueV11 cue, DecodedAudio audio)
        {
            Cue = cue;
            Audio = audio;
        }
        internal ReplayAudioCueV11 Cue { get; }
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
