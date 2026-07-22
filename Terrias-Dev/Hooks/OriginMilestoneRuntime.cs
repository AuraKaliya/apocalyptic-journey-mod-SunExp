using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class OriginMilestoneRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.After(modConfig, "RoleTable.VarsCheck", ReconcileFromRoleHook, "OriginMilestone");
        TerriasHookRegistry.After(modConfig, "RoleTable.Init", ReconcileFromRoleHook, "OriginMilestone");
        TerriasHookRegistry.After(modConfig, "NormalMapManager.InitRoleTable", ReconcileCurrent, "OriginMilestone");
        TerriasBattleLifecycleRouter.Register("OriginMilestone", new TerriasBattleLifecycleSubscription
        {
            AdventureStarting = context => ReconcileCurrent(context),
            FightInitializing = context => ReconcileCurrent(context)
        });
    }

    private static void ReconcileFromRoleHook(ModHookContext context)
    {
        try
        {
            var role = context.Target as RoleTable ?? RoleTable.Instance;
            OriginMilestoneService.Reconcile(role, context.Target?.GetType().Name ?? "RoleTable");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[OriginMilestone] role hook failed: " + ex.Message);
        }
    }

    private static void ReconcileCurrent(ModHookContext context)
    {
        try
        {
            OriginMilestoneService.Reconcile(RoleTable.Instance, context.Target?.GetType().Name ?? "Lifecycle");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[OriginMilestone] lifecycle reconcile failed: " + ex.Message);
        }
    }
}
