using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.GameApi;

public static class CardVisualSkinApi
{
    public static void RegisterTheme(
        string ownerModId,
        string skinId,
        string framePath,
        string backgroundPath,
        string displayName,
        int priority,
        IEnumerable<string>? cardIds = null,
        IEnumerable<string>? packIds = null,
        IEnumerable<string>? iconPrefixes = null)
    {
        CardVisualSkinRegistry.Register(new CardVisualSkinRule(
            new CardVisualSkinSpec(ownerModId, skinId, framePath, backgroundPath, displayName, priority),
            cardIds,
            packIds,
            iconPrefixes,
            priority));
    }

    public static void RegisterSunExpDefaults()
    {
        CardVisualSkinRegistry.ClearOwner(SunExpIds.ModId);
        RegisterTheme(
            SunExpIds.ModId,
            SunExpIds.SunCardVisualSkinId,
            SunExpIds.SunCardFramePath,
            SunExpIds.SunCardBackgroundPath,
            "Sun",
            100,
            SunExpIds.SunThemeExplicitCardIds,
            SunExpIds.SunThemeCardPackIds,
            SunExpIds.SunThemeCardIconPathPrefixes);
        RegisterTheme(
            SunExpIds.ModId,
            SunExpIds.MorningStarCardVisualSkinId,
            SunExpIds.MorningStarCardFramePath,
            "",
            "Morning Star",
            120,
            SunExpIds.StellarOvertureCardIds,
            SunExpIds.MorningStarThemeCardPackIds,
            new[] { SunExpIds.StellarOvertureCardIconPathPrefix });
        SunExpLog.Info("Card visual skin registry initialized: rules=" + CardVisualSkinRegistry.RuleCount);
    }
}
