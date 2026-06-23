using System;
using System.Collections;
using System.Reflection;
using AuraShared.Core;
using AuraSkin.Shared;
using StarterDeckArbiter.Shared;
using Witch.Mod;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using UiTransitionGuardShared;

namespace SunExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RunStep("XLua assembly registration", RegisterLuaVisibleAssembly);
        RunStep("shared core", () => AuraSharedRuntime.Initialize(modConfig, "SunExp"));
        RunStep("shared resource package", () => RegisterSharedResourcePackage(modConfig));
        RunStep("shared registry", () => AuraSharedRegistry.RegisterManifest(modConfig, "SunExp"));
        RunStep("starter deck profiles", () => StarterDeckArbiterRuntime.RegisterProfileManifest(modConfig, "SunExp"));
        RunStep("shared skin runtime", () => AuraSkinRuntime.Initialize(modConfig, "SunExp"));
        RunStep("shared skin package", () => RegisterSkinPackage(modConfig));
        RunStep("journey runtime", () => SolarMemoryJourneyApi.Initialize(modConfig));
        RunStep("audio runtime", () => AudioApi.Initialize(modConfig));
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, "SunExp"));
        SunExpLog.Info("SunExp C# entry loaded");
        RunStep("gameplay hooks", () => RuntimeHooks.Initialize(modConfig));
        RunStep("special tags", SpecialTagRuntime.Initialize);
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => SunExpLog.Error("Initialization step failed: " + step, ex));
    }

    private static void RegisterSkinPackage(ModConfig modConfig)
    {
        if (!AuraSkinRuntime.RegisterPackage(modConfig, "SunExp"))
        {
            SunExpLog.Warn("SunExp bundled skin package was rejected; skin package registration skipped.");
        }
    }

    private static void RegisterSharedResourcePackage(ModConfig modConfig)
    {
        var responses = AuraSharedPackageEngine.InstallManifest(modConfig, "SunExp");
        foreach (var response in responses)
        {
            if (!response.Success)
            {
                throw new InvalidOperationException("SunExp shared resource package was rejected: " + response.Message);
            }
        }
    }

    private static void RegisterLuaVisibleAssembly()
    {
        var assembly = typeof(Entry).Assembly;
        var assemblyName = assembly.GetName().Name;
        var luaEnv = ScriptExecutor.luaEnv;
        if (luaEnv == null)
        {
            SunExpLog.Warn("Unable to register C# script assembly for XLua: LuaEnv is null");
            return;
        }

        try
        {
            var translator = luaEnv.GetType()
                .GetField("translator", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(luaEnv);
            var assemblies = translator?.GetType()
                .GetField("assemblies", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(translator) as IList;

            if (assemblies == null)
            {
                SunExpLog.Warn("Unable to register C# script assembly for XLua: translator assembly list missing");
                return;
            }

            var alreadyRegistered = false;
            foreach (var item in assemblies)
            {
                if (item is Assembly existing && existing.FullName == assembly.FullName)
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
            {
                assemblies.Add(assembly);
            }

            luaEnv.DoString(
                "assert(xlua.import_type('SunExp.Dll.Scripting.CardScripts'), 'SunExp CardScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.WunaScripts'), 'SunExp WunaScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.EventScripts'), 'SunExp EventScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.BossScripts'), 'SunExp BossScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.BuffScripts'), 'SunExp BuffScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.RelicScripts'), 'SunExp RelicScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.PartnerScripts'), 'SunExp PartnerScripts unavailable');",
                "SunExp.RegisterLuaVisibleAssembly");
            SunExpLog.Info("Registered C# script assembly for XLua: " + assemblyName);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to register C# script assembly for XLua: " + assemblyName, ex);
        }
    }
}
