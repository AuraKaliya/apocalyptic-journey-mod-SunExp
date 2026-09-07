using System;
using System.IO;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static class ReplayReplicaStoreV17
{
    internal static string Commit(MatchRecordDatabase database, byte[] payload, string root, int limit)
    {
        var transfer = ReplayPayloadV17.Decode<ReplayNetworkTransferV17>(payload, ReplayReplicationV17.MaximumDecodedTransferBytes);
        if (transfer.Record == null || transfer.Envelope?.Document == null || transfer.AssetPayloads == null
            || !string.Equals(transfer.Envelope.DeclaredDocumentRoot, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Canonical replay transfer shape or root is invalid.");
        try { ReplayAssetPayloadTransferV17.AttachAndValidate(transfer.Envelope.Document, transfer.AssetPayloads); }
        catch (InvalidOperationException ex) { throw new InvalidDataException(ex.Message, ex); }
        var validation = ReplayDocumentValidatorV17.Validate(transfer.Envelope);
        if (!validation.IsValid) throw new InvalidDataException(validation.Message);
        var id = transfer.Envelope.Document.Header.RecordId;
        var existing = database.Get(id);
        if (existing != null)
        {
            if (string.Equals(existing.ContentSha256, root, StringComparison.OrdinalIgnoreCase)) return "联机权威回放已存在，根哈希一致。";
            throw new InvalidDataException("Canonical replay record identity collision.");
        }
        var record = transfer.Record;
        record.Collection = MatchRecordCollections.Auto; record.IsFavorite = false;
        record.Origin = MatchRecordOrigins.Replicated; record.ReplayState = MatchReplayStates.Ready;
        record.ReplayProtocol = ReplayProtocolV17.DocumentVersion; record.ContentSha256 = root;
        if (!database.SaveV17(record, transfer.Envelope, MatchAnalysisBuilder.BuildV17(record, transfer.Envelope.Document)))
            throw new IOException("Canonical replay replica was not committed.");
        database.EnforceAutoLimit(limit);
        return "联机权威回放已验证并提交：" + record.RecordId + "。";
    }

    internal static int Recover(MatchRecordDatabase database, int limit, Action<string>? rejected = null)
    {
        var count = 0;
        foreach (var id in database.IncomingReplayIds())
        {
            try
            {
                var incoming = database.ReadIncomingReplay(id);
                if (incoming == null) continue;
                Commit(database, incoming.Payload, incoming.Root, limit);
                database.FinishIncomingReplay(id); count++;
            }
            catch (InvalidDataException ex)
            {
                database.FinishIncomingReplay(id, ex.Message); rejected?.Invoke(ex.Message);
            }
        }
        return count;
    }
}
