using System;
using System.Collections.Concurrent;
using System.Threading;

namespace AuraShared.Core;

public sealed class AuraSharedResourceLockTable : IDisposable
{
    private readonly ConcurrentDictionary<string, ReaderWriterLockSlim> locks = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public T ExecuteRead<T>(string key, Func<T> action)
    {
        var gate = GetLock(key);
        gate.EnterReadLock();
        try
        {
            return action();
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public T ExecuteWrite<T>(string key, Func<T> action)
    {
        var gate = GetLock(key);
        gate.EnterWriteLock();
        try
        {
            return action();
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var gate in locks.Values)
        {
            gate.Dispose();
        }

        locks.Clear();
    }

    private ReaderWriterLockSlim GetLock(string key)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(AuraSharedResourceLockTable));
        }

        var normalized = string.IsNullOrWhiteSpace(key) ? "Global" : key.Trim();
        return locks.GetOrAdd(normalized, _ => new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion));
    }
}
