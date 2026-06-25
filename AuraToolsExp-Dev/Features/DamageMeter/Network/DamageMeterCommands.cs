using System;
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
public sealed class DamageMeterSubmitCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

    public DamageEvent Candidate { get; set; } = new();

    public DamageEvent? Confirmed { get; set; }

    public string RejectionReason { get; set; } = "";

    public void BindServerSender(AuraToolsRpcSender sender)
    {
        serverSender = sender ?? AuraToolsRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (!DamageMeterNetworkRuntime.AcceptOnServer(Candidate, serverSender, out var confirmed, out var rejection))
        {
            RejectionReason = rejection;
            Confirmed = null;
            AuraToolsLog.Warn("[DamageMeter] event rejected: " + rejection);
            return;
        }

        Confirmed = confirmed;
    }

    public override void RpcExecute()
    {
        if (Confirmed != null)
        {
            DamageMeterNetworkRuntime.ApplyConfirmed(Confirmed);
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
        if (ProtocolVersion == DamageMeterProtocol.Version)
        {
            if (!DamageMeterNetworkRuntime.TryCreateServerSnapshot(serverSender, out var snapshot, out var rejection))
            {
                RejectionReason = rejection;
                Snapshot = null;
                AuraToolsLog.Warn("[DamageMeter] snapshot rejected: " + rejection);
                return;
            }

            Snapshot = snapshot;
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
