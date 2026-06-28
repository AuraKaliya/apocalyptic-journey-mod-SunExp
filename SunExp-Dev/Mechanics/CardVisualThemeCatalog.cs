using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class CardVisualThemeCatalog
{
    private static readonly HashSet<string> SunPackIds = new(SunExpIds.SunThemeCardPackIds, StringComparer.Ordinal);
    private static readonly HashSet<string> StellarOvertureCardIds = new(SunExpIds.StellarOvertureCardIds, StringComparer.Ordinal);
    private static readonly HashSet<string> SunExplicitCardIds = new(SunExpIds.SunThemeExplicitCardIds, StringComparer.Ordinal);

    private static readonly CardVisualSkinSpec SunSkin = new(
        SunExpIds.SunCardVisualSkinId,
        SunExpIds.SunCardFramePath,
        SunExpIds.SunCardBackgroundPath,
        "Sun");

    private static readonly CardVisualSkinSpec MorningStarSkin = new(
        SunExpIds.MorningStarCardVisualSkinId,
        SunExpIds.MorningStarCardFramePath,
        "",
        "Morning Star");

    public static CardVisualSkinSpec? Resolve(IDataConfig? config)
    {
        if (IsStellarOvertureCard(config))
        {
            return MorningStarSkin;
        }

        return IsSunThemeCard(config) ? SunSkin : null;
    }

    public static bool IsStellarOvertureCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var id = CardConfigApi.Id(config);
        if (StellarOvertureCardIds.Contains(id) || StarScoreService.IsStellarOvertureCard(id))
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
        if (SunExplicitCardIds.Contains(id))
        {
            return true;
        }

        var packBelong = DictionaryUtil.Get(config.data, "PackBelong");
        if (!string.IsNullOrWhiteSpace(packBelong) && SunPackIds.Contains(packBelong))
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
