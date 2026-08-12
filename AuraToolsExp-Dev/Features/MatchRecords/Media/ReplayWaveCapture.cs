using System;
using System.Collections.Concurrent;
using System.IO;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal sealed class ReplayWaveCapture : MonoBehaviour
{
    private readonly ConcurrentQueue<AudioBlock> pending = new();
    private volatile bool capturing;
    private int channels = 2;

    internal int SampleRate { get; private set; } = 48000;

    internal int Channels => Math.Max(1, channels);

    internal void BeginCapture()
    {
        SampleRate = Math.Max(8000, AudioSettings.outputSampleRate);
        capturing = true;
    }

    internal void EndCapture()
    {
        capturing = false;
    }

    internal void DrainTo(ReplayWaveWriter writer)
    {
        while (pending.TryDequeue(out var block))
        {
            writer.Write(block.Samples, block.Channels);
        }
    }

    private void OnAudioFilterRead(float[] data, int channelCount)
    {
        if (!capturing || data == null || data.Length == 0)
        {
            return;
        }

        channels = Math.Max(1, channelCount);
        var copy = new float[data.Length];
        Array.Copy(data, copy, data.Length);
        pending.Enqueue(new AudioBlock(copy, channels));
    }

    private readonly struct AudioBlock
    {
        internal AudioBlock(float[] samples, int channels)
        {
            Samples = samples;
            Channels = channels;
        }

        internal float[] Samples { get; }
        internal int Channels { get; }
    }
}

internal sealed class ReplayWaveWriter : IDisposable
{
    private readonly FileStream stream;
    private readonly BinaryWriter writer;
    private readonly int channels;
    private readonly int sampleRate;
    private long dataBytes;

    internal ReplayWaveWriter(string path, int sampleRate, int channels)
    {
        this.channels = Math.Max(1, Math.Min(8, channels));
        this.sampleRate = Math.Max(8000, Math.Min(192000, sampleRate));
        stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer = new BinaryWriter(stream);
        WriteHeader(0);
    }

    internal void Write(float[] samples, int sourceChannels)
    {
        if (samples == null || samples.Length == 0) return;
        var normalizedSource = Math.Max(1, sourceChannels);
        for (var index = 0; index < samples.Length; index += normalizedSource)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var sourceIndex = index + Math.Min(channel, normalizedSource - 1);
                var value = sourceIndex < samples.Length ? Math.Max(-1f, Math.Min(1f, samples[sourceIndex])) : 0f;
                writer.Write((short)Math.Round(value * short.MaxValue));
                dataBytes += 2;
            }
        }
    }

    public void Dispose()
    {
        writer.Flush();
        stream.Position = 0;
        WriteHeader(dataBytes);
        writer.Dispose();
        stream.Dispose();
    }

    private void WriteHeader(long bytes)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked((int)Math.Min(int.MaxValue, 36 + bytes)));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(checked((int)Math.Min(int.MaxValue, bytes)));
    }
}
