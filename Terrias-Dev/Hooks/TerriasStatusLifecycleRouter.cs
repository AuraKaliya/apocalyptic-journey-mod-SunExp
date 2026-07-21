using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasStatusLifecycleSubscription
{
    public Action<ModHookContext>? BeforeAddBuff { get; set; }
    public Action<ModHookContext>? AfterAddBuff { get; set; }
    public Action<ModHookContext>? AfterRemoveBuff { get; set; }
    public Action<ModHookContext>? AfterBuffLevelChanged { get; set; }
    public Action<ModHookContext>? BeforeHit { get; set; }
    public Action<ModHookContext>? AfterHit { get; set; }
    public Action<ModHookContext>? BeforeEnemyDead { get; set; }
    public Action<ModHookContext>? AfterEnemyDead { get; set; }
    public Action<ModHookContext>? AfterCurHpChanged { get; set; }
    public Action<ModHookContext>? AfterMaxHpChanged { get; set; }
    public Action<ModHookContext>? AfterEnemyInit { get; set; }
    public Action<ModHookContext>? AfterInitAnimator { get; set; }
    public Action<ModHookContext>? AfterSetSprite { get; set; }
    public Action<ModHookContext>? AfterFightUiFadeIn { get; set; }
}

public static class TerriasStatusLifecycleRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, TerriasStatusLifecycleSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, TerriasStatusLifecycleSubscription>[]? cachedSubscriptions;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Before(modConfig, TerriasHookTargets.StatusManagerAddBuff, subscription => subscription.BeforeAddBuff);
        After(modConfig, TerriasHookTargets.StatusManagerAddBuff, subscription => subscription.AfterAddBuff);
        After(modConfig, TerriasHookTargets.StatusManagerRemoveBuff, subscription => subscription.AfterRemoveBuff);
        After(modConfig, TerriasHookTargets.BuffItemConfigSetLevel, subscription => subscription.AfterBuffLevelChanged);
        Before(modConfig, TerriasHookTargets.StatusManagerHit, subscription => subscription.BeforeHit);
        After(modConfig, TerriasHookTargets.StatusManagerHit, subscription => subscription.AfterHit);
        Before(modConfig, TerriasHookTargets.StatusManagerEnemyDead, subscription => subscription.BeforeEnemyDead);
        After(modConfig, TerriasHookTargets.StatusManagerEnemyDead, subscription => subscription.AfterEnemyDead);
        After(modConfig, TerriasHookTargets.StatusManagerSetCurHp, subscription => subscription.AfterCurHpChanged);
        After(modConfig, TerriasHookTargets.StatusManagerSetMaxHp, subscription => subscription.AfterMaxHpChanged);
        After(modConfig, TerriasHookTargets.EnemyInit, subscription => subscription.AfterEnemyInit);
        After(modConfig, TerriasHookTargets.StatusManagerInitAnimator, subscription => subscription.AfterInitAnimator);
        After(modConfig, TerriasHookTargets.StatusManagerSetSprite, subscription => subscription.AfterSetSprite);
        After(modConfig, TerriasHookTargets.FightUiFadeIn, subscription => subscription.AfterFightUiFadeIn);
    }

    public static void Register(string id, TerriasStatusLifecycleSubscription subscription)
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

        TerriasPerformanceCounters.Record("StatusLifecycle.HandlerRegistered");
    }

    private static void Before(
        ModConfig config,
        string target,
        Func<TerriasStatusLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        TerriasHookRegistry.BeforeRouted(config, target, context => Dispatch(target, context, selector), "StatusLifecycle");
    }

    private static void After(
        ModConfig config,
        string target,
        Func<TerriasStatusLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        TerriasHookRegistry.AfterRouted(config, target, context => Dispatch(target, context, selector), "StatusLifecycle");
    }

    private static void Dispatch(
        string target,
        ModHookContext context,
        Func<TerriasStatusLifecycleSubscription, Action<ModHookContext>?> selector)
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
                TerriasLog.Error("Status lifecycle handler failed: " + pair.Key + " @ " + target, ex);
            }
        }
    }

    private static KeyValuePair<string, TerriasStatusLifecycleSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, TerriasStatusLifecycleSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
