using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class LoneerRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraSharedHooks.RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart, SunExpLog.Debug, message => SunExpLog.Warn("Loneer " + message));
    }

    private static void OnFightStart(ModHookContext context)
    {
        if (!LoneerMiracleService.IsActive())
        {
            LoneerCombatStateStore.ClearAll();
            StarStonePouchStateStore.ClearAll();
            return;
        }
    }
}
