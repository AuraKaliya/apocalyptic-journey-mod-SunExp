using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasScriptExecutionSubscription
{
    public int Priority { get; set; }
    public Action<ModHookContext>? BeforeRunScript { get; set; }
    public Action<ModHookContext>? AfterSetStatus { get; set; }
}

public static class TerriasScriptExecutionRouter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, TerriasScriptExecutionSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static Handler[] beforeRunScript = Array.Empty<Handler>();
    private static Handler[] afterSetStatus = Array.Empty<Handler>();
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        TerriasHookRegistry.BeforeRouted(modConfig, TerriasHookTargets.ScriptExecutorRunScript,
            context => Dispatch(beforeRunScript, context, "BeforeRunScript", s => s.BeforeRunScript), "ScriptExecution");
        TerriasHookRegistry.AfterRouted(modConfig, "ScriptExecutor.SetStatus",
            context => Dispatch(afterSetStatus, context, "AfterSetStatus", s => s.AfterSetStatus), "ScriptExecution");
    }

    public static IDisposable Register(string id, TerriasScriptExecutionSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null) return EmptyDisposable.Instance;
        var normalized = id.Trim();
        lock (Gate)
        {
            Subscriptions[normalized] = subscription;
            beforeRunScript = Build(s => s.BeforeRunScript);
            afterSetStatus = Build(s => s.AfterSetStatus);
        }
        return new Registration(normalized, subscription);
    }

    private static Handler[] Build(Func<TerriasScriptExecutionSubscription, Action<ModHookContext>?> selector)
    {
        return Subscriptions.Where(pair => selector(pair.Value) != null)
            .OrderByDescending(pair => pair.Value.Priority)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new Handler(pair.Key, pair.Value))
            .ToArray();
    }

    private static void Dispatch(Handler[] snapshot, ModHookContext context, string phase,
        Func<TerriasScriptExecutionSubscription, Action<ModHookContext>?> selector)
    {
        for (var i = 0; i < snapshot.Length; i++)
        {
            var action = selector(snapshot[i].Subscription);
            if (action != null) snapshot[i].Invoke(action, context, phase);
        }
    }

    private readonly struct Handler
    {
        private readonly string id;
        public Handler(string id, TerriasScriptExecutionSubscription subscription) { this.id = id; Subscription = subscription; }
        public TerriasScriptExecutionSubscription Subscription { get; }
        public void Invoke(Action<ModHookContext> action, ModHookContext context, string phase)
        {
            try { action(context); }
            catch (Exception ex) { TerriasLog.Error("Script execution handler failed: " + id + " @ " + phase, ex); }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly string id;
        private readonly TerriasScriptExecutionSubscription subscription;
        private bool disposed;
        public Registration(string id, TerriasScriptExecutionSubscription subscription) { this.id = id; this.subscription = subscription; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
            {
                if (Subscriptions.TryGetValue(id, out var current) && ReferenceEquals(current, subscription))
                {
                    Subscriptions.Remove(id);
                    beforeRunScript = Build(s => s.BeforeRunScript);
                    afterSetStatus = Build(s => s.AfterSetStatus);
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
