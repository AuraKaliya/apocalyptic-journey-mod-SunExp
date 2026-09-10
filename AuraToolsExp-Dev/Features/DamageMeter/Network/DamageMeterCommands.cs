using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

public static class DamageMeterControlKind
{
    public const string StartFight = "StartFight";
    public const string StartRound = "StartRound";
    public const string EndFight = "EndFight";
}

[Serializable]
public sealed class DamageMeterSubmitBatchCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

    public int ProtocolVersion { get; set; }
    public string SessionId { get; set; } = "";
    public bool IsFinalMarker { get; set; }
    public long FinalReporterSequence { get; set; }
    public string ReplyPlayerId { get; set; } = "";
    public int AcknowledgementProtocol { get; set; }
    public long AcknowledgedThrough { get; set; }
    public long AcknowledgedServerSequence { get; set; }
    public bool FinalMarkerAccepted { get; set; }
    public string BatchRejection { get; set; } = "";
    public List<DamageEvent> Candidates { get; set; } = new();

    public List<DamageEvent> Confirmed { get; set; } = new();

    public List<string> RejectionReasons { get; set; } = new();

    public void BindServerSender(AuraToolsRpcSender sender)
    {
        serverSender = sender ?? AuraToolsRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        DamageMeterNetworkRuntime.ResolveSubmission(this, serverSender);
        Candidates = new List<DamageEvent>();
    }

    public override void RpcExecute()
    {
        if (Confirmed != null && Confirmed.Count > 0)
        {
            DamageMeterNetworkRuntime.ApplyConfirmedBatch(Confirmed);
        }
        DamageMeterNetworkRuntime.ReceiveAcknowledgement(this);
    }
}

[Serializable]
public sealed class DamageMeterControlCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

    public int ProtocolVersion { get; set; }
    public bool FinalizeOnly { get; set; }
    public string Kind { get; set; } = "";

    public string IssuerPlayerId { get; set; } = "";

    public string SessionId { get; set; } = "";

    public bool SharedEnabled { get; set; }

    public int RoundIndex { get; set; }

    public string Result { get; set; } = "";

    public DamageMeterSnapshot? Snapshot { get; set; }

    public string RejectionReason { get; set; } = "";

    public void BindServerSender(AuraToolsRpcSender sender)
    {
        serverSender = sender ?? AuraToolsRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (!DamageMeterNetworkRuntime.ApplyControlOnServer(this, serverSender, out var rejection))
        {
            RejectionReason = rejection;
            Snapshot = null;
            AuraToolsLog.Warn("[DamageMeter] control rejected: " + rejection);
            return;
        }

        DamageMeterNetworkRuntime.EnsureControlResponseFits(this);
    }

    public override void RpcExecute()
    {
        if (Snapshot != null)
        {
            DamageMeterNetworkRuntime.ApplyControlSnapshot(this);
        }
    }
}

[Serializable]
public sealed class DamageMeterSnapshotCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = DamageMeterProtocol.Version;

    public string RequesterPlayerId { get; set; } = "";

    public DamageMeterSnapshot? Snapshot { get; set; }

    public string RejectionReason { get; set; } = "";

    public void BindServerSender(AuraToolsRpcSender sender)
    {
        serverSender = sender ?? AuraToolsRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (DamageMeterProtocol.IsCompatible(ProtocolVersion))
        {
            if (!DamageMeterNetworkRuntime.TryCreateServerSnapshot(serverSender, out var snapshot, out var rejection))
            {
                RejectionReason = rejection;
                Snapshot = null;
                AuraToolsLog.Warn("[DamageMeter] snapshot rejected: " + rejection);
                return;
            }

            if (snapshot == null)
            {
                RejectionReason = "快照为空。";
                Snapshot = null;
                return;
            }

            snapshot.ProtocolVersion = ProtocolVersion;
            if (snapshot.RunAggregate != null)
            {
                snapshot.RunAggregate.ProtocolVersion = ProtocolVersion;
            }
            Snapshot = snapshot;
            DamageMeterNetworkRuntime.EnsureSnapshotResponseFits(this);
        }
        else
        {
            RejectionReason = "协议不兼容。";
            Snapshot = null;
        }
    }

    public override void RpcExecute()
    {
        if (Snapshot != null)
        {
            DamageMeterNetworkRuntime.ApplySnapshot(Snapshot);
        }
    }
}
