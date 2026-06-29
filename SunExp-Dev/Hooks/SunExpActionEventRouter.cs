using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public sealed class SunExpActionEventContext
{
    public SunExpActionEventContext(object payload, IDataConfig? config)
    {
        Payload = payload;
        Config = config;
    }

    public object Payload { get; }

    public IDataConfig? Config { get; }
}

public static class SunExpActionEventRouter
{
    private static readonly object EventOwner = new();
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.Ordinal);
    private static Handler[]? cachedHandlers;
    private static string? registeredStatusId;

    public static void RegisterHandler(
        string id,
        Action<SunExpActionEventContext>? onAction,
        Action? onActionAfter)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (SyncRoot)
        {
            Handlers[id.Trim()] = new Handler(id.Trim(), onAction, onActionAfter);
            cachedHandlers = null;
        }

        SunExpPerformanceCounters.Record("ActionEventRouter.HandlerRegistered");
    }

    public static void ResetForFight(string source)
    {
        PendingStatusReset(source);
        EnsureRegistered(source);
    }

    public static void EnsureRegistered(string source)
    {
        try
        {
            var statusId = FightPlayer.Instance?.Status?.InstanceId;
            if (string.IsNullOrWhiteSpace(statusId) || registeredStatusId == statusId)
            {
                return;
            }

            EventCenter.Instance.Clear(EventOwner);
            EventCenter.Instance.AddEventListener("Action" + statusId, new Action<object>(OnAction), EventOwner, EventDispose.OnFightEnd);
            EventCenter.Instance.AddEventListener("ActionAfter" + statusId, new Action(OnActionAfter), EventOwner, EventDispose.OnFightEnd);
            registeredStatusId = statusId;
            SunExpLog.Info("Registered shared SunExp Action router from " + source + ": statusId=" + statusId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to register shared SunExp Action router from " + source, ex);
        }
    }

    private static void PendingStatusReset(string source)
    {
        try
        {
            EventCenter.Instance.Clear(EventOwner);
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Action router clear skipped from " + source + ": " + ex.Message);
        }

        registeredStatusId = null;
    }

    private static void OnAction(object payload)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        var config = CardConfigApi.FromActionPayload(payload);
        var context = new SunExpActionEventContext(payload, config);
        foreach (var handler in SnapshotHandlers())
        {
            if (handler.OnAction == null)
            {
                continue;
            }

            try
            {
                handler.OnAction(context);
            }
            catch (Exception ex)
            {
                SunExpLog.Error("Action router handler failed: " + handler.Id, ex);
            }
        }

        SunExpPerformanceCounters.RecordDuration("ActionEventRouter.Action", start);
    }

    private static void OnActionAfter()
    {
        var start = SunExpPerformanceCounters.Timestamp();
        foreach (var handler in SnapshotHandlers())
        {
            if (handler.OnActionAfter == null)
            {
                continue;
            }

            try
            {
                handler.OnActionAfter();
            }
            catch (Exception ex)
            {
                SunExpLog.Error("ActionAfter router handler failed: " + handler.Id, ex);
            }
        }

        SunExpPerformanceCounters.RecordDuration("ActionEventRouter.ActionAfter", start);
    }

    private static Handler[] SnapshotHandlers()
    {
        lock (SyncRoot)
        {
            if (cachedHandlers != null)
            {
                return cachedHandlers;
            }

            var result = new Handler[Handlers.Count];
            Handlers.Values.CopyTo(result, 0);
            cachedHandlers = result;
            return cachedHandlers;
        }
    }

    private readonly struct Handler
    {
        public Handler(string id, Action<SunExpActionEventContext>? onAction, Action? onActionAfter)
        {
            Id = id;
            OnAction = onAction;
            OnActionAfter = onActionAfter;
        }

        public string Id { get; }

        public Action<SunExpActionEventContext>? OnAction { get; }

        public Action? OnActionAfter { get; }
    }
}
