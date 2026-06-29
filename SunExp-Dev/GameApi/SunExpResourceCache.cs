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

    public static T? Load<T>(string path, bool loadFromMod = true)
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
            SunExpPerformanceCounters.Record(loaded == null ? "ResourceCache.Load.Miss" : "ResourceCache.Load.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
            ObjectCache[key] = null;
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

    public static T[]? LoadAll<T>(string path)
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
            SunExpPerformanceCounters.Record((loaded?.Length ?? 0) == 0
                ? "ResourceCache.LoadAll.Miss"
                : "ResourceCache.LoadAll.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
            ObjectArrayCache[key] = null;
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
    }

    private static string CacheKey<T>(string path, bool loadFromMod)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return typeof(T).FullName + "\u001f" + loadFromMod + "\u001f" + path.Trim();
    }
}
