using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public sealed class SunExpStatusLifecycleSubscription
{
    public Action<ModHookContext>? BeforeAddBuff { get; set; }
    public Action<ModHookContext>? AfterAddBuff { get; set; }
    public Action<ModHookContext>? AfterRemoveBuff { get; set; }
    public Action<ModHookContext>? AfterBuffLevelChanged { get; set; }
    public Action<ModHookContext>? AfterHit { get; set; }
    public Action<ModHookContext>? AfterCurHpChanged { get; set; }
    public Action<ModHookContext>? AfterMaxHpChanged { get; set; }
    public Action<ModHookContext>? AfterEnemyInit { get; set; }
    public Action<ModHookContext>? AfterInitAnimator { get; set; }
    public Action<ModHookContext>? AfterSetSprite { get; set; }
    public Action<ModHookContext>? AfterFightUiFadeIn { get; set; }
}

public static class SunExpStatusLifecycleRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, SunExpStatusLifecycleSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, SunExpStatusLifecycleSubscription>[]? cachedSubscriptions;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Before(modConfig, SunExpHookTargets.StatusManagerAddBuff, subscription => subscription.BeforeAddBuff);
        After(modConfig, SunExpHookTargets.StatusManagerAddBuff, subscription => subscription.AfterAddBuff);
        After(modConfig, SunExpHookTargets.StatusManagerRemoveBuff, subscription => subscription.AfterRemoveBuff);
        After(modConfig, SunExpHookTargets.BuffItemConfigSetLevel, subscription => subscription.AfterBuffLevelChanged);
        After(modConfig, SunExpHookTargets.StatusManagerHit, subscription => subscription.AfterHit);
        After(modConfig, SunExpHookTargets.StatusManagerSetCurHp, subscription => subscription.AfterCurHpChanged);
        After(modConfig, SunExpHookTargets.StatusManagerSetMaxHp, subscription => subscription.AfterMaxHpChanged);
        After(modConfig, SunExpHookTargets.EnemyInit, subscription => subscription.AfterEnemyInit);
        After(modConfig, SunExpHookTargets.StatusManagerInitAnimator, subscription => subscription.AfterInitAnimator);
        After(modConfig, SunExpHookTargets.StatusManagerSetSprite, subscription => subscription.AfterSetSprite);
        After(modConfig, SunExpHookTargets.FightUiFadeIn, subscription => subscription.AfterFightUiFadeIn);
    }

    public static void Register(string id, SunExpStatusLifecycleSubscription subscription)
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

        SunExpPerformanceCounters.Record("StatusLifecycle.HandlerRegistered");
    }

    private static void Before(
        ModConfig config,
        string target,
        Func<SunExpStatusLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        SunExpHookRegistry.BeforeRouted(config, target, context => Dispatch(target, context, selector), "StatusLifecycle");
    }

    private static void After(
        ModConfig config,
        string target,
        Func<SunExpStatusLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        SunExpHookRegistry.AfterRouted(config, target, context => Dispatch(target, context, selector), "StatusLifecycle");
    }

    private static void Dispatch(
        string target,
        ModHookContext context,
        Func<SunExpStatusLifecycleSubscription, Action<ModHookContext>?> selector)
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
                SunExpLog.Error("Status lifecycle handler failed: " + pair.Key + " @ " + target, ex);
            }
        }
    }

    private static KeyValuePair<string, SunExpStatusLifecycleSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, SunExpStatusLifecycleSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
