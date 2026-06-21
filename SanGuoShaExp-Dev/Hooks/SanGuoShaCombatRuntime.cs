using System;
using SanGuoShaExp.Dll.Infrastructure;
using SanGuoShaExp.Dll.Scripting;
using UiRaycastSafetyShared;
using Witch.Core;
using Witch.Mod;

namespace SanGuoShaExp.Dll.Hooks;

public static class SanGuoShaCombatRuntime
{
    private static bool initialized;
    private static bool combatActive;
    private static int generation;

    public static bool IsCombatActive => combatActive;

    public static int Generation => generation;

    public static bool IsCurrentGeneration(int capturedGeneration)
    {
        return combatActive && capturedGeneration == generation;
    }

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        RegisterBefore(modConfig, "Fight_Start.Init", _ => BeginCombat("Fight_Start.Init.before"));
        RegisterAfter(modConfig, "Fight_Start.Init", _ => BeginCombat("Fight_Start.Init"));
        RegisterBefore(modConfig, "FightInit.Init", _ => BeginCombat("FightInit.Init.before"));
        RegisterAfter(modConfig, "FightInit.Init", _ => BeginCombat("FightInit.Init"));
        RegisterBefore(modConfig, "Fight_Win.ResetStates", _ => EndCombat("Fight_Win.ResetStates.before"));
        RegisterAfter(modConfig, "Fight_Win.ResetStates", _ => EndCombat("Fight_Win.ResetStates.after"));
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", _ => EndCombat("Fight_Escape.ResetStates.before"));
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", _ => EndCombat("Fight_Escape.ResetStates.after"));
        RegisterBefore(modConfig, "Fight_Loss.Init", _ => EndCombat("Fight_Loss.Init.before"));
        RegisterAfter(modConfig, "Fight_Loss.Init", _ => EndCombat("Fight_Loss.Init.after"));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, context => SafeInvoke(() => action(context)));
            SanGuoShaExpLog.Info("Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, context => SafeInvoke(() => action(context)));
            SanGuoShaExpLog.Info("Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static void BeginCombat(string source)
    {
        if (!combatActive)
        {
            generation++;
            combatActive = true;
        }

        SanGuoShaExpLog.Debug("Combat begin: " + source + ", generation=" + generation);
    }

    private static void EndCombat(string source)
    {
        SanGuoShaUiRaycastGuardRuntime.BeginTransitionGuard(source);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(source + ":immediate", SanGuoShaExpLog.Debug);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(12, source + ":transition", SanGuoShaExpLog.Debug);

        if (combatActive)
        {
            combatActive = false;
            generation++;
        }

        SanGuoShaDodgeRuntime.ClearPending();
        ShenZhugeLiangScripts.ClearAllRuntimeState();
        SanGuoShaExpLog.Debug("Combat end: " + source + ", generation=" + generation);
    }

    private static void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Combat lifecycle hook failed: " + ex.Message);
        }
    }
}
