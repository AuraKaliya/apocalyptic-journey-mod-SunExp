using System;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedOperationLog
{
    private static readonly ConcurrentDictionary<string, object> FileGates = new(StringComparer.OrdinalIgnoreCase);

    public static void Write(string rootDirectory, AuraSharedOperationRecord record)
    {
        try
        {
            if (record == null || string.IsNullOrWhiteSpace(rootDirectory))
            {
                return;
            }

            record.TimestampUtc = string.IsNullOrWhiteSpace(record.TimestampUtc)
                ? DateTime.UtcNow.ToString("O")
                : record.TimestampUtc;
            record.OperationId = string.IsNullOrWhiteSpace(record.OperationId)
                ? Guid.NewGuid().ToString("N")
                : record.OperationId;

            var directory = Path.Combine(rootDirectory, "Logs", "Operations");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");
            var gate = FileGates.GetOrAdd(path, _ => new object());
            var line = AuraSharedJson.Serialize(record).Replace("\r", "").Replace("\n", "") + Environment.NewLine;
            lock (gate)
            {
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Operation logs are diagnostic only; never let them change runtime behavior.
        }
    }

    public static AuraSharedOperationRecord Create(
        string operationId,
        string transactionId,
        string ownerModId,
        string system,
        string logicalId,
        string kind,
        string phase,
        string result,
        string message,
        long revision = 0,
        long elapsedMs = 0)
    {
        return new AuraSharedOperationRecord
        {
            OperationId = operationId ?? "",
            TransactionId = transactionId ?? "",
            OwnerModId = ownerModId ?? "",
            System = system ?? "",
            LogicalId = logicalId ?? "",
            Kind = kind ?? "",
            Phase = phase ?? "",
            Result = result ?? "",
            Message = message ?? "",
            Revision = revision,
            ElapsedMs = elapsedMs
        };
    }
}

public sealed class AuraSharedOperationRecord
{
    [JsonProperty("timestampUtc")]
    public string TimestampUtc { get; set; } = "";

    [JsonProperty("operationId")]
    public string OperationId { get; set; } = "";

    [JsonProperty("transactionId")]
    public string TransactionId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("logicalId")]
    public string LogicalId { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("phase")]
    public string Phase { get; set; } = "";

    [JsonProperty("result")]
    public string Result { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    [JsonProperty("elapsedMs")]
    public long ElapsedMs { get; set; }
}
