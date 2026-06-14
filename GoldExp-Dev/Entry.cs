using System;
using System.Collections;
using System.Reflection;
using Witch.Mod;
using GoldExp.Dll.Hooks;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RegisterLuaVisibleAssembly();
        GoldExpLog.Info("GoldExp C# entry loaded");
        GoldDreamTagRuntime.Initialize();
    }

    private static void RegisterLuaVisibleAssembly()
    {
        var assembly = typeof(Entry).Assembly;
        var assemblyName = assembly.GetName().Name;
        var luaEnv = ScriptExecutor.luaEnv;
        if (luaEnv == null)
        {
            GoldExpLog.Warn("Unable to register C# script assembly for XLua: LuaEnv is null");
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
                GoldExpLog.Warn("Unable to register C# script assembly for XLua: translator assembly list missing");
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
                "assert(xlua.import_type('GoldExp.Dll.Scripting.CardScripts'), 'GoldExp CardScripts unavailable');"
                + "assert(xlua.import_type('GoldExp.Dll.Scripting.GoldWitchScripts'), 'GoldExp GoldWitchScripts unavailable');"
                + "assert(xlua.import_type('GoldExp.Dll.Scripting.RelicScripts'), 'GoldExp RelicScripts unavailable');"
                + "assert(xlua.import_type('GoldExp.Dll.Scripting.PartnerScripts'), 'GoldExp PartnerScripts unavailable');",
                "GoldExp.RegisterLuaVisibleAssembly");
            GoldExpLog.Info("Registered C# script assembly for XLua: " + assemblyName);
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Failed to register C# script assembly for XLua: " + assemblyName, ex);
        }
    }
}
