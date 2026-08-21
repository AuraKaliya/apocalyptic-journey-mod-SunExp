using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class TerriasCardDescriptionProjector
{
    public static bool TryRecompute(CardItem? card)
    {
        var config = card?.dataConfig;
        if (card == null || config?.scriptExecutor == null)
        {
            return false;
        }

        try
        {
            config.scriptExecutor.Self = FightPlayer.Instance?.Status;
            config.scriptExecutor.RunScript("InitScript");
            return true;
        }
        catch (System.Exception ex)
        {
            TerriasLog.Debug("Card derived-state recompute failed: " + ex.Message);
            return false;
        }
    }

    public static bool TryApplyDescription(CardItem? card)
    {
        var config = card?.dataConfig;
        if (card == null || config == null)
        {
            return false;
        }

        return AuraCardPresentationDelta.TrySetDescription(card.transform, config.Description());
    }
}
