using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class CardPresentationImpactSpec
{
    public CardPresentationImpactSpec(CardPresentationImpact impact, params string[] cardIds)
        : this(FieldsFor(impact), impact == CardPresentationImpact.Full, cardIds)
    {
    }

    public CardPresentationImpactSpec(
        CardPresentationFields fields,
        bool requiresFullRefresh,
        params string[] cardIds)
    {
        Fields = fields;
        RequiresFullRefresh = requiresFullRefresh;
        CardIds = cardIds ?? Array.Empty<string>();
    }

    public CardPresentationFields Fields { get; }

    public bool RequiresFullRefresh { get; }

    public CardPresentationImpact Impact => RequiresFullRefresh
        ? CardPresentationImpact.Full
        : Fields == CardPresentationFields.None
            ? CardPresentationImpact.None
            : Fields == CardPresentationFields.Cost
                ? CardPresentationImpact.CostOnly
                : CardPresentationImpact.DescriptionSubset;

    public IReadOnlyList<string> CardIds { get; }

    private static CardPresentationFields FieldsFor(CardPresentationImpact impact)
    {
        return impact switch
        {
            CardPresentationImpact.None => CardPresentationFields.None,
            CardPresentationImpact.CostOnly => CardPresentationFields.Cost,
            CardPresentationImpact.DescriptionSubset => CardPresentationFields.Description,
            _ => CardPresentationFields.Full
        };
    }
}

public static class CardPresentationImpactRegistry
{
    private static readonly HashSet<string> ManagedBuffIds = new(StringComparer.Ordinal)
    {
        TerriasIds.AbyssGazeBuffI,
        TerriasIds.AbyssGazeBuffII,
        TerriasIds.AbyssGazeBuffIII,
        TerriasIds.Starlight,
        TerriasIds.StarStonePouch,
        TerriasIds.MiracleClock,
        TerriasIds.StarBlessing
    };
    private static readonly CardPresentationImpactSpec None = new(CardPresentationImpact.None);
    private static readonly CardPresentationImpactSpec BuffKindChanged = new(
        CardPresentationFields.Description,
        requiresFullRefresh: false,
        TerriasIds.StellarOvertureCloseCardId,
        TerriasIds.StellarOvertureCloseShortCardId,
        "*" + TerriasIds.StellarOvertureCloseShortCardId);

    public static CardPresentationImpactSpec ForBuffMutation(string buffId, bool buffKindChanged)
    {
        if (ManagedBuffIds.Contains(buffId ?? ""))
        {
            return buffKindChanged ? BuffKindChanged : None;
        }

        return new CardPresentationImpactSpec(CardPresentationImpact.Full);
    }

    public static bool TryForBuffMutation(
        string buffId,
        int beforeLevel,
        int afterLevel,
        out CardPresentationImpactSpec spec)
    {
        if (!ManagedBuffIds.Contains(buffId ?? ""))
        {
            spec = new CardPresentationImpactSpec(CardPresentationImpact.Full);
            return false;
        }

        spec = ForBuffMutation(buffId ?? "", (beforeLevel > 0) != (afterLevel > 0));
        return true;
    }
}
