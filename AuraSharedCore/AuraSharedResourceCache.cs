using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Witch.Core;

namespace AuraShared.Core;

public sealed class AuraSharedResourceCacheStats
{
    public int EntryCount { get; set; }
    public int ReferenceCount { get; set; }
    public int CategoryCount { get; set; }
    public long EstimatedBytes { get; set; }
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
    private static long estimatedBytes;

    public static T? Load<T>(
        string ownerModId,
        string path,
        bool loadFromMod = true,
        string category = "",
        Action<string>? warn = null)
        where T : UnityEngine.Object
        => Load<T>(ownerModId, path, loadFromMod, category, out _, warn);

    public static T? Load<T>(string ownerModId, string path, bool loadFromMod,
        string category, out bool cacheHit, Action<string>? warn = null)
        where T : UnityEngine.Object
    {
        cacheHit = false;
        var key = CacheKey<T>(ownerModId, path, loadFromMod, all: false);
        if (key.Length == 0)
        {
            return null;
        }

        lock (Gate)
        {
            if (ObjectCache.TryGetValue(key, out var cached))
            {
                if (ReferenceEquals(cached, null) || cached != null)
                {
                    cacheHit = true;
                    TouchNoLock(key);
                    return cached as T;
                }
                RemoveEntryNoLock(key); // An externally destroyed Unity asset is not a usable hit.
            }
        }

        try
        {
            // The native custom-image path accepts Texture, not Texture2D.
            var loaded = typeof(T) == typeof(UnityEngine.Texture2D)
                ? ResourceLoader.Load<UnityEngine.Texture>(path.Trim(), loadFromMod) as T
                : ResourceLoader.Load<T>(path.Trim(), loadFromMod);
            lock (Gate)
            {
                StoreNoLock(key, ownerModId, category, loaded, null, 1, EstimateObjectBytes(loaded));
            }

            return loaded;
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                StoreNoLock(key, ownerModId, category, null, null, 1, 0L);
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
        => LoadAll<T>(ownerModId, path, category, out _, warn);

    public static T[] LoadAll<T>(string ownerModId, string path, string category,
        out bool cacheHit, Action<string>? warn = null)
        where T : UnityEngine.Object
    {
        cacheHit = false;
        var key = CacheKey<T>(ownerModId, path, loadFromMod: true, all: true);
        if (key.Length == 0)
        {
            return Array.Empty<T>();
        }

        lock (Gate)
        {
            if (ObjectArrayCache.TryGetValue(key, out var cached))
            {
                if (cached == null || !cached.Cast<UnityEngine.Object>().Any(value => !ReferenceEquals(value, null) && value == null))
                {
                    cacheHit = true;
                    TouchNoLock(key);
                    return cached as T[] ?? cached?.OfType<T>().ToArray() ?? Array.Empty<T>();
                }
                RemoveEntryNoLock(key);
            }
        }

        try
        {
            var loaded = typeof(T) == typeof(UnityEngine.Texture2D)
                ? (ResourceLoader.LoadAll<UnityEngine.Texture>(path.Trim()) ?? Array.Empty<UnityEngine.Texture>()).OfType<T>().ToArray()
                : ResourceLoader.LoadAll<T>(path.Trim()) ?? Array.Empty<T>();
            lock (Gate)
            {
                StoreNoLock(
                    key,
                    ownerModId,
                    category,
                    null,
                    loaded,
                    Math.Max(1, loaded.Length),
                    EstimateArrayBytes(loaded));
            }

            return loaded;
        }
        catch (Exception ex)
        {
            var empty = Array.Empty<T>();
            lock (Gate)
            {
                StoreNoLock(key, ownerModId, category, null, empty, 1, 0L);
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
                    CategoryCount = CountOwnerCategoriesNoLock(normalizedOwner),
                    EstimatedBytes = usage?.EstimatedBytes ?? 0L
                };
            }

            return new AuraSharedResourceCacheStats
            {
                EntryCount = Entries.Count,
                ReferenceCount = referenceCount,
                CategoryCount = CategoryKeys.Count,
                EstimatedBytes = estimatedBytes
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
                estimatedBytes = 0L;
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
        int weight,
        long estimatedEntryBytes)
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
        var normalizedEstimatedBytes = Math.Max(0L, estimatedEntryBytes);
        Entries[key] = new CacheEntry(owner, normalizedCategory, normalizedWeight, normalizedEstimatedBytes, node);
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
        usage.EstimatedBytes = SaturatingAdd(usage.EstimatedBytes, normalizedEstimatedBytes);
        referenceCount += normalizedWeight;
        estimatedBytes = SaturatingAdd(estimatedBytes, normalizedEstimatedBytes);
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
        estimatedBytes = Math.Max(0L, estimatedBytes - entry.EstimatedBytes);
        if (OwnerUsages.TryGetValue(entry.OwnerModId, out var usage))
        {
            usage.EntryCount = Math.Max(0, usage.EntryCount - 1);
            usage.ReferenceCount = Math.Max(0, usage.ReferenceCount - entry.Weight);
            usage.EstimatedBytes = Math.Max(0L, usage.EstimatedBytes - entry.EstimatedBytes);
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

    public static long EstimateObjectBytes(UnityEngine.Object? value)
    {
        if (value == null)
        {
            return 0L;
        }

        try
        {
            switch (value)
            {
                case Sprite sprite when sprite.texture != null:
                    return EstimateTextureBytes(sprite.texture);
                case Texture texture:
                    return EstimateTextureBytes(texture);
                case AudioClip clip:
                    return SaturatingMultiply(Math.Max(1, clip.samples), Math.Max(1, clip.channels), sizeof(float));
                case Mesh mesh:
                    return SaturatingMultiply(Math.Max(1, mesh.vertexCount), 32L);
                case TextAsset text:
                    return text.bytes?.LongLength ?? 0L;
                case GameObject:
                    return 64L * 1024L;
                default:
                    return 4L * 1024L;
            }
        }
        catch
        {
            return 0L;
        }
    }

    private static long EstimateArrayBytes(Array? values)
    {
        if (values == null || values.Length == 0)
        {
            return 0L;
        }

        var total = 0L;
        foreach (var value in values)
        {
            if (value is UnityEngine.Object unityObject)
            {
                total = SaturatingAdd(total, EstimateObjectBytes(unityObject));
            }
        }

        return total;
    }

    private static long EstimateTextureBytes(Texture texture)
    {
        return SaturatingMultiply(Math.Max(1, texture.width), Math.Max(1, texture.height), 4L);
    }

    private static long SaturatingMultiply(long left, long right, long multiplier = 1L)
    {
        try
        {
            return checked(left * right * multiplier);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static long SaturatingAdd(long left, long right)
    {
        return long.MaxValue - left < right ? long.MaxValue : left + right;
    }

    private static string CacheKey<T>(string ownerModId, string path, bool loadFromMod, bool all)
    {
        var owner = NormalizeOwner(ownerModId);
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var resourceType = typeof(T) == typeof(UnityEngine.Texture2D) ? typeof(UnityEngine.Texture) : typeof(T);
        return owner + "|" + resourceType.FullName + "|" + (all ? "all" : loadFromMod.ToString()) + "|" + path.Trim();
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
        public CacheEntry(
            string ownerModId,
            string categoryKey,
            int weight,
            long estimatedBytes,
            LinkedListNode<string> node)
        {
            OwnerModId = ownerModId;
            CategoryKey = categoryKey;
            Weight = weight;
            EstimatedBytes = estimatedBytes;
            Node = node;
        }

        public string OwnerModId { get; }
        public string CategoryKey { get; }
        public int Weight { get; }
        public long EstimatedBytes { get; }
        public LinkedListNode<string> Node { get; }
    }

    private sealed class OwnerUsage
    {
        public int EntryCount { get; set; }
        public int ReferenceCount { get; set; }
        public long EstimatedBytes { get; set; }
    }
}
