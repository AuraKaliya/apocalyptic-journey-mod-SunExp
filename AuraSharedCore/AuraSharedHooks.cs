using System;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedHooks
{
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
}
