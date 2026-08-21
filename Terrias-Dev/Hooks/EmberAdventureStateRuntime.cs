using System;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class EmberAdventureStateRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TerriasBattleLifecycleRouter.Register("EmberAdventureState", new TerriasBattleLifecycleSubscription
        {
            BattleOpening = RestoreForLocalPlayer
        });
    }

    private static void RestoreForLocalPlayer(ModHookContext context)
    {
        try
        {
            EmberAdventureStateService.RestoreForLocalPlayer("BattleOpening");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Ember adventure state restore failed", ex);
        }
    }
}
