using System;
using AuraShared.Core;
using AuraSkin.Shared;
using AuraSkin.Shared.Infrastructure;
using Witch.Mod;

namespace SkinExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RunStep("shared core", () => AuraSharedRuntime.Initialize(modConfig, "SkinExp"));
        RunStep("shared skin runtime", () => AuraSkinRuntime.Initialize(modConfig, "SkinExp"));
        RunStep("shared skin package", () => RegisterSkinPackage(modConfig));
        SkinLog.Info("SkinExp loaded");
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => SkinLog.Error("Initialization step failed: " + step, ex));
    }

    private static void RegisterSkinPackage(ModConfig modConfig)
    {
        if (!AuraSkinRuntime.RegisterPackage(modConfig, "SkinExp"))
        {
            throw new InvalidOperationException("SkinExp bundled skin package was rejected.");
        }
    }
}
