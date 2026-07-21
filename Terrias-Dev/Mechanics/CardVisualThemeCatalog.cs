using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class CardVisualThemeCatalog
{
    public static CardVisualSkinSpec? Resolve(IDataConfig? config)
    {
        if (IsPolymorphRoleCard(config))
        {
            return null;
        }

        return CardVisualSkinRegistry.Resolve(config);
    }

    public static bool IsStellarOvertureCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var id = CardConfigApi.Id(config);
        if (TerriasIds.StellarOvertureCardIds.Contains(id) || StarScoreService.IsStellarOvertureCard(id))
        {
            return true;
        }

        var iconPath = DictionaryUtil.Get(config.data, "Icon");
        return iconPath.StartsWith(TerriasIds.StellarOvertureCardIconPathPrefix, StringComparison.Ordinal);
    }

    public static bool IsSunThemeCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var id = CardConfigApi.Id(config);
        if (TerriasIds.SunThemeExplicitCardIds.Contains(id))
        {
            return true;
        }

        var packBelong = DictionaryUtil.Get(config.data, "PackBelong");
        if (!string.IsNullOrWhiteSpace(packBelong) && TerriasIds.SunThemeCardPackIds.Contains(packBelong))
        {
            return true;
        }

        var iconPath = DictionaryUtil.Get(config.data, "Icon");
        return StartsWithAny(iconPath, TerriasIds.SunThemeCardIconPathPrefixes);
    }

    public static bool IsTerriasCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var id = CardConfigApi.Id(config);
        var iconPath = DictionaryUtil.Get(config.data, "Icon");
        return id.StartsWith("Terrias_", StringComparison.Ordinal)
            || iconPath.StartsWith("Mods/Terrias/", StringComparison.Ordinal);
    }

    private static bool IsPolymorphRoleCard(IDataConfig? config)
    {
        return config != null
            && DictionaryUtil.ContainsToken(
                DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey),
                TerriasIds.PolymorphRoleCardMarker);
    }

    private static bool StartsWithAny(string value, IEnumerable<string> prefixes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix) && value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
