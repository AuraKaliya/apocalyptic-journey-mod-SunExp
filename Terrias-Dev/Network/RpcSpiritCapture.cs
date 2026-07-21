using System;
using Network.Command;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class SpiritCaptureNetworkState
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string Token { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string TargetStatusId { get; set; } = "";
    public bool Resolved { get; set; }
    public bool Success { get; set; }
    public int ChanceBasisPoints { get; set; }
    public int RollBasisPoints { get; set; }
    public CapturedEnemySnapshot? CapturedEnemy { get; set; }
    public string Reason { get; set; } = "";
}

[Serializable]
public sealed class RpcSpiritCaptureRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public string OwnerStatusId { get; set; } = "";
    public string TargetStatusId { get; set; } = "";
    public string Token { get; set; } = "";
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }

    public RpcSpiritCaptureRequest()
    {
    }

    public RpcSpiritCaptureRequest(string ownerStatusId, string targetStatusId, string token)
    {
        OwnerStatusId = ownerStatusId ?? "";
        TargetStatusId = targetStatusId ?? "";
        Token = token ?? "";
        BattleEpoch = CompanionAuthorityService.BattleEpoch;
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        SpiritCaptureService.ResolveNetworkCapture(
            OwnerStatusId,
            TargetStatusId,
            Token,
            serverSender,
            ProtocolVersion,
            BattleEpoch);
        OwnerStatusId = "";
        TargetStatusId = "";
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcSpiritCaptureState : RpcCommandBase
{
    public SpiritCaptureNetworkState State { get; set; } = new();

    public RpcSpiritCaptureState()
    {
    }

    public RpcSpiritCaptureState(SpiritCaptureNetworkState state)
    {
        State = state ?? new SpiritCaptureNetworkState();
    }

    public override void RpcExecute()
    {
        SpiritCaptureService.ApplyNetworkState(State, "RpcSpiritCaptureState");
    }
}
