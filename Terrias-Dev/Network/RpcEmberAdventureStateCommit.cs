using System;
using Network.Command;
using Terrias.Dll.Contracts;
using Terrias.Dll.Application;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcEmberAdventureStateCommit : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender sender = TerriasRpcSender.Unbound;
    public int ProtocolVersion { get; set; } = 2;
    public EmberUpdateRequest Request { get; set; } = new();
    public EmberSyncResult Result { get; set; } = new();
    public void BindServerSender(TerriasRpcSender value) => sender = value;
    public override void CmdExecute()
    {
        Result = ProtocolVersion == 2 ? EmberAdventureStateService.Resolve(Request, sender)
            : new EmberSyncResult { RequestId = Request?.RequestId ?? "", Reason = "余烬同步协议不兼容。" };
    }
    public override void RpcExecute() => EmberAdventureStateService.Receive(Result);
}


public static class EmberNetworkAdapter
{
    public static void Initialize()
    {
        EmberAdventureStateService.Send = request =>
            TerriasNetworkRuntime.Send(new RpcEmberAdventureStateCommit { Request = request }, "Ember.Commit");
        EmberAdventureStateService.Initialize();
    }
}
