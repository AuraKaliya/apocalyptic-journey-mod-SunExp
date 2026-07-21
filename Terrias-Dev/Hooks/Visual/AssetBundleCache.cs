using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

public static class AssetBundleCache
{
    private static readonly Dictionary<string, AssetBundle?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string[]> AssetNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedMissingAssets = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear(bool unloadAllLoadedObjects = false)
    {
        foreach (var bundle in Cache.Values.Where(bundle => bundle != null).Distinct())
        {
            try
            {
                bundle!.Unload(unloadAllLoadedObjects);
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[VisualBundle] unload failed: " + ex.Message);
            }
        }

        Cache.Clear();
        AssetNames.Clear();
        LoggedMissingAssets.Clear();
    }

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

        var requested = assetPath.Trim();
        try
        {
            var asset = bundle.LoadAsset<T>(requested);
            if (asset != null)
            {
                return asset;
            }

            foreach (var candidate in AssetNameCandidates<T>(bundle, requested))
            {
                asset = bundle.LoadAsset<T>(candidate);
                if (asset != null)
                {
                    return asset;
                }
            }

            LogMissingAsset(bundlePath, requested, bundle, logPrefix);
            return null;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(logPrefix + " bundle asset load failed: " + requested + " (" + ex.Message + ")");
            return null;
        }
    }

    public static AssetBundle? LoadBundle(string bundlePath, string logPrefix)
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
            TerriasLog.Warn(logPrefix + " visual bundle missing: " + resolvedPath);
            return null;
        }

        try
        {
            var bundle = AssetBundle.LoadFromFile(resolvedPath);
            Cache[resolvedPath] = bundle;
            if (bundle == null)
            {
                TerriasLog.Warn(logPrefix + " visual bundle could not be loaded: " + resolvedPath);
            }

            return bundle;
        }
        catch (Exception ex)
        {
            Cache[resolvedPath] = null;
            TerriasLog.Warn(logPrefix + " visual bundle load failed: " + resolvedPath + " (" + ex.Message + ")");
            return null;
        }
    }

    private static IEnumerable<string> AssetNameCandidates<T>(AssetBundle bundle, string requested)
        where T : UnityEngine.Object
    {
        var requestedName = requested.Replace('\\', '/').Trim();
        var extension = typeof(T) == typeof(Material)
            ? ".mat"
            : typeof(T) == typeof(Shader)
                ? ".shader"
                : "";
        var leaf = LeafName(requestedName, extension);
        var names = GetAssetNames(bundle);
        var candidates = new List<string>
        {
            requestedName
        };

        if (extension.Length > 0 && !requestedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(requestedName + extension);
        }

        if (leaf.Length > 0)
        {
            candidates.Add(leaf);
            if (extension.Length > 0)
            {
                candidates.Add(leaf + extension);
                candidates.Add("Assets/Terrias/Visuals/Materials/" + leaf + extension);
                candidates.Add("Assets/Terrias/Visuals/Shaders/" + leaf + extension);
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in names)
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileNameWithoutExtension(name), leaf, StringComparison.OrdinalIgnoreCase)
                    || (extension.Length > 0 && name.EndsWith("/" + leaf + extension, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return name;
                }
            }
        }
    }

    private static string[] GetAssetNames(AssetBundle bundle)
    {
        var key = bundle.name ?? "";
        if (key.Length == 0)
        {
            key = bundle.GetInstanceID().ToString();
        }

        if (AssetNames.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            cached = bundle.GetAllAssetNames();
        }
        catch
        {
            cached = Array.Empty<string>();
        }

        AssetNames[key] = cached;
        return cached;
    }

    private static string LeafName(string requested, string extension)
    {
        var leaf = Path.GetFileName(requested);
        if (extension.Length > 0 && leaf.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            leaf = leaf.Substring(0, leaf.Length - extension.Length);
        }

        return leaf;
    }

    private static void LogMissingAsset(string bundlePath, string requested, AssetBundle bundle, string logPrefix)
    {
        var key = bundlePath + "|" + requested;
        if (!LoggedMissingAssets.Add(key))
        {
            return;
        }

        var names = GetAssetNames(bundle);
        var preview = names.Length == 0
            ? "<empty>"
            : string.Join("|", names.Take(8));
        TerriasLog.Warn(logPrefix + " bundle asset missing: " + requested + "; available=" + preview);
    }
}
