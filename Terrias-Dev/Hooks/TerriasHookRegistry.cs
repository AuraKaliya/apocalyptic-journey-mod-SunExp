using System;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpHookRegistry
{
    public static bool Before(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterBefore(
            config,
            target,
            action,
            SunExpLog.Debug,
            message => SunExpLog.Warn(OwnerPrefix(owner) + message),
            safeInvoke: true);
    }

    public static bool After(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterAfter(
            config,
            target,
            action,
            SunExpLog.Debug,
            message => SunExpLog.Warn(OwnerPrefix(owner) + message),
            safeInvoke: true);
    }

    public static IDisposable BeforeRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterBeforeRouted(
            config,
            target,
            action,
            SunExpLog.Debug,
            message => SunExpLog.Warn(OwnerPrefix(owner) + message),
            safeInvoke: true);
    }

    public static IDisposable AfterRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterAfterRouted(
            config,
            target,
            action,
            SunExpLog.Debug,
            message => SunExpLog.Warn(OwnerPrefix(owner) + message),
            safeInvoke: true);
    }

    private static string OwnerPrefix(string owner)
    {
        return string.IsNullOrWhiteSpace(owner) ? "" : "[" + owner.Trim() + "] ";
    }
}
