using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsHookRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, OwnedRegistration> Owned =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> OwnerStates =
        new(StringComparer.Ordinal);

    public static IDisposable Before(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return RegisterOwned(config, target, action, owner, before: true);
    }

    public static IDisposable After(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return RegisterOwned(config, target, action, owner, before: false);
    }

    public static IDisposable BeforeRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return RegisterDirect(config, target, action, owner, before: true);
    }

    public static IDisposable AfterRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return RegisterDirect(config, target, action, owner, before: false);
    }

    public static void SetOwnerActive(string owner, bool active)
    {
        var normalized = NormalizeOwner(owner);
        OwnedRegistration[] registrations;
        lock (Gate)
        {
            OwnerStates[normalized] = active;
            registrations = Owned.Values
                .Where(value => string.Equals(
                    value.Owner,
                    normalized,
                    StringComparison.Ordinal))
                .ToArray();
        }

        for (var i = 0; i < registrations.Length; i++)
        {
            if (active) registrations[i].Activate();
            else registrations[i].Deactivate();
        }
    }

    private static IDisposable RegisterOwned(
        ModConfig config,
        string target,
        Action<ModHookContext> action,
        string owner,
        bool before)
    {
        var normalizedOwner = NormalizeOwner(owner);
        var request = Request(target, action, normalizedOwner);
        var key = (before ? "before:" : "after:")
                  + target.Trim()
                  + ":"
                  + request.HandlerId;
        OwnedRegistration registration;
        lock (Gate)
        {
            if (Owned.TryGetValue(key, out registration!))
            {
                if (!registration.Matches(action))
                {
                    AuraToolsLog.Warn(
                        Prefix(normalizedOwner)
                        + "Owned hook identity conflict: " + key);
                    return EmptyDisposable.Instance;
                }
                registration.AddLease();
                return new OwnedLease(key, registration.Generation);
            }

            registration = new OwnedRegistration(
                key,
                normalizedOwner,
                config,
                target,
                request,
                before);
            Owned[key] = registration;
            if (!OwnerStates.TryGetValue(normalizedOwner, out var active)
                || active)
            {
                registration.Activate();
            }
        }

        return new OwnedLease(key, registration.Generation);
    }

    private static IDisposable RegisterDirect(
        ModConfig config,
        string target,
        Action<ModHookContext> action,
        string owner,
        bool before)
    {
        var request = Request(target, action, NormalizeOwner(owner));
        return before
            ? AuraSharedHooks.RegisterBeforeRouted(
                config,
                target,
                request,
                AuraToolsLog.Info,
                message => AuraToolsLog.Warn(Prefix(owner) + message))
            : AuraSharedHooks.RegisterAfterRouted(
                config,
                target,
                request,
                AuraToolsLog.Info,
                message => AuraToolsLog.Warn(Prefix(owner) + message));
    }

    private static AuraRoutedHookRequest Request(
        string target,
        Action<ModHookContext> action,
        string owner)
    {
        var method = action.Method;
        return new AuraRoutedHookRequest
        {
            OwnerModId = AuraToolsIds.ModId,
            HandlerId = owner
                        + ":" + (target ?? "")
                        + ":"
                        + (method.DeclaringType?.FullName ?? "anonymous")
                        + "."
                        + method.Name,
            Handler = action,
            SafeInvoke = true
        };
    }

    private static string Prefix(string owner)
    {
        return string.IsNullOrWhiteSpace(owner) ? "" : "[" + owner.Trim() + "] ";
    }

    private static string NormalizeOwner(string owner)
    {
        return string.IsNullOrWhiteSpace(owner)
            ? "AuraTools"
            : owner.Trim();
    }

    private sealed class OwnedRegistration
    {
        private static long nextGeneration;
        private readonly ModConfig config;
        private readonly string target;
        private readonly AuraRoutedHookRequest request;
        private readonly bool before;
        private IDisposable? handle;

        public OwnedRegistration(
            string key,
            string owner,
            ModConfig config,
            string target,
            AuraRoutedHookRequest request,
            bool before)
        {
            Key = key;
            Owner = owner;
            this.config = config;
            this.target = target;
            this.request = request;
            this.before = before;
            Generation = ++nextGeneration;
        }

        public string Key { get; }
        public string Owner { get; }
        public long Generation { get; }
        public int LeaseCount { get; private set; } = 1;

        public bool Matches(Action<ModHookContext> action)
        {
            return request.Handler != null
                   && request.Handler.Equals(action);
        }

        public void AddLease()
        {
            LeaseCount++;
        }

        public bool ReleaseLease()
        {
            LeaseCount--;
            return LeaseCount <= 0;
        }

        public void Activate()
        {
            lock (this)
            {
                if (handle != null) return;
                handle = before
                    ? AuraSharedHooks.RegisterBeforeRouted(
                        config,
                        target,
                        request,
                        AuraToolsLog.Info,
                        message => AuraToolsLog.Warn(
                            Prefix(Owner) + message))
                    : AuraSharedHooks.RegisterAfterRouted(
                        config,
                        target,
                        request,
                        AuraToolsLog.Info,
                        message => AuraToolsLog.Warn(
                            Prefix(Owner) + message));
            }
        }

        public void Deactivate()
        {
            lock (this)
            {
                handle?.Dispose();
                handle = null;
            }
        }
    }

    private sealed class OwnedLease : IDisposable
    {
        private string key;
        private readonly long generation;

        public OwnedLease(string key, long generation)
        {
            this.key = key;
            this.generation = generation;
        }

        public void Dispose()
        {
            if (key.Length == 0) return;
            OwnedRegistration? registration = null;
            lock (Gate)
            {
                if (Owned.TryGetValue(key, out var current)
                    && current.Generation == generation)
                {
                    if (current.ReleaseLease())
                    {
                        Owned.Remove(key);
                        registration = current;
                    }
                }
            }

            key = "";
            registration?.Deactivate();
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
