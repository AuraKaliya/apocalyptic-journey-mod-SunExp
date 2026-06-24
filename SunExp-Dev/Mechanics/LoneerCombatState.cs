using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

public sealed class LoneerCombatState
{
    private readonly List<string> stones = new();

    public string GuidanceCardId { get; set; } = "";

    public int ClockValue { get; set; }

    public int ClockMax { get; set; }

    public int BlackStoneMax { get; set; }

    public int PrayerCooldown { get; set; }

    public int PrayerUseCount { get; set; }

    public bool ActionResolving { get; set; }

    public bool SelectionPending { get; set; }

    public int SelectionVersion { get; set; }

    public bool Initialized { get; set; }

    public IReadOnlyList<string> Stones => stones;

    public int BlackStoneCount(string blackStone)
    {
        return stones.Count(stone => stone == blackStone);
    }

    public void ReplaceStones(IEnumerable<string> values)
    {
        stones.Clear();
        stones.AddRange(values);
    }

    public string DrawStone()
    {
        if (stones.Count == 0)
        {
            return "";
        }

        var stone = stones[0];
        stones.RemoveAt(0);
        return stone;
    }

    public bool RemoveStoneAt(int index)
    {
        if (index < 0 || index >= stones.Count)
        {
            return false;
        }

        stones.RemoveAt(index);
        return true;
    }

    public void Reset()
    {
        stones.Clear();
        GuidanceCardId = "";
        ClockValue = 0;
        ClockMax = 0;
        BlackStoneMax = 0;
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
