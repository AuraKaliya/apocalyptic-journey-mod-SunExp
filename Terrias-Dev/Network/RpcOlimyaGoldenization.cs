using AuraShared.Core;
using Terrias.Dll.Contracts;
using System;
using Network.Command;
using Terrias.Dll.Application;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcOlimyaGoldenization : AuraBattleRpcCommand, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender sender = TerriasRpcSender.Unbound;
    public OlimyaGoldenizationCommand Command { get; set; } = new();
    public bool Accepted { get; set; }
    public void BindServerSender(TerriasRpcSender value) => sender = value ?? TerriasRpcSender.Unbound;

    public override void CmdExecute()
    {
        var owns = sender.IsAvailable && sender.IsLobbyMember && Command != null
            && TerriasStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, Command.OwnerStatusId, out _);
        Accepted = Command != null && OlimyaRoleApplication.HandleAuthoritative(Command, owns);
        if (!Accepted)
            TerriasLog.Warn("[Olimya] rejected stale, invalid or unowned goldenization command.");
    }

    public override void RpcExecute() => OlimyaRoleApplication.ReceiveResult(Command, Accepted, NetworkBattleId);
}

public static class OlimyaNetworkAdapter
{
    private static bool initialized;
    public static void Initialize()
    {
        OlimyaRoleApplication.DispatchCommand = Send;
        if (initialized) return;
        initialized = true;
        AuraNetworkIdentityRuntime.Updating += OlimyaRoleApplication.TickPending;
    }

    private static bool Send(OlimyaGoldenizationCommand command)
    {
        if (TerriasNetworkRuntime.IsClientOnly())
            return TerriasNetworkRuntime.Send(new RpcOlimyaGoldenization { Command = command }, "Olimya.Goldenization");
        if (!TerriasNetworkRuntime.NetworkActive())
        {
            var accepted = OlimyaRoleApplication.HandleLocalAuthoritative(command);
            OlimyaRoleApplication.ReceiveResult(command, accepted, AuraNetworkIdentityRuntime.BattleId);
            return accepted;
        }
        var sender = TerriasRpcAuthorityRuntime.CreateLocalServerSender("Olimya.Goldenization");
        var owns = sender.IsAvailable && sender.IsLobbyMember
            && TerriasStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, command.OwnerStatusId, out _);
        var resolved = OlimyaRoleApplication.HandleAuthoritative(command, owns);
        OlimyaRoleApplication.ReceiveResult(command, resolved, AuraNetworkIdentityRuntime.BattleId);
        return resolved;
    }
}
