using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraOnline.Shared;
using AuraShared.Core;
using Network.Command;

namespace AuraToolsExp.Dll.Infrastructure;

public sealed class AuraToolsRpcChunk
{
    public string TransferId { get; set; } = "";

    public int ChunkIndex { get; set; }

    public int ChunkCount { get; set; }

    public int TotalBytes { get; set; }

    public string Sha256 { get; set; } = "";

    public string PayloadBase64 { get; set; } = "";
}

public static class AuraToolsRpcTransport
{
    public const int SoftLimitBytes = AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes;
    public const int WarningLimitBytes = 48000;
    public const int ChunkRawBytes = 18000;
    private static readonly Dictionary<string, TrafficStat> TrafficByCommand = new(StringComparer.Ordinal);
    private static DateTime trafficWindowStartedUtc = DateTime.UtcNow;

    public static bool Send(
        PlayerManager? manager,
        RpcCommandBase command,
        string source,
        bool excludeOwner = false,
        bool measurePayload = true)
    {
        if (manager == null || command == null)
        {
            Log("skipped", source, command, 0, "manager or command missing");
            return false;
        }

        if (!IsLobbyCompatible(out var compatibilityReason))
        {
            Log("blocked", source, command, 0, compatibilityReason);
            return false;
        }

        var bytes = 0;
        if (measurePayload && !AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(command, out bytes, out var error))
        {
            Log("measure-failed", source, command, 0, error);
        }
        else if (measurePayload)
        {
            if (bytes > SoftLimitBytes)
            {
                Log("blocked", source, command, bytes, "payload exceeds soft limit");
                return false;
            }

            if (bytes > WarningLimitBytes)
            {
                Log("warning", source, command, bytes, "payload near soft limit");
            }
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

            RecordTraffic(manager, command, bytes, excludeOwner);
            return true;
        }
        catch (Exception ex)
        {
            Log("failed", source, command, bytes, ex.Message);
            return false;
        }
    }

