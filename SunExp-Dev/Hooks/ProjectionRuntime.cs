using System;
using AuraShared.Core;
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
        RegisterAfter(modConfig, "Fight_Start.Init", context => ClearBattle("Fight_Start.Init"));
        RegisterBefore(modConfig, "Fight_Win.Init", context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, "Fight_Loss.Init", context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, "Fight_Escape.Init", context => ClearBattle("Fight_Escape.Init:before"));
        RegisterBefore(modConfig, "Fight_Win.ResetStates", context => ClearBattle("Fight_Win.ResetStates:before"));
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", context => ClearBattle("Fight_Escape.ResetStates:before"));
        RegisterAfter(modConfig, "StatusManager.Hit", RetireProjectionAfterDamage);
        RegisterAfter(modConfig, "StatusManager.set_CurHp", RetireProjectionAfterHpChange);
        RegisterAfter(modConfig, "StatusManager.set_MaxHp", RetireProjectionAfterHpChange);
        SunExpLog.Info("Projection runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Projection " + message));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Projection " + message));
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
