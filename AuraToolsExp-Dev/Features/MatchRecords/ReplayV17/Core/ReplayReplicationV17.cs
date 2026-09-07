using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayReplicationV17
{
    internal const int MaximumTransferBytes = 192 * 1024 * 1024;
    internal const int MaximumDecodedTransferBytes = 256 * 1024 * 1024;
    internal static bool HasRemoteAudience(bool isHost, string localPlayerId, IEnumerable<string>? lobbyPlayerIds) =>
        isHost && !string.IsNullOrWhiteSpace(localPlayerId)
        && (lobbyPlayerIds ?? Array.Empty<string>()).Any(id =>
            !string.IsNullOrWhiteSpace(id) && !string.Equals(id.Trim(), localPlayerId.Trim(), StringComparison.OrdinalIgnoreCase));

    // The validated document has been detached from gameplay. Transfer encoding
    // reads it without cloning the entire journal through JSON a second time.
    internal static ReplayNetworkTransferV17 CreateTransfer(MatchRecord record, ReplayDocumentEnvelopeV17 envelope) => new()
    {
        Record = record,
        Envelope = envelope,
        AssetPayloads = ReplayAssetPayloadTransferV17.Capture(envelope.Document)
    };
}

internal sealed class ReplayNetworkTransferV17
{
    public MatchRecord Record { get; set; } = new();
    public ReplayDocumentEnvelopeV17 Envelope { get; set; } = new();
    public ReplayAssetPayloadSetV17 AssetPayloads { get; set; } = new();
}
