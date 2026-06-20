using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace AuraToolsExp.Dll.Features.Logging;

internal sealed class AuraToolsLogFileWriter : IDisposable
{
    private readonly BlockingCollection<AuraToolsLogRecord> queue = new();
    private readonly Thread worker;
    private readonly StreamWriter writer;
    private int disposed;

    public AuraToolsLogFileWriter(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        worker = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "AuraTools.FileLog"
        };
        worker.Start();
    }

    public string FilePath { get; }

    public void Enqueue(AuraToolsLogRecord record)
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
            // Logging must never crash gameplay.
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
            // Avoid recursive failures.
        }
    }

    private void DrainQueue()
    {
        while (queue.TryTake(out var record))
        {
            WriteRecord(record);
        }
    }

    private void WriteRecord(AuraToolsLogRecord record)
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
