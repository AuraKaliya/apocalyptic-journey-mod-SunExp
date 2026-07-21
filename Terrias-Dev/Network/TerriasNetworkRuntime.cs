using System;
using System.Collections.Generic;
using AuraShared.Core;
using System.Linq;
using Network.Command;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Network;

public static class TerriasNetworkRuntime
{
    private static readonly Dictionary<string, TrafficStat> TrafficByCommand = new(StringComparer.Ordinal);
    private static DateTime trafficWindowStartedUtc = DateTime.UtcNow;

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
            TerriasLog.Debug("[TerriasRpc] send skipped from " + source + ": PlayerManager unavailable.");
            return false;
        }

        if (!AuraSharedPayloadBudget.FitsSoftLimit(
                command,
                AuraSharedPayloadBudget.DefaultSoftLimitBytes,
                out var payloadBytes,
                out var payloadError))
        {
            TerriasLog.Warn("[TerriasRpc] send blocked from " + source
                + "; command=" + command.GetType().Name
                + "; bytes=" + payloadBytes
                + "; error=" + payloadError + ".");
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

            TerriasLog.Debug("[TerriasRpc] sent "
                + command.GetType().Name
                + " from "
                + source
                + "; bytes="
                + payloadBytes
                + "; excludeOwner="
                + excludeOwner
                + ".");
            RecordTraffic(command, payloadBytes, excludeOwner);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[TerriasRpc] send failed from "
                + source
                + "; command="
                + command.GetType().Name
                + "; error="
                + ex.Message);
            return false;
        }
    }

    private static void RecordTraffic(RpcCommandBase command, int bytes, bool excludeOwner)
    {
        var name = command.GetType().Name;
        if (!TrafficByCommand.TryGetValue(name, out var stat))
        {
            stat = new TrafficStat();
            TrafficByCommand[name] = stat;
        }

        var lobbyCount = Math.Max(1, GameServer.Instance?.LobbyInfo?.AddedPlayers?.Count ?? 1);
        var recipients = Math.Max(0, lobbyCount - (excludeOwner ? 1 : 0));
        stat.Commands++;
        stat.PayloadBytes += Math.Max(0, bytes);
        stat.EstimatedDeliveries += recipients;
        stat.EstimatedDeliveredBytes += (long)Math.Max(0, bytes) * recipients;

        var now = DateTime.UtcNow;
        if (now - trafficWindowStartedUtc < TimeSpan.FromSeconds(10))
        {
            return;
        }

        var top = string.Join(", ", TrafficByCommand
            .OrderByDescending(pair => pair.Value.EstimatedDeliveredBytes)
            .Take(6)
            .Select(pair => pair.Key
                            + "="
                            + pair.Value.Commands
                            + "cmd/"
                            + pair.Value.PayloadBytes
                            + "B/"
                            + pair.Value.EstimatedDeliveries
                            + "deliveries"));
        TerriasLog.Info("[NetworkTraffic] windowMs="
                        + Math.Max(1L, (long)(now - trafficWindowStartedUtc).TotalMilliseconds)
                        + "; lobby="
                        + lobbyCount
                        + "; top="
                        + top
                        + ".");
        TrafficByCommand.Clear();
        trafficWindowStartedUtc = now;
    }

    private sealed class TrafficStat
    {
        public long Commands { get; set; }

        public long PayloadBytes { get; set; }

        public long EstimatedDeliveries { get; set; }

        public long EstimatedDeliveredBytes { get; set; }
    }
}
