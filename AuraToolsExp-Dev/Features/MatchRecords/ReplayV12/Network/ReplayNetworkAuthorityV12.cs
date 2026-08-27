using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Network;

internal static class ReplayNetworkProtocolV12
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
public sealed class ReplayCapabilityCommandV12 : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;
    public int ProtocolVersion { get; set; } = ReplayNetworkProtocolV12.Version;
    public string LevelId { get; set; } = "";
    public List<string> RequiredCapabilities { get; set; } = new();
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(AuraToolsRpcSender sender) => serverSender = sender ?? AuraToolsRpcSender.Unbound;

    public override void CmdExecute()
    {
        Accepted = ReplayNetworkAuthorityV12.AcceptCapabilityOnServer(this, serverSender, out var rejection);
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (!Accepted && !string.IsNullOrWhiteSpace(RejectionReason))
            AuraToolsLog.Warn("[MatchRecords] replay network capability rejected: " + RejectionReason);
    }
}

[Serializable]
public sealed class ReplayCanonicalChunkCommandV12 : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;
    public int ProtocolVersion { get; set; } = ReplayNetworkProtocolV12.Version;
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
        Accepted = ReplayNetworkAuthorityV12.AcceptCanonicalChunkOnServer(this, serverSender, out var rejection);
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (Accepted) ReplayNetworkAuthorityV12.ReceiveCanonicalChunk(this);
    }
}

