using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaBurnoutPolicy
{
    public const string BurnoutTag = "Burnout";

    public static bool HasBurnout(IDataConfig? card) =>
        DictionaryUtil.ContainsToken(CardMutationService.CurrentNativeTagText(card), BurnoutTag);

    public static bool IsPurified(IDataConfig? card) => MatchesInstance(card, TerriasIds.EndlessSeaPurifiedCardInstanceKey);

    public static bool IsStarter(IDataConfig? card)
    {
        if (card == null) return false;
        if (card.Vars.ContainsKey(TerriasIds.EndlessSeaStarterCardInstanceKey))
            return MatchesInstance(card, TerriasIds.EndlessSeaStarterCardInstanceKey);
        return CardMutationService.HasRuntimeMarker(card, TerriasIds.EndlessSeaStarterDeckBaselineMarker);
    }

    public static bool MarkStarter(IDataConfig card)
    {
        var changed = CardMutationService.SetRuntimeMarkers(card, TerriasIds.EndlessSeaStarterDeckBaselineMarker);
        return SetInstance(card, TerriasIds.EndlessSeaStarterCardInstanceKey) || changed;
    }

    public static bool AttachReward(IDataConfig? card)
    {
        if (card == null || IsStarter(card) || IsPurified(card)) return false;
        var changed = CardMutationService.AddNativeTags(card, BurnoutTag);
        return CardMutationService.SetRuntimeMarkers(card, TerriasIds.EndlessSeaAutoBurnoutMarker) || changed;
    }

    public static bool Purify(IDataConfig? card)
    {
        if (card == null || !HasBurnout(card) || string.IsNullOrWhiteSpace(card.InstanceID)) return false;
        CardMutationService.RemoveNativeTags(card, BurnoutTag);
        SetInstance(card, TerriasIds.EndlessSeaPurifiedCardInstanceKey);
        return true;
    }

    public static bool RestorePurification(IDataConfig card)
    {
        var changed = CardMutationService.RemoveNativeTags(card, BurnoutTag);
        return SetInstance(card, TerriasIds.EndlessSeaPurifiedCardInstanceKey) || changed;
    }

    public static IReadOnlyList<IDataConfig> DistinctInstances(IEnumerable<IDataConfig> cards)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var references = new HashSet<IDataConfig>();
        return cards.Where(card => card != null && (string.IsNullOrWhiteSpace(card.InstanceID)
            ? references.Add(card) : ids.Add(card.InstanceID))).ToList();
    }

    private static bool MatchesInstance(IDataConfig? card, string key) =>
        card != null && !string.IsNullOrWhiteSpace(card.InstanceID)
        && string.Equals(DictionaryUtil.Get(card.Vars, key), card.InstanceID, StringComparison.Ordinal);

    private static bool SetInstance(IDataConfig card, string key)
    {
        if (MatchesInstance(card, key)) return false;
        if (string.IsNullOrWhiteSpace(card.InstanceID)) throw new InvalidOperationException("Owned card instance identity is missing.");
        card.Vars[key] = card.InstanceID;
        return true;
    }
}
