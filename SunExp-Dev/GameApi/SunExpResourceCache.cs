using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.GameApi;

public static class SunExpResourceCache
{
    private static readonly Dictionary<string, UnityEngine.Object?> ObjectCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Array?> ObjectArrayCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> CategoryKeys = new(StringComparer.Ordinal);

    public static T? Load<T>(string path, bool loadFromMod = true, string category = "")
        where T : UnityEngine.Object
    {
        var key = CacheKey<T>(path, loadFromMod);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (ObjectCache.TryGetValue(key, out var cached))
        {
            SunExpPerformanceCounters.Record("ResourceCache.Load.Hit");
            return cached as T;
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var resolvedPath = path.Trim();
            var loaded = ResourceLoader.Load<T>(resolvedPath, loadFromMod);
            ObjectCache[key] = loaded;
            AddCategoryKey(category, key);
            SunExpPerformanceCounters.Record(loaded == null ? "ResourceCache.Load.Miss" : "ResourceCache.Load.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
            ObjectCache[key] = null;
            AddCategoryKey(category, key);
            SunExpLog.Warn("[ResourceCache] load failed: "
                + typeof(T).Name
                + " "
                + path
                + " ("
                + ex.Message
                + ")");
            return null;
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ResourceCache.Load", start);
        }
    }

    public static T[]? LoadAll<T>(string path, string category = "")
        where T : UnityEngine.Object
    {
        var key = CacheKey<T>(path, true);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (ObjectArrayCache.TryGetValue(key, out var cached))
        {
            SunExpPerformanceCounters.Record("ResourceCache.LoadAll.Hit");
            return cached as T[];
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var resolvedPath = path.Trim();
            var loaded = ResourceLoader.LoadAll<T>(resolvedPath);
            ObjectArrayCache[key] = loaded;
            AddCategoryKey(category, key);
            SunExpPerformanceCounters.Record((loaded?.Length ?? 0) == 0
                ? "ResourceCache.LoadAll.Miss"
                : "ResourceCache.LoadAll.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
            ObjectArrayCache[key] = null;
            AddCategoryKey(category, key);
            SunExpLog.Warn("[ResourceCache] load-all failed: "
                + typeof(T).Name
                + " "
                + path
                + " ("
                + ex.Message
                + ")");
            return null;
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ResourceCache.LoadAll", start);
        }
    }

    public static void Clear()
    {
        ObjectCache.Clear();
        ObjectArrayCache.Clear();
        CategoryKeys.Clear();
    }

    public static void ClearCategory(string category)
    {
        var normalized = NormalizeCategory(category);
        if (normalized.Length == 0 || !CategoryKeys.TryGetValue(normalized, out var keys))
        {
            return;
        }

        foreach (var key in keys)
        {
            ObjectCache.Remove(key);
            ObjectArrayCache.Remove(key);
        }

        CategoryKeys.Remove(normalized);
        SunExpPerformanceCounters.Record("ResourceCache.CategoryCleared");
    }

    public static void Preload<T>(IEnumerable<string> paths, string category = "")
        where T : UnityEngine.Object
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                Load<T>(path, true, category);
            }
        }
    }

    private static string CacheKey<T>(string path, bool loadFromMod)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return typeof(T).FullName + "\u001f" + loadFromMod + "\u001f" + path.Trim();
    }

    private static void AddCategoryKey(string category, string key)
    {
        var normalized = NormalizeCategory(category);
        if (normalized.Length == 0 || key.Length == 0)
        {
            return;
        }

        if (!CategoryKeys.TryGetValue(normalized, out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            CategoryKeys[normalized] = keys;
        }

        keys.Add(key);
    }

    private static string NormalizeCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? "" : category.Trim();
    }
}
