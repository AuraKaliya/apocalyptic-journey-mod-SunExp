using System.Collections.Generic;
using AuraToolsExp.Dll.Config;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.StarterDeck;

public static class AuraToolsStarterDeckRuntime
{
    public const float CardInfoHeaderHeight = 40f;
    public const float CardImageColumnWidth = 44f;
    public const float CardIconSize = 34f;
    public const float CardRarityColumnWidth = 70f;
    public const float CardCostColumnWidth = 56f;
    public const float CardActionColumnWidth = 84f;

    public static void Initialize(ModConfig modConfig)
    {
        StarterDeckCardCatalog.Initialize();
        StarterRelicCatalog.Initialize();
        WorldSimulationRunProvenanceRuntime.Initialize(modConfig);
        StarterDeckHookAdapter.Initialize(modConfig);
    }

    internal static void ApplyModuleActivation(bool enabled)
    {
        if (enabled) return;
        StarterDeckCardCatalog.Invalidate("module-disabled");
        StarterRelicCatalog.Invalidate();
    }

    public static List<StarterDeckCardPackGroup> BuildCandidateCardPackGroups() =>
        StarterDeckCardCatalog.BuildCandidateCardPackGroups();

    internal static IReadOnlyList<StarterRelicPackGroup> BuildRelicPackGroups() => StarterRelicCatalog.BuildGroups();

    internal static CustomStartResolvedLoadout ResolveEffectiveLoadout(string roleId) =>
        StarterDeckProfileResolver.ResolveEffectiveLoadout(roleId);

    internal static bool IsGlobalModeEnabled() => StarterDeckProfileResolver.IsGlobalModeEnabled();

    internal static StarterDeckLocalProfileSettings EnsureRoleSettings(string roleId, string displayName = "") =>
        StarterDeckLocalProfileStore.EnsureRoleSettings(roleId, displayName);

    internal static void DeleteRoleSettings(string roleId) => StarterDeckLocalProfileStore.DeleteRoleSettings(roleId);

    internal static void RestoreRoleCards(string roleId) => StarterDeckLocalProfileStore.RestoreCardsFromGlobal(roleId);

    internal static void RestoreRoleRelics(string roleId) => StarterDeckLocalProfileStore.RestoreRelicsFromGlobal(roleId);

    public static string CardSortKey(string cardId) => StarterDeckCardPresentation.CardSortKey(cardId);

    public static string CardDisplayName(string cardId) => StarterDeckCardPresentation.CardDisplayName(cardId);

    public static string CardDisplayNameWithSpecialMarker(string cardId) =>
        StarterDeckCardPresentation.CardDisplayNameWithSpecialMarker(cardId);

    public static string CardRarity(string cardId) => StarterDeckCardPresentation.CardRarity(cardId);

    public static string CardCost(string cardId) => StarterDeckCardPresentation.CardCost(cardId);

    public static Sprite? TryLoadCardIcon(string cardId) => StarterDeckCardPresentation.TryLoadCardIcon(cardId);

    internal static string RelicSortKey(string relicId) => StarterRelicCatalog.SortKey(relicId);

    internal static string RelicDisplayName(string relicId) => StarterRelicCatalog.DisplayName(relicId);

    internal static string RelicRarity(string relicId) => StarterRelicCatalog.Rarity(relicId);

    internal static Sprite? TryLoadRelicIcon(string relicId) => StarterRelicCatalog.TryLoadIcon(relicId);
}
