using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class ScriptEventApi
{
    public static ScriptEventScope? BeginFightScope(ScriptExecutor? executor, string registrationId)
    {
        if (executor?.Self == null
            || string.IsNullOrWhiteSpace(registrationId)
            || !AuraBattleLeaseLedger.TryAcquire(
                executor,
                TerriasIds.ModId,
                registrationId,
                out var token))
        {
            return null;
        }

        return new ScriptEventScope(executor, registrationId.Trim(), token);
    }

    public static void InvalidateFightScope(ScriptExecutor? executor, string registrationId)
    {
        if (executor == null || string.IsNullOrWhiteSpace(registrationId))
        {
            return;
        }

        AuraBattleLeaseLedger.Invalidate(executor, TerriasIds.ModId, registrationId);
    }

    public static bool TryAddEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")
    {
        if (executor == null || executor.Self == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        var registrationId = SingleEventRegistrationId(eventName, context, script);
        if (!AuraBattleLeaseLedger.TryAcquire(
                executor,
                TerriasIds.ModId,
                registrationId,
                out var token))
        {
            return true;
        }

        var registered = TryAddEventRaw(
            executor,
            eventName,
            new Action(() =>
            {
                if (AuraBattleLeaseLedger.IsCurrent(token))
                {
                    script();
                }
            }),
            context);
        if (!registered)
        {
            AuraBattleLeaseLedger.Invalidate(token);
        }

        return registered;
    }

    public static bool TryAddEvent<T>(ScriptExecutor? executor, string eventName, Action<T> script, string context = "")
        where T : ISourceData
    {
        if (executor == null || executor.Self == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        var registrationId = SingleEventRegistrationId(eventName, context, script);
        if (!AuraBattleLeaseLedger.TryAcquire(
                executor,
                TerriasIds.ModId,
                registrationId,
                out var token))
        {
            return true;
        }

        var registered = TryAddEventRaw<T>(
            executor,
            eventName,
            data =>
            {
                if (AuraBattleLeaseLedger.IsCurrent(token))
                {
                    script(data);
                }
            },
            context);
        if (!registered)
        {
            AuraBattleLeaseLedger.Invalidate(token);
        }

        return registered;
    }

    internal static bool TryAddEventRaw(
        ScriptExecutor? executor,
        string eventName,
        Action script,
        string context = "")
    {
        if (executor == null || executor.Self == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        try
        {
            executor.AddEvent(eventName, script);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("TryAddEvent skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }

    internal static bool TryAddEventRaw<T>(
        ScriptExecutor? executor,
        string eventName,
        Action<T> script,
        string context = "")
        where T : ISourceData
    {
        if (executor == null || executor.Self == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        try
        {
            executor.AddEvent(eventName, script);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("TryAddEvent<T> skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }

    private static string SingleEventRegistrationId(string eventName, string context, Delegate action)
    {
        var source = string.IsNullOrWhiteSpace(context)
            ? (action.Method.DeclaringType?.FullName ?? "handler") + "." + action.Method.Name
            : context.Trim();
        return "Event." + source + "." + eventName.Trim();
    }

    public static bool TryAddOwnedEventListener(
        string eventName,
        Action script,
        object owner,
        EventDispose dispose = EventDispose.OnFightEnd,
        string context = "")
    {
        if (string.IsNullOrWhiteSpace(eventName) || script == null || owner == null)
        {
            return false;
        }

        var registrationId = SingleEventRegistrationId(eventName, context, script);
        if (!AuraBattleLeaseLedger.TryAcquire(
                owner,
                TerriasIds.ModId,
                registrationId,
                out var token))
        {
            return true;
        }

        try
        {
            EventCenter.Instance.AddEventListener(
                eventName,
                new Action(() =>
                {
                    if (AuraBattleLeaseLedger.IsCurrent(token))
                    {
                        script();
                    }
                }),
                owner,
                dispose);
            return true;
        }
        catch (Exception ex)
        {
            AuraBattleLeaseLedger.Invalidate(token);
            TerriasLog.Debug("TryAddOwnedEventListener skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }

    public static bool TryAddTempEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")
    {
        if (executor == null || executor.Self == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        try
        {
            executor.AddTempEvent(eventName, script);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("TryAddTempEvent skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }
}

public sealed class ScriptEventScope : IDisposable
{
    private readonly ScriptExecutor executor;
    private readonly AuraBattleLeaseToken token;
    private bool committed;
    private bool failed;
    private bool disposed;
    private readonly HashSet<string> markers = new(StringComparer.Ordinal);

    internal ScriptEventScope(
        ScriptExecutor executor,
        string registrationId,
        AuraBattleLeaseToken token)
    {
        this.executor = executor;
        RegistrationId = registrationId;
        this.token = token;
    }

    public string RegistrationId { get; }
    public long SessionId => token.SessionId;
    public long Generation => token.Generation;
    public bool IsActive => AuraBattleLeaseLedger.IsCurrent(token);

    public bool TryMark(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && markers.Add(key.Trim());
    }

    public bool AddRequired(string eventName, Action action, string context = "")
    {
        if (failed || action == null)
        {
            failed = true;
            return false;
        }

        var registered = ScriptEventApi.TryAddEventRaw(
            executor,
            eventName,
            new Action(() =>
            {
                if (IsActive)
                {
                    action();
                }
            }),
            context);
        failed |= !registered;
        return registered;
    }

    public bool AddRequired<T>(string eventName, Action<T> action, string context = "")
        where T : ISourceData
    {
        if (failed || action == null)
        {
            failed = true;
            return false;
        }

        var registered = ScriptEventApi.TryAddEventRaw<T>(
            executor,
            eventName,
            data =>
            {
                if (IsActive)
                {
                    action(data);
                }
            },
            context);
        failed |= !registered;
        return registered;
    }

    public bool Commit()
    {
        if (disposed || failed || !IsActive)
        {
            AuraBattleLeaseLedger.Invalidate(token);
            return false;
        }

        committed = true;
        return true;
    }

    public void Invalidate()
    {
        AuraBattleLeaseLedger.Invalidate(token);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (!committed)
        {
            AuraBattleLeaseLedger.Invalidate(token);
        }
    }
}
