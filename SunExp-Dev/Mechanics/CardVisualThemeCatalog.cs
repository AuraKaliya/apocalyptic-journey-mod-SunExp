using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

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
        if (SunExpIds.StellarOvertureCardIds.Contains(id) || StarScoreService.IsStellarOvertureCard(id))
        {
            return true;
        }

        var iconPath = DictionaryUtil.Get(config.data, "Icon");
        return iconPath.StartsWith(SunExpIds.StellarOvertureCardIconPathPrefix, StringComparison.Ordinal);
    }

    public static bool IsSunThemeCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var id = CardConfigApi.Id(config);
        if (SunExpIds.SunThemeExplicitCardIds.Contains(id))
        {
            return true;
        }

        var packBelong = DictionaryUtil.Get(config.data, "PackBelong");
        if (!string.IsNullOrWhiteSpace(packBelong) && SunExpIds.SunThemeCardPackIds.Contains(packBelong))
        {
            return true;
        }

        var iconPath = DictionaryUtil.Get(config.data, "Icon");
        return StartsWithAny(iconPath, SunExpIds.SunThemeCardIconPathPrefixes);
    }

    public static bool IsSunExpCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var id = CardConfigApi.Id(config);
        var iconPath = DictionaryUtil.Get(config.data, "Icon");
        return id.StartsWith("SunExp_", StringComparison.Ordinal)
            || iconPath.StartsWith("Mods/SunExp/", StringComparison.Ordinal);
    }

    private static bool IsPolymorphRoleCard(IDataConfig? config)
    {
        return config != null
            && DictionaryUtil.ContainsToken(
                DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey),
                SunExpIds.PolymorphRoleCardMarker);
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
