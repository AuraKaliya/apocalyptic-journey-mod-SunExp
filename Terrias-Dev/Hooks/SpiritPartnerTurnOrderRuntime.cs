using System;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

/// <summary>
/// Reorders the complete native Partner phase. Terrias companions contribute
/// their configured speed; native and other-mod Partners keep the neutral 100.
/// </summary>
public static class SpiritPartnerTurnOrderRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.Before(modConfig, "FightManager.DOAllAction", ReorderBeforeSnapshot, "PartnerTurnOrder");
    }

    private static void ReorderBeforeSnapshot(ModHookContext context)
    {
        try
        {
            var manager = context.Target as FightManager ?? FightManager.Instance;
            if (manager?.ActionQueue == null || manager.ActionQueue.Count < 2) return;
            manager.ActionQueue = PartnerTurnOrderPolicy.ReorderPartnerSubsequence(
                    manager.ActionQueue,
                    actor => actor is Partner,
                    actor => actor == null ? 100 : CompanionBattleStateStore.Find(actor.InstanceId)?.Stats.Speed ?? 100,
                    actor => actor?.InstanceId ?? "")
                .ToList();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[PartnerSpeed] partner turn order hook failed", ex);
        }
    }
}