    public static bool SendDeferred(
        PlayerManager? manager,
        RpcCommandBase command,
        string source,
        bool excludeOwner = false,
        bool measurePayload = true)
    {
        if (manager == null || command == null)
        {
            Log("skipped", source, command, 0, "manager or command missing");
            return false;
        }

        return AuraSharedFrameScheduler.Enqueue(new AuraSharedFrameEnqueueRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Source = "RpcTransport.Send:" + source,
            Action = () => Send(manager, command, source, excludeOwner, measurePayload)
        });
    }

    public static bool SendJsonChunksAsync(
        PlayerManager? manager,
        string source,
        string transferId,
        string payloadJson,
        Func<AuraToolsRpcChunk, RpcCommandBase> createCommand,
        bool excludeOwner = false)
    {
        if (manager == null || string.IsNullOrWhiteSpace(payloadJson) || createCommand == null)
        {
            Log("chunk-skipped", source, null, 0, "missing chunk input");
            return false;
        }

        return AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<PreparedChunkTransfer>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "RpcTransport.Chunk:" + transferId,
            Source = "RpcTransport.ChunkPrepare:" + source,
            Kind = AuraSharedBackgroundWorkKind.Cpu,
            Work = _ => PrepareChunks(transferId, payloadJson),
            ApplyOnMainThread = prepared => ScheduleChunkSend(manager, source, createCommand, excludeOwner, prepared),
            OnFailedOnMainThread = ex => Log("chunk-failed", source, null, 0, ex.Message)
        });
    }

    public static bool SendBytesChunksAsync(
        PlayerManager? manager,
        string source,
        string transferId,
        byte[] payload,
        Func<AuraToolsRpcChunk, RpcCommandBase> createCommand,
        bool excludeOwner = false)
    {
        if (manager == null || payload == null || payload.Length == 0 || createCommand == null)
        {
            Log("chunk-skipped", source, null, 0, "missing binary chunk input");
            return false;
        }

        return AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<PreparedChunkTransfer>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "RpcTransport.BinaryChunk:" + transferId,
            Source = "RpcTransport.BinaryChunkPrepare:" + source,
            Kind = AuraSharedBackgroundWorkKind.Cpu,
            Work = _ => PrepareChunks(transferId, payload),
            ApplyOnMainThread = prepared => ScheduleChunkSend(manager, source, createCommand, excludeOwner, prepared),
            OnFailedOnMainThread = ex => Log("chunk-failed", source, null, 0, ex.Message)
        });
    }

    public static string NewTransferId(string prefix)
    {
        return (string.IsNullOrWhiteSpace(prefix) ? "rpc" : prefix.Trim()) + "-" + Guid.NewGuid().ToString("N");
    }

    private static PreparedChunkTransfer PrepareChunks(string transferId, string payloadJson)
    {
        return PrepareChunks(transferId, Encoding.UTF8.GetBytes(payloadJson ?? ""));
    }

    private static PreparedChunkTransfer PrepareChunks(string transferId, byte[] payloadBytes)
    {
        var chunkCount = Math.Max(1, (payloadBytes.Length + ChunkRawBytes - 1) / ChunkRawBytes);
        var sha256 = Sha256Hex(payloadBytes);
        var chunks = new List<AuraToolsRpcChunk>(chunkCount);
        for (var index = 0; index < chunkCount; index++)
        {
            var offset = index * ChunkRawBytes;
            var count = Math.Min(ChunkRawBytes, payloadBytes.Length - offset);
            var chunkBytes = new byte[count];
            Buffer.BlockCopy(payloadBytes, offset, chunkBytes, 0, count);
            chunks.Add(new AuraToolsRpcChunk
            {
                TransferId = transferId,
                ChunkIndex = index,
                ChunkCount = chunkCount,
                TotalBytes = payloadBytes.Length,
                Sha256 = sha256,
                PayloadBase64 = Convert.ToBase64String(chunkBytes)
            });
        }

        return new PreparedChunkTransfer(payloadBytes.Length, chunks);
    }

    private static void ScheduleChunkSend(
        PlayerManager manager,
        string source,
        Func<AuraToolsRpcChunk, RpcCommandBase> createCommand,
        bool excludeOwner,
        PreparedChunkTransfer prepared)
    {
        Log("chunk-prepare", source, null, prepared.TotalBytes, "chunks=" + prepared.Chunks.Count);
        AuraSharedFrameStepRunner.Run(new AuraSharedFrameStepSequence
        {
            OwnerId = AuraToolsIds.ModId,
            Source = "RpcTransport.ChunkSend:" + source,
            DeduplicateKey = "RpcTransport.ChunkSend:" + prepared.Chunks.First().TransferId,
            Phase = AuraSharedFramePhase.Background,
            EstimatedCost = 1,
            Steps = prepared.Chunks.Select(chunk => new AuraSharedFrameStep
            {
                Name = chunk.ChunkIndex.ToString(),
                DelayFrames = 1,
                Action = () => Send(manager, createCommand(chunk), source + ".chunk", excludeOwner, measurePayload: false)
            }).ToList()
        });
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private static void Log(string eventName, string source, RpcCommandBase? command, int bytes, string detail)
    {
        var message = "[RpcTransport] "
                      + eventName
                      + "; source="
                      + (source ?? "")
                      + "; command="
                      + (command?.GetType().FullName ?? "")
                      + "; bytes="
                      + bytes
                      + "; softLimit="
                      + SoftLimitBytes
                      + "; detail="
                      + (detail ?? "");
        if (string.Equals(eventName, "chunk-prepare", StringComparison.Ordinal))
        {
            AuraToolsLog.Info(message);
            return;
        }

        AuraToolsLog.Warn(message);
    }

    private static void RecordTraffic(PlayerManager manager, RpcCommandBase command, int bytes, bool excludeOwner)
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

        var elapsedMs = Math.Max(1L, (long)(now - trafficWindowStartedUtc).TotalMilliseconds);
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
        AuraToolsLog.Info("[NetworkTraffic] windowMs=" + elapsedMs + "; lobby=" + lobbyCount + "; top=" + top + ".");
        TrafficByCommand.Clear();
        trafficWindowStartedUtc = now;
    }

    public static bool IsLobbyCompatible(out string reason)
    {
        reason = "";
        var serverPlayers = GameServer.Instance?.LobbyInfo?.AddedPlayers;
        var clientPlayers = PlayerManager.Instance?.LobbyInfos?.AddedPlayers;
        var players = serverPlayers != null
            ? serverPlayers.Cast<object>().ToList()
            : clientPlayers?.Cast<object>().ToList();
        if (players == null || players.Count <= 1)
        {
            return true;
        }

        var state = AuraChatModSyncSnapshot.BuildState(
            players,
            AuraToolsIds.ModId,
            PlayerManager.Instance?.PlayerId ?? "");
        var compatibility = AuraToolsPeerCompatibility.Evaluate(
            state.Players.Select(player => new AuraToolsPeerModState
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                ToolEnabled = (player.Mods ?? new List<AuraChatModSnapshot>())
                    .Any(mod => mod.Enabled
                                && (string.Equals(
                                        mod.ModId,
                                        AuraToolsIds.ModId,
                                        StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(
                                        mod.ModName,
                                        AuraToolsIds.ModId,
                                        StringComparison.OrdinalIgnoreCase)))
            }));
        if (compatibility.Compatible)
        {
            return true;
        }

        reason = "AuraTools RPC disabled because lobby peers are missing the tool: "
                 + string.Join(", ", compatibility.MissingPeers);
        return false;
    }

    private sealed class TrafficStat
    {
        public long Commands { get; set; }

        public long PayloadBytes { get; set; }

        public long EstimatedDeliveries { get; set; }

        public long EstimatedDeliveredBytes { get; set; }
    }
}

internal sealed class PreparedChunkTransfer
{
    public PreparedChunkTransfer(int totalBytes, List<AuraToolsRpcChunk> chunks)
    {
        TotalBytes = totalBytes;
        Chunks = chunks ?? new List<AuraToolsRpcChunk>();
    }

    public int TotalBytes { get; }

    public List<AuraToolsRpcChunk> Chunks { get; }
}
