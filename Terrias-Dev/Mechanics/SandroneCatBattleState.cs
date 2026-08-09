using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public sealed class SandroneCatBattleState
{
    private readonly object syncRoot = new();
    private readonly HashSet<string> startedOwners = new(StringComparer.Ordinal);
    private readonly HashSet<string> endedOwners = new(StringComparer.Ordinal);
    private long activeSessionId;

    public bool TryMarkStarted(long sessionId, string ownerId)
    {
        if (sessionId <= 0 || string.IsNullOrWhiteSpace(ownerId))
        {
            return false;
        }

        lock (syncRoot)
        {
            MoveToSession(sessionId);
            return startedOwners.Add(ownerId.Trim());
        }
    }

    public bool TryMarkEnded(long sessionId, string ownerId)
    {
        if (sessionId <= 0 || string.IsNullOrWhiteSpace(ownerId))
        {
            return false;
        }

        lock (syncRoot)
        {
            MoveToSession(sessionId);
            var owner = ownerId.Trim();
            return startedOwners.Contains(owner) && endedOwners.Add(owner);
        }
    }

    public void ReleaseStart(long sessionId, string ownerId)
    {
        if (sessionId <= 0 || string.IsNullOrWhiteSpace(ownerId))
        {
            return;
        }

        lock (syncRoot)
        {
            if (activeSessionId != sessionId)
            {
                return;
            }

            var owner = ownerId.Trim();
            startedOwners.Remove(owner);
            endedOwners.Remove(owner);
        }
    }

    public void ReleaseEnd(long sessionId, string ownerId)
    {
        if (sessionId <= 0 || string.IsNullOrWhiteSpace(ownerId))
        {
            return;
        }

        lock (syncRoot)
        {
            if (activeSessionId == sessionId)
            {
                endedOwners.Remove(ownerId.Trim());
            }
        }
    }

    private void MoveToSession(long sessionId)
    {
        if (activeSessionId == sessionId)
        {
            return;
        }

        activeSessionId = sessionId;
        startedOwners.Clear();
        endedOwners.Clear();
    }
}

public static class SandroneCatMaxHpFormula
{
    public static int CalculateEndGain(int maxHp)
    {
        return SaturatingAdd(1, PercentageCeiling(maxHp, 4));
    }

    public static int MaxHpAfterEnd(int maxHp)
    {
        var current = Math.Max(1, maxHp);
        return (int)Math.Min(int.MaxValue, (long)current + CalculateEndGain(current));
    }

    private static int PercentageCeiling(int value, int percent)
    {
        var current = Math.Max(1, value);
        var scaled = (long)current * Math.Max(0, percent);
        return (int)Math.Min(int.MaxValue, (scaled + 99L) / 100L);
    }

    private static int SaturatingAdd(int left, int right)
    {
        return (int)Math.Min(int.MaxValue, (long)Math.Max(0, left) + Math.Max(0, right));
    }
}
