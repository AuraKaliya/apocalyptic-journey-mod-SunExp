using System;
using System.Collections.Generic;
using System.IO;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public static class AssetBundleCache
{
    private static readonly Dictionary<string, AssetBundle?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static T? LoadAsset<T>(string bundlePath, string assetPath, string logPrefix)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        var bundle = LoadBundle(bundlePath, logPrefix);
        if (bundle == null)
        {
            return null;
        }

        try
        {
            return bundle.LoadAsset<T>(assetPath.Trim());
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(logPrefix + " bundle asset load failed: " + assetPath + " (" + ex.Message + ")");
            return null;
        }
    }

    private static AssetBundle? LoadBundle(string bundlePath, string logPrefix)
    {
        var resolvedPath = VisualRegistry.ResolveContentPath(bundlePath);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        if (Cache.TryGetValue(resolvedPath, out var cached))
        {
            return cached;
        }

        if (!File.Exists(resolvedPath))
        {
            Cache[resolvedPath] = null;
            SunExpLog.Debug(logPrefix + " visual bundle missing: " + resolvedPath);
            return null;
        }

        try
        {
            var bundle = AssetBundle.LoadFromFile(resolvedPath);
            Cache[resolvedPath] = bundle;
            if (bundle == null)
            {
                SunExpLog.Warn(logPrefix + " visual bundle could not be loaded: " + resolvedPath);
            }

            return bundle;
        }
        catch (Exception ex)
        {
            Cache[resolvedPath] = null;
            SunExpLog.Warn(logPrefix + " visual bundle load failed: " + resolvedPath + " (" + ex.Message + ")");
            return null;
        }
    }
}
