using System;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class TerriasCardDescriptionProjector
{
    public static bool TryRefresh(CardItem? card)
    {
        var config = card?.dataConfig;
        if (card == null || config?.scriptExecutor is not ScriptExecutor executor)
        {
            return false;
        }

        if (!StarScoreNoteCodes.TryFromCardId(CardConfigApi.Id(config), out var note)
            || note != StarScoreNote.Close)
        {
            return false;
        }

        executor.Self = FightPlayer.Instance?.Status;
        StarScoreService.RefreshCloseDescription(executor);
        return AuraCardPresentationDelta.TrySetDescription(card.transform, config.Description());
    }
}
