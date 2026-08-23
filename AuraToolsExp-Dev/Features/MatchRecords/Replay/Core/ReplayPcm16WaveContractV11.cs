using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal readonly struct ReplayPcm16WaveInfoV11
{
    internal ReplayPcm16WaveInfoV11(
        int sampleRate,
        int channels,
        int bitsPerSample,
        int dataOffset,
        int dataLength)
    {
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        DataOffset = dataOffset;
        DataLength = dataLength;
    }

    internal int SampleRate { get; }
    internal int Channels { get; }
    internal int BitsPerSample { get; }
    internal int DataOffset { get; }
    internal int DataLength { get; }
    internal long SampleFrames => Channels <= 0 ? 0 : DataLength / (Channels * 2L);
}

internal static class ReplayPcm16WaveContractV11
{
    internal const int HeaderBytes = 44;
    internal const int BitsPerSample = 16;
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
            throw new InvalidOperationException(
                "Replay PCM chunk bytes do not match sample metadata: expected="
                + dataBytes + ", actual=" + actualBytes + ".");

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
        var header = new byte[HeaderBytes];
        WriteHeader(header, dataBytes, channels, sampleRate);
        return header;
    }

    internal static bool TryRead(
        byte[]? payload,
        out ReplayPcm16WaveInfoV11 info,
        out string error)
    {
        info = default;
        error = "";
        if (payload == null)
        {
            error = "payload-missing";
            return false;
        }
        return TryReadHeader(payload, payload.LongLength, allowMissingBits: false, out info, out error);
    }

    internal static bool TryRepairLegacyMissingBits(
        byte[]? payload,
        out byte[] repaired,
        out ReplayPcm16WaveInfoV11 info,
        out string error)
    {
        repaired = Array.Empty<byte>();
        info = default;
        error = "";
        if (payload == null)
        {
            error = "payload-missing";
            return false;
        }
        if (!TryReadHeader(payload, payload.LongLength, allowMissingBits: true, out var legacy, out error))
            return false;
        if (legacy.BitsPerSample != 0)
        {
            error = "not-legacy-missing-bits";
            return false;
        }

        repaired = (byte[])payload.Clone();
        WriteInt16(repaired, 34, BitsPerSample);
        if (!TryRead(repaired, out info, out error))
        {
            repaired = Array.Empty<byte>();
            return false;
        }
        return true;
    }

    internal static bool TryNormalizeLegacyAttachments(
        ReplayDocumentV11 document,
        out int repairedAttachments,
        out string error)
    {
        repairedAttachments = 0;
        error = "";
        if (document == null)
        {
            error = "document-missing";
            return false;
        }

        var plans = new List<(ReplayAttachmentV11 Attachment, string OldHash, string NewHash, byte[] Payload, ReplayPcm16WaveInfoV11 Wave)>();
        foreach (var attachment in document.Attachments ?? new List<ReplayAttachmentV11>())
        {
            if (!string.Equals(attachment.MediaType, "audio/wav", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(attachment.Extension, ".wav", StringComparison.OrdinalIgnoreCase))
                continue;
            if (attachment.Payload == null || attachment.Payload.Length == 0)
            {
                error = "PCM attachment payload is missing: " + attachment.Sha256;
                return false;
            }
            if (!string.Equals(
                    ReplayCanonicalJsonV11.Sha256(attachment.Payload),
                    attachment.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "PCM attachment hash mismatch: " + attachment.Sha256;
                return false;
            }
            if (TryRead(attachment.Payload, out var current, out _))
            {
                if (current.SampleRate != attachment.SampleRate
                    || current.Channels != attachment.Channels
                    || current.SampleFrames != attachment.SampleFrames)
                {
                    error = "PCM attachment metadata mismatch: " + attachment.Sha256;
                    return false;
                }
                continue;
            }
            if (!TryRepairLegacyMissingBits(
                    attachment.Payload,
                    out var repaired,
                    out var wave,
                    out var repairError))
            {
                error = "PCM attachment is not migratable: " + attachment.Sha256 + " (" + repairError + ")";
                return false;
            }
            plans.Add((
                attachment,
                attachment.Sha256,
                ReplayCanonicalJsonV11.Sha256(repaired),
                repaired,
                wave));
        }

        var replacements = plans.ToDictionary(value => value.OldHash, value => value.NewHash, StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            plan.Attachment.Sha256 = plan.NewHash;
            plan.Attachment.MediaType = "audio/wav";
            plan.Attachment.Extension = ".wav";
            plan.Attachment.ByteLength = plan.Payload.LongLength;
            plan.Attachment.SampleRate = plan.Wave.SampleRate;
            plan.Attachment.Channels = plan.Wave.Channels;
            plan.Attachment.SampleFrames = plan.Wave.SampleFrames;
            plan.Attachment.Payload = plan.Payload;
        }
        foreach (var cue in (document.Events ?? new List<ReplayTimelineEventV11>())
                     .SelectMany(value => value.Audio ?? new List<ReplayAudioCueV11>()))
        {
            if (replacements.TryGetValue(cue.AssetSha256, out var replacement))
                cue.AssetSha256 = replacement;
        }
        repairedAttachments = plans.Count;
        return true;
    }

    internal static float[] DecodeSamples(
        byte[] payload,
        ReplayPcm16WaveInfoV11 info,
        int maximumSampleValues)
    {
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        var sampleValues = info.DataLength / BytesPerSample;
        if (sampleValues < 0 || sampleValues > maximumSampleValues)
            throw new InvalidOperationException("Replay PCM decoded sample budget exceeded.");
        var samples = new float[sampleValues];
        for (var index = 0; index < sampleValues; index++)
            samples[index] = ReadInt16(payload, info.DataOffset + index * BytesPerSample) / 32768f;
        return samples;
    }

    internal static bool TryReadHeader(
        byte[] header,
        long totalLength,
        bool allowMissingBits,
        out ReplayPcm16WaveInfoV11 info,
        out string error)
    {
        info = default;
        error = "";
        if (header.Length < HeaderBytes || totalLength < HeaderBytes)
            return Fail("header-too-short", out error);
        if (!AsciiEquals(header, 0, "RIFF")
            || !AsciiEquals(header, 8, "WAVE")
            || !AsciiEquals(header, 12, "fmt ")
            || !AsciiEquals(header, 36, "data"))
            return Fail("canonical-chunks-missing", out error);
        if (ReadInt32(header, 4) != totalLength - 8L)
            return Fail("riff-size-mismatch", out error);
        if (ReadInt32(header, 16) != 16)
            return Fail("fmt-size-invalid", out error);
        if (ReadInt16(header, 20) != 1)
            return Fail("format-not-pcm", out error);

        var channels = ReadInt16(header, 22);
        var sampleRate = ReadInt32(header, 24);
        var byteRate = ReadInt32(header, 28);
        var blockAlign = ReadInt16(header, 32);
        var bits = ReadInt16(header, 34);
        var dataLength = ReadInt32(header, 40);
        if (channels is < 1 or > 2) return Fail("channels-invalid", out error);
        if (sampleRate <= 0) return Fail("sample-rate-invalid", out error);
        if (blockAlign != channels * BytesPerSample) return Fail("block-align-invalid", out error);
        if (byteRate != sampleRate * blockAlign) return Fail("byte-rate-invalid", out error);
        if (bits != BitsPerSample && !(allowMissingBits && bits == 0))
            return Fail("bits-per-sample-invalid", out error);
        if (dataLength < 0 || dataLength != totalLength - HeaderBytes)
            return Fail("data-size-mismatch", out error);
        if (dataLength % blockAlign != 0) return Fail("sample-frame-alignment-invalid", out error);

        info = new ReplayPcm16WaveInfoV11(sampleRate, channels, bits, HeaderBytes, dataLength);
        return true;
    }

    private static int CheckedDataBytes(long sampleFrames, int channels, int sampleRate)
    {
        if (sampleFrames < 0) throw new ArgumentOutOfRangeException(nameof(sampleFrames));
        if (channels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var bytes = checked(sampleFrames * channels * (long)BytesPerSample);
        if (bytes > int.MaxValue - HeaderBytes)
            throw new InvalidOperationException("Replay PCM WAV exceeds the canonical RIFF size budget.");
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
        WriteInt16(target, 34, BitsPerSample);
        WriteAscii(target, 36, "data");
        WriteInt32(target, 40, dataBytes);
    }

    private static bool Fail(string value, out string error)
    {
        error = value;
        return false;
    }

    private static bool AsciiEquals(byte[] value, int offset, string expected)
    {
        if (offset < 0 || offset + expected.Length > value.Length) return false;
        for (var index = 0; index < expected.Length; index++)
            if (value[offset + index] != (byte)expected[index]) return false;
        return true;
    }

    private static void WriteAscii(byte[] target, int offset, string value)
    {
        for (var index = 0; index < value.Length; index++) target[offset + index] = (byte)value[index];
    }

    private static short ReadInt16(byte[] value, int offset)
    {
        return (short)(value[offset] | value[offset + 1] << 8);
    }

    private static int ReadInt32(byte[] value, int offset)
    {
        return value[offset]
               | value[offset + 1] << 8
               | value[offset + 2] << 16
               | value[offset + 3] << 24;
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
}
