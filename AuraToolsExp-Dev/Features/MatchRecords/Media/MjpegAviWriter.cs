using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MjpegAviWriter
{
    private const long MaximumAviBytes = 1900L * 1024L * 1024L;

    internal static void Write(
        string outputPath,
        IReadOnlyList<string> jpegFrames,
        int width,
        int height,
        int framesPerSecond,
        string? wavPath,
        Func<bool>? isCancelled = null)
    {
        if (jpegFrames == null || jpegFrames.Count == 0)
        {
            throw new InvalidDataException("没有可编码的视频帧。");
        }

        var frames = jpegFrames.Select(path => new FileInfo(path)).ToList();
        if (frames.Any(item => !item.Exists))
        {
            throw new FileNotFoundException("视频帧文件不完整。");
        }

        var fps = Math.Max(1, Math.Min(120, framesPerSecond));
        var audio = WavePcmInfo.TryRead(wavPath);
        var targetAudioBytes = TargetAudioBytes(audio, frames.Count, fps);
        var estimated = frames.Sum(item => item.Length + 8L + (item.Length & 1L)) + targetAudioBytes + 1024L * 1024L;
        if (estimated > MaximumAviBytes)
        {
            throw new IOException("视频超过 AVI 1.0 的安全大小，请缩短对局或降低画质后重试。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var riff = BeginContainer(writer, "RIFF", "AVI ");
        var hdrl = BeginContainer(writer, "LIST", "hdrl");
        var maxFrameBytes = (int)Math.Min(int.MaxValue, frames.Max(item => item.Length));
        WriteMainHeader(writer, frames.Count, fps, width, height, maxFrameBytes, audio != null);
        WriteVideoStream(writer, frames.Count, fps, width, height, maxFrameBytes);
        if (audio != null)
        {
            WriteAudioStream(writer, audio, targetAudioBytes);
        }

        EndContainer(writer, hdrl);
        var movi = BeginContainer(writer, "LIST", "movi");
        var index = new List<AviIndexEntry>(frames.Count * (audio == null ? 1 : 2) + 16);
        using var audioInterleaver = audio == null || targetAudioBytes <= 0
            ? null
            : new AudioInterleaver(audio, targetAudioBytes);
        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            var frame = frames[frameIndex];
            WriteMediaChunk(writer, movi, "00dc", File.ReadAllBytes(frame.FullName), 0x10, index);
            audioInterleaver?.WriteForFrame(writer, movi, frameIndex, frames.Count, index, isCancelled);
        }

        EndContainer(writer, movi);
        WriteFourCc(writer, "idx1");
        writer.Write(index.Count * 16);
        foreach (var item in index)
        {
            WriteFourCc(writer, item.Id);
            writer.Write(item.Flags);
            writer.Write(item.Offset);
            writer.Write(item.Size);
        }

        EndContainer(writer, riff);
        if (stream.Length > MaximumAviBytes)
        {
            throw new IOException("生成的视频超过 AVI 1.0 的安全大小。");
        }
    }

    internal static void WriteFromSpool(
        string outputPath,
        string spoolPath,
        int frameCount,
        int maxFrameBytes,
        long framePayloadBytes,
        int width,
        int height,
        int framesPerSecond,
        string? wavPath,
        Func<bool>? isCancelled = null)
    {
        if (frameCount <= 0 || !File.Exists(spoolPath))
        {
            throw new InvalidDataException("没有可编码的视频帧。");
        }

        var fps = Math.Max(1, Math.Min(120, framesPerSecond));
        var audio = WavePcmInfo.TryRead(wavPath);
        var targetAudioBytes = TargetAudioBytes(audio, frameCount, fps);
        var estimated = Math.Max(0, framePayloadBytes) + frameCount * 10L + targetAudioBytes + 1024L * 1024L;
        if (estimated > MaximumAviBytes)
        {
            throw new IOException("视频超过 AVI 1.0 的安全大小，请缩短对局或降低画质后重试。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var riff = BeginContainer(writer, "RIFF", "AVI ");
        var hdrl = BeginContainer(writer, "LIST", "hdrl");
        WriteMainHeader(writer, frameCount, fps, width, height, Math.Max(1, maxFrameBytes), audio != null);
        WriteVideoStream(writer, frameCount, fps, width, height, Math.Max(1, maxFrameBytes));
        if (audio != null)
        {
            WriteAudioStream(writer, audio, targetAudioBytes);
        }

        EndContainer(writer, hdrl);
        var movi = BeginContainer(writer, "LIST", "movi");
        var index = new List<AviIndexEntry>(frameCount * (audio == null ? 1 : 2) + 16);
        using var audioInterleaver = audio == null || targetAudioBytes <= 0
            ? null
            : new AudioInterleaver(audio, targetAudioBytes);
        var writtenFrames = 0;
        foreach (var frame in ReplayFrameSpool.Read(spoolPath))
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            WriteMediaChunk(writer, movi, "00dc", frame, 0x10, index);
            audioInterleaver?.WriteForFrame(writer, movi, writtenFrames, frameCount, index, isCancelled);
            writtenFrames++;
        }

        if (writtenFrames != frameCount)
        {
            throw new InvalidDataException("视频帧工作文件与帧计数不一致。");
        }

        EndContainer(writer, movi);
        WriteFourCc(writer, "idx1");
        writer.Write(index.Count * 16);
        foreach (var item in index)
        {
            WriteFourCc(writer, item.Id);
            writer.Write(item.Flags);
            writer.Write(item.Offset);
            writer.Write(item.Size);
        }

        EndContainer(writer, riff);
        if (stream.Length > MaximumAviBytes)
        {
            throw new IOException("生成的视频超过 AVI 1.0 的安全大小。");
        }
    }

    private static long TargetAudioBytes(WavePcmInfo? audio, int frameCount, int fps)
    {
        if (audio == null || frameCount <= 0) return 0L;
        var raw = Math.Max(0L, (long)Math.Round(
            frameCount / (double)Math.Max(1, fps) * audio.BytesPerSecond,
            MidpointRounding.AwayFromZero));
        return raw - raw % Math.Max(1, audio.BlockAlign);
    }

    private static void WriteMainHeader(
        BinaryWriter writer,
        int frameCount,
        int fps,
        int width,
        int height,
        int maxFrameBytes,
        bool hasAudio)
    {
        BeginChunk(writer, "avih", 56);
        writer.Write(1000000 / fps);
        writer.Write(Math.Max(maxFrameBytes * fps, 1));
        writer.Write(0);
        writer.Write(0x10);
        writer.Write(frameCount);
        writer.Write(0);
        writer.Write(hasAudio ? 2 : 1);
        writer.Write(maxFrameBytes);
        writer.Write(width);
        writer.Write(height);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
    }

    private static void WriteVideoStream(BinaryWriter writer, int frameCount, int fps, int width, int height, int maxFrameBytes)
    {
        var strl = BeginContainer(writer, "LIST", "strl");
        BeginChunk(writer, "strh", 56);
        WriteFourCc(writer, "vids");
        WriteFourCc(writer, "MJPG");
        writer.Write(0);
        writer.Write((short)0); writer.Write((short)0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(fps);
        writer.Write(0);
        writer.Write(frameCount);
        writer.Write(maxFrameBytes);
        writer.Write(-1);
        writer.Write(0);
        writer.Write((short)0); writer.Write((short)0); writer.Write((short)width); writer.Write((short)height);

        BeginChunk(writer, "strf", 40);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        WriteFourCc(writer, "MJPG");
        writer.Write(Math.Max(1, width * height * 3));
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        EndContainer(writer, strl);
    }

    private static void WriteAudioStream(BinaryWriter writer, WavePcmInfo audio, long targetBytes)
    {
        var strl = BeginContainer(writer, "LIST", "strl");
        BeginChunk(writer, "strh", 56);
        WriteFourCc(writer, "auds");
        writer.Write(0);
        writer.Write(0);
        writer.Write((short)0); writer.Write((short)0);
        writer.Write(0);
        writer.Write(audio.BlockAlign);
        writer.Write(audio.BytesPerSecond);
        writer.Write(0);
        writer.Write((int)(targetBytes / Math.Max(1, audio.BlockAlign)));
        writer.Write(Math.Max(audio.BlockAlign * 4096, 4096));
        writer.Write(-1);
        writer.Write(audio.BlockAlign);
        writer.Write((short)0); writer.Write((short)0); writer.Write((short)0); writer.Write((short)0);

        BeginChunk(writer, "strf", 16);
        writer.Write((short)1);
        writer.Write((short)audio.Channels);
        writer.Write(audio.SampleRate);
        writer.Write(audio.BytesPerSecond);
        writer.Write((short)audio.BlockAlign);
        writer.Write((short)audio.BitsPerSample);
        EndContainer(writer, strl);
    }

    private static void WriteMediaChunk(
        BinaryWriter writer,
        long moviStart,
        string id,
        byte[] payload,
        int flags,
        List<AviIndexEntry> index)
    {
        var chunkStart = writer.BaseStream.Position;
        WriteFourCc(writer, id);
        writer.Write(payload.Length);
        writer.Write(payload);
        if ((payload.Length & 1) != 0) writer.Write((byte)0);
        index.Add(new AviIndexEntry
        {
            Id = id,
            Flags = flags,
            Offset = checked((int)(chunkStart - (moviStart + 8))),
            Size = payload.Length
        });
    }

    private static long BeginContainer(BinaryWriter writer, string id, string type)
    {
        var start = writer.BaseStream.Position;
        WriteFourCc(writer, id);
        writer.Write(0);
        WriteFourCc(writer, type);
        return start;
    }

    private static void EndContainer(BinaryWriter writer, long start)
    {
        var end = writer.BaseStream.Position;
        writer.BaseStream.Position = start + 4;
        writer.Write(checked((int)(end - start - 8)));
        writer.BaseStream.Position = end;
        if (((end - start) & 1L) != 0) writer.Write((byte)0);
    }

    private static void BeginChunk(BinaryWriter writer, string id, int size)
    {
        WriteFourCc(writer, id);
        writer.Write(size);
    }

    private static void WriteFourCc(BinaryWriter writer, string value)
    {
        if (value == null || value.Length != 4) throw new ArgumentException("FOURCC must contain four characters.", nameof(value));
        writer.Write(Encoding.ASCII.GetBytes(value));
    }

    private sealed class AudioInterleaver : IDisposable
    {
        private readonly WavePcmInfo audio;
        private readonly long targetBytes;
        private readonly FileStream input;
        private long writtenBytes;

        internal AudioInterleaver(WavePcmInfo audio, long targetBytes)
        {
            this.audio = audio;
            this.targetBytes = Math.Max(0L, targetBytes);
            input = File.OpenRead(audio.Path);
            input.Position = audio.DataOffset;
        }

        internal void WriteForFrame(
            BinaryWriter writer,
            long movi,
            int frameIndex,
            int frameCount,
            List<AviIndexEntry> index,
            Func<bool>? isCancelled)
        {
            if (targetBytes <= 0 || frameCount <= 0) return;
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            var cumulative = frameIndex + 1 >= frameCount
                ? targetBytes
                : (long)Math.Round(
                    (frameIndex + 1d) / frameCount * targetBytes,
                    MidpointRounding.AwayFromZero);
            cumulative -= cumulative % Math.Max(1, audio.BlockAlign);
            var requested = checked((int)Math.Max(0L, cumulative - writtenBytes));
            if (requested <= 0) return;

            var payload = new byte[requested];
            var offset = 0;
            var availableEnd = audio.DataOffset + audio.DataBytes;
            while (offset < requested && input.Position < availableEnd)
            {
                var available = (int)Math.Min(requested - offset, availableEnd - input.Position);
                var read = input.Read(payload, offset, available);
                if (read <= 0) break;
                offset += read;
            }

            // The WAV is normally normalized before encoding. Zero padding here
            // keeps the container clock exact if a partial capture reaches us.
            WriteMediaChunk(writer, movi, "01wb", payload, 0, index);
            writtenBytes += requested;
        }

        public void Dispose()
        {
            input.Dispose();
        }
    }

    private sealed class AviIndexEntry
    {
        public string Id { get; set; } = "";
        public int Flags { get; set; }
        public int Offset { get; set; }
        public int Size { get; set; }
    }

    private sealed class WavePcmInfo
    {
        public string Path { get; set; } = "";
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public int BlockAlign { get; set; }
        public int BytesPerSecond { get; set; }
        public long DataOffset { get; set; }
        public long DataBytes { get; set; }

        internal static WavePcmInfo? TryRead(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (ReadFourCc(reader) != "RIFF" || reader.ReadInt32() < 4 || ReadFourCc(reader) != "WAVE") return null;
            var result = new WavePcmInfo { Path = path! };
            var pcm = false;
            while (stream.Position + 8 <= stream.Length)
            {
                var id = ReadFourCc(reader);
                var size = reader.ReadInt32();
                if (size < 0 || stream.Position + size > stream.Length) return null;
                if (id == "fmt " && size >= 16)
                {
                    pcm = reader.ReadInt16() == 1;
                    result.Channels = reader.ReadInt16();
                    result.SampleRate = reader.ReadInt32();
                    result.BytesPerSecond = reader.ReadInt32();
                    result.BlockAlign = reader.ReadInt16();
                    result.BitsPerSample = reader.ReadInt16();
                    stream.Position += size - 16;
                }
                else if (id == "data")
                {
                    result.DataOffset = stream.Position;
                    result.DataBytes = size;
                    break;
                }
                else
                {
                    stream.Position += size;
                }

                if ((size & 1) != 0) stream.Position++;
            }

            return pcm && result.DataBytes > 0 && result.Channels > 0 && result.BlockAlign > 0 ? result : null;
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(4));
        }
    }
}
