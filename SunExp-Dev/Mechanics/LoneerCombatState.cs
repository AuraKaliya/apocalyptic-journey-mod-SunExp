using System;
using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public sealed class LoneerCombatState
{
    public string GuidanceCardId { get; set; } = "";

    public int ClockValue { get; set; }

    public int ClockMax { get; set; }

    public int PrayerCooldown { get; set; }

    public int PrayerUseCount { get; set; }

    public bool ActionResolving { get; set; }

    public bool SelectionPending { get; set; }

    public int SelectionVersion { get; set; }

    public bool Initialized { get; set; }

    public void Reset()
    {
        GuidanceCardId = "";
        ClockValue = 0;
        ClockMax = 0;
        PrayerCooldown = 0;
        PrayerUseCount = 0;
        ActionResolving = false;
        SelectionPending = false;
        SelectionVersion = 0;
        Initialized = false;
    }
}

public static class LoneerCombatStateStore
{
    private static readonly Dictionary<string, LoneerCombatState> States = new(StringComparer.Ordinal);

    public static LoneerCombatState? GetOrCreate(IStatusManager? owner)
    {
        var key = OwnerKey(owner);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!States.TryGetValue(key, out var state))
        {
            state = new LoneerCombatState();
            States[key] = state;
        }

        return state;
    }

    public static LoneerCombatState? Get(IStatusManager? owner)
    {
        var key = OwnerKey(owner);
        return !string.IsNullOrWhiteSpace(key) && States.TryGetValue(key, out var state)
            ? state
            : null;
    }

    public static LoneerCombatState? ResetForFight(IStatusManager? owner)
    {
        var state = GetOrCreate(owner);
        state?.Reset();
        return state;
    }

    public static void Remove(IStatusManager? owner)
    {
        var key = OwnerKey(owner);
        if (!string.IsNullOrWhiteSpace(key))
        {
            States.Remove(key);
        }
    }

    public static void ClearAll()
    {
        States.Clear();
    }

    private static string OwnerKey(IStatusManager? owner)
    {
        return owner?.InstanceId ?? "";
    }
}
