using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared;
using AuraMode.Shared;
using AuraGameData.Shared.GameApi;
using AuraRole.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckCardCatalog
{
    private static readonly object cardCatalogSync = new();
    private static StarterDeckCardCatalogSnapshot? cardCatalogSnapshot;
    private static long cardCatalogEpoch = -1;
    private static bool catalogListenerRegistered;

    internal static void Initialize()
    {
        lock (cardCatalogSync)
        {
            if (catalogListenerRegistered)
            {
                return;
            }

            AuraGameDataCatalogRuntime.SnapshotChanged += OnCatalogSnapshotChanged;
            catalogListenerRegistered = true;
        }
    }

    public static List<StarterDeckCardPackGroup> BuildCandidateCardPackGroups()
    {
        return GetCardCatalogSnapshot("pack-groups").CloneSelectableGroups();
    }

    public static string ResolveCardId(string cardId, string ownerModId = "")
    {
        var declared = (cardId ?? "").Trim();
        var resolution = AuraSharedContentId.Resolve(
            declared,
            GetCardCatalogSnapshot("resolve-card-id").AllCards.Select(card => card.Id),
            ownerModId,
            "careercard_");
        if (resolution.Success)
        {
            return resolution.ResolvedId;
        }

        if (resolution.Kind == AuraSharedContentIdResolutionKind.Ambiguous)
        {
            AuraToolsLog.Warn("[CustomStart] card id is ambiguous: declared="
                              + declared + ", matches=" + string.Join("|", resolution.Matches));
        }

        return declared;
    }

    internal static void Warm(string source)
    {
        _ = GetCardCatalogSnapshot(source);
    }

    internal static void Invalidate(string source)
    {
        lock (cardCatalogSync)
        {
            cardCatalogSnapshot = null;
            cardCatalogEpoch = -1;
            StarterDeckCardPresentation.ClearCache();
        }

        AuraToolsLog.Info("[CustomStart] invalidated card catalog from " + source);
    }
    private static StarterDeckCardCatalogSnapshot GetCardCatalogSnapshot(string source)
    {
        var gameDataSnapshot = AuraGameDataHostApi.AcquireSnapshot();
        lock (cardCatalogSync)
        {
            if (cardCatalogSnapshot != null && cardCatalogEpoch == gameDataSnapshot.Version.Epoch)
            {
                return cardCatalogSnapshot;
            }

            if (!gameDataSnapshot.Version.NativeReady)
            {
                return cardCatalogSnapshot ?? StarterDeckCardCatalogSnapshot.Empty;
            }

            cardCatalogSnapshot = BuildCardCatalogSnapshot(source);
            cardCatalogEpoch = gameDataSnapshot.Version.Epoch;
            return cardCatalogSnapshot;
        }
    }

    private static void OnCatalogSnapshotChanged(AuraGameDataCatalogVersion version)
    {
        lock (cardCatalogSync)
        {
            if (cardCatalogEpoch == version.Epoch)
            {
                return;
            }

            cardCatalogSnapshot = null;
            cardCatalogEpoch = -1;
            StarterDeckCardPresentation.ClearCache();
        }

        AuraToolsLog.Debug("[CustomStart] invalidated card catalog for game-data epoch " + version.Epoch + ".");
    }

    private static StarterDeckCardCatalogSnapshot BuildCardCatalogSnapshot(string source)
    {
        try
        {
            var gameConfig = Singleton<GameConfigManager>.Instance;
            var packDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var existingPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectablePacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in AuraGameDataHostApi.CopyTableForHostInterop(DataType.CardPack))
            {
                if (!row.TryGetValue("Id", out var packId) || string.IsNullOrWhiteSpace(packId))
                {
                    continue;
                }

                packId = packId.Trim();
                existingPacks.Add(packId);
                packDisplayNames[packId] = RowDisplayName(row, packId);
                selectablePacks.Add(packId);
            }

            var groupCards = selectablePacks
                .ToDictionary(packId => packId, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            var allCards = new List<StarterDeckCardCatalogEntry>();
            var hiddenCards = new List<string>();
            var skillCards = new List<string>();
            var systemSkillCards = new List<string>();
            var excludedDerivedCards = new List<string>();
            var otherCards = new List<string>();
            var effectiveRoles = AuraRoleRegistryRuntime.GetEffectiveSnapshot();
            var effectiveCareerRows = effectiveRoles.NativeReady
                ? effectiveRoles.Entries
                    .Select(role => AuraGameDataHostApi.CopyRow(DataType.Career, role.RoleId))
                    .Where(row => row != null)
                    .Select(row => row!)
                    .ToList()
                : new List<Dictionary<string, string>>();
            var careerSkillCardIds = StarterDeckCardClassification.BuildCareerSkillCardIds(effectiveCareerRows);

            foreach (var row in AuraGameDataHostApi.CopyTableForHostInterop(DataType.Card))
            {
                if (!row.TryGetValue("Id", out var id) || string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                id = id.Trim();
                row.TryGetValue("Action", out var action);
                row.TryGetValue("Icon", out var iconPath);
                row.TryGetValue("Rarity", out var rarity);
                row.TryGetValue("Expend", out var cost);

                var packId = StarterDeckCardClassification.ResolveEffectivePackId(row, gameConfig.GetPackBelong);
                var isHidden = StarterDeckCardPresentation.IsSpecialCardId(id);
                var isSkillCard = StarterDeckCardClassification.IsCareerSkillCard(id, careerSkillCardIds);
                var isSystemSkillCard = isSkillCard;
                var isExcludedDerivedCard = StarterDeckCardClassification.IsExcludedDerivedCard(row);
                var displayName = RowDisplayName(row, id);

                var entry = new StarterDeckCardCatalogEntry(
                    id,
                    packId,
                    displayName,
                    rarity ?? "",
                    cost ?? "",
                    iconPath ?? "",
                    action ?? "",
                    isHidden,
                    isSkillCard,
                    isSystemSkillCard,
                    isExcludedDerivedCard);
                allCards.Add(entry);

                if (isHidden)
                {
                    hiddenCards.Add(id);
                }

                if (isSkillCard)
                {
                    skillCards.Add(id);
                }

                if (isSystemSkillCard)
                {
                    systemSkillCards.Add(id);
                    continue;
                }

                if (isExcludedDerivedCard)
                {
                    excludedDerivedCards.Add(id);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(packId) && groupCards.TryGetValue(packId, out var group))
                {
                    group.Add(id);
                }
                else if (string.IsNullOrWhiteSpace(packId) || !existingPacks.Contains(packId))
                {
                    otherCards.Add(id);
                }
            }

            var groups = groupCards
                .Select(pair => new StarterDeckCardPackGroup(pair.Key, PackDisplayName(packDisplayNames, pair.Key), SortedDistinctCards(pair.Value)))
                .Where(group => group.CardIds.Count > 0)
                .OrderBy(group => group.DisplayName)
                .ThenBy(group => group.PackId)
                .ToList();
            var sortedOtherCards = SortedDistinctCards(otherCards);
            if (sortedOtherCards.Count > 0)
            {
                groups.Add(new StarterDeckCardPackGroup(StarterDeckCardPackGroup.OtherGroupId, "\u5176\u5b83", sortedOtherCards));
            }

            var selectableCards = groups
                .SelectMany(group => group.CardIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(StarterDeckCardPresentation.CardSortKey)
                .ToList();
            var snapshot = new StarterDeckCardCatalogSnapshot(
                allCards
                    .GroupBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(card => StarterDeckCardPresentation.CardSortKey(card.Id))
                    .ToList(),
                groups,
                selectableCards,
                SortedDistinctCards(hiddenCards),
                SortedDistinctCards(skillCards),
                SortedDistinctCards(systemSkillCards),
                SortedDistinctCards(excludedDerivedCards));
            AuraToolsLog.Info(
                "[CustomStart] built card catalog from " + source
                + ": cards=" + snapshot.AllCards.Count
                + ", selectable=" + snapshot.SelectableCardIds.Count
                + ", groups=" + snapshot.SelectableGroups.Count
                + ", skills=" + snapshot.SkillCardIds.Count
                + ", systemSkills=" + snapshot.SystemSkillCardIds.Count
                + ", excludedDerived=" + snapshot.ExcludedDerivedCardIds.Count);
            return snapshot;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[CustomStart] failed to build card catalog from " + source + ": " + ex.Message);
            return StarterDeckCardCatalogSnapshot.Empty;
        }
    }

    private static string PackDisplayName(Dictionary<string, string> packDisplayNames, string packId)
    {
        return packDisplayNames.TryGetValue(packId, out var displayName) && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : CardPackDisplayName(packId);
    }

    private static string RowDisplayName(Dictionary<string, string> row, string fallback)
    {
        try
        {
            var localized = row.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }
        }
        catch
        {
            // Fall back to the raw row value below.
        }

        return row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : fallback;
    }

    internal static bool IsStarterDeckExcludedCard(string cardId)
    {
        return GetCardCatalogSnapshot("starter-deck-exclusion-check").IsStarterDeckExcluded(cardId);
    }

    private static string CardPackDisplayName(string packId)
    {
        try
        {
            var data = AuraGameDataHostApi.CopyRow(DataType.CardPack, packId);
            if (data == null)
            {
                return packId;
            }
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }

            return data.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : packId;
        }
        catch
        {
            return packId;
        }
    }

    private static List<string> SortedDistinctCards(IEnumerable<string> cardIds)
    {
        return (cardIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(StarterDeckCardPresentation.CardSortKey)
            .ToList();
    }

    internal static bool IsValidCard(string cardId)
    {
        try
        {
            return AuraGameDataHostApi.Resolve(DataType.Card, cardId) != null;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetCatalogCard(string cardId, out StarterDeckCardCatalogEntry? card)
    {
        card = null;
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        return GetCardCatalogSnapshot("card-lookup").TryGetCard(cardId, out card);
    }

}
