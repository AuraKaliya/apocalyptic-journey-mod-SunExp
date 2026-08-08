using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.Worker;

internal sealed class CombatFoundationReplayWarehouse
{
    private const string LegacyIndexFileName = "replay-index-v1.jsonl";

    private const string IndexFileName = "replay-index-v2.jsonl";

    private const int StorageVersion = 2;

    private const int ShardHeaderSize = 32;

    private const int ShardTrailerSize = 24;

    private const int MaximumEpisodesPerShard = 256;

    private const int MaximumRecordBytes = 256 * 1024 * 1024;

    private const int MaximumCatalogBytes = 16 * 1024 * 1024;

    private const int ShardCompressionGZipLegacy = 1;

    private const int ShardCompressionGZipCatalog = 2;

    private const int IndexChecksumVersion = 1;

    private const long StoredToResidentMultiplier = 96L;

    private static readonly byte[] ShardMagic =
        Encoding.ASCII.GetBytes("AURARP2S");

    private static readonly byte[] ShardTrailerMagic =
        Encoding.ASCII.GetBytes("AURARP2E");

    private static readonly uint[] Crc32Table = CreateCrc32Table();

    private readonly object gate = new();
    private readonly string rootPath;
    private readonly string shardRootPath;
    private readonly string legacyIndexPath;
    private readonly string indexPath;
    private readonly Dictionary<string, ReplayWarehouseEntry> entries =
        new(StringComparer.Ordinal);
    private readonly List<ReplayWarehouseEntry> legacyEntries = new();
    private readonly List<LegacyIndexRow> legacyIndexRows = new();

    private bool recoveryUncertain;
    private bool legacyIndexDirty;

    internal bool RecoveryUncertain => recoveryUncertain;

    public CombatFoundationReplayWarehouse(string path)
    {
        rootPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(rootPath) ?? "";
        if (string.Equals(
                rootPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                volumeRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Replay warehouse cannot use a filesystem root.");
        }
        if (ExistingPathContainsReparsePoint(rootPath))
        {
            throw new InvalidOperationException(
                "Replay warehouse path contains a reparse point.");
        }
        shardRootPath = Path.Combine(rootPath, "shards");
        legacyIndexPath = Path.Combine(rootPath, LegacyIndexFileName);
        indexPath = Path.Combine(rootPath, IndexFileName);
        Directory.CreateDirectory(shardRootPath);
        if (ExistingPathContainsReparsePoint(shardRootPath))
        {
            throw new InvalidOperationException(
                "Replay shard path contains a reparse point.");
        }
        LoadIndex(legacyIndexPath, legacy: true);
        RepairIndexTail();
        LoadIndex(indexPath, legacy: false);
        if (!recoveryUncertain)
        {
            RemoveInvalidShardEnvelopes();
            MigrateLegacyEntries();
            CleanupMigratedLegacyFiles();
            CompactLegacyIndexAfterMigration();
            CleanupOrphanShards();
        }
    }

    public CombatFoundationReplayArchiveReport Archive(
        int iteration,
        IReadOnlyList<CombatEpisode> episodes)
    {
        var report = new CombatFoundationReplayArchiveReport
        {
            Iteration = iteration,
            SourceEpisodes = episodes?.Count(episode => episode != null) ?? 0,
            WarehousePath = rootPath
        };
        lock (gate)
        {
            var pending = new List<PendingEpisode>();
            var pendingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var episode in episodes ?? Array.Empty<CombatEpisode>())
            {
                if (episode == null)
                {
                    continue;
                }
                var key = StableKey(episode);
                if (entries.ContainsKey(key) || !pendingKeys.Add(key))
                {
                    report.DuplicateEpisodes++;
                    continue;
                }
                pending.Add(new PendingEpisode(key, episode));
            }
            if (pending.Count == 0)
            {
                return report;
            }

            var transactionEntries = new List<ReplayWarehouseEntry>(pending.Count);
            var publishedShardPaths = new List<string>();
            var transactionCatalog = new Dictionary<int, string>();
            var indexCommitted = false;
            try
            {
                for (var start = 0; start < pending.Count;
                     start += MaximumEpisodesPerShard)
                {
                    var count = Math.Min(
                        MaximumEpisodesPerShard,
                        pending.Count - start);
                    var records = new List<SerializedEpisode>(count);
                    for (var offset = 0; offset < count; offset++)
                    {
                        var item = pending[start + offset];
                        try
                        {
                            var bytes = Encoding.UTF8.GetBytes(
                                SerializeCompact(item.Episode));
                            if (bytes.Length > MaximumRecordBytes)
                            {
                                throw new InvalidDataException(
                                    "Replay episode exceeds the shard record limit.");
                            }
                            records.Add(new SerializedEpisode(
                                item.Key,
                                item.Episode,
                                bytes,
                                ComputeCrc32(bytes)));
                        }
                        catch (Exception ex)
                        {
                            report.Error = AppendError(report.Error, ex.Message);
                        }
                    }
                    if (records.Count == 0)
                    {
                        continue;
                    }

                    var shard = WriteShard(iteration, start, records);
                    publishedShardPaths.Add(shard.FullPath);
                    MergeFeatureTokenCatalog(
                        transactionCatalog,
                        shard.FeatureTokenCatalog);
                    var storedBytesPerEpisode = Math.Max(
                        1L,
                        (shard.StoredBytes + records.Count - 1L)
                        / records.Count);
                    for (var recordIndex = 0;
                         recordIndex < records.Count;
                         recordIndex++)
                    {
                        var record = records[recordIndex];
                        transactionEntries.Add(CreateEntry(
                            iteration,
                            record.Key,
                            shard.RelativePath,
                            recordIndex,
                            record.Crc32,
                            record.Episode,
                            storedBytesPerEpisode));
                    }
                    report.ArchivedBytes += shard.StoredBytes;
                }

                if (transactionEntries.Count == 0)
                {
                    return report;
                }
                AppendIndexBatch(transactionEntries, transactionCatalog);
                indexCommitted = true;
                foreach (var entry in transactionEntries)
                {
                    entries.Add(entry.Key, entry);
                }
                report.ArchivedEpisodes = transactionEntries.Count;
            }
            catch (Exception ex)
            {
                report.Error = AppendError(report.Error, ex.Message);
                report.ArchivedBytes = 0L;
                if (indexCommitted)
                {
                    // The index append is durable. Preserve every published
                    // shard even if a subsequent in-memory update fails.
                    recoveryUncertain = true;
                }
                else
                {
                    foreach (var path in publishedShardPaths)
                    {
                        TryDelete(path);
                    }
                }
            }
        }
        return report;
    }

