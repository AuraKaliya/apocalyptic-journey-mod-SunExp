using System;
using System.Collections;
using System.Collections.Generic;
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

    internal static AuraCgRpcSender FromAura(AuraRpcSender sender)
    {
        if (sender == null || !sender.IsAvailable)
        {
            return Unbound;
        }

        return new AuraCgRpcSender(
            sender.PlayerId,
            sender.PlayerName,
            sender.IsLobbyMember,
            sender.IsLobbyHost,
            sender.SourceHook,
            sender.IsAvailable);
    }
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

        AuraRpcAuthorityRuntime.Register(
            modConfig,
            "AuraCg",
            command => command is IAuraCgServerBoundRpcCommand,
            (command, sender) =>
                ((IAuraCgServerBoundRpcCommand)command).BindServerSender(
                    AuraCgRpcSender.FromAura(sender)),
            message => AuraCgLog.InfoOnce(
                "rpc-authority:" + message,
                "[RpcAuthority] " + message),
            message => AuraCgLog.WarnOnce(
                "rpc-authority-warn:" + message,
                "[RpcAuthority] " + message));
    }

    internal static AuraCgRpcSender CreateLocalServerSender(string sourceHook)
    {
        return AuraCgRpcSender.FromAura(
            AuraRpcAuthorityRuntime.CreateLocalServerSender(sourceHook));
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
        Playback = new SkillCgPlaybackSnapshot();
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
