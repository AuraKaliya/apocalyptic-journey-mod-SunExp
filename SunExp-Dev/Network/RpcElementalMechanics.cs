using System;
using Network.Command;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class RpcElementalEnemyMagicSnapshot : RpcCommandBase
{
    public ElementalEnemyMagicSnapshot Snapshot { get; set; } = new();

    public RpcElementalEnemyMagicSnapshot()
    {
    }

    public RpcElementalEnemyMagicSnapshot(ElementalEnemyMagicSnapshot snapshot)
    {
        Snapshot = snapshot ?? new ElementalEnemyMagicSnapshot();
    }

    public override void RpcExecute()
    {
        ElementalMagicService.ApplyNetworkSnapshot(Snapshot, "RpcElementalEnemyMagicSnapshot");
    }
}

[Serializable]
public sealed class RpcElementalCrystalSpawn : RpcCommandBase
{
    public ElementalCrystalEventSnapshot Snapshot { get; set; } = new();

    public RpcElementalCrystalSpawn()
    {
    }

    public RpcElementalCrystalSpawn(ElementalCrystalEventSnapshot snapshot)
    {
        Snapshot = snapshot ?? new ElementalCrystalEventSnapshot();
    }

    public override void RpcExecute()
    {
        ElementalCrystalChallengeService.ApplyNetworkSpawn(Snapshot, "RpcElementalCrystalSpawn");
    }
}

[Serializable]
public sealed class RpcElementalCrystalCreateRequest : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public string SourceStatusId { get; set; } = "";

    public string TriggerTargetStatusId { get; set; } = "";

    public int BattleEpoch { get; set; }

    public string Token { get; set; } = "";

    public RpcElementalCrystalCreateRequest()
    {
    }

    public RpcElementalCrystalCreateRequest(
        string sourceStatusId,
        string triggerTargetStatusId,
        int battleEpoch,
        string token)
    {
        SourceStatusId = sourceStatusId ?? "";
        TriggerTargetStatusId = triggerTargetStatusId ?? "";
        BattleEpoch = battleEpoch;
        Token = token ?? "";
    }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ElementalCrystalChallengeService.ResolveCreateRequest(
            SourceStatusId,
            TriggerTargetStatusId,
            Token,
            serverSender,
            BattleEpoch);
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcElementalCrystalClaim : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public string EventId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public int BattleEpoch { get; set; }

    public RpcElementalCrystalClaim()
    {
    }

    public RpcElementalCrystalClaim(string eventId, string ownerStatusId, int battleEpoch)
    {
        EventId = eventId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        BattleEpoch = battleEpoch;
    }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ElementalCrystalChallengeService.ResolveClaim(EventId, OwnerStatusId, serverSender, BattleEpoch);
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcElementalCrystalResolution : RpcCommandBase
{
    public ElementalCrystalResolutionSnapshot Resolution { get; set; } = new();

    public RpcElementalCrystalResolution()
    {
    }

    public RpcElementalCrystalResolution(ElementalCrystalResolutionSnapshot resolution)
    {
        Resolution = resolution ?? new ElementalCrystalResolutionSnapshot();
    }

    public override void RpcExecute()
    {
        ElementalCrystalChallengeService.ApplyNetworkResolution(Resolution, "RpcElementalCrystalResolution");
    }
}
