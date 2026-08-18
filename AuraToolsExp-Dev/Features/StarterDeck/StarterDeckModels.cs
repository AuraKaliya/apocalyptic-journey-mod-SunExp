using System;
using System.Collections.Generic;
using System.Linq;
using AuraMode.Shared;
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

public sealed class StarterDeckCardPackGroup
{
    public const string OtherGroupId = "__other__";

    public StarterDeckCardPackGroup(string packId, string displayName, List<string> cardIds)
    {
        PackId = packId ?? "";
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? PackId : displayName;
        CardIds = cardIds ?? new List<string>();
    }

    public string PackId { get; }

    public string DisplayName { get; }

    public List<string> CardIds { get; }

    public StarterDeckCardPackGroup WithCards(List<string> cardIds)
    {
        return new StarterDeckCardPackGroup(PackId, DisplayName, cardIds);
    }
}

internal sealed class StarterDeckCardCatalogSnapshot
{
    public static readonly StarterDeckCardCatalogSnapshot Empty = new(
        new List<StarterDeckCardCatalogEntry>(),
        new List<StarterDeckCardPackGroup>(),
        new List<string>(),
        new List<string>(),
        new List<string>(),
        new List<string>(),
        new List<string>());

    private readonly Dictionary<string, StarterDeckCardCatalogEntry> cardsById;
    private readonly HashSet<string> systemSkillCardIds;
    private readonly HashSet<string> starterDeckExcludedCardIds;

    public StarterDeckCardCatalogSnapshot(
        List<StarterDeckCardCatalogEntry> allCards,
        List<StarterDeckCardPackGroup> selectableGroups,
        List<string> selectableCardIds,
        List<string> hiddenCardIds,
        List<string> skillCardIds,
        List<string> systemSkillCardIds,
        List<string> excludedDerivedCardIds)
    {
        AllCards = allCards ?? new List<StarterDeckCardCatalogEntry>();
        SelectableGroups = selectableGroups ?? new List<StarterDeckCardPackGroup>();
        SelectableCardIds = selectableCardIds ?? new List<string>();
        HiddenCardIds = hiddenCardIds ?? new List<string>();
        SkillCardIds = skillCardIds ?? new List<string>();
        SystemSkillCardIds = systemSkillCardIds ?? new List<string>();
        ExcludedDerivedCardIds = excludedDerivedCardIds ?? new List<string>();
        cardsById = AllCards
            .GroupBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        this.systemSkillCardIds = new HashSet<string>(SystemSkillCardIds, StringComparer.OrdinalIgnoreCase);
        starterDeckExcludedCardIds = new HashSet<string>(SystemSkillCardIds, StringComparer.OrdinalIgnoreCase);
        starterDeckExcludedCardIds.UnionWith(ExcludedDerivedCardIds);
    }

    public List<StarterDeckCardCatalogEntry> AllCards { get; }

    public List<StarterDeckCardPackGroup> SelectableGroups { get; }

    public List<string> SelectableCardIds { get; }

    public List<string> HiddenCardIds { get; }

    public List<string> SkillCardIds { get; }

    public List<string> SystemSkillCardIds { get; }

    public List<string> ExcludedDerivedCardIds { get; }

    public bool TryGetCard(string cardId, out StarterDeckCardCatalogEntry? card)
    {
        card = null;
        return !string.IsNullOrWhiteSpace(cardId) && cardsById.TryGetValue(cardId, out card);
    }

    public bool IsSystemSkillCard(string cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId) && systemSkillCardIds.Contains(cardId);
    }

    public bool IsStarterDeckExcluded(string cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId) && starterDeckExcludedCardIds.Contains(cardId);
    }

    public List<StarterDeckCardPackGroup> CloneSelectableGroups()
    {
        return SelectableGroups
            .Select(group => new StarterDeckCardPackGroup(group.PackId, group.DisplayName, group.CardIds.ToList()))
            .ToList();
    }
}

internal sealed class StarterDeckCardCatalogEntry
{
    public StarterDeckCardCatalogEntry(
        string id,
        string packId,
        string displayName,
        string rarity,
        string cost,
        string iconPath,
        string action,
        bool isHidden,
        bool isSkillCard,
        bool isSystemSkillCard,
        bool isExcludedDerivedCard)
    {
        Id = id ?? "";
        PackId = packId ?? "";
        DisplayName = displayName ?? Id;
        Rarity = rarity ?? "";
        Cost = cost ?? "";
        IconPath = iconPath ?? "";
        Action = action ?? "";
        IsHidden = isHidden;
        IsSkillCard = isSkillCard;
        IsSystemSkillCard = isSystemSkillCard;
        IsExcludedDerivedCard = isExcludedDerivedCard;
    }

    public string Id { get; }

    public string PackId { get; }

    public string DisplayName { get; }

    public string Rarity { get; }

    public string Cost { get; }

    public string IconPath { get; }

    public string Action { get; }

    public bool IsHidden { get; }

    public bool IsSkillCard { get; }

    public bool IsSystemSkillCard { get; }

    public bool IsExcludedDerivedCard { get; }

}
