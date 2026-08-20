using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class TerriasActionPassiveRegistry
{
    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = new();
    private static Entry[] nativeStarted = Array.Empty<Entry>();
    private static Entry[] committed = Array.Empty<Entry>();

    public static void Register(
        ScriptExecutor? executor,
        string id,
        AuraCardActionPhase phases,
        Action<AuraCardActionContext> action)
    {
        if (executor?.Self == null || string.IsNullOrWhiteSpace(id) || action == null)
        {
            return;
        }

        var normalized = id.Trim();
        lock (Gate)
        {
            Entries.RemoveAll(entry => ReferenceEquals(entry.Executor, executor)
                                       && string.Equals(entry.Id, normalized, StringComparison.Ordinal));
            Entries.Add(new Entry(normalized, executor, phases, action));
            RebuildNoLock();
        }
    }

    public static void Unregister(ScriptExecutor? executor, string id)
    {
        if (executor == null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (Gate)
        {
            Entries.RemoveAll(entry => ReferenceEquals(entry.Executor, executor)
                                       && string.Equals(entry.Id, id.Trim(), StringComparison.Ordinal));
            RebuildNoLock();
        }
    }

    public static void Dispatch(AuraCardActionContext context)
    {
        var handlers = context.Phase == AuraCardActionPhase.NativeStarted
            ? nativeStarted
            : context.Phase == AuraCardActionPhase.Committed
                ? committed
                : Array.Empty<Entry>();
        for (var i = 0; i < handlers.Length; i++)
        {
            handlers[i].Invoke(context);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            RebuildNoLock();
        }
    }

    private static void RebuildNoLock()
    {
        nativeStarted = Entries
            .Where(entry => (entry.Phases & AuraCardActionPhase.NativeStarted) != 0)
            .ToArray();
        committed = Entries
            .Where(entry => (entry.Phases & AuraCardActionPhase.Committed) != 0)
            .ToArray();
    }

    private sealed class Entry
    {
        private readonly Action<AuraCardActionContext> action;

        public Entry(
            string id,
            ScriptExecutor executor,
            AuraCardActionPhase phases,
            Action<AuraCardActionContext> action)
        {
            Id = id;
            Executor = executor;
            Phases = phases;
            this.action = action;
        }

        public string Id { get; }
        public ScriptExecutor Executor { get; }
        public AuraCardActionPhase Phases { get; }

        public void Invoke(AuraCardActionContext context)
        {
            var ownerId = Executor.Self?.InstanceId ?? "";
            if (ownerId.Length == 0
                || !string.Equals(ownerId, context.OwnerStatusId, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                TerriasLog.Error("Action passive failed: " + Id, ex);
            }
        }
    }
}
