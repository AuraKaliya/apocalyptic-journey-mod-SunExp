using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Network;

internal static class ReplayNetworkProtocolV17
{
    internal const int Version = 1;
    internal const int MaximumTransferBytes = 192 * 1024 * 1024;
    internal const int MaximumDecodedTransferBytes = 256 * 1024 * 1024;
    internal const int MaximumChunks = 24_000;
    internal const int MaximumActiveTransfers = 4;
    internal static readonly TimeSpan CapabilityTtl = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan TransferTtl = TimeSpan.FromMinutes(8);
}

[Serializable]
public sealed class ReplayCapabilityCommandV17 : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;
    public int ProtocolVersion { get; set; } = ReplayNetworkProtocolV17.Version;
    public string LevelId { get; set; } = "";
    public List<string> RequiredCapabilities { get; set; } = new();
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(AuraToolsRpcSender sender) => serverSender = sender ?? AuraToolsRpcSender.Unbound;

    public override void CmdExecute()
    {
        Accepted = ReplayNetworkAuthorityV17.AcceptCapabilityOnServer(this, serverSender, out var rejection);
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (!Accepted && !string.IsNullOrWhiteSpace(RejectionReason))
            AuraToolsLog.Warn("[MatchRecords] replay network capability rejected: " + RejectionReason);
    }
}

[Serializable]
public sealed class ReplayCanonicalChunkCommandV17 : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;
    public int ProtocolVersion { get; set; } = ReplayNetworkProtocolV17.Version;
    public string DocumentRoot { get; set; } = "";
    public string TransferId { get; set; } = "";
    public int ChunkIndex { get; set; }
    public int ChunkCount { get; set; }
    public int TotalBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string PayloadBase64 { get; set; } = "";
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(AuraToolsRpcSender sender) => serverSender = sender ?? AuraToolsRpcSender.Unbound;

    public override void CmdExecute()
    {
        Accepted = ReplayNetworkAuthorityV17.AcceptCanonicalChunkOnServer(this, serverSender, out var rejection);
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (Accepted) ReplayNetworkAuthorityV17.ReceiveCanonicalChunk(this);
    }
}

