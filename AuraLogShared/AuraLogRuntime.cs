using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using AuraShared.Core;
using Witch.Mod;

namespace AuraLog.Shared;

public static class AuraLogRuntime
{
    public static string Initialize(ModConfig? modConfig, string ownerModId)
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        return AuraSharedLogStore.OwnerDirectory(ownerModId);
    }

    public static string OwnerLogPath(string ownerModId, string fileName)
    {
        return AuraSharedLogStore.OwnerLogPath(ownerModId, fileName);
    }

    public static IReadOnlyList<string> Enumerate(string ownerModId = "", string searchPattern = "*.log")
    {
        return AuraSharedLogStore.Enumerate(ownerModId, searchPattern);
    }
}

public sealed class AuraLogFileWriter : IDisposable
{
    private readonly BlockingCollection<AuraLogRecord> queue = new();
    private readonly Thread worker;
    private readonly StreamWriter writer;
    private int disposed;

    public AuraLogFileWriter(string ownerModId, string fileName)
        : this(AuraLogRuntime.OwnerLogPath(ownerModId, fileName))
    {
    }

    public AuraLogFileWriter(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        worker = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "AuraLog.FileWriter"
        };
        worker.Start();
    }

    public string FilePath { get; }

    public void Enqueue(AuraLogRecord record)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        try
        {
            queue.Add(record);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        queue.CompleteAdding();
        if (!worker.Join(TimeSpan.FromSeconds(2)))
        {
            DrainQueue();
        }

        writer.Flush();
        writer.Dispose();
        queue.Dispose();
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var record in queue.GetConsumingEnumerable())
            {
                WriteRecord(record);
            }
        }
        catch
        {
        }
    }

    private void DrainQueue()
    {
        while (queue.TryTake(out var record))
        {
            WriteRecord(record);
        }
    }

    private void WriteRecord(AuraLogRecord record)
    {
        try
        {
            writer.WriteLine(record.Format());
        }
        catch
        {
        }
    }
}

public readonly struct AuraLogRecord
{
    public AuraLogRecord(DateTime timestamp, string source, string level, string? tag, string message, string? stackTrace)
    {
        Timestamp = timestamp;
        Source = source ?? "";
        Level = level ?? "";
        Tag = tag;
        Message = message ?? "";
        StackTrace = stackTrace;
    }

    private DateTime Timestamp { get; }

    private string Source { get; }

    private string Level { get; }

    private string? Tag { get; }

    private string Message { get; }

    private string? StackTrace { get; }

    public string Format()
    {
        var builder = new StringBuilder();
        builder.Append('[')
            .Append(Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append("] [")
            .Append(Source)
            .Append('/')
            .Append(Level)
            .Append("] ");

        if (!string.IsNullOrWhiteSpace(Tag))
        {
            builder.Append('[').Append(Tag).Append("] ");
        }

        builder.Append(Message);

        if (!string.IsNullOrWhiteSpace(StackTrace))
        {
            builder.AppendLine();
            builder.Append(StackTrace);
        }

        return builder.ToString();
    }
}
