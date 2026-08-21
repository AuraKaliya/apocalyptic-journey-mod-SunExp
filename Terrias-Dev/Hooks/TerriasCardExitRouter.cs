using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasCardExitSubscription
{
    public int Priority { get; set; }
    public Action<ModHookContext>? BeforeBurn { get; set; }
    public Action<ModHookContext>? AfterBurn { get; set; }
    public Action<ModHookContext>? BeforeThrow { get; set; }
    public Action<ModHookContext>? AfterThrow { get; set; }
}

public static class TerriasCardExitRouter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, TerriasCardExitSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static Handler[] beforeBurn = Array.Empty<Handler>();
    private static Handler[] afterBurn = Array.Empty<Handler>();
    private static Handler[] beforeThrow = Array.Empty<Handler>();
    private static Handler[] afterThrow = Array.Empty<Handler>();
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        TerriasHookRegistry.BeforeRouted(modConfig, TerriasHookTargets.CardItemEffectOfBurnCard,
            context => Dispatch(beforeBurn, context, "BeforeBurn", s => s.BeforeBurn), "CardExit");
        TerriasHookRegistry.AfterRouted(modConfig, TerriasHookTargets.CardItemEffectOfBurnCard,
            context => Dispatch(afterBurn, context, "AfterBurn", s => s.AfterBurn), "CardExit");
        TerriasHookRegistry.BeforeRouted(modConfig, TerriasHookTargets.CardItemEffectOfThrowCard,
            context => Dispatch(beforeThrow, context, "BeforeThrow", s => s.BeforeThrow), "CardExit");
        TerriasHookRegistry.AfterRouted(modConfig, TerriasHookTargets.CardItemEffectOfThrowCard,
            context => Dispatch(afterThrow, context, "AfterThrow", s => s.AfterThrow), "CardExit");
    }

    public static IDisposable Register(string id, TerriasCardExitSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null) return EmptyDisposable.Instance;
        var normalized = id.Trim();
        lock (Gate)
        {
            Subscriptions[normalized] = subscription;
            RebuildNoLock();
        }
        return new Registration(normalized, subscription);
    }

    private static void Dispatch(
        Handler[] snapshot,
        ModHookContext context,
        string phase,
        Func<TerriasCardExitSubscription, Action<ModHookContext>?> selector)
    {
        for (var i = 0; i < snapshot.Length; i++)
        {
            var action = selector(snapshot[i].Subscription);
            if (action != null) snapshot[i].Invoke(action, context, phase);
        }
    }

    private static void RebuildNoLock()
    {
        beforeBurn = Build(s => s.BeforeBurn);
        afterBurn = Build(s => s.AfterBurn);
        beforeThrow = Build(s => s.BeforeThrow);
        afterThrow = Build(s => s.AfterThrow);
    }

    private static Handler[] Build(Func<TerriasCardExitSubscription, Action<ModHookContext>?> selector)
    {
        return Subscriptions
            .Where(pair => selector(pair.Value) != null)
            .OrderByDescending(pair => pair.Value.Priority)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new Handler(pair.Key, pair.Value))
            .ToArray();
    }

    private readonly struct Handler
    {
        private readonly string id;
        public Handler(string id, TerriasCardExitSubscription subscription) { this.id = id; Subscription = subscription; }
        public TerriasCardExitSubscription Subscription { get; }
        public void Invoke(Action<ModHookContext> action, ModHookContext context, string phase)
        {
            try { action(context); }
            catch (Exception ex) { TerriasLog.Error("Card exit handler failed: " + id + " @ " + phase, ex); }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly string id;
        private readonly TerriasCardExitSubscription subscription;
        private bool disposed;
        public Registration(string id, TerriasCardExitSubscription subscription) { this.id = id; this.subscription = subscription; }
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
