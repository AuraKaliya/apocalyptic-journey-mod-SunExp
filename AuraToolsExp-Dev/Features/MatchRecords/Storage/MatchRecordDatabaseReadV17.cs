using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    internal ReplayDocumentEnvelopeV17? LoadV17(string recordId, bool loadAssetPayloads = false)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return null;
        ReplayDocumentEnvelopeV17 envelope;
        IReadOnlyList<ReplayJournalChunkV17> truth;
        IReadOnlyList<ReplayJournalChunkV17> presentation;
        List<(string Hash, byte[] Payload)> truthPoints;
        List<(string Hash, byte[] Payload)> presentationPoints;
        lock (gate)
        {
            EnsureInitialized(); using var connection = Open();
            using (var query = connection.Prepare("SELECT document_version, document_state, document_root, document_payload FROM replay_documents WHERE record_id=? LIMIT 1;"))
            {
                query.Bind(1, recordId.Trim());
                if (!query.Read() || query.Int64(0) != ReplayProtocolV17.DocumentVersion
                    || !string.Equals(query.Text(1), MatchReplayStates.Ready, StringComparison.Ordinal)) return null;
                envelope = ReplayPayloadV17.Decode<ReplayDocumentEnvelopeV17>(query.Blob(3));
                if (!string.Equals(envelope.DeclaredDocumentRoot, query.Text(2), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Stored replay root does not match its envelope.");
            }
            truth = ReadChunksV17(connection, "replay_truth_chunks", recordId, ReplayJournalLanesV17.Truth);
            presentation = ReadChunksV17(connection, "replay_presentation_chunks", recordId, ReplayJournalLanesV17.Presentation);
            truthPoints = ReadCheckpointBlobs(connection, "replay_truth_checkpoints", recordId);
            presentationPoints = ReadCheckpointBlobs(connection, "replay_presentation_checkpoints", recordId);
            // Keep attachment reads under the store lock so deletion cannot
            // retire a referenced file between the metadata and payload reads.
            if (loadAssetPayloads)
                foreach (var asset in envelope.Document.Assets)
                {
                    var path = AttachmentPathV17(asset);
                    if (File.Exists(path)) asset.Payload = File.ReadAllBytes(path);
                }
        }
        // Decompression, object reconstruction and validation own detached
        // bytes, and do not hold up concurrent store commands.
        envelope.Document.TruthEvents = ReplayJournalChunkerV17.Decode(ReplayJournalLanesV17.Truth, truth).ToList();
        envelope.Document.PresentationEvents = ReplayJournalChunkerV17.Decode(ReplayJournalLanesV17.Presentation, presentation).ToList();
        envelope.Document.TruthCheckpoints = truthPoints.Select(pair =>
        {
            var value = ReplayPayloadV17.Decode<ReplayTruthCheckpointV17>(pair.Payload);
            if (!string.Equals(value.CheckpointSha256, pair.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Stored truth checkpoint hash mismatch.");
            return value;
        }).ToList();
        envelope.Document.PresentationCheckpoints = presentationPoints.Select(pair =>
        {
            var value = ReplayPayloadV17.Decode<ReplayPresentationCheckpointV17>(pair.Payload);
            if (!string.Equals(value.CheckpointSha256, pair.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Stored presentation checkpoint hash mismatch.");
            return value;
        }).ToList();
        var validation = ReplayDocumentValidatorV17.Validate(envelope);
        if (!validation.IsValid) throw new InvalidDataException("Stored Replay Document v17 is invalid: " + validation.Message);
        return envelope;
    }

    private static List<(string Hash, byte[] Payload)> ReadCheckpointBlobs(WinSqliteConnection connection, string table, string recordId)
    {
        using var query = connection.Prepare("SELECT sha256, payload FROM " + table + " WHERE record_id=? ORDER BY event_sequence;");
        query.Bind(1, recordId);
        var result = new List<(string, byte[])>();
        while (query.Read()) result.Add((query.Text(0), query.Blob(1)));
        return result;
    }
}
