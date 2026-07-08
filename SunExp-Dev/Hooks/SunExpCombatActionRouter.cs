using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public sealed class SunExpCombatActionSubscription
{
    public Action<ModHookContext>? BeforeOtherObjAction { get; set; }
    public Action<ModHookContext>? AfterOtherObjAction { get; set; }
    public Action<ModHookContext>? BeforeFightUiActionAnimation { get; set; }
    public Action<ModHookContext>? AfterFightUiActionAnimation { get; set; }
}

public static class SunExpCombatActionRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, SunExpCombatActionSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, SunExpCombatActionSubscription>[]? cachedSubscriptions;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Before(modConfig, SunExpHookTargets.OtherObjDoOneAction, subscription => subscription.BeforeOtherObjAction);
        After(modConfig, SunExpHookTargets.OtherObjDoOneAction, subscription => subscription.AfterOtherObjAction);
        Before(modConfig, SunExpHookTargets.FightUiCallActionAnimation, subscription => subscription.BeforeFightUiActionAnimation);
        After(modConfig, SunExpHookTargets.FightUiCallActionAnimation, subscription => subscription.AfterFightUiActionAnimation);
    }

    public static void Register(string id, SunExpCombatActionSubscription subscription)
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

        SunExpPerformanceCounters.Record("CombatAction.HandlerRegistered");
    }

    public static void RegisterActionEventHandler(
        string id,
        Action<SunExpActionEventContext>? onAction,
        Action? onActionAfter)
    {
        SunExpActionEventRouter.RegisterHandler(id, onAction, onActionAfter);
    }

    private static void Before(
        ModConfig config,
        string target,
        Func<SunExpCombatActionSubscription, Action<ModHookContext>?> selector)
    {
        SunExpHookRegistry.BeforeRouted(config, target, context => Dispatch(target, context, selector), "CombatAction");
    }

    private static void After(
        ModConfig config,
        string target,
        Func<SunExpCombatActionSubscription, Action<ModHookContext>?> selector)
    {
        SunExpHookRegistry.AfterRouted(config, target, context => Dispatch(target, context, selector), "CombatAction");
    }

    private static void Dispatch(
        string target,
        ModHookContext context,
        Func<SunExpCombatActionSubscription, Action<ModHookContext>?> selector)
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
                SunExpLog.Error("Combat action handler failed: " + pair.Key + " @ " + target, ex);
            }
        }
    }

    private static KeyValuePair<string, SunExpCombatActionSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, SunExpCombatActionSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
