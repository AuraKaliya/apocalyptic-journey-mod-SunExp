using System;
using SkinExp.Dll.Hooks;
using SkinExp.Dll.Infrastructure;
using SkinExp.Dll.Mechanics;
using Witch.Mod;

namespace SkinExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RunStep("paths", () => SkinPaths.Initialize(modConfig));
        RunStep("skin registry", SkinRuntime.Initialize);
        RunStep("runtime hooks", () => SkinRuntimeHooks.Initialize(modConfig));
        SkinLog.Info("SkinExp loaded");
    }

    private static void RunStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SkinLog.Error("Initialization step failed: " + name, ex);
        }
    }
}
