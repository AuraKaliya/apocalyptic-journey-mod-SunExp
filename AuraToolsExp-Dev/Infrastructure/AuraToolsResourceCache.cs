using System;
using System.Collections.Generic;
using AuraShared.Core;
using UnityEngine;
using Witch.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsResourceCache
{
    private static readonly Dictionary<string, UnityEngine.Object?> SingleCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, UnityEngine.Object[]> MultiCache = new(StringComparer.OrdinalIgnoreCase);

    public static T? Load<T>(string path, bool loadFromMod = true)
        where T : UnityEngine.Object
    {
        var key = SingleKey<T>(path, loadFromMod);
        if (SingleCache.TryGetValue(key, out var cached))
        {
            return cached as T;
        }

        try
        {
            var loaded = ResourceLoader.Load<T>(path, loadFromMod);
            SingleCache[key] = loaded;
            return loaded;
        }
        catch (Exception ex)
        {
            AuraSharedLog.DebugOnce("AuraTools", "resource-load:" + key, "[ResourceCache] load failed: " + path + " -> " + ex.Message);
            SingleCache[key] = null;
            return null;
        }
    }

    public static T[] LoadAll<T>(string path)
        where T : UnityEngine.Object
    {
        var key = MultiKey<T>(path);
        if (MultiCache.TryGetValue(key, out var cached))
        {
            return ConvertArray<T>(cached);
        }

        try
        {
            var loaded = ResourceLoader.LoadAll<T>(path) ?? Array.Empty<T>();
            var values = new UnityEngine.Object[loaded.Length];
            Array.Copy(loaded, values, loaded.Length);
            MultiCache[key] = values;
            return loaded;
        }
        catch (Exception ex)
        {
            AuraSharedLog.DebugOnce("AuraTools", "resource-load-all:" + key, "[ResourceCache] load all failed: " + path + " -> " + ex.Message);
            MultiCache[key] = Array.Empty<UnityEngine.Object>();
            return Array.Empty<T>();
        }
    }

    public static void Clear()
    {
        SingleCache.Clear();
        MultiCache.Clear();
    }

    private static T[] ConvertArray<T>(UnityEngine.Object[] source)
        where T : UnityEngine.Object
    {
        if (source.Length == 0)
        {
            return Array.Empty<T>();
        }

        var values = new T[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            values[i] = (T)source[i];
        }

        return values;
    }

    private static string SingleKey<T>(string path, bool loadFromMod)
    {
        return typeof(T).FullName + "|" + loadFromMod + "|" + (path ?? "").Trim();
    }

    private static string MultiKey<T>(string path)
    {
        return typeof(T).FullName + "|all|" + (path ?? "").Trim();
    }
}
