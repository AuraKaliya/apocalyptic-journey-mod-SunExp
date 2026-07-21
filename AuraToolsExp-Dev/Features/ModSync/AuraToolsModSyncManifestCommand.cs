using System;
using AuraOnline.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;
using Network.Query;

namespace AuraToolsExp.Dll.Features.ModSync;

[Serializable]
public sealed class AuraToolsModSyncManifestQuery : QueryBase<string>, IAuraToolsImmediateQuery
{
    public int ProtocolVersion { get; set; } = AuraToolsModSyncManifestCommand.CurrentProtocolVersion;

    public bool ResponseDispatched { get; set; }

    public string RequesterPlayerId { get; set; } = "";

    public void BindServerRequester(string playerId)
    {
        RequesterPlayerId = (playerId ?? "").Trim();
    }

    public override void CmdExecute()
    {
        Result = ResponseDispatched
            ? ""
            : AuraToolsModSyncRuntime.CreateTargetedHostManifestPayload(ProtocolVersion, RequesterPlayerId);
    }
}

[Serializable]
public sealed class AuraToolsModSyncManifestQueryResult
{
    public AuraChatModPlayerSnapshot? HostManifest { get; set; }

    public string RejectionReason { get; set; } = "";
}

[Serializable]
public sealed class AuraToolsModSyncManifestCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    public const int CurrentProtocolVersion = 1;

    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;

    public string RequesterPlayerId { get; set; } = "";

    public AuraChatModPlayerSnapshot? HostManifest { get; set; }

    public bool HostManifestChunked { get; set; }

    public string TransferId { get; set; } = "";

    public string RejectionReason { get; set; } = "";

    public void BindServerSender(AuraToolsRpcSender sender)
    {
        serverSender = sender ?? AuraToolsRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (ProtocolVersion != CurrentProtocolVersion)
        {
            RejectionReason = "协议版本不匹配。";
            HostManifest = null;
            return;
        }

        if (!AuraToolsModSyncRuntime.TryCreateHostModManifest(serverSender, out var manifest, out var rejection))
        {
            RejectionReason = rejection;
            HostManifest = null;
            AuraToolsLog.Warn("[ModSync] host manifest rejected: " + rejection);
            return;
        }

        HostManifest = manifest;
        RejectionReason = "";
        HostManifestChunked = false;
        TransferId = "";
        if (!AuraToolsRpcPayloadGuard.FitsSoftLimit(
                this,
                AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
                out var bytes,
                out _))
        {
            var payloadJson = AuraSharedJson.Serialize(manifest);
            var transferId = AuraToolsRpcTransport.NewTransferId("modsync-manifest");
            if (!AuraToolsModSyncRuntime.TrySendHostModManifestChunks(
                    serverSender,
                    RequesterPlayerId,
                    transferId,
                    payloadJson,
                    out var chunkRejection))
            {
                HostManifest = null;
                RejectionReason = chunkRejection;
                AuraToolsLog.Warn("[ModSync] host manifest omitted: " + chunkRejection + ". bytes=" + bytes);
                return;
            }

            HostManifest = null;
            HostManifestChunked = true;
            TransferId = transferId;
            AuraToolsLog.Warn("[ModSync] host manifest chunked. transfer="
                              + transferId
                              + ", bytes="
                              + bytes
                              + ", softLimit="
                              + AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes);
        }
    }

    public override void RpcExecute()
    {
        AuraToolsModSyncRuntime.ReceiveHostModManifest(this);
    }
}

[Serializable]
public sealed class AuraToolsModSyncManifestChunkCommand : RpcCommandBase
{
    public int ProtocolVersion { get; set; } = AuraToolsModSyncManifestCommand.CurrentProtocolVersion;

    public string RequesterPlayerId { get; set; } = "";

    public string TransferId { get; set; } = "";

    public int ChunkIndex { get; set; }

    public int ChunkCount { get; set; }

    public int TotalBytes { get; set; }

    public string Sha256 { get; set; } = "";

    public string PayloadBase64 { get; set; } = "";

    public override void RpcExecute()
    {
        AuraToolsModSyncRuntime.ReceiveHostModManifestChunk(this);
    }
}
