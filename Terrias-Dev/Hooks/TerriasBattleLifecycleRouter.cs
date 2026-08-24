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
    public Action<ModHookContext>? BattleInitializing { get; set; }
    public Action<ModHookContext>? BattleManagerInitialized { get; set; }
    public Action<ModHookContext>? BattleMaterialized { get; set; }
    public Action<ModHookContext>? BattleOpening { get; set; }
    public Action<ModHookContext>? FightStartSignaled { get; set; }
    public Action<ModHookContext>? BattleReady { get; set; }
    public Action<ModHookContext>? ActionLoopStarting { get; set; }
    public Action<ModHookContext>? PlayerTurnEntering { get; set; }
    public Action<ModHookContext>? PlayerRoundStarting { get; set; }
    public Action<ModHookContext>? PlayerRoundReady { get; set; }
    public Action<ModHookContext>? PlayerTurnCompleted { get; set; }
    public Action<ModHookContext>? BattleRestarting { get; set; }
    public Action<ModHookContext>? BattleRestarted { get; set; }
    public Action<AuraBattleOutcomeContext>? OutcomeEntering { get; set; }
    public Action<AuraBattleOutcomeContext>? OutcomeSettling { get; set; }
    public Action<ModHookContext>? BattleSettling { get; set; }
    public Action<AuraBattleOutcomeContext>? OutcomeEnded { get; set; }
    public Action<ModHookContext>? BattleEnded { get; set; }
}

public static class TerriasBattleLifecycleRouter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, TerriasBattleLifecycleSubscription> Pending = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IDisposable> Registrations = new(StringComparer.Ordinal);
    private static ModConfig? activeConfig;

    public static void Initialize(ModConfig modConfig)
    {
        lock (Gate)
        {
            activeConfig = modConfig;
            foreach (var pair in Pending)
            {
                RegisterNoLock(pair.Key, pair.Value);
            }
        }
    }

    public static IDisposable Register(string id, TerriasBattleLifecycleSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null)
        {
            return EmptyDisposable.Instance;
        }

        lock (Gate)
        {
            var normalized = id.Trim();
            Pending[normalized] = subscription;
            if (activeConfig != null)
            {
                RegisterNoLock(normalized, subscription);
            }

            TerriasPerformanceCounters.Record("BattleLifecycle.HandlerRegistered");
            return new RegistrationHandle(normalized);
        }
    }

    private static void RegisterNoLock(string id, TerriasBattleLifecycleSubscription subscription)
    {
        if (activeConfig == null)
        {
            return;
        }

        if (Registrations.TryGetValue(id, out var previous))
        {
            previous.Dispose();
        }

        Registrations[id] = AuraBattleLifecycleRouter.Register(
            activeConfig,
            TerriasIds.ModId,
            id,
            new AuraBattleLifecycleSubscription
            {
                AdventureStarting = subscription.AdventureStarting,
                BattleInitializing = subscription.BattleInitializing,
                BattleManagerInitialized = subscription.BattleManagerInitialized,
                BattleMaterialized = subscription.BattleMaterialized,
                BattleOpening = subscription.BattleOpening,
                FightStartSignaled = subscription.FightStartSignaled,
                BattleReady = subscription.BattleReady,
                ActionLoopStarting = subscription.ActionLoopStarting,
                PlayerTurnEntering = subscription.PlayerTurnEntering,
                PlayerRoundStarting = subscription.PlayerRoundStarting,
                PlayerRoundReady = subscription.PlayerRoundReady,
                PlayerTurnCompleted = subscription.PlayerTurnCompleted,
                BattleRestarting = subscription.BattleRestarting,
                BattleRestarted = subscription.BattleRestarted,
                OutcomeEntering = subscription.OutcomeEntering,
                BattleSettling = context =>
                {
                    subscription.OutcomeSettling?.Invoke(context);
                    subscription.BattleSettling?.Invoke(context.NativeContext);
                },
                BattleEnded = context =>
                {
                    subscription.OutcomeEnded?.Invoke(context);
                    subscription.BattleEnded?.Invoke(context.NativeContext);
                }
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
    }

    private static void Unregister(string id)
    {
        lock (Gate)
        {
            Pending.Remove(id);
            if (Registrations.TryGetValue(id, out var registration))
            {
                Registrations.Remove(id);
                registration.Dispose();
            }
        }
    }

    private sealed class RegistrationHandle : IDisposable
    {
        private string? id;

        public RegistrationHandle(string id)
        {
            this.id = id;
        }

        public void Dispose()
        {
            var current = id;
            if (current == null)
            {
                return;
            }

            id = null;
            Unregister(current);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
