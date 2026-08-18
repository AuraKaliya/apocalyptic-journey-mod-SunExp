using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public enum CardPresentationImpact
{
    None,
    CostOnly,
    DescriptionSubset,
    Full
}

[Flags]
public enum CardPresentationFields
{
    None = 0,
    Cost = 1 << 0,
    Description = 1 << 1,
    Name = 1 << 2,
    Keywords = 1 << 3,
    Usability = 1 << 4,
    VisualStyle = 1 << 5,
    Layout = 1 << 6,
    Structural = 1 << 7,
    Full = Cost | Description | Name | Keywords | Usability | VisualStyle | Layout | Structural
}

public readonly struct CardPresentationInvalidationSnapshot
{
    internal CardPresentationInvalidationSnapshot(FightUI? fightUi, bool wasPending)
    {
        FightUi = fightUi;
        WasPending = wasPending;
    }

    internal FightUI? FightUi { get; }

    internal bool WasPending { get; }
}

public static class CardPresentationInvalidationApi
{
    public static CardPresentationInvalidationSnapshot Capture()
    {
        try
        {
            var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
            return new CardPresentationInvalidationSnapshot(fightUi, fightUi?.NeedUpdateCardMsg == true);
        }
        catch
        {
            return default;
        }
    }

    public static IReadOnlyList<CardItem> CurrentHandCards()
    {
        try
        {
            return FightUI.cardItemList == null
                ? Array.Empty<CardItem>()
                : new List<CardItem>(FightUI.cardItemList);
        }
        catch
        {
            return Array.Empty<CardItem>();
        }
    }

    public static bool SuppressNewFullRefresh(
        CardPresentationInvalidationSnapshot snapshot,
        CardPresentationImpact impact,
        string source)
    {
        if (impact == CardPresentationImpact.Full
            || snapshot.FightUi == null
            || snapshot.WasPending
            || !snapshot.FightUi.NeedUpdateCardMsg)
        {
            return false;
        }

        try
        {
            snapshot.FightUi.NeedUpdateCardMsg = false;
            TerriasPerformanceCounters.Record("CardPresentation.FullRefreshSuppressed." + impact);
            TerriasLog.Debug("[CardPresentation] suppressed newly requested full refresh: impact="
                + impact
                + ", source="
                + (string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim())
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[CardPresentation] failed to suppress full refresh from " + source + ": " + ex.Message);
            return false;
        }
    }
}
