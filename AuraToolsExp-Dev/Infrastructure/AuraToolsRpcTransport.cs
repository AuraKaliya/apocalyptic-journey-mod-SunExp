using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

    public static string NewTransferId(string prefix)
    {
        return (string.IsNullOrWhiteSpace(prefix) ? "rpc" : prefix.Trim()) + "-" + Guid.NewGuid().ToString("N");
    }

    private static PreparedChunkTransfer PrepareChunks(string transferId, string payloadJson)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson ?? "");
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
