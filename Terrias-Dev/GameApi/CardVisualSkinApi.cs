using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.GameApi;

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

    public static void RegisterTerriasDefaults()
    {
        CardVisualSkinRegistry.ClearOwner(TerriasIds.ModId);
        RegisterTheme(
            TerriasIds.ModId,
            TerriasIds.SunCardVisualSkinId,
            TerriasIds.SunCardFramePath,
            TerriasIds.SunCardBackgroundPath,
            "Sun",
            100,
            TerriasIds.SunThemeExplicitCardIds,
            TerriasIds.SunThemeCardPackIds,
            TerriasIds.SunThemeCardIconPathPrefixes);
        RegisterTheme(
            TerriasIds.ModId,
            TerriasIds.MorningStarCardVisualSkinId,
            TerriasIds.MorningStarCardFramePath,
            "",
            "Morning Star",
            120,
            TerriasIds.StellarOvertureCardEffectIds,
            TerriasIds.MorningStarThemeCardPackIds,
            new[] { TerriasIds.StellarOvertureCardIconPathPrefix });
        TerriasLog.Info("Card visual skin registry initialized: rules=" + CardVisualSkinRegistry.RuleCount);
    }
}
