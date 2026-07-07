using System;
using System.Collections.Generic;
using System.Linq;
using Network.Command;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Network;

public static class SunExpNetworkRuntime
{
    public static bool IsClientOnly()
    {
        try
        {
            var manager = PlayerManager.Instance;
            return manager != null && manager.isClient && !manager.isServer;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsServer()
    {
        try
        {
            return PlayerManager.Instance?.isServer == true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsMultiplayerSession()
    {
        try
        {
            var manager = PlayerManager.Instance;
            return PlayerApi.IsMultiplayerSession()
                || manager != null && (manager.isClient || manager.isServer);
        }
        catch
        {
            return PlayerApi.IsMultiplayerSession();
        }
    }

    public static bool HasRemotePlayers()
    {
        try
        {
            return PlayerApi.IsMultiplayerSession() || LobbyPlayerIds().Count > 1;
        }
        catch
        {
            return false;
        }
    }

    public static string LocalPlayerId()
    {
        try
        {
            return (PlayerManager.Instance?.PlayerId ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    public static bool IsLocalPlayer(string playerId)
    {
        var local = LocalPlayerId();
        return !string.IsNullOrWhiteSpace(local)
            && string.Equals(local, (playerId ?? "").Trim(), StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> LobbyPlayerIds()
    {
        try
        {
            var ids = GameServer.Instance?.LobbyInfo?.AddedPlayers?
                .Select(player => player?.Id ?? "")
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids != null)
            {
                return ids;
            }

            return Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool Send(RpcCommandBase command, string source, bool excludeOwner = false)
    {
        if (command == null)
        {
            return false;
        }

        var manager = PlayerManager.Instance;
        if (manager == null)
        {
            SunExpLog.Debug("[SunExpRpc] send skipped from " + source + ": PlayerManager unavailable.");
            return false;
        }

        try
        {
            if (excludeOwner)
            {
                manager.SendRpcCommandExcludeOwner(command);
            }
            else
            {
                manager.SendRpcCommand(command);
            }

            SunExpLog.Debug("[SunExpRpc] sent "
                + command.GetType().Name
                + " from "
                + source
                + "; excludeOwner="
                + excludeOwner
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SunExpRpc] send failed from "
                + source
                + "; command="
                + command.GetType().Name
                + "; error="
                + ex.Message);
            return false;
        }
    }
}
