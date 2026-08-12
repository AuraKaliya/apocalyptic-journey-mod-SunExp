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
        var targetAudioBytes = audio == null
            ? 0L
            : Math.Min(audio.DataBytes, (long)Math.Ceiling(frames.Count / (double)fps * audio.BytesPerSecond));
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
        var index = new List<AviIndexEntry>(frames.Count + 64);
        foreach (var frame in frames)
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            WriteMediaChunk(writer, movi, "00dc", File.ReadAllBytes(frame.FullName), 0x10, index);
        }

        if (audio != null && targetAudioBytes > 0)
        {
            using var input = File.OpenRead(audio.Path);
            input.Position = audio.DataOffset;
            var remaining = targetAudioBytes;
            var buffer = new byte[1024 * 1024];
            while (remaining > 0)
            {
                if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = input.Read(buffer, 0, requested);
                if (read <= 0)
                {
                    break;
                }

                var chunk = read == buffer.Length ? buffer : buffer.Take(read).ToArray();
                WriteMediaChunk(writer, movi, "01wb", chunk, 0, index);
                remaining -= read;
            }
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
        var targetAudioBytes = audio == null
            ? 0L
            : Math.Min(audio.DataBytes, (long)Math.Ceiling(frameCount / (double)fps * audio.BytesPerSecond));
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
        var index = new List<AviIndexEntry>(frameCount + 64);
        var writtenFrames = 0;
        foreach (var frame in ReplayFrameSpool.Read(spoolPath))
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            WriteMediaChunk(writer, movi, "00dc", frame, 0x10, index);
            writtenFrames++;
        }

        if (writtenFrames != frameCount)
        {
            throw new InvalidDataException("视频帧工作文件与帧计数不一致。");
        }

        WriteAudioChunks(writer, movi, audio, targetAudioBytes, index, isCancelled);
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

    private static void WriteAudioChunks(
        BinaryWriter writer,
        long movi,
        WavePcmInfo? audio,
        long targetAudioBytes,
        List<AviIndexEntry> index,
        Func<bool>? isCancelled)
    {
        if (audio == null || targetAudioBytes <= 0) return;
        using var input = File.OpenRead(audio.Path);
        input.Position = audio.DataOffset;
        var remaining = targetAudioBytes;
        var buffer = new byte[1024 * 1024];
        while (remaining > 0)
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = input.Read(buffer, 0, requested);
            if (read <= 0) break;
            var chunk = read == buffer.Length ? buffer : buffer.Take(read).ToArray();
            WriteMediaChunk(writer, movi, "01wb", chunk, 0, index);
            remaining -= read;
        }
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
