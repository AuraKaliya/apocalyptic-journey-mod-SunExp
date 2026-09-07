using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class FightUiCardTerminalApi
{
    public static int CloseDrawProduction(FightUI? fightUi, string source)
    {
        if (fightUi == null) return 0;
        var queued = fightUi.createCardQueue?.Count ?? 0;
        fightUi.createCardQueue?.Clear();
        fightUi.NeedUpdateCardMsg = false;
        CardItem.canUse = false;
        if (queued > 0)
        {
            TerriasPerformanceCounters.Record("CombatCardProduction.TerminalQueuedDrawsDiscarded");
        }
        TerriasLog.Debug("[CombatCardProduction] terminal draw production closed: source="
                         + (string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim())
                         + ", discarded="
                         + queued
                         + ".");
        return queued;
    }
}
