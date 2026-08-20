using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    internal bool SaveSummaryV10(MatchRecord record, MatchAnalysisReport? analysis)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.RecordId)) return false;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (Exists(connection, record.RecordId))
                {
                    connection.Execute("ROLLBACK;");
                    return false;
                }

                record.ReplayProtocol = ReplayProtocolV10.DocumentVersion;
                record.ReplayState = MatchReplayStates.Incomplete;
                using (var insert = connection.Prepare(
                           "INSERT INTO battle_records(record_id, adventure_id, session_id, level_id, result, started_utc, ended_utc, "
                           + "collection_kind, replay_state, replay_protocol, game_build, tool_build, mod_fingerprint, event_count, "
                           + "turn_count, compressed_bytes, statistics_payload, initial_payload, metadata_payload) "
                           + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, 10, ?, ?, ?, ?, ?, 0, ?, ?, ?);"))
                {
                    insert.Bind(1, record.RecordId.Trim());
                    insert.Bind(2, record.AdventureId ?? "");
                    insert.Bind(3, record.SessionId ?? "");
                    insert.Bind(4, record.LevelId ?? "");
                    insert.Bind(5, record.Result ?? "");
                    insert.Bind(6, record.StartedUtc ?? "");
                    insert.Bind(7, record.EndedUtc ?? "");
                    insert.Bind(8, NormalizeCollection(record.Collection));
                    insert.Bind(9, MatchReplayStates.Incomplete);
                    insert.Bind(10, record.GameBuild ?? "");
                    insert.Bind(11, record.ToolBuild ?? "");
                    insert.Bind(12, record.ModFingerprint ?? "");
                    insert.Bind(13, Math.Max(0, record.EventCount));
                    insert.Bind(14, Math.Max(0, record.TurnCount));
                    insert.Bind(15, MatchReplayPayload.Encode(record.StatisticsJson ?? ""));
                    insert.Bind(16, MatchReplayPayload.Encode(new MatchReplayInitialState()));
                    insert.Bind(17, MatchReplayPayload.Encode(CreateMetadata(record)));
                    insert.Execute();
                }

                if (analysis != null)
                {
                    analysis.RecordId = record.RecordId;
                    SaveAnalysis(connection, analysis);
                }

                connection.Execute("COMMIT;");
                return true;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal bool SaveV10(
        MatchRecord record,
        ReplayDocumentV10 document,
        MatchAnalysisReport? analysis = null,
        int chunkTargetBytes = ReplayTimelineChunkerV10.DefaultTargetBytes)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (document == null) throw new ArgumentNullException(nameof(document));
        var validation = ReplayDocumentValidatorV10.Validate(document);
        if (!validation.IsValid)
        {
            throw new InvalidDataException("Replay Document v10 is invalid: " + validation.Message);
        }

        if (!string.Equals(record.RecordId, document.Header.RecordId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Replay record id does not match its v10 document.");
        }

        var chunks = ReplayTimelineChunkerV10.Build(document.Events, chunkTargetBytes);
        var skeleton = CloneWithoutTransientPayload(document);
        skeleton.Events.Clear();
        var documentPayload = ReplayPayloadV10.Encode(skeleton);
        var attachmentMoves = StageAttachments(document);
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("PRAGMA foreign_keys=ON;");
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (Exists(connection, record.RecordId))
                {
                    connection.Execute("ROLLBACK;");
                    CleanupStaging(attachmentMoves);
                    return false;
                }

                record.ReplayProtocol = ReplayProtocolV10.DocumentVersion;
                record.ReplayState = MatchReplayStates.Ready;
                record.LevelId = document.Header.LevelId;
                record.EventCount = document.Events.Count;
                record.TurnCount = Math.Max(record.TurnCount, document.InitialState.TurnIndex);
                record.CompressedBytes = chunks.Sum(item => (long)item.Payload.Length) + documentPayload.Length;
                record.ContentSha256 = document.Header.DocumentSha256;
                using (var insert = connection.Prepare(
                           "INSERT INTO battle_records(record_id, adventure_id, session_id, level_id, result, started_utc, ended_utc, "
                           + "collection_kind, replay_state, replay_protocol, game_build, tool_build, mod_fingerprint, event_count, "
                           + "turn_count, compressed_bytes, statistics_payload, initial_payload, metadata_payload) "
                           + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, record.RecordId.Trim());
                    insert.Bind(2, record.AdventureId ?? "");
                    insert.Bind(3, record.SessionId ?? "");
                    insert.Bind(4, record.LevelId ?? "");
                    insert.Bind(5, record.Result ?? "");
                    insert.Bind(6, record.StartedUtc ?? "");
                    insert.Bind(7, record.EndedUtc ?? "");
                    insert.Bind(8, NormalizeCollection(record.Collection));
                    insert.Bind(9, MatchReplayStates.Ready);
                    insert.Bind(10, ReplayProtocolV10.DocumentVersion);
                    insert.Bind(11, record.GameBuild ?? "");
                    insert.Bind(12, record.ToolBuild ?? "");
                    insert.Bind(13, record.ModFingerprint ?? "");
                    insert.Bind(14, record.EventCount);
                    insert.Bind(15, record.TurnCount);
                    insert.Bind(16, record.CompressedBytes);
                    insert.Bind(17, MatchReplayPayload.Encode(record.StatisticsJson ?? ""));
                    insert.Bind(18, MatchReplayPayload.Encode(new MatchReplayInitialState()));
                    insert.Bind(19, MatchReplayPayload.Encode(CreateMetadata(record)));
                    insert.Execute();
                }

                using (var insert = connection.Prepare(
                           "INSERT INTO replay_documents(record_id, document_version, document_state, document_sha256, "
                           + "initial_state_sha256, final_state_sha256, event_chain_sha256, renderer_profile, "
                           + "event_count, checkpoint_count, attachment_count, compressed_bytes, document_payload) "
                           + "VALUES(?, 10, 'Ready', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, record.RecordId);
                    insert.Bind(2, document.Header.DocumentSha256);
                    insert.Bind(3, document.Header.InitialLogicalStateSha256);
                    insert.Bind(4, document.Header.FinalLogicalStateSha256);
                    insert.Bind(5, document.Header.FinalEventChainSha256);
                    insert.Bind(6, document.Header.RenderProfileId ?? "");
                    insert.Bind(7, document.Events.Count);
                    insert.Bind(8, document.Checkpoints.Count);
                    insert.Bind(9, document.Attachments.Count);
                    insert.Bind(10, record.CompressedBytes);
                    insert.Bind(11, documentPayload);
                    insert.Execute();
                }

                foreach (var chunk in chunks)
                {
                    using var insert = connection.Prepare(
                        "INSERT INTO replay_timeline_chunks(record_id, chunk_index, first_sequence, last_sequence, "
                        + "first_time_ticks, last_time_ticks, sha256, payload) VALUES(?, ?, ?, ?, ?, ?, ?, ?);");
                    insert.Bind(1, record.RecordId);
                    insert.Bind(2, chunk.ChunkIndex);
                    insert.Bind(3, chunk.FirstSequence);
                    insert.Bind(4, chunk.LastSequence);
                    insert.Bind(5, chunk.FirstTimeTicks);
                    insert.Bind(6, chunk.LastTimeTicks);
                    insert.Bind(7, chunk.Sha256);
                    insert.Bind(8, chunk.Payload);
                    insert.Execute();
                }

                foreach (var attachment in document.Attachments)
                {
                    var finalPath = AttachmentPath(attachment);
                    using (var insert = connection.Prepare(
                               "INSERT OR IGNORE INTO replay_assets(asset_sha256, media_type, extension, file_path, byte_length, "
                               + "width, height, sample_rate, channels, sample_frames) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
                    {
                        insert.Bind(1, attachment.Sha256);
                        insert.Bind(2, attachment.MediaType ?? "");
                        insert.Bind(3, attachment.Extension ?? "");
                        insert.Bind(4, ToStoredPath(finalPath));
                        insert.Bind(5, attachment.ByteLength);
                        insert.Bind(6, attachment.Width);
                        insert.Bind(7, attachment.Height);
                        insert.Bind(8, attachment.SampleRate);
                        insert.Bind(9, attachment.Channels);
                        insert.Bind(10, attachment.SampleFrames);
                        insert.Execute();
                    }

                    using var reference = connection.Prepare(
                        "INSERT INTO replay_asset_refs(record_id, asset_sha256, usage, required) VALUES(?, ?, ?, ?);");
                    reference.Bind(1, record.RecordId);
                    reference.Bind(2, attachment.Sha256);
                    reference.Bind(3, attachment.Usage ?? "");
                    reference.Bind(4, attachment.Required ? 1 : 0);
                    reference.Execute();
                }

                if (analysis != null)
                {
                    analysis.RecordId = record.RecordId;
                    SaveAnalysis(connection, analysis);
                }

                CommitAttachments(attachmentMoves);
                connection.Execute("COMMIT;");
                return true;
            }
            catch
            {
                TryRollback(connection);
                CleanupStaging(attachmentMoves);
                throw;
            }
        }
    }

    internal ReplayDocumentV10? LoadV10(string recordId, bool loadAttachmentPayloads = false)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            ReplayDocumentV10? document;
            using (var query = connection.Prepare(
                       "SELECT document_version, document_state, document_payload FROM replay_documents "
                       + "WHERE record_id=? LIMIT 1;"))
            {
                query.Bind(1, recordId.Trim());
                if (!query.Read()) return null;
                if (query.Int64(0) != ReplayProtocolV10.DocumentVersion
                    || !string.Equals(query.Text(1), MatchReplayStates.Ready, StringComparison.Ordinal))
                {
                    return null;
                }

                document = ReplayPayloadV10.Decode<ReplayDocumentV10>(query.Blob(2));
            }

            var chunks = new List<ReplayTimelineChunkV10>();
            using (var query = connection.Prepare(
                       "SELECT chunk_index, first_sequence, last_sequence, first_time_ticks, last_time_ticks, sha256, payload "
                       + "FROM replay_timeline_chunks WHERE record_id=? ORDER BY chunk_index;"))
            {
                query.Bind(1, recordId.Trim());
                while (query.Read())
                {
                    chunks.Add(new ReplayTimelineChunkV10
                    {
                        ChunkIndex = (int)query.Int64(0),
                        FirstSequence = query.Int64(1),
                        LastSequence = query.Int64(2),
                        FirstTimeTicks = query.Int64(3),
                        LastTimeTicks = query.Int64(4),
                        Sha256 = query.Text(5),
                        Payload = query.Blob(6)
                    });
                }
            }

            document.Events = ReplayTimelineChunkerV10.Decode(chunks).ToList();
            if (loadAttachmentPayloads)
            {
                foreach (var attachment in document.Attachments)
                {
                    var path = AttachmentPath(attachment);
                    if (File.Exists(path)) attachment.Payload = File.ReadAllBytes(path);
                }
            }

            var validation = ReplayDocumentValidatorV10.Validate(document);
            if (!validation.IsValid)
            {
                throw new InvalidDataException("Stored Replay Document v10 is invalid: " + validation.Message);
            }

            return document;
        }
    }

    internal string ResolveReplayAsset(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return "";
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare("SELECT file_path FROM replay_assets WHERE asset_sha256=? LIMIT 1;");
            query.Bind(1, sha256.Trim());
            if (!query.Read()) return "";
            return ResolveStoredPath(query.Text(0));
        }
    }

    internal void CreateExportJob(MatchReplayExportJob job)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.JobId) || string.IsNullOrWhiteSpace(job.RecordId))
        {
            throw new ArgumentException("Replay export job identity is missing.", nameof(job));
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            job.Revision = 0;
            job.CreatedUtc = string.IsNullOrWhiteSpace(job.CreatedUtc) ? DateTime.UtcNow.ToString("O") : job.CreatedUtc;
            job.UpdatedUtc = DateTime.UtcNow.ToString("O");
            InsertExportJob(connection, job);
        }
    }

    internal bool UpdateExportJob(MatchReplayExportJob job)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.JobId)) return false;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            var expected = job.Revision;
            job.UpdatedUtc = DateTime.UtcNow.ToString("O");
            using var update = connection.Prepare(
                "UPDATE replay_export_jobs SET state=?, revision=revision+1, updated_utc=?, progress=?, staging_path=?, "
                + "target_path=?, output_sha256=?, profile_id=?, message=?, error_code=?, cancel_requested=?, attempt_count=?, "
                + "width=?, height=?, frames_per_second=?, frame_count=?, audio_sample_frames=?, file_bytes=?, estimated_bytes=? "
                + "WHERE job_id=? AND revision=?;");
            BindExportJobUpdate(update, job);
            update.Bind(19, job.JobId);
            update.Bind(20, expected);
            update.Execute();
            if (connection.Changes <= 0) return false;
            job.Revision = expected + 1;
            return true;
        }
    }

    internal MatchReplayExportJob? LoadExportJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(ExportJobSelect + " WHERE job_id=? LIMIT 1;");
            query.Bind(1, jobId.Trim());
            return query.Read() ? ReadExportJob(query) : null;
        }
    }

    internal MatchReplayExportJob? LoadLatestExportJob()
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(ExportJobSelect + " ORDER BY created_utc DESC LIMIT 1;");
            return query.Read() ? ReadExportJob(query) : null;
        }
    }

    internal IReadOnlyList<MatchReplayExportJob> LoadRecoverableExportJobs()
    {
        var result = new List<MatchReplayExportJob>();
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                ExportJobSelect + " WHERE state NOT IN ('Ready','Corrupt','Failed','Cancelled') ORDER BY created_utc;");
            while (query.Read()) result.Add(ReadExportJob(query));
        }

        return result;
    }

    internal bool CommitExportMedia(MatchReplayExportJob job, MatchMediaAsset asset)
    {
        if (job == null || asset == null
            || !string.Equals(job.JobId, asset.MediaId, StringComparison.Ordinal)
            || !string.Equals(job.RecordId, asset.RecordId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Replay export commit identity mismatch.");
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (!Exists(connection, job.RecordId))
                {
                    throw new InvalidDataException("Cannot commit media for a missing match record.");
                }
                using (var insert = connection.Prepare(
                           "INSERT OR REPLACE INTO replay_media(media_id, record_id, media_kind, media_format, file_path, created_utc, "
                           + "media_state, duration_ms, width, height, frames_per_second, file_bytes, sha256, timeline_payload, error_text) "
                           + "VALUES(?, ?, 'Video', 'MP4', ?, ?, 'Ready', ?, ?, ?, ?, ?, ?, ?, '');"))
                {
                    insert.Bind(1, asset.MediaId);
                    insert.Bind(2, asset.RecordId);
                    insert.Bind(3, asset.FilePath);
                    insert.Bind(4, asset.CreatedUtc);
                    insert.Bind(5, asset.DurationMilliseconds);
                    insert.Bind(6, asset.Width);
                    insert.Bind(7, asset.Height);
                    insert.Bind(8, asset.FramesPerSecond);
                    insert.Bind(9, asset.FileBytes);
                    insert.Bind(10, asset.Sha256);
                    insert.Bind(11, MatchReplayPayload.Encode(asset.TimelineJson ?? ""));
                    insert.Execute();
                }

                var expected = job.Revision;
                job.State = MatchReplayExportStates.Ready;
                job.Progress = 1f;
                job.OutputPath = asset.FilePath;
                job.TargetPath = asset.FilePath;
                job.FileBytes = asset.FileBytes;
                job.OutputSha256 = asset.Sha256;
                job.UpdatedUtc = DateTime.UtcNow.ToString("O");
                using (var update = connection.Prepare(
                           "UPDATE replay_export_jobs SET state='Ready', revision=revision+1, updated_utc=?, progress=1, "
                           + "target_path=?, output_sha256=?, message=?, file_bytes=? WHERE job_id=? AND revision=? AND state='Committing';"))
                {
                    update.Bind(1, job.UpdatedUtc);
                    update.Bind(2, job.TargetPath);
                    update.Bind(3, job.OutputSha256);
                    update.Bind(4, job.Message ?? "");
                    update.Bind(5, job.FileBytes);
                    update.Bind(6, job.JobId);
                    update.Bind(7, expected);
                    update.Execute();
                    if (connection.Changes <= 0)
                    {
                        connection.Execute("ROLLBACK;");
                        return false;
                    }
                }

                connection.Execute("COMMIT;");
                job.Revision = expected + 1;
                return true;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal MatchMediaAsset? LoadMediaForDeletion(string mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                "SELECT media_id, record_id, media_kind, media_format, file_path, created_utc, media_state, duration_ms, width, height, "
                + "frames_per_second, file_bytes, sha256, timeline_payload, error_text FROM replay_media WHERE media_id=? LIMIT 1;");
            query.Bind(1, mediaId.Trim());
            if (!query.Read()) return null;
            return new MatchMediaAsset
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
            };
        }
    }

    internal IReadOnlyList<string> LoadLegacyReplayIds()
    {
        var result = new List<string>();
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                "SELECT record_id FROM battle_records WHERE replay_protocol<>10 ORDER BY sequence;");
            while (query.Read()) result.Add(query.Text(0));
        }
        return result;
    }

    internal IReadOnlyList<string> LoadAllMediaPaths()
    {
        var result = new List<string>();
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare("SELECT file_path FROM replay_media;");
            while (query.Read()) result.Add(query.Text(0));
        }
        return result;
    }

    internal void ApplyLegacyReplayCleanup(IReadOnlyCollection<string> recordIds, string migrationId)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                foreach (var recordId in recordIds.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal))
                {
                    using (var chunks = connection.Prepare("DELETE FROM replay_chunks WHERE record_id=?;"))
                    {
                        chunks.Bind(1, recordId);
                        chunks.Execute();
                    }
                    using var update = connection.Prepare(
                        "UPDATE battle_records SET replay_protocol=10, replay_state='Incomplete', compressed_bytes=0, "
                        + "initial_payload=?, metadata_payload=? WHERE record_id=? AND replay_protocol<>10;");
                    update.Bind(1, MatchReplayPayload.Encode(new MatchReplayInitialState()));
                    var record = GetRecordForMigration(connection, recordId);
                    record.ContentSha256 = "";
                    record.RequiredCapabilities.Clear();
                    record.OptionalCapabilities.Clear();
                    record.CaptureDiagnostics.Add("legacy replay removed by migration " + migrationId);
                    update.Bind(2, MatchReplayPayload.Encode(CreateMetadata(record)));
                    update.Bind(3, recordId);
                    update.Execute();
                }
                using var migration = connection.Prepare(
                    "UPDATE replay_migrations SET state='Applied', applied_utc=? WHERE migration_id=?;");
                migration.Bind(1, DateTime.UtcNow.ToString("O"));
                migration.Bind(2, migrationId);
                migration.Execute();
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal void SaveMigrationScan(
        string migrationId,
        string reportPath,
        string reportSha256,
        int recordCount,
        long chunkBytes)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var insert = connection.Prepare(
                "INSERT INTO replay_migrations(migration_id, state, scanned_utc, applied_utc, report_path, report_sha256, "
                + "record_count, chunk_bytes) VALUES(?, 'Scanned', ?, '', ?, ?, ?, ?);");
            insert.Bind(1, migrationId);
            insert.Bind(2, DateTime.UtcNow.ToString("O"));
            insert.Bind(3, reportPath);
            insert.Bind(4, reportSha256);
            insert.Bind(5, recordCount);
            insert.Bind(6, chunkBytes);
            insert.Execute();
        }
    }

    internal bool ValidateMigrationScan(string migrationId, string reportSha256)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                "SELECT report_sha256, state FROM replay_migrations WHERE migration_id=? LIMIT 1;");
            query.Bind(1, migrationId ?? "");
            return query.Read()
                   && string.Equals(query.Text(0), reportSha256, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(query.Text(1), "Scanned", StringComparison.Ordinal);
        }
    }

    private static void EnsureV10Tables(WinSqliteConnection connection)
    {
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_documents("
                           + "record_id TEXT PRIMARY KEY, document_version INTEGER NOT NULL CHECK(document_version=10), "
                           + "document_state TEXT NOT NULL, document_sha256 TEXT NOT NULL, initial_state_sha256 TEXT NOT NULL, "
                           + "final_state_sha256 TEXT NOT NULL, event_chain_sha256 TEXT NOT NULL, renderer_profile TEXT NOT NULL, "
                           + "event_count INTEGER NOT NULL, checkpoint_count INTEGER NOT NULL, attachment_count INTEGER NOT NULL, "
                           + "compressed_bytes INTEGER NOT NULL, document_payload BLOB NOT NULL, "
                           + "FOREIGN KEY(record_id) REFERENCES battle_records(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_timeline_chunks("
                           + "record_id TEXT NOT NULL, chunk_index INTEGER NOT NULL, first_sequence INTEGER NOT NULL, "
                           + "last_sequence INTEGER NOT NULL, first_time_ticks INTEGER NOT NULL, last_time_ticks INTEGER NOT NULL, "
                           + "sha256 TEXT NOT NULL, payload BLOB NOT NULL, PRIMARY KEY(record_id, chunk_index), "
                           + "FOREIGN KEY(record_id) REFERENCES replay_documents(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_assets("
                           + "asset_sha256 TEXT PRIMARY KEY, media_type TEXT NOT NULL, extension TEXT NOT NULL, file_path TEXT NOT NULL, "
                           + "byte_length INTEGER NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL, sample_rate INTEGER NOT NULL, "
                           + "channels INTEGER NOT NULL, sample_frames INTEGER NOT NULL);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_asset_refs("
                           + "record_id TEXT NOT NULL, asset_sha256 TEXT NOT NULL, usage TEXT NOT NULL, required INTEGER NOT NULL, "
                           + "PRIMARY KEY(record_id, asset_sha256, usage), "
                           + "FOREIGN KEY(record_id) REFERENCES replay_documents(record_id) ON DELETE CASCADE, "
                           + "FOREIGN KEY(asset_sha256) REFERENCES replay_assets(asset_sha256));");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_replay_asset_refs_hash ON replay_asset_refs(asset_sha256);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_export_jobs("
                           + "job_id TEXT PRIMARY KEY, record_id TEXT NOT NULL, state TEXT NOT NULL, revision INTEGER NOT NULL, "
                           + "created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, progress REAL NOT NULL, staging_path TEXT NOT NULL, "
                           + "target_path TEXT NOT NULL, output_sha256 TEXT NOT NULL, profile_id TEXT NOT NULL, message TEXT NOT NULL, "
                           + "error_code TEXT NOT NULL, cancel_requested INTEGER NOT NULL, attempt_count INTEGER NOT NULL, "
                           + "width INTEGER NOT NULL, height INTEGER NOT NULL, frames_per_second INTEGER NOT NULL, frame_count INTEGER NOT NULL, "
                           + "audio_sample_frames INTEGER NOT NULL, file_bytes INTEGER NOT NULL, estimated_bytes INTEGER NOT NULL, "
                           + "FOREIGN KEY(record_id) REFERENCES battle_records(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_replay_export_jobs_state ON replay_export_jobs(state, created_utc);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_migrations("
                           + "migration_id TEXT PRIMARY KEY, state TEXT NOT NULL, scanned_utc TEXT NOT NULL, applied_utc TEXT NOT NULL, "
                           + "report_path TEXT NOT NULL, report_sha256 TEXT NOT NULL, record_count INTEGER NOT NULL, chunk_bytes INTEGER NOT NULL);");
    }

    private void ReconcileV10Files(WinSqliteConnection connection)
    {
        var knownAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var query = connection.Prepare("SELECT asset_sha256, file_path FROM replay_assets;"))
        {
            while (query.Read())
            {
                var hash = query.Text(0);
                var path = ResolveStoredPath(query.Text(1));
                knownAssets.Add(Path.GetFullPath(path));
                if (File.Exists(path)) continue;
                using (var corruptDocuments = connection.Prepare(
                           "UPDATE replay_documents SET document_state='Corrupt' WHERE record_id IN "
                           + "(SELECT record_id FROM replay_asset_refs WHERE asset_sha256=?);"))
                {
                    corruptDocuments.Bind(1, hash);
                    corruptDocuments.Execute();
                }
                using var corruptRecords = connection.Prepare(
                    "UPDATE battle_records SET replay_state='Corrupt' WHERE record_id IN "
                    + "(SELECT record_id FROM replay_asset_refs WHERE asset_sha256=?);");
                corruptRecords.Bind(1, hash);
                corruptRecords.Execute();
            }
        }

        Directory.CreateDirectory(AttachmentDirectory);
        var quarantine = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "Quarantine", "Attachments");
        foreach (var file in Directory.GetFiles(AttachmentDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var full = Path.GetFullPath(file);
            if (knownAssets.Contains(full)) continue;
            if (full.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
                || full.Contains(".partial-"))
            {
                try { File.Delete(full); } catch { }
                continue;
            }
            if (full.EndsWith(".delete.partial", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(full); } catch { }
                continue;
            }
            Directory.CreateDirectory(quarantine);
            var target = Path.Combine(quarantine, Path.GetFileName(full) + ".orphan-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { File.Move(full, target); } catch { }
        }

        using var media = connection.Prepare("SELECT media_id, file_path, sha256 FROM replay_media WHERE media_state='Ready';");
        var corruptMedia = new List<(string Id, string Error)>();
        while (media.Read())
        {
            try
            {
                var path = ResolveStoredPath(media.Text(1));
                if (!File.Exists(path)) corruptMedia.Add((media.Text(0), "registered media file is missing"));
                else if (!string.Equals(FileSha256(path), media.Text(2), StringComparison.OrdinalIgnoreCase))
                {
                    corruptMedia.Add((media.Text(0), "registered media hash mismatch"));
                }
            }
            catch
            {
                corruptMedia.Add((media.Text(0), "registered media path is invalid"));
            }
        }
        foreach (var item in corruptMedia)
        {
            using var corrupt = connection.Prepare(
                "UPDATE replay_media SET media_state='Corrupt', error_text=? WHERE media_id=?;");
            corrupt.Bind(1, item.Error);
            corrupt.Bind(2, item.Id);
            corrupt.Execute();
            using var corruptJob = connection.Prepare(
                "UPDATE replay_export_jobs SET state='Corrupt', revision=revision+1, updated_utc=?, error_code='media-corrupt', "
                + "message=? WHERE job_id=? AND state='Ready';");
            corruptJob.Bind(1, DateTime.UtcNow.ToString("O"));
            corruptJob.Bind(2, item.Error);
            corruptJob.Bind(3, item.Id);
            corruptJob.Execute();
        }
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream).Select(item => item.ToString("x2")));
    }

    private static void DeleteReplayV10(WinSqliteConnection connection, string recordId)
    {
        if (!TableExistsV10(connection, "replay_documents")) return;
        using (var jobs = connection.Prepare("DELETE FROM replay_export_jobs WHERE record_id=?;"))
        {
            jobs.Bind(1, recordId);
            jobs.Execute();
        }
        using (var references = connection.Prepare("DELETE FROM replay_asset_refs WHERE record_id=?;"))
        {
            references.Bind(1, recordId);
            references.Execute();
        }

        using (var chunks = connection.Prepare("DELETE FROM replay_timeline_chunks WHERE record_id=?;"))
        {
            chunks.Bind(1, recordId);
            chunks.Execute();
        }

        using var document = connection.Prepare("DELETE FROM replay_documents WHERE record_id=?;");
        document.Bind(1, recordId);
        document.Execute();
    }

    private void SweepUnreferencedReplayAssets()
    {
        var candidates = new List<(string Sha256, string Path, string Staging)>();
        using (var connection = Open())
        using (var query = connection.Prepare(
                   "SELECT a.asset_sha256, a.file_path FROM replay_assets a "
                   + "LEFT JOIN replay_asset_refs r ON r.asset_sha256=a.asset_sha256 WHERE r.asset_sha256 IS NULL;"))
        {
            while (query.Read())
            {
                var path = ResolveStoredPath(query.Text(1));
                candidates.Add((query.Text(0), path, path + ".delete.partial"));
            }
        }
        if (candidates.Count == 0) return;
        var moved = new List<(string Original, string Staging)>();
        try
        {
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate.Path)) continue;
                if (File.Exists(candidate.Staging)) File.Delete(candidate.Staging);
                File.Move(candidate.Path, candidate.Staging);
                moved.Add((candidate.Path, candidate.Staging));
            }
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                foreach (var candidate in candidates)
                {
                    using var delete = connection.Prepare(
                        "DELETE FROM replay_assets WHERE asset_sha256=? "
                        + "AND NOT EXISTS(SELECT 1 FROM replay_asset_refs WHERE asset_sha256=?); ");
                    delete.Bind(1, candidate.Sha256);
                    delete.Bind(2, candidate.Sha256);
                    delete.Execute();
                }
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
            foreach (var pair in moved)
            {
                try { if (File.Exists(pair.Staging)) File.Delete(pair.Staging); } catch { }
            }
        }
        catch
        {
            foreach (var pair in moved)
            {
                try
                {
                    if (File.Exists(pair.Staging) && !File.Exists(pair.Original)) File.Move(pair.Staging, pair.Original);
                }
                catch
                {
                }
            }
            throw;
        }
    }

    private List<AttachmentMove> StageAttachments(ReplayDocumentV10 document)
    {
        var result = new List<AttachmentMove>();
        Directory.CreateDirectory(AttachmentDirectory);
        foreach (var attachment in document.Attachments)
        {
            var finalPath = AttachmentPath(attachment);
            if (File.Exists(finalPath))
            {
                if (!string.Equals(FileSha256(finalPath), attachment.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Existing replay attachment hash mismatch: " + attachment.Sha256);
                }
                continue;
            }
            if (attachment.Payload == null || attachment.Payload.LongLength != attachment.ByteLength)
            {
                throw new InvalidDataException("Replay attachment payload is missing: " + attachment.Sha256);
            }

            var staging = finalPath + ".partial-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(staging, attachment.Payload);
            result.Add(new AttachmentMove(staging, finalPath));
        }

        return result;
    }

    private static void CommitAttachments(IEnumerable<AttachmentMove> moves)
    {
        foreach (var move in moves)
        {
            if (File.Exists(move.FinalPath))
            {
                File.Delete(move.StagingPath);
            }
            else
            {
                File.Move(move.StagingPath, move.FinalPath);
            }
        }
    }

    private static void CleanupStaging(IEnumerable<AttachmentMove> moves)
    {
        foreach (var move in moves)
        {
            try { if (File.Exists(move.StagingPath)) File.Delete(move.StagingPath); } catch { }
        }
    }

    private string AttachmentDirectory => Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "Attachments");

    private string AttachmentPath(ReplayAttachmentV10 attachment)
    {
        var extension = NormalizeExtension(attachment.Extension);
        return Path.Combine(AttachmentDirectory, attachment.Sha256.ToLowerInvariant() + extension);
    }

    private string ToStoredPath(string fullPath)
    {
        var root = Path.GetFullPath(Path.GetDirectoryName(databasePath) ?? ".")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(fullPath);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Replay attachment path escapes the MatchRecords root.");
        }

        return resolved.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    private string ResolveStoredPath(string storedPath)
    {
        var root = Path.GetFullPath(Path.GetDirectoryName(databasePath) ?? ".")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, (storedPath ?? "").Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Replay attachment path escapes the MatchRecords root.");
        }

        return resolved;
    }

    private static string NormalizeExtension(string extension)
    {
        var value = (extension ?? "").Trim().ToLowerInvariant();
        return value is ".png" or ".jpg" or ".jpeg" or ".wav" or ".flac" ? value : ".bin";
    }

    private static bool TableExistsV10(WinSqliteConnection connection, string table)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, table);
        return query.Read();
    }

    private static ReplayDocumentV10 CloneWithoutTransientPayload(ReplayDocumentV10 document)
    {
        var bytes = ReplayCanonicalJsonV10.SerializeUtf8(document);
        var clone = Newtonsoft.Json.JsonConvert.DeserializeObject<ReplayDocumentV10>(System.Text.Encoding.UTF8.GetString(bytes))
                    ?? throw new InvalidDataException("Replay Document v10 could not be cloned.");
        foreach (var attachment in clone.Attachments) attachment.Payload = Array.Empty<byte>();
        return clone;
    }

    private readonly struct AttachmentMove
    {
        internal AttachmentMove(string stagingPath, string finalPath)
        {
            StagingPath = stagingPath;
            FinalPath = finalPath;
        }

        internal string StagingPath { get; }

        internal string FinalPath { get; }
    }

    private static void InsertExportJob(WinSqliteConnection connection, MatchReplayExportJob job)
    {
        using var insert = connection.Prepare(
            "INSERT INTO replay_export_jobs(job_id, record_id, state, revision, created_utc, updated_utc, progress, staging_path, "
            + "target_path, output_sha256, profile_id, message, error_code, cancel_requested, attempt_count, width, height, "
            + "frames_per_second, frame_count, audio_sample_frames, file_bytes, estimated_bytes) "
            + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);");
        insert.Bind(1, job.JobId);
        insert.Bind(2, job.RecordId);
        insert.Bind(3, job.State);
        insert.Bind(4, job.Revision);
        insert.Bind(5, job.CreatedUtc);
        insert.Bind(6, job.UpdatedUtc);
        insert.Bind(7, job.Progress);
        insert.Bind(8, job.StagingPath ?? "");
        insert.Bind(9, job.TargetPath ?? "");
        insert.Bind(10, job.OutputSha256 ?? "");
        insert.Bind(11, job.ProfileId ?? "");
        insert.Bind(12, job.Message ?? "");
        insert.Bind(13, job.ErrorCode ?? "");
        insert.Bind(14, job.CancelRequested ? 1 : 0);
        insert.Bind(15, job.AttemptCount);
        insert.Bind(16, job.Width);
        insert.Bind(17, job.Height);
        insert.Bind(18, job.FramesPerSecond);
        insert.Bind(19, job.FrameCount);
        insert.Bind(20, job.AudioSampleFrames);
        insert.Bind(21, job.FileBytes);
        insert.Bind(22, job.EstimatedBytes);
        insert.Execute();
    }

    private static MatchRecord GetRecordForMigration(WinSqliteConnection connection, string recordId)
    {
        using var query = connection.Prepare(SelectColumns + " WHERE record_id=? LIMIT 1;");
        query.Bind(1, recordId);
        if (!query.Read()) throw new InvalidDataException("Legacy replay record disappeared during migration: " + recordId);
        return ReadRecord(query);
    }

    private static void BindExportJobUpdate(WinSqliteConnection.WinSqliteStatement update, MatchReplayExportJob job)
    {
        update.Bind(1, job.State ?? MatchReplayExportStates.Failed);
        update.Bind(2, job.UpdatedUtc ?? "");
        update.Bind(3, job.Progress);
        update.Bind(4, job.StagingPath ?? "");
        update.Bind(5, job.TargetPath ?? "");
        update.Bind(6, job.OutputSha256 ?? "");
        update.Bind(7, job.ProfileId ?? "");
        update.Bind(8, job.Message ?? "");
        update.Bind(9, job.ErrorCode ?? "");
        update.Bind(10, job.CancelRequested ? 1 : 0);
        update.Bind(11, job.AttemptCount);
        update.Bind(12, job.Width);
        update.Bind(13, job.Height);
        update.Bind(14, job.FramesPerSecond);
        update.Bind(15, job.FrameCount);
        update.Bind(16, job.AudioSampleFrames);
        update.Bind(17, job.FileBytes);
        update.Bind(18, job.EstimatedBytes);
    }

    private static MatchReplayExportJob ReadExportJob(WinSqliteConnection.WinSqliteStatement query)
    {
        return new MatchReplayExportJob
        {
            JobId = query.Text(0),
            RecordId = query.Text(1),
            State = query.Text(2),
            Revision = query.Int64(3),
            CreatedUtc = query.Text(4),
            UpdatedUtc = query.Text(5),
            Progress = (float)query.Double(6),
            StagingPath = query.Text(7),
            TargetPath = query.Text(8),
            OutputPath = query.Text(8),
            OutputSha256 = query.Text(9),
            ProfileId = query.Text(10),
            Message = query.Text(11),
            ErrorCode = query.Text(12),
            CancelRequested = query.Int64(13) != 0,
            AttemptCount = (int)query.Int64(14),
            Width = (int)query.Int64(15),
            Height = (int)query.Int64(16),
            FramesPerSecond = (int)query.Int64(17),
            FrameCount = query.Int64(18),
            AudioSampleFrames = query.Int64(19),
            FileBytes = query.Int64(20),
            EstimatedBytes = query.Int64(21)
        };
    }

    private const string ExportJobSelect =
        "SELECT job_id, record_id, state, revision, created_utc, updated_utc, progress, staging_path, target_path, "
        + "output_sha256, profile_id, message, error_code, cancel_requested, attempt_count, width, height, frames_per_second, "
        + "frame_count, audio_sample_frames, file_bytes, estimated_bytes FROM replay_export_jobs";
}
