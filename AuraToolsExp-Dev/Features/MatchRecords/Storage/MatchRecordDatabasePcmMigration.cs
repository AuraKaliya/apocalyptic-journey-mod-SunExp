using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    private void MigrateReplayPcm16WaveV11Documents(WinSqliteConnection connection)
    {
        const string migrationId = "replay-v11-pcm16-wave-header";
        using (var applied = connection.Prepare(
                   "SELECT 1 FROM replay_migrations WHERE migration_id=? AND state='Applied' LIMIT 1;"))
        {
            applied.Bind(1, migrationId);
            if (applied.Read()) return;
        }

        var assets = ScanPcmWaveAssets(connection);
        var replacements = assets
            .Where(value => value.Kind == PcmWaveAssetMigrationKind.Repairable)
            .ToDictionary(value => value.OldHash, value => value, StringComparer.OrdinalIgnoreCase);
        var documentPlans = PlanPcmWaveDocuments(connection, replacements, migrationId);
        var staged = StagePcmWaveAssets(replacements.Values);
        var archiveAfterCommit = new List<string>();
        long migratedBytes = 0;

        connection.Execute("BEGIN IMMEDIATE;");
        try
        {
            foreach (var asset in replacements.Values)
                InsertRepairedPcmAsset(connection, asset);

            foreach (var plan in documentPlans)
            {
                if (plan.Kind == PcmWaveDocumentMigrationKind.Migrated && plan.Document != null)
                    migratedBytes += PersistPcmWaveDocument(connection, plan, migrationId);
                else if (plan.Kind == PcmWaveDocumentMigrationKind.Corrupt)
                    ReclassifyPcmWaveDocument(connection, plan, migrationId);
            }

            foreach (var invalid in assets.Where(value => value.Kind == PcmWaveAssetMigrationKind.Invalid))
                ReclassifyReadyPcmWaveReferences(connection, invalid, migrationId);

            foreach (var asset in replacements.Values)
            {
                using var refs = connection.Prepare(
                    "SELECT 1 FROM replay_asset_refs WHERE asset_sha256=? LIMIT 1;");
                refs.Bind(1, asset.OldHash);
                if (refs.Read()) continue;
                using var delete = connection.Prepare("DELETE FROM replay_assets WHERE asset_sha256=?;");
                delete.Bind(1, asset.OldHash);
                delete.Execute();
                archiveAfterCommit.Add(asset.OldPath);
            }

            using (var migration = connection.Prepare(
                       "INSERT OR REPLACE INTO replay_migrations(migration_id, state, scanned_utc, applied_utc, "
                       + "report_path, report_sha256, record_count, chunk_bytes) "
                       + "VALUES(?, 'Applied', ?, ?, '', '', ?, ?);"))
            {
                var now = DateTime.UtcNow.ToString("O");
                migration.Bind(1, migrationId);
                migration.Bind(2, now);
                migration.Bind(3, now);
                migration.Bind(4, documentPlans.Count(value => value.Kind == PcmWaveDocumentMigrationKind.Migrated));
                migration.Bind(5, migratedBytes);
                migration.Execute();
            }

            CommitAttachments(staged);
            connection.Execute("COMMIT;");
        }
        catch
        {
            TryRollback(connection);
            CleanupStaging(staged);
            throw;
        }

        foreach (var path in archiveAfterCommit.Distinct(StringComparer.OrdinalIgnoreCase))
            ArchiveLegacyPcmWave(path);

        var migratedDocuments = documentPlans.Count(value => value.Kind == PcmWaveDocumentMigrationKind.Migrated);
        var corruptDocuments = documentPlans.Count(value => value.Kind == PcmWaveDocumentMigrationKind.Corrupt);
        if (assets.Count > 0 || migratedDocuments > 0 || corruptDocuments > 0)
        {
            AuraToolsLog.Info(
                "[MatchRecords] PCM16 WAV migration applied: repairedAssets="
                + replacements.Count
                + ", migratedDocuments="
                + migratedDocuments
                + ", corruptDocuments="
                + corruptDocuments
                + ", invalidAssets="
                + assets.Count(value => value.Kind == PcmWaveAssetMigrationKind.Invalid)
                + ", archivedLegacyFiles="
                + archiveAfterCommit.Count
                + ".");
        }
    }

    private List<PcmWaveAssetMigrationPlan> ScanPcmWaveAssets(WinSqliteConnection connection)
    {
        var result = new List<PcmWaveAssetMigrationPlan>();
        using var query = connection.Prepare(
            "SELECT asset_sha256, file_path, byte_length, sample_rate, channels, sample_frames "
            + "FROM replay_assets WHERE lower(media_type)='audio/wav' OR lower(extension)='.wav' "
            + "ORDER BY asset_sha256;");
        while (query.Read())
        {
            var oldHash = query.Text(0);
            var path = ResolveStoredPath(query.Text(1));
            try
            {
                if (!File.Exists(path))
                {
                    result.Add(PcmWaveAssetMigrationPlan.Invalid(oldHash, path, "file-missing"));
                    continue;
                }
                var payload = File.ReadAllBytes(path);
                if (!string.Equals(ReplayCanonicalJsonV11.Sha256(payload), oldHash, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(PcmWaveAssetMigrationPlan.Invalid(oldHash, path, "content-hash-mismatch"));
                    continue;
                }
                if (ReplayPcm16WaveContractV11.TryRead(payload, out var current, out _))
                {
                    if (!PcmMetadataMatches(query, payload.LongLength, current))
                        result.Add(PcmWaveAssetMigrationPlan.Invalid(oldHash, path, "metadata-mismatch"));
                    continue;
                }
                if (!ReplayPcm16WaveContractV11.TryRepairLegacyMissingBits(
                        payload,
                        out var repaired,
                        out var wave,
                        out var repairError)
                    || !PcmMetadataMatches(query, payload.LongLength, wave))
                {
                    result.Add(PcmWaveAssetMigrationPlan.Invalid(oldHash, path, repairError));
                    continue;
                }

                var newHash = ReplayCanonicalJsonV11.Sha256(repaired);
                var newPath = Path.Combine(AttachmentDirectory, newHash.ToLowerInvariant() + ".wav");
                result.Add(PcmWaveAssetMigrationPlan.Repairable(
                    oldHash,
                    path,
                    newHash,
                    newPath,
                    repaired,
                    wave));
            }
            catch (Exception ex)
            {
                result.Add(PcmWaveAssetMigrationPlan.Invalid(oldHash, path, ex.Message));
            }
        }
        return result;
    }

    private static bool PcmMetadataMatches(
        WinSqliteConnection.WinSqliteStatement query,
        long byteLength,
        ReplayPcm16WaveInfoV11 wave)
    {
        return query.Int64(2) == byteLength
               && query.Int64(3) == wave.SampleRate
               && query.Int64(4) == wave.Channels
               && query.Int64(5) == wave.SampleFrames;
    }

    private static List<PcmWaveDocumentMigrationPlan> PlanPcmWaveDocuments(
        WinSqliteConnection connection,
        IReadOnlyDictionary<string, PcmWaveAssetMigrationPlan> replacements,
        string migrationId)
    {
        var result = new List<PcmWaveDocumentMigrationPlan>();
        if (replacements.Count == 0) return result;
        using var query = connection.Prepare(
            "SELECT DISTINCT d.record_id, d.document_state, b.replay_state "
            + "FROM replay_asset_refs r "
            + "JOIN replay_assets a ON a.asset_sha256=r.asset_sha256 "
            + "JOIN replay_documents d ON d.record_id=r.record_id "
            + "JOIN battle_records b ON b.record_id=r.record_id "
            + "WHERE lower(a.media_type)='audio/wav' OR lower(a.extension)='.wav' "
            + "ORDER BY b.sequence;");
        while (query.Read())
        {
            var recordId = query.Text(0);
            var documentState = query.Text(1);
            var replayState = query.Text(2);
            try
            {
                var document = LoadStoredV11Document(connection, recordId);
                var replacedAttachments = 0;
                foreach (var attachment in document.Attachments)
                {
                    if (!replacements.TryGetValue(attachment.Sha256, out var replacement)) continue;
                    attachment.Sha256 = replacement.NewHash;
                    attachment.MediaType = "audio/wav";
                    attachment.Extension = ".wav";
                    attachment.ByteLength = replacement.Payload.LongLength;
                    attachment.SampleRate = replacement.Wave.SampleRate;
                    attachment.Channels = replacement.Wave.Channels;
                    attachment.SampleFrames = replacement.Wave.SampleFrames;
                    attachment.Payload = replacement.Payload;
                    replacedAttachments++;
                }
                if (replacedAttachments == 0) continue;

                var replacedCues = 0;
                foreach (var cue in document.Events.SelectMany(value =>
                             value.Audio ?? new List<ReplayAudioCueV11>()))
                {
                    if (!replacements.TryGetValue(cue.AssetSha256, out var replacement)) continue;
                    cue.AssetSha256 = replacement.NewHash;
                    replacedCues++;
                }

                var validation = ReplayDocumentFinalizerV11.FinalizeAndValidate(document);
                if (string.Equals(documentState, "Ready", StringComparison.Ordinal)
                    && !validation.IsValid)
                {
                    result.Add(PcmWaveDocumentMigrationPlan.Corrupt(
                        recordId,
                        documentState,
                        replayState,
                        validation.Message));
                    continue;
                }
                result.Add(PcmWaveDocumentMigrationPlan.Migrated(
                    recordId,
                    documentState,
                    replayState,
                    document,
                    replacedAttachments,
                    replacedCues));
            }
            catch (Exception ex)
            {
                result.Add(PcmWaveDocumentMigrationPlan.Corrupt(
                    recordId,
                    documentState,
                    replayState,
                    migrationId + ": " + ex.Message));
            }
        }
        return result;
    }

    private List<AttachmentMove> StagePcmWaveAssets(IEnumerable<PcmWaveAssetMigrationPlan> assets)
    {
        var result = new List<AttachmentMove>();
        Directory.CreateDirectory(AttachmentDirectory);
        foreach (var asset in assets)
        {
            if (File.Exists(asset.NewPath))
            {
                if (!string.Equals(FileSha256(asset.NewPath), asset.NewHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Existing repaired PCM attachment hash mismatch: " + asset.NewHash);
                continue;
            }
            var transaction = AuraSharedFileStore.BeginWrite(AuraToolsIds.ModId, asset.NewPath, overwrite: false);
            transaction.Stream.Write(asset.Payload, 0, asset.Payload.Length);
            result.Add(new AttachmentMove(transaction, asset.NewPath));
        }
        return result;
    }

    private void InsertRepairedPcmAsset(
        WinSqliteConnection connection,
        PcmWaveAssetMigrationPlan asset)
    {
        using var insert = connection.Prepare(
            "INSERT OR IGNORE INTO replay_assets(asset_sha256, media_type, extension, file_path, byte_length, "
            + "width, height, sample_rate, channels, sample_frames) VALUES(?, 'audio/wav', '.wav', ?, ?, 0, 0, ?, ?, ?);");
        insert.Bind(1, asset.NewHash);
        insert.Bind(2, ToStoredPath(asset.NewPath));
        insert.Bind(3, asset.Payload.LongLength);
        insert.Bind(4, asset.Wave.SampleRate);
        insert.Bind(5, asset.Wave.Channels);
        insert.Bind(6, asset.Wave.SampleFrames);
        insert.Execute();
    }

    private static long PersistPcmWaveDocument(
        WinSqliteConnection connection,
        PcmWaveDocumentMigrationPlan plan,
        string migrationId)
    {
        var document = plan.Document!;
        var chunks = ReplayTimelineChunkerV11.Build(document.Events);
        var skeleton = CloneWithoutTransientPayload(document);
        skeleton.Events.Clear();
        var documentPayload = ReplayPayloadV11.Encode(skeleton);
        var compressedBytes = chunks.Sum(value => (long)value.Payload.Length) + documentPayload.LongLength;

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
                   "UPDATE replay_documents SET document_state=?, document_sha256=?, initial_state_sha256=?, "
                   + "final_state_sha256=?, event_chain_sha256=?, renderer_profile=?, event_count=?, checkpoint_count=?, "
                   + "attachment_count=?, compressed_bytes=?, document_payload=? WHERE record_id=?;"))
        {
            update.Bind(1, plan.DocumentState);
            update.Bind(2, document.Header.DocumentSha256);
            update.Bind(3, document.Header.InitialLogicalStateSha256);
            update.Bind(4, document.Header.FinalLogicalStateSha256);
            update.Bind(5, document.Header.FinalEventChainSha256);
            update.Bind(6, document.Header.RenderProfileId ?? "");
            update.Bind(7, document.Events.Count);
            update.Bind(8, document.Checkpoints.Count);
            update.Bind(9, document.Attachments.Count);
            update.Bind(10, compressedBytes);
            update.Bind(11, documentPayload);
            update.Bind(12, plan.RecordId);
            update.Execute();
        }

        var record = GetRecordForMigration(connection, plan.RecordId);
        record.EventCount = document.Events.Count;
        record.TurnCount = Math.Max(document.InitialState.TurnIndex,
            document.Events.Count == 0 ? 1 : document.Events.Max(value => value.TurnIndex));
        record.CompressedBytes = compressedBytes;
        record.ContentSha256 = document.Header.DocumentSha256;
        record.CaptureDiagnostics.Add(
            migrationId
            + ": repairedAttachments=" + plan.RepairedAttachments
            + ", repairedCues=" + plan.RepairedCues);
        using var recordUpdate = connection.Prepare(
            "UPDATE battle_records SET replay_state=?, event_count=?, turn_count=?, compressed_bytes=?, "
            + "metadata_payload=? WHERE record_id=?;");
        recordUpdate.Bind(1, plan.ReplayState);
        recordUpdate.Bind(2, record.EventCount);
        recordUpdate.Bind(3, record.TurnCount);
        recordUpdate.Bind(4, compressedBytes);
        recordUpdate.Bind(5, MatchReplayPayload.Encode(CreateMetadata(record)));
        recordUpdate.Bind(6, plan.RecordId);
        recordUpdate.Execute();
        return compressedBytes;
    }

    private static void ReclassifyPcmWaveDocument(
        WinSqliteConnection connection,
        PcmWaveDocumentMigrationPlan plan,
        string migrationId)
    {
        if (!string.Equals(plan.DocumentState, "Ready", StringComparison.Ordinal)) return;
        var record = GetRecordForMigration(connection, plan.RecordId);
        record.CaptureDiagnostics.Add(migrationId + ": " + plan.Message);
        using (var update = connection.Prepare(
                   "UPDATE battle_records SET replay_state='Corrupt', metadata_payload=? WHERE record_id=?;"))
        {
            update.Bind(1, MatchReplayPayload.Encode(CreateMetadata(record)));
            update.Bind(2, plan.RecordId);
            update.Execute();
        }
        using var document = connection.Prepare(
            "UPDATE replay_documents SET document_state='Corrupt' WHERE record_id=?;");
        document.Bind(1, plan.RecordId);
        document.Execute();
    }

    private static void ReclassifyReadyPcmWaveReferences(
        WinSqliteConnection connection,
        PcmWaveAssetMigrationPlan asset,
        string migrationId)
    {
        var recordIds = new List<string>();
        using (var query = connection.Prepare(
                   "SELECT b.record_id FROM replay_asset_refs r "
                   + "JOIN battle_records b ON b.record_id=r.record_id "
                   + "JOIN replay_documents d ON d.record_id=r.record_id "
                   + "WHERE r.asset_sha256=? AND b.replay_state='Ready' AND d.document_state='Ready';"))
        {
            query.Bind(1, asset.OldHash);
            while (query.Read()) recordIds.Add(query.Text(0));
        }
        foreach (var recordId in recordIds)
        {
            var plan = PcmWaveDocumentMigrationPlan.Corrupt(
                recordId,
                "Ready",
                "Ready",
                "unrepairable PCM attachment " + asset.OldHash + ": " + asset.Message);
            ReclassifyPcmWaveDocument(connection, plan, migrationId);
        }
    }

    private void ArchiveLegacyPcmWave(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var quarantine = Path.Combine(
                Path.GetDirectoryName(databasePath) ?? ".",
                "Quarantine",
                "Attachments");
            Directory.CreateDirectory(quarantine);
            var target = Path.Combine(quarantine, Path.GetFileName(path) + ".legacy-pcm-v6");
            if (File.Exists(target)) AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, target);
            AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, path, target);
        }
        catch
        {
            // ReconcileV11Files moves any surviving unregistered source file into quarantine.
        }
    }

    private static bool TryReadPcmWaveFileHeader(string path, out ReplayPcm16WaveInfoV11 wave)
    {
        wave = default;
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < ReplayPcm16WaveContractV11.HeaderBytes) return false;
            var header = new byte[ReplayPcm16WaveContractV11.HeaderBytes];
            var read = 0;
            while (read < header.Length)
            {
                var count = stream.Read(header, read, header.Length - read);
                if (count <= 0) return false;
                read += count;
            }
            return ReplayPcm16WaveContractV11.TryReadHeader(
                header,
                stream.Length,
                allowMissingBits: false,
                out wave,
                out _);
        }
        catch
        {
            return false;
        }
    }

    private enum PcmWaveAssetMigrationKind
    {
        Repairable,
        Invalid
    }

    private sealed class PcmWaveAssetMigrationPlan
    {
        internal PcmWaveAssetMigrationKind Kind { get; private set; }
        internal string OldHash { get; private set; } = "";
        internal string OldPath { get; private set; } = "";
        internal string NewHash { get; private set; } = "";
        internal string NewPath { get; private set; } = "";
        internal byte[] Payload { get; private set; } = Array.Empty<byte>();
        internal ReplayPcm16WaveInfoV11 Wave { get; private set; }
        internal string Message { get; private set; } = "";

        internal static PcmWaveAssetMigrationPlan Repairable(
            string oldHash,
            string oldPath,
            string newHash,
            string newPath,
            byte[] payload,
            ReplayPcm16WaveInfoV11 wave)
        {
            return new PcmWaveAssetMigrationPlan
            {
                Kind = PcmWaveAssetMigrationKind.Repairable,
                OldHash = oldHash,
                OldPath = oldPath,
                NewHash = newHash,
                NewPath = newPath,
                Payload = payload,
                Wave = wave
            };
        }

        internal static PcmWaveAssetMigrationPlan Invalid(string oldHash, string oldPath, string message)
        {
            return new PcmWaveAssetMigrationPlan
            {
                Kind = PcmWaveAssetMigrationKind.Invalid,
                OldHash = oldHash,
                OldPath = oldPath,
                Message = string.IsNullOrWhiteSpace(message) ? "invalid PCM WAV" : message
            };
        }
    }

    private enum PcmWaveDocumentMigrationKind
    {
        Migrated,
        Corrupt
    }

    private sealed class PcmWaveDocumentMigrationPlan
    {
        internal PcmWaveDocumentMigrationKind Kind { get; private set; }
        internal string RecordId { get; private set; } = "";
        internal string DocumentState { get; private set; } = "";
        internal string ReplayState { get; private set; } = "";
        internal ReplayDocumentV11? Document { get; private set; }
        internal int RepairedAttachments { get; private set; }
        internal int RepairedCues { get; private set; }
        internal string Message { get; private set; } = "";

        internal static PcmWaveDocumentMigrationPlan Migrated(
            string recordId,
            string documentState,
            string replayState,
            ReplayDocumentV11 document,
            int repairedAttachments,
            int repairedCues)
        {
            return new PcmWaveDocumentMigrationPlan
            {
                Kind = PcmWaveDocumentMigrationKind.Migrated,
                RecordId = recordId,
                DocumentState = documentState,
                ReplayState = replayState,
                Document = document,
                RepairedAttachments = repairedAttachments,
                RepairedCues = repairedCues
            };
        }

        internal static PcmWaveDocumentMigrationPlan Corrupt(
            string recordId,
            string documentState,
            string replayState,
            string message)
        {
            return new PcmWaveDocumentMigrationPlan
            {
                Kind = PcmWaveDocumentMigrationKind.Corrupt,
                RecordId = recordId,
                DocumentState = documentState,
                ReplayState = replayState,
                Message = string.IsNullOrWhiteSpace(message) ? "PCM WAV migration failed" : message
            };
        }
    }
}
