using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Network.Command;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraCg.Shared;

public sealed class AuraCgRpcSender
{
    public static readonly AuraCgRpcSender Unbound = new("", "", false, false, "", false);

    public AuraCgRpcSender(
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

public interface IAuraCgServerBoundRpcCommand
{
    void BindServerSender(AuraCgRpcSender sender);
}

public static class AuraCgRpcAuthorityRuntime
{
    private static readonly HashSet<int> RegisteredConfigs = new();

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null || !RegisteredConfigs.Add(modConfig.GetHashCode()))
        {
            return;
        }

        Register(modConfig, "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase");
        Register(modConfig, "PlayerManager.UserCode_CmdReceiveRpcCommandExcludeOwner__RpcCommandBase");
        Register(modConfig, "PlayerManager.CmdReceiveRpcCommand");
        Register(modConfig, "PlayerManager.CmdReceiveRpcCommandExcludeOwner");
    }

    internal static AuraCgRpcSender CreateLocalServerSender(string sourceHook)
    {
        return CreateSender(PlayerManager.Instance, sourceHook);
    }

    private static void Register(ModConfig modConfig, string target)
    {
        AuraSharedHooks.RegisterBefore(
            modConfig,
            target,
            context => BindSender(context, target),
            message => AuraCgLog.InfoOnce("rpc-authority:" + target + ":" + message, "[RpcAuthority] " + message),
            message => AuraCgLog.WarnOnce("rpc-authority-warn:" + target + ":" + message, "[RpcAuthority] " + message),
            safeInvoke: true);
    }

    private static void BindSender(ModHookContext context, string sourceHook)
    {
        var command = FindCommand(context.Arguments);
        if (command is not IAuraCgServerBoundRpcCommand bound)
        {
            return;
        }

        bound.BindServerSender(CreateSender(context.Target, sourceHook));
    }

    private static RpcCommandBase? FindCommand(object[]? args)
    {
        return args?.OfType<RpcCommandBase>().FirstOrDefault();
    }

    private static AuraCgRpcSender CreateSender(object? target, string sourceHook)
    {
        try
        {
            var playerManager = target as PlayerManager;
            var playerId = (playerManager?.PlayerId ?? "").Trim();
            var playerName = (playerManager?.playerInfo?.Name ?? "").Trim();
            var isMember = LobbyContains(playerId);
            return new AuraCgRpcSender(
                playerId,
                playerName,
                isMember,
                isMember && IsLobbyHost(playerId),
                sourceHook,
                playerId.Length > 0);
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("rpc-authority-sender-failed", "[RpcAuthority] failed to resolve server sender: " + ex.Message);
            return AuraCgRpcSender.Unbound;
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

[Serializable]
public sealed class RpcSkillCgPlaybackRequest : RpcCommandBase, IAuraCgServerBoundRpcCommand
{
    private AuraCgRpcSender serverSender = AuraCgRpcSender.Unbound;

    public SkillCgPlaybackSnapshot Playback { get; set; } = new();

    public RpcSkillCgPlaybackRequest()
    {
    }

    public RpcSkillCgPlaybackRequest(SkillCgPlaybackSnapshot playback)
    {
        Playback = playback ?? new SkillCgPlaybackSnapshot();
    }

    public void BindServerSender(AuraCgRpcSender sender)
    {
        serverSender = sender ?? AuraCgRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        SkillCgArbiterRuntime.ApplyServerPlaybackRequest(Playback, serverSender);
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcSkillCgPlayback : RpcCommandBase
{
    public SkillCgPlaybackSnapshot Playback { get; set; } = new();

    public RpcSkillCgPlayback()
    {
    }

    public RpcSkillCgPlayback(SkillCgPlaybackSnapshot playback)
    {
        Playback = playback ?? new SkillCgPlaybackSnapshot();
    }

    public override void RpcExecute()
    {
        SkillCgArbiterRuntime.ApplyNetworkPlayback(Playback, "RpcSkillCgPlayback");
    }
}

[Serializable]
public sealed class RpcSkillCgFightSession : RpcCommandBase, IAuraCgServerBoundRpcCommand
{
    private AuraCgRpcSender serverSender = AuraCgRpcSender.Unbound;

    public RpcSkillCgFightSession()
    {
    }

    public RpcSkillCgFightSession(string ownerModId, string fightToken)
    {
        OwnerModId = ownerModId ?? "";
        FightToken = fightToken ?? "";
    }

    public string OwnerModId { get; set; } = "";

    public string FightToken { get; set; } = "";

    public bool Accepted { get; set; }

    public void BindServerSender(AuraCgRpcSender sender)
    {
        serverSender = sender ?? AuraCgRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = serverSender.IsAvailable && serverSender.IsLobbyMember && serverSender.IsLobbyHost;
        if (Accepted)
        {
            SkillCgArbiterRuntime.ApplyFightSession(OwnerModId, FightToken, "RpcSkillCgFightSession.CmdExecute");
        }
    }

    public override void RpcExecute()
    {
        if (Accepted)
        {
            SkillCgArbiterRuntime.ApplyFightSession(OwnerModId, FightToken, "RpcSkillCgFightSession.RpcExecute");
        }
    }
}
