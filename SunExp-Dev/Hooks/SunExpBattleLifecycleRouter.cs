using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public sealed class SunExpBattleLifecycleSubscription
{
    public Action<ModHookContext>? AdventureStarting { get; set; }

    public Action<ModHookContext>? FightStarted { get; set; }

    public Action<ModHookContext>? FightEnding { get; set; }

    public Action<ModHookContext>? FightEnded { get; set; }
}

public static class SunExpBattleLifecycleRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, SunExpBattleLifecycleSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, SunExpBattleLifecycleSubscription>[]? cachedSubscriptions;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        SunExpHookRegistry.BeforeRouted(modConfig, SunExpHookTargets.GameEntryStartGame, context => DispatchAdventureStarting(context, SunExpHookTargets.GameEntryStartGame), "BattleLifecycle");

        SunExpHookRegistry.AfterRouted(modConfig, SunExpHookTargets.FightStartInit, context => DispatchFightStarted(context, SunExpHookTargets.FightStartInit), "BattleLifecycle");
        SunExpHookRegistry.AfterRouted(modConfig, SunExpHookTargets.FightInitInit, context => DispatchFightStarted(context, SunExpHookTargets.FightInitInit), "BattleLifecycle");

        SunExpHookRegistry.BeforeRouted(modConfig, SunExpHookTargets.FightWinResetStates, context => DispatchFightEnding(context, SunExpHookTargets.FightWinResetStates), "BattleLifecycle");
        SunExpHookRegistry.BeforeRouted(modConfig, SunExpHookTargets.FightEscapeResetStates, context => DispatchFightEnding(context, SunExpHookTargets.FightEscapeResetStates), "BattleLifecycle");
        SunExpHookRegistry.BeforeRouted(modConfig, SunExpHookTargets.FightLossInit, context => DispatchFightEnding(context, SunExpHookTargets.FightLossInit), "BattleLifecycle");

        SunExpHookRegistry.AfterRouted(modConfig, SunExpHookTargets.FightWinResetStates, context => DispatchFightEnded(context, SunExpHookTargets.FightWinResetStates), "BattleLifecycle");
        SunExpHookRegistry.AfterRouted(modConfig, SunExpHookTargets.FightEscapeResetStates, context => DispatchFightEnded(context, SunExpHookTargets.FightEscapeResetStates), "BattleLifecycle");
        SunExpHookRegistry.AfterRouted(modConfig, SunExpHookTargets.FightLossInit, context => DispatchFightEnded(context, SunExpHookTargets.FightLossInit), "BattleLifecycle");
    }

    public static void Register(string id, SunExpBattleLifecycleSubscription subscription)
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

        SunExpPerformanceCounters.Record("BattleLifecycle.HandlerRegistered");
    }

    private static void DispatchAdventureStarting(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.AdventureStarting);
    }

    private static void DispatchFightStarted(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightStarted);
    }

    private static void DispatchFightEnding(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightEnding);
    }

    private static void DispatchFightEnded(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightEnded);
    }

    private static void Dispatch(
        ModHookContext context,
        string source,
        Func<SunExpBattleLifecycleSubscription, Action<ModHookContext>?> selector)
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
                SunExpLog.Error("Battle lifecycle handler failed: " + pair.Key + " @ " + source, ex);
            }
        }
    }

    private static KeyValuePair<string, SunExpBattleLifecycleSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, SunExpBattleLifecycleSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
