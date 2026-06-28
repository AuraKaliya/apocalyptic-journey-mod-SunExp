using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class CardVisualThemeCatalog
{
    private static readonly HashSet<string> SunPackIds = new(SunExpIds.SunThemeCardPackIds, StringComparer.Ordinal);
    private static readonly HashSet<string> StellarOvertureCardIds = new(SunExpIds.StellarOvertureCardIds, StringComparer.Ordinal);

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

        var id = DictionaryUtil.Get(config.data, "Id");
        if (StellarOvertureCardIds.Contains(id))
        {
            return true;
        }

        return id.Equals("*stellar_overture_start", StringComparison.Ordinal)
            || id.Equals("*stellar_overture_sustain", StringComparison.Ordinal)
            || id.Equals("*stellar_overture_turn", StringComparison.Ordinal)
            || id.Equals("*stellar_overture_close", StringComparison.Ordinal)
            || id.EndsWith("_stellar_overture_start", StringComparison.Ordinal)
            || id.EndsWith("_stellar_overture_sustain", StringComparison.Ordinal)
            || id.EndsWith("_stellar_overture_turn", StringComparison.Ordinal)
            || id.EndsWith("_stellar_overture_close", StringComparison.Ordinal);
    }

    public static bool IsSunThemeCard(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var packBelong = DictionaryUtil.Get(config.data, "PackBelong");
        if (!string.IsNullOrWhiteSpace(packBelong) && SunPackIds.Contains(packBelong))
        {
            return true;
        }

        var iconPath = DictionaryUtil.Get(config.data, "Icon");
        return iconPath.StartsWith(SunExpIds.SunCardIconPathPrefix, StringComparison.Ordinal);
    }
}
