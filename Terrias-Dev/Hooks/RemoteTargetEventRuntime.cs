using System;
using System.Runtime.CompilerServices;
using Fight.ObjTarget;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class RemoteTargetEventRuntime
{
    private static readonly ConditionalWeakTable<ObjTargetAction, RemoteTargetEventLease> Leases = new();

    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.Before(
            modConfig,
            TerriasHookTargets.ObjTargetActionInternalExecute,
            Prepare,
            "RemoteTargetEvent");
        TerriasHookRegistry.After(
            modConfig,
            TerriasHookTargets.ObjTargetActionInternalExecute,
            Restore,
            "RemoteTargetEvent");
        TerriasLog.Info("Remote target event runtime initialized");
    }

    private static void Prepare(ModHookContext context)
    {
        if (context.Target is not ObjTargetAction action)
        {
            return;
        }

        Leases.Remove(action);
        var lease = RemoteTargetEventApi.Prepare(action);
        if (lease != null)
        {
            Leases.Add(action, lease);
        }
    }

    private static void Restore(ModHookContext context)
    {
        if (context.Target is not ObjTargetAction action || !Leases.TryGetValue(action, out var lease))
        {
            return;
        }

        Leases.Remove(action);
        try
        {
            lease.Restore();
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[RemoteTargetEvent] source config restore failed: " + ex.Message);
        }
    }
}
