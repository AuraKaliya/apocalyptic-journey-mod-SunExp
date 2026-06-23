using System;
using AuraShared.Core;
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
        StarClayDollRuntime.Initialize(modConfig);
        SolarMemoryModeRuntime.Initialize(modConfig);
        SolarMemoryContentIsolationRuntime.Initialize(modConfig);
        SolarMemoryStarterDeckRuntime.Initialize(modConfig);
        SunExpHardTagRuntime.Initialize(modConfig);
        AnimatedBlessingIconRuntime.Initialize(modConfig);
        AnimatedBuffIconRuntime.Initialize(modConfig);
        AnimatedEnemyDictIconRuntime.Initialize(modConfig);
        SolarMemoryMapItemAnimationRuntime.Initialize(modConfig);
        StarScoreRuntime.Initialize(modConfig);
        LoneerRuntime.Initialize(modConfig);
        SunExpLog.Info("Runtime hooks registered");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, SunExpLog.Warn);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, SunExpLog.Warn);
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

            StarScoreRuntime.TryApplyResonanceBeforeAddBuff(context);
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
