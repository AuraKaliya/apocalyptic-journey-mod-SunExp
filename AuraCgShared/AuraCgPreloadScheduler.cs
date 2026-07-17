using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal enum AuraCgPreloadEnqueueResult
{
    Accepted,
    AlreadyCached,
    Duplicate,
    CapacityExceeded,
    Invalid
}

internal sealed class AuraCgPreloadWork<TRequest>
    where TRequest : class
{
    public AuraCgPreloadWork(string key, string ownerId, TRequest request)
    {
        Key = key;
        OwnerId = ownerId;
        Request = request;
    }

    public string Key { get; }

    public string OwnerId { get; }

    public TRequest Request { get; }
}

internal sealed class AuraCgPreloadScheduler<TRequest>
    where TRequest : class
{
    private readonly int maximumPending;
    private readonly int maximumPendingPerOwner;
    private readonly int maximumConcurrent;
    private readonly Dictionary<string, Queue<AuraCgPreloadWork<TRequest>>> ownerQueues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> ownerRotation = new();
    private readonly Dictionary<string, AuraCgPreloadWork<TRequest>> claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> activeKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> ownerPending = new(StringComparer.OrdinalIgnoreCase);
    private int queuedCount;

    public AuraCgPreloadScheduler(int maximumPending, int maximumPendingPerOwner, int maximumConcurrent)
    {
        this.maximumPending = Math.Max(1, maximumPending);
        this.maximumPendingPerOwner = Math.Min(this.maximumPending, Math.Max(1, maximumPendingPerOwner));
        this.maximumConcurrent = Math.Max(1, maximumConcurrent);
    }

    public int PendingCount => claims.Count;

    public int QueuedCount => queuedCount;

    public int ActiveCount => activeKeys.Count;

    public int CapacityRejectedCount { get; private set; }

    public AuraCgPreloadEnqueueResult TryEnqueue(
        string key,
        string ownerId,
        TRequest request,
        bool alreadyCached)
    {
        var normalizedKey = (key ?? "").Trim();
        if (normalizedKey.Length == 0 || request == null)
        {
            return AuraCgPreloadEnqueueResult.Invalid;
        }

        if (alreadyCached)
        {
            return AuraCgPreloadEnqueueResult.AlreadyCached;
        }

        if (claims.ContainsKey(normalizedKey))
        {
            return AuraCgPreloadEnqueueResult.Duplicate;
        }

        var owner = NormalizeOwner(ownerId);
        var ownerCount = GetOwnerPendingCount(owner);
        if (claims.Count >= maximumPending || ownerCount >= maximumPendingPerOwner)
        {
            CapacityRejectedCount++;
            return AuraCgPreloadEnqueueResult.CapacityExceeded;
        }

        var work = new AuraCgPreloadWork<TRequest>(normalizedKey, owner, request);
        claims[normalizedKey] = work;
        ownerPending[owner] = ownerCount + 1;
        if (!ownerQueues.TryGetValue(owner, out var queue))
        {
            queue = new Queue<AuraCgPreloadWork<TRequest>>();
            ownerQueues[owner] = queue;
            ownerRotation.Enqueue(owner);
        }

        queue.Enqueue(work);
        queuedCount++;
        return AuraCgPreloadEnqueueResult.Accepted;
    }

    public IReadOnlyList<AuraCgPreloadWork<TRequest>> TakeReady(int startBudget)
    {
        var available = Math.Min(Math.Max(0, startBudget), maximumConcurrent - activeKeys.Count);
        if (available <= 0 || queuedCount == 0)
        {
            return Array.Empty<AuraCgPreloadWork<TRequest>>();
        }

        var ready = new List<AuraCgPreloadWork<TRequest>>(available);
        while (ready.Count < available && ownerRotation.Count > 0)
        {
            var owner = ownerRotation.Dequeue();
            if (!ownerQueues.TryGetValue(owner, out var queue) || queue.Count == 0)
            {
                ownerQueues.Remove(owner);
                continue;
            }

            var work = queue.Dequeue();
            queuedCount--;
            activeKeys.Add(work.Key);
            ready.Add(work);

            if (queue.Count > 0)
            {
                ownerRotation.Enqueue(owner);
            }
            else
            {
                ownerQueues.Remove(owner);
            }
        }

        return ready;
    }

    public bool Complete(string key)
    {
        var normalized = (key ?? "").Trim();
        if (!activeKeys.Remove(normalized) || !claims.TryGetValue(normalized, out var work))
        {
            return false;
        }

        claims.Remove(normalized);
        var next = GetOwnerPendingCount(work.OwnerId) - 1;
        if (next > 0)
        {
            ownerPending[work.OwnerId] = next;
        }
        else
        {
            ownerPending.Remove(work.OwnerId);
        }

        return true;
    }

    public int GetOwnerPendingCount(string ownerId)
    {
        var owner = NormalizeOwner(ownerId);
        return ownerPending.TryGetValue(owner, out var count) ? count : 0;
    }

    private static string NormalizeOwner(string ownerId)
    {
        var owner = (ownerId ?? "").Trim();
        return owner.Length == 0 ? "AuraCgShared" : owner;
    }
}
