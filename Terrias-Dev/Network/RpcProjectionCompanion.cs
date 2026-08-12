using System;
using System.Collections.Generic;
using Network.Command;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class ProjectionCompanionSnapshot
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;

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

    public string RejectionReason { get; set; } = "";

}

[Serializable]
public sealed class ProjectionPrepareResult
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string Token { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string OwnerPlayerId { get; set; } = "";
    public int SlotIndex { get; set; } = -1;
    public bool Accepted { get; set; }
    public bool RefundCard { get; set; }
    public string RejectionReason { get; set; } = "";
}

[Serializable]
public sealed class RpcProjectionSummonRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public string RoleId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string Token { get; set; } = "";

    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;

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
        RegistryHash = ProjectionCardBattleState.ProtocolIdentity;
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ProjectionSummonService.ResolveNetworkSummon(RoleId, OwnerStatusId, Token, serverSender, ProtocolVersion, BattleEpoch, RegistryHash);
        RoleId = "";
        OwnerStatusId = "";
        RegistryHash = "";
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
public sealed class RpcProjectionPrepareResult : RpcCommandBase
{
    public ProjectionPrepareResult Result { get; set; } = new();

    public RpcProjectionPrepareResult()
    {
    }

    public RpcProjectionPrepareResult(ProjectionPrepareResult result)
    {
        Result = result ?? new ProjectionPrepareResult();
    }

    public override void RpcExecute()
    {
        ProjectionSummonService.ApplyPrepareResult(Result, "RpcProjectionPrepareResult");
    }
}

[Serializable]
public sealed class RpcProjectionPrivateStateChunk : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string Token { get; set; } = "";
    public int ChunkIndex { get; set; }
    public int ChunkCount { get; set; }
    public int TotalBytes { get; set; }
    public int UncompressedBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ProjectionSummonService.AcceptPrivateStateChunk(this, serverSender);
        Payload = Array.Empty<byte>();
        Sha256 = "";
        Token = "";
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcProjectionPrivateStateAbort : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string Token { get; set; } = "";
    public string Reason { get; set; } = "";

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ProjectionSummonService.AbortPrivateStateUpload(
            Token,
            Reason,
            serverSender,
            ProtocolVersion,
            BattleEpoch);
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class ProjectionCardPresentationSnapshot
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string ActionId { get; set; } = "";
    public int Sequence { get; set; }
    public string ProjectionStatusId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string CardId { get; set; } = "";
    public List<string> TargetStatusIds { get; set; } = new();
}

[Serializable]
public sealed class RpcProjectionCardPresentation : RpcCommandBase
{
    public ProjectionCardPresentationSnapshot Snapshot { get; set; } = new();

    public RpcProjectionCardPresentation()
    {
    }

    public RpcProjectionCardPresentation(ProjectionCardPresentationSnapshot snapshot)
    {
        Snapshot = snapshot ?? new ProjectionCardPresentationSnapshot();
    }

    public override void RpcExecute()
    {
        ProjectionCardPresentationService.Apply(Snapshot, null, "RpcProjectionCardPresentation");
    }
}

[Serializable]
public sealed class RpcHeartChangeControlRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

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

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        HeartChangeControlService.ResolveNetworkControl(TargetStatusId, OwnerStatusId, Token, serverSender);
        TargetStatusId = "";
        OwnerStatusId = "";
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcHeartChangeControlState : RpcCommandBase
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;

    public string TargetStatusId { get; set; } = "";

    public string Token { get; set; } = "";

    public int SlotIndex { get; set; } = -1;

    public bool Active { get; set; }

    public bool Accepted { get; set; }

    public string RejectionReason { get; set; } = "";

    public int IntentCount { get; set; }

    public RpcHeartChangeControlState()
    {
    }

    public RpcHeartChangeControlState(
        string targetStatusId,
        string token,
        int slotIndex,
        bool active,
        bool accepted,
        string rejectionReason = "",
        int intentCount = 0)
    {
        TargetStatusId = targetStatusId ?? "";
        Token = token ?? "";
        SlotIndex = slotIndex;
        Active = active;
        Accepted = accepted;
        RejectionReason = rejectionReason ?? "";
        IntentCount = Math.Max(0, intentCount);
    }

    public override void RpcExecute()
    {
        HeartChangeControlService.ApplyNetworkState(this, "RpcHeartChangeControlState");
    }
}
