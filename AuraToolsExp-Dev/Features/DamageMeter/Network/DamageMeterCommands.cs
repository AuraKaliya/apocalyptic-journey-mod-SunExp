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

    public List<DamageEvent> Candidates { get; set; } = new();

    public List<DamageEvent> Confirmed { get; set; } = new();

    public List<string> RejectionReasons { get; set; } = new();

    public void BindServerSender(AuraToolsRpcSender sender)
    {
        serverSender = sender ?? AuraToolsRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        var accepted = DamageMeterNetworkRuntime.AcceptBatchOnServer(
                Candidates,
                serverSender,
                out var confirmed,
                out var rejections);
        Candidates = new List<DamageEvent>();
        if (!accepted)
        {
            Confirmed = new List<DamageEvent>();
            RejectionReasons = rejections;
            AuraToolsLog.Warn("[DamageMeter] event batch rejected: " + string.Join("; ", rejections));
            return;
        }

        Confirmed = confirmed;
        RejectionReasons = rejections;
        if (rejections.Count > 0)
        {
            AuraToolsLog.Debug("[DamageMeter] event batch accepted with rejections="
                               + rejections.Count
                               + "; first="
                               + rejections[0]);
        }
    }

    public override void RpcExecute()
    {
        if (Confirmed != null && Confirmed.Count > 0)
        {
            DamageMeterNetworkRuntime.ApplyConfirmedBatch(Confirmed);
        }
    }
}

[Serializable]
public sealed class DamageMeterControlCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

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
