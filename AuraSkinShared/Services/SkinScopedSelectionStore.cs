using System;
using System.Collections.Generic;
using AuraSkin.Shared.GameApi;

namespace AuraSkin.Shared.Services;

public static class SkinScopedSelectionStore
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, List<Entry>> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static long sequence;

    public static IDisposable Push(
        string ownerId,
        string careerId,
        string instanceId,
        string qualifiedSkinId)
    {
        var owner = (ownerId ?? "").Trim();
        var career = CareerConfigApi.NormalizeId(careerId);
        var instance = (instanceId ?? "").Trim();
        var skin = (qualifiedSkinId ?? "").Trim();
        if (owner.Length == 0 || career.Length == 0 || skin.Length == 0
            || SkinRegistry.ResolveReference(career, skin, effectiveOnly: false) == null)
            return EmptyScope.Instance;
        var entry = new Entry { OwnerId = owner, Token = ++sequence, QualifiedSkinId = skin };
        var key = Key(career, instance);
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var values))
            {
                values = new List<Entry>();
                Entries[key] = values;
            }
            values.Add(entry);
        }
        return new Handle(key, entry.Token);
    }

    public static string Get(string careerId, string instanceId = "")
    {
        var career = CareerConfigApi.NormalizeId(careerId);
        var instance = (instanceId ?? "").Trim();
        lock (Gate)
        {
            if (instance.Length > 0 && Entries.TryGetValue(Key(career, instance), out var exact) && exact.Count > 0)
                return exact[exact.Count - 1].QualifiedSkinId;
            return Entries.TryGetValue(Key(career, ""), out var careerWide) && careerWide.Count > 0
                ? careerWide[careerWide.Count - 1].QualifiedSkinId
                : "";
        }
    }

    private static string Key(string careerId, string instanceId)
    {
        return CareerConfigApi.NormalizeId(careerId) + "\u001f" + (instanceId ?? "").Trim();
    }

    private static void Remove(string key, long token)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var values)) return;
            values.RemoveAll(value => value.Token == token);
            if (values.Count == 0) Entries.Remove(key);
        }
    }

    private sealed class Entry
    {
        internal string OwnerId { get; set; } = "";
        internal long Token { get; set; }
        internal string QualifiedSkinId { get; set; } = "";
    }

    private sealed class Handle : IDisposable
    {
        private readonly long token;
        private string? key;

        internal Handle(string key, long token)
        {
            this.key = key;
            this.token = token;
        }

        public void Dispose()
        {
            var current = key;
            if (current == null) return;
            key = null;
            Remove(current, token);
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }
}
