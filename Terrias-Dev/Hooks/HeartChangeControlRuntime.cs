using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class HeartChangeControlRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TerriasBattleLifecycleRouter.Register("HeartChange", new TerriasBattleLifecycleSubscription
        {
            FightStarted = context => ClearBattle("Fight_Start.Init"),
            FightEnding = context => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, TerriasHookTargets.FightWinInit, context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightLossInit, context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightEscapeInit, context => ClearBattle("Fight_Escape.Init:before"));
        RegisterAfter(modConfig, "ScriptExecutor.SetStatus", RetargetAfterSetStatus);
        RegisterBefore(modConfig, "ScriptExecutor.RunScript", RetargetBeforeRunScript);
        TerriasCombatActionRouter.Register("HeartChange", new TerriasCombatActionSubscription
        {
            BeforeOtherObjAction = BeginEnemyAction,
            AfterOtherObjAction = EndEnemyAction
        });
        TerriasStatusLifecycleRouter.Register("HeartChange", new TerriasStatusLifecycleSubscription
        {
            AfterHit = CleanupAfterStatusChanged,
            AfterCurHpChanged = CleanupAfterStatusChanged,
            AfterMaxHpChanged = CleanupAfterStatusChanged
        });
        TerriasLog.Info("Heart change control runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "HeartChange");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "HeartChange");
    }

    private static void ClearBattle(string source)
    {
        try
        {
            HeartChangeControlService.ClearBattle(source);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Heart change cleanup failed from " + source, ex);
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
            TerriasLog.Warn("[HeartChange] retarget failed: " + ex.Message);
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
            TerriasLog.Warn("[HeartChange] use-script retarget failed: " + ex.Message);
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
            TerriasLog.Warn("[HeartChange] action begin failed: " + ex.Message);
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
            TerriasLog.Warn("[HeartChange] action end failed: " + ex.Message);
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
            TerriasLog.Warn("[HeartChange] status cleanup failed: " + ex.Message);
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
