using System;
using System.Collections.Generic;
using System.Reflection;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class WunaRoundRadianceState
{
    private const string LocalRoundKey = "TerriasWunaRadianceLocalRound";
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, string> TriggeredRounds = new(StringComparer.Ordinal);

    public static void ResetFight(IStatusManager? owner)
    {
        lock (SyncRoot)
        {
            TriggeredRounds.Remove(OwnerKey(owner));
        }
    }

    public static void AdvanceLocalRound(IStatusManager? owner)
    {
        if (owner == null)
        {
            return;
        }

        var key = PlayerApi.ScopedGameVarKey(LocalRoundKey, owner);
        var next = DictionaryUtil.ParseInt(PlayerApi.GetGameVar(key), 0) + 1;
        PlayerApi.SetGameVar(key, next.ToString());
    }

    public static bool TryMarkTriggered(IStatusManager? owner, string source)
    {
        var ownerKey = OwnerKey(owner);
        if (ownerKey.Length == 0)
        {
            return false;
        }

        var roundKey = CurrentRoundKey(owner);
        lock (SyncRoot)
        {
            if (TriggeredRounds.TryGetValue(ownerKey, out var triggered)
                && string.Equals(triggered, roundKey, StringComparison.Ordinal))
            {
                return false;
            }

            TriggeredRounds[ownerKey] = roundKey;
        }

        TerriasLog.Debug("[WunaRadiance] marked round trigger from "
            + source
            + ": owner="
            + ownerKey
            + ", round="
            + roundKey
            + ".");
        return true;
    }

    private static string CurrentRoundKey(IStatusManager? owner)
    {
        var reflected = ReadFirstInt(FightManager.Instance, "Round", "round", "RoundIndex", "roundIndex", "Turn", "turn", "TurnIndex", "turnIndex");
        if (reflected > 0)
        {
            return "fight:" + reflected;
        }

        var local = PlayerApi.GetGameVar(PlayerApi.ScopedGameVarKey(LocalRoundKey, owner), "0");
        return "local:" + Math.Max(0, DictionaryUtil.ParseInt(local, 0));
    }

    private static int ReadFirstInt(object? target, params string[] names)
    {
        if (target == null)
        {
            return 0;
        }

        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in names)
        {
            try
            {
                var property = type.GetProperty(name, flags);
                if (property != null)
                {
                    var value = property.GetValue(target);
                    var parsed = ParseInt(value);
                    if (parsed > 0)
                    {
                        return parsed;
                    }
                }

                var field = type.GetField(name, flags);
                if (field != null)
                {
                    var value = field.GetValue(target);
                    var parsed = ParseInt(value);
                    if (parsed > 0)
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // Reflection only improves fidelity; fall back to local round state.
            }
        }

        return 0;
    }

    private static int ParseInt(object? value)
    {
        return value is int intValue ? intValue : DictionaryUtil.ParseInt(Convert.ToString(value));
    }

    private static string OwnerKey(IStatusManager? owner)
    {
        return owner?.InstanceId ?? "";
    }
}
