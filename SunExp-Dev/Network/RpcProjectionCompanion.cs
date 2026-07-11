using System;
using System.Collections.Generic;
using Network.Command;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class ProjectionCompanionSnapshot
{
    public int ProtocolVersion { get; set; } = 3;

    public int BattleEpoch { get; set; }

    public string RegistryHash { get; set; } = "";

    public int Revision { get; set; }

    public string Token { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string OwnerPlayerId { get; set; } = "";

    public string StatusId { get; set; } = "";

    public int SlotIndex { get; set; } = -1;

    public bool Accepted { get; set; }

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Attack { get; set; }

    public int Armor { get; set; }

    public int MaxMagic { get; set; }

    public int CurrentMagic { get; set; }

    public int TurnIndex { get; set; }

    public Dictionary<string, int> ReadyOnTurn { get; set; } = new();

    public List<ProjectionBuffSnapshot> Buffs { get; set; } = new();

    public CompanionThreatSnapshot? Threat { get; set; }

    public CompanionIntentPlan? IntentPlan { get; set; }

    public string RejectionReason { get; set; } = "";
}

[Serializable]
public sealed class RpcProjectionSummonRequest : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public string RoleId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string Token { get; set; } = "";

    public int ProtocolVersion { get; set; } = 3;

    public int BattleEpoch { get; set; }

    public string RegistryHash { get; set; } = "";

    public RpcProjectionSummonRequest()
    {
    }

    public RpcProjectionSummonRequest(string roleId, string ownerStatusId, string token)
    {
        RoleId = roleId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        Token = token ?? "";
        BattleEpoch = CompanionAuthorityService.BattleEpoch;
        RegistryHash = CompanionIntentRegistry.RegistryHash;
    }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ProjectionSummonService.ResolveNetworkSummon(RoleId, OwnerStatusId, Token, serverSender, ProtocolVersion, BattleEpoch, RegistryHash);
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcProjectionCompanionState : RpcCommandBase
{
    public ProjectionCompanionSnapshot Snapshot { get; set; } = new();

    public RpcProjectionCompanionState()
    {
    }

    public RpcProjectionCompanionState(ProjectionCompanionSnapshot snapshot)
    {
        Snapshot = snapshot ?? new ProjectionCompanionSnapshot();
    }

    public override void RpcExecute()
    {
        ProjectionSummonService.ApplyNetworkState(Snapshot, "RpcProjectionCompanionState");
    }
}

[Serializable]
public sealed class RpcHeartChangeControlRequest : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public string TargetStatusId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string Token { get; set; } = "";

    public RpcHeartChangeControlRequest()
    {
    }

    public RpcHeartChangeControlRequest(string targetStatusId, string ownerStatusId, string token)
    {
        TargetStatusId = targetStatusId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        Token = token ?? "";
    }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        HeartChangeControlService.ResolveNetworkControl(TargetStatusId, OwnerStatusId, Token, serverSender);
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcHeartChangeControlState : RpcCommandBase
{
    public string TargetStatusId { get; set; } = "";

    public string Token { get; set; } = "";

    public int SlotIndex { get; set; } = -1;

    public bool Active { get; set; }

    public bool Accepted { get; set; }

    public string RejectionReason { get; set; } = "";

    public RpcHeartChangeControlState()
    {
    }

    public RpcHeartChangeControlState(string targetStatusId, string token, int slotIndex, bool active, bool accepted, string rejectionReason = "")
    {
        TargetStatusId = targetStatusId ?? "";
        Token = token ?? "";
        SlotIndex = slotIndex;
        Active = active;
        Accepted = accepted;
        RejectionReason = rejectionReason ?? "";
    }

    public override void RpcExecute()
    {
        HeartChangeControlService.ApplyNetworkState(this, "RpcHeartChangeControlState");
    }
}
