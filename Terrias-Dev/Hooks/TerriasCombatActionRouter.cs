using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasCombatActionSubscription
{
    public Action<ModHookContext>? BeforeOtherObjAction { get; set; }
    public Action<ModHookContext>? AfterOtherObjAction { get; set; }
    public Action<ModHookContext>? BeforeFightUiActionAnimation { get; set; }
    public Action<ModHookContext>? AfterFightUiActionAnimation { get; set; }
}

public static class TerriasCombatActionRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, TerriasCombatActionSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, TerriasCombatActionSubscription>[]? cachedSubscriptions;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Before(modConfig, TerriasHookTargets.OtherObjDoOneAction, subscription => subscription.BeforeOtherObjAction);
        After(modConfig, TerriasHookTargets.OtherObjDoOneAction, subscription => subscription.AfterOtherObjAction);
        Before(modConfig, TerriasHookTargets.FightUiCallActionAnimation, subscription => subscription.BeforeFightUiActionAnimation);
        After(modConfig, TerriasHookTargets.FightUiCallActionAnimation, subscription => subscription.AfterFightUiActionAnimation);
    }

    public static void Register(string id, TerriasCombatActionSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            Subscriptions[id.Trim()] = subscription;
            cachedSubscriptions = null;
        }

        TerriasPerformanceCounters.Record("CombatAction.HandlerRegistered");
    }

    public static void RegisterActionEventHandler(
        string id,
        Action<TerriasActionEventContext>? onAction,
        Action? onActionAfter)
    {
        TerriasActionEventRouter.RegisterHandler(id, onAction, onActionAfter);
    }

    private static void Before(
        ModConfig config,
        string target,
        Func<TerriasCombatActionSubscription, Action<ModHookContext>?> selector)
    {
        TerriasHookRegistry.BeforeRouted(config, target, context => Dispatch(target, context, selector), "CombatAction");
    }

    private static void After(
        ModConfig config,
        string target,
        Func<TerriasCombatActionSubscription, Action<ModHookContext>?> selector)
    {
        TerriasHookRegistry.AfterRouted(config, target, context => Dispatch(target, context, selector), "CombatAction");
    }

    private static void Dispatch(
        string target,
        ModHookContext context,
        Func<TerriasCombatActionSubscription, Action<ModHookContext>?> selector)
    {
        foreach (var pair in SnapshotSubscriptions())
        {
            var action = selector(pair.Value);
            if (action == null)
            {
                continue;
            }

            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                TerriasLog.Error("Combat action handler failed: " + pair.Key + " @ " + target, ex);
            }
        }
    }

    private static KeyValuePair<string, TerriasCombatActionSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, TerriasCombatActionSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