    public IReadOnlyList<CombatEpisode> Load(
        int iteration,
        IReadOnlyCollection<string> excludedKeys,
        int episodeLimit,
        long bytesLimit)
    {
        return Load(
            iteration,
            excludedKeys,
            episodeLimit,
            bytesLimit,
            Array.Empty<string>());
    }

    public IReadOnlyList<CombatEpisode> Load(
        int iteration,
        IReadOnlyCollection<string> excludedKeys,
        int episodeLimit,
        long bytesLimit,
        IReadOnlyCollection<string> preferredKeys)
    {
        if (episodeLimit <= 0 || bytesLimit <= 0L)
        {
            return Array.Empty<CombatEpisode>();
        }
        lock (gate)
        {
            var excluded = new HashSet<string>(
                excludedKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var preferred = new HashSet<string>(
                preferredKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var candidates = entries.Values
                .Where(entry => !excluded.Contains(entry.Key))
                .ToList();
            var selected = new List<ReplayWarehouseEntry>(episodeLimit);
            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            var hardQuota = Math.Max(1, episodeLimit / 2);
            var successQuota = Math.Max(1, episodeLimit / 4);
            var diversityQuota = Math.Max(
                0,
                episodeLimit - hardQuota - successQuota);
            AddCandidates(
                candidates.Where(entry => entry.Hard),
                hardQuota,
                iteration,
                selected,
                selectedKeys,
                preferred);
            AddCandidates(
                candidates.Where(entry => entry.Successful),
                successQuota,
                iteration,
                selected,
                selectedKeys,
                preferred);
            var diverse = candidates
                .GroupBy(
                    entry => entry.DifficultyId + "|" + entry.ScenarioId,
                    StringComparer.Ordinal)
                .SelectMany(group => group.OrderBy(entry =>
                    StableOrder(entry.Key, iteration)))
                .ToList();
            AddCandidates(
                diverse,
                diversityQuota,
                iteration,
                selected,
                selectedKeys,
                preferred);
            AddCandidates(
                candidates,
                episodeLimit - selected.Count,
                iteration,
                selected,
                selectedKeys,
                preferred);

            var accepted = new List<ReplayWarehouseEntry>(selected.Count);
            var residentBytes = 0L;
            foreach (var entry in selected)
            {
                var estimatedResidentBytes = EffectiveResidentBytes(entry);
                if (accepted.Count >= episodeLimit
                    || estimatedResidentBytes > bytesLimit - residentBytes)
                {
                    continue;
                }
                accepted.Add(entry);
                residentBytes += estimatedResidentBytes;
            }

            var loaded = new Dictionary<string, CombatEpisode>(
                StringComparer.Ordinal);
            foreach (var group in accepted
                         .Where(entry => entry.StorageVersion >= StorageVersion)
                         .GroupBy(entry => entry.RelativePath,
                             StringComparer.OrdinalIgnoreCase))
            {
                var shardEntries = group.ToList();
                var shardEpisodes = ReadShard(
                    group.Key,
                    shardEntries,
                    out var shardValid);
                if (!shardValid)
                {
                    InvalidateShard(group.Key);
                    continue;
                }
                foreach (var pair in shardEpisodes)
                {
                    loaded[pair.Key] = pair.Value;
                }
            }
            foreach (var entry in accepted.Where(entry =>
                         entry.StorageVersion < StorageVersion))
            {
                var episode = ReadLegacyEpisode(entry);
                if (episode != null)
                {
                    loaded[entry.Key] = episode;
                }
            }
            var result = new List<CombatEpisode>(accepted.Count);
            residentBytes = 0L;
            foreach (var entry in accepted)
            {
                if (!loaded.TryGetValue(entry.Key, out var episode))
                {
                    continue;
                }
                var actualResidentBytes = Math.Max(
                    EffectiveResidentBytes(entry),
                    CombatFoundationReplaySampler.EstimateResidentBytes(
                        episode));
                if (actualResidentBytes > bytesLimit - residentBytes)
                {
                    continue;
                }
                result.Add(episode);
                residentBytes += actualResidentBytes;
            }
            return result;
        }
    }

    private void LoadIndex(string path, bool legacy)
    {
        if (!File.Exists(path))
        {
            return;
        }
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                if (legacy)
                {
                    var legacyEntry =
                        JsonConvert.DeserializeObject<ReplayWarehouseEntry>(line);
                    AddLoadedEntry(
                        legacyEntry,
                        legacy: true,
                        legacyEntry?.EmbeddedFeatureTokenCatalog,
                        legacyEntry?.EmbeddedFeatureTokenCatalogPresent == true
                        || (legacyEntry?.EmbeddedFeatureTokenCatalog?.Count ?? 0)
                        > 0);
                    legacyIndexRows.Add(new LegacyIndexRow(
                        line,
                        legacyEntry));
                    continue;
                }
                if (!TryReadIndexBatch(
                        line,
                        out var batch,
                        out var unchecksummedLegacyBatch)
                    || batch == null)
                {
                    continue;
                }
                recoveryUncertain |= unchecksummedLegacyBatch;
                foreach (var entry in batch.Entries)
                {
                    AddLoadedEntry(
                        entry,
                        legacy: false,
                        batch.FeatureTokenCatalog,
                        batch.FeatureTokenCatalogPresent);
                }
            }
            catch (JsonException)
            {
                if (legacy)
                {
                    legacyIndexRows.Add(new LegacyIndexRow(line, null));
                }
                // A partial final transaction is ignored as one unit. Earlier
                // committed index batches and legacy rows remain readable.
            }
        }
    }

    private bool AddLoadedEntry(
        ReplayWarehouseEntry? entry,
        bool legacy,
        IReadOnlyDictionary<int, string>? featureTokenCatalog,
        bool catalogPresent)
    {
        if (entry == null
            || string.IsNullOrWhiteSpace(entry.Key)
            || string.IsNullOrWhiteSpace(entry.RelativePath)
            || !TryResolveEntryPath(entry.RelativePath, out var path)
            || !File.Exists(path))
        {
            return false;
        }
        if (legacy)
        {
            entry.StorageVersion = 1;
            entry.RecordIndex = -1;
            legacyEntries.Add(entry);
        }
        entry.FeatureTokenCatalog = featureTokenCatalog == null
            ? new Dictionary<int, string>()
            : new Dictionary<int, string>(featureTokenCatalog);
        entry.FeatureTokenCatalogPresent = catalogPresent;
        entries[entry.Key] = entry;
        return true;
    }

