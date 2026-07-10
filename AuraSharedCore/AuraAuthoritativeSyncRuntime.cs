using System;
using System.Collections.Generic;

namespace AuraShared.Core;

public sealed class AuraAuthoritativeSyncDomainOptions
{
    public string OwnerModId { get; set; } = "";

    public string DomainId { get; set; } = "";

    public double SnapshotRequestThrottleSeconds { get; set; } = 1.0d;

    public int MaxResolvedTokens { get; set; } = 256;
}

public sealed class AuraAuthoritativeSyncDomain
{
    private readonly object sync = new();
    private readonly HashSet<string> resolvedTokens = new(StringComparer.Ordinal);
    private readonly Queue<string> resolvedTokenOrder = new();
    private int nextToken = Environment.TickCount;
    private int localSession = 1;
    private int lastRemoteSession;
    private bool requireFreshRemoteSession;
    private DateTime lastSnapshotRequestAtUtc = DateTime.MinValue;

    internal AuraAuthoritativeSyncDomain(AuraAuthoritativeSyncDomainOptions options)
    {
        OwnerModId = (options.OwnerModId ?? "").Trim();
        DomainId = (options.DomainId ?? "").Trim();
        SnapshotRequestThrottleSeconds = Math.Max(0.05d, options.SnapshotRequestThrottleSeconds);
        MaxResolvedTokens = Math.Max(16, options.MaxResolvedTokens);
    }

    public string OwnerModId { get; }

    public string DomainId { get; }

    public double SnapshotRequestThrottleSeconds { get; }

    public int MaxResolvedTokens { get; }

    public int CurrentSession
    {
        get
        {
            lock (sync)
            {
                return localSession;
            }
        }
    }

    public int NextToken()
    {
        lock (sync)
        {
            unchecked
            {
                nextToken++;
                if (nextToken == 0)
                {
                    nextToken = 1;
                }

                return nextToken;
            }
        }
    }

    public bool TryClaimToken(int token)
    {
        return TryClaimToken("", token);
    }

    public bool TryClaimToken(string senderId, int token)
    {
        if (token == 0)
        {
            return true;
        }

        lock (sync)
        {
            var key = (senderId ?? "").Trim() + "|" + token;
            if (!resolvedTokens.Add(key))
            {
                return false;
            }

            resolvedTokenOrder.Enqueue(key);
            while (resolvedTokenOrder.Count > MaxResolvedTokens)
            {
                resolvedTokens.Remove(resolvedTokenOrder.Dequeue());
            }

            return true;
        }
    }

    public bool TryBeginSnapshotRequest()
    {
        lock (sync)
        {
            var now = DateTime.UtcNow;
            if ((now - lastSnapshotRequestAtUtc).TotalSeconds < SnapshotRequestThrottleSeconds)
            {
                return false;
            }

            lastSnapshotRequestAtUtc = now;
            return true;
        }
    }

    public bool AcceptRemoteSnapshotSession(int remoteSession)
    {
        if (remoteSession <= 0)
        {
            return true;
        }

        lock (sync)
        {
            if (requireFreshRemoteSession && lastRemoteSession > 0 && remoteSession <= lastRemoteSession)
            {
                return false;
            }

            if (remoteSession < lastRemoteSession)
            {
                return false;
            }

            if (remoteSession > lastRemoteSession)
            {
                lastRemoteSession = remoteSession;
            }

            requireFreshRemoteSession = false;
            return true;
        }
    }

    public void ResetSession()
    {
        lock (sync)
        {
            resolvedTokens.Clear();
            resolvedTokenOrder.Clear();
            lastSnapshotRequestAtUtc = DateTime.MinValue;
            requireFreshRemoteSession = true;
            unchecked
            {
                localSession++;
                if (localSession <= 0)
                {
                    localSession = 1;
                }
            }
        }
    }
}

public static class AuraAuthoritativeSyncRuntime
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, AuraAuthoritativeSyncDomain> Domains = new(StringComparer.Ordinal);

    public static AuraAuthoritativeSyncDomain RegisterDomain(AuraAuthoritativeSyncDomainOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var key = DomainKey(options.OwnerModId, options.DomainId);
        lock (Sync)
        {
            if (!Domains.TryGetValue(key, out var domain))
            {
                domain = new AuraAuthoritativeSyncDomain(options);
                Domains[key] = domain;
            }

            return domain;
        }
    }

    public static AuraAuthoritativeSyncDomain Domain(string ownerModId, string domainId)
    {
        var key = DomainKey(ownerModId, domainId);
        lock (Sync)
        {
            if (Domains.TryGetValue(key, out var domain))
            {
                return domain;
            }
        }

        return RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = ownerModId,
            DomainId = domainId
        });
    }

    private static string DomainKey(string ownerModId, string domainId)
    {
        return (ownerModId ?? "").Trim() + "::" + (domainId ?? "").Trim();
    }
}
