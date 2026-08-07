using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AuraCombatAi.Shared;

public static class CombatFoundationCheckpointStorage
{
    public const int SnapshotStorageVersion = 4;

    private const int MaximumFileAttempts = 8;

    public static CombatFoundationEpisodeSnapshot WriteEpisodeSnapshot(
        string basePath,
        IEnumerable<string> serializedEpisodes,
        string replayIdentity)
    {
        if (serializedEpisodes == null)
        {
            throw new ArgumentNullException(nameof(serializedEpisodes));
        }
        return WriteEpisodeSnapshotCore(
            basePath,
            replayIdentity,
            writeLine =>
            {
                foreach (var line in serializedEpisodes)
                {
                    writeLine(line ?? "");
                }
            });
    }

    public static CombatFoundationEpisodeSnapshot WriteEpisodeSnapshot<T>(
        string basePath,
        IReadOnlyList<T> episodes,
        Func<T, string> serialize,
        string replayIdentity,
        int maximumDegreeOfParallelism)
    {
        if (episodes == null)
        {
            throw new ArgumentNullException(nameof(episodes));
        }
        if (serialize == null)
        {
            throw new ArgumentNullException(nameof(serialize));
        }
        var maximumDegree = Math.Max(1, maximumDegreeOfParallelism);
        // Keep transient JSON strings bounded. Large replay snapshots are
        // throughput-insensitive here but previously doubled peak memory.
        var chunkSize = Math.Max(4, Math.Min(16, maximumDegree * 2));
        return WriteEpisodeSnapshotCore(
            basePath,
            replayIdentity,
            writeLine =>
            {
                for (var start = 0; start < episodes.Count; start += chunkSize)
                {
                    var count = Math.Min(chunkSize, episodes.Count - start);
                    var lines = new string[count];
                    Parallel.For(
                        0,
                        count,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maximumDegree
                        },
                        index =>
                        {
                            lines[index] =
                                serialize(episodes[start + index]) ?? "";
                        });
                    for (var index = 0; index < lines.Length; index++)
                    {
                        writeLine(lines[index]);
                    }
                }
            });
    }

    private static CombatFoundationEpisodeSnapshot WriteEpisodeSnapshotCore(
        string basePath,
        string replayIdentity,
        Action<Action<string>> produceLines)
    {
        var fullBasePath = CombatFoundationPathRuntime.Normalize(basePath);
        var directory = Path.GetDirectoryName(fullBasePath)
                        ?? throw new InvalidOperationException(
                            "Checkpoint episode directory is missing.");
        CombatFoundationPathRuntime.CreateDirectory(directory);
        var baseName = Path.GetFileNameWithoutExtension(fullBasePath);
        var snapshotPath = Path.Combine(
            directory,
            baseName
            + ".snapshot-"
            + DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N").Substring(0, 12)
            + ".jsonl");
        var temporaryPath = TemporaryPath(snapshotPath);
        var episodeCount = 0;
        string contentSha256;
        try
        {
            using (var stream = new FileStream(
                       CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       1024 * 1024,
                       FileOptions.SequentialScan))
            using (var hash = SHA256.Create())
            {
                var newline = Encoding.UTF8.GetBytes(Environment.NewLine);
                void WriteLine(string line)
                {
                    var bytes = Encoding.UTF8.GetBytes(line ?? "");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Write(newline, 0, newline.Length);
                    hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                    hash.TransformBlock(
                        newline,
                        0,
                        newline.Length,
                        newline,
                        0);
                    episodeCount++;
                }
                produceLines(WriteLine);
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                stream.Flush(true);
                contentSha256 = ToHex(hash.Hash ?? Array.Empty<byte>());
            }
            ExecuteWithRetry(
                () => File.Move(
                    CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                    CombatFoundationPathRuntime.ForFileSystem(snapshotPath)),
                snapshotPath);
            var length = CombatFoundationPathRuntime.FileLength(snapshotPath);
            return new CombatFoundationEpisodeSnapshot
            {
                StorageVersion = SnapshotStorageVersion,
                Path = snapshotPath,
                ContentSha256 = contentSha256,
                ReplayIdentity = replayIdentity ?? "",
                EpisodeCount = episodeCount,
                Length = length,
                CreatedUtc = DateTime.UtcNow,
                FeatureTokenCatalog = CombatFeatureTokenRegistry.CaptureCatalog()
            };
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static void WriteAtomicText(
        string path,
        string contents,
        bool retainBackup = true)
    {
        WriteAtomicStream(
            path,
            stream =>
            {
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    64 * 1024,
                    leaveOpen: true);
                writer.Write(contents ?? "");
                writer.Flush();
            },
            retainBackup);
    }

    public static void WriteAtomicStream(
        string path,
        Action<Stream> write,
        bool retainBackup = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        if (write == null)
        {
            throw new ArgumentNullException(nameof(write));
        }
        var fullPath = CombatFoundationPathRuntime.Normalize(path);
        CombatFoundationPathRuntime.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Output directory is missing."));
        var temporaryPath = TemporaryPath(fullPath);
        try
        {
            using (var stream = new FileStream(
                       CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                write(stream);
                stream.Flush(true);
            }
            ReplaceTemporaryFile(
                temporaryPath,
                fullPath,
                retainBackup);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static void WriteAtomicJsonLines(
        string path,
        IEnumerable<string> lines)
    {
        var fullPath = CombatFoundationPathRuntime.Normalize(path);
        CombatFoundationPathRuntime.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "JSONL output directory is missing."));
        var temporaryPath = TemporaryPath(fullPath);
        try
        {
            using (var stream = new FileStream(
                       CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       1024 * 1024,
                       FileOptions.SequentialScan))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(false),
                       1024 * 1024))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line ?? "");
                }
                writer.Flush();
                stream.Flush(true);
            }
            ReplaceTemporaryFile(
                temporaryPath,
                fullPath,
                retainBackup: false);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static string ReadAllTextShared(string path)
    {
        return ExecuteWithRetry(
            () =>
            {
                using var stream = new FileStream(
                    CombatFoundationPathRuntime.ForFileSystem(path),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    true,
                    64 * 1024);
                return reader.ReadToEnd();
            },
            path);
    }

    public static List<T> ReadAndValidateJsonLines<T>(
        CombatFoundationEpisodeSnapshot snapshot,
        Func<string, T?> deserialize)
        where T : class
    {
        if (snapshot == null
            || string.IsNullOrWhiteSpace(snapshot.Path)
            || !CombatFoundationPathRuntime.FileExists(snapshot.Path))
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot is missing.");
        }
        var fullPath = CombatFoundationPathRuntime.Normalize(snapshot.Path);
        var actualLength = CombatFoundationPathRuntime.FileLength(fullPath);
        if (snapshot.Length > 0 && actualLength != snapshot.Length)
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot length mismatch.");
        }
        var actualHash = ComputeFileSha256(fullPath);
        if (!string.IsNullOrWhiteSpace(snapshot.ContentSha256)
            && !string.Equals(
                actualHash,
                snapshot.ContentSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot hash mismatch.");
        }

        var result = new List<T>();
        CombatFeatureTokenRegistry.RegisterCatalog(snapshot.FeatureTokenCatalog);
        ExecuteWithRetry(
            () =>
            {
                result.Clear();
                using var stream = new FileStream(
                    CombatFoundationPathRuntime.ForFileSystem(fullPath),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    true,
                    1024 * 1024);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    var value = deserialize(line)
                                ?? throw new InvalidDataException(
                                    "Checkpoint episode snapshot contains an invalid row.");
                    result.Add(value);
                }
                return true;
            },
            fullPath);
        if (snapshot.EpisodeCount >= 0
            && result.Count != snapshot.EpisodeCount)
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot count mismatch.");
        }
        return result;
    }

    public static void CleanupArtifacts(
        string checkpointPath,
        string baseEpisodesPath,
        IEnumerable<string> retainedSnapshotPaths,
        int retainNewestSnapshots = 2)
    {
        var retained = new HashSet<string>(
            (retainedSnapshotPaths ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(CombatFoundationPathRuntime.Normalize),
            StringComparer.OrdinalIgnoreCase);
        var fullBasePath = CombatFoundationPathRuntime.Normalize(baseEpisodesPath);
        var directory = Path.GetDirectoryName(fullBasePath);
        if (string.IsNullOrWhiteSpace(directory)
            || !CombatFoundationPathRuntime.DirectoryExists(directory))
        {
            return;
        }
        var baseName = Path.GetFileNameWithoutExtension(fullBasePath);
        foreach (var path in Directory
                     .EnumerateFiles(
                         CombatFoundationPathRuntime.ForFileSystem(directory),
                         baseName + ".snapshot-*.jsonl")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(Math.Max(0, retainNewestSnapshots)))
        {
            if (!retained.Contains(CombatFoundationPathRuntime.Normalize(path)))
            {
                TryDelete(path);
            }
        }
        CleanupTemporaryFiles(directory, baseName + "*");
        CleanupTemporaryFiles(
            directory,
            Path.GetFileName(
                CombatFoundationPathRuntime.Normalize(checkpointPath)));
    }

    public static void DeleteCheckpointArtifacts(
        string checkpointPath,
        string baseEpisodesPath)
    {
        TryDelete(checkpointPath);
        TryDelete(BackupPath(CombatFoundationPathRuntime.Normalize(checkpointPath)));
        TryDelete(baseEpisodesPath);
        var fullBasePath = CombatFoundationPathRuntime.Normalize(baseEpisodesPath);
        var directory = Path.GetDirectoryName(fullBasePath);
        if (string.IsNullOrWhiteSpace(directory)
            || !CombatFoundationPathRuntime.DirectoryExists(directory))
        {
            return;
        }
        var baseName = Path.GetFileNameWithoutExtension(fullBasePath);
        foreach (var path in Directory.EnumerateFiles(
                     CombatFoundationPathRuntime.ForFileSystem(directory),
                     baseName + ".snapshot-*.jsonl"))
        {
            TryDelete(path);
        }
        CleanupTemporaryFiles(directory, baseName + "*");
    }

    public static string BackupPath(string checkpointPath)
    {
        return CombatFoundationPathRuntime.Normalize(checkpointPath) + ".bak";
    }

    private static string ComputeFileSha256(string path)
    {
        return ExecuteWithRetry(
            () =>
            {
                using var stream = new FileStream(
                    CombatFoundationPathRuntime.ForFileSystem(path),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 1024,
                    FileOptions.SequentialScan);
                using var hash = SHA256.Create();
                return ToHex(hash.ComputeHash(stream));
            },
            path);
    }

    private static void CleanupTemporaryFiles(
        string directory,
        string prefix)
    {
        foreach (var path in Directory.EnumerateFiles(
                     CombatFoundationPathRuntime.ForFileSystem(directory),
                     prefix + ".tmp-*"))
        {
            TryDelete(path);
        }
    }

    private static string TemporaryPath(string path)
    {
        return path
               + ".tmp-"
               + Process.GetCurrentProcess().Id.ToString(
                   CultureInfo.InvariantCulture)
               + "-"
               + Guid.NewGuid().ToString("N");
    }

    private static void ReplaceTemporaryFile(
        string temporaryPath,
        string fullPath,
        bool retainBackup)
    {
        ExecuteWithRetry(
            () =>
            {
                if (CombatFoundationPathRuntime.FileExists(fullPath))
                {
                    File.Replace(
                        CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                        CombatFoundationPathRuntime.ForFileSystem(fullPath),
                        retainBackup
                            ? CombatFoundationPathRuntime.ForFileSystem(
                                BackupPath(fullPath))
                            : null,
                        true);
                }
                else
                {
                    File.Move(
                        CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                        CombatFoundationPathRuntime.ForFileSystem(fullPath));
                }
            },
            fullPath);
    }

    private static void ExecuteWithRetry(Action action, string path)
    {
        ExecuteWithRetry(
            () =>
            {
                action();
                return true;
            },
            path);
    }

    private static T ExecuteWithRetry<T>(Func<T> action, string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < MaximumFileAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt + 1 >= MaximumFileAttempts)
                {
                    break;
                }
                Thread.Sleep(Math.Min(1000, 40 * (1 << attempt)));
            }
        }
        throw new IOException(
            "Checkpoint file remained unavailable after "
            + MaximumFileAttempts
            + " attempts: "
            + path,
            lastError);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path)
                && CombatFoundationPathRuntime.FileExists(path))
            {
                ExecuteWithRetry(
                    () => File.Delete(
                        CombatFoundationPathRuntime.ForFileSystem(path)),
                    path);
            }
        }
        catch
        {
            // Stale artifacts are harmless and can be retried on the next run.
        }
    }

    private static string ToHex(IEnumerable<byte> bytes)
    {
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            builder.Append(
                value.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}

internal sealed class CombatFoundationLatestWritePipeline<T> : IDisposable
    where T : class
{
    private readonly object gate = new();
    private readonly Action<T> execute;
    private T? pending;
    private bool running;
    private Task worker = Task.CompletedTask;
    private long enqueuedCount;
    private long executedCount;
    private long coalescedCount;

    public CombatFoundationLatestWritePipeline(Action<T> execute)
    {
        this.execute = execute
                       ?? throw new ArgumentNullException(nameof(execute));
    }

    public long EnqueuedCount => Interlocked.Read(ref enqueuedCount);

    public long ExecutedCount => Interlocked.Read(ref executedCount);

    public long CoalescedCount => Interlocked.Read(ref coalescedCount);

    public void Enqueue(T item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }
        Interlocked.Increment(ref enqueuedCount);
        lock (gate)
        {
            if (pending != null)
            {
                Interlocked.Increment(ref coalescedCount);
            }
            pending = item;
            if (running)
            {
                return;
            }
            running = true;
            worker = Task.Run(Process);
        }
    }

    public void Drain()
    {
        Task current;
        lock (gate)
        {
            current = worker;
        }
        current.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Drain();
    }

    private void Process()
    {
        try
        {
            while (true)
            {
                T? item;
                lock (gate)
                {
                    item = pending;
                    pending = null;
                    if (item == null)
                    {
                        running = false;
                        return;
                    }
                }
                execute(item);
                Interlocked.Increment(ref executedCount);
            }
        }
        catch
        {
            lock (gate)
            {
                running = false;
            }
            throw;
        }
    }
}
