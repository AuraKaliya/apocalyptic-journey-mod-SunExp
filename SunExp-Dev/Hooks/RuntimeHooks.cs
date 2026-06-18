using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class RuntimeHooks
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffBefore);
        DuskPartnerRuntime.Initialize(modConfig);
        SolarMemoryModeRuntime.Initialize(modConfig);
        SolarMemoryStarterDeckRuntime.Initialize(modConfig);
        AnimatedBlessingIconRuntime.Initialize(modConfig);
        AnimatedBuffIconRuntime.Initialize(modConfig);
        SunExpLog.Info("Runtime hooks registered");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            SunExpLog.Debug("Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void OnStatusManagerAddBuffBefore(ModHookContext context)
    {
        try
        {
            var target = context.Target as IStatusManager;
            var args = context.Arguments;
            var buffId = Convert.ToString(args != null && args.Length > 0 ? args[0] : null);
            if (target == null || buffId != SunExpIds.Burn)
            {
                return;
            }

            var amount = DictionaryUtil.ParseInt(Convert.ToString(args != null && args.Length > 1 ? args[1] : null));
            if (amount <= 0)
            {
                return;
            }

            ExecutorApi.HandleBurnOverflow(target, buffId, amount);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Status AddBuff before hook failed", ex);
        }
    }
}
