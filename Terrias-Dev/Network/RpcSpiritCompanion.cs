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
    public string RegistryHash { get; set; } = "";
    public int Revision { get; set; }
    public int Generation { get; set; }
    public int ExchangeCount { get; set; }
    public string Token { get; set; } = "";
    public CapturedEnemySnapshot CapturedEnemy { get; set; } = new();
    public string OwnerStatusId { get; set; } = "";
    public string OwnerPlayerId { get; set; } = "";
    public string StatusId { get; set; } = "";
    public bool Accepted { get; set; }
    public int Attack { get; set; }
    public int Armor { get; set; }
    public int MaxMagic { get; set; }
    public int CurrentMagic { get; set; }
    public int TurnIndex { get; set; }
    public Dictionary<string, int> ReadyOnTurn { get; set; } = new();
    public CompanionThreatSnapshot? Threat { get; set; }
    public CompanionIntentPlan? IntentPlan { get; set; }
    public string ReplacedStatusId { get; set; } = "";
    public CapturedEnemySnapshot? ReturnedCard { get; set; }
    public int ReturnedExchangeCount { get; set; }
    public int ReturnedTurnIndex { get; set; }
    public Dictionary<string, int> ReturnedReadyOnTurn { get; set; } = new();
    public string CardGrantEventId { get; set; } = "";
    public string RejectionReason { get; set; } = "";
}

[Serializable]
public sealed class RpcSpiritSummonRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public CapturedEnemySnapshot CapturedEnemy { get; set; } = new();
    public string OwnerStatusId { get; set; } = "";
    public string Token { get; set; } = "";
    public int ExchangeCount { get; set; }
    public int TurnIndex { get; set; }
    public Dictionary<string, int> ReadyOnTurn { get; set; } = new();
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string RegistryHash { get; set; } = "";

    public RpcSpiritSummonRequest()
    {
    }

    public RpcSpiritSummonRequest(
        CapturedEnemySnapshot capturedEnemy,
        string ownerStatusId,
        string token,
        int exchangeCount,
        SpiritCardBattleState battleState)
    {
        CapturedEnemy = capturedEnemy ?? new CapturedEnemySnapshot();
        OwnerStatusId = ownerStatusId ?? "";
        Token = token ?? "";
        ExchangeCount = Math.Max(0, exchangeCount);
        TurnIndex = Math.Max(0, battleState?.TurnIndex ?? 0);
        ReadyOnTurn = battleState?.ReadyOnTurn == null
            ? new Dictionary<string, int>()
            : new Dictionary<string, int>(battleState.ReadyOnTurn);
        BattleEpoch = CompanionAuthorityService.BattleEpoch;
        RegistryHash = SpiritIntentRegistry.RegistryHash;
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        SpiritSummonService.ResolveNetworkSummon(
            CapturedEnemy,
            OwnerStatusId,
            Token,
            ExchangeCount,
            new SpiritCardBattleState { TurnIndex = TurnIndex, ReadyOnTurn = ReadyOnTurn },
            serverSender,
            ProtocolVersion,
            BattleEpoch,
            RegistryHash);
        CapturedEnemy = new CapturedEnemySnapshot();
        OwnerStatusId = "";
        ReadyOnTurn = new Dictionary<string, int>();
        RegistryHash = "";
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
