using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasBattleLifecycleSubscription
{
    public Action<ModHookContext>? AdventureStarting { get; set; }

    public Action<ModHookContext>? FightInitializing { get; set; }

    public Action<ModHookContext>? FightInitialized { get; set; }

    public Action<ModHookContext>? FightOpening { get; set; }

    public Action<ModHookContext>? FightStarted { get; set; }

    public Action<ModHookContext>? PlayerRoundStarted { get; set; }

    public Action<ModHookContext>? FightRestarting { get; set; }

    public Action<ModHookContext>? FightRestarted { get; set; }

    public Action<ModHookContext>? FightEnding { get; set; }

    public Action<ModHookContext>? FightEnded { get; set; }
}

public static class TerriasBattleLifecycleRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, TerriasBattleLifecycleSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, TerriasBattleLifecycleSubscription>[]? cachedSubscriptions;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraBattleLifecycleRouter.Register(
            modConfig,
            TerriasIds.ModId,
            "BattleLifecycle",
            new AuraBattleLifecycleSubscription
            {
                AdventureStarting = context => DispatchAdventureStarting(context, AuraBattleLifecycleRouter.GameEntryStartGame),
                FightInitializing = context => DispatchFightInitializing(context, AuraBattleLifecycleRouter.FightInitInit),
                FightInitialized = context => DispatchFightInitialized(context, AuraBattleLifecycleRouter.FightInitInit),
                FightOpening = context => DispatchFightOpening(context, AuraBattleLifecycleRouter.FightStartInit),
                FightStarted = context => DispatchFightStarted(context, "Fight lifecycle start"),
                PlayerRoundStarted = context => DispatchPlayerRoundStarted(context, AuraBattleLifecycleRouter.FightPlayerTurnInit),
                FightRestarting = context => DispatchFightRestarting(context, AuraBattleLifecycleRouter.FightManagerClearFightUi),
                FightRestarted = context => DispatchFightRestarted(context, "Fight lifecycle restarted"),
                FightEnding = context => DispatchFightEnding(context, "Fight lifecycle ending"),
                FightEnded = context => DispatchFightEnded(context, "Fight lifecycle ended")
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
    }

    public static void Register(string id, TerriasBattleLifecycleSubscription subscription)
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

        TerriasPerformanceCounters.Record("BattleLifecycle.HandlerRegistered");
    }

    private static void DispatchAdventureStarting(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.AdventureStarting);
    }

    private static void DispatchFightInitializing(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightInitializing);
    }

    private static void DispatchFightInitialized(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightInitialized);
    }

    private static void DispatchFightOpening(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightOpening);
    }

    private static void DispatchFightStarted(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightStarted);
    }

    private static void DispatchPlayerRoundStarted(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.PlayerRoundStarted);
    }

    private static void DispatchFightRestarting(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightRestarting);
    }

    private static void DispatchFightRestarted(ModHookContext context, string source)
    {
        Dispatch(context, source, subscription => subscription.FightRestarted);
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
        Func<TerriasBattleLifecycleSubscription, Action<ModHookContext>?> selector)
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
                TerriasLog.Error("Battle lifecycle handler failed: " + pair.Key + " @ " + source, ex);
            }
        }
    }

    private static KeyValuePair<string, TerriasBattleLifecycleSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, TerriasBattleLifecycleSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
