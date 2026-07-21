using System.Collections.Generic;
using AuraToolsExp.Dll.Config;
using StarterDeckArbiter.Shared;
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
        StarterDeckHookAdapter.Initialize(modConfig);
    }

    public static List<string> BuildAllCandidateCardIds() => StarterDeckCardCatalog.BuildAllCandidateCardIds();

    public static List<string> BuildCandidateCardIds(IEnumerable<string> packIds) =>
        StarterDeckCardCatalog.BuildCandidateCardIds(packIds);

    public static List<StarterDeckCardPackGroup> BuildCandidateCardPackGroups() =>
        StarterDeckCardCatalog.BuildCandidateCardPackGroups();

    public static List<string> BuildRegisteredCardIds(bool includeSystemSkillCards = false) =>
        StarterDeckCardCatalog.BuildRegisteredCardIds(includeSystemSkillCards);

    public static List<string> BuildRegisteredExplicitCardIds(bool includeSystemSkillCards = false) =>
        StarterDeckCardCatalog.BuildRegisteredExplicitCardIds(includeSystemSkillCards);

    public static List<string> BuildRegisteredHiddenCardIds(bool includeSystemSkillCards = false) =>
        StarterDeckCardCatalog.BuildRegisteredHiddenCardIds(includeSystemSkillCards);

    public static List<string> BuildRegisteredSkillCardIds() => StarterDeckCardCatalog.BuildRegisteredSkillCardIds();

    public static List<string> BuildRegisteredSystemSkillCardIds() =>
        StarterDeckCardCatalog.BuildRegisteredSystemSkillCardIds();

    internal static void WarmStarterDeckCardCatalog(string source) => StarterDeckCardCatalog.Warm(source);

    internal static void InvalidateStarterDeckCardCatalog(string source) => StarterDeckCardCatalog.Invalidate(source);

    internal static StarterDeckResolvedProfile? ResolveEffectiveProfileForPreview(string roleId) =>
        StarterDeckProfileResolver.ResolveEffectiveProfileForPreview(roleId);

    internal static bool IsGlobalModeEnabled() => StarterDeckProfileResolver.IsGlobalModeEnabled();

    internal static string ConfiguredSelectedProfileIdForRole(string roleId) =>
        StarterDeckLocalProfileStore.ConfiguredSelectedProfileIdForRole(roleId);

    internal static bool ProfileMatchesId(StarterDeckProfile profile, string profileId) =>
        StarterDeckProfileResolver.ProfileMatchesId(profile, profileId);

    internal static IReadOnlyList<StarterDeckProfile> BuildCandidateProfilesForRole(string roleId) =>
        StarterDeckProfileResolver.BuildCandidateProfilesForRole(roleId);

    internal static List<string> BuildDeckFromProfile(StarterDeckProfile profile) =>
        StarterDeckProfileResolver.BuildDeckFromProfile(profile);

    internal static string LocalGlobalProfileId() => StarterDeckLocalProfileStore.LocalGlobalProfileId();

    internal static string LocalRoleProfileId(string roleId) => StarterDeckLocalProfileStore.LocalRoleProfileId(roleId);

    internal static bool IsLocalRoleProfileId(string roleId, string profileId) =>
        StarterDeckLocalProfileStore.IsLocalRoleProfileId(roleId, profileId);

    internal static StarterDeckLocalProfileSettings EnsureRoleProfileSettings(string roleId, string displayName = "") =>
        StarterDeckLocalProfileStore.EnsureRoleProfileSettings(roleId, displayName);

    internal static void DeleteRoleProfileSettings(string roleId) => StarterDeckLocalProfileStore.DeleteRoleProfileSettings(roleId);

    internal static void SelectProfileForRole(string roleId, string profileId) =>
        StarterDeckLocalProfileStore.SelectProfileForRole(roleId, profileId);

    internal static void ClearSelectedProfileForRole(string roleId) =>
        StarterDeckLocalProfileStore.ClearSelectedProfileForRole(roleId);

    public static string CardSortKey(string cardId) => StarterDeckCardPresentation.CardSortKey(cardId);

    public static string CardDisplayName(string cardId) => StarterDeckCardPresentation.CardDisplayName(cardId);

    public static string CardDisplayNameWithSpecialMarker(string cardId) =>
        StarterDeckCardPresentation.CardDisplayNameWithSpecialMarker(cardId);

    public static bool IsSpecialCardId(string cardId) => StarterDeckCardPresentation.IsSpecialCardId(cardId);

    public static string CardShortInfo(string cardId) => StarterDeckCardPresentation.CardShortInfo(cardId);

    public static string CardRarity(string cardId) => StarterDeckCardPresentation.CardRarity(cardId);

    public static string CardCost(string cardId) => StarterDeckCardPresentation.CardCost(cardId);

    public static Sprite? TryLoadCardIcon(string cardId) => StarterDeckCardPresentation.TryLoadCardIcon(cardId);
}
