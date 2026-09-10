using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuraShared.Core;

public enum AuraMutationDecision { Apply, Duplicate, Conflict, TokenConflict, Invalid }

public sealed class AuraMutationReceipt
{
    public string RequestId { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public long Version { get; set; }
}

public sealed class AuraVersionedReceipt
{
    public long Version { get; set; }
    public List<AuraMutationReceipt> Recent { get; set; } = new();
    public AuraMutationDecision Decide(string requestId, string payloadHash, long expectedVersion)
    {
        if (!Guid.TryParseExact(requestId, "N", out _) || payloadHash?.Length != 64 || expectedVersion < 0)
            return AuraMutationDecision.Invalid;
        var existing = Recent?.FirstOrDefault(item => item.RequestId == requestId);
        if (existing != null) return existing.PayloadHash == payloadHash ? AuraMutationDecision.Duplicate : AuraMutationDecision.TokenConflict;
        return Version == expectedVersion ? AuraMutationDecision.Apply : AuraMutationDecision.Conflict;
    }
    public AuraVersionedReceipt Commit(string requestId, string payloadHash)
    {
        var next = new AuraVersionedReceipt { Version = checked(Version + 1) };
        next.Recent.AddRange((Recent ?? new List<AuraMutationReceipt>()).Skip(Math.Max(0, (Recent?.Count ?? 0) - 63)));
        next.Recent.Add(new AuraMutationReceipt { RequestId = requestId, PayloadHash = payloadHash, Version = next.Version });
        return next;
    }
    public static string Hash(string payload)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload ?? ""))).Replace("-", "").ToLowerInvariant();
    }
}
