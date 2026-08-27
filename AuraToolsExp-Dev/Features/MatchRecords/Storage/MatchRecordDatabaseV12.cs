using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    private const string ReplayV12CutoverMigrationId =
        "replay-v11-to-v12-independent-presentation-cutover";

    internal bool SaveSummaryV12(MatchRecord record, MatchAnalysisReport? analysis, bool rejected = false)
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
                record.ReplayProtocol = ReplayProtocolV12.DocumentVersion;
                record.ReplayState = rejected ? MatchReplayStates.Rejected : MatchReplayStates.SummaryOnly;
                InsertRecordV12(connection, record, compressedBytes: 0);
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

    internal bool SaveV12(
        MatchRecord record,
        ReplayDocumentEnvelopeV12 envelope,
        MatchAnalysisReport? analysis = null,
        int chunkTargetBytes = ReplayJournalChunkerV12.DefaultTargetBytes)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (envelope?.Document == null) throw new ArgumentNullException(nameof(envelope));
        var validation = ReplayDocumentValidatorV12.Validate(envelope);
        if (!validation.IsValid)
            throw new InvalidDataException("Replay Document v12 is invalid: " + validation.Message);
        var document = envelope.Document;
        if (!string.Equals(record.RecordId, document.Header.RecordId, StringComparison.Ordinal))
            throw new InvalidDataException("Replay record id does not match its v12 document.");

        var truthChunks = ReplayJournalChunkerV12.Build(
            ReplayJournalLanesV12.Truth,
            document.TruthEvents,
            chunkTargetBytes);
        var presentationChunks = ReplayJournalChunkerV12.Build(
            ReplayJournalLanesV12.Presentation,
            document.PresentationEvents,
            chunkTargetBytes);
        var skeleton = CloneV12WithoutTransientPayload(envelope);
        skeleton.Document.TruthEvents.Clear();
        skeleton.Document.PresentationEvents.Clear();
        skeleton.Document.TruthCheckpoints.Clear();
        skeleton.Document.PresentationCheckpoints.Clear();
        var documentPayload = ReplayPayloadV12.Encode(skeleton);
        var attachmentMoves = new List<AttachmentMove>();

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
                attachmentMoves = StageAttachmentsV12(document);

                record.ReplayProtocol = ReplayProtocolV12.DocumentVersion;
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
                                         + document.TruthCheckpoints.Sum(item => (long)ReplayPayloadV12.Encode(item).Length)
                                         + document.PresentationCheckpoints.Sum(item => (long)ReplayPayloadV12.Encode(item).Length);
                record.ContentSha256 = envelope.DeclaredDocumentRoot;
                record.ModFingerprint = "";
                record.RequiredCapabilities = document.Header.RequiredCapabilities.ToList();
                record.OptionalCapabilities = document.Header.OptionalCapabilities.ToList();
                record.ContentDependencies = document.Presentation.Entities
                    .Select(item => item.Provenance.OwnerModId)
                    .Concat(document.Presentation.Cards.Select(item => item.Provenance.OwnerModId))
                    .Concat(document.Presentation.Buffs.Select(item => item.Provenance.OwnerModId))
                    .Concat(document.Presentation.Intents.Select(item => item.Provenance.OwnerModId))
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToList();
                InsertRecordV12(connection, record, record.CompressedBytes);

                using (var insert = connection.Prepare(
                           "INSERT INTO replay_documents(record_id, document_version, document_state, document_root, truth_root, "
                           + "presentation_root, initial_state_sha256, final_state_sha256, presentation_abi, truth_event_count, "
                           + "presentation_event_count, truth_checkpoint_count, presentation_checkpoint_count, asset_count, "
                           + "compressed_bytes, document_payload) VALUES(?, 12, 'Ready', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, record.RecordId);
                    insert.Bind(2, envelope.DeclaredDocumentRoot);
                    insert.Bind(3, document.Header.TruthRoot);
                    insert.Bind(4, document.Header.PresentationRoot);
                    insert.Bind(5, document.Header.InitialPublicStateSha256);
                    insert.Bind(6, document.Header.FinalPublicStateSha256);
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
                InsertChunksV12(connection, "replay_truth_chunks", record.RecordId, truthChunks);
                InsertChunksV12(connection, "replay_presentation_chunks", record.RecordId, presentationChunks);
                InsertCheckpointsV12(connection, record.RecordId, document);
                InsertAssetsV12(connection, record.RecordId, document);
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
                CleanupCommittedAttachments(attachmentMoves);
                throw;
            }
        }
    }

    internal ReplayDocumentEnvelopeV12? LoadV12(string recordId, bool loadAssetPayloads = false)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            ReplayDocumentEnvelopeV12 envelope;
            using (var query = connection.Prepare(
                       "SELECT document_version, document_state, document_root, document_payload FROM replay_documents "
                       + "WHERE record_id=? LIMIT 1;"))
            {
                query.Bind(1, recordId.Trim());
                if (!query.Read()) return null;
                if (query.Int64(0) != ReplayProtocolV12.DocumentVersion
                    || !string.Equals(query.Text(1), MatchReplayStates.Ready, StringComparison.Ordinal))
                    return null;
                envelope = ReplayPayloadV12.Decode<ReplayDocumentEnvelopeV12>(query.Blob(3));
                if (!string.Equals(envelope.DeclaredDocumentRoot, query.Text(2), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Stored Replay Document v12 root does not match its envelope.");
            }
            envelope.Document.TruthEvents = LoadChunksV12(
                connection,
                "replay_truth_chunks",
                recordId,
                ReplayJournalLanesV12.Truth).ToList();
            envelope.Document.PresentationEvents = LoadChunksV12(
                connection,
                "replay_presentation_chunks",
                recordId,
                ReplayJournalLanesV12.Presentation).ToList();
            LoadCheckpointsV12(connection, recordId, envelope.Document);
            if (loadAssetPayloads)
            {
                foreach (var asset in envelope.Document.Assets)
                {
                    var path = AttachmentPathV12(asset);
                    if (File.Exists(path)) asset.Payload = File.ReadAllBytes(path);
                }
            }
            var validation = ReplayDocumentValidatorV12.Validate(envelope);
            if (!validation.IsValid)
                throw new InvalidDataException("Stored Replay Document v12 is invalid: " + validation.Message);
            return envelope;
        }
    }

    internal void SavePovV12(string recordId, ReplayPovSidecarV12 sidecar)
    {
        if (string.IsNullOrWhiteSpace(recordId) || sidecar == null || string.IsNullOrWhiteSpace(sidecar.PlayerId))
            throw new ArgumentException("Replay POV identity is missing.");
        var validation = ReplayPovContractV12.Validate(sidecar, requirePayloads: true);
        if (validation.Length > 0) throw new InvalidDataException("Replay POV sidecar is invalid: " + validation);
        var parent = LoadV12(recordId)
                     ?? throw new InvalidDataException("Replay POV parent document is missing.");
        var alignment = ReplayPovContractV12.ValidateAlignment(sidecar, parent);
        if (alignment.Length > 0) throw new InvalidDataException("Replay POV alignment is invalid: " + alignment);
        var attachmentMoves = new List<AttachmentMove>();
        var skeleton = ReplayCanonicalJsonV12.Clone(sidecar);
        foreach (var asset in skeleton.Assets) asset.Payload = Array.Empty<byte>();
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                using (var document = connection.Prepare("SELECT document_root FROM replay_documents WHERE record_id=? LIMIT 1;"))
                {
                    document.Bind(1, recordId);
                    if (!document.Read()
                        || !string.Equals(document.Text(0), sidecar.ParentDocumentRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Replay POV parent document root does not match storage.");
                }
                attachmentMoves = StageAttachmentsV12(new ReplayDocumentV12 { Assets = sidecar.Assets });
                using (var insert = connection.Prepare(
                           "INSERT OR REPLACE INTO replay_pov_sidecars(record_id, player_id, parent_document_root, sidecar_sha256, payload) "
                           + "VALUES(?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, recordId);
                    insert.Bind(2, sidecar.PlayerId);
                    insert.Bind(3, sidecar.ParentDocumentRoot);
                    insert.Bind(4, sidecar.SidecarSha256);
                    insert.Bind(5, ReplayPayloadV12.Encode(skeleton));
                    insert.Execute();
                }
                InsertPovAssetsV12(connection, recordId, sidecar.PlayerId, sidecar.Assets);
                CommitAttachments(attachmentMoves);
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                CleanupStaging(attachmentMoves);
                CleanupCommittedAttachments(attachmentMoves);
                throw;
            }
            SweepUnreferencedReplayAssets();
        }
    }

    internal ReplayPovSidecarV12? LoadPovV12(string recordId, string playerId, bool loadAssetPayloads = false)
    {
        if (string.IsNullOrWhiteSpace(recordId) || string.IsNullOrWhiteSpace(playerId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            ReplayPovSidecarV12 sidecar;
            using (var query = connection.Prepare(
                       "SELECT parent_document_root, sidecar_sha256, payload FROM replay_pov_sidecars "
                       + "WHERE record_id=? AND player_id=? LIMIT 1;"))
            {
                query.Bind(1, recordId);
                query.Bind(2, playerId);
                if (!query.Read()) return null;
                sidecar = ReplayPayloadV12.Decode<ReplayPovSidecarV12>(query.Blob(2));
                if (!string.Equals(sidecar.ParentDocumentRoot, query.Text(0), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(sidecar.SidecarSha256, query.Text(1), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Stored replay POV metadata is inconsistent.");
            }
            if (loadAssetPayloads)
                foreach (var asset in sidecar.Assets)
                {
                    var path = AttachmentPathV12(asset);
                    if (File.Exists(path)) asset.Payload = File.ReadAllBytes(path);
                }
            var validation = ReplayPovContractV12.Validate(sidecar, requirePayloads: loadAssetPayloads);
            if (validation.Length > 0) throw new InvalidDataException("Stored replay POV sidecar is invalid: " + validation);
            return sidecar;
        }
    }

    internal ReplayPovSidecarV12? LoadFirstPovV12(string recordId, bool loadAssetPayloads = false)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return null;
        string playerId;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                "SELECT player_id FROM replay_pov_sidecars WHERE record_id=? ORDER BY player_id LIMIT 1;");
            query.Bind(1, recordId.Trim());
            if (!query.Read()) return null;
            playerId = query.Text(0);
        }
        return LoadPovV12(recordId, playerId, loadAssetPayloads);
    }

    private void InsertRecordV12(WinSqliteConnection connection, MatchRecord record, long compressedBytes)
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
        insert.Bind(10, ReplayProtocolV12.DocumentVersion);
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

    private static void InsertChunksV12(
        WinSqliteConnection connection,
        string table,
        string recordId,
        IEnumerable<ReplayJournalChunkV12> chunks)
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

    private static IReadOnlyList<ReplayJournalEventV12> LoadChunksV12(
        WinSqliteConnection connection,
        string table,
        string recordId,
        string lane)
    {
        var chunks = new List<ReplayJournalChunkV12>();
        using var query = connection.Prepare(
            "SELECT chunk_index, first_sequence, last_sequence, first_time_ticks, last_time_ticks, previous_chunk_sha256, "
            + "sha256, payload FROM " + table + " WHERE record_id=? ORDER BY chunk_index;");
        query.Bind(1, recordId);
        while (query.Read())
            chunks.Add(new ReplayJournalChunkV12
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
        return ReplayJournalChunkerV12.Decode(lane, chunks);
    }

    private static void InsertCheckpointsV12(
        WinSqliteConnection connection,
        string recordId,
        ReplayDocumentV12 document)
    {
        foreach (var checkpoint in document.TruthCheckpoints)
        {
            using var insert = connection.Prepare(
                "INSERT INTO replay_truth_checkpoints(record_id, event_sequence, time_ticks, sha256, payload) VALUES(?, ?, ?, ?, ?);");
            insert.Bind(1, recordId);
            insert.Bind(2, checkpoint.EventSequence);
            insert.Bind(3, checkpoint.TimeTicks);
            insert.Bind(4, checkpoint.CheckpointSha256);
            insert.Bind(5, ReplayPayloadV12.Encode(checkpoint));
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
            insert.Bind(5, ReplayPayloadV12.Encode(checkpoint));
            insert.Execute();
        }
    }

    private static void LoadCheckpointsV12(
        WinSqliteConnection connection,
        string recordId,
        ReplayDocumentV12 document)
    {
        document.TruthCheckpoints.Clear();
        using (var query = connection.Prepare(
                   "SELECT sha256, payload FROM replay_truth_checkpoints WHERE record_id=? ORDER BY event_sequence;"))
        {
            query.Bind(1, recordId);
            while (query.Read())
            {
                var value = ReplayPayloadV12.Decode<ReplayTruthCheckpointV12>(query.Blob(1));
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
            var value = ReplayPayloadV12.Decode<ReplayPresentationCheckpointV12>(presentation.Blob(1));
            if (!string.Equals(value.CheckpointSha256, presentation.Text(0), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Stored replay presentation checkpoint hash mismatch.");
            document.PresentationCheckpoints.Add(value);
        }
    }

    private void InsertAssetsV12(WinSqliteConnection connection, string recordId, ReplayDocumentV12 document)
    {
        foreach (var asset in document.Assets)
        {
            var finalPath = AttachmentPathV12(asset);
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

    private void InsertPovAssetsV12(
        WinSqliteConnection connection,
        string recordId,
        string playerId,
        IEnumerable<ReplayAssetV12> assets)
    {
        foreach (var asset in assets)
        {
            var finalPath = AttachmentPathV12(asset);
            using (var insert = connection.Prepare(
                       "INSERT OR IGNORE INTO replay_assets(asset_sha256, media_type, extension, file_path, byte_length, width, height, "
                       + "sample_rate, channels, sample_frames) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
            {
                insert.Bind(1, asset.Sha256); insert.Bind(2, asset.MediaType ?? ""); insert.Bind(3, asset.Extension ?? "");
                insert.Bind(4, ToStoredPath(finalPath)); insert.Bind(5, asset.ByteLength); insert.Bind(6, asset.Width);
                insert.Bind(7, asset.Height); insert.Bind(8, asset.SampleRate); insert.Bind(9, asset.Channels);
                insert.Bind(10, asset.SampleFrames); insert.Execute();
            }
            using var reference = connection.Prepare(
                "INSERT INTO replay_pov_asset_refs(record_id, player_id, asset_sha256, usage, required) VALUES(?, ?, ?, ?, ?);");
            reference.Bind(1, recordId); reference.Bind(2, playerId); reference.Bind(3, asset.Sha256);
            reference.Bind(4, asset.Usage ?? ""); reference.Bind(5, asset.Required ? 1 : 0); reference.Execute();
        }
    }

    private List<AttachmentMove> StageAttachmentsV12(ReplayDocumentV12 document)
    {
        var result = new List<AttachmentMove>();
        Directory.CreateDirectory(AttachmentDirectory);
        try
        {
            foreach (var asset in document.Assets)
            {
                var finalPath = AttachmentPathV12(asset);
                if (File.Exists(finalPath))
                {
                    if (!string.Equals(FileSha256(finalPath), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Existing replay asset hash mismatch: " + asset.Sha256);
                    continue;
                }
                if (asset.Payload == null
                    || asset.Payload.LongLength != asset.ByteLength
                    || !string.Equals(ReplayCanonicalJsonV12.Sha256(asset.Payload), asset.Sha256, StringComparison.OrdinalIgnoreCase))
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

    private string AttachmentPathV12(ReplayAssetV12 asset)
    {
        return Path.Combine(AttachmentDirectory, asset.Sha256.ToLowerInvariant() + NormalizeExtension(asset.Extension));
    }

    private static ReplayDocumentEnvelopeV12 CloneV12WithoutTransientPayload(ReplayDocumentEnvelopeV12 envelope)
    {
        var clone = ReplayCanonicalJsonV12.Clone(envelope);
        foreach (var asset in clone.Document.Assets) asset.Payload = Array.Empty<byte>();
        return clone;
    }

    private static void EnsureV12Tables(WinSqliteConnection connection)
    {
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_documents("
                           + "record_id TEXT PRIMARY KEY, document_version INTEGER NOT NULL CHECK(document_version=12), "
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
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_pov_sidecars(record_id TEXT NOT NULL, player_id TEXT NOT NULL, "
                           + "parent_document_root TEXT NOT NULL, sidecar_sha256 TEXT NOT NULL, payload BLOB NOT NULL, "
                            + "PRIMARY KEY(record_id, player_id), FOREIGN KEY(record_id) REFERENCES replay_documents(record_id) ON DELETE CASCADE);");
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_pov_asset_refs(record_id TEXT NOT NULL, player_id TEXT NOT NULL, "
                           + "asset_sha256 TEXT NOT NULL, usage TEXT NOT NULL, required INTEGER NOT NULL, "
                           + "PRIMARY KEY(record_id, player_id, asset_sha256, usage), "
                           + "FOREIGN KEY(record_id, player_id) REFERENCES replay_pov_sidecars(record_id, player_id) ON DELETE CASCADE, "
                           + "FOREIGN KEY(asset_sha256) REFERENCES replay_assets(asset_sha256));");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_replay_pov_asset_refs_hash ON replay_pov_asset_refs(asset_sha256);");
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

    private void ApplyV11ToV12Cutover(WinSqliteConnection connection)
    {
        connection.Execute("CREATE TABLE IF NOT EXISTS replay_migrations(migration_id TEXT PRIMARY KEY, state TEXT NOT NULL, "
                           + "scanned_utc TEXT NOT NULL, applied_utc TEXT NOT NULL, report_path TEXT NOT NULL, report_sha256 TEXT NOT NULL, "
                           + "record_count INTEGER NOT NULL, chunk_bytes INTEGER NOT NULL);");
        using (var applied = connection.Prepare("SELECT 1 FROM replay_migrations WHERE migration_id=? AND state='Applied' LIMIT 1;"))
        {
            applied.Bind(1, ReplayV12CutoverMigrationId);
            if (applied.Read()) return;
        }
        var hasV12Documents = false;
        if (TableExistsV12(connection, "replay_documents"))
        {
            using var current = connection.Prepare(
                "SELECT 1 FROM replay_documents d JOIN battle_records b ON b.record_id=d.record_id "
                + "WHERE b.replay_protocol>=12 LIMIT 1;");
            hasV12Documents = current.Read();
        }
        var assetPaths = new List<string>();
        if (TableExistsV12(connection, "replay_assets"))
        {
            var assetSql = hasV12Documents && TableExistsV12(connection, "replay_asset_refs")
                ? "SELECT DISTINCT a.file_path FROM replay_assets a JOIN replay_asset_refs r ON r.asset_sha256=a.asset_sha256 "
                  + "JOIN battle_records b ON b.record_id=r.record_id WHERE b.replay_protocol<12;"
                : "SELECT file_path FROM replay_assets;";
            using var assets = connection.Prepare(assetSql);
            while (assets.Read()) assetPaths.Add(ResolveStoredPath(assets.Text(0)));
        }
        var recordCount = 0L;
        var bytes = 0L;
        var retiredRecords = new List<ReplayV12CutoverRecord>();
        using (var totals = connection.Prepare(
                   "SELECT COUNT(*), COALESCE(SUM(compressed_bytes),0) FROM battle_records WHERE replay_protocol<12;"))
            if (totals.Read())
            {
                recordCount = totals.Int64(0);
                bytes = totals.Int64(1);
            }
        using (var records = connection.Prepare(
                   "SELECT record_id, replay_protocol, replay_state, compressed_bytes FROM battle_records "
                   + "WHERE replay_protocol<12 ORDER BY sequence;"))
            while (records.Read())
                retiredRecords.Add(new ReplayV12CutoverRecord
                {
                    RecordId = records.Text(0),
                    ReplayProtocol = (int)records.Int64(1),
                    PreviousReplayState = records.Text(2),
                    CompressedBytes = records.Int64(3)
                });
        var report = WriteCutoverReportV12(retiredRecords, assetPaths, bytes);
        connection.Execute("BEGIN IMMEDIATE;");
        try
        {
            using (var query = connection.Prepare("SELECT record_id, metadata_payload FROM battle_records WHERE replay_protocol<12;"))
            {
                var updates = new List<(string Id, byte[] Metadata)>();
                while (query.Read())
                {
                    var metadata = DecodeMetadata(query.Blob(1));
                    metadata.ContentSha256 = "";
                    metadata.RequiredCapabilities.Clear();
                    metadata.OptionalCapabilities.Clear();
                    metadata.ContentDependencies.Clear();
                    metadata.CaptureDiagnostics.Add("pre-v12 structured replay retired by " + ReplayV12CutoverMigrationId);
                    updates.Add((query.Text(0), MatchReplayPayload.Encode(metadata)));
                }
                foreach (var update in updates)
                {
                    using var statement = connection.Prepare(
                        "UPDATE battle_records SET replay_state='SummaryOnly', compressed_bytes=0, initial_payload=?, metadata_payload=? "
                        + "WHERE record_id=? AND replay_protocol<12;");
                    statement.Bind(1, MatchReplayPayload.Encode(new MatchReplayInitialState()));
                    statement.Bind(2, update.Metadata);
                    statement.Bind(3, update.Id);
                    statement.Execute();
                }
            }
            if (hasV12Documents)
            {
                connection.Execute("DELETE FROM replay_documents WHERE record_id IN "
                                   + "(SELECT record_id FROM battle_records WHERE replay_protocol<12);");
                if (TableExistsV12(connection, "replay_export_jobs"))
                    connection.Execute("DELETE FROM replay_export_jobs WHERE record_id IN "
                                       + "(SELECT record_id FROM battle_records WHERE replay_protocol<12);");
                if (TableExistsV12(connection, "replay_assets"))
                {
                    var deleteAssets = "DELETE FROM replay_assets WHERE NOT EXISTS "
                                       + "(SELECT 1 FROM replay_asset_refs r WHERE r.asset_sha256=replay_assets.asset_sha256)";
                    if (TableExistsV12(connection, "replay_pov_asset_refs"))
                        deleteAssets += " AND NOT EXISTS (SELECT 1 FROM replay_pov_asset_refs p "
                                        + "WHERE p.asset_sha256=replay_assets.asset_sha256)";
                    connection.Execute(deleteAssets + ";");
                }
                connection.Execute("DROP TABLE IF EXISTS replay_timeline_chunks;");
                connection.Execute("DROP TABLE IF EXISTS replay_chunks;");
            }
            else
            {
                foreach (var table in new[]
                         {
                             "replay_pov_asset_refs", "replay_pov_sidecars", "replay_truth_checkpoints", "replay_presentation_checkpoints",
                             "replay_truth_chunks", "replay_presentation_chunks", "replay_asset_refs",
                             "replay_timeline_chunks", "replay_documents", "replay_assets", "replay_export_jobs", "replay_chunks"
                         })
                    connection.Execute("DROP TABLE IF EXISTS " + table + ";");
            }
            using var migration = connection.Prepare(
                "INSERT OR REPLACE INTO replay_migrations(migration_id, state, scanned_utc, applied_utc, report_path, report_sha256, "
                + "record_count, chunk_bytes) VALUES(?, 'Applied', ?, ?, ?, ?, ?, ?);");
            var now = DateTime.UtcNow.ToString("O");
            migration.Bind(1, ReplayV12CutoverMigrationId);
            migration.Bind(2, report.ScannedUtc);
            migration.Bind(3, now);
            migration.Bind(4, ToStoredPath(report.Path));
            migration.Bind(5, report.Sha256);
            migration.Bind(6, recordCount);
            migration.Bind(7, bytes);
            migration.Execute();
            connection.Execute("COMMIT;");
        }
        catch
        {
            TryRollback(connection);
            throw;
        }
        foreach (var path in assetPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var retained = false;
                if (hasV12Documents && TableExistsV12(connection, "replay_assets"))
                {
                    using var asset = connection.Prepare("SELECT 1 FROM replay_assets WHERE file_path=? LIMIT 1;");
                    asset.Bind(1, ToStoredPath(path));
                    retained = asset.Read();
                }
                if (!retained && File.Exists(path)) AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, path);
            }
            catch (Exception ex) { AuraToolsLog.Warn("[MatchRecords] v11 asset cleanup failed: " + ex.Message); }
        }
    }

    private ReplayV12CutoverReportFile WriteCutoverReportV12(
        IReadOnlyList<ReplayV12CutoverRecord> records,
        IReadOnlyList<string> assetPaths,
        long compressedBytes)
    {
        var scannedUtc = DateTime.UtcNow.ToString("O");
        var report = new ReplayV12CutoverReport
        {
            MigrationId = ReplayV12CutoverMigrationId,
            ScannedUtc = scannedUtc,
            Records = records.ToList(),
            RetiredAssetPaths = assetPaths.Select(ToStoredPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.Ordinal).ToList(),
            CompressedBytes = compressedBytes
        };
        var payload = ReplayCanonicalJsonV12.SerializeUtf8(report);
        var sha256 = ReplayCanonicalJsonV12.Sha256(payload);
        var directory = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "MigrationReports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            ReplayV12CutoverMigrationId + "-" + Path.GetFileNameWithoutExtension(databasePath)
            + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
        using (var transaction = AuraSharedFileStore.BeginWrite(AuraToolsIds.ModId, path, overwrite: false))
        {
            transaction.Stream.Write(payload, 0, payload.Length);
            transaction.Commit();
        }
        if (!string.Equals(ReplayCanonicalJsonV12.Sha256(File.ReadAllBytes(path)), sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replay v12 cutover report verification failed.");
        return new ReplayV12CutoverReportFile(path, sha256, scannedUtc);
    }

    private static void DeleteReplayV12(WinSqliteConnection connection, string recordId)
    {
        foreach (var table in new[]
                 {
                      "replay_export_jobs", "replay_pov_asset_refs", "replay_pov_sidecars", "replay_asset_refs", "replay_truth_checkpoints",
                     "replay_presentation_checkpoints", "replay_truth_chunks", "replay_presentation_chunks", "replay_documents"
                 })
        {
            if (!TableExistsV12(connection, table)) continue;
            using var delete = connection.Prepare("DELETE FROM " + table + " WHERE record_id=?;");
            delete.Bind(1, recordId);
            delete.Execute();
        }
    }

    private static bool TableExistsV12(WinSqliteConnection connection, string table)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, table);
        return query.Read();
    }

}

internal sealed class ReplayV12CutoverReport
{
    public string MigrationId { get; set; } = "";
    public string ScannedUtc { get; set; } = "";
    public List<ReplayV12CutoverRecord> Records { get; set; } = new();
    public List<string> RetiredAssetPaths { get; set; } = new();
    public long CompressedBytes { get; set; }
}

internal sealed class ReplayV12CutoverRecord
{
    public string RecordId { get; set; } = "";
    public int ReplayProtocol { get; set; }
    public string PreviousReplayState { get; set; } = "";
    public long CompressedBytes { get; set; }
}

internal sealed class ReplayV12CutoverReportFile
{
    internal ReplayV12CutoverReportFile(string path, string sha256, string scannedUtc)
    {
        Path = path;
        Sha256 = sha256;
        ScannedUtc = scannedUtc;
    }
    internal string Path { get; }
    internal string Sha256 { get; }
    internal string ScannedUtc { get; }
}
