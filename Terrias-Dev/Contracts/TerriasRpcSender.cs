using System;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Contracts;

public sealed class TerriasRpcSender
{
    public static readonly TerriasRpcSender Unbound = new("", "", false, false, "", false);

    public TerriasRpcSender(
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

    internal static TerriasRpcSender FromAura(AuraRpcSender sender)
    {
        if (sender == null || !sender.IsAvailable)
        {
            return Unbound;
        }

        return new TerriasRpcSender(
            sender.PlayerId,
            sender.PlayerName,
            sender.IsLobbyMember,
            sender.IsLobbyHost,
            sender.SourceHook,
            sender.IsAvailable);
    }
}
