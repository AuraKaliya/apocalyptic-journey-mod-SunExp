using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class CardVisualThemeCatalog
{
    private static readonly HashSet<string> SunPackIds = new(SunExpIds.SunThemeCardPackIds, StringComparer.Ordinal);

    private static readonly CardVisualSkinSpec SunSkin = new(
        SunExpIds.SunCardVisualSkinId,
        SunExpIds.SunCardFramePath,
        SunExpIds.SunCardBackgroundPath,
        "Sun");

    public static CardVisualSkinSpec? Resolve(IDataConfig? config)
    {
        return IsSunThemeCard(config) ? SunSkin : null;
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
