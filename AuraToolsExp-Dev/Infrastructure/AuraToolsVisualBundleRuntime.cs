using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

internal static class AuraToolsVisualBundleRuntime
{
    private static readonly Dictionary<string, AssetBundle?> Bundles = new(StringComparer.OrdinalIgnoreCase);

    public static AssetBundle? LoadBundle(string logicalPath)
    {
        var resolved = ResourceLoader.ResolveModPath((logicalPath ?? "").Trim());
        if (Bundles.TryGetValue(resolved, out var cached)) return cached;
        if (!File.Exists(resolved)) return Bundles[resolved] = null;
        try
        {
            return Bundles[resolved] = AssetBundle.LoadFromFile(resolved);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[VisualBundle] load failed: " + resolved + " -> " + ex.Message);
            return Bundles[resolved] = null;
        }
    }

    public static T? LoadAsset<T>(string logicalPath, string assetName) where T : UnityEngine.Object
    {
        var bundle = LoadBundle(logicalPath);
        if (bundle == null) return null;
        var requested = (assetName ?? "").Trim();
        var asset = bundle.LoadAsset<T>(requested);
        if (asset != null) return asset;
        var leaf = Path.GetFileNameWithoutExtension(requested);
        var path = bundle.GetAllAssetNames().FirstOrDefault(value =>
            string.Equals(Path.GetFileNameWithoutExtension(value), leaf, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(path) ? null : bundle.LoadAsset<T>(path);
    }
}
