using Terrias.Dll.GameApi;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

/// <summary>Close queued native draws at settlement; FightUI owns view destruction.</summary>
internal static class TerriasCombatCardProductionRuntime
{
    private static bool initialized;

    internal static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        TerriasBattleLifecycleRouter.Register("CombatCardProduction", new TerriasBattleLifecycleSubscription
        {
            OutcomeEntering = _ => Close("OutcomeEntering"),
            BattleSettling = _ => Close("BattleSettling"),
            BattleRestarting = _ => Close("BattleRestarting"),
            BattleEnded = _ => Close("BattleEnded")
        });
    }

    private static void Close(string source) =>
        FightUiCardTerminalApi.CloseDrawProduction(UIManager.Instance?.GetUI<FightUI>("FightUI"), source);
}
