using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    internal bool SaveSummaryV11(MatchRecord record, MatchAnalysisReport? analysis)
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

                record.ReplayProtocol = ReplayProtocolV11.DocumentVersion;
                record.ReplayState = MatchReplayStates.SummaryOnly;
                using (var insert = connection.Prepare(
                           "INSERT INTO battle_records(record_id, adventure_id, session_id, level_id, result, started_utc, ended_utc, "
                           + "collection_kind, replay_state, replay_protocol, game_build, tool_build, mod_fingerprint, event_count, "
                           + "turn_count, compressed_bytes, statistics_payload, initial_payload, metadata_payload) "
                           + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, 11, ?, ?, ?, ?, ?, 0, ?, ?, ?);"))
                {
                    insert.Bind(1, record.RecordId.Trim());
                    insert.Bind(2, record.AdventureId ?? "");
                    insert.Bind(3, record.SessionId ?? "");
                    insert.Bind(4, record.LevelId ?? "");
                    insert.Bind(5, record.Result ?? "");
                    insert.Bind(6, record.StartedUtc ?? "");
                    insert.Bind(7, record.EndedUtc ?? "");
                    insert.Bind(8, NormalizeCollection(record.Collection));
                    insert.Bind(9, MatchReplayStates.SummaryOnly);
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

    internal bool SaveV11(
        MatchRecord record,
        ReplayDocumentV11 document,
        MatchAnalysisReport? analysis = null,
        int chunkTargetBytes = ReplayTimelineChunkerV11.DefaultTargetBytes)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (document == null) throw new ArgumentNullException(nameof(document));
        var validation = ReplayDocumentValidatorV11.Validate(document);
        if (!validation.IsValid)
        {
            throw new InvalidDataException("Replay Document v11 is invalid: " + validation.Message);
        }

        return SaveDocumentV11(
            record,
            document,
            analysis,
            chunkTargetBytes,
            MatchReplayStates.Ready,
            "Ready");
    }

    internal bool SaveRejectedV11(
        MatchRecord record,
        ReplayDocumentV11 document,
        MatchAnalysisReport? analysis = null,
        int chunkTargetBytes = ReplayTimelineChunkerV11.DefaultTargetBytes)
    {
        return SaveDocumentV11(
            record,
            document,
            analysis,
            chunkTargetBytes,
            MatchReplayStates.SummaryOnly,
            "Rejected");
    }

    private bool SaveDocumentV11(
        MatchRecord record,
        ReplayDocumentV11 document,
        MatchAnalysisReport? analysis,
        int chunkTargetBytes,
        string replayState,
        string documentState)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (document == null) throw new ArgumentNullException(nameof(document));

        if (!string.Equals(record.RecordId, document.Header.RecordId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Replay record id does not match its v11 document.");
        }

        var chunks = ReplayTimelineChunkerV11.Build(document.Events, chunkTargetBytes);
        var skeleton = CloneWithoutTransientPayload(document);
        skeleton.Events.Clear();
        var documentPayload = ReplayPayloadV11.Encode(skeleton);
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

                record.ReplayProtocol = ReplayProtocolV11.DocumentVersion;
                record.ReplayState = replayState;
                record.LevelId = document.Header.LevelId;
                record.BattleTitle = document.Header.BattleTitle;
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
                    insert.Bind(9, replayState);
                    insert.Bind(10, ReplayProtocolV11.DocumentVersion);
                    insert.Bind(11, record.GameBuild ?? "");
                    insert.Bind(12, record.ToolBuild ?? "");
                    insert.Bind(13, record.ModFingerprint ?? "");
                    insert.Bind(14, record.EventCount);
                    insert.Bind(15, record.TurnCount);
                    insert.Bind(16, record.CompressedBytes);
                    insert.Bind(17, MatchReplayPayload.Encode(record.StatisticsJson ?? ""));
                    insert.Bind(18, MatchReplayPayload.Encode(record.InitialState ?? new MatchReplayInitialState()));
                    insert.Bind(19, MatchReplayPayload.Encode(CreateMetadata(record)));
                    insert.Execute();
                }

                using (var insert = connection.Prepare(
                           "INSERT INTO replay_documents(record_id, document_version, document_state, document_sha256, "
                           + "initial_state_sha256, final_state_sha256, event_chain_sha256, renderer_profile, "
                           + "event_count, checkpoint_count, attachment_count, compressed_bytes, document_payload) "
                           + "VALUES(?, 11, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, record.RecordId);
                    insert.Bind(2, documentState);
                    insert.Bind(3, document.Header.DocumentSha256);
                    insert.Bind(4, document.Header.InitialLogicalStateSha256);
                    insert.Bind(5, document.Header.FinalLogicalStateSha256);
                    insert.Bind(6, document.Header.FinalEventChainSha256);
                    insert.Bind(7, document.Header.RenderProfileId ?? "");
                    insert.Bind(8, document.Events.Count);
                    insert.Bind(9, document.Checkpoints.Count);
                    insert.Bind(10, document.Attachments.Count);
                    insert.Bind(11, record.CompressedBytes);
                    insert.Bind(12, documentPayload);
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

    internal ReplayDocumentV11? LoadV11(string recordId, bool loadAttachmentPayloads = false)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            ReplayDocumentV11? document;
            using (var query = connection.Prepare(
                       "SELECT document_version, document_state, document_payload FROM replay_documents "
                       + "WHERE record_id=? LIMIT 1;"))
            {
                query.Bind(1, recordId.Trim());
                if (!query.Read()) return null;
                if (query.Int64(0) != ReplayProtocolV11.DocumentVersion
                    || !string.Equals(query.Text(1), MatchReplayStates.Ready, StringComparison.Ordinal))
                {
                    return null;
                }

                document = ReplayPayloadV11.Decode<ReplayDocumentV11>(query.Blob(2));
            }

            var chunks = new List<ReplayTimelineChunkV11>();
            using (var query = connection.Prepare(
                       "SELECT chunk_index, first_sequence, last_sequence, first_time_ticks, last_time_ticks, sha256, payload "
                       + "FROM replay_timeline_chunks WHERE record_id=? ORDER BY chunk_index;"))
            {
                query.Bind(1, recordId.Trim());
                while (query.Read())
                {
                    chunks.Add(new ReplayTimelineChunkV11
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

            document.Events = ReplayTimelineChunkerV11.Decode(chunks).ToList();
            if (loadAttachmentPayloads)
            {
                foreach (var attachment in document.Attachments)
                {
                    var path = AttachmentPath(attachment);
                    if (File.Exists(path)) attachment.Payload = File.ReadAllBytes(path);
                }
            }

            var validation = ReplayDocumentValidatorV11.Validate(document);
            if (!validation.IsValid)
            {
                throw new InvalidDataException("Stored Replay Document v11 is invalid: " + validation.Message);
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
                "SELECT record_id FROM battle_records WHERE replay_protocol<11 ORDER BY sequence;");
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
                        "UPDATE battle_records SET replay_protocol=11, replay_state='SummaryOnly', compressed_bytes=0, "
                        + "initial_payload=?, metadata_payload=? WHERE record_id=? AND replay_protocol<11;");
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

    private static void EnsureV11Tables(WinSqliteConnection connection)
    {
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_migrations("
                           + "migration_id TEXT PRIMARY KEY, state TEXT NOT NULL, scanned_utc TEXT NOT NULL, applied_utc TEXT NOT NULL, "
                           + "report_path TEXT NOT NULL, report_sha256 TEXT NOT NULL, record_count INTEGER NOT NULL, chunk_bytes INTEGER NOT NULL);");
        if (TableExistsV11(connection, "replay_documents") && !ReplayDocumentTableIsV11(connection))
        {
            long recordCount = 0;
            long chunkBytes = 0;
            using (var count = connection.Prepare("SELECT COUNT(*) FROM replay_documents;"))
                if (count.Read()) recordCount = count.Int64(0);
            if (TableExistsV11(connection, "replay_timeline_chunks"))
            {
                using var bytes = connection.Prepare("SELECT COALESCE(SUM(LENGTH(payload)), 0) FROM replay_timeline_chunks;");
                if (bytes.Read()) chunkBytes = bytes.Int64(0);
            }
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (TableExistsV11(connection, "replay_export_jobs"))
                    connection.Execute("DELETE FROM replay_export_jobs WHERE record_id IN (SELECT record_id FROM battle_records WHERE replay_protocol=10);");
                connection.Execute("UPDATE battle_records SET replay_protocol=11, replay_state='SummaryOnly', compressed_bytes=0 WHERE replay_protocol=10;");
                connection.Execute("DROP TABLE IF EXISTS replay_asset_refs;");
                connection.Execute("DROP TABLE IF EXISTS replay_timeline_chunks;");
                connection.Execute("DROP TABLE IF EXISTS replay_documents;");
                if (TableExistsV11(connection, "replay_assets")) connection.Execute("DELETE FROM replay_assets;");
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
            using var migration = connection.Prepare(
                "INSERT OR REPLACE INTO replay_migrations(migration_id, state, scanned_utc, applied_utc, report_path, report_sha256, record_count, chunk_bytes) "
                + "VALUES('replay-v10-to-v11-native-cutover', 'Applied', ?, ?, '', '', ?, ?);");
            var now = DateTime.UtcNow.ToString("O");
            migration.Bind(1, now);
            migration.Bind(2, now);
            migration.Bind(3, recordCount);
            migration.Bind(4, chunkBytes);
            migration.Execute();
        }
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_documents("
                           + "record_id TEXT PRIMARY KEY, document_version INTEGER NOT NULL CHECK(document_version=11), "
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
    }

    private void MigrateEmptyBootstrapV11Documents(WinSqliteConnection connection)
    {
        const string migrationId = "replay-v11-empty-bootstrap-to-materialized-baseline";
        using (var applied = connection.Prepare(
                   "SELECT 1 FROM replay_migrations WHERE migration_id=? AND state='Applied' LIMIT 1;"))
        {
            applied.Bind(1, migrationId);
            if (applied.Read()) return;
        }

        var plans = new List<MaterializedBaselineMigrationPlan>();
        using (var query = connection.Prepare(
                   "SELECT d.record_id FROM replay_documents d "
                   + "JOIN battle_records b ON b.record_id=d.record_id "
                   + "WHERE d.document_version=11 AND d.document_state='Ready' AND b.replay_state='Ready' "
                   + "ORDER BY b.sequence;"))
        {
            while (query.Read())
            {
                var recordId = query.Text(0);
                try
                {
                    var document = LoadStoredV11Document(connection, recordId);
                    if (ReplayPlayableBootstrapContractV11.ValidateState(document.InitialState).Count == 0)
                    {
                        var validation = ReplayDocumentValidatorV11.Validate(document);
                        if (!validation.IsValid)
                            plans.Add(MaterializedBaselineMigrationPlan.Rejected(recordId, validation.Message));
                        continue;
                    }
                    var migration = ReplayMaterializedBaselineMigrationV11.Rebase(document);
                    plans.Add(migration.Success && migration.Changed && migration.Document != null
                        ? MaterializedBaselineMigrationPlan.Migrated(recordId, migration)
                        : MaterializedBaselineMigrationPlan.Rejected(recordId, migration.Message));
                }
                catch (Exception ex)
                {
                    plans.Add(MaterializedBaselineMigrationPlan.Corrupt(recordId, ex.Message));
                }
            }
        }

        var unreferencedFiles = new List<string>();
        connection.Execute("BEGIN IMMEDIATE;");
        try
        {
            foreach (var plan in plans)
            {
                if (plan.Kind == MaterializedBaselineMigrationKind.Migrated && plan.Document != null)
                    ApplyMaterializedBaselineMigration(connection, plan, migrationId);
                else
                    ReclassifyUnplayableBootstrap(connection, plan, migrationId);
            }

            using (var unreferenced = connection.Prepare(
                       "SELECT file_path FROM replay_assets WHERE NOT EXISTS("
                       + "SELECT 1 FROM replay_asset_refs r WHERE r.asset_sha256=replay_assets.asset_sha256);"))
            {
                while (unreferenced.Read()) unreferencedFiles.Add(ResolveStoredPath(unreferenced.Text(0)));
            }
            connection.Execute(
                "DELETE FROM replay_assets WHERE NOT EXISTS(SELECT 1 FROM replay_asset_refs r "
                + "WHERE r.asset_sha256=replay_assets.asset_sha256);");

            using (var migration = connection.Prepare(
                       "INSERT OR REPLACE INTO replay_migrations(migration_id, state, scanned_utc, applied_utc, "
                       + "report_path, report_sha256, record_count, chunk_bytes) "
                       + "VALUES(?, 'Applied', ?, ?, '', '', ?, ?);"))
            {
                var now = DateTime.UtcNow.ToString("O");
                migration.Bind(1, migrationId);
                migration.Bind(2, now);
                migration.Bind(3, now);
                migration.Bind(4, plans.Count);
                migration.Bind(5, plans.Where(value => value.Document != null)
                    .Sum(value => value.DocumentBytes));
                migration.Execute();
            }
            connection.Execute("COMMIT;");
        }
        catch
        {
            TryRollback(connection);
            throw;
        }

        foreach (var path in unreferencedFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path)) AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, path);
            }
            catch
            {
                // ReconcileV11Files quarantines any exact unregistered file that survives cleanup.
            }
        }
        if (plans.Count > 0)
        {
            AuraToolsLog.Info(
                "[MatchRecords] materialized-baseline migration applied: migrated="
                + plans.Count(value => value.Kind == MaterializedBaselineMigrationKind.Migrated)
                + ", rejected="
                + plans.Count(value => value.Kind == MaterializedBaselineMigrationKind.Rejected)
                + ", corrupt="
                + plans.Count(value => value.Kind == MaterializedBaselineMigrationKind.Corrupt)
                + ", removedAssets="
                + unreferencedFiles.Count
                + ".");
        }
    }

    private static ReplayDocumentV11 LoadStoredV11Document(WinSqliteConnection connection, string recordId)
    {
        ReplayDocumentV11 document;
        string storedDocumentHash;
        using (var query = connection.Prepare(
                   "SELECT document_payload, document_sha256 FROM replay_documents WHERE record_id=? LIMIT 1;"))
        {
            query.Bind(1, recordId);
            if (!query.Read()) throw new InvalidDataException("Replay document disappeared during migration: " + recordId);
            document = ReplayPayloadV11.Decode<ReplayDocumentV11>(query.Blob(0));
            storedDocumentHash = query.Text(1);
        }

        var chunks = new List<ReplayTimelineChunkV11>();
        using (var query = connection.Prepare(
                   "SELECT chunk_index, first_sequence, last_sequence, first_time_ticks, last_time_ticks, sha256, payload "
                   + "FROM replay_timeline_chunks WHERE record_id=? ORDER BY chunk_index;"))
        {
            query.Bind(1, recordId);
            while (query.Read())
            {
                chunks.Add(new ReplayTimelineChunkV11
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
        document.Events = ReplayTimelineChunkerV11.Decode(chunks).ToList();
        var actualDocumentHash = ReplayCanonicalJsonV11.DocumentHash(document);
        if (!string.Equals(actualDocumentHash, document.Header.DocumentSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actualDocumentHash, storedDocumentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Stored replay document hash is invalid before migration: " + recordId);
        }
        return document;
    }

    private void MigrateReplayCardPresentationV11Documents(WinSqliteConnection connection)
    {
        const string migrationId = "replay-v11-card-presentation-empty-tag";
        using (var applied = connection.Prepare(
                   "SELECT 1 FROM replay_migrations WHERE migration_id=? AND state='Applied' LIMIT 1;"))
        {
            applied.Bind(1, migrationId);
            if (applied.Read()) return;
        }

        var plans = new List<CardPresentationMigrationPlan>();
        using (var query = connection.Prepare(
                   "SELECT d.record_id FROM replay_documents d "
                   + "JOIN battle_records b ON b.record_id=d.record_id "
                   + "WHERE d.document_version=11 AND d.document_state='Ready' AND b.replay_state='Ready' "
                   + "ORDER BY b.sequence;"))
        {
            while (query.Read())
            {
                var recordId = query.Text(0);
                try
                {
                    var document = LoadStoredV11Document(connection, recordId);
                    var repaired = ReplayCardPresentationContractV11.NormalizeDocument(document);
                    var validation = repaired > 0
                        ? ReplayDocumentFinalizerV11.FinalizeAndValidate(document)
                        : ReplayDocumentValidatorV11.Validate(document);
                    plans.Add(validation.IsValid
                        ? CardPresentationMigrationPlan.Ready(recordId, document, repaired)
                        : CardPresentationMigrationPlan.Rejected(recordId, validation.Message));
                }
                catch (Exception ex)
                {
                    plans.Add(CardPresentationMigrationPlan.Corrupt(recordId, ex.Message));
                }
            }
        }

        connection.Execute("BEGIN IMMEDIATE;");
        try
        {
            foreach (var plan in plans)
            {
                if (plan.Kind == CardPresentationMigrationKind.Ready && plan.Document != null)
                {
                    if (plan.RepairedCards > 0)
                        ApplyCardPresentationMigration(connection, plan, migrationId);
                }
                else
                {
                    ReclassifyCardPresentationDocument(connection, plan, migrationId);
                }
            }

            using var migration = connection.Prepare(
                "INSERT OR REPLACE INTO replay_migrations(migration_id, state, scanned_utc, applied_utc, "
                + "report_path, report_sha256, record_count, chunk_bytes) "
                + "VALUES(?, 'Applied', ?, ?, '', '', ?, ?);");
            var now = DateTime.UtcNow.ToString("O");
            migration.Bind(1, migrationId);
            migration.Bind(2, now);
            migration.Bind(3, now);
            migration.Bind(4, plans.Count(value => value.RepairedCards > 0));
            migration.Bind(5, plans.Sum(value => value.DocumentBytes));
            migration.Execute();
            connection.Execute("COMMIT;");
        }
        catch
        {
            TryRollback(connection);
            throw;
        }

        var repairedRecords = plans.Count(value => value.RepairedCards > 0);
        if (repairedRecords > 0 || plans.Any(value => value.Kind != CardPresentationMigrationKind.Ready))
        {
            AuraToolsLog.Info(
                "[MatchRecords] card-presentation migration applied: repairedRecords="
                + repairedRecords
                + ", repairedCards="
                + plans.Sum(value => value.RepairedCards)
                + ", rejected="
                + plans.Count(value => value.Kind == CardPresentationMigrationKind.Rejected)
                + ", corrupt="
                + plans.Count(value => value.Kind == CardPresentationMigrationKind.Corrupt)
                + ".");
        }
    }

    private static void ApplyCardPresentationMigration(
        WinSqliteConnection connection,
        CardPresentationMigrationPlan plan,
        string migrationId)
    {
        var document = plan.Document!;
        var chunks = ReplayTimelineChunkerV11.Build(document.Events);
        var skeleton = CloneWithoutTransientPayload(document);
        skeleton.Events.Clear();
        var documentPayload = ReplayPayloadV11.Encode(skeleton);
        var compressedBytes = chunks.Sum(value => (long)value.Payload.Length) + documentPayload.LongLength;
        plan.DocumentBytes = compressedBytes;

        using (var delete = connection.Prepare("DELETE FROM replay_timeline_chunks WHERE record_id=?;"))
        {
            delete.Bind(1, plan.RecordId);
            delete.Execute();
        }
        foreach (var chunk in chunks)
        {
            using var insert = connection.Prepare(
                "INSERT INTO replay_timeline_chunks(record_id, chunk_index, first_sequence, last_sequence, "
                + "first_time_ticks, last_time_ticks, sha256, payload) VALUES(?, ?, ?, ?, ?, ?, ?, ?);");
            insert.Bind(1, plan.RecordId);
            insert.Bind(2, chunk.ChunkIndex);
            insert.Bind(3, chunk.FirstSequence);
            insert.Bind(4, chunk.LastSequence);
            insert.Bind(5, chunk.FirstTimeTicks);
            insert.Bind(6, chunk.LastTimeTicks);
            insert.Bind(7, chunk.Sha256);
            insert.Bind(8, chunk.Payload);
            insert.Execute();
        }

        using (var update = connection.Prepare(
                   "UPDATE replay_documents SET document_state='Ready', document_sha256=?, initial_state_sha256=?, "
                   + "final_state_sha256=?, event_chain_sha256=?, renderer_profile=?, event_count=?, checkpoint_count=?, "
                   + "attachment_count=?, compressed_bytes=?, document_payload=? WHERE record_id=?;"))
        {
            update.Bind(1, document.Header.DocumentSha256);
            update.Bind(2, document.Header.InitialLogicalStateSha256);
            update.Bind(3, document.Header.FinalLogicalStateSha256);
            update.Bind(4, document.Header.FinalEventChainSha256);
            update.Bind(5, document.Header.RenderProfileId ?? "");
            update.Bind(6, document.Events.Count);
            update.Bind(7, document.Checkpoints.Count);
            update.Bind(8, document.Attachments.Count);
            update.Bind(9, compressedBytes);
            update.Bind(10, documentPayload);
            update.Bind(11, plan.RecordId);
            update.Execute();
        }

        var record = GetRecordForMigration(connection, plan.RecordId);
        record.EventCount = document.Events.Count;
        record.TurnCount = Math.Max(document.InitialState.TurnIndex,
            document.Events.Count == 0 ? 1 : document.Events.Max(value => value.TurnIndex));
        record.CompressedBytes = compressedBytes;
        record.ContentSha256 = document.Header.DocumentSha256;
        record.CaptureDiagnostics.Add(migrationId + ": repairedCards=" + plan.RepairedCards);
        using var recordUpdate = connection.Prepare(
            "UPDATE battle_records SET replay_state='Ready', event_count=?, turn_count=?, compressed_bytes=?, "
            + "metadata_payload=? WHERE record_id=?;");
        recordUpdate.Bind(1, record.EventCount);
        recordUpdate.Bind(2, record.TurnCount);
        recordUpdate.Bind(3, compressedBytes);
        recordUpdate.Bind(4, MatchReplayPayload.Encode(CreateMetadata(record)));
        recordUpdate.Bind(5, plan.RecordId);
        recordUpdate.Execute();
    }

    private static void ReclassifyCardPresentationDocument(
        WinSqliteConnection connection,
        CardPresentationMigrationPlan plan,
        string migrationId)
    {
        var record = GetRecordForMigration(connection, plan.RecordId);
        var state = plan.Kind == CardPresentationMigrationKind.Corrupt
            ? MatchReplayStates.Corrupt
            : MatchReplayStates.SummaryOnly;
        var documentState = plan.Kind == CardPresentationMigrationKind.Corrupt ? "Corrupt" : "Rejected";
        record.CaptureDiagnostics.Add(migrationId + ": " + plan.Message);
        using (var update = connection.Prepare(
                   "UPDATE battle_records SET replay_state=?, metadata_payload=? WHERE record_id=?;"))
        {
            update.Bind(1, state);
            update.Bind(2, MatchReplayPayload.Encode(CreateMetadata(record)));
            update.Bind(3, plan.RecordId);
            update.Execute();
        }
        using var document = connection.Prepare(
            "UPDATE replay_documents SET document_state=? WHERE record_id=?;");
        document.Bind(1, documentState);
        document.Bind(2, plan.RecordId);
        document.Execute();
    }

    private void ApplyMaterializedBaselineMigration(
        WinSqliteConnection connection,
        MaterializedBaselineMigrationPlan plan,
        string migrationId)
    {
        var document = plan.Document!;
        var chunks = ReplayTimelineChunkerV11.Build(document.Events);
        var skeleton = CloneWithoutTransientPayload(document);
        skeleton.Events.Clear();
        var documentPayload = ReplayPayloadV11.Encode(skeleton);
        var compressedBytes = chunks.Sum(value => (long)value.Payload.Length) + documentPayload.LongLength;
        plan.DocumentBytes = compressedBytes;

        using (var delete = connection.Prepare("DELETE FROM replay_timeline_chunks WHERE record_id=?;"))
        {
            delete.Bind(1, plan.RecordId);
            delete.Execute();
        }
        foreach (var chunk in chunks)
        {
            using var insert = connection.Prepare(
                "INSERT INTO replay_timeline_chunks(record_id, chunk_index, first_sequence, last_sequence, "
                + "first_time_ticks, last_time_ticks, sha256, payload) VALUES(?, ?, ?, ?, ?, ?, ?, ?);");
            insert.Bind(1, plan.RecordId);
            insert.Bind(2, chunk.ChunkIndex);
            insert.Bind(3, chunk.FirstSequence);
            insert.Bind(4, chunk.LastSequence);
            insert.Bind(5, chunk.FirstTimeTicks);
            insert.Bind(6, chunk.LastTimeTicks);
            insert.Bind(7, chunk.Sha256);
            insert.Bind(8, chunk.Payload);
            insert.Execute();
        }

        using (var delete = connection.Prepare("DELETE FROM replay_asset_refs WHERE record_id=?;"))
        {
            delete.Bind(1, plan.RecordId);
            delete.Execute();
        }
        foreach (var attachment in document.Attachments)
        {
            using var insert = connection.Prepare(
                "INSERT INTO replay_asset_refs(record_id, asset_sha256, usage, required) VALUES(?, ?, ?, ?);");
            insert.Bind(1, plan.RecordId);
            insert.Bind(2, attachment.Sha256);
            insert.Bind(3, attachment.Usage ?? "");
            insert.Bind(4, attachment.Required ? 1 : 0);
            insert.Execute();
        }

        using (var update = connection.Prepare(
                   "UPDATE replay_documents SET document_state='Ready', document_sha256=?, initial_state_sha256=?, "
                   + "final_state_sha256=?, event_chain_sha256=?, renderer_profile=?, event_count=?, checkpoint_count=?, "
                   + "attachment_count=?, compressed_bytes=?, document_payload=? WHERE record_id=?;"))
        {
            update.Bind(1, document.Header.DocumentSha256);
            update.Bind(2, document.Header.InitialLogicalStateSha256);
            update.Bind(3, document.Header.FinalLogicalStateSha256);
            update.Bind(4, document.Header.FinalEventChainSha256);
            update.Bind(5, document.Header.RenderProfileId ?? "");
            update.Bind(6, document.Events.Count);
            update.Bind(7, document.Checkpoints.Count);
            update.Bind(8, document.Attachments.Count);
            update.Bind(9, compressedBytes);
            update.Bind(10, documentPayload);
            update.Bind(11, plan.RecordId);
            update.Execute();
        }

        var record = GetRecordForMigration(connection, plan.RecordId);
        record.EventCount = document.Events.Count;
        record.TurnCount = Math.Max(document.InitialState.TurnIndex,
            document.Events.Count == 0 ? 1 : document.Events.Max(value => value.TurnIndex));
        record.CompressedBytes = compressedBytes;
        record.ContentSha256 = document.Header.DocumentSha256;
        record.RequiredCapabilities = document.Header.RequiredCapabilities.ToList();
        record.ContentDependencies = document.Content.Dependencies.Select(value => value.OwnerModId).ToList();
        record.CaptureDiagnostics.Add(
            migrationId + ": rebased from event " + plan.AnchorSequence
            + ", removed prelude events=" + plan.RemovedPreludeEvents
            + ", removed attachments=" + plan.RemovedAttachments);
        record.InitialState ??= new MatchReplayInitialState();
        record.InitialState.LevelId = document.Header.LevelId;
        record.InitialState.BackgroundScene = document.NativeBattle.BackgroundScene;
        record.InitialState.MapMode = document.NativeBattle.MapMode;
        record.InitialState.MapLevel = document.NativeBattle.MapLevel;
        record.InitialState.DiceJson = document.NativeBattle.DiceJson;
        record.InitialState.RoleQueue = (byte[])document.NativeBattle.RoleQueue.Clone();
        record.InitialState.TemporaryRoles = (byte[])document.NativeBattle.TemporaryRoles.Clone();
        record.InitialState.EnemyPositive = document.NativeBattle.EnemyPositive;
        record.InitialState.EnemyHp = document.NativeBattle.EnemyHp;
        record.InitialState.RoleTableJson = document.NativeBattle.RoleTableJson;
        record.InitialState.BaselineState = null;
        using var recordUpdate = connection.Prepare(
            "UPDATE battle_records SET replay_state='Ready', event_count=?, turn_count=?, compressed_bytes=?, "
            + "initial_payload=?, metadata_payload=? WHERE record_id=?;");
        recordUpdate.Bind(1, record.EventCount);
        recordUpdate.Bind(2, record.TurnCount);
        recordUpdate.Bind(3, compressedBytes);
        recordUpdate.Bind(4, MatchReplayPayload.Encode(record.InitialState));
        recordUpdate.Bind(5, MatchReplayPayload.Encode(CreateMetadata(record)));
        recordUpdate.Bind(6, plan.RecordId);
        recordUpdate.Execute();
    }

    private static void ReclassifyUnplayableBootstrap(
        WinSqliteConnection connection,
        MaterializedBaselineMigrationPlan plan,
        string migrationId)
    {
        var record = GetRecordForMigration(connection, plan.RecordId);
        var state = plan.Kind == MaterializedBaselineMigrationKind.Corrupt
            ? MatchReplayStates.Corrupt
            : MatchReplayStates.SummaryOnly;
        var documentState = plan.Kind == MaterializedBaselineMigrationKind.Corrupt ? "Corrupt" : "Rejected";
        record.CaptureDiagnostics.Add(migrationId + ": " + plan.Message);
        using (var update = connection.Prepare(
                   "UPDATE battle_records SET replay_state=?, metadata_payload=? WHERE record_id=?;"))
        {
            update.Bind(1, state);
            update.Bind(2, MatchReplayPayload.Encode(CreateMetadata(record)));
            update.Bind(3, plan.RecordId);
            update.Execute();
        }
        using var document = connection.Prepare(
            "UPDATE replay_documents SET document_state=? WHERE record_id=?;");
        document.Bind(1, documentState);
        document.Bind(2, plan.RecordId);
        document.Execute();
    }

    private static bool ReplayDocumentTableIsV11(WinSqliteConnection connection)
    {
        using var query = connection.Prepare(
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='replay_documents' LIMIT 1;");
        return query.Read() && query.Text(0).Replace(" ", "").Contains("document_version=11");
    }

    private void ReconcileV11Files(WinSqliteConnection connection)
    {
        var knownAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var query = connection.Prepare(
                   "SELECT asset_sha256, file_path, media_type, byte_length, sample_rate, channels, sample_frames "
                   + "FROM replay_assets;"))
        {
            while (query.Read())
            {
                var hash = query.Text(0);
                var path = ResolveStoredPath(query.Text(1));
                knownAssets.Add(Path.GetFullPath(path));
                var valid = File.Exists(path);
                if (valid && string.Equals(query.Text(2), "audio/wav", StringComparison.OrdinalIgnoreCase))
                {
                    valid = new FileInfo(path).Length == query.Int64(3)
                            && TryReadPcmWaveFileHeader(path, out var wave)
                            && wave.SampleRate == query.Int64(4)
                            && wave.Channels == query.Int64(5)
                            && wave.SampleFrames == query.Int64(6);
                }
                if (valid) continue;
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
            try { AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, full, target); } catch { }
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

    private static void DeleteReplayV11(WinSqliteConnection connection, string recordId)
    {
        if (!TableExistsV11(connection, "replay_documents")) return;
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
                if (File.Exists(candidate.Staging)) AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, candidate.Staging);
                AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, candidate.Path, candidate.Staging);
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
                try { if (File.Exists(pair.Staging)) AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, pair.Staging); } catch { }
            }
        }
        catch
        {
            foreach (var pair in moved)
            {
                try
                {
                    if (File.Exists(pair.Staging) && !File.Exists(pair.Original))
                        AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, pair.Staging, pair.Original);
                }
                catch
                {
                }
            }
            throw;
        }
    }

    private List<AttachmentMove> StageAttachments(ReplayDocumentV11 document)
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

            var transaction = AuraSharedFileStore.BeginWrite(
                AuraToolsIds.ModId,
                finalPath,
                overwrite: false);
            transaction.Stream.Write(attachment.Payload, 0, attachment.Payload.Length);
            result.Add(new AttachmentMove(transaction, finalPath));
        }

        return result;
    }

    private static void CommitAttachments(IEnumerable<AttachmentMove> moves)
    {
        foreach (var move in moves)
        {
            move.Transaction.Commit();
            move.Transaction.Dispose();
        }
    }

    private static void CleanupStaging(IEnumerable<AttachmentMove> moves)
    {
        foreach (var move in moves)
        {
            try { move.Transaction.Dispose(); } catch { }
        }
    }

    private string AttachmentDirectory => Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "Attachments");

    private string AttachmentPath(ReplayAttachmentV11 attachment)
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

    private static bool TableExistsV11(WinSqliteConnection connection, string table)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, table);
        return query.Read();
    }

    private static ReplayDocumentV11 CloneWithoutTransientPayload(ReplayDocumentV11 document)
    {
        var bytes = ReplayCanonicalJsonV11.SerializeUtf8(document);
        var clone = Newtonsoft.Json.JsonConvert.DeserializeObject<ReplayDocumentV11>(System.Text.Encoding.UTF8.GetString(bytes))
                    ?? throw new InvalidDataException("Replay Document v11 could not be cloned.");
        foreach (var attachment in clone.Attachments) attachment.Payload = Array.Empty<byte>();
        return clone;
    }

    private enum MaterializedBaselineMigrationKind
    {
        Migrated,
        Rejected,
        Corrupt
    }

    private enum CardPresentationMigrationKind
    {
        Ready,
        Rejected,
        Corrupt
    }

    private sealed class CardPresentationMigrationPlan
    {
        internal string RecordId { get; private set; } = "";
        internal CardPresentationMigrationKind Kind { get; private set; }
        internal ReplayDocumentV11? Document { get; private set; }
        internal int RepairedCards { get; private set; }
        internal long DocumentBytes { get; set; }
        internal string Message { get; private set; } = "";

        internal static CardPresentationMigrationPlan Ready(
            string recordId,
            ReplayDocumentV11 document,
            int repairedCards)
        {
            return new CardPresentationMigrationPlan
            {
                RecordId = recordId,
                Kind = CardPresentationMigrationKind.Ready,
                Document = document,
                RepairedCards = repairedCards
            };
        }

        internal static CardPresentationMigrationPlan Rejected(string recordId, string message)
        {
            return new CardPresentationMigrationPlan
            {
                RecordId = recordId,
                Kind = CardPresentationMigrationKind.Rejected,
                Message = string.IsNullOrWhiteSpace(message) ? "card presentation cannot be migrated" : message
            };
        }

        internal static CardPresentationMigrationPlan Corrupt(string recordId, string message)
        {
            return new CardPresentationMigrationPlan
            {
                RecordId = recordId,
                Kind = CardPresentationMigrationKind.Corrupt,
                Message = string.IsNullOrWhiteSpace(message) ? "card presentation document is corrupt" : message
            };
        }
    }

    private sealed class MaterializedBaselineMigrationPlan
    {
        internal string RecordId { get; private set; } = "";

        internal MaterializedBaselineMigrationKind Kind { get; private set; }

        internal ReplayDocumentV11? Document { get; private set; }

        internal long AnchorSequence { get; private set; }

        internal int RemovedPreludeEvents { get; private set; }

        internal int RemovedAttachments { get; private set; }

        internal long DocumentBytes { get; set; }

        internal string Message { get; private set; } = "";

        internal static MaterializedBaselineMigrationPlan Migrated(
            string recordId,
            ReplayMaterializedBaselineMigrationResultV11 result)
        {
            return new MaterializedBaselineMigrationPlan
            {
                RecordId = recordId,
                Kind = MaterializedBaselineMigrationKind.Migrated,
                Document = result.Document,
                AnchorSequence = result.AnchorSequence,
                RemovedPreludeEvents = result.RemovedPreludeEvents,
                RemovedAttachments = result.RemovedAttachments,
                Message = result.Message
            };
        }

        internal static MaterializedBaselineMigrationPlan Rejected(string recordId, string message)
        {
            return new MaterializedBaselineMigrationPlan
            {
                RecordId = recordId,
                Kind = MaterializedBaselineMigrationKind.Rejected,
                Message = string.IsNullOrWhiteSpace(message) ? "empty bootstrap cannot be migrated" : message
            };
        }

        internal static MaterializedBaselineMigrationPlan Corrupt(string recordId, string message)
        {
            return new MaterializedBaselineMigrationPlan
            {
                RecordId = recordId,
                Kind = MaterializedBaselineMigrationKind.Corrupt,
                Message = string.IsNullOrWhiteSpace(message) ? "empty bootstrap document is corrupt" : message
            };
        }
    }

    private readonly struct AttachmentMove
    {
        internal AttachmentMove(AuraSharedFileWriteTransaction transaction, string finalPath)
        {
            Transaction = transaction;
            FinalPath = finalPath;
        }

        internal AuraSharedFileWriteTransaction Transaction { get; }

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
