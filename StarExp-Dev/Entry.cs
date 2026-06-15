using System;
using System.Collections;
using System.Reflection;
using StarExp.Dll.Infrastructure;
using Witch.Mod;

namespace StarExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RegisterLuaVisibleAssembly();
        StarExpLog.Info("StarExp C# entry loaded");
    }

    private static void RegisterLuaVisibleAssembly()
    {
        var assembly = typeof(Entry).Assembly;
        var assemblyName = assembly.GetName().Name;
        var luaEnv = ScriptExecutor.luaEnv;
        if (luaEnv == null)
        {
            StarExpLog.Warn("Unable to register C# script assembly for XLua: LuaEnv is null");
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
                StarExpLog.Warn("Unable to register C# script assembly for XLua: translator assembly list missing");
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
                "assert(xlua.import_type('StarExp.Dll.Scripting.CardScripts'), 'StarExp CardScripts unavailable');"
                + "assert(xlua.import_type('StarExp.Dll.Scripting.BuffScripts'), 'StarExp BuffScripts unavailable');"
                + "assert(xlua.import_type('StarExp.Dll.Scripting.StarMiracleScripts'), 'StarExp StarMiracleScripts unavailable');",
                "StarExp.RegisterLuaVisibleAssembly");
            StarExpLog.Info("Registered C# script assembly for XLua: " + assemblyName);
        }
        catch (Exception ex)
        {
            StarExpLog.Error("Failed to register C# script assembly for XLua: " + assemblyName, ex);
        }
    }
}
