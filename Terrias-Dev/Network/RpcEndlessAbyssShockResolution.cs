using System;
using System.Collections.Generic;
using Network.Command;
using Terrias.Dll.Hooks;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class EndlessAbyssShockResolution
{
    public EndlessAbyssShockRequest Request { get; set; } = new();

    public List<string> Options { get; set; } = new();

    public string Source { get; set; } = "";

    public string Token { get; set; } = "";
}

[Serializable]
public sealed class RpcEndlessAbyssShockResolution : RpcCommandBase
{
    public EndlessAbyssShockResolution Resolution { get; set; } = new();

    public EndlessSeaStateSnapshot Snapshot { get; set; } = new();

    public string Source { get; set; } = "";

    public RpcEndlessAbyssShockResolution()
    {
    }

    public RpcEndlessAbyssShockResolution(
        EndlessAbyssShockResolution resolution,
        EndlessSeaStateSnapshot snapshot,
        string source)
    {
        Resolution = resolution ?? new EndlessAbyssShockResolution();
        Snapshot = snapshot ?? new EndlessSeaStateSnapshot();
        Source = source ?? "";
    }

    public override void RpcExecute()
    {
        EndlessAbyssShockService.ApplyNetworkResolution(
            Resolution,
            "RpcEndlessAbyssShockResolution:" + Source);
        Snapshot?.Apply("RpcEndlessAbyssShockResolution:" + Source);
        EndlessAbyssMilestonePromptService.Schedule("RpcEndlessAbyssShockResolution:" + Source);
    }
}
