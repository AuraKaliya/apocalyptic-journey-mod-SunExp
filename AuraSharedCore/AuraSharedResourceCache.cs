using System;
using System.Collections.Generic;
using UnityEngine;
using Witch.Core;

namespace AuraShared.Core;

public static class AuraSharedResourceCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, UnityEngine.Object?> ObjectCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Array?> ObjectArrayCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> CategoryKeys = new(StringComparer.Ordinal);

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
                return cached as T;
            }
        }

        try
        {
            var loaded = ResourceLoader.Load<T>(path.Trim(), loadFromMod);
            lock (Gate)
            {
                ObjectCache[key] = loaded;
                AddCategoryKey(category, key);
            }

            return loaded;
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                ObjectCache[key] = null;
                AddCategoryKey(category, key);
            }

            var owner = NormalizeOwner(ownerModId);
            var message = "[ResourceCache] load failed: " + typeof(T).Name + " " + path + " (" + ex.Message + ")";
            if (warn != null)
            {
                warn(message);
            }
            else
            {
                AuraSharedLog.WarnOnce(owner, "resource-load:" + key, message);
            }

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
                return cached as T[] ?? Array.Empty<T>();
            }
        }

        try
        {
            var loaded = ResourceLoader.LoadAll<T>(path.Trim()) ?? Array.Empty<T>();
            lock (Gate)
            {
                ObjectArrayCache[key] = loaded;
                AddCategoryKey(category, key);
            }

            return loaded;
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                ObjectArrayCache[key] = Array.Empty<T>();
                AddCategoryKey(category, key);
            }

            var owner = NormalizeOwner(ownerModId);
            var message = "[ResourceCache] load-all failed: " + typeof(T).Name + " " + path + " (" + ex.Message + ")";
            if (warn != null)
            {
                warn(message);
            }
            else
            {
                AuraSharedLog.WarnOnce(owner, "resource-load-all:" + key, message);
            }

            return Array.Empty<T>();
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
                return;
            }

            var prefix = owner + "|";
            RemoveKeys(prefix, ObjectCache);
            RemoveKeys(prefix, ObjectArrayCache);
            CategoryKeys.Clear();
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

            foreach (var key in keys)
            {
                ObjectCache.Remove(key);
                ObjectArrayCache.Remove(key);
            }

            CategoryKeys.Remove(normalized);
        }
    }

    private static void RemoveKeys<TValue>(string prefix, Dictionary<string, TValue> values)
    {
        var remove = new List<string>();
        foreach (var key in values.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                remove.Add(key);
            }
        }

        foreach (var key in remove)
        {
            values.Remove(key);
        }
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

    private static void AddCategoryKey(string category, string key)
    {
        var normalized = Normalize(category);
        if (normalized.Length == 0 || key.Length == 0)
        {
            return;
        }

        var ownerEnd = key.IndexOf('|');
        var owner = ownerEnd > 0 ? key.Substring(0, ownerEnd) : "AuraShared";
        var categoryKey = owner + "|" + normalized;
        if (!CategoryKeys.TryGetValue(categoryKey, out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            CategoryKeys[categoryKey] = keys;
        }

        keys.Add(key);
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
}
