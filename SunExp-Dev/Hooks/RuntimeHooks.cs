using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class RuntimeHooks
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", SolarEventRuntime.EnsureInCurrentLayer);
        RegisterAfter(modConfig, "NormalMapManager.RandomGenerate", SolarEventRuntime.EnsureInCurrentLayer);
        RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", SolarEventRuntime.EnsureInCurrentLayer);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", SolarEventRuntime.RepairMapSelection);
        RegisterBefore(modConfig, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", SolarEventRuntime.RepairMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMap", SolarEventRuntime.RepairMapSelection);
        RegisterBefore(modConfig, "MapManager.CmdSelectMapIncludeSender", SolarEventRuntime.RepairMapSelection);
        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", SolarEventRuntime.RepairMapSelection);
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", SolarEventRuntime.RepairMapSelection);
        RegisterBefore(modConfig, "ScriptExecutor.AddBuff", OnScriptExecutorAddBuffBefore);
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

    private static void OnScriptExecutorAddBuffBefore(ModHookContext context)
    {
        try
        {
            var executor = context.Target as ScriptExecutor;
            var args = context.Arguments;
            var buffId = Convert.ToString(args != null && args.Length > 0 ? args[0] : null);
            if (executor == null || buffId != SunExpIds.Burn)
            {
                return;
            }

            var amount = DictionaryUtil.ParseInt(Convert.ToString(args != null && args.Length > 1 ? args[1] : null));
            if (amount <= 0)
            {
                return;
            }

            var handled = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in HookTargets(executor))
            {
                var key = target.InstanceId ?? target.GetHashCode().ToString();
                if (handled.Add(key))
                {
                    ExecutorApi.HandleBurnOverflow(executor, target, buffId, amount);
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("AddBuff before hook failed", ex);
        }
    }

    private static IEnumerable<IStatusManager> HookTargets(ScriptExecutor executor)
    {
        foreach (var target in executor.Object ?? new List<IStatusManager>())
        {
            if (target != null)
            {
                yield return target;
            }
        }

        if (executor.Target != null)
        {
            yield return executor.Target;
        }

        if (executor.Self != null)
        {
            yield return executor.Self;
        }
    }
}
