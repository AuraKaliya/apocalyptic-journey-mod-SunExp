using System;
using System.Linq;
using AuraShared.Core;
using Network.Command;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Network;

public sealed class SunExpRpcSender
{
    public static readonly SunExpRpcSender Unbound = new("", "", false, false, "", false);

    public SunExpRpcSender(
        string playerId,
        string playerName,
        bool isLobbyMember,
        bool isLobbyHost,
        string sourceHook,
        bool isAvailable)
    {
        PlayerId = (playerId ?? "").Trim();
        PlayerName = (playerName ?? "").Trim();
        IsLobbyMember = isLobbyMember;
        IsLobbyHost = isLobbyHost;
        SourceHook = (sourceHook ?? "").Trim();
        IsAvailable = isAvailable && PlayerId.Length > 0;
    }

    public string PlayerId { get; }

    public string PlayerName { get; }

    public bool IsLobbyMember { get; }

    public bool IsLobbyHost { get; }

    public string SourceHook { get; }

    public bool IsAvailable { get; }
}

public interface ISunExpServerBoundRpcCommand
{
    void BindServerSender(SunExpRpcSender sender);
}

public static class SunExpRpcAuthorityRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        Register(modConfig, "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase");
        Register(modConfig, "PlayerManager.UserCode_CmdReceiveRpcCommandExcludeOwner__RpcCommandBase");
        Register(modConfig, "PlayerManager.CmdReceiveRpcCommand");
        Register(modConfig, "PlayerManager.CmdReceiveRpcCommandExcludeOwner");
    }

    internal static SunExpRpcSender CreateLocalServerSender(string sourceHook)
    {
        return CreateSender(PlayerManager.Instance, sourceHook);
    }

    private static void Register(ModConfig modConfig, string target)
    {
        AuraSharedHooks.RegisterBefore(
            modConfig,
            target,
            context => BindSender(context, target),
            message => SunExpLog.Info("[RpcAuthority] " + message),
            message => SunExpLog.Warn("[RpcAuthority] " + message),
            safeInvoke: true);
    }

    private static void BindSender(ModHookContext context, string sourceHook)
    {
        var command = FindCommand(context.Arguments);
        if (command is not ISunExpServerBoundRpcCommand bound)
        {
            return;
        }

        bound.BindServerSender(CreateSender(context.Target, sourceHook));
    }

    private static RpcCommandBase? FindCommand(object[]? args)
    {
        return args?.OfType<RpcCommandBase>().FirstOrDefault();
    }

    private static SunExpRpcSender CreateSender(object? target, string sourceHook)
    {
        try
        {
            var playerManager = target as PlayerManager;
            var playerId = (playerManager?.PlayerId ?? "").Trim();
            var playerName = (playerManager?.playerInfo?.Name ?? "").Trim();
            var isMember = LobbyContains(playerId);
            return new SunExpRpcSender(
                playerId,
                playerName,
                isMember,
                isMember && IsLobbyHost(playerId),
                sourceHook,
                playerId.Length > 0);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[RpcAuthority] failed to resolve server sender: " + ex.Message);
            return SunExpRpcSender.Unbound;
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
