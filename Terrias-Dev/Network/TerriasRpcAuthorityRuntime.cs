using Terrias.Dll.Contracts;
using System;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Network;

public interface ITerriasServerBoundRpcCommand
{
    void BindServerSender(TerriasRpcSender sender);
}

public static class TerriasRpcAuthorityRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraRpcAuthorityRuntime.Register(
            modConfig,
            TerriasIds.ModId,
            command => command is ITerriasServerBoundRpcCommand,
            (command, sender) => ((ITerriasServerBoundRpcCommand)command).BindServerSender(TerriasRpcSender.FromAura(sender)),
            message => TerriasLog.Info("[RpcAuthority] " + message),
            message => TerriasLog.Warn("[RpcAuthority] " + message));
    }

    internal static TerriasRpcSender CreateLocalServerSender(string sourceHook)
    {
        return TerriasRpcSender.FromAura(AuraRpcAuthorityRuntime.CreateLocalServerSender(sourceHook));
    }
}
