using System;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraRpcAuthorityRuntime
{
    public static readonly string[] DefaultReceiveHookTargets =
    {
        "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase",
        "PlayerManager.UserCode_CmdReceiveRpcCommandExcludeOwner__RpcCommandBase",
        "PlayerManager.CmdReceiveRpcCommand",
        "PlayerManager.CmdReceiveRpcCommandExcludeOwner"
    };

    public static void Register(
        ModConfig modConfig,
        string ownerModId,
        Func<object, bool> isServerBoundCommand,
        Action<object, AuraRpcSender> bindServerSender,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        AuraNativeRpcReceiveAdapter.Install();
        for (var i = 0; i < DefaultReceiveHookTargets.Length; i++)
        {
            Register(modConfig, ownerModId, DefaultReceiveHookTargets[i], isServerBoundCommand, bindServerSender, info, warn);
        }
    }

    public static AuraRpcSender CreateLocalServerSender(string sourceHook)
    {
        if (PlayerManager.Instance == null && !Mirror.NetworkClient.active && !Mirror.NetworkServer.active)
            return new AuraRpcSender("single-player", "", true, true, sourceHook, true);
        return AuraNativeRpcReceiveAdapter.FromConnection(Mirror.NetworkServer.localConnection, sourceHook);
    }

    private static void Register(
        ModConfig modConfig,
        string ownerModId,
        string target,
        Func<object, bool> isServerBoundCommand,
        Action<object, AuraRpcSender> bindServerSender,
        Action<string>? info,
        Action<string>? warn)
    {
        AuraSharedHooks.RegisterBeforeRouted(
            modConfig,
            target,
            new AuraRoutedHookRequest
            {
                OwnerModId = string.IsNullOrWhiteSpace(ownerModId)
                    ? "AuraRpcAuthority"
                    : ownerModId.Trim(),
                HandlerId = "RpcAuthority." + target,
                Handler = context => BindSender(
                    context,
                    target,
                    isServerBoundCommand,
                    bindServerSender,
                    warn),
                SafeInvoke = true
            },
            info,
            warn);
    }

    private static void BindSender(
        ModHookContext context,
        string sourceHook,
        Func<object, bool> isServerBoundCommand,
        Action<object, AuraRpcSender> bindServerSender,
        Action<string>? warn)
    {
        var command = FindCommand(context.Arguments, isServerBoundCommand);
        if (command == null)
        {
            return;
        }

        bindServerSender(command, AuraRpcReceiveContext.Sender);
    }

    private static object? FindCommand(object[]? args, Func<object, bool> isServerBoundCommand)
    {
        if (args == null)
        {
            return null;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var value = args[i];
            if (value != null && isServerBoundCommand(value))
            {
                return value;
            }
        }

        return null;
    }


}
