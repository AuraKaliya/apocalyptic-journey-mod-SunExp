using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class PolymorphRuntime
{
    private static readonly object SkillUseSyncRoot = new();
    private static readonly HashSet<int> PendingSkillUses = new();

    public static void Initialize(ModConfig modConfig)
    {
        PolymorphRoleCropRegistry.Load(modConfig);
        PolymorphCardFaceRuntime.Initialize(modConfig);
        TerriasBattleLifecycleRouter.Register("Polymorph", new TerriasBattleLifecycleSubscription
        {
            AdventureStarting = context => ClearAdventure("AdventureStarting"),
            FightStarted = context => ClearBattle("Fight_Start.Init"),
            FightRestarting = context => ClearBattle("FightRestarting"),
            FightEnding = context => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, TerriasHookTargets.FightWinInit, context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightLossInit, context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightEscapeInit, context => ClearBattle("Fight_Escape.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.SkillItemTrueUse, CaptureSkillUseBefore);
        RegisterAfter(modConfig, TerriasHookTargets.SkillItemTrueUse, MarkSkillUseAfter);
        TerriasLog.Info("Polymorph runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "Polymorph");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "Polymorph");
    }

    private static void ClearBattle(string source)
    {
        try
        {
            PolymorphActivationService.ClearBattle(source);
            PolymorphNetworkSync.ClearPending(source);
            PolymorphUiApi.CloseRoleSelection(source);
            ClearPendingSkillUses();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Polymorph battle cleanup failed from " + source, ex);
        }
    }

    private static void ClearAdventure(string source)
    {
        PolymorphNetworkSync.ClearPending(source);
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
            TerriasLog.Warn("[Polymorph] failed to capture skill use: " + ex.Message);
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
            TerriasLog.Warn("[Polymorph] failed to mark skill use: " + ex.Message);
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
