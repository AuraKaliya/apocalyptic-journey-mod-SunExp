using System;
using System.Collections.Generic;
using Network.Command;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Network;

/// <summary>
/// A sender-bound request to advance one constellation tier. The client never
/// submits an absolute level; the server reads and increments its authoritative
/// per-owner state, then broadcasts the accepted snapshot in this command.
/// </summary>
[Serializable]
public sealed class RpcConstellationStateCommit : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = ConstellationStateSnapshot.CurrentProtocolVersion;
    public int Token { get; set; }
    public string BattleSessionId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public ConstellationStateSnapshot Snapshot { get; set; } = new();
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = ConstellationService.TryResolveLightUpRequest(
            ProtocolVersion,
            Token,
            BattleSessionId,
            OwnerStatusId,
            RoleId,
            serverSender,
            out var authoritativeSnapshot,
            out var rejection);
        Snapshot = authoritativeSnapshot;
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (!Accepted)
        {
            if (!string.IsNullOrWhiteSpace(RejectionReason))
            {
                TerriasLog.Warn("[ConstellationSync] light-up rejected; token="
                    + Token
                    + "; reason="
                    + RejectionReason
                    + ".");
            }

            return;
        }

        var applied = ConstellationService.ApplySnapshot(Snapshot, "RpcConstellationStateCommit");
        if (applied)
        {
            ConstellationService.NotifyLightUpApplied(Snapshot, "RpcConstellationStateCommit");
        }
    }
}

/// <summary>
/// Host-generated replacement snapshot for battle start, repair, and late
/// owner registration. Client requests contain only their bound status/role;
/// the server always captures the response from its own state table.
/// </summary>
[Serializable]
public sealed class RpcConstellationRosterSnapshot : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = ConstellationStateSnapshot.CurrentProtocolVersion;
    public string RequestOwnerStatusId { get; set; } = "";
    public string RequestRoleId { get; set; } = "";
    public string BattleSessionId { get; set; } = "";
    public List<ConstellationStateSnapshot> Snapshots { get; set; } = new();
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (ProtocolVersion != ConstellationStateSnapshot.CurrentProtocolVersion)
        {
            Accepted = false;
            RejectionReason = "protocol mismatch";
            return;
        }

        Accepted = ConstellationService.TryCaptureAuthoritativeRoster(
            RequestOwnerStatusId,
            RequestRoleId,
            serverSender,
            out var snapshots,
            out var battleSessionId,
            out var rejection);
        Snapshots = snapshots;
        BattleSessionId = battleSessionId;
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (!Accepted)
        {
            if (!string.IsNullOrWhiteSpace(RejectionReason))
            {
                TerriasLog.Warn("[ConstellationSync] roster rejected: " + RejectionReason + ".");
            }

            return;
        }

        ConstellationService.ApplyRoster(BattleSessionId, Snapshots, "RpcConstellationRosterSnapshot");
    }

    public static bool Request(IStatusManager? status, string roleId, string source)
    {
        if (status == null || !TerriasNetworkRuntime.IsClientOnly())
        {
            return false;
        }

        return TerriasNetworkRuntime.Send(new RpcConstellationRosterSnapshot
        {
            RequestOwnerStatusId = status.InstanceId ?? "",
            RequestRoleId = roleId ?? ""
        }, source ?? "Constellation.RosterRequest");
    }

    public static bool Broadcast(string source)
    {
        if (TerriasNetworkRuntime.IsClientOnly() || !TerriasNetworkRuntime.HasRemotePlayers())
        {
            return false;
        }

        var command = new RpcConstellationRosterSnapshot();
        command.BindServerSender(TerriasRpcAuthorityRuntime.CreateLocalServerSender(source));
        return TerriasNetworkRuntime.Send(command, source ?? "Constellation.RosterBroadcast");
    }
}

/// <summary>
/// A host-authorized team reward event. Every peer applies it only to its own
/// real FightPlayer status, avoiding mutations of remote status projections.
/// </summary>
[Serializable]
public sealed class RpcConstellationRoundReward : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public ConstellationRoundRewardEvent Reward { get; set; } = new();
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public RpcConstellationRoundReward()
    {
    }

    public RpcConstellationRoundReward(ConstellationRoundRewardEvent reward)
    {
        Reward = reward ?? new ConstellationRoundRewardEvent();
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = ConstellationService.ValidateRoundRewardOnServer(Reward, serverSender, out var rejection);
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (!Accepted)
        {
            if (!string.IsNullOrWhiteSpace(RejectionReason))
            {
                TerriasLog.Warn("[ConstellationSync] round reward rejected: " + RejectionReason + ".");
            }

            return;
        }

        ConstellationService.ApplyRoundReward(Reward, "RpcConstellationRoundReward");
    }
}
