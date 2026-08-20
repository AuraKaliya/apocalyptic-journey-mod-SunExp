using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AuraShared.Core;

public readonly struct AuraBattleLeaseToken
{
    internal AuraBattleLeaseToken(object owner, string key, long sessionId, long generation)
    {
        Owner = owner;
        Key = key;
        SessionId = sessionId;
        Generation = generation;
    }

    internal object? Owner { get; }
    internal string Key { get; }

    public long SessionId { get; }
    public long Generation { get; }
    public bool IsValid => Owner != null && Key.Length > 0 && SessionId > 0 && Generation > 0;
}

public static class AuraBattleLeaseLedger
{
    private static readonly ConditionalWeakTable<object, OwnerState> Owners = new();

    public static bool TryAcquire(
        object owner,
        string ownerModId,
        string registrationId,
        out AuraBattleLeaseToken token)
    {
        token = default;
        if (owner == null)
        {
            return false;
        }

        var key = LeaseKey(ownerModId, registrationId);
        if (key.Length == 0)
        {
            return false;
        }

        var sessionId = AuraLifecycleSessionRuntime.EnsureBattleSession();
        var state = Owners.GetValue(owner, _ => new OwnerState());
        lock (state.Gate)
        {
            if (!state.Leases.TryGetValue(key, out var lease))
            {
                lease = new LeaseState();
                state.Leases[key] = lease;
            }

            if (lease.Active && lease.SessionId == sessionId)
            {
                return false;
            }

            lease.SessionId = sessionId;
            lease.Generation = NextGeneration(lease.Generation);
            lease.Active = true;
            token = new AuraBattleLeaseToken(owner, key, sessionId, lease.Generation);
            return true;
        }
    }

    public static bool IsCurrent(AuraBattleLeaseToken token)
    {
        if (!token.IsValid || token.Owner == null || !Owners.TryGetValue(token.Owner, out var state))
        {
            return false;
        }

        lock (state.Gate)
        {
            return state.Leases.TryGetValue(token.Key, out var lease)
                   && lease.Active
                   && lease.SessionId == token.SessionId
                   && lease.Generation == token.Generation;
        }
    }

    public static void Invalidate(AuraBattleLeaseToken token)
    {
        if (!token.IsValid || token.Owner == null || !Owners.TryGetValue(token.Owner, out var state))
        {
            return;
        }

        lock (state.Gate)
        {
            if (!state.Leases.TryGetValue(token.Key, out var lease)
                || lease.SessionId != token.SessionId
                || lease.Generation != token.Generation)
            {
                return;
            }

            lease.Active = false;
            lease.Generation = NextGeneration(lease.Generation);
        }
    }

    public static void Invalidate(object owner, string ownerModId, string registrationId)
    {
        if (owner == null || !Owners.TryGetValue(owner, out var state))
        {
            return;
        }

        var key = LeaseKey(ownerModId, registrationId);
        if (key.Length == 0)
        {
            return;
        }

        lock (state.Gate)
        {
            if (!state.Leases.TryGetValue(key, out var lease))
            {
                return;
            }

            lease.Active = false;
            lease.Generation = NextGeneration(lease.Generation);
        }
    }

    private static string LeaseKey(string ownerModId, string registrationId)
    {
        var owner = Normalize(ownerModId);
        var id = Normalize(registrationId);
        return owner.Length == 0 || id.Length == 0 ? "" : owner + ":" + id;
    }

    private static long NextGeneration(long value)
    {
        return value == long.MaxValue ? 1 : Math.Max(1, value + 1);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private sealed class OwnerState
    {
        public object Gate { get; } = new();
        public Dictionary<string, LeaseState> Leases { get; } = new(StringComparer.Ordinal);
    }

    private sealed class LeaseState
    {
        public long SessionId;
        public long Generation;
        public bool Active;
    }
}
