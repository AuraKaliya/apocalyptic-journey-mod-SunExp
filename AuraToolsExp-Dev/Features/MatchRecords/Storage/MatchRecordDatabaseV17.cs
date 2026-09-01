using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    private const string ReplayV17CutoverMigrationId =
        "replay-pre17-to-v17-native-presentation-cutover";

    internal bool SaveSummaryV17(MatchRecord record, MatchAnalysisReport? analysis, bool rejected = false)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.RecordId)) return false;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var existingCapture = Exists(connection, record.RecordId)
                                      && IsCaptureRecordV17(connection, record.RecordId);
                if (Exists(connection, record.RecordId) && !existingCapture)
                {
                    connection.Execute("ROLLBACK;");
                    return false;
                }
                record.ReplayProtocol = ReplayProtocolV17.DocumentVersion;
                record.ReplayState = rejected ? MatchReplayStates.Rejected : MatchReplayStates.SummaryOnly;
                if (existingCapture) UpdateRecordV17(connection, record, compressedBytes: 0);
                else InsertRecordV17(connection, record, compressedBytes: 0);
                if (analysis != null)
                {
                    analysis.RecordId = record.RecordId;
                    SaveAnalysis(connection, analysis);
                }
                if (existingCapture) DeleteCaptureSessionV17(connection, record.RecordId);
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

    internal bool SaveV17(
        MatchRecord record,
        ReplayDocumentEnvelopeV17 envelope,
        MatchAnalysisReport? analysis = null,
        int chunkTargetBytes = ReplayJournalChunkerV17.DefaultTargetBytes)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (envelope?.Document == null) throw new ArgumentNullException(nameof(envelope));
        var validation = ReplayDocumentValidatorV17.Validate(envelope);
        if (!validation.IsValid)
            throw new InvalidDataException("Replay Document v17 is invalid: " + validation.Message);
        var document = envelope.Document;
        if (!string.Equals(record.RecordId, document.Header.RecordId, StringComparison.Ordinal))
            throw new InvalidDataException("Replay record id does not match its v17 document.");

        var truthChunks = ReplayJournalChunkerV17.Build(
            ReplayJournalLanesV17.Truth,
            document.TruthEvents,
            chunkTargetBytes);
        var presentationChunks = ReplayJournalChunkerV17.Build(
            ReplayJournalLanesV17.Presentation,
            document.PresentationEvents,
            chunkTargetBytes);
        var skeleton = CloneV17WithoutTransientPayload(envelope);
        skeleton.Document.TruthEvents.Clear();
        skeleton.Document.PresentationEvents.Clear();
        skeleton.Document.TruthCheckpoints.Clear();
        skeleton.Document.PresentationCheckpoints.Clear();
        var documentPayload = ReplayPayloadV17.Encode(skeleton);
        var attachmentMoves = new List<AttachmentMove>();

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("PRAGMA foreign_keys=ON;");
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var existingCapture = Exists(connection, record.RecordId)
                                      && IsCaptureRecordV17(connection, record.RecordId);
                if (Exists(connection, record.RecordId) && !existingCapture)
                {
                    connection.Execute("ROLLBACK;");
                    CleanupStaging(attachmentMoves);
                    return false;
                }
                attachmentMoves = StageAttachmentsV17(document);

                record.ReplayProtocol = ReplayProtocolV17.DocumentVersion;
                record.ReplayState = MatchReplayStates.Ready;
                record.LevelId = document.Header.LevelId;
                record.BattleTitle = document.Header.BattleTitle;
                record.EventCount = document.TruthEvents.Count + document.PresentationEvents.Count;
                record.TurnCount = Math.Max(record.TurnCount, document.Header.TruthCheckpointCount == 0
                    ? document.InitialState.RoundSequence
                    : document.TruthCheckpoints.Max(item => item.State.RoundSequence));
                record.CompressedBytes = documentPayload.LongLength
                                         + truthChunks.Sum(item => (long)item.Payload.Length)
                                         + presentationChunks.Sum(item => (long)item.Payload.Length)
                                         + document.TruthCheckpoints.Sum(item => (long)ReplayPayloadV17.Encode(item).Length)
                                         + document.PresentationCheckpoints.Sum(item => (long)ReplayPayloadV17.Encode(item).Length);
                record.ContentSha256 = envelope.DeclaredDocumentRoot;
                record.ModFingerprint = "";
                record.RequiredCapabilities = document.Header.RequiredCapabilities.ToList();
                record.OptionalCapabilities = document.Header.OptionalCapabilities.ToList();
                record.ContentDependencies = document.Presentation.Entities
                    .Select(item => item.Provenance.OwnerModId)
                    .Concat(document.Presentation.Cards.Select(item => item.Provenance.OwnerModId))
                    .Concat(document.Presentation.Buffs.Select(item => item.Provenance.OwnerModId))
                    .Concat(document.Presentation.Intents.Select(item => item.Provenance.OwnerModId))
                    .Concat(document.Presentation.Modules.Select(item => item.OwnerModId))
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToList();
                if (existingCapture) UpdateRecordV17(connection, record, record.CompressedBytes);
                else InsertRecordV17(connection, record, record.CompressedBytes);

                using (var insert = connection.Prepare(
                           "INSERT INTO replay_documents(record_id, document_version, document_state, document_root, truth_root, "
                           + "presentation_root, initial_state_sha256, final_state_sha256, presentation_abi, truth_event_count, "
                           + "presentation_event_count, truth_checkpoint_count, presentation_checkpoint_count, asset_count, "
                           + "compressed_bytes, document_payload) VALUES(?, 17, 'Ready', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, record.RecordId);
                    insert.Bind(2, envelope.DeclaredDocumentRoot);
                    insert.Bind(3, document.Header.TruthRoot);
                    insert.Bind(4, document.Header.PresentationRoot);
                    insert.Bind(5, document.Header.InitialVisibleStateSha256);
                    insert.Bind(6, document.Header.FinalVisibleStateSha256);
                    insert.Bind(7, document.Header.PresentationAbi);
                    insert.Bind(8, document.TruthEvents.Count);
                    insert.Bind(9, document.PresentationEvents.Count);
                    insert.Bind(10, document.TruthCheckpoints.Count);
                    insert.Bind(11, document.PresentationCheckpoints.Count);
                    insert.Bind(12, document.Assets.Count);
                    insert.Bind(13, record.CompressedBytes);
                    insert.Bind(14, documentPayload);
                    insert.Execute();
                }
                InsertChunksV17(connection, "replay_truth_chunks", record.RecordId, truthChunks);
                InsertChunksV17(connection, "replay_presentation_chunks", record.RecordId, presentationChunks);
                InsertCheckpointsV17(connection, record.RecordId, document);
                InsertAssetsV17(connection, record.RecordId, document);
                if (analysis != null)
                {
                    analysis.RecordId = record.RecordId;
                    SaveAnalysis(connection, analysis);
                }
                if (existingCapture) DeleteCaptureSessionV17(connection, record.RecordId);
                CommitAttachments(attachmentMoves);
                connection.Execute("COMMIT;");
                return true;
            }
            catch
            {
                TryRollback(connection);
                CleanupStaging(attachmentMoves);
                CleanupCommittedAttachments(attachmentMoves);
                throw;
            }
        }
    }

    internal ReplayDocumentEnvelopeV17? LoadV17(string recordId, bool loadAssetPayloads = false)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            ReplayDocumentEnvelopeV17 envelope;
            using (var query = connection.Prepare(
                       "SELECT document_version, document_state, document_root, document_payload FROM replay_documents "
                       + "WHERE record_id=? LIMIT 1;"))
            {
                query.Bind(1, recordId.Trim());
                if (!query.Read()) return null;
                if (query.Int64(0) != ReplayProtocolV17.DocumentVersion
                    || !string.Equals(query.Text(1), MatchReplayStates.Ready, StringComparison.Ordinal))
                    return null;
                envelope = ReplayPayloadV17.Decode<ReplayDocumentEnvelopeV17>(query.Blob(3));
                if (!string.Equals(envelope.DeclaredDocumentRoot, query.Text(2), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Stored Replay Document v17 root does not match its envelope.");
            }
            envelope.Document.TruthEvents = LoadChunksV17(
                connection,
                "replay_truth_chunks",
                recordId,
                ReplayJournalLanesV17.Truth).ToList();
            envelope.Document.PresentationEvents = LoadChunksV17(
                connection,
                "replay_presentation_chunks",
                recordId,
                ReplayJournalLanesV17.Presentation).ToList();
            LoadCheckpointsV17(connection, recordId, envelope.Document);
            if (loadAssetPayloads)
            {
                foreach (var asset in envelope.Document.Assets)
                {
                    var path = AttachmentPathV17(asset);
                    if (File.Exists(path)) asset.Payload = File.ReadAllBytes(path);
                }
            }
            var validation = ReplayDocumentValidatorV17.Validate(envelope);
            if (!validation.IsValid)
                throw new InvalidDataException("Stored Replay Document v17 is invalid: " + validation.Message);
            return envelope;
        }
    }

    private void InsertRecordV17(WinSqliteConnection connection, MatchRecord record, long compressedBytes)
    {
        using var insert = connection.Prepare(
            "INSERT INTO battle_records(record_id, adventure_id, session_id, level_id, result, started_utc, ended_utc, "
            + "collection_kind, replay_state, replay_protocol, game_build, tool_build, mod_fingerprint, event_count, "
            + "turn_count, compressed_bytes, statistics_payload, initial_payload, metadata_payload) "
            + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);");
        insert.Bind(1, record.RecordId.Trim());
        insert.Bind(2, record.AdventureId ?? "");
        insert.Bind(3, record.SessionId ?? "");
        insert.Bind(4, record.LevelId ?? "");
        insert.Bind(5, record.Result ?? "");
        insert.Bind(6, record.StartedUtc ?? "");
        insert.Bind(7, record.EndedUtc ?? "");
        insert.Bind(8, NormalizeCollection(record.Collection));
        insert.Bind(9, NormalizeReplayState(record.ReplayState));
        insert.Bind(10, ReplayProtocolV17.DocumentVersion);
        insert.Bind(11, record.GameBuild ?? "");
        insert.Bind(12, record.ToolBuild ?? "");
        insert.Bind(13, "");
        insert.Bind(14, Math.Max(0, record.EventCount));
        insert.Bind(15, Math.Max(0, record.TurnCount));
        insert.Bind(16, Math.Max(0, compressedBytes));
        insert.Bind(17, MatchReplayPayload.Encode(record.StatisticsJson ?? ""));
        insert.Bind(18, MatchReplayPayload.Encode(record.InitialState ?? new MatchReplayInitialState()));
        insert.Bind(19, MatchReplayPayload.Encode(CreateMetadata(record)));
        insert.Execute();
    }

    private void UpdateRecordV17(WinSqliteConnection connection, MatchRecord record, long compressedBytes)
    {
        using var update = connection.Prepare(
            "UPDATE battle_records SET adventure_id=?, session_id=?, level_id=?, result=?, started_utc=?, ended_utc=?, "
            + "collection_kind=?, replay_state=?, replay_protocol=?, game_build=?, tool_build=?, mod_fingerprint='', "
            + "event_count=?, turn_count=?, compressed_bytes=?, statistics_payload=?, initial_payload=?, metadata_payload=? "
            + "WHERE record_id=?;");
        update.Bind(1, record.AdventureId ?? "");
        update.Bind(2, record.SessionId ?? "");
        update.Bind(3, record.LevelId ?? "");
        update.Bind(4, record.Result ?? "");
        update.Bind(5, record.StartedUtc ?? "");
        update.Bind(6, record.EndedUtc ?? "");
        update.Bind(7, NormalizeCollection(record.Collection));
        update.Bind(8, NormalizeReplayState(record.ReplayState));
        update.Bind(9, ReplayProtocolV17.DocumentVersion);
        update.Bind(10, record.GameBuild ?? "");
        update.Bind(11, record.ToolBuild ?? "");
        update.Bind(12, Math.Max(0, record.EventCount));
        update.Bind(13, Math.Max(0, record.TurnCount));
        update.Bind(14, Math.Max(0, compressedBytes));
        update.Bind(15, MatchReplayPayload.Encode(record.StatisticsJson ?? ""));
        update.Bind(16, MatchReplayPayload.Encode(record.InitialState ?? new MatchReplayInitialState()));
        update.Bind(17, MatchReplayPayload.Encode(CreateMetadata(record)));
        update.Bind(18, record.RecordId.Trim());
        update.Execute();
    }

    private static bool IsCaptureRecordV17(WinSqliteConnection connection, string recordId)
    {
        using var query = connection.Prepare(
            "SELECT 1 FROM replay_capture_sessions WHERE record_id=? LIMIT 1;");
        query.Bind(1, recordId);
        return query.Read();
    }

    private static void DeleteCaptureSessionV17(WinSqliteConnection connection, string recordId)
    {
        using var delete = connection.Prepare("DELETE FROM replay_capture_sessions WHERE record_id=?;");
        delete.Bind(1, recordId);
        delete.Execute();
    }

    private static void InsertChunksV17(
        WinSqliteConnection connection,
        string table,
        string recordId,
        IEnumerable<ReplayJournalChunkV17> chunks)
    {
        foreach (var chunk in chunks)
        {
            using var insert = connection.Prepare(
                "INSERT INTO " + table + "(record_id, chunk_index, first_sequence, last_sequence, first_time_ticks, "
                + "last_time_ticks, previous_chunk_sha256, sha256, payload) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?);");
            insert.Bind(1, recordId);
            insert.Bind(2, chunk.ChunkIndex);
            insert.Bind(3, chunk.FirstSequence);
            insert.Bind(4, chunk.LastSequence);
            insert.Bind(5, chunk.FirstTimeTicks);
            insert.Bind(6, chunk.LastTimeTicks);
            insert.Bind(7, chunk.PreviousChunkSha256);
            insert.Bind(8, chunk.Sha256);
            insert.Bind(9, chunk.Payload);
            insert.Execute();
        }
    }

    private static IReadOnlyList<ReplayJournalEventV17> LoadChunksV17(
        WinSqliteConnection connection,
        string table,
        string recordId,
        string lane)
    {
        var chunks = new List<ReplayJournalChunkV17>();
        using var query = connection.Prepare(
            "SELECT chunk_index, first_sequence, last_sequence, first_time_ticks, last_time_ticks, previous_chunk_sha256, "
            + "sha256, payload FROM " + table + " WHERE record_id=? ORDER BY chunk_index;");
        query.Bind(1, recordId);
        while (query.Read())
            chunks.Add(new ReplayJournalChunkV17
            {
                Lane = lane,
                ChunkIndex = (int)query.Int64(0),
                FirstSequence = query.Int64(1),
                LastSequence = query.Int64(2),
                FirstTimeTicks = query.Int64(3),
                LastTimeTicks = query.Int64(4),
                PreviousChunkSha256 = query.Text(5),
                Sha256 = query.Text(6),
                Payload = query.Blob(7)
            });
        return ReplayJournalChunkerV17.Decode(lane, chunks);
    }

    private static void InsertCheckpointsV17(
        WinSqliteConnection connection,
        string recordId,
        ReplayDocumentV17 document)
    {
        foreach (var checkpoint in document.TruthCheckpoints)
        {
            using var insert = connection.Prepare(
                "INSERT INTO replay_truth_checkpoints(record_id, event_sequence, time_ticks, sha256, payload) VALUES(?, ?, ?, ?, ?);");
            insert.Bind(1, recordId);
            insert.Bind(2, checkpoint.EventSequence);
            insert.Bind(3, checkpoint.TimeTicks);
            insert.Bind(4, checkpoint.CheckpointSha256);
            insert.Bind(5, ReplayPayloadV17.Encode(checkpoint));
            insert.Execute();
        }
        foreach (var checkpoint in document.PresentationCheckpoints)
        {
            using var insert = connection.Prepare(
                "INSERT INTO replay_presentation_checkpoints(record_id, event_sequence, time_ticks, sha256, payload) VALUES(?, ?, ?, ?, ?);");
            insert.Bind(1, recordId);
            insert.Bind(2, checkpoint.EventSequence);
            insert.Bind(3, checkpoint.TimeTicks);
            insert.Bind(4, checkpoint.CheckpointSha256);
            insert.Bind(5, ReplayPayloadV17.Encode(checkpoint));
            insert.Execute();
        }
    }

    private static void LoadCheckpointsV17(
        WinSqliteConnection connection,
        string recordId,
        ReplayDocumentV17 document)
    {
        document.TruthCheckpoints.Clear();
        using (var query = connection.Prepare(
                   "SELECT sha256, payload FROM replay_truth_checkpoints WHERE record_id=? ORDER BY event_sequence;"))
        {
            query.Bind(1, recordId);
            while (query.Read())
            {
                var value = ReplayPayloadV17.Decode<ReplayTruthCheckpointV17>(query.Blob(1));
                if (!string.Equals(value.CheckpointSha256, query.Text(0), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Stored replay truth checkpoint hash mismatch.");
                document.TruthCheckpoints.Add(value);
            }
        }
        document.PresentationCheckpoints.Clear();
        using var presentation = connection.Prepare(
            "SELECT sha256, payload FROM replay_presentation_checkpoints WHERE record_id=? ORDER BY event_sequence;");
        presentation.Bind(1, recordId);
        while (presentation.Read())
        {
            var value = ReplayPayloadV17.Decode<ReplayPresentationCheckpointV17>(presentation.Blob(1));
            if (!string.Equals(value.CheckpointSha256, presentation.Text(0), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Stored replay presentation checkpoint hash mismatch.");
            document.PresentationCheckpoints.Add(value);
        }
    }

    private void InsertAssetsV17(WinSqliteConnection connection, string recordId, ReplayDocumentV17 document)
    {
        foreach (var asset in document.Assets)
        {
            var finalPath = AttachmentPathV17(asset);
            using (var insert = connection.Prepare(
                       "INSERT OR IGNORE INTO replay_assets(asset_sha256, media_type, extension, file_path, byte_length, width, height, "
                       + "sample_rate, channels, sample_frames) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
            {
                insert.Bind(1, asset.Sha256);
                insert.Bind(2, asset.MediaType ?? "");
                insert.Bind(3, asset.Extension ?? "");
                insert.Bind(4, ToStoredPath(finalPath));
                insert.Bind(5, asset.ByteLength);
                insert.Bind(6, asset.Width);
                insert.Bind(7, asset.Height);
                insert.Bind(8, asset.SampleRate);
                insert.Bind(9, asset.Channels);
                insert.Bind(10, asset.SampleFrames);
                insert.Execute();
            }
            using var reference = connection.Prepare(
                "INSERT OR IGNORE INTO replay_asset_refs(record_id, asset_sha256, usage, required) VALUES(?, ?, ?, ?);");
            reference.Bind(1, recordId);
            reference.Bind(2, asset.Sha256);
            reference.Bind(3, asset.Usage ?? "");
            reference.Bind(4, asset.Required ? 1 : 0);
            reference.Execute();
        }
    }

    private List<AttachmentMove> StageAttachmentsV17(ReplayDocumentV17 document)
    {
        var result = new List<AttachmentMove>();
        Directory.CreateDirectory(AttachmentDirectory);
        try
        {
            foreach (var asset in document.Assets)
            {
                var finalPath = AttachmentPathV17(asset);
                if (File.Exists(finalPath))
                {
                    if (!string.Equals(FileSha256(finalPath), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Existing replay asset hash mismatch: " + asset.Sha256);
                    continue;
                }
                if (asset.Payload == null
                    || asset.Payload.LongLength != asset.ByteLength
                    || !string.Equals(ReplayCanonicalJsonV17.Sha256(asset.Payload), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Replay asset payload is missing or invalid: " + asset.Sha256);
                var transaction = AuraSharedFileStore.BeginWrite(AuraToolsIds.ModId, finalPath, overwrite: false);
                transaction.Stream.Write(asset.Payload, 0, asset.Payload.Length);
                result.Add(new AttachmentMove(transaction, finalPath));
            }
            return result;
        }
        catch
        {
            CleanupStaging(result);
            throw;
        }
    }

    private string AttachmentPathV17(ReplayAssetV17 asset)
    {
        return Path.Combine(AttachmentDirectory, asset.Sha256.ToLowerInvariant() + NormalizeExtension(asset.Extension));
    }

    private static ReplayDocumentEnvelopeV17 CloneV17WithoutTransientPayload(ReplayDocumentEnvelopeV17 envelope)
    {
        var clone = ReplayCanonicalJsonV17.Clone(envelope);
        foreach (var asset in clone.Document.Assets) asset.Payload = Array.Empty<byte>();
        return clone;
    }

    private static void EnsureV17Tables(WinSqliteConnection connection)
    {
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_documents("
                           + "record_id TEXT PRIMARY KEY, document_version INTEGER NOT NULL CHECK(document_version=17), "
                           + "document_state TEXT NOT NULL, document_root TEXT NOT NULL, truth_root TEXT NOT NULL, "
                           + "presentation_root TEXT NOT NULL, initial_state_sha256 TEXT NOT NULL, final_state_sha256 TEXT NOT NULL, "
                           + "presentation_abi TEXT NOT NULL, truth_event_count INTEGER NOT NULL, presentation_event_count INTEGER NOT NULL, "
                           + "truth_checkpoint_count INTEGER NOT NULL, presentation_checkpoint_count INTEGER NOT NULL, asset_count INTEGER NOT NULL, "
                           + "compressed_bytes INTEGER NOT NULL, document_payload BLOB NOT NULL, "
                           + "FOREIGN KEY(record_id) REFERENCES battle_records(record_id) ON DELETE CASCADE);");
        foreach (var table in new[] { "replay_truth_chunks", "replay_presentation_chunks" })
            connection.Execute("CREATE TABLE IF NOT EXISTS " + table + "(record_id TEXT NOT NULL, chunk_index INTEGER NOT NULL, "
                               + "first_sequence INTEGER NOT NULL, last_sequence INTEGER NOT NULL, first_time_ticks INTEGER NOT NULL, "
                               + "last_time_ticks INTEGER NOT NULL, previous_chunk_sha256 TEXT NOT NULL, sha256 TEXT NOT NULL, payload BLOB NOT NULL, "
                               + "PRIMARY KEY(record_id, chunk_index), FOREIGN KEY(record_id) REFERENCES replay_documents(record_id) ON DELETE CASCADE);");
        foreach (var table in new[] { "replay_truth_checkpoints", "replay_presentation_checkpoints" })
            connection.Execute("CREATE TABLE IF NOT EXISTS " + table + "(record_id TEXT NOT NULL, event_sequence INTEGER NOT NULL, "
                               + "time_ticks INTEGER NOT NULL, sha256 TEXT NOT NULL, payload BLOB NOT NULL, "
                               + "PRIMARY KEY(record_id, event_sequence), FOREIGN KEY(record_id) REFERENCES replay_documents(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_assets(asset_sha256 TEXT PRIMARY KEY, media_type TEXT NOT NULL, "
                           + "extension TEXT NOT NULL, file_path TEXT NOT NULL, byte_length INTEGER NOT NULL, width INTEGER NOT NULL, "
                           + "height INTEGER NOT NULL, sample_rate INTEGER NOT NULL, channels INTEGER NOT NULL, sample_frames INTEGER NOT NULL);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_asset_refs(record_id TEXT NOT NULL, asset_sha256 TEXT NOT NULL, "
                           + "usage TEXT NOT NULL, required INTEGER NOT NULL, PRIMARY KEY(record_id, asset_sha256, usage), "
                           + "FOREIGN KEY(record_id) REFERENCES replay_documents(record_id) ON DELETE CASCADE, "
                           + "FOREIGN KEY(asset_sha256) REFERENCES replay_assets(asset_sha256));");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_replay_asset_refs_hash ON replay_asset_refs(asset_sha256);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_capture_sessions(record_id TEXT PRIMARY KEY, "
                           + "capture_state TEXT NOT NULL, revision INTEGER NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, "
                           + "seed_payload BLOB NOT NULL, final_payload BLOB NOT NULL, final_sha256 TEXT NOT NULL, "
                           + "FOREIGN KEY(record_id) REFERENCES battle_records(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_capture_batches(record_id TEXT NOT NULL, batch_index INTEGER NOT NULL, "
                           + "first_sequence INTEGER NOT NULL, last_sequence INTEGER NOT NULL, batch_sha256 TEXT NOT NULL, payload BLOB NOT NULL, "
                           + "PRIMARY KEY(record_id, batch_index), "
                           + "FOREIGN KEY(record_id) REFERENCES replay_capture_sessions(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_replay_capture_sessions_state "
                           + "ON replay_capture_sessions(capture_state, updated_utc);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_export_jobs(job_id TEXT PRIMARY KEY, record_id TEXT NOT NULL, state TEXT NOT NULL, "
                           + "revision INTEGER NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, progress REAL NOT NULL, "
                           + "staging_path TEXT NOT NULL, target_path TEXT NOT NULL, output_sha256 TEXT NOT NULL, profile_id TEXT NOT NULL, "
                           + "message TEXT NOT NULL, error_code TEXT NOT NULL, cancel_requested INTEGER NOT NULL, attempt_count INTEGER NOT NULL, "
                           + "width INTEGER NOT NULL, height INTEGER NOT NULL, frames_per_second INTEGER NOT NULL, frame_count INTEGER NOT NULL, "
                           + "audio_sample_frames INTEGER NOT NULL, file_bytes INTEGER NOT NULL, estimated_bytes INTEGER NOT NULL, "
                           + "FOREIGN KEY(record_id) REFERENCES battle_records(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_replay_export_jobs_state ON replay_export_jobs(state, created_utc);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_migrations(migration_id TEXT PRIMARY KEY, state TEXT NOT NULL, "
                           + "scanned_utc TEXT NOT NULL, applied_utc TEXT NOT NULL, report_path TEXT NOT NULL, report_sha256 TEXT NOT NULL, "
                           + "record_count INTEGER NOT NULL, chunk_bytes INTEGER NOT NULL);");
    }

    private void ApplyPreV17Cutover(WinSqliteConnection connection)
    {
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_migrations(migration_id TEXT PRIMARY KEY, state TEXT NOT NULL, "
                           + "scanned_utc TEXT NOT NULL, applied_utc TEXT NOT NULL, report_path TEXT NOT NULL, report_sha256 TEXT NOT NULL, "
                           + "record_count INTEGER NOT NULL, chunk_bytes INTEGER NOT NULL);");
        using (var existing = connection.Prepare(
                   "SELECT state, report_path, report_sha256 FROM replay_migrations WHERE migration_id=? LIMIT 1;"))
        {
            existing.Bind(1, ReplayV17CutoverMigrationId);
            if (existing.Read())
            {
                var state = existing.Text(0);
                if (string.Equals(state, "Applied", StringComparison.Ordinal)) return;
                if (string.Equals(state, "PendingCleanup", StringComparison.Ordinal))
                {
                    CompleteCutoverAssetCleanupV17(
                        connection,
                        ResolveStoredPath(existing.Text(1)),
                        existing.Text(2));
                    MarkCutoverAppliedV17(connection);
                    return;
                }
                throw new InvalidDataException("Replay v17 cutover has an unknown migration state: " + state + ".");
            }
        }
        var hasV17Documents = false;
        if (TableExistsV17(connection, "replay_documents"))
        {
            using var current = connection.Prepare(
                "SELECT 1 FROM replay_documents d JOIN battle_records b ON b.record_id=d.record_id "
                + "WHERE b.replay_protocol>=17 AND d.document_version=17 LIMIT 1;");
            hasV17Documents = current.Read();
        }
        var assetPaths = new List<string>();
        if (TableExistsV17(connection, "replay_assets"))
        {
            var assetSql = hasV17Documents && TableExistsV17(connection, "replay_asset_refs")
                ? "SELECT DISTINCT a.file_path FROM replay_assets a JOIN replay_asset_refs r ON r.asset_sha256=a.asset_sha256 "
                  + "JOIN battle_records b ON b.record_id=r.record_id WHERE b.replay_protocol<17;"
                : "SELECT file_path FROM replay_assets;";
            using var assets = connection.Prepare(assetSql);
            while (assets.Read()) assetPaths.Add(ResolveStoredPath(assets.Text(0)));
        }
        var recordCount = 0L;
        var bytes = 0L;
        var retiredRecords = new List<ReplayV17CutoverRecord>();
        using (var totals = connection.Prepare(
                   "SELECT COUNT(*), COALESCE(SUM(compressed_bytes),0) FROM battle_records WHERE replay_protocol<17;"))
            if (totals.Read())
            {
                recordCount = totals.Int64(0);
                bytes = totals.Int64(1);
            }
        using (var records = connection.Prepare(
                   "SELECT record_id, replay_protocol, replay_state, compressed_bytes FROM battle_records "
                   + "WHERE replay_protocol<17 ORDER BY sequence;"))
            while (records.Read())
                retiredRecords.Add(new ReplayV17CutoverRecord
                {
                    RecordId = records.Text(0),
                    ReplayProtocol = (int)records.Int64(1),
                    PreviousReplayState = records.Text(2),
                    CompressedBytes = records.Int64(3)
                });
        var report = WriteCutoverReportV17(retiredRecords, assetPaths, bytes);
        connection.Execute("BEGIN IMMEDIATE;");
        try
        {
            using (var query = connection.Prepare("SELECT record_id, metadata_payload FROM battle_records WHERE replay_protocol<17;"))
            {
                var updates = new List<(string Id, byte[] Metadata)>();
                while (query.Read())
                {
                    var metadata = DecodeMetadata(query.Blob(1));
                    metadata.ContentSha256 = "";
                    metadata.RequiredCapabilities.Clear();
                    metadata.OptionalCapabilities.Clear();
                    metadata.ContentDependencies.Clear();
                    metadata.CaptureDiagnostics.Add("pre-v17 structured replay retired by " + ReplayV17CutoverMigrationId);
                    updates.Add((query.Text(0), MatchReplayPayload.Encode(metadata)));
                }
                foreach (var update in updates)
                {
                    using var statement = connection.Prepare(
                        "UPDATE battle_records SET replay_state='SummaryOnly', compressed_bytes=0, initial_payload=?, metadata_payload=? "
                        + "WHERE record_id=? AND replay_protocol<17;");
                    statement.Bind(1, MatchReplayPayload.Encode(new MatchReplayInitialState()));
                    statement.Bind(2, update.Metadata);
                    statement.Bind(3, update.Id);
                    statement.Execute();
                }
            }
            if (hasV17Documents)
            {
                connection.Execute("DELETE FROM replay_documents WHERE record_id IN "
                                   + "(SELECT record_id FROM battle_records WHERE replay_protocol<17);");
                if (TableExistsV17(connection, "replay_export_jobs"))
                    connection.Execute("DELETE FROM replay_export_jobs WHERE record_id IN "
                                        + "(SELECT record_id FROM battle_records WHERE replay_protocol<17);");
                if (TableExistsV17(connection, "replay_assets"))
                {
                    var deleteAssets = "DELETE FROM replay_assets WHERE NOT EXISTS "
                                       + "(SELECT 1 FROM replay_asset_refs r WHERE r.asset_sha256=replay_assets.asset_sha256)";
                    connection.Execute(deleteAssets + ";");
                }
                connection.Execute("DROP TABLE IF EXISTS replay_timeline_chunks;");
                connection.Execute("DROP TABLE IF EXISTS replay_chunks;");
                connection.Execute("DROP TABLE IF EXISTS replay_pov_asset_refs;");
                connection.Execute("DROP TABLE IF EXISTS replay_pov_sidecars;");
            }
            else
            {
                foreach (var table in new[]
                         {
                             "replay_pov_asset_refs", "replay_pov_sidecars",
                             "replay_truth_checkpoints", "replay_presentation_checkpoints",
                             "replay_truth_chunks", "replay_presentation_chunks", "replay_asset_refs",
                             "replay_capture_batches", "replay_capture_sessions", "replay_timeline_chunks",
                             "replay_documents", "replay_assets", "replay_export_jobs", "replay_chunks"
                         })
                    connection.Execute("DROP TABLE IF EXISTS " + table + ";");
            }
            using var migration = connection.Prepare(
                "INSERT OR REPLACE INTO replay_migrations(migration_id, state, scanned_utc, applied_utc, report_path, report_sha256, "
                + "record_count, chunk_bytes) VALUES(?, 'PendingCleanup', ?, '', ?, ?, ?, ?);");
            migration.Bind(1, ReplayV17CutoverMigrationId);
            migration.Bind(2, report.ScannedUtc);
            migration.Bind(3, ToStoredPath(report.Path));
            migration.Bind(4, report.Sha256);
            migration.Bind(5, recordCount);
            migration.Bind(6, bytes);
            migration.Execute();
            connection.Execute("COMMIT;");
        }
        catch
        {
            TryRollback(connection);
            throw;
        }
        CompleteCutoverAssetCleanupV17(connection, report.Path, report.Sha256);
        MarkCutoverAppliedV17(connection);
    }

    private void CompleteCutoverAssetCleanupV17(
        WinSqliteConnection connection,
        string reportPath,
        string expectedReportSha256)
    {
        if (!File.Exists(reportPath))
            throw new FileNotFoundException("Replay v17 cutover report is missing during resumable cleanup.", reportPath);
        var reportBytes = File.ReadAllBytes(reportPath);
        if (!string.Equals(
                ReplayCanonicalJsonV17.Sha256(reportBytes),
                expectedReportSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replay v17 cutover report changed before asset cleanup completed.");
        var report = ReplayCanonicalJsonV17.DeserializeStrict<ReplayV17CutoverReport>(
            Encoding.UTF8.GetString(reportBytes));
        if (!string.Equals(report.MigrationId, ReplayV17CutoverMigrationId, StringComparison.Ordinal))
            throw new InvalidDataException("Replay v17 cutover report has the wrong migration identity.");
        foreach (var storedPath in report.RetiredAssetPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = ResolveStoredPath(storedPath);
            var retained = false;
            if (TableExistsV17(connection, "replay_assets"))
            {
                using var asset = connection.Prepare("SELECT 1 FROM replay_assets WHERE file_path=? LIMIT 1;");
                asset.Bind(1, ToStoredPath(path));
                retained = asset.Read();
            }
            if (retained || !File.Exists(path)) continue;
            AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, path);
            if (File.Exists(path))
                throw new IOException("Replay v17 cutover could not remove retired asset: " + path);
        }
    }

    private static void MarkCutoverAppliedV17(WinSqliteConnection connection)
    {
        connection.Execute("BEGIN IMMEDIATE;");
        try
        {
            using var update = connection.Prepare(
                "UPDATE replay_migrations SET state='Applied', applied_utc=? "
                + "WHERE migration_id=? AND state='PendingCleanup';");
            update.Bind(1, DateTime.UtcNow.ToString("O"));
            update.Bind(2, ReplayV17CutoverMigrationId);
            update.Execute();
            connection.Execute("COMMIT;");
        }
        catch
        {
            TryRollback(connection);
            throw;
        }
        using var verify = connection.Prepare(
            "SELECT 1 FROM replay_migrations WHERE migration_id=? AND state='Applied' LIMIT 1;");
        verify.Bind(1, ReplayV17CutoverMigrationId);
        if (!verify.Read())
            throw new InvalidDataException("Replay v17 cutover asset cleanup was not durably committed.");
    }

    private ReplayV17CutoverReportFile WriteCutoverReportV17(
        IReadOnlyList<ReplayV17CutoverRecord> records,
        IReadOnlyList<string> assetPaths,
        long compressedBytes)
    {
        var scannedUtc = DateTime.UtcNow.ToString("O");
        var report = new ReplayV17CutoverReport
        {
            MigrationId = ReplayV17CutoverMigrationId,
            ScannedUtc = scannedUtc,
            Records = records.ToList(),
            RetiredAssetPaths = assetPaths.Select(ToStoredPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.Ordinal).ToList(),
            CompressedBytes = compressedBytes
        };
        var payload = ReplayCanonicalJsonV17.SerializeUtf8(report);
        var sha256 = ReplayCanonicalJsonV17.Sha256(payload);
        var directory = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "MigrationReports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            ReplayV17CutoverMigrationId + "-" + Path.GetFileNameWithoutExtension(databasePath)
            + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
        using (var transaction = AuraSharedFileStore.BeginWrite(AuraToolsIds.ModId, path, overwrite: false))
        {
            transaction.Stream.Write(payload, 0, payload.Length);
            transaction.Commit();
        }
        if (!string.Equals(ReplayCanonicalJsonV17.Sha256(File.ReadAllBytes(path)), sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replay v17 cutover report verification failed.");
        return new ReplayV17CutoverReportFile(path, sha256, scannedUtc);
    }

    private static void DeleteReplayV17(WinSqliteConnection connection, string recordId)
    {
        foreach (var table in new[]
                 {
                      "replay_export_jobs", "replay_capture_batches", "replay_capture_sessions",
                      "replay_asset_refs", "replay_truth_checkpoints",
                     "replay_presentation_checkpoints", "replay_truth_chunks", "replay_presentation_chunks", "replay_documents"
                 })
        {
            if (!TableExistsV17(connection, table)) continue;
            using var delete = connection.Prepare("DELETE FROM " + table + " WHERE record_id=?;");
            delete.Bind(1, recordId);
            delete.Execute();
        }
    }

    private static bool TableExistsV17(WinSqliteConnection connection, string table)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, table);
        return query.Read();
    }

}

internal sealed class ReplayV17CutoverReport
{
    public string MigrationId { get; set; } = "";
    public string ScannedUtc { get; set; } = "";
    public List<ReplayV17CutoverRecord> Records { get; set; } = new();
    public List<string> RetiredAssetPaths { get; set; } = new();
    public long CompressedBytes { get; set; }
}

internal sealed class ReplayV17CutoverRecord
{
    public string RecordId { get; set; } = "";
    public int ReplayProtocol { get; set; }
    public string PreviousReplayState { get; set; } = "";
    public long CompressedBytes { get; set; }
}

internal sealed class ReplayV17CutoverReportFile
{
    internal ReplayV17CutoverReportFile(string path, string sha256, string scannedUtc)
    {
        Path = path;
        Sha256 = sha256;
        ScannedUtc = scannedUtc;
    }
    internal string Path { get; }
    internal string Sha256 { get; }
    internal string ScannedUtc { get; }
}
