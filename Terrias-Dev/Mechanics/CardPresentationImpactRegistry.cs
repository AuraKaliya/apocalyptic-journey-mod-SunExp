using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public sealed class CardPresentationImpactSpec
{
    public CardPresentationImpactSpec(CardPresentationImpact impact, params string[] cardIds)
    {
        Impact = impact;
        CardIds = cardIds ?? Array.Empty<string>();
    }

    public CardPresentationImpact Impact { get; }

    public IReadOnlyList<string> CardIds { get; }
}

public static class CardPresentationImpactRegistry
{
    private static readonly HashSet<string> ManagedBuffIds = new(StringComparer.Ordinal)
    {
        SunExpIds.AbyssGazeBuffI,
        SunExpIds.AbyssGazeBuffII,
        SunExpIds.AbyssGazeBuffIII,
        SunExpIds.Starlight,
        SunExpIds.StarStonePouch,
        SunExpIds.MiracleClock,
        SunExpIds.StarBlessing
    };
    private static readonly CardPresentationImpactSpec None = new(CardPresentationImpact.None);
    private static readonly CardPresentationImpactSpec BuffKindChanged = new(
        CardPresentationImpact.DescriptionSubset,
        SunExpIds.StellarOvertureCloseCardId,
        SunExpIds.StellarOvertureCloseShortCardId,
        "*" + SunExpIds.StellarOvertureCloseShortCardId);

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
