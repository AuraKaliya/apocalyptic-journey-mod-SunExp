using System;
using System.Collections;
using System.Reflection;
using AuraJourney.Shared;
using AuraShared.Core;
using SanGuoShaExp.Dll.GameApi;
using SanGuoShaExp.Dll.Hooks;
using SanGuoShaExp.Dll.Infrastructure;
using StarterDeckArbiter.Shared;
using Witch.Mod;

namespace SanGuoShaExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RegisterLuaVisibleAssembly();
        SanGuoShaUiRaycastGuardRuntime.Initialize(modConfig);
        SanGuoShaCombatRuntime.Initialize(modConfig);
        SanGuoShaDodgeRuntime.Initialize(modConfig);
        RunStep("journey runtime", () => AuraJourneyRuntime.Initialize(modConfig, SanGuoShaExpIds.ModId));
        RunStep("starter deck profiles", () => StarterDeckArbiterRuntime.RegisterProfileManifest(modConfig, SanGuoShaExpIds.ModId));
        AudioApi.Initialize(modConfig);
        BattleBgmProviderRuntime.Initialize(modConfig);
        SanGuoShaExpLog.Info("SanGuoShaExp C# entry loaded");
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => SanGuoShaExpLog.Error("Initialization step failed: " + step, ex));
    }

    private static void RegisterLuaVisibleAssembly()
    {
        var assembly = typeof(Entry).Assembly;
        var assemblyName = assembly.GetName().Name;
        var luaEnv = ScriptExecutor.luaEnv;
        if (luaEnv == null)
        {
            SanGuoShaExpLog.Warn("Unable to register C# script assembly for XLua: LuaEnv is null");
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
                SanGuoShaExpLog.Warn("Unable to register C# script assembly for XLua: translator assembly list missing");
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
                "assert(xlua.import_type('SanGuoShaExp.Dll.Scripting.ShenZhugeLiangScripts'), 'SanGuoShaExp ShenZhugeLiangScripts unavailable');"
                + "assert(xlua.import_type('SanGuoShaExp.Dll.Scripting.SanGuoShaCardScripts'), 'SanGuoShaExp SanGuoShaCardScripts unavailable');"
                + "assert(xlua.import_type('SanGuoShaExp.Dll.Scripting.SanGuoShaBuffScripts'), 'SanGuoShaExp SanGuoShaBuffScripts unavailable');"
                + "assert(xlua.import_type('SanGuoShaExp.Dll.Scripting.SanGuoShaRelicScripts'), 'SanGuoShaExp SanGuoShaRelicScripts unavailable');",
                "SanGuoShaExp.RegisterLuaVisibleAssembly");
            SanGuoShaExpLog.Info("Registered C# script assembly for XLua: " + assemblyName);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Failed to register C# script assembly for XLua: " + assemblyName, ex);
        }
    }
}
