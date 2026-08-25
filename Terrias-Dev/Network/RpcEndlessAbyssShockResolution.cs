using System;
using Network.Command;
using Terrias.Dll.Application;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcEndlessAbyssShockResolution : RpcCommandBase
{
    public EndlessSeaShockMessage Resolution { get; set; } = new();

    public EndlessSeaStateSnapshot Snapshot { get; set; } = new();

    public string Source { get; set; } = "";

    public RpcEndlessAbyssShockResolution()
    {
    }

    public RpcEndlessAbyssShockResolution(
        EndlessSeaShockMessage resolution,
        EndlessSeaStateSnapshot snapshot,
        string source)
    {
        Resolution = resolution ?? new EndlessSeaShockMessage();
        Snapshot = snapshot ?? new EndlessSeaStateSnapshot();
        Source = source ?? "";
    }

    public override void RpcExecute()
    {
        EndlessSeaApplicationService.ApplyShockResolution(
            Resolution,
            Snapshot,
            "RpcEndlessAbyssShockResolution:" + Source);
    }
}
