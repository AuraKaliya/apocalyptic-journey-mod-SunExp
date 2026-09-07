using Terrias.Dll.Contracts;
using System;
using Network.Command;
using Terrias.Dll.Application;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcOlimyaGoldenization : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender sender = TerriasRpcSender.Unbound;
    public OlimyaGoldenizationCommand Command { get; set; } = new();
    public void BindServerSender(TerriasRpcSender value) => sender = value ?? TerriasRpcSender.Unbound;

    public override void CmdExecute()
    {
        var owns = sender.IsAvailable && sender.IsLobbyMember && Command != null
            && TerriasStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, Command.OwnerStatusId, out _);
        if (Command == null || !OlimyaRoleApplication.HandleAuthoritative(Command, owns))
            TerriasLog.Warn("[Olimya] rejected stale, invalid or unowned goldenization command.");
    }

    public override void RpcExecute() { }
}

public static class OlimyaNetworkAdapter
{
    public static void Initialize() => OlimyaRoleApplication.DispatchCommand = Send;

    private static bool Send(OlimyaGoldenizationCommand command)
    {
        if (TerriasNetworkRuntime.IsClientOnly())
            return TerriasNetworkRuntime.Send(new RpcOlimyaGoldenization { Command = command }, "Olimya.Goldenization");
        if (!TerriasNetworkRuntime.NetworkActive())
            return OlimyaRoleApplication.HandleLocalAuthoritative(command);
        var sender = TerriasRpcAuthorityRuntime.CreateLocalServerSender("Olimya.Goldenization");
        var owns = sender.IsAvailable && sender.IsLobbyMember
            && TerriasStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, command.OwnerStatusId, out _);
        return OlimyaRoleApplication.HandleAuthoritative(command, owns);
    }
}
