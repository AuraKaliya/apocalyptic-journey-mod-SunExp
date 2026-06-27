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
        RunHookStep("status buff hooks", () =>
        {
            RegisterBefore(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffBefore);
            RegisterAfter(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffAfter);
        });
        RunHookStep("dialogue flow", () => DialogueFlowRuntime.Initialize(modConfig));
        RunHookStep("dusk partner", () => DuskPartnerRuntime.Initialize(modConfig));
        RunHookStep("star clay doll", () => StarClayDollRuntime.Initialize(modConfig));
        RunHookStep("solar memory mode", () => SolarMemoryModeRuntime.Initialize(modConfig));
        RunHookStep("solar memory combat", () => SolarMemoryCombatRuntime.Initialize(modConfig));
        RunHookStep("solar memory reward", () => SolarMemoryRewardRuntime.Initialize());
        RunHookStep("battle reward adjustment", () => BattleRewardAdjustmentRuntime.Initialize(modConfig));
        RunHookStep("solar memory content isolation", () => SolarMemoryContentIsolationRuntime.Initialize(modConfig));
        RunHookStep("solar memory starter deck", () => SolarMemoryStarterDeckRuntime.Initialize(modConfig));
        RunHookStep("hard tags", () => SunExpHardTagRuntime.Initialize(modConfig));
        RunHookStep("animated blessing icons", () => AnimatedBlessingIconRuntime.Initialize(modConfig));
        RunHookStep("animated buff icons", () => AnimatedBuffIconRuntime.Initialize(modConfig));
        RunHookStep("animated enemy dictionary icons", () => AnimatedEnemyDictIconRuntime.Initialize(modConfig));
        RunHookStep("solar memory map item animation", () => SolarMemoryMapItemAnimationRuntime.Initialize(modConfig));
        RunHookStep("map node card art", () => MapNodeCardArtRuntime.Initialize(modConfig));
        RunHookStep("star score runtime", () => StarScoreRuntime.Initialize(modConfig));
        RunHookStep("star score HUD", () => StarScoreHudRuntime.Initialize(modConfig));
        RunHookStep("loneer runtime", () => LoneerRuntime.Initialize(modConfig));
        SunExpLog.Info("Runtime hooks registered");
    }

    private static void RunHookStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => SunExpLog.Error("Runtime hook step failed: " + step, ex));
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
