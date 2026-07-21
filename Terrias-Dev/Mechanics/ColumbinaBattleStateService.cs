using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public static class ColumbinaBattleStateService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, int> StartingMaxHp = new(StringComparer.Ordinal);

    public static void BeginBattle()
    {
        lock (SyncRoot)
        {
            StartingMaxHp.Clear();
            foreach (var status in PlayerPartyApi.Snapshot(aliveOnly: false))
            {
                StartingMaxHp[Key(status)] = StatusApi.MaxHp(status);
            }
        }
    }

    public static void EndBattle()
    {
        lock (SyncRoot)
        {
            StartingMaxHp.Clear();
        }
    }

    public static int StartingMaxHpFor(IStatusManager? status)
    {
        if (status == null)
        {
            return 0;
        }

        lock (SyncRoot)
        {
            var key = Key(status);
            if (!StartingMaxHp.TryGetValue(key, out var value))
            {
                value = StatusApi.MaxHp(status);
                StartingMaxHp[key] = value;
            }

            return value;
        }
    }

    private static string Key(IStatusManager status)
    {
        return string.IsNullOrWhiteSpace(status.InstanceId) ? status.GetHashCode().ToString() : status.InstanceId;
    }
}
