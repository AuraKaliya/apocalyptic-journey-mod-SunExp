using System;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class HeartChangeControlRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        SunExpBattleLifecycleRouter.Register("HeartChange", new SunExpBattleLifecycleSubscription
        {
            FightStarted = context => ClearBattle("Fight_Start.Init"),
            FightEnding = context => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, SunExpHookTargets.FightWinInit, context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightLossInit, context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightEscapeInit, context => ClearBattle("Fight_Escape.Init:before"));
        RegisterAfter(modConfig, "ScriptExecutor.SetStatus", RetargetAfterSetStatus);
        RegisterBefore(modConfig, "ScriptExecutor.RunScript", RetargetBeforeRunScript);
        RegisterBefore(modConfig, SunExpHookTargets.OtherObjDoOneAction, BeginEnemyAction);
        RegisterAfter(modConfig, SunExpHookTargets.OtherObjDoOneAction, EndEnemyAction);
        RegisterAfter(modConfig, "StatusManager.Hit", CleanupAfterStatusChanged);
        RegisterAfter(modConfig, "StatusManager.set_CurHp", CleanupAfterStatusChanged);
        RegisterAfter(modConfig, "StatusManager.set_MaxHp", CleanupAfterStatusChanged);
        SunExpLog.Info("Heart change control runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "HeartChange");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "HeartChange");
    }

    private static void ClearBattle(string source)
    {
        try
        {
            HeartChangeControlService.ClearBattle(source);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Heart change cleanup failed from " + source, ex);
        }
    }

    private static void RetargetAfterSetStatus(ModHookContext context)
    {
        try
        {
            if (context.Target is not ScriptExecutor executor
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not string filter)
            {
                return;
            }

            HeartChangeControlService.HandleSetStatus(executor, filter);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] retarget failed: " + ex.Message);
        }
    }

    private static void RetargetBeforeRunScript(ModHookContext context)
    {
        try
        {
            if (context.Target is not ScriptExecutor executor
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not string scriptName)
            {
                return;
            }

            HeartChangeControlService.HandleRunScript(executor, scriptName);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] use-script retarget failed: " + ex.Message);
        }
    }

    private static void BeginEnemyAction(ModHookContext context)
    {
        try
        {
            if (context.Target is Enemy enemy)
            {
                HeartChangeControlService.BeginEnemyAction(enemy, ActionIndex(context), IsSingleAction(context));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] action begin failed: " + ex.Message);
        }
    }

    private static void EndEnemyAction(ModHookContext context)
    {
        try
        {
            if (context.Target is Enemy enemy)
            {
                HeartChangeControlService.EndEnemyAction(enemy, ActionIndex(context));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] action end failed: " + ex.Message);
        }
    }

    private static void CleanupAfterStatusChanged(ModHookContext context)
    {
        try
        {
            if (context.Target is IStatusManager status)
            {
                HeartChangeControlService.CleanupIfDead(status, "StatusChanged");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] status cleanup failed: " + ex.Message);
        }
    }

    private static int ActionIndex(ModHookContext context)
    {
        return context.Arguments != null
            && context.Arguments.Length > 0
            && context.Arguments[0] is int index
            ? index
            : -1;
    }

    private static bool IsSingleAction(ModHookContext context)
    {
        return context.Arguments != null
            && context.Arguments.Length > 1
            && context.Arguments[1] is bool isSingle
            && isSingle;
    }
}
