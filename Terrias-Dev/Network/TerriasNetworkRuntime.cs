using Terrias.Dll.Contracts;
using System;
using System.Collections.Generic;
using AuraShared.Core;
using System.Linq;
using Network.Command;
using Terrias.Dll.Application;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Network;

public static class TerriasNetworkRuntime
{
    private static readonly Dictionary<string, TrafficStat> TrafficByCommand = new(StringComparer.Ordinal);
    private static DateTime trafficWindowStartedUtc = DateTime.UtcNow;

    public static bool NetworkActive() => TerriasNetworkSession.NetworkActive();
    public static bool IsClientOnly() => TerriasNetworkSession.IsClientOnly();
    public static bool IsServer() => TerriasNetworkSession.IsServer();
    public static bool HasRemotePlayers() => TerriasNetworkSession.HasRemotePlayers();
    public static string LocalPlayerId() => TerriasNetworkSession.LocalPlayerId();
    public static IReadOnlyList<string> LobbyPlayerIds() => TerriasNetworkSession.LobbyPlayerIds();
    public static bool IsLocalPlayer(string id) => TerriasNetworkSession.IsLocalPlayer(id);

    public static bool Send(RpcCommandBase command, string source, bool excludeOwner = false)
    {
        return TrySend(command, source, excludeOwner) == TerriasNetworkSendStatus.Sent;
    }

    public static TerriasNetworkSendStatus TrySend(
        RpcCommandBase command,
        string source,
        bool excludeOwner = false)
    {
        if (command == null)
        {
            return TerriasNetworkSendStatus.NotAttempted;
        }

        var manager = PlayerManager.Instance;
        if (manager == null)
        {
            TerriasLog.Debug("[TerriasRpc] send skipped from " + source + ": PlayerManager unavailable.");
            return TerriasNetworkSendStatus.NotAttempted;
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
            return TerriasNetworkSendStatus.NotAttempted;
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
            return TerriasNetworkSendStatus.Sent;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[TerriasRpc] send failed from "
                + source
                + "; command="
                + command.GetType().Name
                + "; error="
                + ex.Message);
            // The underlying transport can throw after it accepted a command.
            // Callers must retry with the same idempotency token, not refund.
            return TerriasNetworkSendStatus.DispatchUnknown;
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
