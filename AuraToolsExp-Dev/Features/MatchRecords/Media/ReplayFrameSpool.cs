using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal sealed class ReplayFrameSpool : IDisposable
{
    private readonly BlockingCollection<byte[]> pending = new(4);
    private readonly Task writerTask;
    private Exception? failure;

    internal ReplayFrameSpool(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");
        writerTask = Task.Run(WriteLoop);
    }

    internal string Path { get; }
    internal int FrameCount { get; private set; }
    internal int MaximumFrameBytes { get; private set; }
    internal long PayloadBytes { get; private set; }

    internal void Enqueue(byte[] jpeg)
    {
        if (jpeg == null || jpeg.Length == 0) throw new InvalidDataException("视频帧为空。");
        ThrowIfFailed();
        while (!pending.TryAdd(jpeg, 100))
        {
            ThrowIfFailed();
            if (pending.IsAddingCompleted) throw new InvalidOperationException("视频帧工作队列已关闭。");
        }
        FrameCount++;
        MaximumFrameBytes = Math.Max(MaximumFrameBytes, jpeg.Length);
        PayloadBytes += jpeg.Length;
    }

    internal void Complete()
    {
        if (!pending.IsAddingCompleted) pending.CompleteAdding();
        writerTask.GetAwaiter().GetResult();
        ThrowIfFailed();
    }

    public void Dispose()
    {
        try { Complete(); } catch { }
        pending.Dispose();
    }

    internal static IEnumerable<byte[]> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        while (stream.Position < stream.Length)
        {
            var length = reader.ReadInt32();
            if (length <= 0 || length > 64 * 1024 * 1024 || stream.Position + length > stream.Length)
            {
                throw new InvalidDataException("视频帧工作文件已损坏。");
            }

            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("视频帧工作文件不完整。");
            yield return bytes;
        }
    }

    private void WriteLoop()
    {
        try
        {
            using var stream = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024);
            using var writer = new BinaryWriter(stream);
            foreach (var bytes in pending.GetConsumingEnumerable())
            {
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    }

    private void ThrowIfFailed()
    {
        if (failure != null) throw new IOException("视频帧后台写入失败。", failure);
    }
}
