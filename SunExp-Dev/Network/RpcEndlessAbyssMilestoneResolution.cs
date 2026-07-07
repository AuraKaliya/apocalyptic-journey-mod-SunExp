using System;
using Network.Command;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Network;

public static class EndlessAbyssMilestoneRewardKind
{
    public const string Relic = "relic";
    public const string OtherDimensionCard = "other-dimension-card";
    public const string RemoveBurnout = "remove-burnout";
    public const string AddExtinction = "add-extinction";
}

[Serializable]
public sealed class EndlessAbyssMilestoneResolution
{
    public int Floor { get; set; }

    public string Kind { get; set; } = "";

    public string RelicId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string CardInstanceId { get; set; } = "";

    public string CardBaseId { get; set; } = "";

    public string Source { get; set; } = "";

    public string Token { get; set; } = "";
}

[Serializable]
public sealed class RpcEndlessAbyssMilestoneResolution : RpcCommandBase
{
    public EndlessAbyssMilestoneResolution Resolution { get; set; } = new();

    public TongtianTowerStateSnapshot Snapshot { get; set; } = new();

    public string Source { get; set; } = "";

    public RpcEndlessAbyssMilestoneResolution()
    {
    }

    public RpcEndlessAbyssMilestoneResolution(
        EndlessAbyssMilestoneResolution resolution,
        TongtianTowerStateSnapshot snapshot,
        string source)
    {
        Resolution = resolution ?? new EndlessAbyssMilestoneResolution();
        Snapshot = snapshot ?? new TongtianTowerStateSnapshot();
        Source = source ?? "";
    }

    public override void RpcExecute()
    {
        EndlessAbyssMilestoneRewardService.ApplyNetworkResolution(
            Resolution,
            "RpcEndlessAbyssMilestoneResolution:" + Source);
        Snapshot?.Apply("RpcEndlessAbyssMilestoneResolution:" + Source);
    }
}
