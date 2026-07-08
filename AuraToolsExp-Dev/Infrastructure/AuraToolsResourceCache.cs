using System;
using AuraShared.Core;
using UnityEngine;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsResourceCache
{
    public static T? Load<T>(string path, bool loadFromMod = true)
        where T : UnityEngine.Object
    {
        return AuraSharedResourceCache.Load<T>(
            "AuraTools",
            path,
            loadFromMod,
            warn: message => AuraSharedLog.DebugOnce("AuraTools", "resource-load:" + typeof(T).FullName + ":" + path, message));
    }

    public static T[] LoadAll<T>(string path)
        where T : UnityEngine.Object
    {
        return AuraSharedResourceCache.LoadAll<T>(
            "AuraTools",
            path,
            warn: message => AuraSharedLog.DebugOnce("AuraTools", "resource-load-all:" + typeof(T).FullName + ":" + path, message));
    }

    public static void Clear()
    {
        AuraSharedResourceCache.Clear("AuraTools");
    }
}
