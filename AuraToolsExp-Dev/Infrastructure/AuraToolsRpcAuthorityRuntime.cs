using System;
using System.Linq;
using AuraShared.Core;
using Network.Command;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsRpcAuthorityRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        Register(modConfig, "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase");
        Register(modConfig, "PlayerManager.UserCode_CmdReceiveRpcCommandExcludeOwner__RpcCommandBase");
        Register(modConfig, "PlayerManager.CmdReceiveRpcCommand");
        Register(modConfig, "PlayerManager.CmdReceiveRpcCommandExcludeOwner");
    }

    private static void Register(ModConfig modConfig, string target)
    {
        AuraSharedHooks.RegisterBefore(
            modConfig,
            target,
            context => BindSender(context, target),
            message => AuraToolsLog.Info("[RpcAuthority] " + message),
            message => AuraToolsLog.Warn("[RpcAuthority] " + message),
            safeInvoke: true);
    }

    private static void BindSender(ModHookContext context, string sourceHook)
    {
        var command = FindCommand(context.Arguments);
        if (command is not IAuraToolsServerBoundRpcCommand bound)
        {
            return;
        }

        bound.BindServerSender(CreateSender(context.Target, sourceHook));
    }

    private static RpcCommandBase? FindCommand(object[]? args)
    {
        return args?.OfType<RpcCommandBase>().FirstOrDefault();
    }

    private static AuraToolsRpcSender CreateSender(object? target, string sourceHook)
    {
        try
        {
            var playerManager = target as PlayerManager;
            var playerId = (playerManager?.PlayerId ?? "").Trim();
            var playerName = (playerManager?.playerInfo?.Name ?? "").Trim();
            var isMember = LobbyContains(playerId);
            return new AuraToolsRpcSender(
                playerId,
                playerName,
                isMember,
                isMember && IsLobbyHost(playerId),
                sourceHook,
                playerId.Length > 0);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[RpcAuthority] failed to resolve server sender: " + ex.Message);
            return AuraToolsRpcSender.Unbound;
        }
    }

    private static bool LobbyContains(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return false;
        }

        var players = GameServer.Instance?.LobbyInfo?.AddedPlayers;
        return players == null
               || players.Count == 0
               || players.Any(player => player != null && player.Id == playerId);
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
