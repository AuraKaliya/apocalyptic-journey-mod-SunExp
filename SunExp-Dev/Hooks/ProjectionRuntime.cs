using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class ProjectionRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        SunExpBattleLifecycleRouter.Register("Projection", new SunExpBattleLifecycleSubscription
        {
            FightStarted = context => ClearBattle("Fight_Start.Init"),
            FightEnding = context => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, SunExpHookTargets.FightWinInit, context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightLossInit, context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightEscapeInit, context => ClearBattle("Fight_Escape.Init:before"));
        SunExpStatusLifecycleRouter.Register("Projection", new SunExpStatusLifecycleSubscription
        {
            AfterHit = RetireProjectionAfterDamage,
            AfterCurHpChanged = RetireProjectionAfterHpChange,
            AfterMaxHpChanged = RetireProjectionAfterHpChange
        });
        SunExpLog.Info("Projection runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "Projection");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "Projection");
    }

    private static void ClearBattle(string source)
    {
        try
        {
            ProjectionActivationService.ClearBattle(source);
            ProjectionUiApi.CloseRoleSelection(source);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Projection battle cleanup failed from " + source, ex);
        }
    }

    private static void RetireProjectionAfterDamage(ModHookContext context)
    {
        RetireProjectionIfDead(context, "StatusManager.Hit");
    }

    private static void RetireProjectionAfterHpChange(ModHookContext context)
    {
        RetireProjectionIfDead(context, "StatusManager.HpChanged");
    }

    private static void RetireProjectionIfDead(ModHookContext context, string source)
    {
        try
        {
            if (context.Target is IStatusManager status)
            {
                ProjectionStateStore.RetireIfDead(status, source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Projection death cleanup failed from " + source, ex);
        }
    }
}
