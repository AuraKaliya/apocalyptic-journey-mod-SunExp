using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed class ReplayCaptureBatchV17
{
    public int BatchIndex { get; set; }
    public long FirstSequence { get; set; }
    public long LastSequence { get; set; }
    public List<ReplayJournalEventV17> TruthEvents { get; set; } = new();
    public List<ReplayJournalEventV17> PresentationEvents { get; set; } = new();
    public ReplayPresentationCapsuleV17? Presentation { get; set; }
    public List<ReplayAssetV17> Assets { get; set; } = new();
    public string BatchSha256 { get; set; } = "";
}

internal sealed class ReplayCaptureSeedV17
{
    public ReplayDocumentHeaderCoreV17 Header { get; set; } = new();
    public ReplayVisibleStateV17 InitialState { get; set; } = new();
}

internal sealed class ReplayFinalizationDraftV17
{
    public MatchRecord Record { get; set; } = new();
    public ReplayDocumentEnvelopeV17 Envelope { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
    public List<ReplayFinalizationAssetPayloadV17> AssetPayloads { get; set; } = new();
}

internal sealed class ReplayFinalizationAssetPayloadV17
{
    public string Sha256 { get; set; } = "";
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal sealed partial class MatchRecordDatabase
{
    internal void BeginCaptureV17(
        MatchRecord record,
        ReplayDocumentHeaderCoreV17 header,
        ReplayVisibleStateV17 initialState,
        ReplayCaptureBatchV17 firstBatch)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.RecordId))
            throw new ArgumentException("Replay capture record identity is missing.", nameof(record));
        ValidateCaptureBatchV17(firstBatch);
        var seed = new ReplayCaptureSeedV17
        {
            Header = ReplayCanonicalJsonV17.Clone(header ?? new ReplayDocumentHeaderCoreV17()),
            InitialState = ReplayStateReducerV17.Normalize(initialState)
        };
        var seedPayload = ReplayPayloadV17.Encode(seed);
        var batchPayload = ReplayPayloadV17.Encode(firstBatch);
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (Exists(connection, record.RecordId))
                    throw new InvalidDataException("Replay capture record already exists: " + record.RecordId);
                record.ReplayProtocol = ReplayProtocolV17.DocumentVersion;
                record.ReplayState = MatchReplayStates.Recording;
                InsertRecordV17(connection, record, compressedBytes: batchPayload.LongLength + seedPayload.LongLength);
                using (var session = connection.Prepare(
                           "INSERT INTO replay_capture_sessions(record_id, capture_state, revision, created_utc, updated_utc, "
                           + "seed_payload, final_payload, final_sha256) VALUES(?, 'Recording', ?, ?, ?, ?, X'', '');"))
                {
                    var now = DateTime.UtcNow.ToString("O");
                    session.Bind(1, record.RecordId);
                    session.Bind(2, firstBatch.BatchIndex);
                    session.Bind(3, now);
                    session.Bind(4, now);
                    session.Bind(5, seedPayload);
                    session.Execute();
                }
                InsertCaptureBatchV17(connection, record.RecordId, firstBatch, batchPayload);
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal bool AppendCaptureBatchV17(string recordId, ReplayCaptureBatchV17 batch)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return false;
        ValidateCaptureBatchV17(batch);
        var payload = ReplayPayloadV17.Encode(batch);
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                using (var state = connection.Prepare(
                           "SELECT capture_state FROM replay_capture_sessions WHERE record_id=? LIMIT 1;"))
                {
                    state.Bind(1, recordId);
                    if (!state.Read())
                    {
                        connection.Execute("ROLLBACK;");
                        return false;
                    }
                }
                using (var existing = connection.Prepare(
                           "SELECT batch_sha256 FROM replay_capture_batches WHERE record_id=? AND batch_index=? LIMIT 1;"))
                {
                    existing.Bind(1, recordId);
                    existing.Bind(2, batch.BatchIndex);
                    if (existing.Read())
                    {
                        var matches = string.Equals(existing.Text(0), batch.BatchSha256, StringComparison.OrdinalIgnoreCase);
                        connection.Execute(matches ? "COMMIT;" : "ROLLBACK;");
                        if (!matches) throw new InvalidDataException("Replay capture batch identity collision.");
                        return true;
                    }
                }
                InsertCaptureBatchV17(connection, recordId, batch, payload);
                using (var update = connection.Prepare(
                           "UPDATE replay_capture_sessions SET revision=MAX(revision, ?), updated_utc=? WHERE record_id=?;"))
                {
                    update.Bind(1, batch.BatchIndex);
                    update.Bind(2, DateTime.UtcNow.ToString("O"));
                    update.Bind(3, recordId);
                    update.Execute();
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

    internal void SaveFinalizingCaptureV17(
        MatchRecord record,
        ReplayDocumentEnvelopeV17 envelope,
        IReadOnlyCollection<string> diagnostics)
    {
        if (record == null || envelope?.Document == null)
            throw new ArgumentNullException(record == null ? nameof(record) : nameof(envelope));
        var assetBytes = envelope.Document.Assets.Sum(item => Math.Max(0L, item?.ByteLength ?? 0L));
        if (assetBytes > ReplayLimitsV17.MaximumAssetBytes)
            throw new InvalidDataException("Replay finalization draft exceeds the dynamic asset budget.");
        foreach (var asset in envelope.Document.Assets.Where(item => item?.Payload?.Length > 0))
        {
            var error = ReplayAssetContractV17.Validate(asset, requirePayload: true);
            if (error.Length > 0)
                throw new InvalidDataException("Replay finalization asset is invalid: " + asset.Sha256 + ":" + error);
        }
        var draft = new ReplayFinalizationDraftV17
        {
            Record = ReplayCanonicalJsonV17.Clone(record),
            Envelope = ReplayCanonicalJsonV17.Clone(envelope),
            Diagnostics = (diagnostics ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList(),
            AssetPayloads = envelope.Document.Assets
                .Where(item => item?.Payload?.Length > 0)
                .Select(item => new ReplayFinalizationAssetPayloadV17
                {
                    Sha256 = item.Sha256,
                    Payload = (byte[])item.Payload.Clone()
                })
                .ToList()
        };
        var payload = ReplayPayloadV17.Encode(draft);
        var hash = ReplayCanonicalJsonV17.Sha256(payload);
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                using (var exists = connection.Prepare(
                           "SELECT 1 FROM replay_capture_sessions WHERE record_id=? LIMIT 1;"))
                {
                    exists.Bind(1, record.RecordId);
                    if (!exists.Read())
                        throw new InvalidDataException("Replay capture session is missing at finalization.");
                }
                using (var session = connection.Prepare(
                           "UPDATE replay_capture_sessions SET capture_state='Finalizing', updated_utc=?, final_payload=?, "
                           + "final_sha256=? WHERE record_id=?;"))
                {
                    session.Bind(1, DateTime.UtcNow.ToString("O"));
                    session.Bind(2, payload);
                    session.Bind(3, hash);
                    session.Bind(4, record.RecordId);
                    session.Execute();
                }
                record.ReplayState = MatchReplayStates.Finalizing;
                using (var update = connection.Prepare(
                           "UPDATE battle_records SET replay_state='Finalizing', result=?, ended_utc=?, event_count=?, turn_count=?, "
                           + "compressed_bytes=?, statistics_payload=?, metadata_payload=? WHERE record_id=?;"))
                {
                    update.Bind(1, record.Result ?? "");
                    update.Bind(2, record.EndedUtc ?? "");
                    update.Bind(3, Math.Max(0, record.EventCount));
                    update.Bind(4, Math.Max(0, record.TurnCount));
                    update.Bind(5, payload.LongLength);
                    update.Bind(6, MatchReplayPayload.Encode(record.StatisticsJson ?? ""));
                    update.Bind(7, MatchReplayPayload.Encode(CreateMetadata(record)));
                    update.Bind(8, record.RecordId);
                    update.Execute();
                }
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int RecoverFinalizingCapturesV17()
    {
        var drafts = new List<ReplayFinalizationDraftV17>();
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using (var query = connection.Prepare(
                       "SELECT final_payload, final_sha256 FROM replay_capture_sessions "
                       + "WHERE capture_state='Finalizing' AND length(final_payload)>0 ORDER BY updated_utc;"))
                while (query.Read())
                {
                    var payload = query.Blob(0);
                    if (!string.Equals(ReplayCanonicalJsonV17.Sha256(payload), query.Text(1), StringComparison.OrdinalIgnoreCase))
                        continue;
                    drafts.Add(ReplayPayloadV17.Decode<ReplayFinalizationDraftV17>(payload));
                }
            connection.Execute("UPDATE replay_capture_sessions SET capture_state='Incomplete' WHERE capture_state='Recording';");
            connection.Execute("UPDATE battle_records SET replay_state='Incomplete' WHERE replay_state='Recording';");
        }

        var recovered = 0;
        foreach (var draft in drafts)
        {
            try
            {
                var payloads = draft.AssetPayloads.ToDictionary(item => item.Sha256, StringComparer.OrdinalIgnoreCase);
                foreach (var asset in draft.Envelope.Document.Assets)
                    if (payloads.TryGetValue(asset.Sha256, out var payload))
                        asset.Payload = (byte[])payload.Payload.Clone();
                var validation = ReplayDocumentFinalizerV17.FinalizeAndValidate(draft.Envelope);
                if (!validation.IsValid) continue;
                draft.Record.ContentSha256 = draft.Envelope.DeclaredDocumentRoot;
                var analysis = MatchAnalysisBuilder.BuildV17(draft.Record, draft.Envelope.Document);
                if (SaveV17(draft.Record, draft.Envelope, analysis)) recovered++;
            }
            catch
            {
                // Keep the finalizing draft intact for a later recovery attempt.
            }
        }
        return recovered;
    }

    private static void InsertCaptureBatchV17(
        WinSqliteConnection connection,
        string recordId,
        ReplayCaptureBatchV17 batch,
        byte[] payload)
    {
        using var insert = connection.Prepare(
            "INSERT INTO replay_capture_batches(record_id, batch_index, first_sequence, last_sequence, batch_sha256, payload) "
            + "VALUES(?, ?, ?, ?, ?, ?);");
        insert.Bind(1, recordId);
        insert.Bind(2, batch.BatchIndex);
        insert.Bind(3, batch.FirstSequence);
        insert.Bind(4, batch.LastSequence);
        insert.Bind(5, batch.BatchSha256);
        insert.Bind(6, payload);
        insert.Execute();
    }

    private static void ValidateCaptureBatchV17(ReplayCaptureBatchV17 batch)
    {
        if (batch == null || batch.BatchIndex < 0) throw new InvalidDataException("Replay capture batch is invalid.");
        var events = (batch.TruthEvents ?? new List<ReplayJournalEventV17>())
            .Concat(batch.PresentationEvents ?? new List<ReplayJournalEventV17>())
            .OrderBy(item => item.Sequence)
            .ToList();
        if (events.Count == 0
            || batch.FirstSequence != events[0].Sequence
            || batch.LastSequence != events[events.Count - 1].Sequence
            || events.Select(item => item.Sequence).Distinct().Count() != events.Count)
            throw new InvalidDataException("Replay capture batch sequence range is invalid.");
        var clone = ReplayCanonicalJsonV17.Clone(batch);
        clone.BatchSha256 = "";
        var expected = ReplayCanonicalJsonV17.Sha256(clone);
        if (string.IsNullOrWhiteSpace(batch.BatchSha256)) batch.BatchSha256 = expected;
        else if (!string.Equals(batch.BatchSha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replay capture batch hash is invalid.");
    }
}
