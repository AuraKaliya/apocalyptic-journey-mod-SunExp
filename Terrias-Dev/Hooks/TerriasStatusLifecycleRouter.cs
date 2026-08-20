using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasStatusLifecycleSubscription
{
    public int Priority { get; set; }
    public Action<ModHookContext>? BeforeHit { get; set; }
    public Action<ModHookContext>? AfterHit { get; set; }
    public Action<ModHookContext>? BeforeEnemyDead { get; set; }
    public Action<ModHookContext>? AfterEnemyDead { get; set; }
    public Action<ModHookContext>? AfterCurHpChanged { get; set; }
    public Action<ModHookContext>? AfterMaxHpChanged { get; set; }
    public Action<ModHookContext>? AfterStateChanged { get; set; }
    public Action<ModHookContext>? AfterEnemyInit { get; set; }
    public Action<ModHookContext>? AfterInitAnimator { get; set; }
    public Action<ModHookContext>? AfterSetSprite { get; set; }
    public Action<ModHookContext>? AfterFightUiFadeIn { get; set; }
}

public static class TerriasStatusLifecycleRouter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, TerriasStatusLifecycleSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static PhaseHandler[] beforeHit = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterHit = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] beforeEnemyDead = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterEnemyDead = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterCurHpChanged = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterMaxHpChanged = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterStateChanged = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterEnemyInit = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterInitAnimator = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterSetSprite = Array.Empty<PhaseHandler>();
    private static PhaseHandler[] afterFightUiFadeIn = Array.Empty<PhaseHandler>();
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        Before(modConfig, TerriasHookTargets.StatusManagerHit, () => beforeHit);
        After(modConfig, TerriasHookTargets.StatusManagerHit, () => afterHit);
        Before(modConfig, TerriasHookTargets.StatusManagerEnemyDead, () => beforeEnemyDead);
        After(modConfig, TerriasHookTargets.StatusManagerEnemyDead, () => afterEnemyDead);
        After(modConfig, TerriasHookTargets.StatusManagerSetCurHp, () => afterCurHpChanged);
        After(modConfig, TerriasHookTargets.StatusManagerSetMaxHp, () => afterMaxHpChanged);
        After(modConfig, TerriasHookTargets.StatusManagerSetState, () => afterStateChanged);
        After(modConfig, TerriasHookTargets.EnemyInit, () => afterEnemyInit);
        After(modConfig, TerriasHookTargets.StatusManagerInitAnimator, () => afterInitAnimator);
        After(modConfig, TerriasHookTargets.StatusManagerSetSprite, () => afterSetSprite);
        After(modConfig, TerriasHookTargets.FightUiFadeIn, () => afterFightUiFadeIn);
    }

    public static IDisposable Register(string id, TerriasStatusLifecycleSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null) return EmptyDisposable.Instance;
        var normalized = id.Trim();
        lock (Gate)
        {
            Subscriptions[normalized] = subscription;
            RebuildNoLock();
        }

        TerriasPerformanceCounters.Record("StatusLifecycle.HandlerRegistered");
        return new Registration(normalized, subscription);
    }

    private static void Before(ModConfig config, string target, Func<PhaseHandler[]> handlers)
    {
        TerriasHookRegistry.BeforeRouted(config, target, context => Dispatch(target, context, handlers()), "StatusLifecycle");
    }

    private static void After(ModConfig config, string target, Func<PhaseHandler[]> handlers)
    {
        TerriasHookRegistry.AfterRouted(config, target, context => Dispatch(target, context, handlers()), "StatusLifecycle");
    }

    private static void Dispatch(string target, ModHookContext context, PhaseHandler[] handlers)
    {
        for (var i = 0; i < handlers.Length; i++) handlers[i].Invoke(context, target);
    }

    private static void RebuildNoLock()
    {
        beforeHit = Build(value => value.BeforeHit);
        afterHit = Build(value => value.AfterHit);
        beforeEnemyDead = Build(value => value.BeforeEnemyDead);
        afterEnemyDead = Build(value => value.AfterEnemyDead);
        afterCurHpChanged = Build(value => value.AfterCurHpChanged);
        afterMaxHpChanged = Build(value => value.AfterMaxHpChanged);
        afterStateChanged = Build(value => value.AfterStateChanged);
        afterEnemyInit = Build(value => value.AfterEnemyInit);
        afterInitAnimator = Build(value => value.AfterInitAnimator);
        afterSetSprite = Build(value => value.AfterSetSprite);
        afterFightUiFadeIn = Build(value => value.AfterFightUiFadeIn);
    }

    private static PhaseHandler[] Build(Func<TerriasStatusLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        return Subscriptions
            .Select(pair => new { pair.Key, Subscription = pair.Value, Action = selector(pair.Value) })
            .Where(item => item.Action != null)
            .OrderByDescending(item => item.Subscription.Priority)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PhaseHandler(item.Key, item.Action!))
            .ToArray();
    }

    private readonly struct PhaseHandler
    {
        private readonly string id;
        private readonly Action<ModHookContext> action;

        public PhaseHandler(string id, Action<ModHookContext> action)
        {
            this.id = id;
            this.action = action;
        }

        public void Invoke(ModHookContext context, string target)
        {
            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                TerriasLog.Error("Status lifecycle handler failed: " + id + " @ " + target, ex);
            }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly string id;
        private readonly TerriasStatusLifecycleSubscription subscription;
        private bool disposed;

        public Registration(string id, TerriasStatusLifecycleSubscription subscription)
        {
            this.id = id;
            this.subscription = subscription;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
            {
                if (Subscriptions.TryGetValue(id, out var current) && ReferenceEquals(current, subscription))
                {
                    Subscriptions.Remove(id);
                    RebuildNoLock();
                }
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
