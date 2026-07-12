using System;
using System.Collections.Generic;
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
        SunExpBattleLifecycleRouter.Register("Polymorph", new SunExpBattleLifecycleSubscription
        {
            AdventureStarting = context => ClearAdventure("AdventureStarting"),
            FightStarted = context => ClearBattle("Fight_Start.Init"),
            FightEnding = context => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, SunExpHookTargets.FightWinInit, context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightLossInit, context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightEscapeInit, context => ClearBattle("Fight_Escape.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.SkillItemTrueUse, CaptureSkillUseBefore);
        RegisterAfter(modConfig, SunExpHookTargets.SkillItemTrueUse, MarkSkillUseAfter);
        SunExpLog.Info("Polymorph runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "Polymorph");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "Polymorph");
    }

    private static void ClearBattle(string source)
    {
        try
        {
            PolymorphActivationService.ClearBattle(source);
            PolymorphUiApi.CloseRoleSelection(source);
            ClearPendingSkillUses();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Polymorph battle cleanup failed from " + source, ex);
        }
    }

    private static void ClearAdventure(string source)
    {
        PolymorphCardFaceCache.ClearGenerated(source);
        ClearPendingSkillUses();
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
