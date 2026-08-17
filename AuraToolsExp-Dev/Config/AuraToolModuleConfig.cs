using System;
using System.Collections.Generic;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolModuleConfigDocument<T>
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonProperty("migratedFrom")]
    public string MigratedFrom { get; set; } = "legacy-aggregate";

    [JsonProperty("settings")]
    public T Settings { get; set; } = default!;
}

public sealed class AuraToolConfigChangedEvent
{
    public string ModuleId { get; set; } = "";

    public long Revision { get; set; }
}

public static class AuraToolConfigChangeBus
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, List<Action<AuraToolConfigChangedEvent>>>
        Handlers = new(StringComparer.Ordinal);

    public static IDisposable Subscribe(
        string moduleId,
        Action<AuraToolConfigChangedEvent> handler)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("Module id is required.", nameof(moduleId));
        }
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        lock (Gate)
        {
            if (!Handlers.TryGetValue(moduleId, out var values))
            {
                values = new List<Action<AuraToolConfigChangedEvent>>();
                Handlers[moduleId] = values;
            }
            values.Add(handler);
        }
        return new Subscription(moduleId, handler);
    }

    public static void Publish(string moduleId, long revision)
    {
        Action<AuraToolConfigChangedEvent>[] handlers;
        lock (Gate)
        {
            handlers = Handlers.TryGetValue(moduleId ?? "", out var values)
                ? values.ToArray()
                : Array.Empty<Action<AuraToolConfigChangedEvent>>();
        }

        var change = new AuraToolConfigChangedEvent
        {
            ModuleId = moduleId ?? "",
            Revision = revision
        };
        foreach (var handler in handlers)
        {
            try
            {
                handler(change);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn(
                    "Module config subscriber failed for " + change.ModuleId
                    + ": " + ex.Message);
            }
        }
    }

    private static void Unsubscribe(
        string moduleId,
        Action<AuraToolConfigChangedEvent> handler)
    {
        lock (Gate)
        {
            if (!Handlers.TryGetValue(moduleId, out var values))
            {
                return;
            }
            values.Remove(handler);
            if (values.Count == 0)
            {
                Handlers.Remove(moduleId);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private string moduleId;
        private Action<AuraToolConfigChangedEvent>? handler;

        public Subscription(
            string moduleId,
            Action<AuraToolConfigChangedEvent> handler)
        {
            this.moduleId = moduleId;
            this.handler = handler;
        }

        public void Dispose()
        {
            var value = handler;
            if (value == null)
            {
                return;
            }
            handler = null;
            Unsubscribe(moduleId, value);
            moduleId = "";
        }
    }
}

internal sealed class AuraToolModuleConfigStore
{
    public const string ConfigSystem = "AuraTools.Modules";
    private readonly Dictionary<string, long> revisions =
        new(StringComparer.Ordinal);

    public void Reset()
    {
        revisions.Clear();
    }

    public T Load<T>(string moduleId, T fallback, out bool migrated)
    {
        var document = new AuraToolModuleConfigDocument<T>
        {
            ModuleId = moduleId,
            Settings = fallback
        };
        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            ConfigSystem,
            FileName(moduleId),
            document);
        revisions[moduleId] = snapshot.Revision;
        migrated = !snapshot.Found;
        if (!snapshot.Found
            || snapshot.Value == null
            || !string.Equals(
                snapshot.Value.ModuleId,
                moduleId,
                StringComparison.Ordinal))
        {
            migrated = true;
            return fallback;
        }

        if (snapshot.Value.Settings is null)
        {
            migrated = true;
            return fallback;
        }
        return snapshot.Value.Settings;
    }

    public bool Save<T>(string moduleId, T settings, out long revision)
    {
        var expected = revisions.TryGetValue(moduleId, out var known)
            ? known
            : 0;
        var result = AuraSharedConfigStore.WriteOwner(
            AuraToolsIds.ModId,
            ConfigSystem,
            FileName(moduleId),
            new AuraToolModuleConfigDocument<T>
            {
                ModuleId = moduleId,
                Settings = settings
            },
            expected,
            schemaVersion: 1);
        revision = result.Revision;
        if (!result.Success)
        {
            AuraToolsLog.Warn(
                "Failed to save module config " + moduleId + ": " + result.Message);
            return false;
        }

        revisions[moduleId] = result.Revision;
        return true;
    }

    public static string FileName(string moduleId)
    {
        return (moduleId ?? "unknown").Trim() + ".json";
    }
}
