using System;
using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedDiagnostics
{
    public static AuraSharedDiagnosticRecord Create(
        string service,
        string ownerModId,
        string level,
        string phase,
        string message,
        bool? isAuthority = null,
        string correlationId = "",
        string detail = "")
    {
        return new AuraSharedDiagnosticRecord
        {
            TimestampUtc = DateTime.UtcNow.ToString("O"),
            Service = service ?? "",
            OwnerModId = ownerModId ?? "",
            Level = level ?? "Info",
            Phase = phase ?? "",
            Message = message ?? "",
            IsAuthority = isAuthority,
            CorrelationId = correlationId ?? "",
            Detail = detail ?? ""
        };
    }

    public static void Info(string service, string ownerModId, string phase, string message, bool? isAuthority = null, string correlationId = "")
    {
        Write(Create(service, ownerModId, "Info", phase, message, isAuthority, correlationId));
    }

    public static void Warn(string service, string ownerModId, string phase, string message, bool? isAuthority = null, string correlationId = "")
    {
        Write(Create(service, ownerModId, "Warn", phase, message, isAuthority, correlationId));
    }

    public static void Error(string service, string ownerModId, string phase, string message, Exception? exception = null, bool? isAuthority = null, string correlationId = "")
    {
        var record = Create(service, ownerModId, "Error", phase, message, isAuthority, correlationId, exception?.ToString() ?? "");
        Write(record);
    }

    public static void Write(AuraSharedDiagnosticRecord record)
    {
        var source = string.IsNullOrWhiteSpace(record.Service) ? "AuraShared" : record.Service.Trim();
        var owner = string.IsNullOrWhiteSpace(record.OwnerModId) ? "UnknownOwner" : record.OwnerModId.Trim();
        var phase = string.IsNullOrWhiteSpace(record.Phase) ? "" : " phase=" + record.Phase.Trim();
        var authority = record.IsAuthority.HasValue ? " authority=" + record.IsAuthority.Value : "";
        var correlation = string.IsNullOrWhiteSpace(record.CorrelationId) ? "" : " cid=" + record.CorrelationId.Trim();
        var detail = string.IsNullOrWhiteSpace(record.Detail) ? "" : " detail=" + record.Detail.Trim();
        var message = "[" + source + "] owner=" + owner + " level=" + record.Level + phase + authority + correlation + " " + record.Message + detail;

        if (string.Equals(record.Level, "Error", StringComparison.OrdinalIgnoreCase))
        {
            AuraSharedLog.Error(owner, message);
        }
        else if (string.Equals(record.Level, "Warn", StringComparison.OrdinalIgnoreCase))
        {
            AuraSharedLog.Warn(owner, message);
        }
        else
        {
            AuraSharedLog.Info(owner, message);
        }
    }
}

public sealed class AuraSharedDiagnosticRecord
{
    [JsonProperty("timestampUtc")]
    public string TimestampUtc { get; set; } = "";

    [JsonProperty("service")]
    public string Service { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("level")]
    public string Level { get; set; } = "Info";

    [JsonProperty("phase")]
    public string Phase { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    [JsonProperty("isAuthority")]
    public bool? IsAuthority { get; set; }

    [JsonProperty("correlationId")]
    public string CorrelationId { get; set; } = "";

    [JsonProperty("detail")]
    public string Detail { get; set; } = "";
}
