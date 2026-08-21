using System;
using System.Collections.Generic;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public sealed class AuraHookRegistry : IDisposable
{
    private readonly ModConfig? config;
    private readonly string owner;
    private readonly Action<string>? info;
    private readonly Action<string>? warn;
    private readonly List<IDisposable> registrations = new();
    private bool disposed;

    public AuraHookRegistry(
        ModConfig? config,
        string owner,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        this.config = config;
        this.owner = string.IsNullOrWhiteSpace(owner) ? "AuraShared" : owner.Trim();
        this.info = info;
        this.warn = warn;
    }

    public IDisposable BeforeRouted(string target, Action<ModHookContext> action, string handlerId = "")
    {
        return Add(AuraSharedHooks.RegisterBeforeRouted(
            config,
            target,
            Request(action, target, handlerId),
            Debug,
            Warn), target, handlerId, before: true);
    }

    public IDisposable AfterRouted(string target, Action<ModHookContext> action, string handlerId = "")
    {
        return Add(AuraSharedHooks.RegisterAfterRouted(
            config,
            target,
            Request(action, target, handlerId),
            Debug,
            Warn), target, handlerId, before: false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        for (var i = registrations.Count - 1; i >= 0; i--)
        {
            try
            {
                registrations[i].Dispose();
            }
            catch (Exception ex)
            {
                Warn("Hook dispose failed: " + ex.Message);
            }
        }

        registrations.Clear();
    }

    private IDisposable Add(IDisposable registration, string target, string handlerId, bool before)
    {
        registrations.Add(registration);
        Debug("Hook " + (before ? "before" : "after") + " routed owner="
              + owner
              + ", handler="
              + (string.IsNullOrWhiteSpace(handlerId) ? "<anonymous>" : handlerId.Trim())
              + ", target="
              + target);
        return registration;
    }

    private AuraRoutedHookRequest Request(
        Action<ModHookContext> action,
        string target,
        string handlerId)
    {
        return new AuraRoutedHookRequest
        {
            OwnerModId = owner,
            HandlerId = string.IsNullOrWhiteSpace(handlerId)
                ? (target ?? "") + ".anonymous"
                : handlerId.Trim(),
            Handler = action,
            SafeInvoke = true
        };
    }

    private void Debug(string message)
    {
        if (info != null)
        {
            info("[" + owner + "] " + message);
            return;
        }

        AuraSharedLog.DebugLog(owner, message, false);
    }

    private void Warn(string message)
    {
        if (warn != null)
        {
            warn("[" + owner + "] " + message);
            return;
        }

        AuraSharedLog.Warn(owner, message);
    }
}
