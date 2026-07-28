using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AuraCombatAi.Shared;

public static class CombatFoundationCheckpointStorage
{
    public const int SnapshotStorageVersion = 2;

    private const int MaximumFileAttempts = 8;

    public static CombatFoundationEpisodeSnapshot WriteEpisodeSnapshot(
        string basePath,
        IEnumerable<string> serializedEpisodes,
        string replayIdentity)
    {
        var fullBasePath = Path.GetFullPath(basePath);
        var directory = Path.GetDirectoryName(fullBasePath)
                        ?? throw new InvalidOperationException(
                            "Checkpoint episode directory is missing.");
        Directory.CreateDirectory(directory);
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
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       1024 * 1024,
                       FileOptions.SequentialScan))
            using (var hash = SHA256.Create())
            {
                var newline = Encoding.UTF8.GetBytes(Environment.NewLine);
                foreach (var line in serializedEpisodes)
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
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                stream.Flush(true);
                contentSha256 = ToHex(hash.Hash ?? Array.Empty<byte>());
            }
            ExecuteWithRetry(
                () => File.Move(temporaryPath, snapshotPath),
                snapshotPath);
            var length = new FileInfo(snapshotPath).Length;
            return new CombatFoundationEpisodeSnapshot
            {
                StorageVersion = SnapshotStorageVersion,
                Path = snapshotPath,
                ContentSha256 = contentSha256,
                ReplayIdentity = replayIdentity ?? "",
                EpisodeCount = episodeCount,
                Length = length,
                CreatedUtc = DateTime.UtcNow
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
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "Output directory is missing."));
        var temporaryPath = TemporaryPath(fullPath);
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(false)))
            {
                writer.Write(contents ?? "");
                writer.Flush();
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
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                "JSONL output directory is missing."));
        var temporaryPath = TemporaryPath(fullPath);
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
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
                    path,
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
            || !File.Exists(snapshot.Path))
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot is missing.");
        }
        var fullPath = Path.GetFullPath(snapshot.Path);
        var actualLength = new FileInfo(fullPath).Length;
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
        ExecuteWithRetry(
            () =>
            {
                result.Clear();
                using var stream = new FileStream(
                    fullPath,
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
            .Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        var fullBasePath = Path.GetFullPath(baseEpisodesPath);
        var directory = Path.GetDirectoryName(fullBasePath);
        if (string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(directory))
        {
            return;
        }
        var baseName = Path.GetFileNameWithoutExtension(fullBasePath);
        foreach (var path in Directory
                     .EnumerateFiles(
                         directory,
                         baseName + ".snapshot-*.jsonl")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(Math.Max(0, retainNewestSnapshots)))
        {
            if (!retained.Contains(Path.GetFullPath(path)))
            {
                TryDelete(path);
            }
        }
        CleanupTemporaryFiles(directory, baseName + "*");
        CleanupTemporaryFiles(
            directory,
            Path.GetFileName(Path.GetFullPath(checkpointPath)));
    }

    public static void DeleteCheckpointArtifacts(
        string checkpointPath,
        string baseEpisodesPath)
    {
        TryDelete(checkpointPath);
        TryDelete(BackupPath(Path.GetFullPath(checkpointPath)));
        TryDelete(baseEpisodesPath);
        var fullBasePath = Path.GetFullPath(baseEpisodesPath);
        var directory = Path.GetDirectoryName(fullBasePath);
        if (string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(directory))
        {
            return;
        }
        var baseName = Path.GetFileNameWithoutExtension(fullBasePath);
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     baseName + ".snapshot-*.jsonl"))
        {
            TryDelete(path);
        }
        CleanupTemporaryFiles(directory, baseName + "*");
    }

    public static string BackupPath(string checkpointPath)
    {
        return Path.GetFullPath(checkpointPath) + ".bak";
    }

    private static string ComputeFileSha256(string path)
    {
        return ExecuteWithRetry(
            () =>
            {
                using var stream = new FileStream(
                    path,
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
                     directory,
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
                if (File.Exists(fullPath))
                {
                    File.Replace(
                        temporaryPath,
                        fullPath,
                        retainBackup ? BackupPath(fullPath) : null,
                        true);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
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
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                ExecuteWithRetry(() => File.Delete(path), path);
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
