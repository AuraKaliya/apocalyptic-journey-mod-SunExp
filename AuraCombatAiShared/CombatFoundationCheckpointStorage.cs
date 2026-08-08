using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AuraCombatAi.Shared;

public static class CombatFoundationCheckpointStorage
{
    public const int SnapshotStorageVersion = 5;

    private const int SnapshotHeaderSize = 72;

    private const int SnapshotCompressionGZip = 1;

    private const int MaximumSnapshotRecordBytes = 256 * 1024 * 1024;

    private const int MaximumSnapshotRecords = 1_000_000;

    private static readonly byte[] SnapshotMagic =
        Encoding.ASCII.GetBytes("AURAFES5");

    private static readonly uint[] Crc32Table = CreateCrc32Table();

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
            + ".afes");
        var temporaryPath = TemporaryPath(snapshotPath);
        var episodeCount = 0;
        try
        {
            using (var stream = new FileStream(
                       CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.Read,
                       1024 * 1024,
                       FileOptions.SequentialScan))
            {
                stream.SetLength(0L);
                stream.Position = SnapshotHeaderSize;
                using (var gzip = new GZipStream(
                           stream,
                           CompressionLevel.Fastest,
                           leaveOpen: true))
                using (var writer = new BinaryWriter(
                           gzip,
                           new UTF8Encoding(false),
                           leaveOpen: true))
                {
                    void WriteLine(string line)
                    {
                        if (episodeCount >= MaximumSnapshotRecords)
                        {
                            throw new InvalidDataException(
                                "Checkpoint episode snapshot record count exceeds the limit.");
                        }
                        var normalizedLine = line ?? "";
                        var byteCount = Encoding.UTF8.GetByteCount(normalizedLine);
                        if (byteCount > MaximumSnapshotRecordBytes)
                        {
                            throw new InvalidDataException(
                                "Checkpoint episode snapshot record length exceeds the limit.");
                        }
                        var bytes = Encoding.UTF8.GetBytes(normalizedLine);
                        writer.Write(bytes.Length);
                        writer.Write(ComputeCrc32(bytes));
                        writer.Write(bytes);
                        episodeCount++;
                    }
                    produceLines(WriteLine);
                    writer.Flush();
                }

                var compressedLength = stream.Length - SnapshotHeaderSize;
                if (compressedLength < 0L)
                {
                    throw new InvalidDataException(
                        "Checkpoint episode snapshot payload is invalid.");
                }
                stream.Flush();
                stream.Position = SnapshotHeaderSize;
                byte[] payloadHash;
                using (var hash = SHA256.Create())
                {
                    payloadHash = hash.ComputeHash(stream);
                }
                stream.Position = 0L;
                using (var writer = new BinaryWriter(
                           stream,
                           new UTF8Encoding(false),
                           leaveOpen: true))
                {
                    writer.Write(SnapshotMagic);
                    writer.Write(SnapshotStorageVersion);
                    writer.Write(SnapshotHeaderSize);
                    writer.Write(episodeCount);
                    writer.Write(SnapshotCompressionGZip);
                    writer.Write(compressedLength);
                    writer.Write(0L);
                    writer.Write(payloadHash);
                    writer.Flush();
                }
                stream.SetLength(SnapshotHeaderSize + compressedLength);
                stream.Flush(true);
            }
            ExecuteWithRetry(
                () => File.Move(
                    CombatFoundationPathRuntime.ForFileSystem(temporaryPath),
                    CombatFoundationPathRuntime.ForFileSystem(snapshotPath)),
                snapshotPath);
            var length = CombatFoundationPathRuntime.FileLength(snapshotPath);
            var contentSha256 = ComputeFileSha256(snapshotPath);
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
                var compress = path.EndsWith(
                    ".gz",
                    StringComparison.OrdinalIgnoreCase);
                using var gzip = compress
                    ? new GZipStream(
                        stream,
                        CompressionLevel.Fastest,
                        leaveOpen: true)
                    : null;
                Stream payload = gzip == null ? stream : gzip;
                using var writer = new StreamWriter(
                    payload,
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

    public static void CopyAtomicFile(
        string sourcePath,
        string destinationPath,
        bool retainBackup = false)
    {
        var fullSourcePath = CombatFoundationPathRuntime.Normalize(sourcePath);
        if (!CombatFoundationPathRuntime.FileExists(fullSourcePath))
        {
            throw new FileNotFoundException(
                "Checkpoint source file is missing.",
                fullSourcePath);
        }
        WriteAtomicStream(
            destinationPath,
            output =>
            {
                using var input = new FileStream(
                    CombatFoundationPathRuntime.ForFileSystem(fullSourcePath),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 1024,
                    FileOptions.SequentialScan);
                input.CopyTo(output, 1024 * 1024);
            },
            retainBackup);
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
                var first = stream.ReadByte();
                var second = stream.ReadByte();
                stream.Position = 0L;
                var compressed = first == 0x1f && second == 0x8b;
                using var gzip = compressed
                    ? new GZipStream(
                        stream,
                        CompressionMode.Decompress,
                        leaveOpen: true)
                    : null;
                Stream payload = gzip == null ? stream : gzip;
                using var reader = new StreamReader(
                    payload,
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

        CombatFeatureTokenRegistry.RegisterCatalog(snapshot.FeatureTokenCatalog);
        if (snapshot.StorageVersion == SnapshotStorageVersion)
        {
            return ReadAndValidateBinarySnapshot(snapshot, fullPath, deserialize);
        }
        if (snapshot.StorageVersion > SnapshotStorageVersion)
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot storage version is unsupported.");
        }

        var result = new List<T>();
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

    public static void ValidateEpisodeSnapshotEnvelope(
        CombatFoundationEpisodeSnapshot snapshot)
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
        if (snapshot.Length > 0L && actualLength != snapshot.Length)
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot length mismatch.");
        }
        var fullContentHashValidated =
            !string.IsNullOrWhiteSpace(snapshot.ContentSha256);
        if (fullContentHashValidated
            && !string.Equals(
                ComputeFileSha256(fullPath),
                snapshot.ContentSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Checkpoint episode snapshot hash mismatch.");
        }
        if (snapshot.StorageVersion != SnapshotStorageVersion)
        {
            if (snapshot.StorageVersion > SnapshotStorageVersion)
            {
                throw new InvalidDataException(
                    "Checkpoint episode snapshot storage version is unsupported.");
            }
            return;
        }

        ExecuteWithRetry(
            () =>
            {
                using var stream = new FileStream(
                    CombatFoundationPathRuntime.ForFileSystem(fullPath),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 1024,
                    FileOptions.SequentialScan);
                using var reader = new BinaryReader(
                    stream,
                    Encoding.UTF8,
                    leaveOpen: true);
                var magic = reader.ReadBytes(SnapshotMagic.Length);
                var storageVersion = reader.ReadInt32();
                var headerSize = reader.ReadInt32();
                var recordCount = reader.ReadInt32();
                var compression = reader.ReadInt32();
                var compressedLength = reader.ReadInt64();
                _ = reader.ReadInt64();
                var expectedPayloadHash = reader.ReadBytes(32);
                if (!magic.SequenceEqual(SnapshotMagic)
                    || storageVersion != SnapshotStorageVersion
                    || headerSize != SnapshotHeaderSize
                    || recordCount < 0
                    || recordCount > MaximumSnapshotRecords
                    || compression != SnapshotCompressionGZip
                    || compressedLength < 0L
                    || compressedLength != stream.Length - SnapshotHeaderSize
                    || expectedPayloadHash.Length != 32
                    || snapshot.EpisodeCount >= 0
                    && snapshot.EpisodeCount != recordCount)
                {
                    throw new InvalidDataException(
                        "Checkpoint episode snapshot header is invalid.");
                }
                if (!fullContentHashValidated)
                {
                    stream.Position = SnapshotHeaderSize;
                    byte[] actualPayloadHash;
                    using (var hash = SHA256.Create())
                    {
                        actualPayloadHash = hash.ComputeHash(stream);
                    }
                    if (!FixedTimeEquals(
                            actualPayloadHash,
                            expectedPayloadHash))
                    {
                        throw new InvalidDataException(
                            "Checkpoint episode snapshot payload hash mismatch.");
                    }
                }
                return true;
            },
            fullPath);
    }

    private static List<T> ReadAndValidateBinarySnapshot<T>(
        CombatFoundationEpisodeSnapshot snapshot,
        string fullPath,
        Func<string, T?> deserialize)
        where T : class
    {
        return ExecuteWithRetry(
            () =>
            {
                using var stream = new FileStream(
                    CombatFoundationPathRuntime.ForFileSystem(fullPath),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 1024,
                    FileOptions.SequentialScan);
                using var header = new BinaryReader(
                    stream,
                    Encoding.UTF8,
                    leaveOpen: true);
                var magic = header.ReadBytes(SnapshotMagic.Length);
                var storageVersion = header.ReadInt32();
                var headerSize = header.ReadInt32();
                var recordCount = header.ReadInt32();
                var compression = header.ReadInt32();
                var compressedLength = header.ReadInt64();
                _ = header.ReadInt64();
                var expectedPayloadHash = header.ReadBytes(32);
                if (!magic.SequenceEqual(SnapshotMagic)
                    || storageVersion != SnapshotStorageVersion
                    || headerSize != SnapshotHeaderSize
                    || recordCount < 0
                    || recordCount > MaximumSnapshotRecords
                    || compression != SnapshotCompressionGZip
                    || compressedLength < 0L
                    || compressedLength != stream.Length - SnapshotHeaderSize
                    || expectedPayloadHash.Length != 32)
                {
                    throw new InvalidDataException(
                        "Checkpoint episode snapshot header is invalid.");
                }

                stream.Position = SnapshotHeaderSize;
                byte[] actualPayloadHash;
                using (var hash = SHA256.Create())
                {
                    actualPayloadHash = hash.ComputeHash(stream);
                }
                if (!FixedTimeEquals(
                        actualPayloadHash,
                        expectedPayloadHash))
                {
                    throw new InvalidDataException(
                        "Checkpoint episode snapshot payload hash mismatch.");
                }

                stream.Position = SnapshotHeaderSize;
                var result = new List<T>(recordCount);
                using (var gzip = new GZipStream(
                           stream,
                           CompressionMode.Decompress,
                           leaveOpen: true))
                using (var reader = new BinaryReader(
                           gzip,
                           Encoding.UTF8,
                           leaveOpen: true))
                {
                    for (var index = 0; index < recordCount; index++)
                    {
                        var length = reader.ReadInt32();
                        var expectedCrc32 = reader.ReadUInt32();
                        if (length < 0 || length > MaximumSnapshotRecordBytes)
                        {
                            throw new InvalidDataException(
                                "Checkpoint episode snapshot record length is invalid.");
                        }
                        var bytes = reader.ReadBytes(length);
                        if (bytes.Length != length
                            || ComputeCrc32(bytes) != expectedCrc32)
                        {
                            throw new InvalidDataException(
                                "Checkpoint episode snapshot record is truncated or corrupt.");
                        }
                        var value = deserialize(Encoding.UTF8.GetString(bytes))
                                    ?? throw new InvalidDataException(
                                        "Checkpoint episode snapshot contains an invalid row.");
                        result.Add(value);
                    }
                    if (gzip.ReadByte() != -1)
                    {
                        throw new InvalidDataException(
                            "Checkpoint episode snapshot contains trailing records.");
                    }
                }
                if (snapshot.EpisodeCount >= 0
                    && result.Count != snapshot.EpisodeCount)
                {
                    throw new InvalidDataException(
                        "Checkpoint episode snapshot count mismatch.");
                }
                return result;
            },
            fullPath);
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
        foreach (var path in EnumerateSnapshotPaths(directory, baseName)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(Math.Max(0, retainNewestSnapshots)))
        {
            if (!retained.Contains(CombatFoundationPathRuntime.Normalize(path)))
            {
                TryDelete(path);
            }
        }
        CleanupFamilyTemporaryFiles(
            directory,
            baseName + ".snapshot-",
            ".afes");
        CleanupExactTemporaryFiles(directory, Path.GetFileName(fullBasePath));
        CleanupExactTemporaryFiles(
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
        foreach (var path in EnumerateSnapshotPaths(directory, baseName))
        {
            TryDelete(path);
        }
        CleanupFamilyTemporaryFiles(
            directory,
            baseName + ".snapshot-",
            ".afes");
        CleanupExactTemporaryFiles(directory, Path.GetFileName(fullBasePath));
        CleanupExactTemporaryFiles(
            directory,
            Path.GetFileName(
                CombatFoundationPathRuntime.Normalize(checkpointPath)));
    }

    public static void CleanupImmutableFiles(
        string directory,
        string searchPattern,
        IEnumerable<string> retainedPaths)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(searchPattern)
            || !CombatFoundationPathRuntime.DirectoryExists(directory))
        {
            return;
        }
        var fullDirectory = CombatFoundationPathRuntime.Normalize(directory);
        var retained = new HashSet<string>(
            (retainedPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(CombatFoundationPathRuntime.Normalize),
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(
                     CombatFoundationPathRuntime.ForFileSystem(fullDirectory),
                     searchPattern,
                     SearchOption.TopDirectoryOnly))
        {
            var normalized = CombatFoundationPathRuntime.Normalize(path);
            if (!retained.Contains(normalized))
            {
                TryDelete(normalized);
                TryDelete(BackupPath(normalized));
            }
        }
        CleanupFamilyTemporaryFiles(
            fullDirectory,
            "foundation-checkpoint-",
            ".json.gz");
    }

    public static string BackupPath(string checkpointPath)
    {
        return CombatFoundationPathRuntime.Normalize(checkpointPath) + ".bak";
    }

    public static string ComputeFileSha256(string path)
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

    private static IEnumerable<string> EnumerateSnapshotPaths(
        string directory,
        string baseName)
    {
        var literalPrefix = baseName + ".snapshot-";
        return Directory.EnumerateFiles(
            CombatFoundationPathRuntime.ForFileSystem(directory),
            "*",
            SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).StartsWith(
                literalPrefix,
                StringComparison.OrdinalIgnoreCase));
    }

    private static uint ComputeCrc32(byte[] bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc = Crc32Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }
        return ~crc;
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }
        var difference = 0;
        for (var index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }
        return difference == 0;
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            var crc = value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0
                    ? 0u
                    : 0xedb88320u);
            }
            table[value] = crc;
        }
        return table;
    }

    private static void CleanupExactTemporaryFiles(
        string directory,
        string leafName)
    {
        var temporaryPrefix = leafName + ".tmp-";
        CleanupTemporaryFiles(
            directory,
            fileName => fileName.StartsWith(
                            temporaryPrefix,
                            StringComparison.OrdinalIgnoreCase)
                        && ValidTemporaryToken(
                            fileName,
                            temporaryPrefix.Length));
    }

    private static void CleanupFamilyTemporaryFiles(
        string directory,
        string prefix,
        string artifactSuffix)
    {
        var temporaryMarker = artifactSuffix + ".tmp-";
        CleanupTemporaryFiles(
            directory,
            fileName =>
            {
                if (!fileName.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                var markerIndex = fileName.LastIndexOf(
                    temporaryMarker,
                    StringComparison.OrdinalIgnoreCase);
                return markerIndex >= prefix.Length
                       && ValidTemporaryToken(
                           fileName,
                           markerIndex + temporaryMarker.Length);
            });
    }

    private static bool ValidTemporaryToken(
        string fileName,
        int tokenStart)
    {
        if (tokenStart < 0 || tokenStart >= fileName.Length)
        {
            return false;
        }
        for (var index = tokenStart; index < fileName.Length; index++)
        {
            var character = fileName[index];
            if (!(character >= 'a' && character <= 'z')
                && !(character >= 'A' && character <= 'Z')
                && !(character >= '0' && character <= '9')
                && character != '-')
            {
                return false;
            }
        }
        return true;
    }

    private static void CleanupTemporaryFiles(
        string directory,
        Func<string, bool> shouldDelete)
    {
        foreach (var path in Directory.EnumerateFiles(
                     CombatFoundationPathRuntime.ForFileSystem(directory),
                     "*",
                     SearchOption.TopDirectoryOnly)
                 .Where(path => shouldDelete(Path.GetFileName(path))))
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
                ex is IOException and not EndOfStreamException
                || ex is UnauthorizedAccessException)
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
