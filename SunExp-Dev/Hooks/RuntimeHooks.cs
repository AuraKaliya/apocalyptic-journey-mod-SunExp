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
        RegisterAfter(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffAfter);
        DuskPartnerRuntime.Initialize(modConfig);
        SolarMemoryModeRuntime.Initialize(modConfig);
        SolarMemoryContentIsolationRuntime.Initialize(modConfig);
        SolarMemoryStarterDeckRuntime.Initialize(modConfig);
        SunExpHardTagRuntime.Initialize(modConfig);
        AnimatedBlessingIconRuntime.Initialize(modConfig);
        AnimatedBuffIconRuntime.Initialize(modConfig);
        AnimatedEnemyDictIconRuntime.Initialize(modConfig);
        SolarMemoryMapItemAnimationRuntime.Initialize(modConfig);
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

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static void OnStatusManagerAddBuffBefore(ModHookContext context)
    {
        try
        {
            var target = context.Target as IStatusManager;
            var args = context.Arguments;
            var buffId = BuffIdFromArgs(args);
            if (target == null)
            {
                return;
            }

            var amount = BuffAmountFromArgs(args);
            ExecutorApi.PrepareSolarRadianceUpperBound(target, buffId);
            if (buffId != SunExpIds.Burn || amount <= 0)
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

    private static void OnStatusManagerAddBuffAfter(ModHookContext context)
    {
        try
        {
            var target = context.Target as IStatusManager;
            var args = context.Arguments;
            var buffId = BuffIdFromArgs(args);
            var amount = BuffAmountFromArgs(args);
            ExecutorApi.FinalizeSolarRadianceUpperBound(target, buffId, amount);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Status AddBuff after hook failed", ex);
        }
    }

    private static string BuffIdFromArgs(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return "";
        }

        return args[0] is IBuffItemConfig config
            ? config.BuffId ?? ""
            : Convert.ToString(args[0]) ?? "";
    }

    private static int BuffAmountFromArgs(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return 0;
        }

        return args[0] is IBuffItemConfig config
            ? config.Level
            : DictionaryUtil.ParseInt(Convert.ToString(args.Length > 1 ? args[1] : null));
    }
}
