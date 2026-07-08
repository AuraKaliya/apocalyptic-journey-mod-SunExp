using System;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
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

    internal static SunExpRpcSender FromAura(AuraRpcSender sender)
    {
        if (sender == null || !sender.IsAvailable)
        {
            return Unbound;
        }

        return new SunExpRpcSender(
            sender.PlayerId,
            sender.PlayerName,
            sender.IsLobbyMember,
            sender.IsLobbyHost,
            sender.SourceHook,
            sender.IsAvailable);
    }
}

public interface ISunExpServerBoundRpcCommand
{
    void BindServerSender(SunExpRpcSender sender);
}

public static class SunExpRpcAuthorityRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraRpcAuthorityRuntime.Register(
            modConfig,
            SunExpIds.ModId,
            command => command is ISunExpServerBoundRpcCommand,
            (command, sender) => ((ISunExpServerBoundRpcCommand)command).BindServerSender(SunExpRpcSender.FromAura(sender)),
            message => SunExpLog.Info("[RpcAuthority] " + message),
            message => SunExpLog.Warn("[RpcAuthority] " + message));
    }

    internal static SunExpRpcSender CreateLocalServerSender(string sourceHook)
    {
        return SunExpRpcSender.FromAura(AuraRpcAuthorityRuntime.CreateLocalServerSender(sourceHook));
    }
}
