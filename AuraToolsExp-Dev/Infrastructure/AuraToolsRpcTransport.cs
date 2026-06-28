using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AuraShared.Core;
using Network.Command;
using UnityEngine;
using Object = UnityEngine.Object;

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

    private const string DispatcherName = "AuraTools.RpcTransportDispatcher";
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();
    private static AuraToolsRpcTransportDispatcher? dispatcher;

    public static bool Send(
        PlayerManager? manager,
        RpcCommandBase command,
        string source,
        bool excludeOwner = false)
    {
        if (manager == null || command == null)
        {
            Log("skipped", source, command, 0, "manager or command missing");
            return false;
        }

        if (!AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(command, out var bytes, out var error))
        {
            Log("measure-failed", source, command, 0, error);
        }
        else
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

        EnsureDispatcher();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                var chunkCount = Math.Max(1, (payloadBytes.Length + ChunkRawBytes - 1) / ChunkRawBytes);
                var sha256 = Sha256Hex(payloadBytes);
                Log("chunk-prepare", source, null, payloadBytes.Length, "chunks=" + chunkCount);

                for (var index = 0; index < chunkCount; index++)
                {
                    var offset = index * ChunkRawBytes;
                    var count = Math.Min(ChunkRawBytes, payloadBytes.Length - offset);
                    var chunkBytes = new byte[count];
                    Buffer.BlockCopy(payloadBytes, offset, chunkBytes, 0, count);
                    var chunk = new AuraToolsRpcChunk
                    {
                        TransferId = transferId,
                        ChunkIndex = index,
                        ChunkCount = chunkCount,
                        TotalBytes = payloadBytes.Length,
                        Sha256 = sha256,
                        PayloadBase64 = Convert.ToBase64String(chunkBytes)
                    };

                    MainThreadActions.Enqueue(() => Send(manager, createCommand(chunk), source + ".chunk", excludeOwner));
                }
            }
            catch (Exception ex)
            {
                Log("chunk-failed", source, null, 0, ex.Message);
            }
        });
        return true;
    }

    public static string NewTransferId(string prefix)
    {
        return (string.IsNullOrWhiteSpace(prefix) ? "rpc" : prefix.Trim()) + "-" + Guid.NewGuid().ToString("N");
    }

    internal static void Pump()
    {
        var limit = 32;
        while (limit-- > 0 && MainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[RpcTransport] dispatcher action failed: " + ex.Message);
            }
        }
    }

    private static void EnsureDispatcher()
    {
        if (dispatcher != null)
        {
            return;
        }

        var existing = GameObject.Find(DispatcherName);
        if (existing != null)
        {
            dispatcher = existing.GetComponent<AuraToolsRpcTransportDispatcher>()
                         ?? existing.AddComponent<AuraToolsRpcTransportDispatcher>();
            return;
        }

        var go = new GameObject(DispatcherName);
        Object.DontDestroyOnLoad(go);
        dispatcher = go.AddComponent<AuraToolsRpcTransportDispatcher>();
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

public sealed class AuraToolsRpcTransportDispatcher : MonoBehaviour
{
    private void Update()
    {
        AuraToolsRpcTransport.Pump();
    }
}
