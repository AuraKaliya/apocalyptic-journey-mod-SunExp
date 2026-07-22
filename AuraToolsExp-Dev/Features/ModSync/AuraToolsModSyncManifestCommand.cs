using System;
using AuraOnline.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;
using Network.Query;

namespace AuraToolsExp.Dll.Features.ModSync;

[Serializable]
public sealed class AuraToolsModSyncManifestQuery : QueryBase<string>
{
    public override void CmdExecute()
    {
        Result = "";
    }
}

[Serializable]
public sealed class AuraToolsModSyncManifestQueryResult
{
    public int ProtocolVersion { get; set; } = AuraToolsModSyncManifestCommand.CurrentProtocolVersion;

    public string RequesterPlayerId { get; set; } = "";

    public string RequestId { get; set; } = "";

    public string HostToolVersion { get; set; } = "";

    public AuraChatModPlayerSnapshot? HostManifest { get; set; }

    public string RejectionReason { get; set; } = "";
}

[Serializable]
public sealed class AuraToolsModSyncManifestCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    public const int CurrentProtocolVersion = 2;

    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;

    public string RequesterPlayerId { get; set; } = "";

    public string RequestId { get; set; } = "";

    public string RequesterToolVersion { get; set; } = "";

    public string HostToolVersion { get; set; } = "";

    public uint TargetQueryId { get; set; }

    public bool ForceBroadcastResponse { get; set; }

    public bool TargetedResponseDispatched { get; set; }

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
        RequesterPlayerId = serverSender.PlayerId;
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

        if (manifest == null)
        {
            RejectionReason = "房主MOD配置为空。";
            HostManifest = null;
            return;
        }

        HostManifest = manifest;
        HostToolVersion = AuraToolsModSyncRuntime.FindToolVersion(manifest);
        if (!string.IsNullOrWhiteSpace(RequesterToolVersion)
            && !string.IsNullOrWhiteSpace(HostToolVersion)
            && !string.Equals(RequesterToolVersion, HostToolVersion, StringComparison.OrdinalIgnoreCase))
        {
            AuraToolsLog.Warn("[ModSync] requester tool version differs: requester="
                              + RequesterToolVersion
                              + ", host="
                              + HostToolVersion
                              + ".");
        }

        RejectionReason = "";
        HostManifestChunked = false;
        TransferId = "";
        var targetedRejection = "targeted response was not requested";
        if (!ForceBroadcastResponse
            && TargetQueryId != 0
            && AuraToolsModSyncRuntime.TrySendTargetedHostModManifest(
                RequesterPlayerId,
                RequestId,
                TargetQueryId,
                HostToolVersion,
                manifest,
                out targetedRejection))
        {
            HostManifest = null;
            TargetedResponseDispatched = true;
            AuraToolsLog.Info("[ModSync] host manifest dispatched through targeted response: requester="
                              + RequesterPlayerId
                              + ", request="
                              + RequestId
                              + ".");
            return;
        }

        TargetedResponseDispatched = false;
        if (!ForceBroadcastResponse && TargetQueryId != 0)
        {
            AuraToolsLog.Warn("[ModSync] targeted host manifest unavailable; using broadcast fallback: "
                              + targetedRejection);
        }

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
                    RequestId,
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
        if (TargetedResponseDispatched)
        {
            return;
        }

        AuraToolsModSyncRuntime.ReceiveHostModManifest(this);
    }
}

[Serializable]
public sealed class AuraToolsModSyncManifestChunkCommand : RpcCommandBase
{
    public int ProtocolVersion { get; set; } = AuraToolsModSyncManifestCommand.CurrentProtocolVersion;

    public string RequesterPlayerId { get; set; } = "";

    public string RequestId { get; set; } = "";

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
