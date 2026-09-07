using Terrias.Dll.Application;
using Terrias.Dll.Contracts;
using System;
using System.Collections.Generic;
using Network.Command;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcProjectionSummonRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public string RoleId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string Token { get; set; } = "";

    public int ProtocolVersion { get; set; } = TerriasProtocolContract.ProjectionVersion;

    public int BattleEpoch { get; set; }

    public string CardModelVersion { get; set; } = TerriasProtocolContract.ProjectionCardModel;

    public string DeckRecipeHash { get; set; } = "";

    public RpcProjectionSummonRequest()
    {
    }

    public RpcProjectionSummonRequest(
        string roleId,
        string ownerStatusId,
        string token,
        string deckRecipeHash)
    {
        RoleId = roleId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        Token = token ?? "";
        DeckRecipeHash = deckRecipeHash ?? "";
        BattleEpoch = ProjectionNetworkApplication.BattleEpoch;
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ProjectionSummonService.ResolveNetworkSummon(
            RoleId,
            OwnerStatusId,
            Token,
            serverSender,
            ProtocolVersion,
            BattleEpoch,
            CardModelVersion,
            DeckRecipeHash);
        RoleId = "";
        OwnerStatusId = "";
        CardModelVersion = "";
        DeckRecipeHash = "";
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
public sealed class RpcProjectionSummonResult : RpcCommandBase
{
    public ProjectionSummonResultSnapshot Result { get; set; } = new();

    public RpcProjectionSummonResult()
    {
    }

    public RpcProjectionSummonResult(ProjectionSummonResultSnapshot result)
    {
        Result = result ?? new ProjectionSummonResultSnapshot();
    }

    public override void RpcExecute()
    {
        ProjectionSummonService.ApplySummonResult(Result, "RpcProjectionSummonResult");
    }
}

[Serializable]
public sealed class RpcProjectionSummonTurnState : RpcCommandBase
{
    public ProjectionSummonTurnSnapshot Snapshot { get; set; } = new();

    public RpcProjectionSummonTurnState()
    {
    }

    public RpcProjectionSummonTurnState(ProjectionSummonTurnSnapshot snapshot)
    {
        Snapshot = snapshot ?? new ProjectionSummonTurnSnapshot();
    }

    public override void RpcExecute()
    {
        ProjectionNetworkApplication.ApplyTurn(Snapshot);
    }
}

[Serializable]
public sealed class RpcProjectionStateRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = TerriasProtocolContract.ProjectionVersion;
    public int BattleEpoch { get; set; }
    public string StatusId { get; set; } = "";
    public string Generation { get; set; } = "";

    public RpcProjectionStateRequest()
    {
    }

    public RpcProjectionStateRequest(string statusId, string generation)
    {
        StatusId = statusId ?? "";
        Generation = generation ?? "";
        BattleEpoch = ProjectionNetworkApplication.BattleEpoch;
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ProjectionSummonService.ResolveStateRequest(
            StatusId,
            Generation,
            serverSender,
            ProtocolVersion,
            BattleEpoch);
        StatusId = "";
        Generation = "";
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class ProjectionActionFrameSnapshot
{
    public int ProtocolVersion { get; set; } = TerriasProtocolContract.ProjectionVersion;
    public int BattleEpoch { get; set; }
    public string Generation { get; set; } = "";
    public long ActionSequence { get; set; }
    public string ProjectionStatusId { get; set; } = "";
    public string CardId { get; set; } = "";
    public List<string> TargetStatusIds { get; set; } = new();
}

[Serializable]
public sealed class RpcProjectionActionFrame : RpcCommandBase
{
    public ProjectionActionFrameSnapshot Snapshot { get; set; } = new();

    public RpcProjectionActionFrame()
    {
    }

    public RpcProjectionActionFrame(ProjectionActionFrameSnapshot snapshot)
    {
        Snapshot = snapshot ?? new ProjectionActionFrameSnapshot();
    }

    public override void RpcExecute()
    {
        ProjectionCardPresentationService.Apply(Snapshot, null, "RpcProjectionActionFrame");
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
    public int ProtocolVersion { get; set; } = TerriasProtocolContract.ProjectionVersion;

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