internal static class ReplayNetworkAuthorityV12
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CapabilityReceipt> Capabilities = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ReplayCanonicalChunkBufferV12> Transfers = new(StringComparer.Ordinal);

    internal static bool IsMultiplayer => DamageMeterNetworkRuntime.IsMultiplayer;
    internal static bool IsHost => DamageMeterNetworkRuntime.IsHost;

    internal static void AnnounceCapability(string levelId)
    {
        if (!IsMultiplayer) return;
        var command = new ReplayCapabilityCommandV12
        {
            LevelId = levelId ?? "",
            RequiredCapabilities = ReplayCapabilitiesV12.Required.OrderBy(item => item, StringComparer.Ordinal).ToList()
        };
        if (IsHost)
        {
            lock (Gate)
                Capabilities[DamageMeterNetworkRuntime.LocalPlayerId] = new CapabilityReceipt(
                    command.LevelId,
                    command.ProtocolVersion,
                    command.RequiredCapabilities,
                    DateTime.UtcNow);
        }
        AuraToolsRpcTransport.Send(PlayerManager.Instance, command, "MatchRecords.ReplayV12.Capability");
    }

    internal static bool CanHostRecord(string levelId, out string rejection)
    {
        rejection = "";
        if (!IsMultiplayer) return true;
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
                                                     || receipt.ProtocolVersion != ReplayNetworkProtocolV12.Version
                                                     || !string.Equals(receipt.LevelId, levelId ?? "", StringComparison.Ordinal)
                                                     || ReplayCapabilitiesV12.Required.Any(capability =>
                                                         !receipt.RequiredCapabilities.Contains(capability, StringComparer.Ordinal)))
                .ToList();
            if (missing.Count == 0) return true;
            rejection = "replay protocol negotiation pending or incompatible: " + string.Join(",", missing);
            return false;
        }
    }

    internal static bool AcceptCapabilityOnServer(
        ReplayCapabilityCommandV12 command,
        AuraToolsRpcSender sender,
        out string rejection)
    {
        if (!RequireLobbyMember(sender, out rejection)) return false;
        if (command == null
            || command.ProtocolVersion != ReplayNetworkProtocolV12.Version
            || command.LevelId == null
            || command.LevelId.Length > 256
            || command.RequiredCapabilities == null
            || command.RequiredCapabilities.Count > 32
            || ReplayCapabilitiesV12.Required.Any(capability =>
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
        return true;
    }

    internal static void PublishCanonical(MatchRecord record, ReplayDocumentEnvelopeV12 envelope)
    {
        if (!IsMultiplayer || !IsHost || record == null || envelope == null) return;
        var transfer = new ReplayNetworkTransferV12
        {
            Record = ReplayCanonicalJsonV12.Clone(record),
            Envelope = ReplayCanonicalJsonV12.Clone(envelope),
            AssetPayloads = ReplayAssetPayloadTransferV12.Capture(envelope.Document)
        };
        AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<PreparedTransfer>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV12.NetworkPrepare." + record.RecordId,
            Source = "MatchRecords.ReplayV12.NetworkPrepare",
            Kind = AuraSharedBackgroundWorkKind.Cpu,
            Work = _ => PrepareTransfer(transfer),
            ApplyOnMainThread = prepared => SendPrepared(prepared),
            OnFailedOnMainThread = ex => AuraToolsLog.Warn("[MatchRecords] canonical replay replication preparation failed: " + ex.Message)
        });
    }

    internal static bool AcceptCanonicalChunkOnServer(
        ReplayCanonicalChunkCommandV12 command,
        AuraToolsRpcSender sender,
        out string rejection)
    {
        if (!RequireLobbyMember(sender, out rejection) || !sender.IsLobbyHost)
        {
            if (string.IsNullOrWhiteSpace(rejection)) rejection = "canonical replay sender is not host";
            return false;
        }
        if (command == null
            || command.ProtocolVersion != ReplayNetworkProtocolV12.Version
            || command.DocumentRoot == null
            || command.DocumentRoot.Length != 64
            || string.IsNullOrWhiteSpace(command.TransferId)
            || command.TransferId.Length > 128
            || command.ChunkCount <= 0
            || command.ChunkCount > ReplayNetworkProtocolV12.MaximumChunks
            || command.ChunkIndex < 0
            || command.ChunkIndex >= command.ChunkCount
            || command.TotalBytes <= 0
            || command.TotalBytes > ReplayNetworkProtocolV12.MaximumTransferBytes
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

    internal static void ReceiveCanonicalChunk(ReplayCanonicalChunkCommandV12 command)
    {
        if (command == null || IsHost || !AuraToolsMatchRecordsRuntime.ReplayEnabled) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(command.PayloadBase64 ?? ""); }
        catch
        {
            AuraToolsLog.Warn("[MatchRecords] rejected canonical replay chunk with invalid base64.");
            return;
        }
        byte[]? completed = null;
        lock (Gate)
        {
            PruneNoLock();
            if (!Transfers.TryGetValue(command.TransferId, out var buffer))
            {
                if (Transfers.Count >= ReplayNetworkProtocolV12.MaximumActiveTransfers)
                {
                    AuraToolsLog.Warn("[MatchRecords] rejected canonical replay transfer: too many active transfers.");
                    return;
                }
                buffer = new ReplayCanonicalChunkBufferV12(
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
            Transfers.Remove(command.TransferId);
            completed = buffer.Join();
            if (completed.Length != buffer.TotalBytes
                || !string.Equals(Sha256(completed), buffer.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                AuraToolsLog.Warn("[MatchRecords] canonical replay transfer hash mismatch: " + command.TransferId);
                return;
            }
        }
        QueueReplicaCommit(completed, command.DocumentRoot);
    }

    private static PreparedTransfer PrepareTransfer(ReplayNetworkTransferV12 transfer)
    {
        var bytes = ReplayPayloadV12.Encode(transfer);
        if (bytes.Length <= 0 || bytes.Length > ReplayNetworkProtocolV12.MaximumTransferBytes)
            throw new InvalidOperationException("canonical replay exceeds network replication budget");
        var chunkCount = (bytes.Length + AuraToolsRpcTransport.ChunkRawBytes - 1) / AuraToolsRpcTransport.ChunkRawBytes;
        if (chunkCount > ReplayNetworkProtocolV12.MaximumChunks)
            throw new InvalidOperationException("canonical replay exceeds network chunk budget");
        return new PreparedTransfer(
            transfer.Envelope.DeclaredDocumentRoot,
            AuraToolsRpcTransport.NewTransferId("replay-v12"),
            bytes);
    }

    private static void SendPrepared(PreparedTransfer prepared)
    {
        var sent = AuraToolsRpcTransport.SendBytesChunksAsync(
            PlayerManager.Instance,
            "MatchRecords.ReplayV12.Canonical",
            prepared.TransferId,
            prepared.Payload,
            chunk => new ReplayCanonicalChunkCommandV12
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

    private static void QueueReplicaCommit(byte[] payload, string declaredRoot)
    {
        AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<ReplicaStoreResult>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV12.ReplicaCommit." + declaredRoot,
            Source = "MatchRecords.ReplayV12.ReplicaCommit",
            Kind = AuraSharedBackgroundWorkKind.Io,
            Work = _ => StoreReplica(payload, declaredRoot),
            ApplyOnMainThread = result => AuraToolsLog.Info("[MatchRecords] " + result.Message),
            OnFailedOnMainThread = ex => AuraToolsLog.Warn("[MatchRecords] canonical replay replica rejected: " + ex.Message)
        });
    }

    private static ReplicaStoreResult StoreReplica(byte[] payload, string declaredRoot)
    {
        var transfer = ReplayPayloadV12.Decode<ReplayNetworkTransferV12>(
            payload,
            ReplayNetworkProtocolV12.MaximumDecodedTransferBytes);
        if (transfer.Record == null
            || transfer.Envelope?.Document == null
            || transfer.AssetPayloads == null)
            throw new InvalidOperationException("canonical replay transfer shape is invalid");
        if (!string.Equals(transfer.Envelope.DeclaredDocumentRoot, declaredRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("canonical replay transfer root mismatch");
        ReplayAssetPayloadTransferV12.AttachAndValidate(transfer.Envelope.Document, transfer.AssetPayloads);
        var validation = ReplayDocumentValidatorV12.Validate(transfer.Envelope);
        if (!validation.IsValid) throw new InvalidOperationException("canonical replay validation failed: " + validation.Message);
        var database = MatchRecordStorage.Database;
        var existing = database.Get(transfer.Envelope.Document.Header.RecordId);
        if (existing != null)
        {
            if (string.Equals(existing.ContentSha256, declaredRoot, StringComparison.OrdinalIgnoreCase))
                return new ReplicaStoreResult("联机权威回放已存在，根哈希一致。");
            throw new InvalidOperationException("canonical replay record id collision");
        }
        var record = ReplayCanonicalJsonV12.Clone(transfer.Record);
        record.Collection = MatchRecordCollections.Auto;
        record.IsFavorite = false;
        record.Origin = MatchRecordOrigins.Replicated;
        record.ReplayState = MatchReplayStates.Ready;
        record.ReplayProtocol = ReplayProtocolV12.DocumentVersion;
        record.ContentSha256 = declaredRoot;
        var analysis = MatchAnalysisBuilder.BuildV12(record, transfer.Envelope.Document);
        if (!database.SaveV12(record, transfer.Envelope, analysis))
            throw new InvalidOperationException("canonical replay replica database commit failed");
        database.EnforceAutoLimit(AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit);
        return new ReplicaStoreResult("联机权威回放已验证并提交：" + record.RecordId + "，root=" + declaredRoot + "。");
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
        var capabilityCutoff = DateTime.UtcNow - ReplayNetworkProtocolV12.CapabilityTtl;
        foreach (var key in Capabilities.Where(item => item.Value.ReceivedUtc < capabilityCutoff).Select(item => item.Key).ToList())
            Capabilities.Remove(key);
        var transferCutoff = DateTime.UtcNow - ReplayNetworkProtocolV12.TransferTtl;
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

internal sealed class ReplayNetworkTransferV12
{
    public MatchRecord Record { get; set; } = new();
    public ReplayDocumentEnvelopeV12 Envelope { get; set; } = new();
    public ReplayAssetPayloadSetV12 AssetPayloads { get; set; } = new();
}
