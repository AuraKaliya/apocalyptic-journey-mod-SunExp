using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.GameApi;

public static class SunExpResourceCache
{
    public static T? Load<T>(string path, bool loadFromMod = true, string category = "")
        where T : UnityEngine.Object
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var loaded = AuraSharedResourceCache.Load<T>(
                SunExpIds.ModId,
                path,
                loadFromMod,
                category,
                message => SunExpLog.Warn(message));
            SunExpPerformanceCounters.Record(loaded == null ? "ResourceCache.Load.Miss" : "ResourceCache.Load.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
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
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var loaded = AuraSharedResourceCache.LoadAll<T>(
                SunExpIds.ModId,
                path,
                category,
                message => SunExpLog.Warn(message));
            SunExpPerformanceCounters.Record((loaded?.Length ?? 0) == 0
                ? "ResourceCache.LoadAll.Miss"
                : "ResourceCache.LoadAll.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
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
        AuraSharedResourceCache.Clear(SunExpIds.ModId);
    }

    public static void ClearCategory(string category)
    {
        AuraSharedResourceCache.ClearCategory(SunExpIds.ModId, category);
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
}