internal static class ReplayNetworkAuthorityV17
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CapabilityReceipt> Capabilities = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ReplayCanonicalChunkBufferV17> Transfers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingReplica> PendingReplicas = new(StringComparer.Ordinal);
    private const long MaximumIncomingBytes = 256L * 1024 * 1024;
    internal static int PendingIncomingCount { get { lock (Gate) return PendingReplicas.Count; } }

    internal static event Action? CapabilityChanged;

    internal static bool NetworkActive => GameApi.AuraToolsNetworkSession.NetworkActive;
    internal static bool IsHost => GameApi.AuraToolsNetworkSession.IsAuthority;

    internal static void AnnounceCapability(string levelId)
    {
        if (!NetworkActive) return;
        var command = new ReplayCapabilityCommandV17
        {
            LevelId = levelId ?? "",
            RequiredCapabilities = ReplayCapabilitiesV17.Required.OrderBy(item => item, StringComparer.Ordinal).ToList()
        };
        if (IsHost)
        {
            lock (Gate)
                Capabilities[GameApi.AuraToolsNetworkSession.LocalPlayerId] = new CapabilityReceipt(
                    command.LevelId,
                    command.ProtocolVersion,
                    command.RequiredCapabilities,
                    DateTime.UtcNow);
            CapabilityChanged?.Invoke();
        }
        AuraToolsRpcTransport.Send(PlayerManager.Instance, command, "MatchRecords.ReplayV17.Capability");
    }

    internal static bool CanHostRecord(string levelId, out string rejection)
    {
        rejection = "";
        if (!NetworkActive) return true;
        if (!IsHost)
        {
            rejection = "local node is not replay authority";
            return false;
        }
        if (!AuraToolsRpcTransport.IsLobbyCompatible(out rejection)) return false;
        var expected = GameServer.Instance?.LobbyInfo?.AddedPlayers?
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        if (expected.Count <= 1) return true;
        lock (Gate)
        {
            PruneNoLock();
            var missing = expected.Where(playerId => !Capabilities.TryGetValue(playerId, out var receipt)
                                                     || receipt.ProtocolVersion != ReplayNetworkProtocolV17.Version
                                                     || !string.Equals(receipt.LevelId, levelId ?? "", StringComparison.Ordinal)
                                                     || ReplayCapabilitiesV17.Required.Any(capability =>
                                                         !receipt.RequiredCapabilities.Contains(capability, StringComparer.Ordinal)))
                .ToList();
            if (missing.Count == 0) return true;
            rejection = "replay protocol negotiation pending or incompatible: " + string.Join(",", missing);
            return false;
        }
    }

    internal static bool AcceptCapabilityOnServer(
        ReplayCapabilityCommandV17 command,
        AuraToolsRpcSender sender,
        out string rejection)
    {
        if (!RequireLobbyMember(sender, out rejection)) return false;
        if (command == null
            || command.ProtocolVersion != ReplayNetworkProtocolV17.Version
            || command.LevelId == null
            || command.LevelId.Length > 256
            || command.RequiredCapabilities == null
            || command.RequiredCapabilities.Count > 32
            || ReplayCapabilitiesV17.Required.Any(capability =>
                !command.RequiredCapabilities.Contains(capability, StringComparer.Ordinal)))
        {
            rejection = "replay capability protocol mismatch";
            return false;
        }
        lock (Gate)
        {
            PruneNoLock();
            Capabilities[sender.PlayerId] = new CapabilityReceipt(
                command.LevelId,
                command.ProtocolVersion,
                command.RequiredCapabilities.ToList(),
                DateTime.UtcNow);
        }
        rejection = "";
        CapabilityChanged?.Invoke();
        return true;
    }

    internal static bool PublishCanonical(MatchRecord record, ReplayDocumentEnvelopeV17 envelope)
    {
        if (record == null || envelope == null || !HasRemoteRecipients()) return true;
        return ReplayBackgroundWork.Finalization.TryEnqueue("NetworkPrepare." + record.RecordId,
            () => PrepareTransfer(ReplayReplicationV17.CreateTransfer(record, envelope)),
            SendPrepared,
            ex => AuraToolsLog.Warn("[MatchRecords] canonical replay replication preparation failed: " + ex.Message),
            ReplayMemoryEstimateV17.Document(envelope.Document));
    }

    private static bool HasRemoteRecipients() => GameApi.AuraToolsNetworkSession.HasRemotePeers && IsHost;

    internal static bool AcceptCanonicalChunkOnServer(
        ReplayCanonicalChunkCommandV17 command,
        AuraToolsRpcSender sender,
        out string rejection)
    {
        if (!RequireLobbyMember(sender, out rejection) || !sender.IsLobbyHost)
        {
            if (string.IsNullOrWhiteSpace(rejection)) rejection = "canonical replay sender is not host";
            return false;
        }
        if (command == null
            || command.ProtocolVersion != ReplayNetworkProtocolV17.Version
            || command.DocumentRoot == null
            || command.DocumentRoot.Length != 64
            || string.IsNullOrWhiteSpace(command.TransferId)
            || command.TransferId.Length > 128
            || command.ChunkCount <= 0
            || command.ChunkCount > ReplayNetworkProtocolV17.MaximumChunks
            || command.ChunkIndex < 0
            || command.ChunkIndex >= command.ChunkCount
            || command.TotalBytes <= 0
            || command.TotalBytes > ReplayNetworkProtocolV17.MaximumTransferBytes
            || command.Sha256 == null
            || command.Sha256.Length != 64
            || string.IsNullOrWhiteSpace(command.PayloadBase64)
            || command.PayloadBase64.Length > 32_768)
        {
            rejection = "canonical replay chunk metadata invalid";
            return false;
        }
        rejection = "";
        return true;
    }

    internal static void ReceiveCanonicalChunk(ReplayCanonicalChunkCommandV17 command)
    {
        if (command == null || IsHost || !AuraToolsMatchRecordsRuntime.ReplayEnabled) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(command.PayloadBase64 ?? ""); }
        catch
        {
            AuraToolsLog.Warn("[MatchRecords] rejected canonical replay chunk with invalid base64.");
            return;
        }
        lock (Gate)
        {
            PruneNoLock();
            if (PendingReplicas.ContainsKey(command.TransferId)) return;
            if (!Transfers.TryGetValue(command.TransferId, out var buffer))
            {
                if (Transfers.Count + PendingReplicas.Count >= ReplayNetworkProtocolV17.MaximumActiveTransfers
                    || Transfers.Values.Sum(item => (long)item.TotalBytes)
                       + PendingReplicas.Values.Sum(item => (long)item.Bytes) + command.TotalBytes > MaximumIncomingBytes)
                {
                    AuraToolsLog.Warn("[MatchRecords] rejected canonical replay transfer: too many active transfers.");
                    return;
                }
                buffer = new ReplayCanonicalChunkBufferV17(
                    command.DocumentRoot,
                    command.TransferId,
                    command.ChunkCount,
                    command.TotalBytes,
                    command.Sha256);
                Transfers[command.TransferId] = buffer;
            }
            if (!buffer.Accepts(
                    command.DocumentRoot,
                    command.TransferId,
                    command.ChunkCount,
                    command.TotalBytes,
                    command.Sha256)
                || !buffer.TrySet(command.ChunkIndex, bytes, AuraToolsRpcTransport.ChunkRawBytes))
            {
                Transfers.Remove(command.TransferId);
                AuraToolsLog.Warn("[MatchRecords] rejected inconsistent canonical replay chunk: " + command.TransferId);
                return;
            }
            if (!buffer.IsComplete) return;
            // Transfer ownership before releasing the assembly buffer. Joining,
            // hashing, journal staging and validation happen on the worker.
            PendingReplicas[command.TransferId] = new PendingReplica(command.TransferId, buffer);
            Transfers.Remove(command.TransferId);
        }
        PumpIncoming();
    }

    private static PreparedTransfer PrepareTransfer(ReplayNetworkTransferV17 transfer)
    {
        var bytes = ReplayPayloadV17.Encode(transfer);
        if (bytes.Length <= 0 || bytes.Length > ReplayNetworkProtocolV17.MaximumTransferBytes)
            throw new InvalidOperationException("canonical replay exceeds network replication budget");
        var chunkCount = (bytes.Length + AuraToolsRpcTransport.ChunkRawBytes - 1) / AuraToolsRpcTransport.ChunkRawBytes;
        if (chunkCount > ReplayNetworkProtocolV17.MaximumChunks)
            throw new InvalidOperationException("canonical replay exceeds network chunk budget");
        return new PreparedTransfer(
            transfer.Envelope.DeclaredDocumentRoot,
            AuraToolsRpcTransport.NewTransferId("replay-v17"),
            bytes);
    }

    private static void SendPrepared(PreparedTransfer prepared)
    {
        // Peers may have left while background encoding was running.
        if (!HasRemoteRecipients()) return;
        var sent = AuraToolsRpcTransport.SendBytesChunksAsync(
            PlayerManager.Instance,
            "MatchRecords.ReplayV17.Canonical",
            prepared.TransferId,
            prepared.Payload,
            chunk => new ReplayCanonicalChunkCommandV17
            {
                DocumentRoot = prepared.DocumentRoot,
                TransferId = chunk.TransferId,
                ChunkIndex = chunk.ChunkIndex,
                ChunkCount = chunk.ChunkCount,
                TotalBytes = chunk.TotalBytes,
                Sha256 = chunk.Sha256,
                PayloadBase64 = chunk.PayloadBase64
            },
            excludeOwner: true);
        if (!sent) AuraToolsLog.Warn("[MatchRecords] canonical replay replication could not be scheduled.");
    }

    internal static void PumpIncoming()
    {
        if (!MatchRecordStorage.Ready) return;
        PendingReplica[] pending;
        lock (Gate) pending = PendingReplicas.Values.Where(item => !item.InFlight && item.RetryAt <= DateTime.UtcNow).ToArray();
        foreach (var incoming in pending)
        {
            var database = MatchRecordStorage.Database;
            var limit = Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit;
            incoming.InFlight = true;
            if (!ReplayBackgroundWork.Storage.TryEnqueue("ReplicaCommit." + incoming.Id, () =>
            {
                if (incoming.Payload == null)
                {
                    incoming.Payload = incoming.Buffer!.Join();
                    incoming.Buffer = null;
                    if (incoming.Payload.Length != incoming.Bytes
                        || !string.Equals(Sha256(incoming.Payload), incoming.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Canonical replay transfer hash mismatch.");
                }
                database.StageIncomingReplay(incoming.Id, incoming.Root, incoming.Payload);
                try
                {
                    var result = ReplayReplicaStoreV17.Commit(database, incoming.Payload, incoming.Root, limit);
                    database.FinishIncomingReplay(incoming.Id);
                    return result;
                }
                catch (InvalidDataException ex)
                {
                    database.FinishIncomingReplay(incoming.Id, ex.Message);
                    throw;
                }
            }, result =>
            {
                lock (Gate) PendingReplicas.Remove(incoming.Id);
                AuraToolsLog.Info("[MatchRecords] " + result);
            }, ex =>
            {
                incoming.InFlight = false;
                if (ex is InvalidDataException)
                {
                    lock (Gate) PendingReplicas.Remove(incoming.Id);
                    AuraToolsLog.Warn("[MatchRecords] incoming replay rejected: " + ex.Message);
                }
                else
                {
                    incoming.RetryAt = DateTime.UtcNow.AddSeconds(15);
                    AuraToolsLog.Warn("[MatchRecords] incoming replay retained for retry: " + incoming.Id + ": " + ex.Message);
                }
            }, incoming.Bytes)) incoming.InFlight = false;
        }
    }

    private sealed class PendingReplica
    {
        internal PendingReplica(string id, ReplayCanonicalChunkBufferV17 buffer)
        {
            Id = id; Buffer = buffer; Root = buffer.DocumentRoot; Hash = buffer.Sha256; Bytes = buffer.TotalBytes;
        }
        internal readonly string Id;
        internal readonly string Root;
        internal readonly string Hash;
        internal readonly int Bytes;
        internal ReplayCanonicalChunkBufferV17? Buffer;
        internal byte[]? Payload;
        internal bool InFlight;
        internal DateTime RetryAt;
    }

    private static bool RequireLobbyMember(AuraToolsRpcSender sender, out string rejection)
    {
        if (sender == null || !sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "missing or non-lobby sender";
            return false;
        }
        rejection = "";
        return true;
    }

    private static void PruneNoLock()
    {
        var capabilityCutoff = DateTime.UtcNow - ReplayNetworkProtocolV17.CapabilityTtl;
        foreach (var key in Capabilities.Where(item => item.Value.ReceivedUtc < capabilityCutoff).Select(item => item.Key).ToList())
            Capabilities.Remove(key);
        var transferCutoff = DateTime.UtcNow - ReplayNetworkProtocolV17.TransferTtl;
        foreach (var key in Transfers.Where(item => item.Value.CreatedUtc < transferCutoff).Select(item => item.Key).ToList())
            Transfers.Remove(key);
    }

    private static string Sha256(byte[] payload)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(payload).Select(item => item.ToString("x2")));
    }

    private sealed class CapabilityReceipt
    {
        internal CapabilityReceipt(string levelId, int protocolVersion, List<string> requiredCapabilities, DateTime receivedUtc)
        {
            LevelId = levelId ?? "";
            ProtocolVersion = protocolVersion;
            RequiredCapabilities = requiredCapabilities ?? new List<string>();
            ReceivedUtc = receivedUtc;
        }
        internal string LevelId { get; }
        internal int ProtocolVersion { get; }
        internal List<string> RequiredCapabilities { get; }
        internal DateTime ReceivedUtc { get; }
    }

    private sealed class PreparedTransfer
    {
        internal PreparedTransfer(string documentRoot, string transferId, byte[] payload)
        {
            DocumentRoot = documentRoot;
            TransferId = transferId;
            Payload = payload;
        }
        internal string DocumentRoot { get; }
        internal string TransferId { get; }
        internal byte[] Payload { get; }
    }

    private sealed class ReplicaStoreResult
    {
        internal ReplicaStoreResult(string message) => Message = message;
        internal string Message { get; }
    }
}
