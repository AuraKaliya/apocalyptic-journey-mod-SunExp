using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace LogExp.Dll.Infrastructure;

internal sealed class LogFileWriter : IDisposable
{
    private readonly BlockingCollection<LogRecord> queue = new BlockingCollection<LogRecord>();
    private readonly Thread worker;
    private readonly StreamWriter writer;
    private int disposed;

    public LogFileWriter(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        worker = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "LogExp.FileWriter"
        };
        worker.Start();
    }

    public string FilePath { get; }

    public void Enqueue(LogRecord record)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        try
        {
            queue.Add(record);
        }
        catch (InvalidOperationException)
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
            // Logging must never crash the game.
        }
    }

    private void DrainQueue()
    {
        while (queue.TryTake(out var record))
        {
            WriteRecord(record);
        }
    }

    private void WriteRecord(LogRecord record)
    {
        try
        {
            writer.WriteLine(record.Format());
        }
        catch
        {
            // Avoid recursive logging from the logging subsystem itself.
        }
    }
}
