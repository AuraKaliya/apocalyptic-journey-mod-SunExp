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
                FightInitializing = subscription.FightInitializing,
                FightInitialized = subscription.FightInitialized,
                FightOpening = subscription.FightOpening,
                FightStarted = subscription.FightStarted,
                PlayerRoundStarted = subscription.PlayerRoundStarted,
                FightRestarting = subscription.FightRestarting,
                FightRestarted = subscription.FightRestarted,
                FightEnding = subscription.FightEnding,
                FightEnded = subscription.FightEnded
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
