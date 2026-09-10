using System;
using AuraShared.Core;
using Data.Save;
using Network.Command;
using Newtonsoft.Json;
using Terrias.Dll.Contracts;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Application;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcSolarMemoryRoleCommit : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender sender = TerriasRpcSender.Unbound;
    public int ProtocolVersion { get; set; } = 2;
    public SolarMemoryCommitRecord Commit { get; set; } = new();
    public bool QueryOnly { get; set; }
    public string Result { get; set; } = "Pending";
    public string RejectionReason { get; set; } = "";
    public string BoundPlayerId { get; set; } = "";
    public void BindServerSender(TerriasRpcSender value) => sender = value;

    public override void CmdExecute()
    {
        BoundPlayerId = sender.PlayerId;
        Result = SolarMemoryRoleCommitApplication.Resolve(Commit, sender, QueryOnly, ProtocolVersion, out var reason);
        RejectionReason = reason;
        if (Commit != null) Commit.Payload = "";
    }

    public override void RpcExecute()
    {
        if (BoundPlayerId == AuraNetworkIdentityRuntime.LocalPlayerId && Commit != null)
            SolarMemoryRoleCommitApplication.ReceiveAuthoritativeResult(Commit.AdventureId, Commit.PlayerId, Commit.Token, Commit.PayloadHash, Result, RejectionReason);
    }

}

public static class SolarMemoryRoleCommitNetworkAdapter
{
    public static void Initialize()
    {
        SolarMemoryRoleCommitApplication.Send = (record, query) =>
            TerriasNetworkRuntime.Send(new RpcSolarMemoryRoleCommit { Commit = record, QueryOnly = query }, "SolarMemory.DurableCommit");
        SolarMemoryRoleCommitApplication.Initialize();
    }
}
