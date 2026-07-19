using System;
using System.IO;
using System.Text;

namespace AuraAudio.Shared;

public enum AudioFileFormat
{
    Unknown,
    Mp3,
    WavPcm,
    WavIeeeFloat,
    OggVorbis,
    OggOpus,
    IsoBaseMedia
}

public sealed class AudioFileFormatDescriptor
{
    public bool Success { get; set; }

    public AudioFileFormat Format { get; set; }

    public string Container { get; set; } = "Unknown";

    public string Codec { get; set; } = "Unknown";

    public string CanonicalExtension { get; set; } = "";

    public string FailureCode { get; set; } = "";

    public string Message { get; set; } = "";

    public string Describe()
    {
        return "success=" + Success
               + ", format=" + Format
               + ", container=" + Container
               + ", codec=" + Codec
               + ", canonicalExtension=" + Display(CanonicalExtension)
               + ", failureCode=" + Display(FailureCode);
    }

    private static string Display(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
    }
}

public static class AudioFileFormatProbe
{
    private const int MaximumProbeBytes = 128 * 1024;

    public static AudioFileFormatDescriptor Probe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Failure("empty-path", "Audio path is empty.");
        }

        if (!File.Exists(path))
        {
            return Failure("file-missing", "Audio file does not exist.");
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(stream.Length, MaximumProbeBytes);
            if (length <= 0)
            {
                return Failure("empty-file", "Audio file is empty.");
            }

            var bytes = new byte[length];
            var read = 0;
            while (read < length)
            {
                var count = stream.Read(bytes, read, length - read);
                if (count <= 0)
                {
                    break;
                }

                read += count;
            }

            if (read != bytes.Length)
            {
                Array.Resize(ref bytes, read);
            }

            var descriptor = Probe(bytes);
            if (descriptor.Success || !TryGetId3AudioOffset(bytes, out var audioOffset) || audioOffset < bytes.Length)
            {
                return descriptor;
            }

            if (audioOffset >= stream.Length)
            {
                return Failure("invalid-id3-size", "ID3 metadata extends beyond the end of the audio file.");
            }

            stream.Position = audioOffset;
            var audioLength = (int)Math.Min(stream.Length - audioOffset, MaximumProbeBytes);
            var audioBytes = new byte[audioLength];
            var audioRead = 0;
            while (audioRead < audioLength)
            {
                var count = stream.Read(audioBytes, audioRead, audioLength - audioRead);
                if (count <= 0)
                {
                    break;
                }

                audioRead += count;
            }

            if (audioRead != audioBytes.Length)
            {
                Array.Resize(ref audioBytes, audioRead);
            }

            var audioDescriptor = Probe(audioBytes);
            return audioDescriptor.Success
                ? audioDescriptor
                : Failure("invalid-id3-audio", "No supported audio stream was found after ID3 metadata.");
        }
        catch (Exception ex)
        {
            return Failure("probe-io-failed", ex.Message);
        }
    }

    public static AudioFileFormatDescriptor Probe(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return Failure("empty-file", "Audio file is empty.");
        }

        if (IsWave(bytes))
        {
            return ProbeWave(bytes);
        }

        if (StartsWithAscii(bytes, 0, "OggS"))
        {
            return ProbeOgg(bytes);
        }

        if (IsIsoBaseMedia(bytes))
        {
            return Failure(
                "unsupported-iso-base-media",
                "ISO base media audio containers such as M4A/MP4 are not supported.",
                AudioFileFormat.IsoBaseMedia,
                "ISO-BMFF",
                "Unknown");
        }

        if (TryFindMpegAudioFrame(bytes, out var frameOffset))
        {
            return Success(AudioFileFormat.Mp3, "MPEG Audio", "MP3", ".mp3",
                frameOffset > 0 ? "MP3 frame detected after metadata." : "MP3 frame detected.");
        }

        return Failure("unknown-container", "Audio container or codec could not be identified.");
    }

    private static AudioFileFormatDescriptor ProbeWave(byte[] bytes)
    {
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = ReadUInt32LittleEndian(bytes, offset + 4);
            var dataOffset = offset + 8;
            if (string.Equals(chunkId, "fmt ", StringComparison.Ordinal))
            {
                if (chunkSize < 16 || dataOffset + 16 > bytes.Length)
                {
                    return Failure("invalid-wav-fmt", "WAV fmt chunk is incomplete.", AudioFileFormat.Unknown, "WAV", "Unknown");
                }

                var formatTag = ReadUInt16LittleEndian(bytes, dataOffset);
                if (formatTag == 1)
                {
                    return Success(AudioFileFormat.WavPcm, "WAV", "PCM", ".wav", "PCM WAV detected.");
                }

                if (formatTag == 3)
                {
                    return Success(AudioFileFormat.WavIeeeFloat, "WAV", "IEEE Float", ".wav", "IEEE Float WAV detected.");
                }

                if (formatTag == 0xfffe && chunkSize >= 40 && dataOffset + 26 <= bytes.Length)
                {
                    var subFormatTag = ReadUInt16LittleEndian(bytes, dataOffset + 24);
                    if (subFormatTag == 1)
                    {
                        return Success(AudioFileFormat.WavPcm, "WAV", "PCM Extensible", ".wav", "WAVE_FORMAT_EXTENSIBLE PCM detected.");
                    }

                    if (subFormatTag == 3)
                    {
                        return Success(AudioFileFormat.WavIeeeFloat, "WAV", "IEEE Float Extensible", ".wav", "WAVE_FORMAT_EXTENSIBLE IEEE Float detected.");
                    }
                }

                return Failure(
                    "unsupported-wav-encoding",
                    "WAV encoding is not PCM or IEEE Float. formatTag=" + formatTag,
                    AudioFileFormat.Unknown,
                    "WAV",
                    "formatTag=" + formatTag);
            }

            var next = (long)dataOffset + chunkSize + (chunkSize % 2);
            if (next <= offset || next > bytes.Length)
            {
                break;
            }

            offset = (int)next;
        }

        return Failure("missing-wav-fmt", "WAV fmt chunk was not found in the probe window.", AudioFileFormat.Unknown, "WAV", "Unknown");
    }

    private static AudioFileFormatDescriptor ProbeOgg(byte[] bytes)
    {
        if (IndexOfAscii(bytes, "\u0001vorbis") >= 0)
        {
            return Success(AudioFileFormat.OggVorbis, "Ogg", "Vorbis", ".ogg", "Ogg Vorbis detected.");
        }

        if (IndexOfAscii(bytes, "OpusHead") >= 0)
        {
            return Failure(
                "unsupported-ogg-opus",
                "Ogg Opus is recognized but is not supported by the Unity Ogg Vorbis loader.",
                AudioFileFormat.OggOpus,
                "Ogg",
                "Opus");
        }

        return Failure("unsupported-ogg-codec", "Ogg container does not contain a supported Vorbis stream.", AudioFileFormat.Unknown, "Ogg", "Unknown");
    }

    private static bool IsWave(byte[] bytes)
    {
        return bytes.Length >= 12
               && (StartsWithAscii(bytes, 0, "RIFF") || StartsWithAscii(bytes, 0, "RF64"))
               && StartsWithAscii(bytes, 8, "WAVE");
    }

    private static bool IsIsoBaseMedia(byte[] bytes)
    {
        return bytes.Length >= 12 && StartsWithAscii(bytes, 4, "ftyp");
    }

    private static bool TryFindMpegAudioFrame(byte[] bytes, out int offset)
    {
        offset = -1;
        var start = 0;
        if (bytes.Length >= 10 && StartsWithAscii(bytes, 0, "ID3"))
        {
            var tagSize = ReadSynchsafeInteger(bytes, 6);
            if (tagSize >= 0)
            {
                start = Math.Min(bytes.Length, 10 + tagSize);
            }
        }

        for (var i = start; i + 4 <= bytes.Length; i++)
        {
            var first = bytes[i];
            var second = bytes[i + 1];
            var third = bytes[i + 2];
            if (first != 0xff || (second & 0xe0) != 0xe0)
            {
                continue;
            }

            var version = (second >> 3) & 0x03;
            var layer = (second >> 1) & 0x03;
            var bitrateIndex = (third >> 4) & 0x0f;
            var sampleRateIndex = (third >> 2) & 0x03;
            if (version == 1 || layer == 0 || bitrateIndex == 0 || bitrateIndex == 0x0f || sampleRateIndex == 0x03)
            {
                continue;
            }

            offset = i;
            return true;
        }

        return false;
    }

    private static bool TryGetId3AudioOffset(byte[] bytes, out long audioOffset)
    {
        audioOffset = 0;
        if (bytes.Length < 10 || !StartsWithAscii(bytes, 0, "ID3"))
        {
            return false;
        }

        var tagSize = ReadSynchsafeInteger(bytes, 6);
        if (tagSize < 0)
        {
            return false;
        }

        var hasFooter = (bytes[5] & 0x10) != 0;
        audioOffset = 10L + tagSize + (hasFooter ? 10L : 0L);
        return true;
    }

    private static int ReadSynchsafeInteger(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
        {
            return -1;
        }

        if ((bytes[offset] & 0x80) != 0
            || (bytes[offset + 1] & 0x80) != 0
            || (bytes[offset + 2] & 0x80) != 0
            || (bytes[offset + 3] & 0x80) != 0)
        {
            return -1;
        }

        return (bytes[offset] << 21)
               | (bytes[offset + 1] << 14)
               | (bytes[offset + 2] << 7)
               | bytes[offset + 3];
    }

    private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset)
    {
        return (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));
    }

    private static int IndexOfAscii(byte[] bytes, string value)
    {
        var pattern = Encoding.ASCII.GetBytes(value);
        for (var i = 0; i + pattern.Length <= bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (bytes[i + j] == pattern[j])
                {
                    continue;
                }

                match = false;
                break;
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool StartsWithAscii(byte[] bytes, int offset, string value)
    {
        if (offset < 0 || offset + value.Length > bytes.Length)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (bytes[offset + i] != (byte)value[i])
            {
                return false;
            }
        }

        return true;
    }

    private static AudioFileFormatDescriptor Success(
        AudioFileFormat format,
        string container,
        string codec,
        string extension,
        string message)
    {
        return new AudioFileFormatDescriptor
        {
            Success = true,
            Format = format,
            Container = container,
            Codec = codec,
            CanonicalExtension = extension,
            Message = message
        };
    }

    private static AudioFileFormatDescriptor Failure(
        string code,
        string message,
        AudioFileFormat format = AudioFileFormat.Unknown,
        string container = "Unknown",
        string codec = "Unknown")
    {
        return new AudioFileFormatDescriptor
        {
            Success = false,
            Format = format,
            Container = container,
            Codec = codec,
            FailureCode = code,
            Message = message
        };
    }
}
