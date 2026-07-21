using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.GameApi;

public static class SunExpResourceCache
{
    private const double SlowLoadWarningMilliseconds = 16.0;

    public static T? Load<T>(string path, bool loadFromMod = true, string category = "")
        where T : UnityEngine.Object
    {
        var start = SunExpPerformanceCounters.Timestamp();
        T? loaded = null;
        try
        {
            loaded = AuraSharedResourceCache.Load<T>(
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
            LogSlowLoad("Load", typeof(T).Name, path, category, loaded != null, start);
        }
    }

    public static T[]? LoadAll<T>(string path, string category = "")
        where T : UnityEngine.Object
    {
        var start = SunExpPerformanceCounters.Timestamp();
        T[]? loaded = null;
        try
        {
            loaded = AuraSharedResourceCache.LoadAll<T>(
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
            LogSlowLoad("LoadAll", typeof(T).Name, path, category, (loaded?.Length ?? 0) > 0, start);
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

    private static void LogSlowLoad(
        string operation,
        string typeName,
        string path,
        string category,
        bool hit,
        long startTimestamp)
    {
        if (!SunExpPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var elapsed = SunExpPerformanceCounters.ElapsedMilliseconds(startTimestamp);
        if (elapsed < SlowLoadWarningMilliseconds)
        {
            return;
        }

        SunExpLog.Warn("Slow SunExp resource " + operation
            + ": type="
            + typeName
            + ", elapsedMs="
            + elapsed.ToString("0.###")
            + ", hit="
            + hit
            + ", category="
            + (string.IsNullOrWhiteSpace(category) ? "<empty>" : category)
            + ", path="
            + path);
    }
}
