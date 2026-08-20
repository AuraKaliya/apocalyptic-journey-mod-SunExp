using System;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Scripting;
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

        executor.Self = FightPlayer.Instance?.Status;
        var id = TerriasContentIdCompatibility.LocalId(CardConfigApi.Id(config)).TrimStart('*');
        if (StarScoreNoteCodes.TryFromCardId(id, out var note) && note == StarScoreNote.Close)
        {
            StarScoreService.RefreshCloseDescription(executor);
        }
        else
        {
            CardScripts.Init(executor, id);
        }

        return AuraCardPresentationDelta.TrySetDescription(card.transform, config.Description());
    }
}
