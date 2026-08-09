using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SunCardPackMigrationRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TryMigrate("Initialize");
        TerriasHookRegistry.Before(
            modConfig,
            "CardPackUI.Init",
            _ => TryMigrate("CardPackUI.Init"),
            "SunCardPackMigration");
    }

    private static void TryMigrate(string source)
    {
        try
        {
            var runtime = Singleton<GameRuntimeData>.Instance;
            if (runtime != null && SunCardPackSelectionMigration.Apply(runtime.UseCardPack))
            {
                runtime.Save();
                TerriasLog.Info("[CardPackMigration] consolidated selected Solar packs from " + source + ".");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CardPackMigration] migration failed from " + source + ": " + ex.Message);
        }
    }
}
