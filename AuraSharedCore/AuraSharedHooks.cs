using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public sealed class AuraRoutedHookRequest
{
    public string OwnerModId { get; set; } = "";

    public string HandlerId { get; set; } = "";

    public int Priority { get; set; }

    public bool SafeInvoke { get; set; } = true;

    public Action<ModHookContext>? Handler { get; set; }
}

public static class AuraSharedHooks
{
    private static readonly object RoutedGate = new();
    private static readonly Dictionary<string, RoutedHook> RoutedHooks = new(StringComparer.Ordinal);

    public static IDisposable RegisterBeforeRouted(
        ModConfig? config,
        string target,
        AuraRoutedHookRequest request,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        return RegisterRouted(config, target, request, info, warn, before: true);
    }

    public static IDisposable RegisterAfterRouted(
        ModConfig? config,
        string target,
        AuraRoutedHookRequest request,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        return RegisterRouted(config, target, request, info, warn, before: false);
    }

    private static IDisposable RegisterRouted(
        ModConfig? config,
        string target,
        AuraRoutedHookRequest? request,
        Action<string>? info,
        Action<string>? warn,
        bool before)
    {
        if (config == null
            || string.IsNullOrWhiteSpace(target)
            || request?.Handler == null
            || string.IsNullOrWhiteSpace(request.OwnerModId)
            || string.IsNullOrWhiteSpace(request.HandlerId))
        {
            warn?.Invoke("Routed hook skipped: target, owner, handler id and callback are required");
            return EmptyDisposable.Instance;
        }

        var key = RoutedKey(target, before);
        RoutedHook hook;
        lock (RoutedGate)
        {
            if (!RoutedHooks.TryGetValue(key, out var existing))
            {
                hook = new RoutedHook(target.Trim(), before);
                RoutedHooks[key] = hook;
            }
            else
            {
                hook = existing;
            }
        }

        if (!hook.EnsureRegistered(config, info, warn))
        {
            return EmptyDisposable.Instance;
        }

        var handler = request.Handler;
        var callback = request.SafeInvoke
            ? context => SafeInvoke(handler, context, target, warn)
            : handler;
        var subscriberId = request.OwnerModId.Trim() + ":" + request.HandlerId.Trim();
        return hook.Add(
            subscriberId,
            request.Priority,
            handler,
            callback,
            warn);
    }

    public static bool RunStep(string name, Action action, Action<string, Exception>? onError = null)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(name, ex);
            return false;
        }
    }

    public static bool SafeInvoke(Action action, Action<Exception>? onError = null)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return false;
        }
    }

    public static bool SafeInvoke(Action<ModHookContext> action, ModHookContext context, string source, Action<string>? warn = null)
    {
        try
        {
            action(context);
            return true;
        }
        catch (Exception ex)
        {
            warn?.Invoke("Hook action failed: " + source + " -> " + ex.Message);
            return false;
        }
    }

    private static string RoutedKey(string target, bool before)
    {
        return (before ? "before:" : "after:") + target.Trim();
    }

    private sealed class RoutedHook
    {
        private readonly object gate = new();
        private readonly string target;
        private readonly bool before;
        private readonly Action<ModHookContext> dispatcher;
        private readonly Dictionary<string, Subscriber> subscribers = new(StringComparer.Ordinal);
        private Subscriber[] snapshot = Array.Empty<Subscriber>();
        private bool registered;
        private long generation;

        public RoutedHook(string target, bool before)
        {
            this.target = target;
            this.before = before;
            dispatcher = Dispatch;
        }

        public bool EnsureRegistered(ModConfig config, Action<string>? info, Action<string>? warn)
        {
            lock (gate)
            {
                if (registered)
                {
                    return true;
                }

                try
                {
                    if (before)
                    {
                        config.AddMethodHookBefore(target, dispatcher);
                    }
                    else
                    {
                        config.AddMethodHookAfter(target, dispatcher);
                    }

                    registered = true;
                    info?.Invoke("Routed hook " + (before ? "before" : "after") + " registered: " + target);
                    return true;
                }
                catch (Exception ex)
                {
                    warn?.Invoke("Routed hook " + (before ? "before" : "after") + " failed: " + target + " -> " + ex.Message);
                    return false;
                }
            }
        }

        public IDisposable Add(
            string subscriberId,
            int priority,
            Action<ModHookContext> handler,
            Action<ModHookContext> callback,
            Action<string>? warn)
        {
            lock (gate)
            {
                if (subscribers.TryGetValue(subscriberId, out var existing))
                {
                    if (!existing.Handler.Equals(handler)
                        || existing.Priority != priority)
                    {
                        warn?.Invoke("Routed hook identity conflict: target="
                                     + target
                                     + ", phase=" + (before ? "before" : "after")
                                     + ", subscriber=" + subscriberId);
                        return EmptyDisposable.Instance;
                    }

                    existing.LeaseCount++;
                    return new Subscription(this, subscriberId, existing.Generation);
                }

                var subscriber = new Subscriber(
                    subscriberId,
                    priority,
                    ++generation,
                    handler,
                    callback);
                subscribers[subscriberId] = subscriber;
                RebuildSnapshotNoLock();
                return new Subscription(this, subscriberId, subscriber.Generation);
            }
        }

        private void Remove(string subscriberId, long subscriberGeneration)
        {
            lock (gate)
            {
                if (!subscribers.TryGetValue(subscriberId, out var subscriber)
                    || subscriber.Generation != subscriberGeneration)
                {
                    return;
                }

                subscriber.LeaseCount--;
                if (subscriber.LeaseCount > 0)
                {
                    return;
                }

                subscribers.Remove(subscriberId);
                RebuildSnapshotNoLock();
            }
        }

        private void RebuildSnapshotNoLock()
        {
            Volatile.Write(
                ref snapshot,
                subscribers.Values
                    .OrderByDescending(value => value.Priority)
                    .ThenBy(value => value.Id, StringComparer.Ordinal)
                    .ToArray());
        }

        private void Dispatch(ModHookContext context)
        {
            var current = Volatile.Read(ref snapshot);
            for (var i = 0; i < current.Length; i++)
            {
                current[i].Callback(context);
            }
        }

        private sealed class Subscriber
        {
            public Subscriber(
                string id,
                int priority,
                long generation,
                Action<ModHookContext> handler,
                Action<ModHookContext> callback)
            {
                Id = id;
                Priority = priority;
                Generation = generation;
                Handler = handler;
                Callback = callback;
                LeaseCount = 1;
            }

            public string Id { get; }

            public int Priority { get; }

            public long Generation { get; }

            public Action<ModHookContext> Handler { get; }

            public Action<ModHookContext> Callback { get; }

            public int LeaseCount { get; set; }
        }

        private sealed class Subscription : IDisposable
        {
            private RoutedHook? owner;
            private string subscriberId;
            private readonly long generation;

            public Subscription(RoutedHook owner, string subscriberId, long generation)
            {
                this.owner = owner;
                this.subscriberId = subscriberId;
                this.generation = generation;
            }

            public void Dispose()
            {
                var currentOwner = owner;
                if (currentOwner == null || subscriberId.Length == 0)
                {
                    return;
                }

                owner = null;
                var currentSubscriberId = subscriberId;
                subscriberId = "";
                currentOwner.Remove(currentSubscriberId, generation);
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
