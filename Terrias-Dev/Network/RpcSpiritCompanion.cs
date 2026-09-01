using System;
using System.Collections.Generic;
using Network.Command;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class SpiritCompanionSnapshot
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public int Revision { get; set; }
    public int Generation { get; set; }
    public int ExchangeCount { get; set; }
    public string Token { get; set; } = "";
    public SpiritDeploymentSnapshot Deployment { get; set; } = new();
    public string OwnerStatusId { get; set; } = "";
    public string OwnerPlayerId { get; set; } = "";
    public string ExecutionRoutePlayerId { get; set; } = "";
    public string StatusId { get; set; } = "";
    public bool Accepted { get; set; }
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public int CurrentDefend { get; set; }
    public int StatusDataVersion { get; set; }
    public int StatusState { get; set; }
    public int Attack { get; set; }
    public int Armor { get; set; }
    public int MaxMagic { get; set; }
    public int CurrentMagic { get; set; }
    public Dictionary<string, int> PassiveState { get; set; } = new();
    public List<SpiritVisibleStatusSnapshot> VisibleStatuses { get; set; } = new();
    public int TurnIndex { get; set; }
    public Dictionary<string, int> ReadyOnTurn { get; set; } = new();
    public CompanionThreatSnapshot? Threat { get; set; }
    public CompanionIntentPlan? IntentPlan { get; set; }
    public string ReplacedStatusId { get; set; } = "";
    public SpiritDeploymentSnapshot? ReturnedDeployment { get; set; }
    public int ReturnedExchangeCount { get; set; }
    public SpiritCardBattleState ReturnedBattleState { get; set; } = new();
    public string CardGrantEventId { get; set; } = "";
    public bool ReturnedCardOnly { get; set; }
    public string RejectionReason { get; set; } = "";
}

[Serializable]
public sealed class SpiritCompanionRemovalSnapshot
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string StatusId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string OwnerPlayerId { get; set; } = "";
    public int Generation { get; set; }
    public int StatusDataVersion { get; set; }
    public bool PlayDeathEffect { get; set; }
    public string Reason { get; set; } = "";
}

[Serializable]
public sealed class RpcSpiritSummonRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public SpiritDeploymentSnapshot Deployment { get; set; } = new();
    public string OwnerStatusId { get; set; } = "";
    public string Token { get; set; } = "";
    public int ExchangeCount { get; set; }
    public SpiritCardBattleState BattleState { get; set; } = new();
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }

    public RpcSpiritSummonRequest()
    {
    }

    public RpcSpiritSummonRequest(
        SpiritDeploymentSnapshot deployment,
        string ownerStatusId,
        string token,
        int exchangeCount,
        SpiritCardBattleState battleState)
    {
        Deployment = deployment ?? new SpiritDeploymentSnapshot();
        OwnerStatusId = ownerStatusId ?? "";
        Token = token ?? "";
        ExchangeCount = Math.Max(0, exchangeCount);
        BattleState = battleState ?? new SpiritCardBattleState();
        BattleEpoch = CompanionAuthorityService.BattleEpoch;
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        SpiritSummonService.ResolveNetworkSummon(
            Deployment,
            OwnerStatusId,
            Token,
            ExchangeCount,
            BattleState ?? new SpiritCardBattleState(),
            serverSender,
            ProtocolVersion,
            BattleEpoch);
        Deployment = new SpiritDeploymentSnapshot();
        OwnerStatusId = "";
        BattleState = new SpiritCardBattleState();
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcSpiritCompanionState : RpcCommandBase
{
    public SpiritCompanionSnapshot Snapshot { get; set; } = new();

    public RpcSpiritCompanionState()
    {
    }

    public RpcSpiritCompanionState(SpiritCompanionSnapshot snapshot)
    {
        Snapshot = snapshot ?? new SpiritCompanionSnapshot();
    }

    public override void RpcExecute()
    {
        SpiritSummonService.ApplyNetworkState(Snapshot, "RpcSpiritCompanionState");
    }
}

[Serializable]
public sealed class RpcSpiritCompanionRemoved : RpcCommandBase
{
    public SpiritCompanionRemovalSnapshot Removal { get; set; } = new();

    public RpcSpiritCompanionRemoved()
    {
    }

    public RpcSpiritCompanionRemoved(SpiritCompanionRemovalSnapshot removal)
    {
        Removal = removal ?? new SpiritCompanionRemovalSnapshot();
    }

    public override void RpcExecute()
    {
        SpiritSummonService.ApplyNetworkRemoval(Removal, "RpcSpiritCompanionRemoved");
    }
}

[Serializable]
public sealed class RpcSpiritWithdrawRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string OwnerStatusId { get; set; } = "";
    public string Token { get; set; } = "";

    public RpcSpiritWithdrawRequest()
    {
    }

    public RpcSpiritWithdrawRequest(string ownerStatusId, string token)
    {
        BattleEpoch = CompanionAuthorityService.BattleEpoch;
        OwnerStatusId = ownerStatusId ?? "";
        Token = token ?? "";
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        SpiritWithdrawService.ResolveNetworkWithdraw(
            OwnerStatusId,
            Token,
            serverSender,
            ProtocolVersion,
            BattleEpoch);
    }

    public override void RpcExecute()
    {
    }
}
