using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AuraShared.Core;

public sealed class AuraSharedObjectPool<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly object syncRoot = new();
    private readonly Dictionary<TKey, Stack<TValue>> buckets = new();
    private readonly HashSet<TValue> idleValues = new(ReferenceComparer.Instance);
    private readonly Func<TValue, bool> isValid;
    private readonly int capacityPerKey;

    public AuraSharedObjectPool(int capacityPerKey, Func<TValue, bool>? isValid = null)
    {
        this.capacityPerKey = Math.Max(1, capacityPerKey);
        this.isValid = isValid ?? (value => value != null);
    }

    public bool TryAcquire(TKey key, out TValue? value)
    {
        lock (syncRoot)
        {
            if (!buckets.TryGetValue(key, out var bucket))
            {
                value = null;
                return false;
            }

            while (bucket.Count > 0)
            {
                var candidate = bucket.Pop();
                idleValues.Remove(candidate);
                if (isValid(candidate))
                {
                    value = candidate;
                    return true;
                }
            }

            buckets.Remove(key);
            value = null;
            return false;
        }
    }

    public bool Release(TKey key, TValue value)
    {
        if (value == null)
        {
            return false;
        }

        lock (syncRoot)
        {
            if (!isValid(value) || idleValues.Contains(value))
            {
                return false;
            }

            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new Stack<TValue>();
                buckets[key] = bucket;
            }

            if (bucket.Count >= capacityPerKey)
            {
                return false;
            }

            bucket.Push(value);
            idleValues.Add(value);
            return true;
        }
    }

    public int Count(TKey key)
    {
        lock (syncRoot)
        {
            return buckets.TryGetValue(key, out var bucket) ? bucket.Count : 0;
        }
    }

    public void Clear(Action<TValue>? dispose = null)
    {
        List<TValue> values = new();
        lock (syncRoot)
        {
            foreach (var bucket in buckets.Values)
            {
                values.AddRange(bucket);
            }

            buckets.Clear();
            idleValues.Clear();
        }

        if (dispose == null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (isValid(value))
            {
                dispose(value);
            }
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<TValue>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(TValue? left, TValue? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(TValue value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
