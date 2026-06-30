using System;
using System.Collections.Generic;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedHooks
{
    private static readonly object RoutedGate = new();
    private static readonly Dictionary<string, RoutedHook> RoutedHooks = new(StringComparer.Ordinal);

    public static bool RegisterBefore(
        ModConfig? config,
        string target,
        Action<ModHookContext> action,
        Action<string>? info = null,
        Action<string>? warn = null,
        bool safeInvoke = false)
    {
        if (config == null || string.IsNullOrWhiteSpace(target))
        {
            warn?.Invoke("Hook before skipped: target is empty");
            return false;
        }

        try
        {
            config.AddMethodHookBefore(target, safeInvoke ? context => SafeInvoke(action, context, target, warn) : action);
            info?.Invoke("Hook before registered: " + target);
            return true;
        }
        catch (Exception ex)
        {
            warn?.Invoke("Hook before failed: " + target + " -> " + ex.Message);
            return false;
        }
    }

    public static bool RegisterAfter(
        ModConfig? config,
        string target,
        Action<ModHookContext> action,
        Action<string>? info = null,
        Action<string>? warn = null,
        bool safeInvoke = false)
    {
        if (config == null || string.IsNullOrWhiteSpace(target))
        {
            warn?.Invoke("Hook after skipped: target is empty");
            return false;
        }

        try
        {
            config.AddMethodHookAfter(target, safeInvoke ? context => SafeInvoke(action, context, target, warn) : action);
            info?.Invoke("Hook after registered: " + target);
            return true;
        }
        catch (Exception ex)
        {
            warn?.Invoke("Hook after failed: " + target + " -> " + ex.Message);
            return false;
        }
    }

    public static IDisposable RegisterBeforeRouted(
        ModConfig? config,
        string target,
        Action<ModHookContext> action,
        Action<string>? info = null,
        Action<string>? warn = null,
        bool safeInvoke = false)
    {
        return RegisterRouted(config, target, action, info, warn, safeInvoke, before: true);
    }

    public static IDisposable RegisterAfterRouted(
        ModConfig? config,
        string target,
        Action<ModHookContext> action,
        Action<string>? info = null,
        Action<string>? warn = null,
        bool safeInvoke = false)
    {
        return RegisterRouted(config, target, action, info, warn, safeInvoke, before: false);
    }

    private static IDisposable RegisterRouted(
        ModConfig? config,
        string target,
        Action<ModHookContext> action,
        Action<string>? info,
        Action<string>? warn,
        bool safeInvoke,
        bool before)
    {
        if (config == null || string.IsNullOrWhiteSpace(target) || action == null)
        {
            warn?.Invoke("Routed hook skipped: target is empty");
            return EmptyDisposable.Instance;
        }

        var key = RoutedKey(target, before);
        RoutedHook hook;
        lock (RoutedGate)
        {
            if (!RoutedHooks.TryGetValue(key, out hook))
            {
                hook = new RoutedHook(target.Trim(), before);
                RoutedHooks[key] = hook;
            }
        }

        if (!hook.EnsureRegistered(config, info, warn))
        {
            return EmptyDisposable.Instance;
        }

        var callback = safeInvoke ? context => SafeInvoke(action, context, target, warn) : action;
        return hook.Add(callback);
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
        private Action<ModHookContext>[] subscribers = Array.Empty<Action<ModHookContext>>();
        private bool registered;

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

        public IDisposable Add(Action<ModHookContext> subscriber)
        {
            lock (gate)
            {
                for (var i = 0; i < subscribers.Length; i++)
                {
                    if (ReferenceEquals(subscribers[i], subscriber))
                    {
                        return new Subscription(this, subscriber);
                    }
                }

                var next = new Action<ModHookContext>[subscribers.Length + 1];
                Array.Copy(subscribers, next, subscribers.Length);
                next[next.Length - 1] = subscriber;
                subscribers = next;
            }

            return new Subscription(this, subscriber);
        }

        private void Remove(Action<ModHookContext> subscriber)
        {
            lock (gate)
            {
                var index = -1;
                for (var i = 0; i < subscribers.Length; i++)
                {
                    if (ReferenceEquals(subscribers[i], subscriber))
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    return;
                }

                if (subscribers.Length == 1)
                {
                    subscribers = Array.Empty<Action<ModHookContext>>();
                    return;
                }

                var next = new Action<ModHookContext>[subscribers.Length - 1];
                if (index > 0)
                {
                    Array.Copy(subscribers, 0, next, 0, index);
                }

                if (index < subscribers.Length - 1)
                {
                    Array.Copy(subscribers, index + 1, next, index, subscribers.Length - index - 1);
                }

                subscribers = next;
            }
        }

        private void Dispatch(ModHookContext context)
        {
            var snapshot = subscribers;
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i](context);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private RoutedHook? owner;
            private Action<ModHookContext>? subscriber;

            public Subscription(RoutedHook owner, Action<ModHookContext> subscriber)
            {
                this.owner = owner;
                this.subscriber = subscriber;
            }

            public void Dispose()
            {
                var currentOwner = owner;
                var currentSubscriber = subscriber;
                if (currentOwner == null || currentSubscriber == null)
                {
                    return;
                }

                owner = null;
                subscriber = null;
                currentOwner.Remove(currentSubscriber);
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
