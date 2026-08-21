using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasHookRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IDisposable> OwnedRegistrations = new(StringComparer.Ordinal);

    public static bool Before(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return RegisterOwnedRouted(config, target, action, owner, before: true);
    }

    public static bool After(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return RegisterOwnedRouted(config, target, action, owner, before: false);
    }

    public static IDisposable BeforeRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterBeforeRouted(
            config,
            target,
            action,
            TerriasLog.Debug,
            message => TerriasLog.Warn(OwnerPrefix(owner) + message),
            safeInvoke: true);
    }

    public static IDisposable AfterRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterAfterRouted(
            config,
            target,
            action,
            TerriasLog.Debug,
            message => TerriasLog.Warn(OwnerPrefix(owner) + message),
            safeInvoke: true);
    }

    private static string OwnerPrefix(string owner)
    {
        return string.IsNullOrWhiteSpace(owner) ? "" : "[" + owner.Trim() + "] ";
    }

    private static bool RegisterOwnedRouted(
        ModConfig config,
        string target,
        Action<ModHookContext> action,
        string owner,
        bool before)
    {
        if (config == null || string.IsNullOrWhiteSpace(target) || action == null)
        {
            return false;
        }

        var normalizedOwner = (owner ?? "").Trim();
        var key = (before ? "before:" : "after:")
                  + target.Trim()
                  + ":"
                  + normalizedOwner
                  + ":"
                  + (action.Method.DeclaringType?.FullName ?? "handler")
                  + "."
                  + action.Method.Name;
        var registration = before
            ? BeforeRouted(config, target, action, normalizedOwner)
            : AfterRouted(config, target, action, normalizedOwner);
        lock (Gate)
        {
            if (OwnedRegistrations.TryGetValue(key, out var previous)) previous.Dispose();
            OwnedRegistrations[key] = registration;
        }

        return true;
    }
}
