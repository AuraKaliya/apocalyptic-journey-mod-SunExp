using System;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class EmberAdventureStateRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraSharedHooks.RegisterAfter(
            modConfig,
            "Fight_Start.Init",
            RestoreForLocalPlayer,
            SunExpLog.Debug,
            message => SunExpLog.Warn("Ember adventure state " + message));
    }

    private static void RestoreForLocalPlayer(ModHookContext context)
    {
        try
        {
            EmberAdventureStateService.RestoreForLocalPlayer("Fight_Start.Init");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Ember adventure state restore failed", ex);
        }
    }
}
