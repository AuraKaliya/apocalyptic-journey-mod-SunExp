using System;
using AuraShared.Core;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsHookRegistry
{
    public static bool Before(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterBefore(
            config,
            target,
            action,
            AuraToolsLog.Info,
            message => AuraToolsLog.Warn(Prefix(owner) + message),
            safeInvoke: true);
    }

    public static bool After(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterAfter(
            config,
            target,
            action,
            AuraToolsLog.Info,
            message => AuraToolsLog.Warn(Prefix(owner) + message),
            safeInvoke: true);
    }

    public static IDisposable BeforeRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterBeforeRouted(
            config,
            target,
            action,
            AuraToolsLog.Info,
            message => AuraToolsLog.Warn(Prefix(owner) + message),
            safeInvoke: true);
    }

    public static IDisposable AfterRouted(ModConfig config, string target, Action<ModHookContext> action, string owner)
    {
        return AuraSharedHooks.RegisterAfterRouted(
            config,
            target,
            action,
            AuraToolsLog.Info,
            message => AuraToolsLog.Warn(Prefix(owner) + message),
            safeInvoke: true);
    }

    private static string Prefix(string owner)
    {
        return string.IsNullOrWhiteSpace(owner) ? "" : "[" + owner.Trim() + "] ";
    }
}
