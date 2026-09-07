using System;
using System.Collections.Generic;
using System.IO;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed class IncomingReplayV17
{
    internal string TransferId { get; set; } = "";
    internal string Root { get; set; } = "";
    internal byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal sealed partial class MatchRecordDatabase
{
    internal void StageIncomingReplay(string transferId, string root, byte[] payload)
    {
        if (string.IsNullOrWhiteSpace(transferId) || root?.Length != 64 || payload == null
            || payload.Length == 0 || payload.Length > ReplayReplicationV17.MaximumTransferBytes)
            throw new InvalidDataException("Invalid incoming replay journal entry.");
        var hash = ReplayCanonicalJsonV17.Sha256(payload);
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using (var insert = connection.Prepare("INSERT OR IGNORE INTO replay_incoming_v17(transfer_id, document_root, payload_sha256, payload, state, error, created_utc) VALUES(?, ?, ?, ?, 'Received', '', ?);"))
            {
                insert.Bind(1, transferId); insert.Bind(2, root); insert.Bind(3, hash);
                insert.Bind(4, payload); insert.Bind(5, DateTime.UtcNow.ToString("O")); insert.Execute();
            }
            using var query = connection.Prepare("SELECT document_root, payload_sha256 FROM replay_incoming_v17 WHERE transfer_id=?;");
            query.Bind(1, transferId);
            if (!query.Read() || !string.Equals(query.Text(0), root, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(query.Text(1), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Incoming replay transfer identity collision.");
        }
    }

    internal List<string> IncomingReplayIds()
    {
        lock (gate)
        {
            EnsureInitialized(); using var connection = Open();
            using var query = connection.Prepare("SELECT transfer_id FROM replay_incoming_v17 WHERE state='Received' ORDER BY created_utc;");
            var ids = new List<string>(); while (query.Read()) ids.Add(query.Text(0)); return ids;
        }
    }

    internal IncomingReplayV17? ReadIncomingReplay(string id)
    {
        lock (gate)
        {
            EnsureInitialized(); using var connection = Open();
            using var query = connection.Prepare("SELECT document_root, payload, payload_sha256 FROM replay_incoming_v17 WHERE transfer_id=? AND state='Received';");
            query.Bind(1, id); if (!query.Read()) return null;
            var payload = query.Blob(1);
            if (payload.Length > ReplayReplicationV17.MaximumTransferBytes
                || !string.Equals(ReplayCanonicalJsonV17.Sha256(payload), query.Text(2), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Incoming replay journal payload is damaged.");
            return new IncomingReplayV17 { TransferId = id, Root = query.Text(0), Payload = payload };
        }
    }

    internal void FinishIncomingReplay(string id, string error = "")
    {
        lock (gate)
        {
            EnsureInitialized(); using var connection = Open();
            if (error.Length == 0)
            {
                using var remove = connection.Prepare("DELETE FROM replay_incoming_v17 WHERE transfer_id=?;");
                remove.Bind(1, id); remove.Execute();
            }
            else
            {
                using var reject = connection.Prepare("UPDATE replay_incoming_v17 SET state='Rejected', payload=X'', error=? WHERE transfer_id=?;");
                reject.Bind(1, error); reject.Bind(2, id); reject.Execute();
                connection.Execute("DELETE FROM replay_incoming_v17 WHERE state='Rejected' AND transfer_id NOT IN (SELECT transfer_id FROM replay_incoming_v17 WHERE state='Rejected' ORDER BY created_utc DESC LIMIT 128);");
            }
        }
    }
}
