using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class CardVisualInterestIndex
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, bool> Cache = new(StringComparer.Ordinal);

    public static bool MayAffect(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var key = CacheKey(config);
        lock (SyncRoot)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var result = IsSpiritCard(config)
            || !IsPolymorphRoleCard(config)
                && CardVisualSkinRegistry.Resolve(config) != null
            || CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, config) != null
            || CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, config) != null;

        lock (SyncRoot)
        {
            Cache[key] = result;
        }

        return result;
    }

    public static void Invalidate()
    {
        lock (SyncRoot)
        {
            Cache.Clear();
        }
    }

    private static string CacheKey(IDataConfig config)
    {
        return CardConfigApi.Id(config)
            + "\u001f"
            + DictionaryUtil.Get(config.data, "PackBelong")
            + "\u001f"
            + DictionaryUtil.Get(config.data, "Icon")
            + "\u001f"
            + DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey);
    }

    private static bool IsPolymorphRoleCard(IDataConfig config)
    {
        return DictionaryUtil.ContainsToken(
            DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey),
            TerriasIds.PolymorphRoleCardMarker);
    }

    private static bool IsSpiritCard(IDataConfig config)
    {
        return DictionaryUtil.ContainsToken(
            DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey),
            TerriasIds.SpiritCardMarker);
    }
}
