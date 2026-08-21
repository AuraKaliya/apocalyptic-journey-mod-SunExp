using System;
using System.Collections.Generic;
using AuraShared.Core;
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
            BattleInitializing = context => ClearBattle("BattleInitializing"),
            BattleRestarting = context => ClearBattle("BattleRestarting"),
            OutcomeEntering = context => ClearBattle("OutcomeEntering." + context.Outcome)
        });
        AuraSkillActionTransactionRouter.Register(
            modConfig,
            TerriasIds.ModId,
            "Polymorph.SkillUse",
            new AuraSkillActionSubscription
            {
                Phases = AuraSkillActionPhase.Attempting | AuraSkillActionPhase.Completed | AuraSkillActionPhase.Aborted,
                Handler = context =>
                {
                    if (context.Phase == AuraSkillActionPhase.Attempting)
                    {
                        CaptureSkillUseBefore(context.NativeContext);
                    }
                    else if (context.Phase == AuraSkillActionPhase.Completed)
                    {
                        MarkSkillUseAfter(context.NativeContext);
                    }
                    else if (context.NativeContext.Target is SkillItem skillItem)
                    {
                        TakePendingSkillUse(skillItem);
                    }
                }
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
        TerriasLog.Info("Polymorph runtime initialized");
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
