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
        for (var i = 0; i < DefaultReceiveHookTargets.Length; i++)
        {
            Register(modConfig, ownerModId, DefaultReceiveHookTargets[i], isServerBoundCommand, bindServerSender, info, warn);
        }
    }

    public static AuraRpcSender CreateLocalServerSender(string sourceHook)
    {
        return CreateSender(PlayerManager.Instance, sourceHook, null);
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
        AuraSharedHooks.RegisterBefore(
            modConfig,
            target,
            context => BindSender(context, target, isServerBoundCommand, bindServerSender, warn),
            info,
            warn,
            safeInvoke: true);
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

        bindServerSender(command, CreateSender(context.Target, sourceHook, warn));
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

    private static AuraRpcSender CreateSender(object? target, string sourceHook, Action<string>? warn)
    {
        try
        {
            var playerManager = target as PlayerManager;
            var playerId = (playerManager?.PlayerId ?? "").Trim();
            var playerName = (playerManager?.playerInfo?.Name ?? "").Trim();
            var isMember = LobbyContains(playerId);
            return new AuraRpcSender(
                playerId,
                playerName,
                isMember,
                isMember && IsLobbyHost(playerId),
                sourceHook,
                playerId.Length > 0);
        }
        catch (Exception ex)
        {
            warn?.Invoke("[RpcAuthority] failed to resolve server sender: " + ex.Message);
            return AuraRpcSender.Unbound;
        }
    }

    private static bool LobbyContains(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return false;
        }

        var players = GameServer.Instance?.LobbyInfo?.AddedPlayers;
        if (players == null || players.Count == 0)
        {
            return true;
        }

        for (var i = 0; i < players.Count; i++)
        {
            if (players[i] != null && players[i].Id == playerId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLobbyHost(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return false;
        }

        var players = GameServer.Instance?.LobbyInfo?.AddedPlayers;
        return players == null
               || players.Count == 0
               || string.Equals(players[0].Id, playerId, StringComparison.Ordinal);
    }
}
