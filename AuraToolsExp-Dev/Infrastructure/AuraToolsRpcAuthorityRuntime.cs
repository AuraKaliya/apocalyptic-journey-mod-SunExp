using AuraShared.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsRpcAuthorityRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraRpcAuthorityRuntime.Register(
            modConfig,
            "AuraTools",
            command => command is IAuraToolsServerBoundRpcCommand,
            (command, sender) => ((IAuraToolsServerBoundRpcCommand)command).BindServerSender(AuraToolsRpcSender.FromAura(sender)),
            message => AuraToolsLog.Info("[RpcAuthority] " + message),
            message => AuraToolsLog.Warn("[RpcAuthority] " + message));
    }
}
