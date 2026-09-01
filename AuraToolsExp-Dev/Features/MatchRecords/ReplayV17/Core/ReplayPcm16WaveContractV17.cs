using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal readonly struct ReplayPcm16WaveInfoV17
{
    internal ReplayPcm16WaveInfoV17(int sampleRate, int channels, int dataOffset, int dataLength)
    {
        SampleRate = sampleRate;
        Channels = channels;
        DataOffset = dataOffset;
        DataLength = dataLength;
    }

    internal int SampleRate { get; }
    internal int Channels { get; }
    internal int DataOffset { get; }
    internal int DataLength { get; }
    internal long SampleFrames => Channels <= 0 ? 0 : DataLength / (Channels * 2L);
}

internal static class ReplayPcm16WaveContractV17
{
    internal const int HeaderBytes = 44;
    internal const int BytesPerSample = 2;

    internal static byte[] BuildPayload(
        IReadOnlyList<byte[]> chunks,
        long sampleFrames,
        int channels,
        int sampleRate)
    {
        var dataBytes = CheckedDataBytes(sampleFrames, channels, sampleRate);
        long actualBytes = 0;
        foreach (var chunk in chunks ?? Array.Empty<byte[]>())
            actualBytes = checked(actualBytes + (chunk?.LongLength ?? 0L));
        if (actualBytes != dataBytes)
            throw new InvalidOperationException("Replay PCM bytes do not match the declared sample metadata.");
        var payload = new byte[checked(HeaderBytes + dataBytes)];
        WriteHeader(payload, dataBytes, channels, sampleRate);
        var offset = HeaderBytes;
        foreach (var chunk in chunks ?? Array.Empty<byte[]>())
        {
            if (chunk == null || chunk.Length == 0) continue;
            Buffer.BlockCopy(chunk, 0, payload, offset, chunk.Length);
            offset += chunk.Length;
        }
        return payload;
    }

    internal static byte[] BuildHeader(long sampleFrames, int channels, int sampleRate)
    {
        var dataBytes = CheckedDataBytes(sampleFrames, channels, sampleRate);
        var result = new byte[HeaderBytes];
        WriteHeader(result, dataBytes, channels, sampleRate);
        return result;
    }

    internal static bool TryRead(byte[]? payload, out ReplayPcm16WaveInfoV17 info, out string error)
    {
        info = default;
        error = "";
        if (payload == null || payload.Length < HeaderBytes) return Fail("header-too-short", out error);
        if (!AsciiEquals(payload, 0, "RIFF")
            || !AsciiEquals(payload, 8, "WAVE")
            || !AsciiEquals(payload, 12, "fmt ")
            || !AsciiEquals(payload, 36, "data"))
            return Fail("canonical-chunks-missing", out error);
        if (ReadInt32(payload, 4) != payload.LongLength - 8L
            || ReadInt32(payload, 16) != 16
            || ReadInt16(payload, 20) != 1)
            return Fail("wave-header-invalid", out error);
        var channels = ReadInt16(payload, 22);
        var sampleRate = ReadInt32(payload, 24);
        var byteRate = ReadInt32(payload, 28);
        var blockAlign = ReadInt16(payload, 32);
        var bits = ReadInt16(payload, 34);
        var dataLength = ReadInt32(payload, 40);
        if (channels is < 1 or > 2) return Fail("channels-invalid", out error);
        if (sampleRate <= 0 || bits != 16) return Fail("sample-format-invalid", out error);
        if (blockAlign != channels * BytesPerSample || byteRate != sampleRate * blockAlign)
            return Fail("sample-layout-invalid", out error);
        if (dataLength < 0 || dataLength != payload.LongLength - HeaderBytes || dataLength % blockAlign != 0)
            return Fail("data-size-invalid", out error);
        info = new ReplayPcm16WaveInfoV17(sampleRate, channels, HeaderBytes, dataLength);
        return true;
    }

    internal static float[] DecodeSamples(byte[] payload, ReplayPcm16WaveInfoV17 info, int maximumSampleValues)
    {
        var count = info.DataLength / BytesPerSample;
        if (count < 0 || count > maximumSampleValues)
            throw new InvalidOperationException("Replay PCM decoded sample budget exceeded.");
        var result = new float[count];
        for (var index = 0; index < count; index++)
            result[index] = ReadInt16(payload, info.DataOffset + index * BytesPerSample) / 32768f;
        return result;
    }

    private static int CheckedDataBytes(long sampleFrames, int channels, int sampleRate)
    {
        if (sampleFrames < 0) throw new ArgumentOutOfRangeException(nameof(sampleFrames));
        if (channels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var bytes = checked(sampleFrames * channels * (long)BytesPerSample);
        if (bytes > int.MaxValue - HeaderBytes) throw new InvalidOperationException("Replay PCM WAV exceeds the RIFF size budget.");
        return (int)bytes;
    }

    private static void WriteHeader(byte[] target, int dataBytes, int channels, int sampleRate)
    {
        WriteAscii(target, 0, "RIFF");
        WriteInt32(target, 4, 36 + dataBytes);
        WriteAscii(target, 8, "WAVEfmt ");
        WriteInt32(target, 16, 16);
        WriteInt16(target, 20, 1);
        WriteInt16(target, 22, channels);
        WriteInt32(target, 24, sampleRate);
        WriteInt32(target, 28, checked(sampleRate * channels * BytesPerSample));
        WriteInt16(target, 32, channels * BytesPerSample);
        WriteInt16(target, 34, 16);
        WriteAscii(target, 36, "data");
        WriteInt32(target, 40, dataBytes);
    }

    private static bool Fail(string value, out string error) { error = value; return false; }

    private static bool AsciiEquals(byte[] value, int offset, string expected)
    {
        for (var index = 0; index < expected.Length; index++)
            if (offset + index >= value.Length || value[offset + index] != (byte)expected[index]) return false;
        return true;
    }

    private static void WriteAscii(byte[] target, int offset, string value)
    {
        for (var index = 0; index < value.Length; index++) target[offset + index] = (byte)value[index];
    }

    private static short ReadInt16(byte[] value, int offset) => (short)(value[offset] | value[offset + 1] << 8);
    private static int ReadInt32(byte[] value, int offset) => value[offset] | value[offset + 1] << 8 | value[offset + 2] << 16 | value[offset + 3] << 24;
    private static void WriteInt16(byte[] target, int offset, int value) { target[offset] = (byte)value; target[offset + 1] = (byte)(value >> 8); }
    private static void WriteInt32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }
}
