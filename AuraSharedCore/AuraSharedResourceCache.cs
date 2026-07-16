using System;
using System.Collections.Generic;
using UnityEngine;
using Witch.Core;

namespace AuraShared.Core;

public sealed class AuraSharedResourceCacheStats
{
    public int EntryCount { get; set; }
    public int ReferenceCount { get; set; }
    public int CategoryCount { get; set; }
}

public static class AuraSharedResourceCache
{
    public const int MaximumEntries = 512;
    public const int MaximumReferences = 4096;
    public const int MaximumEntriesPerOwner = 256;
    public const int MaximumReferencesPerOwner = 2048;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, UnityEngine.Object?> ObjectCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Array?> ObjectArrayCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> CategoryKeys = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CacheEntry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, OwnerUsage> OwnerUsages = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Recency = new();
    private static int referenceCount;

    public static T? Load<T>(
        string ownerModId,
        string path,
        bool loadFromMod = true,
        string category = "",
        Action<string>? warn = null)
        where T : UnityEngine.Object
    {
        var key = CacheKey<T>(ownerModId, path, loadFromMod, all: false);
        if (key.Length == 0)
        {
            return null;
        }

        lock (Gate)
        {
            if (ObjectCache.TryGetValue(key, out var cached))
            {
                TouchNoLock(key);
                return cached as T;
            }
        }

        try
        {
            var loaded = ResourceLoader.Load<T>(path.Trim(), loadFromMod);
            lock (Gate)
            {
                StoreNoLock(key, ownerModId, category, loaded, null, 1);
            }

            return loaded;
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                StoreNoLock(key, ownerModId, category, null, null, 1);
            }

            WarnLoadFailure(ownerModId, warn, "load failed", typeof(T).Name, path, key, ex);
            return null;
        }
    }

    public static T[] LoadAll<T>(
        string ownerModId,
        string path,
        string category = "",
        Action<string>? warn = null)
        where T : UnityEngine.Object
    {
        var key = CacheKey<T>(ownerModId, path, loadFromMod: true, all: true);
        if (key.Length == 0)
        {
            return Array.Empty<T>();
        }

        lock (Gate)
        {
            if (ObjectArrayCache.TryGetValue(key, out var cached))
            {
                TouchNoLock(key);
                return cached as T[] ?? Array.Empty<T>();
            }
        }

        try
        {
            var loaded = ResourceLoader.LoadAll<T>(path.Trim()) ?? Array.Empty<T>();
            lock (Gate)
            {
                StoreNoLock(key, ownerModId, category, null, loaded, Math.Max(1, loaded.Length));
            }

            return loaded;
        }
        catch (Exception ex)
        {
            var empty = Array.Empty<T>();
            lock (Gate)
            {
                StoreNoLock(key, ownerModId, category, null, empty, 1);
            }

            WarnLoadFailure(ownerModId, warn, "load-all failed", typeof(T).Name, path, key, ex);
            return empty;
        }
    }

    public static AuraSharedResourceCacheStats GetStats(string ownerModId = "")
    {
        var owner = Normalize(ownerModId);
        lock (Gate)
        {
            if (owner.Length > 0)
            {
                var normalizedOwner = NormalizeOwner(owner);
                OwnerUsages.TryGetValue(normalizedOwner, out var usage);
                return new AuraSharedResourceCacheStats
                {
                    EntryCount = usage?.EntryCount ?? 0,
                    ReferenceCount = usage?.ReferenceCount ?? 0,
                    CategoryCount = CountOwnerCategoriesNoLock(normalizedOwner)
                };
            }

            return new AuraSharedResourceCacheStats
            {
                EntryCount = Entries.Count,
                ReferenceCount = referenceCount,
                CategoryCount = CategoryKeys.Count
            };
        }
    }

    public static void Clear(string ownerModId = "")
    {
        var owner = Normalize(ownerModId);
        lock (Gate)
        {
            if (owner.Length == 0)
            {
                ObjectCache.Clear();
                ObjectArrayCache.Clear();
                CategoryKeys.Clear();
                Entries.Clear();
                OwnerUsages.Clear();
                Recency.Clear();
                referenceCount = 0;
                return;
            }

            var normalizedOwner = NormalizeOwner(owner);
            var remove = new List<string>();
            foreach (var pair in Entries)
            {
                if (string.Equals(pair.Value.OwnerModId, normalizedOwner, StringComparison.Ordinal))
                {
                    remove.Add(pair.Key);
                }
            }

            foreach (var key in remove)
            {
                RemoveEntryNoLock(key);
            }
        }
    }

    public static void ClearCategory(string ownerModId, string category)
    {
        var normalized = CategoryKey(ownerModId, category);
        if (normalized.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            if (!CategoryKeys.TryGetValue(normalized, out var keys))
            {
                return;
            }

            foreach (var key in new List<string>(keys))
            {
                RemoveEntryNoLock(key);
            }
        }
    }

    private static void StoreNoLock(
        string key,
        string ownerModId,
        string category,
        UnityEngine.Object? value,
        Array? array,
        int weight)
    {
        var owner = NormalizeOwner(ownerModId);
        var normalizedWeight = Math.Max(1, weight);
        RemoveEntryNoLock(key);

        // Oversized arrays are returned to the caller but never retained by the shared cache.
        if (normalizedWeight > MaximumReferencesPerOwner || normalizedWeight > MaximumReferences)
        {
            return;
        }

        var normalizedCategory = CategoryKey(owner, category);
        var node = Recency.AddLast(key);
        Entries[key] = new CacheEntry(owner, normalizedCategory, normalizedWeight, node);
        if (array != null)
        {
            ObjectArrayCache[key] = array;
        }
        else
        {
            ObjectCache[key] = value;
        }

        if (normalizedCategory.Length > 0)
        {
            if (!CategoryKeys.TryGetValue(normalizedCategory, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                CategoryKeys[normalizedCategory] = keys;
            }

            keys.Add(key);
        }

        if (!OwnerUsages.TryGetValue(owner, out var usage))
        {
            usage = new OwnerUsage();
            OwnerUsages[owner] = usage;
        }

        usage.EntryCount++;
        usage.ReferenceCount += normalizedWeight;
        referenceCount += normalizedWeight;
        EnforceLimitsNoLock(owner);
    }

    private static void EnforceLimitsNoLock(string owner)
    {
        while (Entries.Count > MaximumEntries || referenceCount > MaximumReferences)
        {
            if (Recency.First == null)
            {
                break;
            }

            RemoveEntryNoLock(Recency.First.Value);
        }

        while (OwnerUsages.TryGetValue(owner, out var usage)
               && (usage.EntryCount > MaximumEntriesPerOwner
                   || usage.ReferenceCount > MaximumReferencesPerOwner))
        {
            var node = Recency.First;
            while (node != null
                   && (!Entries.TryGetValue(node.Value, out var entry)
                       || !string.Equals(entry.OwnerModId, owner, StringComparison.Ordinal)))
            {
                node = node.Next;
            }

            if (node == null)
            {
                break;
            }

            RemoveEntryNoLock(node.Value);
        }
    }

    private static void TouchNoLock(string key)
    {
        if (!Entries.TryGetValue(key, out var entry) || entry.Node.List == null)
        {
            return;
        }

        Recency.Remove(entry.Node);
        Recency.AddLast(entry.Node);
    }

    private static void RemoveEntryNoLock(string key)
    {
        ObjectCache.Remove(key);
        ObjectArrayCache.Remove(key);
        if (!Entries.TryGetValue(key, out var entry))
        {
            return;
        }

        Entries.Remove(key);
        if (entry.Node.List != null)
        {
            Recency.Remove(entry.Node);
        }

        referenceCount = Math.Max(0, referenceCount - entry.Weight);
        if (OwnerUsages.TryGetValue(entry.OwnerModId, out var usage))
        {
            usage.EntryCount = Math.Max(0, usage.EntryCount - 1);
            usage.ReferenceCount = Math.Max(0, usage.ReferenceCount - entry.Weight);
            if (usage.EntryCount == 0)
            {
                OwnerUsages.Remove(entry.OwnerModId);
            }
        }

        if (entry.CategoryKey.Length > 0 && CategoryKeys.TryGetValue(entry.CategoryKey, out var keys))
        {
            keys.Remove(key);
            if (keys.Count == 0)
            {
                CategoryKeys.Remove(entry.CategoryKey);
            }
        }
    }

    private static void WarnLoadFailure(
        string ownerModId,
        Action<string>? warn,
        string operation,
        string typeName,
        string path,
        string key,
        Exception ex)
    {
        var owner = NormalizeOwner(ownerModId);
        var message = "[ResourceCache] " + operation + ": " + typeName + " " + path + " (" + ex.Message + ")";
        if (warn != null)
        {
            warn(message);
        }
        else
        {
            AuraSharedLog.WarnOnce(owner, "resource-load:" + key, message);
        }
    }

    private static int CountOwnerCategoriesNoLock(string owner)
    {
        var prefix = owner + "|";
        var count = 0;
        foreach (var key in CategoryKeys.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string CacheKey<T>(string ownerModId, string path, bool loadFromMod, bool all)
    {
        var owner = NormalizeOwner(ownerModId);
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return owner + "|" + typeof(T).FullName + "|" + (all ? "all" : loadFromMod.ToString()) + "|" + path.Trim();
    }

    private static string CategoryKey(string ownerModId, string category)
    {
        var owner = NormalizeOwner(ownerModId);
        var value = Normalize(category);
        return value.Length == 0 ? "" : owner + "|" + value;
    }

    private static string NormalizeOwner(string ownerModId)
    {
        var owner = Normalize(ownerModId);
        return owner.Length == 0 ? "AuraShared" : owner;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private sealed class CacheEntry
    {
        public CacheEntry(string ownerModId, string categoryKey, int weight, LinkedListNode<string> node)
        {
            OwnerModId = ownerModId;
            CategoryKey = categoryKey;
            Weight = weight;
            Node = node;
        }

        public string OwnerModId { get; }
        public string CategoryKey { get; }
        public int Weight { get; }
        public LinkedListNode<string> Node { get; }
    }

    private sealed class OwnerUsage
    {
        public int EntryCount { get; set; }
        public int ReferenceCount { get; set; }
    }
}