    private void AppendIndexBatch(
        IReadOnlyList<ReplayWarehouseEntry> batchEntries,
        IReadOnlyDictionary<int, string> featureTokenCatalog)
    {
        var batch = new ReplayWarehouseIndexBatch
        {
            StorageVersion = StorageVersion,
            TransactionId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
            Entries = batchEntries.ToList(),
            FeatureTokenCatalogPresent = true,
            FeatureTokenCatalog = new Dictionary<int, string>(
                featureTokenCatalog ?? new Dictionary<int, string>()),
            ChecksumVersion = IndexChecksumVersion
        };
        batch.ContentChecksumSha256 = ComputeIndexBatchChecksum(batch);
        Directory.CreateDirectory(rootPath);
        RepairIndexTail();
        using var stream = new FileStream(
            indexPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            256 * 1024,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            256 * 1024,
            leaveOpen: true);
        writer.WriteLine(JsonConvert.SerializeObject(batch, Formatting.None));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private void RepairIndexTail()
    {
        if (!File.Exists(indexPath))
        {
            return;
        }
        byte[] bytes;
        using (var input = new FileStream(
                   indexPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            if (input.Length > int.MaxValue)
            {
                throw new InvalidDataException(
                    "Replay index is too large to repair safely.");
            }
            bytes = new byte[(int)input.Length];
            ReadExactly(input, bytes);
        }
        if (bytes.Length == 0)
        {
            return;
        }

        var lineStart = 0;
        var invalidCompleteLine = false;
        var finalLineValid = false;
        var finalLineStarted = 0;
        for (var index = 0; index <= bytes.Length; index++)
        {
            var atEnd = index == bytes.Length;
            if (!atEnd && bytes[index] != (byte)'\n')
            {
                continue;
            }
            var lineEnd = index;
            if (lineEnd > lineStart && bytes[lineEnd - 1] == (byte)'\r')
            {
                lineEnd--;
            }
            var line = Encoding.UTF8.GetString(
                bytes,
                lineStart,
                lineEnd - lineStart);
            var validLine = string.IsNullOrWhiteSpace(line);
            if (!validLine)
            {
                validLine = TryReadIndexBatch(
                    line,
                    out _,
                    out var unchecksummedLegacyBatch);
                recoveryUncertain |= validLine && unchecksummedLegacyBatch;
            }
            if (atEnd)
            {
                finalLineStarted = lineStart;
                finalLineValid = validLine;
            }
            else if (!validLine)
            {
                // A crash can only leave an unterminated final append. A
                // newline-terminated invalid row may be middle-file damage;
                // retain the complete file and disable destructive cleanup.
                invalidCompleteLine = true;
            }
            lineStart = index + 1;
        }

        if (invalidCompleteLine)
        {
            recoveryUncertain = true;
            return;
        }
        var missingTerminalNewline = bytes[^1] != (byte)'\n';
        if (!missingTerminalNewline)
        {
            return;
        }
        var repairedLength = finalLineValid ? bytes.Length : finalLineStarted;
        using var output = new FileStream(
            indexPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.WriteThrough);
        output.SetLength(repairedLength);
        output.Position = repairedLength;
        if (repairedLength > 0 && bytes[repairedLength - 1] != (byte)'\n')
        {
            output.WriteByte((byte)'\n');
        }
        output.Flush(flushToDisk: true);
    }

    private static bool TryReadIndexBatch(
        string line,
        out ReplayWarehouseIndexBatch? batch,
        out bool unchecksummedLegacyBatch)
    {
        batch = null;
        unchecksummedLegacyBatch = false;
        try
        {
            batch = JsonConvert.DeserializeObject<ReplayWarehouseIndexBatch>(line);
            if (batch?.StorageVersion != StorageVersion
                || batch.Entries == null
                || batch.Entries.Count == 0
                || batch.Entries.Any(entry => entry == null
                                              || entry.StorageVersion
                                              < StorageVersion
                                              || string.IsNullOrWhiteSpace(
                                                  entry.Key)
                                              || string.IsNullOrWhiteSpace(
                                                  entry.RelativePath)
                                              || entry.RecordIndex < 0))
            {
                return false;
            }
            batch.FeatureTokenCatalog ??= new Dictionary<int, string>();
            if (!ValidFeatureTokenCatalog(batch.FeatureTokenCatalog))
            {
                return false;
            }
            if (batch.ChecksumVersion <= 0
                && string.IsNullOrWhiteSpace(batch.ContentChecksumSha256))
            {
                // Compatibility with the first Replay v2 implementation.
                batch.FeatureTokenCatalogPresent = false;
                unchecksummedLegacyBatch = true;
                return true;
            }
            return batch.ChecksumVersion == IndexChecksumVersion
                   && string.Equals(
                       batch.ContentChecksumSha256,
                       ComputeIndexBatchChecksum(batch),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private ShardWriteResult WriteShard(
        int iteration,
        int start,
        IReadOnlyList<SerializedEpisode> records)
    {
        var featureTokenCatalog = CaptureRequiredFeatureTokenCatalog(
            records.Select(record => record.Episode));
        var catalogBytes = Encoding.UTF8.GetBytes(
            SerializeFeatureTokenCatalog(featureTokenCatalog));
        if (catalogBytes.Length > MaximumCatalogBytes)
        {
            throw new InvalidDataException(
                "Replay feature token catalog exceeds the shard limit.");
        }
        var shardKeys = string.Join("\n", records.Select(record => record.Key));
        var identity = HashKey(iteration + "|" + start + "|" + shardKeys);
        var relativePath = Path.Combine(
            "shards",
            Math.Max(0, iteration).ToString("D4"),
            "replay-shard-v2-" + identity[..24] + ".arsh");
        var path = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CombatFoundationCheckpointStorage.WriteAtomicStream(
            path,
            output =>
            {
                var headerBytes = BuildShardHeader(
                    records.Count,
                    ShardCompressionGZipCatalog);
                output.Write(headerBytes, 0, headerBytes.Length);
                var payloadStart = output.Position;
                using (var gzip = new GZipStream(
                           output,
                           CompressionLevel.Fastest,
                           leaveOpen: true))
                using (var writer = new BinaryWriter(
                           gzip,
                           new UTF8Encoding(false),
                           leaveOpen: true))
                {
                    writer.Write(catalogBytes.Length);
                    writer.Write(ComputeCrc32(catalogBytes));
                    writer.Write(catalogBytes);
                    for (var recordIndex = 0;
                         recordIndex < records.Count;
                         recordIndex++)
                    {
                        var record = records[recordIndex];
                        writer.Write(recordIndex);
                        writer.Write(record.Bytes.Length);
                        writer.Write(record.Crc32);
                        writer.Write(record.Bytes);
                    }
                    writer.Flush();
                }
                var compressedLength = output.Position - payloadStart;
                using var trailer = new BinaryWriter(
                    output,
                    new UTF8Encoding(false),
                    leaveOpen: true);
                trailer.Write(ShardTrailerMagic);
                trailer.Write(compressedLength);
                trailer.Write(records.Count);
                trailer.Write(ComputeCrc32(headerBytes));
                trailer.Flush();
            },
            retainBackup: false);
        return new ShardWriteResult(
            relativePath.Replace('\\', '/'),
            path,
            new FileInfo(path).Length,
            featureTokenCatalog);
    }

    private Dictionary<string, CombatEpisode> ReadShard(
        string relativePath,
        IReadOnlyList<ReplayWarehouseEntry> requestedEntries,
        out bool valid)
    {
        valid = false;
        var result = new Dictionary<string, CombatEpisode>(StringComparer.Ordinal);
        try
        {
            if (!TryResolveEntryPath(relativePath, out var path))
            {
                return result;
            }
            using var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                256 * 1024,
                FileOptions.SequentialScan);
            if (input.Length < ShardHeaderSize + ShardTrailerSize)
            {
                return result;
            }
            var headerBytes = new byte[ShardHeaderSize];
            ReadExactly(input, headerBytes);
            using var headerStream = new MemoryStream(headerBytes, writable: false);
            using var header = new BinaryReader(headerStream, Encoding.UTF8);
            var magic = header.ReadBytes(ShardMagic.Length);
            var version = header.ReadInt32();
            var headerSize = header.ReadInt32();
            var recordCount = header.ReadInt32();
            var compression = header.ReadInt32();
            _ = header.ReadInt64();
            input.Position = input.Length - ShardTrailerSize;
            using var trailer = new BinaryReader(
                input,
                Encoding.UTF8,
                leaveOpen: true);
            var trailerMagic = trailer.ReadBytes(ShardTrailerMagic.Length);
            var compressedLength = trailer.ReadInt64();
            var trailerRecordCount = trailer.ReadInt32();
            var headerCrc32 = trailer.ReadUInt32();
            if (!magic.SequenceEqual(ShardMagic)
                || version != StorageVersion
                || headerSize != ShardHeaderSize
                || recordCount < 0
                || recordCount > MaximumEpisodesPerShard
                || compression != ShardCompressionGZipLegacy
                && compression != ShardCompressionGZipCatalog
                || !trailerMagic.SequenceEqual(ShardTrailerMagic)
                || trailerRecordCount != recordCount
                || compressedLength != input.Length
                                      - ShardHeaderSize
                                      - ShardTrailerSize
                || ComputeCrc32(headerBytes) != headerCrc32)
            {
                return result;
            }

            var requestedByIndex = requestedEntries
                .Where(entry => entry.RecordIndex >= 0
                                && entry.RecordIndex < recordCount)
                .GroupBy(entry => entry.RecordIndex)
                .ToDictionary(group => group.Key, group => group.First());
            input.Position = ShardHeaderSize;
            using var segment = new ReadOnlySegmentStream(input, compressedLength);
            using var gzip = new GZipStream(
                segment,
                CompressionMode.Decompress,
                leaveOpen: false);
            using var reader = new BinaryReader(
                gzip,
                Encoding.UTF8,
                leaveOpen: true);
            var featureTokenCatalog = ResolveShardFeatureTokenCatalog(
                reader,
                compression,
                requestedEntries);
            for (var expectedIndex = 0;
                 expectedIndex < recordCount;
                 expectedIndex++)
            {
                var recordIndex = reader.ReadInt32();
                var length = reader.ReadInt32();
                var crc32 = reader.ReadUInt32();
                if (recordIndex != expectedIndex
                    || length < 0
                    || length > MaximumRecordBytes)
                {
                    return new Dictionary<string, CombatEpisode>(
                        StringComparer.Ordinal);
                }
                var bytes = reader.ReadBytes(length);
                if (bytes.Length != length || ComputeCrc32(bytes) != crc32)
                {
                    return new Dictionary<string, CombatEpisode>(
                        StringComparer.Ordinal);
                }
                if (!requestedByIndex.TryGetValue(recordIndex, out var entry))
                {
                    continue;
                }
                if (entry.RecordCrc32 != 0u && entry.RecordCrc32 != crc32)
                {
                    return new Dictionary<string, CombatEpisode>(
                        StringComparer.Ordinal);
                }
                var episode = JsonConvert.DeserializeObject<CombatEpisode>(
                    Encoding.UTF8.GetString(bytes));
                if (episode == null)
                {
                    return new Dictionary<string, CombatEpisode>(
                        StringComparer.Ordinal);
                }
                RemapCompactFeatureTokensOrThrow(
                    episode,
                    featureTokenCatalog.Catalog,
                    featureTokenCatalog.Present);
                if (!string.Equals(
                        StableKey(episode),
                        entry.Key,
                        StringComparison.Ordinal))
                {
                    return new Dictionary<string, CombatEpisode>(
                        StringComparer.Ordinal);
                }
                result[entry.Key] = episode;
            }
            if (gzip.ReadByte() != -1)
            {
                result.Clear();
                return result;
            }
            valid = true;
        }
        catch (Exception)
        {
            result.Clear();
            valid = false;
        }
        return result;
    }

    private CombatEpisode? ReadLegacyEpisode(ReplayWarehouseEntry entry)
    {
        try
        {
            if (!TryResolveEntryPath(entry.RelativePath, out var path))
            {
                return null;
            }
            using var input = File.OpenRead(path);
            using var gzip = new GZipStream(
                input,
                CompressionMode.Decompress,
                leaveOpen: false);
            using var reader = new StreamReader(
                gzip,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            using var jsonReader = new JsonTextReader(reader);
            var episode = JsonSerializer.CreateDefault()
                .Deserialize<CombatEpisode>(jsonReader);
            if (episode == null)
            {
                return null;
            }
            RemapCompactFeatureTokensOrThrow(
                episode,
                entry.FeatureTokenCatalog,
                entry.FeatureTokenCatalogPresent);
            return string.Equals(
                StableKey(episode),
                entry.Key,
                StringComparison.Ordinal)
                ? episode
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FeatureTokenCatalogEnvelope ResolveShardFeatureTokenCatalog(
        BinaryReader reader,
        int compression,
        IReadOnlyList<ReplayWarehouseEntry> requestedEntries)
    {
        if (compression == ShardCompressionGZipCatalog)
        {
            var length = reader.ReadInt32();
            var expectedCrc32 = reader.ReadUInt32();
            if (length < 0 || length > MaximumCatalogBytes)
            {
                throw new InvalidDataException(
                    "Replay shard feature token catalog length is invalid.");
            }
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length || ComputeCrc32(bytes) != expectedCrc32)
            {
                throw new InvalidDataException(
                    "Replay shard feature token catalog is corrupt.");
            }
            return new FeatureTokenCatalogEnvelope(
                DeserializeFeatureTokenCatalog(
                    Encoding.UTF8.GetString(bytes)),
                Present: true);
        }

        var merged = new Dictionary<int, string>();
        var present = false;
        foreach (var entry in requestedEntries)
        {
            present |= entry.FeatureTokenCatalogPresent;
            MergeFeatureTokenCatalog(merged, entry.FeatureTokenCatalog);
        }
        return new FeatureTokenCatalogEnvelope(merged, present);
    }

    private static Dictionary<int, string> CaptureRequiredFeatureTokenCatalog(
        IEnumerable<CombatEpisode> episodes)
    {
        var result = new Dictionary<int, string>();
        foreach (var episode in episodes ?? Array.Empty<CombatEpisode>())
        {
            foreach (var tokenId in EnumerateCompactFeatureTokenIds(episode))
            {
                if (tokenId <= 0
                    || !CombatFeatureTokenRegistry.TryResolve(
                        tokenId,
                        out var name)
                    || string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidDataException(
                        "Replay episode contains an unresolved compact feature token.");
                }
                if (result.TryGetValue(tokenId, out var existing)
                    && !string.Equals(
                        existing,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Replay feature token catalog contains a conflicting id.");
                }
                result[tokenId] = name;
            }
        }
        return result;
    }

    private static IEnumerable<int> EnumerateCompactFeatureTokenIds(
        CombatEpisode episode)
    {
        foreach (var frame in episode?.Frames ?? new List<CombatEpisodeFrame>())
        {
            foreach (var tokenId in frame.CompactStateFeatureTokenIds
                         ?? Array.Empty<int>())
            {
                yield return tokenId;
            }
            foreach (var candidate in frame.Candidates
                         ?? new List<CombatEpisodeCandidate>())
            {
                foreach (var tokenId in candidate.CompactFeatureTokenIds
                             ?? Array.Empty<int>())
                {
                    yield return tokenId;
                }
            }
        }
    }

    private static void RemapCompactFeatureTokensOrThrow(
        CombatEpisode episode,
        IReadOnlyDictionary<int, string> featureTokenCatalog,
        bool catalogPresent)
    {
        foreach (var frame in episode?.Frames ?? new List<CombatEpisodeFrame>())
        {
            var stateTokenIds = frame.CompactStateFeatureTokenIds;
            var stateValues = frame.CompactStateFeatureValues;
            if (stateTokenIds != null && stateTokenIds.Length > 0)
            {
                frame.CompactStateFeatureTokenIds = RemapTokenIds(
                    stateTokenIds,
                    stateValues,
                    featureTokenCatalog,
                    catalogPresent);
                frame.CompactStateFeatureValues = stateValues;
            }
            foreach (var candidate in frame.Candidates
                         ?? new List<CombatEpisodeCandidate>())
            {
                var actionTokenIds = candidate.CompactFeatureTokenIds;
                var actionValues = candidate.CompactFeatureValues;
                if (actionTokenIds == null || actionTokenIds.Length == 0)
                {
                    continue;
                }
                candidate.CompactFeatureTokenIds = RemapTokenIds(
                    actionTokenIds,
                    actionValues,
                    featureTokenCatalog,
                    catalogPresent);
                candidate.CompactFeatureValues = actionValues;
            }
        }
    }

    private static int[] RemapTokenIds(
        IReadOnlyList<int> sourceTokenIds,
        IReadOnlyList<float>? values,
        IReadOnlyDictionary<int, string> featureTokenCatalog,
        bool catalogPresent)
    {
        if (values == null || values.Count != sourceTokenIds.Count)
        {
            throw new InvalidDataException(
                "Replay compact feature token/value lengths differ.");
        }
        if (!catalogPresent)
        {
            throw new InvalidDataException(
                "Replay compact features have no persisted token catalog.");
        }
        var remapped = new int[sourceTokenIds.Count];
        for (var index = 0; index < sourceTokenIds.Count; index++)
        {
            var sourceTokenId = sourceTokenIds[index];
            if (!featureTokenCatalog.TryGetValue(sourceTokenId, out var name)
                || string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException(
                    "Replay compact feature token is absent from its catalog.");
            }
            remapped[index] = CombatFeatureTokenRegistry.GetToken(name);
        }
        return remapped;
    }

    private static string SerializeFeatureTokenCatalog(
        IReadOnlyDictionary<int, string> catalog)
    {
        return JsonConvert.SerializeObject(
            (catalog ?? new Dictionary<int, string>())
            .OrderBy(pair => pair.Key)
            .Select(pair => new FeatureTokenCatalogRow
            {
                TokenId = pair.Key,
                Name = pair.Value
            }),
            Formatting.None);
    }

    private static Dictionary<int, string> DeserializeFeatureTokenCatalog(
        string json)
    {
        var rows = JsonConvert.DeserializeObject<List<FeatureTokenCatalogRow>>(
                       json)
                   ?? throw new InvalidDataException(
                       "Replay feature token catalog is missing.");
        var result = new Dictionary<int, string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row == null
                || row.TokenId <= 0
                || string.IsNullOrWhiteSpace(row.Name)
                || !result.TryAdd(row.TokenId, row.Name)
                || !names.Add(row.Name))
            {
                throw new InvalidDataException(
                    "Replay feature token catalog is invalid.");
            }
        }
        return result;
    }

    private static bool ValidFeatureTokenCatalog(
        IReadOnlyDictionary<int, string> catalog)
    {
        return catalog != null
               && catalog.All(pair => pair.Key > 0
                                      && !string.IsNullOrWhiteSpace(pair.Value))
               && catalog.Values.Distinct(StringComparer.OrdinalIgnoreCase)
                      .Count() == catalog.Count;
    }

    private static void MergeFeatureTokenCatalog(
        IDictionary<int, string> target,
        IReadOnlyDictionary<int, string> source)
    {
        foreach (var pair in source ?? new Dictionary<int, string>())
        {
            if (target.TryGetValue(pair.Key, out var existing)
                && !string.Equals(
                    existing,
                    pair.Value,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Replay feature token catalog contains conflicting names.");
            }
            if (target.Any(existingPair => existingPair.Key != pair.Key
                                           && string.Equals(
                                               existingPair.Value,
                                               pair.Value,
                                               StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "Replay feature token catalog maps one name to multiple ids.");
            }
            target[pair.Key] = pair.Value;
        }
    }

    private static string ComputeIndexBatchChecksum(
        ReplayWarehouseIndexBatch batch)
    {
        var payload = JsonConvert.SerializeObject(
            new
            {
                batch.StorageVersion,
                batch.TransactionId,
                CreatedUtcTicks = batch.CreatedUtc.Ticks,
                batch.FeatureTokenCatalogPresent,
                FeatureTokenCatalog = batch.FeatureTokenCatalog
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new FeatureTokenCatalogRow
                    {
                        TokenId = pair.Key,
                        Name = pair.Value
                    })
                    .ToArray(),
                batch.Entries
            },
            Formatting.None,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        return HashKey(payload);
    }

    private void MigrateLegacyEntries()
    {
        var candidates = legacyEntries
            .Where(entry => entries.TryGetValue(entry.Key, out var current)
                            && current.StorageVersion < StorageVersion)
            .ToList();
        for (var start = 0; start < candidates.Count;
             start += MaximumEpisodesPerShard)
        {
            var sourceEntries = candidates
                .Skip(start)
                .Take(MaximumEpisodesPerShard)
                .ToList();
            var records = new List<SerializedEpisode>(sourceEntries.Count);
            var migratedSources = new Dictionary<string, ReplayWarehouseEntry>(
                StringComparer.Ordinal);
            foreach (var sourceEntry in sourceEntries)
            {
                var episode = ReadLegacyEpisode(sourceEntry);
                if (episode == null)
                {
                    // Compact-only legacy rows without an embedded catalog are
                    // deliberately retained but never decoded with process-local ids.
                    continue;
                }
                var bytes = Encoding.UTF8.GetBytes(SerializeCompact(episode));
                if (bytes.Length > MaximumRecordBytes)
                {
                    continue;
                }
                records.Add(new SerializedEpisode(
                    sourceEntry.Key,
                    episode,
                    bytes,
                    ComputeCrc32(bytes)));
                migratedSources[sourceEntry.Key] = sourceEntry;
            }
            if (records.Count == 0)
            {
                continue;
            }

            ShardWriteResult? shard = null;
            var indexCommitted = false;
            try
            {
                var iteration = sourceEntries.Max(entry =>
                    Math.Max(0, entry.TrainingIteration));
                shard = WriteShard(iteration, start, records);
                var storedBytesPerEpisode = Math.Max(
                    1L,
                    (shard.StoredBytes + records.Count - 1L) / records.Count);
                var migratedEntries = records
                    .Select((record, recordIndex) => CreateEntry(
                        iteration,
                        record.Key,
                        shard.RelativePath,
                        recordIndex,
                        record.Crc32,
                        record.Episode,
                        storedBytesPerEpisode))
                    .ToList();
                var verification = ReadShard(
                    shard.RelativePath,
                    migratedEntries,
                    out var shardValid);
                if (!shardValid || verification.Count != migratedEntries.Count)
                {
                    TryDelete(shard.FullPath);
                    continue;
                }
                AppendIndexBatch(
                    migratedEntries,
                    shard.FeatureTokenCatalog);
                indexCommitted = true;
                foreach (var migratedEntry in migratedEntries)
                {
                    entries[migratedEntry.Key] = migratedEntry;
                    if (migratedSources.TryGetValue(
                            migratedEntry.Key,
                            out var sourceEntry))
                    {
                        legacyIndexDirty |= TryDeleteEntryPath(sourceEntry);
                    }
                }
            }
            catch
            {
                if (shard != null && !indexCommitted)
                {
                    TryDelete(shard.FullPath);
                }
                RepairIndexTail();
                recoveryUncertain = true;
                return;
            }
        }
    }

    private void CleanupMigratedLegacyFiles()
    {
        if (recoveryUncertain)
        {
            return;
        }
        foreach (var group in legacyEntries
                     .Where(legacy => entries.TryGetValue(
                                          legacy.Key,
                                          out var current)
                                      && current.StorageVersion >= StorageVersion)
                     .GroupBy(legacy => entries[legacy.Key].RelativePath,
                         StringComparer.OrdinalIgnoreCase))
        {
            var currentEntries = group
                .Select(legacy => entries[legacy.Key])
                .DistinctBy(entry => entry.Key)
                .ToList();
            var loaded = ReadShard(group.Key, currentEntries, out var valid);
            if (!valid)
            {
                continue;
            }
            foreach (var legacy in group)
            {
                if (loaded.ContainsKey(legacy.Key))
                {
                    legacyIndexDirty |= TryDeleteEntryPath(legacy);
                }
            }
        }
    }

    private bool TryDeleteEntryPath(ReplayWarehouseEntry entry)
    {
        if (TryResolveEntryPath(entry.RelativePath, out var path))
        {
            var existed = File.Exists(path);
            TryDelete(path);
            return existed && !File.Exists(path);
        }
        return false;
    }

    private void CompactLegacyIndexAfterMigration()
    {
        if (recoveryUncertain || !File.Exists(legacyIndexPath))
        {
            return;
        }
        try
        {
            var retainedLines = legacyIndexRows
                .Where(row => LegacyArtifactStillNeedsIndex(row.Entry))
                .Select(row => row.Line)
                .ToList();
            if (!legacyIndexDirty
                && retainedLines.Count == legacyIndexRows.Count)
            {
                return;
            }
            var contents = retainedLines.Count == 0
                ? ""
                : string.Join(Environment.NewLine, retainedLines)
                  + Environment.NewLine;
            CombatFoundationCheckpointStorage.WriteAtomicText(
                legacyIndexPath,
                contents,
                retainBackup: false);
            legacyIndexDirty = false;
        }
        catch
        {
            // The durable v2 commit remains authoritative. Keeping the old
            // index is safe but disables cleanup until a later retry.
            recoveryUncertain = true;
        }
    }

    private bool LegacyArtifactStillNeedsIndex(ReplayWarehouseEntry? entry)
    {
        if (entry == null
            || !entries.TryGetValue(entry.Key, out var current)
            || current.StorageVersion < StorageVersion
            || !TryResolveEntryPath(entry.RelativePath, out var legacyPath))
        {
            return true;
        }
        return File.Exists(legacyPath);
    }

    private ReplayWarehouseEntry CreateEntry(
        int iteration,
        string key,
        string relativePath,
        int recordIndex,
        uint recordCrc32,
        CombatEpisode episode,
        long storedBytes)
    {
        var campaign = episode.Campaign ?? new CombatCampaignEpisodeMetadata();
        var successful = campaign.FinalBossVictory;
        var lowHp = episode.FinalPlayerMaxHp > 0
                    && episode.FinalPlayerHp
                       <= Math.Max(1, episode.FinalPlayerMaxHp / 3);
        return new ReplayWarehouseEntry
        {
            StorageVersion = StorageVersion,
            Key = key,
            RelativePath = relativePath.Replace('\\', '/'),
            RecordIndex = recordIndex,
            RecordCrc32 = recordCrc32,
            DifficultyId = campaign.DifficultyId ?? "",
            ScenarioId = episode.ScenarioId ?? "",
            Successful = successful,
            Hard = !successful || lowHp,
            TrainingIteration = Math.Max(
                iteration,
                campaign.TrainingIteration),
            Frames = episode.Frames?.Count ?? 0,
            EstimatedResidentBytes = Math.Max(
                CombatFoundationReplaySampler.EstimateResidentBytes(episode),
                SaturatingMultiply(storedBytes, StoredToResidentMultiplier)),
            StoredBytes = Math.Max(0L, storedBytes),
            CurriculumStage = campaign.CurriculumStage ?? "",
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static void AddCandidates(
        IEnumerable<ReplayWarehouseEntry> source,
        int count,
        int iteration,
        ICollection<ReplayWarehouseEntry> selected,
        ISet<string> selectedKeys,
        ISet<string> preferredKeys)
    {
        if (count <= 0)
        {
            return;
        }
        foreach (var entry in source
                     .OrderByDescending(item => preferredKeys.Contains(item.Key))
                     .ThenByDescending(item => item.TrainingIteration)
                     .ThenBy(item => StableOrder(item.Key, iteration)))
        {
            if (!selectedKeys.Add(entry.Key))
            {
                continue;
            }
            selected.Add(entry);
            count--;
            if (count <= 0)
            {
                break;
            }
        }
    }

    internal static string StableKey(CombatEpisode episode)
    {
        return (episode.JourneyRunId ?? "")
               + "|"
               + episode.JourneyBattleIndex.ToString("D4")
               + "|"
               + episode.Seed.ToString("D20")
               + "|"
               + (episode.ScenarioId ?? "")
               + "|"
               + (episode.EpisodeId ?? "");
    }

    private static string StableOrder(string key, int iteration)
    {
        return HashKey(iteration + "|" + key);
    }

    private static string HashKey(string value)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static long EffectiveResidentBytes(ReplayWarehouseEntry entry)
    {
        return Math.Max(
            Math.Max(0L, entry.EstimatedResidentBytes),
            SaturatingMultiply(
                Math.Max(0L, entry.StoredBytes),
                StoredToResidentMultiplier));
    }

    private static long SaturatingMultiply(long value, long multiplier)
    {
        return value > long.MaxValue / multiplier
            ? long.MaxValue
            : value * multiplier;
    }

    private bool TryResolveEntryPath(string relativePath, out string path)
    {
        path = "";
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(
                rootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = rootPath.TrimEnd(
                             Path.DirectorySeparatorChar,
                             Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (ContainsReparsePoint(candidate))
            {
                return false;
            }
            path = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ContainsReparsePoint(string candidate)
    {
        if (File.Exists(candidate)
            && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }
        var current = Path.GetDirectoryName(candidate) ?? "";
        while (true)
        {
            if (Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            if (string.Equals(
                    current,
                    rootPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = parent;
        }
    }

    private static bool ExistingPathContainsReparsePoint(string path)
    {
        try
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current)
                    && (File.GetAttributes(current)
                        & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent)
                    || string.Equals(
                        parent,
                        current,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                current = parent;
            }
        }
        catch
        {
            return true;
        }
        return false;
    }

    private void RemoveInvalidShardEnvelopes()
    {
        foreach (var group in entries.Values
                     .Where(entry => entry.StorageVersion >= StorageVersion)
                     .GroupBy(
                         entry => entry.RelativePath,
                         StringComparer.OrdinalIgnoreCase)
                     .ToList())
        {
            if (!ValidateShardEnvelope(group.Key, group.ToList()))
            {
                InvalidateShard(group.Key);
            }
        }
    }

    private bool ValidateShardEnvelope(
        string relativePath,
        IReadOnlyList<ReplayWarehouseEntry> shardEntries)
    {
        try
        {
            if (!TryResolveEntryPath(relativePath, out var path)
                || !File.Exists(path))
            {
                return false;
            }
            using var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.RandomAccess);
            if (input.Length < ShardHeaderSize + ShardTrailerSize)
            {
                return false;
            }
            var headerBytes = new byte[ShardHeaderSize];
            ReadExactly(input, headerBytes);
            using var headerStream = new MemoryStream(headerBytes, writable: false);
            using var header = new BinaryReader(headerStream, Encoding.UTF8);
            var magic = header.ReadBytes(ShardMagic.Length);
            var version = header.ReadInt32();
            var headerSize = header.ReadInt32();
            var recordCount = header.ReadInt32();
            var compression = header.ReadInt32();
            _ = header.ReadInt64();
            input.Position = input.Length - ShardTrailerSize;
            using var trailer = new BinaryReader(
                input,
                Encoding.UTF8,
                leaveOpen: true);
            var trailerMagic = trailer.ReadBytes(ShardTrailerMagic.Length);
            var compressedLength = trailer.ReadInt64();
            var trailerRecordCount = trailer.ReadInt32();
            var headerCrc32 = trailer.ReadUInt32();
            return magic.SequenceEqual(ShardMagic)
                   && version == StorageVersion
                   && headerSize == ShardHeaderSize
                    && recordCount > 0
                    && recordCount <= MaximumEpisodesPerShard
                   && (compression == ShardCompressionGZipLegacy
                       || compression == ShardCompressionGZipCatalog)
                   && trailerMagic.SequenceEqual(ShardTrailerMagic)
                   && trailerRecordCount == recordCount
                   && compressedLength == input.Length
                                          - ShardHeaderSize
                                          - ShardTrailerSize
                   && ComputeCrc32(headerBytes) == headerCrc32
                   && shardEntries.All(entry => entry.RecordIndex >= 0
                                                && entry.RecordIndex
                                                < recordCount);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void InvalidateShard(string relativePath)
    {
        var invalidKeys = entries.Values
            .Where(entry => entry.StorageVersion >= StorageVersion
                            && string.Equals(
                                entry.RelativePath,
                                relativePath,
                                StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToList();
        foreach (var key in invalidKeys)
        {
            entries.Remove(key);
        }
        if (!recoveryUncertain
            && TryResolveEntryPath(relativePath, out var path))
        {
            TryDelete(path);
        }
    }

    private void CleanupOrphanShards()
    {
        if (recoveryUncertain || !Directory.Exists(shardRootPath))
        {
            return;
        }
        var retained = entries.Values
            .Where(entry => entry.StorageVersion >= StorageVersion)
            .Select(entry => entry.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var path in Directory.EnumerateFiles(
                     shardRootPath,
                     "*.arsh",
                     enumeration))
        {
            var fullPath = Path.GetFullPath(path);
            var shardPrefix = shardRootPath.TrimEnd(
                                  Path.DirectorySeparatorChar,
                                  Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(
                    shardPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var relative = Path.GetRelativePath(rootPath, fullPath)
                .Replace('\\', '/');
            if (!retained.Contains(relative))
            {
                TryDelete(fullPath);
            }
        }
    }

    private static byte[] BuildShardHeader(int recordCount, int compression)
    {
        using var stream = new MemoryStream(ShardHeaderSize);
        using var writer = new BinaryWriter(
            stream,
            new UTF8Encoding(false),
            leaveOpen: true);
        writer.Write(ShardMagic);
        writer.Write(StorageVersion);
        writer.Write(ShardHeaderSize);
        writer.Write(recordCount);
        writer.Write(compression);
        writer.Write(DateTime.UtcNow.Ticks);
        writer.Flush();
        var bytes = stream.ToArray();
        if (bytes.Length != ShardHeaderSize)
        {
            throw new InvalidDataException("Replay shard header size is invalid.");
        }
        return bytes;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }
            offset += read;
        }
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

    private static string SerializeCompact(object value)
    {
        return JsonConvert.SerializeObject(
            value,
            Formatting.None,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                FloatFormatHandling = FloatFormatHandling.DefaultValue,
                ContractResolver = WorkerCompactEpisodeContractResolver.Instance
            });
    }

    private static string AppendError(string current, string error)
    {
        return string.IsNullOrWhiteSpace(current)
            ? error
            : current + " | " + error;
    }

    private void TryDelete(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var prefix = rootPath.TrimEnd(
                             Path.DirectorySeparatorChar,
                             Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)
                && !ContainsReparsePoint(fullPath)
                && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Orphan shards are harmless and retried on the next open.
        }
    }

    private sealed class ReplayWarehouseIndexBatch
    {
        public int StorageVersion { get; set; } =
            CombatFoundationReplayWarehouse.StorageVersion;

        public string TransactionId { get; set; } = "";

        public DateTime CreatedUtc { get; set; }

        public int ChecksumVersion { get; set; }

        public string ContentChecksumSha256 { get; set; } = "";

        public bool FeatureTokenCatalogPresent { get; set; }

        public Dictionary<int, string> FeatureTokenCatalog { get; set; } = new();

        public List<ReplayWarehouseEntry> Entries { get; set; } = new();
    }

    private sealed class ReplayWarehouseEntry
    {
        public int StorageVersion { get; set; } = 1;

        public string Key { get; set; } = "";

        public string RelativePath { get; set; } = "";

        public int RecordIndex { get; set; } = -1;

        public uint RecordCrc32 { get; set; }

        public string DifficultyId { get; set; } = "";

        public string ScenarioId { get; set; } = "";

        public bool Successful { get; set; }

        public bool Hard { get; set; }

        public int TrainingIteration { get; set; }

        public int Frames { get; set; }

        public long EstimatedResidentBytes { get; set; }

        public long StoredBytes { get; set; }

        public string CurriculumStage { get; set; } = "";

        public DateTime CreatedUtc { get; set; }

        public bool EmbeddedFeatureTokenCatalogPresent { get; set; }

        public Dictionary<int, string>? EmbeddedFeatureTokenCatalog { get; set; }

        [JsonIgnore]
        public bool FeatureTokenCatalogPresent { get; set; }

        [JsonIgnore]
        public Dictionary<int, string> FeatureTokenCatalog { get; set; } = new();
    }

    private sealed class FeatureTokenCatalogRow
    {
        public int TokenId { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed record PendingEpisode(string Key, CombatEpisode Episode);

    private sealed record SerializedEpisode(
        string Key,
        CombatEpisode Episode,
        byte[] Bytes,
        uint Crc32);

    private sealed record ShardWriteResult(
        string RelativePath,
        string FullPath,
        long StoredBytes,
        Dictionary<int, string> FeatureTokenCatalog);

    private sealed record FeatureTokenCatalogEnvelope(
        Dictionary<int, string> Catalog,
        bool Present);

    private sealed record LegacyIndexRow(
        string Line,
        ReplayWarehouseEntry? Entry);

    private sealed class ReadOnlySegmentStream : Stream
    {
        private readonly Stream inner;
        private long remaining;

        public ReadOnlySegmentStream(Stream inner, long length)
        {
            this.inner = inner;
            remaining = Math.Max(0L, length);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => remaining;

        public override long Position
        {
            get => 0L;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (remaining <= 0L)
            {
                return 0;
            }
            var requested = (int)Math.Min(count, remaining);
            var read = inner.Read(buffer, offset, requested);
            remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            if (remaining <= 0L)
            {
                return 0;
            }
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = inner.Read(buffer[..requested]);
            remaining -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
