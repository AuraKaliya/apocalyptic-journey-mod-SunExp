using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.GameApi;

public static class TerriasResourceCache
{
    private const double SlowLoadWarningMilliseconds = 16.0;

    public static T? Load<T>(string path, bool loadFromMod = true, string category = "")
        where T : UnityEngine.Object
    {
        var start = TerriasPerformanceCounters.Timestamp();
        T? loaded = null;
        try
        {
            loaded = AuraSharedResourceCache.Load<T>(
                TerriasIds.ModId,
                path,
                loadFromMod,
                category,
                message => TerriasLog.Warn(message));
            TerriasPerformanceCounters.Record(loaded == null ? "ResourceCache.Load.Miss" : "ResourceCache.Load.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ResourceCache] load failed: "
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
            TerriasPerformanceCounters.RecordDuration("ResourceCache.Load", start);
            LogSlowLoad("Load", typeof(T).Name, path, category, loaded != null, start);
        }
    }

    public static T[]? LoadAll<T>(string path, string category = "")
        where T : UnityEngine.Object
    {
        var start = TerriasPerformanceCounters.Timestamp();
        T[]? loaded = null;
        try
        {
            loaded = AuraSharedResourceCache.LoadAll<T>(
                TerriasIds.ModId,
                path,
                category,
                message => TerriasLog.Warn(message));
            TerriasPerformanceCounters.Record((loaded?.Length ?? 0) == 0
                ? "ResourceCache.LoadAll.Miss"
                : "ResourceCache.LoadAll.Loaded");
            return loaded;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ResourceCache] load-all failed: "
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
            TerriasPerformanceCounters.RecordDuration("ResourceCache.LoadAll", start);
            LogSlowLoad("LoadAll", typeof(T).Name, path, category, (loaded?.Length ?? 0) > 0, start);
        }
    }

    public static void Clear()
    {
        AuraSharedResourceCache.Clear(TerriasIds.ModId);
    }

    public static void ClearCategory(string category)
    {
        AuraSharedResourceCache.ClearCategory(TerriasIds.ModId, category);
        TerriasPerformanceCounters.Record("ResourceCache.CategoryCleared");
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
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var elapsed = TerriasPerformanceCounters.ElapsedMilliseconds(startTimestamp);
        if (elapsed < SlowLoadWarningMilliseconds)
        {
            return;
        }

        TerriasLog.Warn("Slow Terrias resource " + operation
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
