using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class PolymorphRuntime
{
    private static readonly object SkillUseSyncRoot = new();
    private static readonly HashSet<int> PendingSkillUses = new();

    public static void Initialize(ModConfig modConfig)
    {
        PolymorphRoleCropRegistry.Load(modConfig);
        PolymorphCardFaceRuntime.Initialize(modConfig);
        RegisterAfter(modConfig, "Fight_Start.Init", context => ClearBattle("Fight_Start.Init"));
        RegisterBefore(modConfig, "Fight_Win.Init", context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, "Fight_Loss.Init", context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, "Fight_Escape.Init", context => ClearBattle("Fight_Escape.Init:before"));
        RegisterBefore(modConfig, "Fight_Win.ResetStates", context => ClearBattle("Fight_Win.ResetStates:before"));
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", context => ClearBattle("Fight_Escape.ResetStates:before"));
        RegisterBefore(modConfig, "SkillItem.TrueUse", CaptureSkillUseBefore);
        RegisterAfter(modConfig, "SkillItem.TrueUse", MarkSkillUseAfter);
        SunExpLog.Info("Polymorph runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Polymorph " + message));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Polymorph " + message));
    }

    private static void ClearBattle(string source)
    {
        try
        {
            PolymorphActivationService.ClearBattle(source);
            PolymorphUiApi.CloseRoleSelection(source);
            PolymorphCardFaceCache.ClearGenerated(source);
            ClearPendingSkillUses();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Polymorph battle cleanup failed from " + source, ex);
        }
    }

    private static void CaptureSkillUseBefore(ModHookContext context)
    {
        try
        {
            if (context.Target is not SkillItem skillItem
                || !PolymorphCooldownService.ShouldCaptureSkillUse(skillItem, "SkillItem.TrueUse"))
            {
                return;
            }

            lock (SkillUseSyncRoot)
            {
                PendingSkillUses.Add(skillItem.GetInstanceID());
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] failed to capture skill use: " + ex.Message);
        }
    }

    private static void MarkSkillUseAfter(ModHookContext context)
    {
        try
        {
            if (context.Target is not SkillItem skillItem || !TakePendingSkillUse(skillItem))
            {
                return;
            }

            PolymorphCooldownService.MarkSkillItemUsed(skillItem, "SkillItem.TrueUse");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] failed to mark skill use: " + ex.Message);
        }
    }

    private static bool TakePendingSkillUse(SkillItem skillItem)
    {
        lock (SkillUseSyncRoot)
        {
            return PendingSkillUses.Remove(skillItem.GetInstanceID());
        }
    }

    private static void ClearPendingSkillUses()
    {
        lock (SkillUseSyncRoot)
        {
            PendingSkillUses.Clear();
        }
    }
}
