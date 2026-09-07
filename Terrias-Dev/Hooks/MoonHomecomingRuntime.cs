using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class MoonHomecomingRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraCardActionTransactionRouter.Register(modConfig, TerriasIds.ModId, "MoonHomecoming",
            new AuraCardActionSubscription
            {
                Phases = AuraCardActionPhase.NativeStarted | AuraCardActionPhase.Committed | AuraCardActionPhase.Aborted,
                Handler = OnCardAction
            }, TerriasLog.Debug, TerriasLog.Warn);
        TerriasBattleLifecycleRouter.Register("MoonHomecoming", new TerriasBattleLifecycleSubscription
        {
            BattleInitializing = _ => MoonHomecomingMechanics.EndBattle(),
            BattleRestarting = _ => MoonHomecomingMechanics.EndBattle(),
            BattleSettling = _ => MoonHomecomingMechanics.EndBattle(),
            BattleEnded = _ => MoonHomecomingMechanics.EndBattle()
        });
    }

    private static void OnCardAction(AuraCardActionContext context)
    {
        if (context.Config == null || CardConfigApi.Id(context.Config) != MoonHomecomingIds.HomecomingNight) return;
        if (context.Phase == AuraCardActionPhase.NativeStarted)
        {
            if (context.Config.scriptExecutor is ScriptExecutor executor)
                MoonHomecomingMechanics.PrepareHomecoming(executor);
        }
        else context.Config.Vars.Remove(MoonHomecomingIds.HomecomingChroniclesKey);
    }
}
