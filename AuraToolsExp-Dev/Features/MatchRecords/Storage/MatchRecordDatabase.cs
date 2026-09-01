using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    internal const int DefaultPageSize = 30;
    private const int MaximumPageSize = 100;
    private readonly object gate = new();
    private readonly string databasePath;
    private bool initialized;

    internal MatchRecordDatabase(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
    }

    internal string DatabasePath => databasePath;

    internal void Initialize()
    {
        lock (gate)
        {
            EnsureInitialized();
        }
    }

    internal MatchRecordPage LoadPage(string collection, long beforeSequence = 0, int pageSize = DefaultPageSize)
    {
        lock (gate)
        {
            EnsureInitialized();
            var items = new List<MatchRecord>();
            var normalizedCollection = NormalizeCollection(collection);
            var normalizedPageSize = Math.Max(1, Math.Min(MaximumPageSize, pageSize <= 0 ? DefaultPageSize : pageSize));
            using var connection = Open();
            using (var query = connection.Prepare(
                       SelectColumns
                       + " WHERE collection_kind = ? AND replay_state <> ? AND (? <= 0 OR sequence < ?) "
                       + "ORDER BY sequence DESC LIMIT ?;"))
            {
                query.Bind(1, normalizedCollection);
                query.Bind(2, MatchReplayStates.Corrupt);
                query.Bind(3, beforeSequence);
                query.Bind(4, beforeSequence);
                query.Bind(5, normalizedPageSize + 1);
                while (query.Read())
                {
                    items.Add(ReadRecord(query));
                }
            }

            var hasMore = items.Count > normalizedPageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            var nextCursor = items.Count == 0 ? 0 : items[items.Count - 1].Sequence;
            return new MatchRecordPage(items, nextCursor, hasMore, Count(connection, normalizedCollection));
        }
    }

    internal MatchRecord? Get(string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return null;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(SelectColumns + " WHERE record_id = ? LIMIT 1;");
            query.Bind(1, recordId.Trim());
            return query.Read() ? ReadRecord(query) : null;
        }
    }

    internal IReadOnlyList<MatchRecord> SearchRecords(
        string collection,
        string queryText,
        string resultFilter,
        DateTime? endedSinceUtc,
        int maximum = int.MaxValue)
    {
        lock (gate)
        {
            EnsureInitialized();
            var result = new List<MatchRecord>();
            var query = (queryText ?? "").Trim();
            using var connection = Open();
            using var statement = connection.Prepare(
                SelectColumns + " WHERE collection_kind = ? AND replay_state <> ? ORDER BY sequence DESC;");
            statement.Bind(1, NormalizeCollection(collection));
            statement.Bind(2, MatchReplayStates.Corrupt);
            while (statement.Read() && result.Count < Math.Max(1, maximum))
            {
                var record = ReadRecord(statement);
                if (!string.IsNullOrWhiteSpace(resultFilter) && !Contains(record.Result, resultFilter)) continue;
                if (endedSinceUtc.HasValue
                    && DateTime.TryParse(record.EndedUtc, out var ended)
                    && ended.ToUniversalTime() < endedSinceUtc.Value) continue;
                if (query.Length > 0
                    && !Contains(record.LevelId, query)
                    && !Contains(record.BattleTitle, query)
                    && !Contains(record.AdventureId, query)
                    && !Contains(record.Result, query)
                    && !Contains(record.Tags, query)
                    && !Contains(record.Notes, query)
                    && !Contains(record.StatisticsJson, query)) continue;
                result.Add(record);
            }

            return result;
        }
    }

    internal bool UpdateMetadata(string recordId, string tags, string notes)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return false;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            MatchRecord? record;
            using (var query = connection.Prepare(SelectColumns + " WHERE record_id = ? LIMIT 1;"))
            {
                query.Bind(1, recordId.Trim());
                record = query.Read() ? ReadRecord(query) : null;
            }

            if (record == null) return false;
            record.Tags = (tags ?? "").Trim();
            record.Notes = (notes ?? "").Trim();
            using var update = connection.Prepare("UPDATE battle_records SET metadata_payload = ? WHERE record_id = ?;");
            update.Bind(1, MatchReplayPayload.Encode(CreateMetadata(record)));
            update.Bind(2, recordId.Trim());
            update.Execute();
            return true;
        }
    }

    internal void SaveAnalysis(MatchAnalysisReport report)
    {
        if (report == null || string.IsNullOrWhiteSpace(report.RecordId))
        {
            throw new ArgumentException("Analysis report must identify its match record.", nameof(report));
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            if (!Exists(connection, report.RecordId.Trim()))
            {
                throw new InvalidDataException("Cannot save analysis for a missing match record.");
            }
            SaveAnalysis(connection, report);
        }
    }

    internal MatchAnalysisReport? GetAnalysis(string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return null;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                "SELECT payload, sha256 FROM match_analysis WHERE record_id = ? LIMIT 1;");
            query.Bind(1, recordId.Trim());
            if (!query.Read())
            {
                return null;
            }

            return ReadAnalysis(query);
        }
    }

    internal void SaveMedia(MatchMediaAsset asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.MediaId) || string.IsNullOrWhiteSpace(asset.RecordId))
        {
            throw new ArgumentException("Media asset must identify both itself and its match record.", nameof(asset));
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            if (!Exists(connection, asset.RecordId.Trim()))
            {
                throw new InvalidDataException("Cannot save media for a missing match record.");
            }
            using var insert = connection.Prepare(
                "INSERT OR REPLACE INTO replay_media(media_id, record_id, media_kind, media_format, file_path, created_utc, media_state, "
                + "duration_ms, width, height, frames_per_second, file_bytes, sha256, timeline_payload, error_text) "
                + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);");
            insert.Bind(1, asset.MediaId.Trim());
            insert.Bind(2, asset.RecordId.Trim());
            insert.Bind(3, asset.Kind ?? "Video");
            insert.Bind(4, asset.Format ?? "");
            insert.Bind(5, asset.FilePath ?? "");
            insert.Bind(6, asset.CreatedUtc ?? "");
            insert.Bind(7, asset.State ?? MatchMediaStates.Ready);
            insert.Bind(8, Math.Max(0, asset.DurationMilliseconds));
            insert.Bind(9, Math.Max(0, asset.Width));
            insert.Bind(10, Math.Max(0, asset.Height));
            insert.Bind(11, Math.Max(0d, asset.FramesPerSecond));
            insert.Bind(12, Math.Max(0, asset.FileBytes));
            insert.Bind(13, asset.Sha256 ?? "");
            insert.Bind(14, MatchReplayPayload.Encode(asset.TimelineJson ?? ""));
            insert.Bind(15, asset.Error ?? "");
            insert.Execute();
        }
    }

    internal IReadOnlyList<MatchMediaAsset> LoadMedia(string recordId)
    {
        var result = new List<MatchMediaAsset>();
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return result;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                "SELECT media_id, record_id, media_kind, media_format, file_path, created_utc, media_state, duration_ms, width, height, "
                + "frames_per_second, file_bytes, sha256, timeline_payload, error_text FROM replay_media "
                + "WHERE record_id = ? ORDER BY created_utc DESC;");
            query.Bind(1, recordId.Trim());
            while (query.Read())
            {
                result.Add(new MatchMediaAsset
                {
                    MediaId = query.Text(0),
                    RecordId = query.Text(1),
                    Kind = query.Text(2),
                    Format = query.Text(3),
                    FilePath = query.Text(4),
                    CreatedUtc = query.Text(5),
                    State = query.Text(6),
                    DurationMilliseconds = query.Int64(7),
                    Width = (int)query.Int64(8),
                    Height = (int)query.Int64(9),
                    FramesPerSecond = query.Double(10),
                    FileBytes = query.Int64(11),
                    Sha256 = query.Text(12),
                    TimelineJson = MatchReplayPayload.Decode<string>(query.Blob(13)) ?? "",
                    Error = query.Text(14)
                });
            }
        }

        return result;
    }

    internal MatchMediaAsset? DeleteMedia(string mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
        {
            return null;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            MatchMediaAsset? asset = null;
            using (var query = connection.Prepare(
                       "SELECT media_id, record_id, media_kind, media_format, file_path, created_utc, media_state, duration_ms, width, height, "
                       + "frames_per_second, file_bytes, sha256, timeline_payload, error_text FROM replay_media WHERE media_id = ? LIMIT 1;"))
            {
                query.Bind(1, mediaId.Trim());
                if (query.Read())
                {
                    asset = new MatchMediaAsset
                    {
                        MediaId = query.Text(0), RecordId = query.Text(1), Kind = query.Text(2), Format = query.Text(3),
                        FilePath = query.Text(4), CreatedUtc = query.Text(5), State = query.Text(6), DurationMilliseconds = query.Int64(7),
                        Width = (int)query.Int64(8), Height = (int)query.Int64(9), FramesPerSecond = query.Double(10), FileBytes = query.Int64(11),
                        Sha256 = query.Text(12), TimelineJson = MatchReplayPayload.Decode<string>(query.Blob(13)) ?? "", Error = query.Text(14)
                    };
                }
            }

            using var delete = connection.Prepare("DELETE FROM replay_media WHERE media_id = ?;");
            delete.Bind(1, mediaId.Trim());
            delete.Execute();
            if (TableExistsForMediaJob(connection, "replay_export_jobs"))
            {
                using var updateJob = connection.Prepare(
                    "UPDATE replay_export_jobs SET state='Cancelled', revision=revision+1, updated_utc=?, "
                    + "message='media deleted by user' WHERE job_id=? AND state='Ready';");
                updateJob.Bind(1, DateTime.UtcNow.ToString("O"));
                updateJob.Bind(2, mediaId.Trim());
                updateJob.Execute();
            }
            return asset;
        }
    }

    internal bool SetCollection(string recordId, string collection)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return false;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var update = connection.Prepare("UPDATE battle_records SET collection_kind = ? WHERE record_id = ?;");
            update.Bind(1, NormalizeCollection(collection));
            update.Bind(2, recordId.Trim());
            update.Execute();
            return connection.Changes > 0;
        }
    }

    internal bool Delete(string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return false;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                DeleteRecord(connection, recordId.Trim());
                var changed = connection.Changes > 0;
                connection.Execute("COMMIT;");
                if (changed)
                {
                    DeleteMediaDirectory(recordId.Trim());
                    SweepUnreferencedReplayAssets();
                }
                return changed;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int Clear(string collection)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var ids = SelectIds(connection, "collection_kind = ?", NormalizeCollection(collection));
                foreach (var id in ids)
                {
                    DeleteRecord(connection, id);
                }

                connection.Execute("COMMIT;");
                foreach (var id in ids)
                {
                    DeleteMediaDirectory(id);
                }
                SweepUnreferencedReplayAssets();
                return ids.Count;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int EnforceAutoLimit(int limit)
    {
        var normalizedLimit = Math.Max(1, limit);
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var stale = new List<string>();
                using (var query = connection.Prepare(
                           "SELECT record_id FROM battle_records WHERE collection_kind = ? AND replay_state <> ? "
                           + "ORDER BY sequence DESC LIMIT -1 OFFSET ?;"))
                {
                    query.Bind(1, MatchRecordCollections.Auto);
                    query.Bind(2, MatchReplayStates.Corrupt);
                    query.Bind(3, normalizedLimit);
                    while (query.Read())
                    {
                        stale.Add(query.Text(0));
                    }
                }

                foreach (var id in stale)
                {
                    DeleteRecord(connection, id);
                }

                connection.Execute("COMMIT;");
                foreach (var id in stale)
                {
                    DeleteMediaDirectory(id);
                }
                SweepUnreferencedReplayAssets();
                return stale.Count;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int Count(string collection)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            return Count(connection, NormalizeCollection(collection));
        }
    }

    internal bool ContainsContentHash(string contentSha256)
    {
        if (string.IsNullOrWhiteSpace(contentSha256)) return false;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare("SELECT metadata_payload FROM battle_records;");
            while (query.Read())
            {
                if (string.Equals(DecodeMetadata(query.Blob(0)).ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        MatchRecordsDatabaseMigrator.BackupBeforeUpgrade(databasePath);
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode=DELETE;");
        connection.Execute("PRAGMA synchronous=NORMAL;");
        connection.Execute("CREATE TABLE IF NOT EXISTS battle_records("
                           + "sequence INTEGER PRIMARY KEY AUTOINCREMENT, record_id TEXT UNIQUE NOT NULL, adventure_id TEXT NOT NULL, "
                           + "session_id TEXT NOT NULL, level_id TEXT NOT NULL, result TEXT NOT NULL, started_utc TEXT NOT NULL, ended_utc TEXT NOT NULL, "
                           + "collection_kind TEXT NOT NULL, replay_state TEXT NOT NULL, replay_protocol INTEGER NOT NULL, game_build TEXT NOT NULL, "
                           + "tool_build TEXT NOT NULL, mod_fingerprint TEXT NOT NULL, event_count INTEGER NOT NULL, turn_count INTEGER NOT NULL, "
                           + "compressed_bytes INTEGER NOT NULL, statistics_payload BLOB NOT NULL, initial_payload BLOB NOT NULL, "
                           + "metadata_payload BLOB NOT NULL DEFAULT X'');");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_battle_records_collection ON battle_records(collection_kind, sequence DESC);");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_battle_records_adventure ON battle_records(adventure_id, sequence DESC);");
        connection.Execute("CREATE TABLE IF NOT EXISTS match_analysis("
                           + "record_id TEXT PRIMARY KEY, analysis_protocol INTEGER NOT NULL, generated_utc TEXT NOT NULL, "
                           + "payload BLOB NOT NULL, sha256 TEXT NOT NULL);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_media("
                           + "media_id TEXT PRIMARY KEY, record_id TEXT NOT NULL, media_kind TEXT NOT NULL, media_format TEXT NOT NULL, "
                           + "file_path TEXT NOT NULL, created_utc TEXT NOT NULL, media_state TEXT NOT NULL, duration_ms INTEGER NOT NULL, "
                           + "width INTEGER NOT NULL, height INTEGER NOT NULL, frames_per_second REAL NOT NULL, file_bytes INTEGER NOT NULL, "
                           + "sha256 TEXT NOT NULL, timeline_payload BLOB NOT NULL, error_text TEXT NOT NULL);");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_replay_media_record ON replay_media(record_id, created_utc DESC);");
        ApplyPreV17Cutover(connection);
        connection.Execute("DROP TABLE IF EXISTS replay_timeline_chunks;");
        connection.Execute("DROP TABLE IF EXISTS replay_chunks;");
        EnsureV17Tables(connection);
        MatchRecordsDatabaseMigrator.Apply(connection);
        MatchRecordsDatabaseMigrator.Validate(connection);
        NormalizeMediaPaths(connection);
        initialized = true;
    }

    private WinSqliteConnection Open()
    {
        var connection = new WinSqliteConnection(databasePath);
        connection.Execute("PRAGMA foreign_keys=ON;");
        return connection;
    }

    private static MatchRecord ReadRecord(WinSqliteConnection.WinSqliteStatement query)
    {
        var collection = query.Text(8);
        var metadata = DecodeMetadata(query.Blob(19));
        return new MatchRecord
        {
            Sequence = query.Int64(0),
            RecordId = query.Text(1),
            AdventureId = query.Text(2),
            SessionId = query.Text(3),
            LevelId = query.Text(4),
            BattleTitle = metadata.BattleTitle,
            Result = query.Text(5),
            StartedUtc = query.Text(6),
            EndedUtc = query.Text(7),
            Collection = collection,
            IsFavorite = string.Equals(collection, MatchRecordCollections.Favorite, StringComparison.OrdinalIgnoreCase),
            Origin = metadata.Origin,
            Tags = metadata.Tags,
            Notes = metadata.Notes,
            ReplayState = query.Text(9),
            ReplayProtocol = (int)query.Int64(10),
            GameBuild = query.Text(11),
            ToolBuild = query.Text(12),
            ModFingerprint = query.Text(13),
            EventCount = (int)query.Int64(14),
            TurnCount = (int)query.Int64(15),
            CompressedBytes = query.Int64(16),
            StatisticsJson = MatchReplayPayload.Decode<string>(query.Blob(17)) ?? "",
            InitialState = MatchReplayPayload.Decode<MatchReplayInitialState>(query.Blob(18)) ?? new MatchReplayInitialState(),
            RequiredCapabilities = metadata.RequiredCapabilities,
            OptionalCapabilities = metadata.OptionalCapabilities,
            ContentDependencies = metadata.ContentDependencies,
            ContentSha256 = metadata.ContentSha256,
            CaptureDiagnostics = metadata.CaptureDiagnostics ?? new List<string>()
        };
    }

    private static int Count(WinSqliteConnection connection, string collection)
    {
        using var query = connection.Prepare(
            "SELECT COUNT(*) FROM battle_records WHERE collection_kind = ? AND replay_state <> ?;");
        query.Bind(1, collection);
        query.Bind(2, MatchReplayStates.Corrupt);
        return query.Read() ? (int)Math.Min(int.MaxValue, query.Int64(0)) : 0;
    }

    private static bool Exists(WinSqliteConnection connection, string recordId)
    {
        using var query = connection.Prepare("SELECT 1 FROM battle_records WHERE record_id = ? LIMIT 1;");
        query.Bind(1, recordId);
        return query.Read();
    }

    private static string NormalizeReplayState(string? state)
    {
        return string.Equals(state, MatchReplayStates.Incomplete, StringComparison.OrdinalIgnoreCase)
            ? MatchReplayStates.Incomplete
            : string.Equals(state, MatchReplayStates.Recording, StringComparison.OrdinalIgnoreCase)
                ? MatchReplayStates.Recording
            : string.Equals(state, MatchReplayStates.Finalizing, StringComparison.OrdinalIgnoreCase)
                ? MatchReplayStates.Finalizing
            : string.Equals(state, MatchReplayStates.SummaryOnly, StringComparison.OrdinalIgnoreCase)
                ? MatchReplayStates.SummaryOnly
            : string.Equals(state, MatchReplayStates.Rejected, StringComparison.OrdinalIgnoreCase)
                ? MatchReplayStates.Rejected
            : string.Equals(state, MatchReplayStates.Corrupt, StringComparison.OrdinalIgnoreCase)
                ? MatchReplayStates.Corrupt
                : MatchReplayStates.Ready;
    }

    private static void SaveAnalysis(WinSqliteConnection connection, MatchAnalysisReport report)
    {
        using var insert = connection.Prepare(
            "INSERT OR REPLACE INTO match_analysis(record_id, analysis_protocol, generated_utc, payload, sha256) VALUES(?, ?, ?, ?, ?);");
        var payload = MatchReplayPayload.Encode(report);
        insert.Bind(1, report.RecordId.Trim());
        insert.Bind(2, Math.Max(1, report.Protocol));
        insert.Bind(3, report.GeneratedUtc ?? "");
        insert.Bind(4, payload);
        insert.Bind(5, MatchReplayPayload.Sha256(payload));
        insert.Execute();
    }

    private static MatchAnalysisReport? ReadAnalysis(WinSqliteConnection.WinSqliteStatement query)
    {
        var payload = query.Blob(0);
        if (!string.Equals(MatchReplayPayload.Sha256(payload), query.Text(1), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Match analysis checksum mismatch.");
        }

        return MatchReplayPayload.Decode<MatchAnalysisReport>(payload);
    }

    private static MatchRecordMetadata CreateMetadata(MatchRecord record)
    {
        return new MatchRecordMetadata
        {
            BattleTitle = record.BattleTitle ?? "",
            IsFavorite = record.IsFavorite || string.Equals(record.Collection, MatchRecordCollections.Favorite, StringComparison.OrdinalIgnoreCase),
            Origin = record.Origin ?? MatchRecordOrigins.Auto,
            Tags = record.Tags ?? "",
            Notes = record.Notes ?? "",
            RequiredCapabilities = record.RequiredCapabilities ?? new List<string>(),
            OptionalCapabilities = record.OptionalCapabilities ?? new List<string>(),
            ContentDependencies = record.ContentDependencies ?? new List<string>(),
            ContentSha256 = record.ContentSha256 ?? "",
            CaptureDiagnostics = record.CaptureDiagnostics ?? new List<string>()
        };
    }

    private static MatchRecordMetadata DecodeMetadata(byte[] payload)
    {
        if (payload == null || payload.Length == 0) return new MatchRecordMetadata();
        try { return MatchReplayPayload.Decode<MatchRecordMetadata>(payload) ?? new MatchRecordMetadata(); }
        catch { return new MatchRecordMetadata(); }
    }

    private static List<string> SelectIds(WinSqliteConnection connection, string predicate, string value)
    {
        var result = new List<string>();
        using var query = connection.Prepare("SELECT record_id FROM battle_records WHERE " + predicate + ";");
        query.Bind(1, value);
        while (query.Read())
        {
            result.Add(query.Text(0));
        }

        return result;
    }

    private static void DeleteRecord(WinSqliteConnection connection, string recordId)
    {
        DeleteReplayV17(connection, recordId);
        using (var media = connection.Prepare("DELETE FROM replay_media WHERE record_id = ?;"))
        {
            media.Bind(1, recordId);
            media.Execute();
        }

        using (var analysis = connection.Prepare("DELETE FROM match_analysis WHERE record_id = ?;"))
        {
            analysis.Bind(1, recordId);
            analysis.Execute();
        }

        using var record = connection.Prepare("DELETE FROM battle_records WHERE record_id = ?;");
        record.Bind(1, recordId);
        record.Execute();
    }

    private static string NormalizeCollection(string? collection)
    {
        return string.Equals(collection, MatchRecordCollections.Favorite, StringComparison.OrdinalIgnoreCase)
            ? MatchRecordCollections.Favorite
            : MatchRecordCollections.Auto;
    }

    private static bool Contains(string? value, string query)
    {
        return value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void NormalizeMediaPaths(WinSqliteConnection connection)
    {
        if (!File.Exists(databasePath)) return;
        var root = Path.GetFullPath(Path.GetDirectoryName(databasePath) ?? ".")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var updates = new List<(string mediaId, string relativePath)>();
        using (var query = connection.Prepare("SELECT media_id, record_id, file_path FROM replay_media;"))
        {
            while (query.Read())
            {
                var stored = query.Text(2);
                if (!Path.IsPathRooted(stored)) continue;
                var full = Path.GetFullPath(stored);
                string? relative = null;
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    relative = full.Substring(root.Length);
                }
                else
                {
                    var relocated = Path.Combine(root, "Media", query.Text(1), Path.GetFileName(stored));
                    if (File.Exists(relocated)) relative = relocated.Substring(root.Length);
                }

                if (relative != null) updates.Add((query.Text(0), relative.Replace(Path.DirectorySeparatorChar, '/')));
            }
        }

        foreach (var item in updates)
        {
            using var update = connection.Prepare("UPDATE replay_media SET file_path = ? WHERE media_id = ?;");
            update.Bind(1, item.relativePath);
            update.Bind(2, item.mediaId);
            update.Execute();
        }
    }

    private static void TryRollback(WinSqliteConnection connection)
    {
        try
        {
            connection.Execute("ROLLBACK;");
        }
        catch
        {
        }
    }

    private static bool TableExistsForMediaJob(WinSqliteConnection connection, string table)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, table);
        return query.Read();
    }

    private void DeleteMediaDirectory(string recordId)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "Media"));
            var target = Path.GetFullPath(Path.Combine(root, recordId));
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch
        {
        }
    }

    private const string SelectColumns =
        "SELECT sequence, record_id, adventure_id, session_id, level_id, result, started_utc, ended_utc, collection_kind, "
        + "replay_state, replay_protocol, game_build, tool_build, mod_fingerprint, event_count, turn_count, compressed_bytes, "
        + "statistics_payload, initial_payload, metadata_payload FROM battle_records";
}
